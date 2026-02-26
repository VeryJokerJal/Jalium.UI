using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a control for viewing flow content in a fixed page-by-page format.
/// </summary>
[ContentProperty("Document")]
public sealed class FlowDocumentPageViewer : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(100.0));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(80.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the ZoomIncrement dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomIncrementProperty =
        DependencyProperty.Register(nameof(ZoomIncrement), typeof(double), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(10.0));

    private static readonly DependencyPropertyKey PageCountPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PageCount), typeof(int), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the PageCount dependency property.
    /// </summary>
    public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey PageNumberPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PageNumber), typeof(int), typeof(FlowDocumentPageViewer),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the PageNumber dependency property.
    /// </summary>
    public static readonly DependencyProperty PageNumberProperty = PageNumberPropertyKey.DependencyProperty;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the FlowDocument displayed by this viewer.
    /// </summary>
    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the current zoom level as a percentage.
    /// </summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum zoom level.
    /// </summary>
    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum zoom level.
    /// </summary>
    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom increment.
    /// </summary>
    public double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty);
        set => SetValue(ZoomIncrementProperty, value);
    }

    /// <summary>
    /// Gets the page count of the document.
    /// </summary>
    public int PageCount
    {
        get => (int)GetValue(PageCountProperty);
        private set => SetValue(PageCountPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets the current page number.
    /// </summary>
    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty);
        private set => SetValue(PageNumberPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the viewer can navigate to the next page.
    /// </summary>
    public bool CanGoToNextPage => PageNumber < PageCount;

    /// <summary>
    /// Gets a value indicating whether the viewer can navigate to the previous page.
    /// </summary>
    public bool CanGoToPreviousPage => PageNumber > 1;

    #endregion

    #region Methods

    /// <summary>
    /// Increases the zoom level.
    /// </summary>
    public void IncreaseZoom()
    {
        Zoom = Math.Min(MaxZoom, Zoom + ZoomIncrement);
    }

    /// <summary>
    /// Decreases the zoom level.
    /// </summary>
    public void DecreaseZoom()
    {
        Zoom = Math.Max(MinZoom, Zoom - ZoomIncrement);
    }

    /// <summary>
    /// Navigates to the first page.
    /// </summary>
    public void FirstPage()
    {
        if (PageCount > 0)
            PageNumber = 1;
    }

    /// <summary>
    /// Navigates to the last page.
    /// </summary>
    public void LastPage()
    {
        if (PageCount > 0)
            PageNumber = PageCount;
    }

    /// <summary>
    /// Navigates to the next page.
    /// </summary>
    public void NextPage()
    {
        if (CanGoToNextPage)
            PageNumber++;
    }

    /// <summary>
    /// Navigates to the previous page.
    /// </summary>
    public void PreviousPage()
    {
        if (CanGoToPreviousPage)
            PageNumber--;
    }

    /// <summary>
    /// Navigates to the specified page number.
    /// </summary>
    public void GoToPage(int pageNumber)
    {
        if (pageNumber >= 1 && pageNumber <= PageCount)
            PageNumber = pageNumber;
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentPageViewer viewer)
        {
            viewer.OnDocumentChanged((FlowDocument?)e.OldValue, (FlowDocument?)e.NewValue);
        }
    }

    /// <summary>
    /// Called when the document changes.
    /// </summary>
    protected void OnDocumentChanged(FlowDocument? oldDocument, FlowDocument? newDocument)
    {
        PageCount = newDocument != null ? 1 : 0;
        PageNumber = newDocument != null ? 1 : 0;
        InvalidateArrange();
    }

    #endregion
}
