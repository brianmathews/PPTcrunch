using System.IO.Compression;

namespace PPTcrunch;

public static class FileManager
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mpeg4", ".mov" };

    public static void CreateBackupAndWorkingDirectories(string pptxPath, out string zipPath, out string tempDir, out string workingDir)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(pptxPath)) ?? throw new InvalidOperationException("Cannot determine directory");
        string baseName = Path.GetFileNameWithoutExtension(pptxPath);

        zipPath = Path.Combine(baseDir, $"{baseName}.zip");
        tempDir = Path.Combine(baseDir, "PPT-temp");
        workingDir = Path.Combine(baseDir, "PPTX-working");

        // Step 1: Copy PPTX to ZIP
        Console.WriteLine("Step 1: Creating backup and copying PPTX to ZIP file...");
        File.Copy(pptxPath, zipPath, true);

        // Step 2-3: Create temp directories
        Console.WriteLine("Step 2-3: Creating temporary directories...");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        if (Directory.Exists(workingDir)) Directory.Delete(workingDir, true);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(workingDir);
    }

    public static void ExtractPPTXToWorkingDirectory(string zipPath, string workingDir)
    {
        Console.WriteLine("Extracting PPTX contents to working directory...");
        ZipFile.ExtractToDirectory(zipPath, workingDir);
    }

    public static List<VideoFileInfo> ExtractAndRenameVideos(string workingDir, string tempDir)
    {
        var videoFiles = new List<VideoFileInfo>();
        string mediaDir = Path.Combine(workingDir, "ppt", "media");

        if (!Directory.Exists(mediaDir))
        {
            Console.WriteLine("No media directory found in PPTX.");
            return videoFiles;
        }

        foreach (string file in Directory.GetFiles(mediaDir))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (VideoExtensions.Contains(extension))
            {
                string fileName = Path.GetFileName(file);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                string origFileName = $"{nameWithoutExt}-orig{extension}";
                string origFilePath = Path.Combine(tempDir, origFileName);

                File.Copy(file, origFilePath);

                videoFiles.Add(new VideoFileInfo
                {
                    OriginalFileName = fileName,
                    OriginalExtension = extension,
                    OriginalPath = file,
                    TempOrigPath = origFilePath,
                    TempOrigFileName = origFileName
                });

                Console.WriteLine($"  Extracted: {fileName} -> {origFileName}");
            }
        }

        return videoFiles;
    }

    public static void ReplaceVideosInWorkingDirectory(string workingDir, string tempDir, List<VideoCompressionResult> compressionResults)
    {
        string mediaDir = Path.Combine(workingDir, "ppt", "media");

        foreach (var result in compressionResults)
        {
            string originalFileName = result.OriginalFileName;
            string finalFileName = result.FinalFileName;
            string originalPath = Path.Combine(mediaDir, originalFileName);
            string newTempPath = Path.Combine(tempDir, finalFileName);

            if (File.Exists(newTempPath))
            {
                // Delete original file
                if (File.Exists(originalPath))
                {
                    File.Delete(originalPath);
                }

                // Copy the final file (either compressed or original) to media directory
                string targetPath = Path.Combine(mediaDir, finalFileName);
                File.Copy(newTempPath, targetPath, true);

                if (result.WasCompressed && result.FileSizeReduced)
                {
                    Console.WriteLine($"  Replaced with compressed: {originalFileName} -> {finalFileName} ({result.CompressionMethod})");
                }
                else
                {
                    Console.WriteLine($"  Kept original: {originalFileName} ({result.Reason})");
                }
            }
            else
            {
                Console.WriteLine($"  Warning: Final file not found: {newTempPath}");
            }
        }
    }

    public static void CreateFinalPPTXFile(string workingDir, string outputPath)
    {
        Console.WriteLine("Creating final compressed PPTX file...");
        if (File.Exists(outputPath)) File.Delete(outputPath);
        ZipFile.CreateFromDirectory(workingDir, outputPath);
    }

    public static void CleanupTemporaryFiles(string tempDir, string workingDir, string zipPath)
    {
        Console.WriteLine("Cleaning up temporary files...");

        // Track cleanup results
        bool tempDirCleaned = false;
        bool workingDirCleaned = false;
        bool zipFileCleaned = false;

        // Clean up temp directory
        try
        {
            if (Directory.Exists(tempDir))
            {
                // Force garbage collection to release any file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Directory.Delete(tempDir, true);
                tempDirCleaned = true;
                Console.WriteLine($"  ✓ Deleted temp directory: {Path.GetFileName(tempDir)}");
            }
            else
            {
                tempDirCleaned = true; // Already cleaned up
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not delete temp directory '{Path.GetFileName(tempDir)}': {ex.Message}");
            Console.WriteLine($"    You may need to manually delete: {tempDir}");
        }

        // Clean up working directory
        try
        {
            if (Directory.Exists(workingDir))
            {
                // Force garbage collection to release any file handles
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Directory.Delete(workingDir, true);
                workingDirCleaned = true;
                Console.WriteLine($"  ✓ Deleted working directory: {Path.GetFileName(workingDir)}");
            }
            else
            {
                workingDirCleaned = true; // Already cleaned up
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not delete working directory '{Path.GetFileName(workingDir)}': {ex.Message}");
            Console.WriteLine($"    You may need to manually delete: {workingDir}");
        }

        // Clean up ZIP file
        try
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
                zipFileCleaned = true;
                Console.WriteLine($"  ✓ Deleted ZIP file: {Path.GetFileName(zipPath)}");
            }
            else
            {
                zipFileCleaned = true; // Already cleaned up
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Could not delete ZIP file '{Path.GetFileName(zipPath)}': {ex.Message}");
            Console.WriteLine($"    You may need to manually delete: {zipPath}");
        }

        // Summary
        if (tempDirCleaned && workingDirCleaned && zipFileCleaned)
        {
            Console.WriteLine("  ✓ All temporary files cleaned up successfully");
        }
        else
        {
            Console.WriteLine("  ⚠ Some temporary files could not be cleaned up automatically");
            Console.WriteLine($"    Note: FFmpeg binaries are kept at {EmbeddedFFmpegRunner.GetPreferredFFmpegDirectory()} for performance (reuse on next run)");
        }
    }

    public static string GetOutputPath(string pptxPath)
    {
        string baseDir = Path.GetDirectoryName(Path.GetFullPath(pptxPath)) ?? throw new InvalidOperationException("Cannot determine directory");
        string baseName = Path.GetFileNameWithoutExtension(pptxPath);
        return Path.Combine(baseDir, $"{baseName}-shrunk.pptx");
    }
}