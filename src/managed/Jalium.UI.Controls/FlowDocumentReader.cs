using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a control for viewing flow content with built-in support for multiple viewing modes.
/// </summary>
[ContentProperty("Document")]
public class FlowDocumentReader : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentReader),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the ViewingMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty ViewingModeProperty =
        DependencyProperty.Register(nameof(ViewingMode), typeof(FlowDocumentReaderViewingMode), typeof(FlowDocumentReader),
            new PropertyMetadata(FlowDocumentReaderViewingMode.Page));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(FlowDocumentReader),
            new PropertyMetadata(100.0));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(FlowDocumentReader),
            new PropertyMetadata(80.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(FlowDocumentReader),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the ZoomIncrement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ZoomIncrementProperty =
        DependencyProperty.Register(nameof(ZoomIncrement), typeof(double), typeof(FlowDocumentReader),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the IsPageViewEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPageViewEnabledProperty =
        DependencyProperty.Register(nameof(IsPageViewEnabled), typeof(bool), typeof(FlowDocumentReader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsTwoPageViewEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTwoPageViewEnabledProperty =
        DependencyProperty.Register(nameof(IsTwoPageViewEnabled), typeof(bool), typeof(FlowDocumentReader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsScrollViewEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsScrollViewEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollViewEnabled), typeof(bool), typeof(FlowDocumentReader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsFindEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsFindEnabledProperty =
        DependencyProperty.Register(nameof(IsFindEnabled), typeof(bool), typeof(FlowDocumentReader),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsPrintEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPrintEnabledProperty =
        DependencyProperty.Register(nameof(IsPrintEnabled), typeof(bool), typeof(FlowDocumentReader),
            new PropertyMetadata(true));

    private static readonly DependencyPropertyKey PageCountPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PageCount), typeof(int), typeof(FlowDocumentReader),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the PageCount dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageCountProperty = PageCountPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey PageNumberPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(PageNumber), typeof(int), typeof(FlowDocumentReader),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the PageNumber dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PageNumberProperty = PageNumberPropertyKey.DependencyProperty;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the FlowDocument displayed by this reader.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FlowDocument? Document
    {
        get => (FlowDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>
    /// Gets or sets the current viewing mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public FlowDocumentReaderViewingMode ViewingMode
    {
        get => (FlowDocumentReaderViewingMode)GetValue(ViewingModeProperty);
        set => SetValue(ViewingModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the current zoom level as a percentage.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinZoom
    {
        get => (double)GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum zoom level.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxZoom
    {
        get => (double)GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the zoom increment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double ZoomIncrement
    {
        get => (double)GetValue(ZoomIncrementProperty);
        set => SetValue(ZoomIncrementProperty, value);
    }

    /// <summary>
    /// Gets or sets whether page view mode is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPageViewEnabled
    {
        get => (bool)GetValue(IsPageViewEnabledProperty);
        set => SetValue(IsPageViewEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether two-page view mode is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTwoPageViewEnabled
    {
        get => (bool)GetValue(IsTwoPageViewEnabledProperty);
        set => SetValue(IsTwoPageViewEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether scroll view mode is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsScrollViewEnabled
    {
        get => (bool)GetValue(IsScrollViewEnabledProperty);
        set => SetValue(IsScrollViewEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the find feature is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsFindEnabled
    {
        get => (bool)GetValue(IsFindEnabledProperty);
        set => SetValue(IsFindEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the print feature is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPrintEnabled
    {
        get => (bool)GetValue(IsPrintEnabledProperty);
        set => SetValue(IsPrintEnabledProperty, value);
    }

    /// <summary>
    /// Gets the page count of the document.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int PageCount
    {
        get => (int)GetValue(PageCountProperty);
        private set => SetValue(PageCountPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets the current page number.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int PageNumber
    {
        get => (int)GetValue(PageNumberProperty);
        private set => SetValue(PageNumberPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets the selection object for this reader.
    /// </summary>
    public TextSelection? Selection { get; private set; }

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
    /// Navigates to the first page.
    /// </summary>
    public void FirstPage()
    {
        PageNumber = 1;
    }

    /// <summary>
    /// Navigates to the last page.
    /// </summary>
    public void LastPage()
    {
        PageNumber = PageCount;
    }

    /// <summary>
    /// Switches to page view mode.
    /// </summary>
    public void SwitchViewingMode(FlowDocumentReaderViewingMode mode)
    {
        ViewingMode = mode;
    }

    /// <summary>
    /// Finds text in the document.
    /// </summary>
    public bool Find(string searchText)
    {
        if (Document == null || string.IsNullOrEmpty(searchText))
            return false;

        var text = Document.GetText();
        return text.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FlowDocumentReader reader)
        {
            reader.OnDocumentChanged((FlowDocument?)e.OldValue, (FlowDocument?)e.NewValue);
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

/// <summary>
/// Specifies the viewing mode of a FlowDocumentReader control.
/// </summary>
public enum FlowDocumentReaderViewingMode
{
    /// <summary>
    /// Single page viewing mode.
    /// </summary>
    Page,

    /// <summary>
    /// Two-page viewing mode.
    /// </summary>
    TwoPage,

    /// <summary>
    /// Scroll viewing mode with continuous content.
    /// </summary>
    Scroll
}
