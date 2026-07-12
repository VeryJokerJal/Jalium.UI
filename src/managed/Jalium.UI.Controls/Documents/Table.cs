using System.Collections;
using System.ComponentModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// A block element that represents a table.
/// </summary>
public sealed class Table : Block
{
    /// <summary>
    /// Identifies the CellSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CellSpacingProperty =
        DependencyProperty.Register(nameof(CellSpacing), typeof(double), typeof(Table),
            new PropertyMetadata(2.0));

    /// <summary>
    /// Gets the collection of table columns.
    /// </summary>
    public TableColumnCollection Columns { get; }

    /// <summary>
    /// Gets the collection of row groups.
    /// </summary>
    public TableRowGroupCollection RowGroups { get; }

    /// <summary>
    /// Gets or sets the spacing between cells.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double CellSpacing
    {
        get => (double)GetValue(CellSpacingProperty)!;
        set => SetValue(CellSpacingProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class.
    /// </summary>
    public Table()
    {
        Columns = new TableColumnCollection(this);
        RowGroups = new TableRowGroupCollection(this);
    }

    /// <inheritdoc />
    public override void BeginInit() => base.BeginInit();

    /// <inheritdoc />
    public override void EndInit() => base.EndInit();

    /// <summary>Reports whether explicit table columns should be serialized.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeColumns() => Columns.Count > 0;

    /// <inheritdoc />
    protected internal override IEnumerator LogicalChildren =>
        Columns.Cast<object>().Concat(RowGroups.Cast<object>()).GetEnumerator();

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new TableAutomationPeer(this);
}

/// <summary>
/// Represents a column in a table.
/// </summary>
public class TableColumn : FrameworkContentElement
{
    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(GridLength), typeof(TableColumn),
            new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(TableColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridLength Width
    {
        get => (GridLength)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }
}

/// <summary>
/// A collection of table columns.
/// </summary>
public sealed class TableColumnCollection : IList<TableColumn>, IList
{
    private readonly Table _parent;
    private readonly List<TableColumn> _items = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TableColumnCollection"/> class.
    /// </summary>
    public TableColumnCollection(Table parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_items).SyncRoot;
    public int Capacity { get => _items.Capacity; set => _items.Capacity = value; }
    bool IList.IsFixedSize => false;

    public TableColumn this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _items[index];
            if (ReferenceEquals(previous, value)) return;
            _parent.RemoveLogicalChild(previous);
            _items[index] = value;
            _parent.AddLogicalChild(value);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = Cast(value);
    }

    public void Add(TableColumn item) => Insert(Count, item);
    int IList.Add(object? value) { Add(Cast(value)); return Count - 1; }
    public void Clear()
    {
        foreach (var item in _items) _parent.RemoveLogicalChild(item);
        _items.Clear();
    }
    public bool Contains(TableColumn item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is TableColumn item && Contains(item);
    public void CopyTo(TableColumn[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<TableColumn> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(TableColumn item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is TableColumn item ? IndexOf(item) : -1;
    public void Insert(int index, TableColumn item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Insert(index, item);
        _parent.AddLogicalChild(item);
    }
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    public bool Remove(TableColumn item)
    {
        if (!_items.Remove(item)) return false;
        _parent.RemoveLogicalChild(item);
        return true;
    }
    void IList.Remove(object? value) { if (value is TableColumn item) Remove(item); }
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        _parent.RemoveLogicalChild(item);
    }
    public void RemoveRange(int index, int count)
    {
        foreach (var item in _items.GetRange(index, count)) _parent.RemoveLogicalChild(item);
        _items.RemoveRange(index, count);
    }
    public void TrimToSize() => _items.TrimExcess();

    private static TableColumn Cast(object? value) => value as TableColumn
        ?? throw new ArgumentException("The value must be a TableColumn.", nameof(value));
}

/// <summary>
/// Represents a group of rows in a table.
/// </summary>
public sealed class TableRowGroup : TextElement
{
    /// <summary>
    /// Gets the collection of rows.
    /// </summary>
    public TableRowCollection Rows { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowGroup"/> class.
    /// </summary>
    public TableRowGroup()
    {
        Rows = new TableRowCollection(this);
    }

    /// <summary>
    /// Returns whether the rows collection contains content that should be serialized.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeRows() => Rows.Count > 0;
}

/// <summary>
/// A collection of row groups.
/// </summary>
public sealed class TableRowGroupCollection : IList<TableRowGroup>, IList
{
    private readonly Table _parent;
    private readonly List<TableRowGroup> _items = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowGroupCollection"/> class.
    /// </summary>
    public TableRowGroupCollection(Table parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_items).SyncRoot;
    public int Capacity { get => _items.Capacity; set => _items.Capacity = value; }
    bool IList.IsFixedSize => false;

    public TableRowGroup this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _items[index];
            if (ReferenceEquals(previous, value)) return;
            Detach(previous);
            _items[index] = value;
            Attach(value);
        }
    }
    object? IList.this[int index] { get => this[index]; set => this[index] = Cast(value); }
    public void Add(TableRowGroup item) => Insert(Count, item);
    int IList.Add(object? value) { Add(Cast(value)); return Count - 1; }
    public void Clear() { foreach (var item in _items) Detach(item); _items.Clear(); }
    public bool Contains(TableRowGroup item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is TableRowGroup item && Contains(item);
    public void CopyTo(TableRowGroup[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<TableRowGroup> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(TableRowGroup item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is TableRowGroup item ? IndexOf(item) : -1;
    public void Insert(int index, TableRowGroup item) { ArgumentNullException.ThrowIfNull(item); _items.Insert(index, item); Attach(item); }
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    public bool Remove(TableRowGroup item) { if (!_items.Remove(item)) return false; Detach(item); return true; }
    void IList.Remove(object? value) { if (value is TableRowGroup item) Remove(item); }
    public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); Detach(item); }
    public void RemoveRange(int index, int count) { foreach (var item in _items.GetRange(index, count)) Detach(item); _items.RemoveRange(index, count); }
    public void TrimToSize() => _items.TrimExcess();
    private void Attach(TableRowGroup item) { item.Parent = _parent; _parent.AddLogicalChild(item); }
    private void Detach(TableRowGroup item) { _parent.RemoveLogicalChild(item); item.Parent = null; }
    private static TableRowGroup Cast(object? value) => value as TableRowGroup
        ?? throw new ArgumentException("The value must be a TableRowGroup.", nameof(value));
}

/// <summary>
/// Represents a row in a table.
/// </summary>
public sealed class TableRow : TextElement
{
    /// <summary>
    /// Gets the collection of cells.
    /// </summary>
    public TableCellCollection Cells { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRow"/> class.
    /// </summary>
    public TableRow()
    {
        Cells = new TableCellCollection(this);
    }

    /// <summary>
    /// Returns whether the cells collection contains content that should be serialized.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeCells() => Cells.Count > 0;
}

/// <summary>
/// A collection of table rows.
/// </summary>
public sealed class TableRowCollection : IList<TableRow>, IList
{
    private readonly TableRowGroup _parent;
    private readonly List<TableRow> _items = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowCollection"/> class.
    /// </summary>
    public TableRowCollection(TableRowGroup parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_items).SyncRoot;
    public int Capacity { get => _items.Capacity; set => _items.Capacity = value; }
    bool IList.IsFixedSize => false;
    public TableRow this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _items[index];
            if (ReferenceEquals(previous, value)) return;
            Detach(previous);
            _items[index] = value;
            Attach(value);
        }
    }
    object? IList.this[int index] { get => this[index]; set => this[index] = Cast(value); }
    public void Add(TableRow item) => Insert(Count, item);
    int IList.Add(object? value) { Add(Cast(value)); return Count - 1; }
    public void Clear() { foreach (var item in _items) Detach(item); _items.Clear(); }
    public bool Contains(TableRow item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is TableRow item && Contains(item);
    public void CopyTo(TableRow[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<TableRow> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(TableRow item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is TableRow item ? IndexOf(item) : -1;
    public void Insert(int index, TableRow item) { ArgumentNullException.ThrowIfNull(item); _items.Insert(index, item); Attach(item); }
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    public bool Remove(TableRow item) { if (!_items.Remove(item)) return false; Detach(item); return true; }
    void IList.Remove(object? value) { if (value is TableRow item) Remove(item); }
    public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); Detach(item); }
    public void RemoveRange(int index, int count) { foreach (var item in _items.GetRange(index, count)) Detach(item); _items.RemoveRange(index, count); }
    public void TrimToSize() => _items.TrimExcess();
    private void Attach(TableRow item) { item.Parent = _parent; _parent.AddLogicalChild(item); }
    private void Detach(TableRow item) { _parent.RemoveLogicalChild(item); item.Parent = null; }
    private static TableRow Cast(object? value) => value as TableRow
        ?? throw new ArgumentException("The value must be a TableRow.", nameof(value));
}

/// <summary>
/// Represents a cell in a table row.
/// </summary>
public sealed class TableCell : TextElement
{
    /// <summary>
    /// Identifies the ColumnSpan dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.Register(nameof(ColumnSpan), typeof(int), typeof(TableCell),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the RowSpan dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.Register(nameof(RowSpan), typeof(int), typeof(TableCell),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(TableCell),
            new PropertyMetadata(new Thickness(1)));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(TableCell),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(TableCell),
            new PropertyMetadata(new Thickness(5)));

    public static readonly DependencyProperty FlowDirectionProperty =
        Block.FlowDirectionProperty.AddOwner(typeof(TableCell));

    public static readonly DependencyProperty LineHeightProperty =
        Block.LineHeightProperty.AddOwner(typeof(TableCell));

    public static readonly DependencyProperty LineStackingStrategyProperty =
        Block.LineStackingStrategyProperty.AddOwner(typeof(TableCell));

    public static readonly DependencyProperty TextAlignmentProperty =
        Block.TextAlignmentProperty.AddOwner(typeof(TableCell));

    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets the number of columns spanned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int ColumnSpan
    {
        get => (int)GetValue(ColumnSpanProperty)!;
        set => SetValue(ColumnSpanProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of rows spanned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int RowSpan
    {
        get => (int)GetValue(RowSpanProperty)!;
        set => SetValue(RowSpanProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    [TypeConverter(typeof(LengthConverter))]
    public double LineHeight
    {
        get => (double)(GetValue(LineHeightProperty) ?? double.NaN);
        set => SetValue(LineHeightProperty, value);
    }

    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)(GetValue(LineStackingStrategyProperty) ?? LineStackingStrategy.MaxHeight);
        set => SetValue(LineStackingStrategyProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)(GetValue(TextAlignmentProperty) ?? TextAlignment.Left);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableCell"/> class.
    /// </summary>
    public TableCell()
    {
        Blocks = new BlockCollection(this);
        BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableCell"/> class with a block.
    /// </summary>
    public TableCell(Block block) : this()
    {
        Blocks.Add(block);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableCell"/> class with a paragraph.
    /// </summary>
    public TableCell(Paragraph paragraph) : this((Block)paragraph)
    {
    }

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer() => new TableCellAutomationPeer(this);
}

/// <summary>
/// A collection of table cells.
/// </summary>
public sealed class TableCellCollection : IList<TableCell>, IList
{
    private readonly TableRow _parent;
    private readonly List<TableCell> _items = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TableCellCollection"/> class.
    /// </summary>
    public TableCellCollection(TableRow parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_items).SyncRoot;
    public int Capacity { get => _items.Capacity; set => _items.Capacity = value; }
    bool IList.IsFixedSize => false;
    public TableCell this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var previous = _items[index];
            if (ReferenceEquals(previous, value)) return;
            Detach(previous);
            _items[index] = value;
            Attach(value);
        }
    }
    object? IList.this[int index] { get => this[index]; set => this[index] = Cast(value); }
    public void Add(TableCell item) => Insert(Count, item);
    int IList.Add(object? value) { Add(Cast(value)); return Count - 1; }
    public void Clear() { foreach (var item in _items) Detach(item); _items.Clear(); }
    public bool Contains(TableCell item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is TableCell item && Contains(item);
    public void CopyTo(TableCell[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator<TableCell> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(TableCell item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is TableCell item ? IndexOf(item) : -1;
    public void Insert(int index, TableCell item) { ArgumentNullException.ThrowIfNull(item); _items.Insert(index, item); Attach(item); }
    void IList.Insert(int index, object? value) => Insert(index, Cast(value));
    public bool Remove(TableCell item) { if (!_items.Remove(item)) return false; Detach(item); return true; }
    void IList.Remove(object? value) { if (value is TableCell item) Remove(item); }
    public void RemoveAt(int index) { var item = _items[index]; _items.RemoveAt(index); Detach(item); }
    public void RemoveRange(int index, int count) { foreach (var item in _items.GetRange(index, count)) Detach(item); _items.RemoveRange(index, count); }
    public void TrimToSize() => _items.TrimExcess();
    private void Attach(TableCell item) { item.Parent = _parent; _parent.AddLogicalChild(item); }
    private void Detach(TableCell item) { _parent.RemoveLogicalChild(item); item.Parent = null; }
    private static TableCell Cast(object? value) => value as TableCell
        ?? throw new ArgumentException("The value must be a TableCell.", nameof(value));
}
