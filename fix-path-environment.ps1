[CmdletBinding()]
param(
    [ValidateSet('Audit', 'Repair', 'Verify')]
    [string]$Mode = 'Audit',

    [string]$BackupRoot = (Join-Path $env:USERPROFILE 'PathBackups'),

    [switch]$NoElevation
)

<#
.SYNOPSIS
    Safely audits, repairs, and verifies Windows User and System PATH values.

.DESCRIPTION
    This script never uses setx. In Repair mode it:
      * inventories the raw registry PATH values and their value kinds;
      * exports both environment registry keys before mutation;
      * preserves valid existing directories and de-duplicates them
        case-insensitively by expanded path;
      * restores verified Windows defaults and verified developer-tool paths;
      * keeps machine-wide and per-user entries in their appropriate scopes;
      * writes PATH as REG_EXPAND_SZ through Microsoft.Win32 registry APIs;
      * rolls both PATH values back in-process if either write fails;
      * broadcasts WM_SETTINGCHANGE and verifies persisted registry values;
      * tests command resolution from newly spawned cmd.exe and PowerShell shells.

    Audit is the default mode and makes no registry changes. Repair automatically
    requests elevation when necessary. Backups contain environment registry keys,
    which can include unrelated environment values; keep the backup directory
    private and do not publish its .reg files.
#>

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:UserSubKey = 'Environment'
$script:MachineSubKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
$script:RegExe = Join-Path $env:SystemRoot 'System32\reg.exe'
$script:CmdExe = Join-Path $env:SystemRoot 'System32\cmd.exe'
$script:PowerShellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$script:WhereExe = Join-Path $env:SystemRoot 'System32\where.exe'

function Write-Status {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host $Message -ForegroundColor $Color
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-RegistryDescriptor {
    param([ValidateSet('User', 'System')][string]$Scope)

    if ($Scope -eq 'User') {
        return @{
            Hive = [Microsoft.Win32.RegistryHive]::CurrentUser
            SubKey = $script:UserSubKey
            ExportKey = 'HKCU\Environment'
        }
    }

    return @{
        Hive = [Microsoft.Win32.RegistryHive]::LocalMachine
        SubKey = $script:MachineSubKey
        ExportKey = 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
    }
}

function Get-RawPathState {
    param([ValidateSet('User', 'System')][string]$Scope)

    $descriptor = Get-RegistryDescriptor -Scope $Scope
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        $descriptor.Hive,
        [Microsoft.Win32.RegistryView]::Default
    )

    try {
        $key = $baseKey.OpenSubKey($descriptor.SubKey, $false)
        if ($null -eq $key) {
            throw "Registry key not found for $Scope PATH."
        }

        try {
            $raw = $key.GetValue(
                'Path',
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames
            )
            $kind = if ($null -eq $raw) {
                'Missing'
            }
            else {
                $key.GetValueKind('Path').ToString()
            }
            $text = if ($null -eq $raw) { '' } else { [string]$raw }

            return [pscustomobject]@{
                Scope = $Scope
                Raw = $text
                Type = $kind
                Length = $text.Length
                Entries = @($text -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
            }
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

function Set-RawPathState {
    param(
        [ValidateSet('User', 'System')][string]$Scope,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value,
        [Microsoft.Win32.RegistryValueKind]$Kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
    )

    $descriptor = Get-RegistryDescriptor -Scope $Scope
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        $descriptor.Hive,
        [Microsoft.Win32.RegistryView]::Default
    )

    try {
        $key = $baseKey.OpenSubKey($descriptor.SubKey, $true)
        if ($null -eq $key) {
            throw "Could not open the $Scope environment key for writing."
        }

        try {
            $key.SetValue('Path', $Value, $Kind)
            $key.Flush()
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

function Convert-TypeNameToRegistryKind {
    param([string]$TypeName)

    if ($TypeName -eq 'String') {
        return [Microsoft.Win32.RegistryValueKind]::String
    }

    return [Microsoft.Win32.RegistryValueKind]::ExpandString
}

function Expand-PathEntry {
    param([Parameter(Mandatory = $true)][string]$Entry)

    return [Environment]::ExpandEnvironmentVariables($Entry)
}

function Normalize-PathKey {
    param([Parameter(Mandatory = $true)][string]$Entry)

    $expanded = (Expand-PathEntry -Entry $Entry).Trim().TrimEnd('\', '/')
    try {
        $expanded = [IO.Path]::GetFullPath($expanded).TrimEnd('\', '/')
    }
    catch {
        # Keep the expanded representation. Invalid entries are rejected separately.
    }

    return $expanded.ToUpperInvariant()
}

function Test-PathEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Entry,
        [switch]$AllowUserVariable
    )

    $candidate = $Entry.Trim()
    if ([string]::IsNullOrWhiteSpace($candidate)) { return $false }
    if ($candidate -match '(?i)%PATH%') { return $false }
    if ($candidate.Contains('"')) { return $false }
    if (-not $AllowUserVariable -and $candidate -match '(?i)%(USERPROFILE|LOCALAPPDATA|APPDATA)%') {
        return $false
    }

    $expanded = Expand-PathEntry -Entry $candidate
    if ($expanded -match '%[^%]+%') { return $false }
    return Test-Path -LiteralPath $expanded -PathType Container
}

function Add-UniquePathEntry {
    param(
        [Parameter(Mandatory = $true)][ref]$List,
        [Parameter(Mandatory = $true)][ref]$Seen,
        [Parameter(Mandatory = $true)][string]$Entry,
        [switch]$AllowUserVariable
    )

    $trimmed = $Entry.Trim().TrimEnd(';').Trim()
    if (-not (Test-PathEntry -Entry $trimmed -AllowUserVariable:$AllowUserVariable)) {
        return $false
    }

    if ($null -eq $Seen.Value -or -not ($Seen.Value -is [System.Collections.Generic.HashSet[string]])) {
        throw 'Internal error: PATH de-duplication set was not a HashSet.'
    }

    $key = Normalize-PathKey -Entry $trimmed
    if (-not $Seen.Value.Add($key)) {
        return $false
    }

    if ($null -eq $List.Value -or -not ($List.Value -is [System.Collections.ArrayList])) {
        throw 'Internal error: PATH accumulator was not an ArrayList.'
    }

    [void]$List.Value.Add($trimmed)
    return $true
}

function Test-AnyFile {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return $false }
    foreach ($name in $Names) {
        if (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf) {
            return $true
        }
    }
    return $false
}

function Get-VerifiedToolCandidates {
    $machine = New-Object System.Collections.ArrayList
    $user = New-Object System.Collections.ArrayList

    $machineDefinitions = @(
        @{ Path = '%SystemRoot%\System32'; Names = @('cmd.exe', 'where.exe') },
        @{ Path = '%SystemRoot%'; Names = @('explorer.exe') },
        @{ Path = '%SystemRoot%\System32\Wbem'; Names = @('wmic.exe', 'mofcomp.exe') },
        @{ Path = '%SystemRoot%\System32\WindowsPowerShell\v1.0\'; Names = @('powershell.exe') },
        @{ Path = '%SystemRoot%\System32\OpenSSH\'; Names = @('ssh.exe', 'ssh-keygen.exe', 'sshd.exe') },
        @{ Path = 'C:\Python314'; Names = @('python.exe') },
        @{ Path = 'C:\Python314\Scripts'; Names = @('pip.exe', 'pip3.exe') },
        @{ Path = 'C:\Program Files\nodejs'; Names = @('node.exe', 'npm.cmd') },
        @{ Path = 'C:\Program Files\Git\cmd'; Names = @('git.exe') },
        @{ Path = 'C:\Program Files\PowerShell\7'; Names = @('pwsh.exe') },
        @{ Path = 'C:\Program Files\dotnet'; Names = @('dotnet.exe') },
        @{ Path = 'C:\Program Files\Go\bin'; Names = @('go.exe') },
        @{ Path = 'C:\Program Files\Docker\Docker\resources\bin'; Names = @('docker.exe') },
        @{ Path = 'C:\Program Files\Java\jdk-26.0.1\bin'; Names = @('java.exe', 'javac.exe') },
        @{ Path = 'C:\Program Files (x86)\Google\Cloud SDK\google-cloud-sdk\bin'; Names = @('gcloud.cmd') },
        @{ Path = 'C:\Program Files (x86)\Microsoft SDKs\Azure\CLI2\wbin'; Names = @('az.cmd') }
    )

    $userDefinitions = @(
        @{ Path = '%APPDATA%\npm'; Names = @('npm.cmd', 'npx.cmd', 'claude.cmd') },
        @{ Path = '%LOCALAPPDATA%\Programs\Microsoft VS Code\bin'; Names = @('code.cmd') },
        @{ Path = '%LOCALAPPDATA%\hermes\.venv\Scripts'; Names = @('hermes.exe') },
        @{ Path = '%USERPROFILE%\.cargo\bin'; Names = @('cargo.exe', 'rustc.exe') },
        @{ Path = '%USERPROFILE%\.dotnet\tools'; Names = @('dotnet-ef.exe') },
        @{ Path = '%LOCALAPPDATA%\Microsoft\WindowsApps'; Names = @('py.exe', 'python.exe') },
        @{ Path = '%LOCALAPPDATA%\hermes\bin'; Names = @('uv.exe', 'uvx.exe') }
    )

    foreach ($definition in $machineDefinitions) {
        $expanded = Expand-PathEntry -Entry $definition.Path
        if (Test-AnyFile -Directory $expanded -Names $definition.Names) {
            [void]$machine.Add($definition.Path)
        }
    }

    foreach ($definition in $userDefinitions) {
        $expanded = Expand-PathEntry -Entry $definition.Path
        if (Test-AnyFile -Directory $expanded -Names $definition.Names) {
            [void]$user.Add($definition.Path)
        }
    }

    return [pscustomobject]@{
        Machine = @($machine.ToArray())
        User = @($user.ToArray())
    }
}

function Get-ProposedPaths {
    param(
        [Parameter(Mandatory = $true)]$UserBefore,
        [Parameter(Mandatory = $true)]$SystemBefore
    )

    $verified = Get-VerifiedToolCandidates
    $machine = New-Object System.Collections.ArrayList
    $user = New-Object System.Collections.ArrayList
    $machineSeen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $userSeen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $verified.Machine) {
        Add-UniquePathEntry -List ([ref]$machine) -Seen ([ref]$machineSeen) -Entry $entry | Out-Null
    }

    foreach ($entry in $verified.User) {
        Add-UniquePathEntry -List ([ref]$user) -Seen ([ref]$userSeen) -Entry $entry -AllowUserVariable | Out-Null
    }

    # Preserve valid existing machine entries. Migrate user-variable entries out of
    # System PATH because they expand incorrectly for services and other users.
    foreach ($entry in @($SystemBefore.Raw -split ';')) {
        $trimmed = $entry.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if ($trimmed -match '(?i)%(USERPROFILE|LOCALAPPDATA|APPDATA)%') {
            Add-UniquePathEntry -List ([ref]$user) -Seen ([ref]$userSeen) -Entry $trimmed -AllowUserVariable | Out-Null
        }
        else {
            Add-UniquePathEntry -List ([ref]$machine) -Seen ([ref]$machineSeen) -Entry $trimmed | Out-Null
        }
    }

    foreach ($entry in @($UserBefore.Raw -split ';')) {
        $trimmed = $entry.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        Add-UniquePathEntry -List ([ref]$user) -Seen ([ref]$userSeen) -Entry $trimmed -AllowUserVariable | Out-Null
    }

    $machineRaw = @($machine) -join ';'
    $userRaw = @($user) -join ';'

    if ($machineRaw.Length -gt 8191 -or $userRaw.Length -gt 8191) {
        throw "Proposed PATH exceeds the conservative 8191-character command-shell limit."
    }

    return [pscustomobject]@{
        MachineEntries = @($machine.ToArray())
        UserEntries = @($user.ToArray())
        MachineRaw = $machineRaw
        UserRaw = $userRaw
    }
}

function New-PathBackup {
    param(
        [Parameter(Mandatory = $true)]$UserState,
        [Parameter(Mandatory = $true)]$SystemState
    )

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backupDirectory = Join-Path $BackupRoot "path-repair-$timestamp"
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

    & $script:RegExe export 'HKCU\Environment' (Join-Path $backupDirectory 'HKCU-Environment.reg') /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to export HKCU\Environment.' }
    & $script:RegExe export 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' (Join-Path $backupDirectory 'HKLM-Environment.reg') /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to export the System environment key.' }

    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'User-PATH-before.txt'), $UserState.Raw, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'System-PATH-before.txt'), $SystemState.Raw, $utf8NoBom)

    @($UserState, $SystemState) |
        Select-Object Scope, Type, Length, Entries |
        ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $backupDirectory 'PATH-before-metadata.json') -Encoding UTF8

    $restore = @"
# Run from an elevated Windows PowerShell session to restore both environment keys.
& '$script:RegExe' import '$(Join-Path $backupDirectory 'HKCU-Environment.reg')'
if (`$LASTEXITCODE -ne 0) { throw 'HKCU restore failed.' }
& '$script:RegExe' import '$(Join-Path $backupDirectory 'HKLM-Environment.reg')'
if (`$LASTEXITCODE -ne 0) { throw 'HKLM restore failed.' }
Write-Host 'Registry environment keys restored. Sign out and back in before relying on PATH.'
"@
    [IO.File]::WriteAllText((Join-Path $backupDirectory 'restore-environment.ps1'), $restore, $utf8NoBom)

    return $backupDirectory
}

function Broadcast-EnvironmentChange {
    if (-not ('PathRepair.NativeMethods' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace PathRepair {
    public static class NativeMethods {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);
    }
}
'@
    }

    $result = [UIntPtr]::Zero
    [void][PathRepair.NativeMethods]::SendMessageTimeout(
        [IntPtr]0xFFFF,
        0x001A,
        [UIntPtr]::Zero,
        'Environment',
        0x0002,
        5000,
        [ref]$result
    )
}

function Get-CombinedExpandedPath {
    $machine = [Environment]::ExpandEnvironmentVariables((Get-RawPathState -Scope System).Raw)
    $user = [Environment]::ExpandEnvironmentVariables((Get-RawPathState -Scope User).Raw)
    return (($machine.TrimEnd(';') + ';' + $user.Trim(';')).Trim(';'))
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Arguments,
        [int]$TimeoutSeconds = 20
    )

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        return [pscustomobject]@{ ExitCode = -1; Output = 'TIMEOUT' }
    }

    $output = (($process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()).Trim())
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output }
}

function Test-ToolCommands {
    $tools = @(
        @{ Name = 'python'; Args = '--version'; Required = $true },
        @{ Name = 'py'; Args = '--version'; Required = $false },
        @{ Name = 'node'; Args = '--version'; Required = $true },
        @{ Name = 'npm'; Args = '--version'; Required = $true },
        @{ Name = 'npx'; Args = '--version'; Required = $true },
        @{ Name = 'code'; Args = '--version'; Required = $true },
        @{ Name = 'git'; Args = '--version'; Required = $true },
        @{ Name = 'dotnet'; Args = '--version'; Required = $true },
        @{ Name = 'pwsh'; Args = '--version'; Required = $true },
        @{ Name = 'hermes'; Args = '--version'; Required = $true },
        @{ Name = 'go'; Args = 'version'; Required = $false },
        @{ Name = 'docker'; Args = '--version'; Required = $false },
        @{ Name = 'cargo'; Args = '--version'; Required = $false }
    )

    $originalProcessPath = $env:Path
    $results = New-Object System.Collections.ArrayList
    try {
        # A child normally inherits its parent's stale PATH. Reconstruct the process
        # PATH from the just-persisted registry values before launching fresh shells.
        $env:Path = Get-CombinedExpandedPath

        foreach ($tool in $tools) {
            $whereResult = Invoke-CapturedProcess -FilePath $script:WhereExe -Arguments $tool.Name -TimeoutSeconds 10
            if ($whereResult.ExitCode -eq 0) {
                $versionCommand = "`"$($tool.Name)`" $($tool.Args)"
                $versionResult = Invoke-CapturedProcess -FilePath $script:CmdExe -Arguments "/d /c $versionCommand" -TimeoutSeconds 20
            }
            else {
                $versionResult = [pscustomobject]@{ ExitCode = 9009; Output = 'NOT RESOLVED' }
            }

            $firstVersionLine = @($versionResult.Output -split "`r?`n" | Where-Object { $_ }) | Select-Object -First 1
            [void]$results.Add([pscustomobject]@{
                Tool = $tool.Name
                Required = $tool.Required
                Resolved = ($whereResult.ExitCode -eq 0)
                Locations = $whereResult.Output
                VersionExitCode = $versionResult.ExitCode
                Version = $firstVersionLine
            })
        }

        $powerShellProbe = Invoke-CapturedProcess -FilePath $script:PowerShellExe -Arguments '-NoLogo -NoProfile -Command "(Get-Command python,node,npm,code,git,dotnet,pwsh,hermes -ErrorAction SilentlyContinue).Source"' -TimeoutSeconds 20
        return [pscustomobject]@{
            Tools = @($results.ToArray())
            FreshPowerShellExitCode = $powerShellProbe.ExitCode
            FreshPowerShellResolution = $powerShellProbe.Output
        }
    }
    finally {
        $env:Path = $originalProcessPath
    }
}

function Show-State {
    param(
        [Parameter(Mandatory = $true)]$UserState,
        [Parameter(Mandatory = $true)]$SystemState
    )

    Write-Host ("User PATH:   type={0}, entries={1}, length={2}" -f $UserState.Type, $UserState.Entries, $UserState.Length)
    Write-Host ("System PATH: type={0}, entries={1}, length={2}" -f $SystemState.Type, $SystemState.Entries, $SystemState.Length)
}

if ($Mode -eq 'Repair' -and -not (Test-IsAdministrator)) {
    if ($NoElevation) {
        throw 'Repair mode requires an elevated PowerShell process.'
    }

    Write-Status 'Repair requires elevation. Requesting a standard UAC prompt...' Yellow
    $argumentList = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-Mode', 'Repair',
        '-BackupRoot', ('"{0}"' -f $BackupRoot),
        '-NoElevation'
    )
    $elevated = Start-Process -FilePath $script:PowerShellExe -ArgumentList $argumentList -Verb RunAs -Wait -PassThru
    exit $elevated.ExitCode
}

$userBefore = Get-RawPathState -Scope User
$systemBefore = Get-RawPathState -Scope System
Write-Status "PATH environment $Mode" Cyan
Show-State -UserState $userBefore -SystemState $systemBefore

$proposal = Get-ProposedPaths -UserBefore $userBefore -SystemBefore $systemBefore
Write-Host ("Proposed User PATH:   entries={0}, length={1}" -f @($proposal.UserEntries).Count, $proposal.UserRaw.Length)
Write-Host ("Proposed System PATH: entries={0}, length={1}" -f @($proposal.MachineEntries).Count, $proposal.MachineRaw.Length)

if ($Mode -eq 'Audit') {
    Write-Status 'Audit only: no registry values were changed.' Green
    Write-Host 'Proposed System PATH entries:'
    $proposal.MachineEntries | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Proposed User PATH entries:'
    $proposal.UserEntries | ForEach-Object { Write-Host "  $_" }
    exit 0
}

if ($Mode -eq 'Verify') {
    $verification = Test-ToolCommands
    $verification.Tools | Format-Table Tool, Required, Resolved, VersionExitCode, Version -AutoSize
    Write-Host 'Fresh PowerShell resolution:'
    Write-Host $verification.FreshPowerShellResolution
    if (@($verification.Tools | Where-Object { $_.Required -and (-not $_.Resolved -or $_.VersionExitCode -ne 0) }).Count -gt 0) {
        exit 2
    }
    exit 0
}

$backupDirectory = New-PathBackup -UserState $userBefore -SystemState $systemBefore
Write-Status "Backup created: $backupDirectory" Green

try {
    Set-RawPathState -Scope System -Value $proposal.MachineRaw -Kind ExpandString
    Set-RawPathState -Scope User -Value $proposal.UserRaw -Kind ExpandString
}
catch {
    Write-Status 'PATH write failed; attempting immediate rollback of both values.' Red
    try {
        Set-RawPathState -Scope System -Value $systemBefore.Raw -Kind (Convert-TypeNameToRegistryKind $systemBefore.Type)
        Set-RawPathState -Scope User -Value $userBefore.Raw -Kind (Convert-TypeNameToRegistryKind $userBefore.Type)
        Write-Status 'Rollback completed.' Yellow
    }
    catch {
        Write-Status "Rollback failed. Use restore-environment.ps1 in $backupDirectory from an elevated shell." Red
    }
    throw
}

Broadcast-EnvironmentChange
Start-Sleep -Seconds 2
$userAfter = Get-RawPathState -Scope User
$systemAfter = Get-RawPathState -Scope System

if ($userAfter.Raw -cne $proposal.UserRaw -or $systemAfter.Raw -cne $proposal.MachineRaw) {
    throw 'Registry persistence verification failed: a PATH value changed after writing.'
}
if ($userAfter.Type -ne 'ExpandString' -or $systemAfter.Type -ne 'ExpandString') {
    throw 'Registry type verification failed: both PATH values must be REG_EXPAND_SZ.'
}

$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $backupDirectory 'User-PATH-after.txt'), $userAfter.Raw, $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $backupDirectory 'System-PATH-after.txt'), $systemAfter.Raw, $utf8NoBom)

$verification = Test-ToolCommands
$report = [pscustomobject]@{
    Timestamp = (Get-Date).ToString('o')
    BackupDirectory = $backupDirectory
    Before = @($userBefore, $systemBefore | Select-Object Scope, Type, Length, Entries)
    After = @($userAfter, $systemAfter | Select-Object Scope, Type, Length, Entries)
    UserPathEntries = $proposal.UserEntries
    SystemPathEntries = $proposal.MachineEntries
    ToolVerification = $verification.Tools
    FreshPowerShellExitCode = $verification.FreshPowerShellExitCode
    FreshPowerShellResolution = $verification.FreshPowerShellResolution
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backupDirectory 'repair-report.json') -Encoding UTF8

Write-Status 'Registry persistence verified and WM_SETTINGCHANGE broadcast.' Green
Show-State -UserState $userAfter -SystemState $systemAfter
$verification.Tools | Format-Table Tool, Required, Resolved, VersionExitCode, Version -AutoSize
Write-Host 'Fresh PowerShell resolution:'
Write-Host $verification.FreshPowerShellResolution
Write-Status "Detailed report: $(Join-Path $backupDirectory 'repair-report.json')" Cyan

$failures = @($verification.Tools | Where-Object { $_.Required -and (-not $_.Resolved -or $_.VersionExitCode -ne 0) })
if ($failures.Count -gt 0) {
    Write-Status ("Required verification failures: " + (($failures | Select-Object -ExpandProperty Tool) -join ', ')) Red
    exit 2
}

Write-Status 'PATH repair and required tool verification completed successfully.' Green
exit 0
