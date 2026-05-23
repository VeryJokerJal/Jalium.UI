using System.Xml;

namespace Jalium.Extensions.Configuration;

/// <summary>Source describing an XML file. Path semantics match <see cref="JsonConfigurationSource"/>.</summary>
public sealed class XmlConfigurationSource : FileConfigurationSource
{
    public override IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        EnsureDefaults(builder);
        return new XmlConfigurationProvider(this);
    }
}

/// <summary>
/// Reads an XML document into the flat <c>"key:subkey:listidx" → value</c> model.
/// Mirrors <c>Microsoft.Extensions.Configuration.Xml.XmlConfigurationProvider</c>:
/// element children → object keys; attributes → object keys at the same level;
/// repeated sibling elements with the same name → array indices (Name → [0,1,2…]).
/// </summary>
public sealed class XmlConfigurationProvider : FileConfigurationProvider
{
    public XmlConfigurationProvider(XmlConfigurationSource source) : base(source) { }

    /// <summary>Exposes the underlying flat map to sibling stream providers — internal only.</summary>
    internal IDictionary<string, string?> InternalData => Data;

    public override void Load(Stream stream)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        });
        var doc = new XmlDocument();
        doc.Load(reader);
        if (doc.DocumentElement == null) { Data = data; return; }

        // Skip the root element name itself — MS does the same.
        VisitElement(data, doc.DocumentElement, parentPath: null, isRoot: true);
        Data = data;
    }

    private static void VisitElement(IDictionary<string, string?> data, XmlElement element, string? parentPath, bool isRoot)
    {
        // Group same-named sibling element children for array detection.
        var childGroups = new Dictionary<string, List<XmlElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (XmlNode node in element.ChildNodes)
        {
            if (node is XmlElement childElem)
            {
                var name = childElem.LocalName;
                if (!childGroups.TryGetValue(name, out var list)) { list = new List<XmlElement>(); childGroups[name] = list; }
                list.Add(childElem);
            }
        }

        // Attributes on this element become "<currentPath>:<attrName>".
        if (!isRoot)
        {
            foreach (XmlAttribute attr in element.Attributes)
            {
                if (attr.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;
                var path = parentPath == null ? attr.LocalName : ConfigurationPath.Combine(parentPath, attr.LocalName);
                data[path] = attr.Value;
            }
        }

        foreach (var (name, list) in childGroups)
        {
            var basePath = isRoot ? name : (parentPath == null ? name : ConfigurationPath.Combine(parentPath, name));
            if (list.Count == 1)
            {
                var child = list[0];
                ProcessElement(data, child, basePath);
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var path = ConfigurationPath.Combine(basePath, i.ToString());
                    ProcessElement(data, list[i], path);
                }
            }
        }

        // Text content on a leaf element gets stored at the parent path.
        if (!isRoot && childGroups.Count == 0)
        {
            var text = element.InnerText;
            if (!string.IsNullOrEmpty(text) && parentPath != null) data[parentPath] = text;
        }
    }

    private static void ProcessElement(IDictionary<string, string?> data, XmlElement element, string path)
    {
        // Leaf with no element children and no attributes → text value.
        bool hasChildren = false, hasAttributes = false;
        foreach (XmlNode n in element.ChildNodes) if (n is XmlElement) { hasChildren = true; break; }
        foreach (XmlAttribute a in element.Attributes)
        {
            if (a.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;
            hasAttributes = true; break;
        }

        if (!hasChildren && !hasAttributes)
        {
            data[path] = element.InnerText;
            return;
        }

        foreach (XmlAttribute a in element.Attributes)
        {
            if (a.NamespaceURI == "http://www.w3.org/2000/xmlns/") continue;
            data[ConfigurationPath.Combine(path, a.LocalName)] = a.Value;
        }

        if (hasChildren) VisitElement(data, element, path, isRoot: false);
        else if (!string.IsNullOrEmpty(element.InnerText)) data[path] = element.InnerText;
    }
}

public static class XmlConfigurationExtensions
{
    public static IConfigurationBuilder AddXmlFile(this IConfigurationBuilder builder, string path)
        => builder.AddXmlFile(path, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddXmlFile(this IConfigurationBuilder builder, string path, bool optional)
        => builder.AddXmlFile(path, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddXmlFile(this IConfigurationBuilder builder, string path, bool optional, bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        return builder.Add(new XmlConfigurationSource { Path = path, Optional = optional, ReloadOnChange = reloadOnChange });
    }

    public static IConfigurationBuilder AddXmlStream(this IConfigurationBuilder builder, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return builder.Add(new XmlStreamConfigurationSource { Stream = stream });
    }
}

public sealed class XmlStreamConfigurationSource : IConfigurationSource
{
    public Stream Stream { get; set; } = null!;
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new XmlStreamConfigurationProvider(this);
}

internal sealed class XmlStreamConfigurationProvider : ConfigurationProvider
{
    private readonly XmlStreamConfigurationSource _source;
    public XmlStreamConfigurationProvider(XmlStreamConfigurationSource source) { _source = source; }
    public override void Load()
    {
        var tmpSrc = new XmlConfigurationSource();
        var provider = new XmlConfigurationProvider(tmpSrc);
        provider.Load(_source.Stream);
        Data = provider.InternalData;
        OnReload();
    }
}
