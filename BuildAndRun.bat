@echo off
echo Building UGTLive (Debug)...
dotnet build UGTLive.csproj
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b 1
)
echo.
echo Starting UGTLive...
cd app
ugtlive_debug.exe
cd .. 