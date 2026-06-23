using System.Linq;
using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal readonly record struct MarkdownTextStyle(bool Bold, bool Italic, bool Code, Uri? LinkUri);
internal sealed record MarkdownTextSpan(string Text, MarkdownTextStyle Style, bool IsLineBreak = false);

internal sealed class MarkdownTextRenderer : FrameworkElement, IMarkdownSelectable
{
    private IReadOnlyList<MarkdownTextSpan> _spans = Array.Empty<MarkdownTextSpan>();
    private MarkdownTextLayout? _cachedLayout;
    private double _cachedWidth = double.NaN;
    private Pen? _cachedLinkUnderlinePen;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;

    public MarkdownTextRenderer()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler), true);
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler), true);
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), true);
        Focusable = false;
    }

    public IReadOnlyList<MarkdownTextSpan> Spans
    {
        get => _spans;
        set
        {
            _spans = value ?? Array.Empty<MarkdownTextSpan>();
            InvalidateLayout();
        }
    }

    public string TextFontFamily { get; set; } = FrameworkElement.DefaultFontFamilyName;
    public string MonoFontFamily { get; set; } = "Cascadia Code";
    public double TextFontSize { get; set; } = 14;
    public FontWeight DefaultFontWeight { get; set; } = FontWeights.Normal;
    public FontStyle DefaultFontStyle { get; set; } = FontStyles.Normal;
    public Brush? ForegroundBrush { get; set; }
    public Brush? LinkForegroundBrush { get; set; }
    public Brush? CodeBackgroundBrush { get; set; }
    public Brush? SelectionBrush { get; set; }
    public bool Wrap { get; set; } = true;
    public bool PreserveWhitespace { get; set; }
    public double LineHeightMultiplier { get; set; } = 1.5;

    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Spans.Count == 0)
        {
            return Size.Empty;
        }

        var widthConstraint = Wrap && !double.IsInfinity(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : double.PositiveInfinity;
        var layout = EnsureLayout(widthConstraint);
        return new Size(layout.Width, layout.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widthConstraint = Wrap && finalSize.Width > 0
            ? finalSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);
        _ = EnsureLayout(widthConstraint);
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Spans.Count == 0)
        {
            return;
        }
        var dc = drawingContext;

        var widthConstraint = Wrap && RenderSize.Width > 0
            ? RenderSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);
        var layout = EnsureLayout(widthConstraint);

        // Pass 1: inline-code backgrounds, drawn beneath the selection so selected code still highlights.
        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.Style.Code && CodeBackgroundBrush != null)
                {
                    dc.DrawRoundedRectangle(CodeBackgroundBrush, null, placement.Bounds, 4, 4);
                }
            }
        }

        // Pass 2: one continuous selection highlight per line (no per-word gaps).
        if (_selectionEnd > _selectionStart && _selectionStart >= 0 && SelectionBrush != null)
        {
            DrawSelectionHighlight(dc, layout);
        }

        // Pass 3: text and link underlines on top of everything.
        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                var formattedText = CreateFormattedText(placement.Text, placement.Style);
                var textX = placement.Bounds.X + placement.TextOffsetX;
                var textY = placement.Bounds.Y + ((placement.Bounds.Height - placement.TextHeight) / 2);
                dc.DrawText(formattedText, new Point(textX, textY));

                if (placement.Style.LinkUri != null)
                {
                    var underlineBrush = LinkForegroundBrush ?? ForegroundBrush ?? new SolidColorBrush(Color.FromRgb(0, 102, 204));
                    _cachedLinkUnderlinePen ??= new Pen(underlineBrush, 1);
                    var underlineY = placement.Bounds.Y + placement.Bounds.Height - 2;
                    dc.DrawLine(_cachedLinkUnderlinePen, new Point(textX, underlineY), new Point(textX + placement.TextWidth, underlineY));
                }
            }
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        // A link shows the hand cursor; everywhere else the text is selectable, so use the I-beam.
        Cursor = TryGetLinkAt(e.GetPosition(this)) != null ? Jalium.UI.Cursors.Hand : Jalium.UI.Cursors.IBeam;
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        Cursor = Jalium.UI.Cursors.Arrow;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var uri = TryGetLinkAt(e.GetPosition(this));
        if (uri == null)
        {
            return;
        }

        LinkClicked?.Invoke(this, new MarkdownLinkClickedEventArgs(uri));
        e.Handled = true;
    }

    private Uri? TryGetLinkAt(Point point)
    {
        if (Spans.Count == 0)
        {
            return null;
        }

        var widthConstraint = Wrap && RenderSize.Width > 0
            ? RenderSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);
        var layout = EnsureLayout(widthConstraint);

        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.Style.LinkUri != null && placement.Bounds.Contains(point))
                {
                    return placement.Style.LinkUri;
                }
            }
        }

        return null;
    }

    private MarkdownTextLayout EnsureLayout(double widthConstraint)
    {
        if (_cachedLayout != null &&
            ((double.IsInfinity(widthConstraint) && double.IsInfinity(_cachedWidth)) ||
             Math.Abs(widthConstraint - _cachedWidth) < 0.1))
        {
            return _cachedLayout;
        }

        _cachedWidth = widthConstraint;
        _cachedLayout = CreateLayout(widthConstraint);
        return _cachedLayout;
    }

    private MarkdownTextLayout CreateLayout(double widthConstraint)
    {
        var layout = new MarkdownTextLayout();
        var maxWidth = double.IsInfinity(widthConstraint) || widthConstraint <= 0
            ? double.PositiveInfinity
            : widthConstraint;
        var currentLine = new MarkdownTextLine();
        var y = 0.0;

        foreach (var token in Tokenize())
        {
            if (token.IsLineBreak)
            {
                CommitLine(layout, ref currentLine, ref y, forceEmptyLine: true);
                continue;
            }

            AddToken(layout, ref currentLine, token, maxWidth, ref y);
        }

        CommitLine(layout, ref currentLine, ref y, forceEmptyLine: false);
        layout.Height = y;
        layout.Width = layout.Lines.Count == 0 ? 0 : layout.Lines.Max(static line => line.Width);
        return layout;
    }

    private void AddToken(MarkdownTextLayout layout, ref MarkdownTextLine currentLine, MarkdownToken token, double maxWidth, ref double y)
    {
        if (string.IsNullOrEmpty(token.Text))
        {
            return;
        }

        if (token.IsWhitespace && currentLine.Placements.Count == 0 && !PreserveWhitespace)
        {
            return;
        }

        var measurement = MeasureToken(token.Text, token.Style);
        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            !token.IsWhitespace &&
            currentLine.Width > 0 &&
            currentLine.Width + measurement.TotalWidth > maxWidth)
        {
            CommitLine(layout, ref currentLine, ref y, forceEmptyLine: false);
        }

        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            !token.IsWhitespace &&
            measurement.TotalWidth > maxWidth)
        {
            AddWrappedToken(layout, ref currentLine, token, maxWidth, ref y);
            return;
        }

        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            token.IsWhitespace &&
            currentLine.Width + measurement.TotalWidth > maxWidth)
        {
            return;
        }

        PlaceToken(ref currentLine, token.Text, token.Style, measurement);
    }

    private void AddWrappedToken(MarkdownTextLayout layout, ref MarkdownTextLine currentLine, MarkdownToken token, double maxWidth, ref double y)
    {
        var chunk = new StringBuilder();
        for (var index = 0; index < token.Text.Length; index++)
        {
            chunk.Append(token.Text[index]);
            var measurement = MeasureToken(chunk.ToString(), token.Style);
            if (chunk.Length > 1 && currentLine.Width + measurement.TotalWidth > maxWidth)
            {
                chunk.Length--;
                if (chunk.Length > 0)
                {
                    var committed = chunk.ToString();
                    PlaceToken(ref currentLine, committed, token.Style, MeasureToken(committed, token.Style));
                    CommitLine(layout, ref currentLine, ref y, forceEmptyLine: false);
                }

                chunk.Clear();
                chunk.Append(token.Text[index]);
            }
        }

        if (chunk.Length > 0)
        {
            var tail = chunk.ToString();
            PlaceToken(ref currentLine, tail, token.Style, MeasureToken(tail, token.Style));
        }
    }

    private void PlaceToken(ref MarkdownTextLine currentLine, string text, MarkdownTextStyle style, MarkdownTokenMeasurement measurement)
    {
        currentLine.Placements.Add(new MarkdownTokenPlacement(
            text,
            style,
            new Rect(currentLine.Width, 0, measurement.TotalWidth, measurement.TotalHeight),
            measurement.TextWidth,
            measurement.TextHeight,
            measurement.TextOffsetX));
        currentLine.Width += measurement.TotalWidth;
        currentLine.Height = Math.Max(currentLine.Height, measurement.TotalHeight);
    }

    private void CommitLine(MarkdownTextLayout layout, ref MarkdownTextLine currentLine, ref double y, bool forceEmptyLine)
    {
        if (currentLine.Placements.Count == 0)
        {
            if (forceEmptyLine || layout.Lines.Count == 0)
            {
                y += DefaultLineHeight;
            }
            currentLine = new MarkdownTextLine();
            return;
        }

        var placements = new MarkdownTokenPlacement[currentLine.Placements.Count];
        for (var index = 0; index < currentLine.Placements.Count; index++)
        {
            var placement = currentLine.Placements[index];
            placements[index] = placement with
            {
                Bounds = new Rect(placement.Bounds.X, y, placement.Bounds.Width, currentLine.Height)
            };
        }

        layout.Lines.Add(new MarkdownTextLineInfo(placements, currentLine.Width, currentLine.Height));
        y += currentLine.Height;
        currentLine = new MarkdownTextLine();
    }

    private IEnumerable<MarkdownToken> Tokenize()
    {
        foreach (var span in Spans)
        {
            if (span.IsLineBreak)
            {
                yield return new MarkdownToken(string.Empty, span.Style, IsWhitespace: false, IsLineBreak: true);
                continue;
            }

            var preserveWhitespace = PreserveWhitespace || span.Style.Code;
            foreach (var token in TokenizeSpan(span.Text, span.Style, preserveWhitespace))
            {
                yield return token;
            }
        }
    }

    private static IEnumerable<MarkdownToken> TokenizeSpan(string text, MarkdownTextStyle style, bool preserveWhitespace)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        if (preserveWhitespace)
        {
            var buffer = new StringBuilder();
            bool? isWhitespace = null;
            for (var index = 0; index < text.Length; index++)
            {
                var ch = text[index];
                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    if (buffer.Length > 0)
                    {
                        yield return new MarkdownToken(buffer.ToString(), style, IsWhitespace: isWhitespace == true, IsLineBreak: false);
                        buffer.Clear();
                        isWhitespace = null;
                    }

                    yield return new MarkdownToken(string.Empty, style, IsWhitespace: false, IsLineBreak: true);
                    continue;
                }

                var whitespace = ch == ' ' || ch == '\t';
                if (isWhitespace != null && isWhitespace != whitespace)
                {
                    yield return new MarkdownToken(buffer.ToString(), style, IsWhitespace: isWhitespace == true, IsLineBreak: false);
                    buffer.Clear();
                }

                isWhitespace = whitespace;
                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                yield return new MarkdownToken(buffer.ToString(), style, IsWhitespace: isWhitespace == true, IsLineBreak: false);
            }

            yield break;
        }

        var word = new StringBuilder();
        var pendingWhitespace = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
            {
                if (word.Length > 0)
                {
                    yield return new MarkdownToken(word.ToString(), style, IsWhitespace: false, IsLineBreak: false);
                    word.Clear();
                }

                pendingWhitespace = true;
                continue;
            }

            if (pendingWhitespace)
            {
                yield return new MarkdownToken(" ", style, IsWhitespace: true, IsLineBreak: false);
                pendingWhitespace = false;
            }

            word.Append(ch);
        }

        if (word.Length > 0)
        {
            yield return new MarkdownToken(word.ToString(), style, IsWhitespace: false, IsLineBreak: false);
        }
        else if (pendingWhitespace)
        {
            yield return new MarkdownToken(" ", style, IsWhitespace: true, IsLineBreak: false);
        }
    }

    private MarkdownTokenMeasurement MeasureToken(string text, MarkdownTextStyle style)
    {
        var formattedText = CreateFormattedText(text, style);
        TextMeasurement.MeasureText(formattedText);

        var horizontalPadding = style.Code ? 8 : 0;
        var verticalPadding = style.Code ? 4 : 0;
        var totalHeight = Math.Max(DefaultLineHeight, formattedText.Height + verticalPadding);

        return new MarkdownTokenMeasurement(
            formattedText.Width + horizontalPadding,
            totalHeight,
            formattedText.Width,
            formattedText.Height,
            style.Code ? 4 : 0);
    }

    private FormattedText CreateFormattedText(string text, MarkdownTextStyle style)
    {
        return new FormattedText(text, style.Code ? MonoFontFamily : TextFontFamily, TextFontSize)
        {
            Foreground = style.LinkUri != null
                ? LinkForegroundBrush ?? ForegroundBrush ?? new SolidColorBrush(Color.FromRgb(0, 102, 204))
                : ForegroundBrush ?? new SolidColorBrush(Color.Black),
            FontWeight = (style.Bold ? FontWeights.Bold : DefaultFontWeight).ToOpenTypeWeight(),
            FontStyle = (style.Italic ? FontStyles.Italic : DefaultFontStyle).ToOpenTypeStyle()
        };
    }

    private double DefaultLineHeight => Math.Max(1, TextFontSize * LineHeightMultiplier);

    private void InvalidateLayout()
    {
        _cachedLayout = null;
        _cachedWidth = double.NaN;
        InvalidateMeasure();
        InvalidateVisual();
    }

    #region Text selection (IMarkdownSelectable)

    private double CurrentWidthConstraint()
        => Wrap && RenderSize.Width > 0
            ? RenderSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);

    public int SelectableLength
    {
        get
        {
            if (Spans.Count == 0)
            {
                return 0;
            }
            return ComputeLength(EnsureLayout(CurrentWidthConstraint()));
        }
    }

    public string GetSelectionText(int start, int end)
    {
        if (Spans.Count == 0)
        {
            return string.Empty;
        }

        var text = BuildVisualText(EnsureLayout(CurrentWidthConstraint()));
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return end > start ? text.Substring(start, end - start) : string.Empty;
    }

    public void SetSelectionRange(int start, int end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (_selectionStart == start && _selectionEnd == end)
        {
            return;
        }

        _selectionStart = start;
        _selectionEnd = end;
        InvalidateVisual();
    }

    public void ClearSelectionRange()
    {
        if (_selectionStart < 0 && _selectionEnd < 0)
        {
            return;
        }

        _selectionStart = -1;
        _selectionEnd = -1;
        InvalidateVisual();
    }

    public bool TryHitTestCharacter(Point localPoint, out int charIndex)
    {
        charIndex = 0;
        if (Spans.Count == 0)
        {
            return false;
        }

        var layout = EnsureLayout(CurrentWidthConstraint());
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var lineIndex = -1;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            var placements = layout.Lines[i].Placements;
            if (placements.Count == 0)
            {
                continue;
            }

            var bounds = placements[0].Bounds;
            if (localPoint.Y < bounds.Y)
            {
                lineIndex = i;
                break;
            }
            if (localPoint.Y <= bounds.Y + bounds.Height)
            {
                lineIndex = i;
                break;
            }
        }

        if (lineIndex < 0)
        {
            lineIndex = layout.Lines.Count - 1;
        }

        var lineStart = 0;
        for (var i = 0; i < lineIndex; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                lineStart += placement.Text.Length;
            }
            lineStart += 1; // newline between lines
        }

        var line = layout.Lines[lineIndex];
        var local = 0;
        foreach (var placement in line.Placements)
        {
            var left = placement.Bounds.X;
            var right = placement.Bounds.X + placement.Bounds.Width;
            if (localPoint.X < left)
            {
                charIndex = lineStart + local;
                return true;
            }
            if (localPoint.X <= right)
            {
                var textLeft = placement.Bounds.X + placement.TextOffsetX;
                charIndex = lineStart + local + FindCharInText(placement.Text, placement.Style, localPoint.X - textLeft);
                return true;
            }
            local += placement.Text.Length;
        }

        charIndex = lineStart + local;
        return true;
    }

    private void DrawSelectionHighlight(DrawingContext dc, MarkdownTextLayout layout)
    {
        var running = 0;
        for (var li = 0; li < layout.Lines.Count; li++)
        {
            var line = layout.Lines[li];
            var hasSelection = false;
            double left = 0, right = 0, top = 0, height = 0;

            foreach (var placement in line.Placements)
            {
                var cs = running;
                var ce = running + placement.Text.Length;
                var a = Math.Max(_selectionStart, cs);
                var b = Math.Min(_selectionEnd, ce);
                if (a < b)
                {
                    // Use the placement's full box edges for whole-placement coverage (so the
                    // inter-word advance and inline-code padding are included with no gaps), and
                    // fall back to measured prefixes only for a partially-selected first/last token.
                    var textLeft = placement.Bounds.X + placement.TextOffsetX;
                    var startX = a == cs
                        ? placement.Bounds.X
                        : textLeft + MeasurePrefixWidth(placement.Text, a - cs, placement.Style);
                    var endX = b == ce
                        ? placement.Bounds.X + placement.Bounds.Width
                        : textLeft + MeasurePrefixWidth(placement.Text, b - cs, placement.Style);

                    if (!hasSelection)
                    {
                        hasSelection = true;
                        left = startX;
                        top = placement.Bounds.Y;
                        height = placement.Bounds.Height;
                    }
                    else
                    {
                        left = Math.Min(left, startX);
                        top = Math.Min(top, placement.Bounds.Y);
                        height = Math.Max(height, placement.Bounds.Height);
                    }
                    right = Math.Max(right, endX);
                }
                running = ce;
            }

            if (hasSelection)
            {
                dc.DrawRectangle(SelectionBrush, null, new Rect(left, top, Math.Max(1, right - left), height));
            }

            if (li < layout.Lines.Count - 1)
            {
                running += 1;
            }
        }
    }

    private int FindCharInText(string text, MarkdownTextStyle style, double targetX)
    {
        if (text.Length == 0 || targetX <= 0)
        {
            return 0;
        }

        var previous = 0.0;
        for (var i = 1; i <= text.Length; i++)
        {
            var width = MeasurePrefixWidth(text, i, style);
            if (targetX < (previous + width) / 2.0)
            {
                return i - 1;
            }
            previous = width;
        }

        return text.Length;
    }

    private double MeasurePrefixWidth(string text, int count, MarkdownTextStyle style)
    {
        if (count <= 0)
        {
            return 0;
        }
        if (count > text.Length)
        {
            count = text.Length;
        }

        var formatted = CreateFormattedText(text.Substring(0, count), style);
        TextMeasurement.MeasureText(formatted);
        return formatted.Width;
    }

    private static int ComputeLength(MarkdownTextLayout layout)
    {
        var total = 0;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                total += placement.Text.Length;
            }
            if (i < layout.Lines.Count - 1)
            {
                total += 1;
            }
        }
        return total;
    }

    private static string BuildVisualText(MarkdownTextLayout layout)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                sb.Append(placement.Text);
            }
            if (i < layout.Lines.Count - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    #endregion

    private readonly record struct MarkdownToken(string Text, MarkdownTextStyle Style, bool IsWhitespace, bool IsLineBreak);
    private readonly record struct MarkdownTokenMeasurement(double TotalWidth, double TotalHeight, double TextWidth, double TextHeight, double TextOffsetX);
    private sealed class MarkdownTextLine
    {
        public List<MarkdownTokenPlacement> Placements { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class MarkdownTextLayout
    {
        public List<MarkdownTextLineInfo> Lines { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed record MarkdownTextLineInfo(IReadOnlyList<MarkdownTokenPlacement> Placements, double Width, double Height);
    private sealed record MarkdownTokenPlacement(string Text, MarkdownTextStyle Style, Rect Bounds, double TextWidth, double TextHeight, double TextOffsetX);
}
