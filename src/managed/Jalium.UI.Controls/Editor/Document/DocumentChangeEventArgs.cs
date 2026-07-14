namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Event arguments for editor-document text changes.
/// </summary>
public sealed class DocumentChangeEventArgs : EventArgs
{
    /// <summary>
    /// Gets the document change that occurred.
    /// </summary>
    public DocumentChange Change { get; }

    /// <summary>
    /// Gets the document offset where the change occurred.
    /// </summary>
    public int Offset => Change.Offset;

    /// <summary>
    /// Gets the text that was removed.
    /// </summary>
    public string RemovedText => Change.RemovedText;

    /// <summary>
    /// Gets the text that was inserted.
    /// </summary>
    public string InsertedText => Change.InsertedText;

    public DocumentChangeEventArgs(DocumentChange change)
    {
        Change = change ?? throw new ArgumentNullException(nameof(change));
    }
}
