@echo off
setlocal EnableExtensions
title Social Toolkit - install
rem Installs Social Toolkit for the current user only: no administrator rights, no registry entries.
rem Everything this file does is written below, in plain text. Nothing is downloaded.

set "SRC=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\SocialToolkit"
set "STARTLNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Social Toolkit.lnk"
set "DESKLNK=%USERPROFILE%\Desktop\Social Toolkit.lnk"

if not exist "%SRC%TikTool.exe" (
    echo TikTool.exe was not found next to install.bat. Unzip the whole zip first, then run this file from the unzipped folder.
    pause
    exit /b 1
)
if /i "%SRC%"=="%DEST%\" (
    echo This is already the installed copy.
    pause
    exit /b 1
)
tasklist /fi "imagename eq TikTool.exe" 2>nul | find /i "TikTool.exe" >nul
if not errorlevel 1 (
    echo Social Toolkit is running. Close it, then run install.bat again.
    pause
    exit /b 1
)

echo Social Toolkit will be installed to:
echo   %DEST%
echo Your data in %%APPDATA%%\TikTool is not touched.
echo.
choice /c YN /n /m "Continue? [Y/N] "
if errorlevel 2 exit /b 0

rem A yt-dlp.exe / ffmpeg.exe that is already installed (the user may have updated it) is kept.
set "KEEP="
if exist "%DEST%\Resources\yt-dlp.exe" set "KEEP=yt-dlp.exe ffmpeg.exe"
robocopy "%SRC%." "%DEST%" /E /NFL /NDL /NJH /NJS /NP /XF install.bat portable.bat portable.flag %KEEP% /XD Data >nul
if errorlevel 8 (
    echo Copying the files failed.
    pause
    exit /b 1
)
copy /y "%SRC%uninstall.bat" "%DEST%\uninstall.bat" >nul

powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:STARTLNK); $s.TargetPath=$env:DEST+'\TikTool.exe'; $s.WorkingDirectory=$env:DEST; $s.Save()"
echo Start menu shortcut created.
choice /c YN /n /m "Also put a shortcut on the Desktop? [Y/N] "
if not errorlevel 2 (
    powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:DESKLNK); $s.TargetPath=$env:DEST+'\TikTool.exe'; $s.WorkingDirectory=$env:DEST; $s.Save()"
)

echo.
echo Done. To remove it later run: %DEST%\uninstall.bat
choice /c YN /n /m "Start Social Toolkit now? [Y/N] "
if not errorlevel 2 start "" "%DEST%\TikTool.exe"
exit /b 0
