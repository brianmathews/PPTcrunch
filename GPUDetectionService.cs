using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PPTcrunch;

public class GPUDetectionService
{
    public class GPUInfo
    {
        public bool HasNvidiaGPU { get; set; }
        public string GPUModel { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public bool SupportsNVENC { get; set; }
        public bool SupportsH264 { get; set; }
        public bool SupportsH265 { get; set; }
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
            gpuInfo.IsAppleSilicon = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
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

        // Check FFmpeg encoder support
        var encoderInfo = await CheckFFmpegEncoderSupportAsync();
        gpuInfo.SupportsNVENC = encoderInfo.HasNvenc;
        gpuInfo.SupportsVideoToolbox = encoderInfo.HasVideoToolbox;

        if (encoderInfo.HasNvenc)
        {
            gpuInfo.SupportsH264 = encoderInfo.HasNvencH264;
            gpuInfo.SupportsH265 = encoderInfo.HasNvencH265;
            gpuInfo.HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc;
        }
        else if (encoderInfo.HasVideoToolbox)
        {
            gpuInfo.SupportsVideoToolboxH264 = encoderInfo.HasVideoToolboxH264;
            gpuInfo.SupportsVideoToolboxH265 = encoderInfo.HasVideoToolboxH265;
            gpuInfo.SupportsH264 = encoderInfo.HasVideoToolboxH264;
            gpuInfo.SupportsH265 = encoderInfo.HasVideoToolboxH265;
            gpuInfo.HardwareAcceleration = HardwareAccelerationMode.AppleVideoToolbox;
            gpuInfo.IsSupported = true;
            gpuInfo.CompatibilityProfile = gpuInfo.IsAppleSilicon ? "Apple Silicon VideoToolbox" : "Apple VideoToolbox";

            if (string.IsNullOrWhiteSpace(gpuInfo.GPUModel))
            {
                gpuInfo.GPUModel = gpuInfo.IsAppleSilicon ? "Apple Silicon" : "Apple GPU";
            }
        }

        // Use the comprehensive GPU capability detection from QualityConfigService
        if (gpuInfo.HasNvidiaGPU && !string.IsNullOrEmpty(gpuInfo.GPUModel))
        {
            var capabilities = QualityConfigService.GetGPUCapabilities(gpuInfo.GPUModel);
            if (capabilities != null)
            {
                gpuInfo.IsSupported = true;
                gpuInfo.SupportsH265_10bit = capabilities.H265_10bit;
                gpuInfo.MaxReferenceFrames = capabilities.MaxRefs;
                gpuInfo.CompatibilityProfile = DetermineGenerationName(gpuInfo.GPUModel);

                if (gpuInfo.SupportsNVENC)
                {
                    gpuInfo.HardwareAcceleration = HardwareAccelerationMode.NvidiaNvenc;
                }

                // Ensure codec support is at least what the capability system reports
                // (FFmpeg detection might fail even if GPU supports it)
                if (capabilities.SupportedCodecs.Contains("H264"))
                    gpuInfo.SupportsH264 = true;
                if (capabilities.SupportedCodecs.Contains("H265"))
                    gpuInfo.SupportsH265 = true;
            }
            else
            {
                // GPU not supported by our encoding system (below GTX 1060)
                gpuInfo.IsSupported = false;
                gpuInfo.CompatibilityProfile = "Unsupported";
            }
        }

        return gpuInfo;
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
        public bool HasNvenc { get; init; }
        public bool HasNvencH264 { get; init; }
        public bool HasNvencH265 { get; init; }
        public bool HasVideoToolbox { get; init; }
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

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                bool hasH264Nvenc = output.Contains("h264_nvenc");
                bool hasH265Nvenc = output.Contains("hevc_nvenc") || output.Contains("h265_nvenc");
                bool hasH264Vt = output.Contains("h264_videotoolbox");
                bool hasH265Vt = output.Contains("hevc_videotoolbox") || output.Contains("h265_videotoolbox");

                return new EncoderSupport
                {
                    HasNvenc = hasH264Nvenc || hasH265Nvenc,
                    HasNvencH264 = hasH264Nvenc,
                    HasNvencH265 = hasH265Nvenc,
                    HasVideoToolbox = hasH264Vt || hasH265Vt,
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