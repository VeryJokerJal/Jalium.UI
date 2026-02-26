using System.Globalization;
using Jalium.UI;

namespace Jalium.UI.Media.TextFormatting;

/// <summary>
/// Specifies the direction in which text and other UI elements flow within their parent element.
/// </summary>
public enum FlowDirection
{
    /// <summary>
    /// Text and other elements flow from left to right as the default layout direction.
    /// </summary>
    LeftToRight,

    /// <summary>
    /// Text and other elements flow from right to left.
    /// </summary>
    RightToLeft
}

/// <summary>
/// Specifies the vertical alignment of an inline element relative to the baseline.
/// </summary>
public enum BaselineAlignment
{
    Top,
    Center,
    Bottom,
    Baseline,
    TextTop,
    TextBottom,
    Subscript,
    Superscript
}

/// <summary>
/// Provides services for formatting text and breaking text lines.
/// </summary>
public abstract class TextFormatter : IDisposable
{
    /// <summary>
    /// Creates a TextFormatter object.
    /// </summary>
    public static TextFormatter Create() => new SimpleTextFormatter();

    /// <summary>
    /// Creates a TextFormatter object with the specified formatting mode.
    /// </summary>
    public static TextFormatter Create(TextFormattingMode textFormattingMode) => new SimpleTextFormatter();

    /// <summary>
    /// Creates a line of text that is used for formatting and displaying document content.
    /// </summary>
    public abstract TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak);

    /// <summary>
    /// Returns a value that represents the smallest and largest possible paragraph width
    /// that can fully contain the specified text content.
    /// </summary>
    public abstract MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a line of text that has been formatted.
/// </summary>
public abstract class TextLine : IDisposable
{
    /// <summary>Gets the distance from the top to the bottom of the line of text.</summary>
    public abstract double Height { get; }

    /// <summary>Gets the distance from the start of text to the end of text, excluding trailing whitespace.</summary>
    public abstract double Width { get; }

    /// <summary>Gets the distance from the top of the text to the baseline.</summary>
    public abstract double Baseline { get; }

    /// <summary>Gets the distance from the top of the text to the text baseline.</summary>
    public abstract double TextBaseline { get; }

    /// <summary>Gets the number of characters in the line.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the number of newline characters at the end of the line.</summary>
    public abstract int NewlineLength { get; }

    /// <summary>Gets the distance from the beginning of the line to the start of text.</summary>
    public abstract double Start { get; }

    /// <summary>Gets the distance including trailing whitespace characters.</summary>
    public abstract double WidthIncludingTrailingWhitespace { get; }

    /// <summary>Gets the distance that the top of the text overhangs the specified baseline.</summary>
    public abstract double OverhangLeading { get; }

    /// <summary>Gets the distance that the bottom of the text overhangs the specified baseline.</summary>
    public abstract double OverhangTrailing { get; }

    /// <summary>Gets the distance from the bottom of the text to the bottom of the line.</summary>
    public abstract double OverhangAfter { get; }

    /// <summary>Gets the height of the text and any decoration in the line.</summary>
    public abstract double TextHeight { get; }

    /// <summary>Gets the distance from the top of the marker to the baseline.</summary>
    public abstract double MarkerBaseline { get; }

    /// <summary>Gets the height of the marker.</summary>
    public abstract double MarkerHeight { get; }

    /// <summary>Gets a value indicating whether the line has overflowed.</summary>
    public abstract bool HasOverflowed { get; }

    /// <summary>Gets a value indicating whether the line has been collapsed.</summary>
    public abstract bool HasCollapsed { get; }

    /// <summary>Gets the collection of text runs in the line.</summary>
    public abstract IList<TextRun> GetTextRunSpans();

    /// <summary>Gets the character hit corresponding to the specified distance from the beginning of the line.</summary>
    public abstract CharacterHit GetCharacterHitFromDistance(double distance);

    /// <summary>Gets the distance from the beginning of the line to the specified character hit.</summary>
    public abstract double GetDistanceFromCharacterHit(CharacterHit characterHit);

    /// <summary>Gets the next caret character hit for the specified character hit.</summary>
    public abstract CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit);

    /// <summary>Gets the previous caret character hit for the specified character hit.</summary>
    public abstract CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit);

    /// <summary>Gets the backspace caret character hit for the specified character hit.</summary>
    public abstract CharacterHit GetBackspaceCaretCharacterHit(CharacterHit characterHit);

    /// <summary>Gets the text bounds for the specified text range.</summary>
    public abstract IList<TextBounds> GetTextBounds(int firstTextSourceCharacterIndex, int textLength);

    /// <summary>Gets the text line break object for the line.</summary>
    public abstract TextLineBreak? GetTextLineBreak();

    /// <summary>Renders the text line.</summary>
    public abstract void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Abstract class used as the base for all text run types.
/// </summary>
public abstract class TextRun
{
    /// <summary>Gets the reference to the text run character buffer.</summary>
    public abstract CharacterBufferReference CharacterBufferReference { get; }

    /// <summary>Gets the number of characters in the text run.</summary>
    public abstract int Length { get; }

    /// <summary>Gets the set of text properties shared by every character in the text run.</summary>
    public abstract TextRunProperties? Properties { get; }
}

/// <summary>
/// Provides a set of properties that are used during formatting of a text run.
/// </summary>
public abstract class TextRunProperties
{
    /// <summary>Gets the typeface for the text run.</summary>
    public abstract Typeface Typeface { get; }

    /// <summary>Gets the text size in DIPs (Device Independent Pixels) for the text run.</summary>
    public abstract double FontRenderingEmSize { get; }

    /// <summary>Gets the text size for hinting.</summary>
    public abstract double FontHintingEmSize { get; }

    /// <summary>Gets the brush used for the foreground of the text run.</summary>
    public abstract Brush? ForegroundBrush { get; }

    /// <summary>Gets the brush used for the background of the text run.</summary>
    public abstract Brush? BackgroundBrush { get; }

    /// <summary>Gets the culture information for the text run.</summary>
    public abstract CultureInfo CultureInfo { get; }

    /// <summary>Gets the collection of text decorations.</summary>
    public abstract TextDecorationCollection? TextDecorations { get; }

    /// <summary>Gets the collection of text effects.</summary>
    public abstract TextEffectCollection? TextEffects { get; }

    /// <summary>Gets the baseline alignment for the text.</summary>
    public virtual BaselineAlignment BaselineAlignment => BaselineAlignment.Baseline;

    /// <summary>Gets the number substitution settings.</summary>
    public virtual NumberSubstitution? NumberSubstitution => null;
}

/// <summary>
/// Provides properties that are used during text paragraph formatting.
/// </summary>
public abstract class TextParagraphProperties
{
    /// <summary>Gets the flow direction for the paragraph.</summary>
    public abstract FlowDirection FlowDirection { get; }

    /// <summary>Gets the text alignment for the paragraph.</summary>
    public abstract TextAlignment TextAlignment { get; }

    /// <summary>Gets the line height for the paragraph.</summary>
    public abstract double LineHeight { get; }

    /// <summary>Gets a value indicating whether this is the first line in the paragraph.</summary>
    public abstract bool FirstLineInParagraph { get; }

    /// <summary>Gets the default text run properties for the paragraph.</summary>
    public abstract TextRunProperties DefaultTextRunProperties { get; }

    /// <summary>Gets the text wrapping mode for the paragraph.</summary>
    public abstract TextWrapping TextWrapping { get; }

    /// <summary>Gets the indent for the paragraph.</summary>
    public abstract double Indent { get; }

    /// <summary>Gets the paragraph indent.</summary>
    public virtual double ParagraphIndent => 0;

    /// <summary>Gets the text marker properties.</summary>
    public virtual TextMarkerProperties? TextMarkerProperties => null;

    /// <summary>Gets the collection of tab properties.</summary>
    public virtual IList<TextTabProperties>? Tabs => null;
}

/// <summary>
/// Specifies text alignment within a paragraph.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}

/// <summary>
/// Specifies text wrapping behavior.
/// </summary>
public enum TextWrapping
{
    NoWrap,
    Wrap,
    WrapWithOverflow
}

/// <summary>
/// Abstract class used for providing text content to TextFormatter.
/// </summary>
public abstract class TextSource
{
    /// <summary>Retrieves a TextRun starting at a specified TextSource position.</summary>
    public abstract TextRun GetTextRun(int textSourceCharacterIndex);

    /// <summary>Retrieves the text span immediately before the specified TextSource position.</summary>
    public abstract TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit);

    /// <summary>Gets a value that maps a TextEffect character index to a TextSource character index.</summary>
    public abstract int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex);
}

/// <summary>
/// Represents a text run of characters.
/// </summary>
public sealed class TextCharacters : TextRun
{
    private readonly CharacterBufferReference _charRef;
    private readonly int _length;
    private readonly TextRunProperties _properties;

    /// <summary>
    /// Initializes a new instance using a complete string.
    /// </summary>
    public TextCharacters(string characterString, TextRunProperties textRunProperties)
        : this(characterString, 0, characterString?.Length ?? 0, textRunProperties) { }

    /// <summary>
    /// Initializes a new instance using a substring.
    /// </summary>
    public TextCharacters(string characterString, int offsetToFirstChar, int length, TextRunProperties textRunProperties)
    {
        _charRef = new CharacterBufferReference(characterString ?? "", offsetToFirstChar);
        _length = length;
        _properties = textRunProperties;
    }

    /// <inheritdoc />
    public override CharacterBufferReference CharacterBufferReference => _charRef;

    /// <inheritdoc />
    public override int Length => _length;

    /// <inheritdoc />
    public override TextRunProperties? Properties => _properties;
}

/// <summary>
/// Represents the end of a line.
/// </summary>
public class TextEndOfLine : TextRun
{
    private readonly int _length;

    public TextEndOfLine(int length) { _length = length; }

    /// <inheritdoc />
    public override CharacterBufferReference CharacterBufferReference => default;

    /// <inheritdoc />
    public override int Length => _length;

    /// <inheritdoc />
    public override TextRunProperties? Properties => null;
}

/// <summary>
/// Represents the end of a paragraph.
/// </summary>
public sealed class TextEndOfParagraph : TextEndOfLine
{
    public TextEndOfParagraph(int length) : base(length) { }
}

/// <summary>
/// Represents a hidden text run.
/// </summary>
public sealed class TextHidden : TextRun
{
    private readonly int _length;

    public TextHidden(int length) { _length = length; }

    /// <inheritdoc />
    public override CharacterBufferReference CharacterBufferReference => default;

    /// <inheritdoc />
    public override int Length => _length;

    /// <inheritdoc />
    public override TextRunProperties? Properties => null;
}

/// <summary>
/// Represents an embedded object in text.
/// </summary>
public abstract class TextEmbeddedObject : TextRun
{
    /// <summary>Gets a value indicating whether the embedded object has a fixed size.</summary>
    public abstract bool HasFixedSize { get; }

    /// <summary>Gets the formatted metrics of the embedded object.</summary>
    public abstract TextEmbeddedObjectMetrics Format(double remainingParagraphWidth);

    /// <summary>Computes the bounding box of the embedded object.</summary>
    public abstract Rect ComputeBoundingBox(bool rightToLeft, bool sideways);

    /// <summary>Draws the embedded object.</summary>
    public abstract void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways);
}

/// <summary>
/// Specifies properties of an embedded object.
/// </summary>
public sealed class TextEmbeddedObjectMetrics
{
    public TextEmbeddedObjectMetrics(double width, double height, double baseline)
    {
        Width = width;
        Height = height;
        Baseline = baseline;
    }

    /// <summary>Gets the width of the text object.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the text object.</summary>
    public double Height { get; }

    /// <summary>Gets the baseline of the text object.</summary>
    public double Baseline { get; }
}

/// <summary>
/// Contains the state of a line break created during the text formatting process.
/// </summary>
public sealed class TextLineBreak : IDisposable
{
    /// <inheritdoc />
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a reference to a character buffer used in text formatting.
/// </summary>
public struct CharacterBufferReference : IEquatable<CharacterBufferReference>
{
    public CharacterBufferReference(string characterBuffer, int offsetToFirstChar)
    {
        CharacterBuffer = characterBuffer;
        OffsetToFirstChar = offsetToFirstChar;
    }

    /// <summary>Gets the character buffer string.</summary>
    public string CharacterBuffer { get; }

    /// <summary>Gets the offset to the first character.</summary>
    public int OffsetToFirstChar { get; }

    public bool Equals(CharacterBufferReference other)
        => CharacterBuffer == other.CharacterBuffer && OffsetToFirstChar == other.OffsetToFirstChar;

    public override bool Equals(object? obj)
        => obj is CharacterBufferReference other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(CharacterBuffer, OffsetToFirstChar);

    public static bool operator ==(CharacterBufferReference left, CharacterBufferReference right)
        => left.Equals(right);

    public static bool operator !=(CharacterBufferReference left, CharacterBufferReference right)
        => !left.Equals(right);
}

/// <summary>
/// Represents information used to identify a character hit within a run of characters.
/// </summary>
public struct CharacterHit : IEquatable<CharacterHit>
{
    public CharacterHit(int firstCharacterIndex, int trailingLength)
    {
        FirstCharacterIndex = firstCharacterIndex;
        TrailingLength = trailingLength;
    }

    /// <summary>Gets the index of the first character that got hit.</summary>
    public int FirstCharacterIndex { get; }

    /// <summary>Gets the trailing length value for the character that got hit.</summary>
    public int TrailingLength { get; }

    public bool Equals(CharacterHit other)
        => FirstCharacterIndex == other.FirstCharacterIndex && TrailingLength == other.TrailingLength;

    public override bool Equals(object? obj)
        => obj is CharacterHit other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(FirstCharacterIndex, TrailingLength);

    public static bool operator ==(CharacterHit left, CharacterHit right) => left.Equals(right);
    public static bool operator !=(CharacterHit left, CharacterHit right) => !left.Equals(right);
}

/// <summary>
/// Represents the minimum and maximum paragraph widths for the specified text content.
/// </summary>
public struct MinMaxParagraphWidth : IEquatable<MinMaxParagraphWidth>
{
    public MinMaxParagraphWidth(double minWidth, double maxWidth)
    {
        MinWidth = minWidth;
        MaxWidth = maxWidth;
    }

    /// <summary>Gets the smallest paragraph width possible.</summary>
    public double MinWidth { get; }

    /// <summary>Gets the largest paragraph width possible.</summary>
    public double MaxWidth { get; }

    public bool Equals(MinMaxParagraphWidth other)
        => MinWidth == other.MinWidth && MaxWidth == other.MaxWidth;

    public override bool Equals(object? obj)
        => obj is MinMaxParagraphWidth other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(MinWidth, MaxWidth);

    public static bool operator ==(MinMaxParagraphWidth left, MinMaxParagraphWidth right)
        => left.Equals(right);

    public static bool operator !=(MinMaxParagraphWidth left, MinMaxParagraphWidth right)
        => !left.Equals(right);
}

/// <summary>
/// Provides bounds information for a range of characters.
/// </summary>
public sealed class TextBounds
{
    public TextBounds(Rect rectangle, FlowDirection flowDirection, IList<TextRunBounds>? textRunBounds)
    {
        Rectangle = rectangle;
        FlowDirection = flowDirection;
        TextRunBounds = textRunBounds;
    }

    /// <summary>Gets the bounding rectangle for the text.</summary>
    public Rect Rectangle { get; }

    /// <summary>Gets the text flow direction within the bounding rectangle.</summary>
    public FlowDirection FlowDirection { get; }

    /// <summary>Gets the list of text run bounds contained within this text bounds.</summary>
    public IList<TextRunBounds>? TextRunBounds { get; }
}

/// <summary>
/// Represents bounds information for a text run.
/// </summary>
public sealed class TextRunBounds
{
    public TextRunBounds(Rect rectangle, int textSourceCharacterIndex, int length, TextRun textRun)
    {
        Rectangle = rectangle;
        TextSourceCharacterIndex = textSourceCharacterIndex;
        Length = length;
        TextRun = textRun;
    }

    /// <summary>Gets the bounding rectangle for the text run.</summary>
    public Rect Rectangle { get; }

    /// <summary>Gets the character index of the first character in the text run.</summary>
    public int TextSourceCharacterIndex { get; }

    /// <summary>Gets the number of characters in the text run.</summary>
    public int Length { get; }

    /// <summary>Gets the text run.</summary>
    public TextRun TextRun { get; }
}

/// <summary>
/// Abstract class that describes the properties of text markers (e.g. list bullets).
/// </summary>
public abstract class TextMarkerProperties
{
    /// <summary>Gets the distance from the start of the line to the end of the marker symbol.</summary>
    public abstract double Offset { get; }

    /// <summary>Gets the TextSource that represents the source of the marker characters.</summary>
    public abstract TextSource TextSource { get; }
}

/// <summary>
/// Provides properties that describe a tab stop.
/// </summary>
public sealed class TextTabProperties
{
    public TextTabProperties(TextTabAlignment alignment, double location, int tabLeader, int aligningChar)
    {
        Alignment = alignment;
        Location = location;
        TabLeader = tabLeader;
        AligningCharacter = aligningChar;
    }

    /// <summary>Gets the alignment of the tab stop.</summary>
    public TextTabAlignment Alignment { get; }

    /// <summary>Gets the index position of the tab character in the text.</summary>
    public double Location { get; }

    /// <summary>Gets the tab leader character.</summary>
    public int TabLeader { get; }

    /// <summary>Gets the aligning character.</summary>
    public int AligningCharacter { get; }
}

/// <summary>
/// Specifies how text aligns to a tab stop.
/// </summary>
public enum TextTabAlignment
{
    Left,
    Center,
    Right,
    Character
}

/// <summary>
/// Abstract class representing properties that control how text is collapsed (trimmed with ellipsis).
/// </summary>
public abstract class TextCollapsingProperties
{
    /// <summary>Gets the width available for collapsing the text.</summary>
    public abstract double Width { get; }

    /// <summary>Gets the text run that is used as the collapsing symbol.</summary>
    public abstract TextRun Symbol { get; }

    /// <summary>Gets the collapsing style.</summary>
    public abstract TextCollapsingStyle Style { get; }
}

/// <summary>
/// Specifies the style of text collapsing.
/// </summary>
public enum TextCollapsingStyle
{
    /// <summary>Collapse the trailing characters.</summary>
    TrailingCharacter,

    /// <summary>Collapse the trailing word.</summary>
    TrailingWord
}

/// <summary>
/// A text collapsing implementation that collapses at a trailing character boundary.
/// </summary>
public sealed class TextTrailingCharacterEllipsis : TextCollapsingProperties
{
    private readonly double _width;
    private readonly TextRunProperties _textRunProperties;

    public TextTrailingCharacterEllipsis(double width, TextRunProperties textRunProperties)
    {
        _width = width;
        _textRunProperties = textRunProperties;
    }

    /// <inheritdoc />
    public override double Width => _width;

    /// <inheritdoc />
    public override TextRun Symbol => new TextCharacters("\u2026", _textRunProperties);

    /// <inheritdoc />
    public override TextCollapsingStyle Style => TextCollapsingStyle.TrailingCharacter;
}

/// <summary>
/// A text collapsing implementation that collapses at a trailing word boundary.
/// </summary>
public sealed class TextTrailingWordEllipsis : TextCollapsingProperties
{
    private readonly double _width;
    private readonly TextRunProperties _textRunProperties;

    public TextTrailingWordEllipsis(double width, TextRunProperties textRunProperties)
    {
        _width = width;
        _textRunProperties = textRunProperties;
    }

    /// <inheritdoc />
    public override double Width => _width;

    /// <inheritdoc />
    public override TextRun Symbol => new TextCharacters("\u2026", _textRunProperties);

    /// <inheritdoc />
    public override TextCollapsingStyle Style => TextCollapsingStyle.TrailingWord;
}

/// <summary>
/// Provides a generic mechanism for specifying a run of characters that is associated with a length.
/// </summary>
public sealed class TextSpan<T>
{
    public TextSpan(int length, T value)
    {
        Length = length;
        Value = value;
    }

    /// <summary>Gets the length of the text span.</summary>
    public int Length { get; }

    /// <summary>Gets the value associated with the text span.</summary>
    public T Value { get; }
}

/// <summary>
/// Represents a culture-specific character buffer range.
/// </summary>
public sealed class CultureSpecificCharacterBufferRange
{
    public CultureSpecificCharacterBufferRange(CultureInfo? cultureInfo, CharacterBufferReference characterBufferRange, int length)
    {
        CultureInfo = cultureInfo;
        CharacterBufferRange = characterBufferRange;
        Length = length;
    }

    /// <summary>Gets the CultureInfo for the culture-specific range.</summary>
    public CultureInfo? CultureInfo { get; }

    /// <summary>Gets the CharacterBufferReference for the range.</summary>
    public CharacterBufferReference CharacterBufferRange { get; }

    /// <summary>Gets the length of the range.</summary>
    public int Length { get; }
}

/// <summary>
/// Specifies which axes to invert when rendering text.
/// </summary>
[Flags]
public enum InvertAxes
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = Horizontal | Vertical
}

/// <summary>
/// Simple text formatter implementation for basic text layout.
/// </summary>
internal sealed class SimpleTextFormatter : TextFormatter
{
    public override TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak)
    {
        return new SimpleTextLine(textSource, firstCharIndex, paragraphWidth, paragraphProperties);
    }

    public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties)
    {
        return new MinMaxParagraphWidth(0, double.MaxValue);
    }
}

/// <summary>
/// Simple text line implementation for basic text layout.
/// </summary>
internal sealed class SimpleTextLine : TextLine
{
    private readonly TextSource _textSource;
    private readonly int _firstCharIndex;
    private readonly double _paragraphWidth;
    private readonly double _height;
    private readonly int _length;

    public SimpleTextLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties)
    {
        _textSource = textSource;
        _firstCharIndex = firstCharIndex;
        _paragraphWidth = paragraphWidth;

        var emSize = paragraphProperties.DefaultTextRunProperties.FontRenderingEmSize;
        _height = emSize * 1.2;

        var run = textSource.GetTextRun(firstCharIndex);
        _length = run?.Length ?? 0;
    }

    public override double Width => _paragraphWidth;
    public override double Height => _height;
    public override double Baseline => _height * 0.8;
    public override double TextBaseline => Baseline;
    public override int Length => _length;
    public override int NewlineLength => 0;
    public override double Start => 0;
    public override double WidthIncludingTrailingWhitespace => _paragraphWidth;
    public override double OverhangLeading => 0;
    public override double OverhangTrailing => 0;
    public override double OverhangAfter => 0;
    public override double TextHeight => _height;
    public override double MarkerBaseline => 0;
    public override double MarkerHeight => 0;
    public override bool HasOverflowed => false;
    public override bool HasCollapsed => false;

    public override IList<TextRun> GetTextRunSpans() => Array.Empty<TextRun>();

    public override CharacterHit GetCharacterHitFromDistance(double distance) => new(0, 0);

    public override double GetDistanceFromCharacterHit(CharacterHit characterHit) => 0;

    public override CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit)
        => new(characterHit.FirstCharacterIndex + 1, 0);

    public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit)
        => new(Math.Max(0, characterHit.FirstCharacterIndex - 1), 0);

    public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit characterHit)
        => GetPreviousCaretCharacterHit(characterHit);

    public override IList<TextBounds> GetTextBounds(int firstTextSourceCharacterIndex, int textLength)
        => Array.Empty<TextBounds>();

    public override TextLineBreak? GetTextLineBreak() => null;

    public override void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion)
    {
        // Basic text drawing - actual implementation would use the DrawingContext text APIs
    }
}
