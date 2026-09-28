using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Computed CSS border colors in physical left, top, right, bottom order.</summary>
internal sealed record CssBorderPaint(Brush Left, Brush Top, Brush Right, Brush Bottom)
{
    internal Brush Edge(int edge) => edge switch
    {
        0 => Left, 1 => Top, 2 => Right, 3 => Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    internal bool IsUniform => Same(Left, Top) && Same(Top, Right) && Same(Right, Bottom);
    internal bool IsOpaque => Opaque(Left) && Opaque(Top) && Opaque(Right) && Opaque(Bottom);
    internal bool MatchesTop(Brush? brush) => brush is not null && Same(Top, brush);

    private static bool Opaque(Brush brush) => brush is SolidColorBrush solid &&
        solid.Color.A == byte.MaxValue && solid.Opacity >= 1;

    private static bool Same(Brush first, Brush second) => ReferenceEquals(first, second) ||
        first is SolidColorBrush a && second is SolidColorBrush b &&
        a.Color == b.Color && a.Opacity == b.Opacity;
}

internal static class CssBorderUsedThicknessProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBorderUsedThickness", typeof(Thickness), typeof(CssBorderUsedThicknessProperties),
        new PropertyMetadata(new Thickness(0), static (target, _) =>
        {
            if (target is FrameworkElement element)
            {
                element.InvalidateMeasure();
                element.InvalidateVisual();
            }
        }));

    internal static Thickness Get(FrameworkElement element) =>
        element.GetValue(ValueProperty) is Thickness thickness ? thickness : default;
}

internal static class CssLineStyleShading
{
    internal static Color ShadeColor(Color color, bool light)
    {
        static byte Lighter(byte channel) => (byte)Math.Round(channel + (255 - channel) * .35);
        static byte Darker(byte channel) => (byte)Math.Round(channel * .65);
        return light
            ? Color.FromArgb(color.A, Lighter(color.R), Lighter(color.G), Lighter(color.B))
            : Color.FromArgb(color.A, Darker(color.R), Darker(color.G), Darker(color.B));
    }

    internal static Brush Shade(Brush brush, bool light)
    {
        if (brush is not SolidColorBrush solid) return brush;
        var result = new SolidColorBrush(ShadeColor(solid.Color, light)) { Opacity = solid.Opacity };
        if (result.CanFreeze) result.Freeze();
        return result;
    }
}

internal static class CssBorderPaintProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBorderPaint", typeof(CssBorderPaint), typeof(CssBorderPaintProperties),
        new PropertyMetadata(null, static (target, _) => target.NotifyCssBorderPresentationChanged()));

    internal static CssBorderPaint? Get(DependencyObject target)
    {
        var layer = target.GetEffectiveValueLayer(ValueProperty);
        if (layer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState or
            DependencyValueStore.Layer.ParentTemplate)) return null;
        var paint = target.GetValue(ValueProperty) as CssBorderPaint;
        if (paint is null) return null;
        var brushProperty = CssDependencyPropertyLookup.Find(target.GetType(), "BorderBrush");
        if (brushProperty is not null)
        {
            var brushLayer = target.GetEffectiveValueLayer(brushProperty);
            if (layer == DependencyValueStore.Layer.ParentTemplate
                ? brushLayer != DependencyValueStore.Layer.ParentTemplate
                : brushLayer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
                return null;
            if (target is UIElement element && element.HasExplicitAnimation(brushProperty)) return null;
            if (!paint.MatchesTop(target.GetEffectiveBaseValue(brushProperty) as Brush)) return null;
        }
        return paint;
    }

    internal static bool IsTemplateBrushBinding(DependencyProperty? source, DependencyProperty target)
        => source?.Name == "BorderBrush" && source.PropertyType == typeof(Brush) &&
            target.Name == "BorderBrush" && target.PropertyType == typeof(Brush);

    internal static void TransferTemplate(FrameworkElement source, DependencyObject target)
    {
        if (Get(source) is { } paint)
            target.SetLayerValue(ValueProperty, paint, DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
        else ClearTemplate(target);
    }

    internal static void ClearTemplate(DependencyObject target)
        => target.ClearLayerValue(ValueProperty, DependencyObject.LayerValueSource.ParentTemplate,
            allowAutoTransition: false);

    internal static bool InheritColor(string name, CssNode parent, in CssApplyContext context)
    {
        if (!CssLogicalBoxEdges.IsBorderColor(name)) return false;
        var sourceEdge = CssLogicalBoxEdges.Edge(name,
            parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft);
        var targetEdge = CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft);
        var brush = Get(parent.Target)?.Edge(sourceEdge) ??
            (CssDependencyPropertyLookup.Find(parent.GetType(), "BorderBrush") is { } property
                ? parent.GetValue(property) as Brush : null) ?? context.CurrentColor;
        context.Slots.SetBorderColor(targetEdge, brush);
        return true;
    }
}

internal sealed class CssBorderColorEdge(string name, Brush brush) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Slots.SetBorderColor(CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft), brush);
        return true;
    }
}

internal enum CssBorderLineStyle : byte
{
    None,
    Hidden,
    Solid,
    Dotted,
    Dashed,
    Double,
    Groove,
    Ridge,
    Inset,
    Outset,
}

internal sealed record CssBorderStyles(
    CssBorderLineStyle Left, CssBorderLineStyle Top,
    CssBorderLineStyle Right, CssBorderLineStyle Bottom)
{
    internal CssBorderLineStyle Edge(int edge) => edge switch
    {
        0 => Left, 1 => Top, 2 => Right, 3 => Bottom,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    internal bool AllHidden => IsHidden(Left) && IsHidden(Top) && IsHidden(Right) && IsHidden(Bottom);
    internal bool AllSolid => Left == CssBorderLineStyle.Solid && Top == CssBorderLineStyle.Solid &&
        Right == CssBorderLineStyle.Solid && Bottom == CssBorderLineStyle.Solid;
    internal static bool IsHidden(CssBorderLineStyle style) => style is CssBorderLineStyle.None or CssBorderLineStyle.Hidden;
}

internal static class CssBorderStyleProperties
{
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "CssBorderStyles", typeof(CssBorderStyles), typeof(CssBorderStyleProperties),
        new PropertyMetadata(null, static (target, _) => target.NotifyCssBorderPresentationChanged()));

    internal static CssBorderStyles? Get(DependencyObject target)
    {
        var layer = target.GetEffectiveValueLayer(ValueProperty);
        if (layer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState or
            DependencyValueStore.Layer.ParentTemplate)) return null;
        var thicknessProperty = CssDependencyPropertyLookup.Find(target.GetType(), "BorderThickness");
        if (thicknessProperty is not null)
        {
            var thicknessLayer = target.GetEffectiveValueLayer(thicknessProperty);
            if (layer == DependencyValueStore.Layer.ParentTemplate
                ? thicknessLayer != DependencyValueStore.Layer.ParentTemplate
                : thicknessLayer is not (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
                return null;
        }
        return target.GetValue(ValueProperty) as CssBorderStyles;
    }

    internal static bool IsTemplateThicknessBinding(DependencyProperty? source, DependencyProperty target)
        => source?.Name == "BorderThickness" && source.PropertyType == typeof(Thickness) &&
            target.Name == "BorderThickness" && target.PropertyType == typeof(Thickness);

    internal static void TransferTemplate(FrameworkElement source, DependencyObject target)
    {
        if (Get(source) is { } styles)
            target.SetLayerValue(ValueProperty, styles, DependencyObject.LayerValueSource.ParentTemplate,
                allowAutoTransition: false);
        else ClearTemplate(target);
    }

    internal static void ClearTemplate(DependencyObject target)
        => target.ClearLayerValue(ValueProperty, DependencyObject.LayerValueSource.ParentTemplate,
            allowAutoTransition: false);

    internal static bool InheritStyle(string name, CssNode parent, in CssApplyContext context)
    {
        if (!CssLogicalBoxEdges.IsBorderStyle(name)) return false;
        var sourceEdge = CssLogicalBoxEdges.Edge(name,
            parent.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft);
        var targetEdge = CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft);
        context.Slots.SetBorderStyle(targetEdge,
            Get(parent.Target)?.Edge(sourceEdge) ?? CssBorderLineStyle.None);
        return true;
    }
}

internal sealed class CssBorderStyleEdge(string name, CssBorderLineStyle style) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        context.Slots.SetBorderStyle(
            CssLogicalBoxEdges.Edge(name, context.Slots.LogicalRightToLeft), style);
        return true;
    }
}
