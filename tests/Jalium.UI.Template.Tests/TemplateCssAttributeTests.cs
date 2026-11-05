using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TemplateCssAttributeTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void NamespacedAttributesObserveTemplateDeclarationsAndBindingUpdates(bool compiled)
    {
        ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var template = compiled ? Assert.IsType<ControlTemplate>(new GeneratedCssAttributeThemes()["AttributeTemplate"]) : Assert.IsType<ControlTemplate>(XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.jalium.ui/2024"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             xmlns:qualified="clr-namespace:Jalium.UI.Controls;assembly=Jalium.UI.Controls"
                             TargetType="Control">
                <StackPanel Css.StyleSheet="@namespace controls 'clr-namespace:Jalium.UI.Controls;assembly=Jalium.UI.Controls'; [controls|Tag=highlight] {outline:2px solid #fbbf24}">
                    <TextBox x:Name="Source" Text="highlight" />
                    <qualified:Border x:Name="Qualified" Height="40" qualified:Tag="{Binding Text, ElementName=Source}" />
                </StackPanel>
            </ControlTemplate>
            """));
        var control = new Control { Template = template };
        Css.SetIncludeTemplateParts(control, true); Layout(control);
        var source = Assert.IsType<TextBox>(control.FindName("Source")); var qualified = Assert.IsType<Border>(control.FindName("Qualified"));
        Assert.Equal("highlight", qualified.Tag); Assert.False(qualified.HasLocalValue(FrameworkElement.TagProperty));
        Assert.Equal(BaseValueSource.ParentTemplate, DependencyPropertyHelper.GetValueSource(qualified, FrameworkElement.TagProperty).BaseValueSource);
        Assert.Equal(2, qualified.OutlineThickness);
        source.Text = "normal"; Layout(control); Assert.Equal("normal", qualified.Tag); Assert.Equal(0, qualified.OutlineThickness);
        source.Text = "highlight"; Layout(control); Assert.Equal(2, qualified.OutlineThickness);
        Assert.NotNull(BindingOperations.GetBindingExpression(qualified, FrameworkElement.TagProperty));
        qualified.ClearValue(FrameworkElement.TagProperty); Layout(control);
        Assert.Null(qualified.Tag); Assert.Equal(0, qualified.OutlineThickness);
        Assert.Null(BindingOperations.GetBindingExpression(qualified, FrameworkElement.TagProperty));
        qualified.Tag = "highlight"; Layout(control); Assert.Equal(2, qualified.OutlineThickness);
    }
    private static void Layout(UIElement element) { for (int pass = 0; pass < 2; pass++) { element.Measure(new Size(320, 200)); element.Arrange(new Rect(0, 0, 320, 200)); element.UpdateLayout(); } }
}

public partial class GeneratedCssAttributeThemes : ResourceDictionary
{
    public GeneratedCssAttributeThemes() => InitializeComponent();
}
