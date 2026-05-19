namespace Jalium.UI.Controls.QR;

/// <summary>
/// QR Code error correction levels per ISO/IEC 18004.
/// </summary>
public enum QRErrorCorrectionLevel
{
    /// <summary>~7% recoverable.</summary>
    L = 0,
    /// <summary>~15% recoverable.</summary>
    M = 1,
    /// <summary>~25% recoverable.</summary>
    Q = 2,
    /// <summary>~30% recoverable.</summary>
    H = 3
}

/// <summary>
/// QR Code data encoding mode per ISO/IEC 18004.
/// </summary>
public enum QRMode
{
    Numeric = 1,
    Alphanumeric = 2,
    Byte = 4,
    Kanji = 8
}

/// <summary>
/// Byte-stream encoding for the Byte mode.
/// </summary>
public enum QRByteEncoding
{
    /// <summary>Pick ISO-8859-1 if the entire input is in its range, otherwise UTF-8 with ECI.</summary>
    Auto,
    /// <summary>Force ISO-8859-1 (Latin-1); throws if a code point is out of range.</summary>
    Iso8859_1,
    /// <summary>Force UTF-8 with ECI 26 prefix.</summary>
    Utf8,
    /// <summary>Force Shift-JIS encoding (used by Kanji mode segments).</summary>
    ShiftJis
}
