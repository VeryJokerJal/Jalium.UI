using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="INativeVideoDecoder"/> 的默认实现：调用 <see cref="NativeMediaInterop"/>。
/// </summary>
public sealed class NativeVideoDecoder : INativeVideoDecoder, INativeGpuVideoDecoder
{
    private readonly IMediaFramePool _pool;
    private nint _handle;
    private NativeMediaInterop.NativeVideoInfo _info;
    private NativePixelFormat _requestedFormat;
    private bool _disposed;
    private bool _mayHaveGpuOutput;

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

        CloseHandle();
        var path = source.IsFile ? source.LocalPath : source.ToString();
        _requestedFormat = requestedFormat;
        var status = NativeMediaInterop.jalium_video_decoder_open_file(
            path, NativeMediaInterop.ToNative(requestedFormat), out var handle);
        NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_open_file");

        try
        {
            status = NativeMediaInterop.jalium_video_decoder_get_info(handle, out var info);
            NativeMediaException.ThrowIfFailed(status, "jalium_video_decoder_get_info");
            _handle = handle;
            _info = info;
            _mayHaveGpuOutput = OperatingSystem.IsLinux();
        }
        catch
        {
            NativeMediaInterop.jalium_video_decoder_close(handle);
            throw;
        }
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
    public NativeVideoSurface? AcquireGpuSurface(nint renderContextHandle)
    {
        if (_disposed || _handle == nint.Zero || renderContextHandle == nint.Zero) return null;
        var status = NativeMediaInterop.jalium_video_decoder_acquire_gpu_surface_descriptor(
            _handle, out var desc);
        if (status != NativeMediaStatus.Ok) return null;
        if (desc.Width == 0 || desc.Height == 0 || desc.Handle0 == 0) return null;

        return NativeVideoSurface.TryWrapExternal(
            renderContextHandle,
            (NativeVideoSurfaceKind)desc.Kind,
            (int)desc.Width,
            (int)desc.Height,
            desc.Handle0,
            desc.Handle1,
            (NativeVideoSurfaceFormat)desc.FormatHint);
    }

    /// <inheritdoc />
    public bool MayHaveGpuOutput => _mayHaveGpuOutput && !_disposed && _handle != nint.Zero;

    /// <inheritdoc />
    public GpuVideoFrameReadResult TryReadGpuFrame(
        nint renderContextHandle,
        out NativeVideoSurface? surface,
        out TimeSpan presentationTime)
    {
        surface = null;
        presentationTime = TimeSpan.Zero;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == nint.Zero)
            throw new InvalidOperationException("Open must be called before TryReadGpuFrame.");
        if (!_mayHaveGpuOutput || !OperatingSystem.IsLinux() ||
            renderContextHandle == nint.Zero)
            return GpuVideoFrameReadResult.NotSupported;

        var status = NativeMediaInterop.jalium_video_decoder_read_gpu_frame_descriptor(
            _handle, out var descriptor, out var ptsMicroseconds, out _);
        if (status == NativeMediaStatus.NotImplemented)
        {
            _mayHaveGpuOutput = false;
            return GpuVideoFrameReadResult.NotSupported;
        }
        if (status == NativeMediaStatus.EndOfStream)
            return GpuVideoFrameReadResult.EndOfStream;
        if (status != NativeMediaStatus.Ok)
        {
            _mayHaveGpuOutput = false;
            _ = NativeMediaInterop.jalium_video_decoder_disable_gpu_output(_handle);
            return GpuVideoFrameReadResult.FellBackToCpu;
        }

        try
        {
            presentationTime = TimeSpan.FromMicroseconds(ptsMicroseconds);
            surface = NativeVideoSurface.TryWrapExternal(
                renderContextHandle, in descriptor);
        }
        finally
        {
            // The renderer duplicates borrowed dma-buf/sync fds during import;
            // close the decoder-owned descriptor copies on both success and
            // rejection so a long video cannot leak one fd per plane/frame.
            NativeMediaInterop.jalium_video_decoder_release_gpu_surface_descriptor(
                ref descriptor);
        }

        if (surface is not null)
            return GpuVideoFrameReadResult.Frame;

        _mayHaveGpuOutput = false;
        status = NativeMediaInterop.jalium_video_decoder_disable_gpu_output(_handle);
        NativeMediaException.ThrowIfFailed(
            status, "jalium_video_decoder_disable_gpu_output");
        return GpuVideoFrameReadResult.FellBackToCpu;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseHandle();
    }

    private void CloseHandle()
    {
        if (_handle != nint.Zero)
        {
            NativeMediaInterop.jalium_video_decoder_close(_handle);
            _handle = nint.Zero;
        }
        _info = default;
        _mayHaveGpuOutput = false;
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
