using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using System.Reflection;

namespace Jalium.UI.Tests;

public class ItemsControlDataTemplateTests
{
    [Fact]
    public void ItemsControl_ItemTemplate_ShouldMaterializeTemplatedContainers()
    {
        const string xaml = """
            <ItemsControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border>
                            <TextBlock Text="Templated item" />
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;

        var itemsControl = Assert.IsType<ItemsControl>(XamlReader.Parse(xaml));
        itemsControl.ItemsSource = new object[] { "one", "two" };

        itemsControl.Measure(new Size(320, 200));
        itemsControl.Arrange(new Rect(0, 0, 320, 200));

        var itemsHost = Assert.IsAssignableFrom<Panel>(itemsControl.GetVisualChild(0));
        Assert.Equal(2, itemsHost.Children.Count);

        var presenter = Assert.IsType<ContentPresenter>(itemsHost.Children[0]);
        var border = Assert.IsType<Border>(presenter.GetVisualChild(0));
        var text = Assert.IsType<TextBlock>(border.Child);

        Assert.Equal("Templated item", text.Text);
    }

    [Fact]
    public void DataTemplate_PropertyElementBeforeVisualRoot_ShouldParseSuccessfully()
    {
        RegisterCustomXamlType<TestDataTemplate>();

        const string xaml = """
            <local:TestDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:local="clr-namespace:Jalium.UI.Tests;assembly=Jalium.UI.Tests">
                <local:TestDataTemplate.ExtraTemplate>
                    <DataTemplate>
                        <TextBlock Text="Nested template" />
                    </DataTemplate>
                </local:TestDataTemplate.ExtraTemplate>
                <TextBlock Text="Outer template" />
            </local:TestDataTemplate>
            """;

        var template = Assert.IsType<TestDataTemplate>(XamlReader.Parse(xaml));

        Assert.NotNull(template.ExtraTemplate);
        Assert.Equal("Outer template", Assert.IsType<TextBlock>(template.LoadContent()).Text);
        Assert.Equal("Nested template", Assert.IsType<TextBlock>(template.ExtraTemplate!.LoadContent()).Text);
    }

    [Fact]
    public void DataTemplate_PropertyElementAfterVisualRoot_ShouldParseSuccessfully()
    {
        RegisterCustomXamlType<TestDataTemplate>();

        const string xaml = """
            <local:TestDataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:local="clr-namespace:Jalium.UI.Tests;assembly=Jalium.UI.Tests">
                <TextBlock Text="Outer template" />
                <local:TestDataTemplate.ExtraTemplate>
                    <DataTemplate>
                        <TextBlock Text="Nested template" />
                    </DataTemplate>
                </local:TestDataTemplate.ExtraTemplate>
            </local:TestDataTemplate>
            """;

        var template = Assert.IsType<TestDataTemplate>(XamlReader.Parse(xaml));

        Assert.NotNull(template.ExtraTemplate);
        Assert.Equal("Outer template", Assert.IsType<TextBlock>(template.LoadContent()).Text);
        Assert.Equal("Nested template", Assert.IsType<TextBlock>(template.ExtraTemplate!.LoadContent()).Text);
    }

    private static void RegisterCustomXamlType<T>()
    {
        var registryType = typeof(XamlReader).Assembly.GetType("Jalium.UI.Markup.XamlTypeRegistry");
        var typesField = registryType?.GetField("_types", BindingFlags.Static | BindingFlags.NonPublic);
        var types = typesField?.GetValue(null) as IDictionary<string, Type>;

        Assert.NotNull(types);
        types![typeof(T).Name] = typeof(T);
    }
}

public sealed class TestDataTemplate : DataTemplate
{
    public DataTemplate? ExtraTemplate { get; set; }
}
