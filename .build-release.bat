@echo off
setlocal

cd /d "%~dp0"

set "RID=win-x64"
if not "%~1"=="" set "RID=%~1"

set "OUT=bin\Release\net10.0-windows\%RID%\publish"
set "COMMON=/t:Publish /p:Configuration=Release /p:RuntimeIdentifier=%RID% /p:DebugType=none /nologo /v:m"

echo Building release (%RID%)...
echo.

dotnet restore IconPull.csproj -r %RID% --nologo
if errorlevel 1 goto :failed

echo [1/2] Self-contained (includes .NET runtime)...
msbuild IconPull.csproj %COMMON% /p:SelfContained=true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishDir=%OUT%\self-contained\
if errorlevel 1 goto :failed

echo.
echo [2/2] Framework-dependent (requires .NET 10 Desktop Runtime)...
msbuild IconPull.csproj %COMMON% /p:SelfContained=false /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishDir=%OUT%\framework-dependent\
if errorlevel 1 goto :failed

echo.
echo Done:
echo   %OUT%\self-contained\IconPull.exe
echo   %OUT%\framework-dependent\IconPull.exe
goto :end

:failed
echo.
echo Build failed.

:end
pause
exit /b %errorlevel%
