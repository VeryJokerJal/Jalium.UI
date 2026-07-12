using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the kind of title bar button.
/// </summary>
public enum TitleBarButtonKind
{
    /// <summary>Minimize button.</summary>
    Minimize,
    /// <summary>Maximize button.</summary>
    Maximize,
    /// <summary>Restore button (shown when window is maximized).</summary>
    Restore,
    /// <summary>Close button.</summary>
    Close
}

/// <summary>
/// Represents a button control used in the window title bar.
/// Glyph rendering and visual states are defined entirely in the ControlTemplate (TitleBar.jalxaml).
/// </summary>
public class TitleBarButton : ButtonBase
{
    private static readonly SolidColorBrush s_transparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush s_glyphBrush = new(Colors.White);

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TitleBarButtonAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Kind dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(TitleBarButtonKind), typeof(TitleBarButton),
            new PropertyMetadata(TitleBarButtonKind.Close));

    /// <summary>
    /// Identifies the GlyphSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(TitleBarButton),
            new PropertyMetadata(10.0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the kind of title bar button.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TitleBarButtonKind Kind
    {
        get => (TitleBarButtonKind)(GetValue(KindProperty) ?? TitleBarButtonKind.Close);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the glyph.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double GlyphSize
    {
        get => (double)GetValue(GlyphSizeProperty)!;
        set => SetValue(GlyphSizeProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBarButton"/> class.
    /// </summary>
    public TitleBarButton()
    {
        Focusable = false;
    }

    private Brush ResolveHoverBackgroundBrush() =>
        TryFindResource("TitleBarButtonHover") as Brush ?? s_transparentBrush;

    private Brush ResolvePressedBackgroundBrush() =>
        TryFindResource("TitleBarButtonPressed") as Brush ?? s_transparentBrush;

    private Brush ResolveCloseHoverBackgroundBrush() =>
        TryFindResource("TitleBarCloseButtonHover") as Brush ?? s_transparentBrush;

    private Brush ResolveClosePressedBackgroundBrush() =>
        TryFindResource("TitleBarCloseButtonPressed") as Brush ?? s_transparentBrush;

    private Brush ResolveGlyphBrush() =>
        TryFindResource("TitleBarGlyph") as Brush ?? s_glyphBrush;

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = !double.IsNaN(Width) && Width > 0 ? Width : 46;

        double height;
        if (!double.IsNaN(Height) && Height > 0)
        {
            height = Height;
        }
        else if (!double.IsInfinity(availableSize.Height) && availableSize.Height > 0)
        {
            height = availableSize.Height;
        }
        else
        {
            height = 32;
        }

        if (!double.IsInfinity(availableSize.Width))
            width = Math.Min(width, Math.Max(0, availableSize.Width));
        if (!double.IsInfinity(availableSize.Height))
            height = Math.Min(height, Math.Max(0, availableSize.Height));

        var desiredSize = new Size(width, height);
        base.MeasureOverride(desiredSize);
        return desiredSize;
    }

    #endregion
}
