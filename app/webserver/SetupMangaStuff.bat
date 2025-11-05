@echo off
setlocal

echo =============================================================
echo   Manga109 YOLO Setup Helper
echo -------------------------------------------------------------
echo   This script assumes you have already run
echo   SetupServerCondaEnvNVidia.bat which creates the "ocrstuff"
echo   conda environment with GPU-enabled PyTorch.
echo =============================================================
echo.

set "ENV_NAME=ocrstuff"
set "SCRIPT_DIR=%~dp0"
set "MODEL_DIR=%SCRIPT_DIR%models\manga109_yolo"
set "MODEL_FILE=%MODEL_DIR%\model.pt"
set "LABELS_FILE=%MODEL_DIR%\labels.json"
set "MODEL_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/model.pt"
set "LABELS_URL=https://huggingface.co/deepghs/manga109_yolo/resolve/main/v2023.12.07_l_yv11/labels.json"

REM -----------------------------------------------------------------
REM Validate conda availability
REM -----------------------------------------------------------------
where conda >nul 2>nul
if errorlevel 1 goto :NoConda

REM -----------------------------------------------------------------
REM Ensure the expected environment already exists
REM -----------------------------------------------------------------
for /f "tokens=*" %%i in ('conda env list ^| findstr /i "^%ENV_NAME% "') do set "FOUND_ENV=1"

if not defined FOUND_ENV (
    echo ERROR: Could not find a conda environment named "%ENV_NAME%".
    echo Please run SetupServerCondaEnvNVidia.bat first, then re-run this script.
    echo.
    pause
    goto :eof
)

REM -----------------------------------------------------------------
REM Activate environment
REM -----------------------------------------------------------------
call conda activate %ENV_NAME% || goto :FailActivate

echo Upgrading pip...
python -m pip install --upgrade pip || goto :FailPipUpgrade

echo Installing Manga109 YOLO dependencies...
python -m pip install "ultralytics==8.2.100" fastapi "uvicorn[standard]" python-multipart || goto :FailPipInstall

REM Create model directory if missing
if not exist "%MODEL_DIR%" (
    echo Creating model directory: "%MODEL_DIR%"
    mkdir "%MODEL_DIR%" || goto :FailCreateDir
)

REM Download model weights if needed
if exist "%MODEL_FILE%" (
    echo Model file already exists at "%MODEL_FILE%" -- skipping download.
) else (
    echo Downloading Manga109 YOLO model weights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%MODEL_URL%' -OutFile '%MODEL_FILE%' -UseBasicParsing" || goto :FailDownloadModel
)

REM Download labels metadata (optional but helpful)
if exist "%LABELS_FILE%" (
    echo Labels file already exists at "%LABELS_FILE%" -- skipping download.
) else (
    echo Downloading label metadata...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest -Uri '%LABELS_URL%' -OutFile '%LABELS_FILE%' -UseBasicParsing" || goto :FailDownloadLabels
)

echo.
echo =============================================================
echo   Setup complete!
echo   The Manga109 YOLO model is ready for use.
echo   Start the API with RunMangaApiServer.bat and use
echo   TestMangaApi.bat to verify the endpoint.
echo =============================================================
echo.
pause
goto :eof

:NoConda
echo.
echo ERROR: conda was not found in PATH.
echo Please install Miniconda and run SetupServerCondaEnvNVidia.bat first.
echo.
pause
goto :eof

:FailActivate
echo.
echo ERROR: Unable to activate "%ENV_NAME%" environment.
echo Make sure SetupServerCondaEnvNVidia.bat completed successfully.
echo.
pause
goto :eof

:FailPipUpgrade
echo.
echo ERROR: Failed to upgrade pip.
echo.
pause
goto :eof

:FailPipInstall
echo.
echo ERROR: Failed to install required Python packages.
echo.
pause
goto :eof

:FailCreateDir
echo.
echo ERROR: Failed to create model directory.
echo.
pause
goto :eof

:FailDownloadModel
echo.
echo ERROR: Failed to download model weights from:%MODEL_URL%
echo.
pause
goto :eof

:FailDownloadLabels
echo.
echo WARNING: Failed to download labels metadata. Detection will still work.
echo You can re-run this script later to try again.
echo.
pause
goto :eof


