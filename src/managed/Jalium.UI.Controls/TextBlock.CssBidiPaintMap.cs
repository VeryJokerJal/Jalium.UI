using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool HasSplitBidiPaintWrap()
    {
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            if (line.PaintWrap is not { } paint) continue;
            int end = line.StartIndex + line.Length;
            if (line.StartIndex < 0 || end > _visualText.Length) return true;
            int expectedStart = _paintOffsets?[line.StartIndex] ?? line.StartIndex;
            int expectedEnd = _paintOffsets?[end] ?? end;
            if (paint.PaintStart != expectedStart)
            {
                int firstUnitSourceEnd = paint.Units[paint.First].SourceEnd;
                int firstUnitPaintEnd = _paintOffsets?[firstUnitSourceEnd] ?? firstUnitSourceEnd;
                if (_paintOffsets is null || i == 0 || _layoutLines[i - 1].HasLineBreakAfter ||
                    _layoutLines[i - 1].PaintWrap is not { } prevPaint || prevPaint.PaintEnd != paint.PaintStart ||
                    paint.PaintStart <= expectedStart || paint.PaintStart >= firstUnitPaintEnd ||
                    char.IsLowSurrogate(_paintText[paint.PaintStart]))
                    return true;
            }
            if (paint.PaintEnd != expectedEnd)
            {
                int lastUnitSourceStart = paint.Units[paint.End - 1].SourceStart;
                int lastUnitPaintStart = _paintOffsets?[lastUnitSourceStart] ?? lastUnitSourceStart;
                if (_paintOffsets is null || i + 1 >= _layoutLines.Count || line.HasLineBreakAfter ||
                    _layoutLines[i + 1].PaintWrap is not { } nextPaint || nextPaint.PaintStart != paint.PaintEnd ||
                    paint.PaintEnd <= lastUnitPaintStart || paint.PaintEnd >= expectedEnd ||
                    char.IsLowSurrogate(_paintText[paint.PaintEnd]))
                    return true;
            }
        }
        return false;
    }

    // Author indices stay in UTF-16 source space. Each scalar can own zero,
    // one or several displayed scalars after whitespace and case conversion.
    private bool TryCssBidiPaintMap(out string source, out Dictionary<int, string>? replacements,
        bool allowConditionalHyphens = false)
    {
        source = _usesAutospaceShaping ? RestoreShapedAutospaceSpaces(_visualText, 0) : _visualText;
        replacements = null;
        if (!_hasPaintTextChanges) return true;
        if (_visualText.Length != _displayText.Length) return false;
        if (_paintOffsets is { } offsets && (offsets.Length != source.Length + 1 || offsets[0] != 0)) return false;
        int cursor = 0;
        for (int index = 0; index < source.Length;)
        {
            int length = Rune.TryGetRuneAt(source, index, out var rune) ? rune.Utf16SequenceLength : 1;
            int first = _paintOffsets?[index] ?? index, end = _paintOffsets?[index + length] ?? index + length;
            if (first != cursor || end < first || end > _paintText.Length) return false;
            cursor = end;
            // The hidden-whitespace placeholder already is a zero-width BN
            // cell. Keep its explicit source slot, including scope edges.
            if (end == first && source[index] == '\uFEFF') { index += length; continue; }
            if (end == first && source[index] == '\u00AD' && !allowConditionalHyphens && CombinedUnitAt(index) is null) return false;
            bool restoredSpace = _autospaceReplacementWidths.ContainsKey(index) && _displayText[index] == ' ';
            if (!restoredSpace && !_paintText.AsSpan(first, end - first).SequenceEqual(source.AsSpan(index, length)))
                (replacements ??= [])[index] = _paintText.Substring(first, end - first);
            index += length;
        }
        return cursor == _paintText.Length;
    }
}
