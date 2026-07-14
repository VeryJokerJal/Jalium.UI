using System.Collections;
using System.Reflection;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

public sealed class MediaMarkupCollectionApiParityTests
{
    [Fact]
    public void VisualCollectionUsesTheWpfNonGenericSurfaceAndTypedEnumerator()
    {
        Type type = typeof(VisualCollection);

        Assert.True(typeof(ICollection).IsAssignableFrom(type));
        Assert.True(typeof(IEnumerable).IsAssignableFrom(type));
        Assert.False(typeof(IList<Visual>).IsAssignableFrom(type));
        Assert.False(typeof(IEnumerable<Visual>).IsAssignableFrom(type));
        Assert.Equal(
            typeof(int),
            type.GetMethod(nameof(VisualCollection.Add), [typeof(Visual)])!.ReturnType);
        Assert.Equal(
            typeof(void),
            type.GetMethod(nameof(VisualCollection.Remove), [typeof(Visual)])!.ReturnType);
        Assert.Equal(
            typeof(VisualCollection.Enumerator),
            type.GetMethod(nameof(VisualCollection.GetEnumerator), Type.EmptyTypes)!.ReturnType);
        Assert.Equal([typeof(IEnumerator)], typeof(VisualCollection.Enumerator).GetInterfaces());

        var owner = new ContainerVisual();
        var first = new DrawingVisual();
        var second = new DrawingVisual();
        Assert.Equal(0, owner.Children.Add(first));
        Assert.Equal(1, owner.Children.Add(second));
        Assert.Same(owner, VisualTreeHelper.GetParent(first));

        Assert.Throws<ArgumentException>(() => owner.Children.Add(first));
        Assert.Equal(2, owner.Children.Count);

        VisualCollection.Enumerator enumerator = owner.Children.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        owner.Children.Add(null!);
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        Assert.Null(owner.Children[2]);

        owner.Children.Remove(first);
        Assert.Null(VisualTreeHelper.GetParent(first));
        Assert.Equal(2, owner.Children.Count);
    }

    [Fact]
    public void ClockCollectionUsesOnePublicImplementationPerCollectionMember()
    {
        Type type = typeof(ClockCollection);

        Assert.False(type.IsSealed);
        Assert.True(typeof(ICollection<Clock>).IsAssignableFrom(type));
        Assert.False(typeof(IReadOnlyList<Clock>).IsAssignableFrom(type));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IEnumerable.GetEnumerator));

        foreach (string name in new[]
                 { nameof(ClockCollection.Add), nameof(ClockCollection.Clear), nameof(ClockCollection.Remove) })
        {
            Assert.Single(
                type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
                method => method.Name == name);
        }

        ConstructorInfo[] constructors = type.GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.Contains(constructors, constructor =>
            constructor.IsPrivate && constructor.GetParameters().Length == 0);
        Assert.Contains(constructors, constructor =>
            constructor.IsAssembly &&
            constructor.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual([typeof(Clock)]));
    }

    [Fact]
    public void TypedCollectionEnumeratorsAndKeyFrameAdaptersAreNotDuplicatePublicMembers()
    {
        AssertSingleTypedPublicEnumerator<TextDecorationCollection, TextDecoration>(
            typeof(TextDecorationCollection.Enumerator));
        AssertSingleTypedPublicEnumerator<Point3DCollection, Point3D>(
            typeof(Point3DCollection.Enumerator));

        PropertyInfo keyFrames = Assert.Single(
            typeof(DoubleAnimationUsingKeyFrames)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            property => property.Name == nameof(DoubleAnimationUsingKeyFrames.KeyFrames));
        Assert.Equal(typeof(DoubleKeyFrameCollection), keyFrames.PropertyType);

        PropertyInfo indexer = Assert.Single(
            typeof(DoubleKeyFrameCollection)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            property => property.GetIndexParameters().Length == 1);
        Assert.Equal(typeof(DoubleKeyFrame), indexer.PropertyType);
    }

    [Fact]
    public void XmlCollectionsUseWpfVirtualAndExplicitEnumeratorPatterns()
    {
        Type mappings = typeof(XmlNamespaceMappingCollection);
        MethodInfo getEnumerator = mappings.GetMethod(
            nameof(IEnumerable.GetEnumerator),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null)!;
        Assert.Equal(typeof(IEnumerator), getEnumerator.ReturnType);
        Assert.True(getEnumerator.IsVirtual);

        MethodInfo protectedEnumerator = mappings.GetMethod(
            "ProtectedGetEnumerator",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.True(protectedEnumerator.IsFamily);
        Assert.Equal(typeof(IEnumerator<XmlNamespaceMapping>), protectedEnumerator.ReturnType);

        foreach (string name in new[] { "AddChild", "AddText" })
        {
            MethodInfo method = mappings.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
            Assert.True(method.IsFamily);
            Assert.True(method.IsVirtual);
        }

        Type xmlns = typeof(XmlnsDictionary);
        Assert.Single(
            xmlns.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(IDictionary.Contains));
        Assert.Equal(2, xmlns.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Count(property => property.Name == "Item"));
        MethodInfo protectedDictionaryEnumerator = xmlns.GetMethod(
            nameof(IEnumerable.GetEnumerator),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null)!;
        Assert.True(protectedDictionaryEnumerator.IsFamily);
        Assert.Equal(typeof(IEnumerator), protectedDictionaryEnumerator.ReturnType);
    }

    private static void AssertSingleTypedPublicEnumerator<TCollection, TItem>(Type returnType)
    {
        MethodInfo method = Assert.Single(
            typeof(TCollection)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            candidate => candidate.Name == nameof(IEnumerable.GetEnumerator));
        Assert.Equal(returnType, method.ReturnType);

        InterfaceMapping genericMap = typeof(TCollection).GetInterfaceMap(typeof(IEnumerable<TItem>));
        MethodInfo implementation = Assert.Single(genericMap.TargetMethods);
        Assert.False(implementation.IsPublic);
    }
}
