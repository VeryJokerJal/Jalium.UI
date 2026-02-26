namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Undo/redo stack for text document changes.
/// Supports merging consecutive character insertions into a single undo step.
/// </summary>
public sealed class UndoStack
{
    private readonly List<UndoGroup> _undoStack = [];
    private readonly List<UndoGroup> _redoStack = [];
    private UndoGroup? _currentMergeGroup;
    private int _mergeGroupNestingLevel;
    private DateTime _lastPushUtc;

    /// <summary>
    /// Gets or sets the maximum number of undo steps to keep.
    /// </summary>
    public int UndoLimit { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the typing merge time window in milliseconds.
    /// Sequential edits outside this window are not auto-merged.
    /// </summary>
    public int MergeTypingWindowMs { get; set; } = 250;

    /// <summary>
    /// Gets whether there are any changes to undo.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets whether there are any changes to redo.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets the number of undo groups currently available.
    /// </summary>
    public int UndoCount => _undoStack.Count;

    /// <summary>
    /// Gets the number of redo groups currently available.
    /// </summary>
    public int RedoCount => _redoStack.Count;

    /// <summary>
    /// Occurs when the undo/redo state changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Records a text change for undo.
    /// </summary>
    internal void PushChange(TextChange change)
    {
        var now = DateTime.UtcNow;
        if (_currentMergeGroup != null)
        {
            _currentMergeGroup.Changes.Add(change);
        }
        else
        {
            // Try to merge with the last undo group for consecutive single-char typing
            if (_undoStack.Count > 0 && TryMergeWithLast(change, now))
            {
                _lastPushUtc = now;
                return;
            }

            _undoStack.Add(new UndoGroup { Changes = [change] });
        }

        _lastPushUtc = now;

        // Clear redo stack on new changes
        _redoStack.Clear();

        EnforceUndoLimit();

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool TryMergeWithLast(TextChange change, DateTime now)
    {
        if (MergeTypingWindowMs > 0 && (now - _lastPushUtc).TotalMilliseconds > MergeTypingWindowMs)
            return false;

        var last = _undoStack[^1];
        if (last.Changes.Count != 1) return false;

        var lastChange = last.Changes[0];

        // Merge consecutive single-character insertions at adjacent positions
        if (change.RemovalLength == 0 && lastChange.RemovalLength == 0 &&
            change.InsertionLength == 1 && lastChange.InsertionLength > 0 &&
            change.Offset == lastChange.Offset + lastChange.InsertionLength)
        {
            // Don't merge after whitespace (natural word boundary)
            if (lastChange.InsertedText.Length > 0 && char.IsWhiteSpace(lastChange.InsertedText[^1]))
                return false;

            // Merge: combine the inserted text
            last.Changes[0] = new TextChange(
                lastChange.Offset,
                lastChange.RemovedText,
                lastChange.InsertedText + change.InsertedText);
            return true;
        }

        // Merge consecutive single-character deletions (backspace)
        if (change.InsertionLength == 0 && lastChange.InsertionLength == 0 &&
            change.RemovalLength == 1 && lastChange.RemovalLength > 0 &&
            change.Offset == lastChange.Offset - 1)
        {
            last.Changes[0] = new TextChange(
                change.Offset,
                change.RemovedText + lastChange.RemovedText,
                string.Empty);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Starts a merge group. All changes within the group become a single undo step.
    /// </summary>
    public void StartMergeGroup()
    {
        if (_mergeGroupNestingLevel == 0)
        {
            _currentMergeGroup = new UndoGroup();
        }
        _mergeGroupNestingLevel++;
    }

    /// <summary>
    /// Ends a merge group.
    /// </summary>
    public void EndMergeGroup()
    {
        if (_mergeGroupNestingLevel <= 0)
            throw new InvalidOperationException("EndMergeGroup called without a matching StartMergeGroup.");

        _mergeGroupNestingLevel--;
        if (_mergeGroupNestingLevel == 0 && _currentMergeGroup != null)
        {
            if (_currentMergeGroup.Changes.Count > 0)
            {
                _undoStack.Add(_currentMergeGroup);
                _redoStack.Clear();
                EnforceUndoLimit();
            }
            _currentMergeGroup = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Undoes the last change group.
    /// </summary>
    public void Undo(TextDocument document)
    {
        if (!CanUndo) return;

        var group = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        // Undo must apply inverse operations in reverse order.
        for (int i = group.Changes.Count - 1; i >= 0; i--)
        {
            document.ApplyChangeInternal(group.Changes[i].CreateInverse());
        }

        // Redo replays the original operations in original order.
        _redoStack.Add(group.Clone());
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Redoes the last undone change group.
    /// </summary>
    public void Redo(TextDocument document)
    {
        if (!CanRedo) return;

        var group = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        for (int i = 0; i < group.Changes.Count; i++)
        {
            document.ApplyChangeInternal(group.Changes[i]);
        }

        _undoStack.Add(group.Clone());
        EnforceUndoLimit();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Clears all undo/redo history.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _currentMergeGroup = null;
        _mergeGroupNestingLevel = 0;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnforceUndoLimit()
    {
        while (_undoStack.Count > UndoLimit && UndoLimit > 0)
            _undoStack.RemoveAt(0);
    }

    private sealed class UndoGroup
    {
        public List<TextChange> Changes { get; init; } = [];

        public UndoGroup Clone()
        {
            return new UndoGroup { Changes = [..Changes] };
        }
    }
}
