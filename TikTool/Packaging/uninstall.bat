@echo off
setlocal EnableExtensions
title Social Toolkit - uninstall
rem Removes the copy made by install.bat: this folder, the shortcuts and the "start with Windows" entry the app may have made.
rem Your data in %APPDATA%\TikTool is kept unless you answer Y to the question below.

set "DIR=%~dp0"
if "%DIR:~-1%"=="\" set "DIR=%DIR:~0,-1%"
if not exist "%DIR%\TikTool.exe" (
    echo TikTool.exe is not in this folder, so this does not look like an installed copy. Nothing was removed.
    pause
    exit /b 1
)
tasklist /fi "imagename eq TikTool.exe" 2>nul | find /i "TikTool.exe" >nul
if not errorlevel 1 (
    echo Social Toolkit is running. Close it, then run uninstall.bat again.
    pause
    exit /b 1
)

echo This removes Social Toolkit from:
echo   %DIR%
choice /c YN /n /m "Continue? [Y/N] "
if errorlevel 2 exit /b 0

set "DELDATA=N"
choice /c YN /d N /t 20 /n /m "Also delete your data (accounts, history, settings, logs) in %%APPDATA%%\TikTool? Default N. [Y/N] "
if not errorlevel 2 set "DELDATA=Y"

del /q "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Social Toolkit.lnk" 2>nul
del /q "%USERPROFILE%\Desktop\Social Toolkit.lnk" 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v TikTool /f >nul 2>nul
if /i "%DELDATA%"=="Y" rmdir /s /q "%APPDATA%\TikTool" 2>nul

echo Removing the program folder...
cd /d "%TEMP%"
(goto) 2>nul & rmdir /s /q "%DIR%" & echo Social Toolkit was removed. & pause
