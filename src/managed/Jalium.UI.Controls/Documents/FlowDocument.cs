using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a flow document that hosts rich flow content.
/// </summary>
public sealed class FlowDocument : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(string), typeof(FlowDocument),
            new PropertyMetadata("Segoe UI"));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(14.0));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(FlowDocument),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(FlowDocument),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the PageWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageWidthProperty =
        DependencyProperty.Register(nameof(PageWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the PageHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageHeightProperty =
        DependencyProperty.Register(nameof(PageHeight), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the PagePadding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PagePaddingProperty =
        DependencyProperty.Register(nameof(PagePadding), typeof(Thickness), typeof(FlowDocument),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the ColumnWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnWidthProperty =
        DependencyProperty.Register(nameof(ColumnWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the ColumnGap dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(nameof(ColumnGap), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(FlowDocument),
            new PropertyMetadata(TextAlignment.Left));

    /// <summary>
    /// Identifies the LineHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the IsOptimalParagraphEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOptimalParagraphEnabledProperty =
        DependencyProperty.Register(nameof(IsOptimalParagraphEnabled), typeof(bool), typeof(FlowDocument),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsHyphenationEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHyphenationEnabledProperty =
        DependencyProperty.Register(nameof(IsHyphenationEnabled), typeof(bool), typeof(FlowDocument),
            new PropertyMetadata(false));

    #endregion

    #region Properties

    /// <summary>
    /// Gets the collection of block elements.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public string FontFamily
    {
        get => (string)(GetValue(FontFamilyProperty) ?? "Segoe UI");
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
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

    /// <summary>
    /// Gets or sets the page width.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double PageWidth
    {
        get => (double)GetValue(PageWidthProperty)!;
        set => SetValue(PageWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the page height.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double PageHeight
    {
        get => (double)GetValue(PageHeightProperty)!;
        set => SetValue(PageHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the page padding.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness PagePadding
    {
        get => (Thickness)GetValue(PagePaddingProperty)!;
        set => SetValue(PagePaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the column width.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty)!;
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the gap between columns.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty)!;
        set => SetValue(ColumnGapProperty, value);
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

    /// <summary>
    /// Gets or sets whether optimal paragraph layout is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOptimalParagraphEnabled
    {
        get => (bool)GetValue(IsOptimalParagraphEnabledProperty)!;
        set => SetValue(IsOptimalParagraphEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether hyphenation is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHyphenationEnabled
    {
        get => (bool)GetValue(IsHyphenationEnabledProperty)!;
        set => SetValue(IsHyphenationEnabledProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocument"/> class.
    /// </summary>
    public FlowDocument()
    {
        Blocks = new BlockCollection(null!);
        Foreground = new SolidColorBrush(Color.Black);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocument"/> class with a block.
    /// </summary>
    public FlowDocument(Block block) : this()
    {
        Blocks.Add(block);
    }

    #endregion

    #region TextPointer Properties

    /// <summary>
    /// Gets a TextPointer at the start of the document content.
    /// </summary>
    public TextPointer ContentStart
    {
        get
        {
            if (Blocks.Count > 0)
            {
                var firstBlock = Blocks[0];
                if (firstBlock is Paragraph p && p.Inlines.Count > 0)
                {
                    return new TextPointer(this, p.Inlines[0], 0, LogicalDirection.Forward);
                }
                return new TextPointer(this, firstBlock, 0, LogicalDirection.Forward);
            }
            return new TextPointer(this, null, 0, LogicalDirection.Forward);
        }
    }

    /// <summary>
    /// Gets a TextPointer at the end of the document content.
    /// </summary>
    public TextPointer ContentEnd
    {
        get
        {
            int totalLength = GetDocumentLength();
            return GetPositionAtOffset(totalLength, LogicalDirection.Backward)
                ?? new TextPointer(this, null, totalLength, LogicalDirection.Backward);
        }
    }

    #endregion

    #region TextPointer Methods

    /// <summary>
    /// Gets a TextPointer at the specified offset from the start of the document.
    /// </summary>
    /// <param name="offset">The character offset from the start.</param>
    /// <param name="direction">The logical direction for the TextPointer.</param>
    /// <returns>A TextPointer at the specified offset, or null if the offset is invalid.</returns>
    public TextPointer? GetPositionAtOffset(int offset, LogicalDirection direction)
    {
        if (offset < 0)
            return null;

        int currentOffset = 0;

        foreach (var block in Blocks)
        {
            var result = GetPositionInBlock(block, offset, direction, ref currentOffset);
            if (result != null)
                return result;
        }

        // If offset is at the very end
        if (offset == currentOffset)
        {
            if (Blocks.Count > 0)
            {
                var lastBlock = Blocks[Blocks.Count - 1];
                return new TextPointer(this, lastBlock, GetBlockLength(lastBlock), direction);
            }
            return new TextPointer(this, null, 0, direction);
        }

        return null;
    }

    private TextPointer? GetPositionInBlock(Block block, int targetOffset, LogicalDirection direction, ref int currentOffset)
    {
        if (block is Paragraph p)
        {
            foreach (var inline in p.Inlines)
            {
                var result = GetPositionInInline(inline, targetOffset, direction, ref currentOffset);
                if (result != null)
                    return result;
            }

            // Paragraph break
            if (targetOffset == currentOffset)
            {
                return new TextPointer(this, p, GetParagraphTextLength(p), direction);
            }
            currentOffset++; // paragraph break counts as 1
        }
        else if (block is Section section)
        {
            foreach (var childBlock in section.Blocks)
            {
                var result = GetPositionInBlock(childBlock, targetOffset, direction, ref currentOffset);
                if (result != null)
                    return result;
            }
        }
        else if (block is List list)
        {
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    var result = GetPositionInBlock(itemBlock, targetOffset, direction, ref currentOffset);
                    if (result != null)
                        return result;
                }
            }
        }
        else if (block is BlockUIContainer)
        {
            if (targetOffset >= currentOffset && targetOffset < currentOffset + 1)
            {
                return new TextPointer(this, block, targetOffset - currentOffset, direction);
            }
            currentOffset++;
        }

        return null;
    }

    private TextPointer? GetPositionInInline(Inline inline, int targetOffset, LogicalDirection direction, ref int currentOffset)
    {
        if (inline is Run run)
        {
            int textLength = run.Text.Length;
            if (targetOffset >= currentOffset && targetOffset <= currentOffset + textLength)
            {
                return new TextPointer(this, run, targetOffset - currentOffset, direction);
            }
            currentOffset += textLength;
        }
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
            {
                var result = GetPositionInInline(child, targetOffset, direction, ref currentOffset);
                if (result != null)
                    return result;
            }
        }
        else if (inline is LineBreak)
        {
            if (targetOffset == currentOffset)
            {
                return new TextPointer(this, inline, 0, direction);
            }
            currentOffset++;
        }
        else if (inline is InlineUIContainer)
        {
            if (targetOffset >= currentOffset && targetOffset < currentOffset + 1)
            {
                return new TextPointer(this, inline, targetOffset - currentOffset, direction);
            }
            currentOffset++;
        }

        return null;
    }

    /// <summary>
    /// Gets the total document length in characters.
    /// </summary>
    private int GetDocumentLength()
    {
        int length = 0;
        foreach (var block in Blocks)
        {
            length += GetBlockLength(block);
        }
        return length;
    }

    private static int GetBlockLength(Block block)
    {
        if (block is Paragraph p)
        {
            int length = GetParagraphTextLength(p);
            return length + 1; // +1 for paragraph break
        }
        else if (block is Section section)
        {
            int length = 0;
            foreach (var childBlock in section.Blocks)
            {
                length += GetBlockLength(childBlock);
            }
            return length;
        }
        else if (block is List list)
        {
            int length = 0;
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    length += GetBlockLength(itemBlock);
                }
            }
            return length;
        }
        else if (block is BlockUIContainer)
        {
            return 1;
        }

        return 0;
    }

    private static int GetParagraphTextLength(Paragraph p)
    {
        int length = 0;
        foreach (var inline in p.Inlines)
        {
            length += GetInlineLength(inline);
        }
        return length;
    }

    private static int GetInlineLength(Inline inline)
    {
        if (inline is Run run)
        {
            return run.Text.Length;
        }
        else if (inline is Span span)
        {
            int length = 0;
            foreach (var child in span.Inlines)
            {
                length += GetInlineLength(child);
            }
            return length;
        }
        else if (inline is LineBreak)
        {
            return 1;
        }
        else if (inline is InlineUIContainer)
        {
            return 1;
        }

        return 0;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the plain text content of the document.
    /// </summary>
    public string GetText()
    {
        var sb = new System.Text.StringBuilder();
        AppendBlocksText(Blocks, sb);
        return sb.ToString();
    }

    private void AppendBlocksText(IEnumerable<Block> blocks, System.Text.StringBuilder sb)
    {
        foreach (var block in blocks)
        {
            AppendBlockText(block, sb);
        }
    }

    private void AppendBlockText(Block block, System.Text.StringBuilder sb)
    {
        switch (block)
        {
            case Paragraph paragraph:
                AppendInlinesText(paragraph.Inlines, sb);
                sb.AppendLine();
                break;

            case Section section:
                AppendBlocksText(section.Blocks, sb);
                break;

            case List list:
                foreach (var item in list.ListItems)
                {
                    AppendBlocksText(item.Blocks, sb);
                }
                break;

            case Table table:
                foreach (var rowGroup in table.RowGroups)
                {
                    foreach (var row in rowGroup.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            AppendBlocksText(cell.Blocks, sb);
                            sb.Append('\t');
                        }
                        sb.AppendLine();
                    }
                }
                break;
        }
    }

    private void AppendInlinesText(IEnumerable<Inline> inlines, System.Text.StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            AppendInlineText(inline, sb);
        }
    }

    private void AppendInlineText(Inline inline, System.Text.StringBuilder sb)
    {
        switch (inline)
        {
            case Run run:
                sb.Append(run.Text);
                break;

            case Span span:
                AppendInlinesText(span.Inlines, sb);
                break;

            case LineBreak:
                sb.AppendLine();
                break;
        }
    }

    /// <summary>
    /// Creates a simple FlowDocument from plain text.
    /// </summary>
    public static FlowDocument FromText(string text)
    {
        var doc = new FlowDocument();

        if (string.IsNullOrEmpty(text))
            return doc;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            var paragraph = new Paragraph(new Run(line));
            doc.Blocks.Add(paragraph);
        }

        return doc;
    }

    #endregion
}
