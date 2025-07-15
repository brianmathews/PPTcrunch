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
        // Check NVIDIA driver capabilities once
        if (!_driverVersionChecked)
        {
            _supportsVbrHq = CheckNvidiaDriverSupportsVbrHq();
            _driverVersionChecked = true;
        }

        // Choose the appropriate rate control mode based on driver capabilities
        string rcMode = _supportsVbrHq ? "vbr_hq" : "vbr";

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
                        GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 25, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 28, Preset = "slow", Rc = rcMode }
                    }
                },
                ["2"] = new QualityLevel
                {
                    Name = "Balanced with good quality",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 22, Preset = "slow", Rc = rcMode }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 24, Preset = "medium" },
                        GPU = new EncodingSettings { Cq = 26, Preset = "slow", Rc = rcMode }
                    }
                },
                ["3"] = new QualityLevel
                {
                    Name = "Quality indistinguishable from source, bigger file",
                    H264 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 20, Preset = "slow" },
                        GPU = new EncodingSettings { Cq = 20, Preset = "slow", Rc = rcMode }
                    },
                    H265 = new CodecSettings
                    {
                        CPU = new EncodingSettings { Crf = 22, Preset = "slow" },
                        GPU = new EncodingSettings { Cq = 23, Preset = "slow", Rc = rcMode }
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
            },
            GPUCompatibility = new Dictionary<string, GPUInfo>
            {
                ["GTX_1060"] = new GPUInfo
                {
                    SupportedCodecs = new[] { "H264", "H265" },
                    H265_10bit = false,
                    MaxRefs = 3
                },
                ["GTX_1660"] = new GPUInfo
                {
                    SupportedCodecs = new[] { "H264", "H265" },
                    H265_10bit = false,
                    MaxRefs = 3
                },
                ["RTX_20xx"] = new GPUInfo
                {
                    SupportedCodecs = new[] { "H264", "H265" },
                    H265_10bit = true,
                    MaxRefs = 4
                },
                ["RTX_30xx"] = new GPUInfo
                {
                    SupportedCodecs = new[] { "H264", "H265" },
                    H265_10bit = true,
                    MaxRefs = 4
                },
                ["Default"] = new GPUInfo
                {
                    SupportedCodecs = new[] { "H264" },
                    H265_10bit = false,
                    MaxRefs = 2
                }
            }
        };
    }

    private static bool CheckNvidiaDriverSupportsVbrHq()
    {
        try
        {
            // Try to get NVIDIA driver version using nvidia-smi
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=driver_version --format=csv,noheader,nounits",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string version = output.Trim();
                    Console.WriteLine($"  NVIDIA driver version: {version}");

                    // Parse version number (e.g., "472.12" -> 472.12)
                    var match = Regex.Match(version, @"(\d+)\.(\d+)");
                    if (match.Success)
                    {
                        int major = int.Parse(match.Groups[1].Value);
                        int minor = int.Parse(match.Groups[2].Value);

                        // vbr_hq was introduced in driver version 416.34
                        bool supportsVbrHq = major > 416 || (major == 416 && minor >= 34);

                        Console.WriteLine($"  Driver supports vbr_hq: {(supportsVbrHq ? "Yes" : "No")} (using {(supportsVbrHq ? "vbr_hq" : "vbr")} mode)");
                        return supportsVbrHq;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Could not check NVIDIA driver version: {ex.Message}");
        }

        // Default to basic vbr mode for compatibility
        Console.WriteLine("  Using standard vbr mode for maximum compatibility");
        return false;
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
    public Dictionary<string, GPUInfo> GPUCompatibility { get; set; } = new();
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