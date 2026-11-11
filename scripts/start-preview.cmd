@echo off
setlocal
cd /d "%~dp0.."
set "SUMIND_DOTNET=%LOCALAPPDATA%\SuMind\dotnet\dotnet.exe"
if exist "%SUMIND_DOTNET%" (
    set "DOTNET_ROOT=%LOCALAPPDATA%\SuMind\dotnet"
) else (
    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo .NET 10 SDK was not found. Install it or restore the local SDK.
        pause
        exit /b 1
    )
    set "SUMIND_DOTNET=dotnet"
)
echo Building SuMind...
"%SUMIND_DOTNET%" build "src\SuMind.App\SuMind.App.csproj" -c Release --nologo
if errorlevel 1 (
    echo Build failed. See the errors above.
    pause
    exit /b 1
)
start "" "%SUMIND_DOTNET%" "%~dp0..\src\SuMind.App\bin\Release\net10.0-windows\SuMind.App.dll"
endlocal
