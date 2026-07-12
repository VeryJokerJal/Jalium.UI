using System.ComponentModel;

namespace Jalium.UI.Tests;

public sealed class ExpressionWpfParityTests
{
    [Fact]
    public void Expression_HasNoPublicConstructorAndUsesBlockingConverter()
    {
        Assert.Empty(typeof(Expression).GetConstructors());

        var attribute = Assert.Single(
            TypeDescriptor.GetAttributes(typeof(Expression)).OfType<TypeConverterAttribute>());
        Assert.Equal(typeof(ExpressionConverter).AssemblyQualifiedName, attribute.ConverterTypeName);
    }

    [Fact]
    public void Converter_BlocksAllConversions()
    {
        var converter = new ExpressionConverter();

        Assert.False(converter.CanConvertFrom(typeof(string)));
        Assert.False(converter.CanConvertTo(typeof(string)));
        Assert.Throws<NotSupportedException>(() => converter.ConvertFrom("value"));
        Assert.Throws<NotSupportedException>(() => converter.ConvertTo(new object(), typeof(string)));
    }

    [Fact]
    public void BindingExpressionBase_DerivesFromExpressionAndImplementsWeakListener()
    {
        Assert.Equal(typeof(Expression), typeof(Jalium.UI.Data.BindingExpressionBase).BaseType);
        Assert.Contains(typeof(IWeakEventListener), typeof(Jalium.UI.Data.BindingExpressionBase).GetInterfaces());
    }
}
