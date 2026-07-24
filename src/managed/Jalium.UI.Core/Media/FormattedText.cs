using Jalium.UI.Media.Animation;
using Jalium.UI;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Media;

/// <summary>
/// Represents formatted text for rendering and measurement.
/// </summary>
public sealed partial class FormattedText
{
    /// <summary>
    /// Gets the text content.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the font family name.
    /// </summary>
    public string FontFamily { get; private set; }

    /// <summary>
    /// Gets the font size.
    /// </summary>
    public double FontSize { get; private set; }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the maximum width for text wrapping.
    /// </summary>
    public double MaxTextWidth
    {
        get => _maxTextWidth;
        set
        {
            ValidateMaxTextWidth(value, nameof(value));
            if (_maxTextWidth.Equals(value))
            {
                return;
            }

            _maxTextWidth = value;
            _maxTextWidths = null;
            RecomputeApproximateMetrics();
        }
    }

    /// <summary>
    /// Gets or sets the maximum height.
    /// </summary>
    public double MaxTextHeight
    {
        get => _maxTextHeight;
        set
        {
            ValidateMaxTextHeight(value, nameof(value));
            if (_maxTextHeight.Equals(value))
            {
                return;
            }

            _maxTextHeight = value;
            RecomputeApproximateMetrics();
        }
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming Trimming { get; set; } = TextTrimming.None;

    /// <summary>
    /// Gets or sets the font weight (400 = normal, 700 = bold).
    /// </summary>
    public int FontWeight { get; set; } = 400;

    /// <summary>
    /// Gets or sets the font style (0 = normal, 1 = italic, 2 = oblique).
    /// </summary>
    public int FontStyle { get; set; } = 0;

    /// <summary>
    /// Gets or sets the font stretch (5 = normal). Values 1-9 map to DirectWrite font stretch.
    /// </summary>
    public int FontStretch { get; set; } = 5;

    /// <summary>
    /// Gets or sets the per-element text rendering (anti-alias) mode resolved
    /// from <c>TextOptions.TextRenderingMode</c> on the source element.
    /// 0 = Auto (process-wide fallback), 1 = Aliased, 2 = Grayscale, 3 = ClearType.
    /// The renderer forwards this to the native glyph atlas so each element
    /// can render in its own mode within the same frame instead of inheriting
    /// the process-wide value.
    /// </summary>
    public int TextRenderingMode { get; set; }

    /// <summary>
    /// Gets or sets the per-element text formatting mode resolved from
    /// <c>TextOptions.TextFormattingMode</c> on the source element.
    /// 0 = Ideal (WPF default — resolution-independent metrics),
    /// 1 = Display (pixel-snapped metrics — sharper at small sizes).
    /// </summary>
    public int TextFormattingMode { get; set; }

    /// <summary>
    /// Gets or sets the per-element text hinting mode resolved from
    /// <c>TextOptions.TextHintingMode</c> on the source element.
    /// 0 = Auto, 1 = Fixed (full hinting), 2 = Animated (no hinting — smoother
    /// sub-pixel motion through animations).
    /// </summary>
    public int TextHintingMode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FormattedText"/> class.
    /// </summary>
    public FormattedText(string text, string fontFamily, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(fontFamily);
        ValidateEmSize(fontSize, nameof(fontSize));

        Text = text;
        FontFamily = fontFamily;
        FontSize = fontSize;
        InitializeCompatibilityFormatting();
    }

    /// <summary>
    /// Gets the width of the text layout.
    /// </summary>
    public double Width { get; internal set; }

    /// <summary>
    /// Gets the height of the text layout.
    /// </summary>
    public double Height { get; internal set; }

    /// <summary>
    /// Gets the natural line height (ascent + descent + line gap).
    /// This is the WPF-style line height based on actual font metrics.
    /// </summary>
    public double LineHeight { get; internal set; }

    /// <summary>
    /// Gets the font ascent (distance from baseline to top of the tallest glyph).
    /// </summary>
    public double Ascent { get; internal set; }

    /// <summary>
    /// Gets the font descent (distance from baseline to bottom of the lowest glyph).
    /// </summary>
    public double Descent { get; internal set; }

    /// <summary>
    /// Gets the recommended line gap between lines.
    /// </summary>
    public double LineGap { get; internal set; }

    /// <summary>
    /// Gets the baseline offset from the top of the line.
    /// </summary>
    public double Baseline { get; internal set; }

    /// <summary>
    /// Gets the number of lines in the text layout.
    /// </summary>
    public int LineCount { get; internal set; } = 1;

    /// <summary>
    /// Gets whether this text has been measured using native text measurement.
    /// </summary>
    public bool IsMeasured { get; internal set; }

    // --- from FormattedText.WpfParity.cs ---
    private double _maxTextWidth;
    private double _maxTextHeight = double.MaxValue;
    private double[]? _maxTextWidths;
    private double _pixelsPerDip = 1.0;
    private int _maxLineCount = int.MaxValue;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private Typeface? _typeface;
    private NumberSubstitution? _numberSubstitution;
    private CharacterFormat[]? _characterFormats;
    private FlowDirection _flowDirection;
    private TextAlignment _textAlignment;

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, null!, global::Jalium.UI.Media.TextFormattingMode.Ideal, 1.0)
    {
    }

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground,
        double pixelsPerDip)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, null!, global::Jalium.UI.Media.TextFormattingMode.Ideal, pixelsPerDip)
    {
    }

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground,
        NumberSubstitution numberSubstitution)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, numberSubstitution, global::Jalium.UI.Media.TextFormattingMode.Ideal, 1.0)
    {
    }

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground,
        NumberSubstitution numberSubstitution,
        double pixelsPerDip)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, numberSubstitution, global::Jalium.UI.Media.TextFormattingMode.Ideal, pixelsPerDip)
    {
    }

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground,
        NumberSubstitution numberSubstitution,
        TextFormattingMode textFormattingMode)
        : this(textToFormat, culture, flowDirection, typeface, emSize, foreground, numberSubstitution, textFormattingMode, 1.0)
    {
    }

    public FormattedText(
        string textToFormat,
        CultureInfo culture,
        FlowDirection flowDirection,
        Typeface typeface,
        double emSize,
        Brush foreground,
        NumberSubstitution numberSubstitution,
        TextFormattingMode textFormattingMode,
        double pixelsPerDip)
    {
        ArgumentNullException.ThrowIfNull(textToFormat);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(typeface);
        ArgumentNullException.ThrowIfNull(foreground);
        ValidateEmSize(emSize, nameof(emSize));
        ValidatePixelsPerDip(pixelsPerDip, nameof(pixelsPerDip));
        ValidateFlowDirection(flowDirection, nameof(flowDirection));
        ValidateTextFormattingMode(textFormattingMode, nameof(textFormattingMode));

        Text = textToFormat;
        FontFamily = typeface.FontFamily.Source;
        FontSize = emSize;
        Foreground = foreground;
        _culture = culture;
        _flowDirection = flowDirection;
        _typeface = typeface;
        _numberSubstitution = numberSubstitution;
        _pixelsPerDip = pixelsPerDip;
        TextFormattingMode = (int)textFormattingMode;
        FontWeight = typeface.Weight.ToOpenTypeWeight();
        FontStyle = typeface.Style.ToOpenTypeStyle();
        FontStretch = typeface.Stretch.ToOpenTypeStretch();
        InitializeCharacterFormats();
        RecomputeApproximateMetrics();
    }

    public double Extent => Height + OverhangAfter;

    public FlowDirection FlowDirection
    {
        get => _flowDirection;
        set
        {
            ValidateFlowDirection(value, nameof(value));
            _flowDirection = value;
        }
    }

    public int MaxLineCount
    {
        get => _maxLineCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxLineCount = value;
            RecomputeApproximateMetrics();
        }
    }

    public double MinWidth
    {
        get
        {
            double longest = 0;
            double current = 0;
            for (int index = 0; index < Text.Length; index++)
            {
                char character = Text[index];
                if (IsBreakableWhitespace(character))
                {
                    longest = Math.Max(longest, current);
                    current = 0;
                }
                else
                {
                    current += GetCharacterAdvance(index);
                }
            }

            return Math.Max(longest, current);
        }
    }

    public double OverhangAfter => 0.0;

    public double OverhangLeading => 0.0;

    public double OverhangTrailing => 0.0;

    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set => _pixelsPerDip = value;
    }

    public TextAlignment TextAlignment
    {
        get => _textAlignment;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TextAlignment));
            }

            _textAlignment = value;
        }
    }

    public double WidthIncludingTrailingWhitespace { get; internal set; }

    public Geometry BuildGeometry(Point origin) => BuildHighlightGeometry(origin);

    public Geometry BuildHighlightGeometry(Point origin) => BuildHighlightGeometry(origin, 0, Text.Length);

    public Geometry BuildHighlightGeometry(Point origin, int startIndex, int count)
    {
        ValidateRange(startIndex, count);
        var result = new GeometryGroup();
        if (count == 0)
        {
            return result;
        }

        int rangeEnd = startIndex + count;
        IReadOnlyList<LayoutLine> lines = CreateLayoutLines();
        double y = origin.Y;
        double naturalHeight = GetNaturalLineHeight();
        foreach (LayoutLine line in lines)
        {
            int intersectionStart = Math.Max(startIndex, line.Start);
            int intersectionEnd = Math.Min(rangeEnd, line.Start + line.Length);
            if (intersectionStart < intersectionEnd)
            {
                double x = origin.X + line.AlignmentOffset + MeasureRange(line.Start, intersectionStart - line.Start);
                double width = MeasureRange(intersectionStart, intersectionEnd - intersectionStart);
                if (width > 0)
                {
                    result.Children.Add(new RectangleGeometry(new Rect(x, y, width, naturalHeight)));
                }
            }

            y += naturalHeight;
        }

        return result;
    }

    public double[] GetMaxTextWidths()
        => _maxTextWidths is null ? null! : (double[])_maxTextWidths.Clone();

    public void SetMaxTextWidths(double[] maxTextWidths)
    {
        ArgumentNullException.ThrowIfNull(maxTextWidths);
        if (maxTextWidths.Length == 0)
        {
            throw new ArgumentNullException(nameof(maxTextWidths));
        }

        var copy = (double[])maxTextWidths.Clone();
        _maxTextWidths = copy;
        RecomputeApproximateMetrics();
    }

    public void SetCulture(CultureInfo culture) => SetCulture(culture, 0, Text.Length);

    public void SetCulture(CultureInfo culture, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ApplyToRange(startIndex, count, format => format.Culture = culture);
        if (startIndex == 0 && count == Text.Length)
        {
            _culture = culture;
        }
    }

    public void SetFontFamily(string fontFamily) => SetFontFamily(fontFamily, 0, Text.Length);

    public void SetFontFamily(string fontFamily, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        SetFontFamily(new FontFamily(fontFamily), startIndex, count);
    }

    public void SetFontFamily(FontFamily fontFamily) => SetFontFamily(fontFamily, 0, Text.Length);

    public void SetFontFamily(FontFamily fontFamily, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        ApplyToRange(startIndex, count, format => format.FontFamily = fontFamily);
        if (startIndex == 0 && count == Text.Length)
        {
            FontFamily = fontFamily.Source;
            _typeface = new Typeface(fontFamily, CurrentFontStyle, CurrentFontWeight, CurrentFontStretch);
        }

        RecomputeApproximateMetrics();
    }

    public void SetFontSize(double emSize) => SetFontSize(emSize, 0, Text.Length);

    public void SetFontSize(double emSize, int startIndex, int count)
    {
        ValidateEmSize(emSize, nameof(emSize));
        ApplyToRange(startIndex, count, format => format.EmSize = emSize);
        if (startIndex == 0 && count == Text.Length)
        {
            FontSize = emSize;
        }

        RecomputeApproximateMetrics();
    }

    public void SetFontStretch(FontStretch stretch) => SetFontStretch(stretch, 0, Text.Length);

    public void SetFontStretch(FontStretch stretch, int startIndex, int count)
    {
        ApplyToRange(startIndex, count, format => format.Stretch = stretch);
        if (startIndex == 0 && count == Text.Length)
        {
            FontStretch = stretch.ToOpenTypeStretch();
        }
    }

    public void SetFontStyle(FontStyle style) => SetFontStyle(style, 0, Text.Length);

    public void SetFontStyle(FontStyle style, int startIndex, int count)
    {
        ApplyToRange(startIndex, count, format => format.Style = style);
        if (startIndex == 0 && count == Text.Length)
        {
            FontStyle = style.ToOpenTypeStyle();
        }
    }

    public void SetFontTypeface(Typeface typeface) => SetFontTypeface(typeface, 0, Text.Length);

    public void SetFontTypeface(Typeface typeface, int startIndex, int count)
    {
        ArgumentNullException.ThrowIfNull(typeface);
        ApplyToRange(startIndex, count, format =>
        {
            format.FontFamily = typeface.FontFamily;
            format.Weight = typeface.Weight;
            format.Style = typeface.Style;
            format.Stretch = typeface.Stretch;
        });

        if (startIndex == 0 && count == Text.Length)
        {
            _typeface = typeface;
            FontFamily = typeface.FontFamily.Source;
            FontWeight = typeface.Weight.ToOpenTypeWeight();
            FontStyle = typeface.Style.ToOpenTypeStyle();
            FontStretch = typeface.Stretch.ToOpenTypeStretch();
        }

        RecomputeApproximateMetrics();
    }

    public void SetFontWeight(FontWeight weight) => SetFontWeight(weight, 0, Text.Length);

    public void SetFontWeight(FontWeight weight, int startIndex, int count)
    {
        ApplyToRange(startIndex, count, format => format.Weight = weight);
        if (startIndex == 0 && count == Text.Length)
        {
            FontWeight = weight.ToOpenTypeWeight();
        }
    }

    public void SetForegroundBrush(Brush foregroundBrush) => SetForegroundBrush(foregroundBrush, 0, Text.Length);

    public void SetForegroundBrush(Brush foregroundBrush, int startIndex, int count)
    {
        ApplyToRange(startIndex, count, format => format.Foreground = foregroundBrush);
        if (startIndex == 0 && count == Text.Length)
        {
            Foreground = foregroundBrush;
        }
    }

    public void SetNumberSubstitution(NumberSubstitution numberSubstitution)
        => SetNumberSubstitution(numberSubstitution, 0, Text.Length);

    public void SetNumberSubstitution(NumberSubstitution numberSubstitution, int startIndex, int count)
    {
        ApplyToRange(startIndex, count, format => format.NumberSubstitution = numberSubstitution);
        if (startIndex == 0 && count == Text.Length)
        {
            _numberSubstitution = numberSubstitution;
        }
    }

    public void SetTextDecorations(global::Jalium.UI.TextDecorationCollection textDecorations)
        => SetTextDecorations(textDecorations, 0, Text.Length);

    public void SetTextDecorations(
        global::Jalium.UI.TextDecorationCollection textDecorations,
        int startIndex,
        int count)
    {
        ApplyToRange(startIndex, count, format => format.TextDecorations = textDecorations);
    }

    private global::Jalium.UI.FontWeight CurrentFontWeight
        => global::Jalium.UI.FontWeight.FromOpenTypeWeight(Math.Clamp(FontWeight, 1, 999));

    private global::Jalium.UI.FontStyle CurrentFontStyle
        => global::Jalium.UI.FontStyle.FromOpenTypeStyle(FontStyle);

    private global::Jalium.UI.FontStretch CurrentFontStretch
        => global::Jalium.UI.FontStretch.FromOpenTypeStretch(Math.Clamp(FontStretch, 1, 9));

    private void InitializeCompatibilityFormatting()
    {
        InitializeCharacterFormats();
        RecomputeApproximateMetrics();
    }

    /// <summary>
    /// Creates the value snapshot retained by a recorded drawing without
    /// reconstructing and laying out the same text a second time.
    /// </summary>
    internal FormattedText CreateRenderSnapshot(Brush? foreground)
    {
        var snapshot = (FormattedText)MemberwiseClone();

        // Range-format mutations replace entries in the source array. Give the
        // snapshot its own entry table while sharing the immutable-by-convention
        // CharacterFormat instances; ApplyToRange clones a format before writing.
        if (_characterFormats is { Length: > 0 } characterFormats)
        {
            snapshot._characterFormats = (CharacterFormat[])characterFormats.Clone();
        }

        // SetMaxTextWidths replaces its backing array, while GetMaxTextWidths
        // returns a clone, so the current array is also safe to share.
        snapshot.Foreground = foreground;
        return snapshot;
    }

    private void InitializeCharacterFormats()
    {
        // The renderer consumes the scalar font properties for ordinary text.
        // Per-character formats (and the Typeface graph behind them) are only
        // needed once a range-formatting API is used.
        _characterFormats = null;
    }

    private CharacterFormat[] EnsureCharacterFormats()
    {
        if (_characterFormats is not null)
        {
            return _characterFormats;
        }

        var typeface = _typeface ?? new Typeface(new FontFamily(FontFamily));
        var characterFormats = new CharacterFormat[Text.Length];
        if (characterFormats.Length == 0)
        {
            return _characterFormats = characterFormats;
        }

        // Simple text uses one immutable-by-convention format for every character.
        // Range setters detach the affected entries in ApplyToRange, so the common
        // rendering path does not need one heap object per character.
        var defaultFormat = new CharacterFormat
        {
            Culture = _culture,
            FontFamily = typeface.FontFamily,
            EmSize = FontSize,
            Weight = typeface.Weight,
            Style = typeface.Style,
            Stretch = typeface.Stretch,
            Foreground = Foreground,
            NumberSubstitution = _numberSubstitution,
        };
        Array.Fill(characterFormats, defaultFormat);
        return _characterFormats = characterFormats;
    }

    private void ApplyToRange(int startIndex, int count, Action<CharacterFormat> apply)
    {
        ValidateRange(startIndex, count);
        if (count == 0)
        {
            return;
        }

        CharacterFormat[] characterFormats = EnsureCharacterFormats();

        // Preserve sharing when the selected run currently has one format. A later
        // overlapping write will detach again, retaining per-character semantics.
        var shared = characterFormats[startIndex];
        var isSharedRun = true;
        for (int index = startIndex + 1; index < startIndex + count; index++)
        {
            if (!ReferenceEquals(shared, characterFormats[index]))
            {
                isSharedRun = false;
                break;
            }
        }

        if (isSharedRun)
        {
            var updated = shared.Clone();
            apply(updated);
            Array.Fill(characterFormats, updated, startIndex, count);
            return;
        }

        for (int index = startIndex; index < startIndex + count; index++)
        {
            var updated = characterFormats[index].Clone();
            apply(updated);
            characterFormats[index] = updated;
        }
    }

    private void ValidateRange(int startIndex, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (startIndex > Text.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    private static void ValidateEmSize(double value, string parameterName)
    {
        if (!(value > 0) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidatePixelsPerDip(double value, string parameterName) { }

    private static void ValidateMaxTextWidth(double value, string parameterName)
    {
        if (value < 0 || double.IsNegativeInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateMaxTextHeight(double value, string parameterName)
    {
        if (!(value > 0) || double.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateFlowDirection(FlowDirection value, string parameterName)
    {
        if (value is not FlowDirection.LeftToRight and not FlowDirection.RightToLeft)
        {
            throw new InvalidEnumArgumentException(parameterName, (int)value, typeof(FlowDirection));
        }
    }

    private static void ValidateTextFormattingMode(TextFormattingMode value, string parameterName)
    {
        if (value is not global::Jalium.UI.Media.TextFormattingMode.Ideal and not global::Jalium.UI.Media.TextFormattingMode.Display)
        {
            throw new InvalidEnumArgumentException(parameterName, (int)value, typeof(TextFormattingMode));
        }
    }

    private void RecomputeApproximateMetrics()
    {
        ComputeApproximateLayout(
            out int computedLineCount,
            out double maximumWidth,
            out double maximumWidthIncludingTrailingWhitespace);
        double naturalHeight = GetNaturalLineHeight();
        LineHeight = naturalHeight;
        Ascent = FontSize * 0.8;
        Descent = FontSize * 0.2;
        LineGap = Math.Max(0, naturalHeight - Ascent - Descent);
        Baseline = Ascent;
        LineCount = Math.Max(1, computedLineCount);
        Width = maximumWidth;
        WidthIncludingTrailingWhitespace = maximumWidthIncludingTrailingWhitespace;
        Height = Math.Min(_maxTextHeight, LineCount * naturalHeight);
        IsMeasured = false;
    }

    private void ComputeApproximateLayout(
        out int lineCount,
        out double maximumWidth,
        out double maximumWidthIncludingTrailingWhitespace)
    {
        lineCount = 0;
        maximumWidth = 0;
        maximumWidthIncludingTrailingWhitespace = 0;
        if (Text.Length == 0)
        {
            return;
        }

        int lineStart = 0;
        int lineIndex = 0;
        double width = 0;
        double widthWithoutTrailingWhitespace = 0;
        for (int index = 0; index < Text.Length && lineCount < _maxLineCount; index++)
        {
            char character = Text[index];
            if (character == '\r' || character == '\n')
            {
                AddApproximateLine(
                    widthWithoutTrailingWhitespace,
                    width,
                    ref lineCount,
                    ref maximumWidth,
                    ref maximumWidthIncludingTrailingWhitespace);
                int newlineLength = character == '\r' && index + 1 < Text.Length && Text[index + 1] == '\n' ? 2 : 1;
                index += newlineLength - 1;
                lineStart = index + 1;
                lineIndex++;
                width = 0;
                widthWithoutTrailingWhitespace = 0;
                continue;
            }

            double advance = GetCharacterAdvance(index);
            double maximum = GetLineMaximum(lineIndex);
            if (width > 0 && width + advance > maximum)
            {
                AddApproximateLine(
                    widthWithoutTrailingWhitespace,
                    width,
                    ref lineCount,
                    ref maximumWidth,
                    ref maximumWidthIncludingTrailingWhitespace);
                lineIndex++;
                if (lineCount >= _maxLineCount)
                {
                    break;
                }

                lineStart = index;
                width = 0;
                widthWithoutTrailingWhitespace = 0;
            }

            width += advance;
            if (!IsTrailingLayoutWhitespace(character))
            {
                widthWithoutTrailingWhitespace = width;
            }
        }

        if (lineCount < _maxLineCount && lineStart <= Text.Length)
        {
            AddApproximateLine(
                widthWithoutTrailingWhitespace,
                width,
                ref lineCount,
                ref maximumWidth,
                ref maximumWidthIncludingTrailingWhitespace);
        }
    }

    private static void AddApproximateLine(
        double width,
        double widthIncludingTrailingWhitespace,
        ref int lineCount,
        ref double maximumWidth,
        ref double maximumWidthIncludingTrailingWhitespace)
    {
        lineCount++;
        maximumWidth = Math.Max(maximumWidth, width);
        maximumWidthIncludingTrailingWhitespace = Math.Max(
            maximumWidthIncludingTrailingWhitespace,
            widthIncludingTrailingWhitespace);
    }

    private IReadOnlyList<LayoutLine> CreateLayoutLines()
    {
        var lines = new List<LayoutLine>();
        if (Text.Length == 0)
        {
            return lines;
        }

        int lineStart = 0;
        int lineIndex = 0;
        double width = 0;
        double widthWithoutTrailingWhitespace = 0;
        for (int index = 0; index < Text.Length && lines.Count < _maxLineCount; index++)
        {
            char character = Text[index];
            if (character == '\r' || character == '\n')
            {
                int newlineLength = character == '\r' && index + 1 < Text.Length && Text[index + 1] == '\n' ? 2 : 1;
                AddLine(lineStart, index - lineStart, widthWithoutTrailingWhitespace, width, lineIndex++);
                index += newlineLength - 1;
                lineStart = index + 1;
                width = 0;
                widthWithoutTrailingWhitespace = 0;
                continue;
            }

            double advance = GetCharacterAdvance(index);
            double maximum = GetLineMaximum(lineIndex);
            if (width > 0 && width + advance > maximum)
            {
                AddLine(lineStart, index - lineStart, widthWithoutTrailingWhitespace, width, lineIndex++);
                if (lines.Count >= _maxLineCount)
                {
                    break;
                }

                lineStart = index;
                width = 0;
                widthWithoutTrailingWhitespace = 0;
            }

            width += advance;
            if (!IsTrailingLayoutWhitespace(character))
            {
                widthWithoutTrailingWhitespace = width;
            }
        }

        if (lines.Count < _maxLineCount && lineStart <= Text.Length)
        {
            AddLine(lineStart, Text.Length - lineStart, widthWithoutTrailingWhitespace, width, lineIndex);
        }

        return lines;

        void AddLine(int start, int length, double trimmedWidth, double includingWidth, int index)
        {
            double maximum = GetLineMaximum(index);
            double offset = _textAlignment switch
            {
                TextAlignment.Right when maximum < double.MaxValue => Math.Max(0, maximum - trimmedWidth),
                TextAlignment.Center when maximum < double.MaxValue => Math.Max(0, (maximum - trimmedWidth) / 2),
                _ => 0,
            };
            lines.Add(new LayoutLine(start, Math.Max(0, length), trimmedWidth, includingWidth, offset));
        }
    }

    private double GetLineMaximum(int lineIndex)
    {
        if (_maxTextWidths is { Length: > 0 })
        {
            return NormalizeLineMaximum(_maxTextWidths[Math.Min(lineIndex, _maxTextWidths.Length - 1)]);
        }

        return NormalizeLineMaximum(_maxTextWidth);
    }

    private static double NormalizeLineMaximum(double value)
        => value <= 0 || double.IsNaN(value) || double.IsInfinity(value) ? double.MaxValue : value;

    private double GetNaturalLineHeight()
    {
        double largest = FontSize;
        if (_characterFormats is not { } characterFormats)
        {
            return largest * 1.2;
        }

        for (int index = 0; index < characterFormats.Length; index++)
        {
            largest = Math.Max(largest, characterFormats[index].EmSize);
        }

        return largest * 1.2;
    }

    private static bool IsBreakableWhitespace(char value)
    {
        return value is '\r' or '\n' || IsTrailingLayoutWhitespace(value);
    }

    private static bool IsTrailingLayoutWhitespace(char value)
    {
        return value is not '\r' and not '\n' and not '\u00A0' and not '\u202F' and not '\u2060' and not '\uFEFF' &&
               char.IsWhiteSpace(value);
    }

    private double GetCharacterAdvance(int index)
    {
        if ((uint)index >= (uint)Text.Length)
        {
            return 0;
        }

        char value = Text[index];
        double emSize = _characterFormats is { Length: var length } characterFormats && length == Text.Length
            ? characterFormats[index].EmSize
            : FontSize;
        if (value == '\t')
        {
            return emSize * 2;
        }

        if (char.IsWhiteSpace(value))
        {
            return emSize * 0.33;
        }

        if (value >= 0x2E80)
        {
            return emSize;
        }

        return emSize * (char.IsPunctuation(value) ? 0.42 : 0.56);
    }

    private double MeasureRange(int startIndex, int count)
    {
        double result = 0;
        for (int index = startIndex; index < startIndex + count; index++)
        {
            result += GetCharacterAdvance(index);
        }

        return result;
    }

    private sealed class CharacterFormat
    {
        public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;
        public FontFamily FontFamily { get; set; } = null!;
        public double EmSize { get; set; }
        public FontWeight Weight { get; set; }
        public FontStyle Style { get; set; }
        public FontStretch Stretch { get; set; }
        public Brush? Foreground { get; set; }
        public NumberSubstitution? NumberSubstitution { get; set; }
        public global::Jalium.UI.TextDecorationCollection? TextDecorations { get; set; }

        public CharacterFormat Clone() => new()
        {
            Culture = Culture,
            FontFamily = FontFamily,
            EmSize = EmSize,
            Weight = Weight,
            Style = Style,
            Stretch = Stretch,
            Foreground = Foreground,
            NumberSubstitution = NumberSubstitution,
            TextDecorations = TextDecorations,
        };
    }

    private readonly record struct LayoutLine(
        int Start,
        int Length,
        double Width,
        double WidthIncludingTrailingWhitespace,
        double AlignmentOffset);
}
