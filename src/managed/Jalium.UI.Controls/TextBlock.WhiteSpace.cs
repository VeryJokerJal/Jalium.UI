using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // Classification preserves source offsets for wrapping. Paint text omits
    // collapsed characters, and the offset map keeps selection in source space.
    private string _visualText = string.Empty;
    private string _paintText = string.Empty;
    private int[]? _paintOffsets;
    private bool _hasPaintTextChanges;
    private string _whitespaceVisualText = string.Empty;
    private string _whitespacePaintText = string.Empty;
    private int[]? _whitespacePaintOffsets;
    private bool _whitespaceHasPaintChanges;
    private CssWhiteSpaceCollapse _whiteSpaceMode;
    private bool _countTrailingWhitespace;

    private double UsedCssWhitespaceAdvance(int start, int length, double physicalWidth,
        double availableWidth, bool forcedEnd, double tabOrigin)
    {
        if (!HasAuthoredWhiteSpace() || length == 0) return physicalWidth;
        var end = start + length; var tail = end;
        while (tail > start && CombinedUnitAt(tail - 1) is null && _visualText[tail - 1] is ' ' or '\t') tail--;
        if (tail == end) return physicalWidth;
        var collapse = _whiteSpaceMode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks;
        var preserveWrap = _whiteSpaceMode == CssWhiteSpaceCollapse.Preserve && EffectiveTextWrapping() != TextWrapping.NoWrap;
        if (!collapse && !preserveWrap) return physicalWidth;
        var padding = GetLineStartPadding(start, end) + GetLineEndPadding(start, end);
        if (preserveWrap && forcedEnd && !double.IsNaN(availableWidth) &&
            physicalWidth + padding <= availableWidth + .000001) return physicalWidth;
        var prefix = MeasureInlineRange(start, tail - start, includeTrailingWhitespace: true, tabOrigin: tabOrigin);
        return preserveWrap && forcedEnd ? Math.Max(prefix, Math.Min(physicalWidth, availableWidth - padding)) : prefix;
    }

    private void RebuildVisualWhitespace()
    {
        _whiteSpaceMode = (CssWhiteSpaceCollapse)GetValue(CssFlowProperties.WhiteSpaceProperty)!;
        _visualText = _displayText;
        _paintText = _displayText;
        _paintOffsets = null;
        _hasPaintTextChanges = false;
        SaveWhitespaceState();
        var explicitBreaks = GetExplicitLineBreaks();
        var trimMask = GetWhiteSpaceTrimMask(explicitBreaks);
        if (_combinedWhiteSpaceMask is { } combinedTrim)
        {
            trimMask ??= new bool[combinedTrim.Length];
            for (int i = 0; i < combinedTrim.Length; i++) trimMask[i] |= combinedTrim[i];
        }
        if (InlineDisplayMask() is { } displayMask)
        {
            trimMask ??= new bool[displayMask.Length];
            for (var i=0;i<displayMask.Length;i++) trimMask[i] |= displayMask[i];
        }
        var hasCssWhiteSpace = HasAuthoredWhiteSpace();
        _countTrailingWhitespace = hasCssWhiteSpace &&
            (_whiteSpaceMode == CssWhiteSpaceCollapse.BreakSpaces ||
             _whiteSpaceMode == CssWhiteSpaceCollapse.Preserve &&
             (CssTextWrapMode)GetValue(CssFlowProperties.TextWrapModeProperty)! == CssTextWrapMode.NoWrap);
        if (_displayText.Length == 0 ||
            !hasCssWhiteSpace && _combinedTextUnits.Length == 0 && trimMask is null && !_displayText.Contains('\u00AD')) return;

        var collapse = _whiteSpaceMode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks;
        var preserveBreaks = _whiteSpaceMode == CssWhiteSpaceCollapse.PreserveBreaks;
        var discard = _whiteSpaceMode == CssWhiteSpaceCollapse.Discard;
        var preserveSpaces = _whiteSpaceMode == CssWhiteSpaceCollapse.PreserveSpaces;
        char[]? output = trimMask is null ? null : _displayText.ToCharArray();
        bool[]? hidden = trimMask;
        if (trimMask is not null)
            for (var index = 0; index < trimMask.Length; index++)
                if (trimMask[index]) output![index] = '\uFEFF';
        var atLineStart = !_cssInlineWhitespace;
        for (var index = 0; index < _displayText.Length;)
        {
            if (trimMask?[index] == true)
            {
                index++;
                continue;
            }
            if (!hasCssWhiteSpace && _combinedTextUnits.Length > 0 && CombinedUnitAt(index) is null)
            {
                index++;
                continue;
            }
            var value = _displayText[index];
            if (CombinedUnitAt(index) is not null && (IsTextLineBreak(value) || value == '\f'))
            {
                output ??= _displayText.ToCharArray(); hidden ??= new bool[_displayText.Length];
                output[index] = '\uFEFF'; hidden[index++] = true;
                continue;
            }
            if (value == '\u00AD')
            {
                // A soft hyphen remains in source space for line breaking and
                // selection, but has no paint advance unless that line breaks here.
                hidden ??= new bool[_displayText.Length];
                hidden[index] = true;
                index++;
                continue;
            }
            if (explicitBreaks?[index] == true ||
                preserveBreaks && IsSourceSegmentBreak(value))
            {
                if (value == '\f')
                {
                    output ??= _displayText.ToCharArray();
                    output[index] = '\n';
                }
                index++;
                atLineStart = true;
                continue;
            }

            if (discard && IsCssCollapsibleWhiteSpace(value))
            {
                output ??= _displayText.ToCharArray();
                hidden ??= new bool[_displayText.Length];
                output[index] = '\uFEFF';
                hidden[index] = true;
                index++;
                continue;
            }

            if (preserveSpaces && (value == '\t' || IsSourceSegmentBreak(value)))
            {
                output ??= _displayText.ToCharArray();
                output[index] = ' ';
                if (value == '\r' && index + 1 < _displayText.Length &&
                    _displayText[index + 1] == '\n' && explicitBreaks?[index + 1] != true)
                {
                    output[index + 1] = '\uFEFF';
                    hidden ??= new bool[_displayText.Length];
                    hidden[index + 1] = true;
                    index++;
                }
                index++;
                atLineStart = false;
                continue;
            }

            if (collapse && IsCssCollapsibleWhiteSpace(value))
            {
                var start = index;
                while (index < _displayText.Length &&
                       trimMask?[index] != true &&
                       (_combinedTextUnits.Length == 0 || hasCssWhiteSpace || CombinedUnitAt(index) is not null) &&
                       IsCssCollapsibleWhiteSpace(_displayText[index]) &&
                       explicitBreaks?[index] != true &&
                       (!preserveBreaks || !IsSourceSegmentBreak(_displayText[index])))
                    index++;
                var beforeLineEnd = index == _displayText.Length && !_cssInlineWhitespace ||
                    index < _displayText.Length && (explicitBreaks?[index] == true ||
                    IsUnicodeForcedLineBreak(_displayText[index]) ||
                    preserveBreaks && IsSourceSegmentBreak(_displayText[index]));
                output ??= _displayText.ToCharArray();
                for (var offset = start; offset < index; offset++)
                {
                    if (!atLineStart && !beforeLineEnd && offset == start)
                        output[offset] = ' ';
                    else
                    {
                        output[offset] = '\uFEFF';
                        hidden ??= new bool[_displayText.Length];
                        hidden[offset] = true;
                    }
                }
                continue;
            }

            if (value == '\f')
            {
                output ??= _displayText.ToCharArray();
                output[index] = '\n';
                atLineStart = true;
            }
            else if (IsTextLineBreak(value))
                atLineStart = true;
            else
                atLineStart = false;
            index++;
        }

        if (output is null && hidden is null) return;
        _visualText = output is null ? _displayText : new string(output);
        _hasPaintTextChanges = hidden is not null ||
            !string.Equals(_visualText, _displayText, StringComparison.Ordinal);
        if (hidden is null)
        {
            _paintText = _visualText;
            SaveWhitespaceState();
            return;
        }

        var source = output ?? _displayText.ToCharArray();
        var painted = new char[source.Length];
        var offsets = new int[source.Length + 1];
        var paintLength = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (!hidden[index]) painted[paintLength++] = source[index];
            offsets[index + 1] = paintLength;
        }
        _paintText = new string(painted, 0, paintLength);
        _paintOffsets = offsets;
        SaveWhitespaceState();
    }

    private void SaveWhitespaceState()
    {
        _whitespaceVisualText = _visualText;
        _whitespacePaintText = _paintText;
        _whitespacePaintOffsets = _paintOffsets;
        _whitespaceHasPaintChanges = _hasPaintTextChanges;
    }

    private string WhitespacePaintSlice(int start, int length)
    {
        if (length <= 0) return string.Empty;
        if (_whitespacePaintOffsets is null) return _whitespacePaintText.Substring(start, length);
        var paintStart = _whitespacePaintOffsets[start];
        return _whitespacePaintText.Substring(paintStart, _whitespacePaintOffsets[start + length] - paintStart);
    }

    private string PaintSlice(int start, int length)
    {
        if (length <= 0) return string.Empty;
        if (_paintOffsets is null) return _paintText.Substring(start, length);
        var paintStart = _paintOffsets[start];
        return _paintText.Substring(paintStart, _paintOffsets[start + length] - paintStart);
    }

    private static bool IsCssCollapsibleWhiteSpace(char value)
        => value is ' ' or '\t' || IsSourceSegmentBreak(value);

    private bool[]? GetWhiteSpaceTrimMask(bool[]? explicitBreaks)
    {
        if (_displayText.Length == 0) return null;
        var trim = (CssWhiteSpaceTrim)GetValue(CssFlowProperties.WhiteSpaceTrimProperty)!;
        var hasInlineTrim = _inlines is not null && _inlines.Any(HasInlineWhiteSpaceTrim);
        if ((trim & CssWhiteSpaceTrim.DiscardInner) == 0 && !hasInlineTrim) return null;
        var mask = new bool[_displayText.Length];
        if ((trim & CssWhiteSpaceTrim.DiscardInner) != 0)
        {
            // Block containers keep indentation after their first and before
            // their last segment break; plain edge spaces remain untouched.
            var firstContent = 0;
            while (firstContent < _displayText.Length &&
                   IsTrimmableWhiteSpace(firstContent, explicitBreaks)) firstContent++;
            var lastLeadingBreak = -1;
            for (var index = 0; index < firstContent; index++)
                if (IsSourceSegmentBreak(_displayText[index])) lastLeadingBreak = index;
            if (lastLeadingBreak >= 0) MarkTrim(mask, 0, lastLeadingBreak + 1);

            var lastContent = _displayText.Length;
            while (lastContent > 0 && IsTrimmableWhiteSpace(lastContent - 1, explicitBreaks))
                lastContent--;
            for (var index = lastContent; index < _displayText.Length; index++)
            {
                if (!IsSourceSegmentBreak(_displayText[index])) continue;
                MarkTrim(mask, index, _displayText.Length);
                break;
            }
        }

        if (_inlines is not null)
        {
            var offset = 0;
            foreach (var inline in _inlines)
                CollectInlineWhiteSpaceTrim(inline, ref offset, mask, explicitBreaks);
        }
        return Array.IndexOf(mask, true) < 0 ? null : mask;
    }

    private static bool HasInlineWhiteSpaceTrim(Inline inline)
    {
        if (inline is LineBreak) return false;
        if ((CssWhiteSpaceTrim)inline.GetValue(CssFlowProperties.WhiteSpaceTrimProperty)! !=
            CssWhiteSpaceTrim.None) return true;
        return inline is Span span && span.Inlines.Any(HasInlineWhiteSpaceTrim);
    }

    private void CollectInlineWhiteSpaceTrim(Inline inline, ref int offset,
        bool[] mask, bool[]? explicitBreaks)
    {
        var start = offset;
        switch (inline)
        {
            case Run run:
                offset += run.Text.Length;
                break;
            case LineBreak:
                offset++;
                return;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectInlineWhiteSpaceTrim(child, ref offset, mask, explicitBreaks);
                break;
            default:
                return;
        }
        var end = Math.Min(offset, _displayText.Length);
        var trim = (CssWhiteSpaceTrim)inline.GetValue(CssFlowProperties.WhiteSpaceTrimProperty)!;
        if ((trim & CssWhiteSpaceTrim.DiscardBefore) != 0)
            for (var index = start - 1; index >= 0 && IsTrimmableWhiteSpace(index, explicitBreaks); index--)
                mask[index] = true;
        if ((trim & CssWhiteSpaceTrim.DiscardAfter) != 0)
            for (var index = end; index < _displayText.Length &&
                 IsTrimmableWhiteSpace(index, explicitBreaks); index++) mask[index] = true;
        if ((trim & CssWhiteSpaceTrim.DiscardInner) == 0) return;
        for (var index = start; index < end && IsTrimmableWhiteSpace(index, explicitBreaks); index++)
            mask[index] = true;
        for (var index = end - 1; index >= start && IsTrimmableWhiteSpace(index, explicitBreaks); index--)
            mask[index] = true;
    }

    private bool IsTrimmableWhiteSpace(int index, bool[]? explicitBreaks)
        => explicitBreaks?[index] != true && IsCssCollapsibleWhiteSpace(_displayText[index]);


    private static void MarkTrim(bool[] mask, int start, int end)
    {
        for (var index = start; index < end; index++) mask[index] = true;
    }

    private bool[]? GetExplicitLineBreaks()
    {
        if (!_inlinesExplicitlyModified || _inlines is null) return null;
        var positions = new List<int>();
        var offset = 0;
        foreach (var inline in _inlines)
            CollectExplicitLineBreaks(inline, ref offset, positions);
        if (positions.Count == 0) return null;
        var breaks = new bool[_displayText.Length];
        foreach (var position in positions) breaks[position] = true;
        return breaks;
    }

    private static void CollectExplicitLineBreaks(Inline inline, ref int offset, List<int> positions)
    {
        switch (inline)
        {
            case Run run:
                offset += run.Text.Length;
                break;
            case LineBreak:
                if (!CssDisplayProperties.SuppressesBoxTree(inline)) positions.Add(offset);
                offset++;
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectExplicitLineBreaks(child, ref offset, positions);
                break;
        }
    }

    private bool HasAuthoredWhiteSpace()
    {
        if (this is CssGeneratedText || UsesCssTextBody || _cssInlineWhitespace) return true;
        var trim = GetValueSourceInternal(CssFlowProperties.WhiteSpaceTrimProperty).BaseValueSource;
        if (trim is not BaseValueSource.Default and not BaseValueSource.Inherited) return true;
        for (DependencyObject? node = this; node is not null;)
        {
            var collapse = node.GetValueSourceInternal(CssFlowProperties.WhiteSpaceProperty).BaseValueSource;
            var wrap = node.GetValueSourceInternal(CssFlowProperties.TextWrapModeProperty).BaseValueSource;
            var wrapStyle = node.GetValueSourceInternal(CssFlowProperties.TextWrapStyleProperty).BaseValueSource;
            var tabSize = node.GetValueSourceInternal(CssFlowProperties.TabSizeProperty).BaseValueSource;
            if (collapse is not BaseValueSource.Default and not BaseValueSource.Inherited ||
                wrap is not BaseValueSource.Default and not BaseValueSource.Inherited ||
                wrapStyle is not BaseValueSource.Default and not BaseValueSource.Inherited ||
                tabSize is not BaseValueSource.Default and not BaseValueSource.Inherited) return true;
            if (collapse == BaseValueSource.Default && wrap == BaseValueSource.Default &&
                wrapStyle == BaseValueSource.Default && tabSize == BaseValueSource.Default) return false;
            node = node switch
            {
                FrameworkElement element => element.Parent,
                FrameworkContentElement content => content.Parent,
                _ => null,
            };
        }
        return false;
    }
}
