using System.Reflection;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Displays data in the columns of a GridView layout by creating a ContentPresenter
/// for each column, applying CellTemplate or DisplayMemberBinding as appropriate.
/// </summary>
public class GridViewRowPresenter : FrameworkElement
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
        // Clear existing cells
        foreach (var cell in _cellElements)
        {
            RemoveVisualChild(cell);
        }
        _cellElements.Clear();

        var columns = GetGridViewColumns();
        if (columns == null || Content == null) return;

        foreach (var column in columns)
        {
            UIElement cellElement;

            if (column.CellTemplate != null)
            {
                // Use ContentPresenter with CellTemplate
                cellElement = new ContentPresenter
                {
                    Content = Content,
                    ContentTemplate = column.CellTemplate,
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
            AddVisualChild(cellElement);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _cellElements.Count;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index < 0 || index >= _cellElements.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _cellElements[index];
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetGridViewColumns();
        if (columns == null) return Size.Empty;

        double totalWidth = 0;
        double maxHeight = 0;

        for (int i = 0; i < _cellElements.Count && i < columns.Count; i++)
        {
            var colWidth = columns[i].ActualWidth;
            _cellElements[i].Measure(new Size(colWidth, availableSize.Height));
            totalWidth += colWidth;
            maxHeight = Math.Max(maxHeight, _cellElements[i].DesiredSize.Height);
        }

        return new Size(totalWidth, maxHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetGridViewColumns();
        if (columns == null) return finalSize;

        double x = 0;
        for (int i = 0; i < _cellElements.Count && i < columns.Count; i++)
        {
            var colWidth = columns[i].ActualWidth;
            _cellElements[i].Arrange(new Rect(x, 0, colWidth, finalSize.Height));
            x += colWidth;
        }

        return finalSize;
    }

    private Controls.GridViewColumnCollection? GetGridViewColumns()
    {
        // Walk up to find a ListView with a GridView
        var parent = VisualParent;
        while (parent != null)
        {
            if (parent is ListView listView && listView.View is GridView gridView)
                return gridView.Columns;
            parent = parent.VisualParent;
        }
        return null;
    }

    private static object? ResolveColumnValue(object item, Controls.GridViewColumn column)
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
}

/// <summary>
/// Displays column headers in a GridView layout.
/// </summary>
public class GridViewHeaderRowPresenter : FrameworkElement
{
    private readonly List<GridViewColumnHeader> _headers = new();
    private Controls.GridViewColumnCollection? _subscribedColumns;

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // Unsubscribe from old columns
        if (_subscribedColumns != null)
        {
            _subscribedColumns.CollectionChanged -= OnColumnsChanged;
            _subscribedColumns = null;
        }

        // Subscribe to new columns and rebuild
        var columns = GetGridViewColumns();
        if (columns != null)
        {
            _subscribedColumns = columns;
            columns.CollectionChanged += OnColumnsChanged;
        }
        RebuildHeaders();
    }

    private void OnColumnsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RebuildHeaders();
    }

    private void RebuildHeaders()
    {
        foreach (var header in _headers)
        {
            RemoveVisualChild(header);
        }
        _headers.Clear();

        var columns = GetGridViewColumns();
        if (columns == null) return;

        foreach (var column in columns)
        {
            var header = new GridViewColumnHeader
            {
                Column = column,
                Content = column.Header,
                Width = column.ActualWidth
            };

            if (column.HeaderContainerStyle != null)
                header.Style = column.HeaderContainerStyle;

            _headers.Add(header);
            AddVisualChild(header);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _headers.Count;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index < 0 || index >= _headers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _headers[index];
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = GetGridViewColumns();
        if (columns == null) return Size.Empty;

        double totalWidth = 0;
        double maxHeight = 0;

        for (int i = 0; i < _headers.Count && i < columns.Count; i++)
        {
            var colWidth = columns[i].ActualWidth;
            _headers[i].Width = colWidth;
            _headers[i].Measure(new Size(colWidth, availableSize.Height));
            totalWidth += colWidth;
            maxHeight = Math.Max(maxHeight, _headers[i].DesiredSize.Height);
        }

        return new Size(totalWidth, maxHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = GetGridViewColumns();
        if (columns == null) return finalSize;

        double x = 0;
        for (int i = 0; i < _headers.Count && i < columns.Count; i++)
        {
            var colWidth = columns[i].ActualWidth;
            _headers[i].Arrange(new Rect(x, 0, colWidth, finalSize.Height));
            x += colWidth;
        }

        return finalSize;
    }

    private Controls.GridViewColumnCollection? GetGridViewColumns()
    {
        var parent = VisualParent;
        while (parent != null)
        {
            if (parent is ListView listView && listView.View is GridView gridView)
                return gridView.Columns;
            parent = parent.VisualParent;
        }
        return null;
    }
}
