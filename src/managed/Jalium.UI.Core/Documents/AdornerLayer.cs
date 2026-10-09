using System.Collections.ObjectModel;
using Jalium.UI.Media;

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

    /// <summary>Hit-tests the adorner layer and identifies the topmost adorner at a point.</summary>
    public AdornerHitTestResult? AdornerHitTest(Point point)
    {
        for (var index = _adorners.Count - 1; index >= 0; index--)
        {
            var adorner = _adorners[index].Adorner;
            var hit = adorner.HitTest(point);
            if (hit?.VisualHit is Visual visual)
            {
                return new AdornerHitTestResult(visual, point, adorner);
            }
        }

        return null;
    }

    /// <inheritdoc />
    protected internal override System.Collections.IEnumerator LogicalChildren =>
        _adorners.ConvertAll(static info => info.Adorner).GetEnumerator();

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

    internal void InvalidateAdornerPositions(UIElement ancestor)
    {
        if (_adorners.Any(info => IsAncestorOrSelf(ancestor, info.Adorner.AdornedElement)))
        {
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets the number of visual children.
    /// </summary>
    protected override int VisualChildrenCount => _adorners.Count;

    /// <summary>
    /// Gets the visual child at the specified index.
    /// </summary>
    protected override Visual? GetVisualChild(int index)
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
        PruneOrphanedAdorners();

        foreach (var info in _adorners)
        {
            info.Adorner.Measure(constraint);
        }

        // AdornerLayer doesn't consume any space
        return default(Size);
    }

    /// <summary>
    /// Arranges the adorner layer.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        PruneOrphanedAdorners();

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
    /// Drops adorners whose adorned element has left the subtree this layer serves — removed
    /// together with its page, replaced by a template rebuild, recycled by a virtualizing panel
    /// (WPF <c>AdornerLayer.UpdateAdorner</c> semantics). An adorner is positioned from its
    /// element's bounds, so one that outlives its element keeps painting at the element's last
    /// location; running the sweep from layout makes it vanish in the same frame the element
    /// does instead of whenever its owner next notices. Deliberately does not invalidate layout:
    /// it runs from inside Measure/Arrange.
    /// </summary>
    private void PruneOrphanedAdorners()
    {
        if (_adorners.Count == 0)
            return;

        var host = GetHost();
        if (host is null)
            return;

        for (var index = _adorners.Count - 1; index >= 0; index--)
        {
            var adorner = _adorners[index].Adorner;
            if (IsAncestorOrSelf(host, adorner.AdornedElement))
                continue;

            _adorners.RemoveAt(index);
            RemoveVisualChild(adorner);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The element whose subtree this layer adorns: the nearest ancestor that hands this very
    /// layer out (<see cref="IAdornerLayerHost"/> or <see cref="AdornerDecorator"/>), falling
    /// back to the visual root for a layer parented some other way. Null while the layer itself
    /// is detached — nothing is pruned then; the sweep resumes once it is back in a tree.
    /// </summary>
    private Visual? GetHost()
    {
        Visual? top = null;
        var depth = 0;
        for (Visual? current = InternalVisualParent; current is not null && depth++ < MaxTreeDepth; current = current.VisualParent)
        {
            if (current is IAdornerLayerHost host && ReferenceEquals(host.AdornerLayer, this))
                return current;

            if (current is AdornerDecorator decorator && ReferenceEquals(decorator.AdornerLayer, this))
                return current;

            top = current;
        }

        return top;
    }

    private static bool IsAncestorOrSelf(Visual ancestor, Visual element)
    {
        var depth = 0;
        for (Visual? current = element; current is not null && depth++ < MaxTreeDepth; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private const int MaxTreeDepth = 4096;

    /// <summary>
    /// Renders the adorner layer.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        // AdornerLayer itself doesn't render anything
        // The adorners render themselves
    }

    private Point GetAdornedElementPosition(UIElement element)
    {
        return element.TranslatePoint(new Point(0, 0), this);
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
