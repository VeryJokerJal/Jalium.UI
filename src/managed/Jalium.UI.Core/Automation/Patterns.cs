namespace Jalium.UI.Automation;

/// <summary>Specifies the direction and distance to scroll.</summary>
public enum ScrollAmount
{
    LargeDecrement = 0,
    SmallDecrement = 1,
    NoAmount = 2,
    LargeIncrement = 3,
    SmallIncrement = 4,
}

/// <summary>Specifies the kind of text selection a control supports.</summary>
public enum SupportedTextSelection
{
    None = 0,
    Single = 1,
    Multiple = 2,
}

/// <summary>
/// Framework-internal character-offset text source. The public automation surface is
/// <see cref="Provider.ITextProvider"/> and <see cref="Provider.ITextRangeProvider"/>.
/// </summary>
internal interface IAutomationTextProviderSource
{
    string Text { get; }

    int SelectionStart { get; }

    int SelectionLength { get; }

    bool IsReadOnly { get; }

    SupportedTextSelection SupportedTextSelection { get; }

    void Select(int start, int length);

    IReadOnlyList<Rect> GetBoundingRectangles(int start, int length);

    void ScrollIntoView(int start, int length);
}

// Optional layout capabilities: existing third-party text sources keep their
// offset contract, while rendered editors can expose their actual visual rows.
internal interface IAutomationTextViewSource
{
    int CaretIndex { get; }
    bool CaretHasBackwardAffinity => false;
    Rect TextViewport { get; }
    IReadOnlyList<AutomationTextLine> GetTextLines();
    bool TryGetInsertionIndex(Point localPoint, out int index);
    bool ReplaceSelection(string text);
}

internal readonly record struct AutomationTextSpan(int Start, int Length);
internal readonly record struct AutomationTextLine(int Start, int Length, Rect Bounds);
