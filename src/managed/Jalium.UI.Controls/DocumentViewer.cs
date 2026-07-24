using System.Collections.ObjectModel;
using System.Globalization;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using DocumentPaginator = Jalium.UI.Documents.DocumentPaginator;

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
public class DocumentViewer : DocumentViewerBase
{
    private const int MaximumPagesAcross = 32;
    private const double LineScrollAmount = 16.0;
    private static readonly double[] s_zoomLevels =
    [
        5000.0, 4000.0, 3200.0, 2400.0, 2000.0, 1600.0, 1200.0, 800.0,
        400.0, 300.0, 200.0, 175.0, 150.0, 125.0, 100.0, 75.0, 66.0,
        50.0, 33.0, 25.0, 10.0, 5.0,
    ];

    private static readonly RoutedUICommand s_viewThumbnailsCommand =
        new("View Thumbnails", nameof(ViewThumbnailsCommand), typeof(DocumentViewer));
    private static readonly RoutedUICommand s_fitToWidthCommand =
        new("Fit To Width", nameof(FitToWidthCommand), typeof(DocumentViewer));
    private static readonly RoutedUICommand s_fitToHeightCommand =
        new("Fit To Height", nameof(FitToHeightCommand), typeof(DocumentViewer));
    private static readonly RoutedUICommand s_fitToMaxPagesAcrossCommand =
        new("Fit To Max Pages Across", nameof(FitToMaxPagesAcrossCommand), typeof(DocumentViewer));

    private readonly List<DocumentPageView> _pageViews = [];
    private readonly ReadOnlyCollection<DocumentPageView> _readOnlyPageViews;
    private readonly List<TextSearchResult> _searchResults = [];
    private DocumentPaginator? _paginator;
    private bool _pageViewsChanged;
    private bool _publishingPageViews;
    private bool _updatingFitMode;
    private int _lastPageNumber;
    private int _currentSearchResultIndex = -1;
    private string _searchText = string.Empty;

    public DocumentViewer()
    {
        _readOnlyPageViews = new ReadOnlyCollection<DocumentPageView>(_pageViews);
        RegisterCommandBindings();
        UpdateZoomState();
    }

    #region Commands

    public static RoutedUICommand ViewThumbnailsCommand => s_viewThumbnailsCommand;
    public static RoutedUICommand FitToWidthCommand => s_fitToWidthCommand;
    public static RoutedUICommand FitToHeightCommand => s_fitToHeightCommand;
    public static RoutedUICommand FitToMaxPagesAcrossCommand => s_fitToMaxPagesAcrossCommand;

    #endregion

    #region Dependency properties

    public new static readonly DependencyProperty ZoomProperty =
        DocumentViewerBase.ZoomProperty.AddOwner(
            typeof(DocumentViewer),
            new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));

    public new static readonly DependencyProperty MinZoomProperty =
        DocumentViewerBase.MinZoomProperty.AddOwner(
            typeof(DocumentViewer),
            new PropertyMetadata(5.0, OnZoomBoundsChanged));

    public new static readonly DependencyProperty MaxZoomProperty =
        DocumentViewerBase.MaxZoomProperty.AddOwner(
            typeof(DocumentViewer),
            new PropertyMetadata(5000.0, OnZoomBoundsChanged));

    public new static readonly DependencyProperty ZoomIncrementProperty =
        DocumentViewerBase.ZoomIncrementProperty.AddOwner(typeof(DocumentViewer));

    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(
            nameof(HorizontalOffset),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0, OnOffsetChanged),
            IsValidOffset);

    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(
            nameof(VerticalOffset),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0, OnOffsetChanged),
            IsValidOffset);

    private static readonly DependencyPropertyKey ExtentWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentWidth),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ExtentWidthProperty = ExtentWidthPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ExtentHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentHeight),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ExtentHeightProperty = ExtentHeightPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ViewportWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportWidth),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ViewportWidthProperty = ViewportWidthPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ViewportHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportHeight),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ViewportHeightProperty = ViewportHeightPropertyKey.DependencyProperty;

    public static readonly DependencyProperty MaxPagesAcrossProperty =
        DependencyProperty.Register(
            nameof(MaxPagesAcross),
            typeof(int),
            typeof(DocumentViewer),
            new PropertyMetadata(1, OnLayoutPropertyChanged),
            IsValidMaxPagesAcross);

    private static readonly DependencyPropertyKey CanMoveUpPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanMoveUp), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty CanMoveUpProperty = CanMoveUpPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanMoveDownPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanMoveDown), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty CanMoveDownProperty = CanMoveDownPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanMoveLeftPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanMoveLeft), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty CanMoveLeftProperty = CanMoveLeftPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanMoveRightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanMoveRight), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty CanMoveRightProperty = CanMoveRightPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanIncreaseZoom), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanIncreaseZoomProperty = CanIncreaseZoomPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanDecreaseZoom), typeof(bool), typeof(DocumentViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanDecreaseZoomProperty = CanDecreaseZoomPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ShowPageBordersProperty =
        DependencyProperty.Register(
            nameof(ShowPageBorders),
            typeof(bool),
            typeof(DocumentViewer),
            new PropertyMetadata(true, OnShowPageBordersChanged));

    public static readonly DependencyProperty HorizontalPageSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalPageSpacing),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(10.0, OnLayoutPropertyChanged),
            IsValidPageSpacing);

    public static readonly DependencyProperty VerticalPageSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalPageSpacing),
            typeof(double),
            typeof(DocumentViewer),
            new PropertyMetadata(10.0, OnLayoutPropertyChanged),
            IsValidPageSpacing);

    public static readonly DependencyProperty FitModeProperty =
        DependencyProperty.Register(
            nameof(FitMode),
            typeof(DocumentViewerFitMode),
            typeof(DocumentViewer),
            new PropertyMetadata(DocumentViewerFitMode.None, OnFitModeChanged));

    public static readonly DependencyProperty PageDisplayProperty =
        DependencyProperty.Register(
            nameof(PageDisplay),
            typeof(DocumentViewerPageDisplay),
            typeof(DocumentViewer),
            new PropertyMetadata(DocumentViewerPageDisplay.OnePage, OnPageDisplayChanged));

    #endregion

    #region CLR properties

    public new double Zoom
    {
        get => (double)(GetValue(ZoomProperty) ?? 100.0);
        set => SetValue(ZoomProperty, value);
    }

    public new double MinZoom
    {
        get => (double)(GetValue(MinZoomProperty) ?? 5.0);
        set => SetValue(MinZoomProperty, value);
    }

    public new double MaxZoom
    {
        get => (double)(GetValue(MaxZoomProperty) ?? 5000.0);
        set => SetValue(MaxZoomProperty, value);
    }

    public new double ZoomIncrement
    {
        get => (double)(GetValue(ZoomIncrementProperty) ?? 10.0);
        set => SetValue(ZoomIncrementProperty, value);
    }

    public double HorizontalOffset
    {
        get => (double)(GetValue(HorizontalOffsetProperty) ?? 0.0);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    public double VerticalOffset
    {
        get => (double)(GetValue(VerticalOffsetProperty) ?? 0.0);
        set => SetValue(VerticalOffsetProperty, value);
    }

    public double ExtentWidth => (double)(GetValue(ExtentWidthProperty) ?? 0.0);
    public double ExtentHeight => (double)(GetValue(ExtentHeightProperty) ?? 0.0);
    public double ViewportWidth => (double)(GetValue(ViewportWidthProperty) ?? 0.0);
    public double ViewportHeight => (double)(GetValue(ViewportHeightProperty) ?? 0.0);

    public int MaxPagesAcross
    {
        get => (int)(GetValue(MaxPagesAcrossProperty) ?? 1);
        set => SetValue(MaxPagesAcrossProperty, value);
    }

    public bool CanMoveUp => (bool)(GetValue(CanMoveUpProperty) ?? false);
    public bool CanMoveDown => (bool)(GetValue(CanMoveDownProperty) ?? false);
    public bool CanMoveLeft => (bool)(GetValue(CanMoveLeftProperty) ?? false);
    public bool CanMoveRight => (bool)(GetValue(CanMoveRightProperty) ?? false);
    public bool CanIncreaseZoom => (bool)(GetValue(CanIncreaseZoomProperty) ?? true);
    public bool CanDecreaseZoom => (bool)(GetValue(CanDecreaseZoomProperty) ?? true);

    public bool ShowPageBorders
    {
        get => (bool)(GetValue(ShowPageBordersProperty) ?? true);
        set => SetValue(ShowPageBordersProperty, value);
    }

    public double HorizontalPageSpacing
    {
        get => (double)(GetValue(HorizontalPageSpacingProperty) ?? 10.0);
        set => SetValue(HorizontalPageSpacingProperty, value);
    }

    public double VerticalPageSpacing
    {
        get => (double)(GetValue(VerticalPageSpacingProperty) ?? 10.0);
        set => SetValue(VerticalPageSpacingProperty, value);
    }

    public DocumentViewerFitMode FitMode
    {
        get => (DocumentViewerFitMode)(GetValue(FitModeProperty) ?? DocumentViewerFitMode.None);
        set => SetValue(FitModeProperty, value);
    }

    public DocumentViewerPageDisplay PageDisplay
    {
        get => (DocumentViewerPageDisplay)(GetValue(PageDisplayProperty) ?? DocumentViewerPageDisplay.OnePage);
        set => SetValue(PageDisplayProperty, value);
    }

    public int CurrentPage => MasterPageNumber;
    public string SearchText => _searchText;
    public int SearchResultCount => _searchResults.Count;

    #endregion

    #region Events

    public event EventHandler<PageChangedEventArgs>? PageChanged;
    public event EventHandler? DocumentLoaded;
    public event EventHandler<SearchCompletedEventArgs>? SearchCompleted;

    #endregion

    #region Public commands and compatibility methods

    public void ViewThumbnails() => OnViewThumbnailsCommand();
    public void FitToWidth() => OnFitToWidthCommand();
    public void FitToHeight() => OnFitToHeightCommand();
    public void FitToMaxPagesAcross() => OnFitToMaxPagesAcrossCommand();

    public void FitToMaxPagesAcross(int pagesAcross) => OnFitToMaxPagesAcrossCommand(pagesAcross);
    public void ScrollPageUp() => OnScrollPageUpCommand();
    public void ScrollPageDown() => OnScrollPageDownCommand();
    public void ScrollPageLeft() => OnScrollPageLeftCommand();
    public void ScrollPageRight() => OnScrollPageRightCommand();
    public void MoveUp() => OnMoveUpCommand();
    public void MoveDown() => OnMoveDownCommand();
    public void MoveLeft() => OnMoveLeftCommand();
    public void MoveRight() => OnMoveRightCommand();
    public new void IncreaseZoom() => OnIncreaseZoomCommand();
    public new void DecreaseZoom() => OnDecreaseZoomCommand();

    public void ZoomIn() => IncreaseZoom();
    public void ZoomOut() => DecreaseZoom();

    public void FitToPage()
    {
        if (_paginator == null)
        {
            return;
        }

        SetFitMode(DocumentViewerFitMode.FitPage);
        PageDisplay = DocumentViewerPageDisplay.OnePage;
        SetZoomFromViewport(fitWidth: true, fitHeight: true, 1);
    }

    public void SetZoom(double zoomPercent)
    {
        SetFitMode(DocumentViewerFitMode.None);
        Zoom = zoomPercent;
    }

    public void Find() => OnFindCommand();

    public void Find(string text, bool matchCase = false, bool matchWholeWord = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        _searchText = text;
        _searchResults.Clear();
        _currentSearchResultIndex = -1;

        if (text.Length == 0 || _paginator == null)
        {
            OnSearchCompleted(0);
            return;
        }

        if (_paginator is not IDocumentTextSearchProvider searchProvider)
        {
            throw new NotSupportedException(
                "The current paginator does not expose searchable text. Implement IDocumentTextSearchProvider to enable document search.");
        }

        _searchResults.AddRange(searchProvider.Find(text, matchCase, matchWholeWord));
        OnSearchCompleted(_searchResults.Count);
    }

    public void FindNext()
    {
        if (_searchResults.Count == 0)
        {
            return;
        }

        _currentSearchResultIndex = (_currentSearchResultIndex + 1) % _searchResults.Count;
        NavigateToSearchResult(_searchResults[_currentSearchResultIndex]);
    }

    public void FindPrevious()
    {
        if (_searchResults.Count == 0)
        {
            return;
        }

        _currentSearchResultIndex = (_currentSearchResultIndex - 1 + _searchResults.Count) % _searchResults.Count;
        NavigateToSearchResult(_searchResults[_currentSearchResultIndex]);
    }

    public void ClearSearch()
    {
        _searchText = string.Empty;
        _searchResults.Clear();
        _currentSearchResultIndex = -1;
        InvalidateVisual();
    }

    #endregion

    #region Protected command hooks

    protected override void OnDocumentChanged()
    {
        var oldPage = CurrentPage;
        base.OnDocumentChanged();
        _paginator = Document?.DocumentPaginator;

        ClearSearch();
        _lastPageNumber = oldPage;
        if (PageCount > 0)
        {
            SetMasterPageNumber(1);
        }
        else
        {
            SetMasterPageNumber(0);
        }

        if (!_publishingPageViews)
        {
            RefreshPageViews();
        }
        UpdateMoveState();
        DocumentLoaded?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMasterPageNumberChanged()
    {
        base.OnMasterPageNumberChanged();
        var newPage = CurrentPage;
        var oldPage = _lastPageNumber;
        _lastPageNumber = newPage;

        if (!_publishingPageViews)
        {
            RefreshPageViews();
        }
        ScrollCurrentPageIntoView();
        if (oldPage != newPage)
        {
            PageChanged?.Invoke(this, new PageChangedEventArgs(oldPage, newPage));
        }
    }

    protected override void OnBringIntoView(DependencyObject element, Rect rect, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(element);
        OnGoToPageCommand(pageNumber);

        if (!rect.IsEmpty)
        {
            SetHorizontalOffset(rect.X);
            SetVerticalOffset(rect.Y);
        }
    }

    protected override void OnPreviousPageCommand()
    {
        if (PageDisplay == DocumentViewerPageDisplay.TwoUpFacing)
        {
            if (CurrentPage is 2 or 3)
            {
                OnGoToPageCommand(1);
                return;
            }

            if (CurrentPage > 3)
            {
                var currentRowStart = 2 + (((CurrentPage - 2) / 2) * 2);
                OnGoToPageCommand(currentRowStart - 2);
            }
            return;
        }

        OnGoToPageCommand(Math.Max(1, CurrentPage - Math.Max(1, GetLayoutColumns())));
    }

    protected override void OnNextPageCommand()
    {
        if (PageDisplay == DocumentViewerPageDisplay.TwoUpFacing)
        {
            var nextPage = CurrentPage <= 1
                ? 2
                : 2 + (((CurrentPage - 2) / 2) * 2) + 2;
            OnGoToPageCommand(Math.Min(PageCount, nextPage));
            return;
        }

        OnGoToPageCommand(Math.Min(PageCount, CurrentPage + Math.Max(1, GetLayoutColumns())));
    }

    protected override void OnFirstPageCommand() => OnGoToPageCommand(1);
    protected override void OnLastPageCommand() => OnGoToPageCommand(PageCount);

    protected override void OnGoToPageCommand(int pageNumber)
    {
        if (pageNumber >= 1 && pageNumber <= PageCount)
        {
            SetMasterPageNumber(pageNumber);
        }
    }

    protected virtual void OnViewThumbnailsCommand()
    {
        if (_paginator == null)
        {
            return;
        }

        var pageWidth = GetUnscaledPageSize().Width;
        var viewport = GetEffectiveViewportWidth();
        var thumbnailWidth = Math.Max(1.0, pageWidth * Math.Max(MinZoom, 5.0) / 100.0);
        var columns = viewport > 0
            ? Math.Clamp((int)Math.Floor((viewport + HorizontalPageSpacing) / (thumbnailWidth + HorizontalPageSpacing)), 1, MaximumPagesAcross)
            : Math.Min(4, MaximumPagesAcross);

        PageDisplay = DocumentViewerPageDisplay.Continuous;
        OnFitToMaxPagesAcrossCommand(columns);
    }

    protected virtual void OnFitToWidthCommand()
    {
        if (_paginator == null)
        {
            return;
        }

        PageDisplay = DocumentViewerPageDisplay.OnePage;
        MaxPagesAcross = 1;
        SetFitMode(DocumentViewerFitMode.FitWidth);
        SetZoomFromViewport(fitWidth: true, fitHeight: false, 1);
    }

    protected virtual void OnFitToHeightCommand()
    {
        if (_paginator == null)
        {
            return;
        }

        PageDisplay = DocumentViewerPageDisplay.OnePage;
        MaxPagesAcross = 1;
        SetFitMode(DocumentViewerFitMode.FitPage);
        SetZoomFromViewport(fitWidth: false, fitHeight: true, 1);
    }

    protected virtual void OnFitToMaxPagesAcrossCommand() =>
        OnFitToMaxPagesAcrossCommand(MaxPagesAcross);

    protected virtual void OnFitToMaxPagesAcrossCommand(int pagesAcross)
    {
        if (!IsValidMaxPagesAcross(pagesAcross))
        {
            throw new ArgumentOutOfRangeException(nameof(pagesAcross));
        }

        if (_paginator == null)
        {
            return;
        }

        PageDisplay = DocumentViewerPageDisplay.Continuous;
        MaxPagesAcross = pagesAcross;
        SetFitMode(DocumentViewerFitMode.MaxWidth);
        SetZoomFromViewport(fitWidth: true, fitHeight: false, pagesAcross);
    }

    protected virtual void OnFindCommand()
    {
        if (string.IsNullOrEmpty(_searchText))
        {
            throw new NotSupportedException(
                "This viewer has no built-in find toolbar. Supply search text through Find(string, ...) first.");
        }

        Find(_searchText);
    }

    protected virtual void OnScrollPageUpCommand() => SetVerticalOffset(VerticalOffset - Math.Max(1.0, ViewportHeight));
    protected virtual void OnScrollPageDownCommand() => SetVerticalOffset(VerticalOffset + Math.Max(1.0, ViewportHeight));
    protected virtual void OnScrollPageLeftCommand() => SetHorizontalOffset(HorizontalOffset - Math.Max(1.0, ViewportWidth));
    protected virtual void OnScrollPageRightCommand() => SetHorizontalOffset(HorizontalOffset + Math.Max(1.0, ViewportWidth));
    protected virtual void OnMoveUpCommand() => SetVerticalOffset(VerticalOffset - LineScrollAmount);
    protected virtual void OnMoveDownCommand() => SetVerticalOffset(VerticalOffset + LineScrollAmount);
    protected virtual void OnMoveLeftCommand() => SetHorizontalOffset(HorizontalOffset - LineScrollAmount);
    protected virtual void OnMoveRightCommand() => SetHorizontalOffset(HorizontalOffset + LineScrollAmount);

    protected virtual void OnIncreaseZoomCommand()
    {
        if (!CanIncreaseZoom)
        {
            return;
        }

        for (var index = s_zoomLevels.Length - 1; index >= 0; index--)
        {
            if (s_zoomLevels[index] > Zoom)
            {
                Zoom = Math.Min(MaxZoom, s_zoomLevels[index]);
                return;
            }
        }

        Zoom = MaxZoom;
    }

    protected virtual void OnDecreaseZoomCommand()
    {
        if (!CanDecreaseZoom)
        {
            return;
        }

        foreach (var level in s_zoomLevels)
        {
            if (level < Zoom)
            {
                Zoom = Math.Max(MinZoom, level);
                return;
            }
        }

        Zoom = MinZoom;
    }

    protected override ReadOnlyCollection<DocumentPageView> GetPageViewsCollection(out bool changed)
    {
        changed = _pageViewsChanged;
        if (!_publishingPageViews)
        {
            _pageViewsChanged = false;
        }
        return _readOnlyPageViews;
    }

    #endregion

    #region Layout

    protected override int VisualChildrenCount => base.VisualChildrenCount + _pageViews.Count;

    protected override Visual? GetVisualChild(int index)
    {
        var baseCount = base.VisualChildrenCount;
        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        var pageIndex = index - baseCount;
        return pageIndex >= 0 && pageIndex < _pageViews.Count
            ? _pageViews[pageIndex]
            : throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var pageSize = GetScaledPageSize();
        var columns = GetEffectiveColumnCount();
        var rows = _pageViews.Count == 0 ? 0 : (int)Math.Ceiling((double)_pageViews.Count / columns);

        foreach (var pageView in _pageViews)
        {
            pageView.Measure(pageSize);
        }

        var extentWidth = _pageViews.Count == 0
            ? 0.0
            : (columns * pageSize.Width) + ((columns - 1) * HorizontalPageSpacing);
        var extentHeight = rows == 0
            ? 0.0
            : (rows * pageSize.Height) + ((rows - 1) * VerticalPageSpacing);
        var viewportWidth = NormalizeViewport(availableSize.Width, extentWidth);
        var viewportHeight = NormalizeViewport(availableSize.Height, extentHeight);

        SetLayoutMetrics(extentWidth, extentHeight, viewportWidth, viewportHeight);
        return new Size(viewportWidth, viewportHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var pageSize = GetScaledPageSize();
        var columns = GetEffectiveColumnCount();

        for (var index = 0; index < _pageViews.Count; index++)
        {
            var row = index / columns;
            var column = PageDisplay == DocumentViewerPageDisplay.TwoUpFacing &&
                _pageViews[index].PageNumber == 0
                ? 1
                : index % columns;
            var x = (column * (pageSize.Width + HorizontalPageSpacing)) - HorizontalOffset;
            var y = (row * (pageSize.Height + VerticalPageSpacing)) - VerticalOffset;
            _pageViews[index].Arrange(new Rect(x, y, pageSize.Width, pageSize.Height));
        }

        SetLayoutMetrics(ExtentWidth, ExtentHeight, finalSize.Width, finalSize.Height);
        return finalSize;
    }

    #endregion

    #region Helpers

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        new Jalium.UI.Automation.Peers.DocumentViewerAutomationPeer(this);

    private void RegisterCommandBindings()
    {
        AddCommandBinding(ViewThumbnailsCommand, static viewer => viewer.OnViewThumbnailsCommand(), static viewer => viewer.Document != null);
        AddCommandBinding(FitToWidthCommand, static viewer => viewer.OnFitToWidthCommand(), static viewer => viewer.Document != null);
        AddCommandBinding(FitToHeightCommand, static viewer => viewer.OnFitToHeightCommand(), static viewer => viewer.Document != null);
        CommandBindings.Add(new CommandBinding(
            FitToMaxPagesAcrossCommand,
            (_, args) =>
            {
                if (TryGetPagesAcross(args.Parameter, out var pagesAcross))
                {
                    OnFitToMaxPagesAcrossCommand(pagesAcross);
                }
                else
                {
                    OnFitToMaxPagesAcrossCommand();
                }
            },
            (_sender, args) => args.CanExecute = Document != null &&
                (args.Parameter == null || TryGetPagesAcross(args.Parameter, out _))));

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
            (_, args) => args.CanExecute = TryGetPageNumber(args.Parameter, out var pageNumber) &&
                pageNumber >= 1 && pageNumber <= PageCount));
        CommandBindings.Add(new CommandBinding(
            NavigationCommands.Zoom,
            (_, args) =>
            {
                if (TryGetZoom(args.Parameter, out var zoom))
                {
                    SetZoom(zoom);
                }
            },
            (_sender, args) => args.CanExecute = Document != null && TryGetZoom(args.Parameter, out _)));
        AddCommandBinding(NavigationCommands.IncreaseZoom, static viewer => viewer.OnIncreaseZoomCommand(), static viewer => viewer.CanIncreaseZoom && viewer.Document != null);
        AddCommandBinding(NavigationCommands.DecreaseZoom, static viewer => viewer.OnDecreaseZoomCommand(), static viewer => viewer.CanDecreaseZoom && viewer.Document != null);
        AddCommandBinding(ComponentCommands.ScrollPageUp, static viewer => viewer.OnScrollPageUpCommand(), static viewer => viewer.CanMoveUp);
        AddCommandBinding(ComponentCommands.ScrollPageDown, static viewer => viewer.OnScrollPageDownCommand(), static viewer => viewer.CanMoveDown);
        AddCommandBinding(ComponentCommands.ScrollPageLeft, static viewer => viewer.OnScrollPageLeftCommand(), static viewer => viewer.CanMoveLeft);
        AddCommandBinding(ComponentCommands.ScrollPageRight, static viewer => viewer.OnScrollPageRightCommand(), static viewer => viewer.CanMoveRight);
        AddCommandBinding(ComponentCommands.MoveUp, static viewer => viewer.OnMoveUpCommand(), static viewer => viewer.CanMoveUp);
        AddCommandBinding(ComponentCommands.MoveDown, static viewer => viewer.OnMoveDownCommand(), static viewer => viewer.CanMoveDown);
        AddCommandBinding(ComponentCommands.MoveLeft, static viewer => viewer.OnMoveLeftCommand(), static viewer => viewer.CanMoveLeft);
        AddCommandBinding(ComponentCommands.MoveRight, static viewer => viewer.OnMoveRightCommand(), static viewer => viewer.CanMoveRight);
        AddCommandBinding(ApplicationCommands.Print, static viewer => viewer.Print(), static viewer => viewer.Document != null && viewer.PageCount > 0);
        AddCommandBinding(
            ApplicationCommands.Find,
            static viewer => viewer.OnFindCommand(),
            static viewer => viewer._paginator is IDocumentTextSearchProvider && !string.IsNullOrEmpty(viewer.SearchText));
    }

    private void AddCommandBinding(RoutedUICommand command, Action<DocumentViewer> execute, Predicate<DocumentViewer> canExecute)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, _) => execute(this),
            (_, args) => args.CanExecute = canExecute(this)));
    }

    private void RefreshPageViews()
    {
        for (int i = _pageViews.Count - 1; i >= 0; i--)
        {
            var pageView = _pageViews[i];
            _pageViews.RemoveAt(i);
            RemoveVisualChild(pageView);
        }
        if (_paginator != null && PageCount > 0)
        {
            foreach (var pageNumber in GetDisplayedPageNumbers())
            {
                var pageView = new DocumentPageView
                {
                    DocumentPaginator = _paginator,
                    PageNumber = pageNumber,
                    BorderThickness = new Thickness(ShowPageBorders ? 1.0 : 0.0),
                };
                _pageViews.Add(pageView);
                AddVisualChild(pageView);
            }
        }

        _pageViewsChanged = true;
        _publishingPageViews = true;
        try
        {
            InvalidatePageViews();
        }
        finally
        {
            _publishingPageViews = false;
        }
        InvalidateMeasure();
    }

    private IEnumerable<int> GetDisplayedPageNumbers()
    {
        if (PageDisplay is DocumentViewerPageDisplay.Continuous or DocumentViewerPageDisplay.ContinuousFacing)
        {
            return Enumerable.Range(0, PageCount);
        }

        if (PageDisplay == DocumentViewerPageDisplay.OnePage)
        {
            return CurrentPage > 0 ? new[] { CurrentPage - 1 } : Array.Empty<int>();
        }

        if (PageDisplay == DocumentViewerPageDisplay.TwoUpFacing)
        {
            if (CurrentPage <= 1)
            {
                return new[] { 0 };
            }

            var facingFirst = 1 + (((CurrentPage - 2) / 2) * 2);
            var facingCount = Math.Min(2, PageCount - facingFirst);
            return facingCount > 0 ? Enumerable.Range(facingFirst, facingCount) : Array.Empty<int>();
        }

        var first = Math.Max(0, ((Math.Max(1, CurrentPage) - 1) / 2) * 2);
        var count = Math.Min(2, PageCount - first);
        return count > 0 ? Enumerable.Range(first, count) : Array.Empty<int>();
    }

    private int GetLayoutColumns()
    {
        return PageDisplay switch
        {
            DocumentViewerPageDisplay.OnePage => 1,
            DocumentViewerPageDisplay.TwoPages or DocumentViewerPageDisplay.TwoUpFacing => 2,
            DocumentViewerPageDisplay.ContinuousFacing => Math.Max(2, MaxPagesAcross),
            _ => MaxPagesAcross,
        };
    }

    private int GetEffectiveColumnCount()
    {
        if (PageDisplay == DocumentViewerPageDisplay.TwoUpFacing && _pageViews.Count > 0)
        {
            return 2;
        }

        return Math.Max(1, Math.Min(GetLayoutColumns(), Math.Max(1, _pageViews.Count)));
    }

    private Size GetUnscaledPageSize()
    {
        var pageSize = _paginator?.PageSize ?? Size.Empty;
        return !pageSize.IsEmpty && pageSize.Width > 0 && pageSize.Height > 0
            ? pageSize
            : new Size(816.0, 1056.0);
    }

    private Size GetScaledPageSize()
    {
        var pageSize = GetUnscaledPageSize();
        var scale = Zoom / 100.0;
        return new Size(pageSize.Width * scale, pageSize.Height * scale);
    }

    private void SetLayoutMetrics(double extentWidth, double extentHeight, double viewportWidth, double viewportHeight)
    {
        viewportWidth = Math.Max(0.0, viewportWidth);
        viewportHeight = Math.Max(0.0, viewportHeight);
        SetValue(ExtentWidthPropertyKey, Math.Max(0.0, extentWidth));
        SetValue(ExtentHeightPropertyKey, Math.Max(0.0, extentHeight));
        SetValue(ViewportWidthPropertyKey, viewportWidth);
        SetValue(ViewportHeightPropertyKey, viewportHeight);

        var maxHorizontal = Math.Max(0.0, ExtentWidth - ViewportWidth);
        var maxVertical = Math.Max(0.0, ExtentHeight - ViewportHeight);
        if (HorizontalOffset > maxHorizontal)
        {
            SetCurrentValue(HorizontalOffsetProperty, maxHorizontal);
        }
        if (VerticalOffset > maxVertical)
        {
            SetCurrentValue(VerticalOffsetProperty, maxVertical);
        }

        UpdateMoveState();
    }

    private void UpdateMoveState()
    {
        var maxHorizontal = Math.Max(0.0, ExtentWidth - ViewportWidth);
        var maxVertical = Math.Max(0.0, ExtentHeight - ViewportHeight);
        SetValue(CanMoveLeftPropertyKey, HorizontalOffset > 0.0);
        SetValue(CanMoveRightPropertyKey, HorizontalOffset < maxHorizontal);
        SetValue(CanMoveUpPropertyKey, VerticalOffset > 0.0);
        SetValue(CanMoveDownPropertyKey, VerticalOffset < maxVertical);
    }

    private void UpdateZoomState()
    {
        SetValue(CanIncreaseZoomPropertyKey, Zoom < MaxZoom);
        SetValue(CanDecreaseZoomPropertyKey, Zoom > MinZoom);
    }

    private void SetHorizontalOffset(double offset) =>
        HorizontalOffset = Math.Clamp(offset, 0.0, Math.Max(0.0, ExtentWidth - ViewportWidth));

    private void SetVerticalOffset(double offset) =>
        VerticalOffset = Math.Clamp(offset, 0.0, Math.Max(0.0, ExtentHeight - ViewportHeight));

    private void ScrollCurrentPageIntoView()
    {
        if (PageDisplay is not (DocumentViewerPageDisplay.Continuous or DocumentViewerPageDisplay.ContinuousFacing) || CurrentPage <= 0)
        {
            return;
        }

        var columns = Math.Max(1, GetLayoutColumns());
        var row = (CurrentPage - 1) / columns;
        var pageSize = GetScaledPageSize();
        SetVerticalOffset(row * (pageSize.Height + VerticalPageSpacing));
    }

    private void SetZoomFromViewport(bool fitWidth, bool fitHeight, int columns)
    {
        var pageSize = GetUnscaledPageSize();
        var zoom = MaxZoom;

        if (fitWidth)
        {
            var viewportWidth = GetEffectiveViewportWidth();
            if (viewportWidth > 0)
            {
                var availableWidth = Math.Max(1.0, viewportWidth - ((columns - 1) * HorizontalPageSpacing));
                zoom = Math.Min(zoom, availableWidth / (pageSize.Width * columns) * 100.0);
            }
        }

        if (fitHeight)
        {
            var viewportHeight = GetEffectiveViewportHeight();
            if (viewportHeight > 0)
            {
                zoom = Math.Min(zoom, viewportHeight / pageSize.Height * 100.0);
            }
        }

        if (zoom < MaxZoom)
        {
            _updatingFitMode = true;
            try
            {
                Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            }
            finally
            {
                _updatingFitMode = false;
            }
        }
    }

    private double GetEffectiveViewportWidth() =>
        ViewportWidth > 0 ? ViewportWidth : Math.Max(0.0, ActualWidth);

    private double GetEffectiveViewportHeight() =>
        ViewportHeight > 0 ? ViewportHeight : Math.Max(0.0, ActualHeight);

    private void SetFitMode(DocumentViewerFitMode fitMode)
    {
        _updatingFitMode = true;
        try
        {
            FitMode = fitMode;
        }
        finally
        {
            _updatingFitMode = false;
        }
    }

    private void NavigateToSearchResult(TextSearchResult result)
    {
        OnGoToPageCommand(result.PageNumber);
        OnBringIntoView(this, result.BoundingRect, result.PageNumber);
        InvalidateVisual();
    }

    private void OnSearchCompleted(int resultCount) =>
        SearchCompleted?.Invoke(this, new SearchCompletedEventArgs(resultCount));

    private static double NormalizeViewport(double requested, double extent) =>
        double.IsFinite(requested) ? Math.Max(0.0, requested) : Math.Max(0.0, extent);

    private static bool IsValidOffset(object? value) =>
        value is double offset && double.IsFinite(offset) && offset >= 0.0;

    private static bool IsValidPageSpacing(object? value) => IsValidOffset(value);

    private static bool IsValidMaxPagesAcross(object? value) =>
        value is int pagesAcross && pagesAcross is >= 1 and <= MaximumPagesAcross;

    private static bool TryGetPagesAcross(object? value, out int pagesAcross)
    {
        if (value is int integer && IsValidMaxPagesAcross(integer))
        {
            pagesAcross = integer;
            return true;
        }

        if (value is string text &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer) &&
            IsValidMaxPagesAcross(integer))
        {
            pagesAcross = integer;
            return true;
        }

        pagesAcross = 0;
        return false;
    }

    private static bool TryGetPageNumber(object? value, out int pageNumber)
    {
        if (value is int integer)
        {
            pageNumber = integer;
            return true;
        }

        return int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out pageNumber);
    }

    private static bool TryGetZoom(object? value, out double zoom)
    {
        if (value is double number && double.IsFinite(number))
        {
            zoom = number;
            return true;
        }

        return double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out zoom)
            && double.IsFinite(zoom);
    }

    private static object? CoerceZoom(DependencyObject d, object? value)
    {
        if (d is not DocumentViewer viewer || value is not double zoom || !double.IsFinite(zoom))
        {
            return 100.0;
        }

        return Math.Clamp(zoom, viewer.MinZoom, viewer.MaxZoom);
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DocumentViewer viewer)
        {
            return;
        }

        viewer.UpdateZoomState();
        if (!viewer._updatingFitMode)
        {
            viewer.SetFitMode(DocumentViewerFitMode.None);
        }
        viewer.InvalidateMeasure();
    }

    private static void OnZoomBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.CoerceValue(ZoomProperty);
            viewer.UpdateZoomState();
        }
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.UpdateMoveState();
            viewer.InvalidateArrange();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.RefreshPageViews();
        }
    }

    private static void OnPageDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            viewer.RefreshPageViews();
        }
    }

    private static void OnShowPageBordersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentViewer viewer)
        {
            var thickness = new Thickness(viewer.ShowPageBorders ? 1.0 : 0.0);
            foreach (var pageView in viewer._pageViews)
            {
                pageView.BorderThickness = thickness;
            }
            viewer.InvalidateVisual();
        }
    }

    private static void OnFitModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DocumentViewer viewer || viewer._updatingFitMode)
        {
            return;
        }

        switch (viewer.FitMode)
        {
            case DocumentViewerFitMode.FitWidth:
                viewer.OnFitToWidthCommand();
                break;
            case DocumentViewerFitMode.FitPage:
                viewer.FitToPage();
                break;
            case DocumentViewerFitMode.MaxWidth:
                viewer.OnFitToMaxPagesAcrossCommand();
                break;
        }
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

/// <summary>
/// Optional paginator capability used by <see cref="DocumentViewer"/> to perform
/// real text search without assuming a particular document text model.
/// </summary>
public interface IDocumentTextSearchProvider
{
    /// <summary>Finds matching text ranges in the paginated document.</summary>
    IEnumerable<TextSearchResult> Find(string text, bool matchCase, bool matchWholeWord);
}
