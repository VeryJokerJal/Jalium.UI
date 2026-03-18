namespace Jalium.UI.Input;

/// <summary>
/// Enumeration of modifier keys.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>
/// Enumeration of keyboard keys.
/// </summary>
public enum Key
{
    None = 0,
    Back = 8,
    Tab = 9,
    Enter = 13,
    Shift = 16,
    Ctrl = 17,
    Alt = 18,
    Pause = 19,
    CapsLock = 20,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Insert = 45,
    Delete = 46,

    // Numbers
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Letters
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Numpad
    NumPad0 = 96, NumPad1, NumPad2, NumPad3, NumPad4,
    NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Multiply = 106,
    Add = 107,
    Subtract = 109,
    Decimal = 110,
    Divide = 111,

    // Function keys
    F1 = 112, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // OEM keys
    OemSemicolon = 186,
    OemPlus = 187,
    OemComma = 188,
    OemMinus = 189,
    OemPeriod = 190,
    OemQuestion = 191,
    OemTilde = 192,
    OemOpenBrackets = 219,
    OemPipe = 220,
    OemCloseBrackets = 221,
    OemQuotes = 222
}
