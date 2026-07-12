using System.ComponentModel;
using Jalium.UI.Markup;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Media;

/// <summary>
/// Represents a Drawing that renders text.
/// </summary>
public sealed class GlyphRunDrawing : Drawing
{
    public static readonly DependencyProperty ForegroundBrushProperty =
        DependencyProperty.Register(nameof(ForegroundBrush), typeof(Brush), typeof(GlyphRunDrawing), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphRunProperty =
        DependencyProperty.Register(nameof(GlyphRun), typeof(GlyphRun), typeof(GlyphRunDrawing), new PropertyMetadata(null));
    private static readonly DependencyProperty FormattedTextProperty =
        DependencyProperty.Register(nameof(FormattedText), typeof(FormattedText), typeof(GlyphRunDrawing), new PropertyMetadata(null));
    private static readonly DependencyProperty OriginProperty =
        DependencyProperty.Register(nameof(Origin), typeof(Point), typeof(GlyphRunDrawing), new PropertyMetadata(default(Point)));

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
    public Brush? ForegroundBrush
    {
        get => (Brush?)GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the GlyphRun that describes the text to draw.
    /// </summary>
    public GlyphRun? GlyphRun
    {
        get => (GlyphRun?)GetValue(GlyphRunProperty);
        set => SetValue(GlyphRunProperty, value);
    }

    /// <summary>
    /// Gets or sets the FormattedText to draw (alternative to GlyphRun).
    /// </summary>
    public FormattedText? FormattedText
    {
        get => (FormattedText?)GetValue(FormattedTextProperty);
        set => SetValue(FormattedTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the origin point for the text.
    /// </summary>
    public Point Origin
    {
        get => (Point)(GetValue(OriginProperty) ?? default(Point));
        set => SetValue(OriginProperty, value);
    }

    public new GlyphRunDrawing Clone() => (GlyphRunDrawing)base.Clone();
    public new GlyphRunDrawing CloneCurrentValue() => (GlyphRunDrawing)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GlyphRunDrawing();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ForegroundBrushProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, ForegroundBrushProperty);
        }

        if (ReferenceEquals(e.Property, ForegroundBrushProperty)
            || ReferenceEquals(e.Property, GlyphRunProperty)
            || ReferenceEquals(e.Property, FormattedTextProperty)
            || ReferenceEquals(e.Property, OriginProperty))
        {
            WritePostscript();
        }
    }

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
            context.DrawGlyphRun(ForegroundBrush, GlyphRun);
        }
    }
}

/// <summary>
/// Represents a set of glyphs from a single font face at a single size, and with a single rendering style.
/// </summary>
public sealed class GlyphRun : ISupportInitialize
{
    private bool _initializing;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphRun"/> class.
    /// </summary>
    public GlyphRun()
    {
    }

    /// <summary>Initializes an empty glyph run for the specified display density.</summary>
    public GlyphRun(float pixelsPerDip)
    {
        ValidatePixelsPerDip(pixelsPerDip, nameof(pixelsPerDip));
        PixelsPerDip = pixelsPerDip;
    }

    public GlyphRun(
        GlyphTypeface glyphTypeface,
        int bidiLevel,
        bool isSideways,
        double renderingEmSize,
        IList<ushort> glyphIndices,
        Point baselineOrigin,
        IList<double> advanceWidths,
        IList<Point> glyphOffsets,
        IList<char> characters,
        string deviceFontName,
        IList<ushort> clusterMap,
        IList<bool> caretStops,
        XmlLanguage language)
        : this(
            glyphTypeface,
            bidiLevel,
            isSideways,
            renderingEmSize,
            1.0f,
            glyphIndices,
            baselineOrigin,
            advanceWidths,
            glyphOffsets,
            characters,
            deviceFontName,
            clusterMap,
            caretStops,
            language)
    {
    }

    public GlyphRun(
        GlyphTypeface glyphTypeface,
        int bidiLevel,
        bool isSideways,
        double renderingEmSize,
        float pixelsPerDip,
        IList<ushort> glyphIndices,
        Point baselineOrigin,
        IList<double> advanceWidths,
        IList<Point> glyphOffsets,
        IList<char> characters,
        string deviceFontName,
        IList<ushort> clusterMap,
        IList<bool> caretStops,
        XmlLanguage language)
    {
        ArgumentNullException.ThrowIfNull(glyphTypeface);
        ArgumentNullException.ThrowIfNull(glyphIndices);
        ArgumentNullException.ThrowIfNull(advanceWidths);
        ArgumentNullException.ThrowIfNull(language);
        ValidateEmSize(renderingEmSize, nameof(renderingEmSize));
        ValidatePixelsPerDip(pixelsPerDip, nameof(pixelsPerDip));
        ArgumentOutOfRangeException.ThrowIfNegative(bidiLevel);
        ValidateCollections(glyphIndices, advanceWidths, glyphOffsets, characters, clusterMap, caretStops);

        GlyphTypeface = glyphTypeface;
        BidiLevel = bidiLevel;
        IsSideways = isSideways;
        FontRenderingEmSize = renderingEmSize;
        PixelsPerDip = pixelsPerDip;
        GlyphIndices = glyphIndices;
        BaselineOrigin = baselineOrigin;
        AdvanceWidths = advanceWidths;
        GlyphOffsets = glyphOffsets;
        Characters = characters;
        DeviceFontName = deviceFontName;
        ClusterMap = clusterMap;
        CaretStops = caretStops;
        Language = language;
    }

    /// <summary>
    /// Gets or sets the font family for this GlyphRun.
    /// </summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>Gets or sets the physical typeface that owns the glyph indices.</summary>
    public GlyphTypeface? GlyphTypeface { get; set; }

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

    public IList<bool>? CaretStops { get; set; }

    public IList<ushort>? ClusterMap { get; set; }

    public string? DeviceFontName { get; set; }

    public bool IsHitTestable => Characters is { Count: > 0 }
        && GlyphIndices is { Count: > 0 }
        && AdvanceWidths is { Count: > 0 };

    public XmlLanguage? Language { get; set; }

    public float PixelsPerDip { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the bidirectional nesting level of this GlyphRun.
    /// </summary>
    public int BidiLevel { get; set; }

    /// <summary>
    /// Gets or sets whether the GlyphRun is sideways.
    /// </summary>
    public bool IsSideways { get; set; }

    public Geometry BuildGeometry()
    {
        var result = new GeometryGroup();
        if (GlyphIndices is null || AdvanceWidths is null)
        {
            return result;
        }

        double x = BaselineOrigin.X;
        double baseline = GlyphTypeface?.Baseline ?? FontFamily?.Baseline ?? 0.8;
        double heightRatio = GlyphTypeface?.Height ?? FontFamily?.LineSpacing ?? 1.2;
        for (int index = 0; index < Math.Min(GlyphIndices.Count, AdvanceWidths.Count); index++)
        {
            Point offset = GlyphOffsets is { } offsets && index < offsets.Count ? offsets[index] : default;
            double advance = AdvanceWidths[index];
            double glyphWidth = Math.Max(0, advance);
            double glyphHeight = Math.Max(0, FontRenderingEmSize * heightRatio);
            if (glyphWidth > 0 && glyphHeight > 0)
            {
                result.Children.Add(new RectangleGeometry(new Rect(
                    x + offset.X,
                    BaselineOrigin.Y - FontRenderingEmSize * baseline + offset.Y,
                    glyphWidth,
                    glyphHeight)));
            }

            x += advance;
        }

        return result;
    }

    public Rect ComputeAlignmentBox()
    {
        if (GlyphIndices is null || GlyphIndices.Count == 0)
        {
            return Rect.Empty;
        }

        double width = AdvanceWidths?.Sum() ?? 0;
        double baseline = GlyphTypeface?.Baseline ?? FontFamily?.Baseline ?? 0.8;
        double height = FontRenderingEmSize * (GlyphTypeface?.Height ?? FontFamily?.LineSpacing ?? 1.2);
        return new Rect(BaselineOrigin.X, BaselineOrigin.Y - baseline * FontRenderingEmSize, width, height);
    }

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

        var ascent = FontRenderingEmSize * (GlyphTypeface?.Baseline ?? FontFamily?.Baseline ?? 0.8);
        var totalHeight = FontRenderingEmSize * (GlyphTypeface?.Height ?? FontFamily?.LineSpacing ?? 1.2);

        return new Rect(
            BaselineOrigin.X,
            BaselineOrigin.Y - ascent,
            totalWidth,
            totalHeight);
    }

    public double GetDistanceFromCaretCharacterHit(CharacterHit characterHit)
    {
        int position = GetCaretPosition(characterHit);
        int glyphPosition = GetGlyphPosition(position);
        double distance = 0;
        if (AdvanceWidths is null)
        {
            return distance;
        }

        for (int index = 0; index < Math.Min(glyphPosition, AdvanceWidths.Count); index++)
        {
            distance += AdvanceWidths[index];
        }

        return distance;
    }

    public CharacterHit GetCaretCharacterHitFromDistance(double distance, out bool isInside)
    {
        if (double.IsNaN(distance))
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }

        double total = AdvanceWidths?.Sum() ?? 0;
        isInside = distance >= 0 && distance <= total;
        double clamped = Math.Clamp(distance, 0, total);
        if (AdvanceWidths is null || AdvanceWidths.Count == 0)
        {
            return new CharacterHit(0, 0);
        }

        double cursor = 0;
        for (int glyph = 0; glyph < AdvanceWidths.Count; glyph++)
        {
            double advance = AdvanceWidths[glyph];
            if (clamped < cursor + advance)
            {
                int character = GetCharacterPosition(glyph);
                return clamped - cursor < advance / 2
                    ? new CharacterHit(character, 0)
                    : new CharacterHit(character, 1);
            }

            cursor += advance;
        }

        return new CharacterHit(CharacterCount, 0);
    }

    public CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit)
    {
        int position = GetCaretPosition(characterHit);
        int next = Math.Min(CharacterCount, position + 1);
        return new CharacterHit(next, 0);
    }

    public CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit)
    {
        int position = GetCaretPosition(characterHit);
        int previous = Math.Max(0, position - 1);
        return new CharacterHit(previous, 0);
    }

    void ISupportInitialize.BeginInit()
    {
        if (_initializing)
        {
            throw new InvalidOperationException("Initialization is already in progress.");
        }

        _initializing = true;
    }

    void ISupportInitialize.EndInit()
    {
        if (!_initializing)
        {
            throw new InvalidOperationException("BeginInit must be called before EndInit.");
        }

        _initializing = false;
        ValidateInitializedState();
    }

    private int CharacterCount => Characters?.Count ?? ClusterMap?.Count ?? GlyphIndices?.Count ?? 0;

    private int GetCaretPosition(CharacterHit characterHit)
    {
        int position = checked(characterHit.FirstCharacterIndex + characterHit.TrailingLength);
        if (position < 0 || position > CharacterCount)
        {
            throw new ArgumentOutOfRangeException(nameof(characterHit));
        }

        return position;
    }

    private int GetGlyphPosition(int characterPosition)
    {
        if (characterPosition >= CharacterCount)
        {
            return GlyphIndices?.Count ?? 0;
        }

        if (ClusterMap is { } clusterMap && characterPosition < clusterMap.Count)
        {
            return clusterMap[characterPosition];
        }

        return characterPosition;
    }

    private int GetCharacterPosition(int glyphPosition)
    {
        if (ClusterMap is not { } clusterMap)
        {
            return Math.Min(glyphPosition, CharacterCount);
        }

        for (int character = 0; character < clusterMap.Count; character++)
        {
            if (clusterMap[character] >= glyphPosition)
            {
                return character;
            }
        }

        return CharacterCount;
    }

    private void ValidateInitializedState()
    {
        if (GlyphTypeface is null || GlyphIndices is null || AdvanceWidths is null)
        {
            throw new InvalidOperationException("GlyphTypeface, GlyphIndices, and AdvanceWidths are required.");
        }

        ValidateEmSize(FontRenderingEmSize, nameof(FontRenderingEmSize));
        ValidatePixelsPerDip(PixelsPerDip, nameof(PixelsPerDip));
        ValidateCollections(GlyphIndices, AdvanceWidths, GlyphOffsets, Characters, ClusterMap, CaretStops);
    }

    private static void ValidateCollections(
        IList<ushort> glyphIndices,
        IList<double> advanceWidths,
        IList<Point>? glyphOffsets,
        IList<char>? characters,
        IList<ushort>? clusterMap,
        IList<bool>? caretStops)
    {
        if (glyphIndices.Count != advanceWidths.Count)
        {
            throw new ArgumentException("GlyphIndices and AdvanceWidths must have the same number of entries.");
        }

        if (glyphOffsets is not null && glyphOffsets.Count != glyphIndices.Count)
        {
            throw new ArgumentException("GlyphOffsets must have one entry for every glyph.", nameof(glyphOffsets));
        }

        if (clusterMap is not null && characters is not null && clusterMap.Count != characters.Count)
        {
            throw new ArgumentException("ClusterMap must have one entry for every character.", nameof(clusterMap));
        }

        if (caretStops is not null && characters is not null && caretStops.Count != characters.Count + 1)
        {
            throw new ArgumentException("CaretStops must include one entry for every character boundary.", nameof(caretStops));
        }
    }

    private static void ValidateEmSize(double value, string parameterName)
    {
        if (!(value > 0) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePixelsPerDip(float value, string parameterName)
    {
        if (!(value > 0) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
