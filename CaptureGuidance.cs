using System.Globalization;
using System.Text.RegularExpressions;

namespace PPTcrunch;

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

    internal static string RateLabel(double rate)
    {
        string label = RateText(rate) + " fps";
        string reported = CaptureSupport.Number(rate) + " fps";
        return label == reported ? label : $"{label} (device reports {CaptureSupport.Number(rate)})";
    }

    internal static int DefaultRate(IReadOnlyList<double> rates)
    {
        if (rates.Count == 0) throw new ArgumentException("At least one capture rate is required.", nameof(rates));
        for (int i = 0; i < rates.Count; i++)
            if (NominalRate(rates[i]) == 30) return i;
        double ntsc30 = 30000.0 / 1001;
        for (int i = 0; i < rates.Count; i++)
            if (NominalRate(rates[i]) == ntsc30) return i;
        return Enumerable.Range(0, rates.Count).MinBy(i => Math.Abs(NominalRate(rates[i]) - 30));
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
