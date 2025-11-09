@echo off
setlocal ENABLEDELAYEDEXPANSION

echo =============================================================
echo   Setting up UGTLive OCR environment (EasyOCR + MangaOCR)
echo -------------------------------------------------------------
echo   This creates (or recreates) the "ocrstuff" Conda environment
echo   with GPU acceleration, Foreground/Background color analysis,
echo   and Manga109 YOLO region detection assets.
echo =============================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "MODEL_DIR=%SCRIPT_DIR%models\manga109_yolo"
set "MODEL_FILE=%MODEL_DIR%\model.pt"
set "LABELS_FILE=%MODEL_DIR%\labels.json"
set "MODEL_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/model.pt"
set "LABELS_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/labels.json"

REM -----------------------------------------------------------------
REM Validate conda availability
REM -----------------------------------------------------------------
where call conda >nul 2>nul || goto :NoConda

echo Conda detected successfully!
echo.

REM -----------------------------------------------------------------
REM Warn if conda base path contains spaces
REM -----------------------------------------------------------------
for /f "usebackq tokens=* delims=" %%B in (`conda info --base 2^>nul`) do (
    set "CONDA_BASE=%%B"
)
if defined CONDA_BASE (
    if not "!CONDA_BASE!"=="!CONDA_BASE: =!" (
        echo WARNING: Conda installation path contains spaces: "!CONDA_BASE!"
        echo Some tools may warn; paths are quoted in this setup.
    )
)

REM -----------------------------------------------------------------
REM Detect GPU model to choose CUDA toolchain
REM -----------------------------------------------------------------
set "GPU_NAME="
for /f "usebackq skip=1 tokens=* delims=" %%i in (`nvidia-smi --query-gpu=name --format=csv 2^>nul`) do (
    if not defined GPU_NAME set "GPU_NAME=%%i"
)
if not defined GPU_NAME (
    for /f "usebackq tokens=* delims=" %%i in (`nvidia-smi --query-gpu=name --format=csv,noheader 2^>nul`) do (
        if not defined GPU_NAME set "GPU_NAME=%%i"
    )
)
if defined GPU_NAME (
    echo !GPU_NAME! | findstr /I /B /C:"ERROR" >nul 2>&1 && set "GPU_NAME="
)

set "IS_5090=0"
if defined GPU_NAME (
    echo Detected NVIDIA GPU: !GPU_NAME!
    echo !GPU_NAME! | find "5090" >nul 2>&1 && set "IS_5090=1"
) else (
    echo WARNING: Unable to query GPU name via nvidia-smi; assuming non-RTX 5090 configuration.
)
echo.

if "!IS_5090!"=="1" (
    echo RTX 5090 detected - using CUDA 12.x / PyTorch nightly installation path.
    set "PYTHON_VERSION=3.11"
    set "CUPY_PACKAGE=cupy-cuda12x"
) else (
    echo Using standard CUDA 11.x configuration.
    set "PYTHON_VERSION=3.9"
    set "CUPY_PACKAGE=cupy-cuda12x"
)
echo.

REM -----------------------------------------------------------------
REM Activate base environment and update conda
REM -----------------------------------------------------------------
echo Activating conda base environment...
call conda activate base || goto :FailActivateBase

echo Accepting conda Terms of Service for required channels...
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/main 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/r 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/msys2 2>nul

echo Updating conda...
call conda update -y conda || goto :WarnUpdateConda

:ContinueAfterUpdate
echo Configuring conda channels...
call conda config --add channels conda-forge
call conda config --set channel_priority strict

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo Removing existing ocrstuff environment if it exists...
call conda env remove -n ocrstuff -y

echo Creating new conda environment (python=!PYTHON_VERSION!)...
call conda create -y --name ocrstuff python=!PYTHON_VERSION! || goto :FailCreateEnv

echo Activating ocrstuff environment...
call conda activate ocrstuff || goto :FailActivateOcrstuff

if "!IS_5090!"=="1" (
    set "KMP_DUPLICATE_LIB_OK=TRUE"
)

set HF_HUB_DISABLE_SYMLINKS_WARNING=1

echo Upgrading pip...
python -m pip install --upgrade pip || goto :FailPipUpgrade

REM -----------------------------------------------------------------
REM GPU toolchain and core dependencies
REM -----------------------------------------------------------------
if "!IS_5090!"=="1" (
    echo Installing PyTorch nightly with CUDA 12.x support...
    python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 || goto :FailInstallPyTorch

    echo Installing base scientific Python dependencies via pip...
    python -m pip install opencv-python pillow matplotlib scipy || goto :FailInstallBaseDeps
    python -m pip install tqdm pyyaml requests numpy || goto :FailInstallAdditionalDeps
) else (
    echo Installing PyTorch with CUDA 12.1 support via conda...
    call conda install -y pytorch torchvision torchaudio pytorch-cuda=12.1 -c pytorch -c nvidia || goto :FailInstallPyTorch

    echo Installing conda-forge dependencies...
    call conda install -y -c conda-forge opencv pillow matplotlib scipy || goto :FailInstallCondaForge

    echo Installing additional conda dependencies...
    call conda install -y numpy tqdm pyyaml requests || goto :FailInstallCondaDeps
)

REM -----------------------------------------------------------------
REM OCR and detection dependencies (pip for consistency)
REM -----------------------------------------------------------------
echo Installing EasyOCR...
python -m pip install easyocr || goto :FailInstallEasyOCR

echo Installing Manga OCR...
python -m pip install manga-ocr || goto :FailInstallMangaOCR

echo Installing docTR...
python -m pip install python-doctr || goto :FailInstallDocTR

echo Installing CRAFT Text Detector (with compatible dependencies)...
python -m pip install scikit-image gdown || goto :FailInstallCRAFT
python -m pip install craft-text-detector --no-deps || goto :FailInstallCRAFT

echo Installing CuPy for GPU color extraction...
python -m pip install !CUPY_PACKAGE! || goto :FailInstallCuPy

echo Installing Ultralytics YOLO + FastAPI helpers...
python -m pip install "ultralytics>=8.3.0" fastapi "uvicorn[standard]" python-multipart || goto :FailInstallUltralytics

REM -----------------------------------------------------------------
REM Pre-download OCR models
REM -----------------------------------------------------------------
echo Ensuring PyTorch is version 2.6 or newer...
python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
if errorlevel 1 (
    echo Upgrading to stable PyTorch CUDA 12.1 (torch>=2.6)...
    python -m pip install --upgrade torch>=2.6 torchvision>=0.21 torchaudio>=2.6 --index-url https://download.pytorch.org/whl/cu121
    python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
)
if errorlevel 1 (
    echo Stable CU121 not available; trying PyTorch nightly CUDA 12.8...
    python -m pip install --pre --upgrade torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128
    python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
)
if errorlevel 1 goto :FailInstallPyTorch

echo Pre-downloading EasyOCR language models (ja + en)...
python -c "import easyocr; easyocr.Reader(['ja','en'])" || goto :FailWarmEasyOCR

echo Prefetching Manga OCR safetensors model (best-effort)...
python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','model.safetensors')" >nul 2>&1
if errorlevel 1 (
    echo WARNING: model.safetensors not found or could not be fetched; attempting pytorch_model.bin...
    python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','pytorch_model.bin')" >nul 2>&1
)

echo Initializing Manga OCR (downloads model on first use)...
echo Re-checking PyTorch is v2.6+ before Manga OCR init...
python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
if errorlevel 1 (
    python -m pip install --upgrade torch>=2.6 torchvision>=0.21 torchaudio>=2.6 --index-url https://download.pytorch.org/whl/cu121
    python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
)
if errorlevel 1 (
    python -m pip install --pre --upgrade torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128
    python -c "import torch,sys; v=tuple(int(x) for x in torch.__version__.split('+')[0].split('.')[:2]); sys.exit(0 if v>=(2,6) else 1)"
)
if errorlevel 1 goto :FailInstallPyTorch
python -c "from manga_ocr import MangaOcr; MangaOcr()" || goto :FailWarmManga

echo Initializing docTR (downloads models on first use)...
python -c "from doctr.models import ocr_predictor; ocr_predictor(det_arch='db_resnet50', reco_arch='master', pretrained=True)" || goto :FailWarmDocTR

REM -----------------------------------------------------------------
REM Download Manga109 YOLO model + labels
REM -----------------------------------------------------------------
if not exist "%MODEL_DIR%" (
    echo Creating Manga109 YOLO model directory at "%MODEL_DIR%"...
    mkdir "%MODEL_DIR%" || goto :FailCreateModelDir
)

if exist "%MODEL_FILE%" (
    echo Manga109 YOLO model already present; skipping download.
) else (
    echo Downloading Manga109 YOLO model weights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile '%MODEL_FILE%' -UseBasicParsing" || goto :FailDownloadModel
)

if exist "%LABELS_FILE%" (
    echo Manga109 YOLO labels already present; skipping download.
) else (
    echo Downloading Manga109 YOLO label metadata...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%LABELS_URL%' -OutFile '%LABELS_FILE%' -UseBasicParsing" || goto :FailDownloadLabels
)

REM -----------------------------------------------------------------
REM Environment verification
REM -----------------------------------------------------------------
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)" || goto :FailVerify
python -c "import easyocr; print('EasyOCR imported successfully')" || goto :FailVerify
python -c "import cv2; print('OpenCV Version:', cv2.__version__)" || goto :FailVerify
python -c "import ultralytics; print('Ultralytics version:', ultralytics.__version__)" || goto :FailVerify

echo.
echo =============================================================
echo   Setup complete!
echo   The ocrstuff environment now includes EasyOCR, Manga OCR, docTR,
echo   Manga109 YOLO detection, and color extraction helpers.
echo   The OCR server will start automatically from UGTLive.
echo =============================================================
echo.
pause
goto :eof

:NoConda
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
echo If conda is already installed but not detected:
echo   You may need to restart your computer for PATH changes to take full effect.
echo   Alternatively, you can manually add conda to your PATH environment variable.
echo   If you forgot to check the checkbox that adds the to PATH environment variable,
echo   when installing Miniconda, you can use Add Remove Programs in Windows to remove it,
echo   then run UGTLive again to be prompted to re-install.
echo.
pause
exit /b 1

:FailActivateBase
echo ERROR: Failed to activate conda base environment!
pause
exit /b 1

:WarnUpdateConda
echo WARNING: Failed to update conda, but continuing...
goto :ContinueAfterUpdate

:FailCreateEnv
echo ERROR: Failed to create conda environment!
pause
exit /b 1

:FailActivateOcrstuff
echo ERROR: Failed to activate ocrstuff environment!
pause
exit /b 1

:FailPipUpgrade
echo ERROR: Failed to upgrade pip!
pause
exit /b 1

:FailInstallPyTorch
echo ERROR: Failed to install PyTorch!
pause
exit /b 1

:FailInstallCondaForge
echo ERROR: Failed to install conda-forge dependencies!
pause
exit /b 1

:FailInstallCondaDeps
echo ERROR: Failed to install additional conda dependencies!
pause
exit /b 1

:FailInstallBaseDeps
echo ERROR: Failed to install base pip dependencies!
pause
exit /b 1

:FailInstallAdditionalDeps
echo ERROR: Failed to install additional pip dependencies!
pause
exit /b 1

:FailInstallEasyOCR
echo ERROR: Failed to install EasyOCR!
pause
exit /b 1

:FailInstallMangaOCR
echo ERROR: Failed to install Manga OCR!
pause
exit /b 1

:FailInstallCRAFT
echo ERROR: Failed to install CRAFT Text Detector!
pause
exit /b 1

:FailInstallCuPy
echo ERROR: Failed to install CuPy for GPU color extraction!
pause
exit /b 1

:FailInstallUltralytics
echo ERROR: Failed to install Ultralytics / FastAPI dependencies!
pause
exit /b 1

:FailWarmEasyOCR
echo ERROR: Failed while initializing EasyOCR models!
pause
exit /b 1

:FailWarmManga
echo ERROR: Failed while initializing Manga OCR model!
pause
exit /b 1

:FailInstallDocTR
echo ERROR: Failed to install docTR!
pause
exit /b 1

:FailWarmDocTR
echo ERROR: Failed while initializing docTR models!
pause
exit /b 1

:FailCreateModelDir
echo ERROR: Failed to create Manga109 YOLO model directory!
pause
exit /b 1

:FailDownloadModel
echo ERROR: Failed to download Manga109 YOLO model weights!
pause
exit /b 1

:FailDownloadLabels
echo WARNING: Failed to download Manga109 YOLO labels metadata.
echo You can rerun this script later to try again.
pause
goto :eof

:FailVerify
echo ERROR: Environment verification failed!
pause
exit /b 1
