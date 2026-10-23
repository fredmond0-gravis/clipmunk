@echo off
rem Installs Clipmunk for the current user (no admin needed):
rem   - copies Clipmunk.exe to %LOCALAPPDATA%\Programs\Clipmunk
rem   - adds a Start menu shortcut and starts it with Windows
setlocal
set "SRC=%~dp0Clipmunk.exe"
if not exist "%SRC%" (
  echo Clipmunk.exe was not found next to this installer.
  pause
  exit /b 1
)
set "DEST=%LOCALAPPDATA%\Programs\Clipmunk"

taskkill /im Clipmunk.exe /f >nul 2>&1
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%SRC%" "%DEST%\Clipmunk.exe" >nul || (echo Copy failed. & pause & exit /b 1)

powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs')+'\Clipmunk.lnk');$s.TargetPath='%DEST%\Clipmunk.exe';$s.Description='Clipmunk screenshot tool';$s.Save()"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Clipmunk /t REG_SZ /d "\"%DEST%\Clipmunk.exe\"" /f >nul

start "" "%DEST%\Clipmunk.exe"
echo.
echo  Clipmunk is installed and running (look for the chipmunk in the system tray).
echo  Press Print Screen to capture.
echo.
pause
