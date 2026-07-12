using System.Collections.ObjectModel;
using Jalium.UI.Input;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides an abstract base class for document viewing controls.
/// </summary>
public abstract class DocumentViewerBase : Control, Jalium.UI.Markup.IAddChild, IServiceProvider
{
    private ReadOnlyCollection<DocumentPageView> _pageViews = EmptyPageViews;

    /// <summary>
    /// Initializes the routed command surface shared by paginated viewers.
    /// </summary>
    protected DocumentViewerBase()
    {
        AddCommandBinding(NavigationCommands.FirstPage, static viewer => viewer.OnFirstPageCommand(), static viewer => viewer.PageCount > 0);
        AddCommandBinding(NavigationCommands.LastPage, static viewer => viewer.OnLastPageCommand(), static viewer => viewer.PageCount > 0);
        AddCommandBinding(NavigationCommands.PreviousPage, static viewer => viewer.OnPreviousPageCommand(), static viewer => viewer.CanGoToPreviousPage);
        AddCommandBinding(NavigationCommands.NextPage, static viewer => viewer.OnNextPageCommand(), static viewer => viewer.CanGoToNextPage);
        CommandBindings.Add(new CommandBinding(
            NavigationCommands.GoToPage,
            (_, args) =>
            {
                if (TryGetPageNumber(args.Parameter, out var pageNumber))
                {
                    OnGoToPageCommand(pageNumber);
                }
            },
            (_, args) => args.CanExecute = TryGetPageNumber(args.Parameter, out var pageNumber) && CanGoToPage(pageNumber)));
        AddCommandBinding(ApplicationCommands.Print, static viewer => viewer.OnPrintCommand(), static viewer => viewer.Document != null && viewer.PageCount > 0);
        AddCommandBinding(ApplicationCommands.CancelPrint, static viewer => viewer.OnCancelPrintCommand(), static viewer => viewer.IsPrintInProgress);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(IDocumentPaginatorSource), typeof(DocumentViewerBase),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(5.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(5000.0));

    /// <summary>
    /// Identifies the ZoomIncrement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ZoomIncrementProperty =
        DependencyProperty.Register(nameof(ZoomIncrement), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the CanGoToNextPage read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey CanGoToNextPagePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoToNextPage), typeof(bool), typeof(DocumentViewerBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the CanGoToNextPage dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanGoToNextPageProperty = CanGoToNextPagePropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the CanGoToPreviousPage read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey CanGoToPreviousPagePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanGoToPreviousPage), typeof(bool), typeof(DocumentViewerBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the CanGoToPreviousPage dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanGoToPreviousPageProperty = CanGoToPreviousPagePropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the PageCount read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey PageCountPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PageCount), typeof(int), typeof(DocumentViewerBase),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the PageCount dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the MasterPageNumber dependency property.
    /// </summary>
    protected static readonly DependencyPropertyKey MasterPageNumberPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(MasterPageNumber), typeof(int), typeof(DocumentViewerBase),
            new PropertyMetadata(0, OnMasterPageNumberChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty MasterPageNumberProperty = MasterPageNumberPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the attached property used to designate the master page view.
    /// </summary>
    public static readonly DependencyProperty IsMasterPageProperty =
        DependencyProperty.RegisterAttached("IsMasterPage", typeof(bool), typeof(DocumentViewerBase),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the document to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public IDocumentPaginatorSource? Document
    {
        get => (IDocumentPaginatorSource?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom level (percentage).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty)!;
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty)!;
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty)!;
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom increment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty)!;
        set => SetValue(ZoomIncrementProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether navigation to the next page is possible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public virtual bool CanGoToNextPage => (bool)GetValue(CanGoToNextPageProperty)!;

    /// <summary>
    /// Gets a value indicating whether navigation to the previous page is possible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public virtual bool CanGoToPreviousPage => (bool)GetValue(CanGoToPreviousPageProperty)!;

    /// <summary>
    /// Gets the total page count.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int PageCount => (int)GetValue(PageCountProperty)!;

    /// <summary>
    /// Gets the current master page number.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public virtual int MasterPageNumber => (int)GetValue(MasterPageNumberProperty)!;

    /// <summary>
    /// Gets the page views that currently participate in document presentation.
    /// </summary>
#pragma warning disable CS3021 // WPF exposes this attribute even when the containing assembly has no CLSCompliant attribute.
    [CLSCompliant(false)]
    public ReadOnlyCollection<DocumentPageView> PageViews => _pageViews;
#pragma warning restore CS3021

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Navigates to the first page.
    /// </summary>
    public void FirstPage() => OnFirstPageCommand();

    /// <summary>
    /// Navigates to the last page.
    /// </summary>
    public void LastPage() => OnLastPageCommand();

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    public void NextPage() => OnNextPageCommand();

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    public void PreviousPage() => OnPreviousPageCommand();

    /// <summary>
    /// Navigates to the specified page.
    /// </summary>
    /// <param name="pageNumber">The page number to navigate to.</param>
    public void GoToPage(int pageNumber) => OnGoToPageCommand(pageNumber);

    /// <summary>
    /// Returns whether the one-based page number can be navigated to.
    /// </summary>
    public virtual bool CanGoToPage(int pageNumber)
    {
        var paginator = Document?.DocumentPaginator;
        return (pageNumber > 0 && pageNumber <= PageCount) ||
            (paginator != null && pageNumber == PageCount + 1 && !paginator.IsPageCountValid);
    }

    /// <summary>
    /// Invokes the print command.
    /// </summary>
    public void Print() => OnPrintCommand();

    /// <summary>
    /// Cancels the active print operation.
    /// </summary>
    public void CancelPrint() => OnCancelPrintCommand();

    /// <summary>Handles the first-page command.</summary>
    protected virtual void OnFirstPageCommand()
    {
        if (PageCount > 0)
        {
            SetMasterPageNumber(1);
        }
    }

    /// <summary>Handles the last-page command.</summary>
    protected virtual void OnLastPageCommand()
    {
        if (PageCount > 0)
        {
            SetMasterPageNumber(PageCount);
        }
    }

    /// <summary>Handles the next-page command.</summary>
    protected virtual void OnNextPageCommand()
    {
        if (CanGoToNextPage)
        {
            SetMasterPageNumber(MasterPageNumber + 1);
        }
    }

    /// <summary>Handles the previous-page command.</summary>
    protected virtual void OnPreviousPageCommand()
    {
        if (CanGoToPreviousPage)
        {
            SetMasterPageNumber(MasterPageNumber - 1);
        }
    }

    /// <summary>Handles a request to navigate to a one-based page number.</summary>
    protected virtual void OnGoToPageCommand(int pageNumber)
    {
        if (CanGoToPage(pageNumber))
        {
            SetMasterPageNumber(pageNumber);
        }
    }

    #endregion

    #region Zoom Methods

    /// <summary>
    /// Increases the zoom level.
    /// </summary>
    public void IncreaseZoom()
    {
        Zoom = Math.Min(MaxZoom, Zoom + ZoomIncrement);
    }

    /// <summary>
    /// Decreases the zoom level.
    /// </summary>
    public void DecreaseZoom()
    {
        Zoom = Math.Max(MinZoom, Zoom - ZoomIncrement);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewerBase viewer)
        {
            viewer.OnDocumentChanged();
        }
    }

    /// <summary>
    /// Called when the Document property changes.
    /// </summary>
    protected virtual void OnDocumentChanged()
    {
        UpdatePageCount();
        SetMasterPageNumber(PageCount > 0 ? Math.Clamp(MasterPageNumber, 1, PageCount) : 0);
        UpdateNavigationState();
        InvalidatePageViews();
        InvalidateMeasure();
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewerBase viewer)
        {
            viewer.InvalidateMeasure();
        }
    }

    private static object? CoerceZoom(DependencyObject d, object? value)
    {
        if (d is DocumentViewerBase viewer && value is double zoom)
        {
            return Math.Clamp(zoom, viewer.MinZoom, viewer.MaxZoom);
        }
        return value;
    }

    private static void OnMasterPageNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewerBase viewer)
        {
            viewer.UpdateNavigationState();
            viewer.OnMasterPageNumberChanged();
        }
    }

    /// <summary>
    /// Called when the MasterPageNumber property changes.
    /// </summary>
    protected virtual void OnMasterPageNumberChanged()
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Raises <see cref="PageViewsChanged"/> after the active page-view collection changes.
    /// </summary>
    protected virtual void OnPageViewsChanged()
    {
        PageViewsChanged?.Invoke(this, EventArgs.Empty);
        OnMasterPageNumberChanged();
    }

    /// <summary>
    /// Performs the default print operation for the current paginator.
    /// </summary>
    protected virtual void OnPrintCommand()
    {
        if (Document == null || PageCount == 0 || IsPrintInProgress)
        {
            return;
        }

        IsPrintInProgress = true;
        try
        {
            FlowDocumentViewerSupport.Print(Document.DocumentPaginator, Math.Max(1, MasterPageNumber));
        }
        finally
        {
            IsPrintInProgress = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Cancels the active print operation when the platform print pipeline supports cancellation.
    /// </summary>
    protected virtual void OnCancelPrintCommand()
    {
        FlowDocumentViewerSupport.CancelPrint();
        IsPrintInProgress = false;
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Handles a request to bring an element on the specified one-based page into view.
    /// The base implementation navigates to that page.
    /// </summary>
    protected virtual void OnBringIntoView(DependencyObject element, Rect rect, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(element);
        OnGoToPageCommand(pageNumber);
    }

    /// <summary>
    /// Gets the page views currently displayed by the viewer.
    /// </summary>
    protected virtual ReadOnlyCollection<DocumentPageView> GetPageViewsCollection(out bool changed)
    {
        changed = false;
        return EmptyPageViews;
    }

    /// <summary>
    /// Re-evaluates and publishes the current page-view collection.
    /// </summary>
    protected void InvalidatePageViews()
    {
        var pageViews = GetPageViewsCollection(out var changed) ?? EmptyPageViews;
        if (changed || !ReferenceEquals(pageViews, _pageViews))
        {
            _pageViews = pageViews;
            var paginator = Document?.DocumentPaginator;
            foreach (var pageView in _pageViews)
            {
                pageView.DocumentPaginator = paginator;
            }
            OnPageViewsChanged();
        }

        InvalidateMeasure();
    }

    /// <summary>
    /// Returns the page view explicitly marked as master, or the first active page view.
    /// </summary>
    protected DocumentPageView? GetMasterPageView()
    {
        foreach (var pageView in _pageViews)
        {
            if (GetIsMasterPage(pageView))
            {
                return pageView;
            }
        }

        return _pageViews.Count > 0 ? _pageViews[0] : null;
    }

    /// <summary>
    /// Gets the attached master-page marker.
    /// </summary>
    public static bool GetIsMasterPage(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsMasterPageProperty) ?? false);
    }

    /// <summary>
    /// Sets the attached master-page marker.
    /// </summary>
    public static void SetIsMasterPage(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsMasterPageProperty, value);
    }

    /// <summary>
    /// Occurs when <see cref="PageViews"/> is replaced.
    /// </summary>
    public event EventHandler? PageViewsChanged;

    /// <summary>
    /// Updates the read-only master-page dependency property for derived viewers.
    /// </summary>
    protected void SetMasterPageNumber(int value) => SetValue(MasterPageNumberPropertyKey, value);

    private void UpdatePageCount()
    {
        var paginator = Document?.DocumentPaginator;
        var count = paginator?.PageCount ?? 0;
        SetValue(PageCountPropertyKey, count);
    }

    private void UpdateNavigationState()
    {
        SetValue(CanGoToPreviousPagePropertyKey, MasterPageNumber > 1);
        SetValue(CanGoToNextPagePropertyKey, MasterPageNumber < PageCount);
    }

    private static ReadOnlyCollection<DocumentPageView> EmptyPageViews { get; } =
        new(Array.Empty<DocumentPageView>());

    internal bool IsPrintInProgress { get; private set; }

    private void AddCommandBinding(RoutedUICommand command, Action<DocumentViewerBase> execute, Predicate<DocumentViewerBase> canExecute)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, _) => execute(this),
            (_, args) => args.CanExecute = canExecute(this)));
    }

    private static bool TryGetPageNumber(object? parameter, out int pageNumber)
    {
        if (parameter is int value)
        {
            pageNumber = value;
            return true;
        }

        return int.TryParse(parameter?.ToString(), out pageNumber);
    }

    void Jalium.UI.Markup.IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Document != null)
        {
            throw new ArgumentException("DocumentViewerBase accepts only one document child.", nameof(value));
        }

        if (value is not IDocumentPaginatorSource document)
        {
            throw new ArgumentException(
                $"Document children must implement {nameof(IDocumentPaginatorSource)}.", nameof(value));
        }

        Document = document;
    }

    void Jalium.UI.Markup.IAddChild.AddText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("DocumentViewerBase does not accept literal text children.", nameof(text));
        }
    }

    object? IServiceProvider.GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(IDocumentPaginatorSource))
        {
            return Document;
        }

        if (serviceType == typeof(DocumentPaginator))
        {
            return Document?.DocumentPaginator;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    #endregion
}

/// <summary>
/// Interface for document sources that can be paginated.
/// </summary>
public interface IDocumentPaginatorSource
{
    /// <summary>
    /// Gets the document paginator.
    /// </summary>
    DocumentPaginator DocumentPaginator { get; }
}

/// <summary>
/// Provides pagination functionality for documents.
/// </summary>
public abstract class DocumentPaginator
{
    /// <summary>
    /// Gets the page count.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public abstract int PageCount { get; }

    /// <summary>
    /// Gets a value indicating whether page count is valid.
    /// </summary>
    public abstract bool IsPageCountValid { get; }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public abstract Size PageSize { get; set; }

    /// <summary>
    /// Gets the specified page.
    /// </summary>
    /// <param name="pageNumber">The page number.</param>
    /// <returns>The document page.</returns>
    public abstract DocumentPage GetPage(int pageNumber);
}

/// <summary>
/// Represents a page of a document.
/// </summary>
public sealed class DocumentPage
{
    /// <summary>
    /// Gets the visual content of the page.
    /// </summary>
    public Visual? Visual { get; set; }

    /// <summary>
    /// Gets the size of the page.
    /// </summary>
    public Size Size { get; set; }

    /// <summary>
    /// Represents a missing page.
    /// </summary>
    public static readonly DocumentPage Missing = new() { Size = Size.Empty };
}
