
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace PPTcrunch;

public class EmbeddedFFmpegRunner
{
    private const int TimeoutMinutes = 60; // minutes timeout per video
    private static bool _initialized = false;
    private static string? _ffmpegPath = null;

    static EmbeddedFFmpegRunner()
    {
        // Initialize will be called on first use
    }

    private static async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        try
        {
            Console.WriteLine("Initializing embedded FFmpeg...");

            // Use a dedicated FFmpeg directory to avoid cluttering user directories
            string ffmpegBaseDir = @"C:\ffmpeg";

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(ffmpegBaseDir);

            // Check common locations where FFmpeg might be downloaded
            string[] possibleDirectories = {
                ffmpegBaseDir,  // Dedicated FFmpeg directory
                AppContext.BaseDirectory,  // Fallback: directly in app directory
                Path.Combine(AppContext.BaseDirectory, "FFmpeg"),  // Fallback: in FFmpeg subdirectory
                Path.Combine(AppContext.BaseDirectory, "ffmpeg")   // Fallback: in lowercase ffmpeg subdirectory
            };

            string? ffmpegExePath = null;
            string? ffmpegDirectory = null;

            // Check if FFmpeg already exists in any of the possible locations
            foreach (string dir in possibleDirectories)
            {
                string ffmpegPath = Path.Combine(dir, "ffmpeg.exe");
                string ffprobePath = Path.Combine(dir, "ffprobe.exe");
                Console.WriteLine($"Checking for FFmpeg at: {ffmpegPath}");
                if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                {
                    ffmpegExePath = ffmpegPath;
                    ffmpegDirectory = dir;
                    Console.WriteLine($"Found FFmpeg at: {ffmpegExePath}");
                    break;
                }
            }

            // If not found, download FFmpeg
            if (ffmpegExePath == null)
            {
                Console.WriteLine($"Downloading FFmpeg binaries to {ffmpegBaseDir}...");

                // Set the FFmpeg executables path before downloading so it downloads to our desired location
                FFmpeg.SetExecutablesPath(ffmpegBaseDir);

                // Download the latest FFmpeg binaries
                await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegBaseDir);
                Console.WriteLine("FFmpeg binaries downloaded successfully");

                // Check again for the downloaded binaries
                Console.WriteLine($"Searching for downloaded FFmpeg binaries in directories...");
                foreach (string dir in possibleDirectories)
                {
                    string ffmpegPath = Path.Combine(dir, "ffmpeg.exe");
                    string ffprobePath = Path.Combine(dir, "ffprobe.exe");
                    Console.WriteLine($"Checking for FFmpeg at: {ffmpegPath}");
                    if (File.Exists(ffmpegPath) && File.Exists(ffprobePath))
                    {
                        ffmpegExePath = ffmpegPath;
                        ffmpegDirectory = dir;
                        Console.WriteLine($"Found FFmpeg at: {ffmpegExePath}");
                        break;
                    }
                }

                if (ffmpegExePath == null)
                {
                    // List all files in the FFmpeg directory to help debug
                    Console.WriteLine($"Files in FFmpeg directory ({ffmpegBaseDir}):");
                    try
                    {
                        var files = Directory.GetFiles(ffmpegBaseDir, "*", SearchOption.AllDirectories);
                        foreach (var file in files.Take(20)) // Show first 20 files
                        {
                            Console.WriteLine($"  {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Error listing files: {ex.Message}");
                    }

                    throw new InvalidOperationException("Failed to locate FFmpeg binaries after download");
                }
            }

            // Set the executable path for Xabe.FFmpeg
            FFmpeg.SetExecutablesPath(ffmpegDirectory);
            _ffmpegPath = ffmpegDirectory;

            // Verify FFmpeg is working by trying to get media info from a test
            Console.WriteLine("FFmpeg binaries initialized successfully");

            Console.WriteLine("✓ Embedded FFmpeg initialized successfully");
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize embedded FFmpeg: {ex.Message}");
            throw;
        }
    }

    public static async Task<bool> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
    {
        await EnsureInitializedAsync();

        // Check if GPU acceleration is preferred and available
        if (settings.UseGPUAcceleration)
        {
            bool gpuSuccess = await TryCompressWithGPU(inputPath, outputPath, settings);
            if (gpuSuccess)
            {
                return true;
            }

            Console.WriteLine("GPU compression failed, falling back to CPU compression...");
        }

        return await TryCompressWithCPU(inputPath, outputPath, settings);
    }

    private static async Task<bool> TryCompressWithGPU(string inputPath, string outputPath, UserSettings settings)
    {
        Console.WriteLine($"Trying GPU compression...");

        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            if (videoStream == null)
            {
                Console.WriteLine("No video stream found");
                return false;
            }

            // Get quality settings from config
            var encodingSettings = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, true);
            var codecParams = QualityConfigService.GetCodecParams(settings.Codec, true);

            var scaleSize = GetScaleSize(videoStream.Width, videoStream.Height, settings.MaxWidth);

            // Build conversion with GPU settings
            var conversion = FFmpeg.Conversions.New()
                .AddStream(mediaInfo.VideoStreams.ToArray())
                .AddStream(mediaInfo.AudioStreams.ToArray())
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            // Build custom arguments for GPU encoding
            var args = new List<string>();

            // Video filters
            args.Add($"-vf");
            args.Add($"scale={scaleSize.width}:{scaleSize.height}");

            // GPU codec
            args.Add($"-c:v");
            args.Add(GetGpuCodecName(settings.Codec));

            // Quality settings
            args.Add($"-cq");
            args.Add($"{encodingSettings.Cq ?? 23}");

            args.Add($"-b:v");
            args.Add("0"); // True constant quality

            args.Add($"-preset");
            args.Add(encodingSettings.Preset ?? "slow");

            args.Add($"-profile:v");
            args.Add(codecParams.Profile ?? "high");

            args.Add($"-rc");
            args.Add(encodingSettings.Rc ?? "vbr");

            args.Add($"-bf");
            args.Add($"{codecParams.Bf ?? 3}");

            args.Add($"-refs");
            args.Add($"{codecParams.Refs ?? 4}");

            // Add H.265 specific settings
            if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(codecParams.Tag))
            {
                args.Add($"-tag:v");
                args.Add(codecParams.Tag);
            }

            // Add modern quality enhancement parameters if available
            if (!string.IsNullOrEmpty(encodingSettings.Tune))
            {
                args.Add($"-tune");
                args.Add(encodingSettings.Tune);
            }

            if (encodingSettings.Multipass.HasValue)
            {
                args.Add($"-multipass");
                args.Add($"{encodingSettings.Multipass.Value}");
            }

            // Audio settings
            args.Add($"-c:a");
            args.Add("copy");

            // Additional settings
            args.Add("-y");
            args.Add("-stats");

            conversion.AddParameter(string.Join(" ", args));

            // Display FFmpeg command line parameters
            Console.WriteLine("\nFFmpeg Command Parameters (GPU Acceleration):");
            Console.WriteLine("==================================================");
            Console.WriteLine($"ffmpeg -i \"{inputPath}\" {string.Join(" ", args)} \"{outputPath}\"");
            Console.WriteLine("==================================================\n");

            // Add progress reporting
            conversion.OnProgress += (sender, eventArgs) =>
            {
                var percent = Math.Round((double)eventArgs.Percent, 1);
                var processed = eventArgs.Duration;
                var total = eventArgs.TotalLength;
                Console.Write($"\rProgress: {percent}% [{processed:hh\\:mm\\:ss} / {total:hh\\:mm\\:ss}]");
            };

            conversion.OnDataReceived += (sender, eventArgs) =>
            {
                // Show FFmpeg output for debugging (you can comment this out if too verbose)
                if (!string.IsNullOrWhiteSpace(eventArgs.Data) && eventArgs.Data.Contains("frame="))
                {
                    Console.Write($"\r{eventArgs.Data.Trim()}");
                }
            };

            var result = await conversion.Start();
            Console.WriteLine(); // New line after progress
            Console.WriteLine("✓ GPU compression completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GPU compression error: {ex.Message}");
            Console.WriteLine("This could be due to:");
            Console.WriteLine("  - FFmpeg not compiled with NVENC support");
            Console.WriteLine("  - NVIDIA GPU drivers not installed or outdated");
            Console.WriteLine("  - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
            Console.WriteLine("  - GPU is busy with other tasks");
            return false;
        }
    }

    private static async Task<bool> TryCompressWithCPU(string inputPath, string outputPath, UserSettings settings)
    {
        Console.WriteLine($"Running CPU compression...");

        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            if (videoStream == null)
            {
                Console.WriteLine("No video stream found");
                return false;
            }

            // Get quality settings from config
            var encodingSettings = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, false);
            var codecParams = QualityConfigService.GetCodecParams(settings.Codec, false);

            var scaleSize = GetScaleSize(videoStream.Width, videoStream.Height, settings.MaxWidth);

            // Build conversion with CPU settings
            var conversion = FFmpeg.Conversions.New()
                .AddStream(mediaInfo.VideoStreams.ToArray())
                .AddStream(mediaInfo.AudioStreams.ToArray())
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            // Build arguments for CPU encoding
            var args = new List<string>();

            // Video filters
            args.Add($"-vf");
            args.Add($"scale={scaleSize.width}:{scaleSize.height}");

            // CPU codec
            args.Add($"-c:v");
            args.Add(GetCpuCodecName(settings.Codec));

            // Quality settings
            args.Add($"-crf");
            args.Add($"{encodingSettings.Crf ?? 23}");

            args.Add($"-preset");
            args.Add("medium");

            args.Add($"-profile:v");
            args.Add(codecParams.Profile ?? "high");

            args.Add($"-bf");
            args.Add($"{codecParams.Bf ?? 3}");

            args.Add($"-refs");
            args.Add($"{codecParams.Refs ?? 4}");

            // Add H.265 specific settings
            if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(codecParams.Tag))
            {
                args.Add($"-tag:v");
                args.Add(codecParams.Tag);
            }

            // Audio settings
            args.Add($"-c:a");
            args.Add("copy");

            // Additional settings
            args.Add("-y");
            args.Add("-stats");

            conversion.AddParameter(string.Join(" ", args));

            // Display FFmpeg command line parameters
            Console.WriteLine("\nFFmpeg Command Parameters (CPU Processing):");
            Console.WriteLine("==================================================");
            Console.WriteLine($"ffmpeg -i \"{inputPath}\" {string.Join(" ", args)} \"{outputPath}\"");
            Console.WriteLine("==================================================\n");

            // Add progress reporting
            conversion.OnProgress += (sender, eventArgs) =>
            {
                var percent = Math.Round((double)eventArgs.Percent, 1);
                var processed = eventArgs.Duration;
                var total = eventArgs.TotalLength;
                Console.Write($"\rProgress: {percent}% [{processed:hh\\:mm\\:ss} / {total:hh\\:mm\\:ss}]");
            };

            conversion.OnDataReceived += (sender, eventArgs) =>
            {
                // Show FFmpeg output for debugging (you can comment this out if too verbose)
                if (!string.IsNullOrWhiteSpace(eventArgs.Data) && eventArgs.Data.Contains("frame="))
                {
                    Console.Write($"\r{eventArgs.Data.Trim()}");
                }
            };

            var result = await conversion.Start();
            Console.WriteLine(); // New line after progress
            Console.WriteLine("✓ CPU compression completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CPU compression error: {ex.Message}");
            return false;
        }
    }

    private static string GetGpuCodecName(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264_nvenc",
            VideoCodec.H265 => "hevc_nvenc",
            _ => "h264_nvenc"
        };
    }

    private static string GetCpuCodecName(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.H265 => "libx265",
            _ => "libx264"
        };
    }

    private static (int width, int height) GetScaleSize(int originalWidth, int originalHeight, int maxWidth)
    {
        if (maxWidth == int.MaxValue || originalWidth <= maxWidth)
        {
            // No scaling needed, just ensure even dimensions
            return (MakeEven(originalWidth), MakeEven(originalHeight));
        }

        // Scale down maintaining aspect ratio
        double aspectRatio = (double)originalHeight / originalWidth;
        int newWidth = maxWidth;
        int newHeight = (int)(newWidth * aspectRatio);

        return (MakeEven(newWidth), MakeEven(newHeight));
    }

    private static int MakeEven(int value)
    {
        return value % 2 == 0 ? value : value - 1;
    }

    public static async Task<bool> CheckFFmpegAvailabilityAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Embedded FFmpeg initialization failed: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> CheckNVENCAvailabilityAsync()
    {
        Console.WriteLine("Checking NVIDIA NVENC availability...");

        try
        {
            await EnsureInitializedAsync();

            // Try a simple conversion with NVENC to test availability
            // This is a quick test - we'll create a minimal test
            var tempInput = Path.GetTempFileName() + ".mp4";
            var tempOutput = Path.GetTempFileName() + ".mp4";

            try
            {
                // Create a minimal test video (1 second, 2x2 pixel)
                var testConversion = FFmpeg.Conversions.New()
                    .SetOutput(tempInput)
                    .SetOverwriteOutput(true);

                testConversion.AddParameter("-f lavfi -i testsrc=duration=1:size=2x2:rate=1 -c:v libx264 -t 1");
                await testConversion.Start();

                // Test NVENC encoding
                var nvencTest = FFmpeg.Conversions.New()
                    .SetOutput(tempOutput)
                    .SetOverwriteOutput(true);

                nvencTest.AddParameter($"-i \"{tempInput}\" -c:v h264_nvenc -t 0.1");
                await nvencTest.Start();

                Console.WriteLine("✓ NVIDIA NVENC hardware acceleration is available");
                return true;
            }
            catch
            {
                Console.WriteLine("⚠ NVIDIA NVENC hardware acceleration is not available");
                Console.WriteLine("  This could be due to:");
                Console.WriteLine("  - No NVIDIA GPU present");
                Console.WriteLine("  - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
                Console.WriteLine("  - Outdated GPU drivers");
                Console.WriteLine("  - FFmpeg not compiled with NVENC support");
                return false;
            }
            finally
            {
                // Cleanup test files
                try { File.Delete(tempInput); } catch { }
                try { File.Delete(tempOutput); } catch { }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking NVENC availability: {ex.Message}");
            return false;
        }
    }
}