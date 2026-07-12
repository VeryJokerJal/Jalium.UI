using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Abstract base class for block flow content elements.
/// </summary>
public abstract class Block : TextElement
{
    internal BlockCollection? OwnerCollection { get; set; }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Margin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(Block),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(Block),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(Block),
            new PropertyMetadata(TextAlignment.Left));

    /// <summary>
    /// Identifies the LineHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(Block),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty IsHyphenationEnabledProperty =
        DependencyProperty.RegisterAttached(nameof(IsHyphenationEnabled), typeof(bool), typeof(Block),
            new PropertyMetadata(false, null, null, inherits: true));

    public static readonly DependencyProperty FlowDirectionProperty =
        FrameworkElement.FlowDirectionProperty.AddOwner(
            typeof(Block), new PropertyMetadata(FlowDirection.LeftToRight, null, null, inherits: true));

    public static readonly DependencyProperty LineStackingStrategyProperty =
        DependencyProperty.RegisterAttached(nameof(LineStackingStrategy), typeof(LineStackingStrategy), typeof(Block),
            new PropertyMetadata(LineStackingStrategy.MaxHeight, null, null, inherits: true),
            static value => value is LineStackingStrategy strategy && Enum.IsDefined(strategy));

    public static readonly DependencyProperty BreakPageBeforeProperty =
        DependencyProperty.Register(nameof(BreakPageBefore), typeof(bool), typeof(Block), new PropertyMetadata(false));

    public static readonly DependencyProperty BreakColumnBeforeProperty =
        DependencyProperty.Register(nameof(BreakColumnBefore), typeof(bool), typeof(Block), new PropertyMetadata(false));

    public static readonly DependencyProperty ClearFloatersProperty =
        DependencyProperty.Register(nameof(ClearFloaters), typeof(WrapDirection), typeof(Block),
            new PropertyMetadata(WrapDirection.None),
            static value => value is WrapDirection direction && Enum.IsDefined(direction));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the margin.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Margin
    {
        get => (Thickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
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
    /// Gets or sets the text alignment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the line height.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty)!;
        set => SetValue(LineHeightProperty, value);
    }

    public bool IsHyphenationEnabled
    {
        get => (bool)(GetValue(IsHyphenationEnabledProperty) ?? false);
        set => SetValue(IsHyphenationEnabledProperty, value);
    }

    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)(GetValue(LineStackingStrategyProperty) ?? LineStackingStrategy.MaxHeight);
        set => SetValue(LineStackingStrategyProperty, value);
    }

    public bool BreakPageBefore
    {
        get => (bool)(GetValue(BreakPageBeforeProperty) ?? false);
        set => SetValue(BreakPageBeforeProperty, value);
    }

    public bool BreakColumnBefore
    {
        get => (bool)(GetValue(BreakColumnBeforeProperty) ?? false);
        set => SetValue(BreakColumnBeforeProperty, value);
    }

    public WrapDirection ClearFloaters
    {
        get => (WrapDirection)(GetValue(ClearFloatersProperty) ?? WrapDirection.None);
        set => SetValue(ClearFloatersProperty, value);
    }

    public BlockCollection? SiblingBlocks => OwnerCollection;

    /// <summary>
    /// Gets or sets the next sibling block.
    /// </summary>
    public Block? NextBlock { get; internal set; }

    /// <summary>
    /// Gets or sets the previous sibling block.
    /// </summary>
    public Block? PreviousBlock { get; internal set; }

    public static bool GetIsHyphenationEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsHyphenationEnabledProperty) ?? false);
    }

    public static void SetIsHyphenationEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsHyphenationEnabledProperty, value);
    }

    public static double GetLineHeight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)(element.GetValue(LineHeightProperty) ?? double.NaN);
    }

    public static void SetLineHeight(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LineHeightProperty, value);
    }

    public static LineStackingStrategy GetLineStackingStrategy(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (LineStackingStrategy)(element.GetValue(LineStackingStrategyProperty) ?? LineStackingStrategy.MaxHeight);
    }

    public static void SetLineStackingStrategy(DependencyObject element, LineStackingStrategy value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LineStackingStrategyProperty, value);
    }

    public static TextAlignment GetTextAlignment(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextAlignment)(element.GetValue(TextAlignmentProperty) ?? TextAlignment.Left);
    }

    public static void SetTextAlignment(DependencyObject element, TextAlignment value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextAlignmentProperty, value);
    }

    #endregion
}

/// <summary>
/// A collection of block elements.
/// </summary>
public class BlockCollection : TextElementCollection<Block>
{
    private readonly FrameworkContentElement? _parent;

    internal FrameworkContentElement? Parent => _parent;

    /// <summary>
    /// Occurs after the collection's structure changes.
    /// </summary>
    internal event EventHandler? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockCollection"/> class.
    /// </summary>
    public BlockCollection(FrameworkContentElement? parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Gets the first block in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public Block? FirstBlock => Count == 0 ? null : this[0];

    /// <summary>
    /// Gets the last block in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public Block? LastBlock => Count == 0 ? null : this[Count - 1];

    /// <summary>
    /// Adds a block element to the collection.
    /// </summary>
    public new void Add(Block item)
    {
        PrepareForInsert(item);
        item.Parent = _parent as TextElement;
        if (Count > 0)
        {
            var last = this[Count - 1];
            last.NextBlock = item;
            item.PreviousBlock = last;
        }
        base.Add(item);
        Attach(item);
        RaiseChanged();
    }

    /// <summary>
    /// Inserts a block element at the specified index.
    /// </summary>
    public new void Insert(int index, Block item)
    {
        PrepareForInsert(item);
        item.Parent = _parent as TextElement;
        base.Insert(index, item);
        Attach(item);

        // Rebuild sibling links
        if (index > 0)
        {
            var prev = this[index - 1];
            prev.NextBlock = item;
            item.PreviousBlock = prev;
        }
        else
        {
            item.PreviousBlock = null;
        }

        if (index < Count - 1)
        {
            var next = this[index + 1];
            next.PreviousBlock = item;
            item.NextBlock = next;
        }
        else
        {
            item.NextBlock = null;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Removes a block element from the collection.
    /// </summary>
    public new bool Remove(Block item)
    {
        var result = base.Remove(item);
        if (result)
        {
            Detach(item);
            item.Parent = null;
            if (item.PreviousBlock != null)
                item.PreviousBlock.NextBlock = item.NextBlock;
            if (item.NextBlock != null)
                item.NextBlock.PreviousBlock = item.PreviousBlock;
            item.NextBlock = null;
            item.PreviousBlock = null;
            RaiseChanged();
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
            Detach(item);
            item.Parent = null;
            item.NextBlock = null;
            item.PreviousBlock = null;
        }
        base.Clear();
        RaiseChanged();
    }

    /// <summary>
    /// Adds a sequence of blocks while preserving parent links and change notification.
    /// </summary>
    public void AddRange(IEnumerable<Block> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (var block in collection)
        {
            Add(block);
        }
    }

    /// <summary>
    /// Removes the block at the specified index while preserving sibling links.
    /// </summary>
    public new void RemoveAt(int index) => Remove(this[index]);

    private void Attach(Block item)
    {
        item.OwnerCollection = this;
        _parent?.AddLogicalChild(item);
        item.TextContentChanged += OnItemTextContentChanged;
    }

    private void Detach(Block item)
    {
        item.TextContentChanged -= OnItemTextContentChanged;
        _parent?.RemoveLogicalChild(item);
        item.OwnerCollection = null;
    }

    private static void PrepareForInsert(Block item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.OwnerCollection != null)
        {
            throw new InvalidOperationException("The Block already belongs to a BlockCollection.");
        }
    }

    private void OnItemTextContentChanged(object? sender, EventArgs e) => RaiseChanged();

    private void RaiseChanged()
    {
        if (_parent is TextElement parent)
        {
            parent.NotifyTextContentChanged();
        }
        Changed?.Invoke(this, EventArgs.Empty);
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextIndentProperty =
        DependencyProperty.Register(nameof(TextIndent), typeof(double), typeof(Paragraph),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty KeepTogetherProperty =
        DependencyProperty.Register(nameof(KeepTogether), typeof(bool), typeof(Paragraph), new PropertyMetadata(false));

    public static readonly DependencyProperty KeepWithNextProperty =
        DependencyProperty.Register(nameof(KeepWithNext), typeof(bool), typeof(Paragraph), new PropertyMetadata(false));

    public static readonly DependencyProperty MinOrphanLinesProperty =
        DependencyProperty.Register(
            nameof(MinOrphanLines), typeof(int), typeof(Paragraph), new PropertyMetadata(0), IsValidMinimumLineCount);

    public static readonly DependencyProperty MinWidowLinesProperty =
        DependencyProperty.Register(
            nameof(MinWidowLines), typeof(int), typeof(Paragraph), new PropertyMetadata(0), IsValidMinimumLineCount);

    /// <summary>
    /// Gets the collection of inline elements.
    /// </summary>
    public InlineCollection Inlines { get; }

    /// <summary>
    /// Gets or sets the text indentation for the first line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double TextIndent
    {
        get => (double)GetValue(TextIndentProperty)!;
        set => SetValue(TextIndentProperty, value);
    }

    public bool KeepTogether
    {
        get => (bool)(GetValue(KeepTogetherProperty) ?? false);
        set => SetValue(KeepTogetherProperty, value);
    }

    public bool KeepWithNext
    {
        get => (bool)(GetValue(KeepWithNextProperty) ?? false);
        set => SetValue(KeepWithNextProperty, value);
    }

    public int MinOrphanLines
    {
        get => (int)(GetValue(MinOrphanLinesProperty) ?? 0);
        set => SetValue(MinOrphanLinesProperty, value);
    }

    public int MinWidowLines
    {
        get => (int)(GetValue(MinWidowLinesProperty) ?? 0);
        set => SetValue(MinWidowLinesProperty, value);
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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeInlines(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;

    private static bool IsValidMinimumLineCount(object? value) => value is int count && count >= 0;
}

/// <summary>
/// A block element that groups other blocks with a visual boundary.
/// </summary>
public sealed class Section : Block
{
    private bool _ignoreTrailingParagraphBreakOnPaste;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="Section"/> class with the specified first block.
    /// </summary>
    /// <param name="block">The first block in the section.</param>
    public Section(Block block) : this()
    {
        ArgumentNullException.ThrowIfNull(block);
        Blocks.Add(block);
    }

    /// <summary>
    /// Gets or sets whether clipboard serialization preserves the final paragraph break.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [DefaultValue(true)]
    public bool HasTrailingParagraphBreakOnPaste
    {
        get => !_ignoreTrailingParagraphBreakOnPaste;
        set => _ignoreTrailingParagraphBreakOnPaste = !value;
    }

    /// <summary>
    /// Indicates whether block content should be emitted by a designer serializer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeBlocks(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;
}

/// <summary>
/// A block element that represents a list.
/// </summary>
public class List : Block
{
    /// <summary>
    /// Identifies the MarkerStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MarkerStyleProperty =
        DependencyProperty.Register(nameof(MarkerStyle), typeof(TextMarkerStyle), typeof(List),
            new PropertyMetadata(TextMarkerStyle.Disc));

    /// <summary>
    /// Identifies the MarkerOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarkerOffsetProperty =
        DependencyProperty.Register(nameof(MarkerOffset), typeof(double), typeof(List),
            new PropertyMetadata(double.NaN), IsValidMarkerOffset);

    /// <summary>
    /// Identifies the StartIndex dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public TextMarkerStyle MarkerStyle
    {
        get => (TextMarkerStyle)(GetValue(MarkerStyleProperty) ?? TextMarkerStyle.Disc);
        set => SetValue(MarkerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the distance between list-item content and its marker.
    /// </summary>
    [TypeConverter(typeof(LengthConverter))]
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MarkerOffset
    {
        get => (double)GetValue(MarkerOffsetProperty)!;
        set => SetValue(MarkerOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the starting index for numbered lists.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
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

    /// <summary>
    /// Initializes a new instance of the <see cref="List"/> class with the specified first item.
    /// </summary>
    /// <param name="listItem">The first item in the list.</param>
    public List(ListItem listItem) : this()
    {
        ArgumentNullException.ThrowIfNull(listItem);
        ListItems.Add(listItem);
    }

    private static bool IsValidMarkerOffset(object? value)
    {
        if (value is not double offset)
        {
            return false;
        }

        return double.IsNaN(offset) ||
            (!double.IsInfinity(offset) && offset >= -1_000_000d && offset <= 1_000_000d);
    }
}

/// <summary>
/// A collection of list items.
/// </summary>
public class ListItemCollection : TextElementCollection<ListItem>
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
    /// Gets the first list item in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public ListItem? FirstListItem => Count == 0 ? null : this[0];

    /// <summary>
    /// Gets the last list item in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public ListItem? LastListItem => Count == 0 ? null : this[Count - 1];

    /// <summary>
    /// Adds a list item to the collection.
    /// </summary>
    public new void Add(ListItem item)
    {
        PrepareForInsert(item);
        item.Parent = _parent;
        base.Add(item);
        Attach(item);
        RebuildSiblingLinks();
    }

    public new void Insert(int index, ListItem item)
    {
        PrepareForInsert(item);
        item.Parent = _parent;
        base.Insert(index, item);
        Attach(item);
        RebuildSiblingLinks();
    }

    public new bool Remove(ListItem item)
    {
        if (!base.Remove(item))
        {
            return false;
        }

        Detach(item);
        RebuildSiblingLinks();
        return true;
    }

    public new void RemoveAt(int index) => Remove(this[index]);

    public new void Clear()
    {
        foreach (var item in this.ToArray())
        {
            Detach(item);
        }
        base.Clear();
    }

    private static void PrepareForInsert(ListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.OwnerCollection is not null)
        {
            throw new InvalidOperationException("The ListItem already belongs to a ListItemCollection.");
        }
    }

    private void Attach(ListItem item)
    {
        item.OwnerCollection = this;
        _parent.AddLogicalChild(item);
    }

    private void Detach(ListItem item)
    {
        _parent.RemoveLogicalChild(item);
        item.OwnerCollection = null;
        item.Parent = null;
        item.NextListItem = null;
        item.PreviousListItem = null;
    }

    private void RebuildSiblingLinks()
    {
        for (var index = 0; index < Count; index++)
        {
            this[index].PreviousListItem = index == 0 ? null : this[index - 1];
            this[index].NextListItem = index + 1 < Count ? this[index + 1] : null;
        }
    }
}

/// <summary>
/// A list item element.
/// </summary>
public sealed class ListItem : TextElement
{
    internal ListItemCollection? OwnerCollection { get; set; }

    public static readonly DependencyProperty MarginProperty = Block.MarginProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty PaddingProperty = Block.PaddingProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty BorderThicknessProperty = Block.BorderThicknessProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty BorderBrushProperty = Block.BorderBrushProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty TextAlignmentProperty = Block.TextAlignmentProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty FlowDirectionProperty = Block.FlowDirectionProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty LineHeightProperty = Block.LineHeightProperty.AddOwner(typeof(ListItem));
    public static readonly DependencyProperty LineStackingStrategyProperty = Block.LineStackingStrategyProperty.AddOwner(typeof(ListItem));

    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    public List? List => Parent as List;
    public ListItemCollection? SiblingListItems => OwnerCollection;
    public ListItem? NextListItem { get; internal set; }
    public ListItem? PreviousListItem { get; internal set; }

    public Thickness Margin { get => (Thickness)GetValue(MarginProperty)!; set => SetValue(MarginProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty)!; set => SetValue(PaddingProperty, value); }
    public Thickness BorderThickness { get => (Thickness)GetValue(BorderThicknessProperty)!; set => SetValue(BorderThicknessProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public TextAlignment TextAlignment { get => (TextAlignment)(GetValue(TextAlignmentProperty) ?? TextAlignment.Left); set => SetValue(TextAlignmentProperty, value); }
    public FlowDirection FlowDirection { get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight); set => SetValue(FlowDirectionProperty, value); }

    [TypeConverter(typeof(LengthConverter))]
    public double LineHeight { get => (double)(GetValue(LineHeightProperty) ?? double.NaN); set => SetValue(LineHeightProperty, value); }

    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)(GetValue(LineStackingStrategyProperty) ?? LineStackingStrategy.MaxHeight);
        set => SetValue(LineStackingStrategyProperty, value);
    }

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

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeBlocks(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;
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
    private UIElement? _child;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockUIContainer"/> class.
    /// </summary>
    public BlockUIContainer()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockUIContainer"/> class with a child element.
    /// </summary>
    /// <param name="uiElement">The element to host.</param>
    public BlockUIContainer(UIElement uiElement)
    {
        ArgumentNullException.ThrowIfNull(uiElement);
        Child = uiElement;
    }

    /// <summary>
    /// Gets or sets the child UI element.
    /// </summary>
    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            _child = value;
            NotifyTextContentChanged();
        }
    }
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(AnchoredBlock),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(AnchoredBlock),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LineHeightProperty =
        Block.LineHeightProperty.AddOwner(typeof(AnchoredBlock));

    public static readonly DependencyProperty LineStackingStrategyProperty =
        Block.LineStackingStrategyProperty.AddOwner(typeof(AnchoredBlock));

    public static readonly DependencyProperty TextAlignmentProperty =
        Block.TextAlignmentProperty.AddOwner(typeof(AnchoredBlock));

    /// <summary>
    /// Gets or sets the margin.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Margin
    {
        get => (Thickness)GetValue(MarginProperty)!;
        set => SetValue(MarginProperty, value);
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

    /// <summary>Creates anchored content and optionally inserts it into an existing text container.</summary>
    protected AnchoredBlock(Block? block, TextPointer? insertionPosition) : this()
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (block != null)
        {
            Blocks.Add(block);
        }
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeBlocks(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;
}

/// <summary>
/// An anchored element that can be positioned within a flow document.
/// </summary>
public sealed class Figure : AnchoredBlock
{
    /// <summary>
    /// Identifies the HorizontalAnchor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HorizontalAnchorProperty =
        DependencyProperty.Register(nameof(HorizontalAnchor), typeof(FigureHorizontalAnchor), typeof(Figure),
            new PropertyMetadata(FigureHorizontalAnchor.ColumnRight));

    /// <summary>
    /// Identifies the VerticalAnchor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VerticalAnchorProperty =
        DependencyProperty.Register(nameof(VerticalAnchor), typeof(FigureVerticalAnchor), typeof(Figure),
            new PropertyMetadata(FigureVerticalAnchor.ParagraphTop));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(FigureLength), typeof(Figure),
            new PropertyMetadata(new FigureLength(1, FigureUnitType.Auto)));

    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(FigureLength), typeof(Figure),
            new PropertyMetadata(new FigureLength(1, FigureUnitType.Auto)));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(Figure),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(Figure),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the WrapDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty WrapDirectionProperty =
        DependencyProperty.Register(nameof(WrapDirection), typeof(WrapDirection), typeof(Figure),
            new PropertyMetadata(WrapDirection.Both));

    /// <summary>
    /// Identifies the CanDelayPlacement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CanDelayPlacementProperty =
        DependencyProperty.Register(nameof(CanDelayPlacement), typeof(bool), typeof(Figure),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the horizontal anchor position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FigureHorizontalAnchor HorizontalAnchor
    {
        get => (FigureHorizontalAnchor)(GetValue(HorizontalAnchorProperty) ?? FigureHorizontalAnchor.ColumnRight);
        set => SetValue(HorizontalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical anchor position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FigureVerticalAnchor VerticalAnchor
    {
        get => (FigureVerticalAnchor)(GetValue(VerticalAnchorProperty) ?? FigureVerticalAnchor.ParagraphTop);
        set => SetValue(VerticalAnchorProperty, value);
    }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FigureLength Width
    {
        get => (FigureLength)(GetValue(WidthProperty) ?? new FigureLength(1, FigureUnitType.Auto));
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public FigureLength Height
    {
        get => (FigureLength)(GetValue(HeightProperty) ?? new FigureLength(1, FigureUnitType.Auto));
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the wrap direction.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public WrapDirection WrapDirection
    {
        get => (WrapDirection)(GetValue(WrapDirectionProperty) ?? WrapDirection.Both);
        set => SetValue(WrapDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether layout may defer this figure to a later column or page.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool CanDelayPlacement
    {
        get => (bool)GetValue(CanDelayPlacementProperty)!;
        set => SetValue(CanDelayPlacementProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Figure"/> class.
    /// </summary>
    public Figure() : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Figure"/> class with a block.
    /// </summary>
    public Figure(Block childBlock) : this(childBlock, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Figure"/> class with optional content and insertion position.
    /// </summary>
    /// <param name="childBlock">An optional initial block.</param>
    /// <param name="insertionPosition">An optional position at which to insert the figure.</param>
    public Figure(Block? childBlock, TextPointer? insertionPosition)
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childBlock != null)
        {
            Blocks.Add(childBlock);
        }
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalAlignment), typeof(HorizontalAlignment), typeof(Floater),
            new PropertyMetadata(HorizontalAlignment.Left));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(Floater),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(HorizontalAlignmentProperty)!;
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Floater"/> class.
    /// </summary>
    public Floater() : this(null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Floater"/> class with a block.
    /// </summary>
    public Floater(Block childBlock) : this(childBlock, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Floater"/> class, optionally inserting it at a text position.
    /// </summary>
    /// <param name="childBlock">An optional initial block.</param>
    /// <param name="insertionPosition">An optional position at which to insert the floater.</param>
    public Floater(Block? childBlock, TextPointer? insertionPosition)
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childBlock != null)
        {
            Blocks.Add(childBlock);
        }
    }
}

