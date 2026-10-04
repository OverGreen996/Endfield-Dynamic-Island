@echo off
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0Query-GeminiUsage.ps1"
pause
