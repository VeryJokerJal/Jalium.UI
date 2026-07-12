using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a horizontal bar at the bottom of a window for displaying status information.
/// </summary>
public class StatusBar : ItemsControl
{
    private static readonly ResourceKey s_separatorStyleKey =
        new ComponentResourceKey(typeof(StatusBar), nameof(SeparatorStyleKey));

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.StatusBarAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the SeparatorBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeparatorBrushProperty =
        DependencyProperty.Register(nameof(SeparatorBrush), typeof(Brush), typeof(StatusBar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>Identifies the <see cref="ItemContainerTemplateSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ItemContainerTemplateSelectorProperty =
        DependencyProperty.Register(
            nameof(ItemContainerTemplateSelector),
            typeof(ItemContainerTemplateSelector),
            typeof(StatusBar),
            new PropertyMetadata(null, OnContainerTemplateChanged));

    /// <summary>Identifies the <see cref="UsesItemContainerTemplate"/> dependency property.</summary>
    public static readonly DependencyProperty UsesItemContainerTemplateProperty =
        DependencyProperty.Register(
            nameof(UsesItemContainerTemplate),
            typeof(bool),
            typeof(StatusBar),
            new PropertyMetadata(false, OnContainerTemplateChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the brush used for separators between items.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SeparatorBrush
    {
        get => (Brush?)GetValue(SeparatorBrushProperty);
        set => SetValue(SeparatorBrushProperty, value);
    }

    /// <summary>Gets or sets the selector used to create item containers.</summary>
    public ItemContainerTemplateSelector? ItemContainerTemplateSelector
    {
        get => (ItemContainerTemplateSelector?)GetValue(ItemContainerTemplateSelectorProperty);
        set => SetValue(ItemContainerTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets whether item-container templates are used.</summary>
    public bool UsesItemContainerTemplate
    {
        get => (bool)(GetValue(UsesItemContainerTemplateProperty) ?? false);
        set => SetValue(UsesItemContainerTemplateProperty, value);
    }

    /// <summary>Gets the resource key for the default separator style.</summary>
    public static ResourceKey SeparatorStyleKey => s_separatorStyleKey;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBar"/> class.
    /// </summary>
    public StatusBar()
    {
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
        if (UsesItemContainerTemplate)
        {
            DataTemplate? template = ItemContainerTemplateSelector?.SelectTemplate(item, this);
            object? generated = template?.LoadContent();
            if (generated is StatusBarItem or Separator)
            {
                return (FrameworkElement)generated;
            }

            if (generated != null)
            {
                throw new InvalidOperationException(
                    "An item-container template for a StatusBar must create a StatusBarItem or Separator.");
            }
        }

        return new StatusBarItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is StatusBarItem || item is Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is StatusBarItem statusBarItem && !ReferenceEquals(statusBarItem, item))
        {
            statusBarItem.Content = item;
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

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
            var y = borderPen.Thickness * 0.5;
            dc.DrawLine(borderPen, new Point(0, y), new Point(rect.Width, y));
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusBar statusBar)
        {
            statusBar.InvalidateVisual();
        }
    }

    private static void OnContainerTemplateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((StatusBar)dependencyObject).RefreshItems();
    }

    #endregion
}

/// <summary>
/// Represents an item in a StatusBar.
/// </summary>
public class StatusBarItem : ContentControl
{
    private UIElement? _contentVisual;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.StatusBarItemAutomationPeer(this);
    }

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBarItem"/> class.
    /// </summary>
    public StatusBarItem()
    {
        UseTemplateContentManagement();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        if (_contentVisual != null)
        {
            RemoveVisualChild(_contentVisual);
            _contentVisual = null;
        }

        if (newContent is UIElement element)
        {
            _contentVisual = element;
            AddVisualChild(element);
        }

        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => _contentVisual != null ? 1 : 0;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index == 0 && _contentVisual != null)
        {
            return _contentVisual;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;

        if (Content is string text)
        {
            var fontFamily = FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize > 0 ? FontSize : 12;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            Interop.TextMeasurement.MeasureText(formattedText);
            return new Size(
                formattedText.Width + padding.TotalWidth,
                Math.Max(24, formattedText.Height + padding.TotalHeight));
        }

        if (_contentVisual != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - padding.TotalWidth),
                Math.Max(0, availableSize.Height - padding.TotalHeight));
            _contentVisual.Measure(contentAvailable);
            return new Size(
                _contentVisual.DesiredSize.Width + padding.TotalWidth,
                Math.Max(24, _contentVisual.DesiredSize.Height + padding.TotalHeight));
        }

        return new Size(padding.TotalWidth, 24);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_contentVisual != null)
        {
            var padding = Padding;
            _contentVisual.Arrange(new Rect(
                padding.Left,
                padding.Top,
                Math.Max(0, finalSize.Width - padding.TotalWidth),
                Math.Max(0, finalSize.Height - padding.TotalHeight)));
        }

        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var padding = Padding;

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        if (Content is string text)
        {
            var fgBrush = ResolveForegroundBrush();
            var formattedText = new FormattedText(text, FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName, FontSize > 0 ? FontSize : 12)
            {
                Foreground = fgBrush
            };
            Interop.TextMeasurement.MeasureText(formattedText);

            var textX = padding.Left;
            var textY = (rect.Height - formattedText.Height) / 2;
            dc.DrawText(formattedText, new Point(textX, textY));
        }
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextSecondary") as Brush
            ?? Foreground
            ?? new SolidColorBrush(Color.White);
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
