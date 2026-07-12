using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that allows the user to select a date.
/// </summary>
public class DatePicker : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.DatePickerAutomationPeer(this);
    }

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null, OnSelectedDateChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(DatePicker),
            new PropertyMetadata(DateTime.Today, OnDisplayDateChanged, CoerceDisplayDate));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null, OnDisplayDateStartChanged, CoerceDisplayDateStart));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime?), typeof(DatePicker),
            new PropertyMetadata(null, OnDisplayDateEndChanged, CoerceDisplayDateEnd));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(DatePicker),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DatePicker),
            new PropertyMetadata(null, OnLayoutPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(DatePicker),
            new PropertyMetadata("Select a date", OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DateFormatProperty =
        DependencyProperty.Register(nameof(DateFormat), typeof(string), typeof(DatePicker),
            new PropertyMetadata("d", OnDateFormatChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateFormatProperty =
        DependencyProperty.Register(nameof(SelectedDateFormat), typeof(DatePickerFormat), typeof(DatePicker),
            new PropertyMetadata(DatePickerFormat.Long, OnSelectedDateFormatChanged), IsValidSelectedDateFormat);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarStyleProperty =
        DependencyProperty.Register(nameof(CalendarStyle), typeof(Style), typeof(DatePicker),
            new PropertyMetadata(null, OnCalendarStyleChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(DatePicker),
            new PropertyMetadata(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
                OnCalendarConfigurationChanged), IsValidFirstDayOfWeek);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayHighlightedProperty =
        DependencyProperty.Register(nameof(IsTodayHighlighted), typeof(bool), typeof(DatePicker),
            new PropertyMetadata(true, OnCalendarConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(DatePicker),
            new PropertyMetadata(string.Empty, OnTextChanged));

    #endregion

    #region Routed Events

    public static readonly RoutedEvent SelectedDateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedDateChanged), RoutingStrategy.Direct,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(DatePicker));

    public static readonly RoutedEvent CalendarOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(CalendarOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DatePicker));

    public static readonly RoutedEvent CalendarClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(CalendarClosed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DatePicker));

    public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged
    {
        add => AddHandler(SelectedDateChangedEvent, value);
        remove => RemoveHandler(SelectedDateChangedEvent, value);
    }

    public event RoutedEventHandler CalendarOpened
    {
        add => AddHandler(CalendarOpenedEvent, value);
        remove => RemoveHandler(CalendarOpenedEvent, value);
    }

    public event RoutedEventHandler CalendarClosed
    {
        add => AddHandler(CalendarClosedEvent, value);
        remove => RemoveHandler(CalendarClosedEvent, value);
    }

    /// <summary>
    /// Occurs when Text cannot be parsed or represents a date that cannot be selected.
    /// </summary>
    public event EventHandler<DatePickerDateValidationErrorEventArgs>? DateValidationError;

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set
        {
            ValidateSelectedDate(value);
            SetValue(SelectedDateProperty, value);
        }
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty)!;
        set => SetValue(DisplayDateProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? PlaceholderText
    {
        get => (string?)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string DateFormat
    {
        get => (string)(GetValue(DateFormatProperty) ?? "d");
        set => SetValue(DateFormatProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DatePickerFormat SelectedDateFormat
    {
        get => (DatePickerFormat)GetValue(SelectedDateFormatProperty)!;
        set => SetValue(SelectedDateFormatProperty, value);
    }

    /// <summary>
    /// Gets the dates that cannot be selected.
    /// </summary>
    public CalendarBlackoutDatesCollection BlackoutDates => _calendar.BlackoutDates;

    /// <summary>
    /// Gets or sets the style applied to the drop-down calendar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarStyle
    {
        get => (Style?)GetValue(CalendarStyleProperty);
        set => SetValue(CalendarStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the first day displayed in each calendar week.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)GetValue(FirstDayOfWeekProperty)!;
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>
    /// Gets or sets whether today's date is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTodayHighlighted
    {
        get => (bool)GetValue(IsTodayHighlightedProperty)!;
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>
    /// Gets or sets the text displayed by the DatePicker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value);
    }

    #endregion

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_focusBorderBrush = new(ThemeColors.ControlBorderFocused);
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_placeholderBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_iconBrush = new(Color.FromRgb(160, 160, 160));

    #endregion

    #region Private Fields

    private const double DefaultHeight = 32;
    private const double DropdownButtonWidth = 32;
    private Rect _dropdownButtonRect;

    // Popup & Calendar
    private Popup? _popup;
    private readonly Calendar _calendar = new();
    private Border? _calendarBorder;
    private bool _isCloseAnimating;
    private bool _isOpen;
    private bool _isSynchronizingCalendar;
    private bool _isCoercingCalendarValue;
    private bool _isSynchronizingText;
    private bool _isRevertingSelectedDate;

    #endregion

    #region Constructor

    public DatePicker()
    {
        _calendar.SelectedDatesChanged += OnCalendarSelectedDateChanged;
        _calendar.DisplayDateChanged += OnCalendarDisplayDateChanged;
        _calendar.DateClicked += OnCalendarDateClicked;
        SynchronizeCalendarConfiguration();

        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Popup Management

    private void EnsurePopup()
    {
        if (_popup != null) return;

        _calendarBorder = new Border
        {
            Background = ResolvePopupBrush("SurfaceBackground", "ControlBackground", new SolidColorBrush(Color.FromRgb(45, 45, 45))),
            BorderBrush = ResolvePopupBrush("ControlBorder", "MenuFlyoutPresenterBorderBrush", new SolidColorBrush(Color.FromRgb(67, 67, 70))),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 4, 0, 0),
            Child = _calendar
        };

        _popup = new Popup
        {
            Child = _calendarBorder,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false
        };
        _popup.Closed += OnPopupClosed;
    }

    private Brush ResolvePopupBrush(string primaryKey, string secondaryKey, Brush fallback)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    private void OpenDropDown()
    {
        if (_isOpen) return;

        _isCloseAnimating = false;

        EnsurePopup();

        _isSynchronizingCalendar = true;
        try
        {
            SynchronizeCalendarConfiguration();
            _calendar.DisplayDateStart = DisplayDateStart;
            _calendar.DisplayDateEnd = DisplayDateEnd;
            _calendar.DisplayDate = SelectedDate ?? DisplayDate;
            _calendar.SelectedDate = SelectedDate;
            _calendar.DisplayMode = CalendarMode.Month;
        }
        finally
        {
            _isSynchronizingCalendar = false;
        }

        _popup!.IsOpen = true;
        _isOpen = true;

        AnimateOpen();
        OnCalendarOpened(new RoutedEventArgs(CalendarOpenedEvent, this));
    }

    private void CloseDropDown()
    {
        if (!_isOpen) return;
        _isOpen = false;

        AnimateClose();
        OnCalendarClosed(new RoutedEventArgs(CalendarClosedEvent, this));
    }

    private void OnCalendarSelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingCalendar)
        {
            return;
        }

        DateTime? selectedDate = e.AddedItems.Count > 0
            ? (DateTime?)e.AddedItems[0]
            : null;
        _isSynchronizingCalendar = true;
        try
        {
            SetCurrentValue(SelectedDateProperty, selectedDate);
        }
        finally
        {
            _isSynchronizingCalendar = false;
        }
    }

    private void OnCalendarDisplayDateChanged(object? sender, CalendarDateChangedEventArgs e)
    {
        if (_isSynchronizingCalendar || _isCoercingCalendarValue || !e.AddedDate.HasValue)
        {
            return;
        }

        _isSynchronizingCalendar = true;
        try
        {
            SetCurrentValue(DisplayDateProperty, e.AddedDate.Value);
        }
        finally
        {
            _isSynchronizingCalendar = false;
        }
    }

    private void OnCalendarDateClicked(object? sender, DateTime date)
    {
        IsDropDownOpen = false;
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (_isCloseAnimating) return;

        _isOpen = false;
        if (_calendarBorder != null)
        {
            _calendarBorder.Opacity = 1;
            _calendarBorder.RenderOffset = default;
        }
        SetCurrentValue(IsDropDownOpenProperty, false);
        OnCalendarClosed(new RoutedEventArgs(CalendarClosedEvent, this));
    }

    #endregion

    #region Animation

    private void AnimateOpen()
    {
        if (_calendarBorder != null)
        {
            _calendarBorder.Opacity = 1;
            _calendarBorder.RenderOffset = default;
        }
    }

    private void AnimateClose()
    {
        _isCloseAnimating = true;
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }

        if (_calendarBorder != null)
        {
            _calendarBorder.Opacity = 1;
            _calendarBorder.RenderOffset = default;
        }

        _isCloseAnimating = false;
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Space:
                IsDropDownOpen = !IsDropDownOpen;
                e.Handled = true;
                break;
            case Key.Escape when IsDropDownOpen:
                IsDropDownOpen = false;
                e.Handled = true;
                break;
            case Key.Down when e.KeyboardModifiers.HasFlag(ModifierKeys.Alt):
                IsDropDownOpen = true;
                e.Handled = true;
                break;
        }
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        var headerHeight = 0.0;

        if (Header is string headerText)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14);
            TextMeasurement.MeasureText(headerFormatted);
            headerHeight = headerFormatted.Height + 4;
        }

        var width = double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width;
        var height = DefaultHeight + headerHeight;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;
        var headerHeight = 0.0;

        // Draw header
        if (Header is string headerText && Foreground != null)
        {
            var headerFormatted = new FormattedText(headerText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
            {
                Foreground = Foreground
            };
            TextMeasurement.MeasureText(headerFormatted);
            dc.DrawText(headerFormatted, new Point(0, 0));
            headerHeight = headerFormatted.Height + 4;
        }

        var inputRect = new Rect(0, headerHeight, rect.Width, rect.Height - headerHeight);
        var strokeThickness = BorderThickness.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(inputRect, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);

        // Draw background
        if (Background != null)
        {
            dc.DrawRoundedRectangle(Background, null, borderRect, borderRadius);
        }

        // Draw border
        var borderBrush = IsKeyboardFocused ? ResolveFocusedBorderBrush() : BorderBrush;
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, strokeThickness);
            dc.DrawRoundedRectangle(null, pen, borderRect, borderRadius);
        }

        // Focus indicator is painted by FocusVisualManager into the adorner layer.

        // Draw date text or placeholder
        string displayText;
        Brush textBrush;

        if (!string.IsNullOrEmpty(Text))
        {
            displayText = Text;
            textBrush = Foreground ?? s_whiteBrush;
        }
        else
        {
            displayText = PlaceholderText ?? "Select a date";
            textBrush = ResolvePlaceholderBrush();
        }

        var textFormatted = new FormattedText(displayText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
        {
            Foreground = textBrush
        };
        TextMeasurement.MeasureText(textFormatted);

        var textY = inputRect.Top + (inputRect.Height - textFormatted.Height) / 2;
        dc.DrawText(textFormatted, new Point(padding.Left, textY));

        // Draw dropdown button
        _dropdownButtonRect = new Rect(inputRect.Right - DropdownButtonWidth, inputRect.Top, DropdownButtonWidth, inputRect.Height);
        DrawDropdownButton(dc, _dropdownButtonRect);
    }

    private void DrawDropdownButton(DrawingContext dc, Rect rect)
    {
        var iconPen = new Pen(ResolveSecondaryTextBrush(), 1.5);

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        var iconSize = 14;

        // Draw calendar outline
        var calRect = new Rect(centerX - iconSize / 2, centerY - iconSize / 2, iconSize, iconSize);
        dc.DrawRectangle(null, iconPen, calRect);

        // Draw calendar header bar
        dc.DrawLine(iconPen, new Point(calRect.Left, calRect.Top + 4), new Point(calRect.Right, calRect.Top + 4));

        // Draw calendar hangers
        dc.DrawLine(iconPen, new Point(calRect.Left + 3, calRect.Top - 2), new Point(calRect.Left + 3, calRect.Top + 2));
        dc.DrawLine(iconPen, new Point(calRect.Right - 3, calRect.Top - 2), new Point(calRect.Right - 3, calRect.Top + 2));
    }

    private Brush ResolveFocusedBorderBrush()
    {
        return TryFindResource("ControlBorderFocused") as Brush ?? s_focusBorderBrush;
    }

    private Brush ResolvePlaceholderBrush()
    {
        return TryFindResource("TextPlaceholder") as Brush ?? s_placeholderBrush;
    }

    private Brush ResolveSecondaryTextBrush()
    {
        return TryFindResource("TextSecondary") as Brush ?? s_iconBrush;
    }

    #endregion

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

    #region Calendar and Text Synchronization

    private void SynchronizeCalendarConfiguration()
    {
        _calendar.Style = CalendarStyle;
        _calendar.FirstDayOfWeek = FirstDayOfWeek;
        _calendar.IsTodayHighlighted = IsTodayHighlighted;
        _calendar.DisplayDateStart = DisplayDateStart;
        _calendar.DisplayDateEnd = DisplayDateEnd;
    }

    private void ValidateSelectedDate(DateTime? date)
    {
        if (date.HasValue && !_calendar.IsDateSelectableForSelection(date.Value))
        {
            throw new ArgumentOutOfRangeException(nameof(date),
                "The date is outside the selectable range or is blacked out.");
        }
    }

    private void CommitText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (SelectedDate.HasValue)
            {
                SetCurrentValue(SelectedDateProperty, null);
            }
            return;
        }

        if (SelectedDate.HasValue && string.Equals(text, FormatDate(SelectedDate.Value), StringComparison.Ordinal))
        {
            return;
        }

        DateTime parsedDate;
        try
        {
            parsedDate = DateTime.Parse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces);
        }
        catch (FormatException exception)
        {
            ReportValidationError(exception, text);
            return;
        }

        if (!_calendar.IsDateSelectableForSelection(parsedDate))
        {
            ReportValidationError(
                new ArgumentOutOfRangeException(nameof(text), "The parsed date cannot be selected."),
                text);
            return;
        }

        SetCurrentValue(SelectedDateProperty, parsedDate);
    }

    private void ReportValidationError(Exception exception, string text)
    {
        var args = new DatePickerDateValidationErrorEventArgs(exception, text);
        OnDateValidationError(args);
        UpdateTextFromSelectedDate();
        if (args.ThrowException)
        {
            throw args.Exception;
        }
    }

    private void UpdateTextFromSelectedDate()
    {
        SetTextWithoutParsing(SelectedDate.HasValue ? FormatDate(SelectedDate.Value) : string.Empty);
        InvalidateVisual();
    }

    private void SetTextWithoutParsing(string text)
    {
        if (string.Equals(Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _isSynchronizingText = true;
        try
        {
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _isSynchronizingText = false;
        }
    }

    private string FormatDate(DateTime date)
    {
        string format = HasLocalValue(DateFormatProperty) && !string.IsNullOrWhiteSpace(DateFormat)
            ? DateFormat
            : SelectedDateFormat == DatePickerFormat.Long ? "D" : "d";
        return date.ToString(format, CultureInfo.CurrentCulture);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            if (datePicker._isRevertingSelectedDate)
            {
                return;
            }

            DateTime? newDate = (DateTime?)e.NewValue;
            try
            {
                datePicker.ValidateSelectedDate(newDate);
            }
            catch
            {
                datePicker._isRevertingSelectedDate = true;
                try
                {
                    datePicker.SetCurrentValue(SelectedDateProperty, e.OldValue);
                }
                finally
                {
                    datePicker._isRevertingSelectedDate = false;
                }
                throw;
            }

            if (!datePicker._isSynchronizingCalendar)
            {
                datePicker._isSynchronizingCalendar = true;
                try
                {
                    datePicker._calendar.SelectedDate = newDate;
                    if (newDate.HasValue &&
                        (newDate.Value.Month != datePicker.DisplayDate.Month ||
                         newDate.Value.Year != datePicker.DisplayDate.Year))
                    {
                        datePicker.SetCurrentValue(DisplayDateProperty, newDate.Value);
                        datePicker._calendar.DisplayDate = newDate.Value;
                    }
                }
                finally
                {
                    datePicker._isSynchronizingCalendar = false;
                }
            }

            datePicker.CoerceValue(DisplayDateStartProperty);
            datePicker.CoerceValue(DisplayDateEndProperty);
            datePicker.CoerceValue(DisplayDateProperty);

            datePicker.UpdateTextFromSelectedDate();
            datePicker.InvalidateVisual();

            var args = new SelectionChangedEventArgs(SelectedDateChangedEvent,
                e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(),
                e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>());
            datePicker.OnSelectedDateChanged(args);
        }
    }

    private static void OnDisplayDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateVisual();
        }
    }

    private static object? CoerceDisplayDate(DependencyObject d, object? baseValue)
    {
        var datePicker = (DatePicker)d;
        if (datePicker._calendar is null || baseValue is not DateTime displayDate)
        {
            return baseValue;
        }

        datePicker._isCoercingCalendarValue = true;
        try
        {
            datePicker._calendar.DisplayDate = displayDate;
            return datePicker._calendar.DisplayDate;
        }
        finally
        {
            datePicker._isCoercingCalendarValue = false;
        }
    }

    private static object? CoerceDisplayDateStart(DependencyObject d, object? baseValue)
    {
        var datePicker = (DatePicker)d;
        if (datePicker._calendar is null)
        {
            return baseValue;
        }

        datePicker._isCoercingCalendarValue = true;
        try
        {
            datePicker._calendar.DisplayDateStart = (DateTime?)baseValue;
            return datePicker._calendar.DisplayDateStart;
        }
        finally
        {
            datePicker._isCoercingCalendarValue = false;
        }
    }

    private static object? CoerceDisplayDateEnd(DependencyObject d, object? baseValue)
    {
        var datePicker = (DatePicker)d;
        if (datePicker._calendar is null)
        {
            return baseValue;
        }

        datePicker._isCoercingCalendarValue = true;
        try
        {
            datePicker._calendar.DisplayDateEnd = (DateTime?)baseValue;
            return datePicker._calendar.DisplayDateEnd;
        }
        finally
        {
            datePicker._isCoercingCalendarValue = false;
        }
    }

    private static void OnDisplayDateStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.CoerceValue(DisplayDateEndProperty);
            datePicker.CoerceValue(DisplayDateProperty);
        }
    }

    private static void OnDisplayDateEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.CoerceValue(DisplayDateProperty);
        }
    }

    private static void OnCalendarStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker._calendar.Style = (Style?)e.NewValue;
        }
    }

    private static void OnCalendarConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker._calendar.FirstDayOfWeek = datePicker.FirstDayOfWeek;
            datePicker._calendar.IsTodayHighlighted = datePicker.IsTodayHighlighted;
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            if (!datePicker._isSynchronizingText)
            {
                datePicker.CommitText((string?)e.NewValue ?? string.Empty);
            }
            datePicker.InvalidateVisual();
        }
    }

    private static void OnSelectedDateFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.UpdateTextFromSelectedDate();
        }
    }

    private static void OnDateFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.UpdateTextFromSelectedDate();
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            if ((bool)e.NewValue!)
                datePicker.OpenDropDown();
            else
                datePicker.CloseDropDown();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateMeasure();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DatePicker datePicker)
        {
            datePicker.InvalidateVisual();
        }
    }

    private static bool IsValidFirstDayOfWeek(object? value) =>
        value is DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or
            DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday;

    private static bool IsValidSelectedDateFormat(object? value) =>
        value is DatePickerFormat.Long or DatePickerFormat.Short;

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
