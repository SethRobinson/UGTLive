@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"

REM -----------------------------------------------------------------
REM Parse service_config.txt to get port and service name
REM -----------------------------------------------------------------
set "PORT="
set "SERVICE_NAME="

if exist "%CONFIG_FILE%" (
    for /f "usebackq tokens=1,2 delims=| eol=#" %%a in ("%CONFIG_FILE%") do (
        set "KEY=%%a"
        set "VALUE=%%b"
        
        REM Trim leading/trailing spaces
        for /f "tokens=*" %%x in ("!KEY!") do set "KEY=%%x"
        for /f "tokens=*" %%y in ("!VALUE!") do set "VALUE=%%y"
        
        if "!KEY!"=="port" set "PORT=!VALUE!"
        if "!KEY!"=="service_name" set "SERVICE_NAME=!VALUE!"
    )
)

if "!PORT!"=="" (
    echo ERROR: Could not find port in service_config.txt
    pause
    exit /b 1
)

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=Qwen3-TTS"

echo =============================================================
echo   Testing !SERVICE_NAME!
echo   Port: !PORT!
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Check if curl is available
REM -----------------------------------------------------------------
where curl >nul 2>nul
if errorlevel 1 (
    echo ERROR: curl is not available
    echo.
    echo curl is required for testing the service.
    echo Please ensure curl is installed and in your PATH.
    echo.
    pause
    exit /b 1
)

REM -----------------------------------------------------------------
REM Test service endpoints
REM -----------------------------------------------------------------
echo [1/2] Testing info endpoint...
curl -s http://localhost:!PORT!/info
if errorlevel 1 (
    echo   [FAIL] Could not connect to service on port !PORT!
    echo.
    echo   Please ensure the service is running using RunServer.bat
    echo.
    pause
    exit /b 1
)
echo.
echo   [PASS] Info endpoint successful
echo.

echo [2/2] Testing TTS endpoint (ono_anna voice)...
echo   Sending request: {"text": "Hello, this is a test.", "voice": "ono_anna"}
curl -s -X POST http://localhost:!PORT!/tts -H "Content-Type: application/json" -d "{\"text\": \"Hello, this is a test.\", \"voice\": \"ono_anna\"}" -o "%SCRIPT_DIR%test_output.wav" -w "    HTTP Status: %%{http_code}\n    Time: %%{time_total}s\n"
if errorlevel 1 (
    echo   [FAIL] TTS request failed
    echo.
    pause
    exit /b 1
)

if exist "%SCRIPT_DIR%test_output.wav" (
    for %%A in ("%SCRIPT_DIR%test_output.wav") do set "FILESIZE=%%~zA"
    if !FILESIZE! GTR 100 (
        echo   [PASS] TTS endpoint successful - audio file generated (!FILESIZE! bytes)
        del "%SCRIPT_DIR%test_output.wav" >nul 2>&1
    ) else (
        echo   [WARN] TTS response was very small (!FILESIZE! bytes) - may indicate an error
        type "%SCRIPT_DIR%test_output.wav"
        echo.
        del "%SCRIPT_DIR%test_output.wav" >nul 2>&1
    )
) else (
    echo   [FAIL] No output file generated
)

echo.
echo =============================================================
echo   Service test complete!
echo.
echo   To perform manual tests, use:
echo   curl -X POST http://localhost:!PORT!/tts ^
echo        -H "Content-Type: application/json" ^
echo        -d "{\"text\": \"Your text here\", \"voice\": \"ono_anna\"}" ^
echo        -o output.wav
echo.
echo   Available voices: aiden, dylan, eric, ono_anna, ryan, serena, sohee, uncle_fu, vivian
echo =============================================================
echo.
pause
