using System.Text;
using System.Text.RegularExpressions;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal sealed record MarkdownHighlightedCodeLine(int LineNumber, string Text, SyntaxToken[] Tokens);

internal sealed class MarkdownCodeBlockView : FrameworkElement
{
    private const double Padding = 12;
    private const double GutterInnerPadding = 6;
    private const double GutterGap = 8;

    private string _text = string.Empty;
    private string? _language;
    private IReadOnlyList<MarkdownHighlightedCodeLine> _lines = Array.Empty<MarkdownHighlightedCodeLine>();
    private double _lineHeight = 20;
    private double _gutterWidth = 24;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            RebuildHighlighting();
        }
    }

    public string? Language
    {
        get => _language;
        set
        {
            _language = value;
            RebuildHighlighting();
        }
    }

    public string CodeFontFamily { get; set; } = "Cascadia Code";
    public double CodeFontSize { get; set; } = 14;
    public Brush? ForegroundBrush { get; set; }
    public Brush? LineNumberForegroundBrush { get; set; }
    public Brush? GutterBackgroundBrush { get; set; }

    internal IReadOnlyList<MarkdownHighlightedCodeLine> DebugLines => _lines;
    internal double DebugGutterWidth => _gutterWidth;

    public MarkdownCodeBlockView()
    {
        RebuildHighlighting();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();

        var contentWidth = 0.0;
        foreach (var line in _lines)
        {
            var lineWidth = MeasureLineWidth(line);
            contentWidth = Math.Max(contentWidth, lineWidth);
        }

        return new Size(
            Padding + _gutterWidth + GutterGap + contentWidth + Padding,
            Padding + (_lines.Count * _lineHeight) + Padding);
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
        {
            return;
        }

        EnsureMetrics();

        var separatorX = Padding + _gutterWidth;
        var gutterRect = new Rect(0, 0, separatorX, RenderSize.Height);
        if (GutterBackgroundBrush != null)
        {
            dc.DrawRectangle(GutterBackgroundBrush, null, gutterRect);
        }

        var separatorBrush = TryFindResource("ControlBorder") as Brush;
        if (separatorBrush != null)
        {
            var separatorPen = new Pen(separatorBrush, 1);
            dc.DrawLine(separatorPen, new Point(separatorX, 0), new Point(separatorX, RenderSize.Height));
        }

        var lineNumberBrush = LineNumberForegroundBrush
            ?? TryFindResource("TextSecondary") as Brush
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));

        var contentX = separatorX + GutterGap;
        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            var y = Padding + (index * _lineHeight);

            var lineNumberText = new FormattedText(line.LineNumber.ToString(), CodeFontFamily, CodeFontSize)
            {
                Foreground = lineNumberBrush
            };
            TextMeasurement.MeasureText(lineNumberText);
            dc.DrawText(lineNumberText, new Point(separatorX - GutterInnerPadding - lineNumberText.Width, y));

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

                var tokenText = new FormattedText(text, CodeFontFamily, CodeFontSize)
                {
                    Foreground = ResolveSyntaxBrush(token.Classification)
                };
                TextMeasurement.MeasureText(tokenText);
                dc.DrawText(tokenText, new Point(x, y));
                x += tokenText.Width;
            }
        }
    }

    private void RebuildHighlighting()
    {
        var source = _text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var rawLines = source.Split('\n');
        if (rawLines.Length == 0)
        {
            rawLines = new[] { string.Empty };
        }

        var highlighter = MarkdownCodeHighlighterFactory.Create(_language);
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
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void EnsureMetrics()
    {
        var probe = new FormattedText("Ag", CodeFontFamily, CodeFontSize)
        {
            Foreground = ForegroundBrush ?? new SolidColorBrush(Color.White)
        };
        TextMeasurement.MeasureText(probe);
        _lineHeight = Math.Max(CodeFontSize * 1.45, probe.Height);

        var lineNumberText = new FormattedText(Math.Max(1, _lines.Count).ToString(), CodeFontFamily, CodeFontSize)
        {
            Foreground = ForegroundBrush ?? new SolidColorBrush(Color.White)
        };
        TextMeasurement.MeasureText(lineNumberText);
        _gutterWidth = Math.Max(18, lineNumberText.Width + (GutterInnerPadding * 2));
    }

    private double MeasureLineWidth(MarkdownHighlightedCodeLine line)
    {
        double width = 0;
        foreach (var token in line.Tokens)
        {
            if (token.Length <= 0 || token.StartOffset < 0 || token.StartOffset + token.Length > line.Text.Length)
            {
                continue;
            }

            var text = line.Text.Substring(token.StartOffset, token.Length);
            var tokenText = new FormattedText(text, CodeFontFamily, CodeFontSize)
            {
                Foreground = ResolveSyntaxBrush(token.Classification)
            };
            TextMeasurement.MeasureText(tokenText);
            width += tokenText.Width;
        }

        return width;
    }

    private Brush ResolveSyntaxBrush(TokenClassification classification)
    {
        var resourceKey = classification switch
        {
            TokenClassification.PlainText => "EditorSyntaxPlainText",
            TokenClassification.Keyword => "EditorSyntaxKeyword",
            TokenClassification.ControlKeyword => "EditorSyntaxControlKeyword",
            TokenClassification.TypeName => "EditorSyntaxTypeName",
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
            ?? ForegroundBrush
            ?? new SolidColorBrush(Color.White);
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
