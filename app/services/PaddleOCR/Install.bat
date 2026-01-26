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
REM Parse service_config.txt to get environment name and service name
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

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find venv_name in service_config.txt
    echo ERROR: Could not find venv_name in service_config.txt >> "%LOG_FILE%"
    pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

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

set "GPU_NAME=Unknown"
set "GPU_SERIES=UNSUPPORTED"

nvidia-smi --query-gpu=name --format=csv,noheader >"%SCRIPT_DIR%gpu_check_temp.txt" 2>nul
if exist "%SCRIPT_DIR%gpu_check_temp.txt" (
    set /p GPU_NAME=<"%SCRIPT_DIR%gpu_check_temp.txt"
    del "%SCRIPT_DIR%gpu_check_temp.txt" >nul 2>&1
)

if not "!GPU_NAME!"=="Unknown" (
    echo Detected NVIDIA GPU: !GPU_NAME!
    echo GPU Detected: !GPU_NAME! >> "%LOG_FILE%"
    
    REM Check for RTX 50 series
    echo !GPU_NAME! | find /i "RTX 50" >nul && set "GPU_SERIES=50"
    
    REM Check for RTX 2000/3000/4000 series
    if "!GPU_SERIES!"=="UNSUPPORTED" (
        echo !GPU_NAME! | find /i "RTX 20" >nul && set "GPU_SERIES=30_40"
        echo !GPU_NAME! | find /i "RTX 30" >nul && set "GPU_SERIES=30_40"
        echo !GPU_NAME! | find /i "RTX 40" >nul && set "GPU_SERIES=30_40"
    )
    echo GPU Series: !GPU_SERIES! >> "%LOG_FILE%"
) else (
    echo WARNING: Unable to detect NVIDIA GPU via nvidia-smi
    echo WARNING: GPU not detected >> "%LOG_FILE%"
    echo Attempting to continue with RTX 3000/4000 series configuration...
    set "GPU_SERIES=30_40"
    echo Defaulting to GPU Series: 30_40 >> "%LOG_FILE%"
)

:SkipGPUDetect

echo.
echo. >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Set Python version based on GPU series
REM -----------------------------------------------------------------
set "PYTHON_VERSION=3.10"
REM PaddlePaddle 2.x/3.x works well with 3.10. 50 series might prefer newer python, but 3.10 is safe.
REM If 50 series requires newer python for PaddlePaddle, we can adjust.
REM For now, sticking to 3.10 for compatibility unless we know 50 series needs 3.11+.
REM The plan says 50 series (targeting CUDA 12.x) via paddlepaddle-gpu.
REM Let's use 3.10 for 30/40 and 3.11 for 50 series to match EasyOCR logic which seemed to imply 50 series needs newer stack.
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
echo [1/4] Removing existing virtual environment if present...
echo [1/4] Removing existing venv... >> "%LOG_FILE%"

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
echo [2/4] Creating new virtual environment...
echo [2/4] Creating new venv... >> "%LOG_FILE%"
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
echo [3/4] Activating virtual environment...
echo [3/4] Activating venv... >> "%LOG_FILE%"

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
REM Install dependencies based on GPU series
REM -----------------------------------------------------------------
echo [4/4] Installing dependencies for GPU series: !GPU_SERIES!
echo [4/4] Installing dependencies for GPU series: !GPU_SERIES! >> "%LOG_FILE%"

if "!GPU_SERIES!"=="50" (
    echo.
    echo Installing for RTX 50 series with PaddlePaddle CUDA 12...
    echo Installing for RTX 50 series >> "%LOG_FILE%"
    echo Calling Install50Series function... >> "%LOG_FILE%"
    call :Install50Series
    if errorlevel 1 (
        echo ERROR: Install50Series failed with error level !errorlevel! >> "%LOG_FILE%"
        pause
        exit /b 1
    )
    echo Install50Series completed successfully >> "%LOG_FILE%"
    goto :SetupComplete
)

if "!GPU_SERIES!"=="30_40" (
    echo.
    echo Installing for RTX 30/40 series with PaddlePaddle CUDA 11.8...
    echo Installing for RTX 30/40 series >> "%LOG_FILE%"
    echo Calling Install30_40Series function... >> "%LOG_FILE%"
    call :Install30_40Series
    if errorlevel 1 (
        echo ERROR: Install30_40Series failed with error level !errorlevel! >> "%LOG_FILE%"
        pause
        exit /b 1
    )
    echo Install30_40Series completed successfully >> "%LOG_FILE%"
    goto :SetupComplete
)

REM If we get here, unsupported GPU
echo ERROR: Unsupported GPU series: !GPU_SERIES! >> "%LOG_FILE%"
goto :FailGPU

:SetupComplete

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
REM Installation for RTX 30/40 Series (CUDA 11.8)
REM -----------------------------------------------------------------
:Install30_40Series
echo.
echo ============================================================
echo Installing dependencies for RTX 30/40 Series
echo ============================================================
echo.

echo [Step 1/5] Upgrading pip...
python -m pip install --upgrade pip setuptools wheel >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to upgrade pip
    pause
    exit /b 1
)

echo [Step 2/5] Installing PaddlePaddle GPU (CUDA 11.8)...
echo [Step 2/5] Installing PaddlePaddle GPU (CUDA 11.8)... >> "%LOG_FILE%"
python -m pip install paddlepaddle-gpu==3.2.1 -i https://www.paddlepaddle.org.cn/packages/stable/cu118/ >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PaddlePaddle
    echo ERROR: Failed to install PaddlePaddle >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PaddlePaddle installed successfully >> "%LOG_FILE%"

echo [Step 3/5] Installing PaddleOCR...
echo [Step 3/5] Installing PaddleOCR... >> "%LOG_FILE%"
REM Installing paddleocr from PyPI
python -m pip install paddleocr>=2.9.1 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PaddleOCR
    echo ERROR: Failed to install PaddleOCR >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PaddleOCR installed successfully >> "%LOG_FILE%"

echo [Step 4/5] Installing FastAPI and Uvicorn...
echo [Step 4/5] Installing FastAPI/Uvicorn... >> "%LOG_FILE%"
python -m pip install fastapi==0.115.6 "uvicorn[standard]==0.32.1" python-multipart==0.0.17 certifi >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install FastAPI/Uvicorn
    echo ERROR: Failed to install FastAPI/Uvicorn >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI/Uvicorn installed successfully >> "%LOG_FILE%"

echo [Step 5/5] Verifying installation...
echo [Step 5/5] Verifying installation... >> "%LOG_FILE%"
python -c "import paddle; print('PaddlePaddle:', paddle.__version__); print('CUDA Available:', paddle.device.is_compiled_with_cuda())" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PaddlePaddle verification failed
    echo ERROR: PaddlePaddle verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)

python -c "import paddleocr; print('PaddleOCR imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PaddleOCR verification failed
    echo ERROR: PaddleOCR verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)

goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 50 Series (CUDA 12.x)
REM -----------------------------------------------------------------
:Install50Series
echo.
echo ============================================================
echo Installing dependencies for RTX 50 Series
echo ============================================================
echo.

echo [Step 1/5] Upgrading pip...
echo [Step 1/5] Upgrading pip... >> "%LOG_FILE%"
python -m pip install --upgrade pip setuptools wheel >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to upgrade pip
    echo ERROR: Failed to upgrade pip >> "%LOG_FILE%"
    pause
    exit /b 1
)

echo [Step 2/5] Installing PaddlePaddle GPU (CUDA 12.9)...
echo [Step 2/5] Installing PaddlePaddle GPU (CUDA 12.9)... >> "%LOG_FILE%"
REM Using CUDA 12.9 for RTX 50 series (compute capability 12.0) compatibility
python -m pip install paddlepaddle-gpu==3.2.2 -i https://www.paddlepaddle.org.cn/packages/stable/cu129/ >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PaddlePaddle CUDA 12.9
    echo ERROR: Failed to install PaddlePaddle CUDA 12.9 >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PaddlePaddle installed successfully >> "%LOG_FILE%"

echo [Step 3/5] Installing PaddleOCR...
echo [Step 3/5] Installing PaddleOCR... >> "%LOG_FILE%"
python -m pip install paddleocr>=2.9.1 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PaddleOCR
    echo ERROR: Failed to install PaddleOCR >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PaddleOCR installed successfully >> "%LOG_FILE%"

echo [Step 4/5] Installing FastAPI and Uvicorn...
echo [Step 4/5] Installing FastAPI/Uvicorn... >> "%LOG_FILE%"
python -m pip install fastapi "uvicorn[standard]" python-multipart certifi >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install FastAPI/Uvicorn
    echo ERROR: Failed to install FastAPI/Uvicorn >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI/Uvicorn installed successfully >> "%LOG_FILE%"

echo [Step 5/5] Verifying installation...
echo [Step 5/5] Verifying installation... >> "%LOG_FILE%"
python -c "import paddle; print('PaddlePaddle:', paddle.__version__); print('CUDA Available:', paddle.device.is_compiled_with_cuda())" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PaddlePaddle verification failed
    echo ERROR: PaddlePaddle verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)

python -c "import paddleocr; print('PaddleOCR imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PaddleOCR verification failed
    echo ERROR: PaddleOCR verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)

goto :eof

REM -----------------------------------------------------------------
REM Error handler for unsupported GPU
REM -----------------------------------------------------------------
:FailGPU
echo. >> "%LOG_FILE%"
echo ERROR: Unsupported GPU series >> "%LOG_FILE%"
echo.
echo ===== ERROR: Unsupported GPU =====
echo.
echo This setup currently supports:
echo   - NVIDIA RTX 30/40 series
echo   - NVIDIA RTX 50 series
echo.
echo If you have one of these GPUs but it wasn't detected correctly,
echo please ensure your NVIDIA drivers are installed and up to date.
echo.
echo Check setup_log.txt for details.
echo.
pause
exit /b 1

