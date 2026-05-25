using Jalium.UI.Media.Native;

namespace Jalium.UI.Media;

/// <summary>
/// Managed wrapper around a native <c>JaliumVideoSurface</c>. Backed by the
/// <c>jalium.native.core</c> rendering backend (D3D12 / Vulkan / Software);
/// stage 1 only exposes the API surface — actual GPU upload lands in stage 2
/// when the backend video-surface implementations are filled in.
/// </summary>
/// <remarks>
/// <para><b>Fallback chain</b>:
/// <list type="number">
///   <item>External-kind constructors (D3D11 / AHardwareBuffer / IOSurface)
///   return <see langword="null"/> on platforms / backends that haven't
///   implemented the corresponding <c>WrapExternalVideoSurface</c> branch yet.</item>
///   <item><see cref="CreateBgra8"/> throws <see cref="NativeMediaException"/>
///   when the backend doesn't implement <c>CreateVideoSurface</c> yet — caller
///   catches and falls back to <see cref="WriteableBitmap"/>.</item>
/// </list>
/// </para>
/// <para><b>Lifecycle</b>: single-threaded ownership. Lock / Unlock must be called
/// from the same thread that constructed the surface. Pass through
/// <see cref="Dispose"/> when done.</para>
/// </remarks>
public sealed class NativeVideoSurface : IDisposable
{
    private nint _handle;
    private readonly int _width;
    private readonly int _height;
    private readonly NativeVideoSurfaceKind _kind;

    /// <summary>Opaque native handle. Managed code outside this assembly should
    /// not touch it; intended for the framework's render dispatch.</summary>
    internal nint Handle => _handle;

    /// <summary>Native pixel width.</summary>
    public int PixelWidth => _width;

    /// <summary>Native pixel height.</summary>
    public int PixelHeight => _height;

    /// <summary>What kind of GPU resource backs this surface.</summary>
    public NativeVideoSurfaceKind Kind => _kind;

    /// <summary>True once <see cref="Dispose"/> has been called.</summary>
    public bool IsDisposed => _handle == nint.Zero;

    private NativeVideoSurface(nint handle, int width, int height, NativeVideoSurfaceKind kind)
    {
        _handle = handle;
        _width = width;
        _height = height;
        _kind = kind;
    }

    /// <summary>
    /// Creates a CPU-owned BGRA8 surface for the given context. The active backend
    /// owns staging-to-GPU; on stage 1 every backend returns nullptr and this
    /// method throws — caller should catch and fall back.
    /// </summary>
    /// <exception cref="NativeMediaException">
    /// The active backend has not implemented <c>CreateVideoSurface</c> yet,
    /// or context is invalid.
    /// </exception>
    public static NativeVideoSurface CreateBgra8(nint context, int width, int height)
    {
        if (context == nint.Zero) throw new ArgumentException("Native context is null.", nameof(context));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var handle = NativeVideoSurfaceInterop.Create(
            context, (uint)width, (uint)height, (uint)NativeVideoSurfaceFormat.Bgra8);
        if (handle == nint.Zero)
        {
            throw new NativeMediaException(NativeMediaStatus.NotImplemented,
                "jalium_video_surface_create");
        }
        return new NativeVideoSurface(handle, width, height, NativeVideoSurfaceKind.Bgra8Cpu);
    }

    /// <summary>
    /// Imports an external GPU resource. Returns <see langword="null"/> if the
    /// active backend / platform does not support the requested kind so the caller
    /// can transparently fall back to the BGRA8 CPU path.
    /// </summary>
    public static NativeVideoSurface? TryWrapExternal(
        nint context,
        NativeVideoSurfaceKind kind,
        int width,
        int height,
        ulong handle0,
        ulong handle1 = 0,
        NativeVideoSurfaceFormat formatHint = NativeVideoSurfaceFormat.Bgra8)
    {
        if (context == nint.Zero || width <= 0 || height <= 0) return null;

        var desc = new NativeVideoSurfaceInterop.NativeVideoSurfaceDescriptor
        {
            Kind = (int)kind,
            Width = (uint)width,
            Height = (uint)height,
            Handle0 = handle0,
            Handle1 = handle1,
            FormatHint = (uint)formatHint,
        };
        var nh = NativeVideoSurfaceInterop.WrapExternal(context, in desc);
        return nh == nint.Zero ? null : new NativeVideoSurface(nh, width, height, kind);
    }

    /// <summary>
    /// Maps the staging buffer for CPU write (BGRA8 surfaces only). The returned
    /// <see cref="LockedFrame"/> must be disposed to flush the upload to GPU.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The surface was already disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The surface is an external import (no CPU staging path) or the backend
    /// failed to map.
    /// </exception>
    public LockedFrame Lock()
    {
        ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
        unsafe
        {
            int ok = NativeVideoSurfaceInterop.Lock(_handle, out var ptr, out var stride);
            if (ok == 0 || ptr == null)
            {
                throw new InvalidOperationException(
                    "jalium_video_surface_lock failed — surface kind doesn't support CPU staging or backend stub.");
            }
            int byteCount = checked((int)stride * _height);
            var span = new Span<byte>(ptr, byteCount);
            return new LockedFrame(this, span, (int)stride);
        }
    }

    /// <summary>Released by <see cref="LockedFrame.Dispose"/>.</summary>
    private void UnlockFull()
    {
        if (_handle == nint.Zero) return;
        NativeVideoSurfaceInterop.Unlock(_handle, nint.Zero);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var h = _handle;
        if (h == nint.Zero) return;
        _handle = nint.Zero;
        NativeVideoSurfaceInterop.Destroy(h);
    }

    /// <summary>
    /// A scope-bound writable view of the surface's staging buffer.
    /// Dispose to commit the upload and release the lock.
    /// </summary>
    public readonly ref struct LockedFrame
    {
        private readonly NativeVideoSurface _surface;

        /// <summary>BGRA8 pixel span; length == Stride * PixelHeight.</summary>
        public Span<byte> Pixels { get; }

        /// <summary>Row pitch in bytes.</summary>
        public int Stride { get; }

        internal LockedFrame(NativeVideoSurface surface, Span<byte> pixels, int stride)
        {
            _surface = surface;
            Pixels = pixels;
            Stride = stride;
        }

        /// <summary>Commits the write and releases the lock.</summary>
        public void Dispose() => _surface.UnlockFull();
    }
}

/// <summary>
/// Process-wide telemetry snapshot of <c>jalium_video_surface_*</c> usage.
/// Mirrors the native <c>JaliumVideoSurfaceStats</c> POD.
/// </summary>
public readonly record struct VideoSurfaceStats(
    ulong Version,
    ulong SurfacesCreated,
    ulong SurfacesDestroyed,
    ulong CpuUploads,
    ulong CpuUploadBytes,
    ulong ExternalImports,
    ulong ExternalImportFails,
    long  GpuResidentBytes)
{
    public static VideoSurfaceStats Query()
    {
        NativeVideoSurfaceInterop.QueryStats(out var s);
        return new VideoSurfaceStats(
            s.Version, s.SurfacesCreated, s.SurfacesDestroyed,
            s.CpuUploads, s.CpuUploadBytes,
            s.ExternalImports, s.ExternalImportFails,
            s.GpuResidentBytes);
    }

    public static void Reset() => NativeVideoSurfaceInterop.ResetStats();
}
