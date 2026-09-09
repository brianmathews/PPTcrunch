using System.Text.Json;
using PPTcrunch;

internal static class CaptureTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Capture: " + message);
    }

    internal static void Run()
    {
        var devices = CaptureSupport.ParseDevices("""
            [AVFoundation indev @ 1] AVFoundation video devices:
            [AVFoundation indev @ 1] [0] Built-in Camera
            [AVFoundation indev @ 1] [3] USB HDMI Prototype
            [AVFoundation indev @ 1] [4] Capture screen 0
            [AVFoundation indev @ 1] AVFoundation audio devices:
            [AVFoundation indev @ 1] [0] USB Audio
            """, true);
        Check(devices.Count == 2 && devices[1].Id == "3", "AVFoundation IDs survive filtering; screens/audio excluded");
        var stable = CaptureSupport.ParseDevices("[AVFoundation indev @ 1] AVFoundation video devices:\n[AVFoundation indev @ 1] [4] USB Video  [uid:0x1100000534d2109]", true).Single();
        Check(stable.IsUniqueId && stable.Name == "USB Video" && stable.Id == "0x1100000534d2109", "stable device ID parsed separately from display name");
        Check(CaptureSupport.DeviceInput(stable, true).SequenceEqual(new[] { "-video_device_id", "uid:0x1100000534d2109", "-i", "default:none" }), "stable ID used across separate FFmpeg invocations");
        var windows = CaptureSupport.ParseDevices("""
            [dshow @ 1] "USB Video" (video)
            [dshow @ 1]   Alternative name "@device_pnp_123"
            [dshow @ 1] "Microphone" (audio)
            """, false);
        Check(windows.Count == 1 && windows[0].Name == "USB Video", "DirectShow devices only");
        var formats = CaptureSupport.ParseFormats("""
            [dshow @ 1] pixel_format=yuyv422 min s=1920x1080 fps=29.97 max s=1920x1080 fps=29.97
            [dshow @ 1] vcodec=mjpeg min s=1280x720 fps=5 max s=1280x720 fps=60
            """, false);
        Check(formats.Count == 2 && !CaptureSupport.SupportsRate(formats[0], 30, false), "fractional rate never rounded up to 30");
        Check(CaptureSupport.Rates(formats, false).Contains(29.97), "fractional device endpoints offered");
        var mac = CaptureSupport.ParseFormats("""
            [avfoundation @ 1] Supported modes:
            [avfoundation @ 1] 1920x1080@[15.000000 59.940060]fps
            [avfoundation @ 1] 1280x720@[30.000000 30.000000]fps
            """, true);
        Check(mac.Count == 2 && !CaptureSupport.SupportsRate(mac[0], 30, true), "AVFoundation range maxima only");
        Check(CaptureSupport.Rates(mac, true).SequenceEqual(new[] { 59.940060, 30.0 }), "advertised Mac rates retained");
        var pixels = CaptureSupport.ParsePixels("""
            [avfoundation @ 1] Supported pixel formats:
            [avfoundation @ 1]   uyvy422
            [avfoundation @ 1]   nv12
            [avfoundation @ 1] Overriding selected pixel format to use uyvy422 instead.
            """);
        Check(pixels.SequenceEqual(new[] { "uyvy422", "nv12" }), "pixel list excludes diagnostics");
        Check(CaptureMode.HasModeFallback("Overriding selected pixel format to use nv12 instead."), "fallback rejected");
        Check(Math.Abs(CaptureMode.SampleRate("""{"packets":[{"pts_time":"1.0"},{"pts_time":"1.2"},{"pts_time":"1.4"}]}""") - 5) < .001, "actual sample cadence detects 5fps fallback");
        var mode = new CaptureSelection(new("USB \"HDMI\"", "test"), 1920, 1080, 29.97, "pixel_format", "yuyv422");
        var input = CaptureSupport.Input(mode, false);
        Check(input.Contains("-pixel_format") && !input.Contains("-vcodec"), "raw DirectShow input uses pixel_format");
        var psi = CaptureSupport.StartInfo("ffmpeg", input, true);
        Check(psi.ArgumentList.Last() == "video=USB \"HDMI\"", "device names remain one unescaped process argument");
        Check(CaptureSupport.Input(mode with { Device = new("3", "USB") }, true).Last() == "3:none", "Mac input never opens audio");
        Check(CaptureSupport.LosslessPixel("uyvy422") == "yuv422p", "422 retained");
        Check(CaptureSupport.LosslessPixel("yuv444p10le") == "yuv444p10le", "10-bit 444 retained");
        Check(CaptureSupport.LosslessPixel("rgba") == "bgra", "RGB alpha retained");
        Check(CaptureSupport.LosslessPixel("unknown") == null, "unsupported lossless input not guessed");
        foreach (var recording in Enum.GetValues<CaptureRecording>())
        foreach (var hardware in new[] { HardwareAccelerationMode.NvidiaNvenc, HardwareAccelerationMode.AppleVideoToolbox })
        {
            var opts = CaptureSupport.Output(recording, new("rawvideo", "nv12", 1920, 1080), VideoCodec.H264, hardware, 2);
            Check(opts.Contains("-an") && opts.Contains("passthrough") && !opts.Contains("-r"), "video-only and timestamp preservation");
            if (recording == CaptureRecording.Direct)
                Check(opts.Contains("copy") && !opts.Contains("-vf") && !opts.Contains("-pix_fmt"), "direct mode does not convert pixels");
            if (recording == CaptureRecording.LossyHardware && hardware == HardwareAccelerationMode.AppleVideoToolbox)
                Check(opts.Contains("-q:v") && !opts.Contains("-preset") && !opts.Contains("-cq") && opts.Contains("-allow_sw"), "Apple-specific encoding options");
        }
        Console.WriteLine("PASS: capture discovery, fractional modes, lossless format policy, and platform arguments.");
    }

    internal static async Task IntegrationAsync(bool hardware)
    {
        string ffmpeg = await CaptureRuntime.ExecutableAsync();
        string ffprobe = Path.Combine(Path.GetDirectoryName(ffmpeg)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        string root = Path.GetFullPath(Path.Combine("artifacts", "capture-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        async Task<string> Run(string executable, IEnumerable<string> arguments)
        {
            var r = await CaptureSupport.Probe(executable, arguments, 60);
            Check(r.Code == 0, r.Error);
            return r.Output;
        }
        async Task<CaptureStream> Inspect(string path) => CaptureMode.ReadStream(await Run(ffprobe,
            new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,pix_fmt,width,height", "-of", "json", path }));
        async Task<string[]> Hashes(string path, string? pixel, bool copy = false)
        {
            var a = new List<string> { "-v", "error", "-i", path, "-map", "0:v:0", "-an" };
            if (copy) a.AddRange(new[] { "-c:v", "copy" });
            else a.AddRange(new[] { "-pix_fmt", pixel!, "-c:v", "rawvideo" });
            a.AddRange(new[] { "-f", "framemd5", "-" });
            return (await Run(ffmpeg, a)).Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split(',').Last().Trim()).ToArray();
        }
        async Task CheckStreamsAndTimes(string source, string output)
        {
            var options = new[] { "-v", "error", "-show_streams", "-show_packets", "-of", "json" };
            using var src = JsonDocument.Parse(await Run(ffprobe, options.Append(source)));
            using var dst = JsonDocument.Parse(await Run(ffprobe, options.Append(output)));
            var streams = dst.RootElement.GetProperty("streams");
            Check(streams.GetArrayLength() == 1 && streams[0].GetProperty("codec_type").GetString() == "video", "output has exactly one video stream and no audio");
            double[] Times(JsonDocument doc) => doc.RootElement.GetProperty("packets").EnumerateArray()
                .Where(p => p.GetProperty("codec_type").GetString() == "video")
                .Select(p => double.Parse(p.GetProperty("pts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray();
            var before = Times(src); var after = Times(dst);
            // MP4 can shift the time origin to accommodate encoder reordering.
            // Presentation intervals and frame counts must remain unchanged.
            Check(before.Length == after.Length && before.Zip(after).All(t => Math.Abs((t.First - before[0]) - (t.Second - after[0])) < .0011), "all frames and fractional timestamp intervals preserved to container precision");
        }

        foreach (string pixel in new[] { "yuyv422", "nv12", "bgr0", "bgra", "yuv444p10le" })
        {
            string source = Path.Combine(root, pixel + ".nut");
            string signal = pixel == "yuv444p10le"
                ? "nullsrc=size=320x180:rate=30000/1001,format=yuv444p10le,geq=lum='mod(X+Y+N,1024)':cb='mod(2*X+1,1024)':cr='mod(3*Y+2,1024)'"
                : pixel == "bgra"
                    ? "testsrc=size=320x180:rate=30000/1001,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='mod(X+Y,256)'"
                    : "testsrc2=size=320x180:rate=30000/1001";
            await Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", signal, "-f", "lavfi", "-i", "sine=frequency=440",
                "-map", "0:v:0", "-map", "1:a:0", "-frames:v", "12", "-t", "0.4", "-c:v", "rawvideo", "-pix_fmt", pixel, "-c:a", "pcm_s16le", "-f", "nut", source });
            var stream = await Inspect(source);
            foreach (var recording in new[] { CaptureRecording.Direct, CaptureRecording.LosslessCpu })
            {
                string output = Path.Combine(root, pixel + "-" + recording + CaptureSupport.Extension(CaptureSupport.Container(recording, stream)));
                await Run(ffmpeg, new[] { "-v", "error", "-i", source }.Concat(CaptureSupport.Output(recording, stream, VideoCodec.H264, HardwareAccelerationMode.None, 2)).Append(output));
                // BGR0's fourth byte is padding, not alpha; FFV1 may normalize it.
                // Compare all RGB samples, and test meaningful alpha separately with BGRA.
                string canonical = pixel == "bgr0" ? "bgr24" : CaptureSupport.LosslessPixel(pixel)!;
                var sourceHashes = await Hashes(source, canonical);
                var outputHashes = await Hashes(output, canonical);
                Check(sourceHashes.SequenceEqual(outputHashes), $"exact decoded pixel preservation: {pixel}/{recording}");
                await CheckStreamsAndTimes(source, output);
            }
        }
        // MJPEG copy preserves compressed packets; lossless FFV1 preserves decoded
        // full-range samples without a hidden full-to-limited range conversion.
        string mjpeg = Path.Combine(root, "mjpeg.nut");
        await Run(ffmpeg, new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30000/1001", "-frames:v", "12", "-c:v", "mjpeg", "-pix_fmt", "yuvj422p", "-f", "nut", mjpeg });
        var jpegStream = await Inspect(mjpeg);
        foreach (var recording in new[] { CaptureRecording.Direct, CaptureRecording.LosslessCpu, CaptureRecording.LossyCpu })
        foreach (var codec in recording == CaptureRecording.LossyCpu ? new[] { VideoCodec.H264, VideoCodec.H265 } : new[] { VideoCodec.H264 })
        {
            string output = Path.Combine(root, $"mjpeg-{recording}-{codec}" + CaptureSupport.Extension(CaptureSupport.Container(recording, jpegStream)));
            await Run(ffmpeg, new[] { "-v", "error", "-i", mjpeg }.Concat(CaptureSupport.Output(recording, jpegStream, codec, HardwareAccelerationMode.None, 2)).Append(output));
            if (recording == CaptureRecording.Direct)
            {
                var before = await Hashes(mjpeg, null, true);
                var after = await Hashes(output, null, true);
                Check(before.SequenceEqual(after), "MJPEG packets copied unchanged");
            }
            else if (recording == CaptureRecording.LosslessCpu)
            {
                var before = await Hashes(mjpeg, "yuvj422p");
                var after = await Hashes(output, "yuvj422p");
                Check(before.SequenceEqual(after), "MJPEG full-range samples preserved");
            }
            await CheckStreamsAndTimes(mjpeg, output);
        }
        if (hardware)
        {
            var platform = OperatingSystem.IsMacOS() ? HardwareAccelerationMode.AppleVideoToolbox : HardwareAccelerationMode.NvidiaNvenc;
            foreach (var codec in new[] { VideoCodec.H264, VideoCodec.H265 })
            {
                string output = Path.Combine(root, $"hardware-{codec}.mp4");
                await Run(ffmpeg, new[] { "-v", "error", "-i", mjpeg }.Concat(CaptureSupport.Output(CaptureRecording.LossyHardware, jpegStream, codec, platform, 2)).Append(output));
                await CheckStreamsAndTimes(mjpeg, output);
            }
        }
        Console.WriteLine($"PASS: capture packet/pixel equality, bit depth/alpha/range, frame counts/timestamps, video-only muxing and encoding. Artifacts: {root}");
    }
}
