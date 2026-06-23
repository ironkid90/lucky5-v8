# Fix-Antigravity-Webview.ps1
# This script closes Antigravity IDE, clears the corrupt webview/service worker cache, and restarts it.

Write-Host "Closing Antigravity IDE..." -ForegroundColor Yellow

$processes = Get-Process -Name "Antigravity IDE" -ErrorAction SilentlyContinue
if ($processes) {
    Stop-Process -Name "Antigravity IDE" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# Also kill any leftover kilo or language server processes that might hold locks
Stop-Process -Name "kilo" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "language_server_windows_x64" -Force -ErrorAction SilentlyContinue

$appDataPath = "$env:APPDATA\Antigravity IDE"
$targetFolders = @("Service Worker", "Cache", "Code Cache")

foreach ($folder in $targetFolders) {
    $fullPath = Join-Path $appDataPath $folder
    if (Test-Path $fullPath) {
        Write-Host "Clearing folder: $fullPath" -ForegroundColor Cyan
        Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Cache cleared successfully!" -ForegroundColor Green
Write-Host "Restarting Antigravity IDE..." -ForegroundColor Green

Start-Process -FilePath "G:\Antigravity IDE\Antigravity IDE.exe"
Write-Host "Done!" -ForegroundColor Green
