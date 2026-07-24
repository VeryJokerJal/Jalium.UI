using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class TimePickerThemeTests
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
    public void TimePicker_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var timePicker = new TimePicker();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(timePicker);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(TimePicker), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(timePicker.HasLocalValue(Control.BackgroundProperty));
            Assert.False(timePicker.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(timePicker.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.NotNull(timePicker.Background);
            Assert.NotNull(timePicker.BorderBrush);
            Assert.Equal(36, timePicker.MinHeight);
            Assert.True(timePicker.RenderSize.Height >= 36);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TimePicker_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var secondaryObj));
            Assert.True(app.Resources.TryGetValue("AccentBrush", out var accentObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));

            var timePicker = new TimePicker();

            var placeholderMethod = typeof(TimePicker).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var secondaryMethod = typeof(TimePicker).GetMethod("ResolveSecondaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var selectedMethod = typeof(TimePicker).GetMethod("ResolveSelectedItemBackground",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(TimePicker).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(placeholderMethod);
            Assert.NotNull(secondaryMethod);
            Assert.NotNull(selectedMethod);
            Assert.NotNull(focusedMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(timePicker, null));
            Assert.Same(secondaryObj, secondaryMethod!.Invoke(timePicker, null));
            Assert.Same(accentObj, selectedMethod!.Invoke(timePicker, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(timePicker, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
