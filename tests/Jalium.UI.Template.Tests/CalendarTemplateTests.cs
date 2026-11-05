using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class CalendarTemplateTests
{
    [Fact]
    public void DefaultMonthUsesRealDateButtonsAndStableDateModels()
    {
        var calendar = Create();
        var item = Item(calendar);
        Assert.Equal(32, item.PreviousButton!.ActualWidth);
        Assert.Equal(32, item.NextButton!.ActualWidth);
        Assert.Equal(42, item.DayItems.Count);
        Assert.Equal(7, item.DayTitles.Count);
        var buttons = Walk(item).OfType<CalendarDayButton>().ToArray();
        Assert.Equal(42, buttons.Length);
        var date = new DateTime(2026, 7, 15);
        var cell = item.DayItems.Single(x => x.Date == date);
        var button = buttons.Single(x => ReferenceEquals(x.DataContext, cell));
        Assert.True(button.IsEnabled);
        Assert.Equal(date, button.Date);
        Assert.Same(calendar, button.Owner);
        button.PerformClick();
        Assert.Equal(date, calendar.SelectedDate);
        Assert.True(cell.IsSelected);
        Assert.True(button.IsSelected);
        Assert.Same(cell, item.DayItems.Single(x => x.Date == date));
        Assert.NotNull(button.FindName("PART_ContentPresenter"));
        Assert.Equal("Selected", VisualStateManager.GetCurrentStateName(button, "SelectionStates"));
    }

    [Fact]
    public void HeaderAndPeriodButtonsDriveMonthYearAndDecade()
    {
        var calendar = Create();
        var item = Item(calendar);
        item.HeaderButton!.PerformClick();
        Layout(calendar);
        Assert.Equal(CalendarMode.Year, calendar.DisplayMode);
        Assert.Equal("Year", VisualStateManager.GetCurrentStateName(item, "DisplayModeStates"));
        Assert.Equal(Visibility.Visible, item.YearView!.Visibility);
        var month = item.PeriodItems.Single(x => x.Date.Month == 3);
        month.SelectCommand.Execute(month);
        Layout(calendar);
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        Assert.Equal(3, calendar.DisplayDate.Month);
        item.HeaderButton.PerformClick();
        item.HeaderButton.PerformClick();
        Layout(calendar);
        Assert.Equal(CalendarMode.Decade, calendar.DisplayMode);
        var year = item.PeriodItems.Single(x => x.Date.Year == 2028);
        year.SelectCommand.Execute(year);
        Layout(calendar);
        Assert.Equal(CalendarMode.Year, calendar.DisplayMode);
        Assert.Equal(2028, calendar.DisplayDate.Year);
    }

    [Fact]
    public void BlackoutBoundsAndDisabledCalendarSuppressCellCommands()
    {
        var calendar = Create();
        calendar.DisplayDateStart = new DateTime(2026, 7, 5);
        calendar.DisplayDateEnd = new DateTime(2026, 7, 22);
        calendar.BlackoutDates.Add(new CalendarDateRange(new DateTime(2026, 7, 10)));
        var item = Item(calendar);
        foreach (int day in new[]
        {
            1,
            10,
            31
        }

        )
        {
            var cell = item.DayItems.Single(x => x.Date == new DateTime(2026, 7, day));
            Assert.False(cell.IsEnabled);
            Assert.False(cell.SelectCommand.CanExecute(cell));
            cell.SelectCommand.Execute(cell);
        }

        Assert.Null(calendar.SelectedDate);
        Assert.False(item.CanNavigatePrevious);
        Assert.False(item.CanNavigateNext);
        calendar.IsEnabled = false;
        var available = item.DayItems.Single(x => x.Date == new DateTime(2026, 7, 15));
        available.SelectCommand.Execute(available);
        Assert.Null(calendar.SelectedDate);
    }

    [Fact]
    public void RetiredHeaderAndCellCommandsCannotChangeTheCurrentCalendar()
    {
        var calendar = Create();
        var item = Item(calendar);
        var header = item.HeaderButton!;
        var cell = item.DayItems.Single(x => x.Date == new DateTime(2026, 7, 15));
        var command = cell.SelectCommand;
        var template = item.Template;
        var blank = Blank(typeof(CalendarItem));
        item.Template = blank;
        item.ApplyTemplate();
        item.Template = template;
        item.ApplyTemplate();
        Layout(calendar);
        header.PerformClick();
        command.Execute(cell);
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        Assert.Null(calendar.SelectedDate);
        Assert.False(command.CanExecute(cell));
        var current = item.DayItems.Single(x => x.Date == cell.Date);
        current.SelectCommand.Execute(current);
        Assert.Equal(current.Date, calendar.SelectedDate);
    }

    [Fact]
    public void RootTemplateReplacementReleasesOwnerAndPreservesBindings()
    {
        var source = new Dates();
        var calendar = Create();
        BindingOperations.SetBinding(calendar, Calendar.SelectedDateProperty, new Binding(nameof(Dates.Value)) { Source = source, Mode = BindingMode.TwoWay });
        var old = Item(calendar);
        var original = calendar.Template;
        calendar.Template = Blank(typeof(Calendar));
        calendar.ApplyTemplate();
        Assert.Null(old.Owner);
        old.HeaderButton!.PerformClick();
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
        calendar.Template = original;
        calendar.ApplyTemplate();
        Layout(calendar);
        var cell = Item(calendar).DayItems.Single(x => x.Date == new DateTime(2026, 7, 15));
        cell.SelectCommand.Execute(cell);
        Assert.Equal(cell.Date, source.Value);
        Assert.NotNull(BindingOperations.GetBindingExpression(calendar, Calendar.SelectedDateProperty));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(9999, 12)]
    public void ExtremeMonthsHaveDisabledEmptyBoundaryCells(int year, int month)
    {
        var calendar = Create();
        calendar.DisplayDate = new DateTime(year, month, 1);
        Layout(calendar);
        var item = Item(calendar);
        Assert.Equal(42, item.DayItems.Count);
        Assert.All(item.DayItems.Where(x => x.Text.Length == 0), x => Assert.False(x.IsEnabled));
        if (year == 1)
            Assert.False(item.CanNavigatePrevious);
        else
            Assert.False(item.CanNavigateNext);
        calendar.DisplayMode = CalendarMode.Decade;
        Layout(calendar);
        Assert.All(item.PeriodItems, x => Assert.InRange(x.Date.Year, 1, 9999));
    }

    [Fact]
    public void MissingPartsAndArbitraryButtonContentUseStandardTemplates()
    {
        var calendar = Create();
        calendar.Template = Blank(typeof(Calendar));
        calendar.ApplyTemplate();
        calendar.SelectedDate = new DateTime(2026, 7, 11);
        calendar.DisplayMode = CalendarMode.Year;
        var item = new CalendarItem
        {
            Template = Blank(typeof(CalendarItem))
        };
        item.ApplyTemplate();
        item.UpdateHeader("Custom");
        Assert.Equal("Custom", item.HeaderText);
        Assert.Null(item.HeaderButton);
        var content = new Border
        {
            Width = 12,
            Height = 12,
            Background = Brushes.Lime
        };
        var button = new CalendarDayButton
        {
            Style = (Style)Defaults.Value[typeof(CalendarDayButton)],
            Content = content
        };
        Layout(button);
        Assert.Contains(content, Walk(button));
        Assert.Null(typeof(Calendar).GetMethod("OnRender", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void PeriodNavigationRespectsYearAndDecadeBoundsAndKeepsDisplayBinding()
    {
        var calendar = Create();
        var source = new Dates
        {
            Value = new DateTime(2026, 7, 11)
        };
        BindingOperations.SetBinding(calendar, Calendar.DisplayDateProperty, new Binding(nameof(Dates.Value)) { Source = source, Mode = BindingMode.TwoWay });
        Item(calendar).NextButton!.PerformClick();
        Assert.Equal(new DateTime(2026, 8, 11), source.Value);
        Assert.NotNull(BindingOperations.GetBindingExpression(calendar, Calendar.DisplayDateProperty));
        calendar.DisplayDateStart = new DateTime(2020, 5, 1);
        calendar.DisplayDateEnd = new DateTime(2029, 11, 30);
        calendar.DisplayMode = CalendarMode.Decade;
        Layout(calendar);
        Assert.False(Item(calendar).CanNavigatePrevious);
        Assert.False(Item(calendar).CanNavigateNext);
        calendar.DisplayMode = CalendarMode.Year;
        calendar.DisplayDate = new DateTime(2020, 8, 1);
        Layout(calendar);
        Assert.False(Item(calendar).CanNavigatePrevious);
        Assert.True(Item(calendar).CanNavigateNext);
    }

    [Fact]
    public void WeekdayTemplatesAndFontChangesRemainReplaceable()
    {
        var calendar = Create();
        var item = Item(calendar);
        var custom = new DataTemplate();
        custom.SetVisualTree(() => new TextBlock { Text = "weekday" });
        item.Resources[CalendarItem.DayTitleTemplateResourceKey] = custom;
        var original = item.Template;
        item.Template = Blank(typeof(CalendarItem));
        item.ApplyTemplate();
        item.Template = original;
        item.ApplyTemplate();
        Layout(calendar);
        var titles = Assert.IsType<ItemsControl>(item.FindName("PART_DayTitles"));
        Assert.Same(custom, titles.ItemTemplate);
        Assert.Equal(7, Walk(titles).OfType<TextBlock>().Count(x => x.Text == "weekday"));
        calendar.FontSize = 28;
        Layout(calendar);
        Assert.Equal(22, item.DayTitleFontSize);
        calendar.FontSize = 0;
        Layout(calendar);
        Assert.Equal(0, item.DayTitleFontSize);
    }

    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    private static Calendar Create()
    {
        var themes = new ResourceDictionary();
        foreach (var type in new[]
        {
            typeof(CalendarItem),
            typeof(CalendarButton),
            typeof(CalendarDayButton),
            typeof(Button)
        }

        )
            themes[type] = Defaults.Value[type];
        ThemeManager.RegisterControlThemes(themes);
        var calendar = new Calendar
        {
            Style = (Style)Defaults.Value[typeof(Calendar)],
            DisplayDate = new DateTime(2026, 7, 11)
        };
        Layout(calendar);
        return calendar;
    }

    private static CalendarItem Item(Calendar calendar) => Assert.IsType<CalendarItem>(calendar.FindName("PART_CalendarItem"));
    private static ControlTemplate Blank(Type type)
    {
        var template = new ControlTemplate(type);
        template.SetVisualTree(() => new Border());
        return template;
    }

    private static void Layout(UIElement element)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            element.Measure(new Size(300, 350));
            element.Arrange(new Rect(0, 0, 300, 350));
            element.UpdateLayout();
        }
    }

    private static IEnumerable<Visual> Walk(Visual visual)
    {
        yield return visual;
        for (int index = 0; index < visual.VisualChildrenCount; index++)
            if (visual.GetVisualChild(index)is { } child)
                foreach (var value in Walk(child))
                    yield return value;
    }

    private sealed class Dates : INotifyPropertyChanged
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
