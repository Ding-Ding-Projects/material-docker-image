@echo off
setlocal
cd /d "%~dp0"
call "%~dp0download-dependencies.bat" --silent
if errorlevel 1 exit /b %errorlevel%
set /p BUILD_NODE=<"%~dp0.tools\node-path.txt"
"%BUILD_NODE%" "%~dp0scripts\check-vocabulary.mjs"
if errorlevel 1 exit /b %errorlevel%
if /i "%~1"=="-Tests" goto tests
if /i "%~1"=="-DockerSmoke" goto smoke
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\publish.ps1" %*
exit /b %errorlevel%
:tests
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\test.ps1"
exit /b %errorlevel%
:smoke
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build\test.ps1" -DockerSmoke
exit /b %errorlevel%

