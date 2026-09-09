using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace PPTcrunch;

public static class CaptureMode
{
    public static async Task<int> DiagnoseAsync(string deviceName)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            Console.WriteLine("Capture diagnostics require Windows 7 or later.");
            return 1;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "capture-diagnostics.txt");
        var report = new StringBuilder();
        report.AppendLine($"PPTcrunch capture diagnostics — {DateTimeOffset.Now:O}");
        report.AppendLine($"Device requested: {deviceName}");
        try
        {
            var mediaFoundation = WindowsMediaFoundationCapture.Discover();
            report.AppendLine("\n=== MEDIA FOUNDATION DEVICES AND NATIVE MODES ===");
            foreach (var source in mediaFoundation)
            {
                report.AppendLine($"{source.Name}");
                report.AppendLine($"  ID: {source.Id}");
                if (source.Error != null) report.AppendLine($"  ERROR: {source.Error}");
                foreach (var mode in source.Modes.OrderBy(m => m.Width).ThenBy(m => m.Height)
                    .ThenBy(m => m.PixelFormat).ThenBy(m => m.Rate))
                {
                    report.AppendLine($"  [{mode.NativeIndex}] {mode.Width}x{mode.Height} " +
                        $"{mode.PixelFormat} {mode.RateNumerator}/{mode.RateDenominator} " +
                        $"({CaptureSupport.Number(mode.Rate)} fps) subtype={mode.Subtype} stride={mode.Stride}");
                }
            }
            var requested = mediaFoundation.FirstOrDefault(s =>
                s.Name.Contains(deviceName, StringComparison.OrdinalIgnoreCase));
            report.AppendLine($"\n=== MEDIA FOUNDATION {deviceName} 1920x1080 AT 30 FPS ===");
            if (requested == null) report.AppendLine("Device not found.");
            else foreach (var mode in requested.Modes.Where(m => m.Width == 1920 && m.Height == 1080 && Math.Abs(m.Rate - 30) < .01)
                .OrderBy(m => m.PixelFormat))
                report.AppendLine($"{mode.PixelFormat} {mode.RateNumerator}/{mode.RateDenominator} subtype={mode.Subtype}");

            string ffmpeg = await CaptureRuntime.ExecutableAsync();
            report.AppendLine($"FFmpeg: {ffmpeg}");
            var listed = await CaptureSupport.Probe(ffmpeg,
                new[] { "-hide_banner", "-nostdin", "-f", "dshow", "-list_devices", "true", "-i", "dummy" });
            report.AppendLine($"\n=== RAW DEVICE LIST (exit {listed.Code}) ===");
            report.AppendLine(listed.Error.TrimEnd());

            var queried = await CaptureSupport.Probe(ffmpeg,
                new[] { "-hide_banner", "-nostdin", "-f", "dshow", "-list_options", "true", "-i", $"video={deviceName}" });
            report.AppendLine($"\n=== RAW MODE LIST (exit {queried.Code}) ===");
            report.AppendLine(queried.Error.TrimEnd());

            var formats = CaptureSupport.ParseFormats(queried.Error, false);
            report.AppendLine($"\n=== PARSED MODES ({formats.Count}) ===");
            foreach (var format in formats.OrderBy(f => f.Width).ThenBy(f => f.Height)
                .ThenBy(f => f.Format).ThenBy(f => f.MinRate))
            {
                report.AppendLine($"{format.Width}x{format.Height} {format.Kind}={format.Format} " +
                    $"{CaptureSupport.Number(format.MinRate)}-{CaptureSupport.Number(format.MaxRate)} fps");
            }

            report.AppendLine("\n=== 1920x1080 AT 30 FPS FILTER ===");
            foreach (var format in formats.Where(f => f.Width == 1920 && f.Height == 1080)
                .OrderBy(f => f.Format).ThenBy(f => f.MinRate))
            {
                report.AppendLine($"{(CaptureSupport.SupportsRate(format, 30, false) ? "INCLUDED" : "EXCLUDED")} " +
                    $"{format.Kind}={format.Format} {CaptureSupport.Number(format.MinRate)}-{CaptureSupport.Number(format.MaxRate)} fps");
            }

            await File.WriteAllTextAsync(path, report.ToString());
            Console.WriteLine(report);
            Console.WriteLine($"Diagnostic report saved to:\n{path}");
            return formats.Count > 0 || requested?.Modes.Count > 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report.AppendLine($"\nDIAGNOSTIC FAILED: {ex}");
            await File.WriteAllTextAsync(path, report.ToString());
            Console.WriteLine(report);
            Console.WriteLine($"Partial diagnostic report saved to:\n{path}");
            return 1;
        }
    }

    public static async Task<int> RunAsync()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Console.WriteLine("Video capture supports Windows and macOS.");
            return 1;
        }
        try { return await CaptureAsync(OperatingSystem.IsMacOS()); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.WriteLine($"Capture stopped: {ex.Message}");
            return 1;
        }
    }

    private static int Choose(string title, IReadOnlyList<string> labels, int defaultIndex = 0)
    {
        if (labels.Count == 0) throw new IOException($"No choices available for {title}.");
        Console.WriteLine($"\n{title}");
        for (int i = 0; i < labels.Count; i++) Console.WriteLine($"  [{i + 1}] {labels[i]}");
        Console.Write($"Select 1-{labels.Count} (ENTER for {defaultIndex + 1}): ");
        string answer = Console.ReadLine()?.Trim() ?? throw new IOException("Input closed.");
        if (answer.Length == 0) return defaultIndex;
        if (int.TryParse(answer, out int selected) && selected >= 1 && selected <= labels.Count) return selected - 1;
        throw new IOException("Invalid selection. Run capture again to retry.");
    }

    private static async Task<int> CaptureAsync(bool mac)
    {
        Console.WriteLine("\nUSB Video Capture — video only\n");
        if (!mac && !OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            throw new PlatformNotSupportedException("Windows capture requires Windows 7 or later.");
        if (mac) Console.WriteLine("Allow camera access for your terminal when macOS prompts. No microphone access is needed.");
        string ffmpeg = await CaptureRuntime.ExecutableAsync();
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, mac ? "ffprobe" : "ffprobe.exe");
        IReadOnlyList<WindowsCaptureSource> windowsSources = Array.Empty<WindowsCaptureSource>();
        List<CaptureDevice> devices;
        if (mac)
        {
            var listed = await CaptureSupport.Probe(ffmpeg,
                new[] { "-hide_banner", "-nostdin", "-f", "avfoundation", "-list_devices", "true", "-i", "" });
            devices = CaptureSupport.ParseDevices(listed.Error, true);
            if (devices.Count == 0) throw new IOException($"No video capture devices found. Connect the card and check camera access.\n{listed.Error}");
        }
        else
        {
            var discovered = await Task.Run(WindowsMediaFoundationCapture.Discover);
            windowsSources = discovered.Where(source => source.Modes.Count > 0).ToList();
            devices = windowsSources.Select(source => new CaptureDevice(source.Id, source.Name, true)).ToList();
            if (devices.Count == 0)
            {
                string details = string.Join(Environment.NewLine, discovered.Where(source => source.Error != null)
                    .Select(source => $"{source.Name}: {source.Error}"));
                throw new IOException("No accessible video capture devices found. Connect the card and enable " +
                    "Settings > Privacy & security > Camera > Let desktop apps access your camera." +
                    (details.Length == 0 ? "" : Environment.NewLine + details));
            }
        }
        int defaultDevice = Math.Max(0, devices.FindIndex(d =>
            d.Name.Contains("Live Gamer", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("USB", StringComparison.OrdinalIgnoreCase)));
        var device = devices[Choose("Video input", devices.Select(d => d.Name).ToList(), defaultDevice)];
        if (mac && !device.IsUniqueId)
            throw new IOException("The capture runtime did not report a stable video device ID. Refusing to select a camera by an index that may change.");

        // AVFoundation has no list_options switch. An intentionally unsupported
        // size makes FFmpeg enumerate AVFoundation's device formats and rate ranges.
        List<CaptureFormat> formats;
        WindowsCaptureSource? windowsSource = null;
        if (mac)
        {
            var queried = await CaptureSupport.Probe(ffmpeg,
                new[] { "-hide_banner", "-nostdin", "-f", "avfoundation", "-video_size", "1x1", "-framerate", "1" }
                    .Concat(CaptureSupport.DeviceInput(device, true)).Concat(new[] { "-frames:v", "1", "-f", "null", "-" }));
            formats = CaptureSupport.ParseFormats(queried.Error, true);
            if (formats.Count == 0) throw new IOException($"Could not discover supported capture modes.\n{queried.Error}");
        }
        else
        {
            windowsSource = windowsSources.Single(source => source.Id == device.Id);
            formats = windowsSource.Modes.Select(mode => new CaptureFormat(mode.Width, mode.Height, mode.Rate, mode.Rate,
                mode.Kind, mode.PixelFormat)).Distinct().ToList();
            if (formats.Count == 0) throw new IOException("Media Foundation did not report any supported RGB24, YUY2, NV12, P010, or MJPEG capture modes.");
        }
        // Pick resolution first so cadence recommendations cannot select a rate
        // available only at another resolution (e.g. 1080p120 versus 4K30).
        var sizes = formats.Select(f => (f.Width, f.Height)).Distinct().OrderBy(s => s.Height).ThenBy(s => s.Width).ToList();
        int defaultSize = Math.Max(0, sizes.FindIndex(s => s.Width == 1920 && s.Height == 1080));
        var size = sizes[Choose("Resolution", sizes.Select(s => $"{s.Width} × {s.Height}").ToList(), defaultSize)];
        var atSize = formats.Where(f => f.Width == size.Width && f.Height == size.Height).ToList();
        var supportedRates = CaptureSupport.Rates(atSize, mac);
        Console.WriteLine($"\nThe capture device reports these allowed FPS rates at {size.Width} × {size.Height}:");
        foreach (double supportedRate in supportedRates)
            Console.WriteLine($"  • {CaptureGuidance.RateLabel(supportedRate)}");
        Console.WriteLine("Tiny device timing differences are shown as nominal FPS; the exact advertised rate is still requested.");
        double rate = supportedRates[Choose($"Allowed capture frame rate at {size.Width} × {size.Height}",
            supportedRates.Select(CaptureGuidance.RateLabel).ToList(), CaptureGuidance.DefaultRate(supportedRates))];
        var available = atSize.Where(f => CaptureSupport.SupportsRate(f, rate, mac)).ToList();
        List<(string Kind, string Format)> colors;
        if (mac)
        {
            // Request a seldom-supported but valid AVFoundation pixel format to get
            // its advertised output list. If supported, it is itself a verified choice.
            var probeMode = new CaptureSelection(device, size.Width, size.Height, rate, "pixel_format", "monob");
            var pixels = await CaptureSupport.Probe(ffmpeg, CaptureSupport.Input(probeMode, true)
                .Concat(new[] { "-nostdin", "-frames:v", "1", "-an", "-c:v", "copy", "-f", "null", "-" }));
            colors = CaptureSupport.ParsePixels(pixels.Error).Select(p => ("pixel_format", p)).ToList();
            if (colors.Count == 0 && pixels.Code == 0) colors.Add(("pixel_format", "monob"));
            if (colors.Count == 0) throw new IOException($"Could not discover pixel formats.\n{pixels.Error}");
        }
        else colors = available.Select(f => (f.Kind, f.Format)).Distinct().ToList();
        colors = CaptureGuidance.SortColors(colors);
        Console.WriteLine("\nChoose the output format to request from the capture card/driver.");
        Console.WriteLine("Raw formats are ordered by color detail, then bit precision: full-color RGB/4:4:4, 4:2:2, then 4:2:0.");
        if (colors.Any(c => CaptureGuidance.ColorRank(c.Kind, c.Format).Detail < 0))
            Console.WriteLine("Compressed or unrecognized formats follow; their picture quality cannot be ranked from the format name alone.");
        if (mac)
        {
            Console.WriteLine("On macOS, these are the formats macOS delivers; it may decode or convert the card's video first.");
        }
        if (colors.Any(c => c.Format is "uyvy422" or "yuyv422") && colors.Any(c => c.Format is "nv12" or "nv21"))
            Console.WriteLine("For best color detail among the YUV choices below, prefer UYVY/YUYV; choose NV12/NV21 for less data.");
        Console.WriteLine("A richer output format cannot restore detail already lost in the source or card.");
        int colorIndex = Choose("Capture card output format", colors.Select(c => CaptureSupport.DescribeFormat(c.Kind, c.Format)).ToList());
        var color = colors[colorIndex];
        var selection = new CaptureSelection(device, size.Width, size.Height, rate, color.Kind, color.Format);
        WindowsCaptureMode? windowsMode = mac ? null : windowsSource!.Modes.First(mode =>
            mode.Width == size.Width && mode.Height == size.Height && mode.PixelFormat == color.Format &&
            Math.Abs(mode.Rate - rate) < .00001);

        string temp = Path.Combine(Path.GetTempPath(), "pptcrunch-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Console.WriteLine("\nChecking the selected input with a short video sample...");
            string sample = Path.Combine(temp, "input.nut");
            (int Code, string Output, string Error, double? CaptureRate) sampled;
            if (mac)
            {
                var probe = await CaptureSupport.Probe(ffmpeg, CaptureSupport.Input(selection, true)
                    .Concat(new[] { "-nostdin", "-map", "0:v:0", "-an", "-frames:v", "12", "-c:v", "copy", "-f", "nut", "-n", sample }), 45);
                sampled = (probe.Code, probe.Output, probe.Error, null);
            }
            else sampled = await CaptureWindowsSample(ffmpeg, device.Id, windowsMode!, sample);
            if (sampled.Code != 0 || HasModeFallback(sampled.Error))
                throw new IOException($"The requested input mode could not be used exactly.\n{sampled.Error}");
            var inspected = await CaptureSupport.Probe(ffprobe, new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
                "stream=codec_name,pix_fmt,width,height:packet=pts_time", "-show_packets", "-of", "json", sample });
            if (inspected.Code != 0) throw new IOException(inspected.Error);
            var stream = ReadStream(inspected.Output);
            double measuredRate = sampled.CaptureRate ?? SampleRate(inspected.Output);
            if (Math.Abs(measuredRate / rate - 1) > .15)
                throw new IOException($"Device delivered about {measuredRate:F2} fps instead of {CaptureSupport.Number(rate)} fps. Choose another mode or check the signal.");
            if (stream.Width != size.Width || stream.Height != size.Height)
                throw new IOException($"Device delivered {stream.Width}x{stream.Height}, not the selected resolution.");
            if (color.Kind == "pixel_format" && stream.PixelFormat != color.Format && !(color.Format == "gray8" && stream.PixelFormat == "gray"))
                throw new IOException($"Device delivered {stream.PixelFormat}, not {color.Format}.");
            if (color.Kind == "vcodec" && stream.Codec != color.Format)
                throw new IOException($"Device delivered {stream.Codec}, not {color.Format}.");
            Console.WriteLine($"Received: {stream.Codec}, {stream.PixelFormat}, {stream.Width}x{stream.Height}; sample rate {measuredRate:F2} fps (requested {CaptureSupport.Number(rate)}).");

            var modes = new List<CaptureRecording> { CaptureRecording.Direct };
            var labels = new List<string>
            {
                "Pass-through — NO additional transcoding\n" +
                (stream.Codec == "rawvideo"
                    ? $"      Save the received {stream.PixelFormat} pixels uncompressed.\n      Lowest encoding work; very large files and high disk throughput."
                    : $"      Save the received {stream.Codec} stream with its existing compression.\n      No re-encoding and no additional compression quality loss.")
            };
            if (CaptureSupport.LosslessPixel(stream.PixelFormat) != null)
            {
                modes.Add(CaptureRecording.LosslessCpu);
                labels.Add("Lossless transcoding — FFV1 using the CPU\n" +
                    "      Compress without losing any received picture detail.\n" +
                    "      Uses CPU processing; file size and speed depend on the video.");
            }
            modes.Add(CaptureRecording.LossyHardware);
            labels.Add("Lossy transcoding — H.264/H.265 using " + (mac ? "Apple hardware" : "NVIDIA hardware") + "\n" +
                "      Smaller files by discarding some picture detail.\n" +
                "      You choose the target quality; the hardware handles encoding.");
            modes.Add(CaptureRecording.LossyCpu);
            labels.Add("Lossy transcoding — H.264/H.265 using the CPU\n" +
                "      Smaller files by discarding some picture detail.\n" +
                "      You choose the target quality; uses more CPU processing.");
            Console.WriteLine("\nNow choose how to save the received video.");
            Console.WriteLine("Pass-through copies the stream; transcoding creates a new encoded stream.");
            Console.WriteLine("Choose pass-through for minimal encoding work; uncompressed input needs a fast disk.");
            Console.WriteLine("Choose lossless to preserve all received picture detail, or lossy for smaller files.");
            Console.WriteLine("Pass-through and lossless modes cannot restore detail already lost in the card or driver.");
            if (mac) Console.WriteLine("Pass-through preserves what macOS delivers, which may differ from the card's original compressed stream.");
            var recording = modes[Choose("Recording mode — how the file is stored", labels)];
            var hardware = mac ? HardwareAccelerationMode.AppleVideoToolbox : HardwareAccelerationMode.NvidiaNvenc;
            var codec = VideoCodec.H264;
            int quality = 2;
            if (recording is CaptureRecording.LossyCpu or CaptureRecording.LossyHardware)
            {
                codec = Choose("Output codec", new[] { "H.264 (widest compatibility)", "H.265 (more compression)" }) == 0 ? VideoCodec.H264 : VideoCodec.H265;
                quality = Choose("Target quality (lossy, 8-bit YUV 4:2:0)", new[] { "0 — Passable", "1 — Good", "2 — Better", "3 — High quality", "4 — Archive quality (still lossy)" }, 2);
                if (size.Width % 2 != 0 || size.Height % 2 != 0)
                    throw new IOException("H.264/H.265 capture requires even dimensions. Choose an even capture resolution or direct/lossless recording.");
            }
            var outputOptions = CaptureSupport.Output(recording, stream, codec, hardware, quality);
            string extension = CaptureSupport.Extension(CaptureSupport.Container(recording, stream));
            string trial = Path.Combine(temp, "test" + extension);
            // Use the actual received sample and the exact output codec/muxer options.
            // Failure never silently switches hardware, changes quality, or truncates a recording.
            Console.WriteLine("Checking the recording codec and container...");
            var checkedOutput = await CaptureSupport.Probe(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", sample }
                .Concat(outputOptions).Concat(new[] { "-n", trial }));
            if (checkedOutput.Code != 0 || checkedOutput.Error.Contains("Incompatible pixel format", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Recording mode unavailable for this input. Choose another mode or codec.\n{checkedOutput.Error}");
            var trialInfo = await CaptureSupport.Probe(ffprobe, new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
                "stream=codec_name,pix_fmt,width,height", "-of", "json", trial });
            if (trialInfo.Code != 0) throw new IOException(trialInfo.Error);
            var encoded = ReadStream(trialInfo.Output);
            string expectedCodec = recording switch
            {
                CaptureRecording.Direct => stream.Codec,
                CaptureRecording.LosslessCpu => "ffv1",
                _ => codec == VideoCodec.H264 ? "h264" : "hevc"
            };
            string expectedPixel = recording switch
            {
                CaptureRecording.Direct => stream.PixelFormat,
                CaptureRecording.LosslessCpu => CaptureSupport.LosslessPixel(stream.PixelFormat)!,
                _ => "yuv420p"
            };
            if (encoded.Codec != expectedCodec || encoded.PixelFormat != expectedPixel || encoded.Width != size.Width || encoded.Height != size.Height)
                throw new IOException($"Recording codec changed the requested output format: {encoded}. Choose another mode.");
            string suggested = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{size.Width}x{size.Height}@{CaptureSupport.Number(rate)}_{recording}{extension}";
            Console.Write($"\nOutput filename (ENTER for {suggested}): ");
            string path = Console.ReadLine()?.Trim() ?? throw new IOException("Input closed.");
            if (path.Length == 0) path = suggested;
            if (Path.GetExtension(path).Length == 0) path += extension;
            if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"This recording mode uses {extension}. Choose that extension so the container matches the filename.");
            path = Path.GetFullPath(path);
            if (File.Exists(path)) throw new IOException($"File already exists; choose a new filename: {path}");
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new IOException("Output directory does not exist.");
            Console.WriteLine($"\nReady to record video only to {path}");
            if (recording == CaptureRecording.Direct && stream.Codec == "rawvideo")
                Console.WriteLine("Uncompressed direct copy needs high disk throughput and creates large NUT files.");
            Console.WriteLine("Watch FFmpeg's speed and buffer/drop warnings; sustained capture depends on the card, encoder, and disk.");
            Console.WriteLine("\nHOW TO STOP RECORDING");
            Console.WriteLine("Press q in this terminal to stop recording and finish saving the file.");
            Console.WriteLine("Wait for the 'Recording saved' message before closing the terminal.");
            Console.Write("\nPress ENTER to start recording, or Ctrl+C to cancel: ");
            if (Console.ReadLine() == null) throw new IOException("Input closed before recording started.");
            Console.WriteLine("\nStarting recording...");
            if (!mac)
                return await RecordWindows(ffmpeg, device.Id, windowsMode!, outputOptions, path);
            var arguments = CaptureSupport.Input(selection, mac).Concat(outputOptions).Concat(new[] { "-stats", "-n", path }).ToList();
            var info = CaptureSupport.StartInfo(ffmpeg, arguments, false);
            info.RedirectStandardError = true;
            using var proc = Process.Start(info) ?? throw new IOException("Could not start recording.");
            bool modeChanged = false;
            var diagnostics = CaptureSupport.ReadLines(proc.StandardError, line =>
            {
                Console.Error.WriteLine(line);
                if (HasModeFallback(line) || line.Contains("Incompatible pixel format", StringComparison.OrdinalIgnoreCase))
                {
                    modeChanged = true;
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                }
            });
            // Let FFmpeg receive terminal Ctrl+C and finalize its container; keep the
            // parent alive until the child has exited. 'q' is the normal stop control.
            ConsoleCancelEventHandler cancel = (_, e) => e.Cancel = true;
            Console.CancelKeyPress += cancel;
            try { await Task.WhenAll(proc.WaitForExitAsync(), diagnostics); }
            finally { Console.CancelKeyPress -= cancel; }
            if (modeChanged)
                Console.WriteLine("Recording stopped because the input or encoder changed the requested format. Any partial file has been retained.");
            else if (proc.ExitCode != 0)
                Console.WriteLine($"FFmpeg exited with code {proc.ExitCode}. Any partial recording has been retained at {path}.");
            else Console.WriteLine($"Recording saved: {path}");
            return modeChanged ? 1 : proc.ExitCode;
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [SupportedOSPlatform("windows6.1")]
    private static async Task<(int Code, string Output, string Error, double? CaptureRate)> CaptureWindowsSample(
        string ffmpeg, string sourceId, WindowsCaptureMode mode, string sample)
    {
        var arguments = WindowsMediaFoundationCapture.FfmpegInput(mode)
            .Concat(new[] { "-map", "0:v:0", "-an", "-frames:v", "12", "-c:v", "copy", "-f", "nut", "-n", sample });
        var info = CaptureSupport.StartInfo(ffmpeg, arguments, true);
        info.RedirectStandardInput = true;
        using var process = Process.Start(info) ?? throw new IOException("Could not start FFmpeg for the capture sample.");
        Task<string> output = CaptureSupport.ReadOutput(process.StandardOutput);
        Task<string> error = CaptureSupport.ReadOutput(process.StandardError);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            WindowsCaptureResult captured;
            try
            {
                captured = await WindowsMediaFoundationCapture.WriteFramesAsync(sourceId, mode,
                    process.StandardInput.BaseStream, 12, timeout.Token);
            }
            finally { process.StandardInput.Close(); }
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
            return (process.ExitCode, await output, await error,
                captured.MeasuredRate > 0 ? captured.MeasuredRate : null);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw new IOException($"Capture sample timed out. Check the video signal and camera permission, then retry.\n{await error}");
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw;
        }
    }

    [SupportedOSPlatform("windows6.1")]
    private static async Task<int> RecordWindows(string ffmpeg, string sourceId, WindowsCaptureMode mode,
        IReadOnlyList<string> outputOptions, string path)
    {
        var arguments = WindowsMediaFoundationCapture.FfmpegInput(mode).Concat(outputOptions)
            .Concat(new[] { "-stats", "-n", path });
        var info = CaptureSupport.StartInfo(ffmpeg, arguments, false);
        info.RedirectStandardInput = true;
        info.RedirectStandardError = true;
        using var process = Process.Start(info) ?? throw new IOException("Could not start recording.");
        var diagnostics = CaptureSupport.ReadLines(process.StandardError, Console.Error.WriteLine);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        Exception? captureFailure = null;
        WindowsCaptureResult? captured = null;
        try
        {
            Task<WindowsCaptureResult> capture = WindowsMediaFoundationCapture.WriteFramesAsync(sourceId, mode,
                process.StandardInput.BaseStream, null, stop.Token);
            while (!capture.IsCompleted && !process.HasExited)
            {
                if (Console.KeyAvailable && char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar) == 'q')
                    stop.Cancel();
                await Task.Delay(50);
            }
            if (process.HasExited) stop.Cancel();
            try { captured = await capture; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { captureFailure = ex; }
            finally { process.StandardInput.Close(); }
            if (captureFailure != null && !process.HasExited) process.Kill(entireProcessTree: true);
            await Task.WhenAll(process.WaitForExitAsync(), diagnostics);
        }
        finally { Console.CancelKeyPress -= cancel; }

        if (captureFailure != null)
        {
            Console.WriteLine($"Recording stopped because Media Foundation capture failed: {captureFailure.Message}");
            Console.WriteLine($"Any partial recording has been retained at {path}.");
            return 1;
        }
        if (process.ExitCode != 0)
        {
            Console.WriteLine($"FFmpeg exited with code {process.ExitCode}. Any partial recording has been retained at {path}.");
            return process.ExitCode;
        }
        Console.WriteLine($"Recording saved: {path}");
        if (captured != null) Console.WriteLine($"Captured {captured.Frames} frames; measured device cadence {captured.MeasuredRate:F3} fps.");
        return 0;
    }

    internal static bool HasModeFallback(string log) =>
        log.Contains("Overriding selected pixel format", StringComparison.OrdinalIgnoreCase) ||
        log.Contains("falling back to default", StringComparison.OrdinalIgnoreCase);

    internal static CaptureStream ReadStream(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() != 1) throw new IOException("Expected one captured video stream.");
        var s = streams[0];
        return new(s.GetProperty("codec_name").GetString()!, s.GetProperty("pix_fmt").GetString()!,
            s.GetProperty("width").GetInt32(), s.GetProperty("height").GetInt32());
    }

    internal static double SampleRate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var times = doc.RootElement.GetProperty("packets").EnumerateArray()
            .Where(p => p.TryGetProperty("pts_time", out _))
            .Select(p => double.Parse(p.GetProperty("pts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
        var intervals = times.Zip(times.Skip(1), (a, b) => b - a).Where(t => t > 0).Order().ToArray();
        if (intervals.Length < 2) throw new IOException("Not enough captured timestamps to verify the frame rate.");
        return 1 / intervals[intervals.Length / 2];
    }
}
