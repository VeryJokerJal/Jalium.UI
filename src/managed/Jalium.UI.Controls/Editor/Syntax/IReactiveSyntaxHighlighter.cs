namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Optional extension for syntax highlighters that maintain asynchronous or document-wide state.
/// </summary>
public interface IReactiveSyntaxHighlighter : ISyntaxHighlighter
{
    /// <summary>
    /// Raised when cached highlighting becomes stale and the editor should invalidate rendering.
    /// </summary>
    event EventHandler<SyntaxHighlightInvalidatedEventArgs>? HighlightingInvalidated;

    /// <summary>
    /// Attaches this highlighter to a document.
    /// </summary>
    /// <param name="document">The attached document.</param>
    /// <param name="filePathProvider">File path provider used for project-context lookups.</param>
    void Attach(TextDocument document, Func<string?> filePathProvider);

    /// <summary>
    /// Notifies that the attached document changed.
    /// </summary>
    /// <param name="change">The document change payload.</param>
    void NotifyDocumentChanged(TextChangeEventArgs change);

    /// <summary>
    /// Detaches this highlighter and cancels any background work.
    /// </summary>
    void Detach();
}
