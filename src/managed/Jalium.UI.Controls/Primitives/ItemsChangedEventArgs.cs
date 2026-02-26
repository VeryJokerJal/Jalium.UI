using System.Collections.Specialized;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides data for the ItemsChanged event.
/// </summary>
public sealed class ItemsChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the action that caused the event.
    /// </summary>
    public NotifyCollectionChangedAction Action { get; }

    /// <summary>
    /// Gets the position in the collection where the change occurred.
    /// </summary>
    public GeneratorPosition Position { get; }

    /// <summary>
    /// Gets the position in the collection before the change (for Move operations).
    /// </summary>
    public GeneratorPosition OldPosition { get; }

    /// <summary>
    /// Gets the number of items that were affected by the change.
    /// </summary>
    public int ItemCount { get; }

    /// <summary>
    /// Gets the number of UI elements that were affected by the change.
    /// </summary>
    public int ItemUICount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsChangedEventArgs"/> class.
    /// </summary>
    /// <param name="action">The action that caused the event.</param>
    /// <param name="position">The position where the change occurred.</param>
    /// <param name="itemCount">The number of items affected.</param>
    /// <param name="itemUICount">The number of UI elements affected.</param>
    public ItemsChangedEventArgs(NotifyCollectionChangedAction action, GeneratorPosition position, int itemCount, int itemUICount)
    {
        Action = action;
        Position = position;
        OldPosition = new GeneratorPosition(-1, 0);
        ItemCount = itemCount;
        ItemUICount = itemUICount;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemsChangedEventArgs"/> class for move operations.
    /// </summary>
    /// <param name="action">The action that caused the event.</param>
    /// <param name="position">The new position.</param>
    /// <param name="oldPosition">The old position.</param>
    /// <param name="itemCount">The number of items affected.</param>
    /// <param name="itemUICount">The number of UI elements affected.</param>
    public ItemsChangedEventArgs(NotifyCollectionChangedAction action, GeneratorPosition position, GeneratorPosition oldPosition, int itemCount, int itemUICount)
    {
        Action = action;
        Position = position;
        OldPosition = oldPosition;
        ItemCount = itemCount;
        ItemUICount = itemUICount;
    }
}

/// <summary>
/// Specifies the position of an item in an ItemContainerGenerator.
/// </summary>
public struct GeneratorPosition : IEquatable<GeneratorPosition>
{
    /// <summary>
    /// Gets or sets the index of the generated (realized) item.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets the offset from the indexed item.
    /// </summary>
    public int Offset { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratorPosition"/> struct.
    /// </summary>
    /// <param name="index">The index of the generated item.</param>
    /// <param name="offset">The offset from the indexed item.</param>
    public GeneratorPosition(int index, int offset)
    {
        Index = index;
        Offset = offset;
    }

    /// <inheritdoc />
    public bool Equals(GeneratorPosition other)
    {
        return Index == other.Index && Offset == other.Offset;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is GeneratorPosition other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Index, Offset);
    }

    /// <summary>
    /// Compares two GeneratorPosition values for equality.
    /// </summary>
    public static bool operator ==(GeneratorPosition left, GeneratorPosition right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two GeneratorPosition values for inequality.
    /// </summary>
    public static bool operator !=(GeneratorPosition left, GeneratorPosition right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"GeneratorPosition ({Index},{Offset})";
    }
}

/// <summary>
/// Delegate for the ItemsChanged event.
/// </summary>
/// <param name="sender">The source of the event.</param>
/// <param name="e">The event data.</param>
public delegate void ItemsChangedEventHandler(object sender, ItemsChangedEventArgs e);
