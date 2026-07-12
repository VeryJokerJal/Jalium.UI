namespace Jalium.UI;

/// <summary>
/// Specifies how a line box's height is determined.
/// </summary>
public enum LineStackingStrategy
{
    /// <summary>Use the block element's line-height value.</summary>
    BlockLineHeight = 0,

    /// <summary>Use the smallest height that contains all aligned inline elements.</summary>
    MaxHeight = 1,
}

/// <summary>
/// Specifies how unused space is distributed when column content is narrower than its container.
/// </summary>
public enum ColumnSpaceDistribution
{
    /// <summary>Place the unused space before the first column.</summary>
    Left = 0,

    /// <summary>Place the unused space after the last column.</summary>
    Right = 1,

    /// <summary>Distribute the unused space evenly between columns.</summary>
    Between = 2,
}
