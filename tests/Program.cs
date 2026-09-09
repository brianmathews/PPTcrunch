using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using PPTcrunch;

if (args.Length == 2 && args[0] == "--capture-pipe-child")
{
    CapturePipeTests.Child(args[1]);
    return;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (var codec in Enum.GetValues<VideoCodec>())
{
    int lastCrf = 100, lastCq = 100, lastApple = 0;
    for (int level = 0; level <= 4; level++)
    {
        var settings = new UserSettings { Codec = codec, QualityLevel = level };
        var cpu = QualityConfigService.GetEncodingSettings(level, codec, false);
        Check(cpu.Crf < lastCrf, $"CPU quality direction: {codec}");
        lastCrf = cpu.Crf!.Value;
        foreach (var mode in Enum.GetValues<HardwareAccelerationMode>())
        {
            var options = string.Join(" ", EncodingArguments.Build(settings, mode));
            Check(!options.Contains("-maxrate") && !options.Contains("-minrate"), "No video bitrate caps/floors");
            Check(options.Contains(codec == VideoCodec.AV1 ? (mode == HardwareAccelerationMode.NvidiaNvenc ? "-pix_fmt p010le" : "-pix_fmt yuv420p10le") : "-pix_fmt yuv420p"), "Browser pixel format");
            Check(options.Contains("-map 0:a?"), "Silent inputs and audio tracks supported");
            if (codec == VideoCodec.VP9)
            {
                Check(options.Contains("-c:v libvpx-vp9") && options.Contains("-b:v 0") && options.Contains("-f webm"), "VP9 CPU quality mode/container");
                Check(options.Contains("-profile:v 0") && options.Contains("-c:a libopus"), "WebM video/audio compatibility");
                Check(!options.Contains("-preset") && !options.Contains("hvc1") && !options.Contains("faststart"), "No H26x flags in WebM");
            }
            else if (mode == HardwareAccelerationMode.None || (codec == VideoCodec.AV1 && mode == HardwareAccelerationMode.AppleVideoToolbox))
            {
                Check(options.Contains("-preset " + cpu.Preset), "Configured CPU preset honored");
                Check(!options.Contains("-bf") && !options.Contains("-refs"), "CPU preset owns frame decisions");
            }
            else if (mode == HardwareAccelerationMode.NvidiaNvenc)
            {
                Check(options.Contains("-rc vbr") && options.Contains("-b:v 0") && options.Contains("-rc-lookahead 32"), "NVENC quality mode/lookahead");
                if (codec == VideoCodec.H265) Check(!options.Contains("-bf"), "No unsupported HEVC B-frame override");
            }
            else
                Check(options.Contains("-q:v") && options.Contains("-realtime 0") && options.Contains("-allow_sw 0"), "Apple quality mode");
        }
        if (codec != VideoCodec.VP9)
        {
            var gpu = QualityConfigService.GetEncodingSettings(level, codec, true);
            Check(gpu.Cq < lastCq && gpu.VtQuality > lastApple, $"GPU quality direction: {codec}");
            lastCq = gpu.Cq!.Value; lastApple = gpu.VtQuality!.Value;
        }
    }
}
// The full OS x codec x CPU/hardware routing matrix (16 combinations).
foreach (bool mac in new[] { false, true })
foreach (var codec in Enum.GetValues<VideoCodec>())
foreach (bool accelerated in new[] { false, true })
{
    var platform = GPUDetectionService.PlatformHardware(!mac, mac);
    var setting = new UserSettings { Codec = codec, UseGPUAcceleration = accelerated, HardwareAcceleration = platform };
    bool shouldAccelerate = accelerated && codec != VideoCodec.VP9 && !(mac && codec == VideoCodec.AV1);
    Check((setting.EffectiveHardwareAcceleration != HardwareAccelerationMode.None) == shouldAccelerate, "Platform hardware policy");
    string options = string.Join(" ", EncodingArguments.Build(setting, setting.EffectiveHardwareAcceleration));
    string encoder = !shouldAccelerate ? setting.GetCpuCodecName() : mac
        ? (codec == VideoCodec.H264 ? "h264_videotoolbox" : "hevc_videotoolbox")
        : codec switch { VideoCodec.H264 => "h264_nvenc", VideoCodec.H265 => "hevc_nvenc", _ => "av1_nvenc" };
    Check(options.Contains("-c:v " + encoder + " "), $"Correct platform encoder: {mac}/{codec}/{accelerated}");
}
var av1Settings = new UserSettings { Codec = VideoCodec.AV1, UseGPUAcceleration = true, HardwareAcceleration = HardwareAccelerationMode.AppleVideoToolbox };
Check(av1Settings.EffectiveHardwareAcceleration == HardwareAccelerationMode.None, "AV1 uses CPU on Apple");
Check(av1Settings.OutputExtension == ".mp4" && av1Settings.StandaloneOnly, "AV1 MP4 standalone policy");
Check(string.Join(" ", EncodingArguments.Build(av1Settings, HardwareAccelerationMode.AppleVideoToolbox)).Contains("-svtav1-params tune=0"), "AV1 perceptual CPU tuning");
foreach (var codec in Enum.GetValues<VideoCodec>())
{
    var passable = new UserSettings { Codec = codec, QualityLevel = 0, ReduceHighFrameRates = true };
    string name = VideoProcessor.GenerateOutputFilename("foo.mpg", passable, 3840, 50);
    Check(name == $"foo-L0{codec}-1920-25FPS{passable.OutputExtension}", "Passable filename with size/FPS modifiers");
    Check(VideoProcessor.ShouldSkipRecompression(name, passable), "Protect Passable delivery outputs");
    Check(!VideoProcessor.ShouldSkipRecompression($"foo-L4{codec}{passable.OutputExtension}", passable), "Archive can become Passable");
}
var webSettings = new UserSettings { Codec = VideoCodec.VP9, UseGPUAcceleration = true, HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc };
Check(webSettings.EffectiveHardwareAcceleration == HardwareAccelerationMode.None, "VP9 bypasses GPU");
Check(webSettings.UseVp9TwoPass, "Final website exports default to two passes");
Check(VideoProcessor.GenerateOutputFilename("input.mov", webSettings) == "input-L2VP9.webm", "WebM filename");
foreach (var codec in Enum.GetValues<VideoCodec>())
{
    var naming = new UserSettings { Codec = codec };
    Check(VideoProcessor.GenerateOutputFilename("my video.mpg", naming) == $"my video-L2{naming.CodecSuffix}{naming.OutputExtension}", "Preserve original spaces; add none in suffix");
    Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 3840) == $"foo-L2{naming.CodecSuffix}-1920{naming.OutputExtension}", "Downscaled output includes width");
    foreach (int width in new[] { 1280, 1919, 1920 })
        Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, width) == $"foo-L2{naming.CodecSuffix}{naming.OutputExtension}", "Inputs within the limit have no resolution suffix");
    naming.MaxWidth = int.MaxValue;
    Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 3840) == $"foo-L2{naming.CodecSuffix}{naming.OutputExtension}", "Unrestricted exports have no resolution suffix");
    naming.ReduceHighFrameRates = true;
    string fpsName = $"foo-L2{naming.CodecSuffix}-25FPS{naming.OutputExtension}";
    Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 3840, 50) == fpsName, "Changed FPS appears in filename");
    Check(VideoProcessor.ShouldSkipRecompression(fpsName, naming), "Protect FPS-modified delivery exports");
    naming.MaxWidth = 1920;
    string bothName = $"foo-L2{naming.CodecSuffix}-1920-25FPS{naming.OutputExtension}";
    Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 3840, 50) == bothName, "Resolution then FPS suffix");
    Check(VideoProcessor.ShouldSkipRecompression(bothName, naming), "Recognize combined suffixes");
    foreach (double rate in new[] { 24, 40, 49, 59.94 })
        Check(!VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 1920, rate).Contains("FPS"), "Unchanged rate has no FPS suffix");
    Check(VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 1920, 120).Contains("-60FPS"), "Label actual halved FPS above 30");
    naming.ReduceHighFrameRates = false;
    Check(!VideoProcessor.GenerateOutputFilename("foo.mpg", naming, 1920, 50).Contains("FPS"), "Disabled reduction has no FPS suffix");
    string archiveName = $"foo-L4{naming.CodecSuffix}-1920-25FPS{naming.OutputExtension}";
    Check(!VideoProcessor.ShouldSkipRecompression(archiveName, naming), "FPS-modified archive remains reusable for delivery");
    naming.QualityLevel = 4;
    Check(VideoProcessor.ShouldSkipRecompression(archiveName, naming), "Protect FPS-modified archive-to-archive exports");
}

Check(!new UserSettings().ReduceHighFrameRates, "Frame-rate reduction defaults to no");
foreach (double rate in new[] { 24000d / 1001, 24, 25, 30000d / 1001, 30, 40, 46, 48000d / 1001, 48, 49, 50, 51, 60000d / 1001, 60, 90, 120 })
foreach (var codec in Enum.GetValues<VideoCodec>())
foreach (var hardware in Enum.GetValues<HardwareAccelerationMode>())
{
    var settings = new UserSettings { Codec = codec, ReduceHighFrameRates = true };
    string options = string.Join(" ", EncodingArguments.Build(settings, hardware, inputFrameRate: rate));
    bool halve = rate >= 48 && rate % 2 == 0;
    Check(options.Contains("fps=fps=") == halve, "Only even integer frame rates >=48 are reduced");
    if (halve) Check(options.Contains($"fps=fps={(rate / 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}:round=near"), "Halve once, without a 30 FPS cap");
    Check(options.Contains("-fps_mode passthrough"), "Do not let the muxer duplicate lower-rate/VFR frames");
    settings.ReduceHighFrameRates = false;
    Check(!string.Join(" ", EncodingArguments.Build(settings, hardware, inputFrameRate: rate)).Contains("fps=fps="), "Frame-rate opt-out");
}
Check(FrameRatePolicy.ParseRate("30000/1001") < 30, "Fractional rate parsed without rounding up");
foreach (string invalid in new[] { "0/0", "1/0", "NaN", "bad", "-30/1" })
    Check(FrameRatePolicy.ParseRate(invalid) == null, "Invalid rate remains unknown");
Check(VideoProcessor.IsAlreadyRecompressed("foo - L2VP9-1920.webm"), "Recognize resized WebM exports");
Check(VideoProcessor.ShouldSkipRecompression("foo - L2VP9-1920.webm", webSettings), "Protect resized delivery exports");
Check(!VideoProcessor.ShouldSkipRecompression("foo - L4VP9-1920.webm", webSettings), "Resized archives remain reusable");
Check(VideoProcessor.ShouldSkipRecompression("foo - L4H264-1920.mp4", new UserSettings { QualityLevel = 4, Codec = VideoCodec.H264 }), "Protect resized archive-to-archive exports");
foreach (string name in new[] { "clip - Q22H264.mp4", "media-Q26H265.mp4", "clip - L2VP9.webm", "media-L3H265.mp4" })
    Check(VideoProcessor.IsAlreadyRecompressed(name), $"Recompression recognition: {name}");
Check(!VideoProcessor.IsAlreadyRecompressed("clip.webm"), "Plain WebM is an input");
Check(!VideoProcessor.ShouldSkipRecompression("clip - L4H264.mp4", new UserSettings { Codec = VideoCodec.H264, QualityLevel = 3 }), "Archive can be recompressed in the same codec");
Check(!VideoProcessor.ShouldSkipRecompression("clip - L4VP9.webm", webSettings), "WebM archive can be recompressed");
Check(VideoProcessor.ShouldSkipRecompression("clip - L4H264.mp4", new UserSettings { Codec = VideoCodec.H264, QualityLevel = 4 }), "Archive batch does not reprocess its own output");
Check(VideoProcessor.ShouldSkipRecompression("clip - L2H264.mp4", new UserSettings { Codec = VideoCodec.H264 }), "Delivery outputs remain protected");
try { await new PPTXVideoProcessor().ProcessAsync("does-not-exist.pptx", webSettings); throw new Exception("PPTX guard missing"); }
catch (NotSupportedException) { }
Console.WriteLine("PASS: encoder quality, compatibility, routing, filenames and PowerPoint guard.");
await Vp9WorkflowTests.RunAsync();
CaptureTests.Run();
CaptureGuidanceTests.Run();
await CapturePipeTests.RunAsync();
if (args.Contains("--capture-integration")) await CaptureTests.IntegrationAsync(args.Contains("--capture-hardware"));

if (args.Contains("--platform"))
{
    var detected = await GPUDetectionService.DetectGPUCapabilitiesAsync();
    foreach (var codec in Enum.GetValues<VideoCodec>())
        Check(detected.CpuEncoders.Contains(new UserSettings { Codec = codec }.GetCpuCodecName()), "Required software encoder present: " + codec);
    if (args.Contains("--nvenc") && OperatingSystem.IsWindows())
        Check(detected.SupportsH264 && detected.SupportsH265, "Known NVENC machine must pass both H26x quality probes");
    Check(detected.IsSupported == detected.SupportsHardwareAcceleration, "Usable hardware determines supported status");
    Check(await EmbeddedFFmpegRunner.CheckNVENCAvailabilityAsync() == detected.SupportsH264 || !OperatingSystem.IsWindows(), "Legacy and current NVENC probes agree");
    Console.WriteLine($"PASS: platform probe: {detected.HardwareAcceleration}; H264={detected.SupportsH264}, H265={detected.SupportsH265}, AV1={detected.SupportsAV1}; all CPU libraries present.");
}
if (args.Contains("--av1")) await Av1WorkflowTests.RunAsync();
if (!args.Contains("--integration")) return;
string ffmpeg = await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync() ?? throw new Exception("FFmpeg missing");
string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
string artifacts = Path.GetFullPath(Path.Combine("artifacts", "encoding-tests"));
Directory.CreateDirectory(artifacts);

async Task<string> Run(string executable, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(executable, arguments)
    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { process.Kill(true); throw; }
    string output = await stdout + await stderr;
    Check(process.ExitCode == 0, output);
    return output;
}

string source = Path.Combine(artifacts, "source.mkv");
await Run(ffmpeg, $"-v error -f lavfi -i testsrc2=size=640x360:rate=24:duration=4 -f lavfi -i sine=frequency=440:duration=4 -c:v ffv1 -c:a pcm_s16le -y \"{source}\"");
var measurements = new List<object>();
foreach (var codec in Enum.GetValues<VideoCodec>())
foreach (var hardware in codec == VideoCodec.VP9 || (codec == VideoCodec.AV1 ? !args.Contains("--av1-nvenc") : !args.Contains("--nvenc"))
    ? new[] { HardwareAccelerationMode.None } : new[] { HardwareAccelerationMode.None, HardwareAccelerationMode.NvidiaNvenc })
{
    double previousPsnr = 0;
    long previousSize = 0;
    for (int level = 0; level <= 4; level++)
    {
        var settings = new UserSettings { Codec = codec, QualityLevel = level, HardwareAcceleration = hardware, UseGPUAcceleration = hardware != HardwareAccelerationMode.None };
        string output = Path.Combine(artifacts, $"{codec}-{hardware}-{level}{settings.OutputExtension}");
        var watch = Stopwatch.StartNew();
        var result = await EmbeddedFFmpegRunner.CompressVideoWithResultAsync(source, output, settings);
        watch.Stop();
        Check(result.Success && result.HardwareAcceleration == hardware, "Requested backend must really encode: " + output);
        using var probe = JsonDocument.Parse(await Run(ffprobe, $"-v error -show_streams -show_format -of json \"{output}\""));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var video = streams.First(s => s.GetProperty("codec_type").GetString() == "video");
        Check(video.GetProperty("codec_name").GetString() == (codec == VideoCodec.H264 ? "h264" : codec == VideoCodec.H265 ? "hevc" : codec == VideoCodec.AV1 ? "av1" : "vp9"), "Actual codec");
        Check(video.GetProperty("pix_fmt").GetString() == (codec == VideoCodec.AV1 ? "yuv420p10le" : "yuv420p"), "Actual pixel format");
        Check(streams.First(s => s.GetProperty("codec_type").GetString() == "audio").GetProperty("codec_name").GetString() == (codec == VideoCodec.VP9 ? "opus" : "aac"), "Playable audio");
        if (codec == VideoCodec.AV1) Check(video.GetProperty("codec_tag_string").GetString() == "av01" && video.GetProperty("profile").GetString() == "Main", "AV1 web profile/tag");
        if (codec == VideoCodec.VP9) Check(video.GetProperty("profile").GetString() == "Profile 0", "VP9 Profile 0");
        // Compare matching frame indices: MP4 and Matroska round timestamps to
        // different time bases, so STARTPTS alone can compare adjacent frames.
        string metric = await Run(ffmpeg, $"-i \"{output}\" -i \"{source}\" -lavfi \"[0:v]settb=AVTB,setpts=N/(24*TB)[a];[1:v]settb=AVTB,setpts=N/(24*TB)[b];[a][b]psnr\" -an -f null -");
        double psnr = double.Parse(Regex.Match(metric, @"average:([0-9.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        long size = new FileInfo(output).Length;
        Check(psnr > previousPsnr, "Measured quality must rise with user level: " + output);
        Check(size > previousSize, "Higher quality should spend more bits on this fixture: " + output);
        previousPsnr = psnr; previousSize = size;
        double? vmaf = null;
        if (args.Contains("--vmaf"))
        {
            string perceptual = await Run(ffmpeg, $"-i \"{output}\" -i \"{source}\" -lavfi \"[0:v]settb=AVTB,setpts=N/(24*TB)[a];[1:v]settb=AVTB,setpts=N/(24*TB)[b];[a][b]libvmaf=n_threads=4\" -an -f null -");
            vmaf = double.Parse(Regex.Match(perceptual, @"VMAF score: ([0-9.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        measurements.Add(new { codec = codec.ToString(), hardware = hardware.ToString(), level, seconds = watch.Elapsed.TotalSeconds, bytes = size, psnr, vmaf });
    }
}

// Exercise conversion of an old recompressed MP4 filename.
string tiny = Path.Combine(artifacts, "tiny - Q22H264.mp4");
await Run(ffmpeg, $"-v error -f lavfi -i color=size=65x49:duration=0.2 -an -c:v libx264 -y \"{tiny}\"");
Check(await new VideoProcessor().ProcessVideoFileAsync(tiny, webSettings), "Standalone WebM export succeeds");
string tinyWebM = VideoProcessor.GenerateOutputFilename(tiny, webSettings);
Check(File.Exists(tinyWebM), "Explicit WebM output must be retained");
await Run(ffmpeg, $"-v error -i \"{tinyWebM}\" -f null -");
string lowQuality = Path.Combine(artifacts, "low-quality.webm");
await Run(ffmpeg, $"-v error -f lavfi -i testsrc2=size=320x180:duration=1 -an -c:v libvpx-vp9 -crf 63 -b:v 0 -y \"{lowQuality}\"");
var highWebM = new UserSettings { Codec = VideoCodec.VP9, QualityLevel = 3 };
Check(await new VideoProcessor().ProcessVideoFileAsync(lowQuality, highWebM), "Larger WebM export succeeds");
Check(new FileInfo(VideoProcessor.GenerateOutputFilename(lowQuality, highWebM)).Length > new FileInfo(lowQuality).Length, "Larger requested WebM is retained");
string archive = Path.Combine(artifacts, "archive - L4H264.mp4");
File.Copy(Path.Combine(artifacts, "H264-None-4.mp4"), archive, true);
var deliverySettings = new UserSettings { Codec = VideoCodec.H264, QualityLevel = 3 };
Check(await new VideoProcessor().ProcessVideoFileAsync(archive, deliverySettings), "Archive recompression succeeds");
Check(File.Exists(VideoProcessor.GenerateOutputFilename(archive, deliverySettings)), "Archive produces same-codec delivery output");
var archiveSettings = new UserSettings { Codec = VideoCodec.H264, QualityLevel = 4 };
Check(await new VideoProcessor().ProcessVideoFileAsync(Path.Combine(artifacts, "H264-None-1.mp4"), archiveSettings), "Explicit archive export succeeds");
Check(File.Exists(VideoProcessor.GenerateOutputFilename(Path.Combine(artifacts, "H264-None-1.mp4"), archiveSettings)), "Explicit archive export is retained even if larger");
if (OperatingSystem.IsWindows())
{
    var fallback = await EmbeddedFFmpegRunner.CompressVideoWithResultAsync(tiny, Path.Combine(artifacts, "fallback.mp4"),
        new UserSettings { Codec = VideoCodec.H265, QualityLevel = 3, UseGPUAcceleration = true, HardwareAcceleration = HardwareAccelerationMode.AppleVideoToolbox });
    Check(fallback.Success && fallback.HardwareAcceleration == HardwareAccelerationMode.None, "Unavailable hardware falls back to CPU");
}
Check(await FFmpegRunner.CompressVideoAsync(tiny, Path.Combine(artifacts, "legacy.webm"), webSettings), "External runner also exports VP9 with GPU preference");
var onePass = new UserSettings { Codec = VideoCodec.VP9, UseVp9TwoPass = false };
Check(await EmbeddedFFmpegRunner.CompressVideoAsync(tiny, Path.Combine(artifacts, "one-pass.webm"), onePass), "Bundled runner retains one-pass option");
Check(await FFmpegRunner.CompressVideoAsync(tiny, Path.Combine(artifacts, "legacy-one-pass.webm"), onePass), "External runner retains one-pass option");
// Exercise odd source dimensions and a maximum width of 1 (clamped to 2).
string odd = Path.Combine(artifacts, "odd.mkv");
await Run(ffmpeg, $"-v error -f lavfi -i testsrc=size=65x49:duration=0.2 -c:v ffv1 -y \"{odd}\"");
Check(await EmbeddedFFmpegRunner.CompressVideoAsync(odd, Path.Combine(artifacts, "odd.webm"), webSettings), "Odd dimensions supported");
string narrow = Path.Combine(artifacts, "narrow.webm");
Check(await EmbeddedFFmpegRunner.CompressVideoAsync(odd, narrow, new UserSettings { Codec = VideoCodec.VP9, MaxWidth = 1 }), "Tiny width supported");
// A null-muxer analysis pass and WebM final pass must see the same VFR frames.
string variable = Path.Combine(artifacts, "variable.mkv");
string variableOutput = Path.Combine(artifacts, "variable.webm");
await Run(ffmpeg, $"-v error -f lavfi -i testsrc2=size=320x180:rate=24:duration=2 -vf \"select='not(eq(mod(n,5),0))'\" -fps_mode vfr -an -c:v ffv1 -y \"{variable}\"");
Check(await EmbeddedFFmpegRunner.CompressVideoAsync(variable, variableOutput, webSettings), "Two-pass VFR encode");
async Task<double[]> FrameTimes(string path)
{
    using var doc = JsonDocument.Parse(await Run(ffprobe, $"-v error -select_streams v:0 -show_frames -show_entries frame=best_effort_timestamp_time -of json \"{path}\""));
    return doc.RootElement.GetProperty("frames").EnumerateArray()
        .Select(f => double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
var inputTimes = await FrameTimes(variable);
var outputTimes = await FrameTimes(variableOutput);
Check(inputTimes.Length == outputTimes.Length, "Both passes preserve VFR frame count");
for (int i = 0; i < inputTimes.Length; i++)
    Check(Math.Abs((inputTimes[i] - inputTimes[0]) - (outputTimes[i] - outputTimes[0])) < .002, "Preserve VFR timing");
await File.WriteAllTextAsync(Path.Combine(artifacts, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
await FrameRateTests.RunAsync(ffmpeg, ffprobe, artifacts, Run, args.Contains("--nvenc"));
Console.WriteLine("PASS: real encoding, codec/profile/audio, quality progression, silent/odd inputs and standalone WebM retention.");
