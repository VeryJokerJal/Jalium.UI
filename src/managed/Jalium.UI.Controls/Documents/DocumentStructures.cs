using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jalium.UI.Documents.DocumentStructures;

/// <summary>
/// Abstract base class for elements in the document structure tree.
/// </summary>
public abstract class BlockElement
{
}

/// <summary>
/// Represents a break between story fragments.
/// </summary>
public class StoryBreak : BlockElement
{
}

/// <summary>
/// Represents a named element reference in the document structure.
/// </summary>
public class NamedElement : BlockElement
{
    /// <summary>
    /// Gets or sets the name reference for this element.
    /// </summary>
    public string? NameReference { get; set; }
}

/// <summary>
/// Abstract base class for semantic document structure elements that can contain child elements.
/// </summary>
public abstract class SemanticBasicElement : BlockElement
{
    private readonly List<BlockElement> _blockElementList = new();

    /// <summary>
    /// Gets the list of child block elements.
    /// </summary>
    internal List<BlockElement> BlockElementList => _blockElementList;
}

/// <summary>
/// Represents a section in the document structure.
/// </summary>
public sealed class SectionStructure : SemanticBasicElement, IEnumerable<BlockElement>, IEnumerable
{
    /// <summary>
    /// Adds a block element to this section.
    /// </summary>
    public void Add(BlockElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is ParagraphStructure or FigureStructure or ListStructure or TableStructure)
        {
            BlockElementList.Add(element);
            return;
        }

        throw new ArgumentException(
            $"Unexpected parameter type '{element.GetType()}'. Expected: {typeof(ParagraphStructure)}, " +
            $"{typeof(FigureStructure)}, {typeof(ListStructure)}, or {typeof(TableStructure)}.",
            nameof(element));
    }

    IEnumerator<BlockElement> IEnumerable<BlockElement>.GetEnumerator()
    {
        return BlockElementList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<BlockElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a paragraph in the document structure.
/// </summary>
public sealed class ParagraphStructure : SemanticBasicElement, IEnumerable<NamedElement>, IEnumerable
{
    /// <summary>
    /// Adds a named element to this paragraph.
    /// </summary>
    public void Add(NamedElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        BlockElementList.Add(element);
    }

    IEnumerator<NamedElement> IEnumerable<NamedElement>.GetEnumerator()
    {
        return BlockElementList.Cast<NamedElement>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<NamedElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a figure in the document structure.
/// </summary>
public sealed class FigureStructure : SemanticBasicElement, IEnumerable<NamedElement>, IEnumerable
{
    /// <summary>
    /// Adds a named element to this figure.
    /// </summary>
    public void Add(NamedElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        BlockElementList.Add(element);
    }

    IEnumerator<NamedElement> IEnumerable<NamedElement>.GetEnumerator()
    {
        return BlockElementList.Cast<NamedElement>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<NamedElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a list in the document structure.
/// </summary>
public sealed class ListStructure : SemanticBasicElement, IEnumerable<ListItemStructure>, IEnumerable
{
    /// <summary>
    /// Adds a list item to this list.
    /// </summary>
    public void Add(ListItemStructure listItem)
    {
        ArgumentNullException.ThrowIfNull(listItem);

        BlockElementList.Add(listItem);
    }

    IEnumerator<ListItemStructure> IEnumerable<ListItemStructure>.GetEnumerator()
    {
        return BlockElementList.Cast<ListItemStructure>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<ListItemStructure>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a list item in the document structure.
/// </summary>
public sealed class ListItemStructure : SemanticBasicElement, IEnumerable<BlockElement>, IEnumerable
{
    /// <summary>
    /// Gets or sets the marker text for the list item.
    /// </summary>
    public string? Marker { get; set; }

    /// <summary>
    /// Adds a block element to this list item.
    /// </summary>
    public void Add(BlockElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is ParagraphStructure or TableStructure or ListStructure or FigureStructure)
        {
            BlockElementList.Add(element);
            return;
        }

        throw new ArgumentException(
            $"Unexpected parameter type '{element.GetType()}'. Expected: {typeof(ParagraphStructure)}, " +
            $"{typeof(TableStructure)}, {typeof(ListStructure)}, or {typeof(FigureStructure)}.",
            nameof(element));
    }

    IEnumerator<BlockElement> IEnumerable<BlockElement>.GetEnumerator()
    {
        return BlockElementList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<BlockElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a table in the document structure.
/// </summary>
public sealed class TableStructure : SemanticBasicElement, IEnumerable<TableRowGroupStructure>, IEnumerable
{
    /// <summary>
    /// Adds a table row group to this table.
    /// </summary>
    public void Add(TableRowGroupStructure tableRowGroup)
    {
        ArgumentNullException.ThrowIfNull(tableRowGroup);

        BlockElementList.Add(tableRowGroup);
    }

    IEnumerator<TableRowGroupStructure> IEnumerable<TableRowGroupStructure>.GetEnumerator()
    {
        return BlockElementList.Cast<TableRowGroupStructure>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<TableRowGroupStructure>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a table row group in the document structure.
/// </summary>
public sealed class TableRowGroupStructure : SemanticBasicElement, IEnumerable<TableRowStructure>, IEnumerable
{
    /// <summary>
    /// Adds a table row to this row group.
    /// </summary>
    public void Add(TableRowStructure tableRow)
    {
        ArgumentNullException.ThrowIfNull(tableRow);

        BlockElementList.Add(tableRow);
    }

    IEnumerator<TableRowStructure> IEnumerable<TableRowStructure>.GetEnumerator()
    {
        return BlockElementList.Cast<TableRowStructure>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<TableRowStructure>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a table row in the document structure.
/// </summary>
public sealed class TableRowStructure : SemanticBasicElement, IEnumerable<TableCellStructure>, IEnumerable
{
    /// <summary>
    /// Adds a table cell to this row.
    /// </summary>
    public void Add(TableCellStructure tableCell)
    {
        ArgumentNullException.ThrowIfNull(tableCell);

        BlockElementList.Add(tableCell);
    }

    IEnumerator<TableCellStructure> IEnumerable<TableCellStructure>.GetEnumerator()
    {
        return BlockElementList.Cast<TableCellStructure>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<TableCellStructure>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a table cell in the document structure.
/// </summary>
public sealed class TableCellStructure : SemanticBasicElement, IEnumerable<BlockElement>, IEnumerable
{
    private int _rowSpan = 1;
    private int _columnSpan = 1;

    /// <summary>
    /// Gets or sets the number of rows this cell spans.
    /// </summary>
    public int RowSpan
    {
        get => _rowSpan;
        set => _rowSpan = value;
    }

    /// <summary>
    /// Gets or sets the number of columns this cell spans.
    /// </summary>
    public int ColumnSpan
    {
        get => _columnSpan;
        set => _columnSpan = value;
    }

    /// <summary>
    /// Adds a block element to this cell.
    /// </summary>
    public void Add(BlockElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is ParagraphStructure or TableStructure or ListStructure or FigureStructure)
        {
            BlockElementList.Add(element);
            return;
        }

        throw new ArgumentException(
            $"Unexpected parameter type '{element.GetType()}'. Expected: {typeof(ParagraphStructure)}, " +
            $"{typeof(TableStructure)}, {typeof(ListStructure)}, or {typeof(FigureStructure)}.",
            nameof(element));
    }

    IEnumerator<BlockElement> IEnumerable<BlockElement>.GetEnumerator()
    {
        return BlockElementList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<BlockElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a fragment of a story in the document structure.
/// A story fragment is a portion of a story that is contained within a single page.
/// </summary>
public sealed class StoryFragment : IEnumerable<BlockElement>, IEnumerable
{
    private readonly List<BlockElement> _blockElementList = new();

    /// <summary>
    /// Gets or sets the story name.
    /// </summary>
    public string? StoryName { get; set; }

    /// <summary>
    /// Gets or sets the fragment name.
    /// </summary>
    public string? FragmentName { get; set; }

    /// <summary>
    /// Gets or sets the fragment type.
    /// </summary>
    public string? FragmentType { get; set; }

    /// <summary>
    /// Gets the list of block elements in this fragment.
    /// </summary>
    internal List<BlockElement> BlockElementList => _blockElementList;

    /// <summary>
    /// Adds a block element to this story fragment.
    /// </summary>
    public void Add(BlockElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (element is SectionStructure or ParagraphStructure or FigureStructure
            or ListStructure or TableStructure or StoryBreak)
        {
            _blockElementList.Add(element);
            return;
        }

        throw new ArgumentException(
            $"Unexpected parameter type '{element.GetType()}'. Expected: {typeof(SectionStructure)}, " +
            $"{typeof(ParagraphStructure)}, {typeof(FigureStructure)}, {typeof(ListStructure)}, " +
            $"{typeof(TableStructure)}, or {typeof(StoryBreak)}.",
            nameof(element));
    }

    IEnumerator<BlockElement> IEnumerable<BlockElement>.GetEnumerator()
    {
        return BlockElementList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<BlockElement>)this).GetEnumerator();
    }
}

/// <summary>
/// Represents a collection of story fragments in the document structure.
/// This is the root element for XPS document structure markup.
/// </summary>
public sealed class StoryFragments : IEnumerable<StoryFragment>, IEnumerable
{
    private readonly List<StoryFragment> _storyFragmentList = new();

    /// <summary>
    /// Gets the list of story fragments.
    /// </summary>
    internal List<StoryFragment> StoryFragmentList => _storyFragmentList;

    /// <summary>
    /// Adds a story fragment to this collection.
    /// </summary>
    public void Add(StoryFragment storyFragment)
    {
        ArgumentNullException.ThrowIfNull(storyFragment);

        _storyFragmentList.Add(storyFragment);
    }

    IEnumerator<StoryFragment> IEnumerable<StoryFragment>.GetEnumerator()
    {
        return _storyFragmentList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable<StoryFragment>)this).GetEnumerator();
    }
}
