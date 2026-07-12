using System.Reflection;
using System.Xml;

namespace Jalium.UI.Xaml;

public enum XamlNodeType
{
    None = 0,
    StartObject = 1,
    GetObject = 2,
    EndObject = 3,
    StartMember = 4,
    EndMember = 5,
    Value = 6,
    NamespaceDeclaration = 7,
}

public sealed class NamespaceDeclaration
{
    public NamespaceDeclaration(string xamlNamespace, string prefix)
    {
        Namespace = xamlNamespace ?? throw new ArgumentNullException(nameof(xamlNamespace));
        Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
    }

    public string Prefix { get; }

    public string Namespace { get; }

    public override string ToString() => $"xmlns:{Prefix}={Namespace}";
}

public class XamlSchemaContextSettings
{
    public XamlSchemaContextSettings()
    {
    }

    public XamlSchemaContextSettings(XamlSchemaContextSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SupportMarkupExtensionsWithDuplicateArity = settings.SupportMarkupExtensionsWithDuplicateArity;
        FullyQualifyAssemblyNamesInClrNamespaces = settings.FullyQualifyAssemblyNamesInClrNamespaces;
    }

    public bool SupportMarkupExtensionsWithDuplicateArity { get; set; }

    public bool FullyQualifyAssemblyNamesInClrNamespaces { get; set; }
}

public class XamlSchemaContext
{
    private readonly List<Assembly> _referenceAssemblies;
    private readonly Dictionary<Type, XamlType> _typeCache = new();

    public XamlSchemaContext()
        : this(AppDomain.CurrentDomain.GetAssemblies(), new XamlSchemaContextSettings())
    {
    }

    public XamlSchemaContext(XamlSchemaContextSettings settings)
        : this(AppDomain.CurrentDomain.GetAssemblies(), settings)
    {
    }

    public XamlSchemaContext(IEnumerable<Assembly> referenceAssemblies)
        : this(referenceAssemblies, new XamlSchemaContextSettings())
    {
    }

    public XamlSchemaContext(IEnumerable<Assembly> referenceAssemblies, XamlSchemaContextSettings settings)
    {
        ArgumentNullException.ThrowIfNull(referenceAssemblies);
        ArgumentNullException.ThrowIfNull(settings);
        _referenceAssemblies = referenceAssemblies.Distinct().ToList();
        SupportMarkupExtensionsWithDuplicateArity = settings.SupportMarkupExtensionsWithDuplicateArity;
        FullyQualifyAssemblyNamesInClrNamespaces = settings.FullyQualifyAssemblyNamesInClrNamespaces;
    }

    public bool SupportMarkupExtensionsWithDuplicateArity { get; }

    public bool FullyQualifyAssemblyNamesInClrNamespaces { get; }

    public virtual IList<Assembly> ReferenceAssemblies => _referenceAssemblies.AsReadOnly();

    public virtual XamlType GetXamlType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!_typeCache.TryGetValue(type, out XamlType? result))
        {
            result = new XamlType(type, this);
            _typeCache[type] = result;
        }

        return result;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "The schema context searches caller-supplied reference assemblies; trimmed apps can request types directly by Type.")]
    public virtual XamlType? GetXamlType(string xamlNamespace, string name)
    {
        ArgumentNullException.ThrowIfNull(xamlNamespace);
        ArgumentNullException.ThrowIfNull(name);
        foreach (Assembly assembly in _referenceAssemblies)
        {
            Type? type;
            try
            {
                type = assembly.GetTypes().FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            }
            catch (ReflectionTypeLoadException exception)
            {
                type = exception.Types.FirstOrDefault(candidate => candidate is not null && string.Equals(candidate.Name, name, StringComparison.Ordinal));
            }

            if (type is not null)
            {
                return GetXamlType(type);
            }
        }

        return new XamlType(xamlNamespace, name, [], this);
    }
}

public class XamlReaderSettings
{
    public XamlReaderSettings()
    {
    }

    public XamlReaderSettings(XamlReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AllowProtectedMembersOnRoot = settings.AllowProtectedMembersOnRoot;
        ProvideLineInfo = settings.ProvideLineInfo;
        BaseUri = settings.BaseUri;
        LocalAssembly = settings.LocalAssembly;
        IgnoreUidsOnPropertyElements = settings.IgnoreUidsOnPropertyElements;
        ValuesMustBeString = settings.ValuesMustBeString;
    }

    public bool AllowProtectedMembersOnRoot { get; set; }
    public bool ProvideLineInfo { get; set; }
    public Uri? BaseUri { get; set; }
    public Assembly? LocalAssembly { get; set; }
    public bool IgnoreUidsOnPropertyElements { get; set; }
    public bool ValuesMustBeString { get; set; }
}

public class XamlType
{
    public XamlType(Type underlyingType, XamlSchemaContext schemaContext)
    {
        UnderlyingType = underlyingType ?? throw new ArgumentNullException(nameof(underlyingType));
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        Name = underlyingType.Name;
        PreferredXamlNamespace = underlyingType.Namespace ?? string.Empty;
        TypeArguments = underlyingType.IsGenericType
            ? underlyingType.GetGenericArguments().Select(schemaContext.GetXamlType).ToList()
            : [];
    }

    public XamlType(string name, IList<XamlType> typeArguments, XamlSchemaContext schemaContext)
        : this(string.Empty, name, typeArguments, schemaContext)
    {
    }

    public XamlType(string xamlNamespace, string name, IList<XamlType> typeArguments, XamlSchemaContext schemaContext)
    {
        PreferredXamlNamespace = xamlNamespace ?? throw new ArgumentNullException(nameof(xamlNamespace));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeArguments = typeArguments ?? throw new ArgumentNullException(nameof(typeArguments));
        SchemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
    }

    public virtual XamlType? BaseType => UnderlyingType?.BaseType is { } baseType ? SchemaContext.GetXamlType(baseType) : null;
    public virtual bool IsNameValid => !string.IsNullOrWhiteSpace(Name);
    public virtual bool IsUnknown => UnderlyingType is null;
    public virtual string Name { get; }
    public virtual string PreferredXamlNamespace { get; }
    public virtual IList<XamlType> TypeArguments { get; }
    public virtual Type? UnderlyingType { get; }
    public virtual bool ConstructionRequiresArguments => false;
    public virtual bool IsArray => UnderlyingType?.IsArray == true;
    public virtual bool IsCollection => UnderlyingType is not null && typeof(System.Collections.IEnumerable).IsAssignableFrom(UnderlyingType) && UnderlyingType != typeof(string);
    public virtual bool IsConstructible => UnderlyingType is not null && !UnderlyingType.IsAbstract;
    public virtual bool IsDictionary => UnderlyingType is not null && typeof(System.Collections.IDictionary).IsAssignableFrom(UnderlyingType);
    public virtual bool IsGeneric => UnderlyingType?.IsGenericType == true || TypeArguments.Count > 0;
    public virtual bool IsMarkupExtension => UnderlyingType?.Name.EndsWith("Extension", StringComparison.Ordinal) == true;
    public virtual bool IsNullable => UnderlyingType is null || !UnderlyingType.IsValueType || Nullable.GetUnderlyingType(UnderlyingType) is not null;
    public virtual bool IsPublic => UnderlyingType?.IsPublic == true || UnderlyingType?.IsNestedPublic == true;
    public virtual XamlSchemaContext SchemaContext { get; }
    public override string ToString() => $"{{{PreferredXamlNamespace}}}{Name}";
}

public interface IXmlBackedXamlReader
{
    XmlReader XmlReader { get; }
}

public abstract class XamlReader : IDisposable
{
    private bool _isDisposed;

    protected XamlReader()
    {
    }

    public abstract XamlNodeType NodeType { get; }
    public abstract bool IsEof { get; }
    public abstract NamespaceDeclaration? Namespace { get; }
    public abstract XamlType? Type { get; }
    public abstract object? Value { get; }
    public abstract XamlMember? Member { get; }
    public abstract XamlSchemaContext SchemaContext { get; }
    public abstract bool Read();

    public virtual void Skip()
    {
        if (IsEof)
        {
            return;
        }

        XamlNodeType start = NodeType;
        if (start is not XamlNodeType.StartObject and not XamlNodeType.GetObject and not XamlNodeType.StartMember)
        {
            Read();
            return;
        }

        int depth = 1;
        while (depth > 0 && Read())
        {
            depth += NodeType switch
            {
                XamlNodeType.StartObject or XamlNodeType.GetObject or XamlNodeType.StartMember => 1,
                XamlNodeType.EndObject or XamlNodeType.EndMember => -1,
                _ => 0,
            };
        }
    }

    public virtual XamlReader ReadSubtree() => this;

    public void Close() => Dispose();

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Dispose(disposing: true);
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}

public class XamlXmlReader : XamlReader, IXmlBackedXamlReader
{
    private readonly XmlReader _reader;
    private readonly bool _ownsReader;
    private readonly XamlSchemaContext _schemaContext;

    public XamlXmlReader(XmlReader xmlReader)
        : this(xmlReader, new XamlSchemaContext(), ownsReader: false)
    {
    }

    public XamlXmlReader(XmlReader xmlReader, XamlSchemaContext schemaContext)
        : this(xmlReader, schemaContext, ownsReader: false)
    {
    }

    public XamlXmlReader(XmlReader xmlReader, XamlXmlReaderSettings settings)
        : this(xmlReader, new XamlSchemaContext(), settings)
    {
    }

    public XamlXmlReader(XmlReader xmlReader, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        : this(xmlReader, schemaContext, settings?.CloseInput == true)
    {
        ArgumentNullException.ThrowIfNull(settings);
    }

    public XamlXmlReader(Stream stream)
        : this(System.Xml.XmlReader.Create(stream), new XamlSchemaContext(), ownsReader: true)
    {
    }

    public XamlXmlReader(Stream stream, XamlSchemaContext schemaContext)
        : this(CreateXmlReader(stream, closeInput: false), schemaContext, ownsReader: true)
    {
    }

    public XamlXmlReader(Stream stream, XamlXmlReaderSettings settings)
        : this(stream, new XamlSchemaContext(), settings)
    {
    }

    public XamlXmlReader(Stream stream, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        : this(CreateXmlReader(stream, settings?.CloseInput == true), schemaContext, ownsReader: true)
    {
        ArgumentNullException.ThrowIfNull(settings);
    }

    public XamlXmlReader(TextReader textReader)
        : this(System.Xml.XmlReader.Create(textReader), new XamlSchemaContext(), ownsReader: true)
    {
    }

    public XamlXmlReader(TextReader textReader, XamlSchemaContext schemaContext)
        : this(CreateXmlReader(textReader, closeInput: false), schemaContext, ownsReader: true)
    {
    }

    public XamlXmlReader(TextReader textReader, XamlXmlReaderSettings settings)
        : this(textReader, new XamlSchemaContext(), settings)
    {
    }

    public XamlXmlReader(TextReader textReader, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        : this(CreateXmlReader(textReader, settings?.CloseInput == true), schemaContext, ownsReader: true)
    {
        ArgumentNullException.ThrowIfNull(settings);
    }

    public XamlXmlReader(string fileName)
        : this(System.Xml.XmlReader.Create(fileName), new XamlSchemaContext(), ownsReader: true)
    {
    }

    public XamlXmlReader(string fileName, XamlSchemaContext schemaContext)
        : this(System.Xml.XmlReader.Create(fileName ?? throw new ArgumentNullException(nameof(fileName))), schemaContext, ownsReader: true)
    {
    }

    public XamlXmlReader(string fileName, XamlXmlReaderSettings settings)
        : this(fileName, new XamlSchemaContext(), settings)
    {
    }

    public XamlXmlReader(string fileName, XamlSchemaContext schemaContext, XamlXmlReaderSettings settings)
        : this(System.Xml.XmlReader.Create(fileName ?? throw new ArgumentNullException(nameof(fileName)), new XmlReaderSettings { CloseInput = settings?.CloseInput == true }), schemaContext, ownsReader: true)
    {
        ArgumentNullException.ThrowIfNull(settings);
    }

    private XamlXmlReader(XmlReader reader, XamlSchemaContext schemaContext, bool ownsReader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _schemaContext = schemaContext ?? throw new ArgumentNullException(nameof(schemaContext));
        _ownsReader = ownsReader;
    }

    private static XmlReader CreateXmlReader(Stream stream, bool closeInput)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return System.Xml.XmlReader.Create(stream, new XmlReaderSettings { CloseInput = closeInput });
    }

    private static XmlReader CreateXmlReader(TextReader textReader, bool closeInput)
    {
        ArgumentNullException.ThrowIfNull(textReader);
        return System.Xml.XmlReader.Create(textReader, new XmlReaderSettings { CloseInput = closeInput });
    }

    XmlReader IXmlBackedXamlReader.XmlReader => _reader;
    public override XamlNodeType NodeType => _reader.NodeType switch
    {
        XmlNodeType.Element => XamlNodeType.StartObject,
        XmlNodeType.EndElement => XamlNodeType.EndObject,
        XmlNodeType.Attribute => XamlNodeType.StartMember,
        XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace => XamlNodeType.Value,
        _ => XamlNodeType.None,
    };
    public override bool IsEof => _reader.EOF;
    public override NamespaceDeclaration? Namespace => _reader.Prefix == "xmlns" || _reader.Name == "xmlns"
        ? new NamespaceDeclaration(_reader.Value, _reader.LocalName == "xmlns" ? string.Empty : _reader.LocalName)
        : null;
    public override XamlType? Type => _reader.NodeType is XmlNodeType.Element or XmlNodeType.EndElement
        ? _schemaContext.GetXamlType(_reader.NamespaceURI, _reader.LocalName)
        : null;
    public override object? Value => _reader.HasValue ? _reader.Value : null;
    public override XamlMember? Member => null;
    public override XamlSchemaContext SchemaContext => _schemaContext;
    public override bool Read() => _reader.Read();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsReader)
        {
            _reader.Dispose();
        }

        base.Dispose(disposing);
    }
}
