using System.Collections.ObjectModel;
using System.Text;
using System.Xml;

namespace Jalium.UI.Annotations.Storage;

/// <summary>Describes a change to the annotations contained by a store.</summary>
public enum StoreContentAction
{
    Added,
    Deleted,
}

/// <summary>Handles changes to the annotations contained by a store.</summary>
public delegate void StoreContentChangedEventHandler(object sender, StoreContentChangedEventArgs e);

/// <summary>Provides data for an annotation store content change.</summary>
public class StoreContentChangedEventArgs : EventArgs
{
    public StoreContentChangedEventArgs(StoreContentAction action, Annotation annotation)
    {
        Annotation = annotation ?? throw new ArgumentNullException(nameof(annotation));
        Action = action;
    }

    public Annotation Annotation { get; }
    public StoreContentAction Action { get; }
}

/// <summary>Defines persistence and query operations for annotations.</summary>
public abstract class AnnotationStore : IDisposable
{
    private bool _isDisposed;

    protected AnnotationStore()
    {
        SyncRoot = new object();
    }

    public abstract bool AutoFlush { get; set; }
    protected object SyncRoot { get; }
    protected bool IsDisposed => _isDisposed;

    public event StoreContentChangedEventHandler? StoreContentChanged;
    public event AnnotationAuthorChangedEventHandler? AuthorChanged;
    public event AnnotationResourceChangedEventHandler? AnchorChanged;
    public event AnnotationResourceChangedEventHandler? CargoChanged;

    public abstract void AddAnnotation(Annotation annotation);
    public abstract Annotation? DeleteAnnotation(Guid annotationId);
    public abstract IList<Annotation> GetAnnotations(ContentLocator anchorLocator);
    public abstract IList<Annotation> GetAnnotations();
    public abstract Annotation? GetAnnotation(Guid annotationId);
    public abstract void Flush();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
        }
    }

    protected virtual void OnAuthorChanged(AnnotationAuthorChangedEventArgs args) =>
        AuthorChanged?.Invoke(this, args);

    protected virtual void OnAnchorChanged(AnnotationResourceChangedEventArgs args) =>
        AnchorChanged?.Invoke(this, args);

    protected virtual void OnCargoChanged(AnnotationResourceChangedEventArgs args) =>
        CargoChanged?.Invoke(this, args);

    protected virtual void OnStoreContentChanged(StoreContentChangedEventArgs e) =>
        StoreContentChanged?.Invoke(this, e);

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}

/// <summary>Stores annotations in a readable and writable XML stream.</summary>
public sealed class XmlStreamStore : AnnotationStore
{
    private static readonly ReadOnlyCollection<Uri> KnownNamespaceList = Array.AsReadOnly(
        new[]
        {
            new Uri(Annotation.CoreNamespace),
            new Uri(Annotation.BaseNamespace),
            new Uri("http://schemas.microsoft.com/winfx/2006/xaml/presentation"),
        });

    private readonly Stream _stream;
    private readonly Dictionary<Guid, Annotation> _annotations = new();
    private readonly Dictionary<Uri, IList<Uri>> _compatibleNamespaces;
    private readonly List<Uri> _ignoredNamespaces = new();
    private bool _autoFlush;

    public XmlStreamStore(Stream stream)
        : this(stream, new Dictionary<Uri, IList<Uri>>())
    {
    }

    public XmlStreamStore(Stream stream, IDictionary<Uri, IList<Uri>> knownNamespaces)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        ArgumentNullException.ThrowIfNull(knownNamespaces);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The annotation stream must be readable.", nameof(stream));
        }

        _compatibleNamespaces = new Dictionary<Uri, IList<Uri>>();
        foreach (var pair in knownNamespaces)
        {
            ArgumentNullException.ThrowIfNull(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            _compatibleNamespaces[pair.Key] = new ReadOnlyCollection<Uri>(pair.Value.ToArray());
        }

        Load();
    }

    public override bool AutoFlush
    {
        get
        {
            lock (SyncRoot)
            {
                ThrowIfDisposed();
                return _autoFlush;
            }
        }
        set
        {
            lock (SyncRoot)
            {
                ThrowIfDisposed();
                _autoFlush = value;
            }
        }
    }

    public IList<Uri> IgnoredNamespaces => _ignoredNamespaces;
    public static IList<Uri> WellKnownNamespaces => KnownNamespaceList;

    public static IList<Uri> GetWellKnownCompatibleNamespaces(Uri name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Array.Empty<Uri>();
    }

    public override void AddAnnotation(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            if (!_annotations.TryAdd(annotation.Id, annotation))
            {
                throw new ArgumentException($"An annotation with id '{annotation.Id}' already exists.", nameof(annotation));
            }

            Subscribe(annotation);
            OnStoreContentChanged(new StoreContentChangedEventArgs(StoreContentAction.Added, annotation));
            FlushWhenRequested();
        }
    }

    public override Annotation? DeleteAnnotation(Guid annotationId)
    {
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            if (!_annotations.Remove(annotationId, out var annotation))
            {
                return null;
            }

            Unsubscribe(annotation);
            OnStoreContentChanged(new StoreContentChangedEventArgs(StoreContentAction.Deleted, annotation));
            FlushWhenRequested();
            return annotation;
        }
    }

    public override IList<Annotation> GetAnnotations(ContentLocator anchorLocator)
    {
        ArgumentNullException.ThrowIfNull(anchorLocator);
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            return Array.AsReadOnly(_annotations.Values.Where(annotation => Matches(annotation, anchorLocator)).ToArray());
        }
    }

    public override IList<Annotation> GetAnnotations()
    {
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            return Array.AsReadOnly(_annotations.Values.ToArray());
        }
    }

    public override Annotation? GetAnnotation(Guid annotationId)
    {
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            return _annotations.GetValueOrDefault(annotationId);
        }
    }

    public override void Flush()
    {
        lock (SyncRoot)
        {
            ThrowIfDisposed();
            if (!_stream.CanWrite)
            {
                throw new InvalidOperationException("The annotation stream is not writable.");
            }
            if (!_stream.CanSeek)
            {
                throw new InvalidOperationException("The annotation stream must be seekable to flush changes.");
            }

            _stream.Position = 0;
            _stream.SetLength(0);
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                CloseOutput = false,
            };
            using (var writer = XmlWriter.Create(_stream, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("Annotations", Annotation.CoreNamespace);
                foreach (var annotation in _annotations.Values)
                {
                    writer.WriteStartElement("Annotation", Annotation.CoreNamespace);
                    annotation.WriteXml(writer);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            _stream.Flush();
            _stream.Position = 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            lock (SyncRoot)
            {
                if (_autoFlush && _stream.CanWrite && _stream.CanSeek)
                {
                    Flush();
                }
                foreach (var annotation in _annotations.Values)
                {
                    Unsubscribe(annotation);
                }
                _annotations.Clear();
            }
        }
        base.Dispose(disposing);
    }

    private void Load()
    {
        lock (SyncRoot)
        {
            if (_stream.CanSeek)
            {
                if (_stream.Length == 0)
                {
                    return;
                }
                _stream.Position = 0;
            }

            var settings = new XmlReaderSettings
            {
                CloseInput = false,
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
            };
            using var reader = XmlReader.Create(_stream, settings);
            reader.MoveToContent();
            if (reader.LocalName != "Annotations")
            {
                throw new XmlException("The stream does not contain an annotation store document.");
            }
            if (reader.IsEmptyElement)
            {
                reader.ReadStartElement();
                return;
            }

            reader.ReadStartElement();
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.LocalName == "Annotation")
                {
                    var annotation = new Annotation();
                    annotation.ReadXml(reader);
                    if (!_annotations.TryAdd(annotation.Id, annotation))
                    {
                        throw new XmlException($"Duplicate annotation id '{annotation.Id}'.");
                    }
                    Subscribe(annotation);
                }
                else
                {
                    var namespaceUri = reader.NamespaceURI;
                    if (Uri.TryCreate(namespaceUri, UriKind.Absolute, out var ignored) &&
                        !IsSupportedNamespace(ignored) &&
                        !_ignoredNamespaces.Contains(ignored))
                    {
                        _ignoredNamespaces.Add(ignored);
                    }
                    reader.Skip();
                }
            }
            reader.ReadEndElement();
            if (_stream.CanSeek)
            {
                _stream.Position = 0;
            }
        }
    }

    private static bool Matches(Annotation annotation, ContentLocator requested)
    {
        foreach (var anchor in annotation.Anchors)
        {
            foreach (var locatorBase in anchor.ContentLocators)
            {
                if (locatorBase is ContentLocator locator &&
                    (locator.StartsWith(requested) || requested.StartsWith(locator)))
                {
                    return true;
                }
                if (locatorBase is ContentLocatorGroup group && group.Locators.Any(
                    locator => locator.StartsWith(requested) || requested.StartsWith(locator)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private bool IsSupportedNamespace(Uri namespaceUri)
    {
        if (KnownNamespaceList.Contains(namespaceUri) || _compatibleNamespaces.ContainsKey(namespaceUri))
        {
            return true;
        }

        return _compatibleNamespaces.Values.Any(values => values.Contains(namespaceUri));
    }

    private void Subscribe(Annotation annotation)
    {
        annotation.AuthorChanged += HandleAuthorChanged;
        annotation.AnchorChanged += HandleAnchorChanged;
        annotation.CargoChanged += HandleCargoChanged;
    }

    private void Unsubscribe(Annotation annotation)
    {
        annotation.AuthorChanged -= HandleAuthorChanged;
        annotation.AnchorChanged -= HandleAnchorChanged;
        annotation.CargoChanged -= HandleCargoChanged;
    }

    private void HandleAuthorChanged(object sender, AnnotationAuthorChangedEventArgs e)
    {
        lock (SyncRoot)
        {
            if (IsDisposed)
            {
                return;
            }
            OnAuthorChanged(e);
            FlushWhenRequested();
        }
    }

    private void HandleAnchorChanged(object sender, AnnotationResourceChangedEventArgs e)
    {
        lock (SyncRoot)
        {
            if (IsDisposed)
            {
                return;
            }
            OnAnchorChanged(e);
            FlushWhenRequested();
        }
    }

    private void HandleCargoChanged(object sender, AnnotationResourceChangedEventArgs e)
    {
        lock (SyncRoot)
        {
            if (IsDisposed)
            {
                return;
            }
            OnCargoChanged(e);
            FlushWhenRequested();
        }
    }

    private void FlushWhenRequested()
    {
        if (_autoFlush)
        {
            Flush();
        }
    }
}
