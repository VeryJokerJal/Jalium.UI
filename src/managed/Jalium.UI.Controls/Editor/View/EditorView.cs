using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Manages the editor viewport and orchestrates rendering of all visual elements.
/// Not a UIElement — renders directly into EditControl's DrawingContext.
/// </summary>
internal sealed class EditorView
{
    private const double FoldingLaneGap = 6;
    private const double FoldingMarginWidth = 14;
    private const double TextAreaGap = 8;
    private const double LineNumberRightPadding = 8;
    private const double GutterInnerPadding = 6;
    private const int LineCacheBufferLines = 256;
    private static readonly SolidColorBrush s_keywordBrush = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_controlKeywordBrush = new(Color.FromRgb(197, 134, 192));
    private static readonly SolidColorBrush s_typeNameBrush = new(Color.FromRgb(78, 201, 176));
    private static readonly SolidColorBrush s_stringBrush = new(Color.FromRgb(206, 145, 120));
    private static readonly SolidColorBrush s_numberBrush = new(Color.FromRgb(181, 206, 168));
    private static readonly SolidColorBrush s_commentBrush = new(Color.FromRgb(106, 153, 85));
    private static readonly SolidColorBrush s_xmlDocBrush = new(Color.FromRgb(96, 139, 78));
    private static readonly SolidColorBrush s_preprocessorBrush = new(Color.FromRgb(155, 155, 155));
    private static readonly SolidColorBrush s_operatorBrush = new(Color.FromRgb(212, 212, 212));
    private static readonly SolidColorBrush s_punctuationBrush = new(Color.FromRgb(79, 193, 255));
    private static readonly SolidColorBrush s_methodBrush = new(Color.FromRgb(220, 220, 170));
    private static readonly SolidColorBrush s_propertyBrush = new(Color.FromRgb(156, 220, 254));
    private static readonly SolidColorBrush s_namespaceBrush = new(Color.FromRgb(78, 201, 176));
    private static readonly SolidColorBrush s_bindingKeywordBrush = new(Color.FromRgb(255, 196, 79));
    private static readonly SolidColorBrush s_bindingParameterBrush = new(Color.FromRgb(255, 145, 90));
    private static readonly SolidColorBrush s_bindingPathBrush = new(Color.FromRgb(91, 214, 201));
    private static readonly SolidColorBrush s_bindingOperatorBrush = new(Color.FromRgb(144, 214, 255));
    private static readonly SolidColorBrush s_errorBrush = new(Color.FromRgb(244, 71, 71));
    private readonly Dictionary<int, EditorViewLine> _lineCache = [];
    private double _lineHeight;
    private double _charWidth;
    private double _gutterWidth;
    private double _leadingGutterInset;
    private string? _cachedFontFamily;
    private double _cachedFontSize;

    // Visible line map cache
    private int[] _visibleLineNumbers = [1];
    private int[] _lineToVisibleIndex = [0, 0];
    private int[] _lineToAnchorLine = [0, 1];
    private bool[] _hiddenLines = [false, false];
    private int _cachedLineMapDocumentVersion = -1;
    private int _cachedLineMapLineCount = -1;
    private int _cachedLineMapFoldingVersion = int.MinValue;

    // Dependencies
    public TextDocument? Document { get; set; }
    public ISyntaxHighlighter? Highlighter { get; set; }
    public FoldingManager? Folding { get; set; }
    public Func<TokenClassification, Brush, Brush>? ClassificationBrushResolver { get; set; }

    // Highlighting state per line
    private readonly Dictionary<int, object?> _lineStates = [];

    // Viewport state
    public double VerticalOffset { get; set; }
    public double HorizontalOffset { get; set; }
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }

    // Computed
    public double LineHeight => _lineHeight;
    public double CharWidth => _charWidth;
    public double GutterWidth => _gutterWidth;
    public double LeadingGutterInset
    {
        get => _leadingGutterInset;
        set => _leadingGutterInset = Math.Max(0, value);
    }
    public double LineNumberAreaLeft => LeadingGutterInset;
    public double FoldingLaneLeft => LineNumberAreaLeft + _gutterWidth + FoldingLaneGap;
    public double FoldingLaneWidth => FoldingMarginWidth;
    public double TextAreaLeft => FoldingLaneLeft + FoldingMarginWidth + TextAreaGap;
    public int CachedLineCount => _lineCache.Count;

    public int FirstVisibleLineNumber
    {
        get
        {
            if (Document == null) return 1;
            if (_lineHeight <= 0) return 1;

            int firstVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset);
            return GetDocumentLineFromVisibleIndex(firstVisibleIndex);
        }
    }

    public int LastVisibleLineNumber
    {
        get
        {
            if (Document == null) return 1;
            if (_lineHeight <= 0) return Math.Max(1, Document.LineCount);

            double bottom = VerticalOffset + Math.Max(0, ViewportHeight - 1);
            int lastVisibleIndex = GetVisibleLineIndexFromVerticalOffset(bottom);
            return GetDocumentLineFromVisibleIndex(lastVisibleIndex);
        }
    }

    public int VisibleLineCount
    {
        get
        {
            if (Document == null) return 1;
            if (_lineHeight <= 0)
                return Math.Max(1, GetTotalVisibleLineCount());

            int firstVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset);
            double bottom = VerticalOffset + Math.Max(0, ViewportHeight - 1);
            int lastVisibleIndex = GetVisibleLineIndexFromVerticalOffset(bottom);
            return Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
        }
    }

    public double TotalContentHeight
    {
        get
        {
            if (Document == null)
                return 0;

            return GetTotalVisibleLineCount() * Math.Max(_lineHeight, 1);
        }
    }

    public double TotalContentWidth => 2000; // Approximate; could be computed from longest line

    /// <summary>
    /// Gets the measured text width of a line in document coordinates (independent of scroll offset).
    /// </summary>
    public double GetLineTextWidth(int lineNumber)
    {
        if (Document == null)
            return 0;

        int clampedLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        var line = Document.GetLineByNumber(clampedLine);
        if (line.Length <= 0)
            return 0;

        var cachedLine = GetOrCreateLineCacheEntry(clampedLine, line);
        string lineText = Document.GetLineText(clampedLine);
        return GetPrefixWidth(cachedLine, lineText, lineText.Length);
    }

    /// <summary>
    /// Updates layout metrics based on font settings.
    /// </summary>
    public void UpdateLayout(string fontFamily, double fontSize)
    {
        if (_cachedFontFamily == fontFamily && _cachedFontSize == fontSize && _lineHeight > 0)
            return;

        _cachedFontFamily = fontFamily;
        _cachedFontSize = fontSize;

        if (_lineCache.Count > 0)
        {
            foreach (var cachedLine in _lineCache.Values)
                cachedLine.InvalidateGeometry();
        }

        // Get font metrics from native measurement
        var metrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize, 400, 0);
        _lineHeight = metrics.LineHeight > 0
            ? Math.Ceiling(metrics.LineHeight)
            : Math.Ceiling(fontSize * 1.35);
        _charWidth = metrics.Width > 0 ? metrics.Width : fontSize * 0.6;

        UpdateGutterWidth();
    }

    public int GetLineNumberFromY(double y)
    {
        if (Document == null)
            return 1;

        if (_lineHeight <= 0)
            return 1;

        int visibleIndex = GetVisibleLineIndexFromVerticalOffset(y + VerticalOffset);
        return GetDocumentLineFromVisibleIndex(visibleIndex);
    }

    public bool IsLineVisible(int lineNumber)
    {
        if (Document == null)
            return false;

        EnsureLineMap();
        int clampedLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        return !_hiddenLines[clampedLine];
    }

    public int GetVisibleAnchorLineNumber(int lineNumber)
    {
        if (Document == null)
            return 1;

        EnsureLineMap();
        int clampedLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        return _lineToAnchorLine[clampedLine];
    }

    public bool TryGetLineTop(int lineNumber, out double y)
    {
        y = 0;
        if (Document == null || _lineHeight <= 0)
            return false;

        int clampedLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        if (!IsLineVisible(clampedLine))
            return false;

        int visibleIndex = GetVisibleIndexForLine(clampedLine);
        y = visibleIndex * _lineHeight - VerticalOffset;
        return true;
    }

    public double GetLineTop(int lineNumber)
    {
        if (Document == null || _lineHeight <= 0)
            return 0;

        int visibleIndex = GetVisibleIndexForLine(lineNumber);
        return visibleIndex * _lineHeight - VerticalOffset;
    }

    public double GetAbsoluteLineTop(int lineNumber)
    {
        if (Document == null || _lineHeight <= 0)
            return 0;

        int visibleIndex = GetVisibleIndexForLine(lineNumber);
        return visibleIndex * _lineHeight;
    }

    public int MoveVisibleLine(int currentLine, int delta)
    {
        if (Document == null)
            return 1;

        EnsureLineMap();

        int currentVisibleIndex = GetVisibleIndexForLine(currentLine);
        int lastVisibleIndex = Math.Max(0, GetTotalVisibleLineCount() - 1);
        int targetVisibleIndex = Math.Clamp(currentVisibleIndex + delta, 0, lastVisibleIndex);
        return GetDocumentLineFromVisibleIndex(targetVisibleIndex);
    }

    private void UpdateGutterWidth()
    {
        if (Document == null)
        {
            _gutterWidth = 0;
            return;
        }

        int digits = Math.Max(2, Document.LineCount.ToString().Length);
        double estimatedWidth = digits * Math.Max(1, _charWidth);
        double measuredWidth = estimatedWidth;

        if (!string.IsNullOrEmpty(_cachedFontFamily) && _cachedFontSize > 0)
        {
            var sample = new FormattedText(new string('9', digits), _cachedFontFamily, _cachedFontSize);
            TextMeasurement.MeasureText(sample);
            if (sample.Width > 0)
                measuredWidth = Math.Max(measuredWidth, Math.Ceiling(sample.Width));
        }

        _gutterWidth = Math.Ceiling(measuredWidth + GutterInnerPadding + LineNumberRightPadding);
    }

    private void EnsureLineMap()
    {
        if (Document == null)
        {
            _visibleLineNumbers = [1];
            _lineToVisibleIndex = [0, 0];
            _lineToAnchorLine = [0, 1];
            _hiddenLines = [false, false];
            _cachedLineMapDocumentVersion = -1;
            _cachedLineMapLineCount = 1;
            _cachedLineMapFoldingVersion = 0;
            return;
        }

        int lineCount = Math.Max(1, Document.LineCount);
        int foldingVersion = Folding?.Version ?? 0;
        if (_cachedLineMapDocumentVersion == Document.Version &&
            _cachedLineMapLineCount == lineCount &&
            _cachedLineMapFoldingVersion == foldingVersion)
        {
            return;
        }

        var hiddenLines = new bool[lineCount + 1];
        var hiddenAnchorLineByLine = new int[lineCount + 1];
        if (Folding != null)
        {
            var foldings = Folding.Foldings;
            for (int i = 0; i < foldings.Count; i++)
            {
                var section = foldings[i];
                if (!section.IsFolded)
                    continue;

                int hiddenStart = Math.Clamp(section.StartLine + 1, 1, lineCount);
                int hiddenEnd = Math.Clamp(section.EndLine, 1, lineCount);
                int anchorLine = Math.Clamp(section.StartLine, 1, lineCount);
                for (int line = hiddenStart; line <= hiddenEnd; line++)
                {
                    hiddenLines[line] = true;
                    if (anchorLine >= hiddenAnchorLineByLine[line])
                        hiddenAnchorLineByLine[line] = anchorLine;
                }
            }
        }

        var visible = new List<int>(lineCount);
        var visibleIndexByLine = new int[lineCount + 1];
        for (int line = 1; line <= lineCount; line++)
        {
            if (hiddenLines[line])
                continue;

            visibleIndexByLine[line] = visible.Count;
            visible.Add(line);
        }

        if (visible.Count == 0)
        {
            visible.Add(1);
            visibleIndexByLine[1] = 0;
            hiddenLines[1] = false;
        }

        var lineToAnchorLine = new int[lineCount + 1];
        int previousVisibleLine = visible[0];
        for (int line = 1; line <= lineCount; line++)
        {
            if (!hiddenLines[line])
            {
                lineToAnchorLine[line] = line;
                previousVisibleLine = line;
                continue;
            }

            int anchorLine = hiddenAnchorLineByLine[line];
            if (anchorLine <= 0)
                anchorLine = previousVisibleLine;

            if (anchorLine < 1 || anchorLine > lineCount || hiddenLines[anchorLine])
                anchorLine = previousVisibleLine;

            lineToAnchorLine[line] = anchorLine;
        }

        var lineToVisibleIndex = new int[lineCount + 1];
        for (int line = 1; line <= lineCount; line++)
        {
            int anchorLine = lineToAnchorLine[line];
            if (anchorLine < 1 || anchorLine > lineCount)
                anchorLine = visible[0];

            lineToVisibleIndex[line] = visibleIndexByLine[anchorLine];
        }

        _visibleLineNumbers = visible.ToArray();
        _lineToVisibleIndex = lineToVisibleIndex;
        _lineToAnchorLine = lineToAnchorLine;
        _hiddenLines = hiddenLines;
        _cachedLineMapDocumentVersion = Document.Version;
        _cachedLineMapLineCount = lineCount;
        _cachedLineMapFoldingVersion = foldingVersion;
    }

    private int GetTotalVisibleLineCount()
    {
        EnsureLineMap();
        return _visibleLineNumbers.Length;
    }

    private int GetVisibleLineIndexFromVerticalOffset(double absoluteOffset)
    {
        if (_lineHeight <= 0)
            return 0;

        int lastVisibleIndex = Math.Max(0, GetTotalVisibleLineCount() - 1);
        int visibleIndex = (int)(Math.Max(0, absoluteOffset) / _lineHeight);
        return Math.Clamp(visibleIndex, 0, lastVisibleIndex);
    }

    private int GetVisibleIndexForLine(int lineNumber)
    {
        if (Document == null)
            return 0;

        EnsureLineMap();
        int clampedLine = Math.Clamp(lineNumber, 1, Document.LineCount);
        return _lineToVisibleIndex[clampedLine];
    }

    private int GetDocumentLineFromVisibleIndex(int visibleIndex)
    {
        EnsureLineMap();
        int clampedVisibleIndex = Math.Clamp(visibleIndex, 0, Math.Max(0, _visibleLineNumbers.Length - 1));
        return _visibleLineNumbers[clampedVisibleIndex];
    }

    /// <summary>
    /// Invalidates cached line data for re-rendering.
    /// </summary>
    public void InvalidateVisibleLines()
    {
        _lineCache.Clear();
        _lineStates.Clear();
        _cachedLineMapDocumentVersion = -1;
        _cachedLineMapLineCount = -1;
        _cachedLineMapFoldingVersion = int.MinValue;
    }

    /// <summary>
    /// Invalidates cached data from the specified document line onward.
    /// </summary>
    public void InvalidateFromLine(int lineNumber)
    {
        int startLine = Math.Max(1, lineNumber);
        if (Document != null)
            startLine = Math.Clamp(startLine, 1, Math.Max(1, Document.LineCount));

        if (_lineCache.Count > 0)
        {
            var lineKeysToRemove = new List<int>();
            foreach (var key in _lineCache.Keys)
            {
                if (key >= startLine)
                    lineKeysToRemove.Add(key);
            }

            for (int i = 0; i < lineKeysToRemove.Count; i++)
                _lineCache.Remove(lineKeysToRemove[i]);
        }

        if (_lineStates.Count > 0)
        {
            int stateStartLine = Math.Max(1, startLine);
            var stateKeysToRemove = new List<int>();
            foreach (var key in _lineStates.Keys)
            {
                if (key >= stateStartLine)
                    stateKeysToRemove.Add(key);
            }

            for (int i = 0; i < stateKeysToRemove.Count; i++)
                _lineStates.Remove(stateKeysToRemove[i]);
        }

        _cachedLineMapDocumentVersion = -1;
        _cachedLineMapLineCount = -1;
        _cachedLineMapFoldingVersion = int.MinValue;
    }

    /// <summary>
    /// Renders the entire editor view into the given DrawingContext.
    /// </summary>
    public void Render(DrawingContext dc, Size renderSize, CaretManager caret, SelectionManager selection,
        bool showLineNumbers, bool highlightCurrentLine,
        Brush foreground, Brush selectionBrush, Brush caretBrush,
        Brush lineNumberForeground, Brush currentLineBackground, Brush gutterBackground,
        string fontFamily, double fontSize, FontWeight fontWeight, FontStyle fontStyle,
        bool renderLineNumbers = true,
        bool suppressCaret = false)
    {
        if (Document == null || _lineHeight <= 0) return;

        double previousViewportWidth = ViewportWidth;
        double previousViewportHeight = ViewportHeight;
        ViewportWidth = renderSize.Width;
        ViewportHeight = renderSize.Height;
        try
        {
            UpdateGutterWidth();
            EnsureLineMap();

            double textAreaLeft = showLineNumbers ? TextAreaLeft : 0;
            double textAreaWidth = renderSize.Width - textAreaLeft;

            // Render gutter background
            if (showLineNumbers && _gutterWidth > 0)
            {
                dc.DrawRectangle(gutterBackground, null,
                    new Rect(0, 0, TextAreaLeft, renderSize.Height));
            }

            int firstVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset);
            int lastVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset + Math.Max(0, ViewportHeight - 1));
            int firstVisibleLineNumber = GetDocumentLineFromVisibleIndex(firstVisibleIndex);
            int lastVisibleLineNumber = GetDocumentLineFromVisibleIndex(lastVisibleIndex);
            TrimLineCache(firstVisibleLineNumber, lastVisibleLineNumber);
            var (caretLineRaw, caretColumnRaw) = caret.GetLineColumn(Document);
            int caretLine = GetVisibleAnchorLineNumber(caretLineRaw);
            int caretColumn = caretColumnRaw;
            if (caretLine != caretLineRaw)
            {
                var anchorLine = Document.GetLineByNumber(caretLine);
                caretColumn = anchorLine.Length;
            }

            for (int visibleIndex = firstVisibleIndex; visibleIndex <= lastVisibleIndex; visibleIndex++)
            {
                int lineNum = GetDocumentLineFromVisibleIndex(visibleIndex);
                double y = visibleIndex * _lineHeight - VerticalOffset;
                if (y + _lineHeight < 0 || y > renderSize.Height)
                    continue;

                var docLine = Document.GetLineByNumber(lineNum);
                var cachedLine = GetOrCreateLineCacheEntry(lineNum, docLine);
                string lineText = Document.GetLineText(lineNum);
                cachedLine.Y = y;

                // Current line highlight
                if (highlightCurrentLine && lineNum == caretLine)
                {
                    dc.DrawRectangle(currentLineBackground, null,
                        new Rect(textAreaLeft, y, textAreaWidth, _lineHeight));
                }

                // Selection rendering
                var selRange = selection.GetSelectionOnLine(docLine);
                if (selRange.HasValue)
                {
                    int selStartColumn = Math.Clamp(selRange.Value.startColumn, 0, lineText.Length);
                    double selStartX = GetColumnX(cachedLine, lineText, selStartColumn, textAreaLeft);
                    double selEndX = selRange.Value.endColumn > docLine.Length
                        ? textAreaLeft + textAreaWidth // Selection extends to end of line
                        : GetColumnX(cachedLine, lineText, Math.Clamp(selRange.Value.endColumn, 0, lineText.Length), textAreaLeft);

                    dc.DrawRectangle(selectionBrush, null,
                        new Rect(Math.Max(textAreaLeft, selStartX), y,
                            Math.Max(0, selEndX - Math.Max(textAreaLeft, selStartX)), _lineHeight));
                }

                // Line number
                if (showLineNumbers && renderLineNumbers)
                {
                    var lineNumBrush = lineNum == caretLine ? foreground : lineNumberForeground;
                    var lineNumText = new FormattedText(lineNum.ToString(), fontFamily, fontSize)
                    {
                        Foreground = lineNumBrush
                    };
                    TextMeasurement.MeasureText(lineNumText);

                    var lineNumberClip = new Rect(LineNumberAreaLeft, y, Math.Max(0, _gutterWidth), _lineHeight);
                    if (lineNumberClip.Width > 0)
                    {
                        dc.PushClip(new RectangleGeometry(lineNumberClip));
                        double lineNumberX = LineNumberAreaLeft + Math.Max(0, _gutterWidth - lineNumText.Width - LineNumberRightPadding);
                        dc.DrawText(lineNumText, new Point(lineNumberX, y));
                        dc.Pop();
                    }
                }

                // Line text with syntax highlighting
                RenderLineText(dc, docLine, cachedLine, lineNum, lineText, y, textAreaLeft, fontFamily, fontSize, fontWeight, fontStyle, foreground);
            }

            // Render caret
            if (!suppressCaret && caret.IsVisible && caret.Opacity > 0)
            {
                var caretDocLine = Document.GetLineByNumber(caretLine);
                var caretCachedLine = GetOrCreateLineCacheEntry(caretLine, caretDocLine);
                string caretLineText = Document.GetLineText(caretLine);
                int clampedCaretColumn = Math.Clamp(caretColumn, 0, caretLineText.Length);
                double caretX = GetColumnX(caretCachedLine, caretLineText, clampedCaretColumn, textAreaLeft);
                double caretY = GetLineTop(caretLine);

                if (caretX >= textAreaLeft && caretY >= 0 && caretY < renderSize.Height)
                {
                    var caretPen = new Pen(caretBrush, 2);
                    dc.DrawLine(caretPen,
                        new Point(caretX, caretY),
                        new Point(caretX, caretY + _lineHeight));
                }
            }
        }
        finally
        {
            ViewportWidth = previousViewportWidth;
            ViewportHeight = previousViewportHeight;
        }
    }

    /// <summary>
    /// Renders only line numbers for the current visible range.
    /// Useful when a post-processing overlay (for example, backdrop blur) is applied over the gutter.
    /// </summary>
    public void RenderLineNumbers(DrawingContext dc, CaretManager caret, Brush foreground, Brush lineNumberForeground,
        string fontFamily, double fontSize)
    {
        if (Document == null || _lineHeight <= 0)
            return;

        UpdateGutterWidth();
        EnsureLineMap();

        int firstVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset);
        int lastVisibleIndex = GetVisibleLineIndexFromVerticalOffset(VerticalOffset + Math.Max(0, ViewportHeight - 1));
        var (caretLineRaw, _) = caret.GetLineColumn(Document);
        int caretLine = GetVisibleAnchorLineNumber(caretLineRaw);

        for (int visibleIndex = firstVisibleIndex; visibleIndex <= lastVisibleIndex; visibleIndex++)
        {
            int lineNum = GetDocumentLineFromVisibleIndex(visibleIndex);
            double y = visibleIndex * _lineHeight - VerticalOffset;
            if (y + _lineHeight < 0 || y > ViewportHeight)
                continue;

            var lineNumBrush = lineNum == caretLine ? foreground : lineNumberForeground;
            var lineNumText = new FormattedText(lineNum.ToString(), fontFamily, fontSize)
            {
                Foreground = lineNumBrush
            };
            TextMeasurement.MeasureText(lineNumText);

            var lineNumberClip = new Rect(LineNumberAreaLeft, y, Math.Max(0, _gutterWidth), _lineHeight);
            if (lineNumberClip.Width > 0)
            {
                dc.PushClip(new RectangleGeometry(lineNumberClip));
                double lineNumberX = LineNumberAreaLeft + Math.Max(0, _gutterWidth - lineNumText.Width - LineNumberRightPadding);
                dc.DrawText(lineNumText, new Point(lineNumberX, y));
                dc.Pop();
            }
        }
    }

    private void RenderLineText(DrawingContext dc, DocumentLine docLine, EditorViewLine cachedLine, int lineNum, string lineText,
        double y, double textAreaLeft, string fontFamily, double fontSize,
        FontWeight fontWeight, FontStyle fontStyle, Brush defaultForeground)
    {
        if (docLine.Length == 0) return;

        int visibleColumnLimit = lineText.Length;
        if (Folding?.GetFoldingAt(lineNum) is { IsFolded: true } foldedSection)
        {
            int collapseColumn = foldedSection.StartColumn >= 0 ? foldedSection.StartColumn : lineText.Length;
            visibleColumnLimit = GetCollapsedLineVisibleColumn(lineText, collapseColumn);
        }

        if (visibleColumnLimit <= 0)
            return;

        if (Highlighter != null)
        {
            var highlighted = GetOrComputeHighlightedLine(cachedLine, lineNum, lineText);

            foreach (var token in highlighted.Tokens)
            {
                if (token.Length == 0) continue;
                if (token.StartOffset >= visibleColumnLimit)
                    break;

                int tokenStart = Math.Clamp(token.StartOffset, 0, visibleColumnLimit);
                int tokenEnd = Math.Clamp(token.StartOffset + token.Length, tokenStart, visibleColumnLimit);
                int tokenLength = tokenEnd - tokenStart;
                if (tokenLength <= 0)
                    continue;

                var tokenText = lineText.Substring(tokenStart, tokenLength);

                var brush = ResolveBrushForClassification(token.Classification, defaultForeground);
                var formatted = new FormattedText(tokenText, fontFamily, fontSize) { Foreground = brush };
                double tokenX = GetColumnX(cachedLine, lineText, tokenStart, textAreaLeft);
                dc.DrawText(formatted, new Point(tokenX, y));
            }
        }
        else
        {
            // Plain text rendering
            string text = visibleColumnLimit < lineText.Length ? lineText[..visibleColumnLimit] : lineText;
            var formatted = new FormattedText(text, fontFamily, fontSize) { Foreground = defaultForeground };
            dc.DrawText(formatted, new Point(textAreaLeft - HorizontalOffset, y));
        }
    }

    private EditorViewLine GetOrCreateLineCacheEntry(int lineNumber, DocumentLine line)
    {
        if (!_lineCache.TryGetValue(lineNumber, out var cached))
        {
            cached = new EditorViewLine(lineNumber);
            _lineCache.Add(lineNumber, cached);
        }

        if (cached.DocumentOffset != line.Offset || cached.Length != line.Length)
        {
            cached.InvalidateHighlighting();
            cached.InvalidateGeometry();
        }

        cached.DocumentOffset = line.Offset;
        cached.Length = line.Length;
        return cached;
    }

    private HighlightedLine GetOrComputeHighlightedLine(EditorViewLine cachedLine, int lineNumber, string lineText)
    {
        if (Highlighter == null)
            return HighlightedLine.CreatePlainText(lineNumber, lineText.Length);

        if (!cachedLine.IsHighlightingDirty && cachedLine.Highlighting != null)
            return cachedLine.Highlighting;

        var highlighted = ComputeHighlightedLine(lineNumber, lineText);
        cachedLine.Highlighting = highlighted;
        cachedLine.IsHighlightingDirty = false;
        return highlighted;
    }

    private void TrimLineCache(int firstVisibleLine, int lastVisibleLine)
    {
        if (_lineCache.Count == 0)
            return;

        int minKeep = Math.Max(1, firstVisibleLine - LineCacheBufferLines);
        int maxKeep = lastVisibleLine + LineCacheBufferLines;
        int desiredMax = Math.Max(256, (maxKeep - minKeep + 1) * 2);
        if (_lineCache.Count <= desiredMax)
            return;

        var keysToRemove = new List<int>();
        foreach (var key in _lineCache.Keys)
        {
            if (key < minKeep || key > maxKeep)
                keysToRemove.Add(key);
        }

        for (int i = 0; i < keysToRemove.Count; i++)
            _lineCache.Remove(keysToRemove[i]);
    }

    private static int GetCollapsedLineVisibleColumn(string lineText, int collapseColumn)
    {
        if (lineText.Length == 0)
            return 0;

        int clamped = Math.Clamp(collapseColumn, 0, lineText.Length);
        int previewColumn = clamped;
        while (previewColumn > 0 && char.IsWhiteSpace(lineText[previewColumn - 1]))
            previewColumn--;

        return previewColumn;
    }

    private HighlightedLine ComputeHighlightedLine(int lineNumber, string lineText)
    {
        if (Highlighter == null)
            return HighlightedLine.CreatePlainText(lineNumber, lineText.Length);

        // Get state for this line
        object? state;
        if (lineNumber == 1)
        {
            state = Highlighter.GetInitialState();
        }
        else if (!_lineStates.TryGetValue(lineNumber - 1, out state))
        {
            // Resume from nearest cached predecessor state instead of always restarting from line 1.
            state = Highlighter.GetInitialState();
            int resumeLine = 1;
            int bestCachedLine = 0;
            object? bestCachedState = null;
            foreach (var pair in _lineStates)
            {
                int cachedLine = pair.Key;
                if (cachedLine <= 0 || cachedLine >= lineNumber || cachedLine <= bestCachedLine)
                    continue;

                bestCachedLine = cachedLine;
                bestCachedState = pair.Value;
            }

            if (bestCachedLine > 0)
            {
                resumeLine = bestCachedLine + 1;
                state = bestCachedState;
            }

            for (int i = resumeLine; i < lineNumber && Document != null; i++)
            {
                var prevText = Document.GetLineText(i);
                (_, state) = Highlighter.HighlightLine(i, prevText, state);
                _lineStates[i] = state;
            }
        }

        var (tokens, endState) = Highlighter.HighlightLine(lineNumber, lineText, state);
        _lineStates[lineNumber] = endState;
        return new HighlightedLine(lineNumber, tokens);
    }

    private Brush ResolveBrushForClassification(TokenClassification classification, Brush defaultBrush)
    {
        if (ClassificationBrushResolver is not null)
            return ClassificationBrushResolver(classification, GetFallbackBrushForClassification(classification, defaultBrush));

        return GetFallbackBrushForClassification(classification, defaultBrush);
    }

    private Brush GetFallbackBrushForClassification(TokenClassification classification, Brush defaultBrush)
    {
        if (TryResolveApplicationClassificationBrush(classification, out var themeBrush))
            return themeBrush;

        return classification switch
        {
            TokenClassification.Keyword => s_keywordBrush,
            TokenClassification.ControlKeyword => s_controlKeywordBrush,
            TokenClassification.TypeName => s_typeNameBrush,
            TokenClassification.String => s_stringBrush,
            TokenClassification.Character => s_stringBrush,
            TokenClassification.Number => s_numberBrush,
            TokenClassification.Comment => s_commentBrush,
            TokenClassification.XmlDoc => s_xmlDocBrush,
            TokenClassification.Preprocessor => s_preprocessorBrush,
            TokenClassification.Operator => s_operatorBrush,
            TokenClassification.Punctuation => s_punctuationBrush,
            TokenClassification.Method => s_methodBrush,
            TokenClassification.Property => s_propertyBrush,
            TokenClassification.Field => s_propertyBrush,
            TokenClassification.Parameter => s_propertyBrush,
            TokenClassification.Namespace => s_namespaceBrush,
            TokenClassification.Attribute => s_namespaceBrush,
            TokenClassification.BindingKeyword => s_bindingKeywordBrush,
            TokenClassification.BindingParameter => s_bindingParameterBrush,
            TokenClassification.BindingPath => s_bindingPathBrush,
            TokenClassification.BindingOperator => s_bindingOperatorBrush,
            TokenClassification.Error => s_errorBrush,
            _ => defaultBrush,
        };
    }

    private static bool TryResolveApplicationClassificationBrush(TokenClassification classification, out Brush brush)
    {
        if (Application.Current?.Resources.TryGetValue(GetEditorSyntaxBrushKey(classification), out var appResource) == true &&
            appResource is Brush appBrush)
        {
            brush = appBrush;
            return true;
        }

        brush = null!;
        return false;
    }

    private static string GetEditorSyntaxBrushKey(TokenClassification classification)
    {
        return classification switch
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
            _ => "EditorSyntaxPlainText",
        };
    }

    /// <summary>
    /// Tries to get the syntax token at a document offset.
    /// Returns the token with line-relative offsets and the full line text.
    /// </summary>
    public bool TryGetTokenAtOffset(int offset, out int lineNumber, out SyntaxToken token, out string lineText)
    {
        lineNumber = 1;
        token = default;
        lineText = string.Empty;

        if (Document == null || Highlighter == null)
            return false;

        int lineLookupOffset;
        if (Document.TextLength <= 0)
        {
            lineLookupOffset = 0;
        }
        else
        {
            int clamped = Math.Clamp(offset, 0, Document.TextLength);
            lineLookupOffset = Math.Min(clamped, Document.TextLength - 1);
        }

        var line = Document.GetLineByOffset(lineLookupOffset);
        lineNumber = line.LineNumber;
        lineText = Document.GetLineText(lineNumber);
        if (lineText.Length == 0)
            return false;

        int caretColumn = Math.Clamp(offset - line.Offset, 0, lineText.Length);
        var cachedLine = GetOrCreateLineCacheEntry(lineNumber, line);
        var highlighted = GetOrComputeHighlightedLine(cachedLine, lineNumber, lineText);
        if (highlighted.Tokens.Length == 0)
            return false;

        int probeColumn = caretColumn;
        if (probeColumn >= lineText.Length)
            probeColumn = lineText.Length - 1;

        for (int i = 0; i < highlighted.Tokens.Length; i++)
        {
            var current = highlighted.Tokens[i];
            if (current.Length <= 0)
                continue;

            int tokenStart = current.StartOffset;
            int tokenEnd = current.StartOffset + current.Length;
            if (probeColumn >= tokenStart && probeColumn < tokenEnd)
            {
                token = current;
                return true;
            }
        }

        return false;
    }

    private double GetColumnX(EditorViewLine cachedLine, string lineText, int column, double textAreaLeft)
    {
        int clampedColumn = Math.Clamp(column, 0, lineText.Length);
        double prefixWidth = GetPrefixWidth(cachedLine, lineText, clampedColumn);
        return textAreaLeft + prefixWidth - HorizontalOffset;
    }

    private int GetColumnFromRelativeX(EditorViewLine cachedLine, string lineText, double relativeX)
    {
        int length = lineText.Length;
        if (length <= 0 || relativeX <= 0)
            return 0;

        // Use DirectWrite's native hit testing for accurate character mapping
        string fontFamily = GetMeasurementFontFamily();
        double fontSize = GetMeasurementFontSize();
        if (TextMeasurement.HitTestPoint(lineText, fontFamily, fontSize, (float)relativeX, out var hitResult))
        {
            int column = (int)hitResult.TextPosition;
            if (hitResult.IsTrailingHit != 0)
                column++;
            return Math.Clamp(column, 0, length);
        }

        // Fallback: binary search using prefix width measurement
        double lineWidth = GetPrefixWidth(cachedLine, lineText, length);
        if (relativeX >= lineWidth)
            return length;

        int low = 0;
        int high = length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            double midX = GetPrefixWidth(cachedLine, lineText, mid);
            if (midX < relativeX)
                low = mid + 1;
            else
                high = mid;
        }

        int rightColumn = Math.Clamp(low, 0, length);
        int leftColumn = Math.Clamp(rightColumn - 1, 0, length);
        double leftX = GetPrefixWidth(cachedLine, lineText, leftColumn);
        double rightX = GetPrefixWidth(cachedLine, lineText, rightColumn);
        return relativeX - leftX <= rightX - relativeX ? leftColumn : rightColumn;
    }

    private double GetPrefixWidth(EditorViewLine cachedLine, string lineText, int column)
    {
        int clampedColumn = Math.Clamp(column, 0, lineText.Length);
        if (clampedColumn <= 0)
            return 0;

        string fontFamily = GetMeasurementFontFamily();
        double fontSize = GetMeasurementFontSize();
        cachedLine.EnsureGeometryContext(lineText, fontFamily, fontSize);
        if (cachedLine.PrefixWidths.TryGetValue(clampedColumn, out double cachedWidth))
            return cachedWidth;

        double measured = MeasurePrefixWidth(lineText, clampedColumn, fontFamily, fontSize);
        cachedLine.PrefixWidths[clampedColumn] = measured;
        return measured;
    }

    private static double MeasurePrefixWidth(string lineText, int column, string fontFamily, double fontSize)
    {
        if (string.IsNullOrEmpty(lineText) || column <= 0)
            return 0;

        int clampedColumn = Math.Clamp(column, 0, lineText.Length);
        if (clampedColumn <= 0)
            return 0;

        // Use DirectWrite's native hit testing for accurate character position within the full line layout.
        // This ensures the measured position matches what DirectWrite uses during rendering,
        // avoiding drift from measuring prefix substrings independently.
        if (clampedColumn < lineText.Length)
        {
            // Get the leading edge of character at clampedColumn = trailing edge of previous character
            if (TextMeasurement.HitTestTextPosition(lineText, fontFamily, fontSize, (uint)clampedColumn, false, out var hitResult)
                && hitResult.CaretX > 0)
                return hitResult.CaretX;
        }
        else
        {
            // At end of line: get the trailing edge of the last character
            if (TextMeasurement.HitTestTextPosition(lineText, fontFamily, fontSize, (uint)(clampedColumn - 1), true, out var hitResult)
                && hitResult.CaretX > 0)
                return hitResult.CaretX;
        }

        // Fallback: measure the prefix substring directly
        var prefixText = lineText[..clampedColumn];
        if (prefixText.Length == 0)
            return 0;

        var formatted = new FormattedText(prefixText, fontFamily, fontSize);
        TextMeasurement.MeasureText(formatted);
        return formatted.Width > 0 ? formatted.Width : 0;
    }

    private string GetMeasurementFontFamily()
    {
        return string.IsNullOrWhiteSpace(_cachedFontFamily) ? "Cascadia Code" : _cachedFontFamily;
    }

    private double GetMeasurementFontSize()
    {
        return _cachedFontSize > 0 ? _cachedFontSize : 14;
    }

    /// <summary>
    /// Converts a mouse point to a document offset.
    /// </summary>
    public int GetOffsetFromPoint(Point point, bool showLineNumbers)
    {
        if (Document == null || _lineHeight <= 0) return 0;

        double textAreaLeft = showLineNumbers ? TextAreaLeft : 0;

        int lineNum = GetLineNumberFromY(point.Y);
        var line = Document.GetLineByNumber(lineNum);
        var cachedLine = GetOrCreateLineCacheEntry(lineNum, line);
        string lineText = Document.GetLineText(lineNum);

        // Calculate column from X position
        double relativeX = point.X - textAreaLeft + HorizontalOffset;
        int column = GetColumnFromRelativeX(cachedLine, lineText, relativeX);
        column = Math.Clamp(column, 0, line.Length);

        return line.Offset + column;
    }

    /// <summary>
    /// Gets the visual position of a document offset.
    /// </summary>
    public Point GetPointFromOffset(int offset, bool showLineNumbers)
    {
        if (Document == null || _lineHeight <= 0) return Point.Zero;

        int clampedOffset = Math.Clamp(offset, 0, Document.TextLength);
        var sourceLine = Document.GetLineByOffset(clampedOffset);
        int anchorLineNumber = GetVisibleAnchorLineNumber(sourceLine.LineNumber);
        var anchorLine = Document.GetLineByNumber(anchorLineNumber);

        int column;
        if (anchorLineNumber == sourceLine.LineNumber)
        {
            column = Math.Clamp(clampedOffset - sourceLine.Offset, 0, sourceLine.Length);
        }
        else
        {
            column = anchorLine.Length;
        }

        double textAreaLeft = showLineNumbers ? TextAreaLeft : 0;
        string anchorLineText = Document.GetLineText(anchorLineNumber);
        var anchorCachedLine = GetOrCreateLineCacheEntry(anchorLineNumber, anchorLine);
        double x = GetColumnX(anchorCachedLine, anchorLineText, column, textAreaLeft);
        double y = GetLineTop(anchorLineNumber);

        return new Point(x, y);
    }
}
