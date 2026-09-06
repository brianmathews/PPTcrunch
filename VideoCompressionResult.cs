namespace PPTcrunch;

public record VideoEncodingResult(bool Success, HardwareAccelerationMode HardwareAcceleration)
{
    public string Method => HardwareAcceleration switch
    {
        HardwareAccelerationMode.NvidiaNvenc => "NVIDIA NVENC",
        HardwareAccelerationMode.AppleVideoToolbox => "Apple VideoToolbox",
        _ => "CPU"
    };
}

public class VideoCompressionResult
{
    public string OriginalFileName { get; set; } = string.Empty;
    public string FinalFileName { get; set; } = string.Empty;
    public bool WasCompressed { get; set; }
    public bool FileSizeReduced { get; set; }
    public long OriginalSize { get; set; }
    public long FinalSize { get; set; }
    public string CompressionMethod { get; set; } = string.Empty; // "GPU", "CPU", or "Original"
    public string Reason { get; set; } = string.Empty; // Why this result was chosen
}
