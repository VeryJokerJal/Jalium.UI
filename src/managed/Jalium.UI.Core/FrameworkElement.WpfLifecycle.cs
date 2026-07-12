using System.Collections;
using System.ComponentModel;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI;

/// <summary>Specifies the horizontal flow direction of layout and content.</summary>
public enum FlowDirection
{
    LeftToRight,
    RightToLeft,
}

public partial class FrameworkElement : ISupportInitialize
{
    private static readonly DependencyPropertyKey ActualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ActualWidth), typeof(double), typeof(FrameworkElement), new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ActualHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ActualHeight), typeof(double), typeof(FrameworkElement), new PropertyMetadata(0.0));

    public static readonly DependencyProperty ActualWidthProperty = ActualWidthPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ActualHeightProperty = ActualHeightPropertyKey.DependencyProperty;

    public static readonly DependencyProperty FlowDirectionProperty =
        DependencyProperty.RegisterAttached(
            nameof(FlowDirection),
            typeof(FlowDirection),
            typeof(FrameworkElement),
            new PropertyMetadata(FlowDirection.LeftToRight, OnFrameworkLayoutPropertyChanged, null, inherits: true),
            static value => value is FlowDirection direction && Enum.IsDefined(direction));

    public static readonly DependencyProperty ForceCursorProperty =
        DependencyProperty.Register(nameof(ForceCursor), typeof(bool), typeof(FrameworkElement), new PropertyMetadata(false));

    public static readonly DependencyProperty LayoutTransformProperty =
        DependencyProperty.Register(
            nameof(LayoutTransform),
            typeof(Transform),
            typeof(FrameworkElement),
            new PropertyMetadata(null, OnLayoutTransformChanged));

    public static readonly DependencyProperty OverridesDefaultStyleProperty =
        DependencyProperty.Register(
            nameof(OverridesDefaultStyle),
            typeof(bool),
            typeof(FrameworkElement),
            new PropertyMetadata(false, OnDefaultStyleSelectionChanged));

    public static readonly DependencyProperty UseLayoutRoundingProperty =
        DependencyProperty.Register(
            nameof(UseLayoutRounding),
            typeof(bool),
            typeof(FrameworkElement),
            new PropertyMetadata(false, OnFrameworkLayoutPropertyChanged, null, inherits: true));

    protected internal static readonly DependencyProperty DefaultStyleKeyProperty =
        DependencyProperty.Register(
            nameof(DefaultStyleKey),
            typeof(object),
            typeof(FrameworkElement),
            new PropertyMetadata(null, OnDefaultStyleSelectionChanged));

    public static readonly DependencyProperty BindingGroupProperty =
        DependencyProperty.Register(
            nameof(BindingGroup),
            typeof(BindingGroup),
            typeof(FrameworkElement),
            new PropertyMetadata(null, null, null, inherits: true));

    public static readonly RoutedEvent LoadedEvent =
        EventManager.RegisterRoutedEvent(nameof(Loaded), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(FrameworkElement));

    public static readonly RoutedEvent UnloadedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unloaded), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(FrameworkElement));

    public static readonly RoutedEvent SizeChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SizeChanged), RoutingStrategy.Direct, typeof(SizeChangedEventHandler), typeof(FrameworkElement));

    private List<object>? _logicalChildren;
    private FrameworkElement? _logicalParent;
    private int _initializationDepth;
    private bool _isInitialized;
    private bool _isLoaded;
    private InheritanceBehavior _inheritanceBehavior;

    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    public bool ForceCursor
    {
        get => (bool)(GetValue(ForceCursorProperty) ?? false);
        set => SetValue(ForceCursorProperty, value);
    }

    public Transform? LayoutTransform
    {
        get => (Transform?)GetValue(LayoutTransformProperty);
        set => SetValue(LayoutTransformProperty, value);
    }

    public bool OverridesDefaultStyle
    {
        get => (bool)(GetValue(OverridesDefaultStyleProperty) ?? false);
        set => SetValue(OverridesDefaultStyleProperty, value);
    }

    public bool UseLayoutRounding
    {
        get => (bool)(GetValue(UseLayoutRoundingProperty) ?? false);
        set => SetValue(UseLayoutRoundingProperty, value);
    }

    protected internal object? DefaultStyleKey
    {
        get => GetValue(DefaultStyleKeyProperty);
        set => SetValue(DefaultStyleKeyProperty, value);
    }

    public BindingGroup? BindingGroup
    {
        get => (BindingGroup?)GetValue(BindingGroupProperty);
        set
        {
            BindingGroup? oldValue = BindingGroup;
            if (ReferenceEquals(oldValue, value))
            {
                return;
            }

            if (ReferenceEquals(oldValue?.Owner, this))
            {
                oldValue.SetOwner(null);
            }

            value?.SetOwner(this);
            SetValue(BindingGroupProperty, value);
        }
    }

    public bool IsInitialized => _isInitialized;
    public bool IsLoaded => _isLoaded;
    public DependencyObject? Parent => _logicalParent ?? VisualParent as DependencyObject;

    protected internal InheritanceBehavior InheritanceBehavior
    {
        get => _inheritanceBehavior;
        set
        {
            if (_isInitialized || Parent != null)
            {
                throw new InvalidOperationException("InheritanceBehavior must be set before initialization and parenting.");
            }

            _inheritanceBehavior = value;
        }
    }

    protected internal virtual IEnumerator LogicalChildren
        => (_logicalChildren ?? Enumerable.Empty<object>()).GetEnumerator();

    internal FrameworkElement? FrameworkParent => _logicalParent ?? VisualParent as FrameworkElement;

    public static FlowDirection GetFlowDirection(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FlowDirection)(element.GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
    }

    public static void SetFlowDirection(DependencyObject element, FlowDirection value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FlowDirectionProperty, value);
    }

    public virtual void BeginInit()
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException("An initialized FrameworkElement cannot begin initialization again.");
        }

        _initializationDepth++;
    }

    public virtual void EndInit()
    {
        if (_isInitialized)
        {
            return;
        }

        if (_initializationDepth > 0)
        {
            _initializationDepth--;
        }

        if (_initializationDepth == 0)
        {
            EnsureInitialized();
        }
    }

    protected virtual void OnInitialized(EventArgs e) => Initialized?.Invoke(this, e);

    public event EventHandler? Initialized;

    protected internal void AddLogicalChild(object? child)
    {
        if (child == null)
        {
            return;
        }

        if (ReferenceEquals(child, this))
        {
            throw new InvalidOperationException("An element cannot be its own logical child.");
        }

        _logicalChildren ??= new List<object>();
        if (_logicalChildren.Contains(child))
        {
            return;
        }

        if (child is FrameworkElement element)
        {
            if (element._logicalParent != null && !ReferenceEquals(element._logicalParent, this))
            {
                throw new InvalidOperationException("The logical child already has a parent.");
            }

            element._logicalParent = this;
            element.ReactivateBindings();
            if (IsLoaded)
            {
                element.SetLoadedState(true);
            }
        }

        _logicalChildren.Add(child);
        ResourceLookup.InvalidateResourceCache();
    }

    protected internal void RemoveLogicalChild(object? child)
    {
        if (child == null || _logicalChildren == null || !_logicalChildren.Remove(child))
        {
            return;
        }

        if (child is FrameworkElement element && ReferenceEquals(element._logicalParent, this))
        {
            if (element.IsLoaded)
            {
                element.SetLoadedState(false);
            }

            element._logicalParent = null;
        }

        ResourceLookup.InvalidateResourceCache();
    }

    protected internal override DependencyObject? GetUIParentCore() => Parent;

    public sealed override bool MoveFocus(Input.TraversalRequest request) => base.MoveFocus(request);

    public bool ApplyTemplate() => ApplyTemplateCore();

    protected virtual bool ApplyTemplateCore() => false;

    public virtual void OnApplyTemplate()
    {
    }

    protected internal DependencyObject? GetTemplateChild(string childName)
    {
        if (string.IsNullOrEmpty(childName))
        {
            return null;
        }

        return FindTemplateChild(this, childName);
    }

    public void SetResourceReference(DependencyProperty dp, object name)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(name);
        DynamicResourceBindingOperations.SetDynamicResource(this, dp, name);
    }

    public bool ShouldSerializeResources() => _resources is { Count: > 0 };
    public bool ShouldSerializeStyle() => HasLocalValue(StyleProperty);

    public void UpdateDefaultStyle() => ReEvaluateImplicitStyle();

    internal void SetLoadedState(bool loaded)
    {
        if (_isLoaded == loaded)
        {
            return;
        }

        if (loaded)
        {
            EnsureInitialized();
        }

        _isLoaded = loaded;
        var routedEvent = loaded ? LoadedEvent : UnloadedEvent;
        RaiseEvent(new RoutedEventArgs(routedEvent, this));

        if (loaded)
        {
            Loaded?.Invoke(this, new RoutedEventArgs(routedEvent, this));
        }
        else
        {
            Unloaded?.Invoke(this, new RoutedEventArgs(routedEvent, this));
        }

        for (var i = 0; i < VisualChildrenCount; i++)
        {
            if (GetVisualChild(i) is FrameworkElement child)
            {
                child.SetLoadedState(loaded);
            }
        }

        if (_logicalChildren != null)
        {
            foreach (var child in _logicalChildren.OfType<FrameworkElement>())
            {
                if (child.VisualParent == null)
                {
                    child.SetLoadedState(loaded);
                }
            }
        }
    }

    internal static Cursor? ResolveEffectiveCursor(FrameworkElement element)
    {
        Cursor? nearest = null;
        Cursor? forced = null;
        FrameworkElement? current = element;
        while (current != null)
        {
            if (current.Cursor != null)
            {
                nearest ??= current.Cursor;
                if (current.ForceCursor)
                {
                    forced = current.Cursor;
                    break;
                }
            }

            current = current.FrameworkParent;
        }

        var args = new Input.QueryCursorEventArgs
        {
            RoutedEvent = UIElement.QueryCursorEvent,
            Source = element,
            Cursor = forced ?? nearest,
        };
        element.RaiseEvent(args);
        return forced ?? args.Cursor;
    }

    internal Size ApplyLayoutTransformToDesiredSize(Size size)
    {
        var transform = LayoutTransform;
        if (transform == null || transform.Value.IsIdentity)
        {
            return size;
        }

        var matrix = transform.Value;
        var points = new[]
        {
            matrix.Transform(Point.Zero),
            matrix.Transform(new Point(size.Width, 0)),
            matrix.Transform(new Point(0, size.Height)),
            matrix.Transform(new Point(size.Width, size.Height)),
        };
        var minX = points.Min(static point => point.X);
        var maxX = points.Max(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxY = points.Max(static point => point.Y);
        return new Size(Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    internal double RoundLayoutValue(double value)
    {
        if (!UseLayoutRounding || !double.IsFinite(value))
        {
            return value;
        }

        var scale = LayoutDpiScale > 0 && double.IsFinite(LayoutDpiScale) ? LayoutDpiScale : 1.0;
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }

    private void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        OnInitialized(EventArgs.Empty);
    }

    private static DependencyObject? FindTemplateChild(FrameworkElement root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        if (root.FindName(name) is DependencyObject named)
        {
            return named;
        }

        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is FrameworkElement child
                && FindTemplateChild(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static void OnFrameworkLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
        }
    }

    private static void OnLayoutTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if (e.OldValue is Transform oldTransform)
        {
            oldTransform.Changed -= element.OnLayoutTransformSubPropertyChanged;
        }

        if (e.NewValue is Transform newTransform)
        {
            newTransform.Changed += element.OnLayoutTransformSubPropertyChanged;
        }

        OnFrameworkLayoutPropertyChanged(d, e);
    }

    private void OnLayoutTransformSubPropertyChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private static void OnDefaultStyleSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            element.ReEvaluateImplicitStyle();
            element.InvalidateMeasure();
            element.InvalidateVisual();
        }
    }
}
