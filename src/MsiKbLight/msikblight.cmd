@echo off
rem Runs msikblight through the signed dotnet.exe host (Smart App Control blocks an unsigned exe).
rem Without arguments the settings window opens without a console window.
if "%~1"=="" (
  start "" "%SystemRoot%\System32\conhost.exe" --headless "%ProgramFiles%\dotnet\dotnet.exe" "%~dp0msikblight.dll"
  exit /b 0
)
"%ProgramFiles%\dotnet\dotnet.exe" "%~dp0msikblight.dll" %*
