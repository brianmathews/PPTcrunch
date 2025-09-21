namespace PPTcrunch;

public class VideoProcessor
{
    /// <summary>
    /// Checks if a video filename indicates it has already been recompressed.
    /// Detects patterns like " - Q22H264.mp4" (standalone) or "-Q26H265.mp4" (PowerPoint).
    /// </summary>
    /// <param name="filename">The filename to check</param>
    /// <returns>True if the filename indicates the video has already been recompressed</returns>
    public static bool IsAlreadyRecompressed(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return false;

        // Pattern: optional space + dash + optional space + Q + numbers + H + numbers + .mp4 at end
        // Matches: " - Q22H264.mp4", "-Q26H265.mp4", etc.
        var pattern = @"( )?-( )?Q\d+H\d+\.mp4$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public async Task<bool> ProcessVideoFileAsync(string videoPath, UserSettings settings)
    {
        Console.WriteLine($"Processing video: {videoPath}");
        Console.WriteLine("=".PadRight(50, '='));

        if (!File.Exists(videoPath))
        {
            Console.WriteLine($"Error: Video file '{videoPath}' not found.");
            return false;
        }

        // Check if video has already been recompressed
        string filename = Path.GetFileName(videoPath);
        if (IsAlreadyRecompressed(filename))
        {
            Console.WriteLine($"⚠ Video appears to have already been recompressed (filename: {filename})");
            Console.WriteLine("  Skipping to avoid double compression.");
            return true; // Return true as this is not an error condition
        }

        // Check if file is a supported video format
        string extension = Path.GetExtension(videoPath).ToLowerInvariant();
        string[] supportedExtensions = { ".mp4", ".mpeg4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v" };

        if (!supportedExtensions.Contains(extension))
        {
            Console.WriteLine($"Error: Unsupported video format '{extension}'. Supported formats: {string.Join(", ", supportedExtensions)}");
            return false;
        }

        try
        {
            // Generate output filename
            string outputPath = GenerateOutputFilename(videoPath, settings);

            Console.WriteLine($"Input:  {videoPath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine();

            // Compress the video
            Console.WriteLine("Compressing video with FFmpeg...");
            Console.WriteLine("-".PadRight(50, '-'));

            var result = await CompressVideoAsync(videoPath, outputPath, settings);

            if (result.WasCompressed && result.FileSizeReduced)
            {
                Console.WriteLine();
                Console.WriteLine($"✓ Video compressed successfully!");
                ShowCompressionResults(result);
                return true;
            }
            else if (result.WasCompressed && !result.FileSizeReduced)
            {
                // Remove the larger compressed file and keep original
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                Console.WriteLine();
                Console.WriteLine($"⚠ Compressed file was larger than original - keeping original file unchanged");
                Console.WriteLine($"  Original: {FormatFileSize(result.OriginalSize)}");
                Console.WriteLine($"  Compressed: {FormatFileSize(result.FinalSize)}");
                return true;
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"✗ Video compression failed - {result.Reason}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing video: {ex.Message}");
            return false;
        }
    }

    private string GenerateOutputFilename(string inputPath, UserSettings settings)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? "";
        string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);

        // Get actual quality value for filename
        var encodingSettings = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, settings.UseGPUAcceleration);
        int qualityValue;
        if (settings.UseGPUAcceleration)
        {
            qualityValue = settings.HardwareAcceleration switch
            {
                HardwareAccelerationMode.AppleVideoToolbox => encodingSettings.VtQuality ?? encodingSettings.Cq ?? encodingSettings.Crf ?? 55,
                HardwareAccelerationMode.NvidiaNvenc => encodingSettings.Cq ?? encodingSettings.VtQuality ?? encodingSettings.Crf ?? 25,
                _ => encodingSettings.Crf ?? encodingSettings.Cq ?? encodingSettings.VtQuality ?? 25
            };
        }
        else
        {
            qualityValue = encodingSettings.Crf ?? encodingSettings.Cq ?? encodingSettings.VtQuality ?? 25;
        }

        // Generate codec string
        string codecString = settings.Codec == VideoCodec.H264 ? "H264" : "H265";

        // Generate filename with pattern: "originalname - Q{quality}{codec}.mp4"
        string outputFileName = $"{nameWithoutExt} - Q{qualityValue}{codecString}.mp4";

        return Path.Combine(directory, outputFileName);
    }

    private async Task<VideoCompressionResult> CompressVideoAsync(string inputPath, string outputPath, UserSettings settings)
    {
        var result = new VideoCompressionResult
        {
            OriginalFileName = Path.GetFileName(inputPath),
            OriginalSize = new FileInfo(inputPath).Length
        };

        try
        {
            bool compressionSuccess = await EmbeddedFFmpegRunner.CompressVideoAsync(inputPath, outputPath, settings);

            if (compressionSuccess && File.Exists(outputPath))
            {
                result.FinalSize = new FileInfo(outputPath).Length;
                result.WasCompressed = true;
                result.FileSizeReduced = result.FinalSize < result.OriginalSize;
                result.FinalFileName = Path.GetFileName(outputPath);
                result.CompressionMethod = settings.UseGPUAcceleration ? "GPU" : "CPU";
                result.Reason = result.FileSizeReduced ? "Compression successful" : "Compressed file was larger";
            }
            else
            {
                result.FinalFileName = result.OriginalFileName;
                result.WasCompressed = false;
                result.FileSizeReduced = false;
                result.FinalSize = result.OriginalSize;
                result.CompressionMethod = "Original";
                result.Reason = "Compression failed";
            }
        }
        catch (Exception ex)
        {
            result.FinalFileName = result.OriginalFileName;
            result.WasCompressed = false;
            result.FileSizeReduced = false;
            result.FinalSize = result.OriginalSize;
            result.CompressionMethod = "Original";
            result.Reason = $"Error: {ex.Message}";
        }

        return result;
    }

    private void ShowCompressionResults(VideoCompressionResult result)
    {
        Console.WriteLine($"  Original size:   {FormatFileSize(result.OriginalSize)}");
        Console.WriteLine($"  Compressed size: {FormatFileSize(result.FinalSize)}");

        if (result.OriginalSize > 0)
        {
            double compressionRatio = (double)result.FinalSize / result.OriginalSize;
            double spaceSavedPercent = (1 - compressionRatio) * 100;
            long spaceSaved = result.OriginalSize - result.FinalSize;

            Console.WriteLine($"  Space saved:     {FormatFileSize(spaceSaved)} ({spaceSavedPercent:F1}%)");
            Console.WriteLine($"  Compression method: {result.CompressionMethod}");
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:F2} {sizes[order]}";
    }
}