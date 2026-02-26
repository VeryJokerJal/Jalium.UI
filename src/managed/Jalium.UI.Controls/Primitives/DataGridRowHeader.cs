using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a row header in a DataGrid.
/// </summary>
public sealed class DataGridRowHeader : ButtonBase
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsRowSelected dependency property.
    /// </summary>
    public static readonly DependencyProperty IsRowSelectedProperty =
        DependencyProperty.Register(nameof(IsRowSelected), typeof(bool), typeof(DataGridRowHeader),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RowIndex dependency property.
    /// </summary>
    public static readonly DependencyProperty RowIndexProperty =
        DependencyProperty.Register(nameof(RowIndex), typeof(int), typeof(DataGridRowHeader),
            new PropertyMetadata(-1, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeparatorBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(DataGridRowHeader),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeparatorVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty SeparatorVisibilityProperty =
        DependencyProperty.Register(nameof(SeparatorVisibility), typeof(Visibility), typeof(DataGridRowHeader),
            new PropertyMetadata(Visibility.Visible, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the row is selected.
    /// </summary>
    public bool IsRowSelected
    {
        get => (bool)GetValue(IsRowSelectedProperty)!;
        set => SetValue(IsRowSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the index of the row.
    /// </summary>
    public int RowIndex
    {
        get => (int)GetValue(RowIndexProperty)!;
        set => SetValue(RowIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the separator.
    /// </summary>
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the visibility of the separator.
    /// </summary>
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
            bgBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
        }
        else if (IsPressed)
        {
            bgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
        }
        else
        {
            bgBrush = Background ?? new SolidColorBrush(Color.FromRgb(45, 45, 45));
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
            var separatorBrush = SeparatorBrush ?? new SolidColorBrush(Color.FromRgb(67, 67, 70));
            var separatorPen = new Pen(separatorBrush, 1);
            dc.DrawLine(separatorPen, new Point(rect.Width - 1, 0), new Point(rect.Width - 1, rect.Height));
            dc.DrawLine(separatorPen, new Point(0, rect.Height - 1), new Point(rect.Width, rect.Height - 1));
        }
    }

    private void DrawSelectionIndicator(DrawingContext dc, Rect rect)
    {
        var arrowBrush = new SolidColorBrush(Color.White);
        var arrowPen = new Pen(arrowBrush, 2);

        var centerX = rect.Width / 2;
        var centerY = rect.Height / 2;

        // Draw right-pointing arrow
        dc.DrawLine(arrowPen, new Point(centerX - 3, centerY - 4), new Point(centerX + 3, centerY));
        dc.DrawLine(arrowPen, new Point(centerX + 3, centerY), new Point(centerX - 3, centerY + 4));
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
