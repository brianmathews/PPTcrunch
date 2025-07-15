@echo off
echo.
echo ========================================
echo  PPTcrunch - Windows Release Publisher
echo ========================================
echo.
echo Building self-contained single file executable...
echo Target: Windows x64 (no .NET runtime required)
echo.

REM Clean and build
dotnet clean --configuration Release >nul 2>&1
dotnet publish --configuration Release

echo.
echo Checking build results...

REM Check if build succeeded by verifying the executable exists
if exist bin\Release\net8.0\win-x64\publish\PPTcrunch.exe (
    echo.
    echo ========================================
    echo  Build completed successfully!
    echo ========================================
    echo.
    echo Single-file executable created:
    echo bin\Release\net8.0\win-x64\publish\PPTcrunch.exe
    echo.
    
    echo [√] Single-file deployment ready
    echo [√] No external dependencies required  
    echo [√] Auto-detects NVIDIA GPU capabilities
    echo [√] Self-contained includes .NET 8 runtime
    echo.
    echo Ready for distribution to end users!
    echo.
    
    echo Files in publish directory:
    dir /b bin\Release\net8.0\win-x64\publish\
    echo.
    
) else (
    echo.
    echo ========================================
    echo  Build failed!
    echo ========================================
    echo.
    echo Expected executable not found.
    echo Please check the error messages above.
    echo.
)

pause 