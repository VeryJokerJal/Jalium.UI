using System.Buffers;

namespace Jalium.UI.Media.Imaging;

/// <summary>
/// 池化的 BGRA8 / RGBA8 帧句柄。Dispose 时归还池。
/// </summary>
/// <remarks>
/// 视频解码与摄像头采集的热路径专用：旧 OpenCV 时代每帧 <c>new byte[w*h*4]</c>
/// 在 1080p 30fps 下约 240MB/s 的 GC 压力，新管道用此类零稳态分配。
/// </remarks>
public sealed class MediaFrame : IDisposable
{
    private byte[]? _buffer;
    private readonly ArrayPool<byte> _pool;
    private readonly int _bufferSize;

    internal MediaFrame(
        ArrayPool<byte> pool,
        int width,
        int height,
        int stride,
        TimeSpan presentationTime,
        NativePixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);

        _pool = pool;
        Width = width;
        Height = height;
        Stride = stride;
        PresentationTime = presentationTime;
        Format = format;

        _bufferSize = checked(stride * height);
        _buffer = pool.Rent(_bufferSize);
    }

    /// <summary>
    /// 帧像素缓冲。仅在 Dispose 之前有效。
    /// </summary>
    public Memory<byte> Pixels
    {
        get
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return _buffer.AsMemory(0, _bufferSize);
        }
    }

    /// <summary>像素宽度。</summary>
    public int Width { get; }

    /// <summary>像素高度。</summary>
    public int Height { get; }

    /// <summary>行跨度（字节）。至少 <c>Width * 4</c>。</summary>
    public int Stride { get; }

    /// <summary>呈现时间戳（PTS）。</summary>
    public TimeSpan PresentationTime { get; }

    /// <summary>像素格式。</summary>
    public NativePixelFormat Format { get; }

    /// <summary>
    /// 归还缓冲到池。
    /// </summary>
    public void Dispose()
    {
        var buffer = _buffer;
        if (buffer is null) return;
        _buffer = null;
        _pool.Return(buffer, clearArray: false);
    }
}
