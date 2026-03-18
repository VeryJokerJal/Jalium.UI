namespace Jalium.UI.Media;

/// <summary>
/// Represents a Drawing that renders text.
/// </summary>
public sealed class GlyphRunDrawing : Drawing
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphRunDrawing"/> class.
    /// </summary>
    public GlyphRunDrawing()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphRunDrawing"/> class
    /// with the specified formatted text and origin.
    /// </summary>
    /// <param name="formattedText">The formatted text to draw.</param>
    /// <param name="origin">The origin point for the text.</param>
    public GlyphRunDrawing(FormattedText? formattedText, Point origin)
    {
        FormattedText = formattedText;
        Origin = origin;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphRunDrawing"/> class
    /// with the specified foreground brush and glyph run.
    /// </summary>
    /// <param name="foregroundBrush">The brush used to paint the text.</param>
    /// <param name="glyphRun">The glyph run to draw.</param>
    public GlyphRunDrawing(Brush? foregroundBrush, GlyphRun? glyphRun)
    {
        ForegroundBrush = foregroundBrush;
        GlyphRun = glyphRun;
    }

    /// <summary>
    /// Gets or sets the foreground brush used to paint the text.
    /// </summary>
    public Brush? ForegroundBrush { get; set; }

    /// <summary>
    /// Gets or sets the GlyphRun that describes the text to draw.
    /// </summary>
    public GlyphRun? GlyphRun { get; set; }

    /// <summary>
    /// Gets or sets the FormattedText to draw (alternative to GlyphRun).
    /// </summary>
    public FormattedText? FormattedText { get; set; }

    /// <summary>
    /// Gets or sets the origin point for the text.
    /// </summary>
    public Point Origin { get; set; }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (FormattedText != null)
            {
                return new Rect(Origin.X, Origin.Y, FormattedText.Width, FormattedText.Height);
            }

            if (GlyphRun != null)
            {
                return GlyphRun.ComputeInkBoundingBox();
            }

            return Rect.Empty;
        }
    }

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        if (FormattedText != null)
        {
            if (ForegroundBrush != null)
            {
                FormattedText.Foreground = ForegroundBrush;
            }
            context.DrawText(FormattedText, Origin);
        }
        else if (GlyphRun != null)
        {
            // GlyphRun rendering would need DrawGlyphRun method
            // For now, we'll convert to FormattedText if possible
        }
    }
}

/// <summary>
/// Represents a set of glyphs from a single font face at a single size, and with a single rendering style.
/// </summary>
public sealed class GlyphRun
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphRun"/> class.
    /// </summary>
    public GlyphRun()
    {
    }

    /// <summary>
    /// Gets or sets the font family for this GlyphRun.
    /// </summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the em size for this GlyphRun.
    /// </summary>
    public double FontRenderingEmSize { get; set; }

    /// <summary>
    /// Gets or sets the baseline origin for this GlyphRun.
    /// </summary>
    public Point BaselineOrigin { get; set; }

    /// <summary>
    /// Gets or sets the list of glyph indices for this GlyphRun.
    /// </summary>
    public IList<ushort>? GlyphIndices { get; set; }

    /// <summary>
    /// Gets or sets the list of advance widths for this GlyphRun.
    /// </summary>
    public IList<double>? AdvanceWidths { get; set; }

    /// <summary>
    /// Gets or sets the list of glyph offsets for this GlyphRun.
    /// </summary>
    public IList<Point>? GlyphOffsets { get; set; }

    /// <summary>
    /// Gets or sets the characters that correspond to the glyphs.
    /// </summary>
    public IList<char>? Characters { get; set; }

    /// <summary>
    /// Gets or sets the bidirectional nesting level of this GlyphRun.
    /// </summary>
    public int BidiLevel { get; set; }

    /// <summary>
    /// Gets or sets whether the GlyphRun is sideways.
    /// </summary>
    public bool IsSideways { get; set; }

    /// <summary>
    /// Computes the ink bounding box for this GlyphRun.
    /// </summary>
    /// <returns>The ink bounding box.</returns>
    public Rect ComputeInkBoundingBox()
    {
        if (GlyphIndices == null || GlyphIndices.Count == 0)
        {
            return Rect.Empty;
        }

        // Calculate approximate bounds based on advance widths
        double totalWidth = 0;
        if (AdvanceWidths != null)
        {
            foreach (var width in AdvanceWidths)
            {
                totalWidth += width;
            }
        }

        return new Rect(
            BaselineOrigin.X,
            BaselineOrigin.Y - FontRenderingEmSize,
            totalWidth,
            FontRenderingEmSize);
    }
}
