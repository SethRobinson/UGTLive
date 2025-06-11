@echo off
echo ===== Alternative Setup for EasyOCR with NVidia RTX 5090 GPU Support =====
echo 
echo This version uses pip for most packages to minimize OpenMP conflicts.
echo This is for the RTX 5090 which requires CUDA 12.8 and PyTorch nightly builds.
echo This creates (or recreates) a Conda environment called "ocrstuff".
echo

REM Activating base environment
call conda activate base
call conda update -y conda

echo Removing existing ocrstuff environment if it exists...
call conda env remove -n ocrstuff -y

echo Creating minimal Conda environment...
call conda create -y --name ocrstuff python=3.11
call conda activate ocrstuff

REM Install PyTorch nightly with CUDA 12.8 support for RTX 5090
echo Installing PyTorch nightly with CUDA 12.8 support for RTX 5090...
pip install --pre torch torchvision torchaudio --index-url https://download.pytorch.org/whl/nightly/cu128

REM Install all other dependencies via pip to avoid OpenMP conflicts
echo Installing dependencies via pip...
pip install opencv-python pillow matplotlib scipy
pip install tqdm pyyaml requests numpy

REM Install EasyOCR
echo Installing EasyOCR...
pip install easyocr

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