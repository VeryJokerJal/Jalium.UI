using System.Collections;
using System.Collections.ObjectModel;
using System.Timers;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a text box control that provides auto-completion suggestions.
/// </summary>
public class AutoCompleteBox : Control, IImeSupport
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AutoCompleteBox),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(AutoCompleteBox),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the IsDropDownOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(AutoCompleteBox),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>
    /// Identifies the MinimumPrefixLength dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumPrefixLengthProperty =
        DependencyProperty.Register(nameof(MinimumPrefixLength), typeof(int), typeof(AutoCompleteBox),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the FilterMode dependency property.
    /// </summary>
    public static readonly DependencyProperty FilterModeProperty =
        DependencyProperty.Register(nameof(FilterMode), typeof(AutoCompleteFilterMode), typeof(AutoCompleteBox),
            new PropertyMetadata(AutoCompleteFilterMode.StartsWith));

    /// <summary>
    /// Identifies the MaxDropDownHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(AutoCompleteBox),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the MinimumPopulateDelay dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumPopulateDelayProperty =
        DependencyProperty.Register(nameof(MinimumPopulateDelay), typeof(TimeSpan), typeof(AutoCompleteBox),
            new PropertyMetadata(TimeSpan.Zero));

    /// <summary>
    /// Identifies the Watermark dependency property.
    /// </summary>
    public static readonly DependencyProperty WatermarkProperty =
        DependencyProperty.Register(nameof(Watermark), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextMemberPath dependency property.
    /// </summary>
    public static readonly DependencyProperty TextMemberPathProperty =
        DependencyProperty.Register(nameof(TextMemberPath), typeof(string), typeof(AutoCompleteBox),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the IsTextCompletionEnabled dependency property.
    /// </summary>
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

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text in the text box.
    /// </summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the items source for suggestions.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the drop-down is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => (bool)(GetValue(IsDropDownOpenProperty) ?? false);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum number of characters to type before suggestions appear.
    /// </summary>
    public int MinimumPrefixLength
    {
        get => (int)(GetValue(MinimumPrefixLengthProperty) ?? 1);
        set => SetValue(MinimumPrefixLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the filter mode for suggestions.
    /// </summary>
    public AutoCompleteFilterMode FilterMode
    {
        get => (AutoCompleteFilterMode)(GetValue(FilterModeProperty) ?? AutoCompleteFilterMode.StartsWith);
        set => SetValue(FilterModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the drop-down.
    /// </summary>
    public double MaxDropDownHeight
    {
        get => (double)(GetValue(MaxDropDownHeightProperty) ?? 200.0);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the delay before populating suggestions.
    /// </summary>
    public TimeSpan MinimumPopulateDelay
    {
        get => (TimeSpan)(GetValue(MinimumPopulateDelayProperty) ?? TimeSpan.Zero);
        set => SetValue(MinimumPopulateDelayProperty, value);
    }

    /// <summary>
    /// Gets or sets the watermark text.
    /// </summary>
    public string? Watermark
    {
        get => (string?)GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path for text display.
    /// </summary>
    public string? TextMemberPath
    {
        get => (string?)GetValue(TextMemberPathProperty);
        set => SetValue(TextMemberPathProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text completion is enabled.
    /// </summary>
    public bool IsTextCompletionEnabled
    {
        get => (bool)(GetValue(IsTextCompletionEnabledProperty) ?? false);
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

    #region Private Fields

    private const double DefaultHeight = 28;
    private const double ItemHeight = 24;
    private int _selectedSuggestionIndex = -1;
    private int _caretPosition;
    private bool _isUpdatingText;

    // Caret animation
    private double _caretOpacity = 1.0;
    private DateTime _lastCaretBlink;
    private const int CaretBlinkInterval = 530;
    private const int CaretFadeDuration = 150;
    private const int CaretTimerInterval = 16;
    private System.Timers.Timer? _caretTimer;

    // Scrolling
    private double _horizontalOffset;

    // IME composition state
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCursorPosition;

    // Text width cache
    private readonly Dictionary<string, double> _textWidthCache = new();
    private const int MaxCacheSize = 128;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoCompleteBox"/> class.
    /// </summary>
    public AutoCompleteBox()
    {
        Focusable = true;
        Height = DefaultHeight;
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        Foreground = new SolidColorBrush(Color.White);
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8, 4, 8, 4);
        CornerRadius = new CornerRadius(4);
        FontSize = 14;

        // Set IBeam cursor for text input
        Cursor = Jalium.UI.Cursors.IBeam;

        _lastCaretBlink = DateTime.Now;

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new RoutedEventHandler(OnTextInputHandler));

        // Subscribe to IME events
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Subscribe to focus events for IME target management
        AddHandler(GotKeyboardFocusEvent, new RoutedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new RoutedEventHandler(OnLostFocusHandler));
    }

    private void OnGotFocusHandler(object sender, RoutedEventArgs e)
    {
        InputMethod.SetTarget(this);
        StartCaretTimer();
        ResetCaretBlink();
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            InputMethod.SetTarget(null);
        }
        StopCaretTimer();
        IsDropDownOpen = false;

        // End any active IME composition
        if (_isImeComposing)
        {
            _isImeComposing = false;
            _imeCompositionString = string.Empty;
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = mouseArgs.GetPosition(this);

            // Check if clicked on a suggestion in dropdown
            if (IsDropDownOpen)
            {
                var dropDownTop = DefaultHeight;
                if (position.Y > dropDownTop)
                {
                    var suggestionIndex = (int)((position.Y - dropDownTop) / ItemHeight);
                    if (suggestionIndex >= 0 && suggestionIndex < FilteredItems.Count)
                    {
                        SelectSuggestion(suggestionIndex);
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Position caret based on click in text area
            if (position.Y <= DefaultHeight)
            {
                _caretPosition = GetCaretIndexFromPosition(position);
                ResetCaretBlink();
                EnsureCaretVisible();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            switch (keyArgs.Key)
            {
                case Key.Down:
                    if (IsDropDownOpen)
                    {
                        _selectedSuggestionIndex = Math.Min(_selectedSuggestionIndex + 1, FilteredItems.Count - 1);
                        InvalidateVisual();
                    }
                    else if (FilteredItems.Count > 0)
                    {
                        IsDropDownOpen = true;
                    }
                    e.Handled = true;
                    break;

                case Key.Up:
                    if (IsDropDownOpen)
                    {
                        _selectedSuggestionIndex = Math.Max(_selectedSuggestionIndex - 1, 0);
                        InvalidateVisual();
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (IsDropDownOpen && _selectedSuggestionIndex >= 0)
                    {
                        SelectSuggestion(_selectedSuggestionIndex);
                    }
                    IsDropDownOpen = false;
                    e.Handled = true;
                    break;

                case Key.Escape:
                    IsDropDownOpen = false;
                    e.Handled = true;
                    break;

                case Key.Tab:
                    if (IsDropDownOpen && _selectedSuggestionIndex >= 0)
                    {
                        SelectSuggestion(_selectedSuggestionIndex);
                        IsDropDownOpen = false;
                        e.Handled = true;
                    }
                    break;

                case Key.Back:
                    if (keyArgs.IsControlDown)
                    {
                        // Delete all text before caret
                        if (_caretPosition > 0)
                        {
                            Text = Text.Substring(_caretPosition);
                            _caretPosition = 0;
                        }
                    }
                    else if (_caretPosition > 0)
                    {
                        Text = Text.Remove(_caretPosition - 1, 1);
                        _caretPosition--;
                    }
                    ResetCaretBlink();
                    EnsureCaretVisible();
                    e.Handled = true;
                    break;

                case Key.Delete:
                    if (keyArgs.IsControlDown)
                    {
                        // Delete all text after caret
                        if (_caretPosition < Text.Length)
                        {
                            Text = Text.Substring(0, _caretPosition);
                        }
                    }
                    else if (_caretPosition < Text.Length)
                    {
                        Text = Text.Remove(_caretPosition, 1);
                    }
                    ResetCaretBlink();
                    e.Handled = true;
                    break;

                case Key.Left:
                    if (keyArgs.IsControlDown)
                    {
                        // Move to beginning
                        _caretPosition = 0;
                    }
                    else
                    {
                        _caretPosition = Math.Max(0, _caretPosition - 1);
                    }
                    ResetCaretBlink();
                    EnsureCaretVisible();
                    InvalidateVisual();
                    e.Handled = true;
                    break;

                case Key.Right:
                    if (keyArgs.IsControlDown)
                    {
                        // Move to end
                        _caretPosition = Text.Length;
                    }
                    else
                    {
                        _caretPosition = Math.Min(Text.Length, _caretPosition + 1);
                    }
                    ResetCaretBlink();
                    EnsureCaretVisible();
                    InvalidateVisual();
                    e.Handled = true;
                    break;

                case Key.Home:
                    _caretPosition = 0;
                    ResetCaretBlink();
                    EnsureCaretVisible();
                    InvalidateVisual();
                    e.Handled = true;
                    break;

                case Key.End:
                    _caretPosition = Text.Length;
                    ResetCaretBlink();
                    EnsureCaretVisible();
                    InvalidateVisual();
                    e.Handled = true;
                    break;

                case Key.A:
                    if (keyArgs.IsControlDown)
                    {
                        // Select all - move caret to end
                        _caretPosition = Text.Length;
                        InvalidateVisual();
                        e.Handled = true;
                    }
                    break;

                case Key.C:
                    if (keyArgs.IsControlDown)
                    {
                        // Copy all text
                        if (!string.IsNullOrEmpty(Text))
                        {
                            Clipboard.SetText(Text);
                        }
                        e.Handled = true;
                    }
                    break;

                case Key.X:
                    if (keyArgs.IsControlDown)
                    {
                        // Cut all text
                        if (!string.IsNullOrEmpty(Text))
                        {
                            Clipboard.SetText(Text);
                            Text = string.Empty;
                            _caretPosition = 0;
                        }
                        e.Handled = true;
                    }
                    break;

                case Key.V:
                    if (keyArgs.IsControlDown)
                    {
                        Paste();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    private void OnTextInputHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is TextCompositionEventArgs textArgs && !string.IsNullOrEmpty(textArgs.Text))
        {
            var text = textArgs.Text;

            // Filter out control characters
            if (text.Length == 1 && char.IsControl(text[0]))
                return;

            InsertText(text);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Pastes text from the clipboard.
    /// </summary>
    public void Paste()
    {
        var clipboardText = Clipboard.GetText();
        if (!string.IsNullOrEmpty(clipboardText))
        {
            // Remove newlines for single-line control
            clipboardText = clipboardText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            InsertText(clipboardText);
        }
    }

    private void InsertText(string text)
    {
        var newText = Text.Insert(_caretPosition, text);
        Text = newText;
        _caretPosition += text.Length;
        ResetCaretBlink();
        EnsureCaretVisible();
    }

    private int GetCaretIndexFromPosition(Point position)
    {
        var padding = Padding;
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;

        var relativeX = position.X - padding.Left + _horizontalOffset;

        if (relativeX <= 0 || string.IsNullOrEmpty(Text))
            return 0;

        // Binary search for caret position
        var text = Text;
        for (int i = 0; i <= text.Length; i++)
        {
            var width = MeasureTextWidth(text.Substring(0, i));
            if (width >= relativeX)
            {
                // Check if closer to previous or current character
                if (i > 0)
                {
                    var prevWidth = MeasureTextWidth(text.Substring(0, i - 1));
                    if (relativeX - prevWidth < width - relativeX)
                        return i - 1;
                }
                return i;
            }
        }

        return text.Length;
    }

    private void EnsureCaretVisible()
    {
        var padding = Padding;
        var border = BorderThickness;
        var contentWidth = Math.Round(RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);

        if (contentWidth <= 0)
            return;

        var caretX = Math.Round(MeasureTextWidth(Text.Substring(0, _caretPosition)));

        // Horizontal scrolling
        if (caretX < _horizontalOffset)
        {
            _horizontalOffset = caretX;
        }
        else if (caretX > _horizontalOffset + contentWidth - 2)
        {
            _horizontalOffset = caretX - contentWidth + 2;
        }

        _horizontalOffset = Math.Round(Math.Max(0, _horizontalOffset));
    }

    private double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (_textWidthCache.TryGetValue(text, out var cachedWidth))
            return cachedWidth;

        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;

        var formattedText = new FormattedText(text, fontFamily, fontSize);
        TextMeasurement.MeasureText(formattedText);
        var width = formattedText.Width;

        // Cache management
        if (_textWidthCache.Count >= MaxCacheSize)
        {
            var keysToRemove = _textWidthCache.Keys.Take(MaxCacheSize / 2).ToList();
            foreach (var key in keysToRemove)
            {
                _textWidthCache.Remove(key);
            }
        }

        _textWidthCache[text] = width;
        return width;
    }

    #endregion

    #region IME Support

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

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        var padding = Padding;
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        var lineHeight = Math.Round(fontMetrics.LineHeight);

        var caretX = padding.Left + MeasureTextWidth(Text.Substring(0, _caretPosition)) - _horizontalOffset;
        var caretY = (DefaultHeight + lineHeight) / 2;

        return new Point(caretX, caretY);
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = string.Empty;
        _imeCursorPosition = 0;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCursorPosition = cursorPosition;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        // Result string is inserted via TextInput event
        InvalidateVisual();
    }

    /// <summary>
    /// Gets whether IME composition is active.
    /// </summary>
    public bool IsComposing => _isImeComposing;

    #endregion

    #region Caret Animation

    private void StartCaretTimer()
    {
        if (_caretTimer == null)
        {
            _caretTimer = new System.Timers.Timer(CaretTimerInterval);
            _caretTimer.Elapsed += OnCaretTimerElapsed;
            _caretTimer.AutoReset = true;
        }
        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        _caretTimer?.Stop();
    }

    private void OnCaretTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            InvalidateVisual();
        });
    }

    private void ResetCaretBlink()
    {
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;
    }

    private double UpdateCaretAnimation()
    {
        var now = DateTime.Now;
        var timeSinceReset = (now - _lastCaretBlink).TotalMilliseconds;
        var cycleTime = timeSinceReset % (CaretBlinkInterval * 2);

        if (cycleTime < CaretBlinkInterval - CaretFadeDuration)
        {
            _caretOpacity = 1.0;
        }
        else if (cycleTime < CaretBlinkInterval)
        {
            var fadeProgress = (cycleTime - (CaretBlinkInterval - CaretFadeDuration)) / CaretFadeDuration;
            _caretOpacity = 1.0 - fadeProgress;
        }
        else if (cycleTime < CaretBlinkInterval * 2 - CaretFadeDuration)
        {
            _caretOpacity = 0.0;
        }
        else
        {
            var fadeProgress = (cycleTime - (CaretBlinkInterval * 2 - CaretFadeDuration)) / CaretFadeDuration;
            _caretOpacity = fadeProgress;
        }

        return _caretOpacity;
    }

    #endregion

    #region Suggestion Handling

    private void UpdateFilteredItems()
    {
        if (_isUpdatingText) return;

        FilteredItems.Clear();
        _selectedSuggestionIndex = -1;

        if (ItemsSource == null || string.IsNullOrEmpty(Text) || Text.Length < MinimumPrefixLength)
        {
            IsDropDownOpen = false;
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
        }
        else
        {
            IsDropDownOpen = false;
        }
    }

    private bool MatchesFilter(object item)
    {
        var itemText = GetItemText(item);
        if (string.IsNullOrEmpty(itemText)) return false;

        var searchText = Text;

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
            AutoCompleteFilterMode.Custom => true, // Handled by ItemFilter
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
        Text = GetItemText(item);
        _caretPosition = Text.Length;
        _isUpdatingText = false;

        SelectedItem = item;
        IsDropDownOpen = false;

        var args = new SelectionChangedEventArgs(SelectionChangedEvent,
            oldItem != null ? new[] { oldItem } : Array.Empty<object>(),
            new[] { item });
        RaiseEvent(args);

        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
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

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var inputRect = new Rect(0, 0, RenderSize.Width, DefaultHeight);
        var cornerRadius = CornerRadius;
        var hasCornerRadius = cornerRadius.TopLeft > 0;
        var padding = Padding;
        var border = BorderThickness;

        // Draw background
        if (Background != null)
        {
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(Background, null, inputRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(Background, null, inputRect);
            }
        }

        // Draw border
        var borderBrush = IsFocused ? new SolidColorBrush(Color.FromRgb(0, 120, 212)) : BorderBrush;
        if (borderBrush != null && border.Left > 0)
        {
            var pen = new Pen(borderBrush, border.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, inputRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, inputRect);
            }
        }

        // Content area with clipping for horizontal scroll
        var contentRect = new Rect(
            Math.Round(border.Left + padding.Left),
            Math.Round(border.Top + padding.Top),
            Math.Max(0, Math.Round(inputRect.Width - border.Left - border.Right - padding.Left - padding.Right)),
            Math.Max(0, Math.Round(DefaultHeight - border.Top - border.Bottom - padding.Top - padding.Bottom)));

        dc.PushClip(new RectangleGeometry(contentRect));

        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        var lineHeight = Math.Round(fontMetrics.LineHeight);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        var textX = Math.Round(contentRect.X - roundedHorizontalOffset);
        var textY = Math.Round((DefaultHeight - lineHeight) / 2);

        // Draw text or watermark
        if (string.IsNullOrEmpty(Text) && !_isImeComposing && !string.IsNullOrEmpty(Watermark))
        {
            var watermarkText = new FormattedText(Watermark, fontFamily, fontSize)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
            TextMeasurement.MeasureText(watermarkText);
            dc.DrawText(watermarkText, new Point(contentRect.X, textY));
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            var formattedText = new FormattedText(Text, fontFamily, fontSize)
            {
                Foreground = Foreground ?? new SolidColorBrush(Color.White)
            };
            TextMeasurement.MeasureText(formattedText);
            dc.DrawText(formattedText, new Point(textX, textY));
        }

        // Draw IME composition
        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
        {
            DrawImeComposition(dc, contentRect, lineHeight);
        }

        // Draw caret
        if (IsFocused)
        {
            DrawCaret(dc, contentRect, lineHeight);
        }

        dc.Pop();

        // Draw drop-down
        if (IsDropDownOpen && FilteredItems.Count > 0)
        {
            DrawDropDown(dc);
        }
    }

    private void DrawImeComposition(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        var x = Math.Round(contentRect.X + MeasureTextWidth(Text.Substring(0, _caretPosition)) - roundedHorizontalOffset);
        var y = Math.Round((DefaultHeight - lineHeight) / 2);

        // Draw composition background
        var compositionText = new FormattedText(_imeCompositionString, fontFamily, fontSize);
        TextMeasurement.MeasureText(compositionText);
        var compositionWidth = compositionText.Width;

        var compositionBgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80));
        dc.DrawRectangle(compositionBgBrush, null, new Rect(x, y, compositionWidth, lineHeight));

        // Draw composition text
        compositionText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 150));
        dc.DrawText(compositionText, new Point(x, y));

        // Draw underline
        var underlinePen = new Pen(new SolidColorBrush(Color.FromRgb(255, 255, 150)), 1);
        dc.DrawLine(underlinePen, new Point(x, y + lineHeight - 1), new Point(x + compositionWidth, y + lineHeight - 1));
    }

    private void DrawCaret(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var caretOpacity = UpdateCaretAnimation();

        if (caretOpacity <= 0)
            return;

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var caretX = Math.Round(contentRect.X + MeasureTextWidth(Text.Substring(0, _caretPosition)) - roundedHorizontalOffset);
        var caretY = Math.Round((DefaultHeight - lineHeight) / 2);

        // If IME composing, position caret after composition
        if (_isImeComposing)
        {
            caretX += MeasureTextWidth(_imeCompositionString.Substring(0, _imeCursorPosition));
        }

        // Create caret brush with opacity
        var caretColor = Color.White;
        if (Foreground is SolidColorBrush solidBrush)
        {
            caretColor = solidBrush.Color;
        }
        var alpha = (byte)(caretColor.A * caretOpacity);
        var caretBrush = new SolidColorBrush(Color.FromArgb(alpha, caretColor.R, caretColor.G, caretColor.B));

        var caretPen = new Pen(caretBrush, 1);
        dc.DrawLine(caretPen, new Point(caretX, caretY), new Point(caretX, caretY + lineHeight));
    }

    private void DrawDropDown(DrawingContext dc)
    {
        var dropDownTop = DefaultHeight;
        var dropDownHeight = Math.Min(FilteredItems.Count * ItemHeight, MaxDropDownHeight);
        var dropDownRect = new Rect(0, dropDownTop, RenderSize.Width, dropDownHeight);

        // Draw drop-down background
        var dropDownBg = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        dc.DrawRectangle(dropDownBg, new Pen(BorderBrush ?? new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1), dropDownRect);

        // Draw items
        var y = dropDownTop;
        var selectionBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        var hoverBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));

        for (var i = 0; i < FilteredItems.Count && y < dropDownTop + dropDownHeight; i++)
        {
            var itemRect = new Rect(1, y, RenderSize.Width - 2, ItemHeight);

            // Draw selection/hover background
            if (i == _selectedSuggestionIndex)
            {
                dc.DrawRectangle(selectionBrush, null, itemRect);
            }

            // Draw item text
            var itemText = GetItemText(FilteredItems[i]);
            var formattedText = new FormattedText(itemText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 12)
            {
                Foreground = Foreground ?? new SolidColorBrush(Color.White)
            };
            TextMeasurement.MeasureText(formattedText);
            dc.DrawText(formattedText, new Point(Padding.Left, y + (ItemHeight - formattedText.Height) / 2));

            y += ItemHeight;
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            autoComplete.UpdateFilteredItems();
            autoComplete.RaiseEvent(new RoutedEventArgs(TextChangedEvent, autoComplete));
            autoComplete.InvalidateVisual();
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
                autoComplete.RaiseEvent(new RoutedEventArgs(DropDownOpenedEvent, autoComplete));
            }
            else
            {
                autoComplete.RaiseEvent(new RoutedEventArgs(DropDownClosedEvent, autoComplete));
            }
            autoComplete.InvalidateMeasure();
            autoComplete.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AutoCompleteBox autoComplete)
        {
            autoComplete.InvalidateVisual();
        }
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
