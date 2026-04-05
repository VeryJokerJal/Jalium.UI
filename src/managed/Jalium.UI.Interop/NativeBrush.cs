using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Represents a native brush for painting.
/// </summary>
public sealed class NativeBrush : IDisposable
{
    private nint _handle;
    private int _disposed; // 0 = not disposed, 1 = disposed (Interlocked for thread-safety)

    /// <summary>
    /// Gets the native handle.
    /// </summary>
    public nint Handle => _handle;

    /// <summary>
    /// Gets whether the brush is valid.
    /// </summary>
    public bool IsValid => _handle != nint.Zero && Volatile.Read(ref _disposed) == 0;

    /// <summary>
    /// Gets or sets the cached color used for cache invalidation.
    /// </summary>
    internal Color CachedColor { get; set; }

    /// <summary>
    /// Gets or sets the access sequence for LRU eviction.
    /// </summary>
    internal long LastAccessSequence { get; set; }

    /// <summary>
    /// Gets the color components.
    /// </summary>
    public float R { get; }
    public float G { get; }
    public float B { get; }
    public float A { get; }

    internal NativeBrush(RenderContext context, float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;

        _handle = NativeMethods.BrushCreateSolid(context.Handle, r, g, b, a);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create brush");
        }
    }

    internal NativeBrush(RenderContext context,
        float startX, float startY, float endX, float endY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        _handle = NativeMethods.BrushCreateLinearGradient(
            context.Handle, startX, startY, endX, endY, stops, stopCount, extendMode);
    }

    internal NativeBrush(RenderContext context,
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        _handle = NativeMethods.BrushCreateRadialGradient(
            context.Handle, centerX, centerY, radiusX, radiusY,
            originX, originY, stops, stopCount, extendMode);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        var handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            NativeMethods.BrushDestroy(handle);
        }

        GC.SuppressFinalize(this);
    }

    ~NativeBrush()
    {
        Volatile.Write(ref _disposed, 1);
        Volatile.Write(ref _handle, nint.Zero);
    }
}
