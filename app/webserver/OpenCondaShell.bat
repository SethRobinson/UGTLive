@echo off
echo ===== Opening Conda Shell for UGTLive OCR Environment =====
echo.
echo NOTE: You probably don't need this! This is only for advanced users who want to
echo manually tweak the conda environment or install additional packages.
echo.
echo If you just want to run the OCR server, use RunServer.bat instead.
echo.
echo Example commands you might use in the conda shell:
echo   - pip install requests     (install a Python package)
echo   - pip list                 (see all installed packages)
echo   - python --version         (check Python version)
echo   - conda list               (see all conda packages)
echo.
echo ============================================================
echo.

REM Check if conda command exists
where conda >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Conda is not installed or not in PATH!
    echo Please install Miniconda first. See README.md for instructions.
    echo.
    pause
    exit /b 1
)

REM Check if the ocrstuff environment exists
call conda env list | findstr /B "ocrstuff" >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: The 'ocrstuff' conda environment does not exist!
    echo.
    echo Please run SetupServerCondaEnvNVidia.bat first to create the environment.
    echo.
    pause
    exit /b 1
)

echo Opening conda shell with 'ocrstuff' environment...
echo Type 'exit' when you're done to close the shell.
echo.

REM Activate the environment and keep the shell open
call conda activate ocrstuff
cmd /k 