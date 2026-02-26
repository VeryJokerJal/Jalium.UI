namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how SwipeItem objects are displayed.
/// </summary>
public enum SwipeMode
{
    /// <summary>
    /// Swipe items are revealed behind the content.
    /// </summary>
    Reveal,

    /// <summary>
    /// A single swipe item fills the space and is invoked when a threshold is reached.
    /// </summary>
    Execute
}

/// <summary>
/// Represents a collection of SwipeItem objects.
/// </summary>
public sealed class SwipeItems : List<SwipeItem>
{
    /// <summary>
    /// Gets or sets the behavior of the swipe items.
    /// </summary>
    public SwipeMode Mode { get; set; } = SwipeMode.Reveal;
}
