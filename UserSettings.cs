namespace PPTcrunch;

public class UserSettings
{
    public int MaxWidth { get; set; } = 1920;
    public VideoCodec Codec { get; set; } = VideoCodec.H265;
    public int QualityLevel { get; set; } = 2; // 1=Smallest, 2=Balanced, 3=Highest quality
    public bool UseGPUAcceleration { get; set; } = true;
    public bool ReduceHighResTo1920 { get; set; } = true;
    public HardwareAccelerationMode HardwareAcceleration { get; set; } = HardwareAccelerationMode.None;

    // Legacy property for backward compatibility
    public int Quality
    {
        get => GetQualityFromLevel();
        set => QualityLevel = GetLevelFromQuality(value);
    }

    private int GetQualityFromLevel()
    {
        var encodingSettings = QualityConfigService.GetEncodingSettings(QualityLevel, Codec, UseGPUAcceleration);
        return UseGPUAcceleration ? (encodingSettings.Cq ?? 25) : (encodingSettings.Crf ?? 25);
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
            _ => "libx264"
        };
    }

    public string GetGpuCodecName()
    {
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
    H265 = 2
}

public enum HardwareAccelerationMode
{
    None = 0,
    NvidiaNvenc = 1,
    AppleVideoToolbox = 2
}