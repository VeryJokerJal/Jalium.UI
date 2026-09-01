using Jalium.UI.Styling;

namespace Jalium.UI;

public partial class FrameworkElement
{
    /// <summary>
    /// CSS layout state (%, aspect-ratio, box-sizing, position/inset). Null when CSS layout
    /// does not participate — the hot layout path pays a single null check. Written only by
    /// the CSS engine's apply diff (which also calls InvalidateMeasure).
    /// </summary>
    internal CssLayoutState? CssLayout;

    /// <summary>
    /// The element's chance to consume a CSS declaration by name — the interception
    /// protocol container types override to map CSS concepts onto their own properties
    /// (e.g. StackPanel maps flex-direction to Orientation). Called by the engine for
    /// registered intercepted properties and as the last resort before an
    /// unknown-property diagnostic. Return true when consumed. Implementations must
    /// write values through <paramref name="setter"/> (never SetValue), may read their
    /// own properties, and must not trigger layout synchronously.
    /// </summary>
    protected internal virtual bool TryApplyCssPropertyCore(
        string propertyName, string rawValue, in CssDeclarationSetter setter)
        => false;

    /// <summary>Margin resolved against the containing block (CSS %: basis is the block's width).</summary>
    private static Thickness ResolveCssMargin(CssLayoutState layout, double basisWidth)
        => new(
            layout.MarginLeft.Resolve(basisWidth, 0),
            layout.MarginTop.Resolve(basisWidth, 0),
            layout.MarginRight.Resolve(basisWidth, 0),
            layout.MarginBottom.Resolve(basisWidth, 0));

    /// <summary>
    /// The effective explicit width: the Width DP wins when set; otherwise a CSS percentage
    /// resolves against the basis (degrading to auto against an infinite basis); NaN = auto.
    /// box-sizing:content-box adds the padding+border chrome to explicit values so the
    /// framework's border-box measurement model receives the equivalent outer size.
    /// </summary>
    private double ResolveEffectiveWidth(CssLayoutState? layout, double basisWidth)
    {
        var width = Width;
        if (double.IsNaN(width) && layout is not null && layout.Width.IsSet)
        {
            width = layout.Width.Resolve(basisWidth, double.NaN);
        }

        if (!double.IsNaN(width) && layout is { BoxSizing: CssBoxSizing.ContentBox })
        {
            width += GetContentBoxChromeX();
        }

        return width;
    }

    private double ResolveEffectiveHeight(CssLayoutState? layout, double basisHeight)
    {
        var height = Height;
        if (double.IsNaN(height) && layout is not null && layout.Height.IsSet)
        {
            height = layout.Height.Resolve(basisHeight, double.NaN);
        }

        if (!double.IsNaN(height) && layout is { BoxSizing: CssBoxSizing.ContentBox })
        {
            height += GetContentBoxChromeY();
        }

        return height;
    }

    private (double Min, double Max) ResolveEffectiveWidthBounds(CssLayoutState? layout, double basisWidth)
    {
        var min = MinWidth;
        var max = MaxWidth;
        if (layout is not null)
        {
            var chrome = layout.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeX() : 0;
            if (min == 0 && layout.MinWidth.IsSet)
            {
                min = Math.Max(0, layout.MinWidth.Resolve(basisWidth, 0) + chrome);
            }

            if (double.IsPositiveInfinity(max) && layout.MaxWidth.IsSet)
            {
                var resolved = layout.MaxWidth.Resolve(basisWidth, double.PositiveInfinity);
                max = double.IsPositiveInfinity(resolved) ? resolved : Math.Max(0, resolved + chrome);
            }
        }

        return (min, max);
    }

    private (double Min, double Max) ResolveEffectiveHeightBounds(CssLayoutState? layout, double basisHeight)
    {
        var min = MinHeight;
        var max = MaxHeight;
        if (layout is not null)
        {
            var chrome = layout.BoxSizing == CssBoxSizing.ContentBox ? GetContentBoxChromeY() : 0;
            if (min == 0 && layout.MinHeight.IsSet)
            {
                min = Math.Max(0, layout.MinHeight.Resolve(basisHeight, 0) + chrome);
            }

            if (double.IsPositiveInfinity(max) && layout.MaxHeight.IsSet)
            {
                var resolved = layout.MaxHeight.Resolve(basisHeight, double.PositiveInfinity);
                max = double.IsPositiveInfinity(resolved) ? resolved : Math.Max(0, resolved + chrome);
            }
        }

        return (min, max);
    }

    /// <summary>aspect-ratio = width/height. Derives the auto axis from the determinate one.</summary>
    private static (double Width, double Height) ApplyCssAspectRatio(
        double ratio, double width, bool widthDeterminate, double height, bool heightDeterminate)
    {
        if (widthDeterminate && heightDeterminate)
        {
            return (width, height);
        }

        if (widthDeterminate)
        {
            return (width, width / ratio);
        }

        if (heightDeterminate)
        {
            return (height * ratio, height);
        }

        // Both auto: derive from the content's main axis (width first, per CSS block flow).
        if (width > 0)
        {
            return (width, width / ratio);
        }

        if (height > 0)
        {
            return (height * ratio, height);
        }

        return (width, height);
    }

    /// <summary>
    /// Resolves %-padding into the element's Padding DP (CssBase layer) right before
    /// MeasureOverride, so the padding consumers (Control/Border subclasses) see plain
    /// pixel values. Equal values are skipped to avoid invalidation churn.
    /// </summary>
    private void MaterializeCssPadding(CssLayoutState layout, double basisWidth)
    {
        var dp = CssDependencyPropertyLookup.Find(GetType(), "Padding");
        if (dp is null)
        {
            CssDiagnostics.Report(
                "padding", CssDiagnosticReason.TargetPropertyMissing, GetType(),
                "no dependency property 'Padding' on this element type; declaration skipped here");
            return;
        }

        var thickness = new Thickness(
            Math.Max(0, layout.PaddingLeft.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingTop.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingRight.Resolve(basisWidth, 0)),
            Math.Max(0, layout.PaddingBottom.Resolve(basisWidth, 0)));

        if (TryGetLayerValue(dp, LayerValueSource.CssBase, out var existing) &&
            existing is Thickness current && current == thickness)
        {
            return;
        }

        SetLayerValue(dp, thickness, LayerValueSource.CssBase, allowAutoTransition: false);
    }

    private double GetContentBoxChromeX()
    {
        double chrome = 0;
        if (CssDependencyPropertyLookup.Find(GetType(), "Padding") is { } paddingDp &&
            GetValue(paddingDp) is Thickness padding)
        {
            chrome += padding.Left + padding.Right;
        }

        if (CssDependencyPropertyLookup.Find(GetType(), "BorderThickness") is { } borderDp &&
            GetValue(borderDp) is Thickness border)
        {
            chrome += border.Left + border.Right;
        }

        return chrome;
    }

    private double GetContentBoxChromeY()
    {
        double chrome = 0;
        if (CssDependencyPropertyLookup.Find(GetType(), "Padding") is { } paddingDp &&
            GetValue(paddingDp) is Thickness padding)
        {
            chrome += padding.Top + padding.Bottom;
        }

        if (CssDependencyPropertyLookup.Find(GetType(), "BorderThickness") is { } borderDp &&
            GetValue(borderDp) is Thickness border)
        {
            chrome += border.Top + border.Bottom;
        }

        return chrome;
    }
}
