using Jalium.UI.Animation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Input;
using Jalium.UI.Media.Animation;
using AnimationHandoffBehavior = Jalium.UI.Media.Animation.HandoffBehavior;

namespace Jalium.UI;

/// <summary>
/// Base class for nonvisual content that participates in dependency properties,
/// routed input, focus, commands, and animation without becoming a visual.
/// </summary>
public partial class ContentElement : DependencyObject, IInputElement, IAnimatable
{
    private readonly Dictionary<RoutedEvent, List<RoutedEventHandlerInfo>> _eventHandlers = new();
    private InputBindingCollection? _inputBindings;
    private CommandBindingCollection? _commandBindings;
    private DependencyObject? _contentParent;

    private readonly Dictionary<DependencyProperty, ActiveAnimation> _activeAnimations = new();
    private AutomationPeer? _automationPeer;
    private AnimationDriver? _animationDriver;
    private AnimationTickSubscription? _animationSubscription;

    private static ContentElement? s_mouseCaptured;
    private static ContentElement? s_mouseDirectlyOver;
    private static ContentElement? s_stylusCaptured;
    private static ContentElement? s_stylusDirectlyOver;
    private static readonly Dictionary<int, TouchCaptureRecord> s_touchCaptures = new();

    private List<TouchDevice>? _touchesCaptured;
    private List<TouchDevice>? _touchesDirectlyOver;
    private List<TouchDevice>? _touchesOver;

    public ContentElement()
    {
    }

    /// <summary>Gets the command bindings attached to this content element.</summary>
    public CommandBindingCollection CommandBindings => _commandBindings ??= new CommandBindingCollection();

    /// <summary>Gets the input bindings attached to this content element.</summary>
    public InputBindingCollection InputBindings => _inputBindings ??= new InputBindingCollection();

    public bool AllowDrop
    {
        get => (bool)(GetValue(AllowDropProperty) ?? false);
        set => SetValue(AllowDropProperty, value);
    }

    public bool Focusable
    {
        get => (bool)(GetValue(FocusableProperty) ?? false);
        set => SetValue(FocusableProperty, value);
    }

    public bool IsEnabled
    {
        get
        {
            if (!(bool)(GetValue(IsEnabledProperty) ?? true) || !IsEnabledCore)
            {
                return false;
            }

            return GetUIParentCore() switch
            {
                ContentElement contentParent => contentParent.IsEnabled,
                UIElement visualParent => visualParent.IsEnabled,
                _ => true,
            };
        }
        set => SetValue(IsEnabledProperty, value);
    }

    protected virtual bool IsEnabledCore => true;

    public bool IsFocused => ReferenceEquals(FocusService.FocusedElement, this);

    public bool IsInputMethodEnabled => InputMethodService.GetIsInputMethodEnabled(this);

    public bool IsKeyboardFocused => ReferenceEquals(FocusService.FocusedElement, this);

    public bool IsKeyboardFocusWithin => IsSelfOrContentAncestor(this, FocusService.FocusedElement as ContentElement);

    public bool IsMouseCaptured => ReferenceEquals(s_mouseCaptured, this);

    public bool IsMouseCaptureWithin => IsSelfOrContentAncestor(this, s_mouseCaptured);

    public bool IsMouseDirectlyOver => ReferenceEquals(s_mouseDirectlyOver, this);

    public bool IsMouseOver => IsSelfOrContentAncestor(this, s_mouseDirectlyOver);

    public bool IsStylusCaptured => ReferenceEquals(s_stylusCaptured, this);

    public bool IsStylusCaptureWithin => IsSelfOrContentAncestor(this, s_stylusCaptured);

    public bool IsStylusDirectlyOver => ReferenceEquals(s_stylusDirectlyOver, this);

    public bool IsStylusOver => IsSelfOrContentAncestor(this, s_stylusDirectlyOver);

    public IEnumerable<TouchDevice> TouchesCaptured => _touchesCaptured ?? Enumerable.Empty<TouchDevice>();

    public IEnumerable<TouchDevice> TouchesCapturedWithin
    {
        get
        {
            foreach (TouchCaptureRecord capture in s_touchCaptures.Values)
            {
                if (IsSelfOrContentAncestor(this, capture.Element))
                {
                    yield return capture.Device;
                }
            }
        }
    }

    public IEnumerable<TouchDevice> TouchesDirectlyOver =>
        _touchesDirectlyOver ?? Enumerable.Empty<TouchDevice>();

    public IEnumerable<TouchDevice> TouchesOver => _touchesOver ?? Enumerable.Empty<TouchDevice>();

    public bool AreAnyTouchesCaptured => _touchesCaptured is { Count: > 0 };

    public bool AreAnyTouchesCapturedWithin => TouchesCapturedWithin.Any();

    public bool AreAnyTouchesDirectlyOver => _touchesDirectlyOver is { Count: > 0 };

    public bool AreAnyTouchesOver
    {
        get
        {
            if (_touchesOver is { Count: > 0 })
            {
                return true;
            }

            return EnumerateContentDescendants(this).Any(static element => element._touchesOver is { Count: > 0 });
        }
    }

    public bool HasAnimatedProperties => _activeAnimations.Count != 0;

    /// <summary>Adds an instance handler for a routed event.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler) =>
        AddHandler(routedEvent, handler, handledEventsToo: false);

    /// <summary>Adds an instance handler and optionally observes handled events.</summary>
    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        if (!routedEvent.HandlerType.IsInstanceOfType(handler))
        {
            throw new ArgumentException(
                $"Handler must be assignable to '{routedEvent.HandlerType.FullName}'.",
                nameof(handler));
        }

        if (!_eventHandlers.TryGetValue(routedEvent, out List<RoutedEventHandlerInfo>? handlers))
        {
            handlers = new List<RoutedEventHandlerInfo>();
            _eventHandlers.Add(routedEvent, handlers);
        }

        handlers.Add(new RoutedEventHandlerInfo(handler, handledEventsToo));
    }

    /// <summary>Removes one matching instance handler from a routed event.</summary>
    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_eventHandlers.TryGetValue(routedEvent, out List<RoutedEventHandlerInfo>? handlers))
        {
            return;
        }

        for (int index = handlers.Count - 1; index >= 0; index--)
        {
            if (handlers[index].Handler == handler)
            {
                handlers.RemoveAt(index);
                break;
            }
        }

        if (handlers.Count == 0)
        {
            _eventHandlers.Remove(routedEvent);
        }
    }

    /// <summary>Raises an event through the content/logical route and any visual boundary.</summary>
    public void RaiseEvent(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(e.RoutedEvent);

        e.SetOriginalSource(this);
        e.Source ??= this;

        BuildContentRoute(out List<ContentElement> contentPath, out UIElement? visualBoundary);
        switch (e.RoutedEvent.RoutingStrategy)
        {
            case RoutingStrategy.Direct:
                InvokeHandlers(e);
                break;

            case RoutingStrategy.Bubble:
                foreach (ContentElement element in contentPath)
                {
                    element.InvokeHandlers(e);
                }
                visualBoundary?.RaiseEvent(e);
                break;

            case RoutingStrategy.Tunnel:
                visualBoundary?.RaiseEvent(e);
                for (int index = contentPath.Count - 1; index >= 0; index--)
                {
                    contentPath[index].InvokeHandlers(e);
                }
                break;
        }
    }

    /// <summary>Adds this element's class and instance handlers to an event route.</summary>
    public void AddToEventRoute(EventRoute route, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(e);

        foreach (var handler in EventManager.GetClassHandlers(route.RoutedEvent, GetType()))
        {
            route.Add(this, handler.Handler, handler.HandledEventsToo);
        }

        if (_eventHandlers.TryGetValue(route.RoutedEvent, out List<RoutedEventHandlerInfo>? handlers))
        {
            foreach (RoutedEventHandlerInfo handler in handlers.ToArray())
            {
                route.Add(this, handler.Handler, handler.InvokeHandledEventsToo);
            }
        }
    }

    /// <summary>Attempts to give keyboard focus to this content element.</summary>
    public bool Focus()
    {
        if (!Focusable || !IsEnabled)
        {
            return false;
        }

        IInputElement? oldFocus = FocusService.FocusedElement;
        IInputElement? result = FocusService.Focus(this);
        if (!ReferenceEquals(result, this))
        {
            return false;
        }

        RaiseContentFocusTransition(oldFocus, this);
        return true;
    }

    public virtual bool MoveFocus(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FindVisualInputParent()?.MoveFocus(request) == true;
    }

    public virtual DependencyObject? PredictFocus(FocusNavigationDirection direction) =>
        FindVisualInputParent()?.PredictFocus(direction);

    public bool CaptureMouse()
    {
        if (!IsEnabled)
        {
            return false;
        }

        ContentElement? previous = s_mouseCaptured;
        if (ReferenceEquals(previous, this))
        {
            return true;
        }

        UIElement.ForceReleaseMouseCapture();
        s_mouseCaptured = this;
        RaiseMouseCaptureStateChanges(previous, this);
        previous?.RaiseMouseCaptureEvent(captured: false);
        RaiseMouseCaptureEvent(captured: true);
        return true;
    }

    public void ReleaseMouseCapture()
    {
        if (!ReferenceEquals(s_mouseCaptured, this))
        {
            return;
        }

        ContentElement previous = this;
        s_mouseCaptured = null;
        RaiseMouseCaptureStateChanges(previous, null);
        RaiseMouseCaptureEvent(captured: false);
    }

    public bool CaptureStylus()
    {
        if (!IsEnabled || Tablet.CurrentStylusDevice is null)
        {
            return false;
        }

        ContentElement? previous = s_stylusCaptured;
        if (ReferenceEquals(previous, this))
        {
            return true;
        }

        s_stylusCaptured = this;
        RaiseStylusCaptureStateChanges(previous, this);
        previous?.RaiseStylusCaptureEvent(captured: false, Tablet.CurrentStylusDevice);
        RaiseStylusCaptureEvent(captured: true, Tablet.CurrentStylusDevice);
        return true;
    }

    public void ReleaseStylusCapture()
    {
        if (!ReferenceEquals(s_stylusCaptured, this))
        {
            return;
        }

        StylusDevice? device = Tablet.CurrentStylusDevice;
        ContentElement previous = this;
        s_stylusCaptured = null;
        RaiseStylusCaptureStateChanges(previous, null);
        if (device is not null)
        {
            RaiseStylusCaptureEvent(captured: false, device);
        }
    }

    public bool CaptureTouch(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!IsEnabled)
        {
            return false;
        }

        if (s_touchCaptures.TryGetValue(touchDevice.Id, out TouchCaptureRecord previous))
        {
            if (ReferenceEquals(previous.Element, this))
            {
                return true;
            }

            previous.Element._touchesCaptured?.Remove(previous.Device);
            previous.Element.RaiseTouchCaptureEvent(previous.Device, captured: false);
            previous.Element.NotifyTouchStateChanged();
        }

        s_touchCaptures[touchDevice.Id] = new TouchCaptureRecord(this, touchDevice);
        (_touchesCaptured ??= new List<TouchDevice>()).Add(touchDevice);
        RaiseTouchCaptureEvent(touchDevice, captured: true);
        NotifyTouchStateChanged();
        return true;
    }

    public bool ReleaseTouchCapture(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!s_touchCaptures.TryGetValue(touchDevice.Id, out TouchCaptureRecord capture) ||
            !ReferenceEquals(capture.Element, this))
        {
            return false;
        }

        s_touchCaptures.Remove(touchDevice.Id);
        _touchesCaptured?.Remove(touchDevice);
        RaiseTouchCaptureEvent(touchDevice, captured: false);
        NotifyTouchStateChanged();
        return true;
    }

    public void ReleaseAllTouchCaptures()
    {
        if (_touchesCaptured is not { Count: > 0 })
        {
            return;
        }

        foreach (TouchDevice device in _touchesCaptured.ToArray())
        {
            ReleaseTouchCapture(device);
        }
    }

    public void ApplyAnimationClock(DependencyProperty dp, AnimationClock? clock) =>
        ApplyAnimationClock(dp, clock, AnimationHandoffBehavior.SnapshotAndReplace);

    public void ApplyAnimationClock(
        DependencyProperty dp,
        AnimationClock? clock,
        AnimationHandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (!Enum.IsDefined(handoffBehavior))
        {
            throw new ArgumentOutOfRangeException(nameof(handoffBehavior));
        }

        RemoveAnimation(dp);
        if (clock is null)
        {
            return;
        }

        if (clock.Timeline is not AnimationTimeline timeline)
        {
            throw new ArgumentException("The clock must be backed by an AnimationTimeline.", nameof(clock));
        }

        if (!dp.PropertyType.IsAssignableFrom(timeline.TargetPropertyType) &&
            !timeline.TargetPropertyType.IsAssignableFrom(dp.PropertyType))
        {
            throw new ArgumentException("The animation does not target the dependency property's value type.", nameof(clock));
        }

        object? baseValue = base.GetAnimationBaseValue(dp);
        _activeAnimations[dp] = new ActiveAnimation(clock, timeline, baseValue);
        if (!clock.IsRunning)
        {
            clock.Begin();
        }

        EnsureAnimationDriver();
        UpdateAnimationValue(dp, _activeAnimations[dp]);
    }

    public void BeginAnimation(DependencyProperty dp, AnimationTimeline? animation) =>
        BeginAnimation(dp, animation, AnimationHandoffBehavior.SnapshotAndReplace);

    public void BeginAnimation(
        DependencyProperty dp,
        AnimationTimeline? animation,
        AnimationHandoffBehavior handoffBehavior)
    {
        ArgumentNullException.ThrowIfNull(dp);
        if (animation is null)
        {
            ApplyAnimationClock(dp, null, handoffBehavior);
            return;
        }

        ApplyAnimationClock(dp, animation.CreateClock(), handoffBehavior);
    }

    public new object? GetAnimationBaseValue(DependencyProperty dp)
    {
        ArgumentNullException.ThrowIfNull(dp);
        return base.GetAnimationBaseValue(dp);
    }

    public bool ShouldSerializeCommandBindings() => _commandBindings is { Count: > 0 };

    public bool ShouldSerializeInputBindings() => _inputBindings is { Count: > 0 };

    protected virtual AutomationPeer? OnCreateAutomationPeer() => null;

    internal AutomationPeer? GetAutomationPeer()
    {
        _automationPeer ??= OnCreateAutomationPeer();
        return _automationPeer;
    }

    internal AutomationPeer? GetExistingAutomationPeer() => _automationPeer;

    protected void InvalidateAutomationPeer() => _automationPeer = null;

    protected internal virtual DependencyObject? GetUIParentCore() => _contentParent;

    internal void SetContentParent(DependencyObject? parent)
    {
        if (ReferenceEquals(_contentParent, parent))
        {
            return;
        }

        DependencyObject? oldParent = _contentParent;
        _contentParent = parent;
        OnContentParentChanged(oldParent, parent);
    }

    internal virtual void OnContentParentChanged(DependencyObject? oldParent, DependencyObject? newParent)
    {
    }

    internal static void SetMouseDirectlyOverContent(ContentElement? element)
    {
        ContentElement? previous = s_mouseDirectlyOver;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_mouseDirectlyOver = element;
        previous?.RaiseDirectlyOverChanged(
            IsMouseDirectlyOverProperty,
            previous.OnIsMouseDirectlyOverChanged,
            previous.IsMouseDirectlyOverChanged,
            oldValue: true,
            newValue: false);
        element?.RaiseDirectlyOverChanged(
            IsMouseDirectlyOverProperty,
            element.OnIsMouseDirectlyOverChanged,
            element.IsMouseDirectlyOverChanged,
            oldValue: false,
            newValue: true);

        RaiseWithinStateChanges(
            previous,
            element,
            IsMouseOverProperty,
            static (_, _) => { });
    }

    internal static void SetStylusDirectlyOverContent(ContentElement? element)
    {
        ContentElement? previous = s_stylusDirectlyOver;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_stylusDirectlyOver = element;
        previous?.RaiseDirectlyOverChanged(
            IsStylusDirectlyOverProperty,
            previous.OnIsStylusDirectlyOverChanged,
            previous.IsStylusDirectlyOverChanged,
            oldValue: true,
            newValue: false);
        element?.RaiseDirectlyOverChanged(
            IsStylusDirectlyOverProperty,
            element.OnIsStylusDirectlyOverChanged,
            element.IsStylusDirectlyOverChanged,
            oldValue: false,
            newValue: true);

        RaiseWithinStateChanges(
            previous,
            element,
            IsStylusOverProperty,
            static (_, _) => { });
    }

    internal void AddTouchOver(TouchDevice device, bool directlyOver)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (directlyOver && !(_touchesDirectlyOver ??= new List<TouchDevice>()).Contains(device))
        {
            _touchesDirectlyOver.Add(device);
        }

        if (!(_touchesOver ??= new List<TouchDevice>()).Contains(device))
        {
            _touchesOver.Add(device);
        }

        NotifyTouchStateChanged();
    }

    internal void RemoveTouchOver(TouchDevice device)
    {
        _touchesDirectlyOver?.Remove(device);
        _touchesOver?.Remove(device);
        NotifyTouchStateChanged();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FocusableProperty)
        {
            FocusableChanged?.Invoke(this, e);
        }
        else if (e.Property == IsEnabledProperty)
        {
            IsEnabledChanged?.Invoke(this, e);
        }
    }

    private void BuildContentRoute(out List<ContentElement> contentPath, out UIElement? visualBoundary)
    {
        contentPath = new List<ContentElement>(4);
        visualBoundary = null;

        ContentElement? current = this;
        var visited = new HashSet<ContentElement>(ReferenceEqualityComparer.Instance);
        while (current is not null && visited.Add(current))
        {
            contentPath.Add(current);
            switch (current.GetUIParentCore())
            {
                case ContentElement parent:
                    current = parent;
                    break;
                case UIElement parent:
                    visualBoundary = parent;
                    current = null;
                    break;
                default:
                    current = null;
                    break;
            }
        }
    }

    private void InvokeHandlers(RoutedEventArgs e)
    {
        RoutedEvent routedEvent = e.RoutedEvent!;
        foreach (var classHandler in EventManager.GetClassHandlers(routedEvent, GetType()))
        {
            if (!e.Handled || classHandler.HandledEventsToo)
            {
                e.InvokeHandler(classHandler.Handler, this);
            }
        }

        if (_eventHandlers.TryGetValue(routedEvent, out List<RoutedEventHandlerInfo>? handlers))
        {
            foreach (RoutedEventHandlerInfo handler in handlers.ToArray())
            {
                if (!e.Handled || handler.InvokeHandledEventsToo)
                {
                    e.InvokeHandler(handler.Handler, this);
                }
            }
        }

        InvokeCommandBindings(routedEvent, e);
    }

    private void InvokeCommandBindings(RoutedEvent routedEvent, RoutedEventArgs e)
    {
        if (_commandBindings is not { Count: > 0 })
        {
            return;
        }

        foreach (CommandBinding binding in _commandBindings)
        {
            switch (e)
            {
                case CanExecuteRoutedEventArgs canExecute
                    when ReferenceEquals(binding.Command, canExecute.Command) &&
                         ReferenceEquals(routedEvent, RoutedCommand.PreviewCanExecuteEvent):
                    binding.OnPreviewCanExecute(this, canExecute);
                    break;
                case CanExecuteRoutedEventArgs canExecute
                    when ReferenceEquals(binding.Command, canExecute.Command) &&
                         ReferenceEquals(routedEvent, RoutedCommand.CanExecuteEvent):
                    binding.OnCanExecute(this, canExecute);
                    break;
                case ExecutedRoutedEventArgs executed
                    when ReferenceEquals(binding.Command, executed.Command) &&
                         ReferenceEquals(routedEvent, RoutedCommand.PreviewExecutedEvent):
                    binding.OnPreviewExecuted(this, executed);
                    break;
                case ExecutedRoutedEventArgs executed
                    when ReferenceEquals(binding.Command, executed.Command) &&
                         ReferenceEquals(routedEvent, RoutedCommand.ExecutedEvent):
                    binding.OnExecuted(this, executed);
                    break;
            }

            if (e.Handled)
            {
                break;
            }
        }
    }

    private UIElement? FindVisualInputParent()
    {
        DependencyObject? current = GetUIParentCore();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        while (current is not null && visited.Add(current))
        {
            if (current is UIElement visual)
            {
                return visual;
            }

            current = current is ContentElement content ? content.GetUIParentCore() : null;
        }

        return null;
    }

    private static void RaiseContentFocusTransition(IInputElement? oldFocus, ContentElement newFocus)
    {
        if (oldFocus is ContentElement oldContent && !ReferenceEquals(oldContent, newFocus))
        {
            var previewLost = new KeyboardFocusChangedEventArgs(
                PreviewLostKeyboardFocusEvent,
                oldContent,
                newFocus);
            oldContent.RaiseEvent(previewLost);
            oldContent.RaiseEvent(new KeyboardFocusChangedEventArgs(
                LostKeyboardFocusEvent,
                oldContent,
                newFocus));
            oldContent.RaiseEvent(new RoutedEventArgs(LostFocusEvent, oldContent));
            oldContent.RaiseKeyboardStateChanged(isFocused: false);
        }

        newFocus.RaiseEvent(new KeyboardFocusChangedEventArgs(
            PreviewGotKeyboardFocusEvent,
            oldFocus,
            newFocus));
        newFocus.RaiseEvent(new KeyboardFocusChangedEventArgs(
            GotKeyboardFocusEvent,
            oldFocus,
            newFocus));
        newFocus.RaiseEvent(new RoutedEventArgs(GotFocusEvent, newFocus));
        newFocus.RaiseKeyboardStateChanged(isFocused: true);
    }

    private void RaiseKeyboardStateChanged(bool isFocused)
    {
        SetReadOnlyInputState(IsKeyboardFocusedProperty, isFocused);
        SetReadOnlyInputState(IsFocusedProperty, isFocused);
        var focusedArgs = new DependencyPropertyChangedEventArgs(
            IsKeyboardFocusedProperty,
            !isFocused,
            isFocused);
        OnIsKeyboardFocusedChanged(focusedArgs);
        IsKeyboardFocusedChanged?.Invoke(this, focusedArgs);

        foreach (ContentElement element in EnumerateSelfAndContentAncestors(this))
        {
            element.SetReadOnlyInputState(IsKeyboardFocusWithinProperty, isFocused);
            var withinArgs = new DependencyPropertyChangedEventArgs(
                IsKeyboardFocusWithinProperty,
                !isFocused,
                isFocused);
            element.OnIsKeyboardFocusWithinChanged(withinArgs);
            element.IsKeyboardFocusWithinChanged?.Invoke(element, withinArgs);
        }
    }

    private static void RaiseMouseCaptureStateChanges(ContentElement? oldElement, ContentElement? newElement)
    {
        if (oldElement is not null)
        {
            oldElement.RaiseCapturePropertyChanged(
                IsMouseCapturedProperty,
                oldElement.OnIsMouseCapturedChanged,
                oldElement.IsMouseCapturedChanged,
                true,
                false);
        }

        if (newElement is not null)
        {
            newElement.RaiseCapturePropertyChanged(
                IsMouseCapturedProperty,
                newElement.OnIsMouseCapturedChanged,
                newElement.IsMouseCapturedChanged,
                false,
                true);
        }

        RaiseWithinStateChanges(
            oldElement,
            newElement,
            IsMouseCaptureWithinProperty,
            static (element, args) =>
            {
                element.OnIsMouseCaptureWithinChanged(args);
                element.IsMouseCaptureWithinChanged?.Invoke(element, args);
            });
    }

    private static void RaiseStylusCaptureStateChanges(ContentElement? oldElement, ContentElement? newElement)
    {
        if (oldElement is not null)
        {
            oldElement.RaiseCapturePropertyChanged(
                IsStylusCapturedProperty,
                oldElement.OnIsStylusCapturedChanged,
                oldElement.IsStylusCapturedChanged,
                true,
                false);
        }

        if (newElement is not null)
        {
            newElement.RaiseCapturePropertyChanged(
                IsStylusCapturedProperty,
                newElement.OnIsStylusCapturedChanged,
                newElement.IsStylusCapturedChanged,
                false,
                true);
        }

        RaiseWithinStateChanges(
            oldElement,
            newElement,
            IsStylusCaptureWithinProperty,
            static (element, args) =>
            {
                element.OnIsStylusCaptureWithinChanged(args);
                element.IsStylusCaptureWithinChanged?.Invoke(element, args);
            });
    }

    private static void RaiseWithinStateChanges(
        ContentElement? oldElement,
        ContentElement? newElement,
        DependencyProperty property,
        Action<ContentElement, DependencyPropertyChangedEventArgs> callback)
    {
        var oldAncestors = new HashSet<ContentElement>(EnumerateSelfAndContentAncestors(oldElement), ReferenceEqualityComparer.Instance);
        var newAncestors = new HashSet<ContentElement>(EnumerateSelfAndContentAncestors(newElement), ReferenceEqualityComparer.Instance);

        foreach (ContentElement element in oldAncestors)
        {
            if (!newAncestors.Contains(element))
            {
                element.SetReadOnlyInputState(property, false);
                callback(element, new DependencyPropertyChangedEventArgs(property, true, false));
            }
        }

        foreach (ContentElement element in newAncestors)
        {
            if (!oldAncestors.Contains(element))
            {
                element.SetReadOnlyInputState(property, true);
                callback(element, new DependencyPropertyChangedEventArgs(property, false, true));
            }
        }
    }

    private void RaiseCapturePropertyChanged(
        DependencyProperty property,
        Action<DependencyPropertyChangedEventArgs> virtualCallback,
        DependencyPropertyChangedEventHandler? eventHandler,
        bool oldValue,
        bool newValue)
    {
        SetReadOnlyInputState(property, newValue);
        var args = new DependencyPropertyChangedEventArgs(property, oldValue, newValue);
        virtualCallback(args);
        eventHandler?.Invoke(this, args);
    }

    private void RaiseDirectlyOverChanged(
        DependencyProperty property,
        Action<DependencyPropertyChangedEventArgs> virtualCallback,
        DependencyPropertyChangedEventHandler? eventHandler,
        bool oldValue,
        bool newValue) =>
        RaiseCapturePropertyChanged(property, virtualCallback, eventHandler, oldValue, newValue);

    private void RaiseMouseCaptureEvent(bool captured)
    {
        var args = new MouseEventArgs(captured ? GotMouseCaptureEvent : LostMouseCaptureEvent)
        {
            Source = this,
        };
        RaiseEvent(args);
    }

    private void RaiseStylusCaptureEvent(bool captured, StylusDevice device)
    {
        var args = new StylusEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = captured ? GotStylusCaptureEvent : LostStylusCaptureEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    private void RaiseTouchCaptureEvent(TouchDevice device, bool captured)
    {
        var args = new TouchEventArgs(device, Environment.TickCount)
        {
            RoutedEvent = captured ? GotTouchCaptureEvent : LostTouchCaptureEvent,
            Source = this,
        };
        RaiseEvent(args);
    }

    private void NotifyTouchStateChanged()
    {
        foreach (ContentElement element in EnumerateSelfAndContentAncestors(this))
        {
            element.SetReadOnlyInputState(AreAnyTouchesCapturedProperty, element.AreAnyTouchesCaptured);
            element.SetReadOnlyInputState(AreAnyTouchesCapturedWithinProperty, element.AreAnyTouchesCapturedWithin);
            element.SetReadOnlyInputState(AreAnyTouchesDirectlyOverProperty, element.AreAnyTouchesDirectlyOver);
            element.SetReadOnlyInputState(AreAnyTouchesOverProperty, element.AreAnyTouchesOver);
        }
    }

    private void SetReadOnlyInputState(DependencyProperty property, bool value)
    {
        if (ReferenceEquals(property, AreAnyTouchesCapturedProperty))
            SetValue(AreAnyTouchesCapturedPropertyKey, value);
        else if (ReferenceEquals(property, AreAnyTouchesCapturedWithinProperty))
            SetValue(AreAnyTouchesCapturedWithinPropertyKey, value);
        else if (ReferenceEquals(property, AreAnyTouchesDirectlyOverProperty))
            SetValue(AreAnyTouchesDirectlyOverPropertyKey, value);
        else if (ReferenceEquals(property, AreAnyTouchesOverProperty))
            SetValue(AreAnyTouchesOverPropertyKey, value);
        else if (ReferenceEquals(property, IsFocusedProperty))
            SetValue(IsFocusedPropertyKey, value);
        else if (ReferenceEquals(property, IsKeyboardFocusedProperty))
            SetValue(IsKeyboardFocusedPropertyKey, value);
        else if (ReferenceEquals(property, IsKeyboardFocusWithinProperty))
            SetValue(IsKeyboardFocusWithinPropertyKey, value);
        else if (ReferenceEquals(property, IsMouseCapturedProperty))
            SetValue(IsMouseCapturedPropertyKey, value);
        else if (ReferenceEquals(property, IsMouseCaptureWithinProperty))
            SetValue(IsMouseCaptureWithinPropertyKey, value);
        else if (ReferenceEquals(property, IsMouseDirectlyOverProperty))
            SetValue(IsMouseDirectlyOverPropertyKey, value);
        else if (ReferenceEquals(property, IsMouseOverProperty))
            SetValue(IsMouseOverPropertyKey, value);
        else if (ReferenceEquals(property, IsStylusCapturedProperty))
            SetValue(IsStylusCapturedPropertyKey, value);
        else if (ReferenceEquals(property, IsStylusCaptureWithinProperty))
            SetValue(IsStylusCaptureWithinPropertyKey, value);
        else if (ReferenceEquals(property, IsStylusDirectlyOverProperty))
            SetValue(IsStylusDirectlyOverPropertyKey, value);
        else if (ReferenceEquals(property, IsStylusOverProperty))
            SetValue(IsStylusOverPropertyKey, value);
    }

    private void EnsureAnimationDriver()
    {
        _animationDriver ??= new AnimationDriver(this);
        _animationSubscription ??= new AnimationTickSubscription(_animationDriver, weak: false);
        AnimationManager.Register(_animationSubscription);
    }

    private bool OnAnimationFrame(long frameTimestamp)
    {
        foreach ((DependencyProperty property, ActiveAnimation animation) in _activeAnimations.ToArray())
        {
            animation.Clock.Tick(frameTimestamp);
            UpdateAnimationValue(property, animation);

            if (((IAnimationClock)animation.Clock).IsCompleted)
            {
                bool holdEnd = animation.Timeline.FillBehavior == FillBehavior.HoldEnd;
                _activeAnimations.Remove(property);
                if (holdEnd)
                {
                    ClearAnimatedValue(property);
                }
                else
                {
                    DiscardAnimatedValue(property);
                }
            }
        }

        return _activeAnimations.Count != 0;
    }

    private void UpdateAnimationValue(DependencyProperty property, ActiveAnimation animation)
    {
        object? baseValue = animation.BaseValue ?? property.GetMetadata(GetType()).DefaultValue;
        if (baseValue is null)
        {
            return;
        }

        object value = animation.Timeline.GetCurrentValue(baseValue, baseValue, animation.Clock);
        SetAnimatedValue(
            property,
            value,
            holdEndValue: animation.Timeline.FillBehavior == FillBehavior.HoldEnd);
    }

    private void RemoveAnimation(DependencyProperty property)
    {
        if (_activeAnimations.Remove(property))
        {
            DiscardAnimatedValue(property);
        }

        if (_activeAnimations.Count == 0 && _animationSubscription is not null)
        {
            AnimationManager.Unregister(_animationSubscription);
        }
    }

    private static bool IsSelfOrContentAncestor(ContentElement ancestor, ContentElement? element)
    {
        for (ContentElement? current = element; current is not null; current = current.GetUIParentCore() as ContentElement)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<ContentElement> EnumerateSelfAndContentAncestors(ContentElement? element)
    {
        var visited = new HashSet<ContentElement>(ReferenceEqualityComparer.Instance);
        for (ContentElement? current = element; current is not null && visited.Add(current); current = current.GetUIParentCore() as ContentElement)
        {
            yield return current;
        }
    }

    private static IEnumerable<ContentElement> EnumerateContentDescendants(ContentElement root)
    {
        if (root is not FrameworkContentElement frameworkRoot)
        {
            yield break;
        }

        var stack = new Stack<ContentElement>();
        frameworkRoot.CopyLogicalContentChildrenTo(stack);
        while (stack.Count != 0)
        {
            ContentElement current = stack.Pop();
            yield return current;
            if (current is FrameworkContentElement frameworkContent)
            {
                frameworkContent.CopyLogicalContentChildrenTo(stack);
            }
        }
    }

    private readonly record struct TouchCaptureRecord(ContentElement Element, TouchDevice Device);

    private sealed class ActiveAnimation
    {
        public ActiveAnimation(AnimationClock clock, AnimationTimeline timeline, object? baseValue)
        {
            Clock = clock;
            Timeline = timeline;
            BaseValue = baseValue;
        }

        public AnimationClock Clock { get; }
        public AnimationTimeline Timeline { get; }
        public object? BaseValue { get; }
    }

    private sealed class AnimationDriver : IFrameAnimatable
    {
        private readonly ContentElement _owner;

        public AnimationDriver(ContentElement owner) => _owner = owner;

        public bool OnAnimationFrame(long frameTimestamp) => _owner.OnAnimationFrame(frameTimestamp);
    }
}
