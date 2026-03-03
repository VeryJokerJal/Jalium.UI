using System.Text;
using Jalium.UI;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class XamlResourceDictionarySourceCompatibilityTests
{
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

    private static ResourceDictionary ParseWithAssemblyContext(string xaml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        return Assert.IsType<ResourceDictionary>(
            XamlReader.Load(stream, "Jalium.UI.Tests.TestAssets.HostDictionary.jalxaml", typeof(XamlResourceDictionarySourceCompatibilityTests).Assembly));
    }
}
