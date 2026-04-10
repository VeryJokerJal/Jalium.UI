using System.Collections;
using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a text box control that provides auto-completion suggestions.
/// Inherits from TextBoxBase for full text editing support.
/// </summary>
public class AutoCompleteBox : TextBoxBase, IImeSupport
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.AutoCompleteBoxAutomationPeer(this);
    }

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_focusBorderBrush = new(ThemeColors.ControlBorderFocused);
    private static readonly SolidColorBrush s_watermarkBrush = new(ThemeColors.TextPlaceholder);
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_compositionBgBrush = new(ThemeColors.CompositionBackground);
    private static readonly SolidColorBrush s_compositionTextBrush = new(ThemeColors.CompositionText);
    private static readonly SolidColorBrush s_compositionUnderlineBrush = new(ThemeColors.CompositionUnderline);
    private static readonly Pen s_compositionUnderlinePen = new(s_compositionUnderlineBrush, 1);
    private static readonly SolidColorBrush s_dropdownShadowBrush = new(ThemeColors.DropdownShadow);
    private static readonly SolidColorBrush s_dropdownBgBrush = new(ThemeColors.DropdownBackground);
    private static readonly SolidColorBrush s_dropdownBorderFallbackBrush = new(ThemeColors.ControlBorder);
    private static readonly SolidColorBrush s_dropdownSelectionBrush = new(ThemeColors.SelectedItemBackground);
    private static readonly SolidColorBrush s_transparentBrush = new(Color.Transparent);
    private static readonly SolidColorBrush s_dropdownHoverBrush = new(ThemeColors.HighlightBackground);

    #endregion

    #region Fields

    // Internal text storage
    private string _text = string.Empty;
    private bool _linesDirty = true;

    // Text width measurement cache
    private Dictionary<string, double> _textWidthCache = new();
    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private const int MaxCacheSize = 256;

    // IME support
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;
    private int _imeCompositionStart;

    // Suggestion state
    private int _selectedSuggestionIndex = -1;
    private bool _isUpdatingText;
    private DateTime _lastFilterTime;
    private DispatcherTimer? _filterDelayTimer;
    private bool _suppressNextTabTextInput;

    // Popup-based dropdown state
    private Popup? _popup;
    private StackPanel? _dropDownItemsPanel;

    // Constants
    private const double DefaultHeight = 32;
    private const double ItemHeight = 28;
    private const double DropdownMaxItems = 8;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AutoCompleteBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(AutoCompleteBox),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(AutoCompleteBox),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>
    /// Identifies the MinimumPrefixLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumPrefixLengthProperty =
        DependencyProperty.Register(nameof(MinimumPrefixLength), typeof(int), typeof(AutoCompleteBox),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the FilterMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty FilterModeProperty =
        DependencyProperty.Register(nameof(FilterMode), typeof(AutoCompleteFilterMode), typeof(AutoCompleteBox),
            new PropertyMetadata(AutoCompleteFilterMode.StartsWith));

    /// <summary>
    /// Identifies the MaxDropDownHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(AutoCompleteBox),
            new PropertyMetadata(224.0));

    /// <summary>
    /// Identifies the MinimumPopulateDelay dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty MinimumPopulateDelayProperty =
        DependencyProperty.Register(nameof(MinimumPopulateDelay), typeof(TimeSpan), typeof(AutoCompleteBox),
            new PropertyMetadata(TimeSpan.Zero, OnPopulateDelayChanged));

    /// <summary>
    /// Identifies the PlaceholderText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextMemberPath dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty TextMemberPathProperty =
        DependencyProperty.Register(nameof(TextMemberPath), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsTextCompletionEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTextCompletionEnabledProperty =
        DependencyProperty.Register(nameof(IsTextCompletionEnabled), typeof(bool), typeof(AutoCompleteBox),
            new PropertyMetadata(false));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the TextChanged routed event.
    /// </summary>
    public static readonly RoutedEvent TextChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(TextChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(AutoCompleteBox));

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(AutoCompleteBox));

    /// <summary>
    /// Identifies the DropDownOpened routed event.
    /// </summary>
    public static readonly RoutedEvent DropDownOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(DropDownOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(AutoCompleteBox));

    /// <summary>
    /// Identifies the DropDownClosed routed event.
    /// </summary>
    public static readonly RoutedEvent DropDownClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(DropDownClosed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(AutoCompleteBox));

    /// <summary>
    /// Identifies the Populating routed event.
    /// </summary>
    public static readonly RoutedEvent PopulatingEvent =
        EventManager.RegisterRoutedEvent(nameof(Populating), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(AutoCompleteBox));

    /// <summary>
    /// Occurs when the text changes.
    /// </summary>
    public event RoutedEventHandler TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the drop-down opens.
    /// </summary>
    public event RoutedEventHandler DropDownOpened
    {
        add => AddHandler(DropDownOpenedEvent, value);
        remove => RemoveHandler(DropDownOpenedEvent, value);
    }

    /// <summary>
    /// Occurs when the drop-down closes.
    /// </summary>
    public event RoutedEventHandler DropDownClosed
    {
        add => AddHandler(DropDownClosedEvent, value);
        remove => RemoveHandler(DropDownClosedEvent, value);
    }

    /// <summary>
    /// Occurs when the suggestion list is about to be populated.
    /// </summary>
    public event RoutedEventHandler Populating
    {
        add => AddHandler(PopulatingEvent, value);
        remove => RemoveHandler(PopulatingEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text in the text box.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value ?? string.Empty;
                SetValue(TextProperty, _text);
                _linesDirty = true;

                if (_caretIndex > _text.Length)
                    _caretIndex = _text.Length;

                if (!_isUpdatingText)
                {
                    TriggerFilterUpdate();
                    RaiseEvent(new RoutedEventArgs(TextChangedEvent, this));
                }

                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the items source for suggestions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the drop-down is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum number of characters to type before suggestions appear.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int MinimumPrefixLength
    {
        get => (int)GetValue(MinimumPrefixLengthProperty)!;
        set => SetValue(MinimumPrefixLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the filter mode for suggestions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public AutoCompleteFilterMode FilterMode
    {
        get => (AutoCompleteFilterMode)(GetValue(FilterModeProperty) ?? AutoCompleteFilterMode.StartsWith);
        set => SetValue(FilterModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the drop-down.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty)!;
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the delay before populating suggestions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TimeSpan MinimumPopulateDelay
    {
        get => (TimeSpan)GetValue(MinimumPopulateDelayProperty)!;
        set => SetValue(MinimumPopulateDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text displayed when the text box is empty.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path for text display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public string? TextMemberPath
    {
        get => (string?)GetValue(TextMemberPathProperty);
        set => SetValue(TextMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text completion is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTextCompletionEnabled
    {
        get => (bool)GetValue(IsTextCompletionEnabledProperty)!;
        set => SetValue(IsTextCompletionEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets a custom filter predicate.
    /// </summary>
    public Func<string, object, bool>? ItemFilter { get; set; }

    /// <summary>
    /// Gets the filtered suggestions.
    /// </summary>
    public ObservableCollection<object> FilteredItems { get; } = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoCompleteBox"/> class.
    /// </summary>
    public AutoCompleteBox()
    {
        // Set IBeam cursor for text input
        Cursor = Jalium.UI.Cursors.IBeam;

        // Subscribe to IME events
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Subscribe to focus events
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocusHandler));
        SizeChanged += OnAutoCompleteBoxSizeChanged;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
        }

        base.OnApplyTemplate();

        _popup = GetTemplateChild("PART_Popup") as Popup;
        _dropDownItemsPanel = GetTemplateChild("PART_DropDownItemsHost") as StackPanel;

        if (_popup != null)
        {
            _popup.PlacementTarget = this;
            _popup.Closed += OnPopupClosed;
        }

        EnsureDropDownItemsPanel();
        UpdatePopupPlacementAndWidth();
        RefreshDropDownItems();
        SyncPopupOpenState();
    }

    private void OnGotFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputMethod.SetTarget(this);
        InvalidateVisual();
    }

    private void OnLostFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            InputMethod.SetTarget(null);
        }
        // In popup mode, keep the dropdown open while user is interacting with it via mouse.
        // For keyboard focus moves (e.g. Tab to next control), close immediately.
        if (_popup == null || !_popup.IsMouseOver)
        {
            IsDropDownOpen = false;
        }
        InvalidateVisual();
    }

    private void OnAutoCompleteBoxSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdatePopupPlacementAndWidth();
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (IsDropDownOpen)
        {
            SetValue(IsDropDownOpenProperty, false);
        }
    }

    private void OnImeCompositionStarted(object? sender, EventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionStart();
        }
    }

    private void OnImeCompositionUpdated(object? sender, CompositionEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionUpdate(e.Text, e.CursorPosition);
        }
    }

    private void OnImeCompositionEnded(object? sender, CompositionResultEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionEnd(e.Result);
        }
    }

    #endregion

    #region Abstract Method Implementations

    /// <inheritdoc />
    protected override string GetText() => _text;

    /// <inheritdoc />
    protected override void SetText(string value)
    {
        Text = value;
    }

    /// <inheritdoc />
    protected override double GetLineHeight()
    {
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        return fontMetrics.LineHeight;
    }

    /// <inheritdoc />
    protected override double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;

        if (_cachedFontFamily != fontFamily || _cachedFontSize != fontSize)
        {
            _textWidthCache.Clear();
            _cachedFontFamily = fontFamily;
            _cachedFontSize = fontSize;
        }

        if (_textWidthCache.TryGetValue(text, out var cachedWidth))
            return cachedWidth;

        var formattedText = new FormattedText(text, fontFamily, fontSize);
        var usedNative = TextMeasurement.MeasureText(formattedText);

        double width;
        if (usedNative && formattedText.IsMeasured)
        {
            width = formattedText.Width;
        }
        else
        {
            width = text.Length * fontSize * 0.6;
        }

        if (_textWidthCache.Count >= MaxCacheSize)
        {
            var keysToRemove = _textWidthCache.Keys.Take(MaxCacheSize / 2).ToList();
            foreach (var key in keysToRemove)
                _textWidthCache.Remove(key);
        }
        _textWidthCache[text] = width;

        return width;
    }

    /// <inheritdoc />
    protected override int GetLineCount() => 1;

    /// <inheritdoc />
    protected override (int lineIndex, int columnIndex) GetLineColumnFromCharIndex(int charIndex)
    {
        return (0, Math.Clamp(_text.Length, 0, charIndex));
    }

    /// <inheritdoc />
    protected override int GetCharIndexFromLineColumn(int lineIndex, int columnIndex)
    {
        return Math.Clamp(_text.Length, 0, columnIndex);
    }

    /// <inheritdoc />
    protected override string GetLineTextInternal(int lineIndex)
    {
        return _text;
    }

    /// <inheritdoc />
    protected override int GetLineStartIndex(int lineIndex)
    {
        return 0;
    }

    /// <inheritdoc />
    protected override int GetLineLengthInternal(int lineIndex)
    {
        return _text.Length;
    }

    #endregion

    #region Key Handling Override

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (IsDropDownOpen && FilteredItems.Count > 0)
                {
                    _selectedSuggestionIndex = Math.Min(_selectedSuggestionIndex + 1, FilteredItems.Count - 1);
                    UpdateDropDownSelectionVisuals();
                    InvalidateVisual();
                }
                else if (FilteredItems.Count > 0)
                {
                    IsDropDownOpen = true;
                    _selectedSuggestionIndex = 0;
                    RefreshDropDownItems();
                }
                e.Handled = true;
                return;

            case Key.Up:
                if (IsDropDownOpen && _selectedSuggestionIndex > 0)
                {
                    _selectedSuggestionIndex--;
                    UpdateDropDownSelectionVisuals();
                    InvalidateVisual();
                }
                e.Handled = true;
                return;

            case Key.Enter:
                if (IsDropDownOpen && _selectedSuggestionIndex >= 0 && _selectedSuggestionIndex < FilteredItems.Count)
                {
                    SelectSuggestion(_selectedSuggestionIndex);
                }
                IsDropDownOpen = false;
                e.Handled = true;
                return;

            case Key.Escape:
                if (IsDropDownOpen)
                {
                    IsDropDownOpen = false;
                    e.Handled = true;
                    return;
                }
                break;

            case Key.Tab:
                if (IsDropDownOpen && _selectedSuggestionIndex >= 0 && _selectedSuggestionIndex < FilteredItems.Count)
                {
                    SelectSuggestion(_selectedSuggestionIndex);
                    IsDropDownOpen = false;
                    _suppressNextTabTextInput = true;
                    e.Handled = true;
                    return;
                }
                break;
        }

        base.OnKeyDown(e);
    }

    #endregion

    #region Text Input Override

    /// <inheritdoc />
    protected override void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert))
            return;

        // Tab completion should not also insert a literal tab character.
        // Also keep default single-line behavior when AcceptsTab is false.
        var suppressTab = _suppressNextTabTextInput;
        _suppressNextTabTextInput = false;

        if (textToInsert.IndexOf('\t') >= 0 && (suppressTab || !AcceptsTab))
        {
            textToInsert = textToInsert.Replace("\t", string.Empty);
            if (string.IsNullOrEmpty(textToInsert))
                return;
        }

        PushUndo();

        // Delete selection if any
        if (_selectionLength > 0)
        {
            DeleteSelectionInternal();
        }

        // Ensure caret is within bounds
        if (_caretIndex < 0) _caretIndex = 0;
        if (_caretIndex > _text.Length) _caretIndex = _text.Length;

        // Insert text
        _text = _text.Substring(0, _caretIndex) + textToInsert + _text.Substring(_caretIndex);
        _caretIndex += textToInsert.Length;
        _linesDirty = true;

        // Trigger filter update
        TriggerFilterUpdate();
        RaiseEvent(new RoutedEventArgs(TextChangedEvent, this));

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    #endregion

    #region Suggestion Handling

    private void TriggerFilterUpdate()
    {
        var delay = MinimumPopulateDelay;
        if (delay > TimeSpan.Zero)
        {
            _lastFilterTime = DateTime.Now;

            if (_filterDelayTimer == null)
            {
                _filterDelayTimer = new DispatcherTimer();
                _filterDelayTimer.Interval = delay;
                _filterDelayTimer.Tick += (s, e) =>
                {
                    _filterDelayTimer.Stop();
                    // Check if enough time has passed since last update
                    if ((DateTime.Now - _lastFilterTime).TotalMilliseconds >= delay.TotalMilliseconds - 10)
                    {
                        UpdateFilteredItems();
                    }
                };
            }

            _filterDelayTimer.Stop();
            _filterDelayTimer.Start();
        }
        else
        {
            UpdateFilteredItems();
        }
    }

    private void UpdateFilteredItems()
    {
        if (_isUpdatingText) return;

        RaiseEvent(new RoutedEventArgs(PopulatingEvent, this));

        FilteredItems.Clear();
        _selectedSuggestionIndex = -1;

        if (ItemsSource == null || string.IsNullOrEmpty(_text) || _text.Length < MinimumPrefixLength)
        {
            IsDropDownOpen = false;
            RefreshDropDownItems();
            return;
        }

        foreach (var item in ItemsSource)
        {
            if (MatchesFilter(item))
            {
                FilteredItems.Add(item);
            }
        }

        if (FilteredItems.Count > 0)
        {
            _selectedSuggestionIndex = 0;
            IsDropDownOpen = true;

            // Auto-complete first match if enabled
            if (IsTextCompletionEnabled && FilteredItems.Count > 0)
            {
                var firstItemText = GetItemText(FilteredItems[0]);
                if (firstItemText.StartsWith(_text, StringComparison.OrdinalIgnoreCase) && firstItemText.Length > _text.Length)
                {
                    var completion = firstItemText.Substring(_text.Length);
                    _isUpdatingText = true;
                    var currentCaret = _caretIndex;
                    _text = _text + completion;
                    _selectionStart = currentCaret;
                    _selectionLength = completion.Length;
                    _caretIndex = _text.Length;
                    _isUpdatingText = false;
                }
            }
        }
        else
        {
            IsDropDownOpen = false;
        }

        RefreshDropDownItems();
        InvalidateVisual();
    }

    private bool MatchesFilter(object item)
    {
        var itemText = GetItemText(item);
        if (string.IsNullOrEmpty(itemText)) return false;

        var searchText = _text;

        // Use custom filter if provided
        if (ItemFilter != null)
        {
            return ItemFilter(searchText, item);
        }

        // Use built-in filter modes
        return FilterMode switch
        {
            AutoCompleteFilterMode.StartsWith =>
                itemText.StartsWith(searchText, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.StartsWithCaseSensitive =>
                itemText.StartsWith(searchText, StringComparison.Ordinal),
            AutoCompleteFilterMode.Contains =>
                itemText.Contains(searchText, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.ContainsCaseSensitive =>
                itemText.Contains(searchText, StringComparison.Ordinal),
            AutoCompleteFilterMode.Equals =>
                itemText.Equals(searchText, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.EqualsCaseSensitive =>
                itemText.Equals(searchText, StringComparison.Ordinal),
            AutoCompleteFilterMode.Custom => true,
            AutoCompleteFilterMode.None => true,
            _ => true
        };
    }

    private string GetItemText(object item)
    {
        if (item == null) return string.Empty;

        if (!string.IsNullOrEmpty(TextMemberPath))
        {
            var prop = item.GetType().GetProperty(TextMemberPath);
            return prop?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    private void SelectSuggestion(int index)
    {
        if (index < 0 || index >= FilteredItems.Count) return;

        var item = FilteredItems[index];
        var oldItem = SelectedItem;

        _isUpdatingText = true;
        _text = GetItemText(item);
        _caretIndex = _text.Length;
        _selectionStart = 0;
        _selectionLength = 0;
        _isUpdatingText = false;

        SelectedItem = item;
        IsDropDownOpen = false;
        RefreshDropDownItems();

        var args = new SelectionChangedEventArgs(SelectionChangedEvent,
            oldItem != null ? new[] { oldItem } : Array.Empty<object>(),
            new[] { item });
        RaiseEvent(args);

        InvalidateVisual();
    }

    private void EnsureDropDownItemsPanel()
    {
        if (_dropDownItemsPanel != null)
            return;

        if (_popup?.Child is Border border && border.Child is ScrollViewer scrollViewer)
        {
            if (scrollViewer.Content is StackPanel panel)
            {
                _dropDownItemsPanel = panel;
            }
            else
            {
                _dropDownItemsPanel = new StackPanel();
                scrollViewer.Content = _dropDownItemsPanel;
            }
        }
    }

    private void RefreshDropDownItems()
    {
        EnsureDropDownItemsPanel();
        if (_dropDownItemsPanel == null)
        {
            SyncPopupOpenState();
            return;
        }

        _dropDownItemsPanel.Children.Clear();

        for (var i = 0; i < FilteredItems.Count; i++)
        {
            var index = i;
            var item = FilteredItems[i];

            var itemContainer = new ComboBoxItem
            {
                Content = GetItemText(item),
                Tag = item,
                Foreground = Foreground ?? s_whiteBrush,
                MinHeight = ItemHeight,
                Background = index == _selectedSuggestionIndex ? ResolveSelectedSuggestionBackground() : s_transparentBrush
            };

            itemContainer.ItemClicked += (s, e) => SelectSuggestion(index);
            _dropDownItemsPanel.Children.Add(itemContainer);
        }

        SyncPopupOpenState();
    }

    private void UpdateDropDownSelectionVisuals()
    {
        if (_dropDownItemsPanel == null)
            return;

        for (var i = 0; i < _dropDownItemsPanel.Children.Count; i++)
        {
            if (_dropDownItemsPanel.Children[i] is ComboBoxItem itemContainer)
            {
                itemContainer.Background = i == _selectedSuggestionIndex ? ResolveSelectedSuggestionBackground() : s_transparentBrush;
            }
        }
    }

    private void UpdatePopupPlacementAndWidth()
    {
        if (_popup == null)
            return;

        _popup.PlacementTarget = this;

        var popupWidth = ActualWidth;
        if (popupWidth <= 0 || double.IsNaN(popupWidth) || double.IsInfinity(popupWidth))
        {
            if (!double.IsNaN(Width) && !double.IsInfinity(Width) && Width > 0)
                popupWidth = Width;
        }

        if (popupWidth > 0 && !double.IsNaN(popupWidth) && !double.IsInfinity(popupWidth))
        {
            _popup.Width = popupWidth;
            _popup.MinWidth = popupWidth;
            _popup.MaxWidth = popupWidth;
        }
    }

    private void SyncPopupOpenState()
    {
        if (_popup == null)
            return;

        var shouldOpen = IsDropDownOpen && FilteredItems.Count > 0;
        if (_popup.IsOpen != shouldOpen)
        {
            _popup.IsOpen = shouldOpen;
        }
    }

    #endregion

    #region Mouse Handling

    /// <inheritdoc />
    protected override int GetCaretIndexFromPosition(Point position)
    {
        // In non-popup rendering mode, the dropdown is drawn below the input box.
        if (_popup == null && IsDropDownOpen && position.Y > DefaultHeight)
        {
            var dropdownY = position.Y - DefaultHeight;
            var suggestionIndex = (int)(dropdownY / ItemHeight);
            if (suggestionIndex >= 0 && suggestionIndex < FilteredItems.Count)
            {
                SelectSuggestion(suggestionIndex);
                return _caretIndex;
            }
        }

        return base.GetCaretIndexFromPosition(position);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // If using template, delegate to base class which handles template root
        if (Template != null)
        {
            return base.MeasureOverride(availableSize);
        }

        // Direct rendering mode
        var width = double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width;
        var height = DefaultHeight;

        // Account for drop-down height when open
        if (IsDropDownOpen && FilteredItems.Count > 0)
        {
            var dropDownHeight = Math.Min(FilteredItems.Count * ItemHeight, MaxDropDownHeight);
            height += dropDownHeight;
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    internal override Size MeasureTextContent(Size availableSize)
    {
        var lineHeight = Math.Round(GetLineHeight());
        return new Size(availableSize.Width, Math.Min(lineHeight, availableSize.Height));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // If using content host, text rendering is handled by TextBoxContentHost
        // In template + popup mode, dropdown is rendered by Popup content.
        // Keep drawing fallback only for non-popup templates.
        if (HasContentHost)
        {
            if (_popup == null && drawingContext is DrawingContext dc && IsDropDownOpen && FilteredItems.Count > 0)
            {
                DrawDropDown(dc);
            }
            return;
        }

        // Direct rendering mode
        if (drawingContext is not DrawingContext directDc)
            return;

        var inputRect = new Rect(0, 0, RenderSize.Width, DefaultHeight);
        var cornerRadius = CornerRadius;
        var lineHeight = Math.Round(GetLineHeight());
        var padding = Padding;
        var strokeThickness = BorderThickness.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(inputRect, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);

        // Draw background and border
        if (Background != null)
        {
            directDc.DrawRoundedRectangle(Background, null, borderRect, borderRadius);
        }

        var borderBrush = IsKeyboardFocused ? ResolveFocusedBorderBrush() : BorderBrush;
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, strokeThickness);
            directDc.DrawRoundedRectangle(null, pen, borderRect, borderRadius);
        }

        if (IsKeyboardFocused)
        {
            ControlFocusVisual.Draw(directDc, this, inputRect, cornerRadius);
        }

        // Content area
        var contentRect = new Rect(
            padding.Left,
            padding.Top,
            Math.Max(0, inputRect.Width - padding.Left - padding.Right),
            Math.Max(0, inputRect.Height - padding.Top - padding.Bottom));

        // Render text content
        RenderTextContentCore(directDc, contentRect, lineHeight);

        // Draw drop-down
        if (IsDropDownOpen && FilteredItems.Count > 0)
        {
            DrawDropDown(directDc);
        }
    }

    /// <inheritdoc />
    internal override void RenderTextContent(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var lineHeight = Math.Round(GetLineHeight());
        var contentRect = new Rect(0, 0, _textContentSize.Width, _textContentSize.Height);

        RenderTextContentCore(dc, contentRect, lineHeight);
    }

    /// <summary>
    /// Core text rendering logic used by both direct rendering and content host modes.
    /// </summary>
    private void RenderTextContentCore(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        // Clip to content area
        dc.PushClip(new RectangleGeometry(contentRect));

        // Draw selection background
        if (_selectionLength > 0 && IsKeyboardFocused)
        {
            DrawSelection(dc, contentRect, lineHeight);
        }

        // Draw text or placeholder
        if (string.IsNullOrEmpty(_text) && !string.IsNullOrEmpty(PlaceholderText))
        {
            var watermarkText = new FormattedText(PlaceholderText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
            {
                Foreground = ResolvePlaceholderBrush(),
                MaxTextWidth = contentRect.Width,
                MaxTextHeight = lineHeight,
                Trimming = TextTrimming
            };
            TextMeasurement.MeasureText(watermarkText);
            var textY = (contentRect.Height - watermarkText.Height) / 2;
            dc.DrawText(watermarkText, new Point(contentRect.X - Math.Round(_horizontalOffset), contentRect.Y + textY));
        }
        else if (!string.IsNullOrEmpty(_text))
        {
            var formattedText = new FormattedText(_text, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
            {
                Foreground = ResolveTextForegroundBrush(),
                MaxTextWidth = contentRect.Width,
                MaxTextHeight = lineHeight,
                Trimming = TextTrimming
            };
            TextMeasurement.MeasureText(formattedText);
            var textY = (contentRect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(contentRect.X - Math.Round(_horizontalOffset), contentRect.Y + textY));
        }

        // Draw IME composition
        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
        {
            DrawImeComposition(dc, contentRect, lineHeight);
        }

        // Draw caret
        if (IsFocused && !IsReadOnly)
        {
            DrawCaret(dc, contentRect, lineHeight);
        }

        dc.Pop(); // Pop clip
    }

    private void DrawSelection(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var selectionBrush = ResolveSelectionBrush();
        if (selectionBrush == null)
            return;

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var textBefore = _text.Substring(0, Math.Min(_selectionStart, _text.Length));
        var selectedText = _text.Substring(_selectionStart, Math.Min(_selectionLength, _text.Length - _selectionStart));

        var startX = Math.Round(contentRect.X + MeasureTextWidth(textBefore) - roundedHorizontalOffset);
        var width = Math.Max(Math.Round(MeasureTextWidth(selectedText)), 1);
        var textY = contentRect.Y + (contentRect.Height - lineHeight) / 2;

        var selRect = new Rect(startX, textY, width, lineHeight);
        dc.DrawRectangle(selectionBrush, null, selRect);
    }

    private void DrawImeComposition(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        if (string.IsNullOrEmpty(_imeCompositionString))
            return;

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var textBeforeCaret = _text.Substring(0, Math.Min(_caretIndex, _text.Length));
        var x = Math.Round(contentRect.X + MeasureTextWidth(textBeforeCaret) - roundedHorizontalOffset);
        var textY = contentRect.Y + (contentRect.Height - lineHeight) / 2;

        var compositionWidth = MeasureTextWidth(_imeCompositionString);
        dc.DrawRectangle(s_compositionBgBrush, null, new Rect(x, textY, compositionWidth, lineHeight));

        var compositionText = new FormattedText(_imeCompositionString, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize)
        {
            Foreground = s_compositionTextBrush,
            MaxTextWidth = contentRect.Width,
            MaxTextHeight = lineHeight
        };
        dc.DrawText(compositionText, new Point(x, textY));

        dc.DrawLine(s_compositionUnderlinePen, new Point(x, textY + lineHeight - 2), new Point(x + compositionWidth, textY + lineHeight - 2));
    }

    private void DrawCaret(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var caretOpacity = UpdateCaretAnimation();

        var caretBrush = ResolveCaretBrush();
        if (caretBrush == null || _isImeComposing || caretOpacity < 0.01)
            return;

        var columnIndex = Math.Min(_caretIndex, _text.Length);
        var textBeforeCaret = _text.Substring(0, columnIndex);

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var x = Math.Round(contentRect.X + MeasureTextWidth(textBeforeCaret) - roundedHorizontalOffset);
        var textY = contentRect.Y + (contentRect.Height - lineHeight) / 2;

        Brush caretBrushWithOpacity;
        if (caretBrush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            var alpha = (byte)(color.A * caretOpacity);
            caretBrushWithOpacity = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            caretBrushWithOpacity = caretBrush;
        }

        var caretPen = new Pen(caretBrushWithOpacity, 1.5);
        dc.DrawLine(caretPen, new Point(x, textY), new Point(x, textY + lineHeight));
    }

    private void DrawDropDown(DrawingContext dc)
    {
        var dropDownTop = DefaultHeight;
        var dropDownHeight = Math.Min(FilteredItems.Count * ItemHeight, MaxDropDownHeight);
        var dropDownRect = new Rect(0, dropDownTop, RenderSize.Width, dropDownHeight);

        // Draw drop-down background with shadow effect (simplified)
        var shadowRect = new Rect(2, dropDownTop + 2, RenderSize.Width, dropDownHeight);
        dc.DrawRectangle(s_dropdownShadowBrush, null, shadowRect);

        var dropDownBorder = new Pen(ResolveDropDownBorderBrush(), 1);
        dc.DrawRectangle(ResolveDropDownBackground(), dropDownBorder, dropDownRect);

        // Clip to dropdown
        dc.PushClip(new RectangleGeometry(dropDownRect));

        // Draw items
        var y = dropDownTop;

        for (var i = 0; i < FilteredItems.Count && y < dropDownTop + dropDownHeight; i++)
        {
            var itemRect = new Rect(1, y, RenderSize.Width - 2, ItemHeight);

            // Draw selection background
            if (i == _selectedSuggestionIndex)
            {
                dc.DrawRectangle(ResolveSelectedSuggestionBackground(), null, itemRect);
            }

            // Draw item text
            var itemText = GetItemText(FilteredItems[i]);
            var formattedText = new FormattedText(itemText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 13)
            {
                Foreground = Foreground ?? s_whiteBrush
            };
            TextMeasurement.MeasureText(formattedText);
            var itemTextY = y + (ItemHeight - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(Padding.Left, itemTextY));

            y += ItemHeight;
        }

        dc.Pop(); // Pop dropdown clip
    }

    private Brush ResolveFocusedBorderBrush()
    {
        return TryFindResource("ControlBorderFocused") as Brush ?? s_focusBorderBrush;
    }

    private Brush ResolvePlaceholderBrush()
    {
        return TryFindResource("TextPlaceholder") as Brush ?? s_watermarkBrush;
    }

    private Brush ResolveSelectedSuggestionBackground()
    {
        return TryFindResource("AccentBrush") as Brush ?? s_dropdownSelectionBrush;
    }

    private Brush ResolveDropDownBackground()
    {
        return TryFindResource("SurfaceBackground") as Brush
            ?? TryFindResource("ControlBackground") as Brush
            ?? s_dropdownBgBrush;
    }

    private Brush ResolveDropDownBorderBrush()
    {
        return BorderBrush
            ?? TryFindResource("ControlBorder") as Brush
            ?? TryFindResource("MenuFlyoutPresenterBorderBrush") as Brush
            ?? s_dropdownBorderFallbackBrush;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            var newText = (string)(e.NewValue ?? string.Empty);
            if (autoComplete._text != newText)
            {
                autoComplete._text = newText;
                autoComplete._linesDirty = true;

                if (autoComplete._caretIndex > autoComplete._text.Length)
                    autoComplete._caretIndex = autoComplete._text.Length;

                autoComplete.InvalidateVisual();
            }
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            autoComplete.UpdateFilteredItems();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            autoComplete.InvalidateVisual();
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            if ((bool)e.NewValue)
            {
                autoComplete.UpdatePopupPlacementAndWidth();
                autoComplete.RefreshDropDownItems();
                autoComplete.RaiseEvent(new RoutedEventArgs(DropDownOpenedEvent, autoComplete));
            }
            else
            {
                autoComplete.SyncPopupOpenState();
                autoComplete.RaiseEvent(new RoutedEventArgs(DropDownClosedEvent, autoComplete));
            }
            autoComplete.InvalidateMeasure();
            autoComplete.InvalidateVisual();
        }
    }

    private static void OnPopulateDelayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete && autoComplete._filterDelayTimer != null)
        {
            var delay = (TimeSpan)e.NewValue;
            autoComplete._filterDelayTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            autoComplete.UpdateDropDownSelectionVisuals();
            autoComplete.InvalidateVisual();
        }
    }

    #endregion

    #region IME Support

    /// <summary>
    /// Gets whether IME composition is currently active.
    /// </summary>
    public bool IsImeComposing => _isImeComposing;

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        return GetCaretScreenPosition();
    }

    private Point GetCaretScreenPosition()
    {
        var lineHeight = Math.Round(GetLineHeight());
        var columnIndex = Math.Min(_caretIndex, _text.Length);
        var textBeforeCaret = _text.Substring(0, columnIndex);

        double x = Padding.Left - _horizontalOffset + MeasureTextWidth(textBeforeCaret);
        double y = Padding.Top;

        return new Point(x, y + lineHeight);
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionStart = _caretIndex;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        if (_selectionLength > 0)
        {
            DeleteSelection();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;
        InvalidateVisual();
    }

    #endregion
}

/// <summary>
/// Specifies the filter mode for auto-complete suggestions.
/// </summary>
public enum AutoCompleteFilterMode
{
    /// <summary>
    /// No filtering is performed.
    /// </summary>
    None,

    /// <summary>
    /// Items that start with the search text (case-insensitive).
    /// </summary>
    StartsWith,

    /// <summary>
    /// Items that start with the search text (case-sensitive).
    /// </summary>
    StartsWithCaseSensitive,

    /// <summary>
    /// Items that contain the search text (case-insensitive).
    /// </summary>
    Contains,

    /// <summary>
    /// Items that contain the search text (case-sensitive).
    /// </summary>
    ContainsCaseSensitive,

    /// <summary>
    /// Items that equal the search text (case-insensitive).
    /// </summary>
    Equals,

    /// <summary>
    /// Items that equal the search text (case-sensitive).
    /// </summary>
    EqualsCaseSensitive,

    /// <summary>
    /// Custom filtering using ItemFilter predicate.
    /// </summary>
    Custom
}
