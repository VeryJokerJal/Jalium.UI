using Jalium.UI;

namespace Jalium.UI.Input;

/// <summary>
/// Specifies the mouse button.
/// </summary>
public enum MouseButton
{
    Left,
    Middle,
    Right,
    XButton1,
    XButton2
}

/// <summary>
/// Specifies the state of a mouse button.
/// </summary>
public enum MouseButtonState
{
    Released,
    Pressed
}

/// <summary>
/// Normalized mouse button states for platform-independent input dispatch.
/// Both Win32 wParam parsing and PlatformEvent.Modifiers produce this struct.
/// </summary>
public readonly struct MouseButtonStates
{
    public MouseButtonState Left { get; init; }
    public MouseButtonState Middle { get; init; }
    public MouseButtonState Right { get; init; }
    public MouseButtonState XButton1 { get; init; }
    public MouseButtonState XButton2 { get; init; }

    public static MouseButtonStates AllReleased => new()
    {
        Left = MouseButtonState.Released,
        Middle = MouseButtonState.Released,
        Right = MouseButtonState.Released,
        XButton1 = MouseButtonState.Released,
        XButton2 = MouseButtonState.Released,
    };

    public MouseButtonStates WithButton(MouseButton button, MouseButtonState state) => button switch
    {
        MouseButton.Left => this with { Left = state },
        MouseButton.Middle => this with { Middle = state },
        MouseButton.Right => this with { Right = state },
        MouseButton.XButton1 => this with { XButton1 = state },
        MouseButton.XButton2 => this with { XButton2 = state },
        _ => this,
    };
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
    /// Initializes a new instance with the specified routed event and default values.
    /// </summary>
    public MouseEventArgs(RoutedEvent routedEvent)
        : base(routedEvent, 0)
    {
    }

    /// <summary>
    /// Gets the position of the mouse relative to the specified element.
    /// </summary>
    public Point GetPosition(UIElement? relativeTo)
    {
        if (relativeTo == null)
            return Position;

        double offsetX = 0;
        double offsetY = 0;

        Visual? current = relativeTo;
        while (current != null)
        {
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

        return new Point(Position.X - offsetX, Position.Y - offsetY);
    }

    /// <summary>
    /// Debug helper: Gets the visual bounds chain from an element to the root.
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
    internal override void InvokeEventHandler(Delegate handler, object target)
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
public delegate void MouseEventHandler(object sender, MouseEventArgs e);

/// <summary>
/// Provides data for mouse button events.
/// </summary>
public sealed class MouseButtonEventArgs : MouseEventArgs
{
    public MouseButton ChangedButton { get; }
    public MouseButtonState ButtonState { get; }
    public int ClickCount { get; }

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
    internal override void InvokeEventHandler(Delegate handler, object target)
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
public delegate void MouseButtonEventHandler(object sender, MouseButtonEventArgs e);

/// <summary>
/// Provides data for mouse wheel events.
/// </summary>
public sealed class MouseWheelEventArgs : MouseEventArgs
{
    public int Delta { get; }

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
    internal override void InvokeEventHandler(Delegate handler, object target)
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
public delegate void MouseWheelEventHandler(object sender, MouseWheelEventArgs e);
