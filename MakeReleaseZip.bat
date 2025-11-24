@echo off
SET APP_NAME=UGTLive

rmdir tempbuild /S /Q
rmdir app\win-x64\publish /S /Q

dotnet clean UGTLive.sln --configuration Release
if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED: dotnet clean failed!
    echo ========================================
    pause
    exit /b 1
)

dotnet publish .\UGTLive.csproj -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED: dotnet publish failed!
    echo ========================================
    pause
    exit /b 1
)


mkdir tempbuild
if errorlevel 1 (
    echo.
    echo ========================================
    echo FAILED: Could not create tempbuild directory!
    echo ========================================
    pause
    exit /b 1
)

copy app\win-x64\publish\*.exe tempbuild
if errorlevel 1 (
    echo.
    echo ========================================
    echo FAILED: Could not copy .exe files!
    echo ========================================
    pause
    exit /b 1
)

copy app\win-x64\publish\*.dll tempbuild
copy app\win-x64\publish\*.deps.json tempbuild
copy app\win-x64\publish\*.runtimeconfig.json tempbuild
copy README.md tempbuild
copy media\readme.txt tempbuild

:the services stuff too
echo Copying services...

REM First, copy the util directory entirely (no exclusions - we need the venv module in Python's stdlib)
robocopy app\services\util tempbuild\services\util /E
if %errorlevel% geq 8 (
    echo.
    echo ========================================
    echo WARNING: Util directory may not have copied correctly, but continuing...
    echo ========================================
)

REM Copy the rest of services tree, excluding venv, __pycache__, localdata, util, and log files
robocopy app\services tempbuild\services /E /XD venv __pycache__ localdata util /XF setup_log.txt service_install_version_last_installed.txt *.log

REM robocopy returns 0-7 for success, 8+ for errors
if %errorlevel% geq 8 (
    echo.
    echo ========================================
    echo WARNING: Some services files may not have copied correctly, but continuing...
    echo ========================================
)

REM Copy media and audio folders
echo Copying media and audio folders...
robocopy app\media tempbuild\media /E
if %errorlevel% geq 8 (
    echo.
    echo ========================================
    echo WARNING: Media folder may not have copied correctly, but continuing...
    echo ========================================
)

robocopy app\audio tempbuild\audio /E
if %errorlevel% geq 8 (
    echo.
    echo ========================================
    echo WARNING: Audio folder may not have copied correctly, but continuing...
    echo ========================================
)

call %RT_PROJECTS%\Signing\sign.bat tempbuild\ugtlive.exe "Universal Game Translator Live"
if errorlevel 1 (
    echo.
    echo ========================================
    echo WARNING: Code signing failed, but continuing...
    echo ========================================
)

set FNAME=UniversalGameTranslatorLive_Windows.zip
del %FNAME%

%RT_PROJECTS%\proton\shared\win\utils\7za.exe a -r -tzip %FNAME% tempbuild
if errorlevel 1 (
    echo.
    echo ========================================
    echo FAILED: Could not create zip file!
    echo ========================================
    pause
    exit /b 1
)

:Rename the root folder
%RT_PROJECTS%\proton\shared\win\utils\7z.exe rn %FNAME% tempbuild\ %APP_NAME%\
REM Note: 7z.exe rn may return non-zero exit code even on success, so we don't check errorlevel

pause