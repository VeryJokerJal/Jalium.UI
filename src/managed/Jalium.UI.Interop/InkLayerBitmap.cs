using System.Runtime.InteropServices;
using System.Threading;

namespace Jalium.UI.Interop;

/// <summary>
/// Result codes returned by <see cref="InkLayerBitmap.DispatchBrush"/> (the
/// managed mirror of the native <c>JaliumInkDispatchResult</c> enum in
/// <c>jalium_types.h</c> — keep the two in sync).
/// </summary>
/// <remarks>
/// <para>
/// The codes are <em>backend-agnostic by contract</em>: every native backend
/// classifies its internal failure reasons into these categories before
/// returning, so callers pick their recovery strategy from the code alone and
/// never need to know which backend produced it.
/// </para>
/// <para>
/// <see cref="Transient"/> means the layer and shader handles are still
/// healthy and retrying the <em>same</em> handles next frame is expected to
/// succeed — callers must not tear down / rebuild the ink resource chain for
/// it. <see cref="StaleContext"/> means the device generation behind the
/// handles is gone or inconsistent — retrying can never succeed and the whole
/// ink resource chain (layer bitmaps + every shader handle) must be rebuilt so
/// everything re-pairs on the current generation.
/// </para>
/// <para>
/// <see cref="Transient"/> / <see cref="StaleContext"/> live far away from
/// the legacy per-backend raw codes (-1..-7, retired) so a stale comparison
/// against a historical code can never misclassify them.
/// </para>
/// </remarks>
public static class InkDispatchResult
{
    /// <summary>Success — any value &gt;= 0.</summary>
    public const int Ok = 0;

    /// <summary>Malformed call (null handle/pointer, too few points, bad
    /// constants size). Never retryable; skip the stroke.</summary>
    public const int InvalidArg = -1;

    /// <summary>The layer's backing resources are absent (construction or
    /// resize failed earlier). Recovery is the normal layer (re)construction
    /// path, not a retry.</summary>
    public const int InvalidState = -2;

    /// <summary>Momentary resource failure inside the dispatch; retry the
    /// same handles next frame. Do NOT rebuild the ink resource chain.</summary>
    public const int Transient = -100;

    /// <summary>Device generation lost or handles baked on mismatched
    /// generations; rebuild the whole ink resource chain.</summary>
    public const int StaleContext = -101;
}

/// <summary>
/// Native ink/brush operations behind a seam so unit tests can substitute the
/// GPU calls whose real backends require a compute pipeline that is unavailable
/// in headless / WARP CI. Production code always uses
/// <see cref="DefaultInkNativeOps.Instance"/>; tests inject a fake to verify the
/// context-lifetime (pin / unpin / destroy-ordering) contract deterministically.
/// </summary>
internal interface IInkNativeOps
{
    nint CreateInkLayerBitmap(nint context, int width, int height);
    void DestroyInkLayerBitmap(nint handle);
    int ResizeInkLayerBitmap(nint handle, int width, int height);
    void ClearInkLayerBitmap(nint handle, float r, float g, float b, float a);
    int DispatchBrush(
        nint bitmap, nint shader,
        ReadOnlySpan<BrushStrokePoint> points,
        in BrushConstantsNative constants,
        ReadOnlySpan<byte> extraParams);
    nint CreateBrushShader(nint context, string shaderKey, string brushMainHlsl, int blendMode);
    void DestroyBrushShader(nint handle);
}

/// <summary>Production <see cref="IInkNativeOps"/> — thin P/Invoke forwarders.</summary>
internal sealed class DefaultInkNativeOps : IInkNativeOps
{
    public static readonly DefaultInkNativeOps Instance = new();

    public nint CreateInkLayerBitmap(nint context, int width, int height)
        => NativeMethods.InkLayerBitmapCreate(context, width, height);

    public void DestroyInkLayerBitmap(nint handle)
        => NativeMethods.InkLayerBitmapDestroy(handle);

    public int ResizeInkLayerBitmap(nint handle, int width, int height)
        => NativeMethods.InkLayerBitmapResize(handle, width, height);

    public void ClearInkLayerBitmap(nint handle, float r, float g, float b, float a)
        => NativeMethods.InkLayerBitmapClear(handle, r, g, b, a);

    public unsafe int DispatchBrush(
        nint bitmap, nint shader,
        ReadOnlySpan<BrushStrokePoint> points,
        in BrushConstantsNative constants,
        ReadOnlySpan<byte> extraParams)
    {
        fixed (BrushStrokePoint* pPoints = points)
        fixed (BrushConstantsNative* pConst = &constants)
        fixed (byte* pExtras = extraParams)
        {
            return NativeMethods.InkLayerBitmapDispatchBrush(
                bitmap, shader,
                pPoints, points.Length, pConst,
                extraParams.IsEmpty ? null : pExtras,
                extraParams.Length);
        }
    }

    public nint CreateBrushShader(nint context, string shaderKey, string brushMainHlsl, int blendMode)
        => NativeMethods.BrushShaderCreate(context, shaderKey, brushMainHlsl, blendMode);

    public void DestroyBrushShader(nint handle)
        => NativeMethods.BrushShaderDestroy(handle);
}

/// <summary>
/// GPU-side persistent RGBA8 bitmap used by <c>InkCanvas</c> as its
/// committed-ink layer. Brush shaders dispatch directly into this bitmap
/// on stroke commit; every subsequent frame just blits the bitmap to the
/// main render target — committed strokes cost O(1) per frame instead of
/// O(N strokes) CPU rasterization.
/// </summary>
/// <remarks>
/// <para>
/// The bitmap lives on the context it was created from; it must be
/// disposed (or GC-collected) before the context is disposed. Resize
/// reallocates the backing texture and clears its contents.
/// </para>
/// <para>
/// To make the "dispose before the context" contract <em>self-enforcing</em>
/// — rather than a comment the device-lost recovery path can violate — the
/// bitmap registers itself as a dependent resource on its owning context
/// (<see cref="RenderContext.RegisterRenderTarget"/> /
/// <see cref="RenderContext.UnregisterRenderTarget"/>). The native handle
/// routes its destroy back through the backend that created it; if that
/// backend is deleted while the handle is still alive (a forced context
/// replacement during recovery deletes the retired context's backend as
/// soon as its active-resource count hits zero, but InkCanvas only tears
/// the stale handles down on the <em>following</em> frame), the destroy is a
/// virtual call on freed memory — a use-after-free. Counting the handle as
/// an active dependent keeps the retired context (and its backend) alive
/// until the handle is actually disposed, closing that window.
/// </para>
/// </remarks>
public sealed class InkLayerBitmap : IDisposable
{
    private readonly RenderContext _context;
    private readonly IInkNativeOps _native;
    private nint _handle;
    private int _width;
    private int _height;
    private bool _disposed;
    // 0 = the context dependent-resource reference is still held; 1 = released.
    // Interlocked-guarded so the pin is dropped exactly once across the
    // Dispose / finalizer / failed-construction paths.
    private int _contextRefReleased;

    /// <summary>Pixel width of the backing texture.</summary>
    public int Width => _width;

    /// <summary>Pixel height of the backing texture.</summary>
    public int Height => _height;

    /// <summary>Raw native handle (JaliumInkLayerBitmap*).</summary>
    public nint Handle => _handle;

    /// <summary>True until <see cref="Dispose"/> is called.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>
    /// Allocates a new offscreen RGBA8 render target owned by the given
    /// context. Initial contents are cleared to transparent.
    /// </summary>
    public InkLayerBitmap(RenderContext context, int width, int height)
        : this(context, width, height, DefaultInkNativeOps.Instance)
    {
    }

    /// <summary>Test seam overload — see <see cref="IInkNativeOps"/>.</summary>
    internal InkLayerBitmap(RenderContext context, int width, int height, IInkNativeOps native)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(native);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _context = context;
        _native = native;
        _width = width;
        _height = height;

        // Pin the owning context BEFORE allocating the native bitmap: the
        // backend that backs this handle must outlive the handle. Counting
        // ourselves as an active dependent keeps a retired context (and its
        // backend) from being torn down while we still reference it — see the
        // class remarks for the device-lost-recovery race this guards.
        context.RegisterRenderTarget();
        try
        {
            _handle = _native.CreateInkLayerBitmap(context.Handle, width, height);
            if (_handle == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"Native ink layer allocation failed ({width}x{height}).");
            }
        }
        catch
        {
            // Allocation failed (e.g. a backend without the brush-shader
            // pipeline returns 0 → InvalidOperationException, which EnsureInkLayer
            // treats as a quiet fall-back signal). Undo the dependent-resource
            // pin so a handle that never came to exist doesn't keep the context
            // alive forever.
            ReleaseContextReference();
            throw;
        }
    }

    /// <summary>
    /// Reallocates the backing texture at the new size. Contents are
    /// reset to transparent — callers are responsible for replaying any
    /// strokes they want to preserve.
    /// </summary>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0 || height <= 0) return;
        if (width == _width && height == _height) return;

        int rc = _native.ResizeInkLayerBitmap(_handle, width, height);
        if (rc != 0)
        {
            throw new InvalidOperationException(
                $"Ink layer resize failed ({width}x{height}, rc={rc}).");
        }
        _width = width;
        _height = height;
    }

    /// <summary>Clears the bitmap to transparent.</summary>
    public void Clear() => Clear(0, 0, 0, 0);

    /// <summary>Clears the bitmap to a specific premultiplied RGBA color.</summary>
    public void Clear(float r, float g, float b, float a)
    {
        ThrowIfDisposed();
        _native.ClearInkLayerBitmap(_handle, r, g, b, a);
    }

    /// <summary>
    /// Dispatches a compiled brush shader over this bitmap. <paramref name="points"/>
    /// carries the stroke polyline; <paramref name="constants"/> is the
    /// 80-byte <see cref="BrushConstantsNative"/> struct whose last 16
    /// bytes (ViewportSize + Pad) are overwritten by the backend with
    /// this bitmap's pixel dimensions — callers leave them at zero.
    /// </summary>
    /// <returns>An <see cref="InkDispatchResult"/> code:
    /// <see cref="InkDispatchResult.Ok"/> on success;
    /// <see cref="InkDispatchResult.Transient"/> for a momentary failure
    /// (retry the same handles next frame);
    /// <see cref="InkDispatchResult.StaleContext"/> when the device
    /// generation behind the handles is gone (rebuild the ink resource
    /// chain). Backend-agnostic — callers never branch on the backend.</returns>
    public int DispatchBrush(
        BrushShaderHandle shader,
        ReadOnlySpan<BrushStrokePoint> points,
        in BrushConstantsNative constants,
        ReadOnlySpan<byte> extraParams = default)
    {
        ThrowIfDisposed();
        if (shader is null || !shader.IsValid) return InkDispatchResult.InvalidArg;
        if (points.Length < 2) return InkDispatchResult.InvalidArg;

        return _native.DispatchBrush(_handle, shader.Handle, points, in constants, extraParams);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Order matters. Destroy the native bitmap through the (still-live)
        // backend FIRST, then release our context pin. Releasing the pin can
        // drop the owning context's active-dependent count to zero and trigger
        // its deferred disposal (RenderContext.UnregisterRenderTarget →
        // TryDisposeRetiredContexts → jalium_context_destroy → delete backend).
        // Doing that before the native destroy would route the destroy to an
        // already-deleted backend — the original use-after-free.
        if (_handle != nint.Zero)
        {
            _native.DestroyInkLayerBitmap(_handle);
            _handle = nint.Zero;
        }
        ReleaseContextReference();
        GC.SuppressFinalize(this);
    }

    ~InkLayerBitmap() => Dispose();

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InkLayerBitmap));
    }

    private void ReleaseContextReference()
    {
        if (Interlocked.Exchange(ref _contextRefReleased, 1) != 0)
        {
            return;
        }
        _context.UnregisterRenderTarget();
    }
}

/// <summary>
/// Handle to a compiled brush pixel-shader + its PSO. Obtained by
/// <see cref="RenderContext.AcquireBrushShader"/>; owned by the context
/// and automatically released on context dispose. Safe to hold across
/// frames. Thread-safe to share; dispatch must be on the render thread.
/// </summary>
/// <remarks>
/// Like <see cref="InkLayerBitmap"/>, the handle pins its owning context as
/// an active dependent resource for its whole lifetime. The native destroy
/// routes back through the creating backend, so the backend must outlive the
/// handle; pinning the context prevents a forced context replacement (device
/// lost) from deleting that backend before the stale handle is disposed.
/// </remarks>
public sealed class BrushShaderHandle : IDisposable
{
    private readonly RenderContext _context;
    private readonly IInkNativeOps _native;
    private nint _handle;
    private bool _disposed;
    // See InkLayerBitmap._contextRefReleased — idempotent pin-release guard.
    private int _contextRefReleased;

    /// <summary>Raw native handle (JaliumBrushShader*).</summary>
    public nint Handle => _handle;

    /// <summary>True until <see cref="Dispose"/> is called.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    internal BrushShaderHandle(RenderContext context, nint handle, IInkNativeOps native)
    {
        _context = context;
        _native = native;
        _handle = handle;

        // Pin the owning context for the handle's lifetime (same dependent-
        // resource contract as InkLayerBitmap). The handle is only ever
        // constructed with a non-null native handle (see Create), so the pin
        // always corresponds to a live native resource.
        context.RegisterRenderTarget();
    }

    /// <summary>
    /// Compiles (or re-acquires) the HLSL for <paramref name="shaderKey"/> +
    /// <paramref name="brushMainHlsl"/> against the given context. Returns
    /// null on compile failure — caller should log and fall back.
    /// </summary>
    public static BrushShaderHandle? Create(
        RenderContext context,
        string shaderKey,
        string brushMainHlsl,
        int blendMode)
        => Create(context, shaderKey, brushMainHlsl, blendMode, DefaultInkNativeOps.Instance);

    /// <summary>Test seam overload — see <see cref="IInkNativeOps"/>.</summary>
    internal static BrushShaderHandle? Create(
        RenderContext context,
        string shaderKey,
        string brushMainHlsl,
        int blendMode,
        IInkNativeOps native)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shaderKey);
        ArgumentNullException.ThrowIfNull(brushMainHlsl);
        ArgumentNullException.ThrowIfNull(native);
        if (context.Handle == nint.Zero) return null;

        nint h = native.CreateBrushShader(context.Handle, shaderKey, brushMainHlsl, blendMode);
        if (h == nint.Zero) return null;
        return new BrushShaderHandle(context, h, native);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Destroy through the still-live backend FIRST, then drop the context
        // pin (which may trigger the retired context's deferred disposal and
        // delete the backend). See InkLayerBitmap.Dispose for the full
        // rationale — reversing the order reintroduces the use-after-free.
        if (_handle != nint.Zero)
        {
            _native.DestroyBrushShader(_handle);
            _handle = nint.Zero;
        }
        ReleaseContextReference();
        GC.SuppressFinalize(this);
    }

    ~BrushShaderHandle() => Dispose();

    private void ReleaseContextReference()
    {
        if (Interlocked.Exchange(ref _contextRefReleased, 1) != 0)
        {
            return;
        }
        _context.UnregisterRenderTarget();
    }
}

/// <summary>
/// One stroke point uploaded to the brush shader's StrokePoints SRV.
/// Layout must match the HLSL <c>StrokePoint</c> struct in the preamble
/// (16 bytes: x, y, pressure, pad).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BrushStrokePoint
{
    public float X;
    public float Y;
    public float Pressure;
    public float Pad;

    public BrushStrokePoint(float x, float y, float pressure = 0.5f)
    {
        X = x; Y = y; Pressure = pressure; Pad = 0;
    }
}

/// <summary>
/// BrushConstants cbuffer (b0) uploaded for every dispatch. Layout
/// must match the HLSL <c>cbuffer BrushConstants</c> in the preamble
/// byte-for-byte. Total size: 80 bytes (5× float4), observing D3D12
/// cbuffer 16-byte packing rules.
/// </summary>
/// <remarks>
/// The last 16 bytes (<see cref="ViewportWidth"/>, <see cref="ViewportHeight"/>,
/// <see cref="Pad0"/>, <see cref="Pad1"/>) are <em>native-filled</em>
/// — callers leave them at zero and the backend overwrites them
/// with the ink-layer bitmap's pixel dimensions right before the
/// dispatch. Omitting them would let the native code read past the
/// struct (undefined bytes for ViewportSize) and the vertex shader
/// would compute pxPos = (0,0) → every pixel SDF-far from the stroke
/// → full discard, invisible strokes.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct BrushConstantsNative
{
    // float4 StrokeColor — premultiplied RGBA
    public float ColorR, ColorG, ColorB, ColorA;
    public float StrokeWidth;
    public float StrokeHeight;
    public float TimeSeconds;
    public uint  RandomSeed;
    public float BBoxMinX, BBoxMinY;
    public float BBoxMaxX, BBoxMaxY;
    public uint  PointCount;
    public uint  TaperMode;       // 0=None, 1=TaperedStart, 2=TaperedEnd
    public uint  IgnorePressure;  // 0=use pressure, 1=ignore
    public uint  FitToCurve;      // reserved; currently PS does its own sampling

    // Native-filled: backend writes the ink-layer bitmap size here
    // right before upload. Managed code leaves these at zero.
    public float ViewportWidth;
    public float ViewportHeight;
    public float Pad0;
    public float Pad1;
}
