using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays data in the columns of a GridView layout by creating a ContentPresenter
/// for each column, applying CellTemplate or DisplayMemberBinding as appropriate.
/// </summary>
public class GridViewRowPresenter : GridViewRowPresenterBase
{
    private readonly List<UIElement> _cellElements = new();

    /// <summary>
    /// Identifies the Content dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentProperty =
        DependencyProperty.Register(nameof(Content), typeof(object), typeof(GridViewRowPresenter),
            new PropertyMetadata(null, OnContentChanged));

    /// <summary>
    /// Gets or sets the data item that this row represents.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridViewRowPresenter presenter)
        {
            presenter.RebuildCells();
        }
    }

    private void RebuildCells()
    {
        ClearPresenterChildren();
        _cellElements.Clear();

        var columns = GetEffectiveColumns();
        if (columns == null || Content == null) return;

        foreach (var column in columns)
        {
            UIElement cellElement;

            if (column.CellTemplate != null || column.CellTemplateSelector != null)
            {
                cellElement = new ContentPresenter
                {
                    Content = Content,
                    ContentTemplate = column.CellTemplate,
                    ContentTemplateSelector = column.CellTemplateSelector,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                // Fall back to TextBlock with DisplayMemberBinding or header name
                var value = ResolveColumnValue(Content, column);
                cellElement = new TextBlock
                {
                    Text = value?.ToString() ?? "",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
            }

            _cellElements.Add(cellElement);
            AddPresenterChild(cellElement);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetEffectiveColumns();
        if (columns == null) return default(Size);

        double totalWidth = 0;
        double maxHeight = 0;

        for (int i = 0; i < _cellElements.Count && i < columns.Count; i++)
        {
            var column = columns[i];
            var measureWidth = double.IsNaN(column.Width)
                ? double.PositiveInfinity
                : GetColumnDisplayWidth(column);
            _cellElements[i].Measure(new Size(measureWidth, availableSize.Height));
            EnsureAutoColumnWidth(column, Math.Max(120, _cellElements[i].DesiredSize.Width));
            var colWidth = GetColumnDisplayWidth(column);
            totalWidth += colWidth;
            maxHeight = Math.Max(maxHeight, _cellElements[i].DesiredSize.Height);
        }

        return new Size(totalWidth, maxHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetEffectiveColumns();
        if (columns == null) return finalSize;

        double x = 0;
        for (int i = 0; i < _cellElements.Count && i < columns.Count; i++)
        {
            var colWidth = GetColumnDisplayWidth(columns[i]);
            _cellElements[i].Arrange(new Rect(x, 0, colWidth, finalSize.Height));
            x += colWidth;
        }

        return finalSize;
    }

    protected override void OnColumnsChanged(
        GridViewColumnCollection? oldColumns,
        GridViewColumnCollection? newColumns)
    {
        base.OnColumnsChanged(oldColumns, newColumns);
        RebuildCells();
    }

    protected override void OnColumnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnColumnCollectionChanged(e);
        RebuildCells();
    }

    protected override void OnColumnPropertyChanged(GridViewColumn column, string? propertyName)
    {
        base.OnColumnPropertyChanged(column, propertyName);
        if (propertyName is nameof(GridViewColumn.CellTemplate)
            or nameof(GridViewColumn.CellTemplateSelector)
            or nameof(GridViewColumn.DisplayMemberBinding))
        {
            RebuildCells();
        }
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        EnsureColumnsFromParent();
        RebuildCells();
    }

    private GridViewColumnCollection? GetEffectiveColumns()
    {
        EnsureColumnsFromParent();
        return Columns;
    }

    private void EnsureColumnsFromParent()
    {
        if (Columns != null)
        {
            return;
        }

        var attached = GridView.GetColumnCollection(this);
        if (attached != null)
        {
            Columns = attached;
            return;
        }

        var parent = VisualParent;
        while (parent != null)
        {
            attached = GridView.GetColumnCollection(parent);
            if (attached != null)
            {
                Columns = attached;
                return;
            }

            if (parent is ListView listView && listView.View is GridView gridView)
            {
                Columns = gridView.Columns;
                return;
            }

            parent = parent.VisualParent;
        }
    }

    private static object? ResolveColumnValue(object item, GridViewColumn column)
    {
        // Try DisplayMemberBinding
        if (column.DisplayMemberBinding is Jalium.UI.Data.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
        {
            return ResolvePropertyPath(item, binding.Path.Path);
        }

        // Try header name as fallback
        var headerText = column.Header?.ToString();
        if (!string.IsNullOrEmpty(headerText))
        {
            var value = ResolvePropertyPath(item, headerText);
            if (value != null) return value;
        }

        if (item is string || item.GetType().IsPrimitive)
            return item;

        return null;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "Property-path resolution here implements GridViewColumn.DisplayMemberBinding (and the header-name fallback), the data-binding reflection fallback over a user-supplied data item whose concrete Type is only known at runtime via object.GetType(). Consumers that bind a GridView column to a property of a user-defined data type must keep that type's public instance properties preserved — the same documented prerequisite that applies to the data-binding surface under trimming/AOT — so this leaf is not a defect of this site.")]
    private static object? ResolvePropertyPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{GetType()} Content:{Content ?? string.Empty} Columns.Count:{Columns?.Count ?? 0}";
}

/// <summary>
/// Displays column headers in a GridView layout.
/// </summary>
public class GridViewHeaderRowPresenter : GridViewRowPresenterBase
{
    public static readonly DependencyProperty ColumnHeaderContainerStyleProperty =
        GridView.ColumnHeaderContainerStyleProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty ColumnHeaderTemplateProperty =
        GridView.ColumnHeaderTemplateProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty ColumnHeaderTemplateSelectorProperty =
        GridView.ColumnHeaderTemplateSelectorProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty ColumnHeaderStringFormatProperty =
        GridView.ColumnHeaderStringFormatProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty AllowsColumnReorderProperty =
        GridView.AllowsColumnReorderProperty.AddOwner(typeof(GridViewHeaderRowPresenter));

    public static readonly DependencyProperty ColumnHeaderContextMenuProperty =
        GridView.ColumnHeaderContextMenuProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty ColumnHeaderToolTipProperty =
        GridView.ColumnHeaderToolTipProperty.AddOwner(
            typeof(GridViewHeaderRowPresenter),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    private readonly List<GridViewColumnHeader> _headers = [];
    private GridViewColumnHeader? _dragHeader;
    private Point _dragStart;

    public Style? ColumnHeaderContainerStyle
    {
        get => (Style?)GetValue(ColumnHeaderContainerStyleProperty);
        set => SetValue(ColumnHeaderContainerStyleProperty, value);
    }

    public DataTemplate? ColumnHeaderTemplate
    {
        get => (DataTemplate?)GetValue(ColumnHeaderTemplateProperty);
        set => SetValue(ColumnHeaderTemplateProperty, value);
    }

    public DataTemplateSelector? ColumnHeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ColumnHeaderTemplateSelectorProperty);
        set => SetValue(ColumnHeaderTemplateSelectorProperty, value);
    }

    public string? ColumnHeaderStringFormat
    {
        get => (string?)GetValue(ColumnHeaderStringFormatProperty);
        set => SetValue(ColumnHeaderStringFormatProperty, value);
    }

    public bool AllowsColumnReorder
    {
        get => (bool)GetValue(AllowsColumnReorderProperty)!;
        set => SetValue(AllowsColumnReorderProperty, value);
    }

    public ContextMenu? ColumnHeaderContextMenu
    {
        get => (ContextMenu?)GetValue(ColumnHeaderContextMenuProperty);
        set => SetValue(ColumnHeaderContextMenuProperty, value);
    }

    public object? ColumnHeaderToolTip
    {
        get => GetValue(ColumnHeaderToolTipProperty);
        set => SetValue(ColumnHeaderToolTipProperty, value);
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        EnsureColumnsFromParent();
        RebuildHeaders();
    }

    protected override void OnColumnsChanged(
        GridViewColumnCollection? oldColumns,
        GridViewColumnCollection? newColumns)
    {
        base.OnColumnsChanged(oldColumns, newColumns);
        RebuildHeaders();
    }

    protected override void OnColumnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnColumnCollectionChanged(e);
        RebuildHeaders();
    }

    protected override void OnColumnPropertyChanged(GridViewColumn column, string? propertyName)
    {
        base.OnColumnPropertyChanged(column, propertyName);
        if (propertyName is null
            or nameof(GridViewColumn.Header)
            or nameof(GridViewColumn.HeaderContainerStyle)
            or nameof(GridViewColumn.HeaderTemplate)
            or nameof(GridViewColumn.HeaderTemplateSelector)
            or nameof(GridViewColumn.HeaderStringFormat))
        {
            RebuildHeaders();
        }
    }

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((GridViewHeaderRowPresenter)d).RebuildHeaders();
    }

    private void RebuildHeaders()
    {
        ClearPresenterChildren();
        _headers.Clear();

        var columns = GetEffectiveColumns();
        if (columns == null)
        {
            return;
        }

        foreach (var column in columns)
        {
            var template = column.HeaderTemplate ?? ColumnHeaderTemplate;
            var selector = column.HeaderTemplateSelector ?? ColumnHeaderTemplateSelector;
            var header = new GridViewColumnHeader
            {
                Column = column,
                Content = FormatHeader(column.Header, column.HeaderStringFormat ?? ColumnHeaderStringFormat, template, selector),
                ContentTemplate = template,
                ContentTemplateSelector = selector,
                ContextMenu = ColumnHeaderContextMenu,
                ToolTip = ColumnHeaderToolTip,
                Style = column.HeaderContainerStyle ?? ColumnHeaderContainerStyle,
                Width = GetColumnDisplayWidth(column)
            };

            _headers.Add(header);
            AddPresenterChild(header);
        }

        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetEffectiveColumns();
        if (columns == null)
        {
            return default;
        }

        double totalWidth = 0;
        double maxHeight = 0;
        for (int i = 0; i < _headers.Count && i < columns.Count; i++)
        {
            var column = columns[i];
            var measureWidth = double.IsNaN(column.Width) ? double.PositiveInfinity : GetColumnDisplayWidth(column);
            _headers[i].Measure(new Size(measureWidth, availableSize.Height));
            EnsureAutoColumnWidth(column, Math.Max(120, _headers[i].DesiredSize.Width));
            var width = GetColumnDisplayWidth(column);
            _headers[i].Width = width;
            totalWidth += width;
            maxHeight = Math.Max(maxHeight, _headers[i].DesiredSize.Height);
        }

        return new Size(totalWidth, maxHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetEffectiveColumns();
        if (columns == null)
        {
            return finalSize;
        }

        double x = 0;
        for (int i = 0; i < _headers.Count && i < columns.Count; i++)
        {
            var width = GetColumnDisplayWidth(columns[i]);
            _headers[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (AllowsColumnReorder && e.Source is GridViewColumnHeader header && header.Role == GridViewColumnHeaderRole.Normal)
        {
            _dragHeader = header;
            _dragStart = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragHeader == null || e.LeftButton != MouseButtonState.Pressed || Columns == null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < 4)
        {
            return;
        }

        var sourceIndex = _headers.IndexOf(_dragHeader);
        var targetColumn = GetColumnAtPosition(position.X);
        var targetIndex = targetColumn == null ? Columns.Count - 1 : Columns.IndexOf(targetColumn);
        if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
        {
            Columns.Move(sourceIndex, targetIndex);
            _dragHeader = targetIndex < _headers.Count ? _headers[targetIndex] : null;
            _dragStart = position;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragHeader = null;
        ReleaseMouseCapture();
    }

    private GridViewColumnCollection? GetEffectiveColumns()
    {
        EnsureColumnsFromParent();
        return Columns;
    }

    private void EnsureColumnsFromParent()
    {
        if (Columns != null)
        {
            return;
        }

        var attached = GridView.GetColumnCollection(this);
        if (attached != null)
        {
            Columns = attached;
            return;
        }

        var parent = VisualParent;
        while (parent != null)
        {
            attached = GridView.GetColumnCollection(parent);
            if (attached != null)
            {
                Columns = attached;
                return;
            }

            if (parent is ListView listView && listView.View is GridView gridView)
            {
                Columns = gridView.Columns;
                return;
            }

            parent = parent.VisualParent;
        }
    }

    private static object? FormatHeader(
        object? value,
        string? format,
        DataTemplate? template,
        DataTemplateSelector? selector)
    {
        if (value == null || string.IsNullOrEmpty(format) || template != null || selector != null)
        {
            return value;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, value);
        }
        catch (FormatException)
        {
            return value;
        }
    }
}
