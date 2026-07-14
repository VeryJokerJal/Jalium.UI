using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ControlsDataAmbiguityParityTests
{
    private const BindingFlags PublicDeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

    private const BindingFlags NonPublicDeclaredInstance =
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void Border_InheritsChildFromDecoratorWithoutRedeclaringIt()
    {
        Assert.Null(typeof(Border).GetProperty(
            nameof(Decorator.Child),
            PublicDeclaredInstance));

        PropertyInfo child = typeof(Border).GetProperty(nameof(Decorator.Child))!;
        Assert.Equal(typeof(Decorator), child.DeclaringType);
        Assert.True(child.GetMethod!.IsVirtual);
        Assert.True(child.SetMethod!.IsVirtual);
    }

    [Fact]
    public void ItemCollection_PublicMutationMethodsAreNotConfusedWithExplicitInterfaces()
    {
        Type type = typeof(ItemCollection);

        AssertPublicDeclaredMethod(type, nameof(ItemCollection.Add), typeof(int), typeof(object));
        AssertPublicDeclaredMethod(type, nameof(ItemCollection.Remove), typeof(void), typeof(object));
        AssertPublicDeclaredMethod(type, nameof(ItemCollection.CopyTo), typeof(void), typeof(Array), typeof(int));

        InterfaceMapping listMap = type.GetInterfaceMap(typeof(IList));
        AssertExplicitInterfaceTarget(listMap, nameof(IList.Add));
        AssertExplicitInterfaceTarget(listMap, nameof(IList.Remove));
    }

    [Fact]
    public void UIElementCollection_ExposesOnlyTheWpfNonGenericEnumeratorContract()
    {
        MethodInfo enumerator = AssertPublicDeclaredMethod(
            typeof(UIElementCollection),
            nameof(UIElementCollection.GetEnumerator),
            typeof(IEnumerator));
        Assert.True(enumerator.IsVirtual);
        Assert.False(typeof(IEnumerable<UIElement>).IsAssignableFrom(typeof(UIElementCollection)));
    }

    [Fact]
    public void ButtonBase_OwnsTheWpfIsPressedSurface()
    {
        PropertyInfo property = typeof(ButtonBase).GetProperty(
            nameof(ButtonBase.IsPressed),
            PublicDeclaredInstance)!;

        Assert.Equal(typeof(ButtonBase), property.DeclaringType);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsFamily);
    }

    [Fact]
    public void DataGrid_InheritsItemsSourceAndItsDependencyPropertyFromItemsControl()
    {
        Type type = typeof(DataGrid);

        Assert.Null(type.GetProperty(nameof(ItemsControl.ItemsSource), PublicDeclaredInstance));
        Assert.Null(type.GetField(
            nameof(ItemsControl.ItemsSourceProperty),
            BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly));
        Assert.Same(ItemsControl.ItemsSourceProperty, DataGrid.ItemsSourceProperty);

        var source = new BindingList<string> { "first" };
        var grid = new DataGrid { ItemsSource = source };
        Assert.Equal(["first"], grid.Items.Cast<string>());

        source.Add("second");
        Assert.Equal(["first", "second"], grid.Items.Cast<string>());
    }

    [Fact]
    public void CollectionView_UsesProtectedVirtualHooksAndExplicitNotificationInterfaces()
    {
        Type type = typeof(CollectionView);

        AssertProtectedVirtualEvent(type, nameof(INotifyCollectionChanged.CollectionChanged));
        AssertProtectedVirtualEvent(type, nameof(INotifyPropertyChanged.PropertyChanged));

        MethodInfo enumerator = type.GetMethod(
            "GetEnumerator",
            NonPublicDeclaredInstance,
            null,
            Type.EmptyTypes,
            null)!;
        Assert.True(enumerator.IsFamily);
        Assert.True(enumerator.IsVirtual);
        Assert.Equal(typeof(IEnumerator), enumerator.ReturnType);

        AssertExplicitInterfaceTargets(type, typeof(INotifyCollectionChanged));
        AssertExplicitInterfaceTargets(type, typeof(INotifyPropertyChanged));
        AssertExplicitInterfaceTargets(type, typeof(IEnumerable));
    }

    [Fact]
    public void CollectionViewGroup_UsesProtectedVirtualPropertyChangedHook()
    {
        Type type = typeof(CollectionViewGroup);

        AssertProtectedVirtualEvent(type, nameof(INotifyPropertyChanged.PropertyChanged));
        AssertExplicitInterfaceTargets(type, typeof(INotifyPropertyChanged));
        Assert.Null(type.GetEvent(
            nameof(INotifyPropertyChanged.PropertyChanged),
            PublicDeclaredInstance));
    }

    [Fact]
    public void CompositeCollection_SeparatesProtectedHookFromExplicitNotificationEvent()
    {
        Type type = typeof(CompositeCollection);

        AssertProtectedVirtualEvent(type, nameof(INotifyCollectionChanged.CollectionChanged));
        AssertExplicitInterfaceTargets(type, typeof(INotifyCollectionChanged));
    }

    [Fact]
    public void XmlNamespaceMappingCollection_SeparatesProtectedMarkupHooksFromExplicitInterface()
    {
        Type type = typeof(XmlNamespaceMappingCollection);

        foreach (string name in new[] { "AddChild", "AddText" })
        {
            MethodInfo method = type.GetMethod(name, NonPublicDeclaredInstance)!;
            Assert.True(method.IsFamily);
            Assert.True(method.IsVirtual);
        }

        AssertExplicitInterfaceTargets(type, typeof(Jalium.UI.Markup.IAddChild));
    }

    private static MethodInfo AssertPublicDeclaredMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters)
    {
        MethodInfo method = type.GetMethod(
            name,
            PublicDeclaredInstance,
            null,
            parameters,
            null)!;
        Assert.Equal(returnType, method.ReturnType);
        return method;
    }

    private static void AssertProtectedVirtualEvent(Type type, string name)
    {
        EventInfo eventInfo = type.GetEvent(name, NonPublicDeclaredInstance)!;
        Assert.True(eventInfo.AddMethod!.IsFamily);
        Assert.True(eventInfo.AddMethod.IsVirtual);
        Assert.False(eventInfo.AddMethod.IsFinal);
    }

    private static void AssertExplicitInterfaceTargets(Type type, Type interfaceType)
    {
        InterfaceMapping map = type.GetInterfaceMap(interfaceType);
        Assert.All(map.TargetMethods, method =>
        {
            Assert.True(method.IsPrivate);
            Assert.True(method.IsFinal);
        });
    }

    private static void AssertExplicitInterfaceTarget(InterfaceMapping map, string memberName)
    {
        int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == memberName);
        Assert.True(index >= 0);
        Assert.True(map.TargetMethods[index].IsPrivate);
        Assert.True(map.TargetMethods[index].IsFinal);
    }
}
