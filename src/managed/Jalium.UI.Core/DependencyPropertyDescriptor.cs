using System.ComponentModel;

namespace Jalium.UI;

/// <summary>
/// Provides an extension of <see cref="PropertyDescriptor"/> that provides additional
/// information about a <see cref="DependencyProperty"/>.
/// </summary>
public sealed class DependencyPropertyDescriptor : PropertyDescriptor
{
    private readonly DependencyProperty _dp;
    private readonly Type _ownerType;

    private DependencyPropertyDescriptor(DependencyProperty dp, Type ownerType)
        : base(dp.Name, Array.Empty<Attribute>())
    {
        _dp = dp;
        _ownerType = ownerType;
    }

    /// <summary>
    /// Gets the dependency property this descriptor wraps.
    /// </summary>
    public DependencyProperty DependencyProperty => _dp;

    /// <summary>
    /// Gets a value indicating whether this property is an attached property.
    /// </summary>
    public bool IsAttached => _dp.OwnerType != _ownerType;

    /// <summary>
    /// Gets the metadata for this property on the specified type.
    /// </summary>
    public PropertyMetadata? Metadata => _dp.DefaultMetadata;

    /// <inheritdoc />
    public override Type ComponentType => _ownerType;

    /// <inheritdoc />
    public override bool IsReadOnly => false;

    /// <inheritdoc />
    public override Type PropertyType => _dp.PropertyType;

    /// <summary>
    /// Returns a <see cref="DependencyPropertyDescriptor"/> for a given dependency property
    /// and target type.
    /// </summary>
    public static DependencyPropertyDescriptor? FromProperty(DependencyProperty dp, Type targetType)
    {
        if (dp == null) return null;
        return new DependencyPropertyDescriptor(dp, targetType);
    }

    /// <summary>
    /// Returns a <see cref="DependencyPropertyDescriptor"/> for a given property descriptor.
    /// </summary>
    public static DependencyPropertyDescriptor? FromProperty(PropertyDescriptor property)
    {
        if (property is DependencyPropertyDescriptor dpd) return dpd;
        return null;
    }

    /// <summary>
    /// Returns a <see cref="DependencyPropertyDescriptor"/> by name and owner type.
    /// </summary>
    public static DependencyPropertyDescriptor? FromName(string name, Type ownerType, Type targetType)
    {
        // Search for the DP on the owner type by looking for a static field
        var field = ownerType.GetField(name + "Property",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.FlattenHierarchy);
        if (field?.GetValue(null) is DependencyProperty dp)
            return new DependencyPropertyDescriptor(dp, targetType);
        return null;
    }

    /// <summary>
    /// Adds a value changed handler for the specified component.
    /// </summary>
    public new void AddValueChanged(object component, EventHandler handler)
    {
        if (component is DependencyObject dobj)
        {
            // Store the handler association for later removal
            _valueChangedHandlers[new WeakReference(dobj)] = handler;
        }
    }

    /// <summary>
    /// Removes a value changed handler for the specified component.
    /// </summary>
    public new void RemoveValueChanged(object component, EventHandler handler)
    {
        // Clean up handler
        var keysToRemove = _valueChangedHandlers
            .Where(kvp => kvp.Key.Target == component)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in keysToRemove)
            _valueChangedHandlers.Remove(key);
    }

    private readonly Dictionary<WeakReference, EventHandler> _valueChangedHandlers = new();

    /// <inheritdoc />
    public override bool CanResetValue(object component) => true;

    /// <inheritdoc />
    public override object? GetValue(object? component) =>
        (component as DependencyObject)?.GetValue(_dp);

    /// <inheritdoc />
    public override void ResetValue(object component)
    {
        if (component is DependencyObject dobj)
            dobj.ClearValue(_dp);
    }

    /// <inheritdoc />
    public override void SetValue(object? component, object? value)
    {
        if (component is DependencyObject dobj)
            dobj.SetValue(_dp, value);
    }

    /// <inheritdoc />
    public override bool ShouldSerializeValue(object component) => true;
}

/// <summary>
/// Specifies the serialization flags for a property.
/// </summary>
public enum DesignerSerializationOptions
{
    /// <summary>The property should be serialized as an attribute.</summary>
    SerializeAsAttribute
}

/// <summary>
/// Specifies the serialization options for a property.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public sealed class DesignerSerializationOptionsAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DesignerSerializationOptionsAttribute"/> class.
    /// </summary>
    public DesignerSerializationOptionsAttribute(DesignerSerializationOptions designerSerializationOptions)
    {
        DesignerSerializationOptions = designerSerializationOptions;
    }

    /// <summary>
    /// Gets the serialization options.
    /// </summary>
    public DesignerSerializationOptions DesignerSerializationOptions { get; }
}

/// <summary>
/// Provides a callback used during the freeze operation of a <see cref="Freezable"/>.
/// </summary>
public delegate bool FreezeValueCallback(DependencyObject d, DependencyProperty dp);
