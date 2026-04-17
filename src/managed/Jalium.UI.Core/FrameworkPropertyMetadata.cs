using Jalium.UI.Data;

namespace Jalium.UI;

/// <summary>
/// Reports or applies metadata for a dependency property, specifically adding framework-specific property flags.
/// </summary>
public class FrameworkPropertyMetadata : UIPropertyMetadata
{
    public FrameworkPropertyMetadata() : base() { }
    public FrameworkPropertyMetadata(object? defaultValue) : base(defaultValue) { }
    public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback) : base(null, propertyChangedCallback) { }
    public FrameworkPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback) { }
    public FrameworkPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(defaultValue, propertyChangedCallback, coerceValueCallback) { }
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags) : base(defaultValue)
    {
        SetFlags(flags);
    }
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback)
    {
        SetFlags(flags);
    }
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(defaultValue, propertyChangedCallback, coerceValueCallback)
    {
        SetFlags(flags);
    }
    public FrameworkPropertyMetadata(object? defaultValue, FrameworkPropertyMetadataOptions flags, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited) : base(defaultValue, propertyChangedCallback, coerceValueCallback, isAnimationProhibited)
    {
        SetFlags(flags);
    }

    public bool AffectsMeasure { get; set; }
    public bool AffectsArrange { get; set; }
    public bool AffectsRender { get; set; }
    public bool AffectsParentMeasure { get; set; }
    public bool AffectsParentArrange { get; set; }
    public bool BindsTwoWayByDefault { get; set; }
    public bool IsNotDataBindable { get; set; }
    public bool SubPropertiesDoNotAffectRender { get; set; }
    public UpdateSourceTrigger DefaultUpdateSourceTrigger { get; set; }
    public bool Journal { get; set; }
    public bool OverridesInheritanceBehavior { get; set; }

    private void SetFlags(FrameworkPropertyMetadataOptions flags)
    {
        AffectsMeasure = (flags & FrameworkPropertyMetadataOptions.AffectsMeasure) != 0;
        AffectsArrange = (flags & FrameworkPropertyMetadataOptions.AffectsArrange) != 0;
        AffectsRender = (flags & FrameworkPropertyMetadataOptions.AffectsRender) != 0;
        AffectsParentMeasure = (flags & FrameworkPropertyMetadataOptions.AffectsParentMeasure) != 0;
        AffectsParentArrange = (flags & FrameworkPropertyMetadataOptions.AffectsParentArrange) != 0;
        BindsTwoWayByDefault = (flags & FrameworkPropertyMetadataOptions.BindsTwoWayByDefault) != 0;
        IsNotDataBindable = (flags & FrameworkPropertyMetadataOptions.NotDataBindable) != 0;
        Journal = (flags & FrameworkPropertyMetadataOptions.Journal) != 0;
        SubPropertiesDoNotAffectRender = (flags & FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender) != 0;
        OverridesInheritanceBehavior = (flags & FrameworkPropertyMetadataOptions.OverridesInheritanceBehavior) != 0;
    }
}

/// <summary>
/// Provides metadata for a UI element dependency property.
/// </summary>
public class UIPropertyMetadata : PropertyMetadata
{
    public UIPropertyMetadata() : base() { }
    public UIPropertyMetadata(object? defaultValue) : base(defaultValue) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(defaultValue, propertyChangedCallback, coerceValueCallback) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited) : base(defaultValue, propertyChangedCallback, coerceValueCallback)
    {
        IsAnimationProhibited = isAnimationProhibited;
    }

    public bool IsAnimationProhibited { get; set; }
}

[Flags]
public enum FrameworkPropertyMetadataOptions
{
    None = 0,
    AffectsMeasure = 0x1,
    AffectsArrange = 0x2,
    AffectsParentMeasure = 0x4,
    AffectsParentArrange = 0x8,
    AffectsRender = 0x10,
    Inherits = 0x20,
    OverridesInheritanceBehavior = 0x40,
    NotDataBindable = 0x80,
    BindsTwoWayByDefault = 0x100,
    Journal = 0x400,
    SubPropertiesDoNotAffectRender = 0x800,
}
