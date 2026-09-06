using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PPTcrunch.Tests")]

namespace PPTcrunch;

/// <summary>Whole-video VP9 analysis followed by the final quality-mode encode.</summary>
public static class Vp9TwoPassEncoder
{
    public static async Task<bool> RunAsync(
        string inputPath, string outputPath, UserSettings settings, List<string> finalArguments,
        Func<string, List<string>, string, Task<bool>> runPass)
    {
        if (settings.Codec != VideoCodec.VP9) throw new ArgumentException("Two-pass workflow requires VP9.");
        string destination = Path.GetFullPath(outputPath);
        if (string.Equals(Path.GetFullPath(inputPath), destination,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Input and output must be different files.");

        // Each job owns its statistics and staged output. Keep them beside the final
        // output so publishing the successful file does not require a cross-volume copy.
        string work = Path.Combine(Path.GetDirectoryName(destination)!, $".pptcrunch-vp9-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        try
        {
            string passLog = $"\"{Path.Combine(work, "stats")}\"";
            var first = EncodingArguments.Build(settings, HardwareAccelerationMode.None, videoOnly: true);
            // Both passes must analyze exactly the same frames, including the
            // per-input frame-rate decision already made for the final pass.
            first[first.IndexOf("-vf") + 1] = finalArguments[finalArguments.IndexOf("-vf") + 1];
            first[first.IndexOf("-cpu-used") + 1] = "4";
            first.AddRange(new[] { "-pass", "1", "-passlogfile", passLog });
            string nullOutput = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            if (!await runPass("VP9 pass 1/2: analyzing video", first, nullOutput)) return false;

            var second = new List<string>(finalArguments);
            second.AddRange(new[] { "-pass", "2", "-passlogfile", passLog });
            string staged = Path.Combine(work, "output.webm");
            if (!await runPass("VP9 pass 2/2: encoding final video", second, staged)) return false;
            if (!File.Exists(staged) || new FileInfo(staged).Length == 0) return false;
            File.Move(staged, destination, overwrite: true);
            return true;
        }
        finally
        {
            await CleanupAsync(work);
        }
    }

    internal static async Task CleanupAsync(string work, Action<string>? deleteDirectory = null)
    {
        deleteDirectory ??= path => Directory.Delete(path, recursive: true);
        // Deleting the contents can succeed before deletion of the directory
        // fails (for example, while a scanner briefly holds it). Retry the whole
        // operation, even if the directory is now empty. Do not use the encode's
        // cancellation token: canceled exports still need their cleanup.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                deleteDirectory(work);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5)
                {
                    Console.WriteLine($"Could not remove VP9 temporary files in '{work}' after 6 attempts: {ex.Message}");
                    return;
                }
                await Task.Delay(100 * (1 << attempt));
            }
        }
    }
}
