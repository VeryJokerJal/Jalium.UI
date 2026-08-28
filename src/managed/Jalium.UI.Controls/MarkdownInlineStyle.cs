using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Markdown 行内文本可叠加的装饰线。
/// </summary>
[Flags]
public enum MarkdownTextDecorations
{
    /// <summary>不绘制装饰线。</summary>
    None = 0,

    /// <summary>在文本下方绘制下划线。</summary>
    Underline = 1,

    /// <summary>在文本中部绘制删除线。</summary>
    Strikethrough = 2,

    /// <summary>在文本上方绘制上划线。</summary>
    Overline = 4,
}

/// <summary>
/// 描述一类 Markdown 行内文本（粗体、斜体、行内代码、链接）的排版与装饰。
/// </summary>
/// <remarks>
/// <para>
/// 每个属性都带“未设置”语义：引用类型与可空值类型为 <see langword="null"/>，
/// <see cref="FontSizeRatio"/> 为 <see cref="double.NaN"/>。未设置的部分沿用所在块从可视树
/// 继承下来的排版值（<c>FontFamily</c> / <c>FontSize</c> / <c>FontWeight</c> /
/// <c>FontStyle</c> / <c>Foreground</c>），因此只需要覆盖真正想改的项。
/// </para>
/// <para>
/// 实例按不可变配置使用：赋给 <see cref="MarkdownTextPresenter"/> 或 <see cref="Markdown"/>
/// 之后再修改其属性不会触发重排，需要重新赋值才会生效。
/// </para>
/// </remarks>
public class MarkdownInlineStyle : DependencyObject
{
    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="FontSizeRatio"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontSizeRatioProperty =
        DependencyProperty.Register(nameof(FontSizeRatio), typeof(double), typeof(MarkdownInlineStyle),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the <see cref="FontWeight"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight?), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="FontStyle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle?), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="Background"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="CornerRadius"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(MarkdownInlineStyle),
            new PropertyMetadata(new CornerRadius(0)));

    /// <summary>
    /// Identifies the <see cref="Padding"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(MarkdownInlineStyle),
            new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// Identifies the <see cref="Decorations"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DecorationsProperty =
        DependencyProperty.Register(nameof(Decorations), typeof(MarkdownTextDecorations), typeof(MarkdownInlineStyle),
            new PropertyMetadata(MarkdownTextDecorations.None));

    /// <summary>
    /// Identifies the <see cref="DecorationBrush"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DecorationBrushProperty =
        DependencyProperty.Register(nameof(DecorationBrush), typeof(Brush), typeof(MarkdownInlineStyle),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="DecorationThickness"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DecorationThicknessProperty =
        DependencyProperty.Register(nameof(DecorationThickness), typeof(double), typeof(MarkdownInlineStyle),
            new PropertyMetadata(1.0));

    // 静态字段按声明顺序求值：这几个内置默认实例会调用 SetValue，
    // 因此必须排在上面所有 DependencyProperty 字段之后。
    internal static readonly MarkdownInlineStyle DefaultBold = CreateBold();
    internal static readonly MarkdownInlineStyle DefaultItalic = CreateItalic();
    internal static readonly MarkdownInlineStyle DefaultInlineCode = CreateInlineCode();
    internal static readonly MarkdownInlineStyle DefaultLink = CreateLink();
    internal static readonly MarkdownInlineStyle DefaultStrikethrough = CreateStrikethrough();

    /// <summary>
    /// 覆盖字体族。<see langword="null"/> 时行内代码使用
    /// <see cref="MarkdownTextPresenter.MonospaceFontFamily"/>，其余沿用继承字体。
    /// </summary>
    public FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// 相对于继承字号的缩放倍数。<see cref="double.NaN"/> 表示不缩放。
    /// </summary>
    public double FontSizeRatio
    {
        get => (double)GetValue(FontSizeRatioProperty)!;
        set => SetValue(FontSizeRatioProperty, value);
    }

    /// <summary>
    /// 覆盖字重。<see langword="null"/> 时沿用继承字重。
    /// </summary>
    public FontWeight? FontWeight
    {
        get => (FontWeight?)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// 覆盖字形。<see langword="null"/> 时沿用继承字形。
    /// </summary>
    public FontStyle? FontStyle
    {
        get => (FontStyle?)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// 覆盖前景画刷。<see langword="null"/> 时链接使用
    /// <see cref="MarkdownTextPresenter.LinkForeground"/>、行内代码使用
    /// <see cref="MarkdownTextPresenter.InlineCodeForeground"/>，其余沿用继承前景。
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// 文本背景画刷。<see langword="null"/> 时行内代码使用
    /// <see cref="MarkdownTextPresenter.InlineCodeBackground"/>，其余不绘制背景。
    /// </summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// 背景的圆角半径。
    /// </summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// 文本四周的内边距，会计入行内排版尺寸。
    /// </summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// 需要绘制的装饰线。
    /// </summary>
    public MarkdownTextDecorations Decorations
    {
        get => (MarkdownTextDecorations)GetValue(DecorationsProperty)!;
        set => SetValue(DecorationsProperty, value);
    }

    /// <summary>
    /// 装饰线画刷。<see langword="null"/> 时使用解析后的前景画刷。
    /// </summary>
    public Brush? DecorationBrush
    {
        get => (Brush?)GetValue(DecorationBrushProperty);
        set => SetValue(DecorationBrushProperty, value);
    }

    /// <summary>
    /// 装饰线粗细，单位为设备无关像素。
    /// </summary>
    public double DecorationThickness
    {
        get => (double)GetValue(DecorationThicknessProperty)!;
        set => SetValue(DecorationThicknessProperty, value);
    }

    /// <summary>
    /// 创建与内置默认一致的粗体样式，便于在其基础上微调。
    /// </summary>
    public static MarkdownInlineStyle CreateBold() =>
        new() { FontWeight = FontWeights.Bold };

    /// <summary>
    /// 创建与内置默认一致的斜体样式，便于在其基础上微调。
    /// </summary>
    public static MarkdownInlineStyle CreateItalic() =>
        new() { FontStyle = FontStyles.Italic };

    /// <summary>
    /// 创建与内置默认一致的行内代码样式，便于在其基础上微调。
    /// </summary>
    public static MarkdownInlineStyle CreateInlineCode() =>
        new()
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 4, 2),
        };

    /// <summary>
    /// 创建与内置默认一致的链接样式，便于在其基础上微调。
    /// </summary>
    public static MarkdownInlineStyle CreateLink() =>
        new() { Decorations = MarkdownTextDecorations.Underline };

    /// <summary>
    /// 创建与内置默认一致的删除线样式（GFM 的 <c>~~文本~~</c>），便于在其基础上微调。
    /// </summary>
    public static MarkdownInlineStyle CreateStrikethrough() =>
        new() { Decorations = MarkdownTextDecorations.Strikethrough };
}
