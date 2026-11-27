@echo off
rem Builds "WhatsApp Blast.exe" with the C# compiler that ships with Windows (.NET Framework 4.8).
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist app\app.ico powershell -NoProfile -ExecutionPolicy Bypass -File app\make-icon.ps1

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /out:"WhatsApp Blast.exe" ^
  /win32icon:app\app.ico /win32manifest:app\app.manifest ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  app\*.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built "WhatsApp Blast.exe"
