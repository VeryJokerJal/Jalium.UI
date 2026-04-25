using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// An <see cref="Adorner"/> that draws a keyboard focus indicator over an element.
/// The visual is produced by instantiating the <see cref="Style"/> supplied as
/// <c>FocusVisualStyle</c>, which means the focus visual lives in a separate visual tree
/// (the adorner layer) and does not participate in the adorned element's own template or
/// layout.
/// </summary>
public sealed class FocusVisualAdorner : Adorner
{
    private readonly FocusVisualHost _host;

    /// <summary>
    /// Initializes a new <see cref="FocusVisualAdorner"/> for the given element, using
    /// the supplied style to build the indicator's visual tree.
    /// </summary>
    /// <param name="adornedElement">The element whose focus state this adorner visualizes.</param>
    /// <param name="focusVisualStyle">The style describing the focus indicator. May supply a
    /// <see cref="ControlTemplate"/> through its setters, along with appearance properties.</param>
    public FocusVisualAdorner(UIElement adornedElement, Style focusVisualStyle)
        : base(adornedElement)
    {
        ArgumentNullException.ThrowIfNull(focusVisualStyle);

        // Do not capture input — the adorner is purely visual.
        IsHitTestVisible = false;
        Focusable = false;

        _host = new FocusVisualHost
        {
            IsHitTestVisible = false,
            Focusable = false,
        };

        // Forward layout properties that the focus visual template typically needs to mirror
        // the adorned element (CornerRadius for rounded buttons, Padding for offset indicators).
        // These are set before Style assignment so TemplateBinding picks them up cleanly.
        if (adornedElement is Control control)
        {
            _host.CornerRadius = control.CornerRadius;
        }

        _host.Style = focusVisualStyle;

        AddVisualChild(_host);
    }

    /// <summary>
    /// Gets the hosted control that materializes the focus visual's template.
    /// </summary>
    internal FocusVisualHost Host => _host;

    /// <inheritdoc />
    public override int VisualChildrenCount => 1;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _host;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        // Follow the adorned element's layout size, just like the retired inline FocusBorder.
        var desired = AdornedElement.RenderSize;
        _host.Measure(desired);
        return desired;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _host.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    /// <summary>
    /// Minimal <see cref="Control"/> subclass used to host the focus visual's template.
    /// Declaring a dedicated type means per-control-type focus visual styles are unnecessary:
    /// every focus visual style targets <see cref="FocusVisualHost"/>.
    /// </summary>
    internal sealed class FocusVisualHost : Control
    {
    }
}
