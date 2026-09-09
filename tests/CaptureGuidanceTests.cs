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

        Check(CaptureGuidance.RateText(30.00003) == "30" && CaptureGuidance.RateText(59.94018) == "59.94", "friendly labels remove device quantization noise");
        Check(CaptureGuidance.RateLabel(60.00024) == "60 fps (device reports 60.00024)",
            "supported-rate list preserves the device's exact advertised timing");

        double[] rates = { 120.00048, 60.00024, 59.94018, 50, 30.00003, 29.97 };
        Check(rates[CaptureGuidance.DefaultRate(rates)] == 30.00003, "30 fps is the default when supported");
        double[] ntscOnly = { 120, 59.94, 29.97, 24 };
        Check(ntscOnly[CaptureGuidance.DefaultRate(ntscOnly)] == 29.97, "29.97 fps is the fallback default when 30 is unavailable");
        double[] noThirty = { 60, 25, 24 };
        Check(noThirty[CaptureGuidance.DefaultRate(noThirty)] == 25, "the closest available rate to 30 is the final fallback");
        var mode = new CaptureSelection(new("test", "test", true), 1920, 1080, rates[0], "pixel_format", "0rgb");
        var input = CaptureSupport.Input(mode, true);
        Check(input[input.IndexOf("-framerate") + 1] == "120.00048", "nominal display labels never change the actual device request");
        Console.WriteLine("PASS: capture color ordering, 30/29.97 default selection, and device timing preservation.");
    }
}
