using System.Globalization;
using System.Text;
using Jalium.UI;

namespace Jalium.UI.Media.TextFormatting;

/// <summary>Represents a contiguous range in a character buffer.</summary>
public struct CharacterBufferRange : IEquatable<CharacterBufferRange>
{
    private readonly CharacterBufferReference _characterBufferReference;

    public CharacterBufferRange(char[] characterArray, int offsetToFirstChar, int characterLength)
        : this(new CharacterBufferReference(characterArray, offsetToFirstChar), characterLength)
    {
    }

    public CharacterBufferRange(string characterString, int offsetToFirstChar, int characterLength)
        : this(new CharacterBufferReference(characterString, offsetToFirstChar), characterLength)
    {
    }

#pragma warning disable CS3021 // Pointer overload is part of the WPF public contract.
    [CLSCompliant(false)]
    public unsafe CharacterBufferRange(char* unsafeCharacterString, int characterLength)
        : this(new CharacterBufferReference(unsafeCharacterString, characterLength), characterLength)
    {
    }
#pragma warning restore CS3021

    internal CharacterBufferRange(CharacterBufferReference characterBufferReference, int characterLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterLength);

        int availableLength = Math.Max(
            0,
            characterBufferReference.Count - characterBufferReference.OffsetToFirstChar);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(characterLength, availableLength);

        _characterBufferReference = characterBufferReference;
        Length = characterLength;
    }

    /// <summary>Gets the reference to the first character in the range.</summary>
    public CharacterBufferReference CharacterBufferReference => _characterBufferReference;

    /// <summary>Gets the number of characters in the range.</summary>
    public int Length { get; }

    /// <summary>Gets an empty character range.</summary>
    public static CharacterBufferRange Empty => default;

    public bool Equals(CharacterBufferRange value)
        => _characterBufferReference.Equals(value._characterBufferReference) && Length == value.Length;

    public override bool Equals(object? obj) => obj is CharacterBufferRange value && Equals(value);

    public override int GetHashCode() => _characterBufferReference.GetHashCode() ^ Length;

    public static bool operator ==(CharacterBufferRange left, CharacterBufferRange right) => left.Equals(right);

    public static bool operator !=(CharacterBufferRange left, CharacterBufferRange right) => !left.Equals(right);

    internal char GetCharacter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Length);
        return _characterBufferReference.GetCharacter(index);
    }
}

/// <summary>Associates a shaped glyph run with its source character range.</summary>
public sealed class IndexedGlyphRun
{
    internal IndexedGlyphRun(int textSourceCharacterIndex, int textSourceCharacterLength, GlyphRun glyphRun)
    {
        TextSourceCharacterIndex = textSourceCharacterIndex;
        TextSourceLength = textSourceCharacterLength;
        GlyphRun = glyphRun;
    }

    public int TextSourceCharacterIndex { get; }

    public int TextSourceLength { get; }

    public GlyphRun GlyphRun { get; }
}

/// <summary>Describes source text omitted by a collapsed line.</summary>
public sealed class TextCollapsedRange
{
    internal TextCollapsedRange(int cp, int length, double width)
    {
        TextSourceCharacterIndex = cp;
        Length = length;
        Width = width;
    }

    public int TextSourceCharacterIndex { get; }

    public int Length { get; }

    public double Width { get; }
}

/// <summary>Marks the end of the scope introduced by a <see cref="TextModifier"/>.</summary>
public class TextEndOfSegment : TextRun
{
    private readonly int _length;

    public TextEndOfSegment(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        _length = length;
    }

    public sealed override CharacterBufferReference CharacterBufferReference => default;

    public sealed override int Length => _length;

    public sealed override TextRunProperties? Properties => null;
}

/// <summary>Modifies run properties until a matching <see cref="TextEndOfSegment"/> is encountered.</summary>
public abstract class TextModifier : TextRun
{
    public sealed override CharacterBufferReference CharacterBufferReference => default;

    public abstract TextRunProperties ModifyProperties(TextRunProperties properties);

    public abstract bool HasDirectionalEmbedding { get; }

    public abstract FlowDirection FlowDirection { get; }
}

/// <summary>Defines OpenType features applied while shaping a text run.</summary>
public abstract class TextRunTypographyProperties
{
    private int _changeVersion;

    public abstract bool StandardLigatures { get; }
    public abstract bool ContextualLigatures { get; }
    public abstract bool DiscretionaryLigatures { get; }
    public abstract bool HistoricalLigatures { get; }
    public abstract bool ContextualAlternates { get; }
    public abstract bool HistoricalForms { get; }
    public abstract bool Kerning { get; }
    public abstract bool CapitalSpacing { get; }
    public abstract bool CaseSensitiveForms { get; }
    public abstract bool StylisticSet1 { get; }
    public abstract bool StylisticSet2 { get; }
    public abstract bool StylisticSet3 { get; }
    public abstract bool StylisticSet4 { get; }
    public abstract bool StylisticSet5 { get; }
    public abstract bool StylisticSet6 { get; }
    public abstract bool StylisticSet7 { get; }
    public abstract bool StylisticSet8 { get; }
    public abstract bool StylisticSet9 { get; }
    public abstract bool StylisticSet10 { get; }
    public abstract bool StylisticSet11 { get; }
    public abstract bool StylisticSet12 { get; }
    public abstract bool StylisticSet13 { get; }
    public abstract bool StylisticSet14 { get; }
    public abstract bool StylisticSet15 { get; }
    public abstract bool StylisticSet16 { get; }
    public abstract bool StylisticSet17 { get; }
    public abstract bool StylisticSet18 { get; }
    public abstract bool StylisticSet19 { get; }
    public abstract bool StylisticSet20 { get; }
    public abstract bool SlashedZero { get; }
    public abstract bool MathematicalGreek { get; }
    public abstract bool EastAsianExpertForms { get; }
    public abstract FontVariants Variants { get; }
    public abstract FontCapitals Capitals { get; }
    public abstract FontFraction Fraction { get; }
    public abstract FontNumeralStyle NumeralStyle { get; }
    public abstract FontNumeralAlignment NumeralAlignment { get; }
    public abstract FontEastAsianWidths EastAsianWidths { get; }
    public abstract FontEastAsianLanguage EastAsianLanguage { get; }
    public abstract int StandardSwashes { get; }
    public abstract int ContextualSwashes { get; }
    public abstract int StylisticAlternates { get; }
    public abstract int AnnotationAlternates { get; }

    /// <summary>Invalidates cached shaping features after a derived property changes.</summary>
    protected void OnPropertiesChanged()
    {
        unchecked
        {
            _changeVersion++;
        }
    }

    internal int ChangeVersion => _changeVersion;
}

/// <summary>Provides marker text for symbol and auto-numbered list styles.</summary>
public class TextSimpleMarkerProperties : TextMarkerProperties
{
    private readonly double _offset;
    private readonly TextSource? _textSource;

    public TextSimpleMarkerProperties(
        TextMarkerStyle style,
        double offset,
        int autoNumberingIndex,
        TextParagraphProperties textParagraphProperties)
    {
        ArgumentNullException.ThrowIfNull(textParagraphProperties);

        _offset = offset;
        if (style == TextMarkerStyle.None)
        {
            return;
        }

        if (IsSymbolStyle(style))
        {
            _textSource = new MarkerTextSource(textParagraphProperties, style, autoNumberingIndex);
            return;
        }

        if (IsIndexStyle(style))
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(autoNumberingIndex);
            _textSource = new MarkerTextSource(textParagraphProperties, style, autoNumberingIndex);
            return;
        }

        throw new ArgumentException($"Invalid {nameof(TextMarkerStyle)} value.", nameof(style));
    }

    public sealed override double Offset => _offset;

    public sealed override TextSource TextSource => _textSource!;

    private static bool IsSymbolStyle(TextMarkerStyle style)
        => style is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box;

    private static bool IsIndexStyle(TextMarkerStyle style)
        => style is TextMarkerStyle.Decimal
            or TextMarkerStyle.LowerLatin
            or TextMarkerStyle.UpperLatin
            or TextMarkerStyle.LowerRoman
            or TextMarkerStyle.UpperRoman;

    private sealed class MarkerTextSource : TextSource
    {
        private const char NumberSuffix = '.';
        private const string DecimalNumerics = "0123456789";
        private const string LowerLatinNumerics = "abcdefghijklmnopqrstuvwxyz";
        private const string UpperLatinNumerics = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private static readonly string[][] RomanNumerics =
        [
            ["m??", "cdm", "xlc", "ivx"],
            ["M??", "CDM", "XLC", "IVX"],
        ];

        private readonly char[] _characters;
        private readonly TextRunProperties _textRunProperties;

        internal MarkerTextSource(
            TextParagraphProperties textParagraphProperties,
            TextMarkerStyle markerStyle,
            int autoNumberingIndex)
        {
            TextRunProperties defaultProperties = textParagraphProperties.DefaultTextRunProperties;
            ArgumentNullException.ThrowIfNull(defaultProperties);
            PixelsPerDip = defaultProperties.PixelsPerDip;

            string marker;
            if (IsSymbolStyle(markerStyle))
            {
                marker = markerStyle switch
                {
                    TextMarkerStyle.Disc => "\u009f",
                    TextMarkerStyle.Circle => "\u00a1",
                    TextMarkerStyle.Square => "q",
                    TextMarkerStyle.Box => "\u00a7",
                    _ => throw new ArgumentOutOfRangeException(nameof(markerStyle)),
                };

                Typeface defaultTypeface = defaultProperties.Typeface;
                _textRunProperties = new MarkerTextRunProperties(
                    defaultProperties,
                    new Typeface(
                        new FontFamily("Wingdings"),
                        defaultTypeface.Style,
                        defaultTypeface.Weight,
                        defaultTypeface.Stretch));
            }
            else
            {
                _textRunProperties = defaultProperties;
                marker = markerStyle switch
                {
                    TextMarkerStyle.Decimal => new string(ConvertNumberToString(
                        autoNumberingIndex,
                        oneBased: false,
                        DecimalNumerics)),
                    TextMarkerStyle.LowerLatin => new string(ConvertNumberToString(
                        autoNumberingIndex,
                        oneBased: true,
                        LowerLatinNumerics)),
                    TextMarkerStyle.UpperLatin => new string(ConvertNumberToString(
                        autoNumberingIndex,
                        oneBased: true,
                        UpperLatinNumerics)),
                    TextMarkerStyle.LowerRoman => ConvertNumberToRomanString(autoNumberingIndex, uppercase: false),
                    TextMarkerStyle.UpperRoman => ConvertNumberToRomanString(autoNumberingIndex, uppercase: true),
                    _ => throw new ArgumentOutOfRangeException(nameof(markerStyle)),
                };
            }

            _characters = marker.ToCharArray();
        }

        public override TextRun GetTextRun(int textSourceCharacterIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(textSourceCharacterIndex);
            if (textSourceCharacterIndex >= _characters.Length)
            {
                return new TextEndOfParagraph(1);
            }

            _textRunProperties.PixelsPerDip = PixelsPerDip;
            return new TextCharacters(
                _characters,
                textSourceCharacterIndex,
                _characters.Length - textSourceCharacterIndex,
                _textRunProperties);
        }

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(
            int textSourceCharacterIndexLimit)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(textSourceCharacterIndexLimit);
            CharacterBufferRange range = textSourceCharacterIndexLimit == 0
                ? CharacterBufferRange.Empty
                : new CharacterBufferRange(
                    _characters,
                    0,
                    Math.Min(_characters.Length, textSourceCharacterIndexLimit));

            return new TextSpan<CultureSpecificCharacterBufferRange>(
                textSourceCharacterIndexLimit,
                new CultureSpecificCharacterBufferRange(_textRunProperties.CultureInfo, range));
        }

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(
            int textSourceCharacterIndex)
            => throw new NotSupportedException();

        private static char[] ConvertNumberToString(int number, bool oneBased, string numericSymbols)
        {
            if (oneBased)
            {
                number--;
            }

            int numberBase = numericSymbols.Length;
            if (number < numberBase)
            {
                return [numericSymbols[number], NumberSuffix];
            }

            int disjoint = oneBased ? 1 : 0;
            int digits = 1;
            for (long limit = numberBase, power = numberBase; number >= limit; digits++)
            {
                power *= numberBase;
                limit = power + (limit * disjoint);
            }

            var result = new char[digits + 1];
            result[digits] = NumberSuffix;
            for (int index = digits - 1; index >= 0; index--)
            {
                result[index] = numericSymbols[number % numberBase];
                number = (number / numberBase) - disjoint;
            }

            return result;
        }

        private static string ConvertNumberToRomanString(int number, bool uppercase)
        {
            if (number > 3999)
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }

            var builder = new StringBuilder();
            string[] numerics = RomanNumerics[uppercase ? 1 : 0];
            AddRomanNumeric(builder, number / 1000, numerics[0]);
            number %= 1000;
            AddRomanNumeric(builder, number / 100, numerics[1]);
            number %= 100;
            AddRomanNumeric(builder, number / 10, numerics[2]);
            number %= 10;
            AddRomanNumeric(builder, number, numerics[3]);
            builder.Append(NumberSuffix);
            return builder.ToString();
        }

        private static void AddRomanNumeric(StringBuilder builder, int number, string oneFiveTen)
        {
            if (number is < 1 or > 9)
            {
                return;
            }

            if (number is 4 or 9)
            {
                builder.Append(oneFiveTen[0]);
            }

            if (number == 9)
            {
                builder.Append(oneFiveTen[2]);
                return;
            }

            if (number >= 4)
            {
                builder.Append(oneFiveTen[1]);
            }

            for (int count = number % 5; count is > 0 and < 4; count--)
            {
                builder.Append(oneFiveTen[0]);
            }
        }
    }

    private sealed class MarkerTextRunProperties : TextRunProperties
    {
        private readonly TextRunProperties _source;

        internal MarkerTextRunProperties(TextRunProperties source, Typeface typeface)
        {
            _source = source;
            Typeface = typeface;
            PixelsPerDip = source.PixelsPerDip;
        }

        public override Typeface Typeface { get; }
        public override double FontRenderingEmSize => _source.FontRenderingEmSize;
        public override double FontHintingEmSize => _source.FontHintingEmSize;
        public override Brush? ForegroundBrush => _source.ForegroundBrush;
        public override Brush? BackgroundBrush => _source.BackgroundBrush;
        public override CultureInfo CultureInfo => _source.CultureInfo;
        public override TextDecorationCollection? TextDecorations => _source.TextDecorations;
        public override TextEffectCollection? TextEffects => _source.TextEffects;
        public override BaselineAlignment BaselineAlignment => _source.BaselineAlignment;
        public override NumberSubstitution? NumberSubstitution => _source.NumberSubstitution;
        public override TextRunTypographyProperties? TypographyProperties => _source.TypographyProperties;
    }
}
