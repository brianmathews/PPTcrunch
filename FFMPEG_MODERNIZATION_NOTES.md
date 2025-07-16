# FFmpeg Parameter Modernization

## Overview

The C# application has been updated to use modern, non-deprecated FFmpeg parameters while maintaining the same variable bitrate encoding behavior based purely on quality without bitrate limitations.

## Changes Made

### 1. Updated EncodingSettings Class

Added new properties to support modern FFmpeg parameters:

```csharp
public class EncodingSettings
{
    public int? Crf { get; set; }
    public int? Cq { get; set; }
    public string Preset { get; set; } = string.Empty;
    public string Rc { get; set; } = string.Empty;
    public string Tune { get; set; } = string.Empty;      // NEW
    public int? Multipass { get; set; }                   // NEW
}
```

### 2. Updated QualityConfigService

**Before (Deprecated):**
```csharp
string rcMode = _supportsVbrHq ? "vbr_hq" : "vbr";
GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode }
```

**After (Modern):**
```csharp
string rcMode = "vbr";
string tuneMode = _supportsVbrHq ? "hq" : "";
int? multipassValue = _supportsVbrHq ? 2 : null;
GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
```

### 3. Updated FFmpegRunner

The GPU command builder now includes the modern parameters:

**Before:**
```bash
-rc vbr_hq -cq 26 -preset slow
```

**After:**
```bash
-rc vbr -cq 26 -preset slow -tune hq -multipass 2
```

## Key Benefits

### ✅ **Eliminates Deprecation Warnings**
- No more `[hevc_nvenc] Specified rc mode is deprecated` warnings
- Uses modern FFmpeg parameter structure

### ✅ **Maintains Variable Bitrate Encoding**
- Still uses `-rc vbr` for variable bitrate rate control
- `-cq` parameter ensures quality-based encoding
- `-b:v 0` maintains no bitrate limitations

### ✅ **Preserves Quality Behavior**
- `-tune hq` provides the same high-quality optimizations as the old `vbr_hq`
- `-multipass 2` enables better bit allocation through two-pass encoding
- Same CQ values for consistent visual quality

### ✅ **Backward Compatibility**
- Older drivers still work with basic VBR mode
- Driver version detection determines feature availability
- Graceful fallback for unsupported hardware

## Technical Details

### Variable Bitrate Behavior

The encoding still uses variable bitrate where:
- **Rate Control**: `-rc vbr` allows bitrate to vary based on scene complexity
- **Quality Target**: `-cq` maintains consistent visual quality across the video
- **No Limits**: `-b:v 0` ensures no bitrate ceiling restrictions
- **Optimization**: `-tune hq` and `-multipass 2` improve bit allocation decisions

### Driver Support

- **NVIDIA Driver 416.34+**: Uses advanced features (`-tune hq -multipass 2`)
- **Older Drivers**: Uses basic VBR mode for compatibility
- **Detection**: Automatic capability detection via nvidia-smi

## Example Output

**With Advanced Features (Driver 416.34+):**
```
Driver supports advanced quality features: Yes (using vbr with tune hq and multipass mode)
```

**FFmpeg Command:**
```bash
ffmpeg -i input.mov -vf "scale=..." -c:v hevc_nvenc -rc vbr -cq 26 -b:v 0 -preset slow -tune hq -multipass 2 -profile:v main -bf 3 -refs 3 -tag:v hvc1 -c:a copy -y output.mp4
```

**With Basic Features (Older Drivers):**
```
Driver supports advanced quality features: No (using vbr mode)
```

**FFmpeg Command:**
```bash
ffmpeg -i input.mov -vf "scale=..." -c:v hevc_nvenc -rc vbr -cq 26 -b:v 0 -preset slow -profile:v main -bf 3 -refs 3 -tag:v hvc1 -c:a copy -y output.mp4
```

## Migration Notes

- **No user-facing changes**: All changes are internal parameter adjustments
- **Same quality levels**: CRF/CQ values remain unchanged for consistent results
- **Same performance**: Encoding speed and quality should be equivalent or better
- **Enhanced scripts**: The enhanced ffcomp scripts use the same modern approach

This modernization ensures the application remains compatible with current and future FFmpeg versions while maintaining the same high-quality, variable bitrate encoding behavior. 