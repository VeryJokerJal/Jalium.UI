using System.Runtime.CompilerServices;
using System.Xml;
using Jalium.UI.Annotations.Storage;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using DocumentPage = Jalium.UI.Documents.DocumentPage;
using DocumentPaginator = Jalium.UI.Documents.DocumentPaginator;
using IDocumentPaginatorSource = Jalium.UI.Documents.IDocumentPaginatorSource;

namespace Jalium.UI.Annotations;

/// <summary>Coordinates annotations for a document viewer.</summary>
public sealed partial class AnnotationService : DispatcherObject
{
    private static readonly ConditionalWeakTable<DependencyObject, AnnotationService> Services = new();

    public static readonly RoutedUICommand CreateHighlightCommand =
        new("Create Highlight", nameof(CreateHighlightCommand), typeof(AnnotationService));

    public static readonly RoutedUICommand CreateTextStickyNoteCommand =
        new("Create Text Sticky Note", nameof(CreateTextStickyNoteCommand), typeof(AnnotationService));

    public static readonly RoutedUICommand CreateInkStickyNoteCommand =
        new("Create Ink Sticky Note", nameof(CreateInkStickyNoteCommand), typeof(AnnotationService));

    public static readonly RoutedUICommand ClearHighlightsCommand =
        new("Clear Highlights", nameof(ClearHighlightsCommand), typeof(AnnotationService));

    public static readonly RoutedUICommand DeleteStickyNotesCommand =
        new("Delete Sticky Notes", nameof(DeleteStickyNotesCommand), typeof(AnnotationService));

    public static readonly RoutedUICommand DeleteAnnotationsCommand =
        new("Delete Annotations", nameof(DeleteAnnotationsCommand), typeof(AnnotationService));

    private readonly DependencyObject _serviceRoot;

    public AnnotationService(DocumentViewerBase viewer)
        : this((DependencyObject)(viewer ?? throw new ArgumentNullException(nameof(viewer))))
    {
    }

    public AnnotationService(FlowDocumentScrollViewer viewer)
        : this((DependencyObject)(viewer ?? throw new ArgumentNullException(nameof(viewer))))
    {
    }

    public AnnotationService(FlowDocumentReader viewer)
        : this((DependencyObject)(viewer ?? throw new ArgumentNullException(nameof(viewer))))
    {
    }

    private AnnotationService(DependencyObject serviceRoot)
    {
        _serviceRoot = serviceRoot;
        lock (Services)
        {
            Services.Remove(serviceRoot);
            Services.Add(serviceRoot, this);
        }
    }

    public bool IsEnabled { get; private set; }
    public AnnotationStore? Store { get; private set; }

    public static AnnotationService? GetService(DocumentViewerBase viewer) => GetRegisteredService(viewer);
    public static AnnotationService? GetService(FlowDocumentReader reader) => GetRegisteredService(reader);
    public static AnnotationService? GetService(FlowDocumentScrollViewer viewer) => GetRegisteredService(viewer);

    public void Enable(AnnotationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        VerifyAccess();
        if (IsEnabled)
        {
            throw new InvalidOperationException("The annotation service is already enabled.");
        }

        Store = store;
        IsEnabled = true;
    }

    public void Disable()
    {
        VerifyAccess();
        IsEnabled = false;
        Store = null;
    }

    internal DependencyObject ServiceRoot => _serviceRoot;

    internal IList<Annotation> GetAnnotations() => Store?.GetAnnotations() ?? Array.Empty<Annotation>();

    private static AnnotationService? GetRegisteredService(DependencyObject? serviceRoot)
    {
        ArgumentNullException.ThrowIfNull(serviceRoot);
        lock (Services)
        {
            return Services.TryGetValue(serviceRoot, out var service) ? service : null;
        }
    }
}

/// <summary>Provides common annotation operations for the current viewer selection.</summary>
public static class AnnotationHelper
{
    public static void ClearHighlightsForSelection(AnnotationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        DeleteMatching(service, static annotation => annotation.AnnotationType.Name == "Highlight");
    }

    public static Annotation CreateHighlightForSelection(AnnotationService service, string author, Brush highlightBrush)
    {
        ArgumentNullException.ThrowIfNull(highlightBrush);
        var annotation = CreateAnnotation(service, "Highlight", author);

        var document = new XmlDocument();
        var color = document.CreateElement("Highlight", Annotation.BaseNamespace);
        color.SetAttribute("Brush", highlightBrush.ToString() ?? string.Empty);
        annotation.Cargos.Add(new AnnotationResource("Highlight") { Contents = { color } });
        return annotation;
    }

    public static Annotation CreateTextStickyNoteForSelection(AnnotationService service, string author) =>
        CreateAnnotation(service, "TextStickyNote", author);

    public static Annotation CreateInkStickyNoteForSelection(AnnotationService service, string author) =>
        CreateAnnotation(service, "InkStickyNote", author);

    public static void DeleteTextStickyNotesForSelection(AnnotationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        DeleteMatching(service, static annotation => annotation.AnnotationType.Name == "TextStickyNote");
    }

    public static void DeleteInkStickyNotesForSelection(AnnotationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        DeleteMatching(service, static annotation => annotation.AnnotationType.Name == "InkStickyNote");
    }

    public static IAnchorInfo? GetAnchorInfo(AnnotationService service, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(annotation);
        var anchor = annotation.Anchors.FirstOrDefault();
        return anchor is null ? null : new AnchorInfo(annotation, anchor, service.ServiceRoot);
    }

    private static Annotation CreateAnnotation(AnnotationService service, string typeName, string author)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(author);
        if (!service.IsEnabled || service.Store is null)
        {
            throw new InvalidOperationException("The annotation service must be enabled before annotations can be created.");
        }

        var annotation = new Annotation(new XmlQualifiedName(typeName, Annotation.BaseNamespace));
        annotation.Authors.Add(author);
        var anchor = new AnnotationResource("Selection");
        var locator = new ContentLocator();
        var part = new ContentLocatorPart(new XmlQualifiedName("Viewer", Annotation.BaseNamespace));
        part.NameValuePairs["Type"] = service.ServiceRoot.GetType().FullName ?? service.ServiceRoot.GetType().Name;
        locator.Parts.Add(part);
        anchor.ContentLocators.Add(locator);
        annotation.Anchors.Add(anchor);
        service.Store.AddAnnotation(annotation);
        return annotation;
    }

    private static void DeleteMatching(AnnotationService service, Func<Annotation, bool> predicate)
    {
        if (!service.IsEnabled || service.Store is null)
        {
            throw new InvalidOperationException("The annotation service must be enabled before annotations can be deleted.");
        }

        foreach (var annotation in service.Store.GetAnnotations().Where(predicate).ToArray())
        {
            service.Store.DeleteAnnotation(annotation.Id);
        }
    }

    private sealed class AnchorInfo : IAnchorInfo
    {
        internal AnchorInfo(Annotation annotation, AnnotationResource anchor, object resolvedAnchor)
        {
            Annotation = annotation;
            Anchor = anchor;
            ResolvedAnchor = resolvedAnchor;
        }

        public Annotation Annotation { get; }
        public AnnotationResource Anchor { get; }
        public object ResolvedAnchor { get; }
    }
}

/// <summary>Wraps a paginator and carries annotation data with the pagination operation.</summary>
public sealed class AnnotationDocumentPaginator : DocumentPaginator
{
    private readonly DocumentPaginator _source;
    private readonly AnnotationStore _store;

    public AnnotationDocumentPaginator(DocumentPaginator originalPaginator, Stream annotationData)
        : this(originalPaginator, new XmlStreamStore(annotationData), FlowDirection.LeftToRight)
    {
    }

    public AnnotationDocumentPaginator(
        DocumentPaginator originalPaginator,
        Stream annotationData,
        FlowDirection flowDirection)
        : this(originalPaginator, new XmlStreamStore(annotationData), flowDirection)
    {
    }

    public AnnotationDocumentPaginator(DocumentPaginator originalPaginator, AnnotationStore annotationStore)
        : this(originalPaginator, annotationStore, FlowDirection.LeftToRight)
    {
    }

    public AnnotationDocumentPaginator(
        DocumentPaginator originalPaginator,
        AnnotationStore annotationStore,
        FlowDirection flowDirection)
    {
        _source = originalPaginator ?? throw new ArgumentNullException(nameof(originalPaginator));
        _store = annotationStore ?? throw new ArgumentNullException(nameof(annotationStore));
        FlowDirection = flowDirection;
    }

    public FlowDirection FlowDirection { get; }
    public AnnotationStore AnnotationStore => _store;
    public override bool IsPageCountValid => _source.IsPageCountValid;
    public override int PageCount => _source.PageCount;

    public override Size PageSize
    {
        get => _source.PageSize;
        set => _source.PageSize = value;
    }

    public override IDocumentPaginatorSource Source => _source.Source;

    public override DocumentPage GetPage(int pageNumber) => _source.GetPage(pageNumber);

    public override void GetPageAsync(int pageNumber, object? userState) => base.GetPageAsync(pageNumber, userState);

    public override void ComputePageCount()
    {
        _source.ComputePageCount();
    }

    public override void ComputePageCountAsync(object? userState) => base.ComputePageCountAsync(userState);

    public override void CancelAsync(object? userState)
    {
        _source.CancelAsync(userState);
    }
}
