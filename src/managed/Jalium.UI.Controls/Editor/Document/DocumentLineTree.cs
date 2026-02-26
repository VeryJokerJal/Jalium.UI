namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Maintains line metadata with O(log n) lookup by offset or line number.
/// Uses a simple list internally (sufficient for most document sizes).
/// Can be upgraded to a red-black tree for very large documents.
/// </summary>
internal sealed class DocumentLineTree
{
    private readonly List<DocumentLine> _lines = [];

    /// <summary>
    /// Gets the number of lines.
    /// </summary>
    public int LineCount => _lines.Count;

    /// <summary>
    /// Rebuilds the line tree from the document text.
    /// </summary>
    public void Rebuild(Rope rope)
    {
        _lines.Clear();

        if (rope.Length == 0)
        {
            _lines.Add(new DocumentLine(1, 0, 0, 0));
            return;
        }

        var text = rope.ToString();
        int lineNumber = 1;
        int lineStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int delimiterLength = 1;
                int lineLength = i - lineStart;

                // Check for \r\n
                if (lineLength > 0 && text[i - 1] == '\r')
                {
                    lineLength--;
                    delimiterLength = 2;
                }

                _lines.Add(new DocumentLine(lineNumber, lineStart, lineLength, delimiterLength));
                lineNumber++;
                lineStart = i + 1;
            }
        }

        // Add the last line (even if it doesn't end with \n)
        _lines.Add(new DocumentLine(lineNumber, lineStart, text.Length - lineStart, 0));
    }

    /// <summary>
    /// Gets a line by its 1-based line number.
    /// </summary>
    public DocumentLine GetByNumber(int lineNumber)
    {
        int index = lineNumber - 1;
        if (index < 0 || index >= _lines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), $"Line number {lineNumber} is out of range [1, {_lines.Count}]");
        return _lines[index];
    }

    /// <summary>
    /// Gets the line containing the specified document offset.
    /// </summary>
    public DocumentLine GetByOffset(int offset)
    {
        if (_lines.Count == 0)
            throw new InvalidOperationException("Document has no lines");

        // Binary search for the line containing the offset
        int lo = 0, hi = _lines.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            var line = _lines[mid];
            if (offset < line.Offset)
            {
                hi = mid - 1;
            }
            else if (offset >= line.Offset + line.TotalLength)
            {
                lo = mid + 1;
            }
            else
            {
                return line;
            }
        }

        return _lines[lo];
    }

    /// <summary>
    /// Updates line metadata after a text change.
    /// For simplicity, this rebuilds affected lines.
    /// </summary>
    public void UpdateAfterChange(Rope rope, int changeOffset, int removedLength, int insertedLength)
    {
        // For now, rebuild entirely. A more sophisticated implementation would
        // only update affected lines and adjust offsets of subsequent lines.
        Rebuild(rope);
    }

    /// <summary>
    /// Creates an immutable copy of the current line metadata.
    /// </summary>
    internal DocumentLine[] CreateSnapshot()
    {
        var snapshot = new DocumentLine[_lines.Count];
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            snapshot[i] = new DocumentLine(line.LineNumber, line.Offset, line.Length, line.DelimiterLength);
        }

        return snapshot;
    }
}
