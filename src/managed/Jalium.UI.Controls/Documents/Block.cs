using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Abstract base class for block flow content elements.
/// </summary>
public abstract class Block : TextElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Margin dependency property.
    /// </summary>
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(Block),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(Block),
            new PropertyMetadata(TextAlignment.Left));

    /// <summary>
    /// Identifies the LineHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(Block),
            new PropertyMetadata(double.NaN));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the margin.
    /// </summary>
    public Thickness Margin
    {
        get => (Thickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding.
    /// </summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the line height.
    /// </summary>
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty)!;
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the next sibling block.
    /// </summary>
    public Block? NextBlock { get; internal set; }

    /// <summary>
    /// Gets or sets the previous sibling block.
    /// </summary>
    public Block? PreviousBlock { get; internal set; }

    #endregion
}

/// <summary>
/// A collection of block elements.
/// </summary>
public sealed class BlockCollection : List<Block>
{
    private readonly TextElement _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockCollection"/> class.
    /// </summary>
    public BlockCollection(TextElement parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds a block element to the collection.
    /// </summary>
    public new void Add(Block item)
    {
        item.Parent = _parent;
        if (Count > 0)
        {
            var last = this[Count - 1];
            last.NextBlock = item;
            item.PreviousBlock = last;
        }
        base.Add(item);
    }

    /// <summary>
    /// Removes a block element from the collection.
    /// </summary>
    public new bool Remove(Block item)
    {
        var result = base.Remove(item);
        if (result)
        {
            item.Parent = null;
            if (item.PreviousBlock != null)
                item.PreviousBlock.NextBlock = item.NextBlock;
            if (item.NextBlock != null)
                item.NextBlock.PreviousBlock = item.PreviousBlock;
            item.NextBlock = null;
            item.PreviousBlock = null;
        }
        return result;
    }

    /// <summary>
    /// Clears all block elements from the collection.
    /// </summary>
    public new void Clear()
    {
        foreach (var item in this)
        {
            item.Parent = null;
            item.NextBlock = null;
            item.PreviousBlock = null;
        }
        base.Clear();
    }
}

/// <summary>
/// A block element that contains inline content.
/// </summary>
public sealed class Paragraph : Block
{
    /// <summary>
    /// Identifies the TextIndent dependency property.
    /// </summary>
    public static readonly DependencyProperty TextIndentProperty =
        DependencyProperty.Register(nameof(TextIndent), typeof(double), typeof(Paragraph),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Gets the collection of inline elements.
    /// </summary>
    public InlineCollection Inlines { get; }

    /// <summary>
    /// Gets or sets the text indentation for the first line.
    /// </summary>
    public double TextIndent
    {
        get => (double)GetValue(TextIndentProperty)!;
        set => SetValue(TextIndentProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Paragraph"/> class.
    /// </summary>
    public Paragraph()
    {
        Inlines = new InlineCollection(this);
        Margin = new Thickness(0, 0, 0, 10);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Paragraph"/> class with the specified inline.
    /// </summary>
    public Paragraph(Inline inline) : this()
    {
        Inlines.Add(inline);
    }
}

/// <summary>
/// A block element that groups other blocks with a visual boundary.
/// </summary>
public sealed class Section : Block
{
    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Section"/> class.
    /// </summary>
    public Section()
    {
        Blocks = new BlockCollection(this);
    }
}

/// <summary>
/// A block element that represents a list.
/// </summary>
public class List : Block
{
    /// <summary>
    /// Identifies the MarkerStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty MarkerStyleProperty =
        DependencyProperty.Register(nameof(MarkerStyle), typeof(TextMarkerStyle), typeof(List),
            new PropertyMetadata(TextMarkerStyle.Disc));

    /// <summary>
    /// Identifies the StartIndex dependency property.
    /// </summary>
    public static readonly DependencyProperty StartIndexProperty =
        DependencyProperty.Register(nameof(StartIndex), typeof(int), typeof(List),
            new PropertyMetadata(1));

    /// <summary>
    /// Gets the collection of list items.
    /// </summary>
    public ListItemCollection ListItems { get; }

    /// <summary>
    /// Gets or sets the marker style.
    /// </summary>
    public TextMarkerStyle MarkerStyle
    {
        get => (TextMarkerStyle)(GetValue(MarkerStyleProperty) ?? TextMarkerStyle.Disc);
        set => SetValue(MarkerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the starting index for numbered lists.
    /// </summary>
    public int StartIndex
    {
        get => (int)GetValue(StartIndexProperty)!;
        set => SetValue(StartIndexProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="List"/> class.
    /// </summary>
    public List()
    {
        ListItems = new ListItemCollection(this);
        Margin = new Thickness(0, 0, 0, 10);
    }
}

/// <summary>
/// A collection of list items.
/// </summary>
public sealed class ListItemCollection : List<ListItem>
{
    private readonly List _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListItemCollection"/> class.
    /// </summary>
    public ListItemCollection(List parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds a list item to the collection.
    /// </summary>
    public new void Add(ListItem item)
    {
        item.Parent = _parent;
        base.Add(item);
    }
}

/// <summary>
/// A list item element.
/// </summary>
public sealed class ListItem : TextElement
{
    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ListItem"/> class.
    /// </summary>
    public ListItem()
    {
        Blocks = new BlockCollection(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ListItem"/> class with a paragraph.
    /// </summary>
    public ListItem(Paragraph paragraph) : this()
    {
        Blocks.Add(paragraph);
    }
}

/// <summary>
/// Specifies the marker style for list items.
/// </summary>
public enum TextMarkerStyle
{
    None,
    Disc,
    Circle,
    Square,
    Box,
    LowerRoman,
    UpperRoman,
    LowerLatin,
    UpperLatin,
    Decimal
}

/// <summary>
/// A block element that represents a block UI container.
/// </summary>
public sealed class BlockUIContainer : Block
{
    /// <summary>
    /// Gets or sets the child UI element.
    /// </summary>
    public UIElement? Child { get; set; }
}

/// <summary>
/// Base class for elements that can be anchored within a paragraph.
/// </summary>
public abstract class AnchoredBlock : Inline
{
    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Identifies the Margin dependency property.
    /// </summary>
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(AnchoredBlock),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the margin.
    /// </summary>
    public Thickness Margin
    {
        get => (Thickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding.
    /// </summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public Thickness BorderThickness
    {
        get => (Thickness)GetValue(BorderThicknessProperty)!;
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush.
    /// </summary>
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchoredBlock"/> class.
    /// </summary>
    protected AnchoredBlock()
    {
        Blocks = new BlockCollection(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AnchoredBlock"/> class with a block.
    /// </summary>
    protected AnchoredBlock(Block block) : this()
    {
        Blocks.Add(block);
    }
}

/// <summary>
/// An anchored element that can be positioned within a flow document.
/// </summary>
public sealed class Figure : AnchoredBlock
{
    /// <summary>
    /// Identifies the HorizontalAnchor dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalAnchorProperty =
        DependencyProperty.Register(nameof(HorizontalAnchor), typeof(FigureHorizontalAnchor), typeof(Figure),
            new PropertyMetadata(FigureHorizontalAnchor.ColumnRight));

    /// <summary>
    /// Identifies the VerticalAnchor dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalAnchorProperty =
        DependencyProperty.Register(nameof(VerticalAnchor), typeof(FigureVerticalAnchor), typeof(Figure),
            new PropertyMetadata(FigureVerticalAnchor.ParagraphTop));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(FigureLength), typeof(Figure),
            new PropertyMetadata(new FigureLength(1, FigureUnitType.Auto)));

    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(FigureLength), typeof(Figure),
            new PropertyMetadata(new FigureLength(1, FigureUnitType.Auto)));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(Figure),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(Figure),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the WrapDirection dependency property.
    /// </summary>
    public static readonly DependencyProperty WrapDirectionProperty =
        DependencyProperty.Register(nameof(WrapDirection), typeof(WrapDirection), typeof(Figure),
            new PropertyMetadata(WrapDirection.Both));

    /// <summary>
    /// Gets or sets the horizontal anchor position.
    /// </summary>
    public FigureHorizontalAnchor HorizontalAnchor
    {
        get => (FigureHorizontalAnchor)(GetValue(HorizontalAnchorProperty) ?? FigureHorizontalAnchor.ColumnRight);
        set => SetValue(HorizontalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical anchor position.
    /// </summary>
    public FigureVerticalAnchor VerticalAnchor
    {
        get => (FigureVerticalAnchor)(GetValue(VerticalAnchorProperty) ?? FigureVerticalAnchor.ParagraphTop);
        set => SetValue(VerticalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public FigureLength Width
    {
        get => (FigureLength)(GetValue(WidthProperty) ?? new FigureLength(1, FigureUnitType.Auto));
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public FigureLength Height
    {
        get => (FigureLength)(GetValue(HeightProperty) ?? new FigureLength(1, FigureUnitType.Auto));
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset.
    /// </summary>
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset.
    /// </summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the wrap direction.
    /// </summary>
    public WrapDirection WrapDirection
    {
        get => (WrapDirection)(GetValue(WrapDirectionProperty) ?? WrapDirection.Both);
        set => SetValue(WrapDirectionProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Figure"/> class.
    /// </summary>
    public Figure()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Figure"/> class with a block.
    /// </summary>
    public Figure(Block block) : base(block)
    {
    }
}

/// <summary>
/// A floating element within a paragraph.
/// </summary>
public sealed class Floater : AnchoredBlock
{
    /// <summary>
    /// Identifies the HorizontalAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalAlignment), typeof(HorizontalAlignment), typeof(Floater),
            new PropertyMetadata(HorizontalAlignment.Left));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(Floater),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalAlignmentProperty)!;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Floater"/> class.
    /// </summary>
    public Floater()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Floater"/> class with a block.
    /// </summary>
    public Floater(Block block) : base(block)
    {
    }
}

/// <summary>
/// Specifies the horizontal anchor for a Figure element.
/// </summary>
public enum FigureHorizontalAnchor
{
    /// <summary>Anchored to the page left.</summary>
    PageLeft,
    /// <summary>Anchored to the page center.</summary>
    PageCenter,
    /// <summary>Anchored to the page right.</summary>
    PageRight,
    /// <summary>Anchored to the content left.</summary>
    ContentLeft,
    /// <summary>Anchored to the content center.</summary>
    ContentCenter,
    /// <summary>Anchored to the content right.</summary>
    ContentRight,
    /// <summary>Anchored to the column left.</summary>
    ColumnLeft,
    /// <summary>Anchored to the column center.</summary>
    ColumnCenter,
    /// <summary>Anchored to the column right.</summary>
    ColumnRight
}

/// <summary>
/// Specifies the vertical anchor for a Figure element.
/// </summary>
public enum FigureVerticalAnchor
{
    /// <summary>Anchored to the page top.</summary>
    PageTop,
    /// <summary>Anchored to the page center.</summary>
    PageCenter,
    /// <summary>Anchored to the page bottom.</summary>
    PageBottom,
    /// <summary>Anchored to the content top.</summary>
    ContentTop,
    /// <summary>Anchored to the content center.</summary>
    ContentCenter,
    /// <summary>Anchored to the content bottom.</summary>
    ContentBottom,
    /// <summary>Anchored to the paragraph top.</summary>
    ParagraphTop
}

/// <summary>
/// Specifies the wrap direction for a Figure element.
/// </summary>
public enum WrapDirection
{
    /// <summary>No wrapping.</summary>
    None,
    /// <summary>Wrap on the left side.</summary>
    Left,
    /// <summary>Wrap on the right side.</summary>
    Right,
    /// <summary>Wrap on both sides.</summary>
    Both
}

/// <summary>
/// Specifies the unit type for Figure dimensions.
/// </summary>
public enum FigureUnitType
{
    /// <summary>Automatic sizing.</summary>
    Auto,
    /// <summary>Size in device-independent pixels.</summary>
    Pixel,
    /// <summary>Size as a proportion of column width.</summary>
    Column,
    /// <summary>Size as a proportion of content width.</summary>
    Content,
    /// <summary>Size as a proportion of page width.</summary>
    Page
}

/// <summary>
/// Describes the width or height of a Figure element.
/// </summary>
public readonly struct FigureLength : IEquatable<FigureLength>
{
    /// <summary>
    /// Gets the value.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Gets the unit type.
    /// </summary>
    public FigureUnitType FigureUnitType { get; }

    /// <summary>
    /// Gets whether this is auto sizing.
    /// </summary>
    public bool IsAuto => FigureUnitType == FigureUnitType.Auto;

    /// <summary>
    /// Gets whether this is absolute sizing.
    /// </summary>
    public bool IsAbsolute => FigureUnitType == FigureUnitType.Pixel;

    /// <summary>
    /// Creates a new FigureLength.
    /// </summary>
    public FigureLength(double value)
    {
        Value = value;
        FigureUnitType = FigureUnitType.Pixel;
    }

    /// <summary>
    /// Creates a new FigureLength with the specified unit type.
    /// </summary>
    public FigureLength(double value, FigureUnitType type)
    {
        Value = value;
        FigureUnitType = type;
    }

    /// <inheritdoc />
    public bool Equals(FigureLength other) => Value == other.Value && FigureUnitType == other.FigureUnitType;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FigureLength other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Value, FigureUnitType);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(FigureLength left, FigureLength right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(FigureLength left, FigureLength right) => !left.Equals(right);
}
