using Jalium.UI.Controls.Printing;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how pages are displayed in a document viewer.
/// </summary>
public enum DocumentViewerPageDisplay
{
    /// <summary>
    /// One page at a time.
    /// </summary>
    OnePage,

    /// <summary>
    /// Two pages side by side.
    /// </summary>
    TwoPages,

    /// <summary>
    /// Two pages with the first page on the right (book style).
    /// </summary>
    TwoUpFacing,

    /// <summary>
    /// Continuous scrolling through pages.
    /// </summary>
    Continuous,

    /// <summary>
    /// Continuous scrolling with two pages side by side.
    /// </summary>
    ContinuousFacing
}

/// <summary>
/// Specifies the fit mode for document viewing.
/// </summary>
public enum DocumentViewerFitMode
{
    /// <summary>
    /// No automatic fit, use zoom level directly.
    /// </summary>
    None,

    /// <summary>
    /// Fit the page width to the viewer width.
    /// </summary>
    FitWidth,

    /// <summary>
    /// Fit the whole page in the viewer.
    /// </summary>
    FitPage,

    /// <summary>
    /// Maximum width of the page.
    /// </summary>
    MaxWidth
}

/// <summary>
/// Provides viewing, navigation, and printing capabilities for paginated documents.
/// </summary>
public sealed class DocumentViewer : Control
{
    private DocumentPaginator? _document;
    private int _pageCount;
    private int _currentPage = 1;
    private string _searchText = string.Empty;
    private List<TextSearchResult> _searchResults = new();
    private int _currentSearchResultIndex = -1;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(DocumentPaginator), typeof(DocumentViewer),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(DocumentViewer),
            new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(DocumentViewer),
            new PropertyMetadata(25.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(DocumentViewer),
            new PropertyMetadata(400.0));

    /// <summary>
    /// Identifies the FitMode dependency property.
    /// </summary>
    public static readonly DependencyProperty FitModeProperty =
        DependencyProperty.Register(nameof(FitMode), typeof(DocumentViewerFitMode), typeof(DocumentViewer),
            new PropertyMetadata(DocumentViewerFitMode.FitWidth, OnFitModeChanged));

    /// <summary>
    /// Identifies the PageDisplay dependency property.
    /// </summary>
    public static readonly DependencyProperty PageDisplayProperty =
        DependencyProperty.Register(nameof(PageDisplay), typeof(DocumentViewerPageDisplay), typeof(DocumentViewer),
            new PropertyMetadata(DocumentViewerPageDisplay.OnePage, OnPageDisplayChanged));

    /// <summary>
    /// Identifies the ShowPageBorders dependency property.
    /// </summary>
    public static readonly DependencyProperty ShowPageBordersProperty =
        DependencyProperty.Register(nameof(ShowPageBorders), typeof(bool), typeof(DocumentViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the HorizontalPageSpacing dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalPageSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalPageSpacing), typeof(double), typeof(DocumentViewer),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the VerticalPageSpacing dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalPageSpacingProperty =
        DependencyProperty.Register(nameof(VerticalPageSpacing), typeof(double), typeof(DocumentViewer),
            new PropertyMetadata(10.0));

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the current page changes.
    /// </summary>
    public event EventHandler<PageChangedEventArgs>? PageChanged;

    /// <summary>
    /// Occurs when the document is loaded.
    /// </summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>
    /// Occurs when a search completes.
    /// </summary>
    public event EventHandler<SearchCompletedEventArgs>? SearchCompleted;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the document to display.
    /// </summary>
    public DocumentPaginator? Document
    {
        get => (DocumentPaginator?)GetValue(DocumentProperty);
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
    /// Gets or sets the fit mode.
    /// </summary>
    public DocumentViewerFitMode FitMode
    {
        get => (DocumentViewerFitMode)(GetValue(FitModeProperty) ?? DocumentViewerFitMode.FitWidth);
        set => SetValue(FitModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the page display mode.
    /// </summary>
    public DocumentViewerPageDisplay PageDisplay
    {
        get => (DocumentViewerPageDisplay)(GetValue(PageDisplayProperty) ?? DocumentViewerPageDisplay.OnePage);
        set => SetValue(PageDisplayProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to show page borders.
    /// </summary>
    public bool ShowPageBorders
    {
        get => (bool)GetValue(ShowPageBordersProperty)!;
        set => SetValue(ShowPageBordersProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal spacing between pages.
    /// </summary>
    public double HorizontalPageSpacing
    {
        get => (double)GetValue(HorizontalPageSpacingProperty)!;
        set => SetValue(HorizontalPageSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical spacing between pages.
    /// </summary>
    public double VerticalPageSpacing
    {
        get => (double)GetValue(VerticalPageSpacingProperty)!;
        set => SetValue(VerticalPageSpacingProperty, value);
    }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int PageCount => _pageCount;

    /// <summary>
    /// Gets the current page number (1-based).
    /// </summary>
    public int CurrentPage => _currentPage;

    /// <summary>
    /// Gets a value indicating whether navigation to the previous page is possible.
    /// </summary>
    public bool CanGoToPreviousPage => _currentPage > 1;

    /// <summary>
    /// Gets a value indicating whether navigation to the next page is possible.
    /// </summary>
    public bool CanGoToNextPage => _currentPage < _pageCount;

    /// <summary>
    /// Gets the current search text.
    /// </summary>
    public string SearchText => _searchText;

    /// <summary>
    /// Gets the number of search results.
    /// </summary>
    public int SearchResultCount => _searchResults.Count;

    #endregion

    #region Navigation Methods

    /// <summary>
    /// Navigates to the first page.
    /// </summary>
    public void FirstPage()
    {
        GoToPage(1);
    }

    /// <summary>
    /// Navigates to the last page.
    /// </summary>
    public void LastPage()
    {
        GoToPage(_pageCount);
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    public void PreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            GoToPage(_currentPage - 1);
        }
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    public void NextPage()
    {
        if (CanGoToNextPage)
        {
            GoToPage(_currentPage + 1);
        }
    }

    /// <summary>
    /// Navigates to the specified page.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    public void GoToPage(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > _pageCount)
            return;

        var oldPage = _currentPage;
        _currentPage = pageNumber;
        OnPageChanged(oldPage, pageNumber);
        InvalidateVisual();
    }

    #endregion

    #region Zoom Methods

    /// <summary>
    /// Increases the zoom level.
    /// </summary>
    public void ZoomIn()
    {
        Zoom = Math.Min(MaxZoom, Zoom * 1.25);
    }

    /// <summary>
    /// Decreases the zoom level.
    /// </summary>
    public void ZoomOut()
    {
        Zoom = Math.Max(MinZoom, Zoom / 1.25);
    }

    /// <summary>
    /// Sets the zoom level to fit the page width.
    /// </summary>
    public void FitToWidth()
    {
        FitMode = DocumentViewerFitMode.FitWidth;
    }

    /// <summary>
    /// Sets the zoom level to fit the whole page.
    /// </summary>
    public void FitToPage()
    {
        FitMode = DocumentViewerFitMode.FitPage;
    }

    /// <summary>
    /// Sets the zoom level to a specific percentage.
    /// </summary>
    public void SetZoom(double zoomPercent)
    {
        FitMode = DocumentViewerFitMode.None;
        Zoom = zoomPercent;
    }

    #endregion

    #region Search Methods

    /// <summary>
    /// Searches for text in the document.
    /// </summary>
    /// <param name="text">The text to search for.</param>
    /// <param name="matchCase">Whether to match case.</param>
    /// <param name="matchWholeWord">Whether to match whole words only.</param>
    public void Find(string text, bool matchCase = false, bool matchWholeWord = false)
    {
        _searchText = text;
        _searchResults.Clear();
        _currentSearchResultIndex = -1;

        if (string.IsNullOrEmpty(text) || _document == null)
        {
            OnSearchCompleted(0);
            return;
        }

        // Perform search (platform-specific implementation)
        FindTextInDocument(text, matchCase, matchWholeWord);
    }

    /// <summary>
    /// Finds the next search result.
    /// </summary>
    public void FindNext()
    {
        if (_searchResults.Count == 0)
            return;

        _currentSearchResultIndex = (_currentSearchResultIndex + 1) % _searchResults.Count;
        NavigateToSearchResult(_searchResults[_currentSearchResultIndex]);
    }

    /// <summary>
    /// Finds the previous search result.
    /// </summary>
    public void FindPrevious()
    {
        if (_searchResults.Count == 0)
            return;

        _currentSearchResultIndex--;
        if (_currentSearchResultIndex < 0)
            _currentSearchResultIndex = _searchResults.Count - 1;
        NavigateToSearchResult(_searchResults[_currentSearchResultIndex]);
    }

    /// <summary>
    /// Clears the search.
    /// </summary>
    public void ClearSearch()
    {
        _searchText = string.Empty;
        _searchResults.Clear();
        _currentSearchResultIndex = -1;
        InvalidateVisual();
    }

    #endregion

    #region Print Methods

    /// <summary>
    /// Opens the print dialog.
    /// </summary>
    public void Print()
    {
        if (_document == null)
            return;

        var printDialog = new PrintDialog
        {
            MinPage = 1,
            MaxPage = _pageCount,
            PageRangeFrom = 1,
            PageRangeTo = _pageCount,
            CurrentPage = _currentPage,
            CurrentPageEnabled = true
        };

        if (printDialog.ShowDialog())
        {
            printDialog.PrintDocument(_document, "Document");
        }
    }

    #endregion

    #region Property Changed Handlers

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.OnDocumentChanged((DocumentPaginator?)e.NewValue);
        }
    }

    private void OnDocumentChanged(DocumentPaginator? newDocument)
    {
        _document = newDocument;
        _currentPage = 1;
        _pageCount = newDocument?.PageCount ?? 0;
        _searchResults.Clear();
        _currentSearchResultIndex = -1;
        _searchText = string.Empty;

        if (newDocument != null && !newDocument.IsPageCountValid)
        {
            newDocument.ComputePageCountCompleted += OnPaginationCompleted;
            newDocument.ComputePageCount();
        }
        else
        {
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }

        InvalidateMeasure();
    }

    private void OnPaginationCompleted(object? sender, PaginationCompletedEventArgs e)
    {
        if (_document != null)
        {
            _document.ComputePageCountCompleted -= OnPaginationCompleted;
            _pageCount = _document.PageCount;
        }
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
        InvalidateMeasure();
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.InvalidateMeasure();
        }
    }

    private static object CoerceZoom(DependencyObject d, object? value)
    {
        if (d is DocumentViewer viewer)
        {
            var zoom = (double)(value ?? 100.0);
            return Math.Clamp(zoom, viewer.MinZoom, viewer.MaxZoom);
        }
        return value ?? 100.0;
    }

    private static void OnFitModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.UpdateZoomFromFitMode();
        }
    }

    private static void OnPageDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.InvalidateMeasure();
        }
    }

    #endregion

    #region Internal Methods

    private void OnPageChanged(int oldPage, int newPage)
    {
        PageChanged?.Invoke(this, new PageChangedEventArgs(oldPage, newPage));
    }

    private void OnSearchCompleted(int resultCount)
    {
        SearchCompleted?.Invoke(this, new SearchCompletedEventArgs(resultCount));
    }

    private void UpdateZoomFromFitMode()
    {
        // Calculate zoom based on fit mode and available space
        // Platform-specific implementation
        InvalidateMeasure();
    }

    private void FindTextInDocument(string text, bool matchCase, bool matchWholeWord)
    {
        // Platform-specific text search implementation
        OnSearchCompleted(_searchResults.Count);
    }

    private void NavigateToSearchResult(TextSearchResult result)
    {
        if (result.PageNumber != _currentPage)
        {
            GoToPage(result.PageNumber);
        }
        // Scroll to and highlight the result
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    #endregion
}

/// <summary>
/// Event arguments for page changed events.
/// </summary>
public sealed class PageChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous page number.
    /// </summary>
    public int OldPageNumber { get; }

    /// <summary>
    /// Gets the new page number.
    /// </summary>
    public int NewPageNumber { get; }

    /// <summary>
    /// Initializes a new instance of the PageChangedEventArgs class.
    /// </summary>
    public PageChangedEventArgs(int oldPageNumber, int newPageNumber)
    {
        OldPageNumber = oldPageNumber;
        NewPageNumber = newPageNumber;
    }
}

/// <summary>
/// Event arguments for search completed events.
/// </summary>
public sealed class SearchCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the number of results found.
    /// </summary>
    public int ResultCount { get; }

    /// <summary>
    /// Initializes a new instance of the SearchCompletedEventArgs class.
    /// </summary>
    public SearchCompletedEventArgs(int resultCount)
    {
        ResultCount = resultCount;
    }
}

/// <summary>
/// Represents a text search result.
/// </summary>
public sealed class TextSearchResult
{
    /// <summary>
    /// Gets the page number containing the result.
    /// </summary>
    public int PageNumber { get; }

    /// <summary>
    /// Gets the bounding rectangle of the result on the page.
    /// </summary>
    public Rect BoundingRect { get; }

    /// <summary>
    /// Gets the matched text.
    /// </summary>
    public string MatchedText { get; }

    /// <summary>
    /// Initializes a new instance of the TextSearchResult class.
    /// </summary>
    public TextSearchResult(int pageNumber, Rect boundingRect, string matchedText)
    {
        PageNumber = pageNumber;
        BoundingRect = boundingRect;
        MatchedText = matchedText;
    }
}
