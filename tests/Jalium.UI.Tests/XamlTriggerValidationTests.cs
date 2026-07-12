using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public class XamlTriggerValidationTests
{
    [Fact]
    public void PropertyTrigger_UnresolvedProperty_ShouldParseThenThrowWhenSealedOrApplied()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Style.Triggers>
                    <Trigger Property="NotExist" Value="True">
                        <Setter Property="Opacity" Value="0.5" />
                    </Trigger>
                </Style.Triggers>
            </Style>
            """;

        var style = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var trigger = Assert.IsType<Trigger>(Assert.Single(style.Triggers));
        Assert.Null(trigger.Property);

        var sealException = Assert.Throws<InvalidOperationException>(style.Seal);
        Assert.Contains("Property", sealException.Message);

        var secondStyle = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var applyException = Assert.Throws<InvalidOperationException>(() => new Button { Style = secondStyle });
        Assert.Contains("Property", applyException.Message);
    }

    [Fact]
    public void MultiTrigger_UnresolvedConditionProperty_ShouldParseThenThrowWhenSealed()
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

        var style = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var trigger = Assert.IsType<MultiTrigger>(Assert.Single(style.Triggers));
        Assert.Null(trigger.Conditions[1].Property);

        var ex = Assert.Throws<InvalidOperationException>(style.Seal);
        Assert.Contains("Property", ex.Message);
    }

    [Fact]
    public void PropertyTrigger_OwnerQualifiedInheritedProperty_ShouldResolveAndApply()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
                <Style.Triggers>
                    <Trigger Property="Control.IsMouseOver" Value="True">
                        <Setter Property="Opacity" Value="0.5" />
                    </Trigger>
                </Style.Triggers>
            </Style>
            """;

        var style = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var trigger = Assert.IsType<Trigger>(Assert.Single(style.Triggers));
        Assert.Same(UIElement.IsMouseOverProperty, trigger.Property);

        var button = new Button { Style = style };
        button.SetIsMouseOver(true);
        Assert.Equal(0.5, button.Opacity);
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
