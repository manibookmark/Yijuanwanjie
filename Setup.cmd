@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-Project.ps1" -OpenEditor
if errorlevel 1 (
    pause
    exit /b 1
)
