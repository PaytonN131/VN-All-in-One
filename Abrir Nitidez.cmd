@echo off
cd /d "%~dp0"
if not exist "NitidezVN.exe" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if exist "NitidezVN.exe" start "" "%~dp0NitidezVN.exe"
