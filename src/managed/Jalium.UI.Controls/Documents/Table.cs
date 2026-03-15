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
}

/// <summary>
/// Represents a column in a table.
/// </summary>
public sealed class TableColumn : DependencyObject
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
public sealed class TableColumnCollection : List<TableColumn>
{
    private readonly Table _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableColumnCollection"/> class.
    /// </summary>
    public TableColumnCollection(Table parent)
    {
        _parent = parent;
    }
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
}

/// <summary>
/// A collection of row groups.
/// </summary>
public sealed class TableRowGroupCollection : List<TableRowGroup>
{
    private readonly Table _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowGroupCollection"/> class.
    /// </summary>
    public TableRowGroupCollection(Table parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds a row group to the collection.
    /// </summary>
    public new void Add(TableRowGroup item)
    {
        item.Parent = _parent;
        base.Add(item);
    }
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
}

/// <summary>
/// A collection of table rows.
/// </summary>
public sealed class TableRowCollection : List<TableRow>
{
    private readonly TableRowGroup _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableRowCollection"/> class.
    /// </summary>
    public TableRowCollection(TableRowGroup parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds a row to the collection.
    /// </summary>
    public new void Add(TableRow item)
    {
        item.Parent = _parent;
        base.Add(item);
    }
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
}

/// <summary>
/// A collection of table cells.
/// </summary>
public sealed class TableCellCollection : List<TableCell>
{
    private readonly TableRow _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableCellCollection"/> class.
    /// </summary>
    public TableCellCollection(TableRow parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds a cell to the collection.
    /// </summary>
    public new void Add(TableCell item)
    {
        item.Parent = _parent;
        base.Add(item);
    }
}
