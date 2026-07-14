namespace Jalium.UI.Media.Pipeline;

/// <summary>Identifies the type of an embedded media stream.</summary>
public enum MediaTrackKind
{
    Audio = 1,
    Subtitle = 2,
}

/// <summary>Metadata for an audio or subtitle stream in a media source.</summary>
public sealed record MediaTrackInfo(
    MediaTrackKind Kind,
    int Index,
    string Id,
    string Label,
    string Language,
    string Codec,
    int Channels,
    int SampleRate,
    bool IsDefault,
    bool IsForced);

/// <summary>Linux media features that are usable in the current process.</summary>
[Flags]
public enum LinuxMediaCapability : uint
{
    None = 0,
    GStreamerRuntime = 1u << 0,
    VideoCpuFrames = 1u << 1,
    AudioDecode = 1u << 2,
    CameraCapture = 1u << 3,
    MicrophoneCapture = 1u << 4,
    TrackDiscovery = 1u << 5,
    SubtitleDecode = 1u << 6,
    /// <summary>
    /// An actual decoder sample has exported a Vulkan-importable,
    /// single-plane packed RGB dma-buf in this process. This is not a generic
    /// VAAPI capability; NV12/P010, DMA_DRM and multi-plane output fall back
    /// to CPU frames.
    /// </summary>
    DmaBufExport = 1u << 7,
}
