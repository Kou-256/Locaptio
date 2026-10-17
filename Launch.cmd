@echo off
set "app=%~dp0Locaptio.exe"
if not exist "%app%" set "app=%~dp0artifacts\Locaptio-1.0.17\Locaptio.exe"
if not exist "%app%" set "app=%~dp0src\WinUI\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64\Locaptio.exe"
if not exist "%app%" set "app=%~dp0src\App\bin\Release\net8.0-windows\Locaptio.Legacy.exe"
if not exist "%app%" (
    echo Please run scripts/setup.ps1 first.
    pause
    exit /b 1
)
start "" "%app%"
