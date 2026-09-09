using System.Text.RegularExpressions;

namespace PPTcrunch;

public class QualityConfigService
{
    private static QualityConfig? _config;

    public static QualityConfig GetConfig()
    {
        if (_config == null)
        {
            _config = CreateHardcodedConfig();
        }
        return _config!;
    }

    private static QualityConfig CreateHardcodedConfig()
    {
        return new QualityConfig
        {
            QualityLevels = new Dictionary<string, QualityLevel>
            {
                ["0"] = new QualityLevel
                {
                    Name = "Passable - smallest files; minor visible artifacts (target)",
                    H264 = new CodecSettings { CPU = new EncodingSettings { Crf = 28, Preset = "slow" }, GPU = NvencSettings(32, 40) },
                    H265 = new CodecSettings { CPU = new EncodingSettings { Crf = 30, Preset = "slow" }, GPU = NvencSettings(31, 40) },
                    VP9 = new CodecSettings { CPU = new EncodingSettings { Crf = 38, CpuUsed = 2 } },
                    AV1 = new CodecSettings { CPU = new EncodingSettings { Crf = 38, Preset = "4" }, GPU = NvencSettings(31, 40) }
                },
                ["1"] = new QualityLevel
                {
                    Name = "Good - smaller files",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 26, Preset = "slow" },
                        GPU = NvencSettings(29, 50)
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 28, Preset = "slow" },
                        GPU = NvencSettings(28, 50)
                    },
                    VP9 = new CodecSettings { CPU = new EncodingSettings { Crf = 34, CpuUsed = 2 } },
                    AV1 = new CodecSettings { CPU = new EncodingSettings { Crf = 34, Preset = "4" }, GPU = NvencSettings(28, 50) }
                },
                ["2"] = new QualityLevel
                {
                    Name = "Better - balanced quality and file size",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "slow" },
                        GPU = NvencSettings(26, 65)
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 24, Preset = "slow" },
                        GPU = NvencSettings(26, 65)
                    },
                    VP9 = new CodecSettings { CPU = new EncodingSettings { Crf = 30, CpuUsed = 2 } },
                    AV1 = new CodecSettings { CPU = new EncodingSettings { Crf = 30, Preset = "4" }, GPU = NvencSettings(26, 65) }
                },
                ["3"] = new QualityLevel
                {
                    Name = "Indistinguishable in normal playback (target; lossy)",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 20, Preset = "slow" },
                        GPU = NvencSettings(23, 75)
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "slow" },
                        GPU = NvencSettings(23, 75)
                    },
                    VP9 = new CodecSettings { CPU = new EncodingSettings { Crf = 28, CpuUsed = 2 } },
                    AV1 = new CodecSettings { CPU = new EncodingSettings { Crf = 26, Preset = "4" }, GPU = NvencSettings(23, 75) }
                },
                ["4"] = new QualityLevel
                {
                    Name = "Archive mode - extra detail for later recompression (lossy)",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 18, Preset = "slow" },
                        GPU = NvencSettings(20, 85)
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 20, Preset = "slow" },
                        GPU = NvencSettings(20, 85)
                    },
                    VP9 = new CodecSettings { CPU = new EncodingSettings { Crf = 24, CpuUsed = 2 } },
                    AV1 = new CodecSettings { CPU = new EncodingSettings { Crf = 20, Preset = "4" }, GPU = NvencSettings(20, 85) }
                }
            },
            CodecSettings = new CodecSettingsConfig
            {
                H264 = new CodecSpecificSettings
                {
                    GPU = new CodecParams { Profile = "high", Bf = 3 },
                    CPU = new CodecParams { Profile = "high" }
                },
                H265 = new CodecSpecificSettings
                {
                    // Let the HEVC hardware preset select B/ reference frames: older NVENC
                    // generations cannot encode HEVC B-frames at all.
                    GPU = new CodecParams { Profile = "main", Tag = "hvc1" },
                    CPU = new CodecParams { Profile = "main", Tag = "hvc1" }
                },
                VP9 = new CodecSpecificSettings { CPU = new CodecParams { Profile = "0" } },
                AV1 = new CodecSpecificSettings { CPU = new CodecParams { Profile = "0", Tag = "av01" }, GPU = new CodecParams { Profile = "0", Tag = "av01" } }
            }
        };
    }

    // P6 stops short of the most expensive P7 preset. Quality scales are encoder-specific;
    // these are targets, not measured equivalence or a guarantee of visual transparency.
    private static EncodingSettings NvencSettings(int cq, int vtQuality) => new()
    {
        Cq = cq, VtQuality = vtQuality, Preset = "p6", Rc = "vbr", Tune = "hq",
        Multipass = 1, Lookahead = 32
    };

    /// <summary>
    /// Determines GPU capabilities based on GPU name/model using simplified generation detection
    /// </summary>
    /// <param name="gpuName">The GPU name as reported by the system</param>
    /// <returns>GPUInfo with capabilities, or null if GPU is not supported</returns>
    public static GPUInfo? GetGPUCapabilities(string gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName))
            return null;

        string normalizedName = gpuName.ToUpperInvariant().Replace(" ", "");

        // Check if this is a supported NVIDIA GPU and extract model number
        int? modelNumber = ExtractNvidiaModelNumber(normalizedName);
        if (!modelNumber.HasValue)
            return null;

        // Check if model meets minimum requirements (GTX 1060 or higher)
        if (modelNumber.Value < 1060)
            return null;

        // Extract generation number (10 for 10xx, 20 for 20xx, etc.)
        int generation = modelNumber.Value / 100;

        return new GPUInfo
        {
            // All supported GPUs have H264 and H265 support
            SupportedCodecs = new[] { "H264", "H265" },

            // H265 10-bit support introduced with generation 10 (GTX 10xx/Pascal)
            H265_10bit = generation >= 10,

            // MaxRefs: 3 for generation 10-15, 4 for generation 16+ (Turing and later)
            MaxRefs = generation >= 16 ? 4 : 3
        };
    }

    /// <summary>
    /// Extracts the model number from NVIDIA GPU names (e.g., "GTX 1060" -> 1060, "RTX 4080" -> 4080)
    /// </summary>
    private static int? ExtractNvidiaModelNumber(string gpuName)
    {
        // Look for NVIDIA GPU patterns and extract model numbers
        var patterns = new[]
        {
            @"GTX(\d{3,4})",           // GTX 1060, GTX 1660, etc.
            @"RTX(\d{3,4})",           // RTX 2060, RTX 3070, RTX 4080, RTX 5090, etc.
            @"TITAN.*?(\d{3,4})",      // Future Titan models with numbers
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(gpuName, pattern);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int modelNumber))
            {
                return modelNumber;
            }
        }

        // Handle special cases for Titan series without model numbers
        if (gpuName.Contains("TITAN"))
        {
            // Treat Titan cards as high-end models
            if (gpuName.Contains("RTX"))
                return 2080; // Titan RTX ~ RTX 2080 generation
            if (gpuName.Contains("V"))
                return 1080; // Titan V ~ GTX 1080 generation
            if (gpuName.Contains("X"))
                return 1080; // Titan X ~ GTX 1080 generation
        }

        // Handle professional cards - treat as high-end current generation
        if (gpuName.Contains("QUADRORTX") || gpuName.Contains("RTXA") || gpuName.Contains("RTXPRO"))
        {
            return 3080; // Treat professional cards as RTX 3080 equivalent
        }

        return null; // Not a supported NVIDIA GPU
    }

    public static EncodingSettings GetEncodingSettings(int qualityLevel, VideoCodec codec, bool useGPU)
    {
        var config = GetConfig();
        string levelKey = qualityLevel.ToString();

        if (!config.QualityLevels.TryGetValue(levelKey, out var level))
        {
            levelKey = "2"; // Default to level 2
            level = config.QualityLevels[levelKey];
        }

        var codecSettings = codec switch
        {
            VideoCodec.H264 => level.H264,
            VideoCodec.H265 => level.H265,
            VideoCodec.VP9 => level.VP9,
            VideoCodec.AV1 => level.AV1,
            _ => throw new ArgumentOutOfRangeException(nameof(codec))
        };
        if (codec == VideoCodec.VP9) return codecSettings.CPU;
        return useGPU ? codecSettings.GPU : codecSettings.CPU;
    }

    public static CodecParams GetCodecParams(VideoCodec codec, bool useGPU)
    {
        var config = GetConfig();
        var codecSettings = codec switch
        {
            VideoCodec.H264 => config.CodecSettings.H264,
            VideoCodec.H265 => config.CodecSettings.H265,
            VideoCodec.VP9 => config.CodecSettings.VP9,
            VideoCodec.AV1 => config.CodecSettings.AV1,
            _ => throw new ArgumentOutOfRangeException(nameof(codec))
        };
        if (codec == VideoCodec.VP9) return codecSettings.CPU;

        return useGPU ? codecSettings.GPU : codecSettings.CPU;
    }
}



public class QualityConfig
{
    public Dictionary<string, QualityLevel> QualityLevels { get; set; } = new();
    public CodecSettingsConfig CodecSettings { get; set; } = new();
}

public class QualityLevel
{
    public string Name { get; set; } = string.Empty;
    public CodecSettings H264 { get; set; } = new();
    public CodecSettings H265 { get; set; } = new();
    public CodecSettings VP9 { get; set; } = new();
    public CodecSettings AV1 { get; set; } = new();
}

public class CodecSettings
{
    public EncodingSettings CPU { get; set; } = new();
    public EncodingSettings GPU { get; set; } = new();
}

public class EncodingSettings
{
    public int? Crf { get; set; }
    public int? Cq { get; set; }
    public int? VtQuality { get; set; }
    public string Preset { get; set; } = string.Empty;
    public string Rc { get; set; } = string.Empty;
    public string Tune { get; set; } = string.Empty;
    public int? Multipass { get; set; }
    public int? Lookahead { get; set; }
    public int? CpuUsed { get; set; }
}

public class CodecSettingsConfig
{
    public CodecSpecificSettings H264 { get; set; } = new();
    public CodecSpecificSettings H265 { get; set; } = new();
    public CodecSpecificSettings VP9 { get; set; } = new();
    public CodecSpecificSettings AV1 { get; set; } = new();
}

public class CodecSpecificSettings
{
    public CodecParams GPU { get; set; } = new();
    public CodecParams CPU { get; set; } = new();
}

public class CodecParams
{
    public string Profile { get; set; } = string.Empty;
    public int? Bf { get; set; }
    public int? Refs { get; set; }
    public string Tag { get; set; } = string.Empty;
}

public class GPUInfo
{
    public string[] SupportedCodecs { get; set; } = Array.Empty<string>();
    public bool H265_10bit { get; set; }
    public int MaxRefs { get; set; }
}
