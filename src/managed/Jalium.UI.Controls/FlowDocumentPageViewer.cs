using System.Collections.ObjectModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Displays a <see cref = "FlowDocument"/> one page at a time.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Document")]
[TemplatePart(Name = "PART_PagePresenter", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_FindTextBox", Type = typeof(TextBox))]
public class FlowDocumentPageViewer : DocumentViewerBase
{
    private static readonly ReadOnlyCollection<DocumentPageView> s_emptyPageViews = new(Array.Empty<DocumentPageView>());
    private readonly DocumentPageView _pageView;
    private ContentPresenter? _pagePresenter;
    private TextBox? _findTextBox;
    private readonly ReadOnlyCollection<DocumentPageView> _singlePageView;
    private readonly FlowDocumentSearchSession _search = new();
    private FlowDocument? _attachedDocument;
    private TextSelection? _externalSelection;
    private bool _pageViewsDirty = true;
    static FlowDocumentPageViewer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlowDocumentPageViewer), new PropertyMetadata(typeof(FlowDocumentPageViewer)));
    }

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
        AddLogicalChild(_pageView);
        SetValue(PageViewPropertyKey, _pageView);
        RegisterFlowDocumentCommands();
        UpdateZoomState();
        InvalidatePageViews();
    }

#region Dependency properties
    private static readonly DependencyPropertyKey PageViewPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PageView), typeof(DocumentPageView), typeof(FlowDocumentPageViewer), new PropertyMetadata(null));
    public static readonly DependencyProperty PageViewProperty = PageViewPropertyKey.DependencyProperty;
    /// <summary>Gets the retained master-page content for control templates.</summary>
    public DocumentPageView PageView => (DocumentPageView)GetValue(PageViewProperty)!;

    private static readonly DependencyPropertyKey PageViewWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PageViewWidth), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(0.0));
    public static readonly DependencyProperty PageViewWidthProperty = PageViewWidthPropertyKey.DependencyProperty;
    public double PageViewWidth => (double)GetValue(PageViewWidthProperty)!;

    private static readonly DependencyPropertyKey PageViewHeightPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PageViewHeight), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(0.0));
    public static readonly DependencyProperty PageViewHeightProperty = PageViewHeightPropertyKey.DependencyProperty;
    public double PageViewHeight => (double)GetValue(PageViewHeightProperty)!;

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(FlowDocumentPageViewer), new PropertyMetadata(string.Empty, OnSearchTextChanged));
    private static readonly DependencyPropertyKey IsFindToolBarVisiblePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFindToolBarVisible), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsFindToolBarVisibleProperty = IsFindToolBarVisiblePropertyKey.DependencyProperty;
    public new static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentPageViewer), new PropertyMetadata(null, OnFlowDocumentChanged));
    public new static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom), FlowDocumentViewerSupport.IsValidZoomValue);
    public new static readonly DependencyProperty MinZoomProperty = DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(80.0, OnMinZoomChanged), FlowDocumentViewerSupport.IsValidZoomValue);
    public new static readonly DependencyProperty MaxZoomProperty = DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(200.0, OnMaxZoomChanged, CoerceMaxZoom), FlowDocumentViewerSupport.IsValidZoomValue);
    public new static readonly DependencyProperty ZoomIncrementProperty = DependencyProperty.Register(nameof(ZoomIncrement), typeof(double), typeof(FlowDocumentPageViewer), new PropertyMetadata(10.0), FlowDocumentViewerSupport.IsValidZoomValue);
    protected static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanIncreaseZoom), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanIncreaseZoomProperty = CanIncreaseZoomPropertyKey.DependencyProperty;
    protected static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanDecreaseZoom), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanDecreaseZoomProperty = CanDecreaseZoomPropertyKey.DependencyProperty;
    public static readonly DependencyProperty SelectionBrushProperty = TextBoxBase.SelectionBrushProperty.AddOwner(typeof(FlowDocumentPageViewer), new PropertyMetadata(SystemColors.HighlightBrush, OnSelectionVisualChanged));
    public static readonly DependencyProperty SelectionOpacityProperty = TextBoxBase.SelectionOpacityProperty.AddOwner(typeof(FlowDocumentPageViewer), new PropertyMetadata(1.0, OnSelectionVisualChanged));
    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSelectionActive), typeof(bool), typeof(FlowDocumentPageViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectionActiveProperty = IsSelectionActivePropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty = TextBoxBase.IsInactiveSelectionHighlightEnabledProperty.AddOwner(typeof(FlowDocumentPageViewer), new PropertyMetadata(false, OnSelectionVisualChanged));
    public static readonly RoutedUICommand FindNextCommand = FlowDocumentScrollViewer.FindNextCommand;
    public static readonly RoutedUICommand CloseFindCommand = FlowDocumentScrollViewer.CloseFindCommand;
#endregion
#region Properties
    public new FlowDocument? Document { get => (FlowDocument? )GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public new double Zoom { get => (double)GetValue(ZoomProperty)!; set => SetValue(ZoomProperty, value); }
    public new double MinZoom { get => (double)GetValue(MinZoomProperty)!; set => SetValue(MinZoomProperty, value); }
    public new double MaxZoom { get => (double)GetValue(MaxZoomProperty)!; set => SetValue(MaxZoomProperty, value); }
    public new double ZoomIncrement { get => (double)GetValue(ZoomIncrementProperty)!; set => SetValue(ZoomIncrementProperty, value); }
    public virtual bool CanIncreaseZoom => (bool)GetValue(CanIncreaseZoomProperty)!;
    public virtual bool CanDecreaseZoom => (bool)GetValue(CanDecreaseZoomProperty)!;
    public Brush? SelectionBrush { get => (Brush? )GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public double SelectionOpacity { get => (double)GetValue(SelectionOpacityProperty)!; set => SetValue(SelectionOpacityProperty, value); }
    public bool IsSelectionActive => (bool)GetValue(IsSelectionActiveProperty)!;
    public bool IsInactiveSelectionHighlightEnabled { get => (bool)GetValue(IsInactiveSelectionHighlightEnabledProperty)!; set => SetValue(IsInactiveSelectionHighlightEnabledProperty, value); }
    public TextSelection? Selection => _externalSelection ?? _search.Selection;
    /// <summary>
    /// Gets the one-based page number currently displayed.
    /// </summary>
    public int PageNumber => MasterPageNumber;
    /// <summary>
    /// Gets or sets the text used by the parameterless find command.
    /// </summary>
    public string SearchText { get => (string? )GetValue(SearchTextProperty) ?? string.Empty; set => SetValue(SearchTextProperty, value ?? string.Empty); }
    /// <summary>
    /// Gets whether the built-in find affordance is currently active.
    /// </summary>
    public bool IsFindToolBarVisible => (bool)GetValue(IsFindToolBarVisibleProperty)!;

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
        SetCurrentValue(SearchTextProperty, searchText ?? throw new ArgumentNullException(nameof(searchText)));
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
        if (Document == null)
            return;
        SetValue(IsFindToolBarVisiblePropertyKey, !IsFindToolBarVisible);
        UpdateVisualState();
        CommandManager.InvalidateRequerySuggested();
        if (IsFindToolBarVisible)
        {
            var input = _findTextBox;
            Dispatcher.BeginInvoke(() =>
            {
                if (IsFindToolBarVisible && ReferenceEquals(_findTextBox, input))
                    input?.Focus();
            });
        }

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
            SetCurrentValue(ZoomProperty, Math.Min(MaxZoom, Zoom + ZoomIncrement));
        }
    }

    protected virtual void OnDecreaseZoomCommand()
    {
        if (CanDecreaseZoom)
        {
            SetCurrentValue(ZoomProperty, Math.Max(MinZoom, Zoom - ZoomIncrement));
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
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _pagePresenter = GetTemplateChild("PART_PagePresenter") as ContentPresenter;
        _findTextBox = GetTemplateChild("PART_FindTextBox") as TextBox;
        if (_findTextBox != null)
            _findTextBox.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFindPreviewKeyDown));
        UpdateVisualState();
    }

    internal override void OnTemplateContentClearing()
    {
        if (_findTextBox != null)
            _findTextBox.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFindPreviewKeyDown));
        _findTextBox = null;
        if (_pagePresenter != null && ReferenceEquals(_pagePresenter.Content, _pageView))
            _pagePresenter.Content = null;
        _pagePresenter = null;
        base.OnTemplateContentClearing();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, Document == null ? "Empty" : "Ready", false);
        VisualStateManager.GoToState(this, IsFindToolBarVisible ? "FindOpen" : "FindClosed", false);
    }

    private void OnFindPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Find(SearchText);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void CloseFind()
    {
        SetValue(IsFindToolBarVisiblePropertyKey, false);
        UpdateVisualState();
        Focus();
        CommandManager.InvalidateRequerySuggested();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = Document != null && PageCount > 0 ? Document.ViewerPaginator.PageSize : Size.Empty;
        SetValue(PageViewWidthPropertyKey, Math.Max(0.0, size.Width * Zoom / 100.0));
        SetValue(PageViewHeightPropertyKey, Math.Max(0.0, size.Height * Zoom / 100.0));
        var desired = base.MeasureOverride(availableSize);
        ApplySelectionToCurrentPage();
        return desired;
    }

#endregion
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.FlowDocumentPageViewerAutomationPeer(this);
    private void RegisterFlowDocumentCommands()
    {
        AddCommandBinding(ApplicationCommands.Find, static viewer => viewer.OnFindCommand(), static viewer => viewer.Document != null);
        AddCommandBinding(FindNextCommand, static viewer => viewer.Find(viewer.SearchText), static viewer => viewer.Document != null && !string.IsNullOrWhiteSpace(viewer.SearchText));
        AddCommandBinding(CloseFindCommand, static viewer => viewer.CloseFind(), static viewer => viewer.IsFindToolBarVisible);
        AddCommandBinding(NavigationCommands.IncreaseZoom, static viewer => viewer.OnIncreaseZoomCommand(), static viewer => viewer.CanIncreaseZoom);
        AddCommandBinding(NavigationCommands.DecreaseZoom, static viewer => viewer.OnDecreaseZoomCommand(), static viewer => viewer.CanDecreaseZoom);
        AddCommandBinding(ApplicationCommands.SelectAll, static viewer => viewer.SelectAll(), static viewer => viewer.Document != null);
    }

    private void AddCommandBinding(RoutedUICommand command, Action<FlowDocumentPageViewer> execute, Predicate<FlowDocumentPageViewer> canExecute)
    {
        CommandBindings.Add(new CommandBinding(command, (_, _) => execute(this), (_, args) => args.CanExecute = canExecute(this)));
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

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => CommandManager.InvalidateRequerySuggested();
    private void UpdateCurrentPageView()
    {
        _pageView.DocumentPaginator = Document?.ViewerPaginator;
        _pageView.PageNumber = Math.Max(0, MasterPageNumber - 1);
        _pageView.InvalidateMeasure();
        if (Document == null)
            SetValue(IsFindToolBarVisiblePropertyKey, false);
        UpdateVisualState();
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
        if (_pageView.DocumentPage?.Visual is FlowDocumentPresentation documentView)
        {
            documentView.SelectionBrush = CreateSelectionBrush(SelectionBrush, SelectionOpacity);
            documentView.SetSelection(Selection, Document is not null && (IsSelectionActive || IsInactiveSelectionHighlightEnabled));
            return;
        }

        if (_pageView.DocumentPage?.Visual is not TextBlock textBlock)
        {
            return;
        }

        textBlock.SelectionBrush = CreateSelectionBrush(SelectionBrush, SelectionOpacity);
        if (Document == null || Selection == null || (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled))
        {
            textBlock.Select(0, 0);
            return;
        }

        var pageStart = FlowDocumentViewerSupport.GetPageStartOffset(Document, Math.Max(1, MasterPageNumber));
        var localStart = Math.Clamp(Selection.Start.DocumentOffset - pageStart, 0, textBlock.Text.Length);
        var localEnd = Math.Clamp(Selection.End.DocumentOffset - pageStart, localStart, textBlock.Text.Length);
        textBlock.Select(localStart, localEnd - localStart);
    }

    internal static Brush? CreateSelectionBrush(Brush? brush, double opacity)
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
        _pageView.RefreshPage();
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
            viewer.SetDocumentPaginatorSource(e.NewValue as FlowDocument);
        }
    }

    private void SetDocumentPaginatorSource(IDocumentPaginatorSource? source) => base.Document = source;
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

    private static object? CoerceZoom(DependencyObject d, object? value) => d is FlowDocumentPageViewer viewer && value is double zoom ? FlowDocumentViewerSupport.CoerceZoom(zoom, viewer.MinZoom, viewer.MaxZoom) : value;
    private static object? CoerceMaxZoom(DependencyObject d, object? value) => d is FlowDocumentPageViewer viewer && value is double maxZoom ? Math.Max(viewer.MinZoom, maxZoom) : value;
    private static void OnSelectionVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.ApplySelectionToCurrentPage();
            viewer.InvalidateVisual();
        }
    }
}
