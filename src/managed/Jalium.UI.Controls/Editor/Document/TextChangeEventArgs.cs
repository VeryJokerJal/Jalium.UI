namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Event arguments for document text changes.
/// </summary>
public sealed class TextChangeEventArgs : EventArgs
{
    /// <summary>
    /// Gets the text change that occurred.
    /// </summary>
    public TextChange Change { get; }

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

    public TextChangeEventArgs(TextChange change)
    {
        Change = change;
    }
}
