# PPTcrunch - PowerPoint & Video Compressor

PPTcrunch is a .NET 8 console application that compresses videos using quality-based FFmpeg encoding, with optional GPU acceleration. It supports both PowerPoint (.pptx) files with embedded videos AND individual video files, with wildcard support for batch processing.

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
- **Interactive Configuration**: Prompts for hardware acceleration, codec, quality level (1-4), and max width
- **Hardware Acceleration**: Uses NVIDIA NVENC on Windows and Apple VideoToolbox on macOS with CPU fallback
- **Smart Compression**: Keeps smaller MP4/PPTX media; explicit WebM and archive exports are retained even when larger
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
4. **Optional**: macOS 11+ with Apple silicon for VideoToolbox hardware acceleration

## Installation

### Pre-built Executable (macOS)

For macOS users, a pre-built, digitally signed executable is available for download:

1. **Download** the executable from: https://ab6d.com/wp-content/uploads/2025/09/pptcrunch-macos-final.zip
2. **Extract** the ZIP file to get the `pptcrunch` executable and README
3. **Make executable** (if needed):
   ```bash
   chmod +x pptcrunch
   ```
4. **Add to PATH** (recommended): Move `pptcrunch` to a directory in your system PATH, such as:
   ```bash
   sudo mv pptcrunch /usr/local/bin/
   ```
   Or create a local bin directory:
   ```bash
   mkdir -p ~/bin
   mv pptcrunch ~/bin/
   echo 'export PATH="$HOME/bin:$PATH"' >> ~/.zshrc
   source ~/.zshrc
   ```
5. **Run**: `pptcrunch <file-pattern>`

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
- ✅ **Auto-detection**: Detects NVIDIA NVENC and Apple VideoToolbox availability; falls back to CPU automatically
- ✅ **Hardware optimization**: Uses NVENC constant-quality mode (`-rc vbr` with `-b:v 0`) on Windows and VideoToolbox quality targeting (`-q:v` with `-b:v 0`) on macOS
- ✅ **Quality mapping**: CPU CRF, NVENC CQ, and VideoToolbox quality settings are aligned for comparable visual results per codec
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

Use GPU acceleration for faster encoding? (y/N, default: N): N

Video codec options:
  1. H.264 (better compatibility, works on older systems)
  2. H.265 (smaller files, better compression, newer standard)
Enter your choice (1 or 2, default: 2): 2

Quality level options:
  1. Good - smaller files
  2. Better - balanced quality and file size
  3. Indistinguishable in normal playback (target; lossy)
  4. Archive mode - extra detail for later recompression (lossy)
Enter your choice (1-4, default: 2): 2

Reduce high-resolution videos to maximum 1920 pixels wide (2K HD)? (Y/n, default: Y): Y

Selected settings:
  GPU acceleration: No
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
3. Example: `video.mov` → `video - L2H264.mp4` (Quality level 2, H.264 codec)
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
  - **Both width AND height are forced to even numbers** (for compatible 4:2:0 output)
- **Examples**:
  - Set to `1280`: A 1920x1080 video becomes 1280x720, but a 640x480 video stays 640x480
  - Set to `1920`: A 1921x1080 video becomes 1920x1080 (nearest even height)
  - Set to `3840`: Allows up to 4K resolution without downscaling

### Video Codec

- **1: H.264 / MP4**: widest device and PowerPoint compatibility. CPU libx264, NVIDIA NVENC, or Apple VideoToolbox.
- **2: H.265 / MP4** (default): better compression efficiency; requires HEVC playback support. CPU libx265, NVIDIA NVENC, or Apple VideoToolbox.
- **3: WebM / VP9**: standalone video exports for modern laptop and phone browsers. CPU libvpx-vp9, Profile 0, 8-bit `yuv420p`, with Opus audio. Compatible Opus/Vorbis audio is copied. NVENC and VideoToolbox do not encode VP9, so selecting WebM always uses the CPU.

WebM defaults to **two-pass encoding** for final website videos. Accept the default at the WebM prompt, then choose your quality level as usual. The first pass analyzes the original; the second reads the original again and writes the final video, so this does not add another lossy generation. One-pass encoding remains available: mostly static clips can be smaller in one pass because two-pass VP9 sometimes allocates extra bits to their detail. Neither mode guarantees the smallest possible file for every clip.

WebM is supported in current Chrome, Edge, Firefox and Safari. iPhone/iPad playback requires iOS/iPadOS 17.4 or later; macOS Safari supports WebM from 14.1. For older devices use H.264. See [WebKit's compatibility announcement](https://webkit.org/blog/15063/webkit-features-in-safari-17-4/).

PowerPoint and mixed PowerPoint/video batches offer MP4 codecs only. Run a separate standalone video batch to choose WebM. A hardware failure falls back to CPU encoding of the **same codec and user quality level**.

### Quality Level

Choose **1** (good/smaller), **2** (better/balanced, default), **3** (indistinguishable during normal playback, target), or **4** (archive mode for later recompression). All modes target quality and let video bitrate vary with the content. No video bitrate cap or floor is imposed. These are lossy encoder targets, not an objective minimum-quality guarantee; see the [mapping and limitations](#encoding-presets-and-quality-reference).

CPU encoding is now the default because file size at a given quality takes priority over speed. GPU encoding remains available through the hardware prompt. Changing the hardware choice can change file size and visual quality even at the same level.

## How It Works

1. **Backup Creation**: Copies the original PPTX to a ZIP file for processing
2. **Video Extraction**: Finds videos in the `ppt/media` directory within the PPTX
3. **Video Compression**: Uses FFmpeg with your selected settings, prioritizing hardware acceleration (NVENC on Windows, VideoToolbox on macOS) when available and ensuring even dimensions for encoder compatibility
4. **File Replacement**: Replaces original videos with compressed versions
5. **Reference Updates**: Updates all XML references to reflect any filename changes
6. **Final Assembly**: Creates the final compressed PPTX file

## FFmpeg Command Details

The shared `EncodingArguments` builder supplies the same video settings to the bundled and legacy external runners. Representative level-2 options (scaling and stream selection omitted):

| Encoder | Video options |
|---|---|
| H.264 CPU | `-c:v libx264 -crf 22 -preset slow -profile:v high -pix_fmt yuv420p` |
| H.265 CPU | `-c:v libx265 -crf 24 -preset slow -profile:v main -pix_fmt yuv420p -tag:v hvc1` |
| H.264 NVENC | `-c:v h264_nvenc -cq 26 -rc vbr -b:v 0 -preset p6 -tune hq -multipass 1 -rc-lookahead 32 -spatial-aq 1 -temporal-aq 1 -bf 3 -profile:v high -pix_fmt yuv420p` |
| H.265 NVENC | `-c:v hevc_nvenc -cq 26 -rc vbr -b:v 0 -preset p6 -tune hq -multipass 1 -rc-lookahead 32 -spatial-aq 1 -temporal-aq 1 -profile:v main -pix_fmt yuv420p -tag:v hvc1` |
| H.264 VideoToolbox | `-c:v h264_videotoolbox -q:v 65 -b:v 0 -realtime 0 -allow_sw 0 -profile:v high -pix_fmt yuv420p` |
| H.265 VideoToolbox | `-c:v hevc_videotoolbox -q:v 65 -b:v 0 -realtime 0 -allow_sw 0 -profile:v main -pix_fmt yuv420p -tag:v hvc1` |
| VP9 CPU, final pass | `-c:v libvpx-vp9 -crf 30 -b:v 0 -deadline good -cpu-used 2 -row-mt 1 -lag-in-frames 25 -g 240 -profile:v 0 -pix_fmt yuv420p -fps_mode passthrough -pass 2 -passlogfile "<job>/stats"` |

MP4 uses `-movflags +faststart`. The bundled runner copies AAC audio; other audio is converted to AAC at 192 kb/s (64 kb/s per channel above stereo). WebM converts incompatible audio to Opus VBR at 128 kb/s (64 kb/s per channel above stereo), 48 kHz and compression level 10. Existing Opus/Vorbis tracks are copied. The legacy external runner always transcodes audio. Silent inputs work, all audio tracks are retained, and only the first video stream is exported. Audio bitrate targets are separate from the video quality choice.

Scaling uses Lanczos, avoids upscaling apart from the minimum 2-pixel encoder dimension, and produces even dimensions. The generated FFmpeg command is printed during encoding.

## Output

- **Original file**: Remains unchanged (serves as backup)
- **PPTX output**: New file with "-shrunk" suffix
- **Video output**: New `.mp4` or `.webm` with user quality level and codec in the name (e.g., `name - L2H264.mp4`, `name - L2VP9.webm`). `L2` remains accurate after hardware fallback; old `Q...` names are still recognized.

When a standalone video exceeds the selected maximum width, its output name also includes the reduced width: a 4K `foo.mpg` exported at the 1920-pixel limit becomes `foo - L2VP9-1920.webm`. Inputs already at or below that limit keep the usual name. Custom width limits use their resulting even output width in the same suffix. If FPS reduction changes the rate, the new FPS is appended before the extension: `foo - L2VP9-25FPS.webm`, or `foo - L2VP9-1920-25FPS.webm` when both change. Unchanged rates receive no FPS suffix. These names retain the delivery/Archive recompression safeguards.
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
- **Output**: H.264/H.265 in MP4, or standalone VP9 in WebM.

### File Processing Modes

1. **PPTX Mode**: Extracts videos from PowerPoint, compresses them, and repackages into a new PPTX file with `-shrunk` suffix
2. **Video Mode**: Directly compresses individual video files with quality and codec information in filename (e.g., `video - L2H264.mp4`)
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
4. **Hardware acceleration not being used**: The program will show NVENC/VideoToolbox availability during startup
   1. On Windows ensure you have an NVIDIA GPU that supports NVENC (GTX 600+ or RTX series) and updated drivers
   2. On macOS ensure you're using the bundled Apple silicon build and grant macOS permission for VideoToolbox access
   3. The downloaded FFmpeg bundles include both NVENC and VideoToolbox support automatically
   4. If hardware acceleration fails, the program automatically falls back to CPU compression
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

- **Hardware Acceleration**: NVIDIA NVENC (Windows) and Apple VideoToolbox (macOS) provide faster compression when available
- **Automatic Fallback**: Falls back to CPU encoding if GPU is unavailable
- **Smart File Size Checking**: Uses smaller MP4/PPTX media; retains requested WebM or archive output even if larger
- **Progress Feedback**: Shows real-time FFmpeg output during compression
- **Improved Scaling**: Ensures even dimensions and preserves aspect ratio
- **Error Recovery**: Gracefully handles compression failures and keeps original videos

## Smart Compression Logic

The program uses intelligent decision-making for optimal results:

1. **CPU by Default**: Uses software encoding for compression efficiency; NVIDIA/Apple hardware is optional
2. **CPU Fallback**: Falls back to CPU encoding if hardware acceleration fails or is unavailable
3. **Size Comparison**: Compares compressed file size to original after encoding
4. **Output Policy**: MP4/PPTX delivery modes keep the smaller file. WebM and archive exports keep the requested format/quality, even when larger. Originals remain unchanged.
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

The **Enable frame-rate reduction?** option defaults to **N**, preserving the original frame rate. Selecting **Y** halves even integer rates of at least 48 FPS: 48→24, 50→25, 60→30, 90→45, and 120→60. This is one halving, not a 30 FPS cap. Lower rates (including 40 FPS), odd integer rates (such as 49 or 51), fractional rates (such as 47.952 or 59.94), and unknown rates remain unchanged. This applies to all codecs and CPU/GPU paths, including PowerPoint media and Archive mode. The app inspects each video's average frame rate, falling back to its nominal rate if needed, and does not round fractional rates into even integers.

Reduction uses timestamp-based frame selection at half the input rate before scaling, with the same filter in both VP9 passes. For constant-rate input this retains every other frame evenly, preserving sharp images and playback speed without blending or the uneven selection of 50→30. It still loses half the motion samples, so reduction is optional. Blending can soften or double moving edges; motion-compensated interpolation costs considerably more and can invent artifacts. Changed rates add a suffix such as `-25FPS` to standalone output filenames.

| Level | H.264 CPU CRF | H.264 NVENC CQ | H.265 CPU CRF | H.265 NVENC CQ | Apple Q (both codecs) | VP9 CRF |
|---|---:|---:|---:|---:|---:|---:|
| 1: good/smaller | 26 | 29 | 28 | 28 | 50 | 34 |
| 2: better/balanced | 22 | 26 | 24 | 26 | 65 | 30 |
| 3: normal-playback transparency target | 20 | 23 | 22 | 23 | 75 | 28 |
| 4: archive mode | 18 | 20 | 20 | 20 | 85 | 24 |

Lower CRF/CQ means higher quality. **Higher Apple Q means higher quality**. Numbers on different encoders are not interchangeable or calibrated to identical perceived quality. Level 3 targets perceptually transparent animation at normal playback speed, size and viewing distance; it allows differences visible only when paused or magnified. It is not a guarantee for every source. Level 4 retains extra detail for a later delivery encode, rather than trying to improve normal-viewing appearance. Archive mode remains lossy and cannot recover detail already removed from an input. Fine text, motion, grain and gradients can need different settings. Standard outputs are 8-bit 4:2:0; HDR tone mapping, 10-bit preservation, alpha preservation and lossless export are not provided.

- **CPU H.264/H.265:** use `slow` at every quality level. The preset owns B-frames, reference frames and lookahead. `slower`/`veryslow` are excluded to avoid steep time costs. CRF controls quality; preset changes can affect both fidelity and size, so a slower preset does not invariably produce a smaller file at identical CRF.
- **NVIDIA H.264/H.265:** explicit P6/HQ, quality-targeted VBR, 32-frame lookahead, spatial and temporal adaptive quantization, and quarter-resolution multipass. This multipass analyzes each frame; it is not a second traversal of the whole video. P7/full-resolution multipass are excluded as default choices. H.264 enables three B-frames. HEVC lets the hardware preset choose supported frame structures rather than forcing HEVC B-frames on older GPUs. Unsupported hardware/options trigger CPU CRF fallback.
- **Apple H.264/H.265:** ascending Q 50/65/75/85, non-realtime encoding, no bitrate target and no implicit software fallback. Constant-quality VideoToolbox encoding requires Apple silicon; unsupported combinations fall back to CPU CRF. Apple does not expose x264/NVENC-style speed presets or lookahead controls here.
- **VP9:** two-pass quality mode by default, `good`, first-pass `cpu-used 4` and final-pass `cpu-used 2`, row threading and 25-frame lookahead. Both passes use the same CRF, scaling and original frame timing. Pass one produces statistics only, with audio disabled; pass two handles audio and writes WebM. Each job has its own temporary statistics, cleaned up on success or failure, and an existing export is replaced only after both passes succeed. Each pass has a 60-minute timeout. `best` and speed 0 remain excluded. One-pass mode uses the same quality table and final encoding settings, without pass arguments. The keyframe interval can reach 240 frames, allowing smaller files while still supporting seeking.

[Encoding review and measured tradeoffs](ENCODING_REVIEW.md) records the evidence, limitations and primary references.

## Validation

```powershell
# Regression checks (no additional testing packages)
dotnet run --project tests/PPTcrunch.Tests.csproj -p:SelfContained=false -p:PublishSingleFile=false -p:PublishReadyToRun=false

# Real FFmpeg encodes and decoded-frame quality checks; omit --nvenc without NVIDIA hardware
dotnet run --project tests/PPTcrunch.Tests.csproj -p:SelfContained=false -p:PublishSingleFile=false -p:PublishReadyToRun=false -- --integration --nvenc

# Bounded preset comparisons on a representative local input
./tests/benchmark-presets.ps1 -InputVideo ./sample.mkv -FFmpeg C:/ffmpeg/ffmpeg.exe -Nvenc

# VP9 one/two-pass quality curves on the repository's local recordings and a moving pattern
python tests/benchmark-vp9.py --ffmpeg C:/ffmpeg/ffmpeg.exe
```

Integration tests write their generated videos and measurements under `artifacts/encoding-tests`. Benchmarks use the first three seconds, scaled to 1280 pixels wide, and write results under `artifacts/preset-benchmark`. Short samples are useful checks, not universal quality or timing guarantees.

The VP9 benchmark separately uses up to four seconds at 1280 pixels wide and writes `artifacts/vp9-benchmark/results.json`. It measures file size, total encoding time, decoded frame count, frame-aligned VMAF (mean and tenth percentile), PSNR and SSIM across multiple CRFs. Compare similar measured quality, not just identical CRF numbers. The regression harness also checks two-pass failure/cancellation cleanup, preservation of earlier exports, audio routing and variable-frame-rate timing.

## Technical Notes

- PowerPoint media and XML references are updated together in a separate output presentation.
- Successful standalone WebM exports and explicitly requested archive outputs are retained even when larger; the selected quality is never lowered just to beat the original size.
- Filenames recognize both legacy `Q` values and new `L` user levels. An already-compressed video can be converted to another codec; same-codec delivery outputs are skipped to prevent accidental recompression. Archive outputs ending in `-L4H264.mp4`, `-L4H265.mp4` or `-L4VP9.webm` can be recompressed into levels 1-3, including the same codec. Same-codec archive-to-archive runs are skipped.
- FFmpeg errors and timeouts retain the original input; hardware failures retry with the selected software codec and quality level.
