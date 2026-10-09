using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private Dictionary<int, Rune>? _textTransformClassification;

    private bool HasAuthoredWordSpaceTransformOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.WordSpaceTransformProperty))
                return true;
        return false;
    }

    private void RebuildTextTransform()
    {
        _textTransformClassification = null;
        if (_visualText.Length == 0) return;
        var blockMode = (CssTextTransformMode)GetValue(CssFlowProperties.TextTransformProperty)!;
        var blockWordSpaceMode = (CssWordSpaceTransform)GetValue(
            CssFlowProperties.WordSpaceTransformProperty)!;
        var ranges = GetInlineTextRanges();
        if (blockMode == CssTextTransformMode.None &&
            blockWordSpaceMode == CssWordSpaceTransform.None &&
            !ranges.Any(static range =>
                (CssTextTransformMode)range.Run.GetValue(
                    CssFlowProperties.TextTransformProperty)! != CssTextTransformMode.None ||
                (CssWordSpaceTransform)range.Run.GetValue(
                    CssFlowProperties.WordSpaceTransformProperty)! != CssWordSpaceTransform.None))
            return;

        // The layout and interaction indices remain in source UTF-16 space.
        // Rebuild only paint text and its source-boundary map; this also handles
        // full case mappings such as ß -> SS and İ -> i + combining dot.
        var transformed = new StringBuilder(_paintText.Length);
        var classification = _visualText.ToCharArray();
        Dictionary<int, Rune>? classificationOverrides = null;
        var offsets = new int[_visualText.Length + 1];
        var finalSigma = _visualText.Contains('\u03A3') ? ComputeFinalSigma() : null;
        var rangeIndex = 0;
        Run? activeRun = null;
        var mode = blockMode;
        var wordSpaceMode = blockWordSpaceMode;
        var explicitBreaks = GetExplicitLineBreaks();
        var culture = CaseCulture(Language.IetfLanguageTag);
        var turkic = UsesTurkicLatinCasing(Language.IetfLanguageTag);
        var lithuanian = UsesLithuanianCasing(Language.IetfLanguageTag);
        var titlecasedSource = new bool[_visualText.Length];
        var insideWord = false;
        var firstLetterPending = false;
        for (var index = 0; index < _visualText.Length;)
        {
            var validRune = Rune.TryGetRuneAt(_visualText, index, out var rune);
            if (!validRune) rune = Rune.ReplacementChar;
            var length = validRune ? rune.Utf16SequenceLength : 1;
            var paintPiece = PaintSlice(index, length);
            var paintStart = transformed.Length;
            if (paintPiece.Length > 0)
            {
                while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= index)
                    rangeIndex++;
                var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= index
                    ? ranges[rangeIndex].Run : null;
                if (!ReferenceEquals(run, activeRun))
                {
                    activeRun = run;
                    mode = run is null ? blockMode
                        : (CssTextTransformMode)run.GetValue(CssFlowProperties.TextTransformProperty)!;
                    wordSpaceMode = run is null ? blockWordSpaceMode
                        : (CssWordSpaceTransform)run.GetValue(
                            CssFlowProperties.WordSpaceTransformProperty)!;
                    var language = run?.Language.IetfLanguageTag ?? Language.IetfLanguageTag;
                    culture = CaseCulture(language);
                    turkic = UsesTurkicLatinCasing(language);
                    lithuanian = UsesLithuanianCasing(language);
                }
                var firstLetter = UpdateCapitalizationWordState(rune, index + length,
                    ref insideWord, ref firstLetterPending);
                titlecasedSource[index] = validRune && firstLetter &&
                    (mode & CssTextTransformMode.CaseMask) == CssTextTransformMode.Capitalize &&
                    Rune.GetUnicodeCategory(rune) == UnicodeCategory.LowercaseLetter;
                var expandedSeparator = validRune && rune.Value == 0x200B &&
                    wordSpaceMode != CssWordSpaceTransform.None &&
                    !HasForcedBreakAdjacentTo(index, explicitBreaks);
                if (expandedSeparator)
                    paintPiece = (wordSpaceMode & CssWordSpaceTransform.IdeographicSpace) != 0
                        ? "\u3000" : " ";
                var result = validRune
                    ? TransformCase(rune, paintPiece, mode, firstLetter,
                        culture, turkic, lithuanian, titlecasedSource, finalSigma, index, length)
                    : paintPiece;
                if ((mode & (CssTextTransformMode.FullWidth | CssTextTransformMode.FullSizeKana)) != 0)
                    result = TransformWidthAndKana(result, mode, expandedSeparator);
                transformed.Append(result);
                // Keep the classification string in source-index space while
                // letting one-to-one transformations affect line breaking.
                if (result.Length > 0 && Rune.TryGetRuneAt(result, 0, out var classifiedRune) &&
                    classifiedRune.Utf16SequenceLength == result.Length)
                {
                    if (result.Length == length)
                        result.CopyTo(0, classification, index, length);
                    else
                        (classificationOverrides ??= new Dictionary<int, Rune>())[index] = classifiedRune;
                }
            }
            for (var unit = 1; unit <= length; unit++)
                offsets[index + unit] = unit == length ? transformed.Length : paintStart;
            index += length;
        }

        var paint = transformed.ToString();
        _visualText = new string(classification);
        _textTransformClassification = classificationOverrides;
        if (string.Equals(paint, _paintText, StringComparison.Ordinal)) return;
        _paintText = paint;
        _paintOffsets = offsets;
        _hasPaintTextChanges = true;
    }

    private bool HasForcedBreakAdjacentTo(int index, bool[]? explicitBreaks)
        => index > 0 && IsForcedBreakAt(index - 1, explicitBreaks) ||
           index + 1 < _visualText.Length && IsForcedBreakAt(index + 1, explicitBreaks);

    private bool IsForcedBreakAt(int index, bool[]? explicitBreaks)
        => explicitBreaks?[index] == true || _visualText[index] is '\r' or '\n' or '\f';

    private bool UpdateCapitalizationWordState(Rune rune, int next,
        ref bool insideWord, ref bool firstLetterPending)
    {
        var category = Rune.GetUnicodeCategory(rune);
        var isWordUnit = Rune.IsLetterOrDigit(rune) || category is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation ||
            (rune.Value is '\'' or 0x2019) && insideWord &&
            NextVisibleRuneIsLetter(next);
        if (!isWordUnit)
        {
            insideWord = false;
            firstLetterPending = false;
            return false;
        }
        if (!insideWord)
        {
            insideWord = true;
            firstLetterPending = true;
        }
        if (!Rune.IsLetter(rune) || !firstLetterPending) return false;
        firstLetterPending = false;
        return true;
    }

    private bool NextVisibleRuneIsLetter(int start)
    {
        for (var index = start; index < _visualText.Length;)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune)) return false;
            var length = rune.Utf16SequenceLength;
            if (PaintSlice(index, length).Length > 0) return Rune.IsLetter(rune);
            index += length;
        }
        return false;
    }

    private string TransformCase(Rune rune, string paintPiece, CssTextTransformMode mode,
        bool firstLetter, CultureInfo culture, bool turkic, bool lithuanian,
        bool[] titlecasedSource, bool[]? finalSigma, int index, int length)
    {
        var caseMode = mode & CssTextTransformMode.CaseMask;
        if (lithuanian && rune.Value == 0x0307 &&
            AfterSoftDotted(index, out var softDottedStart) &&
            (caseMode == CssTextTransformMode.Uppercase ||
             caseMode == CssTextTransformMode.Capitalize && titlecasedSource[softDottedStart]))
            return string.Empty;

        return caseMode switch
        {
            CssTextTransformMode.Uppercase =>
                turkic && rune.Value == 'i' ? "\u0130" :
                CssUnicodeSpecialCasing.Upper(rune.Value) ?? paintPiece.ToUpper(culture),
            CssTextTransformMode.Lowercase =>
                Lowercase(rune, paintPiece, culture, turkic, lithuanian,
                    finalSigma, index, length),
            CssTextTransformMode.Capitalize when firstLetter &&
                Rune.GetUnicodeCategory(rune) == UnicodeCategory.LowercaseLetter =>
                turkic && rune.Value == 'i' ? "\u0130" :
                CssUnicodeSpecialCasing.Title(rune.Value) ?? culture.TextInfo.ToTitleCase(paintPiece),
            _ => paintPiece,
        };
    }

    private string TransformWidthAndKana(string value, CssTextTransformMode mode,
        bool expandedSeparator)
    {
        if (value.Length == 0) return value;
        var output = new StringBuilder(value.Length);
        var preserveSpaces = expandedSeparator || _whiteSpaceMode is
            CssWhiteSpaceCollapse.Preserve or CssWhiteSpaceCollapse.PreserveSpaces or
            CssWhiteSpaceCollapse.BreakSpaces;
        foreach (var rune in value.EnumerateRunes())
        {
            var codepoint = rune.Value;
            if ((mode & CssTextTransformMode.FullWidth) != 0 &&
                (codepoint != ' ' || preserveSpaces))
                codepoint = CssUnicodeWidthMappings.FullWidth(codepoint);
            if ((mode & CssTextTransformMode.FullSizeKana) != 0)
                codepoint = CssSmallKanaMappings.FullSize(codepoint);
            output.Append(char.ConvertFromUtf32(codepoint));
        }
        return output.ToString();
    }

    private string Lowercase(Rune rune, string paintPiece, CultureInfo culture,
        bool turkic, bool lithuanian, bool[]? finalSigma, int index, int length)
    {
        if (turkic)
        {
            if (rune.Value == 0x0130) return "i";
            if (rune.Value == 'I')
                return BeforeDot(index + length) ? "i" : "\u0131";
            if (rune.Value == 0x0307 && AfterI(index))
                return string.Empty;
        }
        if (lithuanian)
        {
            if (rune.Value == 0x00CC) return "i\u0307\u0300";
            if (rune.Value == 0x00CD) return "i\u0307\u0301";
            if (rune.Value == 0x0128) return "i\u0307\u0303";
            if (MoreAbove(index + length))
            {
                if (rune.Value == 'I') return "i\u0307";
                if (rune.Value == 'J') return "j\u0307";
                if (rune.Value == 0x012E) return "\u012F\u0307";
            }
        }
        if (rune.Value == 0x03A3)
            return finalSigma?[index] == true ? "\u03C2" : "\u03C3";
        return CssUnicodeSpecialCasing.Lower(rune.Value) ?? paintPiece.ToLower(culture);
    }

    private bool MoreAbove(int index)
    {
        while (index < _visualText.Length)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune)) return false;
            if (CssUnicodeCasingData.IsAbove(rune.Value)) return true;
            if (!CssUnicodeCasingData.IsOtherCombining(rune.Value)) return false;
            index += rune.Utf16SequenceLength;
        }
        return false;
    }

    private bool BeforeDot(int index)
    {
        while (index < _visualText.Length)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune)) return false;
            if (rune.Value == 0x0307) return true;
            if (!CssUnicodeCasingData.IsOtherCombining(rune.Value)) return false;
            index += rune.Utf16SequenceLength;
        }
        return false;
    }

    private bool AfterI(int index)
    {
        while (TryPreviousSourceRune(ref index, out var rune))
        {
            if (rune.Value == 'I') return true;
            if (!CssUnicodeCasingData.IsOtherCombining(rune.Value)) return false;
        }
        return false;
    }

    private bool AfterSoftDotted(int index, out int baseIndex)
    {
        while (TryPreviousSourceRune(ref index, out var rune))
        {
            if (CssUnicodeCasingData.IsSoftDotted(rune.Value))
            {
                baseIndex = index;
                return true;
            }
            if (!CssUnicodeCasingData.IsOtherCombining(rune.Value)) break;
        }
        baseIndex = -1;
        return false;
    }

    private bool TryPreviousSourceRune(ref int index, out Rune rune)
    {
        if (index <= 0)
        {
            rune = default;
            return false;
        }
        index--;
        if (index > 0 && char.IsLowSurrogate(_visualText[index]) &&
            char.IsHighSurrogate(_visualText[index - 1])) index--;
        if (Rune.TryGetRuneAt(_visualText, index, out rune)) return true;
        rune = Rune.ReplacementChar;
        return true;
    }

    private bool[] ComputeFinalSigma()
    {
        var preceding = new bool[_visualText.Length];
        var result = new bool[_visualText.Length];
        var cased = false;
        for (var index = 0; index < _visualText.Length;)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune))
                rune = Rune.ReplacementChar;
            preceding[index] = cased;
            if (!CssUnicodeCasingData.IsCaseIgnorable(rune.Value))
                cased = CssUnicodeCasingData.IsCased(rune.Value);
            index += rune.Utf16SequenceLength;
        }
        cased = false;
        for (var index = _visualText.Length; index > 0;)
        {
            var start = index - 1;
            if (char.IsLowSurrogate(_visualText[start]) && start > 0 &&
                char.IsHighSurrogate(_visualText[start - 1])) start--;
            if (!Rune.TryGetRuneAt(_visualText, start, out var rune))
                rune = Rune.ReplacementChar;
            if (rune.Value == 0x03A3 && preceding[start] && !cased)
                result[start] = true;
            if (!CssUnicodeCasingData.IsCaseIgnorable(rune.Value))
                cased = CssUnicodeCasingData.IsCased(rune.Value);
            index = start;
        }
        return result;
    }

    private static bool UsesTurkicLatinCasing(string language)
    {
        var subtags = language.ToLowerInvariant().Split('-');
        if (subtags[0] is not ("tr" or "az")) return false;
        return subtags.Length < 2 || subtags[1].Length != 4 || subtags[1] == "latn";
    }

    private static bool UsesLithuanianCasing(string language)
        => language.Split('-')[0].Equals("lt", StringComparison.OrdinalIgnoreCase);

    private static CultureInfo CaseCulture(string language)
    {
        try { return CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
