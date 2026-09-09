using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace PPTcrunch;

internal record WindowsCaptureMode(
    uint NativeIndex,
    int Width,
    int Height,
    uint RateNumerator,
    uint RateDenominator,
    Guid Subtype,
    string PixelFormat,
    int Stride)
{
    internal double Rate => RateDenominator == 0 ? 0 : (double)RateNumerator / RateDenominator;
    internal string Kind => PixelFormat == "mjpeg" ? "vcodec" : "pixel_format";
}

internal record WindowsCaptureSource(string Id, string Name, IReadOnlyList<WindowsCaptureMode> Modes, string? Error = null);
internal record WindowsCaptureResult(int Frames, IReadOnlyList<long> Timestamps)
{
    internal double MeasuredRate
    {
        get
        {
            var intervals = Timestamps.Zip(Timestamps.Skip(1), (a, b) => b - a)
                .Where(value => value > 0).Order().ToArray();
            return intervals.Length == 0 ? 0 : 10_000_000.0 / intervals[intervals.Length / 2];
        }
    }
}

/// <summary>
/// Uses the Windows inbox Media Foundation capture stack. FFmpeg remains the
/// downstream encoder/muxer, but it no longer negotiates the capture device.
/// </summary>
internal static class WindowsMediaFoundationCapture
{
    private const uint MfVersion = 0x00020070;
    private const int MfENoMoreTypes = unchecked((int)0xC00D36B9);
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private static readonly uint VideoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);

    [SupportedOSPlatform("windows6.1")]
    internal static IReadOnlyList<WindowsCaptureSource> Discover() => WithMediaFoundation(() =>
    {
        PInvoke.MFCreateAttributes(out IMFAttributes attributes, 1).ThrowOnFailure();
        try
        {
            attributes.SetGUID(PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
            unsafe
            {
                PInvoke.MFEnumDeviceSources(attributes, out IMFActivate_unmanaged** found, out uint count).ThrowOnFailure();
                try
                {
                    var result = new List<WindowsCaptureSource>((int)count);
                    for (uint i = 0; i < count; i++)
                    {
                        nint raw = (nint)found[i];
                        if (raw == 0) continue;
                        IMFActivate activate = (IMFActivate)Marshal.GetObjectForIUnknown(raw);
                        Marshal.Release(raw);
                        try
                        {
                            string name = ReadString(activate, PInvoke.MF_DEVSOURCE_ATTRIBUTE_FRIENDLY_NAME);
                            string id = ReadString(activate, PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK);
                            try
                            {
                                IMFMediaSource source = (IMFMediaSource)activate.ActivateObject(typeof(IMFMediaSource).GUID);
                                try { result.Add(new(id, name, ReadModes(source))); }
                                finally { Release(source); }
                            }
                            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
                            {
                                result.Add(new(id, name, Array.Empty<WindowsCaptureMode>(), ex.Message));
                            }
                        }
                        finally { Release(activate); }
                    }
                    return (IReadOnlyList<WindowsCaptureSource>)result;
                }
                finally
                {
                    if (found != null) Marshal.FreeCoTaskMem((nint)found);
                }
            }
        }
        finally { Release(attributes); }
    });

    [SupportedOSPlatform("windows6.1")]
    internal static Task<WindowsCaptureResult> WriteFramesAsync(string sourceId, WindowsCaptureMode mode,
        Stream destination, int? maximumFrames, CancellationToken cancellationToken) => Task.Run(() =>
            WithMediaFoundation(() => WriteFrames(sourceId, mode, destination, maximumFrames, cancellationToken)),
            CancellationToken.None);

    internal static IReadOnlyList<string> FfmpegInput(WindowsCaptureMode mode)
    {
        var result = new List<string> { "-hide_banner", "-nostdin" };
        if (mode.PixelFormat == "mjpeg")
            result.AddRange(new[] { "-f", "mjpeg" });
        else
            result.AddRange(new[] { "-f", "rawvideo", "-pixel_format", mode.PixelFormat,
                "-video_size", $"{mode.Width}x{mode.Height}" });
        // Raw frames carry no timestamps through the pipe. Let FFmpeg's rawvideo
        // demuxer assign consecutive timestamps at the selected device rate.
        // Wall-clock stamping can quantize adjacent live frames to the same tick,
        // which produces non-monotonic DTS warnings and uneven MP4 timestamps.
        result.AddRange(new[] { "-framerate", $"{mode.RateNumerator}/{mode.RateDenominator}",
            "-i", "pipe:0" });
        return result;
    }

    [SupportedOSPlatform("windows6.1")]
    private static WindowsCaptureResult WriteFrames(string sourceId, WindowsCaptureMode mode, Stream destination,
        int? maximumFrames, CancellationToken cancellationToken)
    {
        IMFActivate activate = FindActivation(sourceId);
        try
        {
            IMFMediaSource source = (IMFMediaSource)activate.ActivateObject(typeof(IMFMediaSource).GUID);
            try
            {
                PInvoke.MFCreateSourceReaderFromMediaSource(source, null, out IMFSourceReader reader).ThrowOnFailure();
                try
                {
                    reader.GetNativeMediaType(VideoStream, mode.NativeIndex, out IMFMediaType type);
                    try
                    {
                        WindowsCaptureMode actual = ReadMode(type, mode.NativeIndex)
                            ?? throw new IOException("The selected capture mode is no longer available.");
                        if (actual.Width != mode.Width || actual.Height != mode.Height || actual.Subtype != mode.Subtype ||
                            actual.RateNumerator != mode.RateNumerator || actual.RateDenominator != mode.RateDenominator)
                            throw new IOException("The capture device's native mode list changed. Run capture again and reselect the mode.");
                        reader.SetCurrentMediaType(VideoStream, type);
                    }
                    finally { Release(type); }

                    var timestamps = new List<long>();
                    using var cancellation = cancellationToken.Register(() =>
                    {
                        try { reader.Flush(VideoStream); }
                        catch (COMException) { }
                    });
                    try
                    {
                        while (!cancellationToken.IsCancellationRequested &&
                            (!maximumFrames.HasValue || timestamps.Count < maximumFrames.Value))
                        {
                            reader.ReadSample(VideoStream, 0, out _, out uint flags, out long timestamp, out IMFSample sample);
                            var status = (MF_SOURCE_READER_FLAG)flags;
                            if ((status & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                                throw new IOException("Media Foundation reported a capture error.");
                            if ((status & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0 ||
                                (status & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_NATIVEMEDIATYPECHANGED) != 0)
                                throw new IOException("The capture device changed the selected video mode while recording.");
                            if ((status & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0) break;
                            if (sample == null) continue;
                            try
                            {
                                sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
                                try
                                {
                                    unsafe
                                    {
                                        buffer.Lock(out byte* data, out _, out uint length);
                                        try { WriteBuffer(destination, mode, data, checked((int)length)); }
                                        finally { buffer.Unlock(); }
                                    }
                                }
                                finally { Release(buffer); }
                                timestamps.Add(timestamp);
                            }
                            finally { Release(sample); }
                        }
                    }
                    catch (COMException) when (cancellationToken.IsCancellationRequested) { }
                    destination.Flush();
                    return new(timestamps.Count, timestamps);
                }
                finally { Release(reader); }
            }
            finally { Release(source); }
        }
        finally { Release(activate); }
    }

    [SupportedOSPlatform("windows6.1")]
    private static IMFActivate FindActivation(string sourceId)
    {
        PInvoke.MFCreateAttributes(out IMFAttributes attributes, 1).ThrowOnFailure();
        try
        {
            attributes.SetGUID(PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE,
                PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_GUID);
            unsafe
            {
                PInvoke.MFEnumDeviceSources(attributes, out IMFActivate_unmanaged** found, out uint count).ThrowOnFailure();
                try
                {
                    IMFActivate? match = null;
                    for (uint i = 0; i < count; i++)
                    {
                        nint raw = (nint)found[i];
                        if (raw == 0) continue;
                        IMFActivate activate = (IMFActivate)Marshal.GetObjectForIUnknown(raw);
                        Marshal.Release(raw);
                        if (match == null && ReadString(activate, PInvoke.MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK) == sourceId)
                            match = activate;
                        else Release(activate);
                    }
                    if (match != null) return match;
                }
                finally { if (found != null) Marshal.FreeCoTaskMem((nint)found); }
            }
        }
        finally { Release(attributes); }
        throw new IOException("The selected video capture device is no longer connected.");
    }

    [SupportedOSPlatform("windows6.1")]
    private static List<WindowsCaptureMode> ReadModes(IMFMediaSource source)
    {
        PInvoke.MFCreateSourceReaderFromMediaSource(source, null, out IMFSourceReader reader).ThrowOnFailure();
        try
        {
            var modes = new List<WindowsCaptureMode>();
            for (uint index = 0; ; index++)
            {
                IMFMediaType? type = null;
                try { reader.GetNativeMediaType(VideoStream, index, out type); }
                catch (COMException ex) when (ex.HResult == MfENoMoreTypes) { break; }
                try
                {
                    var mode = ReadMode(type, index);
                    if (mode != null) modes.Add(mode);
                }
                finally { Release(type); }
            }
            return modes;
        }
        finally { Release(reader); }
    }

    private static WindowsCaptureMode? ReadMode(IMFMediaType type, uint index)
    {
        type.GetGUID(PInvoke.MF_MT_MAJOR_TYPE, out Guid major);
        if (major != PInvoke.MFMediaType_Video) return null;
        type.GetGUID(PInvoke.MF_MT_SUBTYPE, out Guid subtype);
        type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out ulong packedSize);
        type.GetUINT64(PInvoke.MF_MT_FRAME_RATE, out ulong packedRate);
        int width = (int)(packedSize >> 32);
        int height = (int)(packedSize & uint.MaxValue);
        uint numerator = (uint)(packedRate >> 32);
        uint denominator = (uint)packedRate;
        string? pixel = PixelFormat(subtype);
        int stride;
        try { type.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out uint storedStride); stride = unchecked((int)storedStride); }
        catch (COMException) { stride = MinimumStride(pixel, width); }
        return pixel != null && width > 0 && height > 0 && numerator > 0 && denominator > 0
            ? new(index, width, height, numerator, denominator, subtype, pixel, stride) : null;
    }

    private static int MinimumStride(string? pixel, int width) => pixel switch
    {
        "bgr24" => checked((width * 3 + 3) & ~3),
        "yuyv422" => checked(width * 2),
        "nv12" => width,
        "p010le" => checked(width * 2),
        _ => 0
    };

    private static unsafe void WriteBuffer(Stream destination, WindowsCaptureMode mode, byte* data, int length)
    {
        if (mode.PixelFormat == "mjpeg")
        {
            destination.Write(new ReadOnlySpan<byte>(data, length));
            return;
        }

        int expected = mode.PixelFormat switch
        {
            "bgr24" => checked(mode.Width * mode.Height * 3),
            "yuyv422" => checked(mode.Width * mode.Height * 2),
            "nv12" => checked(mode.Width * mode.Height * 3 / 2),
            "p010le" => checked(mode.Width * mode.Height * 3),
            _ => throw new IOException($"Unsupported raw capture format {mode.PixelFormat}.")
        };
        if (mode.PixelFormat != "bgr24")
        {
            if (length < expected)
                throw new IOException($"The device returned a short {mode.PixelFormat} frame ({length} bytes; expected {expected}).");
            destination.Write(new ReadOnlySpan<byte>(data, expected));
            return;
        }

        int rowBytes = checked(mode.Width * 3);
        int stride = mode.Stride == 0 ? rowBytes : mode.Stride;
        if (length < checked(Math.Abs(stride) * mode.Height))
            throw new IOException($"The device returned a short RGB24 frame ({length} bytes for stride {stride}).");
        WriteBgr24(destination, new ReadOnlySpan<byte>(data, length), mode.Width, mode.Height, stride);
    }

    internal static void WriteBgr24(Stream destination, ReadOnlySpan<byte> data, int width, int height, int stride)
    {
        int rowBytes = checked(width * 3);
        int absoluteStride = Math.Abs(stride);
        if (absoluteStride < rowBytes || data.Length < checked(absoluteStride * height))
            throw new IOException("The RGB24 frame stride or buffer length is invalid.");
        int offset = stride < 0 ? absoluteStride * (height - 1) : 0;
        for (int y = 0; y < height; y++, offset += stride)
            destination.Write(data.Slice(offset, rowBytes));
    }

    internal static string? PixelFormat(Guid subtype)
    {
        // Accept Media Foundation's native RGB GUID and the legacy DirectShow
        // RGB subtype exposed by some bridge drivers. FFmpeg calls the little-
        // endian in-memory RGB24 layout bgr24.
        if (subtype == PInvoke.MFVideoFormat_RGB24 ||
            subtype == new Guid("e436eb7d-524f-11ce-9f53-0020af0ba770")) return "bgr24";
        if (subtype == PInvoke.MFVideoFormat_YUY2) return "yuyv422";
        if (subtype == PInvoke.MFVideoFormat_NV12) return "nv12";
        if (subtype == PInvoke.MFVideoFormat_P010) return "p010le";
        if (subtype == PInvoke.MFVideoFormat_MJPG) return "mjpeg";
        return subtype.ToByteArray() is var bytes ? BitConverter.ToUInt32(bytes, 0) switch
        {
            0x00000014 => "bgr24",   // MFVideoFormat_RGB24 / D3DFMT_R8G8B8
            0x32595559 => "yuyv422", // YUY2
            0x3231564E => "nv12",    // NV12
            0x30313050 => "p010le",  // P010
            0x47504A4D => "mjpeg",   // MJPG
            _ => null
        } : null;
    }

    [SupportedOSPlatform("windows6.1")]
    private static unsafe string ReadString(IMFActivate activate, Guid key)
    {
        activate.GetAllocatedString(key, out PWSTR value, out _);
        try { return value.ToString(); }
        finally { if (value.Value != null) Marshal.FreeCoTaskMem((nint)value.Value); }
    }

    [SupportedOSPlatform("windows6.1")]
    private static T WithMediaFoundation<T>(Func<T> action)
    {
        HRESULT initialized = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
        bool uninitialize = initialized.Succeeded;
        if (initialized.Failed && (int)initialized != RpcEChangedMode) initialized.ThrowOnFailure();
        PInvoke.MFStartup(MfVersion, 0).ThrowOnFailure();
        try { return action(); }
        finally
        {
            PInvoke.MFShutdown().ThrowOnFailure();
            if (uninitialize) PInvoke.CoUninitialize();
        }
    }

    [SupportedOSPlatform("windows6.1")]
    private static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
