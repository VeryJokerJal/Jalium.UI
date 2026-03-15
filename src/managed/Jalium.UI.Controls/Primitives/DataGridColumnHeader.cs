using System.ComponentModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a column header in a DataGrid.
/// </summary>
public class DataGridColumnHeader : ButtonBase
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_pressedBgBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_defaultBgBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultSeparatorBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly Pen s_borderPen = new(s_borderBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the SortDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty SortDirectionProperty =
        DependencyProperty.Register(nameof(SortDirection), typeof(ListSortDirection?), typeof(DataGridColumnHeader),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CanUserSort dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserSortProperty =
        DependencyProperty.Register(nameof(CanUserSort), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the CanUserResize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserResizeProperty =
        DependencyProperty.Register(nameof(CanUserResize), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the CanUserReorder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserReorderProperty =
        DependencyProperty.Register(nameof(CanUserReorder), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the DisplayIndex dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayIndexProperty =
        DependencyProperty.Register(nameof(DisplayIndex), typeof(int), typeof(DataGridColumnHeader),
            new PropertyMetadata(-1));

    /// <summary>
    /// Identifies the IsFrozen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsFrozenProperty =
        DependencyProperty.Register(nameof(IsFrozen), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SeparatorBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(DataGridColumnHeader),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeparatorVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeparatorVisibilityProperty =
        DependencyProperty.Register(nameof(SeparatorVisibility), typeof(Visibility), typeof(DataGridColumnHeader),
            new PropertyMetadata(Visibility.Visible, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the sort direction for this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ListSortDirection? SortDirection
    {
        get => (ListSortDirection?)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can sort by this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserSort
    {
        get => (bool)GetValue(CanUserSortProperty)!;
        set => SetValue(CanUserSortProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can resize this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserResize
    {
        get => (bool)GetValue(CanUserResizeProperty)!;
        set => SetValue(CanUserResizeProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can reorder this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserReorder
    {
        get => (bool)GetValue(CanUserReorderProperty)!;
        set => SetValue(CanUserReorderProperty, value);
    }

    /// <summary>
    /// Gets or sets the display index of this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int DisplayIndex
    {
        get => (int)GetValue(DisplayIndexProperty)!;
        set => SetValue(DisplayIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this column is frozen.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsFrozen
    {
        get => (bool)GetValue(IsFrozenProperty)!;
        set => SetValue(IsFrozenProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the separator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the separator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Visibility SeparatorVisibility
    {
        get => (Visibility)GetValue(SeparatorVisibilityProperty)!;
        set => SetValue(SeparatorVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the column associated with this header.
    /// </summary>
    public DataGridColumn? Column { get; internal set; }

    #endregion

    #region Private Fields

    private bool _isResizing;
    private double _resizeStartWidth;
    private Point _resizeStartPoint;
    private const double ResizeGripWidth = 5;
    private const double MinColumnWidth = 20;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGridColumnHeader"/> class.
    /// </summary>
    public DataGridColumnHeader()
    {
        Height = 28;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Center;
        Padding = new Thickness(8, 0, 8, 0);

        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
    }

    #endregion

    #region Resize Handling

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseEventArgs mouseArgs && CanUserResize)
        {
            var position = mouseArgs.GetPosition(this);
            var isInResizeGrip = position.X >= RenderSize.Width - ResizeGripWidth;

            // Change cursor when over resize grip
            // Note: Cursor management would be handled by the hosting window
        }
    }

    /// <summary>
    /// Begins a resize operation.
    /// </summary>
    /// <param name="startPoint">The starting point of the resize.</param>
    internal void BeginResize(Point startPoint)
    {
        _isResizing = true;
        _resizeStartPoint = startPoint;
        _resizeStartWidth = RenderSize.Width;
        CaptureMouse();
    }

    /// <summary>
    /// Updates the resize operation.
    /// </summary>
    /// <param name="currentPoint">The current mouse position.</param>
    internal void UpdateResize(Point currentPoint)
    {
        if (!_isResizing) return;

        var delta = currentPoint.X - _resizeStartPoint.X;
        var newWidth = Math.Max(MinColumnWidth, _resizeStartWidth + delta);

        if (Column != null)
        {
            Column.Width = newWidth;
        }
    }

    /// <summary>
    /// Ends the resize operation.
    /// </summary>
    internal void EndResize()
    {
        _isResizing = false;
        ReleaseMouseCapture();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var width = Column?.Width ?? 100;

        return new Size(width, Height > 0 ? Height : 28);
    }

    #endregion

    #region Rendering

    private Brush ResolvePressedBackgroundBrush()
    {
        return ResolveThemeBrush("ControlBackgroundPressed", s_pressedBgBrush, "HighlightBackground");
    }

    private Brush ResolveDefaultBackgroundBrush()
    {
        return Background
            ?? ResolveThemeBrush("ControlBackground", s_defaultBgBrush, "SurfaceBackground");
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return ResolveThemeBrush("TextPrimary", s_defaultFgBrush, "TextFillColorPrimaryBrush");
    }

    private Brush ResolveSeparatorBrush()
    {
        return SeparatorBrush
            ?? BorderBrush
            ?? ResolveThemeBrush("ControlBorder", s_defaultSeparatorBrush, "DividerStrokeColorDefaultBrush");
    }

    private Pen ResolveBottomBorderPen()
    {
        var borderBrush = BorderBrush
            ?? ResolveThemeBrush("ControlBorder", s_borderBrush, "DividerStrokeColorDefaultBrush");
        return new Pen(borderBrush, 1);
    }

    private Brush ResolveThemeBrush(string resourceKey, Brush fallback, string? secondaryResourceKey = null)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        if (secondaryResourceKey != null && TryFindResource(secondaryResourceKey) is Brush secondaryBrush)
        {
            return secondaryBrush;
        }

        return fallback;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;

        // Draw background
        var bgBrush = IsPressed
            ? ResolvePressedBackgroundBrush()
            : ResolveDefaultBackgroundBrush();
        dc.DrawRectangle(bgBrush, null, rect);

        // Draw content
        if (Content is string text)
        {
            var fgBrush = ResolveForegroundBrush();
            var formattedText = new FormattedText(text, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 12)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = padding.Left;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));

            // Draw sort indicator
            if (SortDirection.HasValue)
            {
                DrawSortIndicator(dc, rect, formattedText.Width + padding.Left + 4, fgBrush);
            }
        }

        // Draw separator
        if (SeparatorVisibility == Visibility.Visible)
        {
            var separatorBrush = ResolveSeparatorBrush();
            var separatorPen = new Pen(separatorBrush, 1);
            dc.DrawLine(separatorPen, new Point(rect.Width - 1, 0), new Point(rect.Width - 1, rect.Height));
        }

        // Draw bottom border
        dc.DrawLine(ResolveBottomBorderPen(), new Point(0, rect.Height - 1), new Point(rect.Width, rect.Height - 1));
    }

    private void DrawSortIndicator(DrawingContext dc, Rect rect, double offsetX, Brush brush)
    {
        var arrowPen = new Pen(brush, 1.5);
        var centerX = offsetX + 6;
        var centerY = rect.Height / 2;

        if (SortDirection == ListSortDirection.Ascending)
        {
            // Up arrow
            dc.DrawLine(arrowPen, new Point(centerX - 4, centerY + 2), new Point(centerX, centerY - 2));
            dc.DrawLine(arrowPen, new Point(centerX, centerY - 2), new Point(centerX + 4, centerY + 2));
        }
        else
        {
            // Down arrow
            dc.DrawLine(arrowPen, new Point(centerX - 4, centerY - 2), new Point(centerX, centerY + 2));
            dc.DrawLine(arrowPen, new Point(centerX, centerY + 2), new Point(centerX + 4, centerY - 2));
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumnHeader header)
        {
            header.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Represents a column in a DataGrid (placeholder for reference).
/// </summary>
public abstract class DataGridColumn
{
    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    public double Width { get; set; } = 100;

    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    public object? Header { get; set; }
}
