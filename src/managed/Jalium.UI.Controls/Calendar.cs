using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a calendar control for selecting dates.
/// </summary>
public class Calendar : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.CalendarAutomationPeer(this);
    }

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_headerBgBrush = new(ThemeColors.SecondaryBackground);
    private static readonly SolidColorBrush s_hoverBrush = new(ThemeColors.HighlightBackground);
    private static readonly SolidColorBrush s_arrowNormalBrush = new(ThemeColors.TextSecondary);
    private static readonly SolidColorBrush s_dayHeaderBrush = new(ThemeColors.TextSecondary);
    private static readonly SolidColorBrush s_accentBrush = new(ThemeColors.Accent);
    private static readonly SolidColorBrush s_unselectableBrush = new(ThemeColors.TextDisabled);
    private static readonly SolidColorBrush s_otherMonthBrush = new(ThemeColors.TextDisabled);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedDate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null, OnSelectedDateChanged));

    /// <summary>
    /// Identifies the DisplayDate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(Calendar),
            new PropertyMetadata(DateTime.Today, OnDisplayDateChanged, CoerceDisplayDate));

    /// <summary>
    /// Identifies the DisplayDateStart dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null, OnDisplayDateStartChanged, CoerceDisplayDateStart));

    /// <summary>
    /// Identifies the DisplayDateEnd dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null, OnDisplayDateEndChanged, CoerceDisplayDateEnd));

    /// <summary>
    /// Identifies the FirstDayOfWeek dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(Calendar),
            new PropertyMetadata(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, OnFirstDayOfWeekChanged),
            IsValidFirstDayOfWeek);

    /// <summary>
    /// Identifies the IsTodayHighlighted dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTodayHighlightedProperty =
        DependencyProperty.Register(nameof(IsTodayHighlighted), typeof(bool), typeof(Calendar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(CalendarSelectionMode), typeof(Calendar),
            new PropertyMetadata(CalendarSelectionMode.SingleDate, OnSelectionModePropertyChanged),
            IsValidSelectionMode);

    /// <summary>
    /// Identifies the DisplayMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty DisplayModeProperty =
        DependencyProperty.Register(nameof(DisplayMode), typeof(CalendarMode), typeof(Calendar),
            new FrameworkPropertyMetadata(
                CalendarMode.Month,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnDisplayModePropertyChanged),
            IsValidDisplayMode);

    /// <summary>
    /// Identifies the CalendarButtonStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarButtonStyleProperty =
        DependencyProperty.Register(nameof(CalendarButtonStyle), typeof(Style), typeof(Calendar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CalendarDayButtonStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarDayButtonStyleProperty =
        DependencyProperty.Register(nameof(CalendarDayButtonStyle), typeof(Style), typeof(Calendar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CalendarItemStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CalendarItemStyleProperty =
        DependencyProperty.Register(nameof(CalendarItemStyle), typeof(Style), typeof(Calendar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectedDateChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedDateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedDateChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(Calendar));

    /// <summary>
    /// Identifies the DisplayDateChanged routed event.
    /// </summary>
    public static readonly RoutedEvent DisplayDateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(DisplayDateChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<CalendarDateChangedEventArgs>), typeof(Calendar));

    /// <summary>
    /// Identifies the SelectedDatesChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedDatesChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedDatesChanged), RoutingStrategy.Direct,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(Calendar));

    /// <summary>
    /// Occurs when the selected date changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectedDateChanged
    {
        add => AddHandler(SelectedDateChangedEvent, value);
        remove => RemoveHandler(SelectedDateChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the display date changes.
    /// </summary>
    public event EventHandler<CalendarDateChangedEventArgs> DisplayDateChanged
    {
        add => AddHandler(DisplayDateChangedEvent, value);
        remove => RemoveHandler(DisplayDateChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the SelectedDates collection changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectedDatesChanged
    {
        add => AddHandler(SelectedDatesChangedEvent, value);
        remove => RemoveHandler(SelectedDatesChangedEvent, value);
    }

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
        get => (DateTime?)GetValue(SelectedDateProperty);
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
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "The date is outside the selectable range or is blacked out.");
                }
            }

            SetValue(SelectedDateProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime DisplayDate
    {
        get => (DateTime)GetValue(DisplayDateProperty)!;
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>
    /// Gets or sets the first date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the last date to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    /// <summary>
    /// Gets or sets the first day of the week.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)(GetValue(FirstDayOfWeekProperty) ?? DayOfWeek.Sunday);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether today is highlighted.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTodayHighlighted
    {
        get => (bool)GetValue(IsTodayHighlightedProperty)!;
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public CalendarSelectionMode SelectionMode
    {
        get => (CalendarSelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the calendar displays a month, year, or decade.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public CalendarMode DisplayMode
    {
        get => (CalendarMode)GetValue(DisplayModeProperty)!;
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the style used by month and year buttons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarButtonStyle
    {
        get => (Style?)GetValue(CalendarButtonStyleProperty);
        set => SetValue(CalendarButtonStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style used by day buttons.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarDayButtonStyle
    {
        get => (Style?)GetValue(CalendarDayButtonStyleProperty);
        set => SetValue(CalendarDayButtonStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style used by the calendar item container.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style? CalendarItemStyle
    {
        get => (Style?)GetValue(CalendarItemStyleProperty);
        set => SetValue(CalendarItemStyleProperty, value);
    }

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

    private const double CellWidth = 32;
    private const double CellHeight = 32;
    private const double HeaderHeight = 36;
    private const double DayHeaderHeight = 24;
    private const int Rows = 6;
    private const int Columns = 7;
    private Rect[,] _dayCells = new Rect[Rows, Columns];
    private DateTime[,] _dayDates = new DateTime[Rows, Columns];
    private bool[,] _dayDateIsValid = new bool[Rows, Columns];
    private Rect _prevButtonRect;
    private Rect _nextButtonRect;
    private Rect _monthYearRect;
    private int _hoveredRow = -1;
    private int _hoveredCol = -1;
    private bool _isHoveringPrev;
    private bool _isHoveringNext;
    private bool _isRevertingSelectedDate;
    private bool _isSynchronizingSelectedDates;

    private readonly CalendarBlackoutDatesCollection _blackoutDates;
    private readonly SelectedDatesCollection _selectedDates;

    // Cached pens
    private Pen? _arrowPen;
    private Brush? _arrowPenBrush;
    private Pen? _accentPen;
    private Brush? _accentPenBrush;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Calendar"/> class.
    /// </summary>
    public Calendar()
    {
        _blackoutDates = new CalendarBlackoutDatesCollection(this);
        _selectedDates = new SelectedDatesCollection(this);

        Focusable = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));

        // Swipe horizontally to switch month on touch surfaces.
        IsManipulationEnabled = true;
        AddHandler(ManipulationCompletedEvent, new RoutedEventHandler(OnSwipeCompleted));

        CalculateDayGrid();
    }

    private const double CalendarSwipeThresholdDips = 80.0;

    private void OnSwipeCompleted(object sender, RoutedEventArgs e)
    {
        if (e.Handled || e is not Input.ManipulationCompletedEventArgs args) return;
        var totalX = args.TotalManipulation?.Translation.X ?? 0;
        if (Math.Abs(totalX) < CalendarSwipeThresholdDips) return;

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

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);

            // Check navigation buttons
            if (_prevButtonRect.Contains(position))
            {
                NavigateToPreviousPeriod();
                e.Handled = true;
                return;
            }

            if (_nextButtonRect.Contains(position))
            {
                NavigateToNextPeriod();
                e.Handled = true;
                return;
            }

            if (_monthYearRect.Contains(position))
            {
                DisplayMode = DisplayMode switch
                {
                    CalendarMode.Month => CalendarMode.Year,
                    CalendarMode.Year => CalendarMode.Decade,
                    _ => CalendarMode.Decade
                };
                e.Handled = true;
                return;
            }

            int rowCount = DisplayMode == CalendarMode.Month ? Rows : 3;
            int columnCount = DisplayMode == CalendarMode.Month ? Columns : 4;
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columnCount; col++)
                {
                    if (!_dayCells[row, col].Contains(position))
                    {
                        continue;
                    }

                    DateTime date = _dayDates[row, col];
                    if (DisplayMode == CalendarMode.Month)
                    {
                        if (_dayDateIsValid[row, col] && IsDateSelectableForInteraction(date))
                        {
                            SelectDate(date);
                            DateClicked?.Invoke(this, date);
                        }
                    }
                    else if (DisplayMode == CalendarMode.Year)
                    {
                        DisplayDate = date;
                        DisplayMode = CalendarMode.Month;
                    }
                    else
                    {
                        DisplayDate = date;
                        DisplayMode = CalendarMode.Year;
                    }

                    e.Handled = true;
                    return;
                }
            }
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!IsEnabled) return;

        var position = e.GetPosition(this);
        var newRow = -1;
        var newCol = -1;

        int rowCount = DisplayMode == CalendarMode.Month ? Rows : 3;
        int columnCount = DisplayMode == CalendarMode.Month ? Columns : 4;
        for (int row = 0; row < rowCount; row++)
        {
            for (int col = 0; col < columnCount; col++)
            {
                if (_dayCells[row, col].Contains(position))
                {
                    newRow = row;
                    newCol = col;
                    break;
                }
            }
            if (newRow >= 0) break;
        }

        var hoverChanged = newRow != _hoveredRow || newCol != _hoveredCol;
        _hoveredRow = newRow;
        _hoveredCol = newCol;

        var newHoveringPrev = _prevButtonRect.Contains(position);
        var newHoveringNext = _nextButtonRect.Contains(position);
        if (newHoveringPrev != _isHoveringPrev || newHoveringNext != _isHoveringNext)
        {
            _isHoveringPrev = newHoveringPrev;
            _isHoveringNext = newHoveringNext;
            hoverChanged = true;
        }

        if (hoverChanged)
            InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (_hoveredRow != -1 || _hoveredCol != -1 || _isHoveringPrev || _isHoveringNext)
        {
            _hoveredRow = -1;
            _hoveredCol = -1;
            _isHoveringPrev = false;
            _isHoveringNext = false;
            InvalidateVisual();
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        var currentDate = SelectedDate ?? DisplayDate;

        switch (e.Key)
        {
            case Key.Left:
                SelectDate(currentDate.AddDays(-1));
                e.Handled = true;
                break;
            case Key.Right:
                SelectDate(currentDate.AddDays(1));
                e.Handled = true;
                break;
            case Key.Up:
                SelectDate(currentDate.AddDays(-7));
                e.Handled = true;
                break;
            case Key.Down:
                SelectDate(currentDate.AddDays(7));
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
                SelectDate(new DateTime(currentDate.Year, currentDate.Month,
                    DateTime.DaysInMonth(currentDate.Year, currentDate.Month)));
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (DisplayMode == CalendarMode.Decade)
                {
                    DisplayMode = CalendarMode.Year;
                    e.Handled = true;
                }
                else if (DisplayMode == CalendarMode.Year)
                {
                    DisplayMode = CalendarMode.Month;
                    e.Handled = true;
                }
                break;
        }
    }

    #endregion

    #region Navigation

    private void NavigateToPreviousPeriod()
    {
        DisplayDate = DisplayMode switch
        {
            CalendarMode.Month => AddMonthsOrBoundary(DisplayDate, -1),
            CalendarMode.Year => AddYearsOrBoundary(DisplayDate, -1),
            _ => AddYearsOrBoundary(DisplayDate, -10)
        };
    }

    private void NavigateToNextPeriod()
    {
        DisplayDate = DisplayMode switch
        {
            CalendarMode.Month => AddMonthsOrBoundary(DisplayDate, 1),
            CalendarMode.Year => AddYearsOrBoundary(DisplayDate, 1),
            _ => AddYearsOrBoundary(DisplayDate, 10)
        };
    }

    private void SelectDate(DateTime date)
    {
        if (!IsDateSelectableForInteraction(date))
            return;

        // Update display date if needed
        if (date.Month != DisplayDate.Month || date.Year != DisplayDate.Year)
        {
            DisplayDate = new DateTime(date.Year, date.Month, 1);
        }

        switch (SelectionMode)
        {
            case CalendarSelectionMode.SingleDate:
            case CalendarSelectionMode.SingleRange:
                SelectedDate = date;
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

    private bool IsDateSelectableForInteraction(DateTime date)
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

    #region Grid Calculation

    private void CalculateDayGrid()
    {
        var firstOfMonth = new DateTime(DisplayDate.Year, DisplayDate.Month, 1);
        // Calculate the day of week offset
        var firstDayOfWeek = (int)FirstDayOfWeek;
        var startDayOfWeek = (int)firstOfMonth.DayOfWeek;
        var offset = (startDayOfWeek - firstDayOfWeek + 7) % 7;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int dayOffset = row * Columns + col - offset;
                _dayDateIsValid[row, col] = TryAddDays(firstOfMonth, dayOffset, out DateTime date);
                _dayDates[row, col] = date;
            }
        }
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

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Columns * CellWidth + Padding.TotalWidth + BorderThickness.TotalWidth;
        var height = HeaderHeight + DayHeaderHeight + Rows * CellHeight +
                     Padding.TotalHeight + BorderThickness.TotalHeight;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;

        // CalendarItemStyle is normally consumed by PART_CalendarItem. This control uses
        // direct rendering, so consume the equivalent visual setters here.
        Brush? calendarBackground = GetStyleSetterValue(CalendarItemStyle, Control.BackgroundProperty) as Brush
            ?? Background;
        if (calendarBackground != null)
        {
            dc.DrawRoundedRectangle(calendarBackground, null, rect, cornerRadius);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            dc.DrawRoundedRectangle(null, pen, rect, cornerRadius);
        }

        var startX = padding.Left + BorderThickness.Left;
        var startY = padding.Top + BorderThickness.Top;

        // Draw header
        DrawHeader(dc, startX, startY);

        if (DisplayMode == CalendarMode.Month)
        {
            DrawDayHeaders(dc, startX, startY + HeaderHeight);
            DrawDayGrid(dc, startX, startY + HeaderHeight + DayHeaderHeight);
        }
        else
        {
            DrawPeriodGrid(dc, startX, startY + HeaderHeight);
        }
    }

    private void DrawHeader(DrawingContext dc, double x, double y)
    {
        var width = Columns * CellWidth;
        var headerRect = new Rect(x, y, width, HeaderHeight);

        // Draw header background
        Brush headerBackground = GetStyleSetterValue(CalendarButtonStyle, Control.BackgroundProperty) as Brush
            ?? ResolveCalendarBrush("ControlBackground", s_headerBgBrush);
        dc.DrawRectangle(headerBackground, null, headerRect);

        // Draw previous button
        _prevButtonRect = new Rect(x + 4, y + 4, 28, 28);
        DrawNavigationButton(dc, _prevButtonRect, false, _isHoveringPrev);

        // Draw next button
        _nextButtonRect = new Rect(x + width - 32, y + 4, 28, 28);
        DrawNavigationButton(dc, _nextButtonRect, true, _isHoveringNext);

        // Draw month/year text
        string monthYearText = DisplayMode switch
        {
            CalendarMode.Month => DisplayDate.ToString("MMMM yyyy"),
            CalendarMode.Year => DisplayDate.ToString("yyyy"),
            _ => GetDecadeHeader(DisplayDate.Year)
        };
        var formattedText = new FormattedText(monthYearText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 14)
        {
            Foreground = ResolveCalendarButtonForeground(),
            FontWeight = 600
        };
        TextMeasurement.MeasureText(formattedText);

        var textX = x + (width - formattedText.Width) / 2;
        var textY = y + (HeaderHeight - formattedText.Height) / 2;
        dc.DrawText(formattedText, new Point(textX, textY));

        _monthYearRect = new Rect(x + 32, y, Math.Max(0, width - 64), HeaderHeight);
    }

    // Chevron paths in 8×12 design space, cached once
    private static readonly PathGeometry s_chevronRight = CreateFrozenChevron("M 0,0 L 4,6 L 0,12");
    private static readonly PathGeometry s_chevronLeft = CreateFrozenChevron("M 4,0 L 0,6 L 4,12");

    private static PathGeometry CreateFrozenChevron(string pathData)
    {
        var geometry = (PathGeometry)Geometry.Parse(pathData);
        geometry.Freeze();
        return geometry;
    }

    private void DrawNavigationButton(DrawingContext dc, Rect rect, bool isNext, bool isHovered)
    {
        if (isHovered)
        {
            dc.DrawRoundedRectangle(ResolveCalendarBrush("HighlightBackground", s_hoverBrush), null, rect, new CornerRadius(4));
        }

        var arrowBrush = isHovered
            ? ResolveCalendarButtonForeground()
            : GetStyleSetterValue(CalendarButtonStyle, Control.ForegroundProperty) as Brush
                ?? ResolveCalendarBrush("TextSecondary", s_arrowNormalBrush);
        if (_arrowPen == null || _arrowPenBrush != arrowBrush)
        {
            _arrowPenBrush = arrowBrush;
            _arrowPen = new Pen(arrowBrush, 2);
        }

        var source = isNext ? s_chevronRight : s_chevronLeft;
        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var bounds = source.Bounds;
        var ox = cx - bounds.X - bounds.Width / 2;
        var oy = cy - bounds.Y - bounds.Height / 2;

        foreach (var figure in source.Figures)
        {
            var tf = new PathFigure
            {
                StartPoint = new Point(figure.StartPoint.X + ox, figure.StartPoint.Y + oy),
                IsClosed = figure.IsClosed,
                IsFilled = false
            };
            foreach (var seg in figure.Segments)
                if (seg is LineSegment ls)
                    tf.Segments.Add(new LineSegment(new Point(ls.Point.X + ox, ls.Point.Y + oy), ls.IsStroked));
            var geo = new PathGeometry();
            geo.Figures.Add(tf);
            dc.DrawGeometry(null, _arrowPen, geo);
        }
    }

    private void DrawDayHeaders(DrawingContext dc, double x, double y)
    {
        var dayNames = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
        var firstDayIndex = (int)FirstDayOfWeek;

        for (int col = 0; col < Columns; col++)
        {
            var dayIndex = (firstDayIndex + col) % 7;
            var dayName = dayNames[dayIndex];

            var formattedText = new FormattedText(dayName, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, 11)
            {
                Foreground = ResolveCalendarBrush("TextSecondary", s_dayHeaderBrush)
            };
            TextMeasurement.MeasureText(formattedText);

            var textX = x + col * CellWidth + (CellWidth - formattedText.Width) / 2;
            var textY = y + (DayHeaderHeight - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    private void DrawDayGrid(DrawingContext dc, double x, double y)
    {
        var today = DateTime.Today;

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                var cellX = x + col * CellWidth;
                var cellY = y + row * CellHeight;
                var cellRect = new Rect(cellX, cellY, CellWidth, CellHeight);
                _dayCells[row, col] = cellRect;

                var date = _dayDates[row, col];
                if (!_dayDateIsValid[row, col])
                {
                    continue;
                }
                var isCurrentMonth = date.Month == DisplayDate.Month;
                var isToday = date.Date == today;
                var isSelected = SelectedDate.HasValue && date.Date == SelectedDate.Value.Date;
                var isSelectable = IsDateSelectableForInteraction(date);

                var isHovered = row == _hoveredRow && col == _hoveredCol;
                DrawDayCell(dc, cellRect, date.Day, isCurrentMonth, isToday, isSelected, isSelectable, isHovered);
            }
        }
    }

    private void DrawDayCell(DrawingContext dc, Rect rect, int day, bool isCurrentMonth, bool isToday, bool isSelected, bool isSelectable, bool isHovered)
    {
        if (!isSelected && GetStyleSetterValue(CalendarDayButtonStyle, Control.BackgroundProperty) is Brush dayBackground)
        {
            dc.DrawEllipse(dayBackground, null,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }

        // Draw hover highlight
        if (isHovered && !isSelected && isSelectable)
        {
            dc.DrawEllipse(ResolveCalendarBrush("HighlightBackground", s_hoverBrush), null,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }

        // Draw selection or today highlight
        if (isSelected)
        {
            var accentBrush = ResolveCalendarBrush("AccentBrush", s_accentBrush);
            dc.DrawEllipse(accentBrush, null,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }
        else if (isToday && IsTodayHighlighted)
        {
            var accentBrush = ResolveCalendarBrush("AccentBrush", s_accentBrush);
            if (_accentPen == null || _accentPenBrush != accentBrush)
            {
                _accentPenBrush = accentBrush;
                _accentPen = new Pen(accentBrush, 2);
            }
            var accentPen = _accentPen;
            dc.DrawEllipse(null, accentPen,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }

        // Draw day number
        var dayText = day.ToString();
        Brush textBrush;

        if (isSelected)
        {
            textBrush = ResolveSelectedTextBrush();
        }
        else if (!isSelectable)
        {
            textBrush = ResolveCalendarBrush("TextDisabled", s_unselectableBrush);
        }
        else if (!isCurrentMonth)
        {
            textBrush = ResolveCalendarBrush("TextSecondary", s_otherMonthBrush);
        }
        else
        {
            textBrush = GetStyleSetterValue(CalendarDayButtonStyle, Control.ForegroundProperty) as Brush
                ?? ResolvePrimaryTextBrush();
        }

        var formattedText = new FormattedText(dayText, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 13)
        {
            Foreground = textBrush
        };
        TextMeasurement.MeasureText(formattedText);

        var textX = rect.X + (rect.Width - formattedText.Width) / 2;
        var textY = rect.Y + (rect.Height - formattedText.Height) / 2;
        dc.DrawText(formattedText, new Point(textX, textY));
    }

    private void DrawPeriodGrid(DrawingContext dc, double x, double y)
    {
        const int periodRows = 3;
        const int periodColumns = 4;
        double cellWidth = Columns * CellWidth / periodColumns;
        double cellHeight = Rows * CellHeight / periodRows;
        int firstYear = GetFirstDecadeCellYear(DisplayDate.Year);

        for (int row = 0; row < periodRows; row++)
        {
            for (int col = 0; col < periodColumns; col++)
            {
                int index = row * periodColumns + col;
                DateTime date;
                string label;
                bool isInactive;

                if (DisplayMode == CalendarMode.Year)
                {
                    int month = index + 1;
                    date = new DateTime(DisplayDate.Year, month, 1);
                    label = date.ToString("MMM");
                    isInactive = false;
                }
                else
                {
                    int year = firstYear + index;
                    date = new DateTime(year, 1, 1);
                    label = year.ToString();
                    int decadeStart = DisplayDate.Year - DisplayDate.Year % 10;
                    isInactive = year < decadeStart || year > Math.Min(DateTime.MaxValue.Year, decadeStart + 9);
                }

                var rect = new Rect(x + col * cellWidth, y + row * cellHeight, cellWidth, cellHeight);
                _dayCells[row, col] = rect;
                _dayDates[row, col] = date;

                bool isSelected = DisplayMode == CalendarMode.Year
                    ? date.Year == DisplayDate.Year && date.Month == DisplayDate.Month
                    : date.Year == DisplayDate.Year;
                bool isHovered = row == _hoveredRow && col == _hoveredCol;
                DrawPeriodCell(dc, rect, label, isSelected, isInactive, isHovered);
            }
        }
    }

    private void DrawPeriodCell(
        DrawingContext dc,
        Rect rect,
        string label,
        bool isSelected,
        bool isInactive,
        bool isHovered)
    {
        var highlightRect = ControlRenderGeometry.GetContentRect(rect, new Thickness(4, 8, 4, 8));
        if (isSelected)
        {
            dc.DrawRoundedRectangle(ResolveCalendarBrush("AccentBrush", s_accentBrush), null,
                highlightRect, new CornerRadius(6));
        }
        else if (isHovered)
        {
            dc.DrawRoundedRectangle(ResolveCalendarBrush("HighlightBackground", s_hoverBrush), null,
                highlightRect, new CornerRadius(6));
        }

        Brush foreground = isSelected
            ? ResolveSelectedTextBrush()
            : isInactive
                ? ResolveCalendarBrush("TextSecondary", s_otherMonthBrush)
                : ResolvePrimaryTextBrush();
        var text = new FormattedText(label, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName,
            FontSize > 0 ? FontSize : 13)
        {
            Foreground = foreground
        };
        TextMeasurement.MeasureText(text);
        dc.DrawText(text, new Point(
            rect.X + (rect.Width - text.Width) / 2,
            rect.Y + (rect.Height - text.Height) / 2));
    }

    private static string GetDecadeHeader(int year)
    {
        int start = Math.Max(DateTime.MinValue.Year, year - year % 10);
        int end = Math.Min(DateTime.MaxValue.Year, start + 9);
        return $"{start}-{end}";
    }

    private static int GetFirstDecadeCellYear(int year)
    {
        int decadeStart = year - year % 10;
        return Math.Clamp(decadeStart - 1, DateTime.MinValue.Year, DateTime.MaxValue.Year - 11);
    }

    private Brush ResolveCalendarBrush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }

    private Brush ResolvePrimaryTextBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return GetStyleSetterValue(CalendarItemStyle, Control.ForegroundProperty) as Brush
            ?? ResolveCalendarBrush("TextPrimary", s_whiteBrush);
    }

    private Brush ResolveCalendarButtonForeground() =>
        GetStyleSetterValue(CalendarButtonStyle, Control.ForegroundProperty) as Brush
        ?? ResolvePrimaryTextBrush();

    private static object? GetStyleSetterValue(Style? style, DependencyProperty property)
    {
        object? value = null;
        var visited = new HashSet<Style>();

        void Visit(Style? current)
        {
            if (current is null || !visited.Add(current))
            {
                return;
            }

            Visit(current.BasedOn);
            foreach (SetterBase setterBase in current.Setters)
            {
                if (setterBase is Setter setter && setter.TargetName is null &&
                    ReferenceEquals(setter.Property, property))
                {
                    value = setter.Value;
                }
            }
        }

        Visit(style);
        return value;
    }

    private Brush ResolveSelectedTextBrush()
    {
        return ResolveCalendarBrush("TextOnAccent", s_whiteBrush);
    }

    #endregion

    #region Protected Event Hooks

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

            DateTime? newDate = (DateTime?)e.NewValue;
            if (newDate.HasValue &&
                (calendar.SelectionMode == CalendarSelectionMode.None ||
                 !calendar.IsDateSelectableForSelection(newDate.Value)))
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

                throw new ArgumentOutOfRangeException(nameof(SelectedDate),
                    "The date is outside the selectable range or is blacked out.");
            }

            if (!calendar._isSynchronizingSelectedDates)
            {
                calendar._selectedDates.ReplaceFromSelectedDate(newDate);
            }

            calendar.InvalidateVisual();

            var args = new SelectionChangedEventArgs(SelectedDateChangedEvent,
                e.OldValue != null ? new[] { e.OldValue } : Array.Empty<object>(),
                e.NewValue != null ? new[] { e.NewValue } : Array.Empty<object>());
            calendar.RaiseEvent(args);
        }
    }

    private static void OnDisplayDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.CalculateDayGrid();
            calendar.InvalidateVisual();

            var args = new CalendarDateChangedEventArgs(DisplayDateChangedEvent,
                (DateTime?)e.OldValue, (DateTime?)e.NewValue);
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
        DateTime? requestedStart = (DateTime?)baseValue;
        DateTime? minimumSelectedDate = calendar.SelectedDates.MinimumDate;
        if (requestedStart.HasValue && minimumSelectedDate.HasValue &&
            requestedStart.Value > minimumSelectedDate.Value)
        {
            return minimumSelectedDate;
        }

        return requestedStart;
    }

    private static object? CoerceDisplayDateEnd(DependencyObject d, object? baseValue)
    {
        var calendar = (Calendar)d;
        DateTime? requestedEnd = (DateTime?)baseValue;
        if (!requestedEnd.HasValue)
        {
            return null;
        }

        if (calendar.DisplayDateStart.HasValue &&
            requestedEnd.Value < calendar.DisplayDateStart.Value)
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
            calendar._hoveredRow = -1;
            calendar._hoveredCol = -1;
            calendar.InvalidateVisual();
            calendar.OnDisplayModeChanged(new CalendarModeChangedEventArgs(
                (CalendarMode)e.OldValue!, (CalendarMode)e.NewValue!));
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
            calendar.InvalidateVisual();
        }
    }

    private static bool IsValidDisplayMode(object? value) =>
        value is CalendarMode.Month or CalendarMode.Year or CalendarMode.Decade;

    private static bool IsValidFirstDayOfWeek(object? value) =>
        value is DayOfWeek.Sunday or DayOfWeek.Monday or DayOfWeek.Tuesday or DayOfWeek.Wednesday or
            DayOfWeek.Thursday or DayOfWeek.Friday or DayOfWeek.Saturday;

    private static bool IsValidSelectionMode(object? value) =>
        value is CalendarSelectionMode.SingleDate or CalendarSelectionMode.SingleRange or
            CalendarSelectionMode.MultipleRange or CalendarSelectionMode.None;

    internal void OnSelectedDatesCollectionChanged(
        IReadOnlyList<DateTime> removedDates,
        IReadOnlyList<DateTime> addedDates)
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

    internal void RefreshCalendarView()
    {
        if (DisplayMode == CalendarMode.Month)
        {
            CalculateDayGrid();
        }

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
    /// Initializes a new instance of the <see cref="CalendarDateChangedEventArgs"/> class.
    /// </summary>
    public CalendarDateChangedEventArgs(RoutedEvent routedEvent, DateTime? removedDate, DateTime? addedDate)
    {
        RoutedEvent = routedEvent;
        RemovedDate = removedDate;
        AddedDate = addedDate;
    }
}
