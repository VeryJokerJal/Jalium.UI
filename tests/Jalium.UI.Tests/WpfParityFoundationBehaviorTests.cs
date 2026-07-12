using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace Jalium.UI.Tests;

[CollectionDefinition(nameof(WpfParityFoundationBehaviorCollection), DisableParallelization = true)]
public sealed class WpfParityFoundationBehaviorCollection;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public class WpfParityFoundationBehaviorTests
{
    private static readonly DependencyProperty BrowsableProperty = DependencyProperty.RegisterAttached(
        "FoundationBrowsable",
        typeof(int),
        typeof(OwnerVisual),
        new PropertyMetadata(0));

    [Fact]
    public void AttachedPropertyBrowsableAttribute_DefaultsToIntersectionSemantics()
    {
        var attribute = new NeverBrowsableAttribute();

        Assert.False(attribute.UnionResults);
        Assert.False(attribute.IsBrowsable(new DependencyObject(), BrowsableProperty));
    }

    [Fact]
    public void AttachedPropertyBrowsableForType_MatchesDependencyObjectHierarchyAndUnionsInstances()
    {
        var attribute = new AttachedPropertyBrowsableForTypeAttribute(typeof(TestVisual));
        var equalAttribute = new AttachedPropertyBrowsableForTypeAttribute(typeof(TestVisual));

        Assert.Equal(typeof(TestVisual), attribute.TargetType);
        Assert.Same(attribute, attribute.TypeId);
        Assert.True(attribute.UnionResults);
        Assert.True(attribute.IsBrowsable(new OwnerVisual(), BrowsableProperty));
        Assert.False(attribute.IsBrowsable(new DependencyObject(), BrowsableProperty));
        Assert.Equal(attribute, equalAttribute);
        Assert.Equal(attribute.GetHashCode(), equalAttribute.GetHashCode());
        Assert.NotEqual(attribute, new AttachedPropertyBrowsableForTypeAttribute(typeof(OwnerVisual)));

        var unsupportedType = new AttachedPropertyBrowsableForTypeAttribute(typeof(string));
        Assert.False(unsupportedType.IsBrowsable(new DependencyObject(), BrowsableProperty));

        Assert.Throws<ArgumentNullException>(
            "targetType",
            () => new AttachedPropertyBrowsableForTypeAttribute(null!));
        Assert.Throws<ArgumentNullException>(
            "d",
            () => attribute.IsBrowsable(null!, BrowsableProperty));
        Assert.Throws<ArgumentNullException>(
            "dp",
            () => attribute.IsBrowsable(new DependencyObject(), null!));
    }

    [Fact]
    public void AttachedPropertyBrowsableWhenAttributePresent_RequiresNonDefaultAttribute()
    {
        var attribute = new AttachedPropertyBrowsableWhenAttributePresentAttribute(typeof(BrowseMarkerAttribute));
        var equalAttribute = new AttachedPropertyBrowsableWhenAttributePresentAttribute(typeof(BrowseMarkerAttribute));

        Assert.Equal(typeof(BrowseMarkerAttribute), attribute.AttributeType);
        Assert.Equal(typeof(AttachedPropertyBrowsableWhenAttributePresentAttribute), attribute.TypeId);
        Assert.True(attribute.IsBrowsable(new MarkedDependencyObject(), BrowsableProperty));
        Assert.False(attribute.IsBrowsable(new DefaultMarkedDependencyObject(), BrowsableProperty));
        Assert.False(attribute.IsBrowsable(new DependencyObject(), BrowsableProperty));
        Assert.Equal(attribute, equalAttribute);
        Assert.Equal(attribute.GetHashCode(), equalAttribute.GetHashCode());
        Assert.NotEqual(
            attribute,
            new AttachedPropertyBrowsableWhenAttributePresentAttribute(typeof(ObsoleteAttribute)));

        Assert.Throws<ArgumentNullException>(
            "attributeType",
            () => new AttachedPropertyBrowsableWhenAttributePresentAttribute(null!));
    }

    [Fact]
    public void AttachedPropertyBrowsableForChildren_UsesImmediateOrFullLogicalAncestry()
    {
        var owner = new OwnerVisual();
        var intermediate = new TestVisual();
        var descendant = new TestVisual();
        owner.Attach(intermediate);
        intermediate.Attach(descendant);

        var shallow = new AttachedPropertyBrowsableForChildrenAttribute();
        var deep = new AttachedPropertyBrowsableForChildrenAttribute { IncludeDescendants = true };

        Assert.True(shallow.IsBrowsable(intermediate, BrowsableProperty));
        Assert.False(shallow.IsBrowsable(descendant, BrowsableProperty));
        Assert.True(deep.IsBrowsable(descendant, BrowsableProperty));

        var unrelatedParent = new TestVisual();
        var unrelatedChild = new TestVisual();
        unrelatedParent.Attach(unrelatedChild);
        Assert.False(deep.IsBrowsable(unrelatedChild, BrowsableProperty));

        Assert.Equal(shallow, new AttachedPropertyBrowsableForChildrenAttribute());
        Assert.NotEqual(shallow, deep);
        Assert.Equal(
            deep.GetHashCode(),
            new AttachedPropertyBrowsableForChildrenAttribute { IncludeDescendants = true }.GetHashCode());
    }

    [Fact]
    public void BaseCompatibilityPreferences_RoundTripThenSealOnInternalConsumption()
    {
        BaseCompatibilityPreferences.ResetForTests();
        try
        {
            Assert.False(BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance);
            Assert.True(BaseCompatibilityPreferences.FlowDispatcherSynchronizationContextPriority);
            Assert.True(BaseCompatibilityPreferences.InlineDispatcherSynchronizationContextSend);
            Assert.Equal(
                BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Continue,
                BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure);

            BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = true;
            BaseCompatibilityPreferences.FlowDispatcherSynchronizationContextPriority = false;
            BaseCompatibilityPreferences.InlineDispatcherSynchronizationContextSend = false;

            Assert.True(BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance);
            Assert.False(BaseCompatibilityPreferences.FlowDispatcherSynchronizationContextPriority);
            Assert.False(BaseCompatibilityPreferences.InlineDispatcherSynchronizationContextSend);

            Assert.True(BaseCompatibilityPreferences.GetReuseDispatcherSynchronizationContextInstance());
            Assert.False(BaseCompatibilityPreferences.GetFlowDispatcherSynchronizationContextPriority());
            Assert.False(BaseCompatibilityPreferences.GetInlineDispatcherSynchronizationContextSend());

            Assert.Throws<InvalidOperationException>(
                () => BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = false);
            Assert.Throws<InvalidOperationException>(
                () => BaseCompatibilityPreferences.FlowDispatcherSynchronizationContextPriority = true);
            Assert.Throws<InvalidOperationException>(
                () => BaseCompatibilityPreferences.InlineDispatcherSynchronizationContextSend = true);

            BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure =
                BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset;
            Assert.Equal(
                BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailureOptions.Reset,
                BaseCompatibilityPreferences.HandleDispatcherRequestProcessingFailure);

            BaseCompatibilityPreferences.MatchPackageSignatureMethodToPackagePartDigestMethod = false;
            Assert.False(BaseCompatibilityPreferences.MatchPackageSignatureMethodToPackagePartDigestMethod);
        }
        finally
        {
            BaseCompatibilityPreferences.ResetForTests();
        }
    }

    [Fact]
    public void CoreCompatibilityPreferences_RoundTripAndSealOnFirstConsumedValue()
    {
        CoreCompatibilityPreferences.ResetForTests();
        try
        {
            Assert.False(CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope);
            Assert.True(CoreCompatibilityPreferences.IncludeAllInkInBoundingBox);
            Assert.True(CoreCompatibilityPreferences.TargetsAtLeast_Desktop_V4_5);

            CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;
            CoreCompatibilityPreferences.IncludeAllInkInBoundingBox = false;
            CoreCompatibilityPreferences.EnableMultiMonitorDisplayClipping = false;

            Assert.True(CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope);
            Assert.Equal(false, CoreCompatibilityPreferences.EnableMultiMonitorDisplayClipping);
            Assert.True(CoreCompatibilityPreferences.GetIsAltKeyRequiredInAccessKeyDefaultScope());
            Assert.False(CoreCompatibilityPreferences.GetIncludeAllInkInBoundingBox());
            Assert.Equal(false, CoreCompatibilityPreferences.GetEnableMultiMonitorDisplayClipping());

            Assert.Throws<InvalidOperationException>(
                () => CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = false);
            Assert.Throws<InvalidOperationException>(
                () => CoreCompatibilityPreferences.IncludeAllInkInBoundingBox = true);
            Assert.Throws<InvalidOperationException>(
                () => CoreCompatibilityPreferences.EnableMultiMonitorDisplayClipping = true);
        }
        finally
        {
            CoreCompatibilityPreferences.ResetForTests();
        }
    }

    [Fact]
    public void CultureInfoIetfLanguageTagConverter_ConvertsStringsAndInstanceDescriptors()
    {
        var converter = new CultureInfoIetfLanguageTagConverter();

        Assert.True(converter.CanConvertFrom(null, typeof(string)));
        Assert.False(converter.CanConvertFrom(null, typeof(int)));
        Assert.True(converter.CanConvertTo(null, typeof(string)));
        Assert.True(converter.CanConvertTo(null, typeof(InstanceDescriptor)));
        Assert.False(converter.CanConvertTo(null, typeof(int)));

        var culture = Assert.IsType<CultureInfo>(
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, "en-US"));
        Assert.Equal("en-US", culture.Name);
        Assert.Equal("en-US", converter.ConvertTo(null, CultureInfo.InvariantCulture, culture, typeof(string)));

        var descriptor = Assert.IsType<InstanceDescriptor>(
            converter.ConvertTo(null, CultureInfo.InvariantCulture, culture, typeof(InstanceDescriptor)));
        var recreatedCulture = Assert.IsType<CultureInfo>(descriptor.Invoke());
        Assert.Equal(culture.Name, recreatedCulture.Name);

        Assert.Throws<NotSupportedException>(
            () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, 42));
        Assert.Throws<NotSupportedException>(
            () => converter.ConvertTo(null, CultureInfo.InvariantCulture, 42, typeof(string)));
        Assert.Throws<ArgumentNullException>(
            "destinationType",
            () => converter.ConvertTo(null, CultureInfo.InvariantCulture, culture, null!));
    }

    [Fact]
    public void DataFormat_ValidatesNameAndPreservesValues()
    {
        var format = new DataFormat("Custom Format", -42);

        Assert.Equal("Custom Format", format.Name);
        Assert.Equal(-42, format.Id);
        Assert.Equal(" ", new DataFormat(" ", 1).Name);

        Assert.Throws<ArgumentNullException>("name", () => new DataFormat(null!, 0));
        ArgumentException exception = Assert.Throws<ArgumentException>(() => new DataFormat(string.Empty, 0));
        Assert.Null(exception.ParamName);
    }

    private sealed class NeverBrowsableAttribute : AttachedPropertyBrowsableAttribute
    {
        internal override bool IsBrowsable(DependencyObject d, DependencyProperty dp) => false;
    }

    [AttributeUsage(AttributeTargets.Class)]
    private sealed class BrowseMarkerAttribute(bool isDefault = false) : Attribute
    {
        public override bool IsDefaultAttribute() => isDefault;
    }

    [BrowseMarker]
    private sealed class MarkedDependencyObject : DependencyObject;

    [BrowseMarker(isDefault: true)]
    private sealed class DefaultMarkedDependencyObject : DependencyObject;

    private class TestVisual : Visual
    {
        public void Attach(Visual child) => AddVisualChild(child);
    }

    private sealed class OwnerVisual : TestVisual;
}
