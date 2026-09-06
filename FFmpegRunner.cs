using System.Diagnostics;
using System.Text;

namespace PPTcrunch;

public class FFmpegRunner
{
    private const int TimeoutMinutes = 60; // minutes timeout per encoding pass

    public static async Task<bool> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
    {
        double? inputRate = null;
        if (settings.ReduceHighFrameRates)
        {
            try { inputRate = await FrameRatePolicy.ReadAsync(inputPath, "ffprobe"); }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame-rate inspection failed: {ex.Message}");
                return false;
            }
            double? targetRate = FrameRatePolicy.TargetRate(settings, inputRate);
            if (targetRate.HasValue)
                Console.WriteLine($"Reducing video from {inputRate:0.###} FPS to {targetRate:0.###} FPS.");
        }
        // Check if GPU acceleration is preferred and available
        if (settings.EffectiveHardwareAcceleration != HardwareAccelerationMode.None)
        {
            bool gpuSuccess = await TryCompressWithGPU(inputPath, outputPath, settings, inputRate);
            if (gpuSuccess)
            {
                return true;
            }

            Console.WriteLine("GPU compression failed, falling back to CPU compression...");
        }

        return await TryCompressWithCPU(inputPath, outputPath, settings, inputRate);
    }

    private static async Task<bool> TryCompressWithGPU(string inputPath, string outputPath, UserSettings settings, double? inputRate)
    {
        string arguments = BuildArguments(inputPath, outputPath, settings, settings.EffectiveHardwareAcceleration, inputRate);

        string hardwareLabel = settings.HardwareAcceleration switch
        {
            HardwareAccelerationMode.AppleVideoToolbox => "Apple VideoToolbox",
            HardwareAccelerationMode.NvidiaNvenc => "NVIDIA NVENC",
            _ => "GPU"
        };

        Console.WriteLine($"Trying {hardwareLabel} compression with command:");
        Console.WriteLine($"ffmpeg {arguments}");
        Console.WriteLine();

        bool result = await RunFFmpegProcess(arguments);

        if (!result)
        {
            Console.WriteLine("GPU compression failed. This could be due to:");
            if (settings.HardwareAcceleration == HardwareAccelerationMode.NvidiaNvenc)
            {
                Console.WriteLine("  - FFmpeg not compiled with NVENC support");
                Console.WriteLine("  - NVIDIA GPU drivers not installed or outdated");
                Console.WriteLine("  - GPU doesn't support NVENC (requires GTX 600+ or RTX series)");
                Console.WriteLine("  - GPU is busy with other tasks");
            }
            else if (settings.HardwareAcceleration == HardwareAccelerationMode.AppleVideoToolbox)
            {
                Console.WriteLine("  - FFmpeg build missing VideoToolbox encoder support");
                Console.WriteLine("  - macOS security settings blocking VideoToolbox access");
                Console.WriteLine("  - Unsupported codec/quality combination for this hardware");
            }
        }

        return result;
    }

    private static async Task<bool> TryCompressWithCPU(string inputPath, string outputPath, UserSettings settings, double? inputRate)
    {
        if (settings.Codec == VideoCodec.VP9 && settings.UseVp9TwoPass)
        {
            try
            {
                return await Vp9TwoPassEncoder.RunAsync(inputPath, outputPath, settings,
                    EncodingArguments.Build(settings, HardwareAccelerationMode.None, inputFrameRate: inputRate), async (label, options, destination) =>
                    {
                        string command = $"-nostdin -i \"{inputPath}\" {string.Join(" ", options)} \"{destination}\"";
                        Console.WriteLine($"{label}\nffmpeg {command}");
                        return await RunFFmpegProcess(command);
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VP9 compression error: {ex.Message}");
                return false;
            }
        }
        // Build the FFmpeg command with CPU encoding
        string arguments = BuildArguments(inputPath, outputPath, settings, HardwareAccelerationMode.None, inputRate);

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
                    await process.WaitForExitAsync();
                    await Task.WhenAll(outputTask, errorTask);
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

    private static string BuildArguments(string inputPath, string outputPath, UserSettings settings, HardwareAccelerationMode hardware, double? inputRate)
        => $"-nostdin -i \"{inputPath}\" {string.Join(" ", EncodingArguments.Build(settings, hardware, inputFrameRate: inputRate))} \"{outputPath}\"";
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
