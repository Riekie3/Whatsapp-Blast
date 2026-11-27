@echo off
cd /d "%~dp0"
node send.js --check
echo.
if not defined FROM_APP pause
