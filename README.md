# PPTcrunch - PowerPoint Video Compressor

PPTcrunch is a .NET 8 console application that compresses videos embedded in PowerPoint (.pptx) files using FFmpeg while maintaining the original presentation structure and functionality.

## Table of Contents

- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [Usage](#usage)
- [Configuration Options](#configuration-options)
- [How It Works](#how-it-works)
- [FFmpeg Command Details](#ffmpeg-command-details)
- [Encoding Presets and Quality Reference](#encoding-presets-and-quality-reference)
- [Output](#output)
- [Error Handling](#error-handling)
- [Supported Video Formats](#supported-video-formats)
- [Requirements](#requirements)
- [Troubleshooting](#troubleshooting)
- [Architecture](#architecture)
- [Technical Notes](#technical-notes)

## Features

- **Interactive Configuration**: Prompts for video width, codec (H.264/H.265), and quality settings
- **GPU Acceleration**: Uses NVIDIA GPU (NVENC) for fast video compression with CPU fallback
- **Smart Compression**: Only keeps compressed videos if they're actually smaller than originals
- **Flexible Settings**: Customizable video resolution, codec choice, and quality levels
- **Automatic Format Detection**: Supports .mp4, .mpeg4, .mov video formats
- **Intelligent XML Updates**: Only updates references for files that actually changed
- **Progress Feedback**: Real-time compression progress and detailed results
- **Robust Error Handling**: Gracefully handles compression failures and GPU unavailability
- **File Size Optimization**: Maintains original files when compression doesn't reduce size
- **Backup Preservation**: Keeps original PPTX file unchanged as backup

## Prerequisites

1. **.NET 8 Runtime** - Make sure you have .NET 8 installed on your system
2. **FFmpeg** - FFmpeg must be installed and accessible from the command line
   - Download from: https://ffmpeg.org/download.html
   - Ensure `ffmpeg` command is available in your system PATH

## Installation

1. **Download**: Get the latest `PPTcrunch.exe` from the releases page
2. **Prerequisites**: Ensure FFmpeg is installed and available in your system PATH
3. **Optional**: Install NVIDIA GPU drivers (version 416.34+ recommended for best quality)
4. **Run**: Simply double-click `PPTcrunch.exe` or run from command line

**No additional configuration files needed** - all settings are built into the executable and automatically optimized for your hardware.

## Single-File Distribution

This program is distributed as a **single self-contained executable** with no external dependencies:
- ✅ **Single file**: Just `PPTcrunch.exe` - no configuration files or DLLs needed
- ✅ **Auto-detection**: Automatically detects your NVIDIA driver version and chooses optimal encoding settings
- ✅ **Hardware optimization**: Uses `vbr_hq` mode for newer drivers (416.34+) or `vbr` for older drivers
- ✅ **Quality consistency**: CRF (CPU) and CQ (GPU) values are equivalent for consistent quality regardless of encoding method

## How to Use

```bash
dotnet run -- <pptx-file>
```

or after building:

```bash
PPTcrunch.exe <pptx-file>
```

### Example

```bash
dotnet run -- "presentation.pptx"
```

The program will prompt you for compression settings:

```
Video Compression Settings
==========================
Enter maximum video width in pixels (default: 1920): 1280
Video codec options:
  1. H.264 (better compatibility, standard quality)
  2. H.265 (better compression, newer standard)
Enter your choice (1 or 2, default: 1): 2
Video quality levels (lower = better quality, larger files):
  18-22: Very high quality
  23-28: High quality (recommended)
  29-35: Medium quality
Enter quality level (18-35, default: 23): 25

Selected settings:
  Maximum width: 1280 pixels
  Video codec: H.265
  Quality level: 25
```

Then it will:
1. Create a backup copy of the original file
2. Extract and compress all videos found in the presentation using your settings
3. Generate a new file named `presentation-shrunk.pptx`

## Configuration Options

### Video Width
- **Default**: 1920 pixels
- **Purpose**: Maximum width for video scaling (maintains aspect ratio)
- **Behavior**: 
  - Videos **wider** than this setting will be downscaled to this width
  - Videos **smaller** than this setting will remain their original size (no upscaling)
  - Aspect ratio is always preserved
  - **Both width AND height are forced to even numbers** (required by H.264/H.265 encoders)
- **Examples**: 
  - Set to `1280`: A 1920x1080 video becomes 1280x720, but a 640x480 video stays 640x480
  - Set to `1920`: A 1921x1080 video becomes 1920x1078 (height adjusted to even number)
  - Set to `3840`: Allows up to 4K resolution without downscaling

### Video Codec
- **Option 1**: H.264 (libx264 CPU / h264_nvenc GPU)
  - Better compatibility with older devices
  - Standard compression efficiency
  - Recommended for general use
- **Option 2**: H.265 (libx265 CPU / hevc_nvenc GPU)  
  - Better compression (smaller files)
  - Newer standard, requires modern hardware for playback
  - Recommended for newer devices and better compression ratios

### Quality Level
- **Range**: 18-35 (lower = better quality, larger files)
- **18-22**: Very high quality (near lossless)
- **23-28**: High quality (recommended range) 
- **29-35**: Medium quality (smaller files)
- **Default**: 23 (good balance of quality and file size)

## How It Works

1. **Backup Creation**: Copies the original PPTX to a ZIP file for processing
2. **Video Extraction**: Finds videos in the `ppt/media` directory within the PPTX
3. **Video Compression**: Uses FFmpeg with these settings:
   - Scale to maximum 1920px width (maintains aspect ratio)
   - H.264 codec with CRF 23 for good quality/size balance
   - Automatic dimension correction for encoding compatibility
4. **File Replacement**: Replaces original videos with compressed versions
5. **Reference Updates**: Updates all XML references to reflect any filename changes
6. **Final Assembly**: Creates the final compressed PPTX file

## FFmpeg Command Details

The program tries GPU acceleration first, then falls back to CPU if needed:

**GPU Command Examples:**
```bash
# H.264 with user settings (width=1280, quality=25)
ffmpeg -i "input-orig.mov" -vf "scale='min(1280,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v h264_nvenc -cq 25 -preset 1 -profile:v high -rc vbr -c:a copy -y -stats "output.mp4"

# H.265 with user settings (width=1920, quality=23)
ffmpeg -i "input-orig.mov" -vf "scale='min(1920,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v hevc_nvenc -cq 23 -preset 1 -profile:v high -rc vbr -c:a copy -y -stats "output.mp4"
```

**CPU Command Examples:**
```bash
# H.264 with user settings (width=1280, quality=25)
ffmpeg -i "input-orig.mov" -vf "scale='min(1280,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx264 -crf 25 -preset medium -c:a copy -y -stats "output.mp4"

# H.265 with user settings (width=1920, quality=23)
ffmpeg -i "input-orig.mov" -vf "scale='min(1920,iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2" -c:v libx265 -crf 23 -preset medium -c:a copy -y -stats "output.mp4"
```

Key parameters (dynamically set based on user choices):
- **GPU**: `-c:v h264_nvenc` or `-c:v hevc_nvenc` (H.264/H.265 NVENC encoders)
- **GPU**: `-cq [18-35]` Constant Quality mode (user-configurable)
- **GPU**: `-preset 1` High quality preset for NVENC
- **GPU**: `-rc vbr` Variable bitrate for optimal quality
- **CPU**: `-c:v libx264` or `-c:v libx265` (H.264/H.265 software encoders)
- **CPU**: `-crf [18-35]` Constant Rate Factor for quality (user-configurable)
- `-vf scale=...`: Downscales if needed (no upscaling), maintains aspect ratio, ensures both width AND height are even pixels
- `-y`: Overwrites output files without prompting

## Output

- **Original file**: Remains unchanged (serves as backup)
- **Compressed file**: Created with "-shrunk" suffix
- **Temporary files**: Automatically cleaned up after processing

## Error Handling

- If video compression fails, the original video is retained
- XML reference updates are handled gracefully with warnings for any issues
- All temporary directories are cleaned up even if errors occur

## Supported Video Formats

- **Input**: .mp4, .mpeg4, .mov
- **Output**: All videos are converted to .mp4 format for consistency and smaller file sizes

## Requirements

- Windows, macOS, or Linux with .NET 8
- FFmpeg installed and accessible via command line
- Sufficient disk space for temporary files during processing

## Troubleshooting

1. **"ffmpeg not found"**: Ensure FFmpeg is installed and in your system PATH
2. **Permission errors**: Make sure you have write access to the directory containing the PPTX file
3. **Large file processing**: Ensure sufficient disk space for temporary extraction and processing
4. **GPU not being used**: The program will show NVENC availability during startup
   - Ensure you have an NVIDIA GPU that supports NVENC (GTX 600+ or RTX series)
   - Update NVIDIA GPU drivers to the latest version
   - Verify FFmpeg was compiled with NVENC support: `ffmpeg -encoders | findstr nvenc`
   - If GPU fails, the program automatically falls back to CPU compression

## Architecture

The application is organized into focused classes for maintainability:

- **`Program.cs`** - Main entry point, command line handling, and user input collection
- **`PPTXVideoProcessor.cs`** - Main processing orchestration and progress reporting
- **`FFmpegRunner.cs`** - GPU/CPU video compression with timeout handling and progress feedback
- **`FileManager.cs`** - File operations, ZIP handling, and cleanup
- **`XmlReferenceUpdater.cs`** - Updates XML references when file extensions change
- **`VideoFileInfo.cs`** - Data model for video file information
- **`VideoCompressionResult.cs`** - Tracks compression results and decision-making
- **`UserSettings.cs`** - Configurable compression settings (width, codec, quality)

## FFmpeg Improvements

The program includes several improvements for reliability and performance:

- **GPU Acceleration**: NVIDIA NVENC hardware encoding for 5-10x faster compression
- **Automatic Fallback**: Falls back to CPU encoding if GPU is unavailable
- **Smart File Size Checking**: Only uses compressed files if they're actually smaller
- **Progress Feedback**: Shows real-time FFmpeg output during compression
- **Timeout Protection**: 30-minute timeout per video to prevent infinite hanging
- **Input Stream Handling**: Properly closes stdin to prevent prompts that could cause hanging
- **Improved Scaling**: Uses robust video scaling filters with proper dimension handling
- **Error Recovery**: Gracefully handles compression failures and keeps original videos

## Smart Compression Logic

The program uses intelligent decision-making for optimal results:

1. **GPU First**: Attempts NVIDIA GPU acceleration for faster compression
2. **CPU Fallback**: Falls back to CPU encoding if GPU fails or is unavailable  
3. **Size Comparison**: Compares compressed file size to original after encoding
4. **Best Choice**: Keeps whichever file is smaller (compressed or original)
5. **XML Preservation**: Only updates XML references for files that actually changed extensions

## Progress Display

The program provides comprehensive progress information:

- Shows the exact FFmpeg command being executed (GPU or CPU)
- Displays real-time compression progress with frame rates and bitrates
- Shows compression ratios for each video individually
- Provides detailed file size comparisons and space savings
- Clear status indicators (✓ for compressed, ⚠ for kept original, ✗ for failure)
- Summary of total videos compressed vs. kept as original

## Encoding Presets and Quality Reference

### FFmpeg Preset Options

The program uses different presets for CPU and GPU encoding that balance speed vs quality:

#### CPU Presets (libx264/libx265)
| Preset | Speed | Quality | Use Case |
|--------|-------|---------|----------|
| ultrafast | ⭐⭐⭐⭐⭐ | ⭐ | Real-time streaming, very fast encoding needed |
| superfast | ⭐⭐⭐⭐ | ⭐⭐ | Fast encoding with minimal quality loss |
| veryfast | ⭐⭐⭐ | ⭐⭐⭐ | Good balance for quick processing |
| faster | ⭐⭐ | ⭐⭐⭐⭐ | Slightly slower but better quality |
| **medium** | ⭐⭐⭐ | ⭐⭐⭐⭐ | **Default - good balance** |
| slow | ⭐⭐ | ⭐⭐⭐⭐⭐ | Better quality, longer encoding time |
| slower | ⭐ | ⭐⭐⭐⭐⭐ | High quality for archival purposes |
| veryslow | ⭐ | ⭐⭐⭐⭐⭐ | Maximum quality, very slow |

#### GPU Presets (NVENC)
| Preset | NVENC Code | Speed | Quality | Use Case |
|--------|------------|-------|---------|----------|
| **slow** | p7 | ⭐⭐ | ⭐⭐⭐⭐⭐ | **Default - best quality** |
| medium | p4 | ⭐⭐⭐ | ⭐⭐⭐⭐ | Good balance of speed/quality |
| fast | p1 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | Maximum speed encoding |

### Quality Settings Reference

#### H.264 Quality Levels (CRF for CPU, CQ for GPU)

| Value | Visual Quality | File Size | Human Perception | Recommended Use |
|-------|----------------|-----------|------------------|-----------------|
| **18** | Visually lossless | Very Large | Indistinguishable from original | Archival, master copies |
| **20** | Excellent | Large | Virtually identical to source | High-end production |
| **22** | Very High | Large | Minor differences only visible under scrutiny | Professional content |
| **23** | High | Medium-Large | **Recommended default** - excellent quality | **General use** |
| **25** | Good | Medium | Minor artifacts in complex scenes | Standard compression |
| **27** | Acceptable | Medium-Small | Noticeable quality loss in detailed areas | Web streaming |
| **29** | Fair | Small | Visible compression artifacts | Low bandwidth |
| **32** | Poor | Very Small | Significant quality degradation | Emergency use only |

#### H.265 Quality Levels (CRF for CPU, CQ for GPU)

| Value | Visual Quality | File Size | Human Perception | Recommended Use |
|-------|----------------|-----------|------------------|-----------------|
| **22** | Visually lossless | Very Large | Indistinguishable from original | Archival, master copies |
| **24** | Excellent | Large | Virtually identical to source | High-end production |
| **26** | Very High | Large | Minor differences only visible under scrutiny | Professional content |
| **28** | High | Medium-Large | **Recommended default** - excellent quality | **General use** |
| **30** | Good | Medium | Minor artifacts in complex scenes | Standard compression |
| **32** | Acceptable | Medium-Small | Noticeable quality loss in detailed areas | Web streaming |
| **34** | Fair | Small | Visible compression artifacts | Low bandwidth |
| **36** | Poor | Very Small | Significant quality degradation | Emergency use only |

### Quality Level Mapping

The program maps user-friendly quality levels (1-3) to specific codec values:

| User Level | Description | H.264 Settings | H.265 Settings |
|------------|-------------|----------------|----------------|
| **1** | Smallest file with passable quality | CRF/CQ: 26 | CRF/CQ: 28 |
| **2** | Balanced with good quality ⭐ | CRF/CQ: 22 | CRF/CQ: 26 |
| **3** | Quality indistinguishable from source | CRF/CQ: 20 | CRF/CQ: 23 |

*Note: CRF (CPU) and CQ (GPU) values are now equivalent for consistent quality regardless of encoding method.*

### GPU Acceleration Benefits

When using NVIDIA GPU acceleration with NVENC:

- **Speed**: 5-10x faster encoding compared to CPU
- **Constant Quality**: Uses `-b:v 0` for true constant quality without bitrate limitations
- **Efficiency**: Frees up CPU for other tasks during encoding
- **Quality**: Modern NVENC (Turing/Ampere) approaches software encoder quality

### Advanced Configuration

**Quality settings are automatically optimized** for your hardware and don't require manual configuration. The program uses these built-in settings:

**Quality Level Mapping:**
| User Level | Description | H.264 Settings | H.265 Settings |
|------------|-------------|----------------|----------------|
| **1** | Smallest file with passable quality | CRF/CQ: 26 | CRF/CQ: 28 |
| **2** | Balanced with good quality ⭐ | CRF/CQ: 22 | CRF/CQ: 26 |
| **3** | Quality indistinguishable from source | CRF/CQ: 20 | CRF/CQ: 23 |

**Automatic Driver Detection:**
- **NVIDIA Driver 416.34+**: Uses `vbr_hq` mode for better quality and bit allocation
- **Older drivers**: Uses `vbr` mode for maximum compatibility
- **No GPU/CPU only**: Uses standard CRF encoding

**GPU Compatibility:**
- **GTX 1060+, RTX series**: Full H.264 and H.265 hardware acceleration support
- **Older GPUs**: H.264 acceleration only
- **No NVIDIA GPU**: Falls back to optimized CPU encoding

## Technical Notes

- The program extracts the entire PPTX structure for efficient batch processing
- XML references are updated using string replacement to handle various reference formats
- The final PPTX maintains full compatibility with PowerPoint and other Office applications
- Processing preserves all PowerPoint-specific ZIP file characteristics
- FFmpeg processes are properly managed with timeout and resource cleanup
- Comprehensive error handling ensures partial failures don't corrupt the output
- GPU encoding uses true constant quality mode (`-b:v 0`) for optimal quality-to-size ratios 