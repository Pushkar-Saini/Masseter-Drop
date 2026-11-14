@echo off
setlocal
title Installing Masseter Drop...
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Installation encountered an error.
    pause
) else (
    timeout /t 3 /nobreak >nul
)
