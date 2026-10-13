using Jalium.UI.Media;

namespace Jalium.UI.Interop;

/// <summary>Immutable native shaping for one text line, including glyph-cluster caret positions.</summary>
public sealed class TextLineLayout : IDisposable
{
    private readonly NativeTextParagraph _paragraph;
    private readonly RenderContext _context;
    private readonly long _fontEpoch;

    private TextLineLayout(NativeTextParagraph paragraph, RenderContext context)
    {
        _paragraph = paragraph;
        _context = context;
        _fontEpoch = TextMeasurement.FontCacheEpoch;
    }

    public string Text => _paragraph.Text;
    public double Width => _paragraph.Lines[0].Metrics.Line.Width;
    public bool IsCurrent => !_paragraph.IsDisposed && _context.IsValid &&
        ReferenceEquals(RenderContext.Current, _context) && _fontEpoch == TextMeasurement.FontCacheEpoch;

    /// <summary>Returns null when the platform or render context cannot provide native line shaping.</summary>
    public static TextLineLayout? TryCreate(string text, string fontFamily, double fontSize,
        int fontWeight = 400, int fontStyle = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fontFamily);
        if (!double.IsFinite(fontSize) || fontSize <= 0 || text.AsSpan().IndexOfAny('\r', '\n') >= 0)
            return null;
        var context = RenderContext.Current;
        if (!OperatingSystem.IsMacOS() || context is not { IsValid: true, Backend: RenderBackend.Metal }) return null;
        var paragraph = NativeTextParagraph.TryCreate(text.Length == 0 ? [] :
            [new(text, fontFamily, fontSize, fontWeight, fontStyle, Colors.White)],
            fontFamily, fontSize, 1, 1, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true);
        if (paragraph is null) return null;
        if (paragraph.Lines.Length != 1) { paragraph.Dispose(); return null; }
        return new TextLineLayout(paragraph, context);
    }

    /// <summary>Returns the leading insertion position; interior UTF-16 offsets snap to their grapheme start.</summary>
    public double GetCaretX(int textPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(textPosition, Text.Length);
        return _paragraph.Caret(0, textPosition, false).X;
    }

    /// <summary>Returns the nearest valid insertion offset in UTF-16 units.</summary>
    public int HitTest(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        return (int)_paragraph.HitTest(0, x).TextPosition;
    }

    public void Dispose() => _paragraph.Dispose();
}
