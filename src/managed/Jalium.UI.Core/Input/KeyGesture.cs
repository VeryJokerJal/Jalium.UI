using System.ComponentModel;
using System.Text;

namespace Jalium.UI.Input;

/// <summary>
/// Defines a keyboard combination that can be used to invoke a command.
/// </summary>
[TypeConverter(typeof(KeyGestureConverter))]
public sealed class KeyGesture : InputGesture
{
    /// <summary>
    /// Initializes a new instance of the KeyGesture class with the specified key.
    /// </summary>
    /// <param name="key">The key associated with this gesture (as integer key code).</param>
    public KeyGesture(int key) : this(key, 0)
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyGesture class with the specified key and modifiers.
    /// </summary>
    /// <param name="key">The key associated with this gesture (as integer key code).</param>
    /// <param name="modifiers">The modifier keys associated with this gesture (as flags).</param>
    public KeyGesture(int key, int modifiers) : this(key, modifiers, string.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyGesture class with the specified key, modifiers, and display string.
    /// </summary>
    /// <param name="key">The key associated with this gesture (as integer key code).</param>
    /// <param name="modifiers">The modifier keys associated with this gesture (as flags).</param>
    /// <param name="displayString">A string representation of the KeyGesture.</param>
    public KeyGesture(int key, int modifiers, string displayString)
    {
        Key = key;
        Modifiers = modifiers;
        DisplayString = displayString ?? string.Empty;
    }

    /// <summary>
    /// Gets the key associated with this gesture (as integer key code).
    /// </summary>
    public int Key { get; }

    /// <summary>
    /// Gets the modifier keys associated with this gesture (as flags).
    /// </summary>
    public int Modifiers { get; }

    /// <summary>
    /// Gets the string representation of this KeyGesture.
    /// </summary>
    public string DisplayString { get; }

    /// <summary>
    /// Determines whether this KeyGesture matches the input event.
    /// </summary>
    /// <param name="targetElement">The target element.</param>
    /// <param name="inputEventArgs">The input event data.</param>
    /// <returns>true if the event data matches this KeyGesture; otherwise, false.</returns>
    public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
    {
        // This is a simplified implementation - full matching requires integration with the Input system
        return false;
    }

    /// <summary>
    /// Returns a string that represents the current KeyGesture.
    /// </summary>
    /// <returns>A string representation of this KeyGesture.</returns>
    public string GetDisplayStringForCulture(IFormatProvider? culture)
    {
        if (!string.IsNullOrEmpty(DisplayString))
            return DisplayString;

        return BuildDisplayString();
    }

    private string BuildDisplayString()
    {
        var sb = new StringBuilder();

        if ((Modifiers & 2) != 0) // Control
            sb.Append("Ctrl+");
        if ((Modifiers & 1) != 0) // Alt
            sb.Append("Alt+");
        if ((Modifiers & 4) != 0) // Shift
            sb.Append("Shift+");
        if ((Modifiers & 8) != 0) // Windows
            sb.Append("Win+");

        sb.Append(GetKeyDisplayString(Key));

        return sb.ToString();
    }

    private static string GetKeyDisplayString(int key)
    {
        return key switch
        {
            8 => "Backspace",
            13 => "Enter",
            27 => "Esc",
            32 => "Space",
            33 => "Page Up",
            34 => "Page Down",
            46 => "Delete",
            45 => "Insert",
            >= 48 and <= 57 => ((char)key).ToString(),
            >= 65 and <= 90 => ((char)key).ToString(),
            >= 112 and <= 123 => $"F{key - 111}",
            _ => $"Key{key}"
        };
    }
}

/// <summary>
/// Converts a KeyGesture to and from a string.
/// </summary>
public sealed class KeyGestureConverter : TypeConverter
{
    /// <summary>
    /// Determines whether this converter can convert from the specified source type.
    /// </summary>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
    {
        return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
    }

    /// <summary>
    /// Converts the specified value to a KeyGesture.
    /// </summary>
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
    {
        if (value is string str)
        {
            return ParseKeyGesture(str);
        }
        return base.ConvertFrom(context, culture, value);
    }

    /// <summary>
    /// Converts the KeyGesture to the specified destination type.
    /// </summary>
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is KeyGesture gesture)
        {
            return gesture.GetDisplayStringForCulture(culture);
        }
        return base.ConvertTo(context, culture, value, destinationType);
    }

    private static KeyGesture ParseKeyGesture(string input)
    {
        var parts = input.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = 0;
        var key = 0;

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
                    if (part.Length == 1)
                    {
                        var c = char.ToUpperInvariant(part[0]);
                        if (c >= 'A' && c <= 'Z')
                            key = c;
                        else if (c >= '0' && c <= '9')
                            key = c;
                    }
                    else if (upperPart.StartsWith("F", StringComparison.Ordinal) && int.TryParse(upperPart[1..], out var fNum) && fNum >= 1 && fNum <= 12)
                    {
                        key = 111 + fNum;
                    }
                    break;
            }
        }

        return new KeyGesture(key, modifiers);
    }
}
