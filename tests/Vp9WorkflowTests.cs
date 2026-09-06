using PPTcrunch;

internal static class Vp9WorkflowTests
{
    public static async Task RunAsync()
    {
        static void Check(bool ok, string message)
        { if (!ok) throw new Exception(message); }

        string root = Path.Combine(Path.GetTempPath(), $"PPTcrunch pass tests {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var logs = new HashSet<string>();
        try
        {
            foreach (string outcome in new[] { "success", "first-failure", "second-failure", "cancel" })
            {
                string output = Path.Combine(root, "existing.webm");
                await File.WriteAllTextAsync(output, "previous export");
                var settings = new UserSettings { Codec = VideoCodec.VP9, MaxWidth = 641, ReduceHighFrameRates = true };
                var final = EncodingArguments.Build(settings, HardwareAccelerationMode.None, inputFrameRate: 60);
                final.AddRange(new[] { "-c:a:0", "copy", "-b:a:1", "384k" });
                List<string>? analysis = null;
                string? work = null;
                int calls = 0;
                bool success = false;
                try
                {
                    success = await Vp9TwoPassEncoder.RunAsync("input.mkv", output, settings, final,
                        async (label, options, destination) =>
                        {
                            calls++;
                            string Value(string option) => options[options.IndexOf(option) + 1];
                            string log = Value("-passlogfile").Trim('"');
                            work = Path.GetDirectoryName(log)!;
                            Check(await File.ReadAllTextAsync(output) == "previous export", "Existing output must survive until both passes succeed");
                            Check(Value("-b:v") == "0", "Both passes use uncapped quality mode");
                            if (calls == 1)
                            {
                                Check(logs.Add(log), "Each job must own a unique pass log");
                                Check(Value("-pass") == "1" && Value("-cpu-used") == "4", "Fast first-pass analysis");
                                Check(options.Contains("-an") && !options.Contains("0:a?") && !options.Contains("-c:a"), "Do not encode audio in pass one");
                                Check(Value("-f") == "null" && destination == (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"), "Analysis output goes to the null muxer");
                                analysis = options;
                                await File.WriteAllTextAsync(log + "-0.log", "statistics");
                                return outcome != "first-failure";
                            }
                            Check(calls == 2 && Value("-pass") == "2", "Analysis precedes exactly one final pass");
                            Check(Value("-cpu-used") == "2" && Value("-f") == "webm", "Final VP9 settings");
                            foreach (string key in new[] { "-vf", "-crf", "-pix_fmt", "-g", "-fps_mode", "-passlogfile" })
                                Check(Value(key) == analysis![analysis.IndexOf(key) + 1], "Consistent video and statistics in both passes: " + key);
                            Check(Value("-c:a:0") == "copy" && Value("-b:a:1") == "384k", "Preserve per-track audio decisions only in the final pass");
                            await File.WriteAllTextAsync(destination, "new export");
                            if (outcome == "cancel") throw new OperationCanceledException();
                            return outcome != "second-failure";
                        });
                }
                catch (OperationCanceledException) when (outcome == "cancel") { }
                Check(success == (outcome == "success"), "Workflow result: " + outcome);
                Check(calls == (outcome == "first-failure" ? 1 : 2), "No final pass after failed analysis");
                Check(await File.ReadAllTextAsync(output) == (success ? "new export" : "previous export"), "Failed/canceled jobs preserve earlier exports");
                Check(work != null && !Directory.Exists(work), "Clean up statistics and partial output: " + outcome);
                Check(!final.Contains("-pass"), "Do not mutate the caller's argument list");
            }
            string leftover = Path.Combine(root, "empty-after-partial-cleanup");
            Directory.CreateDirectory(leftover);
            await File.WriteAllTextAsync(Path.Combine(leftover, "stats-0.log"), "statistics");
            int attempts = 0;
            await Vp9TwoPassEncoder.CleanupAsync(leftover, path =>
            {
                attempts++;
                if (attempts == 1)
                {
                    File.Delete(Path.Combine(path, "stats-0.log"));
                    throw new IOException("Simulated transient failure deleting the now-empty directory");
                }
                Check(!Directory.EnumerateFileSystemEntries(path).Any(), "Retry cleanup of an empty leftover folder");
                if (attempts == 2) throw new UnauthorizedAccessException("Simulated transient access failure");
                Directory.Delete(path, recursive: true);
            });
            Check(attempts == 3 && !Directory.Exists(leftover), "Retry transient cleanup failures until the empty folder is removed");
            await Vp9TwoPassEncoder.CleanupAsync(leftover); // Already removed is success.

            attempts = 0;
            await Vp9TwoPassEncoder.CleanupAsync(leftover, _ =>
            {
                attempts++;
                throw new IOException("Simulated persistent lock (expected test warning)");
            });
            Check(attempts == 6, "Permanent cleanup failure must stop after bounded retries");
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine("PASS: VP9 pass ordering, audio routing, unique statistics, failure/cancellation cleanup, transient cleanup retries and output preservation.");
    }
}
