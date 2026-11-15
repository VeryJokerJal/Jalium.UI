using System.Globalization;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Represents a calendar control for selecting dates.
/// </summary>
[TemplatePart(Name = "PART_CalendarItem", Type = typeof(CalendarItem))]
public class Calendar : Control
{
    private CalendarItem? _calendarItem;
    static Calendar() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Calendar), new PropertyMetadata(typeof(Calendar)));
    /// <inheritdoc/>
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.CalendarAutomationPeer(this);
    }

    private static readonly DependencyPropertyKey CalendarItemForegroundPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CalendarItemForeground), typeof(Brush), typeof(Calendar), new PropertyMetadata(null));
    public static readonly DependencyProperty CalendarItemForegroundProperty = CalendarItemForegroundPropertyKey.DependencyProperty;
    public Brush? CalendarItemForeground => (Brush? )GetValue(CalendarItemForegroundProperty);

#region Dependency Properties
    /// <summary>
    /// Identifies the SelectedDate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateProperty = DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime? ), typeof(Calendar), new PropertyMetadata(null, OnSelectedDateChanged));
    /// <summary>
    /// Identifies the DisplayDate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateProperty = DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(Calendar), new PropertyMetadata(DateTime.Today, OnDisplayDateChanged, CoerceDisplayDate));
    /// <summary>
    /// Identifies the DisplayDateStart dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateStartProperty = DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime? ), typeof(Calendar), new PropertyMetadata(null, OnDisplayDateStartChanged, CoerceDisplayDateStart));
    /// <summary>
    /// Identifies the DisplayDateEnd dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateEndProperty = DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime? ), typeof(Calendar), new PropertyMetadata(null, OnDisplayDateEndChanged, CoerceDisplayDateEnd));
    /// <summary>
    /// Identifies the FirstDayOfWeek dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FirstDayOfWeekProperty = DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(Calendar), new PropertyMetadata(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, OnFirstDayOfWeekChanged), IsValidFirstDayOfWeek);
    /// <summary>
    /// Identifies the IsTodayHighlighted dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayHighlightedProperty = DependencyProperty.Register(nameof(IsTodayHighlighted), typeof(bool), typeof(Calendar), new PropertyMetadata(true, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty = DependencyProperty.Register(nameof(SelectionMode), typeof(CalendarSelectionMode), typeof(Calendar), new PropertyMetadata(CalendarSelectionMode.SingleDate, OnSelectionModePropertyChanged), IsValidSelectionMode);
    /// <summary>
    /// Identifies the DisplayMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(CalendarMode), typeof(Calendar), new FrameworkPropertyMetadata(CalendarMode.Month, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayModePropertyChanged), IsValidDisplayMode);
    /// <summary>
    /// Identifies the CalendarButtonStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarButtonStyleProperty = DependencyProperty.Register(nameof(CalendarButtonStyle), typeof(Style), typeof(Calendar), new PropertyMetadata(null, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the CalendarDayButtonStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarDayButtonStyleProperty = DependencyProperty.Register(nameof(CalendarDayButtonStyle), typeof(Style), typeof(Calendar), new PropertyMetadata(null, OnVisualPropertyChanged));
    /// <summary>
    /// Identifies the CalendarItemStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarItemStyleProperty = DependencyProperty.Register(nameof(CalendarItemStyle), typeof(Style), typeof(Calendar), new PropertyMetadata(null, OnVisualPropertyChanged));
#endregion
#region Routed Events
    /// <summary>
    /// Identifies the SelectedDateChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedDateChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectedDateChanged), RoutingStrategy.Bubble, typeof(EventHandler<SelectionChangedEventArgs>), typeof(Calendar));
    /// <summary>
    /// Identifies the DisplayDateChanged routed event.
    /// </summary>
    public static readonly RoutedEvent DisplayDateChangedEvent = EventManager.RegisterRoutedEvent(nameof(DisplayDateChanged), RoutingStrategy.Bubble, typeof(EventHandler<CalendarDateChangedEventArgs>), typeof(Calendar));
    /// <summary>
    /// Identifies the SelectedDatesChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedDatesChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectedDatesChanged), RoutingStrategy.Direct, typeof(EventHandler<SelectionChangedEventArgs>), typeof(Calendar));
    /// <summary>
    /// Occurs when the selected date changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged { add => AddHandler(SelectedDateChangedEvent, value); remove => RemoveHandler(SelectedDateChangedEvent, value); }

    /// <summary>
    /// Occurs when the display date changes.
    /// </summary>
    public event EventHandler<CalendarDateChangedEventArgs> DisplayDateChanged { add => AddHandler(DisplayDateChangedEvent, value); remove => RemoveHandler(DisplayDateChangedEvent, value); }

    /// <summary>
    /// Occurs when the SelectedDates collection changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectedDatesChanged { add => AddHandler(SelectedDatesChangedEvent, value); remove => RemoveHandler(SelectedDatesChangedEvent, value); }

    /// <summary>
    /// Occurs when DisplayMode changes.
    /// </summary>
    public event EventHandler<CalendarModeChangedEventArgs>? DisplayModeChanged;
    /// <summary>
    /// Occurs when SelectionMode changes.
    /// </summary>
    public event EventHandler<EventArgs>? SelectionModeChanged;
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the currently selected date.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DateTime? SelectedDate
    {
        get => (DateTime? )GetValue(SelectedDateProperty);
        set
        {
            if (value.HasValue)
            {
                if (SelectionMode == CalendarSelectionMode.None)
                {
                    throw new InvalidOperationException("A date cannot be selected when SelectionMode is None.");
                }

                if (!IsDateSelectableForSelection(value.Value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "The date is outside the selectable range or is blacked out.");
                }
            }

            SetValue(SelectedDateProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime DisplayDate { get => (DateTime)GetValue(DisplayDateProperty)!; set => SetValue(DisplayDateProperty, value); }

    /// <summary>
    /// Gets or sets the first date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateStart { get => (DateTime? )GetValue(DisplayDateStartProperty); set => SetValue(DisplayDateStartProperty, value); }

    /// <summary>
    /// Gets or sets the last date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateEnd { get => (DateTime? )GetValue(DisplayDateEndProperty); set => SetValue(DisplayDateEndProperty, value); }

    /// <summary>
    /// Gets or sets the first day of the week.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DayOfWeek FirstDayOfWeek { get => (DayOfWeek)(GetValue(FirstDayOfWeekProperty) ?? DayOfWeek.Sunday); set => SetValue(FirstDayOfWeekProperty, value); }

    /// <summary>
    /// Gets or sets a value indicating whether today is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTodayHighlighted { get => (bool)GetValue(IsTodayHighlightedProperty)!; set => SetValue(IsTodayHighlightedProperty, value); }

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public CalendarSelectionMode SelectionMode { get => (CalendarSelectionMode)GetValue(SelectionModeProperty)!; set => SetValue(SelectionModeProperty, value); }

    /// <summary>
    /// Gets or sets whether the calendar displays a month, year, or decade.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public CalendarMode DisplayMode { get => (CalendarMode)GetValue(DisplayModeProperty)!; set => SetValue(DisplayModeProperty, value); }

    /// <summary>
    /// Gets or sets the style used by month and year buttons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarButtonStyle { get => (Style? )GetValue(CalendarButtonStyleProperty); set => SetValue(CalendarButtonStyleProperty, value); }

    /// <summary>
    /// Gets or sets the style used by day buttons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarDayButtonStyle { get => (Style? )GetValue(CalendarDayButtonStyleProperty); set => SetValue(CalendarDayButtonStyleProperty, value); }

    /// <summary>
    /// Gets or sets the style used by the calendar item container.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarItemStyle { get => (Style? )GetValue(CalendarItemStyleProperty); set => SetValue(CalendarItemStyleProperty, value); }
    /// <summary>
    /// Gets the collection of selected dates.
    /// </summary>
    public SelectedDatesCollection SelectedDates => _selectedDates;
    /// <summary>
    /// Gets the collection of blackout dates.
    /// </summary>
    public CalendarBlackoutDatesCollection BlackoutDates => _blackoutDates;

#endregion
#region Private Fields
    private bool _isRevertingSelectedDate;
    private bool _isSynchronizingSelectedDates;
    private readonly CalendarBlackoutDatesCollection _blackoutDates;
    private readonly SelectedDatesCollection _selectedDates;
#endregion
#region Constructor
    /// <summary>
    /// Initializes a new instance of the <see cref = "Calendar"/> class.
    /// </summary>
    public Calendar()
    {
        _blackoutDates = new CalendarBlackoutDatesCollection(this);
        _selectedDates = new SelectedDatesCollection(this);
        Focusable = true;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        RefreshCalendarView();
        // Swipe horizontally to switch month on touch surfaces.
        IsManipulationEnabled = true;
        AddHandler(ManipulationCompletedEvent, new RoutedEventHandler(OnSwipeCompleted));
    }

    private const double CalendarSwipeThresholdDips = 80.0;
    private void OnSwipeCompleted(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e.Handled || e is not Input.ManipulationCompletedEventArgs args)
            return;
        var totalX = args.TotalManipulation?.Translation.X ?? 0;
        if (Math.Abs(totalX) < CalendarSwipeThresholdDips)
            return;
        if (totalX < 0)
        {
            NavigateToNextPeriod();
        }
        else
        {
            NavigateToPreviousPeriod();
        }

        e.Handled = true;
    }

#endregion
    /// <summary>
    /// Occurs when a date cell is clicked (used internally by DatePicker to close popup).
    /// </summary>
    internal event EventHandler<DateTime>? DateClicked;
#region Input Handling
    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled)
            return;
        var currentDate = SelectedDate ?? DisplayDate;
        switch (e.Key)
        {
            case Key.Left:
                SelectDate(AddDaysOrBoundary(currentDate, -1));
                e.Handled = true;
                break;
            case Key.Right:
                SelectDate(AddDaysOrBoundary(currentDate, 1));
                e.Handled = true;
                break;
            case Key.Up:
                SelectDate(AddDaysOrBoundary(currentDate, -7));
                e.Handled = true;
                break;
            case Key.Down:
                SelectDate(AddDaysOrBoundary(currentDate, 7));
                e.Handled = true;
                break;
            case Key.PageUp:
                NavigateToPreviousPeriod();
                e.Handled = true;
                break;
            case Key.PageDown:
                NavigateToNextPeriod();
                e.Handled = true;
                break;
            case Key.Home:
                SelectDate(new DateTime(currentDate.Year, currentDate.Month, 1));
                e.Handled = true;
                break;
            case Key.End:
                SelectDate(new DateTime(currentDate.Year, currentDate.Month, DateTime.DaysInMonth(currentDate.Year, currentDate.Month)));
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (DisplayMode == CalendarMode.Decade)
                {
                    SetCurrentValue(DisplayModeProperty, CalendarMode.Year);
                    e.Handled = true;
                }
                else if (DisplayMode == CalendarMode.Year)
                {
                    SetCurrentValue(DisplayModeProperty, CalendarMode.Month);
                    e.Handled = true;
                }

                break;
        }
    }

#endregion
#region Navigation
    internal void NavigateToPreviousPeriod()
    {
        SetCurrentValue(DisplayDateProperty, DisplayMode switch
        {
            CalendarMode.Month => AddMonthsOrBoundary(DisplayDate, -1),
            CalendarMode.Year => AddYearsOrBoundary(DisplayDate, -1),
            _ => AddYearsOrBoundary(DisplayDate, -10)});
    }

    internal void NavigateToNextPeriod()
    {
        SetCurrentValue(DisplayDateProperty, DisplayMode switch
        {
            CalendarMode.Month => AddMonthsOrBoundary(DisplayDate, 1),
            CalendarMode.Year => AddYearsOrBoundary(DisplayDate, 1),
            _ => AddYearsOrBoundary(DisplayDate, 10)});
    }

    private void SelectDate(DateTime date)
    {
        if (!IsDateSelectableForInteraction(date))
            return;
        // Update display date if needed
        if (date.Month != DisplayDate.Month || date.Year != DisplayDate.Year)
        {
            SetCurrentValue(DisplayDateProperty, new DateTime(date.Year, date.Month, 1));
        }

        switch (SelectionMode)
        {
            case CalendarSelectionMode.SingleDate:
            case CalendarSelectionMode.SingleRange:
                SetCurrentValue(SelectedDateProperty, date);
                break;
            case CalendarSelectionMode.MultipleRange:
                if (!SelectedDates.RemoveDay(date))
                {
                    SelectedDates.Add(date);
                }

                break;
            case CalendarSelectionMode.None:
                break;
        }
    }

    internal bool IsDateSelectableForSelection(DateTime date)
    {
        DateTime day = date.Date;
        return !BlackoutDates.Contains(day);
    }

    internal bool IsDateSelectableForInteraction(DateTime date)
    {
        if (DisplayDateStart.HasValue && date < DisplayDateStart.Value)
            return false;
        if (DisplayDateEnd.HasValue && date > DisplayDateEnd.Value)
            return false;
        return IsDateSelectableForSelection(date);
    }

    private DateTime ClampDisplayDate(DateTime date)
    {
        if (DisplayDateStart.HasValue && date < DisplayDateStart.Value)
        {
            return DisplayDateStart.Value;
        }

        if (DisplayDateEnd.HasValue && date > DisplayDateEnd.Value)
        {
            return DisplayDateEnd.Value;
        }

        return date;
    }

    private static DateTime AddMonthsOrBoundary(DateTime date, int months)
    {
        try
        {
            return date.AddMonths(months);
        }
        catch (ArgumentOutOfRangeException)
        {
            return months < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }
    }

    private static DateTime AddYearsOrBoundary(DateTime date, int years)
    {
        try
        {
            return date.AddYears(years);
        }
        catch (ArgumentOutOfRangeException)
        {
            return years < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }
    }

#endregion
#region Template
    public override void OnApplyTemplate()
    {
        ReleaseCalendarItem();
        base.OnApplyTemplate();
        _calendarItem = GetTemplateChild("PART_CalendarItem") as CalendarItem;
        if (_calendarItem is not null)
            _calendarItem.Owner = this;
        RefreshCalendarView();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseCalendarItem();
        base.OnTemplateContentClearing();
    }

    private void ReleaseCalendarItem()
    {
        if (_calendarItem is not null)
            _calendarItem.Owner = null;
        _calendarItem = null;
    }

    internal void ActivateDate(DateTime date)
    {
        if (!IsEnabled)
            return;
        if (DisplayMode == CalendarMode.Month)
        {
            if (!IsDateSelectableForInteraction(date))
                return;
            SelectDate(date);
            DateClicked?.Invoke(this, date);
        }
        else
        {
            SetCurrentValue(DisplayDateProperty, date);
            SetCurrentValue(DisplayModeProperty, DisplayMode == CalendarMode.Decade ? CalendarMode.Year : CalendarMode.Month);
        }
    }

    private static DateTime AddDaysOrBoundary(DateTime date, int days)
    {
        try
        {
            return date.AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return days < 0 ? DateTime.MinValue : DateTime.MaxValue;
        }
    }

#endregion
#region Event Hooks
    /// <summary>
    /// Raises the DisplayDateChanged event.
    /// </summary>
    protected virtual void OnDisplayDateChanged(CalendarDateChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the DisplayModeChanged event.
    /// </summary>
    protected virtual void OnDisplayModeChanged(CalendarModeChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        DisplayModeChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the SelectedDatesChanged event.
    /// </summary>
    protected virtual void OnSelectedDatesChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the SelectionModeChanged event.
    /// </summary>
    protected virtual void OnSelectionModeChanged(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectionModeChanged?.Invoke(this, e);
    }

#endregion
#region Property Changed Callbacks
    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            if (calendar._isRevertingSelectedDate)
            {
                return;
            }

            DateTime? newDate = (DateTime? )e.NewValue;
            if (newDate.HasValue && (calendar.SelectionMode == CalendarSelectionMode.None || !calendar.IsDateSelectableForSelection(newDate.Value)))
            {
                calendar._isRevertingSelectedDate = true;
                try
                {
                    calendar.SetCurrentValue(SelectedDateProperty, e.OldValue);
                }
                finally
                {
                    calendar._isRevertingSelectedDate = false;
                }

                if (calendar.SelectionMode == CalendarSelectionMode.None)
                {
                    throw new InvalidOperationException("A date cannot be selected when SelectionMode is None.");
                }

                throw new ArgumentOutOfRangeException(nameof(SelectedDate), "The date is outside the selectable range or is blacked out.");
            }

            if (!calendar._isSynchronizingSelectedDates)
            {
                calendar._selectedDates.ReplaceFromSelectedDate(newDate);
            }

            calendar.RefreshCalendarView();
            var args = new SelectionChangedEventArgs(SelectedDateChangedEvent, e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(), e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>());
            calendar.RaiseEvent(args);
        }
    }

    private static void OnDisplayDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.RefreshCalendarView();
            var args = new CalendarDateChangedEventArgs(DisplayDateChangedEvent, (DateTime? )e.OldValue, (DateTime? )e.NewValue);
            calendar.OnDisplayDateChanged(args);
        }
    }

    private static object? CoerceDisplayDate(DependencyObject d, object? baseValue)
    {
        var calendar = (Calendar)d;
        if (baseValue is not DateTime displayDate)
        {
            return baseValue;
        }

        return calendar.ClampDisplayDate(displayDate);
    }

    private static object? CoerceDisplayDateStart(DependencyObject d, object? baseValue)
    {
        var calendar = (Calendar)d;
        DateTime? requestedStart = (DateTime? )baseValue;
        DateTime? minimumSelectedDate = calendar.SelectedDates.MinimumDate;
        if (requestedStart.HasValue && minimumSelectedDate.HasValue && requestedStart.Value > minimumSelectedDate.Value)
        {
            return minimumSelectedDate;
        }

        return requestedStart;
    }

    private static object? CoerceDisplayDateEnd(DependencyObject d, object? baseValue)
    {
        var calendar = (Calendar)d;
        DateTime? requestedEnd = (DateTime? )baseValue;
        if (!requestedEnd.HasValue)
        {
            return null;
        }

        if (calendar.DisplayDateStart.HasValue && requestedEnd.Value < calendar.DisplayDateStart.Value)
        {
            return calendar.DisplayDateStart;
        }

        DateTime? maximumSelectedDate = calendar.SelectedDates.MaximumDate;
        if (maximumSelectedDate.HasValue && requestedEnd.Value < maximumSelectedDate.Value)
        {
            return maximumSelectedDate;
        }

        return requestedEnd;
    }

    private static void OnDisplayDateStartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.CoerceValue(DisplayDateEndProperty);
            calendar.CoerceValue(DisplayDateProperty);
            calendar.RefreshCalendarView();
        }
    }

    private static void OnDisplayDateEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.CoerceValue(DisplayDateProperty);
            calendar.RefreshCalendarView();
        }
    }

    private static void OnFirstDayOfWeekChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.RefreshCalendarView();
        }
    }

    private static void OnDisplayModePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.RefreshCalendarView();
            calendar.OnDisplayModeChanged(new CalendarModeChangedEventArgs((CalendarMode)e.OldValue!, (CalendarMode)e.NewValue!));
        }
    }

    private static void OnSelectionModePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.SelectedDates.Clear();
            calendar.OnSelectionModeChanged(EventArgs.Empty);
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.RefreshCalendarView();
        }
    }

    private static bool IsValidDisplayMode(object? value) => value is CalendarMode.Month or CalendarMode.Year or CalendarMode.Decade;
    private static bool IsValidFirstDayOfWeek(object? value) => value is DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday;
    private static bool IsValidSelectionMode(object? value) => value is CalendarSelectionMode.SingleDate or CalendarSelectionMode.SingleRange or CalendarSelectionMode.MultipleRange or CalendarSelectionMode.None;
    internal void OnSelectedDatesCollectionChanged(IReadOnlyList<DateTime> removedDates, IReadOnlyList<DateTime> addedDates)
    {
        DateTime? selectedDate = SelectedDates.Count == 0 ? null : SelectedDates[0];
        _isSynchronizingSelectedDates = true;
        try
        {
            SetCurrentValue(SelectedDateProperty, selectedDate);
        }
        finally
        {
            _isSynchronizingSelectedDates = false;
        }

        CoerceValue(DisplayDateStartProperty);
        CoerceValue(DisplayDateEndProperty);
        CoerceValue(DisplayDateProperty);
        var removed = removedDates.Select(static date => (object)date).ToArray();
        var added = addedDates.Select(static date => (object)date).ToArray();
        OnSelectedDatesChanged(new SelectionChangedEventArgs(SelectedDatesChangedEvent, removed, added));
        RefreshCalendarView();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == ForegroundProperty)
            RefreshCalendarView();
    }

    private Brush? ResolveItemForeground()
    {
        if (HasLocalValue(ForegroundProperty))
            return Foreground;
        var visited = new HashSet<Style>();
        Brush? Read(Style? style)
        {
            if (style is null || !visited.Add(style))
                return null;
            var result = Read(style.BasedOn);
            foreach (var setter in style.Setters.OfType<Setter>())
                if (setter.Property == ForegroundProperty && setter.TargetName is null && setter.Value is Brush brush)
                    result = brush;
            return result;
        }

        return Read(CalendarItemStyle) ?? Foreground;
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        RefreshCalendarView();
    }

    internal void RefreshCalendarView()
    {
        SetValue(CalendarItemForegroundPropertyKey, ResolveItemForeground());
        _calendarItem?.Refresh();
        VisualStateManager.GoToState(this, DisplayMode.ToString(), false);
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        InvalidateVisual();
    }
#endregion
}

/// <summary>
/// Specifies the selection mode for a Calendar.
/// </summary>
public enum CalendarSelectionMode
{
    /// <summary>
    /// Only a single date can be selected.
    /// </summary>
    SingleDate,
    /// <summary>
    /// A range of dates can be selected.
    /// </summary>
    SingleRange,
    /// <summary>
    /// Multiple dates or ranges can be selected.
    /// </summary>
    MultipleRange,
    /// <summary>
    /// No selection is allowed.
    /// </summary>
    None
}

/// <summary>
/// Provides data for the DisplayDateChanged event.
/// </summary>
public sealed class CalendarDateChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the previous display date.
    /// </summary>
    public DateTime? RemovedDate { get; }
    /// <summary>
    /// Gets the new display date.
    /// </summary>
    public DateTime? AddedDate { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref = "CalendarDateChangedEventArgs"/> class.
    /// </summary>
    public CalendarDateChangedEventArgs(RoutedEvent routedEvent, DateTime? removedDate, DateTime? addedDate)
    {
        RoutedEvent = routedEvent;
        RemovedDate = removedDate;
        AddedDate = addedDate;
    }
}
