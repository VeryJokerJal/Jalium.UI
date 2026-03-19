using System.Collections.Concurrent;

namespace Jalium.UI;

/// <summary>
/// Represents a dependency property that can be registered on a <see cref="DependencyObject"/>.
/// </summary>
public sealed class DependencyProperty
{
    private static readonly ConcurrentDictionary<(Type, string), DependencyProperty> _registered = new();
    private static int _globalIndex;

    /// <summary>
    /// Represents an unset value for a dependency property.
    /// This is used to indicate that a property has no value set, or has mixed values in a selection.
    /// </summary>
    public static readonly object UnsetValue = new UnsetValueType();

    /// <summary>
    /// Internal type representing an unset value.
    /// </summary>
    private sealed class UnsetValueType
    {
        public override string ToString() => "{DependencyProperty.UnsetValue}";
    }

    /// <summary>
    /// Per-type metadata for types that called <see cref="AddOwner"/> or <see cref="OverrideMetadata"/>.
    /// Enables different types sharing the same DependencyProperty to have different callbacks and defaults.
    /// </summary>
    private readonly Dictionary<Type, PropertyMetadata> _typeMetadata = new();

    /// <summary>
    /// Cache for <see cref="GetMetadata"/> lookups to avoid repeated type-hierarchy walks.
    /// </summary>
    private readonly Dictionary<Type, PropertyMetadata> _metadataCache = new();

    /// <summary>
    /// Gets the property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the property type.
    /// </summary>
    public Type PropertyType { get; }

    /// <summary>
    /// Gets the owner type that registered this property.
    /// </summary>
    public Type OwnerType { get; }

    /// <summary>
    /// Gets the default metadata for this property.
    /// </summary>
    public PropertyMetadata DefaultMetadata { get; }

    /// <summary>
    /// Gets a value indicating whether this is a read-only property.
    /// </summary>
    public bool ReadOnly { get; }

    /// <summary>
    /// Gets the global index for this property (used for fast lookup).
    /// </summary>
    public int GlobalIndex { get; }

    private DependencyProperty(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, bool readOnly)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        DefaultMetadata = metadata ?? new PropertyMetadata();
        ReadOnly = readOnly;
        GlobalIndex = Interlocked.Increment(ref _globalIndex);

        // Store the initial owner's metadata for GetMetadata lookups
        _typeMetadata[ownerType] = DefaultMetadata;
    }

    /// <summary>
    /// Registers a new dependency property.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="propertyType">The property type.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="metadata">Optional property metadata.</param>
    /// <returns>The registered dependency property.</returns>
    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);

        var key = (ownerType, name);
        var dp = new DependencyProperty(name, propertyType, ownerType, metadata, readOnly: false);

        if (!_registered.TryAdd(key, dp))
        {
            // Return the existing property if already registered (handles concurrent registration)
            return _registered[key];
        }

        return dp;
    }

    /// <summary>
    /// Registers a new read-only dependency property.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="propertyType">The property type.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="metadata">Optional property metadata.</param>
    /// <returns>The dependency property key for the read-only property.</returns>
    public static DependencyPropertyKey RegisterReadOnly(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);

        var key = (ownerType, name);
        var dp = new DependencyProperty(name, propertyType, ownerType, metadata, readOnly: true);

        if (!_registered.TryAdd(key, dp))
        {
            // Return the existing property key if already registered (handles concurrent registration)
            return new DependencyPropertyKey(_registered[key]);
        }

        return new DependencyPropertyKey(dp);
    }

    /// <summary>
    /// Registers a new attached dependency property.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="propertyType">The property type.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <param name="metadata">Optional property metadata.</param>
    /// <returns>The registered dependency property.</returns>
    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata = null)
    {
        // For now, attached properties are implemented the same as regular properties
        return Register(name, propertyType, ownerType, metadata);
    }

    /// <summary>
    /// Adds the specified type as an owner of this dependency property, optionally with type-specific metadata.
    /// This enables the WPF-style shared property pattern where multiple types (e.g. Control, TextBlock)
    /// share the same DependencyProperty instance so that property inheritance works across the visual tree.
    /// </summary>
    /// <param name="ownerType">The type to register as an additional owner.</param>
    /// <param name="typeMetadata">Optional metadata for this owner type. If null, the DefaultMetadata is used.</param>
    /// <returns>This DependencyProperty instance (for assignment to a static field).</returns>
    public DependencyProperty AddOwner(Type ownerType, PropertyMetadata? typeMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(ownerType);

        var metadata = typeMetadata ?? DefaultMetadata;
        _typeMetadata[ownerType] = metadata;

        // Register under the new owner so the global registry can find it
        _registered.TryAdd((ownerType, Name), this);

        // Invalidate the lookup cache since a new type was added
        _metadataCache.Clear();

        return this;
    }

    /// <summary>
    /// Overrides the metadata for this property when used by the specified type.
    /// </summary>
    /// <param name="forType">The type to override metadata for.</param>
    /// <param name="typeMetadata">The new metadata.</param>
    public void OverrideMetadata(Type forType, PropertyMetadata typeMetadata)
    {
        ArgumentNullException.ThrowIfNull(forType);
        ArgumentNullException.ThrowIfNull(typeMetadata);

        _typeMetadata[forType] = typeMetadata;
        _metadataCache.Clear();
    }

    /// <summary>
    /// Gets the metadata for this property as used by the specified type.
    /// Walks up the type hierarchy to find the most specific metadata, falling back to DefaultMetadata.
    /// </summary>
    /// <param name="forType">The type to look up metadata for.</param>
    /// <returns>The most specific PropertyMetadata for the given type.</returns>
    public PropertyMetadata GetMetadata(Type forType)
    {
        // Fast path: check cache
        if (_metadataCache.TryGetValue(forType, out var cached))
            return cached;

        // Walk up the type hierarchy
        var type = forType;
        while (type != null)
        {
            if (_typeMetadata.TryGetValue(type, out var metadata))
            {
                _metadataCache[forType] = metadata;
                return metadata;
            }
            type = type.BaseType;
        }

        _metadataCache[forType] = DefaultMetadata;
        return DefaultMetadata;
    }

    /// <inheritdoc />
    public override string ToString() => $"{OwnerType.Name}.{Name}";

    /// <inheritdoc />
    public override int GetHashCode() => GlobalIndex;
}

/// <summary>
/// Key for a read-only dependency property, allowing internal set access.
/// </summary>
public sealed class DependencyPropertyKey
{
    /// <summary>
    /// Gets the associated dependency property.
    /// </summary>
    public DependencyProperty DependencyProperty { get; }

    internal DependencyPropertyKey(DependencyProperty dp)
    {
        DependencyProperty = dp;
    }
}

/// <summary>
/// Metadata for a dependency property.
/// </summary>
public class PropertyMetadata
{
    /// <summary>
    /// Gets the default value for the property.
    /// </summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// Gets the callback invoked when the property value changes.
    /// </summary>
    public PropertyChangedCallback? PropertyChangedCallback { get; }

    /// <summary>
    /// Gets the callback invoked to coerce the property value.
    /// </summary>
    public CoerceValueCallback? CoerceValueCallback { get; }

    /// <summary>
    /// Gets a value indicating whether this property inherits its value from parent elements.
    /// </summary>
    public bool Inherits { get; }

    /// <summary>
    /// Gets or sets the factory used to create automatic transition animations for this property.
    /// When null, the framework falls back to the global type-based animation factory.
    /// </summary>
    public AutomaticTransitionFactoryCallback? AutomaticTransitionFactory { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyMetadata"/> class.
    /// </summary>
    public PropertyMetadata()
        : this(null, null, null, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyMetadata"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    public PropertyMetadata(object? defaultValue)
        : this(defaultValue, null, null, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyMetadata"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="propertyChangedCallback">The property changed callback.</param>
    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
        : this(defaultValue, propertyChangedCallback, null, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyMetadata"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="propertyChangedCallback">The property changed callback.</param>
    /// <param name="coerceValueCallback">The coerce value callback.</param>
    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback)
        : this(defaultValue, propertyChangedCallback, coerceValueCallback, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertyMetadata"/> class.
    /// </summary>
    /// <param name="defaultValue">The default value.</param>
    /// <param name="propertyChangedCallback">The property changed callback.</param>
    /// <param name="coerceValueCallback">The coerce value callback.</param>
    /// <param name="inherits">Whether the property value inherits from parent elements.</param>
    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback, CoerceValueCallback? coerceValueCallback, bool inherits)
    {
        DefaultValue = defaultValue;
        PropertyChangedCallback = propertyChangedCallback;
        CoerceValueCallback = coerceValueCallback;
        Inherits = inherits;
    }
}

/// <summary>
/// Creates an automatic transition animation for a dependency property.
/// </summary>
/// <param name="property">The property being transitioned.</param>
/// <param name="fromValue">The currently displayed value.</param>
/// <param name="toValue">The new target base value.</param>
/// <param name="duration">The transition duration.</param>
/// <param name="timingFunction">The framework timing preset.</param>
/// <returns>An animation timeline, or null to fall back to the default type-based transition behavior.</returns>
public delegate IAnimationTimeline? AutomaticTransitionFactoryCallback(
    DependencyProperty property,
    object? fromValue,
    object? toValue,
    TimeSpan duration,
    TransitionTimingFunction timingFunction);

/// <summary>
/// Callback for property changed notifications.
/// </summary>
/// <param name="d">The dependency object.</param>
/// <param name="e">The event arguments.</param>
public delegate void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e);

/// <summary>
/// Callback for coercing property values.
/// </summary>
/// <param name="d">The dependency object.</param>
/// <param name="baseValue">The base value to coerce.</param>
/// <returns>The coerced value.</returns>
public delegate object? CoerceValueCallback(DependencyObject d, object? baseValue);

/// <summary>
/// Event arguments for dependency property changes.
/// </summary>
public readonly struct DependencyPropertyChangedEventArgs
{
    /// <summary>
    /// Gets the property that changed.
    /// </summary>
    public DependencyProperty Property { get; }

    /// <summary>
    /// Gets the old value.
    /// </summary>
    public object? OldValue { get; }

    /// <summary>
    /// Gets the new value.
    /// </summary>
    public object? NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPropertyChangedEventArgs"/> struct.
    /// </summary>
    public DependencyPropertyChangedEventArgs(DependencyProperty property, object? oldValue, object? newValue)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }
}
