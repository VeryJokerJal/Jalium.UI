using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>
/// Represents a native brush for painting.
/// </summary>
public sealed class NativeBrush : IDisposable
{
    private nint _handle;
    private int _disposed; // 0 = not disposed, 1 = disposed (Interlocked for thread-safety)
    private RenderContext.BackendResourceLease? _contextLease;

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
    /// For solid brushes: the managed brush opacity captured when the native
    /// alpha was created. Kept separately from <see cref="CachedColor"/> so
    /// fractional opacity is not quantized to an 8-bit effective color.
    /// </summary>
    internal double CachedOpacity { get; set; } = 1d;

    /// <summary>
    /// For ImageBrush-stroke fallbacks: the <see cref="ImageSource"/> the average
    /// color was sampled from. Lets the cache lookup detect when the underlying
    /// source has been swapped on the brush and re-sample instead of returning a
    /// stale color. <see langword="null"/> for solid / gradient brushes.
    /// </summary>
    internal ImageSource? CachedImageSource { get; set; }

    /// <summary>
    /// For gradient brushes in <see cref="BrushMappingMode.RelativeToBoundingBox"/>
    /// mode: a hash of the (bx, by, bw, bh) bounds that produced this native
    /// brush. Lets the cache reuse the upload across frames whenever the same
    /// brush reference paints the same bounds — the dominant case for static
    /// backgrounds painted under retained-mode replay.
    ///
    /// 0 means "not bound to a specific bbox" (Absolute-mode gradients, solid
    /// brushes, ImageBrush fallback) — those entries match purely on the brush
    /// reference.
    /// </summary>
    internal long CachedBoundsKey { get; set; }

    /// <summary>
    /// For gradient brushes: a hash of the gradient stops + endpoint/center/radius
    /// + spread/mapping configuration captured when the native brush was created.
    /// Lets the cache detect when the user mutated the managed gradient (e.g. added
    /// a stop, changed a start point) and rebuild the native upload.
    ///
    /// 0 means "no content hash recorded" — solid brushes use <see cref="CachedColor"/>,
    /// ImageBrush uses <see cref="CachedImageSource"/>.
    /// </summary>
    internal long CachedGradientContentHash { get; set; }

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

        RenderContext.BackendResourceLease? lease = context.AcquireBackendResourceLease();
        try
        {
            _handle = NativeMethods.BrushCreateSolid(context.Handle, r, g, b, a);
            if (_handle == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create brush");
            }

            _contextLease = lease;
            lease = null;
        }
        finally
        {
            lease?.Dispose();
        }
    }

    internal NativeBrush(RenderContext context,
        float startX, float startY, float endX, float endY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        RenderContext.BackendResourceLease? lease = context.AcquireBackendResourceLease();
        try
        {
            _handle = NativeMethods.BrushCreateLinearGradient(
                context.Handle, startX, startY, endX, endY, stops, stopCount, extendMode);
            if (_handle != nint.Zero)
            {
                _contextLease = lease;
                lease = null;
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    internal NativeBrush(RenderContext context,
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        float[] stops, uint stopCount, uint extendMode = 0)
    {
        RenderContext.BackendResourceLease? lease = context.AcquireBackendResourceLease();
        try
        {
            _handle = NativeMethods.BrushCreateRadialGradient(
                context.Handle, centerX, centerY, radiusX, radiusY,
                originX, originY, stops, stopCount, extendMode);
            if (_handle != nint.Zero)
            {
                _contextLease = lease;
                lease = null;
            }
        }
        finally
        {
            lease?.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        try
        {
            var handle = Interlocked.Exchange(ref _handle, nint.Zero);
            if (handle != nint.Zero)
            {
                NativeMethods.BrushDestroy(handle);
            }
        }
        finally
        {
            ReleaseContextLease();
            GC.SuppressFinalize(this);
        }
    }

    ~NativeBrush()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var handle = Interlocked.Exchange(ref _handle, nint.Zero);
        try
        {
            if (handle != nint.Zero)
            {
                NativeMethods.BrushDestroy(handle);
            }
        }
        catch
        {
            // Finalizers must not surface native cleanup failures.
        }
        finally
        {
            ReleaseContextLease();
        }
    }

    private void ReleaseContextLease()
        => Interlocked.Exchange(ref _contextLease, null)?.Dispose();
}
