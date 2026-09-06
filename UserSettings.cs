namespace PPTcrunch;

public class UserSettings
{
    public int MaxWidth { get; set; } = 1920;
    public VideoCodec Codec { get; set; } = VideoCodec.H265;
    public int QualityLevel { get; set; } = 2; // 1=Good, 2=Better, 3=Normal-viewing transparency target, 4=Archive
    public bool UseGPUAcceleration { get; set; } = false;
    public bool UseVp9TwoPass { get; set; } = true;
    public bool ReduceHighResTo1920 { get; set; } = true;
    public bool ReduceHighFrameRates { get; set; } = false;
    public HardwareAccelerationMode HardwareAcceleration { get; set; } = HardwareAccelerationMode.None;
    public HardwareAccelerationMode EffectiveHardwareAcceleration =>
        UseGPUAcceleration && Codec != VideoCodec.VP9 ? HardwareAcceleration : HardwareAccelerationMode.None;
    public string OutputExtension => Codec == VideoCodec.VP9 ? ".webm" : ".mp4";
    public string CodecSuffix => Codec.ToString();

    // Legacy property for backward compatibility
    public int Quality
    {
        get => GetQualityFromLevel();
        set => QualityLevel = GetLevelFromQuality(value);
    }

    private int GetQualityFromLevel()
    {
        var encodingSettings = QualityConfigService.GetEncodingSettings(QualityLevel, Codec, EffectiveHardwareAcceleration != HardwareAccelerationMode.None);
        return EffectiveHardwareAcceleration switch
        {
            HardwareAccelerationMode.NvidiaNvenc => encodingSettings.Cq ?? 25,
            HardwareAccelerationMode.AppleVideoToolbox => encodingSettings.VtQuality ?? 65,
            _ => encodingSettings.Crf ?? 25
        };
    }

    private int GetLevelFromQuality(int quality)
    {
        // Map old quality values to new levels (approximate)
        return quality switch
        {
            <= 22 => 3, // High quality
            <= 27 => 2, // Balanced
            _ => 1      // Smaller file
        };
    }

    public string GetCpuCodecName()
    {
        return Codec switch
        {
            VideoCodec.H264 => "libx264",
            VideoCodec.H265 => "libx265",
            VideoCodec.VP9 => "libvpx-vp9",
            _ => "libx264"
        };
    }

    public string GetGpuCodecName()
    {
        if (Codec == VideoCodec.VP9)
            throw new NotSupportedException("VP9 uses the CPU libvpx-vp9 encoder; NVENC and VideoToolbox do not encode VP9.");
        return HardwareAcceleration switch
        {
            HardwareAccelerationMode.AppleVideoToolbox => Codec switch
            {
                VideoCodec.H264 => "h264_videotoolbox",
                VideoCodec.H265 => "hevc_videotoolbox",
                _ => "h264_videotoolbox"
            },
            HardwareAccelerationMode.NvidiaNvenc => Codec switch
            {
                VideoCodec.H264 => "h264_nvenc",
                VideoCodec.H265 => "hevc_nvenc",
                _ => "h264_nvenc"
            },
            _ => Codec switch
            {
                VideoCodec.H264 => "h264_nvenc",
                VideoCodec.H265 => "hevc_nvenc",
                _ => "h264_nvenc"
            }
        };
    }

    public string GetCodecDisplayName()
    {
        return Codec switch
        {
            VideoCodec.H264 => "H.264 (better compatibility, standard quality)",
            VideoCodec.H265 => "H.265 (smaller files, newer standard, may not work on older systems)",
            VideoCodec.VP9 => "WebM / VP9 (modern web browsers, CPU encoding, standalone videos only)",
            _ => "H.264"
        };
    }

    public string GetQualityLevelDisplayName()
    {
        var config = QualityConfigService.GetConfig();
        string levelKey = QualityLevel.ToString();

        if (config.QualityLevels.TryGetValue(levelKey, out var level))
        {
            return $"{QualityLevel} - {level.Name}";
        }

        return QualityLevel.ToString();
    }
}

public enum VideoCodec
{
    H264 = 1,
    H265 = 2,
    VP9 = 3
}

public enum HardwareAccelerationMode
{
    None = 0,
    NvidiaNvenc = 1,
    AppleVideoToolbox = 2
}
