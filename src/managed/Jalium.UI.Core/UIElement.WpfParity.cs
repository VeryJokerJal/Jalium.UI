using System.Threading;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class UIElement
{
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCaptured), typeof(bool), typeof(UIElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCapturedWithin), typeof(bool), typeof(UIElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesDirectlyOver), typeof(bool), typeof(UIElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesOver), typeof(bool), typeof(UIElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsMouseCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptured), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsMouseCapturedPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptureWithin), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsMouseCaptureWithinPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseDirectlyOver), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsMouseDirectlyOverPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptured), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsStylusCapturedPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptureWithin), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsStylusCaptureWithinPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusDirectlyOver), typeof(bool), typeof(UIElement), new PropertyMetadata(false, OnIsStylusDirectlyOverPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusOver), typeof(bool), typeof(UIElement), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsVisiblePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsVisible), typeof(bool), typeof(UIElement), new PropertyMetadata(true, OnIsVisiblePropertyChanged));

    public static readonly DependencyProperty AllowDropProperty = DragDrop.AllowDropProperty;
    public static readonly DependencyProperty AreAnyTouchesCapturedProperty = AreAnyTouchesCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesCapturedWithinProperty = AreAnyTouchesCapturedWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesDirectlyOverProperty = AreAnyTouchesDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesOverProperty = AreAnyTouchesOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCapturedProperty = IsMouseCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCaptureWithinProperty = IsMouseCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseDirectlyOverProperty = IsMouseDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCapturedProperty = IsStylusCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCaptureWithinProperty = IsStylusCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusDirectlyOverProperty = IsStylusDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusOverProperty = IsStylusOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsVisibleProperty = IsVisiblePropertyKey.DependencyProperty;

    public static readonly DependencyProperty SnapsToDevicePixelsProperty =
        DependencyProperty.Register(
            nameof(SnapsToDevicePixels),
            typeof(bool),
            typeof(UIElement),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UidProperty =
        DependencyProperty.Register(nameof(Uid), typeof(string), typeof(UIElement), new PropertyMetadata(string.Empty));

    public static readonly RoutedEvent PreviewDragEnterEvent = DragDrop.PreviewDragEnterEvent;
    public static readonly RoutedEvent DragEnterEvent = DragDrop.DragEnterEvent;
    public static readonly RoutedEvent PreviewDragOverEvent = DragDrop.PreviewDragOverEvent;
    public static readonly RoutedEvent DragOverEvent = DragDrop.DragOverEvent;
    public static readonly RoutedEvent PreviewDragLeaveEvent = DragDrop.PreviewDragLeaveEvent;
    public static readonly RoutedEvent DragLeaveEvent = DragDrop.DragLeaveEvent;
    public static readonly RoutedEvent PreviewDropEvent = DragDrop.PreviewDropEvent;
    public static readonly RoutedEvent DropEvent = DragDrop.DropEvent;
    public static readonly RoutedEvent GiveFeedbackEvent = DragDrop.GiveFeedbackEvent;
    public static readonly RoutedEvent QueryContinueDragEvent = DragDrop.QueryContinueDragEvent;
    public static readonly RoutedEvent PreviewGiveFeedbackEvent = DragDrop.PreviewGiveFeedbackEvent;
    public static readonly RoutedEvent PreviewQueryContinueDragEvent = DragDrop.PreviewQueryContinueDragEvent;
    public static readonly RoutedEvent QueryCursorEvent =
        EventManager.RegisterRoutedEvent(nameof(QueryCursor), RoutingStrategy.Bubble, typeof(QueryCursorEventHandler), typeof(UIElement));

    private static int s_nextPersistId;
    private readonly int _persistId = Interlocked.Increment(ref s_nextPersistId);

    public bool IsVisible => (bool)(GetValue(IsVisibleProperty) ?? true);

    public bool IsInputMethodEnabled => InputMethodService.GetIsInputMethodEnabled(this);

    public bool IsStylusCaptureWithin
    {
        get
        {
            Visual? current = _stylusCaptured;
            while (current != null)
            {
                if (ReferenceEquals(current, this)) return true;
                current = current.VisualParent;
            }

            return false;
        }
    }

    public bool SnapsToDevicePixels
    {
        get => (bool)(GetValue(SnapsToDevicePixelsProperty) ?? false);
        set => SetValue(SnapsToDevicePixelsProperty, value);
    }

    public string Uid
    {
        get => (string?)GetValue(UidProperty) ?? string.Empty;
        set => SetValue(UidProperty, value ?? string.Empty);
    }

    [Obsolete("PersistId is retained for WPF compatibility only.")]
    public int PersistId => _persistId;

    public bool HasAnimatedProperties => _activeAnimations is { Count: > 0 };

    protected virtual bool IsEnabledCore => true;

    protected internal virtual bool HasEffectiveKeyboardFocus => IsKeyboardFocused;

    public event EventHandler? LayoutUpdated;
    public event DependencyPropertyChangedEventHandler? FocusableChanged;
    public event DependencyPropertyChangedEventHandler? IsEnabledChanged;
    public event DependencyPropertyChangedEventHandler? IsHitTestVisibleChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusedChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsVisibleChanged;

    public event GiveFeedbackEventHandler PreviewGiveFeedback
    {
        add => AddHandler(PreviewGiveFeedbackEvent, value);
        remove => RemoveHandler(PreviewGiveFeedbackEvent, value);
    }

    public event QueryContinueDragEventHandler PreviewQueryContinueDrag
    {
        add => AddHandler(PreviewQueryContinueDragEvent, value);
        remove => RemoveHandler(PreviewQueryContinueDragEvent, value);
    }

    public event QueryCursorEventHandler QueryCursor
    {
        add => AddHandler(QueryCursorEvent, value);
        remove => RemoveHandler(QueryCursorEvent, value);
    }

    public new object? GetAnimationBaseValue(DependencyProperty dp) => base.GetAnimationBaseValue(dp);

    public bool ShouldSerializeCommandBindings() => _commandBindings is { Count: > 0 };

    public bool ShouldSerializeInputBindings() => _inputBindings is { Count: > 0 };

    public IInputElement? InputHitTest(Point point) => VisualTreeHelper.HitTest(this, point)?.VisualHit as IInputElement;

    public Point TranslatePoint(Point point, UIElement? relativeTo)
    {
        var inRoot = GetRenderMatrixTo(null).Transform(point);
        if (relativeTo == null)
        {
            return inRoot;
        }

        var relativeMatrix = relativeTo.GetRenderMatrixTo(null);
        return relativeMatrix.TryInvert(out var inverse) ? inverse.Transform(inRoot) : point;
    }

    public void UpdateLayout()
    {
        UIElement root = this;
        while (root.VisualParent is UIElement parent)
        {
            root = parent;
        }

        var available = root.PreviousAvailableSize;
        if (double.IsNaN(available.Width) || double.IsNaN(available.Height) ||
            double.IsInfinity(available.Width) || double.IsInfinity(available.Height))
        {
            available = root.RenderSize;
        }

        if (available.Width <= 0 || available.Height <= 0)
        {
            available = root.DesiredSize;
        }

        if (FindLayoutManager() is { } layoutManager)
        {
            layoutManager.UpdateLayout(root, available);
        }
        else
        {
            root.Measure(available);
            root.Arrange(new Rect(0, 0, available.Width, available.Height));
        }

        RaiseLayoutUpdatedRecursive(root);
    }

    public void AddToEventRoute(EventRoute route, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(e);

        foreach (var classHandler in EventManager.GetClassHandlers(route.RoutedEvent, GetType()))
        {
            route.Add(this, classHandler.Handler, classHandler.HandledEventsToo);
        }

        if (_eventHandlers != null && _eventHandlers.TryGetValue(route.RoutedEvent, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                route.Add(this, handler.Handler, handler.InvokeHandledEventsToo);
            }
        }
    }

    protected internal virtual DependencyObject? GetUIParentCore() => VisualParent;

    protected internal virtual void OnRenderSizeChanged(SizeChangedInfo info)
    {
    }

    protected virtual void OnChildDesiredSizeChanged(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        InvalidateMeasure();
    }

    protected virtual Geometry? GetLayoutClip(Size layoutSlotSize) => GetLayoutClip();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        return HitTestCore(hitTestParameters.HitPoint);
    }

    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters)
    {
        ArgumentNullException.ThrowIfNull(hitTestParameters);
        var elementBounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var hitBounds = hitTestParameters.HitTestArea.Bounds;
        var intersection = Rect.Intersect(elementBounds, hitBounds);
        if (intersection.IsEmpty)
        {
            return new GeometryHitTestResult(this, IntersectionDetail.Empty);
        }

        var detail = ContainsRect(elementBounds, hitBounds)
            ? IntersectionDetail.FullyContains
            : ContainsRect(hitBounds, elementBounds)
                ? IntersectionDetail.FullyInside
                : IntersectionDetail.Intersects;
        return new GeometryHitTestResult(this, detail);
    }

    private static bool ContainsRect(Rect outer, Rect inner)
    {
        return !outer.IsEmpty && !inner.IsEmpty &&
               inner.Left >= outer.Left && inner.Top >= outer.Top &&
               inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
    }

    protected virtual void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected virtual void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected virtual void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected virtual void OnPreviewLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected virtual void OnPreviewGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    protected virtual void OnPreviewQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    protected virtual void OnQueryCursor(QueryCursorEventArgs e)
    {
    }

    protected virtual void OnAccessKey(AccessKeyEventArgs e)
    {
    }

    internal void InvokeAccessKey(AccessKeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnAccessKey(e);
    }

    protected virtual void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    private static void RaiseLayoutUpdatedRecursive(UIElement element)
    {
        element.LayoutUpdated?.Invoke(element, EventArgs.Empty);
        for (var index = 0; index < element.VisualChildrenCount; index++)
        {
            if (element.GetVisualChild(index) is UIElement child)
            {
                RaiseLayoutUpdatedRecursive(child);
            }
        }
    }

    private static void InitializeWpfParityClassHandlers()
    {
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, e) => ((UIElement)sender).OnPreviewGotKeyboardFocus(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, e) => ((UIElement)sender).OnGotKeyboardFocus(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewLostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, e) => ((UIElement)sender).OnPreviewLostKeyboardFocus(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, e) => ((UIElement)sender).OnLostKeyboardFocus(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewGiveFeedbackEvent,
            new GiveFeedbackEventHandler((sender, e) => ((UIElement)sender).OnPreviewGiveFeedback(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewQueryContinueDragEvent,
            new QueryContinueDragEventHandler((sender, e) => ((UIElement)sender).OnPreviewQueryContinueDrag(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), QueryCursorEvent,
            new QueryCursorEventHandler((sender, e) => ((UIElement)sender).OnQueryCursor(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), GotStylusCaptureEvent,
            new StylusEventHandler((sender, e) => ((UIElement)sender).OnGotStylusCapture(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), LostStylusCaptureEvent,
            new StylusEventHandler((sender, e) => ((UIElement)sender).OnLostStylusCapture(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusButtonDownEvent,
            new StylusButtonEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusButtonUpEvent,
            new StylusButtonEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusInAirMoveEvent,
            new StylusEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusInAirMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusInRangeEvent,
            new StylusEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusInRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusOutOfRangeEvent,
            new StylusEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusOutOfRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusSystemGestureEvent,
            new StylusSystemGestureEventHandler((sender, e) => ((UIElement)sender).OnPreviewStylusSystemGesture(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationStartingEvent,
            new EventHandler<ManipulationStartingEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationStarting(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationStartedEvent,
            new EventHandler<ManipulationStartedEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationStarted(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationDeltaEvent,
            new EventHandler<ManipulationDeltaEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationDelta(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationInertiaStartingEvent,
            new EventHandler<ManipulationInertiaStartingEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationInertiaStarting(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationBoundaryFeedbackEvent,
            new EventHandler<ManipulationBoundaryFeedbackEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationBoundaryFeedback(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), ManipulationCompletedEvent,
            new EventHandler<ManipulationCompletedEventArgs>((sender, e) => ((UIElement)sender!).OnManipulationCompleted(e)));
    }

    private static void OnFocusablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element) element.FocusableChanged?.Invoke(element, e);
    }

    private static void OnIsMouseCapturedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsMouseCapturedChanged(e);
        element.IsMouseCapturedChanged?.Invoke(element, e);
    }

    private static void OnIsMouseCaptureWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsMouseCaptureWithinChanged(e);
        element.IsMouseCaptureWithinChanged?.Invoke(element, e);
    }

    private static void OnIsMouseDirectlyOverPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsMouseDirectlyOverChanged(e);
        element.IsMouseDirectlyOverChanged?.Invoke(element, e);
    }

    private static void OnIsStylusCapturedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsStylusCapturedChanged(e);
        element.IsStylusCapturedChanged?.Invoke(element, e);
    }

    private static void OnIsStylusCaptureWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsStylusCaptureWithinChanged(e);
        element.IsStylusCaptureWithinChanged?.Invoke(element, e);
    }

    private static void OnIsStylusDirectlyOverPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.OnIsStylusDirectlyOverChanged(e);
        element.IsStylusDirectlyOverChanged?.Invoke(element, e);
    }

    private static void OnIsVisiblePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement)d;
        element.IsVisibleChanged?.Invoke(element, e);
    }

    private void RaiseIsEnabledChanged(DependencyPropertyChangedEventArgs e) => IsEnabledChanged?.Invoke(this, e);
    private void RaiseIsHitTestVisibleChanged(DependencyPropertyChangedEventArgs e) => IsHitTestVisibleChanged?.Invoke(this, e);
    private void RaiseIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e) => IsKeyboardFocusedChanged?.Invoke(this, e);
    private void RaiseIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e) => IsKeyboardFocusWithinChanged?.Invoke(this, e);

    internal void UpdateIsVisibleFromTree()
    {
        var visible = Visibility == Visibility.Visible &&
                      (VisualParent is not UIElement parent || parent.IsVisible);
        SetValue(IsVisiblePropertyKey, visible);
        for (var index = 0; index < VisualChildrenCount; index++)
        {
            if (GetVisualChild(index) is UIElement child)
            {
                child.UpdateIsVisibleFromTree();
            }
        }
    }

    private static HashSet<UIElement> GetAncestorSet(UIElement? element)
    {
        var result = new HashSet<UIElement>();
        Visual? current = element;
        while (current != null)
        {
            if (current is UIElement ui) result.Add(ui);
            current = current.VisualParent;
        }

        return result;
    }

    private static void UpdateMouseDirectlyOverDependencyState(UIElement? oldElement, UIElement? newElement)
    {
        oldElement?.SetValue(IsMouseDirectlyOverPropertyKey, false);
        newElement?.SetValue(IsMouseDirectlyOverPropertyKey, true);
    }

    private static void UpdateMouseCaptureDependencyState(UIElement? oldElement, UIElement? newElement)
    {
        oldElement?.SetValue(IsMouseCapturedPropertyKey, false);
        newElement?.SetValue(IsMouseCapturedPropertyKey, true);
        UpdateWithinState(oldElement, newElement, IsMouseCaptureWithinPropertyKey);
    }

    private static void UpdateStylusDirectlyOverDependencyState(UIElement? oldElement, UIElement? newElement)
    {
        oldElement?.SetValue(IsStylusDirectlyOverPropertyKey, false);
        newElement?.SetValue(IsStylusDirectlyOverPropertyKey, true);
        UpdateWithinState(oldElement, newElement, IsStylusOverPropertyKey);
    }

    private static void UpdateStylusCaptureDependencyState(UIElement? oldElement, UIElement? newElement)
    {
        oldElement?.SetValue(IsStylusCapturedPropertyKey, false);
        newElement?.SetValue(IsStylusCapturedPropertyKey, true);
        UpdateWithinState(oldElement, newElement, IsStylusCaptureWithinPropertyKey);
    }

    private static void UpdateWithinState(UIElement? oldElement, UIElement? newElement, DependencyPropertyKey key)
    {
        var oldAncestors = GetAncestorSet(oldElement);
        var newAncestors = GetAncestorSet(newElement);
        foreach (var element in oldAncestors)
        {
            if (!newAncestors.Contains(element)) element.SetValue(key, false);
        }

        foreach (var element in newAncestors)
        {
            if (!oldAncestors.Contains(element)) element.SetValue(key, true);
        }
    }

    private void UpdateTouchDependencyState()
    {
        SetValue(AreAnyTouchesCapturedPropertyKey, AreAnyTouchesCaptured);
        SetValue(AreAnyTouchesCapturedWithinPropertyKey, AreAnyTouchesCapturedWithin);
        SetValue(AreAnyTouchesDirectlyOverPropertyKey, AreAnyTouchesDirectlyOver);
        SetValue(AreAnyTouchesOverPropertyKey, AreAnyTouchesOver);

        if (VisualParent is UIElement parent)
        {
            parent.UpdateTouchDependencyState();
        }
    }
}
