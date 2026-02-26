namespace Jalium.UI.Documents;

/// <summary>
/// Provides an abstraction for the position of content within a text container.
/// </summary>
public interface ITextPointer
{
    /// <summary>
    /// Gets the text container that contains this position.
    /// </summary>
    ITextContainer? TextContainer { get; }

    /// <summary>
    /// Gets a value indicating whether the text container has a valid layout.
    /// </summary>
    bool HasValidLayout { get; }

    /// <summary>
    /// Gets a value indicating whether this position is at a valid insertion position.
    /// </summary>
    bool IsAtInsertionPosition { get; }

    /// <summary>
    /// Gets the logical direction associated with this position.
    /// </summary>
    LogicalDirection LogicalDirection { get; }

    /// <summary>
    /// Gets the offset of this position from the beginning of the text container.
    /// </summary>
    int Offset { get; }

    /// <summary>
    /// Creates a new <see cref="ITextPointer"/> at the same position.
    /// </summary>
    /// <returns>A new text pointer at the same position.</returns>
    ITextPointer CreatePointer();

    /// <summary>
    /// Creates a new <see cref="ITextPointer"/> at the specified offset from this position.
    /// </summary>
    /// <param name="offset">The offset from this position.</param>
    /// <returns>A new text pointer at the offset position.</returns>
    ITextPointer CreatePointer(int offset);

    /// <summary>
    /// Creates a new <see cref="ITextPointer"/> at the same position with the specified direction.
    /// </summary>
    /// <param name="direction">The logical direction for the new pointer.</param>
    /// <returns>A new text pointer with the specified direction.</returns>
    ITextPointer CreatePointer(LogicalDirection direction);

    /// <summary>
    /// Creates a new <see cref="ITextPointer"/> at the specified offset with the specified direction.
    /// </summary>
    /// <param name="offset">The offset from this position.</param>
    /// <param name="direction">The logical direction for the new pointer.</param>
    /// <returns>A new text pointer at the offset position.</returns>
    ITextPointer CreatePointer(int offset, LogicalDirection direction);

    /// <summary>
    /// Compares the position of this pointer with another pointer.
    /// </summary>
    /// <param name="position">The position to compare with.</param>
    /// <returns>Negative if this position precedes, zero if equal, positive if follows.</returns>
    int CompareTo(ITextPointer position);

    /// <summary>
    /// Gets the type of content adjacent to this position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to check.</param>
    /// <returns>The type of adjacent content.</returns>
    Type? GetPointerContext(LogicalDirection direction);

    /// <summary>
    /// Returns a bounding rectangle for the content bordering this position.
    /// </summary>
    /// <param name="direction">The direction of the content to measure.</param>
    /// <returns>A <see cref="Rect"/> representing the character bounds.</returns>
    Rect GetCharacterRect(LogicalDirection direction);

    /// <summary>
    /// Returns the next context position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to search.</param>
    /// <returns>The next context position, or null if none exists.</returns>
    ITextPointer GetNextContextPosition(LogicalDirection direction);

    /// <summary>
    /// Returns the next valid insertion position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to search.</param>
    /// <returns>The next insertion position, or null if none exists.</returns>
    ITextPointer GetNextInsertionPosition(LogicalDirection direction);

    /// <summary>
    /// Returns the position at the start of a line relative to this position.
    /// </summary>
    /// <param name="count">The number of lines to move (0 = current line).</param>
    /// <returns>The position at the start of the target line, or null.</returns>
    ITextPointer? GetLineStartPosition(int count);

    /// <summary>
    /// Returns the position at the start of a line relative to this position,
    /// and reports how many lines were actually moved.
    /// </summary>
    /// <param name="count">The number of lines to move (0 = current line).</param>
    /// <param name="actualCount">The actual number of lines moved.</param>
    /// <returns>The position at the start of the target line, or null.</returns>
    ITextPointer? GetLineStartPosition(int count, out int actualCount);

    /// <summary>
    /// Gets the element adjacent to this position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to check.</param>
    /// <returns>The adjacent element, or null.</returns>
    object? GetAdjacentElement(LogicalDirection direction);

    /// <summary>
    /// Gets the text in the current run in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to read.</param>
    /// <returns>The text in the run.</returns>
    string GetTextInRun(LogicalDirection direction);

    /// <summary>
    /// Gets the length of the text run in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to measure.</param>
    /// <returns>The length of the text run.</returns>
    int GetTextRunLength(LogicalDirection direction);

    /// <summary>
    /// Moves this pointer to the same position as the specified pointer.
    /// </summary>
    /// <param name="position">The target position.</param>
    void MoveToPosition(ITextPointer position);

    /// <summary>
    /// Moves this pointer to the next context position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to move.</param>
    /// <returns>true if the pointer was moved; false if no context position exists.</returns>
    bool MoveToNextContextPosition(LogicalDirection direction);

    /// <summary>
    /// Moves this pointer to the next insertion position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to move.</param>
    /// <returns>true if the pointer was moved; false if no insertion position exists.</returns>
    bool MoveToNextInsertionPosition(LogicalDirection direction);

    /// <summary>
    /// Moves this pointer by the specified offset.
    /// </summary>
    /// <param name="offset">The number of positions to move.</param>
    /// <returns>The actual number of positions moved.</returns>
    int MoveByOffset(int offset);
}

/// <summary>
/// Provides an abstraction for content that hosts text.
/// </summary>
public interface ITextContainer
{
    /// <summary>
    /// Gets a pointer to the start of the text content.
    /// </summary>
    ITextPointer Start { get; }

    /// <summary>
    /// Gets a pointer to the end of the text content.
    /// </summary>
    ITextPointer End { get; }

    /// <summary>
    /// Gets a value indicating whether the text container is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Occurs when the content of the text container changes.
    /// </summary>
    event EventHandler Changed;
}

/// <summary>
/// Provides an abstraction for text selection within a text container.
/// </summary>
public interface ITextSelection : ITextRange
{
    /// <summary>
    /// Gets the anchor position of the selection (the position where the selection started).
    /// </summary>
    ITextPointer AnchorPosition { get; }

    /// <summary>
    /// Gets the moving position of the selection (the position that moves during selection extension).
    /// </summary>
    ITextPointer MovingPosition { get; }

    /// <summary>
    /// Sets the selection to span from the anchor position to the moving position.
    /// </summary>
    /// <param name="anchorPosition">The anchor position.</param>
    /// <param name="movingPosition">The moving position.</param>
    void Select(ITextPointer anchorPosition, ITextPointer movingPosition);

    /// <summary>
    /// Extends the selection to the next insertion position in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to extend.</param>
    /// <returns>true if the selection was extended; false if no insertion position exists.</returns>
    bool ExtendToNextInsertionPosition(LogicalDirection direction);
}

/// <summary>
/// Provides an abstraction for a range of text content.
/// </summary>
public interface ITextRange
{
    /// <summary>
    /// Gets a pointer to the start of the range.
    /// </summary>
    ITextPointer Start { get; }

    /// <summary>
    /// Gets a pointer to the end of the range.
    /// </summary>
    ITextPointer End { get; }

    /// <summary>
    /// Gets a value indicating whether the range is empty (start and end are at the same position).
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets or sets the plain text content of the range.
    /// </summary>
    string Text { get; set; }

    /// <summary>
    /// Determines whether the specified position is within this range.
    /// </summary>
    /// <param name="position">The position to check.</param>
    /// <returns>true if the position is within the range; otherwise false.</returns>
    bool Contains(ITextPointer position);

    /// <summary>
    /// Sets the range to span between two positions.
    /// </summary>
    /// <param name="position1">The first position.</param>
    /// <param name="position2">The second position.</param>
    void Select(ITextPointer position1, ITextPointer position2);

    /// <summary>
    /// Gets the value of the specified formatting property for the range.
    /// </summary>
    /// <param name="formattingProperty">The formatting dependency property to query.</param>
    /// <returns>The property value, or <see cref="DependencyProperty.UnsetValue"/> if mixed.</returns>
    object? GetPropertyValue(DependencyProperty formattingProperty);

    /// <summary>
    /// Applies a formatting property value to the entire range.
    /// </summary>
    /// <param name="formattingProperty">The formatting dependency property to set.</param>
    /// <param name="value">The value to apply.</param>
    void ApplyPropertyValue(DependencyProperty formattingProperty, object value);
}
