using PPTcrunch;

internal static class CaptureGuidanceTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Capture guidance: " + message);
    }

    internal static void Run()
    {
        var colors = CaptureGuidance.SortColors(new[] { "nv12", "uyvy422", "yuyv422", "0rgb", "bgr0" }
            .Select(f => ("pixel_format", f)));
        Check(colors.Select(c => c.Format).SequenceEqual(new[] { "0rgb", "bgr0", "uyvy422", "yuyv422", "nv12" }),
            "actual card formats sort by color detail while retaining equal-quality order");
        Check(CaptureGuidance.ColorRank("pixel_format", "rgb24") == CaptureGuidance.ColorRank("pixel_format", "bgr0"),
            "padding bytes do not raise color precision");
        var varied = CaptureGuidance.SortColors(new[] { "gray", "nv12", "yuv422p10le", "rgb24", "gbrp10le", "p010le", "monob" }
            .Select(f => ("pixel_format", f)).Append(("vcodec", "mjpeg")));
        Check(varied.Select(c => c.Format).SequenceEqual(new[] { "gbrp10le", "rgb24", "yuv422p10le", "p010le", "nv12", "gray", "monob", "mjpeg" }),
            "spatial color detail precedes precision; unrankable compressed formats follow");
        Check(CaptureGuidance.ColorRank("pixel_format", "yuv444p16le").Precision == 48 &&
              CaptureGuidance.ColorRank("pixel_format", "rgba64le").Precision == 48, "16-bit color retained in ranking");
        Check(CaptureGuidance.ColorRank("pixel_format", "unknown").Detail == -1, "unknown quality is not guessed");

        var sixty = CaptureGuidance.ParseSourceTiming("60");
        var ntsc = CaptureGuidance.ParseSourceTiming("60000/1001");
        Check(CaptureGuidance.ParseSourceTiming("59.94") == ntsc, "decimal NTSC input agrees with exact fraction");
        foreach (string invalid in new[] { "0", "-60", "NaN", "Infinity", "60/0", "60/-1", "abc", "60/1/1", "1001" })
        {
            bool rejected = false;
            try { CaptureGuidance.ParseSourceTiming(invalid); }
            catch (FormatException) { rejected = true; }
            Check(rejected, "reject invalid source rate: " + invalid);
        }
        Check(CaptureGuidance.Classify(30.00003, sixty) is { Cadence: CaptureCadence.EvenReduction, Factor: 2 }, "60 to quantized 30 is even");
        Check(CaptureGuidance.Classify(29.97, ntsc) is { Cadence: CaptureCadence.EvenReduction, Factor: 2 }, "59.94 to 29.97 is even");
        Check(CaptureGuidance.Classify(29.97, sixty).Cadence == CaptureCadence.Uneven, "60 to 29.97 must warn");
        Check(CaptureGuidance.Classify(30.00003, ntsc).Cadence == CaptureCadence.Uneven, "59.94 to 30 must warn");
        Check(CaptureGuidance.Classify(24, sixty).Cadence == CaptureCadence.Uneven, "60 to 24 has uneven drop cadence");
        Check(CaptureGuidance.Classify(60, CaptureGuidance.ParseSourceTiming("24")).Cadence == CaptureCadence.Uneven, "24 to 60 has uneven repeat cadence");
        Check(CaptureGuidance.Classify(120.00048, CaptureGuidance.ParseSourceTiming("24")) is { Cadence: CaptureCadence.EvenRepeat, Factor: 5 }, "24 to 120 is even repeats");
        Check(CaptureGuidance.Classify(25, CaptureGuidance.ParseSourceTiming("50")) is { Cadence: CaptureCadence.EvenReduction, Factor: 2 }, "50 to 25 is even");
        Check(CaptureGuidance.Classify(48, CaptureGuidance.ParseSourceTiming("144")) is { Cadence: CaptureCadence.EvenReduction, Factor: 3 }, "non-power-of-two reduction is even");
        Check(CaptureGuidance.Classify(30.003, sixty).Cadence == CaptureCadence.Uneven, "tolerance does not hide larger timing mismatches");
        Check(CaptureGuidance.RateText(30.00003) == "30" && CaptureGuidance.RateText(59.94018) == "59.94", "friendly labels remove device quantization noise");

        double[] rates = { 120.00048, 60.00024, 59.94018, 50, 30.00003, 29.97 };
        var choices = CaptureGuidance.RateChoices(rates, sixty);
        Check(choices[0].Rate == 60.00024 && choices[1].Rate == 30.00003 && choices[2].Rate == 120.00048,
            "source match, even reduction, and even repeats are preferred in that order");
        Check(CaptureGuidance.DefaultRate(choices, sixty) == 0, "default matches source when available");
        var fourK = CaptureGuidance.RateChoices(new[] { 30.00003, 29.97 }, ntsc);
        Check(fourK[0].Rate == 29.97 && fourK[0].Cadence == CaptureCadence.EvenReduction,
            "4K recommendation uses only rates available at 4K and distinguishes NTSC");
        var noEvenRate = CaptureGuidance.RateChoices(new[] { 25.0, 30.0 }, CaptureGuidance.ParseSourceTiming("24"));
        Check(noEvenRate.All(c => c.Cadence == CaptureCadence.Uneven), "no false recommendation when nothing divides evenly");
        var unknown = CaptureGuidance.ParseSourceTiming("");
        var unknownChoices = CaptureGuidance.RateChoices(rates, unknown);
        Check(unknownChoices.All(c => c.Cadence == CaptureCadence.Unknown) &&
              unknownChoices[CaptureGuidance.DefaultRate(unknownChoices, unknown)].Rate == 30.00003, "unknown source keeps neutral 30 fps default");
        var variable = CaptureGuidance.ParseSourceTiming("VRR");
        Check(variable.Variable && CaptureGuidance.RateChoices(rates, variable).All(c => c.Cadence == CaptureCadence.Unknown), "VRR never claims fixed cadence");
        Check(CaptureGuidance.DescribeRate(choices[0], sixty).Contains("60.00024"), "labels retain exact device timing for review");
        var mode = new CaptureSelection(new("test", "test", true), 1920, 1080, choices[0].Rate, "pixel_format", "0rgb");
        var input = CaptureSupport.Input(mode, true);
        Check(input[input.IndexOf("-framerate") + 1] == "60.00024", "nominal matching never changes the actual device request");
        Console.WriteLine("PASS: capture color ordering, source-rate parsing, cadence recommendations, NTSC separation and device timing preservation.");
    }
}
