using Jalium.UI.Documents;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Displays a flow document in page, two-page, or continuous-scroll mode.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Document")]
[TemplatePart(Name = "PART_ViewerPresenter", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_FindTextBox", Type = typeof(TextBox))]
public class FlowDocumentReader : Control
{
    private readonly FlowDocumentPageViewer _pageViewer;
    private readonly DocumentViewer _twoPageViewer;
    private readonly FlowDocumentScrollViewer _scrollViewer;
    private readonly FlowDocumentSearchSession _search = new();
    private FrameworkElement _activeViewer;
    private ContentPresenter? _viewerPresenter;
    private TextBox? _findTextBox;
    private FlowDocument? _attachedDocument;
    private bool _switchingViewingMode;
    private bool _isPrinting;
    static FlowDocumentReader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlowDocumentReader), new PropertyMetadata(typeof(FlowDocumentReader)));
    }

    public FlowDocumentReader()
    {
        _pageViewer = new FlowDocumentPageViewer();
        _twoPageViewer = new DocumentViewer
        {
            PageDisplay = DocumentViewerPageDisplay.TwoPages
        };
        _scrollViewer = new FlowDocumentScrollViewer();
        _activeViewer = _pageViewer;
        AddLogicalChild(_pageViewer);
        AddLogicalChild(_twoPageViewer);
        AddLogicalChild(_scrollViewer);
        SetValue(ActiveViewerPropertyKey, _activeViewer);
        RegisterCommands();
        ApplyZoomToViewers();
        UpdateReadOnlyState();
    }

#region Commands
    public static readonly RoutedUICommand SwitchViewingModeCommand = new("Switch Viewing Mode", nameof(SwitchViewingModeCommand), typeof(FlowDocumentReader));
#endregion
#region Dependency properties
    private static readonly DependencyPropertyKey ActiveViewerPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ActiveViewer), typeof(FrameworkElement), typeof(FlowDocumentReader), new PropertyMetadata(null));
    public static readonly DependencyProperty ActiveViewerProperty = ActiveViewerPropertyKey.DependencyProperty;
    /// <summary>Gets the retained presentation model for the selected viewing mode.</summary>
    public FrameworkElement ActiveViewer => (FrameworkElement)GetValue(ActiveViewerProperty)!;

    private static readonly DependencyPropertyKey IsPageModePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsPageMode), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true));
    public static readonly DependencyProperty IsPageModeProperty = IsPageModePropertyKey.DependencyProperty;
    public bool IsPageMode => (bool)GetValue(IsPageModeProperty)!;

    private static readonly DependencyPropertyKey IsTwoPageModePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsTwoPageMode), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty IsTwoPageModeProperty = IsTwoPageModePropertyKey.DependencyProperty;
    public bool IsTwoPageMode => (bool)GetValue(IsTwoPageModeProperty)!;

    private static readonly DependencyPropertyKey IsScrollModePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsScrollMode), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty IsScrollModeProperty = IsScrollModePropertyKey.DependencyProperty;
    public bool IsScrollMode => (bool)GetValue(IsScrollModeProperty)!;

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(FlowDocumentReader), new PropertyMetadata(string.Empty, OnSearchTextChanged));
    private static readonly DependencyPropertyKey IsFindToolBarVisiblePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFindToolBarVisible), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty IsFindToolBarVisibleProperty = IsFindToolBarVisiblePropertyKey.DependencyProperty;
    public static readonly RoutedUICommand FindNextCommand = FlowDocumentScrollViewer.FindNextCommand;
    public static readonly RoutedUICommand CloseFindCommand = FlowDocumentScrollViewer.CloseFindCommand;
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentReader), new PropertyMetadata(null, OnDocumentChanged));
    public static readonly DependencyProperty ViewingModeProperty = DependencyProperty.Register(nameof(ViewingMode), typeof(FlowDocumentReaderViewingMode), typeof(FlowDocumentReader), new PropertyMetadata(FlowDocumentReaderViewingMode.Page, OnViewingModeChanged), static value => value is FlowDocumentReaderViewingMode mode && Enum.IsDefined(mode));
    public static readonly DependencyProperty ZoomProperty = FlowDocumentPageViewer.ZoomProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));
    public static readonly DependencyProperty MinZoomProperty = FlowDocumentPageViewer.MinZoomProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(80.0, OnMinZoomChanged));
    public static readonly DependencyProperty MaxZoomProperty = FlowDocumentPageViewer.MaxZoomProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(200.0, OnMaxZoomChanged, CoerceMaxZoom));
    public static readonly DependencyProperty ZoomIncrementProperty = FlowDocumentPageViewer.ZoomIncrementProperty.AddOwner(typeof(FlowDocumentReader));
    public static readonly DependencyProperty IsPageViewEnabledProperty = DependencyProperty.Register(nameof(IsPageViewEnabled), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true, OnViewingModeAvailabilityChanged));
    public static readonly DependencyProperty IsTwoPageViewEnabledProperty = DependencyProperty.Register(nameof(IsTwoPageViewEnabled), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true, OnViewingModeAvailabilityChanged));
    public static readonly DependencyProperty IsScrollViewEnabledProperty = DependencyProperty.Register(nameof(IsScrollViewEnabled), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true, OnViewingModeAvailabilityChanged));
    public static readonly DependencyProperty IsFindEnabledProperty = DependencyProperty.Register(nameof(IsFindEnabled), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true, OnCommandAvailabilityChanged));
    public static readonly DependencyProperty IsPrintEnabledProperty = DependencyProperty.Register(nameof(IsPrintEnabled), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true, OnCommandAvailabilityChanged));
    private static readonly DependencyPropertyKey PageCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PageCount), typeof(int), typeof(FlowDocumentReader), new PropertyMetadata(0));
    public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PageNumberPropertyKey = DependencyProperty.RegisterReadOnly(nameof(PageNumber), typeof(int), typeof(FlowDocumentReader), new PropertyMetadata(0));
    public static readonly DependencyProperty PageNumberProperty = PageNumberPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanGoToNextPagePropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanGoToNextPage), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty CanGoToNextPageProperty = CanGoToNextPagePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanGoToPreviousPagePropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanGoToPreviousPage), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty CanGoToPreviousPageProperty = CanGoToPreviousPagePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanIncreaseZoom), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true));
    public static readonly DependencyProperty CanIncreaseZoomProperty = CanIncreaseZoomPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanDecreaseZoom), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(true));
    public static readonly DependencyProperty CanDecreaseZoomProperty = CanDecreaseZoomPropertyKey.DependencyProperty;
    public static readonly DependencyProperty SelectionBrushProperty = TextBoxBase.SelectionBrushProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(SystemColors.HighlightBrush, OnSelectionVisualChanged));
    public static readonly DependencyProperty SelectionOpacityProperty = TextBoxBase.SelectionOpacityProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(1.0, OnSelectionVisualChanged));
    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSelectionActive), typeof(bool), typeof(FlowDocumentReader), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectionActiveProperty = IsSelectionActivePropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty = TextBoxBase.IsInactiveSelectionHighlightEnabledProperty.AddOwner(typeof(FlowDocumentReader), new PropertyMetadata(false, OnSelectionVisualChanged));
#endregion
#region Properties
    public FlowDocument? Document { get => (FlowDocument? )GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }

    public FlowDocumentReaderViewingMode ViewingMode
    {
        get => (FlowDocumentReaderViewingMode)GetValue(ViewingModeProperty)!;
        set
        {
            if (!CanSwitchToViewingMode(value))
            {
                throw new ArgumentException($"Viewing mode '{value}' is disabled.", nameof(value));
            }

            SetValue(ViewingModeProperty, value);
        }
    }

    public double Zoom { get => (double)GetValue(ZoomProperty)!; set => SetValue(ZoomProperty, value); }
    public double MinZoom { get => (double)GetValue(MinZoomProperty)!; set => SetValue(MinZoomProperty, value); }
    public double MaxZoom { get => (double)GetValue(MaxZoomProperty)!; set => SetValue(MaxZoomProperty, value); }
    public double ZoomIncrement { get => (double)GetValue(ZoomIncrementProperty)!; set => SetValue(ZoomIncrementProperty, value); }
    public bool IsPageViewEnabled { get => (bool)GetValue(IsPageViewEnabledProperty)!; set => SetValue(IsPageViewEnabledProperty, value); }
    public bool IsTwoPageViewEnabled { get => (bool)GetValue(IsTwoPageViewEnabledProperty)!; set => SetValue(IsTwoPageViewEnabledProperty, value); }
    public bool IsScrollViewEnabled { get => (bool)GetValue(IsScrollViewEnabledProperty)!; set => SetValue(IsScrollViewEnabledProperty, value); }
    public bool IsFindEnabled { get => (bool)GetValue(IsFindEnabledProperty)!; set => SetValue(IsFindEnabledProperty, value); }
    public bool IsPrintEnabled { get => (bool)GetValue(IsPrintEnabledProperty)!; set => SetValue(IsPrintEnabledProperty, value); }
    public int PageCount => (int)GetValue(PageCountProperty)!;
    public int PageNumber => (int)GetValue(PageNumberProperty)!;
    public bool CanGoToNextPage => (bool)GetValue(CanGoToNextPageProperty)!;
    public bool CanGoToPreviousPage => (bool)GetValue(CanGoToPreviousPageProperty)!;
    public bool CanIncreaseZoom => (bool)GetValue(CanIncreaseZoomProperty)!;
    public bool CanDecreaseZoom => (bool)GetValue(CanDecreaseZoomProperty)!;
    public Brush? SelectionBrush { get => (Brush? )GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public double SelectionOpacity { get => (double)GetValue(SelectionOpacityProperty)!; set => SetValue(SelectionOpacityProperty, value); }
    public bool IsSelectionActive => (bool)GetValue(IsSelectionActiveProperty)!;
    public bool IsInactiveSelectionHighlightEnabled { get => (bool)GetValue(IsInactiveSelectionHighlightEnabledProperty)!; set => SetValue(IsInactiveSelectionHighlightEnabledProperty, value); }
    public TextSelection? Selection => _search.Selection;
    public string SearchText { get => (string? )GetValue(SearchTextProperty) ?? string.Empty; set => SetValue(SearchTextProperty, value ?? string.Empty); }
    public bool IsFindToolBarVisible => (bool)GetValue(IsFindToolBarVisibleProperty)!;

#endregion
#region Public operations
    public bool CanGoToPage(int pageNumber) => pageNumber >= 1 && pageNumber <= PageCount;
    public void FirstPage() => GoToPageCore(PageCount > 0 ? 1 : 0);
    public void LastPage() => GoToPageCore(PageCount);
    public void NextPage()
    {
        var step = ViewingMode == FlowDocumentReaderViewingMode.TwoPage ? 2 : 1;
        GoToPageCore(Math.Min(PageCount, PageNumber + step));
    }

    public void PreviousPage()
    {
        var step = ViewingMode == FlowDocumentReaderViewingMode.TwoPage ? 2 : 1;
        GoToPageCore(Math.Max(1, PageNumber - step));
    }

    public void IncreaseZoom() => OnIncreaseZoomCommand();
    public void DecreaseZoom() => OnDecreaseZoomCommand();
    public void Find() => OnFindCommand();
    public bool Find(string searchText)
    {
        SetCurrentValue(SearchTextProperty, searchText ?? throw new ArgumentNullException(nameof(searchText)));
        if (!IsFindEnabled || !_search.Find(SearchText))
        {
            return false;
        }

        NavigateToSelection();
        return true;
    }

    public void Print() => OnPrintCommand();
    public void CancelPrint() => OnCancelPrintCommand();
    public void SwitchViewingMode(FlowDocumentReaderViewingMode viewingMode) => OnSwitchViewingModeCommand(viewingMode);
#endregion
#region Protected command hooks
    protected virtual void OnFindCommand()
    {
        if (!IsFindEnabled || Document == null)
        {
            return;
        }

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

        if (!string.IsNullOrWhiteSpace(SearchText) && _search.Find(SearchText))
        {
            NavigateToSelection();
        }
    }

    protected virtual void OnPrintCommand()
    {
        if (!IsPrintEnabled || Document == null)
        {
            return;
        }

        if (_isPrinting)
        {
            return;
        }

        _isPrinting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            FlowDocumentViewerSupport.Print(Document.ViewerPaginator, Math.Max(1, PageNumber));
        }
        finally
        {
            _isPrinting = false;
            OnPrintCompleted();
        }
    }

    protected virtual void OnCancelPrintCommand()
    {
        FlowDocumentViewerSupport.CancelPrint();
        _isPrinting = false;
        CommandManager.InvalidateRequerySuggested();
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

    protected virtual void OnSwitchViewingModeCommand(FlowDocumentReaderViewingMode viewingMode)
    {
        if (!CanSwitchToViewingMode(viewingMode))
        {
            throw new ArgumentException($"Viewing mode '{viewingMode}' is disabled.", nameof(viewingMode));
        }

        SetCurrentValue(ViewingModeProperty, viewingMode);
    }

    protected virtual void SwitchViewingModeCore(FlowDocumentReaderViewingMode viewingMode)
    {
        if (!CanSwitchToViewingMode(viewingMode))
        {
            throw new ArgumentException($"Viewing mode '{viewingMode}' is disabled.", nameof(viewingMode));
        }

        var nextViewer = GetViewer(viewingMode);
        if (ReferenceEquals(nextViewer, _activeViewer))
        {
            return;
        }

        var pageNumber = PageNumber;
        _activeViewer = nextViewer;
        SetValue(ActiveViewerPropertyKey, _activeViewer);
        GoToPageCore(pageNumber);
        InvalidateMeasure();
    }

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        SetValue(IsSelectionActivePropertyKey, isFocusWithin);
        ApplySelectionVisuals();
    }

#endregion
#region Layout
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _viewerPresenter = GetTemplateChild("PART_ViewerPresenter") as ContentPresenter;
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
        if (_viewerPresenter != null && ReferenceEquals(_viewerPresenter.Content, _activeViewer))
            _viewerPresenter.Content = null;
        _viewerPresenter = null;
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
        VisualStateManager.GoToState(this, ViewingMode.ToString(), false);
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
        var desired = base.MeasureOverride(availableSize);
        ApplyTwoPageSelection();
        return desired;
    }

#endregion
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.FlowDocumentReaderAutomationPeer(this);
    private void RegisterCommands()
    {
        AddCommandBinding(ApplicationCommands.Find, static reader => reader.OnFindCommand(), static reader => reader.IsFindEnabled && reader.Document != null);
        AddCommandBinding(FindNextCommand, static reader => reader.Find(reader.SearchText), static reader => reader.IsFindEnabled && reader.Document != null && !string.IsNullOrWhiteSpace(reader.SearchText));
        AddCommandBinding(CloseFindCommand, static reader => reader.CloseFind(), static reader => reader.IsFindToolBarVisible);
        AddCommandBinding(ApplicationCommands.Print, static reader => reader.OnPrintCommand(), static reader => reader.IsPrintEnabled && reader.Document != null && !reader._isPrinting);
        AddCommandBinding(ApplicationCommands.CancelPrint, static reader => reader.OnCancelPrintCommand(), static reader => reader._isPrinting);
        AddCommandBinding(NavigationCommands.FirstPage, static reader => reader.FirstPage(), static reader => reader.PageCount > 0);
        AddCommandBinding(NavigationCommands.LastPage, static reader => reader.LastPage(), static reader => reader.PageCount > 0);
        AddCommandBinding(NavigationCommands.NextPage, static reader => reader.NextPage(), static reader => reader.CanGoToNextPage);
        AddCommandBinding(NavigationCommands.PreviousPage, static reader => reader.PreviousPage(), static reader => reader.CanGoToPreviousPage);
        AddCommandBinding(NavigationCommands.IncreaseZoom, static reader => reader.OnIncreaseZoomCommand(), static reader => reader.CanIncreaseZoom);
        AddCommandBinding(NavigationCommands.DecreaseZoom, static reader => reader.OnDecreaseZoomCommand(), static reader => reader.CanDecreaseZoom);
        AddCommandBinding(ApplicationCommands.SelectAll, static reader => reader.SelectAll(), static reader => reader.Document != null);
        CommandBindings.Add(new CommandBinding(NavigationCommands.GoToPage, (_, args) =>
        {
            if (TryGetPageNumber(args.Parameter, out var pageNumber))
            {
                GoToPageCore(pageNumber);
            }
        }, (_, args) => args.CanExecute = TryGetPageNumber(args.Parameter, out var pageNumber) && CanGoToPage(pageNumber)));
        CommandBindings.Add(new CommandBinding(SwitchViewingModeCommand, (_, args) =>
        {
            if (TryGetViewingMode(args.Parameter, out var viewingMode))
            {
                OnSwitchViewingModeCommand(viewingMode);
            }
        }, (_, args) => args.CanExecute = TryGetViewingMode(args.Parameter, out var viewingMode) && CanSwitchToViewingMode(viewingMode)));
    }

    private void AddCommandBinding(RoutedUICommand command, Action<FlowDocumentReader> execute, Predicate<FlowDocumentReader> canExecute)
    {
        CommandBindings.Add(new CommandBinding(command, (_, _) => execute(this), (_, args) => args.CanExecute = canExecute(this)));
    }

    private FrameworkElement GetViewer(FlowDocumentReaderViewingMode mode) => mode switch
    {
        FlowDocumentReaderViewingMode.Page => _pageViewer,
        FlowDocumentReaderViewingMode.TwoPage => _twoPageViewer,
        FlowDocumentReaderViewingMode.Scroll => _scrollViewer,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
    private bool CanSwitchToViewingMode(FlowDocumentReaderViewingMode mode) => mode switch
    {
        FlowDocumentReaderViewingMode.Page => IsPageViewEnabled,
        FlowDocumentReaderViewingMode.TwoPage => IsTwoPageViewEnabled,
        FlowDocumentReaderViewingMode.Scroll => IsScrollViewEnabled,
        _ => false,
    };
    private void GoToPageCore(int pageNumber)
    {
        if (PageCount == 0)
        {
            SetValue(PageNumberPropertyKey, 0);
            UpdateReadOnlyState();
            return;
        }

        var normalized = Math.Clamp(pageNumber, 1, PageCount);
        _pageViewer.GoToPage(normalized);
        _twoPageViewer.GoToPage(normalized);
        _scrollViewer.GoToPageInternal(normalized);
        SetValue(PageNumberPropertyKey, normalized);
        UpdateReadOnlyState();
    }

    private void ApplyDocument(FlowDocument? document)
    {
        if (_attachedDocument != null)
        {
            _attachedDocument.ViewerPaginationChanged -= OnPaginationChanged;
        }

        _attachedDocument = document;
        if (_attachedDocument != null)
        {
            _attachedDocument.ViewerPaginationChanged += OnPaginationChanged;
        }

        _search.Attach(document);
        if (document == null)
            SetValue(IsFindToolBarVisiblePropertyKey, false);
        UpdateVisualState();
        _pageViewer.Document = document;
        _twoPageViewer.Document = document;
        _scrollViewer.Document = document;
        SetValue(PageCountPropertyKey, FlowDocumentViewerSupport.GetPageCount(document));
        GoToPageCore(PageCount > 0 ? 1 : 0);
        ApplySelectionVisuals();
        InvalidateMeasure();
    }

    private void NavigateToSelection()
    {
        if (Document == null || Selection == null)
        {
            return;
        }

        GoToPageCore(FlowDocumentViewerSupport.GetPageNumberForOffset(Document, Selection.Start.DocumentOffset));
        _pageViewer.ApplySelection(Selection);
        _scrollViewer.ApplySelection(Selection);
        ApplyTwoPageSelection();
        InvalidateVisual();
    }

    private void SelectAll()
    {
        _search.SelectAll();
        _pageViewer.ApplySelection(Selection);
        _scrollViewer.ApplySelection(Selection);
        ApplyTwoPageSelection();
        InvalidateVisual();
    }

    private void ApplyZoomToViewers()
    {
        _pageViewer.MinZoom = MinZoom;
        _pageViewer.MaxZoom = MaxZoom;
        _pageViewer.ZoomIncrement = ZoomIncrement;
        _pageViewer.Zoom = Zoom;
        _twoPageViewer.MinZoom = MinZoom;
        _twoPageViewer.MaxZoom = MaxZoom;
        _twoPageViewer.ZoomIncrement = ZoomIncrement;
        _twoPageViewer.Zoom = Zoom;
        _scrollViewer.MinZoom = MinZoom;
        _scrollViewer.MaxZoom = MaxZoom;
        _scrollViewer.ZoomIncrement = ZoomIncrement;
        _scrollViewer.Zoom = Zoom;
    }

    private void ApplySelectionVisuals()
    {
        _pageViewer.SelectionBrush = SelectionBrush;
        _pageViewer.SelectionOpacity = SelectionOpacity;
        _pageViewer.IsInactiveSelectionHighlightEnabled = IsSelectionActive || IsInactiveSelectionHighlightEnabled;
        _scrollViewer.SelectionBrush = SelectionBrush;
        _scrollViewer.SelectionOpacity = SelectionOpacity;
        _scrollViewer.IsInactiveSelectionHighlightEnabled = IsSelectionActive || IsInactiveSelectionHighlightEnabled;
        ApplyTwoPageSelection();
    }

    private void ApplyTwoPageSelection()
    {
        if (ViewingMode != FlowDocumentReaderViewingMode.TwoPage)
            return;
        var brush = FlowDocumentPageViewer.CreateSelectionBrush(SelectionBrush, SelectionOpacity);
        foreach (var page in _twoPageViewer.PageViews)
        {
            if (page.DocumentPage?.Visual is not FlowDocumentPresentation view)
                continue;
            view.SelectionBrush = brush;
            view.SetSelection(Selection, IsSelectionActive || IsInactiveSelectionHighlightEnabled);
        }
    }

    private void UpdateReadOnlyState()
    {
        SetValue(IsPageModePropertyKey, ViewingMode == FlowDocumentReaderViewingMode.Page);
        SetValue(IsTwoPageModePropertyKey, ViewingMode == FlowDocumentReaderViewingMode.TwoPage);
        SetValue(IsScrollModePropertyKey, ViewingMode == FlowDocumentReaderViewingMode.Scroll);
        UpdateVisualState();
        SetValue(CanGoToPreviousPagePropertyKey, PageNumber > 1);
        SetValue(CanGoToNextPagePropertyKey, PageNumber > 0 && PageNumber < PageCount);
        SetValue(CanIncreaseZoomPropertyKey, Zoom < MaxZoom);
        SetValue(CanDecreaseZoomPropertyKey, Zoom > MinZoom);
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPaginationChanged(object? sender, EventArgs e)
    {
        var pageNumber = PageNumber;
        SetValue(PageCountPropertyKey, FlowDocumentViewerSupport.GetPageCount(Document));
        GoToPageCore(PageCount > 0 ? Math.Clamp(pageNumber, 1, PageCount) : 0);
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => CommandManager.InvalidateRequerySuggested();
    private static void OnCommandAvailabilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var reader = (FlowDocumentReader)d;
        if (!reader.IsFindEnabled && reader.IsFindToolBarVisible)
            reader.CloseFind();
        CommandManager.InvalidateRequerySuggested();
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.ApplyDocument((FlowDocument? )e.NewValue);
        }
    }

    private static void OnViewingModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentReader reader || reader._switchingViewingMode)
        {
            return;
        }

        reader._switchingViewingMode = true;
        try
        {
            reader.SwitchViewingModeCore((FlowDocumentReaderViewingMode)e.NewValue!);
            reader.UpdateReadOnlyState();
        }
        finally
        {
            reader._switchingViewingMode = false;
        }
    }

    private static void OnViewingModeAvailabilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentReader reader)
            return;
        if (reader.CanSwitchToViewingMode(reader.ViewingMode))
        {
            reader.UpdateReadOnlyState();
            return;
        }

        var fallback = Enum.GetValues<FlowDocumentReaderViewingMode>().FirstOrDefault(reader.CanSwitchToViewingMode);
        if (!reader.CanSwitchToViewingMode(fallback))
        {
            throw new InvalidOperationException("At least one FlowDocumentReader viewing mode must remain enabled.");
        }

        reader.SetCurrentValue(ViewingModeProperty, fallback);
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.ApplyZoomToViewers();
            reader.UpdateReadOnlyState();
        }
    }

    private static void OnMinZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.CoerceValue(MaxZoomProperty);
            reader.CoerceValue(ZoomProperty);
            reader.ApplyZoomToViewers();
            reader.UpdateReadOnlyState();
        }
    }

    private static void OnMaxZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.CoerceValue(ZoomProperty);
            reader.ApplyZoomToViewers();
            reader.UpdateReadOnlyState();
        }
    }

    private static object? CoerceZoom(DependencyObject d, object? value) => d is FlowDocumentReader reader && value is double zoom ? FlowDocumentViewerSupport.CoerceZoom(zoom, reader.MinZoom, reader.MaxZoom) : value;
    private static object? CoerceMaxZoom(DependencyObject d, object? value) => d is FlowDocumentReader reader && value is double maxZoom ? Math.Max(reader.MinZoom, maxZoom) : value;
    private static void OnSelectionVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.ApplySelectionVisuals();
            reader.InvalidateVisual();
        }
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

    private static bool TryGetViewingMode(object? parameter, out FlowDocumentReaderViewingMode mode)
    {
        if (parameter is FlowDocumentReaderViewingMode value)
        {
            mode = value;
            return true;
        }

        return Enum.TryParse(parameter?.ToString(), true, out mode) && Enum.IsDefined(mode);
    }
}

/// <summary>
/// Specifies the presentation mode used by <see cref = "FlowDocumentReader"/>.
/// </summary>
public enum FlowDocumentReaderViewingMode
{
    Page,
    TwoPage,
    Scroll,
}
