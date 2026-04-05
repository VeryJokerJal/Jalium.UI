namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="HexEditor.SelectionChanged"/> event.
/// </summary>
public sealed class HexSelectionChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the previous selection start offset.
    /// </summary>
    public long OldSelectionStart { get; }

    /// <summary>
    /// Gets the previous selection length.
    /// </summary>
    public long OldSelectionLength { get; }

    /// <summary>
    /// Gets the new selection start offset.
    /// </summary>
    public long NewSelectionStart { get; }

    /// <summary>
    /// Gets the new selection length.
    /// </summary>
    public long NewSelectionLength { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HexSelectionChangedEventArgs"/> class.
    /// </summary>
    public HexSelectionChangedEventArgs(RoutedEvent routedEvent,
        long oldSelectionStart, long oldSelectionLength,
        long newSelectionStart, long newSelectionLength)
    {
        RoutedEvent = routedEvent;
        OldSelectionStart = oldSelectionStart;
        OldSelectionLength = oldSelectionLength;
        NewSelectionStart = newSelectionStart;
        NewSelectionLength = newSelectionLength;
    }
}

/// <summary>
/// Provides data for the <see cref="HexEditor.ByteModified"/> event.
/// </summary>
public sealed class HexByteModifiedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the offset of the modified byte.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// Gets the old byte value.
    /// </summary>
    public byte OldValue { get; }

    /// <summary>
    /// Gets the new byte value.
    /// </summary>
    public byte NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HexByteModifiedEventArgs"/> class.
    /// </summary>
    public HexByteModifiedEventArgs(RoutedEvent routedEvent, long offset, byte oldValue, byte newValue)
    {
        RoutedEvent = routedEvent;
        Offset = offset;
        OldValue = oldValue;
        NewValue = newValue;
    }
}
