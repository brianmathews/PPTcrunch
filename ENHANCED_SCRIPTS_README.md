# Enhanced FFComp Scripts

## Overview

I've created enhanced versions of your ffcomp.bat and ffcomp.sh scripts that incorporate many of the advanced features from your C# PPTcrunch application. These enhanced scripts provide sophisticated video compression with GPU acceleration, user prompting, and quality configuration while maintaining reasonable complexity.

## Files Created

- `ffcomp-enhanced.bat` - Enhanced Windows batch script
- `ffcomp-enhanced.sh` - Enhanced Linux/macOS shell script

## Features Added

### 🔧 System Detection & Validation
- **FFmpeg availability check** - Ensures FFmpeg is installed before proceeding
- **NVIDIA GPU detection** - Uses nvidia-smi to detect GPU model and driver version
- **NVENC support verification** - Checks if FFmpeg was compiled with NVENC support
- **Driver capability detection** - Determines if advanced rate control (vbr_hq) is supported

### 🎯 Interactive User Prompting
- **GPU acceleration preference** - Asks user if they want to use GPU (if available)
- **Codec selection** - Choose between H.264 (compatibility) and H.265 (efficiency)
- **Quality levels** - Three quality presets with different CRF/CQ values
- **Resolution control** - Option to limit maximum video width

### ⚙️ Advanced FFmpeg Parameters
- **Quality mapping** - Different CRF/CQ values for H.264 vs H.265, CPU vs GPU
- **Rate control optimization** - Uses vbr_hq for newer drivers, vbr for older ones
- **Proper scaling** - Only downscales videos, maintains aspect ratio, ensures even dimensions
- **Codec-specific profiles** - Appropriate profiles and parameters for each codec
- **Audio handling** - Intelligently copies or omits audio based on detection

### 🔄 Intelligent Fallback
- **GPU to CPU fallback** - Automatically falls back to CPU if GPU encoding fails
- **H.265 to H.264 fallback** - Falls back if GPU doesn't support H.265

## Quality Level Mapping

The scripts implement the same quality levels as your C# application:

### H.264 Settings
| Level | Description | CPU CRF | GPU CQ |
|-------|-------------|---------|---------|
| 1 | Smallest file with passable quality | 26 | 26 |
| 2 | Balanced with good quality (default) | 22 | 22 |
| 3 | Quality indistinguishable from source | 20 | 20 |

### H.265 Settings
| Level | Description | CPU CRF | GPU CQ |
|-------|-------------|---------|---------|
| 1 | Smallest file with passable quality | 25 | 28 |
| 2 | Balanced with good quality (default) | 24 | 26 |
| 3 | Quality indistinguishable from source | 22 | 23 |

## Usage Examples

### Windows (Batch Script)
```cmd
ffcomp-enhanced.bat video.mov
```

### Linux/macOS (Shell Script)
```bash
./ffcomp-enhanced.sh video.mov
```

## Sample Interaction Flow

```
FFComp Enhanced Video Compressor
================================

Converting: "sample_video.mov"

Checking FFmpeg availability...
  ✓ FFmpeg is available

Detecting GPU capabilities...
  ✓ NVIDIA GPU detected: GeForce RTX 3070
    Driver version: 472.12
    ✓ H.264 NVENC supported
    ✓ H.265 NVENC supported
    ✓ Advanced quality mode supported

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

Reduce high-resolution videos to maximum 1920 pixels wide? (Y/n, default: Y): Y

Selected settings:
  GPU acceleration: Yes (hevc_nvenc)
  Video codec: H.265 (smaller files, newer standard)
  Quality level: 2
  Maximum width: 1920 pixels

Output: "sample_video - h265.mp4"

Checking for audio streams...
Audio detected - will be copied

Running GPU compression...
[FFmpeg progress output...]

Conversion completed successfully!

File size comparison:
Original file: 156 MB
New file:      89 MB
Space saved:   67 MB
```

## Key Differences from Original Scripts

### Original Scripts
- Fixed H.265 codec with CRF 28
- Basic audio detection
- Simple scaling
- No GPU detection or optimization

### Enhanced Scripts
- **Interactive codec selection** (H.264/H.265)
- **GPU detection and acceleration** with intelligent fallback
- **Quality level selection** with optimized CRF/CQ values
- **Modern FFmpeg parameters** - Uses non-deprecated rate control options
- **Advanced scaling filters** that only downscale
- **Comprehensive system checks** before encoding
- **Better error handling** and user feedback

## Deprecation Warning Fix

**Important Note**: The original C# application and earlier versions of these scripts used `-rc vbr_hq` parameters that are now deprecated in newer FFmpeg versions. The enhanced scripts have been updated to use modern parameters:

- **Old (deprecated)**: `-rc vbr_hq -cq 26`
- **New (modern)**: `-rc vbr -tune hq -multipass 2 -cq 26`

This eliminates the deprecation warnings while maintaining the same quality and performance.

**To fix the C# application**: You can update the `QualityConfigService.cs` to replace `vbr_hq` with `vbr` and add `-tune hq -multipass 2` parameters in the FFmpeg command building logic.

## Technical Features Ported from C# Application

✅ **GPU Detection Service** - nvidia-smi integration and capability detection  
✅ **Quality Configuration** - Multiple quality levels with codec-specific values  
✅ **User Settings Collection** - Interactive prompting for all major options  
✅ **FFmpeg Parameter Building** - Advanced parameter generation based on settings  
✅ **Driver Version Checking** - vbr_hq support detection for newer drivers  
✅ **Intelligent Fallbacks** - GPU→CPU and H.265→H.264 fallbacks  
✅ **Audio Stream Detection** - Smart audio handling based on stream presence  
✅ **File Size Reporting** - Detailed before/after size comparison  

## Requirements

- **FFmpeg** - Must be installed and available in system PATH
- **NVIDIA GPU** (optional) - For hardware acceleration
- **NVIDIA Drivers 416.34+** (recommended) - For advanced quality mode

## Limitations

While these scripts capture most of the key functionality from your C# application, some advanced features were omitted to keep the scripts manageable:

- **Complex GPU model detection** - Simplified to basic NVENC support checking
- **Detailed GPU capabilities** - Basic H.264/H.265 support rather than generation-specific features
- **Advanced error recovery** - Simpler error handling compared to the C# application
- **Progress monitoring** - Basic FFmpeg stats rather than detailed progress tracking

The enhanced scripts provide a good balance of functionality and simplicity, making them much more powerful than the originals while remaining easy to use and maintain. 