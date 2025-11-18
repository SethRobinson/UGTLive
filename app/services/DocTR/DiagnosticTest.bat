@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "NOPAUSE=%~1"

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
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   !SERVICE_NAME! Diagnostic Test
echo   Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Check if conda is available
REM -----------------------------------------------------------------
echo [1/5] Checking if conda is installed...
where conda >nul 2>nul
if errorlevel 1 (
    echo   [FAIL] Conda is not installed or not in PATH
    echo.
    echo   Please install Miniconda using the InstallMiniConda.bat script.
    echo.
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)
echo   [PASS] Conda is installed
echo.

REM -----------------------------------------------------------------
REM Check if environment exists
REM -----------------------------------------------------------------
echo [2/5] Checking if environment exists...
call conda env list | findstr /C:"!ENV_NAME!" >nul
if errorlevel 1 (
    echo   [FAIL] Environment !ENV_NAME! does not exist
    echo.
    echo   Click Install to fix this. Manual option: run SetupServerCondaEnv.bat
    echo.
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)
echo   [PASS] Environment !ENV_NAME! exists
echo.

REM -----------------------------------------------------------------
REM Activate environment and test imports
REM -----------------------------------------------------------------
echo [3/5] Activating environment...
call conda activate !ENV_NAME!
if errorlevel 1 (
    echo   [FAIL] Could not activate environment !ENV_NAME!
    echo.
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)
echo   [PASS] Environment activated
echo.

echo [4/5] Testing Python imports...
echo   - Testing PyTorch...
python -c "import torch; print('    PyTorch version:', torch.__version__); print('    CUDA available:', torch.cuda.is_available())" 2>nul
if errorlevel 1 (
    echo   [FAIL] PyTorch import failed
    goto :TestFail
)

echo   - Testing DocTR...
python -c "from doctr.models import ocr_predictor; print('    DocTR imported successfully')" 2>nul
if errorlevel 1 (
    echo   [FAIL] DocTR import failed
    goto :TestFail
)

echo   - Testing FastAPI...
python -c "import fastapi; print('    FastAPI imported successfully')" 2>nul
if errorlevel 1 (
    echo   [FAIL] FastAPI import failed
    goto :TestFail
)

echo   - Testing Uvicorn...
python -c "import uvicorn; print('    Uvicorn imported successfully')" 2>nul
if errorlevel 1 (
    echo   [FAIL] Uvicorn import failed
    goto :TestFail
)

echo   [PASS] All imports successful
echo.

echo [5/5] Testing GPU availability...
python -c "import torch; cuda_ok = torch.cuda.is_available(); print('    CUDA Available:', cuda_ok); print('    GPU Count:', torch.cuda.device_count() if cuda_ok else 0); print('    GPU Name:', torch.cuda.get_device_name(0) if cuda_ok else 'N/A')" 2>nul
if errorlevel 1 (
    echo   [WARN] Could not query GPU status
) else (
    echo   [PASS] GPU status queried successfully
)
echo.

echo =============================================================
echo   All diagnostic tests passed!
echo   !SERVICE_NAME! is ready to use.
echo =============================================================
echo.
if not "%NOPAUSE%"=="nopause" pause
goto :eof

:TestFail
echo.
echo =============================================================
echo   Diagnostic tests FAILED!
echo   Click Install to fix this ^(or manually run SetupServerCondaEnv.bat^)
echo =============================================================
echo.
if not "%NOPAUSE%"=="nopause" pause
exit /b 1

