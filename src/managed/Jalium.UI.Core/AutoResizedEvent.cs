namespace Jalium.UI;

/// <summary>
/// Represents the method that handles an automatic resize notification.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">The event data containing the new size.</param>
public delegate void AutoResizedEventHandler(object sender, AutoResizedEventArgs e);

/// <summary>
/// Provides data for an automatic resize notification.
/// </summary>
public class AutoResizedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AutoResizedEventArgs"/> class.
    /// </summary>
    /// <param name="size">The new size.</param>
    public AutoResizedEventArgs(Size size)
    {
        Size = size;
    }

    /// <summary>
    /// Gets the new size.
    /// </summary>
    public Size Size { get; }
}
