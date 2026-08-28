using System.Diagnostics;
using System.Linq;
using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Media;
using WpfClipboard = global::Jalium.UI.Clipboard;

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
/// 用原生解析器与渲染器显示 Markdown 内容。
/// </summary>
/// <remarks>
/// <para>
/// 每个 Markdown 块都渲染成一个带默认 <c>ControlTemplate</c> 的
/// <see cref="MarkdownBlockPresenter"/> 派生控件（标题、段落、引用、代码块、列表、
/// 列表项、表格、单元格、分隔线、图表）。外观有三层定制入口：
/// </para>
/// <list type="number">
/// <item>隐式样式：<c>&lt;Style TargetType="MarkdownHeadingPresenter"&gt;</c> 等，作用于整个应用；</item>
/// <item>容器样式属性：<see cref="HeadingStyle"/>、<see cref="ParagraphStyle"/> 等，
/// 只作用于这一个 <see cref="Markdown"/> 实例，语义与 <c>ItemsControl.ItemContainerStyle</c> 一致；
/// <see cref="BlockContainerStyleSelector"/> 可按块动态选择；</item>
/// <item>行内文本：<see cref="BoldStyle"/>、<see cref="ItalicStyle"/>、
/// <see cref="InlineCodeStyle"/>、<see cref="LinkStyle"/> 描述形态，
/// <see cref="LinkForeground"/>、<see cref="InlineCodeForeground"/>、
/// <see cref="InlineCodeBackground"/> 描述主题色；这些属性都可继承，
/// 因此也能在单个块的样式里覆盖。</item>
/// </list>
/// </remarks>
[Jalium.UI.Markup.ContentProperty(nameof(Text))]
public class Markdown : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Document);

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

    // 增量追加状态：上次解析过的源文本、它规范化后的行，以及每个顶层块的起始行号。
    // 三者一起让「末尾追加」只需要重新解析并重建尾部，见 TryAppendText。
    private string _parsedText = string.Empty;
    private string[] _lines = Array.Empty<string>();
    private readonly List<int> _blockLineStarts = new();
    /// <summary>本次解析的链接引用与脚注定义表；增量追加要沿用它，见 <see cref="TryAppendText"/>。</summary>
    private MarkdownParseContext _parseContext = MarkdownParseContext.Empty(null);
    /// <summary>视图还「粘」在底部——内容变长时跟着滚。用户手动上滚会把它置为 false。</summary>
    private bool _isPinnedToEnd = true;
    private bool _suppressPinTracking;

    private readonly List<MarkdownSegment> _segments = new();
    private int _selectionAnchor;
    private int _selectionGlobalStart;
    private int _selectionGlobalEnd;
    private int _totalSelectableLength;
    private bool _isSelecting;

    private sealed class MarkdownSegment
    {
        public MarkdownSegment(IMarkdownSelectable selectable, UIElement element, int blockIndex)
        {
            Selectable = selectable;
            Element = element;
            BlockIndex = blockIndex;
        }

        public IMarkdownSelectable Selectable { get; }
        public UIElement Element { get; }
        public int BlockIndex { get; }
        public int GlobalStart { get; set; }
        public int Length { get; set; }
    }

    #region Content dependency properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Markdown),
            new PropertyMetadata(string.Empty, OnMarkdownStructureChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BaseUriProperty =
        DependencyProperty.Register(nameof(BaseUri), typeof(Uri), typeof(Markdown),
            new PropertyMetadata(null, OnMarkdownStructureChanged));

    /// <summary>
    /// Identifies the <see cref="HorizontalScrollBarVisibility"/> dependency property.
    /// </summary>
    /// <remarks>
    /// 默认是 <see cref="ScrollBarVisibility.Auto"/> 而不是 <see cref="ScrollViewer"/> 自己的
    /// <see cref="ScrollBarVisibility.Disabled"/>：Markdown 里的宽表格与长代码行没法换行，
    /// 横向滚动是它们唯一的出路。其余滚动行为一律沿用 <see cref="ScrollViewer"/> 的默认值。
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(Markdown),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the <see cref="VerticalScrollBarVisibility"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(Markdown),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the <see cref="IsStreaming"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsStreamingProperty =
        DependencyProperty.Register(nameof(IsStreaming), typeof(bool), typeof(Markdown),
            new PropertyMetadata(false, OnMarkdownStructureChanged));

    /// <summary>
    /// Identifies the <see cref="AutoScrollToEnd"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AutoScrollToEndProperty =
        DependencyProperty.Register(nameof(AutoScrollToEnd), typeof(bool), typeof(Markdown),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AutoLinkPlainUrlsProperty =
        DependencyProperty.Register(nameof(AutoLinkPlainUrls), typeof(bool), typeof(Markdown),
            new PropertyMetadata(true, OnMarkdownStructureChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OpenLinksExternallyProperty =
        DependencyProperty.Register(nameof(OpenLinksExternally), typeof(bool), typeof(Markdown),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsTextSelectionEnabledProperty =
        DependencyProperty.Register(nameof(IsTextSelectionEnabled), typeof(bool), typeof(Markdown),
            new PropertyMetadata(true, OnSelectionEnabledChanged));

    #endregion

    #region Block container style hooks

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HeadingStyleProperty =
        DependencyProperty.Register(nameof(HeadingStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ParagraphStyleProperty =
        DependencyProperty.Register(nameof(ParagraphStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty QuoteStyleProperty =
        DependencyProperty.Register(nameof(QuoteStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CodeBlockStyleProperty =
        DependencyProperty.Register(nameof(CodeBlockStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RuleStyleProperty =
        DependencyProperty.Register(nameof(RuleStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ListStyleProperty =
        DependencyProperty.Register(nameof(ListStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ListItemStyleProperty =
        DependencyProperty.Register(nameof(ListItemStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TableStyleProperty =
        DependencyProperty.Register(nameof(TableStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TableCellStyleProperty =
        DependencyProperty.Register(nameof(TableCellStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DiagramStyleProperty =
        DependencyProperty.Register(nameof(DiagramStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ImageStyleProperty =
        DependencyProperty.Register(nameof(ImageStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FootnoteStyleProperty =
        DependencyProperty.Register(nameof(FootnoteStyle), typeof(Style), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BlockContainerStyleSelectorProperty =
        DependencyProperty.Register(nameof(BlockContainerStyleSelector), typeof(StyleSelector), typeof(Markdown),
            new PropertyMetadata(null, OnContainerStyleChanged));

    #endregion

    #region Inline text style hooks (shared with MarkdownTextPresenter, inherited down the tree)

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightRatioProperty =
        MarkdownTextPresenter.LineHeightRatioProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty MonospaceFontFamilyProperty =
        MarkdownTextPresenter.MonospaceFontFamilyProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkForegroundProperty =
        MarkdownTextPresenter.LinkForegroundProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeForegroundProperty =
        MarkdownTextPresenter.InlineCodeForegroundProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeBackgroundProperty =
        MarkdownTextPresenter.InlineCodeBackgroundProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty BoldStyleProperty =
        MarkdownTextPresenter.BoldStyleProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty ItalicStyleProperty =
        MarkdownTextPresenter.ItalicStyleProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty InlineCodeStyleProperty =
        MarkdownTextPresenter.InlineCodeStyleProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LinkStyleProperty =
        MarkdownTextPresenter.LinkStyleProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty StrikethroughStyleProperty =
        MarkdownTextPresenter.StrikethroughStyleProperty.AddOwner(typeof(Markdown));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        MarkdownTextPresenter.SelectionBrushProperty.AddOwner(typeof(Markdown));

    #endregion

    public Markdown()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnSelectionMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnSelectionMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnSelectionMouseUp));
        BuildContextMenu();
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
    /// Replaces <see cref="Text"/> (the Markdown source) with the contents of a
    /// file. A byte-order mark, if present, decides the encoding; otherwise
    /// <paramref name="encoding"/> is used — UTF-8 when it is <see langword="null"/>.
    /// </summary>
    public void LoadFromFile(string path, System.Text.Encoding? encoding = null)
        => Text = TextFile.ReadAllText(path, encoding);

    /// <summary>
    /// Writes the Markdown source to a file using <paramref name="encoding"/> —
    /// UTF-8 when it is <see langword="null"/>.
    /// </summary>
    public void SaveToFile(string path, System.Text.Encoding? encoding = null)
        => TextFile.WriteAllText(path, Text, encoding);

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
    /// 内容还在增长（流式输出）。
    /// </summary>
    /// <remarks>
    /// 开启后，文档末尾那些「话没说完」的结构按已经闭合来解析：<c>**bold</c> 直接是粗体，
    /// 未闭合的反引号直接是代码，正在打出来的表格分隔行直接立成表格。少了这层处理，逐字渲染会
    /// 反复改主意——同一个词先是字面量、再变斜体、最后才是粗体。
    /// <para>
    /// 流式结束时把它设回 <see langword="false"/>：内容会按最终语义重新解析一次，
    /// 那些没能真正闭合的记号就退回字面量。
    /// </para>
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsStreaming
    {
        get => (bool)GetValue(IsStreamingProperty)!;
        set => SetValue(IsStreamingProperty, value);
    }

    /// <summary>
    /// 内容增长时自动滚到底部。
    /// </summary>
    /// <remarks>
    /// 跟随是「粘」在底部而不是每次都强拉：用户手动往上滚就停止跟随，滚回底部附近又恢复——
    /// 否则流式输出期间用户根本没法回看前文。
    /// </remarks>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool AutoScrollToEnd
    {
        get => (bool)GetValue(AutoScrollToEndProperty)!;
        set => SetValue(AutoScrollToEndProperty, value);
    }

    /// <summary>内容超出宽度时横向滚动条的显示策略。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>内容超出高度时纵向滚动条的显示策略。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// 是否把正文里裸写的 <c>https://…</c>、<c>www.…</c> 与邮箱识别成链接（GFM 扩展）。
    /// 关掉后只有 <c>[文本](地址)</c> 与 <c>&lt;地址&gt;</c> 这类显式写法才算链接。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool AutoLinkPlainUrls
    {
        get => (bool)GetValue(AutoLinkPlainUrlsProperty)!;
        set => SetValue(AutoLinkPlainUrlsProperty, value);
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
    /// Gets or sets whether the rendered content can be selected with the mouse and copied.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty)!;
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    #region Block container style accessors

    /// <summary>应用到每个标题容器（<see cref="MarkdownHeadingPresenter"/>）的样式。</summary>
    public Style? HeadingStyle
    {
        get => (Style?)GetValue(HeadingStyleProperty);
        set => SetValue(HeadingStyleProperty, value);
    }

    /// <summary>应用到每个段落容器（<see cref="MarkdownParagraphPresenter"/>）的样式。</summary>
    public Style? ParagraphStyle
    {
        get => (Style?)GetValue(ParagraphStyleProperty);
        set => SetValue(ParagraphStyleProperty, value);
    }

    /// <summary>应用到每个块引用容器（<see cref="MarkdownQuotePresenter"/>）的样式。</summary>
    public Style? QuoteStyle
    {
        get => (Style?)GetValue(QuoteStyleProperty);
        set => SetValue(QuoteStyleProperty, value);
    }

    /// <summary>应用到每个代码块容器（<see cref="MarkdownCodePresenter"/>）的样式。</summary>
    public Style? CodeBlockStyle
    {
        get => (Style?)GetValue(CodeBlockStyleProperty);
        set => SetValue(CodeBlockStyleProperty, value);
    }

    /// <summary>应用到每条分隔线容器（<see cref="MarkdownRulePresenter"/>）的样式。</summary>
    public Style? RuleStyle
    {
        get => (Style?)GetValue(RuleStyleProperty);
        set => SetValue(RuleStyleProperty, value);
    }

    /// <summary>应用到每个列表容器（<see cref="MarkdownListPresenter"/>）的样式。</summary>
    public Style? ListStyle
    {
        get => (Style?)GetValue(ListStyleProperty);
        set => SetValue(ListStyleProperty, value);
    }

    /// <summary>应用到每个列表项容器（<see cref="MarkdownListItemPresenter"/>）的样式。</summary>
    public Style? ListItemStyle
    {
        get => (Style?)GetValue(ListItemStyleProperty);
        set => SetValue(ListItemStyleProperty, value);
    }

    /// <summary>应用到每个表格容器（<see cref="MarkdownTablePresenter"/>）的样式。</summary>
    public Style? TableStyle
    {
        get => (Style?)GetValue(TableStyleProperty);
        set => SetValue(TableStyleProperty, value);
    }

    /// <summary>应用到每个表格单元格容器（<see cref="MarkdownTableCellPresenter"/>）的样式。</summary>
    public Style? TableCellStyle
    {
        get => (Style?)GetValue(TableCellStyleProperty);
        set => SetValue(TableCellStyleProperty, value);
    }

    /// <summary>应用到每个图表容器（<see cref="MarkdownDiagramPresenter"/>）的样式。</summary>
    public Style? DiagramStyle
    {
        get => (Style?)GetValue(DiagramStyleProperty);
        set => SetValue(DiagramStyleProperty, value);
    }

    /// <summary>应用到每个块级图片容器（<see cref="MarkdownImagePresenter"/>）的样式。</summary>
    public Style? ImageStyle
    {
        get => (Style?)GetValue(ImageStyleProperty);
        set => SetValue(ImageStyleProperty, value);
    }

    /// <summary>应用到每条脚注定义容器（<see cref="MarkdownFootnotePresenter"/>）的样式。</summary>
    public Style? FootnoteStyle
    {
        get => (Style?)GetValue(FootnoteStyleProperty);
        set => SetValue(FootnoteStyleProperty, value);
    }

    /// <summary>
    /// 按块动态选择容器样式。选择器返回非 <see langword="null"/> 时优先于上面的固定样式属性；
    /// 传入的 <c>item</c> 与 <c>container</c> 都是待应用样式的
    /// <see cref="MarkdownBlockPresenter"/>，可从 <see cref="MarkdownBlockPresenter.BlockKind"/>
    /// 或具体类型（如 <see cref="MarkdownHeadingPresenter.Level"/>）分派。
    /// </summary>
    public StyleSelector? BlockContainerStyleSelector
    {
        get => (StyleSelector?)GetValue(BlockContainerStyleSelectorProperty);
        set => SetValue(BlockContainerStyleSelectorProperty, value);
    }

    #endregion

    #region Inline text style accessors

    /// <summary>行内文本行高相对字号的倍数。</summary>
    public double LineHeightRatio
    {
        get => (double)GetValue(LineHeightRatioProperty)!;
        set => SetValue(LineHeightRatioProperty, value);
    }

    /// <summary>行内代码与代码块使用的等宽字体族。</summary>
    public FontFamily? MonospaceFontFamily
    {
        get => (FontFamily?)GetValue(MonospaceFontFamilyProperty);
        set => SetValue(MonospaceFontFamilyProperty, value);
    }

    /// <summary>链接文本的前景画刷。</summary>
    public Brush? LinkForeground
    {
        get => (Brush?)GetValue(LinkForegroundProperty);
        set => SetValue(LinkForegroundProperty, value);
    }

    /// <summary>行内代码的前景画刷。</summary>
    public Brush? InlineCodeForeground
    {
        get => (Brush?)GetValue(InlineCodeForegroundProperty);
        set => SetValue(InlineCodeForegroundProperty, value);
    }

    /// <summary>行内代码的背景画刷。</summary>
    public Brush? InlineCodeBackground
    {
        get => (Brush?)GetValue(InlineCodeBackgroundProperty);
        set => SetValue(InlineCodeBackgroundProperty, value);
    }

    /// <summary>粗体片段的行内样式。</summary>
    public MarkdownInlineStyle? BoldStyle
    {
        get => (MarkdownInlineStyle?)GetValue(BoldStyleProperty);
        set => SetValue(BoldStyleProperty, value);
    }

    /// <summary>斜体片段的行内样式。</summary>
    public MarkdownInlineStyle? ItalicStyle
    {
        get => (MarkdownInlineStyle?)GetValue(ItalicStyleProperty);
        set => SetValue(ItalicStyleProperty, value);
    }

    /// <summary>行内代码片段的行内样式。</summary>
    public MarkdownInlineStyle? InlineCodeStyle
    {
        get => (MarkdownInlineStyle?)GetValue(InlineCodeStyleProperty);
        set => SetValue(InlineCodeStyleProperty, value);
    }

    /// <summary>链接片段的行内样式。</summary>
    public MarkdownInlineStyle? LinkStyle
    {
        get => (MarkdownInlineStyle?)GetValue(LinkStyleProperty);
        set => SetValue(LinkStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to paint the text selection highlight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    #endregion

    /// <summary>
    /// Occurs when a rendered Markdown link is clicked.
    /// </summary>
    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    internal IReadOnlyList<MarkdownBlock> DebugBlocks => _blocks;

    /// <summary>诊断计数器：建过几个顶层块元素。流式追加时它应该几乎不涨。</summary>
    internal static long DebugBlockElementsCreated;

    /// <summary>诊断计数器：走了几次全量重建可视树。</summary>
    internal static long DebugFullRebuilds;

    internal StackPanel? DebugContentHost => _contentHost;

    /// <summary>
    /// 取回模板部件。
    /// </summary>
    /// <remarks>
    /// ★滚动视图与内容宿主都来自模板，不再由这里 <c>new</c> 出来塞进容器。以前那种写法有两个后果：
    /// 应用级的 <c>&lt;Style TargetType="ScrollViewer"&gt;</c> 够不着它，而且代码里顺手写死的
    /// <c>IsScrollBarAutoHideEnabled = false</c> 让 Markdown 的滚动条成了全应用唯一不会自动隐藏的那个。
    /// 现在只有横向滚动策略有自己的默认值（见 <see cref="HorizontalScrollBarVisibilityProperty"/>），
    /// 其余滚动行为全部跟随 <see cref="ScrollViewer"/>。
    /// </remarks>
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _container = GetTemplateChild("PART_Container") as Border;
        _contentHost = GetTemplateChild("PART_ContentHost") as StackPanel;

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
        }

        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }

        if (_contentHost == null)
        {
            return;
        }

        RebuildVisualTree();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // 可继承的排版属性（字体、前景、各类行内样式）改了不需要重建可视树，
        // 但要把已缓存排版的叶子呈现器叫醒——继承是按需向上取值的，它们收不到回调。
        if (MarkdownInheritedFormatting.AffectsInlineFormatting(e.Property))
        {
            MarkdownInheritedFormatting.InvalidateSubtree(_contentHost);
        }
    }

    private static void OnMarkdownStructureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Markdown markdown)
        {
            return;
        }

        if (e.Property == TextProperty && markdown.TryAppendText(e.NewValue as string))
        {
            return;
        }

        markdown.ParseMarkdown();
        markdown.RebuildVisualTree();
    }

    private static void OnContainerStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 容器样式换了要重新套用；行内样式与颜色是可继承属性，会自己流到呈现器上，不必重建。
        (d as Markdown)?.RebuildVisualTree();
    }

    private void ParseMarkdown()
    {
        _parsedText = Text ?? string.Empty;
        _blockLineStarts.Clear();

        if (string.IsNullOrWhiteSpace(_parsedText))
        {
            _lines = Array.Empty<string>();
            _blocks = Array.Empty<MarkdownBlock>();
            return;
        }

        _lines = MarkdownParser.Normalize(_parsedText).Split('\n');
        _parseContext = MarkdownParseContext.Create(_lines, BaseUri, AutoLinkPlainUrls, IsStreaming);
        _blocks = MarkdownParser.ParseLines(_lines, _parseContext, _blockLineStarts);
    }

    /// <summary>
    /// 把一段内容追加到末尾。
    /// </summary>
    /// <remarks>
    /// 流式输出（模型逐块吐字）用这个，而不是自己拼 <c>Text = Text + chunk</c>：这里保证走的是
    /// 「只重解析尾部、只重排最后一行」的增量路径，也保证 <see cref="AutoScrollToEnd"/> 的跟随
    /// 在同一帧里生效。<paramref name="chunk"/> 为空时什么都不做。
    /// </remarks>
    public void AppendText(string? chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        SetCurrentValue(TextProperty, (Text ?? string.Empty) + chunk);
    }

    /// <summary>清空内容，并把滚动位置与选区一起复位。</summary>
    public void Clear()
    {
        SetCurrentValue(TextProperty, string.Empty);
        _isPinnedToEnd = true;
        _scrollViewer?.ScrollToTop();
    }

    #region Incremental append (streaming)

    /// <summary>
    /// 流式输出的快路径：<see cref="Text"/> 只在末尾变长时，避开「全量重解析 + 整棵可视树重建」。
    /// </summary>
    /// <remarks>
    /// 逐字追加走慢路径的代价是 O(文本长度 × 追加次数)：每次赋值都要重新解析全文、清空
    /// <c>_contentHost.Children</c> 重建每一个块的呈现器，顺带把它们的排版缓存全部丢弃。
    /// 快路径把三件事都收敛到「只碰尾部」：
    /// <list type="number">
    ///   <item>只规范化并切分新追加的那一段文本，接到行缓冲末尾；</item>
    ///   <item>只从倒数第二个顶层块的起始行重新解析（顶层块无跨块状态，见
    ///         <c>MarkdownParser.ParseLines</c>）；</item>
    ///   <item>可视树按块结构比对，未变的块连元素带排版缓存一起留用，变了的块优先原地换内容
    ///         而不是重建元素——这样 <see cref="MarkdownTextPresenter"/> 才有机会只重排最后一行。</item>
    /// </list>
    /// 返回 <see langword="false"/> 表示这次变化不是纯追加（或状态不足以复用），交回慢路径。
    /// </remarks>
    private bool TryAppendText(string? newValue)
    {
        var newText = newValue ?? string.Empty;

        if (_contentHost == null ||
            _parsedText.Length == 0 ||
            _blockLineStarts.Count == 0 ||
            newText.Length <= _parsedText.Length ||
            !newText.StartsWith(_parsedText, StringComparison.Ordinal))
        {
            return false;
        }

        // 旧文本以 \r 收尾时，追加的 \n 会与它合并成一个换行——规范化就不再是逐段可拼接的了。
        if (_parsedText[^1] == '\r')
        {
            return false;
        }

        if (_contentHost.Children.Count != _blocks.Count)
        {
            return false;
        }

        // 链接引用定义与脚注定义是全文档可见的，块与块之间不再无状态：一条定义能改变文档里任何位置的
        // 行内解析结果。文档已经带定义、或这次追加引入了新定义时，都交回慢路径重扫一遍定义表。
        var delta = MarkdownParser.Normalize(newText.AsSpan(_parsedText.Length).ToString());
        if (_parseContext.HasDefinitions || AppendIntroducesDefinitions(delta))
        {
            return false;
        }

        AppendLines(delta);
        _parsedText = newText;

        // 只有最后一个顶层块会被追加的内容延长，这里保守地从倒数第二个块起重解析：多解析一个块几乎不要钱，
        // 而块结构比对会把没真正变化的块挡在可视树更新之外。
        var stableBlockCount = Math.Max(0, _blockLineStarts.Count - 2);
        var fromLine = _blockLineStarts[stableBlockCount];

        var tailLines = new string[_lines.Length - fromLine];
        Array.Copy(_lines, fromLine, tailLines, 0, tailLines.Length);

        var tailStarts = new List<int>();
        var tailBlocks = MarkdownParser.ParseLines(tailLines, _parseContext, tailStarts);

        var merged = new List<MarkdownBlock>(stableBlockCount + tailBlocks.Count);
        for (var index = 0; index < stableBlockCount; index++)
        {
            merged.Add(_blocks[index]);
        }
        merged.AddRange(tailBlocks);

        _blockLineStarts.RemoveRange(stableBlockCount, _blockLineStarts.Count - stableBlockCount);
        foreach (var start in tailStarts)
        {
            _blockLineStarts.Add(start + fromLine);
        }

        var previousBlocks = _blocks;
        _blocks = merged;
        SyncVisualTree(previousBlocks, stableBlockCount);
        return true;
    }

    /// <summary>
    /// 这次追加会不会让文档里多出（或改出）一条链接引用/脚注定义。第一段续在原末行上，
    /// 所以那一行要连着旧内容一起重新判定。
    /// </summary>
    private bool AppendIntroducesDefinitions(string normalizedDelta)
    {
        // 定义行必然带方括号；绝大多数追加片段一个都没有，这一步就把它们全放过去了。
        if (!normalizedDelta.Contains('[', StringComparison.Ordinal) &&
            !(_lines.Length > 0 && _lines[^1].Contains('[', StringComparison.Ordinal)))
        {
            return false;
        }

        var parts = normalizedDelta.Split('\n');
        if (LooksLikeDefinition((_lines.Length > 0 ? _lines[^1] : string.Empty) + parts[0]))
        {
            return true;
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (LooksLikeDefinition(parts[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeDefinition(string line)
    {
        var trimmed = line.TrimStart();
        return MarkdownParser.TryParseFootnoteLabel(trimmed, out _) ||
               MarkdownParser.TryParseLinkDefinitionLine(trimmed, baseUri: null, out _, out _);
    }

    /// <summary>
    /// 内容变过之后，如果还「粘」在底部就滚到底。
    /// </summary>
    /// <remarks>
    /// 要等布局跑完才知道新的可滚动高度，所以这里先 <see cref="UIElement.UpdateLayout"/> 再滚。
    /// </remarks>
    private void ScrollToEndIfPinned()
    {
        if (!AutoScrollToEnd || _scrollViewer == null || !_isPinnedToEnd)
        {
            return;
        }

        UpdateLayout();
        _suppressPinTracking = true;
        try
        {
            _scrollViewer.ScrollToEnd();
        }
        finally
        {
            _suppressPinTracking = false;
        }
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // 自己滚出来的那一次不算「用户意图」，否则跟随会把自己关掉。
        if (_suppressPinTracking || _scrollViewer == null)
        {
            return;
        }

        // 只在用户真的动了纵向位置时重新判断——内容变高会带来 ExtentHeight 变化，那不是用户操作。
        if (Math.Abs(e.VerticalChange) < 0.5)
        {
            return;
        }

        var distanceToEnd = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        _isPinnedToEnd = distanceToEnd <= PinToEndThreshold;
    }

    /// <summary>离底部多近算「还粘着」。留一点余量，免得亚像素误差把跟随关掉。</summary>
    private const double PinToEndThreshold = 4.0;

    /// <summary>把一段已规范化的新文本接到行缓冲末尾——第一段续在原末行上，其余各自成行。</summary>
    private void AppendLines(string normalizedDelta)
    {
        var parts = normalizedDelta.Split('\n');
        if (_lines.Length == 0)
        {
            _lines = parts;
            return;
        }

        var previousCount = _lines.Length;
        if (parts.Length > 1)
        {
            Array.Resize(ref _lines, previousCount + parts.Length - 1);
        }

        _lines[previousCount - 1] += parts[0];
        for (var index = 1; index < parts.Length; index++)
        {
            _lines[previousCount + index - 1] = parts[index];
        }
    }

    /// <summary>
    /// 把可视树对齐到新的块列表：前 <paramref name="stableBlockCount"/> 个块保证没动，从那之后按结构比对，
    /// 第一个真正变化的块起才动元素。
    /// </summary>
    private void SyncVisualTree(IReadOnlyList<MarkdownBlock> previousBlocks, int stableBlockCount)
    {
        if (_contentHost == null)
        {
            RebuildVisualTree();
            return;
        }

        var comparable = Math.Min(previousBlocks.Count, _blocks.Count);
        var firstChanged = stableBlockCount;
        while (firstChanged < comparable &&
               MarkdownParser.StructuralEquals(previousBlocks[firstChanged], _blocks[firstChanged]))
        {
            firstChanged++;
        }

        if (firstChanged >= _blocks.Count && previousBlocks.Count == _blocks.Count)
        {
            return;
        }

        for (var index = firstChanged; index < _blocks.Count; index++)
        {
            if (index >= previousBlocks.Count)
            {
                _contentHost.Children.Add(CreateBlockElement(_blocks[index], isNested: false));
                continue;
            }

            if (TryUpdateBlockInPlace(_contentHost.Children[index], previousBlocks[index], _blocks[index]))
            {
                continue;
            }

            _contentHost.Children[index] = CreateBlockElement(_blocks[index], isNested: false);
        }

        for (var index = previousBlocks.Count - 1; index >= _blocks.Count; index--)
        {
            _contentHost.Children.RemoveAt(index);
        }

        _selectionGlobalStart = 0;
        _selectionGlobalEnd = 0;
        _isSelecting = false;
        RecollectSelectablesFrom(firstChanged);
    }

    /// <summary>
    /// 同类型的块只换内容，不换元素——留住呈现器就是留住它的排版缓存，
    /// 正在变长的那个段落因此只需重排最后一行。
    /// </summary>
    private bool TryUpdateBlockInPlace(UIElement element, MarkdownBlock previous, MarkdownBlock current)
    {
        switch (previous, current)
        {
            case (MarkdownParagraphBlock, MarkdownParagraphBlock paragraph)
                when element is MarkdownParagraphPresenter { Content: MarkdownTextPresenter text }:
                text.Spans = FlattenInlines(paragraph.Inlines);
                return true;

            case (MarkdownHeadingBlock previousHeading, MarkdownHeadingBlock heading)
                when previousHeading.Level == heading.Level &&
                     element is MarkdownHeadingPresenter { Content: MarkdownTextPresenter text }:
                text.Spans = FlattenInlines(heading.Inlines);
                return true;

            case (MarkdownCodeBlock previousCode, MarkdownCodeBlock code)
                when element is MarkdownCodePresenter presenter &&
                     string.Equals(previousCode.Language, code.Language, StringComparison.Ordinal) &&
                     !IsMermaidLanguage(code.Language):
                presenter.Code = code.Text;
                return true;

            default:
                return false;
        }
    }

    private static bool IsMermaidLanguage(string? language) =>
        !string.IsNullOrEmpty(language) &&
        string.Equals(language.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase);

    #endregion

    private void RebuildVisualTree()
    {
        if (_contentHost == null)
        {
            return;
        }

        System.Threading.Interlocked.Increment(ref DebugFullRebuilds);
        _contentHost.Children.Clear();
        foreach (var block in _blocks)
        {
            _contentHost.Children.Add(CreateBlockElement(block, isNested: false));
        }

        _selectionGlobalStart = 0;
        _selectionGlobalEnd = 0;
        _isSelecting = false;
        CollectSelectables();
    }

    #region Block container construction

    private UIElement CreateBlockElement(MarkdownBlock block, bool isNested)
    {
        if (!isNested)
        {
            System.Threading.Interlocked.Increment(ref DebugBlockElementsCreated);
        }

        return block switch
        {
            MarkdownHeadingBlock heading => CreateHeadingPresenter(heading, isNested),
            MarkdownParagraphBlock paragraph => TryGetStandaloneImages(paragraph, out var images)
                ? CreateStandaloneImageBlock(images, isNested)
                : CreateParagraphPresenter(paragraph, isNested),
            MarkdownListBlock list => CreateListPresenter(list, isNested),
            MarkdownQuoteBlock quote => CreateQuotePresenter(quote, isNested),
            MarkdownCodeBlock code => CreateCodePresenter(code, isNested),
            MarkdownRuleBlock => CreateRulePresenter(isNested),
            MarkdownTableBlock table => CreateTablePresenter(table, isNested),
            MarkdownFootnoteDefinitionBlock footnote => CreateFootnotePresenter(footnote, isNested),
            _ => CreateEmptyParagraphPresenter(isNested),
        };
    }

    private MarkdownHeadingPresenter CreateHeadingPresenter(MarkdownHeadingBlock heading, bool isNested)
    {
        var presenter = new MarkdownHeadingPresenter
        {
            Level = Math.Clamp(heading.Level, 1, 6),
            IsNested = isNested,
            Content = CreateTextPresenter(heading.Inlines),
        };
        ApplyContainerStyle(presenter, HeadingStyle);
        return presenter;
    }

    private MarkdownParagraphPresenter CreateParagraphPresenter(MarkdownParagraphBlock paragraph, bool isNested)
    {
        var presenter = new MarkdownParagraphPresenter
        {
            IsNested = isNested,
            Content = CreateTextPresenter(paragraph.Inlines),
        };
        ApplyContainerStyle(presenter, ParagraphStyle);
        return presenter;
    }

    private MarkdownParagraphPresenter CreateEmptyParagraphPresenter(bool isNested)
    {
        var presenter = new MarkdownParagraphPresenter { IsNested = isNested };
        ApplyContainerStyle(presenter, ParagraphStyle);
        return presenter;
    }

    private MarkdownQuotePresenter CreateQuotePresenter(MarkdownQuoteBlock quote, bool isNested)
    {
        var presenter = new MarkdownQuotePresenter
        {
            IsNested = isNested,
            Content = CreateBlockHost(quote.Blocks, isNested: false),
        };
        ApplyContainerStyle(presenter, QuoteStyle);
        return presenter;
    }

    private MarkdownRulePresenter CreateRulePresenter(bool isNested)
    {
        var presenter = new MarkdownRulePresenter { IsNested = isNested };
        ApplyContainerStyle(presenter, RuleStyle);
        return presenter;
    }

    private MarkdownBlockPresenter CreateCodePresenter(MarkdownCodeBlock code, bool isNested)
    {
        if (!string.IsNullOrEmpty(code.Language) &&
            string.Equals(code.Language.Trim(), "mermaid", StringComparison.OrdinalIgnoreCase) &&
            TryCreateDiagram(code.Text) is { } diagram)
        {
            var diagramPresenter = new MarkdownDiagramPresenter
            {
                IsNested = isNested,
                Content = diagram,
            };
            ApplyContainerStyle(diagramPresenter, DiagramStyle);
            return diagramPresenter;
        }

        var presenter = new MarkdownCodePresenter
        {
            Code = code.Text,
            CodeLanguage = code.Language,
            IsNested = isNested,
        };
        ApplyContainerStyle(presenter, CodeBlockStyle);
        return presenter;
    }

    private static Jalium.UI.Controls.Charts.MermaidDiagram? TryCreateDiagram(string source)
    {
        var diagram = new Jalium.UI.Controls.Charts.MermaidDiagram { Source = source };
        return diagram.DiagramKind == Jalium.UI.Controls.Charts.MermaidDiagramKind.Unknown ? null : diagram;
    }

    private MarkdownListPresenter CreateListPresenter(MarkdownListBlock list, bool isNested)
    {
        var itemHost = new StackPanel { Orientation = Orientation.Vertical };

        for (var index = 0; index < list.Items.Count; index++)
        {
            var item = list.Items[index];
            var itemPresenter = new MarkdownListItemPresenter
            {
                IsNested = true,
                IsLastItem = index == list.Items.Count - 1,
                MarkerKind = item.TaskState is not null
                    ? MarkdownListMarkerKind.Task
                    : (list.Ordered ? MarkdownListMarkerKind.Number : MarkdownListMarkerKind.Bullet),
                ItemNumber = list.StartIndex + index,
                IsChecked = item.TaskState is true,
                Content = CreateBlockHost(item.Blocks, isNested: true),
            };
            ApplyContainerStyle(itemPresenter, ListItemStyle);
            itemHost.Children.Add(itemPresenter);
        }

        var presenter = new MarkdownListPresenter
        {
            IsOrdered = list.Ordered,
            StartIndex = list.StartIndex,
            IsNested = isNested,
            Content = itemHost,
        };
        ApplyContainerStyle(presenter, ListStyle);
        return presenter;
    }

    private MarkdownTablePresenter CreateTablePresenter(MarkdownTableBlock table, bool isNested)
    {
        var rowCount = table.HeaderRows.Count + table.Rows.Count;
        var columnCount = Math.Max(
            table.HeaderRows.Count == 0 ? 0 : table.HeaderRows.Max(static row => row.Cells.Count),
            table.Rows.Count == 0 ? 0 : table.Rows.Max(static row => row.Cells.Count));

        var grid = new Grid();

        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        for (var row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var alignments = table.Alignments;
        var currentRow = 0;
        foreach (var headerRow in table.HeaderRows)
        {
            AddTableRow(grid, headerRow, currentRow++, isHeader: true, alignments);
        }

        foreach (var bodyRow in table.Rows)
        {
            AddTableRow(grid, bodyRow, currentRow++, isHeader: false, alignments);
        }

        var presenter = new MarkdownTablePresenter
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            IsNested = isNested,
            Content = grid,
        };
        ApplyContainerStyle(presenter, TableStyle);
        return presenter;
    }

    private void AddTableRow(
        Grid grid,
        MarkdownTableRow row,
        int rowIndex,
        bool isHeader,
        IReadOnlyList<MarkdownColumnAlignment>? alignments)
    {
        for (var columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
        {
            var cell = new MarkdownTableCellPresenter
            {
                IsHeaderCell = isHeader,
                RowIndex = rowIndex,
                ColumnIndex = columnIndex,
                ColumnAlignment = ResolveColumnAlignment(alignments, columnIndex),
                IsNested = true,
                Content = CreateTextPresenter(row.Cells[columnIndex]),
            };
            ApplyContainerStyle(cell, TableCellStyle);

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, columnIndex);
            grid.Children.Add(cell);
        }
    }

    private StackPanel CreateBlockHost(IReadOnlyList<MarkdownBlock> blocks, bool isNested)
    {
        var host = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var block in blocks)
        {
            host.Children.Add(CreateBlockElement(block, isNested));
        }
        return host;
    }

    /// <summary>
    /// 段落里除了图片没有别的内容时，把每张图片提成独立的块级图片。
    /// </summary>
    /// <remarks>
    /// <c>![](shot.png)</c> 独占一行是最常见的写法，作者期待的是一张按容器宽度自适应、可以带图注的图，
    /// 而不是嵌在一行文字里的小图元。混排的图片（<c>文字 ![icon](i.png) 文字</c>）仍走
    /// <see cref="MarkdownTextPresenter"/> 的行内图片路径，那里它才应该跟着文字基线走。
    /// </remarks>
    private static bool TryGetStandaloneImages(
        MarkdownParagraphBlock paragraph, out List<MarkdownImageInline> images)
    {
        images = null!;
        var found = new List<MarkdownImageInline>();

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case MarkdownImageInline image:
                    found.Add(image);
                    continue;

                case MarkdownTextInline text when string.IsNullOrWhiteSpace(text.Text):
                case MarkdownLineBreakInline:
                    continue;

                default:
                    return false;
            }
        }

        if (found.Count == 0)
        {
            return false;
        }

        images = found;
        return true;
    }

    private UIElement CreateStandaloneImageBlock(List<MarkdownImageInline> images, bool isNested)
    {
        if (images.Count == 1)
        {
            return CreateImagePresenter(images[0], isNested);
        }

        var host = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var image in images)
        {
            host.Children.Add(CreateImagePresenter(image, isNested));
        }

        return host;
    }

    private MarkdownImagePresenter CreateImagePresenter(MarkdownImageInline image, bool isNested)
    {
        var presenter = new MarkdownImagePresenter
        {
            IsNested = isNested,
            Alt = image.Alt,
            ImageTarget = image.Target,
            Caption = image.Title ?? string.Empty,
            Source = CreateImageSource(image.Uri),
        };
        ApplyContainerStyle(presenter, ImageStyle);
        return presenter;
    }

    private static Jalium.UI.Media.ImageSource? CreateImageSource(Uri? uri)
    {
        if (uri == null || !uri.IsAbsoluteUri)
        {
            return null;
        }

        try
        {
            return new Jalium.UI.Media.Imaging.BitmapImage(uri);
        }
        catch (Exception)
        {
            // 坏地址或不支持的协议：交给默认模板显示替代文本。
            return null;
        }
    }

    private MarkdownFootnotePresenter CreateFootnotePresenter(MarkdownFootnoteDefinitionBlock footnote, bool isNested)
    {
        var presenter = new MarkdownFootnotePresenter
        {
            IsNested = isNested,
            Number = footnote.Number,
            Label = footnote.Label,
            Content = CreateBlockHost(footnote.Blocks, isNested: true),
        };
        ApplyContainerStyle(presenter, FootnoteStyle);
        return presenter;
    }

    private void ApplyContainerStyle(MarkdownBlockPresenter presenter, Style? fallbackStyle)
    {
        var style = BlockContainerStyleSelector?.SelectStyle(presenter, presenter) ?? fallbackStyle;
        if (style != null)
        {
            presenter.Style = style;
        }
    }

    private static HorizontalAlignment ResolveColumnAlignment(
        IReadOnlyList<MarkdownColumnAlignment>? alignments, int columnIndex)
    {
        if (alignments == null || columnIndex >= alignments.Count)
        {
            return HorizontalAlignment.Stretch;
        }

        return alignments[columnIndex] switch
        {
            MarkdownColumnAlignment.Left => HorizontalAlignment.Left,
            MarkdownColumnAlignment.Center => HorizontalAlignment.Center,
            MarkdownColumnAlignment.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Stretch,
        };
    }

    private MarkdownTextPresenter CreateTextPresenter(IReadOnlyList<MarkdownInline> inlines)
    {
        // 字体、字号、字重、前景以及全部行内样式都靠依赖属性继承流下来，
        // 所以块级样式改什么，这里的文字就跟着变，不需要在这里再解析一次。
        var presenter = new MarkdownTextPresenter
        {
            Spans = FlattenInlines(inlines),
        };
        presenter.LinkClicked += OnInlineLinkClicked;
        return presenter;
    }

    #endregion

    private void OnInlineLinkClicked(object? sender, MarkdownLinkClickedEventArgs e)
    {
        LinkClicked?.Invoke(this, e);
        if (e.Handled)
        {
            return;
        }

        // 脚注引用默认滚到对应的定义上；宿主想自己处理，把事件标成 Handled 即可。
        if (!e.Uri.IsAbsoluteUri && TryBringFootnoteIntoView(e.Uri.OriginalString))
        {
            return;
        }

        if (!OpenLinksExternally || !e.Uri.IsAbsoluteUri || !s_allowedSchemes.Contains(e.Uri.Scheme))
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

    /// <summary>脚注引用链接的目标前缀。用相对 URI，这样它永远不会被当成外部地址打开。</summary>
    private const string FootnoteUriPrefix = "#fn-";

    private bool TryBringFootnoteIntoView(string target)
    {
        if (!target.StartsWith(FootnoteUriPrefix, StringComparison.Ordinal) || _contentHost == null)
        {
            return false;
        }

        var label = target[FootnoteUriPrefix.Length..];
        var presenter = FindFootnotePresenter(_contentHost, label);
        presenter?.BringIntoView();
        return presenter != null;
    }

    private static MarkdownFootnotePresenter? FindFootnotePresenter(DependencyObject root, string label)
    {
        if (root is MarkdownFootnotePresenter footnote &&
            string.Equals(footnote.Label, label, StringComparison.Ordinal))
        {
            return footnote;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is { } child &&
                FindFootnotePresenter(child, label) is { } found)
            {
                return found;
            }
        }

        return null;
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

                case MarkdownStrikethroughInline strikethrough:
                    AppendInlineSpans(spans, strikethrough.Children, style with { Strikethrough = true });
                    break;

                case MarkdownLinkInline link:
                    AppendInlineSpans(spans, link.Children, style with { LinkUri = link.Uri });
                    break;

                case MarkdownImageInline image:
                    spans.Add(new MarkdownTextSpan(
                        image.Alt,
                        style,
                        IsLineBreak: false,
                        Image: new MarkdownInlineImage(image.Uri, image.Alt, image.Target, image.Title)));
                    break;

                case MarkdownFootnoteReferenceInline footnote:
                    // 做成指向脚注定义的相对链接：既拿到了链接样式（一眼看出可点），
                    // 又因为不是绝对 URI 而绝不会被当成外部地址打开。
                    spans.Add(new MarkdownTextSpan(
                        $"[{footnote.Number}]",
                        style with { LinkUri = new Uri(FootnoteUriPrefix + footnote.Label, UriKind.Relative) }));
                    break;

                case MarkdownLineBreakInline:
                    spans.Add(new MarkdownTextSpan(string.Empty, style, IsLineBreak: true));
                    break;
            }
        }
    }

    #region Content extraction (translation / programmatic copy)

    /// <summary>
    /// Returns the rendered content as plain text with all Markdown markers removed.
    /// Useful for feeding the content to translation or text-to-speech services.
    /// </summary>
    public string GetPlainText() => MarkdownSerializer.ToPlainText(_blocks);

    /// <summary>
    /// Returns the Markdown source (the value of <see cref="Text"/>).
    /// </summary>
    public string GetMarkdownText() => Text ?? string.Empty;

    /// <summary>
    /// Returns the content rendered as a standalone HTML document.
    /// </summary>
    public string GetHtml() => MarkdownSerializer.ToHtmlDocument(_blocks);

    /// <summary>
    /// Returns the content rendered as an RTF document.
    /// </summary>
    public string GetRtf() => MarkdownSerializer.ToRtf(_blocks);

    /// <summary>
    /// Gets the currently selected text as plain text, or an empty string when nothing is selected.
    /// </summary>
    public string SelectedText => HasSelection ? BuildSelectedText() : string.Empty;

    /// <summary>
    /// Gets whether a non-empty selection currently exists.
    /// </summary>
    public bool HasSelection => _selectionGlobalEnd > _selectionGlobalStart;

    #endregion

    #region Copy commands

    /// <summary>
    /// Copies the selection (or the whole document when nothing is selected) to the clipboard in
    /// plain-text, HTML, and RTF formats so it can be pasted into any target.
    /// </summary>
    public void Copy()
    {
        var blocks = HasSelection ? GetTouchedBlocks() : _blocks;
        if (blocks.Count == 0)
        {
            return;
        }

        var plain = HasSelection ? BuildSelectedText() : MarkdownSerializer.ToPlainText(blocks);
        var data = new global::Jalium.UI.DataObject();
        data.SetData(DataFormats.Text, plain);
        data.SetData(DataFormats.Html, MarkdownSerializer.ToHtmlFragment(blocks));
        data.SetData(DataFormats.Rtf, MarkdownSerializer.ToRtf(blocks));
        WpfClipboard.SetDataObject(data, copy: true);
    }

    /// <summary>
    /// Copies the selection (or the whole document) as plain text without Markdown markers.
    /// </summary>
    public void CopyAsPlainText()
        => WpfClipboard.SetText(HasSelection ? BuildSelectedText() : GetPlainText());

    /// <summary>
    /// Copies the selection (or the whole document) as Markdown source (text with markers).
    /// </summary>
    public void CopyAsMarkdownText()
    {
        var markdown = HasSelection ? MarkdownSerializer.ToMarkdown(GetTouchedBlocks()) : GetMarkdownText();
        WpfClipboard.SetText(markdown);
    }

    /// <summary>
    /// Copies the selection (or the whole document) as rich text (HTML + RTF, plus a plain-text fallback).
    /// </summary>
    public void CopyAsRichText() => Copy();

    /// <summary>
    /// Selects the entire rendered document.
    /// </summary>
    public void SelectAll()
    {
        if (!IsTextSelectionEnabled || _segments.Count == 0)
        {
            return;
        }

        RecomputeSegmentOffsets();
        ApplyGlobalSelection(0, _totalSelectableLength);
        Focus();
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectionGlobalStart = 0;
        _selectionGlobalEnd = 0;
        foreach (var segment in _segments)
        {
            segment.Selectable.ClearSelectionRange();
        }
    }

    #endregion

    #region Selection coordination

    private static void OnSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Markdown markdown && e.NewValue is false)
        {
            markdown.ClearSelection();
        }
    }

    private void CollectSelectables() => RecollectSelectablesFrom(0);

    /// <summary>
    /// 从第 <paramref name="fromBlockIndex"/> 个块起重新收集可选中片段，之前的原样留用。
    /// </summary>
    /// <remarks>
    /// 收集是按块序深度优先做的，所以 <c>_segments</c> 的 <c>BlockIndex</c> 天然非递减，
    /// 砍掉尾巴再续收就等价于全量重收。追加一次就全树递归一遍的话，块一多它自己就成了新的瓶颈。
    /// </remarks>
    private void RecollectSelectablesFrom(int fromBlockIndex)
    {
        if (_contentHost == null)
        {
            _segments.Clear();
            return;
        }

        if (fromBlockIndex <= 0)
        {
            _segments.Clear();
        }
        else
        {
            var keep = 0;
            while (keep < _segments.Count && _segments[keep].BlockIndex < fromBlockIndex)
            {
                keep++;
            }

            _segments.RemoveRange(keep, _segments.Count - keep);
        }

        var count = Math.Min(_contentHost.Children.Count, _blocks.Count);
        for (var i = Math.Max(0, fromBlockIndex); i < count; i++)
        {
            CollectSelectablesFrom(_contentHost.Children[i], i);
        }
    }

    private void CollectSelectablesFrom(DependencyObject node, int blockIndex)
    {
        // 选区画刷是可继承属性，会自己从 Markdown 流到叶子呈现器，这里只负责收集顺序。
        if (node is IMarkdownSelectable selectable && node is UIElement element)
        {
            _segments.Add(new MarkdownSegment(selectable, element, blockIndex));
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child != null)
            {
                CollectSelectablesFrom(child, blockIndex);
            }
        }
    }

    private void RecomputeSegmentOffsets()
    {
        var global = 0;
        foreach (var segment in _segments)
        {
            segment.GlobalStart = global;
            segment.Length = segment.Selectable.SelectableLength;
            global += segment.Length + 1; // +1 for the implicit newline between segments
        }
        _totalSelectableLength = global > 0 ? global - 1 : 0;
    }

    private void ApplyGlobalSelection(int start, int end)
    {
        _selectionGlobalStart = Math.Min(start, end);
        _selectionGlobalEnd = Math.Max(start, end);

        foreach (var segment in _segments)
        {
            var localStart = Math.Clamp(_selectionGlobalStart - segment.GlobalStart, 0, segment.Length);
            var localEnd = Math.Clamp(_selectionGlobalEnd - segment.GlobalStart, 0, segment.Length);
            if (localEnd > localStart)
            {
                segment.Selectable.SetSelectionRange(localStart, localEnd);
            }
            else
            {
                segment.Selectable.ClearSelectionRange();
            }
        }
    }

    private string BuildSelectedText()
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var segment in _segments)
        {
            var localStart = Math.Clamp(_selectionGlobalStart - segment.GlobalStart, 0, segment.Length);
            var localEnd = Math.Clamp(_selectionGlobalEnd - segment.GlobalStart, 0, segment.Length);
            if (localEnd > localStart)
            {
                if (!first)
                {
                    sb.Append('\n');
                }
                sb.Append(segment.Selectable.GetSelectionText(localStart, localEnd));
                first = false;
            }
        }
        return sb.ToString();
    }

    private IReadOnlyList<MarkdownBlock> GetTouchedBlocks()
    {
        var indices = new SortedSet<int>();
        foreach (var segment in _segments)
        {
            var localStart = Math.Clamp(_selectionGlobalStart - segment.GlobalStart, 0, segment.Length);
            var localEnd = Math.Clamp(_selectionGlobalEnd - segment.GlobalStart, 0, segment.Length);
            if (localEnd > localStart)
            {
                indices.Add(segment.BlockIndex);
            }
        }

        var result = new List<MarkdownBlock>(indices.Count);
        foreach (var index in indices)
        {
            if (index >= 0 && index < _blocks.Count)
            {
                result.Add(_blocks[index]);
            }
        }
        return result;
    }

    private bool TryHitTestGlobal(MouseEventArgs e, out int globalIndex)
    {
        globalIndex = 0;
        MarkdownSegment? best = null;
        var bestDistance = double.PositiveInfinity;
        var bestLocal = default(Point);

        foreach (var segment in _segments)
        {
            var local = e.GetPosition(segment.Element);
            var height = segment.Element.RenderSize.Height;
            var distance = local.Y < 0 ? -local.Y : (local.Y > height ? local.Y - height : 0);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = segment;
                bestLocal = local;
            }
        }

        if (best == null)
        {
            return false;
        }

        if (best.Selectable.TryHitTestCharacter(bestLocal, out var charIndex))
        {
            globalIndex = best.GlobalStart + Math.Clamp(charIndex, 0, best.Length);
            return true;
        }

        globalIndex = best.GlobalStart;
        return true;
    }

    private void OnSelectionMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsTextSelectionEnabled || e.ChangedButton != MouseButton.Left || _segments.Count == 0)
        {
            return;
        }

        RecomputeSegmentOffsets();
        if (!TryHitTestGlobal(e, out var anchor))
        {
            return;
        }

        _selectionAnchor = anchor;
        _isSelecting = true;
        Focus();
        CaptureMouse();
        ApplyGlobalSelection(anchor, anchor);
        e.Handled = true;
    }

    private void OnSelectionMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        if (TryHitTestGlobal(e, out var caret))
        {
            ApplyGlobalSelection(_selectionAnchor, caret);
        }
        e.Handled = true;
    }

    private void OnSelectionMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting)
        {
            return;
        }

        _isSelecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsTextSelectionEnabled)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.C)
            {
                Copy();
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                SelectAll();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape && HasSelection)
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        var copyPlain = new MenuItem { Header = "复制纯文本" };
        copyPlain.Click += (_, _) => CopyAsPlainText();

        var copyMarkdown = new MenuItem { Header = "复制 Markdown" };
        copyMarkdown.Click += (_, _) => CopyAsMarkdownText();

        var copyRich = new MenuItem { Header = "复制富文本" };
        copyRich.Click += (_, _) => CopyAsRichText();

        menu.Items.Add(copyPlain);
        menu.Items.Add(copyMarkdown);
        menu.Items.Add(copyRich);
        ContextMenu = menu;
    }

    #endregion
}
