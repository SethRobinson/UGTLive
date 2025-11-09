echo Running UGTLive OCR Server, locally only.
echo This is a python backend that uses your GPU to run AI stuff we needed.
echo .

REM Prefer running via conda without activating (handles spaces in paths)
call conda run --no-capture-output -n ocrstuff python server.py
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo WARNING: conda run failed, attempting to activate environment...
    call conda activate ocrstuff 2>nul
    if %ERRORLEVEL% NEQ 0 (
        echo ERROR: Failed to activate the 'ocrstuff' conda environment.
        echo Make sure Conda is installed and the 'ocrstuff' environment exists.
        pause
        exit /b 1
    )
    python server.py
)
pause
