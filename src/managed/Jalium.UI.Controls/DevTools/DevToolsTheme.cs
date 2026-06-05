using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Centralised palette + type scale for every DevTools surface.
///
/// Design language: "Instrument" — a precision laboratory instrument / oscilloscope
/// for inspecting a GPU rendering engine. Near-black graphite ground, a single warm
/// "signal amber" accent (like a needle / phosphor trace), hairline rules, Bahnschrift
/// uppercase technical labels and Cascadia Code for every piece of live data. Depth is
/// expressed through tonal layering and hairlines, never drop shadows.
///
/// All panels pull their brushes/fonts/metrics from here, so a single edit re-skins the
/// whole tool window. Tints that derive from these colors (Color.FromArgb(a, X.R, X.G,
/// X.B)) cascade automatically too.
/// </summary>
internal static class DevToolsTheme
{
    // --- Surface palette ---------------------------------------------------
    // Five layers of depth, from window chrome down to inline controls. Cool
    // graphite so the warm amber signal reads as the only temperature in the room.
    public static readonly Color ChromeColor       = Color.FromRgb(0x0B, 0x0C, 0x0F); // window chrome / toolbars / tab strip
    public static readonly Color SurfaceColor       = Color.FromRgb(0x11, 0x13, 0x17); // primary tab surface
    public static readonly Color SurfaceAltColor    = Color.FromRgb(0x17, 0x1A, 0x20); // cards / secondary pane
    public static readonly Color SurfaceRaisedColor = Color.FromRgb(0x1D, 0x21, 0x2A); // hero panels / active rows
    public static readonly Color RowAltColor        = Color.FromRgb(0x1B, 0x1F, 0x27); // zebra striping
    public static readonly Color ControlColor       = Color.FromRgb(0x21, 0x26, 0x2F); // input/button idle bg

    // --- Lines & dividers --------------------------------------------------
    public static readonly Color BorderColor       = Color.FromRgb(0x2B, 0x31, 0x3B);
    public static readonly Color BorderSubtleColor = Color.FromRgb(0x1E, 0x22, 0x2A);
    public static readonly Color BorderStrongColor = Color.FromRgb(0x3B, 0x43, 0x4F);
    public static readonly Color GridLineColor     = Color.FromRgb(0x16, 0x19, 0x1F); // faint instrument grid

    // --- Text hierarchy ----------------------------------------------------
    public static readonly Color TextPrimaryColor   = Color.FromRgb(0xDD, 0xE2, 0xE9);
    public static readonly Color TextSecondaryColor = Color.FromRgb(0x94, 0x9C, 0xA8);
    public static readonly Color TextMutedColor     = Color.FromRgb(0x64, 0x6C, 0x79);
    public static readonly Color TextDisabledColor  = Color.FromRgb(0x45, 0x4B, 0x55);

    // --- Accent: SIGNAL AMBER ---------------------------------------------
    // The single interactive / "you are here" / live-data colour. Used sparingly
    // and with intent — selection, active tab, primary action, focus, the needle.
    public static readonly Color AccentColor        = Color.FromRgb(0xFF, 0xB2, 0x2E);
    public static readonly Color AccentHoverColor   = Color.FromRgb(0xFF, 0xC7, 0x5C);
    public static readonly Color AccentPressedColor = Color.FromRgb(0xE2, 0x99, 0x1B);
    public static readonly Color AccentSoftColor    = Color.FromArgb(0x2E, 0xFF, 0xB2, 0x2E); // active-fill wash

    // --- Semantic palette (logs, stats, diagnostics) ----------------------
    public static readonly Color SuccessColor = Color.FromRgb(0x56, 0xD9, 0x8D); // phosphor green
    public static readonly Color WarningColor = Color.FromRgb(0xED, 0xA6, 0x3C); // caution amber-orange
    public static readonly Color ErrorColor   = Color.FromRgb(0xF2, 0x65, 0x5C);
    public static readonly Color InfoColor    = Color.FromRgb(0x5F, 0xB7, 0xCB); // muted gauge cyan

    // --- Syntax-ish tokens (Cascadia Code data) ---------------------------
    // Warm-tuned for graphite so values harmonize with the amber signal rather
    // than fighting it (no stock VS Code Dark+ blues).
    public static readonly Color TokenStringColor   = Color.FromRgb(0xD7, 0xA2, 0x6B);
    public static readonly Color TokenNumberColor   = Color.FromRgb(0x8F, 0xD6, 0x9E);
    public static readonly Color TokenKeywordColor  = Color.FromRgb(0xC9, 0x9B, 0xD6);
    public static readonly Color TokenTypeColor     = Color.FromRgb(0x5B, 0xC8, 0xBE);
    public static readonly Color TokenBoolColor     = Color.FromRgb(0x5F, 0xB7, 0xCB);
    public static readonly Color TokenPropertyColor = Color.FromRgb(0xB7, 0xC3, 0xD2);
    public static readonly Color TokenEnumColor     = Color.FromRgb(0xE0, 0xC3, 0x6A);

    // --- Brushes (pre-allocated, shared) ----------------------------------
    public static readonly SolidColorBrush Chrome        = new(ChromeColor);
    public static readonly SolidColorBrush Surface       = new(SurfaceColor);
    public static readonly SolidColorBrush SurfaceAlt    = new(SurfaceAltColor);
    public static readonly SolidColorBrush SurfaceRaised = new(SurfaceRaisedColor);
    public static readonly SolidColorBrush RowAlt        = new(RowAltColor);
    public static readonly SolidColorBrush Control        = new(ControlColor);
    public static readonly SolidColorBrush ControlHover   = new(Color.FromRgb(0x2A, 0x30, 0x39));
    public static readonly SolidColorBrush ControlPressed = new(Color.FromRgb(0x19, 0x1D, 0x24));
    public static readonly SolidColorBrush Border         = new(BorderColor);
    public static readonly SolidColorBrush BorderSubtle   = new(BorderSubtleColor);
    public static readonly SolidColorBrush BorderStrong   = new(BorderStrongColor);
    public static readonly SolidColorBrush GridLine       = new(GridLineColor);

    public static readonly SolidColorBrush TextPrimary   = new(TextPrimaryColor);
    public static readonly SolidColorBrush TextSecondary = new(TextSecondaryColor);
    public static readonly SolidColorBrush TextMuted     = new(TextMutedColor);
    public static readonly SolidColorBrush TextDisabled  = new(TextDisabledColor);

    public static readonly SolidColorBrush Accent        = new(AccentColor);
    public static readonly SolidColorBrush AccentHover   = new(AccentHoverColor);
    public static readonly SolidColorBrush AccentPressed = new(AccentPressedColor);
    public static readonly SolidColorBrush AccentSoft    = new(AccentSoftColor);

    public static readonly SolidColorBrush Success = new(SuccessColor);
    public static readonly SolidColorBrush Warning = new(WarningColor);
    public static readonly SolidColorBrush Error   = new(ErrorColor);
    public static readonly SolidColorBrush Info    = new(InfoColor);

    public static readonly SolidColorBrush TokenString   = new(TokenStringColor);
    public static readonly SolidColorBrush TokenNumber   = new(TokenNumberColor);
    public static readonly SolidColorBrush TokenKeyword  = new(TokenKeywordColor);
    public static readonly SolidColorBrush TokenType     = new(TokenTypeColor);
    public static readonly SolidColorBrush TokenBool     = new(TokenBoolColor);
    public static readonly SolidColorBrush TokenProperty = new(TokenPropertyColor);
    public static readonly SolidColorBrush TokenEnum     = new(TokenEnumColor);

    // --- Type scale --------------------------------------------------------
    // Body stays on the proven system UI font. Character comes from the display
    // and mono families, which both ship with Windows 11 (graceful family-name
    // fallback if absent).
    public static readonly FontFamily UiFont          = new(FrameworkElement.DefaultFontFamilyName);
    public static readonly FontFamily MonoFont         = new("Cascadia Code"); // all live data / values / REPL / tree
    public static readonly FontFamily DisplayFont      = new("Bahnschrift SemiBold"); // uppercase technical labels
    public static readonly FontFamily DisplayFontLight = new("Bahnschrift");           // large light readouts

    // Explicit constant sizes — use these instead of inlined literals.
    public const double FontXS   = 10;  // badge / meta
    public const double FontSm   = 11;  // secondary body / table cell
    public const double FontBase = 12;  // default body
    public const double FontLg   = 13;  // section title
    public const double FontXL   = 15;  // panel heading
    public const double Font2XL  = 20;  // sub-readout
    public const double FontHero = 30;  // hero readout (FPS, distance)

    // --- Spacing -----------------------------------------------------------
    public const double GutterXS  = 2;
    public const double GutterSm  = 4;
    public const double GutterBase = 8;
    public const double GutterLg  = 12;
    public const double GutterXL  = 16;
    public const double Gutter2XL = 24;

    // --- Radii -------------------------------------------------------------
    public static readonly CornerRadius RadiusSm   = new(3);
    public static readonly CornerRadius RadiusBase = new(4);
    public static readonly CornerRadius RadiusLg   = new(7);

    // --- Thicknesses (single object reuse where possible) -----------------
    public static readonly Thickness ThicknessHairline = new(1);
    public static readonly Thickness ThicknessBottom   = new(0, 0, 0, 1);
    public static readonly Thickness ThicknessRight    = new(0, 0, 1, 0);
    public static readonly Thickness ThicknessLeftRail = new(2, 0, 0, 0); // accent rail on hero/tool cards

    // --- Atmosphere --------------------------------------------------------
    // A faint repeating scanline wash for chrome strips (toolbars / status bar),
    // evoking a phosphor instrument front panel. ~3px period; if the renderer
    // doesn't honour absolute-tiled gradients it degrades to a barely-visible
    // sheen rather than anything broken.
    public static readonly LinearGradientBrush Scanline = CreateScanline();

    private static LinearGradientBrush CreateScanline()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 3),
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Repeat,
        };
        var clear = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        var line  = Color.FromArgb(0x16, 0x00, 0x00, 0x00);
        brush.GradientStops.Add(new GradientStop(clear, 0.0));
        brush.GradientStops.Add(new GradientStop(line, 0.5));
        brush.GradientStops.Add(new GradientStop(clear, 1.0));
        return brush;
    }
}
