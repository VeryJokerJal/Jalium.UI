using System.Collections.ObjectModel;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Jalium.UI.Documents;

namespace Jalium.UI.Annotations;

/// <summary>Base class for annotation content locators.</summary>
public abstract class ContentLocatorBase
{
    protected ContentLocatorBase()
    {
    }

    public abstract object Clone();
}
/// <summary>Identifies one typed step in a content locator.</summary>
public sealed class ContentLocatorPart
{
    private readonly Dictionary<string, string> _nameValuePairs = new(StringComparer.Ordinal);

    public ContentLocatorPart(XmlQualifiedName partType)
    {
        PartType = partType ?? throw new ArgumentNullException(nameof(partType));
        if (partType.IsEmpty)
        {
            throw new ArgumentException("The locator part type must have a local name.", nameof(partType));
        }
    }

    public IDictionary<string, string> NameValuePairs => _nameValuePairs;
    public XmlQualifiedName PartType { get; }

    public object Clone()
    {
        var clone = new ContentLocatorPart(new XmlQualifiedName(PartType.Name, PartType.Namespace));
        foreach (var pair in _nameValuePairs)
        {
            clone._nameValuePairs.Add(pair.Key, pair.Value);
        }
        return clone;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not ContentLocatorPart other || !PartType.Equals(other.PartType) ||
            _nameValuePairs.Count != other._nameValuePairs.Count)
        {
            return false;
        }

        foreach (var pair in _nameValuePairs)
        {
            if (!other._nameValuePairs.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PartType);
        foreach (var pair in _nameValuePairs.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Represents an ordered path to annotated content.</summary>
public sealed class ContentLocator : ContentLocatorBase, IXmlSerializable
{
    public ContentLocator()
    {
        Parts = new Collection<ContentLocatorPart>();
    }

    public Collection<ContentLocatorPart> Parts { get; }

    public bool StartsWith(ContentLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (locator.Parts.Count > Parts.Count)
        {
            return false;
        }

        for (var index = 0; index < locator.Parts.Count; index++)
        {
            if (!Parts[index].Equals(locator.Parts[index]))
            {
                return false;
            }
        }
        return true;
    }

    public override object Clone()
    {
        var clone = new ContentLocator();
        foreach (var part in Parts)
        {
            clone.Parts.Add((ContentLocatorPart)part.Clone());
        }
        return clone;
    }

    public XmlSchema? GetSchema() => null;

    public void WriteXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var part in Parts)
        {
            writer.WriteStartElement("ContentLocatorPart", Annotation.CoreNamespace);
            writer.WriteAttributeString("PartTypeName", part.PartType.Name);
            writer.WriteAttributeString("PartTypeNamespace", part.PartType.Namespace);
            foreach (var pair in part.NameValuePairs)
            {
                writer.WriteStartElement("Item", Annotation.CoreNamespace);
                writer.WriteAttributeString("Name", pair.Key);
                writer.WriteAttributeString("Value", pair.Value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    public void ReadXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.MoveToContent();
        Parts.Clear();
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (reader.LocalName != "ContentLocatorPart")
            {
                reader.Skip();
                continue;
            }

            var part = new ContentLocatorPart(new XmlQualifiedName(
                reader.GetAttribute("PartTypeName") ?? "Part",
                reader.GetAttribute("PartTypeNamespace") ?? Annotation.BaseNamespace));
            if (reader.IsEmptyElement)
            {
                reader.ReadStartElement();
                Parts.Add(part);
                continue;
            }

            reader.ReadStartElement();
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.LocalName == "Item")
                {
                    var name = reader.GetAttribute("Name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        part.NameValuePairs[name] = reader.GetAttribute("Value") ?? string.Empty;
                    }
                    reader.Skip();
                }
                else
                {
                    reader.Skip();
                }
            }
            reader.ReadEndElement();
            Parts.Add(part);
        }
        reader.ReadEndElement();
    }
}

/// <summary>Represents a set of equivalent paths to annotated content.</summary>
public sealed class ContentLocatorGroup : ContentLocatorBase, IXmlSerializable
{
    public ContentLocatorGroup()
    {
        Locators = new Collection<ContentLocator>();
    }

    public Collection<ContentLocator> Locators { get; }

    public override object Clone()
    {
        var clone = new ContentLocatorGroup();
        foreach (var locator in Locators)
        {
            clone.Locators.Add((ContentLocator)locator.Clone());
        }
        return clone;
    }

    public XmlSchema? GetSchema() => null;

    public void WriteXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        foreach (var locator in Locators)
        {
            writer.WriteStartElement("ContentLocator", Annotation.CoreNamespace);
            locator.WriteXml(writer);
            writer.WriteEndElement();
        }
    }

    public void ReadXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.MoveToContent();
        Locators.Clear();
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (reader.LocalName == "ContentLocator")
            {
                var locator = new ContentLocator();
                locator.ReadXml(reader);
                Locators.Add(locator);
            }
            else
            {
                reader.Skip();
            }
        }
        reader.ReadEndElement();
    }
}

/// <summary>Exposes the annotation, anchor resource, and resolved content for an anchor.</summary>
public interface IAnchorInfo
{
    Annotation Annotation { get; }
    AnnotationResource Anchor { get; }
    object? ResolvedAnchor { get; }
}

/// <summary>Represents the bounding positions of a resolved text anchor.</summary>
public sealed class TextAnchor
{
    internal TextAnchor(ContentPosition boundingStart, ContentPosition boundingEnd)
    {
        BoundingStart = boundingStart ?? throw new ArgumentNullException(nameof(boundingStart));
        BoundingEnd = boundingEnd ?? throw new ArgumentNullException(nameof(boundingEnd));
    }

    public ContentPosition BoundingStart { get; }
    public ContentPosition BoundingEnd { get; }

    public override bool Equals(object? obj) =>
        obj is TextAnchor other &&
        Equals(BoundingStart, other.BoundingStart) &&
        Equals(BoundingEnd, other.BoundingEnd);

    public override int GetHashCode() => HashCode.Combine(BoundingStart, BoundingEnd);
}
