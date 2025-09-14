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
        Console.WriteLine("\n\nUSB Capture - Direct to Disk");
        Console.WriteLine("============================\n\n");

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

        // 3) Prompt for output filename
        string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string suggested = $"{ts}_{selectedMode.Width}x{selectedMode.Height}@{selectedMode.Fps}.mkv";
        Console.WriteLine($"Suggested filename: {suggested}");
        Console.Write("Output filename (ENTER to accept): ");
        string? outName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(outName)) outName = suggested;
        if (string.IsNullOrWhiteSpace(Path.GetExtension(outName))) outName += ".mkv";

        // 4) Build ffmpeg command
        var inputArgs = new StringBuilder();
        inputArgs.Append("-hide_banner -f dshow -rtbufsize 512M ");
        inputArgs.Append($"-video_size {selectedMode.Width}x{selectedMode.Height} ");
        inputArgs.Append($"-framerate {selectedMode.Fps} ");
        inputArgs.Append($"-vcodec {selectedMode.Format} ");
        inputArgs.Append($"-i video=\"{selectedDevice}\" ");

        string vopts = string.Equals(selectedMode.Format, "mjpeg", StringComparison.OrdinalIgnoreCase)
            ? "-c:v copy -fps_mode passthrough"
            : "-pix_fmt yuv422p -c:v ffv1 -level 3 -g 1";

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


