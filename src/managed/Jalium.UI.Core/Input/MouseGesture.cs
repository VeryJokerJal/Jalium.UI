using System.ComponentModel;

namespace Jalium.UI.Input;

/// <summary>
/// Specifies the action of the mouse for a mouse gesture.
/// </summary>
public enum MouseAction
{
    /// <summary>No action.</summary>
    None,
    /// <summary>A left mouse button click.</summary>
    LeftClick,
    /// <summary>A right mouse button click.</summary>
    RightClick,
    /// <summary>A middle mouse button click.</summary>
    MiddleClick,
    /// <summary>A mouse wheel rotation.</summary>
    WheelClick,
    /// <summary>A left mouse button double-click.</summary>
    LeftDoubleClick,
    /// <summary>A right mouse button double-click.</summary>
    RightDoubleClick,
    /// <summary>A middle mouse button double-click.</summary>
    MiddleDoubleClick
}

/// <summary>
/// Defines a mouse input gesture that can be used to invoke a command.
/// </summary>
[TypeConverter(typeof(MouseGestureConverter))]
public sealed class MouseGesture : InputGesture
{
    /// <summary>
    /// Initializes a new instance of the MouseGesture class.
    /// </summary>
    public MouseGesture() : this(MouseAction.None, ModifierKeys.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the MouseGesture class with the specified mouse action.
    /// </summary>
    /// <param name="mouseAction">The action associated with this gesture.</param>
    public MouseGesture(MouseAction mouseAction) : this(mouseAction, ModifierKeys.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the MouseGesture class with the specified action and modifiers.
    /// </summary>
    /// <param name="mouseAction">The action associated with this gesture.</param>
    /// <param name="modifiers">The modifier keys associated with this gesture.</param>
    public MouseGesture(MouseAction mouseAction, ModifierKeys modifiers)
    {
        MouseAction = mouseAction;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Initializes a compatibility instance from a numeric modifier value.
    /// </summary>
    public MouseGesture(MouseAction mouseAction, int modifiers)
        : this(mouseAction, (ModifierKeys)modifiers)
    {
    }

    /// <summary>
    /// Gets or sets the MouseAction associated with this gesture.
    /// </summary>
    public MouseAction MouseAction { get; set; }

    /// <summary>
    /// Gets or sets the modifier keys associated with this gesture.
    /// </summary>
    public ModifierKeys Modifiers { get; set; }

    /// <summary>
    /// Determines whether this MouseGesture matches the input event.
    /// </summary>
    /// <param name="targetElement">The target element.</param>
    /// <param name="inputEventArgs">The input event data.</param>
    /// <returns>true if the event data matches this MouseGesture; otherwise, false.</returns>
    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        if (inputEventArgs is not MouseEventArgs mouseEventArgs)
        {
            return false;
        }

        MouseAction action = GetMouseAction(inputEventArgs);
        return action != MouseAction.None
            && MouseAction == action
            && Modifiers == mouseEventArgs.KeyboardModifiers;
    }

    private static MouseAction GetMouseAction(InputEventArgs inputEventArgs)
    {
        if (inputEventArgs is MouseWheelEventArgs)
        {
            return MouseAction.WheelClick;
        }

        if (inputEventArgs is not MouseButtonEventArgs buttonEventArgs)
        {
            return MouseAction.None;
        }

        return (buttonEventArgs.ChangedButton, buttonEventArgs.ClickCount) switch
        {
            (MouseButton.Left, 1) => MouseAction.LeftClick,
            (MouseButton.Left, 2) => MouseAction.LeftDoubleClick,
            (MouseButton.Right, 1) => MouseAction.RightClick,
            (MouseButton.Right, 2) => MouseAction.RightDoubleClick,
            (MouseButton.Middle, 1) => MouseAction.MiddleClick,
            (MouseButton.Middle, 2) => MouseAction.MiddleDoubleClick,
            _ => MouseAction.None,
        };
    }
}

/// <summary>
/// Converts a MouseGesture to and from a string.
/// </summary>
public sealed class MouseGestureConverter : TypeConverter
{
    /// <summary>
    /// Determines whether this converter can convert from the specified source type.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <summary>
    /// Converts the specified value to a MouseGesture.
    /// </summary>
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return ParseMouseGesture(str);
        }
        return base.ConvertFrom(context, culture, value);
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string);
    }

    /// <inheritdoc />
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        System.Globalization.CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(string))
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is MouseGesture gesture)
            {
                var parts = new List<string>(5);
                if ((gesture.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
                if ((gesture.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
                if ((gesture.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
                if ((gesture.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
                parts.Add(gesture.MouseAction.ToString());
                return string.Join('+', parts);
            }
        }

        throw GetConvertToException(value, destinationType);
    }

    private static MouseGesture ParseMouseGesture(string input)
    {
        var parts = input.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = ModifierKeys.None;
        var action = MouseAction.None;

        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModifierKeys.Control;
                    break;
                case "ALT":
                    modifiers |= ModifierKeys.Alt;
                    break;
                case "SHIFT":
                    modifiers |= ModifierKeys.Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModifierKeys.Windows;
                    break;
                default:
                    if (Enum.TryParse<MouseAction>(part, true, out var parsedAction))
                    {
                        action = parsedAction;
                    }
                    else if (part.Length != 0)
                    {
                        throw new NotSupportedException($"Mouse action '{part}' is not supported.");
                    }
                    break;
            }
        }

        return new MouseGesture(action, modifiers);
    }
}
