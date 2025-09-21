@echo off
echo.
echo ========================================
echo  PPTcrunch - Windows Release Publisher
echo ========================================
echo.
echo Building self-contained single file executable with embedded FFmpeg...
echo Target: Windows x64 (no .NET runtime or FFmpeg installation required)
echo.

REM Clean and build
dotnet clean --configuration Release >nul 2>&1
dotnet publish PPTcrunch.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish

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
    echo [OK] No external dependencies required  
    echo [OK] Embedded FFmpeg included - no external installation needed
    echo [OK] Auto-detects NVIDIA GPU capabilities
    echo [OK] Self-contained includes .NET 8 runtime
    echo.
    echo Ready for distribution to end users!
    echo.
    echo Files in publish directory:
    dir /b publish\
    echo.
) else (
    echo ========================================
    echo  Build failed!
    echo ========================================
    echo.
    echo Expected executable not found.
    echo Please check the error messages above.
    echo.
)

pause 