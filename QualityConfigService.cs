using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace PPTcrunch;

public class QualityConfigService
{
    private static QualityConfig? _config;
    private static bool _driverVersionChecked = false;
    private static bool _supportsVbrHq = false;

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
        // Check NVIDIA driver capabilities once for advanced features
        if (!_driverVersionChecked)
        {
            _supportsVbrHq = CheckNvidiaDriverSupportsVbrHq();
            _driverVersionChecked = true;
        }

        // Use modern vbr rate control with tune and multipass for advanced features
        string rcMode = "vbr";
        string tuneMode = _supportsVbrHq ? "hq" : "";
        int? multipassValue = _supportsVbrHq ? 2 : null;

        return new QualityConfig
        {
            QualityLevels = new Dictionary<string, QualityLevel>
            {
                ["1"] = new QualityLevel
                {
                    Name = "Smallest file with passable quality",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 26, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 25, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 28, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    }
                },
                ["2"] = new QualityLevel
                {
                    Name = "Balanced with good quality",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 22, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 24, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    }
                },
                ["3"] = new QualityLevel
                {
                    Name = "Quality indistinguishable from source, bigger file",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 20, Preset = "slow" },
                        GPU = new EncodingSettings { Cq = 20, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "slow" },
                        GPU = new EncodingSettings { Cq = 23, Preset = "slow", Rc = rcMode, Tune = tuneMode, Multipass = multipassValue }
                    }
                }
            },
            CodecSettings = new CodecSettingsConfig
            {
                H264 = new CodecSpecificSettings
                {
                    GPU = new CodecParams { Profile = "high", Bf = 3, Refs = 4 },
                    CPU = new CodecParams { Profile = "high" }
                },
                H265 = new CodecSpecificSettings
                {
                    GPU = new CodecParams { Profile = "main", Bf = 3, Refs = 3, Tag = "hvc1" },
                    CPU = new CodecParams { Profile = "main", Tag = "hvc1" }
                }
            }
        };
    }

    private static bool CheckNvidiaDriverSupportsVbrHq()
    {
        // For embedded FFmpeg distribution, we'll use conservative settings
        // to ensure maximum compatibility across different systems
        // Most modern systems with NVENC support will have recent enough drivers
        // but we'll default to basic vbr mode for reliability

        Console.WriteLine("  Using standard vbr mode for maximum compatibility");
        return false;
    }

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
    /// Extracts the model number from NVIDIA GPU names (e.g., "GTX 1060" → 1060, "RTX 4080" → 4080)
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

        var codecSettings = codec == VideoCodec.H264 ? level.H264 : level.H265;
        return useGPU ? codecSettings.GPU : codecSettings.CPU;
    }

    public static CodecParams GetCodecParams(VideoCodec codec, bool useGPU)
    {
        var config = GetConfig();
        var codecSettings = codec == VideoCodec.H264 ?
            config.CodecSettings.H264 :
            config.CodecSettings.H265;

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
    public string Preset { get; set; } = string.Empty;
    public string Rc { get; set; } = string.Empty;
    public string Tune { get; set; } = string.Empty;
    public int? Multipass { get; set; }
}

public class CodecSettingsConfig
{
    public CodecSpecificSettings H264 { get; set; } = new();
    public CodecSpecificSettings H265 { get; set; } = new();
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