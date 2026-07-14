using Jalium.UI.Media;
using Jalium.UI;
using Jalium.UI.Documents;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a view that displays a single page of a document.
/// </summary>
public class DocumentPageView : FrameworkElement, IServiceProvider, IDisposable
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_placeholderBrush = new(Color.FromRgb(240, 240, 240));
    private static readonly SolidColorBrush s_pageBrush = new(Color.White);
    private static readonly SolidColorBrush s_shadowBrush = new(Color.FromArgb(32, 0, 0, 0));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly Pen s_borderPen = new(s_borderBrush, 1);

    // Cached border pen
    private Pen? _borderPen;
    private Brush? _borderPenBrush;
    private double _borderPenThickness;

    private DocumentPage? _documentPage;
    private bool _newPageConnected;
    private bool _disposed;

    private const string PlaceholderBrushKey = "ControlBackground";
    private const string PlaceholderSecondaryBrushKey = "ControlFillColorTertiaryBrush";
    private const string PageBrushKey = "WindowBackground";
    private const string PageSecondaryBrushKey = "CardBackgroundFillColorDefaultBrush";
    private const string ShadowBrushKey = "SmokeFillColorDefaultBrush";
    private const string BorderBrushKey = "ControlBorder";
    private const string BorderSecondaryBrushKey = "DividerStrokeColorDefaultBrush";

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(DocumentPageView),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BorderBrushProperty =
        DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(DocumentPageView),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BorderThicknessProperty =
        DependencyProperty.Register(nameof(BorderThickness), typeof(Thickness), typeof(DocumentPageView),
            new PropertyMetadata(new Thickness(1), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DocumentPaginator dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DocumentPaginatorProperty =
        DependencyProperty.Register(nameof(DocumentPaginator), typeof(DocumentPaginator), typeof(DocumentPageView),
            new PropertyMetadata(null, OnDocumentPaginatorChanged));

    /// <summary>
    /// Identifies the PageNumber dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageNumberProperty =
        DependencyProperty.Register(nameof(PageNumber), typeof(int), typeof(DocumentPageView),
            new PropertyMetadata(0, OnPageNumberChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(DocumentPageView),
            new PropertyMetadata(Stretch.Uniform, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(DocumentPageView),
            new PropertyMetadata(StretchDirection.DownOnly, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the background brush used for the rendered page surface.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the border brush used for the rendered page outline.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the rendered page outline.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness BorderThickness
    {
        get => (Thickness)(GetValue(BorderThicknessProperty) ?? new Thickness(1));
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the document paginator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DocumentPaginator? DocumentPaginator
    {
        get => (DocumentPaginator?)GetValue(DocumentPaginatorProperty);
        set
        {
            CheckDisposed();
            SetValue(DocumentPaginatorProperty, value);
        }
    }

    /// <summary>
    /// Gets or sets the page number to display (0-based).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty)!;
        set => SetValue(PageNumberProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch direction.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>
    /// Gets the current document page.
    /// </summary>
    public DocumentPage DocumentPage => _documentPage ?? Jalium.UI.Documents.DocumentPage.Missing;

    #endregion

    #region Events

    /// <summary>
    /// Occurs after the current document page has been connected during arrange.
    /// </summary>
    public event EventHandler? PageConnected;

    /// <summary>
    /// Occurs after the current document page has been disconnected.
    /// </summary>
    public event EventHandler? PageDisconnected;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        CheckDisposed();
        LoadPage();

        if (_documentPage == null || _documentPage == Jalium.UI.Documents.DocumentPage.Missing)
        {
            return default(Size);
        }

        var pageSize = _documentPage.Size;
        if (pageSize.IsEmpty)
        {
            return default(Size);
        }

        // Calculate stretched size
        var scale = CalculateScale(availableSize, pageSize);

        return new Size(pageSize.Width * scale, pageSize.Height * scale);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        CheckDisposed();

        if (_newPageConnected)
        {
            OnPageConnected();
        }

        // Page visual is rendered in OnRender.
        return finalSize;
    }

    private double CalculateScale(Size availableSize, Size pageSize)
    {
        if (pageSize.Width <= 0 || pageSize.Height <= 0)
            return 1.0;

        var scaleX = double.IsPositiveInfinity(availableSize.Width) ? 1.0 : availableSize.Width / pageSize.Width;
        var scaleY = double.IsPositiveInfinity(availableSize.Height) ? 1.0 : availableSize.Height / pageSize.Height;

        switch (Stretch)
        {
            case Stretch.None:
                return 1.0;

            case Stretch.Fill:
                return StretchDirection switch
                {
                    StretchDirection.UpOnly => Math.Max(1.0, Math.Max(scaleX, scaleY)),
                    StretchDirection.DownOnly => Math.Min(1.0, Math.Min(scaleX, scaleY)),
                    _ => 1.0 // Fill doesn't maintain aspect ratio, use average
                };

            case Stretch.Uniform:
                var uniformScale = Math.Min(scaleX, scaleY);
                return StretchDirection switch
                {
                    StretchDirection.UpOnly => Math.Max(1.0, uniformScale),
                    StretchDirection.DownOnly => Math.Min(1.0, uniformScale),
                    _ => uniformScale
                };

            case Stretch.UniformToFill:
                var fillScale = Math.Max(scaleX, scaleY);
                return StretchDirection switch
                {
                    StretchDirection.UpOnly => Math.Max(1.0, fillScale),
                    StretchDirection.DownOnly => Math.Min(1.0, fillScale),
                    _ => fillScale
                };

            default:
                return 1.0;
        }
    }

    #endregion

    #region Page Loading

    private void LoadPage()
    {
        if (_documentPage != null)
        {
            return;
        }

        var paginator = DocumentPaginator;
        if (paginator == null)
        {
            return;
        }

        if (PageNumber < 0)
        {
            _documentPage = Jalium.UI.Documents.DocumentPage.Missing;
            return;
        }

        _documentPage = paginator.GetPage(PageNumber) ?? Jalium.UI.Documents.DocumentPage.Missing;
        _newPageConnected = true;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        if (_documentPage?.Visual == null)
        {
            // Draw placeholder
            dc.DrawRectangle(ResolvePlaceholderBrush(), null, new Rect(RenderSize));
            return;
        }

        // Draw shadow
        var shadowRect = new Rect(2, 2, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(ResolveShadowBrush(), null, shadowRect);

        // Draw page
        var pageRect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(ResolvePageBrush(), null, pageRect);

        // Composite the paginator's actual visual into the page surface.  A VisualBrush keeps
        // the source visual live, so text/selection invalidation is reflected without replacing
        // the DocumentPageView or rasterizing the document into a stale bitmap.
        var documentVisualBrush = new VisualBrush(_documentPage.Visual)
        {
            Stretch = Stretch.Fill,
        };
        dc.DrawRectangle(documentVisualBrush, null, pageRect);

        // Draw border
        dc.DrawRectangle(null, ResolveBorderPen(), pageRect);
    }

    private Brush ResolvePlaceholderBrush()
    {
        return ResolveThemeBrush(PlaceholderBrushKey, s_placeholderBrush, PlaceholderSecondaryBrushKey);
    }

    private Brush ResolvePageBrush()
    {
        if (Background != null)
        {
            return Background;
        }

        return ResolveThemeBrush(PageBrushKey, s_pageBrush, PageSecondaryBrushKey);
    }

    private Brush ResolveShadowBrush()
    {
        return ResolveThemeBrush(ShadowBrushKey, s_shadowBrush);
    }

    private Pen ResolveBorderPen()
    {
        var borderBrush = BorderBrush ?? ResolveThemeBrush(BorderBrushKey, s_borderBrush, BorderSecondaryBrushKey);
        var borderThickness = GetMaxBorderThickness();
        if (_borderPen == null || _borderPenBrush != borderBrush || _borderPenThickness != borderThickness)
        {
            _borderPenBrush = borderBrush;
            _borderPenThickness = borderThickness;
            _borderPen = new Pen(borderBrush, borderThickness);
        }
        return _borderPen;
    }

    private double GetMaxBorderThickness()
    {
        var thickness = BorderThickness;
        return Math.Max(thickness.Left, Math.Max(thickness.Top, Math.Max(thickness.Right, thickness.Bottom)));
    }

    private Brush ResolveThemeBrush(string primaryKey, Brush fallback, string? secondaryKey = null)
    {
        if (TryFindResource(primaryKey) is Brush primaryBrush)
            return primaryBrush;

        if (!string.IsNullOrWhiteSpace(secondaryKey) && TryFindResource(secondaryKey) is Brush secondaryBrush)
            return secondaryBrush;

        if (Application.Current?.Resources.TryGetValue(primaryKey, out var appPrimary) == true &&
            appPrimary is Brush appPrimaryBrush)
        {
            return appPrimaryBrush;
        }

        if (!string.IsNullOrWhiteSpace(secondaryKey) &&
            Application.Current?.Resources.TryGetValue(secondaryKey, out var appSecondary) == true &&
            appSecondary is Brush appSecondaryBrush)
        {
            return appSecondaryBrush;
        }

        return fallback;
    }

    #endregion

    #region Lifetime and Services

    /// <summary>
    /// Gets whether this instance has been disposed.
    /// </summary>
    protected bool IsDisposed => _disposed;

    /// <summary>
    /// Releases the current page and paginator association.
    /// </summary>
    protected void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCurrentPage();
        SetValue(DocumentPaginatorProperty, null);
    }

    /// <summary>
    /// Returns a text service supplied by the current document paginator.
    /// </summary>
    protected object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        CheckDisposed();

        if (serviceType == typeof(Jalium.UI.Documents.ITextContainer) &&
            DocumentPaginator is IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService(serviceType);
        }

        return null;
    }

    private void DisposeCurrentPage()
    {
        if (_documentPage == null)
        {
            return;
        }

        _documentPage = null;
        _newPageConnected = false;
        InvalidateVisual();
        PageDisconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageConnected()
    {
        _newPageConnected = false;
        if (_documentPage != null)
        {
            PageConnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CheckDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    object? IServiceProvider.GetService(Type serviceType) => GetService(serviceType);

    void IDisposable.Dispose() => Dispose();

    #endregion

    #region Property Changed Callbacks

    private static void OnDocumentPaginatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
            view.DisposeCurrentPage();
            view.InvalidateMeasure();
        }
    }

    private static void OnPageNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
            view.DisposeCurrentPage();
            view.InvalidateMeasure();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
            view.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
            view.InvalidateVisual();
        }
    }

    #endregion
}
