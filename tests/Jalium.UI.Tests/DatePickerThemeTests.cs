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
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
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
            var host = new StackPanel
            {
                Width = 320,
                Height = 80
            };
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
    public void DatePicker_DefaultTemplateUsesThemeBrushesAndActualParts()
    {
        ResetApplicationState();
        var app = new Application();
        try
        {
            var picker = new DatePicker
            {
                PlaceholderText = "Choose"
            };
            var host = new StackPanel();
            host.Children.Add(picker);
            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));
            var input = Assert.IsType<Jalium.UI.Controls.Primitives.DatePickerTextBox>(picker.FindName("PART_TextBox"));
            var border = Assert.IsType<Border>(picker.FindName("PART_Root"));
            Assert.Equal("Choose", input.PlaceholderText);
            Assert.Same(app.Resources["TextPrimary"], input.Foreground);
            Assert.Same(app.Resources["ControlBorder"], border.BorderBrush);
            Assert.IsType<Button>(picker.FindName("PART_Button"));
            Assert.IsType<Jalium.UI.Controls.Primitives.Popup>(picker.FindName("PART_Popup"));
            Assert.IsType<Calendar>(picker.FindName("PART_Calendar"));
            Assert.Null(typeof(DatePicker).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
