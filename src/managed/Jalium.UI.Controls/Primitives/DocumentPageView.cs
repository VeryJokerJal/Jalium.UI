using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a view that displays a single page of a document.
/// </summary>
public sealed class DocumentPageView : FrameworkElement
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_placeholderBrush = new(Color.FromRgb(240, 240, 240));
    private static readonly SolidColorBrush s_pageBrush = new(Color.White);
    private static readonly SolidColorBrush s_shadowBrush = new(Color.FromArgb(32, 0, 0, 0));
    private static readonly SolidColorBrush s_borderBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly Pen s_borderPen = new(s_borderBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the DocumentPaginator dependency property.
    /// </summary>
    public static readonly DependencyProperty DocumentPaginatorProperty =
        DependencyProperty.Register(nameof(DocumentPaginator), typeof(DocumentPaginator), typeof(DocumentPageView),
            new PropertyMetadata(null, OnDocumentPaginatorChanged));

    /// <summary>
    /// Identifies the PageNumber dependency property.
    /// </summary>
    public static readonly DependencyProperty PageNumberProperty =
        DependencyProperty.Register(nameof(PageNumber), typeof(int), typeof(DocumentPageView),
            new PropertyMetadata(0, OnPageNumberChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(DocumentPageView),
            new PropertyMetadata(Stretch.Uniform, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(DocumentPageView),
            new PropertyMetadata(StretchDirection.Both, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the document paginator.
    /// </summary>
    public DocumentPaginator? DocumentPaginator
    {
        get => (DocumentPaginator?)GetValue(DocumentPaginatorProperty);
        set => SetValue(DocumentPaginatorProperty, value);
    }

    /// <summary>
    /// Gets or sets the page number to display (0-based).
    /// </summary>
    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty)!;
        set => SetValue(PageNumberProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch mode.
    /// </summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the stretch direction.
    /// </summary>
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
            dc.DrawRectangle(s_placeholderBrush, null, new Rect(RenderSize));
            return;
        }

        // The actual page visual would be rendered here
        // For now, draw a representation

        // Draw shadow
        var shadowRect = new Rect(2, 2, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(s_shadowBrush, null, shadowRect);

        // Draw page
        var pageRect = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        dc.DrawRectangle(s_pageBrush, null, pageRect);

        // Draw border
        dc.DrawRectangle(null, s_borderPen, pageRect);
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

    #endregion
}

/// <summary>
/// Describes how content should be stretched to fill available space.
/// </summary>
public enum Stretch
{
    /// <summary>
    /// Content preserves its original size.
    /// </summary>
    None,

    /// <summary>
    /// Content is resized to fill the destination, aspect ratio is not preserved.
    /// </summary>
    Fill,

    /// <summary>
    /// Content is resized to fit while preserving aspect ratio.
    /// </summary>
    Uniform,

    /// <summary>
    /// Content is resized to fill completely while preserving aspect ratio.
    /// </summary>
    UniformToFill
}

/// <summary>
/// Describes the direction that stretching is permitted.
/// </summary>
public enum StretchDirection
{
    /// <summary>
    /// Scale up only.
    /// </summary>
    UpOnly,

    /// <summary>
    /// Scale down only.
    /// </summary>
    DownOnly,

    /// <summary>
    /// Scale in both directions.
    /// </summary>
    Both
}
