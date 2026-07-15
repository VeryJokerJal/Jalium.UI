using System.Reflection;
using Jalium.UI;
using System.Diagnostics;
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
    public void ApplicationCtor_ShouldAutoInitializeTheme_WithoutManualJalxamlLoad()
    {
        ResetApplicationState();
        var originalLoader = ThemeManager.XamlLoader;
        ThemeManager.XamlLoader = null;

        try
        {
            var app = new Application();

            Assert.NotNull(ThemeManager.XamlLoader);
            Assert.True(ThemeManager.IsInitialized);
            Assert.True(app.Resources.TryGetValue(typeof(Button), out var buttonStyle));
            Assert.IsType<Style>(buttonStyle);
        }
        finally
        {
            ThemeManager.XamlLoader = originalLoader;
            ResetApplicationState();
        }
    }

    [Fact]
    public void RepeatedInitialize_IsBounded_AndMovesManagedResourcesToReplacementApplication()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var first = new Application();
        var liveBindings = new List<Border>();

        try
        {
            for (var i = 0; i < 2_000; i++)
            {
                var border = new Border();
                DynamicResourceBindingOperations.SetDynamicResource(
                    border,
                    Border.BackgroundProperty,
                    "AccentBrush");
                liveBindings.Add(border);
            }

            var managedDictionaries = first.Resources.MergedDictionaries.ToArray();
            var themeVersionField = typeof(ThemeManager).GetField(
                "_themeVersion",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var initialVersion = Assert.IsType<int>(themeVersionField.GetValue(null));

            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < 500; i++)
            {
                ThemeManager.Initialize(first);
            }

            Assert.Equal(initialVersion, Assert.IsType<int>(themeVersionField.GetValue(null)));

            typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, null);
            var replacement = new Application();

            for (var i = 0; i < 500; i++)
            {
                ThemeManager.Initialize(replacement);
            }
            stopwatch.Stop();

            Assert.True(replacement.Resources.TryGetValue(typeof(Button), out var buttonStyle));
            Assert.IsType<Style>(buttonStyle);
            Assert.IsAssignableFrom<Brush>(replacement.Resources["AccentBrush"]);
            Assert.IsType<FontFamily>(replacement.Resources["BodyFontFamily"]);
            Assert.All(managedDictionaries, dictionary =>
            {
                Assert.DoesNotContain(dictionary, first.Resources.MergedDictionaries);
                Assert.Contains(dictionary, replacement.Resources.MergedDictionaries);
            });
            Assert.Equal(initialVersion, Assert.IsType<int>(themeVersionField.GetValue(null)));
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"One thousand no-change Initialize calls took {stopwatch.Elapsed}.");
        }
        finally
        {
            foreach (var border in liveBindings)
            {
                DynamicResourceBindingOperations.ClearDynamicResource(border, Border.BackgroundProperty);
            }

            ResetApplicationState();
        }
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
            var progressBefore = Assert.IsType<LinearGradientBrush>(progressBar.ProgressBrush);
            Assert.Equal(2, progressBefore.GradientStops.Count);
            var progressBeforeStart = progressBefore.GradientStops[0].Color;
            var progressBeforeEnd = progressBefore.GradientStops[1].Color;

            var accent = Color.FromRgb(0x40, 0xB8, 0x5A);
            ThemeManager.ApplyAccent(accent);

            var appBarAfter = GetBrushColor(appBarButton.Foreground);
            var progressAfter = Assert.IsType<LinearGradientBrush>(progressBar.ProgressBrush);
            var accentBrush = Assert.IsType<LinearGradientBrush>(app.Resources["AccentBrush"]);
            var selection = Assert.IsType<SolidColorBrush>(app.Resources["SelectionBackground"]);

            Assert.NotEqual(appBarBefore, appBarAfter);
            Assert.Equal(2, progressAfter.GradientStops.Count);
            Assert.NotEqual(progressBeforeStart, progressAfter.GradientStops[0].Color);
            Assert.NotEqual(progressBeforeEnd, progressAfter.GradientStops[1].Color);
            Assert.Same(accentBrush, progressAfter);
            Assert.Equal(accent, appBarAfter);
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
            Assert.Equal("Calibri", textBlock.FontFamily.Source);
            Assert.Equal("Calibri", button.FontFamily.Source);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldReapplyImplicitStyles_InSecondaryWindows()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var mainButton = new Button { Content = "Main" };
            var mainRoot = new StackPanel();
            mainRoot.Children.Add(mainButton);
            app.MainWindow = new Window { Content = mainRoot };

            var secondaryButton = new Button { Content = "Secondary" };
            var secondaryRoot = new StackPanel();
            secondaryRoot.Children.Add(secondaryButton);
            var secondaryWindow = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 160,
                Height = 120,
                Content = secondaryRoot,
            };

            secondaryWindow.Show();
            try
            {
                var mainTemplateBefore = mainButton.Template;
                var secondaryTemplateBefore = secondaryButton.Template;
                Assert.NotNull(mainTemplateBefore);
                Assert.NotNull(secondaryTemplateBefore);

                ThemeManager.ApplyTheme(ThemeVariant.Light);

                // Theme switch replaces the generic dictionary, so implicit styles must be
                // re-evaluated in every live window — the refreshed styles carry brand-new
                // ControlTemplate instances.
                Assert.NotSame(mainTemplateBefore, mainButton.Template);
                Assert.NotSame(secondaryTemplateBefore, secondaryButton.Template);
            }
            finally
            {
                secondaryWindow.Close();
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldHealImplicitStyles_WhenSecondaryWindowShownAfterSwitch()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            app.MainWindow = new Window { Content = new StackPanel() };

            // Pre-instantiate a secondary window WITH content before the theme switch,
            // but leave it unshown — so it is not a live root and the broadcast in
            // Application.OnApplicationResourcesChanged cannot reach it.
            var button = new Button { Content = "Deferred" };
            var deferredRoot = new StackPanel();
            deferredRoot.Children.Add(button);
            var deferredWindow = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 160,
                Height = 120,
                Content = deferredRoot,
            };

            var templateBefore = button.Template;
            Assert.NotNull(templateBefore);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            // The broadcast cannot reach an unshown window, so its template stays stale.
            Assert.Same(templateBefore, button.Template);

            // Show() must detect the missed theme version and heal the subtree.
            deferredWindow.Show();
            try
            {
                Assert.NotSame(templateBefore, button.Template);
            }
            finally
            {
                deferredWindow.Close();
            }
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
