namespace Jalium.UI.Controls.Editor;

/// <summary>
/// An immutable snapshot of a TextDocument at a point in time.
/// Shares the Rope tree with the document (O(1) creation).
/// Safe to read from background threads for syntax analysis.
/// </summary>
public sealed class TextDocumentSnapshot
{
    private readonly Rope _rope;
    private readonly DocumentLine[] _lines;

    /// <summary>
    /// Gets the document version when this snapshot was created.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the total length of the document text.
    /// </summary>
    public int TextLength => _rope.Length;

    /// <summary>
    /// Gets the number of lines.
    /// </summary>
    public int LineCount => _lines.Length;

    internal TextDocumentSnapshot(Rope rope, DocumentLine[] lines, int version)
    {
        _rope = rope;
        _lines = lines;
        Version = version;
    }

    /// <summary>
    /// Gets the character at the specified offset.
    /// </summary>
    public char GetCharAt(int offset) => _rope[offset];

    /// <summary>
    /// Gets a substring of the document.
    /// </summary>
    public string GetText(int offset, int length) => _rope.ToString(offset, length);

    /// <summary>
    /// Gets the text of a specific line (excluding delimiter).
    /// </summary>
    public string GetLineText(int lineNumber)
    {
        var line = GetLineByNumber(lineNumber);
        return line.Length > 0 ? _rope.ToString(line.Offset, line.Length) : string.Empty;
    }

    /// <summary>
    /// Gets the start offset of a line.
    /// </summary>
    public int GetLineStartOffset(int lineNumber) => GetLineByNumber(lineNumber).Offset;

    /// <summary>
    /// Gets the length of a line (excluding delimiter).
    /// </summary>
    public int GetLineLength(int lineNumber) => GetLineByNumber(lineNumber).Length;

    /// <summary>
    /// Returns the full document text.
    /// </summary>
    public override string ToString() => _rope.ToString();

    private DocumentLine GetLineByNumber(int lineNumber)
    {
        int index = lineNumber - 1;
        if (index < 0 || index >= _lines.Length)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), $"Line number {lineNumber} is out of range [1, {_lines.Length}]");

        return _lines[index];
    }
}
