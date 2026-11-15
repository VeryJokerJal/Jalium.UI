using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // An editor borrows the normal text layout/shaping engine without adding
    // a source node, visual child, selection state or automation element.
    internal sealed class EditorLayout : IDisposable
    {
        private readonly TextBlock _view;
        private readonly string _text;
        private readonly List<(TextLayoutLine Line, double X, double Y, NativeTextParagraph? Native)> _rows = [];
        internal double Width { get; }
        internal double Height { get; private set; }
        internal double Baseline => _rows.Count == 0 ? Height * .8 : _rows[0].Line.Baseline;
        internal double ExtentWidth => _rows.Count == 0 ? 0 : _rows.Max(row => row.X + row.Line.Width);
        internal int Count => Math.Max(1, _rows.Count);
        internal IEnumerable<(NativeTextParagraph Paragraph, int Offset)> WordParagraphs
            => _rows.Where(row => row.Native is not null).Select(row => (row.Native!, row.Line.StartIndex));

        internal EditorLayout(Control owner, string text, double width, TextWrapping wrap, TextAlignment alignment, Brush ink)
        {
            _text = text;
            var family = owner.FontFamily?.GetRenderingSource(owner) ?? FrameworkElement.DefaultFontFamilyName;
            var metrics = TextMeasurement.GetFontMetrics(family, owner.FontSize, owner.FontWeight.ToOpenTypeWeight(), owner.FontStyle.ToOpenTypeStyle());
            _view = new TextBlock
            {
                DocumentFontSource = owner, Text = text, Foreground = ink,
                FontFamily = owner.FontFamily, FontSize = owner.FontSize, FontWeight = owner.FontWeight,
                FontStyle = owner.FontStyle, FontStretch = owner.FontStretch, FlowDirection = owner.FlowDirection,
                TextWrapping = wrap, TextAlignment = alignment, TextTrimming = TextTrimming.None,
                LineHeight = metrics.LineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            };
            _view.SetRootDpi(owner.DpiScale);
            _view.SetValue(CssFlowProperties.LetterSpacingProperty, owner.GetValue(CssFlowProperties.LetterSpacingProperty));
            _view.SetValue(CssFlowProperties.WordSpacingProperty, owner.GetValue(CssFlowProperties.WordSpacingProperty));
            _view.SetValue(CssFlowProperties.WhiteSpaceProperty, CssWhiteSpaceCollapse.Preserve);
            _view.SetValue(CssFlowProperties.TextWrapModeProperty, wrap == TextWrapping.NoWrap ? CssTextWrapMode.NoWrap : CssTextWrapMode.Wrap);
            Width = Math.Max(1, width);
            Layout();
            if (wrap == TextWrapping.NoWrap && ExtentWidth > Width)
            {
                Width = ExtentWidth; Layout();
            }
            void Layout()
            {
                _view.Measure(new(Width, double.PositiveInfinity));
                _view.Arrange(new(0, 0, Width, Math.Max(1, _view.DesiredSize.Height)));
                _view.EnsureLayout(_view.GetLayoutConstraintWidth(Width));
                _rows.Clear(); Height = 0;
                foreach (var line in _view._layoutLines)
                {
                    _view.TryShapedSpacing(line.StartIndex, line.StartIndex + line.Length, line.Height, out var native);
                    _rows.Add((line, _view.GetLineOriginX(line, Width), Height, native));
                    Height += line.Height;
                }
                Height = Math.Max(1, Height);
            }
        }

        private int RowIndex(int position, bool backward)
        {
            for (var i = 0; i + 1 < _rows.Count; i++)
            {
                var next = _rows[i + 1].Line.StartIndex;
                if (position < next || backward && position == next) return i;
            }
            return Math.Max(0, _rows.Count - 1);
        }

        internal bool Row(uint position, bool backward, out TextLineMetrics result)
        {
            if (_rows.Count == 0) { result = new() { Height = (float)Height }; return true; }
            var row = _rows[RowIndex((int)position, backward)];
            result = new() { TextPosition = (uint)row.Line.StartIndex, Length = (uint)row.Line.Length,
                X = (float)row.X, Y = (float)row.Y, Width = (float)row.Line.Width, Height = (float)row.Line.Height };
            return true;
        }

        internal bool Caret(uint position, bool trailing, out TextHitTestResult result)
        {
            var index = Math.Clamp((int)position, 0, _text.Length);
            if (trailing) index = GraphemeClusters.NextBoundary(_text, index);
            if (_rows.Count == 0) { result = new() { CaretHeight = (float)Height }; return true; }
            var row = _rows[RowIndex(index, trailing)];
            var x = row.Native is { } native
                ? row.X + native.Caret(0, Math.Clamp(index - row.Line.StartIndex, 0, row.Line.Length), trailing).X - native.Lines[0].Metrics.Line.X
                : _view.GetInlineBoundaryX(row.Line, row.X, index);
            result = new() { TextPosition = (uint)index, CaretX = (float)x, CaretY = (float)row.Y,
                CaretHeight = (float)row.Line.Height, IsInside = 1 };
            return true;
        }

        internal bool Hit(float x, float y, out TextHitTestResult result)
        {
            var index = _view.GetCharacterIndexFromPosition(new(x, y));
            Caret((uint)index, false, out result); return true;
        }

        internal TextRangeMetrics[] Selection(int start, int length)
        {
            if (length <= 0) return [];
            var result = new List<TextRangeMetrics>();
            foreach (var row in _rows)
            {
                var first = Math.Max(start, row.Line.StartIndex); var end = (int)Math.Min((long)start + length, row.Line.StartIndex + row.Line.Length);
                if (end <= first) continue;
                if (row.Native is { } native)
                {
                    foreach (var box in native.Selection(0, first - row.Line.StartIndex, end - first))
                        result.Add(box with { TextPosition = (uint)first, Length = (uint)(end - first),
                            X = (float)(row.X + box.X - native.Lines[0].Metrics.Line.X), Y = (float)row.Y, Height = (float)row.Line.Height });
                }
                else
                {
                    var a = _view.GetInlineBoundaryX(row.Line, row.X, first); var b = _view.GetInlineBoundaryX(row.Line, row.X, end);
                    result.Add(new() { TextPosition = (uint)first, Length = (uint)(end - first), X = (float)Math.Min(a, b),
                        Y = (float)row.Y, Width = (float)Math.Abs(b - a), Height = (float)row.Line.Height });
                }
            }
            return result.ToArray();
        }

        internal bool Range(uint start, uint length, out TextRangeMetrics result)
        {
            if (length == 0) { Caret(start, false, out var caret); result = new() { TextPosition = start, X = caret.CaretX, Y = caret.CaretY, Height = caret.CaretHeight }; return true; }
            var boxes = Selection((int)start, (int)length);
            if (boxes.Length == 0) { result = default; return false; }
            var top = boxes[0].Y; var line = boxes.Where(box => box.Y == top).ToArray();
            var left = line.Min(box => box.X); var right = line.Max(box => box.X + box.Width);
            result = new() { TextPosition = start, Length = length, X = left, Y = top, Width = right - left, Height = line.Max(box => box.Height) }; return true;
        }

        internal void Draw(DrawingContext context, FormattedText source, Point origin)
        {
            if (source.Foreground is not SolidColorBrush || _rows.Any(row => row.Line.Length > 0 && row.Native is null))
            {
                DrawFallback(context, source, origin);
                return;
            }
            foreach (var row in _rows)
            {
                if (row.Native is not { } native || row.Line.Length == 0 || source.Foreground is not SolidColorBrush ink) continue;
                var masks = new List<uint>();
                var sourceRanges = source.GlyphRanges ?? [];
                for (var i = 0; i + 1 < sourceRanges.Length; i += 2)
                {
                    var start = Math.Max(row.Line.StartIndex, (int)sourceRanges[i]);
                    var end = (int)Math.Min((long)row.Line.StartIndex + row.Line.Length, (long)sourceRanges[i] + sourceRanges[i + 1]);
                    if (end > start) { masks.Add((uint)(start - row.Line.StartIndex)); masks.Add((uint)(end - start)); }
                }
                if (source.GlyphVisibility == 1 && masks.Count == 0) continue;
                var line = native.Lines[0]; var text = new FormattedText(native.Text, source.FontFamily, source.FontSize)
                {
                    Foreground = ink, FontWeight = source.FontWeight, FontStyle = source.FontStyle,
                    MaxTextWidth = Math.Max(1, row.Line.Width), MaxTextHeight = Math.Max(1, row.Line.GlyphHeight),
                    NativeNoWrap = true, GlyphRanges = masks.ToArray(), GlyphVisibility = source.GlyphVisibility,
                    PlatformTextLine = line.ForSource(0, native.Text.Length, ink.Color, ink.Opacity, masks.ToArray(), source.GlyphVisibility),
                };
                context.DrawText(text, new(origin.X + row.X - line.Metrics.Line.X,
                    origin.Y + row.Y + row.Line.Baseline - line.Metrics.Baseline));
            }
        }

        private void DrawFallback(DrawingContext context, FormattedText source, Point origin)
        {
            var paint = new EditorInkContext(context, source.Foreground);
            foreach (var row in _rows)
            {
                var first = row.Line.StartIndex; var last = first + row.Line.Length;
                var spans = new List<(int Start, int End)>();
                var ranges = source.GlyphRanges ?? [];
                if (source.GlyphVisibility != 1) spans.Add((first, last));
                for (var i = 0; i + 1 < ranges.Length; i += 2)
                {
                    var start = (int)Math.Clamp((long)ranges[i], first, last);
                    var end = (int)Math.Clamp((long)ranges[i] + ranges[i + 1], first, last);
                    if (end <= start) continue;
                    if (source.GlyphVisibility == 1) spans.Add((start, end));
                    else if (source.GlyphVisibility == 2)
                        spans = spans.SelectMany(span => new[] { (span.Start, Math.Min(span.End, start)),
                            (Math.Max(span.Start, end), span.End) }).Where(span => span.Item2 > span.Item1).ToList();
                }
                if (spans.Count == 0) continue;
                var full = spans.Count == 1 && spans[0] == (first, last);
                if (!full)
                {
                    var clip = new GeometryGroup { FillRule = FillRule.Nonzero };
                    foreach (var span in spans)
                    {
                        var a = _view.GetInlineBoundaryX(row.Line, row.X, span.Start);
                        var b = _view.GetInlineBoundaryX(row.Line, row.X, span.End);
                        clip.Children.Add(new RectangleGeometry(new Rect(origin.X + Math.Min(a, b),
                            origin.Y + row.Y - row.Line.Height, Math.Abs(b - a), 3 * row.Line.Height)));
                    }
                    context.PushClip(clip);
                }
                try { _view.DrawStyledInlineLine(paint, row.Line, origin.X + row.X, origin.Y + row.Y); }
                finally { if (!full) context.Pop(); }
            }
        }

        private sealed class EditorInkContext(DrawingContext inner, Brush? ink) : DrawingContextAdapter
        {
            public override void DrawText(FormattedText text, Point origin)
                => inner.DrawText(text.CreateRenderSnapshot(ink), origin);
            public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => inner.DrawRectangle(brush, pen, rect);
            public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => inner.DrawGeometry(brush, pen, geometry);
            public override void DrawLine(Pen pen, Point start, Point end) => inner.DrawLine(pen, start, end);
            public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => inner.DrawRoundedRectangle(brush, pen, rect, x, y);
            public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) => inner.DrawEllipse(brush, pen, center, x, y);
            public override void DrawImage(ImageSource image, Rect rect) => inner.DrawImage(image, rect);
            public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) => inner.DrawBackdropEffect(rect, effect, radius);
            public override void PushClip(Geometry clip) => inner.PushClip(clip);
            public override void PushTransform(Transform transform) => inner.PushTransform(transform);
            public override void PushOpacity(double opacity) => inner.PushOpacity(opacity);
            public override void Pop() => inner.Pop();
            public override void Close() { }
        }

        public void Dispose() => _view.ClearNativeBidiLines();
    }
}
