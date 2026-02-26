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
    public MouseGesture() : this(MouseAction.None, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the MouseGesture class with the specified mouse action.
    /// </summary>
    /// <param name="mouseAction">The action associated with this gesture.</param>
    public MouseGesture(MouseAction mouseAction) : this(mouseAction, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the MouseGesture class with the specified action and modifiers.
    /// </summary>
    /// <param name="mouseAction">The action associated with this gesture.</param>
    /// <param name="modifiers">The modifier keys associated with this gesture (as flags).</param>
    public MouseGesture(MouseAction mouseAction, int modifiers)
    {
        MouseAction = mouseAction;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Gets or sets the MouseAction associated with this gesture.
    /// </summary>
    public MouseAction MouseAction { get; set; }

    /// <summary>
    /// Gets or sets the modifier keys associated with this gesture (as flags).
    /// </summary>
    public int Modifiers { get; set; }

    /// <summary>
    /// Determines whether this MouseGesture matches the input event.
    /// </summary>
    /// <param name="targetElement">The target element.</param>
    /// <param name="inputEventArgs">The input event data.</param>
    /// <returns>true if the event data matches this MouseGesture; otherwise, false.</returns>
    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        // Simplified implementation - full matching requires integration with the Input system
        return false;
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

    private static MouseGesture ParseMouseGesture(string input)
    {
        var parts = input.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = 0;
        var action = MouseAction.None;

        foreach (var part in parts)
        {
            var upperPart = part.ToUpperInvariant();
            switch (upperPart)
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= 2;
                    break;
                case "ALT":
                    modifiers |= 1;
                    break;
                case "SHIFT":
                    modifiers |= 4;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= 8;
                    break;
                default:
                    if (Enum.TryParse<MouseAction>(part, true, out var parsedAction))
                    {
                        action = parsedAction;
                    }
                    break;
            }
        }

        return new MouseGesture(action, modifiers);
    }
}
