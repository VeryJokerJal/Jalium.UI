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
    private static readonly FieldInfo s_isRenderDirtyField =
        typeof(Visual).GetField("_isRenderDirty", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Visual._isRenderDirty field not found.");

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        typeof(ThemeManager).GetField("_controlThemeDictionaries", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, Array.Empty<ResourceDictionary>());

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
            var buttonForegroundBefore = GetBrushColor(button.Foreground);
            var textBoxBefore = GetBrushColor(textBox.Background);
            var navBefore = GetBrushColor(navigationView.Background);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var buttonAfter = GetBrushColor(button.Background);
            var buttonForegroundAfter = GetBrushColor(button.Foreground);
            var textBoxAfter = GetBrushColor(textBox.Background);
            var navAfter = GetBrushColor(navigationView.Background);

            Assert.NotEqual(buttonBefore, buttonAfter);
            Assert.NotEqual(buttonForegroundBefore, buttonForegroundAfter);
            Assert.Equal(
                GetBrushColor(Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"])),
                buttonForegroundAfter);
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
    public void ImplicitComponentStyles_WithForeground_ShouldUseDynamicThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var failures = EnumerateResourceDictionaries(app.Resources)
                .SelectMany(static dictionary => dictionary.Keys
                    .Cast<object>()
                    .Where(static key => key is Type)
                    .Select(key => (TargetType: (Type)key, Style: dictionary[key] as Style)))
                .Where(static entry =>
                    entry.Style != null &&
                    entry.TargetType.GetProperty(
                        nameof(Control.Foreground),
                        BindingFlags.Instance | BindingFlags.Public) != null)
                .Where(static entry =>
                {
                    var setter = FindSetter(entry.Style!, nameof(Control.Foreground));
                    return setter?.Value is not IDynamicResourceReference;
                })
                .Select(static entry => entry.TargetType.FullName ?? entry.TargetType.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                failures.Length == 0,
                "Implicit component styles with a Foreground property must bind it " +
                "to a dynamic theme resource. Missing: " +
                string.Join(", ", failures));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldUpdate_DefaultForeground_AcrossComponentFamilies()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var controls = new Control[]
            {
                new DatePicker(),
                new InfoBar(),
                new DataGrid(),
                new Markdown(),
                new NavigationView(),
                new ComboBox(),
                new TabControl(),
                new TextBox(),
                new Jalium.UI.Controls.Primitives.ToggleButton(),
                new TreeView(),
            };
            var textBlock = new TextBlock { Text = "Theme" };
            var root = new StackPanel { Width = 640, Height = 900 };
            foreach (var control in controls)
            {
                root.Children.Add(control);
            }
            root.Children.Add(textBlock);

            app.MainWindow = new Window { Content = root };
            root.Measure(new Size(640, 900));
            root.Arrange(new Rect(0, 0, 640, 900));

            var before = controls
                .Select(static control => GetBrushColor(control.Foreground))
                .Append(GetBrushColor(textBlock.Foreground))
                .ToArray();

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            var expected = GetBrushColor(
                Assert.IsAssignableFrom<Brush>(app.Resources["TextPrimary"]));
            var after = controls
                .Select(static control => GetBrushColor(control.Foreground))
                .Append(GetBrushColor(textBlock.Foreground))
                .ToArray();

            Assert.Equal(before.Length, after.Length);
            for (var i = 0; i < after.Length; i++)
            {
                Assert.NotEqual(before[i], after[i]);
                Assert.Equal(expected, after[i]);
            }
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

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void ApplyTypography_InvalidBodyFontSize_UsesFiniteDefault(double bodyFontSize)
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            ThemeManager.ApplyTypography("Georgia", "Calibri", "Consolas", bodyFontSize);

            Assert.Equal(FrameworkElement.DefaultFontSize, ThemeManager.CurrentBodyFontSize);
            Assert.Equal(
                FrameworkElement.DefaultFontSize,
                Assert.IsType<double>(app.Resources["BodyFontSize"]));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_InvalidVariant_ThrowsWithoutMutatingThemeState()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        _ = new Application();

        try
        {
            ThemeVariant themeBefore = ThemeManager.CurrentTheme;
            object? keyBefore = ResourceDictionary.CurrentThemeKey;
            int versionBefore = ThemeManager.CurrentThemeVersion;

            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThemeManager.ApplyTheme((ThemeVariant)int.MaxValue));

            Assert.Equal(themeBefore, ThemeManager.CurrentTheme);
            Assert.Equal(keyBefore, ResourceDictionary.CurrentThemeKey);
            Assert.Equal(versionBefore, ThemeManager.CurrentThemeVersion);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Reset_DetachesOnlyThemeManagedDictionaries_AndAllowsCleanReinitialize()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        var userDictionary = new ResourceDictionary();
        app.Resources.MergedDictionaries.Insert(0, userDictionary);
        ResourceDictionary[] managedDictionaries = app.Resources.MergedDictionaries
            .Where(dictionary => !ReferenceEquals(dictionary, userDictionary))
            .ToArray();

        try
        {
            Assert.NotEmpty(managedDictionaries);

            ThemeManager.Reset();

            Assert.False(ThemeManager.IsInitialized);
            Assert.Single(app.Resources.MergedDictionaries);
            Assert.Same(userDictionary, app.Resources.MergedDictionaries[0]);
            foreach (ResourceDictionary dictionary in managedDictionaries)
            {
                Assert.DoesNotContain(
                    app.Resources.MergedDictionaries,
                    candidate => ReferenceEquals(candidate, dictionary));
            }

            ThemeManager.Initialize(app);

            Assert.True(ThemeManager.IsInitialized);
            Assert.Contains(userDictionary, app.Resources.MergedDictionaries);
            Assert.Equal(managedDictionaries.Length + 1, app.Resources.MergedDictionaries.Count);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldPreserveTemplates_AndUpdateResources_InSecondaryWindows()
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
                var mainBackgroundBefore = GetBrushColor(mainButton.Background);
                var secondaryBackgroundBefore = GetBrushColor(secondaryButton.Background);
                Assert.NotNull(mainTemplateBefore);
                Assert.NotNull(secondaryTemplateBefore);

                ThemeManager.ApplyTheme(ThemeVariant.Light);

                // Templates are theme-invariant and their palette setters are ThemeResources.
                // Switching variants must update both roots without rebuilding either template.
                Assert.Same(mainTemplateBefore, mainButton.Template);
                Assert.Same(secondaryTemplateBefore, secondaryButton.Template);
                Assert.NotEqual(mainBackgroundBefore, GetBrushColor(mainButton.Background));
                Assert.NotEqual(secondaryBackgroundBefore, GetBrushColor(secondaryButton.Background));
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
    public void ApplyTheme_ShouldUpdateSecondaryWindow_WhenMainWindowIsNull()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            Assert.Null(app.MainWindow);

            var button = new Button { Content = "Secondary only" };
            var root = new StackPanel();
            root.Children.Add(button);
            var secondaryWindow = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 160,
                Height = 120,
                Content = root,
            };

            secondaryWindow.Show();
            try
            {
                Assert.Null(app.MainWindow);
                Assert.Contains(secondaryWindow, app.Windows.Cast<Window>());

                var templateBefore = button.Template;
                var backgroundBefore = GetBrushColor(button.Background);
                Assert.NotNull(templateBefore);

                ThemeManager.ApplyTheme(ThemeVariant.Light);

                Assert.Same(templateBefore, button.Template);
                Assert.NotEqual(backgroundBefore, GetBrushColor(button.Background));
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
    public void ApplyTheme_ShouldUpdateUnshownWindow_WithoutRebuildingItsTemplate()
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
            var backgroundBefore = GetBrushColor(button.Background);
            Assert.NotNull(templateBefore);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            // The global ThemeResource registry includes unshown trees, so their palette is
            // current before Show() and the already-built template remains reusable.
            Assert.Same(templateBefore, button.Template);
            Assert.NotEqual(backgroundBefore, GetBrushColor(button.Background));

            deferredWindow.Show();
            try
            {
                Assert.Same(templateBefore, button.Template);
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

    [Fact]
    public void ApplyTheme_ShouldReuseGenericDictionary_AndUseSingleLightweightBroadcast()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var root = new StackPanel();
            var button = new Button { Content = "Probe" };
            root.Children.Add(button);
            app.MainWindow = new Window { Content = root };

            var buttonStyleBefore = app.Resources[typeof(Button)];
            var buttonTemplateBefore = button.Template;
            var broadcasts = 0;
            EventHandler handler = (_, _) => broadcasts++;
            app.ResourcesChanged += handler;

            var originalLoader = ThemeManager.XamlLoader;
            Assert.NotNull(originalLoader);
            var genericLoads = 0;
            ThemeManager.XamlLoader = (stream, path, assembly) =>
            {
                genericLoads++;
                return originalLoader(stream, path, assembly);
            };

            try
            {
                ThemeManager.ApplyTheme(ThemeVariant.Light);
            }
            finally
            {
                ThemeManager.XamlLoader = originalLoader;
            }

            app.ResourcesChanged -= handler;

            Assert.Equal(0, genericLoads);
            Assert.Equal(1, broadcasts);
            Assert.Same(buttonStyleBefore, app.Resources[typeof(Button)]);
            Assert.Same(buttonTemplateBefore, button.Template);
            Assert.Equal(ThemeVariant.Light, ThemeManager.CurrentTheme);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_WithCurrentVariant_ShouldBeNoOp()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            app.MainWindow = new Window { Content = new Button() };
            var versionBefore = ThemeManager.CurrentThemeVersion;
            var broadcasts = 0;
            EventHandler handler = (_, _) => broadcasts++;
            app.ResourcesChanged += handler;

            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme);

            app.ResourcesChanged -= handler;
            Assert.Equal(0, broadcasts);
            Assert.Equal(versionBefore, ThemeManager.CurrentThemeVersion);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldRefreshNonVisualThemeResources()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();
        var host = new Border();
        var brush = new SolidColorBrush();

        try
        {
            app.MainWindow = new Window { Content = host };
            DynamicResourceBindingOperations.SetDynamicResourceForNonVisual(
                host,
                brush,
                SolidColorBrush.ColorProperty,
                "TextFillColorPrimary");
            var before = brush.Color;

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            Assert.NotEqual(before, brush.Color);
        }
        finally
        {
            DynamicResourceBindingOperations.ClearDynamicResourceForNonVisual(
                brush,
                SolidColorBrush.ColorProperty);
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyTheme_ShouldInvalidateThemeConsumers_NotResourceIndependentElements()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var themed = new Button { Content = "Themed" };
            var independent = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x12, 0x34, 0x56))
            };
            var root = new StackPanel();
            root.Children.Add(themed);
            root.Children.Add(independent);
            app.MainWindow = new Window { Content = root };

            // Materialize the themed value and the independent brush-owner registration before
            // clearing the retained-render flags. Only the former should change with the variant.
            var themedBackgroundBefore = GetBrushColor(themed.Background);
            _ = independent.Background;
            s_isRenderDirtyField.SetValue(themed, false);
            s_isRenderDirtyField.SetValue(independent, false);

            ThemeManager.ApplyTheme(ThemeVariant.Light);

            Assert.NotEqual(themedBackgroundBefore, GetBrushColor(themed.Background));
            Assert.True((bool)s_isRenderDirtyField.GetValue(themed)!);
            Assert.False((bool)s_isRenderDirtyField.GetValue(independent)!);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyBrandTheme_ShouldReevaluateEachLiveRoot_ExactlyOnce()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            // Main window with a probe element.
            var mainButton = new Button { Content = "Main" };
            var mainRoot = new StackPanel();
            mainRoot.Children.Add(mainButton);
            app.MainWindow = new Window { Content = mainRoot };

            // A live secondary window (shown, hence reachable by the resource broadcast) with
            // its own probe element, so we can assert the multi-window fan-out cost directly.
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
                // Number of full-tree broadcasts fired by the whole brand-theme application.
                var broadcasts = 0;
                EventHandler broadcastHandler = (_, _) => broadcasts++;
                app.ResourcesChanged += broadcastHandler;

                // Each broadcast walks every live root's subtree once, raising ResourcesChanged
                // on every element it visits. Counting a probe in each window measures how many
                // times that window's tree is re-evaluated by a single ApplyBrandTheme call.
                var mainReevaluations = 0;
                var secondaryReevaluations = 0;
                mainButton.ResourcesChanged += (_, _) => mainReevaluations++;
                secondaryButton.ResourcesChanged += (_, _) => secondaryReevaluations++;

                ThemeManager.ApplyBrandTheme(new BrandThemeOptions
                {
                    Theme = ThemeVariant.Light,
                    AccentColor = Color.FromRgb(0x40, 0xB8, 0x5A),
                    DisplayFontFamily = "Georgia",
                    BodyFontFamily = "Calibri",
                    MonoFontFamily = "Consolas",
                });

                app.ResourcesChanged -= broadcastHandler;

                // ApplyBrandTheme internally replaces the generic, accent AND typography
                // dictionaries (four ReplaceManagedDictionary calls across ApplyTheme + ApplyAccent
                // + ApplyTypography). Every one of those swaps used to fire its own whole-tree
                // broadcast, so a single brand switch re-evaluated each live window/popup FOUR
                // times. Batching them under one DeferNotifications scope collapses that to one.
                Assert.Equal(1, broadcasts);
                Assert.Equal(1, mainReevaluations);
                Assert.Equal(1, secondaryReevaluations);

                // Sanity: the brand theme actually took effect, so the single broadcast is not a
                // degenerate "nothing changed" no-op.
                Assert.Equal(ThemeVariant.Light, ThemeManager.CurrentTheme);
                Assert.Equal("Calibri", ThemeManager.CurrentBodyFontFamily);
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

    private static Color GetBrushColor(Brush? brush)
    {
        return Assert.IsType<SolidColorBrush>(brush).Color;
    }

    private static IEnumerable<ResourceDictionary> EnumerateResourceDictionaries(
        ResourceDictionary root)
    {
        yield return root;

        foreach (var dictionary in root.MergedDictionaries)
        {
            foreach (var nested in EnumerateResourceDictionaries(dictionary))
            {
                yield return nested;
            }
        }
    }

    private static Setter? FindSetter(Style style, string propertyName)
    {
        for (var current = style; current != null; current = current.BasedOn)
        {
            var setter = current.Setters
                .OfType<Setter>()
                .FirstOrDefault(candidate =>
                    candidate.TargetName == null &&
                    (candidate.Property?.Name == propertyName ||
                     candidate.PropertyName == propertyName));
            if (setter != null)
            {
                return setter;
            }
        }

        return null;
    }
}
