namespace Jalium.UI.Controls.Editor;

/// <summary>
/// The document model for the Edit control.
/// Owns a Rope buffer, line tree, and undo stack.
/// Thread-affine: must only be modified from the UI thread.
/// </summary>
public sealed class TextDocument
{
    private Rope _rope;
    private readonly DocumentLineTree _lineTree = new();
    private int _version;
    private int _updateLevel;
    private List<TextChange>? _batchChanges;
    private int _batchStartLineCount;

    /// <summary>
    /// Gets the undo/redo stack.
    /// </summary>
    public UndoStack UndoStack { get; } = new();

    /// <summary>
    /// Gets the total length of the document text.
    /// </summary>
    public int TextLength => _rope.Length;

    /// <summary>
    /// Gets the number of lines in the document.
    /// </summary>
    public int LineCount => _lineTree.LineCount;

    /// <summary>
    /// Gets the current document version (incremented on each change).
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// Gets or sets the full document text.
    /// Setting this replaces all text and clears undo history.
    /// </summary>
    public string Text
    {
        get => _rope.ToString();
        set
        {
            var oldText = _rope.ToString();
            _rope = Rope.FromString(value ?? string.Empty);
            _lineTree.Rebuild(_rope);
            _version++;
            UndoStack.Clear();
            Changed?.Invoke(this, new TextChangeEventArgs(
                new TextChange(0, oldText, value ?? string.Empty)));
        }
    }

    /// <summary>
    /// Occurs before a text change is applied.
    /// </summary>
    public event EventHandler<TextChangeEventArgs>? Changing;

    /// <summary>
    /// Occurs after a text change is applied.
    /// </summary>
    public event EventHandler<TextChangeEventArgs>? Changed;

    /// <summary>
    /// Occurs when the line count changes.
    /// </summary>
    public event EventHandler? LineCountChanged;

    public TextDocument() : this(string.Empty) { }

    public TextDocument(string text)
    {
        _rope = Rope.FromString(text ?? string.Empty);
        _lineTree.Rebuild(_rope);
    }

    /// <summary>
    /// Gets a line by its 1-based line number.
    /// </summary>
    public DocumentLine GetLineByNumber(int lineNumber)
    {
        return _lineTree.GetByNumber(lineNumber);
    }

    /// <summary>
    /// Gets the line containing the specified offset.
    /// </summary>
    public DocumentLine GetLineByOffset(int offset)
    {
        return _lineTree.GetByOffset(offset);
    }

    /// <summary>
    /// Gets a substring of the document text.
    /// </summary>
    public string GetText(int offset, int length)
    {
        return _rope.ToString(offset, length);
    }

    /// <summary>
    /// Gets the character at the specified offset.
    /// </summary>
    public char GetCharAt(int offset)
    {
        return _rope[offset];
    }

    /// <summary>
    /// Gets the text of a specific line (excluding delimiter).
    /// </summary>
    public string GetLineText(int lineNumber)
    {
        var line = GetLineByNumber(lineNumber);
        return line.Length > 0 ? _rope.ToString(line.Offset, line.Length) : string.Empty;
    }

    /// <summary>
    /// Inserts text at the specified offset.
    /// </summary>
    public void Insert(int offset, string text)
    {
        Replace(offset, 0, text);
    }

    /// <summary>
    /// Removes text starting at the specified offset.
    /// </summary>
    public void Remove(int offset, int length)
    {
        Replace(offset, length, string.Empty);
    }

    /// <summary>
    /// Replaces a range of text with new text.
    /// </summary>
    public void Replace(int offset, int length, string newText)
    {
        if (offset < 0 || length < 0 || offset + length > TextLength)
            throw new ArgumentOutOfRangeException();

        var removedText = length > 0 ? _rope.ToString(offset, length) : string.Empty;
        var change = new TextChange(offset, removedText, newText);

        ApplyChange(change, pushToUndo: true);
    }

    /// <summary>
    /// Begins a batch update. Changes are collected and fired as a single event at EndUpdate.
    /// </summary>
    public void BeginUpdate()
    {
        if (_updateLevel == 0)
        {
            _batchChanges = [];
            _batchStartLineCount = LineCount;
            UndoStack.StartMergeGroup();
        }
        _updateLevel++;
    }

    /// <summary>
    /// Ends a batch update and fires collected change events.
    /// </summary>
    public void EndUpdate()
    {
        if (_updateLevel <= 0)
            throw new InvalidOperationException("EndUpdate called without a matching BeginUpdate.");

        _updateLevel--;
        if (_updateLevel != 0)
            return;

        UndoStack.EndMergeGroup();

        if (_batchChanges is { Count: > 0 } batchChanges)
        {
            Changed?.Invoke(this, new TextChangeEventArgs(CreateBatchChange(batchChanges)));
        }

        if (LineCount != _batchStartLineCount)
            LineCountChanged?.Invoke(this, EventArgs.Empty);

        _batchChanges = null;
    }

    /// <summary>
    /// Creates an immutable snapshot of the document for background analysis.
    /// O(1) operation — shares the Rope root.
    /// </summary>
    public TextDocumentSnapshot CreateSnapshot()
    {
        return new TextDocumentSnapshot(_rope, _lineTree.CreateSnapshot(), _version);
    }

    private void ApplyChange(TextChange change, bool pushToUndo)
    {
        int oldLineCount = LineCount;

        Changing?.Invoke(this, new TextChangeEventArgs(change));

        // Apply to rope
        if (change.RemovalLength > 0)
            _rope = _rope.Remove(change.Offset, change.RemovalLength);
        if (change.InsertionLength > 0)
            _rope = _rope.Insert(change.Offset, change.InsertedText);

        // Update line tree
        _lineTree.UpdateAfterChange(_rope, change.Offset, change.RemovalLength, change.InsertionLength);

        _version++;

        // Push to undo stack
        if (pushToUndo)
            UndoStack.PushChange(change);

        // Fire events
        if (_updateLevel > 0)
        {
            _batchChanges?.Add(change);
            return;
        }

        Changed?.Invoke(this, new TextChangeEventArgs(change));

        if (LineCount != oldLineCount)
            LineCountChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a change without pushing to undo stack (used by UndoStack.Undo/Redo).
    /// </summary>
    internal void ApplyChangeInternal(TextChange change)
    {
        ApplyChange(change, pushToUndo: false);
    }

    private static TextChange CreateBatchChange(IReadOnlyList<TextChange> batchChanges)
    {
        if (batchChanges.Count == 1)
            return batchChanges[0];

        int offset = batchChanges[0].Offset;
        var removedBuilder = new System.Text.StringBuilder();
        var insertedBuilder = new System.Text.StringBuilder();

        for (int i = 0; i < batchChanges.Count; i++)
        {
            var change = batchChanges[i];
            if (change.Offset < offset)
                offset = change.Offset;

            removedBuilder.Append(change.RemovedText);
            insertedBuilder.Append(change.InsertedText);
        }

        return new TextChange(offset, removedBuilder.ToString(), insertedBuilder.ToString());
    }
}
