using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Documents;

/// <summary>Chooses page boundaries over already shaped lines without copying document content.</summary>
internal static class FlowDocumentPagination
{
    internal readonly record struct Fragment(
        int FirstLine,
        int EndLine,
        double Top,
        double Bottom,
        bool Blank = false,
        string? PageName = null,
        double RepeatedHeaderHeight = 0,
        double RepeatedFooterHeight = 0,
        double RepeatTableTop = double.NaN);
    private struct Boundary
    {
        internal bool Forced, Avoid, StartsBox;
        internal CssFragmentBreak Side;
        internal int SideOrder;
        internal double MarginTop, BorderTop;
    }
    private readonly record struct ParagraphRule(int First, int End, int Orphans, int Widows, int[]? LineBands);

    internal static IReadOnlyList<Fragment> Build(FlowDocumentPresentation layout, double height, FlowDirection direction)
    {
        var lines = layout.Lines;
        var count = lines.Count;
        height = double.IsFinite(height) ? Math.Max(1, height) : 1056;
        var rootPageName = layout.Document is { } doc ? CssPageProperties.Page(doc) : null;
        if (string.IsNullOrEmpty(rootPageName)) rootPageName = null;
        if (count == 0) return [new(0, 0, 0, height, PageName: rootPageName)];
        var boundaries = new Boundary[count + 1];
        var avoidChanges = new int[count + 2];
        var paragraphRules = new List<ParagraphRule>?[count + 1];
        var linePageNames = new string?[count];
        for (var i = 0; i < count; i++) linePageNames[i] = rootPageName;

        void Apply(int index, CssFragmentBreak value, int order)
        {
            ref var boundary = ref boundaries[index];
            boundary.Avoid |= CssFragmentationProperties.AvoidsPage(value);
            boundary.Forced |= CssFragmentationProperties.ForcesPage(value);
            if (value is CssFragmentBreak.Left or CssFragmentBreak.Right or CssFragmentBreak.Recto or CssFragmentBreak.Verso &&
                order >= boundary.SideOrder)
            { boundary.Side = value; boundary.SideOrder = order; }
        }

        var repeatableTables = layout.RepeatableTables;
        var order = 0;
        foreach (var box in layout.FragmentBoxes)
        {
            var first = box.FirstLine;
            var end = box.EndLine;
            if (box.AnonymousOwner is null)
            {
                Apply(first, CssFragmentationProperties.Before(box.Source), order++);
                Apply(end, CssFragmentationProperties.After(box.Source), order++);
                var boxPage = CssPageProperties.Page(box.Source);
                if (!string.IsNullOrEmpty(boxPage))
                {
                    for (var i = Math.Max(0, first); i < Math.Min(count, end); i++)
                        linePageNames[i] = boxPage;
                }
            }
            if (!boundaries[first].StartsBox)
            {
                boundaries[first].StartsBox = true;
                boundaries[first].MarginTop = box.MarginTop;
                boundaries[first].BorderTop = box.BorderTop;
            }
            var avoidInside = box.AnonymousOwner is null && CssFragmentationProperties.AvoidsPage(CssFragmentationProperties.Inside(box.Source));
            if (repeatableTables.Count > 0)
            {
                if (box.Source is TableRowGroup trg && repeatableTables.Any(t => ReferenceEquals(t.Header, trg) || ReferenceEquals(t.Footer, trg)))
                {
                    avoidInside = true;
                    if (repeatableTables.Any(t => ReferenceEquals(t.Footer, trg)))
                        boundaries[first].Avoid = true;
                }
                else if (box.Source is TableRow tr && tr.Parent is TableRowGroup rowGroup && rowGroup.Parent is Table parentTable &&
                         repeatableTables.Any(t => ReferenceEquals(t.Table, parentTable)))
                {
                    avoidInside = true;
                }
            }
            if (end > first + 1 && avoidInside)
            { avoidChanges[first + 1]++; avoidChanges[end]--; }
            if (box.Source is Paragraph paragraph)
            {
                var orphans = box.AnonymousOwner is { } parent ? (int)parent.GetValue(CssFragmentationProperties.OrphansProperty)! : CssFragmentationProperties.Orphans(paragraph);
                var widows = box.AnonymousOwner is { } owner ? (int)owner.GetValue(CssFragmentationProperties.WidowsProperty)! : CssFragmentationProperties.Widows(paragraph);
                var rule = new ParagraphRule(first, end, orphans, widows, box.LineBands);
                for (var index = first + 1; index < end; index++) (paragraphRules[index] ??= []).Add(rule);
            }
        }
        for (var i = 1; i < count; i++)
        {
            if (!string.Equals(linePageNames[i - 1], linePageNames[i], StringComparison.Ordinal))
                boundaries[i].Forced = true;
        }
        var avoids = new bool[count + 1];
        var active = 0;
        for (var index = 0; index <= count; index++) { active += avoidChanges[index]; avoids[index] = active != 0 || boundaries[index].Avoid; }

        bool Allowed(int index, int start, bool respectLines, bool respectAvoid)
        {
            if (respectAvoid && avoids[index]) return false;
            if (respectLines && paragraphRules[index] is { } rules)
                foreach (var rule in rules)
                {
                    var before = index - Math.Max(start, rule.First); var after = rule.End - index;
                    if (rule.LineBands is { } bands)
                    { var at = LowerBound(bands, index); before = at - LowerBound(bands, start); after = bands.Length - at; }
                    if (before < rule.Orphans || after < rule.Widows) return false;
                }
            return true;
        }
        double StartTop(int index, bool forced) => boundaries[index].StartsBox
            ? forced ? boundaries[index].MarginTop : boundaries[index].BorderTop : lines[index].Top;
        var rtl = direction == FlowDirection.RightToLeft;
        bool? WantsRight(CssFragmentBreak value) => value switch
        {
            CssFragmentBreak.Left => false, CssFragmentBreak.Right => true,
            CssFragmentBreak.Recto => !rtl, CssFragmentBreak.Verso => rtl, _ => null,
        };

        (double Header, double Footer, double TableTop) ResolveRepeatSpace(double pageTop)
        {
            if (repeatableTables.Count == 0) return (0, 0, double.NaN);
            var repeatedHeader = 0d;
            var repeatedFooter = 0d;
            var tableTop = double.NaN;
            foreach (var table in repeatableTables)
            {
                var headerHeight = table.Header is not null && table.HeaderBounds.Height > 0 && table.HeaderBounds.Height <= height * 0.25 + .01
                    ? table.HeaderBounds.Height : 0;
                var footerHeight = table.Footer is not null && table.FooterBounds.Height > 0 && table.FooterBounds.Height <= height * 0.25 + .01
                    ? table.FooterBounds.Height : 0;
                if (headerHeight <= 0 && footerHeight <= 0) continue;
                var continuesIntoBody = pageTop >= table.BodyTop - .01 && pageTop < table.BodyBottom - .01;
                if (continuesIntoBody && headerHeight > 0 && table.HeaderBounds.Bottom <= pageTop + .01)
                {
                    repeatedHeader = headerHeight;
                    tableTop = table.TableBounds.Top;
                }
                var remainingForBody = Math.Max(1, height - repeatedHeader);
                var tableActiveOnPage = pageTop < table.BodyBottom - .01 && table.BodyTop < pageTop + remainingForBody - .01;
                if (tableActiveOnPage && footerHeight > 0 && table.FooterBounds.Bottom > pageTop + remainingForBody + .01)
                {
                    repeatedFooter = footerHeight;
                    if (double.IsNaN(tableTop)) tableTop = table.TableBounds.Top;
                }
                if (repeatedHeader > 0 || repeatedFooter > 0) break;
            }
            return (repeatedHeader, repeatedFooter, tableTop);
        }

        var firstRight = WantsRight(boundaries[0].Side) ?? !rtl;
        var pages = new List<Fragment>();
        var startLine = 0;
        var top = 0d;
        var nextForced = 1;
        while (startLine < count)
        {
            while (nextForced <= startLine || nextForced < count && !boundaries[nextForced].Forced) nextForced++;
            var (repeatedHeader, repeatedFooter, repeatTableTop) = ResolveRepeatSpace(top);
            var effectiveHeight = Math.Max(1, height - repeatedHeader - repeatedFooter);
            var pageName = linePageNames[startLine];
            // Discard an excessive leading gap instead of emitting empty pages forever.
            if (lines[startLine].Top >= top + effectiveHeight && lines[startLine].Height <= effectiveHeight)
            {
                top = lines[startLine].Top;
                (repeatedHeader, repeatedFooter, repeatTableTop) = ResolveRepeatSpace(top);
                effectiveHeight = Math.Max(1, height - repeatedHeader - repeatedFooter);
            }
            var fit = startLine;
            var partialSpace = -1;
            while (fit < count)
            {
                if (lines[fit].Top + lines[fit].Height <= top + effectiveHeight + .01) { fit++; continue; }
                if (lines[fit].IsSpace && lines[fit].Top < top + effectiveHeight - .01) { partialSpace = fit++; }
                break;
            }
            fit = Math.Max(startLine + 1, fit);
            var forced = nextForced < count && nextForced <= fit && (partialSpace < 0 || nextForced <= partialSpace);
            var endLine = forced ? nextForced : fit;
            if (!forced && fit < count)
            {
                var found = false;
                // CSS first relaxes widow/orphan restrictions, then avoidance.
                for (var pass = 0; pass < 3 && !found; pass++)
                    for (var candidate = fit; candidate > startLine; candidate--)
                        if (Allowed(candidate, startLine, pass == 0, pass < 2))
                        { endLine = candidate; found = true; break; }
            }
            if (partialSpace >= 0 && endLine == partialSpace + 1)
            {
                pages.Add(new(startLine, endLine, top, top + effectiveHeight, PageName: pageName,
                    RepeatedHeaderHeight: repeatedHeader, RepeatedFooterHeight: repeatedFooter, RepeatTableTop: repeatTableTop));
                startLine = partialSpace; top += effectiveHeight;
                continue;
            }
            var nextTop = endLine < count ? StartTop(endLine, forced) : top + effectiveHeight;
            if (endLine == startLine + 1 && lines[startLine].Top + lines[startLine].Height > top + effectiveHeight + .01)
            {
                // A band with no common line boundary (or a single oversized line)
                // still has to continue on subsequent pages instead of losing ink.
                var bottom = Math.Max(lines[startLine].Top + lines[startLine].Height,
                    endLine == count ? lines[startLine].Top + lines[startLine].Height : nextTop);
                while (top + effectiveHeight < bottom - .01)
                {
                    pages.Add(new(startLine, endLine, top, top + effectiveHeight, PageName: pageName,
                        RepeatedHeaderHeight: repeatedHeader, RepeatedFooterHeight: repeatedFooter, RepeatTableTop: repeatTableTop));
                    top += effectiveHeight;
                    (repeatedHeader, repeatedFooter, repeatTableTop) = ResolveRepeatSpace(top);
                    effectiveHeight = Math.Max(1, height - repeatedHeader - repeatedFooter);
                }
                nextTop = endLine < count ? StartTop(endLine, forced) : top + effectiveHeight;
            }
            pages.Add(new(startLine, endLine, top, Math.Max(top, Math.Min(top + effectiveHeight, nextTop)), PageName: pageName,
                RepeatedHeaderHeight: repeatedHeader, RepeatedFooterHeight: repeatedFooter, RepeatTableTop: repeatTableTop));
            if (forced && WantsRight(boundaries[endLine].Side) is { } right)
            {
                var nextRight = firstRight != ((pages.Count & 1) != 0);
                if (nextRight != right)
                    pages.Add(new(endLine, endLine, nextTop, nextTop, Blank: true, PageName: pageName));
            }
            startLine = endLine;
            top = nextTop;
        }
        return pages;
    }

    private static int LowerBound(int[] sorted, int value)
    {
        var lo = 0; var hi = sorted.Length;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (sorted[mid] < value) lo = mid + 1; else hi = mid; }
        return lo;
    }
}
