namespace Jalium.UI;

/// <summary>
/// Specifies a text data format used by clipboard operations.
/// </summary>
public enum TextDataFormat
{
    /// <summary>ANSI text.</summary>
    Text = 0,

    /// <summary>Unicode text.</summary>
    UnicodeText = 1,

    /// <summary>Rich Text Format data.</summary>
    Rtf = 2,

    /// <summary>HTML data.</summary>
    Html = 3,

    /// <summary>Comma-separated value data.</summary>
    CommaSeparatedValue = 4,

    /// <summary>XAML data.</summary>
    Xaml = 5,
}
