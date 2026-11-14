@echo off
setlocal
title Uninstalling Masseter Drop...
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
pause
