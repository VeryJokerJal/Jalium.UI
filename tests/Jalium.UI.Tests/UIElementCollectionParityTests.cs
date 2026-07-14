using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class UIElementCollectionParityTests
{
    [Fact]
    public void ConstructorAndCollectionInfrastructureMatchWpfSurface()
    {
        var panel = new StackPanel();
        var collection = new UIElementCollection(panel, panel);
        var collectionType = typeof(UIElementCollection);

        collection.Capacity = 8;

        Assert.True(collection.Capacity >= 8);
        Assert.False(collection.IsSynchronized);
        Assert.Same(((ICollection)collection).SyncRoot, collection.SyncRoot);
        Assert.False(((IList)collection).IsFixedSize);
        Assert.False(((IList)collection).IsReadOnly);
        Assert.False(typeof(IList<UIElement>).IsAssignableFrom(collectionType));
        Assert.False(typeof(IEnumerable<UIElement>).IsAssignableFrom(collectionType));
        Assert.Null(collectionType.GetProperty("IsReadOnly", BindingFlags.Instance | BindingFlags.Public));

        Assert.True(collectionType.GetMethod(
            nameof(UIElementCollection.CopyTo),
            [typeof(Array), typeof(int)])!.IsVirtual);
        AssertPublicVirtualMethod(collectionType, nameof(UIElementCollection.Add), typeof(int), typeof(UIElement));
        AssertPublicVirtualMethod(collectionType, nameof(UIElementCollection.Remove), typeof(void), typeof(UIElement));
        AssertPublicVirtualMethod(collectionType, nameof(UIElementCollection.GetEnumerator), typeof(IEnumerator));

        Assert.NotNull(collectionType.GetMethod(
            "SetLogicalParent",
            BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(collectionType.GetMethod(
            "ClearLogicalParent",
            BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void CopyToArrayAndRemoveRangePreserveVisualParenting()
    {
        var panel = new StackPanel();
        var first = new Border();
        var second = new Border();
        var third = new Border();
        panel.Children.Add(first);
        panel.Children.Add(second);
        panel.Children.Add(third);
        var copied = new object[3];

        panel.Children.CopyTo(copied, 0);
        panel.Children.RemoveRange(0, 2);

        Assert.Equal([first, second, third], copied);
        Assert.Null(first.VisualParent);
        Assert.Null(second.VisualParent);
        Assert.Same(panel, third.VisualParent);
        Assert.Single(panel.Children);
    }

    private static void AssertPublicVirtualMethod(
        Type declaringType,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = declaringType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);

        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);
    }
}
