namespace Jalium.UI;

/// <summary>
/// Describes the height or width of a <see cref="Jalium.UI.Controls.Documents.Figure"/>.
/// </summary>
public readonly struct FigureLength : IEquatable<FigureLength>
{
    private readonly double _value;
    private readonly FigureUnitType _unitType;

    /// <summary>
    /// Initializes a new instance with the specified value and unit type Auto.
    /// </summary>
    public FigureLength(double value) : this(value, FigureUnitType.Auto)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified value and unit type.
    /// </summary>
    public FigureLength(double value, FigureUnitType type)
    {
        _value = value;
        _unitType = type;
    }

    /// <summary>
    /// Gets the value of this FigureLength.
    /// </summary>
    public double Value => _value;

    /// <summary>
    /// Gets the unit type of this FigureLength.
    /// </summary>
    public FigureUnitType FigureUnitType => _unitType;

    /// <summary>
    /// Gets a value indicating whether this FigureLength is automatic.
    /// </summary>
    public bool IsAuto => _unitType == FigureUnitType.Auto;

    /// <summary>
    /// Gets a value indicating whether this FigureLength is absolute.
    /// </summary>
    public bool IsAbsolute => _unitType == FigureUnitType.Pixel;

    /// <summary>
    /// Gets a value indicating whether this FigureLength is a column.
    /// </summary>
    public bool IsColumn => _unitType == FigureUnitType.Column;

    /// <summary>
    /// Gets a value indicating whether this FigureLength is content-based.
    /// </summary>
    public bool IsContent => _unitType == FigureUnitType.Content;

    /// <summary>
    /// Gets a value indicating whether this FigureLength is page-based.
    /// </summary>
    public bool IsPage => _unitType == FigureUnitType.Page;

    /// <inheritdoc />
    public bool Equals(FigureLength other) => _value == other._value && _unitType == other._unitType;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FigureLength other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_value, _unitType);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(FigureLength left, FigureLength right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(FigureLength left, FigureLength right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => _unitType switch
    {
        FigureUnitType.Auto => "Auto",
        FigureUnitType.Pixel => $"{_value}px",
        FigureUnitType.Column => $"{_value}col",
        FigureUnitType.Content => $"{_value}content",
        FigureUnitType.Page => $"{_value}page",
        _ => _value.ToString()
    };
}

/// <summary>
/// Describes the unit type associated with the width or height of a <see cref="FigureLength"/>.
/// </summary>
public enum FigureUnitType
{
    /// <summary>Value is automatically determined.</summary>
    Auto,
    /// <summary>Value is in device independent pixels.</summary>
    Pixel,
    /// <summary>Value is expressed as a fraction of the column width.</summary>
    Column,
    /// <summary>Value is expressed as a fraction of the content area.</summary>
    Content,
    /// <summary>Value is expressed as a fraction of the page.</summary>
    Page
}

/// <summary>
/// Specifies the horizontal anchoring position of a figure.
/// </summary>
public enum FigureHorizontalAnchor
{
    /// <summary>Anchor to the left of the page.</summary>
    PageLeft,
    /// <summary>Anchor to the center of the page.</summary>
    PageCenter,
    /// <summary>Anchor to the right of the page.</summary>
    PageRight,
    /// <summary>Anchor to the left of the content.</summary>
    ContentLeft,
    /// <summary>Anchor to the center of the content.</summary>
    ContentCenter,
    /// <summary>Anchor to the right of the content.</summary>
    ContentRight,
    /// <summary>Anchor to the left of the column.</summary>
    ColumnLeft,
    /// <summary>Anchor to the center of the column.</summary>
    ColumnCenter,
    /// <summary>Anchor to the right of the column.</summary>
    ColumnRight
}

/// <summary>
/// Specifies the vertical anchoring position of a figure.
/// </summary>
public enum FigureVerticalAnchor
{
    /// <summary>Anchor to the top of the page.</summary>
    PageTop,
    /// <summary>Anchor to the center of the page.</summary>
    PageCenter,
    /// <summary>Anchor to the bottom of the page.</summary>
    PageBottom,
    /// <summary>Anchor to the top of the content.</summary>
    ContentTop,
    /// <summary>Anchor to the center of the content.</summary>
    ContentCenter,
    /// <summary>Anchor to the bottom of the content.</summary>
    ContentBottom,
    /// <summary>Anchor to the top of the paragraph.</summary>
    ParagraphTop
}
