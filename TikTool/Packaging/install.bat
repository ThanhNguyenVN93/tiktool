@echo off
setlocal EnableExtensions
title Social Toolkit - install
rem Installs Social Toolkit for the current user only: no administrator rights, no registry entries.
rem Everything this file does is written below, in plain text. Nothing is downloaded.
rem The program itself is inside SocialToolkit-package.exe (a self-extracting 7-Zip archive) next to this file.

set "SRC=%~dp0"
set "PKG=%SRC%SocialToolkit-package.exe"
set "DEST=%LOCALAPPDATA%\Programs\SocialToolkit"
set "STARTLNK=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Social Toolkit.lnk"
set "DESKLNK=%USERPROFILE%\Desktop\Social Toolkit.lnk"
set "YTKEEP=%TEMP%\socialtoolkit_ytdlp_keep.exe"

if not exist "%PKG%" (
    echo SocialToolkit-package.exe was not found next to install.bat. Unzip the whole zip first, then run this file from the unzipped folder.
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

rem A yt-dlp.exe that is already installed (the user may have updated it) is kept.
if exist "%YTKEEP%" del /q "%YTKEEP%"
if exist "%DEST%\Resources\yt-dlp.exe" copy /y "%DEST%\Resources\yt-dlp.exe" "%YTKEEP%" >nul

echo Unpacking, please wait...
"%PKG%" -y -o"%DEST%" >nul
if errorlevel 1 (
    echo Unpacking failed.
    pause
    exit /b 1
)
if exist "%YTKEEP%" (
    copy /y "%YTKEEP%" "%DEST%\Resources\yt-dlp.exe" >nul
    del /q "%YTKEEP%"
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
