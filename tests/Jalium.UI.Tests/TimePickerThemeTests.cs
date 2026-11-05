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
    public void TimePicker_DefaultTemplatePartsShouldUseTheCurrentThemeResources()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var picker = new TimePicker();
            picker.Measure(new Size(240, 80));
            picker.Arrange(new Rect(0, 0, 240, 80));
            var display = Assert.IsType<TextBlock>(picker.FindName("PART_DisplayText"));
            var input = Assert.IsType<Border>(picker.FindName("PART_InputBorder"));
            Assert.Same(app.Resources["TextPlaceholder"], display.Foreground);
            Assert.Same(app.Resources["SurfaceBackground"], input.Background);
            Assert.Same(app.Resources["ControlBorder"], input.BorderBrush);
            picker.SelectedTime = new TimeSpan(13, 30, 0);
            Assert.Equal("1:30 PM", display.Text);
            Assert.Same(app.Resources["TextPrimary"], display.Foreground);
            Assert.False(picker.HasLocalValue(Control.BackgroundProperty));
            Assert.False(picker.HasLocalValue(Control.BorderBrushProperty));
        }
        finally { ResetApplicationState(); }
    }
}
