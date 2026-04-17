using Jalium.UI.Media;
using Jalium.UI;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a view that displays a single page of a document.
/// </summary>
public class DocumentPageView : FrameworkElement
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
            new PropertyMetadata(StretchDirection.Both, OnLayoutPropertyChanged));

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
        set => SetValue(DocumentPaginatorProperty, value);
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
    public DocumentPage? DocumentPage { get; private set; }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        LoadPage();

        if (DocumentPage == null || DocumentPage == Primitives.DocumentPage.Missing)
        {
            return Size.Empty;
        }

        var pageSize = DocumentPage.Size;

        // Calculate stretched size
        var scale = CalculateScale(availableSize, pageSize);

        return new Size(pageSize.Width * scale, pageSize.Height * scale);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Page visual is rendered in OnRender
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
        if (DocumentPaginator == null || PageNumber < 0)
        {
            DocumentPage = null;
            return;
        }

        if (PageNumber >= DocumentPaginator.PageCount)
        {
            DocumentPage = Primitives.DocumentPage.Missing;
            return;
        }

        DocumentPage = DocumentPaginator.GetPage(PageNumber);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        if (DocumentPage?.Visual == null)
        {
            // Draw placeholder
            dc.DrawRectangle(ResolvePlaceholderBrush(), null, new Rect(RenderSize));
            return;
        }

        // The actual page visual would be rendered here
        // For now, draw a representation

        // Draw shadow
        var shadowRect = new Rect(2, 2, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(ResolveShadowBrush(), null, shadowRect);

        // Draw page
        var pageRect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(ResolvePageBrush(), null, pageRect);

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

    #region Property Changed Callbacks

    private static void OnDocumentPaginatorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
            view.InvalidateMeasure();
        }
    }

    private static void OnPageNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentPageView view)
        {
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
