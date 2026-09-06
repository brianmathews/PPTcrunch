using System.Text.Json;
using PPTcrunch;

internal static class FrameRateTests
{
    public static async Task RunAsync(string ffmpeg, string ffprobe, string artifacts,
        Func<string, string, Task<string>> run, bool nvenc)
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var sources = new Dictionary<string, string>();
        foreach (string rate in new[] { "24000/1001", "24", "25", "30000/1001", "30", "40", "48000/1001", "48", "49", "50", "51", "60000/1001", "60", "90", "120" })
        {
            string source = Path.Combine(artifacts, $"fps-source-{rate.Replace('/', '-')}.mkv");
            await run(ffmpeg, $"-v error -f lavfi -i testsrc2=size=320x180:rate={rate}:duration=1 -f lavfi -i sine=duration=1 -c:v ffv1 -c:a pcm_s16le -y \"{source}\"");
            sources[rate] = source;
        }

        async Task Verify(string rate, VideoCodec codec, bool reduce = true,
            HardwareAccelerationMode hardware = HardwareAccelerationMode.None, bool external = false)
        {
            var settings = new UserSettings { Codec = codec, ReduceHighFrameRates = reduce,
                HardwareAcceleration = hardware, UseGPUAcceleration = hardware != HardwareAccelerationMode.None };
            string output = Path.Combine(artifacts, $"fps-{rate.Replace('/', '-')}-{codec}-{reduce}-{hardware}-{external}{settings.OutputExtension}");
            bool success;
            if (external) success = await FFmpegRunner.CompressVideoAsync(sources[rate], output, settings);
            else
            {
                var result = await EmbeddedFFmpegRunner.CompressVideoWithResultAsync(sources[rate], output, settings);
                success = result.Success && result.HardwareAcceleration == hardware;
            }
            Check(success, "Frame-rate export: " + output);
            double inputRate = FrameRatePolicy.ParseRate(rate)!.Value;
            double expected = reduce && inputRate >= 48 && inputRate % 2 == 0 ? inputRate / 2 : inputRate;
            using var doc = JsonDocument.Parse(await run(ffprobe, $"-v error -count_frames -show_streams -show_format -of json \"{output}\""));
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var video = streams.First(s => s.GetProperty("codec_type").GetString() == "video");
            double actual = FrameRatePolicy.ParseRate(video.GetProperty("avg_frame_rate").GetString())
                ?? FrameRatePolicy.ParseRate(video.GetProperty("r_frame_rate").GetString())!.Value;
            Check(Math.Abs(actual - expected) < .002, $"Expected {expected} FPS, got {actual}: {output}");
            int count = int.Parse(video.GetProperty("nb_read_frames").GetString()!);
            Check(Math.Abs(count - expected) <= 1, "Frame count reflects preserved playback speed: " + output);
            double duration = double.Parse(doc.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            Check(Math.Abs(duration - 1) < .08, "Duration preserved within one frame/audio padding: " + output);
            Check(streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), "Audio retained");
        }

        foreach (string rate in sources.Keys) await Verify(rate, VideoCodec.VP9);
        foreach (var codec in Enum.GetValues<VideoCodec>())
        {
            if (codec != VideoCodec.VP9)
            {
                await Verify("60", codec);
                await Verify("50", codec);
                await Verify("40", codec);
                await Verify("60000/1001", codec);
                await Verify("30000/1001", codec);
            }
            await Verify("60", codec, reduce: false);
            await Verify("60", codec, external: true);
            await Verify("50", codec, external: true);
            if (nvenc && codec != VideoCodec.VP9)
                await Verify("60", codec, hardware: HardwareAccelerationMode.NvidiaNvenc);
        }
        await Verify("30000/1001", VideoCodec.VP9, external: true);
        await Verify("60", VideoCodec.VP9, reduce: false, external: true);

        // Verify the conversion itself retains complete source images rather than
        // introducing blended frames, independently of lossy video encoding.
        string Hashes(string text) => string.Join('\n', text.Split('\n').Where(l => !l.StartsWith('#') && l.Contains(','))
            .Select(l => l.Split(',').Last().Trim()));
        string original = Hashes(await run(ffmpeg, $"-v error -i \"{sources["60"]}\" -map 0:v:0 -an -f framemd5 -"));
        var options = EncodingArguments.Build(new UserSettings { Codec = VideoCodec.VP9, ReduceHighFrameRates = true }, HardwareAccelerationMode.None, inputFrameRate: 60);
        string filter = options[options.IndexOf("-vf") + 1];
        string selected = Hashes(await run(ffmpeg, $"-v error -i \"{sources["60"]}\" -vf {filter} -fps_mode passthrough -an -f framemd5 -"));
        var originals = original.Split('\n').ToHashSet();
        Check(selected.Split('\n').Length == 30 && selected.Split('\n').All(originals.Contains), "30 FPS filter selects sharp original frames without blending");
        Console.WriteLine("PASS: lower/odd/fractional rates preserved; 48/50/60/90/120 halved; opt-out, CPU/GPU, both runners, duration/audio and unblended frames.");
    }
}
