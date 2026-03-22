using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the base class for all button controls.
/// </summary>
public abstract class ButtonBase : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ButtonBaseAutomationPeer(this);
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
        DependencyProperty.Register(nameof(Command), typeof(System.Windows.Input.ICommand), typeof(ButtonBase),
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
    public new bool IsPressed => base.IsPressed;

    /// <summary>
    /// Gets or sets the command to invoke when this button is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public System.Windows.Input.ICommand? Command
    {
        get => (System.Windows.Input.ICommand?)GetValue(CommandProperty);
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

        // Register mouse event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(KeyUpEvent, new KeyEventHandler(OnKeyUpHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            // Capture mouse to receive mouse events even when mouse moves outside
            CaptureMouse();
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
        if (!IsEnabled) return;

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
        if (ClickMode == ClickMode.Hover)
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

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

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
        if (!IsEnabled) return;

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
    protected virtual void OnMouseEnter(MouseEventArgs e)
    {
    }

    /// <summary>
    /// Called when the mouse leaves the button.
    /// </summary>
    protected virtual void OnMouseLeave(MouseEventArgs e)
    {
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
    internal void SetIsPressed(bool value)
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

    #endregion

    #region Command Changed Callbacks

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button) return;

        if (e.OldValue is System.Windows.Input.ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= button.OnCanExecuteChanged;
        }

        if (e.NewValue is System.Windows.Input.ICommand newCommand)
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
    protected override void OnIsPressedChanged(bool oldValue, bool newValue)
    {
        base.OnIsPressedChanged(oldValue, newValue);
    }

    #endregion
}

/// <summary>
/// Specifies when the Click event should be raised.
/// </summary>
public enum ClickMode
{
    /// <summary>
    /// Click is raised when the mouse button is released.
    /// </summary>
    Release,

    /// <summary>
    /// Click is raised when the mouse button is pressed.
    /// </summary>
    Press,

    /// <summary>
    /// Click is raised when the mouse enters the button.
    /// </summary>
    Hover
}
