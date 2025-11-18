@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "LOG_FILE=%SCRIPT_DIR%setup_conda_log.txt"

REM Delete existing log file
if exist "%LOG_FILE%" del "%LOG_FILE%"

REM Create log file and add header
echo ============================================================= >> "%LOG_FILE%"
echo Setup Log - %date% %time% >> "%LOG_FILE%"
echo ============================================================= >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Parse service_config.txt
REM Note: service_name and conda_env_name should use ASCII characters
REM       only for maximum compatibility across different locales.
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

if "!ENV_NAME!"=="" set "ENV_NAME=ugt_mangaocr"
if "!SERVICE_NAME!"=="" set "SERVICE_NAME=MangaOCR"

echo ============================================================= >> "%LOG_FILE%"
echo Service: !SERVICE_NAME! >> "%LOG_FILE%"
echo Environment: !ENV_NAME! >> "%LOG_FILE%"
echo ============================================================= >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

echo =============================================================
echo   !SERVICE_NAME! Environment Setup
echo   Conda Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Validate conda
REM -----------------------------------------------------------------
echo Checking for conda installation...
where conda >nul 2>nul
if errorlevel 1 (
    echo WARNING: Conda not found in PATH. Please install conda first.
    echo WARNING: Conda not found >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo Conda found successfully >> "%LOG_FILE%"

echo Detecting GPU...
echo.
echo Detecting GPU... >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Detect GPU
REM -----------------------------------------------------------------
set "GPU_SERIES=30_40"
nvidia-smi --query-gpu=name --format=csv,noheader >"%TEMP%\gpu_check.txt" 2>nul
if exist "%TEMP%\gpu_check.txt" (
    set /p GPU_NAME=<"%TEMP%\gpu_check.txt"
    del "%TEMP%\gpu_check.txt" >nul 2>&1
    echo Detected GPU: !GPU_NAME! >> "%LOG_FILE%"
    echo Detected GPU: !GPU_NAME!
    
    echo !GPU_NAME! | find /i "RTX 50" >nul && set "GPU_SERIES=50"
) else (
    echo Using default GPU configuration (RTX 30/40 series)
    echo Using default GPU configuration >> "%LOG_FILE%"
)

echo.

REM -----------------------------------------------------------------
REM Prepare conda
REM -----------------------------------------------------------------
echo [1/3] Activating conda base environment...
call conda activate base >> "%LOG_FILE%" 2>&1

echo [2/3] Accepting conda Terms of Service...
call conda tos accept 2>nul

echo [3/3] Updating conda...
call conda update -y conda >> "%LOG_FILE%" 2>&1

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo Removing existing environment if present...
call conda env remove -n "!ENV_NAME!" -y >> "%LOG_FILE%" 2>&1

echo Creating new conda environment...

REM Set Python version based on GPU series
set "PYTHON_VERSION=3.10"
if "!GPU_SERIES!"=="50" set "PYTHON_VERSION=3.11"

echo Creating environment "!ENV_NAME!" with Python !PYTHON_VERSION!...
call conda create -y --name "!ENV_NAME!" python=!PYTHON_VERSION! >> "%LOG_FILE%" 2>&1

echo Activating new environment...
call conda activate "!ENV_NAME!" >> "%LOG_FILE%" 2>&1

set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set KMP_DUPLICATE_LIB_OK=TRUE
echo Environment variables set >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Install based on GPU series
REM -----------------------------------------------------------------
echo.
echo Installing dependencies for GPU series: !GPU_SERIES!
echo Installing dependencies for GPU series: !GPU_SERIES! >> "%LOG_FILE%"

if "!GPU_SERIES!"=="50" (
    echo Installing for RTX 50 series with PyTorch nightly CUDA 12.8...
    call :Install50Series
) else (
    echo Installing for RTX 30/40 series with PyTorch 2.6 CUDA 11.8...
    call :Install30_40Series
)

goto :SetupComplete

:SetupComplete
echo Reached SetupComplete label >> "%LOG_FILE%"
echo Reached SetupComplete label

REM -----------------------------------------------------------------
REM Setup complete
REM -----------------------------------------------------------------
echo. >> "%LOG_FILE%"
echo ============================================================= >> "%LOG_FILE%"
echo SUCCESS! Setup completed successfully >> "%LOG_FILE%"
echo End time: %date% %time% >> "%LOG_FILE%"
echo ============================================================= >> "%LOG_FILE%"

echo.
echo =============================================================
echo   SUCCESS! Setup complete for !SERVICE_NAME!
echo   Environment: !ENV_NAME!
echo   The service is ready to use.
echo =============================================================
echo.
echo Next steps:
echo   1. Run DiagnosticTest.bat to verify the installation
echo   2. Run RunServer.bat to start the service
echo   3. Run TestService.bat to test the running service
echo.
echo Log file: setup_conda_log.txt
echo.
echo Press any key to exit...
pause
exit /b 0

REM -----------------------------------------------------------------
REM Installation for RTX 30/40 Series
REM -----------------------------------------------------------------
:Install30_40Series
echo Installing dependencies...

set "PYTORCH_CHANNEL=https://download.pytorch.org/whl/cu118"

echo [1/5] Installing PyTorch with CUDA 11.8...
python -m pip install --upgrade pip >> "%LOG_FILE%" 2>&1
python -m pip install --extra-index-url !PYTORCH_CHANNEL! torch==2.6.0+cu118 torchvision==0.21.0+cu118 torchaudio==2.6.0+cu118 >> "%LOG_FILE%" 2>&1

echo [2/5] Installing core packages...
python -m pip install numpy==2.0.2 pillow==11.3.0 "opencv-python-headless>=4.10,<4.12" scipy==1.13.1 >> "%LOG_FILE%" 2>&1
python -m pip install transformers==4.57.1 huggingface-hub==0.36.0 tokenizers==0.22.1 safetensors==0.6.2 >> "%LOG_FILE%" 2>&1
python -m pip install fugashi==1.5.2 unidic_lite==1.0.8 >> "%LOG_FILE%" 2>&1

echo [3/5] Installing Manga OCR and YOLO...
python -m pip install --extra-index-url !PYTORCH_CHANNEL! manga-ocr==0.1.14 ultralytics==8.3.226 >> "%LOG_FILE%" 2>&1

echo [4/5] Installing API server...
python -m pip install fastapi==0.121.1 "uvicorn[standard]==0.38.0" python-multipart==0.0.20 pydantic==2.10.5 >> "%LOG_FILE%" 2>&1

echo [5/5] Installing CuPy (optional)...
python -m pip install cupy-cuda12x==13.6.0 >> "%LOG_FILE%" 2>&1

echo Initializing Manga OCR...
python -c "from manga_ocr import MangaOcr; MangaOcr()" >> "%LOG_FILE%" 2>&1

echo Installation completed for RTX 30/40 series
goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 50 Series (PyTorch Nightly with CUDA 12.8)
REM -----------------------------------------------------------------
:Install50Series
echo Installing dependencies...

echo [1/5] Installing PyTorch nightly with CUDA 12.8...
python -m pip install --upgrade pip >> "%LOG_FILE%" 2>&1
python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 >> "%LOG_FILE%" 2>&1

echo [2/5] Installing core packages...
python -m pip install numpy pillow opencv-python scipy tqdm pyyaml requests >> "%LOG_FILE%" 2>&1
python -m pip install transformers huggingface-hub tokenizers safetensors >> "%LOG_FILE%" 2>&1
python -m pip install fugashi unidic_lite >> "%LOG_FILE%" 2>&1

echo [3/5] Installing Manga OCR and YOLO...
python -m pip install manga-ocr ultralytics >> "%LOG_FILE%" 2>&1

echo [4/5] Installing API server...
python -m pip install fastapi "uvicorn[standard]" python-multipart >> "%LOG_FILE%" 2>&1

echo [5/5] Installing CuPy (optional)...
python -m pip install cupy-cuda12x >> "%LOG_FILE%" 2>&1

echo Initializing Manga OCR...
python -c "from manga_ocr import MangaOcr; MangaOcr()" >> "%LOG_FILE%" 2>&1

echo Installation completed for RTX 50 series
goto :eof

