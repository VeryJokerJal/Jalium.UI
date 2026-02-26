using Jalium.UI.Media;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Visual style applied to a syntax token classification.
/// </summary>
public sealed class HighlightingStyle
{
    public Brush? Foreground { get; init; }
    public Brush? Background { get; init; }
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }

    public static HighlightingStyle Default { get; } = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212))
    };
}
