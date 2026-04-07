using System.Globalization;
using System.Runtime.InteropServices;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a common dialog box that allows the user to choose a font.
/// </summary>
public sealed class FontDialog
{
    private static readonly string DefaultFontFamilyName = FrameworkElement.DefaultFontFamilyName;
    private const int DefaultLogicalDpi = 96;
    private const byte DEFAULT_CHARSET = 1;
    private const byte DEFAULT_PITCH = 0;
    private const byte FIXED_PITCH = 1;
    private const int LF_FACESIZE = 32;

    private const uint CF_SCREENFONTS = 0x00000001;
    private const uint CF_INITTOLOGFONTSTRUCT = 0x00000040;
    private const uint CF_EFFECTS = 0x00000100;
    private const uint CF_NOVECTORFONTS = 0x00000800;
    private const uint CF_NOSIMULATIONS = 0x00001000;
    private const uint CF_LIMITSIZE = 0x00002000;
    private const uint CF_FIXEDPITCHONLY = 0x00004000;
    private const uint CF_FORCEFONTEXIST = 0x00010000;
    private const uint CF_NOSCRIPTSEL = 0x00800000;
    private const uint CF_NOVERTFONTS = 0x01000000;

    private const int LOGPIXELSY = 90;

    private static readonly Lazy<IReadOnlyList<FontFamily>> s_systemFontFamilies = new(LoadSystemFontFamilies);
    private static readonly double[] s_standardFontSizes =
    {
        8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72
    };

    private double _fontSize = 12.0;
    private double _minSize = 1.0;
    private double _maxSize = 500.0;

    #region Properties

    /// <summary>
    /// Gets or sets the selected font family.
    /// </summary>
    public FontFamily? FontFamily { get; set; }

    /// <summary>
    /// Gets or sets the selected font size.
    /// </summary>
    public double FontSize
    {
        get => _fontSize;
        set => _fontSize = CoerceFontSize(value, _minSize, _maxSize);
    }

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
    public double MinSize
    {
        get => _minSize;
        set
        {
            _minSize = NormalizeLowerSizeBound(value);
            if (_maxSize < _minSize)
            {
                _maxSize = _minSize;
            }

            _fontSize = CoerceFontSize(_fontSize, _minSize, _maxSize);
        }
    }

    /// <summary>
    /// Gets or sets the maximum font size allowed.
    /// </summary>
    public double MaxSize
    {
        get => _maxSize;
        set
        {
            _maxSize = NormalizeUpperSizeBound(value, _minSize);
            _fontSize = CoerceFontSize(_fontSize, _minSize, _maxSize);
        }
    }

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
        return ShowDialogInternal(Jalium.UI.Application.Current?.MainWindow);
    }

    /// <summary>
    /// Displays the font dialog with the specified owner window.
    /// </summary>
    public bool ShowDialog(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
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
        return s_standardFontSizes;
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Shows the dialog internally.
    /// </summary>
    private bool ShowDialogInternal(Window? owner = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ShowWindowsDialog(DialogOwnerResolver.Resolve(owner?.Handle ?? nint.Zero));
        }

        return ShowFallbackDialog();
    }

    /// <summary>
    /// Gets system font families.
    /// </summary>
    private static IEnumerable<FontFamily> GetSystemFontFamilies()
    {
        return s_systemFontFamilies.Value;
    }

    #endregion

    #region Internal Helpers

    internal static (bool Underline, bool Strikeout) GetDialogEffects(TextDecorationCollection? decorations)
    {
        return (
            decorations?.HasDecoration(TextDecorationLocation.Underline) == true,
            decorations?.HasDecoration(TextDecorationLocation.Strikethrough) == true);
    }

    internal static TextDecorationCollection? UpdateDialogTextDecorations(
        TextDecorationCollection? existing,
        bool underline,
        bool strikeout)
    {
        var merged = existing is { Count: > 0 }
            ? new TextDecorationCollection(
                existing
                    .Where(static decoration =>
                        decoration.Location != TextDecorationLocation.Underline &&
                        decoration.Location != TextDecorationLocation.Strikethrough)
                    .Select(CloneTextDecoration))
            : new TextDecorationCollection();

        if (underline)
        {
            merged.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        if (strikeout)
        {
            merged.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
        }

        return merged.Count > 0 ? merged : null;
    }

    #endregion

    #region Platform Implementations

    private bool ShowWindowsDialog(nint ownerHandle)
    {
        var logFont = CreateLogFont();
        var logFontSize = Marshal.SizeOf<LOGFONT>();
        var logFontPointer = Marshal.AllocHGlobal(logFontSize);

        try
        {
            Marshal.StructureToPtr(logFont, logFontPointer, false);

            var minSize = (int)Math.Ceiling(MinSize);
            var maxSize = Math.Max(minSize, (int)Math.Floor(MaxSize));

            var chooseFont = new CHOOSEFONT
            {
                lStructSize = Marshal.SizeOf<CHOOSEFONT>(),
                hwndOwner = ownerHandle,
                lpLogFont = logFontPointer,
                iPointSize = (int)Math.Round(FontSize * 10.0, MidpointRounding.AwayFromZero),
                Flags = BuildChooseFontFlags(),
                rgbColors = ToColorRef(Color),
                nSizeMin = minSize,
                nSizeMax = maxSize
            };

            if (!ChooseFont(ref chooseFont))
            {
                var error = CommDlgExtendedError();
                if (error != 0)
                {
                    throw new InvalidOperationException($"ChooseFont failed with error 0x{error:X8}.");
                }

                return false;
            }

            logFont = Marshal.PtrToStructure<LOGFONT>(logFontPointer);
            ApplyDialogSelection(logFont, chooseFont.iPointSize, chooseFont.rgbColors);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(logFontPointer);
        }
    }

    private bool ShowFallbackDialog()
    {
        Console.WriteLine("Font Dialog");
        Console.WriteLine($"Sample: {SampleText}");
        Console.Write($"Font family [{FontFamily?.Source ?? DefaultFontFamilyName}] (blank to cancel): ");

        var familyInput = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(familyInput))
        {
            return false;
        }

        FontFamily = new FontFamily(familyInput.Trim());

        Console.Write($"Font size [{FontSize.ToString("0.##", CultureInfo.InvariantCulture)}]: ");
        var sizeInput = Console.ReadLine();
        if (TryParseFontSize(sizeInput, out var size))
        {
            FontSize = size;
        }

        if (ShowEffects)
        {
            var effects = GetDialogEffects(TextDecorations);
            var bold = PromptYesNo("Bold", FontWeight >= FontWeights.Bold);
            var italic = PromptYesNo("Italic", FontStyle == FontStyles.Italic || FontStyle == FontStyles.Oblique);
            var underline = PromptYesNo("Underline", effects.Underline);
            var strikeout = PromptYesNo("Strikeout", effects.Strikeout);

            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
            TextDecorations = UpdateDialogTextDecorations(TextDecorations, underline, strikeout);
        }

        if (ShowColor)
        {
            Console.Write($"Font color [{Color}] (#RRGGBB or #AARRGGBB): ");
            var colorInput = Console.ReadLine();
            if (TryParseDialogColor(colorInput, out var selectedColor))
            {
                Color = selectedColor;
            }
        }

        return true;
    }

    #endregion

    #region Private Methods

    private void ApplyDialogSelection(LOGFONT logFont, int pointSizeTenths, uint colorRef)
    {
        FontFamily = new FontFamily(string.IsNullOrWhiteSpace(logFont.lfFaceName) ? DefaultFontFamilyName : logFont.lfFaceName);
        FontSize = pointSizeTenths > 0 ? pointSizeTenths / 10.0 : FontSize;
        FontWeight = logFont.lfWeight > 0 ? FontWeight.FromOpenTypeWeight(logFont.lfWeight) : FontWeights.Normal;
        FontStyle = logFont.lfItalic != 0 ? FontStyles.Italic : FontStyles.Normal;

        if (ShowEffects)
        {
            TextDecorations = UpdateDialogTextDecorations(
                TextDecorations,
                underline: logFont.lfUnderline != 0,
                strikeout: logFont.lfStrikeOut != 0);
        }

        if (ShowColor)
        {
            Color = FromColorRef(colorRef);
        }
    }

    private LOGFONT CreateLogFont()
    {
        var effects = GetDialogEffects(TextDecorations);
        return new LOGFONT
        {
            lfHeight = GetLogicalFontHeight(FontSize),
            lfWeight = FontWeight.ToOpenTypeWeight(),
            lfItalic = (byte)(FontStyle == FontStyles.Italic || FontStyle == FontStyles.Oblique ? 1 : 0),
            lfUnderline = (byte)(effects.Underline ? 1 : 0),
            lfStrikeOut = (byte)(effects.Strikeout ? 1 : 0),
            lfCharSet = DEFAULT_CHARSET,
            lfPitchAndFamily = FixedPitchOnly ? FIXED_PITCH : DEFAULT_PITCH,
            lfFaceName = TrimDialogFaceName(FontFamily?.Source ?? DefaultFontFamilyName)
        };
    }

    private uint BuildChooseFontFlags()
    {
        var flags = CF_SCREENFONTS | CF_INITTOLOGFONTSTRUCT | CF_FORCEFONTEXIST;

        if (ShowEffects || ShowColor)
        {
            // The Win32 common font dialog couples color, underline, and strikeout controls behind CF_EFFECTS.
            flags |= CF_EFFECTS;
        }

        if (FixedPitchOnly)
        {
            flags |= CF_FIXEDPITCHONLY;
        }

        if (MinSize > 0 || MaxSize < int.MaxValue)
        {
            flags |= CF_LIMITSIZE;
        }

        if (!AllowScriptChange)
        {
            flags |= CF_NOSCRIPTSEL;
        }

        if (!AllowVectorFonts)
        {
            flags |= CF_NOVECTORFONTS;
        }

        if (!AllowVerticalFonts)
        {
            flags |= CF_NOVERTFONTS;
        }

        if (!AllowSimulations)
        {
            flags |= CF_NOSIMULATIONS;
        }

        return flags;
    }

    private static int GetLogicalFontHeight(double pointSize)
    {
        var dpi = DefaultLogicalDpi;
        var desktopDc = GetDC(nint.Zero);

        try
        {
            if (desktopDc != nint.Zero)
            {
                dpi = GetDeviceCaps(desktopDc, LOGPIXELSY);
            }
        }
        finally
        {
            if (desktopDc != nint.Zero)
            {
                ReleaseDC(nint.Zero, desktopDc);
            }
        }

        var clampedSize = CoerceFontSize(pointSize, 1.0, double.MaxValue);
        return -(int)Math.Round((clampedSize * dpi) / 72.0, MidpointRounding.AwayFromZero);
    }

    private static string TrimDialogFaceName(string faceName)
    {
        var primaryName = faceName
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(primaryName))
        {
            primaryName = DefaultFontFamilyName;
        }

        return primaryName.Length >= LF_FACESIZE
            ? primaryName[..(LF_FACESIZE - 1)]
            : primaryName;
    }

    private static bool PromptYesNo(string prompt, bool defaultValue)
    {
        Console.Write($"{prompt} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        var response = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(response))
        {
            return defaultValue;
        }

        return response.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFontSize(string? input, out double size)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            size = default;
            return false;
        }

        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out size) ||
               double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out size);
    }

    private static bool TryParseDialogColor(string? input, out Color color)
    {
        if (ColorConverter.ConvertFromString(input ?? string.Empty) is Color parsedColor)
        {
            color = parsedColor;
            return true;
        }

        color = default;
        return false;
    }

    private static double CoerceFontSize(double value, double minSize, double maxSize)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minSize;
        }

        return Math.Clamp(value, minSize, maxSize);
    }

    private static double NormalizeLowerSizeBound(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Max(1.0, value);
    }

    private static double NormalizeUpperSizeBound(double value, double minSize)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return minSize;
        }

        return Math.Max(minSize, value);
    }

    private static IReadOnlyList<FontFamily> LoadSystemFontFamilies()
    {
        try
        {
            var names = Helpers.FontEnumerationHelper.EnumerateSystemFontFamilies();
            if (names != null && names.Length > 0)
            {
                return names
                    .Where(static name => !name.StartsWith('@'))
                    .Select(static name => new FontFamily(name))
                    .ToArray();
            }
        }
        catch
        {
            // Fall back to the framework defaults below if native font enumeration is unavailable.
        }

        return Fonts.SystemFontFamilies
            .Select(static family => new FontFamily(family.Source))
            .Distinct()
            .OrderBy(static family => family.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }

    private static Color FromColorRef(uint colorRef)
    {
        return Color.FromRgb(
            (byte)(colorRef & 0xFF),
            (byte)((colorRef >> 8) & 0xFF),
            (byte)((colorRef >> 16) & 0xFF));
    }

    private static TextDecoration CloneTextDecoration(TextDecoration decoration)
    {
        return new TextDecoration
        {
            Location = decoration.Location,
            Brush = decoration.Brush,
            Thickness = decoration.Thickness,
            Offset = decoration.Offset,
            OffsetUnit = decoration.OffsetUnit,
            ThicknessUnit = decoration.ThicknessUnit
        };
    }

    #endregion

    #region Native Methods

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHOOSEFONT
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hDC;
        public nint lpLogFont;
        public int iPointSize;
        public uint Flags;
        public uint rgbColors;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint hInstance;
        public string? lpszStyle;
        public ushort nFontType;
        public ushort __MISSING_ALIGNMENT__;
        public int nSizeMin;
        public int nSizeMax;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = LF_FACESIZE)]
        public string lfFaceName;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChooseFont(ref CHOOSEFONT chooseFont);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint windowHandle, nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint deviceContext, int index);

    #endregion
}
