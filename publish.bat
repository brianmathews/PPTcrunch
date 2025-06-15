@echo off
echo.
echo ========================================
echo  PPTcrunch - Windows Release Publisher
echo ========================================
echo.
echo Building self-contained single file executable...
echo.

dotnet publish -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo  Build completed successfully!
    echo ========================================
    echo.
    echo Executable location:
    echo bin\Release\net8.0\win-x64\publish\PPTcrunch.exe
    echo.
    echo The executable is ready to run on any Windows 64-bit machine
    echo without requiring .NET 8 to be installed.
    echo.
) else (
    echo.
    echo ========================================
    echo  Build failed!
    echo ========================================
    echo.
    echo Please check the error messages above.
    echo.
)

pause 