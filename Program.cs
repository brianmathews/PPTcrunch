

namespace PPTcrunch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("Usage: PPTcrunch <pptx-file>");
            Console.WriteLine("Example: PPTcrunch presentation.pptx");
            return 1;
        }

        string pptxPath = args[0];

        if (!File.Exists(pptxPath))
        {
            Console.WriteLine($"Error: File '{pptxPath}' not found.");
            return 1;
        }

        if (!Path.GetExtension(pptxPath).Equals(".pptx", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Error: File must have .pptx extension.");
            return 1;
        }

        try
        {
            // Collect user settings
            var settings = CollectUserSettings();

            var processor = new PPTXVideoProcessor();
            await processor.ProcessAsync(pptxPath, settings);
            Console.WriteLine("Video compression completed successfully!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return 1;
        }
    }

    private static UserSettings CollectUserSettings()
    {
        var settings = new UserSettings();

        Console.WriteLine();
        Console.WriteLine("Video Compression Settings");
        Console.WriteLine("==========================");

        // Collect maximum width
        Console.Write($"Enter maximum video width in pixels (default: {settings.MaxWidth}): ");
        string? widthInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(widthInput) && int.TryParse(widthInput, out int width) && width > 0)
        {
            settings.MaxWidth = width;
        }

        // Collect codec preference
        Console.WriteLine();
        Console.WriteLine("Video codec options:");
        Console.WriteLine("  1. H.264 (better compatibility, standard quality)");
        Console.WriteLine("  2. H.265 (better compression, newer standard)");
        Console.Write("Enter your choice (1 or 2, default: 2): ");
        string? codecInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(codecInput) && int.TryParse(codecInput, out int codecChoice))
        {
            if (codecChoice == 1)
            {
                settings.Codec = VideoCodec.H264;
                settings.Quality = 23; // H.264 default quality
            }
            // Default to H265 for any other input (including empty input)
        }

        // Collect quality setting
        Console.WriteLine();
        Console.WriteLine("Video quality levels (lower = better quality, larger files):");
        if (settings.Codec == VideoCodec.H265)
        {
            Console.WriteLine("For H.265 GPU encoding (GTX cards with slow preset):");
            Console.WriteLine("  15-19: Nearly indistinguishable from source (18 recommended for technical drawings)");
            Console.WriteLine("  20-24: Very high quality - minor differences visible under scrutiny");
            Console.WriteLine("  25-29: High quality - good balance of quality and file size (26 recommended)");
            Console.WriteLine("  30-35: Medium quality - noticeable but acceptable quality loss");
        }
        else
        {
            Console.WriteLine("For H.264 CPU encoding:");
            Console.WriteLine("  18-22: Very high quality (23 recommended)");
            Console.WriteLine("  23-28: High quality - good balance of quality and file size");
            Console.WriteLine("  29-35: Medium quality - noticeable but acceptable quality loss");
        }
        Console.Write($"Enter quality level (15-35, default: {settings.Quality}): ");
        string? qualityInput = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(qualityInput) && int.TryParse(qualityInput, out int quality) && quality >= 15 && quality <= 35)
        {
            settings.Quality = quality;
        }

        // Display selected settings
        Console.WriteLine();
        Console.WriteLine("Selected settings:");
        Console.WriteLine($"  Maximum width: {settings.MaxWidth} pixels");
        Console.WriteLine($"  Video codec: {settings.GetCodecDisplayName()}");
        Console.WriteLine($"  Quality level: {settings.Quality}");
        Console.WriteLine();

        return settings;
    }
}
