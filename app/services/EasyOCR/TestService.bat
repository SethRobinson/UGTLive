@echo off
REM Force UTF-8 encoding for international compatibility
chcp 65001 >nul 2>&1
setlocal ENABLEDELAYEDEXPANSION

set "SCRIPT_DIR=%~dp0"
set "CONFIG_FILE=%SCRIPT_DIR%service_config.txt"
set "TEST_IMAGE=%SCRIPT_DIR%..\shared\test_images\anime_test.jpg"

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

if "!SERVICE_NAME!"=="" set "SERVICE_NAME=OCR Service"

echo =============================================================
echo   Testing !SERVICE_NAME!
echo   Port: !PORT!
echo   Test Image: %TEST_IMAGE%
echo =============================================================
echo.

REM -----------------------------------------------------------------
REM Check if test image exists
REM -----------------------------------------------------------------
if not exist "%TEST_IMAGE%" (
    echo ERROR: Test image not found: %TEST_IMAGE%
    echo.
    echo Please ensure the shared test_images folder contains test.png
    echo.
    pause
    exit /b 1
)

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
echo [1/4] Testing health endpoint...
curl -s http://localhost:!PORT!/health
if errorlevel 1 (
    echo   [FAIL] Could not connect to service on port !PORT!
    echo.
    echo   Please ensure the service is running using RunServer.bat
    echo.
    pause
    exit /b 1
)
echo.
echo   [PASS] Health check successful
echo.

echo [2/4] Testing info endpoint...
curl -s http://localhost:!PORT!/info
echo.
echo   [PASS] Info endpoint successful
echo.

echo [3/4] Testing OCR process endpoint...
curl -s -X POST http://localhost:!PORT!/process?lang=japan -H "Content-Type: application/octet-stream" --data-binary "@%TEST_IMAGE%"
echo.
echo   [PASS] Process endpoint successful
echo.

echo [4/4] Running performance test (5 requests)...
echo.

for /L %%i in (1,1,5) do (
    echo   Request %%i/5...
    curl -s -X POST http://localhost:!PORT!/process?lang=japan -H "Content-Type: application/octet-stream" --data-binary "@%TEST_IMAGE%" -w "    Time: %%{time_total}s\n" -o nul
)

echo.
echo =============================================================
echo   Service test complete!
echo.
echo   To perform manual tests, use:
echo   curl -X POST http://localhost:!PORT!/process?lang=japan \
echo        -H "Content-Type: application/octet-stream" \
echo        --data-binary "@path\to\image.png"
echo =============================================================
echo.
pause

