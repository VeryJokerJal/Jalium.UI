namespace Jalium.UI;

/// <summary>
/// Provides an identity key for CLR events stored in an internal handler table.
/// </summary>
/// <remarks>
/// The type deliberately uses reference identity. Event owners keep instances in
/// private static fields so unrelated code cannot spoof an event-table lookup.
/// </remarks>
public class EventPrivateKey
{
    /// <summary>
    /// Initializes a unique event key.
    /// </summary>
    public EventPrivateKey()
    {
    }
}
