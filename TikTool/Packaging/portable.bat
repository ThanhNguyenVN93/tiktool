@echo off
setlocal EnableExtensions
title Social Toolkit - portable
rem Portable mode: the app runs from this folder and keeps ALL its data in the Data folder next to it
rem (instead of %APPDATA%\TikTool). Delete this folder and nothing is left behind.
rem To go back to the normal mode, delete the file portable.flag.

set "SRC=%~dp0"
if not exist "%SRC%TikTool.exe" (
    echo TikTool.exe was not found next to portable.bat. Unzip the whole zip first, then run this file from the unzipped folder.
    pause
    exit /b 1
)
if not exist "%SRC%portable.flag" echo portable> "%SRC%portable.flag"
if not exist "%SRC%Data" mkdir "%SRC%Data"
echo Portable mode is on. Data folder: %SRC%Data
start "" "%SRC%TikTool.exe"
exit /b 0
