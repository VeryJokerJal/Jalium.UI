using System.Linq;
using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;

internal readonly record struct MarkdownTextStyle(
    bool Bold, bool Italic, bool Code, Uri? LinkUri, bool Strikethrough = false);

/// <summary>行内图片的描述。加载与绘制由 <see cref="MarkdownTextPresenter"/> 用一个内嵌的 <see cref="Image"/> 完成。</summary>
internal sealed record MarkdownInlineImage(Uri? Uri, string Alt, string Target, string? Title);

internal sealed record MarkdownTextSpan(
    string Text, MarkdownTextStyle Style, bool IsLineBreak = false, MarkdownInlineImage? Image = null);

/// <summary>
/// 排版并绘制一段 Markdown 行内文本（普通文字、粗体、斜体、行内代码、链接）。
/// </summary>
/// <remarks>
/// <para>
/// 该元素是 Markdown 块级控件模板中的行内文本承载体：基础排版（<see cref="FontFamily"/>、
/// <see cref="FontSize"/>、<see cref="FontWeight"/>、<see cref="FontStyle"/>、
/// <see cref="Foreground"/>）全部从可视树继承，因此在
/// <c>&lt;Style TargetType="MarkdownHeadingPresenter"&gt;</c> 之类的块级样式里设置字体，
/// 就会直接作用到其中的文字。
/// </para>
/// <para>
/// 各类行内片段的差异由 <see cref="MarkdownInlineStyle"/> 描述，通过
/// <see cref="BoldStyle"/>、<see cref="ItalicStyle"/>、<see cref="InlineCodeStyle"/>、
/// <see cref="LinkStyle"/> 提供；这些属性同样可继承，可以在 <see cref="Markdown"/> 上统一设置，
/// 也可以在单个块级样式里覆盖。与主题相关的颜色则通过 <see cref="LinkForeground"/>、
/// <see cref="InlineCodeForeground"/>、<see cref="InlineCodeBackground"/> 设置，
/// 它们能正常参与 <c>{ThemeResource}</c> 的主题切换。
/// </para>
/// </remarks>
public sealed class MarkdownTextPresenter : FrameworkElement, IMarkdownSelectable
{
    /// <summary>行内代码在未指定等宽字体时使用的字体族名。</summary>
    public const string DefaultMonospaceFontFamilyName = "Cascadia Code";

    private static readonly SolidColorBrush s_fallbackForeground = new(Color.Black);
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(90, 51, 153, 255));
    private static readonly SolidColorBrush s_fallbackLinkForeground = new(Color.FromRgb(0, 102, 204));

    private IReadOnlyList<MarkdownTextSpan> _spans = Array.Empty<MarkdownTextSpan>();
    private MarkdownTextLayout? _cachedLayout;
    private double _cachedWidth = double.NaN;
    /// <summary>文本只在末尾变长了：<see cref="_cachedLayout"/> 仍可用，重排最后一行即可。</summary>
    private bool _pendingExtend;
    private readonly Dictionary<int, ResolvedFormat> _formatCache = new();
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private readonly List<InlineImageHost> _inlineImages = new();
    /// <summary>span 下标 → <see cref="_inlineImages"/> 下标；非图片 span 为 -1。</summary>
    private int[] _spanImageIndex = Array.Empty<int>();

    #region Dependency properties

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(MarkdownTextPresenter),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits, OnFormatChanged));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(MarkdownTextPresenter),
            new PropertyMetadata(14.0, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="FontWeight"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(MarkdownTextPresenter),
            new PropertyMetadata(FontWeights.Normal, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="FontStyle"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(MarkdownTextPresenter),
            new PropertyMetadata(FontStyles.Normal, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(MarkdownTextPresenter),
            new PropertyMetadata(null, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="FontSizeRatio"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeRatioProperty =
        DependencyProperty.Register(nameof(FontSizeRatio), typeof(double), typeof(MarkdownTextPresenter),
            new PropertyMetadata(1.0, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="LineHeightRatio"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightRatioProperty =
        DependencyProperty.Register(nameof(LineHeightRatio), typeof(double), typeof(MarkdownTextPresenter),
            new PropertyMetadata(1.5, OnLayoutOnlyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="MonospaceFontFamily"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty MonospaceFontFamilyProperty =
        DependencyProperty.Register(nameof(MonospaceFontFamily), typeof(FontFamily), typeof(MarkdownTextPresenter),
            new PropertyMetadata(new FontFamily(DefaultMonospaceFontFamilyName), OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="LinkForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LinkForegroundProperty =
        DependencyProperty.Register(nameof(LinkForeground), typeof(Brush), typeof(MarkdownTextPresenter),
            new PropertyMetadata(null, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="InlineCodeForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeForegroundProperty =
        DependencyProperty.Register(nameof(InlineCodeForeground), typeof(Brush), typeof(MarkdownTextPresenter),
            new PropertyMetadata(null, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="InlineCodeBackground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InlineCodeBackgroundProperty =
        DependencyProperty.Register(nameof(InlineCodeBackground), typeof(Brush), typeof(MarkdownTextPresenter),
            new PropertyMetadata(null, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="BoldStyle"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty BoldStyleProperty =
        DependencyProperty.Register(nameof(BoldStyle), typeof(MarkdownInlineStyle), typeof(MarkdownTextPresenter),
            new PropertyMetadata(MarkdownInlineStyle.DefaultBold, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="ItalicStyle"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty ItalicStyleProperty =
        DependencyProperty.Register(nameof(ItalicStyle), typeof(MarkdownInlineStyle), typeof(MarkdownTextPresenter),
            new PropertyMetadata(MarkdownInlineStyle.DefaultItalic, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="InlineCodeStyle"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty InlineCodeStyleProperty =
        DependencyProperty.Register(nameof(InlineCodeStyle), typeof(MarkdownInlineStyle), typeof(MarkdownTextPresenter),
            new PropertyMetadata(MarkdownInlineStyle.DefaultInlineCode, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="StrikethroughStyle"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty StrikethroughStyleProperty =
        DependencyProperty.Register(nameof(StrikethroughStyle), typeof(MarkdownInlineStyle), typeof(MarkdownTextPresenter),
            new PropertyMetadata(MarkdownInlineStyle.DefaultStrikethrough, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="LinkStyle"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LinkStyleProperty =
        DependencyProperty.Register(nameof(LinkStyle), typeof(MarkdownInlineStyle), typeof(MarkdownTextPresenter),
            new PropertyMetadata(MarkdownInlineStyle.DefaultLink, OnFormatChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="TextWrapping"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(MarkdownTextPresenter),
            new PropertyMetadata(TextWrapping.Wrap, OnLayoutOnlyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="PreserveWhitespace"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty PreserveWhitespaceProperty =
        DependencyProperty.Register(nameof(PreserveWhitespace), typeof(bool), typeof(MarkdownTextPresenter),
            new PropertyMetadata(false, OnLayoutOnlyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="SelectionBrush"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(MarkdownTextPresenter),
            new PropertyMetadata(s_defaultSelectionBrush, OnSelectionVisualChanged, null, inherits: true));

    #endregion

    /// <summary>
    /// 初始化 <see cref="MarkdownTextPresenter"/> 的新实例。
    /// </summary>
    public MarkdownTextPresenter()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler), true);
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler), true);
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), true);
        Focusable = false;
    }

    #region CLR accessors

    /// <summary>正文字体族，默认从父级继承。</summary>
    public FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>正文字号，默认从父级继承。</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>正文字重，默认从父级继承。</summary>
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight weight ? weight : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>正文字形，默认从父级继承。</summary>
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle style ? style : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>正文前景画刷，默认从父级继承。</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// 相对继承字号的缩放倍数。标题就是靠它按级别放大字号，同时保持跟随
    /// <see cref="Markdown"/> 的基准字号。
    /// </summary>
    public double FontSizeRatio
    {
        get => (double)GetValue(FontSizeRatioProperty)!;
        set => SetValue(FontSizeRatioProperty, value);
    }

    /// <summary>行高相对字号的倍数。</summary>
    public double LineHeightRatio
    {
        get => (double)GetValue(LineHeightRatioProperty)!;
        set => SetValue(LineHeightRatioProperty, value);
    }

    /// <summary>行内代码使用的等宽字体族。</summary>
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

    /// <summary>行内代码的前景画刷；未设置时沿用继承前景。</summary>
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

    /// <summary>删除线片段（GFM 的 <c>~~文本~~</c>）的行内样式。</summary>
    public MarkdownInlineStyle? StrikethroughStyle
    {
        get => (MarkdownInlineStyle?)GetValue(StrikethroughStyleProperty);
        set => SetValue(StrikethroughStyleProperty, value);
    }

    /// <summary>是否自动换行。</summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>是否保留原始空白（含换行）。</summary>
    public bool PreserveWhitespace
    {
        get => (bool)GetValue(PreserveWhitespaceProperty)!;
        set => SetValue(PreserveWhitespaceProperty, value);
    }

    /// <summary>选区高亮画刷。</summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    #endregion

    internal IReadOnlyList<MarkdownTextSpan> Spans
    {
        get => _spans;
        set
        {
            var next = value ?? Array.Empty<MarkdownTextSpan>();
            // 「纯追加」可以连着攒：A→B、B→C 都是追加，那 A→C 也是追加，所以中途没排过版也不必退回全量。
            if (_cachedLayout != null && IsAppendOnly(next))
            {
                // 排版缓存留着，下一次 EnsureLayout 只重排最后一行。
                _spans = next;
                SyncInlineImages(next);
                _pendingExtend = true;
                InvalidateMeasure();
                InvalidateVisual();
                return;
            }

            _spans = next;
            SyncInlineImages(next);
            InvalidateLayout();
        }
    }

    private bool Wrap => TextWrapping != TextWrapping.NoWrap;

    /// <summary>
    /// 用户点击了其中的链接。
    /// </summary>
    public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextPresenter presenter)
        {
            presenter._formatCache.Clear();
            presenter.InvalidateLayout();
        }
    }

    private static void OnLayoutOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as MarkdownTextPresenter)?.InvalidateLayout();
    }

    private static void OnSelectionVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as MarkdownTextPresenter)?.InvalidateVisual();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Spans.Count == 0)
        {
            return default(Size);
        }

        var widthConstraint = Wrap && !double.IsInfinity(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : double.PositiveInfinity;

        // 图片是异步解码的：尺寸从 0 变成真实值时排版必须作废重来，否则行高永远停在占位状态。
        if (MeasureInlineImages(widthConstraint))
        {
            _cachedLayout = null;
            _pendingExtend = false;
        }

        var layout = EnsureLayout(widthConstraint);
        return new Size(layout.Width, layout.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var widthConstraint = Wrap && finalSize.Width > 0
            ? finalSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);
        ArrangeInlineImages(EnsureLayout(widthConstraint));
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Spans.Count == 0)
        {
            return;
        }
        var dc = drawingContext;

        var layout = EnsureLayout(CurrentWidthConstraint());

        // Pass 1: 片段背景（行内代码等），画在选区之下，选中时仍能看到底色。
        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.ImageIndex >= 0)
                {
                    continue;
                }

                var format = ResolveFormat(placement.Style);
                if (format.Background == null)
                {
                    continue;
                }

                var radius = format.CornerRadius;
                if (radius.TopLeft <= 0 && radius.TopRight <= 0 && radius.BottomLeft <= 0 && radius.BottomRight <= 0)
                {
                    dc.DrawRectangle(format.Background, null, placement.Bounds);
                }
                else
                {
                    dc.DrawRoundedRectangle(format.Background, null, placement.Bounds, radius.TopLeft, radius.TopLeft);
                }
            }
        }

        // Pass 2: 每行一条连续的选区高亮（词间不留缝）。
        if (_selectionEnd > _selectionStart && _selectionStart >= 0 && SelectionBrush != null)
        {
            DrawSelectionHighlight(dc, layout);
        }

        // Pass 3: 文字与装饰线。走合并后的绘制批次而不是逐个片段——见 BuildDrawRuns。
        foreach (var line in layout.Lines)
        {
            foreach (var run in line.Runs)
            {
                var format = ResolveFormat(run.Style);
                dc.DrawText(CreateFormattedText(run.Text, format), new Point(run.TextX, run.TextY));

                if (format.Decorations != MarkdownTextDecorations.None && format.DecorationPen != null)
                {
                    DrawDecorations(dc, format, run.TextX, run.TextY, run.TextWidth, run.TextHeight);
                }
            }
        }
    }

    private static void DrawDecorations(
        DrawingContext dc,
        in ResolvedFormat format,
        double textX,
        double textY,
        double textWidth,
        double textHeight)
    {
        var pen = format.DecorationPen!;
        var right = textX + textWidth;

        if ((format.Decorations & MarkdownTextDecorations.Underline) != 0)
        {
            var y = textY + textHeight - 1;
            dc.DrawLine(pen, new Point(textX, y), new Point(right, y));
        }

        if ((format.Decorations & MarkdownTextDecorations.Strikethrough) != 0)
        {
            var y = textY + (textHeight * 0.58);
            dc.DrawLine(pen, new Point(textX, y), new Point(right, y));
        }

        if ((format.Decorations & MarkdownTextDecorations.Overline) != 0)
        {
            var y = textY + 1;
            dc.DrawLine(pen, new Point(textX, y), new Point(right, y));
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        // 链接显示手型，其余区域可选中，用 I 型光标。
        Cursor = TryGetLinkAt(e.GetPosition(this)) != null ? Jalium.UI.Input.Cursors.Hand : Jalium.UI.Input.Cursors.IBeam;
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        Cursor = Jalium.UI.Input.Cursors.Arrow;
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var uri = TryGetLinkAt(e.GetPosition(this));
        if (uri == null)
        {
            return;
        }

        LinkClicked?.Invoke(this, new MarkdownLinkClickedEventArgs(uri));
        e.Handled = true;
    }

    private Uri? TryGetLinkAt(Point point)
    {
        if (Spans.Count == 0)
        {
            return null;
        }

        var layout = EnsureLayout(CurrentWidthConstraint());

        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.Style.LinkUri != null && placement.Bounds.Contains(point))
                {
                    return placement.Style.LinkUri;
                }
            }
        }

        return null;
    }

    #region Inline format resolution

    private ResolvedFormat ResolveFormat(MarkdownTextStyle style)
    {
        var key = (style.Bold ? 1 : 0) |
                  (style.Italic ? 2 : 0) |
                  (style.Code ? 4 : 0) |
                  (style.LinkUri != null ? 8 : 0) |
                  (style.Strikethrough ? 16 : 0);

        if (_formatCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var inheritedForeground = Foreground ?? s_fallbackForeground;
        var family = FontFamily?.Source is { Length: > 0 } inheritedFamily
            ? inheritedFamily
            : FrameworkElement.DefaultFontFamilyName;
        var size = Math.Max(1, FontSize * NormalizeRatio(FontSizeRatio));
        var weight = FontWeight;
        var fontStyle = FontStyle;
        var foreground = inheritedForeground;
        Brush? background = null;
        var cornerRadius = new CornerRadius(0);
        var padding = new Thickness(0);
        var decorations = MarkdownTextDecorations.None;
        Brush? decorationBrush = null;
        var decorationThickness = 1.0;

        // 角色默认：先按“行内代码 / 链接”给出主题相关的底色与前景，再叠加样式对象。
        if (style.Code)
        {
            family = MonospaceFontFamily?.Source is { Length: > 0 } monoFamily
                ? monoFamily
                : DefaultMonospaceFontFamilyName;
            foreground = InlineCodeForeground ?? foreground;
            background = InlineCodeBackground;
        }

        if (style.LinkUri != null)
        {
            foreground = LinkForeground ?? s_fallbackLinkForeground;
        }

        ApplyInlineStyle(style.Bold ? BoldStyle : null);
        ApplyInlineStyle(style.Italic ? ItalicStyle : null);
        ApplyInlineStyle(style.Code ? InlineCodeStyle : null);
        ApplyInlineStyle(style.Strikethrough ? StrikethroughStyle : null);
        ApplyInlineStyle(style.LinkUri != null ? LinkStyle : null);

        Pen? decorationPen = null;
        if (decorations != MarkdownTextDecorations.None)
        {
            decorationPen = new Pen(decorationBrush ?? foreground, Math.Max(0.5, decorationThickness));
        }

        var resolved = new ResolvedFormat(
            family,
            size,
            weight.ToOpenTypeWeight(),
            fontStyle.ToOpenTypeStyle(),
            foreground,
            background,
            cornerRadius,
            padding,
            decorations,
            decorationPen);

        _formatCache[key] = resolved;
        return resolved;

        void ApplyInlineStyle(MarkdownInlineStyle? inlineStyle)
        {
            if (inlineStyle == null)
            {
                return;
            }

            if (inlineStyle.FontFamily?.Source is { Length: > 0 } overrideFamily)
            {
                family = overrideFamily;
            }

            var ratio = inlineStyle.FontSizeRatio;
            if (!double.IsNaN(ratio) && ratio > 0)
            {
                size = Math.Max(1, size * ratio);
            }

            if (inlineStyle.FontWeight is { } overrideWeight)
            {
                weight = overrideWeight;
            }

            if (inlineStyle.FontStyle is { } overrideStyle)
            {
                fontStyle = overrideStyle;
            }

            if (inlineStyle.Foreground is { } overrideForeground)
            {
                foreground = overrideForeground;
            }

            if (inlineStyle.Background is { } overrideBackground)
            {
                background = overrideBackground;
            }

            // 圆角与内边距按“非零才覆盖”叠加，这样链接样式不会把行内代码的胶囊底压回 0。
            var radius = inlineStyle.CornerRadius;
            if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomLeft > 0 || radius.BottomRight > 0)
            {
                cornerRadius = radius;
            }

            var stylePadding = inlineStyle.Padding;
            if (stylePadding.Left != 0 || stylePadding.Top != 0 || stylePadding.Right != 0 || stylePadding.Bottom != 0)
            {
                padding = stylePadding;
            }

            if (inlineStyle.Decorations != MarkdownTextDecorations.None)
            {
                decorations |= inlineStyle.Decorations;
                decorationThickness = inlineStyle.DecorationThickness;
            }

            if (inlineStyle.DecorationBrush is { } overrideDecorationBrush)
            {
                decorationBrush = overrideDecorationBrush;
            }
        }
    }

    private static double NormalizeRatio(double ratio) =>
        double.IsNaN(ratio) || ratio <= 0 ? 1.0 : ratio;

    #endregion

    /// <summary>诊断计数器：走了几次全量排版。测试用它证明流式追加没有退回全量。</summary>
    internal static long DebugFullLayoutPasses;

    /// <summary>诊断计数器：走了几次增量续排（只重排最后一行）。</summary>
    internal static long DebugIncrementalLayoutPasses;

    /// <summary>诊断计数器：文本测量次数，直接反映排版的实际工作量。</summary>
    internal static long DebugTokenMeasurements;

    /// <summary>当前缓存排版的内容宽度（没有缓存时为 0）。</summary>
    internal double DebugLayoutWidth => _cachedLayout?.Width ?? 0;

    /// <summary>把当前排版结果的绘制批次数量算出来（不真的绘制），用于给「录制成本」定量。</summary>
    internal int DebugDrawRunCount(double widthConstraint)
    {
        var layout = EnsureLayout(widthConstraint);
        var total = 0;
        foreach (var line in layout.Lines)
        {
            total += line.Runs.Count;
        }

        return total;
    }

    /// <summary>把当前排版结果的片段数量算出来——合并前的绘制次数，用作对照。</summary>
    internal int DebugPlacementCount(double widthConstraint)
    {
        var layout = EnsureLayout(widthConstraint);
        var total = 0;
        foreach (var line in layout.Lines)
        {
            total += line.Placements.Count;
        }

        return total;
    }

    private static readonly bool s_mergeDrawRuns =
        !string.Equals(Environment.GetEnvironmentVariable("JALIUM_MARKDOWN_NO_DRAW_MERGE"), "1", StringComparison.Ordinal);

    /// <summary>把排版结果导成可比对的签名：每行的文本、行顶 y、行宽、行高。</summary>
    internal string DebugLayoutSignature(double widthConstraint)
    {
        var layout = EnsureLayout(widthConstraint);
        var builder = new StringBuilder();
        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                builder.Append(placement.Text);
            }

            builder.Append('␞')
                .Append(Math.Round(line.StartY, 2)).Append('/')
                .Append(Math.Round(line.Width, 2)).Append('/')
                .Append(Math.Round(line.Height, 2));

            // 绘制批次也进签名：增量续排复用了整行（含批次），比对能顺带证明批次没排错。
            foreach (var run in line.Runs)
            {
                builder.Append('␟').Append(run.Text).Append('@')
                    .Append(Math.Round(run.TextX, 2)).Append(',')
                    .Append(Math.Round(run.TextY, 2));
            }

            builder.Append('\n');
        }

        builder.Append("total=")
            .Append(Math.Round(layout.Width, 2)).Append('x')
            .Append(Math.Round(layout.Height, 2));
        return builder.ToString();
    }

    /// <summary>
    /// 已缓存的排版能不能直接拿来用在新的宽度约束上。
    /// </summary>
    /// <remarks>
    /// 除了「宽度一样」这个显然的情形，还有一条关键的等价：<b>约束比内容宽时，宽多少都排得一样</b>。
    /// 这一条是必须的——<see cref="MeasureOverride"/> 拿到的是可用宽度，<see cref="ArrangeOverride"/>
    /// 拿到的是最终宽度，而放在横向可滚动的容器里时前者是无穷、后者是内容宽度。两者一旦被当成
    /// 「宽度变了」，每次布局就要全量重排两遍，增量续排从此再也命不中。
    /// </remarks>
    private bool CanReuseLayout(double widthConstraint)
    {
        if (_cachedLayout == null)
        {
            return false;
        }

        if (double.IsInfinity(widthConstraint) && double.IsInfinity(_cachedWidth))
        {
            return true;
        }

        if (Math.Abs(widthConstraint - _cachedWidth) < 0.1)
        {
            return true;
        }

        // 关键判据是「这份排版有没有被宽度挤着换过行」，**不是**「内容宽度有没有超过约束」——
        // 内容宽度总是不超过约束，正因为它已经按那个约束换过行了；据此放宽会让加宽约束后
        // 该重排的不重排（窗口拉宽后文字还挤在原来的窄栏里）。
        // 从没因宽度换过行的排版才真的与更宽的约束等价，此时只需新约束仍容得下内容。
        return !_cachedLayout.WrappedByWidth &&
               (double.IsInfinity(widthConstraint) || widthConstraint >= _cachedLayout.Width - 0.1);
    }

    private MarkdownTextLayout EnsureLayout(double widthConstraint)
    {
        var widthMatches = CanReuseLayout(widthConstraint);

        if (widthMatches)
        {
            if (!_pendingExtend)
            {
                return _cachedLayout!;
            }

            if (TryExtendLayout(_cachedLayout!, widthConstraint) is { } extended)
            {
                _pendingExtend = false;
                _cachedLayout = extended;
                // 缓存此后对应的是这次的约束：续排出来的新行就是按它排的。
                _cachedWidth = widthConstraint;
                Interlocked.Increment(ref DebugIncrementalLayoutPasses);
                return extended;
            }
        }

        _pendingExtend = false;
        _cachedWidth = widthConstraint;
        _cachedLayout = CreateLayout(widthConstraint);
        Interlocked.Increment(ref DebugFullLayoutPasses);
        return _cachedLayout;
    }

    /// <summary>
    /// 文本只是在末尾变长时的续排：保留除最后一行以外的全部已排行，从最后一行的起点重新排到末尾。
    /// </summary>
    /// <remarks>
    /// 之所以只需丢最后一行：换行是自左向右贪心的，一行放几个词只取决于「进入这一行时的剩余宽度」和
    /// 后续 token，而末尾追加的字符碰不到任何一个已经闭合的行。最后一行则可能被继续填满并溢出到新行，
    /// 所以它必须重排。
    /// </remarks>
    private MarkdownTextLayout? TryExtendLayout(MarkdownTextLayout previous, double widthConstraint)
    {
        if (previous.Lines.Count == 0)
        {
            return null;
        }

        var lastLine = previous.Lines[^1];
        if (lastLine.StartSpanIndex >= Spans.Count)
        {
            return null;
        }

        var layout = new MarkdownTextLayout
        {
            // 保留的行是按同一个约束排的，它们当初有没有被宽度挤过要一并继承下来。
            WrappedByWidth = previous.WrappedByWidth,
        };
        for (var index = 0; index < previous.Lines.Count - 1; index++)
        {
            layout.Lines.Add(previous.Lines[index]);
        }

        var maxWidthSoFar = layout.Lines.Count == 0 ? 0 : layout.Lines[^1].MaxWidthThrough;
        FillLayout(layout, widthConstraint, lastLine.StartSpanIndex, lastLine.StartCharOffset, lastLine.StartY, maxWidthSoFar);
        return layout;
    }

    /// <summary>
    /// 判断 <paramref name="next"/> 是不是当前 <see cref="Spans"/> 的「纯末尾追加」——前面的片段逐字相同，
    /// 最后一个共有片段只是变长。流式输出正是这个形状，命中就能走 <see cref="TryExtendLayout"/>。
    /// </summary>
    private bool IsAppendOnly(IReadOnlyList<MarkdownTextSpan> next)
    {
        var current = _spans;
        if (current.Count == 0 || next.Count < current.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count - 1; index++)
        {
            if (!SpanEquals(current[index], next[index]))
            {
                return false;
            }
        }

        var oldLast = current[^1];
        var newLast = next[current.Count - 1];
        return oldLast.Style == newLast.Style &&
               oldLast.IsLineBreak == newLast.IsLineBreak &&
               oldLast.Image == newLast.Image &&
               newLast.Text.StartsWith(oldLast.Text, StringComparison.Ordinal);
    }

    private static bool SpanEquals(MarkdownTextSpan left, MarkdownTextSpan right) =>
        left.IsLineBreak == right.IsLineBreak &&
        left.Style == right.Style &&
        left.Image == right.Image &&
        string.Equals(left.Text, right.Text, StringComparison.Ordinal);

    private MarkdownTextLayout CreateLayout(double widthConstraint)
    {
        var layout = new MarkdownTextLayout();
        FillLayout(layout, widthConstraint, startSpanIndex: 0, startCharOffset: 0, startY: 0, maxWidthSoFar: 0);
        return layout;
    }

    /// <summary>
    /// 从给定的来源位置续排到末尾，把产出的行追加进 <paramref name="layout"/>。全量排版就是「从 (0,0)
    /// 排进一个空 layout」；增量续排是「丢掉最后一行，从它的起点排进保留了前面行的 layout」。
    /// </summary>
    private void FillLayout(
        MarkdownTextLayout layout,
        double widthConstraint,
        int startSpanIndex,
        int startCharOffset,
        double startY,
        double maxWidthSoFar)
    {
        var maxWidth = double.IsInfinity(widthConstraint) || widthConstraint <= 0
            ? double.PositiveInfinity
            : widthConstraint;
        var currentLine = new MarkdownTextLine();
        var y = startY;
        var runningMaxWidth = maxWidthSoFar;

        foreach (var token in Tokenize(startSpanIndex, startCharOffset))
        {
            if (token.IsLineBreak)
            {
                CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: true);
                continue;
            }

            AddToken(layout, ref currentLine, token, maxWidth, ref y, ref runningMaxWidth);
        }

        CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: false);
        layout.Height = y;
        layout.Width = runningMaxWidth;
    }

    private void AddToken(
        MarkdownTextLayout layout,
        ref MarkdownTextLine currentLine,
        MarkdownToken token,
        double maxWidth,
        ref double y,
        ref double runningMaxWidth)
    {
        if (token.ImageIndex >= 0)
        {
            var imageMeasurement = MeasureImageToken(token);
            if (Wrap &&
                !double.IsInfinity(maxWidth) &&
                currentLine.Width > 0 &&
                currentLine.Width + imageMeasurement.TotalWidth > maxWidth)
            {
                layout.WrappedByWidth = true;
                CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: false);
            }

            PlaceToken(ref currentLine, token.Text, token.Style, imageMeasurement, token.SpanIndex, token.CharOffset, token.ImageIndex);
            return;
        }

        if (string.IsNullOrEmpty(token.Text))
        {
            return;
        }

        if (token.IsWhitespace && currentLine.Placements.Count == 0 && !PreserveWhitespace)
        {
            return;
        }

        var measurement = MeasureToken(token.Text, token.Style);
        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            !token.IsWhitespace &&
            currentLine.Width > 0 &&
            currentLine.Width + measurement.TotalWidth > maxWidth)
        {
            layout.WrappedByWidth = true;
            CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: false);
        }

        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            !token.IsWhitespace &&
            measurement.TotalWidth > maxWidth)
        {
            layout.WrappedByWidth = true;
            AddWrappedToken(layout, ref currentLine, token, maxWidth, ref y, ref runningMaxWidth);
            return;
        }

        if (Wrap &&
            !double.IsInfinity(maxWidth) &&
            token.IsWhitespace &&
            currentLine.Width + measurement.TotalWidth > maxWidth)
        {
            // 行尾空白被宽度吃掉，同样是这份排版依赖了具体约束。
            layout.WrappedByWidth = true;
            return;
        }

        PlaceToken(ref currentLine, token.Text, token.Style, measurement, token.SpanIndex, token.CharOffset);
    }

    private void AddWrappedToken(
        MarkdownTextLayout layout,
        ref MarkdownTextLine currentLine,
        MarkdownToken token,
        double maxWidth,
        ref double y,
        ref double runningMaxWidth)
    {
        // 一个词本身就比一行宽（长 URL、无空格的 CJK 长串）时按字符切断。用二分找「还放得下的最长前缀」，
        // 逐字符试探是 O(len²) 次文本测量——一条几千字符的长串足以让一次排版卡住整个 UI 线程。
        var text = token.Text;
        var start = 0;
        while (start < text.Length)
        {
            var available = maxWidth - currentLine.Width;
            var fit = FindLongestFittingPrefix(text, start, token.Style, available);
            if (fit == 0)
            {
                if (currentLine.Placements.Count > 0)
                {
                    // 本行已被占用，换行后整行宽度可用，再试一次。
                    CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: false);
                    continue;
                }

                // 空行都放不下一个字符：强制放一个，避免死循环。
                fit = 1;
            }

            var chunk = text.Substring(start, fit);
            PlaceToken(ref currentLine, chunk, token.Style, MeasureToken(chunk, token.Style), token.SpanIndex, token.CharOffset + start);
            start += fit;

            if (start < text.Length)
            {
                CommitLine(layout, ref currentLine, ref y, ref runningMaxWidth, forceEmptyLine: false);
            }
        }
    }

    /// <summary>
    /// 二分出 <c>text[start..]</c> 中总宽度不超过 <paramref name="available"/> 的最长前缀长度。
    /// </summary>
    private int FindLongestFittingPrefix(string text, int start, MarkdownTextStyle style, double available)
    {
        var remaining = text.Length - start;
        if (remaining <= 0 || available <= 0)
        {
            return 0;
        }

        var low = 0;
        var high = remaining;
        while (low < high)
        {
            var mid = low + ((high - low + 1) / 2);
            if (MeasureToken(text.Substring(start, mid), style).TotalWidth <= available)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return low;
    }

    private void PlaceToken(
        ref MarkdownTextLine currentLine,
        string text,
        MarkdownTextStyle style,
        MarkdownTokenMeasurement measurement,
        int spanIndex,
        int charOffset,
        int imageIndex = -1)
    {
        if (currentLine.Placements.Count == 0)
        {
            currentLine.StartSpanIndex = spanIndex;
            currentLine.StartCharOffset = charOffset;
        }

        currentLine.Placements.Add(new MarkdownTokenPlacement(
            text,
            style,
            new Rect(currentLine.Width, 0, measurement.TotalWidth, measurement.TotalHeight),
            measurement.TextWidth,
            measurement.TextHeight,
            measurement.TextOffsetX,
            imageIndex));
        currentLine.Width += measurement.TotalWidth;
        currentLine.Height = Math.Max(currentLine.Height, measurement.TotalHeight);
    }

    private void CommitLine(
        MarkdownTextLayout layout,
        ref MarkdownTextLine currentLine,
        ref double y,
        ref double runningMaxWidth,
        bool forceEmptyLine)
    {
        if (currentLine.Placements.Count == 0)
        {
            if (forceEmptyLine || layout.Lines.Count == 0)
            {
                y += DefaultLineHeight;
            }
            currentLine = new MarkdownTextLine();
            return;
        }

        var placements = new MarkdownTokenPlacement[currentLine.Placements.Count];
        for (var index = 0; index < currentLine.Placements.Count; index++)
        {
            var placement = currentLine.Placements[index];
            placements[index] = placement with
            {
                Bounds = new Rect(placement.Bounds.X, y, placement.Bounds.Width, currentLine.Height)
            };
        }

        // 行尾空白不计进行宽——和 WPF 的 TextBlock 一样，否则 DesiredSize 会被看不见的空格撑大。
        // 只有 token 里带着空白的情况（PreserveWhitespace）才原样保留。
        var lineWidth = PreserveWhitespace ? currentLine.Width : TrimTrailingWhitespaceWidth(placements, currentLine.Width);

        runningMaxWidth = Math.Max(runningMaxWidth, lineWidth);
        layout.Lines.Add(new MarkdownTextLineInfo(
            placements,
            lineWidth,
            currentLine.Height,
            currentLine.StartSpanIndex,
            currentLine.StartCharOffset,
            y,
            runningMaxWidth,
            BuildDrawRuns(placements)));
        y += currentLine.Height;
        currentLine = new MarkdownTextLine();
    }

    private static double TrimTrailingWhitespaceWidth(MarkdownTokenPlacement[] placements, double fallback)
    {
        for (var index = placements.Length - 1; index >= 0; index--)
        {
            var placement = placements[index];
            if (placement.ImageIndex >= 0 || !string.IsNullOrWhiteSpace(placement.Text))
            {
                return placement.Bounds.X + placement.Bounds.Width;
            }
        }

        return placements.Length == 0 ? fallback : 0;
    }

    /// <summary>
    /// 把一行里「相邻、同款式、首尾相接」的片段并成一次绘制。
    /// </summary>
    /// <remarks>
    /// 切词是按空白切的，所以一行 20 个词会摊成 40 个片段（词与词间空白各一个），逐个 <c>DrawText</c>
    /// 意味着一次 <see cref="OnRender"/> 要录几千条绘制指令；而流式追加每改一次文本就要重录一整遍。
    /// 合并后每行通常只剩款式段数（一两条）。片段位置本来就是逐个累加测量宽度得到的，合并串的起点
    /// 仍取第一个片段的精确原点，因此只有字距调整会带来亚像素级差异；<see cref="MarkdownTokenPlacement"/>
    /// 本身不变，选区高亮与命中测试仍按原片段边界走。带底色或内边距的片段（行内代码）不参与合并。
    /// 需要排查渲染问题时可用环境变量 <c>JALIUM_MARKDOWN_NO_DRAW_MERGE=1</c> 关掉。
    /// </remarks>
    private IReadOnlyList<MarkdownDrawRun> BuildDrawRuns(MarkdownTokenPlacement[] placements)
    {
        if (placements.Length == 0)
        {
            return Array.Empty<MarkdownDrawRun>();
        }

        var runs = new List<MarkdownDrawRun>();
        var index = 0;
        while (index < placements.Length)
        {
            var first = placements[index];
            if (first.ImageIndex >= 0)
            {
                // 图片由内嵌的 Image 元素自己画；只有还没解码出尺寸时才退回画 alt 文本。
                if (!HasResolvedImage(first.ImageIndex))
                {
                    runs.Add(CreateRun(first, first.Text, first.TextWidth));
                }

                index++;
                continue;
            }

            var format = ResolveFormat(first.Style);
            var canMerge = s_mergeDrawRuns &&
                           format.Background == null &&
                           format.Padding.Left == 0 &&
                           format.Padding.Right == 0;

            var end = index + 1;
            if (canMerge)
            {
                while (end < placements.Length)
                {
                    var previous = placements[end - 1];
                    var next = placements[end];
                    if (next.ImageIndex >= 0 ||
                        next.Style != first.Style ||
                        Math.Abs(next.Bounds.X - (previous.Bounds.X + previous.Bounds.Width)) > 0.01 ||
                        Math.Abs(next.TextHeight - first.TextHeight) > 0.01 ||
                        Math.Abs(next.Bounds.Y - first.Bounds.Y) > 0.01 ||
                        Math.Abs(next.Bounds.Height - first.Bounds.Height) > 0.01)
                    {
                        break;
                    }

                    end++;
                }
            }

            if (end - index == 1)
            {
                runs.Add(CreateRun(first, first.Text, first.TextWidth));
                index = end;
                continue;
            }

            var builder = new StringBuilder();
            var width = 0.0;
            for (var i = index; i < end; i++)
            {
                builder.Append(placements[i].Text);
                width += placements[i].TextWidth;
            }

            runs.Add(CreateRun(first, builder.ToString(), width));
            index = end;
        }

        return runs;
    }

    private static MarkdownDrawRun CreateRun(MarkdownTokenPlacement anchor, string text, double textWidth) =>
        new(text,
            anchor.Style,
            anchor.Bounds.X + anchor.TextOffsetX,
            anchor.Bounds.Y + ((anchor.Bounds.Height - anchor.TextHeight) / 2),
            textWidth,
            anchor.TextHeight);

    /// <summary>
    /// 从 <paramref name="startSpanIndex"/> 的第 <paramref name="startCharOffset"/> 个字符起切词。
    /// 全量排版传 (0, 0)；增量续排传上一次记下的行起点。
    /// </summary>
    private IEnumerable<MarkdownToken> Tokenize(int startSpanIndex, int startCharOffset)
    {
        var spans = Spans;
        for (var spanIndex = Math.Max(0, startSpanIndex); spanIndex < spans.Count; spanIndex++)
        {
            var span = spans[spanIndex];
            var offset = spanIndex == startSpanIndex ? Math.Max(0, startCharOffset) : 0;

            if (span.IsLineBreak)
            {
                if (offset == 0)
                {
                    yield return new MarkdownToken(string.Empty, span.Style, IsWhitespace: false, IsLineBreak: true, spanIndex, 0);
                }
                continue;
            }

            if (span.Image != null)
            {
                if (offset == 0)
                {
                    yield return new MarkdownToken(
                        span.Text, span.Style, IsWhitespace: false, IsLineBreak: false, spanIndex, 0,
                        ImageIndex: spanIndex < _spanImageIndex.Length ? _spanImageIndex[spanIndex] : -1);
                }
                continue;
            }

            var preserveWhitespace = PreserveWhitespace || span.Style.Code;
            foreach (var token in TokenizeSpan(span.Text, span.Style, preserveWhitespace, spanIndex, offset))
            {
                yield return token;
            }
        }
    }

    private static IEnumerable<MarkdownToken> TokenizeSpan(
        string text,
        MarkdownTextStyle style,
        bool preserveWhitespace,
        int spanIndex,
        int startCharOffset)
    {
        if (string.IsNullOrEmpty(text) || startCharOffset >= text.Length)
        {
            yield break;
        }

        if (preserveWhitespace)
        {
            var buffer = new StringBuilder();
            var bufferStart = startCharOffset;
            bool? isWhitespace = null;
            for (var index = startCharOffset; index < text.Length; index++)
            {
                var ch = text[index];
                if (ch == '\r')
                {
                    continue;
                }

                if (ch == '\n')
                {
                    if (buffer.Length > 0)
                    {
                        yield return new MarkdownToken(buffer.ToString(), style, isWhitespace == true, IsLineBreak: false, spanIndex, bufferStart);
                        buffer.Clear();
                        isWhitespace = null;
                    }

                    yield return new MarkdownToken(string.Empty, style, IsWhitespace: false, IsLineBreak: true, spanIndex, index);
                    bufferStart = index + 1;
                    continue;
                }

                var whitespace = ch == ' ' || ch == '\t';
                if (isWhitespace != null && isWhitespace != whitespace)
                {
                    yield return new MarkdownToken(buffer.ToString(), style, isWhitespace == true, IsLineBreak: false, spanIndex, bufferStart);
                    buffer.Clear();
                    bufferStart = index;
                }

                if (buffer.Length == 0)
                {
                    bufferStart = index;
                }

                isWhitespace = whitespace;
                buffer.Append(ch);
            }

            if (buffer.Length > 0)
            {
                yield return new MarkdownToken(buffer.ToString(), style, isWhitespace == true, IsLineBreak: false, spanIndex, bufferStart);
            }

            yield break;
        }

        var word = new StringBuilder();
        var wordStart = startCharOffset;
        var pendingWhitespace = false;
        var whitespaceStart = startCharOffset;
        for (var index = startCharOffset; index < text.Length; index++)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
            {
                if (word.Length > 0)
                {
                    yield return new MarkdownToken(word.ToString(), style, IsWhitespace: false, IsLineBreak: false, spanIndex, wordStart);
                    word.Clear();
                }

                if (!pendingWhitespace)
                {
                    whitespaceStart = index;
                    pendingWhitespace = true;
                }

                continue;
            }

            if (pendingWhitespace)
            {
                yield return new MarkdownToken(" ", style, IsWhitespace: true, IsLineBreak: false, spanIndex, whitespaceStart);
                pendingWhitespace = false;
            }

            if (word.Length == 0)
            {
                wordStart = index;
            }

            word.Append(ch);
        }

        if (word.Length > 0)
        {
            yield return new MarkdownToken(word.ToString(), style, IsWhitespace: false, IsLineBreak: false, spanIndex, wordStart);
        }
        else if (pendingWhitespace)
        {
            yield return new MarkdownToken(" ", style, IsWhitespace: true, IsLineBreak: false, spanIndex, whitespaceStart);
        }
    }

    /// <summary>一条绘制批次的位置与内容，供排版自检使用。</summary>
    internal readonly record struct MarkdownDrawRunInfo(string Text, MarkdownTextStyle Style, double X, double Width);

    /// <summary>
    /// 按当前宽度排一次版，把每行的绘制批次摊平返回。
    /// </summary>
    /// <remarks>
    /// 绘制批次的 x 与宽度是「片段宽度累加」的结果，而真正画到屏幕上的是把批次文本交给
    /// <c>DrawText</c>。两者一旦对不上，换款式的地方就会重叠——这个钩子让回归测试能直接比对
    /// 「声明的宽度」与「实测的宽度」，不必去反射内部排版结构。
    /// </remarks>
    internal IReadOnlyList<MarkdownDrawRunInfo> DebugGetDrawRuns()
    {
        if (Spans.Count == 0)
        {
            return Array.Empty<MarkdownDrawRunInfo>();
        }

        var result = new List<MarkdownDrawRunInfo>();
        foreach (var line in EnsureLayout(CurrentWidthConstraint()).Lines)
        {
            foreach (var run in line.Runs)
            {
                result.Add(new MarkdownDrawRunInfo(run.Text, run.Style, run.TextX, run.TextWidth));
            }
        }

        return result;
    }

    /// <summary>把一段文本按给定款式实测一次宽度，用于与绘制批次声明的宽度对账。</summary>
    internal double DebugMeasureRunWidth(string text, MarkdownTextStyle style) =>
        MeasureToken(text, style).TextWidth;

    #region Inline images

    /// <summary>
    /// 让内嵌的 <see cref="Image"/> 元素与当前 <see cref="Spans"/> 中的图片一一对应。
    /// 目标不变的元素原样留用，这样流式追加时已经下载好的图片不会被重新拉一遍。
    /// </summary>
    private void SyncInlineImages(IReadOnlyList<MarkdownTextSpan> spans)
    {
        var map = _spanImageIndex.Length == spans.Count ? _spanImageIndex : new int[spans.Count];
        var count = 0;

        for (var index = 0; index < spans.Count; index++)
        {
            var image = spans[index].Image;
            if (image == null)
            {
                map[index] = -1;
                continue;
            }

            map[index] = count;
            if (count < _inlineImages.Count)
            {
                var existing = _inlineImages[count];
                if (!string.Equals(existing.Model.Target, image.Target, StringComparison.Ordinal))
                {
                    existing.Element.Source = CreateImageSource(image);
                    existing.LastDesiredSize = default;
                }

                existing.Model = image;
            }
            else
            {
                var element = new Image
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    Source = CreateImageSource(image),
                };
                AddVisualChild(element);
                _inlineImages.Add(new InlineImageHost { Model = image, Element = element });
            }

            count++;
        }

        for (var index = _inlineImages.Count - 1; index >= count; index--)
        {
            RemoveVisualChild(_inlineImages[index].Element);
            _inlineImages.RemoveAt(index);
        }

        _spanImageIndex = map;
    }

    private static ImageSource? CreateImageSource(MarkdownInlineImage image)
    {
        if (image.Uri == null || !image.Uri.IsAbsoluteUri)
        {
            return null;
        }

        try
        {
            return new BitmapImage(image.Uri);
        }
        catch (Exception)
        {
            // 坏地址、不支持的协议：当作没有图片，回落到 alt 文本。
            return null;
        }
    }

    /// <summary>测量全部行内图片，返回是否有尺寸发生了变化（异步解码完成的信号）。</summary>
    private bool MeasureInlineImages(double widthConstraint)
    {
        if (_inlineImages.Count == 0)
        {
            return false;
        }

        var slot = new Size(
            double.IsInfinity(widthConstraint) || widthConstraint <= 0 ? double.PositiveInfinity : widthConstraint,
            double.PositiveInfinity);

        var changed = false;
        foreach (var host in _inlineImages)
        {
            host.Element.Measure(slot);
            if (host.Element.DesiredSize != host.LastDesiredSize)
            {
                host.LastDesiredSize = host.Element.DesiredSize;
                changed = true;
            }
        }

        return changed;
    }

    private void ArrangeInlineImages(MarkdownTextLayout layout)
    {
        if (_inlineImages.Count == 0)
        {
            return;
        }

        Span<bool> arranged = _inlineImages.Count <= 64
            ? stackalloc bool[_inlineImages.Count]
            : new bool[_inlineImages.Count];

        foreach (var line in layout.Lines)
        {
            foreach (var placement in line.Placements)
            {
                if (placement.ImageIndex < 0 || placement.ImageIndex >= _inlineImages.Count)
                {
                    continue;
                }

                var host = _inlineImages[placement.ImageIndex];
                var size = host.LastDesiredSize;
                host.Element.Arrange(new Rect(
                    placement.Bounds.X,
                    placement.Bounds.Y + Math.Max(0, (placement.Bounds.Height - size.Height) / 2),
                    size.Width,
                    size.Height));
                arranged[placement.ImageIndex] = true;
            }
        }

        // 没进这份排版的图片收一个零矩形，免得它停在上一次的位置上继续画。
        for (var index = 0; index < _inlineImages.Count; index++)
        {
            if (!arranged[index])
            {
                _inlineImages[index].Element.Arrange(default(Rect));
            }
        }
    }

    private bool HasResolvedImage(int imageIndex) =>
        imageIndex >= 0 &&
        imageIndex < _inlineImages.Count &&
        _inlineImages[imageIndex].LastDesiredSize.Width > 0 &&
        _inlineImages[imageIndex].LastDesiredSize.Height > 0;

    /// <summary>
    /// 图片 token 的尺寸取自它的 <see cref="Image"/> 元素。还没解码出来时按 alt 文本占位——
    /// 解码完成后 <see cref="MeasureOverride"/> 会看到尺寸变化并作废排版，位置随即补正。
    /// </summary>
    private MarkdownTokenMeasurement MeasureImageToken(MarkdownToken token)
    {
        if (HasResolvedImage(token.ImageIndex))
        {
            var size = _inlineImages[token.ImageIndex].LastDesiredSize;
            return new MarkdownTokenMeasurement(size.Width, size.Height, size.Width, size.Height, 0);
        }

        return MeasureToken(token.Text.Length > 0 ? token.Text : " ", token.Style);
    }

    #endregion

    /// <summary>
    /// 测一个 token 占多宽。
    /// </summary>
    /// <remarks>
    /// ★用 <see cref="FormattedText.WidthIncludingTrailingWhitespace"/> 而不是
    /// <see cref="FormattedText.Width"/>：后者是 WPF 语义的「不含尾随空白」宽度，对一个纯空格的 token
    /// 直接给 0。而这里的 token 是排版单位，词与词之间的空白本身就是独立 token——宽度算成 0，
    /// 后面每个片段的 x 就都少了一截。
    /// <para>
    /// 这个错位一整行同款式时看不出来：<see cref="BuildDrawRuns"/> 会把相邻同款式片段并成一次
    /// <c>DrawText</c>，空格照样画得出来。但换款式的地方（链接、粗体、行内代码）绘制批次断开，
    /// 下一批的起点仍按 0 宽空格累加出来，于是它的头几个字直接叠在上一批的尾巴上。
    /// </para>
    /// </remarks>
    private MarkdownTokenMeasurement MeasureToken(string text, MarkdownTextStyle style)
    {
        Interlocked.Increment(ref DebugTokenMeasurements);
        var format = ResolveFormat(style);
        var formattedText = CreateFormattedText(text, format);
        TextMeasurement.MeasureText(formattedText);

        var width = formattedText.WidthIncludingTrailingWhitespace;
        var horizontalPadding = format.Padding.Left + format.Padding.Right;
        var verticalPadding = format.Padding.Top + format.Padding.Bottom;
        var totalHeight = Math.Max(DefaultLineHeight, formattedText.Height + verticalPadding);

        return new MarkdownTokenMeasurement(
            width + horizontalPadding,
            totalHeight,
            width,
            formattedText.Height,
            format.Padding.Left);
    }

    private static FormattedText CreateFormattedText(string text, in ResolvedFormat format)
    {
        return new FormattedText(text, format.FontFamily, format.FontSize)
        {
            Foreground = format.Foreground,
            FontWeight = format.OpenTypeWeight,
            FontStyle = format.OpenTypeStyle,
        };
    }

    private double DefaultLineHeight
    {
        get
        {
            var size = Math.Max(1, FontSize * NormalizeRatio(FontSizeRatio));
            var ratio = LineHeightRatio;
            return Math.Max(1, size * (double.IsNaN(ratio) || ratio <= 0 ? 1.5 : ratio));
        }
    }

    /// <summary>
    /// 丢弃已解析的行内格式与排版缓存。属性继承是按需向上取值的，祖先上的可继承排版属性
    /// 变化不会回调到这里，由 <see cref="Markdown"/> 与 <see cref="MarkdownBlockPresenter"/>
    /// 在自己的属性变化时主动调用。
    /// </summary>
    internal void InvalidateFormatting()
    {
        _formatCache.Clear();
        InvalidateLayout();
    }

    private void InvalidateLayout()
    {
        _cachedLayout = null;
        _cachedWidth = double.NaN;
        _pendingExtend = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    #region Text selection (IMarkdownSelectable)

    private double CurrentWidthConstraint()
        => Wrap && RenderSize.Width > 0
            ? RenderSize.Width
            : (Wrap && DesiredSize.Width > 0 ? DesiredSize.Width : double.PositiveInfinity);

    int IMarkdownSelectable.SelectableLength
    {
        get
        {
            if (Spans.Count == 0)
            {
                return 0;
            }
            return ComputeLength(EnsureLayout(CurrentWidthConstraint()));
        }
    }

    string IMarkdownSelectable.GetSelectionText(int start, int end)
    {
        if (Spans.Count == 0)
        {
            return string.Empty;
        }

        var text = BuildVisualText(EnsureLayout(CurrentWidthConstraint()));
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        return end > start ? text.Substring(start, end - start) : string.Empty;
    }

    void IMarkdownSelectable.SetSelectionRange(int start, int end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (_selectionStart == start && _selectionEnd == end)
        {
            return;
        }

        _selectionStart = start;
        _selectionEnd = end;
        InvalidateVisual();
    }

    void IMarkdownSelectable.ClearSelectionRange()
    {
        if (_selectionStart < 0 && _selectionEnd < 0)
        {
            return;
        }

        _selectionStart = -1;
        _selectionEnd = -1;
        InvalidateVisual();
    }

    bool IMarkdownSelectable.TryHitTestCharacter(Point localPoint, out int charIndex)
    {
        charIndex = 0;
        if (Spans.Count == 0)
        {
            return false;
        }

        var layout = EnsureLayout(CurrentWidthConstraint());
        if (layout.Lines.Count == 0)
        {
            return false;
        }

        var lineIndex = -1;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            var placements = layout.Lines[i].Placements;
            if (placements.Count == 0)
            {
                continue;
            }

            var bounds = placements[0].Bounds;
            if (localPoint.Y < bounds.Y)
            {
                lineIndex = i;
                break;
            }
            if (localPoint.Y <= bounds.Y + bounds.Height)
            {
                lineIndex = i;
                break;
            }
        }

        if (lineIndex < 0)
        {
            lineIndex = layout.Lines.Count - 1;
        }

        var lineStart = 0;
        for (var i = 0; i < lineIndex; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                lineStart += placement.Text.Length;
            }
            lineStart += 1; // 行间的换行符
        }

        var line = layout.Lines[lineIndex];
        var local = 0;
        foreach (var placement in line.Placements)
        {
            var left = placement.Bounds.X;
            var right = placement.Bounds.X + placement.Bounds.Width;
            if (localPoint.X < left)
            {
                charIndex = lineStart + local;
                return true;
            }
            if (localPoint.X <= right)
            {
                var textLeft = placement.Bounds.X + placement.TextOffsetX;
                charIndex = lineStart + local + FindCharInText(placement.Text, placement.Style, localPoint.X - textLeft);
                return true;
            }
            local += placement.Text.Length;
        }

        charIndex = lineStart + local;
        return true;
    }

    private void DrawSelectionHighlight(DrawingContext dc, MarkdownTextLayout layout)
    {
        var running = 0;
        for (var li = 0; li < layout.Lines.Count; li++)
        {
            var line = layout.Lines[li];
            var hasSelection = false;
            double left = 0, right = 0, top = 0, height = 0;

            foreach (var placement in line.Placements)
            {
                var cs = running;
                var ce = running + placement.Text.Length;
                var a = Math.Max(_selectionStart, cs);
                var b = Math.Min(_selectionEnd, ce);
                if (a < b)
                {
                    // 整片段被选中时直接用它的外框（把词间步进和行内代码内边距一起盖住，不留缝），
                    // 只有首尾部分选中的片段才回退到按前缀宽度测量。
                    var textLeft = placement.Bounds.X + placement.TextOffsetX;
                    var startX = a == cs
                        ? placement.Bounds.X
                        : textLeft + MeasurePrefixWidth(placement.Text, a - cs, placement.Style);
                    var endX = b == ce
                        ? placement.Bounds.X + placement.Bounds.Width
                        : textLeft + MeasurePrefixWidth(placement.Text, b - cs, placement.Style);

                    if (!hasSelection)
                    {
                        hasSelection = true;
                        left = startX;
                        top = placement.Bounds.Y;
                        height = placement.Bounds.Height;
                    }
                    else
                    {
                        left = Math.Min(left, startX);
                        top = Math.Min(top, placement.Bounds.Y);
                        height = Math.Max(height, placement.Bounds.Height);
                    }
                    right = Math.Max(right, endX);
                }
                running = ce;
            }

            if (hasSelection)
            {
                dc.DrawRectangle(SelectionBrush, null, new Rect(left, top, Math.Max(1, right - left), height));
            }

            if (li < layout.Lines.Count - 1)
            {
                running += 1;
            }
        }
    }

    private int FindCharInText(string text, MarkdownTextStyle style, double targetX)
    {
        if (text.Length == 0 || targetX <= 0)
        {
            return 0;
        }

        var previous = 0.0;
        for (var i = 1; i <= text.Length; i++)
        {
            var width = MeasurePrefixWidth(text, i, style);
            if (targetX < (previous + width) / 2.0)
            {
                return i - 1;
            }
            previous = width;
        }

        return text.Length;
    }

    private double MeasurePrefixWidth(string text, int count, MarkdownTextStyle style)
    {
        if (count <= 0)
        {
            return 0;
        }
        if (count > text.Length)
        {
            count = text.Length;
        }

        var formatted = CreateFormattedText(text.Substring(0, count), ResolveFormat(style));
        TextMeasurement.MeasureText(formatted);

        // 与 MeasureToken 同理：前缀可能以空格结尾，用会丢掉尾随空白的 Width 会让选区高亮
        // 和命中测试都短一截。
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private static int ComputeLength(MarkdownTextLayout layout)
    {
        var total = 0;
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                total += placement.Text.Length;
            }
            if (i < layout.Lines.Count - 1)
            {
                total += 1;
            }
        }
        return total;
    }

    private static string BuildVisualText(MarkdownTextLayout layout)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < layout.Lines.Count; i++)
        {
            foreach (var placement in layout.Lines[i].Placements)
            {
                sb.Append(placement.Text);
            }
            if (i < layout.Lines.Count - 1)
            {
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    #endregion

    private readonly record struct ResolvedFormat(
        string FontFamily,
        double FontSize,
        int OpenTypeWeight,
        int OpenTypeStyle,
        Brush Foreground,
        Brush? Background,
        CornerRadius CornerRadius,
        Thickness Padding,
        MarkdownTextDecorations Decorations,
        Pen? DecorationPen);

    /// <summary>
    /// 一个待排版的词/空白/换行，连同它在 <see cref="Spans"/> 里的来源位置。位置是增量续排的锚：
    /// 排版时每行都记下它第一个 token 的来源位置，追加文本后就能只从最后一行的起点重排。
    /// </summary>
    private readonly record struct MarkdownToken(
        string Text,
        MarkdownTextStyle Style,
        bool IsWhitespace,
        bool IsLineBreak,
        int SpanIndex,
        int CharOffset,
        int ImageIndex = -1);

    private readonly record struct MarkdownTokenMeasurement(double TotalWidth, double TotalHeight, double TextWidth, double TextHeight, double TextOffsetX);

    private sealed class MarkdownTextLine
    {
        public List<MarkdownTokenPlacement> Placements { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }
        public int StartSpanIndex { get; set; }
        public int StartCharOffset { get; set; }
    }

    private sealed class MarkdownTextLayout
    {
        public List<MarkdownTextLineInfo> Lines { get; } = new();
        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>
        /// 排版过程中有没有被宽度约束挤着换过行（或挤掉过行尾空白）。
        /// 为 <see langword="false"/> 时这份排版与任何更宽的约束等价，见 <see cref="CanReuseLayout"/>。
        /// </summary>
        public bool WrappedByWidth { get; set; }
    }

    /// <param name="StartSpanIndex">本行第一个 token 在 <see cref="Spans"/> 中的下标。</param>
    /// <param name="StartCharOffset">本行第一个 token 在该 span 文本内的字符偏移。</param>
    /// <param name="StartY">本行顶边的 y（含此前空行推进的高度），续排时用来把 y 拨回去。</param>
    /// <param name="MaxWidthThrough">含本行在内的前缀最大行宽，续排时 O(1) 拿到稳定部分的宽度。</param>
    /// <param name="Runs">合并后的绘制批次，排版时算一次、随行一起复用，见 <see cref="BuildDrawRuns"/>。</param>
    private sealed record MarkdownTextLineInfo(
        IReadOnlyList<MarkdownTokenPlacement> Placements,
        double Width,
        double Height,
        int StartSpanIndex,
        int StartCharOffset,
        double StartY,
        double MaxWidthThrough,
        IReadOnlyList<MarkdownDrawRun> Runs);

    private sealed record MarkdownDrawRun(
        string Text,
        MarkdownTextStyle Style,
        double TextX,
        double TextY,
        double TextWidth,
        double TextHeight);
    private sealed record MarkdownTokenPlacement(
        string Text, MarkdownTextStyle Style, Rect Bounds, double TextWidth, double TextHeight, double TextOffsetX,
        int ImageIndex = -1);

    /// <summary>
    /// 一张行内图片。加载、解码分级与失败处理全部交给内嵌的 <see cref="Controls.Image"/>——
    /// 那套逻辑（解码桶、DPI、GPU 缓存回收）在这里重写一遍既没必要，也不可能与它保持一致。
    /// </summary>
    private sealed class InlineImageHost
    {
        public required MarkdownInlineImage Model { get; set; }

        public required Image Element { get; init; }

        /// <summary>上一次测量得到的尺寸。异步解码完成后它会变，这正是排版作废的信号。</summary>
        public Size LastDesiredSize { get; set; }
    }
}
