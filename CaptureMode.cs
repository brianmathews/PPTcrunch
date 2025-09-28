using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PPTcrunch;

public static class CaptureMode
{
    private class ModeOption
    {
        public required string Label { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int Fps { get; init; }
        public required string Format { get; init; } // e.g., mjpeg, yuyv422
    }

    private class RawMode
    {
        public required string Kind { get; init; } // vcodec or pixel_format
        public required string Fmt { get; init; } // mjpeg, yuyv422, etc.
        public required int W { get; init; }
        public required int H { get; init; }
        public required double MinF { get; init; }
        public required double MaxF { get; init; }
    }

    public static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Capture mode is only supported on Windows due to DirectShow dependencies.");
            return 1;
        }

        Console.WriteLine("\n\nUSB Capture");
        Console.WriteLine("============\n\n");

        // Ensure embedded FFmpeg is initialized and get its path
        string? ffmpegExe = await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync();
        if (string.IsNullOrWhiteSpace(ffmpegExe) || !File.Exists(ffmpegExe))
        {
            Console.WriteLine("Error: FFmpeg not available.");
            return 1;
        }

        // 1) List devices
        var devices = await ListDirectShowVideoDevicesAsync(ffmpegExe);
        if (devices.Count == 0)
        {
            Console.WriteLine("No DirectShow video devices found.");
            return 1;
        }

        Console.WriteLine("\nAvailable video capture devices:");
        int defaultDeviceIndex = Math.Max(0, devices.FindIndex(d => string.Equals(d, "USB Video", StringComparison.OrdinalIgnoreCase)));
        for (int i = 0; i < devices.Count; i++)
        {
            Console.WriteLine($"  [{i}] {devices[i]}");
        }
        Console.WriteLine($"\nDefault: [{defaultDeviceIndex}] {devices[defaultDeviceIndex]}");
        Console.Write("Select device index (ENTER for default): ");
        string? deviceInput = Console.ReadLine();
        int deviceIndex;
        if (string.IsNullOrWhiteSpace(deviceInput)) deviceIndex = defaultDeviceIndex;
        else if (!int.TryParse(deviceInput, out deviceIndex) || deviceIndex < 0 || deviceIndex >= devices.Count)
        {
            Console.WriteLine("Invalid selection.");
            return 1;
        }
        string selectedDevice = devices[deviceIndex];
        Console.WriteLine($"Selected device: \"{selectedDevice}\"\n");

        // 2) Query raw modes for selected device
        var rawModes = await ListRawModesForDeviceAsync(ffmpegExe, selectedDevice);
        if (rawModes.Count == 0)
        {
            Console.WriteLine("No modes parsed from FFmpeg output.");
            return 1;
        }

        // 2a) Select frame rate (discrete list)
        int[] candidateFps = new[] { 60, 50, 30, 25, 20, 15, 10, 5 };
        var fpsOptions = candidateFps.Where(f => rawModes.Any(r => f >= Math.Floor(r.MinF) && f <= Math.Ceiling(r.MaxF))).ToList();
        if (fpsOptions.Count == 0)
        {
            Console.WriteLine("No discrete frame rates available from device.");
            return 1;
        }

        Console.WriteLine($"\nAvailable frame rates for '{selectedDevice}':");
        int defaultFpsIndex = fpsOptions.IndexOf(30);
        if (defaultFpsIndex < 0) defaultFpsIndex = 0;
        for (int i = 0; i < fpsOptions.Count; i++)
        {
            Console.WriteLine($"  [{i}] {fpsOptions[i]} fps");
        }
        Console.WriteLine($"\nDefault: [{defaultFpsIndex}] {fpsOptions[defaultFpsIndex]} fps");
        Console.Write("Select frame rate index (ENTER for default): ");
        string? fpsInput = Console.ReadLine();
        int fpsIndex;
        if (string.IsNullOrWhiteSpace(fpsInput)) fpsIndex = defaultFpsIndex;
        else if (!int.TryParse(fpsInput, out fpsIndex) || fpsIndex < 0 || fpsIndex >= fpsOptions.Count)
        {
            Console.WriteLine("Invalid selection.");
            return 1;
        }
        int chosenFps = fpsOptions[fpsIndex];
        Console.WriteLine($"Selected framerate: {chosenFps} fps\n");

        // 2b) Resolutions supported at chosen fps with format options
        var supportedAtFps = rawModes.Where(r => chosenFps >= Math.Floor(r.MinF) && chosenFps <= Math.Ceiling(r.MaxF)).ToList();
        if (supportedAtFps.Count == 0)
        {
            Console.WriteLine("No resolutions supported for the selected frame rate.");
            return 1;
        }

        var resolutionGroups = supportedAtFps
            .GroupBy(r => new { r.W, r.H })
            .Select(g => new
            {
                W = g.Key.W,
                H = g.Key.H,
                Formats = g.Select(x => x.Fmt).Distinct().OrderBy(f => f != "mjpeg").ThenBy(f => f).ToList()
            })
            .OrderBy(x => x.H)
            .ThenBy(x => x.W)
            .ToList();

        if (resolutionGroups.Count == 0)
        {
            Console.WriteLine("No resolutions available for the selected frame rate.");
            return 1;
        }

        Console.WriteLine($"\nAvailable resolutions at {chosenFps} fps:");
        int defaultResIndex = resolutionGroups.FindIndex(r => r.W == 1920 && r.H == 1080);
        if (defaultResIndex < 0) defaultResIndex = 0;
        for (int i = 0; i < resolutionGroups.Count; i++)
        {
            var formatList = string.Join(", ", resolutionGroups[i].Formats.Select(f => FormatDisplayName(f)));
            Console.WriteLine($"  [{i}] {resolutionGroups[i].W}x{resolutionGroups[i].H} ({formatList})");
        }
        Console.WriteLine($"\nDefault: [{defaultResIndex}] {resolutionGroups[defaultResIndex].W}x{resolutionGroups[defaultResIndex].H}");
        Console.Write("Select resolution index (ENTER for default): ");
        string? resInput = Console.ReadLine();
        int resIndex;
        if (string.IsNullOrWhiteSpace(resInput)) resIndex = defaultResIndex;
        else if (!int.TryParse(resInput, out resIndex) || resIndex < 0 || resIndex >= resolutionGroups.Count)
        {
            Console.WriteLine("Invalid selection.");
            return 1;
        }

        var resChoice = resolutionGroups[resIndex];
        Console.WriteLine($"Selected resolution: {resChoice.W}x{resChoice.H}\n");

        // 2c) Select compression format if multiple options available
        string selectedFormat;
        if (resChoice.Formats.Count == 1)
        {
            selectedFormat = resChoice.Formats[0];
            Console.WriteLine($"Using compression format: {FormatDisplayName(selectedFormat)}\n");
        }
        else
        {
            Console.WriteLine($"Available compression formats for {resChoice.W}x{resChoice.H} at {chosenFps} fps:");
            int defaultFormatIndex = resChoice.Formats.IndexOf("mjpeg");
            if (defaultFormatIndex < 0) defaultFormatIndex = 0;
            for (int i = 0; i < resChoice.Formats.Count; i++)
            {
                Console.WriteLine($"  [{i}] {FormatDisplayName(resChoice.Formats[i])}");
            }
            Console.WriteLine($"\nDefault: [{defaultFormatIndex}] {FormatDisplayName(resChoice.Formats[defaultFormatIndex])}");
            Console.Write("Select compression format index (ENTER for default): ");
            string? formatInput = Console.ReadLine();
            int formatIndex;
            if (string.IsNullOrWhiteSpace(formatInput)) formatIndex = defaultFormatIndex;
            else if (!int.TryParse(formatInput, out formatIndex) || formatIndex < 0 || formatIndex >= resChoice.Formats.Count)
            {
                Console.WriteLine("Invalid selection.");
                return 1;
            }
            selectedFormat = resChoice.Formats[formatIndex];
            Console.WriteLine($"Selected compression format: {FormatDisplayName(selectedFormat)}\n");
        }

        var selectedMode = new ModeOption
        {
            Label = $"{resChoice.W}x{resChoice.H}@{chosenFps} {selectedFormat}",
            Width = resChoice.W,
            Height = resChoice.H,
            Fps = chosenFps,
            Format = selectedFormat
        };
        Console.WriteLine($"Selected mode : {selectedMode.Label}\n");

        // 3) Choose recording mode (direct copy/lossless vs live transcode)
        Console.WriteLine("Choose recording mode:");
        Console.WriteLine("  1. Direct storage (no transcoding): copy MJPEG or lossless FFV1");
        Console.WriteLine("  2. Transcode while recording (H.264/H.265)\n");
        Console.Write("Enter your choice (1-2, default: 1): ");
        string? modeInput = Console.ReadLine();
        bool liveTranscode = false;
        if (!string.IsNullOrWhiteSpace(modeInput) && int.TryParse(modeInput, out int modeChoice))
        {
            liveTranscode = modeChoice == 2;
        }

        // 4) If transcoding, collect codec/quality/GPU/resolution preferences (realtime-safe presets)
        var settings = new UserSettings();
        if (liveTranscode)
        {
            Console.WriteLine();
            Console.WriteLine("Live Transcode Settings");
            Console.WriteLine("-----------------------");

            // Detect GPU capabilities
            var gpuInfo = await GPUDetectionService.DetectGPUCapabilitiesAsync();
            if (gpuInfo.SupportsHardwareAcceleration)
            {
                string hardwarePrompt = gpuInfo.HardwareAcceleration switch
                {
                    HardwareAccelerationMode.NvidiaNvenc => "Use NVIDIA NVENC hardware acceleration for faster real-time encoding?",
                    HardwareAccelerationMode.AppleVideoToolbox => "Use Apple VideoToolbox hardware acceleration for faster real-time encoding?",
                    _ => "Use hardware acceleration for faster real-time encoding?"
                };
                Console.Write($"{hardwarePrompt} (Y/n, default: Y): ");
                string? gpuInput = Console.ReadLine()?.Trim().ToLowerInvariant();
                settings.UseGPUAcceleration = string.IsNullOrEmpty(gpuInput) || gpuInput == "y" || gpuInput == "yes";
                settings.HardwareAcceleration = settings.UseGPUAcceleration ? gpuInfo.HardwareAcceleration : HardwareAccelerationMode.None;
            }
            else
            {
                settings.UseGPUAcceleration = false;
                settings.HardwareAcceleration = HardwareAccelerationMode.None;
                Console.WriteLine("Hardware acceleration not available - will use CPU (optimize for real-time)");
            }

            // Codec preference
            Console.WriteLine();
            Console.WriteLine("Video codec options:");
            Console.WriteLine("  1. H.264 (better compatibility)");
            Console.WriteLine("  2. H.265 (smaller files, more CPU/GPU cost)");
            if (settings.UseGPUAcceleration && !gpuInfo.SupportsH265)
            {
                Console.WriteLine("     Note: Your hardware encoder doesn't support H.265 - H.264 will be used if selected");
            }
            Console.Write("Enter your choice (1 or 2, default: 2): ");
            string? codecInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(codecInput) && int.TryParse(codecInput, out int codecChoice))
            {
                if (codecChoice == 1) settings.Codec = VideoCodec.H264;
                else if (codecChoice == 2)
                {
                    if (settings.UseGPUAcceleration && !gpuInfo.SupportsH265) settings.Codec = VideoCodec.H264;
                    else settings.Codec = VideoCodec.H265;
                }
            }

            // Quality level (maps to CRF/CQ/Q)
            Console.WriteLine();
            Console.WriteLine("Quality level options:");
            var qcfg = QualityConfigService.GetConfig();
            foreach (var kvp in qcfg.QualityLevels.OrderBy(x => int.Parse(x.Key)))
            {
                Console.WriteLine($"  {kvp.Key}. {kvp.Value.Name}");
            }
            Console.Write("Enter your choice (1-3, default: 2): ");
            string? qInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(qInput) && int.TryParse(qInput, out int qLevel) && qLevel >= 1 && qLevel <= 3)
            {
                settings.QualityLevel = qLevel;
            }

            // Optional downscale for real-time headroom
            Console.WriteLine();
            Console.Write($"Limit width to 1920 for smoother real-time encoding? (Y/n, default: Y): ");
            string? resLimitInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            settings.ReduceHighResTo1920 = string.IsNullOrEmpty(resLimitInput) || resLimitInput == "y" || resLimitInput == "yes";
            settings.MaxWidth = settings.ReduceHighResTo1920 ? 1920 : int.MaxValue;
        }

        // 5) Prompt for output filename
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string baseName = $"{ts}_{selectedMode.Width}x{selectedMode.Height}@{selectedMode.Fps}";
        string suggested = liveTranscode ? ($"{baseName}.mp4") : ($"{baseName}.mkv");
        Console.WriteLine($"Suggested filename: {suggested}");
        Console.Write("Output filename (ENTER to accept): ");
        string? outName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(outName)) outName = suggested;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(outName))) outName += liveTranscode ? ".mp4" : ".mkv";

        // 6) Build ffmpeg command
        var inputArgs = new StringBuilder();
        inputArgs.Append("-hide_banner -f dshow -rtbufsize 512M ");
        inputArgs.Append($"-video_size {selectedMode.Width}x{selectedMode.Height} ");
        inputArgs.Append($"-framerate {selectedMode.Fps} ");
        inputArgs.Append($"-vcodec {selectedMode.Format} ");
        inputArgs.Append($"-i video=\"{selectedDevice}\" ");

        string vopts;
        if (!liveTranscode)
        {
            vopts = string.Equals(selectedMode.Format, "mjpeg", StringComparison.OrdinalIgnoreCase)
                ? "-c:v copy -fps_mode passthrough"
                : "-pix_fmt yuv422p -c:v ffv1 -level 3 -g 1";
        }
        else
        {
            // Build real-time transcode options from settings
            var enc = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, settings.UseGPUAcceleration);
            var cparams = QualityConfigService.GetCodecParams(settings.Codec, settings.UseGPUAcceleration);

            string scaleFilter = settings.MaxWidth == int.MaxValue
                ? "scale=trunc(iw/2)*2:trunc(ih/2)*2"
                : $"scale='if(gt(iw,{settings.MaxWidth}),{settings.MaxWidth},iw)':'if(gt(iw,{settings.MaxWidth}),trunc(ih*{settings.MaxWidth}/iw/2)*2,ih)'";

            var sb = new StringBuilder();
            sb.Append($"-vf \"{scaleFilter}\" ");

            if (settings.UseGPUAcceleration)
            {
                // NVENC/VideoToolbox real-time tuned
                sb.Append($"-c:v {settings.GetGpuCodecName()} ");
                if (!string.IsNullOrEmpty(enc.Rc)) sb.Append($"-rc {enc.Rc} ");
                if (enc.Cq.HasValue) sb.Append($"-cq {enc.Cq.Value} ");
                sb.Append("-b:v 0 ");
                // Prefer faster preset for real-time than offline defaults
                string realtimePreset = string.IsNullOrEmpty(enc.Preset) ? "medium" : (enc.Preset == "slow" ? "medium" : enc.Preset);
                sb.Append($"-preset {realtimePreset} ");
                if (!string.IsNullOrEmpty(enc.Tune)) sb.Append($"-tune {enc.Tune} ");
                if (enc.Multipass.HasValue) sb.Append($"-multipass {enc.Multipass.Value} ");
                if (!string.IsNullOrEmpty(cparams.Profile)) sb.Append($"-profile:v {cparams.Profile} ");
                if (cparams.Bf.HasValue) sb.Append($"-bf {cparams.Bf.Value} ");
                if (cparams.Refs.HasValue) sb.Append($"-refs {cparams.Refs.Value} ");
                if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(cparams.Tag)) sb.Append($"-tag:v {cparams.Tag} ");
                sb.Append("-pix_fmt yuv420p ");
            }
            else
            {
                // CPU real-time tuned (use faster presets)
                sb.Append($"-c:v {settings.GetCpuCodecName()} ");
                if (enc.Crf.HasValue) sb.Append($"-crf {enc.Crf.Value} ");
                string cpuPreset = settings.Codec == VideoCodec.H265 ? "faster" : "veryfast";
                sb.Append($"-preset {cpuPreset} ");
                if (!string.IsNullOrEmpty(cparams.Profile)) sb.Append($"-profile:v {cparams.Profile} ");
                if (cparams.Bf.HasValue) sb.Append($"-bf {cparams.Bf.Value} ");
                if (cparams.Refs.HasValue) sb.Append($"-refs {cparams.Refs.Value} ");
                if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(cparams.Tag)) sb.Append($"-tag:v {cparams.Tag} ");
            }

            // Audio copy if present; optimize for MP4 playback
            sb.Append("-c:a copy -movflags +faststart -y -stats");
            vopts = sb.ToString();
        }

        Console.WriteLine();
        Console.WriteLine("==============================================================");
        Console.WriteLine("Press 'q' in the FFmpeg console to stop the recording.");
        Console.WriteLine("==============================================================\n");

        Console.WriteLine("Running:");
        Console.WriteLine($"{ffmpegExe} {inputArgs}{vopts} \"{outName}\"\n");

        // Launch FFmpeg inheriting the current console so user can press 'q'
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = inputArgs.ToString() + vopts + " \"" + outName + "\"",
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                Console.WriteLine("Failed to start FFmpeg.");
                return 1;
            }
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running FFmpeg: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<string>> ListDirectShowVideoDevicesAsync(string ffmpegExe)
    {
        var devices = new List<string>();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = "-hide_banner -f dshow -list_devices true -i dummy",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return devices;

        string stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var lines = stderr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var rx = new Regex("^\\s*\\[.*\\]\\s*\"(.+)\"\\s*\\(video\\)");
        foreach (var line in lines)
        {
            var m = rx.Match(line);
            if (m.Success)
            {
                string name = m.Groups[1].Value.Trim();
                devices.Add(name);
            }
        }

        return devices;
    }

    private static async Task<List<RawMode>> ListRawModesForDeviceAsync(string ffmpegExe, string deviceName)
    {
        var rawModes = new List<RawMode>();

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegExe,
            Arguments = $"-hide_banner -f dshow -list_options true -i video=\"{deviceName}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return new List<RawMode>();

        string stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var lines = stderr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // Matches both vcodec and pixel_format entries
        var rx = new Regex("^(?:\\s*\\[.*\\]\\s*)?(?<key>vcodec|pixel_format)=(?<fmt>\\S+)\\s+min s=(?<w>\\d+)x(?<h>\\d+)\\s+fps=(?<min>[\\d\\.]+)\\s+max s=\\k<w>x\\k<h>\\s+fps=(?<max>[\\d\\.]+)");
        foreach (var line in lines)
        {
            var m = rx.Match(line);
            if (m.Success)
            {
                rawModes.Add(new RawMode
                {
                    Kind = m.Groups["key"].Value,
                    Fmt = m.Groups["fmt"].Value,
                    W = int.Parse(m.Groups["w"].Value),
                    H = int.Parse(m.Groups["h"].Value),
                    MinF = double.Parse(m.Groups["min"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    MaxF = double.Parse(m.Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        }

        return rawModes;
    }

    private static string FormatDisplayName(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "mjpeg" => "MJPEG",
            "yuyv422" => "YUV422",
            "nv12" => "NV12",
            "rgb24" => "RGB24",
            "bgr24" => "BGR24",
            "uyvy422" => "UYVY422",
            _ => format.ToUpperInvariant()
        };
    }

}


