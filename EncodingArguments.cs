namespace PPTcrunch;

/// <summary>Shared output options for bundled and external FFmpeg runners.</summary>
public static class EncodingArguments
{
    public static List<string> Build(UserSettings settings, HardwareAccelerationMode hardware, bool videoOnly = false, double? inputFrameRate = null)
    {
        if (settings.Codec == VideoCodec.VP9 || (settings.Codec == VideoCodec.AV1 && hardware == HardwareAccelerationMode.AppleVideoToolbox)) hardware = HardwareAccelerationMode.None;
        var encoding = QualityConfigService.GetEncodingSettings(settings.QualityLevel, settings.Codec, hardware != HardwareAccelerationMode.None);
        var codec = QualityConfigService.GetCodecParams(settings.Codec, hardware != HardwareAccelerationMode.None);
        // A single scale operation; no upscaling, both dimensions at least two and even.
        string width = $"max(2,trunc(min(iw,{Math.Max(2, settings.MaxWidth)})/2)*2)";
        // Select frames on the output timeline before scaling/encoding. Preserve
        // source timestamps entirely when no reduction is needed (including VFR).
        double? targetRate = FrameRatePolicy.TargetRate(settings, inputFrameRate);
        string filters = targetRate.HasValue
            ? $"fps=fps={targetRate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}:round=near," : "";
        filters += $"scale=w='{width}':h=-2:flags=lanczos";
        var args = new List<string>
        {
            "-map", "0:v:0", "-sn", "-dn",
            "-vf", $"\"{filters}\"",
            "-pix_fmt", settings.Codec == VideoCodec.AV1 ? (hardware == HardwareAccelerationMode.NvidiaNvenc ? "p010le" : "yuv420p10le") : "yuv420p", "-profile:v", codec.Profile, "-fps_mode", "passthrough"
        };
        if (videoOnly) args.Add("-an");
        else args.AddRange(new[] { "-map", "0:a?" });
        switch (hardware)
        {
            case HardwareAccelerationMode.NvidiaNvenc:
                args.AddRange(new[]
                {
                    "-c:v", settings.Codec switch { VideoCodec.H264 => "h264_nvenc", VideoCodec.H265 => "hevc_nvenc", VideoCodec.AV1 => "av1_nvenc", _ => throw new NotSupportedException() },
                    "-rc", encoding.Rc, "-cq", $"{encoding.Cq}", "-b:v", "0",
                    "-preset", encoding.Preset, "-tune", encoding.Tune,
                    "-multipass", $"{encoding.Multipass}", "-rc-lookahead", $"{encoding.Lookahead}",
                    "-spatial-aq", "1", "-temporal-aq", "1"
                });
                if (codec.Bf.HasValue) args.AddRange(new[] { "-bf", $"{codec.Bf}" });
                break;
            case HardwareAccelerationMode.AppleVideoToolbox:
                // Apple's scale increases with quality, unlike CRF/CQ. Require hardware;
                // a failed quality-mode encode falls back to explicit CPU CRF encoding.
                args.AddRange(new[]
                {
                    "-c:v", settings.Codec == VideoCodec.H264 ? "h264_videotoolbox" : "hevc_videotoolbox",
                    "-q:v", $"{encoding.VtQuality}", "-b:v", "0", "-realtime", "0", "-allow_sw", "0"
                });
                break;
            default:
                args.AddRange(new[] { "-c:v", settings.GetCpuCodecName(), "-crf", $"{encoding.Crf}" });
                if (settings.Codec == VideoCodec.VP9)
                    args.AddRange(new[] { "-b:v", "0", "-deadline", "good", "-cpu-used", $"{encoding.CpuUsed}",
                        "-row-mt", "1", "-lag-in-frames", "25", "-g", "240" });
                else if (settings.Codec == VideoCodec.AV1)
                    // Single-pass CRF with visual-quality tuning; no fixed bitrate budget.
                    args.AddRange(new[] { "-preset", encoding.Preset, "-svtav1-params", "tune=0", "-g", "240" });
                else
                    // Let the preset choose B-frames, references and lookahead together.
                    args.AddRange(new[] { "-preset", encoding.Preset });
                break;
        }

        if (videoOnly)
            args.AddRange(new[] { "-f", "null" });
        else if (settings.Codec == VideoCodec.VP9)
        {
            // AAC/PCM cannot be copied into WebM. Opus VBR uses a modest audio budget;
            // retain every audio track and let libopus preserve mono/stereo/multichannel.
            args.AddRange(new[] { "-c:a", "libopus", "-b:a", "128k", "-vbr", "on", "-compression_level", "10",
                "-ar", "48000", "-f", "webm" });
        }
        else
        {
            if (!string.IsNullOrEmpty(codec.Tag)) args.AddRange(new[] { "-tag:v", codec.Tag });
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "mp4" });
        }
        args.AddRange(new[] { "-y", "-stats" });
        return args;
    }
}
