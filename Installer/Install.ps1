# Masseter Drop — Automatic Windows Installer
# Installs per-user without requiring Administrator privileges

[CmdletBinding()]
param(
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "      MASSETER DROP — INSTALLER           " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

$installDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "Programs", "Masseter Drop")
$sourceDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\publish"))

if (-not (Test-Path (Join-Path $sourceDir "MasseterDrop.exe"))) {
    # If publish folder doesn't exist, check debug output
    $debugDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\bin\Release\net8.0-windows\win-x64"))
    if (Test-Path (Join-Path $debugDir "MasseterDrop.exe")) {
        $sourceDir = $debugDir
    } else {
        $debugDir2 = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\bin\Debug\net8.0-windows"))
        if (Test-Path (Join-Path $debugDir2 "MasseterDrop.exe")) {
            $sourceDir = $debugDir2
        } else {
            Write-Error "Could not find MasseterDrop.exe build outputs. Please run 'dotnet publish -c Release' first."
            exit 1
        }
    }
}

Write-Host "-> Source directory: $sourceDir" -ForegroundColor Gray
Write-Host "-> Target directory: $installDir" -ForegroundColor Gray

# 1. Close any running instance of Masseter Drop
Write-Host "-> Checking for existing Masseter Drop processes..." -ForegroundColor Yellow
$running = Get-Process -Name "MasseterDrop" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "   Closing running Masseter Drop instance..." -ForegroundColor Yellow
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# 2. Create destination directory
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
}

# 3. Copy application files
Write-Host "-> Installing application files..." -ForegroundColor Green
Copy-Item -Path "$sourceDir\*" -Destination $installDir -Recurse -Force

# Ensure Assets folder exists
if (Test-Path "$sourceDir\Assets") {
    Copy-Item -Path "$sourceDir\Assets" -Destination $installDir -Recurse -Force
}

$exePath = Join-Path $installDir "MasseterDrop.exe"
$icoPath = Join-Path $installDir "Assets\Icons\app.ico"

# 4. Create Start Menu Shortcut
Write-Host "-> Creating Start Menu shortcut..." -ForegroundColor Green
$startMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
$shortcutPath = Join-Path $startMenuDir "Masseter Drop.lnk"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = "Masseter Drop — Your downloads. One click away."
if (Test-Path $icoPath) {
    $shortcut.IconLocation = "$icoPath,0"
}
$shortcut.Save()

# 5. Register in Windows Startup (HKCU\Software\Microsoft\Windows\CurrentVersion\Run)
Write-Host "-> Configuring automatic Windows Startup..." -ForegroundColor Green
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$startupCommand = "`"$exePath`" --minimized"
Set-ItemProperty -Path $runKey -Name "MasseterDrop" -Value $startupCommand -Type String -Force

# Ensure Task Manager doesn't flag it as disabled
$approvedKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
if (Test-Path $approvedKey) {
    $enabledBytes = [byte[]](0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00)
    Set-ItemProperty -Path $approvedKey -Name "MasseterDrop" -Value $enabledBytes -Type Binary -Force -ErrorAction SilentlyContinue
}

# 6. Copy Uninstaller scripts to install directory
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall.ps1") -Destination $installDir -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall.bat") -Destination $installDir -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "  MASSETER DROP INSTALLED SUCCESSFULLY!   " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Installed Location: $installDir" -ForegroundColor White
Write-Host "Windows Startup:    REGISTERED (HKCU Run -> `"$exePath`" --minimized)" -ForegroundColor White
Write-Host "Start Menu:         Created 'Masseter Drop' shortcut" -ForegroundColor White
Write-Host ""

if (-not $NoLaunch) {
    Write-Host "-> Starting Masseter Drop in system tray..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath -WorkingDirectory $installDir -ArgumentList "--minimized"
    Write-Host "   Masseter Drop is now active in your system tray!" -ForegroundColor Green
}
