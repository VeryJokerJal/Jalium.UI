using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Root container for a docking layout.
/// Hosts a <see cref="DockSplitPanel"/> or <see cref="DockTabPanel"/> as content.
/// Shows an accent border when dock highlighting is active.
/// Dock indicator buttons are rendered in a separate topmost window by <see cref="DockManager"/>.
/// </summary>
public sealed class DockLayout : ContentControl
{
    private static readonly SolidColorBrush s_fallbackBackgroundBrush = new(ThemeColors.WindowBackground);

    private bool _isDockHighlighted;

    /// <summary>
    /// When true, an accent border is drawn to indicate this layout is a dock target.
    /// Managed by <see cref="DockManager"/>.
    /// </summary>
    internal bool IsDockHighlighted
    {
        get => _isDockHighlighted;
        set
        {
            if (_isDockHighlighted != value)
            {
                _isDockHighlighted = value;
                InvalidateVisual();
            }
        }
    }

    public DockLayout()
    {
        DockManager.Register(this);
        Loaded += (_, _) => DockManager.Register(this);
        Unloaded += (_, _) => DockManager.Unregister(this);
    }

    protected override void OnRender(object drawingContextObj)
    {
        if (drawingContextObj is not DrawingContext dc)
        {
            base.OnRender(drawingContextObj);
            return;
        }

        var background = Background ?? ResolveBrush("OneBackgroundPrimary", "WindowBackground", s_fallbackBackgroundBrush);
        dc.DrawRectangle(background, null, new Rect(RenderSize));

        base.OnRender(drawingContextObj);
    }

    private Brush ResolveBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }
}
