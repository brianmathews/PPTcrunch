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
| AV1 SVT CPU CRF | 34 | 30 | 26 | 20 |
| AV1 NVENC CQ (provisional) | 28 | 26 | 23 | 20 |
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


## AV1 web delivery addition - September 8, 2026

**Output:** a separate MP4 containing AV1 Main-profile 10-bit YUV 4:2:0 video (`av01`) and AAC audio, with `faststart`. Windows and macOS CPU encoding use `libsvtav1`. Optional NVIDIA encoding uses `av1_nvenc`; an actual short encode with the chosen output settings verifies support. An FFmpeg encoder listing alone is insufficient. This machine's GTX 1660 SUPER correctly fails the AV1 probe and uses CPU encoding. GPU failure during a real job still retries the same codec and user quality level on the CPU.

AV1 is a standalone export. PowerPoint and mixed batches continue to offer H.264/H.265. Filenames, resolution/FPS modifiers, reusable archives, and same-codec delivery protection include AV1. Explicit AV1 exports are retained even if larger than the source. No additional VP9 temporary directories or two-pass statistics are created for AV1.

### Browser and hardware boundary

[WebKit's Safari AV1 announcement](https://webkit.org/blog/14445/webkit-features-in-safari-17-0/) explicitly requires hardware AV1 decoding, including iPhone 15 Pro. Safari on M2 Macs and standard iPhone 15 devices cannot be assumed to play AV1 merely because the operating system/browser is current. Current Chrome/Edge/Firefox support AV1 on supported platforms, with software or hardware decoding as available. Browser playback on actual iOS, Android and macOS devices has not been tested here.

A site serving both groups should offer **separate AV1 and H.264 files**, with correct codec declarations/source selection. The browser fetches the selected alternative; neither PPTcrunch nor this delivery scheme combines the two codecs in a single download. Range requests may help seeking/progressive delivery, but they are not the mechanism for choosing between these encodings. An AV1-only export is still useful when the website's audience has compatible devices.

[NVIDIA's encoder guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/nvenc-video-encoder-api-prog-guide/index.html) documents AV1 encoding starting with Ada hardware. [Apple's current MacBook Pro specifications](https://www.apple.com/macbook-pro/specs/) list AV1 decoding, alongside H.264/HEVC/ProRes encoding. This app therefore uses CPU AV1 on Mac; its existing H.264/HEVC VideoToolbox paths remain available.

### Quality and speed choices

SVT-AV1 **preset 4, `tune=0`, CRF 34/30/26/20**, with up to 240 frames between keyframes. This favors efficient final delivery, avoids the most expensive presets, and keeps grain synthesis/denoising disabled for screen recordings and animation. [SVT-AV1's guidance](https://gitlab.com/AOMediaCodec/SVT-AV1/-/blob/v2.1.0/Docs/CommonQuestions.md) groups presets 4-6 as balanced and 1-3 as appropriate when encode time matters little. The installed SVT-AV1 1.7 accepts these options. Newer releases may change their behavior.

[HandBrake's quality guidance](https://handbrake.fr/docs/en/1.6.0/workflow/adjust-quality.html) recommends SVT RF 25-35 as a starting range for 720p/1080p. Good and Better sit within that range; level 3 uses 26 to favor fidelity without spending archive-level bits. Archive uses 20 as extra lossy headroom for subsequent compression, not a lossless or guaranteed generation-proof master. The values are engineering starting points, not conversions of x264/x265 CRF numbers or a guarantee of visual transparency.

NVENC uses **CQ 28/26/23/20**, P6/HQ, uncapped quality-targeted VBR, quarter-resolution multipass, 32-frame lookahead, spatial/temporal adaptive quantization and 10-bit input (`p010le`). These CQ values deliberately use the hardware encoder's own scale. Successful AV1 hardware encoding and quality equivalence could not be measured on this GPU, so the AV1 CQ table is provisional. CPU encoding remains the default for quality/size priority.

Multi-pass was considered. SVT guidance describes its principal benefit as bitrate allocation for a specified VBR budget, with some potential CRF gains for difficult high-motion material. CPU AV1 here uses single-pass CRF: no fixed bitrate target, no blind reuse of libvpx's two-pass workflow, and no claim that multipass can never improve AV1. NVENC's selected multipass operates internally during encoding, not by encoding the entire file twice.

### 8-bit sources and 10-bit output

The chosen output remains 10-bit even from 8-bit RGB or YUV input. It does not restore missing colors, create HDR or preserve full RGB chroma; it gives compression/resampling additional numerical precision. SVT documentation identifies reduced rounding as a potential fidelity benefit with possible size/decode-cost tradeoffs. Hardware AV1 Main support includes 8/10-bit 4:2:0.

Two normalized 8-bit, two-second 1280x720 samples were compared at preset 4. On the screen recording, 10-bit was 3.3-9.5% smaller across the four CRFs, with VMAF improvements of 0.12-0.18. On the synthetic motion pattern, size changes ranged from 1.7% smaller to 4.0% larger and VMAF was lower by 0.02-0.17. Thus 10-bit is a practical default for the user's screen/animation workflow, not a universal reduction in file size or proof of visible improvement. Metrics were computed after conversion to a common 8-bit pixel format against equal-length references.

At CRF 30, preset 4 versus 6 reduced screen bytes by 7.5% and motion bytes by 3.0%, with slightly higher VMAF. Preset 3 reduced bytes another 7.2%/15.5% but slightly lowered VMAF at the same CRF and took substantially longer. This is not an equal-quality rate/distortion proof: CRF output varies with preset. Timing on this workstation was noisy due to other local activity; it supports only a broad slowdown comparison. The official preset guidance and these bounded measurements support preset 4 as the default compromise.

### Validation and limits

- Full regression/integration matrix passed for all H.264/H.265/VP9/AV1 CPU quality levels and available H.264/HEVC NVENC paths. AV1's four synthetic 640x360 outputs grew monotonically in file size and PSNR/VMAF with quality level; VMAF was 98.82/99.28/99.58/99.79. These scores do not establish human transparency on real footage.
- AV1 workflow tests passed for 8-bit RGB input, 10-bit Main/`av01` output, silent video, fast-start metadata placement, changed dimensions/FPS and filenames, larger-output retention, unsupported-NVENC fallback, archive reuse, PowerPoint exclusion and VFR timestamp preservation. The shared integration tests cover AAC audio and both FFmpeg runners.
- Windows in-app browser played the generated AV1 MP4 to completion: 25 decoded frames, 160x90, one second, no media error. This verifies one Chromium-based browser, not all target platforms.
- Published Windows executable was exercised through its interactive prompts, including requested GPU use falling back to CPU, AV1 selection, resizing and 50-to-25 FPS conversion.
- CPU FFmpeg requires `libsvtav1`; older builds can reject dimensions below 64 pixels. Existing per-encode 60-minute timeout remains. No full HDR metadata/tone-mapping workflow was added. The application and source should be rebuilt on macOS; successful AV1 Mac/NVIDIA hardware playback/encoding has not been claimed.

Reproduce with `--integration --nvenc --vmaf`, `--av1`, and `python tests/benchmark-av1.py --ffmpeg C:/ffmpeg/ffmpeg.exe sample.mkv`. Use `--av1-nvenc` only on AV1-capable hardware. Raw measurements are under `artifacts/encoding-tests` and `artifacts/av1-review`; these generated files are ignored by Git.


## Passable level addition - September 8, 2026

Passable is **level 0**, below the existing levels 1-4. This preserves the meaning of legacy filenames, particularly L4 reusable archives. Better (2) remains the default. Passable targets smaller delivery files with minor, non-distracting artifacts during normal playback; no fixed quality value can guarantee that perception for every scene or display.

| Encoder | Passable (0) | Good (1) |
|---|---:|---:|
| H.264 CPU CRF | 28 | 26 |
| H.265 CPU CRF | 30 | 28 |
| VP9 CPU CRF | 38 | 34 |
| AV1 SVT CPU CRF | 38 | 34 |
| H.264 NVENC CQ | 32 | 29 |
| H.265 NVENC CQ | 31 | 28 |
| AV1 NVENC CQ | 31 | 28 |
| Apple H.264/H.265 Q | 40 | 50 |

These are incremental steps on each encoder's own scale. Software speed presets, visual tuning, VP9 two-pass selection, hardware multipass, audio and pixel formats stay unchanged. The smaller file comes from accepting more compression loss, not switching to a faster/less efficient encoder preset. [HandBrake's guidance](https://handbrake.fr/docs/en/latest/workflow/adjust-quality.html) supports small quality adjustments and emphasizes encoder-specific scales and content dependence; it does not prescribe these exact values.

Both compression and capture/transcode quality menus accept 0-4. Passable filenames use `-L0`, with the existing optional width/FPS modifiers. Old L1-L4 meanings, archive-to-delivery behavior, defaults and PowerPoint codec restrictions remain unchanged. The existing parameter/real-encoding matrix now includes all five levels. Apple and AV1 NVIDIA values remain uncalibrated on live hardware here.

Validation: on the existing four-second 640x360 moving-pattern fixture with audio, level 0 files were 17.2% (x264), 15.3% (x265), 15.8% (VP9), and 15.6% (SVT-AV1) smaller than Good. NVENC reductions were 22.6% (H.264) and 22.5% (HEVC). File size and PSNR increased monotonically through levels 0-4. These are fixture measurements, not a guarantee of savings or a human-perception study. The published CLI accepted level 0 and generated an AV1 file with both L0 and changed-FPS suffixes.
