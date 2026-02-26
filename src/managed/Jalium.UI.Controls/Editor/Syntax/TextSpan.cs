namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Represents a span of text with a start position and length.
/// </summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public bool Contains(int offset) => offset >= Start && offset < End;

    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;
}
