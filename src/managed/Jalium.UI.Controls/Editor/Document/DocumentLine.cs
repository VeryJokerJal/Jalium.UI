namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Metadata for a single document line.
/// Managed by DocumentLineTree for O(log n) lookups.
/// </summary>
public sealed class DocumentLine
{
    /// <summary>
    /// Gets the 1-based line number.
    /// </summary>
    public int LineNumber { get; internal set; }

    /// <summary>
    /// Gets or sets the character offset of the line start in the document.
    /// </summary>
    public int Offset { get; internal set; }

    /// <summary>
    /// Gets or sets the length of the line content (excluding line delimiter).
    /// </summary>
    public int Length { get; internal set; }

    /// <summary>
    /// Gets or sets the length of the line delimiter (\n = 1, \r\n = 2, none = 0).
    /// </summary>
    public int DelimiterLength { get; internal set; }

    /// <summary>
    /// Gets the total length of the line including its delimiter.
    /// </summary>
    public int TotalLength => Length + DelimiterLength;

    /// <summary>
    /// Gets the offset of the end of the line content (excluding delimiter).
    /// </summary>
    public int EndOffset => Offset + Length;

    internal DocumentLine(int lineNumber, int offset, int length, int delimiterLength)
    {
        LineNumber = lineNumber;
        Offset = offset;
        Length = length;
        DelimiterLength = delimiterLength;
    }

    public override string ToString() => $"Line {LineNumber}: Offset={Offset}, Length={Length}, Delimiter={DelimiterLength}";
}
