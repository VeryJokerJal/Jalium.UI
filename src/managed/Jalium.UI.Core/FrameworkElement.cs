namespace Jalium.UI;

/// <summary>
/// Provides a framework-level set of properties, events, and methods for UI elements.
/// </summary>
public class FrameworkElement : UIElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MinHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register(nameof(MinHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxHeightProperty =
        DependencyProperty.Register(nameof(MaxHeight), typeof(double), typeof(FrameworkElement),
            new PropertyMetadata(double.PositiveInfinity, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Margin dependency property.
    /// </summary>
    public static readonly DependencyProperty MarginProperty =
        DependencyProperty.Register(nameof(Margin), typeof(Thickness), typeof(FrameworkElement),
            new PropertyMetadata(new Thickness(0), OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the HorizontalAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalAlignmentProperty =
        DependencyProperty.Register(nameof(HorizontalAlignment), typeof(HorizontalAlignment), typeof(FrameworkElement),
            new PropertyMetadata(HorizontalAlignment.Stretch, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the VerticalAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalAlignmentProperty =
        DependencyProperty.Register(nameof(VerticalAlignment), typeof(VerticalAlignment), typeof(FrameworkElement),
            new PropertyMetadata(VerticalAlignment.Stretch, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the DataContext dependency property.
    /// </summary>
    public static readonly DependencyProperty DataContextProperty =
        DependencyProperty.Register(nameof(DataContext), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null, OnDataContextChanged));

    /// <summary>
    /// Identifies the Name dependency property.
    /// </summary>
    public static readonly DependencyProperty NameProperty =
        DependencyProperty.Register(nameof(Name), typeof(string), typeof(FrameworkElement),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Tag dependency property.
    /// </summary>
    public static readonly DependencyProperty TagProperty =
        DependencyProperty.Register(nameof(Tag), typeof(object), typeof(FrameworkElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Style dependency property.
    /// </summary>
    public static readonly DependencyProperty StyleProperty =
        DependencyProperty.Register(nameof(Style), typeof(Style), typeof(FrameworkElement),
            new PropertyMetadata(null, OnStyleChanged));

    /// <summary>
    /// Identifies the Cursor dependency property.
    /// </summary>
    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.Register(nameof(Cursor), typeof(Cursor), typeof(FrameworkElement),
            new PropertyMetadata(null, null, null, inherits: true));

    #endregion

    #region Internal Fields

    /// <summary>
    /// Stores original property values before style application.
    /// Used internally by the style system.
    /// </summary>
    internal readonly Dictionary<DependencyProperty, object?> _styleOriginalValues = new();

    /// <summary>
    /// The implicit style applied to this element based on its type.
    /// </summary>
    private Style? _implicitStyle;

    /// <summary>
    /// The element that owns the template in which this element is defined.
    /// </summary>
    private FrameworkElement? _templatedParent;

    /// <summary>
    /// Named elements registered in this element's scope (when it's a template root).
    /// </summary>
    private Dictionary<string, FrameworkElement>? _namedElements;

    #endregion

    #region Template Properties

    /// <summary>
    /// Gets the element that owns the template in which this element is defined.
    /// </summary>
    public FrameworkElement? TemplatedParent => _templatedParent;

    /// <summary>
    /// Sets the templated parent. This is called internally when applying templates.
    /// </summary>
    internal void SetTemplatedParent(FrameworkElement? parent)
    {
        _templatedParent = parent;
    }

    /// <summary>
    /// Registers a named element in this element's template scope.
    /// </summary>
    /// <param name="name">The name of the element.</param>
    /// <param name="element">The element to register.</param>
    public void RegisterName(string name, FrameworkElement element)
    {
        _namedElements ??= new Dictionary<string, FrameworkElement>();
        _namedElements[name] = element;
    }

    /// <summary>
    /// Unregisters a named element from this element's template scope.
    /// </summary>
    /// <param name="name">The name to unregister.</param>
    public void UnregisterName(string name)
    {
        _namedElements?.Remove(name);
    }

    /// <summary>
    /// Finds a named element in this element's template scope.
    /// </summary>
    /// <param name="name">The name of the element to find.</param>
    /// <returns>The element, or null if not found.</returns>
    public object? FindName(string name)
    {
        if (_namedElements != null && _namedElements.TryGetValue(name, out var element))
        {
            return element;
        }

        // Try parent's scope
        return (VisualParent as FrameworkElement)?.FindName(name);
    }

    #endregion

    #region Property Inheritance

    /// <inheritdoc />
    public override object? GetValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        // Check if we have a local value
        if (HasLocalValue(dp))
        {
            return base.GetValue(dp);
        }

        // For inheriting properties, check parent chain
        if (dp.DefaultMetadata.Inherits && VisualParent is FrameworkElement parent)
        {
            return parent.GetValue(dp);
        }

        return base.GetValue(dp);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the width of the element.
    /// </summary>
    public double Width
    {
        get => (double)(GetValue(WidthProperty) ?? double.NaN);
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of the element.
    /// </summary>
    public double Height
    {
        get => (double)(GetValue(HeightProperty) ?? double.NaN);
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum width of the element.
    /// </summary>
    public double MinWidth
    {
        get => (double)(GetValue(MinWidthProperty) ?? 0.0);
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the element.
    /// </summary>
    public double MinHeight
    {
        get => (double)(GetValue(MinHeightProperty) ?? 0.0);
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the element.
    /// </summary>
    public double MaxWidth
    {
        get => (double)(GetValue(MaxWidthProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the element.
    /// </summary>
    public double MaxHeight
    {
        get => (double)(GetValue(MaxHeightProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the margin around the element.
    /// </summary>
    public Thickness Margin
    {
        get => (Thickness)(GetValue(MarginProperty) ?? new Thickness(0));
        set => SetValue(MarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment.
    /// </summary>
    public HorizontalAlignment HorizontalAlignment
    {
        get => (HorizontalAlignment)(GetValue(HorizontalAlignmentProperty) ?? HorizontalAlignment.Stretch);
        set => SetValue(HorizontalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    public VerticalAlignment VerticalAlignment
    {
        get => (VerticalAlignment)(GetValue(VerticalAlignmentProperty) ?? VerticalAlignment.Stretch);
        set => SetValue(VerticalAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the data context for data binding.
    /// </summary>
    public object? DataContext
    {
        get => GetValue(DataContextProperty);
        set => SetValue(DataContextProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of the element.
    /// </summary>
    public string Name
    {
        get => (string)(GetValue(NameProperty) ?? string.Empty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>
    /// Gets or sets arbitrary object data associated with this element.
    /// </summary>
    public object? Tag
    {
        get => GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    private ResourceDictionary? _resources;

    /// <summary>
    /// Gets or sets the locally-defined resource dictionary.
    /// </summary>
    public ResourceDictionary Resources
    {
        get => _resources ??= new ResourceDictionary();
        set => _resources = value;
    }

    /// <summary>
    /// Searches for a resource with the specified key, and throws an exception if not found.
    /// </summary>
    /// <param name="resourceKey">The key identifier for the requested resource.</param>
    /// <returns>The requested resource.</returns>
    /// <exception cref="InvalidOperationException">The resource was not found.</exception>
    public object FindResource(object resourceKey)
    {
        var result = TryFindResource(resourceKey);
        if (result == null)
        {
            throw new InvalidOperationException($"Resource '{resourceKey}' not found.");
        }
        return result;
    }

    /// <summary>
    /// Searches for a resource with the specified key, and returns null if not found.
    /// </summary>
    /// <param name="resourceKey">The key identifier for the requested resource.</param>
    /// <returns>The requested resource, or null if not found.</returns>
    public object? TryFindResource(object resourceKey)
    {
        return ResourceLookup.FindResource(this, resourceKey);
    }

    /// <summary>
    /// Gets the actual rendered width of this element.
    /// </summary>
    public double ActualWidth => RenderSize.Width;

    /// <summary>
    /// Gets the actual rendered height of this element.
    /// </summary>
    public double ActualHeight => RenderSize.Height;

    /// <summary>
    /// Gets or sets the style used by this element.
    /// </summary>
    public Style? Style
    {
        get => (Style?)GetValue(StyleProperty);
        set => SetValue(StyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the cursor that displays when the mouse pointer is over this element.
    /// </summary>
    public Cursor? Cursor
    {
        get => (Cursor?)GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    #endregion

    #region Layout

    private Rect _visualBounds;

    /// <summary>
    /// Gets the visual bounds of this element in parent coordinates.
    /// </summary>
    public override Rect VisualBounds => _visualBounds;

    /// <summary>
    /// Sets the visual bounds of this element.
    /// This should only be called by parent containers after Arrange().
    /// Note: ArrangeCore already sets _visualBounds based on the finalRect,
    /// so this call is typically used to ensure consistency.
    /// </summary>
    public void SetVisualBounds(Rect bounds)
    {
        _visualBounds = bounds;
    }

    /// <summary>
    /// Debug helper: Gets the absolute position of this element in window coordinates
    /// by walking up the visual tree and accumulating VisualBounds offsets.
    /// </summary>
    /// <returns>The absolute position in window coordinates.</returns>
    public Point GetAbsolutePosition()
    {
        double x = 0;
        double y = 0;

        Visual? current = this;
        while (current != null)
        {
            if (current.VisualParent == null)
                break;

            if (current is FrameworkElement fe)
            {
                x += fe._visualBounds.X;
                y += fe._visualBounds.Y;
            }
            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Debug helper: Gets a string describing the visual bounds chain from this element to the root.
    /// </summary>
    public string GetVisualBoundsChainDebug()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Visual Bounds Chain for {GetType().Name} (Name={Name}):");

        Visual? current = this;
        int depth = 0;
        double totalX = 0, totalY = 0;

        while (current != null)
        {
            string indent = new string(' ', depth * 2);
            string name = (current as FrameworkElement)?.Name ?? "";
            if (!string.IsNullOrEmpty(name)) name = $" [{name}]";

            if (current is FrameworkElement fe)
            {
                var bounds = fe._visualBounds;
                sb.AppendLine($"{indent}{current.GetType().Name}{name}:");
                sb.AppendLine($"{indent}  VisualBounds = ({bounds.X:F1}, {bounds.Y:F1}, {bounds.Width:F1}, {bounds.Height:F1})");
                sb.AppendLine($"{indent}  VisualParent = {(current.VisualParent?.GetType().Name ?? "null")}");

                if (current.VisualParent != null)
                {
                    totalX += bounds.X;
                    totalY += bounds.Y;
                }
            }
            else
            {
                sb.AppendLine($"{indent}{current.GetType().Name}{name}: (not FrameworkElement)");
            }

            current = current.VisualParent;
            depth++;
        }

        sb.AppendLine($"Total offset from window: ({totalX:F1}, {totalY:F1})");
        return sb.ToString();
    }

    /// <inheritdoc />
    protected override Size MeasureCore(Size availableSize)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - marginWidth),
            Math.Max(0, availableSize.Height - marginHeight));

        // Apply explicit size constraints
        if (!double.IsNaN(Width))
        {
            contentAvailable = new Size(Width, contentAvailable.Height);
        }
        if (!double.IsNaN(Height))
        {
            contentAvailable = new Size(contentAvailable.Width, Height);
        }

        // Apply min/max constraints
        contentAvailable = new Size(
            Math.Clamp(contentAvailable.Width, MinWidth, MaxWidth),
            Math.Clamp(contentAvailable.Height, MinHeight, MaxHeight));

        // Measure content
        var contentSize = MeasureOverride(contentAvailable);

        // Apply constraints to result
        var resultWidth = double.IsNaN(Width) ? contentSize.Width : Width;
        var resultHeight = double.IsNaN(Height) ? contentSize.Height : Height;

        resultWidth = Math.Clamp(resultWidth, MinWidth, MaxWidth);
        resultHeight = Math.Clamp(resultHeight, MinHeight, MaxHeight);

        return new Size(resultWidth + marginWidth, resultHeight + marginHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeCore(Rect finalRect)
    {
        var margin = Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // Calculate available size for content
        var arrangeSize = new Size(
            Math.Max(0, finalRect.Width - marginWidth),
            Math.Max(0, finalRect.Height - marginHeight));

        // Apply explicit size constraints
        if (!double.IsNaN(Width))
        {
            arrangeSize = new Size(Width, arrangeSize.Height);
        }
        if (!double.IsNaN(Height))
        {
            arrangeSize = new Size(arrangeSize.Width, Height);
        }

        // Apply min/max constraints
        arrangeSize = new Size(
            Math.Clamp(arrangeSize.Width, MinWidth, MaxWidth),
            Math.Clamp(arrangeSize.Height, MinHeight, MaxHeight));

        // Arrange content
        var renderSize = ArrangeOverride(arrangeSize);

        // Calculate visual bounds based on alignment
        var x = finalRect.X + margin.Left;
        var y = finalRect.Y + margin.Top;

        // Horizontal alignment
        var extraWidth = finalRect.Width - marginWidth - renderSize.Width;
        if (extraWidth > 0)
        {
            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Center:
                    x += extraWidth / 2;
                    break;
                case HorizontalAlignment.Right:
                    x += extraWidth;
                    break;
            }
        }

        // Vertical alignment
        var extraHeight = finalRect.Height - marginHeight - renderSize.Height;
        if (extraHeight > 0)
        {
            switch (VerticalAlignment)
            {
                case VerticalAlignment.Center:
                    y += extraHeight / 2;
                    break;
                case VerticalAlignment.Bottom:
                    y += extraHeight;
                    break;
            }
        }

        // Set visual bounds for rendering
        _visualBounds = new Rect(x, y, renderSize.Width, renderSize.Height);

        return renderSize;
    }

    /// <summary>
    /// Override to implement custom measure logic.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected virtual Size MeasureOverride(Size availableSize)
    {
        return Size.Empty;
    }

    /// <summary>
    /// Override to implement custom arrange logic.
    /// </summary>
    /// <param name="finalSize">The final size.</param>
    /// <returns>The render size.</returns>
    protected virtual Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    #endregion

    #region Hit Testing

    /// <inheritdoc />
    protected override HitTestResult? HitTestCore(Point point)
    {
        // Check if point is within our visual bounds
        if (!_visualBounds.Contains(point))
        {
            return null;
        }

        // Check if this element is visible and enabled for hit testing
        if (this is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
        {
            return null;
        }

        // Transform point to local coordinates (relative to this element)
        var localPoint = new Point(point.X - _visualBounds.X, point.Y - _visualBounds.Y);

        // Check children in reverse order (top to bottom in z-order)
        for (int i = VisualChildrenCount - 1; i >= 0; i--)
        {
            var child = GetVisualChild(i);
            if (child is FrameworkElement fe)
            {
                // Pass the localPoint to children since their bounds are relative to us
                var childResult = fe.HitTestCore(localPoint);
                if (childResult != null)
                {
                    return childResult;
                }
            }
        }

        // No child was hit, check if we're hit-testable
        if (IsHitTestVisible)
        {
            return HitTestResult.GetReusable(this);
        }

        return null;
    }

    /// <summary>
    /// Gets or sets a value indicating whether this element can be hit tested.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>
    /// Performs hit testing at the specified point.
    /// </summary>
    /// <param name="point">The point to test in this element's coordinate space.</param>
    /// <returns>The hit test result, or null if nothing was hit.</returns>
    public HitTestResult? HitTest(Point point)
    {
        return HitTestCore(point);
    }

    #endregion

    #region BringIntoView

    /// <summary>
    /// Identifies the RequestBringIntoView routed event.
    /// </summary>
    public static readonly RoutedEvent RequestBringIntoViewEvent =
        EventManager.RegisterRoutedEvent(nameof(RequestBringIntoView), RoutingStrategy.Bubble,
            typeof(RequestBringIntoViewEventHandler), typeof(FrameworkElement));

    /// <summary>
    /// Occurs when BringIntoView is called on this element.
    /// </summary>
    public event RequestBringIntoViewEventHandler RequestBringIntoView
    {
        add => AddHandler(RequestBringIntoViewEvent, value);
        remove => RemoveHandler(RequestBringIntoViewEvent, value);
    }

    /// <summary>
    /// Attempts to bring this element into view, within any scrollable regions it is contained within.
    /// </summary>
    public void BringIntoView()
    {
        BringIntoView(Rect.Empty);
    }

    /// <summary>
    /// Attempts to bring the provided region size of this element into view,
    /// within any scrollable regions it is contained within.
    /// </summary>
    /// <param name="targetRectangle">The rectangular region to bring into view. Use Rect.Empty for the entire element.</param>
    public void BringIntoView(Rect targetRectangle)
    {
        var args = new RequestBringIntoViewEventArgs(RequestBringIntoViewEvent, this)
        {
            TargetObject = this,
            TargetRect = targetRectangle.IsEmpty ? new Rect(0, 0, ActualWidth, ActualHeight) : targetRectangle
        };

        RaiseEvent(args);
    }

    /// <summary>
    /// Calculates this element's position relative to an ancestor element.
    /// </summary>
    /// <param name="ancestor">The ancestor element. If null, calculates to the root.</param>
    /// <returns>The position offset relative to the ancestor.</returns>
    public Point TransformToAncestor(Visual? ancestor)
    {
        double x = 0;
        double y = 0;

        Visual? current = this;
        while (current != null && current != ancestor)
        {
            if (current is FrameworkElement fe)
            {
                x += fe._visualBounds.X;
                y += fe._visualBounds.Y;
            }

            if (current.VisualParent == null)
                break;

            current = current.VisualParent;
        }

        return new Point(x, y);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.InvalidateMeasure();
        }
    }

    private static void OnDataContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.OnDataContextChanged(e.OldValue, e.NewValue);
            element.DataContextChanged?.Invoke(element, e);
        }
    }

    /// <summary>
    /// Called when the DataContext property changes.
    /// </summary>
    protected virtual void OnDataContextChanged(object? oldValue, object? newValue)
    {
    }

    /// <summary>
    /// Occurs when the DataContext property changes.
    /// </summary>
    public event EventHandler<DependencyPropertyChangedEventArgs>? DataContextChanged;

    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            // Remove implicit style if explicit style is being set
            if (e.NewValue != null && element._implicitStyle != null)
            {
                element._implicitStyle.Remove(element);
                element._implicitStyle = null;
            }

            // Remove old style
            if (e.OldValue is Style oldStyle)
            {
                oldStyle.Remove(element);
            }

            // Apply new style
            if (e.NewValue is Style newStyle)
            {
                newStyle.Apply(element);
            }
            else
            {
                // If explicit style is cleared, try to apply implicit style
                element.ApplyImplicitStyleIfNeeded();
            }

            element.InvalidateVisual();
        }
    }

    #endregion

    #region Visual Parent Changed

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ApplyImplicitStyleIfNeeded();
    }

    /// <summary>
    /// Applies an implicit style to this element if no explicit style is set.
    /// </summary>
    private void ApplyImplicitStyleIfNeeded()
    {
        // Explicit style takes priority
        if (Style != null)
        {
            return;
        }

        // Remove old implicit style
        if (_implicitStyle != null)
        {
            _implicitStyle.Remove(this);
            _implicitStyle = null;
        }

        // Look up implicit style by Type
        var elementType = GetType();
        var implicitStyle = TryFindResource(elementType) as Style;

        if (implicitStyle != null && IsStyleApplicable(implicitStyle))
        {
            _implicitStyle = implicitStyle;
            implicitStyle.Apply(this);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Checks if a style is applicable to this element.
    /// </summary>
    private bool IsStyleApplicable(Style style)
    {
        if (style.TargetType == null)
            return true;

        return style.TargetType.IsAssignableFrom(GetType());
    }

    #endregion
}

/// <summary>
/// Specifies horizontal alignment.
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Stretch
}

/// <summary>
/// Specifies vertical alignment.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Stretch
}

/// <summary>
/// Provides data for the RequestBringIntoView event.
/// </summary>
public class RequestBringIntoViewEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the object that should be made visible.
    /// </summary>
    public DependencyObject? TargetObject { get; init; }

    /// <summary>
    /// Gets the rectangular region in the object's coordinate space which should be made visible.
    /// </summary>
    public Rect TargetRect { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestBringIntoViewEventArgs"/> class.
    /// </summary>
    public RequestBringIntoViewEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }
}

/// <summary>
/// Delegate for handling RequestBringIntoView events.
/// </summary>
public delegate void RequestBringIntoViewEventHandler(object sender, RequestBringIntoViewEventArgs e);
