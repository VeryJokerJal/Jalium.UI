using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Abstract class that represents a FrameworkElement that decorates a UIElement.
/// </summary>
public abstract class Adorner : FrameworkElement
{
    private readonly UIElement _adornedElement;

    /// <summary>
    /// Initializes a new instance of the Adorner class.
    /// </summary>
    /// <param name="adornedElement">The element to which this adorner is bound.</param>
    protected Adorner(UIElement adornedElement)
    {
        _adornedElement = adornedElement ?? throw new ArgumentNullException(nameof(adornedElement));
    }

    /// <summary>
    /// Gets the UIElement that this adorner is bound to.
    /// </summary>
    public UIElement AdornedElement => _adornedElement;

    /// <summary>
    /// Gets or sets a value that indicates whether clipping of the adorner is enabled.
    /// </summary>
    public bool IsClipEnabled { get; set; }

    /// <summary>
    /// Returns a Transform for the adorner, based on the transform that is currently applied to the adorned element.
    /// </summary>
    /// <param name="transform">The transform that is currently applied to the adorned element.</param>
    /// <returns>A transform to apply to the adorner.</returns>
    public virtual Jalium.UI.Media.GeneralTransform? GetDesiredTransform(Jalium.UI.Media.GeneralTransform? transform)
    {
        return transform;
    }

    /// <summary>
    /// Implements any custom measuring behavior for the adorner.
    /// </summary>
    /// <param name="constraint">A size to constrain the adorner to.</param>
    /// <returns>A Size object representing the amount of layout space needed by the adorner.</returns>
    protected override Size MeasureOverride(Size constraint)
    {
        // By default, adorners size to their adorned element
        return _adornedElement.RenderSize;
    }

    /// <summary>
    /// Positions child elements and determines a size for the adorner.
    /// </summary>
    /// <param name="finalSize">The final area within the parent that the adorner should use to arrange itself and its child elements.</param>
    /// <returns>The actual size used by the adorner.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    /// <summary>
    /// Gets the layout clip for this adorner.
    /// </summary>
    /// <returns>The clipping geometry, or null if clipping is not enabled.</returns>
    internal override Geometry? GetLayoutClip()
    {
        if (IsClipEnabled)
        {
            return new RectangleGeometry(new Rect(0, 0, RenderSize.Width, RenderSize.Height));
        }
        return null;
    }
}
