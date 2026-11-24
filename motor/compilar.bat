@echo off
rem Compila o motor do Timbro (timbro_apo.dll, 64 bits) e o programa de teste.
rem Precisa das Ferramentas de Build do Visual Studio com C++.
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`call "%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS=%%i"
if not defined VS (echo Nao achei o compilador C++. & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1
set "AQUI=%~dp0"
set "SAIDA=%AQUI%bin"
if not exist "%SAIDA%" mkdir "%SAIDA%"
pushd "%SAIDA%"
cl /nologo /O2 /MT /EHsc /W3 /utf-8 /std:c++17 /LD "%AQUI%timbro_apo.cpp" /Fe:timbro_apo.dll /link /DEF:"%AQUI%timbro_apo.def" ole32.lib advapi32.lib || (popd & exit /b 1)
cl /nologo /O2 /MT /EHsc /W3 /utf-8 /std:c++17 "%AQUI%teste_motor.cpp" /Fe:teste_motor.exe /link ole32.lib advapi32.lib || (popd & exit /b 1)
popd
echo Compilado em %SAIDA%
