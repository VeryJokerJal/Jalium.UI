using System.Xml.Linq;

namespace Jalium.UI.Controls.Annotations;

/// <summary>
/// Represents a user annotation (sticky note or highlight).
/// </summary>
public sealed class Annotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Annotation"/> class.
    /// </summary>
    public Annotation(XName annotationType)
    {
        Id = Guid.NewGuid();
        AnnotationType = annotationType;
        CreationTime = DateTime.UtcNow;
        LastModificationTime = DateTime.UtcNow;
        Authors = new();
        Anchors = new();
        Cargos = new();
    }

    /// <summary>
    /// Initializes a new instance with a specified id.
    /// </summary>
    public Annotation(XName annotationType, Guid id, DateTime creationTime, DateTime lastModificationTime)
    {
        Id = id;
        AnnotationType = annotationType;
        CreationTime = creationTime;
        LastModificationTime = lastModificationTime;
        Authors = new();
        Anchors = new();
        Cargos = new();
    }

    /// <summary>
    /// Gets the globally unique identifier of this annotation.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the type name of this annotation.
    /// </summary>
    public XName AnnotationType { get; }

    /// <summary>
    /// Gets the date and time the annotation was created.
    /// </summary>
    public DateTime CreationTime { get; }

    /// <summary>
    /// Gets the date and time the annotation was last modified.
    /// </summary>
    public DateTime LastModificationTime { get; internal set; }

    /// <summary>
    /// Gets the collection of authors.
    /// </summary>
    public List<string> Authors { get; }

    /// <summary>
    /// Gets the collection of anchors.
    /// </summary>
    public List<AnnotationResource> Anchors { get; }

    /// <summary>
    /// Gets the collection of cargos (content).
    /// </summary>
    public List<AnnotationResource> Cargos { get; }

    /// <summary>
    /// Occurs when the annotation is modified.
    /// </summary>
    public event EventHandler? AnchorChanged;

    /// <summary>
    /// Occurs when cargo content changes.
    /// </summary>
    public event EventHandler? CargoChanged;

    /// <summary>
    /// Occurs when authors change.
    /// </summary>
    public event EventHandler? AuthorChanged;

    internal void OnAnchorChanged() => AnchorChanged?.Invoke(this, EventArgs.Empty);
    internal void OnCargoChanged() => CargoChanged?.Invoke(this, EventArgs.Empty);
    internal void OnAuthorChanged() => AuthorChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents a resource (anchor or cargo) in an annotation.
/// </summary>
public sealed class AnnotationResource
{
    public AnnotationResource() { Id = Guid.NewGuid(); Contents = new(); }
    public AnnotationResource(string name) { Id = Guid.NewGuid(); Name = name; Contents = new(); }

    /// <summary>
    /// Gets the id.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets the XML content elements.
    /// </summary>
    public List<XElement> Contents { get; }
}

/// <summary>
/// Manages the creation, deletion, and querying of annotations.
/// </summary>
public sealed class AnnotationService : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly List<Annotation> _annotations = new();
    private bool _isEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnnotationService"/> class.
    /// </summary>
    public AnnotationService(FrameworkElement serviceRoot)
    {
        _root = serviceRoot;
    }

    /// <summary>
    /// Gets a value indicating whether the service is enabled.
    /// </summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>
    /// Enables annotations on the associated element.
    /// </summary>
    public void Enable(AnnotationStore store)
    {
        Store = store;
        _isEnabled = true;
        // Load existing annotations from store
        foreach (var ann in store.GetAnnotations())
            _annotations.Add(ann);
    }

    /// <summary>
    /// Disables annotations.
    /// </summary>
    public void Disable()
    {
        _isEnabled = false;
        _annotations.Clear();
    }

    /// <summary>
    /// Gets the annotation store.
    /// </summary>
    public AnnotationStore? Store { get; private set; }

    /// <summary>
    /// Gets the AnnotationService for the specified element.
    /// </summary>
    public static AnnotationService? GetService(FrameworkElement element)
    {
        // In a full implementation, this would be stored as an attached property
        return null;
    }

    /// <summary>
    /// Creates a text sticky note annotation.
    /// </summary>
    public Annotation CreateTextStickyNoteForSelection()
    {
        var ann = new Annotation(XName.Get("TextStickyNote"));
        _annotations.Add(ann);
        Store?.AddAnnotation(ann);
        return ann;
    }

    /// <summary>
    /// Creates a highlight annotation.
    /// </summary>
    public Annotation CreateHighlightForSelection()
    {
        var ann = new Annotation(XName.Get("Highlight"));
        _annotations.Add(ann);
        Store?.AddAnnotation(ann);
        return ann;
    }

    /// <summary>
    /// Deletes the specified annotation.
    /// </summary>
    public void DeleteAnnotation(Guid annotationId)
    {
        var ann = _annotations.Find(a => a.Id == annotationId);
        if (ann != null)
        {
            _annotations.Remove(ann);
            Store?.DeleteAnnotation(annotationId);
        }
    }

    /// <summary>
    /// Gets all annotations.
    /// </summary>
    public IReadOnlyList<Annotation> GetAnnotations() => _annotations.AsReadOnly();

    /// <inheritdoc />
    public void Dispose()
    {
        Disable();
    }
}

/// <summary>
/// Abstract base class for annotation storage.
/// </summary>
public abstract class AnnotationStore : IDisposable
{
    /// <summary>
    /// Gets the annotations in this store.
    /// </summary>
    public abstract IList<Annotation> GetAnnotations();

    /// <summary>
    /// Gets an annotation by its id.
    /// </summary>
    public abstract Annotation? GetAnnotation(Guid annotationId);

    /// <summary>
    /// Adds an annotation.
    /// </summary>
    public abstract void AddAnnotation(Annotation annotation);

    /// <summary>
    /// Deletes an annotation.
    /// </summary>
    public abstract void DeleteAnnotation(Guid annotationId);

    /// <summary>
    /// Flushes changes to storage.
    /// </summary>
    public abstract void Flush();

    /// <summary>
    /// Occurs when an annotation is added.
    /// </summary>
    public event EventHandler<AnnotationStoreChangedEventArgs>? StoreContentChanged;

    /// <summary>
    /// Raises the StoreContentChanged event.
    /// </summary>
    protected void OnStoreContentChanged(AnnotationStoreChangedEventArgs e) => StoreContentChanged?.Invoke(this, e);

    /// <inheritdoc />
    public virtual void Dispose() { Flush(); }
}

/// <summary>
/// Stores annotations in an XML stream.
/// </summary>
public sealed class XmlStreamStore : AnnotationStore
{
    private readonly List<Annotation> _annotations = new();

    /// <summary>
    /// Initializes a new instance with the specified stream.
    /// </summary>
    public XmlStreamStore(Stream stream)
    {
        Stream = stream;
    }

    /// <summary>
    /// Gets the underlying stream.
    /// </summary>
    public Stream Stream { get; }

    /// <summary>
    /// Gets or sets whether auto-flush is enabled.
    /// </summary>
    public bool AutoFlush { get; set; }

    /// <inheritdoc />
    public override IList<Annotation> GetAnnotations() => _annotations.AsReadOnly();

    /// <inheritdoc />
    public override Annotation? GetAnnotation(Guid annotationId) => _annotations.Find(a => a.Id == annotationId);

    /// <inheritdoc />
    public override void AddAnnotation(Annotation annotation)
    {
        _annotations.Add(annotation);
        OnStoreContentChanged(new AnnotationStoreChangedEventArgs(AnnotationAction.Added, annotation));
        if (AutoFlush) Flush();
    }

    /// <inheritdoc />
    public override void DeleteAnnotation(Guid annotationId)
    {
        var ann = _annotations.Find(a => a.Id == annotationId);
        if (ann != null)
        {
            _annotations.Remove(ann);
            OnStoreContentChanged(new AnnotationStoreChangedEventArgs(AnnotationAction.Deleted, ann));
            if (AutoFlush) Flush();
        }
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Serialize annotations to the XML stream
    }
}

/// <summary>
/// Provides data for the StoreContentChanged event.
/// </summary>
public sealed class AnnotationStoreChangedEventArgs : EventArgs
{
    public AnnotationStoreChangedEventArgs(AnnotationAction action, Annotation annotation)
    {
        Action = action;
        Annotation = annotation;
    }

    /// <summary>
    /// Gets the action that occurred.
    /// </summary>
    public AnnotationAction Action { get; }

    /// <summary>
    /// Gets the annotation involved.
    /// </summary>
    public Annotation Annotation { get; }
}

/// <summary>
/// Specifies the annotation store action.
/// </summary>
public enum AnnotationAction
{
    /// <summary>
    /// An annotation was added.
    /// </summary>
    Added,

    /// <summary>
    /// An annotation was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// An annotation was modified.
    /// </summary>
    Modified
}
