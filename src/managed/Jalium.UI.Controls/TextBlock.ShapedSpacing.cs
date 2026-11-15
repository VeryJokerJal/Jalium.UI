using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Globalization;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _usesCapsShaping;
    private readonly Dictionary<(int Start, int End, double Height, double? JustifiedWidth), ShapedSpacingEntry> _shapedSpacing = [];
    private sealed record ShapedSpacingEntry(NativeTextParagraph.Span[] Spans,
        NativeMethods.ParagraphAdvanceUnit[] Units, FlowDirection Direction, NativeTextParagraph Paragraph);

    private void ClearShapedSpacing()
    {
        _justifiedLineCache.Clear();
        foreach (var entry in _shapedSpacing.Values) entry.Paragraph.Dispose();
        _shapedSpacing.Clear();
        _autospaceNaturals?.Clear();
    }

    private bool TryShapedSpacing(int start, int end, double height, out NativeTextParagraph paragraph,
        int visibleEnd = int.MaxValue, ShapingJustification? justification = null)
    {
        paragraph = null!;
        if (!OperatingSystem.IsMacOS() || FontSize <= 0 || (!_hasLetterSpacing && !_hasWordSpacing && !_usesWordSpacingShaping && !_usesCapsShaping && !_usesAutospaceShaping && !WantsShapedJustification && justification is null) ||
            (_cssInlineWhitespace && (_sharedCapsContext is null || !_usesCapsShaping)) ||
            TextTrimming != TextTrimming.None || _visualText.AsSpan().IndexOfAny("\t\ufffc".AsSpan()) >= 0 ||
            start < 0 || end <= start || end > _visualText.Length ||
            _visualText.AsSpan(start, end - start).IndexOfAny("\r\n\t\ufffc".AsSpan()) >= 0 ||
            Foreground is not SolidColorBrush foreground)
            return false;
        if (_hasPaintTextChanges)
        {
            var visual = _visualText.AsSpan(start, end - start);
            string paint = PaintSlice(start, end - start);
            if (!visual.SequenceEqual(paint) && string.Concat(visual.ToString().EnumerateRunes()
                .Where(rune => Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format).Select(rune => rune.ToString())) != paint) return false;
        }
        var ranges = GetInlineTextRanges();
        if (ranges.Any(range => range.Run.FontSize <= 0)) return false;
        var spans = new List<NativeTextParagraph.Span>();
        bool contextPaint = _hasWordSpacing || _usesWordSpacingShaping || _usesCapsShaping || _usesAutospaceShaping || WantsShapedJustification || justification is not null;
        bool suppressLigatures = WantsShapedJustification && SuppressJustificationLigatures(_visualText,
            ranges.Select(range => (DependencyObject)range.Run).Prepend(this));
        int cursor = start;
        foreach (var range in ranges)
        {
            if (range.End <= cursor) continue;
            if (range.Start >= end) break;
            if (range.Start > cursor) Add(null, cursor, Math.Min(end, range.Start));
            if (cursor >= end) break;
            int last = Math.Min(end, range.End);
            if (!Add(range.Run, cursor, last)) return false;
        }
        if (cursor < end && !Add(null, cursor, end)) return false;
        bool Add(Run? run, int first, int last)
        {
            if (last <= first) return true;
            var brush = run?.Foreground ?? foreground;
            if (brush is not SolidColorBrush solid) return false;
            string family = run is null ? FontFamily.GetRenderingSource(this) : GetRunFontFamily(run);
            if (suppressLigatures) family = JustificationFont(family, run ?? (DependencyObject)this);
            family = CapsContextFamily(family, start, end, first);
            var span = new NativeTextParagraph.Span(RestoreShapedAutospaceSpaces(_visualText.Substring(first, last - first), first), family,
                run is null ? FontSize : GetRunFontSize(run),
                run?.FontWeight.ToOpenTypeWeight() ?? FontWeight.ToOpenTypeWeight(),
                run?.FontStyle.ToOpenTypeStyle() ?? FontStyle.ToOpenTypeStyle(),
                contextPaint ? Colors.Black : solid.Color, contextPaint ? 1 : solid.Opacity);
            // An otherwise identical source-node boundary must not split shaping.
            if (spans.Count > 0 && (spans[^1] with { Text = "" }) == (span with { Text = "" }))
                spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
            else spans.Add(span);
            cursor = last; return true;
        }
        string text = RestoreShapedAutospaceSpaces(_visualText.Substring(start, end - start), start);
        var boundaries = GraphemeClusters.GetBoundaries(text);
        var units = new NativeMethods.ParagraphAdvanceUnit[boundaries.Length - 1];
        var metadata = new AutospaceUnit[units.Length];
        int unitRange = 0;
        double defaultSpacing = CssTextSpacing.Letter(this);
        for (int i = 0; i < units.Length; i++)
        {
            int first = boundaries[i], last = boundaries[i + 1];
            int position = start + first;
            while (unitRange < ranges.Count && ranges[unitRange].End <= position) unitRange++;
            double spacing = unitRange < ranges.Count && ranges[unitRange].Start <= position
                ? CssTextSpacing.Letter(ranges[unitRange].Run) : defaultSpacing;
            if (!double.IsFinite(spacing) || Math.Abs(spacing) > 1000000) return false;
            bool ignored = true;
            foreach (var rune in text.AsSpan(first, last - first).EnumerateRunes())
                if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) { ignored = false; break; }
            // Collapsed line-end spaces keep their source offsets, but neither
            // their advance nor a half tracking gap belongs to the used line.
            ignored |= start + first >= visibleEnd;
            bool cursive = Rune.TryGetRuneAt(text, first, out var letter) && Rune.IsLetter(letter) && IsCursiveJoiningPair(letter, letter);
            Rune.TryGetRuneAt(text, first, out var separatorRune);
            metadata[i] = AutospaceMetadata(position, separatorRune);
            bool separator = IsWordSeparator(separatorRune) || metadata[i].Replacement;
            double word = metadata[i].Word;
            if (!double.IsFinite(word) || Math.Abs(word) > 1000000) return false;
            units[i] = new() { TextPosition = (uint)first, Length = (uint)(last - first), Spacing = (float)spacing,
                Leading = (float)(word / 2), Trailing = (float)(word / 2),
                Flags = ignored ? 1u : (cursive ? 2u : 0u) | (separator ? 4u : 0u) };
        }
        var spanArray = spans.ToArray(); var key = (start, end, height, justification?.Width);
        if (_usesAutospaceShaping)
        {
            var natural = (_autospaceNaturals ??= new()).Get(spanArray, units, FontFamily.GetRenderingSource(this), FontSize, Math.Max(1, height), FlowDirection);
            if (natural is null || !ApplyShapedAutospace(natural, units, metadata, this, out _)) return false;
        }
        if (justification is not null)
        {
            var natural = (_autospaceNaturals ??= new()).Get(spanArray, units, FontFamily.GetRenderingSource(this), FontSize,
                Math.Max(1, height), FlowDirection, preserveAdvances: true);
            if (natural is null) return false;
            ApplyShapedJustification(natural, units, metadata, this, justification);
        }
        if (_shapedSpacing.TryGetValue(key, out var cached))
        {
            if (cached.Direction == FlowDirection && cached.Paragraph.ContextPaint == contextPaint &&
                cached.Spans.SequenceEqual(spanArray) && cached.Units.SequenceEqual(units))
            { paragraph = cached.Paragraph; return true; }
            cached.Paragraph.Dispose(); _shapedSpacing.Remove(key);
        }
        var found = NativeTextParagraph.TryCreateAdvanced(spanArray, FontFamily.GetRenderingSource(this), FontSize,
            Math.Max(1000000, FontSize * text.Length * 4), Math.Max(1, height), FlowDirection, units, contextPaint);
        if (found?.Lines.Length != 1) { found?.Dispose(); return false; }
        if (_shapedSpacing.Count >= 128)
        { var old = _shapedSpacing.First(); old.Value.Paragraph.Dispose(); _shapedSpacing.Remove(old.Key); }
        _shapedSpacing[key] = new(spanArray, units, FlowDirection, found); paragraph = found; return true;
    }

    private bool TryDrawShapedSpacing(DrawingContext context, in TextLayoutLine line,
        int start, int end, ref double x, double lineY)
    {
        if (line.PaintWrap is not null || line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true } || TextTrimming != TextTrimming.None ||
            !TryShapedSpacing(start, end, line.Height, out var paragraph)) return false;
        var native = paragraph.Lines[0];
        if (paragraph.ContextPaint)
        {
            DrawContextSpacedLine(context, line, paragraph, start, end, x, lineY);
            x += native.Metrics.Line.Width; return true;
        }
        var formatted = new FormattedText(paragraph.Text, FontFamily.GetRenderingSource(this), FontSize)
        { PlatformTextLine = native, Foreground = Foreground, MaxTextWidth = Math.Max(1, native.Metrics.Line.Width), MaxTextHeight = Math.Max(1, TextPaintHeight(line)), Baseline = native.Metrics.Baseline };
        formatted.ApplyTextOptionsFrom(this);
        DrawDocumentGlyphs(context, formatted, new(x - native.Metrics.Line.X, lineY + line.Baseline - native.Metrics.Baseline), start, end - start);
        x += native.Metrics.Line.Width; return true;
    }

    private void DrawContextSpacedLine(DrawingContext context, in TextLayoutLine line,
        NativeTextParagraph paragraph, int start, int end, double x, double y)
    {
        var native = paragraph.Lines[0]; var selected = new List<uint>(); int visibility = 0;
        if (DocumentWritingToolsOwner is { } owner &&
            MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var ranges))
        {
            visibility = preview ? 1 : 2;
            foreach (var range in ranges)
                foreach (var source in DocumentWritingToolsRanges(range.Start, range.Length))
                {
                    int from = Math.Max(start, source.Start), to = (int)Math.Min(end, (long)source.Start + source.Length);
                    if (to > from) { selected.Add((uint)(from - start)); selected.Add((uint)(to - from)); }
                }
        }
        uint[] masks = selected.ToArray();
        var origin = new Point(x - native.Metrics.Line.X, y + line.Baseline - native.Metrics.Baseline);
        var paintHeight = Math.Max(1, TextPaintHeight(line));
        int cursor = start;
        foreach (var range in GetInlineTextRanges())
        {
            int first = Math.Max(cursor, range.Start), last = Math.Min(end, range.End);
            if (last <= first) continue;
            if (first > cursor) Paint(cursor, first, Foreground);
            Paint(first, last, range.Run.Foreground ?? Foreground); cursor = last;
        }
        if (cursor < end) Paint(cursor, end, Foreground);

        void Paint(int first, int last, Brush? foreground)
        {
            if (foreground is not SolidColorBrush brush || last <= first) return;
            int sourceStart = first - start; var localMasks = new List<uint>();
            for (int i = 0; i < masks.Length; i += 2)
            {
                int from = Math.Max(sourceStart, (int)masks[i]), to = Math.Min(last - start, (int)(masks[i] + masks[i + 1]));
                if (to > from) { localMasks.Add((uint)(from - sourceStart)); localMasks.Add((uint)(to - from)); }
            }
            var formatted = new FormattedText(PaintSlice(first, last - first), FontFamily.GetRenderingSource(this), FontSize)
            {
                PlatformTextLine = native.ForSource(sourceStart, last - first, brush.Color, brush.Opacity, masks, visibility),
                Foreground = brush, MaxTextWidth = Math.Max(1, native.Metrics.Line.Width), MaxTextHeight = paintHeight,
                Baseline = native.Metrics.Baseline, GlyphRanges = localMasks.ToArray(), GlyphVisibility = visibility,
            };
            formatted.ApplyTextOptionsFrom(this); context.DrawText(formatted, origin);
        }
    }
}
