

namespace PPTcrunch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: PPTcrunch <pptx-file>");
            Console.WriteLine("Example: PPTcrunch presentation.pptx");
            return 1;
        }

        string pptxPath = args[0];

        if (!File.Exists(pptxPath))
        {
            Console.WriteLine($"Error: File '{pptxPath}' not found.");
            return 1;
        }

        if (!Path.GetExtension(pptxPath).Equals(".pptx", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Error: File must have .pptx extension.");
            return 1;
        }

        try
        {
            // Perform system checks
            Console.WriteLine("Checking system requirements...");
            Console.WriteLine("=".PadRight(50, '='));

            var systemCheckResult = await PerformSystemChecks();
            if (!systemCheckResult.CanProceed)
            {
                return 1;
            }

            // Collect user settings
            var settings = await CollectUserSettingsAsync(systemCheckResult);

            var processor = new PPTXVideoProcessor();
            await processor.ProcessAsync(pptxPath, settings);
            Console.WriteLine("Video compression completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return 1;
        }
    }

    private static async Task<SystemCheckResult> PerformSystemChecks()
    {
        var result = new SystemCheckResult();

        // Check FFmpeg availability
        Console.WriteLine("Checking FFmpeg availability...");
        bool ffmpegAvailable = await FFmpegRunner.CheckFFmpegAvailabilityAsync();
        if (ffmpegAvailable)
        {
            Console.WriteLine("  ✓ FFmpeg is available");
        }
        else
        {
            Console.WriteLine("  ✗ FFmpeg is not available or not in PATH");
            Console.WriteLine("    Please install FFmpeg and ensure it's accessible from the command line.");
            result.CanProceed = false;
        }

        // Check FFprobe availability
        Console.WriteLine("Checking FFprobe availability...");
        bool ffprobeAvailable = await GPUDetectionService.CheckFFprobeAvailabilityAsync();
        if (ffprobeAvailable)
        {
            Console.WriteLine("  ✓ FFprobe is available");
        }
        else
        {
            Console.WriteLine("  ✗ FFprobe is not available or not in PATH");
            Console.WriteLine("    Please install FFprobe (usually comes with FFmpeg).");
            result.CanProceed = false;
        }

        if (!result.CanProceed)
        {
            return result;
        }

        // Detect GPU capabilities
        Console.WriteLine("Detecting GPU capabilities...");
        result.GPUInfo = await GPUDetectionService.DetectGPUCapabilitiesAsync();

        if (result.GPUInfo.HasNvidiaGPU)
        {
            Console.WriteLine($"  ✓ NVIDIA GPU detected: {result.GPUInfo.GPUModel}");
            Console.WriteLine($"    Driver version: {result.GPUInfo.DriverVersion}");
            Console.WriteLine($"    Compatibility profile: {result.GPUInfo.CompatibilityProfile}");
        }
        else
        {
            Console.WriteLine("  ⚠ No NVIDIA GPU detected");
        }

        if (result.GPUInfo.SupportsNVENC)
        {
            Console.WriteLine("  ✓ NVENC hardware acceleration available");
            if (result.GPUInfo.SupportsH264)
            {
                Console.WriteLine("    ✓ H.264 NVENC supported");
            }
            if (result.GPUInfo.SupportsH265)
            {
                Console.WriteLine("    ✓ H.265 NVENC supported");
                if (!result.GPUInfo.SupportsH265_10bit)
                {
                    Console.WriteLine("      Note: 10-bit H.265 not supported on this GPU");
                }
            }
        }
        else
        {
            Console.WriteLine("  ⚠ GPU acceleration not available - will use CPU encoding");
        }

        result.CanProceed = true;
        Console.WriteLine();
        return result;
    }

    private static Task<UserSettings> CollectUserSettingsAsync(SystemCheckResult systemCheck)
    {
        var settings = new UserSettings();

        Console.WriteLine("Video Compression Settings");
        Console.WriteLine("==========================");

        // GPU acceleration preference
        if (systemCheck.GPUInfo.SupportsNVENC)
        {
            Console.WriteLine();
            Console.Write($"Use GPU acceleration for faster encoding? (Y/n, default: Y): ");
            string? gpuInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            settings.UseGPUAcceleration = string.IsNullOrEmpty(gpuInput) || gpuInput == "y" || gpuInput == "yes";
        }
        else
        {
            settings.UseGPUAcceleration = false;
            Console.WriteLine("GPU acceleration not available - using CPU encoding");
        }

        // Codec preference
        Console.WriteLine();
        Console.WriteLine("Video codec options:");
        Console.WriteLine("  1. H.264 (better compatibility, works on older systems)");
        Console.WriteLine("  2. H.265 (smaller files, better compression, newer standard)");

        if (settings.UseGPUAcceleration)
        {
            if (!systemCheck.GPUInfo.SupportsH265)
            {
                Console.WriteLine("     Note: Your GPU doesn't support H.265 encoding - H.264 will be used if selected");
            }
        }

        Console.Write("Enter your choice (1 or 2, default: 2): ");
        string? codecInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(codecInput) && int.TryParse(codecInput, out int codecChoice))
        {
            if (codecChoice == 1)
            {
                settings.Codec = VideoCodec.H264;
            }
            else if (codecChoice == 2)
            {
                // Check if H.265 is supported when using GPU
                if (settings.UseGPUAcceleration && !systemCheck.GPUInfo.SupportsH265)
                {
                    Console.WriteLine("Warning: H.265 not supported on your GPU. Falling back to H.264.");
                    settings.Codec = VideoCodec.H264;
                }
                else
                {
                    settings.Codec = VideoCodec.H265;
                }
            }
        }
        else
        {
            // Default to H.265 if supported, otherwise H.264
            if (settings.UseGPUAcceleration && !systemCheck.GPUInfo.SupportsH265)
            {
                settings.Codec = VideoCodec.H264;
            }
            else
            {
                settings.Codec = VideoCodec.H265;
            }
        }

        // Quality level
        Console.WriteLine();
        Console.WriteLine("Quality level options:");
        var config = QualityConfigService.GetConfig();
        foreach (var kvp in config.QualityLevels.OrderBy(x => int.Parse(x.Key)))
        {
            Console.WriteLine($"  {kvp.Key}. {kvp.Value.Name}");
        }
        Console.Write("Enter your choice (1-3, default: 2): ");
        string? qualityInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(qualityInput) && int.TryParse(qualityInput, out int qualityLevel) && qualityLevel >= 1 && qualityLevel <= 3)
        {
            settings.QualityLevel = qualityLevel;
        }

        // High resolution reduction
        Console.WriteLine();
        Console.Write($"Reduce high-resolution videos to maximum 1920 pixels wide (2K HD)? (Y/n, default: Y): ");
        string? resInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        settings.ReduceHighResTo1920 = string.IsNullOrEmpty(resInput) || resInput == "y" || resInput == "yes";

        if (settings.ReduceHighResTo1920)
        {
            settings.MaxWidth = 1920;
        }
        else
        {
            Console.Write("Enter maximum video width in pixels (default: no limit): ");
            string? widthInput = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(widthInput) && int.TryParse(widthInput, out int width) && width > 0)
            {
                settings.MaxWidth = width;
            }
            else
            {
                settings.MaxWidth = int.MaxValue; // No limit
            }
        }

        // Display selected settings
        Console.WriteLine();
        Console.WriteLine("Selected settings:");
        Console.WriteLine($"  GPU acceleration: {(settings.UseGPUAcceleration ? "Yes" : "No")}");
        Console.WriteLine($"  Video codec: {settings.GetCodecDisplayName()}");
        Console.WriteLine($"  Quality level: {settings.GetQualityLevelDisplayName()}");
        Console.WriteLine($"  Maximum width: {(settings.MaxWidth == int.MaxValue ? "No limit" : $"{settings.MaxWidth} pixels")}");
        Console.WriteLine();

        return Task.FromResult(settings);
    }

    private class SystemCheckResult
    {
        public bool CanProceed { get; set; } = true;
        public GPUDetectionService.GPUInfo GPUInfo { get; set; } = new();
    }
}
