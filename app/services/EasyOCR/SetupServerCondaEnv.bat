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
REM Parse service_config.txt to get environment name and service name
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

if "!ENV_NAME!"=="" (
    echo ERROR: Could not find conda_env_name in service_config.txt
    echo ERROR: Could not find conda_env_name in service_config.txt >> "%LOG_FILE%"
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
echo   Conda Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Validate conda availability
REM -----------------------------------------------------------------
echo Validating conda installation... >> "%LOG_FILE%"
where conda >nul 2>nul
if errorlevel 1 (
    echo.
    echo ===== ERROR: Conda is not installed or not in PATH =====
    echo ERROR: Conda not found in PATH >> "%LOG_FILE%"
    echo.
    echo Conda is required to set up the OCR environment.
    echo Please run the InstallMiniConda.bat script from the util folder.
    echo.
    pause
    exit /b 1
)
echo Conda found successfully >> "%LOG_FILE%"

echo Detecting GPU...
echo.
echo Detecting GPU... >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Detect GPU model
REM -----------------------------------------------------------------
set "GPU_NAME=Unknown"
set "GPU_SERIES=UNSUPPORTED"

nvidia-smi --query-gpu=name --format=csv,noheader >"%TEMP%\gpu_check.txt" 2>nul
if exist "%TEMP%\gpu_check.txt" (
    set /p GPU_NAME=<"%TEMP%\gpu_check.txt"
    del "%TEMP%\gpu_check.txt" >nul 2>&1
)

if not "!GPU_NAME!"=="Unknown" (
    echo Detected NVIDIA GPU: !GPU_NAME!
    echo GPU Detected: !GPU_NAME! >> "%LOG_FILE%"
    
    REM Check for RTX 50 series
    echo !GPU_NAME! | find /i "RTX 50" >nul && set "GPU_SERIES=50"
    
    REM Check for RTX 3000/4000 series
    if "!GPU_SERIES!"=="UNSUPPORTED" (
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

echo.
echo. >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Activate base environment
REM -----------------------------------------------------------------
echo [1/6] Activating conda base environment...
echo [1/6] Activating conda base environment... >> "%LOG_FILE%"
call conda activate base >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: Failed to activate conda base environment!
    echo ERROR: Failed to activate conda base >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)
echo Successfully activated base environment >> "%LOG_FILE%"

echo [2/6] Accepting conda Terms of Service...
echo [2/6] Accepting conda ToS... >> "%LOG_FILE%"
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/main 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/r 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/msys2 2>nul
echo Conda ToS accepted >> "%LOG_FILE%"

echo [3/6] Updating conda (this may take a minute)...
echo [3/6] Updating conda... >> "%LOG_FILE%"
call conda update -y conda >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo WARNING: Conda update failed, continuing anyway...
    echo WARNING: Conda update failed >> "%LOG_FILE%"
) else (
    echo Conda updated successfully >> "%LOG_FILE%"
)

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo [4/6] Checking for existing environment...
echo [4/6] Checking for existing environment... >> "%LOG_FILE%"
conda env list | findstr /C:"!ENV_NAME!" >nul 2>nul
if !errorlevel! equ 0 (
    echo Found existing "!ENV_NAME!" environment. Removing it...
    echo Found existing environment, removing... >> "%LOG_FILE%"
    call conda env remove -n "!ENV_NAME!" -y >> "%LOG_FILE%" 2>&1
    if errorlevel 1 (
        echo.
        echo ERROR: Failed to remove existing environment "!ENV_NAME!"
        echo ERROR: Failed to remove existing environment >> "%LOG_FILE%"
        echo Please close any terminals that have this environment activated and try again.
        echo.
        pause
        exit /b 1
    )
    echo Successfully removed old environment.
    echo Successfully removed old environment >> "%LOG_FILE%"
) else (
    echo No existing environment found.
    echo No existing environment found >> "%LOG_FILE%"
)

echo [5/6] Creating new conda environment...
echo [5/6] Creating new conda environment... >> "%LOG_FILE%"

REM Set Python version based on GPU series
set "PYTHON_VERSION=3.10"
if "!GPU_SERIES!"=="50" set "PYTHON_VERSION=3.11"

echo Python version: !PYTHON_VERSION! >> "%LOG_FILE%"
echo GPU Series !GPU_SERIES! requires Python !PYTHON_VERSION!
echo.

echo Creating environment "!ENV_NAME!" with Python !PYTHON_VERSION!...
echo Creating environment "!ENV_NAME!" with Python !PYTHON_VERSION!... >> "%LOG_FILE%"
call conda create -y --name "!ENV_NAME!" python=!PYTHON_VERSION! >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: Failed to create conda environment "!ENV_NAME!"
    echo ERROR: Failed to create conda environment >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)
echo Environment created successfully >> "%LOG_FILE%"

echo [6/6] Activating new environment...
echo [6/6] Activating new environment... >> "%LOG_FILE%"
call conda activate "!ENV_NAME!" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo.
    echo ERROR: Failed to activate environment "!ENV_NAME!"
    echo ERROR: Failed to activate new environment >> "%LOG_FILE%"
    echo.
    pause
    exit /b 1
)
echo Environment activated successfully >> "%LOG_FILE%"

set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set KMP_DUPLICATE_LIB_OK=TRUE
echo Environment variables set >> "%LOG_FILE%"

REM -----------------------------------------------------------------
REM Install dependencies based on GPU series
REM -----------------------------------------------------------------
echo. >> "%LOG_FILE%"
echo Installing dependencies for GPU series: !GPU_SERIES! >> "%LOG_FILE%"

if "!GPU_SERIES!"=="50" (
    echo.
    echo Installing for RTX 50 series with PyTorch nightly CUDA 12.8...
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
    echo Installing for RTX 30/40 series with PyTorch 2.6 CUDA 11.8...
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
pause >nul
exit /b 0

REM -----------------------------------------------------------------
REM Installation for RTX 30/40 Series
REM -----------------------------------------------------------------
:Install30_40Series
echo.
echo ============================================================
echo Installing dependencies for RTX 30/40 Series
echo ============================================================
echo.

set "PYTORCH_CHANNEL=https://download.pytorch.org/whl/cu118"
set "TORCH_VERSION=2.6.0+cu118"
set "TORCHVISION_VERSION=0.21.0+cu118"
set "TORCHAUDIO_VERSION=2.6.0+cu118"

echo [Step 1/7] Upgrading pip...
python -m pip install --upgrade pip setuptools wheel
if errorlevel 1 (
    echo ERROR: Failed to upgrade pip!
    pause
    exit /b 1
)

echo [Step 2/7] Installing PyTorch 2.6 GPU stack (CUDA 11.8)...
echo [Step 2/7] Installing PyTorch 2.6 (CUDA 11.8)... >> "%LOG_FILE%"
python -m pip install --extra-index-url !PYTORCH_CHANNEL! torch==!TORCH_VERSION! torchvision==!TORCHVISION_VERSION! torchaudio==!TORCHAUDIO_VERSION! >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PyTorch!
    echo ERROR: Failed to install PyTorch >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PyTorch installed successfully >> "%LOG_FILE%"

echo [Step 3/7] Installing core dependencies...
echo [Step 3/7] Installing core dependencies... >> "%LOG_FILE%"
python -m pip install numpy==2.0.2 pillow==11.3.0 "opencv-python-headless>=4.10,<4.12" scipy==1.13.1 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install core dependencies!
    echo ERROR: Failed to install core dependencies >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo Core dependencies installed successfully >> "%LOG_FILE%"

echo [Step 4/7] Installing EasyOCR...
echo [Step 4/7] Installing EasyOCR... >> "%LOG_FILE%"
python -m pip install --extra-index-url !PYTORCH_CHANNEL! easyocr==1.7.2 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install EasyOCR!
    echo ERROR: Failed to install EasyOCR >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo EasyOCR installed successfully >> "%LOG_FILE%"

echo [Step 5/7] Installing FastAPI and Uvicorn...
echo [Step 5/7] Installing FastAPI/Uvicorn... >> "%LOG_FILE%"
python -m pip install fastapi==0.121.1 "uvicorn[standard]==0.38.0" python-multipart==0.0.20 pydantic==2.10.5 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install FastAPI/Uvicorn!
    echo ERROR: Failed to install FastAPI/Uvicorn >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI/Uvicorn installed successfully >> "%LOG_FILE%"

echo [Step 6/7] Installing CuPy for GPU color extraction...
echo [Step 6/7] Installing CuPy... >> "%LOG_FILE%"
python -m pip install cupy-cuda12x==13.6.0 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo WARNING: CuPy installation failed, color extraction will use CPU
    echo WARNING: CuPy installation failed >> "%LOG_FILE%"
) else (
    echo CuPy installed successfully >> "%LOG_FILE%"
)

echo [Step 7/7] Pre-downloading EasyOCR models...
echo This may take a few minutes...
echo [Step 7/7] Pre-downloading EasyOCR models... >> "%LOG_FILE%"
python -c "import easyocr; easyocr.Reader(['ja','en'])" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo WARNING: Failed to pre-download EasyOCR models
    echo WARNING: Failed to pre-download models >> "%LOG_FILE%"
) else (
    echo Models downloaded successfully >> "%LOG_FILE%"
)

echo.
echo Verifying installation...
echo Verifying installation... >> "%LOG_FILE%"
python -c "import torch; print('PyTorch:', torch.__version__); print('CUDA Available:', torch.cuda.is_available())" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PyTorch verification failed!
    echo ERROR: PyTorch verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PyTorch verification passed >> "%LOG_FILE%"

python -c "import easyocr; print('EasyOCR imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: EasyOCR verification failed!
    echo ERROR: EasyOCR verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo EasyOCR verification passed >> "%LOG_FILE%"

python -c "import fastapi; print('FastAPI imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: FastAPI verification failed!
    echo ERROR: FastAPI verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI verification passed >> "%LOG_FILE%"

goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 50 Series (PyTorch Nightly with CUDA 12.8)
REM -----------------------------------------------------------------
:Install50Series
echo.
echo ============================================================
echo Installing dependencies for RTX 50 Series
echo ============================================================
echo.

echo [Step 1/7] Upgrading pip...
echo [Step 1/7] Upgrading pip... >> "%LOG_FILE%"
python -m pip install --upgrade pip setuptools wheel >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to upgrade pip!
    echo ERROR: Failed to upgrade pip >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo Pip upgraded successfully >> "%LOG_FILE%"

echo [Step 2/7] Installing PyTorch nightly with CUDA 12.8 support...
echo This may take several minutes...
echo [Step 2/7] Installing PyTorch nightly (CUDA 12.8)... >> "%LOG_FILE%"
python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install PyTorch nightly!
    echo ERROR: Failed to install PyTorch nightly >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PyTorch nightly installed successfully >> "%LOG_FILE%"

echo [Step 3/7] Installing core dependencies...
echo [Step 3/7] Installing core dependencies... >> "%LOG_FILE%"
python -m pip install numpy pillow opencv-python scipy tqdm pyyaml requests >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install core dependencies!
    echo ERROR: Failed to install core dependencies >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo Core dependencies installed successfully >> "%LOG_FILE%"

echo [Step 4/7] Installing EasyOCR...
echo [Step 4/7] Installing EasyOCR... >> "%LOG_FILE%"
python -m pip install easyocr >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install EasyOCR!
    echo ERROR: Failed to install EasyOCR >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo EasyOCR installed successfully >> "%LOG_FILE%"

echo [Step 5/7] Installing FastAPI and Uvicorn...
echo [Step 5/7] Installing FastAPI/Uvicorn... >> "%LOG_FILE%"
python -m pip install fastapi "uvicorn[standard]" python-multipart >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: Failed to install FastAPI/Uvicorn!
    echo ERROR: Failed to install FastAPI/Uvicorn >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI/Uvicorn installed successfully >> "%LOG_FILE%"

echo [Step 6/7] Installing CuPy for GPU color extraction...
echo [Step 6/7] Installing CuPy... >> "%LOG_FILE%"
python -m pip install cupy-cuda12x >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo WARNING: CuPy installation failed, color extraction will use CPU
    echo WARNING: CuPy installation failed >> "%LOG_FILE%"
) else (
    echo CuPy installed successfully >> "%LOG_FILE%"
)

echo [Step 7/7] Pre-downloading EasyOCR models...
echo This may take a few minutes...
echo [Step 7/7] Pre-downloading EasyOCR models... >> "%LOG_FILE%"
python -c "import easyocr; easyocr.Reader(['ja','en'])" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo WARNING: Failed to pre-download EasyOCR models
    echo WARNING: Failed to pre-download models >> "%LOG_FILE%"
) else (
    echo Models downloaded successfully >> "%LOG_FILE%"
)

echo.
echo Verifying installation...
echo Verifying installation... >> "%LOG_FILE%"
python -c "import torch; print('PyTorch:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: PyTorch verification failed!
    echo ERROR: PyTorch verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo PyTorch verification passed >> "%LOG_FILE%"

python -c "import easyocr; print('EasyOCR imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: EasyOCR verification failed!
    echo ERROR: EasyOCR verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo EasyOCR verification passed >> "%LOG_FILE%"

python -c "import fastapi; print('FastAPI imported successfully')" >> "%LOG_FILE%" 2>&1
if errorlevel 1 (
    echo ERROR: FastAPI verification failed!
    echo ERROR: FastAPI verification failed >> "%LOG_FILE%"
    pause
    exit /b 1
)
echo FastAPI verification passed >> "%LOG_FILE%"

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
echo   - NVIDIA RTX 3050, 3060, 3070, 3080, 3090
echo   - NVIDIA RTX 4060, 4070, 4080, 4090
echo   - NVIDIA RTX 5050, 5060, 5070, 5080, 5090
echo.
echo If you have one of these GPUs but it wasn't detected correctly,
echo please ensure your NVIDIA drivers are installed and up to date.
echo.
echo Check setup_conda_log.txt for details.
echo.
pause
exit /b 1

