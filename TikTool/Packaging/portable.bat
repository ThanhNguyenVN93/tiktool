@echo off
setlocal EnableExtensions
title Social Toolkit - portable
rem Portable mode: unpacks the program into the SocialToolkit folder right next to this file and runs it from there.
rem ALL data (accounts, history, settings, logs, downloads) is kept in SocialToolkit\Data, nothing goes to %APPDATA%.
rem Delete the SocialToolkit folder and nothing is left behind. To go back to normal mode, delete SocialToolkit\portable.flag.
rem The program itself is inside SocialToolkit-package.exe (a self-extracting 7-Zip archive) next to this file.

set "SRC=%~dp0"
set "PKG=%SRC%SocialToolkit-package.exe"
set "APP=%SRC%SocialToolkit"

if not exist "%APP%\TikTool.exe" (
    if not exist "%PKG%" (
        echo SocialToolkit-package.exe was not found next to portable.bat. Unzip the whole zip first, then run this file from the unzipped folder.
        pause
        exit /b 1
    )
    echo Unpacking into %APP% , please wait...
    "%PKG%" -y -o"%APP%" >nul
    if errorlevel 1 (
        echo Unpacking failed.
        pause
        exit /b 1
    )
)
if not exist "%APP%\portable.flag" echo portable> "%APP%\portable.flag"
if not exist "%APP%\Data" mkdir "%APP%\Data"
echo Portable mode is on. Data folder: %APP%\Data
start "" "%APP%\TikTool.exe"
exit /b 0
