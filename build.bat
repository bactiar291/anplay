@echo off
setlocal
set ROOT=%~dp0
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found.
  exit /b 1
)
if not exist "%ROOT%dist" mkdir "%ROOT%dist"
if not exist "%ROOT%release" mkdir "%ROOT%release"
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Web.Extensions.dll ^
  /win32icon:"%ROOT%assets\AnPlay.ico" ^
  /out:"%ROOT%dist\AnPlay.exe" ^
  "%ROOT%src\AnPlay.cs"
if errorlevel 1 exit /b %errorlevel%
echo Built "%ROOT%dist\AnPlay.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Force -Path '%ROOT%dist\AnPlay.exe' -DestinationPath '%ROOT%release\AnPlay-release.zip'" >nul 2>nul
if errorlevel 1 (
  echo Release zip skipped. EXE is ready in "%ROOT%dist\AnPlay.exe"
) else (
  echo Packaged "%ROOT%release\AnPlay-release.zip"
)
