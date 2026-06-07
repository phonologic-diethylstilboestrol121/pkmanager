@echo off
setlocal
cd /d "%~dp0"

set "SELF=%~f0"
set "LAUNCHER=%~dp0pkmanager-launcher.ps1"
set "CMD=powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%LAUNCHER%\" \"%%1\""

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo [pkmanager] Admin permission required. Requesting elevation...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath $env:SELF -Verb RunAs"
    exit /b
)

echo [pkmanager] Registering pkmanager:// protocol...

if not exist "%LAUNCHER%" (
    echo [ERROR] Missing launcher file:
    echo %LAUNCHER%
    pause
    exit /b 1
)

reg add "HKCR\pkmanager" /f /ve /d "URL:pkmanager Protocol" >nul 2>&1
if errorlevel 1 goto :register_error
reg add "HKCR\pkmanager" /f /v "URL Protocol" /d "" >nul 2>&1
if errorlevel 1 goto :register_error
reg add "HKCR\pkmanager\shell\open\command" /f /ve /d "%CMD%" >nul 2>&1
if errorlevel 1 goto :register_error

echo [pkmanager] Registration complete.
echo Return to pkmanager and click the local-launch button.
pause
exit /b 0

:register_error
echo [ERROR] Protocol registration failed.
pause
exit /b 1
