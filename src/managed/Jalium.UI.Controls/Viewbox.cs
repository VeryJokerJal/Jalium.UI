using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the direction that content is scaled.
/// </summary>
public enum StretchDirection
{
    /// <summary>
    /// The content scales upward only when it is smaller than the parent.
    /// If the content is larger, no scaling downward is performed.
    /// </summary>
    UpOnly,

    /// <summary>
    /// The content scales downward only when it is larger than the parent.
    /// If the content is smaller, no scaling upward is performed.
    /// </summary>
    DownOnly,

    /// <summary>
    /// The content scales to fit the parent according to the Stretch mode.
    /// </summary>
    Both,
}

/// <summary>
/// Defines a content decorator that can stretch and scale a single child to fill the available space.
/// </summary>
public class Viewbox : Decorator
{
    private readonly ViewboxVisualHost _internalVisual;
    private UIElement? _child;

    /// <summary>
    /// Initializes a new instance of the <see cref="Viewbox"/> class.
    /// </summary>
    public Viewbox()
    {
        _internalVisual = new ViewboxVisualHost();
        AddVisualChild(_internalVisual);
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ViewboxAutomationPeer(this);
    }

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(
            nameof(Stretch),
            typeof(Stretch),
            typeof(Viewbox),
            new FrameworkPropertyMetadata(Stretch.Uniform, FrameworkPropertyMetadataOptions.AffectsMeasure),
            IsValidStretch);

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(
            nameof(StretchDirection),
            typeof(StretchDirection),
            typeof(Viewbox),
            new FrameworkPropertyMetadata(StretchDirection.Both, FrameworkPropertyMetadataOptions.AffectsMeasure),
            IsValidStretchDirection);

    /// <summary>
    /// Gets or sets a value that describes how the content should be stretched to fill the allocated space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that determines how scaling is applied to the child.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    /// <summary>
    /// Gets or sets the single child of the Viewbox.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public override UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            UIElement? oldChild = _child;
            _child = null;
            if (oldChild != null)
            {
                _internalVisual.Child = null;
                RemoveLogicalChild(oldChild);
            }

            if (value != null)
            {
                try
                {
                    AddLogicalChild(value);
                    _child = value;
                    _internalVisual.Child = value;
                }
                catch
                {
                    _internalVisual.Child = null;
                    RemoveLogicalChild(value);
                    _child = null;
                    throw;
                }
            }

            InvalidateMeasure();
        }
    }

    /// <inheritdoc />
    protected internal override System.Collections.IEnumerator LogicalChildren =>
        _child == null
            ? Enumerable.Empty<object>().GetEnumerator()
            : new object[] { _child }.GetEnumerator();

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _internalVisual;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_child == null)
        {
            return default;
        }

        _internalVisual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size childSize = _child.DesiredSize;
        Size scale = ComputeScaleFactor(availableSize, childSize, Stretch, StretchDirection);
        return new Size(childSize.Width * scale.Width, childSize.Height * scale.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_child == null)
        {
            _internalVisual.Arrange(new Rect());
            return finalSize;
        }

        Size childSize = _child.DesiredSize;
        Size scale = ComputeScaleFactor(finalSize, childSize, Stretch, StretchDirection);
        _internalVisual.RenderTransformOrigin = new Point(0, 0);
        _internalVisual.RenderTransform = new ScaleTransform(scale.Width, scale.Height);
        _internalVisual.Arrange(new Rect(0, 0, childSize.Width, childSize.Height));

        return new Size(scale.Width * childSize.Width, scale.Height * childSize.Height);
    }

    internal static Size ComputeScaleFactor(
        Size availableSize,
        Size contentSize,
        Stretch stretch,
        StretchDirection stretchDirection)
    {
        double scaleX = 1.0;
        double scaleY = 1.0;
        bool isConstrainedWidth = !double.IsPositiveInfinity(availableSize.Width);
        bool isConstrainedHeight = !double.IsPositiveInfinity(availableSize.Height);

        if (stretch is Stretch.Uniform or Stretch.UniformToFill or Stretch.Fill &&
            (isConstrainedWidth || isConstrainedHeight))
        {
            scaleX = IsZero(contentSize.Width) ? 0 : availableSize.Width / contentSize.Width;
            scaleY = IsZero(contentSize.Height) ? 0 : availableSize.Height / contentSize.Height;

            if (!isConstrainedWidth)
            {
                scaleX = scaleY;
            }
            else if (!isConstrainedHeight)
            {
                scaleY = scaleX;
            }
            else if (stretch == Stretch.Uniform)
            {
                scaleX = scaleY = Math.Min(scaleX, scaleY);
            }
            else if (stretch == Stretch.UniformToFill)
            {
                scaleX = scaleY = Math.Max(scaleX, scaleY);
            }

            if (stretchDirection == StretchDirection.UpOnly)
            {
                scaleX = Math.Max(1, scaleX);
                scaleY = Math.Max(1, scaleY);
            }
            else if (stretchDirection == StretchDirection.DownOnly)
            {
                scaleX = Math.Min(1, scaleX);
                scaleY = Math.Min(1, scaleY);
            }
        }

        return new Size(scaleX, scaleY);
    }

    private static bool IsValidStretch(object? value) =>
        value is Stretch stretch &&
        stretch is Stretch.None or Stretch.Fill or Stretch.Uniform or Stretch.UniformToFill;

    private static bool IsValidStretchDirection(object? value) =>
        value is StretchDirection direction &&
        direction is StretchDirection.UpOnly or StretchDirection.DownOnly or StretchDirection.Both;

    private static bool IsZero(double value) => Math.Abs(value) < 1e-12;

    /// <summary>
    /// Current Jalium rendering and input matrices are UIElement-based. This private host provides
    /// WPF's hidden transform-container role without exposing another public content API or replacing
    /// the child's own RenderTransform.
    /// </summary>
    private sealed class ViewboxVisualHost : FrameworkElement
    {
        private UIElement? _child;

        internal UIElement? Child
        {
            get => _child;
            set
            {
                if (ReferenceEquals(_child, value))
                {
                    return;
                }

                UIElement? oldChild = _child;
                _child = null;
                if (oldChild != null)
                {
                    RemoveVisualChild(oldChild);
                }

                _child = value;
                if (value != null)
                {
                    try
                    {
                        AddVisualChild(value);
                    }
                    catch
                    {
                        _child = null;
                        throw;
                    }
                }

                InvalidateMeasure();
            }
        }

        protected override int VisualChildrenCount => _child == null ? 0 : 1;

        protected override Visual? GetVisualChild(int index)
        {
            if (_child == null || index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _child;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_child == null)
            {
                return default;
            }

            _child.Measure(availableSize);
            return _child.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (_child != null)
            {
                _child.Arrange(new Rect(0, 0, _child.DesiredSize.Width, _child.DesiredSize.Height));
            }

            return finalSize;
        }
    }
}
