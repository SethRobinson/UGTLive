@echo off
setlocal

set "API_URL=http://127.0.0.1:8000/api/detect"
set "DEFAULT_IMAGE=%~dp0test_images\vertical_manga1.jpg"
set "IMAGE_PATH=%~1"

if "%IMAGE_PATH%"=="" set "IMAGE_PATH=%DEFAULT_IMAGE%"

if not exist "%IMAGE_PATH%" (
    echo ERROR: Could not find image "%IMAGE_PATH%".
    echo Provide a valid image path as the first parameter or place
    echo the sample assets back into the test_images folder.
    echo.
    pause
    goto :eof
)

echo Sending "%IMAGE_PATH%" to %API_URL% ...
echo.

curl --silent --show-error --write-out "HTTP status: %{http_code}\n" ^
    --request POST "%API_URL%" ^
    --form "image=@%IMAGE_PATH%" ^
    --output "%TEMP%\manga_api_response.json"

if errorlevel 1 (
    echo.
    echo ERROR: curl encountered a problem contacting the API.
    echo Ensure RunMangaApiServer.bat is running and reachable.
    echo.
    pause
    goto :eof
)

echo.
echo Response body:
type "%TEMP%\manga_api_response.json"
echo.
pause


