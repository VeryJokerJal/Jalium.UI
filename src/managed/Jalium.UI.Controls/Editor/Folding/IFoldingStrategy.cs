namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Interface for strategies that detect foldable regions in a document.
/// </summary>
public interface IFoldingStrategy
{
    /// <summary>
    /// Detects foldable regions in the specified document.
    /// </summary>
    IEnumerable<FoldingSection> CreateFoldings(TextDocument document);
}
