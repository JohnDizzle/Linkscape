@echo off
setlocal

set "PROJECT=%~dp0src\LinkScape\LinkScape.csproj"

dotnet run --project "%PROJECT%" -c Debug
exit /b %ERRORLEVEL%
