using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Paints CSS background layers using the owning control's current box edges.</summary>
internal static class CssBackgroundPainter
{
    internal static bool TryDraw(FrameworkElement owner, DependencyProperty backgroundProperty,
        Brush current, DrawingContext drawingContext, Rect outer, CssUsedBorderRadii radii,
        Thickness border, Thickness padding, Action<Brush> drawShape, bool tileGradients = true)
    {
        if (owner.HasLocalOrAnimatedValue(backgroundProperty))
        {
            if (current is ImageBrush localBrush &&
                localBrush.ScalingMode == BitmapScalingMode.Unspecified)
            {
                var usedMode = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
                if (usedMode != BitmapScalingMode.Unspecified)
                {
                    drawShape(CopyImageBrush(localBrush, localBrush.CssBackgroundLayout, usedMode));
                    return true;
                }
            }
            return false;
        }
        // A caller can mutate the layered brush after CSS applied. Keep its
        // composite opacity semantics until box painting can group that stack.
        for (Brush? layer = current; layer is CssLayeredBackgroundBrush stack; layer = stack.Bottom)
            if (stack.Opacity != 1) return false;
        var state = CssBackgroundPaintProperties.Get(owner);
        if (!ReferenceEquals(state?.Composite, current) && owner.TemplatedParent is { } parent)
            state = CssBackgroundPaintProperties.Get(parent);
        if (state is null || !ReferenceEquals(state.Composite, current)) return false;

        var contentInset = new Thickness(
            Math.Max(0, border.Left + padding.Left),
            Math.Max(0, border.Top + padding.Top),
            Math.Max(0, border.Right + padding.Right),
            Math.Max(0, border.Bottom + padding.Bottom));

        Thickness Inset(CssBackgroundBox box) => box switch
        {
            CssBackgroundBox.Padding => border,
            CssBackgroundBox.Content => contentInset,
            _ => default,
        };

        void DrawLayer(Brush? brush, CssBackgroundBox origin, CssBackgroundBox clip, int imageIndex = -1)
        {
            if (brush is null) return;
            var positioningInset = Inset(origin);
            if (tileGradients && imageIndex >= 0 && brush.CssGradientLayout is { } gradient)
            {
                var area = InnerRect(outer, positioningInset);
                var gradientLayout = new CssBackgroundImageLayout(
                    state.Sizes[imageIndex % state.Sizes.Length],
                    state.Repeats[imageIndex % state.Repeats.Length],
                    state.Positions[imageIndex % state.Positions.Length]);
                var pattern = gradientLayout.GradientTilePattern(area);
                if (pattern.ImageRect.IsEmpty) return;
                var columns = pattern.X.TileStarts(outer.Left, outer.Right, 1024);
                var rows = pattern.Y.TileStarts(outer.Top, outer.Bottom, 1024);
                if (columns.Count == 0 || rows.Count == 0) return;
                if (clip == CssBackgroundBox.Border && columns.Count == 1 && rows.Count == 1 &&
                    new Rect(columns[0], rows[0], pattern.ImageRect.Width, pattern.ImageRect.Height) == outer)
                {
                    drawShape(brush);
                    return;
                }
                var tileBrush = gradient.Resolve(pattern.ImageRect.Width, pattern.ImageRect.Height);
                if (tileBrush is null) return;
                var clipInset = Inset(clip);
                var gradientClipRect = InnerRect(outer, clipInset);
                if (clip != CssBackgroundBox.Border &&
                    (gradientClipRect.Width <= 0 || gradientClipRect.Height <= 0)) return;

                drawingContext.PushClip(new CssRoundedRectangleGeometry(outer, radii));
                try
                {
                    if (clip != CssBackgroundBox.Border)
                        drawingContext.PushClip(new CssRoundedRectangleGeometry(gradientClipRect, radii.Inset(clipInset)));
                    try
                    {
                        foreach (var y in rows)
                        foreach (var x in columns)
                            drawingContext.DrawRectangle(tileBrush, null,
                                new Rect(x, y, pattern.ImageRect.Width, pattern.ImageRect.Height));
                    }
                    finally { if (clip != CssBackgroundBox.Border) drawingContext.Pop(); }
                }
                finally { drawingContext.Pop(); }
                return;
            }
            if (brush is ImageBrush bitmap)
            {
                var scaling = bitmap.ScalingMode;
                if (scaling == BitmapScalingMode.Unspecified)
                    scaling = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
                var hasPositioningInset = bitmap.CssBackgroundLayout is not null &&
                    (positioningInset.Left != 0 || positioningInset.Top != 0 ||
                     positioningInset.Right != 0 || positioningInset.Bottom != 0);
                if (hasPositioningInset || scaling != bitmap.ScalingMode)
                {
                    // The CSS brush may be frozen. Keep its source live and set the used
                    // sampling hint on a draw-local clone instead of mutating the style.
                    brush = CopyImageBrush(bitmap,
                        hasPositioningInset
                            ? bitmap.CssBackgroundLayout!.WithPositioningInsets(positioningInset)
                            : bitmap.CssBackgroundLayout,
                        scaling);
                }
            }

            if (clip == CssBackgroundBox.Border)
            {
                drawShape(brush);
                return;
            }

            var inset = Inset(clip);
            var clipRect = InnerRect(outer, inset);
            if (clipRect.Width <= 0 || clipRect.Height <= 0) return;
            var clipGeometry = new CssRoundedRectangleGeometry(clipRect, radii.Inset(inset));
            drawingContext.PushClip(clipGeometry);
            try { drawShape(brush); }
            finally { drawingContext.Pop(); }
        }

        DrawLayer(state.Color, CssBackgroundBox.Border, state.ColorClip);
        for (var i = state.Layers.Length - 1; i >= 0; i--)
        {
            var layer = state.Layers[i];
            DrawLayer(layer.Image, layer.Origin, layer.Clip, i);
        }
        return true;
    }

    private static ImageBrush CopyImageBrush(ImageBrush source,
        CssBackgroundImageLayout? layout, BitmapScalingMode scaling)
    {
        var copy = new ImageBrush
        {
            IsTransientPaintCopy = true,
            ImageSource = source.ImageSource,
            CssBackgroundLayout = layout,
            CssGradientLayout = source.CssGradientLayout,
            ScalingMode = scaling,
            Opacity = source.Opacity,
            Transform = source.Transform,
            RelativeTransform = source.RelativeTransform,
            AlignmentX = source.AlignmentX,
            AlignmentY = source.AlignmentY,
            Stretch = source.Stretch,
            TileMode = source.TileMode,
            Viewport = source.Viewport,
            ViewportUnits = source.ViewportUnits,
            Viewbox = source.Viewbox,
            ViewboxUnits = source.ViewboxUnits,
        };
        return copy;
    }

    internal static CssUsedBorderRadii CircularRadii(CornerRadius corners)
    {
        static Size Pair(double radius)
        {
            var value = double.IsFinite(radius) ? Math.Max(0, radius) : 0;
            return new Size(value, value);
        }
        return new(Pair(corners.TopLeft), Pair(corners.TopRight),
            Pair(corners.BottomRight), Pair(corners.BottomLeft));
    }

    private static Rect InnerRect(Rect outer, Thickness inset)
    {
        var left = outer.Left + inset.Left;
        var top = outer.Top + inset.Top;
        return new Rect(left, top,
            Math.Max(0, outer.Right - inset.Right - left),
            Math.Max(0, outer.Bottom - inset.Bottom - top));
    }
}
