using Jalium.UI.Media;
using Jalium.UI.Markup;
using Jalium.UI.Controls;

namespace Jalium.UI.Documents;

/// <summary>
/// Provides a low-level element for displaying glyphs.
/// </summary>
public sealed class Glyphs : FrameworkElement, IUriContext
{
    private Uri? _baseUri;
    #region Dependency Properties

    /// <summary>
    /// Identifies the FontUri dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontUriProperty =
        DependencyProperty.Register(nameof(FontUri), typeof(Uri), typeof(Glyphs),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the FontRenderingEmSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontRenderingEmSizeProperty =
        DependencyProperty.Register(nameof(FontRenderingEmSize), typeof(double), typeof(Glyphs),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the StyleSimulations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StyleSimulationsProperty =
        DependencyProperty.Register(nameof(StyleSimulations), typeof(StyleSimulations), typeof(Glyphs),
            new PropertyMetadata(StyleSimulations.None));

    /// <summary>
    /// Identifies the UnicodeString dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UnicodeStringProperty =
        DependencyProperty.Register(nameof(UnicodeString), typeof(string), typeof(Glyphs),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Indices dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty IndicesProperty =
        DependencyProperty.Register(nameof(Indices), typeof(string), typeof(Glyphs),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Fill dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Glyphs),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the OriginX dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OriginXProperty =
        DependencyProperty.Register(nameof(OriginX), typeof(double), typeof(Glyphs),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the OriginY dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OriginYProperty =
        DependencyProperty.Register(nameof(OriginY), typeof(double), typeof(Glyphs),
            new PropertyMetadata(double.NaN));

    public static readonly DependencyProperty CaretStopsProperty =
        DependencyProperty.Register(nameof(CaretStops), typeof(string), typeof(Glyphs), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DeviceFontNameProperty =
        DependencyProperty.Register(nameof(DeviceFontName), typeof(string), typeof(Glyphs), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSidewaysProperty =
        DependencyProperty.Register(nameof(IsSideways), typeof(bool), typeof(Glyphs), new PropertyMetadata(false));

    public static readonly DependencyProperty BidiLevelProperty =
        DependencyProperty.Register(nameof(BidiLevel), typeof(int), typeof(Glyphs), new PropertyMetadata(0));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the URI of the font to render.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public Uri? FontUri
    {
        get => (Uri?)GetValue(FontUriProperty);
        set => SetValue(FontUriProperty, value);
    }

    /// <summary>
    /// Gets or sets the em size used for rendering the glyphs.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontRenderingEmSize
    {
        get => (double)GetValue(FontRenderingEmSizeProperty)!;
        set => SetValue(FontRenderingEmSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the StyleSimulations for the glyphs.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StyleSimulations StyleSimulations
    {
        get => (StyleSimulations)(GetValue(StyleSimulationsProperty) ?? StyleSimulations.None);
        set => SetValue(StyleSimulationsProperty, value);
    }

    /// <summary>
    /// Gets or sets the Unicode string to render.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string UnicodeString
    {
        get => (string)(GetValue(UnicodeStringProperty) ?? string.Empty);
        set => SetValue(UnicodeStringProperty, value);
    }

    /// <summary>
    /// Gets or sets the glyph indices string.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string Indices
    {
        get => (string)(GetValue(IndicesProperty) ?? string.Empty);
        set => SetValue(IndicesProperty, value);
    }

    /// <summary>
    /// Gets or sets the Brush used to render the glyphs.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the origin X of the glyph run.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double OriginX
    {
        get => (double)GetValue(OriginXProperty)!;
        set => SetValue(OriginXProperty, value);
    }

    /// <summary>
    /// Gets or sets the origin Y of the glyph run.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double OriginY
    {
        get => (double)GetValue(OriginYProperty)!;
        set => SetValue(OriginYProperty, value);
    }

    public string CaretStops
    {
        get => (string)(GetValue(CaretStopsProperty) ?? string.Empty);
        set => SetValue(CaretStopsProperty, value);
    }

    public string DeviceFontName
    {
        get => (string)(GetValue(DeviceFontNameProperty) ?? string.Empty);
        set => SetValue(DeviceFontNameProperty, value);
    }

    public bool IsSideways
    {
        get => (bool)(GetValue(IsSidewaysProperty) ?? false);
        set => SetValue(IsSidewaysProperty, value);
    }

    public int BidiLevel
    {
        get => (int)(GetValue(BidiLevelProperty) ?? 0);
        set => SetValue(BidiLevelProperty, value);
    }

    Uri? IUriContext.BaseUri
    {
        get => _baseUri;
        set => _baseUri = value;
    }

    /// <summary>Builds a portable glyph run from the fixed-format glyph attributes.</summary>
    public GlyphRun ToGlyphRun()
    {
        var characters = (UnicodeString ?? string.Empty).ToCharArray();
        var glyphIndices = characters.Select(static character => (ushort)character).ToArray();
        var advance = Math.Max(0.0, FontRenderingEmSize * 0.5);
        var run = new GlyphRun
        {
            FontRenderingEmSize = FontRenderingEmSize,
            BaselineOrigin = new Point(
                double.IsNaN(OriginX) ? 0.0 : OriginX,
                double.IsNaN(OriginY) ? 0.0 : OriginY),
            GlyphIndices = glyphIndices,
            AdvanceWidths = Enumerable.Repeat(advance, glyphIndices.Length).ToArray(),
            Characters = characters,
            DeviceFontName = DeviceFontName,
            BidiLevel = BidiLevel,
            IsSideways = IsSideways,
            CaretStops = ParseCaretStops(CaretStops, characters.Length),
        };

        if (FontUri != null)
        {
            run.GlyphTypeface = new GlyphTypeface(FontUri);
        }

        return run;
    }

    private static IList<bool> ParseCaretStops(string? text, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Enumerable.Repeat(true, length + 1).ToArray();
        }

        var values = text
            .Where(static character => character is '0' or '1')
            .Select(static character => character == '1')
            .ToList();
        while (values.Count < length + 1)
        {
            values.Add(true);
        }
        return values;
    }

    #endregion
}
