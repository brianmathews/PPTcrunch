#!/bin/bash

# Enhanced ffcomp.sh - Video compression with GPU acceleration and quality options
# Based on PPTcrunch C# application features

format_size() {
    local size=$1
    if [ $size -lt 1024 ]; then
        echo "${size} bytes"
    elif [ $size -lt 1048576 ]; then
        echo "$((size / 1024)) KB"
    elif [ $size -lt 1073741824 ]; then
        echo "$((size / 1048576)) MB"
    else
        echo "$((size / 1073741824)) GB"
    fi
}

if [ "$#" -eq 0 ]; then
    echo "Usage: ffcomp-enhanced.sh input_filename"
    echo "Example: ffcomp-enhanced.sh video.mov"
    exit 1
fi

input_file="$1"
input_dir="$(dirname "$input_file")"
input_name="$(basename "$input_file" | sed 's/\.[^.]*$//')"

echo "FFComp Enhanced Video Compressor"
echo "================================"
echo
echo "Converting: \"$input_file\""
echo

# Check FFmpeg availability
echo "Checking FFmpeg availability..."
if ! command -v ffmpeg &> /dev/null; then
    echo "ERROR: FFmpeg not found. Please install FFmpeg and ensure it's in your PATH."
    exit 1
fi
echo "  ✓ FFmpeg is available"
echo

# Detect GPU capabilities
echo "Detecting GPU capabilities..."
has_nvidia_gpu=false
gpu_model=""
driver_version=""
supports_nvenc=false
supports_h265_nvenc=false
supports_vbr_hq=false

# Check for NVIDIA GPU using nvidia-smi
if command -v nvidia-smi &> /dev/null; then
    gpu_info=$(nvidia-smi --query-gpu=name,driver_version --format=csv,noheader,nounits 2>/dev/null)
    if [ $? -eq 0 ] && [ -n "$gpu_info" ]; then
        has_nvidia_gpu=true
        gpu_model=$(echo "$gpu_info" | cut -d',' -f1 | xargs)
        driver_version=$(echo "$gpu_info" | cut -d',' -f2 | xargs)
        
        echo "  ✓ NVIDIA GPU detected: $gpu_model"
        echo "    Driver version: $driver_version"
        
        # Check driver version for vbr_hq support (416.34+)
        driver_major=$(echo "$driver_version" | cut -d'.' -f1)
        if [ "$driver_major" -ge 416 ]; then
            supports_vbr_hq=true
        fi
        
        # Check FFmpeg NVENC support
        if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q "h264_nvenc"; then
            supports_nvenc=true
            echo "    ✓ H.264 NVENC supported"
        fi
        
        if ffmpeg -hide_banner -encoders 2>/dev/null | grep -q "hevc_nvenc"; then
            supports_h265_nvenc=true
            echo "    ✓ H.265 NVENC supported"
        fi
        
        if [ "$supports_vbr_hq" = true ]; then
            echo "    ✓ Advanced quality mode supported"
        fi
    fi
else
    echo "  ⚠ No NVIDIA GPU detected"
fi

if [ "$supports_nvenc" = false ]; then
    echo "  ⚠ GPU acceleration not available - will use CPU encoding"
fi
echo

# Collect user settings
echo "Video Compression Settings"
echo "=========================="
echo

# GPU acceleration preference
use_gpu=false
if [ "$supports_nvenc" = true ]; then
    read -p "Use GPU acceleration for faster encoding? (Y/n, default: Y): " gpu_choice
    gpu_choice=${gpu_choice:-y}
    if [[ "$gpu_choice" =~ ^[Yy]([Ee][Ss])?$ ]]; then
        use_gpu=true
    fi
else
    echo "GPU acceleration not available - using CPU encoding"
fi
echo

# Codec preference
echo "Video codec options:"
echo "  1. H.264 (better compatibility, works on older systems)"
echo "  2. H.265 (smaller files, better compression, newer standard)"
if [ "$use_gpu" = true ] && [ "$supports_h265_nvenc" = false ]; then
    echo "     Note: Your GPU doesn't support H.265 encoding - H.264 will be used if selected"
fi
echo
read -p "Enter your choice (1 or 2, default: 2): " codec_choice
codec_choice=${codec_choice:-2}

codec="h265"
cpu_codec="libx265"
gpu_codec="hevc_nvenc"
if [ "$codec_choice" = "1" ]; then
    codec="h264"
    cpu_codec="libx264"
    gpu_codec="h264_nvenc"
else
    # Check H.265 GPU support if using GPU
    if [ "$use_gpu" = true ] && [ "$supports_h265_nvenc" = false ]; then
        echo "Warning: H.265 not supported on your GPU. Falling back to H.264."
        codec="h264"
        cpu_codec="libx264"
        gpu_codec="h264_nvenc"
    fi
fi
echo

# Quality level
echo "Quality level options:"
echo "  1. Smallest file with passable quality"
echo "  2. Balanced with good quality (recommended)"
echo "  3. Quality indistinguishable from source, bigger file"
echo
read -p "Enter your choice (1-3, default: 2): " quality_choice
quality_choice=${quality_choice:-2}

# Set quality values based on codec and processing type
if [ "$codec" = "h264" ]; then
    case $quality_choice in
        1)
            cpu_crf=26
            gpu_cq=26
            ;;
        3)
            cpu_crf=20
            gpu_cq=20
            ;;
        *)
            cpu_crf=22
            gpu_cq=22
            ;;
    esac
else
    case $quality_choice in
        1)
            cpu_crf=25
            gpu_cq=28
            ;;
        3)
            cpu_crf=22
            gpu_cq=23
            ;;
        *)
            cpu_crf=24
            gpu_cq=26
            ;;
    esac
fi

# Resolution settings
echo
read -p "Reduce high-resolution videos to maximum 1920 pixels wide? (Y/n, default: Y): " reduce_res
reduce_res=${reduce_res:-y}
max_width=0
if [[ "$reduce_res" =~ ^[Yy]([Ee][Ss])?$ ]]; then
    max_width=1920
else
    read -p "Enter maximum video width in pixels (or press Enter for no limit): " max_width
    max_width=${max_width:-0}
fi

# Display selected settings
echo
echo "Selected settings:"
if [ "$use_gpu" = true ]; then
    echo "  GPU acceleration: Yes ($gpu_codec)"
else
    echo "  GPU acceleration: No ($cpu_codec)"
fi
if [ "$codec" = "h264" ]; then
    echo "  Video codec: H.264 (better compatibility, standard quality)"
else
    echo "  Video codec: H.265 (smaller files, newer standard)"
fi
echo "  Quality level: $quality_choice"
if [ "$max_width" -eq 0 ]; then
    echo "  Maximum width: No limit"
else
    echo "  Maximum width: $max_width pixels"
fi
echo

# Build output filename
if [ "$codec" = "h264" ]; then
    output_file="${input_dir}/${input_name} - h264.mp4"
else
    output_file="${input_dir}/${input_name} - h265.mp4"
fi

echo "Output: \"$output_file\""
echo

# Check for audio streams
echo "Checking for audio streams..."
if ffprobe -v quiet -select_streams a -show_entries stream=codec_name -of csv=p=0 "$input_file" 2>/dev/null | grep -q .; then
    has_audio=true
    echo "Audio detected - will be copied"
else
    has_audio=false
    echo "No audio detected - video only output"
fi
echo

# Build FFmpeg command
if [ "$use_gpu" = true ]; then
    echo "Running GPU compression..."
    
    # Build scale filter
    if [ "$max_width" -eq 0 ]; then
        scale_filter="scale=trunc(iw/2)*2:trunc(ih/2)*2"
    else
        scale_filter="scale='if(gt(iw,$max_width),$max_width,iw)':'if(gt(iw,$max_width),trunc(ih*$max_width/iw/2)*2,ih)'"
    fi
    
    # Rate control mode - using modern non-deprecated parameters
    tune_param=""
    multipass_param=""
    if [ "$supports_vbr_hq" = true ]; then
        tune_param="-tune hq"
        multipass_param="-multipass 2"
    fi
    
    # Audio settings
    if [ "$has_audio" = true ]; then
        audio_params="-c:a copy"
    else
        audio_params="-an"
    fi
    
    # GPU encoding command
    if [ "$codec" = "h264" ]; then
        ffmpeg -i "$input_file" -vf "$scale_filter" -c:v "$gpu_codec" -rc vbr -cq "$gpu_cq" -b:v 0 -preset slow -profile:v high -bf 3 -refs 4 $tune_param $multipass_param $audio_params -y -stats "$output_file"
    else
        ffmpeg -i "$input_file" -vf "$scale_filter" -c:v "$gpu_codec" -rc vbr -cq "$gpu_cq" -b:v 0 -preset slow -profile:v main -bf 3 -refs 3 -tag:v hvc1 $tune_param $multipass_param $audio_params -y -stats "$output_file"
    fi
    
    if [ $? -ne 0 ]; then
        echo
        echo "GPU compression failed, falling back to CPU compression..."
        echo
        use_gpu=false
    fi
fi

if [ "$use_gpu" = false ]; then
    echo "Running CPU compression..."
    
    # Build scale filter for CPU
    if [ "$max_width" -eq 0 ]; then
        scale_filter="scale=trunc(iw/2)*2:trunc(ih/2)*2"
    else
        scale_filter="scale='min($max_width,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2"
    fi
    
    # Audio settings
    if [ "$has_audio" = true ]; then
        audio_params="-c:a copy"
    else
        audio_params="-an"
    fi
    
    # CPU encoding command
    if [ "$codec" = "h264" ]; then
        ffmpeg -i "$input_file" -vf "$scale_filter" -c:v "$cpu_codec" -crf "$cpu_crf" -preset medium -profile:v high $audio_params -y -stats "$output_file"
    else
        ffmpeg -i "$input_file" -vf "$scale_filter" -c:v "$cpu_codec" -crf "$cpu_crf" -preset medium -profile:v main -tag:v hvc1 $audio_params -y -stats "$output_file"
    fi
fi

# Check conversion result and show file size comparison
if [ $? -eq 0 ]; then
    echo
    echo "Conversion completed successfully!"
    echo
    echo "File size comparison:"
    original_size=$(stat -f%z "$input_file" 2>/dev/null || stat -c%s "$input_file" 2>/dev/null)
    new_size=$(stat -f%z "$output_file" 2>/dev/null || stat -c%s "$output_file" 2>/dev/null)
    echo "Original file: $original_size bytes"
    echo "New file:      $new_size bytes"
    
    original_formatted=$(format_size $original_size)
    new_formatted=$(format_size $new_size)
    echo
    echo "Original file: $original_formatted"
    echo "New file:      $new_formatted"
    
    if [ $original_size -gt $new_size ]; then
        size_diff=$((original_size - new_size))
        diff_formatted=$(format_size $size_diff)
        echo "Space saved:   $diff_formatted"
    else
        size_diff=$((new_size - original_size))
        diff_formatted=$(format_size $size_diff)
        echo "Size increase: $diff_formatted"
    fi
else
    echo
    echo "Conversion failed with error code: $?"
fi 