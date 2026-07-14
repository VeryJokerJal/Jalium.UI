using System.Collections;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Data;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class AmbiguousCoreApiParityTests
{
    [Fact]
    public void FrameworkElementOwnsThePublicBindingExpressionMethod()
    {
        Assert.Null(typeof(DependencyObject).GetMethod(
            nameof(FrameworkElement.GetBindingExpression),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(DependencyProperty)],
            modifiers: null));

        MethodInfo method = typeof(FrameworkElement).GetMethod(
            nameof(FrameworkElement.GetBindingExpression),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(DependencyProperty)],
            modifiers: null)!;

        Assert.Equal(typeof(BindingExpression), method.ReturnType);
    }

    [Fact]
    public void FrameworkPropertyMetadataOwnsThePublicInheritsProperty()
    {
        Assert.Null(typeof(PropertyMetadata).GetProperty(
            nameof(FrameworkPropertyMetadata.Inherits),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        PropertyInfo property = typeof(FrameworkPropertyMetadata).GetProperty(
            nameof(FrameworkPropertyMetadata.Inherits),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;

        Assert.True(property.CanRead);
        Assert.True(property.SetMethod?.IsPublic);
    }

    [Fact]
    public void ResourceDictionaryExposesOnlyTheNonGenericDictionaryContract()
    {
        Type type = typeof(ResourceDictionary);

        Assert.Contains(typeof(IDictionary), type.GetInterfaces());
        Assert.DoesNotContain(typeof(IDictionary<object, object?>), type.GetInterfaces());
        Assert.Equal(typeof(ICollection), type.GetProperty(nameof(ResourceDictionary.Keys))!.PropertyType);
        Assert.Equal(typeof(ICollection), type.GetProperty(nameof(ResourceDictionary.Values))!.PropertyType);
        Assert.Equal(typeof(void), type.GetMethod(nameof(ResourceDictionary.Remove), [typeof(object)])!.ReturnType);
        Assert.Equal(typeof(IDictionaryEnumerator), type.GetMethod(nameof(ResourceDictionary.GetEnumerator), Type.EmptyTypes)!.ReturnType);
    }

    [Fact]
    public void UIElementPublicAnimationSurfaceUsesAnimationTimelineOnly()
    {
        MethodInfo[] methods = typeof(UIElement)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == nameof(UIElement.BeginAnimation))
            .ToArray();

        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.Equal(typeof(AnimationTimeline), method.GetParameters()[1].ParameterType));
        Assert.Single(
            typeof(UIElement).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == nameof(UIElement.GetAnimationBaseValue));
    }

    [Fact]
    public void UIElementDoesNotPublishTheControlSpecificPressedState()
    {
        Assert.Null(typeof(UIElement).GetProperty(
            "IsPressed",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Null(typeof(UIElement).GetField(
            "IsPressedProperty",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    [Theory]
    [InlineData(typeof(PointConverter))]
    [InlineData(typeof(SizeConverter))]
    [InlineData(typeof(VectorConverter))]
    public void PrimitiveConvertersOverrideRatherThanDuplicateTypeConverterMembers(Type converterType)
    {
        MethodInfo canConvertFrom = converterType.GetMethod(
            nameof(TypeConverter.CanConvertFrom),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(ITypeDescriptorContext), typeof(Type)],
            modifiers: null)!;
        MethodInfo convertFrom = converterType.GetMethod(
            nameof(TypeConverter.ConvertFrom),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(ITypeDescriptorContext), typeof(System.Globalization.CultureInfo), typeof(object)],
            modifiers: null)!;

        Assert.Equal(typeof(TypeConverter), canConvertFrom.GetBaseDefinition().DeclaringType);
        Assert.Equal(typeof(TypeConverter), convertFrom.GetBaseDefinition().DeclaringType);
    }

    [Fact]
    public void FreezableCollectionTypedMembersIntentionallyHideAndImplementBaseContracts()
    {
        Type type = typeof(FreezableCollection<DependencyObject>);

        Assert.Equal(type, type.GetMethod(nameof(FreezableCollection<DependencyObject>.Clone), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(type, type.GetMethod(nameof(FreezableCollection<DependencyObject>.CloneCurrentValue), Type.EmptyTypes)!.ReturnType);
        Type enumeratorType = type.GetMethod(
            nameof(FreezableCollection<DependencyObject>.GetEnumerator),
            Type.EmptyTypes)!.ReturnType;
        Assert.Equal(nameof(FreezableCollection<DependencyObject>.Enumerator), enumeratorType.Name);
        Assert.Equal(typeof(FreezableCollection<>), enumeratorType.DeclaringType);
    }
}
