namespace PPTcrunch;

public class UserSettings
{
    public int MaxWidth { get; set; } = 1920;
    public VideoCodec Codec { get; set; } = VideoCodec.H265;
    public int Quality { get; set; } = 26;

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
        return Codec switch
        {
            VideoCodec.H264 => "h264_nvenc",
            VideoCodec.H265 => "hevc_nvenc",
            _ => "h264_nvenc"
        };
    }

    public string GetCodecDisplayName()
    {
        return Codec switch
        {
            VideoCodec.H264 => "H.264",
            VideoCodec.H265 => "H.265",
            _ => "H.264"
        };
    }
}

public enum VideoCodec
{
    H264 = 1,
    H265 = 2
}