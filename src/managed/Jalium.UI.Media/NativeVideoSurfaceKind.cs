namespace Jalium.UI.Media;

/// <summary>
/// What kind of GPU resource (or CPU buffer) a <see cref="NativeVideoSurface"/>
/// wraps. Mirrors the native <c>JaliumVideoSurfaceKind</c> enum in
/// <c>jalium_video_surface.h</c>.
/// </summary>
/// <remarks>
/// Stage 1 (this commit) wires the type system; only <see cref="Bgra8Cpu"/> is
/// actually created at this point. External-kind imports (D3D11SharedTexture,
/// AHardwareBuffer, IOSurface) land in stages 3-5 along with the per-platform
/// hardware decoder bridges.
/// </remarks>
public enum NativeVideoSurfaceKind
{
    /// <summary>CPU-side BGRA8 buffer; backend stages to GPU on Unlock.</summary>
    Bgra8Cpu = 0,

    /// <summary>Windows: ID3D11Texture2D shared via NT handle. Stage 3.</summary>
    D3D11SharedTexture = 1,

    /// <summary>Cross-platform: VkImage via external memory extension. Stage 5.</summary>
    VkImageExternal = 2,

    /// <summary>Android AHardwareBuffer (MediaCodec output). Stage 4.</summary>
    AHardwareBuffer = 3,

    /// <summary>Apple IOSurface (CoreVideo). Future Apple media module.</summary>
    IOSurface = 4,

    /// <summary>Direct Metal MTLTexture. Future Apple media module.</summary>
    MetalTexture = 5,

    /// <summary>CoreVideo CVPixelBufferRef wrapping IOSurface. Future Apple.</summary>
    CVPixelBuffer = 6,

    /// <summary>
    /// Linux dma-buf exported by VAAPI/GStreamer. The descriptor ABI can
    /// carry multiple planes; the current Vulkan path imports only a
    /// single-plane packed RGB layout and rejects other layouts for CPU
    /// fallback.
    /// </summary>
    LinuxDmaBuf = 7,
}

/// <summary>
/// Pixel format hint for <see cref="NativeVideoSurface"/> create / wrap.
/// v1 only implements BGRA8; NV12 / P010 etc. are reserved for direct-from-decoder
/// paths (stage 3+).
/// </summary>
public enum NativeVideoSurfaceFormat
{
    Bgra8   = 0,
    Nv12    = 1,
    P010    = 2,
    Rgb10A2 = 3,
}
