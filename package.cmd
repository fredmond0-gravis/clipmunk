@echo off
rem Builds Clipmunk and zips everything a colleague needs into dist\Clipmunk.zip
setlocal
cd /d "%~dp0"
call "%~dp0build.cmd" || exit /b 1
if exist build\package rmdir /s /q build\package
mkdir build\package
copy /y dist\Clipmunk.exe build\package\ >nul
copy /y "packaging\Install Clipmunk.cmd" build\package\ >nul
copy /y "packaging\Uninstall Clipmunk.cmd" build\package\ >nul
copy /y README.md build\package\ >nul
if exist dist\Clipmunk.zip del dist\Clipmunk.zip
powershell -NoProfile -Command "Compress-Archive -Path 'build\package\*' -DestinationPath 'dist\Clipmunk.zip'" || exit /b 1
echo Packaged dist\Clipmunk.zip
