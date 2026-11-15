using System.Runtime.CompilerServices;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Media.Rendering;

/// <summary>
/// Snapshots mutable draw inputs before retained or whole-frame publication.
/// Solid brushes use the frozen value pool. Gradients use current-value frozen
/// clones, memoised by content and CSS used-size layout identity so unchanged
/// frames continue to hit the native brush cache. Mutable image resources still
/// require direct rendering. All snapshots are made on the recording thread.
/// </summary>
internal static class DrawInputSnapshotter
{
    private sealed class GradientEntry
    {
        public long Hash;
        public long LayoutHash;
        public Jalium.UI.Styling.CssImageLayout? Layout;
        public Brush Snapshot = null!;
    }

    private static readonly ConditionalWeakTable<GradientBrush, GradientEntry> s_gradientCache = new();
    private static readonly object s_lock = new();

    /// <summary>
    /// Returns a render-thread-safe snapshot of <paramref name="brush"/>:
    /// solid/null pass through, gradients are memoised value copies, an image
    /// brush backed by a live mutable image source trips a fallback.
    /// </summary>
    public static Brush? SnapshotBrush(Brush? brush)
    {
        switch (brush)
        {
            case CssLayeredBackgroundBrush layers:
                return new CssLayeredBackgroundBrush
                {
                    Bottom = SnapshotBrush(layers.Bottom), Top = SnapshotBrush(layers.Top),
                    Opacity = layers.Opacity, SnapshotBlendMode = layers.BlendMode,
                };
            case null:
            case SolidColorBrush:
                return brush;                       // immutable in practice (pooled value copy)
            case GradientBrush g:
                return SnapshotGradient(g);
            case ImageBrush ib:
                // Static image brushes are effectively immutable. WriteableBitmap
                // mutates its CPU back buffer, while D3DImage references a mutable,
                // caller-owned native resource. Neither can be published safely to
                // the render worker by retaining the ImageSource reference.
                if (IsLiveImageSource(ib.ImageSource))
                    DrawingContext.MarkCurrentFrameUnrecordable();
                return brush;
            default:
                return brush;
        }
    }

    private static Brush SnapshotGradient(GradientBrush brush)
    {
        var hash = brush.ComputeContentHash();
        var layout = brush.CssImageLayout;
        long layoutHash = 0;
        var capturedLayout = layout is Jalium.UI.Styling.CssGradientLayout css
            ? css.CaptureForRendering(out layoutHash) : layout;
        if (brush.IsFrozen && ReferenceEquals(layout, capturedLayout)) return brush;
        lock (s_lock)
        {
            if (s_gradientCache.TryGetValue(brush, out var entry) && entry.Hash == hash && entry.LayoutHash == layoutHash &&
                ReferenceEquals(entry.Layout, layout))
                return entry.Snapshot;

            // Freezable captures current animation values, nested stops,
            // transforms and brush metadata without sharing mutable children.
            var snapshot = brush.CloneCurrentValue();
            snapshot.CssImageLayout = capturedLayout;
            snapshot.Freeze();
            _ = snapshot.ComputeContentHash();
            s_gradientCache.AddOrUpdate(brush, new GradientEntry { Hash = hash, LayoutHash = layoutHash, Layout = layout, Snapshot = snapshot });
            return snapshot;
        }
    }

    public static FormattedText SnapshotText(FormattedText text)
    {
        // Keep the common scalar solid-text path in the existing value pool.
        if (text.Foreground is null or SolidColorBrush && !text.HasCharacterFormats)
            return text;
        return text.CreateRenderSnapshot(SnapshotTextBrush(text.Foreground), SnapshotTextBrush);
    }

    private static Brush? SnapshotTextBrush(Brush? brush)
    {
        if (brush is null or SolidColorBrush)
            return DrawingObjectPool.CanonicalizeBrush(brush);
        if (brush is GradientBrush)
            return SnapshotBrush(brush);
        // Native text does not yet paint image/custom foregrounds consistently.
        DrawingContext.MarkCurrentFrameUnrecordable();
        return brush;
    }

    /// <summary>
    /// Returns a snapshot of <paramref name="pen"/> when its brush is a gradient
    /// (value-copying the pen and its brush); simple/solid pens pass through
    /// (already pooled).
    /// </summary>
    public static Pen? SnapshotPen(Pen? pen, Rect? paintBounds = null)
    {
        if (pen?.Brush is not GradientBrush)
            return pen;

        var brush = pen.Brush;
        if (paintBounds is { Width: > 0, Height: > 0 } bounds)
            brush = Jalium.UI.Styling.CssImageLayout.ResolveBrush(brush, bounds.Width, bounds.Height);
        if (brush is null) return null;
        var snappedBrush = SnapshotBrush(brush)!;
        return new Pen(snappedBrush, pen.Thickness)
        {
            SvgViewportStroke = pen.SvgViewportStroke,
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
    /// uses a managed-offset path for <see cref="TranslateTransform"/>. Preserve
    /// that representation so cached text uses the same pixel placement as direct
    /// rendering. Frozen snapshots remain readable on the render worker.
    /// </summary>
    public static Transform SnapshotTransform(Transform transform)
    {
        var value = transform.Value;
        Transform snapshot = transform is TranslateTransform
            ? new TranslateTransform(value.OffsetX, value.OffsetY)
            : new MatrixTransform(value);
        snapshot.Freeze();
        return snapshot;
    }

    /// <summary>
    /// Returns a render-thread-readable geometry. Frozen geometries are already
    /// immutable and can be shared; mutable or dispatcher-owned geometries are
    /// deep-cloned at their current value and frozen on the recording thread.
    /// If a custom geometry cannot be frozen, mark the frame unrecordable so the
    /// window safely falls back instead of publishing a cross-thread object.
    /// </summary>
    public static Geometry SnapshotGeometry(Geometry geometry)
    {
        if (geometry.IsFrozen)
        {
            return geometry;
        }

        var snapshot = geometry.CloneCurrentValue();
        if (!snapshot.TryFreeze())
        {
            DrawingContext.MarkCurrentFrameUnrecordable();
            return geometry;
        }

        return snapshot;
    }

    private static bool IsLiveImageSource(ImageSource? image) =>
        image is WriteableBitmap or D3DImage;

    /// <summary>
    /// Returns <paramref name="image"/> unchanged, but trips a frame fallback for
    /// live mutable sources whose current pixels/native handle cannot be captured
    /// by value. Replaying either source later could observe a torn buffer, a newer
    /// frame, or a native surface that the UI thread has already released.
    /// </summary>
    public static ImageSource SnapshotImage(ImageSource image)
    {
        if (IsLiveImageSource(image))
            DrawingContext.MarkCurrentFrameUnrecordable();
        return image;
    }
}
