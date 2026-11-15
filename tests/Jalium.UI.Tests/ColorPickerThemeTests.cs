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
    public void ColorPicker_BorderBrush_ShouldDriveTheTemplateAndColorParts()
    {
        ResetApplicationState(); var app=new Application();
        try
        {
            var brush=new SolidColorBrush(Color.FromRgb(12,34,56));
            var picker=new ColorPicker { BorderBrush=brush,BorderThickness=new Thickness(3) };
            picker.Measure(new Size(240,400));picker.Arrange(new Rect(0,0,240,400));
            var root=Assert.IsType<Border>(picker.FindName("PART_Root"));
            Assert.Same(brush,root.BorderBrush);Assert.Equal(new Thickness(3),root.BorderThickness);
            Assert.Same(brush,Assert.IsType<ColorSpectrum>(picker.FindName("PART_Spectrum")).BorderBrush);
            Assert.Same(brush,Assert.IsType<ColorRamp>(picker.FindName("PART_HueSlider")).BorderBrush);
        }
        finally { ResetApplicationState(); }
    }

    [Fact]
    public void ColorPicker_DefaultPartsUseThemeResources()
    {
        ResetApplicationState();var app=new Application();
        try
        {
            var picker=new ColorPicker();picker.Measure(new Size(240,400));picker.Arrange(new Rect(0,0,240,400));
            Assert.Same(app.Resources["TextPrimary"],picker.Foreground);
            Assert.Same(app.Resources["ControlBorder"],picker.BorderBrush);
            Assert.Same(picker.BorderBrush,Assert.IsType<ColorPreview>(picker.FindName("PART_Preview")).BorderBrush);
        }
        finally { ResetApplicationState(); }
    }
}
