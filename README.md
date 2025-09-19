# PPTcrunch - PowerPoint & Video Compressor

PPTcrunch is a .NET 8 console application that compresses videos using FFmpeg with GPU acceleration. It supports both PowerPoint (.pptx) files with embedded videos AND individual video files, with wildcard support for batch processing.

## Table of Contents

- [Features](#features)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
- [How to Use](#how-to-use)
- [Capture Mode](#capture-mode)
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

- **Dual File Support**: Process both PowerPoint (.pptx) files AND individual video files
- **Wildcard Processing**: Support for wildcards to batch process multiple files (e.g., `*.mov`, `*.pptx`)
- **Interactive Configuration**: Prompts for GPU, codec, quality level (1-3), and max width
- **GPU Acceleration**: Uses NVIDIA GPU (NVENC) for fast video compression with CPU fallback
- **Smart Compression**: Only keeps compressed videos if they're actually smaller than originals
- **Flexible Settings**: Customizable video resolution, codec choice, and quality levels
- **Extensive Format Support**: Supports .mp4, .mov, .avi, .mkv, .webm, .wmv, .flv, .m4v, .mpg, .mpeg, .3gp, .3g2, .asf, .ogv and more
- **Intelligent Naming**: Video files get descriptive suffixes with quality and codec info
- **Intelligent XML Updates**: Only updates references for files that actually changed (PPTX processing)
- **Progress Feedback**: Real-time compression progress and detailed results
- **Robust Error Handling**: Gracefully handles compression failures and GPU unavailability
- **File Size Optimization**: Maintains original files when compression doesn't reduce size
- **Backup Preservation**: Keeps original PPTX file unchanged as backup
- **Direct-to-Disk Capture**: Record from USB HDMI capture devices to disk with no transcoding (MJPEG copy) or lossless FFV1 for uncompressed input

## Prerequisites

1. **.NET 8 SDK** - Required for building the application from source
2. **Internet connection** - Required for initial FFmpeg download on first run
3. **Optional**: NVIDIA GPU drivers (version 416.34+ recommended for best GPU acceleration)

## Installation

### Building from Source

1. **Clone or download** the source code
2. **Build the executable** using the script for your platform:

   - **Windows**:

     ```batch
     publish.bat
     ```

   - **macOS (Apple silicon)**:

     ```bash
     ./publish.sh
     ```

- **Add to PATH** (recommended): Copy `publish\PPTcrunch.exe` to a directory in your system PATH
- **Alternative**: Place `PPTcrunch.exe` in any directory and run with full path

### Using the Executable

1. **First run**: The application will automatically download FFmpeg binaries to an OS-specific directory (Windows: `C:\ffmpeg`, macOS: `~/Library/Application Support/PPTcrunch/ffmpeg`).
2. **Subsequent runs**: FFmpeg binaries are reused from that directory for faster startup.
3. **Run**:
   - Windows: `PPTcrunch.exe <file-pattern>`
   - macOS: `./PPTcrunch <file-pattern>`

### Build Output Location

- Windows builds output `publish\PPTcrunch.exe` in the repository root.
- macOS builds output `publish/osx-arm64/PPTcrunch`.

### Adding PPTcrunch to PATH (Windows)

Make it available from any folder by adding its directory to your PATH:

Option A — Use File Explorer and Settings:

1. Create or choose a folder for tools (for example: `C:\Tools`).
2. Copy `publish\PPTcrunch.exe` into that folder.
3. Open Start → search for "Environment Variables" → open "Edit environment variables for your account".
4. Select "Path" → "Edit" → "New" → add `C:\Tools` → OK.
5. Close and reopen any Command Prompt or PowerShell windows to pick up the change.

Option B — Use PowerShell (current user):

```powershell
$dir = 'C:\Tools'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Copy-Item -Force 'publish\PPTcrunch.exe' $dir
[Environment]::SetEnvironmentVariable('Path', ($env:Path + ';' + $dir), 'User')
```

After this, you can run `PPTcrunch` from any directory.

## Distribution

This program is distributed as a **self-contained executable** with **automatic FFmpeg management**:

- ✅ **Single file**: Just `PPTcrunch.exe` - no external FFmpeg installation required
- ✅ **Automatic FFmpeg**: Downloads and manages FFmpeg binaries automatically on first use
- ✅ **Persistent storage**: FFmpeg binaries stored in an OS-specific cache (`C:\ffmpeg` on Windows, `~/Library/Application Support/PPTcrunch/ffmpeg` on macOS) for reuse across sessions
- ✅ **Auto-detection**: Detects NVIDIA NVENC availability and codec support; falls back to CPU automatically
- ✅ **Hardware optimization**: Uses NVENC constant-quality mode (`-rc vbr` with `-b:v 0`) when available
- ✅ **Quality mapping**: CPU CRF and GPU CQ are mapped to comparable visual quality per codec
- ✅ **Cross-platform builds**: Scripts provided for Windows x64 and macOS (Apple silicon) self-contained executables

## How to Use

After building (`publish.bat` on Windows or `publish.sh` on macOS), run the executable from the publish directory:

- Windows: `PPTcrunch.exe <file-pattern>`
- macOS: `./PPTcrunch <file-pattern>`

Capture mode uses Windows-only DirectShow APIs and remains available as `PPTcrunch.exe capture` on Windows.

### Examples

**Process PowerPoint files:**

```bash
PPTcrunch.exe "presentation.pptx"          # Single PowerPoint file
PPTcrunch.exe "*.pptx"                     # All PowerPoint files in current directory
```

**Process video files:**

```bash
PPTcrunch.exe "video.mp4"                  # Single video file
PPTcrunch.exe "*.mov"                      # All .mov files in current directory
PPTcrunch.exe "*.*"                        # All supported files (PPTX and video)
```

**Record from a USB HDMI capture device:**

```bash
PPTcrunch.exe capture
```

The program will prompt you for compression settings:

```text
Video Compression Settings
==========================

Use GPU acceleration for faster encoding? (Y/n, default: Y): Y

Video codec options:
  1. H.264 (better compatibility, works on older systems)
  2. H.265 (smaller files, better compression, newer standard)
Enter your choice (1 or 2, default: 2): 2

Quality level options:
  1. Smallest file with passable quality
  2. Balanced with good quality (recommended)
  3. Quality indistinguishable from source, bigger file
Enter your choice (1-3, default: 2): 2

Reduce high-resolution videos to maximum 1920 pixels wide (2K HD)? (Y/n, default: Y): Y

Selected settings:
  GPU acceleration: Yes
  Video codec: H.265 (smaller files, newer standard, may not work on older systems)
  Quality level: Balanced with good quality
  Maximum width: 1920 pixels
```

## Capture Mode

The capture mode records video directly from a USB HDMI capture device to disk, without transcoding when possible.

Workflow:

1. Device selection
   - Enumerates DirectShow video devices via FFmpeg
   - Default selection prefers a device named "USB Video" if present
2. Frame rate selection
   - Detects supported frame rates from the device's advertised ranges
   - Prompts with discrete options (e.g., 60, 50, 30, 25, 20, 15, 10, 5)
   - Default: 30 fps if available
3. Resolution selection
   - Lists only resolutions compatible with the chosen frame rate
   - Shows available compression formats for each resolution (e.g., "1920x1080 (YUV422, MJPEG)")
   - Default: 1920x1080 if available at the chosen frame rate
4. Compression format selection
   - If multiple formats are available for the chosen resolution and frame rate, prompts user to select
   - Automatically uses the only available format if there's just one option
   - Default: MJPEG if available, otherwise the first available format
   - Common formats: MJPEG (compressed), YUV422 (uncompressed), RGB24, etc.
5. Output filename
   - Suggested: `yyyy-MM-dd_HH-mm-ss_WxH@FPS.mkv`
   - Container: `.mkv`

Recording details:

- MJPEG input: video is copied without re-encoding using `-c:v copy -fps_mode passthrough`
- Uncompressed input (e.g., yuyv422): recorded losslessly with FFV1 using `-pix_fmt yuv422p -c:v ffv1 -level 3 -g 1`
- Press 'q' in the FFmpeg console to stop recording cleanly
- Files are written to the current working directory

Note: Capture mode uses Windows DirectShow (`-f dshow`) and requires FFmpeg (downloaded automatically on first run).

**For PowerPoint files (.pptx)**, the program will:

1. Create a backup copy of the original file
2. Extract and compress all videos found in the presentation using your settings
3. Generate a new file named `presentation-shrunk.pptx`

**For video files**, the program will:

1. Compress the video file using your selected settings
2. Generate a new file with quality and codec information in the filename
3. Example: `video.mov` → `video - Q22H264.mp4` (Quality 22, H.264 codec)
4. Original file remains unchanged

**For wildcard patterns**, the program will:

1. Find all matching files in the current directory
2. Process each supported file type appropriately
3. Show a summary of processed files

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
3. **Video Compression**: Uses FFmpeg with your selected settings, prioritizing GPU (NVENC) when available and ensuring even dimensions for encoder compatibility
4. **File Replacement**: Replaces original videos with compressed versions
5. **Reference Updates**: Updates all XML references to reflect any filename changes
6. **Final Assembly**: Creates the final compressed PPTX file

## FFmpeg Command Details

The program tries GPU acceleration first, then falls back to CPU if needed:

**GPU Command Examples:**

```bash
# H.264 with user settings (example scale=1280:720, quality level → CQ 22)
ffmpeg -i "input-orig.mov" -vf "scale=1280:720" -c:v h264_nvenc -cq 22 -b:v 0 -preset slow -profile:v high -rc vbr -c:a copy -y -stats "output.mp4"

# H.265 with user settings (example scale=1920:1080, quality level → CQ 26)
ffmpeg -i "input-orig.mov" -vf "scale=1920:1080" -c:v hevc_nvenc -cq 26 -b:v 0 -preset slow -profile:v main -rc vbr -c:a copy -y -stats "output.mp4"
```

**CPU Command Examples:**

```bash
# H.264 with user settings (example scale=1280:720, quality level → CRF 22)
ffmpeg -i "input-orig.mov" -vf "scale=1280:720" -c:v libx264 -crf 22 -preset medium -c:a copy -y -stats "output.mp4"

# H.265 with user settings (example scale=1920:1080, quality level → CRF 24)
ffmpeg -i "input-orig.mov" -vf "scale=1920:1080" -c:v libx265 -crf 24 -preset medium -c:a copy -y -stats "output.mp4"
```

Key parameters (dynamically set based on user choices):

- **GPU**: `-c:v h264_nvenc` or `-c:v hevc_nvenc` (H.264/H.265 NVENC encoders)
- **GPU**: `-cq` (constant quality) with `-b:v 0` and `-rc vbr` (true CQ mode)
- **GPU**: `-preset slow` and appropriate `-profile:v` per codec
- **CPU**: `-c:v libx264` or `-c:v libx265` (H.264/H.265 software encoders)
- **CPU**: `-crf` per quality level, `-preset medium`
- `-vf scale=...`: Downscales if needed (no upscaling), maintains aspect ratio, ensures even dimensions
- `-y`: Overwrites output files without prompting

## Output

- **Original file**: Remains unchanged (serves as backup)
- **PPTX output**: New file with "-shrunk" suffix
- **Video output**: New `.mp4` with quality/codec in the name (e.g., `name - Q22H264.mp4`)
- **Temporary files**: Automatically cleaned up after processing

## Error Handling

- If video compression fails, the original video is retained
- XML reference updates are handled gracefully with warnings for any issues
- All temporary directories are cleaned up even if errors occur
- **Temporary Files**: The program creates and cleans up temporary directories (`PPT-temp` and `PPTX-working`) during processing
- **FFmpeg Directory**: FFmpeg binaries are stored in an OS-specific cache (`C:\ffmpeg` on Windows, `~/Library/Application Support/PPTcrunch/ffmpeg` on macOS) and are **intentionally preserved** between runs for performance (to avoid re-downloading)

## Supported Video Formats

- **Direct video mode (input)**: .mp4, .mpeg4, .mov, .avi, .mkv, .webm, .wmv, .flv, .m4v, .mpg, .mpeg, .3gp, .3g2, .asf, .ogv
- **PPTX-embedded videos**: Common media types found in `ppt/media` are processed as extracted files
- **Output**: All videos are converted to .mp4 format for consistency and smaller file sizes

### File Processing Modes

1. **PPTX Mode**: Extracts videos from PowerPoint, compresses them, and repackages into a new PPTX file with `-shrunk` suffix
2. **Video Mode**: Directly compresses individual video files with quality and codec information in filename (e.g., `video - Q22H264.mp4`)
3. **Batch Mode**: Process multiple files using wildcards (e.g., `*.mov`, `*.pptx`, `*.*`)

## Requirements

- Windows x64 or macOS (Apple silicon) with the .NET 8 SDK for building (self-contained builds include the runtime)
- Internet connection for initial FFmpeg binary download (first run only)
- Sufficient disk space for temporary files during processing
- Write access to the FFmpeg cache directory (`C:\ffmpeg` on Windows, `~/Library/Application Support/PPTcrunch/ffmpeg` on macOS)

## Troubleshooting

1. **First run initialization**: On first use, the application will automatically download and initialize FFmpeg binaries to the cache directory (`C:\ffmpeg` on Windows, `~/Library/Application Support/PPTcrunch/ffmpeg` on macOS)
2. **Permission errors**: Make sure you have write access to the directory containing the PPTX file
3. **Large file processing**: Ensure sufficient disk space for temporary extraction and processing
4. **GPU not being used**: The program will show NVENC availability during startup
   1. Ensure you have an NVIDIA GPU that supports NVENC (GTX 600+ or RTX series)
   2. Update NVIDIA GPU drivers to the latest version
   3. The downloaded FFmpeg includes NVENC support automatically
   4. If GPU fails, the program automatically falls back to CPU compression
5. **Network connectivity**: Initial setup requires internet access to download FFmpeg binaries (one-time only)
6. **Temporary directories not cleaned up**: If you see `PPT-temp` or `PPTX-working` directories left behind:
   1. This usually happens when file handles are still open during cleanup
   2. The program will show warnings and provide the full paths for manual deletion
   3. Try closing any applications that might have the files open
   4. The FFmpeg cache directory is intentionally preserved for performance

## Architecture

The application is organized into focused classes for maintainability:

- **`Program.cs`** - Main entry point, command line handling, and user input collection
- **`PPTXVideoProcessor.cs`** - Main processing orchestration and progress reporting
- **`EmbeddedFFmpegRunner.cs`** - FFmpeg video compression with GPU/CPU acceleration and automatic binary download/management
- **`FFmpegRunner.cs`** - Legacy external FFmpeg runner (replaced by automatic download version)
- **`FileManager.cs`** - File operations, ZIP handling, and cleanup
- **`XmlReferenceUpdater.cs`** - Updates XML references when file extensions change
- **`VideoFileInfo.cs`** - Data model for video file information
- **`VideoCompressionResult.cs`** - Tracks compression results and decision-making
- **`UserSettings.cs`** - Configurable compression settings (width, codec, quality)

## FFmpeg Improvements

The program includes several improvements for reliability and performance:

- **GPU Acceleration**: NVIDIA NVENC hardware encoding for faster compression when available
- **Automatic Fallback**: Falls back to CPU encoding if GPU is unavailable
- **Smart File Size Checking**: Only uses compressed files if they're actually smaller
- **Progress Feedback**: Shows real-time FFmpeg output during compression
- **Improved Scaling**: Ensures even dimensions and preserves aspect ratio
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

The program maps user-friendly quality levels (1-3) to concrete settings:

| User Level | Description | H.264 CPU (CRF) | H.264 GPU (CQ) | H.265 CPU (CRF) | H.265 GPU (CQ) |
|------------|-------------|------------------|-----------------|------------------|-----------------|
| **1** | Smallest file with passable quality | 26 | 26 | 25 | 28 |
| **2** | Balanced with good quality ⭐ | 22 | 22 | 24 | 26 |
| **3** | Quality indistinguishable from source | 20 | 20 | 22 | 23 |

Note: CPU CRF and GPU CQ values are chosen to produce comparable visual quality per codec.

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

- NVENC capability is detected automatically. Current builds use standard `vbr` rate control with constant quality (`-b:v 0`).
- If NVENC is unavailable, CPU encoding with CRF is used.

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