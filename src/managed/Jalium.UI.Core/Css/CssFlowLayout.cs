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
    private GroupContext[]? _nextInheritedGroups;
    private GroupContext[] _appliedInheritedGroups = [];

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
    internal double FirstBaseline => _snapshot?.FirstBaseline ?? double.NaN;
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
        var inheritedGroups = _nextInheritedGroups ?? [];
        _nextInheritedGroups = null;
        var insets = Insets(finalSize.Width);
        var containing = ContainingSize(CssBoxMetrics.InnerSize(finalSize, insets));
        var snapshot = Layout(containing, insets);
        _snapshot = snapshot;
        ApplyInheritedGroupAlignment(snapshot, inheritedGroups);
        _appliedInheritedGroups = inheritedGroups;
        var descendantGroups = inheritedGroups;
        var ownMode = (CssTextGroupAlignment)owner.GetValue(CssFlowProperties.TextGroupAlignProperty)!;
        if (ownMode != CssTextGroupAlignment.None && snapshot.GroupPadding > 0)
            descendantGroups = [.. inheritedGroups, new GroupContext(ownMode, snapshot.GroupPadding)];
        foreach (var item in snapshot.Items)
        {
            item.Box.MeasureBorderBox(new Size(item.Width, double.PositiveInfinity), containing, item.Environment);
            if (item.Box.Element is Panel child && CssDisplayLayout.IsFlow(child))
                CssDisplayLayout.FlowFor(child).SetInheritedGroups(
                    CssFloatProperties.IsIndependentBox(child) ? [] : descendantGroups);
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

    private void SetInheritedGroups(GroupContext[] groups)
    {
        _nextInheritedGroups = groups;
        if (!_appliedInheritedGroups.SequenceEqual(groups)) owner.InvalidateArrange();
    }

    private bool NeedsInlineLineCollection()
    {
        FrameworkElement current = owner;
        while (CssDisplayLayout.IsFlow(current))
        {
            if ((CssTextGroupAlignment)current.GetValue(CssFlowProperties.TextGroupAlignProperty)! !=
                CssTextGroupAlignment.None) return true;
            if (CssFloatProperties.IsIndependentBox(current) ||
                current.VisualParent is not FrameworkElement parent || !CssDisplayLayout.IsFlow(parent))
                break;
            current = parent;
        }
        return false;
    }

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
        var cursor = 0d; var width = 0d; var firstBaseline = double.NaN; var baseline = double.NaN;
        var pending = default(CssMarginStrut); var before = default(CssMarginStrut); var after = default(CssMarginStrut);
        List<InlineLine>? inlineLines = NeedsInlineLineCollection() ? [] : null;
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
                var lines = PlaceInline(entries, start, i, containing.Width, cursor,
                    firstFormattedLine: atStart, useTextIndent: true, inlineLines: inlineLines);
                cursor = lines.Bottom;
                if (!double.IsFinite(firstBaseline)) firstBaseline = lines.FirstBaseline + insets.Top;
                baseline = lines.Baseline + insets.Top;
                width = Math.Max(width, lines.Width);
                atStart = false;
                continue;
            }
            var adjoining = pending.Merge(item.Before);
            var y = atStart && topEscapes ? 0 : cursor + adjoining.Value;
            item.Rect = new(item.X, y, item.Width, item.Height);
            NoteBlockBaseline(item, insets.Top, ref firstBaseline, ref baseline);
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
        var group = CompleteInlineGroup(inlineLines, entries);
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
            through, firstBaseline, baseline, sharesParent, version, exclusions, [],
            group.Lines, group.Padding, group.Remaining);
    }

    private static void NoteBlockBaseline(Entry item, double insetTop, ref double first, ref double last)
    {
        if (CssDisplayLayout.TryGetBaseline(item.Box.Element, item.Height, last: false, out var childFirst) &&
            !double.IsFinite(first))
            first = insetTop + item.Rect.Y + childFirst;
        if (CssDisplayLayout.TryGetBaseline(item.Box.Element, item.Height, last: true, out var childLast))
            last = insetTop + item.Rect.Y + childLast;
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

    private (double Bottom, double FirstBaseline, double Baseline, double Width) PlaceInline(
        List<Entry> entries, int start, int end, double available, double y,
        bool firstFormattedLine = false, bool useTextIndent = false, double? indentBasis = null,
        List<InlineLine>? inlineLines = null)
    {
        var metrics = CssFlowProperties.LineMetrics(owner);
        var noWrap = owner.GetValueSourceInternal(TextBlock.TextWrappingProperty).BaseValueSource != BaseValueSource.Default &&
            (TextWrapping)owner.GetValue(TextBlock.TextWrappingProperty)! == TextWrapping.NoWrap;
        var firstBaseline = double.NaN; var lastBaseline = double.NaN; var maximumWidth = 0d;
        var textIndent = useTextIndent
            ? (CssTextIndent)owner.GetValue(CssFlowProperties.TextIndentProperty)!
            : default;
        var linePadding = CssFlowProperties.UsedLinePadding(owner);
        for (var lineStart = start; lineStart < end;)
        {
            var applies = useTextIndent && textIndent.Applies(firstFormattedLine && lineStart == start, false);
            var indent = applies ? textIndent.Resolve(indentBasis ?? available) : 0;
            if (!double.IsFinite(indent)) indent = 0;
            var lineAvailable = available - indent - 2 * linePadding;
            var lineEnd = lineStart; var used = 0d;
            while (lineEnd < end)
            {
                var next = entries[lineEnd].OuterWidth;
                if (!noWrap && lineEnd > lineStart && used + next > lineAvailable + 0.000001) break;
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
                item.Shift = CssFlowProperties.InlineShift(owner, item.Box.Element, alignment,
                    item.Baseline, item.OuterHeight, metrics.Height, metrics.Ascent, metrics.Descent);
                if (alignment.Kind is CssInlineAlignment.Top or CssInlineAlignment.Bottom or CssInlineAlignment.Center)
                    alignedHeight = Math.Max(alignedHeight, item.OuterHeight);
                else
                {
                    ascent = Math.Max(ascent, item.Baseline + item.Shift);
                    descent = Math.Max(descent, item.OuterHeight - item.Baseline - item.Shift);
                }
            }
            var lineHeight = Math.Max(alignedHeight, ascent + descent);
            var free = double.IsFinite(lineAvailable) ? lineAvailable - used : 0;
            var textAlign = CssFlowProperties.LineAlignment(owner, lineEnd == end);
            var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
            var offset = InlineAlignmentOffset(textAlign, rtl, free);
            var x = rtl ? linePadding + offset + used
                : indent + linePadding + offset;
            for (var i = lineStart; i < lineEnd; i++)
            {
                var item = entries[i];
                if (rtl) x -= item.OuterWidth;
                var top = item.Alignment switch
                {
                    CssInlineAlignment.Top => y, CssInlineAlignment.Bottom => y + lineHeight - item.OuterHeight,
                    CssInlineAlignment.Center => y + (lineHeight - item.OuterHeight) / 2,
                    _ => y + ascent - item.Baseline - item.Shift,
                };
                item.Rect = new(x + item.Margin.Left, top + item.Margin.Top, item.Width, item.Height);
                if (!rtl) x += item.OuterWidth;
            }
            inlineLines?.Add(new InlineLine(entries.GetRange(lineStart, lineEnd - lineStart).ToArray(),
                free, textAlign, textAlign));
            maximumWidth = Math.Max(maximumWidth,
                used + Math.Max(0, indent) + 2 * linePadding);
            if (!double.IsFinite(firstBaseline)) firstBaseline = y + ascent;
            lastBaseline = y + ascent;
            y += lineHeight;
            lineStart = lineEnd;
        }
        return (y, firstBaseline, lastBaseline, maximumWidth);
    }

    private static double InlineAlignmentOffset(CssFlowTextAlignment alignment,
        bool rtl, double free) => alignment switch
    {
        CssFlowTextAlignment.Center => free / 2,
        CssFlowTextAlignment.Right => free,
        CssFlowTextAlignment.Start when rtl => free,
        CssFlowTextAlignment.End when !rtl => free,
        _ => 0,
    };

    private (InlineLine[] Lines, double Padding, double Remaining) CompleteInlineGroup(
        List<InlineLine>? lines, List<Entry> entries)
    {
        var shortestRemaining = double.PositiveInfinity;
        if (lines is not null)
            foreach (var line in lines)
                shortestRemaining = Math.Min(shortestRemaining, Math.Max(0, line.Free));
        foreach (var entry in entries)
        {
            if (entry.Box.Element is not Panel child || !CssDisplayLayout.IsFlow(child) ||
                CssFloatProperties.IsIndependentBox(child)) continue;
            var childSnapshot = CssDisplayLayout.FlowFor(child)._snapshot;
            if (childSnapshot is { SharesParent: true })
                shortestRemaining = Math.Min(shortestRemaining, childSnapshot.MinimumRemaining);
        }
        var groupMode = (CssTextGroupAlignment)owner.GetValue(CssFlowProperties.TextGroupAlignProperty)!;
        var groupPadding = groupMode == CssTextGroupAlignment.None || !double.IsFinite(shortestRemaining)
            ? 0 : shortestRemaining;
        var startPadding = CssFlowProperties.GroupStartPadding(owner, groupPadding);
        var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
        if (lines is not null)
        {
            foreach (var line in lines)
            {
                var shift = startPadding +
                    InlineAlignmentOffset(line.Alignment, rtl, line.Free - groupPadding) -
                    InlineAlignmentOffset(line.InitialAlignment, rtl, line.Free);
                if (Math.Abs(shift) <= 0.000001) continue;
                foreach (var entry in line.Items)
                    entry.Rect = new(entry.Rect.X + shift, entry.Rect.Y,
                        entry.Rect.Width, entry.Rect.Height);
            }
        }
        return (lines?.ToArray() ?? [], groupPadding,
            double.IsFinite(shortestRemaining) ? Math.Max(0, shortestRemaining - groupPadding) : double.PositiveInfinity);
    }

    private void ApplyInheritedGroupAlignment(Snapshot snapshot, GroupContext[] groups)
    {
        var rtl = owner.FlowDirection == FlowDirection.RightToLeft;
        var inheritedPadding = groups.Sum(group => group.Padding);
        var startPadding = groups.Sum(group =>
            CssFlowProperties.GroupStartPadding(group.Mode, rtl, group.Padding));
        foreach (var line in snapshot.InlineLines)
        {
            var free = line.Free - snapshot.GroupPadding;
            var targetShift = startPadding +
                InlineAlignmentOffset(line.Alignment, rtl, free - inheritedPadding) -
                InlineAlignmentOffset(line.Alignment, rtl, free);
            var delta = targetShift - line.InheritedShift;
            if (Math.Abs(delta) > 0.000001)
                foreach (var entry in line.Items)
                    entry.Rect = new(entry.Rect.X + delta, entry.Rect.Y,
                        entry.Rect.Width, entry.Rect.Height);
            line.InheritedShift = targetShift;
        }
    }

    private readonly record struct GroupContext(CssTextGroupAlignment Mode, double Padding);

    private sealed class InlineLine(Entry[] items, double free,
        CssFlowTextAlignment alignment, CssFlowTextAlignment initialAlignment)
    {
        internal Entry[] Items { get; } = items;
        internal double Free { get; } = free;
        internal CssFlowTextAlignment Alignment { get; } = alignment;
        internal CssFlowTextAlignment InitialAlignment { get; } = initialAlignment;
        internal double InheritedShift;
    }

    private static double Baseline(Entry item)
    {
        var explicitBaseline = TextBlock.GetBaselineOffset(item.Box.Element);
        if (double.IsFinite(explicitBaseline)) return item.Margin.Top + explicitBaseline;
        if (item.Box.Element is Panel panel && CssDisplayLayout.IsFlow(panel) && !panel.ClipToBounds &&
            double.IsFinite(CssDisplayLayout.FlowFor(panel).LastBaseline))
            return item.Margin.Top + CssDisplayLayout.FlowFor(panel).LastBaseline;
        if (item.Box.Element is Panel gridOrFlex &&
            (gridOrFlex is FlexPanel || gridOrFlex.CssDisplayMode is CssDisplayMode.Flex or CssDisplayMode.Grid) &&
            CssDisplayLayout.TryGetBaseline(gridOrFlex, item.Height, last: false, out var containerBaseline))
            return item.Margin.Top + containerBaseline;
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
        CssMarginStrut After, bool Through, double FirstBaseline, double Baseline, bool SharesParent, long InputVersion,
        CssFloatEnvironment Exclusions, CssFloatRegion[] Floats, InlineLine[] InlineLines,
        double GroupPadding, double MinimumRemaining);

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
