@echo off
setlocal
cd /d "%~dp0"
echo.
echo ========================================
echo  PPTcrunch - Windows Release Publisher
echo ========================================
echo.
echo Building self-contained single file executable with automatic FFmpeg setup...
echo Target: Windows x64 (no .NET runtime or manual FFmpeg installation required)
echo.

REM Derive a reproducible build number from the current Git commit.
set "pptcrunchBuildNumber="
set "pptcrunchShallow="
for /f "delims=" %%B in ('git rev-list --count HEAD 2^>nul') do set "pptcrunchBuildNumber=%%B"
for /f "delims=" %%S in ('git rev-parse --is-shallow-repository 2^>nul') do set "pptcrunchShallow=%%S"
if not defined pptcrunchBuildNumber goto :failed_git_version
if /i "%pptcrunchShallow%"=="true" goto :failed_shallow_clone
echo Build number: %pptcrunchBuildNumber% ^(Git commit count^)
echo.

REM Restore only the Windows runtime. The project also supports macOS, and an
REM unrestricted restore tries to download both runtime packs.
dotnet restore PPTcrunch.csproj -r win-x64 -p:RuntimeIdentifiers=win-x64 -p:BuildNumber=%pptcrunchBuildNumber%
if errorlevel 1 goto :failed

REM Publish from the Windows-specific restore completed above.
dotnet publish PPTcrunch.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:RuntimeIdentifiers=win-x64 -p:BuildNumber=%pptcrunchBuildNumber% --no-restore -o publish
if errorlevel 1 goto :failed

publish\pptcrunch.exe --version
if errorlevel 1 goto :failed

echo.
echo Checking build results...
echo.

REM Check if build succeeded by verifying the executable exists
if exist publish\pptcrunch.exe (
    echo ========================================
    echo  Build completed successfully!
    echo ========================================
    echo.
    echo Single-file executable created:
    echo publish\pptcrunch.exe
    echo.
    echo [OK] Single-file deployment ready
    echo [OK] .NET runtime included
    echo [OK] Standard FFmpeg is downloaded and cached automatically when needed
    echo [OK] Auto-detects NVIDIA GPU capabilities
    echo [OK] Self-contained includes .NET 10 runtime
    echo.
    echo Ready for distribution to end users!
    echo.
    echo Files in publish directory:
    dir /b publish\
    echo.
) else (
    echo Expected executable not found.
    goto :failed_missing_output
)

pause
exit /b 0

:failed_git_version
set "publishExitCode=1"
echo.
echo Git is required and this directory must be a Git checkout so the build number can be derived from HEAD.
goto :failed_message

:failed_shallow_clone
set "publishExitCode=1"
echo.
echo A full Git clone is required because shallow clones do not have a stable total commit count.
goto :failed_message

:failed_missing_output
set "publishExitCode=1"
goto :failed_message

:failed
set "publishExitCode=%errorlevel%"

:failed_message
echo.
echo ========================================
echo  Build failed with exit code %publishExitCode%!
echo ========================================
echo.
echo Review the error messages above, then press any key to close this window.
echo.
pause
exit /b %publishExitCode%
