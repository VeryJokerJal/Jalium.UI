using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal readonly record struct CssMarginStrut(double Positive, double Negative)
{
    public double Value => Positive + Negative;
    public static CssMarginStrut From(double value) => new(Math.Max(0, value), Math.Min(0, value));
    public CssMarginStrut Merge(CssMarginStrut other) => new(Math.Max(Positive, other.Positive), Math.Min(Negative, other.Negative));
}

/// <summary>Block flow and atomic inline line boxes over the original native child collection.</summary>
internal sealed partial class CssFlowLayout
{
    private readonly Panel owner;
    private Snapshot? _snapshot;
    private readonly List<Snapshot> _cache = [];

    internal CssFlowLayout(Panel owner)
    {
        this.owner = owner;
        owner.PropertyChangedInternal += (property, _, _) =>
        {
            if (property != UIElement.ClipToBoundsProperty || !CssDisplayLayout.IsFlow(owner)) return;
            owner.InvalidateMeasure();
            if (owner.VisualParent is UIElement parent) parent.InvalidateMeasure();
        };
    }

    internal CssMarginStrut EscapedBefore => _snapshot?.Before ?? default;
    internal CssMarginStrut EscapedAfter => _snapshot?.After ?? default;
    internal bool CollapsesThrough => _snapshot?.Through == true;
    internal double LastBaseline => _snapshot?.Baseline ?? double.NaN;
    internal bool HasExportedFloats => _snapshot is { SharesParent: true, Floats.Length: > 0 };
    internal CssFloatRegion[] ExportedFloats => HasExportedFloats ? _snapshot!.Floats : [];

    internal Size Measure(Size available)
    {
        var insets = Insets(available.Width);
        var inner = CssBoxMetrics.InnerSize(available, insets);
        var containing = ContainingSize(inner);
        var exported = HasExportedFloats;
        _snapshot = Layout(containing, insets);
        if (exported != HasExportedFloats && owner.VisualParent is Panel parent) parent.InvalidateCssFlowOrder();
        owner.MeasureCssLayoutAbsoluteChildren(available);
        return new(_snapshot.Desired.Width + insets.Left + insets.Right, _snapshot.Desired.Height + insets.Top + insets.Bottom);
    }

    internal Size Arrange(Size finalSize)
    {
        var insets = Insets(finalSize.Width);
        var containing = ContainingSize(CssBoxMetrics.InnerSize(finalSize, insets));
        var snapshot = Layout(containing, insets);
        _snapshot = snapshot;
        foreach (var item in snapshot.Items)
        {
            item.Box.MeasureBorderBox(new Size(item.Width, double.PositiveInfinity), containing, item.Environment);
            item.Box.ArrangeBorderBox(new Rect(item.Rect.X + insets.Left, item.Rect.Y + insets.Top, item.Rect.Width, item.Rect.Height), containing,
                floats: item.Environment);
        }
        owner.ArrangeCssLayoutAbsoluteChildren(finalSize);
        return finalSize;
    }

    private Thickness Insets(double fallbackWidth) => CssBoxMetrics.ContentInsets(owner, owner.CssLayout?.ContainingWidthCache ?? fallbackWidth);

    private Size ContainingSize(Size available)
    {
        var parentHeight = owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height;
        var explicitHeight = owner.CssPreferredHeight(parentHeight);
        var gridOrFlexItem = owner.VisualParent is FrameworkElement parent && parent.CssDisplayMode is CssDisplayMode.Grid or CssDisplayMode.Flex;
        return new(available.Width, double.IsFinite(explicitHeight) || gridOrFlexItem ? available.Height : double.PositiveInfinity);
    }

    private bool SharesParentFlow => owner.CssParentBox is not null && !owner.ClipToBounds &&
        owner.CssLayout?.Position != CssPositionMode.Absolute && CssFloatProperties.ActiveSide(owner) == CssFloatSide.None &&
        !owner.CssInlineOuter && !CssContainerProperties.HasSizeContainment(owner) && owner.CssDisplayMode == CssDisplayMode.Block &&
        owner.VisualParent is FrameworkElement parent && CssDisplayLayout.IsFlow(parent);

    private Snapshot Layout(Size containing, Thickness insets)
    {
        var version = InputVersion(owner);
        var sharesParent = SharesParentFlow;
        var exclusions = sharesParent ? owner.CssParentBox?.Floats ?? CssFloatEnvironment.Empty : CssFloatEnvironment.Empty;
        foreach (var cached in _cache)
            if (cached.InputVersion == version && cached.Containing == containing && cached.Insets == insets &&
                cached.SharesParent == sharesParent && cached.Exclusions.Equals(exclusions))
                return cached;
        _cache.RemoveAll(cached => cached.InputVersion != version);
        var snapshot = Compute(containing, insets, sharesParent, version, exclusions);
        if (_cache.Count == 8) _cache.RemoveAt(0);
        _cache.Add(snapshot);
        return snapshot;
    }

    private Snapshot Compute(Size containing, Thickness insets, bool sharesParent, long version, CssFloatEnvironment exclusions)
    {
        var entries = new List<Entry>();
        foreach (var box in CssLayoutBox.ChildrenOf(owner, orderModified: false)) entries.Add(Measure(box, containing));
        if (exclusions.Regions.Length > 0 || entries.Any(item => item.Float != CssFloatSide.None || item.Clear != CssClearSide.None ||
            item.Box.Element is Panel panel && CssDisplayLayout.IsFlow(panel) && CssDisplayLayout.FlowFor(panel).HasExportedFloats))
            return ComputeFloats(entries, containing, insets, sharesParent, version, exclusions);
        var height = owner.CssPreferredHeight(owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height);
        var minHeight = owner.CssHeightBounds(containing.Height).Min;
        var topEscapes = sharesParent && insets.Top == 0;
        var bottomEscapes = sharesParent && insets.Bottom == 0 && double.IsNaN(height) && minHeight == 0;
        var canThrough = owner.CssDisplayMode == CssDisplayMode.Block && !owner.CssInlineOuter && !owner.ClipToBounds &&
            insets.Top == 0 && insets.Bottom == 0 && minHeight == 0 && (double.IsNaN(height) || height == 0);
        var cursor = 0d; var width = 0d; var baseline = double.NaN;
        var pending = default(CssMarginStrut); var before = default(CssMarginStrut); var after = default(CssMarginStrut);
        var atStart = true;
        for (var i = 0; i < entries.Count;)
        {
            var item = entries[i];
            if (item.Inline)
            {
                var start = i;
                while (i < entries.Count && entries[i].Inline) i++;
                if (atStart && topEscapes) before = before.Merge(pending);
                else cursor += pending.Value;
                pending = default;
                var lines = PlaceInline(entries, start, i, containing.Width, cursor);
                cursor = lines.Bottom;
                baseline = lines.Baseline + insets.Top;
                width = Math.Max(width, lines.Width);
                atStart = false;
                continue;
            }
            var adjoining = pending.Merge(item.Before);
            var y = atStart && topEscapes ? 0 : cursor + adjoining.Value;
            item.Rect = new(item.X, y, item.Width, item.Height);
            width = Math.Max(width, item.Width + item.Margin.Left + item.Margin.Right);
            if (item.Through)
            {
                pending = adjoining.Merge(item.After);
            }
            else
            {
                if (atStart && topEscapes) before = before.Merge(adjoining);
                cursor = y + item.Height;
                pending = item.After;
                atStart = false;
            }
            i++;
        }
        var through = canThrough && atStart;
        if (atStart && topEscapes)
        {
            before = before.Merge(pending);
            if (through) after = before;
        }
        else if (bottomEscapes) after = pending;
        else cursor += pending.Value;
        if (!owner.CssInlineOuter && CssFloatProperties.ActiveSide(owner) == CssFloatSide.None && double.IsFinite(containing.Width) &&
            (!owner.HasLocalOrAnimatedValue(FrameworkElement.HorizontalAlignmentProperty) || owner.HorizontalAlignment == HorizontalAlignment.Stretch))
            width = containing.Width;
        return new(containing, insets, new Size(Math.Max(0, width), Math.Max(0, cursor)), entries.ToArray(), before, after,
            through, baseline, sharesParent, version, exclusions, []);
    }

    private Entry Measure(CssLayoutBox box, Size containing, CssFloatEnvironment? environment = null, double? widthLimit = null)
    {
        var element = box.Element as FrameworkElement;
        var margin = element is null ? default : CssBoxMetrics.Margin(element, containing.Width);
        var side = CssFloatProperties.ActiveSide(box.Element);
        var inline = side == CssFloatSide.None && element?.CssInlineOuter == true;
        var availableWidth = widthLimit ?? Math.Max(0, containing.Width - margin.Left - margin.Right);
        var explicitWidth = element?.CssPreferredWidth(containing.Width) ?? double.NaN;
        double width;
        if ((inline || side != CssFloatSide.None) && double.IsNaN(explicitWidth))
        {
            box.MeasureBorderBox(new Size(0, double.PositiveInfinity), containing, environment);
            var min = box.Element.DesiredSize.Width;
            box.MeasureBorderBox(new Size(double.PositiveInfinity, double.PositiveInfinity), containing, environment);
            var max = box.Element.DesiredSize.Width;
            width = Math.Min(Math.Max(min, availableWidth), Math.Max(min, max));
        }
        else
        {
            box.MeasureBorderBox(new Size(availableWidth, double.PositiveInfinity), containing, environment);
            var localAlignment = element is not null && element.HasLocalOrAnimatedValue(FrameworkElement.HorizontalAlignmentProperty) &&
                element.HorizontalAlignment != HorizontalAlignment.Stretch;
            width = !double.IsNaN(explicitWidth) || inline || localAlignment || !double.IsFinite(availableWidth)
                ? box.Element.DesiredSize.Width : availableWidth;
        }
        if (element is not null)
        {
            var limits = element.CssWidthBounds(containing.Width);
            width = Math.Clamp(width, limits.Min, limits.Max);
        }
        box.MeasureBorderBox(new Size(Math.Max(0, width), double.PositiveInfinity), containing, environment);
        var height = box.Element.DesiredSize.Height;
        var before = CssMarginStrut.From(margin.Top); var after = CssMarginStrut.From(margin.Bottom);
        var through = false;
        if (!inline && side == CssFloatSide.None && element is Panel panel && CssDisplayLayout.IsFlow(panel))
        {
            var flow = CssDisplayLayout.FlowFor(panel);
            before = before.Merge(flow.EscapedBefore); after = after.Merge(flow.EscapedAfter);
            through = flow.CollapsesThrough && height == 0;
        }
        var x = margin.Left;
        var autoMargin = false;
        if (!inline && side == CssFloatSide.None && element is not null && !element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) && element.CssLayout is { HasMargin: true } layout)
        {
            autoMargin = layout.MarginLeft.IsAuto || layout.MarginRight.IsAuto;
            var remaining = containing.Width - width - margin.Left - margin.Right;
            var free = Math.Max(0, remaining);
            if (layout.MarginLeft.IsAuto) x += layout.MarginRight.IsAuto ? free / 2 : free;
            if (autoMargin && remaining < 0 && owner.FlowDirection == FlowDirection.RightToLeft) x = containing.Width - margin.Right - width;
        }
        if (!inline && owner.FlowDirection == FlowDirection.RightToLeft && !autoMargin)
            x = containing.Width - margin.Right - width;
        return new(box, margin, Math.Max(0, width), height, inline, before, after, through, x, side, CssFloatProperties.ClearSide(box.Element))
            { Environment = environment };
    }

    private (double Bottom, double Baseline, double Width) PlaceInline(List<Entry> entries, int start, int end, double available, double y)
    {
        var metrics = CssFlowProperties.LineMetrics(owner);
        var noWrap = owner.GetValueSourceInternal(TextBlock.TextWrappingProperty).BaseValueSource != BaseValueSource.Default &&
            (TextWrapping)owner.GetValue(TextBlock.TextWrappingProperty)! == TextWrapping.NoWrap;
        var lastBaseline = double.NaN; var maximumWidth = 0d;
        for (var lineStart = start; lineStart < end;)
        {
            var lineEnd = lineStart; var used = 0d;
            while (lineEnd < end)
            {
                var next = entries[lineEnd].OuterWidth;
                if (!noWrap && lineEnd > lineStart && used + next > available + 0.000001) break;
                used += next; lineEnd++;
            }
            var ascent = metrics.Baseline; var descent = metrics.Height - metrics.Baseline;
            var alignedHeight = 0d;
            for (var i = lineStart; i < lineEnd; i++)
            {
                var item = entries[i];
                item.Baseline = Baseline(item);
                var alignment = (CssInlineVerticalAlign)item.Box.Element.GetValue(CssFlowProperties.VerticalAlignProperty)!;
                item.Alignment = alignment.Kind;
                item.Shift = alignment.Kind switch
                {
                    CssInlineAlignment.Offset => alignment.Offset.Resolve(item.Box.Element is FrameworkElement element ? CssFlowProperties.LineMetrics(element).Height : metrics.Height, 0),
                    CssInlineAlignment.TextTop => metrics.Ascent - item.Baseline,
                    CssInlineAlignment.TextBottom => item.OuterHeight - metrics.Descent - item.Baseline, _ => 0,
                };
                if (alignment.Kind is CssInlineAlignment.Top or CssInlineAlignment.Bottom) alignedHeight = Math.Max(alignedHeight, item.OuterHeight);
                else
                {
                    ascent = Math.Max(ascent, item.Baseline + item.Shift);
                    descent = Math.Max(descent, item.OuterHeight - item.Baseline - item.Shift);
                }
            }
            var lineHeight = Math.Max(alignedHeight, ascent + descent);
            var free = double.IsFinite(available) ? available - used : 0;
            var textAlign = CssFlowProperties.TextAlignment(owner);
            var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
            var offset = textAlign switch
            {
                CssFlowTextAlignment.Center => free / 2, CssFlowTextAlignment.Right => free,
                CssFlowTextAlignment.Start when rtl => free, CssFlowTextAlignment.End when !rtl => free, _ => 0,
            };
            var x = rtl ? offset + used : offset;
            for (var i = lineStart; i < lineEnd; i++)
            {
                var item = entries[i];
                if (rtl) x -= item.OuterWidth;
                var top = item.Alignment switch
                {
                    CssInlineAlignment.Top => y, CssInlineAlignment.Bottom => y + lineHeight - item.OuterHeight,
                    _ => y + ascent - item.Baseline - item.Shift,
                };
                item.Rect = new(x + item.Margin.Left, top + item.Margin.Top, item.Width, item.Height);
                if (!rtl) x += item.OuterWidth;
            }
            maximumWidth = Math.Max(maximumWidth, used);
            lastBaseline = y + ascent;
            y += lineHeight;
            lineStart = lineEnd;
        }
        return (y, lastBaseline, maximumWidth);
    }

    private static double Baseline(Entry item)
    {
        var explicitBaseline = TextBlock.GetBaselineOffset(item.Box.Element);
        if (double.IsFinite(explicitBaseline)) return item.Margin.Top + explicitBaseline;
        if (item.Box.Element is Panel panel && CssDisplayLayout.IsFlow(panel) && !panel.ClipToBounds &&
            double.IsFinite(CssDisplayLayout.FlowFor(panel).LastBaseline))
            return item.Margin.Top + CssDisplayLayout.FlowFor(panel).LastBaseline;
        if (item.Box.Element is TextBlock text)
        {
            var metrics = CssFlowProperties.LineMetrics(text);
            return item.Margin.Top + Math.Max(0, item.Height - metrics.Height) + metrics.Baseline;
        }
        return item.OuterHeight;
    }

    private static long InputVersion(Visual visual)
    {
        var result = visual is UIElement element ? element.MeasureInputVersion : 0;
        for (var i = 0; i < visual.VisualChildrenCount; i++)
            if (visual.GetVisualChild(i) is { } child) result = Math.Max(result, InputVersion(child));
        return result;
    }

    private sealed record Snapshot(Size Containing, Thickness Insets, Size Desired, Entry[] Items, CssMarginStrut Before,
        CssMarginStrut After, bool Through, double Baseline, bool SharesParent, long InputVersion,
        CssFloatEnvironment Exclusions, CssFloatRegion[] Floats);

    private sealed class Entry(CssLayoutBox box, Thickness margin, double width, double height, bool inline,
        CssMarginStrut before, CssMarginStrut after, bool through, double x, CssFloatSide side, CssClearSide clear)
    {
        public CssLayoutBox Box { get; } = box;
        public Thickness Margin { get; } = margin;
        public double Width { get; } = width;
        public double Height { get; } = height;
        public bool Inline { get; } = inline;
        public CssMarginStrut Before { get; } = before;
        public CssMarginStrut After { get; } = after;
        public bool Through { get; } = through;
        public double X { get; } = x;
        public CssFloatSide Float { get; } = side;
        public CssClearSide Clear { get; } = clear;
        public CssFloatEnvironment? Environment;
        public bool HasClearance;
        public double OuterWidth => Width + Margin.Left + Margin.Right;
        public double OuterHeight => Height + Margin.Top + Margin.Bottom;
        public Rect Rect;
        public double Baseline, Shift;
        public CssInlineAlignment Alignment;
    }
}
