namespace Jalium.UI.Documents;

/// <summary>
/// Represents the active selection maintained by a text editor or document viewer.
/// </summary>
public sealed class TextSelection : TextRange
{
    private TextPointer _anchorPosition;
    private TextPointer _movingPosition;

    internal TextSelection(TextPointer anchorPosition, TextPointer movingPosition)
        : base(anchorPosition, movingPosition)
    {
        _anchorPosition = anchorPosition;
        _movingPosition = movingPosition;
    }

    /// <summary>
    /// Gets the fixed end of the selection.
    /// </summary>
    public TextPointer AnchorPosition => _anchorPosition;

    /// <summary>
    /// Gets the moving end (caret position) of the selection.
    /// </summary>
    public TextPointer MovingPosition => _movingPosition;

    internal override void OnRangeSelected(TextPointer position1, TextPointer position2)
    {
        _anchorPosition = position1;
        _movingPosition = position2;
    }

    internal override void OnRangeContentChanged()
    {
        _anchorPosition = Start;
        _movingPosition = End;
    }

    internal void SelectOffsets(FlowDocument document, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(document);
        var textLength = document.GetText().Length;
        var normalizedStart = Math.Clamp(start, 0, textLength);
        var normalizedEnd = Math.Clamp(normalizedStart + Math.Max(0, length), normalizedStart, textLength);
        var startPointer = document.GetPositionAtOffset(normalizedStart, LogicalDirection.Forward) ?? document.ContentStart;
        var endPointer = document.GetPositionAtOffset(normalizedEnd, LogicalDirection.Backward) ?? document.ContentEnd;
        Select(startPointer, endPointer);
    }
}
