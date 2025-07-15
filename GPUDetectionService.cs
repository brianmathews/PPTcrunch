using System.Diagnostics;
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
        public bool SupportsH265_10bit { get; set; }
        public int MaxReferenceFrames { get; set; } = 2;
        public string CompatibilityProfile { get; set; } = "Default";
    }

    public static async Task<GPUInfo> DetectGPUCapabilitiesAsync()
    {
        var gpuInfo = new GPUInfo();

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
        gpuInfo.SupportsNVENC = encoderInfo.HasNVENC;
        gpuInfo.SupportsH264 = encoderInfo.HasH264;
        gpuInfo.SupportsH265 = encoderInfo.HasH265;

        // Determine compatibility profile based on GPU model
        if (gpuInfo.HasNvidiaGPU && !string.IsNullOrEmpty(gpuInfo.GPUModel))
        {
            gpuInfo.CompatibilityProfile = DetermineCompatibilityProfile(gpuInfo.GPUModel);
            SetCapabilitiesFromProfile(gpuInfo);
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

    private static async Task<(bool HasNVENC, bool HasH264, bool HasH265)> CheckFFmpegEncoderSupportAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-hide_banner -encoders",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return (false, false, false);

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                bool hasH264Nvenc = output.Contains("h264_nvenc");
                bool hasH265Nvenc = output.Contains("hevc_nvenc") || output.Contains("h265_nvenc");
                bool hasNvenc = hasH264Nvenc || hasH265Nvenc;

                return (hasNvenc, hasH264Nvenc, hasH265Nvenc);
            }
        }
        catch
        {
            // FFmpeg not available or error
        }

        return (false, false, false);
    }

    private static string DetermineCompatibilityProfile(string gpuModel)
    {
        string model = gpuModel.ToUpperInvariant();

        // RTX 30xx series
        if (model.Contains("RTX 30") || model.Contains("RTX 31") || model.Contains("RTX 32") ||
            model.Contains("RTX 33") || model.Contains("RTX 34") || model.Contains("RTX 35") ||
            model.Contains("RTX 36") || model.Contains("RTX 37") || model.Contains("RTX 38") ||
            model.Contains("RTX 39"))
        {
            return "RTX_30xx";
        }

        // RTX 20xx series
        if (model.Contains("RTX 20") || model.Contains("RTX 21") || model.Contains("RTX 22") ||
            model.Contains("RTX 23") || model.Contains("RTX 24") || model.Contains("RTX 25") ||
            model.Contains("RTX 26") || model.Contains("RTX 27") || model.Contains("RTX 28") ||
            model.Contains("RTX 29"))
        {
            return "RTX_20xx";
        }

        // GTX 1660 series
        if (model.Contains("GTX 1660"))
        {
            return "GTX_1660";
        }

        // GTX 1060 series
        if (model.Contains("GTX 1060"))
        {
            return "GTX_1060";
        }

        // GTX 1050, 1070, 1080 series (similar capabilities to 1060)
        if (model.Contains("GTX 10"))
        {
            return "GTX_1060";
        }

        return "Default";
    }

    private static void SetCapabilitiesFromProfile(GPUInfo gpuInfo)
    {
        switch (gpuInfo.CompatibilityProfile)
        {
            case "RTX_30xx":
                gpuInfo.SupportsH265_10bit = true;
                gpuInfo.MaxReferenceFrames = 4;
                break;
            case "RTX_20xx":
                gpuInfo.SupportsH265_10bit = true;
                gpuInfo.MaxReferenceFrames = 4;
                break;
            case "GTX_1660":
                gpuInfo.SupportsH265_10bit = false;
                gpuInfo.MaxReferenceFrames = 3;
                break;
            case "GTX_1060":
                gpuInfo.SupportsH265_10bit = false;
                gpuInfo.MaxReferenceFrames = 3;
                break;
            default:
                gpuInfo.SupportsH265_10bit = false;
                gpuInfo.MaxReferenceFrames = 2;
                break;
        }
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