using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class DataTemplateIdentityWpfParityTests
{
    [Fact]
    public void DataTypeAcceptsClrTypesAndXmlTagNamesAndRejectsInvalidValues()
    {
        var clrTemplate = new DataTemplate((object)typeof(string));
        var xmlTemplate = new DataTemplate("book");
        var mutableTemplate = new DataTemplate();

        Assert.Equal(typeof(string), clrTemplate.DataType);
        Assert.Equal("book", xmlTemplate.DataType);

        Assert.Throws<ArgumentNullException>(() => new DataTemplate((object)null!));
        Assert.Throws<ArgumentException>(() => new DataTemplate(new object()));
        Assert.Throws<ArgumentException>(() => new DataTemplate(typeof(object)));
        Assert.Throws<ArgumentNullException>(() => mutableTemplate.DataType = null);
        Assert.Throws<ArgumentException>(() => mutableTemplate.DataType = new object());
        Assert.Throws<ArgumentException>(() => mutableTemplate.DataType = typeof(object));
    }

    [Fact]
    public void DataTemplateKeyTracksDataTypeAndTypeOverloadRemainsAvailable()
    {
        var template = new DataTemplate();

        Assert.Null(template.DataTemplateKey);

        template.DataType = typeof(string);
        object? firstKey = template.DataTemplateKey;
        object? secondKey = template.DataTemplateKey;

        Assert.Equal(new DataTemplateKey(typeof(string)), firstKey);
        Assert.Equal(firstKey, secondKey);
        Assert.NotSame(firstKey, secondKey);
        Assert.NotNull(typeof(DataTemplate).GetConstructor(new[] { typeof(object) }));
        Assert.NotNull(typeof(DataTemplate).GetConstructor(new[] { typeof(Type) }));

        template.Seal();
        Assert.Throws<InvalidOperationException>(() => template.DataType = typeof(int));
        Assert.Equal(typeof(string), template.DataType);
    }

    [Fact]
    public void HierarchicalAndItemContainerTemplatesUseTheCorrectImplicitKeyFamily()
    {
        var hierarchical = new HierarchicalDataTemplate((object)"node");
        var itemContainer = new Jalium.UI.Controls.ItemContainerTemplate
        {
            DataType = typeof(string),
        };

        Assert.Equal(new DataTemplateKey("node"), hierarchical.DataTemplateKey);
        Assert.False(typeof(HierarchicalDataTemplate).IsSealed);
        Assert.NotNull(typeof(HierarchicalDataTemplate).GetConstructor(new[] { typeof(object) }));
        Assert.NotNull(typeof(HierarchicalDataTemplate).GetConstructor(new[] { typeof(Type) }));

        Assert.Equal(
            new ItemContainerTemplateKey(typeof(string)),
            itemContainer.ItemContainerTemplateKey);
        Assert.NotEqual(
            new DataTemplateKey(typeof(string)),
            itemContainer.ItemContainerTemplateKey);
        Assert.False(typeof(Jalium.UI.Controls.ItemContainerTemplate).IsSealed);
    }

    [Fact]
    public void TemplateTypesDeclareTheirImplicitKeyProperties()
    {
        var dataAttribute = typeof(DataTemplate)
            .GetCustomAttribute<DictionaryKeyPropertyAttribute>(inherit: true);
        var itemAttribute = typeof(Jalium.UI.Controls.ItemContainerTemplate)
            .GetCustomAttribute<DictionaryKeyPropertyAttribute>(inherit: true);

        Assert.Equal(nameof(DataTemplate.DataTemplateKey), dataAttribute?.Name);
        Assert.Equal(
            nameof(Jalium.UI.Controls.ItemContainerTemplate.ItemContainerTemplateKey),
            itemAttribute?.Name);
    }

    [Fact]
    public void RuntimeXamlUsesDataTemplateKeyWhenXKeyIsOmitted()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:controls="clr-namespace:Jalium.UI.Controls;assembly=Jalium.UI.Controls">
                <DataTemplate DataType="{x:Type controls:Button}">
                    <Border />
                </DataTemplate>
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));
        var key = new DataTemplateKey(typeof(Button));

        var template = Assert.IsType<DataTemplate>(dictionary[key]);
        Assert.Equal(typeof(Button), template.DataType);
        Assert.IsType<Border>(template.LoadContent());
    }

    [Fact]
    public void ExplicitXKeyOverridesTheDataTemplateImplicitKey()
    {
        const string xaml = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <DataTemplate x:Key="ExplicitTemplate" DataType="{x:Type TextBlock}">
                    <Border />
                </DataTemplate>
            </ResourceDictionary>
            """;

        var dictionary = Assert.IsType<ResourceDictionary>(XamlReader.Parse(xaml));

        Assert.IsType<DataTemplate>(dictionary["ExplicitTemplate"]);
        Assert.False(dictionary.Contains(new DataTemplateKey(typeof(TextBlock))));
    }

    [Fact]
    public void BareClrTypeAndXmlTagStringRemainDistinctDataTypeForms()
    {
        const string clrXaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          DataType="TextBlock">
                <Border />
            </DataTemplate>
            """;
        const string xmlXaml = """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          DataType="book">
                <Border />
            </DataTemplate>
            """;

        var clrTemplate = Assert.IsType<DataTemplate>(XamlReader.Parse(clrXaml));
        var xmlTemplate = Assert.IsType<DataTemplate>(XamlReader.Parse(xmlXaml));

        Assert.Equal(typeof(TextBlock), clrTemplate.DataType);
        Assert.Equal("book", xmlTemplate.DataType);
    }

    [Fact]
    public void XamlBuilderUsesImplicitKeyAndHonorsExplicitKeyPrecedence()
    {
        XamlBuilderInitializer.Register();

        var implicitDictionary = new ResourceDictionary();
        var implicitContext = XamlBuilder.BeginComponent(
            implicitDictionary,
            sourceAssembly: typeof(DataTemplateIdentityWpfParityTests).Assembly);
        var implicitTemplate = new DataTemplate(typeof(string));

        XamlBuilder.AddChild(implicitDictionary, implicitTemplate, implicitContext);

        Assert.Same(
            implicitTemplate,
            implicitDictionary[new DataTemplateKey(typeof(string))]);

        var itemDictionary = new ResourceDictionary();
        var itemContext = XamlBuilder.BeginComponent(
            itemDictionary,
            sourceAssembly: typeof(DataTemplateIdentityWpfParityTests).Assembly);
        var itemTemplate = new Jalium.UI.Controls.ItemContainerTemplate
        {
            DataType = typeof(string),
        };

        XamlBuilder.AddChild(itemDictionary, itemTemplate, itemContext);

        Assert.Same(
            itemTemplate,
            itemDictionary[new ItemContainerTemplateKey(typeof(string))]);
        Assert.False(itemDictionary.Contains(new DataTemplateKey(typeof(string))));

        var explicitDictionary = new ResourceDictionary();
        var explicitContext = XamlBuilder.BeginComponent(
            explicitDictionary,
            sourceAssembly: typeof(DataTemplateIdentityWpfParityTests).Assembly);
        var explicitTemplate = new DataTemplate(typeof(string));

        XamlBuilder.AddChild(
            explicitDictionary,
            explicitTemplate,
            explicitContext,
            "ExplicitTemplate");

        Assert.Same(explicitTemplate, explicitDictionary["ExplicitTemplate"]);
        Assert.False(explicitDictionary.Contains(new DataTemplateKey(typeof(string))));
    }

    [Fact]
    public void ContentPresenterAppliesImplicitTemplateToStringContent()
    {
        var template = new DataTemplate(typeof(string));
        template.SetVisualTree(() => new Border());

        var presenter = new ContentPresenter();
        presenter.Resources[new DataTemplateKey(typeof(string))] = template;
        presenter.Content = "templated text";

        var contentElement = GetContentElement(presenter);
        Assert.IsType<Border>(contentElement);
        Assert.Equal("templated text", contentElement!.DataContext);
    }

    private static FrameworkElement? GetContentElement(ContentPresenter presenter)
    {
        var field = typeof(ContentPresenter).GetField(
            "_contentElement",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (FrameworkElement?)field!.GetValue(presenter);
    }
}
