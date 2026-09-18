using Jalium.UI.Controls;

namespace Jalium.UI.Styling;

internal sealed partial class CssFlowLayout
{
    private Snapshot ComputeFloats(List<Entry> entries, Size containing, Thickness insets, bool sharesParent,
        long version, CssFloatEnvironment exclusions)
    {
        var space = new CssFloatSpace(exclusions.Offset(-insets.Left, -insets.Top));
        var height = owner.CssPreferredHeight(owner.CssParentBox?.ContainingBlock.Height ?? owner.PreviousAvailableSize.Height);
        var minHeight = owner.CssHeightBounds(containing.Height).Min;
        var topEscapes = sharesParent && insets.Top == 0;
        var bottomEscapes = sharesParent && insets.Bottom == 0 && double.IsNaN(height) && minHeight == 0;
        var canThrough = sharesParent && insets.Top == 0 && insets.Bottom == 0 && minHeight == 0 &&
            (double.IsNaN(height) || height == 0);
        var cursor = 0d; var width = 0d; var baseline = double.NaN;
        var pending = default(CssMarginStrut); var before = default(CssMarginStrut); var after = default(CssMarginStrut);
        var atStart = true; var lastClearEmpty = false;
        for (var i = 0; i < entries.Count;)
        {
            var item = entries[i];
            if (item.Inline || item.Float != CssFloatSide.None)
            {
                var start = i;
                while (i < entries.Count && (entries[i].Inline || entries[i].Float != CssFloatSide.None)) i++;
                var lineY = atStart && topEscapes ? cursor : cursor + pending.Value;
                var run = PlaceInlineAndFloats(entries, start, i, containing.Width, lineY, space);
                width = Math.Max(width, run.Width);
                if (run.HasInline)
                {
                    if (atStart && topEscapes) before = before.Merge(pending);
                    cursor = run.Bottom; baseline = run.Baseline + insets.Top;
                    pending = default; atStart = false; lastClearEmpty = false;
                }
                continue;
            }

            item = PlaceBlockBesideFloats(item, containing, space, cursor, pending, atStart && topEscapes);
            entries[i] = item;
            var adjoining = pending.Merge(item.Before);
            width = Math.Max(width, item.Width + item.Margin.Left + item.Margin.Right);
            space.NoteSourceTop(item.Rect.Y - item.Margin.Top);
            if (item.Box.Element is Panel panel && CssDisplayLayout.IsFlow(panel) && !CssFloatProperties.IsIndependentBox(panel))
                space.AddRange(CssDisplayLayout.FlowFor(panel).ExportedFloats.Select(region => region.Offset(item.Rect.X, item.Rect.Y)));
            if (item.Through && !item.HasClearance)
                pending = adjoining.Merge(item.After);
            else
            {
                if (atStart && topEscapes && !item.HasClearance) before = before.Merge(adjoining);
                cursor = item.Rect.Y + item.Height;
                pending = item.After; atStart = false;
                lastClearEmpty = item.HasClearance && item.Height == 0;
            }
            i++;
        }
        var through = canThrough && atStart;
        if (atStart && topEscapes)
        {
            before = before.Merge(pending);
            if (through) after = before;
        }
        else if (bottomEscapes && !lastClearEmpty) after = pending;
        else cursor += pending.Value;
        if (!sharesParent) { cursor = Math.Max(cursor, space.OwnBottom); through = false; }
        foreach (var region in space.OwnRegions) width = Math.Max(width, Math.Max(region.Width, region.Right));
        if (!owner.CssInlineOuter && CssFloatProperties.ActiveSide(owner) == CssFloatSide.None && double.IsFinite(containing.Width) &&
            (!owner.HasLocalOrAnimatedValue(FrameworkElement.HorizontalAlignmentProperty) || owner.HorizontalAlignment == HorizontalAlignment.Stretch))
            width = containing.Width;
        return new(containing, insets, new Size(Math.Max(0, width), Math.Max(0, cursor)), entries.ToArray(), before, after,
            through, baseline, sharesParent, version, exclusions,
            space.OwnRegions.Select(region => region.Offset(insets.Left, insets.Top)).ToArray());
    }

    private Entry PlaceBlockBesideFloats(Entry item, Size containing, CssFloatSpace space, double cursor,
        CssMarginStrut pending, bool topEscapes)
    {
        var independent = CssFloatProperties.IsIndependentBox(item.Box.Element);
        double? previousY = null;
        for (var pass = 0; pass < 8; pass++)
        {
            var adjoining = pending.Merge(item.Before);
            var hypothetical = topEscapes ? 0 : cursor + adjoining.Value;
            var clearBottom = space.ClearBottom(item.Clear);
            var clearance = clearBottom > hypothetical + 0.000001;
            var y = clearance ? Math.Max(cursor + adjoining.Value, clearBottom) : hypothetical;
            var x = item.X;
            if (independent)
            {
                while (true)
                {
                    var band = space.Band(containing.Width, y, item.Height);
                    var left = Math.Max(item.Margin.Left, band.Left);
                    var right = Math.Min(containing.Width - item.Margin.Right, band.Right);
                    var available = Math.Max(0, right - left);
                    var measured = Measure(item.Box, containing, widthLimit: available);
                    if (measured.Width <= available + 0.000001 || !band.Obstructed)
                    {
                        item = measured;
                        x = owner.FlowDirection == FlowDirection.RightToLeft ? right - item.Width : left;
                        // Preserve horizontal auto-margin positioning when the full containing block is available.
                        if (!band.Obstructed) x = item.X;
                        break;
                    }
                    var next = space.NextBottom(y);
                    if (!double.IsFinite(next)) break;
                    y = next;
                }
            }
            else
            {
                var environment = space.At(x, y);
                var measured = Measure(item.Box, containing, environment);
                var nextAdjoining = pending.Merge(measured.Before);
                var nextHypothetical = topEscapes ? 0 : cursor + nextAdjoining.Value;
                var nextClearance = clearBottom > nextHypothetical + 0.000001;
                var nextY = nextClearance ? Math.Max(cursor + nextAdjoining.Value, clearBottom) : nextHypothetical;
                item = measured;
                if (Math.Abs(nextY - y) > 0.000001 && previousY != nextY)
                { previousY = y; continue; }
            }
            item.HasClearance = clearance;
            item.Rect = new Rect(x, y, item.Width, item.Height);
            return item;
        }
        var fallback = cursor + pending.Merge(item.Before).Value;
        item.Rect = new(item.X, Math.Max(fallback, space.ClearBottom(item.Clear)), item.Width, item.Height);
        return item;
    }

    private void PlaceFloat(Entry item, double containingWidth, double lineY, CssFloatSpace space,
        double occupiedInlineWidth = 0, double lineHeight = 0)
    {
        var y = Math.Max(0, Math.Max(lineY, Math.Max(space.LastTop, space.ClearBottom(item.Clear))));
        while (true)
        {
            var band = space.Band(containingWidth, y, item.OuterHeight);
            var reserve = Math.Abs(y - lineY) < 0.000001 ? occupiedInlineWidth : 0;
            if (item.OuterWidth + reserve <= band.Right - band.Left + 0.000001 || !band.Obstructed && reserve == 0)
            {
                var x = item.Float == CssFloatSide.Right && double.IsFinite(band.Right) ? band.Right - item.OuterWidth : band.Left;
                item.Rect = new(x + item.Margin.Left, y + item.Margin.Top, item.Width, item.Height);
                space.Add(new(item.Float, x, y, item.OuterWidth, item.OuterHeight));
                return;
            }
            var next = space.NextBottom(y);
            if (reserve != 0 && lineHeight > 0) next = Math.Min(next, lineY + lineHeight);
            if (!double.IsFinite(next) || next <= y)
            {
                var x = item.Float == CssFloatSide.Right && double.IsFinite(containingWidth) ? containingWidth - item.OuterWidth : 0;
                item.Rect = new(x + item.Margin.Left, y + item.Margin.Top, item.Width, item.Height);
                space.Add(new(item.Float, x, y, item.OuterWidth, item.OuterHeight));
                return;
            }
            y = next;
        }
    }

    private (double Bottom, double Baseline, double Width, bool HasInline) PlaceInlineAndFloats(
        List<Entry> entries, int start, int end, double available, double y, CssFloatSpace space)
    {
        var noWrap = owner.GetValueSourceInternal(TextBlock.TextWrappingProperty).BaseValueSource != BaseValueSource.Default &&
            (TextWrapping)owner.GetValue(TextBlock.TextWrappingProperty)! == TextWrapping.NoWrap;
        var buffer = new List<Entry>();
        var baseline = double.NaN; var maximumWidth = 0d; var hadInline = false;
        void Flush()
        {
            if (buffer.Count == 0) return;
            var metrics = InlineMetrics(buffer);
            var band = space.Band(available, y, metrics.Height);
            var result = PlaceInline(buffer, 0, buffer.Count, Math.Max(0, band.Right - band.Left), y);
            foreach (var entry in buffer) entry.Rect = new(entry.Rect.X + band.Left, entry.Rect.Y, entry.Rect.Width, entry.Rect.Height);
            space.NoteSourceTop(y);
            y = result.Bottom; baseline = result.Baseline;
            maximumWidth = Math.Max(maximumWidth, result.Width + band.Left);
            buffer.Clear();
        }
        for (var i = start; i < end; i++)
        {
            var item = entries[i];
            if (item.Float != CssFloatSide.None)
            {
                PlaceFloat(item, available, y, space, buffer.Sum(entry => entry.OuterWidth), buffer.Count == 0 ? 0 : InlineMetrics(buffer).Height);
                maximumWidth = Math.Max(maximumWidth, Math.Max(item.OuterWidth, item.Rect.Right + item.Margin.Right));
                continue;
            }
            hadInline = true;
            while (true)
            {
                buffer.Add(item);
                var metrics = InlineMetrics(buffer);
                var used = buffer.Sum(entry => entry.OuterWidth);
                var band = space.Band(available, y, metrics.Height);
                if (used <= band.Right - band.Left + 0.000001 || noWrap && buffer.Count > 1 || !band.Obstructed && buffer.Count == 1) break;
                buffer.RemoveAt(buffer.Count - 1);
                if (buffer.Count > 0) { Flush(); continue; }
                var next = space.NextBottom(y);
                if (!double.IsFinite(next)) { buffer.Add(item); break; }
                y = next;
            }
        }
        Flush();
        return (y, baseline, maximumWidth, hadInline);
    }

    private (double Height, double Ascent) InlineMetrics(List<Entry> entries)
    {
        var parent = CssFlowProperties.LineMetrics(owner);
        var ascent = parent.Baseline; var descent = parent.Height - parent.Baseline; var aligned = 0d;
        foreach (var item in entries)
        {
            var baseline = Baseline(item);
            var alignment = (CssInlineVerticalAlign)item.Box.Element.GetValue(CssFlowProperties.VerticalAlignProperty)!;
            var shift = alignment.Kind switch
            {
                CssInlineAlignment.Offset => alignment.Offset.Resolve(item.Box.Element is FrameworkElement element
                    ? CssFlowProperties.LineMetrics(element).Height : parent.Height, 0),
                CssInlineAlignment.TextTop => parent.Ascent - baseline,
                CssInlineAlignment.TextBottom => item.OuterHeight - parent.Descent - baseline, _ => 0,
            };
            if (alignment.Kind is CssInlineAlignment.Top or CssInlineAlignment.Bottom) aligned = Math.Max(aligned, item.OuterHeight);
            else { ascent = Math.Max(ascent, baseline + shift); descent = Math.Max(descent, item.OuterHeight - baseline - shift); }
        }
        return (Math.Max(aligned, ascent + descent), ascent);
    }
}
