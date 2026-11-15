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
    private readonly FrameworkElement owner;
    private Snapshot? _snapshot;
    private readonly List<Snapshot> _cache = [];
    private FlowDirection? _direction;
    private GroupContext[]? _nextInheritedGroups;
    private GroupContext[] _appliedInheritedGroups = [];

    internal CssFlowLayout(FrameworkElement owner)
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
    internal double LastLineTop => _snapshot is { InlineLines.Length: > 0 } snapshot
        ? snapshot.Insets.Top + snapshot.InlineLines[^1].Top : double.NaN;
    internal bool HasExportedFloats => _snapshot is { SharesParent: true, Floats.Length: > 0 };
    internal CssFloatRegion[] ExportedFloats => HasExportedFloats ? _snapshot!.Floats : [];

    internal Size Measure(Size available)
    {
        var logical = Axes.Logical(available);
        var insets = Insets(available.Width);
        var inner = CssBoxMetrics.InnerSize(logical, insets);
        var containing = ContainingSize(inner);
        var exported = HasExportedFloats;
        _snapshot = Layout(containing, insets);
        if (exported != HasExportedFloats && CssContentsLayout.BoxParent(owner) is Panel parent) parent.InvalidateCssFlowOrder();
        owner.MeasureCssLayoutAbsoluteChildren(available);
        return Axes.Physical(new Size(_snapshot.Desired.Width + insets.Left + insets.Right, _snapshot.Desired.Height + insets.Top + insets.Bottom));
    }

    internal Size Arrange(Size finalSize)
    {
        var inheritedGroups = _nextInheritedGroups ?? [];
        _nextInheritedGroups = null;
        var logical = Axes.Logical(finalSize);
        var insets = Insets(finalSize.Width);
        var containing = ContainingSize(CssBoxMetrics.InnerSize(logical, insets));
        var physicalContent = CssBoxMetrics.InnerSize(finalSize, Axes.Physical(insets));
        var snapshot = Layout(containing, insets);
        _snapshot = snapshot;
        ApplyInheritedGroupAlignment(snapshot, inheritedGroups);
        _appliedInheritedGroups = inheritedGroups;
        var descendantGroups = inheritedGroups;
        var ownMode = (CssTextGroupAlignment)owner.GetValue(CssFlowProperties.TextGroupAlignProperty)!;
        if (ownMode != CssTextGroupAlignment.None && snapshot.GroupPadding > 0)
            descendantGroups = [.. inheritedGroups, new GroupContext(ownMode, snapshot.GroupPadding)];
        var origin = owner is CssTableAnonymousBox anonymous ? anonymous.AllocationOrigin : default;
        var trackOrigin = CssTableLayout.ContentOrigin(owner);
        origin = new(origin.X + trackOrigin.X, origin.Y + trackOrigin.Y);
        var allocationInsets = new Thickness(insets.Left + origin.X, insets.Top + origin.Y, insets.Right, insets.Bottom);
        var physicalInsets = Axes.Physical(allocationInsets);
        foreach (var item in snapshot.Items)
        {
            if (item.InlineParagraph is { } paragraph)
            {
                if (ReferenceEquals(item.Box.Element, paragraph.FirstElement)) paragraph.Layout.ActivateAndArrange(allocationInsets,
                    child => { if (CssDisplayLayout.IsFlow(child)) FlowForInlineChild(child, descendantGroups); }, physicalSize: finalSize);
                continue;
            }
            var itemContaining = item.ContainingOverride ?? containing;
            MeasureFlowBox(item.Box, new Size(item.Width, double.PositiveInfinity), itemContaining, item.Environment);
            if (item.Box.Element is FrameworkElement child && CssDisplayLayout.IsFlow(child))
                CssDisplayLayout.FlowFor(child).SetInheritedGroups(
                    CssFloatProperties.IsIndependentBox(child) ? [] : descendantGroups);
            var slot = Axes.Physical(item.Rect, physicalContent);
            slot.Offset(physicalInsets.Left, physicalInsets.Top);
            item.Box.ArrangeBorderBox(slot, Axes.Physical(itemContaining),
                floats: item.Environment, containingWritingMode: Axes.Mode);
        }
        owner.ArrangeCssLayoutAbsoluteChildren(finalSize);
        return finalSize;
    }

    private Thickness Insets(double fallbackWidth) => Axes.Logical(CssTableLayout.ContentInsets(owner,
        CssBoxMetrics.ContentInsets(owner, CssWritingAxes.PercentageBasis(owner, fallbackWidth))));

    private Size ContainingSize(Size available)
    {
        if (CssTableLayout.TryCellContainingHeight(owner, out var cellHeight))
            return Axes.Vertical ? new(cellHeight, available.Height) : new(available.Width, cellHeight);
        if (CssMultiColumnLayout.TryColumnContainingHeight(owner, out var columnHeight))
            return Axes.Vertical ? new(columnHeight, available.Height) : new(available.Width, columnHeight);
        var parent = Axes.Logical(owner.CssParentBox?.ContainingBlock ?? owner.PreviousAvailableSize);
        var definite = Axes.Vertical ? owner.CssHasDefiniteWidth(parent.Height) : owner.CssHasDefiniteHeight(parent.Height);
        return new(available.Width, definite && !HasIntrinsicBlockSize ? available.Height : double.PositiveInfinity);
    }

    private bool SharesParentFlow => SharesParent(owner) && owner.CssParentBox?.WritingMode == Axes.Mode;

    private void SetInheritedGroups(GroupContext[] groups)
    {
        _nextInheritedGroups = groups;
        if (!_appliedInheritedGroups.SequenceEqual(groups)) owner.InvalidateArrange();
    }

    private bool NeedsInlineLineCollection()
    {
        if (CssInitialLetterProperties.Metrics(owner) is not null) return true;
        FrameworkElement current = owner;
        while (CssDisplayLayout.IsFlow(current))
        {
            if (CssLineClampProperties.Active(current)) return true;
            if ((CssTextGroupAlignment)current.GetValue(CssFlowProperties.TextGroupAlignProperty)! !=
                CssTextGroupAlignment.None) return true;
            if (CssFloatProperties.IsIndependentBox(current) ||
                CssContentsLayout.BoxParent(current) is not FrameworkElement parent || !CssDisplayLayout.IsFlow(parent))
                break;
            current = parent;
        }
        return false;
    }

    private Snapshot Layout(Size containing, Thickness insets)
    {
        var direction = CssWritingAxes.UsedDirection(owner);
        if (_direction != direction || _writingMode != Axes.Mode)
        {
            _direction = direction; _writingMode = Axes.Mode; _cache.Clear();
            // Inherited dependency values can change without advancing every
            // descendant's local input version. Reflow its allocation as well.
            foreach (var child in CssTableLayout.FlowChildren(owner)) child.Element.InvalidateCssAllocation();
        }
        var version = InputVersion(owner);
        var sharesParent = SharesParentFlow;
        var exclusions = sharesParent ? owner.CssParentBox?.Floats ?? CssFloatEnvironment.Empty : CssFloatEnvironment.Empty;
        foreach (var cached in _cache)
            if (cached.InputVersion == version && cached.ClampStamp == CssLineClampLayout.PassStamp && cached.Intrinsic == owner.CssIntrinsicMeasure &&
                cached.Containing == containing && cached.Insets == insets &&
                cached.SharesParent == sharesParent && cached.Exclusions.Equals(exclusions))
                return cached;
        _cache.RemoveAll(cached => cached.InputVersion != version);
        var snapshot = Compute(containing, insets, sharesParent, version, exclusions) with { ClampStamp = CssLineClampLayout.PassStamp };
        if (_cache.Count == 8) _cache.RemoveAt(0);
        _cache.Add(snapshot);
        return snapshot;
    }

    private Snapshot Compute(Size containing, Thickness insets, bool sharesParent, long version, CssFloatEnvironment exclusions)
    {
        var entries = new List<Entry>();
        foreach (var box in CssTableLayout.FlowChildren(owner))
        {
            if (CssLineClampLayout.Excluded(box.Element)) continue;
            TextBlock.CssTextFlowLayout.ReleaseInlineTree(box.Element, owner);
            entries.Add(Measure(box, containing));
        }
        if (exclusions.Regions.Length > 0 || entries.Any(item => item.Float != CssFloatSide.None || item.Clear != CssClearSide.None || TextBlock.CssTextFlowLayout.HasNestedFloats(item.Box.Element) || TextBlock.CssTextFlowLayout.HasNestedBlocks(item.Box.Element) ||
            TextBlock.CssTextFlowLayout.HasNestedInitials(item.Box.Element) ||
            item.Box.Element is FrameworkElement panel && CssDisplayLayout.IsFlow(panel) && ExportedRegions(panel).Length > 0))
            return ComputeFloats(entries, containing, insets, sharesParent, version, exclusions);
        var height = PreferredBlockSize;
        var autoHeight = double.IsNaN(height) && !HasIntrinsicBlockSize;
        var bounds = BlockBounds(containing.Height);
        var minHeight = bounds.Min;
        var maxHeight = bounds.Max;
        var topEscapes = sharesParent && insets.Top == 0;
        var canBottomEscape = sharesParent && insets.Bottom == 0 && autoHeight;
        var canThrough = owner.CssDisplayMode == CssDisplayMode.Block && !owner.CssInlineOuter && !owner.ClipToBounds &&
            insets.Top == 0 && insets.Bottom == 0 && minHeight == 0 && (autoHeight || height == 0);
        var cursor = 0d; var width = 0d; var firstBaseline = double.NaN; var baseline = double.NaN;
        var pending = default(CssMarginStrut); var before = default(CssMarginStrut); var after = default(CssMarginStrut);
        List<InlineLine>? inlineLines = NeedsInlineLineCollection() || entries.Any(static item =>
            TextBlock.CssTextFlowLayout.IsFragmentable(item.Box.Element)) ? [] : null;
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
        else if (canBottomEscape)
        {
            var innerMinHeight = Math.Max(0, minHeight - insets.Top - insets.Bottom);
            var innerMaxHeight = Math.Max(innerMinHeight, maxHeight - insets.Top - insets.Bottom);
            if (cursor >= innerMinHeight && cursor <= innerMaxHeight) after = pending;
            else cursor = Math.Clamp(cursor, innerMinHeight, innerMaxHeight);
        }
        else cursor += pending.Value;
        if (!owner.CssIntrinsicMeasure && !owner.CssInlineOuter && CssFloatProperties.ActiveSide(owner) == CssFloatSide.None && double.IsFinite(containing.Width) &&
            !LocalInlineAlignment(owner))
            width = containing.Width;
        return new(containing, insets, new Size(Math.Max(0, width), Math.Max(0, cursor)), entries.ToArray(), before, after,
            through, firstBaseline, baseline, sharesParent, version, exclusions, [],
            group.Lines, group.Padding, group.Remaining, owner.CssIntrinsicMeasure);
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
        Size? containingOverride = null;
        if (element is not null && CssMultiColumnLayout.SpannerWidth(element) is { } spanningWidth)
        { containingOverride = containing = new(spanningWidth, containing.Height); widthLimit = null; }
        var side = CssFloatProperties.ActiveSide(box.Element);
        var inline = side == CssFloatSide.None && (element?.CssInlineOuter == true || box.Element is ICssAnonymousTextBox { Owner: TextBlock });
        var margin = element is null ? default : inline ? Axes.LineLogical(CssBoxMetrics.Margin(element, containing.Width))
            : Axes.Logical(CssBoxMetrics.Margin(element, containing.Width));
        if (inline && TextBlock.CssTextFlowLayout.IsFragmentable(box.Element))
        {
            return new(box, margin, 0, 0, true, default, default, false, margin.Left, side, CssFloatProperties.ClearSide(box.Element))
                { Environment = environment, ContainingOverride = containingOverride };
        }
        var availableWidth = widthLimit ?? Math.Max(0, containing.Width - margin.Left - margin.Right);
        var explicitWidth = element is null ? double.NaN : PreferredInlineSize(element, containing.Width);
        double width;
        if ((inline || side != CssFloatSide.None) && double.IsNaN(explicitWidth))
        {
            MeasureFlowIntrinsic(box, containing, environment);
            var min = box.MinContent.Width;
            var max = box.MaxContent.Width;
            width = Math.Min(Math.Max(min, availableWidth), Math.Max(min, max));
        }
        else
        {
            MeasureFlowBox(box, new Size(availableWidth, double.PositiveInfinity), containing, environment);
            var localAlignment = element is not null && LocalInlineAlignment(element);
            width = owner.CssIntrinsicMeasure || !double.IsNaN(explicitWidth) || inline || localAlignment || element?.CssDisplayMode == CssDisplayMode.Table || CssReplacedContent.HasReplacement(element) || !double.IsFinite(availableWidth)
                ? Desired(box).Width : availableWidth;
        }
        if (element is not null)
        {
            var limits = InlineBounds(element, containing.Width);
            width = Math.Clamp(width, limits.Min, limits.Max);
        }
        MeasureFlowBox(box, new Size(Math.Max(0, width), double.PositiveInfinity), containing, environment);
        if (element?.CssDisplayMode == CssDisplayMode.Table)
            width = Math.Max(width, Desired(box).Width);
        var height = Desired(box).Height;
        var before = CssMarginStrut.From(margin.Top); var after = CssMarginStrut.From(margin.Bottom);
        var through = false;
        if (!inline && side == CssFloatSide.None && element is FrameworkElement panel && CssDisplayLayout.IsFlow(panel))
        {
            var flow = CssDisplayLayout.FlowFor(panel);
            before = before.Merge(flow.EscapedBefore); after = after.Merge(flow.EscapedAfter);
            through = flow.CollapsesThrough && height == 0;
        }
        var x = margin.Left;
        var autoMargin = false;
        if (!inline && side == CssFloatSide.None && element is not null && !element.HasLocalOrAnimatedValue(FrameworkElement.MarginProperty) && element.CssLayout is { HasMargin: true } layout)
        {
            var leftAuto = Axes.Mode == CssWritingMode.SidewaysLr ? layout.MarginBottom.IsAuto : Axes.Vertical ? layout.MarginTop.IsAuto : layout.MarginLeft.IsAuto;
            var rightAuto = Axes.Mode == CssWritingMode.SidewaysLr ? layout.MarginTop.IsAuto : Axes.Vertical ? layout.MarginBottom.IsAuto : layout.MarginRight.IsAuto;
            autoMargin = leftAuto || rightAuto;
            var remaining = containing.Width - width - margin.Left - margin.Right;
            var free = Math.Max(0, remaining);
            if (leftAuto) x += rightAuto ? free / 2 : free;
            if (autoMargin && remaining < 0 && CssWritingAxes.UsedDirection(owner) == FlowDirection.RightToLeft) x = containing.Width - margin.Right - width;
        }
        if (!inline && CssWritingAxes.UsedDirection(owner) == FlowDirection.RightToLeft && !autoMargin)
            x = containing.Width - margin.Right - width;
        return new(box, margin, Math.Max(0, width), height, inline, before, after, through, x, side, CssFloatProperties.ClearSide(box.Element))
            { Environment = environment, ContainingOverride = containingOverride };
    }

    private (double Bottom, double FirstBaseline, double Baseline, double Width) PlaceInline(
        List<Entry> entries, int start, int end, double available, double y,
        bool firstFormattedLine = false, bool useTextIndent = false, double? indentBasis = null,
        List<InlineLine>? inlineLines = null)
    {
        if (inlineLines is not null && (CssLineClampLayout.Participates(owner) ||
            entries.GetRange(start, end - start).Any(static entry => TextBlock.CssTextFlowLayout.IsFragmentable(entry.Box.Element))))
        {
            return FormatSharedRun(entries.GetRange(start, end - start), available, y,
                firstFormattedLine, useTextIndent, indentBasis, inlineLines);
        }
        var metrics = FlowLineMetrics();
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
            var rtl = CssWritingAxes.UsedDirection(owner) == FlowDirection.RightToLeft;
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
                item.Rect = Axes.LinePlacement(item.Rect, y, lineHeight);
                if (!rtl) x += item.OuterWidth;
            }
            inlineLines?.Add(new InlineLine(entries.GetRange(lineStart, lineEnd - lineStart).ToArray(),
                free, textAlign, textAlign) { Top = y, Height = lineHeight });
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

    private (double Bottom, double FirstBaseline, double Baseline, double Width) FormatSharedRun(
        List<Entry> items, double available, double y, bool firstFormattedLine, bool useTextIndent,
        double? indentBasis, List<InlineLine> inlineLines, CssFloatSpace? space = null, CssInlineSplitOptions? split = null)
    {
        var layout = new TextBlock.CssTextFlowLayout(owner);
        var inline = items.Where(static item => item.Float == CssFloatSide.None).ToArray();
        var paragraph = new InlineParagraph(layout, inline[0].Box.Element);
        var lookup = new Dictionary<UIElement, Entry>(ReferenceEqualityComparer.Instance);
        foreach (var item in items) lookup.Add(item.Box.Element, item);
        foreach (var item in inline) item.InlineParagraph = paragraph;
        if (split is { } options && space is not null)
            layout.SetBlockFlow(options, (element, cursor, pending, topEscapes) =>
                PlaceSplitBlock(element, options.ContainingBlock, space, cursor, pending, topEscapes));
        var result = layout.FormatRun(items.Select(static item => item.Box.Element).ToArray(),
            new Size(available, double.PositiveInfinity), y, firstFormattedLine, useTextIndent, indentBasis,
            (element, rect) => { if (lookup.TryGetValue(element, out var entry)) entry.Rect = rect; }, space,
            space is null ? null : (element, lineY, occupied, height) =>
            {
                if (!lookup.TryGetValue(element, out var entry))
                {
                    entry = Measure(new CssLayoutBox(element, lookup.Count), new(available, double.PositiveInfinity));
                    lookup.Add(element, entry);
                }
                PlaceFloat(entry, available, lineY, space, occupied, height);
                return entry.Rect;
            });
        if (space is not null && layout.InitialExclusion is { } initial) space.Add(initial);
        foreach (var line in layout.Lines)
            inlineLines.Add(new InlineLine([], line.Free, line.Alignment, line.Alignment, line.Shift)
                { Top = line.Top, Height = line.Height });
        return result;
    }

    private (InlineLine[] Lines, double Padding, double Remaining) CompleteInlineGroup(
        List<InlineLine>? lines, List<Entry> entries)
    {
        var shortestRemaining = double.PositiveInfinity;
        if (lines is not null)
            foreach (var line in lines)
                shortestRemaining = Math.Min(shortestRemaining, Math.Max(0, line.Free));
        foreach (var entry in entries)
        {
            if (entry.Box.Element is not FrameworkElement child || !CssDisplayLayout.IsFlow(child) ||
                CssFloatProperties.IsIndependentBox(child)) continue;
            var childSnapshot = CssDisplayLayout.FlowFor(child)._snapshot;
            if (childSnapshot is { SharesParent: true })
                shortestRemaining = Math.Min(shortestRemaining, childSnapshot.MinimumRemaining);
        }
        foreach (var layout in entries.Select(static entry => entry.InlineParagraph?.Layout).OfType<TextBlock.CssTextFlowLayout>().Distinct())
            foreach (var child in layout.BlockElements)
                if (CssDisplayLayout.IsFlow(child) && !CssFloatProperties.IsIndependentBox(child) &&
                    CssDisplayLayout.FlowFor(child)._snapshot is { SharesParent: true } nested)
                    shortestRemaining = Math.Min(shortestRemaining, nested.MinimumRemaining);
        var groupMode = (CssTextGroupAlignment)owner.GetValue(CssFlowProperties.TextGroupAlignProperty)!;
        var groupPadding = groupMode == CssTextGroupAlignment.None || !double.IsFinite(shortestRemaining)
            ? 0 : shortestRemaining;
        var startPadding = CssFlowProperties.GroupStartPadding(owner, groupPadding);
        var rtl = CssWritingAxes.UsedDirection(owner) == FlowDirection.RightToLeft;
        if (lines is not null)
        {
            foreach (var line in lines)
            {
                var shift = startPadding +
                    InlineAlignmentOffset(line.Alignment, rtl, line.Free - groupPadding) -
                    InlineAlignmentOffset(line.InitialAlignment, rtl, line.Free);
                if (Math.Abs(shift) <= 0.000001) continue;
                line.Shift(shift);
            }
        }
        return (lines?.ToArray() ?? [], groupPadding,
            double.IsFinite(shortestRemaining) ? Math.Max(0, shortestRemaining - groupPadding) : double.PositiveInfinity);
    }

    private void ApplyInheritedGroupAlignment(Snapshot snapshot, GroupContext[] groups)
    {
        var rtl = CssWritingAxes.UsedDirection(owner) == FlowDirection.RightToLeft;
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
                line.Shift(delta);
            line.InheritedShift = targetShift;
        }
    }

    private readonly record struct GroupContext(CssTextGroupAlignment Mode, double Padding);

    private sealed class InlineLine(Entry[] items, double free,
        CssFlowTextAlignment alignment, CssFlowTextAlignment initialAlignment, Action<double>? fragmentShift = null)
    {
        internal Entry[] Items { get; } = items;
        internal double Free { get; } = free;
        internal CssFlowTextAlignment Alignment { get; } = alignment;
        internal CssFlowTextAlignment InitialAlignment { get; } = initialAlignment;
        internal double InheritedShift;
        internal double Top, Height;
        internal void Shift(double delta)
        {
            if (fragmentShift is not null) fragmentShift(delta);
            else foreach (var entry in Items)
                entry.Rect = new(entry.Rect.X + delta, entry.Rect.Y, entry.Rect.Width, entry.Rect.Height);
        }
    }

    private double Baseline(Entry item)
    {
        var explicitBaseline = TextBlock.GetBaselineOffset(item.Box.Element);
        if (double.IsFinite(explicitBaseline)) return item.Margin.Top + explicitBaseline;
        if (Axes.Vertical && item.Box.Element is not TextBlock &&
            Axes.Mode is CssWritingMode.VerticalRl or CssWritingMode.VerticalLr &&
            CssWritingModeProperties.GetTextOrientation(owner) != CssTextOrientation.Sideways)
            return item.Margin.Top + item.Height / 2;
        if (item.Box.Element is ICssAnonymousBox &&
            CssDisplayLayout.TryGetBaseline(item.Box.Element, item.Height, last: true, out var textBaseline))
            return item.Margin.Top + textBaseline;
        if (item.Box.Element is FrameworkElement panel && CssDisplayLayout.IsFlow(panel) && !panel.ClipToBounds &&
            CssDisplayLayout.TryGetBaseline(panel, item.Height, last: true, out var flowBaseline))
            return item.Margin.Top + flowBaseline;
        if (item.Box.Element is FrameworkElement gridOrFlex &&
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
        if (visual is CssTableAnonymousBox anonymous)
        {
            foreach (var item in anonymous.Items) result = Math.Max(result, InputVersion(item));
            return result;
        }
        for (var i = 0; i < visual.InternalVisualChildrenCount; i++)
            if (visual.InternalGetVisualChild(i) is { } child) result = Math.Max(result, InputVersion(child));
        return result;
    }

    private sealed record Snapshot(Size Containing, Thickness Insets, Size Desired, Entry[] Items, CssMarginStrut Before,
        CssMarginStrut After, bool Through, double FirstBaseline, double Baseline, bool SharesParent, long InputVersion,
        CssFloatEnvironment Exclusions, CssFloatRegion[] Floats, InlineLine[] InlineLines,
        double GroupPadding, double MinimumRemaining, bool Intrinsic, int ClampStamp = 0);

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
        public Size? ContainingOverride;
        public bool HasClearance;
        public double OuterWidth => Width + Margin.Left + Margin.Right;
        public double OuterHeight => Height + Margin.Top + Margin.Bottom;
        public Rect Rect;
        public double Baseline, Shift;
        public CssInlineAlignment Alignment;
        public InlineParagraph? InlineParagraph;
    }
    private sealed record InlineParagraph(TextBlock.CssTextFlowLayout Layout, UIElement FirstElement);
}
