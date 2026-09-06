using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace PPTcrunch;

internal static class FrameRatePolicy
{
    internal static double? TargetRate(UserSettings settings, double? inputRate)
        => settings.ReduceHighFrameRates && inputRate is >= 48 && double.IsFinite(inputRate.Value)
            && inputRate.Value % 2 == 0 ? inputRate.Value / 2 : null;

    internal static double? ParseRate(string? value)
    {
        var parts = (value ?? "").Split('/');
        if (parts.Length is < 1 or > 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double numerator)) return null;
        double denominator = 1;
        if (parts.Length == 2 && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out denominator)) return null;
        double rate = numerator / denominator;
        return rate > 0 && double.IsFinite(rate) ? rate : null;
    }

    internal static async Task<double?> ReadAsync(string inputPath, string ffprobe)
    {
        var info = new ProcessStartInfo(ffprobe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
                     "stream=avg_frame_rate,r_frame_rate", "-of", "json", inputPath })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Could not start FFprobe.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        string output = await stdout, error = await stderr;
        if (process.ExitCode != 0) throw new IOException($"Could not inspect video frame rate: {error.Trim()}");
        using var doc = JsonDocument.Parse(output);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new IOException("No video stream found.");
        var video = streams[0];
        double? Rate(string key) => video.TryGetProperty(key, out var item) ? ParseRate(item.GetString()) : null;
        return Rate("avg_frame_rate") ?? Rate("r_frame_rate");
    }
}
