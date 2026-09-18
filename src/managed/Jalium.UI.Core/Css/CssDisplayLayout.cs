using System.Runtime.CompilerServices;
using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal enum CssDisplayMode { Native, Flex, Grid, Block, FlowRoot }

/// <summary>Reuses the FlexPanel algorithm over an existing panel's child collection.</summary>
internal static class CssDisplayLayout
{
    private static readonly ConditionalWeakTable<Panel, FlexPanel> s_flexLayouts = new();
    private static readonly ConditionalWeakTable<Panel, CssGridLayout> s_gridLayouts = new();
    private static readonly ConditionalWeakTable<Panel, CssFlowLayout> s_flowLayouts = new();
    internal static CssFlowLayout FlowFor(Panel owner) => s_flowLayouts.GetValue(owner, static panel => new CssFlowLayout(panel));
    internal static bool IsFlow(FrameworkElement element) => element is Panel && element.CssDisplayMode is CssDisplayMode.Block or CssDisplayMode.FlowRoot;
    internal static bool HasBoxFormatter(FrameworkElement element) => element is Panel &&
        element.CssDisplayMode is CssDisplayMode.Grid or CssDisplayMode.Block or CssDisplayMode.FlowRoot;
    internal static CssGridLayout GridFor(Panel owner) => s_gridLayouts.GetValue(owner, static panel => new CssGridLayout(panel));
    internal static bool IsSubgridded(FrameworkElement element, bool row) =>
        element.CssDisplayMode == CssDisplayMode.Grid && element is Panel panel &&
        s_gridLayouts.TryGetValue(panel, out var layout) && layout.IsSubgridded(row);

    internal static readonly DependencyProperty ModeProperty = DependencyProperty.RegisterAttached(
        "DisplayMode", typeof(CssDisplayMode), typeof(CssDisplayLayout),
        new PropertyMetadata(CssDisplayMode.Native, static (target, args) =>
        {
            if (target is FrameworkElement element)
            {
                element.CssDisplayMode = (CssDisplayMode)args.NewValue!;
                element.InvalidateMeasure();
                if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
                if (element is Panel panel)
                {
                    panel.InvalidateCssFlowOrder();
                    foreach (UIElement child in panel.Children) { child.InvalidateMeasure(); child.InvalidateArrange(); }
                }
            }
        }));

    internal static readonly DependencyProperty InlineProperty = DependencyProperty.RegisterAttached(
        "InlineOuter", typeof(bool), typeof(CssDisplayLayout), new PropertyMetadata(false, static (target, args) =>
        {
            if (target is not FrameworkElement element) return;
            element.CssInlineOuter = (bool)args.NewValue!;
            element.InvalidateMeasure();
            if (element.VisualParent is UIElement parent) parent.InvalidateMeasure();
        }));

    public static bool TryMeasure(FrameworkElement element, Size available, out Size result)
    {
        if (IsFlow(element) && element is Panel flowOwner)
        {
            result = FlowFor(flowOwner).Measure(available);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Grid && element is Panel gridOwner)
        {
            result = GridFor(gridOwner).Measure(available);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Flex && element is Panel panel and not FlexPanel)
        {
            result = s_flexLayouts.GetValue(panel, static owner => new FlexPanel(owner)).MeasureCssLayout(available);
            return true;
        }
        result = default;
        return false;
    }

    public static bool TryArrange(FrameworkElement element, Size finalSize, out Size result)
    {
        if (IsFlow(element) && element is Panel flowOwner)
        {
            result = FlowFor(flowOwner).Arrange(finalSize);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Grid && element is Panel gridOwner)
        {
            result = GridFor(gridOwner).Arrange(finalSize);
            return true;
        }
        if (element.CssDisplayMode == CssDisplayMode.Flex && element is Panel panel and not FlexPanel)
        {
            result = s_flexLayouts.GetValue(panel, static owner => new FlexPanel(owner)).ArrangeCssLayout(finalSize);
            return true;
        }
        result = default;
        return false;
    }
}
