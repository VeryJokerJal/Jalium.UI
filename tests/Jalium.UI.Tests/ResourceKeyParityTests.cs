using System.ComponentModel;
using Jalium.UI.Markup;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ResourceKeyParityTests
{
    [Fact]
    public void ComponentResourceKeyConverterTypeShapeMatchesWpf()
    {
        Assert.Equal(
            typeof(global::Jalium.UI.ExpressionConverter),
            typeof(ComponentResourceKeyConverter).BaseType);
        Assert.False(typeof(ComponentResourceKeyConverter).IsSealed);
        Assert.NotNull(typeof(ComponentResourceKeyConverter).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void ResourceKeyIsAMarkupExtensionThatProvidesItself()
    {
        var key = new ComponentResourceKey(typeof(ResourceKeyParityTests), "Accent");

        Assert.IsAssignableFrom<ResourceKey>(key);
        Assert.IsAssignableFrom<MarkupExtension>(key);
        Assert.Same(key, key.ProvideValue(new EmptyServiceProvider()));
        Assert.Equal(typeof(ResourceKeyParityTests).Assembly, key.Assembly);
        Assert.IsType<ComponentResourceKeyConverter>(TypeDescriptor.GetConverter(key));
    }

    [Fact]
    public void ComponentResourceKeyDefaultConstructionAllowsEachPartToBeSetOnce()
    {
        var key = new ComponentResourceKey();

        Assert.Null(key.TypeInTargetAssembly);
        Assert.Null(key.ResourceId);
        Assert.Null(key.Assembly);

        key.TypeInTargetAssembly = typeof(string);
        key.ResourceId = 42;

        Assert.Equal(typeof(string), key.TypeInTargetAssembly);
        Assert.Equal(42, key.ResourceId);
        Assert.Throws<InvalidOperationException>(() => key.TypeInTargetAssembly = typeof(int));
        Assert.Throws<InvalidOperationException>(() => key.ResourceId = 43);
    }

    [Fact]
    public void ComponentResourceKeyUsesTypeAndArbitraryIdentifierForEquality()
    {
        var first = new ComponentResourceKey(typeof(string), 7);
        var equal = new ComponentResourceKey(typeof(string), 7);
        var otherId = new ComponentResourceKey(typeof(string), "7");

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, otherId);
        Assert.Contains("System.String", first.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => new ComponentResourceKey(null!, 1));
        Assert.Throws<ArgumentNullException>(() => new ComponentResourceKey(typeof(string), null!));
    }

    [Fact]
    public void DataTemplateKeyAcceptsTypeOrXmlTagAndRejectsObject()
    {
        var typeKey = new DataTemplateKey(typeof(string));
        var xmlKey = new DataTemplateKey("book");

        Assert.Equal(typeof(string), typeKey.DataType);
        Assert.Equal(typeof(string).Assembly, typeKey.Assembly);
        Assert.Equal("book", xmlKey.DataType);
        Assert.Null(xmlKey.Assembly);
        Assert.Equal(new DataTemplateKey(typeof(string)), typeKey);
        Assert.Throws<ArgumentNullException>(() => new DataTemplateKey(null!));
        Assert.Throws<ArgumentException>(() => new DataTemplateKey(new object()));
        Assert.Throws<ArgumentException>(() => new DataTemplateKey(typeof(object)));
    }

    [Fact]
    public void ParameterlessTemplateKeyRequiresSupportInitializeTransaction()
    {
        var key = new DataTemplateKey();
        var initializer = (ISupportInitialize)key;

        Assert.Throws<InvalidOperationException>(() => key.DataType = typeof(string));
        Assert.Throws<InvalidOperationException>(() => initializer.EndInit());

        initializer.BeginInit();
        key.DataType = typeof(string);
        initializer.EndInit();

        Assert.Equal(new DataTemplateKey(typeof(string)), key);
        Assert.Throws<InvalidOperationException>(() => key.DataType = typeof(int));
    }

    [Fact]
    public void ItemContainerAndDataTemplateKeysRemainDifferentFamilies()
    {
        var itemKey = new Controls.ItemContainerTemplateKey(typeof(string));
        var dataKey = new DataTemplateKey(typeof(string));

        Assert.False(itemKey.Equals(dataKey));
        Assert.Equal(typeof(string), itemKey.DataType);
        Assert.IsType<TemplateKeyConverter>(TypeDescriptor.GetConverter(dataKey));
    }

    [Fact]
    public void ResourceKeyConvertersDeliberatelyRejectAllConversions()
    {
        System.ComponentModel.TypeConverter componentConverter = new ComponentResourceKeyConverter();
        System.ComponentModel.TypeConverter templateConverter = new TemplateKeyConverter();

        Assert.False(componentConverter.CanConvertFrom(typeof(string)));
        Assert.False(componentConverter.CanConvertTo(typeof(string)));
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = componentConverter.ConvertFromInvariantString("key");
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = componentConverter.ConvertToInvariantString(
                new ComponentResourceKey(typeof(string), "key"));
        });

        Assert.False(templateConverter.CanConvertFrom(typeof(string)));
        Assert.False(templateConverter.CanConvertTo(typeof(string)));
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = templateConverter.ConvertFromInvariantString("key");
        });
        Assert.Throws<NotSupportedException>(() =>
        {
            _ = templateConverter.ConvertToInvariantString(
                new DataTemplateKey(typeof(string)));
        });
    }

    [Fact]
    public void MarkupExtensionReturnTypeAttributePreservesBothConstructors()
    {
        var returnOnly = new MarkupExtensionReturnTypeAttribute(typeof(ResourceKey));
#pragma warning disable CS0618
        var withExpression = new MarkupExtensionReturnTypeAttribute(typeof(ResourceKey), typeof(object));
#pragma warning restore CS0618

#pragma warning disable CS0618
        Assert.Equal(typeof(ResourceKey), returnOnly.ReturnType);
        Assert.Null(returnOnly.ExpressionType);
        Assert.Equal(typeof(ResourceKey), withExpression.ReturnType);
        Assert.Equal(typeof(object), withExpression.ExpressionType);
#pragma warning restore CS0618
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
