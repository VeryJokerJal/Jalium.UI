namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Represents a single editor-document change operation (insertion, removal, or replacement).
/// </summary>
public sealed class DocumentChange
{
    /// <summary>
    /// Gets the document offset where the change occurred.
    /// </summary>
    public int Offset { get; }

    /// <summary>
    /// Gets the text that was removed (empty string for pure insertion).
    /// </summary>
    public string RemovedText { get; }

    /// <summary>
    /// Gets the text that was inserted (empty string for pure removal).
    /// </summary>
    public string InsertedText { get; }

    /// <summary>
    /// Gets the length of the removed text.
    /// </summary>
    public int RemovalLength => RemovedText.Length;

    /// <summary>
    /// Gets the length of the inserted text.
    /// </summary>
    public int InsertionLength => InsertedText.Length;

    public DocumentChange(int offset, string removedText, string insertedText)
    {
        Offset = offset;
        RemovedText = removedText;
        InsertedText = insertedText;
    }

    /// <summary>
    /// Creates the inverse change (for undo).
    /// </summary>
    public DocumentChange CreateInverse()
    {
        return new DocumentChange(Offset, InsertedText, RemovedText);
    }
}
