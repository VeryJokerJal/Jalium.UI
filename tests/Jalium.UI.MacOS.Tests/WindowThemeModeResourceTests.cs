#pragma warning disable WPF0001
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WindowThemeModeResourceTests : IDisposable
{
    private readonly object? _theme = ResourceDictionary.CurrentThemeKey;
    private readonly Func<object, object?>? _applicationLookup = ResourceLookup.ApplicationResourceLookup;
    private readonly List<Window> _windows = [];
    private readonly ResourceDictionary _palette = Palette();

    public WindowThemeModeResourceTests() => ResourceDictionary.CurrentThemeKey = "Dark";

    [Fact]
    public void LightAndDarkWindowsResolveSharedPaletteIndependently()
    {
        var light = Create(ThemeMode.Light);
        var dark = Create(ThemeMode.Dark);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal("light", light.TryFindResource("Token"));
            Assert.Equal("dark", dark.TryFindResource("Token"));
        }
        Assert.Same(light.Resources.MergedDictionaries[0], dark.Resources.MergedDictionaries[0]);
    }

    [Theory]
    [InlineData("Light", "Dark", "light")]
    [InlineData("Dark", "Light", "dark")]
    public void ExplicitWindowThemeOverridesTheGlobalPalette(string requested, string global, string expected)
    {
        ResourceDictionary.CurrentThemeKey = global;
        var owner = Create(new ThemeMode(requested));
        Assert.Equal(expected, owner.TryFindResource("Token"));
        Assert.Equal(global, ResourceDictionary.CurrentThemeKey);
    }

    [Fact]
    public void RuntimeThemeChangeRefreshesExistingDynamicResources()
    {
        var owner = Create(ThemeMode.Dark);
        var border = new Border();
        owner.Content = border;
        border.SetResourceReference(Border.BackgroundProperty, "PaletteBrush");
        Assert.Same(_palette.ThemeDictionaries["Dark"]["PaletteBrush"], border.Background);
        owner.ThemeMode = ThemeMode.Light;
        Assert.Same(_palette.ThemeDictionaries["Light"]["PaletteBrush"], border.Background);
        owner.ThemeMode = ThemeMode.Dark;
        Assert.Same(_palette.ThemeDictionaries["Dark"]["PaletteBrush"], border.Background);
        Assert.Same(border, owner.Content);
    }

    [Fact]
    public void ResourcesRefreshWhenAThemeDictionaryBecomesAnAncestor()
    {
        var owner = Create(ThemeMode.Light);
        var child = new Border();
        child.SetResourceReference(Border.BackgroundProperty, "PaletteBrush");
        owner.Content = child;
        Assert.Same(_palette.ThemeDictionaries["Light"]["PaletteBrush"], child.Background);
    }

    [Fact]
    public void AWindowThemeIsAResourceScopeEvenWithoutLocalResources()
    {
        ResourceLookup.ApplicationResourceLookup = key => _palette.TryGetValue(key, out var value) ? value : null;
        var owner = Create(ThemeMode.Light, includePalette: false);
        var child = new Border();
        child.SetResourceReference(Border.BackgroundProperty, "PaletteBrush");
        Assert.Same(_palette.ThemeDictionaries["Dark"]["PaletteBrush"], child.Background);
        owner.Content = child;
        Assert.Same(_palette.ThemeDictionaries["Light"]["PaletteBrush"], child.Background);
    }

    [Fact]
    public void ExplicitWindowThemeSurvivesGlobalPaletteChanges()
    {
        var owner = Create(ThemeMode.Light);
        Assert.Equal("light", owner.TryFindResource("Token"));
        foreach (string global in new[] { "Light", "Dark", "Light", "Dark" })
        {
            ResourceDictionary.CurrentThemeKey = global;
            Assert.Equal("light", owner.TryFindResource("Token"));
        }
    }

    [Fact]
    public void NoneRestoresTheApplicationPalette()
    {
        var owner = Create(ThemeMode.Light);
        Assert.Equal("light", owner.TryFindResource("Token"));
        owner.ThemeMode = ThemeMode.None;
        Assert.Equal("dark", owner.TryFindResource("Token"));
        ResourceDictionary.CurrentThemeKey = "Light";
        Assert.Equal("light", owner.TryFindResource("Token"));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void HighContrastOverridesAnExplicitWindowTheme(string requested)
    {
        _palette.ThemeDictionaries["HighContrast"] = new ResourceDictionary { ["Token"] = "contrast" };
        ResourceDictionary.CurrentThemeKey = "HighContrast";
        var owner = Create(new ThemeMode(requested));
        Assert.Equal("contrast", owner.TryFindResource("Token"));
        owner.ThemeMode = requested == "Light" ? ThemeMode.Dark : ThemeMode.Light;
        Assert.Equal("contrast", owner.TryFindResource("Token"));
    }

    [Fact]
    public void OrdinaryLocalResourcesKeepPriorityOverThemeVariants()
    {
        var owner = Create(ThemeMode.Light);
        owner.Resources["Token"] = "custom";
        Assert.Equal("custom", owner.TryFindResource("Token"));
        owner.ThemeMode = ThemeMode.Dark;
        Assert.Equal("custom", owner.TryFindResource("Token"));
    }

    [Fact]
    public void NullLocalResourceStillShadowsThePalette()
    {
        var owner = Create(ThemeMode.Light);
        owner.Resources["Token"] = null;
        Assert.Null(owner.TryFindResource("Token"));
    }

    [Fact]
    public void SharedStyleResourcesUseTheRequestingWindowsTheme()
    {
        var style = new Style(typeof(Border));
        style.Resources.MergedDictionaries.Add(_palette);
        var lightChild = new Border { Style = style };
        var darkChild = new Border { Style = style };
        var light = Create(ThemeMode.Light);
        var dark = Create(ThemeMode.Dark);
        light.Content = lightChild;
        dark.Content = darkChild;
        Assert.Equal("light", lightChild.TryFindResource("Token"));
        Assert.Equal("dark", darkChild.TryFindResource("Token"));
    }

    [Fact]
    public void MovingContentToAnotherWindowChangesItsResourceTheme()
    {
        var light = Create(ThemeMode.Light);
        var dark = Create(ThemeMode.Dark);
        var child = new Border();
        light.Content = child;
        Assert.Equal("light", child.TryFindResource("Token"));
        light.Content = null;
        dark.Content = child;
        Assert.Equal("dark", child.TryFindResource("Token"));
    }

    [Fact]
    public void ApplicationResourceCallbacksUseTheWindowContextWithoutChangingTheGlobalKey()
    {
        ResourceLookup.ApplicationResourceLookup = key =>
        {
            Assert.Equal("Dark", ResourceDictionary.CurrentThemeKey);
            return _palette.TryGetValue(key, out var value) ? value : null;
        };
        var owner = Create(ThemeMode.Light, includePalette: false);
        Assert.Equal("light", owner.TryFindResource("Token"));
        Assert.Equal("dark", _palette["Token"]);
    }

    [Fact]
    public void LookupFailureDoesNotLeaveAWindowContextActive()
    {
        var owner = Create(ThemeMode.Light, includePalette: false);
        ResourceLookup.ApplicationResourceLookup = key => Equals(key, "ThrowingToken")
            ? throw new InvalidOperationException("probe") : null;
        Assert.Throws<InvalidOperationException>(() => owner.TryFindResource("ThrowingToken"));
        Assert.Equal("Dark", ResourceDictionary.CurrentThemeKey);
        Assert.Equal("dark", _palette["Token"]);
    }

    private Window Create(ThemeMode mode, bool includePalette = true)
    {
        var owner = new Window { ThemeMode = mode };
        _windows.Add(owner);
        if (includePalette) owner.Resources.MergedDictionaries.Add(_palette);
        return owner;
    }

    private static ResourceDictionary Palette()
    {
        var result = new ResourceDictionary();
        result.ThemeDictionaries["Light"] = new ResourceDictionary
        {
            ["Token"] = "light", ["PaletteBrush"] = new SolidColorBrush(Color.FromRgb(0xF6, 0xF7, 0xF8))
        };
        result.ThemeDictionaries["Dark"] = new ResourceDictionary
        {
            ["Token"] = "dark", ["PaletteBrush"] = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x26))
        };
        return result;
    }

    public void Dispose()
    {
        ResourceLookup.ApplicationResourceLookup = _applicationLookup;
        foreach (var owner in _windows) owner.Close();
        ResourceDictionary.CurrentThemeKey = _theme;
    }
}
