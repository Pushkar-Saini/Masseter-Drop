# Masseter Drop — Uninstaller
[CmdletBinding()]
param()

$ErrorActionPreference = "SilentlyContinue"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "     MASSETER DROP — UNINSTALLER          " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

# 1. Stop process
Write-Host "-> Closing Masseter Drop..." -ForegroundColor Yellow
Get-Process -Name "MasseterDrop" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

# 2. Remove Windows Startup key
Write-Host "-> Removing Windows Startup registration..." -ForegroundColor Green
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "MasseterDrop" -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run" -Name "MasseterDrop" -Force -ErrorAction SilentlyContinue

# 3. Remove Start Menu shortcut
Write-Host "-> Removing Start Menu shortcut..." -ForegroundColor Green
$startMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
Remove-Item -Path (Join-Path $startMenuDir "Masseter Drop.lnk") -Force -ErrorAction SilentlyContinue

# 4. Remove installation folder
$installDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "Programs", "Masseter Drop")
Write-Host "-> Removing program files from $installDir..." -ForegroundColor Green
if (Test-Path $installDir) {
    # If uninstaller was run from inside installDir, we schedule deletion
    Start-Process powershell -ArgumentList "-NoProfile -Command `"Start-Sleep -Seconds 1; Remove-Item -Path '$installDir' -Recurse -Force -ErrorAction SilentlyContinue`"" -WindowStyle Hidden
}

Write-Host ""
Write-Host "Masseter Drop has been completely uninstalled." -ForegroundColor Green
