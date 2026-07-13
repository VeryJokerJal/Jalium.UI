using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// WPF parity: a user Style — explicit or implicit, with or without BasedOn — layers ON TOP
/// of the theme default style. Properties the user style does not set (most critically the
/// control Template) keep falling back to the theme defaults, so styling a control never
/// makes it lose its visual tree. (Regression: Calculator-template buttons vanished because
/// an explicit Style removed the theme style including its Template setter.)
/// </summary>
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

    [Fact]
    public void ExplicitStyle_WithoutTemplateSetter_KeepsThemeTemplate()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var styled = new Button { Content = "styled" };
            styled.Style = new Style(typeof(Button)); // empty style — no setters at all

            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(styled);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            // An explicit Style must not strip the theme default template.
            Assert.NotNull(styled.Template);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ExplicitStyle_SetterOverridesThemeSetter_OthersFallBack()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var explicitBackground = new SolidColorBrush(Color.FromRgb(0x17, 0x28, 0x3D));
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, explicitBackground));

            var styled = new Button { Content = "styled", Style = style };
            var plain = new Button { Content = "plain" };

            var host = new StackPanel { Width = 400, Height = 200 };
            host.Children.Add(styled);
            host.Children.Add(plain);

            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));

            // Explicit style setter beats the theme setter for the property it sets…
            Assert.Same(explicitBackground, styled.Background);
            // …while unset properties match the theme-styled control exactly.
            Assert.NotNull(styled.Template);
            Assert.Same(plain.Template, styled.Template);
            Assert.Equal(plain.MinHeight, styled.MinHeight);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
