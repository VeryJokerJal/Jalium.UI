using System.ComponentModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a column header in a DataGrid.
/// </summary>
public class DataGridColumnHeader : ButtonBase, Jalium.UI.Controls.IProvideDataGridColumn
{
    private static readonly ComponentResourceKey s_columnFloatingHeaderStyleKey =
        new(typeof(DataGridColumnHeader), nameof(ColumnFloatingHeaderStyleKey));
    private static readonly ComponentResourceKey s_columnHeaderDropSeparatorStyleKey =
        new(typeof(DataGridColumnHeader), nameof(ColumnHeaderDropSeparatorStyleKey));

    /// <summary>Gets the resource key for the floating column-header style.</summary>
    public static ComponentResourceKey ColumnFloatingHeaderStyleKey => s_columnFloatingHeaderStyleKey;

    /// <summary>Gets the resource key for the column-header drop-separator style.</summary>
    public static ComponentResourceKey ColumnHeaderDropSeparatorStyleKey =>
        s_columnHeaderDropSeparatorStyleKey;

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_pressedBgBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_defaultBgBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_defaultFgBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultSeparatorBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(67, 67, 70));

    #endregion

    // Cached pens
    private Pen? _separatorPen;
    private Brush? _separatorPenBrush;
    private Pen? _sortIndicatorPen;
    private Brush? _sortIndicatorPenBrush;
    private Pen? _bottomBorderPen;
    private Brush? _bottomBorderPenBrush;

    #region Dependency Properties

    /// <summary>
    /// Identifies the SortDirection dependency property.
    /// </summary>
    private static readonly DependencyPropertyKey SortDirectionPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(SortDirection), typeof(ListSortDirection?), typeof(DataGridColumnHeader),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty SortDirectionProperty = SortDirectionPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the CanUserSort dependency property.
    /// </summary>
    private static readonly DependencyPropertyKey CanUserSortPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanUserSort), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserSortProperty = CanUserSortPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the DisplayIndex dependency property.
    /// </summary>
    private static readonly DependencyPropertyKey DisplayIndexPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(DisplayIndex), typeof(int), typeof(DataGridColumnHeader),
            new PropertyMetadata(-1));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayIndexProperty = DisplayIndexPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsFrozen dependency property.
    /// </summary>
    private static readonly DependencyPropertyKey IsFrozenPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFrozen), typeof(bool), typeof(DataGridColumnHeader),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsFrozenProperty = IsFrozenPropertyKey.DependencyProperty;

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
    /// Gets the sort direction for this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ListSortDirection? SortDirection => (ListSortDirection?)GetValue(SortDirectionProperty);

    /// <summary>
    /// Gets a value indicating whether the user can sort by this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserSort => (bool)GetValue(CanUserSortProperty)!;

    /// <summary>
    /// Gets the display index of this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int DisplayIndex => (int)GetValue(DisplayIndexProperty)!;

    /// <summary>
    /// Gets a value indicating whether this column is frozen.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsFrozen => (bool)GetValue(IsFrozenProperty)!;

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
    /// Gets the column associated with this header.
    /// </summary>
    public Jalium.UI.Controls.DataGridColumn? Column => _column;

    #endregion

    #region Private Fields

    private const double ResizeHotZoneWidth = 8.0;
    private const double DragThreshold = 5.0;

    private Jalium.UI.Controls.DataGridColumn? _column;
    private TextBlock? _sortIndicator;
    private FrameworkElement? _resizeGrip;
    private bool _isResizing;
    private double _resizeStartX;
    private double _resizeStartWidth;
    private bool _isDragging;
    private Point _mouseDownPoint;
    private bool _mouseDownForDrag;

    internal Jalium.UI.Controls.DataGrid? ParentDataGrid { get; private set; }
    internal Jalium.UI.Controls.IColumnHeaderHost? ColumnHost { get; private set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGridColumnHeader"/> class.
    /// </summary>
    public DataGridColumnHeader()
    {
        UseTemplateContentManagement();
        Focusable = false;

        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownHandler), true);
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler), true);
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler), true);
    }

    #endregion

    #region Column Integration and Interaction

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _sortIndicator = GetTemplateChild("PART_SortIndicator") as TextBlock;
        _resizeGrip = GetTemplateChild("PART_ResizeGrip") as FrameworkElement;

        if (_resizeGrip != null)
        {
            _resizeGrip.IsHitTestVisible = false;
        }

        UpdateResizeGripState();
        UpdateSortIndicatorVisual();
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.DataGridColumnHeaderAutomationPeer(this);

    internal void PrepareColumnHeader(
        object? item,
        Jalium.UI.Controls.DataGrid? parentDataGrid,
        Jalium.UI.Controls.IColumnHeaderHost? columnHost,
        Jalium.UI.Controls.DataGridColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        _column = column;
        ParentDataGrid = parentDataGrid;
        ColumnHost = columnHost;
        Content = item;
        SynchronizeColumnState();
    }

    internal void ClearColumnHeader()
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _isResizing = false;
        _isDragging = false;
        _mouseDownForDrag = false;
        Cursor = null;
        _column = null;
        ParentDataGrid = null;
        ColumnHost = null;
        Content = null;
        SetValue(SortDirectionPropertyKey, null);
        SetValue(CanUserSortPropertyKey, true);
        SetValue(DisplayIndexPropertyKey, -1);
        SetValue(IsFrozenPropertyKey, false);
        UpdateResizeGripState();
        UpdateSortIndicatorVisual();
    }

    internal void SynchronizeColumnState()
    {
        SetValue(SortDirectionPropertyKey, Column?.SortDirection);
        SetValue(CanUserSortPropertyKey, Column?.CanUserSort ?? true);
        SetValue(DisplayIndexPropertyKey, Column?.DisplayIndex ?? -1);
        SetValue(IsFrozenPropertyKey, Column?.IsFrozen ?? false);
        UpdateResizeGripState();
        UpdateSortIndicatorVisual();
    }

    internal void UpdateSortIndicator(ListSortDirection? direction)
    {
        SetValue(SortDirectionPropertyKey, direction);
        UpdateSortIndicatorVisual();
    }

    private void UpdateSortIndicatorVisual()
    {
        if (_sortIndicator == null)
        {
            return;
        }

        if (SortDirection.HasValue)
        {
            _sortIndicator.Text = SortDirection == ListSortDirection.Ascending ? "\u25B2" : "\u25BC";
            _sortIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            _sortIndicator.Text = string.Empty;
            _sortIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateResizeGripState()
    {
        if (_resizeGrip != null)
        {
            _resizeGrip.Visibility = CanResizeCurrentColumn() ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private bool CanResizeCurrentColumn() =>
        Column != null
        && (ColumnHost?.CanUserResizeColumns ?? false)
        && Column.CanUserResize;

    private bool CanDragCurrentColumn() =>
        Column != null
        && (ColumnHost?.CanUserReorderColumns ?? false)
        && Column.CanUserReorder;

    private bool IsInResizeZone(Point point)
    {
        if (!CanResizeCurrentColumn())
        {
            return false;
        }

        var hotZoneWidth = Math.Max(1.0, Math.Min(RenderSize.Width, ResizeHotZoneWidth));
        return point.X >= Math.Max(0.0, RenderSize.Width - hotZoneWidth);
    }

    private void UpdateResizeCursor(Point point)
    {
        if (_isDragging)
        {
            return;
        }

        Cursor = (_isResizing || IsInResizeZone(point)) ? Cursors.SizeWE : null;
    }

    private void OnPreviewMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || Column == null)
        {
            return;
        }

        if (IsInResizeZone(e.GetPosition(this)))
        {
            BeginResize(e.GetPosition(null).X);
            e.Handled = true;
        }
        else if (CanDragCurrentColumn())
        {
            _mouseDownPoint = e.GetPosition(null);
            _mouseDownForDrag = true;
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var host = ColumnHost;
        if (_isResizing && Column != null)
        {
            UpdateResize(e.GetPosition(null).X);
            Cursor = Cursors.SizeWE;
            e.Handled = true;
            return;
        }

        if (_isDragging && host is UIElement hostElement)
        {
            host.UpdateColumnDrag(e.GetPosition(hostElement));
            e.Handled = true;
            return;
        }

        if (_mouseDownForDrag && !_isDragging)
        {
            var currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _mouseDownPoint.X) > DragThreshold)
            {
                _isDragging = true;
                _mouseDownForDrag = false;
                Cursor = Cursors.SizeAll;
                host?.StartColumnDrag(this, Column!);
                e.Handled = true;
                return;
            }
        }

        UpdateResizeCursor(e.GetPosition(this));
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing)
        {
            EndResize();
            UpdateResizeCursor(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            Cursor = null;
            if (ColumnHost is UIElement hostElement)
            {
                ColumnHost.EndColumnDrag(e.GetPosition(hostElement));
            }

            e.Handled = true;
            return;
        }

        if (_mouseDownForDrag)
        {
            _mouseDownForDrag = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();

        if (_isDragging && ColumnHost is { IsColumnDragging: true } host)
        {
            host.CancelColumnDrag();
        }

        _isDragging = false;
        _isResizing = false;
        _mouseDownForDrag = false;
        Cursor = null;
    }

    internal void BeginResize(double startX)
    {
        if (!CanResizeCurrentColumn() || Column == null)
        {
            return;
        }

        _isResizing = true;
        _resizeStartX = startX;
        _resizeStartWidth = Column.ActualWidth;
        if (!(_resizeStartWidth > 0))
        {
            _resizeStartWidth = double.IsFinite(Width) && Width > 0 ? Width : Column.Width.DisplayValue;
        }

        CaptureMouse();
        Cursor = Cursors.SizeWE;
    }

    internal void UpdateResize(double currentX)
    {
        if (!_isResizing || Column == null)
        {
            return;
        }

        var newWidth = Math.Clamp(_resizeStartWidth + currentX - _resizeStartX, Column.MinWidth, Column.MaxWidth);
        ColumnHost?.ResizeColumn(Column, newWidth);
    }

    internal void EndResize()
    {
        _isResizing = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
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
        if (_bottomBorderPen == null || _bottomBorderPenBrush != borderBrush)
        {
            _bottomBorderPen = new Pen(borderBrush, 1);
            _bottomBorderPenBrush = borderBrush;
        }
        return _bottomBorderPen;
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
    protected override void OnRender(DrawingContext drawingContext)
    {
        // A template owns the visual tree in normal DataGrid use. Keep the
        // lightweight renderer only as a template-less fallback.
        if (Template != null)
        {
            return;
        }

        var dc = drawingContext;

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
            var formattedText = new FormattedText(text, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 12)
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
            if (_separatorPen == null || _separatorPenBrush != separatorBrush)
            {
                _separatorPen = new Pen(separatorBrush, 1);
                _separatorPenBrush = separatorBrush;
            }
            dc.DrawLine(_separatorPen, new Point(rect.Width - 1, 0), new Point(rect.Width - 1, rect.Height));
        }

        // Draw bottom border
        dc.DrawLine(ResolveBottomBorderPen(), new Point(0, rect.Height - 1), new Point(rect.Width, rect.Height - 1));
    }

    // Sort indicator chevrons in 8×4 design space, cached
    private static readonly PathGeometry s_sortUp = (PathGeometry)Geometry.Parse("M 0,4 L 4,0 L 8,4");
    private static readonly PathGeometry s_sortDown = (PathGeometry)Geometry.Parse("M 0,0 L 4,4 L 8,0");

    private void DrawSortIndicator(DrawingContext dc, Rect rect, double offsetX, Brush brush)
    {
        if (_sortIndicatorPen == null || _sortIndicatorPenBrush != brush)
        {
            _sortIndicatorPen = new Pen(brush, 1.5);
            _sortIndicatorPenBrush = brush;
        }

        var source = SortDirection == ListSortDirection.Ascending ? s_sortUp : s_sortDown;
        var bounds = source.Bounds;
        var ox = offsetX + 6 - bounds.X - bounds.Width / 2;
        var oy = rect.Height / 2 - bounds.Y - bounds.Height / 2;

        foreach (var figure in source.Figures)
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
            dc.DrawGeometry(null, _sortIndicatorPen, geo);
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumnHeader header)
        {
            header.InvalidateVisual();
        }
    }

    #endregion
}
