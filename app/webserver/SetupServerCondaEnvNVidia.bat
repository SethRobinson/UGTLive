@echo off
echo ===== Setting up an EasyOCR with NVidia GPU Support Conda Env =====
echo.
echo This is something you only have to do once, it creates (or recreates) a Conda environment called "ocrstuff".
echo This can take quite a long time to setup.  It's for NVidia cards, you'll have to hack this script up or do it
echo manually for other card types.
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
echo Configuring conda channels...
call conda config --add channels conda-forge
call conda config --set channel_priority strict

echo Removing existing ocrstuff environment if it exists...
call conda env remove -n ocrstuff -y

echo Creating and setting up new Conda environment...
call conda create -y --name ocrstuff python=3.9 || goto :FailCreateEnv

echo Activating ocrstuff environment...
call conda activate ocrstuff || goto :FailActivateOcrstuff

REM Install PyTorch with GPU support (includes correct CUDA and cuDNN versions)
echo Installing PyTorch with GPU support...
call conda install -y pytorch torchvision torchaudio pytorch-cuda=11.8 -c pytorch -c nvidia || goto :FailInstallPyTorch

REM Install additional dependencies
echo Installing conda-forge dependencies...
call conda install -y -c conda-forge opencv pillow matplotlib scipy || goto :FailInstallCondaForge

echo Installing additional conda dependencies...
call conda install -y tqdm pyyaml requests || goto :FailInstallCondaDeps

REM Install EasyOCR via pip
echo Installing EasyOCR...
pip install easyocr || goto :FailInstallEasyOCR

REM Ensure the requests library is available
pip install requests || goto :FailInstallRequests

REM Download language models for EasyOCR (Japanese and English)
echo Installing language models for EasyOCR...
python -c "import easyocr; reader = easyocr.Reader(['ja', 'en'])"

REM Verify installations
echo Verifying installations...
python -c "import torch; print('PyTorch Version:', torch.__version__); print('CUDA Version:', torch.version.cuda); print('CUDA Available:', torch.cuda.is_available()); print('GPU Count:', torch.cuda.device_count() if torch.cuda.is_available() else 0)"
python -c "import easyocr; print('EasyOCR imported successfully')"
python -c "import cv2; print('OpenCV Version:', cv2.__version__)"

echo ===== Setup Complete =====
echo If the above looks looks like the test worked, you can now double click "RunServer.bat" and it will load this conda env and run the python server.
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

:FailInstallCondaForge
echo ERROR: Failed to install conda-forge dependencies!
pause
exit /b 1

:FailInstallCondaDeps
echo ERROR: Failed to install additional conda dependencies!
pause
exit /b 1

:FailInstallEasyOCR
echo ERROR: Failed to install EasyOCR!
pause
exit /b 1

:FailInstallRequests
echo ERROR: Failed to install requests!
pause
exit /b 1
