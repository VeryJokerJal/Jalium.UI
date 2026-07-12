using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI;

/// <summary>
/// Reports or applies metadata for a dependency property, specifically adding framework-specific property flags.
/// </summary>
public class FrameworkPropertyMetadata : UIPropertyMetadata
{
    private bool _affectsMeasure;
    private bool _affectsArrange;
    private bool _affectsRender;
    private bool _affectsParentMeasure;
    private bool _affectsParentArrange;
    private bool _affectsCompositionOnly;
    private bool _bindsTwoWayByDefault;
    private bool _isNotDataBindable;
    private bool _subPropertiesDoNotAffectRender;
    private bool _journal;
    private bool _overridesInheritanceBehavior;
    private bool _readOnly;
    private bool _subPropertiesDoNotAffectRenderModified;
    private bool _journalModified;
    private bool _overridesInheritanceBehaviorModified;
    private bool _defaultUpdateSourceTriggerModified;
    private UpdateSourceTrigger _defaultUpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;

    public FrameworkPropertyMetadata() : base() { }
    public FrameworkPropertyMetadata(object? defaultValue) : base(defaultValue) { }
    public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback) : base(propertyChangedCallback) { }
    public FrameworkPropertyMetadata(PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(propertyChangedCallback)
    {
        CoerceValueCallback = coerceValueCallback;
    }
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

    public FrameworkPropertyMetadata(
        object? defaultValue,
        FrameworkPropertyMetadataOptions flags,
        PropertyChangedCallback? propertyChangedCallback,
        CoerceValueCallback? coerceValueCallback,
        bool isAnimationProhibited,
        UpdateSourceTrigger defaultUpdateSourceTrigger)
        : base(defaultValue, propertyChangedCallback, coerceValueCallback, isAnimationProhibited)
    {
        if (!Enum.IsDefined(defaultUpdateSourceTrigger))
        {
            throw new InvalidEnumArgumentException(
                nameof(defaultUpdateSourceTrigger),
                (int)defaultUpdateSourceTrigger,
                typeof(UpdateSourceTrigger));
        }

        if (defaultUpdateSourceTrigger == UpdateSourceTrigger.Default)
        {
            throw new ArgumentException(
                "The default update source trigger cannot be UpdateSourceTrigger.Default.",
                nameof(defaultUpdateSourceTrigger));
        }

        SetFlags(flags);
        DefaultUpdateSourceTrigger = defaultUpdateSourceTrigger;
    }

    public bool AffectsMeasure
    {
        get => _affectsMeasure;
        set { ThrowIfSealed(); _affectsMeasure = value; }
    }

    public bool AffectsArrange
    {
        get => _affectsArrange;
        set { ThrowIfSealed(); _affectsArrange = value; }
    }

    public bool AffectsRender
    {
        get => _affectsRender;
        set { ThrowIfSealed(); _affectsRender = value; }
    }

    public bool AffectsParentMeasure
    {
        get => _affectsParentMeasure;
        set { ThrowIfSealed(); _affectsParentMeasure = value; }
    }

    public bool AffectsParentArrange
    {
        get => _affectsParentArrange;
        set { ThrowIfSealed(); _affectsParentArrange = value; }
    }

    /// <summary>
    /// When set, changes to this property are treated as a composition-only invalidation:
    /// the parent's child-render loop reads the live property value via PushOpacity /
    /// PushTransform / PushClip each frame, so the cached drawing of this visual stays
    /// valid. The framework will trigger <see cref="UIElement.InvalidateComposition"/>
    /// instead of <see cref="UIElement.InvalidateVisual()"/> for animation ticks and
    /// fallback paths. Implies <c>AffectsRender</c>.
    /// </summary>
    public bool AffectsCompositionOnly
    {
        get => _affectsCompositionOnly;
        set { ThrowIfSealed(); _affectsCompositionOnly = value; }
    }

    public bool BindsTwoWayByDefault
    {
        get => _bindsTwoWayByDefault;
        set { ThrowIfSealed(); _bindsTwoWayByDefault = value; }
    }

    public bool IsNotDataBindable
    {
        get => _isNotDataBindable;
        set { ThrowIfSealed(); _isNotDataBindable = value; }
    }

    public bool SubPropertiesDoNotAffectRender
    {
        get => _subPropertiesDoNotAffectRender;
        set
        {
            ThrowIfSealed();
            _subPropertiesDoNotAffectRender = value;
            _subPropertiesDoNotAffectRenderModified = true;
        }
    }

    /// <summary>
    /// Gets or sets whether the dependency-property value is inherited by descendants.
    /// </summary>
    public new bool Inherits
    {
        get => base.Inherits;
        set => base.Inherits = value;
    }

    public UpdateSourceTrigger DefaultUpdateSourceTrigger
    {
        get => _defaultUpdateSourceTrigger;
        set
        {
            ThrowIfSealed();

            if (!Enum.IsDefined(value))
            {
                throw new InvalidEnumArgumentException(
                    nameof(value),
                    (int)value,
                    typeof(UpdateSourceTrigger));
            }

            if (value == UpdateSourceTrigger.Default)
                throw new ArgumentException("The default update source trigger cannot be UpdateSourceTrigger.Default.", nameof(value));

            _defaultUpdateSourceTrigger = value;
            _defaultUpdateSourceTriggerModified = true;
        }
    }

    public bool Journal
    {
        get => _journal;
        set
        {
            ThrowIfSealed();
            _journal = value;
            _journalModified = true;
        }
    }

    public bool OverridesInheritanceBehavior
    {
        get => _overridesInheritanceBehavior;
        set
        {
            ThrowIfSealed();
            _overridesInheritanceBehavior = value;
            _overridesInheritanceBehaviorModified = true;
        }
    }

    /// <summary>
    /// Gets whether data binding is allowed for this metadata and property.
    /// </summary>
    public bool IsDataBindingAllowed => !_isNotDataBindable && !_readOnly;

    private void SetFlags(FrameworkPropertyMetadataOptions flags)
    {
        if ((flags & FrameworkPropertyMetadataOptions.AffectsMeasure) != 0) AffectsMeasure = true;
        if ((flags & FrameworkPropertyMetadataOptions.AffectsArrange) != 0) AffectsArrange = true;
        if ((flags & FrameworkPropertyMetadataOptions.AffectsRender) != 0) AffectsRender = true;
        if ((flags & FrameworkPropertyMetadataOptions.AffectsParentMeasure) != 0) AffectsParentMeasure = true;
        if ((flags & FrameworkPropertyMetadataOptions.AffectsParentArrange) != 0) AffectsParentArrange = true;
        if ((flags & FrameworkPropertyMetadataOptions.AffectsCompositionOnly) != 0) AffectsCompositionOnly = true;
        if ((flags & FrameworkPropertyMetadataOptions.BindsTwoWayByDefault) != 0) BindsTwoWayByDefault = true;
        if ((flags & FrameworkPropertyMetadataOptions.NotDataBindable) != 0) IsNotDataBindable = true;
        if ((flags & FrameworkPropertyMetadataOptions.Journal) != 0) Journal = true;
        if ((flags & FrameworkPropertyMetadataOptions.SubPropertiesDoNotAffectRender) != 0) SubPropertiesDoNotAffectRender = true;
        if ((flags & FrameworkPropertyMetadataOptions.OverridesInheritanceBehavior) != 0) OverridesInheritanceBehavior = true;
        if ((flags & FrameworkPropertyMetadataOptions.Inherits) != 0) Inherits = true;
    }

    /// <inheritdoc />
    protected override void Merge(PropertyMetadata baseMetadata, DependencyProperty dp)
    {
        base.Merge(baseMetadata, dp);

        if (baseMetadata is not FrameworkPropertyMetadata frameworkBase)
            return;

        _affectsMeasure |= frameworkBase.AffectsMeasure;
        _affectsArrange |= frameworkBase.AffectsArrange;
        _affectsRender |= frameworkBase.AffectsRender;
        _affectsParentMeasure |= frameworkBase.AffectsParentMeasure;
        _affectsParentArrange |= frameworkBase.AffectsParentArrange;
        _affectsCompositionOnly |= frameworkBase.AffectsCompositionOnly;
        _bindsTwoWayByDefault |= frameworkBase.BindsTwoWayByDefault;
        _isNotDataBindable |= frameworkBase.IsNotDataBindable;

        if (!_subPropertiesDoNotAffectRenderModified)
            _subPropertiesDoNotAffectRender = frameworkBase.SubPropertiesDoNotAffectRender;

        if (!_journalModified)
            _journal = frameworkBase.Journal;

        if (!_overridesInheritanceBehaviorModified)
            _overridesInheritanceBehavior = frameworkBase.OverridesInheritanceBehavior;

        if (!_defaultUpdateSourceTriggerModified)
            _defaultUpdateSourceTrigger = frameworkBase.DefaultUpdateSourceTrigger;
    }

    /// <inheritdoc />
    protected override void OnApply(DependencyProperty dp, Type targetType)
    {
        _readOnly = dp.ReadOnly;
        base.OnApply(dp, targetType);
    }
}

/// <summary>
/// Provides metadata for a UI element dependency property.
/// </summary>
public class UIPropertyMetadata : PropertyMetadata
{
    private bool _isAnimationProhibited;

    public UIPropertyMetadata() : base() { }
    public UIPropertyMetadata(object? defaultValue) : base(defaultValue) { }
    public UIPropertyMetadata(PropertyChangedCallback? propertyChangedCallback) : base(propertyChangedCallback) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback) : base(defaultValue, propertyChangedCallback) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback) : base(defaultValue, propertyChangedCallback, coerceValueCallback) { }
    public UIPropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool isAnimationProhibited) : base(defaultValue, propertyChangedCallback, coerceValueCallback)
    {
        IsAnimationProhibited = isAnimationProhibited;
    }

    public bool IsAnimationProhibited
    {
        get => _isAnimationProhibited;
        set
        {
            ThrowIfSealed();
            _isAnimationProhibited = value;
        }
    }
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
    /// <summary>
    /// Property changes only affect how the parent composites this element
    /// (Opacity / RenderTransform / RenderTransformOrigin); the element's
    /// recorded command list is unchanged, so the framework triggers a
    /// composition-only invalidation that does not flip the retained-mode
    /// render-dirty flag. See <see cref="UIElement.InvalidateComposition"/>.
    /// </summary>
    AffectsCompositionOnly = 0x1000,
}
