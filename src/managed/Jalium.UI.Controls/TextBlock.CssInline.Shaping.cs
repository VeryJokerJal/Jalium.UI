using System.Globalization;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private abstract record CssInlineShape
    {
        internal abstract double Boundary(int source);
        internal abstract IReadOnlyList<TextRangeMetrics> Selection(int start, int length);
        internal abstract int Hit(double x);
        internal abstract double InkOverhang { get; }
        internal virtual double BeforeOutside(DependencyObject owner) => 0;
        internal virtual double AfterOutside(DependencyObject owner) => 0;
    }

    private sealed record SharedShapeMap(CssInlineFragment Original, int Start, int End);
    private sealed record SharedShapeExterior(DependencyObject Owner, double Width);
    private sealed record SharedShapeCell(int Start, int End, double Left, double Right,
        bool Rtl, SharedShapeMap Map, SharedShapeExterior[] Before, SharedShapeExterior[] After);
    private sealed record SharedShapePlan(NativeTextParagraph Paragraph, SharedShapeMap[] Maps, SharedShapeCell[] Cells);
    private sealed record SharedShapePart(SharedShapePlan Plan, SharedShapeMap Map,
        int Start, int End, double Left, double Right, SharedShapeExterior[] Before, SharedShapeExterior[] After) : CssInlineShape
    {
        internal int SourceStart => Map.Original.Line.StartIndex + Math.Max(Start, Map.Start) - Map.Start;
        internal int SourceEnd => Map.Original.Line.StartIndex + Math.Min(End, Map.End) - Map.Start;
        internal int NativePosition(int source) => Math.Clamp(Map.Start + source - Map.Original.Line.StartIndex, Start, End);
        internal override double InkOverhang
        {
            get
            {
                int start = Map.Start + SourceStart - Map.Original.Line.StartIndex;
                var bounds = Plan.Paragraph.SourceBounds(0, start, SourceEnd - SourceStart);
                return bounds is { Width: > 0 } ink
                    ? Math.Max(0, Math.Max(Left - ink.X, ink.X + ink.Width - Right))
                    : bounds is null ? Plan.Paragraph.Lines[0].Metrics.Line.Width : 0;
            }
        }
        internal override double BeforeOutside(DependencyObject element) => Before.Where(gap => !ContainsAutospaceOwner(element, gap.Owner)).Sum(gap => gap.Width);
        internal override double AfterOutside(DependencyObject element) => After.Where(gap => !ContainsAutospaceOwner(element, gap.Owner)).Sum(gap => gap.Width);
        internal override double Boundary(int source)
        {
            int position = NativePosition(source);
            foreach (var cell in Plan.Cells)
            {
                if (!ReferenceEquals(cell.Map, Map) || cell.Start < Start || cell.End > End) continue;
                if (position == cell.Start) return (cell.Rtl ? cell.Right : cell.Left) - Left;
                if (position == cell.End) return (cell.Rtl ? cell.Left : cell.Right) - Left;
            }
            return Plan.Paragraph.Caret(0, position, false).X - Left;
        }
        internal override IReadOnlyList<TextRangeMetrics> Selection(int start, int length)
        {
            int from = NativePosition(start), to = NativePosition(start + length);
            var result = new List<TextRangeMetrics>();
            foreach (var cell in Plan.Cells)
            {
                if (!ReferenceEquals(cell.Map, Map) || cell.Start < Start || cell.End > End ||
                    cell.End <= from || cell.Start >= to || cell.Right <= cell.Left) continue;
                int first = Math.Max(cell.Start, Map.Start), last = Math.Min(cell.End, Map.End);
                result.Add(new TextRangeMetrics { TextPosition = (uint)(first - Map.Start + Map.Original.Line.StartIndex),
                    Length = (uint)(last - first), X = (float)(cell.Left - Left), Width = (float)(cell.Right - cell.Left),
                    Height = (float)Map.Original.Line.Height });
            }
            result.Sort(static (a, b) => a.X.CompareTo(b.X));
            var merged = new List<TextRangeMetrics>();
            foreach (var rectangle in result)
            {
                if (merged.Count > 0 && rectangle.X <= merged[^1].X + merged[^1].Width + .001)
                {
                    var previous = merged[^1];
                    uint last = Math.Max(previous.TextPosition + previous.Length, rectangle.TextPosition + rectangle.Length);
                    previous.TextPosition = Math.Min(previous.TextPosition, rectangle.TextPosition); previous.Length = last - previous.TextPosition;
                    previous.Width = Math.Max(previous.X + previous.Width, rectangle.X + rectangle.Width) - previous.X; merged[^1] = previous;
                }
                else merged.Add(rectangle);
            }
            // A source contributing only the first part of a cross-element
            // grapheme still owns its original source position and line box.
            if (merged.Count == 0 && to > from)
                merged.Add(new TextRangeMetrics { TextPosition = (uint)start, Length = (uint)length,
                    Height = (float)Map.Original.Line.Height });
            return merged;
        }
        internal override int Hit(double x)
        {
            int best = SourceStart; double distance = double.PositiveInfinity;
            foreach (var cell in Plan.Cells)
            {
                if (!ReferenceEquals(cell.Map, Map) || cell.Start < Start || cell.End > End) continue;
                Test(cell.Start, cell.Rtl ? cell.Right : cell.Left); Test(cell.End, cell.Rtl ? cell.Left : cell.Right);
            }
            return best;
            void Test(int position, double edge)
            {
                double next = Math.Abs(x - (edge - Left));
                var candidate = Math.Clamp(Map.Original.Line.StartIndex + position - Map.Start, SourceStart, SourceEnd);
                if (next > distance || next == distance && candidate <= best) return;
                distance = next; best = candidate;
            }
        }
    }

    private CssInlineShape? SharedShape(in TextLayoutLine line) => InlineFragment(line)?.Shape;

    private bool DrawSharedShape(DrawingContext context, in TextLayoutLine line, double x, double y)
    {
        if (SharedShape(line) is CssInlineBidiPart bidi) return DrawInlineBidi(context, line, bidi, x, y);
        if (SharedShape(line) is not SharedShapePart part) return false;
        var native = part.Plan.Paragraph.Lines[0];
        double baseline = line.Baseline;
        var text = new FormattedText(part.Plan.Paragraph.Text, FontFamily.GetRenderingSource(this), FontSize)
        { Foreground = Foreground, MaxTextWidth = Math.Max(1, native.Metrics.Line.Width), MaxTextHeight = Math.Max(1, TextPaintHeight(line)) };
        text.ApplyTextOptionsFrom(this);
        // Writing Tools masks address the complete shared shaped string. A
        // cluster painted by a later source may overlap an earlier source's
        // logical mask, so collect all source mappings before filtering.
        if (DocumentWritingToolsOwner is { } owner && MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var ranges))
        {
            var selected = new List<uint>();
            foreach (var map in part.Plan.Maps)
                foreach (var range in ranges)
                    foreach (var source in map.Original.Source.Text.DocumentWritingToolsRanges(range.Start, range.Length))
                    {
                        int first = Math.Max(source.Start, map.Original.Line.StartIndex);
                        int last = Math.Min(source.Start + source.Length, map.Original.Line.StartIndex + map.End - map.Start);
                        if (last <= first) continue;
                        selected.Add((uint)(map.Start + first - map.Original.Line.StartIndex)); selected.Add((uint)(last - first));
                    }
            text.GlyphVisibility = preview ? 1 : 2; text.GlyphRanges = selected.ToArray();
        }
        int firstSource = part.SourceStart, endSource = part.SourceEnd, cursor = firstSource;
        foreach (var range in GetInlineTextRanges())
        {
            int first = Math.Max(cursor, range.Start), last = Math.Min(endSource, range.End);
            if (last <= first) continue;
            if (first > cursor) Paint(cursor, first, Foreground);
            Paint(first, last, range.Run.Foreground ?? Foreground); cursor = last;
        }
        if (cursor < endSource) Paint(cursor, endSource, Foreground);
        return true;
        void Paint(int first, int last, Brush? foreground)
        {
            if (foreground is not SolidColorBrush brush || last <= first) return;
            int start = part.Map.Start + first - part.Map.Original.Line.StartIndex;
            var localRanges = new List<uint>();
            for (int i = 0; i < (text.GlyphRanges?.Length ?? 0); i += 2)
            {
                int from = Math.Max(start, (int)text.GlyphRanges![i]);
                int to = Math.Min(start + last - first, (int)(text.GlyphRanges[i] + text.GlyphRanges[i + 1]));
                if (to <= from) continue;
                int paintedStart = PaintSlice(first, from - start).Length;
                int paintedLength = PaintSlice(first + from - start, to - from).Length;
                if (paintedLength > 0) { localRanges.Add((uint)paintedStart); localRanges.Add((uint)paintedLength); }
            }
            var painted = new FormattedText(PaintSlice(first, last - first), FontFamily.GetRenderingSource(this), FontSize)
            {
                PlatformTextLine = native.ForSource(start, last - first, brush.Color, brush.Opacity, text.GlyphRanges, text.GlyphVisibility),
                Foreground = brush, MaxTextWidth = text.MaxTextWidth, MaxTextHeight = text.MaxTextHeight,
                GlyphVisibility = text.GlyphVisibility, GlyphRanges = localRanges.ToArray(),
                Baseline = native.Metrics.Baseline,
            };
            painted.ApplyTextOptionsFrom(this);
            context.DrawText(painted, new(x - part.Left, y + baseline - native.Metrics.Baseline));
        }
    }

    internal sealed partial class CssTextFlowLayout
    {
        private sealed record ShapeCacheEntry(NativeTextParagraph.Span[] Spans, NativeMethods.ParagraphAdvanceUnit[] Units,
            FlowDirection Direction, double Height, string Family, double Size, NativeTextParagraph Paragraph);
        private readonly Dictionary<int, List<ShapeCacheEntry>> _sharedShapes = [];
        private int _sharedShapeCount;
        private NaturalShapeCache? _sharedAutospaceNaturals;

        private void ShapePieces(List<CssInlineFragment> pieces)
        {
            if (!OperatingSystem.IsMacOS() || RenderContext.Current is not
                { IsValid: true, Backend: RenderBackend.Metal or RenderBackend.Software } ||
                pieces.Count == 0 || pieces.Any(fragment => !fragment.Source.Atomic &&
                fragment.Source.Text._visualText.AsSpan(fragment.Line.StartIndex, fragment.Line.Length).Contains('\t'))) return;
            bool justified = CssFlowProperties.LineAlignment(owner, false) == CssFlowTextAlignment.Justify ||
                CssFlowProperties.LineAlignment(owner, true) == CssFlowTextAlignment.Justify;
            // A cursive word has no internal justification opportunities. It
            // must keep the same joined shape when alignment falls back to
            // centering; expandable lines still use the existing gap planner.
            if (justified && !_intrinsic && pieces.Any(fragment => fragment.Source.Atomic ||
                fragment.Source.Text._visualText.AsSpan(fragment.Line.StartIndex, fragment.Line.Length).ToString().EnumerateRunes()
                    .Any(rune => Rune.IsLetter(rune) ? !IsCursiveJoiningPair(rune, rune) :
                        Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)))) return;
            for (int start = 0; start < pieces.Count;)
            {
                if (!CanShape(pieces[start])) { start++; continue; }
                int end = start + 1;
                while (end < pieces.Count && CanShape(pieces[end]) && !ShapeBoundary(pieces[end - 1], pieces[end])) end++;
                var group = pieces.GetRange(start, end - start);
                if ((group.Count > 1 || group[0].Source.Text is InlineSourceText || group[0].Source.Text._hasLetterSpacing || group[0].Source.Text._hasWordSpacing ||
                    group[0].Source.Text._usesWordSpacingShaping || group[0].Source.Text._usesCapsShaping ||
                    group[0].Source.Text._usesAutospaceShaping || justified) && TryShape(group) is { } shaped)
                { pieces.RemoveRange(start, end - start); pieces.InsertRange(start, shaped); start += shaped.Count; }
                else start = end;
            }
        }

        private static bool CanShape(CssInlineFragment fragment)
        {
            if (fragment.Source.Atomic || fragment.Source.EmptyFrame || fragment.Line.Length == 0 ||
                fragment.Line.PaintWrap is not null || fragment.Line.SoftHyphenVisible ||
                fragment.Line.HyphenChange is { HasChanges: true } ||
                fragment.Line.HangingStart != 0 || fragment.Line.HangingEnd != 0) return false;
            var text = fragment.Source.Text;
            if (text.FontSize <= 0 || (text.FontStretch.ToOpenTypeStretch() != 5 && !OperatingSystem.IsMacOS()) ||
                text.Foreground is not SolidColorBrush || text.TextTrimming != TextTrimming.None) return false;
            var visual = text._visualText.AsSpan(fragment.Line.StartIndex, fragment.Line.Length);
            if (visual.IndexOfAny("\r\n\t\ufffc".AsSpan()) >= 0) return false;
            var paint = text.PaintSlice(fragment.Line.StartIndex, fragment.Line.Length);
            if (!visual.SequenceEqual(paint) && string.Concat(visual.ToString().EnumerateRunes()
                .Where(r => Rune.GetUnicodeCategory(r) != UnicodeCategory.Format).Select(r => r.ToString())) != paint) return false;
            return !text.GetInlineTextRanges().Any(range => range.Run.FontSize <= 0 ||
                (range.Run.FontStretch.ToOpenTypeStretch() != 5 && !OperatingSystem.IsMacOS()) ||
                (range.Run.Foreground ?? text.Foreground) is not SolidColorBrush);
        }

        private static bool ShapeBoundary(CssInlineFragment before, CssInlineFragment after)
        {
            if (!ReferenceEquals(before.Source.Initial, after.Source.Initial)) return true;
            if (before.Right != 0 || before.MarginRight != 0 || before.OuterRight != 0 ||
                after.Left != 0 || after.MarginLeft != 0 || after.OuterLeft != 0) return true;
            if ((CssInlineVerticalAlign)before.Source.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! != default ||
                (CssInlineVerticalAlign)after.Source.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! != default) return true;
            // A nested box can shift its whole subtree without changing the
            // leaf's computed vertical-align. Only common shifted ancestors
            // may participate in the same shaping context.
            if (HasShiftedFrame(before.Source.Frame, after.Source.Frame) ||
                HasShiftedFrame(after.Source.Frame, before.Source.Frame)) return true;
            return before.Source.RightToLeft != after.Source.RightToLeft ||
                !ReferenceEquals(before.Source.Text.DocumentWritingToolsOwner, after.Source.Text.DocumentWritingToolsOwner);

            static bool HasShiftedFrame(CssInlineFrame? frame, CssInlineFrame? other)
            {
                for (; frame is not null; frame = frame.Parent)
                {
                    bool common = false;
                    for (var ancestor = other; ancestor is not null; ancestor = ancestor.Parent)
                        if (ReferenceEquals(frame, ancestor)) { common = true; break; }
                    if (!common && (CssInlineVerticalAlign)frame.Element.GetValue(CssFlowProperties.VerticalAlignProperty)! != default) return true;
                }
                return false;
            }
        }

        private List<CssInlineFragment>? TryShape(List<CssInlineFragment> originals, ShapingJustification? justification = null)
        {
            var joined = new StringBuilder(); var maps = new List<SharedShapeMap>();
            var spans = new List<NativeTextParagraph.Span>();
            byte capsBoundaries = (byte)((originals[0].Source.Text.CapsBoundaryFlags(originals[0].Line.StartIndex,
                originals[0].Line.StartIndex) & 13) | (originals[^1].Source.Text.CapsBoundaryFlags(
                    originals[^1].Line.StartIndex + originals[^1].Line.Length,
                    originals[^1].Line.StartIndex + originals[^1].Line.Length) & 50));
            byte emojiAfter = originals[^1].Source.Text.EmojiAfterAt(originals[^1].Line.StartIndex + originals[^1].Line.Length);
            byte capsContinuation = originals[0].Source.Text.CapsContinuation(originals[0].Line.StartIndex);
            bool wantsJustification = justification is not null || _intrinsic &&
                (CssFlowProperties.LineAlignment(owner, false) == CssFlowTextAlignment.Justify || CssFlowProperties.LineAlignment(owner, true) == CssFlowTextAlignment.Justify);
            bool suppressLigatures = wantsJustification && SuppressJustificationLigatures(
                string.Concat(originals.Select(fragment => fragment.Source.Text._visualText.Substring(fragment.Line.StartIndex, fragment.Line.Length))),
                originals.SelectMany(fragment => fragment.Source.Text.GetInlineTextRanges().Select(range => (DependencyObject)range.Run)
                    .Prepend(fragment.Source.Element)).Prepend(owner));
            foreach (var original in originals)
            {
                var source = original.Source.Text; int first = original.Line.StartIndex, last = first + original.Line.Length;
                int offset = joined.Length; joined.Append(source.RestoreShapedAutospaceSpaces(source._visualText.Substring(first, original.Line.Length), first));
                maps.Add(new(original, offset, joined.Length));
                int cursor = first;
                foreach (var range in source.GetInlineTextRanges())
                {
                    int from = Math.Max(cursor, range.Start), to = Math.Min(last, range.End);
                    if (to <= from) continue;
                    if (from > cursor) Add(source, null, cursor, from);
                    Add(source, range.Run, from, to); cursor = to;
                }
                if (cursor < last) Add(source, null, cursor, last);
            }
            void Add(TextBlock source, Run? run, int first, int last)
            {
                string family = run is null ? source.FontFamily.GetRenderingSource(source) : source.GetRunFontFamily(run);
                if (suppressLigatures) family = JustificationFont(family, run ?? (DependencyObject)source);
                family = FontFeatureRenderingSource.WithCapsFragment(family, capsBoundaries, capsContinuation, source.PositionRunAt(first), emojiAfter);
                var span = new NativeTextParagraph.Span(source.RestoreShapedAutospaceSpaces(source._visualText.Substring(first, last - first), first), family,
                    run is null ? source.FontSize : source.GetRunFontSize(run), run?.FontWeight.ToOpenTypeWeight() ?? source.FontWeight.ToOpenTypeWeight(),
                    run?.FontStyle.ToOpenTypeStyle() ?? source.FontStyle.ToOpenTypeStyle(), Colors.Black);
                if (spans.Count > 0 && (spans[^1] with { Text = "" }) == (span with { Text = "" }))
                    spans[^1] = spans[^1] with { Text = spans[^1].Text + span.Text };
                else spans.Add(span);
            }
            string content = joined.ToString();
            var boundaries = GraphemeClusters.GetBoundaries(content);
            var units = new NativeMethods.ParagraphAdvanceUnit[boundaries.Length - 1]; int atMap = 0;
            var metadata = new AutospaceUnit[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                int first = boundaries[i], last = boundaries[i + 1];
                while (atMap + 1 < maps.Count && maps[atMap].End <= first) atMap++;
                var map = maps[atMap]; var source = map.Original.Source.Text;
                double spacing = source.ComputedLetterSpacingAt(map.Original.Line.StartIndex + first - map.Start);
                if (!double.IsFinite(spacing) || Math.Abs(spacing) > 1000000) return null;
                bool ignored = true;
                foreach (var rune in content.AsSpan(first, last - first).EnumerateRunes())
                    if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format) { ignored = false; break; }
                bool cursive = Rune.TryGetRuneAt(content, first, out var letter) && Rune.IsLetter(letter) && IsCursiveJoiningPair(letter, letter);
                Rune.TryGetRuneAt(content, first, out var separatorRune);
                int position = map.Original.Line.StartIndex + first - map.Start;
                metadata[i] = source.AutospaceMetadata(position, separatorRune);
                if (source is not InlineSourceText && InlineRunAt(source.GetInlineTextRanges(), position) is null)
                    metadata[i] = metadata[i] with { Owner = AutospaceStyleSource(map.Original.Source.Element) };
                bool separator = IsWordSeparator(separatorRune) || metadata[i].Replacement;
                double word = metadata[i].Word;
                if (!double.IsFinite(word) || Math.Abs(word) > 1000000) return null;
                units[i] = new() { TextPosition = (uint)first, Length = (uint)(last - first), Spacing = (float)spacing,
                    Leading = (float)(word / 2), Trailing = (float)(word / 2),
                    Flags = ignored ? 1u : (cursive ? 2u : 0u) | (separator ? 4u : 0u) };
            }
            var inputs = spans.ToArray(); double height = originals.Max(fragment => fragment.Line.Height);
            var family = originals[0].Source.Text.FontFamily.GetRenderingSource(originals[0].Source.Text);
            double size = originals[0].Source.Text.FontSize;
            VisualAutoGap[] autoGaps = [];
            if (content.EnumerateRunes().Any(rune => IsAutospaceIdeograph(rune) || rune.Value is ';' or '!' or '?' or ':' or 0x00ab or 0x00bb) &&
                ((CssTextAutospace)owner.GetValue(CssFlowProperties.TextAutospaceProperty)! != CssTextAutospace.None ||
                metadata.Any(unit => (CssTextAutospace)unit.Owner.GetValue(CssFlowProperties.TextAutospaceProperty)! != CssTextAutospace.None)))
            {
                var natural = (_sharedAutospaceNaturals ??= new()).Get(inputs, units, family, size, height, owner.FlowDirection);
                if (natural is null || !originals[0].Source.Text.ApplyShapedAutospace(natural, units, metadata, owner, out autoGaps)) return null;
            }
            // Keep the established document paint path when contextual spacing
            // is enabled but contributes nothing to this line. Native document
            // readers can use that same path without a shared-flow projection.
            if (justification is null && autoGaps.Length == 0 && !maps.Any(map => map.Original.Source.Text._autospaceReplacementWidths.Count > 0))
            {
                bool cjk = false, other = false;
                foreach (var rune in content.EnumerateRunes()) { cjk |= IsCjkLineBreakRune(rune); other |= Rune.IsLetterOrDigit(rune) && !IsCjkLineBreakRune(rune); }
                bool French(DependencyObject target) => UsesFrenchPunctuation(target) &&
                    ((CssTextAutospace)target.GetValue(CssFlowProperties.TextAutospaceProperty)! & CssTextAutospace.Punctuation) != 0;
                if (cjk && other && metadata.Any(unit => (CssTextAutospace)unit.Owner.GetValue(CssFlowProperties.TextAutospaceProperty)! != CssTextAutospace.None) ||
                    content.AsSpan().IndexOfAny(";!?:«»".AsSpan()) >= 0 && metadata.Any(unit => French(unit.Owner))) return null;
            }
            if (justification is not null)
            {
                var natural = (_sharedAutospaceNaturals ??= new()).Get(inputs, units, family, size, height, owner.FlowDirection, preserveAdvances: true);
                if (natural is null) return null;
                ApplyShapedJustification(natural, units, metadata, owner, justification);
            }
            var hash = new HashCode();
            hash.Add(owner.FlowDirection); hash.Add(height); hash.Add(family); hash.Add(size);
            foreach (var span in inputs) hash.Add(span); foreach (var unit in units) hash.Add(unit);
            int key = hash.ToHashCode(); NativeTextParagraph? paragraph = null;
            if (_sharedShapes.TryGetValue(key, out var entries))
                paragraph = entries.FirstOrDefault(entry => entry.Direction == owner.FlowDirection && entry.Height == height &&
                    entry.Family == family && entry.Size == size && entry.Spans.SequenceEqual(inputs) && entry.Units.SequenceEqual(units))?.Paragraph;
            if (paragraph is null)
            {
                paragraph = NativeTextParagraph.TryCreateAdvanced(inputs, family, size, Math.Max(1000000, size * content.Length * 4), height, owner.FlowDirection, units, contextPaint: true);
                if (paragraph?.Lines.Length != 1) { paragraph?.Dispose(); return null; }
                // Fragments/column checkpoints retain their plans. Dropping a
                // cache entry must not close a paragraph that a checkpoint uses.
                if (_sharedShapeCount >= 512) { _sharedShapes.Clear(); _sharedShapeCount = 0; }
                if (!_sharedShapes.TryGetValue(key, out entries)) _sharedShapes[key] = entries = [];
                entries.Add(new(inputs, units, owner.FlowDirection, height, family, size, paragraph)); _sharedShapeCount++;
            }
            var cells = new List<SharedShapeCell>(); atMap = 0;
            for (int i = 0; i < units.Length; i++)
            {
                int first = boundaries[i], last = boundaries[i + 1];
                while (atMap + 1 < maps.Count && maps[atMap].End < last) atMap++;
                double a = paragraph.Caret(0, first, false).X, b = paragraph.Caret(0, last, true).X;
                cells.Add(new(first, last, Math.Min(a, b), Math.Max(a, b), b < a, maps[atMap], [], []));
            }
            // Virtual spacing belongs to its common ancestor. Account for it
            // in flow, while keeping descendant paint/hit/selection boxes out
            // of that ancestor's spacing area.
            var before = cells.Select(_ => new List<SharedShapeExterior>()).ToArray();
            var after = cells.Select(_ => new List<SharedShapeExterior>()).ToArray();
            foreach (var gap in autoGaps)
            {
                if (gap.Replacement >= 0)
                {
                    var cell = cells[gap.Replacement];
                    if (!ContainsAutospaceOwner(cell.Map.Original.Source.Element, gap.Owner))
                    {
                        double half = (cell.Right - cell.Left) / 2;
                        before[gap.Replacement].Add(new(gap.Owner, half)); after[gap.Replacement].Add(new(gap.Owner, half));
                    }
                }
                else
                {
                    if (!ContainsAutospaceOwner(cells[gap.Left].Map.Original.Source.Element, gap.Owner)) after[gap.Left].Add(new(gap.Owner, gap.Width / 2));
                    if (!ContainsAutospaceOwner(cells[gap.Right].Map.Original.Source.Element, gap.Owner)) before[gap.Right].Add(new(gap.Owner, gap.Width / 2));
                }
            }
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i]; double requested = before[i].Sum(gap => gap.Width) + after[i].Sum(gap => gap.Width);
                double scale = requested > 0 ? Math.Min(1, (cell.Right - cell.Left) / requested) : 1;
                var left = before[i].Select(gap => gap with { Width = gap.Width * scale }).ToArray();
                var right = after[i].Select(gap => gap with { Width = gap.Width * scale }).ToArray();
                cells[i] = cell with { Left = cell.Left + left.Sum(gap => gap.Width), Right = cell.Right - right.Sum(gap => gap.Width), Before = left, After = right };
            }
            // The existing RTL box formatter places LTR source boxes in
            // reverse source order. Preserve that path until its paragraph
            // bidi ordering (including atomic/physical boundaries) is shared.
            if (owner.FlowDirection == FlowDirection.RightToLeft && maps.Count > 1 && cells.Any(cell => !cell.Rtl &&
                content.AsSpan(cell.Start, cell.End - cell.Start).ToString().EnumerateRunes().Any(Rune.IsLetterOrDigit))) return null;
            var plan = new SharedShapePlan(paragraph, maps.ToArray(), cells.ToArray());
            var result = new List<CssInlineFragment>();
            var visual = cells.Where(cell => cell.Right > cell.Left || cell.Before.Length + cell.After.Length > 0).OrderBy(cell => cell.Left).ToArray();
            for (int i = 0; i < visual.Length;)
            {
                var first = visual[i++]; int from = first.Start, to = first.End; double left = first.Left, right = first.Right;
                var previous = first;
                while (i < visual.Length && previous.After.Length == 0 && visual[i].Before.Length == 0 && ReferenceEquals(visual[i].Map, first.Map) && visual[i].Rtl == first.Rtl &&
                    (first.Rtl ? visual[i].End == from : visual[i].Start == to))
                { var cell = visual[i++]; from = Math.Min(from, cell.Start); to = Math.Max(to, cell.End); right = Math.Max(right, cell.Right); previous = cell; }
                AddPart(first.Map, from, to, left, right, first.Before, previous.After);
            }
            foreach (var map in maps)
                if (!result.Any(fragment => ReferenceEquals(((SharedShapePart)fragment.Shape!).Map, map)))
                { double x = paragraph.Caret(0, map.Start, false).X; AddPart(map, map.Start, map.End, x, x, [], []); }
            result.Sort((a, b) => owner.FlowDirection == FlowDirection.RightToLeft
                ? ((SharedShapePart)b.Shape!).Left.CompareTo(((SharedShapePart)a.Shape!).Left)
                : ((SharedShapePart)a.Shape!).Left.CompareTo(((SharedShapePart)b.Shape!).Left));
            return result;
            void AddPart(SharedShapeMap map, int first, int last, double left, double right, SharedShapeExterior[] before, SharedShapeExterior[] after)
            {
                var original = map.Original; var line = original.Line;
                var part = new SharedShapePart(plan, map, first, last, left, right, before, after);
                int start = part.SourceStart, length = part.SourceEnd - start;
                var shapedLine = new TextLayoutLine(start, length, right - left, line.HasLineBreakAfter,
                    line.Height, line.Baseline, line.GlyphHeight, line.FormattedText, line.CanJustify, line.IndentApplies,
                    line.PaintWrap, line.SoftHyphenVisible, first <= map.Start ? line.StartPadding : 0,
                    last >= map.End ? line.EndPadding : 0, hyphenChange: line.HyphenChange);
                var fragment = original.Copy(); fragment.Line = shapedLine; fragment.Shape = part;
                fragment.First = original.First && first <= map.Start; fragment.Last = original.Last && last >= map.End;
                if (first > map.Start) { fragment.Left = fragment.MarginLeft = fragment.OuterLeft = 0; }
                if (last < map.End) { fragment.Right = fragment.MarginRight = fragment.OuterRight = 0; }
                fragment.MarginLeft += before.Sum(gap => gap.Width); fragment.MarginRight += after.Sum(gap => gap.Width);
                result.Add(fragment);
            }
        }
    }
}
