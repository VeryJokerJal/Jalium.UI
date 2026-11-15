using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a flow document that hosts rich flow content.
/// </summary>
[ContentProperty(nameof(Blocks))]
public partial class FlowDocument : FrameworkContentElement, IServiceProvider, IDocumentPaginatorSource, IAddChild
{
    private Typography? _typography;
    private double _pixelsPerDip = 1.0;

    #region Dependency Properties

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(FlowDocument),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits,
                OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(14.0, OnViewerPaginationPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(null, OnViewerPaginationPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        TextElement.BackgroundProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(null, OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the PageWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageWidthProperty =
        DependencyProperty.Register(nameof(PageWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN, OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the PageHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageHeightProperty =
        DependencyProperty.Register(nameof(PageHeight), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN, OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the PagePadding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PagePaddingProperty =
        DependencyProperty.Register(nameof(PagePadding), typeof(Thickness), typeof(FlowDocument),
            new PropertyMetadata(new Thickness(0), OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the ColumnWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnWidthProperty =
        DependencyProperty.Register(nameof(ColumnWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.NaN, OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the ColumnGap dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(nameof(ColumnGap), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(10.0, OnViewerPaginationPropertyChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        Block.TextAlignmentProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(TextAlignment.Left, OnViewerPaginationPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the LineHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightProperty =
        Block.LineHeightProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(double.NaN, OnViewerPaginationPropertyChanged, null, inherits: true));

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
        Block.IsHyphenationEnabledProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(false, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(FontStyles.Normal, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(FontWeights.Normal, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty FontStretchProperty =
        TextElement.FontStretchProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(FontStretches.Normal, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty FlowDirectionProperty =
        FrameworkElement.FlowDirectionProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(FlowDirection.LeftToRight, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty TextEffectsProperty =
        TextElement.TextEffectsProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(null, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty LineStackingStrategyProperty =
        Block.LineStackingStrategyProperty.AddOwner(typeof(FlowDocument),
            new PropertyMetadata(LineStackingStrategy.MaxHeight, OnViewerPaginationPropertyChanged, null, inherits: true));

    public static readonly DependencyProperty IsColumnWidthFlexibleProperty =
        DependencyProperty.Register(nameof(IsColumnWidthFlexible), typeof(bool), typeof(FlowDocument),
            new PropertyMetadata(true, OnViewerPaginationPropertyChanged));

    public static readonly DependencyProperty ColumnRuleWidthProperty =
        DependencyProperty.Register(nameof(ColumnRuleWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(0.0, OnViewerPaginationPropertyChanged), IsValidNonNegativeFinite);

    public static readonly DependencyProperty ColumnRuleBrushProperty =
        DependencyProperty.Register(nameof(ColumnRuleBrush), typeof(Brush), typeof(FlowDocument),
            new PropertyMetadata(null, OnViewerPaginationPropertyChanged));

    public static readonly DependencyProperty MinPageWidthProperty =
        DependencyProperty.Register(nameof(MinPageWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(0.0, OnViewerPaginationPropertyChanged), IsValidNonNegativeFinite);

    public static readonly DependencyProperty MaxPageWidthProperty =
        DependencyProperty.Register(nameof(MaxPageWidth), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.PositiveInfinity, OnViewerPaginationPropertyChanged), IsValidNonNegative);

    public static readonly DependencyProperty MinPageHeightProperty =
        DependencyProperty.Register(nameof(MinPageHeight), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(0.0, OnViewerPaginationPropertyChanged), IsValidNonNegativeFinite);

    public static readonly DependencyProperty MaxPageHeightProperty =
        DependencyProperty.Register(nameof(MaxPageHeight), typeof(double), typeof(FlowDocument),
            new PropertyMetadata(double.PositiveInfinity, OnViewerPaginationPropertyChanged), IsValidNonNegative);

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
    public FontFamily FontFamily
    {
        get => (FontFamily)(GetValue(FontFamilyProperty) ?? SystemFonts.MessageFontFamily);
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

    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle style ? style : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight weight ? weight : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch stretch ? stretch : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
    }

    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    public TextEffectCollection TextEffects
    {
        get
        {
            if (GetValue(TextEffectsProperty) is TextEffectCollection effects)
            {
                return effects;
            }

            effects = [];
            SetValue(TextEffectsProperty, effects);
            return effects;
        }
        set => SetValue(TextEffectsProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)(GetValue(LineStackingStrategyProperty) ?? LineStackingStrategy.MaxHeight);
        set => SetValue(LineStackingStrategyProperty, value);
    }

    public bool IsColumnWidthFlexible
    {
        get => (bool)(GetValue(IsColumnWidthFlexibleProperty) ?? true);
        set => SetValue(IsColumnWidthFlexibleProperty, value);
    }

    public double ColumnRuleWidth
    {
        get => (double)(GetValue(ColumnRuleWidthProperty) ?? 0.0);
        set => SetValue(ColumnRuleWidthProperty, value);
    }

    public Brush? ColumnRuleBrush
    {
        get => (Brush?)GetValue(ColumnRuleBrushProperty);
        set => SetValue(ColumnRuleBrushProperty, value);
    }

    public double MinPageWidth
    {
        get => (double)(GetValue(MinPageWidthProperty) ?? 0.0);
        set => SetValue(MinPageWidthProperty, value);
    }

    public double MaxPageWidth
    {
        get => (double)(GetValue(MaxPageWidthProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxPageWidthProperty, value);
    }

    public double MinPageHeight
    {
        get => (double)(GetValue(MinPageHeightProperty) ?? 0.0);
        set => SetValue(MinPageHeightProperty, value);
    }

    public double MaxPageHeight
    {
        get => (double)(GetValue(MaxPageHeightProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxPageHeightProperty, value);
    }

    public Typography Typography => _typography ??= new Typography(this);

    protected internal override IEnumerator LogicalChildren => Blocks.GetEnumerator();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="FlowDocument"/> class.
    /// </summary>
    public FlowDocument()
    {
        Blocks = new BlockCollection(this);
        InitializeViewerPagination();
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
            {
                // At a shared Run boundary, a forward insertion belongs to
                // the following Run, even when it is inside a nested Span.
                if (direction == LogicalDirection.Forward && result.Parent is Run run && result.Offset == run.Text.Length)
                {
                    var following = GetPositionAtOffset(offset + 1, LogicalDirection.Backward);
                    if (following?.Parent is Run next && GetDocumentOffset(next, 0) == offset)
                        return new TextPointer(this, next, 0, direction);
                }
                return result;
            }
        }

        // The position after the final paragraph is part of the plain-text
        // range. Parenting it to a Paragraph would clamp away its final break.
        if (offset == currentOffset)
            return new TextPointer(this, null, offset, direction);

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
        else if (block is Table table)
        {
            foreach (var row in table.RowGroups.SelectMany(group => group.Rows))
            {
                foreach (var cell in row.Cells)
                {
                    foreach (var child in cell.Blocks)
                    {
                        var found = GetPositionInBlock(child, targetOffset, direction, ref currentOffset);
                        if (found is not null) return found;
                    }
                    if (targetOffset == currentOffset) return new TextPointer(this, cell, TextElement.GetContentLength(cell), direction);
                    currentOffset++; // Cell separator in GetText().
                }
                if (targetOffset == currentOffset) return new TextPointer(this, null, currentOffset, direction);
                currentOffset++; // Row separator in GetText().
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

    internal int GetDocumentOffset(TextElement? element, int relativeOffset)
    {
        if (element is null)
        {
            return Math.Clamp(relativeOffset, 0, GetDocumentLength());
        }

        var cursor = 0;
        foreach (var block in Blocks)
        {
            if (TryGetElementOffset(block, element, relativeOffset, ref cursor, out var result))
            {
                return result;
            }
        }

        return cursor;
    }

    internal IEnumerable<Paragraph> EnumerateParagraphs()
    {
        foreach (var block in Blocks)
        {
            foreach (var paragraph in EnumerateParagraphs(block))
            {
                yield return paragraph;
            }
        }
    }

    internal TextPointer InsertParagraphBreak(TextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(position.Document, this))
            throw new ArgumentException("The TextPointer belongs to a different document.", nameof(position));
        using var change = (Parent as Controls.RichTextBox)?.DeclareChangeBlock();
        return DocumentInsertion.SplitParagraph(position);
    }

    private static bool TryGetElementOffset(
        TextElement current,
        TextElement target,
        int relativeOffset,
        ref int cursor,
        out int result)
    {
        if (ReferenceEquals(current, target))
        {
            result = cursor + Math.Clamp(relativeOffset, 0, TextElement.GetContentLength(current));
            return true;
        }

        IEnumerable<TextElement>? children = current switch
        {
            Paragraph paragraph => paragraph.Inlines,
            Span span => span.Inlines,
            Section section => section.Blocks,
            List list => list.ListItems,
            ListItem item => item.Blocks,
            Table table => table.RowGroups,
            TableRowGroup group => group.Rows,
            TableRow row => row.Cells,
            TableCell cell => cell.Blocks,
            _ => null,
        };

        if (children is not null)
        {
            foreach (var child in children)
            {
                if (TryGetElementOffset(child, target, relativeOffset, ref cursor, out result))
                {
                    return true;
                }
            }

            if (current is Paragraph or TableCell or TableRow)
            {
                cursor++;
            }
        }
        else
        {
            cursor += TextElement.GetContentLength(current);
        }

        result = -1;
        return false;
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(TextElement element)
    {
        if (element is Paragraph paragraph)
        {
            yield return paragraph;
            yield break;
        }

        IEnumerable<TextElement>? children = element switch
        {
            Section section => section.Blocks,
            List list => list.ListItems,
            ListItem item => item.Blocks,
            Table table => table.RowGroups,
            TableRowGroup group => group.Rows,
            TableRow row => row.Cells,
            TableCell cell => cell.Blocks,
            _ => null,
        };
        if (children is null)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var childParagraph in EnumerateParagraphs(child))
            {
                yield return childParagraph;
            }
        }
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

    internal static int GetBlockLength(Block block)
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
        else if (block is Table table)
        {
            return table.RowGroups.Sum(group => group.Rows.Sum(row => row.Cells.Sum(cell => cell.Blocks.Sum(GetBlockLength) + 1) + 1));
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
    /// Gets the plain text content of the document, using one LF per structural break.
    /// Authored Run text is preserved verbatim so offsets match TextPointer on every platform.
    /// Embedded UI elements occupy one U+FFFC object replacement character; their child text is not flattened.
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
                sb.Append('\n');
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
                        sb.Append('\n');
                    }
                }
                break;

            case BlockUIContainer:
                sb.Append('\uFFFC');
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
                sb.Append('\n');
                break;

            case InlineUIContainer:
                sb.Append('\uFFFC');
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

    /// <summary>Updates the text formatting scale used by document layout.</summary>
    public void SetDpi(DpiScale dpiInfo)
    {
        if (!double.IsFinite(dpiInfo.PixelsPerDip) || dpiInfo.PixelsPerDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpiInfo));
        }

        if (_pixelsPerDip != dpiInfo.PixelsPerDip)
        {
            _pixelsPerDip = dpiInfo.PixelsPerDip;
            _viewerPaginator?.InvalidatePagination();
            ViewerPaginationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    object? IServiceProvider.GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IDocumentPaginatorSource) || serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceType == typeof(DocumentPaginator))
        {
            return ((IDocumentPaginatorSource)this).DocumentPaginator;
        }

        return null;
    }

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not Block block)
        {
            throw new ArgumentException("FlowDocument children must be Block elements.", nameof(value));
        }

        Blocks.Add(block);
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != 0)
        {
            Blocks.Add(new Paragraph(new Run(text)));
        }
    }

    internal double PixelsPerDip => _pixelsPerDip;

    private static bool IsValidNonNegativeFinite(object? value) =>
        value is double number && double.IsFinite(number) && number >= 0;

    private static bool IsValidNonNegative(object? value) =>
        value is double number && !double.IsNaN(number) && number >= 0;

    #endregion

    #region Pagination

    private FlowDocumentViewerPaginator? _viewerPaginator;

    internal DocumentPaginator ViewerPaginator =>
        _viewerPaginator ??= new FlowDocumentViewerPaginator(this);

    DocumentPaginator IDocumentPaginatorSource.DocumentPaginator => ViewerPaginator;

    internal event EventHandler? ViewerPaginationChanged;
    internal event EventHandler? ContentChanged;

    internal override void OnEffectiveValueSourceChanged(DependencyProperty property)
    {
        base.OnEffectiveValueSourceChanged(property);
        if (property == TextAlignmentProperty || property == ColumnWidthProperty || property == ColumnGapProperty ||
            property == ColumnRuleWidthProperty || property == ColumnRuleBrushProperty || property == IsColumnWidthFlexibleProperty || IsRootBoxProperty(property))
        {
            Styling.CssEvaluationScheduler.InvalidateSubtree(Styling.CssNode.Get(this));
            NotifyPaginationStyleChanged();
        }
    }

    private static bool IsRootBoxProperty(DependencyProperty property) => property == FrameworkElement.WidthProperty ||
        property == FrameworkElement.MinWidthProperty || property == FrameworkElement.MaxWidthProperty || property == FrameworkElement.HeightProperty ||
        property == FrameworkElement.MinHeightProperty || property == FrameworkElement.MaxHeightProperty || property == FrameworkElement.MarginProperty ||
        property == Control.PaddingProperty || property == Control.BorderThicknessProperty || property == Control.BorderBrushProperty ||
        property == Styling.CssBorderUsedThicknessProperties.ValueProperty;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (IsRootBoxProperty(change.Property) ||
            change.Property == Styling.Css.MediaTypeProperty ||
            change.Property == Styling.Css.StyleSheetsProperty)
        {
            NotifyPaginationStyleChanged();
        }
    }

    internal void NotifyPaginationStyleChanged()
    {
        _viewerPaginator?.InvalidatePagination();
        try { ContentChanged?.Invoke(this, EventArgs.Empty); }
        finally { ViewerPaginationChanged?.Invoke(this, EventArgs.Empty); }
    }

    internal void NotifyTextPresentationChanged()
        => ViewerPaginationChanged?.Invoke(this, EventArgs.Empty);

    private void InitializeViewerPagination()
    {
        Blocks.Changed += OnBlocksChanged;
    }

    private void OnBlocksChanged(object? sender, EventArgs e)
    {
        _viewerPaginator?.InvalidatePagination();
        try { ContentChanged?.Invoke(this, EventArgs.Empty); }
        finally { ViewerPaginationChanged?.Invoke(this, EventArgs.Empty); }
    }

    private static void OnViewerPaginationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocument document)
        {
            document._viewerPaginator?.InvalidatePagination();
            try
            {
                // Page constraints belong to the viewer. Text formatting is
                // editable document content, including formatting on the root.
                if (e.Property == FontFamilyProperty || e.Property == FontSizeProperty ||
                    e.Property == FontStyleProperty || e.Property == FontWeightProperty ||
                    e.Property == FontStretchProperty || e.Property == ForegroundProperty ||
                    e.Property == BackgroundProperty || e.Property == TextAlignmentProperty ||
                    e.Property == LineHeightProperty || e.Property == FlowDirectionProperty ||
                    e.Property == TextElement.TextDecorationsProperty || e.Property == LineStackingStrategyProperty ||
                    e.Property == IsHyphenationEnabledProperty)
                    document.ContentChanged?.Invoke(document, EventArgs.Empty);
            }
            finally { document.ViewerPaginationChanged?.Invoke(document, EventArgs.Empty); }
        }
    }

    private sealed class FlowDocumentViewerPaginator : DocumentPaginator, IFlowDocumentPageMetrics, IFlowDocumentPagePresentation, IDocumentTextSearchProvider
    {
        private readonly FlowDocument _document;
        private Size _pageSize = new(816.0, 1056.0);
        private FlowDocumentPresentation? _layout;
        private List<PageSlice>? _pages;
        private Size _layoutPageSize;
        private long _layoutFontEpoch = -1;
        private bool _buildingPages;

        public FlowDocumentViewerPaginator(FlowDocument document)
        {
            _document = document;
        }

        public override int PageCount => BuildPages().Count;

        public override bool IsPageCountValid => !_buildingPages;

        public override IDocumentPaginatorSource Source => _document;

        public override Size PageSize
        {
            get => ResolvePageSize();
            set
            {
                if (value.IsEmpty || !double.IsFinite(value.Width) || !double.IsFinite(value.Height) ||
                    value.Width <= 0 || value.Height <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                if (_pageSize != value)
                {
                    _pageSize = value;
                    InvalidatePagination();
                    _document.ViewerPaginationChanged?.Invoke(_document, EventArgs.Empty);
                }
            }
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            var pages = BuildPages();
            if (pageNumber < 0 || pageNumber >= pages.Count)
            {
                return DocumentPage.Missing;
            }

            var page = pages[pageNumber];
            var pageSize = page.PageSize;
            var visual = new FlowDocumentPresentation();
            visual.SetDocument(_document);
            visual.SetPage(
                pageSize,
                page.Top,
                page.Bottom,
                pageNumber,
                pages.Count,
                page.Blank,
                page.PageStyle,
                page.RepeatedHeaderHeight,
                page.RepeatedFooterHeight,
                page.RepeatTableTop);
            visual.Measure(pageSize);
            visual.Arrange(new Rect(pageSize));
            return new DocumentPage(visual, pageSize, new Rect(pageSize), new Rect(pageSize));
        }

        public int GetPageNumberForOffset(int offset)
        {
            var pages = BuildPages();
            if (pages.Count == 0)
            {
                return 0;
            }

            var normalized = Math.Max(0, offset);
            if (_layout is { } layout && (layout.HasTables || layout.HasColumns))
            {
                var top = layout.TopForOffset(normalized);
                for (var index = 0; index < pages.Count; index++)
                    if (pages[index].Bottom > pages[index].Top && top < pages[index].Bottom - .01) return index + 1;
                return pages.Count;
            }
            for (var index = 0; index < pages.Count; index++)
            {
                if (normalized < pages[index].StartOffset + pages[index].Length || index == pages.Count - 1)
                {
                    return index + 1;
                }
            }

            return pages.Count;
        }

        public IEnumerable<TextSearchResult> Find(string text, bool matchCase, bool matchWholeWord)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (text.Length == 0) return [];
            var source = _document.GetText();
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var matches = new List<(int Offset, int Page)>();
            for (int offset = 0; offset <= source.Length - text.Length;)
            {
                int found = source.IndexOf(text, offset, comparison);
                if (found < 0) break;
                int end = found + text.Length;
                if (!matchWholeWord || !WordAt(found - 1) && !WordAt(end))
                    matches.Add((found, GetPageNumberForOffset(found)));
                offset = end;
            }

            var results = new List<TextSearchResult>();
            foreach (var group in matches.GroupBy(match => match.Page))
            {
                if (group.Key <= 0 || GetPage(group.Key - 1).Visual is not FlowDocumentPresentation view) continue;
                using (view)
                {
                    foreach (var match in group)
                    {
                        var bounds = Rect.Empty;
                        foreach (var rectangle in view.TextBounds(match.Offset, text.Length))
                            bounds = bounds.IsEmpty ? rectangle : Rect.Union(bounds, rectangle);
                        results.Add(new TextSearchResult(group.Key, bounds, source.Substring(match.Offset, text.Length)));
                    }
                }
            }
            return results;

            bool WordAt(int index)
            {
                if (index < 0 || index >= source.Length) return false;
                if (char.IsLowSurrogate(source[index]) && index > 0 && char.IsHighSurrogate(source[index - 1])) index--;
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(source, index);
                return char.IsLetterOrDigit(source, index) || category is
                    System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or
                    System.Globalization.UnicodeCategory.EnclosingMark or System.Globalization.UnicodeCategory.ConnectorPunctuation;
            }
        }

        public bool TryRefreshPage(int pageNumber, FlowDocumentPresentation visual, out Size size)
        {
            var pages = BuildPages();
            if (!ReferenceEquals(visual.Document, _document) || pageNumber < 0 || pageNumber >= pages.Count)
            {
                size = ResolvePageSize();
                return false;
            }
            var page = pages[pageNumber];
            size = page.PageSize;
            visual.SetPage(
                size,
                page.Top,
                page.Bottom,
                pageNumber,
                pages.Count,
                page.Blank,
                page.PageStyle,
                page.RepeatedHeaderHeight,
                page.RepeatedFooterHeight,
                page.RepeatTableTop);
            return true;
        }

        public int GetPageStartOffset(int oneBasedPageNumber)
        {
            var pages = BuildPages();
            if (pages.Count == 0)
            {
                return 0;
            }

            var index = Math.Clamp(oneBasedPageNumber - 1, 0, pages.Count - 1);
            return pages[index].StartOffset;
        }

        public void InvalidatePagination()
        {
            _pages = null;
            _layout?.DocumentChanged();
        }

        private Styling.CssLengthContext CreatePageLengthContext(Size referenceSize)
        {
            var fontSize = double.IsFinite(_document.FontSize) && _document.FontSize > 0 ? _document.FontSize : 14.0;
            return new Styling.CssLengthContext(
                elementFontSize: fontSize,
                inheritedFontSize: fontSize,
                rootFontSize: fontSize,
                viewportWidth: referenceSize.Width > 0 && double.IsFinite(referenceSize.Width) ? referenceSize.Width : 816.0,
                viewportHeight: referenceSize.Height > 0 && double.IsFinite(referenceSize.Height) ? referenceSize.Height : 1056.0);
        }

        private Styling.CssComputedPageStyle ResolveCssPageStyle(
            int pageIndex,
            int pageCount,
            string? pageName,
            bool isBlank,
            Size defaultSize)
        {
            var rtl = _document.FlowDirection == FlowDirection.RightToLeft;
            var firstRight = !rtl;
            var isRight = firstRight != ((pageIndex & 1) != 0);
            var isLeft = !isRight;
            var lengths = CreatePageLengthContext(defaultSize);
            return Styling.CssPageRules.ResolvePageStyle(
                _document,
                pageIndex,
                pageCount,
                pageName,
                isLeft,
                isBlank,
                defaultSize,
                _document.PagePadding,
                lengths);
        }

        private List<PageSlice> BuildPages()
        {
            // Computed-style publication can synchronously notify viewers. Let
            // those queries lay out the live document before entering the page
            // construction guard, rather than handing them a temporary one-page
            // result that a DocumentPageView could cache as Missing.
            Jalium.UI.Styling.CssEvaluationScheduler.FlushIfPending(_document.Dispatcher);
            if (_buildingPages) return _pages ?? [new(0, 0, 0, 0, false, null, ResolveBasePageSize(), null, 0, 0, double.NaN)];
            _buildingPages = true;
            try
            {
                var size = ResolvePageSize();
                var epoch = TextMeasurement.FontCacheEpoch;
                if (_pages is not null && _layoutPageSize == size && _layoutFontEpoch == epoch) return _pages;
                _layout ??= new FlowDocumentPresentation(observe: false);
                _layout.SetDocument(_document);
                var rootPageName = Styling.CssPageProperties.Page(_document);
                if (string.IsNullOrEmpty(rootPageName)) rootPageName = null;
                var firstPageStyle = ResolveCssPageStyle(0, 1, rootPageName, isBlank: false, size);
                var padding = firstPageStyle.HasMatchedRules ? firstPageStyle.ContentInsets : _document.PagePadding;
                var width = Math.Max(1, size.Width - padding.Left - padding.Right);
                var height = Math.Max(1, size.Height - padding.Top - padding.Bottom);
                _layout.SetPaginationColumnHeight(height);
                _layout.EnsureDocumentLayout(width);
                var rawPages = new List<PageSlice>();
                var textLength = _document.GetText().Length;
                if (_layout.HasColumns)
                {
                    foreach (var columnPage in _layout.ColumnPages)
                    {
                        var offset = columnPage.Blank ? (rawPages.Count > 0 ? rawPages[^1].StartOffset + rawPages[^1].Length : 0)
                            : _layout.FirstOffsetInPage(columnPage.Top, columnPage.Bottom);
                        rawPages.Add(new(offset, 0, columnPage.Top, columnPage.Bottom, columnPage.Blank, rootPageName, size, null, 0, 0, double.NaN));
                    }
                }
                else
                {
                    foreach (var fragment in FlowDocumentPagination.Build(_layout, height, _document.FlowDirection))
                    {
                        if (_layout.UsesVisualPagination)
                        {
                            var anchor = fragment.Blank
                                ? (rawPages.Count > 0 ? rawPages[^1].StartOffset + rawPages[^1].Length : 0)
                                : _layout.FirstOffsetInPage(fragment.Top, fragment.Bottom);
                            rawPages.Add(new(anchor, 0, fragment.Top, fragment.Bottom, fragment.Blank, fragment.PageName, size, null,
                                fragment.RepeatedHeaderHeight, fragment.RepeatedFooterHeight, fragment.RepeatTableTop));
                            continue;
                        }
                        var start = fragment.Blank
                            ? (rawPages.Count > 0 ? rawPages[^1].StartOffset + rawPages[^1].Length : 0)
                            : fragment.FirstLine == 0 ? 0 : _layout.Lines[fragment.FirstLine].Start;
                        var end = fragment.Blank ? start : fragment.EndLine < _layout.Lines.Count ? _layout.Lines[fragment.EndLine].Start : textLength;
                        rawPages.Add(new(start, Math.Max(0, end - start), fragment.Top, fragment.Bottom, fragment.Blank, fragment.PageName, size, null,
                            fragment.RepeatedHeaderHeight, fragment.RepeatedFooterHeight, fragment.RepeatTableTop));
                    }
                }

                var totalCount = Math.Max(1, rawPages.Count);
                var baseSize = ResolveBasePageSize();
                var pages = new List<PageSlice>(rawPages.Count);
                for (var i = 0; i < rawPages.Count; i++)
                {
                    var item = rawPages[i];
                    var style = ResolveCssPageStyle(i, totalCount, item.PageName ?? rootPageName, item.Blank, baseSize);
                    var pageSize = ResolvePageSizeForStyle(style, baseSize);
                    pages.Add(item with
                    {
                        PageSize = pageSize,
                        PageStyle = style.HasMatchedRules ? style : null,
                    });
                }

                _layoutPageSize = size;
                _layoutFontEpoch = epoch;
                return _pages = pages;
            }
            finally { _buildingPages = false; }
        }

        private Size ResolveBasePageSize()
        {
            var width = double.IsFinite(_document.PageWidth) && _document.PageWidth > 0
                ? _document.PageWidth
                : _pageSize.Width;
            var height = double.IsFinite(_document.PageHeight) && _document.PageHeight > 0
                ? _document.PageHeight
                : _pageSize.Height;
            width = Math.Max(_document.MinPageWidth, Math.Min(_document.MaxPageWidth, width));
            height = Math.Max(_document.MinPageHeight, Math.Min(_document.MaxPageHeight, height));
            return new Size(width, height);
        }

        private Size ResolvePageSizeForStyle(Styling.CssComputedPageStyle style, Size baseSize)
        {
            var width = double.IsFinite(_document.PageWidth) && _document.PageWidth > 0
                ? _document.PageWidth
                : style.HasMatchedRules && style.Size.Width > 0 ? style.Size.Width : baseSize.Width;
            var height = double.IsFinite(_document.PageHeight) && _document.PageHeight > 0
                ? _document.PageHeight
                : style.HasMatchedRules && style.Size.Height > 0 ? style.Size.Height : baseSize.Height;
            width = Math.Max(_document.MinPageWidth, Math.Min(_document.MaxPageWidth, width));
            height = Math.Max(_document.MinPageHeight, Math.Min(_document.MaxPageHeight, height));
            return new Size(width, height);
        }

        private Size ResolvePageSize()
        {
            var baseSize = ResolveBasePageSize();
            var rootPageName = Styling.CssPageProperties.Page(_document);
            if (string.IsNullOrEmpty(rootPageName)) rootPageName = null;
            var style = ResolveCssPageStyle(0, 1, rootPageName, isBlank: false, baseSize);
            return ResolvePageSizeForStyle(style, baseSize);
        }

        private readonly record struct PageSlice(
            int StartOffset,
            int Length,
            double Top,
            double Bottom,
            bool Blank,
            string? PageName,
            Size PageSize,
            Styling.CssComputedPageStyle? PageStyle,
            double RepeatedHeaderHeight,
            double RepeatedFooterHeight,
            double RepeatTableTop);
    }

    #endregion
}

internal interface IFlowDocumentPageMetrics
{
    int GetPageNumberForOffset(int offset);
    int GetPageStartOffset(int oneBasedPageNumber);
}

internal interface IFlowDocumentPagePresentation
{
    bool TryRefreshPage(int pageNumber, FlowDocumentPresentation visual, out Size size);
}
