using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Markup;

namespace Jalium.UI.Xaml.Schema;

[Flags]
public enum AllowedMemberLocations
{
    None = 0,
    Attribute = 1,
    MemberElement = 2,
    Any = Attribute | MemberElement,
}

public enum ShouldSerializeResult
{
    Default = 0,
    True = 1,
    False = 2,
}

public enum XamlCollectionKind : byte
{
    None = 0,
    Collection = 1,
    Dictionary = 2,
    Array = 3,
}

public class XamlValueConverter<TConverterBase> : IEquatable<XamlValueConverter<TConverterBase>> where TConverterBase : class
{
    private TConverterBase? _instance;
    private bool _instanceCreated;

    public XamlValueConverter(Type? converterType, global::Jalium.UI.Xaml.XamlType? targetType)
        : this(converterType, targetType, null) { }

    public XamlValueConverter(Type? converterType, global::Jalium.UI.Xaml.XamlType? targetType, string? name)
    {
        if (converterType is null && targetType is null && name is null)
            throw new ArgumentException("At least one converter descriptor must be supplied.");
        if (converterType is not null && !typeof(TConverterBase).IsAssignableFrom(converterType))
            throw new ArgumentException($"'{converterType}' does not derive from '{typeof(TConverterBase)}'.", nameof(converterType));
        ConverterType = converterType;
        TargetType = targetType;
        Name = name ?? converterType?.Name ?? targetType?.Name ?? string.Empty;
    }

    public TConverterBase? ConverterInstance
    {
        get
        {
            if (!_instanceCreated)
            {
                _instance = CreateInstance();
                _instanceCreated = true;
            }
            return _instance;
        }
    }

    public Type? ConverterType { get; }
    public string Name { get; }
    public global::Jalium.UI.Xaml.XamlType? TargetType { get; }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Converter construction is the purpose of this reflection compatibility API.")]
    protected virtual TConverterBase? CreateInstance()
    {
        if (ConverterType is null) return null;
        return Activator.CreateInstance(ConverterType) as TConverterBase
            ?? throw new global::Jalium.UI.Xaml.XamlSchemaException($"Converter '{ConverterType}' could not be created as '{typeof(TConverterBase)}'.");
    }

    public bool Equals(XamlValueConverter<TConverterBase>? other)
        => other is not null && ConverterType == other.ConverterType && Equals(TargetType, other.TargetType) && string.Equals(Name, other.Name, StringComparison.Ordinal);
    public override bool Equals(object? obj) => Equals(obj as XamlValueConverter<TConverterBase>);
    public override int GetHashCode() => HashCode.Combine(ConverterType, TargetType, StringComparer.Ordinal.GetHashCode(Name));
    public override string ToString() => Name;
    public static bool operator ==(XamlValueConverter<TConverterBase>? left, XamlValueConverter<TConverterBase>? right) => Equals(left, right);
    public static bool operator !=(XamlValueConverter<TConverterBase>? left, XamlValueConverter<TConverterBase>? right) => !Equals(left, right);
}

public class XamlMemberInvoker
{
    private readonly global::Jalium.UI.Xaml.XamlMember? _member;

    protected XamlMemberInvoker() { }
    public XamlMemberInvoker(global::Jalium.UI.Xaml.XamlMember member) => _member = member ?? throw new ArgumentNullException(nameof(member));

    public MethodInfo? UnderlyingGetter { get; }
    public MethodInfo? UnderlyingSetter { get; }
    public static XamlMemberInvoker UnknownInvoker { get; } = new();

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Member invocation is the purpose of this reflection compatibility API.")]
    public virtual object? GetValue(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_member is null) throw new NotSupportedException("The XAML member is unknown.");
        PropertyInfo? property = instance.GetType().GetProperty(_member.Name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true) throw new NotSupportedException($"Member '{_member.Name}' is not readable.");
        return property.GetValue(instance);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Member invocation is the purpose of this reflection compatibility API.")]
    public virtual void SetValue(object instance, object? value)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_member is null) throw new NotSupportedException("The XAML member is unknown.");
        PropertyInfo? property = instance.GetType().GetProperty(_member.Name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite != true) throw new NotSupportedException($"Member '{_member.Name}' is not writable.");
        property.SetValue(instance, value);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Serialization inspection is the purpose of this reflection compatibility API.")]
    public virtual ShouldSerializeResult ShouldSerializeValue(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_member is null) return ShouldSerializeResult.Default;
        Type type = instance.GetType();
        MethodInfo? shouldSerialize = type.GetMethod("ShouldSerialize" + _member.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (shouldSerialize?.ReturnType == typeof(bool))
            return (bool)shouldSerialize.Invoke(instance, null)! ? ShouldSerializeResult.True : ShouldSerializeResult.False;

        PropertyInfo? property = type.GetProperty(_member.Name, BindingFlags.Instance | BindingFlags.Public);
        DefaultValueAttribute? defaultValue = property?.GetCustomAttribute<DefaultValueAttribute>();
        if (property is not null && defaultValue is not null)
            return Equals(property.GetValue(instance), defaultValue.Value) ? ShouldSerializeResult.False : ShouldSerializeResult.True;
        return ShouldSerializeResult.Default;
    }
}

public class XamlTypeInvoker
{
    private readonly global::Jalium.UI.Xaml.XamlType? _type;

    protected XamlTypeInvoker() { }
    public XamlTypeInvoker(global::Jalium.UI.Xaml.XamlType type) => _type = type ?? throw new ArgumentNullException(nameof(type));

    public EventHandler<XamlSetMarkupExtensionEventArgs>? SetMarkupExtensionHandler => null;
    public EventHandler<XamlSetTypeConverterEventArgs>? SetTypeConverterHandler => null;
    public static XamlTypeInvoker UnknownInvoker { get; } = new();

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Type invocation is the purpose of this reflection compatibility API.")]
    public virtual object? CreateInstance(object?[]? arguments)
    {
        Type runtimeType = _type?.UnderlyingType ?? throw new NotSupportedException("The XAML type is unknown.");
        return Activator.CreateInstance(runtimeType, arguments ?? []);
    }

    public virtual void AddToCollection(object instance, object? item)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance is IList list) { list.Add(item); return; }
        InvokeAdd(instance, [item]);
    }

    public virtual void AddToDictionary(object instance, object? key, object? item)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(key);
        if (instance is IDictionary dictionary) { dictionary.Add(key, item); return; }
        InvokeAdd(instance, [key, item]);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Collection invocation is the purpose of this reflection compatibility API.")]
    public virtual MethodInfo? GetAddMethod(global::Jalium.UI.Xaml.XamlType contentType)
    {
        ArgumentNullException.ThrowIfNull(contentType);
        Type? runtimeType = _type?.UnderlyingType;
        if (runtimeType is null) return null;
        Type? contentRuntimeType = contentType.UnderlyingType;
        return runtimeType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "Add")
            .FirstOrDefault(method => method.GetParameters().Length == 1
                && (contentRuntimeType is null || method.GetParameters()[0].ParameterType.IsAssignableFrom(contentRuntimeType)));
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Collection invocation is the purpose of this reflection compatibility API.")]
    public virtual MethodInfo? GetEnumeratorMethod()
        => _type?.UnderlyingType?.GetMethod(nameof(IEnumerable.GetEnumerator), BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);

    public virtual IEnumerator GetItems(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return instance is IEnumerable enumerable
            ? enumerable.GetEnumerator()
            : throw new NotSupportedException($"'{instance.GetType()}' is not enumerable.");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Collection invocation is the purpose of this reflection compatibility API.")]
    private static void InvokeAdd(object instance, object?[] arguments)
    {
        MethodInfo? add = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "Add" && method.GetParameters().Length == arguments.Length);
        if (add is null) throw new global::Jalium.UI.Xaml.XamlSchemaException($"No compatible Add method exists on '{instance.GetType()}'.");
        add.Invoke(instance, arguments);
    }
}

public class XamlTypeName
{
    public XamlTypeName() { }
    public XamlTypeName(string xamlNamespace, string name) : this(xamlNamespace, name, []) { }

    public XamlTypeName(string xamlNamespace, string name, IEnumerable<XamlTypeName> typeArguments)
    {
        Namespace = xamlNamespace ?? throw new ArgumentNullException(nameof(xamlNamespace));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeArguments = new List<XamlTypeName>(typeArguments ?? throw new ArgumentNullException(nameof(typeArguments)));
    }

    public XamlTypeName(global::Jalium.UI.Xaml.XamlType xamlType)
    {
        ArgumentNullException.ThrowIfNull(xamlType);
        Namespace = xamlType.PreferredXamlNamespace;
        Name = xamlType.Name;
        TypeArguments = xamlType.TypeArguments.Select(static item => new XamlTypeName(item)).ToList();
    }

    public string? Name { get; set; }
    public string? Namespace { get; set; }
    public IList<XamlTypeName> TypeArguments { get; } = new List<XamlTypeName>();

    public static XamlTypeName Parse(string typeName, global::Jalium.UI.Xaml.IXamlNamespaceResolver namespaceResolver)
        => TryParse(typeName, namespaceResolver, out XamlTypeName? result)
            ? result
            : throw new FormatException($"'{typeName}' is not a valid XAML type name.");

    public static IList<XamlTypeName> ParseList(string typeNameList, global::Jalium.UI.Xaml.IXamlNamespaceResolver namespaceResolver)
        => TryParseList(typeNameList, namespaceResolver, out IList<XamlTypeName>? result)
            ? result
            : throw new FormatException($"'{typeNameList}' is not a valid XAML type-name list.");

    public static bool TryParse(string typeName, global::Jalium.UI.Xaml.IXamlNamespaceResolver namespaceResolver, [NotNullWhen(true)] out XamlTypeName? result)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(namespaceResolver);
        result = null;
        string text = typeName.Trim();
        if (text.Length == 0) return false;

        int argumentsStart = FindTopLevel(text, '(');
        string head = argumentsStart >= 0 ? text[..argumentsStart].Trim() : text;
        string? arguments = null;
        if (argumentsStart >= 0)
        {
            if (!text.EndsWith(')')) return false;
            arguments = text[(argumentsStart + 1)..^1];
        }

        string xamlNamespace;
        string name;
        if (head.StartsWith('{'))
        {
            int close = head.IndexOf('}');
            if (close <= 1 || close == head.Length - 1) return false;
            xamlNamespace = head[1..close];
            name = head[(close + 1)..];
        }
        else
        {
            int colon = head.IndexOf(':');
            string prefix = colon >= 0 ? head[..colon] : string.Empty;
            name = colon >= 0 ? head[(colon + 1)..] : head;
            xamlNamespace = namespaceResolver.GetNamespace(prefix) ?? string.Empty;
            if (colon >= 0 && xamlNamespace.Length == 0) return false;
        }

        if (!IsValidName(name)) return false;
        var parsed = new XamlTypeName(xamlNamespace, name);
        if (arguments is not null)
        {
            foreach (string argument in SplitTopLevel(arguments))
            {
                if (!TryParse(argument, namespaceResolver, out XamlTypeName? typeArgument)) return false;
                parsed.TypeArguments.Add(typeArgument);
            }
        }
        result = parsed;
        return true;
    }

    public static bool TryParseList(string typeNameList, global::Jalium.UI.Xaml.IXamlNamespaceResolver namespaceResolver, [NotNullWhen(true)] out IList<XamlTypeName>? result)
    {
        ArgumentNullException.ThrowIfNull(typeNameList);
        ArgumentNullException.ThrowIfNull(namespaceResolver);
        var values = new List<XamlTypeName>();
        foreach (string item in SplitTopLevel(typeNameList))
        {
            if (!TryParse(item, namespaceResolver, out XamlTypeName? parsed)) { result = null; return false; }
            values.Add(parsed);
        }
        result = values;
        return values.Count > 0;
    }

    public override string ToString()
    {
        string head = string.IsNullOrEmpty(Namespace) ? Name ?? string.Empty : $"{{{Namespace}}}{Name}";
        return TypeArguments.Count == 0 ? head : $"{head}({string.Join(", ", TypeArguments)})";
    }

    public string ToString(global::Jalium.UI.Xaml.INamespacePrefixLookup prefixLookup)
    {
        ArgumentNullException.ThrowIfNull(prefixLookup);
        if (!IsValidName(Name) || Namespace is null) throw new InvalidOperationException("The XAML type name is incomplete.");
        string? prefix = prefixLookup.LookupPrefix(Namespace);
        if (prefix is null) throw new InvalidOperationException($"No prefix is registered for '{Namespace}'.");
        string head = prefix.Length == 0 ? Name! : $"{prefix}:{Name}";
        return TypeArguments.Count == 0 ? head : $"{head}({string.Join(", ", TypeArguments.Select(item => item.ToString(prefixLookup)))})";
    }

    public static string ToString(IList<XamlTypeName> typeNameList, global::Jalium.UI.Xaml.INamespacePrefixLookup prefixLookup)
    {
        ArgumentNullException.ThrowIfNull(typeNameList);
        ArgumentNullException.ThrowIfNull(prefixLookup);
        return string.Join(", ", typeNameList.Select(item => item.ToString(prefixLookup)));
    }

    private static bool IsValidName(string? name)
        => !string.IsNullOrWhiteSpace(name) && name.All(character => char.IsLetterOrDigit(character) || character is '_' or '.' or '`');

    private static int FindTopLevel(string text, char target)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == target && depth == 0) return i;
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
        }
        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            else if (text[i] == ',' && depth == 0)
            {
                string part = text[start..i].Trim();
                if (part.Length > 0) yield return part;
                start = i + 1;
            }
        }
        string last = text[start..].Trim();
        if (last.Length > 0) yield return last;
    }
}

public class XamlTypeTypeConverter : System.ComponentModel.TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            var resolver = context?.GetService(typeof(global::Jalium.UI.Xaml.IXamlNamespaceResolver)) as global::Jalium.UI.Xaml.IXamlNamespaceResolver
                ?? throw new NotSupportedException("An IXamlNamespaceResolver service is required.");
            XamlTypeName name = XamlTypeName.Parse(text, resolver);
            var provider = context?.GetService(typeof(global::Jalium.UI.Xaml.IXamlSchemaContextProvider)) as global::Jalium.UI.Xaml.IXamlSchemaContextProvider;
            return (provider?.SchemaContext ?? new global::Jalium.UI.Xaml.XamlSchemaContext()).GetXamlType(name.Namespace ?? string.Empty, name.Name ?? string.Empty);
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is global::Jalium.UI.Xaml.XamlType xamlType)
        {
            var lookup = context?.GetService(typeof(global::Jalium.UI.Xaml.INamespacePrefixLookup)) as global::Jalium.UI.Xaml.INamespacePrefixLookup;
            return lookup is null ? new XamlTypeName(xamlType).ToString() : new XamlTypeName(xamlType).ToString(lookup);
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
