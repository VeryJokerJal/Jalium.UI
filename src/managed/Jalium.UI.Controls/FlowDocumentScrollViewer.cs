using Jalium.UI.Documents;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays a <see cref="FlowDocument"/> as a continuous scrolling text surface.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Document")]
public class FlowDocumentScrollViewer : Control
{
    private readonly ScrollViewer _contentHost;
    private readonly TextBlock _textView;
    private readonly FlowDocumentSearchSession _search = new();
    private FlowDocument? _attachedDocument;
    private bool _isPrinting;
    private int _pageNumber;

    public FlowDocumentScrollViewer()
    {
        _textView = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        _contentHost = new ScrollViewer { Content = _textView };
        _contentHost.ScrollChanged += OnContentScrollChanged;
        AddVisualChild(_contentHost);
        RegisterCommands();
        ApplyScrollBarVisibility();
        ApplyDocumentVisual();
        UpdateZoomState();
    }

    #region Dependency properties

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document), typeof(FlowDocument), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(null, OnDocumentChanged));

    public static readonly DependencyProperty ZoomProperty =
        FlowDocumentPageViewer.ZoomProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(100.0, OnZoomChanged, CoerceZoom));

    public static readonly DependencyProperty MinZoomProperty =
        FlowDocumentPageViewer.MinZoomProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(80.0, OnMinZoomChanged));

    public static readonly DependencyProperty MaxZoomProperty =
        FlowDocumentPageViewer.MaxZoomProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(200.0, OnMaxZoomChanged, CoerceMaxZoom));

    public static readonly DependencyProperty ZoomIncrementProperty =
        FlowDocumentPageViewer.ZoomIncrementProperty.AddOwner(typeof(FlowDocumentScrollViewer));

    public static readonly DependencyProperty IsToolBarVisibleProperty =
        DependencyProperty.Register(nameof(IsToolBarVisible), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSelectionEnabled), typeof(bool), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(true, OnIsSelectionEnabledChanged));

    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(
            nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged));

    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(
            nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Visible, OnScrollBarVisibilityChanged));

    private static readonly DependencyPropertyKey CanIncreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanIncreaseZoom), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(true));

    public static readonly DependencyProperty CanIncreaseZoomProperty = CanIncreaseZoomPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey CanDecreaseZoomPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanDecreaseZoom), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(true));

    public static readonly DependencyProperty CanDecreaseZoomProperty = CanDecreaseZoomPropertyKey.DependencyProperty;

    public static readonly DependencyProperty SelectionBrushProperty =
        TextBoxBase.SelectionBrushProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(SystemColors.HighlightBrush, OnSelectionVisualChanged));

    public static readonly DependencyProperty SelectionOpacityProperty =
        TextBoxBase.SelectionOpacityProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(1.0, OnSelectionVisualChanged));

    private static readonly DependencyPropertyKey IsSelectionActivePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsSelectionActive), typeof(bool), typeof(FlowDocumentScrollViewer), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectionActiveProperty = IsSelectionActivePropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty =
        TextBoxBase.IsInactiveSelectionHighlightEnabledProperty.AddOwner(
            typeof(FlowDocumentScrollViewer), new PropertyMetadata(false, OnSelectionVisualChanged));

    #endregion

    #region Properties

    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty)!;
        set => SetValue(ZoomProperty, value);
    }

    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty)!;
        set => SetValue(MinZoomProperty, value);
    }

    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty)!;
        set => SetValue(MaxZoomProperty, value);
    }

    public double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty)!;
        set => SetValue(ZoomIncrementProperty, value);
    }

    public bool IsToolBarVisible
    {
        get => (bool)GetValue(IsToolBarVisibleProperty)!;
        set => SetValue(IsToolBarVisibleProperty, value);
    }

    public bool IsSelectionEnabled
    {
        get => (bool)GetValue(IsSelectionEnabledProperty)!;
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    public bool CanIncreaseZoom => (bool)GetValue(CanIncreaseZoomProperty)!;

    public bool CanDecreaseZoom => (bool)GetValue(CanDecreaseZoomProperty)!;

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

    public TextSelection? Selection => _search.Selection;

    public string SearchText { get; set; } = string.Empty;

    public bool IsFindToolBarVisible { get; private set; }

    internal int PageCountInternal => FlowDocumentViewerSupport.GetPageCount(Document);

    internal int PageNumberInternal => _pageNumber;

    #endregion

    #region Public operations

    public void IncreaseZoom() => OnIncreaseZoomCommand();

    public void DecreaseZoom() => OnDecreaseZoomCommand();

    public void Find() => OnFindCommand();

    public bool Find(string searchText)
    {
        SearchText = searchText ?? throw new ArgumentNullException(nameof(searchText));
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

        IsFindToolBarVisible = !IsFindToolBarVisible;
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

    protected override void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
        base.OnIsKeyboardFocusWithinChanged(isFocusWithin);
        SetValue(IsSelectionActivePropertyKey, isFocusWithin);
        ApplySelectionVisual();
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

        return index == baseCount ? _contentHost : throw new ArgumentOutOfRangeException(nameof(index));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _contentHost.Measure(availableSize);
        return _contentHost.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _contentHost.Arrange(new Rect(finalSize));
        return finalSize;
    }

    #endregion

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() =>
        new Jalium.UI.Automation.Peers.FlowDocumentScrollViewerAutomationPeer(this);

    internal void GoToPageInternal(int pageNumber)
    {
        if (Document == null || PageCountInternal == 0)
        {
            _pageNumber = 0;
            _contentHost.ScrollToTop();
            return;
        }

        _pageNumber = Math.Clamp(pageNumber, 1, PageCountInternal);
        var pageHeight = Document.ViewerPaginator.PageSize.Height * (Zoom / 100.0);
        _contentHost.ScrollToVerticalOffset(Math.Max(0.0, (_pageNumber - 1) * pageHeight));
    }

    internal void ApplySelection(TextSelection? selection)
    {
        if (!IsSelectionEnabled || (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled) ||
            Document == null || selection == null ||
            !ReferenceEquals(selection.Start.Document, Document))
        {
            _textView.Select(0, 0);
            return;
        }

        var start = Math.Clamp(selection.Start.DocumentOffset, 0, _textView.Text.Length);
        var end = Math.Clamp(selection.End.DocumentOffset, start, _textView.Text.Length);
        _textView.Select(start, end - start);
        _pageNumber = FlowDocumentViewerSupport.GetPageNumberForOffset(Document, start);

        var textBeforeSelection = _textView.Text.AsSpan(0, start);
        var line = 0;
        foreach (var character in textBeforeSelection)
        {
            if (character == '\n')
            {
                line++;
            }
        }
        _contentHost.ScrollToVerticalOffset(line * Math.Max(1.0, _textView.FontSize * 1.35));
    }

    private void RegisterCommands()
    {
        AddCommandBinding(ApplicationCommands.Find, static viewer => viewer.OnFindCommand(), static viewer => viewer.Document != null);
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
        CommandBindings.Add(new CommandBinding(
            command,
            (_, _) => execute(this),
            (_, args) => args.CanExecute = canExecute(this)));
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
        ApplyDocumentVisual();
        _contentHost.ScrollToTop();
        InvalidateMeasure();
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyDocumentVisual()
    {
        var document = Document;
        _textView.Text = document?.GetText() ?? string.Empty;
        _textView.FontFamily = document?.FontFamily ?? new FontFamily(FrameworkElement.DefaultFontFamilyName);
        _textView.FontSize = Math.Max(1.0, (document?.FontSize ?? 14.0) * Zoom / 100.0);
        _textView.Foreground = document?.Foreground ?? new SolidColorBrush(Color.Black);
        _textView.Background = document?.Background;
        _textView.Padding = document?.PagePadding ?? new Thickness(0);
        _textView.IsTextSelectionEnabled = IsSelectionEnabled;
        ApplySelectionVisual();
        _textView.InvalidateMeasure();
    }

    private void ApplySelectionVisual()
    {
        _textView.SelectionBrush = CreateSelectionBrush(SelectionBrush, SelectionOpacity);
        if (!IsSelectionEnabled || (!IsSelectionActive && !IsInactiveSelectionHighlightEnabled))
        {
            _textView.Select(0, 0);
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

    private void ApplyScrollBarVisibility()
    {
        _contentHost.HorizontalScrollBarVisibility = HorizontalScrollBarVisibility;
        _contentHost.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
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
        GoToPageInternal(PageCountInternal > 0 ? Math.Clamp(pageNumber, 1, PageCountInternal) : 0);
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Document == null || PageCountInternal == 0)
        {
            _pageNumber = 0;
            return;
        }

        var pageHeight = Math.Max(1.0, Document.ViewerPaginator.PageSize.Height * (Zoom / 100.0));
        _pageNumber = Math.Clamp((int)Math.Floor(_contentHost.VerticalOffset / pageHeight) + 1, 1, PageCountInternal);
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.AttachDocument((FlowDocument?)e.NewValue);
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

    private static object? CoerceZoom(DependencyObject d, object? value) =>
        d is FlowDocumentScrollViewer viewer && value is double zoom
            ? FlowDocumentViewerSupport.CoerceZoom(zoom, viewer.MinZoom, viewer.MaxZoom)
            : value;

    private static object? CoerceMaxZoom(DependencyObject d, object? value) =>
        d is FlowDocumentScrollViewer viewer && value is double maxZoom
            ? Math.Max(viewer.MinZoom, maxZoom)
            : value;

    private static void OnScrollBarVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.ApplyScrollBarVisibility();
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
