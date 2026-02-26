using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a common dialog box that allows the user to choose a font.
/// </summary>
public sealed class FontDialog
{
    #region Properties

    /// <summary>
    /// Gets or sets the selected font family.
    /// </summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the selected font size.
    /// </summary>
    public double FontSize { get; set; } = 12.0;

    /// <summary>
    /// Gets or sets the selected font style.
    /// </summary>
    public FontStyle FontStyle { get; set; } = FontStyles.Normal;

    /// <summary>
    /// Gets or sets the selected font weight.
    /// </summary>
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;

    /// <summary>
    /// Gets or sets the selected font stretch.
    /// </summary>
    public FontStretch FontStretch { get; set; } = FontStretches.Normal;

    /// <summary>
    /// Gets or sets the selected text decorations.
    /// </summary>
    public TextDecorationCollection? TextDecorations { get; set; }

    /// <summary>
    /// Gets or sets the selected font color.
    /// </summary>
    public Color Color { get; set; } = Color.Black;

    /// <summary>
    /// Gets or sets a value indicating whether the color selection is shown.
    /// </summary>
    public bool ShowColor { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the effects (strikeout, underline) are shown.
    /// </summary>
    public bool ShowEffects { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether only fixed pitch fonts are shown.
    /// </summary>
    public bool FixedPitchOnly { get; set; }

    /// <summary>
    /// Gets or sets the minimum font size allowed.
    /// </summary>
    public double MinSize { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the maximum font size allowed.
    /// </summary>
    public double MaxSize { get; set; } = 500.0;

    /// <summary>
    /// Gets or sets a value indicating whether script selection is enabled.
    /// </summary>
    public bool AllowScriptChange { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether vector fonts are shown.
    /// </summary>
    public bool AllowVectorFonts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether vertical fonts are shown.
    /// </summary>
    public bool AllowVerticalFonts { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether simulated fonts are shown.
    /// </summary>
    public bool AllowSimulations { get; set; } = true;

    /// <summary>
    /// Gets or sets the sample text displayed in the preview.
    /// </summary>
    public string SampleText { get; set; } = "AaBbYyZz";

    #endregion

    #region Methods

    /// <summary>
    /// Displays the font dialog.
    /// </summary>
    /// <returns>True if the user clicked OK; otherwise, false.</returns>
    public bool ShowDialog()
    {
        return ShowDialogInternal();
    }

    /// <summary>
    /// Displays the font dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(Window owner)
    {
        return ShowDialogInternal(owner);
    }

    /// <summary>
    /// Gets the list of available font families.
    /// </summary>
    public static IEnumerable<FontFamily> GetFontFamilies()
    {
        return GetSystemFontFamilies();
    }

    /// <summary>
    /// Gets a list of available font sizes.
    /// </summary>
    public static IEnumerable<double> GetStandardFontSizes()
    {
        return new double[]
        {
            8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72
        };
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Shows the dialog internally.
    /// </summary>
    protected bool ShowDialogInternal(Window? owner = null)
    {
        // Platform-specific implementation
        // Would use Windows ChooseFont or custom dialog
        return false;
    }

    /// <summary>
    /// Gets system font families.
    /// </summary>
    protected static IEnumerable<FontFamily> GetSystemFontFamilies()
    {
        // Platform-specific implementation to enumerate fonts
        // Would use DirectWrite or GDI+ to get installed fonts
        return Enumerable.Empty<FontFamily>();
    }

    #endregion
}
