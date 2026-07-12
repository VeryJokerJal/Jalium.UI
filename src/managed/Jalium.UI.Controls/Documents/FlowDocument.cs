using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a flow document that hosts rich flow content.
/// </summary>
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
        {
            throw new ArgumentException("The TextPointer belongs to a different document.", nameof(position));
        }

        var paragraph = position.Paragraph
            ?? throw new InvalidOperationException("A paragraph break can only be inserted inside a Paragraph.");
        var siblings = paragraph.OwnerCollection
            ?? throw new InvalidOperationException("The Paragraph is not attached to a BlockCollection.");
        var newParagraph = new Paragraph();
        CopyLocalValues(paragraph, newParagraph);

        if (position.Parent is Run run && ReferenceEquals(run.Parent, paragraph) && run.OwnerCollection is { } inlines)
        {
            var runIndex = inlines.IndexOf(run);
            var split = Math.Clamp(position.Offset, 0, run.Text.Length);
            var trailingText = run.Text[split..];
            run.Text = run.Text[..split];
            if (trailingText.Length != 0)
            {
                var trailingRun = new Run(trailingText);
                CopyLocalValues(run, trailingRun);
                trailingRun.Text = trailingText;
                newParagraph.Inlines.Add(trailingRun);
            }

            while (inlines.Count > runIndex + 1)
            {
                var trailing = inlines[runIndex + 1];
                inlines.RemoveAt(runIndex + 1);
                newParagraph.Inlines.Add(trailing);
            }
        }
        else
        {
            var text = paragraph.Inlines.GetText();
            var paragraphStart = GetDocumentOffset(paragraph, 0);
            var split = Math.Clamp(position.DocumentOffset - paragraphStart, 0, text.Length);
            paragraph.Inlines.Clear();
            if (split != 0)
            {
                paragraph.Inlines.Add(new Run(text[..split]));
            }
            if (split != text.Length)
            {
                newParagraph.Inlines.Add(new Run(text[split..]));
            }
        }

        siblings.Insert(siblings.IndexOf(paragraph) + 1, newParagraph);
        return newParagraph.ContentStart;
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

            if (current is Paragraph)
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

    private static void CopyLocalValues(DependencyObject source, DependencyObject destination)
    {
        var values = source.GetLocalValueEnumerator();
        while (values.MoveNext())
        {
            var entry = values.Current;
            destination.SetValue(entry.Property, entry.Value);
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
}
