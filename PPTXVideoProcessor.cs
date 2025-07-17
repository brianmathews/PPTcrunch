namespace PPTcrunch;

public class PPTXVideoProcessor
{
    public async Task ProcessAsync(string pptxPath, UserSettings settings)
    {
        Console.WriteLine($"Processing: {pptxPath}");
        Console.WriteLine("=".PadRight(50, '='));

        // Check FFmpeg availability first
        if (!await EmbeddedFFmpegRunner.CheckFFmpegAvailabilityAsync())
        {
            throw new InvalidOperationException("Embedded FFmpeg is not available. This should not happen with the embedded version.");
        }

        // Check NVENC availability for GPU acceleration
        bool nvencAvailable = await EmbeddedFFmpegRunner.CheckNVENCAvailabilityAsync();
        if (!nvencAvailable)
        {
            Console.WriteLine("GPU acceleration will not be used - falling back to CPU-only compression");
        }
        Console.WriteLine();

        string outputPath = FileManager.GetOutputPath(pptxPath);

        // Create backup and working directories
        FileManager.CreateBackupAndWorkingDirectories(pptxPath, out string zipPath, out string tempDir, out string workingDir);

        try
        {
            // Extract PPTX to working directory
            FileManager.ExtractPPTXToWorkingDirectory(zipPath, workingDir);

            // Step 4-5: Extract and rename video files
            Console.WriteLine("Step 4-5: Extracting and renaming video files...");
            var videoFiles = FileManager.ExtractAndRenameVideos(workingDir, tempDir);

            if (videoFiles.Count == 0)
            {
                Console.WriteLine("No video files found in the PPTX. Creating output file without compression.");
                File.Copy(pptxPath, outputPath, true);
                return;
            }

            Console.WriteLine($"Found {videoFiles.Count} video files to compress.");
            Console.WriteLine();

            // Step 6: Compress videos with ffmpeg
            Console.WriteLine("Step 6: Compressing videos with FFmpeg...");
            Console.WriteLine("-".PadRight(50, '-'));
            var compressionResults = await CompressVideosAsync(tempDir, videoFiles, settings);

            // Step 7: Replace videos in working directory
            Console.WriteLine();
            Console.WriteLine("Step 7: Replacing videos in PPTX structure...");
            FileManager.ReplaceVideosInWorkingDirectory(workingDir, tempDir, compressionResults);

            // Step 8: Update XML references (only for files that actually changed extensions)
            Console.WriteLine();
            Console.WriteLine("Step 8: Updating XML references...");
            var xmlChanges = compressionResults
                .Where(r => r.OriginalFileName != r.FinalFileName)
                .ToDictionary(r => r.OriginalFileName, r => r.FinalFileName);
            XmlReferenceUpdater.UpdateXmlReferences(workingDir, xmlChanges);

            // Step 9: Create final PPTX file
            Console.WriteLine();
            Console.WriteLine("Step 9: Creating final compressed PPTX file...");
            FileManager.CreateFinalPPTXFile(workingDir, outputPath);

            Console.WriteLine();
            Console.WriteLine($"✓ Compressed PPTX saved as: {outputPath}");

            // Show file size comparison
            ShowFileSizeComparison(pptxPath, outputPath, compressionResults);
        }
        finally
        {
            // Cleanup
            Console.WriteLine();
            FileManager.CleanupTemporaryFiles(tempDir, workingDir, zipPath);
        }
    }

    private async Task<List<VideoCompressionResult>> CompressVideosAsync(string tempDir, List<VideoFileInfo> videoFiles, UserSettings settings)
    {
        var results = new List<VideoCompressionResult>();

        for (int i = 0; i < videoFiles.Count; i++)
        {
            var video = videoFiles[i];
            string nameWithoutExt = Path.GetFileNameWithoutExtension(video.OriginalFileName);
            string outputFileName = $"{nameWithoutExt}.mp4";
            string outputPath = Path.Combine(tempDir, outputFileName);

            Console.WriteLine($"[{i + 1}/{videoFiles.Count}] Compressing: {video.OriginalFileName} -> {outputFileName}");
            Console.WriteLine($"Input: {video.TempOrigPath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine();

            var result = new VideoCompressionResult
            {
                OriginalFileName = video.OriginalFileName,
                OriginalSize = new FileInfo(video.TempOrigPath).Length
            };

            try
            {
                bool success = await EmbeddedFFmpegRunner.CompressVideoAsync(video.TempOrigPath, outputPath, settings);
                if (success && File.Exists(outputPath))
                {
                    long compressedSize = new FileInfo(outputPath).Length;
                    bool isSmaller = compressedSize < result.OriginalSize;

                    if (isSmaller)
                    {
                        // Use compressed version - it's smaller
                        result.FinalFileName = outputFileName;
                        result.WasCompressed = true;
                        result.FileSizeReduced = true;
                        result.FinalSize = compressedSize;
                        result.CompressionMethod = "GPU/CPU";
                        result.Reason = "Compressed file is smaller";

                        Console.WriteLine($"✓ Compression successful - using compressed version");
                        ShowCompressionResults(video.TempOrigPath, outputPath);
                    }
                    else
                    {
                        // Compressed file is not smaller - use original
                        result.FinalFileName = video.OriginalFileName;
                        result.WasCompressed = false;
                        result.FileSizeReduced = false;
                        result.FinalSize = result.OriginalSize;
                        result.CompressionMethod = "Original";
                        result.Reason = "Compressed file was not smaller than original";

                        // Copy original file to temp directory with final name
                        string originalFinalPath = Path.Combine(tempDir, video.OriginalFileName);
                        File.Copy(video.TempOrigPath, originalFinalPath, true);

                        Console.WriteLine($"⚠ Compressed file ({FormatFileSize(compressedSize)}) is not smaller than original ({FormatFileSize(result.OriginalSize)})");
                        Console.WriteLine("  Using original file instead.");

                        // Clean up the compressed file since we're not using it
                        if (File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }
                    }
                }
                else
                {
                    // Compression failed - use original
                    result.FinalFileName = video.OriginalFileName;
                    result.WasCompressed = false;
                    result.FileSizeReduced = false;
                    result.FinalSize = result.OriginalSize;
                    result.CompressionMethod = "Original";
                    result.Reason = "Compression failed";

                    Console.WriteLine($"✗ Failed to compress: {video.OriginalFileName}");
                    Console.WriteLine("  Using original file instead.");

                    // Keep original file
                    string fallbackPath = Path.Combine(tempDir, video.OriginalFileName);
                    File.Copy(video.TempOrigPath, fallbackPath, true);
                }
            }
            catch (Exception ex)
            {
                // Error during compression - use original
                result.FinalFileName = video.OriginalFileName;
                result.WasCompressed = false;
                result.FileSizeReduced = false;
                result.FinalSize = result.OriginalSize;
                result.CompressionMethod = "Original";
                result.Reason = $"Error during compression: {ex.Message}";

                Console.WriteLine($"✗ Error compressing {video.OriginalFileName}: {ex.Message}");
                Console.WriteLine("  Using original file instead.");

                // Keep original file
                string fallbackPath = Path.Combine(tempDir, video.OriginalFileName);
                File.Copy(video.TempOrigPath, fallbackPath, true);
            }

            results.Add(result);
            Console.WriteLine();
            Console.WriteLine("-".PadRight(50, '-'));
        }

        return results;
    }

    private void ShowCompressionResults(string originalPath, string compressedPath)
    {
        try
        {
            var originalInfo = new FileInfo(originalPath);
            var compressedInfo = new FileInfo(compressedPath);

            long originalSize = originalInfo.Length;
            long compressedSize = compressedInfo.Length;

            double compressionRatio = (double)(originalSize - compressedSize) / originalSize * 100;

            Console.WriteLine($"  Original size:  {FormatFileSize(originalSize)}");
            Console.WriteLine($"  Compressed size: {FormatFileSize(compressedSize)}");
            Console.WriteLine($"  Space saved:    {FormatFileSize(originalSize - compressedSize)} ({compressionRatio:F1}%)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Could not calculate compression ratio: {ex.Message}");
        }
    }

    private void ShowFileSizeComparison(string originalPath, string compressedPath, List<VideoCompressionResult> compressionResults)
    {
        try
        {
            var originalInfo = new FileInfo(originalPath);
            var compressedInfo = new FileInfo(compressedPath);

            long originalSize = originalInfo.Length;
            long compressedSize = compressedInfo.Length;

            double compressionRatio = (double)(originalSize - compressedSize) / originalSize * 100;

            Console.WriteLine("File Size Comparison:");
            Console.WriteLine($"  Original PPTX:   {FormatFileSize(originalSize)}");
            Console.WriteLine($"  Compressed PPTX: {FormatFileSize(compressedSize)}");
            Console.WriteLine($"  Total space saved: {FormatFileSize(originalSize - compressedSize)} ({compressionRatio:F1}%)");

            Console.WriteLine();
            Console.WriteLine("Video Compression Summary:");

            int compressedCount = 0;
            int originalKeptCount = 0;
            long totalVideoSaved = 0;

            foreach (var result in compressionResults)
            {
                if (result.WasCompressed && result.FileSizeReduced)
                {
                    compressedCount++;
                    totalVideoSaved += (result.OriginalSize - result.FinalSize);
                    Console.WriteLine($"  ✓ {result.OriginalFileName}: {FormatFileSize(result.OriginalSize)} -> {FormatFileSize(result.FinalSize)} " +
                                    $"({((double)(result.OriginalSize - result.FinalSize) / result.OriginalSize * 100):F1}% saved, {result.CompressionMethod})");
                }
                else
                {
                    originalKeptCount++;
                    Console.WriteLine($"  ⚠ {result.OriginalFileName}: {FormatFileSize(result.OriginalSize)} (kept original - {result.Reason})");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Summary: {compressedCount} videos compressed, {originalKeptCount} kept as original");
            if (totalVideoSaved > 0)
            {
                Console.WriteLine($"Total video space saved: {FormatFileSize(totalVideoSaved)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not calculate file size comparison: {ex.Message}");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        double number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:F1} {suffixes[counter]}";
    }
}