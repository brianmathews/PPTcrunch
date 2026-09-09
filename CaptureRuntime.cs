using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PPTcrunch;

internal static class CaptureRuntime
{
    // This snapshot fixes AVFoundation selecting a frame-rate range from one
    // device format and then applying it to a different format (observed as 5fps
    // instead of 30fps on a USB card). It also adds backpressure to frame delivery.
    // Keep capture separate from the legacy binary used for offline compression.
    internal const string MacBuild = "1788701347_N-126416-g9997fd0606";
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    internal static async Task<string> ExecutableAsync()
    {
        if (!OperatingSystem.IsMacOS())
            return await EmbeddedFFmpegRunner.GetFFmpegExecutablePathAsync() ?? throw new IOException("FFmpeg unavailable.");
        if (RuntimeInformation.OSArchitecture != Architecture.Arm64)
            throw new NotSupportedException("macOS capture currently requires Apple silicon.");
        string directory = Path.Combine(Path.GetDirectoryName(EmbeddedFFmpegRunner.GetPreferredFFmpegDirectory())!, "capture-ffmpeg", MacBuild);
        await DownloadLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PPTcrunch/1.0");
            foreach (var (name, hash) in new[]
            {
                ("ffmpeg", "64f1ad87071be6883ba8ba3fe01412baa9ae1891a50f28449e14a1280132deda"),
                ("ffprobe", "c068f1ea299413c7aa822efb67517bf5bc63c7684e7383ed9af5b66c55e7f728")
            })
            {
                string destination = Path.Combine(directory, name);
                if (File.Exists(destination)) continue;
                Console.WriteLine($"Downloading {name} for macOS capture (one time)...");
                string temporary = Path.Combine(directory, ".download-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                try
                {
                    string archive = Path.Combine(temporary, name + ".zip");
                    using (var response = await client.GetAsync($"https://ffmpeg.martin-riedl.de/download/macos/arm64/{MacBuild}/{name}.zip", HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        await using var file = File.Create(archive);
                        await response.Content.CopyToAsync(file);
                    }
                    await using (var file = File.OpenRead(archive))
                    {
                        string actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
                        if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException($"Checksum mismatch for capture {name}.");
                    }
                    using var zip = ZipFile.OpenRead(archive);
                    var entry = zip.GetEntry(name) ?? throw new IOException($"Missing {name} in capture download.");
                    string executable = Path.Combine(temporary, name);
                    entry.ExtractToFile(executable);
                    File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                    // Atomic publication; another app process may have downloaded it too.
                    try { File.Move(executable, destination); }
                    catch (IOException) when (File.Exists(destination)) { }
                }
                finally { Directory.Delete(temporary, true); }
            }
            return Path.Combine(directory, "ffmpeg");
        }
        finally { DownloadLock.Release(); }
    }
}
