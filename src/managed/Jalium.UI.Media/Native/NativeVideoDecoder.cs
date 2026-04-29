using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="INativeVideoDecoder"/> 的默认实现：调用 <see cref="NativeMediaInterop"/>。
/// </summary>
public sealed class NativeVideoDecoder : INativeVideoDecoder
{
    private readonly IMediaFramePool _pool;
    private nint _handle;
    private NativeMediaInterop.NativeVideoInfo _info;
    private NativePixelFormat _requestedFormat;
    private bool _disposed;

    /// <summary>初始化新的解码器实例。</summary>
    public NativeVideoDecoder(IMediaFramePool? framePool = null)
    {
        NativeMediaInitializer.EnsureInitialized();
        _pool = framePool ?? DefaultMediaFramePool.Shared;
    }

    /// <inheritdoc />
    public TimeSpan Duration => TimeSpan.FromSeconds(_info.DurationSeconds);

    /// <inheritdoc />
    public double Fps => _info.FrameRate;

    /// <inheritdoc />
    public int Width => (int)_info.Width;

    /// <inheritdoc />
    public int Height => (int)_info.Height;

    /// <inheritdoc />
    public SupportedCodec ActiveVideoCodec => (SupportedCodec)_info.ActiveCodec;

    /// <inheritdoc />
    public void Open(Uri source, NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var path = source.IsFile ? source.LocalPath : source.ToString();
        _requestedFormat = requestedFormat;

        var status = NativeMediaInterop.jalium_video_decoder_open_file(
            path, NativeMediaInterop.ToNative(requestedFormat), out _handle);
        NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_open_file");

        status = NativeMediaInterop.jalium_video_decoder_get_info(_handle, out _info);
        NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_get_info");
    }

    /// <inheritdoc />
    public bool TryReadFrame(out MediaFrame? frame)
    {
        frame = null;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Open must be called before TryReadFrame.");
        }

        var status = NativeMediaInterop.jalium_video_decoder_read_frame(_handle, out var native);
        if (status == NativeMediaStatus.EndOfStream) return false;
        NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_read_frame");

        // 帧缓冲由解码器拥有，仅在下次 read_frame / close 之前有效。
        // 这里复制到池化 MediaFrame，调用方拿到的是独立缓冲，可异步消费。
        var pts = TimeSpan.FromMicroseconds(native.PtsMicroseconds);
        frame = _pool.Rent((int)native.Width, (int)native.Height, (int)native.StrideBytes, pts,
            NativeMediaInterop.FromNative(native.Format));
        var size = checked((int)native.StrideBytes * (int)native.Height);
        unsafe
        {
            fixed (byte* dst = frame.Pixels.Span)
            {
                Buffer.MemoryCopy((void*)native.Pixels, dst, size, size);
            }
        }
        return true;
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Open must be called before Seek.");
        }
        var status = NativeMediaInterop.jalium_video_decoder_seek_microseconds(_handle, (long)position.TotalMicroseconds);
        NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_seek_microseconds");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != nint.Zero)
        {
            NativeMediaInterop.jalium_video_decoder_close(_handle);
            _handle = nint.Zero;
        }
    }

    /// <summary>未使用，保留供未来扩展。</summary>
    internal NativePixelFormat RequestedFormat => _requestedFormat;
}

/// <summary>
/// <see cref="INativeVideoDecoderFactory"/> 的默认实现。
/// </summary>
public sealed class NativeVideoDecoderFactory : INativeVideoDecoderFactory
{
    /// <summary>初始化工厂，确保原生库已加载。</summary>
    public NativeVideoDecoderFactory()
    {
        NativeMediaInitializer.EnsureInitialized();
    }

    /// <inheritdoc />
    public INativeVideoDecoder Create(IMediaFramePool? framePool = null) => new NativeVideoDecoder(framePool);

    /// <inheritdoc />
    public SupportedCodec GetSupportedCodecs() => (SupportedCodec)NativeMediaInterop.jalium_media_supported_video_codecs();
}
