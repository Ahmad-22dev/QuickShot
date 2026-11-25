@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find .NET Framework C# compiler at %CSC%
  exit /b 1
)

taskkill /f /im QuickShot.exe >nul 2>&1

echo Building QuickShot.exe...
"%CSC%" /nologo /target:winexe /optimize+ /warn:4 ^
  /win32icon:"%~dp0QuickShot.ico" ^
  /out:"%~dp0QuickShot.exe" ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%~dp0QuickShot.cs"
if errorlevel 1 exit /b 1

echo Building QuickShot-Setup.exe...
"%CSC%" /nologo /target:winexe /optimize+ /warn:4 ^
  /win32icon:"%~dp0QuickShot.ico" ^
  /resource:"%~dp0QuickShot.exe",QuickShot.exe ^
  /resource:"%~dp0QuickShot.ico",QuickShot.ico ^
  /out:"%~dp0QuickShot-Setup.exe" ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%~dp0QuickShotSetup.cs"
if errorlevel 1 exit /b 1

echo Build succeeded: QuickShot.exe + QuickShot-Setup.exe
start "" "%~dp0QuickShot.exe" --silent
