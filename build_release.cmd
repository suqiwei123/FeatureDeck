@echo off
rem ============================================================
rem  Build a release zip of FeatureDeck for GitHub Releases
rem  Usage:  build_release.cmd  [version]  (default: 0.1.0)
rem ============================================================
setlocal
cd /d "%~dp0src\FeatureDeck"

set VER=%~1
if "%VER%"=="" set VER=0.1.0
set OUT=bind\Release
et8.0-windows10.0.19041.0
set DIST=%~dp0dist
set ZIP=%DIST%\FeatureDeck-v%VER%-win-x64.zip

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet not found. Please install .NET 8 SDK first.
    pause
    exit /b 1
)

echo Building FeatureDeck v%VER% (Release, self-contained x64)...
dotnet build -c Release -p:Platform=x64
if errorlevel 1 (
    echo [ERROR] Build failed.
    pause
    exit /b 1
)

if not exist "%OUT%\FeatureDeck.exe" (
    echo [ERROR] Build output not found: %OUT%\FeatureDeck.exe
    pause
    exit /b 1
)

echo Cleaning debug symbols...
del /S /Q "%OUT%\*.pdb" 2>nul

echo Packaging release zip...
if not exist "%DIST%" mkdir "%DIST%"
if exist "%ZIP%" del /Q "%ZIP%"

rem Windows 10 1803+ has built-in tar, picks format by .zip extension
tar -a -c -f "%ZIP%" -C "%OUT%" .
if errorlevel 1 (
    echo [ERROR] tar packaging failed.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo   Release built: %ZIP%
echo ============================================================
for %%I in ("%ZIP%") do @echo   Size: %%~zI bytes
echo.
echo   Upload to GitHub Release:
echo     gh release create v%VER% "%ZIP%" --title "FeatureDeck v%VER%" --generate-notes
echo ============================================================
pause