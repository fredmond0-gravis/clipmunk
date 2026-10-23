@echo off
rem Removes Clipmunk. Your screenshots and library folder are left untouched.
setlocal
taskkill /im Clipmunk.exe /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v Clipmunk /f >nul 2>&1
del "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Clipmunk.lnk" >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\Programs\Clipmunk" >nul 2>&1
echo Clipmunk was removed. Your screenshots were not touched.
pause
