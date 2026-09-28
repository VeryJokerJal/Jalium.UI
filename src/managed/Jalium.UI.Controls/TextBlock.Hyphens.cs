using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool IsAllowedSoftHyphen(int sourceIndex)
        => sourceIndex >= 0 && sourceIndex < _visualText.Length &&
           _visualText[sourceIndex] == '\u00AD' &&
           GetHyphensAt(sourceIndex) != CssHyphens.None &&
           MeetsHyphenateLimitChars(sourceIndex);

    private CssHyphens GetHyphensAt(int sourceIndex)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= sourceIndex && sourceIndex < range.End)
                return (CssHyphens)range.Run.GetValue(CssFlowProperties.HyphensProperty)!;
        return (CssHyphens)GetValue(CssFlowProperties.HyphensProperty)!;
    }

    private Run? GetSoftHyphenRun(int sourceIndex)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= sourceIndex && sourceIndex < range.End)
                return range.Run;
        return null;
    }

    private bool MeetsHyphenateLimitChars(int sourceIndex)
    {
        var run = GetSoftHyphenRun(sourceIndex);
        var limits = run is null
            ? (CssHyphenateLimitChars)GetValue(CssFlowProperties.HyphenateLimitCharsProperty)!
            : (CssHyphenateLimitChars)run.GetValue(CssFlowProperties.HyphenateLimitCharsProperty)!;

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
            var category = Rune.GetUnicodeCategory(rune);
            if (rune.Value != 0x00AD && category is not
                (UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation or
                 UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
                 UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or
                 UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation))
                count++;
            index += rune.Utf16SequenceLength;
        }
        return count;
    }

    private double MeasureSoftHyphenWidth(int sourceIndex)
    {
        var text = GetHyphenationStringAt(sourceIndex);
        if (text.Length == 0) return 0;
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

    private void DrawVisibleSoftHyphen(DrawingContext context, in TextLayoutLine line,
        ref double x, double lineY)
    {
        var sourceIndex = line.StartIndex + line.Length - 1;
        var text = GetHyphenationStringAt(sourceIndex);
        if (text.Length == 0) return;
        var run = GetSoftHyphenRun(sourceIndex);
        var measurement = run is null ? MeasureText(text) : MeasureRunText(run, text);
        DrawMeasuredFragment(context, run, measurement, ref x, lineY, line);
    }
}
