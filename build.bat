@echo off
  
set Configuration=Release

if /i "%1"=="DEBUG" set Configuration=Debug
if /i "%2"=="DEBUG" set Configuration=Debug
if /i "%1"=="RESET" goto net40
if /i "%1"=="PUSH-NUGET" goto net40

echo abc | powershell -NonInteractive -NoProfile -ExecutionPolicy unrestricted -Command "%~dp0\packages\psake.4.0.1.0\tools\psake.ps1 .\BuildScript.ps1 -properties @{configuration='%Configuration%'} -framework '3.5' BuildNet35"

:net40
echo abc | powershell -NonInteractive -NoProfile -ExecutionPolicy unrestricted -Command "%~dp0\packages\psake.4.0.1.0\tools\psake.ps1 .\BuildScript.ps1 -properties @{configuration='%Configuration%'} -framework '4.0' %1"
goto end

:end
IF %ERRORLEVEL% NEQ 0 EXIT /B %ERRORLEVEL%