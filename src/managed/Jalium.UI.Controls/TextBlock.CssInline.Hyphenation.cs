using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private sealed record InlineHyphenBreak(
            bool Allowed,
            bool IsExplicitSoftHyphen,
            int TailDeleteLength = 0,
            string TailReplaceText = "",
            CssInlineSource? PrevTailSource = null,
            int PrevTailDeleteLength = 0,
            CssInlineSource? LeadSource = null,
            int LeadStartIndex = 0,
            int LeadDeleteLength = 0,
            string LeadReplaceText = "");

        private readonly record struct InlineWordItem(
            Rune Rune,
            CssInlineSource Source,
            int SourceIndex,
            int SourceEnd,
            Run? Run);

        private readonly Dictionary<(CssInlineSource Source, int Index), InlineHyphenBreak> _inlineHyphens = [];

        private bool AllowedInlineSoftHyphen(CssInlineSource source, int index)
            => _inlineHyphens.TryGetValue((source, index), out var info)
                ? info.Allowed
                : source.Text.IsAllowedSoftHyphen(index);

        private bool TryGetInlineHyphen(CssInlineSource source, int index, out InlineHyphenBreak info)
        {
            if (_inlineHyphens.TryGetValue((source, index), out info!) && info.Allowed)
                return true;
            if (source.Text.TryGetHyphenBreak(index, out var single) && single.Allowed)
            {
                info = new InlineHyphenBreak(
                    Allowed: true,
                    IsExplicitSoftHyphen: single.IsExplicitSoftHyphen,
                    TailDeleteLength: single.TailDeleteLength,
                    TailReplaceText: single.TailReplaceText,
                    LeadSource: source,
                    LeadStartIndex: index + 1,
                    LeadDeleteLength: single.LeadDeleteEnd > index + 1 ? single.LeadDeleteEnd - (index + 1) : 0,
                    LeadReplaceText: single.LeadReplaceText);
                return true;
            }
            info = null!;
            return false;
        }

        private bool TryGetLeadingHyphenBreak(List<CssInlineUnit> units, int start, out InlineHyphenBreak info)
        {
            info = null!;
            if (start <= 0 || start >= units.Count)
                return false;
            var prev = start - 1;
            while (prev >= 0 && !units[prev].Source.Atomic && !units[prev].HardBreak &&
                   units[prev].Start == units[prev].End)
                prev--;
            if (prev < 0)
                return false;
            var unit = units[prev];
            if (unit.Source.Atomic || unit.HardBreak || unit.Space || unit.End <= unit.Start)
                return false;
            return TryGetInlineHyphen(unit.Source, unit.End - 1, out info);
        }

        private bool IsHyphenBreakAfter(List<CssInlineUnit> units, int index, int start)
        {
            var previous = index;
            while (previous >= start && !units[previous].Source.Atomic && !units[previous].HardBreak &&
                   units[previous].Start == units[previous].End)
                previous--;
            if (previous < start)
                return false;
            var unit = units[previous];
            return !unit.Source.Atomic && !unit.HardBreak && unit.End > unit.Start &&
                   AllowedInlineSoftHyphen(unit.Source, unit.End - 1);
        }

        private void PrepareHyphenation(List<CssInlineUnit> units)
        {
            _inlineHyphens.Clear();
            var hasSoftHyphen = false;
            var hasAutoHyphens = (CssHyphens)owner.GetValue(CssFlowProperties.HyphensProperty)! == CssHyphens.Auto;

            foreach (var unit in units)
            {
                if (unit.Source.Atomic || unit.Source.EmptyFrame)
                    continue;
                var text = unit.Source.Text;
                if (!hasSoftHyphen &&
                    text._visualText.AsSpan(unit.Start, unit.End - unit.Start).Contains('\u00AD'))
                    hasSoftHyphen = true;
                if (!hasAutoHyphens &&
                    ((CssHyphens)text.GetValue(CssFlowProperties.HyphensProperty)! == CssHyphens.Auto ||
                     text.HasAuthoredHyphensOnInlines()))
                    hasAutoHyphens = true;
                if (hasSoftHyphen && hasAutoHyphens)
                    break;
            }

            if (!hasSoftHyphen && !hasAutoHyphens)
                return;

            var items = new List<InlineWordItem>(32);
            foreach (var unit in units)
            {
                if (unit.Source.EmptyFrame || unit.PaintOffset > 0)
                    continue;
                if (unit.Source.Atomic || unit.Source.Block || unit.HardBreak)
                {
                    FinishInlineHyphenationWord(items);
                    continue;
                }

                var text = unit.Source.Text;
                var visual = text._visualText;
                for (var pos = unit.Start; pos < unit.End;)
                {
                    var valid = Rune.TryGetRuneAt(visual, pos, out var rune);
                    var len = valid ? rune.Utf16SequenceLength : 1;
                    if (text._textTransformClassification?.TryGetValue(pos, out var classified) == true)
                        rune = classified;

                    if (valid && IsHyphenationWordRune(rune))
                    {
                        var run = text.GetSoftHyphenRun(pos);
                        items.Add(new InlineWordItem(rune, unit.Source, pos, pos + len, run));
                    }
                    else
                    {
                        FinishInlineHyphenationWord(items);
                    }

                    pos += len;
                }
            }

            FinishInlineHyphenationWord(items);
        }

        private void FinishInlineHyphenationWord(List<InlineWordItem> items)
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
                        var allowed = item.Source.Text.IsAllowedSoftHyphen(
                            item.SourceIndex, before, totalChars - before);
                        _inlineHyphens[(item.Source, item.SourceIndex)] = new InlineHyphenBreak(
                            Allowed: allowed,
                            IsExplicitSoftHyphen: true);
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
                if (!anyAuto && items[i].Source.Text.GetHyphensAt(items[i].SourceIndex) == CssHyphens.Auto)
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
            string? cachedLang = null;
            List<CssHyphenator.BreakOpportunity>? cachedBreaks = null;

            for (var pos = 1; pos < count; pos++)
            {
                var prevItem = items[first + pos - 1];
                var nextItem = items[first + pos];
                if (Rune.GetUnicodeCategory(nextItem.Rune) is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                    continue;

                if (prevItem.Source.Text.GetHyphensAt(prevItem.SourceIndex) != CssHyphens.Auto ||
                    nextItem.Source.Text.GetHyphensAt(nextItem.SourceIndex) != CssHyphens.Auto)
                    continue;

                var lang = prevItem.Source.Text.GetLanguageTagAt(prevItem.Run);
                if (!string.Equals(lang, cachedLang, StringComparison.Ordinal))
                {
                    cachedLang = lang;
                    var dict = CssHyphenator.ResolveDictionary(lang);
                    cachedBreaks = dict?.FindBreaks(wordRunes);
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
                if (!prevItem.Source.Text.IsAllowedAutoHyphen(prevItem.SourceIndex, before, after))
                    continue;

                var change = breakOpp.Change;
                var tailDeleteLength = 0;
                CssInlineSource? prevTailSource = null;
                var prevTailDeleteLength = 0;
                if (change.TailDeleteRunes > 0)
                {
                    var deleteStart = Math.Max(0, pos - change.TailDeleteRunes);
                    for (var k = pos - 1; k >= deleteStart; k--)
                    {
                        var item = items[first + k];
                        var itemLen = item.SourceEnd - item.SourceIndex;
                        if (ReferenceEquals(item.Source, prevItem.Source))
                        {
                            tailDeleteLength += itemLen;
                        }
                        else if (prevTailSource is null || ReferenceEquals(item.Source, prevTailSource))
                        {
                            prevTailSource = item.Source;
                            prevTailDeleteLength += itemLen;
                        }
                    }
                }

                var leadDeleteLength = 0;
                if (change.LeadDeleteRunes > 0)
                {
                    var deleteEnd = Math.Min(count - 1, pos + change.LeadDeleteRunes - 1);
                    for (var k = pos; k <= deleteEnd; k++)
                    {
                        var item = items[first + k];
                        if (ReferenceEquals(item.Source, nextItem.Source))
                            leadDeleteLength += item.SourceEnd - item.SourceIndex;
                    }
                }

                _inlineHyphens[(prevItem.Source, prevItem.SourceEnd - 1)] = new InlineHyphenBreak(
                    Allowed: true,
                    IsExplicitSoftHyphen: false,
                    TailDeleteLength: tailDeleteLength,
                    TailReplaceText: change.TailReplace ?? string.Empty,
                    PrevTailSource: prevTailSource,
                    PrevTailDeleteLength: prevTailDeleteLength,
                    LeadSource: nextItem.Source,
                    LeadStartIndex: nextItem.SourceIndex,
                    LeadDeleteLength: leadDeleteLength,
                    LeadReplaceText: change.LeadReplace ?? string.Empty);
            }

            items.Clear();
        }
    }
}
