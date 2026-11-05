using Jalium.UI.Documents;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>
/// Displays a <see cref = "FlowDocument"/> as a continuous scrolling text surface.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Document")]
[TemplatePart(Name = "PART_ContentHost", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_FindTextBox", Type = typeof(TextBox))]
public class FlowDocumentScrollViewer : Control
{
    private ScrollViewer? _contentHost;
    private TextBox? _findTextBox;
    private Point _retainedOffset;
    private bool _restoreScroll;
    private readonly FlowDocumentPresentation _textView;
    private readonly FlowDocumentSearchSession _search = new();
    private FlowDocument? _attachedDocument;
    private bool _isPrinting;
    private int _pageNumber;
    static FlowDocumentScrollViewer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FlowDocumentScrollViewer), new PropertyMetadata(typeof(FlowDocumentScrollViewer)));
    }

    public FlowDocumentScrollViewer()
    {
        // This is the semantic document renderer, retained across frame templates so
        // selection and document state do not belong to a discarded scroll host.
        _textView = new FlowDocumentPresentation();
        AddLogicalChild(_textView);
        SetValue(DocumentViewPropertyKey, _textView);
        RegisterCommands();
        ApplyDocumentVisual();
        UpdateZoomState();
    }

#region Dependency properties
    private static readonly DependencyPropertyKey DocumentViewPropertyKey = DependencyProperty.RegisterReadOnly(nameof(DocumentView), typeof(UIElement), typeof(FlowDocumentScrollViewer), new PropertyMetadata(null));
    public static readonly DependencyProperty DocumentViewProperty = DocumentViewPropertyKey.DependencyProperty;
    /// <summary>Gets the retained document content used by the default template.</summary>
    public UIElement DocumentView => (UIElement)GetValue(DocumentViewProperty)!;

    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(FlowDocumentScrollViewer), new PropertyMetadata(string.Empty, OnSearchTextChanged));
    private static readonly DependencyPropertyKey IsFindToolBarVisiblePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsFindToolBarVisible), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsFindToolBarVisibleProperty = IsFindToolBarVisiblePropertyKey.DependencyProperty;
    public static readonly RoutedUICommand FindNextCommand = new("Find next", nameof(FindNextCommand), typeof(FlowDocumentScrollViewer));
    public static readonly RoutedUICommand CloseFindCommand = new("Close find", nameof(CloseFindCommand), typeof(FlowDocumentScrollViewer));
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentScrollViewer), new PropertyMetadata(null, OnDocumentChanged));
    public static readonly DependencyProperty ZoomProperty = FlowDocumentPageViewer.ZoomProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));
    public static readonly DependencyProperty MinZoomProperty = FlowDocumentPageViewer.MinZoomProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(80.0, OnMinZoomChanged));
    public static readonly DependencyProperty MaxZoomProperty = FlowDocumentPageViewer.MaxZoomProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(200.0, OnMaxZoomChanged, CoerceMaxZoom));
    public static readonly DependencyProperty ZoomIncrementProperty = FlowDocumentPageViewer.ZoomIncrementProperty.AddOwner(typeof(FlowDocumentScrollViewer));
    public static readonly DependencyProperty IsToolBarVisibleProperty = DependencyProperty.Register(nameof(IsToolBarVisible), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectionEnabledProperty = DependencyProperty.Register(nameof(IsSelectionEnabled), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(true, OnIsSelectionEnabledChanged));
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer), new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged));
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer), new PropertyMetadata(ScrollBarVisibility.Visible, OnScrollBarVisibilityChanged));
    private static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanIncreaseZoom), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanIncreaseZoomProperty = CanIncreaseZoomPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey = DependencyProperty.RegisterReadOnly(nameof(CanDecreaseZoom), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(true));
    public static readonly DependencyProperty CanDecreaseZoomProperty = CanDecreaseZoomPropertyKey.DependencyProperty;
    public static readonly DependencyProperty SelectionBrushProperty = TextBoxBase.SelectionBrushProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(SystemColors.HighlightBrush, OnSelectionVisualChanged));
    public static readonly DependencyProperty SelectionOpacityProperty = TextBoxBase.SelectionOpacityProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(1.0, OnSelectionVisualChanged));
    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsSelectionActive), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty IsSelectionActiveProperty = IsSelectionActivePropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty = TextBoxBase.IsInactiveSelectionHighlightEnabledProperty.AddOwner(typeof(FlowDocumentScrollViewer), new PropertyMetadata(false, OnSelectionVisualChanged));
#endregion
#region Properties
    public FlowDocument? Document { get => (FlowDocument? )GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public double Zoom { get => (double)GetValue(ZoomProperty)!; set => SetValue(ZoomProperty, value); }
    public double MinZoom { get => (double)GetValue(MinZoomProperty)!; set => SetValue(MinZoomProperty, value); }
    public double MaxZoom { get => (double)GetValue(MaxZoomProperty)!; set => SetValue(MaxZoomProperty, value); }
    public double ZoomIncrement { get => (double)GetValue(ZoomIncrementProperty)!; set => SetValue(ZoomIncrementProperty, value); }
    public bool IsToolBarVisible { get => (bool)GetValue(IsToolBarVisibleProperty)!; set => SetValue(IsToolBarVisibleProperty, value); }
    public bool IsSelectionEnabled { get => (bool)GetValue(IsSelectionEnabledProperty)!; set => SetValue(IsSelectionEnabledProperty, value); }
    public ScrollBarVisibility HorizontalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!; set => SetValue(HorizontalScrollBarVisibilityProperty, value); }
    public ScrollBarVisibility VerticalScrollBarVisibility { get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!; set => SetValue(VerticalScrollBarVisibilityProperty, value); }
    public bool CanIncreaseZoom => (bool)GetValue(CanIncreaseZoomProperty)!;
    public bool CanDecreaseZoom => (bool)GetValue(CanDecreaseZoomProperty)!;
    public Brush? SelectionBrush { get => (Brush? )GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public double SelectionOpacity { get => (double)GetValue(SelectionOpacityProperty)!; set => SetValue(SelectionOpacityProperty, value); }
    public bool IsSelectionActive => (bool)GetValue(IsSelectionActiveProperty)!;
    public bool IsInactiveSelectionHighlightEnabled { get => (bool)GetValue(IsInactiveSelectionHighlightEnabledProperty)!; set => SetValue(IsInactiveSelectionHighlightEnabledProperty, value); }
    public TextSelection? Selection => _search.Selection;
    public string SearchText { get => (string? )GetValue(SearchTextProperty) ?? string.Empty; set => SetValue(SearchTextProperty, value ?? string.Empty); }
    public bool IsFindToolBarVisible => (bool)GetValue(IsFindToolBarVisibleProperty)!;
    internal int PageCountInternal => FlowDocumentViewerSupport.GetPageCount(Document);
    internal int PageNumberInternal => _pageNumber;

#endregion
#region Public operations
    public void IncreaseZoom() => OnIncreaseZoomCommand();
    public void DecreaseZoom() => OnDecreaseZoomCommand();
    public void Find() => OnFindCommand();
    public bool Find(string searchText)
    {
        SetCurrentValue(SearchTextProperty, searchText ?? throw new ArgumentNullException(nameof(searchText)));
        if (!_search.Find(SearchText))
        {
            return false;
        }

        ApplySelection(_search.Selection);
        return true;
    }

    public void Print() => OnPrintCommand();
    public void CancelPrint() => OnCancelPrintCommand();
#endregion
#region Protected command hooks
    protected virtual void OnFindCommand()
    {
        if (Document == null)
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
            ApplySelection(_search.Selection);
        }
    }

    protected virtual void OnPrintCommand()
    {
        if (Document == null || _isPrinting)
        {
            return;
        }

        _isPrinting = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            FlowDocumentViewerSupport.Print(Document.ViewerPaginator, Math.Max(1, _pageNumber));
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

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        SetValue(IsSelectionActivePropertyKey, isFocusWithin);
        ApplySelectionVisual();
    }

#endregion
#region Layout
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _contentHost = GetTemplateChild("PART_ContentHost") as ScrollViewer;
        _findTextBox = GetTemplateChild("PART_FindTextBox") as TextBox;
        if (_findTextBox != null)
            _findTextBox.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFindPreviewKeyDown));
        if (_contentHost != null)
        {
            _contentHost.ScrollChanged += OnContentScrollChanged;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(_contentHost); index++)
            {
                if (VisualTreeHelper.GetChild(_contentHost, index)is not ScrollBar bar)
                    continue;
                bar.Focusable = false;
                KeyboardNavigation.SetIsTabStop(bar, false);
                KeyboardNavigation.SetTabNavigation(bar, KeyboardNavigationMode.None);
            }
        }

        UpdateVisualState();
    }

    internal override void OnTemplateContentClearing()
    {
        if (_findTextBox != null)
            _findTextBox.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnFindPreviewKeyDown));
        _findTextBox = null;
        if (_contentHost != null)
        {
            _retainedOffset = new Point(_contentHost.HorizontalOffset, _contentHost.VerticalOffset);
            _restoreScroll = true;
            _contentHost.ScrollChanged -= OnContentScrollChanged;
            if (ReferenceEquals(_contentHost.Content, _textView))
                _contentHost.Content = null;
        }

        _contentHost = null;
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
        _textView.Focus();
        CommandManager.InvalidateRequerySuggested();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _textView.SetViewportWidth(availableSize.Width / (Zoom / 100.0));
        _textView.SetViewportHeight(availableSize.Height / (Zoom / 100.0));
        var desired = base.MeasureOverride(availableSize);
        if (_contentHost == null)
            return desired;
        var widthChanged = _textView.SetViewportWidth(_contentHost.ViewportWidth / (Zoom / 100.0));
        var heightChanged = _textView.SetViewportHeight(double.IsFinite(availableSize.Height) ? _contentHost.ViewportHeight / (Zoom / 100.0) : double.NaN);
        if (widthChanged || heightChanged)
        {
            _contentHost.InvalidateMeasure();
            desired = base.MeasureOverride(availableSize);
        }

        if (_restoreScroll)
        {
            _restoreScroll = false;
            _contentHost?.ScrollToHorizontalOffset(_retainedOffset.X);
            _contentHost?.ScrollToVerticalOffset(_retainedOffset.Y);
        }

        return desired;
    }

#endregion
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.FlowDocumentScrollViewerAutomationPeer(this);
    internal void GoToPageInternal(int pageNumber)
    {
        if (Document == null || PageCountInternal == 0)
        {
            _pageNumber = 0;
            _contentHost?.ScrollToTop();
            return;
        }

        var targetPage = Math.Clamp(pageNumber, 1, PageCountInternal);
        _pageNumber = targetPage;
        EnsureTextLayout();
        targetPage = Math.Clamp(targetPage, 1, Math.Max(1, PageCountInternal));
        var offset = FlowDocumentViewerSupport.GetPageStartOffset(Document, targetPage);
        _contentHost?.ScrollToVerticalOffset(Math.Max(0, _textView.TopForOffset(offset) * (Zoom / 100.0)));
        // The continuous row can begin before this paginated page's first
        // source offset. Its synchronous ScrollChanged notification must not
        // replace an explicit navigation target with that earlier page.
        _pageNumber = targetPage;
    }

    internal void ApplySelection(TextSelection? selection)
    {
        if (!IsSelectionEnabled || (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled) || Document == null || selection == null || !ReferenceEquals(selection.Start.Document, Document))
        {
            _textView.SetSelection(selection, highlight: false);
            return;
        }

        EnsureTextLayout();
        var start = selection.Start.DocumentOffset;
        _textView.SetSelection(selection);
        _pageNumber = FlowDocumentViewerSupport.GetPageNumberForOffset(Document, start);
        _contentHost?.ScrollToVerticalOffset(_textView.TopForOffset(start) * (Zoom / 100.0));
    }

    private void EnsureTextLayout()
    {
        if (Document is null)
            return;
        var width = _textView.RenderSize.Width > 0 ? _textView.RenderSize.Width : RenderSize.Width > 0 ? RenderSize.Width / (Zoom / 100.0) : 816;
        _textView.EnsureViewerLayout(width);
    }

    private void RegisterCommands()
    {
        AddCommandBinding(ApplicationCommands.Find, static viewer => viewer.OnFindCommand(), static viewer => viewer.Document != null);
        AddCommandBinding(FindNextCommand, static viewer => viewer.Find(viewer.SearchText), static viewer => viewer.Document != null && !string.IsNullOrWhiteSpace(viewer.SearchText));
        AddCommandBinding(CloseFindCommand, static viewer => viewer.CloseFind(), static viewer => viewer.IsFindToolBarVisible);
        AddCommandBinding(ApplicationCommands.Print, static viewer => viewer.OnPrintCommand(), static viewer => viewer.Document != null && !viewer._isPrinting);
        AddCommandBinding(ApplicationCommands.CancelPrint, static viewer => viewer.OnCancelPrintCommand(), static viewer => viewer._isPrinting);
        AddCommandBinding(NavigationCommands.IncreaseZoom, static viewer => viewer.OnIncreaseZoomCommand(), static viewer => viewer.CanIncreaseZoom);
        AddCommandBinding(NavigationCommands.DecreaseZoom, static viewer => viewer.OnDecreaseZoomCommand(), static viewer => viewer.CanDecreaseZoom);
        AddCommandBinding(NavigationCommands.FirstPage, static viewer => viewer.GoToPageInternal(1), static viewer => viewer.PageCountInternal > 0);
        AddCommandBinding(NavigationCommands.LastPage, static viewer => viewer.GoToPageInternal(viewer.PageCountInternal), static viewer => viewer.PageCountInternal > 0);
        AddCommandBinding(NavigationCommands.PreviousPage, static viewer => viewer.GoToPageInternal(viewer._pageNumber - 1), static viewer => viewer._pageNumber > 1);
        AddCommandBinding(NavigationCommands.NextPage, static viewer => viewer.GoToPageInternal(viewer._pageNumber + 1), static viewer => viewer._pageNumber > 0 && viewer._pageNumber < viewer.PageCountInternal);
        AddCommandBinding(ApplicationCommands.SelectAll, static viewer => viewer.SelectAll(), static viewer => viewer.Document != null && viewer.IsSelectionEnabled);
    }

    private void AddCommandBinding(RoutedUICommand command, Action<FlowDocumentScrollViewer> execute, Predicate<FlowDocumentScrollViewer> canExecute)
    {
        CommandBindings.Add(new CommandBinding(command, (_, _) => execute(this), (_, args) => args.CanExecute = canExecute(this)));
    }

    private void SelectAll()
    {
        _search.SelectAll();
        ApplySelection(_search.Selection);
    }

    private void AttachDocument(FlowDocument? document)
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
        _pageNumber = document == null ? 0 : 1;
        if (document == null)
            SetValue(IsFindToolBarVisiblePropertyKey, false);
        _restoreScroll = false;
        _retainedOffset = default;
        ApplyDocumentVisual();
        UpdateVisualState();
        _contentHost?.ScrollToTop();
        InvalidateMeasure();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyDocumentVisual()
    {
        var document = Document;
        _textView.SetDocument(document);
        var scale = Zoom / 100.0;
        if (_textView.LayoutTransform is not ScaleTransform transform || transform.ScaleX != scale || transform.ScaleY != scale)
            _textView.LayoutTransform = new ScaleTransform(scale, scale);
        // LayoutTransform contributes the scaled scroll extent. The retained
        // renderer also needs the matching visual transform for text/hit/AX geometry.
        if (_textView.RenderTransform is not ScaleTransform visualScale || visualScale.ScaleX != scale || visualScale.ScaleY != scale)
            _textView.RenderTransform = new ScaleTransform(scale, scale);
        _textView.IsTextSelectionEnabled = IsSelectionEnabled;
        ApplySelectionVisual();
        _textView.InvalidateMeasure();
    }

    private void ApplySelectionVisual()
    {
        _textView.SelectionBrush = CreateSelectionBrush(SelectionBrush, SelectionOpacity);
        if (!IsSelectionEnabled || (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled))
        {
            _textView.SetSelection(_search.Selection, highlight: false);
        }
        else if (_search.Selection != null)
        {
            ApplySelection(_search.Selection);
        }
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
        ApplyDocumentVisual();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPaginationChanged(object? sender, EventArgs e)
    {
        var pageNumber = _pageNumber;
        ApplyDocumentVisual();
        // ApplySelection has already scrolled to the original selected range.
        // A page boundary can have different geometry from this continuous view.
        if (IsSelectionEnabled && (IsSelectionActive || IsInactiveSelectionHighlightEnabled) && _search.Selection is { IsEmpty: false })
            return;
        GoToPageInternal(PageCountInternal > 0 ? Math.Clamp(pageNumber, 1, PageCountInternal) : 0);
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_contentHost == null || !ReferenceEquals(sender, _contentHost))
            return;
        if (Document == null || PageCountInternal == 0)
        {
            _pageNumber = 0;
            return;
        }

        var top = _contentHost.VerticalOffset / (Zoom / 100.0);
        var line = _textView.Lines.LastOrDefault(line => line.Top <= top);
        _pageNumber = FlowDocumentViewerSupport.GetPageNumberForOffset(Document, line.Start);
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => CommandManager.InvalidateRequerySuggested();
    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.AttachDocument((FlowDocument? )e.NewValue);
        }
    }

    private static void OnZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.UpdateZoomState();
        }
    }

    private static void OnMinZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.CoerceValue(MaxZoomProperty);
            viewer.CoerceValue(ZoomProperty);
            viewer.UpdateZoomState();
        }
    }

    private static void OnMaxZoomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.CoerceValue(ZoomProperty);
            viewer.UpdateZoomState();
        }
    }

    private static object? CoerceZoom(DependencyObject d, object? value) => d is FlowDocumentScrollViewer viewer && value is double zoom ? FlowDocumentViewerSupport.CoerceZoom(zoom, viewer.MinZoom, viewer.MaxZoom) : value;
    private static object? CoerceMaxZoom(DependencyObject d, object? value) => d is FlowDocumentScrollViewer viewer && value is double maxZoom ? Math.Max(viewer.MinZoom, maxZoom) : value;
    private static void OnScrollBarVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.InvalidateMeasure();
        }
    }

    private static void OnIsSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.ApplyDocumentVisual();
        }
    }

    private static void OnSelectionVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.ApplySelectionVisual();
            viewer.InvalidateVisual();
        }
    }
}
