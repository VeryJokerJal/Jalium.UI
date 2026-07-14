using System.ComponentModel;
using Jalium.UI.Input;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI;

/// <summary>
/// Base class for visual 3D elements that participate in routed input and focus.
/// </summary>
public abstract partial class UIElement3D : Visual3D, IInputElement
{
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCaptured), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesCapturedWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesCapturedWithin), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesDirectlyOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey AreAnyTouchesOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(AreAnyTouchesOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFocused), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsKeyboardFocusedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocused), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnKeyboardFocusedPropertyChanged));
    private static readonly DependencyPropertyKey IsKeyboardFocusWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsKeyboardFocusWithin), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnKeyboardFocusWithinPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptured), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnMouseCapturedPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseCaptureWithin), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnMouseCaptureWithinPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseDirectlyOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnMouseDirectlyOverPropertyChanged));
    private static readonly DependencyPropertyKey IsMouseOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsMouseOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsStylusCapturedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptured), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnStylusCapturedPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusCaptureWithinPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusCaptureWithin), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnStylusCaptureWithinPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusDirectlyOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusDirectlyOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false, OnStylusDirectlyOverPropertyChanged));
    private static readonly DependencyPropertyKey IsStylusOverPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsStylusOver), typeof(bool), typeof(UIElement3D), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey IsVisiblePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsVisible), typeof(bool), typeof(UIElement3D), new PropertyMetadata(true, OnVisiblePropertyChanged));

    public static readonly DependencyProperty VisibilityProperty =
        UIElement.VisibilityProperty.AddOwner(typeof(UIElement3D), new PropertyMetadata(Visibility.Visible, OnVisibilityPropertyChanged));
    public static readonly DependencyProperty IsEnabledProperty =
        UIElement.IsEnabledProperty.AddOwner(typeof(UIElement3D), new PropertyMetadata(true, OnEnabledPropertyChanged));
    public static readonly DependencyProperty IsHitTestVisibleProperty =
        UIElement.IsHitTestVisibleProperty.AddOwner(typeof(UIElement3D), new PropertyMetadata(true, OnHitTestVisiblePropertyChanged));
    public static readonly DependencyProperty FocusableProperty =
        UIElement.FocusableProperty.AddOwner(typeof(UIElement3D), new PropertyMetadata(false, OnFocusablePropertyChanged));
    public static readonly DependencyProperty AllowDropProperty =
        UIElement.AllowDropProperty.AddOwner(typeof(UIElement3D));

    public static readonly DependencyProperty AreAnyTouchesCapturedProperty = AreAnyTouchesCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesCapturedWithinProperty = AreAnyTouchesCapturedWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesDirectlyOverProperty = AreAnyTouchesDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty AreAnyTouchesOverProperty = AreAnyTouchesOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsFocusedProperty = IsFocusedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsKeyboardFocusedProperty = IsKeyboardFocusedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsKeyboardFocusWithinProperty = IsKeyboardFocusWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCapturedProperty = IsMouseCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseCaptureWithinProperty = IsMouseCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseDirectlyOverProperty = IsMouseDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsMouseOverProperty = IsMouseOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCapturedProperty = IsStylusCapturedPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusCaptureWithinProperty = IsStylusCaptureWithinPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusDirectlyOverProperty = IsStylusDirectlyOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsStylusOverProperty = IsStylusOverPropertyKey.DependencyProperty;
    public static readonly DependencyProperty IsVisibleProperty = IsVisiblePropertyKey.DependencyProperty;

    private readonly Dictionary<RoutedEvent, List<RoutedEventHandlerInfo>> _eventHandlers = new();
    private readonly HashSet<TouchDevice> _touchesCaptured = new();
    private readonly HashSet<TouchDevice> _touchesDirectlyOver = new();
    private Input.CommandBindingCollection? _commandBindings;
    private Input.InputBindingCollection? _inputBindings;
    private bool _modelInvalid;

    private static UIElement3D? s_keyboardFocused;
    private static UIElement3D? s_mouseCaptured;
    private static UIElement3D? s_mouseDirectlyOver;
    private static UIElement3D? s_stylusCaptured;
    private static UIElement3D? s_stylusDirectlyOver;

    public Visibility Visibility
    {
        get => (Visibility)(GetValue(VisibilityProperty) ?? Visibility.Visible);
        set => SetValue(VisibilityProperty, value);
    }

    public bool IsVisible
    {
        get
        {
            UpdateIsVisible();
            return (bool)(GetValue(IsVisibleProperty) ?? true);
        }
    }

    public bool IsEnabled
    {
        get
        {
            if (!(bool)(GetValue(IsEnabledProperty) ?? true) || !IsEnabledCore)
            {
                return false;
            }

            return Visual3DParent switch
            {
                UIElement3D parent3D => parent3D.IsEnabled,
                UIElement parent2D => parent2D.IsEnabled,
                _ => true,
            };
        }
        set => SetValue(IsEnabledProperty, value);
    }

    protected virtual bool IsEnabledCore => true;

    public bool IsHitTestVisible
    {
        get
        {
            if (!(bool)(GetValue(IsHitTestVisibleProperty) ?? true))
            {
                return false;
            }

            return Visual3DParent switch
            {
                UIElement3D parent3D => parent3D.IsHitTestVisible,
                UIElement parent2D => parent2D.IsHitTestVisible,
                _ => true,
            };
        }
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    public bool Focusable
    {
        get => (bool)(GetValue(FocusableProperty) ?? false);
        set => SetValue(FocusableProperty, value);
    }

    public bool AllowDrop
    {
        get => (bool)(GetValue(AllowDropProperty) ?? false);
        set => SetValue(AllowDropProperty, value);
    }

    public bool IsFocused => (bool)(GetValue(IsFocusedProperty) ?? false);
    public bool IsKeyboardFocused => (bool)(GetValue(IsKeyboardFocusedProperty) ?? false);
    public bool IsKeyboardFocusWithin => (bool)(GetValue(IsKeyboardFocusWithinProperty) ?? false);
    public bool IsMouseOver => (bool)(GetValue(IsMouseOverProperty) ?? false);
    public bool IsMouseDirectlyOver => (bool)(GetValue(IsMouseDirectlyOverProperty) ?? false);
    public bool IsMouseCaptured => (bool)(GetValue(IsMouseCapturedProperty) ?? false);
    public bool IsMouseCaptureWithin => (bool)(GetValue(IsMouseCaptureWithinProperty) ?? false);
    public bool IsStylusOver => (bool)(GetValue(IsStylusOverProperty) ?? false);
    public bool IsStylusDirectlyOver => (bool)(GetValue(IsStylusDirectlyOverProperty) ?? false);
    public bool IsStylusCaptured => (bool)(GetValue(IsStylusCapturedProperty) ?? false);
    public bool IsStylusCaptureWithin => (bool)(GetValue(IsStylusCaptureWithinProperty) ?? false);
    public bool IsInputMethodEnabled => InputMethodService.GetIsInputMethodEnabled(this);

    public bool AreAnyTouchesCaptured => (bool)(GetValue(AreAnyTouchesCapturedProperty) ?? false);
    public bool AreAnyTouchesCapturedWithin => (bool)(GetValue(AreAnyTouchesCapturedWithinProperty) ?? false);
    public bool AreAnyTouchesDirectlyOver => (bool)(GetValue(AreAnyTouchesDirectlyOverProperty) ?? false);
    public bool AreAnyTouchesOver => (bool)(GetValue(AreAnyTouchesOverProperty) ?? false);
    public IEnumerable<TouchDevice> TouchesCaptured => _touchesCaptured.ToArray();
    public IEnumerable<TouchDevice> TouchesCapturedWithin => EnumerateTouchesCapturedWithin();
    public IEnumerable<TouchDevice> TouchesDirectlyOver => _touchesDirectlyOver.ToArray();
    public IEnumerable<TouchDevice> TouchesOver => EnumerateTouchesOver();

    public Input.CommandBindingCollection CommandBindings => _commandBindings ??= new Input.CommandBindingCollection();
    public Input.InputBindingCollection InputBindings => _inputBindings ??= new Input.InputBindingCollection();

    public bool ShouldSerializeCommandBindings() => _commandBindings is { Count: > 0 };
    public bool ShouldSerializeInputBindings() => _inputBindings is { Count: > 0 };

    public void AddHandler(RoutedEvent routedEvent, Delegate handler) => AddHandler(routedEvent, handler, false);

    public void AddHandler(RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            handlers = new List<RoutedEventHandlerInfo>();
            _eventHandlers.Add(routedEvent, handlers);
        }

        handlers.Add(new RoutedEventHandlerInfo(handler, handledEventsToo));
    }

    public void RemoveHandler(RoutedEvent routedEvent, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        if (!_eventHandlers.TryGetValue(routedEvent, out var handlers))
        {
            return;
        }

        for (var index = handlers.Count - 1; index >= 0; index--)
        {
            if (handlers[index].Handler == handler)
            {
                handlers.RemoveAt(index);
                break;
            }
        }
    }

    public void AddToEventRoute(EventRoute route, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(e);

        foreach (var classHandler in EventManager.GetClassHandlers(route.RoutedEvent, GetType()))
        {
            route.Add(this, classHandler.Handler, classHandler.HandledEventsToo);
        }

        if (_eventHandlers.TryGetValue(route.RoutedEvent, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                route.Add(this, handler.Handler, handler.InvokeHandledEventsToo);
            }
        }
    }

    public void RaiseEvent(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(e.RoutedEvent);

        e.SetOriginalSource(this);
        e.Source ??= this;

        var path = GetInputRoute();
        if (e.RoutedEvent.RoutingStrategy == RoutingStrategy.Direct)
        {
            InvokeHandlers(e);
        }
        else if (e.RoutedEvent.RoutingStrategy == RoutingStrategy.Tunnel)
        {
            for (var index = path.Count - 1; index >= 0; index--)
            {
                path[index].InvokeHandlers(e);
            }
        }
        else
        {
            foreach (var element in path)
            {
                element.InvokeHandlers(e);
            }
        }
    }

    public bool Focus()
    {
        if (!Focusable || !IsEnabled || Visibility != Visibility.Visible)
        {
            return false;
        }

        var serviceResult = FocusService.Focus(this);
        if (FocusService.Provider is not null && !ReferenceEquals(serviceResult, this))
        {
            return false;
        }

        SetKeyboardFocusedElement(this);
        return true;
    }

    public virtual bool MoveFocus(TraversalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return false;
    }

    public virtual DependencyObject? PredictFocus(FocusNavigationDirection direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new InvalidEnumArgumentException(nameof(direction), (int)direction, typeof(FocusNavigationDirection));
        }

        return null;
    }

    public bool CaptureMouse()
    {
        if (!IsEnabled || !IsHitTestVisible)
        {
            return false;
        }

        SetMouseCapturedElement(this);
        return true;
    }

    public void ReleaseMouseCapture()
    {
        if (ReferenceEquals(s_mouseCaptured, this))
        {
            SetMouseCapturedElement(null);
        }
    }

    public bool CaptureStylus()
    {
        if (!IsEnabled || !IsHitTestVisible)
        {
            return false;
        }

        SetStylusCapturedElement(this);
        return true;
    }

    public void ReleaseStylusCapture()
    {
        if (ReferenceEquals(s_stylusCaptured, this))
        {
            SetStylusCapturedElement(null);
        }
    }

    public bool CaptureTouch(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!IsEnabled || !IsHitTestVisible)
        {
            return false;
        }

        if (!_touchesCaptured.Add(touchDevice))
        {
            return true;
        }

        UpdateTouchCaptureState();
        RaiseEvent(new TouchEventArgs(touchDevice, Environment.TickCount) { RoutedEvent = GotTouchCaptureEvent });
        return true;
    }

    public bool ReleaseTouchCapture(TouchDevice touchDevice)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (!_touchesCaptured.Remove(touchDevice))
        {
            return false;
        }

        UpdateTouchCaptureState();
        RaiseEvent(new TouchEventArgs(touchDevice, Environment.TickCount) { RoutedEvent = LostTouchCaptureEvent });
        return true;
    }

    public void ReleaseAllTouchCaptures()
    {
        foreach (var touch in _touchesCaptured.ToArray())
        {
            ReleaseTouchCapture(touch);
        }
    }

    public void InvalidateModel()
    {
        if (_modelInvalid)
        {
            return;
        }

        _modelInvalid = true;
        try
        {
            OnUpdateModel();
        }
        finally
        {
            _modelInvalid = false;
        }
    }

    protected virtual void OnUpdateModel()
    {
    }

    protected internal DependencyObject? GetUIParentCore() => Visual3DParent;

    protected internal override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        UpdateIsVisible();
        InvalidateModel();
    }

    private List<UIElement3D> GetInputRoute()
    {
        var result = new List<UIElement3D>();
        UIElement3D? current = this;
        while (current is not null)
        {
            result.Add(current);
            current = current.Visual3DParent as UIElement3D;
        }

        return result;
    }

    private void InvokeHandlers(RoutedEventArgs e)
    {
        InvokeClassHandler(e);

        foreach (var classHandler in EventManager.GetClassHandlers(e.RoutedEvent!, GetType()))
        {
            if (!e.Handled || classHandler.HandledEventsToo)
            {
                e.InvokeHandler(classHandler.Handler, this);
            }
        }

        if (_eventHandlers.TryGetValue(e.RoutedEvent!, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                if (!e.Handled || handler.InvokeHandledEventsToo)
                {
                    e.InvokeHandler(handler.Handler, this);
                }
            }
        }

        if (_commandBindings is null)
        {
            return;
        }

        if (e is CanExecuteRoutedEventArgs canExecute)
        {
            foreach (var binding in _commandBindings.Where(binding => binding.Command == canExecute.Command))
            {
                if (ReferenceEquals(e.RoutedEvent, RoutedCommand.PreviewCanExecuteEvent))
                {
                    binding.OnPreviewCanExecute(this, canExecute);
                }
                else if (ReferenceEquals(e.RoutedEvent, RoutedCommand.CanExecuteEvent))
                {
                    binding.OnCanExecute(this, canExecute);
                }
            }
        }
        else if (e is ExecutedRoutedEventArgs executed)
        {
            foreach (var binding in _commandBindings.Where(binding => binding.Command == executed.Command))
            {
                if (ReferenceEquals(e.RoutedEvent, RoutedCommand.PreviewExecutedEvent))
                {
                    binding.OnPreviewExecuted(this, executed);
                }
                else if (ReferenceEquals(e.RoutedEvent, RoutedCommand.ExecutedEvent))
                {
                    binding.OnExecuted(this, executed);
                }
            }
        }
    }

    private static void OnVisibilityPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.UpdateIsVisible();
        element.InvalidateModel();
    }

    private static void OnEnabledPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.IsEnabledChanged?.Invoke(element, e);
        element.InvalidateModel();
    }

    private static void OnHitTestVisiblePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.IsHitTestVisibleChanged?.Invoke(element, e);
    }

    private static void OnFocusablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.FocusableChanged?.Invoke(element, e);
    }

    private static void OnVisiblePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.IsVisibleChanged?.Invoke(element, e);
    }

    private static void OnKeyboardFocusedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsKeyboardFocusedChanged(e);
        element.IsKeyboardFocusedChanged?.Invoke(element, e);
    }

    private static void OnKeyboardFocusWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsKeyboardFocusWithinChanged(e);
        element.IsKeyboardFocusWithinChanged?.Invoke(element, e);
    }

    private static void OnMouseCapturedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsMouseCapturedChanged(e);
        element.IsMouseCapturedChanged?.Invoke(element, e);
    }

    private static void OnMouseCaptureWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsMouseCaptureWithinChanged(e);
        element.IsMouseCaptureWithinChanged?.Invoke(element, e);
    }

    private static void OnMouseDirectlyOverPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsMouseDirectlyOverChanged(e);
        element.IsMouseDirectlyOverChanged?.Invoke(element, e);
    }

    private static void OnStylusCapturedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsStylusCapturedChanged(e);
        element.IsStylusCapturedChanged?.Invoke(element, e);
    }

    private static void OnStylusCaptureWithinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsStylusCaptureWithinChanged(e);
        element.IsStylusCaptureWithinChanged?.Invoke(element, e);
    }

    private static void OnStylusDirectlyOverPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var element = (UIElement3D)d;
        element.OnIsStylusDirectlyOverChanged(e);
        element.IsStylusDirectlyOverChanged?.Invoke(element, e);
    }

    private void UpdateIsVisible()
    {
        var parentVisible = Visual3DParent switch
        {
            UIElement3D parent3D => parent3D.IsVisible,
            UIElement parent2D => parent2D.IsVisible,
            _ => true,
        };
        var value = Visibility == Visibility.Visible && parentVisible;
        if (!Equals(GetValue(IsVisibleProperty), value))
        {
            SetValue(IsVisiblePropertyKey, value);
        }
    }

    private static void SetKeyboardFocusedElement(UIElement3D? element)
    {
        var previous = s_keyboardFocused;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        if (previous is not null)
        {
            previous.RaiseEvent(new KeyboardFocusChangedEventArgs(PreviewLostKeyboardFocusEvent, previous, element));
            previous.SetValue(IsKeyboardFocusedPropertyKey, false);
            previous.SetValue(IsFocusedPropertyKey, false);
            previous.SetKeyboardFocusWithin(false);
            previous.RaiseEvent(new KeyboardFocusChangedEventArgs(LostKeyboardFocusEvent, previous, element));
            previous.RaiseEvent(new RoutedEventArgs(LostFocusEvent, previous));
        }

        s_keyboardFocused = element;
        if (element is not null)
        {
            element.RaiseEvent(new KeyboardFocusChangedEventArgs(PreviewGotKeyboardFocusEvent, previous, element));
            element.SetValue(IsKeyboardFocusedPropertyKey, true);
            element.SetValue(IsFocusedPropertyKey, true);
            element.SetKeyboardFocusWithin(true);
            element.RaiseEvent(new KeyboardFocusChangedEventArgs(GotKeyboardFocusEvent, previous, element));
            element.RaiseEvent(new RoutedEventArgs(GotFocusEvent, element));
        }
    }

    private static void SetMouseCapturedElement(UIElement3D? element)
    {
        var previous = s_mouseCaptured;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_mouseCaptured = element;
        if (previous is not null)
        {
            previous.SetValue(IsMouseCapturedPropertyKey, false);
            previous.SetCaptureWithin(IsMouseCaptureWithinPropertyKey, false);
            previous.RaiseEvent(new MouseEventArgs(LostMouseCaptureEvent));
        }

        if (element is not null)
        {
            element.SetValue(IsMouseCapturedPropertyKey, true);
            element.SetCaptureWithin(IsMouseCaptureWithinPropertyKey, true);
            element.RaiseEvent(new MouseEventArgs(GotMouseCaptureEvent));
        }
    }

    private static void SetStylusCapturedElement(UIElement3D? element)
    {
        var previous = s_stylusCaptured;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_stylusCaptured = element;
        if (previous is not null)
        {
            previous.SetValue(IsStylusCapturedPropertyKey, false);
            previous.SetCaptureWithin(IsStylusCaptureWithinPropertyKey, false);
            previous.RaiseEvent(new StylusEventArgs(null!, Environment.TickCount) { RoutedEvent = LostStylusCaptureEvent });
        }

        if (element is not null)
        {
            element.SetValue(IsStylusCapturedPropertyKey, true);
            element.SetCaptureWithin(IsStylusCaptureWithinPropertyKey, true);
            element.RaiseEvent(new StylusEventArgs(null!, Environment.TickCount) { RoutedEvent = GotStylusCaptureEvent });
        }
    }

    internal static void SetMouseDirectlyOverElement(UIElement3D? element)
    {
        var previous = s_mouseDirectlyOver;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_mouseDirectlyOver = element;
        previous?.SetOverState(IsMouseDirectlyOverPropertyKey, IsMouseOverPropertyKey, false);
        element?.SetOverState(IsMouseDirectlyOverPropertyKey, IsMouseOverPropertyKey, true);

        previous?.RaiseEvent(new MouseEventArgs(MouseLeaveEvent));
        element?.RaiseEvent(new MouseEventArgs(MouseEnterEvent));
    }

    internal static void SetStylusDirectlyOverElement(UIElement3D? element, StylusDevice? device = null)
    {
        var previous = s_stylusDirectlyOver;
        if (ReferenceEquals(previous, element))
        {
            return;
        }

        s_stylusDirectlyOver = element;
        previous?.SetOverState(IsStylusDirectlyOverPropertyKey, IsStylusOverPropertyKey, false);
        element?.SetOverState(IsStylusDirectlyOverPropertyKey, IsStylusOverPropertyKey, true);

        previous?.RaiseEvent(new StylusEventArgs(device!, Environment.TickCount) { RoutedEvent = StylusLeaveEvent });
        element?.RaiseEvent(new StylusEventArgs(device!, Environment.TickCount) { RoutedEvent = StylusEnterEvent });
    }

    internal void SetTouchDirectlyOver(TouchDevice touchDevice, bool isOver)
    {
        ArgumentNullException.ThrowIfNull(touchDevice);
        if (isOver ? _touchesDirectlyOver.Add(touchDevice) : _touchesDirectlyOver.Remove(touchDevice))
        {
            UpdateTouchOverState();
            RaiseEvent(new TouchEventArgs(touchDevice, Environment.TickCount)
            {
                RoutedEvent = isOver ? TouchEnterEvent : TouchLeaveEvent,
            });
        }
    }

    private void SetCaptureWithin(DependencyPropertyKey key, bool value)
    {
        for (UIElement3D? current = this; current is not null; current = current.Visual3DParent as UIElement3D)
        {
            current.SetValue(key, value);
        }
    }

    private void SetKeyboardFocusWithin(bool value)
    {
        for (UIElement3D? current = this; current is not null; current = current.Visual3DParent as UIElement3D)
        {
            current.SetValue(IsKeyboardFocusWithinPropertyKey, value);
        }
    }

    private void SetOverState(DependencyPropertyKey directlyOverKey, DependencyPropertyKey overKey, bool value)
    {
        SetValue(directlyOverKey, value);
        for (UIElement3D? current = this; current is not null; current = current.Visual3DParent as UIElement3D)
        {
            current.SetValue(overKey, value);
        }
    }

    private void UpdateTouchCaptureState()
    {
        var hasTouches = _touchesCaptured.Count != 0;
        SetValue(AreAnyTouchesCapturedPropertyKey, hasTouches);
        for (UIElement3D? current = this; current is not null; current = current.Visual3DParent as UIElement3D)
        {
            current.SetValue(AreAnyTouchesCapturedWithinPropertyKey, current.EnumerateTouchesCapturedWithin().Any());
        }
    }

    private void UpdateTouchOverState()
    {
        var hasTouches = _touchesDirectlyOver.Count != 0;
        SetValue(AreAnyTouchesDirectlyOverPropertyKey, hasTouches);
        for (UIElement3D? current = this; current is not null; current = current.Visual3DParent as UIElement3D)
        {
            current.SetValue(AreAnyTouchesOverPropertyKey, current.EnumerateTouchesOver().Any());
        }
    }

    private IEnumerable<TouchDevice> EnumerateTouchesCapturedWithin()
    {
        var touches = new HashSet<TouchDevice>(_touchesCaptured);
        AddDescendantTouches(touches, captured: true);
        return touches.ToArray();
    }

    private IEnumerable<TouchDevice> EnumerateTouchesOver()
    {
        var touches = new HashSet<TouchDevice>(_touchesDirectlyOver);
        AddDescendantTouches(touches, captured: false);
        return touches.ToArray();
    }

    private void AddDescendantTouches(HashSet<TouchDevice> result, bool captured)
    {
        for (var index = 0; index < Visual3DChildrenCount; index++)
        {
            if (GetVisual3DChild(index) is not UIElement3D child)
            {
                continue;
            }

            result.UnionWith(captured ? child._touchesCaptured : child._touchesDirectlyOver);
            child.AddDescendantTouches(result, captured);
        }
    }

    #region Events

    private static RoutedEvent Own(RoutedEvent routedEvent) => routedEvent.AddOwner(typeof(UIElement3D));

    public static readonly RoutedEvent PreviewKeyDownEvent = Own(UIElement.PreviewKeyDownEvent);
    public static readonly RoutedEvent KeyDownEvent = Own(UIElement.KeyDownEvent);
    public static readonly RoutedEvent PreviewKeyUpEvent = Own(UIElement.PreviewKeyUpEvent);
    public static readonly RoutedEvent KeyUpEvent = Own(UIElement.KeyUpEvent);
    public static readonly RoutedEvent PreviewTextInputEvent = Own(UIElement.PreviewTextInputEvent);
    public static readonly RoutedEvent TextInputEvent = Own(UIElement.TextInputEvent);

    public static readonly RoutedEvent PreviewGotKeyboardFocusEvent = Own(UIElement.PreviewGotKeyboardFocusEvent);
    public static readonly RoutedEvent GotKeyboardFocusEvent = Own(UIElement.GotKeyboardFocusEvent);
    public static readonly RoutedEvent PreviewLostKeyboardFocusEvent = Own(UIElement.PreviewLostKeyboardFocusEvent);
    public static readonly RoutedEvent LostKeyboardFocusEvent = Own(UIElement.LostKeyboardFocusEvent);
    public static readonly RoutedEvent GotFocusEvent = Own(UIElement.GotFocusEvent);
    public static readonly RoutedEvent LostFocusEvent = Own(UIElement.LostFocusEvent);

    public static readonly RoutedEvent PreviewMouseDownEvent = Own(UIElement.PreviewMouseDownEvent);
    public static readonly RoutedEvent MouseDownEvent = Own(UIElement.MouseDownEvent);
    public static readonly RoutedEvent PreviewMouseUpEvent = Own(UIElement.PreviewMouseUpEvent);
    public static readonly RoutedEvent MouseUpEvent = Own(UIElement.MouseUpEvent);
    public static readonly RoutedEvent PreviewMouseMoveEvent = Own(UIElement.PreviewMouseMoveEvent);
    public static readonly RoutedEvent MouseMoveEvent = Own(UIElement.MouseMoveEvent);
    public static readonly RoutedEvent MouseEnterEvent = Own(UIElement.MouseEnterEvent);
    public static readonly RoutedEvent MouseLeaveEvent = Own(UIElement.MouseLeaveEvent);
    public static readonly RoutedEvent PreviewMouseWheelEvent = Own(UIElement.PreviewMouseWheelEvent);
    public static readonly RoutedEvent MouseWheelEvent = Own(UIElement.MouseWheelEvent);
    public static readonly RoutedEvent PreviewMouseLeftButtonDownEvent = Own(UIElement.PreviewMouseLeftButtonDownEvent);
    public static readonly RoutedEvent MouseLeftButtonDownEvent = Own(UIElement.MouseLeftButtonDownEvent);
    public static readonly RoutedEvent PreviewMouseLeftButtonUpEvent = Own(UIElement.PreviewMouseLeftButtonUpEvent);
    public static readonly RoutedEvent MouseLeftButtonUpEvent = Own(UIElement.MouseLeftButtonUpEvent);
    public static readonly RoutedEvent PreviewMouseRightButtonDownEvent = Own(UIElement.PreviewMouseRightButtonDownEvent);
    public static readonly RoutedEvent MouseRightButtonDownEvent = Own(UIElement.MouseRightButtonDownEvent);
    public static readonly RoutedEvent PreviewMouseRightButtonUpEvent = Own(UIElement.PreviewMouseRightButtonUpEvent);
    public static readonly RoutedEvent MouseRightButtonUpEvent = Own(UIElement.MouseRightButtonUpEvent);
    public static readonly RoutedEvent GotMouseCaptureEvent = Own(UIElement.GotMouseCaptureEvent);
    public static readonly RoutedEvent LostMouseCaptureEvent = Own(UIElement.LostMouseCaptureEvent);

    public static readonly RoutedEvent PreviewStylusDownEvent = Own(UIElement.PreviewStylusDownEvent);
    public static readonly RoutedEvent StylusDownEvent = Own(UIElement.StylusDownEvent);
    public static readonly RoutedEvent PreviewStylusMoveEvent = Own(UIElement.PreviewStylusMoveEvent);
    public static readonly RoutedEvent StylusMoveEvent = Own(UIElement.StylusMoveEvent);
    public static readonly RoutedEvent PreviewStylusUpEvent = Own(UIElement.PreviewStylusUpEvent);
    public static readonly RoutedEvent StylusUpEvent = Own(UIElement.StylusUpEvent);
    public static readonly RoutedEvent PreviewStylusInAirMoveEvent = Own(UIElement.PreviewStylusInAirMoveEvent);
    public static readonly RoutedEvent StylusInAirMoveEvent = Own(UIElement.StylusInAirMoveEvent);
    public static readonly RoutedEvent StylusEnterEvent = Own(UIElement.StylusEnterEvent);
    public static readonly RoutedEvent StylusLeaveEvent = Own(UIElement.StylusLeaveEvent);
    public static readonly RoutedEvent PreviewStylusInRangeEvent = Own(UIElement.PreviewStylusInRangeEvent);
    public static readonly RoutedEvent StylusInRangeEvent = Own(UIElement.StylusInRangeEvent);
    public static readonly RoutedEvent PreviewStylusOutOfRangeEvent = Own(UIElement.PreviewStylusOutOfRangeEvent);
    public static readonly RoutedEvent StylusOutOfRangeEvent = Own(UIElement.StylusOutOfRangeEvent);
    public static readonly RoutedEvent PreviewStylusButtonDownEvent = Own(UIElement.PreviewStylusButtonDownEvent);
    public static readonly RoutedEvent StylusButtonDownEvent = Own(UIElement.StylusButtonDownEvent);
    public static readonly RoutedEvent PreviewStylusButtonUpEvent = Own(UIElement.PreviewStylusButtonUpEvent);
    public static readonly RoutedEvent StylusButtonUpEvent = Own(UIElement.StylusButtonUpEvent);
    public static readonly RoutedEvent PreviewStylusSystemGestureEvent = Own(UIElement.PreviewStylusSystemGestureEvent);
    public static readonly RoutedEvent StylusSystemGestureEvent = Own(UIElement.StylusSystemGestureEvent);
    public static readonly RoutedEvent GotStylusCaptureEvent = Own(UIElement.GotStylusCaptureEvent);
    public static readonly RoutedEvent LostStylusCaptureEvent = Own(UIElement.LostStylusCaptureEvent);

    public static readonly RoutedEvent PreviewTouchDownEvent = Own(UIElement.PreviewTouchDownEvent);
    public static readonly RoutedEvent TouchDownEvent = Own(UIElement.TouchDownEvent);
    public static readonly RoutedEvent PreviewTouchMoveEvent = Own(UIElement.PreviewTouchMoveEvent);
    public static readonly RoutedEvent TouchMoveEvent = Own(UIElement.TouchMoveEvent);
    public static readonly RoutedEvent PreviewTouchUpEvent = Own(UIElement.PreviewTouchUpEvent);
    public static readonly RoutedEvent TouchUpEvent = Own(UIElement.TouchUpEvent);
    public static readonly RoutedEvent TouchEnterEvent = Own(UIElement.TouchEnterEvent);
    public static readonly RoutedEvent TouchLeaveEvent = Own(UIElement.TouchLeaveEvent);
    public static readonly RoutedEvent GotTouchCaptureEvent = Own(UIElement.GotTouchCaptureEvent);
    public static readonly RoutedEvent LostTouchCaptureEvent = Own(UIElement.LostTouchCaptureEvent);

    public static readonly RoutedEvent PreviewDragEnterEvent = Own(UIElement.PreviewDragEnterEvent);
    public static readonly RoutedEvent DragEnterEvent = Own(UIElement.DragEnterEvent);
    public static readonly RoutedEvent PreviewDragOverEvent = Own(UIElement.PreviewDragOverEvent);
    public static readonly RoutedEvent DragOverEvent = Own(UIElement.DragOverEvent);
    public static readonly RoutedEvent PreviewDragLeaveEvent = Own(UIElement.PreviewDragLeaveEvent);
    public static readonly RoutedEvent DragLeaveEvent = Own(UIElement.DragLeaveEvent);
    public static readonly RoutedEvent PreviewDropEvent = Own(UIElement.PreviewDropEvent);
    public static readonly RoutedEvent DropEvent = Own(UIElement.DropEvent);
    public static readonly RoutedEvent PreviewGiveFeedbackEvent = Own(UIElement.PreviewGiveFeedbackEvent);
    public static readonly RoutedEvent GiveFeedbackEvent = Own(UIElement.GiveFeedbackEvent);
    public static readonly RoutedEvent PreviewQueryContinueDragEvent = Own(UIElement.PreviewQueryContinueDragEvent);
    public static readonly RoutedEvent QueryContinueDragEvent = Own(UIElement.QueryContinueDragEvent);
    public static readonly RoutedEvent QueryCursorEvent = Own(UIElement.QueryCursorEvent);

    public event DependencyPropertyChangedEventHandler? FocusableChanged;
    public event DependencyPropertyChangedEventHandler? IsEnabledChanged;
    public event DependencyPropertyChangedEventHandler? IsHitTestVisibleChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsKeyboardFocusedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsMouseDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCaptureWithinChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusCapturedChanged;
    public event DependencyPropertyChangedEventHandler? IsStylusDirectlyOverChanged;
    public event DependencyPropertyChangedEventHandler? IsVisibleChanged;

    public event KeyEventHandler PreviewKeyDown
    {
        add => AddHandler(PreviewKeyDownEvent, value);
        remove => RemoveHandler(PreviewKeyDownEvent, value);
    }

    public event KeyEventHandler KeyDown
    {
        add => AddHandler(KeyDownEvent, value);
        remove => RemoveHandler(KeyDownEvent, value);
    }

    public event KeyEventHandler PreviewKeyUp
    {
        add => AddHandler(PreviewKeyUpEvent, value);
        remove => RemoveHandler(PreviewKeyUpEvent, value);
    }

    public event KeyEventHandler KeyUp
    {
        add => AddHandler(KeyUpEvent, value);
        remove => RemoveHandler(KeyUpEvent, value);
    }

    public event TextCompositionEventHandler PreviewTextInput
    {
        add => AddHandler(PreviewTextInputEvent, value);
        remove => RemoveHandler(PreviewTextInputEvent, value);
    }

    public event TextCompositionEventHandler TextInput
    {
        add => AddHandler(TextInputEvent, value);
        remove => RemoveHandler(TextInputEvent, value);
    }

    public event KeyboardFocusChangedEventHandler PreviewGotKeyboardFocus
    {
        add => AddHandler(PreviewGotKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewGotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler GotKeyboardFocus
    {
        add => AddHandler(GotKeyboardFocusEvent, value);
        remove => RemoveHandler(GotKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler PreviewLostKeyboardFocus
    {
        add => AddHandler(PreviewLostKeyboardFocusEvent, value);
        remove => RemoveHandler(PreviewLostKeyboardFocusEvent, value);
    }

    public event KeyboardFocusChangedEventHandler LostKeyboardFocus
    {
        add => AddHandler(LostKeyboardFocusEvent, value);
        remove => RemoveHandler(LostKeyboardFocusEvent, value);
    }

    public event RoutedEventHandler GotFocus
    {
        add => AddHandler(GotFocusEvent, value);
        remove => RemoveHandler(GotFocusEvent, value);
    }

    public event RoutedEventHandler LostFocus
    {
        add => AddHandler(LostFocusEvent, value);
        remove => RemoveHandler(LostFocusEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseDown
    {
        add => AddHandler(PreviewMouseDownEvent, value);
        remove => RemoveHandler(PreviewMouseDownEvent, value);
    }

    public event MouseButtonEventHandler MouseDown
    {
        add => AddHandler(MouseDownEvent, value);
        remove => RemoveHandler(MouseDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseUp
    {
        add => AddHandler(PreviewMouseUpEvent, value);
        remove => RemoveHandler(PreviewMouseUpEvent, value);
    }

    public event MouseButtonEventHandler MouseUp
    {
        add => AddHandler(MouseUpEvent, value);
        remove => RemoveHandler(MouseUpEvent, value);
    }

    public event MouseEventHandler PreviewMouseMove
    {
        add => AddHandler(PreviewMouseMoveEvent, value);
        remove => RemoveHandler(PreviewMouseMoveEvent, value);
    }

    public event MouseEventHandler MouseMove
    {
        add => AddHandler(MouseMoveEvent, value);
        remove => RemoveHandler(MouseMoveEvent, value);
    }

    public event MouseEventHandler MouseEnter
    {
        add => AddHandler(MouseEnterEvent, value);
        remove => RemoveHandler(MouseEnterEvent, value);
    }

    public event MouseEventHandler MouseLeave
    {
        add => AddHandler(MouseLeaveEvent, value);
        remove => RemoveHandler(MouseLeaveEvent, value);
    }

    public event MouseWheelEventHandler PreviewMouseWheel
    {
        add => AddHandler(PreviewMouseWheelEvent, value);
        remove => RemoveHandler(PreviewMouseWheelEvent, value);
    }

    public event MouseWheelEventHandler MouseWheel
    {
        add => AddHandler(MouseWheelEvent, value);
        remove => RemoveHandler(MouseWheelEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseLeftButtonDown
    {
        add => AddHandler(PreviewMouseLeftButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonDownEvent, value);
    }

    public event MouseButtonEventHandler MouseLeftButtonDown
    {
        add => AddHandler(MouseLeftButtonDownEvent, value);
        remove => RemoveHandler(MouseLeftButtonDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseLeftButtonUp
    {
        add => AddHandler(PreviewMouseLeftButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseLeftButtonUpEvent, value);
    }

    public event MouseButtonEventHandler MouseLeftButtonUp
    {
        add => AddHandler(MouseLeftButtonUpEvent, value);
        remove => RemoveHandler(MouseLeftButtonUpEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseRightButtonDown
    {
        add => AddHandler(PreviewMouseRightButtonDownEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonDownEvent, value);
    }

    public event MouseButtonEventHandler MouseRightButtonDown
    {
        add => AddHandler(MouseRightButtonDownEvent, value);
        remove => RemoveHandler(MouseRightButtonDownEvent, value);
    }

    public event MouseButtonEventHandler PreviewMouseRightButtonUp
    {
        add => AddHandler(PreviewMouseRightButtonUpEvent, value);
        remove => RemoveHandler(PreviewMouseRightButtonUpEvent, value);
    }

    public event MouseButtonEventHandler MouseRightButtonUp
    {
        add => AddHandler(MouseRightButtonUpEvent, value);
        remove => RemoveHandler(MouseRightButtonUpEvent, value);
    }

    public event MouseEventHandler GotMouseCapture
    {
        add => AddHandler(GotMouseCaptureEvent, value);
        remove => RemoveHandler(GotMouseCaptureEvent, value);
    }

    public event MouseEventHandler LostMouseCapture
    {
        add => AddHandler(LostMouseCaptureEvent, value);
        remove => RemoveHandler(LostMouseCaptureEvent, value);
    }

    public event StylusDownEventHandler PreviewStylusDown
    {
        add => AddHandler(PreviewStylusDownEvent, value);
        remove => RemoveHandler(PreviewStylusDownEvent, value);
    }

    public event StylusDownEventHandler StylusDown
    {
        add => AddHandler(StylusDownEvent, value);
        remove => RemoveHandler(StylusDownEvent, value);
    }

    public event StylusEventHandler PreviewStylusMove
    {
        add => AddHandler(PreviewStylusMoveEvent, value);
        remove => RemoveHandler(PreviewStylusMoveEvent, value);
    }

    public event StylusEventHandler StylusMove
    {
        add => AddHandler(StylusMoveEvent, value);
        remove => RemoveHandler(StylusMoveEvent, value);
    }

    public event StylusEventHandler PreviewStylusUp
    {
        add => AddHandler(PreviewStylusUpEvent, value);
        remove => RemoveHandler(PreviewStylusUpEvent, value);
    }

    public event StylusEventHandler StylusUp
    {
        add => AddHandler(StylusUpEvent, value);
        remove => RemoveHandler(StylusUpEvent, value);
    }

    public event StylusEventHandler PreviewStylusInAirMove
    {
        add => AddHandler(PreviewStylusInAirMoveEvent, value);
        remove => RemoveHandler(PreviewStylusInAirMoveEvent, value);
    }

    public event StylusEventHandler StylusInAirMove
    {
        add => AddHandler(StylusInAirMoveEvent, value);
        remove => RemoveHandler(StylusInAirMoveEvent, value);
    }

    public event StylusEventHandler StylusEnter
    {
        add => AddHandler(StylusEnterEvent, value);
        remove => RemoveHandler(StylusEnterEvent, value);
    }

    public event StylusEventHandler StylusLeave
    {
        add => AddHandler(StylusLeaveEvent, value);
        remove => RemoveHandler(StylusLeaveEvent, value);
    }

    public event StylusEventHandler PreviewStylusInRange
    {
        add => AddHandler(PreviewStylusInRangeEvent, value);
        remove => RemoveHandler(PreviewStylusInRangeEvent, value);
    }

    public event StylusEventHandler StylusInRange
    {
        add => AddHandler(StylusInRangeEvent, value);
        remove => RemoveHandler(StylusInRangeEvent, value);
    }

    public event StylusEventHandler PreviewStylusOutOfRange
    {
        add => AddHandler(PreviewStylusOutOfRangeEvent, value);
        remove => RemoveHandler(PreviewStylusOutOfRangeEvent, value);
    }

    public event StylusEventHandler StylusOutOfRange
    {
        add => AddHandler(StylusOutOfRangeEvent, value);
        remove => RemoveHandler(StylusOutOfRangeEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonDown
    {
        add => AddHandler(PreviewStylusButtonDownEvent, value);
        remove => RemoveHandler(PreviewStylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler StylusButtonDown
    {
        add => AddHandler(StylusButtonDownEvent, value);
        remove => RemoveHandler(StylusButtonDownEvent, value);
    }

    public event StylusButtonEventHandler PreviewStylusButtonUp
    {
        add => AddHandler(PreviewStylusButtonUpEvent, value);
        remove => RemoveHandler(PreviewStylusButtonUpEvent, value);
    }

    public event StylusButtonEventHandler StylusButtonUp
    {
        add => AddHandler(StylusButtonUpEvent, value);
        remove => RemoveHandler(StylusButtonUpEvent, value);
    }

    public event StylusSystemGestureEventHandler PreviewStylusSystemGesture
    {
        add => AddHandler(PreviewStylusSystemGestureEvent, value);
        remove => RemoveHandler(PreviewStylusSystemGestureEvent, value);
    }

    public event StylusSystemGestureEventHandler StylusSystemGesture
    {
        add => AddHandler(StylusSystemGestureEvent, value);
        remove => RemoveHandler(StylusSystemGestureEvent, value);
    }

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

    public event EventHandler<TouchEventArgs> PreviewTouchDown
    {
        add => AddHandler(PreviewTouchDownEvent, value);
        remove => RemoveHandler(PreviewTouchDownEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchDown
    {
        add => AddHandler(TouchDownEvent, value);
        remove => RemoveHandler(TouchDownEvent, value);
    }

    public event EventHandler<TouchEventArgs> PreviewTouchMove
    {
        add => AddHandler(PreviewTouchMoveEvent, value);
        remove => RemoveHandler(PreviewTouchMoveEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchMove
    {
        add => AddHandler(TouchMoveEvent, value);
        remove => RemoveHandler(TouchMoveEvent, value);
    }

    public event EventHandler<TouchEventArgs> PreviewTouchUp
    {
        add => AddHandler(PreviewTouchUpEvent, value);
        remove => RemoveHandler(PreviewTouchUpEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchUp
    {
        add => AddHandler(TouchUpEvent, value);
        remove => RemoveHandler(TouchUpEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchEnter
    {
        add => AddHandler(TouchEnterEvent, value);
        remove => RemoveHandler(TouchEnterEvent, value);
    }

    public event EventHandler<TouchEventArgs> TouchLeave
    {
        add => AddHandler(TouchLeaveEvent, value);
        remove => RemoveHandler(TouchLeaveEvent, value);
    }

    public event EventHandler<TouchEventArgs> GotTouchCapture
    {
        add => AddHandler(GotTouchCaptureEvent, value);
        remove => RemoveHandler(GotTouchCaptureEvent, value);
    }

    public event EventHandler<TouchEventArgs> LostTouchCapture
    {
        add => AddHandler(LostTouchCaptureEvent, value);
        remove => RemoveHandler(LostTouchCaptureEvent, value);
    }

    public event DragEventHandler PreviewDragEnter
    {
        add => AddHandler(PreviewDragEnterEvent, value);
        remove => RemoveHandler(PreviewDragEnterEvent, value);
    }

    public event DragEventHandler DragEnter
    {
        add => AddHandler(DragEnterEvent, value);
        remove => RemoveHandler(DragEnterEvent, value);
    }

    public event DragEventHandler PreviewDragOver
    {
        add => AddHandler(PreviewDragOverEvent, value);
        remove => RemoveHandler(PreviewDragOverEvent, value);
    }

    public event DragEventHandler DragOver
    {
        add => AddHandler(DragOverEvent, value);
        remove => RemoveHandler(DragOverEvent, value);
    }

    public event DragEventHandler PreviewDragLeave
    {
        add => AddHandler(PreviewDragLeaveEvent, value);
        remove => RemoveHandler(PreviewDragLeaveEvent, value);
    }

    public event DragEventHandler DragLeave
    {
        add => AddHandler(DragLeaveEvent, value);
        remove => RemoveHandler(DragLeaveEvent, value);
    }

    public event DragEventHandler PreviewDrop
    {
        add => AddHandler(PreviewDropEvent, value);
        remove => RemoveHandler(PreviewDropEvent, value);
    }

    public event DragEventHandler Drop
    {
        add => AddHandler(DropEvent, value);
        remove => RemoveHandler(DropEvent, value);
    }

    public event GiveFeedbackEventHandler PreviewGiveFeedback
    {
        add => AddHandler(PreviewGiveFeedbackEvent, value);
        remove => RemoveHandler(PreviewGiveFeedbackEvent, value);
    }

    public event GiveFeedbackEventHandler GiveFeedback
    {
        add => AddHandler(GiveFeedbackEvent, value);
        remove => RemoveHandler(GiveFeedbackEvent, value);
    }

    public event QueryContinueDragEventHandler PreviewQueryContinueDrag
    {
        add => AddHandler(PreviewQueryContinueDragEvent, value);
        remove => RemoveHandler(PreviewQueryContinueDragEvent, value);
    }

    public event QueryContinueDragEventHandler QueryContinueDrag
    {
        add => AddHandler(QueryContinueDragEvent, value);
        remove => RemoveHandler(QueryContinueDragEvent, value);
    }

    public event QueryCursorEventHandler QueryCursor
    {
        add => AddHandler(QueryCursorEvent, value);
        remove => RemoveHandler(QueryCursorEvent, value);
    }

    #endregion

    #region Overrides

    protected virtual Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;

    protected virtual void OnAccessKey(AccessKeyEventArgs e)
    {
    }

    protected virtual void OnGotFocus(RoutedEventArgs e)
    {
    }

    protected virtual void OnLostFocus(RoutedEventArgs e)
    {
    }

    protected virtual void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsKeyboardFocusedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsMouseDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCaptureWithinChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected virtual void OnIsStylusDirectlyOverChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewKeyDown(KeyEventArgs e)
    {
    }

    protected internal virtual void OnKeyDown(KeyEventArgs e)
    {
    }

    protected internal virtual void OnPreviewKeyUp(KeyEventArgs e)
    {
    }

    protected internal virtual void OnKeyUp(KeyEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTextInput(TextCompositionEventArgs e)
    {
    }

    protected internal virtual void OnTextInput(TextCompositionEventArgs e)
    {
    }

    protected internal virtual void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseMove(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseMove(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseEnter(MouseEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeave(MouseEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
    }

    protected internal virtual void OnMouseWheel(MouseWheelEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
    }

    protected internal virtual void OnGotMouseCapture(MouseEventArgs e)
    {
    }

    protected internal virtual void OnLostMouseCapture(MouseEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusDown(StylusDownEventArgs e)
    {
    }

    protected internal virtual void OnStylusDown(StylusDownEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusUp(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusUp(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusInAirMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusInAirMove(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusEnter(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusLeave(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusInRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusInRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnStylusOutOfRange(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnStylusButtonDown(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnStylusButtonUp(StylusButtonEventArgs e)
    {
    }

    protected internal virtual void OnPreviewStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }

    protected internal virtual void OnStylusSystemGesture(StylusSystemGestureEventArgs e)
    {
    }

    protected internal virtual void OnGotStylusCapture(StylusEventArgs e)
    {
    }

    protected internal virtual void OnLostStylusCapture(StylusEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchDown(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchDown(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchMove(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchMove(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewTouchUp(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchUp(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchEnter(TouchEventArgs e)
    {
    }

    protected internal virtual void OnTouchLeave(TouchEventArgs e)
    {
    }

    protected internal virtual void OnGotTouchCapture(TouchEventArgs e)
    {
    }

    protected internal virtual void OnLostTouchCapture(TouchEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragEnter(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragEnter(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragOver(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragOver(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDragLeave(DragEventArgs e)
    {
    }

    protected internal virtual void OnDragLeave(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewDrop(DragEventArgs e)
    {
    }

    protected internal virtual void OnDrop(DragEventArgs e)
    {
    }

    protected internal virtual void OnPreviewGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    protected internal virtual void OnGiveFeedback(GiveFeedbackEventArgs e)
    {
    }

    protected internal virtual void OnPreviewQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    protected internal virtual void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
    }

    protected internal virtual void OnQueryCursor(QueryCursorEventArgs e)
    {
    }

    private void InvokeClassHandler(RoutedEventArgs e)
    {
        var routedEvent = e.RoutedEvent;
        if (ReferenceEquals(routedEvent, PreviewKeyDownEvent)) OnPreviewKeyDown((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, KeyDownEvent)) OnKeyDown((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewKeyUpEvent)) OnPreviewKeyUp((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, KeyUpEvent)) OnKeyUp((KeyEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTextInputEvent)) OnPreviewTextInput((TextCompositionEventArgs)e);
        else if (ReferenceEquals(routedEvent, TextInputEvent)) OnTextInput((TextCompositionEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewGotKeyboardFocusEvent)) OnPreviewGotKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotKeyboardFocusEvent)) OnGotKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewLostKeyboardFocusEvent)) OnPreviewLostKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostKeyboardFocusEvent)) OnLostKeyboardFocus((KeyboardFocusChangedEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotFocusEvent)) OnGotFocus(e);
        else if (ReferenceEquals(routedEvent, LostFocusEvent)) OnLostFocus(e);
        else if (ReferenceEquals(routedEvent, PreviewMouseDownEvent)) OnPreviewMouseDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseDownEvent)) OnMouseDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseUpEvent)) OnPreviewMouseUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseUpEvent)) OnMouseUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseMoveEvent)) OnPreviewMouseMove((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseMoveEvent)) OnMouseMove((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseEnterEvent)) OnMouseEnter((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeaveEvent)) OnMouseLeave((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseWheelEvent)) OnPreviewMouseWheel((MouseWheelEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseWheelEvent)) OnMouseWheel((MouseWheelEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseLeftButtonDownEvent)) OnPreviewMouseLeftButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeftButtonDownEvent)) OnMouseLeftButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseLeftButtonUpEvent)) OnPreviewMouseLeftButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseLeftButtonUpEvent)) OnMouseLeftButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseRightButtonDownEvent)) OnPreviewMouseRightButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseRightButtonDownEvent)) OnMouseRightButtonDown((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewMouseRightButtonUpEvent)) OnPreviewMouseRightButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, MouseRightButtonUpEvent)) OnMouseRightButtonUp((MouseButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotMouseCaptureEvent)) OnGotMouseCapture((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostMouseCaptureEvent)) OnLostMouseCapture((MouseEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusDownEvent)) OnPreviewStylusDown((StylusDownEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusDownEvent)) OnStylusDown((StylusDownEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusMoveEvent)) OnPreviewStylusMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusMoveEvent)) OnStylusMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusUpEvent)) OnPreviewStylusUp((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusUpEvent)) OnStylusUp((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusInAirMoveEvent)) OnPreviewStylusInAirMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusInAirMoveEvent)) OnStylusInAirMove((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusEnterEvent)) OnStylusEnter((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusLeaveEvent)) OnStylusLeave((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusInRangeEvent)) OnPreviewStylusInRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusInRangeEvent)) OnStylusInRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusOutOfRangeEvent)) OnPreviewStylusOutOfRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusOutOfRangeEvent)) OnStylusOutOfRange((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusButtonDownEvent)) OnPreviewStylusButtonDown((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusButtonDownEvent)) OnStylusButtonDown((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusButtonUpEvent)) OnPreviewStylusButtonUp((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusButtonUpEvent)) OnStylusButtonUp((StylusButtonEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewStylusSystemGestureEvent)) OnPreviewStylusSystemGesture((StylusSystemGestureEventArgs)e);
        else if (ReferenceEquals(routedEvent, StylusSystemGestureEvent)) OnStylusSystemGesture((StylusSystemGestureEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotStylusCaptureEvent)) OnGotStylusCapture((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostStylusCaptureEvent)) OnLostStylusCapture((StylusEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchDownEvent)) OnPreviewTouchDown((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchDownEvent)) OnTouchDown((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchMoveEvent)) OnPreviewTouchMove((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchMoveEvent)) OnTouchMove((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewTouchUpEvent)) OnPreviewTouchUp((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchUpEvent)) OnTouchUp((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchEnterEvent)) OnTouchEnter((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, TouchLeaveEvent)) OnTouchLeave((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, GotTouchCaptureEvent)) OnGotTouchCapture((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, LostTouchCaptureEvent)) OnLostTouchCapture((TouchEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragEnterEvent)) OnPreviewDragEnter((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragEnterEvent)) OnDragEnter((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragOverEvent)) OnPreviewDragOver((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragOverEvent)) OnDragOver((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDragLeaveEvent)) OnPreviewDragLeave((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DragLeaveEvent)) OnDragLeave((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewDropEvent)) OnPreviewDrop((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, DropEvent)) OnDrop((DragEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewGiveFeedbackEvent)) OnPreviewGiveFeedback((GiveFeedbackEventArgs)e);
        else if (ReferenceEquals(routedEvent, GiveFeedbackEvent)) OnGiveFeedback((GiveFeedbackEventArgs)e);
        else if (ReferenceEquals(routedEvent, PreviewQueryContinueDragEvent)) OnPreviewQueryContinueDrag((QueryContinueDragEventArgs)e);
        else if (ReferenceEquals(routedEvent, QueryContinueDragEvent)) OnQueryContinueDrag((QueryContinueDragEventArgs)e);
        else if (ReferenceEquals(routedEvent, QueryCursorEvent)) OnQueryCursor((QueryCursorEventArgs)e);
    }

    #endregion
}