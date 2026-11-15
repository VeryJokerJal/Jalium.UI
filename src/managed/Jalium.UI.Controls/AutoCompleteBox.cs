using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>A text editor with a replaceable, templated suggestion list.</summary>
[TemplatePart(Name = "PART_ContentHost", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_SuggestionsList", Type = typeof(ListBox))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "MouseOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "DropDownOpen", GroupName = "DropDownStates")]
[TemplateVisualState(Name = "DropDownClosed", GroupName = "DropDownStates")]
public class AutoCompleteBox : TextBox
{
    private readonly SuggestionCollection _filteredItems = new();
    private Popup? _popup;
    private ListBox? _suggestionsList;
    private DispatcherTimer? _filterDelayTimer;
    private bool _acceptingSuggestion;
    private bool _updatingSuggestions;
    private bool _syncingPopup;
    private bool _insertingText;
    private bool _suppressNextTabTextInput;
    private int _filterRevision;
    private int _pressedSuggestionIndex = -1;
    private int _touchId = -1;
    private Point _touchStart;
    private bool _touchCandidate;

    static AutoCompleteBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AutoCompleteBox), new PropertyMetadata(typeof(AutoCompleteBox)));
        TextWrappingProperty.OverrideMetadata(typeof(AutoCompleteBox), new FrameworkPropertyMetadata(TextWrapping.NoWrap));
    }

    // Keep the existing public identifiers while using the TextBox editing engine.
    public new static readonly DependencyProperty TextProperty = TextBox.TextProperty;
    public new static readonly DependencyProperty PlaceholderTextProperty = TextBox.PlaceholderTextProperty;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(AutoCompleteBox), new PropertyMetadata(null, OnItemsSourceChanged));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(AutoCompleteBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(AutoCompleteBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));
    public static readonly DependencyProperty MinimumPrefixLengthProperty = DependencyProperty.Register(
        nameof(MinimumPrefixLength), typeof(int), typeof(AutoCompleteBox), new PropertyMetadata(1, OnFilterOptionsChanged));
    public static readonly DependencyProperty FilterModeProperty = DependencyProperty.Register(
        nameof(FilterMode), typeof(AutoCompleteFilterMode), typeof(AutoCompleteBox),
        new PropertyMetadata(AutoCompleteFilterMode.StartsWith, OnFilterOptionsChanged));
    public static readonly DependencyProperty MaxDropDownHeightProperty = DependencyProperty.Register(
        nameof(MaxDropDownHeight), typeof(double), typeof(AutoCompleteBox), new PropertyMetadata(224.0));
    public static readonly DependencyProperty MinimumPopulateDelayProperty = DependencyProperty.Register(
        nameof(MinimumPopulateDelay), typeof(TimeSpan), typeof(AutoCompleteBox), new PropertyMetadata(TimeSpan.Zero, OnFilterOptionsChanged));
    public static readonly DependencyProperty TextMemberPathProperty = DependencyProperty.Register(
        nameof(TextMemberPath), typeof(string), typeof(AutoCompleteBox), new PropertyMetadata(null, OnFilterOptionsChanged));
    public static readonly DependencyProperty IsTextCompletionEnabledProperty = DependencyProperty.Register(
        nameof(IsTextCompletionEnabled), typeof(bool), typeof(AutoCompleteBox), new PropertyMetadata(false, OnFilterOptionsChanged));
    public static readonly DependencyProperty ItemFilterProperty = DependencyProperty.Register(
        nameof(ItemFilter), typeof(Func<string, object, bool>), typeof(AutoCompleteBox), new PropertyMetadata(null, OnFilterOptionsChanged));
    public static readonly DependencyProperty SuggestionItemTemplateProperty = DependencyProperty.Register(
        nameof(SuggestionItemTemplate), typeof(DataTemplate), typeof(AutoCompleteBox), new PropertyMetadata(null));
    public static readonly DependencyProperty SuggestionItemTemplateSelectorProperty = DependencyProperty.Register(
        nameof(SuggestionItemTemplateSelector), typeof(DataTemplateSelector), typeof(AutoCompleteBox), new PropertyMetadata(null));
    public static readonly DependencyProperty SuggestionItemContainerStyleProperty = DependencyProperty.Register(
        nameof(SuggestionItemContainerStyle), typeof(Style), typeof(AutoCompleteBox), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey FilteredItemsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(FilteredItems), typeof(ObservableCollection<object>), typeof(AutoCompleteBox), new PropertyMetadata(null));
    public static readonly DependencyProperty FilteredItemsProperty = FilteredItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey SelectedSuggestionIndexPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedSuggestionIndex), typeof(int), typeof(AutoCompleteBox), new PropertyMetadata(-1));
    public static readonly DependencyProperty SelectedSuggestionIndexProperty = SelectedSuggestionIndexPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasSuggestionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasSuggestions), typeof(bool), typeof(AutoCompleteBox), new PropertyMetadata(false));
    public static readonly DependencyProperty HasSuggestionsProperty = HasSuggestionsPropertyKey.DependencyProperty;

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty)!; set => SetValue(IsDropDownOpenProperty, value); }
    public int MinimumPrefixLength { get => (int)GetValue(MinimumPrefixLengthProperty)!; set => SetValue(MinimumPrefixLengthProperty, value); }
    public AutoCompleteFilterMode FilterMode { get => (AutoCompleteFilterMode)GetValue(FilterModeProperty)!; set => SetValue(FilterModeProperty, value); }
    public double MaxDropDownHeight { get => (double)GetValue(MaxDropDownHeightProperty)!; set => SetValue(MaxDropDownHeightProperty, value); }
    public TimeSpan MinimumPopulateDelay { get => (TimeSpan)GetValue(MinimumPopulateDelayProperty)!; set => SetValue(MinimumPopulateDelayProperty, value); }
    public string? TextMemberPath { get => (string?)GetValue(TextMemberPathProperty); set => SetValue(TextMemberPathProperty, value); }
    public bool IsTextCompletionEnabled { get => (bool)GetValue(IsTextCompletionEnabledProperty)!; set => SetValue(IsTextCompletionEnabledProperty, value); }
    public Func<string, object, bool>? ItemFilter { get => (Func<string, object, bool>?)GetValue(ItemFilterProperty); set => SetValue(ItemFilterProperty, value); }
    public DataTemplate? SuggestionItemTemplate { get => (DataTemplate?)GetValue(SuggestionItemTemplateProperty); set => SetValue(SuggestionItemTemplateProperty, value); }
    public DataTemplateSelector? SuggestionItemTemplateSelector { get => (DataTemplateSelector?)GetValue(SuggestionItemTemplateSelectorProperty); set => SetValue(SuggestionItemTemplateSelectorProperty, value); }
    public Style? SuggestionItemContainerStyle { get => (Style?)GetValue(SuggestionItemContainerStyleProperty); set => SetValue(SuggestionItemContainerStyleProperty, value); }
    public ObservableCollection<object> FilteredItems => _filteredItems;
    public int SelectedSuggestionIndex => (int)GetValue(SelectedSuggestionIndexProperty)!;
    public bool HasSuggestions => (bool)GetValue(HasSuggestionsProperty)!;

    // Preserve the AutoCompleteBox events independently from the inherited text-selection event.
    public new static readonly RoutedEvent TextChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(TextChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AutoCompleteBox));
    public new static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(EventHandler<SelectionChangedEventArgs>), typeof(AutoCompleteBox));
    public static readonly RoutedEvent DropDownOpenedEvent = EventManager.RegisterRoutedEvent(
        nameof(DropDownOpened), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AutoCompleteBox));
    public static readonly RoutedEvent DropDownClosedEvent = EventManager.RegisterRoutedEvent(
        nameof(DropDownClosed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AutoCompleteBox));
    public static readonly RoutedEvent PopulatingEvent = EventManager.RegisterRoutedEvent(
        nameof(Populating), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AutoCompleteBox));
    public new event RoutedEventHandler TextChanged { add => AddHandler(TextChangedEvent, value); remove => RemoveHandler(TextChangedEvent, value); }
    public new event EventHandler<SelectionChangedEventArgs> SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }
    public event RoutedEventHandler DropDownOpened { add => AddHandler(DropDownOpenedEvent, value); remove => RemoveHandler(DropDownOpenedEvent, value); }
    public event RoutedEventHandler DropDownClosed { add => AddHandler(DropDownClosedEvent, value); remove => RemoveHandler(DropDownClosedEvent, value); }
    public event RoutedEventHandler Populating { add => AddHandler(PopulatingEvent, value); remove => RemoveHandler(PopulatingEvent, value); }

    public AutoCompleteBox()
    {
        SetValue(FilteredItemsPropertyKey, _filteredItems);
        _filteredItems.CollectionChanged += OnFilteredItemsChanged;
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocusHandler));
        SizeChanged += (_, _) => UpdatePopupPlacementAndWidth();
        Unloaded += OnUnloaded;
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.AutoCompleteBoxAutomationPeer(this);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _suggestionsList = GetTemplateChild("PART_SuggestionsList") as ListBox;
        if (_popup != null)
        {
            _popup.PlacementTarget = this;
            _popup.Closed += OnPopupClosed;
        }
        if (_suggestionsList != null)
        {
            _suggestionsList.SelectionChanged += OnSuggestionsSelectionChanged;
            _suggestionsList.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnSuggestionMouseDown), true);
            _suggestionsList.AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnSuggestionMouseUp), true);
            _suggestionsList.AddHandler(PreviewTouchDownEvent, new RoutedEventHandler(OnSuggestionTouchDown), true);
            _suggestionsList.AddHandler(PreviewTouchMoveEvent, new RoutedEventHandler(OnSuggestionTouchMove), true);
            _suggestionsList.AddHandler(PreviewTouchUpEvent, new RoutedEventHandler(OnSuggestionTouchUp), true);
        }
        UpdatePopupPlacementAndWidth();
        SyncSuggestionSelection();
        SyncPopupOpenState();
        UpdateVisualStates();
    }

    protected override void OnTemplateChanging()
    {
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _syncingPopup = true;
            try { _popup.SetCurrentValue(Popup.IsOpenProperty, false); }
            finally { _syncingPopup = false; }
        }
        if (_suggestionsList != null)
        {
            _suggestionsList.SelectionChanged -= OnSuggestionsSelectionChanged;
            _suggestionsList.RemoveHandler(PreviewMouseDownEvent, new MouseButtonEventHandler(OnSuggestionMouseDown));
            _suggestionsList.RemoveHandler(MouseUpEvent, new MouseButtonEventHandler(OnSuggestionMouseUp));
            _suggestionsList.RemoveHandler(PreviewTouchDownEvent, new RoutedEventHandler(OnSuggestionTouchDown));
            _suggestionsList.RemoveHandler(PreviewTouchMoveEvent, new RoutedEventHandler(OnSuggestionTouchMove));
            _suggestionsList.RemoveHandler(PreviewTouchUpEvent, new RoutedEventHandler(OnSuggestionTouchUp));
            BindingOperations.ClearBinding(_suggestionsList, ItemsControl.ItemsSourceProperty);
            _suggestionsList.SetCurrentValue(ItemsControl.ItemsSourceProperty, null);
        }
        _suggestionsList = null;
        _popup = null;
        _pressedSuggestionIndex = -1;
        _touchCandidate = false;
        base.OnTemplateChanging();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsEnabledProperty || e.Property == IsReadOnlyProperty || e.Property == IsMouseOverProperty || e.Property == IsKeyboardFocusWithinProperty)
        {
            if (!IsEnabled || IsReadOnly) SetCurrentValue(IsDropDownOpenProperty, false);
            UpdateVisualStates();
        }
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (!_acceptingSuggestion)
        {
            if (SelectedItem != null && !string.Equals(GetItemText(SelectedItem), Text, StringComparison.Ordinal))
                SetCurrentValue(SelectedItemProperty, null);
            TriggerFilterUpdate();
        }
        RaiseEvent(new RoutedEventArgs(TextChangedEvent, this));
    }

    protected override void InsertText(string textToInsert)
    {
        var suppressTab = _suppressNextTabTextInput;
        _suppressNextTabTextInput = false;
        if (suppressTab) textToInsert = textToInsert.Replace("\t", string.Empty);
        _insertingText = true;
        try { base.InsertText(textToInsert); }
        finally { _insertingText = false; }
        if (MinimumPopulateDelay <= TimeSpan.Zero) CompleteFirstMatch();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Handled || !IsEnabled) return;
        if (IsImeComposing) { base.OnKeyDown(e); return; }
        switch (e.Key)
        {
            case Key.Down when HasSuggestions:
                if (!IsDropDownOpen) { SetCurrentValue(IsDropDownOpenProperty, true); SetSuggestionIndex(0); }
                else SetSuggestionIndex(Math.Min(SelectedSuggestionIndex + 1, _filteredItems.Count - 1));
                e.Handled = true;
                return;
            case Key.Up when IsDropDownOpen && HasSuggestions:
                SetSuggestionIndex(Math.Max(0, SelectedSuggestionIndex - 1));
                e.Handled = true;
                return;
            case Key.Enter when IsDropDownOpen && SelectedSuggestionIndex >= 0:
                AcceptSuggestion(SelectedSuggestionIndex, false);
                e.Handled = true;
                return;
            case Key.Tab when IsDropDownOpen && SelectedSuggestionIndex >= 0:
                AcceptSuggestion(SelectedSuggestionIndex, false);
                _suppressNextTabTextInput = true;
                e.Handled = true;
                return;
            case Key.Escape when IsDropDownOpen:
                SetCurrentValue(IsDropDownOpenProperty, false);
                e.Handled = true;
                return;
        }
        base.OnKeyDown(e);
    }

    private void TriggerFilterUpdate()
    {
        _filterRevision++;
        _filterDelayTimer?.Stop();
        if (MinimumPopulateDelay <= TimeSpan.Zero) { UpdateFilteredItems(); return; }
        if (_filterDelayTimer == null)
        {
            _filterDelayTimer = new DispatcherTimer();
            _filterDelayTimer.Tick += OnFilterDelayTick;
        }
        _filterDelayTimer.Interval = MinimumPopulateDelay;
        _filterDelayTimer.Start();
    }
    private void OnFilterDelayTick(object? sender, EventArgs e)
    {
        _filterDelayTimer?.Stop();
        UpdateFilteredItems();
    }

    private void UpdateFilteredItems()
    {
        if (_acceptingSuggestion) return;
        var revision = ++_filterRevision;
        RaiseEvent(new RoutedEventArgs(PopulatingEvent, this));
        if (revision != _filterRevision) return;
        var text = Text;
        var items = new List<object>();
        if (ItemsSource != null && text.Length >= Math.Max(0, MinimumPrefixLength))
        {
            foreach (var item in ItemsSource)
            {
                if (item != null && MatchesFilter(text, item)) items.Add(item);
                if (revision != _filterRevision) return;
            }
        }
        _updatingSuggestions = true;
        try { _filteredItems.Replace(items); }
        finally { _updatingSuggestions = false; }
        SetValue(HasSuggestionsPropertyKey, _filteredItems.Count > 0);
        SetSuggestionIndex(HasSuggestions ? 0 : -1);
        SetCurrentValue(IsDropDownOpenProperty, IsEnabled && !IsReadOnly && HasSuggestions);
        SyncPopupOpenState();
        UpdateVisualStates();
        if (!_insertingText) CompleteFirstMatch();
    }

    private bool MatchesFilter(string text, object item)
    {
        if (ItemFilter is { } filter) return filter(text, item);
        var itemText = GetItemText(item);
        return FilterMode switch
        {
            AutoCompleteFilterMode.StartsWith => itemText.StartsWith(text, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.StartsWithCaseSensitive => itemText.StartsWith(text, StringComparison.Ordinal),
            AutoCompleteFilterMode.Contains => itemText.Contains(text, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.ContainsCaseSensitive => itemText.Contains(text, StringComparison.Ordinal),
            AutoCompleteFilterMode.Equals => itemText.Equals(text, StringComparison.OrdinalIgnoreCase),
            AutoCompleteFilterMode.EqualsCaseSensitive => itemText.Equals(text, StringComparison.Ordinal),
            _ => true,
        };
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "TextMemberPath is a consumer-provided binding path; applications must preserve the referenced item properties under trimming.")]
    private string GetItemText(object item)
    {
        if (string.IsNullOrEmpty(TextMemberPath)) return item.ToString() ?? string.Empty;
        return item.GetType().GetProperty(TextMemberPath)?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }

    private void CompleteFirstMatch()
    {
        if (!IsTextCompletionEnabled || IsReadOnly || !IsDropDownOpen || _filteredItems.Count == 0 || _acceptingSuggestion) return;
        var prefix = Text;
        var completed = GetItemText(_filteredItems[0]);
        if (!completed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || completed.Length <= prefix.Length) return;
        _acceptingSuggestion = true;
        try { SetCurrentValue(TextProperty, completed); Select(prefix.Length, completed.Length - prefix.Length); }
        finally { _acceptingSuggestion = false; }
    }

    private void AcceptSuggestion(int index, bool restoreFocus)
    {
        if (IsReadOnly || !IsEnabled || index < 0 || index >= _filteredItems.Count) return;
        var item = _filteredItems[index];
        _filterDelayTimer?.Stop();
        _filterRevision++;
        _acceptingSuggestion = true;
        try
        {
            SetCurrentValue(TextProperty, GetItemText(item));
            CaretIndex = Text.Length;
            Select(CaretIndex, 0);
            SetCurrentValue(SelectedItemProperty, item);
        }
        finally { _acceptingSuggestion = false; }
        SetCurrentValue(IsDropDownOpenProperty, false);
        if (restoreFocus) Focus();
    }

    private void OnFilteredItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_updatingSuggestions) return;
        SetValue(HasSuggestionsPropertyKey, _filteredItems.Count > 0);
        SetSuggestionIndex(HasSuggestions ? Math.Clamp(SelectedSuggestionIndex, 0, _filteredItems.Count - 1) : -1);
        if (!HasSuggestions) SetCurrentValue(IsDropDownOpenProperty, false);
        SyncPopupOpenState();
    }
    private void SetSuggestionIndex(int index)
    {
        SetValue(SelectedSuggestionIndexPropertyKey, index);
        SyncSuggestionSelection();
    }
    private void SyncSuggestionSelection()
    {
        if (_suggestionsList == null) return;
        _updatingSuggestions = true;
        try { _suggestionsList.SetCurrentValue(Selector.SelectedIndexProperty, SelectedSuggestionIndex); }
        finally { _updatingSuggestions = false; }
        (_suggestionsList.ItemContainerGenerator.ContainerFromIndex(SelectedSuggestionIndex) as FrameworkElement)?.BringIntoView();
    }
    private void OnSuggestionsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingSuggestions && _suggestionsList != null) SetSuggestionIndex(_suggestionsList.SelectedIndex);
    }
    private int SuggestionIndex(object? source)
    {
        for (var visual = source as Visual; visual != null && !ReferenceEquals(visual, _suggestionsList); visual = visual.VisualParent)
            if (visual is ListBoxItem item) return _suggestionsList?.ItemContainerGenerator.IndexFromContainer(item) ?? -1;
        return -1;
    }
    private void OnSuggestionMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedSuggestionIndex = e.ChangedButton == MouseButton.Left ? SuggestionIndex(e.OriginalSource ?? e.Source) : -1;
    }
    private void OnSuggestionMouseUp(object sender, MouseButtonEventArgs e)
    {
        var index = SuggestionIndex(e.OriginalSource ?? e.Source);
        if (e.ChangedButton == MouseButton.Left && index >= 0 && index == _pressedSuggestionIndex)
        {
            AcceptSuggestion(index, true);
            e.Handled = true;
        }
        _pressedSuggestionIndex = -1;
    }
    private void OnSuggestionTouchDown(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touch) return;
        _touchId = touch.TouchDevice.Id;
        _touchStart = touch.GetTouchPoint(_suggestionsList).Position;
        _touchCandidate = SuggestionIndex(e.OriginalSource ?? e.Source) >= 0;
    }
    private void OnSuggestionTouchMove(object sender, RoutedEventArgs e)
    {
        if (!_touchCandidate || e is not TouchEventArgs touch || touch.TouchDevice.Id != _touchId) return;
        var position = touch.GetTouchPoint(_suggestionsList).Position;
        if (Math.Abs(position.X - _touchStart.X) > 8 || Math.Abs(position.Y - _touchStart.Y) > 8) _touchCandidate = false;
    }
    private void OnSuggestionTouchUp(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touch || touch.TouchDevice.Id != _touchId) return;
        var candidate = _touchCandidate;
        _touchCandidate = false;
        _touchId = -1;
        var index = SuggestionIndex(e.OriginalSource ?? e.Source);
        if (candidate && index >= 0) { AcceptSuggestion(index, true); e.Handled = true; }
    }

    private void UpdatePopupPlacementAndWidth()
    {
        if (_popup == null) return;
        _popup.PlacementTarget = this;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        if (width > 0 && double.IsFinite(width))
        {
            _popup.Width = width;
            _popup.MinWidth = width;
            _popup.MaxWidth = width;
        }
    }
    private void SyncPopupOpenState()
    {
        if (_popup == null) return;
        _syncingPopup = true;
        try { _popup.SetCurrentValue(Popup.IsOpenProperty, IsEnabled && IsDropDownOpen && HasSuggestions); }
        finally { _syncingPopup = false; }
    }
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (!_syncingPopup) SetCurrentValue(IsDropDownOpenProperty, false);
    }
    private void OnGotFocusHandler(object sender, KeyboardFocusChangedEventArgs e) => UpdateVisualStates();
    private void OnLostFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_popup == null || !_popup.IsMouseOver) SetCurrentValue(IsDropDownOpenProperty, false);
        UpdateVisualStates();
    }
    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _filterDelayTimer?.Stop();
        _filterRevision++;
        SetCurrentValue(IsDropDownOpenProperty, false);
    }
    private void UpdateVisualStates()
    {
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "MouseOver" : "Normal", true);
        VisualStateManager.GoToState(this, IsEnabled && IsKeyboardFocusWithin ? "Focused" : "Unfocused", true);
        VisualStateManager.GoToState(this, IsDropDownOpen && HasSuggestions ? "DropDownOpen" : "DropDownClosed", true);
    }
    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var owner = (AutoCompleteBox)d;
        if (e.OldValue is INotifyCollectionChanged oldSource) CollectionChangedEventManager.RemoveHandler(oldSource, owner.OnSourceChanged);
        if (e.NewValue is INotifyCollectionChanged newSource) CollectionChangedEventManager.AddHandler(newSource, owner.OnSourceChanged);
        owner.TriggerFilterUpdate();
    }
    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => TriggerFilterUpdate();
    private static void OnFilterOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((AutoCompleteBox)d).TriggerFilterUpdate();
    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var owner = (AutoCompleteBox)d;
        if (!owner._acceptingSuggestion && e.NewValue != null)
        {
            owner._acceptingSuggestion = true;
            try { owner.SetCurrentValue(TextProperty, owner.GetItemText(e.NewValue)); owner.CaretIndex = owner.Text.Length; }
            finally { owner._acceptingSuggestion = false; }
            owner.SetCurrentValue(IsDropDownOpenProperty, false);
        }
        owner.RaiseEvent(new SelectionChangedEventArgs(SelectionChangedEvent,
            e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(),
            e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>()));
    }
    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var owner = (AutoCompleteBox)d;
        owner.UpdatePopupPlacementAndWidth();
        owner.SyncPopupOpenState();
        owner.UpdateVisualStates();
        owner.RaiseEvent(new RoutedEventArgs((bool)e.NewValue! ? DropDownOpenedEvent : DropDownClosedEvent, owner));
    }

    private sealed class SuggestionCollection : ObservableCollection<object>
    {
        internal void Replace(IReadOnlyList<object> items)
        {
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
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
