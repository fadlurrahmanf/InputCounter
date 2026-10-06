@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:winexe /out:"%~dp0InputCounter-compact-aura-overlay.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%~dp0InputCounter.cs"
if errorlevel 1 pause
