using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using PrimitiveDocumentPaginator = Jalium.UI.Controls.Primitives.DocumentPaginator;
using PrimitiveDocumentPaginatorSource = Jalium.UI.Controls.Primitives.IDocumentPaginatorSource;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays a <see cref="FlowDocument"/> one page at a time.
/// </summary>
[ContentProperty("Document")]
public class FlowDocumentPageViewer : DocumentViewerBase
{
    private static readonly ReadOnlyCollection<DocumentPageView> s_emptyPageViews =
        new(Array.Empty<DocumentPageView>());

    private readonly DocumentPageView _pageView;
    private readonly ReadOnlyCollection<DocumentPageView> _singlePageView;
    private readonly FlowDocumentSearchSession _search = new();
    private FlowDocument? _attachedDocument;
    private TextSelection? _externalSelection;
    private bool _pageViewsDirty = true;

    /// <summary>
    /// Initializes a flow-document page viewer and its master page surface.
    /// </summary>
    public FlowDocumentPageViewer()
    {
        _pageView = new DocumentPageView
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
        };
        SetIsMasterPage(_pageView, true);
        _singlePageView = new ReadOnlyCollection<DocumentPageView>([_pageView]);
        AddVisualChild(_pageView);
        RegisterFlowDocumentCommands();
        UpdateZoomState();
        InvalidatePageViews();
    }

    #region Dependency properties

    public new static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document), typeof(FlowDocument), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(null, OnFlowDocumentChanged));

    public new static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(
            nameof(Zoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom),
            FlowDocumentViewerSupport.IsValidZoomValue);

    public new static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(
            nameof(MinZoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(80.0, OnMinZoomChanged),
            FlowDocumentViewerSupport.IsValidZoomValue);

    public new static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(
            nameof(MaxZoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(200.0, OnMaxZoomChanged, CoerceMaxZoom),
            FlowDocumentViewerSupport.IsValidZoomValue);

    public new static readonly DependencyProperty ZoomIncrementProperty =
        DependencyProperty.Register(
            nameof(ZoomIncrement), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(10.0),
            FlowDocumentViewerSupport.IsValidZoomValue);

    protected static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanIncreaseZoom), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(true));

    public static readonly DependencyProperty CanIncreaseZoomProperty =
        CanIncreaseZoomPropertyKey.DependencyProperty;

    protected static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanDecreaseZoom), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(true));

    public static readonly DependencyProperty CanDecreaseZoomProperty =
        CanDecreaseZoomPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBrushProperty =
        TextBoxBase.SelectionBrushProperty.AddOwner(
            typeof(FlowDocumentPageViewer),
            new PropertyMetadata(SystemColors.HighlightBrush, OnSelectionVisualChanged));

    public static readonly DependencyProperty SelectionOpacityProperty =
        TextBoxBase.SelectionOpacityProperty.AddOwner(
            typeof(FlowDocumentPageViewer),
            new PropertyMetadata(1.0, OnSelectionVisualChanged));

    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsSelectionActive), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectionActiveProperty =
        IsSelectionActivePropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty =
        TextBoxBase.IsInactiveSelectionHighlightEnabledProperty.AddOwner(
            typeof(FlowDocumentPageViewer), new PropertyMetadata(false, OnSelectionVisualChanged));

    #endregion

    #region Properties

    public new FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public new double Zoom
    {
        get => (double)GetValue(ZoomProperty)!;
        set => SetValue(ZoomProperty, value);
    }

    public new double MinZoom
    {
        get => (double)GetValue(MinZoomProperty)!;
        set => SetValue(MinZoomProperty, value);
    }

    public new double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty)!;
        set => SetValue(MaxZoomProperty, value);
    }

    public new double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty)!;
        set => SetValue(ZoomIncrementProperty, value);
    }

    public virtual bool CanIncreaseZoom => (bool)GetValue(CanIncreaseZoomProperty)!;

    public virtual bool CanDecreaseZoom => (bool)GetValue(CanDecreaseZoomProperty)!;

    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public double SelectionOpacity
    {
        get => (double)GetValue(SelectionOpacityProperty)!;
        set => SetValue(SelectionOpacityProperty, value);
    }

    public bool IsSelectionActive => (bool)GetValue(IsSelectionActiveProperty)!;

    public bool IsInactiveSelectionHighlightEnabled
    {
        get => (bool)GetValue(IsInactiveSelectionHighlightEnabledProperty)!;
        set => SetValue(IsInactiveSelectionHighlightEnabledProperty, value);
    }

    public TextSelection? Selection => _externalSelection ?? _search.Selection;

    /// <summary>
    /// Gets the one-based page number currently displayed.
    /// </summary>
    public int PageNumber => MasterPageNumber;

    /// <summary>
    /// Gets or sets the text used by the parameterless find command.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets whether the built-in find affordance is currently active.
    /// </summary>
    public bool IsFindToolBarVisible { get; private set; }

    #endregion

    #region Public commands

    public new void IncreaseZoom() => OnIncreaseZoomCommand();

    public new void DecreaseZoom() => OnDecreaseZoomCommand();

    public void Find() => OnFindCommand();

    /// <summary>
    /// Finds the next occurrence and makes its page current.
    /// </summary>
    public bool Find(string searchText)
    {
        SearchText = searchText ?? throw new ArgumentNullException(nameof(searchText));
        _externalSelection = null;
        if (!_search.Find(SearchText))
        {
            return false;
        }

        NavigateToSelection();
        return true;
    }

    #endregion

    #region Protected hooks

    protected override void OnDocumentChanged()
    {
        if (_attachedDocument != null)
        {
            _attachedDocument.ViewerPaginationChanged -= OnPaginationChanged;
        }

        _attachedDocument = Document;
        if (_attachedDocument != null)
        {
            _attachedDocument.ViewerPaginationChanged += OnPaginationChanged;
        }

        _search.Attach(_attachedDocument);
        _externalSelection = null;
        base.OnDocumentChanged();
        SetMasterPageNumber(PageCount > 0 ? 1 : 0);
        _pageViewsDirty = true;
        InvalidatePageViews();
        UpdateCurrentPageView();
    }

    protected override void OnPageViewsChanged()
    {
        UpdateCurrentPageView();
        base.OnPageViewsChanged();
    }

    protected override void OnMasterPageNumberChanged()
    {
        UpdateCurrentPageView();
        base.OnMasterPageNumberChanged();
    }

    protected override void OnPreviousPageCommand()
    {
        if (CanGoToPreviousPage)
        {
            base.OnPreviousPageCommand();
        }
    }

    protected override void OnNextPageCommand()
    {
        if (CanGoToNextPage)
        {
            base.OnNextPageCommand();
        }
    }

    protected override void OnFirstPageCommand()
    {
        if (CanGoToPreviousPage)
        {
            base.OnFirstPageCommand();
        }
    }

    protected override void OnLastPageCommand()
    {
        if (CanGoToNextPage)
        {
            base.OnLastPageCommand();
        }
    }

    protected override void OnGoToPageCommand(int pageNumber)
    {
        if (CanGoToPage(pageNumber) && MasterPageNumber != pageNumber)
        {
            base.OnGoToPageCommand(pageNumber);
        }
    }

    protected virtual void OnFindCommand()
    {
        IsFindToolBarVisible = !IsFindToolBarVisible;
        _externalSelection = null;
        if (!string.IsNullOrWhiteSpace(SearchText) && _search.Find(SearchText))
        {
            NavigateToSelection();
        }
    }

    protected override void OnPrintCommand()
    {
        base.OnPrintCommand();
        OnPrintCompleted();
    }

    protected override void OnCancelPrintCommand()
    {
        base.OnCancelPrintCommand();
    }

    protected virtual void OnPrintCompleted()
    {
        CommandManager.InvalidateRequerySuggested();
    }

    protected virtual void OnIncreaseZoomCommand()
    {
        if (CanIncreaseZoom)
        {
            Zoom = Math.Min(MaxZoom, Zoom + ZoomIncrement);
        }
    }

    protected virtual void OnDecreaseZoomCommand()
    {
        if (CanDecreaseZoom)
        {
            Zoom = Math.Max(MinZoom, Zoom - ZoomIncrement);
        }
    }

    protected override ReadOnlyCollection<DocumentPageView> GetPageViewsCollection(out bool changed)
    {
        changed = _pageViewsDirty;
        _pageViewsDirty = false;
        return Document != null && PageCount > 0 ? _singlePageView : s_emptyPageViews;
    }

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        SetValue(IsSelectionActivePropertyKey, isFocusWithin);
        ApplySelectionToCurrentPage();
    }

    #endregion

    #region Layout

    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;

    protected override Visual? GetVisualChild(int index)
    {
        var baseCount = base.VisualChildrenCount;
        if (index < baseCount)
        {
            return base.GetVisualChild(index);
        }

        return index == baseCount ? _pageView : throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Document == null || PageCount == 0)
        {
            _pageView.Measure(Size.Empty);
            return base.MeasureOverride(availableSize);
        }

        var pageSize = Document.ViewerPaginator.PageSize;
        var scale = Zoom / 100.0;
        var target = new Size(Math.Max(0.0, pageSize.Width * scale), Math.Max(0.0, pageSize.Height * scale));
        _pageView.Measure(target);
        ApplySelectionToCurrentPage();
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? target.Width : Math.Min(target.Width, availableSize.Width),
            double.IsPositiveInfinity(availableSize.Height) ? target.Height : Math.Min(target.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var desired = _pageView.DesiredSize;
        var width = Math.Min(finalSize.Width, desired.Width);
        var height = Math.Min(finalSize.Height, desired.Height);
        _pageView.Arrange(new Rect(
            Math.Max(0.0, (finalSize.Width - width) / 2.0),
            Math.Max(0.0, (finalSize.Height - height) / 2.0),
            width,
            height));
        return finalSize;
    }

    #endregion

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        new Jalium.UI.Automation.Peers.FlowDocumentPageViewerAutomationPeer(this);

    private void RegisterFlowDocumentCommands()
    {
        AddCommandBinding(ApplicationCommands.Find, static viewer => viewer.OnFindCommand(), static viewer => viewer.Document != null);
        AddCommandBinding(NavigationCommands.IncreaseZoom, static viewer => viewer.OnIncreaseZoomCommand(), static viewer => viewer.CanIncreaseZoom);
        AddCommandBinding(NavigationCommands.DecreaseZoom, static viewer => viewer.OnDecreaseZoomCommand(), static viewer => viewer.CanDecreaseZoom);
        AddCommandBinding(ApplicationCommands.SelectAll, static viewer => viewer.SelectAll(), static viewer => viewer.Document != null);
    }

    private void AddCommandBinding(RoutedUICommand command, Action<FlowDocumentPageViewer> execute, Predicate<FlowDocumentPageViewer> canExecute)
    {
        CommandBindings.Add(new CommandBinding(
            command,
            (_, _) => execute(this),
            (_, args) => args.CanExecute = canExecute(this)));
    }

    private void SelectAll()
    {
        _externalSelection = null;
        _search.SelectAll();
        InvalidateVisual();
    }

    private void NavigateToSelection()
    {
        if (Document == null || Selection == null)
        {
            return;
        }

        var pageNumber = FlowDocumentViewerSupport.GetPageNumberForOffset(Document, Selection.Start.DocumentOffset);
        OnGoToPageCommand(pageNumber);
        InvalidateVisual();
    }

    private void UpdateCurrentPageView()
    {
        _pageView.DocumentPaginator = Document?.ViewerPaginator;
        _pageView.PageNumber = Math.Max(0, MasterPageNumber - 1);
        _pageView.InvalidateMeasure();
        InvalidateMeasure();
    }

    internal void ApplySelection(TextSelection? selection)
    {
        _externalSelection = selection;
        ApplySelectionToCurrentPage();
        InvalidateVisual();
    }

    private void ApplySelectionToCurrentPage()
    {
        if (_pageView.DocumentPage?.Visual is not TextBlock textBlock)
        {
            return;
        }

        textBlock.SelectionBrush = CreateSelectionBrush(SelectionBrush, SelectionOpacity);
        if (Document == null || Selection == null ||
            (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled))
        {
            textBlock.Select(0, 0);
            return;
        }

        var pageStart = FlowDocumentViewerSupport.GetPageStartOffset(Document, Math.Max(1, MasterPageNumber));
        var localStart = Math.Clamp(Selection.Start.DocumentOffset - pageStart, 0, textBlock.Text.Length);
        var localEnd = Math.Clamp(Selection.End.DocumentOffset - pageStart, localStart, textBlock.Text.Length);
        textBlock.Select(localStart, localEnd - localStart);
    }

    private static Brush? CreateSelectionBrush(Brush? brush, double opacity)
    {
        if (brush is SolidColorBrush solidColorBrush)
        {
            return new SolidColorBrush(solidColorBrush.Color)
            {
                Opacity = solidColorBrush.Opacity * Math.Clamp(opacity, 0.0, 1.0),
            };
        }

        return brush;
    }

    private void UpdateZoomState()
    {
        SetValue(CanIncreaseZoomPropertyKey, Zoom < MaxZoom);
        SetValue(CanDecreaseZoomPropertyKey, Zoom > MinZoom);
        InvalidateMeasure();
    }

    private void OnPaginationChanged(object? sender, EventArgs e)
    {
        var oldPage = MasterPageNumber;
        base.OnDocumentChanged();
        SetMasterPageNumber(PageCount > 0 ? Math.Clamp(oldPage, 1, PageCount) : 0);
        _pageViewsDirty = true;
        InvalidatePageViews();
        UpdateCurrentPageView();
    }

    private static void OnFlowDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.SetDocumentPaginatorSource(e.NewValue is FlowDocument document
                ? new FlowDocumentPaginatorSource(document)
                : null);
        }
    }

    private void SetDocumentPaginatorSource(PrimitiveDocumentPaginatorSource? source) =>
        base.Document = source;

    internal sealed class FlowDocumentPaginatorSource : PrimitiveDocumentPaginatorSource
    {
        private readonly FlowDocument _document;

        public FlowDocumentPaginatorSource(FlowDocument document)
        {
            _document = document;
        }

        public PrimitiveDocumentPaginator DocumentPaginator => _document.ViewerPaginator;
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.UpdateZoomState();
        }
    }

    private static void OnMinZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.CoerceValue(MaxZoomProperty);
            viewer.CoerceValue(ZoomProperty);
            viewer.UpdateZoomState();
        }
    }

    private static void OnMaxZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.CoerceValue(ZoomProperty);
            viewer.UpdateZoomState();
        }
    }

    private static object? CoerceZoom(DependencyObject d, object? value) =>
        d is FlowDocumentPageViewer viewer && value is double zoom
            ? FlowDocumentViewerSupport.CoerceZoom(zoom, viewer.MinZoom, viewer.MaxZoom)
            : value;

    private static object? CoerceMaxZoom(DependencyObject d, object? value) =>
        d is FlowDocumentPageViewer viewer && value is double maxZoom
            ? Math.Max(viewer.MinZoom, maxZoom)
            : value;

    private static void OnSelectionVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.ApplySelectionToCurrentPage();
            viewer.InvalidateVisual();
        }
    }
}
