@echo off
setlocal

set "ENV_NAME=ocrstuff"
set "HOST=0.0.0.0"
set "PORT=8000"

echo Activating %ENV_NAME% environment...
call conda activate %ENV_NAME% || goto :FailActivate

echo Starting Manga109 Region Detection API on %HOST%:%PORT%...
python -m uvicorn api_server:app --host %HOST% --port %PORT% --log-level info

echo.
echo API stopped.
pause
goto :eof

:FailActivate
echo.
echo ERROR: Unable to activate the %ENV_NAME% environment.
echo Make sure SetupServerCondaEnvNVidia.bat has been run successfully.
echo.
pause


