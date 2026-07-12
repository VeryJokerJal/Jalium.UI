using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Jalium.UI.Markup;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class AcceptedMarkupExtensionExpressionTypeAttribute : Attribute
{
    public AcceptedMarkupExtensionExpressionTypeAttribute(Type type) => Type = type ?? throw new ArgumentNullException(nameof(type));
    public Type Type { get; }
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ConstructorArgumentAttribute : Attribute
{
    public ConstructorArgumentAttribute(string argumentName) => ArgumentName = argumentName ?? throw new ArgumentNullException(nameof(argumentName));
    public string ArgumentName { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ContentPropertyAttribute : Attribute
{
    public ContentPropertyAttribute() { }
    public ContentPropertyAttribute(string? name) => Name = name;
    public string? Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ContentWrapperAttribute : Attribute
{
    public ContentWrapperAttribute(Type contentWrapper) => ContentWrapper = contentWrapper ?? throw new ArgumentNullException(nameof(contentWrapper));
    public Type ContentWrapper { get; }
    public override object TypeId => ContentWrapper;
    public override bool Equals(object? obj) => obj is ContentWrapperAttribute other && other.ContentWrapper == ContentWrapper;
    public override int GetHashCode() => ContentWrapper.GetHashCode();
}

[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class DependsOnAttribute : Attribute
{
    public DependsOnAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));
    public string Name { get; }
    public override object TypeId => this;
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class MarkupExtensionBracketCharactersAttribute : Attribute
{
    public MarkupExtensionBracketCharactersAttribute(char openingBracket, char closingBracket)
    {
        OpeningBracket = openingBracket;
        ClosingBracket = closingBracket;
    }
    public char OpeningBracket { get; }
    public char ClosingBracket { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NameScopePropertyAttribute : Attribute
{
    public NameScopePropertyAttribute(string name) : this(name, null) { }
    public NameScopePropertyAttribute(string name, Type? type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type;
    }
    public string Name { get; }
    public Type? Type { get; }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class RootNamespaceAttribute : Attribute
{
    public RootNamespaceAttribute(string nameSpace) => Namespace = nameSpace ?? throw new ArgumentNullException(nameof(nameSpace));
    public string Namespace { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RuntimeNamePropertyAttribute : Attribute
{
    public RuntimeNamePropertyAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));
    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class TrimSurroundingWhitespaceAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UidPropertyAttribute : Attribute
{
    public UidPropertyAttribute(string name) => Name = name ?? throw new ArgumentNullException(nameof(name));
    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UsableDuringInitializationAttribute : Attribute
{
    public UsableDuringInitializationAttribute(bool usable) => Usable = usable;
    public bool Usable { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class WhitespaceSignificantCollectionAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class XamlDeferLoadAttribute : Attribute
{
    public XamlDeferLoadAttribute(string loaderType, string contentType)
    {
        LoaderTypeName = loaderType ?? throw new ArgumentNullException(nameof(loaderType));
        ContentTypeName = contentType ?? throw new ArgumentNullException(nameof(contentType));
    }

    public XamlDeferLoadAttribute(Type loaderType, Type contentType)
    {
        LoaderType = loaderType ?? throw new ArgumentNullException(nameof(loaderType));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        LoaderTypeName = loaderType.AssemblyQualifiedName;
        ContentTypeName = contentType.AssemblyQualifiedName;
    }

    public Type? ContentType { get; }
    public string? ContentTypeName { get; }
    public Type? LoaderType { get; }
    public string? LoaderTypeName { get; }
}

public enum DesignerSerializationOptions
{
    SerializeAsAttribute = 1,
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class DesignerSerializationOptionsAttribute : Attribute
{
    public DesignerSerializationOptionsAttribute(DesignerSerializationOptions designerSerializationOptions)
        => DesignerSerializationOptions = designerSerializationOptions;
    public DesignerSerializationOptions DesignerSerializationOptions { get; }
}

public interface IXamlTypeResolver
{
    Type Resolve(string qualifiedTypeName);
}

public interface INameScopeDictionary : ICollection<KeyValuePair<string, object>>, IDictionary<string, object>, INameScope
{
}

[Obsolete("IReceiveMarkupExtension has been deprecated. This interface is no longer in use.")]
public interface IReceiveMarkupExtension
{
    void ReceiveMarkupExtension(string property, MarkupExtension markupExtension, IServiceProvider serviceProvider);
}

[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class InternalTypeHelper
{
    protected InternalTypeHelper() { }
    protected internal abstract void AddEventHandler(EventInfo eventInfo, object target, Delegate handler);
    protected internal abstract Delegate CreateDelegate(Type delegateType, object target, string handler);
    protected internal abstract object CreateInstance(Type type, CultureInfo culture);
    protected internal abstract object? GetPropertyValue(PropertyInfo propertyInfo, object target, CultureInfo culture);
    protected internal abstract void SetPropertyValue(PropertyInfo propertyInfo, object target, object? value, CultureInfo culture);
}

public interface IValueSerializerContext : ITypeDescriptorContext, IServiceProvider
{
    ValueSerializer? GetValueSerializerFor(PropertyDescriptor descriptor);
    ValueSerializer? GetValueSerializerFor(Type type);
}

public abstract class ValueSerializer
{
    protected ValueSerializer() { }

    public virtual bool CanConvertFromString(string value, IValueSerializerContext? context) => false;
    public virtual bool CanConvertToString(object value, IValueSerializerContext? context) => false;
    public virtual object ConvertFromString(string value, IValueSerializerContext? context) => throw GetConvertFromException(value);
    public virtual string ConvertToString(object value, IValueSerializerContext? context) => throw GetConvertToException(value, typeof(string));
    public virtual IEnumerable<Type> TypeReferences(object value, IValueSerializerContext? context) => [];

    protected Exception GetConvertFromException(object? value)
        => new NotSupportedException($"Cannot convert '{value ?? "(null)"}' from a string representation.");

    protected Exception GetConvertToException(object? value, Type destinationType)
        => new NotSupportedException($"Cannot convert '{value ?? "(null)"}' to '{destinationType?.FullName ?? "(null)"}'.");

    public static ValueSerializer? GetSerializerFor(PropertyDescriptor descriptor)
        => GetSerializerFor(descriptor, null);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ValueSerializer is an explicitly reflection-based component-model API.")]
    public static ValueSerializer? GetSerializerFor(PropertyDescriptor descriptor, IValueSerializerContext? context)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ValueSerializer? contextual = context?.GetValueSerializerFor(descriptor);
        if (contextual is not null) return contextual;
        TypeConverter converter = descriptor.Converter;
        return CanRoundTripString(converter) ? new TypeConverterValueSerializer(converter) : GetSerializerFor(descriptor.PropertyType, context);
    }

    public static ValueSerializer? GetSerializerFor(Type type) => GetSerializerFor(type, null);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ValueSerializer is an explicitly reflection-based component-model API.")]
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "ValueSerializer intentionally accepts arbitrary runtime types for component-model conversion.")]
    public static ValueSerializer? GetSerializerFor(Type type, IValueSerializerContext? context)
    {
        ArgumentNullException.ThrowIfNull(type);
        ValueSerializer? contextual = context?.GetValueSerializerFor(type);
        if (contextual is not null) return contextual;
        if (type == typeof(DateTime)) return new DateTimeValueSerializer();
        TypeConverter converter = TypeDescriptor.GetConverter(type);
        return CanRoundTripString(converter) ? new TypeConverterValueSerializer(converter) : null;
    }

    private static bool CanRoundTripString(TypeConverter converter)
        => converter.CanConvertFrom(typeof(string)) && converter.CanConvertTo(typeof(string));

    private sealed class TypeConverterValueSerializer : ValueSerializer
    {
        private readonly TypeConverter _converter;
        public TypeConverterValueSerializer(TypeConverter converter) => _converter = converter;
        public override bool CanConvertFromString(string value, IValueSerializerContext? context) => value is not null;
        public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is not null && _converter.CanConvertTo(context, typeof(string));
        public override object ConvertFromString(string value, IValueSerializerContext? context)
            => _converter.ConvertFrom(context, CultureInfo.InvariantCulture, value) ?? throw GetConvertFromException(value);
        public override string ConvertToString(object value, IValueSerializerContext? context)
            => _converter.ConvertTo(context, CultureInfo.InvariantCulture, value, typeof(string)) as string ?? throw GetConvertToException(value, typeof(string));
    }
}

public class DateTimeValueSerializer : ValueSerializer
{
    public override bool CanConvertFromString(string value, IValueSerializerContext? context)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
    public override bool CanConvertToString(object value, IValueSerializerContext? context) => value is DateTime;
    public override object ConvertFromString(string value, IValueSerializerContext? context)
        => DateTime.Parse(value ?? throw GetConvertFromException(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public override string ConvertToString(object value, IValueSerializerContext? context)
        => value is DateTime dateTime ? dateTime.ToString("o", CultureInfo.InvariantCulture) : throw GetConvertToException(value, typeof(string));
}
