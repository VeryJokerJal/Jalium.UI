using System.Collections;
using System.Reflection;
using System.Xml;

namespace Jalium.UI.Markup;

/// <summary>
/// Represents a mapping between an XML namespace, a CLR namespace, and the assembly that contains the relevant types.
/// </summary>
public class NamespaceMapEntry
{
    private string? _xmlNamespace;
    private string? _clrNamespace;
    private string? _assemblyName;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public NamespaceMapEntry()
    {
    }

    /// <summary>
    /// Constructor with all properties.
    /// </summary>
    /// <param name="xmlNamespace">The XML namespace.</param>
    /// <param name="assemblyName">The assembly name.</param>
    /// <param name="clrNamespace">The CLR namespace.</param>
    public NamespaceMapEntry(string? xmlNamespace, string? assemblyName, string? clrNamespace)
    {
        _xmlNamespace = xmlNamespace;
        _assemblyName = assemblyName;
        _clrNamespace = clrNamespace;
    }

    /// <summary>
    /// Gets or sets the XML namespace for this mapping entry.
    /// </summary>
    public string? XmlNamespace
    {
        get => _xmlNamespace;
        set => _xmlNamespace = value;
    }

    /// <summary>
    /// Gets or sets the CLR namespace for this mapping entry.
    /// </summary>
    public string? ClrNamespace
    {
        get => _clrNamespace;
        set => _clrNamespace = value;
    }

    /// <summary>
    /// Gets or sets the assembly name for this mapping entry.
    /// </summary>
    public string? AssemblyName
    {
        get => _assemblyName;
        set => _assemblyName = value;
    }
}

/// <summary>
/// Provides methods used internally to attach events on EventSetters and Templates in compiled content.
/// </summary>
public interface IStyleConnector
{
    /// <summary>
    /// Called to attach events and templates on compiled content.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="target">The target object.</param>
    void Connect(int connectionId, object target);
}

/// <summary>
/// A dictionary that controls XML prefix-to-namespace URI mappings.
/// </summary>
public class XmlnsDictionary : IDictionary, Jalium.UI.Xaml.IXamlNamespaceResolver
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Stack<Dictionary<string, string>> _scopes = new();
    private bool _sealed;

    public XmlnsDictionary() { }

    public XmlnsDictionary(XmlnsDictionary xmlnsDictionary)
    {
        ArgumentNullException.ThrowIfNull(xmlnsDictionary);
        foreach ((string prefix, string xmlNamespace) in xmlnsDictionary._values)
            _values.Add(prefix, xmlNamespace);
    }

    public int Count => _values.Count;
    public bool IsFixedSize => _sealed;
    public bool IsReadOnly => _sealed;
    public bool IsSynchronized => false;
    public object SyncRoot => ((ICollection)_values).SyncRoot;
    public ICollection Keys => _values.Keys;
    public ICollection Values => _values.Values;
    public bool Sealed => _sealed;

    public object? this[object prefix]
    {
        get => prefix is string text ? this[text] : null;
        set
        {
            if (prefix is not string text) throw new ArgumentException("The XML namespace prefix must be a string.", nameof(prefix));
            this[text] = value as string ?? throw new ArgumentException("The XML namespace must be a string.", nameof(value));
        }
    }

    public string? this[string prefix]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(prefix);
            return _values.GetValueOrDefault(prefix);
        }
        set
        {
            EnsureMutable();
            ArgumentNullException.ThrowIfNull(prefix);
            ArgumentNullException.ThrowIfNull(value);
            _values[prefix] = value;
        }
    }

    public void Add(object prefix, object? xmlNamespace)
    {
        if (prefix is not string prefixText) throw new ArgumentException("The XML namespace prefix must be a string.", nameof(prefix));
        if (xmlNamespace is not string namespaceText) throw new ArgumentException("The XML namespace must be a string.", nameof(xmlNamespace));
        Add(prefixText, namespaceText);
    }

    public void Add(string prefix, string xmlNamespace)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        _values.Add(prefix, xmlNamespace);
    }

    public void Clear() { EnsureMutable(); _values.Clear(); }
    public bool Contains(object key) => key is string prefix && _values.ContainsKey(prefix);
    public void Remove(object prefix) { if (prefix is string text) Remove(text); }
    public void Remove(string prefix) { EnsureMutable(); ArgumentNullException.ThrowIfNull(prefix); _values.Remove(prefix); }

    public string? DefaultNamespace() => GetNamespace(string.Empty);
    public string? GetNamespace(string prefix) => LookupNamespace(prefix);
    public string? LookupNamespace(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return _values.GetValueOrDefault(prefix);
    }

    public string? LookupPrefix(string xmlNamespace)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        foreach ((string prefix, string candidate) in _values)
            if (string.Equals(candidate, xmlNamespace, StringComparison.Ordinal)) return prefix;
        return null;
    }

    public IEnumerable<Jalium.UI.Xaml.NamespaceDeclaration> GetNamespacePrefixes()
        => _values.Select(static item => new Jalium.UI.Xaml.NamespaceDeclaration(item.Value, item.Key)).ToArray();

    public void PushScope()
    {
        EnsureMutable();
        _scopes.Push(new Dictionary<string, string>(_values, StringComparer.Ordinal));
    }

    public void PopScope()
    {
        EnsureMutable();
        if (_scopes.Count == 0) throw new InvalidOperationException("No XML namespace scope is active.");
        Dictionary<string, string> previous = _scopes.Pop();
        _values.Clear();
        foreach ((string prefix, string xmlNamespace) in previous) _values.Add(prefix, xmlNamespace);
    }

    public void Seal() => _sealed = true;

    public void CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array is DictionaryEntry[] entries) { CopyTo(entries, index); return; }
        ((ICollection)_values.Select(static item => new DictionaryEntry(item.Key, item.Value)).ToArray()).CopyTo(array, index);
    }

    public void CopyTo(DictionaryEntry[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (index < 0 || index + Count > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        foreach ((string prefix, string xmlNamespace) in _values)
            array[index++] = new DictionaryEntry(prefix, xmlNamespace);
    }

    protected IDictionaryEnumerator GetDictionaryEnumerator() => ((IDictionary)_values).GetEnumerator();
    protected IEnumerator GetEnumerator() => GetDictionaryEnumerator();
    IDictionaryEnumerator IDictionary.GetEnumerator() => GetDictionaryEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void EnsureMutable()
    {
        if (_sealed) throw new InvalidOperationException("The XML namespace dictionary is sealed.");
    }
}

/// <summary>
/// Provides all the context information required by the XAML parser.
/// </summary>
public class ParserContext
{
    private XmlnsDictionary? _xmlnsDictionary;
    private Uri? _baseUri;
    private string _xmlLang = string.Empty;
    private string _xmlSpace = string.Empty;
    private XamlTypeMapper? _xamlTypeMapper;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public ParserContext()
    {
    }

    /// <summary>
    /// Constructor that takes an XmlParserContext.
    /// </summary>
    /// <param name="xmlParserContext">The XML parser context to initialize from.</param>
    public ParserContext(XmlParserContext xmlParserContext)
    {
        ArgumentNullException.ThrowIfNull(xmlParserContext);

        _xmlLang = xmlParserContext.XmlLang;
        _xmlnsDictionary = new XmlnsDictionary();

        if (xmlParserContext.BaseURI != null && xmlParserContext.BaseURI.Length > 0)
        {
            _baseUri = new Uri(xmlParserContext.BaseURI, UriKind.RelativeOrAbsolute);
        }

        var xmlnsManager = xmlParserContext.NamespaceManager;
        if (xmlnsManager != null)
        {
            foreach (string key in xmlnsManager)
            {
                var ns = xmlnsManager.LookupNamespace(key);
                if (ns != null)
                {
                    _xmlnsDictionary.Add(key, ns);
                }
            }
        }
    }

    /// <summary>
    /// Gets the XML namespace dictionary for this context.
    /// </summary>
    public XmlnsDictionary XmlnsDictionary
    {
        get
        {
            _xmlnsDictionary ??= new XmlnsDictionary();
            return _xmlnsDictionary;
        }
    }

    /// <summary>
    /// Gets or sets the xml:lang property.
    /// </summary>
    public string XmlLang
    {
        get => _xmlLang;
        set => _xmlLang = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the xml:space property.
    /// </summary>
    public string XmlSpace
    {
        get => _xmlSpace;
        set => _xmlSpace = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the base URI.
    /// </summary>
    public Uri? BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    public XamlTypeMapper? XamlTypeMapper
    {
        get => _xamlTypeMapper;
        set => _xamlTypeMapper = value;
    }

    /// <summary>
    /// Converts a ParserContext to an XmlParserContext.
    /// </summary>
    public static implicit operator XmlParserContext(ParserContext parserContext)
    {
        return ToXmlParserContext(parserContext);
    }

    /// <summary>
    /// Converts a ParserContext to an XmlParserContext.
    /// </summary>
    /// <param name="parserContext">The ParserContext to convert.</param>
    /// <returns>An XmlParserContext with the same namespace and base URI information.</returns>
    public static XmlParserContext ToXmlParserContext(ParserContext parserContext)
    {
        ArgumentNullException.ThrowIfNull(parserContext);

        var xmlnsMgr = new XmlNamespaceManager(new NameTable());

        if (parserContext._xmlnsDictionary != null)
        {
            foreach (DictionaryEntry entry in parserContext._xmlnsDictionary)
            {
                xmlnsMgr.AddNamespace((string)entry.Key, (string)entry.Value!);
            }
        }

        var xmlSpace = System.Xml.XmlSpace.None;
        if (!string.IsNullOrEmpty(parserContext.XmlSpace))
        {
            if (Enum.TryParse<System.Xml.XmlSpace>(parserContext.XmlSpace, true, out var parsedSpace))
            {
                xmlSpace = parsedSpace;
            }
        }

        var xmlParserContext = new XmlParserContext(null, xmlnsMgr, parserContext.XmlLang, xmlSpace);

        if (parserContext.BaseUri != null)
        {
            xmlParserContext.BaseURI = parserContext.BaseUri.ToString();
        }

        return xmlParserContext;
    }
}

/// <summary>
/// Maps XML namespaces and local names to appropriate CLR types, properties, and events.
/// </summary>
/// <remarks>
/// In Jalium.UI, type mapping is handled by <see cref="XamlTypeRegistry"/>.
/// This class is provided for WPF API compatibility.
/// </remarks>
public class XamlTypeMapper
{
    private readonly string[] _assemblyNames;
    private readonly NamespaceMapEntry[]? _namespaceMaps;
    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);

    public static XamlTypeMapper DefaultMapper { get; } = new XamlTypeMapper([]);

    /// <summary>
    /// Constructs a XamlTypeMapper with the specified assembly names.
    /// </summary>
    /// <param name="assemblyNames">Assemblies XamlTypeMapper should use when resolving XAML.</param>
    public XamlTypeMapper(string[] assemblyNames)
    {
        ArgumentNullException.ThrowIfNull(assemblyNames);
        _assemblyNames = assemblyNames;
        _namespaceMaps = null;
    }

    /// <summary>
    /// Constructs a XamlTypeMapper with the specified assembly names and namespace maps.
    /// </summary>
    /// <param name="assemblyNames">Assemblies XamlTypeMapper should use when resolving XAML.</param>
    /// <param name="namespaceMaps">NamespaceMap entries the XamlTypeMapper should use when resolving XAML.</param>
    public XamlTypeMapper(string[] assemblyNames, NamespaceMapEntry[] namespaceMaps)
    {
        ArgumentNullException.ThrowIfNull(assemblyNames);
        _assemblyNames = assemblyNames;
        _namespaceMaps = namespaceMaps;
    }

    /// <summary>
    /// Maps an XAML tag to a CLR Type.
    /// </summary>
    /// <param name="xmlNamespace">The XML namespace URI of the tag.</param>
    /// <param name="localName">The local name of the tag.</param>
    /// <returns>The CLR Type for the object, or null if no type was found.</returns>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026",
        Justification = "The compatibility mapper searches explicitly supplied assemblies; trimmed applications should register XAML types in XamlTypeRegistry.")]
    public Type? GetType(string xmlNamespace, string localName)
    {
        ArgumentNullException.ThrowIfNull(xmlNamespace);
        ArgumentNullException.ThrowIfNull(localName);

        Type? registered = XamlTypeRegistry.GetType(localName);
        if (registered is not null && (registered.IsPublic || registered.IsNestedPublic || AllowInternalType(registered)))
        {
            return registered;
        }

        foreach (string assemblyName in _assemblyNames)
        {
            try
            {
                Assembly assembly = _assemblyPaths.TryGetValue(assemblyName, out string? path)
                    ? Assembly.LoadFrom(path)
                    : Assembly.Load(new AssemblyName(assemblyName));
                Type? candidate = assembly.GetTypes().FirstOrDefault(type =>
                    string.Equals(type.Name, localName, StringComparison.Ordinal)
                    && (type.IsPublic || type.IsNestedPublic || AllowInternalType(type)));
                if (candidate is not null)
                {
                    return candidate;
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException or ReflectionTypeLoadException)
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Adds a mapping entry for the specified XML namespace, CLR namespace, and assembly.
    /// </summary>
    /// <param name="xmlNamespace">The XML namespace to map.</param>
    /// <param name="clrNamespace">The CLR namespace.</param>
    /// <param name="assemblyName">The assembly name.</param>
    public void AddMappingProcessingInstruction(string xmlNamespace, string clrNamespace, string assemblyName)
    {
        // In Jalium.UI, type registration is handled by XamlTypeRegistry.
        // This method is provided for WPF API compatibility.
    }

    public void SetAssemblyPath(string assemblyName, string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyName);
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
        _assemblyPaths[assemblyName] = Path.GetFullPath(assemblyPath);
    }

    protected virtual bool AllowInternalType(Type type) => false;

    /// <summary>
    /// Sets a subclass mapping for the specified XML namespace.
    /// </summary>
    /// <param name="xmlNamespace">The XML namespace.</param>
    /// <param name="subClass">The subclass type.</param>
    public void SetSubclassTypeMapper(string xmlNamespace, Type subClass)
    {
        // Stub for WPF API compatibility
    }
}

/// <summary>
/// Interface for indicating that an element has resources.
/// </summary>
public interface IHaveResources
{
    /// <summary>
    /// Gets the resources associated with this element.
    /// </summary>
    ResourceDictionary Resources { get; set; }
}

/// <summary>
/// Interface for providing component connection during XAML loading.
/// </summary>
public interface IComponentConnector
{
    /// <summary>
    /// Attaches events and sets names of compiled content.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    /// <param name="target">The target object.</param>
    void Connect(int connectionId, object target);

    /// <summary>
    /// Called by the generated code to initialize the component.
    /// </summary>
    void InitializeComponent();
}

