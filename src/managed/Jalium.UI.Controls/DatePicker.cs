using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a control that allows the user to select a date.
/// </summary>
[TemplatePart(Name = "PART_TextBox", Type = typeof(DatePickerTextBox))]
[TemplatePart(Name = "PART_Button", Type = typeof(Button))]
[TemplatePart(Name = "PART_Popup", Type = typeof(Popup))]
[TemplatePart(Name = "PART_Calendar", Type = typeof(Calendar))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Open", GroupName = "DropDownStates")]
[TemplateVisualState(Name = "Closed", GroupName = "DropDownStates")]
[TemplateVisualState(Name = "Valid", GroupName = "ValidationStates")]
[TemplateVisualState(Name = "Invalid", GroupName = "ValidationStates")]
public class DatePicker : Control
{
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.DatePickerAutomationPeer(this);
    }

#region Dependency Properties
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime? ), typeof(DatePicker), new PropertyMetadata(null, OnSelectedDateChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateProperty = DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(DatePicker), new PropertyMetadata(DateTime.Today, OnDisplayDateChanged, CoerceDisplayDate));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateStartProperty = DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime? ), typeof(DatePicker), new PropertyMetadata(null, OnDisplayDateStartChanged, CoerceDisplayDateStart));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateEndProperty = DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime? ), typeof(DatePicker), new PropertyMetadata(null, OnDisplayDateEndChanged, CoerceDisplayDateEnd));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty = DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(DatePicker), new PropertyMetadata(false, OnIsDropDownOpenChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(object), typeof(DatePicker), new PropertyMetadata(null, OnLayoutPropertyChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(DatePicker), new PropertyMetadata("Select a date", OnVisualPropertyChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DateFormatProperty = DependencyProperty.Register(nameof(DateFormat), typeof(string), typeof(DatePicker), new PropertyMetadata("d", OnDateFormatChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateFormatProperty = DependencyProperty.Register(nameof(SelectedDateFormat), typeof(DatePickerFormat), typeof(DatePicker), new PropertyMetadata(DatePickerFormat.Long, OnSelectedDateFormatChanged), IsValidSelectedDateFormat);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarStyleProperty = DependencyProperty.Register(nameof(CalendarStyle), typeof(Style), typeof(DatePicker), new PropertyMetadata(null, OnCalendarStyleChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(DatePicker), new PropertyMetadata(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, OnCalendarConfigurationChanged), IsValidFirstDayOfWeek);
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayHighlightedProperty = DependencyProperty.Register(nameof(IsTodayHighlighted), typeof(bool), typeof(DatePicker), new PropertyMetadata(true, OnCalendarConfigurationChanged));
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(DatePicker), new PropertyMetadata(string.Empty, OnTextChanged));
#endregion
#region Routed Events
    public static readonly RoutedEvent SelectedDateChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectedDateChanged), RoutingStrategy.Direct, typeof(EventHandler<SelectionChangedEventArgs>), typeof(DatePicker));
    public static readonly RoutedEvent CalendarOpenedEvent = EventManager.RegisterRoutedEvent(nameof(CalendarOpened), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DatePicker));
    public static readonly RoutedEvent CalendarClosedEvent = EventManager.RegisterRoutedEvent(nameof(CalendarClosed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DatePicker));
    public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged { add => AddHandler(SelectedDateChangedEvent, value); remove => RemoveHandler(SelectedDateChangedEvent, value); }

    public event RoutedEventHandler CalendarOpened { add => AddHandler(CalendarOpenedEvent, value); remove => RemoveHandler(CalendarOpenedEvent, value); }

    public event RoutedEventHandler CalendarClosed { add => AddHandler(CalendarClosedEvent, value); remove => RemoveHandler(CalendarClosedEvent, value); }

    /// <summary>
    /// Occurs when Text cannot be parsed or represents a date that cannot be selected.
    /// </summary>
    public event EventHandler<DatePickerDateValidationErrorEventArgs>? DateValidationError;
#endregion
#region CLR Properties
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DateTime? SelectedDate
    {
        get => (DateTime? )GetValue(SelectedDateProperty);
        set
        {
            ValidateSelectedDate(value);
            SetValue(SelectedDateProperty, value);
        }
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime DisplayDate { get => (DateTime)GetValue(DisplayDateProperty)!; set => SetValue(DisplayDateProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateStart { get => (DateTime? )GetValue(DisplayDateStartProperty); set => SetValue(DisplayDateStartProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateEnd { get => (DateTime? )GetValue(DisplayDateEndProperty); set => SetValue(DisplayDateEndProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen { get => (bool)GetValue(IsDropDownOpenProperty)!; set => SetValue(IsDropDownOpenProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? PlaceholderText { get => (string? )GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string DateFormat { get => (string)(GetValue(DateFormatProperty) ?? "d"); set => SetValue(DateFormatProperty, value); }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DatePickerFormat SelectedDateFormat { get => (DatePickerFormat)GetValue(SelectedDateFormatProperty)!; set => SetValue(SelectedDateFormatProperty, value); }
    /// <summary>
    /// Gets the dates that cannot be selected.
    /// </summary>
    public CalendarBlackoutDatesCollection BlackoutDates => _blackoutDates;

    /// <summary>
    /// Gets or sets the style applied to the drop-down calendar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarStyle { get => (Style? )GetValue(CalendarStyleProperty); set => SetValue(CalendarStyleProperty, value); }

    /// <summary>
    /// Gets or sets the first day displayed in each calendar week.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DayOfWeek FirstDayOfWeek { get => (DayOfWeek)GetValue(FirstDayOfWeekProperty)!; set => SetValue(FirstDayOfWeekProperty, value); }

    /// <summary>
    /// Gets or sets whether today's date is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTodayHighlighted { get => (bool)GetValue(IsTodayHighlightedProperty)!; set => SetValue(IsTodayHighlightedProperty, value); }

    /// <summary>
    /// Gets or sets the text displayed by the DatePicker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text { get => (string? )GetValue(TextProperty) ?? string.Empty; set => SetValue(TextProperty, value); }

#endregion
    private static readonly DependencyPropertyKey HasHeaderPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasHeader), typeof(bool), typeof(DatePicker), new PropertyMetadata(false));
    public static readonly DependencyProperty HasHeaderProperty = HasHeaderPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsHeaderVisiblePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsHeaderVisible), typeof(bool), typeof(DatePicker), new PropertyMetadata(false));
    public static readonly DependencyProperty IsHeaderVisibleProperty = IsHeaderVisiblePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey HasValidationErrorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HasValidationError), typeof(bool), typeof(DatePicker), new PropertyMetadata(false));
    public static readonly DependencyProperty HasValidationErrorProperty = HasValidationErrorPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey ValidationMessagePropertyKey = DependencyProperty.RegisterReadOnly(nameof(ValidationMessage), typeof(string), typeof(DatePicker), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ValidationMessageProperty = ValidationMessagePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PickerFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(PickerFontSize), typeof(double), typeof(DatePicker), new PropertyMetadata(14.0));
    public static readonly DependencyProperty PickerFontSizeProperty = PickerFontSizePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PickerFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PickerFontFamily), typeof(FontFamily), typeof(DatePicker), new PropertyMetadata(FontFamilyProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty PickerFontFamilyProperty = PickerFontFamilyPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PickerFontWeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PickerFontWeight), typeof(FontWeight), typeof(DatePicker), new PropertyMetadata(FontWeightProperty.GetMetadata(typeof(Control)).DefaultValue));
    public static readonly DependencyProperty PickerFontWeightProperty = PickerFontWeightPropertyKey.DependencyProperty;
    public bool HasHeader => (bool)GetValue(HasHeaderProperty)!;
    public bool IsHeaderVisible => (bool)GetValue(IsHeaderVisibleProperty)!;
    public bool HasValidationError => (bool)GetValue(HasValidationErrorProperty)!;
    public string ValidationMessage => (string)GetValue(ValidationMessageProperty)!;
    public double PickerFontSize => (double)GetValue(PickerFontSizeProperty)!;
    public FontFamily PickerFontFamily => (FontFamily)GetValue(PickerFontFamilyProperty)!;
    public FontWeight PickerFontWeight => (FontWeight)GetValue(PickerFontWeightProperty)!;

    private readonly CalendarBlackoutDatesCollection _blackoutDates;
    private DatePickerTextBox? _textBox;
    private Button? _button;
    private Popup? _popup;
    private Calendar? _calendar;
    private UIElement? _popupChild;
    private FrameworkElement? _inputRoot;
    private Action<DependencyProperty, object?, object?>? _popupStateChanged;
    private CalendarMode _savedDisplayMode = CalendarMode.Month;
    private DateTime? _savedViewDate;
    private bool _isOpen, _updatingPopup, _isSynchronizingCalendar, _isSynchronizingText, _isRevertingSelectedDate;
    static DatePicker()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DatePicker), new PropertyMetadata(typeof(DatePicker)));
        FocusableProperty.OverrideMetadata(typeof(DatePicker), new PropertyMetadata(true));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(DatePicker), new PropertyMetadata(false));
    }

    public DatePicker()
    {
        _blackoutDates = new CalendarBlackoutDatesCollection(() => SelectedDate.HasValue ? new[] { SelectedDate.Value } : Array.Empty<DateTime>(), SynchronizeCalendarConfiguration);
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        Loaded += (_, _) =>
        {
            UpdatePresentation();
            UpdatePopup();
            FocusCalendarWhenOpen();
        };
        Unloaded += (_, _) => SetCurrentValue(IsDropDownOpenProperty, false);
    }

    public override void OnApplyTemplate()
    {
        ReleaseParts();
        base.OnApplyTemplate();
        _textBox = GetTemplateChild("PART_TextBox") as DatePickerTextBox;
        _button = GetTemplateChild("PART_Button") as Button;
        _popup = GetTemplateChild("PART_Popup") as Popup;
        _calendar = GetTemplateChild("PART_Calendar") as Calendar;
        _inputRoot = GetTemplateChild("PART_InputRoot") as FrameworkElement;
        if (_textBox is not null)
        {
            _textBox.TextChanged += OnInputTextChanged;
            _textBox.LostKeyboardFocus += OnInputLostFocus;
            _textBox.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnInputKeyDown));
            UpdateInputText();
        }

        if (_button is not null)
            _button.Click += OnButtonClick;
        if (_popup is not null)
        {
            _popup.SetCurrentValue(Popup.PlacementTargetProperty, _inputRoot ?? this);
            _popup.Closed += OnPopupClosed;
            var popup = _popup;
            _popupStateChanged = (property, _, value) =>
            {
                if (ReferenceEquals(_popup, popup) && property == Popup.IsOpenProperty && value is false && !_updatingPopup)
                    SetCurrentValue(IsDropDownOpenProperty, false);
            };
            _popup.PropertyChangedInternal += _popupStateChanged;
            _popupChild = _popup.Child;
            _popupChild?.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnPopupKeyDown));
        }

        if (_calendar is not null)
        {
            _calendar.SelectedDatesChanged += OnCalendarSelectedDateChanged;
            _calendar.DisplayDateChanged += OnCalendarDisplayDateChanged;
            _calendar.DateClicked += OnCalendarDateClicked;
        }

        SynchronizeCalendarConfiguration();
        if (_isOpen && IsDropDownOpen && _calendar is not null)
        {
            _calendar.SetCurrentValue(Calendar.DisplayModeProperty, _savedDisplayMode);
            if (_savedViewDate.HasValue)
                _calendar.SetCurrentValue(Calendar.DisplayDateProperty, _savedViewDate.Value);
        }

        _savedViewDate = null;
        UpdatePresentation();
        UpdatePopup();
        FocusCalendarWhenOpen();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseParts();
        base.OnTemplateContentClearing();
    }

    private void ReleaseParts()
    {
        if (_textBox is not null)
        {
            _textBox.TextChanged -= OnInputTextChanged;
            _textBox.LostKeyboardFocus -= OnInputLostFocus;
            _textBox.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnInputKeyDown));
        }

        if (_button is not null)
            _button.Click -= OnButtonClick;
        if (_calendar is not null)
        {
            _savedDisplayMode = _calendar.DisplayMode;
            _savedViewDate = _calendar.DisplayDate;
            _calendar.SelectedDatesChanged -= OnCalendarSelectedDateChanged;
            _calendar.DisplayDateChanged -= OnCalendarDisplayDateChanged;
            _calendar.DateClicked -= OnCalendarDateClicked;
            _calendar.BlackoutDates.Clear();
        }

        _popupChild?.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnPopupKeyDown));
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.PropertyChangedInternal -= _popupStateChanged;
            _popup.SetCurrentValue(Popup.IsOpenProperty, false);
        }

        _textBox = null;
        _button = null;
        _popup = null;
        _calendar = null;
        _popupChild = null;
        _inputRoot = null;
        _popupStateChanged = null;
    }

    private void SynchronizeCalendarConfiguration()
    {
        if (_calendar is not { } calendar || _isSynchronizingCalendar)
            return;
        _isSynchronizingCalendar = true;
        try
        {
            calendar.SetCurrentValue(StyleProperty, CalendarStyle);
            calendar.SetCurrentValue(Calendar.SelectionModeProperty, CalendarSelectionMode.SingleDate);
            calendar.SetCurrentValue(Calendar.FirstDayOfWeekProperty, FirstDayOfWeek);
            calendar.SetCurrentValue(Calendar.IsTodayHighlightedProperty, IsTodayHighlighted);
            calendar.SetCurrentValue(Calendar.SelectedDateProperty, SelectedDate);
            calendar.SetCurrentValue(Calendar.DisplayDateStartProperty, DisplayDateStart);
            calendar.SetCurrentValue(Calendar.DisplayDateEndProperty, DisplayDateEnd);
            calendar.SetCurrentValue(Calendar.DisplayDateProperty, DisplayDate);
            if (!calendar.BlackoutDates.SequenceEqual(BlackoutDates))
            {
                calendar.BlackoutDates.Clear();
                foreach (var range in BlackoutDates)
                    calendar.BlackoutDates.Add(range);
            }
        }
        finally
        {
            _isSynchronizingCalendar = false;
        }
    }

    private void UpdatePopup()
    {
        if (_updatingPopup)
            return;
        _updatingPopup = true;
        try
        {
            bool open = IsDropDownOpen && IsEnabled;
            if (open)
                SynchronizeCalendarConfiguration();
            if (open && !_isOpen)
                _calendar?.SetCurrentValue(Calendar.DisplayModeProperty, CalendarMode.Month);
            if (_popup is not null)
                _popup.SetCurrentValue(Popup.IsOpenProperty, open);
            if (_isOpen != open)
            {
                _isOpen = open;
                if (open)
                {
                    OnCalendarOpened(new RoutedEventArgs(CalendarOpenedEvent, this));
                    FocusCalendarWhenOpen();
                }
                else
                {
                    OnCalendarClosed(new RoutedEventArgs(CalendarClosedEvent, this));
                    Dispatcher.BeginInvokeCritical(() =>
                    {
                        if (!IsDropDownOpen && IsLoaded)
                            _textBox?.Focus();
                    });
                }
            }
        }
        finally
        {
            _updatingPopup = false;
        }

        UpdatePresentation();
    }

    private void FocusCalendarWhenOpen()
    {
        if (IsDropDownOpen && IsLoaded)
            Dispatcher.BeginInvokeCritical(() =>
            {
                if (IsDropDownOpen && IsLoaded)
                    _calendar?.Focus();
            });
    }

    private void OnPopupClosed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _popup) || _updatingPopup)
            return;
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnButtonClick(object sender, RoutedEventArgs args)
    {
        if (IsEnabled)
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
    }

    private void OnCalendarSelectedDateChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_isSynchronizingCalendar || !ReferenceEquals(sender, _calendar))
            return;
        SetCurrentValue(SelectedDateProperty, _calendar!.SelectedDate);
    }

    private void OnCalendarDisplayDateChanged(object? sender, CalendarDateChangedEventArgs args)
    {
        if (_isSynchronizingCalendar || !ReferenceEquals(sender, _calendar) || !args.AddedDate.HasValue)
            return;
        SetCurrentValue(DisplayDateProperty, args.AddedDate.Value);
    }

    private void OnCalendarDateClicked(object? sender, DateTime date)
    {
        if (ReferenceEquals(sender, _calendar))
            SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnInputTextChanged(object sender, RoutedEventArgs args)
    {
        if (_isSynchronizingText || !ReferenceEquals(sender, _textBox))
            return;
        _isSynchronizingText = true;
        try
        {
            SetCurrentValue(TextProperty, _textBox!.Text ?? string.Empty);
        }
        finally
        {
            _isSynchronizingText = false;
        }

        SetValidation(false, string.Empty);
    }

    private void OnInputLostFocus(object sender, RoutedEventArgs args)
    {
        if (ReferenceEquals(sender, _textBox) && !_isSynchronizingText && !_textBox!.IsImeComposing)
            CommitText(Text);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs args)
    {
        if (!IsEnabled || _textBox?.IsImeComposing == true)
            return;
        if (args.Key == Key.Enter)
        {
            CommitText(Text);
            if (!HasValidationError)
                SetCurrentValue(IsDropDownOpenProperty, false);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape)
        {
            UpdateTextFromSelectedDate();
            SetValidation(false, string.Empty);
            SetCurrentValue(IsDropDownOpenProperty, false);
            args.Handled = true;
        }
        else if (args.Key == Key.Down && args.KeyboardModifiers.HasFlag(ModifierKeys.Alt))
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            args.Handled = true;
        }
    }

    private void OnPopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape)
            return;
        SetCurrentValue(IsDropDownOpenProperty, false);
        args.Handled = true;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs args)
    {
        if (!IsEnabled || _textBox?.IsKeyboardFocusWithin == true)
            return;
        if (args.Key is Key.Enter or Key.Space)
        {
            SetCurrentValue(IsDropDownOpenProperty, !IsDropDownOpen);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            args.Handled = true;
        }
        else if (args.Key == Key.Down && args.KeyboardModifiers.HasFlag(ModifierKeys.Alt))
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            args.Handled = true;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdatePresentation();
        return base.MeasureOverride(availableSize);
    }

    protected override void RenderTemplatedBackground(DrawingContext context)
    {
        UpdatePresentation();
        if (RenderSize.Width > 0 && RenderSize.Height > 0)
            base.RenderTemplatedBackground(context);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == FontSizeProperty || args.Property == FontFamilyProperty || args.Property == FontWeightProperty || args.Property == IsMouseOverProperty || args.Property == IsKeyboardFocusWithinProperty)
            UpdatePresentation();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
            SetCurrentValue(IsDropDownOpenProperty, false);
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        SetValue(HasHeaderPropertyKey, Header is not null && Header is not string { Length: 0 });
        SetValue(IsHeaderVisiblePropertyKey, HasHeader && FontSize > 0);
        SetValue(PickerFontSizePropertyKey, FontSize);
        SetValue(PickerFontFamilyPropertyKey, FontFamily);
        SetValue(PickerFontWeightPropertyKey, FontWeight);
        VisualStateManager.GoToState(this, !IsEnabled ? "Disabled" : IsKeyboardFocusWithin ? "Focused" : IsMouseOver ? "PointerOver" : "Normal", false);
        VisualStateManager.GoToState(this, IsDropDownOpen ? "Open" : "Closed", false);
        VisualStateManager.GoToState(this, HasValidationError ? "Invalid" : "Valid", false);
    }

    private void SetValidation(bool invalid, string message)
    {
        SetValue(HasValidationErrorPropertyKey, invalid);
        SetValue(ValidationMessagePropertyKey, message);
        UpdatePresentation();
    }

    private bool IsDateSelectable(DateTime date) => !(_blackoutDates?.Contains(date.Date) ?? false);
    private void ValidateSelectedDate(DateTime? date)
    {
        if (date.HasValue && !IsDateSelectable(date.Value))
            throw new ArgumentOutOfRangeException(nameof(date), "The date is blacked out.");
    }

    private void CommitText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            SetValidation(false, string.Empty);
            SetCurrentValue(SelectedDateProperty, null);
            UpdateTextFromSelectedDate();
            return;
        }

        if (SelectedDate.HasValue && string.Equals(text, FormatDate(SelectedDate.Value), StringComparison.Ordinal))
        {
            SetValidation(false, string.Empty);
            UpdateInputText();
            return;
        }

        DateTime parsed;
        try
        {
            parsed = DateTime.Parse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces);
        }
        catch (FormatException error)
        {
            ReportValidationError(error, text);
            return;
        }

        if (!IsDateSelectable(parsed))
        {
            ReportValidationError(new ArgumentOutOfRangeException(nameof(text), "The parsed date cannot be selected."), text);
            return;
        }

        SetValidation(false, string.Empty);
        SetCurrentValue(SelectedDateProperty, parsed);
        UpdateTextFromSelectedDate();
    }

    private void ReportValidationError(Exception error, string text)
    {
        var args = new DatePickerDateValidationErrorEventArgs(error, text);
        OnDateValidationError(args);
        UpdateTextFromSelectedDate();
        SetValidation(true, error.Message);
        if (args.ThrowException)
            throw args.Exception;
    }

    private void UpdateTextFromSelectedDate()
    {
        _isSynchronizingText = true;
        try
        {
            SetCurrentValue(TextProperty, SelectedDate.HasValue ? FormatDate(SelectedDate.Value) : string.Empty);
            UpdateInputText();
        }
        finally
        {
            _isSynchronizingText = false;
        }
    }

    private void UpdateInputText()
    {
        if (_textBox is null || string.Equals(_textBox.Text, Text, StringComparison.Ordinal))
            return;
        bool previous = _isSynchronizingText;
        _isSynchronizingText = true;
        try
        {
            _textBox.SetCurrentValue(TextBox.TextProperty, Text);
        }
        finally
        {
            _isSynchronizingText = previous;
        }
    }

    private string FormatDate(DateTime date)
    {
        string format = DependencyPropertyHelper.GetValueSource(this, DateFormatProperty).BaseValueSource != BaseValueSource.Default && !string.IsNullOrWhiteSpace(DateFormat) ? DateFormat : SelectedDateFormat == DatePickerFormat.Long ? "D" : "d";
        return date.ToString(format, CultureInfo.CurrentCulture);
    }

    private static void OnSelectedDateChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DatePicker)target;
        if (picker._isRevertingSelectedDate)
            return;
        try
        {
            picker.ValidateSelectedDate((DateTime? )args.NewValue);
        }
        catch
        {
            picker._isRevertingSelectedDate = true;
            try
            {
                picker.SetCurrentValue(SelectedDateProperty, args.OldValue);
            }
            finally
            {
                picker._isRevertingSelectedDate = false;
            }

            throw;
        }

        picker.CoerceValue(DisplayDateStartProperty);
        picker.CoerceValue(DisplayDateEndProperty);
        picker.CoerceValue(DisplayDateProperty);
        if (args.NewValue is DateTime date && (date.Month != picker.DisplayDate.Month || date.Year != picker.DisplayDate.Year))
            picker.SetCurrentValue(DisplayDateProperty, date);
        picker.SynchronizeCalendarConfiguration();
        picker.UpdateTextFromSelectedDate();
        picker.SetValidation(false, string.Empty);
        picker.OnSelectedDateChanged(new SelectionChangedEventArgs(SelectedDateChangedEvent, args.OldValue is null ? Array.Empty<object>() : new[] { args.OldValue }, args.NewValue is null ? Array.Empty<object>() : new[] { args.NewValue }));
    }

    private static object? CoerceDisplayDate(DependencyObject target, object? value)
    {
        if (value is not DateTime date)
            return value;
        var picker = (DatePicker)target;
        if (picker.DisplayDateStart.HasValue && date < picker.DisplayDateStart.Value)
            return picker.DisplayDateStart.Value;
        if (picker.DisplayDateEnd.HasValue && date > picker.DisplayDateEnd.Value)
            return picker.DisplayDateEnd.Value;
        return date;
    }

    private static object? CoerceDisplayDateStart(DependencyObject target, object? value)
    {
        var picker = (DatePicker)target;
        var date = (DateTime? )value;
        return date.HasValue && picker.SelectedDate.HasValue && date.Value > picker.SelectedDate.Value ? picker.SelectedDate : date;
    }

    private static object? CoerceDisplayDateEnd(DependencyObject target, object? value)
    {
        var picker = (DatePicker)target;
        var date = (DateTime? )value;
        if (!date.HasValue)
            return null;
        if (picker.DisplayDateStart.HasValue && date < picker.DisplayDateStart.Value)
            return picker.DisplayDateStart;
        return picker.SelectedDate.HasValue && date < picker.SelectedDate ? picker.SelectedDate : date;
    }

    private static void OnDisplayDateChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).SynchronizeCalendarConfiguration();
    private static void OnDisplayDateStartChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        target.CoerceValue(DisplayDateEndProperty);
        target.CoerceValue(DisplayDateProperty);
        ((DatePicker)target).SynchronizeCalendarConfiguration();
    }

    private static void OnDisplayDateEndChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        target.CoerceValue(DisplayDateProperty);
        ((DatePicker)target).SynchronizeCalendarConfiguration();
    }

    private static void OnCalendarStyleChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).SynchronizeCalendarConfiguration();
    private static void OnCalendarConfigurationChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).SynchronizeCalendarConfiguration();
    private static void OnTextChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DatePicker)target;
        if (!picker._isSynchronizingText)
            picker.CommitText((string? )args.NewValue ?? string.Empty);
        picker.UpdateInputText();
    }

    private static void OnSelectedDateFormatChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).UpdateTextFromSelectedDate();
    private static void OnDateFormatChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).UpdateTextFromSelectedDate();
    private static void OnIsDropDownOpenChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DatePicker)target;
        if (!picker.IsEnabled && (bool)args.NewValue!)
        {
            picker.SetCurrentValue(IsDropDownOpenProperty, false);
            return;
        }

        picker.ApplyTemplate();
        picker.UpdatePopup();
    }

    private static void OnLayoutPropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var picker = (DatePicker)target;
        picker.UpdatePresentation();
        picker.InvalidateMeasure();
    }

    private static new void OnVisualPropertyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) => ((DatePicker)target).UpdatePresentation();
    private static bool IsValidFirstDayOfWeek(object? value) => value is DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday;
    private static bool IsValidSelectedDateFormat(object? value) => value is DatePickerFormat.Long or DatePickerFormat.Short;
#region Protected Event Hooks
    /// <summary>
    /// Raises the CalendarClosed event.
    /// </summary>
    protected virtual void OnCalendarClosed(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the CalendarOpened event.
    /// </summary>
    protected virtual void OnCalendarOpened(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the DateValidationError event.
    /// </summary>
    protected virtual void OnDateValidationError(DatePickerDateValidationErrorEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        DateValidationError?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the SelectedDateChanged event.
    /// </summary>
    protected virtual void OnSelectedDateChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }
#endregion
}

/// <summary>
/// Specifies the format used to display the selected date in a DatePicker.
/// </summary>
public enum DatePickerFormat
{
    /// <summary>
    /// Short date format (e.g., "2/15/2026").
    /// </summary>
    Short = 1,
    /// <summary>
    /// Long date format (e.g., "Sunday, February 15, 2026").
    /// </summary>
    Long = 0
}
