namespace Jalium.UI.Controls.Virtualization;

/// <summary>
/// Represents the logical index window that should be realized.
/// </summary>
internal readonly struct RealizationWindow : IEquatable<RealizationWindow>
{
    public static readonly RealizationWindow Empty = new(-1, -1);

    public RealizationWindow(int startIndex, int endIndex)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
    }

    public int StartIndex { get; }

    public int EndIndex { get; }

    public bool IsEmpty => StartIndex < 0 || EndIndex < StartIndex;

    public bool Contains(int index) => !IsEmpty && index >= StartIndex && index <= EndIndex;

    public bool Equals(RealizationWindow other) => StartIndex == other.StartIndex && EndIndex == other.EndIndex;

    public override bool Equals(object? obj) => obj is RealizationWindow other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StartIndex, EndIndex);
}

