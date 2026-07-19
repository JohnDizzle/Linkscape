@echo off
setlocal

set SCRIPT_DIR=%~dp0
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%search-reactor.ps1" %*

exit /b %ERRORLEVEL%
