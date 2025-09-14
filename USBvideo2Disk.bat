@echo off
echo USB Video Capture Tool
echo.
echo Please select frame rate:
echo 1) 30 fps (default)
echo 2) 60 fps
echo.
set /p choice="Enter your choice (1 or 2, default is 1): "

if "%choice%"=="" set choice=1
if "%choice%"=="2" (
    set framerate=60
    set filename=output_1080p60.mkv
) else (
    set framerate=30
    set filename=output_1080p30.mkv
)

echo.
echo Starting recording at %framerate% fps...
echo Output file: %filename%
echo.
echo To stop recording, press 'q' key in the ffmpeg window
echo.

ffmpeg -hide_banner -f dshow -rtbufsize 512M -video_size 1920x1080 -framerate %framerate% -vcodec mjpeg -i video="USB Video" -c:v copy -fps_mode passthrough %filename%