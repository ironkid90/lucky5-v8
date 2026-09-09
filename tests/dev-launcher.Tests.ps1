$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../dev.ps1'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Wait-Port' }, $true)
Invoke-Expression $function.Extent.Text
$script:calls = 0
function Invoke-WebRequest {
    param($Uri, $TimeoutSec, [switch]$UseBasicParsing)
    if ($Uri -ne 'http://localhost:5051/health/live') { throw 'Wrong health URL' }
    $script:calls++
    if ($script:calls -eq 1) { throw 'Connection refused' }
    return @{ StatusCode = 200 }
}
function Start-Sleep { param($Milliseconds) }
$live = [System.Diagnostics.Process]::GetCurrentProcess()
if (-not (Wait-Port 5051 $live 5)) { throw 'Expected readiness after transient refusal' }
if ($script:calls -ne 2) { throw 'Expected health retry' }
if (Wait-Port 5051 $live 0) { throw 'Timeout must not return ready' }
$child = Start-Process -FilePath "$PSHOME/powershell.exe" -ArgumentList '-NoProfile', '-Command', 'exit 7' -PassThru -WindowStyle Hidden
$child.WaitForExit()
$callsBeforeExitCheck = $script:calls
$exitDetected = $false
try {
    Wait-Port 5051 $child 5 | Out-Null
} catch {
    if ($_.Exception.Message -notlike '*exited before becoming ready (exit code 7)*') { throw }
    $exitDetected = $true
}
if (-not $exitDetected) { throw 'An exited API process must fail immediately' }
if ($script:calls -ne $callsBeforeExitCheck) { throw 'Must not accept another listener after API exit' }
$child.Dispose()
$build = $ast.Extent.Text.IndexOf('& dotnet build $apiProject')
$start = $ast.Extent.Text.IndexOf('$apiProcess = Start-Process')
$ready = $ast.Extent.Text.IndexOf('$ready = Wait-Port')
$browser = $ast.Extent.Text.IndexOf('Start-Process "http://localhost:$Port"')
if (-not ($build -lt $start -and $start -lt $ready -and $ready -lt $browser)) { throw 'Incorrect startup ordering' }
if (-not $ast.Extent.Text.Contains('Browser was not opened.')) { throw 'Missing readiness failure guard' }
Write-Host 'PASS: parser, health retry, timeout, early process exit, build-before-start, readiness-before-browser'
