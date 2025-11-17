@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1

echo ===== Miniconda Installation =====
echo.
echo This will download and install Miniconda (a minimal conda installer).
echo This is required for setting up the OCR service environments.
echo.

REM Check if conda is already installed
where conda >nul 2>nul && goto :CondaAlreadyInstalled

echo Downloading Miniconda installer...
echo.
curl https://repo.anaconda.com/miniconda/Miniconda3-latest-Windows-x86_64.exe -o .\miniconda.exe || goto :FailDownload

echo.
echo Starting Miniconda installation...
echo.
echo IMPORTANT: When the installer appears, please:
echo   - Accept the license agreement
echo   - Choose install location (default is fine)
echo   - Check "Add Miniconda3 to my PATH environment variable" (IMPORTANT!)
echo.
pause

start /wait "" .\miniconda.exe

echo.
echo Cleaning up installer...
del .\miniconda.exe

echo.
echo ===== Installation Complete =====
echo.
echo IMPORTANT NEXT STEPS:
echo   1. Close UGTLive (if it's running)
echo   2. Restart UGTLive
echo   3. The app will detect conda and allow you to continue setup
echo.
echo NOTE: PATH changes may not be immediately available. If conda is not
echo detected after restarting, you may need to restart your computer for
echo the PATH changes to take full effect.
echo.
pause
goto :eof

:CondaAlreadyInstalled
echo Conda is already installed!
echo.
conda --version
echo.
echo If you're still having issues with the setup scripts, you may need to:
echo   1. Close this window
echo   2. Open a NEW Command Prompt
echo   3. Run the setup script again
echo.
pause
exit /b 0

:FailDownload
echo ERROR: Failed to download Miniconda installer!
echo Please check your internet connection and try again.
pause
exit /b 1

