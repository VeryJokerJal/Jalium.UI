using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DatePickerThemeTests
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
    public void DatePicker_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var datePicker = new DatePicker();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(datePicker);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(DatePicker), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(datePicker.HasLocalValue(Control.BackgroundProperty));
            Assert.False(datePicker.HasLocalValue(Control.BorderBrushProperty));
            Assert.False(datePicker.HasLocalValue(FrameworkElement.HeightProperty));

            Assert.NotNull(datePicker.Background);
            Assert.NotNull(datePicker.BorderBrush);
            Assert.Equal(36, datePicker.MinHeight);
            Assert.True(datePicker.RenderSize.Height >= 36);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DatePicker_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var secondaryObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));

            var datePicker = new DatePicker();

            var placeholderMethod = typeof(DatePicker).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var secondaryMethod = typeof(DatePicker).GetMethod("ResolveSecondaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(DatePicker).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(placeholderMethod);
            Assert.NotNull(secondaryMethod);
            Assert.NotNull(focusedMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(datePicker, null));
            Assert.Same(secondaryObj, secondaryMethod!.Invoke(datePicker, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(datePicker, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
