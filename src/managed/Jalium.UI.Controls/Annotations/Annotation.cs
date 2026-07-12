using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Jalium.UI.Annotations;

/// <summary>Describes a change to an annotation author or resource collection.</summary>
public enum AnnotationAction
{
    Added,
    Removed,
    Modified,
}

/// <summary>Handles changes to an annotation's authors.</summary>
public delegate void AnnotationAuthorChangedEventHandler(object sender, AnnotationAuthorChangedEventArgs e);

/// <summary>Handles changes to an annotation's anchors or cargos.</summary>
public delegate void AnnotationResourceChangedEventHandler(object sender, AnnotationResourceChangedEventArgs e);

/// <summary>Provides data for an annotation author change.</summary>
public sealed class AnnotationAuthorChangedEventArgs : EventArgs
{
    public AnnotationAuthorChangedEventArgs(Annotation annotation, AnnotationAction action, object author)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Author = author ?? throw new ArgumentNullException(nameof(author));
        Action = action;
    }

    public Annotation Annotation { get; }
    public object Author { get; }
    public AnnotationAction Action { get; }
}

/// <summary>Provides data for an annotation resource change.</summary>
public sealed class AnnotationResourceChangedEventArgs : EventArgs
{
    public AnnotationResourceChangedEventArgs(
        Annotation annotation,
        AnnotationAction action,
        AnnotationResource resource)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
        Action = action;
    }

    public Annotation Annotation { get; }
    public AnnotationResource Resource { get; }
    public AnnotationAction Action { get; }
}

/// <summary>Represents a serializable annotation with authors, anchors, and cargo resources.</summary>
public sealed class Annotation : IXmlSerializable
{
    internal const string CoreNamespace = "http://schemas.microsoft.com/windows/annotations/2003/11/core";
    internal const string BaseNamespace = "http://schemas.microsoft.com/windows/annotations/2003/11/base";

    private readonly ChangeCollection<string> _authors;
    private readonly ChangeCollection<AnnotationResource> _anchors;
    private readonly ChangeCollection<AnnotationResource> _cargos;

    public Annotation()
    {
        AnnotationType = XmlQualifiedName.Empty;
        Id = Guid.Empty;
        CreationTime = DateTime.MinValue;
        LastModificationTime = DateTime.MinValue;
        _authors = new ChangeCollection<string>(OnAuthorCollectionChanged);
        _anchors = new ChangeCollection<AnnotationResource>(OnAnchorCollectionChanged);
        _cargos = new ChangeCollection<AnnotationResource>(OnCargoCollectionChanged);
    }

    public Annotation(XmlQualifiedName annotationType)
        : this(annotationType, Guid.NewGuid(), DateTime.Now, DateTime.Now)
    {
    }

    public Annotation(
        XmlQualifiedName annotationType,
        Guid id,
        DateTime creationTime,
        DateTime lastModificationTime)
    {
        AnnotationType = annotationType ?? throw new ArgumentNullException(nameof(annotationType));
        if (annotationType.IsEmpty)
        {
            throw new ArgumentException("The annotation type must have a local name.", nameof(annotationType));
        }
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Guid.Empty is not a valid annotation id.", nameof(id));
        }
        if (lastModificationTime < creationTime)
        {
            throw new ArgumentException("Last modification time cannot be earlier than creation time.", nameof(lastModificationTime));
        }

        Id = id;
        CreationTime = creationTime;
        LastModificationTime = lastModificationTime;
        _authors = new ChangeCollection<string>(OnAuthorCollectionChanged);
        _anchors = new ChangeCollection<AnnotationResource>(OnAnchorCollectionChanged);
        _cargos = new ChangeCollection<AnnotationResource>(OnCargoCollectionChanged);
    }

    public Guid Id { get; private set; }
    public XmlQualifiedName AnnotationType { get; private set; }
    public DateTime CreationTime { get; private set; }
    public DateTime LastModificationTime { get; private set; }
    public Collection<string> Authors => _authors;
    public Collection<AnnotationResource> Anchors => _anchors;
    public Collection<AnnotationResource> Cargos => _cargos;

    public event AnnotationAuthorChangedEventHandler? AuthorChanged;
    public event AnnotationResourceChangedEventHandler? AnchorChanged;
    public event AnnotationResourceChangedEventHandler? CargoChanged;

    public XmlSchema? GetSchema() => null;

    public void WriteXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteAttributeString("Id", XmlConvert.ToString(Id));
        writer.WriteAttributeString("CreationTime", XmlConvert.ToString(CreationTime, XmlDateTimeSerializationMode.RoundtripKind));
        writer.WriteAttributeString("LastModificationTime", XmlConvert.ToString(LastModificationTime, XmlDateTimeSerializationMode.RoundtripKind));
        writer.WriteAttributeString("TypeName", AnnotationType.Name);
        writer.WriteAttributeString("TypeNamespace", AnnotationType.Namespace);

        writer.WriteStartElement("Authors", CoreNamespace);
        foreach (var author in Authors)
        {
            writer.WriteElementString("Author", CoreNamespace, author);
        }
        writer.WriteEndElement();

        WriteResources(writer, "Anchors", Anchors);
        WriteResources(writer, "Cargos", Cargos);
    }

    public void ReadXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.MoveToContent();

        Id = ParseGuid(reader.GetAttribute("Id"), Guid.NewGuid());
        CreationTime = ParseDateTime(reader.GetAttribute("CreationTime"), DateTime.Now);
        LastModificationTime = ParseDateTime(reader.GetAttribute("LastModificationTime"), CreationTime);
        var typeName = reader.GetAttribute("TypeName") ?? "Annotation";
        var typeNamespace = reader.GetAttribute("TypeNamespace") ?? CoreNamespace;
        AnnotationType = new XmlQualifiedName(typeName, typeNamespace);

        foreach (var resource in _anchors)
        {
            resource.Changed -= OnAnchorResourceChanged;
        }
        foreach (var resource in _cargos)
        {
            resource.Changed -= OnCargoResourceChanged;
        }

        _authors.ClearWithoutNotifications();
        _anchors.ClearWithoutNotifications();
        _cargos.ClearWithoutNotifications();

        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            switch (reader.LocalName)
            {
                case "Authors":
                    ReadAuthors(reader);
                    break;
                case "Anchors":
                    ReadResources(reader, _anchors);
                    break;
                case "Cargos":
                    ReadResources(reader, _cargos);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        reader.ReadEndElement();
        foreach (var resource in _anchors)
        {
            resource.Changed += OnAnchorResourceChanged;
        }
        foreach (var resource in _cargos)
        {
            resource.Changed += OnCargoResourceChanged;
        }
    }

    private static void WriteResources(
        XmlWriter writer,
        string containerName,
        Collection<AnnotationResource> resources)
    {
        writer.WriteStartElement(containerName, CoreNamespace);
        foreach (var resource in resources)
        {
            writer.WriteStartElement("Resource", CoreNamespace);
            resource.WriteXml(writer);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private void ReadAuthors(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (reader.LocalName == "Author")
            {
                _authors.AddWithoutNotification(reader.ReadElementContentAsString());
            }
            else
            {
                reader.Skip();
            }
        }
        reader.ReadEndElement();
    }

    private static void ReadResources(XmlReader reader, ChangeCollection<AnnotationResource> target)
    {
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (reader.LocalName == "Resource")
            {
                var resource = new AnnotationResource();
                resource.ReadXml(reader);
                target.AddWithoutNotification(resource);
            }
            else
            {
                reader.Skip();
            }
        }
        reader.ReadEndElement();
    }

    private void OnAuthorCollectionChanged(AnnotationAction action, string item)
    {
        Touch();
        AuthorChanged?.Invoke(this, new AnnotationAuthorChangedEventArgs(this, action, item));
    }

    private void OnAnchorCollectionChanged(AnnotationAction action, AnnotationResource item)
    {
        if (action == AnnotationAction.Added)
        {
            item.Changed += OnAnchorResourceChanged;
        }
        else if (action == AnnotationAction.Removed)
        {
            item.Changed -= OnAnchorResourceChanged;
        }
        Touch();
        AnchorChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, action, item));
    }

    private void OnCargoCollectionChanged(AnnotationAction action, AnnotationResource item)
    {
        if (action == AnnotationAction.Added)
        {
            item.Changed += OnCargoResourceChanged;
        }
        else if (action == AnnotationAction.Removed)
        {
            item.Changed -= OnCargoResourceChanged;
        }
        Touch();
        CargoChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, action, item));
    }

    private void OnAnchorResourceChanged(object? sender, EventArgs e)
    {
        if (sender is AnnotationResource resource)
        {
            Touch();
            AnchorChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, AnnotationAction.Modified, resource));
        }
    }

    private void OnCargoResourceChanged(object? sender, EventArgs e)
    {
        if (sender is AnnotationResource resource)
        {
            Touch();
            CargoChanged?.Invoke(this, new AnnotationResourceChangedEventArgs(this, AnnotationAction.Modified, resource));
        }
    }

    private void Touch() => LastModificationTime = DateTime.Now;

    private static Guid ParseGuid(string? text, Guid fallback) =>
        Guid.TryParse(text, out var result) ? result : fallback;

    private static DateTime ParseDateTime(string? text, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        try
        {
            return XmlConvert.ToDateTime(text, XmlDateTimeSerializationMode.RoundtripKind);
        }
        catch (FormatException)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : fallback;
        }
    }
}

/// <summary>Represents a serializable anchor or cargo resource.</summary>
public sealed class AnnotationResource : IXmlSerializable
{
    private readonly ChangeCollection<ContentLocatorBase> _contentLocators;
    private readonly ChangeCollection<XmlElement> _contents;

    public AnnotationResource()
        : this(Guid.NewGuid())
    {
    }

    public AnnotationResource(string name)
        : this()
    {
        Name = name;
    }

    public AnnotationResource(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Guid.Empty is not a valid annotation resource id.", nameof(id));
        }
        Id = id;
        _contentLocators = new ChangeCollection<ContentLocatorBase>(OnChanged);
        _contents = new ChangeCollection<XmlElement>(OnChanged);
    }

    public Guid Id { get; private set; }

    public string? Name
    {
        get => _name;
        set
        {
            if (!string.Equals(_name, value, StringComparison.Ordinal))
            {
                _name = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Collection<ContentLocatorBase> ContentLocators => _contentLocators;
    public Collection<XmlElement> Contents => _contents;

    internal event EventHandler? Changed;

    private string? _name;

    public XmlSchema? GetSchema() => null;

    public void WriteXml(XmlWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteAttributeString("Id", XmlConvert.ToString(Id));
        if (Name is not null)
        {
            writer.WriteAttributeString("Name", Name);
        }

        writer.WriteStartElement("ContentLocators", Annotation.CoreNamespace);
        foreach (var locator in ContentLocators)
        {
            var elementName = locator is ContentLocatorGroup ? "ContentLocatorGroup" : "ContentLocator";
            writer.WriteStartElement(elementName, Annotation.CoreNamespace);
            if (locator is IXmlSerializable serializable)
            {
                serializable.WriteXml(writer);
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("Contents", Annotation.CoreNamespace);
        foreach (var content in Contents)
        {
            content.WriteTo(writer);
        }
        writer.WriteEndElement();
    }

    public void ReadXml(XmlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.MoveToContent();
        Id = Guid.TryParse(reader.GetAttribute("Id"), out var id) ? id : Guid.NewGuid();
        Name = reader.GetAttribute("Name");
        _contentLocators.ClearWithoutNotifications();
        _contents.ClearWithoutNotifications();

        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (reader.LocalName == "ContentLocators")
            {
                ReadLocators(reader);
            }
            else if (reader.LocalName == "Contents")
            {
                ReadContents(reader);
            }
            else
            {
                reader.Skip();
            }
        }
        reader.ReadEndElement();
    }

    private void ReadLocators(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            ContentLocatorBase? locator = reader.LocalName switch
            {
                "ContentLocator" => new ContentLocator(),
                "ContentLocatorGroup" => new ContentLocatorGroup(),
                _ => null,
            };

            if (locator is IXmlSerializable serializable)
            {
                serializable.ReadXml(reader);
                _contentLocators.AddWithoutNotification(locator);
            }
            else
            {
                reader.Skip();
            }
        }
        reader.ReadEndElement();
    }

    private void ReadContents(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.ReadStartElement();
            return;
        }

        var document = new XmlDocument { PreserveWhitespace = true };
        reader.ReadStartElement();
        while (reader.MoveToContent() == XmlNodeType.Element)
        {
            if (document.ReadNode(reader) is XmlElement element)
            {
                _contents.AddWithoutNotification(element);
            }
        }
        reader.ReadEndElement();
    }

    private void OnChanged<T>(AnnotationAction action, T item) => Changed?.Invoke(this, EventArgs.Empty);
}

internal sealed class ChangeCollection<T> : Collection<T>
{
    private readonly Action<AnnotationAction, T> _changed;
    private bool _suppress;

    internal ChangeCollection(Action<AnnotationAction, T> changed)
    {
        _changed = changed;
    }

    internal void AddWithoutNotification(T item)
    {
        _suppress = true;
        try
        {
            Add(item);
        }
        finally
        {
            _suppress = false;
        }
    }

    internal void ClearWithoutNotifications()
    {
        _suppress = true;
        try
        {
            Clear();
        }
        finally
        {
            _suppress = false;
        }
    }

    protected override void InsertItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        if (!_suppress)
        {
            _changed(AnnotationAction.Added, item);
        }
    }

    protected override void SetItem(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var previous = this[index];
        base.SetItem(index, item);
        if (!_suppress)
        {
            _changed(AnnotationAction.Removed, previous);
            _changed(AnnotationAction.Added, item);
        }
    }

    protected override void RemoveItem(int index)
    {
        var previous = this[index];
        base.RemoveItem(index);
        if (!_suppress)
        {
            _changed(AnnotationAction.Removed, previous);
        }
    }

    protected override void ClearItems()
    {
        var previous = this.ToArray();
        base.ClearItems();
        if (!_suppress)
        {
            foreach (var item in previous)
            {
                _changed(AnnotationAction.Removed, item);
            }
        }
    }
}
