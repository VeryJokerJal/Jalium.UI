using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a low-level element for displaying glyphs.
/// </summary>
public class Glyphs : FrameworkElement
{
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

    #endregion
}

/// <summary>
/// Specifies style simulations for a font.
/// </summary>
[Flags]
public enum StyleSimulations
{
    /// <summary>
    /// No style simulation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Bold simulation.
    /// </summary>
    BoldSimulation = 1,

    /// <summary>
    /// Italic simulation.
    /// </summary>
    ItalicSimulation = 2,

    /// <summary>
    /// Bold and italic simulation.
    /// </summary>
    BoldItalicSimulation = BoldSimulation | ItalicSimulation
}
