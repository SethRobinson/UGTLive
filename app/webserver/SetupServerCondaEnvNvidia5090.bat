@echo off
echo ===== Alternative Setup for EasyOCR with NVidia RTX 5090 GPU Support =====
echo.
echo This version uses pip for most packages to minimize OpenMP conflicts.
echo This is for the RTX 5090 which requires CUDA 12.8 and PyTorch nightly builds.
echo This creates (or recreates) a Conda environment called "ocrstuff".
echo.

REM Check if conda is installed
where conda >nul 2>nul || goto :NoConda

echo Conda detected successfully!
echo.
goto :AfterCondaCheck

:NoConda
echo.
echo ===== ERROR: Conda is not installed or not in PATH =====
echo.
echo Conda is required to set up the OCR environment.
echo.
echo OPTION 1 - Automatic Installation:
echo   Run "InstallMiniConda.bat" (in this same directory) to install Miniconda.
echo   After installation, CLOSE THIS WINDOW and open a NEW Command Prompt, then run this script again.
echo.
echo OPTION 2 - Manual Installation:
echo   1. Download Miniconda from: https://docs.conda.io/en/latest/miniconda.html
echo   2. Run the installer and make sure to check "Add conda to PATH"
echo   3. Close this window and open a NEW Command Prompt
echo   4. Run this script again
echo.
echo OPTION 3 - If conda is already installed:
echo   Open a NEW Command Prompt and make sure conda is activated
echo   You might need to run: conda init cmd.exe
echo.
pause
exit /b 1

:AfterCondaCheck
REM Activating base environment
echo Activating conda base environment...
call conda activate base || goto :FailActivateBase

echo Updating conda...
call conda update -y conda || goto :WarnUpdateConda

:ContinueAfterUpdate
echo Removing existing ocrstuff environment if it exists...
call conda env remove -n ocrstuff -y

echo Creating minimal Conda environment...
call conda create -y --name ocrstuff python=3.11 || goto :FailCreateEnv

echo Activating ocrstuff environment...
call conda activate ocrstuff || goto :FailActivateOcrstuff

REM Install PyTorch nightly with CUDA 12.8 support for RTX 5090
echo Installing PyTorch nightly with CUDA 12.8 support for RTX 5090...
pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128 || goto :FailInstallPyTorch

REM Install all other dependencies via pip to avoid OpenMP conflicts
echo Installing dependencies via pip...
pip install opencv-python pillow matplotlib scipy || goto :FailInstallBaseDeps

pip install tqdm pyyaml requests numpy || goto :FailInstallAdditionalDeps

REM Install EasyOCR
echo Installing EasyOCR...
pip install easyocr || goto :FailInstallEasyOCR

REM Download language models for EasyOCR (Japanese and English)
echo Installing language models for EasyOCR...
python -c "import os; os.environ['KMP_DUPLICATE_LIB_OK']='TRUE'; import easyocr; reader = easyocr.Reader(['ja', 'en'])"

REM Verify installations
echo Verifying installations...
python -c "import os; os.environ['KMP_DUPLICATE_LIB_OK']='TRUE'; import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import os; os.environ['KMP_DUPLICATE_LIB_OK']='TRUE'; import torch; print('GPU Name:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'No GPU detected')"
python -c "import os; os.environ['KMP_DUPLICATE_LIB_OK']='TRUE'; import torch; device = torch.cuda.current_device() if torch.cuda.is_available() else None; print('CUDA Capability:', torch.cuda.get_device_capability(device) if device is not None else 'N/A')"
python -c "import easyocr; print('EasyOCR imported successfully')"
python -c "import cv2; print('OpenCV Version:', cv2.__version__)"

echo ===== Setup Complete =====
echo If the above looks like the test worked and shows RTX 5090 with CUDA capability (12, 0),
echo you can now use "RunServer.bat" to run the server.
pause
goto :eof

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

:FailInstallPyTorch
echo ERROR: Failed to install PyTorch!
pause
exit /b 1

:FailInstallBaseDeps
echo ERROR: Failed to install base dependencies!
pause
exit /b 1

:FailInstallAdditionalDeps
echo ERROR: Failed to install additional dependencies!
pause
exit /b 1

:FailInstallEasyOCR
echo ERROR: Failed to install EasyOCR!
pause
exit /b 1
