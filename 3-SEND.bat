@echo off
cd /d "%~dp0"
node send.js
echo.
if not defined FROM_APP pause
