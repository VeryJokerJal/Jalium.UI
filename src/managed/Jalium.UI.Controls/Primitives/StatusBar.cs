using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that displays items and information in a horizontal bar.
/// </summary>
[StyleTypedProperty(Property = nameof(ItemContainerStyle), StyleTargetType = typeof(StatusBarItem))]
public class StatusBar : ItemsControl
{
    private static readonly ResourceKey s_separatorStyleKey =
        new ComponentResourceKey(typeof(StatusBar), nameof(SeparatorStyleKey));

    /// <summary>
    /// Identifies the <see cref="ItemContainerTemplateSelector"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemContainerTemplateSelectorProperty =
        MenuBase.ItemContainerTemplateSelectorProperty.AddOwner(
            typeof(StatusBar),
            new FrameworkPropertyMetadata(
                MenuBase.ItemContainerTemplateSelectorProperty.GetMetadata(typeof(MenuBase)).DefaultValue,
                OnContainerTemplateChanged));

    /// <summary>
    /// Identifies the <see cref="UsesItemContainerTemplate"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty UsesItemContainerTemplateProperty =
        MenuBase.UsesItemContainerTemplateProperty.AddOwner(
            typeof(StatusBar),
            new FrameworkPropertyMetadata(false, OnContainerTemplateChanged));

    /// <summary>
    /// Gets or sets the selector used to create item containers.
    /// </summary>
    public ItemContainerTemplateSelector? ItemContainerTemplateSelector
    {
        get => (ItemContainerTemplateSelector?)GetValue(ItemContainerTemplateSelectorProperty);
        set => SetValue(ItemContainerTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether item-container templates are used.
    /// </summary>
    public bool UsesItemContainerTemplate
    {
        get => (bool)(GetValue(UsesItemContainerTemplateProperty) ?? false);
        set => SetValue(UsesItemContainerTemplateProperty, value);
    }

    /// <summary>
    /// Gets the resource key for the default separator style.
    /// </summary>
    public static ResourceKey SeparatorStyleKey => s_separatorStyleKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusBar"/> class.
    /// </summary>
    public StatusBar()
    {
        IsTabStop = false;
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.StatusBarAutomationPeer(this);
    }

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
        return item is StatusBarItem or Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is StatusBarItem statusBarItem && !ReferenceEquals(statusBarItem, item))
        {
            statusBarItem.Content = item;
        }
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var rect = new Rect(RenderSize);

        if (Background != null)
        {
            drawingContext.DrawRectangle(Background, null, rect);
        }

        if (BorderBrush != null && BorderThickness.Top > 0)
        {
            var borderPen = new Pen(BorderBrush, BorderThickness.Top);
            var y = borderPen.Thickness * 0.5;
            drawingContext.DrawLine(borderPen, new Point(0, y), new Point(rect.Width, y));
        }
    }

    private static void OnContainerTemplateChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        ((StatusBar)dependencyObject).RefreshItems();
    }
}

/// <summary>
/// Arranges status-bar items horizontally.
/// </summary>
internal sealed class StatusBarPanel : StackPanel
{
    public StatusBarPanel()
    {
        Orientation = Orientation.Horizontal;
    }
}
