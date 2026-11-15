using System.Globalization;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>A templated time selector with a pending selection that is committed only on acceptance.</summary>
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_DisplayText", Type = typeof(TextBlock))]
[TemplatePart(Name = "PART_HourList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_MinuteList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_PeriodList", Type = typeof(ListBox))]
[TemplatePart(Name = "PART_AcceptButton", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_CancelButton", Type = typeof(ButtonBase))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "PointerOver", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Placeholder", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "Selected", GroupName = "SelectionStates")]
[TemplateVisualState(Name = "HeaderVisible", GroupName = "HeaderStates")]
[TemplateVisualState(Name = "HeaderCollapsed", GroupName = "HeaderStates")]
[TemplateVisualState(Name = "TwelveHourClock", GroupName = "ClockStates")]
[TemplateVisualState(Name = "TwentyFourHourClock", GroupName = "ClockStates")]
[TemplateVisualState(Name = "Open", GroupName = "DropDownStates")]
[TemplateVisualState(Name = "Closed", GroupName = "DropDownStates")]
public class TimePicker : Control
{
    public static readonly DependencyProperty SelectedTimeProperty = DependencyProperty.Register(
        nameof(SelectedTime), typeof(TimeSpan?), typeof(TimePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedTimeChanged));
    public static readonly DependencyProperty ClockIdentifierProperty = DependencyProperty.Register(
        nameof(ClockIdentifier), typeof(string), typeof(TimePicker), new PropertyMetadata("12HourClock", OnOptionsChanged));
    public static readonly DependencyProperty MinuteIncrementProperty = DependencyProperty.Register(
        nameof(MinuteIncrement), typeof(int), typeof(TimePicker), new PropertyMetadata(1, OnOptionsChanged,
            (_, value) => Math.Clamp(value is int increment ? increment : 1, 1, 59)));
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(object), typeof(TimePicker), new PropertyMetadata(null, OnDisplayChanged));
    public static readonly DependencyProperty HeaderTemplateProperty = DependencyProperty.Register(
        nameof(HeaderTemplate), typeof(DataTemplate), typeof(TimePicker), new PropertyMetadata(null));
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(TimePicker), new PropertyMetadata("Select a time", OnDisplayChanged));
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(
        nameof(IsDropDownOpen), typeof(bool), typeof(TimePicker),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsDropDownOpenChanged));
    public static readonly DependencyProperty PendingHourIndexProperty = DependencyProperty.Register(
        nameof(PendingHourIndex), typeof(int), typeof(TimePicker), new PropertyMetadata(0, OnPendingIndexChanged));
    public static readonly DependencyProperty PendingMinuteIndexProperty = DependencyProperty.Register(
        nameof(PendingMinuteIndex), typeof(int), typeof(TimePicker), new PropertyMetadata(0, OnPendingIndexChanged));
    public static readonly DependencyProperty PendingPeriodIndexProperty = DependencyProperty.Register(
        nameof(PendingPeriodIndex), typeof(int), typeof(TimePicker), new PropertyMetadata(0, OnPendingIndexChanged));
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(TimePicker), new PropertyMetadata("Select a time"));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HourOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HourOptions), typeof(IReadOnlyList<string>), typeof(TimePicker), new PropertyMetadata(Array.Empty<string>()));
    public static readonly DependencyProperty HourOptionsProperty = HourOptionsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey MinuteOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(MinuteOptions), typeof(IReadOnlyList<string>), typeof(TimePicker), new PropertyMetadata(Array.Empty<string>()));
    public static readonly DependencyProperty MinuteOptionsProperty = MinuteOptionsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PeriodOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PeriodOptions), typeof(IReadOnlyList<string>), typeof(TimePicker), new PropertyMetadata(new[] { "AM", "PM" }));
    public static readonly DependencyProperty PeriodOptionsProperty = PeriodOptionsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PendingTimePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PendingTime), typeof(TimeSpan), typeof(TimePicker), new PropertyMetadata(TimeSpan.Zero));
    public static readonly DependencyProperty PendingTimeProperty = PendingTimePropertyKey.DependencyProperty;

    public static readonly RoutedEvent SelectedTimeChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectedTimeChanged), RoutingStrategy.Bubble, typeof(EventHandler<TimePickerSelectedValueChangedEventArgs>), typeof(TimePicker));
    public event EventHandler<TimePickerSelectedValueChangedEventArgs> SelectedTimeChanged
    {
        add => AddHandler(SelectedTimeChangedEvent, value);
        remove => RemoveHandler(SelectedTimeChangedEvent, value);
    }

    public TimeSpan? SelectedTime { get => (TimeSpan?)GetValue(SelectedTimeProperty); set => SetValue(SelectedTimeProperty, value); }
    public string ClockIdentifier { get => (string?)GetValue(ClockIdentifierProperty) ?? "12HourClock"; set => SetValue(ClockIdentifierProperty, value); }
    public int MinuteIncrement { get => (int)GetValue(MinuteIncrementProperty)!; set => SetValue(MinuteIncrementProperty, value); }
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public DataTemplate? HeaderTemplate { get => (DataTemplate?)GetValue(HeaderTemplateProperty); set => SetValue(HeaderTemplateProperty, value); }
    public string? PlaceholderText { get => (string?)GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty)!; set => SetValue(IsDropDownOpenProperty, value); }
    public int PendingHourIndex { get => (int)GetValue(PendingHourIndexProperty)!; set => SetValue(PendingHourIndexProperty, value); }
    public int PendingMinuteIndex { get => (int)GetValue(PendingMinuteIndexProperty)!; set => SetValue(PendingMinuteIndexProperty, value); }
    public int PendingPeriodIndex { get => (int)GetValue(PendingPeriodIndexProperty)!; set => SetValue(PendingPeriodIndexProperty, value); }
    public string DisplayText => (string)GetValue(DisplayTextProperty)!;
    public IReadOnlyList<string> HourOptions => (IReadOnlyList<string>)GetValue(HourOptionsProperty)!;
    public IReadOnlyList<string> MinuteOptions => (IReadOnlyList<string>)GetValue(MinuteOptionsProperty)!;
    public IReadOnlyList<string> PeriodOptions => (IReadOnlyList<string>)GetValue(PeriodOptionsProperty)!;
    public TimeSpan PendingTime => (TimeSpan)GetValue(PendingTimeProperty)!;

    private Popup? _popup;
    private UIElement? _popupChild;
    private TextBlock? _displayText;
    private ListBox? _hours, _minutes, _periods;
    private ButtonBase? _accept, _cancel;
    private TimeSpan? _pendingOnRetemplate;
    private Jalium.UI.Threading.DispatcherOperation? _pendingOpenLayout;
    private bool _updatingPending, _hasPendingSelection, _changingTemplate;
    private bool Is24Hour => ClockIdentifier == "24HourClock";

    public TimePicker()
    {
        Focusable = true;
        SetCurrentValue(TransitionPropertyProperty, TransitionPropertyCollection.None());
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Unloaded += (_, _) => SetCurrentValue(IsDropDownOpenProperty, false);
        RebuildOptions();
        UpdateDisplay();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _displayText = GetTemplateChild("PART_DisplayText") as TextBlock;
        _hours = GetTemplateChild("PART_HourList") as ListBox;
        _minutes = GetTemplateChild("PART_MinuteList") as ListBox;
        _periods = GetTemplateChild("PART_PeriodList") as ListBox;
        _accept = GetTemplateChild("PART_AcceptButton") as ButtonBase;
        _cancel = GetTemplateChild("PART_CancelButton") as ButtonBase;
        CssPlaceholder.Bind(_displayText, this);
        if (_popup != null)
        {
            _popup.SetCurrentValue(Popup.PlacementTargetProperty, this);
            _popup.Closed += OnPopupClosed;
            _popupChild = _popup.Child;
            _popupChild?.AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        }
        if (_accept != null) _accept.Click += OnAccept;
        if (_cancel != null) _cancel.Click += OnCancel;
        if (_pendingOnRetemplate is { } pending)
        {
            SetPendingTime(pending);
            _pendingOnRetemplate = null;
        }
        UpdateVisualStates();
        if (IsDropDownOpen) OpenDropDown();
    }

    protected override void OnTemplateChanging()
    {
        _changingTemplate = true;
        CancelPendingOpenLayout();
        if (_hasPendingSelection && !_pendingOnRetemplate.HasValue) _pendingOnRetemplate = PendingTime;
        // A retiring selector can clear its selection while its item containers
        // detach. Disconnect its TwoWay editor binding before that reset occurs.
        _hours?.ClearBinding(Selector.SelectedIndexProperty);
        _minutes?.ClearBinding(Selector.SelectedIndexProperty);
        _periods?.ClearBinding(Selector.SelectedIndexProperty);
        _popupChild?.RemoveHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        if (_popup != null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.SetCurrentValue(Popup.IsOpenProperty, false);
        }
        if (_accept != null) _accept.Click -= OnAccept;
        if (_cancel != null) _cancel.Click -= OnCancel;
        CssPlaceholder.Unbind(_displayText);
        _popup = null; _popupChild = null; _displayText = null; _hours = _minutes = _periods = null; _accept = _cancel = null;
        _changingTemplate = false;
        base.OnTemplateChanging();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue) SetCurrentValue(IsDropDownOpenProperty, false);
        UpdateVisualStates();
    }
    protected override void OnIsMouseOverChanged(bool oldValue, bool newValue) { base.OnIsMouseOverChanged(oldValue, newValue); UpdateVisualStates(); }
    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); UpdateVisualStates(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); UpdateVisualStates(); }
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.TimePickerAutomationPeer(this);

    private void OpenDropDown()
    {
        if (!IsEnabled) { SetCurrentValue(IsDropDownOpenProperty, false); return; }
        if (!_hasPendingSelection) { SetPendingTime(SelectedTime ?? TimeSpan.Zero); _hasPendingSelection = true; }
        ApplyTemplate();
        if (_popup == null || _popup.IsOpen) return;
        _popup.SetCurrentValue(Popup.IsOpenProperty, true);
        if (_popup.IsOpen && _hours != null)
        {
            _hours.Focus();
            if (_hours.SelectedItem is { } selected) _hours.ScrollIntoView(selected);
        }
        if (_minutes?.SelectedItem is { } minute) _minutes.ScrollIntoView(minute);
        var openedPopup = _popup;
        CancelPendingOpenLayout();
        _pendingOpenLayout = Dispatcher.BeginInvoke(() =>
        {
            _pendingOpenLayout = null;
            if (!ReferenceEquals(_popup, openedPopup) || !openedPopup.IsOpen) return;
            openedPopup.Child?.UpdateLayout();
            foreach (var list in new[] { _hours, _minutes, _periods })
                if (list?.SelectedItem is { } item) list.ScrollIntoView(item);
            if (_hours?.ItemContainerGenerator.ContainerFromIndex(PendingHourIndex) is UIElement selectedHour)
                selectedHour.Focus();
        }, Jalium.UI.Threading.DispatcherPriority.Loaded);
    }

    private void CancelPendingOpenLayout()
    {
        _pendingOpenLayout?.Abort();
        _pendingOpenLayout = null;
    }

    private void CloseDropDown(bool restoreFocus)
    {
        SetCurrentValue(IsDropDownOpenProperty, false);
        if (restoreFocus) Focus();
    }
    private void OnAccept(object sender, RoutedEventArgs e) => AcceptSelection();
    private void OnCancel(object sender, RoutedEventArgs e) => CloseDropDown(true);
    private void AcceptSelection()
    {
        if (!IsEnabled || !IsDropDownOpen) return;
        SetCurrentValue(SelectedTimeProperty, PendingTime);
        CloseDropDown(true);
    }
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (!_changingTemplate) SetCurrentValue(IsDropDownOpenProperty, false);
    }
    private bool IsPopupSource(object? source)
    {
        for (var current = source as Visual; current != null; current = current.VisualParent)
            if (ReferenceEquals(current, _popup?.Child)) return true;
        return false;
    }
    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || e.ChangedButton != MouseButton.Left || IsPopupSource(e.OriginalSource)) return;
        Focus();
        SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
        e.Handled = true;
    }
    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || e.Handled) return;
        if (e.Key == Key.Escape && IsDropDownOpen) { CloseDropDown(true); e.Handled = true; return; }
        if (e.Key == Key.Enter && IsDropDownOpen) { AcceptSelection(); e.Handled = true; return; }
        if (e.Key == Key.F4 || (e.Key == Key.Down && e.IsAltDown) ||
            (!IsPopupSource(e.OriginalSource) && e.Key is Key.Enter or Key.Space))
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen); e.Handled = true; return;
        }
        if (!IsDropDownOpen && e.Key is Key.Up or Key.Down)
        {
            var minutes = (SelectedTime ?? TimeSpan.Zero).TotalMinutes + (e.Key == Key.Up ? MinuteIncrement : -MinuteIncrement);
            SetCurrentValue(SelectedTimeProperty, TimeSpan.FromMinutes((minutes % 1440 + 1440) % 1440));
            e.Handled = true;
        }
    }

    private void RebuildOptions()
    {
        SetValue(HourOptionsPropertyKey, Enumerable.Range(Is24Hour ? 0 : 1, Is24Hour ? 24 : 12)
            .Select(hour => hour.ToString(Is24Hour ? "D2" : "D", CultureInfo.InvariantCulture)).ToArray());
        SetValue(MinuteOptionsPropertyKey, Enumerable.Range(0, (59 / MinuteIncrement) + 1)
            .Select(index => (index * MinuteIncrement).ToString("D2", CultureInfo.InvariantCulture)).ToArray());
    }
    private void SetPendingTime(TimeSpan time)
    {
        _updatingPending = true;
        try
        {
            var hour = (time.Hours + 24) % 24;
            SetCurrentValue(PendingHourIndexProperty, Is24Hour ? hour : (hour + 11) % 12);
            SetCurrentValue(PendingMinuteIndexProperty, Math.Clamp(time.Minutes / MinuteIncrement, 0, MinuteOptions.Count - 1));
            SetCurrentValue(PendingPeriodIndexProperty, hour >= 12 ? 1 : 0);
        }
        finally { _updatingPending = false; }
        UpdatePendingTime();
    }
    private void UpdatePendingTime()
    {
        if (_updatingPending || HourOptions.Count == 0 || MinuteOptions.Count == 0) return;
        var hour = Math.Clamp(PendingHourIndex, 0, HourOptions.Count - 1) + (Is24Hour ? 0 : 1);
        if (!Is24Hour) hour = hour % 12 + (PendingPeriodIndex == 1 ? 12 : 0);
        SetValue(PendingTimePropertyKey, new TimeSpan(hour, Math.Clamp(PendingMinuteIndex, 0, MinuteOptions.Count - 1) * MinuteIncrement, 0));
    }
    private void UpdateDisplay()
    {
        SetValue(DisplayTextPropertyKey, SelectedTime is { } time ? (Is24Hour
            ? $"{time.Hours:D2}:{time.Minutes:D2}"
            : $"{(time.Hours % 12 == 0 ? 12 : time.Hours % 12)}:{time.Minutes:D2} {(time.Hours >= 12 ? "PM" : "AM")}")
            : PlaceholderText ?? string.Empty);
        UpdateVisualStates();
    }
    private void UpdateVisualStates()
    {
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsKeyboardFocusWithin ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, SelectedTime.HasValue ? "Selected" : "Placeholder", false);
        VisualStateManager.GoToState(this, Header is null or "" ? "HeaderCollapsed" : "HeaderVisible", false);
        VisualStateManager.GoToState(this, Is24Hour ? "TwentyFourHourClock" : "TwelveHourClock", false);
        VisualStateManager.GoToState(this, IsDropDownOpen ? "Open" : "Closed", false);
    }
    private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TimePicker)d).UpdateDisplay();
    private static void OnPendingIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TimePicker)d).UpdatePendingTime();
    private static void OnOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TimePicker)d;
        var pending = picker._hasPendingSelection ? picker.PendingTime : picker.SelectedTime ?? TimeSpan.Zero;
        picker._updatingPending = true;
        try { picker.RebuildOptions(); }
        finally { picker._updatingPending = false; }
        picker.SetPendingTime(pending);
        picker.UpdateDisplay();
    }
    private static void OnSelectedTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TimePicker)d;
        if (picker._hasPendingSelection) picker.SetPendingTime(picker.SelectedTime ?? TimeSpan.Zero);
        picker.UpdateDisplay();
        picker.RaiseEvent(new TimePickerSelectedValueChangedEventArgs(SelectedTimeChangedEvent, (TimeSpan?)e.OldValue, (TimeSpan?)e.NewValue));
    }
    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TimePicker)d;
        if ((bool)e.NewValue!) picker.OpenDropDown();
        else
        {
            picker._hasPendingSelection = false;
            picker._pendingOnRetemplate = null;
            picker.CancelPendingOpenLayout();
            picker._popup?.SetCurrentValue(Popup.IsOpenProperty, false);
        }
        picker.UpdateVisualStates();
    }
}

/// <summary>Provides the old and new selected time.</summary>
public sealed class TimePickerSelectedValueChangedEventArgs : RoutedEventArgs
{
    public TimeSpan? OldTime { get; }
    public TimeSpan? NewTime { get; }
    public TimePickerSelectedValueChangedEventArgs(RoutedEvent routedEvent, TimeSpan? oldTime, TimeSpan? newTime)
    { RoutedEvent = routedEvent; OldTime = oldTime; NewTime = newTime; }
}
