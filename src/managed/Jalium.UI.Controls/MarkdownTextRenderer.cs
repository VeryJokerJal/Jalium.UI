using System.Linq;
using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal readonly record struct MarkdownTextStyle(bool Bold, bool Italic, bool Code, Uri? LinkUri);
internal sealed record MarkdownTextSpan(string Text, MarkdownTextStyle Style, bool IsLineBreak = false);

internal sealed class MarkdownTextRenderer : FrameworkElement
{
    private IReadOnlyList<MarkdownTextSpan> _spans = Array.Empty<MarkdownTextSpan>();
    private MarkdownTextLayout? _cachedLayout;
    private double _cachedWidth = double.NaN;
    private Pen? _cachedLinkUnderlinePen;

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

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc || Spans.Count == 0)
        {
            return;
        }

        var widthConstraint = Wrap && RenderSize.Width > 0
            ? RenderSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);
        var layout = EnsureLayout(widthConstraint);

        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.Style.Code && CodeBackgroundBrush != null)
                {
                    dc.DrawRoundedRectangle(CodeBackgroundBrush, null, placement.Bounds, 4, 4);
                }

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
        Cursor = TryGetLinkAt(e.GetPosition(this)) != null ? Jalium.UI.Cursors.Hand : Jalium.UI.Cursors.Arrow;
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
