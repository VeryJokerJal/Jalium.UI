using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal enum CssFloatSide { None, Left, Right, InlineStart, InlineEnd }
internal enum CssClearSide { None, Left, Right, Both, InlineStart, InlineEnd }

internal static class CssFloatProperties
{
    internal static readonly DependencyProperty FloatProperty = DependencyProperty.RegisterAttached(
        "CssFloat", typeof(CssFloatSide), typeof(CssFloatProperties), new PropertyMetadata(CssFloatSide.None, Changed));
    internal static readonly DependencyProperty ClearProperty = DependencyProperty.RegisterAttached(
        "CssClear", typeof(CssClearSide), typeof(CssFloatProperties), new PropertyMetadata(CssClearSide.None, Changed));

    internal static void Register()
    {
        CssPropertyRegistry.Register(new()
        {
            Name = "float", Kind = CssPropertyKind.Longhand, StorageProperty = FloatProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var keyword) || !reader.AtEnd) return null;
                CssFloatSide? value = keyword.ToString().ToLowerInvariant() switch
                {
                    "none" => CssFloatSide.None, "left" => CssFloatSide.Left, "right" => CssFloatSide.Right,
                    "inline-start" => CssFloatSide.InlineStart, "inline-end" => CssFloatSide.InlineEnd, _ => null,
                };
                return value is { } side ? new CssImmediateValue(FloatProperty, side) : null;
            },
        });
        CssPropertyRegistry.Register(new()
        {
            Name = "clear", Kind = CssPropertyKind.Longhand, StorageProperty = ClearProperty,
            Parse = (ref CssTokenReader reader, CssCompileContext _) =>
            {
                if (!reader.TryReadIdent(out var keyword) || !reader.AtEnd) return null;
                CssClearSide? value = keyword.ToString().ToLowerInvariant() switch
                {
                    "none" => CssClearSide.None, "left" => CssClearSide.Left, "right" => CssClearSide.Right,
                    "both" => CssClearSide.Both, "inline-start" => CssClearSide.InlineStart, "inline-end" => CssClearSide.InlineEnd, _ => null,
                };
                return value is { } side ? new CssImmediateValue(ClearProperty, side) : null;
            },
        });
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is not UIElement element) return;
        element.InvalidateMeasure();
        for (var parent = element.VisualParent as FrameworkElement; parent is not null; parent = parent.VisualParent as FrameworkElement)
        {
            parent.InvalidateMeasure();
            if (parent is Panel panel) panel.InvalidateCssFlowOrder();
            if (!CssDisplayLayout.IsFlow(parent)) break;
        }
    }

    internal static CssFloatSide ActiveSide(UIElement element)
    {
        if (element is not FrameworkElement fe || fe.CssLayout?.Position == CssPositionMode.Absolute ||
            fe.VisualParent is not FrameworkElement parent || !CssDisplayLayout.IsFlow(parent)) return CssFloatSide.None;
        var side = (CssFloatSide)element.GetValue(FloatProperty)!;
        return side switch
        {
            CssFloatSide.InlineStart => parent.FlowDirection == FlowDirection.RightToLeft ? CssFloatSide.Right : CssFloatSide.Left,
            CssFloatSide.InlineEnd => parent.FlowDirection == FlowDirection.RightToLeft ? CssFloatSide.Left : CssFloatSide.Right, _ => side,
        };
    }

    internal static CssClearSide ClearSide(UIElement element)
    {
        var side = (CssClearSide)element.GetValue(ClearProperty)!;
        var rtl = element.VisualParent is FrameworkElement { FlowDirection: FlowDirection.RightToLeft };
        return side switch
        {
            CssClearSide.InlineStart => rtl ? CssClearSide.Right : CssClearSide.Left,
            CssClearSide.InlineEnd => rtl ? CssClearSide.Left : CssClearSide.Right, _ => side,
        };
    }

    internal static bool IsIndependentBox(UIElement element) => element is not Panel panel || !CssDisplayLayout.IsFlow(panel) ||
        panel.CssDisplayMode != CssDisplayMode.Block || panel.CssInlineOuter || panel.ClipToBounds ||
        CssContainerProperties.HasSizeContainment(panel) || ActiveSide(panel) != CssFloatSide.None;

    internal static int PaintRank(Panel parent, UIElement child)
    {
        if (!CssDisplayLayout.IsFlow(parent)) return 0;
        if (child is FrameworkElement { CssLayout.Position: CssPositionMode.Absolute }) return 3;
        if (ActiveSide(child) != CssFloatSide.None) return 1;
        if (child is FrameworkElement { CssInlineOuter: true }) return 2;
        return child is Panel panel && CssDisplayLayout.IsFlow(panel) && CssDisplayLayout.FlowFor(panel).HasExportedFloats ? 1 : 0;
    }
}
