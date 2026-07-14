using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class DependencyPropertyMetadataParityTests
{
    [Fact]
    public void PropertyMetadata_IsMutableUntilRegistration_ThenSealed()
    {
        var changed = new PropertyChangedCallback(static (_, _) => { });
        var coerce = new CoerceValueCallback(static (_, value) => value);
        var metadata = new TrackingMetadata
        {
            DefaultValue = 17,
            PropertyChangedCallback = changed,
            CoerceValueCallback = coerce,
        };

        Assert.False(metadata.PublicIsSealed);
        Assert.Throws<ArgumentException>(() => metadata.DefaultValue = DependencyProperty.UnsetValue);

        var property = DependencyProperty.Register(
            "MutableUntilRegistration",
            typeof(int),
            typeof(BaseOwner),
            metadata);

        Assert.True(metadata.PublicIsSealed);
        Assert.Equal(1, metadata.ApplyCount);
        Assert.Same(property, metadata.AppliedProperty);
        Assert.Equal(typeof(BaseOwner), metadata.AppliedTargetType);
        Assert.True(metadata.AppliedAsCurrentMetadata);
        Assert.Throws<InvalidOperationException>(() => metadata.DefaultValue = 18);
        Assert.Throws<InvalidOperationException>(() => metadata.PropertyChangedCallback = null);
        Assert.Throws<InvalidOperationException>(() => metadata.CoerceValueCallback = null);
    }

    [Fact]
    public void OverrideMetadata_MergesDefaultsAndCallbacks_BaseFirst_ThenSeals()
    {
        var calls = new List<string>();
        PropertyChangedCallback baseChanged = (_, _) => calls.Add("base");
        PropertyChangedCallback derivedChanged = (_, _) => calls.Add("derived");
        CoerceValueCallback baseCoerce = static (_, value) => value;

        var property = DependencyProperty.Register(
            "MergedMetadata",
            typeof(int),
            typeof(BaseOwner),
            new TrackingMetadata(23, baseChanged, baseCoerce));
        var derivedMetadata = new TrackingMetadata(derivedChanged);

        property.OverrideMetadata(typeof(DerivedOwner), derivedMetadata);

        Assert.True(derivedMetadata.PublicIsSealed);
        Assert.Equal(1, derivedMetadata.MergeCount);
        Assert.Equal(1, derivedMetadata.ApplyCount);
        Assert.True(derivedMetadata.AppliedAsCurrentMetadata);
        Assert.Equal(23, derivedMetadata.DefaultValue);
        Assert.Same(baseCoerce, derivedMetadata.CoerceValueCallback);

        derivedMetadata.PropertyChangedCallback!(
            new DerivedOwner(),
            new DependencyPropertyChangedEventArgs(property, 0, 1));
        Assert.Equal(["base", "derived"], calls);
    }

    [Fact]
    public void AddOwner_WithMetadata_MergesAndSealsOwnerMetadata()
    {
        var property = DependencyProperty.Register(
            "AddedOwnerMetadata",
            typeof(string),
            typeof(BaseOwner),
            new PropertyMetadata("root"));
        var ownerMetadata = new TrackingMetadata();

        var returned = property.AddOwner(typeof(AdditionalOwner), ownerMetadata);

        Assert.Same(property, returned);
        Assert.True(ownerMetadata.PublicIsSealed);
        Assert.Equal("root", ownerMetadata.DefaultValue);
        Assert.Same(ownerMetadata, property.GetMetadata(typeof(AdditionalOwner)));
        Assert.Same(property, DependencyProperty.FromName(typeof(AdditionalOwner), "AddedOwnerMetadata"));
    }

    [Fact]
    public void GetMetadata_InstanceAndDependencyObjectType_ReturnMostSpecificMetadata()
    {
        var property = DependencyProperty.Register(
            "MetadataLookupOverloads",
            typeof(string),
            typeof(BaseOwner),
            new PropertyMetadata("base"));
        var derivedMetadata = new PropertyMetadata("derived");
        property.OverrideMetadata(typeof(DerivedOwner), derivedMetadata);

        var instance = new DerivedOwner();
        var dependencyObjectType = DependencyObjectType.FromSystemType(typeof(DerivedOwner));

        Assert.Same(derivedMetadata, property.GetMetadata(instance));
        Assert.Same(derivedMetadata, property.GetMetadata(dependencyObjectType));
        Assert.Same(property.DefaultMetadata, property.GetMetadata((DependencyObjectType?)null));
    }

    [Fact]
    public void RegisterAttachedReadOnly_KeyAuthorizesOverride_AndMetadataIsSealed()
    {
        var metadata = new FrameworkPropertyMetadata(7);
        var key = DependencyProperty.RegisterAttachedReadOnly(
            "ReadOnlyAttachedMetadata",
            typeof(int),
            typeof(ReadOnlyOwner),
            metadata);
        var property = key.DependencyProperty;

        Assert.True(property.ReadOnly);
        Assert.False(metadata.IsDataBindingAllowed);
        Assert.Throws<InvalidOperationException>(() =>
            property.OverrideMetadata(typeof(ReadOnlyDerivedOwner), new FrameworkPropertyMetadata()));

        var wrongKey = DependencyProperty.RegisterReadOnly(
            "OtherReadOnlyMetadata",
            typeof(int),
            typeof(ReadOnlyOwner),
            new FrameworkPropertyMetadata(0));
        Assert.Throws<ArgumentException>(() =>
            property.OverrideMetadata(typeof(ReadOnlyDerivedOwner), new FrameworkPropertyMetadata(), wrongKey));

        var derivedMetadata = new FrameworkPropertyMetadata();
        key.OverrideMetadata(typeof(ReadOnlyDerivedOwner), derivedMetadata);

        Assert.Same(derivedMetadata, property.GetMetadata(typeof(ReadOnlyDerivedOwner)));
        Assert.Equal(7, derivedMetadata.DefaultValue);
        Assert.False(derivedMetadata.IsDataBindingAllowed);
        Assert.Throws<InvalidOperationException>(() => derivedMetadata.Inherits = true);
    }

    [Fact]
    public void RegisterAttachedReadOnly_ValidatorRejectsInvalidDefault()
    {
        Assert.Throws<ArgumentException>(() =>
            DependencyProperty.RegisterAttachedReadOnly(
                "ValidatedReadOnlyAttachedMetadata",
                typeof(int),
                typeof(ReadOnlyOwner),
                new PropertyMetadata(-1),
                static value => value is int number && number >= 0));
    }

    [Fact]
    public void FrameworkPropertyMetadata_MergeUsesCumulativeAndOverrideSemantics()
    {
        var baseMetadata = new FrameworkPropertyMetadata(
            "base",
            FrameworkPropertyMetadataOptions.AffectsMeasure |
            FrameworkPropertyMetadataOptions.Inherits |
            FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender |
            FrameworkPropertyMetadataOptions.Journal |
            FrameworkPropertyMetadataOptions.OverridesInheritanceBehavior);
        var property = DependencyProperty.Register(
            "FrameworkMetadataMerge",
            typeof(string),
            typeof(FrameworkBaseOwner),
            baseMetadata);

        var derivedMetadata = new FrameworkPropertyMetadata
        {
            AffectsArrange = true,
            Inherits = false,
            SubPropertiesDoNotAffectRender = false,
            Journal = false,
            OverridesInheritanceBehavior = false,
            DefaultUpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
        };

        property.OverrideMetadata(typeof(FrameworkDerivedOwner), derivedMetadata);

        Assert.Equal("base", derivedMetadata.DefaultValue);
        Assert.True(derivedMetadata.AffectsMeasure);
        Assert.True(derivedMetadata.AffectsArrange);
        Assert.False(derivedMetadata.Inherits);
        Assert.False(derivedMetadata.SubPropertiesDoNotAffectRender);
        Assert.False(derivedMetadata.Journal);
        Assert.False(derivedMetadata.OverridesInheritanceBehavior);
        Assert.Equal(UpdateSourceTrigger.LostFocus, derivedMetadata.DefaultUpdateSourceTrigger);
        Assert.True(derivedMetadata.IsDataBindingAllowed);
        Assert.Throws<InvalidOperationException>(() => derivedMetadata.AffectsRender = true);
    }

    [Fact]
    public void FrameworkPropertyMetadata_NotDataBindableFlagDisallowsBinding()
    {
        var metadata = new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.NotDataBindable);

        DependencyProperty.Register(
            "NotDataBindableMetadata",
            typeof(int),
            typeof(FrameworkBaseOwner),
            metadata);

        Assert.False(metadata.IsDataBindingAllowed);
    }

    private sealed class TrackingMetadata : PropertyMetadata
    {
        public TrackingMetadata()
        {
        }

        public TrackingMetadata(object? defaultValue, PropertyChangedCallback? changed, CoerceValueCallback? coerce)
            : base(defaultValue, changed, coerce)
        {
        }

        public TrackingMetadata(PropertyChangedCallback? changed)
            : base(changed)
        {
        }

        public bool PublicIsSealed => IsSealed;
        public int MergeCount { get; private set; }
        public int ApplyCount { get; private set; }
        public DependencyProperty? AppliedProperty { get; private set; }
        public Type? AppliedTargetType { get; private set; }
        public bool AppliedAsCurrentMetadata { get; private set; }

        protected override void Merge(PropertyMetadata baseMetadata, DependencyProperty dp)
        {
            MergeCount++;
            base.Merge(baseMetadata, dp);
        }

        protected override void OnApply(DependencyProperty dp, Type targetType)
        {
            ApplyCount++;
            AppliedProperty = dp;
            AppliedTargetType = targetType;
            AppliedAsCurrentMetadata = ReferenceEquals(dp.GetMetadata(targetType), this);
            base.OnApply(dp, targetType);
        }
    }

    private class BaseOwner : DependencyObject { }
    private sealed class DerivedOwner : BaseOwner { }
    private sealed class AdditionalOwner : DependencyObject { }
    private class ReadOnlyOwner : DependencyObject { }
    private sealed class ReadOnlyDerivedOwner : ReadOnlyOwner { }
    private class FrameworkBaseOwner : DependencyObject { }
    private sealed class FrameworkDerivedOwner : FrameworkBaseOwner { }
}
