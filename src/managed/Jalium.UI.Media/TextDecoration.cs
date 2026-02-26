namespace Jalium.UI.Media;

/// <summary>
/// Specifies the location of the text decoration with respect to the text.
/// </summary>
public enum TextDecorationLocation
{
    /// <summary>
    /// The text decoration is underline.
    /// </summary>
    Underline,

    /// <summary>
    /// The text decoration is overline.
    /// </summary>
    OverLine,

    /// <summary>
    /// The text decoration is strikethrough.
    /// </summary>
    Strikethrough,

    /// <summary>
    /// The text decoration is baseline.
    /// </summary>
    Baseline
}

/// <summary>
/// Represents a text decoration, such as an underline.
/// </summary>
public sealed class TextDecoration
{
    /// <summary>
    /// Gets or sets the location of the text decoration.
    /// </summary>
    public TextDecorationLocation Location { get; set; }

    /// <summary>
    /// Gets or sets the brush used to draw the text decoration.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the thickness of the decoration line.
    /// </summary>
    public double Thickness { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the offset for the text decoration.
    /// </summary>
    public double Offset { get; set; }

    /// <summary>
    /// Gets or sets the offset unit for the text decoration.
    /// </summary>
    public TextDecorationUnit OffsetUnit { get; set; } = TextDecorationUnit.FontRecommended;

    /// <summary>
    /// Gets or sets the thickness unit for the text decoration.
    /// </summary>
    public TextDecorationUnit ThicknessUnit { get; set; } = TextDecorationUnit.FontRecommended;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecoration"/> class.
    /// </summary>
    public TextDecoration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecoration"/> class with the specified location.
    /// </summary>
    /// <param name="location">The location of the text decoration.</param>
    /// <param name="brush">The brush used to draw the decoration.</param>
    /// <param name="thickness">The thickness of the decoration line.</param>
    /// <param name="offset">The offset of the decoration line.</param>
    /// <param name="offsetUnit">The unit for the offset.</param>
    /// <param name="thicknessUnit">The unit for the thickness.</param>
    public TextDecoration(TextDecorationLocation location, Brush? brush, double thickness,
        double offset, TextDecorationUnit offsetUnit, TextDecorationUnit thicknessUnit)
    {
        Location = location;
        Brush = brush;
        Thickness = thickness;
        Offset = offset;
        OffsetUnit = offsetUnit;
        ThicknessUnit = thicknessUnit;
    }
}

/// <summary>
/// Specifies the units for a text decoration.
/// </summary>
public enum TextDecorationUnit
{
    /// <summary>
    /// The unit is a fraction of the font em size.
    /// </summary>
    FontRecommended,

    /// <summary>
    /// The unit is a fraction of the font em size.
    /// </summary>
    FontRenderingEmSize,

    /// <summary>
    /// The unit is in pixels.
    /// </summary>
    Pixel
}

/// <summary>
/// Represents a collection of TextDecoration objects.
/// </summary>
public sealed class TextDecorationCollection : List<TextDecoration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecorationCollection"/> class.
    /// </summary>
    public TextDecorationCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextDecorationCollection"/> class with the specified decorations.
    /// </summary>
    /// <param name="decorations">The initial decorations.</param>
    public TextDecorationCollection(IEnumerable<TextDecoration> decorations) : base(decorations)
    {
    }

    /// <summary>
    /// Determines whether the collection contains a decoration at the specified location.
    /// </summary>
    /// <param name="location">The location to check.</param>
    /// <returns>True if the collection contains a decoration at the location; otherwise, false.</returns>
    public bool HasDecoration(TextDecorationLocation location)
    {
        return this.Any(d => d.Location == location);
    }

    /// <summary>
    /// Removes all decorations at the specified location.
    /// </summary>
    /// <param name="location">The location of decorations to remove.</param>
    public void RemoveDecoration(TextDecorationLocation location)
    {
        RemoveAll(d => d.Location == location);
    }
}

/// <summary>
/// Provides a set of predefined text decorations.
/// </summary>
public static class TextDecorations
{
    private static TextDecorationCollection? _underline;
    private static TextDecorationCollection? _strikethrough;
    private static TextDecorationCollection? _overLine;
    private static TextDecorationCollection? _baseline;

    /// <summary>
    /// Gets a text decoration collection that specifies an underline.
    /// </summary>
    public static TextDecorationCollection Underline
    {
        get
        {
            _underline ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Underline
                    }
                };
            return _underline;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies a strikethrough.
    /// </summary>
    public static TextDecorationCollection Strikethrough
    {
        get
        {
            _strikethrough ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Strikethrough
                    }
                };
            return _strikethrough;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies an overline.
    /// </summary>
    public static TextDecorationCollection OverLine
    {
        get
        {
            _overLine ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.OverLine
                    }
                };
            return _overLine;
        }
    }

    /// <summary>
    /// Gets a text decoration collection that specifies a baseline decoration.
    /// </summary>
    public static TextDecorationCollection Baseline
    {
        get
        {
            _baseline ??= new TextDecorationCollection
                {
                    new TextDecoration
                    {
                        Location = TextDecorationLocation.Baseline
                    }
                };
            return _baseline;
        }
    }
}
