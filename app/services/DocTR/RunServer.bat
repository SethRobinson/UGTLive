@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"

REM -----------------------------------------------------------------
REM Parse service_config.txt to get environment name
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
        
        if "!KEY!"=="conda_env_name" set "ENV_NAME=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
    )
)

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find conda_env_name in service_config.txt
    pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   Starting !SERVICE_NAME!
echo   Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Activate conda environment and run server
REM -----------------------------------------------------------------
call conda activate !ENV_NAME! || goto :FailActivate

echo Activating environment...
echo Starting server...
echo.

python "%SCRIPT_DIR%server.py"
if errorlevel 1 (
    echo.
    echo ERROR: Server crashed or failed to start
    echo Please check the error message above
    echo.
    pause
    exit /b 1
)

goto :eof

:FailActivate
echo.
echo ERROR: Failed to activate conda environment: !ENV_NAME!
echo.
echo Please run SetupServerCondaEnv.bat first to create the environment.
echo.
pause
exit /b 1

