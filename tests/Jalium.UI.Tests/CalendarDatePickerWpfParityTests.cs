using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Calendar = Jalium.UI.Controls.Calendar;

namespace Jalium.UI.Tests;

public sealed class CalendarDatePickerWpfParityTests
{
    [Fact]
    public void Calendar_DeclaresWpfDependencyPropertiesEventsAndVirtualHooks()
    {
        foreach (string name in new[]
                 {
                     nameof(Calendar.CalendarButtonStyleProperty),
                     nameof(Calendar.CalendarDayButtonStyleProperty),
                     nameof(Calendar.CalendarItemStyleProperty),
                     nameof(Calendar.DisplayModeProperty)
                 })
        {
            AssertField<Calendar, DependencyProperty>(name);
        }

        AssertField<Calendar, RoutedEvent>(nameof(Calendar.SelectedDatesChangedEvent));
        Assert.Equal(typeof(EventHandler<SelectionChangedEventArgs>),
            typeof(Calendar).GetEvent(nameof(Calendar.SelectedDatesChanged))!.EventHandlerType);
        Assert.Equal(typeof(EventHandler<CalendarModeChangedEventArgs>),
            typeof(Calendar).GetEvent(nameof(Calendar.DisplayModeChanged))!.EventHandlerType);
        Assert.Equal(typeof(EventHandler<EventArgs>),
            typeof(Calendar).GetEvent(nameof(Calendar.SelectionModeChanged))!.EventHandlerType);

        AssertVirtual(typeof(Calendar), "OnDisplayDateChanged", typeof(CalendarDateChangedEventArgs));
        AssertVirtual(typeof(Calendar), "OnDisplayModeChanged", typeof(CalendarModeChangedEventArgs));
        AssertVirtual(typeof(Calendar), "OnSelectedDatesChanged", typeof(SelectionChangedEventArgs));
        AssertVirtual(typeof(Calendar), "OnSelectionModeChanged", typeof(EventArgs));
    }

    [Fact]
    public void Calendar_CollectionsSynchronizeSelectionAndEnforceBlackoutDates()
    {
        var calendar = new Calendar();
        Assert.IsType<SelectedDatesCollection>(calendar.SelectedDates);
        Assert.IsType<CalendarBlackoutDatesCollection>(calendar.BlackoutDates);

        int selectedDatesChanged = 0;
        calendar.SelectedDatesChanged += (_, _) => selectedDatesChanged++;
        var first = new DateTime(2026, 7, 11);
        calendar.SelectedDate = first;

        Assert.Equal(first, calendar.SelectedDate);
        Assert.Equal(new[] { first }, calendar.SelectedDates);
        Assert.Equal(1, selectedDatesChanged);

        calendar.SelectionMode = CalendarSelectionMode.MultipleRange;
        calendar.SelectedDates.AddRange(first, first.AddDays(2));
        Assert.Equal(3, calendar.SelectedDates.Count);
        Assert.Equal(first, calendar.SelectedDate);

        calendar.SelectedDates.Clear();
        calendar.BlackoutDates.Add(new CalendarDateRange(first, first.AddDays(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            calendar.SelectedDate = first;
        });

        calendar.SelectedDate = first.AddDays(2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            calendar.BlackoutDates.Add(new CalendarDateRange(first.AddDays(2)));
        });
        Assert.True(calendar.BlackoutDates.Contains(first, first.AddDays(1)));
    }

    [Fact]
    public void Calendar_BlackoutAndDuplicateSelectionCompareByDayButBoundsPreserveTime()
    {
        var day = new DateTime(2026, 7, 11);
        var calendar = new Calendar
        {
            SelectionMode = CalendarSelectionMode.MultipleRange
        };
        calendar.BlackoutDates.Add(new CalendarDateRange(day.AddHours(18), day.AddHours(20)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectedDates.Add(day.AddHours(9)));

        calendar.BlackoutDates.Clear();
        calendar.SelectedDates.Add(day.AddHours(9));
        calendar.SelectedDates.Add(day.AddHours(18));
        Assert.Single(calendar.SelectedDates);

        calendar.DisplayDateStart = day.AddHours(18);
        Assert.Equal(day.AddHours(9), calendar.DisplayDateStart);
    }

    [Fact]
    public void Calendar_ExtremeDisplayMonthsBuildBoundarySafeDayGrids()
    {
        var calendar = new Calendar();
        calendar.DisplayDate = DateTime.MinValue;
        Assert.Equal(DateTime.MinValue, calendar.DisplayDate);
        calendar.DisplayDate = DateTime.MaxValue;
        Assert.Equal(DateTime.MaxValue, calendar.DisplayDate);
    }

    [Fact]
    public void Calendar_DisplayAndSelectionModeChangesRaisePublicEvents()
    {
        var calendar = new Calendar();
        int displayModeChanges = 0;
        int selectionModeChanges = 0;
        calendar.DisplayModeChanged += (_, e) =>
        {
            displayModeChanges++;
            Assert.Equal(CalendarMode.Month, e.OldMode);
            Assert.Equal(CalendarMode.Year, e.NewMode);
        };
        calendar.SelectionModeChanged += (_, _) => selectionModeChanges++;

        calendar.DisplayMode = CalendarMode.Year;
        calendar.SelectionMode = CalendarSelectionMode.None;

        Assert.Equal(1, displayModeChanges);
        Assert.Equal(1, selectionModeChanges);
        Assert.Throws<ArgumentException>(() =>
        {
            calendar.DisplayMode = (CalendarMode)99;
        });
    }

    [Fact]
    public void DatePicker_DeclaresWpfSurfaceAndSynchronizesCalendarConfiguration()
    {
        foreach (string name in new[]
                 {
                     nameof(DatePicker.CalendarStyleProperty),
                     nameof(DatePicker.FirstDayOfWeekProperty),
                     nameof(DatePicker.IsTodayHighlightedProperty),
                     nameof(DatePicker.TextProperty)
                 })
        {
            AssertField<DatePicker, DependencyProperty>(name);
        }

        Assert.Equal(typeof(EventHandler<DatePickerDateValidationErrorEventArgs>),
            typeof(DatePicker).GetEvent(nameof(DatePicker.DateValidationError))!.EventHandlerType);
        AssertVirtual(typeof(DatePicker), "OnCalendarClosed", typeof(RoutedEventArgs));
        AssertVirtual(typeof(DatePicker), "OnCalendarOpened", typeof(RoutedEventArgs));
        AssertVirtual(typeof(DatePicker), "OnDateValidationError", typeof(DatePickerDateValidationErrorEventArgs));
        AssertVirtual(typeof(DatePicker), "OnSelectedDateChanged", typeof(SelectionChangedEventArgs));

        var picker = new DatePicker
        {
            FirstDayOfWeek = DayOfWeek.Monday,
            IsTodayHighlighted = false
        };
        var calendar = (Calendar)typeof(DatePicker)
            .GetField("_calendar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(picker)!;
        Assert.Same(calendar.BlackoutDates, picker.BlackoutDates);
        Assert.Equal(DayOfWeek.Monday, calendar.FirstDayOfWeek);
        Assert.False(calendar.IsTodayHighlighted);
        Assert.Same(CalendarItem.DayTitleTemplateResourceKey, CalendarItem.DayTitleTemplateResourceKey);
        Assert.Equal("DayTitleTemplate", CalendarItem.DayTitleTemplateResourceKey.ResourceId);
        Assert.Equal(DatePickerFormat.Long, new DatePicker().SelectedDateFormat);
        Assert.Equal(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, new Calendar().FirstDayOfWeek);
        Assert.Equal(CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek, new DatePicker().FirstDayOfWeek);
        Assert.Equal(RoutingStrategy.Direct, DatePicker.SelectedDateChangedEvent.RoutingStrategy);
    }

    [Fact]
    public void DatePicker_DisplayRangeUsesCalendarCoercionAndSelectionCanExpandRange()
    {
        var start = new DateTime(2026, 7, 10);
        var picker = new DatePicker
        {
            DisplayDateStart = start,
            DisplayDateEnd = new DateTime(2026, 7, 20),
            DisplayDate = new DateTime(2026, 7, 1)
        };

        Assert.Equal(start, picker.DisplayDate);
        picker.DisplayDateEnd = new DateTime(2026, 7, 5);
        Assert.Equal(start, picker.DisplayDateEnd);

        var selected = new DateTime(2026, 7, 1);
        picker.SelectedDate = selected;
        Assert.Equal(selected, picker.SelectedDate);
        Assert.Equal(selected, picker.DisplayDateStart);
        Assert.Equal(new DateTime(2026, 7, 5), picker.DisplayDateEnd);
        Assert.Equal(selected, picker.DisplayDate);
    }

    [Fact]
    public void Calendar_DirectRendererConsumesCalendarStyles()
    {
        var itemBrush = new SolidColorBrush(Color.FromRgb(10, 20, 30));
        var buttonBrush = new SolidColorBrush(Color.FromRgb(40, 50, 60));
        var dayBrush = new SolidColorBrush(Color.FromRgb(70, 80, 90));
        var itemStyle = new Style(typeof(CalendarItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, itemBrush));
        var buttonStyle = new Style(typeof(CalendarButton));
        buttonStyle.Setters.Add(new Setter(Control.BackgroundProperty, buttonBrush));
        var dayStyle = new Style(typeof(CalendarDayButton));
        dayStyle.Setters.Add(new Setter(Control.BackgroundProperty, dayBrush));

        var calendar = new Calendar
        {
            CalendarItemStyle = itemStyle,
            CalendarButtonStyle = buttonStyle,
            CalendarDayButtonStyle = dayStyle,
            DisplayDate = new DateTime(2026, 7, 11)
        };
        calendar.Measure(new Size(300, 300));
        calendar.Arrange(new Rect(0, 0, 300, 300));
        var context = new RecordingDrawingContext();
        calendar.Render(context);

        Assert.Contains(context.Brushes, brush => ReferenceEquals(brush, itemBrush));
        Assert.Contains(context.Brushes, brush => ReferenceEquals(brush, buttonBrush));
        Assert.Contains(context.Brushes, brush => ReferenceEquals(brush, dayBrush));
    }

    [Fact]
    public void DatePicker_TextParsesSelectedDateAndReportsInvalidInput()
    {
        var picker = new DatePicker();
        var validDate = new DateTime(2026, 7, 11);
        picker.Text = validDate.ToString("d", CultureInfo.CurrentCulture);
        Assert.Equal(validDate, picker.SelectedDate);

        int errors = 0;
        picker.DateValidationError += (_, e) =>
        {
            errors++;
            Assert.False(string.IsNullOrEmpty(e.Text));
        };

        string validText = picker.Text;
        picker.Text = "not-a-date";
        Assert.Equal(1, errors);
        Assert.Equal(validDate, picker.SelectedDate);
        Assert.Equal(validText, picker.Text);

        var blockedDate = validDate.AddDays(1);
        picker.BlackoutDates.Add(new CalendarDateRange(blockedDate));
        picker.Text = blockedDate.ToString("d", CultureInfo.CurrentCulture);
        Assert.Equal(2, errors);
        Assert.Equal(validDate, picker.SelectedDate);
    }

    private static void AssertField<TOwner, TField>(string name)
    {
        FieldInfo? field = typeof(TOwner).GetField(name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        Assert.Equal(typeof(TField), field.FieldType);
    }

    private static void AssertVirtual(Type owner, string name, Type parameterType)
    {
        MethodInfo? method = owner.GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, [parameterType], null);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        public List<Brush?> Brushes { get; } = [];

        public override void DrawLine(Pen pen, Point point0, Point point1) => Brushes.Add(pen.Brush);
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            Brushes.Add(brush);
            Brushes.Add(pen?.Brush);
        }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
            Brushes.Add(brush);
            Brushes.Add(pen?.Brush);
        }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
            Brushes.Add(brush);
            Brushes.Add(pen?.Brush);
        }
        public override void DrawText(FormattedText formattedText, Point origin) => Brushes.Add(formattedText.Foreground);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            Brushes.Add(brush);
            Brushes.Add(pen?.Brush);
        }
        public override void DrawImage(ImageSource imageSource, Rect rectangle) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
