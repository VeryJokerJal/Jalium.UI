namespace Jalium.UI.Documents;

/// <summary>Provides a common base for positions in document content.</summary>
public abstract class ContentPosition
{
    /// <summary>Represents content for which no position can be determined.</summary>
    public static readonly ContentPosition Missing = new MissingContentPosition();

    protected ContentPosition()
    {
    }

    private sealed class MissingContentPosition : ContentPosition
    {
    }
}
