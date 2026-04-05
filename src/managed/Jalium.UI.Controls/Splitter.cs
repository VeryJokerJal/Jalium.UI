using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

using static Jalium.UI.Cursors;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that provides a draggable divider between two areas.
/// Used with Grid to create resizable panels.
/// </summary>
public class GridSplitter : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GridSplitterAutomationPeer(this);
    }

    #region Static Brushes

    private static readonly SolidColorBrush s_draggingBrush = new(ThemeColors.Accent);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the ResizeDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ResizeDirectionProperty =
        DependencyProperty.Register(nameof(ResizeDirection), typeof(GridResizeDirection), typeof(GridSplitter),
            new PropertyMetadata(GridResizeDirection.Auto, OnResizeDirectionChanged));

    /// <summary>
    /// Identifies the ResizeBehavior dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ResizeBehaviorProperty =
        DependencyProperty.Register(nameof(ResizeBehavior), typeof(GridResizeBehavior), typeof(GridSplitter),
            new PropertyMetadata(GridResizeBehavior.BasedOnAlignment));

    /// <summary>
    /// Identifies the ShowsPreview dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowsPreviewProperty =
        DependencyProperty.Register(nameof(ShowsPreview), typeof(bool), typeof(GridSplitter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the PreviewStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PreviewStyleProperty =
        DependencyProperty.Register(nameof(PreviewStyle), typeof(Style), typeof(GridSplitter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the DragIncrement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DragIncrementProperty =
        DependencyProperty.Register(nameof(DragIncrement), typeof(double), typeof(GridSplitter),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the KeyboardIncrement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty KeyboardIncrementProperty =
        DependencyProperty.Register(nameof(KeyboardIncrement), typeof(double), typeof(GridSplitter),
            new PropertyMetadata(10.0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the direction in which the splitter resizes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridResizeDirection ResizeDirection
    {
        get => (GridResizeDirection)GetValue(ResizeDirectionProperty)!;
        set => SetValue(ResizeDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets how the splitter resizes adjacent rows/columns.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridResizeBehavior ResizeBehavior
    {
        get => (GridResizeBehavior)GetValue(ResizeBehaviorProperty)!;
        set => SetValue(ResizeBehaviorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a preview is shown during drag.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowsPreview
    {
        get => (bool)GetValue(ShowsPreviewProperty)!;
        set => SetValue(ShowsPreviewProperty, value);
    }

    /// <summary>
    /// Gets or sets the style for the preview.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Style? PreviewStyle
    {
        get => (Style?)GetValue(PreviewStyleProperty);
        set => SetValue(PreviewStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum drag increment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double DragIncrement
    {
        get => (double)GetValue(DragIncrementProperty)!;
        set => SetValue(DragIncrementProperty, value);
    }

    /// <summary>
    /// Gets or sets the keyboard movement increment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double KeyboardIncrement
    {
        get => (double)GetValue(KeyboardIncrementProperty)!;
        set => SetValue(KeyboardIncrementProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isDragging;
    private Point _dragStartPoint;
    private double _originalDimension1;
    private double _originalDimension2;
    private GridResizeDirection _effectiveResizeDirection;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="GridSplitter"/> class.
    /// </summary>
    public GridSplitter()
    {
        Focusable = true;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Cursor = SizeWE; // Default to horizontal resize cursor

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            // Use position relative to parent Grid for stable coordinates during drag
            var grid = GetParentGrid();
            var relativeTo = grid as UIElement ?? this;
            StartDrag(e.GetPosition(relativeTo));
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isDragging)
            {
                FinishDrag();
            }
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            // Use position relative to parent Grid for stable coordinates during drag
            var grid = GetParentGrid();
            var relativeTo = grid as UIElement ?? this;
            UpdateDrag(e.GetPosition(relativeTo));
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        var delta = 0.0;
        var isHorizontal = GetEffectiveResizeDirection() == GridResizeDirection.Columns;

        switch (e.Key)
        {
            case Key.Left when isHorizontal:
                delta = -KeyboardIncrement;
                break;
            case Key.Right when isHorizontal:
                delta = KeyboardIncrement;
                break;
            case Key.Up when !isHorizontal:
                delta = -KeyboardIncrement;
                break;
            case Key.Down when !isHorizontal:
                delta = KeyboardIncrement;
                break;
        }

        if (delta != 0)
        {
            // Initialize state that ApplyResize and GetResizeIndices depend on
            _effectiveResizeDirection = GetEffectiveResizeDirection();
            var grid = GetParentGrid();
            if (grid != null)
            {
                var (idx1, idx2) = GetResizeIndices();
                if (_effectiveResizeDirection == GridResizeDirection.Columns)
                {
                    _originalDimension1 = idx1 >= 0 && idx1 < grid.ColumnDefinitions.Count
                        ? grid.ColumnDefinitions[idx1].ActualWidth : 0;
                    _originalDimension2 = idx2 >= 0 && idx2 < grid.ColumnDefinitions.Count
                        ? grid.ColumnDefinitions[idx2].ActualWidth : 0;
                }
                else
                {
                    _originalDimension1 = idx1 >= 0 && idx1 < grid.RowDefinitions.Count
                        ? grid.RowDefinitions[idx1].ActualHeight : 0;
                    _originalDimension2 = idx2 >= 0 && idx2 < grid.RowDefinitions.Count
                        ? grid.RowDefinitions[idx2].ActualHeight : 0;
                }
            }
            ApplyResize(delta);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isDragging)
        {
            FinishDrag();
        }
    }

    #endregion

    #region Drag Operations

    private void StartDrag(Point position)
    {
        CaptureMouse();
        _isDragging = true;
        _dragStartPoint = position;
        Background = s_draggingBrush;
        _effectiveResizeDirection = GetEffectiveResizeDirection();

        // Store original dimensions
        var grid = GetParentGrid();
        if (grid != null)
        {
            var (index1, index2) = GetResizeIndices();
            if (_effectiveResizeDirection == GridResizeDirection.Columns)
            {
                if (index1 >= 0 && index1 < grid.ColumnDefinitions.Count)
                    _originalDimension1 = grid.ColumnDefinitions[index1].ActualWidth;
                if (index2 >= 0 && index2 < grid.ColumnDefinitions.Count)
                    _originalDimension2 = grid.ColumnDefinitions[index2].ActualWidth;
            }
            else
            {
                if (index1 >= 0 && index1 < grid.RowDefinitions.Count)
                    _originalDimension1 = grid.RowDefinitions[index1].ActualHeight;
                if (index2 >= 0 && index2 < grid.RowDefinitions.Count)
                    _originalDimension2 = grid.RowDefinitions[index2].ActualHeight;
            }
        }
    }

    private void UpdateDrag(Point position)
    {
        var delta = _effectiveResizeDirection == GridResizeDirection.Columns
            ? position.X - _dragStartPoint.X
            : position.Y - _dragStartPoint.Y;

        // Apply drag increment
        if (DragIncrement > 1)
        {
            delta = Math.Round(delta / DragIncrement) * DragIncrement;
        }

        ApplyResize(delta);
    }

    private void FinishDrag()
    {
        ReleaseMouseCapture();
        _isDragging = false;
        ClearValue(BackgroundProperty);
    }

    private void ApplyResize(double delta)
    {
        var grid = GetParentGrid();
        if (grid == null) return;

        var (index1, index2) = GetResizeIndices();

        if (_effectiveResizeDirection == GridResizeDirection.Columns)
        {
            if (index1 >= 0 && index1 < grid.ColumnDefinitions.Count &&
                index2 >= 0 && index2 < grid.ColumnDefinitions.Count)
            {
                var def1 = grid.ColumnDefinitions[index1];
                var def2 = grid.ColumnDefinitions[index2];

                // Clamp delta so neither side goes below its MinWidth
                var maxForward = _originalDimension2 - def2.MinWidth;
                var maxBackward = _originalDimension1 - def1.MinWidth;
                delta = Math.Clamp(delta, -maxBackward, maxForward);

                var newWidth1 = _originalDimension1 + delta;
                var newWidth2 = _originalDimension2 - delta;

                // Use Star units so both columns scale proportionally when the window is resized.
                def1.Width = new GridLength(newWidth1, GridUnitType.Star);
                def2.Width = new GridLength(newWidth2, GridUnitType.Star);
            }
        }
        else
        {
            if (index1 >= 0 && index1 < grid.RowDefinitions.Count &&
                index2 >= 0 && index2 < grid.RowDefinitions.Count)
            {
                var def1 = grid.RowDefinitions[index1];
                var def2 = grid.RowDefinitions[index2];

                // Clamp delta so neither side goes below its MinHeight
                var maxForward = _originalDimension2 - def2.MinHeight;
                var maxBackward = _originalDimension1 - def1.MinHeight;
                delta = Math.Clamp(delta, -maxBackward, maxForward);

                var newHeight1 = _originalDimension1 + delta;
                var newHeight2 = _originalDimension2 - delta;

                def1.Height = new GridLength(newHeight1, GridUnitType.Star);
                def2.Height = new GridLength(newHeight2, GridUnitType.Star);
            }
        }

        grid.InvalidateMeasure();
    }

    #endregion

    #region Helper Methods

    private Grid? GetParentGrid()
    {
        var parent = VisualParent;
        while (parent != null)
        {
            if (parent is Grid grid)
                return grid;
            parent = (parent as Visual)?.VisualParent;
        }
        return null;
    }

    private GridResizeDirection GetEffectiveResizeDirection()
    {
        if (ResizeDirection != GridResizeDirection.Auto)
            return ResizeDirection;

        // Auto: determine from dimensions
        if (ActualWidth <= ActualHeight)
            return GridResizeDirection.Columns;
        else
            return GridResizeDirection.Rows;
    }

    private (int Index1, int Index2) GetResizeIndices()
    {
        var grid = GetParentGrid();
        if (grid == null) return (-1, -1);

        var col = Grid.GetColumn(this);
        var row = Grid.GetRow(this);

        switch (ResizeBehavior)
        {
            case GridResizeBehavior.PreviousAndNext:
                return _effectiveResizeDirection == GridResizeDirection.Columns
                    ? (col - 1, col + 1)
                    : (row - 1, row + 1);

            case GridResizeBehavior.PreviousAndCurrent:
                return _effectiveResizeDirection == GridResizeDirection.Columns
                    ? (col - 1, col)
                    : (row - 1, row);

            case GridResizeBehavior.CurrentAndNext:
                return _effectiveResizeDirection == GridResizeDirection.Columns
                    ? (col, col + 1)
                    : (row, row + 1);

            case GridResizeBehavior.BasedOnAlignment:
            default:
                // Based on alignment (WPF behavior):
                // Left/Top 閳?PreviousAndCurrent
                // Right/Bottom 閳?CurrentAndNext
                // Center/Stretch 閳?PreviousAndNext
                if (_effectiveResizeDirection == GridResizeDirection.Columns)
                {
                    return HorizontalAlignment switch
                    {
                        HorizontalAlignment.Left => (col - 1, col),
                        HorizontalAlignment.Right => (col, col + 1),
                        _ => (col - 1, col + 1) // Center, Stretch
                    };
                }
                else
                {
                    return VerticalAlignment switch
                    {
                        VerticalAlignment.Top => (row - 1, row),
                        VerticalAlignment.Bottom => (row, row + 1),
                        _ => (row - 1, row + 1) // Center, Stretch
                    };
                }
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var direction = GetEffectiveResizeDirection();
        if (direction == GridResizeDirection.Columns)
        {
            var width = double.IsNaN(Width) || Width <= 0 ? 5 : Width;
            return new Size(width, double.IsPositiveInfinity(availableSize.Height) ? 1 : availableSize.Height);
        }
        else
        {
            var height = double.IsNaN(Height) || Height <= 0 ? 5 : Height;
            return new Size(double.IsPositiveInfinity(availableSize.Width) ? 1 : availableSize.Width, height);
        }
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        // Update cursor after layout when we know the actual dimensions
        // (needed for Auto resize direction which depends on dimensions)
        UpdateCursor();
        return result;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnResizeDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridSplitter splitter)
        {
            splitter.UpdateCursor();
            splitter.InvalidateMeasure();
            splitter.InvalidateVisual();
        }
    }

    /// <summary>
    /// Updates the cursor based on the effective resize direction.
    /// </summary>
    private void UpdateCursor()
    {
        var direction = GetEffectiveResizeDirection();
        Cursor = direction == GridResizeDirection.Columns
            ? SizeWE    // Horizontal resize (left-right)
            : SizeNS;   // Vertical resize (up-down)
    }

    #endregion
}

/// <summary>
/// Specifies which rows or columns are resized by a GridSplitter.
/// </summary>
public enum GridResizeDirection
{
    /// <summary>
    /// Automatically determine resize direction based on splitter size.
    /// </summary>
    Auto,

    /// <summary>
    /// Resize columns.
    /// </summary>
    Columns,

    /// <summary>
    /// Resize rows.
    /// </summary>
    Rows
}

/// <summary>
/// Specifies which adjacent rows/columns a GridSplitter resizes.
/// </summary>
public enum GridResizeBehavior
{
    /// <summary>
    /// Determine behavior based on splitter alignment.
    /// </summary>
    BasedOnAlignment,

    /// <summary>
    /// Resize the previous and current row/column.
    /// </summary>
    PreviousAndCurrent,

    /// <summary>
    /// Resize the current and next row/column.
    /// </summary>
    CurrentAndNext,

    /// <summary>
    /// Resize the previous and next row/column.
    /// </summary>
    PreviousAndNext
}
