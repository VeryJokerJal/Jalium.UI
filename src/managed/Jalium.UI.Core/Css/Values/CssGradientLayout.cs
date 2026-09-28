using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// Keeps a CSS gradient's source lengths until the brush's actual paint bounds are
/// known. A style application owns one instance, so font/container rulers belong
/// to that element instead of leaking through a shared compiled stylesheet.
/// </summary>
internal sealed class CssGradientLayout(string function, string arguments, CssLengthContext lengths,
    bool dependsOnCurrentColor = false, Color? currentColor = null)
{
    internal bool DependsOnCurrentColor => dependsOnCurrentColor;
    private readonly object _gate = new();
    private double _lastWidth = double.NaN, _lastHeight = double.NaN;
    private Brush? _lastBrush;

    internal CssGradientLayout ForElement(in CssLengthContext context, Color color) =>
        new(function, arguments, context, dependsOnCurrentColor, color);

    internal Brush? Resolve(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return null;
        lock (_gate)
        {
            if (_lastBrush is not null && _lastWidth == width && _lastHeight == height)
                return _lastBrush;
            var source = arguments;
            if (dependsOnCurrentColor)
            {
                if (currentColor is not { } color ||
                    !CssColorParser.TrySubstituteCurrentColor(source, color, out source, out _))
                    return null;
            }
            var reader = new CssTokenReader(source, new CssNumericReadContext(lengths));
            if (!CssGradientParser.TryParseResolved(function, ref reader, lengths, width, height, out var brush) ||
                brush is null) return null;
            if (brush.CanFreeze) brush.Freeze();
            _lastWidth = width;
            _lastHeight = height;
            return _lastBrush = brush;
        }
    }
}
