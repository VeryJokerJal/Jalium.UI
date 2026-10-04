using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssGridLayout
{
    private sealed record GridBaselineGroups(Dictionary<Item, double> Shims,
        Dictionary<int, double> FirstRows, Dictionary<int, double> LastRows);

    private CssBoxAlignment BlockAlignment()
    {
        var alignment = (CssBoxAlignment)owner.GetValue(CssGridProperties.AlignItemsProperty)!;
        if (owner.HasLocalOrAnimatedValue(FlexPanel.AlignItemsProperty))
            return (FlexAlign)owner.GetValue(FlexPanel.AlignItemsProperty)! switch
            {
                FlexAlign.Baseline => CssBoxAlignment.Baseline,
                FlexAlign.LastBaseline => CssBoxAlignment.LastBaseline,
                _ => alignment,
            };
        return alignment;
    }

    private static CssBoxAlignment ItemBlockAlignment(Item item, CssBoxAlignment container) =>
        FlexPanel.GetAlignSelf(item.Box.Element) switch
        {
            FlexAlign.Auto => container,
            FlexAlign.Stretch => CssBoxAlignment.Stretch,
            FlexAlign.FlexStart => CssBoxAlignment.Start,
            FlexAlign.FlexEnd => CssBoxAlignment.End,
            FlexAlign.Center => CssBoxAlignment.Center,
            FlexAlign.Baseline => CssBoxAlignment.Baseline,
            FlexAlign.LastBaseline => CssBoxAlignment.LastBaseline,
            _ => CssBoxAlignment.Start,
        };

    private static bool ParticipatesInBlockBaseline(Item item, CssBoxAlignment container)
    {
        if (ItemBlockAlignment(item, container) is not (CssBoxAlignment.Baseline or CssBoxAlignment.LastBaseline)) return false;
        if (item.Box.Element is not FrameworkElement element) return true;
        if (element.HasLocalOrAnimatedValue(FrameworkElement.VerticalAlignmentProperty)) return false;
        if (element is Panel panel && CssDisplayLayout.IsSubgridded(panel, true)) return false;
        return element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) ||
            element.CssLayout is not { HasMargin: true } margin ||
            !margin.MarginTop.IsAuto && !margin.MarginBottom.IsAuto;
    }

    private static bool AutoBlockMarginShift(Item item, double areaHeight, out double shift)
    {
        shift = 0;
        if (item.Box.Element is not FrameworkElement element ||
            CssDisplayLayout.IsSubgridded(element, true) ||
            element.HasLocalOrAnimatedValue(FrameworkElement.VerticalAlignmentProperty) ||
            element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) ||
            element.CssLayout is not { HasMargin: true } margin ||
            !margin.MarginTop.IsAuto && !margin.MarginBottom.IsAuto) return false;
        var free = Math.Max(0, areaHeight - element.DesiredSize.Height);
        if (margin.MarginTop.IsAuto)
            shift = margin.MarginBottom.IsAuto ? free / 2 : free;
        return true;
    }

    private static double ItemBaseline(Item item, double areaWidth, double outerHeight, bool last = false)
    {
        var element = item.Box.Element;
        if (element is not FrameworkElement framework) return outerHeight;
        var margin = CssBoxMetrics.Margin(framework, areaWidth);
        var borderHeight = Math.Max(0, outerHeight - margin.Top - margin.Bottom);
        var explicitBaseline = TextBlock.GetBaselineOffset(element);
        if (double.IsFinite(explicitBaseline)) return margin.Top + explicitBaseline;
        if (CssDisplayLayout.TryGetBaseline(element, borderHeight, last, out var nested))
            return margin.Top + nested;
        if (element is TextBlock text)
        {
            var textBaseline = last ? text.GetLastBaselineOffset(borderHeight) : text.GetFirstBaselineOffset(borderHeight);
            if (double.IsFinite(textBaseline)) return margin.Top + textBaseline;
        }
        return outerHeight - margin.Bottom;
    }

    private static GridBaselineGroups BaselineGroups(Item[] items, SizedAxis columns, CssBoxAlignment container)
    {
        var firstMembers = new List<(Item Item, int Row, double Ascent)>();
        var lastMembers = new List<(Item Item, int Row, double Descent)>();
        foreach (var item in items)
        {
            if (!ParticipatesInBlockBaseline(item, container)) continue;
            var width = columns.SpanSize(item.Column, item.ColumnSpan);
            var height = item.Box.Element.DesiredSize.Height;
            if (ItemBlockAlignment(item, container) == CssBoxAlignment.LastBaseline)
                lastMembers.Add((item, item.Row + item.RowSpan - 1,
                    height - ItemBaseline(item, width, height, last: true)));
            else
                firstMembers.Add((item, item.Row, ItemBaseline(item, width, height)));
        }
        var shims = new Dictionary<Item, double>();
        var firstRows = new Dictionary<int, double>();
        foreach (var group in firstMembers.GroupBy(member => member.Row))
        {
            var ascent = group.Max(member => member.Ascent);
            firstRows.Add(group.Key, ascent);
            foreach (var member in group) shims.Add(member.Item, ascent - member.Ascent);
        }
        var lastRows = new Dictionary<int, double>();
        foreach (var group in lastMembers.GroupBy(member => member.Row))
        {
            var descent = group.Max(member => member.Descent);
            lastRows.Add(group.Key, descent);
            foreach (var member in group) shims.Add(member.Item, descent - member.Descent);
        }
        return new(shims, firstRows, lastRows);
    }

    // Grid §11.6: export a shared baseline from the first/last occupied row
    // when one exists, otherwise use the first/last item in grid order.
    internal double BaselineOffset(double borderHeight, bool last)
    {
        var snapshot = _snapshot;
        if (snapshot is null || snapshot.Items.Length == 0) return double.NaN;
        var insets = CssBoxMetrics.ContentInsets(owner,
            owner.CssLayout?.ContainingWidthCache ?? owner.DesiredSize.Width);
        var usedInnerHeight = Math.Max(0, borderHeight - insets.Top - insets.Bottom);
        if (double.IsFinite(usedInnerHeight) &&
            Math.Abs(snapshot.Available.Height - usedInnerHeight) > 0.001)
            snapshot = Compute(new Size(snapshot.Available.Width, usedInnerHeight));
        var row = last
            ? snapshot.Items.Max(item => item.Row + item.RowSpan - 1)
            : snapshot.Items.Min(item => item.Row);
        var alignItems = BlockAlignment();
        var groups = BaselineGroups(snapshot.Items, snapshot.Columns, alignItems);
        if (!last && groups.FirstRows.TryGetValue(row, out var firstShared))
            return insets.Top + snapshot.Rows.Starts[row] + firstShared;
        if (last && groups.LastRows.TryGetValue(row, out var lastShared))
            return insets.Top + snapshot.Rows.Starts[row] + snapshot.Rows.Sizes[row] - lastShared;
        if (!last && groups.LastRows.TryGetValue(row, out var firstRowLastShared))
            return insets.Top + snapshot.Rows.Starts[row] + snapshot.Rows.Sizes[row] - firstRowLastShared;
        if (last && groups.FirstRows.TryGetValue(row, out var lastRowFirstShared))
            return insets.Top + snapshot.Rows.Starts[row] + lastRowFirstShared;

        var candidates = snapshot.Items.Where(item => item.Row <= row && item.Row + item.RowSpan > row);
        // Column indices already follow inline-start; RTL mirrors only their
        // physical X positions during Arrange.
        var ordered = candidates.OrderBy(item => item.Column);
        var selected = last ? ordered.Last() : ordered.First();
        var width = snapshot.Columns.SpanSize(selected.Column, selected.ColumnSpan);
        var height = snapshot.Rows.SpanSize(selected.Row, selected.RowSpan);
        var outerHeight = selected.Box.Element.DesiredSize.Height;
        var alignment = ItemBlockAlignment(selected, alignItems);
        if (selected.Box.Element is FrameworkElement framework)
        {
            if (framework.HasLocalOrAnimatedValue(FrameworkElement.VerticalAlignmentProperty))
                alignment = framework.VerticalAlignment switch
                {
                    VerticalAlignment.Center => CssBoxAlignment.Center,
                    VerticalAlignment.Bottom => CssBoxAlignment.End,
                    VerticalAlignment.Stretch => CssBoxAlignment.Stretch,
                    _ => CssBoxAlignment.Start,
                };
            if (alignment is CssBoxAlignment.Normal or CssBoxAlignment.Stretch &&
                double.IsNaN(framework.CssPreferredHeight(height))) outerHeight = height;
        }
        var free = Math.Max(0, height - outerHeight);
        var offset = AutoBlockMarginShift(selected, height, out var autoShift) ? autoShift
            : alignment switch
            {
                CssBoxAlignment.Center => free / 2,
                CssBoxAlignment.End or CssBoxAlignment.LastBaseline => free,
                _ => 0,
            };
        return insets.Top + snapshot.Rows.Starts[selected.Row] + offset +
            ItemBaseline(selected, width, outerHeight, last);
    }
}
