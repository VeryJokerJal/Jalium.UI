using System.Runtime.InteropServices;

namespace Jalium.UI.Interop;

/// <summary>
/// GPU-side persistent RGBA8 bitmap used by <c>InkCanvas</c> as its
/// committed-ink layer. Brush shaders dispatch directly into this bitmap
/// on stroke commit; every subsequent frame just blits the bitmap to the
/// main render target — committed strokes cost O(1) per frame instead of
/// O(N strokes) CPU rasterization.
/// </summary>
/// <remarks>
/// The bitmap lives on the context it was created from; it must be
/// disposed (or GC-collected) before the context is disposed. Resize
/// reallocates the backing texture and clears its contents.
/// </remarks>
public sealed class InkLayerBitmap : IDisposable
{
    private readonly RenderContext _context;
    private nint _handle;
    private int _width;
    private int _height;
    private bool _disposed;

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
    {
        ArgumentNullException.ThrowIfNull(context);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _context = context;
        _width = width;
        _height = height;
        _handle = NativeMethods.InkLayerBitmapCreate(context.Handle, width, height);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException(
                $"Native ink layer allocation failed ({width}x{height}).");
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

        int rc = NativeMethods.InkLayerBitmapResize(_handle, width, height);
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
        NativeMethods.InkLayerBitmapClear(_handle, r, g, b, a);
    }

    /// <summary>
    /// Dispatches a compiled brush shader over this bitmap. <paramref name="points"/>
    /// carries the stroke polyline; <paramref name="constants"/> is the
    /// 80-byte <see cref="BrushConstantsNative"/> struct whose last 16
    /// bytes (ViewportSize + Pad) are overwritten by the backend with
    /// this bitmap's pixel dimensions — callers leave them at zero.
    /// </summary>
    /// <returns>0 on success; non-zero on compile / dispatch failure
    /// (caller should fall back to a CPU path).</returns>
    public int DispatchBrush(
        BrushShaderHandle shader,
        ReadOnlySpan<BrushStrokePoint> points,
        in BrushConstantsNative constants,
        ReadOnlySpan<byte> extraParams = default)
    {
        ThrowIfDisposed();
        if (shader is null || !shader.IsValid) return -1;
        if (points.Length < 2) return -2;

        unsafe
        {
            fixed (BrushStrokePoint* pPoints = points)
            fixed (BrushConstantsNative* pConst = &constants)
            fixed (byte* pExtras = extraParams)
            {
                return NativeMethods.InkLayerBitmapDispatchBrush(
                    _handle, shader.Handle,
                    pPoints, points.Length, pConst,
                    extraParams.IsEmpty ? null : pExtras,
                    extraParams.Length);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != nint.Zero)
        {
            NativeMethods.InkLayerBitmapDestroy(_handle);
            _handle = nint.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~InkLayerBitmap() => Dispose();

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InkLayerBitmap));
    }
}

/// <summary>
/// Handle to a compiled brush pixel-shader + its PSO. Obtained by
/// <see cref="RenderContext.AcquireBrushShader"/>; owned by the context
/// and automatically released on context dispose. Safe to hold across
/// frames. Thread-safe to share; dispatch must be on the render thread.
/// </summary>
public sealed class BrushShaderHandle : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>Raw native handle (JaliumBrushShader*).</summary>
    public nint Handle => _handle;

    /// <summary>True until <see cref="Dispose"/> is called.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    internal BrushShaderHandle(nint handle) { _handle = handle; }

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
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(shaderKey);
        ArgumentNullException.ThrowIfNull(brushMainHlsl);
        if (context.Handle == nint.Zero) return null;

        nint h = NativeMethods.BrushShaderCreate(
            context.Handle, shaderKey, brushMainHlsl, blendMode);
        if (h == nint.Zero) return null;
        return new BrushShaderHandle(h);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != nint.Zero)
        {
            NativeMethods.BrushShaderDestroy(_handle);
            _handle = nint.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~BrushShaderHandle() => Dispose();
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
