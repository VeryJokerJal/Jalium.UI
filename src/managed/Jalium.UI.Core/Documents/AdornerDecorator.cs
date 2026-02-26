namespace Jalium.UI.Documents;

/// <summary>
/// Provides an AdornerLayer for elements below it in the visual tree.
/// </summary>
public sealed class AdornerDecorator : Decorator
{
    private readonly AdornerLayer _adornerLayer;

    /// <summary>
    /// Initializes a new instance of the AdornerDecorator class.
    /// </summary>
    public AdornerDecorator()
    {
        _adornerLayer = new AdornerLayer();
        AddVisualChild(_adornerLayer);
    }

    /// <summary>
    /// Gets the AdornerLayer associated with this AdornerDecorator.
    /// </summary>
    public AdornerLayer AdornerLayer => _adornerLayer;

    /// <summary>
    /// Gets the number of visual children.
    /// </summary>
    public override int VisualChildrenCount
    {
        get
        {
            // Child + AdornerLayer
            return Child != null ? 2 : 1;
        }
    }

    /// <summary>
    /// Gets the visual child at the specified index.
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        if (Child != null)
        {
            return index switch
            {
                0 => Child,
                1 => _adornerLayer,
                _ => throw new ArgumentOutOfRangeException(nameof(index))
            };
        }

        return index == 0 ? _adornerLayer : throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Measures the decorator and its children.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var desiredSize = Size.Empty;

        if (Child != null)
        {
            Child.Measure(constraint);
            desiredSize = Child.DesiredSize;
        }

        // Measure the adorner layer
        _adornerLayer.Measure(constraint);

        return desiredSize;
    }

    /// <summary>
    /// Arranges the decorator and its children.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var childRect = new Rect(0, 0, finalSize.Width, finalSize.Height);

        if (Child != null)
        {
            Child.Arrange(childRect);
        }

        // The adorner layer covers the same area
        _adornerLayer.Arrange(childRect);

        return finalSize;
    }
}

/// <summary>
/// Base class for elements that apply effects around a single child element.
/// </summary>
public class Decorator : FrameworkElement
{
    private UIElement? _child;

    /// <summary>
    /// Identifies the Child property.
    /// </summary>
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(UIElement), typeof(Decorator),
            new PropertyMetadata(null, OnChildChanged));

    /// <summary>
    /// Gets or sets the single child element of a Decorator.
    /// </summary>
    public virtual UIElement? Child
    {
        get => _child;
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets the number of visual children.
    /// </summary>
    public override int VisualChildrenCount => _child != null ? 1 : 0;

    /// <summary>
    /// Gets the visual child at the specified index.
    /// </summary>
    public override Visual? GetVisualChild(int index)
    {
        if (_child == null || index != 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _child;
    }

    /// <summary>
    /// Measures the decorator and its child.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (_child != null)
        {
            _child.Measure(constraint);
            return _child.DesiredSize;
        }

        return Size.Empty;
    }

    /// <summary>
    /// Arranges the decorator and its child.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_child != null)
        {
            _child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    private static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Decorator decorator)
        {
            var oldChild = e.OldValue as UIElement;
            var newChild = e.NewValue as UIElement;

            if (oldChild != null)
            {
                decorator.RemoveVisualChild(oldChild);
            }

            decorator._child = newChild;

            if (newChild != null)
            {
                decorator.AddVisualChild(newChild);
            }

            decorator.InvalidateMeasure();
        }
    }
}
