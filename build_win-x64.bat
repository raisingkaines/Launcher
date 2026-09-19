@echo off
setlocal enabledelayedexpansion

if "%~1"=="" (
    echo Version number is required.
    echo Usage: build.bat [version] [extra_args...]
    exit /b 1
)

set "version=%~1"

echo.
echo Compiling Launcher with dotnet...
echo %~dp0publish
dotnet publish .\src\Launcher.slnx -c Release --no-self-contained -r win-x64 --property:PublishDir="%~dp0publish"

echo.
echo Building Velopack Release v%version%
vpk pack --packTitle "Free Realms Legacy" --packAuthors "Raising Kaines" -u FreeRealmsLegacyLauncher -e Launcher.exe -o "%~dp0releases" -p "%~dp0publish" -i "%~dp0publish\App.ico" -f net10-x64-desktop -v %*