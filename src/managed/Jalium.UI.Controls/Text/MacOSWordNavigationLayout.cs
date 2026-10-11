using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Bounded shaped layout for plain editors' native word movement.</summary>
internal sealed class MacOSWordNavigationLayout : IDisposable
{
    private NativeTextParagraph? _paragraph;
    private long _fontEpoch;
    private (string Text, string Family, double Size, int Weight, int Style, double Width, double Height) _key;
    private RenderContext? _context;

    internal bool TryNavigate(string text, string family, double size, int weight, int style,
        double width, double height, int position, int start, int length, bool right,
        bool backward, out NativeMethods.ParagraphCaret destination)
    {
        destination = default;
        var context = RenderContext.Current;
        if (!OperatingSystem.IsMacOS() || context is not { IsValid: true, Backend: RenderBackend.Metal }) return false;
        var key = (text, family, size, weight, style, width, height);
        if (_paragraph is null || _paragraph.IsDisposed || _key != key || !ReferenceEquals(_context, context) || _fontEpoch != TextMeasurement.FontCacheEpoch)
        {
            Dispose();
            _key = key; _context = context; _fontEpoch = TextMeasurement.FontCacheEpoch;
            _paragraph = NativeTextParagraph.TryCreate(text.Length == 0 ? [] :
                [new NativeTextParagraph.Span(text, family, size, weight, style, Colors.White)],
                family, size, double.IsPositiveInfinity(width) ? 1 : Math.Max(1, width), Math.Max(1, height), TextAlignment.Left,
                FlowDirection.LeftToRight, naturalDirection: true, noWrap: double.IsPositiveInfinity(width));
        }
        return _paragraph is not null && NativeTextParagraph.TryNavigateWord(text, [(_paragraph, 0)],
            position, start, length, right, backward, out destination);
    }

    public void Dispose()
    {
        _paragraph?.Dispose(); _paragraph = null; _context = null;
    }
}
