using System.Diagnostics;
using System.Text;

namespace PPTcrunch;

public class FFmpegRunner
{
    private const int TimeoutMinutes = 30; // 30 minute timeout per video

    public static async Task<bool> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
    {
        // Try GPU acceleration first, fallback to CPU if it fails
        bool gpuSuccess = await TryCompressWithGPU(inputPath, outputPath, settings);
        if (gpuSuccess)
        {
            return true;
        }

        Console.WriteLine("GPU compression failed or not available, falling back to CPU compression...");
        return await TryCompressWithCPU(inputPath, outputPath, settings);
    }

    private static async Task<bool> TryCompressWithGPU(string inputPath, string outputPath, UserSettings settings)
    {
        // Build the FFmpeg command with NVIDIA GPU acceleration
        string arguments = BuildFFmpegArgumentsGPU(inputPath, outputPath, settings);

        Console.WriteLine($"Trying GPU compression with command:");
        Console.WriteLine($"ffmpeg {arguments}");
        Console.WriteLine();

        bool result = await RunFFmpegProcess(arguments);

        if (!result)
        {
            Console.WriteLine("GPU compression failed. This could be due to:");
            Console.WriteLine("  - FFmpeg not compiled with NVENC support");
            Console.WriteLine("  - NVIDIA GPU drivers not installed or outdated");
            Console.WriteLine("  - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
            Console.WriteLine("  - GPU is busy with other tasks");
        }

        return result;
    }

    private static async Task<bool> TryCompressWithCPU(string inputPath, string outputPath, UserSettings settings)
    {
        // Build the FFmpeg command with CPU encoding
        string arguments = BuildFFmpegArgumentsCPU(inputPath, outputPath, settings);

        Console.WriteLine($"Running CPU compression with command:");
        Console.WriteLine($"ffmpeg {arguments}");
        Console.WriteLine();

        return await RunFFmpegProcess(arguments);
    }

    private static async Task<bool> RunFFmpegProcess(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // Redirect stdin to prevent hanging on prompts
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("Failed to start FFmpeg process.");
                return false;
            }

            // Close stdin immediately to prevent any input prompts from hanging
            process.StandardInput.Close();

            // Create tasks to read output and error streams
            var outputTask = ReadStreamAsync(process.StandardOutput, "OUTPUT");
            var errorTask = ReadStreamAsync(process.StandardError, "PROGRESS");

            // Wait for the process with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(TimeoutMinutes));
            var processTask = process.WaitForExitAsync();

            var completedTask = await Task.WhenAny(processTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Console.WriteLine($"FFmpeg process timed out after {TimeoutMinutes} minutes. Killing process...");
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error killing process: {ex.Message}");
                }
                return false;
            }

            // Wait for output reading to complete
            await Task.WhenAll(outputTask, errorTask);

            bool success = process.ExitCode == 0;
            if (success)
            {
                Console.WriteLine("✓ Video compression completed successfully");
            }
            else
            {
                Console.WriteLine($"✗ FFmpeg failed with exit code: {process.ExitCode}");
            }

            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running FFmpeg: {ex.Message}");
            return false;
        }
    }

    private static string BuildFFmpegArgumentsGPU(string inputPath, string outputPath, UserSettings settings)
    {
        // Use NVIDIA GPU acceleration for faster encoding
        var args = new StringBuilder();

        // Input file
        args.Append($"-i \"{inputPath}\" ");

        // Video filters: scale down only if needed, maintain aspect ratio, ensure even dimensions
        // First scale: only downscale if width exceeds max, maintain aspect ratio (-1 = auto height)
        // Second scale: ensure BOTH width AND height are even (required by H.264/H.265 encoders)
        // trunc(iw/2)*2 = force even width, trunc(ih/2)*2 = force even height
        args.Append($"-vf \"scale='min({settings.MaxWidth},iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2\" ");

        // Video codec settings - NVIDIA NVENC
        args.Append($"-c:v {settings.GetGpuCodecName()} ");

        // Simplified NVENC settings compatible with GTX 1660 SUPER
        if (settings.Codec == VideoCodec.H265)
        {
            // H.265 NVENC settings optimized for GTX 1660 SUPER compatibility
            args.Append($"-cq {settings.Quality} "); // User-specified quality level
            args.Append("-preset slow "); // Slow preset for maximum quality
            args.Append("-profile:v main "); // Use 8-bit main profile (GTX 1660 SUPER doesn't support 10-bit HEVC)
            args.Append("-rc vbr "); // Variable bitrate for better quality allocation
            args.Append("-bf 3 "); // B-frames for better compression
            args.Append("-refs 3 "); // More reference frames for better quality
        }
        else
        {
            // H.264 NVENC settings optimized for technical content
            args.Append($"-cq {settings.Quality} "); // Use user-specified quality level
            args.Append("-preset slow "); // Slow preset for maximum quality
            args.Append("-profile:v high "); // High profile for better features
            args.Append("-rc vbr "); // Variable bitrate
            args.Append("-bf 3 "); // B-frames
            args.Append("-refs 4 "); // More reference frames for better quality
        }

        // Audio settings (copy if exists, otherwise ignore)
        args.Append("-c:a copy ");

        // Overwrite output file without prompting
        args.Append("-y ");

        // Reduce verbosity but keep essential info
        args.Append("-stats ");

        // Output file
        args.Append($"\"{outputPath}\"");

        return args.ToString();
    }

    private static string BuildFFmpegArgumentsCPU(string inputPath, string outputPath, UserSettings settings)
    {
        // Use CPU encoding as fallback
        var args = new StringBuilder();

        // Input file
        args.Append($"-i \"{inputPath}\" ");

        // Video filters: scale down only if needed, maintain aspect ratio, ensure even dimensions
        // First scale: only downscale if width exceeds max, maintain aspect ratio (-1 = auto height)
        // Second scale: ensure BOTH width AND height are even (required by H.264/H.265 encoders)
        // trunc(iw/2)*2 = force even width, trunc(ih/2)*2 = force even height
        args.Append($"-vf \"scale='min({settings.MaxWidth},iw):-1',scale=trunc(iw/2)*2:trunc(ih/2)*2\" ");

        // Video codec settings
        args.Append($"-c:v {settings.GetCpuCodecName()} ");
        args.Append($"-crf {settings.Quality} ");
        args.Append("-preset medium "); // Use medium preset for balance of speed/quality

        // Audio settings (copy if exists, otherwise ignore)
        args.Append("-c:a copy ");

        // Overwrite output file without prompting
        args.Append("-y ");

        // Reduce verbosity but keep essential info
        args.Append("-stats ");

        // Output file
        args.Append($"\"{outputPath}\"");

        return args.ToString();
    }

    private static async Task ReadStreamAsync(StreamReader reader, string streamType)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                // Only show progress info for FFmpeg, filter out excessive debug info
                if (streamType == "PROGRESS" && ShouldShowLine(line))
                {
                    Console.WriteLine($"  {line}");
                }
                else if (streamType == "OUTPUT" && !string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"  {line}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading {streamType} stream: {ex.Message}");
        }
    }

    private static bool ShouldShowLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        // Show progress lines that contain useful information
        return line.Contains("frame=") ||
               line.Contains("fps=") ||
               line.Contains("time=") ||
               line.Contains("bitrate=") ||
               line.Contains("size=") ||
               line.Contains("Duration:") ||
               line.Contains("Stream #") ||
               line.Contains("Error") ||
               line.Contains("Warning") ||
               line.Contains("Press [q] to stop") ||
               line.StartsWith("video:");
    }

    public static async Task<bool> CheckFFmpegAvailabilityAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
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

    public static async Task<bool> CheckNVENCAvailabilityAsync()
    {
        Console.WriteLine("Checking NVIDIA NVENC availability...");

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
            if (process == null)
            {
                Console.WriteLine("  ✗ Failed to start FFmpeg to check encoders");
                return false;
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            bool hasNVENC = output.Contains("h264_nvenc") || output.Contains("nvenc");

            if (hasNVENC)
            {
                Console.WriteLine("  ✓ NVIDIA NVENC encoder found in FFmpeg");

                // Test if CUDA/NVENC actually works with a quick test
                return await TestNVENCFunctionality();
            }
            else
            {
                Console.WriteLine("  ✗ NVIDIA NVENC encoder not found in FFmpeg build");
                Console.WriteLine("    Your FFmpeg may not be compiled with NVENC support");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error checking NVENC availability: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestNVENCFunctionality()
    {
        try
        {
            Console.WriteLine("  Testing NVENC functionality...");

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-f lavfi -i testsrc=duration=1:size=320x240:rate=1 -c:v h264_nvenc -t 1 -f null -",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                Console.WriteLine("  ✓ NVENC test successful - GPU acceleration available");
                return true;
            }
            else
            {
                Console.WriteLine("  ✗ NVENC test failed:");
                Console.WriteLine($"    {error.Split('\n').LastOrDefault(l => !string.IsNullOrWhiteSpace(l))}");
                Console.WriteLine("    GPU may not support NVENC or drivers may be outdated");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Error testing NVENC: {ex.Message}");
            return false;
        }
    }
}