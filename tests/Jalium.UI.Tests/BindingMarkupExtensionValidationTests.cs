using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class BindingMarkupExtensionValidationTests
{
    [Fact]
    public void BindingMarkupExtension_WithValidationFlags_ShouldPopulateBindingProperties()
    {
        const string xaml = """
            <TextBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Text="{Binding Path=ProjectName, Mode=TwoWay, ValidatesOnDataErrors=True, ValidatesOnNotifyDataErrors=False, NotifyOnValidationError=True}" />
            """;

        var textBox = Assert.IsType<TextBox>(XamlReader.Parse(xaml));
        var bindingExpression = Assert.IsType<BindingExpression>(
            textBox.GetBindingExpression(TextBox.TextProperty));

        Assert.True(bindingExpression.ParentBinding.ValidatesOnDataErrors);
        Assert.False(bindingExpression.ParentBinding.ValidatesOnNotifyDataErrors);
        Assert.True(bindingExpression.ParentBinding.NotifyOnValidationError);
    }

    [Fact]
    public void BindingMarkupExtension_WithoutValidationFlags_ShouldKeepBindingDefaults()
    {
        const string xaml = """
            <TextBox xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     Text="{Binding Path=ProjectName}" />
            """;

        var textBox = Assert.IsType<TextBox>(XamlReader.Parse(xaml));
        var bindingExpression = Assert.IsType<BindingExpression>(
            textBox.GetBindingExpression(TextBox.TextProperty));

        Assert.False(bindingExpression.ParentBinding.ValidatesOnDataErrors);
        Assert.True(bindingExpression.ParentBinding.ValidatesOnNotifyDataErrors);
        Assert.False(bindingExpression.ParentBinding.NotifyOnValidationError);
    }
}
