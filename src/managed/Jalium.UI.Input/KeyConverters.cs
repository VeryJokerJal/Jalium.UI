using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Input;

/// <summary>
/// Converts a Key value to and from a string.
/// </summary>
public sealed class KeyConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s && Enum.TryParse<Key>(s.Trim(), true, out var key))
            return key;
        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is Key key)
            return key.ToString();
        return base.ConvertTo(context, culture, value, destinationType);
    }
}

/// <summary>
/// Converts ModifierKeys to and from a string.
/// </summary>
public sealed class ModifierKeysConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string s)
        {
            var result = ModifierKeys.None;
            foreach (var part in s.Split('+'))
            {
                var trimmed = part.Trim();
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Control;
                else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Alt;
                else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Shift;
                else if (trimmed.Equals("Windows", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Windows;
            }
            return result;
        }
        return base.ConvertFrom(context, culture, value);
    }
}

/// <summary>
/// Converts between WPF Key enum and Win32 virtual key codes.
/// </summary>
public static class KeyInterop
{
    public static int VirtualKeyFromKey(Key key) => (int)key;
    public static Key KeyFromVirtualKey(int virtualKey) => (Key)virtualKey;
}
