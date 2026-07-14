using System.Windows.Input;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the base class for all button controls.
/// </summary>
public abstract class ButtonBase : ContentControl, ICommandSource
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.GenericButtonBaseAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsPressed dependency property.
    /// </summary>
    public new static readonly DependencyProperty IsPressedProperty = UIElement.IsPressedProperty;

    /// <summary>
    /// Identifies the Command dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(ButtonBase),
            new PropertyMetadata(null, OnCommandChanged));

    /// <summary>
    /// Identifies the CommandParameter dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ButtonBase),
            new PropertyMetadata(null, OnCommandParameterChanged));

    /// <summary>
    /// Identifies the CommandTarget dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register(nameof(CommandTarget), typeof(IInputElement), typeof(ButtonBase),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ClickMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ClickModeProperty =
        DependencyProperty.Register(nameof(ClickMode), typeof(ClickMode), typeof(ButtonBase),
            new PropertyMetadata(ClickMode.Release));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Click routed event.
    /// </summary>
    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ButtonBase));

    /// <summary>
    /// Occurs when the button is clicked.
    /// </summary>
    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets a value indicating whether the button is currently pressed.
    /// </summary>
    public new bool IsPressed
    {
        get => base.IsPressed;
        protected set => base.SetIsPressed(value);
    }

    /// <summary>
    /// Gets or sets the command to invoke when this button is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the parameter to pass to the Command property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>
    /// Gets or sets the element on which to raise the specified command.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets when the Click event should be raised.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ClickMode ClickMode
    {
        get => (ClickMode)GetValue(ClickModeProperty)!;
        set => SetValue(ClickModeProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ButtonBase"/> class.
    /// </summary>
    protected ButtonBase()
    {
        // Use ControlTemplate for visual appearance instead of direct content
        UseTemplateContentManagement();
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        Focusable = true;

        // Touch ripple is on by default for buttons — apps can opt out via
        // TouchHelper.SetIsRippleEnabled(button, false).
        TouchHelper.SetIsRippleEnabled(this, true);

        // Register mouse event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(KeyUpEvent, new KeyEventHandler(OnKeyUpHandler));

        // Register touch event handlers. Touch is captured per-contact so a
        // second simultaneous finger on another button still activates that
        // button; mouse capture is single-slot and would have blocked it.
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        AddHandler(TouchMoveEvent, new RoutedEventHandler(OnTouchMoveHandler));
        AddHandler(TouchUpEvent, new RoutedEventHandler(OnTouchUpHandler));
        AddHandler(TouchEnterEvent, new RoutedEventHandler(OnTouchEnterHandler));
        AddHandler(TouchLeaveEvent, new RoutedEventHandler(OnTouchLeaveHandler));
        AddHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnLostTouchCaptureHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!CanRespondToInput()) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            // Capture mouse to receive mouse events even when mouse moves outside
            CaptureMouse();
            // Re-assert hover: a mouse-down means the pointer is over this button, but a
            // just-finished command's async CanExecute flap may have driven IsEnabled false
            // for a beat, and OnIsEnabledChanged clears IsMouseOver when that happens. Without
            // restoring it the ClickMode.Release condition (wasPressed && IsMouseOver) in
            // OnMouseUpHandler would silently drop the click. CanRespondToInput already healed
            // IsEnabled to true above, so SetIsMouseOver's enabled-guard passes.
            SetIsMouseOver(true);
            SetIsPressed(true);
            Focus();

            if (ClickMode == ClickMode.Press)
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (!CanRespondToInput()) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            var wasPressed = IsPressed;

            // Release mouse capture
            ReleaseMouseCapture();
            SetIsPressed(false);

            // Only fire click if mouse is still over the button (for Release mode)
            if (wasPressed && IsMouseOver && ClickMode == ClickMode.Release)
            {
                OnClick();
            }

            e.Handled = true;
        }
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        // ClickMode.Hover fires on mouse-enter. Gate it through CanRespondToInput so a disabled
        // button no longer auto-activates on hover (it previously had no IsEnabled check at all),
        // while a button only transiently disabled by a command's async CanExecute flap still works.
        if (ClickMode == ClickMode.Hover && CanRespondToInput())
        {
            OnClick();
        }

        OnMouseEnter(e);
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        OnMouseLeave(e);
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        // If we lose capture unexpectedly, reset pressed state
        if (IsPressed)
        {
            SetIsPressed(false);
        }
    }

    // ── Touch handlers ─────────────────────────────────────────────
    // The active touch contact (if any) that drove the press, plus the
    // down-position used by the panning gate. If the finger drifts more
    // than PanCancelThresholdDips before lifting, we treat the gesture as
    // a scroll candidate and silently bail out so an ancestor ScrollViewer
    // (which gets PointerMove via routing) can take over.
    private const double PanCancelThresholdDips = 8.0;
    private int _activeTouchId = -1;
    private Point _activeTouchDownPosition;
    private bool _touchClickCandidate;

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!CanRespondToInput() || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;

        _activeTouchId = touchArgs.TouchDevice.Id;
        _activeTouchDownPosition = touchArgs.GetTouchPoint(this).Position;
        _touchClickCandidate = true;
        // Do NOT CaptureTouch — leaving the contact uncaptured lets PointerMove
        // bubble up to ancestor ScrollViewer / manipulation hosts, so a finger
        // that drifts off the button hands control to the scroller.
        SetIsPressed(true);
        Focus();
        if (ClickMode == ClickMode.Press)
        {
            OnClick();
        }

        // Mark handled so the mouse synthesis layer in WindowInputDispatcher
        // (`sourceHandled |= bubbleArgs.Handled` then guards SynthesizeMouseFromTouch)
        // does not fire a duplicate MouseDown on this button. PointerDown is
        // raised unconditionally by the dispatcher so ancestor ScrollViewers
        // still see it.
        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        if (!_touchClickCandidate) return;

        var current = touchArgs.GetTouchPoint(this).Position;
        double dx = current.X - _activeTouchDownPosition.X;
        double dy = current.Y - _activeTouchDownPosition.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > PanCancelThresholdDips)
        {
            // Pan / drag gesture detected. Drop the press visual and stop
            // marking events as handled so an ancestor ScrollViewer can
            // proceed with panning via PointerMove.
            _touchClickCandidate = false;
            SetIsPressed(false);
        }
    }

    private void OnTouchUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId)
        {
            return;
        }

        bool wasCandidate = _touchClickCandidate;
        _activeTouchId = -1;
        _touchClickCandidate = false;
        SetIsPressed(false);

        // ClickMode.Release: fire Click only when the contact lifts on the
        // button AND we still considered it a click (no pan-drift cancellation).
        if (wasCandidate && ClickMode == ClickMode.Release)
        {
            OnClick();
        }
        else
        {
        }

        // If we never committed to a click (pan-cancelled) let the touch-up
        // bubble freely so the ancestor ScrollViewer can finish its
        // PointerUp inertia path. Otherwise eat the event to prevent mouse
        // synthesis from firing a phantom click.
        if (wasCandidate)
        {
            e.Handled = true;
        }
    }

    private void OnTouchEnterHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id == _activeTouchId)
        {
            // Re-pressed visual when the finger returns to the button.
            SetIsPressed(true);
        }
    }

    private void OnTouchLeaveHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id == _activeTouchId)
        {
            // Drop pressed-visual while the finger is outside the button bounds.
            SetIsPressed(false);
        }
    }

    private void OnLostTouchCaptureHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        _activeTouchId = -1;
        if (IsPressed)
        {
            SetIsPressed(false);
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!CanRespondToInput()) return;

        if (e.Key == Key.Space)
        {
            // Space key presses the button
            if (ClickMode == ClickMode.Press)
            {
                OnClick();
            }
            else
            {
                SetIsPressed(true);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Enter key clicks immediately
            OnClick();
            e.Handled = true;
        }
    }

    private void OnKeyUpHandler(object sender, KeyEventArgs e)
    {
        if (!CanRespondToInput()) return;

        if (e.Key == Key.Space && IsPressed)
        {
            SetIsPressed(false);
            if (ClickMode == ClickMode.Release)
            {
                OnClick();
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Called when the mouse enters the button.
    /// </summary>
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
    }

    /// <summary>
    /// Called when the mouse leaves the button.
    /// </summary>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
    }

    #endregion

    #region Click Handling

    /// <summary>
    /// Raises the Click event and executes the Command if set.
    /// </summary>
    protected virtual void OnClick()
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        ExecuteCommand();
    }

    /// <summary>
    /// Programmatically performs a click action on the button.
    /// </summary>
    public void PerformClick()
    {
        if (IsEnabled)
        {
            OnClick();
        }
    }

    /// <summary>
    /// Sets the IsPressed property value.
    /// </summary>
    internal new void SetIsPressed(bool value)
    {
        base.SetIsPressed(value);
    }

    private void ExecuteCommand()
    {
        var command = Command;
        if (command == null) return;

        var parameter = CommandParameter;

        if (command is RoutedCommand routedCommand)
        {
            var target = CommandTarget ?? this;
            if (routedCommand.CanExecute(parameter, target))
            {
                routedCommand.Execute(parameter, target);
            }
        }
        else if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    /// <summary>
    /// Determines whether the control should respond to an input gesture (mouse / touch / key
    /// down and up, and <see cref="Primitives.RepeatButton"/> auto-repeat). This is the input-side
    /// counterpart to <see cref="ExecuteCommand"/>.
    /// <para>
    /// When the bound <see cref="Command"/> toggles its <c>CanExecute</c> while it executes
    /// (e.g. a ReactiveCommand), the change notification is delivered asynchronously, so
    /// <see cref="UpdateCanExecute"/> can leave <see cref="UIElement.IsEnabled"/> stuck at
    /// <see langword="false"/> for a beat after the command has already finished. That swallows
    /// rapid repeat clicks — the user has to move the pointer off the button and back to click
    /// again. To avoid it, when the element is disabled solely through that transient we re-query
    /// the command live and, if it can run right now, treat the cached disabled state as stale.
    /// </para>
    /// </summary>
    internal bool CanRespondToInput()
    {
        // Effectively enabled (own value AND every ancestor enabled): the common path, no work.
        if (IsEnabled) return true;

        // Effectively disabled. The only state we override is a transient, command-driven disable.
        var command = Command;
        if (command == null) return false; // disabled by the app with no command to re-query: honor it.

        // A disabled ancestor is a real disable, never a command flap — never override it.
        if (VisualParent is UIElement parent && !parent.IsEnabled) return false;

        // The local IsEnabled is the only thing keeping us disabled. Ask the command whether it can
        // run at this instant: an async command genuinely mid-flight (or one whose CanExecute is
        // legitimately false) still reports false and stays blocked; a just-finished command reports
        // true, revealing the disabled state as a stale async flap.
        bool canExecuteNow = command is RoutedCommand routedCommand
            ? routedCommand.CanExecute(CommandParameter, CommandTarget ?? this)
            : command.CanExecute(CommandParameter);
        if (!canExecuteNow) return false;

        // Stale flap: heal the local IsEnabled value so the IsEnabled=False style trigger reverts at
        // once and OnIsEnabledChanged stops clearing IsMouseOver/IsPressed (which ClickMode.Release
        // depends on). SetCurrentValue keeps any IsEnabled binding intact; the next CanExecuteChanged
        // writes true as well, so this is idempotent.
        SetCurrentValue(UIElement.IsEnabledProperty, true);
        return true;
    }

    #endregion

    #region Command Changed Callbacks

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        if (e.OldValue is ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= button.OnCanExecuteChanged;
        }

        if (e.NewValue is ICommand newCommand)
        {
            newCommand.CanExecuteChanged += button.OnCanExecuteChanged;
        }

        button.UpdateCanExecute();
    }

    private static void OnCommandParameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ButtonBase button)
        {
            button.UpdateCanExecute();
        }
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateCanExecute();
    }

    private void UpdateCanExecute()
    {
        var command = Command;
        if (command == null)
        {
            return;
        }

        bool canExecute;
        if (command is RoutedCommand routedCommand)
        {
            var target = CommandTarget ?? this;
            canExecute = routedCommand.CanExecute(CommandParameter, target);
        }
        else
        {
            canExecute = command.CanExecute(CommandParameter);
        }

        IsEnabled = canExecute;
    }

    #endregion

    #region Property Changed Callbacks

    /// <summary>
    /// Called when the IsPressed property changes.
    /// </summary>
    internal override void OnIsPressedChanged(bool oldValue, bool newValue)
    {
        base.OnIsPressedChanged(oldValue, newValue);
        OnIsPressedChanged(new DependencyPropertyChangedEventArgs(
            IsPressedProperty,
            oldValue,
            newValue));
    }

    /// <summary>
    /// Called when the value of <see cref="IsPressed"/> changes.
    /// </summary>
    protected virtual void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    #endregion
}
