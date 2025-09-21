
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
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

            string ffmpegBaseDir = GetDefaultFFmpegBaseDirectory();
            Directory.CreateDirectory(ffmpegBaseDir);

            string? ffmpegDirectory = FindExistingFFmpegDirectory(ffmpegBaseDir, searchAllSubdirectories: true);

            if (ffmpegDirectory == null)
            {
                Console.WriteLine($"Downloading FFmpeg binaries to {ffmpegBaseDir}...");

                await DownloadFFmpegAsync(ffmpegBaseDir);
                Console.WriteLine("FFmpeg binaries downloaded successfully");

                ffmpegDirectory = FindExistingFFmpegDirectory(ffmpegBaseDir, searchAllSubdirectories: true);
                if (ffmpegDirectory == null)
                {
                    Console.WriteLine($"Files in FFmpeg directory ({ffmpegBaseDir}):");
                    try
                    {
                        var files = Directory.GetFiles(ffmpegBaseDir, "*", SearchOption.AllDirectories);
                        foreach (var file in files.Take(20))
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

            FFmpeg.SetExecutablesPath(ffmpegDirectory);
            _ffmpegPath = ffmpegDirectory;

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

    private static IConversion CreateConversion(IMediaInfo mediaInfo, string outputPath)
    {
        return FFmpeg.Conversions.New()
            .AddStream(mediaInfo.VideoStreams.ToArray())
            .AddStream(mediaInfo.AudioStreams.ToArray())
            .SetOutput(outputPath)
            .SetOverwriteOutput(true);
    }

    private static void AttachProgressHandlers(IConversion conversion)
    {
        conversion.OnProgress += (sender, eventArgs) =>
        {
            var percent = Math.Round((double)eventArgs.Percent, 1);
            var processed = eventArgs.Duration;
            var total = eventArgs.TotalLength;
            Console.Write($"\rProgress: {percent}% [{processed:hh\\:mm\\:ss} / {total:hh\\:mm\\:ss}]");
        };

        conversion.OnDataReceived += (sender, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data) && eventArgs.Data.Contains("frame="))
            {
                Console.Write($"\r{eventArgs.Data.Trim()}");
            }
        };
    }

    private static void PrintCommand(string title, string inputPath, string outputPath, IEnumerable<string> args)
    {
        var argumentString = string.Join(" ", args);
        Console.WriteLine($"\nFFmpeg Command Parameters ({title}):");
        Console.WriteLine("==================================================");
        Console.WriteLine($"ffmpeg -i \"{inputPath}\" {argumentString} \"{outputPath}\"");
        Console.WriteLine("==================================================\n");
    }

    private static string GetHardwareLabel(HardwareAccelerationMode mode)
    {
        return mode switch
        {
            HardwareAccelerationMode.NvidiaNvenc => "NVIDIA NVENC GPU",
            HardwareAccelerationMode.AppleVideoToolbox => "Apple VideoToolbox",
            _ => "hardware"
        };
    }

    public static string GetPreferredFFmpegDirectory() => GetDefaultFFmpegBaseDirectory();

    private static string GetDefaultFFmpegBaseDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return @"C:\ffmpeg";
        }

        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (string.IsNullOrWhiteSpace(home))
            {
                home = Path.GetTempPath();
            }

            if (OperatingSystem.IsMacOS())
            {
                basePath = Path.Combine(home, "Library", "Application Support");
            }
            else
            {
                basePath = Path.Combine(home, ".local", "share");
            }
        }

        return Path.Combine(basePath, "PPTcrunch", "ffmpeg");
    }

    private static string GetFFmpegExecutableName() => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    private static string GetFFprobeExecutableName() => OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    private static IEnumerable<string> GetCandidateDirectories(string ffmpegBaseDir)
    {
        var directories = new List<string?>
        {
            ffmpegBaseDir,
            Path.Combine(ffmpegBaseDir, "bin"),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "FFmpeg"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg")
        };

        return directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? FindExistingFFmpegDirectory(string ffmpegBaseDir, bool searchAllSubdirectories = false)
    {
        string ffmpegExecutable = GetFFmpegExecutableName();
        string ffprobeExecutable = GetFFprobeExecutableName();

        var candidateDirectories = GetCandidateDirectories(ffmpegBaseDir);

        foreach (var directory in candidateDirectories)
        {
            Console.WriteLine($"Checking for FFmpeg at: {Path.Combine(directory, ffmpegExecutable)}");
            var match = FindInDirectory(directory, ffmpegExecutable, ffprobeExecutable, SearchOption.TopDirectoryOnly);
            if (match != null)
            {
                return match;
            }
        }

        if (searchAllSubdirectories)
        {
            foreach (var directory in candidateDirectories)
            {
                var match = FindInDirectory(directory, ffmpegExecutable, ffprobeExecutable, SearchOption.AllDirectories);
                if (match != null)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private static string? FindInDirectory(string directory, string ffmpegExecutable, string ffprobeExecutable, SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var ffmpegPath = Directory.EnumerateFiles(directory, ffmpegExecutable, searchOption).FirstOrDefault();
            if (ffmpegPath == null)
            {
                return null;
            }

            string candidateDirectory = Path.GetDirectoryName(ffmpegPath)!;
            string ffprobePath = Path.Combine(candidateDirectory, ffprobeExecutable);

            if (!File.Exists(ffprobePath))
            {
                return null;
            }

            Console.WriteLine($"Found FFmpeg at: {ffmpegPath}");
            return candidateDirectory;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Error searching '{directory}': {ex.Message}");
            return null;
        }
    }

    private static async Task DownloadFFmpegAsync(string ffmpegBaseDir)
    {
        if (OperatingSystem.IsMacOS())
        {
            await DownloadMacFFmpegAsync(ffmpegBaseDir);
            return;
        }

        FFmpeg.SetExecutablesPath(ffmpegBaseDir);
        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegBaseDir);
    }

    private static async Task DownloadMacFFmpegAsync(string ffmpegBaseDir)
    {
        string archiveName = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "ffmpeg-master-latest-macos64-arm64-static.zip"
            : "ffmpeg-master-latest-macos64-static.zip";
        string downloadUrl = $"https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/{archiveName}";

        string tempFile = Path.Combine(Path.GetTempPath(), archiveName);
        if (File.Exists(tempFile))
        {
            try
            {
                File.Delete(tempFile);
            }
            catch
            {
                // Non-critical cleanup failure
            }
        }

        Console.WriteLine($"  Downloading Apple optimized FFmpeg build: {archiveName}");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        using var response = await httpClient.GetAsync(downloadUrl);
        response.EnsureSuccessStatusCode();

        await using (var fileStream = File.Create(tempFile))
        {
            await response.Content.CopyToAsync(fileStream);
        }

        Console.WriteLine("  Extracting FFmpeg archive...");
        ZipFile.ExtractToDirectory(tempFile, ffmpegBaseDir, true);

        try
        {
            File.Delete(tempFile);
        }
        catch
        {
            // Ignore extraction cleanup failures
        }
    }

    public static async Task<bool> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
    {
        await EnsureInitializedAsync();

        if (settings.UseGPUAcceleration && settings.HardwareAcceleration != HardwareAccelerationMode.None)
        {
            bool hardwareSuccess = await TryCompressWithHardwareAcceleration(inputPath, outputPath, settings);
            if (hardwareSuccess)
            {
                return true;
            }

            Console.WriteLine("Hardware compression failed, falling back to CPU compression...");
        }

        return await TryCompressWithCPU(inputPath, outputPath, settings);
    }

    private static async Task<bool> TryCompressWithHardwareAcceleration(string inputPath, string outputPath, UserSettings settings)
    {
        Console.WriteLine($"Trying {GetHardwareLabel(settings.HardwareAcceleration)} compression...");

        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            if (videoStream == null)
            {
                Console.WriteLine("No video stream found");
                return false;
            }

            var encodingSettings = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, true);
            var codecParams = QualityConfigService.GetCodecParams(settings.Codec, true);
            var scaleSize = GetScaleSize(videoStream.Width, videoStream.Height, settings.MaxWidth);

            return settings.HardwareAcceleration switch
            {
                HardwareAccelerationMode.NvidiaNvenc => await RunNvencConversion(mediaInfo, inputPath, outputPath, settings, encodingSettings, codecParams, scaleSize),
                HardwareAccelerationMode.AppleVideoToolbox => await RunVideoToolboxConversion(mediaInfo, inputPath, outputPath, settings, encodingSettings, codecParams, scaleSize),
                _ => false
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Hardware compression error: {ex.Message}");

            if (settings.HardwareAcceleration == HardwareAccelerationMode.NvidiaNvenc)
            {
                Console.WriteLine("This could be due to:");
                Console.WriteLine("  - FFmpeg not compiled with NVENC support");
                Console.WriteLine("  - NVIDIA GPU drivers not installed or outdated");
                Console.WriteLine("  - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
                Console.WriteLine("  - GPU is busy with other tasks");
            }
            else if (settings.HardwareAcceleration == HardwareAccelerationMode.AppleVideoToolbox)
            {
                Console.WriteLine("This could be due to:");
                Console.WriteLine("  - macOS security restrictions preventing VideoToolbox access");
                Console.WriteLine("  - FFmpeg build missing VideoToolbox encoder support");
                Console.WriteLine("  - Unsupported codec/quality combination for this hardware");
            }

            return false;
        }
    }

    private static async Task<bool> RunNvencConversion(IMediaInfo mediaInfo, string inputPath, string outputPath, UserSettings settings, EncodingSettings encodingSettings, CodecParams codecParams, (int width, int height) scaleSize)
    {
        var conversion = CreateConversion(mediaInfo, outputPath);

        var args = new List<string>
        {
            "-vf", $"scale={scaleSize.width}:{scaleSize.height}",
            "-c:v", GetNvencCodecName(settings.Codec),
            "-cq", $"{encodingSettings.Cq ?? 23}",
            "-b:v", "0"
        };

        if (!string.IsNullOrEmpty(encodingSettings.Rc))
        {
            args.Add("-rc");
            args.Add(encodingSettings.Rc);
        }

        if (!string.IsNullOrEmpty(encodingSettings.Preset))
        {
            args.Add("-preset");
            args.Add(encodingSettings.Preset);
        }

        args.Add("-profile:v");
        args.Add(codecParams.Profile ?? "high");

        args.Add("-bf");
        args.Add($"{codecParams.Bf ?? 3}");

        args.Add("-refs");
        args.Add($"{codecParams.Refs ?? 4}");

        if (!string.IsNullOrEmpty(encodingSettings.Tune))
        {
            args.Add("-tune");
            args.Add(encodingSettings.Tune);
        }

        if (encodingSettings.Multipass.HasValue)
        {
            args.Add("-multipass");
            args.Add($"{encodingSettings.Multipass.Value}");
        }

        if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(codecParams.Tag))
        {
            args.Add("-tag:v");
            args.Add(codecParams.Tag);
        }

        args.Add("-c:a");
        args.Add("copy");

        args.Add("-y");
        args.Add("-stats");

        conversion.AddParameter(string.Join(" ", args));

        PrintCommand("Hardware Acceleration (NVIDIA NVENC)", inputPath, outputPath, args);
        AttachProgressHandlers(conversion);

        await conversion.Start();
        Console.WriteLine();
        Console.WriteLine("✓ NVIDIA NVENC compression completed successfully");
        return true;
    }

    private static async Task<bool> RunVideoToolboxConversion(IMediaInfo mediaInfo, string inputPath, string outputPath, UserSettings settings, EncodingSettings encodingSettings, CodecParams codecParams, (int width, int height) scaleSize)
    {
        var conversion = CreateConversion(mediaInfo, outputPath);

        var args = new List<string>
        {
            "-hwaccel", "videotoolbox",
            "-allow_sw", "1",
            "-vf", $"scale={scaleSize.width}:{scaleSize.height}",
            "-c:v", GetVideoToolboxCodecName(settings.Codec),
            "-q:v", $"{encodingSettings.VtQuality ?? 55}",
            "-b:v", "0",
            "-pix_fmt", "yuv420p"
        };

        if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(codecParams.Tag))
        {
            args.Add("-tag:v");
            args.Add(codecParams.Tag);
        }

        args.Add("-c:a");
        args.Add("copy");

        args.Add("-y");
        args.Add("-stats");

        conversion.AddParameter(string.Join(" ", args));

        PrintCommand("Hardware Acceleration (Apple VideoToolbox)", inputPath, outputPath, args);
        AttachProgressHandlers(conversion);

        await conversion.Start();
        Console.WriteLine();
        Console.WriteLine("✓ Apple VideoToolbox compression completed successfully");
        return true;
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

            var conversion = CreateConversion(mediaInfo, outputPath);

            var args = new List<string>
            {
                "-vf", $"scale={scaleSize.width}:{scaleSize.height}",
                "-c:v", GetCpuCodecName(settings.Codec),
                "-crf", $"{encodingSettings.Crf ?? 23}",
                "-preset", "medium",
                "-profile:v", codecParams.Profile ?? "high",
                "-bf", $"{codecParams.Bf ?? 3}",
                "-refs", $"{codecParams.Refs ?? 4}"
            };

            if (settings.Codec == VideoCodec.H265 && !string.IsNullOrEmpty(codecParams.Tag))
            {
                args.Add("-tag:v");
                args.Add(codecParams.Tag);
            }

            args.Add("-c:a");
            args.Add("copy");

            args.Add("-y");
            args.Add("-stats");

            conversion.AddParameter(string.Join(" ", args));

            PrintCommand("CPU Processing", inputPath, outputPath, args);
            AttachProgressHandlers(conversion);

            await conversion.Start();
            Console.WriteLine();
            Console.WriteLine("✓ CPU compression completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CPU compression error: {ex.Message}");
            return false;
        }
    }

    private static string GetNvencCodecName(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264_nvenc",
            VideoCodec.H265 => "hevc_nvenc",
            _ => "h264_nvenc"
        };
    }

    private static string GetVideoToolboxCodecName(VideoCodec codec)
    {
        return codec switch
        {
            VideoCodec.H264 => "h264_videotoolbox",
            VideoCodec.H265 => "hevc_videotoolbox",
            _ => "h264_videotoolbox"
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

    public static async Task<string?> GetFFmpegExecutablePathAsync()
    {
        await EnsureInitializedAsync();
        if (_ffmpegPath == null)
        {
            return null;
        }

        return Path.Combine(_ffmpegPath, GetFFmpegExecutableName());
    }

    public static async Task<string?> GetFFmpegDirectoryAsync()
    {
        await EnsureInitializedAsync();
        return _ffmpegPath;
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
                // Cleanup test files with better error handling
                try
                {
                    if (File.Exists(tempInput))
                    {
                        File.Delete(tempInput);
                        Console.WriteLine("  ✓ Cleaned up temporary test input file");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ Could not delete temporary test input file: {ex.Message}");
                }

                try
                {
                    if (File.Exists(tempOutput))
                    {
                        File.Delete(tempOutput);
                        Console.WriteLine("  ✓ Cleaned up temporary test output file");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ Could not delete temporary test output file: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking NVENC availability: {ex.Message}");
            return false;
        }
    }
}