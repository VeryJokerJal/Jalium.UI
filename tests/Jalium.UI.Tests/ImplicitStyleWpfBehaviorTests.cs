using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ImplicitStyleWpfBehaviorTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ImplicitStyle_OverrideWithoutBasedOn_ShouldNotKeepDefaultTemplate()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var overrideStyle = new Style(typeof(ComboBox));
            overrideStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33))));
            overrideStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE))));
            app.Resources[typeof(ComboBox)] = overrideStyle;

            var host = new StackPanel { Width = 400, Height = 200 };
            var comboBox = new ComboBox { Width = 220, MinHeight = 32 };
            host.Children.Add(comboBox);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            Assert.Null(overrideStyle.BasedOn);
            Assert.Null(comboBox.Template);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
