using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

namespace Jalium.UI.Controls.Primitives;
/// <summary>Templated navigation and date views for a Calendar.</summary>
[TemplatePart(Name = "PART_HeaderButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_PreviousButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_NextButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_MonthView", Type = typeof(Grid))]
[TemplatePart(Name = "PART_YearView", Type = typeof(Grid))]
[TemplatePart(Name = "PART_DayTitles", Type = typeof(ItemsControl))]
public class CalendarItem : Control
{
    public static ComponentResourceKey DayTitleTemplateResourceKey { get; } = new(typeof(CalendarItem), "DayTitleTemplate");

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(nameof(DisplayMode), typeof(CalendarMode), typeof(CalendarItem), new PropertyMetadata(CalendarMode.Month, OnPresentationChanged), value => value is CalendarMode.Month or CalendarMode.Year or CalendarMode.Decade);
    public static readonly DependencyProperty DisplayDateProperty = DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(CalendarItem), new PropertyMetadata(DateTime.Today, OnPresentationChanged));
    private static readonly DependencyPropertyKey HeaderTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HeaderText), typeof(string), typeof(CalendarItem), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty HeaderTextProperty = HeaderTextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanNavigatePreviousPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanNavigatePrevious), typeof(bool), typeof(CalendarItem), new PropertyMetadata(true));
    public static readonly DependencyProperty CanNavigatePreviousProperty = CanNavigatePreviousPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanNavigateNextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanNavigateNext), typeof(bool), typeof(CalendarItem), new PropertyMetadata(true));
    public static readonly DependencyProperty CanNavigateNextProperty = CanNavigateNextPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey DayItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DayItems), typeof(IReadOnlyList<CalendarDateItem>), typeof(CalendarItem), new PropertyMetadata(null));
    public static readonly DependencyProperty DayItemsProperty = DayItemsPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PeriodItemsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PeriodItems), typeof(IReadOnlyList<CalendarDateItem>), typeof(CalendarItem), new PropertyMetadata(null));
    public static readonly DependencyProperty PeriodItemsProperty = PeriodItemsPropertyKey.DependencyProperty;
    private CalendarDateItem[] _days = [], _periods = [];
    private DateCommand? _dateCommand;
    private static readonly DependencyPropertyKey DayTitleFontSizePropertyKey = DependencyProperty.RegisterReadOnly(nameof(DayTitleFontSize), typeof(double), typeof(CalendarItem), new PropertyMetadata(11.0));
    public static readonly DependencyProperty DayTitleFontSizeProperty = DayTitleFontSizePropertyKey.DependencyProperty;
    public double DayTitleFontSize => (double)GetValue(DayTitleFontSizeProperty)!;

    private static readonly DependencyPropertyKey DayGridHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DayGridHeight), typeof(double), typeof(CalendarItem), new PropertyMetadata(192.0));
    public static readonly DependencyProperty DayGridHeightProperty = DayGridHeightPropertyKey.DependencyProperty;
    public double DayGridHeight => (double)GetValue(DayGridHeightProperty)!;

    private static readonly DependencyPropertyKey DayTitleHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DayTitleHeight), typeof(double), typeof(CalendarItem), new PropertyMetadata(24.0));
    public static readonly DependencyProperty DayTitleHeightProperty = DayTitleHeightPropertyKey.DependencyProperty;
    public double DayTitleHeight => (double)GetValue(DayTitleHeightProperty)!;

    private static readonly DependencyPropertyKey DisplayBodyHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DisplayBodyHeight), typeof(double), typeof(CalendarItem), new PropertyMetadata(216.0));
    public static readonly DependencyProperty DisplayBodyHeightProperty = DisplayBodyHeightPropertyKey.DependencyProperty;
    public double DisplayBodyHeight => (double)GetValue(DisplayBodyHeightProperty)!;

    private readonly ObservableCollection<string> _dayTitles = [];
    private Calendar? _owner;
    private bool _partsActive, _refreshing;
    static CalendarItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CalendarItem), new PropertyMetadata(typeof(CalendarItem)));
    }

    public CalendarItem()
    {
        ResetItems();
        DayTitles = new ReadOnlyObservableCollection<string>(_dayTitles);
        Refresh();
    }

    public CalendarMode DisplayMode { get => (CalendarMode)GetValue(DisplayModeProperty)!; set => SetValue(DisplayModeProperty, value); }
    public DateTime DisplayDate { get => (DateTime)GetValue(DisplayDateProperty)!; set => SetValue(DisplayDateProperty, value); }
    public string HeaderText => (string)GetValue(HeaderTextProperty)!;
    public bool CanNavigatePrevious => (bool)GetValue(CanNavigatePreviousProperty)!;
    public bool CanNavigateNext => (bool)GetValue(CanNavigateNextProperty)!;
    public IReadOnlyList<CalendarDateItem> DayItems => (IReadOnlyList<CalendarDateItem>)GetValue(DayItemsProperty)!;
    public IReadOnlyList<CalendarDateItem> PeriodItems => (IReadOnlyList<CalendarDateItem>)GetValue(PeriodItemsProperty)!;

    private void ResetItems()
    {
        _dateCommand = new DateCommand(this);
        _days = Enumerable.Range(0, 42).Select(_ => new CalendarDateItem(_dateCommand)).ToArray();
        _periods = Enumerable.Range(0, 12).Select(_ => new CalendarDateItem(_dateCommand)).ToArray();
        SetValue(DayItemsPropertyKey, Array.AsReadOnly(_days));
        SetValue(PeriodItemsPropertyKey, Array.AsReadOnly(_periods));
    }

    public ReadOnlyObservableCollection<string> DayTitles { get; }
    public Button? HeaderButton { get; private set; }
    public Button? PreviousButton { get; private set; }
    public Button? NextButton { get; private set; }
    public Grid? MonthView { get; private set; }
    public Grid? YearView { get; private set; }

    public Calendar? Owner
    {
        get => _owner;
        internal set
        {
            if (ReferenceEquals(_owner, value))
                return;
            _owner = value;
            Refresh();
        }
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Calendar);
    public override void OnApplyTemplate()
    {
        ReleaseParts();
        ResetItems();
        base.OnApplyTemplate();
        HeaderButton = GetTemplateChild("PART_HeaderButton") as Button;
        PreviousButton = GetTemplateChild("PART_PreviousButton") as Button;
        NextButton = GetTemplateChild("PART_NextButton") as Button;
        MonthView = GetTemplateChild("PART_MonthView") as Grid;
        YearView = GetTemplateChild("PART_YearView") as Grid;
        if (HeaderButton is not null)
            HeaderButton.Click += OnHeaderClick;
        if (PreviousButton is not null)
            PreviousButton.Click += OnPreviousClick;
        if (NextButton is not null)
            NextButton.Click += OnNextClick;
        if (GetTemplateChild("PART_DayTitles")is ItemsControl titles && TryFindResource(DayTitleTemplateResourceKey)is DataTemplate titleTemplate)
            titles.SetCurrentValue(ItemsControl.ItemTemplateProperty, titleTemplate);
        _partsActive = true;
        Refresh();
    }

    internal override void OnTemplateContentClearing()
    {
        ReleaseParts();
        base.OnTemplateContentClearing();
    }

    private void ReleaseParts()
    {
        _partsActive = false;
        if (HeaderButton is not null)
            HeaderButton.Click -= OnHeaderClick;
        if (PreviousButton is not null)
            PreviousButton.Click -= OnPreviousClick;
        if (NextButton is not null)
            NextButton.Click -= OnNextClick;
        HeaderButton = PreviousButton = NextButton = null;
        MonthView = YearView = null;
    }

    public void UpdateHeader(string text) => SetValue(HeaderTextPropertyKey, text ?? string.Empty);
    internal void Refresh()
    {
        if (_refreshing || _days is null)
            return;
        _refreshing = true;
        try
        {
            var date = Owner?.DisplayDate ?? DisplayDate;
            var mode = Owner?.DisplayMode ?? DisplayMode;
            var culture = CultureInfo.CurrentCulture;
            int decadeStart = Math.Max(1, date.Year - date.Year % 10);
            SetValue(HeaderTextPropertyKey, mode switch
            {
                CalendarMode.Month => date.ToString("Y", culture),
                CalendarMode.Year => date.Year.ToString(culture),
                _ => $"{decadeStart}-{Math.Min(9999, decadeStart + 9)}"});
            int firstDay = (int)(Owner?.FirstDayOfWeek ?? culture.DateTimeFormat.FirstDayOfWeek);
            for (int column = 0; column < 7; column++)
            {
                string title = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((firstDay + column) % 7));
                if (_dayTitles.Count <= column)
                    _dayTitles.Add(title);
                else if (_dayTitles[column] != title)
                    _dayTitles[column] = title;
            }

            var first = new DateTime(date.Year, date.Month, 1);
            int offset = ((int)first.DayOfWeek - firstDay + 7) % 7;
            for (int index = 0; index < 42; index++)
            {
                bool valid = TryAddDays(first, index - offset, out var day);
                bool blackedOut = Owner?.BlackoutDates.Contains(day) ?? false;
                _days[index].Update(Owner, day, valid ? day.Day.ToString(culture) : string.Empty, valid ? day.ToString("D", culture) : string.Empty, valid && (Owner?.SelectedDates.Any(value => value.Date == day.Date) ?? false), valid && day.Date == DateTime.Today && (Owner?.IsTodayHighlighted ?? true), blackedOut, day.Month != date.Month || day.Year != date.Year, valid && !blackedOut && (Owner?.IsDateSelectableForInteraction(day) ?? true), Owner?.CalendarDayButtonStyle);
            }

            int firstYear = Math.Clamp(date.Year - date.Year % 10 - 1, 1, 9988);
            for (int index = 0; index < 12; index++)
            {
                var period = mode == CalendarMode.Year ? new DateTime(date.Year, index + 1, 1) : new DateTime(firstYear + index, 1, 1);
                var end = mode == CalendarMode.Year ? new DateTime(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month)) : new DateTime(period.Year, 12, 31);
                bool enabled = Owner?.DisplayDateStart is not DateTime start || end.Date >= start.Date;
                enabled &= Owner?.DisplayDateEnd is not DateTime finish || period.Date <= finish.Date;
                bool selected = Owner?.SelectedDates.Any(value => value.Year == period.Year && (mode != CalendarMode.Year || value.Month == period.Month)) ?? false;
                string text = mode == CalendarMode.Year ? culture.DateTimeFormat.GetAbbreviatedMonthName(period.Month) : period.Year.ToString(culture);
                _periods[index].Update(Owner, period, text, mode == CalendarMode.Year ? period.ToString("Y", culture) : text, selected, false, false, mode == CalendarMode.Decade && (period.Year < decadeStart || period.Year > decadeStart + 9), enabled, Owner?.CalendarButtonStyle);
            }

            int Period(DateTime value) => mode == CalendarMode.Month ? (value.Year - 1) * 12 + value.Month - 1 : mode == CalendarMode.Year ? value.Year : value.Year / 10;
            SetValue(CanNavigatePreviousPropertyKey, Period(date) > Period(Owner?.DisplayDateStart ?? DateTime.MinValue));
            SetValue(CanNavigateNextPropertyKey, Period(date) < Period(Owner?.DisplayDateEnd ?? DateTime.MaxValue));
            SyncTypography();
            VisualStateManager.GoToState(this, mode.ToString(), false);
            _dateCommand?.NotifyCanExecuteChanged();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SyncTypography()
    {
        SetValue(DayTitleFontSizePropertyKey, FontSize * (11.0 / 14.0));
        SetValue(DayGridHeightPropertyKey, Math.Max(32, Math.Ceiling(FontSize * 1.3 + 4)) * 6);
        SetValue(DayTitleHeightPropertyKey, Math.Max(24, Math.Ceiling(DayTitleFontSize * 1.3 + 4)));
        SetValue(DisplayBodyHeightPropertyKey, DayGridHeight + DayTitleHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncTypography();
        return base.MeasureOverride(availableSize);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == FontSizeProperty)
            SyncTypography();
    }

    private static bool TryAddDays(DateTime date, int days, out DateTime result)
    {
        try
        {
            result = date.AddDays(days);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = days < 0 ? DateTime.MinValue : DateTime.MaxValue;
            return false;
        }
    }

    private void OnHeaderClick(object sender, RoutedEventArgs args)
    {
        if (!IsEnabled || !_partsActive)
            return;
        var next = (Owner?.DisplayMode ?? DisplayMode) == CalendarMode.Month ? CalendarMode.Year : CalendarMode.Decade;
        if (Owner is { } owner)
            owner.SetCurrentValue(Calendar.DisplayModeProperty, next);
        else
            SetCurrentValue(DisplayModeProperty, next);
    }

    private void OnPreviousClick(object sender, RoutedEventArgs args)
    {
        if (IsEnabled && _partsActive && CanNavigatePrevious)
            Navigate(-1);
    }

    private void OnNextClick(object sender, RoutedEventArgs args)
    {
        if (IsEnabled && _partsActive && CanNavigateNext)
            Navigate(1);
    }

    private void Navigate(int direction)
    {
        if (Owner is { } owner)
        {
            if (direction < 0)
                owner.NavigateToPreviousPeriod();
            else
                owner.NavigateToNextPeriod();
        }
        else
        {
            try
            {
                SetCurrentValue(DisplayDateProperty, DisplayMode == CalendarMode.Month ? DisplayDate.AddMonths(direction) : DisplayDate.AddYears(direction * (DisplayMode == CalendarMode.Year ? 1 : 10)));
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
    }

    private bool CanSelect(object? parameter) => IsEnabled && _partsActive && parameter is CalendarDateItem cell && cell.IsEnabled && ((Owner?.DisplayMode ?? DisplayMode) == CalendarMode.Month ? _days.Contains(cell) : _periods.Contains(cell));
    private void Select(object? parameter)
    {
        if (!CanSelect(parameter))
            return;
        var cell = (CalendarDateItem)parameter!;
        if (Owner is { } owner)
            owner.ActivateDate(cell.Date);
        else if (DisplayMode != CalendarMode.Month)
        {
            SetCurrentValue(DisplayDateProperty, cell.Date);
            SetCurrentValue(DisplayModeProperty, DisplayMode == CalendarMode.Decade ? CalendarMode.Year : CalendarMode.Month);
        }
    }

    private sealed class DateCommand(CalendarItem item) : ICommand
    {
        public bool CanExecute(object? parameter) => item.CanSelect(parameter);
        public void Execute(object? parameter) => item.Select(parameter);
        public event EventHandler? CanExecuteChanged;
        internal void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnPresentationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((CalendarItem)sender).Refresh();
}
