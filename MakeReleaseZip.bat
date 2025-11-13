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

:the server stuff too
mkdir tempbuild\webserver
if errorlevel 1 (
    echo.
    echo ========================================
    echo FAILED: Could not create webserver directory!
    echo ========================================
    pause
    exit /b 1
)

copy app\webserver\*.py tempbuild\webserver
copy app\webserver\*.bat tempbuild\webserver

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