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
    private readonly Style _focusVisualStyle;

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
        _focusVisualStyle = focusVisualStyle;

        // Do not capture input — the adorner is purely visual.
        IsHitTestVisible = false;
        Focusable = false;

        _host = new FocusVisualHost
        {
            IsHitTestVisible = false,
            Focusable = false,
        };

        // Forward layout properties that the focus visual template typically needs to mirror
        // the adorned element (CornerRadius for rounded buttons). Written as a current value,
        // not a local one: a local value outranks every style setter, so a focus visual style
        // that sets its own CornerRadius would silently lose to the mirror. As a current value
        // it fills in only when the style stays silent, and TemplateBinding still sees it.
        if (adornedElement is Control control)
        {
            _host.SetCurrentValue(Control.CornerRadiusProperty, control.CornerRadius);
        }

        _host.Style = focusVisualStyle;

        AddVisualChild(_host);
    }

    /// <summary>
    /// Gets the hosted control that materializes the focus visual's template.
    /// </summary>
    internal FocusVisualHost Host => _host;

    /// <summary>
    /// Gets the style this indicator was built from, so the manager can tell whether a later
    /// change of the adorned element's effective focus visual style requires a rebuild.
    /// </summary>
    internal Style FocusVisualStyle => _focusVisualStyle;

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
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

    internal override Geometry? GetLayoutClip()
    {
        Rect? visible = null;
        // The layer is outside the adorned element's subtree. Reapply its ancestor
        // clips without clipping the focus style's intentional outward border.
        for (Visual? current = AdornedElement.VisualParent; current != null; current = current.VisualParent)
        {
            if (current is UIElement ancestor)
            {
                IntersectClip(ancestor, ancestor.GetLayoutClip(), ref visible);
                IntersectClip(ancestor, ancestor.GetChildLayoutClip(), ref visible);
            }
        }
        return visible is Rect bounds ? new RectangleGeometry(bounds.IsEmpty ? new Rect(0, 0, 0, 0) : bounds) : base.GetLayoutClip();
    }

    private void IntersectClip(UIElement ancestor, Geometry? geometry, ref Rect? visible)
    {
        if (geometry == null) return;
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            visible = new Rect(0, 0, 0, 0);
            return;
        }
        var topLeft = ancestor.TranslatePoint(new Point(bounds.Left, bounds.Top), this);
        var topRight = ancestor.TranslatePoint(new Point(bounds.Right, bounds.Top), this);
        var bottomLeft = ancestor.TranslatePoint(new Point(bounds.Left, bounds.Bottom), this);
        var bottomRight = ancestor.TranslatePoint(new Point(bounds.Right, bounds.Bottom), this);
        double left = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        double top = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        double right = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        double bottom = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        var clip = new Rect(left, top, right - left, bottom - top);
        visible = visible is Rect previous ? Rect.Intersect(previous, clip) : clip;
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
