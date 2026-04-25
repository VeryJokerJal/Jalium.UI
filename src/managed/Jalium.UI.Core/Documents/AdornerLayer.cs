using System.Collections.ObjectModel;

namespace Jalium.UI.Documents;

/// <summary>
/// Represents a surface for rendering adorners.
/// </summary>
public sealed class AdornerLayer : FrameworkElement
{
    private readonly List<AdornerInfo> _adorners = new();

    /// <summary>
    /// Initializes a new instance of the AdornerLayer class.
    /// </summary>
    public AdornerLayer()
    {
        // AdornerLayer should not participate in hit testing for normal scenarios
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Returns the first adorner layer in the visual tree above a specified Visual.
    /// </summary>
    /// <param name="visual">The Visual from which to find an adorner layer.</param>
    /// <returns>An AdornerLayer for the specified visual, or null if no adorner layer can be found.</returns>
    public static AdornerLayer? GetAdornerLayer(Visual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);

        Visual? current = visual;
        while (current != null)
        {
            // Any ancestor that exposes an adorner layer wins — Window, custom host, or AdornerDecorator.
            if (current is IAdornerLayerHost host && host.AdornerLayer != null)
            {
                return host.AdornerLayer;
            }

            if (current is AdornerDecorator decoratorSelf)
            {
                return decoratorSelf.AdornerLayer;
            }

            if (current.VisualParent is AdornerDecorator decoratorParent)
            {
                return decoratorParent.AdornerLayer;
            }

            current = current.VisualParent;
        }

        return null;
    }

    /// <summary>
    /// Adds an adorner to the adorner layer.
    /// </summary>
    /// <param name="adorner">The adorner to add.</param>
    public void Add(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);

        var info = new AdornerInfo(adorner);
        _adorners.Add(info);

        // Add to visual tree
        AddVisualChild(adorner);

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Removes the specified adorner from the adorner layer.
    /// </summary>
    /// <param name="adorner">The adorner to remove.</param>
    public void Remove(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);

        for (int i = _adorners.Count - 1; i >= 0; i--)
        {
            if (_adorners[i].Adorner == adorner)
            {
                _adorners.RemoveAt(i);
                RemoveVisualChild(adorner);
                InvalidateMeasure();
                InvalidateVisual();
                return;
            }
        }
    }

    /// <summary>
    /// Returns an array of adorners that are bound to the specified UIElement.
    /// </summary>
    /// <param name="element">The UIElement to retrieve an array of adorners for.</param>
    /// <returns>An array of adorners that decorate the specified UIElement, or null if there are no adorners bound to the specified element.</returns>
    public Adorner[]? GetAdorners(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var result = new List<Adorner>();
        foreach (var info in _adorners)
        {
            if (info.Adorner.AdornedElement == element)
            {
                result.Add(info.Adorner);
            }
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    /// <summary>
    /// Causes the adorner layer to re-render all adorners.
    /// </summary>
    public void Update()
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    /// <summary>
    /// Causes the adorner layer to re-render the adorner for the specified element.
    /// </summary>
    /// <param name="element">The UIElement to update adorners for.</param>
    public void Update(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        foreach (var info in _adorners)
        {
            if (info.Adorner.AdornedElement == element)
            {
                info.Adorner.InvalidateMeasure();
                info.Adorner.InvalidateArrange();
                info.Adorner.InvalidateVisual();
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Gets the number of visual children.
    /// </summary>
    public override int VisualChildrenCount => _adorners.Count;

    /// <summary>
    /// Gets the visual child at the specified index.
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        if (index < 0 || index >= _adorners.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _adorners[index].Adorner;
    }

    /// <summary>
    /// Measures the adorner layer.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        foreach (var info in _adorners)
        {
            info.Adorner.Measure(constraint);
        }

        // AdornerLayer doesn't consume any space
        return Size.Empty;
    }

    /// <summary>
    /// Arranges the adorner layer.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var info in _adorners)
        {
            var adorner = info.Adorner;
            var adornedElement = adorner.AdornedElement;

            // Calculate the position of the adorned element relative to the adorner layer
            var position = GetAdornedElementPosition(adornedElement);

            // Arrange the adorner at the same position and size as the adorned element
            var adornerRect = new Rect(position, adornedElement.RenderSize);
            adorner.Arrange(adornerRect);
        }

        return finalSize;
    }

    /// <summary>
    /// Renders the adorner layer.
    /// </summary>
    protected override void OnRender(object drawingContext)
    {
        // AdornerLayer itself doesn't render anything
        // The adorners render themselves
    }

    private Point GetAdornedElementPosition(UIElement element)
    {
        // Walk up the visual tree to accumulate offsets
        double x = 0, y = 0;
        Visual? current = element;

        while (current != null && current != this && current.VisualParent != null)
        {
            if (current is UIElement uiElement)
            {
                var bounds = uiElement.VisualBounds;
                x += bounds.X;
                y += bounds.Y;
            }

            if (current.VisualParent == this.VisualParent)
                break;

            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    private sealed class AdornerInfo
    {
        public Adorner Adorner { get; }

        public AdornerInfo(Adorner adorner)
        {
            Adorner = adorner;
        }
    }
}
