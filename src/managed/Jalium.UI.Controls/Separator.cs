using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control used to separate items in a list or menu.
/// </summary>
public class Separator : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.SeparatorAutomationPeer(this);
    }

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultStrokeBrush = new(Color.FromRgb(60, 60, 60));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Separator),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StrokeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(Separator),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StrokeThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Separator),
            new PropertyMetadata(1.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the separator line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Separator"/> class.
    /// </summary>
    public Separator()
    {
        // Separators are not focusable
        Focusable = false;
        IsHitTestVisible = false;
    }

    #endregion

    #region Template Parts

    private Border? _separatorBorder;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _separatorBorder = GetTemplateChild("SeparatorBorder") as Border;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var thickness = StrokeThickness;
        var margin = Margin;

        if (Orientation == Orientation.Horizontal)
        {
            // Horizontal separator: zero desired width (stretches to fill), minimal height
            return new Size(0, thickness);
        }
        else
        {
            // Vertical separator: minimal width, zero desired height (stretches to fill)
            return new Size(thickness, 0);
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // If using template, let the template handle rendering
        if (_separatorBorder != null)
        {
            return;
        }

        if (drawingContext is not DrawingContext dc)
            return;

        var brush = StrokeBrush ?? BorderBrush ?? s_defaultStrokeBrush;
        var thickness = StrokeThickness;
        var pen = new Pen(brush, thickness);

        if (Orientation == Orientation.Horizontal)
        {
            // Draw horizontal line
            var y = RenderSize.Height / 2;
            dc.DrawLine(pen, new Point(0, y), new Point(RenderSize.Width, y));
        }
        else
        {
            // Draw vertical line
            var x = RenderSize.Width / 2;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Separator separator)
        {
            separator.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Separator separator)
        {
            separator.InvalidateVisual();
        }
    }

    #endregion
}
