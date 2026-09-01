@echo off
rem One-click build and launch for FeatureDeck
setlocal
cd /d "%~dp0src\FeatureDeck"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Please install .NET 8 SDK first.
    pause
    exit /b 1
)

echo Building (Release / x64)...
call dotnet build -c Release -p:Platform=x64
if errorlevel 1 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

set OUT=bin\x64\Release\net8.0-windows10.0.19041.0
echo Build succeeded.
echo Launching (requires administrator privileges, UAC prompt will appear)...
start "" "%OUT%\FeatureDeck.exe"
exit /b 0