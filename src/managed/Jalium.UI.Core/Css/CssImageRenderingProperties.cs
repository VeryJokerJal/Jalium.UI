using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssImageRendering { Auto, Smooth, HighQuality, CrispEdges, Pixelated }

/// <summary>Inherited CSS hint for sampling images owned by an element.</summary>
internal static class CssImageRenderingProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssImageRendering", typeof(CssImageRendering), typeof(CssImageRenderingProperties),
        new PropertyMetadata(CssImageRendering.Auto, Changed, null, inherits: true));

    internal static BitmapScalingMode Resolve(DependencyObject element, BitmapScalingMode fallback)
        => element.GetValue(ValueProperty) is CssImageRendering mode
            ? mode switch
            {
                CssImageRendering.Smooth => BitmapScalingMode.Linear,
                CssImageRendering.HighQuality => BitmapScalingMode.HighQuality,
                CssImageRendering.CrispEdges => BitmapScalingMode.NearestNeighbor,
                CssImageRendering.Pixelated => BitmapScalingMode.Pixelated,
                _ => fallback,
            }
            : fallback;

    internal static Brush? ResolveBrush(DependencyObject owner, Brush? brush)
    {
        if (brush is not ImageBrush imageBrush ||
            imageBrush.ScalingMode != BitmapScalingMode.Unspecified)
            return brush;

        var scaling = Resolve(owner, BitmapScalingMode.Unspecified);
        if (scaling == BitmapScalingMode.Unspecified) return brush;

        // Keep the authored brush and its image source live. A Freezable deep clone
        // would detach mutable bitmap sources from their owner.
        var copy = new ImageBrush
        {
            IsTransientPaintCopy = true,
            ImageSource = imageBrush.ImageSource,
            CssBackgroundLayout = imageBrush.CssBackgroundLayout,
            CssGradientLayout = imageBrush.CssGradientLayout,
            ScalingMode = scaling,
            Opacity = imageBrush.Opacity,
            Transform = imageBrush.Transform,
            RelativeTransform = imageBrush.RelativeTransform,
            AlignmentX = imageBrush.AlignmentX,
            AlignmentY = imageBrush.AlignmentY,
            Stretch = imageBrush.Stretch,
            TileMode = imageBrush.TileMode,
            Viewport = imageBrush.Viewport,
            ViewportUnits = imageBrush.ViewportUnits,
            Viewbox = imageBrush.Viewbox,
            ViewboxUnits = imageBrush.ViewboxUnits,
        };
        return copy;
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is UIElement element) element.InvalidateVisual();
    }
}
