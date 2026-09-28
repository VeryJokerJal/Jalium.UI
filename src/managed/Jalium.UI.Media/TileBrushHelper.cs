namespace Jalium.UI.Media;

/// <summary>
/// Pure layout math for <see cref="TileBrush"/> rendering. Translates
/// <see cref="TileBrush.Viewport"/> / <see cref="TileBrush.Viewbox"/> /
/// <see cref="TileBrush.Stretch"/> / <see cref="TileBrush.AlignmentX"/> /
/// <see cref="TileBrush.AlignmentY"/> / <see cref="TileBrush.TileMode"/> into
/// concrete screen-space rectangles that a renderer can blit a single bitmap
/// against. Has no rendering or platform dependencies — it can run from tests.
/// </summary>
public static class TileBrushHelper
{
    internal static Matrix ComputeBrushTransform(Brush brush, Rect bounds, Point offset)
    {
        var relative = Matrix.Identity;
        if (brush.RelativeTransform is { } transform && !transform.Value.IsIdentity && bounds.Width > 0 && bounds.Height > 0)
        {
            var normalize = new Matrix(1 / bounds.Width, 0, 0, 1 / bounds.Height, -bounds.X / bounds.Width, -bounds.Y / bounds.Height);
            var denormalize = new Matrix(bounds.Width, 0, 0, bounds.Height, bounds.X, bounds.Y);
            relative = normalize * transform.Value * denormalize;
        }
        var absolute = brush.Transform?.Value ?? Matrix.Identity;
        absolute = new Matrix(1, 0, 0, 1, -offset.X, -offset.Y) * absolute * new Matrix(1, 0, 0, 1, offset.X, offset.Y);
        return relative * absolute;
    }

    internal static Rect TransformBounds(Rect bounds, Matrix transform)
    {
        var a = transform.Transform(new Point(bounds.Left, bounds.Top));
        var b = transform.Transform(new Point(bounds.Right, bounds.Top));
        var c = transform.Transform(new Point(bounds.Right, bounds.Bottom));
        var d = transform.Transform(new Point(bounds.Left, bounds.Bottom));
        var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        return new(left, top, Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) - left,
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) - top);
    }
    /// <summary>
    /// One tile of a <see cref="TileBrush"/> in screen-space. <see cref="ImageDestRect"/>
    /// is where the FULL source bitmap is drawn (potentially extending past
    /// <see cref="ClipRect"/> when the brush has a non-default Viewbox); the
    /// renderer is expected to clip drawing to <see cref="ClipRect"/> so only
    /// the viewbox portion of the source is visible.
    /// </summary>
    public readonly struct TilePlacement
    {
        /// <summary>Where the full source bitmap is drawn.</summary>
        public Rect ImageDestRect { get; }

        /// <summary>Tile boundary clip; restricts visible bitmap to this rectangle.</summary>
        public Rect ClipRect { get; }

        /// <summary>Horizontal flip applied to this tile (FlipX / FlipXY at odd column).</summary>
        public bool FlipX { get; }

        /// <summary>Vertical flip applied to this tile (FlipY / FlipXY at odd row).</summary>
        public bool FlipY { get; }

        /// <summary>
        /// Initializes a new <see cref="TilePlacement"/>.
        /// </summary>
        public TilePlacement(Rect imageDestRect, Rect clipRect, bool flipX, bool flipY)
        {
            ImageDestRect = imageDestRect;
            ClipRect = clipRect;
            FlipX = flipX;
            FlipY = flipY;
        }
    }

    /// <summary>
    /// Resolves <see cref="TileBrush.Viewport"/> against <paramref name="shapeBounds"/>
    /// using <see cref="TileBrush.ViewportUnits"/>. Returns the absolute screen-space
    /// rectangle of one base tile.
    /// </summary>
    public static Rect ComputeViewport(TileBrush brush, Rect shapeBounds)
    {
        if (brush is null) throw new ArgumentNullException(nameof(brush));

        var v = brush.Viewport;
        if (brush.ViewportUnits == BrushMappingMode.RelativeToBoundingBox)
        {
            return new Rect(
                shapeBounds.X + v.X * shapeBounds.Width,
                shapeBounds.Y + v.Y * shapeBounds.Height,
                v.Width * shapeBounds.Width,
                v.Height * shapeBounds.Height);
        }
        return v;
    }

    /// <summary>
    /// Resolves <see cref="TileBrush.Viewbox"/> against the source content size
    /// using <see cref="TileBrush.ViewboxUnits"/>. Returns the absolute rectangle
    /// in source-content coordinates.
    /// </summary>
    public static Rect ComputeViewbox(TileBrush brush, double contentWidth, double contentHeight)
    {
        if (brush is null) throw new ArgumentNullException(nameof(brush));

        var v = brush.Viewbox;
        if (brush.ViewboxUnits == BrushMappingMode.RelativeToBoundingBox)
        {
            return new Rect(
                v.X * contentWidth,
                v.Y * contentHeight,
                v.Width * contentWidth,
                v.Height * contentHeight);
        }
        return v;
    }

    /// <summary>
    /// Computes the rect within <paramref name="viewport"/> where the
    /// <paramref name="viewbox"/> content is drawn after applying
    /// <see cref="TileBrush.Stretch"/> and <see cref="TileBrush.AlignmentX"/> /
    /// <see cref="TileBrush.AlignmentY"/>.
    /// </summary>
    public static Rect ComputeContentRect(TileBrush brush, Rect viewport, Rect viewbox)
    {
        if (brush is null) throw new ArgumentNullException(nameof(brush));

        if (viewbox.Width <= 0 || viewbox.Height <= 0 ||
            viewport.Width <= 0 || viewport.Height <= 0)
        {
            return new Rect(viewport.X, viewport.Y, 0, 0);
        }

        double contentW;
        double contentH;
        switch (brush.Stretch)
        {
            case Stretch.None:
                contentW = viewbox.Width;
                contentH = viewbox.Height;
                break;
            case Stretch.Fill:
                contentW = viewport.Width;
                contentH = viewport.Height;
                break;
            case Stretch.Uniform:
            {
                var s = Math.Min(viewport.Width / viewbox.Width, viewport.Height / viewbox.Height);
                contentW = viewbox.Width * s;
                contentH = viewbox.Height * s;
                break;
            }
            case Stretch.UniformToFill:
            {
                var s = Math.Max(viewport.Width / viewbox.Width, viewport.Height / viewbox.Height);
                contentW = viewbox.Width * s;
                contentH = viewbox.Height * s;
                break;
            }
            default:
                contentW = viewport.Width;
                contentH = viewport.Height;
                break;
        }

        double offsetX = brush.AlignmentX switch
        {
            AlignmentX.Left => 0,
            AlignmentX.Right => viewport.Width - contentW,
            _ => (viewport.Width - contentW) * 0.5,
        };
        double offsetY = brush.AlignmentY switch
        {
            AlignmentY.Top => 0,
            AlignmentY.Bottom => viewport.Height - contentH,
            _ => (viewport.Height - contentH) * 0.5,
        };

        return new Rect(viewport.X + offsetX, viewport.Y + offsetY, contentW, contentH);
    }

    /// <summary>
    /// Given the screen-space content rect (where <paramref name="viewbox"/> lands),
    /// computes the dest rect for the FULL source bitmap. The viewbox sub-region
    /// of the rendered bitmap aligns exactly with the content rect — surrounding
    /// pixels lie outside (and the caller is expected to clip them away).
    /// </summary>
    public static Rect ComputeFullImageRect(Rect contentRect, Rect viewbox,
        double contentWidth, double contentHeight)
    {
        if (viewbox.Width <= 0 || viewbox.Height <= 0)
        {
            return contentRect;
        }

        var scaleX = contentRect.Width / viewbox.Width;
        var scaleY = contentRect.Height / viewbox.Height;
        return new Rect(
            contentRect.X - viewbox.X * scaleX,
            contentRect.Y - viewbox.Y * scaleY,
            contentWidth * scaleX,
            contentHeight * scaleY);
    }

    /// <summary>
    /// Computes the per-tile placements needed to fill <paramref name="shapeBounds"/>
    /// with <paramref name="brush"/>. Returns one entry for <see cref="TileMode.None"/>;
    /// returns the full grid of tiles covering the shape for any other tile mode.
    /// </summary>
    public static List<TilePlacement> ComputeTilePlacements(
        TileBrush brush,
        Rect shapeBounds,
        double contentWidth,
        double contentHeight)
        => ComputeTilePlacements(brush, shapeBounds, contentWidth, contentHeight, null);

    internal static List<TilePlacement> ComputeTilePlacements(
        TileBrush brush, Rect shapeBounds, double contentWidth, double contentHeight, Rect? coverageBounds)
    {
        if (brush is null) throw new ArgumentNullException(nameof(brush));

        var result = new List<TilePlacement>();
        if (contentWidth <= 0 || contentHeight <= 0 ||
            shapeBounds.Width <= 0 || shapeBounds.Height <= 0)
        {
            return result;
        }

        if (brush is ImageBrush { CssBackgroundLayout: { } cssLayout })
            return ComputeCssBackgroundTiles(cssLayout, shapeBounds, contentWidth, contentHeight,
                coverageBounds ?? shapeBounds);

        var viewport = ComputeViewport(brush, shapeBounds);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return result;
        }

        var viewbox = ComputeViewbox(brush, contentWidth, contentHeight);
        if (viewbox.Width <= 0 || viewbox.Height <= 0)
        {
            return result;
        }

        var contentRect = ComputeContentRect(brush, viewport, viewbox);
        var fullImage = ComputeFullImageRect(contentRect, viewbox, contentWidth, contentHeight);

        if (brush.TileMode == TileMode.None)
        {
            result.Add(new TilePlacement(fullImage, viewport, false, false));
            return result;
        }

        var coverage = coverageBounds ?? shapeBounds;
        var startCol = (int)Math.Floor((coverage.X - viewport.X) / viewport.Width);
        var endCol = (int)Math.Ceiling((coverage.Right - viewport.X) / viewport.Width);
        var startRow = (int)Math.Floor((coverage.Y - viewport.Y) / viewport.Height);
        var endRow = (int)Math.Ceiling((coverage.Bottom - viewport.Y) / viewport.Height);

        // Cap absurd tile counts to keep the renderer safe against pathological
        // inputs — a 1×1 viewport on a 10000×10000 shape would otherwise spin up
        // 10⁸ DrawBitmap calls.
        const int MaxTilesPerAxis = 1024;
        if (endCol - startCol > MaxTilesPerAxis)
        {
            endCol = startCol + MaxTilesPerAxis;
        }
        if (endRow - startRow > MaxTilesPerAxis)
        {
            endRow = startRow + MaxTilesPerAxis;
        }

        for (int row = startRow; row < endRow; row++)
        {
            for (int col = startCol; col < endCol; col++)
            {
                bool flipX = brush.TileMode is TileMode.FlipX or TileMode.FlipXY && (col & 1) != 0;
                bool flipY = brush.TileMode is TileMode.FlipY or TileMode.FlipXY && (row & 1) != 0;

                var tileX = viewport.X + col * viewport.Width;
                var tileY = viewport.Y + row * viewport.Height;

                var tileClip = new Rect(tileX, tileY, viewport.Width, viewport.Height);
                var tileImage = new Rect(
                    fullImage.X + (tileX - viewport.X),
                    fullImage.Y + (tileY - viewport.Y),
                    fullImage.Width,
                    fullImage.Height);

                result.Add(new TilePlacement(tileImage, tileClip, flipX, flipY));
            }
        }
        return result;
    }

    private static List<TilePlacement> ComputeCssBackgroundTiles(
        CssBackgroundImageLayout layout, Rect area, double intrinsicWidth, double intrinsicHeight,
        Rect coverage)
    {
        var result = new List<TilePlacement>();
        var pattern = layout.TilePattern(area, intrinsicWidth, intrinsicHeight);
        var image = pattern.ImageRect;
        if (image.IsEmpty) return result;

        const int MaxTilesPerAxis = 1024;
        var columns = pattern.X.TileStarts(coverage.X, coverage.Right, MaxTilesPerAxis);
        var rows = pattern.Y.TileStarts(coverage.Y, coverage.Bottom, MaxTilesPerAxis);
        foreach (var y in rows)
        {
            foreach (var x in columns)
            {
                var tile = new Rect(x, y, image.Width, image.Height);
                result.Add(new TilePlacement(tile, tile, false, false));
            }
        }
        return result;
    }
}
