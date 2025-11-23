@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "VENV_DIR=%SCRIPT_DIR%venv"
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
        
        if "!KEY!"=="venv_name" set "ENV_NAME=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
    )
)

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find venv_name in service_config.txt
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
REM Check if virtual environment exists
REM -----------------------------------------------------------------
echo [1/4] Checking if virtual environment exists...
if not exist "%VENV_DIR%\Scripts\activate.bat" (
    echo   [FAIL] Virtual environment does not exist at: %VENV_DIR%
    echo.
    echo   Click Install to fix this. Manual option: run Install.bat
    echo.
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)
echo   [PASS] Virtual environment exists
echo.

REM -----------------------------------------------------------------
REM Activate environment and test imports
REM -----------------------------------------------------------------
echo [2/4] Activating virtual environment...
call "%VENV_DIR%\Scripts\activate.bat"
if errorlevel 1 (
    echo   [FAIL] Could not activate virtual environment
    echo.
    if not "%NOPAUSE%"=="nopause" pause
    exit /b 1
)
echo   [PASS] Virtual environment activated
echo.

echo Checking Python version...
python --version
echo.

echo [3/4] Testing Python imports and library versions...
echo   - Testing PyTorch...
python -c "import torch; print('    PyTorch version:', torch.__version__); print('    CUDA available:', torch.cuda.is_available())" 2>nul
if errorlevel 1 (
    echo   [FAIL] PyTorch import failed
    goto :TestFail
)

echo   - Testing Manga OCR...
python -c "from manga_ocr import MangaOcr; print('    Manga OCR imported successfully')" 2>nul
if errorlevel 1 (
    echo   [FAIL] Manga OCR import failed
    goto :TestFail
)

echo   - Testing Ultralytics...
python -c "import ultralytics; print('    Ultralytics version:', ultralytics.__version__); print('    Ultralytics imported successfully')" 2>nul
if errorlevel 1 (
    echo   [FAIL] Ultralytics import failed
    goto :TestFail
)

echo   - Testing core dependencies...
python -c "import numpy; import PIL; import cv2; import scipy; print('    NumPy version:', numpy.__version__); print('    Pillow version:', PIL.__version__); print('    OpenCV version:', cv2.__version__); print('    SciPy version:', scipy.__version__)" 2>nul
if errorlevel 1 (
    echo   [WARN] Some core dependencies not found or version info unavailable
) else (
    echo   [PASS] Core dependencies verified
)

echo   - Testing Transformers dependencies...
python -c "import transformers; import tokenizers; print('    Transformers version:', transformers.__version__); print('    Tokenizers version:', tokenizers.__version__)" 2>nul
if errorlevel 1 (
    echo   [WARN] Transformers dependencies not found or version info unavailable
) else (
    echo   [PASS] Transformers dependencies verified
)

echo   - Testing Japanese text processing...
python -c "import fugashi; import unidic_lite; print('    Fugashi version:', fugashi.__version__); print('    Japanese text processing libraries verified')" 2>nul
if errorlevel 1 (
    echo   [WARN] Japanese text processing libraries not found or version info unavailable
) else (
    echo   [PASS] Japanese text processing verified
)

echo   - Testing FastAPI...
python -c "import fastapi; import pydantic; print('    FastAPI version:', fastapi.__version__); print('    Pydantic version:', pydantic.__version__)" 2>nul
if errorlevel 1 (
    echo   [FAIL] FastAPI import failed
    goto :TestFail
)

echo   - Testing Uvicorn...
python -c "import uvicorn; print('    Uvicorn version:', uvicorn.__version__)" 2>nul
if errorlevel 1 (
    echo   [FAIL] Uvicorn import failed
    goto :TestFail
)

echo   - Testing Scikit-learn (optional)...
python -c "import sklearn; print('    Scikit-learn version:', sklearn.__version__)" 2>nul
if errorlevel 1 (
    echo   [WARN] Scikit-learn not found (optional dependency)
) else (
    echo   [PASS] Scikit-learn verified
)

echo   [PASS] All imports successful
echo.

echo [4/4] Testing GPU availability...
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
echo   Click Install to fix this ^(or manually run Install.bat^)
echo =============================================================
echo.
if not "%NOPAUSE%"=="nopause" pause
exit /b 1

