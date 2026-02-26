using System.Collections;
using System.Xml;

namespace Jalium.UI.Markup;

/// <summary>
/// XAML writer modes for values that are of type Expression.
/// </summary>
public enum XamlWriterMode
{
    /// <summary>
    /// Serialize the expression itself (e.g., *Bind(...)).
    /// </summary>
    Expression,

    /// <summary>
    /// Evaluated value of the expression will be serialized.
    /// Used when a snapshot of the tree is needed without evaluating references.
    /// </summary>
    Value,
}

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
public class XmlnsDictionary : IDictionary<string, string>, IDictionary
{
    private readonly Dictionary<string, string> _dict = new();
    private readonly Stack<Dictionary<string, string>>? _scopeStack;
    private int _scopeCount;

    /// <summary>
    /// Default constructor.
    /// </summary>
    public XmlnsDictionary()
    {
    }

    /// <summary>
    /// Copy constructor.
    /// </summary>
    /// <param name="other">The XmlnsDictionary to copy from.</param>
    public XmlnsDictionary(XmlnsDictionary other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var kvp in other._dict)
        {
            _dict[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Gets or sets the namespace URI for the specified prefix.
    /// </summary>
    public string this[string key]
    {
        get => _dict[key];
        set => _dict[key] = value;
    }

    /// <summary>
    /// Gets the number of prefix-namespace mappings.
    /// </summary>
    public int Count => _dict.Count;

    /// <summary>
    /// Gets whether the dictionary is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets the collection of prefix keys.
    /// </summary>
    public ICollection<string> Keys => _dict.Keys;

    /// <summary>
    /// Gets the collection of namespace URI values.
    /// </summary>
    public ICollection<string> Values => _dict.Values;

    /// <summary>
    /// Adds a prefix-namespace mapping.
    /// </summary>
    public void Add(string key, string value) => _dict.Add(key, value);

    /// <summary>
    /// Removes all prefix-namespace mappings.
    /// </summary>
    public void Clear() => _dict.Clear();

    /// <summary>
    /// Determines whether the dictionary contains the specified prefix.
    /// </summary>
    public bool ContainsKey(string key) => _dict.ContainsKey(key);

    /// <summary>
    /// Removes the mapping with the specified prefix.
    /// </summary>
    public bool Remove(string key) => _dict.Remove(key);

    /// <summary>
    /// Tries to get the namespace URI for the specified prefix.
    /// </summary>
    public bool TryGetValue(string key, out string value) => _dict.TryGetValue(key, out value!);

    /// <summary>
    /// Looks up the namespace URI for a given prefix.
    /// </summary>
    /// <param name="prefix">The prefix to look up.</param>
    /// <returns>The namespace URI, or null if not found.</returns>
    public string? LookupNamespace(string prefix)
    {
        return _dict.TryGetValue(prefix, out var ns) ? ns : null;
    }

    /// <summary>
    /// Looks up the prefix for a given namespace URI.
    /// </summary>
    /// <param name="xmlNamespace">The namespace URI to look up.</param>
    /// <returns>The prefix, or null if not found.</returns>
    public string? LookupPrefix(string xmlNamespace)
    {
        foreach (var kvp in _dict)
        {
            if (kvp.Value == xmlNamespace)
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Pushes a new scope onto the namespace stack.
    /// </summary>
    public void PushScope()
    {
        _scopeCount++;
    }

    /// <summary>
    /// Pops the current scope from the namespace stack.
    /// </summary>
    public void PopScope()
    {
        if (_scopeCount > 0)
            _scopeCount--;
    }

    void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item) =>
        ((ICollection<KeyValuePair<string, string>>)_dict).Add(item);

    bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item) =>
        ((ICollection<KeyValuePair<string, string>>)_dict).Contains(item);

    void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, string>>)_dict).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item) =>
        ((ICollection<KeyValuePair<string, string>>)_dict).Remove(item);

    /// <summary>
    /// Returns an enumerator that iterates through the prefix-namespace mappings.
    /// </summary>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _dict.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region IDictionary explicit implementation

    bool IDictionary.IsFixedSize => false;
    bool IDictionary.IsReadOnly => false;
    ICollection IDictionary.Keys => _dict.Keys;
    ICollection IDictionary.Values => _dict.Values;
    int ICollection.Count => Count;
    object ICollection.SyncRoot => ((ICollection)_dict).SyncRoot;
    bool ICollection.IsSynchronized => false;

    object? IDictionary.this[object key]
    {
        get => key is string s ? (_dict.TryGetValue(s, out var v) ? v : null) : null;
        set { if (key is string s && value is string v) _dict[s] = v; }
    }

    bool IDictionary.Contains(object key) => key is string s && _dict.ContainsKey(s);

    void IDictionary.Add(object key, object? value)
    {
        if (key is string s && value is string v) _dict.Add(s, v);
    }

    void IDictionary.Remove(object key)
    {
        if (key is string s) _dict.Remove(s);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)_dict).GetEnumerator();

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_dict).CopyTo(array, index);

    #endregion
}

/// <summary>
/// Manages the serialization context for XAML writing, including the serialization mode for Expression types.
/// </summary>
public class XamlDesignerSerializationManager : IServiceProvider
{
    private XamlWriterMode _xamlWriterMode = XamlWriterMode.Value;
    private readonly XmlWriter? _xmlWriter;
    private readonly Dictionary<Type, object> _services = new();

    /// <summary>
    /// Constructs a XamlDesignerSerializationManager with the specified XmlWriter.
    /// </summary>
    /// <param name="xmlWriter">The XmlWriter to use for serialization.</param>
    public XamlDesignerSerializationManager(XmlWriter xmlWriter)
    {
        _xmlWriter = xmlWriter;
    }

    /// <summary>
    /// Gets or sets the mode of serialization for all Expressions.
    /// </summary>
    public XamlWriterMode XamlWriterMode
    {
        get => _xamlWriterMode;
        set
        {
            if (value != XamlWriterMode.Expression && value != XamlWriterMode.Value)
                throw new ArgumentException($"Invalid XamlWriterMode value: {value}", nameof(value));
            _xamlWriterMode = value;
        }
    }

    /// <summary>
    /// Adds a service to the service provider.
    /// </summary>
    /// <param name="serviceType">The type of service to add.</param>
    /// <param name="service">The service instance.</param>
    public void AddService(Type serviceType, object service)
    {
        _services[serviceType] = service;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        return _services.GetValueOrDefault(serviceType);
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
            foreach (var kvp in parserContext._xmlnsDictionary)
            {
                xmlnsMgr.AddNamespace(kvp.Key, kvp.Value);
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
    public Type? GetType(string xmlNamespace, string localName)
    {
        // Delegate to the Jalium.UI type registry
        return XamlTypeRegistry.GetType(localName);
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
/// Interface for providing the base URI context in XAML.
/// </summary>
public interface IUriContext
{
    /// <summary>
    /// Gets or sets the base URI of the current context.
    /// </summary>
    Uri? BaseUri { get; set; }
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
