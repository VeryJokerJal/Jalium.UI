using System.Runtime.InteropServices;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Pipeline;

namespace Jalium.UI.Media.Native;

/// <summary>
/// <see cref="INativeCameraSource"/> 的默认实现：调用 <see cref="NativeMediaInterop"/>。
/// </summary>
public sealed class NativeCameraSource : INativeCameraSource
{
    private readonly IMediaFramePool _pool;
    private nint _handle;
    private bool _disposed;

    /// <summary>初始化新的采集源实例。</summary>
    public NativeCameraSource(IMediaFramePool? framePool = null)
    {
        NativeMediaInitializer.EnsureInitialized();
        _pool = framePool ?? DefaultMediaFramePool.Shared;
    }

    /// <inheritdoc />
    public void Open(string deviceId, int requestedWidth, int requestedHeight, double requestedFps,
                     NativePixelFormat requestedFormat = NativePixelFormat.Bgra8)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var status = NativeMediaInterop.jalium_camera_open(
            deviceId, (uint)requestedWidth, (uint)requestedHeight, requestedFps,
            NativeMediaInterop.ToNative(requestedFormat), out _handle);
        NativeMediaException.ThrowIfFailed(status, "jalium_camera_open");
    }

    /// <inheritdoc />
    public bool TryReadFrame(out MediaFrame? frame)
    {
        frame = null;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle == nint.Zero) return false;

        var status = NativeMediaInterop.jalium_camera_read_frame(_handle, out var native);
        if (status == NativeMediaStatus.EndOfStream) return false;
        NativeMediaException.ThrowIfFailed(status, "jalium_camera_read_frame");

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
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_handle != nint.Zero)
        {
            NativeMediaInterop.jalium_camera_close(_handle);
            _handle = nint.Zero;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != nint.Zero)
        {
            NativeMediaInterop.jalium_camera_close(_handle);
            _handle = nint.Zero;
        }
    }
}

/// <summary>
/// <see cref="INativeCameraSourceFactory"/> 的默认实现。
/// </summary>
public sealed class NativeCameraSourceFactory : INativeCameraSourceFactory
{
    /// <summary>初始化工厂，确保原生库已加载。</summary>
    public NativeCameraSourceFactory()
    {
        NativeMediaInitializer.EnsureInitialized();
    }

    /// <inheritdoc />
    public IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
    {
        var status = NativeMediaInterop.jalium_camera_enumerate(out var raw, out var count);
        NativeMediaException.ThrowIfFailed(status, "jalium_camera_enumerate");

        if (raw == nint.Zero || count == 0) return Array.Empty<CameraDeviceInfo>();

        try
        {
            var result = new CameraDeviceInfo[count];
            var size = Marshal.SizeOf<NativeMediaInterop.NativeCameraDevice>();
            for (var i = 0; i < count; i++)
            {
                var native = Marshal.PtrToStructure<NativeMediaInterop.NativeCameraDevice>(raw + i * size);
                var formats = ReadFormats(native.Formats, native.FormatCount);
                result[i] = new CameraDeviceInfo(
                    Marshal.PtrToStringUTF8(native.Id) ?? string.Empty,
                    Marshal.PtrToStringUTF8(native.FriendlyName) ?? string.Empty,
                    (CameraFacing)native.Facing,
                    formats);
            }
            return result;
        }
        finally
        {
            NativeMediaInterop.jalium_camera_devices_free(raw, count);
        }
    }

    /// <inheritdoc />
    public INativeCameraSource Create(IMediaFramePool? framePool = null) => new NativeCameraSource(framePool);

    private static IReadOnlyList<CameraFormat> ReadFormats(nint ptr, uint count)
    {
        if (ptr == nint.Zero || count == 0) return Array.Empty<CameraFormat>();
        var result = new CameraFormat[count];
        var size = Marshal.SizeOf<NativeMediaInterop.NativeCameraFormat>();
        for (var i = 0; i < count; i++)
        {
            var nf = Marshal.PtrToStructure<NativeMediaInterop.NativeCameraFormat>(ptr + i * size);
            result[i] = new CameraFormat((int)nf.Width, (int)nf.Height, nf.Fps);
        }
        return result;
    }
}
