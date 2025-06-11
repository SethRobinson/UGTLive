@echo off
echo Building UGTLive (Release)...
dotnet build UGTLive.csproj -c Release
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)
echo.
echo Starting UGTLive (Release)...
cd app
ugtlive.exe
cd .. 