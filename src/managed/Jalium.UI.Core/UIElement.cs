using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

namespace Jalium.UI;

/// <summary>
/// Base class for UI elements that participate in layout, input, and rendering.
/// </summary>
public abstract partial class UIElement : Visual, IInputElement
{
    #region Static Constructor — Class Handler Registration

    static UIElement()
    {
        // Keyboard
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewKeyDownEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnPreviewKeyDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), KeyDownEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnKeyDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewKeyUpEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnPreviewKeyUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), KeyUpEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnKeyUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTextInputEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewTextInput(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TextInputEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnTextInput(e)));

        // Mouse
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseDownEvent, new MouseButtonEventHandler(OnMouseDownThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUpThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseUpEvent, new MouseButtonEventHandler(OnMouseUpThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseMoveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnPreviewMouseMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseMoveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseEnterEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseLeaveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseWheelEvent, new MouseWheelEventHandler((s, e) => ((UIElement)s).OnPreviewMouseWheel(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseWheelEvent, new MouseWheelEventHandler((s, e) => ((UIElement)s).OnMouseWheel(e)));

        // Touch
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchDownEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewTouchDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchDownEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnTouchDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchMoveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewTouchMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchMoveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnTouchMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchUpEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewTouchUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchUpEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnTouchUp(e)));

        // Stylus
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusDownEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewStylusDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusDownEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusMoveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewStylusMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusMoveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusUpEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnPreviewStylusUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusUpEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusInAirMoveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusInAirMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusEnterEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusLeaveEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusInRangeEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusInRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusOutOfRangeEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusOutOfRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusButtonDownEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusButtonUpEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusSystemGestureEvent, new RoutedEventHandler((s, e) => ((UIElement)s).OnStylusSystemGesture(e)));

        // Drag and Drop
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.PreviewDragEnterEvent, new DragEventHandler((s, e) => ((UIElement)s).OnPreviewDragEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.DragEnterEvent, new DragEventHandler((s, e) => ((UIElement)s).OnDragEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.PreviewDragOverEvent, new DragEventHandler((s, e) => ((UIElement)s).OnPreviewDragOver(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.DragOverEvent, new DragEventHandler((s, e) => ((UIElement)s).OnDragOver(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.PreviewDragLeaveEvent, new DragEventHandler((s, e) => ((UIElement)s).OnPreviewDragLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.DragLeaveEvent, new DragEventHandler((s, e) => ((UIElement)s).OnDragLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.PreviewDropEvent, new DragEventHandler((s, e) => ((UIElement)s).OnPreviewDrop(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.DropEvent, new DragEventHandler((s, e) => ((UIElement)s).OnDrop(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.GiveFeedbackEvent, new GiveFeedbackEventHandler((s, e) => ((UIElement)s).OnGiveFeedback(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), DragDrop.QueryContinueDragEvent, new QueryContinueDragEventHandler((s, e) => ((UIElement)s).OnQueryContinueDrag(e)));
    }

    private static void OnPreviewMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnPreviewMouseDown(e);

        if (!e.Handled)
        {
            if (e.ChangedButton == MouseButton.Left)
                element.OnPreviewMouseLeftButtonDown(e);
            else if (e.ChangedButton == MouseButton.Right)
                element.OnPreviewMouseRightButtonDown(e);
        }
    }

    private static void OnMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnMouseDown(e);

        if (!e.Handled)
        {
            if (e.ChangedButton == MouseButton.Left)
                element.OnMouseLeftButtonDown(e);
            else if (e.ChangedButton == MouseButton.Right)
                element.OnMouseRightButtonDown(e);
        }
    }

    private static void OnPreviewMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnPreviewMouseUp(e);

        if (!e.Handled)
        {
            if (e.ChangedButton == MouseButton.Left)
                element.OnPreviewMouseLeftButtonUp(e);
            else if (e.ChangedButton == MouseButton.Right)
                element.OnPreviewMouseRightButtonUp(e);
        }
    }

    private static void OnMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnMouseUp(e);

        if (!e.Handled)
        {
            if (e.ChangedButton == MouseButton.Left)
                element.OnMouseLeftButtonUp(e);
            else if (e.ChangedButton == MouseButton.Right)
                element.OnMouseRightButtonUp(e);
        }
    }

    #endregion

    #region Event Handlers

    private Dictionary<RoutedEvent, List<RoutedEventHandlerInfo>>? _eventHandlers;
    private StylusPlugInCollection? _stylusPlugIns;

    /// <summary>
    /// Gets the stylus plug-ins attached to this element.
    /// </summary>
    public StylusPlugInCollection StylusPlugIns => GetStylusPlugIns(createIfMissing: true)!;

    internal StylusPlugInCollection? GetStylusPlugIns(bool createIfMissing)
    {
        if (_stylusPlugIns == null && createIfMissing)
        {
            _stylusPlugIns = new StylusPlugInCollection(this);
        }

        return _stylusPlugIns;
    }

    /// <summary>
    /// Adds a handler for the specified routed event.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The event handler.</param>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler)
    {
        AddHandler(routedEvent, handler, handledEventsToo: false);
    }

    /// <summary>
    /// Adds a handler for the specified routed event.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The event handler.</param>
    /// <param name="handledEventsToo">Whether to invoke the handler even if the event is already handled.</param>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        _eventHandlers ??= new Dictionary<RoutedEvent, List<RoutedEventHandlerInfo>>();

        if (!_eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            handlers = new List<RoutedEventHandlerInfo>();
            _eventHandlers[routedEvent] = handlers;
        }

        handlers.Add(new RoutedEventHandlerInfo(handler, handledEventsToo));
    }

    /// <summary>
    /// Removes a handler for the specified routed event.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The event handler to remove.</param>
    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        if (_eventHandlers == null || !_eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            return;
        }

        for (int i = handlers.Count - 1; i >= 0; i--)
        {
            if (handlers[i].Handler == handler)
            {
                handlers.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Raises the specified routed event.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    public void RaiseEvent(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(e.RoutedEvent);

        e.SetOriginalSource(this);
        e.Source ??= this;

        switch (e.RoutedEvent.RoutingStrategy)
        {
            case RoutingStrategy.Direct:
                RaiseEventDirect(e);
                break;

            case RoutingStrategy.Bubble:
                RaiseEventBubble(e);
                break;

            case RoutingStrategy.Tunnel:
                RaiseEventTunnel(e);
                break;
        }
    }

    private void RaiseEventDirect(RoutedEventArgs e)
    {
        InvokeHandlers(this, e);
    }

    private void RaiseEventBubble(RoutedEventArgs e)
    {
        UIElement? current = this;
        while (current != null)
        {
            current.InvokeHandlers(current, e);
            current = current.VisualParent as UIElement;
        }
    }

    // Reusable list for tunnel event path to avoid allocations on every mouse move.
    // Uses _tunnelDepth to detect reentrant tunnel events and allocate a fresh list.
    [ThreadStatic]
    private static List<UIElement>? _tunnelPath;
    [ThreadStatic]
    private static int _tunnelDepth;

    private void RaiseEventTunnel(RoutedEventArgs e)
    {
        List<UIElement> path;
        if (_tunnelDepth == 0)
        {
            // Top-level tunnel: reuse the static list
            _tunnelPath ??= new List<UIElement>(32);
            path = _tunnelPath;
            path.Clear();
        }
        else
        {
            // Reentrant tunnel (handler triggered another tunnel event):
            // allocate a fresh list to avoid corrupting the outer iteration
            path = new List<UIElement>(32);
        }

        // Build the path from root to source
        UIElement? current = this;
        while (current != null)
        {
            path.Add(current);
            current = current.VisualParent as UIElement;
        }

        // Tunnel from root to source
        _tunnelDepth++;
        try
        {
            for (int i = path.Count - 1; i >= 0; i--)
            {
                path[i].InvokeHandlers(path[i], e);
            }
        }
        finally
        {
            _tunnelDepth--;
        }
    }

    private void InvokeHandlers(object sender, RoutedEventArgs e)
    {
        var routedEvent = e.RoutedEvent!;

        // Invoke class handlers first
        foreach (var classHandler in EventManager.GetClassHandlers(routedEvent, GetType()))
        {
            if (!e.Handled || classHandler.HandledEventsToo)
            {
                e.InvokeEventHandler(classHandler.Handler, sender);
            }
        }

        // Invoke instance handlers (snapshot to allow handler list modification during dispatch)
        if (_eventHandlers != null && _eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                if (!e.Handled || handler.HandledEventsToo)
                {
                    e.InvokeEventHandler(handler.Handler, sender);
                }
            }
        }

        // Check CommandBindings for RoutedCommand events
        if (_commandBindings != null && _commandBindings.Count > 0)
        {
            if (routedEvent == Input.RoutedCommand.CanExecuteEvent && e is Input.CanExecuteRoutedEventArgs canExecArgs)
            {
                foreach (var binding in _commandBindings)
                {
                    if (binding.Command == canExecArgs.Command)
                    {
                        binding.OnCanExecute(sender, canExecArgs);
                        if (canExecArgs.Handled) break;
                    }
                }
            }
            else if (routedEvent == Input.RoutedCommand.ExecutedEvent && e is Input.ExecutedRoutedEventArgs execArgs)
            {
                foreach (var binding in _commandBindings)
                {
                    if (binding.Command == execArgs.Command)
                    {
                        binding.OnExecuted(sender, execArgs);
                        if (execArgs.Handled) break;
                    }
                }
            }
            else if (routedEvent == Input.RoutedCommand.PreviewCanExecuteEvent && e is Input.CanExecuteRoutedEventArgs previewCanExecArgs)
            {
                foreach (var binding in _commandBindings)
                {
                    if (binding.Command == previewCanExecArgs.Command)
                    {
                        binding.OnPreviewCanExecute(sender, previewCanExecArgs);
                        if (previewCanExecArgs.Handled) break;
                    }
                }
            }
            else if (routedEvent == Input.RoutedCommand.PreviewExecutedEvent && e is Input.ExecutedRoutedEventArgs previewExecArgs)
            {
                foreach (var binding in _commandBindings)
                {
                    if (binding.Command == previewExecArgs.Command)
                    {
                        binding.OnPreviewExecuted(sender, previewExecArgs);
                        if (previewExecArgs.Handled) break;
                    }
                }
            }
        }
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Visibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VisibilityProperty =
        DependencyProperty.Register(nameof(Visibility), typeof(Visibility), typeof(UIElement),
            new PropertyMetadata(Visibility.Visible, OnVisibilityChanged));

    /// <summary>
    /// Identifies the IsEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register(nameof(IsEnabled), typeof(bool), typeof(UIElement),
            new PropertyMetadata(true, OnIsEnabledChanged));

    /// <summary>
    /// Identifies the IsHitTestVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHitTestVisibleProperty =
        DependencyProperty.Register(nameof(IsHitTestVisible), typeof(bool), typeof(UIElement),
            new PropertyMetadata(true, OnIsHitTestVisibleChanged));

    /// <summary>
    /// Identifies the Opacity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OpacityProperty =
        DependencyProperty.Register(nameof(Opacity), typeof(double), typeof(UIElement),
            new PropertyMetadata(1.0, OnRenderPropertyChanged));

    /// <summary>
    /// Identifies the BackdropEffect dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BackdropEffectProperty =
        DependencyProperty.Register(nameof(BackdropEffect), typeof(IBackdropEffect), typeof(UIElement),
            new PropertyMetadata(null, OnBackdropEffectChanged));

    /// <summary>
    /// Identifies the Effect dependency property.
    /// This is for element-level bitmap effects like DropShadowEffect, distinct from BackdropEffect.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EffectProperty =
        DependencyProperty.Register(nameof(Effect), typeof(object), typeof(UIElement),
            new PropertyMetadata(null, OnEffectChanged));

    /// <summary>
    /// Identifies the OpacityMask dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OpacityMaskProperty =
        DependencyProperty.Register(nameof(OpacityMask), typeof(object), typeof(UIElement),
            new PropertyMetadata(null, OnOpacityMaskChanged));

    /// <summary>
    /// Identifies the RenderTransform dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RenderTransformProperty =
        DependencyProperty.Register(nameof(RenderTransform), typeof(object), typeof(UIElement),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    /// <summary>
    /// Identifies the RenderTransformOrigin dependency property.
    /// The origin is specified as a normalized point (0-1 range) relative to the element's render size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RenderTransformOriginProperty =
        DependencyProperty.Register(nameof(RenderTransformOrigin), typeof(Point), typeof(UIElement),
            new PropertyMetadata(new Point(0, 0), OnRenderPropertyChanged));

    /// <summary>
    /// Identifies the Focusable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty FocusableProperty =
        DependencyProperty.Register(nameof(Focusable), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsManipulationEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsManipulationEnabledProperty =
        DependencyProperty.Register(nameof(IsManipulationEnabled), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsMouseOver read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsMouseOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseOver), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnIsMouseOverChanged));

    /// <summary>
    /// Identifies the IsMouseOver dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsMouseOverProperty = IsMouseOverPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsPressed read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsPressedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsPressed), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnIsPressedChanged));

    /// <summary>
    /// Identifies the IsPressed dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPressedProperty = IsPressedPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsFocused read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFocused), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnIsFocusedChanged));

    /// <summary>
    /// Identifies the IsKeyboardFocused read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsKeyboardFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocused), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnIsKeyboardFocusedPropertyChanged));

    /// <summary>
    /// Identifies the IsKeyboardFocused dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsKeyboardFocusedProperty = IsKeyboardFocusedPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsKeyboardFocusWithin read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsKeyboardFocusWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocusWithin), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnIsKeyboardFocusWithinPropertyChanged));

    /// <summary>
    /// Identifies the IsKeyboardFocusWithin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsKeyboardFocusWithinProperty = IsKeyboardFocusWithinPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsFocused dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsFocusedProperty = IsFocusedPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the ClipToBounds dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ClipToBoundsProperty =
        DependencyProperty.Register(nameof(ClipToBounds), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnClipToBoundsChanged));

    /// <summary>
    /// Identifies the Clip dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ClipProperty =
        DependencyProperty.Register(nameof(Clip), typeof(object), typeof(UIElement),
            new PropertyMetadata(null, OnRenderPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the visibility of this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public virtual Visibility Visibility
    {
        get => (Visibility)GetValue(VisibilityProperty)!;
        set => SetValue(VisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this element is enabled.
    /// The effective value considers the parent chain — if any ancestor is disabled,
    /// this element is also effectively disabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEnabled
    {
        get
        {
            var localValue = (bool)GetValue(IsEnabledProperty)!;
            if (!localValue) return false;
            // Check parent chain
            return VisualParent is not UIElement parent || parent.IsEnabled;
        }
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this element can participate in hit testing.
    /// The effective value considers the parent chain — if any ancestor is not hit-test visible,
    /// this element is also effectively not hit-test visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsHitTestVisible
    {
        get
        {
            var localValue = (bool)GetValue(IsHitTestVisibleProperty)!;
            if (!localValue) return false;
            return VisualParent is not UIElement parent || parent.IsHitTestVisible;
        }
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public virtual double Opacity
    {
        get => (double)GetValue(OpacityProperty)!;
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets the backdrop effect.
    /// Use implementations like BlurEffect, AcrylicEffect, MicaEffect, etc.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public IBackdropEffect? BackdropEffect
    {
        get => (IBackdropEffect?)GetValue(BackdropEffectProperty);
        set => SetValue(BackdropEffectProperty, value);
    }

    /// <summary>
    /// Gets or sets the bitmap effect applied to the element's rendered content.
    /// Use DropShadowEffect, ElementBlurEffect, etc. from Jalium.UI.Media.Effects.
    /// This is distinct from BackdropEffect which affects content behind the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public object? Effect
    {
        get => GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    /// <summary>
    /// Gets or sets a brush that specifies the opacity mask for this element.
    /// The alpha channel of the brush determines the opacity of corresponding parts of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public object? OpacityMask
    {
        get => GetValue(OpacityMaskProperty);
        set => SetValue(OpacityMaskProperty, value);
    }

    /// <summary>
    /// Gets or sets the render transform.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public object? RenderTransform
    {
        get => GetValue(RenderTransformProperty);
        set => SetValue(RenderTransformProperty, value);
    }

    /// <summary>
    /// Gets or sets the origin point for the render transform, relative to the element's bounds.
    /// Values are normalized (0-1), where (0.5, 0.5) is the center.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Point RenderTransformOrigin
    {
        get => (Point)GetValue(RenderTransformOriginProperty)!;
        set => SetValue(RenderTransformOriginProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether this element can receive focus.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool Focusable
    {
        get => (bool)GetValue(FocusableProperty)!;
        set => SetValue(FocusableProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether manipulation events are enabled for this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsManipulationEnabled
    {
        get => (bool)GetValue(IsManipulationEnabledProperty)!;
        set => SetValue(IsManipulationEnabledProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the mouse pointer is over this element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsMouseOver => (bool)GetValue(IsMouseOverProperty)!;

    /// <summary>
    /// Sets the IsMouseOver property value. Called internally by mouse tracking.
    /// </summary>
    internal void SetIsMouseOver(bool value)
    {
        SetValue(IsMouseOverPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether this element is currently pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>
    /// Sets the IsPressed property value. Called internally by input state tracking.
    /// </summary>
    internal void SetIsPressed(bool value)
    {
        SetValue(IsPressedPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether to clip the content of this element to its bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public bool ClipToBounds
    {
        get => (bool)GetValue(ClipToBoundsProperty)!;
        set => SetValue(ClipToBoundsProperty, value);
    }

    /// <summary>
    /// Gets or sets the geometry used to define the outline of the contents of an element.
    /// The Clip geometry is applied to the element's rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public object? Clip
    {
        get => GetValue(ClipProperty);
        set => SetValue(ClipProperty, value);
    }

    #endregion

    #region Focus

    /// <summary>
    /// Gets a value indicating whether this element has keyboard focus.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsKeyboardFocused => (bool)GetValue(IsKeyboardFocusedProperty)!;

    /// <summary>
    /// Gets a value indicating whether keyboard focus is anywhere within this element or its visual subtree.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsKeyboardFocusWithin => (bool)GetValue(IsKeyboardFocusWithinProperty)!;

    /// <summary>
    /// Gets a value indicating whether this element has logical focus.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsFocused => (bool)GetValue(IsFocusedProperty)!;

    /// <summary>
    /// Sets the IsFocused property value. Called internally by focus tracking.
    /// </summary>
    internal void SetIsFocused(bool value)
    {
        SetValue(IsFocusedPropertyKey.DependencyProperty, value);
    }

    /// <summary>
    /// Attempts to set focus to this element.
    /// </summary>
    /// <returns>True if focus was successfully set; otherwise, false.</returns>
    public bool Focus()
    {
        if (!Focusable || !IsEnabled || Visibility != Visibility.Visible)
        {
            return false;
        }

        var result = FocusService.Focus(this);
        return result == this;
    }

    /// <summary>
    /// Moves focus from this element.
    /// </summary>
    /// <param name="direction">The direction to move focus.</param>
    /// <returns>True if focus was successfully moved; otherwise, false.</returns>
    public bool MoveFocus(FocusNavigationDirection direction)
    {
        return FocusService.MoveFocus(this, direction);
    }

    /// <summary>
    /// Updates the IsKeyboardFocused state. Called by the Keyboard class.
    /// </summary>
    internal void UpdateIsKeyboardFocused(bool isFocused)
    {
        if (IsKeyboardFocused != isFocused)
        {
            SetValue(IsKeyboardFocusedPropertyKey.DependencyProperty, isFocused);
        }
    }

    /// <summary>
    /// Updates the IsKeyboardFocusWithin state. Called by the Keyboard class.
    /// </summary>
    internal void UpdateIsKeyboardFocusWithin(bool isFocusWithin)
    {
        if (IsKeyboardFocusWithin != isFocusWithin)
        {
            SetValue(IsKeyboardFocusWithinPropertyKey.DependencyProperty, isFocusWithin);
        }
    }

    /// <summary>
    /// Called when the IsKeyboardFocused property changes.
    /// </summary>
    protected virtual void OnIsKeyboardFocusedChanged(bool isFocused)
    {
    }

    /// <summary>
    /// Called when the IsKeyboardFocusWithin property changes.
    /// </summary>
    protected virtual void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
    }

    #endregion

    #region Keyboard Focus Events

    /// <summary>
    /// Identifies the PreviewGotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent =
        FocusService.PreviewGotKeyboardFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the GotKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotKeyboardFocusEvent =
        FocusService.GotKeyboardFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewLostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent =
        FocusService.PreviewLostKeyboardFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the LostKeyboardFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostKeyboardFocusEvent =
        FocusService.LostKeyboardFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the GotFocus routed event.
    /// </summary>
    public static readonly RoutedEvent GotFocusEvent =
        FocusService.GotFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the LostFocus routed event.
    /// </summary>
    public static readonly RoutedEvent LostFocusEvent =
        FocusService.LostFocusEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Occurs when keyboard focus is received (tunnel).
    /// </summary>
    public event KeyboardFocusChangedEventHandler PreviewGotKeyboardFocus
    {
        add => AddHandler(PreviewGotKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewGotKeyboardFocusEvent, value);
    }

    /// <summary>
    /// Occurs when keyboard focus is received (bubble).
    /// </summary>
    public event KeyboardFocusChangedEventHandler GotKeyboardFocus
    {
        add => AddHandler(GotKeyboardFocusEvent, value);
        remove => RemoveHandler(GotKeyboardFocusEvent, value);
    }

    /// <summary>
    /// Occurs when keyboard focus is lost (tunnel).
    /// </summary>
    public event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus
    {
        add => AddHandler(PreviewLostKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewLostKeyboardFocusEvent, value);
    }

    /// <summary>
    /// Occurs when keyboard focus is lost (bubble).
    /// </summary>
    public event KeyboardFocusChangedEventHandler LostKeyboardFocus
    {
        add => AddHandler(LostKeyboardFocusEvent, value);
        remove => RemoveHandler(LostKeyboardFocusEvent, value);
    }

    /// <summary>
    /// Occurs when this element receives logical focus.
    /// </summary>
    public event RoutedEventHandler GotFocus
    {
        add => AddHandler(GotFocusEvent, value);
        remove => RemoveHandler(GotFocusEvent, value);
    }

    /// <summary>
    /// Occurs when this element loses logical focus.
    /// </summary>
    public event RoutedEventHandler LostFocus
    {
        add => AddHandler(LostFocusEvent, value);
        remove => RemoveHandler(LostFocusEvent, value);
    }

    #endregion

    #region Layout

    private Size _desiredSize;
    /// <summary>
    /// Protected so FrameworkElement.ArrangeCore can update before firing SizeChanged.
    /// </summary>
    protected Size _renderSize;
    private bool _isMeasureValid;
    private bool _isArrangeValid;
    private Size _previousAvailableSize;
    private Rect _previousFinalRect;
    private IWindowHost? _cachedWindowHost;
    private LayoutManager? _cachedLayoutManager;
    private Point _cachedScreenOffset;
    private bool _isScreenOffsetValid;

    /// <summary>
    /// Gets the desired size computed during the measure pass.
    /// </summary>
    public Size DesiredSize => _desiredSize;

    /// <summary>
    /// Gets the final render size after arrangement.
    /// </summary>
    public Size RenderSize => _renderSize;

    /// <summary>
    /// Gets the visual bounds of this element in parent coordinates.
    /// Override in FrameworkElement to provide actual bounds.
    /// </summary>
    public virtual Rect VisualBounds => new Rect(0, 0, _renderSize.Width, _renderSize.Height);

    /// <summary>
    /// Gets or sets a visual-only translation offset applied during rendering.
    /// Does not affect layout — used for animation effects (e.g., cloth draping).
    /// </summary>
    internal Point RenderOffset { get; set; }

    /// <summary>
    /// Returns a geometry for clipping the contents of this element.
    /// Override in derived classes to provide custom clipping (e.g., ScrollViewer).
    /// When ClipToBounds is true, returns a Rect matching the element's RenderSize.
    /// </summary>
    /// <returns>The clipping geometry (Media.Geometry or Rect), or null if no clipping should be applied.</returns>
    internal virtual object? GetLayoutClip()
    {
        // Explicit Clip geometry takes precedence
        var clip = Clip;
        if (clip != null)
            return clip;

        if (ClipToBounds)
        {
            return new Rect(0, 0, _renderSize.Width, _renderSize.Height);
        }
        return null;
    }

    /// <summary>
    /// Gets a value indicating whether the measure pass is valid.
    /// </summary>
    public bool IsMeasureValid => _isMeasureValid;

    /// <summary>
    /// Gets a value indicating whether the arrange pass is valid.
    /// </summary>
    public bool IsArrangeValid => _isArrangeValid;

    /// <summary>
    /// Gets the previous available size used for measurement (used by LayoutManager).
    /// </summary>
    internal Size PreviousAvailableSize => _previousAvailableSize;

    /// <summary>
    /// Gets the previous final rect used for arrangement (used by LayoutManager).
    /// </summary>
    internal Rect PreviousFinalRect => _previousFinalRect;

    /// <summary>
    /// Marks measure as invalid without triggering LayoutManager notification.
    /// Used by LayoutManager's upward propagation.
    /// </summary>
    internal void MarkMeasureInvalid()
    {
        _isMeasureValid = false;
        _isArrangeValid = false;
    }

    /// <summary>
    /// Marks arrange as invalid without triggering LayoutManager notification.
    /// Used by LayoutManager's upward propagation.
    /// </summary>
    internal void MarkArrangeInvalid()
    {
        _isArrangeValid = false;
    }

    /// <summary>
    /// Invalidates the measure pass for this element.
    /// </summary>
    public void InvalidateMeasure()
    {
        var wasValid = _isMeasureValid;
        _isMeasureValid = false;
        _isArrangeValid = false;
        var layoutManager = FindLayoutManager();

        if (wasValid)
        {
            // First invalidation: notify LayoutManager and mark parent dirty
            layoutManager?.InvalidateMeasure(this);
            InvalidateLayoutVisual();
        }
        else
        {
            // Already invalid. If this element became invalid while detached,
            // it may not be in the current LayoutManager queue yet.
            // Re-queue is idempotent (LayoutManager uses HashSet).
            layoutManager?.InvalidateMeasure(this);
            GetWindowHost()?.InvalidateWindow();
        }
    }

    /// <summary>
    /// Invalidates the arrange pass for this element.
    /// </summary>
    public void InvalidateArrange()
    {
        var wasValid = _isArrangeValid;
        _isArrangeValid = false;
        var layoutManager = FindLayoutManager();

        if (wasValid)
        {
            // First invalidation: notify LayoutManager and mark parent dirty
            layoutManager?.InvalidateArrange(this);
            InvalidateLayoutVisual();
        }
        else
        {
            // Same rationale as InvalidateMeasure: keep queue membership correct
            // when transitioning from detached -> attached while already invalid.
            layoutManager?.InvalidateArrange(this);
            GetWindowHost()?.InvalidateWindow();
        }
    }

    /// <summary>
    /// Requests a full window repaint for layout changes.
    /// Layout changes (measure/arrange) can move elements arbitrarily,
    /// so dirty rects for individual elements are unreliable.
    /// Marks this element dirty so its region is repainted.
    /// </summary>
    private void InvalidateLayoutVisual()
    {
        var windowHost = GetWindowHost();
        if (windowHost == null) return;

        windowHost.AddDirtyElement(this);
        windowHost.InvalidateWindow();
    }

    /// <summary>
    /// Invalidates the visual rendering of this element.
    /// Submits this element's screen bounds as a dirty rect for partial redraw.
    /// </summary>
    public void InvalidateVisual()
    {
        SetRenderDirty();

        var windowHost = GetWindowHost();
        if (windowHost != null)
        {
            windowHost.AddDirtyElement(this);
            windowHost.InvalidateWindow();
        }
    }

    /// <summary>
    /// Gets the cached IWindowHost by walking up the tree (lazy, cached).
    /// </summary>
    private IWindowHost? GetWindowHost()
    {
        if (_cachedWindowHost != null)
            return _cachedWindowHost;

        Visual? current = this;
        while (current != null)
        {
            if (current is IWindowHost host)
            {
                _cachedWindowHost = host;
                return host;
            }
            current = current.VisualParent;
        }
        return null;
    }

    /// <summary>
    /// Gets the cached LayoutManager by walking up to the ILayoutManagerHost (lazy, cached).
    /// </summary>
    private LayoutManager? FindLayoutManager()
    {
        if (_cachedLayoutManager != null)
            return _cachedLayoutManager;

        Visual? current = this;
        while (current != null)
        {
            if (current is ILayoutManagerHost host)
            {
                _cachedLayoutManager = host.LayoutManager;
                return _cachedLayoutManager;
            }
            current = current.VisualParent;
        }
        return null;
    }

    /// <summary>
    /// Invalidates cached host references. Called when visual parent changes.
    /// </summary>
    internal void InvalidateHostCaches()
    {
        _cachedWindowHost = null;
        _cachedLayoutManager = null;
        InvalidateScreenOffsetCacheRecursive();

        // Recursively invalidate children's host caches too
        var count = VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (GetVisualChild(i) is UIElement uiChild)
                uiChild.InvalidateHostCaches();
        }
    }

    internal void InvalidateScreenOffsetCacheRecursive()
    {
        _isScreenOffsetValid = false;

        var count = VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (GetVisualChild(i) is UIElement uiChild)
                uiChild.InvalidateScreenOffsetCacheRecursive();
        }
    }

    /// <summary>
    /// Gets the screen-space bounds of this element relative to its Window.
    /// Uses cached layout offset (O(1)) plus current RenderOffset (not cached,
    /// since RenderOffset changes during animations without triggering layout).
    /// </summary>
    internal Rect GetScreenBounds()
    {
        if (!_isScreenOffsetValid)
        {
            double x = 0, y = 0;
            Visual? current = this;
            while (current != null)
            {
                if (current is IWindowHost)
                    break;
                if (current is UIElement ui)
                {
                    var vb = ui.VisualBounds;
                    x += vb.X;
                    y += vb.Y;
                }
                current = current.VisualParent;
            }
            _cachedScreenOffset = new Point(x, y);
            _isScreenOffsetValid = true;
        }

        // Include RenderOffset — animation systems (ProgressBar indeterminate,
        // spring physics, etc.) move elements via RenderOffset without triggering
        // layout. Without this, the dirty region wouldn't cover the actual
        // rendered position, causing ghost images during animation.
        var ro = RenderOffset;
        return new Rect(_cachedScreenOffset.X + ro.X, _cachedScreenOffset.Y + ro.Y,
                        _renderSize.Width, _renderSize.Height);
    }

    /// <summary>
    /// Updates the desired size of this element.
    /// </summary>
    /// <param name="availableSize">The available size for the element.</param>
    public void Measure(Size availableSize)
    {
        if (Visibility == Visibility.Collapsed)
        {
            _desiredSize = Size.Empty;
            _isMeasureValid = true;
            return;
        }

        // Short-circuit: if measure is already valid and constraints haven't changed, skip
        if (_isMeasureValid && _previousAvailableSize == availableSize)
            return;

        var oldDesiredSize = _desiredSize;
        _previousAvailableSize = availableSize;
        _desiredSize = MeasureCore(availableSize);
        _isMeasureValid = true;

        // If desired size changed, parent needs to re-arrange (mark parent dirty)
        if (_desiredSize != oldDesiredSize)
        {
            if (VisualParent is UIElement parent)
            {
                parent.SetRenderDirty();
            }
        }
    }

    /// <summary>
    /// Positions child elements and determines a size for this element.
    /// </summary>
    /// <param name="finalRect">The final area for this element.</param>
    public void Arrange(Rect finalRect)
    {
        if (Visibility == Visibility.Collapsed)
        {
            _renderSize = Size.Empty;
            _isArrangeValid = true;
            return;
        }

        // Short-circuit: if arrange is already valid and final rect hasn't changed, skip
        if (_isArrangeValid && _previousFinalRect == finalRect)
            return;

        var oldRenderSize = _renderSize;
        _previousFinalRect = finalRect;
        InvalidateScreenOffsetCacheRecursive();
        _renderSize = ArrangeCore(finalRect);
        _isArrangeValid = true;

        // If render size changed, mark this element as needing re-render
        if (_renderSize != oldRenderSize)
        {
            SetRenderDirty();
        }
    }

    /// <summary>
    /// Override to implement custom measure logic.
    /// </summary>
    /// <param name="availableSize">The available size.</param>
    /// <returns>The desired size.</returns>
    protected virtual Size MeasureCore(Size availableSize)
    {
        return Size.Empty;
    }

    /// <summary>
    /// Override to implement custom arrange logic.
    /// </summary>
    /// <param name="finalRect">The final rectangle.</param>
    /// <returns>The render size.</returns>
    protected virtual Size ArrangeCore(Rect finalRect)
    {
        return new Size(finalRect.Width, finalRect.Height);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateMeasure();
        }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var oldValue = (bool)(e.OldValue ?? true);
            var newValue = (bool)(e.NewValue ?? true);
            element.OnIsEnabledChanged(oldValue, newValue);
            // Propagate effective IsEnabled change to descendants
            element.PropagateIsEnabledToDescendants();

            // Notify UIA of IsEnabled property change
            var peer = element._automationPeer;
            if (peer != null)
                peer.RaisePropertyChangedEvent(Automation.AutomationProperty.IsEnabledProperty, oldValue, newValue);
        }
    }

    private static void OnIsHitTestVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsHitTestVisibleChanged((bool)(e.OldValue ?? true), (bool)(e.NewValue ?? true));
            element.PropagateIsHitTestVisibleToDescendants();
        }
    }

    /// <summary>
    /// Called when the IsEnabled property changes.
    /// </summary>
    protected virtual void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
    }

    private void PropagateIsEnabledToDescendants()
    {
        for (int i = 0; i < VisualChildrenCount; i++)
        {
            if (GetVisualChild(i) is UIElement child)
            {
                child.InvalidateVisual();
                child.OnIsEnabledChanged(true, child.IsEnabled);
                child.PropagateIsEnabledToDescendants();
            }
        }
    }

    /// <summary>
    /// Called when the IsHitTestVisible property changes.
    /// </summary>
    protected virtual void OnIsHitTestVisibleChanged(bool oldValue, bool newValue)
    {
    }

    private void PropagateIsHitTestVisibleToDescendants()
    {
        for (int i = 0; i < VisualChildrenCount; i++)
        {
            if (GetVisualChild(i) is UIElement child)
            {
                child.InvalidateVisual();
                child.OnIsHitTestVisibleChanged(true, child.IsHitTestVisible);
                child.PropagateIsHitTestVisibleToDescendants();
            }
        }
    }

    private static void OnIsMouseOverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsMouseOverChanged((bool)(e.OldValue ?? false), (bool)(e.NewValue ?? false));
        }
    }

    private static void OnIsPressedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsPressedChanged((bool)(e.OldValue ?? false), (bool)(e.NewValue ?? false));
        }
    }

    private static void OnIsFocusedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsFocusedChanged((bool)(e.OldValue ?? false), (bool)(e.NewValue ?? false));
        }
    }

    private static void OnIsKeyboardFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var isFocused = (bool)(e.NewValue ?? false);
            if (element.IsFocused != isFocused)
            {
                element.SetIsFocused(isFocused);
            }

            element.OnIsKeyboardFocusedChanged(isFocused);
            element.InvalidateVisual();

            // Notify UIA of focus change
            if (isFocused)
            {
                var peer = element.GetAutomationPeer();
                if (peer != null)
                    Automation.AutomationPeer.EventSink?.OnFocusChanged(peer);
            }
        }
    }

    private static void OnIsKeyboardFocusWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsKeyboardFocusWithinChanged((bool)(e.NewValue ?? false));
        }
    }

    /// <summary>
    /// Called when the IsMouseOver property changes.
    /// </summary>
    protected virtual void OnIsMouseOverChanged(bool oldValue, bool newValue)
    {
    }

    /// <summary>
    /// Called when the IsPressed property changes.
    /// </summary>
    protected virtual void OnIsPressedChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Called when the IsFocused property changes.
    /// </summary>
    protected virtual void OnIsFocusedChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
    }

    private static void OnBackdropEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnBackdropEffectChanged((IBackdropEffect?)e.OldValue, (IBackdropEffect?)e.NewValue);
        }
    }

    /// <summary>
    /// Called when the BackdropEffect property changes.
    /// </summary>
    protected virtual void OnBackdropEffectChanged(IBackdropEffect? oldValue, IBackdropEffect? newValue)
    {
        InvalidateVisual();
    }

    private static void OnEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnEffectChanged(e.OldValue, e.NewValue);
        }
    }

    /// <summary>
    /// Called when the Effect property changes.
    /// </summary>
    protected virtual void OnEffectChanged(object? oldValue, object? newValue)
    {
        // Unsubscribe from old effect changes
        if (oldValue is IEffect oldEffect)
        {
            oldEffect.EffectChanged -= OnEffectPropertyChanged;
        }

        // Subscribe to new effect changes
        if (newValue is IEffect newEffect)
        {
            newEffect.EffectChanged += OnEffectPropertyChanged;
        }

        InvalidateVisual();
    }

    private void OnEffectPropertyChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private static void OnOpacityMaskChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
    }

    private static void OnClipToBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
    }

    /// <summary>
    /// Generic callback for render-affecting properties (e.g., Opacity).
    /// </summary>
    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
    }

    #endregion

    #region Mouse Capture

    private static UIElement? _mouseCaptured;

    /// <summary>
    /// Gets the element that currently has mouse capture.
    /// </summary>
    public static UIElement? MouseCapturedElement => _mouseCaptured;

    /// <summary>
    /// Gets a value indicating whether this element has captured the mouse.
    /// </summary>
    public bool IsMouseCaptured => _mouseCaptured == this;

    /// <summary>
    /// Gets a value indicating whether the mouse is captured to this element or any of its child elements.
    /// </summary>
    public bool IsMouseCaptureWithin
    {
        get
        {
            var captured = _mouseCaptured;
            if (captured == null) return false;
            if (captured == this) return true;

            // Check if captured element is a descendant
            Visual? current = captured;
            while (current != null)
            {
                if (current == this) return true;
                current = current.VisualParent;
            }
            return false;
        }
    }

    /// <summary>
    /// Captures the mouse to this element.
    /// </summary>
    /// <returns>True if capture was successful; otherwise, false.</returns>
    public bool CaptureMouse()
    {
        if (!IsEnabled || Visibility != Visibility.Visible)
        {
            return false;
        }

        var previousCaptured = _mouseCaptured;
        _mouseCaptured = this;

        // Tell Win32 to keep sending mouse messages even when cursor is outside the window
        GetWindowHost()?.SetNativeCapture();

        // Notify the previously captured element
        if (previousCaptured != null && previousCaptured != this)
        {
            previousCaptured.RaiseMouseCaptureChanged(false);
        }

        // Notify the newly captured element
        RaiseMouseCaptureChanged(true);
        return true;
    }

    /// <summary>
    /// Releases mouse capture from this element.
    /// </summary>
    public void ReleaseMouseCapture()
    {
        if (_mouseCaptured == this)
        {
            var windowHost = GetWindowHost();
            _mouseCaptured = null;
            windowHost?.ReleaseNativeCapture();
            RaiseMouseCaptureChanged(false);
        }
    }

    /// <summary>
    /// Forces release of mouse capture from any element.
    /// </summary>
    internal static void ForceReleaseMouseCapture()
    {
        var captured = _mouseCaptured;
        if (captured != null)
        {
            var windowHost = captured.GetWindowHost();
            _mouseCaptured = null;
            windowHost?.ReleaseNativeCapture();
            captured.RaiseMouseCaptureChanged(false);
        }
    }

    /// <summary>
    /// Clears managed mouse capture state without calling Win32 ReleaseCapture.
    /// Used when WM_CAPTURECHANGED arrives (native capture already lost).
    /// </summary>
    internal static void OnNativeCaptureChanged()
    {
        var captured = _mouseCaptured;
        if (captured != null)
        {
            _mouseCaptured = null;
            captured.RaiseMouseCaptureChanged(false);
        }
    }

    /// <summary>
    /// Identifies the GotMouseCapture routed event.
    /// </summary>
    public static readonly RoutedEvent GotMouseCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(GotMouseCapture), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the LostMouseCapture routed event.
    /// </summary>
    public static readonly RoutedEvent LostMouseCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(LostMouseCapture), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Occurs when this element captures the mouse.
    /// </summary>
    public event RoutedEventHandler GotMouseCapture
    {
        add => AddHandler(GotMouseCaptureEvent, value);
        remove => RemoveHandler(GotMouseCaptureEvent, value);
    }

    /// <summary>
    /// Occurs when this element loses mouse capture.
    /// </summary>
    public event RoutedEventHandler LostMouseCapture
    {
        add => AddHandler(LostMouseCaptureEvent, value);
        remove => RemoveHandler(LostMouseCaptureEvent, value);
    }

    /// <summary>
    /// Called when mouse capture changes for this element.
    /// </summary>
    /// <param name="captured">True if this element now has capture; false if it lost capture.</param>
    internal void RaiseMouseCaptureChanged(bool captured)
    {
        if (captured)
        {
            OnGotMouseCapture();
            var args = new RoutedEventArgs(GotMouseCaptureEvent, this);
            RaiseEvent(args);
        }
        else
        {
            OnLostMouseCapture();
            var args = new RoutedEventArgs(LostMouseCaptureEvent, this);
            RaiseEvent(args);
        }
    }

    /// <summary>
    /// Called when this element captures the mouse.
    /// </summary>
    protected virtual void OnGotMouseCapture()
    {
    }

    /// <summary>
    /// Called when this element loses mouse capture.
    /// </summary>
    protected virtual void OnLostMouseCapture()
    {
    }

    #endregion

    #region Routed Input Events

    /// <summary>
    /// Identifies the PreviewKeyDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewKeyDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewKeyDown), RoutingStrategy.Tunnel, typeof(KeyEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the KeyDown routed event.
    /// </summary>
    public static readonly RoutedEvent KeyDownEvent =
        EventManager.RegisterRoutedEvent(nameof(KeyDown), RoutingStrategy.Bubble, typeof(KeyEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewKeyUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewKeyUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewKeyUp), RoutingStrategy.Tunnel, typeof(KeyEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the KeyUp routed event.
    /// </summary>
    public static readonly RoutedEvent KeyUpEvent =
        EventManager.RegisterRoutedEvent(nameof(KeyUp), RoutingStrategy.Bubble, typeof(KeyEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTextInput routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTextInputEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTextInput), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TextInput routed event.
    /// </summary>
    public static readonly RoutedEvent TextInputEvent =
        EventManager.RegisterRoutedEvent(nameof(TextInput), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseDown), RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseDown routed event.
    /// </summary>
    public static readonly RoutedEvent MouseDownEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseDown), RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseUp), RoutingStrategy.Tunnel, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseUp routed event.
    /// </summary>
    public static readonly RoutedEvent MouseUpEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseUp), RoutingStrategy.Bubble, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseMove), RoutingStrategy.Tunnel, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseMove routed event.
    /// </summary>
    public static readonly RoutedEvent MouseMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseMove), RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseEnter routed event.
    /// </summary>
    public static readonly RoutedEvent MouseEnterEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseEnter), RoutingStrategy.Direct, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseLeave routed event.
    /// </summary>
    public static readonly RoutedEvent MouseLeaveEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseLeave), RoutingStrategy.Direct, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseWheel routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseWheelEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseWheel), RoutingStrategy.Tunnel, typeof(MouseWheelEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseWheel routed event.
    /// </summary>
    public static readonly RoutedEvent MouseWheelEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseWheel), RoutingStrategy.Bubble, typeof(MouseWheelEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseLeftButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseLeftButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseLeftButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent MouseLeftButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseLeftButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseLeftButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseLeftButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseLeftButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent MouseLeftButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseLeftButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseRightButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseRightButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseRightButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent MouseRightButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseRightButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseRightButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseRightButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseRightButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent MouseRightButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseRightButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchDown), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchDown routed event.
    /// </summary>
    public static readonly RoutedEvent TouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchDown), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchMove), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchMove routed event.
    /// </summary>
    public static readonly RoutedEvent TouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchMove), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchUp), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchUp routed event.
    /// </summary>
    public static readonly RoutedEvent TouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchUp), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewStylusDown), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusDown routed event.
    /// </summary>
    public static readonly RoutedEvent StylusDownEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusDown), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewStylusMove), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusMove routed event.
    /// </summary>
    public static readonly RoutedEvent StylusMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusMove), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewStylusUp), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusUp routed event.
    /// </summary>
    public static readonly RoutedEvent StylusUpEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusUp), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusInAirMove routed event.
    /// </summary>
    public static readonly RoutedEvent StylusInAirMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusInAirMove), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusEnter routed event.
    /// </summary>
    public static readonly RoutedEvent StylusEnterEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusEnter), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusLeave routed event.
    /// </summary>
    public static readonly RoutedEvent StylusLeaveEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusLeave), RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusInRange routed event.
    /// </summary>
    public static readonly RoutedEvent StylusInRangeEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusInRange), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusOutOfRange routed event.
    /// </summary>
    public static readonly RoutedEvent StylusOutOfRangeEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusOutOfRange), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent StylusButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusButtonDown), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent StylusButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusButtonUp), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the StylusSystemGesture routed event.
    /// </summary>
    public static readonly RoutedEvent StylusSystemGestureEvent =
        EventManager.RegisterRoutedEvent(nameof(StylusSystemGesture), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewPointerDown), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerDown routed event.
    /// </summary>
    public static readonly RoutedEvent PointerDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerDown), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewPointerMove), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerMove routed event.
    /// </summary>
    public static readonly RoutedEvent PointerMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerMove), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewPointerUp), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerUp routed event.
    /// </summary>
    public static readonly RoutedEvent PointerUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerUp), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewPointerCancel routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewPointerCancelEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewPointerCancel), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerCancel routed event.
    /// </summary>
    public static readonly RoutedEvent PointerCancelEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerCancel), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerPressed routed event.
    /// </summary>
    public static readonly RoutedEvent PointerPressedEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerPressed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerMoved routed event.
    /// </summary>
    public static readonly RoutedEvent PointerMovedEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerMoved), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PointerReleased routed event.
    /// </summary>
    public static readonly RoutedEvent PointerReleasedEvent =
        EventManager.RegisterRoutedEvent(nameof(PointerReleased), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationStarting routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationStarting), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationStarting routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationStarting), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationStarted routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationStarted), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationStarted routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationStarted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationDelta routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationDeltaEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationDelta), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationDelta routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationDeltaEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationDelta), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationInertiaStarting routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationInertiaStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationInertiaStarting), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationInertiaStarting routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationInertiaStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationInertiaStarting), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationBoundaryFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationBoundaryFeedbackEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationBoundaryFeedback), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationBoundaryFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationBoundaryFeedbackEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationBoundaryFeedback), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationCompleted routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationCompletedEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationCompleted), RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationCompleted routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationCompletedEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationCompleted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(UIElement));

    /// <summary>
    /// Occurs when a key is pressed (tunnel).
    /// </summary>
    public event KeyEventHandler PreviewKeyDown
    {
        add => AddHandler(PreviewKeyDownEvent, value);
        remove => RemoveHandler(PreviewKeyDownEvent, value);
    }

    /// <summary>
    /// Occurs when a key is pressed (bubble).
    /// </summary>
    public event KeyEventHandler KeyDown
    {
        add => AddHandler(KeyDownEvent, value);
        remove => RemoveHandler(KeyDownEvent, value);
    }

    /// <summary>
    /// Occurs when a key is released (tunnel).
    /// </summary>
    public event KeyEventHandler PreviewKeyUp
    {
        add => AddHandler(PreviewKeyUpEvent, value);
        remove => RemoveHandler(PreviewKeyUpEvent, value);
    }

    /// <summary>
    /// Occurs when a key is released (bubble).
    /// </summary>
    public event KeyEventHandler KeyUp
    {
        add => AddHandler(KeyUpEvent, value);
        remove => RemoveHandler(KeyUpEvent, value);
    }

    /// <summary>
    /// Occurs when text is input (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewTextInput
    {
        add => AddHandler(PreviewTextInputEvent, value);
        remove => RemoveHandler(PreviewTextInputEvent, value);
    }

    /// <summary>
    /// Occurs when text is input (bubble).
    /// </summary>
    public event RoutedEventHandler TextInput
    {
        add => AddHandler(TextInputEvent, value);
        remove => RemoveHandler(TextInputEvent, value);
    }

    /// <summary>
    /// Occurs when a mouse button is pressed (tunnel).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseDown
    {
        add => AddHandler(PreviewMouseDownEvent, value);
        remove => RemoveHandler(PreviewMouseDownEvent, value);
    }

    /// <summary>
    /// Occurs when a mouse button is pressed (bubble).
    /// </summary>
    public event MouseButtonEventHandler MouseDown
    {
        add => AddHandler(MouseDownEvent, value);
        remove => RemoveHandler(MouseDownEvent, value);
    }

    /// <summary>
    /// Occurs when a mouse button is released (tunnel).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseUp
    {
        add => AddHandler(PreviewMouseUpEvent, value);
        remove => RemoveHandler(PreviewMouseUpEvent, value);
    }

    /// <summary>
    /// Occurs when a mouse button is released (bubble).
    /// </summary>
    public event MouseButtonEventHandler MouseUp
    {
        add => AddHandler(MouseUpEvent, value);
        remove => RemoveHandler(MouseUpEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse moves (tunnel).
    /// </summary>
    public event MouseEventHandler PreviewMouseMove
    {
        add => AddHandler(PreviewMouseMoveEvent, value);
        remove => RemoveHandler(PreviewMouseMoveEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse moves (bubble).
    /// </summary>
    public event MouseEventHandler MouseMove
    {
        add => AddHandler(MouseMoveEvent, value);
        remove => RemoveHandler(MouseMoveEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse enters this element.
    /// </summary>
    public event MouseEventHandler MouseEnter
    {
        add => AddHandler(MouseEnterEvent, value);
        remove => RemoveHandler(MouseEnterEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse leaves this element.
    /// </summary>
    public event MouseEventHandler MouseLeave
    {
        add => AddHandler(MouseLeaveEvent, value);
        remove => RemoveHandler(MouseLeaveEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse wheel is rotated (tunnel).
    /// </summary>
    public event MouseWheelEventHandler PreviewMouseWheel
    {
        add => AddHandler(PreviewMouseWheelEvent, value);
        remove => RemoveHandler(PreviewMouseWheelEvent, value);
    }

    /// <summary>
    /// Occurs when the mouse wheel is rotated (bubble).
    /// </summary>
    public event MouseWheelEventHandler MouseWheel
    {
        add => AddHandler(MouseWheelEvent, value);
        remove => RemoveHandler(MouseWheelEvent, value);
    }

    /// <summary>
    /// Occurs when the left mouse button is pressed (tunnel, direct).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseLeftButtonDown
    {
        add => AddHandler(PreviewMouseLeftButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when the left mouse button is pressed (bubble, direct).
    /// </summary>
    public event MouseButtonEventHandler MouseLeftButtonDown
    {
        add => AddHandler(MouseLeftButtonDownEvent, value);
        remove => RemoveHandler(MouseLeftButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when the left mouse button is released (tunnel, direct).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseLeftButtonUp
    {
        add => AddHandler(PreviewMouseLeftButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when the left mouse button is released (bubble, direct).
    /// </summary>
    public event MouseButtonEventHandler MouseLeftButtonUp
    {
        add => AddHandler(MouseLeftButtonUpEvent, value);
        remove => RemoveHandler(MouseLeftButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when the right mouse button is pressed (tunnel, direct).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseRightButtonDown
    {
        add => AddHandler(PreviewMouseRightButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when the right mouse button is pressed (bubble, direct).
    /// </summary>
    public event MouseButtonEventHandler MouseRightButtonDown
    {
        add => AddHandler(MouseRightButtonDownEvent, value);
        remove => RemoveHandler(MouseRightButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when the right mouse button is released (tunnel, direct).
    /// </summary>
    public event MouseButtonEventHandler PreviewMouseRightButtonUp
    {
        add => AddHandler(PreviewMouseRightButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when the right mouse button is released (bubble, direct).
    /// </summary>
    public event MouseButtonEventHandler MouseRightButtonUp
    {
        add => AddHandler(MouseRightButtonUpEvent, value);
        remove => RemoveHandler(MouseRightButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when touch begins (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewTouchDown
    {
        add => AddHandler(PreviewTouchDownEvent, value);
        remove => RemoveHandler(PreviewTouchDownEvent, value);
    }

    /// <summary>
    /// Occurs when touch begins (bubble).
    /// </summary>
    public event RoutedEventHandler TouchDown
    {
        add => AddHandler(TouchDownEvent, value);
        remove => RemoveHandler(TouchDownEvent, value);
    }

    /// <summary>
    /// Occurs when touch moves (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewTouchMove
    {
        add => AddHandler(PreviewTouchMoveEvent, value);
        remove => RemoveHandler(PreviewTouchMoveEvent, value);
    }

    /// <summary>
    /// Occurs when touch moves (bubble).
    /// </summary>
    public event RoutedEventHandler TouchMove
    {
        add => AddHandler(TouchMoveEvent, value);
        remove => RemoveHandler(TouchMoveEvent, value);
    }

    /// <summary>
    /// Occurs when touch ends (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewTouchUp
    {
        add => AddHandler(PreviewTouchUpEvent, value);
        remove => RemoveHandler(PreviewTouchUpEvent, value);
    }

    /// <summary>
    /// Occurs when touch ends (bubble).
    /// </summary>
    public event RoutedEventHandler TouchUp
    {
        add => AddHandler(TouchUpEvent, value);
        remove => RemoveHandler(TouchUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact begins (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewStylusDown
    {
        add => AddHandler(PreviewStylusDownEvent, value);
        remove => RemoveHandler(PreviewStylusDownEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact begins (bubble).
    /// </summary>
    public event RoutedEventHandler StylusDown
    {
        add => AddHandler(StylusDownEvent, value);
        remove => RemoveHandler(StylusDownEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewStylusMove
    {
        add => AddHandler(PreviewStylusMoveEvent, value);
        remove => RemoveHandler(PreviewStylusMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves (bubble).
    /// </summary>
    public event RoutedEventHandler StylusMove
    {
        add => AddHandler(StylusMoveEvent, value);
        remove => RemoveHandler(StylusMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact ends (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewStylusUp
    {
        add => AddHandler(PreviewStylusUpEvent, value);
        remove => RemoveHandler(PreviewStylusUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact ends (bubble).
    /// </summary>
    public event RoutedEventHandler StylusUp
    {
        add => AddHandler(StylusUpEvent, value);
        remove => RemoveHandler(StylusUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves in air.
    /// </summary>
    public event RoutedEventHandler StylusInAirMove
    {
        add => AddHandler(StylusInAirMoveEvent, value);
        remove => RemoveHandler(StylusInAirMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus enters this element.
    /// </summary>
    public event RoutedEventHandler StylusEnter
    {
        add => AddHandler(StylusEnterEvent, value);
        remove => RemoveHandler(StylusEnterEvent, value);
    }

    /// <summary>
    /// Occurs when stylus leaves this element.
    /// </summary>
    public event RoutedEventHandler StylusLeave
    {
        add => AddHandler(StylusLeaveEvent, value);
        remove => RemoveHandler(StylusLeaveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus enters detection range.
    /// </summary>
    public event RoutedEventHandler StylusInRange
    {
        add => AddHandler(StylusInRangeEvent, value);
        remove => RemoveHandler(StylusInRangeEvent, value);
    }

    /// <summary>
    /// Occurs when stylus exits detection range.
    /// </summary>
    public event RoutedEventHandler StylusOutOfRange
    {
        add => AddHandler(StylusOutOfRangeEvent, value);
        remove => RemoveHandler(StylusOutOfRangeEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus button is pressed.
    /// </summary>
    public event RoutedEventHandler StylusButtonDown
    {
        add => AddHandler(StylusButtonDownEvent, value);
        remove => RemoveHandler(StylusButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus button is released.
    /// </summary>
    public event RoutedEventHandler StylusButtonUp
    {
        add => AddHandler(StylusButtonUpEvent, value);
        remove => RemoveHandler(StylusButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus system gesture is recognized.
    /// </summary>
    public event RoutedEventHandler StylusSystemGesture
    {
        add => AddHandler(StylusSystemGestureEvent, value);
        remove => RemoveHandler(StylusSystemGestureEvent, value);
    }

    /// <summary>
    /// Occurs when pointer contact begins (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewPointerDown
    {
        add => AddHandler(PreviewPointerDownEvent, value);
        remove => RemoveHandler(PreviewPointerDownEvent, value);
    }

    /// <summary>
    /// Occurs when pointer contact begins (bubble).
    /// </summary>
    public event RoutedEventHandler PointerDown
    {
        add => AddHandler(PointerDownEvent, value);
        remove => RemoveHandler(PointerDownEvent, value);
    }

    /// <summary>
    /// Occurs when pointer moves (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewPointerMove
    {
        add => AddHandler(PreviewPointerMoveEvent, value);
        remove => RemoveHandler(PreviewPointerMoveEvent, value);
    }

    /// <summary>
    /// Occurs when pointer moves (bubble).
    /// </summary>
    public event RoutedEventHandler PointerMove
    {
        add => AddHandler(PointerMoveEvent, value);
        remove => RemoveHandler(PointerMoveEvent, value);
    }

    /// <summary>
    /// Occurs when pointer contact ends (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewPointerUp
    {
        add => AddHandler(PreviewPointerUpEvent, value);
        remove => RemoveHandler(PreviewPointerUpEvent, value);
    }

    /// <summary>
    /// Occurs when pointer contact ends (bubble).
    /// </summary>
    public event RoutedEventHandler PointerUp
    {
        add => AddHandler(PointerUpEvent, value);
        remove => RemoveHandler(PointerUpEvent, value);
    }

    /// <summary>
    /// Occurs when pointer is canceled (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewPointerCancel
    {
        add => AddHandler(PreviewPointerCancelEvent, value);
        remove => RemoveHandler(PreviewPointerCancelEvent, value);
    }

    /// <summary>
    /// Occurs when pointer is canceled (bubble).
    /// </summary>
    public event RoutedEventHandler PointerCancel
    {
        add => AddHandler(PointerCancelEvent, value);
        remove => RemoveHandler(PointerCancelEvent, value);
    }

    /// <summary>
    /// Occurs when pointer is pressed (legacy alias).
    /// </summary>
    public event RoutedEventHandler PointerPressed
    {
        add => AddHandler(PointerPressedEvent, value);
        remove => RemoveHandler(PointerPressedEvent, value);
    }

    /// <summary>
    /// Occurs when pointer moves (legacy alias).
    /// </summary>
    public event RoutedEventHandler PointerMoved
    {
        add => AddHandler(PointerMovedEvent, value);
        remove => RemoveHandler(PointerMovedEvent, value);
    }

    /// <summary>
    /// Occurs when pointer is released (legacy alias).
    /// </summary>
    public event RoutedEventHandler PointerReleased
    {
        add => AddHandler(PointerReleasedEvent, value);
        remove => RemoveHandler(PointerReleasedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation is starting (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationStarting
    {
        add => AddHandler(PreviewManipulationStartingEvent, value);
        remove => RemoveHandler(PreviewManipulationStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation is starting (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationStarting
    {
        add => AddHandler(ManipulationStartingEvent, value);
        remove => RemoveHandler(ManipulationStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation has started (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationStarted
    {
        add => AddHandler(PreviewManipulationStartedEvent, value);
        remove => RemoveHandler(PreviewManipulationStartedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation has started (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationStarted
    {
        add => AddHandler(ManipulationStartedEvent, value);
        remove => RemoveHandler(ManipulationStartedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation delta is produced (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationDelta
    {
        add => AddHandler(PreviewManipulationDeltaEvent, value);
        remove => RemoveHandler(PreviewManipulationDeltaEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation delta is produced (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationDelta
    {
        add => AddHandler(ManipulationDeltaEvent, value);
        remove => RemoveHandler(ManipulationDeltaEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation inertia starts (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationInertiaStarting
    {
        add => AddHandler(PreviewManipulationInertiaStartingEvent, value);
        remove => RemoveHandler(PreviewManipulationInertiaStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation inertia starts (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationInertiaStarting
    {
        add => AddHandler(ManipulationInertiaStartingEvent, value);
        remove => RemoveHandler(ManipulationInertiaStartingEvent, value);
    }

    /// <summary>
    /// Occurs when boundary feedback is raised (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationBoundaryFeedback
    {
        add => AddHandler(PreviewManipulationBoundaryFeedbackEvent, value);
        remove => RemoveHandler(PreviewManipulationBoundaryFeedbackEvent, value);
    }

    /// <summary>
    /// Occurs when boundary feedback is raised (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationBoundaryFeedback
    {
        add => AddHandler(ManipulationBoundaryFeedbackEvent, value);
        remove => RemoveHandler(ManipulationBoundaryFeedbackEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation completes (tunnel).
    /// </summary>
    public event RoutedEventHandler PreviewManipulationCompleted
    {
        add => AddHandler(PreviewManipulationCompletedEvent, value);
        remove => RemoveHandler(PreviewManipulationCompletedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation completes (bubble).
    /// </summary>
    public event RoutedEventHandler ManipulationCompleted
    {
        add => AddHandler(ManipulationCompletedEvent, value);
        remove => RemoveHandler(ManipulationCompletedEvent, value);
    }

    #endregion

    #region Protected Virtual Input Event Methods

    // ── Keyboard ──

    /// <summary>
    /// Invoked when an unhandled PreviewKeyDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewKeyDown(KeyEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled KeyDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnKeyDown(KeyEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewKeyUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewKeyUp(KeyEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled KeyUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnKeyUp(KeyEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewTextInput attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTextInput(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TextInput attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTextInput(RoutedEventArgs e)
    {
    }

    // ── Mouse ──

    /// <summary>
    /// Invoked when an unhandled PreviewMouseDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseUp(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseMove(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseMove(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseEnter attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseEnter(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseLeave attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseLeave(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseWheel attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseWheel attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseWheel(MouseWheelEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseLeftButtonDown routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseLeftButtonDown routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseLeftButtonUp routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseLeftButtonUp routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseRightButtonDown routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseRightButtonDown routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewMouseRightButtonUp routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled MouseRightButtonUp routed event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    // ── Touch ──

    /// <summary>
    /// Invoked when an unhandled PreviewTouchDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTouchDown(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchDown(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewTouchMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTouchMove(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchMove(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewTouchUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTouchUp(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchUp(RoutedEventArgs e)
    {
    }

    // ── Stylus ──

    /// <summary>
    /// Invoked when an unhandled PreviewStylusDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusDown(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusDown(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewStylusMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusMove(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusMove(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewStylusUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusUp(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusUp(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusInAirMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusInAirMove(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusEnter attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusEnter(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusLeave attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusLeave(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusInRange attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusInRange(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusOutOfRange attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusOutOfRange(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusButtonDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusButtonDown(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusButtonUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusButtonUp(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusSystemGesture attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusSystemGesture(RoutedEventArgs e)
    {
    }

    #endregion

    #region Drag and Drop

    /// <summary>
    /// Gets or sets a value indicating whether this element can be used as the target of a drag-and-drop operation.
    /// </summary>
    public bool AllowDrop
    {
        get => (bool)(GetValue(DragDrop.AllowDropProperty) ?? false);
        set => SetValue(DragDrop.AllowDropProperty, value);
    }

    /// <summary>
    /// Occurs when the drag cursor enters this element (tunnel).
    /// </summary>
    public event DragEventHandler PreviewDragEnter
    {
        add => AddHandler(DragDrop.PreviewDragEnterEvent, value);
        remove => RemoveHandler(DragDrop.PreviewDragEnterEvent, value);
    }

    /// <summary>
    /// Occurs when the drag cursor enters this element (bubble).
    /// </summary>
    public event DragEventHandler DragEnter
    {
        add => AddHandler(DragDrop.DragEnterEvent, value);
        remove => RemoveHandler(DragDrop.DragEnterEvent, value);
    }

    /// <summary>
    /// Occurs when the drag cursor moves over this element (tunnel).
    /// </summary>
    public event DragEventHandler PreviewDragOver
    {
        add => AddHandler(DragDrop.PreviewDragOverEvent, value);
        remove => RemoveHandler(DragDrop.PreviewDragOverEvent, value);
    }

    /// <summary>
    /// Occurs when the drag cursor moves over this element (bubble).
    /// </summary>
    public event DragEventHandler DragOver
    {
        add => AddHandler(DragDrop.DragOverEvent, value);
        remove => RemoveHandler(DragDrop.DragOverEvent, value);
    }

    /// <summary>
    /// Occurs when the drag cursor leaves this element (tunnel).
    /// </summary>
    public event DragEventHandler PreviewDragLeave
    {
        add => AddHandler(DragDrop.PreviewDragLeaveEvent, value);
        remove => RemoveHandler(DragDrop.PreviewDragLeaveEvent, value);
    }

    /// <summary>
    /// Occurs when the drag cursor leaves this element (bubble).
    /// </summary>
    public event DragEventHandler DragLeave
    {
        add => AddHandler(DragDrop.DragLeaveEvent, value);
        remove => RemoveHandler(DragDrop.DragLeaveEvent, value);
    }

    /// <summary>
    /// Occurs when data is dropped on this element (tunnel).
    /// </summary>
    public event DragEventHandler PreviewDrop
    {
        add => AddHandler(DragDrop.PreviewDropEvent, value);
        remove => RemoveHandler(DragDrop.PreviewDropEvent, value);
    }

    /// <summary>
    /// Occurs when data is dropped on this element (bubble).
    /// </summary>
    public event DragEventHandler Drop
    {
        add => AddHandler(DragDrop.DropEvent, value);
        remove => RemoveHandler(DragDrop.DropEvent, value);
    }

    /// <summary>
    /// Occurs to allow the drag source to provide visual feedback (bubble).
    /// </summary>
    public event GiveFeedbackEventHandler GiveFeedback
    {
        add => AddHandler(DragDrop.GiveFeedbackEvent, value);
        remove => RemoveHandler(DragDrop.GiveFeedbackEvent, value);
    }

    /// <summary>
    /// Occurs to allow the drag source to control the drag operation (bubble).
    /// </summary>
    public event QueryContinueDragEventHandler QueryContinueDrag
    {
        add => AddHandler(DragDrop.QueryContinueDragEvent, value);
        remove => RemoveHandler(DragDrop.QueryContinueDragEvent, value);
    }

    /// <summary>
    /// Called when the drag cursor enters this element.
    /// </summary>
    protected virtual void OnDragEnter(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when the drag cursor moves over this element.
    /// </summary>
    protected virtual void OnDragOver(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when the drag cursor leaves this element.
    /// </summary>
    protected virtual void OnDragLeave(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when data is dropped on this element.
    /// </summary>
    protected virtual void OnDrop(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when the drag cursor enters this element (tunnel).
    /// </summary>
    protected virtual void OnPreviewDragEnter(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when the drag cursor moves over this element (tunnel).
    /// </summary>
    protected virtual void OnPreviewDragOver(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when the drag cursor leaves this element (tunnel).
    /// </summary>
    protected virtual void OnPreviewDragLeave(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called when data is dropped on this element (tunnel).
    /// </summary>
    protected virtual void OnPreviewDrop(DragEventArgs e)
    {
    }

    /// <summary>
    /// Called to provide feedback during a drag operation.
    /// </summary>
    protected virtual void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    /// <summary>
    /// Called to query whether to continue a drag operation.
    /// </summary>
    protected virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    #endregion

    #region Animation

    /// <summary>
    /// Tracks active animations on this element.
    /// </summary>
    private Dictionary<DependencyProperty, ElementAnimation>? _activeAnimations;
    private bool _subscribedToRendering;

    private enum ElementAnimationKind
    {
        Explicit,
        AutomaticTransition
    }

    private sealed class ElementAnimation
    {
        public IAnimationTimeline Animation { get; }
        public IAnimationClock Clock { get; }
        public object? BaseValue { get; }
        public ElementAnimationKind Kind { get; }
        public bool StartPending { get; private set; }

        public ElementAnimation(
            IAnimationTimeline animation,
            IAnimationClock clock,
            object? baseValue,
            ElementAnimationKind kind,
            bool startPending)
        {
            Animation = animation;
            Clock = clock;
            BaseValue = baseValue;
            Kind = kind;
            StartPending = startPending;
        }

        public bool ConsumePendingStart()
        {
            if (!StartPending)
                return false;

            StartPending = false;
            return true;
        }
    }

    /// <summary>
    /// Starts an animation for the specified dependency property.
    /// </summary>
    /// <param name="dp">The dependency property to animate.</param>
    /// <param name="animation">The animation timeline, or null to stop any existing animation.</param>
    public void BeginAnimation(DependencyProperty dp, IAnimationTimeline? animation)
    {
        BeginAnimation(dp, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Starts an animation for the specified dependency property with a handoff behavior.
    /// </summary>
    /// <param name="dp">The dependency property to animate.</param>
    /// <param name="animation">The animation timeline, or null to stop any existing animation.</param>
    /// <param name="handoffBehavior">How to handle existing animations (currently only Replace is supported).</param>
    public void BeginAnimation(DependencyProperty dp, IAnimationTimeline? animation, HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        _ = BeginAnimationCore(
            dp,
            animation,
            handoffBehavior,
            ElementAnimationKind.Explicit,
            clearAnimatedValueOnReplace: true,
            allowAutomaticToReplaceExplicit: true);
    }

    private bool BeginAnimationCore(
        DependencyProperty dp,
        IAnimationTimeline? animation,
        HandoffBehavior handoffBehavior,
        ElementAnimationKind kind,
        bool clearAnimatedValueOnReplace,
        bool allowAutomaticToReplaceExplicit,
        object? initialAnimatedValue = null,
        bool useInitialAnimatedValue = false,
        bool deferClockBeginUntilRendering = false)
    {
        _activeAnimations ??= new Dictionary<DependencyProperty, ElementAnimation>();

        // Stop any existing animation on this property
        if (_activeAnimations.TryGetValue(dp, out var existing))
        {
            if (existing.Kind == ElementAnimationKind.Explicit &&
                kind == ElementAnimationKind.AutomaticTransition &&
                !allowAutomaticToReplaceExplicit)
            {
                return false;
            }

            RemoveAnimationCore(dp, existing, clearAnimatedValueOnReplace);
        }

        if (animation == null)
        {
            UnsubscribeFromRenderingIfNeeded();
            return true;
        }

        // Store base value and create clock
        var baseValue = GetAnimationBaseValue(dp);
        var clock = animation.CreateClock();

        _activeAnimations[dp] = new ElementAnimation(animation, clock, baseValue, kind, deferClockBeginUntilRendering);

        // Subscribe to completion
        clock.Completed += OnAnimationClockCompleted;

        if (!deferClockBeginUntilRendering)
        {
            // Start the clock immediately unless the caller needs the first rendered frame to begin at 0%.
            clock.Begin();
        }

        // Start the animation timer
        SubscribeToRenderingIfNeeded();

        // Set initial animated value
        if (useInitialAnimatedValue)
        {
            SetAnimatedValue(dp, initialAnimatedValue, holdEndValue: false);
        }
        else
        {
            UpdateAnimatedValue(dp);
        }

        return true;
    }

    /// <summary>
    /// Gets a value indicating whether the specified property has an active animation.
    /// </summary>
    /// <param name="dp">The dependency property to check.</param>
    /// <returns>True if the property has an active animation; otherwise, false.</returns>
    public bool HasAnimation(DependencyProperty dp)
    {
        return _activeAnimations?.ContainsKey(dp) == true;
    }

    private bool TryGetActiveAnimation(DependencyProperty dp, out ElementAnimation animation)
    {
        if (_activeAnimations != null && _activeAnimations.TryGetValue(dp, out var activeAnimation))
        {
            animation = activeAnimation;
            return true;
        }

        animation = null!;
        return false;
    }

    private void StopAnimationCore(DependencyProperty dp, ElementAnimationKind kind, bool clearAnimatedValue)
    {
        if (!TryGetActiveAnimation(dp, out var existing) || existing.Kind != kind)
            return;

        RemoveAnimationCore(dp, existing, clearAnimatedValue);
        UnsubscribeFromRenderingIfNeeded();
    }

    private void RemoveAnimationCore(DependencyProperty dp, ElementAnimation animation, bool clearAnimatedValue)
    {
        animation.Clock.Stop();
        animation.Clock.Completed -= OnAnimationClockCompleted;
        _activeAnimations?.Remove(dp);

        if (clearAnimatedValue)
        {
            ClearAnimatedValue(dp);
        }
    }

    private void OnAnimationClockCompleted(object? sender, EventArgs e)
    {
        if (sender is not IAnimationClock clock)
            return;

        // Find the property this clock belongs to
        if (_activeAnimations == null)
            return;

        DependencyProperty? completedProperty = null;
        ElementAnimation? completedAnimation = null;

        foreach (var (dp, anim) in _activeAnimations)
        {
            if (anim.Clock == clock)
            {
                completedProperty = dp;
                completedAnimation = anim;
                break;
            }
        }

        if (completedProperty == null || completedAnimation == null)
            return;

        // Handle fill behavior
        var fillBehavior = completedAnimation.Animation.AnimationFillBehavior;

        if (fillBehavior == AnimationFillBehavior.Stop)
        {
            // Remove animation and restore base value
            RemoveAnimationCore(completedProperty, completedAnimation, clearAnimatedValue: true);
        }
        // For HoldEnd, keep the animation record but mark as completed
        // The final value remains via the animated value layer

        UnsubscribeFromRenderingIfNeeded();
        InvalidateVisual();
    }

    private void SubscribeToRenderingIfNeeded()
    {
        if (!_subscribedToRendering && _activeAnimations?.Count > 0)
        {
            _subscribedToRendering = true;
            CompositionTarget.Rendering += OnRenderingTick;
            CompositionTarget.Subscribe();
        }
    }

    private void UnsubscribeFromRenderingIfNeeded()
    {
        if (_subscribedToRendering &&
            (_activeAnimations == null || _activeAnimations.Count == 0 ||
             !_activeAnimations.Values.Any(a => a.Clock.IsRunning)))
        {
            _subscribedToRendering = false;
            CompositionTarget.Rendering -= OnRenderingTick;
            CompositionTarget.Unsubscribe();
        }
    }

    /// <summary>
    /// Called by CompositionTarget.Rendering on the UI thread, once per frame.
    /// Processes all active animations for this element.
    /// </summary>
    private void OnRenderingTick(object? sender, EventArgs e)
    {
        if (_activeAnimations == null || _activeAnimations.Count == 0)
            return;

        var hasRunningAnimation = false;

        foreach (var (dp, anim) in _activeAnimations.ToArray())
        {
            if (anim.ConsumePendingStart())
            {
                anim.Clock.Begin();
                hasRunningAnimation = true;
                continue;
            }

            if (!anim.Clock.IsRunning)
                continue;

            hasRunningAnimation = true;

            // Update clock progress
            anim.Clock.Tick();

            // Update animated value
            UpdateAnimatedValue(dp);
        }

        if (hasRunningAnimation)
        {
            InvalidateVisual();
        }
        else
        {
            UnsubscribeFromRenderingIfNeeded();
        }
    }

    private void UpdateAnimatedValue(DependencyProperty dp)
    {
        if (_activeAnimations == null || !_activeAnimations.TryGetValue(dp, out var anim))
            return;

        try
        {
            var baseValue = anim.BaseValue ?? dp.DefaultMetadata.DefaultValue ?? GetDefaultAnimationValue(dp);
            var currentValue = anim.Animation.GetCurrentValue(baseValue, baseValue, anim.Clock);

            var holdEnd = anim.Animation.AnimationFillBehavior == AnimationFillBehavior.HoldEnd;
            SetAnimatedValue(dp, currentValue, holdEnd);
        }
        catch
        {
            // If animation value calculation fails, silently continue
        }
    }

    private static object GetDefaultAnimationValue(DependencyProperty dp)
    {
        var type = dp.PropertyType;

        if (type == typeof(double))
            return 0.0;
        if (type == typeof(float))
            return 0f;
        if (type == typeof(int))
            return 0;

        return type.IsValueType ? Activator.CreateInstance(type)! : null!;
    }

    #endregion

    #region Commands

    private Input.CommandBindingCollection? _commandBindings;
    private Input.InputBindingCollection? _inputBindings;

    /// <summary>
    /// Gets the collection of command bindings associated with this element.
    /// </summary>
    public Input.CommandBindingCollection CommandBindings => _commandBindings ??= new Input.CommandBindingCollection();

    /// <summary>
    /// Gets the collection of input bindings associated with this element.
    /// </summary>
    public Input.InputBindingCollection InputBindings => _inputBindings ??= new Input.InputBindingCollection();

    #endregion

    #region Automation

    private Automation.AutomationPeer? _automationPeer;

    /// <summary>
    /// Creates the automation peer for this element.
    /// </summary>
    /// <returns>The automation peer, or null if no peer should be created.</returns>
    protected virtual Automation.AutomationPeer? OnCreateAutomationPeer() => null;

    /// <summary>
    /// Gets or creates the automation peer for this element.
    /// </summary>
    /// <returns>The automation peer, or null if the element doesn't support automation.</returns>
    public Automation.AutomationPeer? GetAutomationPeer()
    {
        _automationPeer ??= OnCreateAutomationPeer();
        return _automationPeer;
    }

    /// <summary>
    /// Invalidates the automation peer, causing it to be recreated on next access.
    /// </summary>
    protected void InvalidateAutomationPeer()
    {
        _automationPeer = null;
    }

    /// <summary>
    /// Notifies UIA when the visual tree structure changes (children added/removed).
    /// Finds the nearest ancestor with a peer and raises StructureChanged on it.
    /// </summary>
    protected override void OnVisualChildrenChanged(Visual? visualAdded, Visual? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);

        // Find the nearest element (self or ancestor) with an existing automation peer
        UIElement? current = this;
        while (current != null)
        {
            if (current._automationPeer != null)
            {
                current._automationPeer.RaiseAutomationEvent(Automation.AutomationEvents.StructureChanged);
                break;
            }
            current = current.VisualParent as UIElement;
        }
    }

    #endregion
}

/// <summary>
/// Specifies the visibility of an element.
/// </summary>
public enum Visibility
{
    /// <summary>
    /// Display the element.
    /// </summary>
    Visible,

    /// <summary>
    /// Do not display the element, but reserve space for it in layout.
    /// </summary>
    Hidden,

    /// <summary>
    /// Do not display the element, and do not reserve space for it in layout.
    /// </summary>
    Collapsed
}

/// <summary>
/// Information about an instance-level event handler.
/// </summary>
internal sealed class RoutedEventHandlerInfo
{
    public Delegate Handler { get; }
    public bool HandledEventsToo { get; }

    public RoutedEventHandlerInfo(Delegate handler, bool handledEventsToo)
    {
        Handler = handler;
        HandledEventsToo = handledEventsToo;
    }
}

/// <summary>
/// Interface for elements that can host a window and handle invalidation.
/// </summary>
public interface IWindowHost
{
    /// <summary>
    /// Invalidates the window, causing it to repaint.
    /// </summary>
    void InvalidateWindow();

    /// <summary>
    /// Adds a dirty element for partial rendering.
    /// </summary>
    void AddDirtyElement(UIElement element);

    /// <summary>
    /// Requests a full invalidation (entire window redraw).
    /// </summary>
    void RequestFullInvalidation();

    /// <summary>
    /// Calls Win32 SetCapture to receive mouse messages even when the cursor is outside the window.
    /// </summary>
    void SetNativeCapture();

    /// <summary>
    /// Calls Win32 ReleaseCapture to stop receiving mouse messages outside the window.
    /// </summary>
    void ReleaseNativeCapture();
}

/// <summary>
/// Interface for elements that host a LayoutManager (typically the Window).
/// </summary>
internal interface ILayoutManagerHost
{
    /// <summary>
    /// Gets the layout manager for this host.
    /// </summary>
    LayoutManager LayoutManager { get; }
}





