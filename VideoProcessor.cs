namespace PPTcrunch;

public class VideoProcessor
{
    private const string OutputModifiers = @"(?:-\d+)?(?:-\d+FPS)?";
    /// <summary>
    /// Checks if a video filename indicates it has already been recompressed.
    /// Recognizes legacy Q values and current L quality levels for MP4 and WebM.
    /// </summary>
    /// <param name="filename">The filename to check</param>
    /// <returns>True if the filename indicates the video has already been recompressed</returns>
    public static bool IsAlreadyRecompressed(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return false;

        // Examples: " - Q22H264.mp4", "-L3H265.mp4", " - L2VP9.webm".
        var pattern = $@" ?- ?(?:Q|L)\d+(?:H26[45]{OutputModifiers}\.mp4|VP9{OutputModifiers}\.webm)$";
        return System.Text.RegularExpressions.Regex.IsMatch(filename, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public static bool ShouldSkipRecompression(string filename, UserSettings settings)
    {
        if (!IsAlreadyRecompressed(filename)) return false;
        // An archive is a reusable source for a smaller delivery encode, including
        // the same codec. Still skip archive-to-archive runs of the same codec.
        bool archive = System.Text.RegularExpressions.Regex.IsMatch(filename,
            $@" ?- ?L4(?:H26[45]{OutputModifiers}\.mp4|VP9{OutputModifiers}\.webm)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (archive && settings.QualityLevel < 4) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(filename,
            settings.CodecSuffix + OutputModifiers + System.Text.RegularExpressions.Regex.Escape(settings.OutputExtension) + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
        if (ShouldSkipRecompression(filename, settings))
        {
            Console.WriteLine($"⚠ Video appears to have already been recompressed (filename: {filename})");
            Console.WriteLine("  Skipping to avoid double compression.");
            return true; // Return true as this is not an error condition
        }

        // Check if file is a supported video format
        string extension = Path.GetExtension(videoPath).ToLowerInvariant();
        string[] supportedExtensions = { ".mp4", ".mpeg4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".mpg", ".mpeg", ".3gp", ".3g2", ".asf", ".ogv" };

        if (!supportedExtensions.Contains(extension))
        {
            Console.WriteLine($"Error: Unsupported video format '{extension}'. Supported formats: {string.Join(", ", supportedExtensions)}");
            return false;
        }

        try
        {
            // Generate output filename
            int inputWidth = await EmbeddedFFmpegRunner.GetVideoWidthAsync(videoPath);
            double? inputFrameRate = settings.ReduceHighFrameRates
                ? await EmbeddedFFmpegRunner.GetVideoFrameRateAsync(videoPath) : null;
            string outputPath = GenerateOutputFilename(videoPath, settings, inputWidth, inputFrameRate);

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
            else if (result.WasCompressed && (settings.Codec == VideoCodec.VP9 || settings.QualityLevel == 4))
            {
                Console.WriteLine("✓ Export saved. It is larger than the source at the selected quality.");
                Console.WriteLine($"  Original: {FormatFileSize(result.OriginalSize)}; export: {FormatFileSize(result.FinalSize)}");
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

    public static string GenerateOutputFilename(string inputPath, UserSettings settings, int? inputWidth = null, double? inputFrameRate = null)
    {
        string directory = Path.GetDirectoryName(inputPath) ?? "";
        string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);

        // The user level stays accurate if hardware encoding falls back to CPU.
        // Match the encoder's even-width cap. Merely enabling the cap must not
        // label videos that already fit it, or odd widths rounded for 4:2:0.
        int widthLimit = Math.Max(2, settings.MaxWidth);
        string resolutionSuffix = inputWidth > widthLimit ? $"-{widthLimit / 2 * 2}" : "";
        double? targetRate = FrameRatePolicy.TargetRate(settings, inputFrameRate);
        string fpsSuffix = targetRate.HasValue
            ? $"-{targetRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}FPS" : "";
        string outputFileName = $"{nameWithoutExt} - L{settings.QualityLevel}{settings.CodecSuffix}{resolutionSuffix}{fpsSuffix}{settings.OutputExtension}";
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
            var encodingResult = await EmbeddedFFmpegRunner.CompressVideoWithResultAsync(inputPath, outputPath, settings);

            if (encodingResult.Success && File.Exists(outputPath))
            {
                result.FinalSize = new FileInfo(outputPath).Length;
                result.WasCompressed = true;
                result.FileSizeReduced = result.FinalSize < result.OriginalSize;
                result.FinalFileName = Path.GetFileName(outputPath);
                result.CompressionMethod = encodingResult.Method;
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
