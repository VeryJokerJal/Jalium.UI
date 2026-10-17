#pragma warning disable WPF0001
using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class GeneratedWindowThemeTests : IDisposable
{
    private readonly object? _theme = ResourceDictionary.CurrentThemeKey;
    private readonly List<Window> _windows = [];

    public GeneratedWindowThemeTests() => ResourceDictionary.CurrentThemeKey = "Dark";

    [Fact]
    public void MarkupUsesTheExistingThemeModeConverter()
    {
        var converter = TypeConverterRegistry.GetConverter(typeof(ThemeMode));
        Assert.NotNull(converter);
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.Equal(ThemeMode.Light, TypeConverterRegistry.ConvertValue("Light", typeof(ThemeMode)));
    }

    [Theory]
    [InlineData("None")] [InlineData("Light")] [InlineData("Dark")] [InlineData("System")]
    public void TypeDescriptorConvertsTheSupportedModes(string name)
    {
        var converter = TypeDescriptor.GetConverter(typeof(ThemeMode));
        Assert.IsType<ThemeModeConverter>(converter);
        Assert.Equal(new ThemeMode(name), converter.ConvertFromInvariantString(name));
    }

    [Fact]
    public void GeneratedAttributeSetsTheModeBeforeResolvingVisualResources()
    {
        var owner = Create();
        Assert.Equal(ThemeMode.Light, owner.ThemeMode);
        Assert.Equal(2, owner.Resources.ThemeDictionaries.Count);
        Assert.Equal(Light, owner.StaticBrush.Color);
        Assert.Equal(Light, owner.DynamicBrush.Color);
    }

    [Fact]
    public void StaticResourcesKeepTheirInitialValueWhileThemeResourcesFollowTheWindow()
    {
        var owner = Create();
        var staticBrush = owner.StaticBrush;
        owner.ThemeMode = ThemeMode.Dark;
        Assert.Same(staticBrush, owner.StaticBrush);
        Assert.Equal(Light, owner.StaticBrush.Color);
        Assert.Equal(Dark, owner.DynamicBrush.Color);
        owner.ThemeMode = ThemeMode.Light;
        Assert.Equal(Light, owner.DynamicBrush.Color);
    }

    [Fact]
    public void TwoGeneratedWindowsKeepTheirOwnTheme()
    {
        var first = Create();
        var second = Create();
        second.ThemeMode = ThemeMode.Dark;
        Assert.Equal(Light, first.DynamicBrush.Color);
        Assert.Equal(Dark, second.DynamicBrush.Color);
        Assert.Equal(Light, second.StaticBrush.Color);
    }

    [Fact]
    public void ComponentStaticResourcesKeepPriorityOverApplicationResources()
    {
        var previous = ResourceLookup.ApplicationResourceLookup;
        try
        {
            ResourceLookup.ApplicationResourceLookup = key => Equals(key, "ThemeProbeBrush")
                ? new SolidColorBrush(Color.FromRgb(255, 0, 0)) : previous?.Invoke(key);
            var owner = Create();
            Assert.Equal(Light, owner.StaticBrush.Color);
            Assert.Equal(Light, owner.DynamicBrush.Color);
        }
        finally { ResourceLookup.ApplicationResourceLookup = previous; }
    }

    private GeneratedWindowThemeView Create()
    {
        var owner = new GeneratedWindowThemeView();
        _windows.Add(owner);
        return owner;
    }

    private static Color Light => Color.FromRgb(0xF5, 0xF5, 0xF7);
    private static Color Dark => Color.FromRgb(0x1C, 0x1C, 0x1E);

    public void Dispose()
    {
        foreach (var owner in _windows) owner.Close();
        ResourceDictionary.CurrentThemeKey = _theme;
    }
}

public partial class GeneratedWindowThemeView : Window
{
    public GeneratedWindowThemeView() => InitializeComponent();
    internal SolidColorBrush StaticBrush => (SolidColorBrush)StaticSample.Background!;
    internal SolidColorBrush DynamicBrush => (SolidColorBrush)DynamicSample.Background!;
}
