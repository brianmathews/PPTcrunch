using System.Globalization;
using System.Text.RegularExpressions;

namespace PPTcrunch;

internal enum CaptureCadence { Match, EvenReduction, EvenRepeat, Uneven, Unknown }
internal record CaptureSourceTiming(double? Rate, bool Variable = false);
internal record CaptureRateChoice(double Rate, CaptureCadence Cadence, int Factor);

internal static class CaptureGuidance
{
    // UVC frame intervals are quantized. Ignore a few ppm without conflating
    // 30 with 30000/1001 (a roughly 1000 ppm difference).
    private const double TimingTolerance = 0.00002;

    internal static double NominalRate(double rate)
    {
        double[] candidates = { Math.Round(rate), 24000.0 / 1001, 30000.0 / 1001,
            48000.0 / 1001, 60000.0 / 1001, 120000.0 / 1001, 240000.0 / 1001 };
        double closest = candidates.Where(c => c > 0).MinBy(c => Math.Abs(c - rate));
        return Math.Abs(closest / rate - 1) <= TimingTolerance ? closest : rate;
    }

    internal static string RateText(double rate)
    {
        double nominal = NominalRate(rate);
        // Display familiar NTSC labels, but preserve nonstandard values in full.
        return nominal != rate || new[] { 24000.0 / 1001, 30000.0 / 1001, 48000.0 / 1001,
            60000.0 / 1001, 120000.0 / 1001, 240000.0 / 1001 }.Contains(nominal)
            ? nominal.ToString("0.###", CultureInfo.InvariantCulture) : CaptureSupport.Number(rate);
    }

    internal static CaptureSourceTiming ParseSourceTiming(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return new(null);
        if (text.Equals("v", StringComparison.OrdinalIgnoreCase) || text.Equals("vrr", StringComparison.OrdinalIgnoreCase))
            return new(null, true);
        var parts = text.Split('/');
        if (parts.Length > 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            throw new FormatException("Enter a positive FPS value such as 60, 59.94 or 60000/1001; V for variable; or ENTER if unknown.");
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double denominator) || denominator <= 0)
                throw new FormatException("The frame-rate denominator must be positive.");
            rate /= denominator;
        }
        if (!double.IsFinite(rate) || rate <= 0 || rate > 1000)
            throw new FormatException("Source FPS must be greater than 0 and at most 1000.");
        return new(NominalRate(rate));
    }

    internal static CaptureRateChoice Classify(double capture, CaptureSourceTiming source)
    {
        if (source.Rate is not double sourceRate || source.Variable) return new(capture, CaptureCadence.Unknown, 0);
        double from = NominalRate(sourceRate), to = NominalRate(capture);
        if (Math.Abs(from / to - 1) <= TimingTolerance) return new(capture, CaptureCadence.Match, 1);
        double ratio = Math.Max(from, to) / Math.Min(from, to);
        int factor = (int)Math.Round(ratio);
        if (factor >= 2 && Math.Abs(ratio / factor - 1) <= TimingTolerance)
            return new(capture, from > to ? CaptureCadence.EvenReduction : CaptureCadence.EvenRepeat, factor);
        return new(capture, CaptureCadence.Uneven, 0);
    }

    internal static List<CaptureRateChoice> RateChoices(IEnumerable<double> rates, CaptureSourceTiming source) => rates
        .Select(r => Classify(r, source)).OrderBy(r => r.Cadence).ThenByDescending(r => r.Rate).ToList();

    internal static int DefaultRate(IReadOnlyList<CaptureRateChoice> choices, CaptureSourceTiming source)
    {
        // Exact match first, otherwise the highest even reduction. Multiples above
        // the source add no motion detail, so prefer them only when no divisor exists.
        if (choices[0].Cadence is CaptureCadence.Match or CaptureCadence.EvenReduction or CaptureCadence.EvenRepeat) return 0;
        double target = source.Rate ?? 30;
        return Enumerable.Range(0, choices.Count).MinBy(i => Math.Abs(NominalRate(choices[i].Rate) - target));
    }

    internal static string DescribeRate(CaptureRateChoice choice, CaptureSourceTiming source)
    {
        string rate = RateText(choice.Rate) + " fps";
        if (rate != CaptureSupport.Number(choice.Rate) + " fps")
            rate += $" (device reports {CaptureSupport.Number(choice.Rate)})";
        return rate + " — " + (choice.Cadence switch
        {
            CaptureCadence.Match => "recommended: matches the source rate",
            CaptureCadence.EvenReduction => $"recommended reduction: 1 frame per {choice.Factor} source frames",
            CaptureCadence.EvenRepeat => $"even repeats: {choice.Factor} output frames per source frame; no extra motion detail",
            CaptureCadence.Uneven => "uneven cadence: periodic frame drops/repeats may cause judder",
            _ => source.Variable ? "variable source: no fixed-rate cadence guarantee" : "source rate unknown"
        });
    }

    // Prioritize spatial color detail, then precision. These are representation
    // capabilities, not a claim about detail retained by the HDMI/card/driver path.
    // Compression quality and unknown layouts cannot be ranked reliably.
    internal static (int Detail, int Precision) ColorRank(string kind, string name)
    {
        string format = name.ToLowerInvariant();
        if (kind == "vcodec") return (-1, 0);
        if (format is "rgb24" or "bgr24" or "0rgb" or "rgb0" or "0bgr" or "bgr0" or "rgba" or "bgra" or "argb" or "abgr")
            return (6, 24);
        if (Regex.IsMatch(format, @"^(rgb|bgr)48(be|le)$") || Regex.IsMatch(format, @"^(rgba|bgra)64(be|le)$")) return (6, 48);
        if (Regex.IsMatch(format, @"^(rgb|bgr)565(be|le)$")) return (6, 16);
        if (Regex.IsMatch(format, @"^(rgb|bgr)555(be|le)$")) return (6, 15);
        if (format is "uyvy422" or "yuyv422" or "yvyu422") return (5, 24);
        if (format is "nv12" or "nv21") return (4, 24);
        var planar = Regex.Match(format, @"^(?:yuvj?|yuva|gbr|gbra)(?:(444|422|420|411|410))?p(?:(9|10|12|14|16)(?:le|be)?)?$");
        if (planar.Success)
        {
            int depth = planar.Groups[2].Success ? int.Parse(planar.Groups[2].Value) : 8;
            int detail = planar.Groups[1].Value switch { "422" => 5, "420" => 4, "411" => 3, "410" => 2, _ => 6 };
            return (detail, depth * 3);
        }
        var packed = Regex.Match(format, @"^p(0|2|4)(10|12|16)(?:le|be)?$");
        if (packed.Success) return (packed.Groups[1].Value switch { "0" => 4, "2" => 5, _ => 6 }, int.Parse(packed.Groups[2].Value) * 3);
        var gray = Regex.Match(format, @"^gray(?:(8|9|10|12|14|16)(?:le|be)?)?$");
        if (gray.Success) return (1, gray.Groups[1].Success ? int.Parse(gray.Groups[1].Value) : 8);
        if (format is "monob" or "monow") return (0, 1);
        return (-1, 0);
    }

    internal static List<(string Kind, string Format)> SortColors(IEnumerable<(string Kind, string Format)> colors) => colors
        .Distinct().OrderByDescending(c => ColorRank(c.Kind, c.Format).Detail)
        .ThenByDescending(c => ColorRank(c.Kind, c.Format).Precision).ToList();
}
