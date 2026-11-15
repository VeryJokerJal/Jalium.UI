extern alias sg;

using sg::Jalium.UI.Xaml.SourceGenerator;

namespace Jalium.UI.Tests;

public sealed class DeferredThemeStyleCodeGenerationTests
{
    private const string PresentationXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XamlXmlns =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SafeStylesAreRegisteredBeforeEagerResources()
    {
        var code = Generate(
            """
            <Style x:Key="DerivedStyle" TargetType="Button" BasedOn="{StaticResource BaseStyle}">
                <Setter Property="Padding" Value="8" />
            </Style>
            <SolidColorBrush x:Key="AccentBrush" Color="#FF0078D4" Opacity="0.5" />
            <Style x:Key="BaseStyle" TargetType="Button">
                <Setter Property="MinHeight" Value="32" />
            </Style>
            """);

        Assert.Equal(2, Count(code, "AddDeferredResource"));
        Assert.Contains("static object? __CreateDeferredStyle0", code);
        Assert.Contains("static object? __CreateDeferredStyle1", code);
        Assert.Contains(
            "SetStaticResource(__style, \"BasedOn\", \"BaseStyle\", __deferredCtx0)",
            code);

        // An additional property keeps this resource on the eager path. A plain
        // literal brush is now a compact deferred descriptor, so it cannot serve
        // as the eager construction boundary this test is meant to verify.
        Assert.DoesNotContain("AddDeferredLiteralSolidColorBrush", code);
        var lastRegistration = code.LastIndexOf("AddDeferredResource", StringComparison.Ordinal);
        var eagerBrush = code.LastIndexOf(
            "new global::Jalium.UI.Media.SolidColorBrush()",
            StringComparison.Ordinal);
        Assert.True(lastRegistration >= 0 && eagerBrush > lastRegistration);
    }

    [Fact]
    public void SafeStylesAreRegisteredBeforeLiteralBrushDescriptors()
    {
        var code = Generate(
            """
            <Style x:Key="DerivedStyle" TargetType="Button" BasedOn="{StaticResource BaseStyle}" />
            <SolidColorBrush x:Key="AccentBrush" Color="#FF0078D4" />
            <Style x:Key="BaseStyle" TargetType="Button" />
            """);

        Assert.Equal(2, Count(code, "AddDeferredResource"));
        Assert.DoesNotContain("new global::Jalium.UI.Media.SolidColorBrush()", code);
        var lastStyleRegistration = code.LastIndexOf("AddDeferredResource", StringComparison.Ordinal);
        var brushRegistration = code.IndexOf(
            ".AddDeferredLiteralSolidColorBrush(\"AccentBrush\", 0xFF0078D4u);",
            StringComparison.Ordinal);
        Assert.True(lastStyleRegistration >= 0 && brushRegistration > lastStyleRegistration);
    }

    [Fact]
    public void DeferredTemplateFactoryKeepsDictionaryOwnerAndFreshContext()
    {
        var code = Generate(
            """
            <Style x:Key="TemplatedStyle" TargetType="Button">
                <Setter Property="Template">
                    <ControlTemplate TargetType="Button">
                        <Border Name="TemplateRoot" />
                    </ControlTemplate>
                </Setter>
            </Style>
            """);

        Assert.Contains("AddDeferredResource", code);
        Assert.Contains("SetVisualTree(() =>", code);
        Assert.Contains(
            "XamlBuilder.PushParent(__owner, __deferredCtx0)",
            code);
        Assert.DoesNotContain(
            "XamlBuilder.PushParent(__target, __deferredCtx0)",
            code);
    }

    [Fact]
    public void DictionaryDeferralIsOptIn()
    {
        var result = Parse(
            """
            <Style x:Key="ControlStyle" TargetType="Button" />
            """);

        var ordinary = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(
            result,
            symbols: null,
            xmlnsResolver: null,
            deferFrameworkThemeStyles: false);
        var frameworkTheme = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(
            result,
            symbols: null,
            xmlnsResolver: null,
            deferFrameworkThemeStyles: true);

        Assert.NotNull(ordinary);
        Assert.NotNull(frameworkTheme);
        Assert.DoesNotContain("AddDeferredResource", ordinary!);
        Assert.Contains("AddDeferredResource", frameworkTheme!);
    }

    [Fact]
    public void SharedStyleStaysEager()
    {
        AssertEager(
            """
            <Style x:Key="SharedStyle" x:Shared="False" TargetType="Button" />
            """);
    }

    [Fact]
    public void DirectNameScopeStyleStaysEager()
    {
        AssertEager(
            """
            <Style x:Key="NamedStyle" x:Name="NamedStyleObject" TargetType="Button" />
            """);
    }

    [Fact]
    public void ComplexKeyStyleStaysEager()
    {
        AssertEager(
            """
            <Style x:Key="{x:Type Button}" TargetType="Button" />
            """);
    }

    [Fact]
    public void ConflictingPlainKeysStayEager()
    {
        AssertEager(
            """
            <Style x:Key="Duplicate" TargetType="Button" />
            <Style x:Key="Duplicate" TargetType="Button" />
            """);
    }

    private static void AssertEager(string body)
    {
        var code = Generate(body);
        Assert.DoesNotContain("AddDeferredResource", code);
        Assert.Contains("new global::Jalium.UI.Style()", code);
    }

    private static string Generate(string body)
    {
        var code = JalxamlCodeGenerator.TryEmitDictionaryBuildBody(
            Parse(body),
            symbols: null,
            xmlnsResolver: null,
            deferFrameworkThemeStyles: true);
        return Assert.IsType<string>(code);
    }

    private static JalxamlParseResult Parse(string body)
        => JalxamlParser.Parse(
            $$"""
            <ResourceDictionary xmlns="{{PresentationXmlns}}"
                                xmlns:x="{{XamlXmlns}}">
            {{body}}
            </ResourceDictionary>
            """,
            "FrameworkTheme.jalxaml")!;

    private static int Count(string value, string needle)
        => value.Split(new[] { needle }, StringSplitOptions.None).Length - 1;
}
