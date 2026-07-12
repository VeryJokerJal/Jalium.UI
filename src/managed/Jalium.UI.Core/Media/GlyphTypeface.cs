using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>Describes one physical font face and its glyph metrics.</summary>
public sealed class GlyphTypeface : ISupportInitialize
{
    private readonly Dictionary<int, ushort> _characterToGlyphMap = new();
    private readonly Dictionary<ushort, double> _advanceHeights = new();
    private readonly Dictionary<ushort, double> _advanceWidths = new();
    private readonly Dictionary<ushort, double> _bottomSideBearings = new();
    private readonly Dictionary<ushort, double> _baselineDistances = new();
    private readonly Dictionary<ushort, double> _leftSideBearings = new();
    private readonly Dictionary<ushort, double> _rightSideBearings = new();
    private readonly Dictionary<ushort, double> _topSideBearings = new();
    private readonly Dictionary<CultureInfo, string> _copyrights = new();
    private readonly Dictionary<CultureInfo, string> _descriptions = new();
    private readonly Dictionary<CultureInfo, string> _designerNames = new();
    private readonly Dictionary<CultureInfo, string> _designerUrls = new();
    private readonly Dictionary<CultureInfo, string> _faceNames = new();
    private readonly Dictionary<CultureInfo, string> _familyNames = new();
    private readonly Dictionary<CultureInfo, string> _licenseDescriptions = new();
    private readonly Dictionary<CultureInfo, string> _manufacturerNames = new();
    private readonly Dictionary<CultureInfo, string> _sampleTexts = new();
    private readonly Dictionary<CultureInfo, string> _trademarks = new();
    private readonly Dictionary<CultureInfo, string> _vendorUrls = new();
    private readonly Dictionary<CultureInfo, string> _versionStrings = new();
    private readonly Dictionary<CultureInfo, string> _win32FaceNames = new();
    private readonly Dictionary<CultureInfo, string> _win32FamilyNames = new();
    private bool _initializing;
    private Uri? _fontUri;
    private StyleSimulations _styleSimulations;

    public GlyphTypeface()
    {
        InitializeDefaultMetrics();
    }

    public GlyphTypeface(Uri typefaceSource)
        : this(typefaceSource, StyleSimulations.None)
    {
    }

    public GlyphTypeface(Uri typefaceSource, StyleSimulations styleSimulations)
    {
        ArgumentNullException.ThrowIfNull(typefaceSource);
        ValidateStyleSimulations(styleSimulations, nameof(styleSimulations));
        _fontUri = typefaceSource;
        _styleSimulations = styleSimulations;
        InitializeDefaultMetrics();
        PopulateNamesFromUri(typefaceSource);
    }

    public IDictionary<ushort, double> AdvanceHeights => _advanceHeights;

    public IDictionary<ushort, double> AdvanceWidths => _advanceWidths;

    public double Baseline { get; internal set; } = 0.8;

    public IDictionary<ushort, double> BottomSideBearings => _bottomSideBearings;

    public double CapsHeight { get; internal set; } = 0.7;

    public IDictionary<int, ushort> CharacterToGlyphMap => _characterToGlyphMap;

    public IDictionary<CultureInfo, string> Copyrights => _copyrights;

    public IDictionary<CultureInfo, string> Descriptions => _descriptions;

    public IDictionary<CultureInfo, string> DesignerNames => _designerNames;

    public IDictionary<CultureInfo, string> DesignerUrls => _designerUrls;

    public IDictionary<ushort, double> DistancesFromHorizontalBaselineToBlackBoxBottom => _baselineDistances;

    public FontEmbeddingRight EmbeddingRights { get; internal set; } = FontEmbeddingRight.Installable;

    public IDictionary<CultureInfo, string> FaceNames => _faceNames;

    public string FaceName { get; internal set; } = string.Empty;

    public IDictionary<CultureInfo, string> FamilyNames => _familyNames;

    public string FamilyName { get; internal set; } = string.Empty;

    public Uri? FontUri
    {
        get => _fontUri;
        set
        {
            if (!_initializing)
            {
                throw new InvalidOperationException("FontUri can only be changed during initialization.");
            }

            _fontUri = value;
            if (value is not null)
            {
                PopulateNamesFromUri(value);
            }
        }
    }

    public int GlyphCount => _advanceWidths.Count;

    public double Height { get; internal set; } = 1.2;

    public IDictionary<ushort, double> LeftSideBearings => _leftSideBearings;

    public IDictionary<CultureInfo, string> LicenseDescriptions => _licenseDescriptions;

    public IDictionary<CultureInfo, string> ManufacturerNames => _manufacturerNames;

    public IDictionary<ushort, double> RightSideBearings => _rightSideBearings;

    public IDictionary<CultureInfo, string> SampleTexts => _sampleTexts;

    public FontStretch Stretch { get; internal set; } = FontStretches.Normal;

    public double StrikethroughPosition { get; internal set; } = 0.31;

    public double StrikethroughThickness { get; internal set; } = 0.05;

    public FontStyle Style { get; internal set; } = FontStyles.Normal;

    public StyleSimulations StyleSimulations
    {
        get => _styleSimulations;
        set
        {
            if (!_initializing)
            {
                throw new InvalidOperationException("StyleSimulations can only be changed during initialization.");
            }

            ValidateStyleSimulations(value, nameof(value));
            _styleSimulations = value;
        }
    }

    public bool Symbol { get; internal set; }

    public IDictionary<CultureInfo, string> Trademarks => _trademarks;

    public IDictionary<ushort, double> TopSideBearings => _topSideBearings;

    public double UnderlinePosition { get; internal set; } = -0.1;

    public double UnderlineThickness { get; internal set; } = 0.05;

    public IDictionary<CultureInfo, string> VendorUrls => _vendorUrls;

    public double Version { get; internal set; } = 1.0;

    public IDictionary<CultureInfo, string> VersionStrings => _versionStrings;

    public FontWeight Weight { get; internal set; } = FontWeights.Normal;

    public IDictionary<CultureInfo, string> Win32FaceNames => _win32FaceNames;

    public IDictionary<CultureInfo, string> Win32FamilyNames => _win32FamilyNames;

    public double XHeight { get; internal set; } = 0.5;

    public byte[] ComputeSubset(ICollection<ushort> glyphs)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        if (glyphs.Count == 0)
        {
            throw new ArgumentException("At least one glyph is required.", nameof(glyphs));
        }

        foreach (ushort glyph in glyphs)
        {
            if (!_advanceWidths.ContainsKey(glyph))
            {
                throw new ArgumentOutOfRangeException(nameof(glyphs), $"Glyph {glyph} is not present in the typeface.");
            }
        }

        using Stream stream = GetFontStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public Stream GetFontStream()
    {
        if (_fontUri is null)
        {
            throw new InvalidOperationException("A FontUri has not been specified.");
        }

        string path = ResolveFontPath(_fontUri);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public Geometry GetGlyphOutline(ushort glyphIndex, double renderingEmSize, double hintingEmSize)
    {
        ValidateEmSize(renderingEmSize, nameof(renderingEmSize));
        ValidateEmSize(hintingEmSize, nameof(hintingEmSize));
        if (!_advanceWidths.TryGetValue(glyphIndex, out double advance))
        {
            return Geometry.Empty;
        }

        double width = Math.Max(0, advance * renderingEmSize);
        double height = Math.Max(0, Height * renderingEmSize);
        return width == 0 || height == 0
            ? Geometry.Empty
            : new RectangleGeometry(new Rect(0, -Baseline * renderingEmSize, width, height));
    }

    public double GetAdvanceWidth(int glyphIndex)
        => glyphIndex is >= 0 and <= ushort.MaxValue && _advanceWidths.TryGetValue((ushort)glyphIndex, out double width)
            ? width
            : 0.0;

    public bool TryGetGlyphIndex(int unicodeValue, out int glyphIndex)
    {
        if (_characterToGlyphMap.TryGetValue(unicodeValue, out ushort glyph))
        {
            glyphIndex = glyph;
            return true;
        }

        glyphIndex = 0;
        return false;
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
        if (_fontUri is null)
        {
            throw new InvalidOperationException("FontUri must be set before initialization completes.");
        }
    }

    private void InitializeDefaultMetrics()
    {
        for (int codePoint = 0; codePoint <= 255; codePoint++)
        {
            ushort glyph = (ushort)codePoint;
            _characterToGlyphMap[codePoint] = glyph;
            _advanceWidths[glyph] = codePoint switch
            {
                9 => 2.0,
                32 => 0.33,
                _ when codePoint >= 33 && codePoint <= 126 && char.IsPunctuation((char)codePoint) => 0.42,
                _ => 0.56,
            };
            _advanceHeights[glyph] = Height;
            _bottomSideBearings[glyph] = 0;
            _baselineDistances[glyph] = 0.2;
            _leftSideBearings[glyph] = 0;
            _rightSideBearings[glyph] = 0;
            _topSideBearings[glyph] = 0;
        }
    }

    private void PopulateNamesFromUri(Uri uri)
    {
        string source = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
        string family = Path.GetFileNameWithoutExtension(source);
        if (string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        FamilyName = family;
        FaceName = family;
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");
        _familyNames[culture] = family;
        _faceNames[culture] = family;
        _win32FamilyNames[culture] = family;
        _win32FaceNames[culture] = family;
        _versionStrings[culture] = Version.ToString(CultureInfo.InvariantCulture);
    }

    private static string ResolveFontPath(Uri uri)
    {
        if (uri.IsAbsoluteUri)
        {
            if (!uri.IsFile)
            {
                throw new NotSupportedException("Only file-backed font URIs can be opened by this renderer.");
            }

            return uri.LocalPath;
        }

        return Path.GetFullPath(uri.OriginalString);
    }

    private static void ValidateStyleSimulations(StyleSimulations value, string parameterName)
    {
        if ((value & ~StyleSimulations.BoldItalicSimulation) != 0)
        {
            throw new InvalidEnumArgumentException(parameterName, (int)value, typeof(StyleSimulations));
        }
    }

    private static void ValidateEmSize(double value, string parameterName)
    {
        if (!(value > 0) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
