using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Replaced content uses its intrinsic size and ratio, independently of object-fit painting.</summary>
internal static class CssReplacedImageSizing
{
    internal static Size Measure(FrameworkElement owner, CssGeneratedImage image, Size available)
        => Measure(owner, image.Valid ? image.Intrinsic : default, image.Valid ? image.NaturalSize : default, available,
            image.Measure, image.Valid ? image.DefaultObjectSize : default(Size));

    internal static Size Measure(FrameworkElement owner, CssImageIntrinsicSize intrinsic, Size natural,
        Size available, Action<Size>? measureChild = null, Size? defaultObjectSize = null)
    {
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? available.Width);
        var x = insets.Left + insets.Right; var y = insets.Top + insets.Bottom;
        var basis = owner.CssParentBox?.ContainingBlock ?? CssPositionedLayout.MeasureContainingSize(owner, owner.PreviousAvailableSize);
        var widthSet = !double.IsNaN(owner.CssPreferredWidth(basis.Width));
        var heightSet = !double.IsNaN(owner.CssPreferredHeight(basis.Height));
        var width = widthSet && double.IsFinite(available.Width) ? Math.Max(0, available.Width - x) : natural.Width;
        var height = heightSet && double.IsFinite(available.Height) ? Math.Max(0, available.Height - y) : natural.Height;
        var ratio = EffectiveRatio(owner, intrinsic, out var authored);
        // An authored aspect-ratio follows box-sizing; an intrinsic image ratio
        // always relates the content dimensions, independently of box-sizing.
        var ratioChromeX = authored && owner.CssLayout?.BoxSizing != CssBoxSizing.ContentBox ? x : 0;
        var ratioChromeY = authored && owner.CssLayout?.BoxSizing != CssBoxSizing.ContentBox ? y : 0;
        if (ratio > 0)
        {
            if (widthSet && !heightSet) height = Math.Max(0, (width + ratioChromeX) / ratio - ratioChromeY);
            else if (heightSet && !widthSet) width = Math.Max(0, (height + ratioChromeY) * ratio - ratioChromeX);
            else if (!widthSet && !heightSet)
            {
                if (intrinsic.Width.HasValue) height = Math.Max(0, (width + ratioChromeX) / ratio - ratioChromeY);
                else if (intrinsic.Height.HasValue) width = Math.Max(0, (height + ratioChromeY) * ratio - ratioChromeX);
                else
                {
                    var fallback = defaultObjectSize ?? CssImageIntrinsicSize.ReplacedDefault;
                    var size = new CssImageIntrinsicSize(null, null, ratio).Constraint(new(fallback.Width + ratioChromeX, fallback.Height + ratioChromeY), false);
                    width = Math.Max(0, size.Width - ratioChromeX); height = Math.Max(0, size.Height - ratioChromeY);
                }
            }
        }
        var widthBounds = owner.CssWidthBounds(basis.Width);
        var heightBounds = owner.CssHeightBounds(basis.Height);
        var minWidth = Math.Max(0, widthBounds.Min - x); var maxWidth = Math.Max(minWidth, widthBounds.Max - x);
        var minHeight = Math.Max(0, heightBounds.Min - y); var maxHeight = Math.Max(minHeight, heightBounds.Max - y);
        if (!widthSet && !heightSet && ratio > 0 && width > 0 && height > 0)
        {
            // CSS 2.2 10.4: preserve the intrinsic ratio while satisfying both
            // axes; conflicting limits override the ratio at their boundaries.
            var ratioWidth = width + ratioChromeX; var ratioHeight = height + ratioChromeY;
            var minScale = Math.Max((minWidth + ratioChromeX) / ratioWidth, (minHeight + ratioChromeY) / ratioHeight);
            var maxScale = Math.Min((maxWidth + ratioChromeX) / ratioWidth, (maxHeight + ratioChromeY) / ratioHeight);
            var scale = Math.Clamp(1, Math.Min(minScale, maxScale), Math.Max(minScale, maxScale));
            width = Math.Max(0, ratioWidth * scale - ratioChromeX);
            height = Math.Max(0, ratioHeight * scale - ratioChromeY);
        }
        width = Math.Clamp(width, minWidth, maxWidth);
        height = Math.Clamp(height, minHeight, maxHeight);
        measureChild?.Invoke(new(width, height));
        return new(Math.Max(0, width + x), Math.Max(0, height + y));
    }

    private static double EffectiveRatio(FrameworkElement owner, CssImageIntrinsicSize intrinsic, out bool authored)
    {
        var layout = owner.CssLayout;
        authored = layout is { AspectRatio: > 0 } && (!layout.AspectRatioAuto || !(intrinsic.Ratio > 0));
        return authored ? layout!.AspectRatio : intrinsic.Ratio;
    }

    internal static bool NativeActive(FrameworkElement owner) => owner is Controls.Image { UsesCssIntrinsicSizing: true };

    internal static Size ArrangeNativeBox(Controls.Image owner, Size outer, bool widthSet, bool heightSet)
    {
        var intrinsic = owner.Source is { } source ? CssImageIntrinsicSize.FromSource(source) : default;
        var ratio = EffectiveRatio(owner, intrinsic, out var authored);
        if (!(ratio > 0) || widthSet == heightSet) return outer;
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? outer.Width);
        var x = insets.Left + insets.Right; var y = insets.Top + insets.Bottom;
        var rx = authored && owner.CssLayout?.BoxSizing != CssBoxSizing.ContentBox ? x : 0;
        var ry = authored && owner.CssLayout?.BoxSizing != CssBoxSizing.ContentBox ? y : 0;
        return widthSet ? new(outer.Width, Math.Max(0, (Math.Max(0, outer.Width - x) + rx) / ratio - ry) + y)
            : new(Math.Max(0, (Math.Max(0, outer.Height - y) + ry) * ratio - rx) + x, outer.Height);
    }

    internal static void Arrange(FrameworkElement owner, CssGeneratedImage image, Size size)
    {
        var insets = CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? size.Width);
        var content = new Rect(insets.Left, insets.Top, Math.Max(0, size.Width - insets.Left - insets.Right), Math.Max(0, size.Height - insets.Top - insets.Bottom));
        var fit = CssImageObjectProperties.Fit(owner);
        if (fit == CssObjectFit.Unspecified) fit = CssObjectFit.Fill;
        var concrete = image.Intrinsic.ObjectSize(content.Size, fit);
        var rect = CssImageObjectProperties.PositionedRect(owner, content, concrete);
        image.Arrange(rect);
        var radii = CssReplacedContent.Radii(owner, size);
        image.Clip = new CssRoundedRectangleGeometry(new(content.X - rect.X, content.Y - rect.Y, content.Width, content.Height), radii.Inset(insets));
    }
}
