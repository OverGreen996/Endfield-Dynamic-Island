@echo off
setlocal
title Gemini API Key
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0Set-GeminiKey.ps1"
if errorlevel 1 (
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File "%~dp0Restart-GeminiHub.ps1"
pause
