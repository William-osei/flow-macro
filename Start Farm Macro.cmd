@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0FarmMacro.exe" (
    "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -File "%~dp0build.ps1"
    if errorlevel 1 (
        pause
        exit /b 1
    )
)
start "" "%~dp0FarmMacro.exe"
