using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Abstract class that represents an element that decorates a UIElement.
/// </summary>
public abstract class Adorner : FrameworkElement
{
    /// <summary>
    /// Gets the UIElement that this adorner is bound to.
    /// </summary>
    public UIElement AdornedElement { get; }

    /// <summary>
    /// Gets or sets a value that indicates whether hit testing is enabled on this adorner.
    /// </summary>
    public bool IsClipEnabled { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Adorner"/> class.
    /// </summary>
    /// <param name="adornedElement">The element to bind the adorner to.</param>
    protected Adorner(UIElement adornedElement)
    {
        ArgumentNullException.ThrowIfNull(adornedElement);
        AdornedElement = adornedElement;
    }

    /// <summary>
    /// Returns a Transform for the adorner, based on the transform that is currently applied
    /// to the adorned element.
    /// </summary>
    public virtual GeneralTransform? GetDesiredTransform(GeneralTransform? transform)
    {
        return transform;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        // By default, size to the adorned element
        return AdornedElement.RenderSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }
}

/// <summary>
/// Represents a surface for rendering adorners.
/// </summary>
public sealed class AdornerLayer : FrameworkElement
{
    private readonly List<AdornerInfo> _adorners = new();

    /// <summary>
    /// Adds an adorner to the adorner layer.
    /// </summary>
    public void Add(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);

        _adorners.Add(new AdornerInfo(adorner));
        AddVisualChild(adorner);
        InvalidateMeasure();
    }

    /// <summary>
    /// Removes the specified adorner from the adorner layer.
    /// </summary>
    public void Remove(Adorner adorner)
    {
        ArgumentNullException.ThrowIfNull(adorner);

        for (int i = _adorners.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_adorners[i].Adorner, adorner))
            {
                RemoveVisualChild(adorner);
                _adorners.RemoveAt(i);
                InvalidateMeasure();
                return;
            }
        }
    }

    /// <summary>
    /// Returns an array of adorners that are bound to the specified element.
    /// </summary>
    public Adorner[]? GetAdorners(UIElement element)
    {
        var result = new List<Adorner>();
        foreach (var info in _adorners)
        {
            if (ReferenceEquals(info.Adorner.AdornedElement, element))
            {
                result.Add(info.Adorner);
            }
        }
        return result.Count > 0 ? result.ToArray() : null;
    }

    /// <summary>
    /// Returns the first AdornerLayer in the visual tree above a specified visual.
    /// </summary>
    public static AdornerLayer? GetAdornerLayer(Visual visual)
    {
        var current = visual.VisualParent;
        while (current != null)
        {
            if (current is AdornerDecorator decorator)
                return decorator.AdornerLayer;

            current = current.VisualParent;
        }
        return null;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        foreach (var info in _adorners)
        {
            info.Adorner.Measure(constraint);
        }
        return constraint;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var info in _adorners)
        {
            var adorner = info.Adorner;
            var element = adorner.AdornedElement;

            // Position the adorner relative to its adorned element
            var transform = element.TransformToVisual(this);
            var origin = transform?.Transform(new Point(0, 0)) ?? new Point(0, 0);
            var elementSize = element.RenderSize;

            adorner.Arrange(new Rect(origin, elementSize));
        }
        return finalSize;
    }

    /// <inheritdoc />
    public override int VisualChildrenCount => _adorners.Count;

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (index < 0 || index >= _adorners.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _adorners[index].Adorner;
    }

    private record struct AdornerInfo(Adorner Adorner);
}

/// <summary>
/// Provides an adorner layer for child elements.
/// </summary>
[ContentProperty("Child")]
public sealed class AdornerDecorator : FrameworkElement
{
    private UIElement? _child;

    /// <summary>
    /// Gets the AdornerLayer associated with this AdornerDecorator.
    /// </summary>
    public AdornerLayer AdornerLayer { get; }

    /// <summary>
    /// Gets or sets the single child of the AdornerDecorator.
    /// </summary>
    public UIElement? Child
    {
        get => _child;
        set
        {
            if (_child != value)
            {
                if (_child != null)
                    RemoveVisualChild(_child);

                _child = value;

                if (_child != null)
                    AddVisualChild(_child);

                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AdornerDecorator"/> class.
    /// </summary>
    public AdornerDecorator()
    {
        AdornerLayer = new AdornerLayer();
        AddVisualChild(AdornerLayer);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var desiredSize = Size.Empty;

        if (_child != null)
        {
            _child.Measure(constraint);
            desiredSize = _child.DesiredSize;
        }

        AdornerLayer.Measure(constraint);

        return desiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _child?.Arrange(new Rect(finalSize));
        AdornerLayer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            int count = 1; // AdornerLayer is always present
            if (_child != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        if (_child != null)
        {
            return index switch
            {
                0 => _child,
                1 => AdornerLayer,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        if (index == 0) return AdornerLayer;
        throw new ArgumentOutOfRangeException(nameof(index));
    }
}

/// <summary>
/// Adorner that displays a red border around elements with validation errors.
/// </summary>
internal sealed class ValidationErrorAdorner : Adorner
{
    private static readonly Pen _errorPen = new(new SolidColorBrush(Color.FromRgb(255, 0, 0)), 1.5);

    public ValidationErrorAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        var rect = new Rect(AdornedElement.RenderSize);
        dc.DrawRectangle(null, _errorPen, rect);
    }
}

/// <summary>
/// Registers the validation adorner handler. Called during application startup
/// to connect the base Validation class with the adorner infrastructure.
/// </summary>
/// <summary>
/// Represents the result of a hit test on an adorner layer.
/// Provides information about which adorner was hit in addition to the visual and point data.
/// </summary>
public sealed class AdornerHitTestResult : Jalium.UI.Media.PointHitTestResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AdornerHitTestResult"/> class.
    /// </summary>
    /// <param name="visual">The visual that was hit.</param>
    /// <param name="pt">The point that was hit, in the visual's coordinate space.</param>
    /// <param name="adorner">The adorner that was hit.</param>
    internal AdornerHitTestResult(Visual visual, Point pt, Adorner adorner)
        : base(visual, pt)
    {
        Adorner = adorner;
    }

    /// <summary>
    /// Gets the adorner that was hit.
    /// </summary>
    public Adorner Adorner { get; }
}

/// <summary>
/// Registers the validation adorner handler. Called during application startup
/// to connect the base Validation class with the adorner infrastructure.
/// </summary>
internal static class ValidationAdornerIntegration
{
    private static readonly Dictionary<DependencyObject, ValidationErrorAdorner> _adorners = new();

    internal static void Initialize()
    {
        Validation.AdornerHandler = HandleValidationAdorner;
    }

    private static void HandleValidationAdorner(DependencyObject element, bool hasError)
    {
        if (element is not UIElement uiElement) return;

        if (hasError)
        {
            if (_adorners.ContainsKey(element)) return;

            var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
            if (adornerLayer != null)
            {
                var adorner = new ValidationErrorAdorner(uiElement);
                adornerLayer.Add(adorner);
                _adorners[element] = adorner;
            }
        }
        else
        {
            if (_adorners.TryGetValue(element, out var adorner))
            {
                var adornerLayer = AdornerLayer.GetAdornerLayer(uiElement);
                adornerLayer?.Remove(adorner);
                _adorners.Remove(element);
            }
        }
    }
}
