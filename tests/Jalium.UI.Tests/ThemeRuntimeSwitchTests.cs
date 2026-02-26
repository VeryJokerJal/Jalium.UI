using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeRuntimeSwitchTests
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
    public void ApplyTheme_ShouldUpdate_Button_TextBox_NavigationView_Brushing()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var root = new StackPanel { Width = 480, Height = 360 };
            var button = new Button { Content = "Apply" };
            var textBox = new TextBox { Text = "Theme" };
            var navigationView = new NavigationView { Height = 120 };
            root.Children.Add(button);
            root.Children.Add(textBox);
            root.Children.Add(navigationView);

            app.MainWindow = new Window { Content = root };
            root.Measure(new Size(480, 360));
            root.Arrange(new Rect(0, 0, 480, 360));

            var buttonBefore = GetBrushColor(button.Background);
            var textBoxBefore = GetBrushColor(textBox.Background);
            var navBefore = GetBrushColor(navigationView.Background);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var buttonAfter = GetBrushColor(button.Background);
            var textBoxAfter = GetBrushColor(textBox.Background);
            var navAfter = GetBrushColor(navigationView.Background);

            Assert.NotEqual(buttonBefore, buttonAfter);
            Assert.NotEqual(textBoxBefore, textBoxAfter);
            Assert.NotEqual(navBefore, navAfter);
            Assert.Equal(ThemeVariant.Light, ThemeManager.CurrentTheme);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyAccent_ShouldUpdate_AppBar_Selection_Progress_Resources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var root = new StackPanel { Width = 480, Height = 360 };
            var appBarButton = new AppBarButton { Label = "Accent" };
            var progressBar = new ProgressBar { Width = 220, Height = 8, Value = 30, Maximum = 100 };
            root.Children.Add(appBarButton);
            root.Children.Add(progressBar);

            app.MainWindow = new Window { Content = root };
            root.Measure(new Size(480, 360));
            root.Arrange(new Rect(0, 0, 480, 360));

            var appBarBefore = GetBrushColor(appBarButton.Foreground);
            var progressBefore = GetBrushColor(progressBar.ProgressBrush);

            var accent = Color.FromRgb(0x40, 0xB8, 0x5A);
            ThemeManager.ApplyAccent(accent);

            var appBarAfter = GetBrushColor(appBarButton.Foreground);
            var progressAfter = GetBrushColor(progressBar.ProgressBrush);
            var selection = Assert.IsType<SolidColorBrush>(app.Resources["SelectionBackground"]);

            Assert.NotEqual(appBarBefore, appBarAfter);
            Assert.NotEqual(progressBefore, progressAfter);
            Assert.Equal(accent, appBarAfter);
            Assert.Equal(accent, progressAfter);
            Assert.Equal(accent.R, selection.Color.R);
            Assert.Equal(accent.G, selection.Color.G);
            Assert.Equal(accent.B, selection.Color.B);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTypography_ShouldUpdate_TextualControl_FontFamilies()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var root = new StackPanel { Width = 480, Height = 240 };
            var textBlock = new TextBlock { Text = "Typography" };
            var button = new Button { Content = "Body" };
            root.Children.Add(textBlock);
            root.Children.Add(button);

            app.MainWindow = new Window { Content = root };
            root.Measure(new Size(480, 240));
            root.Arrange(new Rect(0, 0, 480, 240));

            ThemeManager.ApplyTypography("Georgia", "Calibri", "Consolas");

            Assert.Equal("Georgia", ThemeManager.CurrentDisplayFontFamily);
            Assert.Equal("Calibri", ThemeManager.CurrentBodyFontFamily);
            Assert.Equal("Consolas", ThemeManager.CurrentMonospaceFontFamily);
            Assert.Equal("Calibri", textBlock.FontFamily);
            Assert.Equal("Calibri", button.FontFamily);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Color GetBrushColor(Brush? brush)
    {
        return Assert.IsType<SolidColorBrush>(brush).Color;
    }
}
