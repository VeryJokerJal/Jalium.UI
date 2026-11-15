using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed record LineHyphenChange(
        int TailDeleteLength = 0,
        string TailReplaceText = "",
        int LeadDeleteLength = 0,
        string LeadReplaceText = "")
    {
        internal static readonly LineHyphenChange Empty = new();

        internal bool HasChanges =>
            TailDeleteLength > 0 || TailReplaceText.Length > 0 ||
            LeadDeleteLength > 0 || LeadReplaceText.Length > 0;

        internal bool HasTailChanges =>
            TailDeleteLength > 0 || TailReplaceText.Length > 0;

        internal bool HasLeadChanges =>
            LeadDeleteLength > 0 || LeadReplaceText.Length > 0;
    }

    internal readonly record struct HyphenBreakInfo(
        bool Allowed,
        bool IsExplicitSoftHyphen,
        int TailDeleteLength,
        string TailReplaceText,
        int LeadDeleteEnd,
        string LeadReplaceText)
    {
        public bool HasTailChange => TailDeleteLength > 0 || TailReplaceText.Length > 0;
        public bool HasLeadChange => LeadDeleteEnd > 0 || LeadReplaceText.Length > 0;
    }

    private readonly record struct HyphenWordItem(
        Rune Rune,
        int Start,
        int End,
        Run? Run);

    private Dictionary<int, HyphenBreakInfo>? _hyphenBreaks;
    private int _activeLeadHyphenBreakIndex = -1;

    private bool HasAuthoredHyphensOnInlines()
    {
        if (_inlines is null || _inlines.Count == 0) return false;
        foreach (var range in GetInlineTextRanges())
        {
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.HyphensProperty) ||
                HasAuthoredFlowValue(range.Run, CssFlowProperties.HyphenateCharacterProperty) ||
                HasAuthoredFlowValue(range.Run, CssFlowProperties.HyphenateLimitCharsProperty))
                return true;
        }
        return false;
    }

    internal bool TryGetHyphenBreak(int sourceIndex, out HyphenBreakInfo info)
    {
        if (_hyphenBreaks is not null && _hyphenBreaks.TryGetValue(sourceIndex, out info))
            return info.Allowed;
        info = default;
        return false;
    }

    private bool IsAllowedSoftHyphen(int sourceIndex)
    {
        if (_hyphenBreaks is not null)
            return _hyphenBreaks.TryGetValue(sourceIndex, out var info) && info.Allowed;
        return sourceIndex >= 0 && sourceIndex < _visualText.Length &&
            _visualText[sourceIndex] == '\u00AD' &&
            GetHyphensAt(sourceIndex) != CssHyphens.None &&
            MeetsHyphenateLimitChars(sourceIndex);
    }

    private bool IsAllowedSoftHyphen(int sourceIndex, int before, int after)
        => sourceIndex >= 0 && sourceIndex < _visualText.Length &&
           _visualText[sourceIndex] == '\u00AD' &&
           GetHyphensAt(sourceIndex) != CssHyphens.None &&
           MeetsHyphenateLimitChars(sourceIndex, before, after);

    private bool IsAllowedAutoHyphen(int sourceIndex, int before, int after)
        => sourceIndex >= 0 && sourceIndex < _visualText.Length &&
           GetHyphensAt(sourceIndex) == CssHyphens.Auto &&
           MeetsHyphenateLimitChars(sourceIndex, before, after);

    private CssHyphens GetHyphensAt(int sourceIndex)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= sourceIndex && sourceIndex < range.End)
                return (CssHyphens)range.Run.GetValue(CssFlowProperties.HyphensProperty)!;
        return (CssHyphens)GetValue(CssFlowProperties.HyphensProperty)!;
    }

    private string GetLanguageTagAt(Run? run)
    {
        if (run is not null)
        {
            var runLang = run.Language?.IetfLanguageTag;
            if (!string.IsNullOrEmpty(runLang))
                return runLang;
        }
        return Language?.IetfLanguageTag ?? "en-US";
    }

    private string GetLanguageTagAt(int sourceIndex)
        => GetLanguageTagAt(GetSoftHyphenRun(sourceIndex));

    private Run? GetSoftHyphenRun(int sourceIndex)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= sourceIndex && sourceIndex < range.End)
                return range.Run;
        return null;
    }

    private void RebuildHyphenation()
    {
        _hyphenBreaks = null;
        _activeLeadHyphenBreakIndex = -1;
        if (string.IsNullOrEmpty(_visualText))
            return;

        var hasSoftHyphen = _visualText.Contains('\u00AD');
        var hasAutoHyphens = (CssHyphens)GetValue(CssFlowProperties.HyphensProperty)! == CssHyphens.Auto;
        var ranges = GetInlineTextRanges();
        if (!hasAutoHyphens)
        {
            foreach (var range in ranges)
            {
                if ((CssHyphens)range.Run.GetValue(CssFlowProperties.HyphensProperty)! == CssHyphens.Auto)
                {
                    hasAutoHyphens = true;
                    break;
                }
            }
        }

        if (!hasSoftHyphen && !hasAutoHyphens)
            return;

        var breaks = new Dictionary<int, HyphenBreakInfo>();
        var displayNone = InlineDisplayMask();
        var items = new List<HyphenWordItem>(32);
        var rangeIndex = 0;

        for (var index = 0; index < _visualText.Length;)
        {
            var validRune = Rune.TryGetRuneAt(_visualText, index, out var rune);
            var length = validRune ? rune.Utf16SequenceLength : 1;
            if (displayNone?[index] == true)
            {
                index += length;
                continue;
            }

            if (_textTransformClassification?.TryGetValue(index, out var classified) == true)
                rune = classified;

            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= index)
                rangeIndex++;
            var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= index
                ? ranges[rangeIndex].Run
                : null;

            if (validRune && IsHyphenationWordRune(rune))
            {
                items.Add(new HyphenWordItem(rune, index, index + length, run));
            }
            else
            {
                FinishHyphenationWord(items, breaks);
            }

            index += length;
        }

        FinishHyphenationWord(items, breaks);
        _hyphenBreaks = breaks;
    }

    private void FinishHyphenationWord(List<HyphenWordItem> items, Dictionary<int, HyphenBreakInfo> breaks)
    {
        if (items.Count == 0)
            return;

        var hasExplicitSoftHyphen = false;
        var totalChars = 0;
        foreach (var item in items)
        {
            if (item.Rune.Value == 0x00AD)
                hasExplicitSoftHyphen = true;
            else if (CountsForHyphenation(item.Rune))
                totalChars++;
        }

        if (hasExplicitSoftHyphen)
        {
            var before = 0;
            foreach (var item in items)
            {
                if (item.Rune.Value == 0x00AD)
                {
                    var allowed = GetHyphensAt(item.Start) != CssHyphens.None &&
                                  MeetsHyphenateLimitChars(item.Start, before, totalChars - before);
                    breaks[item.Start] = new HyphenBreakInfo(
                        allowed, IsExplicitSoftHyphen: true,
                        TailDeleteLength: 0, TailReplaceText: string.Empty,
                        LeadDeleteEnd: 0, LeadReplaceText: string.Empty);
                }
                else if (CountsForHyphenation(item.Rune))
                {
                    before++;
                }
            }
            items.Clear();
            return;
        }

        var first = 0;
        var last = items.Count - 1;
        while (first <= last && !IsHyphenationCoreRune(items[first].Rune))
            first++;
        while (last >= first && !IsHyphenationCoreRune(items[last].Rune))
            last--;

        var count = last - first + 1;
        if (count < 2)
        {
            items.Clear();
            return;
        }

        var anyAuto = false;
        for (var i = first; i <= last; i++)
        {
            var r = items[i].Rune;
            if (Rune.IsDigit(r) || r.Value == '_')
            {
                items.Clear();
                return;
            }
            if (!anyAuto && GetHyphensAt(items[i].Start) == CssHyphens.Auto)
                anyAuto = true;
        }

        if (!anyAuto)
        {
            items.Clear();
            return;
        }

        var wordRunes = new Rune[count];
        var charPrefix = new int[count + 1];
        for (var i = 0; i < count; i++)
        {
            var r = items[first + i].Rune;
            wordRunes[i] = r;
            charPrefix[i + 1] = charPrefix[i] + (CountsForHyphenation(r) ? 1 : 0);
        }

        var wordTotalChars = charPrefix[count];
        CssHyphenator.CompiledDictionary? cachedDict = null;
        string? cachedLang = null;
        List<CssHyphenator.BreakOpportunity>? cachedBreaks = null;

        for (var pos = 1; pos < count; pos++)
        {
            var prevItem = items[first + pos - 1];
            var nextItem = items[first + pos];
            if (Rune.GetUnicodeCategory(nextItem.Rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;

            if (GetHyphensAt(prevItem.Start) != CssHyphens.Auto ||
                GetHyphensAt(nextItem.Start) != CssHyphens.Auto)
                continue;

            var lang = GetLanguageTagAt(prevItem.Run);
            if (!string.Equals(lang, cachedLang, StringComparison.Ordinal))
            {
                cachedLang = lang;
                cachedDict = CssHyphenator.ResolveDictionary(lang);
                cachedBreaks = cachedDict?.FindBreaks(wordRunes);
            }

            if (cachedBreaks is null || cachedBreaks.Count == 0)
                continue;

            CssHyphenator.BreakOpportunity? matched = null;
            foreach (var opp in cachedBreaks)
            {
                if (opp.RuneBoundaryIndex == pos)
                {
                    matched = opp;
                    break;
                }
            }

            if (matched is not { } breakOpp)
                continue;

            var before = charPrefix[pos];
            var after = wordTotalChars - before;
            if (!MeetsHyphenateLimitChars(prevItem.Start, before, after))
                continue;

            var breakKey = prevItem.End - 1;
            var change = breakOpp.Change;
            var tailReplace = change.TailReplace ?? string.Empty;
            var leadReplace = change.LeadReplace ?? string.Empty;
            var tailDeleteLength = change.TailDeleteRunes > 0
                ? prevItem.End - items[first + Math.Max(0, pos - change.TailDeleteRunes)].Start
                : 0;
            var leadDeleteEnd = change.LeadDeleteRunes > 0
                ? items[first + Math.Min(count - 1, pos + change.LeadDeleteRunes - 1)].End
                : leadReplace.Length > 0 ? prevItem.End : 0;

            breaks[breakKey] = new HyphenBreakInfo(
                Allowed: true,
                IsExplicitSoftHyphen: false,
                TailDeleteLength: tailDeleteLength,
                TailReplaceText: tailReplace,
                LeadDeleteEnd: leadDeleteEnd,
                LeadReplaceText: leadReplace);
        }

        items.Clear();
    }

    internal static bool IsHyphenationCoreRune(Rune rune)
    {
        if (Rune.IsLetter(rune))
            return true;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private bool MeetsHyphenateLimitChars(int sourceIndex)
    {
        var wordStart = sourceIndex;
        while (wordStart > 0)
        {
            var previous = wordStart - 1;
            if (previous > 0 && char.IsLowSurrogate(_visualText[previous]) &&
                char.IsHighSurrogate(_visualText[previous - 1])) previous--;
            if (!Rune.TryGetRuneAt(_visualText, previous, out var rune) ||
                !IsHyphenationWordRune(rune)) break;
            wordStart = previous;
        }

        var wordEnd = sourceIndex + 1;
        while (wordEnd < _visualText.Length &&
               Rune.TryGetRuneAt(_visualText, wordEnd, out var rune) &&
               IsHyphenationWordRune(rune))
            wordEnd += rune.Utf16SequenceLength;

        var before = CountHyphenationCharacters(wordStart, sourceIndex);
        var after = CountHyphenationCharacters(sourceIndex + 1, wordEnd);
        return MeetsHyphenateLimitChars(sourceIndex, before, after);
    }

    private bool MeetsHyphenateLimitChars(int sourceIndex, int before, int after)
    {
        var run = GetSoftHyphenRun(sourceIndex);
        var limits = run is null
            ? (CssHyphenateLimitChars)GetValue(CssFlowProperties.HyphenateLimitCharsProperty)!
            : (CssHyphenateLimitChars)run.GetValue(CssFlowProperties.HyphenateLimitCharsProperty)!;
        return before + after >= limits.UsedWord &&
               before >= limits.UsedBefore && after >= limits.UsedAfter;
    }

    private static bool IsHyphenationWordRune(Rune rune)
    {
        if (Rune.IsLetterOrDigit(rune) || rune.Value is 0x00AD or '\'' or 0x2019 or 0x00B7 or '_')
            return true;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private int CountHyphenationCharacters(int start, int end)
    {
        var count = 0;
        for (var index = start; index < end;)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune)) break;
            if (CountsForHyphenation(rune)) count++;
            index += rune.Utf16SequenceLength;
        }
        return count;
    }

    private static bool CountsForHyphenation(Rune rune)
        => rune.Value != 0x00AD && Rune.GetUnicodeCategory(rune) is not
            (UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation or
             UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
             UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or
             UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation);

    private double MeasureSoftHyphenWidth(int sourceIndex)
    {
        var text = GetHyphenationStringAt(sourceIndex);
        return MeasureReplacementTextWidth(sourceIndex, text);
    }

    internal double MeasureReplacementTextWidth(int sourceIndex, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var run = GetSoftHyphenRun(sourceIndex);
        return run is null ? MeasureText(text).WidthIncludingTrailingWhitespace
            : MeasureRunText(run, text).WidthIncludingTrailingWhitespace;
    }

    private string GetHyphenationStringAt(int sourceIndex)
    {
        var run = GetSoftHyphenRun(sourceIndex);
        var value = run is null
            ? (CssHyphenateCharacter)GetValue(CssFlowProperties.HyphenateCharacterProperty)!
            : (CssHyphenateCharacter)run.GetValue(CssFlowProperties.HyphenateCharacterProperty)!;
        return value.Value ?? "-";
    }

    private LineHyphenChange? ResolveLineHyphenChange(
        int lineStart,
        int lineLength,
        int prevHyphenBreakIndex,
        bool softHyphenVisible)
    {
        var leadDelete = 0;
        var leadReplace = string.Empty;
        if (prevHyphenBreakIndex >= 0 &&
            prevHyphenBreakIndex + 1 == lineStart &&
            TryGetHyphenBreak(prevHyphenBreakIndex, out var prevInfo) &&
            prevInfo.HasLeadChange)
        {
            leadDelete = prevInfo.LeadDeleteEnd > lineStart
                ? Math.Clamp(prevInfo.LeadDeleteEnd - lineStart, 0, lineLength)
                : 0;
            leadReplace = prevInfo.LeadReplaceText;
        }

        var tailDelete = 0;
        var tailReplace = string.Empty;
        if (softHyphenVisible && lineLength > 0 &&
            TryGetHyphenBreak(lineStart + lineLength - 1, out var curInfo) &&
            curInfo.HasTailChange)
        {
            tailDelete = Math.Clamp(curInfo.TailDeleteLength, 0, lineLength - leadDelete);
            tailReplace = curInfo.TailReplaceText;
        }

        if (leadDelete == 0 && leadReplace.Length == 0 && tailDelete == 0 && tailReplace.Length == 0)
            return null;

        return new LineHyphenChange(tailDelete, tailReplace, leadDelete, leadReplace);
    }

    private double MeasureCandidatePrefixWidth(int startIndex, int candidateLength, double tabOrigin)
    {
        if (_activeLeadHyphenBreakIndex >= 0 &&
            _activeLeadHyphenBreakIndex + 1 == startIndex &&
            TryGetHyphenBreak(_activeLeadHyphenBreakIndex, out var prevInfo) &&
            prevInfo.HasLeadChange)
        {
            var leadDelete = prevInfo.LeadDeleteEnd > startIndex
                ? Math.Clamp(prevInfo.LeadDeleteEnd - startIndex, 0, candidateLength)
                : 0;
            var change = new LineHyphenChange(0, string.Empty, leadDelete, prevInfo.LeadReplaceText);
            return MeasureLineWithHyphenChange(startIndex, candidateLength, change,
                softHyphenVisible: false, tabOrigin: tabOrigin, includeTrailingWhitespace: false);
        }

        return MeasureInlineRange(startIndex, candidateLength, tabOrigin: tabOrigin) -
               EdgeAutospaceReplacementWidth(startIndex, startIndex + candidateLength);
    }

    private double MeasureHyphenatedCandidateWidth(int lineStart, int boundary, double tabOrigin)
    {
        var length = boundary - lineStart;
        var change = ResolveLineHyphenChange(lineStart, length, _activeLeadHyphenBreakIndex, softHyphenVisible: true);
        return MeasureLineWithHyphenChange(lineStart, length, change,
            softHyphenVisible: true, tabOrigin: tabOrigin, includeTrailingWhitespace: false);
    }

    internal double MeasureLineWithHyphenChange(
        int start,
        int length,
        LineHyphenChange? change,
        bool softHyphenVisible,
        double tabOrigin,
        bool includeTrailingWhitespace = false)
    {
        if (length <= 0)
            return 0;

        var end = start + length;
        if (change is not { HasChanges: true })
        {
            var baseWidth = MeasureInlineRange(start, length,
                includeTrailingWhitespace: includeTrailingWhitespace && !softHyphenVisible,
                tabOrigin: tabOrigin);
            baseWidth = Math.Max(0, baseWidth - EdgeAutospaceReplacementWidth(start, end));
            if (softHyphenVisible)
                baseWidth += MeasureSoftHyphenWidth(end - 1);
            return baseWidth;
        }

        var leadDelete = Math.Clamp(change.LeadDeleteLength, 0, length);
        var tailDelete = Math.Clamp(change.TailDeleteLength, 0, length - leadDelete);
        var bodyStart = start + leadDelete;
        var bodyLength = length - leadDelete - tailDelete;

        var width = 0.0;
        if (change.LeadReplaceText.Length > 0)
            width += MeasureReplacementTextWidth(start, change.LeadReplaceText);

        if (bodyLength > 0)
        {
            var bodyIncludeWs = includeTrailingWhitespace && !softHyphenVisible &&
                                tailDelete == 0 && change.TailReplaceText.Length == 0;
            var bodyWidth = MeasureInlineRange(bodyStart, bodyLength,
                includeTrailingWhitespace: bodyIncludeWs,
                tabOrigin: tabOrigin + width);
            bodyWidth = Math.Max(0, bodyWidth - EdgeAutospaceReplacementWidth(bodyStart, bodyStart + bodyLength));
            width += bodyWidth;
        }

        if (change.TailReplaceText.Length > 0)
            width += MeasureReplacementTextWidth(end - 1, change.TailReplaceText);

        if (softHyphenVisible)
            width += MeasureSoftHyphenWidth(end - 1);

        return width;
    }

    private double GetHyphenChangeBoundaryOffset(in TextLayoutLine line, int index, double tabOrigin)
    {
        var physicalWidth = line.Width + line.HangingStart + line.HangingEnd;
        if (index <= line.StartIndex) return 0;
        if (index >= line.StartIndex + line.Length) return physicalWidth;

        var change = line.HyphenChange;
        if (change is not { HasChanges: true })
        {
            var raw = MeasureInlineRange(line.StartIndex, index - line.StartIndex,
                includeTrailingWhitespace: true, tabOrigin: tabOrigin) +
                WordSpacingBeforeSeparator(index) +
                LetterSpacingAtBoundary(index, line.StartIndex, line.StartIndex + line.Length);
            return Math.Min(physicalWidth, raw);
        }

        var leadDelete = Math.Clamp(change.LeadDeleteLength, 0, line.Length);
        var tailDelete = Math.Clamp(change.TailDeleteLength, 0, line.Length - leadDelete);
        var bodyStart = line.StartIndex + leadDelete;
        var bodyEnd = line.StartIndex + line.Length - tailDelete;
        var leadWidth = change.LeadReplaceText.Length > 0
            ? MeasureReplacementTextWidth(line.StartIndex, change.LeadReplaceText)
            : 0.0;

        if (index < bodyStart)
        {
            var frac = leadDelete > 0 ? (double)(index - line.StartIndex) / leadDelete : 0.0;
            return Math.Min(physicalWidth, leadWidth * frac);
        }

        if (index == bodyStart)
            return Math.Min(physicalWidth, leadWidth);

        if (index <= bodyEnd)
        {
            var bodyOffset = MeasureInlineRange(bodyStart, index - bodyStart,
                includeTrailingWhitespace: true, tabOrigin: tabOrigin + leadWidth);
            if (index < bodyEnd)
            {
                bodyOffset += WordSpacingBeforeSeparator(index) +
                    LetterSpacingAtBoundary(index, bodyStart, bodyEnd);
            }
            return Math.Min(physicalWidth, leadWidth + bodyOffset);
        }

        var fullBodyWidth = bodyEnd > bodyStart
            ? MeasureInlineRange(bodyStart, bodyEnd - bodyStart,
                includeTrailingWhitespace: true, tabOrigin: tabOrigin + leadWidth)
            : 0.0;
        var tailWidth = (change.TailReplaceText.Length > 0
                ? MeasureReplacementTextWidth(line.StartIndex + line.Length - 1, change.TailReplaceText)
                : 0.0) +
            (line.SoftHyphenVisible
                ? MeasureSoftHyphenWidth(line.StartIndex + line.Length - 1)
                : 0.0);
        var tailFrac = tailDelete > 0 ? (double)(index - bodyEnd) / tailDelete : 1.0;
        return Math.Min(physicalWidth, leadWidth + fullBodyWidth + tailWidth * tailFrac);
    }

    private void DrawStyledInlineRangeWithHyphenChange(
        DrawingContext context,
        in TextLayoutLine line,
        int start,
        int end,
        ref double x,
        double lineY)
    {
        if (line.HyphenChange is not { HasChanges: true } change)
        {
            DrawStyledInlineRange(context, line, start, end, ref x, lineY);
            return;
        }

        var lineEnd = line.StartIndex + line.Length;
        var leadDelete = Math.Clamp(change.LeadDeleteLength, 0, line.Length);
        var tailDelete = Math.Clamp(change.TailDeleteLength, 0, line.Length - leadDelete);
        var bodyStart = line.StartIndex + leadDelete;
        var bodyEnd = lineEnd - tailDelete;

        if (start <= line.StartIndex && change.LeadReplaceText.Length > 0)
        {
            var run = GetSoftHyphenRun(line.StartIndex);
            var measurement = run is null
                ? MeasureText(change.LeadReplaceText)
                : MeasureRunText(run, change.LeadReplaceText);
            DrawMeasuredFragment(context, run, measurement, ref x, lineY, line,
                line.StartIndex, Math.Max(1, leadDelete), generated: true);
        }

        var clampedStart = Math.Max(start, bodyStart);
        var clampedEnd = Math.Min(end, bodyEnd);
        if (clampedEnd > clampedStart)
            DrawStyledInlineRange(context, line, clampedStart, clampedEnd, ref x, lineY);

        if (end >= lineEnd && change.TailReplaceText.Length > 0 && line.Length > 0)
        {
            var run = GetSoftHyphenRun(lineEnd - 1);
            var measurement = run is null
                ? MeasureText(change.TailReplaceText)
                : MeasureRunText(run, change.TailReplaceText);
            var tailSourceStart = tailDelete > 0 ? bodyEnd : lineEnd - 1;
            DrawMeasuredFragment(context, run, measurement, ref x, lineY, line,
                tailSourceStart, Math.Max(1, tailDelete), generated: true);
        }
    }

    private void DrawVisibleSoftHyphen(DrawingContext context, in TextLayoutLine line,
        ref double x, double lineY)
    {
        var sourceIndex = line.StartIndex + line.Length - 1;
        var text = GetHyphenationStringAt(sourceIndex);
        if (text.Length == 0) return;
        var run = GetSoftHyphenRun(sourceIndex);
        var measurement = run is null ? MeasureText(text) : MeasureRunText(run, text);
        DrawMeasuredFragment(context, run, measurement, ref x, lineY, line, sourceIndex, 1, generated: true);
    }

    private bool TryDrawCoalescedHyphenatedLine(
        DrawingContext context,
        in TextLayoutLine line,
        int lineEnd,
        double originX,
        double lineY)
    {
        if (line.Length <= 0 ||
            lineEnd != line.StartIndex + line.Length ||
            (!line.SoftHyphenVisible && line.HyphenChange is not { HasChanges: true }) ||
            !CanCoalesceHyphenatedLine(line.StartIndex, line.Length, out var uniformForeground) ||
            (DocumentWritingToolsOwner is { } wtOwner &&
             MacOSWritingToolsPresentation.TryGetGlyphRanges(wtOwner, out _, out _)))
        {
            return false;
        }

        var combined = BuildCoalescedHyphenatedLineText(
            line.StartIndex, line.Length, line.HyphenChange, line.SoftHyphenVisible);
        if (combined.Length == 0)
            return true;

        var measurement = MeasureText(combined);
        var formatted = measurement.FormattedText;
        if (formatted is null)
            return true;

        formatted.Foreground = uniformForeground;
        formatted.MaxTextWidth = double.MaxValue;
        formatted.MaxTextHeight = TextPaintHeight(line);
        formatted.Trimming = EffectiveTextWrapping() == TextWrapping.NoWrap ? TextTrimming : TextTrimming.None;
        formatted.ApplyTextOptionsFrom(this);
        Point origin;
        if (UsesCssLineMetrics)
        {
            origin = TextPaintOrigin(formatted, line, originX, lineY);
        }
        else
        {
            var metrics = TextMeasurement.GetFontMetrics(
                FontFamily.GetRenderingSource(this),
                FontSize,
                FontWeight.ToOpenTypeWeight(),
                FontStyle.ToOpenTypeStyle());
            var ascent = metrics.Ascent > 0 ? metrics.Ascent : FontSize * 0.8;
            origin = new Point(originX, lineY + line.Baseline - ascent);
        }
        DrawDocumentGlyphs(context, formatted, origin, line.StartIndex, line.Length, generated: true);
        return true;
    }

    private bool CanCoalesceHyphenatedLine(int start, int length, out Brush? uniformForeground)
    {
        uniformForeground = Foreground;
        if (length <= 0 ||
            _usesInlineFontLayout ||
            _hasWordSpacing ||
            _hasLetterSpacing ||
            _usesCapsShaping ||
            _usesAutospaceShaping ||
            WantsShapedJustification ||
            ContainsPreservedTab(start, length) ||
            _internalPaintSpacingUnits.Count > 0)
        {
            return false;
        }

        if (_inlines is null)
            return true;

        var end = start + length;
        Brush? firstForeground = null;
        var firstSeen = false;
        var coveredLength = 0;
        foreach (var range in GetInlineTextRanges())
        {
            if (range.End <= start || range.Start >= end) continue;
            var overlapStart = Math.Max(start, range.Start);
            var overlapEnd = Math.Min(end, range.End);
            coveredLength += overlapEnd - overlapStart;
            var fg = range.Run.Foreground ?? Foreground;
            if (!firstSeen)
            {
                firstSeen = true;
                firstForeground = fg;
            }
            else if (!ReferenceEquals(fg, firstForeground))
            {
                return false;
            }
        }

        if (firstSeen)
        {
            if (coveredLength < length && !ReferenceEquals(firstForeground, Foreground))
                return false;
            uniformForeground = firstForeground;
        }

        return true;
    }

    private string BuildCoalescedHyphenatedLineText(
        int start,
        int length,
        LineHyphenChange? change,
        bool softHyphenVisible)
    {
        var end = start + length;
        var leadDelete = change is { HasChanges: true } ? Math.Clamp(change.LeadDeleteLength, 0, length) : 0;
        var tailDelete = change is { HasChanges: true } ? Math.Clamp(change.TailDeleteLength, 0, length - leadDelete) : 0;
        var bodyStart = start + leadDelete;
        var bodyLength = length - leadDelete - tailDelete;
        var leadText = change is { HasChanges: true } ? change.LeadReplaceText : string.Empty;
        var bodyText = bodyLength > 0 ? PaintSlice(bodyStart, bodyLength) : string.Empty;
        var tailText = change is { HasChanges: true } ? change.TailReplaceText : string.Empty;
        var hyphenText = softHyphenVisible ? GetHyphenationStringAt(end - 1) : string.Empty;
        return string.Concat(leadText, bodyText, tailText, hyphenText);
    }
}
