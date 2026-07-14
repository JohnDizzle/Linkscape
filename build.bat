@echo off
setlocal

set "PROJECT=%~dp0src\LinkScape\LinkScape.csproj"

dotnet build "%PROJECT%" -c Debug
exit /b %ERRORLEVEL%
