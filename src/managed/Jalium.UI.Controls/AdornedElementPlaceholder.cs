namespace Jalium.UI.Controls;

/// <summary>
/// Represents the element used in a ControlTemplate to specify where a decorated control is placed
/// relative to other elements in the ControlTemplate. Used with Validation.ErrorTemplate.
/// </summary>
public class AdornedElementPlaceholder : FrameworkElement, Jalium.UI.Markup.IAddChild
{
    private UIElement? _child;

    /// <summary>
    /// Gets the UIElement that this AdornedElementPlaceholder is reserving space for.
    /// </summary>
    public UIElement? AdornedElement { get; internal set; }

    /// <summary>
    /// Gets or sets the single child element of this AdornedElementPlaceholder.
    /// </summary>
    [System.ComponentModel.DefaultValue(null)]
    public virtual UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            if (_child != null)
            {
                RemoveVisualChild(_child);
                RemoveLogicalChild(_child);
            }

            _child = value;
            if (_child != null)
            {
                AddLogicalChild(_child);
                AddVisualChild(_child);
            }

            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected override int VisualChildrenCount => _child == null ? 0 : 1;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0 || _child == null)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _child;
    }

    /// <inheritdoc />
    protected internal override System.Collections.IEnumerator LogicalChildren =>
        _child == null
            ? Enumerable.Empty<object>().GetEnumerator()
            : new object[] { _child }.GetEnumerator();

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

    void Jalium.UI.Markup.IAddChild.AddChild(object value)
    {
        if (value is not UIElement element)
        {
            throw new ArgumentException(
                $"AdornedElementPlaceholder accepts only {nameof(UIElement)} children.",
                nameof(value));
        }

        if (Child != null)
        {
            throw new ArgumentException(
                "AdornedElementPlaceholder can accept only one child.",
                nameof(value));
        }

        Child = element;
    }

    void Jalium.UI.Markup.IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Non-whitespace text is not valid in AdornedElementPlaceholder.",
                nameof(text));
        }
    }
}
