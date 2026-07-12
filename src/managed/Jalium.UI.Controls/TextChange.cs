namespace Jalium.UI.Controls;

/// <summary>
/// Describes a contiguous text-content change.
/// </summary>
public class TextChange
{
    internal TextChange()
    {
    }

    /// <summary>
    /// Gets the zero-based character offset of the change.
    /// </summary>
    public int Offset { get; internal set; }

    /// <summary>
    /// Gets the number of characters added at <see cref="Offset"/>.
    /// </summary>
    public int AddedLength { get; internal set; }

    /// <summary>
    /// Gets the number of characters removed at <see cref="Offset"/>.
    /// </summary>
    public int RemovedLength { get; internal set; }
}
