using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Jalium.UI.Markup;

namespace Jalium.UI.Xaml;

public sealed class AmbientPropertyValue
{
    public AmbientPropertyValue(XamlMember property, object? value)
    {
        RetrievedProperty = property ?? throw new ArgumentNullException(nameof(property));
        Value = value;
    }

    public XamlMember RetrievedProperty { get; }
    public object? Value { get; }
}

public sealed class AttachableMemberIdentifier : IEquatable<AttachableMemberIdentifier>
{
    public AttachableMemberIdentifier(Type declaringType, string memberName)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
    }

    public Type DeclaringType { get; }
    public string MemberName { get; }

    public bool Equals(AttachableMemberIdentifier? other)
        => other is not null && DeclaringType == other.DeclaringType && string.Equals(MemberName, other.MemberName, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as AttachableMemberIdentifier);
    public override int GetHashCode() => HashCode.Combine(DeclaringType, StringComparer.Ordinal.GetHashCode(MemberName));
    public override string ToString() => $"{DeclaringType.FullName}.{MemberName}";
    public static bool operator ==(AttachableMemberIdentifier? left, AttachableMemberIdentifier? right) => Equals(left, right);
    public static bool operator !=(AttachableMemberIdentifier? left, AttachableMemberIdentifier? right) => !Equals(left, right);
}

public interface IAttachedPropertyStore
{
    int PropertyCount { get; }
    void CopyPropertiesTo(KeyValuePair<AttachableMemberIdentifier, object?>[] array, int index);
    bool RemoveProperty(AttachableMemberIdentifier attachableMemberIdentifier);
    void SetProperty(AttachableMemberIdentifier attachableMemberIdentifier, object? value);
    bool TryGetProperty(AttachableMemberIdentifier attachableMemberIdentifier, out object? value);
}

public static class AttachablePropertyServices
{
    private sealed class AttachedPropertyStore : IAttachedPropertyStore
    {
        private readonly Dictionary<AttachableMemberIdentifier, object?> _values = new();

        public int PropertyCount => _values.Count;
        public void CopyPropertiesTo(KeyValuePair<AttachableMemberIdentifier, object?>[] array, int index)
            => ((ICollection<KeyValuePair<AttachableMemberIdentifier, object?>>)_values).CopyTo(array, index);
        public bool RemoveProperty(AttachableMemberIdentifier attachableMemberIdentifier) => _values.Remove(attachableMemberIdentifier);
        public void SetProperty(AttachableMemberIdentifier attachableMemberIdentifier, object? value) => _values[attachableMemberIdentifier] = value;
        public bool TryGetProperty(AttachableMemberIdentifier attachableMemberIdentifier, out object? value)
            => _values.TryGetValue(attachableMemberIdentifier, out value);
    }

    private static readonly ConditionalWeakTable<object, AttachedPropertyStore> s_stores = new();

    private static IAttachedPropertyStore GetStore(object instance, bool create)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance is IAttachedPropertyStore store)
        {
            return store;
        }

        if (create)
        {
            return s_stores.GetValue(instance, static _ => new AttachedPropertyStore());
        }

        return s_stores.TryGetValue(instance, out AttachedPropertyStore? existing)
            ? existing
            : EmptyAttachedPropertyStore.Instance;
    }

    private sealed class EmptyAttachedPropertyStore : IAttachedPropertyStore
    {
        public static EmptyAttachedPropertyStore Instance { get; } = new();
        public int PropertyCount => 0;
        public void CopyPropertiesTo(KeyValuePair<AttachableMemberIdentifier, object?>[] array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            if ((uint)index > (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        }
        public bool RemoveProperty(AttachableMemberIdentifier attachableMemberIdentifier) => false;
        public void SetProperty(AttachableMemberIdentifier attachableMemberIdentifier, object? value) => throw new NotSupportedException();
        public bool TryGetProperty(AttachableMemberIdentifier attachableMemberIdentifier, out object? value) { value = null; return false; }
    }

    public static void CopyPropertiesTo(object instance, KeyValuePair<AttachableMemberIdentifier, object?>[] array, int index)
        => GetStore(instance, create: false).CopyPropertiesTo(array, index);

    public static int GetAttachedPropertyCount(object instance) => GetStore(instance, create: false).PropertyCount;

    public static bool RemoveProperty(object instance, AttachableMemberIdentifier name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return GetStore(instance, create: false).RemoveProperty(name);
    }

    public static void SetProperty(object instance, AttachableMemberIdentifier name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        GetStore(instance, create: true).SetProperty(name, value);
    }

    public static bool TryGetProperty(object instance, AttachableMemberIdentifier name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return GetStore(instance, create: false).TryGetProperty(name, out value);
    }

    public static bool TryGetProperty<T>(object instance, AttachableMemberIdentifier name, out T? value)
    {
        if (TryGetProperty(instance, name, out object? candidate) && candidate is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }
}

public interface IAmbientProvider
{
    IEnumerable<AmbientPropertyValue> GetAllAmbientValues(IEnumerable<XamlType> ceilingTypes, bool searchLiveStackOnly, IEnumerable<XamlType> types, params XamlMember[] properties);
    IEnumerable<AmbientPropertyValue> GetAllAmbientValues(IEnumerable<XamlType> ceilingTypes, params XamlMember[] properties);
    IEnumerable<object> GetAllAmbientValues(params XamlType[] types);
    AmbientPropertyValue? GetFirstAmbientValue(IEnumerable<XamlType> ceilingTypes, params XamlMember[] properties);
    object? GetFirstAmbientValue(params XamlType[] types);
}

public interface IDestinationTypeProvider { Type GetDestinationType(); }
public interface INamespacePrefixLookup { string? LookupPrefix(string ns); }
public interface IRootObjectProvider { object? RootObject { get; } }
public interface IXamlIndexingReader { int Count { get; } int CurrentIndex { get; set; } }
public interface IXamlLineInfo { bool HasLineInfo { get; } int LineNumber { get; } int LinePosition { get; } }
public interface IXamlLineInfoConsumer { bool ShouldProvideLineInfo { get; } void SetLineInfo(int lineNumber, int linePosition); }
public interface IXamlNameProvider { string? GetName(object value); }

public interface IXamlNameResolver
{
    bool IsFixupTokenAvailable { get; }
    event EventHandler? OnNameScopeInitializationComplete;
    IEnumerable<KeyValuePair<string, object>> GetAllNamesAndValuesInScope();
    object GetFixupToken(IEnumerable<string> names);
    object GetFixupToken(IEnumerable<string> names, bool canAssignDirectly);
    object? Resolve(string name);
    object? Resolve(string name, out bool isFullyInitialized);
}

public interface IXamlNamespaceResolver
{
    string? GetNamespace(string prefix);
    IEnumerable<NamespaceDeclaration> GetNamespacePrefixes();
}

public interface IXamlObjectWriterFactory
{
    XamlObjectWriterSettings GetParentSettings();
    XamlObjectWriter GetXamlObjectWriter(XamlObjectWriterSettings settings);
}

public interface IXamlSchemaContextProvider { XamlSchemaContext SchemaContext { get; } }

public class XamlWriterSettings
{
    public XamlWriterSettings() { }
    public XamlWriterSettings(XamlWriterSettings settings) => ArgumentNullException.ThrowIfNull(settings);
}

public class XamlObjectReaderSettings : XamlReaderSettings
{
    public bool RequireExplicitContentVisibility { get; set; }
}

public class XamlObjectWriterSettings : XamlWriterSettings
{
    public XamlObjectWriterSettings() { }

    public XamlObjectWriterSettings(XamlObjectWriterSettings settings) : base(settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AfterBeginInitHandler = settings.AfterBeginInitHandler;
        AfterEndInitHandler = settings.AfterEndInitHandler;
        AfterPropertiesHandler = settings.AfterPropertiesHandler;
        BeforePropertiesHandler = settings.BeforePropertiesHandler;
        ExternalNameScope = settings.ExternalNameScope;
        IgnoreCanConvert = settings.IgnoreCanConvert;
        PreferUnconvertedDictionaryKeys = settings.PreferUnconvertedDictionaryKeys;
        RegisterNamesOnExternalNamescope = settings.RegisterNamesOnExternalNamescope;
        RootObjectInstance = settings.RootObjectInstance;
        SkipDuplicatePropertyCheck = settings.SkipDuplicatePropertyCheck;
        SkipProvideValueOnRoot = settings.SkipProvideValueOnRoot;
        SourceBamlUri = settings.SourceBamlUri;
        XamlSetValueHandler = settings.XamlSetValueHandler;
    }

    public EventHandler<XamlObjectEventArgs>? AfterBeginInitHandler { get; set; }
    public EventHandler<XamlObjectEventArgs>? AfterEndInitHandler { get; set; }
    public EventHandler<XamlObjectEventArgs>? AfterPropertiesHandler { get; set; }
    public EventHandler<XamlObjectEventArgs>? BeforePropertiesHandler { get; set; }
    public INameScope? ExternalNameScope { get; set; }
    public bool IgnoreCanConvert { get; set; }
    public bool PreferUnconvertedDictionaryKeys { get; set; }
    public bool RegisterNamesOnExternalNamescope { get; set; }
    public object? RootObjectInstance { get; set; }
    public bool SkipDuplicatePropertyCheck { get; set; }
    public bool SkipProvideValueOnRoot { get; set; }
    public Uri? SourceBamlUri { get; set; }
    public EventHandler<XamlSetValueEventArgs>? XamlSetValueHandler { get; set; }
}

public class XamlXmlReaderSettings : XamlReaderSettings
{
    public XamlXmlReaderSettings() { }

    public XamlXmlReaderSettings(XamlXmlReaderSettings settings) : base(settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CloseInput = settings.CloseInput;
        SkipXmlCompatibilityProcessing = settings.SkipXmlCompatibilityProcessing;
        XmlLang = settings.XmlLang;
        XmlSpacePreserve = settings.XmlSpacePreserve;
    }

    public bool CloseInput { get; set; }
    public bool SkipXmlCompatibilityProcessing { get; set; }
    public string? XmlLang { get; set; }
    public bool XmlSpacePreserve { get; set; }
}

public class XamlXmlWriterSettings : XamlWriterSettings
{
    public bool AssumeValidInput { get; set; }
    public bool CloseOutput { get; set; }
    public XamlXmlWriterSettings Copy() => new() { AssumeValidInput = AssumeValidInput, CloseOutput = CloseOutput };
}

public class XamlObjectEventArgs : EventArgs
{
    public XamlObjectEventArgs(object instance) => Instance = instance ?? throw new ArgumentNullException(nameof(instance));

    internal XamlObjectEventArgs(object instance, int lineNumber, int linePosition, Uri? sourceBamlUri) : this(instance)
    {
        ElementLineNumber = lineNumber;
        ElementLinePosition = linePosition;
        SourceBamlUri = sourceBamlUri;
    }

    public int ElementLineNumber { get; }
    public int ElementLinePosition { get; }
    public object Instance { get; }
    public Uri? SourceBamlUri { get; }
}

[Serializable]
public class XamlException : Exception
{
    public XamlException() { }
    public XamlException(string? message) : base(message) { }
    public XamlException(string? message, Exception? innerException) : base(message, innerException) { }
    public XamlException(string? message, Exception? innerException, int lineNumber, int linePosition) : base(message, innerException)
    {
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

#pragma warning disable SYSLIB0051
    protected XamlException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        LineNumber = info.GetInt32(nameof(LineNumber));
        LinePosition = info.GetInt32(nameof(LinePosition));
    }

    [Obsolete("Formatter-based serialization is obsolete.")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(LineNumber), LineNumber);
        info.AddValue(nameof(LinePosition), LinePosition);
        base.GetObjectData(info, context);
    }
#pragma warning restore SYSLIB0051

    public int LineNumber { get; protected set; }
    public int LinePosition { get; protected set; }
    public override string Message => LineNumber > 0
        ? $"{base.Message} Line {LineNumber}, position {LinePosition}."
        : base.Message;
}

[Serializable]
public class XamlDuplicateMemberException : XamlException
{
    public XamlDuplicateMemberException() { }
    public XamlDuplicateMemberException(string? message) : base(message) { }
    public XamlDuplicateMemberException(string? message, Exception? innerException) : base(message, innerException) { }
    public XamlDuplicateMemberException(XamlMember member, XamlType type)
        : base($"Member '{member?.Name}' is already set on '{type?.Name}'.")
    {
        DuplicateMember = member;
        ParentType = type;
    }
#pragma warning disable SYSLIB0051
    protected XamlDuplicateMemberException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore SYSLIB0051
    public XamlMember? DuplicateMember { get; set; }
    public XamlType? ParentType { get; set; }
}

[Serializable] public class XamlInternalException : XamlException { public XamlInternalException() { } public XamlInternalException(string? message) : base(message) { } public XamlInternalException(string? message, Exception? inner) : base(message, inner) { } protected XamlInternalException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
[Serializable] public class XamlObjectReaderException : XamlException { public XamlObjectReaderException() { } public XamlObjectReaderException(string? message) : base(message) { } public XamlObjectReaderException(string? message, Exception? inner) : base(message, inner) { } protected XamlObjectReaderException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
[Serializable] public class XamlObjectWriterException : XamlException { public XamlObjectWriterException() { } public XamlObjectWriterException(string? message) : base(message) { } public XamlObjectWriterException(string? message, Exception? inner) : base(message, inner) { } protected XamlObjectWriterException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
[Serializable] public class XamlSchemaException : XamlException { public XamlSchemaException() { } public XamlSchemaException(string? message) : base(message) { } public XamlSchemaException(string? message, Exception? inner) : base(message, inner) { } protected XamlSchemaException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
[Serializable] public class XamlXmlWriterException : XamlException { public XamlXmlWriterException() { } public XamlXmlWriterException(string? message) : base(message) { } public XamlXmlWriterException(string? message, Exception? inner) : base(message, inner) { } protected XamlXmlWriterException(SerializationInfo info, StreamingContext context) : base(info, context) { } }
[Serializable] public class XamlParseException : XamlException { public XamlParseException() { } public XamlParseException(string? message) : base(message) { } public XamlParseException(string? message, Exception? inner) : base(message, inner) { } protected XamlParseException(SerializationInfo info, StreamingContext context) : base(info, context) { } }

public abstract class XamlDeferringLoader
{
    protected XamlDeferringLoader() { }
    public abstract object? Load(XamlReader xamlReader, IServiceProvider serviceProvider);
    public abstract XamlReader Save(object? value, IServiceProvider serviceProvider);
}
