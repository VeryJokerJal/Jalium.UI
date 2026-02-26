using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a horizontal bar at the bottom of a window for displaying status information.
/// </summary>
public sealed class StatusBar : ItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the SeparatorBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(StatusBar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the brush used for separators between items.
    /// </summary>
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBar"/> class.
    /// </summary>
    public StatusBar()
    {
        // Default styling
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        Foreground = new SolidColorBrush(Color.White);
        Height = 24;
        Padding = new Thickness(4, 0, 4, 0);
    }

    #endregion

    #region Item Generation

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new StatusBarPanel { Orientation = Orientation.Horizontal };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new StatusBarItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is StatusBarItem || item is Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is StatusBarItem statusBarItem)
        {
            statusBarItem.Content = item;
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw top border
        if (BorderBrush != null && BorderThickness.Top > 0)
        {
            var borderPen = new Pen(BorderBrush, BorderThickness.Top);
            dc.DrawLine(borderPen, new Point(0, 0), new Point(rect.Width, 0));
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar statusBar)
        {
            statusBar.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Represents an item in a StatusBar.
/// </summary>
public sealed class StatusBarItem : ContentControl
{
    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarItem"/> class.
    /// </summary>
    public StatusBarItem()
    {
        Padding = new Thickness(8, 0, 8, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;

        if (Content is string text)
        {
            var fontFamily = FontFamily ?? "Segoe UI";
            var fontSize = FontSize > 0 ? FontSize : 12;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            Interop.TextMeasurement.MeasureText(formattedText);
            return new Size(
                formattedText.Width + padding.TotalWidth,
                Math.Max(24, formattedText.Height + padding.TotalHeight));
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return new Size(
                element.DesiredSize.Width + padding.TotalWidth,
                Math.Max(24, element.DesiredSize.Height + padding.TotalHeight));
        }

        return new Size(padding.TotalWidth, 24);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw content
        if (Content is string text)
        {
            var fgBrush = Foreground ?? new SolidColorBrush(Color.White);
            var formattedText = new FormattedText(text, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 12)
            {
                Foreground = fgBrush
            };
            Interop.TextMeasurement.MeasureText(formattedText);

            var textX = padding.Left;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    #endregion
}

/// <summary>
/// A specialized panel for StatusBar items that supports horizontal layout with separators.
/// </summary>
internal sealed class StatusBarPanel : StackPanel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarPanel"/> class.
    /// </summary>
    public StatusBarPanel()
    {
        Orientation = Orientation.Horizontal;
    }
}
