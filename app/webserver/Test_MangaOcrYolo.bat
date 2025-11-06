@echo off
echo Testing Manga109 YOLO + MangaOCR Integration
echo ============================================
echo.

call conda activate ocrstuff
if errorlevel 1 (
    echo Failed to activate conda environment 'ocrstuff'
    echo Please run SetupServerCondaEnvNVidia.bat first
    pause
    exit /b 1
)

python test_mangaocr_yolo.py
pause

