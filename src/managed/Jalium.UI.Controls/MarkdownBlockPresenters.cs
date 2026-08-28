using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// <see cref="Markdown"/> 渲染出的块级元素类别。
/// </summary>
public enum MarkdownBlockKind
{
    /// <summary>段落。</summary>
    Paragraph,

    /// <summary>标题。</summary>
    Heading,

    /// <summary>列表容器。</summary>
    List,

    /// <summary>列表项。</summary>
    ListItem,

    /// <summary>块引用。</summary>
    Quote,

    /// <summary>围栏代码块。</summary>
    Code,

    /// <summary>分隔线。</summary>
    Rule,

    /// <summary>表格容器。</summary>
    Table,

    /// <summary>表格单元格。</summary>
    TableCell,

    /// <summary>由 mermaid 代码块渲染出的图表。</summary>
    Diagram,

    /// <summary>独占一行的图片。</summary>
    Image,

    /// <summary>脚注定义。</summary>
    Footnote,
}

/// <summary>
/// 列表项前导标记的类别。
/// </summary>
public enum MarkdownListMarkerKind
{
    /// <summary>无序列表的项目符号。</summary>
    Bullet,

    /// <summary>有序列表的编号。</summary>
    Number,

    /// <summary>任务列表的勾选框。</summary>
    Task,
}

/// <summary>
/// 所有 Markdown 块级容器控件的基类。
/// </summary>
/// <remarks>
/// 每个 Markdown 块都由一个此类的派生控件承载，它带默认 <c>ControlTemplate</c>，
/// 可以像 <see cref="ComboBoxItem"/> 那样通过隐式样式
/// （<c>&lt;Style TargetType="MarkdownHeadingPresenter"&gt;</c>）或
/// <see cref="Markdown"/> 上对应的容器样式属性整体替换外观。
/// 块的行内文本放在 <see cref="ContentControl.Content"/> 中，由模板里的
/// <c>ContentPresenter</c> 呈现。
/// </remarks>
public abstract class MarkdownBlockPresenter : ContentControl
{
    /// <summary>
    /// Identifies the <see cref="IsNested"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty IsNestedProperty =
        DependencyProperty.Register(nameof(IsNested), typeof(bool), typeof(MarkdownBlockPresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="FontSizeRatio"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeRatioProperty =
        MarkdownTextPresenter.FontSizeRatioProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="LineHeightRatio"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightRatioProperty =
        MarkdownTextPresenter.LineHeightRatioProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="MonospaceFontFamily"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty MonospaceFontFamilyProperty =
        MarkdownTextPresenter.MonospaceFontFamilyProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="LinkForeground"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkForegroundProperty =
        MarkdownTextPresenter.LinkForegroundProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="InlineCodeForeground"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeForegroundProperty =
        MarkdownTextPresenter.InlineCodeForegroundProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="InlineCodeBackground"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeBackgroundProperty =
        MarkdownTextPresenter.InlineCodeBackgroundProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="BoldStyle"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty BoldStyleProperty =
        MarkdownTextPresenter.BoldStyleProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="ItalicStyle"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty ItalicStyleProperty =
        MarkdownTextPresenter.ItalicStyleProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="InlineCodeStyle"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty InlineCodeStyleProperty =
        MarkdownTextPresenter.InlineCodeStyleProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="LinkStyle"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LinkStyleProperty =
        MarkdownTextPresenter.LinkStyleProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// Identifies the <see cref="SelectionBrush"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        MarkdownTextPresenter.SelectionBrushProperty.AddOwner(typeof(MarkdownBlockPresenter));

    /// <summary>
    /// 初始化 <see cref="MarkdownBlockPresenter"/> 的新实例。
    /// </summary>
    protected MarkdownBlockPresenter()
    {
        // 所有 Markdown 块都靠 ControlTemplate + ContentPresenter 呈现，
        // 不能走 ContentControl 默认的“直接把 Content 挂成可视子级”那条路，
        // 否则模板里的边框、分隔线、前导标记都不会出现。
        UseTemplateContentManagement();
    }

    /// <summary>
    /// 该控件承载的块类别，可用于 <see cref="StyleSelector"/> 中分派样式。
    /// </summary>
    public abstract MarkdownBlockKind BlockKind { get; }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (MarkdownInheritedFormatting.AffectsInlineFormatting(e.Property))
        {
            MarkdownInheritedFormatting.InvalidateSubtree(this);
        }
    }

    /// <summary>
    /// 该块是否嵌套在列表项或引用等容器内。默认样式据此收紧块间距。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public bool IsNested
    {
        get => (bool)GetValue(IsNestedProperty)!;
        set => SetValue(IsNestedProperty, value);
    }

    /// <summary>行内文本相对继承字号的缩放倍数。</summary>
    public double FontSizeRatio
    {
        get => (double)GetValue(FontSizeRatioProperty)!;
        set => SetValue(FontSizeRatioProperty, value);
    }

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

    /// <summary>选区高亮画刷。</summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }
}

/// <summary>
/// 承载一个 Markdown 标题。默认样式按 <see cref="Level"/> 触发字号、行高与下方分隔线。
/// </summary>
public class MarkdownHeadingPresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="Level"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(int), typeof(MarkdownHeadingPresenter),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the <see cref="HasSeparator"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HasSeparatorProperty =
        DependencyProperty.Register(nameof(HasSeparator), typeof(bool), typeof(MarkdownHeadingPresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="SeparatorBrush"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(MarkdownHeadingPresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="SeparatorThickness"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeparatorThicknessProperty =
        DependencyProperty.Register(nameof(SeparatorThickness), typeof(double), typeof(MarkdownHeadingPresenter),
            new PropertyMetadata(1.0));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Heading;

    /// <summary>标题级别，1 至 6。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int Level
    {
        get => (int)GetValue(LevelProperty)!;
        set => SetValue(LevelProperty, value);
    }

    /// <summary>是否在标题下方绘制分隔线。默认样式为一、二级标题打开。</summary>
    public bool HasSeparator
    {
        get => (bool)GetValue(HasSeparatorProperty)!;
        set => SetValue(HasSeparatorProperty, value);
    }

    /// <summary>分隔线画刷。</summary>
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>分隔线粗细。</summary>
    public double SeparatorThickness
    {
        get => (double)GetValue(SeparatorThicknessProperty)!;
        set => SetValue(SeparatorThicknessProperty, value);
    }
}

/// <summary>
/// 承载一个 Markdown 段落。
/// </summary>
public class MarkdownParagraphPresenter : MarkdownBlockPresenter
{
    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Paragraph;
}

/// <summary>
/// 承载一个 Markdown 块引用，内容是引用内的子块。
/// </summary>
public class MarkdownQuotePresenter : MarkdownBlockPresenter
{
    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Quote;
}

/// <summary>
/// 承载一条 Markdown 分隔线。
/// </summary>
public class MarkdownRulePresenter : MarkdownBlockPresenter
{
    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Rule;
}

/// <summary>
/// 承载一个围栏代码块。默认模板内是 <see cref="MarkdownCodeTextPresenter"/>。
/// </summary>
public class MarkdownCodePresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="Code"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(nameof(Code), typeof(string), typeof(MarkdownCodePresenter),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="CodeLanguage"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CodeLanguageProperty =
        DependencyProperty.Register(nameof(CodeLanguage), typeof(string), typeof(MarkdownCodePresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="ShowLineNumbers"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(MarkdownCodePresenter),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the <see cref="LineNumberForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineNumberForegroundProperty =
        DependencyProperty.Register(nameof(LineNumberForeground), typeof(Brush), typeof(MarkdownCodePresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="GutterBackground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(MarkdownCodePresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="GutterSeparatorBrush"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterSeparatorBrushProperty =
        DependencyProperty.Register(nameof(GutterSeparatorBrush), typeof(Brush), typeof(MarkdownCodePresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="CodePadding"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CodePaddingProperty =
        DependencyProperty.Register(nameof(CodePadding), typeof(Thickness), typeof(MarkdownCodePresenter),
            new PropertyMetadata(new Thickness(12)));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Code;

    /// <summary>代码正文。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Code
    {
        get => (string)(GetValue(CodeProperty) ?? string.Empty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>围栏代码块的语言标识。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? CodeLanguage
    {
        get => (string?)GetValue(CodeLanguageProperty);
        set => SetValue(CodeLanguageProperty, value);
    }

    /// <summary>是否显示行号栏。</summary>
    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty)!;
        set => SetValue(ShowLineNumbersProperty, value);
    }

    /// <summary>行号文字画刷。</summary>
    public Brush? LineNumberForeground
    {
        get => (Brush?)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    /// <summary>行号栏背景画刷。</summary>
    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    /// <summary>行号栏与正文之间分隔线的画刷。</summary>
    public Brush? GutterSeparatorBrush
    {
        get => (Brush?)GetValue(GutterSeparatorBrushProperty);
        set => SetValue(GutterSeparatorBrushProperty, value);
    }

    /// <summary>代码正文四周的内边距。与 <see cref="Control.Padding"/> 分开，便于外框与正文各自留白。</summary>
    public Thickness CodePadding
    {
        get => (Thickness)GetValue(CodePaddingProperty)!;
        set => SetValue(CodePaddingProperty, value);
    }
}

/// <summary>
/// 承载由 mermaid 代码块解析出的图表。
/// </summary>
public class MarkdownDiagramPresenter : MarkdownBlockPresenter
{
    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Diagram;
}

/// <summary>
/// 承载一个 Markdown 列表，内容是若干 <see cref="MarkdownListItemPresenter"/>。
/// </summary>
public class MarkdownListPresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="IsOrdered"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IsOrderedProperty =
        DependencyProperty.Register(nameof(IsOrdered), typeof(bool), typeof(MarkdownListPresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="StartIndex"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty StartIndexProperty =
        DependencyProperty.Register(nameof(StartIndex), typeof(int), typeof(MarkdownListPresenter),
            new PropertyMetadata(1));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.List;

    /// <summary>是否为有序列表。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public bool IsOrdered
    {
        get => (bool)GetValue(IsOrderedProperty)!;
        set => SetValue(IsOrderedProperty, value);
    }

    /// <summary>有序列表的起始序号。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int StartIndex
    {
        get => (int)GetValue(StartIndexProperty)!;
        set => SetValue(StartIndexProperty, value);
    }
}

/// <summary>
/// 承载一个 Markdown 列表项：左侧是前导标记，右侧是该项的子块。
/// </summary>
/// <remarks>
/// 前导标记文本由 <see cref="MarkerKind"/>、<see cref="ItemNumber"/>、
/// <see cref="IsChecked"/> 以及 <see cref="BulletGlyph"/> / <see cref="NumberFormat"/> /
/// <see cref="TaskCheckedGlyph"/> / <see cref="TaskUncheckedGlyph"/> 共同算出，
/// 结果暴露在只读的 <see cref="Marker"/> 上供模板绑定。改这几个字形属性就能换掉标记外观，
/// 无需替换模板。
/// </remarks>
public class MarkdownListItemPresenter : MarkdownBlockPresenter
{
    private static readonly DependencyPropertyKey MarkerPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Marker), typeof(string), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="Marker"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MarkerProperty = MarkerPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the <see cref="MarkerKind"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MarkerKindProperty =
        DependencyProperty.Register(nameof(MarkerKind), typeof(MarkdownListMarkerKind), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(MarkdownListMarkerKind.Bullet, OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="ItemNumber"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ItemNumberProperty =
        DependencyProperty.Register(nameof(ItemNumber), typeof(int), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(1, OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="IsChecked"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(false, OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="IsLastItem"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty IsLastItemProperty =
        DependencyProperty.Register(nameof(IsLastItem), typeof(bool), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="BulletGlyph"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BulletGlyphProperty =
        DependencyProperty.Register(nameof(BulletGlyph), typeof(string), typeof(MarkdownListItemPresenter),
            new PropertyMetadata("•", OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="NumberFormat"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NumberFormatProperty =
        DependencyProperty.Register(nameof(NumberFormat), typeof(string), typeof(MarkdownListItemPresenter),
            new PropertyMetadata("{0}.", OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="TaskCheckedGlyph"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TaskCheckedGlyphProperty =
        DependencyProperty.Register(nameof(TaskCheckedGlyph), typeof(string), typeof(MarkdownListItemPresenter),
            new PropertyMetadata("[x]", OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="TaskUncheckedGlyph"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TaskUncheckedGlyphProperty =
        DependencyProperty.Register(nameof(TaskUncheckedGlyph), typeof(string), typeof(MarkdownListItemPresenter),
            new PropertyMetadata("[ ]", OnMarkerInputChanged));

    /// <summary>
    /// Identifies the <see cref="MarkerWidth"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarkerWidthProperty =
        DependencyProperty.Register(nameof(MarkerWidth), typeof(double), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(28.0));

    /// <summary>
    /// Identifies the <see cref="MarkerMargin"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MarkerMarginProperty =
        DependencyProperty.Register(nameof(MarkerMargin), typeof(Thickness), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(new Thickness(0, 0, 10, 0)));

    /// <summary>
    /// Identifies the <see cref="MarkerForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MarkerForegroundProperty =
        DependencyProperty.Register(nameof(MarkerForeground), typeof(Brush), typeof(MarkdownListItemPresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// 初始化 <see cref="MarkdownListItemPresenter"/> 的新实例。
    /// </summary>
    public MarkdownListItemPresenter()
    {
        UpdateMarker();
    }

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.ListItem;

    /// <summary>算出的前导标记文本，供模板绑定。</summary>
    public string Marker => (string)(GetValue(MarkerProperty) ?? string.Empty);

    /// <summary>前导标记类别。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public MarkdownListMarkerKind MarkerKind
    {
        get => (MarkdownListMarkerKind)GetValue(MarkerKindProperty)!;
        set => SetValue(MarkerKindProperty, value);
    }

    /// <summary>有序列表项的序号。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int ItemNumber
    {
        get => (int)GetValue(ItemNumberProperty)!;
        set => SetValue(ItemNumberProperty, value);
    }

    /// <summary>任务列表项是否已勾选。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty)!;
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>是否为列表中的最后一项。默认样式据此去掉项间距。</summary>
    public bool IsLastItem
    {
        get => (bool)GetValue(IsLastItemProperty)!;
        set => SetValue(IsLastItemProperty, value);
    }

    /// <summary>无序列表使用的项目符号字形。</summary>
    public string BulletGlyph
    {
        get => (string)(GetValue(BulletGlyphProperty) ?? string.Empty);
        set => SetValue(BulletGlyphProperty, value);
    }

    /// <summary>有序列表编号的格式串，<c>{0}</c> 是序号。</summary>
    public string NumberFormat
    {
        get => (string)(GetValue(NumberFormatProperty) ?? "{0}.");
        set => SetValue(NumberFormatProperty, value);
    }

    /// <summary>任务列表已勾选时的字形。</summary>
    public string TaskCheckedGlyph
    {
        get => (string)(GetValue(TaskCheckedGlyphProperty) ?? string.Empty);
        set => SetValue(TaskCheckedGlyphProperty, value);
    }

    /// <summary>任务列表未勾选时的字形。</summary>
    public string TaskUncheckedGlyph
    {
        get => (string)(GetValue(TaskUncheckedGlyphProperty) ?? string.Empty);
        set => SetValue(TaskUncheckedGlyphProperty, value);
    }

    /// <summary>前导标记列的最小宽度。</summary>
    public double MarkerWidth
    {
        get => (double)GetValue(MarkerWidthProperty)!;
        set => SetValue(MarkerWidthProperty, value);
    }

    /// <summary>前导标记的外边距。</summary>
    public Thickness MarkerMargin
    {
        get => (Thickness)GetValue(MarkerMarginProperty)!;
        set => SetValue(MarkerMarginProperty, value);
    }

    /// <summary>前导标记的前景画刷。</summary>
    public Brush? MarkerForeground
    {
        get => (Brush?)GetValue(MarkerForegroundProperty);
        set => SetValue(MarkerForegroundProperty, value);
    }

    private static void OnMarkerInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as MarkdownListItemPresenter)?.UpdateMarker();
    }

    private void UpdateMarker()
    {
        var marker = MarkerKind switch
        {
            MarkdownListMarkerKind.Number => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                string.IsNullOrEmpty(NumberFormat) ? "{0}." : NumberFormat,
                ItemNumber),
            MarkdownListMarkerKind.Task => IsChecked ? TaskCheckedGlyph : TaskUncheckedGlyph,
            _ => BulletGlyph,
        };

        SetValue(MarkerPropertyKey, marker ?? string.Empty);
    }
}

/// <summary>
/// 承载一个 Markdown 表格，内容是按行列摆放 <see cref="MarkdownTableCellPresenter"/> 的网格。
/// </summary>
public class MarkdownTablePresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="ColumnCount"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ColumnCountProperty =
        DependencyProperty.Register(nameof(ColumnCount), typeof(int), typeof(MarkdownTablePresenter),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the <see cref="RowCount"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RowCountProperty =
        DependencyProperty.Register(nameof(RowCount), typeof(int), typeof(MarkdownTablePresenter),
            new PropertyMetadata(0));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Table;

    /// <summary>表格列数。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int ColumnCount
    {
        get => (int)GetValue(ColumnCountProperty)!;
        set => SetValue(ColumnCountProperty, value);
    }

    /// <summary>表格行数（含表头行）。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int RowCount
    {
        get => (int)GetValue(RowCountProperty)!;
        set => SetValue(RowCountProperty, value);
    }
}

/// <summary>
/// 承载一个 Markdown 表格单元格。
/// </summary>
public class MarkdownTableCellPresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="IsHeaderCell"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IsHeaderCellProperty =
        DependencyProperty.Register(nameof(IsHeaderCell), typeof(bool), typeof(MarkdownTableCellPresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="RowIndex"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RowIndexProperty =
        DependencyProperty.Register(nameof(RowIndex), typeof(int), typeof(MarkdownTableCellPresenter),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the <see cref="ColumnIndex"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ColumnIndexProperty =
        DependencyProperty.Register(nameof(ColumnIndex), typeof(int), typeof(MarkdownTableCellPresenter),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the <see cref="ColumnAlignment"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnAlignmentProperty =
        DependencyProperty.Register(nameof(ColumnAlignment), typeof(HorizontalAlignment), typeof(MarkdownTableCellPresenter),
            new PropertyMetadata(HorizontalAlignment.Stretch));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.TableCell;

    /// <summary>
    /// 本列的水平对齐，来自 GFM 分隔行里的冒号（<c>:--</c>、<c>:-:</c>、<c>--:</c>）。
    /// 没写冒号时是 <see cref="HorizontalAlignment.Stretch"/>，即沿用默认排布。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public HorizontalAlignment ColumnAlignment
    {
        get => (HorizontalAlignment)GetValue(ColumnAlignmentProperty)!;
        set => SetValue(ColumnAlignmentProperty, value);
    }

    /// <summary>是否为表头单元格。默认样式据此加粗并铺表头底色。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public bool IsHeaderCell
    {
        get => (bool)GetValue(IsHeaderCellProperty)!;
        set => SetValue(IsHeaderCellProperty, value);
    }

    /// <summary>单元格所在行索引（含表头行）。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int RowIndex
    {
        get => (int)GetValue(RowIndexProperty)!;
        set => SetValue(RowIndexProperty, value);
    }

    /// <summary>单元格所在列索引。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int ColumnIndex
    {
        get => (int)GetValue(ColumnIndexProperty)!;
        set => SetValue(ColumnIndexProperty, value);
    }
}

/// <summary>
/// 承载一张独占一行的 Markdown 图片（<c>![alt](src "title")</c>）。
/// </summary>
/// <remarks>
/// 图片本身交给模板里的 <see cref="Image"/> 部件，加载、解码与失败处理都沿用它那一套。
/// 目标地址解析不出可用的绝对 URI 时（例如没有设 <see cref="Markdown.BaseUri"/> 的相对路径），
/// <see cref="HasSource"/> 为 <see langword="false"/>，默认模板改为显示 <see cref="Alt"/> 文本。
/// </remarks>
public class MarkdownImagePresenter : MarkdownBlockPresenter
{
    private static readonly DependencyPropertyKey HasSourcePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasSource), typeof(bool), typeof(MarkdownImagePresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="Source"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(MarkdownImagePresenter),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the <see cref="Alt"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty AltProperty =
        DependencyProperty.Register(nameof(Alt), typeof(string), typeof(MarkdownImagePresenter),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="ImageTarget"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ImageTargetProperty =
        DependencyProperty.Register(nameof(ImageTarget), typeof(string), typeof(MarkdownImagePresenter),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="Caption"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(MarkdownImagePresenter),
            new PropertyMetadata(string.Empty, OnCaptionChanged));

    /// <summary>
    /// Identifies the <see cref="CaptionForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaptionForegroundProperty =
        DependencyProperty.Register(nameof(CaptionForeground), typeof(Brush), typeof(MarkdownImagePresenter),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="HasSource"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HasSourceProperty = HasSourcePropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasCaptionPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(HasCaption), typeof(bool), typeof(MarkdownImagePresenter),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="HasCaption"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HasCaptionProperty = HasCaptionPropertyKey.DependencyProperty;

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Image;

    /// <summary>图片源。为 <see langword="null"/> 时默认模板改显 <see cref="Alt"/>。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>图片的替代文本，即 <c>![这里](…)</c> 的内容。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Alt
    {
        get => (string)GetValue(AltProperty)!;
        set => SetValue(AltProperty, value);
    }

    /// <summary>源文档里写的目标地址原文（未解析成绝对 URI 的那一份）。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string ImageTarget
    {
        get => (string)GetValue(ImageTargetProperty)!;
        set => SetValue(ImageTargetProperty, value);
    }

    /// <summary>图注，来自 <c>![alt](src "这里")</c>。为空时默认模板不显示图注行。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Caption
    {
        get => (string)GetValue(CaptionProperty)!;
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>图注与替代文本的前景画刷。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CaptionForeground
    {
        get => (Brush?)GetValue(CaptionForegroundProperty);
        set => SetValue(CaptionForegroundProperty, value);
    }

    /// <summary>是否有可用的图片源。默认模板用它在图片与替代文本之间切换。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public bool HasSource
    {
        get => (bool)GetValue(HasSourceProperty)!;
        private set => SetValue(HasSourcePropertyKey, value);
    }

    /// <summary>是否有图注。默认模板用它决定要不要占一行显示图注。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public bool HasCaption
    {
        get => (bool)GetValue(HasCaptionProperty)!;
        private set => SetValue(HasCaptionPropertyKey, value);
    }

    /// <summary>
    /// 弱引用转发 <see cref="ImageSource.LoadFailed"/>：图片源常常是共享的长寿对象，
    /// 直接挂事件会把它变成呈现器的强引用根。
    /// </summary>
    private sealed class SourceFailureListener
    {
        private readonly WeakReference<MarkdownImagePresenter> _owner;

        public SourceFailureListener(MarkdownImagePresenter owner) =>
            _owner = new WeakReference<MarkdownImagePresenter>(owner);

        public void OnLoadFailed(ImageSource source, Exception error)
        {
            if (_owner.TryGetTarget(out var owner))
            {
                owner.HasSource = false;
            }
            else
            {
                source.LoadFailed -= OnLoadFailed;
            }
        }
    }

    private SourceFailureListener? _failureListener;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not MarkdownImagePresenter presenter)
        {
            return;
        }

        if (e.OldValue is ImageSource oldSource && presenter._failureListener != null)
        {
            oldSource.LoadFailed -= presenter._failureListener.OnLoadFailed;
        }

        presenter._failureListener = null;

        if (e.NewValue is not ImageSource source)
        {
            presenter.HasSource = false;
            return;
        }

        // 已经失败过的源不必再等一次事件——坏地址就直接显示替代文本。
        presenter.HasSource = source.LoadFailure == null;
        presenter._failureListener = new SourceFailureListener(presenter);
        source.LoadFailed += presenter._failureListener.OnLoadFailed;
    }

    private static void OnCaptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownImagePresenter presenter)
        {
            presenter.HasCaption = !string.IsNullOrEmpty(e.NewValue as string);
        }
    }
}

/// <summary>
/// 承载一条 Markdown 脚注定义（<c>[^label]: 正文</c>）。
/// </summary>
public class MarkdownFootnotePresenter : MarkdownBlockPresenter
{
    /// <summary>
    /// Identifies the <see cref="Number"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty NumberProperty =
        DependencyProperty.Register(nameof(Number), typeof(int), typeof(MarkdownFootnotePresenter),
            new PropertyMetadata(1, OnNumberChanged));

    /// <summary>
    /// Identifies the <see cref="Label"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MarkdownFootnotePresenter),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey MarkerPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Marker), typeof(string), typeof(MarkdownFootnotePresenter),
            new PropertyMetadata("1."));

    /// <summary>
    /// Identifies the <see cref="Marker"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MarkerProperty = MarkerPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the <see cref="MarkerForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MarkerForegroundProperty =
        DependencyProperty.Register(nameof(MarkerForeground), typeof(Brush), typeof(MarkdownFootnotePresenter),
            new PropertyMetadata(null));

    /// <inheritdoc />
    public override MarkdownBlockKind BlockKind => MarkdownBlockKind.Footnote;

    /// <summary>脚注序号，按定义在文档中出现的先后从 1 开始。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int Number
    {
        get => (int)GetValue(NumberProperty)!;
        set => SetValue(NumberProperty, value);
    }

    /// <summary>脚注标签，即 <c>[^这里]</c>。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Label
    {
        get => (string)GetValue(LabelProperty)!;
        set => SetValue(LabelProperty, value);
    }

    /// <summary>算好的前导标记文本，默认模板直接显示它。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Marker
    {
        get => (string)GetValue(MarkerProperty)!;
        private set => SetValue(MarkerPropertyKey, value);
    }

    /// <summary>前导标记的前景画刷。</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? MarkerForeground
    {
        get => (Brush?)GetValue(MarkerForegroundProperty);
        set => SetValue(MarkerForegroundProperty, value);
    }

    private static void OnNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownFootnotePresenter presenter)
        {
            presenter.Marker = $"{presenter.Number}.";
        }
    }
}

/// <summary>
/// 让 <see cref="Markdown"/> 与块级容器把“可继承排版属性变了”这件事推给子树里的呈现器。
/// </summary>
/// <remarks>
/// 依赖属性继承在本框架里是按需向上取值的：祖先的值变化不会回调到后代，
/// 因此持有排版缓存的叶子呈现器必须由持有该值的那一层主动通知。
/// </remarks>
internal static class MarkdownInheritedFormatting
{
    internal static bool AffectsInlineFormatting(DependencyProperty property) =>
        property == Control.ForegroundProperty ||
        property == Control.FontFamilyProperty ||
        property == Control.FontSizeProperty ||
        property == Control.FontWeightProperty ||
        property == Control.FontStyleProperty ||
        property == MarkdownTextPresenter.FontSizeRatioProperty ||
        property == MarkdownTextPresenter.LineHeightRatioProperty ||
        property == MarkdownTextPresenter.MonospaceFontFamilyProperty ||
        property == MarkdownTextPresenter.LinkForegroundProperty ||
        property == MarkdownTextPresenter.InlineCodeForegroundProperty ||
        property == MarkdownTextPresenter.InlineCodeBackgroundProperty ||
        property == MarkdownTextPresenter.BoldStyleProperty ||
        property == MarkdownTextPresenter.ItalicStyleProperty ||
        property == MarkdownTextPresenter.InlineCodeStyleProperty ||
        property == MarkdownTextPresenter.LinkStyleProperty ||
        property == MarkdownTextPresenter.SelectionBrushProperty;

    internal static void InvalidateSubtree(DependencyObject? root)
    {
        if (root == null)
        {
            return;
        }

        switch (root)
        {
            case MarkdownTextPresenter text:
                text.InvalidateFormatting();
                break;
            case MarkdownCodeTextPresenter code:
                code.InvalidateFormatting();
                break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            InvalidateSubtree(VisualTreeHelper.GetChild(root, i));
        }
    }
}
