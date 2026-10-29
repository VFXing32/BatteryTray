@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0BatteryTray.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "%~dp0BatteryTray.cs"
if errorlevel 1 exit /b 1
echo Built %~dp0BatteryTray.exe
