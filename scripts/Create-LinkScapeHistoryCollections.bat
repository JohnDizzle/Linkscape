@echo off
setlocal

where pwsh >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required to run this script because LinkScape uses .NET 10 SQLite assemblies.
  echo Install PowerShell 7, then run this file again.
  exit /b 1
)

pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0Create-LinkScapeHistoryCollections.ps1" %*
