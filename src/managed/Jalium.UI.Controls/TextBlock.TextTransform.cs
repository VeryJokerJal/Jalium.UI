using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private Dictionary<int, Rune>? _textTransformClassification;
    private TextTransformContextData? _sharedTransformContext;
    private int _sharedTransformOffset;

    private bool HasAuthoredWordSpaceTransformOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.WordSpaceTransformProperty))
                return true;
        return false;
    }

    internal bool HasTextTransformRequest()
    {
        var blockMode = (CssTextTransformMode)GetValue(CssFlowProperties.TextTransformProperty)!;
        var blockWordSpaceMode = (CssWordSpaceTransform)GetValue(CssFlowProperties.WordSpaceTransformProperty)!;
        if (blockMode != CssTextTransformMode.None || blockWordSpaceMode != CssWordSpaceTransform.None)
            return true;
        foreach (var range in GetInlineTextRanges())
            if ((CssTextTransformMode)range.Run.GetValue(CssFlowProperties.TextTransformProperty)! != CssTextTransformMode.None ||
                (CssWordSpaceTransform)range.Run.GetValue(CssFlowProperties.WordSpaceTransformProperty)! != CssWordSpaceTransform.None)
                return true;
        return false;
    }

    internal void AppendTransformContext(StringBuilder text, List<bool> hidden, List<byte> caseModes)
    {
        var blockCase = (byte)((CssTextTransformMode)GetValue(CssFlowProperties.TextTransformProperty)! & CssTextTransformMode.CaseMask);
        var ranges = GetInlineTextRanges();
        var rangeIndex = 0;
        Run? activeRun = null;
        var activeCase = blockCase;
        var visual = _whitespaceVisualText;
        text.Append(visual);
        for (var index = 0; index < visual.Length;)
        {
            var validRune = Rune.TryGetRuneAt(visual, index, out var rune);
            var length = validRune ? rune.Utf16SequenceLength : 1;
            var isHidden = WhitespacePaintSlice(index, length).Length == 0;
            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= index)
                rangeIndex++;
            var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= index
                ? ranges[rangeIndex].Run : null;
            if (!ReferenceEquals(run, activeRun))
            {
                activeRun = run;
                activeCase = run is null ? blockCase
                    : (byte)((CssTextTransformMode)run.GetValue(CssFlowProperties.TextTransformProperty)! & CssTextTransformMode.CaseMask);
            }
            for (var u = 0; u < length; u++)
            {
                hidden.Add(isHidden);
                caseModes.Add(activeCase);
            }
            index += length;
        }
    }

    private TextTransformContextData BuildOwnTransformContext()
    {
        var visual = _whitespaceVisualText;
        var text = new StringBuilder(visual.Length);
        var hidden = new List<bool>(visual.Length);
        var caseModes = new List<byte>(visual.Length);
        AppendTransformContext(text, hidden, caseModes);
        return new TextTransformContextData(text.ToString(), hidden.ToArray(), caseModes.ToArray());
    }

    internal bool SetSharedTransformContext(TextTransformContextData? context, int offset)
    {
        if (context is null)
        {
            if (_sharedTransformContext is null && _sharedTransformOffset == 0) return false;
            _sharedTransformContext = null;
            _sharedTransformOffset = 0;
            _layoutDirty = true;
            return false;
        }
        if (ReferenceEquals(_sharedTransformContext, context) && _sharedTransformOffset == offset)
            return false;
        _sharedTransformContext = context;
        _sharedTransformOffset = offset;
        if (!HasTextTransformRequest()) return false;
        var oldPaint = _paintText;
        var oldVisual = _visualText;
        var oldOffsets = _paintOffsets;
        RebuildTextTransform();
        if (string.Equals(oldPaint, _paintText, StringComparison.Ordinal) &&
            string.Equals(oldVisual, _visualText, StringComparison.Ordinal) &&
            ((oldOffsets is null && _paintOffsets is null) ||
             (oldOffsets is not null && _paintOffsets is not null && oldOffsets.AsSpan().SequenceEqual(_paintOffsets))))
            return false;
        ClearShapedSpacing();
        ClearNativeBidiLines();
        _inlineFontMeasurements.Clear();
        _textWidthCache.Clear();
        _justifiedLineCache.Clear();
        _paintWrapJustificationCache.Clear();
        _formattedLinesCacheDirty = true;
        RebuildTextCombine();
        RebuildHyphenation();
        RebuildAutospaceReplacement();
        RebuildLinePadding();
        _hasPreservedTabs = (_whiteSpaceMode is CssWhiteSpaceCollapse.Preserve or
            CssWhiteSpaceCollapse.BreakSpaces) && _visualText.Contains('\t') &&
            HasAuthoredWhiteSpace();
        _usesInlineFontLayout = HasInlineFontOverrides();
        RebuildCapsContext();
        RebuildWordSpacing();
        RebuildLetterSpacing();
        RebuildHangingPunctuation();
        PrepareCssClamp();
        return true;
    }

    private void RebuildTextTransform()
    {
        _textTransformClassification = null;
        _visualText = _whitespaceVisualText;
        _paintText = _whitespacePaintText;
        _paintOffsets = _whitespacePaintOffsets;
        _hasPaintTextChanges = _whitespaceHasPaintChanges;
        if (_visualText.Length == 0 || !HasTextTransformRequest()) return;
        var blockMode = (CssTextTransformMode)GetValue(CssFlowProperties.TextTransformProperty)!;
        var blockWordSpaceMode = (CssWordSpaceTransform)GetValue(
            CssFlowProperties.WordSpaceTransformProperty)!;
        var ranges = GetInlineTextRanges();

        // The layout and interaction indices remain in source UTF-16 space.
        // Rebuild only paint text and its source-boundary map; this also handles
        // full case mappings such as ß -> SS and İ -> i + combining dot.
        var transformed = new StringBuilder(_paintText.Length);
        var classification = _visualText.ToCharArray();
        Dictionary<int, Rune>? classificationOverrides = null;
        var offsets = new int[_visualText.Length + 1];
        var context = _sharedTransformContext is { } shared &&
            _sharedTransformOffset >= 0 &&
            _sharedTransformOffset + _visualText.Length <= shared.Length
            ? shared : BuildOwnTransformContext();
        var contextOffset = ReferenceEquals(context, _sharedTransformContext) ? _sharedTransformOffset : 0;
        var rangeIndex = 0;
        Run? activeRun = null;
        var mode = blockMode;
        var wordSpaceMode = blockWordSpaceMode;
        var explicitBreaks = GetExplicitLineBreaks();
        var culture = CaseCulture(Language.IetfLanguageTag);
        var turkic = UsesTurkicLatinCasing(Language.IetfLanguageTag);
        var lithuanian = UsesLithuanianCasing(Language.IetfLanguageTag);
        for (var index = 0; index < _visualText.Length;)
        {
            var validRune = Rune.TryGetRuneAt(_visualText, index, out var rune);
            if (!validRune) rune = Rune.ReplacementChar;
            var length = validRune ? rune.Utf16SequenceLength : 1;
            var paintPiece = WhitespacePaintSlice(index, length);
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
                var contextIndex = contextOffset + index;
                var expandedSeparator = validRune && rune.Value == 0x200B &&
                    wordSpaceMode != CssWordSpaceTransform.None &&
                    !HasForcedBreakAdjacentTo(index, explicitBreaks);
                if (expandedSeparator)
                    paintPiece = (wordSpaceMode & CssWordSpaceTransform.IdeographicSpace) != 0
                        ? "\u3000" : " ";
                var result = validRune
                    ? TransformCase(rune, paintPiece, mode, context, contextIndex,
                        culture, turkic, lithuanian)
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
        => explicitBreaks?[index] == true || IsTextLineBreak(_visualText[index]) || _visualText[index] == '\f';

    private string TransformCase(Rune rune, string paintPiece, CssTextTransformMode mode,
        TextTransformContextData context, int contextIndex, CultureInfo culture, bool turkic, bool lithuanian)
    {
        var caseMode = mode & CssTextTransformMode.CaseMask;
        if (lithuanian && rune.Value == 0x0307 &&
            (caseMode == CssTextTransformMode.Uppercase && context.IsAfterSoftDotted(contextIndex) ||
             caseMode == CssTextTransformMode.Capitalize && context.IsAfterTitlecasedSoftDotted(contextIndex)))
            return string.Empty;

        return caseMode switch
        {
            CssTextTransformMode.Uppercase =>
                turkic && rune.Value == 'i' ? "\u0130" :
                CssUnicodeSpecialCasing.Upper(rune.Value) ?? paintPiece.ToUpper(culture),
            CssTextTransformMode.Lowercase =>
                Lowercase(rune, paintPiece, context, contextIndex, culture, turkic, lithuanian),
            CssTextTransformMode.Capitalize when context.IsFirstLetter(contextIndex) &&
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

    private static string Lowercase(Rune rune, string paintPiece, TextTransformContextData context,
        int contextIndex, CultureInfo culture, bool turkic, bool lithuanian)
    {
        if (turkic)
        {
            if (rune.Value == 0x0130) return "i";
            if (rune.Value == 'I')
                return context.IsBeforeDot(contextIndex) ? "i" : "\u0131";
            if (rune.Value == 0x0307 && context.IsAfterI(contextIndex))
                return string.Empty;
        }
        if (lithuanian)
        {
            if (rune.Value == 0x00CC) return "i\u0307\u0300";
            if (rune.Value == 0x00CD) return "i\u0307\u0301";
            if (rune.Value == 0x0128) return "i\u0307\u0303";
            if (context.IsMoreAbove(contextIndex))
            {
                if (rune.Value == 'I') return "i\u0307";
                if (rune.Value == 'J') return "j\u0307";
                if (rune.Value == 0x012E) return "\u012F\u0307";
            }
        }
        if (rune.Value == 0x03A3)
            return context.IsFinalSigma(contextIndex) ? "\u03C2" : "\u03C3";
        return CssUnicodeSpecialCasing.Lower(rune.Value) ?? paintPiece.ToLower(culture);
    }

    internal sealed class TextTransformContextData
    {
        private const byte FlagFirstLetter = 1;
        private const byte FlagFinalSigma = 2;
        private const byte FlagBeforeDot = 4;
        private const byte FlagAfterI = 8;
        private const byte FlagMoreAbove = 16;
        private const byte FlagAfterSoftDotted = 32;
        private const byte FlagAfterTitlecasedSoftDotted = 64;

        private readonly byte[] _flags;
        internal int Length => _flags.Length;

        internal bool IsFirstLetter(int index) => (_flags[index] & FlagFirstLetter) != 0;
        internal bool IsFinalSigma(int index) => (_flags[index] & FlagFinalSigma) != 0;
        internal bool IsBeforeDot(int index) => (_flags[index] & FlagBeforeDot) != 0;
        internal bool IsAfterI(int index) => (_flags[index] & FlagAfterI) != 0;
        internal bool IsMoreAbove(int index) => (_flags[index] & FlagMoreAbove) != 0;
        internal bool IsAfterSoftDotted(int index) => (_flags[index] & FlagAfterSoftDotted) != 0;
        internal bool IsAfterTitlecasedSoftDotted(int index) => (_flags[index] & FlagAfterTitlecasedSoftDotted) != 0;

        internal TextTransformContextData(string text, bool[] hidden, byte[] caseModes)
        {
            _flags = new byte[text.Length];
            bool insideWord = false, firstLetterPending = false;
            bool casedBefore = false, afterI = false, afterSoftDotted = false, afterTitlecasedSoftDotted = false;
            for (var index = 0; index < text.Length;)
            {
                var validRune = Rune.TryGetRuneAt(text, index, out var rune);
                if (!validRune) rune = Rune.ReplacementChar;
                var length = validRune ? rune.Utf16SequenceLength : 1;
                if (!hidden[index])
                {
                    var firstLetter = UpdateCapitalizationWordState(text, hidden, rune, index + length,
                        ref insideWord, ref firstLetterPending);
                    byte f = 0;
                    if (firstLetter) f |= FlagFirstLetter;
                    if (rune.Value == 0x03A3 && casedBefore) f |= FlagFinalSigma;
                    if (afterI) f |= FlagAfterI;
                    if (afterSoftDotted) f |= FlagAfterSoftDotted;
                    if (afterTitlecasedSoftDotted) f |= FlagAfterTitlecasedSoftDotted;
                    _flags[index] = f;
                    var value = rune.Value;
                    if (!CssUnicodeCasingData.IsCaseIgnorable(value))
                        casedBefore = CssUnicodeCasingData.IsCased(value);
                    if (!CssUnicodeCasingData.IsOtherCombining(value))
                    {
                        afterI = value == 'I';
                        afterSoftDotted = CssUnicodeCasingData.IsSoftDotted(value);
                        var caseMode = (CssTextTransformMode)caseModes[index];
                        afterTitlecasedSoftDotted = afterSoftDotted &&
                            (caseMode == CssTextTransformMode.Uppercase ||
                             caseMode == CssTextTransformMode.Capitalize && firstLetter &&
                             Rune.GetUnicodeCategory(rune) == UnicodeCategory.LowercaseLetter);
                    }
                }
                index += length;
            }

            bool casedAfter = false, beforeDot = false, moreAbove = false;
            for (var index = text.Length; index > 0;)
            {
                var start = index - 1;
                if (char.IsLowSurrogate(text[start]) && start > 0 && char.IsHighSurrogate(text[start - 1]))
                    start--;
                if (!Rune.TryGetRuneAt(text, start, out var rune))
                    rune = Rune.ReplacementChar;
                if (!hidden[start])
                {
                    var f = _flags[start];
                    if (casedAfter) f = (byte)(f & ~FlagFinalSigma);
                    if (beforeDot) f |= FlagBeforeDot;
                    if (moreAbove) f |= FlagMoreAbove;
                    _flags[start] = f;
                    var value = rune.Value;
                    if (!CssUnicodeCasingData.IsCaseIgnorable(value))
                        casedAfter = CssUnicodeCasingData.IsCased(value);
                    if (value == 0x0307)
                        beforeDot = true;
                    else if (!CssUnicodeCasingData.IsOtherCombining(value))
                        beforeDot = false;
                    if (CssUnicodeCasingData.IsAbove(value))
                        moreAbove = true;
                    else if (!CssUnicodeCasingData.IsOtherCombining(value))
                        moreAbove = false;
                }
                index = start;
            }
        }

        private static bool UpdateCapitalizationWordState(string text, bool[] hidden, Rune rune, int next,
            ref bool insideWord, ref bool firstLetterPending)
        {
            var category = Rune.GetUnicodeCategory(rune);
            var isWordUnit = Rune.IsLetterOrDigit(rune) || category is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation ||
                (rune.Value is '\'' or 0x2019) && insideWord &&
                NextVisibleRuneIsLetter(text, hidden, next);
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

        private static bool NextVisibleRuneIsLetter(string text, bool[] hidden, int start)
        {
            for (var index = start; index < text.Length;)
            {
                if (!Rune.TryGetRuneAt(text, index, out var rune)) return false;
                var length = rune.Utf16SequenceLength;
                if (!hidden[index]) return Rune.IsLetter(rune);
                index += length;
            }
            return false;
        }
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
