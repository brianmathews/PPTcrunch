@echo off
setlocal enabledelayedexpansion

REM Enhanced ffcomp.bat - Video compression with GPU acceleration and quality options
REM Based on PPTcrunch C# application features

if "%~1"=="" (
    echo Usage: ffcomp-enhanced.bat input_filename
    echo Example: ffcomp-enhanced.bat video.mov
    exit /b 1
)

set "input_file=%~1"
set "input_dir=%~dp1"
set "input_name=%~n1"

echo FFComp Enhanced Video Compressor
echo ================================
echo.
echo Converting: "%input_file%"
echo.

REM Check FFmpeg availability
echo Checking FFmpeg availability...
ffmpeg -version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: FFmpeg not found. Please install FFmpeg and ensure it's in your PATH.
    pause
    exit /b 1
)
echo   ✓ FFmpeg is available
echo.

REM Detect GPU capabilities
echo Detecting GPU capabilities...
set "has_nvidia_gpu=false"
set "gpu_model="
set "driver_version="
set "supports_nvenc=false"
set "supports_h265_nvenc=false"
set "supports_vbr_hq=false"

REM Check for NVIDIA GPU using nvidia-smi
nvidia-smi --query-gpu=name,driver_version --format=csv,noheader,nounits >temp_gpu_info.txt 2>nul
if %errorlevel% equ 0 (
    for /f "tokens=1,2 delims=," %%a in (temp_gpu_info.txt) do (
        set "has_nvidia_gpu=true"
        set "gpu_model=%%a"
        set "driver_version=%%b"
    )
    del temp_gpu_info.txt >nul 2>&1
    
    if "!has_nvidia_gpu!"=="true" (
        echo   ✓ NVIDIA GPU detected: !gpu_model!
        echo     Driver version: !driver_version!
        
        REM Check driver version for vbr_hq support (416.34+)
        for /f "tokens=1 delims=." %%v in ("!driver_version!") do (
            if %%v geq 416 set "supports_vbr_hq=true"
        )
        
        REM Check FFmpeg NVENC support
        ffmpeg -hide_banner -encoders 2>nul | findstr "h264_nvenc" >nul
        if !errorlevel! equ 0 (
            set "supports_nvenc=true"
            echo     ✓ H.264 NVENC supported
        )
        
        ffmpeg -hide_banner -encoders 2>nul | findstr "hevc_nvenc" >nul
        if !errorlevel! equ 0 (
            set "supports_h265_nvenc=true"
            echo     ✓ H.265 NVENC supported
        )
        
        if "!supports_vbr_hq!"=="true" (
            echo     ✓ Advanced quality mode supported
        )
    )
) else (
    del temp_gpu_info.txt >nul 2>&1
    echo   ⚠ No NVIDIA GPU detected
)

if "!supports_nvenc!"=="false" (
    echo   ⚠ GPU acceleration not available - will use CPU encoding
)
echo.

REM Collect user settings
echo Video Compression Settings
echo ==========================
echo.

REM GPU acceleration preference
set "use_gpu=false"
if "!supports_nvenc!"=="true" (
    set /p "gpu_choice=Use GPU acceleration for faster encoding? (Y/n, default: Y): "
    if "!gpu_choice!"=="" set "gpu_choice=y"
    if /i "!gpu_choice!"=="y" set "use_gpu=true"
    if /i "!gpu_choice!"=="yes" set "use_gpu=true"
) else (
    echo GPU acceleration not available - using CPU encoding
)
echo.

REM Codec preference
echo Video codec options:
echo   1. H.264 (better compatibility, works on older systems)
echo   2. H.265 (smaller files, better compression, newer standard)
if "!use_gpu!"=="true" (
    if "!supports_h265_nvenc!"=="false" (
        echo      Note: Your GPU doesn't support H.265 encoding - H.264 will be used if selected
    )
)
echo.
set /p "codec_choice=Enter your choice (1 or 2, default: 2): "
if "!codec_choice!"=="" set "codec_choice=2"

set "codec=h265"
set "cpu_codec=libx265"
set "gpu_codec=hevc_nvenc"
if "!codec_choice!"=="1" (
    set "codec=h264"
    set "cpu_codec=libx264"
    set "gpu_codec=h264_nvenc"
) else (
    REM Check H.265 GPU support if using GPU
    if "!use_gpu!"=="true" (
        if "!supports_h265_nvenc!"=="false" (
            echo Warning: H.265 not supported on your GPU. Falling back to H.264.
            set "codec=h264"
            set "cpu_codec=libx264"
            set "gpu_codec=h264_nvenc"
        )
    )
)
echo.

REM Quality level
echo Quality level options:
echo   1. Smallest file with passable quality
echo   2. Balanced with good quality (recommended)
echo   3. Quality indistinguishable from source, bigger file
echo.
set /p "quality_choice=Enter your choice (1-3, default: 2): "
if "!quality_choice!"=="" set "quality_choice=2"

REM Set quality values based on codec and processing type
if "!codec!"=="h264" (
    if "!quality_choice!"=="1" (
        set "cpu_crf=26"
        set "gpu_cq=26"
    ) else if "!quality_choice!"=="3" (
        set "cpu_crf=20"
        set "gpu_cq=20"
    ) else (
        set "cpu_crf=22"
        set "gpu_cq=22"
    )
) else (
    if "!quality_choice!"=="1" (
        set "cpu_crf=25"
        set "gpu_cq=28"
    ) else if "!quality_choice!"=="3" (
        set "cpu_crf=22"
        set "gpu_cq=23"
    ) else (
        set "cpu_crf=24"
        set "gpu_cq=26"
    )
)

REM Resolution settings
echo.
set /p "reduce_res=Reduce high-resolution videos to maximum 1920 pixels wide? (Y/n, default: Y): "
if "!reduce_res!"=="" set "reduce_res=y"
set "max_width=0"
if /i "!reduce_res!"=="y" (
    set "max_width=1920"
) else if /i "!reduce_res!"=="yes" (
    set "max_width=1920"
) else (
    set /p "max_width=Enter maximum video width in pixels (or press Enter for no limit): "
    if "!max_width!"=="" set "max_width=0"
)

REM Display selected settings
echo.
echo Selected settings:
if "!use_gpu!"=="true" (
    echo   GPU acceleration: Yes (!gpu_codec!)
) else (
    echo   GPU acceleration: No (!cpu_codec!)
)
if "!codec!"=="h264" (
    echo   Video codec: H.264 (better compatibility, standard quality)
) else (
    echo   Video codec: H.265 (smaller files, newer standard)
)
echo   Quality level: !quality_choice!
if "!max_width!"=="0" (
    echo   Maximum width: No limit
) else (
    echo   Maximum width: !max_width! pixels
)
echo.

REM Build output filename
if "!codec!"=="h264" (
    set "output_file=!input_dir!!input_name! - h264.mp4"
) else (
    set "output_file=!input_dir!!input_name! - h265.mp4"
)

echo Output: "!output_file!"
echo.

REM Check for audio streams
echo Checking for audio streams...
ffprobe -v quiet -select_streams a -show_entries stream=codec_name -of csv=p=0 "!input_file!" > temp_audio_check.txt 2>nul
set "has_audio=false"
for /f %%i in (temp_audio_check.txt) do (
    if not "%%i"=="" set "has_audio=true"
)
del temp_audio_check.txt >nul 2>&1

if "!has_audio!"=="true" (
    echo Audio detected - will be copied
) else (
    echo No audio detected - video only output
)
echo.

REM Build FFmpeg command
if "!use_gpu!"=="true" (
    echo Running GPU compression...
    
    REM Build scale filter
    if "!max_width!"=="0" (
        set "scale_filter=scale=trunc(iw/2)*2:trunc(ih/2)*2"
    ) else (
        set "scale_filter=scale='if(gt(iw,!max_width!),!max_width!,iw)':'if(gt(iw,!max_width!),trunc(ih*!max_width!/iw/2)*2,ih)'"
    )
    
    REM Rate control mode - using modern non-deprecated parameters
    set "tune_param="
    set "multipass_param="
    if "!supports_vbr_hq!"=="true" (
        set "tune_param=-tune hq"
        set "multipass_param=-multipass 2"
    )
    
    REM Audio settings
    set "audio_params=-an"
    if "!has_audio!"=="true" set "audio_params=-c:a copy"
    
    REM GPU encoding command
    if "!codec!"=="h264" (
        ffmpeg -i "!input_file!" -vf "!scale_filter!" -c:v !gpu_codec! -rc vbr -cq !gpu_cq! -b:v 0 -preset slow -profile:v high -bf 3 -refs 4 !tune_param! !multipass_param! !audio_params! -y -stats "!output_file!"
    ) else (
        ffmpeg -i "!input_file!" -vf "!scale_filter!" -c:v !gpu_codec! -rc vbr -cq !gpu_cq! -b:v 0 -preset slow -profile:v main -bf 3 -refs 3 -tag:v hvc1 !tune_param! !multipass_param! !audio_params! -y -stats "!output_file!"
    )
    
    if !errorlevel! neq 0 (
        echo.
        echo GPU compression failed, falling back to CPU compression...
        echo.
        set "use_gpu=false"
        goto :cpu_encode
    )
) else (
    :cpu_encode
    echo Running CPU compression...
    
    REM Build scale filter for CPU
    if "!max_width!"=="0" (
        set "scale_filter=scale=trunc(iw/2)*2:trunc(ih/2)*2"
    ) else (
        set "scale_filter=scale='min(!max_width!,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2"
    )
    
    REM Audio settings
    set "audio_params=-an"
    if "!has_audio!"=="true" set "audio_params=-c:a copy"
    
    REM CPU encoding command
    if "!codec!"=="h264" (
        ffmpeg -i "!input_file!" -vf "!scale_filter!" -c:v !cpu_codec! -crf !cpu_crf! -preset medium -profile:v high !audio_params! -y -stats "!output_file!"
    ) else (
        ffmpeg -i "!input_file!" -vf "!scale_filter!" -c:v !cpu_codec! -crf !cpu_crf! -preset medium -profile:v main -tag:v hvc1 !audio_params! -y -stats "!output_file!"
    )
)

REM Check conversion result and show file size comparison
if %errorlevel% equ 0 (
    echo.
    echo Conversion completed successfully!
    echo.
    echo File size comparison:
    for %%A in ("!input_file!") do set "original_size=%%~zA"
    for %%A in ("!output_file!") do set "new_size=%%~zA"
    echo Original file: !original_size! bytes
    echo New file:      !new_size! bytes
    call :FormatSize !original_size! original_formatted
    call :FormatSize !new_size! new_formatted
    echo.
    echo Original file: !original_formatted!
    echo New file:      !new_formatted!
    set /a "size_diff=!original_size! - !new_size!"
    if !size_diff! gtr 0 (
        call :FormatSize !size_diff! diff_formatted
        echo Space saved:  !diff_formatted!
    ) else (
        set /a "size_diff=!new_size! - !original_size!"
        call :FormatSize !size_diff! diff_formatted
        echo Size increase: !diff_formatted!
    )
) else (
    echo.
    echo Conversion failed with error code: %errorlevel%
)

echo.
pause
goto :eof

:FormatSize
set "size=%1"
set "result_var=%2"
if %size% lss 1024 (
    set "%result_var%=%size% bytes"
    goto :eof
)
set /a "kb=%size% / 1024"
if %kb% lss 1024 (
    set "%result_var%=%kb% KB"
    goto :eof
)
set /a "mb=%kb% / 1024"
if %mb% lss 1024 (
    set "%result_var%=%mb% MB"
    goto :eof
)
set /a "gb=%mb% / 1024"
set "%result_var%=%gb% GB"
goto :eof 