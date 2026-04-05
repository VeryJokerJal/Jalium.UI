using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="DiffViewer.NavigatedToChange"/> event.
/// </summary>
public sealed class NavigatedToChangeEventArgs : RoutedEventArgs
{
    /// <summary>Gets the zero-based index of the change that was navigated to.</summary>
    public int ChangeIndex { get; }

    /// <summary>Gets the type of the change.</summary>
    public DiffLineType ChangeType { get; }

    public NavigatedToChangeEventArgs(RoutedEvent routedEvent, int changeIndex, DiffLineType changeType)
        : base(routedEvent)
    {
        ChangeIndex = changeIndex;
        ChangeType = changeType;
    }
}

/// <summary>
/// Displays a side-by-side or unified diff between two text documents with
/// line-level and word-level highlighting, virtual scrolling, and keyboard navigation.
/// </summary>
public class DiffViewer : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.DiffViewerAutomationPeer(this);

    #region Cached Brushes

    private static readonly SolidColorBrush s_defaultAddedLineBrush = new(Color.FromArgb(40, 0, 200, 0));
    private static readonly SolidColorBrush s_defaultRemovedLineBrush = new(Color.FromArgb(40, 200, 0, 0));
    private static readonly SolidColorBrush s_defaultModifiedLineBrush = new(Color.FromArgb(40, 200, 200, 0));
    private static readonly SolidColorBrush s_defaultAddedWordBrush = new(Color.FromArgb(80, 0, 200, 0));
    private static readonly SolidColorBrush s_defaultRemovedWordBrush = new(Color.FromArgb(80, 200, 0, 0));
    private static readonly SolidColorBrush s_defaultGutterBackground = new(Color.FromRgb(30, 30, 30));
    private static readonly SolidColorBrush s_defaultLineNumberForeground = new(Color.FromRgb(130, 130, 130));
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(80, 51, 153, 255));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.FromRgb(220, 220, 220));
    private static readonly SolidColorBrush s_defaultTextBrush = new(Color.FromRgb(212, 212, 212));
    private static readonly SolidColorBrush s_defaultSeparatorBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_addedIndicatorBrush = new(Color.FromRgb(0, 180, 0));
    private static readonly SolidColorBrush s_removedIndicatorBrush = new(Color.FromRgb(200, 0, 0));
    private static readonly SolidColorBrush s_minimapAddedBrush = new(Color.FromArgb(160, 0, 200, 0));
    private static readonly SolidColorBrush s_minimapRemovedBrush = new(Color.FromArgb(160, 200, 0, 0));
    private static readonly SolidColorBrush s_minimapModifiedBrush = new(Color.FromArgb(160, 200, 200, 0));
    private static readonly SolidColorBrush s_minimapViewportBrush = new(Color.FromArgb(40, 255, 255, 255));

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the OriginalText dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty OriginalTextProperty =
        DependencyProperty.Register(nameof(OriginalText), typeof(string), typeof(DiffViewer),
            new PropertyMetadata("", OnTextChanged));

    /// <summary>Identifies the ModifiedText dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ModifiedTextProperty =
        DependencyProperty.Register(nameof(ModifiedText), typeof(string), typeof(DiffViewer),
            new PropertyMetadata("", OnTextChanged));

    /// <summary>Identifies the ViewMode dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ViewModeProperty =
        DependencyProperty.Register(nameof(ViewMode), typeof(DiffViewMode), typeof(DiffViewer),
            new PropertyMetadata(DiffViewMode.SideBySide, OnVisualPropertyChanged));

    /// <summary>Identifies the IsReadOnly dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(DiffViewer),
            new PropertyMetadata(true));

    /// <summary>Identifies the ShowLineNumbers dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(DiffViewer),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>Identifies the ShowMinimap dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowMinimapProperty =
        DependencyProperty.Register(nameof(ShowMinimap), typeof(bool), typeof(DiffViewer),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>Identifies the GutterWidth dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty GutterWidthProperty =
        DependencyProperty.Register(nameof(GutterWidth), typeof(double), typeof(DiffViewer),
            new PropertyMetadata(60.0, OnLayoutPropertyChanged));

    /// <summary>Identifies the AddedLineBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AddedLineBrushProperty =
        DependencyProperty.Register(nameof(AddedLineBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the RemovedLineBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RemovedLineBrushProperty =
        DependencyProperty.Register(nameof(RemovedLineBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the ModifiedLineBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ModifiedLineBrushProperty =
        DependencyProperty.Register(nameof(ModifiedLineBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the AddedWordBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AddedWordBrushProperty =
        DependencyProperty.Register(nameof(AddedWordBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the RemovedWordBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RemovedWordBrushProperty =
        DependencyProperty.Register(nameof(RemovedWordBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the GutterBackground dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the LineNumberForeground dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineNumberForegroundProperty =
        DependencyProperty.Register(nameof(LineNumberForeground), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the SelectionBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the CaretBrush dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(DiffViewer),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the EnableInlineEdit dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty EnableInlineEditProperty =
        DependencyProperty.Register(nameof(EnableInlineEdit), typeof(bool), typeof(DiffViewer),
            new PropertyMetadata(false));

    /// <summary>Identifies the ContextLines dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ContextLinesProperty =
        DependencyProperty.Register(nameof(ContextLines), typeof(int), typeof(DiffViewer),
            new PropertyMetadata(3, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>Identifies the DiffComputed routed event.</summary>
    public static readonly RoutedEvent DiffComputedEvent =
        EventManager.RegisterRoutedEvent(nameof(DiffComputed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DiffViewer));

    /// <summary>Occurs when the diff has been recomputed.</summary>
    public event RoutedEventHandler DiffComputed
    {
        add => AddHandler(DiffComputedEvent, value);
        remove => RemoveHandler(DiffComputedEvent, value);
    }

    /// <summary>Identifies the NavigatedToChange routed event.</summary>
    public static readonly RoutedEvent NavigatedToChangeEvent =
        EventManager.RegisterRoutedEvent(nameof(NavigatedToChange), RoutingStrategy.Bubble,
            typeof(EventHandler<NavigatedToChangeEventArgs>), typeof(DiffViewer));

    /// <summary>Occurs when the user navigates to a change via keyboard or API.</summary>
    public event EventHandler<NavigatedToChangeEventArgs> NavigatedToChange
    {
        add => AddHandler(NavigatedToChangeEvent, value);
        remove => RemoveHandler(NavigatedToChangeEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets the original (left/old) text.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string OriginalText
    {
        get => (string)(GetValue(OriginalTextProperty) ?? "");
        set => SetValue(OriginalTextProperty, value);
    }

    /// <summary>Gets or sets the modified (right/new) text.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string ModifiedText
    {
        get => (string)(GetValue(ModifiedTextProperty) ?? "");
        set => SetValue(ModifiedTextProperty, value);
    }

    /// <summary>Gets or sets the view mode (SideBySide or Unified).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public DiffViewMode ViewMode
    {
        get => (DiffViewMode)(GetValue(ViewModeProperty) ?? DiffViewMode.SideBySide);
        set => SetValue(ViewModeProperty, value);
    }

    /// <summary>Gets or sets whether the diff viewer is read-only.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Gets or sets whether line numbers are shown.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty)!;
        set => SetValue(ShowLineNumbersProperty, value);
    }

    /// <summary>Gets or sets whether the minimap is shown.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowMinimap
    {
        get => (bool)GetValue(ShowMinimapProperty)!;
        set => SetValue(ShowMinimapProperty, value);
    }

    /// <summary>Gets or sets the width of the line-number gutter.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double GutterWidth
    {
        get => (double)GetValue(GutterWidthProperty)!;
        set => SetValue(GutterWidthProperty, value);
    }

    /// <summary>Gets or sets the brush for added-line backgrounds.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? AddedLineBrush
    {
        get => (Brush?)GetValue(AddedLineBrushProperty);
        set => SetValue(AddedLineBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for removed-line backgrounds.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? RemovedLineBrush
    {
        get => (Brush?)GetValue(RemovedLineBrushProperty);
        set => SetValue(RemovedLineBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for modified-line backgrounds.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ModifiedLineBrush
    {
        get => (Brush?)GetValue(ModifiedLineBrushProperty);
        set => SetValue(ModifiedLineBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for added-word highlights.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? AddedWordBrush
    {
        get => (Brush?)GetValue(AddedWordBrushProperty);
        set => SetValue(AddedWordBrushProperty, value);
    }

    /// <summary>Gets or sets the brush for removed-word highlights.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? RemovedWordBrush
    {
        get => (Brush?)GetValue(RemovedWordBrushProperty);
        set => SetValue(RemovedWordBrushProperty, value);
    }

    /// <summary>Gets or sets the gutter background brush.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    /// <summary>Gets or sets the line-number foreground brush.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LineNumberForeground
    {
        get => (Brush?)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    /// <summary>Gets or sets the selection highlight brush.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>Gets or sets the caret brush.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>Gets or sets whether inline editing is enabled on the modified side.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool EnableInlineEdit
    {
        get => (bool)GetValue(EnableInlineEditProperty)!;
        set => SetValue(EnableInlineEditProperty, value);
    }

    /// <summary>Gets or sets the number of unchanged context lines shown around each change.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int ContextLines
    {
        get => (int)GetValue(ContextLinesProperty)!;
        set => SetValue(ContextLinesProperty, value);
    }

    #endregion

    #region Private State

    private List<DiffLine> _diffLines = new();
    private List<int> _changeIndices = new(); // indices into _diffLines for changed lines
    private int _currentChangeIndex = -1;

    // Scrolling
    private double _scrollOffsetY;
    private double _scrollOffsetX;
    private double _lineHeight = 18;
    private double _charWidth = 7.8;
    private double _totalContentHeight;

    // Selection
    private bool _isSelecting;
    private int _selectionStartLine = -1;
    private int _selectionEndLine = -1;
    private Point _selectionStartPoint;

    // Minimap
    private const double MinimapWidth = 60;

    // Layout cache
    private double _effectiveGutterWidth;
    private double _contentAreaWidth;
    private int _visibleLineCount;
    private int _firstVisibleLine;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DiffViewer"/> class.
    /// </summary>
    public DiffViewer()
    {
        Focusable = true;
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets the total number of changes (added, removed, or modified lines) in the diff.
    /// </summary>
    public int GetChangeCount() => _changeIndices.Count;

    /// <summary>
    /// Navigates to the next change in the diff. Wraps around to the first change.
    /// </summary>
    public void NavigateToNextChange()
    {
        if (_changeIndices.Count == 0) return;

        _currentChangeIndex++;
        if (_currentChangeIndex >= _changeIndices.Count)
            _currentChangeIndex = 0;

        ScrollToChange(_currentChangeIndex);
    }

    /// <summary>
    /// Navigates to the previous change in the diff. Wraps around to the last change.
    /// </summary>
    public void NavigateToPreviousChange()
    {
        if (_changeIndices.Count == 0) return;

        _currentChangeIndex--;
        if (_currentChangeIndex < 0)
            _currentChangeIndex = _changeIndices.Count - 1;

        ScrollToChange(_currentChangeIndex);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);

        // Measure character dimensions using current font settings
        MeasureCharacterDimensions();

        var desiredWidth = double.IsPositiveInfinity(availableSize.Width) ? 600 : availableSize.Width;
        var desiredHeight = double.IsPositiveInfinity(availableSize.Height) ? 400 : availableSize.Height;

        return new Size(desiredWidth, desiredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        base.ArrangeOverride(finalSize);
        UpdateLayoutMetrics(finalSize);
        return finalSize;
    }

    private void MeasureCharacterDimensions()
    {
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 13;

        var measureText = new FormattedText("M", fontFamily, fontSize)
        {
            Foreground = s_defaultTextBrush
        };
        TextMeasurement.MeasureText(measureText);

        _charWidth = measureText.Width > 0 ? measureText.Width : 7.8;
        _lineHeight = measureText.Height > 0 ? measureText.Height + 4 : 18;
    }

    private void UpdateLayoutMetrics(Size size)
    {
        _effectiveGutterWidth = ShowLineNumbers ? GutterWidth : 0;

        double minimapW = ShowMinimap ? MinimapWidth : 0;

        if (ViewMode == DiffViewMode.SideBySide)
        {
            // Each side gets half the remaining width (minus separator and gutter for each side)
            _contentAreaWidth = (size.Width - minimapW - 1) / 2.0 - _effectiveGutterWidth;
        }
        else
        {
            // Unified: single gutter + indicator column + content
            _contentAreaWidth = size.Width - _effectiveGutterWidth - minimapW - 20; // 20 for +/- indicator
        }

        if (_contentAreaWidth < 0) _contentAreaWidth = 0;

        _visibleLineCount = (int)Math.Ceiling(size.Height / _lineHeight) + 1;
        _totalContentHeight = _diffLines.Count * _lineHeight;
        _firstVisibleLine = (int)Math.Floor(_scrollOffsetY / _lineHeight);

        // Clamp scroll
        double maxScroll = Math.Max(0, _totalContentHeight - size.Height);
        if (_scrollOffsetY > maxScroll)
            _scrollOffsetY = maxScroll;
        if (_scrollOffsetY < 0)
            _scrollOffsetY = 0;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        var bgBrush = Background ?? ResolveThemeBrush("ControlBackground", s_defaultGutterBackground);
        dc.DrawRectangle(bgBrush, null, bounds);

        if (_diffLines.Count == 0) return;

        UpdateLayoutMetrics(RenderSize);

        if (ViewMode == DiffViewMode.SideBySide)
        {
            RenderSideBySide(dc, bounds);
        }
        else
        {
            RenderUnified(dc, bounds);
        }

        if (ShowMinimap)
        {
            RenderMinimap(dc, bounds);
        }
    }

    private void RenderSideBySide(DrawingContext dc, Rect bounds)
    {
        double halfWidth = (bounds.Width - (ShowMinimap ? MinimapWidth : 0) - 1) / 2.0;
        double leftX = 0;
        double rightX = halfWidth + 1;

        // Draw separator
        dc.DrawRectangle(s_defaultSeparatorBrush, null, new Rect(halfWidth, 0, 1, bounds.Height));

        // Draw left panel (original)
        RenderPanel(dc, leftX, halfWidth, isOriginalSide: true);

        // Draw right panel (modified)
        RenderPanel(dc, rightX, halfWidth, isOriginalSide: false);
    }

    private void RenderPanel(DrawingContext dc, double panelX, double panelWidth, bool isOriginalSide)
    {
        var gutterBrush = GutterBackground ?? s_defaultGutterBackground;
        var lineNumBrush = LineNumberForeground ?? s_defaultLineNumberForeground;
        var selBrush = SelectionBrush ?? s_defaultSelectionBrush;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 13;

        double gutterW = _effectiveGutterWidth;
        double textX = panelX + gutterW + 4; // 4px padding

        // Draw gutter background
        if (ShowLineNumbers)
        {
            dc.DrawRectangle(gutterBrush, null, new Rect(panelX, 0, gutterW, RenderSize.Height));
        }

        int endLine = Math.Min(_firstVisibleLine + _visibleLineCount, _diffLines.Count);
        for (int i = _firstVisibleLine; i < endLine; i++)
        {
            var line = _diffLines[i];
            double y = (i - _firstVisibleLine) * _lineHeight - (_scrollOffsetY % _lineHeight);

            if (y + _lineHeight < 0 || y > RenderSize.Height) continue;

            // Line background
            var lineBg = GetLineBrush(line.LineType, isOriginalSide);
            if (lineBg != null)
            {
                dc.DrawRectangle(lineBg, null, new Rect(panelX + gutterW, y, panelWidth - gutterW, _lineHeight));
            }

            // Selection highlight
            if (_selectionStartLine >= 0 && _selectionEndLine >= 0)
            {
                int selMin = Math.Min(_selectionStartLine, _selectionEndLine);
                int selMax = Math.Max(_selectionStartLine, _selectionEndLine);
                if (i >= selMin && i <= selMax)
                {
                    dc.DrawRectangle(selBrush, null, new Rect(panelX + gutterW, y, panelWidth - gutterW, _lineHeight));
                }
            }

            // Line number
            if (ShowLineNumbers)
            {
                int? lineNum = isOriginalSide ? line.OriginalLineNumber : line.ModifiedLineNumber;
                if (lineNum.HasValue)
                {
                    var numText = lineNum.Value.ToString();
                    var ft = new FormattedText(numText, fontFamily, fontSize)
                    {
                        Foreground = lineNumBrush
                    };
                    TextMeasurement.MeasureText(ft);
                    double numX = panelX + gutterW - ft.Width - 4;
                    dc.DrawText(ft, new Point(numX, y));
                }
            }

            // Line text with word-level diffs
            string text = isOriginalSide ? line.OriginalText : line.ModifiedText;

            if (line.LineType == DiffLineType.Modified && line.WordDiffs != null)
            {
                RenderWordDiffs(dc, line.WordDiffs, textX - _scrollOffsetX, y, fontFamily, fontSize, isOriginalSide);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                var textBrush = Foreground ?? s_defaultTextBrush;
                var ft = new FormattedText(text, fontFamily, fontSize)
                {
                    Foreground = textBrush
                };
                TextMeasurement.MeasureText(ft);
                dc.DrawText(ft, new Point(textX - _scrollOffsetX, y));
            }
        }
    }

    private void RenderUnified(DrawingContext dc, Rect bounds)
    {
        var gutterBrush = GutterBackground ?? s_defaultGutterBackground;
        var lineNumBrush = LineNumberForeground ?? s_defaultLineNumberForeground;
        var selBrush = SelectionBrush ?? s_defaultSelectionBrush;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 13;

        double gutterW = _effectiveGutterWidth;
        double indicatorX = gutterW + 2;
        double textX = gutterW + 20 + 4;

        // Draw gutter background
        if (ShowLineNumbers)
        {
            dc.DrawRectangle(gutterBrush, null, new Rect(0, 0, gutterW, bounds.Height));
        }

        int endLine = Math.Min(_firstVisibleLine + _visibleLineCount, _diffLines.Count);
        for (int i = _firstVisibleLine; i < endLine; i++)
        {
            var line = _diffLines[i];
            double y = (i - _firstVisibleLine) * _lineHeight - (_scrollOffsetY % _lineHeight);

            if (y + _lineHeight < 0 || y > RenderSize.Height) continue;

            // Line background
            var lineBg = GetLineBrushUnified(line.LineType);
            if (lineBg != null)
            {
                dc.DrawRectangle(lineBg, null, new Rect(gutterW, y, bounds.Width - gutterW, _lineHeight));
            }

            // Selection highlight
            if (_selectionStartLine >= 0 && _selectionEndLine >= 0)
            {
                int selMin = Math.Min(_selectionStartLine, _selectionEndLine);
                int selMax = Math.Max(_selectionStartLine, _selectionEndLine);
                if (i >= selMin && i <= selMax)
                {
                    dc.DrawRectangle(selBrush, null, new Rect(gutterW, y, bounds.Width - gutterW, _lineHeight));
                }
            }

            // Line numbers (original | modified)
            if (ShowLineNumbers)
            {
                if (line.OriginalLineNumber.HasValue)
                {
                    var numText = line.OriginalLineNumber.Value.ToString();
                    var ft = new FormattedText(numText, fontFamily, fontSize)
                    {
                        Foreground = lineNumBrush
                    };
                    TextMeasurement.MeasureText(ft);
                    double numX = gutterW / 2.0 - ft.Width - 2;
                    dc.DrawText(ft, new Point(Math.Max(2, numX), y));
                }

                if (line.ModifiedLineNumber.HasValue)
                {
                    var numText = line.ModifiedLineNumber.Value.ToString();
                    var ft = new FormattedText(numText, fontFamily, fontSize)
                    {
                        Foreground = lineNumBrush
                    };
                    TextMeasurement.MeasureText(ft);
                    double numX = gutterW - ft.Width - 4;
                    dc.DrawText(ft, new Point(Math.Max(gutterW / 2.0 + 2, numX), y));
                }
            }

            // +/- indicator
            string indicator = line.LineType switch
            {
                DiffLineType.Added => "+",
                DiffLineType.Removed => "-",
                DiffLineType.Modified => "~",
                _ => " "
            };

            var indicatorBrush = line.LineType switch
            {
                DiffLineType.Added => s_addedIndicatorBrush,
                DiffLineType.Removed => s_removedIndicatorBrush,
                DiffLineType.Modified => (Brush)s_defaultModifiedLineBrush,
                _ => s_defaultLineNumberForeground
            };

            var indFt = new FormattedText(indicator, fontFamily, fontSize)
            {
                Foreground = indicatorBrush
            };
            TextMeasurement.MeasureText(indFt);
            dc.DrawText(indFt, new Point(indicatorX, y));

            // Text content
            string text = line.LineType switch
            {
                DiffLineType.Removed => line.OriginalText,
                DiffLineType.Added => line.ModifiedText,
                DiffLineType.Modified => line.ModifiedText,
                _ => line.OriginalText
            };

            if (line.LineType == DiffLineType.Modified && line.WordDiffs != null)
            {
                RenderWordDiffs(dc, line.WordDiffs, textX - _scrollOffsetX, y, fontFamily, fontSize, isOriginalSide: false);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                var textBrush = Foreground ?? s_defaultTextBrush;
                var ft = new FormattedText(text, fontFamily, fontSize)
                {
                    Foreground = textBrush
                };
                TextMeasurement.MeasureText(ft);
                dc.DrawText(ft, new Point(textX - _scrollOffsetX, y));
            }
        }
    }

    private void RenderWordDiffs(DrawingContext dc, List<WordDiff> wordDiffs, double startX, double y,
        string fontFamily, double fontSize, bool isOriginalSide)
    {
        double x = startX;
        var textBrush = Foreground ?? s_defaultTextBrush;
        var addedBrush = AddedWordBrush ?? s_defaultAddedWordBrush;
        var removedBrush = RemovedWordBrush ?? s_defaultRemovedWordBrush;

        foreach (var wd in wordDiffs)
        {
            // On the original side, show Removed and Unchanged tokens; skip Added.
            // On the modified side, show Added and Unchanged tokens; skip Removed.
            if (isOriginalSide && wd.Type == DiffLineType.Added) continue;
            if (!isOriginalSide && wd.Type == DiffLineType.Removed) continue;

            if (string.IsNullOrEmpty(wd.Text)) continue;

            // Draw word-level highlight background
            if (wd.Type == DiffLineType.Added || wd.Type == DiffLineType.Removed)
            {
                var measureFt = new FormattedText(wd.Text, fontFamily, fontSize)
                {
                    Foreground = textBrush
                };
                TextMeasurement.MeasureText(measureFt);

                var highlightBrush = wd.Type == DiffLineType.Added ? addedBrush : removedBrush;
                dc.DrawRectangle(highlightBrush, null, new Rect(x, y, measureFt.Width, _lineHeight));
            }

            var ft = new FormattedText(wd.Text, fontFamily, fontSize)
            {
                Foreground = textBrush
            };
            TextMeasurement.MeasureText(ft);
            dc.DrawText(ft, new Point(x, y));
            x += ft.Width;
        }
    }

    private void RenderMinimap(DrawingContext dc, Rect bounds)
    {
        if (_diffLines.Count == 0) return;

        double minimapX = bounds.Width - MinimapWidth;
        double minimapHeight = bounds.Height;

        // Minimap background
        dc.DrawRectangle(s_defaultGutterBackground, null, new Rect(minimapX, 0, MinimapWidth, minimapHeight));

        // Draw separator line
        dc.DrawRectangle(s_defaultSeparatorBrush, null, new Rect(minimapX, 0, 1, minimapHeight));

        // Scale factor: map all lines into the minimap height
        double lineScale = minimapHeight / Math.Max(1, _diffLines.Count);
        double minLineH = Math.Max(1, lineScale);

        for (int i = 0; i < _diffLines.Count; i++)
        {
            var line = _diffLines[i];
            Brush? brush = line.LineType switch
            {
                DiffLineType.Added => s_minimapAddedBrush,
                DiffLineType.Removed => s_minimapRemovedBrush,
                DiffLineType.Modified => s_minimapModifiedBrush,
                _ => null
            };

            if (brush != null)
            {
                double my = i * lineScale;
                dc.DrawRectangle(brush, null, new Rect(minimapX + 2, my, MinimapWidth - 4, minLineH));
            }
        }

        // Draw viewport indicator
        if (_totalContentHeight > 0)
        {
            double viewportTop = (_scrollOffsetY / _totalContentHeight) * minimapHeight;
            double viewportH = (bounds.Height / Math.Max(1, _totalContentHeight)) * minimapHeight;
            viewportH = Math.Max(10, Math.Min(minimapHeight, viewportH));

            dc.DrawRectangle(s_minimapViewportBrush, null, new Rect(minimapX, viewportTop, MinimapWidth, viewportH));
        }
    }

    private Brush? GetLineBrush(DiffLineType lineType, bool isOriginalSide)
    {
        return lineType switch
        {
            DiffLineType.Added when !isOriginalSide => AddedLineBrush ?? s_defaultAddedLineBrush,
            DiffLineType.Added when isOriginalSide => null, // empty on original side
            DiffLineType.Removed when isOriginalSide => RemovedLineBrush ?? s_defaultRemovedLineBrush,
            DiffLineType.Removed when !isOriginalSide => null, // empty on modified side
            DiffLineType.Modified => ModifiedLineBrush ?? s_defaultModifiedLineBrush,
            _ => null
        };
    }

    private Brush? GetLineBrushUnified(DiffLineType lineType)
    {
        return lineType switch
        {
            DiffLineType.Added => AddedLineBrush ?? s_defaultAddedLineBrush,
            DiffLineType.Removed => RemovedLineBrush ?? s_defaultRemovedLineBrush,
            DiffLineType.Modified => ModifiedLineBrush ?? s_defaultModifiedLineBrush,
            _ => null
        };
    }

    #endregion

    #region Theme Resolution

    private Brush ResolveThemeBrush(string resourceKey, Brush fallback, string? secondaryResourceKey = null)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        if (secondaryResourceKey != null && TryFindResource(secondaryResourceKey) is Brush secondaryBrush)
        {
            return secondaryBrush;
        }

        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null)
        {
            if (app.Resources.TryGetValue(resourceKey, out var appResource) && appResource is Brush appBrush)
            {
                return appBrush;
            }

            if (secondaryResourceKey != null &&
                app.Resources.TryGetValue(secondaryResourceKey, out var secondaryAppResource) &&
                secondaryAppResource is Brush secondaryAppBrush)
            {
                return secondaryAppBrush;
            }
        }

        return fallback;
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            CaptureMouse();

            var position = e.GetPosition(this);

            // Check if clicking on minimap for quick navigation
            if (ShowMinimap && position.X >= RenderSize.Width - MinimapWidth)
            {
                double ratio = position.Y / RenderSize.Height;
                _scrollOffsetY = ratio * Math.Max(0, _totalContentHeight - RenderSize.Height);
                ClampScrollOffset();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // Start selection
            _isSelecting = true;
            _selectionStartLine = GetLineIndexFromY(position.Y);
            _selectionEndLine = _selectionStartLine;
            _selectionStartPoint = position;

            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isSelecting)
        {
            var position = e.GetPosition(this);
            _selectionEndLine = GetLineIndexFromY(position.Y);

            // Auto-scroll when dragging near edges
            if (position.Y < 0)
            {
                _scrollOffsetY = Math.Max(0, _scrollOffsetY - _lineHeight);
                ClampScrollOffset();
            }
            else if (position.Y > RenderSize.Height)
            {
                _scrollOffsetY += _lineHeight;
                ClampScrollOffset();
            }

            InvalidateVisual();
        }
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (e.KeyboardModifiers.HasFlag(ModifierKeys.Shift))
        {
            // Horizontal scroll
            _scrollOffsetX -= e.Delta / 120.0 * _charWidth * 3;
            if (_scrollOffsetX < 0) _scrollOffsetX = 0;
        }
        else
        {
            // Vertical scroll: 3 lines per notch
            double delta = -e.Delta / 120.0 * _lineHeight * 3;
            _scrollOffsetY += delta;
            ClampScrollOffset();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when e.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                NavigateToNextChange();
                e.Handled = true;
                break;

            case Key.Up when e.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                NavigateToPreviousChange();
                e.Handled = true;
                break;

            case Key.Down:
                _scrollOffsetY += _lineHeight;
                ClampScrollOffset();
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.Up:
                _scrollOffsetY -= _lineHeight;
                ClampScrollOffset();
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.PageDown:
                _scrollOffsetY += RenderSize.Height;
                ClampScrollOffset();
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.PageUp:
                _scrollOffsetY -= RenderSize.Height;
                ClampScrollOffset();
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.Home when e.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                _scrollOffsetY = 0;
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.End when e.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                _scrollOffsetY = Math.Max(0, _totalContentHeight - RenderSize.Height);
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.Left:
                _scrollOffsetX = Math.Max(0, _scrollOffsetX - _charWidth * 3);
                InvalidateVisual();
                e.Handled = true;
                break;

            case Key.Right:
                _scrollOffsetX += _charWidth * 3;
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    private int GetLineIndexFromY(double y)
    {
        int line = (int)Math.Floor((y + _scrollOffsetY) / _lineHeight);
        return Math.Clamp(line, 0, Math.Max(0, _diffLines.Count - 1));
    }

    private void ClampScrollOffset()
    {
        double maxScroll = Math.Max(0, _totalContentHeight - RenderSize.Height);
        _scrollOffsetY = Math.Clamp(_scrollOffsetY, 0, maxScroll);
    }

    #endregion

    #region Diff Computation

    private void RecomputeDiff()
    {
        var originalText = OriginalText;
        var modifiedText = ModifiedText;

        _diffLines = DiffComputer.ComputeDiff(originalText, modifiedText);

        // Rebuild change index list
        _changeIndices.Clear();
        for (int i = 0; i < _diffLines.Count; i++)
        {
            if (_diffLines[i].LineType != DiffLineType.Unchanged)
            {
                _changeIndices.Add(i);
            }
        }

        _currentChangeIndex = _changeIndices.Count > 0 ? 0 : -1;
        _totalContentHeight = _diffLines.Count * _lineHeight;

        // Reset selection
        _selectionStartLine = -1;
        _selectionEndLine = -1;

        InvalidateMeasure();
        InvalidateVisual();

        RaiseEvent(new RoutedEventArgs(DiffComputedEvent));
    }

    private void ScrollToChange(int changeIndex)
    {
        if (changeIndex < 0 || changeIndex >= _changeIndices.Count) return;

        int lineIndex = _changeIndices[changeIndex];
        var diffLine = _diffLines[lineIndex];

        // Center the change in the viewport
        double targetY = lineIndex * _lineHeight - (RenderSize.Height / 2.0) + (_lineHeight / 2.0);
        _scrollOffsetY = Math.Max(0, targetY);
        ClampScrollOffset();

        InvalidateVisual();

        RaiseEvent(new NavigatedToChangeEventArgs(
            NavigatedToChangeEvent, changeIndex, diffLine.LineType));
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer.RecomputeDiff();
        }
    }

    private new static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DiffViewer viewer)
        {
            viewer.InvalidateMeasure();
            viewer.InvalidateVisual();
        }
    }

    #endregion
}
