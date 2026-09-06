
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace PPTcrunch;

public class EmbeddedFFmpegRunner
{
    private const int TimeoutMinutes = 60; // minutes timeout per encoding pass
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
        // Use Martin Riedl's FFmpeg builds which include Apple VideoToolbox hardware acceleration
        string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        string baseUrl = $"https://ffmpeg.martin-riedl.de/download/macos/{architecture}/1756401489_8.0";

        Console.WriteLine($"  Downloading Apple optimized FFmpeg build with VideoToolbox support for {architecture}...");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // Download ffmpeg
        string ffmpegUrl = $"{baseUrl}/ffmpeg.zip";
        string ffmpegTempFile = Path.Combine(Path.GetTempPath(), $"ffmpeg-{architecture}.zip");

        Console.WriteLine("  Downloading ffmpeg binary...");
        using var ffmpegResponse = await httpClient.GetAsync(ffmpegUrl);
        ffmpegResponse.EnsureSuccessStatusCode();

        await using (var fileStream = File.Create(ffmpegTempFile))
        {
            await ffmpegResponse.Content.CopyToAsync(fileStream);
        }

        // Download ffprobe
        string ffprobeUrl = $"{baseUrl}/ffprobe.zip";
        string ffprobeTempFile = Path.Combine(Path.GetTempPath(), $"ffprobe-{architecture}.zip");

        Console.WriteLine("  Downloading ffprobe binary...");
        using var ffprobeResponse = await httpClient.GetAsync(ffprobeUrl);
        ffprobeResponse.EnsureSuccessStatusCode();

        await using (var fileStream = File.Create(ffprobeTempFile))
        {
            await ffprobeResponse.Content.CopyToAsync(fileStream);
        }

        Console.WriteLine("  Extracting FFmpeg binaries...");
        ZipFile.ExtractToDirectory(ffmpegTempFile, ffmpegBaseDir, true);
        ZipFile.ExtractToDirectory(ffprobeTempFile, ffmpegBaseDir, true);

        // Cleanup temp files
        try
        {
            File.Delete(ffmpegTempFile);
            File.Delete(ffprobeTempFile);
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    public static async Task<bool> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
        => (await CompressVideoWithResultAsync(inputPath, outputPath, settings)).Success;

    public static async Task<int> GetVideoWidthAsync(string inputPath)
    {
        await EnsureInitializedAsync();
        var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
        return mediaInfo.VideoStreams.FirstOrDefault()?.Width
            ?? throw new InvalidOperationException("No video stream found");
    }

    internal static async Task<double?> GetVideoFrameRateAsync(string inputPath)
    {
        await EnsureInitializedAsync();
        return await FrameRatePolicy.ReadAsync(inputPath, Path.Combine(_ffmpegPath!, GetFFprobeExecutableName()));
    }

    public static async Task<VideoEncodingResult> CompressVideoWithResultAsync(string inputPath, string outputPath, UserSettings settings)
    {
        await EnsureInitializedAsync();
        var hardware = settings.EffectiveHardwareAcceleration;
        if (hardware != HardwareAccelerationMode.None)
        {
            if (await TryEncodeAsync(inputPath, outputPath, settings, hardware))
                return new(true, hardware);
            Console.WriteLine("Hardware compression failed, falling back to CPU at the selected quality level...");
        }
        bool success = await TryEncodeAsync(inputPath, outputPath, settings, HardwareAccelerationMode.None);
        return new(success, HardwareAccelerationMode.None);
    }

    private static async Task<bool> TryEncodeAsync(string inputPath, string outputPath, UserSettings settings, HardwareAccelerationMode hardware)
    {
        string label = hardware == HardwareAccelerationMode.None ? "CPU" : GetHardwareLabel(hardware);
        Console.WriteLine($"Running {label} compression...");
        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath);
            if (!mediaInfo.VideoStreams.Any())
            {
                Console.WriteLine("No video stream found");
                return false;
            }
            double? inputRate = settings.ReduceHighFrameRates
                ? await GetVideoFrameRateAsync(inputPath) : null;
            double? targetRate = FrameRatePolicy.TargetRate(settings, inputRate);
            if (targetRate.HasValue)
                Console.WriteLine($"Reducing video from {inputRate:0.###} FPS to {targetRate:0.###} FPS.");
            var args = EncodingArguments.Build(settings, hardware, inputFrameRate: inputRate);
            // Preserve compatible audio without another lossy generation. Convert other
            // input audio to the container's browser-compatible codec.
            var audio = mediaInfo.AudioStreams.ToArray();
            for (int i = 0; i < audio.Length; i++)
            {
                bool canCopy = settings.Codec == VideoCodec.VP9
                    ? audio[i].Codec is "opus" or "vorbis"
                    : audio[i].Codec == "aac";
                if (canCopy) args.AddRange(new[] { $"-c:a:{i}", "copy" });
                else if (audio[i].Channels > 2)
                    args.AddRange(new[] { $"-b:a:{i}", $"{64 * audio[i].Channels}k" });
            }
            async Task<bool> RunPass(string title, List<string> options, string destination)
            {
                var conversion = FFmpeg.Conversions.New();
                AttachProgressHandlers(conversion);
                Console.WriteLine(title);
                PrintCommand(title, inputPath, destination, options);
                // Explicit arguments avoid source bitrate/codec options inferred by AddStream.
                string command = $"-nostdin -i \"{inputPath}\" {string.Join(" ", options)} \"{destination}\"";
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(TimeoutMinutes));
                await conversion.Start(command, timeout.Token);
                return true;
            }
            bool success = settings.Codec == VideoCodec.VP9 && settings.UseVp9TwoPass
                ? await Vp9TwoPassEncoder.RunAsync(inputPath, outputPath, settings, args, RunPass)
                : await RunPass(label, args, outputPath);
            if (!success) return false;
            Console.WriteLine($"\n✓ {label} compression completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{label} compression error: {ex.Message}");
            return false;
        }
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
