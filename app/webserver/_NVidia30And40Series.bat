@echo off
setlocal ENABLEDELAYEDEXPANSION

echo =============================================================
echo   Setting up UGTLive OCR environment (RTX 30/40 Series)
echo   EasyOCR + MangaOCR with GPU acceleration
echo =============================================================
echo.

set "SCRIPT_DIR=%~dp0"
set "MODEL_DIR=%SCRIPT_DIR%models\manga109_yolo"
set "MODEL_FILE=%MODEL_DIR%\model.pt"
set "LABELS_FILE=%MODEL_DIR%\labels.json"
set "MODEL_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/model.pt"
set "LABELS_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/labels.json"

set "ENV_NAME=ocrstuff"
set "PYTHON_VERSION=3.10"
set "PYTORCH_CHANNEL=https://download.pytorch.org/whl/cu118"
set "TORCH_VERSION=2.6.0+cu118"
set "TORCHVISION_VERSION=0.21.0+cu118"
set "TORCHAUDIO_VERSION=2.6.0+cu118"
set "TORCH_REQUIRE=torch==!TORCH_VERSION! torchvision==!TORCHVISION_VERSION! torchaudio==!TORCHAUDIO_VERSION!"
set "EASYOCR_VERSION=1.7.2"
set "MANGA_OCR_VERSION=0.1.14"
set "DOCTR_VERSION=0.10.0"
set "CRAFT_VERSION=0.4.3"
set "CUPY_PACKAGE=cupy-cuda12x==13.6.0"
set "ULTRALYTICS_VERSION=8.3.226"
set "FASTAPI_VERSION=0.121.1"
set "UVICORN_VERSION=0.38.0"

REM -----------------------------------------------------------------
REM Activate base environment and update conda
REM -----------------------------------------------------------------
echo Activating conda base environment...
call conda activate base || goto :FailActivateBase

echo Accepting conda Terms of Service for required channels...
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/main 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/r 2>nul
call conda tos accept --override-channels --channel https://repo.anaconda.com/pkgs/msys2 2>nul

echo Updating conda (this may take a few minutes)...
call conda update -y conda >nul

echo Configuring conda channels...
call conda config --set channel_priority strict >nul
call conda config --add channels conda-forge >nul 2>nul

REM -----------------------------------------------------------------
REM Recreate environment
REM -----------------------------------------------------------------
echo Removing existing !ENV_NAME! environment if it exists...
call conda env remove -n !ENV_NAME! -y >nul 2>nul

echo Creating new conda environment (python=!PYTHON_VERSION!)...
call conda create -y --name !ENV_NAME! python=!PYTHON_VERSION! || goto :FailCreateEnv

echo Activating !ENV_NAME! environment...
call conda activate !ENV_NAME! || goto :FailActivateOcrstuff

set HF_HUB_DISABLE_SYMLINKS_WARNING=1
set KMP_DUPLICATE_LIB_OK=TRUE
set PIP_INDEX_URL=https://pypi.org/simple
set PIP_EXTRA_INDEX_URL=!PYTORCH_CHANNEL!

echo Upgrading pip tooling...
python -m pip install --upgrade pip setuptools wheel packaging || goto :FailPipUpgrade

REM -----------------------------------------------------------------
REM Install PyTorch 2.6 GPU stack (stable cu118 wheels)
REM -----------------------------------------------------------------
echo Installing PyTorch 2.6 GPU stack (cu118)...
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! || goto :FailInstallTorch

REM -----------------------------------------------------------------
REM Core scientific / CV dependencies
REM -----------------------------------------------------------------
echo Installing scientific Python packages...
python -m pip install --upgrade ^
    numpy==2.0.2 ^
    scipy==1.13.1 ^
    pillow==11.3.0 ^
    matplotlib==3.9.4 ^
    "opencv-python-headless>=4.10,<4.12" ^
    scikit-image==0.24.0 ^
    shapely==2.0.7 ^
    pyclipper==1.3.0.post6 ^
    python-bidi==0.6.7 ^
    ninja==1.13.0 ^
    gdown==5.2.0 ^
    !CUPY_PACKAGE! ^
    fastrlock==0.8.3 ^
    tqdm==4.67.1 ^
    "requests>=2.32,<2.33" ^
    pyyaml==6.0.2 || goto :FailInstallPythonDeps

REM -----------------------------------------------------------------
REM NLP / transformer dependencies used by Manga OCR & docTR
REM -----------------------------------------------------------------
echo Installing transformer/NLP dependencies...
python -m pip install --upgrade ^
    regex==2025.11.3 ^
    transformers==4.57.1 ^
    huggingface-hub==0.36.0 ^
    tokenizers==0.22.1 ^
    safetensors==0.6.2 ^
    fire==0.7.1 ^
    fugashi==1.5.2 ^
    jaconv==0.4.0 ^
    loguru==0.7.3 ^
    pyperclip==1.11.0 ^
    unidic_lite==1.0.8 ^
    h5py==3.14.0 ^
    pypdfium2==4.30.0 ^
    rapidfuzz==3.13.0 ^
    anyascii==0.3.3 ^
    defusedxml==0.7.1 ^
    langdetect==1.0.9 ^
    python-dotenv==1.2.1 || goto :FailInstallPythonDeps

REM -----------------------------------------------------------------
REM Install OCR toolchains with GPU-safe torch pin
REM -----------------------------------------------------------------
echo Installing OCR toolchains...
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! easyocr==!EASYOCR_VERSION! || goto :FailInstallEasyOCR
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! manga-ocr==!MANGA_OCR_VERSION! || goto :FailInstallMangaOCR
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! python-doctr==!DOCTR_VERSION! || goto :FailInstallDocTR
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! craft-text-detector==!CRAFT_VERSION! || goto :FailInstallCraft

REM -----------------------------------------------------------------
REM Install YOLO server stack (keeps torch pinned)
REM -----------------------------------------------------------------
echo Installing Ultralytics YOLO + FastAPI stack...
python -m pip install --upgrade --extra-index-url !PYTORCH_CHANNEL! !TORCH_REQUIRE! ^
    ultralytics==!ULTRALYTICS_VERSION! ^
    fastapi==!FASTAPI_VERSION! ^
    "uvicorn[standard]==!UVICORN_VERSION!" ^
    python-multipart==0.0.20 ^
    psutil==5.9.8 ^
    watchfiles==1.1.1 ^
    websockets==15.0.1 ^
    httptools==0.7.1 ^
    h11==0.16.0 ^
    click==8.1.8 ^
    anyio==4.11.0 ^
    sniffio==1.3.1 ^
    exceptiongroup==1.3.0 ^
    starlette==0.49.3 ^
    annotated-types==0.7.0 ^
    annotated-doc==0.0.3 ^
    typing-inspection==0.4.2 ^
    polars==1.35.2 ^
    polars-runtime-32==1.35.2 ^
    ultralytics-thop==2.0.18 || goto :FailInstallUltralytics

REM -----------------------------------------------------------------
REM Pre-download OCR models
REM -----------------------------------------------------------------
echo.
echo Pre-downloading EasyOCR language models (ja + en)...
python -c "import easyocr; easyocr.Reader(['ja','en'])" 1>nul
if errorlevel 1 (
    echo WARNING: Failed to pre-download EasyOCR models
)

echo Prefetching Manga OCR base model from Huggingface...
python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','model.safetensors')" 1>nul 2>nul
if errorlevel 1 (
    python -c "from huggingface_hub import hf_hub_download as d; d('kha-white/manga-ocr-base','pytorch_model.bin')" 1>nul 2>nul
)

echo Initializing Manga OCR (first-run warmup)...
python -c "from manga_ocr import MangaOcr; MangaOcr()" 1>nul
if errorlevel 1 (
    echo WARNING: Failed to initialize Manga OCR
)

echo Initializing docTR (first-run warmup)...
python -c "from doctr.models import ocr_predictor; ocr_predictor(det_arch='db_resnet50', reco_arch='master', pretrained=True)" 1>nul
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
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "try { Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile '%MODEL_FILE%' -UseBasicParsing } catch { exit 1 }" || goto :FailDownloadModel
)

if exist "%LABELS_FILE%" (
    echo Manga109 YOLO labels already present; skipping download.
) else (
    echo Downloading Manga109 YOLO label metadata...
    powershell -NoProfile -ExecutionPolicy Bypass -Command ^
        "try { Invoke-WebRequest -Uri '%LABELS_URL%' -OutFile '%LABELS_FILE%' -UseBasicParsing } catch { exit 1 }"
    if errorlevel 1 (
        echo WARNING: Failed to download labels metadata
    )
)

REM -----------------------------------------------------------------
REM Environment verification
REM -----------------------------------------------------------------
echo.
echo Verifying installations...
python -c "import torch, sys; from packaging import version; ver = torch.__version__.split('+')[0]; assert version.parse(ver) >= version.parse('2.6.0'), f'Required torch>=2.6.0 but found {torch.__version__}'; cuda_ok = torch.cuda.is_available(); print('PyTorch Version:', torch.__version__); print('CUDA Available:', cuda_ok); print('GPU Count:', torch.cuda.device_count() if cuda_ok else 0); print('First GPU:', torch.cuda.get_device_name(0) if cuda_ok else 'N/A')" || goto :FailVerifyTorch
python -c "import easyocr; print('EasyOCR imported successfully')" || goto :FailVerifyEasyOCR
python -c "import cv2; print('OpenCV Version:', cv2.__version__)" || goto :FailVerifyOpenCV
python -c "import ultralytics; print('Ultralytics version:', ultralytics.__version__)" || goto :FailVerifyUltralytics
python -c "import cupy; print('CuPy version:', cupy.__version__)" || echo WARNING: CuPy import failed (ensure CUDA driver is installed and GPU available).

echo.
echo =============================================================
echo   Setup complete for RTX 30/40 Series!
echo   Torch !TORCH_VERSION! (GPU build) is installed and verified.
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
echo ERROR: Failed to upgrade pip/setuptools tooling!
pause
exit /b 1

:FailInstallTorch
echo ERROR: Failed to install PyTorch 2.6 GPU stack!
pause
exit /b 1

:FailInstallPythonDeps
echo ERROR: Failed to install scientific/transformer dependencies!
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

:FailInstallCraft
echo ERROR: Failed to install CRAFT text detector!
pause
exit /b 1

:FailInstallUltralytics
echo ERROR: Failed to install Ultralytics/FastAPI stack!
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

:FailVerifyTorch
echo ERROR: Torch verification failed (GPU build missing or below 2.6)!
pause
exit /b 1

:FailVerifyEasyOCR
echo ERROR: EasyOCR verification failed!
pause
exit /b 1

:FailVerifyOpenCV
echo ERROR: OpenCV verification failed!
pause
exit /b 1

:FailVerifyUltralytics
echo ERROR: Ultralytics verification failed!
pause
exit /b 1
