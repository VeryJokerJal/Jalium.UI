using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A hex editor control that displays and edits binary data in hexadecimal format.
/// Provides offset, hex, and ASCII columns with selection, caret navigation,
/// keyboard hex entry, and data interpretation.
/// </summary>
public class HexEditor : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.HexEditorAutomationPeer(this);
    }

    // Default brushes
    private static readonly SolidColorBrush s_defaultForeground = new(Color.FromRgb(212, 212, 212));
    private static readonly SolidColorBrush s_defaultOffsetForeground = new(Color.FromRgb(86, 156, 214));
    private static readonly SolidColorBrush s_defaultHexForeground = new(Color.FromRgb(212, 212, 212));
    private static readonly SolidColorBrush s_defaultAsciiForeground = new(Color.FromRgb(206, 145, 120));
    private static readonly SolidColorBrush s_defaultModifiedBrush = new(Color.FromRgb(255, 80, 80));
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(100, 51, 153, 255));
    private static readonly SolidColorBrush s_defaultGutterBackground = new(Color.FromRgb(37, 37, 38));
    private static readonly SolidColorBrush s_defaultColumnSeparator = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_caretBrush = new(Color.FromRgb(255, 255, 255));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(byte[]), typeof(HexEditor),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(HexEditor),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the BytesPerRow dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BytesPerRowProperty =
        DependencyProperty.Register(nameof(BytesPerRow), typeof(int), typeof(HexEditor),
            new PropertyMetadata(16, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ColumnGroupSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnGroupSizeProperty =
        DependencyProperty.Register(nameof(ColumnGroupSize), typeof(int), typeof(HexEditor),
            new PropertyMetadata(8, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DisplayFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DisplayFormatProperty =
        DependencyProperty.Register(nameof(DisplayFormat), typeof(HexDisplayFormat), typeof(HexEditor),
            new PropertyMetadata(HexDisplayFormat.Byte, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Endianness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty EndiannessProperty =
        DependencyProperty.Register(nameof(Endianness), typeof(Endianness), typeof(HexEditor),
            new PropertyMetadata(Endianness.Little, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowOffsetColumn dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowOffsetColumnProperty =
        DependencyProperty.Register(nameof(ShowOffsetColumn), typeof(bool), typeof(HexEditor),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowAsciiColumn dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowAsciiColumnProperty =
        DependencyProperty.Register(nameof(ShowAsciiColumn), typeof(bool), typeof(HexEditor),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowDataInterpretation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowDataInterpretationProperty =
        DependencyProperty.Register(nameof(ShowDataInterpretation), typeof(bool), typeof(HexEditor),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionStart dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionStartProperty =
        DependencyProperty.Register(nameof(SelectionStart), typeof(long), typeof(HexEditor),
            new PropertyMetadata(-1L, OnSelectionChanged));

    /// <summary>
    /// Identifies the SelectionLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionLengthProperty =
        DependencyProperty.Register(nameof(SelectionLength), typeof(long), typeof(HexEditor),
            new PropertyMetadata(0L, OnSelectionChanged));

    /// <summary>
    /// Identifies the CaretOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CaretOffsetProperty =
        DependencyProperty.Register(nameof(CaretOffset), typeof(long), typeof(HexEditor),
            new PropertyMetadata(0L, OnCaretOffsetChanged));

    /// <summary>
    /// Identifies the OffsetForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty OffsetForegroundProperty =
        DependencyProperty.Register(nameof(OffsetForeground), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HexForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HexForegroundProperty =
        DependencyProperty.Register(nameof(HexForeground), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AsciiForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AsciiForegroundProperty =
        DependencyProperty.Register(nameof(AsciiForeground), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ModifiedByteBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ModifiedByteBrushProperty =
        DependencyProperty.Register(nameof(ModifiedByteBrush), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the GutterBackground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ColumnSeparatorBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColumnSeparatorBrushProperty =
        DependencyProperty.Register(nameof(ColumnSeparatorBrush), typeof(Brush), typeof(HexEditor),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the OffsetFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty OffsetFormatProperty =
        DependencyProperty.Register(nameof(OffsetFormat), typeof(string), typeof(HexEditor),
            new PropertyMetadata("X8", OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<HexSelectionChangedEventArgs>), typeof(HexEditor));

    /// <summary>
    /// Occurs when the byte selection changes.
    /// </summary>
    public event EventHandler<HexSelectionChangedEventArgs> SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    /// <summary>
    /// Identifies the ByteModified routed event.
    /// </summary>
    public static readonly RoutedEvent ByteModifiedEvent =
        EventManager.RegisterRoutedEvent(nameof(ByteModified), RoutingStrategy.Bubble,
            typeof(EventHandler<HexByteModifiedEventArgs>), typeof(HexEditor));

    /// <summary>
    /// Occurs when a byte is modified.
    /// </summary>
    public event EventHandler<HexByteModifiedEventArgs> ByteModified
    {
        add => AddHandler(ByteModifiedEvent, value);
        remove => RemoveHandler(ByteModifiedEvent, value);
    }

    /// <summary>
    /// Identifies the CaretMoved routed event.
    /// </summary>
    public static readonly RoutedEvent CaretMovedEvent =
        EventManager.RegisterRoutedEvent(nameof(CaretMoved), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(HexEditor));

    /// <summary>
    /// Occurs when the caret position changes.
    /// </summary>
    public event RoutedEventHandler CaretMoved
    {
        add => AddHandler(CaretMovedEvent, value);
        remove => RemoveHandler(CaretMovedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the binary data to display and edit.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public byte[]? Data
    {
        get => (byte[]?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the data is read-only.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of bytes displayed per row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int BytesPerRow
    {
        get => (int)GetValue(BytesPerRowProperty)!;
        set => SetValue(BytesPerRowProperty, value);
    }

    /// <summary>
    /// Gets or sets the column group size for visual grouping in the hex column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int ColumnGroupSize
    {
        get => (int)GetValue(ColumnGroupSizeProperty)!;
        set => SetValue(ColumnGroupSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the display format for hex values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public HexDisplayFormat DisplayFormat
    {
        get => (HexDisplayFormat)(GetValue(DisplayFormatProperty) ?? HexDisplayFormat.Byte);
        set => SetValue(DisplayFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the byte order for multi-byte display formats.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Endianness Endianness
    {
        get => (Endianness)(GetValue(EndiannessProperty) ?? Endianness.Little);
        set => SetValue(EndiannessProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the offset column is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowOffsetColumn
    {
        get => (bool)GetValue(ShowOffsetColumnProperty)!;
        set => SetValue(ShowOffsetColumnProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ASCII column is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowAsciiColumn
    {
        get => (bool)GetValue(ShowAsciiColumnProperty)!;
        set => SetValue(ShowAsciiColumnProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the data interpretation panel is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowDataInterpretation
    {
        get => (bool)GetValue(ShowDataInterpretationProperty)!;
        set => SetValue(ShowDataInterpretationProperty, value);
    }

    /// <summary>
    /// Gets or sets the start offset of the selection (-1 for no selection).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public long SelectionStart
    {
        get => (long)GetValue(SelectionStartProperty)!;
        set => SetValue(SelectionStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the length of the selection in bytes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public long SelectionLength
    {
        get => (long)GetValue(SelectionLengthProperty)!;
        set => SetValue(SelectionLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the caret byte offset.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public long CaretOffset
    {
        get => (long)GetValue(CaretOffsetProperty)!;
        set => SetValue(CaretOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for offset column text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? OffsetForeground
    {
        get => (Brush?)GetValue(OffsetForegroundProperty);
        set => SetValue(OffsetForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for hex column text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? HexForeground
    {
        get => (Brush?)GetValue(HexForegroundProperty);
        set => SetValue(HexForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for ASCII column text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? AsciiForeground
    {
        get => (Brush?)GetValue(AsciiForegroundProperty);
        set => SetValue(AsciiForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to highlight modified bytes.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ModifiedByteBrush
    {
        get => (Brush?)GetValue(ModifiedByteBrushProperty);
        set => SetValue(ModifiedByteBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the selection background.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush for the offset gutter.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for column separators.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ColumnSeparatorBrush
    {
        get => (Brush?)GetValue(ColumnSeparatorBrushProperty);
        set => SetValue(ColumnSeparatorBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for offset display (e.g., "X8" for 8-digit hex).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public string OffsetFormat
    {
        get => (string)(GetValue(OffsetFormatProperty) ?? "X8");
        set => SetValue(OffsetFormatProperty, value);
    }

    #endregion

    #region Private Fields

    private const double ColumnSeparatorWidth = 1.0;
    private const double ColumnPadding = 8.0;
    private const double DataInterpretationPanelWidth = 200.0;
    private const double CaretBlinkIntervalMs = 530;

    // Layout cache
    private double _charWidth;
    private double _rowHeight;
    private double _offsetColumnWidth;
    private double _hexColumnWidth;
    private double _asciiColumnWidth;
    private double _hexColumnStartX;
    private double _asciiColumnStartX;
    private double _dataInterpretationStartX;

    // Scroll state
    private long _scrollOffset; // first visible byte offset (row-aligned)
    private int _visibleRowCount;

    // Interaction state
    private bool _isDragging;
    private long _dragStartOffset;
    private bool _isCaretInAsciiPane;
    private bool _isHighNibble = true; // true = editing high nibble of current byte

    // Per-row string cache for pixel-accurate positioning
    // Key: row byte offset, Value: (hexString, asciiString, byteToHexCharIndex[], fontFamily, fontSize)
    private readonly Dictionary<long, RowStringCache> _rowCache = new();

    private sealed class RowStringCache
    {
        public string HexString = "";
        public string AsciiString = "";
        public int[] ByteToHexCharStart = Array.Empty<int>(); // char index where each byte's hex starts
        public string FontFamily = "";
        public double FontSize;
    }

    // Modified byte tracking
    private readonly HashSet<long> _modifiedBytes = new();

    // Caret blink
    private bool _caretVisible = true;
    private long _lastCaretBlinkTicks;

    // Vertical scrollbar
    private readonly Primitives.ScrollBar _verticalScrollBar;
    private bool _isUpdatingScrollBar;
    private const double ScrollBarWidth = 14.0;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="HexEditor"/> class.
    /// </summary>
    public HexEditor()
    {
        Focusable = true;

        // Create vertical scrollbar
        _verticalScrollBar = new Primitives.ScrollBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            SmallChange = 1,
            LargeChange = 10,
            Focusable = false,
            IsThumbSlim = true,
            Cursor = Jalium.UI.Cursors.Arrow,
        };
        _verticalScrollBar.Scroll += OnScrollBarScroll;
        AddVisualChild(_verticalScrollBar);

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocusHandler));
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        if (_isUpdatingScrollBar) return;
        var bytesPerRow = Math.Max(1, BytesPerRow);
        long newRow = (long)Math.Round(_verticalScrollBar.Value);
        _scrollOffset = newRow * bytesPerRow;

        var data = Data;
        var dataLength = data?.Length ?? 0;
        long maxOffset = Math.Max(0, ((long)dataLength / bytesPerRow - _visibleRowCount + 1) * bytesPerRow);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);
        InvalidateVisual();
    }

    private void UpdateScrollBar()
    {
        var data = Data;
        var dataLength = data?.Length ?? 0;
        var bytesPerRow = Math.Max(1, BytesPerRow);
        long totalRows = dataLength > 0 ? ((dataLength - 1) / bytesPerRow) + 1 : 1;

        _isUpdatingScrollBar = true;
        _verticalScrollBar.Maximum = Math.Max(0, totalRows - _visibleRowCount);
        _verticalScrollBar.ViewportSize = _visibleRowCount;
        _verticalScrollBar.LargeChange = Math.Max(1, _visibleRowCount - 1);
        _verticalScrollBar.Value = _scrollOffset / bytesPerRow;
        _isUpdatingScrollBar = false;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (HexEditor)d;
        editor._modifiedBytes.Clear();
        editor._scrollOffset = 0;
        editor.CaretOffset = 0;
        editor.SelectionStart = -1;
        editor.SelectionLength = 0;
        editor._isHighNibble = true;
        editor.InvalidateMeasure();
        editor.InvalidateVisual();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (HexEditor)d;
        editor.InvalidateVisual();
    }

    private static void OnCaretOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (HexEditor)d;
        editor._isHighNibble = true;
        editor._caretVisible = true;
        editor._lastCaretBlinkTicks = Environment.TickCount64;
        editor.EnsureCaretVisible();
        editor.RaiseEvent(new RoutedEventArgs(CaretMovedEvent));
        editor.InvalidateVisual();
    }

    #endregion

    #region Visual Children (scrollbar)

    public override int VisualChildrenCount => base.VisualChildrenCount + 1;

    public override Visual? GetVisualChild(int index)
    {
        if (index == base.VisualChildrenCount)
            return _verticalScrollBar;
        return base.GetVisualChild(index);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontFamily = GetMonospaceFont();
        var fontSize = GetFontSize();

        var charSize = MeasureChar('0', fontFamily, fontSize);
        _charWidth = charSize.Width;
        _rowHeight = charSize.Height + 2; // 2px line spacing

        var bytesPerRow = Math.Max(1, BytesPerRow);

        // Calculate column widths using actual text measurement
        if (ShowOffsetColumn)
        {
            var sampleOffset = (0L).ToString(OffsetFormat);
            var offsetFt = new FormattedText(sampleOffset + "  ", fontFamily, fontSize) { Foreground = s_defaultForeground };
            TextMeasurement.MeasureText(offsetFt);
            _offsetColumnWidth = offsetFt.Width + ColumnPadding * 2;
        }
        else
        {
            _offsetColumnWidth = 0;
        }

        // Measure hex column width using actual sample string
        {
            int groupSize = Math.Max(1, ColumnGroupSize);
            var sb = new System.Text.StringBuilder(bytesPerRow * 4);
            for (int col = 0; col < bytesPerRow; col++)
            {
                sb.Append("00");
                if (col < bytesPerRow - 1)
                {
                    sb.Append(' ');
                    if ((col + 1) % groupSize == 0)
                        sb.Append(' ');
                }
            }
            var hexFt = new FormattedText(sb.ToString(), fontFamily, fontSize) { Foreground = s_defaultForeground };
            TextMeasurement.MeasureText(hexFt);
            _hexColumnWidth = hexFt.Width + ColumnPadding * 2;

            var asciiFt = new FormattedText(new string('.', bytesPerRow), fontFamily, fontSize) { Foreground = s_defaultForeground };
            TextMeasurement.MeasureText(asciiFt);
            _asciiColumnWidth = ShowAsciiColumn ? asciiFt.Width + ColumnPadding * 2 : 0;
        }

        var totalWidth = _offsetColumnWidth + _hexColumnWidth + _asciiColumnWidth;
        if (ShowOffsetColumn) totalWidth += ColumnSeparatorWidth;
        if (ShowAsciiColumn) totalWidth += ColumnSeparatorWidth;
        if (ShowDataInterpretation) totalWidth += ColumnSeparatorWidth + DataInterpretationPanelWidth;

        var data = Data;
        var dataLength = data?.Length ?? 0;
        var totalRows = dataLength > 0 ? ((dataLength - 1) / bytesPerRow) + 1 : 1;

        totalWidth += ScrollBarWidth; // Reserve space for scrollbar

        // Measure scrollbar
        _verticalScrollBar.Measure(new Size(ScrollBarWidth, availableSize.Height > 0 ? availableSize.Height : double.PositiveInfinity));

        var desiredWidth = Math.Min(totalWidth, availableSize.Width > 0 ? availableSize.Width : totalWidth);
        var desiredHeight = availableSize.Height > 0
            ? Math.Min(totalRows * _rowHeight, availableSize.Height)
            : totalRows * _rowHeight;

        return new Size(desiredWidth, desiredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var bytesPerRow = Math.Max(1, BytesPerRow);

        // Recompute column positions using actual text measurement
        {
            var fontFamily = GetMonospaceFont();
            var fontSize = GetFontSize();

            // Measure offset column width from actual formatted text
            if (ShowOffsetColumn)
            {
                var sampleOffset = (0L).ToString(OffsetFormat);
                var offsetFt = new FormattedText(sampleOffset + "  ", fontFamily, fontSize) { Foreground = s_defaultForeground };
                TextMeasurement.MeasureText(offsetFt);
                _offsetColumnWidth = offsetFt.Width + ColumnPadding * 2;
            }
            else
            {
                _offsetColumnWidth = 0;
            }
        }

        // Measure hex column width using an actual sample hex string
        {
            var fontFamily = GetMonospaceFont();
            var fontSize = GetFontSize();
            int groupSize = Math.Max(1, ColumnGroupSize);
            var sb = new System.Text.StringBuilder(bytesPerRow * 4);
            for (int col = 0; col < bytesPerRow; col++)
            {
                sb.Append("00");
                if (col < bytesPerRow - 1)
                {
                    sb.Append(' ');
                    if ((col + 1) % groupSize == 0)
                        sb.Append(' ');
                }
            }
            var hexFt = new FormattedText(sb.ToString(), fontFamily, fontSize) { Foreground = s_defaultForeground };
            TextMeasurement.MeasureText(hexFt);
            _hexColumnWidth = hexFt.Width + ColumnPadding * 2;

            // Measure ASCII column width using sample string
            var asciiFt = new FormattedText(new string('.', bytesPerRow), fontFamily, fontSize) { Foreground = s_defaultForeground };
            TextMeasurement.MeasureText(asciiFt);
            _asciiColumnWidth = ShowAsciiColumn ? asciiFt.Width + ColumnPadding * 2 : 0;
        }

        _hexColumnStartX = _offsetColumnWidth + (ShowOffsetColumn ? ColumnSeparatorWidth : 0);
        _asciiColumnStartX = _hexColumnStartX + _hexColumnWidth + (ShowAsciiColumn ? ColumnSeparatorWidth : 0);
        _dataInterpretationStartX = _asciiColumnStartX + _asciiColumnWidth + (ShowDataInterpretation ? ColumnSeparatorWidth : 0);

        _visibleRowCount = _rowHeight > 0 ? (int)Math.Ceiling(finalSize.Height / _rowHeight) : 0;

        // Arrange scrollbar on the right edge
        _verticalScrollBar.Arrange(new Rect(
            finalSize.Width - ScrollBarWidth, 0,
            ScrollBarWidth, finalSize.Height));

        UpdateScrollBar();

        return finalSize;
    }

    private double CalculateHexColumnWidth(int bytesPerRow)
    {
        int unitSize = GetDisplayUnitSize();
        int unitsPerRow = bytesPerRow / unitSize;
        int charsPerUnit = unitSize * 2; // 2 hex chars per byte
        int groupSize = Math.Max(1, ColumnGroupSize / unitSize);

        // Each unit takes charsPerUnit + 1 space, plus extra space between groups
        int groupCount = unitsPerRow > 0 ? ((unitsPerRow - 1) / groupSize) : 0;
        double totalChars = (unitsPerRow * (charsPerUnit + 1)) + groupCount;

        return (totalChars * _charWidth) + ColumnPadding * 2;
    }

    private int GetDisplayUnitSize()
    {
        return DisplayFormat switch
        {
            HexDisplayFormat.Word16 => 2,
            HexDisplayFormat.DWord32 => 4,
            HexDisplayFormat.QWord64 => 8,
            _ => 1
        };
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var data = Data;
        var dataLength = data?.Length ?? 0;
        var bytesPerRow = Math.Max(1, BytesPerRow);
        var fontFamily = GetMonospaceFont();
        var fontSize = GetFontSize();

        // Ensure char metrics are valid
        if (_charWidth <= 0 || _rowHeight <= 0)
        {
            var charSize = MeasureChar('0', fontFamily, fontSize);
            _charWidth = charSize.Width;
            _rowHeight = charSize.Height + 2;
        }

        // Clip to bounds
        // Clip content area (exclude scrollbar on the right)
        double contentWidth = Math.Max(0, RenderSize.Width - ScrollBarWidth);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, contentWidth, RenderSize.Height)));
        try
        {
            // Draw background
            if (Background != null)
            {
                dc.DrawRectangle(Background, null, new Rect(RenderSize));
            }

            // Draw offset gutter background
            if (ShowOffsetColumn && _offsetColumnWidth > 0)
            {
                var gutterBrush = ResolveGutterBackground();
                dc.DrawRectangle(gutterBrush, null, new Rect(0, 0, _offsetColumnWidth, RenderSize.Height));
            }

            // Calculate visible range
            long firstVisibleByte = _scrollOffset;
            int visibleRows = _visibleRowCount > 0 ? _visibleRowCount + 1 : 0; // +1 for partial row
            _rowCache.Clear();

            // Pre-pass: build all hex strings and find the maximum actual width
            // so column positions are based on real data, not estimates
            double maxHexTextWidth = 0;
            int groupSize = Math.Max(1, ColumnGroupSize);
            var hexStrings = new List<(long offset, string hexStr, string asciiStr, int[] charStarts, int bytesInRow)>();

            for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
            {
                long rowByteOffset = firstVisibleByte + (long)rowIndex * bytesPerRow;
                if (rowByteOffset >= dataLength) break;

                int bytesInRow = (int)Math.Min(bytesPerRow, dataLength - rowByteOffset);

                // Build hex string
                var sb = new System.Text.StringBuilder(bytesPerRow * 4);
                var charStarts = new int[bytesInRow];
                for (int col = 0; col < bytesInRow; col++)
                {
                    charStarts[col] = sb.Length;
                    sb.Append(data![(int)(rowByteOffset + col)].ToString("X2"));
                    if (col < bytesInRow - 1)
                    {
                        sb.Append(' ');
                        if ((col + 1) % groupSize == 0)
                            sb.Append(' ');
                    }
                }
                var hexStr = sb.ToString();

                // Measure actual width
                var ft = new FormattedText(hexStr, fontFamily, fontSize) { Foreground = s_defaultForeground };
                TextMeasurement.MeasureText(ft);
                if (ft.Width > maxHexTextWidth)
                    maxHexTextWidth = ft.Width;

                // Build ascii string
                var asb = new System.Text.StringBuilder(bytesInRow);
                for (int col = 0; col < bytesInRow; col++)
                {
                    byte b = data![(int)(rowByteOffset + col)];
                    asb.Append((b >= 0x20 && b <= 0x7E) ? (char)b : '.');
                }

                hexStrings.Add((rowByteOffset, hexStr, asb.ToString(), charStarts, bytesInRow));
            }

            // Correct column widths using actual measured hex width
            _hexColumnWidth = maxHexTextWidth + ColumnPadding * 2;
            _asciiColumnStartX = _hexColumnStartX + _hexColumnWidth + (ShowAsciiColumn ? ColumnSeparatorWidth : 0);

            // Render pass: draw all content with corrected positions
            var hexBrush = ResolveHexForeground();
            var asciiBrush = ResolveAsciiForeground();

            for (int rowIndex = 0; rowIndex < hexStrings.Count; rowIndex++)
            {
                var (rowByteOffset, hexStr, asciiStr, charStarts, bytesInRow2) = hexStrings[rowIndex];
                double y = rowIndex * _rowHeight;

                // Cache
                var cache = new RowStringCache
                {
                    HexString = hexStr,
                    AsciiString = asciiStr,
                    ByteToHexCharStart = charStarts,
                    FontFamily = fontFamily,
                    FontSize = fontSize
                };
                _rowCache[rowByteOffset] = cache;

                // Draw offset
                if (ShowOffsetColumn)
                    DrawOffsetCell(dc, rowByteOffset, y, fontFamily, fontSize);

                // Draw hex (clipped to column)
                var hexFt = new FormattedText(hexStr, fontFamily, fontSize) { Foreground = hexBrush };
                TextMeasurement.MeasureText(hexFt);
                dc.PushClip(new RectangleGeometry(new Rect(_hexColumnStartX, y, _hexColumnWidth, _rowHeight)));
                dc.DrawText(hexFt, new Point(_hexColumnStartX + ColumnPadding, y));
                dc.Pop();

                // Draw ASCII
                if (ShowAsciiColumn)
                {
                    var asciiFt = new FormattedText(asciiStr, fontFamily, fontSize) { Foreground = asciiBrush };
                    TextMeasurement.MeasureText(asciiFt);
                    dc.DrawText(asciiFt, new Point(_asciiColumnStartX + ColumnPadding, y));
                }
            }

            // Draw column separators (with corrected positions)
            DrawColumnSeparators(dc);

            // Pass 2: Draw selection backgrounds (uses cached strings for pixel-accurate positions)
            for (int rowIndex = 0; rowIndex < visibleRows; rowIndex++)
            {
                long rowByteOffset = firstVisibleByte + (long)rowIndex * bytesPerRow;
                if (rowByteOffset >= dataLength) break;

                double y = rowIndex * _rowHeight;
                int bytesInRow = (int)Math.Min(bytesPerRow, dataLength - rowByteOffset);

                DrawRowSelectionBackground(dc, rowByteOffset, bytesInRow, bytesPerRow, y);
            }

            // Draw caret
            if (IsKeyboardFocused && dataLength > 0)
            {
                UpdateCaretBlink();
                if (_caretVisible)
                {
                    DrawCaret(dc);
                }
            }

            // Draw data interpretation panel
            if (ShowDataInterpretation && data != null && dataLength > 0)
            {
                DrawDataInterpretation(dc, data, fontFamily, fontSize);
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private void DrawColumnSeparators(DrawingContext dc)
    {
        var separatorBrush = ResolveColumnSeparatorBrush();
        var pen = new Pen(separatorBrush, ColumnSeparatorWidth);

        if (ShowOffsetColumn && _offsetColumnWidth > 0)
        {
            double x = _offsetColumnWidth;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        }

        if (ShowAsciiColumn && _asciiColumnStartX > 0)
        {
            double x = _asciiColumnStartX - ColumnSeparatorWidth;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        }

        if (ShowDataInterpretation && _dataInterpretationStartX > 0)
        {
            double x = _dataInterpretationStartX - ColumnSeparatorWidth;
            dc.DrawLine(pen, new Point(x, 0), new Point(x, RenderSize.Height));
        }
    }

    private void DrawRowSelectionBackground(DrawingContext dc, long rowByteOffset, int bytesInRow, int bytesPerRow, double y)
    {
        var selStart = SelectionStart;
        var selLength = SelectionLength;
        if (selStart < 0 || selLength <= 0) return;

        long selEnd = selStart + selLength;
        long rowEnd = rowByteOffset + bytesInRow;
        if (rowByteOffset >= selEnd || rowEnd <= selStart) return;

        long overlapStart = Math.Max(rowByteOffset, selStart);
        long overlapEnd = Math.Min(rowEnd, selEnd);
        int startCol = (int)(overlapStart - rowByteOffset);
        int endCol = (int)(overlapEnd - rowByteOffset);

        var selBrush = ResolveSelectionBrush();

        if (!_rowCache.TryGetValue(rowByteOffset, out var cache))
            return;

        // Hex column selection — use cached string for pixel-accurate measurement
        if (!string.IsNullOrEmpty(cache.HexString) && cache.ByteToHexCharStart.Length > 0)
        {
            int hexCharStart = startCol < cache.ByteToHexCharStart.Length ? cache.ByteToHexCharStart[startCol] : 0;
            int hexCharEnd = (endCol - 1) < cache.ByteToHexCharStart.Length ? cache.ByteToHexCharStart[endCol - 1] + 2 : cache.HexString.Length;

            // Measure the prefix up to the start/end characters using the ACTUAL hex string
            double hexStartPx = MeasureSubstring(cache.HexString, 0, hexCharStart, cache.FontFamily, cache.FontSize);
            double hexEndPx = MeasureSubstring(cache.HexString, 0, hexCharEnd, cache.FontFamily, cache.FontSize);

            dc.DrawRectangle(selBrush, null, new Rect(
                _hexColumnStartX + ColumnPadding + hexStartPx, y,
                hexEndPx - hexStartPx, _rowHeight));
        }

        // ASCII column selection
        if (ShowAsciiColumn && !string.IsNullOrEmpty(cache.AsciiString))
        {
            double asciiStartPx = MeasureSubstring(cache.AsciiString, 0, startCol, cache.FontFamily, cache.FontSize);
            double asciiEndPx = MeasureSubstring(cache.AsciiString, 0, endCol, cache.FontFamily, cache.FontSize);

            dc.DrawRectangle(selBrush, null, new Rect(
                _asciiColumnStartX + ColumnPadding + asciiStartPx, y,
                asciiEndPx - asciiStartPx, _rowHeight));
        }
    }

    private double MeasureSubstring(string fullString, int start, int length, string fontFamily, double fontSize)
    {
        if (length <= 0 || start >= fullString.Length) return 0;
        length = Math.Min(length, fullString.Length - start);
        var sub = fullString.Substring(start, length);
        var ft = new FormattedText(sub, fontFamily, fontSize) { Foreground = s_defaultForeground };
        TextMeasurement.MeasureText(ft);
        return ft.Width;
    }

    /// <summary>
    /// Gets the pixel X position of a byte column within the hex string,
    /// by measuring the actual rendered prefix width. This avoids cumulative
    /// charWidth rounding errors.
    /// </summary>
    private double GetHexBytePixelX(int byteCol, int groupSize, string fontFamily, double fontSize)
    {
        if (byteCol <= 0) return 0;

        // Build the prefix string up to byteCol (same logic as DrawHexRow)
        var sb = new System.Text.StringBuilder(byteCol * 4);
        for (int col = 0; col < byteCol; col++)
        {
            sb.Append("00"); // placeholder hex digits
            sb.Append(' ');
            if ((col + 1) % groupSize == 0)
                sb.Append(' ');
        }

        var ft = new FormattedText(sb.ToString(), fontFamily, fontSize)
        {
            Foreground = s_defaultForeground
        };
        TextMeasurement.MeasureText(ft);
        return ft.Width;
    }

    /// <summary>
    /// Gets the pixel width of one hex byte ("XX") in the current font.
    /// </summary>
    private double GetHexBytePixelWidth(string fontFamily, double fontSize)
    {
        var ft = new FormattedText("00", fontFamily, fontSize) { Foreground = s_defaultForeground };
        TextMeasurement.MeasureText(ft);
        return ft.Width;
    }

    private void DrawOffsetCell(DrawingContext dc, long offset, double y, string fontFamily, double fontSize)
    {
        var text = offset.ToString(OffsetFormat);
        var brush = ResolveOffsetForeground();
        var ft = new FormattedText(text, fontFamily, fontSize) { Foreground = brush };
        TextMeasurement.MeasureText(ft);
        dc.DrawText(ft, new Point(ColumnPadding, y));
    }

    /// <summary>
    /// Measures the pixel width of N ASCII characters by rendering a sample string.
    /// </summary>
    private double MeasureAsciiPrefixWidth(int charCount, string fontFamily, double fontSize)
    {
        if (charCount <= 0) return 0;
        // Use dots as placeholder — all printable ASCII chars in monospace should be same width,
        // but measure to be safe
        var ft = new FormattedText(new string('.', charCount), fontFamily, fontSize) { Foreground = s_defaultForeground };
        TextMeasurement.MeasureText(ft);
        return ft.Width;
    }

    private void DrawHexRow(DrawingContext dc, byte[] data, long rowByteOffset, int bytesInRow, int bytesPerRow, double y, string fontFamily, double fontSize)
    {
        var hexBrush = ResolveHexForeground();
        int groupSize = Math.Max(1, ColumnGroupSize);

        // Build hex string and track byte→char index mapping
        var sb = new System.Text.StringBuilder(bytesPerRow * 3 + 4);
        var byteCharStarts = new int[bytesInRow];

        for (int col = 0; col < bytesInRow; col++)
        {
            byteCharStarts[col] = sb.Length;
            int byteIndex = (int)(rowByteOffset + col);
            sb.Append(data[byteIndex].ToString("X2"));

            if (col < bytesInRow - 1)
            {
                sb.Append(' ');
                if ((col + 1) % groupSize == 0)
                    sb.Append(' ');
            }
        }

        var hexStr = sb.ToString();

        // Cache for selection/caret/hittest
        if (!_rowCache.TryGetValue(rowByteOffset, out var cache))
        {
            cache = new RowStringCache();
            _rowCache[rowByteOffset] = cache;
        }
        cache.HexString = hexStr;
        cache.ByteToHexCharStart = byteCharStarts;
        cache.FontFamily = fontFamily;
        cache.FontSize = fontSize;

        var ft = new FormattedText(hexStr, fontFamily, fontSize) { Foreground = hexBrush };
        TextMeasurement.MeasureText(ft);
        dc.DrawText(ft, new Point(_hexColumnStartX + ColumnPadding, y));
    }

    private void DrawAsciiRow(DrawingContext dc, byte[] data, long rowByteOffset, int bytesInRow, double y, string fontFamily, double fontSize)
    {
        var asciiBrush = ResolveAsciiForeground();

        var sb = new System.Text.StringBuilder(bytesInRow);
        for (int col = 0; col < bytesInRow; col++)
        {
            int byteIndex = (int)(rowByteOffset + col);
            byte b = data[byteIndex];
            sb.Append((b >= 0x20 && b <= 0x7E) ? (char)b : '.');
        }

        var asciiStr = sb.ToString();

        // Cache for selection/hittest
        if (_rowCache.TryGetValue(rowByteOffset, out var cache))
        {
            cache.AsciiString = asciiStr;
        }

        var ft = new FormattedText(sb.ToString(), fontFamily, fontSize) { Foreground = asciiBrush };
        TextMeasurement.MeasureText(ft);
        dc.DrawText(ft, new Point(_asciiColumnStartX + ColumnPadding, y));
    }

    private void DrawCaret(DrawingContext dc)
    {
        var caretOffset = CaretOffset;
        var data = Data;
        if (data == null || data.Length == 0) return;
        if (caretOffset < 0 || caretOffset >= data.Length) return;

        var bytesPerRow = Math.Max(1, BytesPerRow);
        long caretRow = caretOffset / bytesPerRow;
        long firstVisibleRow = _scrollOffset / bytesPerRow;
        int rowIndex = (int)(caretRow - firstVisibleRow);

        if (rowIndex < 0 || rowIndex > _visibleRowCount) return;

        double y = rowIndex * _rowHeight;
        int col = (int)(caretOffset % bytesPerRow);

        long rowByteOffset = caretRow * bytesPerRow;
        _rowCache.TryGetValue(rowByteOffset, out var cache);

        if (_isCaretInAsciiPane && ShowAsciiColumn)
        {
            double x = _asciiColumnStartX + ColumnPadding;
            if (cache != null && !string.IsNullOrEmpty(cache.AsciiString))
                x += MeasureSubstring(cache.AsciiString, 0, col, cache.FontFamily, cache.FontSize);
            dc.DrawRectangle(s_caretBrush, null, new Rect(x, y, 2, _rowHeight));
        }
        else
        {
            double x = _hexColumnStartX + ColumnPadding;
            if (cache != null && cache.ByteToHexCharStart.Length > col)
            {
                int charIdx = cache.ByteToHexCharStart[col];
                x += MeasureSubstring(cache.HexString, 0, charIdx, cache.FontFamily, cache.FontSize);
                if (!_isHighNibble)
                {
                    // Measure one more hex char for low nibble
                    x += MeasureSubstring(cache.HexString, charIdx, 1, cache.FontFamily, cache.FontSize);
                }
            }
            dc.DrawRectangle(s_caretBrush, null, new Rect(x, y, 2, _rowHeight));
        }
    }

    private void DrawDataInterpretation(DrawingContext dc, byte[] data, string fontFamily, double fontSize)
    {
        var offset = CaretOffset;
        if (offset < 0 || offset >= data.Length) return;

        double x = _dataInterpretationStartX + ColumnPadding;
        double y = 4;
        var brush = ResolveHexForeground();
        bool isLittle = Endianness == Endianness.Little;

        // Int8
        DrawInterpretationLine(dc, $"Int8:   {(sbyte)data[offset]}", x, ref y, fontFamily, fontSize, brush);
        DrawInterpretationLine(dc, $"UInt8:  {data[offset]}", x, ref y, fontFamily, fontSize, brush);

        // Int16
        if (offset + 1 < data.Length)
        {
            short val16 = isLittle
                ? (short)(data[offset] | (data[offset + 1] << 8))
                : (short)((data[offset] << 8) | data[offset + 1]);
            DrawInterpretationLine(dc, $"Int16:  {val16}", x, ref y, fontFamily, fontSize, brush);
            DrawInterpretationLine(dc, $"UInt16: {(ushort)val16}", x, ref y, fontFamily, fontSize, brush);
        }

        // Int32
        if (offset + 3 < data.Length)
        {
            int val32;
            if (isLittle)
            {
                val32 = data[offset]
                    | (data[offset + 1] << 8)
                    | (data[offset + 2] << 16)
                    | (data[offset + 3] << 24);
            }
            else
            {
                val32 = (data[offset] << 24)
                    | (data[offset + 1] << 16)
                    | (data[offset + 2] << 8)
                    | data[offset + 3];
            }
            DrawInterpretationLine(dc, $"Int32:  {val32}", x, ref y, fontFamily, fontSize, brush);
            DrawInterpretationLine(dc, $"UInt32: {(uint)val32}", x, ref y, fontFamily, fontSize, brush);

            // Float
            float floatVal = BitConverter.Int32BitsToSingle(val32);
            DrawInterpretationLine(dc, $"Float:  {floatVal:G7}", x, ref y, fontFamily, fontSize, brush);
        }

        // Int64
        if (offset + 7 < data.Length)
        {
            long val64;
            if (isLittle)
            {
                val64 = (long)data[offset]
                    | ((long)data[offset + 1] << 8)
                    | ((long)data[offset + 2] << 16)
                    | ((long)data[offset + 3] << 24)
                    | ((long)data[offset + 4] << 32)
                    | ((long)data[offset + 5] << 40)
                    | ((long)data[offset + 6] << 48)
                    | ((long)data[offset + 7] << 56);
            }
            else
            {
                val64 = ((long)data[offset] << 56)
                    | ((long)data[offset + 1] << 48)
                    | ((long)data[offset + 2] << 40)
                    | ((long)data[offset + 3] << 32)
                    | ((long)data[offset + 4] << 24)
                    | ((long)data[offset + 5] << 16)
                    | ((long)data[offset + 6] << 8)
                    | (long)data[offset + 7];
            }
            DrawInterpretationLine(dc, $"Int64:  {val64}", x, ref y, fontFamily, fontSize, brush);
            DrawInterpretationLine(dc, $"UInt64: {(ulong)val64}", x, ref y, fontFamily, fontSize, brush);

            // Double
            double doubleVal = BitConverter.Int64BitsToDouble(val64);
            DrawInterpretationLine(dc, $"Double: {doubleVal:G15}", x, ref y, fontFamily, fontSize, brush);
        }
    }

    private void DrawInterpretationLine(DrawingContext dc, string text, double x, ref double y,
        string fontFamily, double fontSize, Brush brush)
    {
        var ft = new FormattedText(text, fontFamily, fontSize) { Foreground = brush };
        TextMeasurement.MeasureText(ft);
        dc.DrawText(ft, new Point(x, y));
        y += _rowHeight;
    }

    #endregion

    #region Hex Formatting

    private string FormatHexUnit(byte[] data, int offset, int unitSize)
    {
        if (unitSize == 1)
        {
            return data[offset].ToString("X2");
        }

        bool isLittle = Endianness == Endianness.Little;
        ulong value = 0;

        if (isLittle)
        {
            for (int i = unitSize - 1; i >= 0; i--)
            {
                value = (value << 8) | data[offset + i];
            }
        }
        else
        {
            for (int i = 0; i < unitSize; i++)
            {
                value = (value << 8) | data[offset + i];
            }
        }

        return unitSize switch
        {
            2 => value.ToString("X4"),
            4 => value.ToString("X8"),
            8 => value.ToString("X16"),
            _ => value.ToString("X2")
        };
    }

    private double GetHexCellX(int byteColumn)
    {
        int unitSize = GetDisplayUnitSize();
        int unitIndex = byteColumn / unitSize;
        int groupSize = Math.Max(1, ColumnGroupSize / unitSize);
        int charsPerUnit = unitSize * 2;

        // Each unit occupies (charsPerUnit + 1) chars, plus 1 extra char per group boundary
        int groupBoundaries = unitIndex > 0 ? ((unitIndex - 1) / groupSize) : 0;
        // For byte-level positioning within a unit for the caret
        int byteWithinUnit = byteColumn % unitSize;

        double unitX = unitIndex * (charsPerUnit + 1) * _charWidth + groupBoundaries * _charWidth;

        // If positioning within a multi-byte unit, offset by the byte position
        if (unitSize > 1 && byteWithinUnit > 0)
        {
            unitX += byteWithinUnit * 2 * _charWidth;
        }

        return unitX;
    }

    private double GetHexCellWidth()
    {
        int unitSize = GetDisplayUnitSize();
        return unitSize * 2 * _charWidth;
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
            var hitResult = HitTest(position);

            if (hitResult.ByteOffset >= 0)
            {
                _isDragging = true;
                _dragStartOffset = hitResult.ByteOffset;
                _isCaretInAsciiPane = hitResult.IsAsciiPane;

                if (e.KeyboardModifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Extend selection
                    ExtendSelection(hitResult.ByteOffset);
                }
                else
                {
                    CaretOffset = hitResult.ByteOffset;
                    SelectionStart = -1;
                    SelectionLength = 0;
                }
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var position = e.GetPosition(this);
        var hitResult = HitTest(position);

        if (hitResult.ByteOffset >= 0)
        {
            CaretOffset = hitResult.ByteOffset;
            UpdateDragSelection(hitResult.ByteOffset);
        }
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        var data = Data;
        if (data == null || data.Length == 0) return;

        var bytesPerRow = Math.Max(1, BytesPerRow);
        int scrollRows = e.Delta > 0 ? -3 : 3;
        long newOffset = _scrollOffset + scrollRows * bytesPerRow;

        long maxOffset = Math.Max(0, ((long)data.Length - 1) / bytesPerRow * bytesPerRow - (_visibleRowCount - 2) * bytesPerRow);
        _scrollOffset = Math.Clamp(newOffset, 0, maxOffset);

        // Align to row boundary
        _scrollOffset = (_scrollOffset / bytesPerRow) * bytesPerRow;

        UpdateScrollBar();
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        var data = Data;
        if (data == null || data.Length == 0) return;

        var bytesPerRow = Math.Max(1, BytesPerRow);
        var caretOffset = CaretOffset;

        switch (e.Key)
        {
            case Key.Left:
                if (e.IsShiftDown)
                    ExtendSelection(Math.Max(0, caretOffset - 1));
                else
                    ClearSelection();
                CaretOffset = Math.Max(0, caretOffset - 1);
                e.Handled = true;
                break;

            case Key.Right:
                if (e.IsShiftDown)
                    ExtendSelection(Math.Min(data.Length - 1, caretOffset + 1));
                else
                    ClearSelection();
                CaretOffset = Math.Min(data.Length - 1, caretOffset + 1);
                e.Handled = true;
                break;

            case Key.Up:
                if (e.IsShiftDown)
                    ExtendSelection(Math.Max(0, caretOffset - bytesPerRow));
                else
                    ClearSelection();
                CaretOffset = Math.Max(0, caretOffset - bytesPerRow);
                e.Handled = true;
                break;

            case Key.Down:
                if (e.IsShiftDown)
                    ExtendSelection(Math.Min(data.Length - 1, caretOffset + bytesPerRow));
                else
                    ClearSelection();
                CaretOffset = Math.Min(data.Length - 1, caretOffset + bytesPerRow);
                e.Handled = true;
                break;

            case Key.Home:
                if (e.IsControlDown)
                {
                    if (e.IsShiftDown) ExtendSelection(0);
                    else ClearSelection();
                    CaretOffset = 0;
                }
                else
                {
                    long rowStart = (caretOffset / bytesPerRow) * bytesPerRow;
                    if (e.IsShiftDown) ExtendSelection(rowStart);
                    else ClearSelection();
                    CaretOffset = rowStart;
                }
                e.Handled = true;
                break;

            case Key.End:
                if (e.IsControlDown)
                {
                    long lastByte = data.Length - 1;
                    if (e.IsShiftDown) ExtendSelection(lastByte);
                    else ClearSelection();
                    CaretOffset = lastByte;
                }
                else
                {
                    long rowEnd = Math.Min(data.Length - 1, ((caretOffset / bytesPerRow) + 1) * bytesPerRow - 1);
                    if (e.IsShiftDown) ExtendSelection(rowEnd);
                    else ClearSelection();
                    CaretOffset = rowEnd;
                }
                e.Handled = true;
                break;

            case Key.PageUp:
            {
                long newOffset = Math.Max(0, caretOffset - (long)_visibleRowCount * bytesPerRow);
                if (e.IsShiftDown) ExtendSelection(newOffset);
                else ClearSelection();
                CaretOffset = newOffset;
                e.Handled = true;
                break;
            }

            case Key.PageDown:
            {
                long newOffset = Math.Min(data.Length - 1, caretOffset + (long)_visibleRowCount * bytesPerRow);
                if (e.IsShiftDown) ExtendSelection(newOffset);
                else ClearSelection();
                CaretOffset = newOffset;
                e.Handled = true;
                break;
            }

            case Key.Tab:
                // Toggle between hex and ASCII panes
                if (ShowAsciiColumn)
                {
                    _isCaretInAsciiPane = !_isCaretInAsciiPane;
                    _isHighNibble = true;
                    InvalidateVisual();
                }
                e.Handled = true;
                break;

            case Key.G:
                if (e.IsControlDown)
                {
                    // Goto offset - for now, this is a hook point; the host can handle CaretMoved
                    // or override behavior via a command binding.
                    e.Handled = true;
                }
                break;

            case Key.A:
                if (e.IsControlDown)
                {
                    // Select all
                    SelectionStart = 0;
                    SelectionLength = data.Length;
                    RaiseSelectionChangedEvent(-1, 0, 0, data.Length);
                    InvalidateVisual();
                    e.Handled = true;
                }
                break;

            case Key.C:
                if (e.IsControlDown)
                {
                    CopySelection();
                    e.Handled = true;
                }
                break;

            case Key.V:
                if (e.IsControlDown && !IsReadOnly)
                {
                    PasteFromClipboard();
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        var data = Data;
        if (data == null || data.Length == 0) return;

        var caretOffset = CaretOffset;
        if (caretOffset < 0 || caretOffset >= data.Length) return;

        if (_isCaretInAsciiPane)
        {
            // ASCII input: replace the byte at caret with the typed character
            char ch = e.Text[0];
            if (ch >= 0x20 && ch <= 0x7E)
            {
                byte oldValue = data[caretOffset];
                byte newValue = (byte)ch;
                data[caretOffset] = newValue;
                _modifiedBytes.Add(caretOffset);
                RaiseByteModifiedEvent(caretOffset, oldValue, newValue);

                CaretOffset = Math.Min(data.Length - 1, caretOffset + 1);
                InvalidateVisual();
            }
        }
        else
        {
            // Hex input: accept hex digits
            char ch = char.ToUpperInvariant(e.Text[0]);
            if (IsHexDigit(ch))
            {
                int nibbleValue = HexCharToValue(ch);
                byte oldValue = data[caretOffset];
                byte newValue;

                if (_isHighNibble)
                {
                    newValue = (byte)((nibbleValue << 4) | (oldValue & 0x0F));
                    data[caretOffset] = newValue;
                    _modifiedBytes.Add(caretOffset);
                    _isHighNibble = false;
                    _caretVisible = true;
                    _lastCaretBlinkTicks = Environment.TickCount64;
                    InvalidateVisual();
                }
                else
                {
                    newValue = (byte)((oldValue & 0xF0) | nibbleValue);
                    data[caretOffset] = newValue;
                    _modifiedBytes.Add(caretOffset);
                    RaiseByteModifiedEvent(caretOffset, oldValue, newValue);
                    _isHighNibble = true;

                    // Advance caret to next byte
                    CaretOffset = Math.Min(data.Length - 1, caretOffset + 1);
                    InvalidateVisual();
                }

                if (oldValue != newValue)
                {
                    RaiseByteModifiedEvent(caretOffset, oldValue, newValue);
                }
            }
        }

        e.Handled = true;
    }

    private void OnGotFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        _caretVisible = true;
        _lastCaretBlinkTicks = Environment.TickCount64;
        InvalidateVisual();
    }

    private void OnLostFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        _caretVisible = false;
        InvalidateVisual();
    }

    #endregion

    #region Hit Testing

    private readonly struct HitTestResult
    {
        public long ByteOffset { get; init; }
        public bool IsAsciiPane { get; init; }
    }

    private new HitTestResult HitTest(Point position)
    {
        var data = Data;
        if (data == null || data.Length == 0)
            return new HitTestResult { ByteOffset = -1, IsAsciiPane = false };

        var bytesPerRow = Math.Max(1, BytesPerRow);
        int row = (int)(position.Y / _rowHeight);
        long rowByteOffset = _scrollOffset + (long)row * bytesPerRow;

        if (rowByteOffset >= data.Length)
            return new HitTestResult { ByteOffset = -1, IsAsciiPane = false };

        _rowCache.TryGetValue(rowByteOffset, out var cache);

        // Check ASCII pane
        if (ShowAsciiColumn && position.X >= _asciiColumnStartX && position.X < _asciiColumnStartX + _asciiColumnWidth)
        {
            double relX = position.X - _asciiColumnStartX - ColumnPadding;
            int col = 0;
            if (cache != null && !string.IsNullOrEmpty(cache.AsciiString))
            {
                for (int c = 1; c <= Math.Min(bytesPerRow, cache.AsciiString.Length); c++)
                {
                    if (MeasureSubstring(cache.AsciiString, 0, c, cache.FontFamily, cache.FontSize) > relX)
                        break;
                    col = c;
                }
            }
            col = Math.Clamp(col, 0, bytesPerRow - 1);
            long offset = Math.Min(data.Length - 1, rowByteOffset + col);
            return new HitTestResult { ByteOffset = offset, IsAsciiPane = true };
        }

        // Check hex pane
        if (position.X >= _hexColumnStartX && position.X < _hexColumnStartX + _hexColumnWidth)
        {
            double relativeX = position.X - _hexColumnStartX - ColumnPadding;
            int col = 0;
            if (cache != null && cache.ByteToHexCharStart.Length > 0)
            {
                double byteW = cache.ByteToHexCharStart.Length > 0
                    ? MeasureSubstring(cache.HexString, cache.ByteToHexCharStart[0], 2, cache.FontFamily, cache.FontSize)
                    : _charWidth * 2;
                double bestDist = double.MaxValue;
                for (int c = 0; c < Math.Min(bytesPerRow, cache.ByteToHexCharStart.Length); c++)
                {
                    double cellX = MeasureSubstring(cache.HexString, 0, cache.ByteToHexCharStart[c], cache.FontFamily, cache.FontSize);
                    double cellMid = cellX + byteW / 2;
                    double dist = Math.Abs(relativeX - cellMid);
                    if (dist < bestDist) { bestDist = dist; col = c; }
                }
            }
            col = Math.Clamp(col, 0, bytesPerRow - 1);
            long offset = Math.Min(data.Length - 1, rowByteOffset + col);
            return new HitTestResult { ByteOffset = offset, IsAsciiPane = false };
        }

        return new HitTestResult { ByteOffset = -1, IsAsciiPane = false };
    }

    private int HitTestHexColumn(double relativeX, int bytesPerRow)
    {
        // Find the byte column using actual pixel measurement
        int groupSize = Math.Max(1, ColumnGroupSize);
        var fontFamily = GetMonospaceFont();
        var fontSize = GetFontSize();
        double byteWidth = GetHexBytePixelWidth(fontFamily, fontSize);
        int bestCol = 0;
        double bestDist = double.MaxValue;

        for (int col = 0; col < bytesPerRow; col++)
        {
            double cellX = GetHexBytePixelX(col, groupSize, fontFamily, fontSize);
            double cellMid = cellX + byteWidth / 2;
            double dist = Math.Abs(relativeX - cellMid);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestCol = col;
            }
        }

        return bestCol;
    }

    #endregion

    #region Selection

    private void ExtendSelection(long toOffset)
    {
        var data = Data;
        if (data == null || data.Length == 0) return;

        var currentStart = SelectionStart;
        var currentLength = SelectionLength;
        long anchorOffset;

        if (currentStart < 0 || currentLength == 0)
        {
            anchorOffset = CaretOffset;
        }
        else
        {
            // Determine anchor: the end of the selection that is NOT the caret
            long selEnd = currentStart + currentLength;
            anchorOffset = (CaretOffset == currentStart) ? selEnd - 1 : currentStart;
        }

        long newStart = Math.Min(anchorOffset, toOffset);
        long newEnd = Math.Max(anchorOffset, toOffset);
        long newLength = newEnd - newStart + 1;

        var oldStart = SelectionStart;
        var oldLength = SelectionLength;

        SelectionStart = newStart;
        SelectionLength = newLength;

        RaiseSelectionChangedEvent(oldStart, oldLength, newStart, newLength);
    }

    private void UpdateDragSelection(long currentOffset)
    {
        long start = Math.Min(_dragStartOffset, currentOffset);
        long end = Math.Max(_dragStartOffset, currentOffset);
        long length = end - start + 1;

        var oldStart = SelectionStart;
        var oldLength = SelectionLength;

        SelectionStart = start;
        SelectionLength = length;

        if (oldStart != start || oldLength != length)
        {
            RaiseSelectionChangedEvent(oldStart, oldLength, start, length);
        }
    }

    private void ClearSelection()
    {
        if (SelectionStart >= 0 || SelectionLength > 0)
        {
            var oldStart = SelectionStart;
            var oldLength = SelectionLength;
            SelectionStart = -1;
            SelectionLength = 0;
            RaiseSelectionChangedEvent(oldStart, oldLength, -1, 0);
        }
    }

    #endregion

    #region Clipboard

    private void CopySelection()
    {
        var data = Data;
        if (data == null) return;

        var selStart = SelectionStart;
        var selLength = SelectionLength;

        if (selStart < 0 || selLength <= 0)
        {
            // Copy single byte at caret
            var offset = CaretOffset;
            if (offset >= 0 && offset < data.Length)
            {
                Clipboard.SetText(data[offset].ToString("X2"));
            }
            return;
        }

        long end = Math.Min(selStart + selLength, data.Length);
        var sb = new System.Text.StringBuilder((int)(end - selStart) * 3);
        for (long i = selStart; i < end; i++)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2"));
        }

        Clipboard.SetText(sb.ToString());
    }

    private void PasteFromClipboard()
    {
        var data = Data;
        if (data == null || IsReadOnly) return;

        var text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        // Parse hex bytes from clipboard text
        var bytes = ParseHexString(text);
        if (bytes.Length == 0) return;

        var offset = CaretOffset;
        if (offset < 0 || offset >= data.Length) return;

        int count = (int)Math.Min(bytes.Length, data.Length - offset);
        for (int i = 0; i < count; i++)
        {
            byte oldValue = data[offset + i];
            data[offset + i] = bytes[i];
            _modifiedBytes.Add(offset + i);
            if (oldValue != bytes[i])
            {
                RaiseByteModifiedEvent(offset + i, oldValue, bytes[i]);
            }
        }

        CaretOffset = Math.Min(data.Length - 1, offset + count);
        InvalidateVisual();
    }

    private static byte[] ParseHexString(string text)
    {
        // Support "AA BB CC" or "AABBCC" or "AA-BB-CC" formats
        var cleaned = text.Replace(" ", "").Replace("-", "").Replace(",", "").Trim();
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];

        // Validate all characters are hex
        foreach (char c in cleaned)
        {
            if (!IsHexDigit(char.ToUpperInvariant(c)))
                return Array.Empty<byte>();
        }

        if (cleaned.Length % 2 != 0)
            cleaned = "0" + cleaned; // Pad with leading zero

        var result = new byte[cleaned.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (byte)((HexCharToValue(cleaned[i * 2]) << 4) | HexCharToValue(cleaned[i * 2 + 1]));
        }
        return result;
    }

    #endregion

    #region Scrolling

    private void EnsureCaretVisible()
    {
        var data = Data;
        if (data == null || data.Length == 0) return;

        var bytesPerRow = Math.Max(1, BytesPerRow);
        var caretOffset = CaretOffset;
        long caretRow = caretOffset / bytesPerRow;
        long firstVisibleRow = _scrollOffset / bytesPerRow;
        long lastVisibleRow = firstVisibleRow + _visibleRowCount - 1;

        if (caretRow < firstVisibleRow)
        {
            _scrollOffset = caretRow * bytesPerRow;
        }
        else if (caretRow > lastVisibleRow)
        {
            _scrollOffset = (caretRow - _visibleRowCount + 1) * bytesPerRow;
        }

        // Clamp scroll offset
        long maxOffset = Math.Max(0, ((long)data.Length - 1) / bytesPerRow * bytesPerRow - (_visibleRowCount - 2) * bytesPerRow);
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maxOffset);
        _scrollOffset = (_scrollOffset / bytesPerRow) * bytesPerRow;
        UpdateScrollBar();
    }

    /// <summary>
    /// Scrolls the view so the specified byte offset is visible.
    /// </summary>
    /// <param name="offset">The byte offset to scroll to.</param>
    public void ScrollToOffset(long offset)
    {
        var data = Data;
        if (data == null || data.Length == 0) return;

        offset = Math.Clamp(offset, 0, data.Length - 1);
        CaretOffset = offset;
        // EnsureCaretVisible is called by OnCaretOffsetChanged
        InvalidateVisual();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Searches for a byte pattern in the data starting from the current caret position.
    /// Returns the offset of the first match, or -1 if not found.
    /// </summary>
    /// <param name="pattern">The byte pattern to search for.</param>
    /// <returns>The offset of the first match, or -1 if not found.</returns>
    public long FindBytes(byte[] pattern)
    {
        var data = Data;
        if (data == null || pattern == null || pattern.Length == 0 || data.Length == 0)
            return -1;

        long startOffset = CaretOffset + 1;
        if (startOffset >= data.Length) startOffset = 0;

        // Search from caret to end
        long result = SearchPattern(data, pattern, startOffset, data.Length);
        if (result >= 0) return result;

        // Wrap around: search from beginning to caret
        result = SearchPattern(data, pattern, 0, Math.Min(startOffset + pattern.Length - 1, data.Length));
        return result;
    }

    /// <summary>
    /// Replaces bytes at the specified offset with the given byte array.
    /// </summary>
    /// <param name="offset">The starting offset for the replacement.</param>
    /// <param name="replacement">The bytes to write.</param>
    public void ReplaceBytes(long offset, byte[] replacement)
    {
        var data = Data;
        if (data == null || replacement == null || IsReadOnly) return;
        if (offset < 0 || offset >= data.Length) return;

        int count = (int)Math.Min(replacement.Length, data.Length - offset);
        for (int i = 0; i < count; i++)
        {
            byte oldValue = data[offset + i];
            if (oldValue != replacement[i])
            {
                data[offset + i] = replacement[i];
                _modifiedBytes.Add(offset + i);
                RaiseByteModifiedEvent(offset + i, oldValue, replacement[i]);
            }
        }

        InvalidateVisual();
    }

    #endregion

    #region Private Helpers

    private static long SearchPattern(byte[] data, byte[] pattern, long start, long end)
    {
        long searchEnd = end - pattern.Length;
        for (long i = start; i <= searchEnd; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
    }

    private static int HexCharToValue(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return 0;
    }

    private void UpdateCaretBlink()
    {
        long now = Environment.TickCount64;
        if (now - _lastCaretBlinkTicks >= CaretBlinkIntervalMs)
        {
            _caretVisible = !_caretVisible;
            _lastCaretBlinkTicks = now;
        }
    }

    private string GetMonospaceFont()
    {
        return FontFamily ?? "Cascadia Code";
    }

    private double GetFontSize()
    {
        return FontSize > 0 ? FontSize : 14;
    }

    private Size MeasureChar(char c, string fontFamily, double fontSize)
    {
        // Measure using a hex-like string to get accurate average character width
        // that matches the actual rendering (digits + spaces mixed)
        const string sample = "00 11 22 33 44 55 66 77"; // 23 chars, similar to hex row
        var ft = new FormattedText(sample, fontFamily, fontSize)
        {
            Foreground = s_defaultForeground
        };
        TextMeasurement.MeasureText(ft);
        return new Size(ft.Width / sample.Length, ft.Height);
    }

    #endregion

    #region Brush Resolution

    private Brush ResolveOffsetForeground()
    {
        return OffsetForeground
            ?? ResolveThemeBrush("HexEditorOffsetForeground", s_defaultOffsetForeground);
    }

    private Brush ResolveHexForeground()
    {
        return HexForeground
            ?? ResolveThemeBrush("HexEditorHexForeground", s_defaultHexForeground);
    }

    private Brush ResolveAsciiForeground()
    {
        return AsciiForeground
            ?? ResolveThemeBrush("HexEditorAsciiForeground", s_defaultAsciiForeground);
    }

    private Brush ResolveModifiedByteBrush()
    {
        return ModifiedByteBrush
            ?? ResolveThemeBrush("HexEditorModifiedByteBrush", s_defaultModifiedBrush);
    }

    private Brush ResolveSelectionBrush()
    {
        return SelectionBrush
            ?? ResolveThemeBrush("HexEditorSelectionBrush", s_defaultSelectionBrush, "SystemHighlightColor");
    }

    private Brush ResolveGutterBackground()
    {
        return GutterBackground
            ?? ResolveThemeBrush("HexEditorGutterBackground", s_defaultGutterBackground);
    }

    private Brush ResolveColumnSeparatorBrush()
    {
        return ColumnSeparatorBrush
            ?? ResolveThemeBrush("HexEditorColumnSeparatorBrush", s_defaultColumnSeparator);
    }

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

    #region Event Raising

    private void RaiseSelectionChangedEvent(long oldStart, long oldLength, long newStart, long newLength)
    {
        var args = new HexSelectionChangedEventArgs(SelectionChangedEvent,
            oldStart, oldLength, newStart, newLength);
        RaiseEvent(args);
    }

    private void RaiseByteModifiedEvent(long offset, byte oldValue, byte newValue)
    {
        var args = new HexByteModifiedEventArgs(ByteModifiedEvent, offset, oldValue, newValue);
        RaiseEvent(args);
    }

    #endregion
}
