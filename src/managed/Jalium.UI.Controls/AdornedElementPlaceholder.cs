namespace Jalium.UI.Controls;

/// <summary>
/// Represents the element used in a ControlTemplate to specify where a decorated control is placed
/// relative to other elements in the ControlTemplate. Used with Validation.ErrorTemplate.
/// </summary>
public sealed class AdornedElementPlaceholder : FrameworkElement
{
    /// <summary>
    /// Gets the UIElement that this AdornedElementPlaceholder is reserving space for.
    /// </summary>
    public UIElement? AdornedElement { get; internal set; }

    /// <summary>
    /// Gets or sets the single child element of this AdornedElementPlaceholder.
    /// </summary>
    public UIElement? Child { get; set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (AdornedElement != null)
        {
            return AdornedElement.RenderSize;
        }

        Child?.Measure(availableSize);
        return Child?.DesiredSize ?? default;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
