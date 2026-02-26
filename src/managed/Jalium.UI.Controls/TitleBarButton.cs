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
public sealed class TitleBarButton : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Kind dependency property.
    /// </summary>
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(TitleBarButtonKind), typeof(TitleBarButton),
            new PropertyMetadata(TitleBarButtonKind.Close, OnKindChanged));

    /// <summary>
    /// Identifies the GlyphSize dependency property.
    /// </summary>
    public static readonly DependencyProperty GlyphSizeProperty =
        DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(TitleBarButton),
            new PropertyMetadata(10.0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the kind of title bar button.
    /// </summary>
    public TitleBarButtonKind Kind
    {
        get => (TitleBarButtonKind)(GetValue(KindProperty) ?? TitleBarButtonKind.Close);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the glyph.
    /// </summary>
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
        Width = 46;
        Height = 32;
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
        // Title bar buttons have fixed size
        var desiredSize = new Size(
            double.IsInfinity(availableSize.Width) ? Width : Math.Min(Width, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? Height : Math.Min(Height, availableSize.Height));

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
                return new SolidColorBrush(Themes.ThemeColors.TitleBarCloseButtonPressed);
            if (IsMouseOver)
                return new SolidColorBrush(Themes.ThemeColors.TitleBarCloseButtonHover);
        }
        else
        {
            // Other buttons: gray hover/pressed colors
            if (IsPressed)
                return new SolidColorBrush(Themes.ThemeColors.TitleBarButtonPressed);
            if (IsMouseOver)
                return new SolidColorBrush(Themes.ThemeColors.TitleBarButtonHover);
        }

        // Default: use Background property (usually transparent)
        return Background;
    }

    private void DrawGlyph(DrawingContext dc, Rect rect)
    {
        var glyphBrush = Foreground ?? new SolidColorBrush(Color.White);
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

    #endregion
}
