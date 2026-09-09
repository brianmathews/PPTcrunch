using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PPTcrunch;

internal enum CaptureRecording { Direct, LosslessCpu, LossyHardware, LossyCpu }
internal record CaptureDevice(string Id, string Name, bool IsUniqueId = false);
internal record CaptureFormat(int Width, int Height, double MinRate, double MaxRate, string Kind, string Format);
internal record CaptureSelection(CaptureDevice Device, int Width, int Height, double Rate, string Kind, string Format);
internal record CaptureStream(string Codec, string PixelFormat, int Width, int Height);

internal static class CaptureSupport
{
    internal static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    internal static string DescribeFormat(string kind, string format) => format.ToLowerInvariant() switch
    {
        "uyvy422" => "UYVY — uncompressed video, 4:2:2 color (uyvy422)\n" +
            "      More color detail than NV12, useful for colored text and graphics.\n" +
            "      Same picture detail and data size as YUYV.",
        "yuyv422" => "YUYV — uncompressed video, 4:2:2 color (yuyv422)\n" +
            "      Same picture detail and data size as UYVY; only the byte order differs.\n" +
            "      More color detail than NV12.",
        "nv12" or "nv21" => $"{format.ToUpperInvariant()} — uncompressed video, 4:2:0 color\n" +
            "      Same brightness detail, but less color detail than UYVY/YUYV.\n" +
            "      About 25% less data at the same resolution and frame rate.",
        "mjpeg" => "MJPEG — video already compressed as individual JPEG frames\n" +
            "      Image quality depends on the card's JPEG compression settings.\n" +
            "      Usually sends less data than uncompressed video.",
        "h264" => "H.264 — video already compressed before PPTcrunch receives it\n" +
            "      Color detail and compression quality depend on the card's settings.",
        "hevc" => "H.265 / HEVC — video already compressed before PPTcrunch receives it\n" +
            "      Color detail and compression quality depend on the card's settings.",
        "rgb24" or "bgr24" => $"{format.ToUpperInvariant()} — uncompressed red, green and blue values\n" +
            "      Full color values for every pixel; a large data stream.\n" +
            "      Can retain more color detail than 4:2:2/4:2:0 if present in the source.",
        "0rgb" or "rgb0" or "0bgr" or "bgr0" => $"{format.ToUpperInvariant()} — uncompressed RGB, 8 bits per color plus one padding byte\n" +
            "      Same color precision as RGB24; uses 4 bytes per pixel instead of 3.\n" +
            "      macOS/driver may convert YUV to RGB; this cannot restore missing color detail.",
        "bgra" or "rgba" or "argb" or "abgr" => $"{format.ToUpperInvariant()} — uncompressed color plus transparency (alpha)\n" +
            "      Full color values for every pixel; a large data stream.",
        _ => kind == "vcodec"
            ? $"{format} — encoded video delivered by the card/driver\n      Picture quality depends on the card's format and compression settings."
            : $"{format} — uncompressed pixel format delivered by the card/driver\n      Color detail and bit depth depend on this device format."
    };

    internal static List<CaptureDevice> ParseDevices(string text, bool mac)
    {
        var result = new List<CaptureDevice>();
        bool videoSection = !mac;
        foreach (string line in text.Split('\n'))
        {
            if (mac && line.Contains("AVFoundation video devices:")) { videoSection = true; continue; }
            if (mac && line.Contains("AVFoundation audio devices:")) videoSection = false;
            if (!videoSection) continue;
            var match = Regex.Match(line, mac ? @"\]\s+\[(\d+)\]\s+(.+?)\s*$" : "\\\"(.+)\\\"\\s+\\(video\\)");
            if (!match.Success) continue;
            string name = match.Groups[mac ? 2 : 1].Value.Trim();
            // AVFoundation also lists displays. This feature records physical video inputs only.
            if (mac && name.StartsWith("Capture screen ", StringComparison.OrdinalIgnoreCase)) continue;
            // USB devices may append [serial:...] after [uid:...]. Keep each
            // bracketed field separate so the serial cannot become part of the ID.
            var uid = mac ? Regex.Match(name, @"\s+\[uid:([^\[\]\r\n]+)\](?:\s+\[serial:[^\[\]\r\n]*\])?\s*$") : Match.Empty;
            result.Add(uid.Success
                ? new(uid.Groups[1].Value, name[..uid.Index].Trim(), true)
                : new(mac ? match.Groups[1].Value : name, name));
        }
        return result;
    }

    internal static List<CaptureFormat> ParseFormats(string text, bool mac)
    {
        var result = new List<CaptureFormat>();
        string pattern = mac
            ? @"(?<w>\d+)x(?<h>\d+)@\[(?<min>[\d.]+)\s+(?<max>[\d.]+)\]fps"
            : @"(?<kind>vcodec|pixel_format)=(?<fmt>\S+)\s+min s=(?<w>\d+)x(?<h>\d+)\s+fps=(?<min>[\d.]+)\s+max s=\k<w>x\k<h>\s+fps=(?<max>[\d.]+)";
        foreach (Match m in Regex.Matches(text, pattern))
        {
            var format = new CaptureFormat(int.Parse(m.Groups["w"].Value), int.Parse(m.Groups["h"].Value),
                double.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture),
                mac ? "pixel_format" : m.Groups["kind"].Value, mac ? "" : m.Groups["fmt"].Value);
            if (format.Width > 0 && format.Height > 0 && format.MinRate > 0 && format.MaxRate >= format.MinRate)
                result.Add(format);
        }
        return result.Distinct().ToList();
    }

    internal static bool SupportsRate(CaptureFormat mode, double rate, bool mac) => mac
        // AVFoundation's FFmpeg backend selects a range's maximum, not arbitrary rates within it.
        ? Math.Abs(mode.MaxRate - rate) < 0.00001
        : rate >= mode.MinRate - 0.00001 && rate <= mode.MaxRate + 0.00001;

    internal static List<double> Rates(IEnumerable<CaptureFormat> modes, bool mac)
    {
        var list = modes.ToList();
        double[] common = { 5, 10, 15, 20, 24, 24000.0 / 1001, 25, 30, 30000.0 / 1001, 50, 60, 60000.0 / 1001, 120 };
        return list.SelectMany(m => mac ? new[] { m.MaxRate } : new[] { m.MinRate, m.MaxRate })
            .Concat(mac ? Array.Empty<double>() : common.Where(r => list.Any(m => SupportsRate(m, r, false))))
            .DistinctBy(r => Math.Round(r, 6)).OrderDescending().ToList();
    }

    internal static List<string> ParsePixels(string text)
    {
        var result = new List<string>();
        bool inList = false;
        foreach (string line in text.Split('\n'))
        {
            if (line.Contains("Supported pixel formats:")) { inList = true; continue; }
            if (!inList) continue;
            // Formats such as 0rgb/0bgr start with a digit. Do not truncate the
            // list at those entries and lose every following format as well.
            var m = Regex.Match(line, @"\]\s+([a-z0-9][a-z0-9_]+)\s*$");
            if (!m.Success) { inList = false; continue; }
            result.Add(m.Groups[1].Value);
        }
        return result.Distinct().ToList();
    }

    internal static List<string> Input(CaptureSelection mode, bool mac)
    {
        var args = new List<string> { "-hide_banner", "-thread_queue_size", "16", "-f", mac ? "avfoundation" : "dshow" };
        if (mac) args.AddRange(new[] { "-drop_late_frames", "0" });
        else args.AddRange(new[] { "-rtbufsize", "512M" });
        args.AddRange(new[] { "-video_size", $"{mode.Width}x{mode.Height}", "-framerate", Number(mode.Rate),
            mode.Kind == "vcodec" ? "-vcodec" : "-pixel_format", mode.Format });
        args.AddRange(DeviceInput(mode.Device, mac));
        return args;
    }

    internal static string[] DeviceInput(CaptureDevice device, bool mac) => mac && device.IsUniqueId
        ? new[] { "-video_device_id", "uid:" + device.Id, "-i", "default:none" }
        : new[] { "-i", mac ? $"{device.Id}:none" : $"video={device.Id}" };

    // Only reversible packing changes are permitted here. Never turn RGB/4:4:4/10-bit
    // input into 8-bit 4:2:2 simply to fit FFV1's default format.
    internal static string? LosslessPixel(string input) => input switch
    {
        "yuyv422" or "uyvy422" => "yuv422p",
        "nv12" or "nv21" => "yuv420p",
        "rgb24" or "bgr24" or "rgb0" or "0rgb" or "0bgr" => "bgr0",
        "rgba" or "argb" or "abgr" => "bgra",
        "yuvj420p" => "yuv420p", "yuvj422p" => "yuv422p", "yuvj444p" => "yuv444p", "yuvj440p" => "yuv440p",
        "gray8" => "gray",
        "p010le" => "yuv420p10le",
        "bgr0" or "bgra" or "gray" or "gray16le" or "rgb48le" or "rgba64le" => input,
        _ when Regex.IsMatch(input, @"^(yuv(a)?(420|422|444|440|411|410)p(9le|10le|12le|14le|16le)?|gbr(a)?p(9le|10le|12le|14le|16le))$") => input,
        _ => null
    };

    internal static string Container(CaptureRecording recording, CaptureStream stream) => recording switch
    {
        CaptureRecording.Direct => stream.Codec is "mjpeg" or "h264" or "hevc" ? "matroska" : "nut",
        CaptureRecording.LosslessCpu => "matroska",
        _ => "mp4"
    };
    internal static string Extension(string container) => container == "matroska" ? ".mkv" : "." + container;

    internal static List<string> Output(CaptureRecording recording, CaptureStream stream, VideoCodec codec,
        HardwareAccelerationMode hardware, int quality)
    {
        var args = new List<string> { "-map", "0:v:0", "-an", "-sn", "-dn", "-fps_mode", "passthrough" };
        if (recording == CaptureRecording.Direct) args.AddRange(new[] { "-c:v", "copy" });
        else if (recording == CaptureRecording.LosslessCpu)
        {
            string pixel = LosslessPixel(stream.PixelFormat) ?? throw new NotSupportedException($"Lossless recording cannot preserve {stream.PixelFormat}. Choose direct copy.");
            if (stream.PixelFormat.StartsWith("yuvj", StringComparison.Ordinal))
                args.AddRange(new[] { "-vf", "scale=in_range=full:out_range=full", "-color_range", "pc" });
            args.AddRange(new[] { "-c:v", "ffv1", "-level", "3", "-g", "1", "-slicecrc", "1", "-pix_fmt", pixel });
        }
        else
        {
            if (codec is not (VideoCodec.H264 or VideoCodec.H265)) throw new NotSupportedException("Capture encoding supports H.264 and H.265.");
            if (quality is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(quality));
            if (recording == CaptureRecording.LossyHardware && hardware == HardwareAccelerationMode.None)
                throw new NotSupportedException("No hardware encoder selected.");
            bool gpu = recording == CaptureRecording.LossyHardware;
            var enc = QualityConfigService.GetEncodingSettings(quality, codec, gpu);
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
            if (!gpu)
                args.AddRange(new[] { "-c:v", codec == VideoCodec.H264 ? "libx264" : "libx265", "-preset", "veryfast", "-crf", $"{enc.Crf}" });
            else if (hardware == HardwareAccelerationMode.AppleVideoToolbox)
                args.AddRange(new[] { "-c:v", codec == VideoCodec.H264 ? "h264_videotoolbox" : "hevc_videotoolbox",
                    "-q:v", $"{enc.VtQuality}", "-b:v", "0", "-realtime", "1", "-allow_sw", "0" });
            else
                args.AddRange(new[] { "-c:v", codec == VideoCodec.H264 ? "h264_nvenc" : "hevc_nvenc",
                    "-preset", "p4", "-tune", "ll", "-rc", "vbr", "-cq", $"{enc.Cq}", "-b:v", "0", "-rc-lookahead", "0" });
            if (codec == VideoCodec.H265) args.AddRange(new[] { "-tag:v", "hvc1" });
            // Fragmented MP4 writes playable fragments throughout capture instead of
            // waiting for a final moov atom after a potentially long recording.
            args.AddRange(new[] { "-movflags", "+frag_keyframe+empty_moov+default_base_moof" });
        }
        args.AddRange(new[] { "-f", Container(recording, stream) });
        return args;
    }

    internal static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args, bool redirect)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = redirect,
            RedirectStandardOutput = redirect, RedirectStandardError = redirect };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    // Drain both redirected streams concurrently: a full pipe can deadlock FFmpeg.
    internal static Task<string> ReadOutput(StreamReader reader) => reader.ReadToEndAsync();

    internal static async Task ReadLines(StreamReader reader, Action<string> consume)
    {
        while (await reader.ReadLineAsync() is { } line) consume(line);
    }

    internal static async Task<(int Code, string Output, string Error)> Probe(string exe, IEnumerable<string> args, int seconds = 30)
    {
        using var proc = Process.Start(StartInfo(exe, args, true)) ?? throw new IOException($"Could not start {exe}.");
        var output = ReadOutput(proc.StandardOutput);
        var error = ReadOutput(proc.StandardError);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await proc.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync();
            await Task.WhenAll(output, error);
            throw new IOException($"Capture probe timed out. Check the video signal and camera permission, then retry.\n{await error}");
        }
        return (proc.ExitCode, await output, await error);
    }
}
