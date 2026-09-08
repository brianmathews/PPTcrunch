using System.Diagnostics;
using System.Text.Json;
using PPTcrunch;

internal static class Av1WorkflowTests
{
    internal static async Task RunAsync()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
        string ffmpeg = await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync() ?? throw new Exception("FFmpeg missing");
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        string directory = Path.GetFullPath("artifacts/av1-tests");
        Directory.CreateDirectory(directory);
        async Task<string> Run(string executable, params string[] arguments)
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(true); throw; }
            string result = await stdout;
            Check(process.ExitCode == 0, await stderr);
            return result;
        }
        string source = Path.Combine(directory, "rgb.mkv");
        await Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=320x180:rate=50:duration=1", "-c:v", "ffv1", "-pix_fmt", "bgr0", "-y", source);
        var settings = new UserSettings { Codec = VideoCodec.AV1, MaxWidth = 160, ReduceHighFrameRates = true };
        Check(await new VideoProcessor().ProcessVideoFileAsync(source, settings), "AV1 standalone RGB conversion");
        string output = Path.Combine(directory, "rgb-L2AV1-160-25FPS.mp4");
        using var probe = JsonDocument.Parse(await Run(ffprobe, "-v", "error", "-show_streams", "-of", "json", output));
        var video = probe.RootElement.GetProperty("streams")[0];
        Check(video.GetProperty("codec_name").GetString() == "av1" && video.GetProperty("pix_fmt").GetString() == "yuv420p10le", "AV1 10-bit output from 8-bit RGB");
        Check(video.GetProperty("width").GetInt32() == 160 && video.GetProperty("avg_frame_rate").GetString() == "25/1", "AV1 resizing and FPS conversion");
        byte[] bytes = await File.ReadAllBytesAsync(output);
        string boxes = System.Text.Encoding.Latin1.GetString(bytes);
        Check(boxes.IndexOf("moov", StringComparison.Ordinal) < boxes.IndexOf("mdat", StringComparison.Ordinal), "AV1 fast-start metadata precedes media");

        if (!await GPUDetectionService.ProbeAv1NvencAsync())
        {
            var fallback = await EmbeddedFFmpegRunner.CompressVideoWithResultAsync(source, Path.Combine(directory, "fallback.mp4"),
                new UserSettings { Codec = VideoCodec.AV1, UseGPUAcceleration = true, HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc });
            Check(fallback.Success && fallback.HardwareAcceleration == HardwareAccelerationMode.None, "Unsupported AV1 GPU falls back to AV1 CPU");
        }

        string small = Path.Combine(directory, "static.mkv");
        await Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "color=size=320x180:rate=24:duration=0.04", "-c:v", "ffv1", "-y", small);
        Check(await new VideoProcessor().ProcessVideoFileAsync(small, new UserSettings { Codec = VideoCodec.AV1 }), "Explicit AV1 delivery export retained");
        Check(new FileInfo(Path.Combine(directory, "static-L2AV1.mp4")).Length > new FileInfo(small).Length, "Larger explicit AV1 output retained");
        Check(!VideoProcessor.ShouldSkipRecompression("clip - L4AV1-1920-25FPS.mp4", settings), "AV1 archive reusable");
        try { await new PPTXVideoProcessor().ProcessAsync("missing.pptx", settings); throw new Exception("AV1 PPTX guard missing"); }
        catch (NotSupportedException) { }

        string variable = Path.Combine(directory, "variable.mkv");
        await Run(ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=24:duration=1", "-vf", "select='not(eq(mod(n,5),0))'", "-fps_mode", "vfr", "-c:v", "ffv1", "-y", variable);
        string variableOutput = Path.Combine(directory, "variable.mp4");
        Check(await EmbeddedFFmpegRunner.CompressVideoAsync(variable, variableOutput, new UserSettings { Codec = VideoCodec.AV1 }), "AV1 VFR encode");
        async Task<double[]> Times(string path)
        {
            using var doc = JsonDocument.Parse(await Run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_frames", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", path));
            return doc.RootElement.GetProperty("frames").EnumerateArray().Select(frame => double.Parse(frame.GetProperty("best_effort_timestamp_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        }
        var before = await Times(variable);
        var after = await Times(variableOutput);
        Check(before.Length == after.Length, "AV1 preserves VFR frame count");
        for (int i = 0; i < before.Length; i++) Check(Math.Abs((before[i] - before[0]) - (after[i] - after[0])) < .002, "AV1 preserves VFR timing");
        Console.WriteLine("PASS: AV1 RGB input, web MP4, fast start, filenames, retention, GPU fallback, PowerPoint guard and VFR timing.");
    }
}
