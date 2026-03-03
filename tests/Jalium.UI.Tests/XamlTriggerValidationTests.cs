using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public class XamlTriggerValidationTests
{
    [Fact]
    public void PropertyTrigger_UnresolvedProperty_ShouldThrowAtLoadTime()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Style.Triggers>
                    <PropertyTrigger Property="NotExist" Value="True">
                        <Setter Property="Opacity" Value="0.5" />
                    </PropertyTrigger>
                </Style.Triggers>
            </Style>
            """;

        var ex = Assert.Throws<XamlParseException>(() => XamlReader.Parse(xaml));
        Assert.Contains("NotExist", ex.Message);
        Assert.Contains("StyleTargetType='Jalium.UI.Controls.Button'", ex.Message);
        Assert.Contains("Line=", ex.Message);
    }

    [Fact]
    public void MultiTrigger_UnresolvedConditionProperty_ShouldThrowAtLoadTime()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Style.Triggers>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property="IsMouseOver" Value="True" />
                            <Condition Property="NotExist" Value="True" />
                        </MultiTrigger.Conditions>
                        <Setter Property="Opacity" Value="0.7" />
                    </MultiTrigger>
                </Style.Triggers>
            </Style>
            """;

        var ex = Assert.Throws<XamlParseException>(() => XamlReader.Parse(xaml));
        Assert.Contains("NotExist", ex.Message);
        Assert.Contains("StyleTargetType='Jalium.UI.Controls.Button'", ex.Message);
        Assert.Contains("Line=", ex.Message);
    }

    [Fact]
    public void Setter_WithTargetName_UnresolvedProperty_ShouldRemainDeferred()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="NavigationViewItem">
                <Style.Setters>
                    <Setter TargetName="PART_Chevron" Property="Data" Value="M0,0 L1,1" />
                </Style.Setters>
            </Style>
            """;

        var style = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));

        Assert.Null(setter.Property);
        Assert.Equal("Data", setter.PropertyName);
        Assert.Equal("PART_Chevron", setter.TargetName);
    }
}
