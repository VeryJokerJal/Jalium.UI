using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;
[Collection("Application")]
public class CalendarThemeTests
{
    [Fact]
    public void Calendar_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides() => WithApplication(app =>
    {
        var calendar = new Calendar();
        Layout(calendar);
        Assert.IsType<Style>(app.Resources[typeof(Calendar)]);
        Assert.False(calendar.HasLocalValue(Control.BackgroundProperty));
        Assert.False(calendar.HasLocalValue(Control.BorderBrushProperty));
        Assert.NotNull(calendar.Background);
        Assert.NotNull(calendar.BorderBrush);
        Assert.Equal(8, calendar.Padding.Left);
        Assert.IsType<CalendarItem>(calendar.FindName("PART_CalendarItem"));
    });
    [Fact]
    public void SelectedDateTemplateUsesThemeAccentAndSelectedText() => WithApplication(app =>
    {
        var calendar = new Calendar
        {
            DisplayDate = new DateTime(2026, 7, 11),
            SelectedDate = new DateTime(2026, 7, 11)
        };
        Layout(calendar);
        var button = Walk(calendar).OfType<CalendarDayButton>().Single(x => x.IsSelected);
        Assert.Same(app.Resources["AccentBrush"], button.Background);
        Assert.Same(app.Resources["TextOnAccent"], button.Foreground);
        Assert.Same(button.Background, Assert.IsType<Border>(button.FindName("PART_Root")).Background);
    });
    [Fact]
    public void CalendarForwardsCallerForegroundAndTypographyToRealContent() => WithApplication(app =>
    {
        var foreground = Brushes.Magenta;
        var calendar = new Calendar
        {
            Foreground = foreground,
            FontSize = 28,
            DisplayDate = new DateTime(2026, 7, 11)
        };
        Layout(calendar);
        var button = Walk(calendar).OfType<CalendarDayButton>().First(x => !x.IsInactive && !x.IsSelected && !x.IsBlackedOut);
        Assert.Same(foreground, button.Foreground);
        Assert.Equal(28, button.FontSize);
        var item = Assert.IsType<CalendarItem>(calendar.FindName("PART_CalendarItem"));
        Assert.Equal(22, item.DayTitleFontSize);
        Assert.Equal(28, item.HeaderButton!.FontSize);
    });
    [Fact]
    public void CalendarDayButtonStatesUseCurrentThemeRoles() => WithApplication(app =>
    {
        var button = new CalendarDayButton
        {
            Content = "11",
            IsToday = true
        };
        Layout(button);
        Assert.Same(app.Resources["AccentBrush"], button.BorderBrush);
        button.IsSelected = true;
        Assert.Same(app.Resources["TextOnAccent"], button.Foreground);
        button.IsInactive = true;
        Assert.Same(app.Resources["TextSecondary"], button.Foreground);
        button.IsInactive = false;
        button.IsBlackedOut = true;
        Assert.Same(app.Resources["TextDisabled"], button.Foreground);
        Assert.Equal(Visibility.Visible, Assert.IsType<Border>(button.FindName("PART_BlackoutStrike")).Visibility);
    });
    [Fact]
    public void CalendarItemStyleAndWeekdayRolesReachTheirTemplateParts() => WithApplication(app =>
    {
        var brush = Brushes.Lime;
        var style = new Style(typeof(CalendarItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, brush));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Cyan));
        var calendar = new Calendar
        {
            CalendarItemStyle = style
        };
        Layout(calendar);
        var item = Assert.IsType<CalendarItem>(calendar.FindName("PART_CalendarItem"));
        Assert.Same(brush, item.Background);
        Assert.Same(brush, Assert.IsType<Border>(item.FindName("PART_Root")).Background);
        Assert.Same(Brushes.Cyan, Walk(item).OfType<CalendarDayButton>().First(x => !x.IsInactive && !x.IsToday).Foreground);
        var titles = Assert.IsType<ItemsControl>(item.FindName("PART_DayTitles"));
        Assert.All(Walk(titles).OfType<TextBlock>(), text => Assert.Same(app.Resources["TextSecondary"], text.Foreground));
    });
    [Fact]
    public void ThemeFlipUpdatesExistingCalendarDateText() => WithApplication(app =>
    {
        var calendar = new Calendar
        {
            DisplayDate = new DateTime(2026, 7, 11)
        };
        Layout(calendar);
        var item = Assert.IsType<CalendarItem>(calendar.FindName("PART_CalendarItem"));
        var button = Walk(calendar).OfType<CalendarDayButton>().First(x => !x.IsInactive && !x.IsToday);
        var text = Assert.Single(Walk(button).OfType<TextBlock>());
        ThemeManager.ApplyTheme(ThemeVariant.Light);
        Layout(calendar);
        var expected = Assert.IsType<SolidColorBrush>(app.Resources["TextPrimary"]).Color;
        string Color(Brush? brush) => brush is SolidColorBrush solid ? solid.Color.ToString() : brush?.GetType().Name ?? "null";
        string Colors() => $"root={Color(calendar.Foreground)};item={Color(item.Foreground)};button={Color(button.Foreground)};text={Color(text.Foreground)}";
        Assert.True(button.Foreground is SolidColorBrush buttonBrush && buttonBrush.Color == expected, "Stale date button foreground: " + Colors());
        Assert.True(text.Foreground is SolidColorBrush textBrush && textBrush.Color == expected, "Stale date text foreground: " + Colors());
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneDateButtonsRetargetAndReleaseCalendarColors(bool day) => WithApplication(app =>
    {
        Button button = day ? new CalendarDayButton
        {
            Content = "11"
        }

        : new CalendarButton
        {
            Content = "July"
        };
        Layout(button);
        ThemeManager.ApplyTheme(ThemeVariant.Light);
        Layout(button);
        Assert.Equal(Assert.IsType<SolidColorBrush>(app.Resources["TextPrimary"]).Color, Assert.IsType<SolidColorBrush>(Assert.Single(Walk(button).OfType<TextBlock>()).Foreground).Color);
        var owner = new Calendar
        {
            Foreground = Brushes.Magenta
        };
        if (button is CalendarDayButton date)
            date.Owner = owner;
        else
            ((CalendarButton)button).Owner = owner;
        Layout(button);
        Assert.Same(Brushes.Magenta, button.Foreground);
        if (button is CalendarDayButton formerDate)
            formerDate.Owner = null;
        else
            ((CalendarButton)button).Owner = null;
        owner.Foreground = Brushes.Lime;
        Layout(button);
        Assert.NotSame(Brushes.Lime, button.Foreground);
    });
    [Fact]
    public void CallerDayAndPeriodStylesRetainTheirForegroundValues() => WithApplication(app =>
    {
        var dayStyle = new Style(typeof(CalendarDayButton));
        dayStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Magenta));
        var periodStyle = new Style(typeof(CalendarButton));
        periodStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Cyan));
        var calendar = new Calendar
        {
            CalendarDayButtonStyle = dayStyle,
            CalendarButtonStyle = periodStyle
        };
        Layout(calendar);
        Assert.Same(Brushes.Magenta, Walk(calendar).OfType<CalendarDayButton>().First(x => !x.IsInactive && !x.IsToday).Foreground);
        calendar.SelectedDate = DateTime.Today;
        Layout(calendar);
        Assert.Same(Brushes.Magenta, Walk(calendar).OfType<CalendarDayButton>().Single(x => x.IsSelected).Foreground);
        ThemeManager.ApplyTheme(ThemeVariant.Light);
        Layout(calendar);
        Assert.Same(Brushes.Magenta, Walk(calendar).OfType<CalendarDayButton>().Single(x => x.IsSelected).Foreground);
        calendar.DisplayMode = CalendarMode.Year;
        Layout(calendar);
        Assert.All(Walk(calendar).OfType<CalendarButton>(), button => Assert.Same(Brushes.Cyan, button.Foreground));
    });
    private static void Layout(UIElement element)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            element.Measure(new Size(320, 320));
            element.Arrange(new Rect(0, 0, 320, 320));
            element.UpdateLayout();
        }
    }

    private static IEnumerable<Visual> Walk(Visual root)
    {
        yield return root;
        for (int i = 0; i < root.VisualChildrenCount; i++)
            if (root.GetVisualChild(i)is { } child)
                foreach (var value in Walk(child))
                    yield return value;
    }

    private static void WithApplication(Action<Application> action)
    {
        Reset();
        try
        {
            action(new Application());
        }
        finally
        {
            Reset();
        }
    }

    private static void Reset()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
    }
}
