using System.ComponentModel;
using System.Globalization;
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
    /// <param name="key">The key associated with this gesture.</param>
    public KeyGesture(Key key) : this(key, ModifierKeys.None)
    {
    }

    /// <summary>
    /// Initializes a compatibility instance from a numeric key code.
    /// </summary>
    public KeyGesture(int key) : this(KeyFromLegacyCode(key), ModifierKeys.None)
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyGesture class with the specified key and modifiers.
    /// </summary>
    /// <param name="key">The key associated with this gesture.</param>
    /// <param name="modifiers">The modifier keys associated with this gesture.</param>
    public KeyGesture(Key key, ModifierKeys modifiers) : this(key, modifiers, string.Empty)
    {
    }

    /// <summary>
    /// Initializes a compatibility instance from numeric key and modifier values.
    /// </summary>
    public KeyGesture(int key, int modifiers) : this(KeyFromLegacyCode(key), (ModifierKeys)modifiers, string.Empty)
    {
    }

    /// <summary>
    /// Initializes a new instance of the KeyGesture class with the specified key, modifiers, and display string.
    /// </summary>
    /// <param name="key">The key associated with this gesture.</param>
    /// <param name="modifiers">The modifier keys associated with this gesture.</param>
    /// <param name="displayString">A string representation of the KeyGesture.</param>
    public KeyGesture(Key key, ModifierKeys modifiers, string displayString)
    {
        Key = key;
        Modifiers = modifiers;
        DisplayString = displayString ?? string.Empty;
    }

    /// <summary>
    /// Initializes a compatibility instance from numeric key and modifier values.
    /// </summary>
    public KeyGesture(int key, int modifiers, string displayString)
        : this(KeyFromLegacyCode(key), (ModifierKeys)modifiers, displayString)
    {
    }

    /// <summary>
    /// Gets the key associated with this gesture.
    /// </summary>
    public Key Key { get; }

    /// <summary>
    /// Gets the modifier keys associated with this gesture.
    /// </summary>
    public ModifierKeys Modifiers { get; }

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
        return inputEventArgs is KeyEventArgs keyEventArgs
            && Key == keyEventArgs.Key
            && Modifiers == keyEventArgs.KeyboardModifiers;
    }

    /// <summary>
    /// Returns a string that represents the current KeyGesture.
    /// </summary>
    /// <returns>A string representation of this KeyGesture.</returns>
    public string GetDisplayStringForCulture(CultureInfo? culture)
    {
        if (!string.IsNullOrEmpty(DisplayString))
            return DisplayString;

        return BuildDisplayString();
    }

    private string BuildDisplayString()
    {
        var sb = new StringBuilder();

        if ((Modifiers & ModifierKeys.Control) != 0)
            sb.Append("Ctrl+");
        if ((Modifiers & ModifierKeys.Alt) != 0)
            sb.Append("Alt+");
        if ((Modifiers & ModifierKeys.Windows) != 0)
            sb.Append("Windows+");
        if ((Modifiers & ModifierKeys.Shift) != 0)
            sb.Append("Shift+");

        sb.Append(GetKeyDisplayString(Key));

        return sb.ToString();
    }

    private static string GetKeyDisplayString(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + (int)key - (int)Key.D0)).ToString();
        if (key is >= Key.A and <= Key.Z)
            return ((char)('A' + (int)key - (int)Key.A)).ToString();
        if (key is >= Key.F1 and <= Key.F24)
            return $"F{(int)key - (int)Key.F1 + 1}";

        return key switch
        {
            Key.Back => "Backspace",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Prior => "Page Up",
            Key.Next => "Page Down",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            _ => key.ToString()
        };
    }

    private static Key KeyFromLegacyCode(int keyCode)
    {
        if (keyCode is >= 0x30 and <= 0x39)
            return (Key)((int)Key.D0 + keyCode - 0x30);
        if (keyCode is >= 0x41 and <= 0x5A)
            return (Key)((int)Key.A + keyCode - 0x41);
        if (keyCode is >= 0x70 and <= 0x87)
            return (Key)((int)Key.F1 + keyCode - 0x70);

        return keyCode switch
        {
            0x08 => Key.Back,
            0x09 => Key.Tab,
            0x0D => Key.Return,
            0x1B => Key.Escape,
            0x21 => Key.Prior,
            0x22 => Key.Next,
            0x23 => Key.End,
            0x24 => Key.Home,
            0x25 => Key.Left,
            0x26 => Key.Up,
            0x27 => Key.Right,
            0x28 => Key.Down,
            0x2D => Key.Insert,
            0x2E => Key.Delete,
            _ when Enum.IsDefined(typeof(Key), keyCode) => (Key)keyCode,
            _ => Key.None,
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
    /// Determines whether a gesture from the current serialization context can be converted to a string.
    /// </summary>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            && context?.Instance is KeyGesture gesture
            && IsDefinedModifiers(gesture.Modifiers)
            && Enum.IsDefined(gesture.Key);
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
        var modifiers = ModifierKeys.None;
        var key = Key.None;

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
                    if (part.Length == 1)
                    {
                        var c = char.ToUpperInvariant(part[0]);
                        if (c >= 'A' && c <= 'Z')
                            key = (Key)((int)Key.A + c - 'A');
                        else if (c >= '0' && c <= '9')
                            key = (Key)((int)Key.D0 + c - '0');
                    }
                    else if (upperPart.StartsWith("F", StringComparison.Ordinal) && int.TryParse(upperPart[1..], out var fNum) && fNum >= 1 && fNum <= 24)
                    {
                        key = (Key)((int)Key.F1 + fNum - 1);
                    }
                    else if (TryParseKeyName(part, out Key parsedKey))
                    {
                        key = parsedKey;
                    }
                    break;
            }
        }

        return new KeyGesture(key, modifiers);
    }

    private static bool TryParseKeyName(string text, out Key key)
    {
        string normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        key = normalized.ToUpperInvariant() switch
        {
            "BACKSPACE" => Key.Back,
            "ENTER" => Key.Return,
            "ESC" => Key.Escape,
            "PAGEUP" => Key.Prior,
            "PAGEDOWN" => Key.Next,
            _ => Key.None,
        };
        return key != Key.None || Enum.TryParse(normalized, ignoreCase: true, out key);
    }

    private static bool IsDefinedModifiers(ModifierKeys modifiers)
    {
        const ModifierKeys defined =
            ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows | ModifierKeys.Shift;
        return (modifiers & ~defined) == 0;
    }
}
