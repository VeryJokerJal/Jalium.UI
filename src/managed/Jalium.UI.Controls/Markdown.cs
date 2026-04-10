using System.Diagnostics;
using System.Linq;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="Markdown.LinkClicked"/> event.
/// </summary>
public sealed class MarkdownLinkClickedEventArgs : EventArgs
{
    public MarkdownLinkClickedEventArgs(Uri uri)
    {
        Uri = uri;
    }

    /// <summary>
    /// Gets the resolved link URI.
    /// </summary>
    public Uri Uri { get; }

    /// <summary>
    /// Gets or sets whether the default link handling should be suppressed.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Displays Markdown content using a native parser and renderer.
/// </summary>
[ContentProperty(nameof(Text))]
public class Markdown : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.GenericAutomationPeer(this, Jalium.UI.Automation.AutomationControlType.Document);

    private static readonly HashSet<string> s_allowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http",
        "https",
        "ftp",
        "ftps",
        "mailto"
    };

    private Border? _container;
    private ScrollViewer? _scrollViewer;
    private StackPanel? _contentHost;
    private IReadOnlyList<MarkdownBlock> _blocks = Array.Empty<MarkdownBlock>();

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Markdown),
            new PropertyMetadata(string.Empty, OnMarkdownStructureChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BaseUriProperty =
        DependencyProperty.Register(nameof(BaseUri), typeof(Uri), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownStructureChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OpenLinksExternallyProperty =
        DependencyProperty.Register(nameof(OpenLinksExternally), typeof(bool), typeof(Markdown),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LinkForegroundProperty =
        DependencyProperty.Register(nameof(LinkForeground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CodeBackgroundProperty =
        DependencyProperty.Register(nameof(CodeBackground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CodeLineNumberForegroundProperty =
        DependencyProperty.Register(nameof(CodeLineNumberForeground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CodeGutterBackgroundProperty =
        DependencyProperty.Register(nameof(CodeGutterBackground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty QuoteBackgroundProperty =
        DependencyProperty.Register(nameof(QuoteBackground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty QuoteBorderBrushProperty =
        DependencyProperty.Register(nameof(QuoteBorderBrush), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HeadingSeparatorBrushProperty =
        DependencyProperty.Register(nameof(HeadingSeparatorBrush), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TableBorderBrushProperty =
        DependencyProperty.Register(nameof(TableBorderBrush), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TableHeaderBackgroundProperty =
        DependencyProperty.Register(nameof(TableHeaderBackground), typeof(Brush), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownVisualChanged));

    public Markdown()
    {
        Focusable = false;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        ParseMarkdown();
    }

    /// <summary>
    /// Gets or sets the Markdown source text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the base URI used to resolve relative links.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Uri? BaseUri
    {
        get => (Uri?)GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    /// <summary>
    /// Gets or sets whether absolute safe links should open with the OS shell by default.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool OpenLinksExternally
    {
        get => (bool)GetValue(OpenLinksExternallyProperty)!;
        set => SetValue(OpenLinksExternallyProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for links.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? LinkForeground
    {
        get => (Brush?)GetValue(LinkForegroundProperty);
        set => SetValue(LinkForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used behind inline and fenced code.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? CodeBackground
    {
        get => (Brush?)GetValue(CodeBackgroundProperty);
        set => SetValue(CodeBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for code block line numbers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? CodeLineNumberForeground
    {
        get => (Brush?)GetValue(CodeLineNumberForegroundProperty);
        set => SetValue(CodeLineNumberForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used behind the code block gutter.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? CodeGutterBackground
    {
        get => (Brush?)GetValue(CodeGutterBackgroundProperty);
        set => SetValue(CodeGutterBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used behind block quotes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? QuoteBackground
    {
        get => (Brush?)GetValue(QuoteBackgroundProperty);
        set => SetValue(QuoteBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the left border brush used for block quotes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? QuoteBorderBrush
    {
        get => (Brush?)GetValue(QuoteBorderBrushProperty);
        set => SetValue(QuoteBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for heading underline separators.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? HeadingSeparatorBrush
    {
        get => (Brush?)GetValue(HeadingSeparatorBrushProperty);
        set => SetValue(HeadingSeparatorBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush used by Markdown tables.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TableBorderBrush
    {
        get => (Brush?)GetValue(TableBorderBrushProperty);
        set => SetValue(TableBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush used by Markdown table headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public Brush? TableHeaderBackground
    {
        get => (Brush?)GetValue(TableHeaderBackgroundProperty);
        set => SetValue(TableHeaderBackgroundProperty, value);
    }

    /// <summary>
    /// Occurs when a rendered Markdown link is clicked.
    /// </summary>
    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    internal IReadOnlyList<MarkdownBlock> DebugBlocks => _blocks;

    protected override void OnApplyTemplate()
    {
        if (_container != null && _scrollViewer != null && ReferenceEquals(_container.Child, _scrollViewer))
        {
            _container.Child = null;
        }

        base.OnApplyTemplate();

        _container = GetTemplateChild("PART_Container") as Border;
        if (_container == null)
        {
            return;
        }

        _contentHost = new StackPanel
        {
            Orientation = Orientation.Vertical,
            TransitionProperty = "None"
        };

        _scrollViewer = new ScrollViewer
        {
            Content = _contentHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            TransitionProperty = "None",
            IsScrollInertiaEnabled = true,
            IsScrollBarAutoHideEnabled = false
        };

        _container.Child = _scrollViewer;
        RebuildVisualTree();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == ForegroundProperty ||
            e.Property == FontFamilyProperty ||
            e.Property == FontSizeProperty ||
            e.Property == LinkForegroundProperty ||
            e.Property == CodeBackgroundProperty ||
            e.Property == CodeLineNumberForegroundProperty ||
            e.Property == CodeGutterBackgroundProperty ||
            e.Property == QuoteBackgroundProperty ||
            e.Property == QuoteBorderBrushProperty ||
            e.Property == HeadingSeparatorBrushProperty ||
            e.Property == TableBorderBrushProperty ||
            e.Property == TableHeaderBackgroundProperty)
        {
            RebuildVisualTree();
        }
    }

    private static void OnMarkdownStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Markdown markdown)
        {
            return;
        }

        markdown.ParseMarkdown();
        markdown.RebuildVisualTree();
    }

    private static void OnMarkdownVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Markdown markdown)
        {
            markdown.RebuildVisualTree();
        }
    }

    private void ParseMarkdown()
    {
        _blocks = MarkdownParser.Parse(Text, BaseUri);
    }

    private void RebuildVisualTree()
    {
        if (_contentHost == null)
        {
            return;
        }

        _contentHost.Children.Clear();
        foreach (var block in _blocks)
        {
            _contentHost.Children.Add(CreateBlockElement(block, inListItem: false));
        }
    }

    private UIElement CreateBlockElement(MarkdownBlock block, bool inListItem)
    {
        return block switch
        {
            MarkdownHeadingBlock heading => CreateHeadingElement(heading),
            MarkdownParagraphBlock paragraph => CreateParagraphElement(paragraph, inListItem),
            MarkdownListBlock list => CreateListElement(list),
            MarkdownQuoteBlock quote => CreateQuoteElement(quote),
            MarkdownCodeBlock code => CreateCodeBlockElement(code),
            MarkdownRuleBlock => new Border
            {
                Height = 1,
                MinHeight = 1,
                MaxHeight = 1,
                Background = ResolveBrush(TableBorderBrush, "ControlBorder", new SolidColorBrush(Color.FromRgb(200, 200, 200))),
                Margin = new Thickness(0, 4, 0, 16)
            },
            MarkdownTableBlock table => CreateTableElement(table),
            _ => new TextBlock { Text = string.Empty }
        };
    }

    private UIElement CreateHeadingElement(MarkdownHeadingBlock heading)
    {
        var scale = heading.Level switch
        {
            1 => 2.0,
            2 => 1.65,
            3 => 1.4,
            4 => 1.2,
            5 => 1.05,
            _ => 1.0
        };

        var renderer = CreateTextRenderer(heading.Inlines);
        renderer.TextFontSize = FontSize * scale;
        renderer.DefaultFontWeight = FontWeights.Bold;
        renderer.LineHeightMultiplier = heading.Level <= 2 ? 1.0 : 1.3;

        if (heading.Level is not (1 or 2))
        {
            renderer.Margin = new Thickness(0, heading.Level == 1 ? 0 : 4, 0, 12);
            return renderer;
        }

        renderer.Margin = new Thickness(0, heading.Level == 1 ? 0 : 4, 0, 2);

        var host = new StackPanel
        {
            Orientation = Orientation.Vertical
        };
        host.Children.Add(renderer);
        host.Children.Add(new Border
        {
            Height = 1,
            MinHeight = 1,
            MaxHeight = 1,
            Background = ResolveBrush(HeadingSeparatorBrush, "TextSecondary", new SolidColorBrush(Color.FromRgb(160, 160, 160))),
            Margin = new Thickness(0, 0, 0, 0)
        });
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Child = host
        };
    }

    private UIElement CreateParagraphElement(MarkdownParagraphBlock paragraph, bool inListItem)
    {
        var renderer = CreateTextRenderer(paragraph.Inlines);
        renderer.Margin = new Thickness(0, 0, 0, inListItem ? 6 : 12);
        return renderer;
    }

    private UIElement CreateListElement(MarkdownListBlock list)
    {
        var host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 12)
        };

        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, index == list.Items.Count - 1 ? 0 : 6)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            var marker = CreateListMarker(list, item, index);
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var contentHost = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            foreach (var childBlock in item.Blocks)
            {
                contentHost.Children.Add(CreateBlockElement(childBlock, inListItem: true));
            }

            Grid.SetColumn(contentHost, 1);
            row.Children.Add(contentHost);
            host.Children.Add(row);
        }

        return host;
    }

    private UIElement CreateListMarker(MarkdownListBlock list, MarkdownListItemBlock item, int index)
    {
        var markerBrush = ResolveBrush(Foreground, "TextSecondary", new SolidColorBrush(Color.FromRgb(128, 128, 128)));
        var markerMargin = new Thickness(0, 2, 10, 0);

        if (item.TaskState is bool taskState)
        {
            return new TextBlock
            {
                Text = taskState ? "[x]" : "[ ]",
                Foreground = markerBrush,
                Margin = markerMargin,
                MinWidth = 28
            };
        }

        if (list.Ordered)
        {
            return new TextBlock
            {
                Text = $"{list.StartIndex + index}.",
                Foreground = markerBrush,
                Margin = markerMargin,
                MinWidth = 28
            };
        }

        return new Border
        {
            Width = 28,
            Height = Math.Max(16, FontSize * 1.5),
            Margin = markerMargin,
            Child = new Border
            {
                Width = 6,
                Height = 6,
                Background = markerBrush,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private UIElement CreateQuoteElement(MarkdownQuoteBlock quote)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        foreach (var block in quote.Blocks)
        {
            content.Children.Add(CreateBlockElement(block, inListItem: false));
        }

        return new Border
        {
            Background = ResolveBrush(QuoteBackground, "ControlBackground", new SolidColorBrush(Color.FromRgb(245, 245, 245))),
            BorderBrush = ResolveBrush(QuoteBorderBrush, "HyperlinkForeground", new SolidColorBrush(Color.FromRgb(0, 102, 204))),
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 10, 6),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };
    }

    private UIElement CreateCodeBlockElement(MarkdownCodeBlock code)
    {
        var codeView = new MarkdownCodeBlockView
        {
            Text = code.Text,
            Language = code.Language,
            CodeFontFamily = ResolveMonoFontFamily(),
            CodeFontSize = FontSize,
            ForegroundBrush = Foreground,
            LineNumberForegroundBrush = ResolveBrush(CodeLineNumberForeground, "TextSecondary", new SolidColorBrush(Color.FromRgb(128, 128, 128))),
            GutterBackgroundBrush = ResolveBrush(CodeGutterBackground, "ControlBackground", new SolidColorBrush(Color.FromRgb(32, 32, 32)))
        };

        return new Border
        {
            Background = ResolveBrush(CodeBackground, "ControlBackground", new SolidColorBrush(Color.FromRgb(245, 245, 245))),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 0, 12),
            Child = codeView
        };
    }

    private UIElement CreateTableElement(MarkdownTableBlock table)
    {
        var rowCount = table.HeaderRows.Count + table.Rows.Count;
        var columnCount = Math.Max(
            table.HeaderRows.Count == 0 ? 0 : table.HeaderRows.Max(static row => row.Cells.Count),
            table.Rows.Count == 0 ? 0 : table.Rows.Max(static row => row.Cells.Count));

        var grid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 12)
        };

        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        for (var row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var currentRow = 0;
        foreach (var headerRow in table.HeaderRows)
        {
            AddTableRow(grid, headerRow, currentRow++, isHeader: true);
        }

        foreach (var bodyRow in table.Rows)
        {
            AddTableRow(grid, bodyRow, currentRow++, isHeader: false);
        }

        return grid;
    }

    private void AddTableRow(Grid grid, MarkdownTableRow row, int rowIndex, bool isHeader)
    {
        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
        {
            var renderer = CreateTextRenderer(row.Cells[columnIndex]);
            renderer.DefaultFontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal;
            renderer.Margin = new Thickness(0);

            var cell = new Border
            {
                Background = isHeader
                    ? ResolveBrush(TableHeaderBackground, "ControlBackground", new SolidColorBrush(Color.FromRgb(245, 245, 245)))
                    : null,
                BorderBrush = ResolveBrush(TableBorderBrush, "ControlBorder", new SolidColorBrush(Color.FromRgb(200, 200, 200))),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                Child = renderer
            };

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, columnIndex);
            grid.Children.Add(cell);
        }
    }

    private MarkdownTextRenderer CreateTextRenderer(IReadOnlyList<MarkdownInline> inlines)
    {
        var renderer = new MarkdownTextRenderer
        {
            Spans = FlattenInlines(inlines),
            TextFontFamily = ResolveBodyFontFamily(),
            MonoFontFamily = ResolveMonoFontFamily(),
            TextFontSize = FontSize,
            DefaultFontWeight = FontWeights.Normal,
            DefaultFontStyle = FontStyles.Normal,
            ForegroundBrush = Foreground,
            LinkForegroundBrush = ResolveBrush(LinkForeground, "HyperlinkForeground", new SolidColorBrush(Color.FromRgb(0, 102, 204))),
            CodeBackgroundBrush = ResolveBrush(CodeBackground, "ControlBackground", new SolidColorBrush(Color.FromRgb(245, 245, 245))),
            Wrap = true,
            PreserveWhitespace = false,
            LineHeightMultiplier = 1.5
        };

        renderer.LinkClicked += OnInlineLinkClicked;
        return renderer;
    }

    private void OnInlineLinkClicked(object? sender, MarkdownLinkClickedEventArgs e)
    {
        LinkClicked?.Invoke(this, e);
        if (e.Handled || !OpenLinksExternally || !e.Uri.IsAbsoluteUri || !s_allowedSchemes.Contains(e.Uri.Scheme))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ignore navigation failures.
        }
    }

    private IReadOnlyList<MarkdownTextSpan> FlattenInlines(IReadOnlyList<MarkdownInline> inlines)
    {
        var spans = new List<MarkdownTextSpan>();
        AppendInlineSpans(spans, inlines, new MarkdownTextStyle(Bold: false, Italic: false, Code: false, LinkUri: null));
        return spans;
    }

    private static void AppendInlineSpans(List<MarkdownTextSpan> spans, IEnumerable<MarkdownInline> inlines, MarkdownTextStyle style)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    spans.Add(new MarkdownTextSpan(text.Text, style));
                    break;

                case MarkdownStrongInline strong:
                    AppendInlineSpans(spans, strong.Children, style with { Bold = true });
                    break;

                case MarkdownEmphasisInline emphasis:
                    AppendInlineSpans(spans, emphasis.Children, style with { Italic = true });
                    break;

                case MarkdownCodeInline code:
                    spans.Add(new MarkdownTextSpan(code.Text, style with { Code = true }));
                    break;

                case MarkdownLinkInline link:
                    AppendInlineSpans(spans, link.Children, style with { LinkUri = link.Uri });
                    break;

                case MarkdownLineBreakInline:
                    spans.Add(new MarkdownTextSpan(string.Empty, style, IsLineBreak: true));
                    break;
            }
        }
    }

    private Brush ResolveBrush(Brush? preferred, object resourceKey, Brush fallback)
    {
        return preferred ?? TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private string ResolveBodyFontFamily() =>
        string.IsNullOrWhiteSpace(FontFamily)
            ? (TryFindResource("BodyFontFamily") as string ?? FrameworkElement.DefaultFontFamilyName)
            : FontFamily;

    private string ResolveMonoFontFamily() =>
        TryFindResource("MonoFontFamily") as string ?? "Cascadia Code";
}
