using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal readonly record struct CssInlineSplitOptions(Size ContainingBlock, CssMarginStrut Pending, bool TopEscapes);
internal readonly record struct CssInlineSplitFlow(CssMarginStrut Before, CssMarginStrut Pending, bool Through, bool LastClearEmpty);
internal readonly record struct CssInlineBlockPlacement(Rect Rect, CssMarginStrut Before, CssMarginStrut After,
    bool Through, bool HasClearance, CssFloatEnvironment? Environment, double FirstBaseline, double Baseline, double OuterWidth);

internal sealed partial class CssFlowLayout
{
    private static void FlowForInlineChild(FrameworkElement child, GroupContext[] groups) =>
        CssDisplayLayout.FlowFor(child).SetInheritedGroups(CssFloatProperties.IsIndependentBox(child) ? [] : groups);

    private CssInlineBlockPlacement PlaceSplitBlock(UIElement element, Size containing, CssFloatSpace space,
        double cursor, CssMarginStrut pending, bool topEscapes)
    {
        var item = Measure(new CssLayoutBox(element, 0), containing);
        item = PlaceBlockBesideFloats(item, containing, space, cursor, pending, topEscapes);
        space.NoteSourceTop(item.Rect.Y - item.Margin.Top);
        if (element is FrameworkElement block && CssDisplayLayout.IsFlow(block) && !CssFloatProperties.IsIndependentBox(block))
            space.AddRange(ExportedRegions(block).Select(region => region.Offset(item.Rect.X, item.Rect.Y)));
        var first = double.NaN; var last = double.NaN;
        if (!CssDisplayLayout.TryGetBaseline(element, item.Height, last: false, out first) && element is TextBlock firstText)
            first = firstText.GetFirstBaselineOffset(item.Height);
        if (!CssDisplayLayout.TryGetBaseline(element, item.Height, last: true, out last) && element is TextBlock lastText)
            last = lastText.GetLastBaselineOffset(item.Height);
        return new(item.Rect, item.Before, item.After, item.Through, item.HasClearance,
            item.Environment, first, last, item.OuterWidth);
    }
}
