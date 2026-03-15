using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Hosts a portable, high fidelity, fixed-format document with read access for user text selection,
/// keyboard navigation, and search.
/// </summary>
public sealed class FixedDocument : FrameworkContentElement, IDocumentPaginatorSource
{
    private readonly PageContentCollection _pages;
    private FixedDocumentPaginator? _paginator;

    /// <summary>
    /// Identifies the PrintTicket dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrintTicketProperty =
        DependencyProperty.Register(nameof(PrintTicket), typeof(object), typeof(FixedDocument),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the collection of pages.
    /// </summary>
    public PageContentCollection Pages => _pages;

    /// <summary>
    /// Gets or sets the print ticket.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? PrintTicket
    {
        get => GetValue(PrintTicketProperty);
        set => SetValue(PrintTicketProperty, value);
    }

    /// <summary>
    /// Gets the document paginator.
    /// </summary>
    public DocumentPaginator DocumentPaginator => _paginator ??= new FixedDocumentPaginator(this);

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedDocument"/> class.
    /// </summary>
    public FixedDocument()
    {
        _pages = new PageContentCollection(this);
    }
}

/// <summary>
/// Provides access to an ordered sequence of pages in a FixedDocument.
/// </summary>
public sealed class PageContentCollection : Collection<PageContent>
{
    private readonly FixedDocument _owner;

    internal PageContentCollection(FixedDocument owner)
    {
        _owner = owner;
    }

    /// <inheritdoc />
    protected override void InsertItem(int index, PageContent item)
    {
        base.InsertItem(index, item);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        base.RemoveItem(index);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, PageContent item)
    {
        base.SetItem(index, item);
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        base.ClearItems();
    }
}

/// <summary>
/// Provides information about a page that is part of a FixedDocument.
/// </summary>
public class PageContent : FrameworkElement
{
    private FixedPage? _child;
    private Uri? _source;

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(PageContent),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Gets or sets the URI of the FixedPage markup.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the FixedPage associated with this PageContent.
    /// </summary>
    public FixedPage? Child
    {
        get => _child;
        set
        {
            if (_child != value)
            {
                _child = value;
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets the page asynchronously.
    /// </summary>
    public FixedPage? GetPageRoot(bool forceReload)
    {
        if (_child == null && _source != null)
        {
            // In a real implementation, this would load the XAML from the source URI
        }
        return _child;
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var pageContent = (PageContent)d;
        pageContent._source = (Uri?)e.NewValue;
        // In a real implementation, this would trigger loading of the page
    }
}

/// <summary>
/// Provides the content for a high fidelity, fixed-format page.
/// </summary>
public class FixedPage : FrameworkElement
{
    private readonly List<UIElement> _children = new();

    /// <summary>
    /// Identifies the PrintTicket dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrintTicketProperty =
        DependencyProperty.Register(nameof(PrintTicket), typeof(object), typeof(FixedPage),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(FixedPage),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ContentBox dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentBoxProperty =
        DependencyProperty.Register(nameof(ContentBox), typeof(Rect), typeof(FixedPage),
            new PropertyMetadata(Rect.Empty));

    /// <summary>
    /// Identifies the BleedBox dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BleedBoxProperty =
        DependencyProperty.Register(nameof(BleedBox), typeof(Rect), typeof(FixedPage),
            new PropertyMetadata(Rect.Empty));

    /// <summary>
    /// Identifies the Left attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LeftProperty =
        DependencyProperty.RegisterAttached("Left", typeof(double), typeof(FixedPage),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the Top attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TopProperty =
        DependencyProperty.RegisterAttached("Top", typeof(double), typeof(FixedPage),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the Right attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RightProperty =
        DependencyProperty.RegisterAttached("Right", typeof(double), typeof(FixedPage),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the Bottom attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BottomProperty =
        DependencyProperty.RegisterAttached("Bottom", typeof(double), typeof(FixedPage),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Gets the collection of children.
    /// </summary>
    public IList<UIElement> Children => _children;

    /// <summary>
    /// Gets or sets the print ticket.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? PrintTicket
    {
        get => GetValue(PrintTicketProperty);
        set => SetValue(PrintTicketProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the content box.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Rect ContentBox
    {
        get => (Rect)GetValue(ContentBoxProperty)!;
        set => SetValue(ContentBoxProperty, value);
    }

    /// <summary>
    /// Gets or sets the bleed box.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Rect BleedBox
    {
        get => (Rect)GetValue(BleedBoxProperty)!;
        set => SetValue(BleedBoxProperty, value);
    }

    /// <summary>
    /// Gets the Left attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static double GetLeft(UIElement element) => (double)(element.GetValue(LeftProperty) ?? 0.0);

    /// <summary>
    /// Sets the Left attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetLeft(UIElement element, double value) => element.SetValue(LeftProperty, value);

    /// <summary>
    /// Gets the Top attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static double GetTop(UIElement element) => (double)(element.GetValue(TopProperty) ?? 0.0);

    /// <summary>
    /// Sets the Top attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetTop(UIElement element, double value) => element.SetValue(TopProperty, value);

    /// <summary>
    /// Gets the Right attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static double GetRight(UIElement element) => (double)(element.GetValue(RightProperty) ?? double.NaN);

    /// <summary>
    /// Sets the Right attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetRight(UIElement element, double value) => element.SetValue(RightProperty, value);

    /// <summary>
    /// Gets the Bottom attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static double GetBottom(UIElement element) => (double)(element.GetValue(BottomProperty) ?? double.NaN);

    /// <summary>
    /// Sets the Bottom attached property value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetBottom(UIElement element, double value) => element.SetValue(BottomProperty, value);
}

/// <summary>
/// Represents a sequence of FixedDocument elements.
/// </summary>
public sealed class FixedDocumentSequence : FrameworkContentElement, IDocumentPaginatorSource
{
    private readonly DocumentReferenceCollection _references;
    private FixedDocumentSequencePaginator? _paginator;

    /// <summary>
    /// Identifies the PrintTicket dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PrintTicketProperty =
        DependencyProperty.Register(nameof(PrintTicket), typeof(object), typeof(FixedDocumentSequence),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the collection of document references.
    /// </summary>
    public DocumentReferenceCollection References => _references;

    /// <summary>
    /// Gets or sets the print ticket.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? PrintTicket
    {
        get => GetValue(PrintTicketProperty);
        set => SetValue(PrintTicketProperty, value);
    }

    /// <summary>
    /// Gets the document paginator.
    /// </summary>
    public DocumentPaginator DocumentPaginator => _paginator ??= new FixedDocumentSequencePaginator(this);

    /// <summary>
    /// Initializes a new instance of the <see cref="FixedDocumentSequence"/> class.
    /// </summary>
    public FixedDocumentSequence()
    {
        _references = new DocumentReferenceCollection();
    }
}

/// <summary>
/// A collection of DocumentReference objects.
/// </summary>
public sealed class DocumentReferenceCollection : Collection<DocumentReference>
{
}

/// <summary>
/// References a FixedDocument that is part of a FixedDocumentSequence.
/// </summary>
public class DocumentReference : FrameworkElement
{
    private FixedDocument? _document;

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(DocumentReference),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the URI of the referenced FixedDocument.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets the FixedDocument.
    /// </summary>
    public FixedDocument? GetDocument(bool forceReload)
    {
        if (_document == null || forceReload)
        {
            // In a real implementation, this would load the document from Source
        }
        return _document;
    }

    /// <summary>
    /// Sets the document.
    /// </summary>
    public void SetDocument(FixedDocument? document)
    {
        _document = document;
    }
}

/// <summary>
/// Interface for objects that are sources for document paginators.
/// </summary>
public interface IDocumentPaginatorSource
{
    /// <summary>
    /// Gets the document paginator.
    /// </summary>
    DocumentPaginator DocumentPaginator { get; }
}

/// <summary>
/// Provides an abstract base class that supports creation of multiple-page elements from a single document.
/// </summary>
public abstract class DocumentPaginator
{
    /// <summary>
    /// Gets whether the page count is valid.
    /// </summary>
    public abstract bool IsPageCountValid { get; }

    /// <summary>
    /// Gets the page count.
    /// </summary>
    public abstract int PageCount { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public abstract Size PageSize { get; set; }

    /// <summary>
    /// Gets the source document.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public abstract IDocumentPaginatorSource Source { get; }

    /// <summary>
    /// Gets the page at the specified index.
    /// </summary>
    public abstract DocumentPage GetPage(int pageNumber);

    /// <summary>
    /// Gets the page asynchronously.
    /// </summary>
    public virtual void GetPageAsync(int pageNumber, object? userState)
    {
        var page = GetPage(pageNumber);
        OnGetPageCompleted(new GetPageCompletedEventArgs(page, pageNumber, null, false, userState));
    }

    /// <summary>
    /// Cancels all pending async operations.
    /// </summary>
    public virtual void CancelAsync(object? userState)
    {
    }

    /// <summary>
    /// Computes the page count.
    /// </summary>
    public virtual void ComputePageCount()
    {
    }

    /// <summary>
    /// Computes the page count asynchronously.
    /// </summary>
    public virtual void ComputePageCountAsync(object? userState)
    {
        ComputePageCount();
        OnComputePageCountCompleted(new AsyncCompletedEventArgs(null, false, userState));
    }

    /// <summary>
    /// Occurs when GetPageAsync completes.
    /// </summary>
    public event GetPageCompletedEventHandler? GetPageCompleted;

    /// <summary>
    /// Occurs when ComputePageCountAsync completes.
    /// </summary>
    public event AsyncCompletedEventHandler? ComputePageCountCompleted;

    /// <summary>
    /// Occurs when the page count changes.
    /// </summary>
    public event EventHandler? PagesChanged;

    /// <summary>
    /// Raises the GetPageCompleted event.
    /// </summary>
    protected virtual void OnGetPageCompleted(GetPageCompletedEventArgs e)
    {
        GetPageCompleted?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the ComputePageCountCompleted event.
    /// </summary>
    protected virtual void OnComputePageCountCompleted(AsyncCompletedEventArgs e)
    {
        ComputePageCountCompleted?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the PagesChanged event.
    /// </summary>
    protected virtual void OnPagesChanged(EventArgs e)
    {
        PagesChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Represents a page of a document.
/// </summary>
public sealed class DocumentPage
{
    /// <summary>
    /// A blank document page.
    /// </summary>
    public static readonly DocumentPage Missing = new();

    /// <summary>
    /// Gets the visual representation of the page.
    /// </summary>
    public Visual? Visual { get; }

    /// <summary>
    /// Gets the size of the page.
    /// </summary>
    public Size Size { get; }

    /// <summary>
    /// Gets the bleed box of the page.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Rect BleedBox { get; }

    /// <summary>
    /// Gets the content box of the page.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Rect ContentBox { get; }

    /// <summary>
    /// Creates a blank document page.
    /// </summary>
    public DocumentPage()
    {
        Size = Size.Empty;
        BleedBox = Rect.Empty;
        ContentBox = Rect.Empty;
    }

    /// <summary>
    /// Creates a document page with the specified visual.
    /// </summary>
    public DocumentPage(Visual visual)
    {
        Visual = visual;
        Size = Size.Empty;
        BleedBox = Rect.Empty;
        ContentBox = Rect.Empty;
    }

    /// <summary>
    /// Creates a document page with the specified visual and size.
    /// </summary>
    public DocumentPage(Visual visual, Size pageSize, Rect bleedBox, Rect contentBox)
    {
        Visual = visual;
        Size = pageSize;
        BleedBox = bleedBox;
        ContentBox = contentBox;
    }
}

/// <summary>
/// Delegate for GetPageCompleted event.
/// </summary>
public delegate void GetPageCompletedEventHandler(object sender, GetPageCompletedEventArgs e);

/// <summary>
/// Delegate for async completed events.
/// </summary>
public delegate void AsyncCompletedEventHandler(object sender, AsyncCompletedEventArgs e);

/// <summary>
/// Event args for GetPageCompleted event.
/// </summary>
public sealed class GetPageCompletedEventArgs : AsyncCompletedEventArgs
{
    /// <summary>
    /// Gets the document page.
    /// </summary>
    public DocumentPage DocumentPage { get; }

    /// <summary>
    /// Gets the page number.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Creates new GetPageCompletedEventArgs.
    /// </summary>
    public GetPageCompletedEventArgs(DocumentPage page, int pageNumber, Exception? error, bool cancelled, object? userState)
        : base(error, cancelled, userState)
    {
        DocumentPage = page;
        PageNumber = pageNumber;
    }
}

/// <summary>
/// Event args for async completed events.
/// </summary>
public class AsyncCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the error.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    /// Gets whether the operation was cancelled.
    /// </summary>
    public bool Cancelled { get; }

    /// <summary>
    /// Gets the user state.
    /// </summary>
    public object? UserState { get; }

    /// <summary>
    /// Creates new AsyncCompletedEventArgs.
    /// </summary>
    public AsyncCompletedEventArgs(Exception? error, bool cancelled, object? userState)
    {
        Error = error;
        Cancelled = cancelled;
        UserState = userState;
    }
}

/// <summary>
/// Paginator for FixedDocument.
/// </summary>
internal sealed class FixedDocumentPaginator : DocumentPaginator
{
    private readonly FixedDocument _document;
    private Size _pageSize = new(816, 1056); // Default US Letter

    public FixedDocumentPaginator(FixedDocument document)
    {
        _document = document;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount => _document.Pages.Count;

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = value;
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public override IDocumentPaginatorSource Source => _document;

    public override DocumentPage GetPage(int pageNumber)
    {
        if (pageNumber < 0 || pageNumber >= _document.Pages.Count)
            return DocumentPage.Missing;

        var pageContent = _document.Pages[pageNumber];
        var page = pageContent.GetPageRoot(false);

        if (page == null)
            return DocumentPage.Missing;

        return new DocumentPage(page, _pageSize, Rect.Empty, new Rect(_pageSize));
    }
}

/// <summary>
/// Paginator for FixedDocumentSequence.
/// </summary>
internal sealed class FixedDocumentSequencePaginator : DocumentPaginator
{
    private readonly FixedDocumentSequence _sequence;
    private Size _pageSize = new(816, 1056);

    public FixedDocumentSequencePaginator(FixedDocumentSequence sequence)
    {
        _sequence = sequence;
    }

    public override bool IsPageCountValid => true;

    public override int PageCount
    {
        get
        {
            var count = 0;
            foreach (var reference in _sequence.References)
            {
                var doc = reference.GetDocument(false);
                if (doc != null)
                {
                    count += doc.Pages.Count;
                }
            }
            return count;
        }
    }

    public override Size PageSize
    {
        get => _pageSize;
        set => _pageSize = value;
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public override IDocumentPaginatorSource Source => _sequence;

    public override DocumentPage GetPage(int pageNumber)
    {
        var currentPage = 0;
        foreach (var reference in _sequence.References)
        {
            var doc = reference.GetDocument(false);
            if (doc != null)
            {
                if (pageNumber < currentPage + doc.Pages.Count)
                {
                    var localPage = pageNumber - currentPage;
                    return doc.DocumentPaginator.GetPage(localPage);
                }
                currentPage += doc.Pages.Count;
            }
        }
        return DocumentPage.Missing;
    }
}

/// <summary>
/// Base class for content elements in a document.
/// </summary>
public class FrameworkContentElement : ContentElement
{
}

/// <summary>
/// Base class for content elements.
/// </summary>
public class ContentElement : DependencyObject
{
}
