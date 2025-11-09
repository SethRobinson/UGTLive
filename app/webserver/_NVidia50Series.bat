@echo off
setlocal ENABLEDELAYEDEXPANSION

echo =============================================================
echo   Setting up UGTLive OCR environment (RTX 50 Series)
echo   EasyOCR + MangaOCR with GPU acceleration
echo   Using PyTorch nightly for RTX 50 Series support
echo =============================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "MODEL_DIR=%SCRIPT_DIR%models\manga109_yolo"
set "MODEL_FILE=%MODEL_DIR%\model.pt"
set "LABELS_FILE=%MODEL_DIR%\labels.json"
set "MODEL_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/model.pt"
set "LABELS_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/labels.json"

set "PYTHON_VERSION=3.11"
set "CUPY_PACKAGE=cupy-cuda12x"
set "KMP_DUPLICATE_LIB_OK=TRUE"

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
call conda update -y conda 2>nul

echo Configuring conda channels...
call conda config --add channels conda-forge
call conda config --set channel_priority strict

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo Removing existing ocrstuff environment if it exists...
call conda env remove -n ocrstuff -y 2>nul

echo Creating new conda environment (python=!PYTHON_VERSION!)...
call conda create -y --name ocrstuff python=!PYTHON_VERSION! || goto :FailCreateEnv

echo Activating ocrstuff environment...
call conda activate ocrstuff || goto :FailActivateOcrstuff

set HF_HUB_DISABLE_SYMLINKS_WARNING=1

echo Upgrading pip...
python -m pip install --upgrade pip || goto :FailPipUpgrade

REM -----------------------------------------------------------------
REM Install PyTorch nightly for RTX 50 Series support
REM -----------------------------------------------------------------
echo.
echo Installing PyTorch nightly with CUDA 12.8 support for RTX 50 Series...
python -m pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 || goto :FailInstallPyTorch

echo Installing base scientific Python dependencies via pip...
python -m pip install opencv-python pillow matplotlib scipy || goto :FailInstallBaseDeps
python -m pip install tqdm pyyaml requests numpy || goto :FailInstallAdditionalDeps

REM -----------------------------------------------------------------
REM OCR and detection dependencies
REM -----------------------------------------------------------------
echo Installing EasyOCR...
python -m pip install easyocr || goto :FailInstallEasyOCR

echo Installing Manga OCR...
python -m pip install manga-ocr || goto :FailInstallMangaOCR

echo Installing docTR...
python -m pip install python-doctr || goto :FailInstallDocTR

echo Installing CRAFT Text Detector (with compatible dependencies)...
python -m pip install scikit-image gdown
python -m pip install craft-text-detector --no-deps

echo Installing CuPy for GPU color extraction...
python -m pip install !CUPY_PACKAGE!

echo Installing Ultralytics YOLO + FastAPI helpers...
python -m pip install "ultralytics>=8.3.0" fastapi "uvicorn[standard]" python-multipart

REM -----------------------------------------------------------------
REM Pre-download OCR models
REM -----------------------------------------------------------------
echo.
echo Pre-downloading EasyOCR language models (ja + en)...
python -c "import easyocr; easyocr.Reader(['ja','en'])"
if errorlevel 1 (
    echo WARNING: Failed to pre-download EasyOCR models
)

echo Prefetching Manga OCR model...
python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','model.safetensors')" >nul 2>&1
if errorlevel 1 (
    python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','pytorch_model.bin')" >nul 2>&1
)

echo Initializing Manga OCR...
python -c "from manga_ocr import MangaOcr; MangaOcr()"
if errorlevel 1 (
    echo WARNING: Failed to initialize Manga OCR
)

echo Initializing docTR...
python -c "from doctr.models import ocr_predictor; ocr_predictor(det_arch='db_resnet50', reco_arch='master', pretrained=True)"
if errorlevel 1 (
    echo WARNING: Failed to initialize docTR
)

REM -----------------------------------------------------------------
REM Download Manga109 YOLO model + labels
REM -----------------------------------------------------------------
if not exist "%MODEL_DIR%" (
    echo Creating Manga109 YOLO model directory...
    mkdir "%MODEL_DIR%" || goto :FailCreateModelDir
)

if exist "%MODEL_FILE%" (
    echo Manga109 YOLO model already present; skipping download.
) else (
    echo Downloading Manga109 YOLO model weights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile '%MODEL_FILE%' -UseBasicParsing } catch { exit 1 }" || goto :FailDownloadModel
)

if exist "%LABELS_FILE%" (
    echo Manga109 YOLO labels already present; skipping download.
) else (
    echo Downloading Manga109 YOLO label metadata...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Invoke-WebRequest -Uri '%LABELS_URL%' -OutFile '%LABELS_FILE%' -UseBasicParsing } catch { exit 1 }"
    if errorlevel 1 (
        echo WARNING: Failed to download labels metadata
    )
)

REM -----------------------------------------------------------------
REM Environment verification
REM -----------------------------------------------------------------
echo.
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import easyocr; print('EasyOCR imported successfully')"
python -c "import cv2; print('OpenCV Version:', cv2.__version__)"
python -c "import ultralytics; print('Ultralytics version:', ultralytics.__version__)"

echo.
echo =============================================================
echo   Setup complete for RTX 50 Series!
echo   The ocrstuff environment is ready with GPU acceleration.
echo   The OCR server will start automatically from UGTLive.
echo =============================================================
echo.
pause
goto :eof

:FailActivateBase
echo ERROR: Failed to activate conda base environment!
pause
exit /b 1

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

:FailInstallDocTR
echo ERROR: Failed to install docTR!
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
