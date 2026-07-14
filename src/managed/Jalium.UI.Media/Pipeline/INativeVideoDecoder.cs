using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Pipeline;

// NativeVideoSurface lives in the Jalium.UI.Media namespace; default-interface
// AcquireGpuSurface() returns null so existing decoders compile unchanged.

/// <summary>
/// 平台原生视频解码器抽象。Windows 由 Media Foundation <c>IMFSourceReader</c> 实现、
/// Android 由 <c>AMediaExtractor + AMediaCodec</c> 实现。
/// </summary>
/// <remarks>
/// 实现禁止依赖 OpenCV、FFmpeg.AutoGen、libvlc 等。音频由 SoundFlow + FFmpeg 单独管理。
/// </remarks>
public interface INativeVideoDecoder : IDisposable
{
    /// <summary>
    /// 打开视频源（本地文件或 HTTP URL）。
    /// </summary>
    /// <exception cref="NotSupportedException">视频编码器不在 <see cref="ActiveVideoCodec"/> 探测到的支持范围内。</exception>
    void Open(Uri source, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8);

    /// <summary>
    /// 同步读取下一帧。返回 false 表示流结束。
    /// </summary>
    /// <param name="frame">解码后的池化帧。调用方负责 Dispose。</param>
    bool TryReadFrame(out MediaFrame? frame);

    /// <summary>
    /// 跳转到指定播放位置（基于关键帧最近邻）。
    /// </summary>
    void Seek(TimeSpan position);

    /// <summary>原始视频时长（不可知时返回 <see cref="TimeSpan.Zero"/>）。</summary>
    TimeSpan Duration { get; }

    /// <summary>原始视频帧率。</summary>
    double Fps { get; }

    /// <summary>原始视频宽度（像素）。</summary>
    int Width { get; }

    /// <summary>原始视频高度（像素）。</summary>
    int Height { get; }

    /// <summary>当前激活的视频编解码器。</summary>
    SupportedCodec ActiveVideoCodec { get; }

    /// <summary>
    /// 尝试拿到一个 GPU-resident 的视频帧(stage 3+ Windows MF DXVA / Android
    /// MediaCodec SurfaceTexture / Apple VTDecompressionSession 等硬件解码路径
    /// 的产出),省去 CPU → GPU 的拷贝。
    /// </summary>
    /// <param name="renderContextHandle">
    /// framework <c>JaliumContext*</c> handle(从 <c>Jalium.UI.Interop.RenderContext.Handle</c>),
    /// 用于 <c>jalium_video_surface_wrap_external</c> 把 decoder 给的 native handle
    /// import 成 render-backend 能采样的 GPU 资源。
    /// </param>
    /// <remarks>
    /// 默认返 <see langword="null"/>(decoder 没接 GPU 路径,caller fallback 走
    /// <see cref="TryReadFrame(out MediaFrame?)"/> + NativeVideoSurface BGRA staging
    /// 或最终 WriteableBitmap 路径)。stage 3 真填后 Windows MF DXVA decoder 返非 null。
    /// </remarks>
    NativeVideoSurface? AcquireGpuSurface(nint renderContextHandle)
    {
        return null;
    }
}

/// <summary>Result of pulling a decoder frame directly as an external GPU surface.</summary>
public enum GpuVideoFrameReadResult
{
    /// <summary>The decoder is using its CPU frame pipeline.</summary>
    NotSupported,
    /// <summary>A GPU surface and presentation timestamp were returned.</summary>
    Frame,
    /// <summary>The media stream reached end of stream.</summary>
    EndOfStream,
    /// <summary>The external format/modifier was rejected and the decoder switched to CPU output.</summary>
    FellBackToCpu,
}

/// <summary>
/// Optional zero-copy extension implemented by decoders that can pull the next
/// frame directly as an external GPU resource, without first allocating a CPU
/// <see cref="MediaFrame"/>.
/// </summary>
public interface INativeGpuVideoDecoder
{
    /// <summary>Whether the GPU path has not yet been ruled out for this source.</summary>
    bool MayHaveGpuOutput { get; }

    /// <summary>Pulls and imports the next GPU frame into the active render context.</summary>
    GpuVideoFrameReadResult TryReadGpuFrame(
        nint renderContextHandle,
        out NativeVideoSurface? surface,
        out TimeSpan presentationTime);
}

/// <summary>
/// <see cref="INativeVideoDecoder"/> 工厂。注入到 <see cref="MediaPlayer"/> / <see cref="VideoDrawing"/>。
/// </summary>
public interface INativeVideoDecoderFactory
{
    /// <summary>创建一个未打开的解码器实例。</summary>
    INativeVideoDecoder Create(IMediaFramePool? framePool = null);

    /// <summary>查询当前平台支持的编解码器。</summary>
    SupportedCodec GetSupportedCodecs();
}
