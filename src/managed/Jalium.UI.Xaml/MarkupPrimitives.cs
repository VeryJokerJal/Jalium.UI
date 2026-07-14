using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ComponentModelTypeConverter = System.ComponentModel.TypeConverter;

namespace Jalium.UI.Markup.Primitives;

public abstract class MarkupObject
{
    internal MarkupObject() { }
    public abstract AttributeCollection Attributes { get; }
    public abstract object Instance { get; }
    public abstract Type ObjectType { get; }
    public virtual IEnumerable<MarkupProperty> Properties => [];
    public abstract void AssignRootContext(IValueSerializerContext context);
}

public abstract class MarkupProperty
{
    internal MarkupProperty() { }
    public abstract AttributeCollection Attributes { get; }
    public virtual DependencyProperty? DependencyProperty => null;
    public virtual bool IsAttached => false;
    public virtual bool IsComposite => false;
    public virtual bool IsConstructorArgument => false;
    public virtual bool IsContent => false;
    public virtual bool IsKey => false;
    public virtual bool IsValueAsString => false;
    public abstract IEnumerable<MarkupObject> Items { get; }
    public abstract string Name { get; }
    public virtual PropertyDescriptor? PropertyDescriptor => null;
    public abstract Type PropertyType { get; }
    public abstract string StringValue { get; }
    public abstract IEnumerable<Type> TypeReferences { get; }
    public abstract object? Value { get; }
}

public sealed class MarkupWriter : IDisposable
{
    internal MarkupWriter() { }
    public void Dispose() { }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "MarkupWriter is an explicitly reflection-based component-model API.")]
    public static MarkupObject GetMarkupObjectFor(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new ReflectionMarkupObject(instance, new MarkupValueSerializerContext(instance, null));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "MarkupWriter is an explicitly reflection-based component-model API.")]
    public static MarkupObject GetMarkupObjectFor(object instance, XamlDesignerSerializationManager manager)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(manager);
        return new ReflectionMarkupObject(instance, new MarkupValueSerializerContext(instance, manager));
    }
}

[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Markup primitives intentionally use component-model reflection.")]
internal sealed class ReflectionMarkupObject : MarkupObject
{
    private IValueSerializerContext _context;

    public ReflectionMarkupObject(object instance, IValueSerializerContext context)
    {
        Instance = instance;
        _context = context;
    }

    public override AttributeCollection Attributes => TypeDescriptor.GetAttributes(Instance);
    public override object Instance { get; }
    public override Type ObjectType => Instance.GetType();

    public override IEnumerable<MarkupProperty> Properties
    {
        get
        {
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(Instance))
            {
                if (!descriptor.IsBrowsable || descriptor.Attributes[typeof(DesignerSerializationVisibilityAttribute)] is DesignerSerializationVisibilityAttribute visibility
                    && visibility.Visibility == DesignerSerializationVisibility.Hidden)
                    continue;

                object? value;
                try { value = descriptor.GetValue(Instance); }
                catch { continue; }
                if (value is null) continue;
                if (!descriptor.ShouldSerializeValue(Instance) && IsDefaultValue(descriptor, value)) continue;
                yield return new ReflectionMarkupProperty(this, descriptor, value, _context);
            }
        }
    }

    public override void AssignRootContext(IValueSerializerContext context)
        => _context = context ?? throw new ArgumentNullException(nameof(context));

    private static bool IsDefaultValue(PropertyDescriptor descriptor, object value)
        => descriptor.Attributes[typeof(DefaultValueAttribute)] is DefaultValueAttribute attribute && Equals(attribute.Value, value);
}

internal sealed class ReflectionMarkupProperty : MarkupProperty
{
    private readonly ReflectionMarkupObject _owner;
    private readonly PropertyDescriptor _descriptor;
    private readonly object _value;
    private readonly IValueSerializerContext _context;
    private readonly ValueSerializer? _serializer;

    public ReflectionMarkupProperty(ReflectionMarkupObject owner, PropertyDescriptor descriptor, object value, IValueSerializerContext context)
    {
        _owner = owner;
        _descriptor = descriptor;
        _value = value;
        _context = context;
        _serializer = context.GetValueSerializerFor(descriptor) ?? ValueSerializer.GetSerializerFor(descriptor, context);
    }

    public override AttributeCollection Attributes => _descriptor.Attributes;
    public override DependencyProperty? DependencyProperty
        => System.ComponentModel.DependencyPropertyDescriptor.FromProperty(_descriptor)?.DependencyProperty
            ?? Jalium.UI.DependencyProperty.FromName(_descriptor.ComponentType, _descriptor.Name);
    public override bool IsAttached => DependencyProperty is { } property && property.OwnerType != _owner.ObjectType;
    public override bool IsConstructorArgument => _descriptor.Attributes[typeof(ConstructorArgumentAttribute)] is ConstructorArgumentAttribute;
    public override bool IsContent
    {
        get
        {
            string? contentProperty = (_owner.Attributes[typeof(ContentPropertyAttribute)] as ContentPropertyAttribute)?.Name;
            return string.Equals(_descriptor.Name, contentProperty, StringComparison.Ordinal);
        }
    }
    public override bool IsKey
        => (_owner.Attributes[typeof(DictionaryKeyPropertyAttribute)] as DictionaryKeyPropertyAttribute)?.Name == _descriptor.Name;
    public override bool IsValueAsString => _serializer?.CanConvertToString(_value, _context) == true;
    public override bool IsComposite => !IsValueAsString && _value is not string && !_value.GetType().IsPrimitive && !_value.GetType().IsEnum;

    public override IEnumerable<MarkupObject> Items
    {
        get
        {
            if (_value is string || IsValueAsString) yield break;
            if (_value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                    if (item is not null) yield return new ReflectionMarkupObject(item, _context);
                yield break;
            }
            yield return new ReflectionMarkupObject(_value, _context);
        }
    }

    public override string Name => _descriptor.Name;
    public override PropertyDescriptor PropertyDescriptor => _descriptor;
    public override Type PropertyType => _descriptor.PropertyType;
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Markup serialization intentionally uses the component-model converter declared for the reflected property.")]
    public override string StringValue
    {
        get
        {
            if (_serializer?.CanConvertToString(_value, _context) == true) return _serializer.ConvertToString(_value, _context);
            ComponentModelTypeConverter converter = _descriptor.Converter;
            if (converter.CanConvertTo(_context, typeof(string)))
                return converter.ConvertTo(_context, CultureInfo.InvariantCulture, _value, typeof(string)) as string ?? string.Empty;
            return Convert.ToString(_value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
    public override IEnumerable<Type> TypeReferences => _serializer?.TypeReferences(_value, _context) ?? [];
    public override object Value => _value;
}

internal sealed class MarkupValueSerializerContext : IValueSerializerContext, Jalium.UI.Xaml.IRootObjectProvider
{
    private readonly object _root;
    private readonly XamlDesignerSerializationManager? _manager;
    private PropertyDescriptor? _propertyDescriptor;

    public MarkupValueSerializerContext(object root, XamlDesignerSerializationManager? manager)
    {
        _root = root;
        _manager = manager;
    }

    public IContainer? Container => null;
    public object Instance => _root;
    public PropertyDescriptor? PropertyDescriptor => _propertyDescriptor;
    public object RootObject => _root;
    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IValueSerializerContext) || serviceType == typeof(ITypeDescriptorContext)) return this;
        if (serviceType == typeof(Jalium.UI.Xaml.IRootObjectProvider)) return this;
        return _manager?.GetService(serviceType);
    }
    public ValueSerializer? GetValueSerializerFor(PropertyDescriptor descriptor)
    {
        _propertyDescriptor = descriptor;
        return ValueSerializer.GetSerializerFor(descriptor.PropertyType);
    }
    public ValueSerializer? GetValueSerializerFor(Type type) => ValueSerializer.GetSerializerFor(type);
    public bool OnComponentChanging() => true;
    public void OnComponentChanged() { }
}
