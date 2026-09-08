# Encoder/platform audit - September 8, 2026

All 16 OS/codec/CPU-or-hardware combinations are accounted for below. Windows hardware in this app means NVIDIA NVENC; macOS hardware means Apple VideoToolbox. Intel Quick Sync, AMD AMF and Windows ARM Media Foundation are not implemented backends. These are encoder choices, not a claim that every installed FFmpeg build includes every library or that every GPU implements every codec.

| Codec | Windows CPU | Windows hardware requested | macOS CPU | macOS hardware requested |
|---|---|---|---|---|
| H.264 | `libx264`, CRF, slow | `h264_nvenc`, CQ/VBR, P6; CPU fallback | Native `libx264`, CRF, slow | `h264_videotoolbox`, quality mode; CPU fallback |
| H.265 | `libx265`, CRF, slow | `hevc_nvenc`, CQ/VBR, P6; CPU fallback | Native `libx265`, CRF, slow | `hevc_videotoolbox`, quality mode; CPU fallback |
| WebM/VP9 | `libvpx-vp9`, quality-targeted two-pass by default | CPU `libvpx-vp9`; no NVENC VP9 encoder | Native `libvpx-vp9`, same quality/two-pass policy | CPU `libvpx-vp9`; no VideoToolbox VP9 encoder |
| AV1 | `libsvtav1`, CRF, preset 4 | `av1_nvenc`, CQ/VBR, P6 when supported; CPU fallback | Native `libsvtav1`, CRF, preset 4 | CPU `libsvtav1`; no VideoToolbox AV1 encoder |

## Findings and corrections

The libraries, rate-control families, encoder arguments and existing runtime CPU fallbacks were appropriate. No change to the quality tables was needed for this audit. Hardware detection did need correction:

- Previously, a compiled FFmpeg H.264/HEVC encoder entry could be reported as working hardware; model-name guesses could override failed/missing capability information. Now each supported hardware codec is verified by a short encode using the application's actual level-2 quality settings, pixel format, preset and lookahead. Model names are descriptive only.
- Windows probes NVENC, while macOS probes VideoToolbox, preventing an irrelevant compiled backend from taking priority over the OS-native backend. `nvidia-smi` is useful for a name but is not a prerequisite for a successful NVENC probe.
- Probes use 640x360 for two seconds, output only to the null muxer, drain both process streams, and have a 15-second timeout. Tiny probes can falsely fail minimum encoder dimensions. The old legacy NVENC check used 2x2 video and also leaked empty files through `GetTempFileName`; it now shares the same file-free probe.
- VP9 hardware requests still route directly to CPU. Mac AV1 requests still route directly to CPU. Their quality levels remain unchanged.
- Startup records which of the four CPU libraries FFmpeg reports. Selection of an unavailable CPU encoder produces an actionable error; a hardware selection with a missing CPU fallback gives a warning.
- Mac download architecture now follows the OS architecture, so an Intel app process running under Rosetta does not cause a new Intel FFmpeg download onto Apple silicon. The existing Mac publishing/signing scripts target `osx-arm64`.

A startup probe confirms availability, not every resolution, source pixel format or selected quality level. Actual conversion can still fail; both runners retain CPU fallback to the same codec/quality level. It also does not prove visual equivalence between hardware and CPU quality numbers.

## Platform-specific assistance

The native libraries are the appropriate CPU approach. x264, x265, libvpx and SVT-AV1 contain architecture-specific optimized routines, and the app does not disable assembly/SIMD detection or impose a one-thread limit. ARM64 builds can use ARM instructions; x86-64 builds can use supported x86 vector instructions. Available optimizations depend on the library version/build and processor. Adding VideoToolbox to a software encoder is not a way to accelerate that encoder's compression decisions.

Native FFmpeg is important on Apple silicon: an old x86-only FFmpeg running under Rosetta can lose CPU performance and cannot provide the FFmpeg quality-mode VideoToolbox path used here. Existing FFmpeg files are reused, not silently replaced. This Windows audit cannot certify a particular installed Mac binary's architecture or compiled optimizations. On the Mac, inspect the selected FFmpeg/ffprobe with `file`, and check `ffmpeg -version` and `ffmpeg -encoders`; use a native ARM64 (or ARM64-capable universal) full build with the four named CPU encoders. Merely naming an encoder cannot prove that an old build contains current optimizations.

VideoToolbox accesses Apple's media engine (or supported hardware selected by macOS); it is not necessarily computation on general GPU cores. FFmpeg's current quality mode is restricted to Apple silicon. Intel Macs may have H.264/HEVC hardware encoding, but their bitrate-based interface does not satisfy this app's quality-targeted policy, so those tests fall back to CPU. Apple AV1 decoding capability is not AV1 encoding capability.

The app still decodes, selects frames, resizes with Lanczos, and handles audio on the CPU. Hardware-assisted decoding/scaling is a separate optimization; it is not required to use NVENC/VideoToolbox encoding. It was not added because encoding speed is secondary here, it complicates format/filter interoperability, and a different hardware scaler need not match the selected image-quality behavior. CPU remains the default.

## Evidence and validation

- Tests cover all 16 routing combinations plus all four quality levels and encoder-specific arguments in the existing matrix.
- On this Windows machine, actual probes verify H.264 and HEVC NVENC, reject AV1 NVENC on the GTX 1660 SUPER, and confirm all four software encoders in FFmpeg 6.1. The legacy probe agrees with the new detector.
- The AV1 addition's full integration run already validated all four CPU codecs, H.264/HEVC NVENC quality progression, both runners and frame-rate behavior. This audit changes detection/download architecture, not their encoding settings.
- AV1 fallback/workflow checks passed after the detection changes. Windows build and interactive CLI checks verify the new capability/encoder selection path.
- No live Mac or AV1-capable NVIDIA GPU was available. Those paths have code/documentation coverage and runtime probes, not a claim of local hardware validation. Intel/AMD Windows backends would be a separate feature.

Reproduce routing and live capability checks with `dotnet run --project tests/PPTcrunch.Tests.csproj -p:SelfContained=false -p:PublishSingleFile=false -p:PublishReadyToRun=false -- --platform`. Add `--nvenc` on the known H.264/HEVC-capable NVIDIA test machine, and `--av1` for AV1 workflow/fallback checks.

## Primary references

- [FFmpeg encoder/library documentation](https://ffmpeg.org/ffmpeg-codecs.html).
- [NVIDIA's NVENC guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/nvenc-video-encoder-api-prog-guide/index.html): H.264/HEVC/AV1 encoder capabilities and rate control; VP9 is a decoder capability, not an NVENC encoder.
- [FFmpeg VideoToolbox implementation](https://www.ffmpeg.org/doxygen/8.0/videotoolboxenc_8c_source.html): Apple-silicon restriction on `q:v`, hardware encoder setup and quality handling.
- [HandBrake's VideoToolbox engineering documentation](https://handbrake.fr/docs/en/latest/technical/video-videotoolbox.html): media-engine selection, software stages and hardware-scaler tradeoffs.
- [Apple media-engine specifications](https://www.apple.com/macbook-pro/specs/): H.264/HEVC/ProRes encoding and AV1 decoding.
- [VideoLAN x264](https://code.videolan.org/videolan/x264), [Arm's x265 optimization guide](https://learn.arm.com/learning-paths/servers-and-cloud-computing/codec/x265/), [libvpx changes](https://github.com/webmproject/libvpx/blob/main/CHANGELOG), and [AOMedia implementation report](https://aomedia.org/docs/Software_Implementation_Working_Group_Update_ICIP2024.pdf): native library/ARM optimization background.
