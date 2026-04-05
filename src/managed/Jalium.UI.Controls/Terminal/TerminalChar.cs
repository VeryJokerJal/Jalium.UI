using Jalium.UI.Media;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// Represents a single character cell in the terminal buffer.
/// </summary>
internal struct TerminalChar
{
    /// <summary>
    /// The Unicode character displayed in this cell.
    /// </summary>
    public char Character;

    /// <summary>
    /// The foreground color index (0-255 for ANSI palette, or -1 for default).
    /// </summary>
    public int ForegroundIndex;

    /// <summary>
    /// The background color index (0-255 for ANSI palette, or -1 for default).
    /// </summary>
    public int BackgroundIndex;

    /// <summary>
    /// True-color foreground (used when 24-bit color sequences are received).
    /// Null means use the palette index instead.
    /// </summary>
    public Color? ForegroundRgb;

    /// <summary>
    /// True-color background (used when 24-bit color sequences are received).
    /// Null means use the palette index instead.
    /// </summary>
    public Color? BackgroundRgb;

    /// <summary>
    /// Character attribute flags (bold, italic, underline, etc.).
    /// </summary>
    public CharAttributes Attributes;

    /// <summary>
    /// Creates a blank cell with default colors.
    /// </summary>
    public static TerminalChar Blank => new()
    {
        Character = ' ',
        ForegroundIndex = -1,
        BackgroundIndex = -1,
        ForegroundRgb = null,
        BackgroundRgb = null,
        Attributes = CharAttributes.None
    };

    /// <summary>
    /// Creates a cell with the specified character and default colors.
    /// </summary>
    public static TerminalChar FromChar(char c) => new()
    {
        Character = c,
        ForegroundIndex = -1,
        BackgroundIndex = -1,
        ForegroundRgb = null,
        BackgroundRgb = null,
        Attributes = CharAttributes.None
    };
}

/// <summary>
/// Character rendering attributes.
/// </summary>
[Flags]
internal enum CharAttributes : byte
{
    /// <summary>No special attributes.</summary>
    None = 0,

    /// <summary>Bold / bright text.</summary>
    Bold = 1 << 0,

    /// <summary>Dim / faint text.</summary>
    Dim = 1 << 1,

    /// <summary>Italic text.</summary>
    Italic = 1 << 2,

    /// <summary>Underlined text.</summary>
    Underline = 1 << 3,

    /// <summary>Blinking text (rendered as steady in this implementation).</summary>
    Blink = 1 << 4,

    /// <summary>Foreground and background colors are swapped.</summary>
    Inverse = 1 << 5,

    /// <summary>Hidden / invisible text.</summary>
    Hidden = 1 << 6,

    /// <summary>Strikethrough text.</summary>
    Strikethrough = 1 << 7
}
