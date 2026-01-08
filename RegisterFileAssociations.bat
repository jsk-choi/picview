@echo off
:: Run as Administrator to register file associations
:: This script associates image files with PicView

setlocal EnableDelayedExpansion

:: Get the full path to the executable
set "EXEPATH=%~dp0PicView\bin\Release\net8.0-windows\PicView.exe"

:: Check if running as admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This script requires administrator privileges.
    echo Right-click and select "Run as administrator"
    pause
    exit /b 1
)

:: Check if exe exists
if not exist "%EXEPATH%" (
    echo PicView.exe not found at: %EXEPATH%
    echo Please build the project first: dotnet build -c Release
    pause
    exit /b 1
)

echo Registering PicView as image handler...

:: Register the application
reg add "HKLM\SOFTWARE\Classes\PicView.Image" /ve /d "Image File" /f
reg add "HKLM\SOFTWARE\Classes\PicView.Image\shell\open\command" /ve /d "\"%EXEPATH%\" \"%%1\"" /f

:: Associate file types
for %%e in (jpg jpeg png gif bmp webp tiff tif) do (
    echo Registering .%%e
    reg add "HKLM\SOFTWARE\Classes\.%%e\OpenWithProgids" /v "PicView.Image" /t REG_SZ /d "" /f
)

:: Refresh shell
ie4uinit.exe -show

echo.
echo Done! You can now right-click an image, choose "Open with" and select PicView.
echo To make it the default, go to Settings ^> Apps ^> Default apps.
pause
