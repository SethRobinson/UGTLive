@echo off
setlocal ENABLEDELAYEDEXPANSION

echo =============================================================
echo   UGTLive OCR Environment Setup
echo =============================================================
echo.

set "SCRIPT_DIR=%~dp0"

REM -----------------------------------------------------------------
REM Validate conda availability
REM -----------------------------------------------------------------
where conda >nul 2>nul
if errorlevel 1 (
    echo.
    echo ===== ERROR: Conda is not installed or not in PATH =====
    echo.
    echo Conda is required to set up the OCR environment.
    echo.
    echo If you just installed Miniconda, please:
    echo   1. Close UGTLive completely
    echo   2. Restart UGTLive
    echo   3. The app will detect conda and allow you to continue setup
    echo.
    echo If conda is not installed:
    echo   Use the "Install Miniconda" button in the UGTLive Server Setup window.
    echo   After installation, close and restart UGTLive for PATH changes to take effect.
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

REM Try to detect GPU name
nvidia-smi --query-gpu=name --format=csv,noheader >"%TEMP%\gpu_check.txt" 2>nul
if exist "%TEMP%\gpu_check.txt" (
    set /p GPU_NAME=<"%TEMP%\gpu_check.txt"
    del "%TEMP%\gpu_check.txt" >nul 2>&1
)

if not "!GPU_NAME!"=="Unknown" (
    echo Detected NVIDIA GPU: !GPU_NAME!
    
    REM Check for RTX 50 series
    if "!GPU_SERIES!"=="UNSUPPORTED" (
        echo !GPU_NAME! | find /i "RTX 50" >nul && set "GPU_SERIES=50"
    )
    
    REM Check for RTX 3000/4000 series
    if "!GPU_SERIES!"=="UNSUPPORTED" (
        echo !GPU_NAME! | find /i "RTX 30" >nul && set "GPU_SERIES=30_40"
        echo !GPU_NAME! | find /i "RTX 40" >nul && set "GPU_SERIES=30_40"
    )
) else (
    echo WARNING: Unable to detect NVIDIA GPU via nvidia-smi
    echo.
    echo Attempting to continue with RTX 3000/4000 series configuration...
    set "GPU_SERIES=30_40"
)

echo.

REM -----------------------------------------------------------------
REM Call appropriate setup script
REM -----------------------------------------------------------------
if "!GPU_SERIES!"=="50" (
    echo Detected RTX 50 series - using specialized setup...
    echo.
    if exist "%SCRIPT_DIR%_NVidia50Series.bat" (
        call "%SCRIPT_DIR%_NVidia50Series.bat"
        exit /b %errorlevel%
    ) else (
        echo ERROR: RTX 50 series setup script not found!
        pause
        exit /b 1
    )
) else if "!GPU_SERIES!"=="30_40" (
    echo Using RTX 3000/4000 series setup...
    echo.
    if exist "%SCRIPT_DIR%_NVidia30And40Series.bat" (
        call "%SCRIPT_DIR%_NVidia30And40Series.bat"
        exit /b %errorlevel%
    ) else (
        echo ERROR: RTX 30/40 series setup script not found!
        pause
        exit /b 1
    )
) else (
    echo.
    echo ===== ERROR: Unsupported GPU =====
    echo.
    echo GPU Detected: !GPU_NAME!
    echo.
    echo This setup currently supports:
    echo   - NVIDIA RTX 3050, 3060, 3070, 3080, 3090
    echo   - NVIDIA RTX 4060, 4070, 4080, 4090  
    echo   - NVIDIA RTX 5050, 5060, 5070, 5080, 5090
    echo.
    echo If you have one of these GPUs but it wasn't detected correctly,
    echo please ensure your NVIDIA drivers are installed and up to date.
    echo.
    pause
    exit /b 1
)
