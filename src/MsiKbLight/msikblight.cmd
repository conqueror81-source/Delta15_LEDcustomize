@echo off
rem Runs msikblight through the signed dotnet.exe host (Smart App Control blocks an unsigned exe).
rem Without arguments the settings window opens without a console window.
setlocal
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET%" for /f "delims=" %%i in ('where dotnet.exe 2^>nul') do if not defined DOTNET_FOUND (set "DOTNET=%%i" & set DOTNET_FOUND=1)
if "%~1"=="" (
  start "" "%SystemRoot%\System32\conhost.exe" --headless "%DOTNET%" "%~dp0msikblight.dll"
  exit /b 0
)
"%DOTNET%" "%~dp0msikblight.dll" %*
exit /b %ERRORLEVEL%
