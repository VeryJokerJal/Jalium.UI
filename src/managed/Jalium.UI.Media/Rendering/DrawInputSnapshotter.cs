using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Snapshots mutable live draw inputs at whole-frame record time so a later
/// UI-thread mutation cannot corrupt an in-flight replay on the render thread
/// (the JALIUM_RENDER_THREAD path). Used ONLY on the whole-frame capture path;
/// the default per-visual cache and direct render never call it.
/// </summary>
/// <remarks>
/// <para>
/// Strategy is snapshot-by-value, NOT Freeze(): in this codebase
/// <see cref="Brush"/>, <see cref="Pen"/> and <see cref="Transform"/> are not
/// Freezable, so a value copy is the only safe option. The dominant inputs —
/// <see cref="SolidColorBrush"/>, simple <see cref="Pen"/>, and
/// <see cref="FormattedText"/> — are ALREADY value-snapshotted by
/// <see cref="DrawingObjectPool"/>, so they pass through untouched here.
/// </para>
/// <para>
/// To preserve the backend's <c>Dictionary&lt;Brush, NativeBrush&gt;</c>
/// identity cache, gradient snapshots are memoised per source brush keyed by
/// <see cref="GradientBrush.ComputeContentHash"/>: an unchanged gradient returns
/// the SAME snapshot instance every frame (native-cache hit), and only a genuine
/// mutation produces a fresh snapshot. The cache is written on the UI thread
/// during record and only dereferenced (never mutated) on the render thread, so
/// the published Drawing carries immutable snapshots.
/// </para>
/// <para>
/// Per-frame-mutating GPU resources (a <see cref="WriteableBitmap"/> used as an
/// image or image-brush source — e.g. video) cannot be cheaply value-copied, so
/// they trip <see cref="DrawingContext.MarkCurrentFrameUnrecordable"/> and the
/// frame falls back to a direct render. Unfrozen <see cref="Geometry"/> is left
/// as-is (the common case is a per-OnRender temporary that never escapes the
/// walk); callers that animate a long-lived Geometry should <c>Freeze()</c> it,
/// matching the WPF convention.
/// </para>
/// </remarks>
internal static class DrawInputSnapshotter
{
    private sealed class GradientEntry
    {
        public long Hash;
        public Brush Snapshot = null!;
    }

    private static readonly ConditionalWeakTable<GradientBrush, GradientEntry> s_gradientCache = new();
    private static readonly object s_lock = new();

    /// <summary>
    /// Returns a render-thread-safe snapshot of <paramref name="brush"/>:
    /// solid/null pass through, gradients are memoised value copies, an image
    /// brush backed by a <see cref="WriteableBitmap"/> trips a fallback.
    /// </summary>
    public static Brush? SnapshotBrush(Brush? brush)
    {
        switch (brush)
        {
            case null:
            case SolidColorBrush:
                return brush;                       // immutable in practice (pooled value copy)
            case GradientBrush g:
                return SnapshotGradient(g);
            case ImageBrush ib:
                // Static image brushes are effectively immutable; a WriteableBitmap
                // source mutates per frame and can't be value-copied cheaply.
                if (ib.ImageSource is WriteableBitmap)
                    DrawingContext.MarkCurrentFrameUnrecordable();
                return brush;
            default:
                return brush;
        }
    }

    private static Brush SnapshotGradient(GradientBrush g)
    {
        // Use ComputeContentHashCore (UNCACHED) — NOT ComputeContentHash: the
        // gradient scalar setters (StartPoint/EndPoint/Center/Radius/Opacity/
        // SpreadMethod/MappingMode) do not call InvalidateContentHash, so the
        // memoised ComputeContentHash returns a STALE hash and would freeze an
        // animated gradient on the render-thread path (the memo below would keep
        // handing back the first frame's snapshot). Fold the brush Transform in
        // too — it is not part of the content hash, but an animated brush
        // Transform must also re-snapshot.
        long h = g.ComputeContentHashCore() ^ TransformVersion(g.Transform);
        lock (s_lock)
        {
            if (s_gradientCache.TryGetValue(g, out var entry) && entry.Hash == h)
                return entry.Snapshot;

            var snap = CopyGradient(g);
            s_gradientCache.AddOrUpdate(g, new GradientEntry { Hash = h, Snapshot = snap });
            return snap;
        }
    }

    private static long TransformVersion(Transform? t)
    {
        if (t is null) return 0;
        var m = t.Value;
        return m.M11.GetHashCode()
             ^ (m.M12.GetHashCode() << 1) ^ (m.M21.GetHashCode() << 2)
             ^ (m.M22.GetHashCode() << 3) ^ (m.OffsetX.GetHashCode() << 4)
             ^ (m.OffsetY.GetHashCode() << 5);
    }

    private static Brush CopyGradient(GradientBrush src)
    {
        GradientBrush dst;
        switch (src)
        {
            case LinearGradientBrush l:
                dst = new LinearGradientBrush { StartPoint = l.StartPoint, EndPoint = l.EndPoint };
                break;
            case RadialGradientBrush r:
                dst = new RadialGradientBrush
                {
                    Center = r.Center,
                    GradientOrigin = r.GradientOrigin,
                    RadiusX = r.RadiusX,
                    RadiusY = r.RadiusY,
                };
                break;
            default:
                return src;                         // unknown gradient subtype — can't copy safely
        }

        dst.SpreadMethod = src.SpreadMethod;
        dst.MappingMode = src.MappingMode;
        dst.Opacity = src.Opacity;
        // Value-snapshot the brush transform too (folded into the memo key above)
        // so a transformed/animated gradient brush is race-free on the render thread.
        dst.Transform = src.Transform is null ? null : SnapshotTransform(src.Transform);
        foreach (var stop in src.GradientStops)
            dst.GradientStops.Add(new GradientStop(stop.Color, stop.Offset));
        return dst;
    }

    /// <summary>
    /// Returns a snapshot of <paramref name="pen"/> when its brush is a gradient
    /// (value-copying the pen and its brush); simple/solid pens pass through
    /// (already pooled).
    /// </summary>
    public static Pen? SnapshotPen(Pen? pen)
    {
        if (pen?.Brush is not GradientBrush)
            return pen;

        var snappedBrush = SnapshotBrush(pen.Brush)!;
        return new Pen(snappedBrush, pen.Thickness)
        {
            StartLineCap = pen.StartLineCap,
            EndLineCap = pen.EndLineCap,
            DashCap = pen.DashCap,
            LineJoin = pen.LineJoin,
            MiterLimit = pen.MiterLimit,
            DashStyle = pen.DashStyle is { } ds ? new DashStyle(ds.Dashes, ds.Offset) : DashStyles.Solid,
        };
    }

    /// <summary>
    /// Returns a value snapshot of <paramref name="transform"/>. The backend
    /// reads only <see cref="Transform.Value"/> (a value-type <see cref="Matrix"/>),
    /// so a fresh <see cref="MatrixTransform"/> over the current matrix is fully
    /// race-free; transforms are not identity-cached so per-frame allocation here
    /// has no native-cache cost.
    /// </summary>
    public static Transform SnapshotTransform(Transform transform)
        => new MatrixTransform(transform.Value);

    /// <summary>
    /// Returns <paramref name="image"/> unchanged, but trips a frame fallback for
    /// a <see cref="WriteableBitmap"/> (per-frame-mutating back buffer that a
    /// recorded handle would replay as a torn/stale surface on the render thread).
    /// </summary>
    public static ImageSource SnapshotImage(ImageSource image)
    {
        if (image is WriteableBitmap)
            DrawingContext.MarkCurrentFrameUnrecordable();
        return image;
    }
}
