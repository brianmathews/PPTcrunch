using System.Diagnostics;
using System.Reflection;
using PPTcrunch;

internal static class CapturePipeTests
{
    internal static void Child(string mode)
    {
        if (mode == "timeout")
        {
            Console.Error.WriteLine($"child-pid:{Environment.ProcessId}");
            Console.Error.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
        else if (mode == "lines")
        {
            Console.Error.Write("first\rsecond\nlast without newline");
        }
        else
        {
            string chunk = new('x', 8192);
            // Well above pipe capacity. Both streams must drain concurrently,
            // including the final characters written immediately before exit.
            for (int i = 0; i < 128; i++)
            {
                Console.Out.Write(chunk);
                Console.Error.Write(chunk);
            }
            Console.Out.Write("stdout-end");
            Console.Error.Write("stderr-end");
        }
    }

    internal static async Task RunAsync()
    {
        string exe = Environment.ProcessPath ?? throw new Exception("Missing test executable path.");
        string[] Arguments(string mode) => (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? new[] { Assembly.GetExecutingAssembly().Location } : Array.Empty<string>())
            .Concat(new[] { "--capture-pipe-child", mode }).ToArray();
        var result = await CaptureSupport.Probe(exe, Arguments("large"), 15);
        if (result.Code != 0 || result.Output.Length != 128 * 8192 + 10 || result.Error.Length != 128 * 8192 + 10 ||
            !result.Output.EndsWith("stdout-end") || !result.Error.EndsWith("stderr-end"))
            throw new Exception("Concurrent capture pipe drains lost output or failed.");

        using (var proc = Process.Start(CaptureSupport.StartInfo(exe, Arguments("lines"), true))!)
        {
            var lines = new List<string>();
            var stdout = CaptureSupport.ReadOutput(proc.StandardOutput);
            var stderr = CaptureSupport.ReadLines(proc.StandardError, lines.Add);
            await Task.WhenAll(proc.WaitForExitAsync(), stdout, stderr);
            if (!lines.SequenceEqual(new[] { "first", "second", "last without newline" }))
                throw new Exception("Recording diagnostics lost progress lines or final unterminated output.");
        }

        var watch = Stopwatch.StartNew();
        try
        {
            await CaptureSupport.Probe(exe, Arguments("timeout"), 1);
            throw new Exception("Capture probe timeout was not enforced.");
        }
        catch (IOException ex) when (ex.Message.Contains("Capture probe timed out"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(ex.Message, @"child-pid:(\d+)");
            if (!match.Success || watch.Elapsed > TimeSpan.FromSeconds(10))
                throw new Exception("Timeout did not drain diagnostics or promptly finish.");
            try
            {
                using var child = Process.GetProcessById(int.Parse(match.Groups[1].Value));
                if (!child.HasExited) throw new Exception("Timed-out capture child is still running.");
            }
            catch (ArgumentException) { /* Process has already been reaped. */ }
        }
        Console.WriteLine("PASS: concurrent capture pipe drains, progress lines, EOF and timeout cleanup.");
    }
}
