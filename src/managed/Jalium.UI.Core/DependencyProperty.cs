using System.Collections.Concurrent;

namespace Jalium.UI;

/// <summary>
/// Represents a dependency property that can be registered on a <see cref="DependencyObject"/>.
/// </summary>
public sealed class DependencyProperty
{
    private static readonly ConcurrentDictionary<(Type, string), DependencyProperty> _registered = new();
    private static readonly ConcurrentDictionary<Type, byte> _cctorPrimed = new();
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

    // Lazily-synthesized boxed default(T) for a non-nullable value-type property whose registered
    // metadata default is null (see GetEffectiveDefaultValue). A synthesized value-type default is never
    // null, so a non-null box is its own publication signal — no separate "computed" flag (which would
    // carry a store-ordering hole on weak memory models, ARM64), and a benign recompute is harmless.
    private object? _valueTypeDefaultBox;

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

    /// <summary>
    /// Gets the value-validation callback supplied at registration. When set,
    /// <see cref="DependencyObject.SetValue(DependencyProperty, object?)"/> and
    /// <see cref="DependencyObject.SetCurrentValue(DependencyProperty, object?)"/>
    /// invoke it and reject the write with <see cref="ArgumentException"/>
    /// when the callback returns <see langword="false"/>. Mirrors the WPF
    /// <c>DependencyProperty.ValidateValueCallback</c> contract — used by enum
    /// attached properties (<c>TextOptions.TextRenderingMode</c>, …) to fence
    /// out-of-range values at the API boundary instead of letting them rot
    /// in the property store.
    /// </summary>
    public ValidateValueCallback? ValidateValueCallback { get; }

    private DependencyProperty(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, bool readOnly, ValidateValueCallback? validateValueCallback)
    {
        Name = name;
        PropertyType = propertyType;
        OwnerType = ownerType;
        DefaultMetadata = metadata ?? new PropertyMetadata();
        ReadOnly = readOnly;
        ValidateValueCallback = validateValueCallback;
        GlobalIndex = Interlocked.Increment(ref _globalIndex);

        // Store the initial owner's metadata for GetMetadata lookups
        _typeMetadata[ownerType] = DefaultMetadata;
    }

    /// <summary>
    /// Runs the registered <see cref="ValidateValueCallback"/> against <paramref name="value"/>.
    /// Returns <see langword="true"/> when no callback is registered.
    /// </summary>
    /// <remarks>
    /// NOTE — divergence from WPF: WPF's <c>IsValidValue</c> is the conjunction
    /// <c>IsValidType(value) &amp;&amp; (ValidateValueCallback == null || ValidateValueCallback(value))</c>.
    /// This implementation intentionally runs ONLY the callback and does NOT perform the
    /// <see cref="IsValidType"/> type-assignability check, so a type-incompatible value (including a
    /// <see langword="null"/> for a non-nullable value type) passes this gate. Null/mismatch safety for
    /// value-type properties is instead enforced at the write paths (TemplateBinding/Style transfers, the
    /// <c>SetLayerValueCore</c> backstop, <c>SetCurrentValue</c>, local promotion) and by value-type
    /// default synthesis at registration. Folding <see cref="IsValidType"/> into this method (so
    /// <c>SetValue</c> throws on an illegal value, full WPF parity) is a deliberate larger follow-up that
    /// would change framework-wide throw semantics and needs separate regression vetting.
    /// </remarks>
    public bool IsValidValue(object? value)
    {
        return ValidateValueCallback is null || ValidateValueCallback(value);
    }

    /// <summary>
    /// Determines whether <paramref name="value"/> is type-compatible with this dependency
    /// property — i.e. whether it could be stored as the property's value without later throwing
    /// when the generated CLR accessor casts it back to <see cref="PropertyType"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors WPF's public <c>DependencyProperty.IsValidType</c>. A <see langword="null"/> is
    /// acceptable only for reference types and <see cref="Nullable{T}"/>; a non-nullable value type
    /// (<c>Thickness</c>, <c>double</c>, <c>bool</c>, an enum, <c>CornerRadius</c>, …) cannot hold
    /// <see langword="null"/> and would throw <see cref="NullReferenceException"/> on unbox. A
    /// non-null value must be assignable to <see cref="PropertyType"/>. This is the type-assignability
    /// gate that <see cref="IsValidValue"/> deliberately does not perform — the latter only runs the
    /// registered <see cref="ValidateValueCallback"/>.
    /// </remarks>
    public bool IsValidType(object? value)
    {
        if (value is null)
        {
            return !PropertyType.IsValueType || Nullable.GetUnderlyingType(PropertyType) is not null;
        }

        // A non-null value must be an instance of the property type. For a Nullable<T> property a
        // boxed T surfaces with runtime type T (boxed nullables don't exist), so compare against the
        // underlying T rather than Nullable<T> — otherwise a legal value would be rejected.
        var targetType = Nullable.GetUnderlyingType(PropertyType) ?? PropertyType;
        return targetType.IsInstanceOfType(value);
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
        return Register(name, propertyType, ownerType, metadata, validateValueCallback: null);
    }

    /// <summary>
    /// Registers a new dependency property with a value-validation callback.
    /// Mirrors the WPF <c>DependencyProperty.Register(name, propertyType, ownerType, typeMetadata, validateValueCallback)</c> overload.
    /// </summary>
    public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, ValidateValueCallback? validateValueCallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);

        ValidateDefaultValue(metadata, propertyType, ownerType, name, validateValueCallback);

        var key = (ownerType, name);
        var dp = new DependencyProperty(name, propertyType, ownerType, metadata, readOnly: false, validateValueCallback);

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
        return RegisterReadOnly(name, propertyType, ownerType, metadata, validateValueCallback: null);
    }

    /// <summary>
    /// Registers a new read-only dependency property with a value-validation callback.
    /// </summary>
    public static DependencyPropertyKey RegisterReadOnly(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, ValidateValueCallback? validateValueCallback)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(ownerType);

        ValidateDefaultValue(metadata, propertyType, ownerType, name, validateValueCallback);

        var key = (ownerType, name);
        var dp = new DependencyProperty(name, propertyType, ownerType, metadata, readOnly: true, validateValueCallback);

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
        return Register(name, propertyType, ownerType, metadata, validateValueCallback: null);
    }

    /// <summary>
    /// Registers a new attached dependency property with a value-validation callback.
    /// Mirrors the WPF <c>DependencyProperty.RegisterAttached(name, propertyType, ownerType, defaultMetadata, validateValueCallback)</c> overload.
    /// </summary>
    public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata? metadata, ValidateValueCallback? validateValueCallback)
    {
        return Register(name, propertyType, ownerType, metadata, validateValueCallback);
    }

    private static void ValidateDefaultValue(PropertyMetadata? metadata, Type propertyType, Type ownerType, string name, ValidateValueCallback? validateValueCallback)
    {
        if (validateValueCallback is null || metadata is null)
            return;

        // WPF rejects registrations whose default value already fails the validator —
        // catch the inconsistency at registration time rather than letting every read
        // of the unset value silently return an illegal default.
        var defaultValue = metadata.DefaultValue;
        if (defaultValue is null && propertyType.IsValueType)
            return;

        if (!validateValueCallback(defaultValue))
        {
            throw new ArgumentException(
                $"Default value of dependency property '{ownerType.Name}.{name}' is not valid according to its ValidateValueCallback.");
        }
    }

    /// <summary>
    /// AOT-safe lookup: walks the type hierarchy of <paramref name="ownerType"/> and returns the
    /// first registered <see cref="DependencyProperty"/> with the given <paramref name="name"/>.
    /// Avoids reflection over <c>NameProperty</c> static fields. Returns <c>null</c> if none is found.
    /// </summary>
    /// <param name="ownerType">Owner type to start the search from. Walks up the inheritance chain.</param>
    /// <param name="name">Property name (without the trailing "Property" suffix).</param>
    /// <remarks>
    /// In NativeAOT / PublishTrimmed builds a type's static field initializers — which are how
    /// every framework <c>FooProperty = DependencyProperty.Register(...)</c> populates the
    /// registry — only run on first static access of that type. Pure XAML-driven workloads
    /// (StartupUri Window, framework Themes loaded by name, &lt;Style TargetType="Button"&gt; in
    /// a ResourceDictionary) reach a type only as a string-resolved <c>System.Type</c> handle,
    /// which does NOT trigger the cctor. The registry is therefore empty for that type and
    /// every Setter / Trigger / Binding lookup returns null, leaving the visual tree unstyled.
    /// On a cache miss we walk the inheritance chain once per type and force the cctor via
    /// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor"/>, then
    /// retry. JIT builds are unaffected — RunClassConstructor is a no-op when the cctor has
    /// already run, and the priming flag short-circuits subsequent calls.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2059:RunClassConstructor",
        Justification = "This is the priming site for the AOT registry described in this method's doc comment: a string-resolved framework Type (e.g. a <Style TargetType=\"Button\"> resolved by name from a ResourceDictionary) reaches us as a runtime System.Type whose cctor has not run, so its FooProperty = DependencyProperty.Register(...) static fields never populated the registry. We force the cctor so the DependencyProperty fields self-register. We never construct instances of the type or call any of its members reflectively here — only its static field initializers run, and those code paths are reachable through normal (non-reflective) use of the same framework controls, so the trimmer already preserves them. There is no DAM member kind that satisfies RunClassConstructor for a runtime-supplied Type; suppressing at this leaf is the documented AOT contract for XAML-driven styling.")]
    public static DependencyProperty? FromName(Type ownerType, string name)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(name);

        for (Type? type = ownerType; type != null; type = type.BaseType)
        {
            if (_registered.TryGetValue((type, name), out var dp))
                return dp;
        }

        // Cache miss — prime the static constructors along the inheritance chain.
        var primedAny = false;
        for (Type? type = ownerType; type != null; type = type.BaseType)
        {
            if (_cctorPrimed.TryAdd(type, 0))
            {
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                primedAny = true;
            }
        }

        if (!primedAny)
            return null;

        for (Type? type = ownerType; type != null; type = type.BaseType)
        {
            if (_registered.TryGetValue((type, name), out var dp))
                return dp;
        }

        return null;
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

    /// <summary>
    /// Returns the effective default value of this property as seen by <paramref name="forType"/>,
    /// guaranteeing that a non-nullable value-type property never yields <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Mirrors WPF's <c>AutoCreateInstance</c> default-value behaviour. If a value-type property is
    /// registered with a null/absent metadata default (the 3-arg <see cref="Register(string, Type, Type)"/>
    /// overload, <c>new PropertyMetadata()</c>, or <c>PropertyMetadata(null)</c>), the registered default is
    /// <see langword="null"/> and the generated CLR getter (<c>(Thickness)GetValue(...)</c>) would unbox
    /// that null and throw at layout. Here a boxed <c>default(T)</c> is synthesized so a plain
    /// <see cref="DependencyObject.GetValue"/> with no higher-precedence value still returns a usable
    /// struct. Reference types and <see cref="Nullable{T}"/> keep their genuine null default.
    /// </remarks>
    internal object? GetEffectiveDefaultValue(Type forType)
    {
        var metadataDefault = GetMetadata(forType).DefaultValue;
        if (metadataDefault is not null)
            return metadataDefault;

        // Null metadata default: legal for reference types and Nullable<T>; for a non-nullable value
        // type, synthesize default(T) once and reuse the boxed instance.
        if (!PropertyType.IsValueType || Nullable.GetUnderlyingType(PropertyType) is not null)
            return null;

        // A synthesized value-type default is never null, so the box doubles as its own "computed"
        // signal: gating solely on (box is null) is race-free on weak memory models (no separate flag
        // that could be observed set before the box store is visible) and cannot loop forever.
        var box = _valueTypeDefaultBox;
        if (box is null)
        {
            box = SynthesizeValueTypeDefault(PropertyType);
            _valueTypeDefaultBox = box;
        }

        return box;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2067:RequiresUnreferencedCode",
        Justification = "Reached only for a non-nullable value type whose registered default is null. A value type always has an intrinsic parameterless (default) constructor performing zero-initialization that the trimmer never removes, so Activator.CreateInstance's PublicParameterlessConstructor requirement is structurally satisfied even though the value-type Type flowing in here does not carry the DAM annotation. Mirrors the established GetDefaultAnimationValue pattern (UIElement.cs). The AOT-safe DefaultValueTypeBox table handles every primitive/enum/framework struct first; the Activator fallback runs only for an exotic user-defined struct DP registered with a null default.")]
    private static object SynthesizeValueTypeDefault(Type valueType)
        => BindingValueCoercion.DefaultValueTypeBox(valueType) ?? System.Activator.CreateInstance(valueType)!;

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
    /// <remarks>
    /// The setter is intentionally <c>internal</c>: only <see cref="FrameworkPropertyMetadata"/>
    /// (which translates <see cref="FrameworkPropertyMetadataOptions.Inherits"/> into this flag)
    /// and the <see cref="PropertyMetadata"/> constructor that accepts an <c>inherits</c>
    /// parameter are allowed to write it. Public callers must go through
    /// <see cref="FrameworkPropertyMetadata"/> or the 4-arg <see cref="PropertyMetadata"/>
    /// constructor so the value is locked at construction time.
    /// </remarks>
    public bool Inherits { get; internal set; }

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
