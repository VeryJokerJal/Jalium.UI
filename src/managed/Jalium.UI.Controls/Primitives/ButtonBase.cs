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
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(KeyUpEvent, new RoutedEventHandler(OnKeyUpHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
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

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
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

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        if (ClickMode == ClickMode.Hover)
        {
            OnClick();
        }

        OnMouseEnter(e);
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
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

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            if (keyArgs.Key == Key.Space)
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
            else if (keyArgs.Key == Key.Enter)
            {
                // Enter key clicks immediately
                OnClick();
                e.Handled = true;
            }
        }
    }

    private void OnKeyUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            if (keyArgs.Key == Key.Space && IsPressed)
            {
                SetIsPressed(false);
                if (ClickMode == ClickMode.Release)
                {
                    OnClick();
                }
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Called when the mouse enters the button.
    /// </summary>
    protected virtual void OnMouseEnter(RoutedEventArgs e)
    {
    }

    /// <summary>
    /// Called when the mouse leaves the button.
    /// </summary>
    protected virtual void OnMouseLeave(RoutedEventArgs e)
    {
    }

    #endregion

    #region Click Handling

    /// <summary>
    /// Raises the Click event.
    /// </summary>
    protected virtual void OnClick()
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
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
