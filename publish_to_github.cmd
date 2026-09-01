@echo off
rem ============================================================
rem  Publish FeatureDeck to your GitHub fork
rem  Before running: fork https://github.com/thebookisclosed/ViVe
rem ============================================================
setlocal
cd /d "%~dp0"

echo.
echo   FeatureDeck - Publish to GitHub Fork
echo   ======================================
echo.
echo IMPORTANT: This will OVERWRITE the content of your fork
echo            (that is intended - we replace ViVe CLI with this GUI).
echo.
echo If you have NOT forked yet, open this URL first:
echo     https://github.com/thebookisclosed/ViVe/fork
echo.
pause

set /p GH_USER=GitHub username: 
if "%GH_USER%"=="" (
    echo [ERROR] Username cannot be empty.
    pause
    exit /b 1
)

set GH_REPO=FeatureDeck
set /p GH_REPO=Repo name [default: FeatureDeck]: 
if "%GH_REPO%"=="" set GH_REPO=FeatureDeck

set REPO_URL=https://github.com/%GH_USER%/%GH_REPO%.git
echo.
echo Target: %REPO_URL%
echo.

where git >nul 2>nul
if errorlevel 1 (
    echo [ERROR] git not found in PATH.
    pause
    exit /b 1
)

git remote remove origin >nul 2>&1
git remote add origin %REPO_URL%

echo Pushing (branch: main, force overwrite)...
echo When asked for password, use a Personal Access Token,
echo NOT your GitHub login password.
echo Create one at: https://github.com/settings/tokens  ^(scope: repo^)
echo.

git push -u origin main --force
if errorlevel 1 (
    echo.
    echo [ERROR] Push failed. Common causes:
    echo   1. No network/proxy access to github.com
    echo   2. Fork not created yet - visit:
    echo      https://github.com/thebookisclosed/ViVe/fork
    echo   3. Auth failed - use a Personal Access Token as password
    pause
    exit /b 1
)

echo.
echo [OK] Published!
echo Repo:  https://github.com/%GH_USER%/%GH_REPO%
pause