using System.Text;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Tests.Resources;

namespace Jalium.UI.Tests;

public class XamlResourceDictionarySourceCompatibilityTests
{
    [Fact]
    public void ResourceDictionary_ShouldParsePrimitiveColorAndStringResources()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Color x:Key="AccentColor">#FF112233</Color>
                <x:String x:Key="AnimationKeySpline">0,0,0,1</x:String>
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));

        Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), Assert.IsType<Color>(dictionary["AccentColor"]));
        Assert.Equal("0,0,0,1", Assert.IsType<string>(dictionary["AnimationKeySpline"]));
    }

    [Fact]
    public void ResourceDictionary_ShouldResolvePrimitiveThemeDictionaryResources()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.ThemeDictionaries>
                    <ResourceDictionary x:Key="Dark">
                        <Color x:Key="AccentColor">#FF112233</Color>
                    </ResourceDictionary>
                    <ResourceDictionary x:Key="Light">
                        <Color x:Key="AccentColor">#FF445566</Color>
                    </ResourceDictionary>
                </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """;

        var previousThemeKey = ResourceDictionary.CurrentThemeKey;

        try
        {
            var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));

            ResourceDictionary.CurrentThemeKey = "Dark";
            Assert.Equal(Color.FromArgb(0xFF, 0x11, 0x22, 0x33), Assert.IsType<Color>(dictionary["AccentColor"]));

            ResourceDictionary.CurrentThemeKey = "Light";
            Assert.Equal(Color.FromArgb(0xFF, 0x44, 0x55, 0x66), Assert.IsType<Color>(dictionary["AccentColor"]));
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = previousThemeKey;
        }
    }

    [Fact]
    public void Source_WithRootPathAndPackPath_ShouldLoadMergedDictionariesWithXamlFallback()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="/TestAssets/Colors.xaml" />
                    <ResourceDictionary Source="/Jalium.UI.Tests;component/TestAssets/Typography.xaml" />
                </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """;

        var dictionary = ParseWithAssemblyContext(xaml);

        var accent = Assert.IsType<SolidColorBrush>(dictionary["TestAccentBrush"]);
        var typography = Assert.IsType<SolidColorBrush>(dictionary["TestTypographyBrush"]);

        Assert.Equal(Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5), accent.Color);
        Assert.Equal(Color.FromArgb(0xFF, 0x6D, 0x4C, 0x41), typography.Color);
    }

    [Fact]
    public void Source_WhenOneMergedDictionaryIsMissing_ShouldContinueLoadingOthers()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="/TestAssets/NotFound.xaml" />
                    <ResourceDictionary Source="/TestAssets/Colors.xaml" />
                </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """;

        var dictionary = ParseWithAssemblyContext(xaml);
        var accent = Assert.IsType<SolidColorBrush>(dictionary["TestAccentBrush"]);
        Assert.Equal(Color.FromArgb(0xFF, 0x1E, 0x88, 0xE5), accent.Color);
    }

    [Fact]
    public void Source_WithXClassResourceDictionary_ShouldPreserveDerivedDictionaryInstance()
    {
        ThemeLoader.Initialize();

        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="/Jalium.UI.Tests;component/TestAssets/CodeBehindDictionary.jalxaml" />
                </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
            """;

        var dictionary = ParseWithAssemblyContext(xaml);
        var merged = Assert.IsType<TestCodeBehindDictionary>(Assert.Single(dictionary.MergedDictionaries));

        Assert.Equal("DictionaryCodeBehind", merged.Marker);
        Assert.Equal(Color.FromArgb(0xFF, 0x5A, 0x7B, 0xEF), merged.AccentBrush.Color);
        Assert.Equal(Color.FromArgb(0xFF, 0x5A, 0x7B, 0xEF), Assert.IsType<SolidColorBrush>(dictionary["CodeBehindAccentBrush"]).Color);
    }

    [Theory]
    [InlineData("Jalium.UI.Controls.Themes.Controls.Containers.jalxaml")]
    [InlineData("Jalium.UI.Controls.Themes.Controls.Primitives.jalxaml")]
    public void ThemeControlDictionaries_ShouldParseStandalone(string resourceName)
    {
        var assembly = typeof(Button).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        Assert.NotNull(stream);

        var dictionary = XamlReader.Load(stream!, resourceName, assembly);
        Assert.IsType<ResourceDictionary>(dictionary);
    }

    private static ResourceDictionary ParseWithAssemblyContext(string xaml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return Assert.IsType<ResourceDictionary>(
            XamlReader.Load(stream, "Jalium.UI.Tests.TestAssets.HostDictionary.jalxaml", typeof(XamlResourceDictionarySourceCompatibilityTests).Assembly));
    }
}
