@echo off
setlocal
set "FARMMACRO_LAUNCH_ROOT=%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "& { $ErrorActionPreference = 'Stop'; try { $launchRoot = [IO.Path]::GetFullPath($env:FARMMACRO_LAUNCH_ROOT); if (@(Get-Process -Name 'FarmMacro','FarmMacro.next' -ErrorAction SilentlyContinue).Count -gt 0) { throw 'Close every Farm Macro window and tray copy first, then use this launcher again.' }; $appPath = Join-Path $launchRoot 'FarmMacro.exe'; if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw 'FarmMacro.exe is missing. Run Start Farm Macro.cmd to build it first.' }; Start-Process -FilePath $appPath -WorkingDirectory $launchRoot -Verb RunAs -ErrorAction Stop } catch { Write-Host ('Administrator launch canceled or failed: ' + $_.Exception.Message); exit 1 } }"
if errorlevel 1 (
    pause
    exit /b 1
)
