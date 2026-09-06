# Encoding review — September 6, 2026

## Quality goals

1. **Good:** favor smaller delivery files.
2. **Better:** a balance of quality and size.
3. **Indistinguishable during normal playback:** aim for perceptual transparency at normal speed, display size and viewing distance. Differences found by freezing frames or magnifying pixels are acceptable.
4. **Archive mode:** retain extra detail for a later delivery encode. This is a high-quality lossy intermediate, not a lossless master, and cannot restore detail already missing from its input.

The app targets quality and lets video bitrate vary. It does not lower quality to meet a file-size target. Its four presets are practical starting points, not a content-adaptive search for the smallest file meeting a measured threshold. No fixed CRF/CQ/Q table can guarantee the same human perception for every scene and display. The tests below validate encoding behavior and provide limited calibration evidence; they are not human viewing studies.

## Final mapping

| Backend | Good | Better | Normal-playback transparency | Archive |
|---|---:|---:|---:|---:|
| H.264 CPU CRF | 26 | 22 | 20 | 18 |
| H.265 CPU CRF | 28 | 24 | 22 | 20 |
| H.264 NVENC CQ | 29 | 26 | 23 | 20 |
| H.265 NVENC CQ | 28 | 26 | 23 | 20 |
| VP9 CPU CRF | 34 | 30 | 28 | 24 |
| Apple Q, H.264 and H.265 | 50 | 65 | 75 | 85 |

These scales are encoder-specific. In particular, copying CPU CRF values directly to NVENC CQ spent substantially more bits on the synthetic fixture without matching the intended level. NVIDIA values now have their own mapping. Apple Q increases with quality; other columns decrease.

The CPU targets were cross-checked against [HandBrake's x264/x265 quality guidance](https://handbrake.fr/docs/en/latest/workflow/adjust-quality.html), which recommends RF 20–24 as a starting range for 1080p and explains why encoder scales, display size and viewing distance matter. The guidance does not declare one number universally visually transparent. [Apple's quality-property documentation](https://developer.apple.com/documentation/videotoolbox/kvtcompressionpropertykey_quality) defines its ascending scale and identifies 0.75 as high quality. FFmpeg maps its Q value into that property; Apple values here have not been tested on a Mac.

## Rate control and encoding effort

| Path | Decision and reason |
|---|---|
| CPU H.264/H.265 | CRF with `slow` for every level. Remove the old forced B-frame/reference counts so the preset can choose a coherent configuration. Do not use `slower`/`veryslow` by default. |
| NVENC H.264/H.265 | CQ + `-rc vbr -b:v 0`, P6/HQ, 32-frame lookahead, spatial/temporal AQ and quarter-resolution multipass. No maximum bitrate that could prevent reaching the quality target. Avoid P7/full-resolution multipass as defaults. H.264 uses three B-frames; HEVC leaves frame structure to the hardware preset to avoid requiring HEVC B-frames on older GPUs. |
| Apple H.264/H.265 | `-q:v`, `-b:v 0`, non-realtime, explicit hardware encoding. Unsupported quality mode falls back to CPU CRF at the same user level. No made-up equivalent for x264 presets or NVIDIA lookahead. |
| VP9 | Two-pass `libvpx-vp9 -crf … -b:v 0 -deadline good`, first-pass speed 4 and final-pass speed 2, row threading, 25-frame lookahead and up to 240 frames between keyframes. One pass remains selectable for static material. Avoid `best` and speed 0 as defaults. |

[NVIDIA's programming guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/nvenc-video-encoder-api-prog-guide/index.html) explains quality-targeted VBR, bitrate-cap effects, lookahead, AQ and the tradeoffs between multipass modes. AQ redistributes detail perceptually, so raw PSNR alone can favor an encoder without AQ. [x265's preset documentation](https://x265.readthedocs.io/en/master/presets.html) describes the coordinated speed/efficiency changes within presets.

[Google's VP9 bitrate-mode guide](https://developers.google.com/media/vp9/bitrate-modes/) documents zero-bitrate quality mode and cautions that `best` offers marginal gains. Its separate [VOD recommendations](https://developers.google.com/media/vp9/settings/vod/) recommend two passes and speeds 4/2 for 720p and larger outputs. We adopt that effort level with quality mode, without their streaming bandwidth caps, because the user selects quality rather than a delivery bitrate. [FFmpeg's libvpx documentation](https://ffmpeg.org/ffmpeg-codecs.html#libvpx) documents the pass flags, lookahead and encoder options. Two-pass VP9 is not presumed to give a smaller file at identical CRF; it changes rate allocation and must be compared at comparable fidelity.

## Bugs and output handling corrected

- The bundled CPU path hardcoded `medium` and ignored configured `slow` settings.
- Apple quality levels were reversed: selecting a higher user level reduced its Q target.
- A stub capability check always disabled NVIDIA HQ tuning/multipass.
- CPU B/reference-frame overrides partially defeated the selected preset. Forced HEVC B-frames could reject older NVIDIA hardware.
- Independent bundled/external recipes could diverge. They now use a shared option builder.
- GPU fallback and output filenames could misreport the settings used. Outputs now use `L1`–`L4` for user level and report the actual backend.
- Standalone WebM includes the correct container, VP9 Profile 0/8-bit 4:2:0 and browser-compatible audio. MP4 uses fast-start layout, 4:2:0 and `hvc1` for HEVC.
- AAC/PCM input audio is converted as required for WebM; compatible audio is preserved by the bundled runner. Silent videos and multiple audio tracks are supported.
- WebM and explicitly requested archive outputs are retained even when larger. Delivery MP4/PPTX media retains the existing smaller-file policy.
- Archive filenames are recognized as reusable inputs for levels 1–3, including same-codec recompression. Same-codec delivery outputs and archive-to-archive runs remain protected against accidental repeated batch compression.

WebM is offered for standalone video batches, with PowerPoint batches restricted to MP4. [WebKit's Safari 17.4 announcement](https://webkit.org/blog/15063/webkit-features-in-safari-17-4/) establishes WebM support on iOS/iPadOS 17.4 and macOS Safari 14.1. Older devices can use H.264. NVENC and VideoToolbox do not encode VP9; the VP9 option deliberately uses CPU encoding.

## Measurements and validation

Tests used the bundled FFmpeg 6.1 and NVIDIA GTX 1660 SUPER on Windows. All 20 combinations of four levels across CPU H.264, CPU H.265, NVENC H.264, NVENC H.265 and CPU VP9 encoded successfully. Frame-index-aligned PSNR increased at every quality level on the four-second 640×360 moving test pattern. VMAF is an additional perceptual proxy, not a transparency guarantee; synthetic footage is especially limited for perceptual calibration.

| Backend | Level 3 bytes | Level 3 VMAF | Archive bytes | Archive VMAF |
|---|---:|---:|---:|---:|
| H.264 CPU | 529,714 | 99.06 | 598,284 | 99.42 |
| H.265 CPU | 494,600 | 99.41 | 554,490 | 99.64 |
| H.264 NVENC | 550,217 | 99.43 | 683,461 | 99.75 |
| H.265 NVENC | 554,478 | 99.29 | 677,516 | 99.70 |
| VP9 CPU, two passes | 445,370 | 99.11 | 511,661 | 99.43 |

CPU archive → level-3 delivery recompression was also tested against the original fixture: H.264 VMAF 98.53, H.265 99.01, and two-pass VP9 98.56. The VP9 check used the rebuilt executable to recompress its two-pass Archive output. This confirms the intended workflow on that fixture and also demonstrates generation loss.

Preset comparisons used the first three seconds of the repository's `2025-09-27_16-05-44_1920x1080@30.mkv`, normalized to 1280×720 4:2:0. These are short, single-run timings including process startup, not robust throughput benchmarks:

- x264: `slow` took 0.32 s versus 0.27 s for `medium`; `slower` took 0.43 s. At fixed CRF, fidelity and size both changed; slower does not imply smaller at identical CRF.
- x265: `slow` took 0.70 s versus 0.45 s for `medium`; `slower` took 2.70 s. This supports stopping at `slow` for the desired time tradeoff.
- VP9, initial static-clip comparison: speed 2 took 0.98 s and 98,025 bytes. Speed 0 took 1.39 s and 97,308 bytes: about 42% more time for 0.7% fewer bytes. Speed 1 saved just 59 bytes versus speed 2. This supported the effort limit, but the initial same-CRF two-pass comparison did not establish compression efficiency. The quality-curve study below supersedes that comparison for the multipass decision.
- NVENC: P7 produced essentially the same output size as P6 on this GPU/sample. This does not establish equivalence across other hardware or content.

Other checks passed: actual codec/profile/pixel format/audio, odd dimensions, very small width, silent input, forced hardware failure and CPU fallback, external-runner WebM output, larger WebM/archive retention, same-codec archive recompression, and PowerPoint media/reference rewriting with original-file hash preservation. Generated WebM played in the in-app browser with advancing decoded frames and no media error. Apple encoding and physical phone playback were not available for live testing.

## VP9 multipass follow-up

For the user's final website export workflow, two passes are now the WebM default. The four CRF values remain 34/30/28/24: the curves do not support a single global offset between one and two passes across different content. One pass is selectable in the WebM prompt because static material can behave differently. This is an encoding-efficiency preset, not an automatic VMAF search or a human-transparency guarantee.

`tests/benchmark-vp9.py` measured 40 encodes: three local screen recordings (up to four seconds each) and a four-second moving test pattern, normalized to 1280×720 4:2:0 at 30 fps. One-pass CRFs were 24/28/30/34; two-pass CRFs were 24/28/30/34/38/42. Audio was excluded to isolate video compression. Frame counts were verified and quality metrics compared matching decoded frame indices. Timings include startup and both passes; they are short, single-run measurements on a shared workstation, with observed timing variation.

Examples with nearly matched VMAF around the normal-viewing preset:

| Clip | One-pass / two-pass CRF | One-pass / two-pass bytes | VMAF mean | VMAF tenth percentile | Bytes saved | Total encode time |
|---|---|---|---|---|---:|---|
| `2025-09-14_15-00-55` | 28 / 28 | 745,654 / 704,902 | 95.536 / 95.506 | 93.473 / 93.449 | 5.5% | 2.62 / 6.48 s |
| `2025-09-14_15-22-17` | 28 / 30 | 540,296 / 445,637 | 95.560 / 95.602 | 93.667 / 93.973 | 17.5% | 2.68 / 6.17 s |
| Moving test pattern | 28 / 28 | 2,028,780 / 1,750,982 | 98.268 / 98.269 | 97.377 / 97.508 | 13.7% | 2.32 / 5.49 s |

The second recording uses a different CRF in the matched-quality comparison intentionally. At the app's unchanged CRF 28, two passes saved 3.0% on that clip and increased mean VMAF by 0.23. Across these three moving clips, the unmodified normal-viewing preset saved 3–14%. PSNR and SSIM were also recorded; no single metric establishes equal perceived quality. These samples support spending roughly 2.3–2.5 times the encoding time for final exports, rather than using the much more expensive `best` mode.

The nearly static `2025-09-27_16-05-44` is the counterexample. At CRF 28, one pass used 103,400 bytes and two passes used 160,803 (+55.5%), with mean VMAF increasing from 96.770 to 97.285. Even two-pass CRF 42 still used more bytes than one-pass CRF 28. Increasing all presets' CRFs to fix this special case would damage moving material. Retaining a one-pass choice is preferable to promising universal savings or restricting reference-frame quality with an unvalidated quantizer cap.

Both passes read the original input, using identical scaling, pixel format and passthrough frame timing. Pass one disables audio and writes only temporary statistics; pass two retains the normal audio handling. Thus two passes do not add another lossy generation. Job-specific statistics and staged outputs prevent collisions and keep earlier exports intact on failure. Automated checks cover pass order, failed analysis, failed final encoding, cancellation cleanup, audio-option preservation, all four VP9 levels in real FFmpeg, both runners' one/two-pass paths, and a variable-frame-rate fixture with matching frame counts and timestamps.

The rebuilt Windows executable passed interactive-input CLI checks for both WebM modes and Archive-to-delivery recompression. A new two-pass output played through in the in-app browser (`currentTime = duration = 4.008`, `ended = true`, `readyState = 4`, no media error). The refreshed executable is `publish/webm-update/pptcrunch.exe`; this does not replace an independently installed copy on PATH.

VP9 cleanup retries transient I/O/access failures up to six times, with 100/200/400/800/1600 ms delays (3.1 seconds total). This also handles partial cleanup where the files were deleted but the empty working directory could not yet be removed. An already-removed directory counts as success; a persistent failure still reports the exact folder path. Regression tests inject partial deletion and transient failures, verify eventual folder removal, and check that persistent failures stop after the retry limit. The user's original filesystem failure was not reproduced locally.

## Optional high-frame-rate reduction

Frame-rate reduction now defaults to **off**. When enabled, an even integer source rate of at least 48 FPS is halved exactly once: 48→24, 50→25, 60→30, 90→45, 120→60. All other rates are preserved, including 40, 49, 51, 47.952 and 59.94 FPS. Keeping odd/fractional rates is a conservative choice permitted by the user; no approximate rounding converts them into eligible rates. Both runners use FFprobe's average rate, with nominal rate as fallback. All codecs use passthrough output timing so unaffected variable-rate sources are not implicitly made constant-rate. Archive mode follows the explicit user setting too.

[FFmpeg's FPS filter](https://ffmpeg.org/ffmpeg-filters.html#fps) selects/drops/duplicates frames on the target timeline. We use nearest timestamp rounding at half the input rate before scaling, preserving source-frame sharpness and duration rather than changing playback speed. Both VP9 passes use the identical per-video filter chain. Regression coverage includes lower/odd/fractional rate retention; 48/50/60/90/120 halving; opt-out; CPU H.264/H.265/VP9, NVIDIA H.264/H.265, both runners, audio retention and duration. A frame-hash check confirms that 60→30 filtering retains actual input images without blending. The existing VFR timestamp regression checks preservation when reduction is disabled.

The user's frequent 50 FPS sources motivated replacing the original 30 FPS cap: 50→30 selects source frames at 20/40 ms intervals for display at regular 33.3 ms intervals, causing uneven motion steps. The selected 50→25 conversion keeps every other frame at consistent 40 ms intervals. Halving constant-rate input avoids this conversion-induced cadence irregularity, but does not retain the full smoothness of the original high rate or guarantee perfect motion on every display.

A bounded test used a two-second 640×360 moving pattern sampled to 50 FPS from a 150 FPS reference. Conversion to lossless 30 FPS output took 0.13 s with `fps=30`, 0.16 s with `framerate=fps=30` (blending), and 3.19 s with `minterpolate=fps=30:mi_mode=mci:mc_mode=aobmc:me_mode=bidir:me=epzs:search_param=16`. Inspection showed softened/doubled edges with blending. Motion compensation improved that edge but was roughly 24 times as expensive in this short filter-only test and emitted 59 rather than 60 frames, so adopting it would also require deliberate end-of-video handling. These are single-run synthetic observations, not a general perceptual evaluation. See [FFmpeg's motion-interpolation options](https://ffmpeg.org/ffmpeg-filters.html#minterpolate). Blending and motion compensation are not defaults.

Run the regression harness and preset benchmark using the commands in [README](README.md#validation). Add `--vmaf` to the integration command when FFmpeg includes `libvmaf`. Generated measurements and videos are under `artifacts/`; they are intentionally not committed.
