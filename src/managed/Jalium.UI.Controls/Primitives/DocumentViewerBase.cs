namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides an abstract base class for document viewing controls.
/// </summary>
public abstract class DocumentViewerBase : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(IDocumentPaginatorSource), typeof(DocumentViewerBase),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(5.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(DocumentViewerBase),
            new PropertyMetadata(5000.0));

    /// <summary>
    /// Identifies the ZoomIncrement dependency property.
    /// </summary>
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
    public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the MasterPageNumber dependency property.
    /// </summary>
    public static readonly DependencyProperty MasterPageNumberProperty =
        DependencyProperty.Register(nameof(MasterPageNumber), typeof(int), typeof(DocumentViewerBase),
            new PropertyMetadata(0, OnMasterPageNumberChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the document to display.
    /// </summary>
    public IDocumentPaginatorSource? Document
    {
        get => (IDocumentPaginatorSource?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom level (percentage).
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty)!;
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum zoom level.
    /// </summary>
    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty)!;
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum zoom level.
    /// </summary>
    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty)!;
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom increment.
    /// </summary>
    public double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty)!;
        set => SetValue(ZoomIncrementProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether navigation to the next page is possible.
    /// </summary>
    public bool CanGoToNextPage => (bool)GetValue(CanGoToNextPageProperty)!;

    /// <summary>
    /// Gets a value indicating whether navigation to the previous page is possible.
    /// </summary>
    public bool CanGoToPreviousPage => (bool)GetValue(CanGoToPreviousPageProperty)!;

    /// <summary>
    /// Gets the total page count.
    /// </summary>
    public int PageCount => (int)GetValue(PageCountProperty)!;

    /// <summary>
    /// Gets or sets the current master page number.
    /// </summary>
    public int MasterPageNumber
    {
        get => (int)GetValue(MasterPageNumberProperty)!;
        set => SetValue(MasterPageNumberProperty, value);
    }

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Navigates to the first page.
    /// </summary>
    public void FirstPage()
    {
        if (PageCount > 0)
        {
            MasterPageNumber = 1;
        }
    }

    /// <summary>
    /// Navigates to the last page.
    /// </summary>
    public void LastPage()
    {
        if (PageCount > 0)
        {
            MasterPageNumber = PageCount;
        }
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    public void NextPage()
    {
        if (CanGoToNextPage)
        {
            MasterPageNumber++;
        }
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    public void PreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            MasterPageNumber--;
        }
    }

    /// <summary>
    /// Navigates to the specified page.
    /// </summary>
    /// <param name="pageNumber">The page number to navigate to.</param>
    public void GoToPage(int pageNumber)
    {
        if (pageNumber >= 1 && pageNumber <= PageCount)
        {
            MasterPageNumber = pageNumber;
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

    /// <summary>
    /// Fits the document to the window width.
    /// </summary>
    public abstract void FitToWidth();

    /// <summary>
    /// Fits the document to the window height.
    /// </summary>
    public abstract void FitToHeight();

    /// <summary>
    /// Fits the maximum amount of the document in the view.
    /// </summary>
    public abstract void FitToMaxPagesAcross();

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
        UpdateNavigationState();
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

    private void UpdatePageCount()
    {
        var paginator = Document?.DocumentPaginator;
        var count = paginator?.PageCount ?? 0;
        SetValue(PageCountPropertyKey.DependencyProperty, count);
    }

    private void UpdateNavigationState()
    {
        SetValue(CanGoToPreviousPagePropertyKey.DependencyProperty, MasterPageNumber > 1);
        SetValue(CanGoToNextPagePropertyKey.DependencyProperty, MasterPageNumber < PageCount);
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
