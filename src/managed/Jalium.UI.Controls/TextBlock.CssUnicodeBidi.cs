using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly Dictionary<TextLayoutLine, CssBidiLine> _cssBidiLines = [];
    private CssBidiParagraph? _cssBidiParagraph;
    private bool _cssBidiAttempted;
    private RenderContext? _cssBidiContext;
    private long _cssBidiFontEpoch;
    private (int Rendering, int Formatting, int Hinting) _cssBidiOptions;
    private CssBidiParagraph? _cssBidiMeasurement;
    private bool _cssBidiMeasurementAttempted;
    private double[]? _cssBidiAdvances;
    private CssBidiSpacingMetrics? _cssBidiSpacing;
    private CssBidiProjection? _cssBidiDirectionProjection;

    private void ClearCssBidiLines()
    {
        _cssBidiParagraph?.Dispose(); _cssBidiParagraph = null;
        _cssBidiMeasurement?.Dispose(); _cssBidiMeasurement = null;
        _cssBidiMeasurementAttempted = false; _cssBidiAdvances = null; _cssBidiSpacing = null;
        _cssBidiLines.Clear(); _cssBidiContext = null; _cssBidiAttempted = false;
        _cssBidiDirectionProjection = null;
    }

    // Resolve the complete paragraph before reordering any soft-wrapped line.
    // Both native engines see U+2028 at the already chosen CSS wrap boundaries;
    // this keeps neutral resolution, isolates and plaintext paragraph direction
    // intact without changing public text or its UTF-16 positions.
    private sealed class CssBidiParagraph : IDisposable
    {
        internal readonly CssBidiProjection Projection;
        internal readonly string Family;
        internal readonly NativeTextParagraph? Paragraph;
        internal readonly NativeTextFormat? Format;
        internal readonly int[] Boundaries;
        internal readonly double Height, Width, Baseline;
        internal readonly double MaximumFontSize;
        internal readonly CssBidiJustification? Justification;
        internal bool IsSupported { get; }

        internal CssBidiParagraph(RenderContext context, string family, double size, int weight, int style,
            FlowDirection direction, double lineHeight, CssBidiProjection projection,
            IReadOnlyList<NativeTextParagraph.Span> spans, bool uniformFont, DependencyObject textOptionsOwner,
            NativeMethods.ParagraphAdvanceUnit[]? advances = null, CssBidiJustification? justification = null,
            NativeMethods.ParagraphCombineUnit[]? combined = null, int[]? sourceBoundaries = null)
        {
            Justification = justification;
            Family = family; Projection = projection;
            Boundaries = sourceBoundaries ?? GraphemeClusters.GetBoundaries(projection.Source);
            MaximumFontSize = Math.Max(size, spans.Max(span => span.FontSize));
            Paragraph = advances is not null
                ? NativeTextParagraph.TryCreateAdvanced(spans, family, size, 10000000, Math.Max(1, lineHeight),
                    direction, advances, contextPaint: true, naturalDirection: projection.NaturalParagraphDirection,
                    textOptionsOwner: textOptionsOwner, verticalForms: textOptionsOwner is TextBlock { UsesVerticalTypography: true },
                    uprightForms: textOptionsOwner is TextBlock { UsesUprightGlyphForms: true }, combined: combined)
                : NativeTextParagraph.TryCreate(spans,
                    family, size, 10000000, Math.Max(1, lineHeight), TextAlignment.Left, direction,
                    naturalDirection: projection.NaturalParagraphDirection, noWrap: true, textOptionsOwner: textOptionsOwner,
                    verticalForms: textOptionsOwner is TextBlock { UsesVerticalTypography: true },
                    uprightForms: textOptionsOwner is TextBlock { UsesUprightGlyphForms: true }, combined: combined);
            if (Paragraph is not null) { IsSupported = Paragraph.Lines.Length > 0; return; }
            if (advances is not null || combined is { Length: > 0 } || textOptionsOwner is TextBlock { UsesVerticalGlyphForms: true }) return;
            if (!uniformFont) return;
            Format = TextMeasurement.CreateTextFormatFromFamilyList(context, family, (float)size, weight, style);
            if (Format is null) return;
            try
            {
                Format.SetNoWrap(true);
                if (!Format.TrySetTextDirection(projection.NaturalParagraphDirection ? 2 : (int)direction)) return;
                var metrics = Format.MeasureText(projection.Text, 10000000, 10000000);
                Height = Math.Max(1, metrics.Height); Width = Math.Max(1, metrics.WidthIncludingTrailingWhitespace); Baseline = metrics.Baseline;
                IsSupported = Format.HitTestTextPosition(projection.Text, 10000000, (float)Height,
                    (uint)projection.NativePosition(0), false, out _);
            }
            catch { Format.Dispose(); throw; }
        }
        public void Dispose() { Paragraph?.Dispose(); Format?.Dispose(); }
    }

    private sealed class CssBidiLine
    {
        internal readonly CssBidiParagraph Owner;
        internal CssBidiProjection Projection => Owner.Projection;
        internal readonly int Start, Length, SliceNativeStart, SliceNativeEnd;
        internal readonly uint NativeLine;
        internal readonly double Width, Baseline, NativeY;
        private double NativeX => Owner.Paragraph?.Lines[NativeLine].Metrics.Line.X ?? 0;
        private bool _sourceInkRead;
        private TextRangeMetrics? _sourceInkBounds;
        internal TextRangeMetrics? SourceInkBounds
        {
            get
            {
                if (!_sourceInkRead)
                {
                    _sourceInkRead = true;
                    _sourceInkBounds = Owner.Paragraph?.SourceBounds(NativeLine, SliceNativeStart,
                        SliceNativeEnd - SliceNativeStart);
                }
                return _sourceInkBounds;
            }
        }


        internal CssBidiLine(CssBidiParagraph owner, TextLayoutLine line, int[]? paintOffsets)
        {
            Owner = owner; Start = line.StartIndex; Length = line.Length;
            int end = Start + Length;
            if (line.PaintWrap is { } paint && paintOffsets is not null && Start >= 0 && end <= paintOffsets.Length - 1)
            {
                int startDisplayOffset = paint.PaintStart - paintOffsets[Start];
                SliceNativeStart = startDisplayOffset > 0
                    ? Projection.NativeDisplayPosition(Start, startDisplayOffset, trailing: false)
                    : Projection.NativePosition(Start, trailing: false);
                int lastUnitSourceStart = paint.Units[paint.End - 1].SourceStart;
                int endDisplayOffset = paint.PaintEnd - paintOffsets[lastUnitSourceStart];
                SliceNativeEnd = paint.PaintEnd < paintOffsets[end]
                    ? Projection.NativeDisplayPosition(lastUnitSourceStart, endDisplayOffset, trailing: true)
                    : Projection.NativePosition(end, trailing: true);
            }
            else
            {
                SliceNativeStart = Projection.NativePosition(Start, trailing: false);
                SliceNativeEnd = Projection.NativePosition(end, trailing: true);
            }
            int first = SliceNativeStart;
            if (owner.Paragraph is { } paragraph)
            {
                int index = Array.FindIndex(paragraph.Lines, row => first >= row.Metrics.Line.TextPosition &&
                    first < row.Metrics.Line.TextPosition + row.Metrics.Line.Length);
                if (index < 0) throw new InvalidOperationException("The CSS line is absent from the shaped paragraph.");
                NativeLine = (uint)index;
                Width = paragraph.Lines[index].Metrics.Line.Width;
                Baseline = paragraph.Lines[index].Metrics.Baseline;
            }
            else
            {
                if (!owner.Format!.HitTestTextPosition(Projection.Text, 10000000, (float)owner.Height, (uint)first, false, out var hit))
                    throw new InvalidOperationException("Cannot obtain the CSS paragraph's caret geometry.");
                NativeY = hit.CaretY; Baseline = owner.Baseline;
                double left = double.PositiveInfinity, right = double.NegativeInfinity;
                foreach (int source in owner.Boundaries)
                {
                    if (source < Start || source >= end) continue;
                    double a = GlyphEdge(source, false), b = GlyphEdge(source, true);
                    left = Math.Min(left, Math.Min(a, b)); right = Math.Max(right, Math.Max(a, b));
                }
                Width = double.IsFinite(left) ? right - left : 0;
            }
        }

        internal (int Start, int Length) ClipNativeRange((int Start, int Length) range)
        {
            int first = Math.Clamp(range.Start, SliceNativeStart, SliceNativeEnd);
            int last = Math.Clamp(range.Start + range.Length, first, SliceNativeEnd);
            return (first, last - first);
        }

        internal double Boundary(int source, bool backward = false)
        {
            source = Start + Math.Clamp(source, 0, Length);
            if (Owner.Paragraph is { } paragraph)
            {
                int nativePos = Math.Clamp(Projection.NativePosition(source, backward), SliceNativeStart, SliceNativeEnd);
                return paragraph.Caret(NativeLine, nativePos, backward).X - NativeX;
            }
            if ((backward || source == Start + Length) && source > Start)
            {
                int previous = Array.BinarySearch(Owner.Boundaries, source);
                previous = previous >= 0 ? previous - 1 : ~previous - 1;
                return GlyphEdge(Owner.Boundaries[Math.Max(0, previous)], true);
            }
            return GlyphEdge(source, false);
        }

        private double GlyphEdge(int source, bool trailing)
            => Owner.Format!.HitTestTextPosition(Projection.Text, 10000000, (float)Owner.Height,
                (uint)Projection.NativePosition(source), trailing, out var hit) ? hit.CaretX : 0;

        internal int Hit(double x)
        {
            int position;
            if (Owner.Paragraph is { } paragraph)
                position = (int)paragraph.HitTest(NativeLine, x + NativeX).TextPosition;
            else
            {
                if (!Owner.Format!.HitTestPoint(Projection.Text, 10000000, (float)Owner.Height, (float)x,
                    (float)(NativeY + 1), out var hit)) return 0;
                position = (int)hit.TextPosition;
                if (hit.IsTrailingHit != 0 && position < Projection.Text.Length)
                    position = GraphemeClusters.GetBoundaries(Projection.Text).FirstOrDefault(next => next > position, Projection.Text.Length);
            }
            return Math.Clamp(Projection.SourcePosition(position) - Start, 0, Length);
        }

        internal IEnumerable<TextRangeMetrics> Selection(int start, int length)
        {
            var range = ClipNativeRange(Projection.NativeRange(Start + start, length));
            if (Owner.Paragraph is { } paragraph)
            {
                foreach (var rect in paragraph.Selection(NativeLine, range.Start, range.Length))
                {
                    var original = Projection.SourceRange((int)rect.TextPosition, (int)rect.Length);
                    int first = Math.Max(Start + start, original.Start), last = Math.Min(Start + start + length, original.Start + original.Length);
                    yield return new() { TextPosition = (uint)Math.Max(0, first - Start), Length = (uint)Math.Max(0, last - first),
                        X = rect.X - (float)NativeX, Width = rect.Width };
                }
                yield break;
            }
            var rectangles = new List<TextRangeMetrics>();
            for (int i = 0; i + 1 < Owner.Boundaries.Length; i++)
            {
                int first = Owner.Boundaries[i], last = Owner.Boundaries[i + 1];
                if (last <= Start + start || first >= Start + start + length) continue;
                // At a bidi boundary the next glyph's leading edge can be on
                // the other side of the line. Use this glyph's two edges.
                double a = GlyphEdge(first, false), b = GlyphEdge(first, true);
                if (Math.Abs(a - b) > .0001)
                    rectangles.Add(new() { TextPosition = (uint)(first - Start), Length = (uint)(last - first),
                        X = (float)Math.Min(a, b), Width = (float)Math.Abs(a - b) });
            }
            rectangles.Sort((a, b) => a.X.CompareTo(b.X));
            TextRangeMetrics? merged = null;
            foreach (var rect in rectangles)
            {
                if (merged is { } previous && rect.X <= previous.X + previous.Width + .0001)
                {
                    previous.Width = Math.Max(previous.X + previous.Width, rect.X + rect.Width) - previous.X;
                    int end = (int)Math.Max(previous.TextPosition + previous.Length, rect.TextPosition + rect.Length);
                    previous.TextPosition = Math.Min(previous.TextPosition, rect.TextPosition);
                    previous.Length = (uint)(end - previous.TextPosition); merged = previous;
                }
                else { if (merged is { } ready) yield return ready; merged = rect; }
            }
            if (merged is { } finalRectangle) yield return finalRectangle;
        }

        internal object? PlatformLine(int start, int length, Color color, double opacity, double cssBaseline, double cssHeight)
        {
            var range = ClipNativeRange(Projection.NativeRange(start, length));
            return Owner.Paragraph?.Lines[NativeLine].ForSource(range.Start, range.Length, color, opacity)
                .WithBrushLineBox(Baseline - cssBaseline, cssHeight);
        }
    }

    // The existing LTR tab formatter already owns source/display offsets and
    // its ruler. Keep that path for ordinary tabbed text. Linux's tab-free
    // compatibility justification needs retained paragraph shaping instead.
    private bool RequiresRetainedLtrParagraph => IsVerticalTextLayout || OperatingSystem.IsLinux() &&
        !_hasPreservedTabs && WantsCssBidiJustification;

    private bool TryCssBidiLine(TextLayoutLine line, out CssBidiLine result)
    {
        result = null!;
        // Installed vertical fragments already share an immutable paragraph.
        // A standalone line measured before allocation must not replace it.
        if (IsVerticalTextLayout && HasCssInlineFragments && InlineFragment(line)?.Shape is not null) return false;
        if (!TryCssBidiContext(out var context, out _)) return false;
        // A layout line is replaced when source/style layout changes. Check its
        // retained paragraph before scanning scopes or the entire source again.
        if (_cssBidiLines.TryGetValue(line, out result!)) return true;
        var root = this is InlineSourceText anonymous
            ? anonymous.ProjectedStyleSource ?? anonymous.SourceOwner : (DependencyObject)this;
        var mode = CssUnicodeBidiProperties.Value(root);
        bool nested = _inlines is not null && _inlines.Any(HasScope);
        bool directionalText = FlowDirection == FlowDirection.RightToLeft || NeedsBidiParagraph(_visualText);
        bool mapped = TryCssBidiPaintMap(out _, out var displayMap) && displayMap is { Count: > 0 } && !_hasPreservedTabs;
        if (mode == CssUnicodeBidi.Normal && !nested && !directionalText && !mapped && !RequiresRetainedLtrParagraph) return false;
        if (!nested && !directionalText && !mapped && !RequiresRetainedLtrParagraph && !CssInlineOuter && mode is CssUnicodeBidi.Embed or CssUnicodeBidi.Isolate) return false;
        if (line.Length == 0) return false;
        if (!SupportedCssBidiLayout())
        {
            if (mode != CssUnicodeBidi.Normal || nested)
                CssBidiUnavailable("This combination needs shared paragraph bidi layout (inline boxes, transformed paint text, special shaping or unsupported ink).");
            return false;
        }
        if (!_cssBidiAttempted)
        {
            _cssBidiAttempted = true;
            // CSS owns each row's height and baseline. A query for a different
            // row must not impose that row's minimum height on the paragraph.
            var value = CreateCssBidiParagraph(context, mode, 1, softBreaks: true);
            if (value is not { IsSupported: true })
            {
                value?.Dispose();
                if (mode != CssUnicodeBidi.Normal || nested)
                    CssBidiUnavailable("The native text backend does not provide the required direction and caret operations.");
                return false;
            }
            _cssBidiParagraph = value;
        }
        if (_cssBidiParagraph is null) return false;
        result = new(_cssBidiParagraph, line, _paintOffsets);
        _cssBidiLines[line] = result;
        return true;
    }

    private bool TryCssBidiContext(out RenderContext context, out Brush? brush)
    {
        context = null!; brush = null!;
        if (RenderContext.Current is not { IsValid: true } current) return false;
        context = current; brush = Foreground;
        var options = ((int)TextOptions.GetTextRenderingMode(this), (int)TextOptions.GetTextFormattingMode(this), (int)TextOptions.GetTextHintingMode(this));
        if (!ReferenceEquals(_cssBidiContext, context) ||
            _cssBidiFontEpoch != TextMeasurement.FontCacheEpoch || _cssBidiOptions != options)
        {
            ClearCssBidiLines(); _cssBidiContext = context;
            _cssBidiFontEpoch = TextMeasurement.FontCacheEpoch; _cssBidiOptions = options;
        }
        return true;
    }

    private bool TryCssBidiParagraphOverflow(in TextLayoutLine line,out double overflow)
    {
        overflow=0;
        if (!_cssBidiMeasurementAttempted)
            TryMeasureCssBidiRange(line.StartIndex,line.Length,true,out _);
        if (_cssBidiMeasurement is null||!TryCssBidiLine(line,out var bidi)) return false;
        int end=line.StartIndex+line.Length;
        if(end<=line.StartIndex)return true;
        int last=GraphemeClusters.PreviousBoundary(_visualText,end);
        if(!Rune.TryGetRuneAt(_visualText,last,out var rune)||!Rune.IsWhiteSpace(rune))return true;
        var range=bidi.Projection.NativeRange(last,end-last);
        if(bidi.Owner.Paragraph is {} paragraph&&
            paragraph.Caret(bidi.NativeLine,range.Start+range.Length,true).X<paragraph.Caret(bidi.NativeLine,range.Start,false).X)
            overflow=Math.Max(0,bidi.Width-line.Width-line.HangingStart-line.HangingEnd);
        return true;
    }

    private bool SupportedCssBidiLayout()
        => !(this is InlineSourceText || HasCssInlineFragments || HasCssInlineBoxes ||
            TextTrimming != TextTrimming.None || HasSplitBidiPaintWrap() || _layoutLines.Any(value => value.SoftHyphenVisible || value.HyphenChange is { HasChanges: true }) ||
            (_usesCapsShaping && !OperatingSystem.IsMacOS()) || !SupportedCssBidiPaint() ||
            (FontStretch.ToOpenTypeStretch() != 5 && !OperatingSystem.IsMacOS()) ||
            !SupportedCssBidiFonts() || !SupportedCssBidiSpacing());

    private bool TryMeasureCssBidiRange(int start, int length, bool includeTrailingWhitespace, out double width, double tabOrigin = 0)
    {
        width = 0;
        if (length <= 0 || !TryCssBidiContext(out var context, out _)) return false;
        if (!_cssBidiMeasurementAttempted)
        {
            _cssBidiMeasurementAttempted = true;
            var root = this is InlineSourceText anonymous
                ? anonymous.ProjectedStyleSource ?? anonymous.SourceOwner : (DependencyObject)this;
            var mode = CssUnicodeBidiProperties.Value(root);
            bool nested = _inlines is not null && _inlines.Any(HasScope);
            bool directional = FlowDirection == FlowDirection.RightToLeft || NeedsBidiParagraph(_visualText);
            bool mapped = TryCssBidiPaintMap(out _, out var displayMap) && displayMap is { Count: > 0 } && !_hasPreservedTabs;
            if (mode == CssUnicodeBidi.Normal && !nested && !directional && !mapped && !RequiresRetainedLtrParagraph ||
                !nested && !directional && !mapped && !RequiresRetainedLtrParagraph && !CssInlineOuter && mode is CssUnicodeBidi.Embed or CssUnicodeBidi.Isolate ||
                !SupportedCssBidiLayout()) return false;
            var value = CreateCssBidiParagraph(context, mode, 1, softBreaks: false, applySpacing: false);
            if (value?.Paragraph is not { } paragraph) { value?.Dispose(); return false; }
            _cssBidiMeasurement = value;
            var prefix = CssBidiNaturalAdvances(value);
            _cssBidiAdvances = prefix;
            if (_hasLetterSpacing || _hasWordSpacing || _hasPreservedTabs || _usesAutospaceShaping)
                _cssBidiSpacing = new(value, BuildCssBidiSpacing(value.Projection).Sources, prefix, this);
        }
        if (_cssBidiAdvances is not { } advances) return false;
        int end = Math.Min(_visualText.Length, start + length);
        if (!includeTrailingWhitespace)
            while (end > start && CombinedUnitAt(end - 1) is null && IsBreakableWhitespace(_visualText[end - 1])) end--;
        width = Math.Max(0, advances[end] - advances[start] + (_cssBidiSpacing?.Extra(start, end, tabOrigin) ?? 0)); return true;
    }

    private static double[] CssBidiNaturalAdvances(CssBidiParagraph value)
    {
        var paragraph = value.Paragraph!;
        var prefix = new double[value.Projection.Source.Length+1]; uint line = 0;
        for (int i = 0; i+1 < value.Boundaries.Length; i++)
        {
            int first = value.Boundaries[i], last = value.Boundaries[i+1];
            var source = value.Projection.NativeRange(first,last-first);
            while (line+1 < paragraph.Lines.Length && source.Start >=
                paragraph.Lines[line].Metrics.Line.TextPosition+paragraph.Lines[line].Metrics.Line.Length) line++;
            double advance = paragraph.Selection(line,source.Start,source.Length).Sum(rect => (double)rect.Width);
            for (int at=first+1; at<last; at++) prefix[at]=prefix[first];
            prefix[last]=prefix[first]+advance;
        }
        return prefix;
    }

    private static bool HasScope(Inline inline) => CssUnicodeBidiProperties.Value(inline) != CssUnicodeBidi.Normal ||
        inline is Span span && span.Inlines.Any(HasScope);

    private bool SupportedCssBidiFonts()
        => FontSize > 0 && GetInlineTextRanges().All(range => range.Run.FontSize > 0 &&
            (OperatingSystem.IsMacOS() || range.Run.FontStretch.ToOpenTypeStretch() == 5) &&
            !(range.Start > 0 && range.Start < _visualText.Length && char.IsLowSurrogate(_visualText[range.Start]) &&
              char.IsHighSurrogate(_visualText[range.Start - 1])));

    private static bool SupportedInk(Brush? brush)
        => brush is null or SolidColorBrush or LinearGradientBrush or RadialGradientBrush;

    private bool TryGetCssBidiPaint(out string paint, bool allowConditionalHyphens = false)
        => TryCssBidiPaintMap(out paint, out _, allowConditionalHyphens);

    private CssBidiParagraph? CreateCssBidiParagraph(RenderContext context, CssUnicodeBidi mode, double height, bool softBreaks,
        bool applySpacing = true)
    {
        if (!TryGetCssBidiPaint(out string paint)) { CssBidiUnavailable("The paint-to-source projection needs bidi integration."); return null; }
        var projection = CreateCssBidiProjection(paint, mode, softBreaks);
        bool wantsJustification = WantsCssBidiJustification;
        bool suppressLigatures = wantsJustification && SuppressJustificationLigatures(paint,
            GetInlineTextRanges().Select(range => (DependencyObject)range.Run).Prepend(this));
        var spans = new List<NativeTextParagraph.Span>(); int cursor = 0;
        NativeTextParagraph.Span? pendingFont = null; var pendingText = new StringBuilder();
        foreach (var range in GetInlineTextRanges())
        {
            int first = Math.Clamp(range.Start, cursor, paint.Length), last = Math.Clamp(range.End, first, paint.Length);
            if (first > cursor) Add(null, cursor, first);
            Add(range.Run, first, last); cursor = last;
        }
        if (cursor < paint.Length) Add(null, cursor, paint.Length);
        FlushFont();
        if (spans.Count == 0) return null;
        NativeMethods.ParagraphAdvanceUnit[]? advances = applySpacing &&
            (_hasLetterSpacing || _hasWordSpacing || _hasPreservedTabs || _usesAutospaceShaping || softBreaks && wantsJustification)
            ? BuildCssBidiSpacing(projection,usedLines:softBreaks).Units : null;
        if (applySpacing && (_hasPreservedTabs || _usesAutospaceShaping))
        {
            using var natural = Make(null);
            if (natural.Paragraph is null) return null;
            var spacing = BuildCssBidiSpacing(projection,usedLines:softBreaks);
            var tabs = new CssBidiSpacingMetrics(natural,spacing.Sources,CssBidiNaturalAdvances(natural),this);
            advances = _usesAutospaceShaping ? tabs.AutospaceUnits(spacing.Units) : tabs.TabUnits(spacing.Units);
        }
        if (softBreaks && wantsJustification && advances is not null)
        {
            var justified = new CssBidiJustification();
            var natural = Make(advances, justified);
            try
            {
                if (natural.Paragraph is not null)
                {
                    ApplyCssBidiJustification(natural, advances, justified);
                    if (justified.Expanded.Count == 0) return natural;
                    var final = Make(advances, justified);
                    natural.Dispose(); return final;
                }
                natural.Dispose();
            }
            catch { natural.Dispose(); throw; }
        }
        return Make(advances);

        CssBidiParagraph Make(NativeMethods.ParagraphAdvanceUnit[]? planned, CssBidiJustification? justified = null)
        {
            string baseFamily = _usesCapsShaping
                ? CapsContextFamily(FontFamily.GetRenderingSource(this), 0, _visualText.Length, 0)
                : FontFamily.GetRenderingSource(this);
            return new(context, baseFamily, FontSize, FontWeight.ToOpenTypeWeight(),
                FontStyle.ToOpenTypeStyle(), UsedTextFlowDirection, height, projection, spans, !_usesInlineFontLayout && !_usesCapsShaping, this, planned, justified,
                NativeCombinedUnits(projection), _combinedTextBoundaries);
        }

        void Add(Run? run, int first, int last)
        {
            if (last <= first) return;
            if (_hasPositionRequest)
            {
                byte posMode = PositionRunAt(first);
                for (int at = first + 1; at < last; at++)
                {
                    if (PositionRunAt(at) != posMode)
                    {
                        AddSlice(run, first, at);
                        first = at;
                        posMode = PositionRunAt(at);
                    }
                }
            }
            AddSlice(run, first, last);
        }

        void AddSlice(Run? run, int first, int last)
        {
            if (last <= first) return;
            var range = projection.NativeStyleRange(first, last - first);
            string family = run is null ? FontFamily.GetRenderingSource(this) : GetRunFontFamily(run);
            if (suppressLigatures) family = JustificationFont(family, run ?? (DependencyObject)this);
            if (_usesCapsShaping) family = CapsContextFamily(family, 0, _visualText.Length, first);
            var span = new NativeTextParagraph.Span("", family,
                run is null ? FontSize : GetRunFontSize(run),
                run?.FontWeight.ToOpenTypeWeight() ?? FontWeight.ToOpenTypeWeight(),
                run?.FontStyle.ToOpenTypeStyle() ?? FontStyle.ToOpenTypeStyle(), Colors.Black,
                TextOrientation: UsesVerticalTypography ? (int)CssWritingModeProperties.GetTextOrientation(run ?? TextWritingSource) : null);
            // Color is paint-only. Keep kerning and ligatures continuous across
            // source runs whose resolved font is identical.
            if (pendingFont is { } previous && previous != span) FlushFont();
            pendingFont = span; pendingText.Append(projection.Text, range.Start, range.Length);
        }
        void FlushFont()
        {
            if (pendingFont is { } font && pendingText.Length > 0)
                spans.Add(font with { Text = pendingText.ToString() });
            pendingText.Clear();
        }
    }

    private CssBidiProjection CreateCssBidiProjection(string paint, CssUnicodeBidi mode, bool softBreaks)
    {
        var scopes = new List<CssBidiProjection.Scope> { new(0, paint.Length, mode, FlowDirection, Inline: CssInlineOuter) };
        if (_inlines is not null) { int offset = 0; foreach (var inline in _inlines) Collect(inline, ref offset); }
        var breaks = new List<int>();
        var displayBreaks = new List<(int SourcePosition, int DisplayOffset)>();
        if (softBreaks)
            for (int i = 1; i < _layoutLines.Count; i++)
            {
                var before = _layoutLines[i - 1]; var after = _layoutLines[i];
                if (before.HasLineBreakAfter) continue;
                if (before.PaintWrap is { } beforePaint && after.PaintWrap is { } afterPaint &&
                    beforePaint.PaintEnd == afterPaint.PaintStart && _paintOffsets is not null &&
                    after.StartIndex >= 0 && after.StartIndex < paint.Length)
                {
                    int sourcePaintStart = _paintOffsets[after.StartIndex];
                    int displayOffset = afterPaint.PaintStart - sourcePaintStart;
                    if (displayOffset > 0)
                    {
                        displayBreaks.Add((after.StartIndex, displayOffset));
                        continue;
                    }
                }
                if (before.StartIndex + before.Length == after.StartIndex &&
                    after.StartIndex > 0 && after.StartIndex < paint.Length)
                    breaks.Add(after.StartIndex);
            }
        TryCssBidiPaintMap(out _, out var replacements, allowConditionalHyphens: true);
        return new CssBidiProjection(paint, scopes, breaks, displayReplacements: replacements, softDisplayBreaks: displayBreaks);

        void Collect(Inline inline, ref int offset)
        {
            int first = offset, index = scopes.Count; scopes.Add(default);
            if (inline is Run run) offset += run.Text?.Length ?? 0;
            else if (inline is Span span) foreach (var child in span.Inlines) Collect(child, ref offset);
            else if (inline is LineBreak or InlineUIContainer) offset++;
            var value = CssUnicodeBidiProperties.Value(inline);
            scopes[index] = offset > first && value != CssUnicodeBidi.Normal
                ? new(first, offset - first, value, (FlowDirection)inline.GetValue(FlowDirectionProperty)!, true)
                : new(0, 0, CssUnicodeBidi.Normal, FlowDirection.LeftToRight);
        }
    }

    private static bool NeedsBidiParagraph(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            // FEFF is also our hidden-whitespace slot. It is BN, not an Arabic
            // presentation form, and must not opt ordinary Latin text into bidi.
            if (value is >= 0x0590 and <= 0x08FF or >= 0xFB1D and <= 0xFDFF or >= 0xFE70 and <= 0xFEFE or
                >= 0x10800 and <= 0x10FFF or >= 0x1E800 and <= 0x1EEFF or 0x200E or 0x200F or
                >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069) return true;
        }
        return false;
    }

    private void CssBidiUnavailable(string detail)
        => CssDiagnostics.Report("unicode-bidi", CssDiagnosticReason.LossyConversion, GetType(), detail);

    private bool DrawCssBidiLine(DrawingContext context, TextLayoutLine line, double x, double y)
    {
        if (!TryCssBidiLine(line, out var bidi)) return false;
        // Paint support cannot change bidi geometry or wrap decisions. Check
        // it on every draw, also when a retained line predates a brush change.
        if (!SupportedInk(Foreground) || GetInlineTextRanges().Any(range => !SupportedInk(range.Run.Foreground ?? Foreground)))
        {
            CssBidiUnavailable("This foreground brush needs retained paragraph paint integration.");
            return false;
        }
        Brush brush = Foreground ?? Brushes.Transparent;
        var visible = new List<(int Start, int End)> { (line.StartIndex, line.StartIndex + line.Length) };
        byte visibility = 1;
        if (DocumentWritingToolsOwner is { } owner &&
            MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var ranges))
        {
            if (MacOSWritingToolsPresentation.UsesShapedGlyphRanges)
            {
                var requested = ranges.SelectMany(range => DocumentWritingToolsRanges(range.Start, range.Length)).ToArray();
                visibility = preview ? (byte)1 : (byte)2;
                visible = requested.Select(range => (Start: Math.Max(line.StartIndex, range.Start),
                    End: (int)Math.Min(line.StartIndex + line.Length, (long)range.Start + range.Length))).Where(range => range.End > range.Start).ToList();
            }
            else
            {
                using var clip = PushDocumentWritingToolsClip(context, preview, ranges);
                Paint(); return true;
            }
        }
        Paint(); return true;

        void Paint()
        {
            var glyphs = new List<uint>();
            foreach (var source in visible)
            {
                var projected = bidi.ClipNativeRange(bidi.Projection.NativeRange(source.Start, source.End - source.Start));
                glyphs.Add((uint)projected.Start); glyphs.Add((uint)projected.Length);
            }
            var filters = glyphs.ToArray();
            int cursor = line.StartIndex, end = cursor + line.Length;
            int paintStart = cursor, paintEnd = cursor; Brush? paintInk = null;
            var ranges = GetInlineTextRanges();
            int first = 0, limit = ranges.Count;
            while (first < limit)
            {
                int middle = first + (limit - first) / 2;
                if (ranges[middle].End <= cursor) first = middle + 1; else limit = middle;
            }
            for (int i = first; i < ranges.Count && ranges[i].Start < end; i++)
            {
                var range = ranges[i];
                if (range.Start > cursor) Queue(cursor, Math.Min(range.Start, end), brush);
                int last = Math.Min(end, range.End);
                Queue(Math.Max(cursor, range.Start), last, range.Run.Foreground ?? brush);
                cursor = last;
            }
            if (cursor < end) Queue(cursor, end, brush);
            if (paintInk is not null) Draw(paintStart, paintEnd, paintInk);

            void Queue(int first, int last, Brush ink)
            {
                if (last <= first) return;
                if (paintInk is not null && first == paintEnd && (ReferenceEquals(paintInk, ink) ||
                    paintInk is SolidColorBrush a && ink is SolidColorBrush b && a.Color == b.Color && a.Opacity == b.Opacity))
                    paintEnd = last;
                else
                {
                    if (paintInk is not null) Draw(paintStart, paintEnd, paintInk);
                    paintStart = first; paintEnd = last; paintInk = ink;
                }
            }

            void Draw(int first, int last, Brush ink)
            {
                if (last <= first) return;
                var source = bidi.ClipNativeRange(bidi.Projection.NativeRange(first, last - first));
                var text = new FormattedText(bidi.Projection.Text, bidi.Owner.Family, FontSize)
                {
                    FontWeight = FontWeight.ToOpenTypeWeight(), FontStyle = FontStyle.ToOpenTypeStyle(), Foreground = ink,
                // Glyph ink can overhang its advance (marks, italics). A source
                // filter is a shaped slice, not an implicit clip at that advance.
                    MaxTextWidth = Math.Max(1, (bidi.Owner.Paragraph is null ? bidi.Owner.Width : bidi.Width) + 2 * bidi.Owner.MaximumFontSize),
                    MaxTextHeight = Math.Max(1, bidi.Owner.Paragraph is null ? bidi.Owner.Height : Math.Max(line.Height, TextPaintHeight(line))),
                    NativeNoWrap = true,
                    PlatformTextLine = bidi.PlatformLine(first, last - first, ink is SolidColorBrush solid ? solid.Color : Colors.White,
                        ink.Opacity, line.Baseline, line.Height),
                    FlowDirection = UsedTextFlowDirection, NaturalBidiDirection = bidi.Projection.NaturalParagraphDirection,
                    GlyphVisibility = visibility, GlyphRanges = bidi.Owner.Paragraph is null ? Slice(filters, source.Start, source.Length) : filters,
                };
                text.ApplyTextOptionsFrom(this);
                double nativeX = bidi.Owner.Paragraph?.Lines[bidi.NativeLine].Metrics.Line.X ?? 0;
                context.DrawText(text, new(x - nativeX, y + line.Baseline - bidi.Baseline - bidi.NativeY));
            }
        }

        static uint[] Slice(uint[] filters, int start, int length)
        {
            var result = new List<uint>();
            for (int i = 0; i < filters.Length; i += 2)
            {
                uint first = Math.Max((uint)start, filters[i]);
                uint last = Math.Min((uint)(start + length), filters[i] + filters[i + 1]);
                if (last > first) { result.Add(first); result.Add(last - first); }
            }
            return result.ToArray();
        }
    }
}
