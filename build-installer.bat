@echo off
setlocal
cd /d "%~dp0"
call "%~dp0build.bat" %*
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\package.ps1" %*
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish-companion.ps1" %*
if errorlevel 1 exit /b %errorlevel%
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\package-docs.ps1"
exit /b %errorlevel%
