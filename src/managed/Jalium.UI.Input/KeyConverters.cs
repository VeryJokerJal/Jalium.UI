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

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            && context?.Instance is Key key
            && Enum.IsDefined(key);
    }

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
    private const ModifierKeys DefinedModifiers =
        ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows | ModifierKeys.Shift;

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
    {
        return destinationType == typeof(string)
            && context?.Instance is ModifierKeys modifiers
            && IsDefinedModifierKeys(modifiers);
    }

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
                else if (trimmed.Length != 0)
                    throw new NotSupportedException($"Modifier key '{trimmed}' is not supported.");
            }
            return result;
        }
        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType != typeof(string) || value is not ModifierKeys modifiers)
        {
            return base.ConvertTo(context, culture, value, destinationType);
        }

        if (!IsDefinedModifierKeys(modifiers))
        {
            throw new InvalidEnumArgumentException(nameof(value), (int)modifiers, typeof(ModifierKeys));
        }

        var parts = new List<string>(4);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Windows");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        return string.Join('+', parts);
    }

    /// <summary>
    /// Determines whether a modifier value contains only defined flags.
    /// </summary>
    public static bool IsDefinedModifierKeys(ModifierKeys modifierKeys) =>
        (modifierKeys & ~DefinedModifiers) == 0;
}

/// <summary>
/// Converts between WPF Key enum and Win32 virtual key codes.
/// </summary>
public static class KeyInterop
{
    /// <summary>
    /// Converts a WPF-compatible logical <see cref="Key"/> value to a Win32
    /// virtual-key code. Logical key values are deliberately not virtual-key
    /// codes; keeping this translation explicit is also what makes the Linux
    /// and Android platform shims consume the same normalized codes.
    /// </summary>
    public static int VirtualKeyFromKey(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
            return 0x30 + (int)key - (int)Key.D0;
        if (key is >= Key.A and <= Key.Z)
            return 0x41 + (int)key - (int)Key.A;
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return 0x60 + (int)key - (int)Key.NumPad0;
        if (key is >= Key.F1 and <= Key.F24)
            return 0x70 + (int)key - (int)Key.F1;
        if (key is >= Key.BrowserBack and <= Key.LaunchApplication2)
            return 0xA6 + (int)key - (int)Key.BrowserBack;

        return key switch
        {
            Key.Cancel => 0x03,
            Key.Back => 0x08,
            Key.Tab => 0x09,
            Key.Clear => 0x0C,
            Key.Return => 0x0D,
            Key.Pause => 0x13,
            Key.Capital => 0x14,
            Key.KanaMode => 0x15,
            Key.JunjaMode => 0x17,
            Key.FinalMode => 0x18,
            Key.HanjaMode => 0x19,
            Key.Escape => 0x1B,
            Key.ImeConvert => 0x1C,
            Key.ImeNonConvert => 0x1D,
            Key.ImeAccept => 0x1E,
            Key.ImeModeChange => 0x1F,
            Key.Space => 0x20,
            Key.Prior => 0x21,
            Key.Next => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Select => 0x29,
            Key.Print => 0x2A,
            Key.Execute => 0x2B,
            Key.Snapshot => 0x2C,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Help => 0x2F,
            Key.LWin => 0x5B,
            Key.RWin => 0x5C,
            Key.Apps => 0x5D,
            Key.Sleep => 0x5F,
            Key.Multiply => 0x6A,
            Key.Add => 0x6B,
            Key.Separator => 0x6C,
            Key.Subtract => 0x6D,
            Key.Decimal => 0x6E,
            Key.Divide => 0x6F,
            Key.NumLock => 0x90,
            Key.Scroll => 0x91,
            Key.LeftShift => 0xA0,
            Key.RightShift => 0xA1,
            Key.LeftCtrl => 0xA2,
            Key.RightCtrl => 0xA3,
            Key.LeftAlt => 0xA4,
            Key.RightAlt => 0xA5,
            Key.Oem1 => 0xBA,
            Key.OemPlus => 0xBB,
            Key.OemComma => 0xBC,
            Key.OemMinus => 0xBD,
            Key.OemPeriod => 0xBE,
            Key.Oem2 => 0xBF,
            Key.Oem3 => 0xC0,
            Key.AbntC1 => 0xC1,
            Key.AbntC2 => 0xC2,
            Key.Oem4 => 0xDB,
            Key.Oem5 => 0xDC,
            Key.Oem6 => 0xDD,
            Key.Oem7 => 0xDE,
            Key.Oem8 => 0xDF,
            Key.Oem102 => 0xE2,
            Key.ImeProcessed => 0xE5,
            Key.OemAttn => 0xF0,
            Key.OemFinish => 0xF1,
            Key.OemCopy => 0xF2,
            Key.OemAuto => 0xF3,
            Key.OemEnlw => 0xF4,
            Key.OemBackTab => 0xF5,
            Key.Attn => 0xF6,
            Key.CrSel => 0xF7,
            Key.ExSel => 0xF8,
            Key.EraseEof => 0xF9,
            Key.Play => 0xFA,
            Key.Zoom => 0xFB,
            Key.NoName => 0xFC,
            Key.Pa1 => 0xFD,
            Key.OemClear => 0xFE,
            _ => 0,
        };
    }

    /// <summary>Converts a Win32 virtual-key code to its logical key.</summary>
    public static Key KeyFromVirtualKey(int virtualKey)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
            return (Key)((int)Key.D0 + virtualKey - 0x30);
        if (virtualKey is >= 0x41 and <= 0x5A)
            return (Key)((int)Key.A + virtualKey - 0x41);
        if (virtualKey is >= 0x60 and <= 0x69)
            return (Key)((int)Key.NumPad0 + virtualKey - 0x60);
        if (virtualKey is >= 0x70 and <= 0x87)
            return (Key)((int)Key.F1 + virtualKey - 0x70);
        if (virtualKey is >= 0xA6 and <= 0xB7)
            return (Key)((int)Key.BrowserBack + virtualKey - 0xA6);

        return virtualKey switch
        {
            0x03 => Key.Cancel,
            0x08 => Key.Back,
            0x09 => Key.Tab,
            0x0C => Key.Clear,
            0x0D => Key.Return,
            0x13 => Key.Pause,
            0x14 => Key.Capital,
            0x15 => Key.KanaMode,
            0x17 => Key.JunjaMode,
            0x18 => Key.FinalMode,
            0x19 => Key.HanjaMode,
            0x1B => Key.Escape,
            0x1C => Key.ImeConvert,
            0x1D => Key.ImeNonConvert,
            0x1E => Key.ImeAccept,
            0x1F => Key.ImeModeChange,
            0x20 => Key.Space,
            0x21 => Key.Prior,
            0x22 => Key.Next,
            0x23 => Key.End,
            0x24 => Key.Home,
            0x25 => Key.Left,
            0x26 => Key.Up,
            0x27 => Key.Right,
            0x28 => Key.Down,
            0x29 => Key.Select,
            0x2A => Key.Print,
            0x2B => Key.Execute,
            0x2C => Key.Snapshot,
            0x2D => Key.Insert,
            0x2E => Key.Delete,
            0x2F => Key.Help,
            0x5B => Key.LWin,
            0x5C => Key.RWin,
            0x5D => Key.Apps,
            0x5F => Key.Sleep,
            0x6A => Key.Multiply,
            0x6B => Key.Add,
            0x6C => Key.Separator,
            0x6D => Key.Subtract,
            0x6E => Key.Decimal,
            0x6F => Key.Divide,
            0x90 => Key.NumLock,
            0x91 => Key.Scroll,
            0x10 or 0xA0 => Key.LeftShift,
            0xA1 => Key.RightShift,
            0x11 or 0xA2 => Key.LeftCtrl,
            0xA3 => Key.RightCtrl,
            0x12 or 0xA4 => Key.LeftAlt,
            0xA5 => Key.RightAlt,
            0xBA => Key.Oem1,
            0xBB => Key.OemPlus,
            0xBC => Key.OemComma,
            0xBD => Key.OemMinus,
            0xBE => Key.OemPeriod,
            0xBF => Key.Oem2,
            0xC0 => Key.Oem3,
            0xC1 => Key.AbntC1,
            0xC2 => Key.AbntC2,
            0xDB => Key.Oem4,
            0xDC => Key.Oem5,
            0xDD => Key.Oem6,
            0xDE => Key.Oem7,
            0xDF => Key.Oem8,
            0xE2 => Key.Oem102,
            0xE5 => Key.ImeProcessed,
            0xF0 => Key.OemAttn,
            0xF1 => Key.OemFinish,
            0xF2 => Key.OemCopy,
            0xF3 => Key.OemAuto,
            0xF4 => Key.OemEnlw,
            0xF5 => Key.OemBackTab,
            0xF6 => Key.Attn,
            0xF7 => Key.CrSel,
            0xF8 => Key.ExSel,
            0xF9 => Key.EraseEof,
            0xFA => Key.Play,
            0xFB => Key.Zoom,
            0xFC => Key.NoName,
            0xFD => Key.Pa1,
            0xFE => Key.OemClear,
            _ => Key.None,
        };
    }
}
