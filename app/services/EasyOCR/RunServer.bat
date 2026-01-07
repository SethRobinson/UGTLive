@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "VENV_DIR=%SCRIPT_DIR%venv"

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
        
        if "!KEY!"=="venv_name" set "ENV_NAME=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
    )
)

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find venv_name in service_config.txt
    if not "%1"=="nopause" pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   Starting !SERVICE_NAME!
echo   Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Check if virtual environment exists
REM -----------------------------------------------------------------
if not exist "%VENV_DIR%\Scripts\activate.bat" (
    echo.
    echo ERROR: Virtual environment not found at: %VENV_DIR%
    echo.
    echo Please run Install.bat first to create the environment.
    echo.
    if not "%1"=="nopause" pause
    exit /b 1
)

REM -----------------------------------------------------------------
REM Activate virtual environment and run server
REM -----------------------------------------------------------------
call "%VENV_DIR%\Scripts\activate.bat"

echo Activating environment...

REM -----------------------------------------------------------------
REM Set SSL certificate file for urllib (fixes certificate errors on some systems)
REM -----------------------------------------------------------------
for /f "delims=" %%i in ('python -c "import certifi; print(certifi.where())"') do set "SSL_CERT_FILE=%%i"
set "SSL_CERT_DIR="

echo Starting server...
echo.

python "%SCRIPT_DIR%server.py"
if errorlevel 1 (
    echo.
    echo ERROR: Server crashed or failed to start
    echo Please check the error message above
    echo.
    if not "%1"=="nopause" pause
    exit /b 1
)

goto :eof

