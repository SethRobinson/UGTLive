@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"

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
    pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   !SERVICE_NAME! Environment Setup
echo   Conda Environment: !ENV_NAME!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Validate conda availability
REM -----------------------------------------------------------------
where conda >nul 2>nul
if errorlevel 1 (
    echo.
    echo ===== ERROR: Conda is not installed or not in PATH =====
    echo.
    echo Conda is required to set up the OCR environment.
    echo Please run the InstallMiniConda.bat script from the util folder.
    echo.
    pause
    exit /b 1
)

echo Detecting GPU...
echo.

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
    
    REM Check for RTX 50 series
    echo !GPU_NAME! | find /i "RTX 50" >nul && set "GPU_SERIES=50"
    
    REM Check for RTX 3000/4000 series
    if "!GPU_SERIES!"=="UNSUPPORTED" (
        echo !GPU_NAME! | find /i "RTX 30" >nul && set "GPU_SERIES=30_40"
        echo !GPU_NAME! | find /i "RTX 40" >nul && set "GPU_SERIES=30_40"
    )
) else (
    echo WARNING: Unable to detect NVIDIA GPU via nvidia-smi
    echo Attempting to continue with RTX 3000/4000 series configuration...
    set "GPU_SERIES=30_40"
)

echo.

REM -----------------------------------------------------------------
REM Activate base environment
REM -----------------------------------------------------------------
echo Activating conda base environment...
call conda activate base || goto :FailActivateBase

echo Accepting conda Terms of Service...
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/main 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/r 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/msys2 2>nul

echo Updating conda...
call conda update -y conda >nul

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo Removing existing !ENV_NAME! environment if it exists...
call conda env remove -n !ENV_NAME! -y >nul 2>nul

echo Creating new conda environment (python=3.10)...
call conda create -y --name !ENV_NAME! python=3.10 || goto :FailCreateEnv

echo Activating !ENV_NAME! environment...
call conda activate !ENV_NAME! || goto :FailActivateEnv

set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set KMP_DUPLICATE_LIB_OK=TRUE

REM -----------------------------------------------------------------
REM Install dependencies based on GPU series
REM -----------------------------------------------------------------
if "!GPU_SERIES!"=="50" (
    echo Installing for RTX 50 series...
    call :Install50Series
) else if "!GPU_SERIES!"=="30_40" (
    echo Installing for RTX 30/40 series...
    call :Install30_40Series
) else (
    echo ERROR: Unsupported GPU series
    goto :FailGPU
)

echo.
echo =============================================================
echo   Setup complete for !SERVICE_NAME!!
echo   Environment: !ENV_NAME!
echo   The service is ready to use.
echo =============================================================
echo.
pause
goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 30/40 Series
REM -----------------------------------------------------------------
:Install30_40Series
set "PYTORCH_CHANNEL=https://download.pytorch.org/whl/cu118"
set "TORCH_VERSION=2.6.0+cu118"
set "TORCHVISION_VERSION=0.21.0+cu118"
set "TORCHAUDIO_VERSION=2.6.0+cu118"

echo Upgrading pip...
python -m pip install --upgrade pip setuptools wheel || goto :FailPip

echo Installing PyTorch 2.6 GPU stack (cu118)...
python -m pip install --extra-index-url !PYTORCH_CHANNEL! torch==!TORCH_VERSION! torchvision==!TORCHVISION_VERSION! torchaudio==!TORCHAUDIO_VERSION! || goto :FailTorch

echo Installing core dependencies...
python -m pip install numpy==2.0.2 pillow==11.3.0 "opencv-python-headless>=4.10,<4.12" scipy==1.13.1 || goto :FailDeps

echo Installing EasyOCR...
python -m pip install --extra-index-url !PYTORCH_CHANNEL! easyocr==1.7.2 || goto :FailEasyOCR

echo Installing FastAPI and Uvicorn...
python -m pip install fastapi==0.121.1 "uvicorn[standard]==0.38.0" python-multipart==0.0.20 pydantic==2.10.5 || goto :FailFastAPI

echo Installing CuPy for GPU color extraction...
python -m pip install cupy-cuda12x==13.6.0 || echo WARNING: CuPy installation failed, color extraction will use CPU

echo Pre-downloading EasyOCR models (ja + en)...
python -c "import easyocr; easyocr.Reader(['ja','en'])" 1>nul 2>nul
if errorlevel 1 echo WARNING: Failed to pre-download EasyOCR models

echo Verifying installation...
python -c "import torch; print('PyTorch:', torch.__version__); print('CUDA Available:', torch.cuda.is_available())" || goto :FailVerify
python -c "import easyocr; print('EasyOCR imported successfully')" || goto :FailVerify
python -c "import fastapi; print('FastAPI imported successfully')" || goto :FailVerify

goto :eof

REM -----------------------------------------------------------------
REM Installation for RTX 50 Series
REM -----------------------------------------------------------------
:Install50Series
REM RTX 50 series would use newer CUDA version if needed
REM For now, use same as 30/40 series
call :Install30_40Series
goto :eof

REM -----------------------------------------------------------------
REM Error handlers
REM -----------------------------------------------------------------
:FailActivateBase
echo ERROR: Failed to activate conda base environment!
pause
exit /b 1

:FailCreateEnv
echo ERROR: Failed to create conda environment!
pause
exit /b 1

:FailActivateEnv
echo ERROR: Failed to activate !ENV_NAME! environment!
pause
exit /b 1

:FailPip
echo ERROR: Failed to upgrade pip!
pause
exit /b 1

:FailTorch
echo ERROR: Failed to install PyTorch!
pause
exit /b 1

:FailDeps
echo ERROR: Failed to install core dependencies!
pause
exit /b 1

:FailEasyOCR
echo ERROR: Failed to install EasyOCR!
pause
exit /b 1

:FailFastAPI
echo ERROR: Failed to install FastAPI/Uvicorn!
pause
exit /b 1

:FailVerify
echo ERROR: Installation verification failed!
pause
exit /b 1

:FailGPU
echo ERROR: Unsupported GPU series!
pause
exit /b 1

