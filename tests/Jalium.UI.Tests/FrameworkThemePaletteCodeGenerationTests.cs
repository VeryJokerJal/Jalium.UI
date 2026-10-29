extern alias sg;

using sg::Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Tests;

public sealed class FrameworkThemePaletteCodeGenerationTests
{
    private const string PresentationXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void LiteralPaletteBrushesUseDedicatedDeferredDescriptors()
    {
        var code = Generate(
            """
            <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark">
                    <Color x:Key="DarkColor">#FF102030</Color>
                    <SolidColorBrush x:Key="DarkBrush" Color="#FF102030" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Light">
                    <SolidColorBrush x:Key="LightBrush" Color="#FFF0F0F0" />
                    <LinearGradientBrush x:Key="LightGradient" StartPoint="0,0" EndPoint="1,0">
                        <GradientStop Color="#FFF0F0F0" Offset="0" />
                        <GradientStop Color="#FFFFFFFF" Offset="1" />
                    </LinearGradientBrush>
                    <Color x:Key="LightColor">#FFF0F0F0</Color>
                </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            """,
            optimizeFrameworkTheme: true);

        Assert.Equal(2, Count(code, ".AddDeferredLiteralSolidColorBrush("));
        Assert.DoesNotContain("new global::Jalium.UI.Media.SolidColorBrush()", code);
        Assert.Contains(
            ".AddDeferredLiteralSolidColorBrush(\"DarkBrush\", 0xFF102030u);",
            code);
        Assert.Contains(
            ".AddDeferredLiteralSolidColorBrush(\"LightBrush\", 0xFFF0F0F0u);",
            code);

        // Every theme variant still registers all of its keys. Only the exact literal brushes
        // become the dedicated compact descriptor; this never routes through the general
        // deferred Style entry or branches on the currently active variant.
        Assert.DoesNotContain("AddDeferredResource", code);
        Assert.DoesNotContain("CurrentThemeKey", code);
        Assert.Equal(2, Count(code, "new global::Jalium.UI.Media.Color()"));
        Assert.Equal(1, Count(code, "new global::Jalium.UI.Media.LinearGradientBrush()"));
    }

    [Fact]
    public void DescriptorRegistrationKeepsOriginalDictionaryPosition()
    {
        var code = Generate(
            """
            <SolidColorBrush x:Key="Before" Color="#123" />
            <Color x:Key="Middle">#FF445566</Color>
            <SolidColorBrush x:Key="After" Color="#8123" />
            """,
            optimizeFrameworkTheme: true);

        var before = code.IndexOf(
            ".AddDeferredLiteralSolidColorBrush(\"Before\", 0xFF112233u);",
            StringComparison.Ordinal);
        var middle = code.LastIndexOf(
            "new global::Jalium.UI.Media.Color()",
            StringComparison.Ordinal);
        var after = code.IndexOf(
            ".AddDeferredLiteralSolidColorBrush(\"After\", 0x88112233u);",
            StringComparison.Ordinal);

        Assert.True(before >= 0 && middle > before && after > middle);
        Assert.DoesNotContain("AddDeferredResource", code);
    }

    [Fact]
    public void PaletteCompactionIsLimitedToFrameworkThemeGeneration()
    {
        const string body =
            """
            <SolidColorBrush x:Key="One" Color="#FF102030" />
            <SolidColorBrush x:Key="Two" Color="#FFF0F0F0" />
            """;

        var ordinary = Generate(body, optimizeFrameworkTheme: false);
        var frameworkTheme = Generate(body, optimizeFrameworkTheme: true);

        Assert.DoesNotContain("AddDeferredLiteralSolidColorBrush", ordinary);
        Assert.Equal(2, Count(ordinary, "new global::Jalium.UI.Media.SolidColorBrush()"));

        Assert.Equal(2, Count(frameworkTheme, ".AddDeferredLiteralSolidColorBrush("));
        Assert.DoesNotContain("new global::Jalium.UI.Media.SolidColorBrush()", frameworkTheme);
    }

    [Fact]
    public void ResourceDependentOrCustomizedBrushesStayOnGeneralEagerPath()
    {
        var code = Generate(
            """
            <Color x:Key="AccentColor">#FF102030</Color>
            <SolidColorBrush x:Key="ResourceBrush"
                             Color="{StaticResource AccentColor}" />
            <SolidColorBrush x:Key="CustomizedBrush"
                             Color="#FF102030"
                             Opacity="0.5" />
            <SolidColorBrush x:Key="NamedBrush" Color="Transparent" />
            """,
            optimizeFrameworkTheme: true);

        Assert.DoesNotContain("AddDeferredLiteralSolidColorBrush", code);
        Assert.DoesNotContain("AddDeferredResource", code);
        Assert.Equal(3, Count(code, "new global::Jalium.UI.Media.SolidColorBrush()"));
        Assert.Contains("SetStaticResource(", code);
    }

    [Fact]
    public void NameSharedAndAdditionalPropertiesStayOnGeneralEagerPath()
    {
        var code = Generate(
            """
            <SolidColorBrush x:Key="NamedBrush"
                             x:Name="NamedBrushObject"
                             Color="#FF102030" />
            <SolidColorBrush x:Key="UnsharedBrush"
                             x:Shared="False"
                             Color="#FF102030" />
            <SolidColorBrush x:Key="OpacityBrush"
                             Color="#FF102030"
                             Opacity="0.5" />
            """,
            optimizeFrameworkTheme: true);

        Assert.DoesNotContain("AddDeferredLiteralSolidColorBrush", code);
        Assert.Equal(3, Count(code, "new global::Jalium.UI.Media.SolidColorBrush()"));
    }

    private static string Generate(string body, bool optimizeFrameworkTheme)
    {
        var result = JalxamlParser.Parse(
            $$"""
            <ResourceDictionary xmlns="{{PresentationXmlns}}"
                                xmlns:x="{{XamlXmlns}}">
            {{body}}
            </ResourceDictionary>
            """,
            "FrameworkTheme.jalxaml")!;
        ResolveColorNodes(result.Root!);

        var code = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(
            result,
            symbols: null,
            xmlnsResolver: null,
            deferFrameworkThemeStyles: optimizeFrameworkTheme);
        return Assert.IsType<string>(code);
    }

    private static void ResolveColorNodes(JalxamlAstNode node)
    {
        // Production generation runs JalxamlSourceGenerator.AugmentResolvedTypeNames with
        // the Roslyn-backed resolver before this codegen entry point. These focused tests call
        // the parser directly, whose compact built-in table intentionally omits the Color
        // value type, so reproduce just that one resolution step here.
        if (string.Equals(node.LocalName, "Color", StringComparison.Ordinal)
            && string.IsNullOrEmpty(node.ResolvedClrTypeName))
        {
            node.ResolvedClrTypeName = "Jalium.UI.Media.Color";
            node.FallbackClrTypeName = "Jalium.UI.Media.Color";
        }

        foreach (var child in node.Children)
        {
            ResolveColorNodes(child);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var child in propertyElement.Children)
            {
                ResolveColorNodes(child);
            }
        }
    }

    private static int Count(string value, string needle)
        => value.Split(new[] { needle }, StringSplitOptions.None).Length - 1;
}
