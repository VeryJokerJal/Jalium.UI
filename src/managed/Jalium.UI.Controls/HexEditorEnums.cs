namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the display format for hex data in a <see cref="HexEditor"/>.
/// </summary>
public enum HexDisplayFormat
{
    /// <summary>
    /// Display data as individual bytes (8-bit).
    /// </summary>
    Byte,

    /// <summary>
    /// Display data as 16-bit words.
    /// </summary>
    Word16,

    /// <summary>
    /// Display data as 32-bit double words.
    /// </summary>
    DWord32,

    /// <summary>
    /// Display data as 64-bit quad words.
    /// </summary>
    QWord64
}

/// <summary>
/// Specifies the byte order for multi-byte display formats.
/// </summary>
public enum Endianness
{
    /// <summary>
    /// Little-endian byte order (least significant byte first).
    /// </summary>
    Little,

    /// <summary>
    /// Big-endian byte order (most significant byte first).
    /// </summary>
    Big
}
