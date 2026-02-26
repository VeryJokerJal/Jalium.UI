using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a control for viewing flow content in a continuous, scrolling mode.
/// </summary>
[ContentProperty("Document")]
public sealed class FlowDocumentScrollViewer : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Document dependency property.
    /// </summary>
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(FlowDocument), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(null, OnDocumentChanged));

    /// <summary>
    /// Identifies the Zoom dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(100.0));

    /// <summary>
    /// Identifies the MinZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MinZoomProperty =
        DependencyProperty.Register(nameof(MinZoom), typeof(double), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(80.0));

    /// <summary>
    /// Identifies the MaxZoom dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxZoomProperty =
        DependencyProperty.Register(nameof(MaxZoom), typeof(double), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(200.0));

    /// <summary>
    /// Identifies the ZoomIncrement dependency property.
    /// </summary>
    public static readonly DependencyProperty ZoomIncrementProperty =
        DependencyProperty.Register(nameof(ZoomIncrement), typeof(double), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the IsToolBarVisible dependency property.
    /// </summary>
    public static readonly DependencyProperty IsToolBarVisibleProperty =
        DependencyProperty.Register(nameof(IsToolBarVisible), typeof(bool), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsSelectionEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectionEnabledProperty =
        DependencyProperty.Register(nameof(IsSelectionEnabled), typeof(bool), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(FlowDocumentScrollViewer),
            new PropertyMetadata(ScrollBarVisibility.Visible));

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
    /// Gets or sets whether the toolbar is visible.
    /// </summary>
    public bool IsToolBarVisible
    {
        get => (bool)GetValue(IsToolBarVisibleProperty);
        set => SetValue(IsToolBarVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text selection is enabled.
    /// </summary>
    public bool IsSelectionEnabled
    {
        get => (bool)GetValue(IsSelectionEnabledProperty);
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scrollbar visibility.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scrollbar visibility.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets the selection object for this viewer.
    /// </summary>
    public TextSelection? Selection { get; private set; }

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
        if (d is FlowDocumentScrollViewer viewer)
        {
            viewer.OnDocumentChanged((FlowDocument?)e.OldValue, (FlowDocument?)e.NewValue);
        }
    }

    /// <summary>
    /// Called when the document changes.
    /// </summary>
    protected void OnDocumentChanged(FlowDocument? oldDocument, FlowDocument? newDocument)
    {
        InvalidateArrange();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure as a simple content presenter for the document
        return base.MeasureOverride(availableSize);
    }

    #endregion
}

/// <summary>
/// Represents a text selection in a flow document viewer.
/// </summary>
public sealed class TextSelection
{
    /// <summary>
    /// Gets the starting position of the selection.
    /// </summary>
    public int Start { get; internal set; }

    /// <summary>
    /// Gets the ending position of the selection.
    /// </summary>
    public int End { get; internal set; }

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the selection is empty.
    /// </summary>
    public bool IsEmpty => Start == End;

    /// <summary>
    /// Selects all content in the document.
    /// </summary>
    public void SelectAll(string fullText)
    {
        Start = 0;
        End = fullText.Length;
        Text = fullText;
    }
}
