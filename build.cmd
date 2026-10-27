@echo off
rem Builds dist\Clipmunk.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
rem No SDK or Visual Studio needed.
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework 4 C# compiler.
  exit /b 1
)

if not exist dist mkdir dist
if not exist build mkdir build

rem The icon is generated from code (Art.DrawAppIcon). Bootstrap it on a fresh checkout.
if not exist assets\clipmunk.ico (
  if not exist assets mkdir assets
  "%CSC%" /nologo /target:winexe /out:build\iconbootstrap.exe /win32manifest:src\app.manifest ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll src\*.cs || exit /b 1
  "%~dp0build\iconbootstrap.exe" --write-icon "%~dp0assets\clipmunk.ico" || exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:dist\Clipmunk.exe ^
  /win32icon:assets\clipmunk.ico /win32manifest:src\app.manifest /resource:assets\clipmunk.ico,Clipmunk.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.VisualBasic.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  src\*.cs || exit /b 1

echo Built dist\Clipmunk.exe
