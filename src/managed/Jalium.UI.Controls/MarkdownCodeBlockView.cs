using System.Text;
using System.Text.RegularExpressions;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

internal sealed record MarkdownHighlightedCodeLine(int LineNumber, string Text, SyntaxToken[] Tokens);

internal sealed class MarkdownCodeBlockView : FrameworkElement, IMarkdownSelectable
{
    private const double Padding = 12;
    private const double GutterInnerPadding = 6;
    private const double GutterGap = 8;

    private string _text = string.Empty;
    private string? _language;
    private IReadOnlyList<MarkdownHighlightedCodeLine> _lines = Array.Empty<MarkdownHighlightedCodeLine>();
    private double _lineHeight = 20;
    private double _gutterWidth = 24;

    private string _visualText = string.Empty;
    private int[] _lineStartIndex = Array.Empty<int>();
    private int _selectionStart = -1;
    private int _selectionEnd = -1;

    // Cached pen/brush
    private Pen? _separatorPen;
    private Brush? _separatorPenBrush;
    private Brush? _lineNumberBrush;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            RebuildHighlighting();
        }
    }

    public new string? Language
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
    public Brush? SelectionBrush { get; set; }

    internal IReadOnlyList<MarkdownHighlightedCodeLine> DebugLines => _lines;
    internal double DebugGutterWidth => _gutterWidth;

    public MarkdownCodeBlockView()
    {
        Cursor = Jalium.UI.Input.Cursors.IBeam;
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

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

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
            if (_separatorPen == null || _separatorPenBrush != separatorBrush)
            {
                _separatorPenBrush = separatorBrush;
                _separatorPen = new Pen(separatorBrush, 1);
            }
            dc.DrawLine(_separatorPen, new Point(separatorX, 0), new Point(separatorX, RenderSize.Height));
        }

        var lineNumberBrush = LineNumberForegroundBrush
            ?? TryFindResource("TextSecondary") as Brush;
        if (lineNumberBrush == null)
        {
            _lineNumberBrush ??= new SolidColorBrush(Color.FromRgb(128, 128, 128));
            lineNumberBrush = _lineNumberBrush;
        }

        var contentX = separatorX + GutterGap;

        if (_selectionEnd > _selectionStart && _selectionStart >= 0 && SelectionBrush != null)
        {
            DrawSelectionHighlight(dc, contentX);
        }

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

        // Build a flat selectable-text projection (lines joined by newlines) and remember each
        // line's starting character index so selection maps cleanly back to screen positions.
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

    #region Text selection (IMarkdownSelectable)

    public int SelectableLength => _visualText.Length;

    public string GetSelectionText(int start, int end)
    {
        start = Math.Clamp(start, 0, _visualText.Length);
        end = Math.Clamp(end, 0, _visualText.Length);
        return end > start ? _visualText.Substring(start, end - start) : string.Empty;
    }

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

    public bool TryHitTestCharacter(Point localPoint, out int charIndex)
    {
        charIndex = 0;
        if (_lines.Count == 0)
        {
            return false;
        }

        EnsureMetrics();
        var contentX = Padding + _gutterWidth + GutterGap;
        var line = (int)Math.Floor((localPoint.Y - Padding) / _lineHeight);
        line = Math.Clamp(line, 0, _lines.Count - 1);
        var text = _lines[line].Text;
        var col = FindColumn(text, localPoint.X - contentX);
        charIndex = _lineStartIndex[line] + col;
        return true;
    }

    private void DrawSelectionHighlight(DrawingContext dc, double contentX)
    {
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
            var y = Padding + (li * _lineHeight);
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
        if (count <= 0)
        {
            return 0;
        }
        if (count > text.Length)
        {
            count = text.Length;
        }

        var ft = new FormattedText(text.Substring(0, count), CodeFontFamily, CodeFontSize)
        {
            Foreground = ForegroundBrush ?? new SolidColorBrush(Color.White)
        };
        TextMeasurement.MeasureText(ft);
        return ft.Width;
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
