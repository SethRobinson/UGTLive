@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "VENV_DIR=%SCRIPT_DIR%venv"
set "NOPAUSE=%~1"
set "NOPAUSE_MODE=0"

REM Check for nopause parameter
if /I "%~1"=="nopause" set "NOPAUSE_MODE=1"

REM -----------------------------------------------------------------
REM Parse service_config.txt to get environment name and service name
REM -----------------------------------------------------------------
set "ENV_NAME="
set "SERVICE_NAME="

if exist "%CONFIG_FILE%" (
    for /f "usebackq tokens=1,2 delims=| eol=#" %%a in ("%CONFIG_FILE%") do (
        set "KEY=%%a"
        set "VALUE=%%b"
        
        REM Trim leading/trailing spaces
        for /f "tokens=*" %%x in ("!KEY!") do set "KEY=%%x"
        for /f "tokens=*" %%y in ("!VALUE!") do set "VALUE=%%y"
        
        if "!KEY!"=="venv_name" set "ENV_NAME=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
    )
)

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find venv_name in service_config.txt
    if "!NOPAUSE_MODE!"=="0" pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   !SERVICE_NAME! - Remove Virtual Environment
echo   Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Check if venv exists
REM -----------------------------------------------------------------
echo Checking for virtual environment "!ENV_NAME!"...
if exist "%VENV_DIR%" (
    echo Found virtual environment at: %VENV_DIR%
    echo Removing it...
    echo.
    rmdir /s /q "%VENV_DIR%"
    if exist "%VENV_DIR%" (
        echo.
        echo ERROR: Failed to remove virtual environment
        echo Please close any terminals or applications using this environment and try again.
        echo.
        if "!NOPAUSE_MODE!"=="0" pause
        exit /b 1
    )
    echo.
    echo =============================================================
    echo   SUCCESS! Virtual environment "!ENV_NAME!" removed successfully.
    echo =============================================================
) else (
    echo.
    echo Virtual environment "!ENV_NAME!" not found. Nothing to remove.
    echo.
)

echo.
if "!NOPAUSE_MODE!"=="0" (
    echo Press any key to exit...
    pause >nul
)

exit /b 0

