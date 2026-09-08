using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PPTcrunch;

public class GPUDetectionService
{
    public class GPUInfo
    {
        public HashSet<string> CpuEncoders { get; set; } = new(StringComparer.Ordinal);
        public bool HasNvidiaGPU { get; set; }
        public string GPUModel { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public bool SupportsNVENC { get; set; }
        public bool SupportsH264 { get; set; }
        public bool SupportsH265 { get; set; }
        public bool SupportsAV1 { get; set; }
        public bool SupportsVideoToolbox { get; set; }
        public bool SupportsVideoToolboxH264 { get; set; }
        public bool SupportsVideoToolboxH265 { get; set; }
        public bool SupportsH265_10bit { get; set; }
        public int MaxReferenceFrames { get; set; } = 2;
        public string CompatibilityProfile { get; set; } = "Default";
        public bool IsSupported { get; set; } // New field to indicate if GPU is supported for encoding
        public bool IsAppleSilicon { get; set; }
        public HardwareAccelerationMode HardwareAcceleration { get; set; } = HardwareAccelerationMode.None;
        public bool SupportsHardwareAcceleration => HardwareAcceleration != HardwareAccelerationMode.None;
    }

    public static async Task<GPUInfo> DetectGPUCapabilitiesAsync()
    {
        var gpuInfo = new GPUInfo();

        if (OperatingSystem.IsMacOS())
        {
            gpuInfo.IsAppleSilicon = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            gpuInfo.CompatibilityProfile = gpuInfo.IsAppleSilicon ? "Apple Silicon" : "macOS";
        }

        // Check if nvidia-smi is available first
        bool hasNvidiaSmi = await CheckNvidiaSmiAvailabilityAsync();
        if (hasNvidiaSmi)
        {
            var nvidiaInfo = await GetNvidiaGPUInfoAsync();
            gpuInfo.HasNvidiaGPU = nvidiaInfo.HasGPU;
            gpuInfo.GPUModel = nvidiaInfo.Model;
            gpuInfo.DriverVersion = nvidiaInfo.DriverVersion;
        }

        // Encoder listings describe the FFmpeg build, not usable hardware.
        // Probe the platform's encoder with our actual quality-mode arguments.
        var encoderInfo = await CheckFFmpegEncoderSupportAsync();
        gpuInfo.CpuEncoders = encoderInfo.CpuEncoders;
        var mode = PlatformHardware(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
        if (mode == HardwareAccelerationMode.NvidiaNvenc)
        {
            gpuInfo.SupportsH264 = encoderInfo.HasNvencH264 && await ProbeHardwareAsync(VideoCodec.H264, mode);
            gpuInfo.SupportsH265 = encoderInfo.HasNvencH265 && await ProbeHardwareAsync(VideoCodec.H265, mode);
            gpuInfo.SupportsAV1 = encoderInfo.HasNvencAV1 && await ProbeHardwareAsync(VideoCodec.AV1, mode);
            gpuInfo.SupportsNVENC = gpuInfo.SupportsH264 || gpuInfo.SupportsH265 || gpuInfo.SupportsAV1;
            if (gpuInfo.SupportsNVENC)
            {
                gpuInfo.HardwareAcceleration = mode;
                gpuInfo.HasNvidiaGPU = true;
                if (string.IsNullOrWhiteSpace(gpuInfo.GPUModel)) gpuInfo.GPUModel = "NVIDIA (verified by encoding)";
            }
            gpuInfo.CompatibilityProfile = DetermineGenerationName(gpuInfo.GPUModel);
            // Model heuristics are descriptive only; never override failed probes.
            var capabilities = QualityConfigService.GetGPUCapabilities(gpuInfo.GPUModel);
            gpuInfo.SupportsH265_10bit = capabilities?.H265_10bit ?? false;
            gpuInfo.MaxReferenceFrames = capabilities?.MaxRefs ?? 2;
        }
        else if (mode == HardwareAccelerationMode.AppleVideoToolbox)
        {
            gpuInfo.SupportsH264 = encoderInfo.HasVideoToolboxH264 && await ProbeHardwareAsync(VideoCodec.H264, mode);
            gpuInfo.SupportsH265 = encoderInfo.HasVideoToolboxH265 && await ProbeHardwareAsync(VideoCodec.H265, mode);
            gpuInfo.SupportsVideoToolboxH264 = gpuInfo.SupportsH264;
            gpuInfo.SupportsVideoToolboxH265 = gpuInfo.SupportsH265;
            gpuInfo.SupportsVideoToolbox = gpuInfo.SupportsH264 || gpuInfo.SupportsH265;
            if (gpuInfo.SupportsVideoToolbox) gpuInfo.HardwareAcceleration = mode;
            gpuInfo.GPUModel = gpuInfo.IsAppleSilicon ? "Apple Silicon" : "Mac hardware";
            gpuInfo.CompatibilityProfile = "Apple VideoToolbox quality mode";
        }
        gpuInfo.IsSupported = gpuInfo.SupportsHardwareAcceleration;
        return gpuInfo;
    }

    internal static HardwareAccelerationMode PlatformHardware(bool windows, bool macOS) =>
        macOS ? HardwareAccelerationMode.AppleVideoToolbox : windows ? HardwareAccelerationMode.NvidiaNvenc : HardwareAccelerationMode.None;

    internal static Task<bool> ProbeAv1NvencAsync() => ProbeHardwareAsync(VideoCodec.AV1, HardwareAccelerationMode.NvidiaNvenc);

    internal static async Task<bool> ProbeHardwareAsync(VideoCodec codec, HardwareAccelerationMode mode)
    {
        if (mode == HardwareAccelerationMode.None || codec == VideoCodec.VP9 ||
            (codec == VideoCodec.AV1 && mode == HardwareAccelerationMode.AppleVideoToolbox)) return false;
        try
        {
            string path = await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync() ?? "ffmpeg";
            var settings = new UserSettings { Codec = codec };
            var options = EncodingArguments.Build(settings, mode, videoOnly: true);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-hide_banner -nostdin -f lavfi -i color=size=640x360:rate=24:duration=2 " + string.Join(" ", options) + " -",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            });
            if (process == null) return false;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                return false;
            }
            await Task.WhenAll(stdout, stderr);
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<bool> CheckNvidiaSmiAvailabilityAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(bool HasGPU, string Model, string DriverVersion)> GetNvidiaGPUInfoAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,driver_version --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return (false, string.Empty, string.Empty);

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var lines = output.Trim().Split('\n');
                if (lines.Length > 0)
                {
                    var parts = lines[0].Split(',');
                    if (parts.Length >= 2)
                    {
                        return (true, parts[0].Trim(), parts[1].Trim());
                    }
                }
            }
        }
        catch
        {
            // Fall back to alternative detection methods if nvidia-smi fails
        }

        return (false, string.Empty, string.Empty);
    }

    private class EncoderSupport
    {
        public HashSet<string> CpuEncoders { get; init; } = new(StringComparer.Ordinal);
        public bool HasNvencH264 { get; init; }
        public bool HasNvencH265 { get; init; }
        public bool HasNvencAV1 { get; init; }
        public bool HasVideoToolboxH264 { get; init; }
        public bool HasVideoToolboxH265 { get; init; }
    }

    private static async Task<EncoderSupport> CheckFFmpegEncoderSupportAsync()
    {
        try
        {
            // Get the embedded FFmpeg path
            string? ffmpegPath = await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath))
            {
                // Fallback to external ffmpeg if embedded is not available
                ffmpegPath = "ffmpeg";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -encoders",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return new EncoderSupport();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); return new EncoderSupport(); }
            string output = await stdout;
            await stderr;

            if (process.ExitCode == 0)
            {
                bool hasH264Nvenc = output.Contains("h264_nvenc");
                bool hasH265Nvenc = output.Contains("hevc_nvenc") || output.Contains("h265_nvenc");
                bool hasH264Vt = output.Contains("h264_videotoolbox");
                bool hasH265Vt = output.Contains("hevc_videotoolbox") || output.Contains("h265_videotoolbox");

                return new EncoderSupport
                {
                    CpuEncoders = new HashSet<string>(new[] { "libx264", "libx265", "libvpx-vp9", "libsvtav1" }.Where(name => Regex.IsMatch(output, @"(?m)^\s*V\S*\s+" + Regex.Escape(name) + @"\s")), StringComparer.Ordinal),
                    HasNvencH264 = hasH264Nvenc,
                    HasNvencH265 = hasH265Nvenc,
                    HasNvencAV1 = output.Contains("av1_nvenc"),
                    HasVideoToolboxH264 = hasH264Vt,
                    HasVideoToolboxH265 = hasH265Vt
                };
            }
        }
        catch
        {
            // FFmpeg not available or error
        }

        return new EncoderSupport();
    }

    /// <summary>
    /// Determines a human-readable generation name for the GPU based on its model
    /// </summary>
    private static string DetermineGenerationName(string gpuModel)
    {
        string model = gpuModel.ToUpperInvariant().Replace(" ", "");

        // Extract model number using same logic as QualityConfigService
        var patterns = new[]
        {
            @"GTX(\d{3,4})",           // GTX 1060, GTX 1660, etc.
            @"RTX(\d{3,4})",           // RTX 2060, RTX 3070, RTX 4080, RTX 5090, etc.
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(model, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int modelNumber))
            {
                int generation = modelNumber / 100;

                return generation switch
                {
                    10 => $"GTX {modelNumber} (Pascal)",
                    16 => $"GTX {modelNumber} (Turing)",
                    20 => $"RTX {modelNumber} (Turing)",
                    30 => $"RTX {modelNumber} (Ampere)",
                    40 => $"RTX {modelNumber} (Ada Lovelace)",
                    50 => $"RTX {modelNumber} (Blackwell)",
                    >= 60 => $"RTX {modelNumber} (Future Gen)",
                    _ => $"GPU {modelNumber} (Unknown Gen)"
                };
            }
        }

        // Handle special cases for Titan and professional cards
        if (model.Contains("TITAN"))
        {
            if (model.Contains("RTX"))
                return "Titan RTX (Turing)";
            if (model.Contains("V"))
                return "Titan V (Volta)";
            if (model.Contains("X"))
                return "Titan X (Pascal)";

            return "Titan (Pascal+)";
        }

        // Professional cards
        if (model.Contains("QUADRO") || model.Contains("RTX") && (model.Contains("A") || model.Contains("PRO")))
            return "Professional (Workstation)";

        return "Unknown Generation";
    }

    public static async Task<bool> CheckFFprobeAvailabilityAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}