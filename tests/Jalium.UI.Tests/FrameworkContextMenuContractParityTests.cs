using System.Reflection;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class FrameworkContextMenuContractParityTests
{
    [Theory]
    [InlineData(typeof(FrameworkElement))]
    [InlineData(typeof(FrameworkContentElement))]
    public void ContextMenuClrAndDependencyProperties_UseExactContextMenuType(Type ownerType)
    {
        PropertyInfo? property = ownerType.GetProperty(
            nameof(FrameworkElement.ContextMenu),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        FieldInfo? field = ownerType.GetField(
            nameof(FrameworkElement.ContextMenuProperty),
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.NotNull(field);
        DependencyProperty dependencyProperty = Assert.IsType<DependencyProperty>(field!.GetValue(null));

        Assert.Equal(typeof(ContextMenu), property!.PropertyType);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal(typeof(ContextMenu), dependencyProperty.PropertyType);
    }

    [Theory]
    [InlineData(typeof(FrameworkElement))]
    [InlineData(typeof(FrameworkContentElement))]
    public void ContextMenuAccessor_RoundTripsTypedValue(Type ownerType)
    {
        var owner = Assert.IsAssignableFrom<DependencyObject>(Activator.CreateInstance(ownerType));
        var menu = new ContextMenu();
        PropertyInfo? property = ownerType.GetProperty(
            nameof(FrameworkElement.ContextMenu),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);

        property!.SetValue(owner, menu);

        Assert.Same(menu, property.GetValue(owner));
    }
}
