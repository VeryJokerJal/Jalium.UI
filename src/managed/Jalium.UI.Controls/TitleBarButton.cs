using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the kind of title bar button.
/// </summary>
public enum TitleBarButtonKind
{
    /// <summary>
    /// Minimize button.
    /// </summary>
    Minimize,

    /// <summary>
    /// Maximize button.
    /// </summary>
    Maximize,

    /// <summary>
    /// Restore button (shown when window is maximized).
    /// </summary>
    Restore,

    /// <summary>
    /// Close button.
    /// </summary>
    Close
}

/// <summary>
/// Represents a button control used in the window title bar.
/// </summary>
public class TitleBarButton : ButtonBase
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TitleBarButtonAutomationPeer(this);
    }

    private const double DefaultButtonWidth = 46;
    private const double DefaultButtonHeight = 32;
    private static readonly SolidColorBrush s_fallbackTitleBarButtonHoverBrush = new(Themes.ThemeColors.TitleBarButtonHover);
    private static readonly SolidColorBrush s_fallbackTitleBarButtonPressedBrush = new(Themes.ThemeColors.TitleBarButtonPressed);
    private static readonly SolidColorBrush s_fallbackTitleBarCloseButtonHoverBrush = new(Themes.ThemeColors.TitleBarCloseButtonHover);
    private static readonly SolidColorBrush s_fallbackTitleBarCloseButtonPressedBrush = new(Themes.ThemeColors.TitleBarCloseButtonPressed);
    private static readonly SolidColorBrush s_fallbackTitleBarGlyphBrush = new(Color.White);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Kind dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(TitleBarButtonKind), typeof(TitleBarButton),
            new PropertyMetadata(TitleBarButtonKind.Close, OnKindChanged));

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

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TitleBarButton"/> class.
    /// </summary>
    public TitleBarButton()
    {
        Focusable = false;
    }

    #endregion

    #region Template Parts

    private Border? _rootBorder;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _rootBorder = GetTemplateChild("PART_RootBorder") as Border;
    }

    #endregion

    #region Property Changed

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TitleBarButton button)
        {
            button.InvalidateVisual();
        }
    }

    /// <summary>
    /// Called when the mouse enters the button.
    /// </summary>
    protected override void OnMouseEnter(RoutedEventArgs e)
    {
        base.OnMouseEnter(e);
        // Redraw to show hover state
        InvalidateVisual();
    }

    /// <summary>
    /// Called when the mouse leaves the button.
    /// </summary>
    protected override void OnMouseLeave(RoutedEventArgs e)
    {
        base.OnMouseLeave(e);
        // Redraw to remove hover state
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Respect styled Width/Height first; fall back to native caption button defaults.
        double width = !double.IsNaN(Width) && Width > 0 ? Width : DefaultButtonWidth;
        double height = !double.IsNaN(Height) && Height > 0 ? Height : DefaultButtonHeight;
        if (!double.IsInfinity(availableSize.Width))
        {
            width = Math.Min(width, Math.Max(0, availableSize.Width));
        }

        if (!double.IsInfinity(availableSize.Height))
        {
            height = Math.Min(height, Math.Max(0, availableSize.Height));
        }

        var desiredSize = new Size(width, height);

        // IMPORTANT: Still need to measure template content so children get proper RenderSize
        // Call base to apply template and measure template root
        base.MeasureOverride(desiredSize);

        return desiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Let base class arrange the template content
        return base.ArrangeOverride(finalSize);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // If using template, let the template handle rendering
        if (_rootBorder != null)
            return;

        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw background based on state and Kind
        Brush? bgBrush = GetBackgroundBrush();

        if (bgBrush != null)
        {
            dc.DrawRectangle(bgBrush, null, rect);
        }

        // Draw glyph
        DrawGlyph(dc, rect);
    }

    /// <summary>
    /// Gets the appropriate background brush based on button state and kind.
    /// </summary>
    private Brush? GetBackgroundBrush()
    {
        if (Kind == TitleBarButtonKind.Close)
        {
            // Close button: red hover/pressed colors
            if (IsPressed)
                return ResolveClosePressedBackgroundBrush();
            if (IsMouseOver)
                return ResolveCloseHoverBackgroundBrush();
        }
        else
        {
            // Other buttons: gray hover/pressed colors
            if (IsPressed)
                return ResolvePressedBackgroundBrush();
            if (IsMouseOver)
                return ResolveHoverBackgroundBrush();
        }

        // Default: use Background property (usually transparent)
        return Background;
    }

    private void DrawGlyph(DrawingContext dc, Rect rect)
    {
        var glyphBrush = ResolveGlyphBrush();
        var glyphSize = GlyphSize;
        var centerX = rect.Width / 2;
        var centerY = rect.Height / 2;
        var halfSize = glyphSize / 2;

        var pen = new Pen(glyphBrush, 1);

        switch (Kind)
        {
            case TitleBarButtonKind.Minimize:
                // Horizontal line
                dc.DrawLine(pen,
                    new Point(centerX - halfSize, centerY),
                    new Point(centerX + halfSize, centerY));
                break;

            case TitleBarButtonKind.Maximize:
                // Square
                var maxRect = new Rect(centerX - halfSize, centerY - halfSize, glyphSize, glyphSize);
                dc.DrawRectangle(null, pen, maxRect);
                break;

            case TitleBarButtonKind.Restore:
                // Two overlapping squares
                var offset = 2;
                // Back square (top-right)
                var backRect = new Rect(centerX - halfSize + offset, centerY - halfSize - offset, glyphSize - offset, glyphSize - offset);
                dc.DrawRectangle(null, pen, backRect);
                // Front square (bottom-left)
                var frontRect = new Rect(centerX - halfSize, centerY - halfSize + offset, glyphSize - offset, glyphSize - offset);
                // Fill front square with background to cover back square lines
                dc.DrawRectangle(Background ?? new SolidColorBrush(Color.Transparent), null, frontRect);
                dc.DrawRectangle(null, pen, frontRect);
                break;

            case TitleBarButtonKind.Close:
                // X shape
                dc.DrawLine(pen,
                    new Point(centerX - halfSize, centerY - halfSize),
                    new Point(centerX + halfSize, centerY + halfSize));
                dc.DrawLine(pen,
                    new Point(centerX + halfSize, centerY - halfSize),
                    new Point(centerX - halfSize, centerY + halfSize));
                break;
        }
    }

    private Brush ResolveHoverBackgroundBrush()
    {
        return TryFindResource("TitleBarButtonHover") as Brush ?? s_fallbackTitleBarButtonHoverBrush;
    }

    private Brush ResolvePressedBackgroundBrush()
    {
        return TryFindResource("TitleBarButtonPressed") as Brush ?? s_fallbackTitleBarButtonPressedBrush;
    }

    private Brush ResolveCloseHoverBackgroundBrush()
    {
        return TryFindResource("TitleBarCloseButtonHover") as Brush ?? s_fallbackTitleBarCloseButtonHoverBrush;
    }

    private Brush ResolveClosePressedBackgroundBrush()
    {
        return TryFindResource("TitleBarCloseButtonPressed") as Brush ?? s_fallbackTitleBarCloseButtonPressedBrush;
    }

    private Brush ResolveGlyphBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TitleBarGlyph") as Brush
            ?? Foreground
            ?? s_fallbackTitleBarGlyphBrush;
    }

    #endregion
}
