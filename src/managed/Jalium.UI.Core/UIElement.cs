using System.Diagnostics;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using System.Collections.Specialized;
using System.Threading;

namespace Jalium.UI;

/// <summary>
/// Base class for UI elements that participate in layout, input, and rendering.
/// </summary>
public partial class UIElement : Visual, IInputElement, Animation.IFrameAnimatable, Media.Animation.IAnimatable
{
    #region Static Constructor — Class Handler Registration

    static UIElement()
    {
        // Keyboard
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewKeyDownEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnPreviewKeyDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), KeyDownEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnKeyDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewKeyUpEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnPreviewKeyUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), KeyUpEvent, new KeyEventHandler((s, e) => ((UIElement)s).OnKeyUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTextInputEvent, new TextCompositionEventHandler((s, e) => ((UIElement)s).OnPreviewTextInput(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TextInputEvent, new TextCompositionEventHandler((s, e) => ((UIElement)s).OnTextInput(e)));

        // Mouse — Down/Up Thunk：MouseDown/Up 是真正被 dispatcher raise 的 Bubble 事件。
        // 类处理器 OnXxxDownThunk 调虚方法 OnMouseDown/Up，然后把通用事件"裂解"成
        // MouseLeftButton{Down/Up}Event / MouseRightButton{Down/Up}Event（Direct）在当前
        // 元素上 RaiseEvent —— WPF 风格。下面再为这 8 个左/右键 routed event 注册类处理器，
        // 调对应虚方法，让派生类 override OnMouseLeftButtonDown(e) 等仍能生效。
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDownThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseDownEvent, new MouseButtonEventHandler(OnMouseDownThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUpThunk));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseUpEvent, new MouseButtonEventHandler(OnMouseUpThunk));
        // 左/右键专用事件的类处理器：thunk 翻译 RaiseEvent 之后，路由系统会调到这里，
        // 把事件派发到 element.OnXxxLeftButtonDown / OnXxxRightButtonDown 虚方法。
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnPreviewMouseLeftButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseLeftButtonDownEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnMouseLeftButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnPreviewMouseLeftButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseLeftButtonUpEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnMouseLeftButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseRightButtonDownEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnPreviewMouseRightButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseRightButtonDownEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnMouseRightButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnPreviewMouseRightButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseRightButtonUpEvent,
            new MouseButtonEventHandler((s, e) => ((UIElement)s).OnMouseRightButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseMoveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnPreviewMouseMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseMoveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseEnterEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseLeaveEvent, new MouseEventHandler((s, e) => ((UIElement)s).OnMouseLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewMouseWheelEvent, new MouseWheelEventHandler((s, e) => ((UIElement)s).OnPreviewMouseWheel(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), MouseWheelEvent, new MouseWheelEventHandler((s, e) => ((UIElement)s).OnMouseWheel(e)));

        // Touch
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchDownEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnPreviewTouchDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchDownEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnTouchDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchMoveEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnPreviewTouchMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchMoveEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnTouchMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewTouchUpEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnPreviewTouchUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchUpEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnTouchUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchEnterEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnTouchEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), TouchLeaveEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnTouchLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), GotTouchCaptureEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnGotTouchCapture(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), LostTouchCaptureEvent, new TouchEventHandler((s, e) => ((UIElement)s).OnLostTouchCapture(e)));

        // Stylus
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusDownEvent, new StylusDownEventHandler((s, e) => ((UIElement)s).OnPreviewStylusDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusDownEvent, new StylusDownEventHandler((s, e) => ((UIElement)s).OnStylusDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusMoveEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnPreviewStylusMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusMoveEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), PreviewStylusUpEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnPreviewStylusUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusUpEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusInAirMoveEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusInAirMove(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusEnterEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusEnter(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusLeaveEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusLeave(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusInRangeEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusInRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusOutOfRangeEvent, new StylusEventHandler((s, e) => ((UIElement)s).OnStylusOutOfRange(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusButtonDownEvent, new StylusButtonEventHandler((s, e) => ((UIElement)s).OnStylusButtonDown(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusButtonUpEvent, new StylusButtonEventHandler((s, e) => ((UIElement)s).OnStylusButtonUp(e)));
        EventManager.RegisterClassHandler(typeof(UIElement), StylusSystemGestureEvent, new StylusSystemGestureEventHandler((s, e) => ((UIElement)s).OnStylusSystemGesture(e)));

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
        InitializeWpfParityClassHandlers();
    }

    public UIElement()
    {
    }

    // Thunk = WPF 风格的"原始事件 → 翻译事件"裂解：MouseDownEvent（Bubble）路由到每个
    // 元素时，类处理器先调虚方法 OnMouseDown 让派生类有机会拦截，再把事件翻译成
    // MouseLeftButton{Down/Up}Event / MouseRightButton{Down/Up}Event（Direct）并在当前
    // 元素上 RaiseEvent —— 这样：
    //   - 订阅 `element.MouseLeftButtonDown += handler` 真能收到（路径上每个元素各收一次）
    //   - 派生类 override OnMouseLeftButtonDown(e) 仍工作（RaiseEvent 走类处理器路径
    //     会调用 OnXxxLeftButton{Down/Up}Event 的对应虚方法 thunk —— 见 InvokeLeftButtonThunk）
    //   - Handled 在源 args 和翻译 args 之间双向同步：派生类的 OnMouseLeftButtonDown
    //     设 Handled 也会回传给 source，让 bubble 链上后续 ancestor 知道事件已被处理。

    private static void OnPreviewMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnPreviewMouseDown(e);
        if (e.Handled) return;
        ReRaiseButtonEvent(element, e, PreviewMouseLeftButtonDownEvent, PreviewMouseRightButtonDownEvent);
    }

    private static void OnMouseDownThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnMouseDown(e);
        if (e.Handled) return;
        ReRaiseButtonEvent(element, e, MouseLeftButtonDownEvent, MouseRightButtonDownEvent);
    }

    private static void OnPreviewMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnPreviewMouseUp(e);
        if (e.Handled) return;
        ReRaiseButtonEvent(element, e, PreviewMouseLeftButtonUpEvent, PreviewMouseRightButtonUpEvent);
    }

    private static void OnMouseUpThunk(object sender, MouseButtonEventArgs e)
    {
        var element = (UIElement)sender;
        element.OnMouseUp(e);
        if (e.Handled) return;
        ReRaiseButtonEvent(element, e, MouseLeftButtonUpEvent, MouseRightButtonUpEvent);
    }

    /// <summary>把通用的 Mouse{Up/Down}Event 翻译成对应的左/右键专用 Direct routed event
    /// 并在当前元素上 raise。Handled 双向同步：翻译事件 Handled = true 会写回原事件，
    /// 让 bubble 链上后续 ancestor 看到已被处理；反之原事件已 Handled 不会重复翻译。
    /// 中键 / XButton1/2 不翻译（与 WPF 行为一致 —— 它们只通过通用 MouseDown/Up 暴露）。</summary>
    private static void ReRaiseButtonEvent(
        UIElement element,
        MouseButtonEventArgs source,
        RoutedEvent leftEvent,
        RoutedEvent rightEvent)
    {
        RoutedEvent? translated = source.ChangedButton switch
        {
            MouseButton.Left => leftEvent,
            MouseButton.Right => rightEvent,
            _ => null,
        };
        if (translated is null) return;

        var args = new MouseButtonEventArgs(
            translated,
            source.GetPosition(null),
            source.ChangedButton,
            source.ButtonState,
            source.ClickCount,
            source.LeftButton,
            source.MiddleButton,
            source.RightButton,
            source.XButton1,
            source.XButton2,
            source.KeyboardModifiers,
            source.Timestamp)
        {
            Source = source.Source,
        };
        // WPF 一贯行为：OriginalSource 在整个事件路由过程中保持为 hit test 最初命中的元素。
        // 不复制 OriginalSource 会让 element.RaiseEvent 里的 SetOriginalSource(this) 把
        // 当前 path 节点（如 designer/window 这样的 host）当成 OriginalSource，破坏 hit
        // 命中元素的可追溯性 —— 任何按 OriginalSource 判断"用户点了哪个真实元素"的 handler
        // 都会拿到错误的祖先节点而不是叶子。
        args.SetOriginalSource(source.OriginalSource);
        element.RaiseEvent(args);
        if (args.Handled) source.Handled = true;
    }

    #endregion

    #region Event Handlers

    private Dictionary<RoutedEvent, List<RoutedEventHandlerInfo>>? _eventHandlers;
    private StylusPlugInCollection? _stylusPlugIns;

    /// <summary>
    /// Gets the stylus plug-ins attached to this element.
    /// </summary>
    protected StylusPlugInCollection StylusPlugIns => GetStylusPlugIns(createIfMissing: true)!;

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

        if (RoutedEventDiagnostics.IsRecording)
            RoutedEventDiagnostics.NotifyRaised(e);
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
            // 沿 visual parent 链向上找下一个 UIElement 祖先。如果中间夹了非 UIElement
            // 的 Visual（比如 ContainerVisual / DrawingVisual / HostVisual），用 `as UIElement`
            // 直接返回 null 会让 bubble 在那里中断、无法到达真正的 UIElement 祖先（例如
            // 模板根之上的控件本体）。改为穿透非 UIElement 节点继续向上寻找。
            current = NextUIElementAncestor(current);
        }
    }

    /// <summary>
    /// 沿 <see cref="Visual.VisualParent"/> 链向上找下一个 <see cref="UIElement"/> 祖先；
    /// 中间夹的纯 <see cref="Visual"/> 节点会被穿透。链尽头返回 null。
    /// </summary>
    private static UIElement? NextUIElementAncestor(Visual node)
    {
        Visual? cur = node.VisualParent;
        while (cur != null)
        {
            if (cur is UIElement ui) return ui;
            cur = cur.VisualParent;
        }
        return null;
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

        // Build the path from root to source. 同 RaiseEventBubble：穿透中间的非 UIElement
        // 节点（ContainerVisual 等），否则 tunnel 会丢失模板控件本体之上的祖先。
        UIElement? current = this;
        while (current != null)
        {
            path.Add(current);
            current = NextUIElementAncestor(current);
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
                e.InvokeHandler(classHandler.Handler, sender);
            }
        }

        // Invoke instance handlers (snapshot to allow handler list modification during dispatch)
        if (_eventHandlers != null && _eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            var snapshot = handlers.ToArray();
            foreach (var handler in snapshot)
            {
                if (!e.Handled || handler.InvokeHandledEventsToo)
                {
                    e.InvokeHandler(handler.Handler, sender);
                }
            }
        }

        // Check CommandBindings for RoutedCommand events
        if (_commandBindings != null && _commandBindings.Count > 0)
        {
            if (routedEvent == Input.RoutedCommand.CanExecuteEvent && e is Input.CanExecuteRoutedEventArgs canExecArgs)
            {
                foreach (Input.CommandBinding binding in _commandBindings)
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
                foreach (Input.CommandBinding binding in _commandBindings)
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
                foreach (Input.CommandBinding binding in _commandBindings)
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
                foreach (Input.CommandBinding binding in _commandBindings)
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
            new FrameworkPropertyMetadata(1.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsCompositionOnly,
                OnCompositionPropertyChanged));

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
        DependencyProperty.Register(nameof(Effect), typeof(Effect), typeof(UIElement),
            new PropertyMetadata(null, OnEffectChanged));

    public static readonly DependencyProperty CacheModeProperty =
        DependencyProperty.Register(nameof(CacheMode), typeof(CacheMode), typeof(UIElement),
            new PropertyMetadata(null, OnCacheModeChanged));

    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public static readonly DependencyProperty BitmapEffectProperty =
        DependencyProperty.Register(nameof(BitmapEffect), typeof(BitmapEffect), typeof(UIElement),
            new PropertyMetadata(null, OnBitmapEffectChanged));

    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public static readonly DependencyProperty BitmapEffectInputProperty =
        DependencyProperty.Register(nameof(BitmapEffectInput), typeof(BitmapEffectInput), typeof(UIElement),
            new PropertyMetadata(null, OnBitmapEffectChanged));

    /// <summary>
    /// Identifies the OpacityMask dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OpacityMaskProperty =
        DependencyProperty.Register(nameof(OpacityMask), typeof(Brush), typeof(UIElement),
            new PropertyMetadata(null, OnOpacityMaskChanged));

    /// <summary>
    /// Identifies the RenderTransform dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RenderTransformProperty =
        DependencyProperty.Register(nameof(RenderTransform), typeof(Transform), typeof(UIElement),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsCompositionOnly,
                OnRenderTransformChanged));

    /// <summary>
    /// Identifies the RenderTransformOrigin dependency property.
    /// The origin is specified as a normalized point (0-1 range) relative to the element's render size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RenderTransformOriginProperty =
        DependencyProperty.Register(nameof(RenderTransformOrigin), typeof(Point), typeof(UIElement),
            new FrameworkPropertyMetadata(new Point(0, 0),
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsCompositionOnly,
                OnCompositionPropertyChanged));

    /// <summary>
    /// Identifies the Focusable dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty FocusableProperty =
        DependencyProperty.Register(nameof(Focusable), typeof(bool), typeof(UIElement),
            new PropertyMetadata(false, OnFocusablePropertyChanged));

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
    internal static readonly DependencyProperty IsPressedProperty = IsPressedPropertyKey.DependencyProperty;

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
        DependencyProperty.Register(nameof(Clip), typeof(Geometry), typeof(UIElement),
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
            if (!localValue || !IsEnabledCore) return false;
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
    /// Use implementations like BackdropBlurEffect, AcrylicEffect, MicaEffect, etc.
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
    public Effect? Effect
    {
        get => (Effect?)GetValue(EffectProperty);
        set => SetValue(EffectProperty, value);
    }

    public CacheMode? CacheMode
    {
        get => (CacheMode?)GetValue(CacheModeProperty);
        set => SetValue(CacheModeProperty, value);
    }

    [Obsolete("BitmapEffect is deprecated. Use Effect instead.")]
    public BitmapEffect? BitmapEffect
    {
        get => (BitmapEffect?)GetValue(BitmapEffectProperty);
        set => SetValue(BitmapEffectProperty, value);
    }

    [Obsolete("BitmapEffectInput is deprecated. Use Effect instead.")]
    public BitmapEffectInput? BitmapEffectInput
    {
        get => (BitmapEffectInput?)GetValue(BitmapEffectInputProperty);
        set => SetValue(BitmapEffectInputProperty, value);
    }

    /// <summary>
    /// Gets or sets a brush that specifies the opacity mask for this element.
    /// The alpha channel of the brush determines the opacity of corresponding parts of the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? OpacityMask
    {
        get => (Brush?)GetValue(OpacityMaskProperty);
        set => SetValue(OpacityMaskProperty, value);
    }

    /// <summary>
    /// Gets or sets the render transform.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Transform? RenderTransform
    {
        get => (Transform?)GetValue(RenderTransformProperty);
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
    /// Gets a value indicating whether the mouse pointer is directly over this element.
    /// </summary>
    public bool IsMouseDirectlyOver => ReferenceEquals(_mouseDirectlyOver, this);

    private static UIElement? _mouseDirectlyOver;

    /// <summary>Gets the element most recently reported directly under the mouse.</summary>
    public static UIElement? MouseDirectlyOverElement => _mouseDirectlyOver;

    /// <summary>
    /// Updates the direct mouse-over element tracked by the platform input dispatcher.
    /// </summary>
    internal static void SetMouseDirectlyOverElement(UIElement? element)
    {
        var previous = _mouseDirectlyOver;
        if (ReferenceEquals(previous, element))
            return;

        _mouseDirectlyOver = element;
        UpdateMouseDirectlyOverDependencyState(previous, element);
    }

    /// <summary>
    /// Sets the IsMouseOver property value. Called internally by mouse tracking.
    /// </summary>
    internal void SetIsMouseOver(bool value)
    {
        // A disabled element must never enter the hover state. Hover visuals
        // (IsMouseOver triggers, "MouseOver" visual states) do not apply to a
        // disabled control. IsEnabled is the effective value — it walks the
        // parent chain — so a child of a disabled ancestor is rejected too.
        if (value && !IsEnabled)
            value = false;
        SetValue(IsMouseOverPropertyKey, value);
    }

    /// <summary>
    /// Gets a value indicating whether this element is currently pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    internal bool IsPressed => (bool)GetValue(IsPressedProperty)!;

    /// <summary>
    /// Sets the IsPressed property value. Called internally by input state tracking.
    /// </summary>
    internal void SetIsPressed(bool value)
    {
        // A disabled element must never enter the pressed state, mirroring
        // SetIsMouseOver — pressed visuals do not apply to a disabled control.
        if (value && !IsEnabled)
            value = false;
        SetValue(IsPressedPropertyKey, value);
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
    public Geometry? Clip
    {
        get => (Geometry?)GetValue(ClipProperty);
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
        SetValue(IsFocusedPropertyKey, value);
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

    /// <summary>Moves keyboard focus according to the specified traversal request.</summary>
    public virtual bool MoveFocus(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FocusService.MoveFocus(this, request.FocusNavigationDirection);
    }

    /// <summary>Returns the element that would receive focus without moving it.</summary>
    public virtual DependencyObject? PredictFocus(FocusNavigationDirection direction)
    {
        return FocusService.PredictFocus(this, direction);
    }

    /// <summary>
    /// Updates the IsKeyboardFocused state. Called by the Keyboard class.
    /// </summary>
    internal void UpdateIsKeyboardFocused(bool isFocused)
    {
        if (IsKeyboardFocused != isFocused)
        {
            SetValue(IsKeyboardFocusedPropertyKey, isFocused);
        }
    }

    /// <summary>
    /// Updates the IsKeyboardFocusWithin state. Called by the Keyboard class.
    /// </summary>
    internal void UpdateIsKeyboardFocusWithin(bool isFocusWithin)
    {
        if (IsKeyboardFocusWithin != isFocusWithin)
        {
            SetValue(IsKeyboardFocusWithinPropertyKey, isFocusWithin);
        }
    }

    /// <summary>
    /// Called when the IsKeyboardFocused property changes.
    /// </summary>
    protected virtual void OnIsKeyboardFocusedChanged(bool isFocused)
    {
    }

    protected virtual void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
    {
        OnIsKeyboardFocusedChanged((bool)(e.NewValue ?? false));
    }

    /// <summary>
    /// Called when the IsKeyboardFocusWithin property changes.
    /// </summary>
    protected virtual void OnIsKeyboardFocusWithinChanged(bool isFocusWithin)
    {
    }

    protected virtual void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        OnIsKeyboardFocusWithinChanged((bool)(e.NewValue ?? false));
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
    // Screen-offset cache validity is tracked by epoch comparison rather than a
    // per-element bool. A single global counter bump invalidates every element's
    // cached screen offset in O(1); each element lazily recomputes (walking its
    // full parent chain) the next time GetScreenBounds is called and its stored
    // epoch is stale. This replaces a recursive subtree invalidation that ran on
    // every Arrange — see InvalidateScreenOffsetCache for the full rationale.
    private long _screenOffsetEpoch = -1;
    private static long s_screenOffsetEpoch;
    // ── RC4-b/RC4-c 脏区管线字段 ──
    // Arrange 至少完成过一次（位移钩子的前置短路必须放过首次 arrange）。
    private bool _hasArrangedOnce;
    // 上一次 Arrange 结束时的 DesiredSize。与 _previousFinalRect 一起证明 alignment
    // 的全部输入未变（槽、期望尺寸都没变 ⇒ 槽内位置也不变；alignment 属性变化自身
    // 走 AffectsArrange 失效注册），让位移钩子跳过静止元素的 O(depth) 走链。
    private Size _previousDesiredSize;
    // Effect DP 的普通字段镜像，供 GetDirtyRenderBounds 使用：该方法会被后台线程
    // （窗口 AddDirtyElement 注册路径）调用，直接读 DependencyProperty 会与 UI 线程
    // 的值存储 Dictionary 写入竞争；字段读的撕裂等级与这些调用方本就在读的其他
    // 布局字段相同（良性）。由静态 OnEffectChanged 回调维护。
    private IEffect? _effectForDirtyBounds;
    // 最近一次脏区计算为本元素提交的未 clip 屏幕 AABB（Window.ComputeDirtyRegions
    // 在 present 前写，AddDirtyElement 注册时读作 PrevPaintedBounds）。用于擦除
    // "先改值后失效 + 长空闲后不连续跳变 + 新 AABB 不含旧 AABB"场景下上次画过的
    // 像素。UI 线程写；后台注册线程可能读到撕裂值——影响限于一帧的过/欠擦除矩形，
    // 与既有跨线程 bounds 读取同级。
    internal Rect LastDirtyBounds = Rect.Empty;

    /// <summary>
    /// Gets the desired size computed during the measure pass.
    /// </summary>
    public Size DesiredSize => _desiredSize;

    /// <summary>
    /// Gets the final render size after arrangement.
    /// </summary>
    public Size RenderSize
    {
        get => _renderSize;
        set
        {
            if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || value.Width < 0 || value.Height < 0)
            {
                throw new ArgumentException("RenderSize must contain finite, non-negative dimensions.", nameof(value));
            }

            if (_renderSize == value)
            {
                return;
            }

            var previous = _renderSize;
            _renderSize = value;
            OnRenderSizeChanged(new SizeChangedInfo(
                this,
                previous,
                previous.Width != value.Width,
                previous.Height != value.Height));
            SetRenderDirty();
        }
    }

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
    /// When ClipToBounds is true, returns a RectangleGeometry matching the element's RenderSize.
    /// </summary>
    /// <returns>The clipping geometry, or null if no clipping should be applied.</returns>
    internal virtual Geometry? GetLayoutClip()
    {
        // Explicit Clip geometry takes precedence
        var clip = Clip;
        if (clip != null)
            return clip;

        if (ClipToBounds)
        {
            return new RectangleGeometry(new Rect(0, 0, _renderSize.Width, _renderSize.Height));
        }
        return null;
    }

    /// <summary>
    /// Returns whether a point in this element's local coordinate space falls inside
    /// the layout clip that would be pushed during rendering. Hit-testing relies on this
    /// to ensure clicks do not fall through to content that is visually clipped away
    /// (e.g. an input control scrolled out of a ScrollViewer viewport whose VisualBounds
    /// still extend past the viewport edge).
    /// </summary>
    internal virtual bool IsPointInsideLayoutClip(Point localPoint)
    {
        var clip = GetLayoutClip();
        if (clip == null)
        {
            return true;
        }

        return clip.FillContains(localPoint);
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
        _isMeasureValid = false;
        _isArrangeValid = false;
        FindLayoutManager()?.InvalidateMeasure(this);

        // Always register this element in the window's dirty-element set — NOT only
        // on the first valid->invalid transition, which is what the old code did (it
        // took a cheaper "already invalid -> just InvalidateWindow()" branch).
        //
        // ComputeDirtyRegions builds the partial dirty-rect present purely from
        // _dirtyElements; the layout pass itself never adds to that set (Arrange only
        // flips the per-visual _isRenderDirty flag, it does not call AddDirtyElement).
        // So when InvalidateMeasure skipped registration for an ALREADY measure-invalid
        // element, two common cases left the element render-dirty but absent from
        // _dirtyElements — its region was then excluded from the present and it rendered
        // BLANK until a full present (a resize) repainted it:
        //   1. a freshly-created subtree — its _isMeasureValid starts false, so the very
        //      first InvalidateMeasure would otherwise skip registration (this is the
        //      "navigate to a page and the whole content card is blank" case: the
        //      ContentControl swaps in the new page and InvalidateMeasure's on the
        //      content host / new subtree fall into the skip branch);
        //   2. an element re-invalidated after an intervening render already cleared
        //      _dirtyElements (the "nav label intermittently missing" case).
        // Registering unconditionally is cheap: AddDirtyElement dedupes per frame and
        // SetRenderDirty short-circuits at already-marked ancestors.
        InvalidateLayoutVisual();

        if (LayoutDiagnostics.IsRecording)
            LayoutDiagnostics.NotifyInvalidation(this, LayoutDiagnostics.InvalidationKind.Measure);
    }

    /// <summary>
    /// Invalidates the arrange pass for this element.
    /// </summary>
    public void InvalidateArrange()
    {
        _isArrangeValid = false;
        FindLayoutManager()?.InvalidateArrange(this);

        // Always register as dirty — same reasoning as InvalidateMeasure above. An
        // element arrange-invalidated while already arrange-invalid (a fresh subtree,
        // or one re-invalidated after a render cleared _dirtyElements) would otherwise
        // never enter _dirtyElements and would render blank until a full present.
        InvalidateLayoutVisual();

        if (LayoutDiagnostics.IsRecording)
            LayoutDiagnostics.NotifyInvalidation(this, LayoutDiagnostics.InvalidationKind.Arrange);
    }

    /// <summary>
    /// Requests a full window repaint for layout changes.
    /// Layout changes (measure/arrange) can move elements arbitrarily,
    /// so dirty rects for individual elements are unreliable.
    /// Marks this element dirty so its region is repainted.
    /// </summary>
    /// <remarks>
    /// Must also flip <see cref="Visual.SetRenderDirty"/> — otherwise the
    /// retained-mode drawing cache (<see cref="Visual.RenderCacheHost"/>)
    /// will replay the stale captured command list and the dirty rect gets
    /// painted with last frame's content. Layout invalidations always imply
    /// visual invalidations (size/position/content shifted), so they share
    /// the same render-cache-invalidation semantics as InvalidateVisual.
    /// </remarks>
    private void InvalidateLayoutVisual()
    {
        var windowHost = GetWindowHostOrNull();
        if (windowHost == null) return;

        SetRenderDirty();
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

        var windowHost = GetWindowHostOrNull();
        if (windowHost != null)
        {
            windowHost.AddDirtyElement(this);
            windowHost.InvalidateWindow();
        }

        if (LayoutDiagnostics.IsRecording)
            LayoutDiagnostics.NotifyInvalidation(this, LayoutDiagnostics.InvalidationKind.Visual);
    }

    /// <summary>
    /// Schedules a repaint without invalidating this element's cached drawing.
    /// Use for property changes that affect only how the parent composites this
    /// element (Opacity, RenderTransform, RenderTransformOrigin) — the parent's
    /// child-render loop reads these values live each frame via PushOpacity /
    /// PushTransform, so the recorded command list stays correct.
    /// </summary>
    /// <remarks>
    /// Compared with <see cref="InvalidateVisual()"/>:
    ///   - does not flip <c>_isRenderDirty</c>, so retained-mode replay continues
    ///     to skip the OnRender re-record on this and ancestor visuals;
    ///   - still propagates the subtree-dirty flag up so the render walker reaches
    ///     this element and re-runs the parent's child loop;
    ///   - still asks the window host for a present.
    /// Per-frame transitions (180 ms hover Opacity fade across many cards) used
    /// to flip every animated element render-dirty every frame, blowing the
    /// retained cache. This path keeps the cache hot.
    /// </remarks>
    public void InvalidateComposition()
    {
        MarkSubtreeDirtyForComposition();

        var windowHost = GetWindowHostOrNull();
        if (windowHost != null)
        {
            windowHost.AddDirtyElement(this);
            windowHost.InvalidateWindow();
        }

        if (LayoutDiagnostics.IsRecording)
            LayoutDiagnostics.NotifyInvalidation(this, LayoutDiagnostics.InvalidationKind.Visual);
    }

    /// <summary>
    /// Invalidates a specific sub-rectangle (in this element's local coordinate space)
    /// for partial redraw. Enables precise dirty tracking for animations that affect
    /// only a small part of the element — caret blink, focus rings, hover glyphs,
    /// progress bar fill — so a 400-px-wide TextBox caret doesn't mark the whole
    /// control dirty just to flash 2×20 pixels.
    /// </summary>
    /// <param name="localDirtyRect">
    /// Dirty rectangle in local coordinates (0,0 = this element's top-left).
    /// An empty rect is ignored. A rect that extends past the element's bounds
    /// is clipped by the window layer.
    /// </param>
    public void InvalidateVisual(Rect localDirtyRect)
    {
        // Rect.Empty is a distinct WPF sentinel. Preserve the renderer's
        // historical handling of degenerate dirty rectangles: they carry no
        // pixels, so fall back to a full invalidation instead of queuing an
        // unusable precise region.
        if (localDirtyRect.IsEmpty || localDirtyRect.Width <= 0 || localDirtyRect.Height <= 0)
        {
            InvalidateVisual();
            return;
        }

        SetRenderDirty();

        var windowHost = GetWindowHostOrNull();
        if (windowHost != null)
        {
            windowHost.AddDirtyElement(this, localDirtyRect);
            windowHost.InvalidateWindow();
        }

        if (LayoutDiagnostics.IsRecording)
            LayoutDiagnostics.NotifyInvalidation(this, LayoutDiagnostics.InvalidationKind.Visual);
    }

    /// <summary>
    /// Gets the cached IWindowHost by walking up the tree (lazy, cached), or
    /// null when the element is not hosted by a window.
    /// </summary>
    internal IWindowHost? GetWindowHostOrNull()
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
        InvalidateScreenOffsetCache();

        // Recursively invalidate children's host caches too
        var count = VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (GetVisualChild(i) is UIElement uiChild)
                uiChild.InvalidateHostCaches();
        }
    }

    /// <summary>
    /// Invalidates the cached screen-space offset of this element and, by
    /// construction, every other element in the process.
    /// </summary>
    /// <remarks>
    /// This used to walk the entire visual subtree setting a per-element
    /// "invalid" bool. Because <see cref="Arrange"/> calls it and
    /// <c>ArrangeCore</c> recurses into every child's <see cref="Arrange"/>,
    /// each descendant was re-invalidated once per ancestor on the way down —
    /// an O(n·depth) blow-up that dominated the arrange pass on deep/wide
    /// trees (measured ~31 ms for a single Canvas-heavy frame).
    ///
    /// Since <see cref="GetScreenBounds"/> always recomputes from the full
    /// parent chain (it never trusts a parent's cached value), cache validity
    /// only needs a cheap "is my cached value from the current layout state?"
    /// check. A monotonic global epoch gives exactly that: bumping it here is
    /// O(1) and every element's next <see cref="GetScreenBounds"/> sees a
    /// stale epoch and recomputes lazily. Over-invalidation (elements that
    /// didn't actually move also recompute on next query) is bounded by O(n)
    /// total per frame and only paid when the value is read, versus the old
    /// O(n·depth) eager walk paid on every arrange.
    /// </remarks>
    internal void InvalidateScreenOffsetCache()
    {
        unchecked { s_screenOffsetEpoch++; }
    }

    /// <summary>
    /// Gets the screen-space bounds of this element relative to its Window.
    /// Uses cached layout offset (O(1)) plus current RenderOffset (not cached,
    /// since RenderOffset changes during animations without triggering layout).
    /// </summary>
    internal Rect GetScreenBounds()
    {
        if (_screenOffsetEpoch != s_screenOffsetEpoch)
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
            _screenOffsetEpoch = s_screenOffsetEpoch;
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
    /// Gets the screen-space bounds of this element relative to its Window, composing the
    /// full ancestor + own <see cref="RenderTransform"/> chain so the returned AABB matches
    /// where pixels actually rasterize.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetScreenBounds"/> reports only the static layout box (ancestor
    /// <see cref="Visual.VisualBounds"/> offsets + own <see cref="RenderOffset"/>) and is
    /// intentionally left transform-unaware for drag/drop hit math. This method additionally
    /// folds in every <see cref="RenderTransform"/> on the chain from this element up to (but
    /// excluding) the window, applied around each level's <see cref="RenderTransformOrigin"/>
    /// scaled by that level's <see cref="RenderSize"/> — exactly matching the live draw path
    /// <c>Visual.RenderChildVisualInline</c> (offset set first, then
    /// <c>PushTransform(transform, origin.X*RenderSize.Width, origin.Y*RenderSize.Height)</c>).
    /// </para>
    /// <para>
    /// The D3D12 inline partial-present path derives its dirty region (and FLIP_SEQUENTIAL
    /// Present1 dirty rects) from this AABB. If it tracked the static layout box instead, a
    /// RenderTransform animation (scale/rotate/translate — spinners, slide/scale transitions,
    /// hover/press scale, expand/collapse) would rasterize content at the transformed position
    /// while invalidating the un-transformed box → the transformed pixels fall outside the clip
    /// and the alternate FLIP buffer keeps stale pixels → all Path icons flicker. When no
    /// element on the chain carries a RenderTransform the result is identical to
    /// <see cref="GetScreenBounds"/>, returned directly as a fast path.
    /// </para>
    /// </remarks>
    internal Rect GetRenderBounds()
    {
        // Fast path: identical to GetScreenBounds() when nothing on the chain carries a
        // RenderTransform (the overwhelmingly common case → no matrix math, cache-friendly).
        if (!ChainHasRenderTransform())
            return GetScreenBounds();

        return TransformLocalRectToScreen(GetRenderMatrix(), 0, 0, _renderSize.Width, _renderSize.Height);
    }

    /// <summary>
    /// Screen-space AABB of everything this element can ink this frame:
    /// <see cref="GetRenderBounds"/> expanded by the element <see cref="Effect"/>'s
    /// <see cref="IEffect.EffectPadding"/> when one is active. The dirty-region pipeline
    /// must track THIS rect — DropShadow/OuterGlow/Blur paint outside <see cref="RenderSize"/>,
    /// and erasing/redrawing only the layout box leaves effect tails behind a moving element.
    /// </summary>
    /// <remarks>
    /// 扩边公式与渲染路径的裁剪判定（Visual.ShouldRenderChild）逐字一致，保证
    /// "会画到哪"与"标脏到哪"同一口径。经 <c>_effectForDirtyBounds</c> 字段镜像读取
    /// Effect（而非 DependencyProperty），后台注册线程（AddDirtyElement）调用安全——
    /// 与 <see cref="GetRenderBounds"/> 的既有跨线程读取同级。
    /// </remarks>
    internal Rect GetDirtyRenderBounds()
    {
        double extra = GetExtraDirtyPadding();
        if (_effectForDirtyBounds is { HasEffect: true } effect)
        {
            var padding = effect.EffectPadding;
            var size = _renderSize;
            return MapLocalRectToScreen(new Rect(
                -padding.Left - extra,
                -padding.Top - extra,
                size.Width + padding.Left + padding.Right + extra * 2,
                size.Height + padding.Top + padding.Bottom + extra * 2));
        }
        if (extra > 0)
        {
            var size = _renderSize;
            return MapLocalRectToScreen(new Rect(
                -extra, -extra, size.Width + extra * 2, size.Height + extra * 2));
        }
        return GetRenderBounds();
    }

    /// <summary>
    /// Extra symmetric dirty padding (DIPs) this element inks beyond
    /// <see cref="RenderSize"/> through channels other than <see cref="Effect"/> —
    /// e.g. Border.LiquidGlass draws a 32 px outer-shadow ring in its native quad.
    /// The dirty pipeline must track everything the element can ink, or a moving
    /// element leaves that ring behind. Overrides must be thread-safe reads
    /// (mirror fields, not DP getters): callers include background-thread dirty
    /// registration, same contract as <c>_effectForDirtyBounds</c>.
    /// </summary>
    internal virtual double GetExtraDirtyPadding() => 0.0;

    /// <summary>
    /// Maps a rect expressed in THIS element's local content space into screen space through
    /// the full local→screen render matrix (ancestor + own <see cref="RenderTransform"/>).
    /// When no transform is on the chain this reduces to a pure translation by the element's
    /// screen origin — byte-identical to the legacy translate-by-origin behavior, so callers
    /// (e.g. the precise-rect dirty path) need no special-casing.
    /// </summary>
    internal Rect MapLocalRectToScreen(Rect local)
    {
        if (!ChainHasRenderTransform())
        {
            var origin = GetScreenBounds();
            return new Rect(origin.X + local.X, origin.Y + local.Y, local.Width, local.Height);
        }

        return TransformLocalRectToScreen(GetRenderMatrix(), local.X, local.Y, local.Width, local.Height);
    }

    /// <summary>
    /// Builds the local→screen affine matrix for this element, composing the full ancestor
    /// chain in the SAME row-vector order the renderer uses.
    /// </summary>
    /// <remarks>
    /// Row-vector convention (point * matrix; see <see cref="Matrix"/>). For each level k from
    /// this element UP to the window the local contribution is
    /// <c>level_k = aboutOrigin_k * T(off_k)</c> with
    /// <c>off_k = (VisualBounds.X + RenderOffset.X, VisualBounds.Y + RenderOffset.Y)</c>,
    /// <c>aboutOrigin_k = T(-o) * RenderTransform.Value * T(+o)</c> (omitted when the transform
    /// is null) and <c>o = (RenderTransformOrigin.X * RenderSize.Width, .Y * RenderSize.Height)</c>.
    /// Accumulated self-first (<c>acc = acc * level</c>) so this element is applied innermost,
    /// reproducing the renderer's "add absolute Offset to local coords, then apply the
    /// conjugated T(-Offset)*R*T(+Offset) stack with nested LEFT-multiply" pipeline for single
    /// AND nested transforms — derived and verified against <c>RenderTargetDrawingContext</c>.
    /// </remarks>
    internal Matrix GetRenderMatrix()
    {
        var acc = Matrix.Identity;
        Visual? current = this;
        while (current != null && current is not IWindowHost)
        {
            if (current is UIElement ui)
            {
                var vb = ui.VisualBounds;
                var ro = ui.RenderOffset;
                double offX = vb.X + ro.X;
                double offY = vb.Y + ro.Y;

                Matrix level;
                var rt = ui.RenderTransform;
                if (rt != null)
                {
                    var size = ui._renderSize;
                    var origin = ui.RenderTransformOrigin;
                    double ox = origin.X * size.Width;
                    double oy = origin.Y * size.Height;

                    // aboutOrigin = T(-o) * R * T(+o) — transform about the (scaled) origin.
                    var r = rt.Value;
                    var pre = new Matrix(1, 0, 0, 1, -ox, -oy);
                    var post = new Matrix(1, 0, 0, 1, ox, oy);
                    var aboutOrigin = Matrix.Multiply(Matrix.Multiply(pre, r), post);

                    // level = aboutOrigin * T(off): the layout/render offset positions the
                    // whole transformed subtree (offset applied AFTER the about-origin transform).
                    level = Matrix.Multiply(aboutOrigin, new Matrix(1, 0, 0, 1, offX, offY));
                }
                else
                {
                    level = new Matrix(1, 0, 0, 1, offX, offY);
                }

                // Self-first accumulation (inner applied first): acc = acc * level.
                acc = Matrix.Multiply(acc, level);
            }

            current = current.VisualParent;
        }

        return acc;
    }

    /// <summary>
    /// Builds the affine matrix mapping THIS element's local space into the local space of
    /// <paramref name="ancestor"/> (or, when <paramref name="ancestor"/> is <c>null</c>, the window
    /// root), composing every level's <see cref="RenderTransform"/> about its scaled
    /// <c>RenderTransformOrigin</c> in the SAME row-vector, self-first order as
    /// <see cref="GetRenderMatrix"/>.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetRenderMatrix"/> this walks only up to (and excluding)
    /// <paramref name="ancestor"/> — falling back to the <see cref="IWindowHost"/> boundary if that
    /// ancestor is never reached — and it composes <see cref="VisualBounds"/> translation WITHOUT
    /// <c>RenderOffset</c>. TransformToAncestor is a layout-position query (WPF semantics), and
    /// excluding the animation-only <c>RenderOffset</c> keeps every non-transform chain
    /// byte-identical to the historical VisualBounds-sum behavior, so existing callers
    /// (DockTabPanel reorder, MiniMap scroll mapping) do not shift, while a scale/rotate on the
    /// chain (e.g. a <c>Viewbox</c>) is now composed correctly.
    /// </remarks>
    internal Matrix GetRenderMatrixTo(Visual? ancestor)
    {
        var acc = Matrix.Identity;
        Visual? current = this;
        while (current != null && current != ancestor && current is not IWindowHost)
        {
            if (current is UIElement ui)
            {
                var vb = ui.VisualBounds;
                double offX = vb.X;
                double offY = vb.Y;

                Matrix level;
                var rt = ui.RenderTransform;
                if (rt != null)
                {
                    var size = ui._renderSize;
                    var origin = ui.RenderTransformOrigin;
                    double ox = origin.X * size.Width;
                    double oy = origin.Y * size.Height;

                    var r = rt.Value;
                    var pre = new Matrix(1, 0, 0, 1, -ox, -oy);
                    var post = new Matrix(1, 0, 0, 1, ox, oy);
                    var aboutOrigin = Matrix.Multiply(Matrix.Multiply(pre, r), post);
                    level = Matrix.Multiply(aboutOrigin, new Matrix(1, 0, 0, 1, offX, offY));
                }
                else
                {
                    level = new Matrix(1, 0, 0, 1, offX, offY);
                }

                acc = Matrix.Multiply(acc, level);
            }

            current = current.VisualParent;
        }

        return acc;
    }

    /// <summary>
    /// True if this element or any ancestor up to the window carries a non-null
    /// <see cref="RenderTransform"/>. Cheap pre-check that lets <see cref="GetRenderBounds"/>
    /// and <see cref="MapLocalRectToScreen"/> short-circuit to the transform-free fast path.
    /// </summary>
    private bool ChainHasRenderTransform()
    {
        Visual? current = this;
        while (current != null && current is not IWindowHost)
        {
            if (current is UIElement ui && ui.RenderTransform != null)
                return true;
            current = current.VisualParent;
        }
        return false;
    }

    /// <summary>
    /// Walks the visual-parent chain from this element's parent up to (but excluding) the
    /// <see cref="IWindowHost"/>, intersecting each ancestor's <see cref="GetLayoutClip()"/>
    /// projected into screen space. Returns <c>false</c> — meaning the caller must NOT cull —
    /// whenever the chain cannot be reasoned about geometrically:
    /// <list type="bullet">
    /// <item>no clipping ancestor exists (the overwhelmingly common non-scrolling case), or</item>
    /// <item>any ancestor carries a <see cref="RenderTransform"/> or a non-zero
    /// <see cref="RenderOffset"/>: its clip's screen rect cannot be located through the
    /// transform-unaware <see cref="GetScreenBounds"/> origin, so we conservatively bail on the
    /// <b>entire</b> chain rather than "skip and continue" (continuing would place later clips
    /// with a wrong origin and risk under-erasing).</item>
    /// </list>
    /// A degenerate zero-area ancestor clip also returns <c>false</c> so we never rely on the
    /// <c>Empty.Intersect</c> coincidence to cull an element whose ancestor happens to have a
    /// zero-size axis this frame.
    /// </summary>
    /// <remarks>
    /// CONTRACT (relied on by the RC4-b displacement hook, do not break): the caller intersects
    /// the returned clip — computed at the <i>new-frame</i> ancestor positions — against BOTH the
    /// old and the new element AABB. This is sound only because when a clipping ancestor's own
    /// screen position/size changes it necessarily re-arranges and hits its OWN RC4-b path, whose
    /// old AABB (captured before its epoch bump = its old clip region) is submitted via
    /// <c>AddDirtyRect</c> and repaints the stale pixels of any descendant this method wrongly
    /// culled. Do NOT cache the result across frames: <see cref="Clip"/>/<see cref="ClipToBounds"/>
    /// changes only <c>InvalidateVisual</c> and never bump <c>s_screenOffsetEpoch</c>, so a cached
    /// intersection would go stale and under-erase.
    /// </remarks>
    private bool TryGetAncestorClipScreenBounds(out Rect clipBounds)
    {
        clipBounds = Rect.Empty;
        bool found = false;
        // Start at the parent: an element's own layout clip never clips its own bounds.
        Visual? current = ParentVisual;
        while (current != null && current is not IWindowHost)
        {
            if (current is UIElement ui)
            {
                // A transform/offset ancestor breaks the transform-unaware GetScreenBounds origin
                // used below to place its clip — bail on the whole chain (see summary). Because we
                // bail on the FIRST such ancestor, any successful (found=true) result guarantees
                // the entire self→ancestor chain is offset/transform-free, so every GetScreenBounds
                // below is a pure layout origin and the intersection is exact.
                if (ui.RenderTransform != null || ui.RenderOffset.X != 0 || ui.RenderOffset.Y != 0)
                    return false;

                var clip = ui.GetLayoutClip();
                if (clip != null)
                {
                    // clip.Bounds is the ancestor-local AABB. Rounded/inset shapes collapse to their
                    // outer box here: larger-and-safe for round corners; identical to the render clip
                    // for Border's inset since RenderDirect pushes the same GetLayoutClip geometry.
                    var b = clip.Bounds;
                    var o = ui.GetScreenBounds(); // ancestor screen origin (chain proven clean above)
                    var screenClip = new Rect(o.X + b.X, o.Y + b.Y, b.Width, b.Height);
                    clipBounds = found ? Rect.Intersect(clipBounds, screenClip) : screenClip;
                    found = true;
                }
            }
            current = current.VisualParent;
        }

        // Degenerate zero-area ancestor clip: refuse to cull rather than lean on Empty.Intersect.
        if (found && (clipBounds.Width <= 0 || clipBounds.Height <= 0))
            return false;

        return found; // false ⇒ caller does not cull
    }

    /// <summary>
    /// Axis-aligned screen bounds of a local-space rect under <paramref name="m"/> (four-corner
    /// transform). Inlined here because <c>BoundsAccumulator.TransformBounds</c> is internal to
    /// the Jalium.UI.Media assembly and not visible to Jalium.UI.Core.
    /// </summary>
    private static Rect TransformLocalRectToScreen(Matrix m, double x, double y, double w, double h)
    {
        var p0 = m.Transform(new Point(x, y));
        var p1 = m.Transform(new Point(x + w, y));
        var p2 = m.Transform(new Point(x, y + h));
        var p3 = m.Transform(new Point(x + w, y + h));
        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Updates the desired size of this element.
    /// </summary>
    /// <param name="availableSize">The available size for the element.</param>
    public void Measure(Size availableSize)
    {
        if (Visibility == Visibility.Collapsed)
        {
            _desiredSize = default;
            _isMeasureValid = true;
            return;
        }

        // Short-circuit: if measure is already valid and constraints haven't changed, skip
        if (_isMeasureValid && _previousAvailableSize == availableSize)
            return;

        var oldDesiredSize = _desiredSize;
        _previousAvailableSize = availableSize;

        bool trace = LayoutDiagnostics.IsRecording;
        long startTicks = trace ? Stopwatch.GetTimestamp() : 0;
        Dispatcher.LastLayoutExceptionElement = null;
        try
        {
            _desiredSize = MeasureCore(availableSize);
        }
        catch
        {
            Dispatcher.LastLayoutExceptionElement ??= this;
            throw;
        }
        _isMeasureValid = true;
        if (trace)
        {
            double us = (Stopwatch.GetTimestamp() - startTicks) * 1_000_000.0 / Stopwatch.Frequency;
            LayoutDiagnostics.NotifyMeasure(this, us);
        }

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
            _renderSize = default;
            _isArrangeValid = true;
            return;
        }

        // Short-circuit: if arrange is already valid and final rect hasn't changed, skip
        if (_isArrangeValid && _previousFinalRect == finalRect)
            return;

        // RC4-b 位移钩子前置短路：槽与期望尺寸都没变 ⇒ ArrangeCore 的 alignment 输入
        // 全部相同 ⇒ 槽内位置也不变，无位移可注册（alignment/margin 等属性变化自身走
        // AffectsArrange 失效 → InvalidateArrange 已注册，不依赖本钩子）。这让稳态布局
        // 与滚动中未动的行完全跳过下面两次 O(depth) 屏幕 bounds 走链。
        bool trackDisplacement =
            !_hasArrangedOnce || finalRect != _previousFinalRect || _desiredSize != _previousDesiredSize;
        // 旧 bounds 必须在 InvalidateScreenOffsetCache 抬升全局 epoch 之前捕获（缓存
        // 命中时 O(1)）。此刻整条祖先链 _visualBounds 均为旧值——FrameworkElement.
        // ArrangeCore 先递归 ArrangeOverride、最后才写自身 _visualBounds——所以最顶层
        // 移动元素读到的是真实旧屏幕位置；嵌套移动的后代读到混合坐标幻影矩形，仅构成
        // 无害过失效（真实旧/新位置由该顶层元素的 old∪new 覆盖）。首次 arrange 没有
        // 旧位置可擦，恒用 Empty——带 Effect 的元素在 _renderSize=(0,0) 时
        // GetDirtyRenderBounds 会返回非空的 padding 矩形，且此刻祖先链未布局、位置
        // 错位，注册它只会多擦一块无关区域。
        Rect oldDirtyBounds = trackDisplacement && _hasArrangedOnce ? GetDirtyRenderBounds() : Rect.Empty;

        var oldRenderSize = _renderSize;
        _previousFinalRect = finalRect;
        InvalidateScreenOffsetCache();

        bool trace = LayoutDiagnostics.IsRecording;
        long startTicks = trace ? Stopwatch.GetTimestamp() : 0;
        Dispatcher.LastLayoutExceptionElement = null;
        try
        {
            ArrangeCore(finalRect);
        }
        catch
        {
            Dispatcher.LastLayoutExceptionElement ??= this;
            throw;
        }
        _isArrangeValid = true;
        if (trace)
        {
            double us = (Stopwatch.GetTimestamp() - startTicks) * 1_000_000.0 / Stopwatch.Frequency;
            LayoutDiagnostics.NotifyArrange(this, us);
        }

        _hasArrangedOnce = true;
        _previousDesiredSize = _desiredSize;

        if (trackDisplacement)
        {
            // RC4-b：父级驱动的位移（reflow 推动的兄弟、Canvas 偏移、邻居尺寸变化引发
            // 的 alignment 平移）只会走到这里——元素自身没有任何失效调用，窗口脏集里
            // 没有它：旧像素永不擦除、新位置永不 present（此前被"每帧 promote 全绘"
            // 回路掩盖）。此处注册旧 AABB + 元素自身。纯位移刻意不 SetRenderDirty：
            // 父级 child-render 循环每帧活读子偏移，retained 录制仍然有效（虚拟化滚动
            // 每帧行位移若强制重录，正是 retained 缓存要避免的形态）。也刻意不
            // InvalidateWindow：帧内 UpdateLayout 的注册被同帧 ComputeDirtyRegions
            // 消费（脏集 swap 在 UpdateLayout 之后）；帧外 layout 必然由某次
            // InvalidateMeasure/InvalidateArrange 触发，调度已由 InvalidateLayoutVisual
            // 完成。
            // 祖先 clip 剔除（收敛虚拟化滚动脏区）：旧、新 AABB 若都完全落在祖先裁剪链
            // （ScrollViewer 视口）之外，本元素这帧会被 Visual.ShouldRenderChild 剔除、画不
            // 出，其旧像素上帧也在 clip 外无需擦除 ⇒ 完全跳过全部四条脏区通道。目标：让脏区
            // bounding box 从 ≈3×视口（视口 + 上下各一 cache band）收敛到 ≈视口 + O(边缘行
            // 高)，消除"中窗列表被 promote 50% 阈值误判转全窗重绘"。
            //   · 三态契约：唯一 cull 条件是旧且新都严格在 clip 外；跨界行（滚入/滚出视口边
            //     缘，旧或新与 clip 有交）仍走 full 注册，其溢出 clip ≤1 行高的部分进 aggregator
            //     （不足以触发 promote、渲染时被剔除），刻意接受。
            //   · 保守退化：祖先带 RenderTransform/RenderOffset → TryGetAncestorClipScreenBounds
            //     放弃整链返回 false → 回退现状 full 注册，永不欠擦除。
            //   · 隐式耦合契约：clip 用 new 时刻走链、拿去交【旧】AABB，仅当 clip 祖先本帧不动
            //     才严格同帧同坐标系。clip 祖先若移动/resize，必经自身 InvalidateArrange→本
            //     RC4-b 分支，其旧 AABB（自身 epoch bump 前捕获=旧 clip 区）经 AddDirtyRect 提交，
            //     覆盖任何被误 cull 后代的旧像素——由回归测试 SvTranslateAndRowDisplace 锁死。
            //   · 刻意不做：不裁跨界行的新位置通道（post-layout/PreLayoutBounds 在消费侧现算，
            //     要裁需改 ComputeDirtyRegions、牵动所有 dirty 元素口径与祖先 RenderOffset 补偿）；
            //     不缓存 clip 交集（Clip/ClipToBounds 变化不 bump 屏幕偏移 epoch，缓存必陈旧）。
            var newDirtyBounds = GetDirtyRenderBounds();
            if (newDirtyBounds != oldDirtyBounds)
            {
                var windowHost = GetWindowHostOrNull();
                if (windowHost != null)
                {
                    // new 先于 old：视口内行（多数）new∩clip≠∅ 即在第一个 Intersect 短路，
                    // 省掉一次注定被推翻的 old 交集。TryGetAncestorClipScreenBounds 返回 false
                    // （无 clip 祖先 / 已退化）时 && 首项即假，走原样 full 注册。
                    bool cull = TryGetAncestorClipScreenBounds(out var clip)
                                && Rect.Intersect(newDirtyBounds, clip).IsEmpty
                                && Rect.Intersect(oldDirtyBounds, clip).IsEmpty;
                    if (!cull)
                    {
                        if (!oldDirtyBounds.IsEmpty)
                            windowHost.AddDirtyRect(oldDirtyBounds);
                        windowHost.AddDirtyElement(this);
                    }
                }
            }
        }
        else if (_renderSize != oldRenderSize)
        {
            // 前置短路跳过了捕获（槽、期望尺寸未变）但 ArrangeOverride 仍返回了不同
            // 尺寸（内容驱动、DesiredSize 被 clamp 的面板形态）。旧 AABB 没捕到——
            // 注册元素本身即可：条目的 PrevPaintedBounds（LastDirtyBounds 通道）会把
            // 上次真实画过的位置一并提交擦除。
            // （对称性说明：此分支是内容驱动 resize、非滚动 band 来源——滚动中静止行既
            // 不改 finalRect 也不 resize——故刻意不参与上面的祖先 clip 剔除。）
            GetWindowHostOrNull()?.AddDirtyElement(this);
        }

        // If render size changed, mark this element as needing re-render
        if (_renderSize != oldRenderSize)
        {
            OnRenderSizeChanged(new SizeChangedInfo(
                this,
                oldRenderSize,
                oldRenderSize.Width != _renderSize.Width,
                oldRenderSize.Height != _renderSize.Height));
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
        return default(Size);
    }

    /// <summary>
    /// Override to implement custom arrange logic.
    /// </summary>
    /// <param name="finalRect">The final rectangle.</param>
    protected virtual void ArrangeCore(Rect finalRect)
    {
        _renderSize = new Size(finalRect.Width, finalRect.Height);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            // Visibility 变化必须同时触发整条 layout 链（Measure / Arrange）和渲染链
            // （Visual）失效，且需要**递归整个子树**：从 Collapsed→Visible 切换时，
            // 子元素之前没参与 paint，PaintCommand 不存在，仅标父元素脏不够。
            // 同时父级也要重新 layout（留出/收回空间）。
            // 这是 framework 关键 bug 的修复 — 之前 Visibility 切换会出现
            // "脏渲染"（要鼠标移开触发别的失效才会顺带重绘）。
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
            InvalidateVisualRecursive(element);

            // 父级也需要重新 layout — 子元素 Collapsed/Visible 切换会改变父级的
            // 可用空间分配。也要 InvalidateVisual 以擦除/绘制子元素留下的痕迹。
            if (element.VisualParent is UIElement parent)
            {
                parent.InvalidateMeasure();
                parent.InvalidateArrange();
                parent.InvalidateVisual();
            }

            element.UpdateIsVisibleFromTree();
        }
    }

    /// <summary>
    /// 递归把整个 visual 子树标记为渲染脏 — Visibility 从 Collapsed 切回 Visible 时
    /// 子树之前未参与 paint，PaintCommand 链上不存在，所以仅标自己脏不够，
    /// 必须主动让每个后代都重绘一次。
    /// </summary>
    private static void InvalidateVisualRecursive(UIElement element)
    {
        var count = element.VisualChildrenCount;
        for (int i = 0; i < count; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
            {
                child.InvalidateVisual();
                InvalidateVisualRecursive(child);
            }
        }
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var oldValue = (bool)(e.OldValue ?? true);
            var newValue = (bool)(e.NewValue ?? true);
            element.OnIsEnabledChanged(oldValue, newValue);
            element.RaiseIsEnabledChanged(e);
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
            element.RaiseIsHitTestVisibleChanged(e);
            element.PropagateIsHitTestVisibleToDescendants();
        }
    }

    /// <summary>
    /// Called when the IsEnabled property changes.
    /// </summary>
    protected virtual void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        // When an element becomes (effectively) disabled, drop any lingering
        // interaction state. A control that was hovered or pressed at the
        // moment it got disabled would otherwise keep IsMouseOver/IsPressed
        // true, and its single-condition IsMouseOver/IsPressed triggers would
        // stay active — the disabled control would keep showing hover visuals.
        // This also covers descendants: PropagateIsEnabledToDescendants invokes
        // this method on the whole subtree when an ancestor is disabled.
        if (!IsEnabled)
        {
            if (IsMouseOver)
                SetIsMouseOver(false);
            if (IsPressed)
                SetIsPressed(false);
        }
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
            var oldValue = (bool)(e.OldValue ?? false);
            var newValue = (bool)(e.NewValue ?? false);
            element.OnIsFocusedChanged(oldValue, newValue);

            // DependencyObject suppresses equal-value callbacks, but retain this guard so
            // logical focus is explicitly tied to a real state transition if that changes.
            if (oldValue != newValue)
            {
                var routedEvent = newValue ? GotFocusEvent : LostFocusEvent;
                var args = new RoutedEventArgs(routedEvent, element);
                if (newValue)
                {
                    element.OnGotFocus(args);
                }
                else
                {
                    element.OnLostFocus(args);
                }
            }
        }
    }

    /// <summary>
    /// Raised whenever any element's <see cref="IsKeyboardFocused"/> state transitions.
    /// Used by higher layers (e.g. the focus visual adorner manager in Jalium.UI.Controls)
    /// to react to focus changes without taking a dependency on the Controls assembly.
    /// </summary>
    internal static event Action<UIElement, bool>? IsKeyboardFocusedChangedStatic;

    private static void OnIsKeyboardFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            var isFocused = (bool)(e.NewValue ?? false);
            if (element.IsFocused != isFocused)
            {
                element.SetIsFocused(isFocused);
            }

            element.OnIsKeyboardFocusedChanged(e);
            element.RaiseIsKeyboardFocusedChanged(e);
            element.InvalidateVisual();

            // Notify UIA of focus change
            if (isFocused)
            {
                var peer = element.GetAutomationPeer();
                if (peer != null)
                    Automation.Peers.AutomationPeer.EventSink?.OnFocusChanged(peer);
            }

            IsKeyboardFocusedChangedStatic?.Invoke(element, isFocused);
        }
    }

    private static void OnIsKeyboardFocusWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.OnIsKeyboardFocusWithinChanged(e);
            element.RaiseIsKeyboardFocusWithinChanged(e);
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
    internal virtual void OnIsPressedChanged(bool oldValue, bool newValue)
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

    /// <summary>
    /// Called when this element receives logical focus.
    /// </summary>
    /// <param name="e">The routed event data.</param>
    protected virtual void OnGotFocus(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Called when this element loses logical focus.
    /// </summary>
    /// <param name="e">The routed event data.</param>
    protected virtual void OnLostFocus(RoutedEventArgs e)
    {
        RaiseEvent(e);
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
            // 先同步字段镜像再进虚方法：override 不调 base 也不能让
            // GetDirtyRenderBounds 的扩边判断落后于真实 Effect。
            element._effectForDirtyBounds = e.NewValue as IEffect;
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

    private static void OnCacheModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.OldValue is CacheMode oldCache)
        {
            oldCache.Changed -= element.OnCacheModePropertyChanged;
        }

        if (e.NewValue is CacheMode newCache)
        {
            newCache.Changed += element.OnCacheModePropertyChanged;
        }

        element.InvalidateVisual();
    }

    private void OnCacheModePropertyChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static void OnBitmapEffectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateVisual();
        }
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

    /// <summary>
    /// Property-changed callback for "composition-only" DPs whose values are read
    /// live by the parent's child-render loop (Opacity, RenderTransform,
    /// RenderTransformOrigin). Schedules a present without invalidating the
    /// retained-mode cache — see <see cref="InvalidateComposition"/>.
    /// </summary>
    private static void OnCompositionPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            element.InvalidateComposition();
        }
    }

    /// <summary>
    /// Property-changed callback for <see cref="RenderTransformProperty"/>. In
    /// addition to the standard composition invalidation, this manages
    /// subscription to the <see cref="Transform.Changed"/> event so that
    /// mutating a sub-property of the assigned Transform (e.g.
    /// <c>translateTransform.X</c>, <c>scaleTransform.ScaleX</c>) re-invalidates
    /// composition without requiring callers to reassign the Transform.
    /// </summary>
    private static void OnRenderTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if (e.OldValue is Transform oldTransform)
        {
            oldTransform.Changed -= element.OnRenderTransformSubPropertyChanged;
        }

        if (e.NewValue is Transform newTransform)
        {
            newTransform.Changed += element.OnRenderTransformSubPropertyChanged;
        }

        element.InvalidateComposition();
    }

    private void OnRenderTransformSubPropertyChanged(object? sender, EventArgs e)
    {
        InvalidateComposition();
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
        UpdateMouseCaptureDependencyState(previousCaptured, this);

        // Tell Win32 to keep sending mouse messages even when cursor is outside the window
        GetWindowHostOrNull()?.SetNativeCapture();

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
            var windowHost = GetWindowHostOrNull();
            var previousCaptured = _mouseCaptured;
            _mouseCaptured = null;
            UpdateMouseCaptureDependencyState(previousCaptured, null);
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
            var windowHost = captured.GetWindowHostOrNull();
            _mouseCaptured = null;
            UpdateMouseCaptureDependencyState(captured, null);
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
            UpdateMouseCaptureDependencyState(captured, null);
            captured.RaiseMouseCaptureChanged(false);
        }
    }

    /// <summary>
    /// Identifies the GotMouseCapture routed event.
    /// </summary>
    public static readonly RoutedEvent GotMouseCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(GotMouseCapture), RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the LostMouseCapture routed event.
    /// </summary>
    public static readonly RoutedEvent LostMouseCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(LostMouseCapture), RoutingStrategy.Bubble, typeof(MouseEventHandler), typeof(UIElement));

    /// <summary>
    /// Occurs when this element captures the mouse.
    /// </summary>
    public event MouseEventHandler GotMouseCapture
    {
        add => AddHandler(GotMouseCaptureEvent, value);
        remove => RemoveHandler(GotMouseCaptureEvent, value);
    }

    /// <summary>
    /// Occurs when this element loses mouse capture.
    /// </summary>
    public event MouseEventHandler LostMouseCapture
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
            var args = new MouseEventArgs(GotMouseCaptureEvent) { Source = this };
            OnGotMouseCapture(args);
            RaiseEvent(args);
        }
        else
        {
            var args = new MouseEventArgs(LostMouseCaptureEvent) { Source = this };
            OnLostMouseCapture(args);
            RaiseEvent(args);
        }
    }

    /// <summary>Called when this element captures the mouse.</summary>
    protected virtual void OnGotMouseCapture(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnGotMouseCapture();
    }

    /// <summary>Called when this element loses mouse capture.</summary>
    protected virtual void OnLostMouseCapture(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        OnLostMouseCapture();
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

    #region Stylus State And Capture

    private static UIElement? _stylusDirectlyOver;
    private static UIElement? _stylusCaptured;

    /// <summary>
    /// Gets a value indicating whether the stylus is over this element or one of its descendants.
    /// </summary>
    public bool IsStylusOver
    {
        get
        {
            Visual? current = _stylusDirectlyOver;
            while (current != null)
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }

                current = current.VisualParent;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the stylus is directly over this element.
    /// </summary>
    public bool IsStylusDirectlyOver => ReferenceEquals(_stylusDirectlyOver, this);

    /// <summary>
    /// Gets a value indicating whether this element has stylus capture.
    /// </summary>
    public bool IsStylusCaptured => ReferenceEquals(_stylusCaptured, this);

    internal static UIElement? StylusCapturedElement => _stylusCaptured;
    internal static Func<UIElement, bool>? StylusCaptureRequested { get; set; }
    internal static Action<UIElement>? StylusCaptureReleaseRequested { get; set; }

    internal static void SetStylusDirectlyOverElement(UIElement? element)
    {
        var previous = _stylusDirectlyOver;
        _stylusDirectlyOver = element;
        UpdateStylusDirectlyOverDependencyState(previous, element);
    }

    internal static void SetStylusCapturedElement(UIElement? element)
    {
        var previous = _stylusCaptured;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        _stylusCaptured = element;
        UpdateStylusCaptureDependencyState(previous, element);

        if (Tablet.CurrentStylusDevice is { } device)
        {
            if (previous != null)
            {
                previous.RaiseEvent(new StylusEventArgs(device, Environment.TickCount)
                {
                    RoutedEvent = LostStylusCaptureEvent,
                    Source = previous,
                });
            }

            if (element != null)
            {
                element.RaiseEvent(new StylusEventArgs(device, Environment.TickCount)
                {
                    RoutedEvent = GotStylusCaptureEvent,
                    Source = element,
                });
            }
        }
    }

    /// <summary>
    /// Captures the current stylus to this element.
    /// </summary>
    public bool CaptureStylus()
    {
        if (!IsEnabled || Visibility != Visibility.Visible)
        {
            return false;
        }

        return StylusCaptureRequested?.Invoke(this) == true;
    }

    /// <summary>
    /// Releases stylus capture from this element.
    /// </summary>
    public void ReleaseStylusCapture()
    {
        if (IsStylusCaptured)
        {
            StylusCaptureReleaseRequested?.Invoke(this);
        }
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
        TextCompositionManager.PreviewTextInputEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the TextInput routed event.
    /// </summary>
    public static readonly RoutedEvent TextInputEvent =
        TextCompositionManager.TextInputEvent.AddOwner(typeof(UIElement));

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
    /// Raised by <see cref="OnPreviewMouseDownThunk"/> on every element along the tunneling
    /// PreviewMouseDown path when <c>ChangedButton == Left</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseLeftButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseLeftButtonDown routed event.
    /// Raised by <see cref="OnMouseDownThunk"/> on every element along the bubbling
    /// MouseDown path when <c>ChangedButton == Left</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent MouseLeftButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseLeftButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseLeftButtonUp routed event.
    /// Raised by <see cref="OnPreviewMouseUpThunk"/> on every element along the tunneling
    /// PreviewMouseUp path when <c>ChangedButton == Left</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseLeftButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseLeftButtonUp routed event.
    /// Raised by <see cref="OnMouseUpThunk"/> on every element along the bubbling
    /// MouseUp path when <c>ChangedButton == Left</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent MouseLeftButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseLeftButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseRightButtonDown routed event.
    /// Raised by <see cref="OnPreviewMouseDownThunk"/> on every element along the tunneling
    /// PreviewMouseDown path when <c>ChangedButton == Right</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseRightButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseRightButtonDown routed event.
    /// Raised by <see cref="OnMouseDownThunk"/> on every element along the bubbling
    /// MouseDown path when <c>ChangedButton == Right</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent MouseRightButtonDownEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseRightButtonDown), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewMouseRightButtonUp routed event.
    /// Raised by <see cref="OnPreviewMouseUpThunk"/> on every element along the tunneling
    /// PreviewMouseUp path when <c>ChangedButton == Right</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewMouseRightButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the MouseRightButtonUp routed event.
    /// Raised by <see cref="OnMouseUpThunk"/> on every element along the bubbling
    /// MouseUp path when <c>ChangedButton == Right</c> — WPF-compatible behavior.
    /// </summary>
    public static readonly RoutedEvent MouseRightButtonUpEvent =
        EventManager.RegisterRoutedEvent(nameof(MouseRightButtonUp), RoutingStrategy.Direct, typeof(MouseButtonEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchDown), RoutingStrategy.Tunnel, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchDown routed event.
    /// </summary>
    public static readonly RoutedEvent TouchDownEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchDown), RoutingStrategy.Bubble, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchMove), RoutingStrategy.Tunnel, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchMove routed event.
    /// </summary>
    public static readonly RoutedEvent TouchMoveEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchMove), RoutingStrategy.Bubble, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewTouchUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewTouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewTouchUp), RoutingStrategy.Tunnel, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchUp routed event.
    /// </summary>
    public static readonly RoutedEvent TouchUpEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchUp), RoutingStrategy.Bubble, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchEnter routed event.
    /// </summary>
    public static readonly RoutedEvent TouchEnterEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchEnter), RoutingStrategy.Direct, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the TouchLeave routed event.
    /// </summary>
    public static readonly RoutedEvent TouchLeaveEvent =
        EventManager.RegisterRoutedEvent(nameof(TouchLeave), RoutingStrategy.Direct, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the GotTouchCapture routed event.
    /// </summary>
    public static readonly RoutedEvent GotTouchCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(GotTouchCapture), RoutingStrategy.Bubble, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the LostTouchCapture routed event.
    /// </summary>
    public static readonly RoutedEvent LostTouchCaptureEvent =
        EventManager.RegisterRoutedEvent(nameof(LostTouchCapture), RoutingStrategy.Bubble, typeof(TouchEventHandler), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusDown routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusDownEvent =
        Stylus.PreviewStylusDownEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusDown routed event.
    /// </summary>
    public static readonly RoutedEvent StylusDownEvent =
        Stylus.StylusDownEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusMove routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusMoveEvent =
        Stylus.PreviewStylusMoveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusMove routed event.
    /// </summary>
    public static readonly RoutedEvent StylusMoveEvent =
        Stylus.StylusMoveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewStylusUp routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewStylusUpEvent =
        Stylus.PreviewStylusUpEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusUp routed event.
    /// </summary>
    public static readonly RoutedEvent StylusUpEvent =
        Stylus.StylusUpEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusInAirMove routed event.
    /// </summary>
    public static readonly RoutedEvent StylusInAirMoveEvent =
        Stylus.StylusInAirMoveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusEnter routed event.
    /// </summary>
    public static readonly RoutedEvent StylusEnterEvent =
        Stylus.StylusEnterEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusLeave routed event.
    /// </summary>
    public static readonly RoutedEvent StylusLeaveEvent =
        Stylus.StylusLeaveEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusInRange routed event.
    /// </summary>
    public static readonly RoutedEvent StylusInRangeEvent =
        Stylus.StylusInRangeEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusOutOfRange routed event.
    /// </summary>
    public static readonly RoutedEvent StylusOutOfRangeEvent =
        Stylus.StylusOutOfRangeEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusButtonDown routed event.
    /// </summary>
    public static readonly RoutedEvent StylusButtonDownEvent =
        Stylus.StylusButtonDownEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusButtonUp routed event.
    /// </summary>
    public static readonly RoutedEvent StylusButtonUpEvent =
        Stylus.StylusButtonUpEvent.AddOwner(typeof(UIElement));

    /// <summary>
    /// Identifies the StylusSystemGesture routed event.
    /// </summary>
    public static readonly RoutedEvent StylusSystemGestureEvent =
        Stylus.StylusSystemGestureEvent.AddOwner(typeof(UIElement));

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
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationStarting), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationStartingEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationStarting routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationStarting), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationStartingEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationStarted routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationStarted), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationStartedEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationStarted routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationStarted), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationStartedEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationDelta routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationDeltaEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationDelta), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationDeltaEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationDelta routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationDeltaEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationDelta), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationDeltaEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationInertiaStarting routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationInertiaStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationInertiaStarting), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationInertiaStartingEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationInertiaStarting routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationInertiaStartingEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationInertiaStarting), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationInertiaStartingEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationBoundaryFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationBoundaryFeedbackEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationBoundaryFeedback), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationBoundaryFeedbackEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationBoundaryFeedback routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationBoundaryFeedbackEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationBoundaryFeedback), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationBoundaryFeedbackEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the PreviewManipulationCompleted routed event.
    /// </summary>
    public static readonly RoutedEvent PreviewManipulationCompletedEvent =
        EventManager.RegisterRoutedEvent(nameof(PreviewManipulationCompleted), RoutingStrategy.Tunnel, typeof(EventHandler<ManipulationCompletedEventArgs>), typeof(UIElement));

    /// <summary>
    /// Identifies the ManipulationCompleted routed event.
    /// </summary>
    public static readonly RoutedEvent ManipulationCompletedEvent =
        EventManager.RegisterRoutedEvent(nameof(ManipulationCompleted), RoutingStrategy.Bubble, typeof(EventHandler<ManipulationCompletedEventArgs>), typeof(UIElement));

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
    public event TextCompositionEventHandler PreviewTextInput
    {
        add => AddHandler(PreviewTextInputEvent, value);
        remove => RemoveHandler(PreviewTextInputEvent, value);
    }

    /// <summary>
    /// Occurs when text is input (bubble).
    /// </summary>
    public event TextCompositionEventHandler TextInput
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
    public event TouchEventHandler PreviewTouchDown
    {
        add => AddHandler(PreviewTouchDownEvent, value);
        remove => RemoveHandler(PreviewTouchDownEvent, value);
    }

    /// <summary>
    /// Occurs when touch begins (bubble).
    /// </summary>
    public event TouchEventHandler TouchDown
    {
        add => AddHandler(TouchDownEvent, value);
        remove => RemoveHandler(TouchDownEvent, value);
    }

    /// <summary>
    /// Occurs when touch moves (tunnel).
    /// </summary>
    public event TouchEventHandler PreviewTouchMove
    {
        add => AddHandler(PreviewTouchMoveEvent, value);
        remove => RemoveHandler(PreviewTouchMoveEvent, value);
    }

    /// <summary>
    /// Occurs when touch moves (bubble).
    /// </summary>
    public event TouchEventHandler TouchMove
    {
        add => AddHandler(TouchMoveEvent, value);
        remove => RemoveHandler(TouchMoveEvent, value);
    }

    /// <summary>
    /// Occurs when touch ends (tunnel).
    /// </summary>
    public event TouchEventHandler PreviewTouchUp
    {
        add => AddHandler(PreviewTouchUpEvent, value);
        remove => RemoveHandler(PreviewTouchUpEvent, value);
    }

    /// <summary>
    /// Occurs when touch ends (bubble).
    /// </summary>
    public event TouchEventHandler TouchUp
    {
        add => AddHandler(TouchUpEvent, value);
        remove => RemoveHandler(TouchUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact begins (tunnel).
    /// </summary>
    public event StylusDownEventHandler PreviewStylusDown
    {
        add => AddHandler(PreviewStylusDownEvent, value);
        remove => RemoveHandler(PreviewStylusDownEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact begins (bubble).
    /// </summary>
    public event StylusDownEventHandler StylusDown
    {
        add => AddHandler(StylusDownEvent, value);
        remove => RemoveHandler(StylusDownEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves (tunnel).
    /// </summary>
    public event StylusEventHandler PreviewStylusMove
    {
        add => AddHandler(PreviewStylusMoveEvent, value);
        remove => RemoveHandler(PreviewStylusMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves (bubble).
    /// </summary>
    public event StylusEventHandler StylusMove
    {
        add => AddHandler(StylusMoveEvent, value);
        remove => RemoveHandler(StylusMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact ends (tunnel).
    /// </summary>
    public event StylusEventHandler PreviewStylusUp
    {
        add => AddHandler(PreviewStylusUpEvent, value);
        remove => RemoveHandler(PreviewStylusUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus contact ends (bubble).
    /// </summary>
    public event StylusEventHandler StylusUp
    {
        add => AddHandler(StylusUpEvent, value);
        remove => RemoveHandler(StylusUpEvent, value);
    }

    /// <summary>
    /// Occurs when stylus moves in air.
    /// </summary>
    public event StylusEventHandler StylusInAirMove
    {
        add => AddHandler(StylusInAirMoveEvent, value);
        remove => RemoveHandler(StylusInAirMoveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus enters this element.
    /// </summary>
    public event StylusEventHandler StylusEnter
    {
        add => AddHandler(StylusEnterEvent, value);
        remove => RemoveHandler(StylusEnterEvent, value);
    }

    /// <summary>
    /// Occurs when stylus leaves this element.
    /// </summary>
    public event StylusEventHandler StylusLeave
    {
        add => AddHandler(StylusLeaveEvent, value);
        remove => RemoveHandler(StylusLeaveEvent, value);
    }

    /// <summary>
    /// Occurs when stylus enters detection range.
    /// </summary>
    public event StylusEventHandler StylusInRange
    {
        add => AddHandler(StylusInRangeEvent, value);
        remove => RemoveHandler(StylusInRangeEvent, value);
    }

    /// <summary>
    /// Occurs when stylus exits detection range.
    /// </summary>
    public event StylusEventHandler StylusOutOfRange
    {
        add => AddHandler(StylusOutOfRangeEvent, value);
        remove => RemoveHandler(StylusOutOfRangeEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus button is pressed.
    /// </summary>
    public event StylusButtonEventHandler StylusButtonDown
    {
        add => AddHandler(StylusButtonDownEvent, value);
        remove => RemoveHandler(StylusButtonDownEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus button is released.
    /// </summary>
    public event StylusButtonEventHandler StylusButtonUp
    {
        add => AddHandler(StylusButtonUpEvent, value);
        remove => RemoveHandler(StylusButtonUpEvent, value);
    }

    /// <summary>
    /// Occurs when a stylus system gesture is recognized.
    /// </summary>
    public event StylusSystemGestureEventHandler StylusSystemGesture
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
    public event EventHandler<ManipulationStartingEventArgs> PreviewManipulationStarting
    {
        add => AddHandler(PreviewManipulationStartingEvent, value);
        remove => RemoveHandler(PreviewManipulationStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation is starting (bubble).
    /// </summary>
    public event EventHandler<ManipulationStartingEventArgs> ManipulationStarting
    {
        add => AddHandler(ManipulationStartingEvent, value);
        remove => RemoveHandler(ManipulationStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation has started (tunnel).
    /// </summary>
    public event EventHandler<ManipulationStartedEventArgs> PreviewManipulationStarted
    {
        add => AddHandler(PreviewManipulationStartedEvent, value);
        remove => RemoveHandler(PreviewManipulationStartedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation has started (bubble).
    /// </summary>
    public event EventHandler<ManipulationStartedEventArgs> ManipulationStarted
    {
        add => AddHandler(ManipulationStartedEvent, value);
        remove => RemoveHandler(ManipulationStartedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation delta is produced (tunnel).
    /// </summary>
    public event EventHandler<ManipulationDeltaEventArgs> PreviewManipulationDelta
    {
        add => AddHandler(PreviewManipulationDeltaEvent, value);
        remove => RemoveHandler(PreviewManipulationDeltaEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation delta is produced (bubble).
    /// </summary>
    public event EventHandler<ManipulationDeltaEventArgs> ManipulationDelta
    {
        add => AddHandler(ManipulationDeltaEvent, value);
        remove => RemoveHandler(ManipulationDeltaEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation inertia starts (tunnel).
    /// </summary>
    public event EventHandler<ManipulationInertiaStartingEventArgs> PreviewManipulationInertiaStarting
    {
        add => AddHandler(PreviewManipulationInertiaStartingEvent, value);
        remove => RemoveHandler(PreviewManipulationInertiaStartingEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation inertia starts (bubble).
    /// </summary>
    public event EventHandler<ManipulationInertiaStartingEventArgs> ManipulationInertiaStarting
    {
        add => AddHandler(ManipulationInertiaStartingEvent, value);
        remove => RemoveHandler(ManipulationInertiaStartingEvent, value);
    }

    /// <summary>
    /// Occurs when boundary feedback is raised (tunnel).
    /// </summary>
    public event EventHandler<ManipulationBoundaryFeedbackEventArgs> PreviewManipulationBoundaryFeedback
    {
        add => AddHandler(PreviewManipulationBoundaryFeedbackEvent, value);
        remove => RemoveHandler(PreviewManipulationBoundaryFeedbackEvent, value);
    }

    /// <summary>
    /// Occurs when boundary feedback is raised (bubble).
    /// </summary>
    public event EventHandler<ManipulationBoundaryFeedbackEventArgs> ManipulationBoundaryFeedback
    {
        add => AddHandler(ManipulationBoundaryFeedbackEvent, value);
        remove => RemoveHandler(ManipulationBoundaryFeedbackEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation completes (tunnel).
    /// </summary>
    public event EventHandler<ManipulationCompletedEventArgs> PreviewManipulationCompleted
    {
        add => AddHandler(PreviewManipulationCompletedEvent, value);
        remove => RemoveHandler(PreviewManipulationCompletedEvent, value);
    }

    /// <summary>
    /// Occurs when manipulation completes (bubble).
    /// </summary>
    public event EventHandler<ManipulationCompletedEventArgs> ManipulationCompleted
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
    protected virtual void OnPreviewTextInput(TextCompositionEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TextInput attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTextInput(TextCompositionEventArgs e)
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
    protected virtual void OnPreviewTouchDown(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchDown(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewTouchMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTouchMove(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchMove(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewTouchUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewTouchUp(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled TouchUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnTouchUp(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when a touch contact enters the bounds of this element.
    /// </summary>
    protected virtual void OnTouchEnter(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when a touch contact leaves the bounds of this element.
    /// </summary>
    protected virtual void OnTouchLeave(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when this element captures a touch contact.
    /// </summary>
    protected virtual void OnGotTouchCapture(TouchEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when this element loses a captured touch contact.
    /// </summary>
    protected virtual void OnLostTouchCapture(TouchEventArgs e)
    {
    }

    // ── Stylus ──

    /// <summary>
    /// Invoked when an unhandled PreviewStylusDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusDown(StylusDownEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusDown(StylusDownEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewStylusMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusMove(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusMove(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled PreviewStylusUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnPreviewStylusUp(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusUp(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusInAirMove attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusInAirMove(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusEnter attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusEnter(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusLeave attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusLeave(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusInRange attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusInRange(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusOutOfRange attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusOutOfRange(StylusEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusButtonDown attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusButtonUp attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    /// <summary>
    /// Invoked when an unhandled StylusSystemGesture attached event reaches this element. Override to handle this event.
    /// </summary>
    protected virtual void OnStylusSystemGesture(StylusSystemGestureEventArgs e)
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

    /// <summary>
    /// Reusable registration handle with the central <see cref="Animation.AnimationManager"/>.
    /// Created once on first animation and reused across register/unregister
    /// cycles. Weak so the manager never becomes the GC root that keeps a
    /// forgotten element's Forever animation alive.
    /// </summary>
    private Animation.AnimationTickSubscription? _animationTickSubscription;

    private enum ElementAnimationKind
    {
        Explicit,
        AutomaticTransition,
        Storyboard
    }

    private sealed class ElementAnimation
    {
        public IAnimationTimeline Animation { get; }
        public IAnimationClock Clock { get; }
        public object? BaseValue { get; }
        public ElementAnimationKind Kind { get; }
        public bool StartPending { get; private set; }

        /// <summary>
        /// Snapshot of the property's effective value taken at the instant this
        /// animation replaced a previous one (HandoffBehavior.SnapshotAndReplace):
        /// used as the origin value so a To-only animation takes over smoothly
        /// from the current visual value instead of jumping back to base.
        /// </summary>
        public object? HandoffSnapshot { get; }
        public bool HasHandoffSnapshot { get; }

        /// <summary>
        /// For Storyboard-kind entries: the storyboard that must be told when
        /// this clock is terminated from the element side and will therefore
        /// never complete naturally (settlement bookkeeping, prevents the
        /// storyboard from waiting forever in its static active set).
        /// </summary>
        public IStoryboardClockOwner? StoryboardOwner { get; }

        public ElementAnimation(
            IAnimationTimeline animation,
            IAnimationClock clock,
            object? baseValue,
            ElementAnimationKind kind,
            bool startPending,
            object? handoffSnapshot = null,
            bool hasHandoffSnapshot = false,
            IStoryboardClockOwner? storyboardOwner = null)
        {
            Animation = animation;
            Clock = clock;
            BaseValue = baseValue;
            Kind = kind;
            StartPending = startPending;
            HandoffSnapshot = handoffSnapshot;
            HasHandoffSnapshot = hasHandoffSnapshot;
            StoryboardOwner = storyboardOwner;
        }

        public bool ConsumePendingStart()
        {
            if (!StartPending)
                return false;

            StartPending = false;
            return true;
        }
    }

    /// <summary>Applies an animation clock to the specified dependency property.</summary>
    public void ApplyAnimationClock(DependencyProperty dp, Media.Animation.AnimationClock? clock)
    {
        ApplyAnimationClock(dp, clock, Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Applies an animation clock with the requested handoff behavior.</summary>
    public void ApplyAnimationClock(
        DependencyProperty dp,
        Media.Animation.AnimationClock? clock,
        Media.Animation.HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }

        IAnimationTimeline? animation = clock?.Timeline as IAnimationTimeline;
        if (clock is not null && animation is null)
        {
            throw new ArgumentException(
                "The clock must be backed by an AnimationTimeline.",
                nameof(clock));
        }

        _ = BeginAnimationCore(
            dp,
            animation,
            handoffBehavior,
            ElementAnimationKind.Explicit,
            clearAnimatedValueOnReplace: true,
            allowAutomaticToReplaceExplicit: true,
            existingClock: clock);
    }

    /// <summary>Starts an animation using the WPF-compatible animation timeline.</summary>
    public void BeginAnimation(DependencyProperty dp, Media.Animation.AnimationTimeline? animation)
    {
        BeginAnimation(dp, animation, Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Starts an animation using the WPF-compatible handoff behavior.</summary>
    public void BeginAnimation(
        DependencyProperty dp,
        Media.Animation.AnimationTimeline? animation,
        Media.Animation.HandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }

        _ = BeginAnimationCore(
            dp,
            animation,
            handoffBehavior,
            ElementAnimationKind.Explicit,
            clearAnimatedValueOnReplace: true,
            allowAutomaticToReplaceExplicit: true);
    }

    /// <summary>
    /// Starts an animation for the specified dependency property through the core animation abstraction.
    /// </summary>
    /// <param name="dp">The dependency property to animate.</param>
    /// <param name="animation">The animation timeline, or null to stop any existing animation.</param>
    internal void BeginAnimation(DependencyProperty dp, IAnimationTimeline? animation)
    {
        BeginAnimation(dp, animation, Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// Starts an animation for the specified dependency property with a handoff behavior.
    /// </summary>
    /// <param name="dp">The dependency property to animate.</param>
    /// <param name="animation">The animation timeline, or null to stop any existing animation.</param>
    /// <param name="handoffBehavior">How to handle existing animations. With
    /// <see cref="Media.Animation.HandoffBehavior.SnapshotAndReplace"/> the currently displayed value is
    /// captured at the replacement instant and used as the new animation's origin;
    /// <see cref="Media.Animation.HandoffBehavior.Compose"/> currently degrades to the same behavior.</param>
    internal void BeginAnimation(
        DependencyProperty dp,
        IAnimationTimeline? animation,
        Media.Animation.HandoffBehavior handoffBehavior)
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
        Media.Animation.HandoffBehavior handoffBehavior,
        ElementAnimationKind kind,
        bool clearAnimatedValueOnReplace,
        bool allowAutomaticToReplaceExplicit,
        object? initialAnimatedValue = null,
        bool useInitialAnimatedValue = false,
        bool deferClockBeginUntilRendering = false,
        IAnimationClock? existingClock = null,
        IStoryboardClockOwner? storyboardOwner = null)
    {
        _activeAnimations ??= new Dictionary<DependencyProperty, ElementAnimation>();

        object? handoffSnapshot = null;
        var hasHandoffSnapshot = false;

        // Stop any existing animation on this property
        if (_activeAnimations.TryGetValue(dp, out var existing))
        {
            // An automatic transition may never replace an explicit or
            // storyboard-driven animation: those own the property until they
            // finish or are explicitly stopped.
            if (existing.Kind != ElementAnimationKind.AutomaticTransition &&
                kind == ElementAnimationKind.AutomaticTransition &&
                !allowAutomaticToReplaceExplicit)
            {
                return false;
            }

            // Compose degrades to SnapshotAndReplace (documented on BeginAnimation):
            // both take the snapshot, otherwise a Compose caller would restart the
            // new animation from the base value — a visible jump when the old
            // animation was mid-flight with FillBehavior.Stop.
            if (animation != null &&
                (handoffBehavior == Media.Animation.HandoffBehavior.SnapshotAndReplace ||
                 handoffBehavior == Media.Animation.HandoffBehavior.Compose))
            {
                // Capture the currently displayed value BEFORE the old animation
                // is removed — the animated layer is still in effect here.
                handoffSnapshot = GetValue(dp);
                hasHandoffSnapshot = true;
            }

            RemoveAnimationCore(dp, existing, clearAnimatedValueOnReplace);
        }

        if (animation == null)
        {
            UnregisterFromAnimationManagerIfIdle();
            return true;
        }

        // Store base value and create clock
        var baseValue = GetAnimationBaseValue(dp);
        var clock = existingClock ?? animation.CreateClock();

        _activeAnimations[dp] = new ElementAnimation(
            animation,
            clock,
            baseValue,
            kind,
            deferClockBeginUntilRendering,
            handoffSnapshot,
            hasHandoffSnapshot,
            storyboardOwner);

        // Subscribe to completion
        clock.Completed += OnAnimationClockCompleted;

        if (!deferClockBeginUntilRendering)
        {
            // Start the clock immediately unless the caller needs the first rendered frame to begin at 0%.
            clock.Begin();
        }

        // Start receiving animation frames
        RegisterWithAnimationManager();

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
    /// Begins a storyboard-driven animation on this element with a clock created
    /// and owned by the storyboard. Tick, value writes and FillBehavior cleanup
    /// run through the element's unified animation path; the storyboard is
    /// notified via <see cref="IStoryboardClockOwner.NotifyClockTerminated"/>
    /// when the entry is terminated from the element side.
    /// </summary>
    internal bool BeginStoryboardAnimation(
        DependencyProperty dp,
        IAnimationTimeline animation,
        IAnimationClock clock,
        IStoryboardClockOwner owner,
        Media.Animation.HandoffBehavior handoffBehavior = Media.Animation.HandoffBehavior.SnapshotAndReplace)
    {
        ArgumentNullException.ThrowIfNull(dp);
        ArgumentNullException.ThrowIfNull(animation);
        ArgumentNullException.ThrowIfNull(clock);

        // Two children of the SAME storyboard targeting one (target, property):
        // the replacement below would settle the first clock as "terminated" and
        // permanently suppress the storyboard's Completed event. Pre-settle it as
        // superseded (counts like natural completion); the terminated notification
        // fired by RemoveAnimationCore is then an idempotent no-op.
        if (TryGetActiveAnimation(dp, out var existing) &&
            ReferenceEquals(existing.StoryboardOwner, owner))
        {
            owner.NotifyClockSuperseded(existing.Clock);
        }

        return BeginAnimationCore(
            dp,
            animation,
            handoffBehavior,
            ElementAnimationKind.Storyboard,
            clearAnimatedValueOnReplace: true,
            allowAutomaticToReplaceExplicit: true,
            existingClock: clock,
            storyboardOwner: owner);
    }

    /// <summary>
    /// Stops a storyboard-driven animation, but only while the property is still
    /// owned by that exact clock — an animation that has since replaced it is
    /// left alone (the replacement already settled the old clock).
    /// </summary>
    internal void StopStoryboardAnimation(DependencyProperty dp, IAnimationClock clock)
    {
        if (!TryGetActiveAnimation(dp, out var existing) || !ReferenceEquals(existing.Clock, clock))
            return;

        RemoveAnimationCore(dp, existing, clearAnimatedValue: true);
        UnregisterFromAnimationManagerIfIdle();
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
        UnregisterFromAnimationManagerIfIdle();
    }

    private void RemoveAnimationCore(DependencyProperty dp, ElementAnimation animation, bool clearAnimatedValue)
    {
        animation.Clock.Stop();
        animation.Clock.Completed -= OnAnimationClockCompleted;
        _activeAnimations?.Remove(dp);

        if (clearAnimatedValue)
        {
            if (animation.Kind == ElementAnimationKind.Storyboard)
            {
                // Explicit Storyboard Stop/Remove and replacement restore the DP's
                // base layer even when the running animation uses HoldEnd. HoldEnd
                // promotes only after natural completion; it must not overwrite the
                // base value during a controllable-clock teardown.
                DiscardAnimatedValue(dp);
            }
            else
            {
                ClearAnimatedValue(dp);
            }
        }

        // A storyboard-driven clock stopped from the element side never fires
        // Completed: settle its bookkeeping so the storyboard can finish and
        // release its static root. Idempotent for a clock that already settled
        // naturally. Notified last so the storyboard observes the element's
        // final state.
        animation.StoryboardOwner?.NotifyClockTerminated(animation.Clock);
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

        UnregisterFromAnimationManagerIfIdle();
        InvalidateVisual();
    }

    private void RegisterWithAnimationManager()
    {
        _animationTickSubscription ??= new Animation.AnimationTickSubscription(this, weak: true);
        Animation.AnimationManager.Register(_animationTickSubscription);
    }

    /// <summary>
    /// Re-registers this element with the animation manager after one of its
    /// clocks was revived externally (Storyboard.Resume/Seek): an all-paused or
    /// all-completed element returns false from OnAnimationFrame and drops off
    /// the manager, so a revival needs an explicit new frame source.
    /// </summary>
    internal void EnsureAnimationFrameSource()
    {
        if (_activeAnimations == null || _activeAnimations.Count == 0)
            return;

        RegisterWithAnimationManager();
    }

    /// <summary>
    /// Unregisters from the animation manager when no entry can still make
    /// progress (paused entries do not count: an all-paused element drops off
    /// the manager and is re-registered on Resume/Seek).
    /// </summary>
    private void UnregisterFromAnimationManagerIfIdle()
    {
        if (_animationTickSubscription == null)
            return;

        if (_activeAnimations != null && _activeAnimations.Count > 0)
        {
            foreach (var anim in _activeAnimations.Values)
            {
                if (anim.StartPending || (!anim.Clock.IsCompleted && !anim.Clock.IsPaused))
                    return;
            }
        }

        Animation.AnimationManager.Unregister(_animationTickSubscription);
    }

    /// <summary>
    /// Called by the central AnimationManager on the UI thread, once per frame.
    /// Processes all active animations for this element. Returns false when no
    /// entry can still make progress, which unregisters this element from the
    /// manager (new/revived animations re-register).
    /// </summary>
    bool Animation.IFrameAnimatable.OnAnimationFrame(long frameTimestamp)
    {
        if (_activeAnimations == null || _activeAnimations.Count == 0)
            return false;

        // Per-call snapshot rented from the pool — never a shared/static buffer:
        // Completed handlers and OnPropertyChanged run user code that may
        // re-enter animation teardown (detach → recycle stop) while this frame
        // is still iterating.
        var pool = System.Buffers.ArrayPool<KeyValuePair<DependencyProperty, ElementAnimation>>.Shared;
        var scratch = pool.Rent(_activeAnimations.Count);
        var count = 0;
        foreach (var pair in _activeAnimations)
            scratch[count++] = pair;

        var hasRunningAnimation = false;
        var anyVisibleChange = false;
        var anyNonCompositionChange = false;

        try
        {
            for (int i = 0; i < count; i++)
            {
                var dp = scratch[i].Key;
                var anim = scratch[i].Value;

                // The entry may have been removed/replaced by an earlier
                // iteration's callback: only tick entries that still own their
                // property.
                if (!_activeAnimations.TryGetValue(dp, out var current) || !ReferenceEquals(current, anim))
                    continue;

                if (anim.ConsumePendingStart())
                {
                    anim.Clock.BeginAt(frameTimestamp);
                    hasRunningAnimation = true;
                    // The first animated value is written on the next tick; nothing
                    // changed this frame, so do not force an invalidation here.
                    continue;
                }

                if (anim.Clock.IsCompleted)
                    continue;

                if (anim.Clock.IsPaused)
                    continue;

                hasRunningAnimation = true;

                // Update clock progress
                anim.Clock.Tick(frameTimestamp);

                // Update animated value (routes through SetAnimatedValue → metadata
                // callback → InvalidateVisual / InvalidateComposition). Crucially this
                // now reports whether the value actually MOVED this frame: a live clock
                // that produced a pixel-identical value contributes no invalidation.
                if (UpdateAnimatedValue(dp))
                {
                    anyVisibleChange = true;
                    if (!IsCompositionOnlyDp(dp)) anyNonCompositionChange = true;
                }
            }
        }
        finally
        {
            Array.Clear(scratch, 0, count);
            pool.Return(scratch);
        }

        // Change-gated consolidated invalidation. SetAnimatedValue already routes a
        // precise per-DP invalidation when a value changes; this single lightweight
        // call is the backup that also covers render-affecting DPs whose metadata
        // callback may not itself invalidate. It is gated on a REAL value change so a
        // frame where every running clock produced an unchanged value submits zero
        // GPU work (it reaches Window's idle-frame skip) instead of forcing a present
        // every refresh interval for the entire lifetime of the animation.
        if (anyVisibleChange)
        {
            if (anyNonCompositionChange)
                InvalidateVisual();
            else
                InvalidateComposition();
        }

        return hasRunningAnimation;
    }

    private bool IsCompositionOnlyDp(DependencyProperty dp)
    {
        return dp.GetMetadata(GetType()) is FrameworkPropertyMetadata fpm && fpm.AffectsCompositionOnly;
    }

    /// <summary>
    /// Recomputes and applies the current value for an animated dependency property.
    /// </summary>
    /// <returns><c>true</c> if the value actually changed this frame (a present was
    /// scheduled); <c>false</c> if it produced a pixel-identical value or could not be
    /// evaluated.</returns>
    private bool UpdateAnimatedValue(DependencyProperty dp)
    {
        if (_activeAnimations == null || !_activeAnimations.TryGetValue(dp, out var anim))
            return false;

        try
        {
            var baseValue = anim.BaseValue ?? dp.DefaultMetadata.DefaultValue ?? GetDefaultAnimationValue(dp);
            // SnapshotAndReplace: the value displayed at the replacement instant
            // is the origin, so a To-only animation continues from the visual
            // value it took over instead of jumping back to the base value.
            var originValue = anim.HasHandoffSnapshot ? (anim.HandoffSnapshot ?? baseValue) : baseValue;
            var currentValue = anim.Animation.GetCurrentValue(originValue, baseValue, anim.Clock);

            var holdEnd = anim.Animation.AnimationFillBehavior == AnimationFillBehavior.HoldEnd;
            return SetAnimatedValue(dp, currentValue, holdEnd);
        }
        catch
        {
            // If animation value calculation fails, silently continue
            return false;
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072:RequiresUnreferencedCode",
        Justification = "Activator.CreateInstance is reached only when type.IsValueType is true. A value type always has an intrinsic parameterless (default) constructor that performs zero-initialization; the trimmer never removes a struct's default construction, so the PublicParameterlessConstructor requirement is structurally satisfied here even though DependencyProperty.PropertyType does not flow the DAM annotation.")]
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

    /// <summary>
    /// Stops every animation in this element's subtree and hard-discards the
    /// animated value layer (no HoldEnd promotion), leaving the subtree
    /// "clean and tick-free". Called by container recycling before pooling and
    /// by <see cref="Jalium.UI.Animation.AnimationManager.NotifyDetached"/>'s
    /// deferred detach check.
    /// </summary>
    internal void StopAnimationsForRecycleRecursive()
    {
        StopAnimationsForRecycleLocal();

        for (int i = 0; i < VisualChildrenCount; i++)
        {
            if (GetVisualChild(i) is UIElement child)
            {
                child.StopAnimationsForRecycleRecursive();
            }
        }
    }

    private void StopAnimationsForRecycleLocal()
    {
        // The animations are being stopped deterministically right now: any
        // deferred detach re-check queued for this element is redundant.
        Animation.AnimationManager.CancelPendingDetach(this);

        if (_activeAnimations != null && _activeAnimations.Count > 0)
        {
            // Per-call snapshot rented from the pool (never a shared buffer):
            // storyboard bookkeeping and OnPropertyChanged run user code that
            // may re-enter animation teardown while this loop is running.
            var pool = System.Buffers.ArrayPool<KeyValuePair<DependencyProperty, ElementAnimation>>.Shared;
            var scratch = pool.Rent(_activeAnimations.Count);
            var count = 0;
            foreach (var pair in _activeAnimations)
                scratch[count++] = pair;

            try
            {
                for (int i = 0; i < count; i++)
                {
                    var dp = scratch[i].Key;
                    var anim = scratch[i].Value;

                    // The entry may have been removed by an earlier iteration's
                    // callback re-entering teardown.
                    if (!_activeAnimations.TryGetValue(dp, out var current) || !ReferenceEquals(current, anim))
                        continue;

                    // clearAnimatedValue:false — no HoldEnd promotion; the whole
                    // animated layer is hard-discarded below. Storyboard-kind
                    // entries settle their clock via the termination callback
                    // inside RemoveAnimationCore.
                    RemoveAnimationCore(dp, anim, clearAnimatedValue: false);
                }
            }
            finally
            {
                Array.Clear(scratch, 0, count);
                pool.Return(scratch);
            }
        }

        // Hard-discard every animated value (no HoldEnd promotion): a pooled
        // element must not carry an animation's final value as a ghost.
        DiscardAllAnimatedValues();

        // Idle-checked, not unconditional: the discards above run user code
        // (OnPropertyChanged → triggers/transitions) that may legitimately start
        // a NEW animation on this element — an unconditional Unregister would
        // freeze it at its first frame with no frame source. Same pattern as
        // StopStoryboardAnimation / StopAnimationCore.
        UnregisterFromAnimationManagerIfIdle();
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

    private Automation.Peers.AutomationPeer? _automationPeer;

    /// <summary>
    /// Creates the automation peer for this element.
    /// </summary>
    /// <returns>The automation peer, or null if no peer should be created.</returns>
    protected virtual Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;

    /// <summary>
    /// Gets or creates the automation peer for this element.
    /// </summary>
    /// <returns>The automation peer, or null if the element doesn't support automation.</returns>
    public Automation.Peers.AutomationPeer? GetAutomationPeer()
    {
        _automationPeer ??= OnCreateAutomationPeer();
        return _automationPeer;
    }

    internal Automation.Peers.AutomationPeer? GetExistingAutomationPeer() => _automationPeer;

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
                // GetChildren() is cached on the peer.  A visual change may be
                // below one or more elements that do not expose peers, in which
                // case this ancestor's flattened accessibility children still
                // change.  Invalidate before notifying the platform bridge so
                // it can compare the new tree with its previously cached view.
                current._automationPeer.ResetChildrenCache();
                current._automationPeer.RaiseAutomationEvent(Automation.Peers.AutomationEvents.StructureChanged);
                break;
            }
            current = current.VisualParent as UIElement;
        }
    }

    #endregion

    #region ManipulationWpfParity

    protected virtual void OnManipulationStarting(ManipulationStartingEventArgs e)
    {
    }

    protected virtual void OnManipulationStarted(ManipulationStartedEventArgs e)
    {
    }

    protected virtual void OnManipulationDelta(ManipulationDeltaEventArgs e)
    {
    }

    protected virtual void OnManipulationInertiaStarting(ManipulationInertiaStartingEventArgs e)
    {
    }

    protected virtual void OnManipulationBoundaryFeedback(ManipulationBoundaryFeedbackEventArgs e)
    {
    }

    protected virtual void OnManipulationCompleted(ManipulationCompletedEventArgs e)
    {
    }

    #endregion

    #region StylusWpfParity

    public static readonly RoutedEvent GotStylusCaptureEvent =
        Stylus.GotStylusCaptureEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent LostStylusCaptureEvent =
        Stylus.LostStylusCaptureEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusButtonDownEvent =
        Stylus.PreviewStylusButtonDownEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusButtonUpEvent =
        Stylus.PreviewStylusButtonUpEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusInAirMoveEvent =
        Stylus.PreviewStylusInAirMoveEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusInRangeEvent =
        Stylus.PreviewStylusInRangeEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent =
        Stylus.PreviewStylusOutOfRangeEvent.AddOwner(typeof(UIElement));

    public static readonly RoutedEvent PreviewStylusSystemGestureEvent =
        Stylus.PreviewStylusSystemGestureEvent.AddOwner(typeof(UIElement));

    public event StylusEventHandler GotStylusCapture
    {
        add => AddHandler(GotStylusCaptureEvent, value);
        remove => RemoveHandler(GotStylusCaptureEvent, value);
    }

    public event StylusEventHandler LostStylusCapture
    {
        add => AddHandler(LostStylusCaptureEvent, value);
        remove => RemoveHandler(LostStylusCaptureEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonDown
    {
        add => AddHandler(PreviewStylusButtonDownEvent, value);
        remove => RemoveHandler(PreviewStylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonUp
    {
        add => AddHandler(PreviewStylusButtonUpEvent, value);
        remove => RemoveHandler(PreviewStylusButtonUpEvent, value);
    }

    public event StylusEventHandler PreviewStylusInAirMove
    {
        add => AddHandler(PreviewStylusInAirMoveEvent, value);
        remove => RemoveHandler(PreviewStylusInAirMoveEvent, value);
    }

    public event StylusEventHandler PreviewStylusInRange
    {
        add => AddHandler(PreviewStylusInRangeEvent, value);
        remove => RemoveHandler(PreviewStylusInRangeEvent, value);
    }

    public event StylusEventHandler PreviewStylusOutOfRange
    {
        add => AddHandler(PreviewStylusOutOfRangeEvent, value);
        remove => RemoveHandler(PreviewStylusOutOfRangeEvent, value);
    }

    public event StylusSystemGestureEventHandler PreviewStylusSystemGesture
    {
        add => AddHandler(PreviewStylusSystemGestureEvent, value);
        remove => RemoveHandler(PreviewStylusSystemGestureEvent, value);
    }

    protected virtual void OnGotStylusCapture(StylusEventArgs e)
    {
    }

    protected virtual void OnLostStylusCapture(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusInAirMove(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusInRange(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected virtual void OnPreviewStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }

    #endregion

    #region Touch

    // ─────────────────────────────────────────────────────────────
    //  Touch capture & over tracking.
    //  Multiple touch contacts can exist simultaneously. Each contact is
    //  tracked per-pointer-id rather than as a single static slot like the
    //  mouse, so a static dictionary keyed by id holds capture and each
    //  UIElement holds three lazily-allocated TouchDevice lists for the
    //  contacts it currently owns or covers.
    // ─────────────────────────────────────────────────────────────

    private readonly struct CaptureRecord
    {
        public CaptureRecord(UIElement element, TouchDevice device) { Element = element; Device = device; }
        public UIElement Element { get; }
        public TouchDevice Device { get; }
    }

    private static readonly Dictionary<int, CaptureRecord> s_touchCaptures = new();

    private List<TouchDevice>? _touchesOver;
    private List<TouchDevice>? _touchesDirectlyOver;
    private List<TouchDevice>? _touchesCaptured;

    // ── CLR event wrappers ──

    /// <summary>Occurs when a touch contact enters the bounds of this element.</summary>
    public event TouchEventHandler TouchEnter
    {
        add => AddHandler(TouchEnterEvent, value);
        remove => RemoveHandler(TouchEnterEvent, value);
    }

    /// <summary>Occurs when a touch contact leaves the bounds of this element.</summary>
    public event TouchEventHandler TouchLeave
    {
        add => AddHandler(TouchLeaveEvent, value);
        remove => RemoveHandler(TouchLeaveEvent, value);
    }

    /// <summary>Occurs when this element acquires capture of a touch contact.</summary>
    public event TouchEventHandler GotTouchCapture
    {
        add => AddHandler(GotTouchCaptureEvent, value);
        remove => RemoveHandler(GotTouchCaptureEvent, value);
    }

    /// <summary>Occurs when a captured touch contact is released from this element.</summary>
    public event TouchEventHandler LostTouchCapture
    {
        add => AddHandler(LostTouchCaptureEvent, value);
        remove => RemoveHandler(LostTouchCaptureEvent, value);
    }

    // ── Public capture/query API ──

    /// <summary>Gets the touch contacts captured to this element.</summary>
    public IEnumerable<TouchDevice> TouchesCaptured => _touchesCaptured ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts whose primary hit target is this element.</summary>
    public IEnumerable<TouchDevice> TouchesDirectlyOver => _touchesDirectlyOver ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts currently over this element or any of its descendants.</summary>
    public IEnumerable<TouchDevice> TouchesOver => _touchesOver ?? Enumerable.Empty<TouchDevice>();

    /// <summary>Gets the touch contacts captured to this element or any of its descendants.</summary>
    public IEnumerable<TouchDevice> TouchesCapturedWithin
    {
        get
        {
            foreach (var pair in s_touchCaptures)
            {
                if (IsSelfOrDescendant(pair.Value.Element, this))
                    yield return pair.Value.Device;
            }
        }
    }

    /// <summary>True if any touch contact is captured to this element.</summary>
    public bool AreAnyTouchesCaptured => _touchesCaptured is { Count: > 0 };

    /// <summary>True if any touch contact is captured to this element or any of its descendants.</summary>
    public bool AreAnyTouchesCapturedWithin
    {
        get
        {
            foreach (var pair in s_touchCaptures)
            {
                if (IsSelfOrDescendant(pair.Value.Element, this))
                    return true;
            }
            return false;
        }
    }

    /// <summary>True if any touch contact is directly over this element.</summary>
    public bool AreAnyTouchesDirectlyOver => _touchesDirectlyOver is { Count: > 0 };

    /// <summary>True if any touch contact is over this element or any of its descendants.</summary>
    public bool AreAnyTouchesOver
    {
        get
        {
            if (_touchesOver is { Count: > 0 })
            {
                return true;
            }

            for (var index = 0; index < VisualChildrenCount; index++)
            {
                if (GetVisualChild(index) is UIElement child && child.AreAnyTouchesOver)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Captures the specified touch contact to this element. Any prior capture for that
    /// contact is released, raising a paired LostTouchCapture / GotTouchCapture.
    /// </summary>
    public bool CaptureTouch(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!IsEnabled || Visibility != Visibility.Visible)
            return false;

        if (s_touchCaptures.TryGetValue(touchDevice.Id, out var previous))
        {
            if (ReferenceEquals(previous.Element, this))
                return true;
            previous.Element.RemoveCapturedTouchInternal(touchDevice);
        }

        s_touchCaptures[touchDevice.Id] = new CaptureRecord(this, touchDevice);
        AddCapturedTouchInternal(touchDevice);
        touchDevice.Capture(this);
        return true;
    }

    /// <summary>Releases a previously captured touch contact from this element.</summary>
    public bool ReleaseTouchCapture(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!s_touchCaptures.TryGetValue(touchDevice.Id, out var record) || !ReferenceEquals(record.Element, this))
            return false;
        s_touchCaptures.Remove(touchDevice.Id);
        RemoveCapturedTouchInternal(touchDevice);
        touchDevice.Capture(null);
        return true;
    }

    /// <summary>Releases all touch contacts captured by this element.</summary>
    public void ReleaseAllTouchCaptures()
    {
        if (_touchesCaptured == null || _touchesCaptured.Count == 0)
            return;
        // Snapshot to allow mutation while raising events.
        var devices = _touchesCaptured.ToArray();
        foreach (var device in devices)
        {
            ReleaseTouchCapture(device);
        }
    }

    /// <summary>Returns the element that has captured the specified touch contact, or null.</summary>
    public static UIElement? GetTouchCapture(int touchId)
    {
        return s_touchCaptures.TryGetValue(touchId, out var record) ? record.Element : null;
    }

    /// <summary>Forces release of all touch captures. Invoked on window deactivation / capture loss.</summary>
    internal static void ForceReleaseAllTouchCaptures()
    {
        if (s_touchCaptures.Count == 0) return;
        var snapshot = s_touchCaptures.ToArray();
        s_touchCaptures.Clear();
        foreach (var pair in snapshot)
        {
            CaptureRecord record = pair.Value;
            record.Element.RemoveCapturedTouchInternal(record.Device);
            record.Device.Capture(null);
        }
    }

    // ── Internal hooks used by the input dispatcher ──

    internal void AddCapturedTouchInternal(TouchDevice device)
    {
        (_touchesCaptured ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveCapturedTouchInternal(TouchDevice device)
    {
        _touchesCaptured?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void AddDirectlyOverTouchInternal(TouchDevice device)
    {
        (_touchesDirectlyOver ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveDirectlyOverTouchInternal(TouchDevice device)
    {
        _touchesDirectlyOver?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void AddOverTouchInternal(TouchDevice device)
    {
        (_touchesOver ??= new List<TouchDevice>(1)).Add(device);
        UpdateTouchDependencyState();
    }

    internal void RemoveOverTouchInternal(TouchDevice device)
    {
        _touchesOver?.Remove(device);
        UpdateTouchDependencyState();
    }

    internal void RaiseGotTouchCapture(TouchDevice device)
    {
        var args = new TouchEventArgs(device, Environment.TickCount) { RoutedEvent = GotTouchCaptureEvent };
        RaiseEvent(args);
    }

    internal void RaiseLostTouchCapture(TouchDevice device)
    {
        var args = new TouchEventArgs(device, Environment.TickCount) { RoutedEvent = LostTouchCaptureEvent };
        RaiseEvent(args);
    }

    private static bool IsSelfOrDescendant(UIElement candidate, UIElement reference)
    {
        Visual? current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, reference)) return true;
            current = current.VisualParent;
        }
        return false;
    }

    #endregion

    #region Transitions

    private const string TransitionAllValue = "All";
    private const string TransitionNoneValue = "None";
    private const string DefaultTransitionPropertyValue = TransitionNoneValue;
    private static readonly TimeSpan s_defaultTransitionDuration = TimeSpan.FromMilliseconds(180);

    private Dictionary<string, bool>? _transitionPropertyLookup;
    private string? _transitionPropertyLookupSource;
    private TransitionPropertyCollection? _transitionPropertyCollectionSubscription;
    private bool _transitionAllProperties;
    private bool _transitionNoProperties;
    private int _transitionArmVersion;
    private bool _automaticTransitionsArmed;

    internal static Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>? AutomaticTransitionAnimationFactory { get; set; }

    /// <summary>
    /// Identifies the TransitionProperty dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionPropertyProperty =
        DependencyProperty.Register(nameof(TransitionProperty), typeof(TransitionPropertyCollection), typeof(UIElement),
            new PropertyMetadata(DefaultTransitionPropertyValue, OnTransitionConfigurationChanged));

    /// <summary>
    /// Identifies the TransitionDuration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionDurationProperty =
        DependencyProperty.Register(nameof(TransitionDuration), typeof(Duration), typeof(UIElement),
            new PropertyMetadata(new Duration(s_defaultTransitionDuration), OnTransitionConfigurationChanged));

    /// <summary>
    /// Identifies the TransitionTimingFunction dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty TransitionTimingFunctionProperty =
        DependencyProperty.Register(nameof(TransitionTimingFunction), typeof(TransitionTimingFunction), typeof(UIElement),
            new PropertyMetadata(TransitionTimingFunction.Recommended, OnTransitionConfigurationChanged));

    /// <summary>
    /// Gets or sets the collection of properties that should participate in automatic transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionPropertyCollection TransitionProperty
    {
        get => TransitionPropertyCollection.FromRawValue(GetValue(TransitionPropertyProperty));
        set => SetValue(TransitionPropertyProperty, value ?? TransitionPropertyCollection.None());
    }

    /// <summary>
    /// Gets or sets the duration used by automatic property transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public Duration TransitionDuration
    {
        get => GetValue(TransitionDurationProperty) is Duration duration
            ? duration
            : new Duration(s_defaultTransitionDuration);
        set => SetValue(TransitionDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets the timing function used by automatic property transitions.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TransitionTimingFunction TransitionTimingFunction
    {
        get => GetValue(TransitionTimingFunctionProperty) is TransitionTimingFunction timingFunction
            ? timingFunction
            : TransitionTimingFunction.Recommended;
        set => SetValue(TransitionTimingFunctionProperty, value);
    }

    /// <summary>
    /// Allows derived controls to suppress automatic transitions for specific properties.
    /// </summary>
    /// <param name="dp">The property being mutated.</param>
    /// <returns><see langword="true"/> to bypass automatic transition handling for the property.</returns>
    protected virtual bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        return false;
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
    }

    /// <summary>
    /// Provides the WPF-compatible dependency-object parent-change hook while the
    /// lower visual layer retains its strongly typed compatibility overload.
    /// </summary>
    protected internal override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        UpdateIsVisibleFromTree();

        if (VisualParent != null)
        {
            ScheduleAutomaticTransitionArmRecursive(this);
        }
        else if (oldParent != null)
        {
            DisarmAutomaticTransitionsRecursive(this);

            // Deferred, cancellable stop: if the subtree is still detached at the
            // next frame its animations are stopped for good; a re-attach in the
            // meantime (Popup/ComboBox moving content between trees within one
            // dispatcher batch) cancels the pending check and nothing stops.
            Animation.AnimationManager.NotifyDetached(this);
        }
    }

    internal bool ShouldAutomaticallyTransition(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);

        if (!_automaticTransitionsArmed)
            return false;

        if (GetAutomaticTransitionAnimationFactory() == null)
            return false;

        if (ReferenceEquals(dp, TransitionPropertyProperty) ||
            ReferenceEquals(dp, TransitionDurationProperty) ||
            ReferenceEquals(dp, TransitionTimingFunctionProperty))
        {
            return false;
        }

        if (ShouldSuppressAutomaticTransition(dp))
            return false;

        var duration = GetTransitionDurationOrDefault();
        if (duration <= TimeSpan.Zero)
            return false;

        EnsureTransitionPropertyLookup();
        if (_transitionNoProperties)
            return false;

        return _transitionAllProperties ||
               (_transitionPropertyLookup?.ContainsKey(dp.Name) == true);
    }

    internal bool TryStartAutomaticTransition(DependencyProperty dp, object? fromValue, object? toValue)
    {
        ArgumentNullException.ThrowIfNull(dp);

        var duration = GetTransitionDurationOrDefault();
        if (duration <= TimeSpan.Zero)
            return false;

        var animationFactory = GetAutomaticTransitionAnimationFactory();
        if (animationFactory == null)
            return false;

        var animation = animationFactory(
            dp,
            fromValue,
            toValue,
            duration,
            TransitionTimingFunction);

        if (animation == null)
            return false;

        return BeginAnimationCore(
            dp,
            animation,
            Media.Animation.HandoffBehavior.SnapshotAndReplace,
            ElementAnimationKind.AutomaticTransition,
            clearAnimatedValueOnReplace: false,
            allowAutomaticToReplaceExplicit: false,
            initialAnimatedValue: fromValue,
            useInitialAnimatedValue: true,
            deferClockBeginUntilRendering: true);
    }

    internal void StopAutomaticTransition(DependencyProperty dp, bool clearAnimatedValue)
    {
        StopAnimationCore(dp, ElementAnimationKind.AutomaticTransition, clearAnimatedValue);
    }

    internal bool HasExplicitAnimation(DependencyProperty dp)
    {
        return TryGetActiveAnimation(dp, out var animation) &&
               animation.Kind == ElementAnimationKind.Explicit;
    }

    internal bool HasAutomaticTransition(DependencyProperty dp)
    {
        return TryGetActiveAnimation(dp, out var animation) &&
               animation.Kind == ElementAnimationKind.AutomaticTransition;
    }

    private static void OnTransitionConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            if (ReferenceEquals(e.Property, TransitionPropertyProperty))
            {
                element.UpdateTransitionPropertyCollectionSubscription(e.OldValue, e.NewValue);
                element.InvalidateTransitionPropertyLookup();
            }
        }
    }

    private TimeSpan GetTransitionDurationOrDefault()
    {
        var duration = TransitionDuration;
        if (!duration.HasTimeSpan)
            return TimeSpan.Zero;

        return duration.TimeSpan;
    }

    private void EnsureTransitionPropertyLookup()
    {
        var raw = GetValue(TransitionPropertyProperty);
        var cacheKey = TransitionPropertyCollection.GetCacheKey(raw);
        if (cacheKey == _transitionPropertyLookupSource)
            return;

        _transitionPropertyLookupSource = cacheKey;
        _transitionPropertyLookup = null;
        _transitionAllProperties = false;
        _transitionNoProperties = false;

        if (raw is TransitionPropertyCollection collection)
        {
            ApplyTransitionPropertyCollectionLookup(collection);
            return;
        }

        var rawText = raw as string;
        if (string.IsNullOrWhiteSpace(rawText) ||
            string.Equals(rawText, TransitionNoneValue, StringComparison.OrdinalIgnoreCase))
        {
            _transitionNoProperties = true;
            return;
        }

        if (string.Equals(rawText, TransitionAllValue, StringComparison.OrdinalIgnoreCase))
        {
            _transitionAllProperties = true;
            return;
        }

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in rawText.Split(','))
        {
            var trimmed = TransitionPropertyCollection.NormalizeName(name);
            if (trimmed == null)
                continue;

            lookup[trimmed] = true;
        }

        _transitionPropertyLookup = lookup;
        _transitionNoProperties = lookup.Count == 0;
    }

    private void InvalidateTransitionPropertyLookup()
    {
        _transitionPropertyLookupSource = null;
        _transitionPropertyLookup = null;
        _transitionAllProperties = false;
        _transitionNoProperties = false;
    }

    private void ApplyTransitionPropertyCollectionLookup(TransitionPropertyCollection collection)
    {
        if (collection.IsNone)
        {
            _transitionNoProperties = true;
            return;
        }

        if (collection.IsAll)
        {
            _transitionAllProperties = true;
            return;
        }

        var lookup = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in collection)
        {
            var normalized = TransitionPropertyCollection.NormalizeName(propertyName);
            if (normalized == null)
                continue;

            lookup[normalized] = true;
        }

        _transitionPropertyLookup = lookup;
        _transitionNoProperties = lookup.Count == 0;
    }

    private void UpdateTransitionPropertyCollectionSubscription(object? oldValue, object? newValue)
    {
        if (_transitionPropertyCollectionSubscription != null &&
            ReferenceEquals(oldValue, _transitionPropertyCollectionSubscription))
        {
            _transitionPropertyCollectionSubscription.CollectionChanged -= OnTransitionPropertyCollectionChanged;
            _transitionPropertyCollectionSubscription = null;
        }

        if (newValue is not TransitionPropertyCollection collection)
            return;

        collection.CollectionChanged -= OnTransitionPropertyCollectionChanged;
        collection.CollectionChanged += OnTransitionPropertyCollectionChanged;
        _transitionPropertyCollectionSubscription = collection;
    }

    private void OnTransitionPropertyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateTransitionPropertyLookup();
    }

    private void ScheduleAutomaticTransitionArmRecursive(UIElement root)
    {
        root.ScheduleAutomaticTransitionArm();

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                ScheduleAutomaticTransitionArmRecursive(child);
            }
        }
    }

    private void DisarmAutomaticTransitionsRecursive(UIElement root)
    {
        root.DisarmAutomaticTransitions();

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is UIElement child)
            {
                DisarmAutomaticTransitionsRecursive(child);
            }
        }
    }

    private void ScheduleAutomaticTransitionArm()
    {
        // 同步 arm — Style.Setter (baseline) 走 allowAutoTransition=false，
        // 不会因为这里 arm=true 而误触过渡；只有 Trigger.Setter 等动态状态切换
        // 才参与过渡。BeginInvoke 异步 arm 的旧实现存在多次 disarm-rearm 循环
        // 之间的竞态：当用户 navigation 切回页面时，layout pass 中的 ApplyTemplate
        // 会让模板内元素再次 disarm 并重新调度 BeginInvoke，而最初一次的 callback
        // 可能因 dispatcher 拥塞被压制，导致用户首次 hover 时 arm 仍是 false。
        // 同步 arm 彻底消除这一竞态。
        unchecked { _transitionArmVersion++; }
        _automaticTransitionsArmed = true;
    }

    private static Func<DependencyProperty, object?, object?, TimeSpan, TransitionTimingFunction, IAnimationTimeline?>? GetAutomaticTransitionAnimationFactory()
    {
        return AutomaticTransitionAnimationFactory ??=
            Jalium.UI.Media.Animation.AnimationFactory.CreateTransitionAnimation;
    }

    private void DisarmAutomaticTransitions()
    {
        unchecked { _transitionArmVersion++; }
        _automaticTransitionsArmed = false;

        if (_activeAnimations == null || _activeAnimations.Count == 0)
            return;

        foreach (var dp in _activeAnimations
                     .Where(static pair => pair.Value.Kind == ElementAnimationKind.AutomaticTransition)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            StopAutomaticTransition(dp, clearAnimatedValue: true);
        }
    }

    #endregion

    #region WpfParity

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
public struct RoutedEventHandlerInfo
{
    private Delegate _handler;
    private bool _handledEventsToo;

    public Delegate Handler => _handler;
    public bool InvokeHandledEventsToo => _handledEventsToo;

    internal RoutedEventHandlerInfo(Delegate handler, bool handledEventsToo)
    {
        _handler = handler;
        _handledEventsToo = handledEventsToo;
    }

    internal void InvokeHandler(object target, RoutedEventArgs routedEventArgs)
    {
        if (routedEventArgs.Handled && !_handledEventsToo)
        {
            return;
        }

        if (_handler is RoutedEventHandler routedHandler)
        {
            routedHandler(target, routedEventArgs);
        }
        else
        {
            routedEventArgs.InvokeHandler(_handler, target);
        }
    }

    public bool Equals(RoutedEventHandlerInfo handlerInfo)
        => _handler == handlerInfo._handler && _handledEventsToo == handlerInfo._handledEventsToo;

    public override bool Equals(object? obj) => obj is RoutedEventHandlerInfo other && Equals(other);

    public override int GetHashCode() => base.GetHashCode();

    public static bool operator ==(RoutedEventHandlerInfo handlerInfo1, RoutedEventHandlerInfo handlerInfo2)
        => handlerInfo1.Equals(handlerInfo2);

    public static bool operator !=(RoutedEventHandlerInfo handlerInfo1, RoutedEventHandlerInfo handlerInfo2)
        => !handlerInfo1.Equals(handlerInfo2);
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
    /// Adds a dirty element for partial rendering. The element's full screen bounds
    /// are used as the dirty region.
    /// </summary>
    void AddDirtyElement(UIElement element);

    /// <summary>
    /// Adds a dirty element with a precise sub-rectangle (in the element's local
    /// coordinate space). Allows callers — e.g. TextBox caret blink — to mark only
    /// the affected pixels dirty instead of the whole control.
    /// Default implementation falls back to the full-element overload.
    /// </summary>
    void AddDirtyElement(UIElement element, Rect localDirtyRect)
        => AddDirtyElement(element);

    /// <summary>
    /// Adds a free-floating dirty rectangle in window (screen) coordinates. Used
    /// by animation / compositor systems that don't own a single <see cref="UIElement"/>
    /// but still know what pixels changed. Default implementation is a no-op.
    /// </summary>
    void AddDirtyRect(Rect screenRect) { }

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

    /// <summary>
    /// Gets the platform window handle (HWND on Windows). Returns <see cref="nint.Zero"/>
    /// if no native window has been created. Exposed so cross-cutting platform code (drag-drop,
    /// IME, etc.) can avoid reflective <c>GetType().GetProperty("Handle")</c> lookups.
    /// </summary>
    nint Handle => nint.Zero;

    /// <summary>
    /// Gets the DPI scale factor for this window (1.0 = 96 DPI, 2.0 = 192 DPI, etc.).
    /// Default implementation returns 1.0 so existing hosts that don't override remain valid.
    /// </summary>
    double DpiScale => 1.0;
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
