using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Specifies the mouse button.
/// </summary>
public enum MouseButton
{
    /// <summary>
    /// The left mouse button.
    /// </summary>
    Left,

    /// <summary>
    /// The middle mouse button.
    /// </summary>
    Middle,

    /// <summary>
    /// The right mouse button.
    /// </summary>
    Right,

    /// <summary>
    /// The first extended mouse button.
    /// </summary>
    XButton1,

    /// <summary>
    /// The second extended mouse button.
    /// </summary>
    XButton2
}

/// <summary>
/// Specifies the state of a mouse button.
/// </summary>
public enum MouseButtonState
{
    /// <summary>
    /// The button is released.
    /// </summary>
    Released,

    /// <summary>
    /// The button is pressed.
    /// </summary>
    Pressed
}

/// <summary>
/// Provides data for mouse events.
/// </summary>
public class MouseEventArgs : InputEventArgs
{
    /// <summary>
    /// Gets the position of the mouse relative to the source element.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the state of the left mouse button.
    /// </summary>
    public MouseButtonState LeftButton { get; }

    /// <summary>
    /// Gets the state of the middle mouse button.
    /// </summary>
    public MouseButtonState MiddleButton { get; }

    /// <summary>
    /// Gets the state of the right mouse button.
    /// </summary>
    public MouseButtonState RightButton { get; }

    /// <summary>
    /// Gets the state of the first extended mouse button.
    /// </summary>
    public MouseButtonState XButton1 { get; }

    /// <summary>
    /// Gets the state of the second extended mouse button.
    /// </summary>
    public MouseButtonState XButton2 { get; }

    /// <summary>
    /// Gets the modifier keys that were pressed during the event.
    /// </summary>
    public ModifierKeys KeyboardModifiers { get; }

    /// <summary>
    /// Gets or sets a value indicating whether downstream pointer promotion should be canceled.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseEventArgs"/> class.
    /// </summary>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="position">The mouse position.</param>
    /// <param name="leftButton">The left button state.</param>
    /// <param name="middleButton">The middle button state.</param>
    /// <param name="rightButton">The right button state.</param>
    /// <param name="xButton1">The XButton1 state.</param>
    /// <param name="xButton2">The XButton2 state.</param>
    /// <param name="modifiers">The keyboard modifiers.</param>
    /// <param name="timestamp">The event timestamp.</param>
    public MouseEventArgs(
        RoutedEvent routedEvent,
        Point position,
        MouseButtonState leftButton,
        MouseButtonState middleButton,
        MouseButtonState rightButton,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp)
        : base(routedEvent, timestamp)
    {
        Position = position;
        LeftButton = leftButton;
        MiddleButton = middleButton;
        RightButton = rightButton;
        XButton1 = xButton1;
        XButton2 = xButton2;
        KeyboardModifiers = modifiers;
    }

    /// <summary>
    /// Gets the position of the mouse relative to the specified element.
    /// </summary>
    /// <param name="relativeTo">The element to get the position relative to.</param>
    /// <returns>The relative position.</returns>
    public Point GetPosition(UIElement? relativeTo)
    {
        if (relativeTo == null)
            return Position;

        // Walk up the visual tree to calculate the element's absolute position
        double offsetX = 0;
        double offsetY = 0;

        Visual? current = relativeTo;
        while (current != null)
        {
            // Skip the root element (Window) - mouse Position is already in window client coordinates
            if (current.VisualParent == null)
                break;

            if (current is FrameworkElement fe)
            {
                var bounds = fe.VisualBounds;
                offsetX += bounds.X;
                offsetY += bounds.Y;
            }
            current = current.VisualParent;
        }

        // Return position relative to the element
        return new Point(Position.X - offsetX, Position.Y - offsetY);
    }

    /// <summary>
    /// Debug helper: Gets the visual bounds chain from an element to the root.
    /// Returns a string describing each element's type, name, and VisualBounds.
    /// </summary>
    public static string GetVisualBoundsChain(UIElement element)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Visual Bounds Chain for {element.GetType().Name}:");

        Visual? current = element;
        int depth = 0;
        double totalX = 0, totalY = 0;

        while (current != null)
        {
            string indent = new string(' ', depth * 2);
            string name = (current as FrameworkElement)?.Name ?? "";
            if (!string.IsNullOrEmpty(name)) name = $" [{name}]";

            if (current is FrameworkElement fe)
            {
                var bounds = fe.VisualBounds;
                sb.AppendLine($"{indent}{current.GetType().Name}{name}: VisualBounds=({bounds.X:F1}, {bounds.Y:F1}, {bounds.Width:F1}, {bounds.Height:F1})");

                if (current.VisualParent != null)
                {
                    totalX += bounds.X;
                    totalY += bounds.Y;
                }
            }
            else
            {
                sb.AppendLine($"{indent}{current.GetType().Name}{name}: (not FrameworkElement)");
            }

            if (current.VisualParent == null)
            {
                sb.AppendLine($"{indent}  VisualParent = null (root)");
            }

            current = current.VisualParent;
            depth++;
        }

        sb.AppendLine($"Total offset: ({totalX:F1}, {totalY:F1})");
        return sb.ToString();
    }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is MouseEventHandler mouseHandler)
        {
            mouseHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Delegate for handling mouse events.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="e">The event arguments.</param>
public delegate void MouseEventHandler(object sender, MouseEventArgs e);

/// <summary>
/// Provides data for mouse button events.
/// </summary>
public sealed class MouseButtonEventArgs : MouseEventArgs
{
    /// <summary>
    /// Gets the button that changed state.
    /// </summary>
    public MouseButton ChangedButton { get; }

    /// <summary>
    /// Gets the state of the changed button.
    /// </summary>
    public MouseButtonState ButtonState { get; }

    /// <summary>
    /// Gets the click count (1 for single click, 2 for double click).
    /// </summary>
    public int ClickCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseButtonEventArgs"/> class.
    /// </summary>
    public MouseButtonEventArgs(
        RoutedEvent routedEvent,
        Point position,
        MouseButton changedButton,
        MouseButtonState buttonState,
        int clickCount,
        MouseButtonState leftButton,
        MouseButtonState middleButton,
        MouseButtonState rightButton,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp)
        : base(routedEvent, position, leftButton, middleButton, rightButton, xButton1, xButton2, modifiers, timestamp)
    {
        ChangedButton = changedButton;
        ButtonState = buttonState;
        ClickCount = clickCount;
    }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is MouseButtonEventHandler buttonHandler)
        {
            buttonHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Delegate for handling mouse button events.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="e">The event arguments.</param>
public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);

/// <summary>
/// Provides data for mouse wheel events.
/// </summary>
public sealed class MouseWheelEventArgs : MouseEventArgs
{
    /// <summary>
    /// Gets the wheel delta. Positive values indicate forward rotation, negative values indicate backward rotation.
    /// </summary>
    public int Delta { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseWheelEventArgs"/> class.
    /// </summary>
    public MouseWheelEventArgs(
        RoutedEvent routedEvent,
        Point position,
        int delta,
        MouseButtonState leftButton,
        MouseButtonState middleButton,
        MouseButtonState rightButton,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp)
        : base(routedEvent, position, leftButton, middleButton, rightButton, xButton1, xButton2, modifiers, timestamp)
    {
        Delta = delta;
    }

    /// <inheritdoc />
    protected internal override void InvokeEventHandler(Delegate handler, object target)
    {
        if (handler is MouseWheelEventHandler wheelHandler)
        {
            wheelHandler(target, this);
        }
        else
        {
            base.InvokeEventHandler(handler, target);
        }
    }
}

/// <summary>
/// Delegate for handling mouse wheel events.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="e">The event arguments.</param>
public delegate void MouseWheelEventHandler(object sender, MouseWheelEventArgs e);
