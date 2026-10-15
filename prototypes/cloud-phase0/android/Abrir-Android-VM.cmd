@echo off
powershell.exe -NoProfile -File "%~dp0android-vm.ps1" -Action Show
if errorlevel 1 pause
