using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.TextFormatting;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private sealed class ListMarkerLayout(ListItem item, FormattedText text, double gap, bool rtl)
    {
        internal readonly ListItem Item = item;
        internal readonly FormattedText Text = text;
        internal readonly double Gap = gap;
        internal readonly bool RightToLeft = rtl;
        internal readonly long FontEpoch = TextMeasurement.FontCacheEpoch;
        internal double Gutter, ContentWidth;
        internal double Left => RightToLeft ? ContentWidth - Gutter + Gap : Gutter - Gap - Text.Width;
    }

    private void LayoutList(DocList list, BlockLayoutInfo block, double maxWidth, ref int offset)
    {
        var padding = SafeInsets(list.Padding);
        var margin = block.Margin;
        block.Margin = new(margin.Left + padding.Left, margin.Top + padding.Top,
            margin.Right + padding.Right, margin.Bottom + padding.Bottom);
        bool unbounded = !double.IsFinite(maxWidth);
        double available = unbounded ? 100000 : Math.Max(1, maxWidth - block.Margin.Left - block.Margin.Right);
        bool rtl = list.FlowDirection == FlowDirection.RightToLeft;
        var markers = new List<ListMarkerLayout>();
        double gutter = 20;
        for (int index = 0; index < list.ListItems.Count; ++index)
        {
            var item = list.ListItems[index];
            var text = TextSimpleMarkerProperties.GetMarkerText(list.MarkerStyle, (long)list.StartIndex + index);
            var formatted = new FormattedText(text,
                item.FontFamily?.GetRenderingSource(item) ?? ResolveNativeRunFamily(null),
                item.FontSize > 0 ? item.FontSize : _document.FontSize)
            { FontWeight = item.FontWeight.ToOpenTypeWeight(), FontStyle = item.FontStyle.ToOpenTypeStyle() };
            TextMeasurement.MeasureText(formatted);
            double gap = double.IsNaN(list.MarkerOffset) ? Math.Max(2, formatted.FontSize * .25) : list.MarkerOffset;
            var marker = new ListMarkerLayout(item, formatted, gap, rtl);
            markers.Add(marker);
            if (text.Length != 0) gutter = Math.Max(gutter, formatted.Width + gap);
        }
        foreach (var marker in markers)
        {
            var item = marker.Item;
            var itemPadding = SafeInsets(item.Padding);
            static double Edge(double value) => double.IsFinite(value) ? value : 0;
            var insets = new Thickness(Edge(item.Margin.Left) + itemPadding.Left, Edge(item.Margin.Top) + itemPadding.Top,
                Edge(item.Margin.Right) + itemPadding.Right, Edge(item.Margin.Bottom) + itemPadding.Bottom);
            var wrapper = new BlockLayoutInfo { Block = new Section(), Margin = insets,
                ChildOffset = rtl ? 0 : gutter, ListMarker = marker };
            marker.Gutter = gutter;
            marker.ContentWidth = Math.Max(gutter + 1, available - insets.Left - insets.Right);
            int start = offset;
            void Children(double width, ref int next)
            {
                foreach (var child in item.Blocks) wrapper.ChildBlocks.Add(LayoutBlock(child, width, ref next));
                if (wrapper.ChildBlocks.Count == 0)
                {
                    double height = GetLineHeightForFormatting(marker.Text.FontSize);
                    var empty = new BlockLayoutInfo { Block = new Paragraph() };
                    empty.Lines.Add(new LineLayoutInfo { StartOffset = next, EndOffset = next,
                        Height = height, Baseline = height * .8 });
                    wrapper.ChildBlocks.Add(empty);
                }
            }
            Children(Math.Max(1, marker.ContentWidth - gutter), ref offset);
            if (unbounded)
            {
                var lines = new List<(LineLayoutInfo line, double y, double x)>();
                double y = 0; CollectAllLines(wrapper.ChildBlocks, 0, ref y, lines);
                double width = lines.Count == 0 ? 1 : Math.Max(1, lines.Max(line => line.x + line.line.Width));
                foreach (var child in wrapper.ChildBlocks) width = Math.Max(width, TableExtent(child, 0));
                foreach (var child in wrapper.ChildBlocks) child.Dispose();
                wrapper.ChildBlocks.Clear(); offset = start;
                marker.ContentWidth = gutter + width;
                Children(width, ref offset);
            }
            block.ChildBlocks.Add(wrapper);
        }
    }

    private void RenderListMarker(DrawingContext context, BlockLayoutInfo block, ListMarkerLayout marker,
        double x, double y, Rect viewport)
    {
        if (marker.Text.Text.Length == 0) return;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double bottom = 0; CollectAllLines(block.ChildBlocks, 0, ref bottom, lines);
        if (lines.Count == 0) return;
        var first = lines.OrderBy(line => line.y).First();
        double top = y + first.y + first.line.Baseline - marker.Text.Baseline;
        if (top + marker.Text.Height <= viewport.Top || top >= viewport.Bottom) return;
        marker.Text.Foreground = marker.Item.Foreground ?? _document.Foreground ?? ResolveDocumentForegroundBrush();
        using var clip = MacOSWritingToolsPresentation.PushTextClip(this, context);
        context.DrawText(marker.Text, new Point(x + marker.Left, top));
    }

    private static double ListMarkerExtent(BlockLayoutInfo block, double x)
    {
        x += block.Margin.Left;
        double width = block.ListMarker is { } marker && marker.Text.Text.Length != 0
            ? x + marker.Left + marker.Text.Width : 0;
        foreach (var child in block.ChildBlocks)
            width = Math.Max(width, ListMarkerExtent(child, x + block.ChildOffset));
        if (block.Table is { } table)
            foreach (var cell in table.Cells)
                foreach (var child in cell.Blocks)
                    width = Math.Max(width, ListMarkerExtent(child, x + cell.Bounds.Left + cell.Insets.Left));
        return width;
    }
}
