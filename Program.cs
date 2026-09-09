

namespace PPTcrunch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--version")
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
            Console.WriteLine($"pptcrunch {version}");
            return 0;
        }

        if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            ShowUsage();
            return 0;
        }

        if (args.Length == 1 && string.Equals(args[0], "capture", StringComparison.OrdinalIgnoreCase))
        {
            return await CaptureMode.RunAsync();
        }

        if (args.Length != 1)
        {
            ShowUsage();
            return 1;
        }

        try
        {
            // Expand wildcards and get list of files to process
            var filesToProcess = ExpandFilePattern(args[0]);

            if (filesToProcess.Count == 0)
            {
                Console.WriteLine($"Error: No files found matching pattern '{args[0]}'");
                return 1;
            }

            // Categorize files by type
            var pptxFiles = new List<string>();
            var videoFiles = new List<string>();
            var unsupportedFiles = new List<string>();

            foreach (string file in filesToProcess)
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".pptx")
                {
                    pptxFiles.Add(file);
                }
                else if (IsVideoFile(extension))
                {
                    videoFiles.Add(file);
                }
                else
                {
                    unsupportedFiles.Add(file);
                }
            }

            // Report what we found
            Console.WriteLine($"Found {filesToProcess.Count} file(s) to process:");
            if (pptxFiles.Count > 0)
                Console.WriteLine($"  - {pptxFiles.Count} PowerPoint file(s)");
            if (videoFiles.Count > 0)
                Console.WriteLine($"  - {videoFiles.Count} video file(s)");
            if (unsupportedFiles.Count > 0)
                Console.WriteLine($"  - {unsupportedFiles.Count} unsupported file(s) (will be skipped)");
            Console.WriteLine();

            if (pptxFiles.Count == 0 && videoFiles.Count == 0)
            {
                Console.WriteLine("Error: No supported files found. Supported formats: .pptx, .mp4, .mov, .avi, .mkv, .webm, .wmv, .flv, .m4v, .nut");
                return 1;
            }

            // Perform system checks
            Console.WriteLine("Checking system requirements...");
            Console.WriteLine("=".PadRight(50, '='));

            var systemCheckResult = await PerformSystemChecks();
            if (!systemCheckResult.CanProceed)
            {
                return 1;
            }

            // Collect user settings
            var settings = await CollectUserSettingsAsync(systemCheckResult, allowWebCodecs: pptxFiles.Count == 0);

            // Process files
            int successCount = 0;
            int totalFiles = pptxFiles.Count + videoFiles.Count;

            // Process PPTX files
            if (pptxFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Processing PowerPoint files:");
                Console.WriteLine("=".PadRight(50, '='));

                var pptxProcessor = new PPTXVideoProcessor();
                foreach (string pptxFile in pptxFiles)
                {
                    try
                    {
                        await pptxProcessor.ProcessAsync(pptxFile, settings);
                        Console.WriteLine($"✓ Successfully processed: {Path.GetFileName(pptxFile)}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✗ Failed to process {Path.GetFileName(pptxFile)}: {ex.Message}");
                    }
                    Console.WriteLine();
                }
            }

            // Process video files
            if (videoFiles.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Processing video files:");
                Console.WriteLine("=".PadRight(50, '='));

                var videoProcessor = new VideoProcessor();
                foreach (string videoFile in videoFiles)
                {
                    try
                    {
                        bool success = await videoProcessor.ProcessVideoFileAsync(videoFile, settings);
                        if (success)
                        {
                            Console.WriteLine($"✓ Successfully processed: {Path.GetFileName(videoFile)}");
                            successCount++;
                        }
                        else
                        {
                            Console.WriteLine($"✗ Failed to process: {Path.GetFileName(videoFile)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✗ Failed to process {Path.GetFileName(videoFile)}: {ex.Message}");
                    }
                    Console.WriteLine();
                }
            }

            // Summary
            Console.WriteLine("=".PadRight(50, '='));
            Console.WriteLine($"Processing complete: {successCount}/{totalFiles} files processed successfully");

            if (unsupportedFiles.Count > 0)
            {
                Console.WriteLine($"Skipped {unsupportedFiles.Count} unsupported file(s):");
                foreach (string file in unsupportedFiles)
                {
                    Console.WriteLine($"  - {Path.GetFileName(file)}");
                }
            }

            return successCount > 0 ? 0 : 1;
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

        // Check embedded FFmpeg availability
        Console.WriteLine("Initializing embedded FFmpeg...");
        bool ffmpegAvailable = await EmbeddedFFmpegRunner.CheckFFmpegAvailabilityAsync();
        if (ffmpegAvailable)
        {
            Console.WriteLine("  ✓ Embedded FFmpeg is available");
        }
        else
        {
            Console.WriteLine("  ✗ Failed to initialize embedded FFmpeg");
            Console.WriteLine("    This should not happen with the embedded version.");
            result.CanProceed = false;
        }

        if (!result.CanProceed)
        {
            return result;
        }

        // Use GPUDetectionService for comprehensive GPU detection
        Console.WriteLine("Detecting GPU capabilities...");
        result.GPUInfo = await GPUDetectionService.DetectGPUCapabilitiesAsync();

        switch (result.GPUInfo.HardwareAcceleration)
        {
            case HardwareAccelerationMode.NvidiaNvenc:
                Console.WriteLine($"  ✓ NVIDIA GPU detected: {result.GPUInfo.GPUModel}");
                if (!string.IsNullOrEmpty(result.GPUInfo.DriverVersion))
                {
                    Console.WriteLine($"    Driver version: {result.GPUInfo.DriverVersion}");
                }
                Console.WriteLine($"    Generation: {result.GPUInfo.CompatibilityProfile}");
                Console.WriteLine("    ✓ NVENC hardware acceleration available");
                if (result.GPUInfo.SupportsH264)
                    Console.WriteLine("      ✓ H.264 encoding supported");
                if (result.GPUInfo.SupportsH265)
                    Console.WriteLine("      ✓ H.265 encoding supported");
                Console.WriteLine(result.GPUInfo.SupportsAV1 ? "      AV1 encoding verified" : "      AV1 encoding uses CPU on this GPU");
                break;
            case HardwareAccelerationMode.AppleVideoToolbox:
                Console.WriteLine("  ✓ Apple VideoToolbox hardware acceleration detected");
                Console.WriteLine($"    Platform: {(result.GPUInfo.IsAppleSilicon ? "Apple silicon (arm64)" : "Intel macOS")}");
                Console.WriteLine($"    VideoToolbox profile: {result.GPUInfo.CompatibilityProfile}");
                if (result.GPUInfo.SupportsH264)
                    Console.WriteLine("      ✓ H.264 encoding supported");
                if (result.GPUInfo.SupportsH265)
                    Console.WriteLine("      ✓ H.265 encoding supported");
                else
                    Console.WriteLine("      ⚠ H.265 encoding not reported by this FFmpeg build");
                break;
            default:
                if (result.GPUInfo.HasNvidiaGPU)
                {
                    Console.WriteLine($"  ⚠ NVIDIA GPU detected ({result.GPUInfo.GPUModel}) but NVENC is unavailable");
                    Console.WriteLine("    This could be due to:");
                    Console.WriteLine("    - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
                    Console.WriteLine("    - Outdated GPU drivers");
                    Console.WriteLine("    - FFmpeg build missing NVENC support");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Console.WriteLine("  ⚠ Apple VideoToolbox hardware acceleration not available");
                    Console.WriteLine("    Ensure you're using the bundled FFmpeg build and macOS allows VideoToolbox access");
                }
                else
                {
                    Console.WriteLine("  ⚠ No supported hardware encoder detected");
                    Console.WriteLine("    GPU acceleration requires NVIDIA NVENC or Apple VideoToolbox");
                }
                break;
        }

        if (!result.GPUInfo.SupportsHardwareAcceleration)
        {
            Console.WriteLine("  ⚠ Hardware acceleration not available - will use CPU encoding");
        }

        result.CanProceed = true;
        Console.WriteLine();
        return result;
    }

    private static Task<UserSettings> CollectUserSettingsAsync(SystemCheckResult systemCheck, bool allowWebCodecs)
    {
        var settings = new UserSettings();

        Console.WriteLine("Video Compression Settings");
        Console.WriteLine("==========================");

        // GPU acceleration preference
        if (systemCheck.GPUInfo.SupportsHardwareAcceleration)
        {
            Console.WriteLine();
            string hardwarePrompt = systemCheck.GPUInfo.HardwareAcceleration switch
            {
                HardwareAccelerationMode.NvidiaNvenc => "Use NVIDIA NVENC hardware acceleration for faster encoding?",
                HardwareAccelerationMode.AppleVideoToolbox => "Use Apple VideoToolbox hardware acceleration for faster encoding?",
                _ => "Use hardware acceleration for faster encoding?"
            };

            Console.WriteLine("CPU encoding is recommended for smaller files at a given quality; hardware encoding is faster.");
            Console.Write($"{hardwarePrompt} (y/N, default: N): ");
            string? gpuInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            settings.UseGPUAcceleration = gpuInput == "y" || gpuInput == "yes";
            settings.HardwareAcceleration = settings.UseGPUAcceleration ? systemCheck.GPUInfo.HardwareAcceleration : HardwareAccelerationMode.None;
        }
        else
        {
            settings.UseGPUAcceleration = false;
            settings.HardwareAcceleration = HardwareAccelerationMode.None;
            Console.WriteLine("Hardware acceleration not available - using CPU encoding");
        }

        Console.WriteLine();
        Console.WriteLine("Video codec options:");
        Console.WriteLine("  1. H.264 / MP4 (broadest compatibility, including older devices)");
        Console.WriteLine("  2. H.265 / MP4 (smaller files, requires HEVC playback support)");
        if (allowWebCodecs)
        {
            Console.WriteLine("  3. WebM / VP9 (final website videos, two-pass CPU encoding)");
            Console.WriteLine("  4. AV1 / MP4 (efficient website video; CPU or supported NVIDIA GPU)");
        }
        else
            Console.WriteLine("  WebM and AV1 are available for standalone videos; PowerPoint batches use H.264 or H.265.");

        while (true)
        {
            Console.Write(allowWebCodecs ? "Enter your choice (1-4, default: 2): " : "Enter your choice (1 or 2, default: 2): ");
            string? codecInput = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(codecInput)) break;
            if (int.TryParse(codecInput, out int choice) && choice >= 1 && choice <= (allowWebCodecs ? 4 : 2))
            {
                settings.Codec = (VideoCodec)choice;
                break;
            }
            Console.WriteLine("Please select one of the listed codecs.");
        }
        if (settings.Codec == VideoCodec.VP9)
        {
            settings.UseGPUAcceleration = false;
            settings.HardwareAcceleration = HardwareAccelerationMode.None;
            Console.WriteLine("VP9 uses CPU encoding; NVENC and VideoToolbox do not encode VP9.");
            Console.WriteLine("Two passes: analyze the video, then encode at the selected quality for efficient web delivery.");
            Console.WriteLine("WebM on iPhone/iPad requires iOS/iPadOS 17.4 or later. Use H.264 for older devices.");
            Console.WriteLine("Two passes are recommended for final website videos. One pass can be smaller for mostly static clips.");
            Console.Write("Use two-pass WebM encoding? (Y/n, default: Y): ");
            string? passInput = Console.ReadLine()?.Trim().ToLowerInvariant();
            settings.UseVp9TwoPass = passInput != "n" && passInput != "no";
        }
        else if (settings.UseGPUAcceleration &&
                 !(settings.Codec switch { VideoCodec.H264 => systemCheck.GPUInfo.SupportsH264, VideoCodec.H265 => systemCheck.GPUInfo.SupportsH265, VideoCodec.AV1 => systemCheck.GPUInfo.SupportsAV1, _ => false }))
        {
            Console.WriteLine("The selected codec will use CPU encoding because it is unavailable on this hardware.");
            settings.UseGPUAcceleration = false;
            settings.HardwareAcceleration = HardwareAccelerationMode.None;
        }
        if (settings.Codec == VideoCodec.AV1)
        {
            Console.WriteLine("AV1 uses MP4 with 10-bit Main-profile video and AAC audio for web delivery.");
            Console.WriteLine("Safari AV1 playback requires an AV1-capable device (for example, iPhone 15 Pro or an M3 Mac).");
            Console.WriteLine("macOS uses CPU AV1 encoding. CPU encoding favors quality and small files over speed.");
        }
        if (!systemCheck.GPUInfo.CpuEncoders.Contains(settings.GetCpuCodecName()))
        {
            if (!settings.UseGPUAcceleration)
                throw new NotSupportedException($"This FFmpeg build did not report the required encoder {settings.GetCpuCodecName()}. Install a full native FFmpeg build containing that encoder.");
            Console.WriteLine($"CPU fallback is unavailable: this FFmpeg build lacks {settings.GetCpuCodecName()}.");
        }
        // Quality level
        Console.WriteLine();
        Console.WriteLine("Quality level options:");
        var config = QualityConfigService.GetConfig();
        foreach (var kvp in config.QualityLevels.OrderBy(x => int.Parse(x.Key)))
        {
            Console.WriteLine($"  {kvp.Key}. {kvp.Value.Name}");
        }
        Console.Write("Enter your choice (0-4, default: 2): ");
        string? qualityInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(qualityInput) && int.TryParse(qualityInput, out int qualityLevel) && qualityLevel >= 0 && qualityLevel <= 4)
        {
            settings.QualityLevel = qualityLevel;
        }

        Console.WriteLine();
        Console.WriteLine("Halve even integer frame rates of 48 FPS or higher (48→24, 50→25, 60→30, 120→60).");
        Console.WriteLine("Lower, odd and fractional rates are preserved.");
        Console.Write("Enable frame-rate reduction? (y/N, default: N): ");
        string? fpsInput = Console.ReadLine()?.Trim().ToLowerInvariant();
        settings.ReduceHighFrameRates = fpsInput == "y" || fpsInput == "yes";

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
        Console.WriteLine($"  Hardware acceleration: {(settings.UseGPUAcceleration ? $"Yes ({GetHardwareDescription(settings.HardwareAcceleration)})" : "No")}");
        Console.WriteLine($"  Video codec: {settings.GetCodecDisplayName()}");
        if (settings.Codec == VideoCodec.VP9)
            Console.WriteLine($"  WebM encoding: {(settings.UseVp9TwoPass ? "Two passes" : "One pass")}");
        Console.WriteLine($"  Quality level: {settings.GetQualityLevelDisplayName()}");
        Console.WriteLine($"  Frame rate: {(settings.ReduceHighFrameRates ? "Halve even integer rates >=48 FPS; preserve all others" : "Preserve original")}");
        Console.WriteLine($"  Maximum width: {(settings.MaxWidth == int.MaxValue ? "No limit" : $"{settings.MaxWidth} pixels")}");
        Console.WriteLine();

        return Task.FromResult(settings);
    }

    private static void ShowUsage()
    {
        Console.WriteLine("PPTcrunch - PowerPoint Video Compressor");
        Console.WriteLine("=========================================");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  PPTcrunch <file-pattern>");
        Console.WriteLine("  PPTcrunch capture");
        Console.WriteLine("  PPTcrunch --version                 # Print release version and build number");
        Console.WriteLine();
        Console.WriteLine("Supported file types:");
        Console.WriteLine("  - PowerPoint presentations: *.pptx");
        Console.WriteLine("  - Video files: *.mp4, *.mov, *.avi, *.mkv, *.webm, *.wmv, *.flv, *.m4v, *.nut");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  PPTcrunch presentation.pptx          # Process single PowerPoint file");
        Console.WriteLine("  PPTcrunch video.mp4                  # Process single video file");
        Console.WriteLine("  PPTcrunch *.pptx                     # Process all PowerPoint files");
        Console.WriteLine("  PPTcrunch *.mov                      # Process all .mov video files");
        Console.WriteLine("  PPTcrunch *.*                        # Process all supported files");
        Console.WriteLine("  PPTcrunch capture                    # Record from USB capture (direct or transcode)");
        Console.WriteLine();
        Console.WriteLine("Output:");
        Console.WriteLine("  - PPTX files: Creates new file with '-shrunk' suffix");
        Console.WriteLine("  - Video files: Creates new file with quality and codec suffix");
        Console.WriteLine("    Examples: video.mov → video-L2H264.mp4 or video-L2VP9.webm or video-L2AV1.mp4");
    }

    private static List<string> ExpandFilePattern(string pattern)
    {
        var files = new List<string>();

        try
        {
            // Check if pattern contains wildcards
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                // Get directory and search pattern
                string directory = Path.GetDirectoryName(pattern) ?? "";
                string searchPattern = Path.GetFileName(pattern);

                // If no directory specified, use current directory
                if (string.IsNullOrEmpty(directory))
                {
                    directory = Directory.GetCurrentDirectory();
                }

                // Expand wildcards
                if (Directory.Exists(directory))
                {
                    var matchingFiles = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly);
                    files.AddRange(matchingFiles);
                }
            }
            else
            {
                // No wildcards - single file
                if (File.Exists(pattern))
                {
                    files.Add(Path.GetFullPath(pattern));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error expanding file pattern '{pattern}': {ex.Message}");
        }

        return files;
    }

    private static bool IsVideoFile(string extension)
    {
        string[] videoExtensions = {
            ".mp4", ".mpeg4", ".mov", ".avi", ".mkv",
            ".webm", ".wmv", ".flv", ".m4v", ".mpg",
            ".mpeg", ".3gp", ".3g2", ".asf", ".ogv", ".nut"
        };

        return videoExtensions.Contains(extension.ToLowerInvariant());
    }

    private static string GetHardwareDescription(HardwareAccelerationMode mode)
    {
        return mode switch
        {
            HardwareAccelerationMode.NvidiaNvenc => "NVIDIA NVENC",
            HardwareAccelerationMode.AppleVideoToolbox => "Apple VideoToolbox",
            _ => "None"
        };
    }

    private class SystemCheckResult
    {
        public bool CanProceed { get; set; } = true;
        public GPUDetectionService.GPUInfo GPUInfo { get; set; } = new();
    }
}
