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
}
