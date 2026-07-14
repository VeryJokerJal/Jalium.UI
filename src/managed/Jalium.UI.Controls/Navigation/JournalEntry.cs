namespace Jalium.UI.Controls.Navigation;

/// <summary>
/// Specifies the position of an entry in Jalium's legacy combined journal view.
/// </summary>
/// <remarks>
/// This is a Jalium-specific helper. WPF does not expose an equivalent type.
/// </remarks>
public enum JournalEntryPositionType
{
    /// <summary>
    /// The entry is in the back history.
    /// </summary>
    Back,

    /// <summary>
    /// The entry represents the current position.
    /// </summary>
    Current,

    /// <summary>
    /// The entry is in the forward history.
    /// </summary>
    Forward
}
