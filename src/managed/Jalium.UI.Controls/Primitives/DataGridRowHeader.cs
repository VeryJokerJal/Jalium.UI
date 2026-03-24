using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a row header in a DataGrid.
/// </summary>
public class DataGridRowHeader : ButtonBase
{
    private static readonly SolidColorBrush s_selectedBackgroundBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush s_pressedBackgroundBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_defaultSeparatorBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_selectionIndicatorBrush = new(Color.White);

    // Cached pens
    private Pen? _separatorPen;
    private Brush? _separatorPenBrush;
    private Pen? _arrowPen;
    private Brush? _arrowPenBrush;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsRowSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsRowSelectedProperty =
        DependencyProperty.Register(nameof(IsRowSelected), typeof(bool), typeof(DataGridRowHeader),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RowIndex dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowIndexProperty =
        DependencyProperty.Register(nameof(RowIndex), typeof(int), typeof(DataGridRowHeader),
            new PropertyMetadata(-1, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeparatorBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(DataGridRowHeader),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeparatorVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SeparatorVisibilityProperty =
        DependencyProperty.Register(nameof(SeparatorVisibility), typeof(Visibility), typeof(DataGridRowHeader),
            new PropertyMetadata(Visibility.Visible, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the row is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsRowSelected
    {
        get => (bool)GetValue(IsRowSelectedProperty)!;
        set => SetValue(IsRowSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the index of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int RowIndex
    {
        get => (int)GetValue(RowIndexProperty)!;
        set => SetValue(RowIndexProperty, value);
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

    #endregion

    #region Private Fields

    private const double DefaultWidth = 30;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGridRowHeader"/> class.
    /// </summary>
    public DataGridRowHeader()
    {
        Width = DefaultWidth;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Width > 0 ? Width : DefaultWidth, availableSize.Height);
    }

    #endregion

    #region Rendering

    private Brush ResolveSelectedBackgroundBrush()
    {
        return ResolveThemeBrush("AccentBrush", s_selectedBackgroundBrush, "AccentFillColorDefaultBrush");
    }

    private Brush ResolvePressedBackgroundBrush()
    {
        return ResolveThemeBrush("ControlBackgroundPressed", s_pressedBackgroundBrush, "HighlightBackground");
    }

    private Brush ResolveDefaultBackgroundBrush()
    {
        return Background
            ?? ResolveThemeBrush("ControlBackground", s_defaultBackgroundBrush, "SurfaceBackground");
    }

    private Brush ResolveSeparatorBrush()
    {
        return SeparatorBrush
            ?? BorderBrush
            ?? ResolveThemeBrush("ControlBorder", s_defaultSeparatorBrush, "DividerStrokeColorDefaultBrush");
    }

    private Brush ResolveSelectionIndicatorBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return ResolveThemeBrush("TextOnAccent", s_selectionIndicatorBrush, "TextOnAccentFillColorPrimaryBrush");
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

        // Draw background
        Brush bgBrush;
        if (IsRowSelected)
        {
            bgBrush = ResolveSelectedBackgroundBrush();
        }
        else if (IsPressed)
        {
            bgBrush = ResolvePressedBackgroundBrush();
        }
        else
        {
            bgBrush = ResolveDefaultBackgroundBrush();
        }
        dc.DrawRectangle(bgBrush, null, rect);

        // Draw selection indicator arrow
        if (IsRowSelected)
        {
            DrawSelectionIndicator(dc, rect);
        }

        // Draw separator
        if (SeparatorVisibility == Visibility.Visible)
        {
            var separatorBrush = ResolveSeparatorBrush();
            if (_separatorPen == null || _separatorPenBrush != separatorBrush)
            {
                _separatorPen = new Pen(separatorBrush, 1);
                _separatorPenBrush = separatorBrush;
            }
            dc.DrawLine(_separatorPen, new Point(rect.Width - 1, 0), new Point(rect.Width - 1, rect.Height));
            dc.DrawLine(_separatorPen, new Point(0, rect.Height - 1), new Point(rect.Width, rect.Height - 1));
        }
    }

    // Right-pointing chevron in 6×8 design space, cached
    private static readonly PathGeometry s_selectionChevron = (PathGeometry)Geometry.Parse("M 0,0 L 6,4 L 0,8");

    private void DrawSelectionIndicator(DrawingContext dc, Rect rect)
    {
        var arrowBrush = ResolveSelectionIndicatorBrush();
        if (_arrowPen == null || _arrowPenBrush != arrowBrush)
        {
            _arrowPen = new Pen(arrowBrush, 2);
            _arrowPenBrush = arrowBrush;
        }

        var bounds = s_selectionChevron.Bounds;
        var ox = rect.Width / 2 - bounds.X - bounds.Width / 2;
        var oy = rect.Height / 2 - bounds.Y - bounds.Height / 2;

        foreach (var figure in s_selectionChevron.Figures)
        {
            var tf = new PathFigure
            {
                StartPoint = new Point(figure.StartPoint.X + ox, figure.StartPoint.Y + oy),
                IsClosed = figure.IsClosed,
                IsFilled = false
            };
            foreach (var seg in figure.Segments)
                if (seg is LineSegment ls)
                    tf.Segments.Add(new LineSegment(new Point(ls.Point.X + ox, ls.Point.Y + oy), ls.IsStroked));
            var geo = new PathGeometry();
            geo.Figures.Add(tf);
            dc.DrawGeometry(null, _arrowPen, geo);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRowHeader header)
        {
            header.InvalidateVisual();
        }
    }

    #endregion
}
