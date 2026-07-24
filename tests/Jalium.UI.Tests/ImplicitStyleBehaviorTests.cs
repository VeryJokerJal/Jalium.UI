using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ImplicitStyleBehaviorTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ImplicitStyle_OverrideWithoutBasedOn_KeepsThemeTemplate()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var overrideBackground = new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33));
            var overrideStyle = new Style(typeof(ComboBox));
            overrideStyle.Setters.Add(new Setter(Control.BackgroundProperty, overrideBackground));
            app.Resources[typeof(ComboBox)] = overrideStyle;

            var host = new StackPanel { Width = 400, Height = 200 };
            var comboBox = new ComboBox { Width = 220, MinHeight = 32 };
            host.Children.Add(comboBox);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.Null(overrideStyle.BasedOn);
            // The user style's own setter wins…
            Assert.Same(overrideBackground, comboBox.Background);
            // …but the theme default style still supplies everything the user style
            // does not set — the control keeps its default Template and renders.
            Assert.NotNull(comboBox.Template);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
