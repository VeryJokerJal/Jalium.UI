using System.ComponentModel;
using System.Text;
using System.Xml;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class XamlSerializationParityTests
{
    [Fact]
    public void ServiceProvidersRetainsOneServicePerType()
    {
        var providers = new ServiceProviders();
        var service = new object();

        providers.AddService(typeof(object), service);
        providers.AddService(typeof(object), service);

        Assert.Same(service, providers.GetService(typeof(object)));
        Assert.Throws<ArgumentException>(() => providers.AddService(typeof(object), new object()));
    }

    [Fact]
    public void DesignerManagerValidatesModeAndExposesWriterStateToSerializers()
    {
        var valueOnlyManager = new XamlDesignerSerializationManager(null!);
        Assert.Equal(XamlWriterMode.Value, valueOnlyManager.XamlWriterMode);
        Assert.Throws<InvalidEnumArgumentException>(() =>
        {
            valueOnlyManager.XamlWriterMode = (XamlWriterMode)99;
        });

        var textBlock = new TextBlock();
        textBlock.Inlines.Add(new Run("content"));
        textBlock.Inlines.Clear();
        var textBox = new TextBox();

        Assert.True(textBlock.ShouldSerializeInlines(valueOnlyManager));
        Assert.True(textBox.ShouldSerializeText(valueOnlyManager));
        Assert.False(textBlock.ShouldSerializeInlines(null!));

        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output);
        var writerManager = new XamlDesignerSerializationManager(writer);

        Assert.False(textBlock.ShouldSerializeInlines(writerManager));
        Assert.False(textBox.ShouldSerializeText(writerManager));
    }
}
