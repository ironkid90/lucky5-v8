<#
.SYNOPSIS
  Lucky5 v8 - web-native launcher.
  Starts the .NET API and opens the built-in cabinet in a browser by default.

.PARAMETER Headless
  API only - do not open a browser.

.PARAMETER NoBrowser
  Start the API without opening the cabinet automatically.

.PARAMETER Port
  API port (default: 5051).

.EXAMPLE
  .\dev.ps1

.EXAMPLE
  .\dev.ps1 -Headless -Port 8080

Admin login: admin / admin123
Test login:  tester / password
#>
param(
    [switch]$Headless,
    [switch]$NoBrowser,
    [int]$Port = 5051
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

function Assert-Command([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        Write-Error "'$name' not found in PATH. Install it and re-run."
        exit 1
    }
}

function Wait-Port([int]$port, [System.Diagnostics.Process]$process, [int]$timeoutSec = 90) {
    Write-Host "  Waiting for localhost:$port ..." -NoNewline
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "Lucky5.Api exited before becoming ready (exit code $($process.ExitCode)). See server output above."
        }
        try {
            $health = Invoke-WebRequest -Uri "http://localhost:$port/health/live" -TimeoutSec 2 -UseBasicParsing
            if ($health.StatusCode -ne 200) { throw "Health check failed" }
            Write-Host " ready." -ForegroundColor Green
            return $true
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    Write-Warning " timed out."
    return $false
}

Assert-Command "dotnet"

if ($Headless -and $NoBrowser) {
    $NoBrowser = $true
}

$launchBrowser = -not $Headless -and -not $NoBrowser

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Lucky5 v8 - Web Cabinet" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  URL:  http://localhost:$Port"
Write-Host "  Admin: admin / admin123"
Write-Host "  Test:  tester / password"
Write-Host ""

$env:PORT = "$Port"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$apiProject = "$root\server\src\Lucky5.Api\Lucky5.Api.csproj"

Write-Host "[1/2] Starting Lucky5.Api on http://localhost:$Port ..." -ForegroundColor Yellow
Write-Host "  Building API before starting the readiness timeout..."
& dotnet build $apiProject
if ($LASTEXITCODE -ne 0) {
    throw "API build failed (exit code $LASTEXITCODE). Server was not started."
}
$apiProcess = Start-Process -PassThru -NoNewWindow `
    -FilePath "dotnet" `
    -ArgumentList "run", "--no-build", "--project", "`"$apiProject`"", "--no-launch-profile" `
    -WorkingDirectory "$root\server\src\Lucky5.Api"

Write-Host "  API PID: $($apiProcess.Id)"
$ready = Wait-Port $Port $apiProcess 90

if (-not $ready) {
    throw "API did not become healthy at http://localhost:$Port/health/live. Browser was not opened. API PID: $($apiProcess.Id)."
}

if ($launchBrowser) {
    Write-Host "[2/2] Opening web cabinet..." -ForegroundColor Yellow
    Start-Process "http://localhost:$Port"
    Write-Host "  Cabinet opened in browser." -ForegroundColor Green
} else {
    Write-Host "[2/2] Server running. Press Ctrl+C to stop." -ForegroundColor Green
}

if ($apiProcess) {
    Wait-Process -Id $apiProcess.Id
}
