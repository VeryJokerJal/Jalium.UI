using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class CalendarThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void Calendar_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var calendar = new Calendar();
            var host = new StackPanel { Width = 320, Height = 320 };
            host.Children.Add(calendar);

            host.Measure(new Size(320, 320));
            host.Arrange(new Rect(0, 0, 320, 320));

            Assert.True(app.Resources.TryGetValue(typeof(Calendar), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(calendar.HasLocalValue(Control.BackgroundProperty));
            Assert.False(calendar.HasLocalValue(Control.BorderBrushProperty));
            Assert.NotNull(calendar.Background);
            Assert.NotNull(calendar.BorderBrush);
            Assert.Equal(8, calendar.Padding.Left);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Calendar_InternalBrushResolution_ShouldUseThemeAccentBrush()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("AccentBrush", out var accentObj));
            var calendar = new Calendar();

            var resolveMethod = typeof(Calendar).GetMethod("ResolveCalendarBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveMethod);

            var brush = resolveMethod!.Invoke(calendar, new object[] { "AccentBrush", new SolidColorBrush(Color.Black) });
            Assert.Same(accentObj, brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Calendar_TextResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var calendar = new Calendar();

            var primaryMethod = typeof(Calendar).GetMethod("ResolvePrimaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectedMethod = typeof(Calendar).GetMethod("ResolveSelectedTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(primaryMethod);
            Assert.NotNull(selectedMethod);

            Assert.Same(app.Resources["TextPrimary"], primaryMethod!.Invoke(calendar, null));
            Assert.Same(app.Resources["TextOnAccent"], selectedMethod!.Invoke(calendar, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CalendarDayButton_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var accent = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var highlight = Assert.IsAssignableFrom<Brush>(app.Resources["HighlightBackground"]);
            var textDisabled = Assert.IsAssignableFrom<Brush>(app.Resources["TextDisabled"]);
            var textSecondary = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);
            var textOnAccent = Assert.IsAssignableFrom<Brush>(app.Resources["TextOnAccent"]);
            var textPrimary = Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]);

            var dayButton = new CalendarDayButton();

            Assert.Same(accent, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveSelectedBackgroundBrush"));
            Assert.Same(highlight, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveHighlightBackgroundBrush"));
            Assert.Same(textDisabled, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveBlackedOutForegroundBrush"));
            Assert.Same(textSecondary, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveInactiveForegroundBrush"));
            Assert.Same(textOnAccent, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveSelectedForegroundBrush"));
            Assert.Same(textPrimary, InvokeCalendarDayButtonBrushResolver(dayButton, "ResolveDefaultForegroundBrush"));

            var todayPen = InvokeCalendarDayButtonPenResolver(dayButton, "ResolveTodayRingPen");
            var strikePen = InvokeCalendarDayButtonPenResolver(dayButton, "ResolveStrikePen");

            Assert.Same(accent, todayPen.Brush);
            Assert.Equal(2, todayPen.Thickness);
            Assert.Same(textDisabled, strikePen.Brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void CalendarItem_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var surface = Assert.IsAssignableFrom<Brush>(app.Resources["SurfaceBackground"]);
            var textSecondary = Assert.IsAssignableFrom<Brush>(app.Resources["TextSecondary"]);

            var item = new CalendarItem();

            Assert.Same(surface, InvokeCalendarItemBrushResolver(item, "ResolveBackgroundBrush"));
            Assert.Same(textSecondary, InvokeCalendarItemBrushResolver(item, "ResolveDayOfWeekHeaderBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokeCalendarDayButtonBrushResolver(CalendarDayButton button, string methodName)
    {
        var method = typeof(CalendarDayButton).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(button, null));
    }

    private static Pen InvokeCalendarDayButtonPenResolver(CalendarDayButton button, string methodName)
    {
        var method = typeof(CalendarDayButton).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Pen>(method!.Invoke(button, null));
    }

    private static Brush InvokeCalendarItemBrushResolver(CalendarItem item, string methodName)
    {
        var method = typeof(CalendarItem).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(item, null));
    }
}
