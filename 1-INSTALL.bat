@echo off
cd /d "%~dp0"
set PUPPETEER_SKIP_DOWNLOAD=true
call npm install
echo.
echo Done. Now run 2-CHECK-LIST.bat
if not defined FROM_APP pause
