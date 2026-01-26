@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "LOG_FILE=%SCRIPT_DIR%setup_log.txt"
set "VENV_DIR=%SCRIPT_DIR%venv"
set "UTIL_DIR=%SCRIPT_DIR%..\util"
set "NOPAUSE=%~1"
set "FORCE_GPU=%~2"

REM Delete existing log file
if exist "%LOG_FILE%" del "%LOG_FILE%"

REM Create log file and add header
echo ============================================================= >> "%LOG_FILE%"
echo Setup Log - %date% %time% >> "%LOG_FILE%"
echo ============================================================= >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Parse service_config.txt
REM Note: service_name and venv_name should use ASCII characters
REM       only for maximum compatibility across different locales.
REM -----------------------------------------------------------------
set "ENV_NAME="
set "SERVICE_NAME="
set "SERVICE_INSTALL_VERSION="

if exist "%CONFIG_FILE%" (
    for /f "usebackq tokens=1,2 delims=| eol=#" %%a in ("%CONFIG_FILE%") do (
        set "KEY=%%a"
        set "VALUE=%%b"
        
        REM Trim leading/trailing spaces
        for /f "tokens=*" %%x in ("!KEY!") do set "KEY=%%x"
        for /f "tokens=*" %%y in ("!VALUE!") do set "VALUE=%%y"
        
        if "!KEY!"=="venv_name" set "ENV_NAME=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
        if "!KEY!"=="service_install_version" set "SERVICE_INSTALL_VERSION=!VALUE!"
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
echo   Virtual Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Detect GPU - or use forced GPU series for testing
REM Usage: Install.bat [nopause] [30_40|50]
REM -----------------------------------------------------------------
if "!FORCE_GPU!"=="30_40" (
    echo FORCED GPU SERIES: 30_40 [test mode]
    echo FORCED GPU SERIES: 30_40 >> "%LOG_FILE%"
    set "GPU_SERIES=30_40"
    set "GPU_NAME=Forced RTX 30/40 series"
    goto :SkipGPUDetect
)
if "!FORCE_GPU!"=="50" (
    echo FORCED GPU SERIES: 50 [test mode]
    echo FORCED GPU SERIES: 50 >> "%LOG_FILE%"
    set "GPU_SERIES=50"
    set "GPU_NAME=Forced RTX 50 series"
    goto :SkipGPUDetect
)

echo Detecting GPU...
echo.
echo Detecting GPU... >> "%LOG_FILE%"

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

:SkipGPUDetect

echo.

REM -----------------------------------------------------------------
REM Set Python version based on GPU series
REM -----------------------------------------------------------------
set "PYTHON_VERSION=3.10"
if "!GPU_SERIES!"=="50" set "PYTHON_VERSION=3.11"

echo Python version: !PYTHON_VERSION! >> "%LOG_FILE%"
echo GPU Series !GPU_SERIES! requires Python !PYTHON_VERSION!
echo.

REM -----------------------------------------------------------------
REM Set Python executable path
REM -----------------------------------------------------------------
if "!PYTHON_VERSION!"=="3.10" (
    set "PYTHON_EXE=%UTIL_DIR%\Python310\python.exe"
) else (
    set "PYTHON_EXE=%UTIL_DIR%\Python311\python.exe"
)

echo Python executable: !PYTHON_EXE! >> "%LOG_FILE%"

if not exist "!PYTHON_EXE!" (
    echo.
    echo ERROR: Python !PYTHON_VERSION! not found at !PYTHON_EXE!
    echo ERROR: Python !PYTHON_VERSION! not found >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)

REM -----------------------------------------------------------------
REM Remove existing venv if present
REM -----------------------------------------------------------------
echo [1/3] Removing existing virtual environment if present...
echo [1/3] Removing existing venv... >> "%LOG_FILE%"

if exist "%VENV_DIR%" (
    echo Removing existing venv directory...
    echo Removing existing venv directory... >> "%LOG_FILE%"
    rmdir /s /q "%VENV_DIR%"
    if exist "%VENV_DIR%" (
        echo ERROR: Failed to remove existing venv directory
        echo ERROR: Failed to remove venv directory >> "%LOG_FILE%"
        pause
        exit /b 1
    )
    echo Removed existing venv >> "%LOG_FILE%"
) else (
    echo No existing venv found >> "%LOG_FILE%"
)

REM -----------------------------------------------------------------
REM Create new virtual environment
REM -----------------------------------------------------------------
echo [2/3] Creating new virtual environment...
echo [2/3] Creating new venv... >> "%LOG_FILE%"
echo Creating venv with Python !PYTHON_VERSION!...

"!PYTHON_EXE!" -m venv "%VENV_DIR%" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: Failed to create virtual environment
    echo ERROR: Failed to create venv >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)
echo Virtual environment created successfully >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Activate virtual environment
REM -----------------------------------------------------------------
echo [3/3] Activating virtual environment...
echo [3/3] Activating venv... >> "%LOG_FILE%"

call "%VENV_DIR%\Scripts\activate.bat" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: Failed to activate virtual environment
    echo ERROR: Failed to activate venv >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)
echo Virtual environment activated successfully >> "%LOG_FILE%"

set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set KMP_DUPLICATE_LIB_OK=TRUE

REM Set SSL certificate file for urllib (fixes certificate errors on some systems)
for /f "delims=" %%i in ('python -c "import certifi; print(certifi.where())" 2^>nul') do set "SSL_CERT_FILE=%%i"
set "SSL_CERT_DIR="

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

REM Write service_install_version to file to track successful installation
if not "!SERVICE_INSTALL_VERSION!"=="" (
    echo !SERVICE_INSTALL_VERSION!> "%SCRIPT_DIR%service_install_version_last_installed.txt"
    echo Wrote service_install_version !SERVICE_INSTALL_VERSION! to service_install_version_last_installed.txt >> "%LOG_FILE%"
)

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
echo Log file: setup_log.txt
echo.
if not "%NOPAUSE%"=="nopause" (
    echo Press any key to exit...
    pause >nul
)
exit /b 0

REM -----------------------------------------------------------------
REM Installation for RTX 30/40 Series
REM -----------------------------------------------------------------
:Install30_40Series
echo Installing dependencies...

echo [1/5] Installing PyTorch with CUDA 11.8...
python -m pip install --upgrade pip >> "%LOG_FILE%" 2>&1
python -m pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu118 >> "%LOG_FILE%" 2>&1

echo [2/5] Installing core packages...
python -m pip install numpy pillow opencv-python-headless scipy certifi >> "%LOG_FILE%" 2>&1
python -m pip install transformers huggingface-hub tokenizers safetensors >> "%LOG_FILE%" 2>&1
python -m pip install fugashi unidic_lite >> "%LOG_FILE%" 2>&1

echo [3/5] Installing Manga OCR and YOLO...
python -m pip install manga-ocr ultralytics >> "%LOG_FILE%" 2>&1

echo [4/5] Installing API server...
python -m pip install fastapi "uvicorn[standard]" python-multipart pydantic >> "%LOG_FILE%" 2>&1

echo [5/5] Installing Scikit-learn (optional)...
python -m pip install scikit-learn >> "%LOG_FILE%" 2>&1

echo Initializing Manga OCR...
REM Set SSL cert for model downloads (certifi is now installed)
for /f "delims=" %%i in ('python -c "import certifi; print(certifi.where())"') do set "SSL_CERT_FILE=%%i"
python -c "import ssl, certifi; ssl._create_default_https_context = lambda: ssl.create_default_context(cafile=certifi.where()); from manga_ocr import MangaOcr; MangaOcr()" >> "%LOG_FILE%" 2>&1

echo Installation completed for RTX 30/40 series
goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 50 Series (PyTorch Nightly with CUDA 12.8)
REM -----------------------------------------------------------------
:Install50Series
echo Installing dependencies...

echo [1/7] Installing pip and core packages first...
python -m pip install --upgrade pip >> "%LOG_FILE%" 2>&1
python -m pip install numpy==2.1.3 pillow opencv-python scipy tqdm pyyaml requests certifi >> "%LOG_FILE%" 2>&1

echo [2/7] Installing PyTorch nightly with CUDA 12.8...
python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 >> "%LOG_FILE%" 2>&1

echo [3/7] Installing transformers and HuggingFace dependencies...
python -m pip install transformers==4.47.1 huggingface-hub tokenizers safetensors >> "%LOG_FILE%" 2>&1
python -m pip install fugashi unidic_lite >> "%LOG_FILE%" 2>&1

echo [4/7] Installing Manga OCR (without deps to preserve torch nightly)...
python -m pip install manga-ocr --no-deps >> "%LOG_FILE%" 2>&1
python -m pip install fire jaconv loguru pyperclip >> "%LOG_FILE%" 2>&1

echo [5/7] Installing Ultralytics (without deps to preserve torch nightly)...
python -m pip install ultralytics --no-deps >> "%LOG_FILE%" 2>&1
python -m pip install matplotlib psutil polars ultralytics-thop >> "%LOG_FILE%" 2>&1

echo [6/7] Reinstalling PyTorch nightly to ensure correct version...
python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 --force-reinstall --no-deps >> "%LOG_FILE%" 2>&1

echo [7/7] Installing API server and optional packages...
python -m pip install fastapi "uvicorn[standard]" python-multipart >> "%LOG_FILE%" 2>&1
python -m pip install scikit-learn >> "%LOG_FILE%" 2>&1

echo Initializing Manga OCR...
REM Set SSL cert for model downloads (certifi is now installed)
for /f "delims=" %%i in ('python -c "import certifi; print(certifi.where())"') do set "SSL_CERT_FILE=%%i"
python -c "import ssl, certifi; ssl._create_default_https_context = lambda: ssl.create_default_context(cafile=certifi.where()); from manga_ocr import MangaOcr; MangaOcr()" >> "%LOG_FILE%" 2>&1

echo Installation completed for RTX 50 series
goto :eof

