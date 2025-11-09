@echo off
setlocal ENABLEDELAYEDEXPANSION

echo =============================================================
echo   UGTLive Server Diagnostics Check
echo =============================================================
echo.

REM Ensure conda is available
where conda >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Conda is not installed or not in PATH.
    echo Please install Miniconda and re-run the environment setup.
    exit /b 1
)

echo Using environment: ocrstuff
echo.

REM Check Python version
echo Checking Python version...
conda run -n ocrstuff python --version
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Python not found in environment
    exit /b 1
)
echo.

REM Check PyTorch
echo Checking PyTorch...
conda run -n ocrstuff python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Available:', torch.cuda.is_available()); print('CUDA Version:', torch.version.cuda if torch.cuda.is_available() else 'N/A')" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: PyTorch not found or error importing
)
echo.

REM Check EasyOCR
echo Checking EasyOCR...
conda run -n ocrstuff python -c "import easyocr; print('EasyOCR: Available')" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: EasyOCR not found
)
echo.

REM Check Manga OCR
echo Checking Manga OCR...
conda run -n ocrstuff python -c "from manga_ocr import MangaOcr; print('Manga OCR: Available')" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: Manga OCR not found
)
echo.

REM Check docTR
echo Checking docTR...
conda run -n ocrstuff python -c "from doctr.models import ocr_predictor; print('docTR: Available')" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: docTR not found
)
echo.

REM Check Ultralytics
echo Checking Ultralytics...
conda run -n ocrstuff python -c "import ultralytics; print('Ultralytics Version:', ultralytics.__version__)" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: Ultralytics not found
)
echo.

REM Check OpenCV
echo Checking OpenCV...
conda run -n ocrstuff python -c "import cv2; print('OpenCV Version:', cv2.__version__)" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo WARNING: OpenCV not found
)
echo.

REM Check CuPy (try both versions)
echo Checking CuPy...
conda run -n ocrstuff python -c "import cupy; print('CuPy Version:', cupy.__version__)" 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo INFO: CuPy not found (optional for CPU-only operation)
)
echo.

echo =============================================================
echo   Diagnostics Complete
echo =============================================================
echo.

pause

