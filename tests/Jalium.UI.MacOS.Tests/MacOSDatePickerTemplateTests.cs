using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Calendar = Jalium.UI.Controls.Calendar;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class MacOSDatePickerTemplateTests
{
    [Fact]
    public void DefaultTemplateOwnsInputPopupCalendarAndHeader()
    {
        var picker = Create();
        Assert.IsType<DatePickerTextBox>(picker.FindName("PART_TextBox"));
        Assert.IsType<Button>(picker.FindName("PART_Button"));
        Assert.IsType<Popup>(picker.FindName("PART_Popup"));
        Assert.IsType<Calendar>(picker.FindName("PART_Calendar"));
        picker.Header = "Birthday";
        Assert.True(picker.HasHeader);
        Assert.Equal(Visibility.Visible, Assert.IsType<ContentPresenter>(picker.FindName("PART_Header")).Visibility);
        Assert.Equal("Normal", VisualStateManager.GetCurrentStateName(picker, "CommonStates"));
    }

    [Fact]
    public void TypedDraftCommitsOnEnterAndInvalidInputReportsAndRestores()
    {
        var picker = Create();
        var input = Input(picker);
        var date = new DateTime(2026, 7, 11);
        input.SetCurrentValue(TextBox.TextProperty, date.ToString("d", CultureInfo.CurrentCulture));
        Assert.Null(picker.SelectedDate);
        Enter(input);
        Assert.Equal(date, picker.SelectedDate);
        Assert.Equal(picker.Text, input.Text);
        int errors = 0;
        picker.DateValidationError += (_, _) => errors++;
        input.SetCurrentValue(TextBox.TextProperty, "not a date");
        Enter(input);
        Assert.Equal(1, errors);
        Assert.True(picker.HasValidationError);
        Assert.Equal("Invalid", VisualStateManager.GetCurrentStateName(picker, "ValidationStates"));
        Assert.Equal(date, picker.SelectedDate);
        Assert.Equal(picker.Text, input.Text);
    }

    [Fact]
    public void ButtonAndLightDismissKeepOpenBindingAndRaiseEventsOnce()
    {
        var source = new OpenSource();
        var picker = Create();
        BindingOperations.SetBinding(picker, DatePicker.IsDropDownOpenProperty, new Binding(nameof(OpenSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        int opened = 0, closed = 0;
        picker.CalendarOpened += (_, _) => opened++;
        picker.CalendarClosed += (_, _) => closed++;
        Button(picker).PerformClick();
        Assert.True(source.Value);
        Assert.True(Popup(picker).IsOpen);
        Assert.Equal(1, opened);
        Popup(picker).IsOpen = false;
        Assert.False(source.Value);
        Assert.Equal(1, closed);
        Assert.NotNull(BindingOperations.GetBindingExpression(picker, DatePicker.IsDropDownOpenProperty));
    }

    [Fact]
    public void RetemplateReleasesOldHandlersAndKeepsOpenStateAndStableBlackouts()
    {
        var picker = Create();
        var date = new DateTime(2026, 7, 11);
        picker.SelectedDate = date;
        var blackouts = picker.BlackoutDates;
        blackouts.Add(new CalendarDateRange(date.AddDays(1)));
        var oldInput = Input(picker);
        var oldButton = Button(picker);
        var oldCalendar = Calendar(picker);
        var oldPopup = Popup(picker);
        int opened = 0, closed = 0;
        picker.CalendarOpened += (_, _) => opened++;
        picker.CalendarClosed += (_, _) => closed++;
        picker.IsDropDownOpen = true;
        oldCalendar.DisplayMode = CalendarMode.Year;
        var original = picker.Template;
        var blank = new ControlTemplate(typeof(DatePicker));
        blank.SetVisualTree(() => new Border());
        picker.Template = blank;
        picker.ApplyTemplate();
        picker.Template = original;
        picker.ApplyTemplate();
        Assert.True(picker.IsDropDownOpen);
        Assert.True(Popup(picker).IsOpen);
        Assert.Equal(1, opened);
        Assert.Equal(0, closed);
        Assert.Same(blackouts, picker.BlackoutDates);
        Assert.Single(Calendar(picker).BlackoutDates);
        Assert.Equal(CalendarMode.Year, Calendar(picker).DisplayMode);
        Assert.Empty(oldCalendar.BlackoutDates);
        oldInput.Text = "old";
        oldButton.PerformClick();
        oldCalendar.SelectedDate = date.AddDays(2);
        oldPopup.IsOpen = false;
        Assert.Equal(date, picker.SelectedDate);
        Assert.True(picker.IsDropDownOpen);
        picker.IsDropDownOpen = false;
        Assert.Equal(1, closed);
    }

    [Fact]
    public void BlackoutModelAndRangeCoercionWorkWithoutCalendarParts()
    {
        var picker = new DatePicker();
        var stable = picker.BlackoutDates;
        var date = new DateTime(2026, 7, 11);
        stable.Add(new CalendarDateRange(date));
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.SelectedDate = date);
        picker.DisplayDateStart = date;
        picker.DisplayDateEnd = date.AddDays(3);
        picker.DisplayDate = date.AddDays(-10);
        Assert.Equal(date, picker.DisplayDate);
        picker.SelectedDate = date.AddDays(-5);
        Assert.Equal(date.AddDays(-5), picker.DisplayDateStart);
        DatePickerTemplateTestHelper.Apply(picker);
        Assert.Same(stable, picker.BlackoutDates);
        Assert.Equal(stable, Calendar(picker).BlackoutDates);
    }

    [Fact]
    public void DisabledAndMissingPartsRemainSafeAndSelectionBindingStaysLive()
    {
        var source = new DateSource();
        var picker = Create();
        BindingOperations.SetBinding(picker, DatePicker.SelectedDateProperty, new Binding(nameof(DateSource.Value)) { Source = source, Mode = BindingMode.TwoWay });
        var date = new DateTime(2026, 7, 11);
        Calendar(picker).SelectedDate = date;
        Assert.Equal(date, source.Value);
        picker.IsEnabled = false;
        Button(picker).PerformClick();
        Assert.False(picker.IsDropDownOpen);
        Assert.Equal("Disabled", VisualStateManager.GetCurrentStateName(picker, "CommonStates"));
        var blank = new ControlTemplate(typeof(DatePicker));
        blank.SetVisualTree(() => new Border());
        picker.Template = blank;
        picker.ApplyTemplate();
        source.Value = date.AddDays(1);
        Assert.Equal(source.Value, picker.SelectedDate);
        Assert.NotNull(BindingOperations.GetBindingExpression(picker, DatePicker.SelectedDateProperty));
    }

    private static DatePicker Create()
    {
        var picker = new DatePicker();
        DatePickerTemplateTestHelper.Apply(picker);
        return picker;
    }

    private static DatePickerTextBox Input(DatePicker picker) => Assert.IsType<DatePickerTextBox>(picker.FindName("PART_TextBox"));
    private static Button Button(DatePicker picker) => Assert.IsType<Button>(picker.FindName("PART_Button"));
    private static Popup Popup(DatePicker picker) => Assert.IsType<Popup>(picker.FindName("PART_Popup"));
    private static Calendar Calendar(DatePicker picker) => Assert.IsType<Calendar>(picker.FindName("PART_Calendar"));
    private static void Enter(UIElement input) => input.RaiseEvent(new KeyEventArgs(UIElement.PreviewKeyDownEvent, Key.Enter, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));
    private sealed class OpenSource : INotifyPropertyChanged
    {
        private bool _value;
        public bool Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class DateSource : INotifyPropertyChanged
    {
        private DateTime? _value;
        public DateTime? Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
