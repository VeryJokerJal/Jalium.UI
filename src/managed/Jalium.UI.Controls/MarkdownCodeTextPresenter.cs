using System.Text;
using System.Text.RegularExpressions;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal sealed record MarkdownHighlightedCodeLine(int LineNumber, string Text, SyntaxToken[] Tokens);

/// <summary>
/// 绘制一段带语法高亮与行号的代码文本，是 <see cref="MarkdownCodePresenter"/> 默认模板中的正文部件。
/// </summary>
/// <remarks>
/// 外框（背景、圆角、边框、外边距）由 <see cref="MarkdownCodePresenter"/> 的
/// <c>ControlTemplate</c> 负责；此元素只关心代码正文、行号栏与选区。
/// </remarks>
public sealed class MarkdownCodeTextPresenter : FrameworkElement, IMarkdownSelectable
{
    private const double GutterInnerPadding = 6;
    private const double GutterGap = 8;

    private static readonly SolidColorBrush s_fallbackForeground = new(Color.White);
    private static readonly SolidColorBrush s_fallbackLineNumberForeground = new(Color.FromRgb(128, 128, 128));

    private IReadOnlyList<MarkdownHighlightedCodeLine> _lines = Array.Empty<MarkdownHighlightedCodeLine>();
    private double _lineHeight = 20;
    private double _gutterWidth = 24;

    private string _visualText = string.Empty;
    private int[] _lineStartIndex = Array.Empty<int>();
    private int _selectionStart = -1;
    private int _selectionEnd = -1;

    private Pen? _separatorPen;
    private Brush? _separatorPenBrush;

    #region Dependency properties

    /// <summary>
    /// Identifies the <see cref="Code"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(nameof(Code), typeof(string), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(string.Empty, OnCodeChanged));

    /// <summary>
    /// Identifies the <see cref="CodeLanguage"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CodeLanguageProperty =
        DependencyProperty.Register(nameof(CodeLanguage), typeof(string), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(null, OnCodeChanged));

    /// <summary>
    /// Identifies the <see cref="MonospaceFontFamily"/> dependency property.
    /// 与行内呈现器共享同一个等宽字体设置，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty MonospaceFontFamilyProperty =
        MarkdownTextPresenter.MonospaceFontFamilyProperty.AddOwner(typeof(MarkdownCodeTextPresenter));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(14.0, OnLayoutChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property. 与 <see cref="TextElement"/> 共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(null, OnVisualChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="CodeLineHeightRatio"/> dependency property.
    /// 与正文可继承的 <see cref="MarkdownTextPresenter.LineHeightRatioProperty"/> 是两回事：
    /// 代码块行距独立于段落行距，故单独注册且不参与继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty CodeLineHeightRatioProperty =
        DependencyProperty.Register(nameof(CodeLineHeightRatio), typeof(double), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(1.45, OnLayoutChanged));

    /// <summary>
    /// Identifies the <see cref="Padding"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(new Thickness(12), OnLayoutChanged));

    /// <summary>
    /// Identifies the <see cref="ShowLineNumbers"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(true, OnLayoutChanged));

    /// <summary>
    /// Identifies the <see cref="LineNumberForeground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineNumberForegroundProperty =
        DependencyProperty.Register(nameof(LineNumberForeground), typeof(Brush), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(null, OnVisualChanged));

    /// <summary>
    /// Identifies the <see cref="GutterBackground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(null, OnVisualChanged));

    /// <summary>
    /// Identifies the <see cref="GutterSeparatorBrush"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterSeparatorBrushProperty =
        DependencyProperty.Register(nameof(GutterSeparatorBrush), typeof(Brush), typeof(MarkdownCodeTextPresenter),
            new PropertyMetadata(null, OnVisualChanged));

    /// <summary>
    /// Identifies the <see cref="SelectionBrush"/> dependency property. 与行内呈现器共享，跨可视树继承。
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        MarkdownTextPresenter.SelectionBrushProperty.AddOwner(typeof(MarkdownCodeTextPresenter));

    #endregion

    /// <summary>
    /// 初始化 <see cref="MarkdownCodeTextPresenter"/> 的新实例。
    /// </summary>
    public MarkdownCodeTextPresenter()
    {
        Cursor = Jalium.UI.Input.Cursors.IBeam;
        RebuildHighlighting();
    }

    #region CLR accessors

    /// <summary>代码正文。</summary>
    public string Code
    {
        get => (string)(GetValue(CodeProperty) ?? string.Empty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>代码语言标识（围栏代码块的 info string），决定使用哪个语法高亮器。</summary>
    public string? CodeLanguage
    {
        get => (string?)GetValue(CodeLanguageProperty);
        set => SetValue(CodeLanguageProperty, value);
    }

    /// <summary>代码使用的等宽字体族。</summary>
    public FontFamily? MonospaceFontFamily
    {
        get => (FontFamily?)GetValue(MonospaceFontFamilyProperty);
        set => SetValue(MonospaceFontFamilyProperty, value);
    }

    /// <summary>代码字号。</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>代码默认前景画刷（语法高亮资源缺失时的兜底）。</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>代码行高相对字号的倍数。</summary>
    public double CodeLineHeightRatio
    {
        get => (double)GetValue(CodeLineHeightRatioProperty)!;
        set => SetValue(CodeLineHeightRatioProperty, value);
    }

    /// <summary>正文四周的内边距。</summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>是否显示行号栏。</summary>
    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty)!;
        set => SetValue(ShowLineNumbersProperty, value);
    }

    /// <summary>行号文字的画刷。</summary>
    public Brush? LineNumberForeground
    {
        get => (Brush?)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    /// <summary>行号栏的背景画刷。</summary>
    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    /// <summary>行号栏与正文之间分隔线的画刷；<see langword="null"/> 时不画分隔线。</summary>
    public Brush? GutterSeparatorBrush
    {
        get => (Brush?)GetValue(GutterSeparatorBrushProperty);
        set => SetValue(GutterSeparatorBrushProperty, value);
    }

    /// <summary>选区高亮画刷。</summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    #endregion

    internal IReadOnlyList<MarkdownHighlightedCodeLine> DebugLines => _lines;
    internal double DebugGutterWidth => _gutterWidth;

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as MarkdownCodeTextPresenter)?.RebuildHighlighting();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownCodeTextPresenter presenter)
        {
            presenter.InvalidateMeasure();
            presenter.InvalidateVisual();
        }
    }

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as MarkdownCodeTextPresenter)?.InvalidateVisual();
    }

    /// <summary>
    /// 丢弃排版度量并重绘。祖先上的可继承排版属性变化不会回调到这里，
    /// 由 <see cref="Markdown"/> 与 <see cref="MarkdownBlockPresenter"/> 主动调用。
    /// </summary>
    internal void InvalidateFormatting()
    {
        _separatorPen = null;
        _separatorPenBrush = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private string ResolveFontFamilyName() =>
        MonospaceFontFamily?.Source is { Length: > 0 } source
            ? source
            : MarkdownTextPresenter.DefaultMonospaceFontFamilyName;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();

        var contentWidth = 0.0;
        foreach (var line in _lines)
        {
            var lineWidth = MeasureLineWidth(line);
            contentWidth = Math.Max(contentWidth, lineWidth);
        }

        var padding = Padding;
        return new Size(
            padding.Left + GutterExtent + contentWidth + padding.Right,
            padding.Top + (_lines.Count * _lineHeight) + padding.Bottom);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        EnsureMetrics();
        if (_lineHeight <= 0)
            return;

        var padding = Padding;
        var fontFamilyName = ResolveFontFamilyName();
        var fontSize = FontSize;
        var showLineNumbers = ShowLineNumbers;
        var separatorX = padding.Left + _gutterWidth;

        if (showLineNumbers)
        {
            if (GutterBackground is { } gutterBackground)
            {
                dc.DrawRectangle(gutterBackground, null, new Rect(0, 0, separatorX, RenderSize.Height));
            }

            if (GutterSeparatorBrush is { } separatorBrush)
            {
                if (_separatorPen == null || !ReferenceEquals(_separatorPenBrush, separatorBrush))
                {
                    _separatorPenBrush = separatorBrush;
                    _separatorPen = new Pen(separatorBrush, 1);
                }
                dc.DrawLine(_separatorPen, new Point(separatorX, 0), new Point(separatorX, RenderSize.Height));
            }
        }

        var lineNumberBrush = LineNumberForeground ?? s_fallbackLineNumberForeground;
        var contentX = padding.Left + GutterExtent;

        if (_selectionEnd > _selectionStart && _selectionStart >= 0 && SelectionBrush != null)
        {
            DrawSelectionHighlight(dc, contentX);
        }

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            var y = padding.Top + (index * _lineHeight);

            if (showLineNumbers)
            {
                var lineNumberText = new FormattedText(line.LineNumber.ToString(), fontFamilyName, fontSize)
                {
                    Foreground = lineNumberBrush
                };
                TextMeasurement.MeasureText(lineNumberText);
                dc.DrawText(lineNumberText, new Point(separatorX - GutterInnerPadding - lineNumberText.Width, y));
            }

            var x = contentX;
            foreach (var token in line.Tokens)
            {
                if (token.Length <= 0 || token.StartOffset < 0 || token.StartOffset + token.Length > line.Text.Length)
                {
                    continue;
                }

                var text = line.Text.Substring(token.StartOffset, token.Length);
                if (text.Length == 0)
                {
                    continue;
                }

                var tokenText = new FormattedText(text, fontFamilyName, fontSize)
                {
                    Foreground = ResolveSyntaxBrush(token.Classification)
                };
                TextMeasurement.MeasureText(tokenText);
                dc.DrawText(tokenText, new Point(x, y));
                // 笔位置必须按「含尾随空白」的宽度推进：FormattedText.Width 按排版惯例丢弃尾随空白，
                // 而高亮器会把 token 之间的空白单独产出成 PlainText token（见 RegexSyntaxHighlighter
                // 的 gap 补齐）。用 Width 推进时，纯空白 token 量得 0，两个关键字就会被贴在一起 ——
                // "private const int" 渲染成 "privateconstint"。
                x += tokenText.WidthIncludingTrailingWhitespace;
            }
        }
    }

    private double GutterExtent => ShowLineNumbers && _lineHeight > 0 ? _gutterWidth + GutterGap : 0;

    private void RebuildHighlighting()
    {
        var source = Code.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var rawLines = source.Split('\n');
        if (rawLines.Length == 0)
        {
            rawLines = new[] { string.Empty };
        }

        var highlighter = MarkdownCodeHighlighterFactory.Create(CodeLanguage);
        var lines = new List<MarkdownHighlightedCodeLine>(rawLines.Length);
        object? state = highlighter.GetInitialState();

        for (var index = 0; index < rawLines.Length; index++)
        {
            var lineText = rawLines[index].Replace("\t", "    ", StringComparison.Ordinal);
            var (tokens, nextState) = highlighter.HighlightLine(index + 1, lineText, state);
            state = nextState;
            lines.Add(new MarkdownHighlightedCodeLine(index + 1, lineText, tokens));
        }

        _lines = lines;

        // 建立一份扁平的可选中文本投影（按行用换行符拼接），并记住每行的起始字符索引，
        // 让选区能干净地映射回屏幕位置。
        _lineStartIndex = new int[lines.Count];
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            _lineStartIndex[index] = builder.Length;
            builder.Append(lines[index].Text);
            if (index < lines.Count - 1)
            {
                builder.Append('\n');
            }
        }
        _visualText = builder.ToString();

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EnsureMetrics()
    {
        if (FontSize <= 0)
        {
            _lineHeight = 0;
            _gutterWidth = 0;
            return;
        }

        var fontFamilyName = ResolveFontFamilyName();
        var fontSize = FontSize;
        var probeBrush = Foreground ?? s_fallbackForeground;

        var probe = new FormattedText("Ag", fontFamilyName, fontSize)
        {
            Foreground = probeBrush
        };
        TextMeasurement.MeasureText(probe);

        var ratio = CodeLineHeightRatio;
        _lineHeight = Math.Max(fontSize * (double.IsNaN(ratio) || ratio <= 0 ? 1.45 : ratio), probe.Height);

        if (!ShowLineNumbers)
        {
            _gutterWidth = 0;
            return;
        }

        var lineNumberText = new FormattedText(Math.Max(1, _lines.Count).ToString(), fontFamilyName, fontSize)
        {
            Foreground = probeBrush
        };
        TextMeasurement.MeasureText(lineNumberText);
        _gutterWidth = Math.Max(18, lineNumberText.Width + (GutterInnerPadding * 2));
    }

    private double MeasureLineWidth(MarkdownHighlightedCodeLine line)
    {
        if (FontSize <= 0)
            return 0;

        var fontFamilyName = ResolveFontFamilyName();
        var fontSize = FontSize;

        double width = 0;
        foreach (var token in line.Tokens)
        {
            if (token.Length <= 0 || token.StartOffset < 0 || token.StartOffset + token.Length > line.Text.Length)
            {
                continue;
            }

            var text = line.Text.Substring(token.StartOffset, token.Length);
            var tokenText = new FormattedText(text, fontFamilyName, fontSize)
            {
                Foreground = ResolveSyntaxBrush(token.Classification)
            };
            TextMeasurement.MeasureText(tokenText);
            // 与 OnRender 的笔位置推进保持同一口径，否则行宽算少、长行右端被裁。
            width += tokenText.WidthIncludingTrailingWhitespace;
        }

        return width;
    }

    #region Text selection (IMarkdownSelectable)

    /// <inheritdoc />
    public int SelectableLength => _visualText.Length;

    /// <inheritdoc />
    public string GetSelectionText(int start, int end)
    {
        start = Math.Clamp(start, 0, _visualText.Length);
        end = Math.Clamp(end, 0, _visualText.Length);
        return end > start ? _visualText.Substring(start, end - start) : string.Empty;
    }

    /// <inheritdoc />
    public void SetSelectionRange(int start, int end)
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

    /// <inheritdoc />
    public void ClearSelectionRange()
    {
        if (_selectionStart < 0 && _selectionEnd < 0)
        {
            return;
        }
        _selectionStart = -1;
        _selectionEnd = -1;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public bool TryHitTestCharacter(Point localPoint, out int charIndex)
    {
        charIndex = 0;
        if (_lines.Count == 0)
        {
            return false;
        }

        EnsureMetrics();
        if (_lineHeight <= 0)
            return false;

        var padding = Padding;
        var contentX = padding.Left + GutterExtent;
        var line = (int)Math.Floor((localPoint.Y - padding.Top) / _lineHeight);
        line = Math.Clamp(line, 0, _lines.Count - 1);
        var text = _lines[line].Text;
        var col = FindColumn(text, localPoint.X - contentX);
        charIndex = _lineStartIndex[line] + col;
        return true;
    }

    private void DrawSelectionHighlight(DrawingContext dc, double contentX)
    {
        var top = Padding.Top;
        for (var li = 0; li < _lines.Count; li++)
        {
            var text = _lines[li].Text;
            var lineStart = _lineStartIndex[li];
            var len = text.Length;
            var a = Math.Clamp(_selectionStart - lineStart, 0, len);
            var b = Math.Clamp(_selectionEnd - lineStart, 0, len);
            if (a >= b)
            {
                continue;
            }
            var x0 = contentX + MeasureWidth(text, a);
            var x1 = contentX + MeasureWidth(text, b);
            var y = top + (li * _lineHeight);
            dc.DrawRectangle(SelectionBrush, null, new Rect(x0, y, Math.Max(1, x1 - x0), _lineHeight));
        }
    }

    private int FindColumn(string text, double targetX)
    {
        if (text.Length == 0 || targetX <= 0)
        {
            return 0;
        }

        var previous = 0.0;
        for (var i = 1; i <= text.Length; i++)
        {
            var width = MeasureWidth(text, i);
            if (targetX < (previous + width) / 2.0)
            {
                return i - 1;
            }
            previous = width;
        }
        return text.Length;
    }

    private double MeasureWidth(string text, int count)
    {
        if (count <= 0 || FontSize <= 0)
        {
            return 0;
        }
        if (count > text.Length)
        {
            count = text.Length;
        }

        var ft = new FormattedText(text.Substring(0, count), ResolveFontFamilyName(), FontSize)
        {
            Foreground = Foreground ?? s_fallbackForeground
        };
        TextMeasurement.MeasureText(ft);
        // 前缀宽度用于选区矩形与鼠标命中列，同样要含尾随空白 ——
        // 否则选到行尾空格时高亮框不前进、点击落列也会偏。
        return ft.WidthIncludingTrailingWhitespace;
    }

    #endregion

    private Brush ResolveSyntaxBrush(TokenClassification classification)
    {
        var resourceKey = classification switch
        {
            TokenClassification.PlainText => "EditorSyntaxPlainText",
            TokenClassification.Keyword => "EditorSyntaxKeyword",
            TokenClassification.ControlKeyword => "EditorSyntaxControlKeyword",
            TokenClassification.TypeName => "EditorSyntaxTypeName",
            TokenClassification.StructName => "EditorSyntaxStructName",
            TokenClassification.EnumName => "EditorSyntaxEnumName",
            TokenClassification.InterfaceName => "EditorSyntaxInterfaceName",
            TokenClassification.DelegateName => "EditorSyntaxDelegateName",
            TokenClassification.String => "EditorSyntaxString",
            TokenClassification.Character => "EditorSyntaxCharacter",
            TokenClassification.Number => "EditorSyntaxNumber",
            TokenClassification.Comment => "EditorSyntaxComment",
            TokenClassification.XmlDoc => "EditorSyntaxXmlDoc",
            TokenClassification.Preprocessor => "EditorSyntaxPreprocessor",
            TokenClassification.Operator => "EditorSyntaxOperator",
            TokenClassification.Punctuation => "EditorSyntaxPunctuation",
            TokenClassification.Identifier => "EditorSyntaxIdentifier",
            TokenClassification.LocalVariable => "EditorSyntaxLocalVariable",
            TokenClassification.Parameter => "EditorSyntaxParameter",
            TokenClassification.Field => "EditorSyntaxField",
            TokenClassification.EnumMember => "EditorSyntaxEnumMember",
            TokenClassification.Property => "EditorSyntaxProperty",
            TokenClassification.Method => "EditorSyntaxMethod",
            TokenClassification.Namespace => "EditorSyntaxNamespace",
            TokenClassification.Attribute => "EditorSyntaxAttribute",
            TokenClassification.BindingKeyword => "EditorSyntaxBindingKeyword",
            TokenClassification.BindingParameter => "EditorSyntaxBindingParameter",
            TokenClassification.BindingPath => "EditorSyntaxBindingPath",
            TokenClassification.BindingOperator => "EditorSyntaxBindingOperator",
            TokenClassification.Error => "EditorSyntaxError",
            _ => "EditorSyntaxPlainText"
        };

        return TryFindResource(resourceKey) as Brush
            ?? Foreground
            ?? s_fallbackForeground;
    }
}

internal static class MarkdownCodeHighlighterFactory
{
    public static ISyntaxHighlighter Create(string? language)
    {
        var normalized = language?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "xaml" or "xml" or "jalxaml" => JalxamlSyntaxHighlighter.Create(),
            "c#" or "cs" or "csharp" => RegexSyntaxHighlighter.CreateCSharpHighlighter(),
            _ => CreateGenericHighlighter()
        };
    }

    private static ISyntaxHighlighter CreateGenericHighlighter()
    {
        var highlighter = new RegexSyntaxHighlighter();
        highlighter.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        highlighter.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        highlighter.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        highlighter.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        highlighter.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.Character));
        highlighter.Rules.Add(new HighlightingRule(@"\b(true|false|null|if|else|for|while|switch|case|return|break|continue|class|struct|enum|namespace|function|fn|let|var|const|new|public|private|protected|internal|static|void)\b", TokenClassification.Keyword));
        highlighter.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?\b", TokenClassification.Number));
        highlighter.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        highlighter.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return highlighter;
    }
}
