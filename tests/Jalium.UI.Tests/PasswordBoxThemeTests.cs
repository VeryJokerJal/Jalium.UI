using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PasswordBoxThemeTests
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
    public void PasswordBox_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var passwordBox = new PasswordBox();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(passwordBox);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(PasswordBox), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(passwordBox.HasLocalValue(Control.BackgroundProperty));
            Assert.False(passwordBox.HasLocalValue(Control.ForegroundProperty));
            Assert.False(passwordBox.HasLocalValue(Control.BorderBrushProperty));

            Assert.NotNull(passwordBox.Background);
            Assert.NotNull(passwordBox.Foreground);
            Assert.NotNull(passwordBox.BorderBrush);
            Assert.Equal(36, passwordBox.MinHeight);
            Assert.True(passwordBox.RenderSize.Height >= 36);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void PasswordBox_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("TextPlaceholder", out var placeholderObj));
            Assert.True(app.Resources.TryGetValue("TextSecondary", out var secondaryObj));
            Assert.True(app.Resources.TryGetValue("ControlBorderFocused", out var focusedObj));

            var passwordBox = new PasswordBox();

            var placeholderMethod = typeof(PasswordBox).GetMethod("ResolvePlaceholderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var secondaryMethod = typeof(PasswordBox).GetMethod("ResolveSecondaryTextBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var focusedMethod = typeof(PasswordBox).GetMethod("ResolveFocusedBorderBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(placeholderMethod);
            Assert.NotNull(secondaryMethod);
            Assert.NotNull(focusedMethod);

            Assert.Same(placeholderObj, placeholderMethod!.Invoke(passwordBox, null));
            Assert.Same(secondaryObj, secondaryMethod!.Invoke(passwordBox, null));
            Assert.Same(focusedObj, focusedMethod!.Invoke(passwordBox, null));
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
