using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a calendar control for selecting dates.
/// </summary>
public class Calendar : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedDate dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(nameof(SelectedDate), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null, OnSelectedDateChanged));

    /// <summary>
    /// Identifies the DisplayDate dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateProperty =
        DependencyProperty.Register(nameof(DisplayDate), typeof(DateTime), typeof(Calendar),
            new PropertyMetadata(DateTime.Today, OnDisplayDateChanged));

    /// <summary>
    /// Identifies the DisplayDateStart dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateStartProperty =
        DependencyProperty.Register(nameof(DisplayDateStart), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the DisplayDateEnd dependency property.
    /// </summary>
    public static readonly DependencyProperty DisplayDateEndProperty =
        DependencyProperty.Register(nameof(DisplayDateEnd), typeof(DateTime?), typeof(Calendar),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the FirstDayOfWeek dependency property.
    /// </summary>
    public static readonly DependencyProperty FirstDayOfWeekProperty =
        DependencyProperty.Register(nameof(FirstDayOfWeek), typeof(DayOfWeek), typeof(Calendar),
            new PropertyMetadata(DayOfWeek.Sunday, OnDisplayDateChanged));

    /// <summary>
    /// Identifies the IsTodayHighlighted dependency property.
    /// </summary>
    public static readonly DependencyProperty IsTodayHighlightedProperty =
        DependencyProperty.Register(nameof(IsTodayHighlighted), typeof(bool), typeof(Calendar),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(CalendarSelectionMode), typeof(Calendar),
            new PropertyMetadata(CalendarSelectionMode.SingleDate));

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

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the currently selected date.
    /// </summary>
    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    /// <summary>
    /// Gets or sets the date to display.
    /// </summary>
    public DateTime DisplayDate
    {
        get => (DateTime)(GetValue(DisplayDateProperty) ?? DateTime.Today);
        set => SetValue(DisplayDateProperty, value);
    }

    /// <summary>
    /// Gets or sets the first date to display.
    /// </summary>
    public DateTime? DisplayDateStart
    {
        get => (DateTime?)GetValue(DisplayDateStartProperty);
        set => SetValue(DisplayDateStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the last date to display.
    /// </summary>
    public DateTime? DisplayDateEnd
    {
        get => (DateTime?)GetValue(DisplayDateEndProperty);
        set => SetValue(DisplayDateEndProperty, value);
    }

    /// <summary>
    /// Gets or sets the first day of the week.
    /// </summary>
    public DayOfWeek FirstDayOfWeek
    {
        get => (DayOfWeek)(GetValue(FirstDayOfWeekProperty) ?? DayOfWeek.Sunday);
        set => SetValue(FirstDayOfWeekProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether today is highlighted.
    /// </summary>
    public bool IsTodayHighlighted
    {
        get => (bool)(GetValue(IsTodayHighlightedProperty) ?? true);
        set => SetValue(IsTodayHighlightedProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
    public CalendarSelectionMode SelectionMode
    {
        get => (CalendarSelectionMode)(GetValue(SelectionModeProperty) ?? CalendarSelectionMode.SingleDate);
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>
    /// Gets the collection of selected dates.
    /// </summary>
    public List<DateTime> SelectedDates { get; } = new();

    /// <summary>
    /// Gets the collection of blackout dates.
    /// </summary>
    public List<DateTime> BlackoutDates { get; } = new();

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
    private Rect _prevButtonRect;
    private Rect _nextButtonRect;
    private Rect _monthYearRect;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Calendar"/> class.
    /// </summary>
    public Calendar()
    {
        Focusable = true;
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));

        CalculateDayGrid();
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

            // Check navigation buttons
            if (_prevButtonRect.Contains(position))
            {
                NavigateToPreviousMonth();
                e.Handled = true;
                return;
            }

            if (_nextButtonRect.Contains(position))
            {
                NavigateToNextMonth();
                e.Handled = true;
                return;
            }

            // Check day cells
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Columns; col++)
                {
                    if (_dayCells[row, col].Contains(position))
                    {
                        var date = _dayDates[row, col];
                        if (IsDateSelectable(date))
                        {
                            SelectDate(date);
                        }
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            var currentDate = SelectedDate ?? DisplayDate;

            switch (keyArgs.Key)
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
                    NavigateToPreviousMonth();
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    NavigateToNextMonth();
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
            }
        }
    }

    #endregion

    #region Navigation

    private void NavigateToPreviousMonth()
    {
        DisplayDate = DisplayDate.AddMonths(-1);
    }

    private void NavigateToNextMonth()
    {
        DisplayDate = DisplayDate.AddMonths(1);
    }

    private void SelectDate(DateTime date)
    {
        if (!IsDateSelectable(date))
            return;

        // Update display date if needed
        if (date.Month != DisplayDate.Month || date.Year != DisplayDate.Year)
        {
            DisplayDate = new DateTime(date.Year, date.Month, 1);
        }

        var oldDate = SelectedDate;
        SelectedDate = date;

        if (SelectionMode == CalendarSelectionMode.SingleDate)
        {
            SelectedDates.Clear();
            SelectedDates.Add(date);
        }
    }

    private bool IsDateSelectable(DateTime date)
    {
        if (DisplayDateStart.HasValue && date < DisplayDateStart.Value)
            return false;

        if (DisplayDateEnd.HasValue && date > DisplayDateEnd.Value)
            return false;

        if (BlackoutDates.Contains(date.Date))
            return false;

        return true;
    }

    #endregion

    #region Grid Calculation

    private void CalculateDayGrid()
    {
        var firstOfMonth = new DateTime(DisplayDate.Year, DisplayDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(DisplayDate.Year, DisplayDate.Month);

        // Calculate the day of week offset
        var firstDayOfWeek = (int)FirstDayOfWeek;
        var startDayOfWeek = (int)firstOfMonth.DayOfWeek;
        var offset = (startDayOfWeek - firstDayOfWeek + 7) % 7;

        // Fill in dates
        var currentDate = firstOfMonth.AddDays(-offset);

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                _dayDates[row, col] = currentDate;
                currentDate = currentDate.AddDays(1);
            }
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
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var cornerRadius = CornerRadius;
        var hasCornerRadius = cornerRadius.TopLeft > 0;

        // Draw background
        if (Background != null)
        {
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(Background, null, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(Background, null, rect);
            }
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, rect);
            }
        }

        var startX = padding.Left + BorderThickness.Left;
        var startY = padding.Top + BorderThickness.Top;

        // Draw header
        DrawHeader(dc, startX, startY);

        // Draw day-of-week headers
        DrawDayHeaders(dc, startX, startY + HeaderHeight);

        // Draw day grid
        DrawDayGrid(dc, startX, startY + HeaderHeight + DayHeaderHeight);
    }

    private void DrawHeader(DrawingContext dc, double x, double y)
    {
        var width = Columns * CellWidth;
        var headerRect = new Rect(x, y, width, HeaderHeight);

        // Draw header background
        var headerBg = new SolidColorBrush(Color.FromRgb(55, 55, 55));
        dc.DrawRectangle(headerBg, null, headerRect);

        // Draw previous button
        _prevButtonRect = new Rect(x + 4, y + 4, 28, 28);
        DrawNavigationButton(dc, _prevButtonRect, false);

        // Draw next button
        _nextButtonRect = new Rect(x + width - 32, y + 4, 28, 28);
        DrawNavigationButton(dc, _nextButtonRect, true);

        // Draw month/year text
        var monthYearText = DisplayDate.ToString("MMMM yyyy");
        var formattedText = new FormattedText(monthYearText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
        {
            Foreground = Foreground ?? new SolidColorBrush(Color.White),
            FontWeight = 600
        };
        TextMeasurement.MeasureText(formattedText);

        var textX = x + (width - formattedText.Width) / 2;
        var textY = y + (HeaderHeight - formattedText.Height) / 2;
        dc.DrawText(formattedText, new Point(textX, textY));

        _monthYearRect = new Rect(textX, textY, formattedText.Width, formattedText.Height);
    }

    private void DrawNavigationButton(DrawingContext dc, Rect rect, bool isNext)
    {
        var arrowBrush = new SolidColorBrush(Color.White);
        var arrowPen = new Pen(arrowBrush, 2);

        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;

        if (isNext)
        {
            dc.DrawLine(arrowPen, new Point(centerX - 4, centerY - 6), new Point(centerX + 4, centerY));
            dc.DrawLine(arrowPen, new Point(centerX + 4, centerY), new Point(centerX - 4, centerY + 6));
        }
        else
        {
            dc.DrawLine(arrowPen, new Point(centerX + 4, centerY - 6), new Point(centerX - 4, centerY));
            dc.DrawLine(arrowPen, new Point(centerX - 4, centerY), new Point(centerX + 4, centerY + 6));
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

            var formattedText = new FormattedText(dayName, FontFamily ?? "Segoe UI", 11)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160))
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
                var isCurrentMonth = date.Month == DisplayDate.Month;
                var isToday = date.Date == today;
                var isSelected = SelectedDate.HasValue && date.Date == SelectedDate.Value.Date;
                var isSelectable = IsDateSelectable(date);

                DrawDayCell(dc, cellRect, date.Day, isCurrentMonth, isToday, isSelected, isSelectable);
            }
        }
    }

    private void DrawDayCell(DrawingContext dc, Rect rect, int day, bool isCurrentMonth, bool isToday, bool isSelected, bool isSelectable)
    {
        // Draw selection or today highlight
        if (isSelected)
        {
            var selectBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            dc.DrawEllipse(selectBrush, null,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }
        else if (isToday && IsTodayHighlighted)
        {
            var todayBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));
            var todayPen = new Pen(todayBrush, 2);
            dc.DrawEllipse(null, todayPen,
                new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2),
                rect.Width / 2 - 2, rect.Height / 2 - 2);
        }

        // Draw day number
        var dayText = day.ToString();
        Color textColor;

        if (isSelected)
        {
            textColor = Color.White;
        }
        else if (!isSelectable)
        {
            textColor = Color.FromRgb(80, 80, 80);
        }
        else if (!isCurrentMonth)
        {
            textColor = Color.FromRgb(100, 100, 100);
        }
        else
        {
            textColor = Color.White;
        }

        var formattedText = new FormattedText(dayText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 13)
        {
            Foreground = new SolidColorBrush(textColor)
        };
        TextMeasurement.MeasureText(formattedText);

        var textX = rect.X + (rect.Width - formattedText.Width) / 2;
        var textY = rect.Y + (rect.Height - formattedText.Height) / 2;
        dc.DrawText(formattedText, new Point(textX, textY));
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
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
                e.OldValue as DateTime?, e.NewValue as DateTime?);
            calendar.RaiseEvent(args);
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Calendar calendar)
        {
            calendar.InvalidateVisual();
        }
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
public class CalendarDateChangedEventArgs : RoutedEventArgs
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

// Note: SelectionChangedEventArgs is defined in Selector.cs
