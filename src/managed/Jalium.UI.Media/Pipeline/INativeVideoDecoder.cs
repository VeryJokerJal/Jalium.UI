using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Pipeline;

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
