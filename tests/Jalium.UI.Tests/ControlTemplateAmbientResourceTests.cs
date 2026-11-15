using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ControlTemplateAmbientResourceTests
{
    [Fact]
    public void DeferredControlTemplatesKeepTheirOwnDictionaryResources()
    {
        var first = Load("#FF112233"); var second = Load("#FF998877");
        var firstRoot = Assert.IsType<Border>(((ControlTemplate)first["Preview"]!).LoadContent());
        var secondRoot = Assert.IsType<Border>(((ControlTemplate)second["Preview"]!).LoadContent());
        Assert.Same(first["Accent"], firstRoot.Background);
        Assert.Same(second["Accent"], secondRoot.Background);
        Assert.NotEqual(Assert.IsType<SolidColorBrush>(firstRoot.Background).Color, Assert.IsType<SolidColorBrush>(secondRoot.Background).Color);
    }

    private static ResourceDictionary Load(string color) => (ResourceDictionary)XamlReader.Parse($$"""
        <ResourceDictionary xmlns="http://schemas.jalium.ui/2024" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <SolidColorBrush x:Key="Accent" Color="{{color}}" />
            <ControlTemplate x:Key="Preview" TargetType="Button"><Border Background="{StaticResource Accent}" /></ControlTemplate>
        </ResourceDictionary>
        """)!;

    [Fact]
    public void TemplateLocalResourcesOverrideTheDeclaringDictionary()
    {
        var dictionary = (ResourceDictionary)XamlReader.Parse("""
            <ResourceDictionary xmlns="http://schemas.jalium.ui/2024" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <SolidColorBrush x:Key="Accent" Color="#FF112233" />
                <ControlTemplate x:Key="Preview" TargetType="Button">
                    <ControlTemplate.Resources><SolidColorBrush x:Key="Accent" Color="#FF998877" /></ControlTemplate.Resources>
                    <Border Background="{StaticResource Accent}" />
                </ControlTemplate>
            </ResourceDictionary>
            """)!;
        var template = (ControlTemplate)dictionary["Preview"]!;
        var root = Assert.IsType<Border>(template.LoadContent());
        Assert.Same(template.Resources["Accent"], root.Background);
        Assert.NotSame(dictionary["Accent"], root.Background);
    }
}
