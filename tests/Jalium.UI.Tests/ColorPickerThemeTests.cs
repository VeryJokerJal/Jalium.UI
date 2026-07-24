using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ColorPickerThemeTests
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
    public void ColorPicker_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var colorPicker = new ColorPicker();
            var host = new StackPanel { Width = 320, Height = 320 };
            host.Children.Add(colorPicker);

            host.Measure(new Size(320, 320));
            host.Arrange(new Rect(0, 0, 320, 320));

            Assert.True(app.Resources.TryGetValue(typeof(ColorPicker), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(colorPicker.HasLocalValue(Control.BackgroundProperty));
            Assert.NotNull(colorPicker.Background);
            Assert.NotNull(colorPicker.BorderBrush);
            Assert.Equal(10, colorPicker.Padding.Left);
            Assert.Equal(6, colorPicker.Padding.Top);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ColorPicker_BorderBrush_ShouldDriveRenderedPickerBorders()
    {
        var borderBrush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var colorPicker = new ColorPicker
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(3)
        };

        var getBorderPen = typeof(ColorPicker).GetMethod("GetBorderPen",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(getBorderPen);

        var pen = Assert.IsType<Pen>(getBorderPen!.Invoke(colorPicker, null));
        Assert.Same(borderBrush, pen.Brush);
        Assert.Equal(3, pen.Thickness);
    }

    [Fact]
    public void ColorPicker_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var colorPicker = new ColorPicker();

            var foregroundMethod = typeof(ColorPicker).GetMethod("ResolveForegroundBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var borderMethod = typeof(ColorPicker).GetMethod("ResolveBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(foregroundMethod);
            Assert.NotNull(borderMethod);

            Assert.Same(app.Resources["TextPrimary"], foregroundMethod!.Invoke(colorPicker, null));
            Assert.Same(app.Resources["ControlBorder"], borderMethod!.Invoke(colorPicker, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
