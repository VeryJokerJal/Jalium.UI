using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Jalium.UI;

namespace System.ComponentModel;

/// <summary>
/// Describes a <see cref="Jalium.UI.DependencyProperty"/> through the component-model API.
/// </summary>
public sealed class DependencyPropertyDescriptor : PropertyDescriptor
{
    private static readonly ConcurrentDictionary<(DependencyProperty Property, Type TargetType), DependencyPropertyDescriptor>
        Cache = new();
    private static readonly ConditionalWeakTable<DependencyProperty, DesignerCoercionState> DesignerCoercions = new();

    private readonly DependencyProperty _dependencyProperty;
    private readonly Type _componentType;
    private readonly bool _isAttached;
    private readonly PropertyMetadata _metadata;
    private readonly ConditionalWeakTable<DependencyObject, ValueChangedSubscription> _valueChangedSubscriptions = new();
    private PropertyDescriptor? _property;

    private DependencyPropertyDescriptor(
        DependencyProperty dependencyProperty,
        Type componentType,
        bool isAttached,
        PropertyDescriptor? property = null)
        : base(dependencyProperty.Name, null)
    {
        _dependencyProperty = dependencyProperty;
        _componentType = componentType;
        _isAttached = isAttached;
        _metadata = dependencyProperty.GetMetadata(componentType);
        _property = property;
    }

    public DependencyProperty DependencyProperty => _dependencyProperty;

    public bool IsAttached => _isAttached;

    public PropertyMetadata Metadata => _metadata;

    public override Type ComponentType => _componentType;

    public override bool IsReadOnly => _dependencyProperty.ReadOnly || Property.IsReadOnly;

    public override Type PropertyType => _dependencyProperty.PropertyType;

    public override AttributeCollection Attributes => Property.Attributes;

    public override string Category => Property.Category;

    public override string Description => Property.Description;

    public override bool DesignTimeOnly => Property.DesignTimeOnly;

    public override string DisplayName => Property.DisplayName;

    public override TypeConverter Converter
    {
        [RequiresUnreferencedCode("PropertyDescriptor converters rely on component-model reflection.")]
        get => Property.Converter;
    }

    public override bool IsBrowsable => Property.IsBrowsable;

    public override bool IsLocalizable => Property.IsLocalizable;

    public override bool SupportsChangeEvents => true;

    public CoerceValueCallback? DesignerCoerceValueCallback
    {
        get => DesignerCoercions.TryGetValue(_dependencyProperty, out DesignerCoercionState? state)
            ? state.Callback
            : null;
        set
        {
            if (value is null)
            {
                DesignerCoercions.Remove(_dependencyProperty);
            }
            else
            {
                DesignerCoercions.GetValue(
                    _dependencyProperty,
                    static _ => new DesignerCoercionState()).Callback = value;
            }
        }
    }

    private PropertyDescriptor Property
    {
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "DependencyPropertyDescriptor is an explicitly reflection-based component-model API.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2077",
            Justification = "The runtime component type is intentionally inspected by the component-model API.")]
        get => _property ??=
            TypeDescriptor.GetProperties(_componentType)[Name] ??
            TypeDescriptor.CreateProperty(_componentType, Name, _dependencyProperty.PropertyType);
    }

    public static DependencyPropertyDescriptor? FromProperty(PropertyDescriptor property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property is DependencyPropertyDescriptor descriptor)
        {
            return descriptor;
        }

        Type? componentType = property.ComponentType;
        if (componentType is null)
        {
            return null;
        }

        DependencyProperty? dependencyProperty = DependencyProperty.FromName(componentType, property.Name);
        if (dependencyProperty is null)
        {
            return null;
        }

        return Cache.GetOrAdd(
            (dependencyProperty, componentType),
            static (key, sourceProperty) => new DependencyPropertyDescriptor(
                key.Property,
                key.TargetType,
                !key.Property.OwnerType.IsAssignableFrom(key.TargetType),
                sourceProperty),
            property);
    }

    public static DependencyPropertyDescriptor FromProperty(
        DependencyProperty dependencyProperty,
        Type targetType)
    {
        ArgumentNullException.ThrowIfNull(dependencyProperty);
        ArgumentNullException.ThrowIfNull(targetType);

        return Cache.GetOrAdd(
            (dependencyProperty, targetType),
            static key => new DependencyPropertyDescriptor(
                key.Property,
                key.TargetType,
                !key.Property.OwnerType.IsAssignableFrom(key.TargetType)));
    }

    public static DependencyPropertyDescriptor? FromName(
        string name,
        Type ownerType,
        Type targetType) =>
        FromName(name, ownerType, targetType, ignorePropertyType: false);

    public static DependencyPropertyDescriptor? FromName(
        string name,
        Type ownerType,
        Type targetType,
        bool ignorePropertyType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(ownerType);
        ArgumentNullException.ThrowIfNull(targetType);

        DependencyProperty? dependencyProperty = DependencyProperty.FromName(ownerType, name);
        return dependencyProperty is null ? null : FromProperty(dependencyProperty, targetType);
    }

    public override bool Equals(object? obj) =>
        obj is DependencyPropertyDescriptor other &&
        ReferenceEquals(_dependencyProperty, other._dependencyProperty) &&
        _componentType == other._componentType;

    public override int GetHashCode() => HashCode.Combine(_dependencyProperty, _componentType);

    public override string ToString() => Name;

    public override bool CanResetValue(object component) =>
        component is DependencyObject dependencyObject && dependencyObject.HasLocalValue(_dependencyProperty);

    public override object? GetValue(object? component) =>
        (component as DependencyObject)?.GetValue(_dependencyProperty);

    public override void ResetValue(object component)
    {
        if (component is DependencyObject dependencyObject)
        {
            dependencyObject.ClearValue(_dependencyProperty);
        }
    }

    public override void SetValue(object? component, object? value)
    {
        if (component is not DependencyObject dependencyObject)
        {
            throw new ArgumentException("The component must be a DependencyObject.", nameof(component));
        }

        dependencyObject.SetValue(_dependencyProperty, value);
    }

    public override bool ShouldSerializeValue(object component) =>
        component is DependencyObject dependencyObject && dependencyObject.HasLocalValue(_dependencyProperty);

    public override void AddValueChanged(object component, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(handler);
        if (component is not DependencyObject dependencyObject)
        {
            throw new ArgumentException("The component must be a DependencyObject.", nameof(component));
        }

        ValueChangedSubscription subscription = _valueChangedSubscriptions.GetValue(
            dependencyObject,
            owner => new ValueChangedSubscription(owner, _dependencyProperty));
        subscription.Add(handler);
    }

    public override void RemoveValueChanged(object component, EventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(handler);
        if (component is not DependencyObject dependencyObject ||
            !_valueChangedSubscriptions.TryGetValue(dependencyObject, out ValueChangedSubscription? subscription))
        {
            return;
        }

        subscription.Remove(handler);
        if (subscription.IsEmpty)
        {
            subscription.Detach(dependencyObject);
            _valueChangedSubscriptions.Remove(dependencyObject);
        }
    }

    [RequiresUnreferencedCode("Child property discovery relies on component-model reflection.")]
    public override PropertyDescriptorCollection GetChildProperties(object? instance, Attribute[]? filter) =>
        Property.GetChildProperties(instance, filter);

    [RequiresUnreferencedCode("Editor discovery relies on component-model reflection.")]
    public override object? GetEditor(Type editorBaseType) => Property.GetEditor(editorBaseType);

    private sealed class DesignerCoercionState
    {
        public CoerceValueCallback? Callback { get; set; }
    }

    private sealed class ValueChangedSubscription
    {
        private readonly DependencyProperty _property;
        private readonly WeakReference<DependencyObject> _owner;
        private readonly List<EventHandler> _handlers = new();

        public ValueChangedSubscription(DependencyObject owner, DependencyProperty property)
        {
            _property = property;
            _owner = new WeakReference<DependencyObject>(owner);
            owner.PropertyChangedInternal += OnPropertyChanged;
        }

        public bool IsEmpty
        {
            get
            {
                lock (_handlers)
                {
                    return _handlers.Count == 0;
                }
            }
        }

        public void Add(EventHandler handler)
        {
            lock (_handlers)
            {
                _handlers.Add(handler);
            }
        }

        public void Remove(EventHandler handler)
        {
            lock (_handlers)
            {
                _handlers.Remove(handler);
            }
        }

        public void Detach(DependencyObject owner) => owner.PropertyChangedInternal -= OnPropertyChanged;

        private void OnPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
        {
            if (!ReferenceEquals(property, _property))
            {
                return;
            }

            EventHandler[] handlers;
            lock (_handlers)
            {
                handlers = _handlers.ToArray();
            }

            foreach (EventHandler handler in handlers)
            {
                if (_owner.TryGetTarget(out DependencyObject? owner))
                {
                    handler(owner, EventArgs.Empty);
                }
            }
        }
    }
}
