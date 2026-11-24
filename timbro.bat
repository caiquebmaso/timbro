@echo off
rem Abre o Timbro. Na primeira vez (ou se o codigo mudar) compila o timbro.exe e o motor
rem em %LOCALAPPDATA%\Timbro.
setlocal
set "SRC=%~dp0timbro.cs"
set "OUT=%LOCALAPPDATA%\Timbro"
set "EXE=%OUT%\timbro.exe"
set "DLL=%~dp0motor\bin\timbro_apo.dll"
if not exist "%OUT%" mkdir "%OUT%"

if not exist "%DLL%" (
  call "%~dp0motor\compilar.bat" || (echo Nao consegui compilar o motor. & pause & exit /b 1)
)
copy /y "%DLL%" "%OUT%\timbro_apo.dll" >nul

set "PRECISA="
if not exist "%EXE%" set PRECISA=1
if exist "%EXE%" (
  for /f %%r in ('powershell -NoProfile -Command "if ((Get-Item '%SRC%').LastWriteTime -gt (Get-Item '%EXE%').LastWriteTime) {1} else {0}"') do if "%%r"=="1" set PRECISA=1
)

if defined PRECISA (
  "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -codepage:65001 -optimize+ -target:winexe -out:"%EXE%" ^
    -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll -r:System.ServiceProcess.dll "%SRC%" || (echo Nao consegui compilar. & pause & exit /b 1)
)

start "" "%EXE%" %*
exit /b 0
