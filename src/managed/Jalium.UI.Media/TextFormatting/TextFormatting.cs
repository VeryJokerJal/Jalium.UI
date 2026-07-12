using System.Globalization;
using Jalium.UI;

namespace Jalium.UI.Media.TextFormatting;

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
    /// Creates a line of text while reusing text runs held by the caller-owned cache.
    /// </summary>
    public abstract TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak,
        TextRunCache textRunCache);

    /// <summary>
    /// Returns a value that represents the smallest and largest possible paragraph width
    /// that can fully contain the specified text content.
    /// </summary>
    public abstract MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties);

    /// <summary>
    /// Returns the paragraph width range while reusing text runs held by the caller-owned cache.
    /// </summary>
    public abstract MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties,
        TextRunCache textRunCache);

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
    private double _pixelsPerDip = 1.0;

    /// <summary>Initializes a text line using the default display density.</summary>
    protected TextLine()
    {
    }

    /// <summary>Initializes a text line using the specified display density.</summary>
    /// <param name="pixelsPerDip">The number of physical pixels per device-independent pixel.</param>
    protected TextLine(double pixelsPerDip)
    {
        _pixelsPerDip = pixelsPerDip;
    }

    /// <summary>Gets or sets the display density used to render the line.</summary>
    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set => _pixelsPerDip = value;
    }

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

    /// <summary>Gets the number of trailing whitespace characters in the line.</summary>
    public abstract int TrailingWhitespaceLength { get; }

    /// <summary>Gets the number of characters after the line that can affect its formatting.</summary>
    public abstract int DependentLength { get; }

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

    /// <summary>Gets the height of the actual ink occupied by the line.</summary>
    public abstract double Extent { get; }

    /// <summary>Gets the distance from the top of the marker to the baseline.</summary>
    public abstract double MarkerBaseline { get; }

    /// <summary>Gets the height of the marker.</summary>
    public abstract double MarkerHeight { get; }

    /// <summary>Gets a value indicating whether the line has overflowed.</summary>
    public abstract bool HasOverflowed { get; }

    /// <summary>Gets a value indicating whether the line has been collapsed.</summary>
    public abstract bool HasCollapsed { get; }

    /// <summary>Gets whether the formatter truncated the line in the middle of a word.</summary>
    public virtual bool IsTruncated => false;

    /// <summary>Gets the collection of text runs in the line.</summary>
    public abstract IList<TextSpan<TextRun>> GetTextRunSpans();

    /// <summary>Collapses this line using the first applicable collapsing policy.</summary>
    public abstract TextLine Collapse(params TextCollapsingProperties[] collapsingPropertiesList);

    /// <summary>Gets the source ranges omitted from a collapsed line.</summary>
    public abstract IList<TextCollapsedRange> GetTextCollapsedRanges();

    /// <summary>Enumerates glyph runs and their source character ranges.</summary>
    public abstract IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns();

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
    private double _pixelsPerDip = 1.0;

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

    /// <summary>Gets the OpenType typography settings for the text run.</summary>
    public virtual TextRunTypographyProperties? TypographyProperties => null;

    /// <summary>Gets or sets the number of physical pixels per device-independent pixel.</summary>
    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set => _pixelsPerDip = value;
    }
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

    /// <summary>Gets whether the formatted line may be collapsed even when it does not overflow.</summary>
    public virtual bool AlwaysCollapsible => false;

    /// <summary>Gets decorations that are applied to every run in the paragraph.</summary>
    public virtual TextDecorationCollection? TextDecorations => null;

    /// <summary>Gets the paragraph indent.</summary>
    public virtual double ParagraphIndent => 0;

    /// <summary>Gets the default distance between incremental tab stops.</summary>
    public virtual double DefaultIncrementalTab => 4 * DefaultTextRunProperties.FontRenderingEmSize;

    /// <summary>Gets the text marker properties.</summary>
    public virtual TextMarkerProperties? TextMarkerProperties => null;

    /// <summary>Gets the collection of tab properties.</summary>
    public virtual IList<TextTabProperties>? Tabs => null;
}

/// <summary>
/// Abstract class used for providing text content to TextFormatter.
/// </summary>
public abstract class TextSource
{
    private double _pixelsPerDip = 1.0;

    /// <summary>Retrieves a TextRun starting at a specified TextSource position.</summary>
    public abstract TextRun GetTextRun(int textSourceCharacterIndex);

    /// <summary>Retrieves the text span immediately before the specified TextSource position.</summary>
    public abstract TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit);

    /// <summary>Gets a value that maps a TextEffect character index to a TextSource character index.</summary>
    public abstract int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex);

    /// <summary>
    /// Gets or sets the number of physical pixels per device-independent pixel at which text is rendered.
    /// </summary>
    /// <remarks>
    /// The default corresponds to 96 DPI. As in WPF, the value is stored verbatim so a text source can
    /// track the rendering surface's DPI policy.
    /// </remarks>
    public double PixelsPerDip
    {
        get => _pixelsPerDip;
        set => _pixelsPerDip = value;
    }
}

/// <summary>
/// Stores text runs returned by a <see cref="TextSource"/> so repeated formatting can reuse them.
/// </summary>
public sealed class TextRunCache
{
    private readonly Dictionary<int, TextRun> _runs = new();

    public TextRunCache()
    {
    }

    /// <summary>
    /// Notifies the cache that text was inserted or removed at the specified source index.
    /// Runs before the edit that cannot overlap it are retained; all affected and shifted runs are discarded.
    /// </summary>
    public void Change(int textSourceCharacterIndex, int addition, int removal)
    {
        if (_runs.Count == 0)
        {
            return;
        }

        foreach (int runStart in _runs
                     .Where(pair => pair.Key >= textSourceCharacterIndex ||
                                    pair.Key + Math.Max(0, pair.Value.Length) > textSourceCharacterIndex)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            _runs.Remove(runStart);
        }
    }

    /// <summary>Discards every cached text run.</summary>
    public void Invalidate() => _runs.Clear();

    internal TextRun GetOrAdd(TextSource textSource, int textSourceCharacterIndex)
    {
        if (!_runs.TryGetValue(textSourceCharacterIndex, out TextRun? run))
        {
            run = textSource.GetTextRun(textSourceCharacterIndex);
            _runs.Add(textSourceCharacterIndex, run);
        }

        return run;
    }
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
    /// Initializes a run from a range within a character array.
    /// </summary>
    public TextCharacters(
        char[] characterArray,
        int offsetToFirstChar,
        int length,
        TextRunProperties textRunProperties)
        : this(new CharacterBufferReference(characterArray, offsetToFirstChar), length, textRunProperties)
    {
    }

    /// <summary>
    /// Initializes a new instance using a substring.
    /// </summary>
    public TextCharacters(string characterString, int offsetToFirstChar, int length, TextRunProperties textRunProperties)
        : this(new CharacterBufferReference(characterString, offsetToFirstChar), length, textRunProperties)
    {
    }

    /// <summary>
    /// Initializes a run from an unmanaged character buffer. The characters are snapshotted so the run
    /// remains valid after the caller releases the pointer.
    /// </summary>
#pragma warning disable CS3021 // Attribute is part of the WPF public contract.
    [CLSCompliant(false)]
    public unsafe TextCharacters(
        char* unsafeCharacterString,
        int length,
        TextRunProperties textRunProperties)
        : this(new CharacterBufferReference(unsafeCharacterString, length), length, textRunProperties)
    {
    }
#pragma warning restore CS3021

    private TextCharacters(
        CharacterBufferReference characterBufferReference,
        int length,
        TextRunProperties textRunProperties)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentNullException.ThrowIfNull(textRunProperties);

        if (textRunProperties.Typeface is null)
        {
            throw new ArgumentNullException("textRunProperties.Typeface");
        }

        if (textRunProperties.CultureInfo is null)
        {
            throw new ArgumentNullException("textRunProperties.CultureInfo");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            textRunProperties.FontRenderingEmSize,
            "textRunProperties.FontRenderingEmSize");

        _charRef = characterBufferReference;
        _length = length;
        _properties = textRunProperties;
    }

    /// <inheritdoc />
    public sealed override CharacterBufferReference CharacterBufferReference => _charRef;

    /// <inheritdoc />
    public sealed override int Length => _length;

    /// <inheritdoc />
    public sealed override TextRunProperties Properties => _properties;
}

/// <summary>
/// Represents the end of a line.
/// </summary>
public class TextEndOfLine : TextRun
{
    private readonly int _length;
    private readonly TextRunProperties? _properties;

    public TextEndOfLine(int length)
        : this(length, null)
    {
    }

    /// <summary>
    /// Initializes an end-of-line run with the supplied character length and optional run properties.
    /// </summary>
    public TextEndOfLine(int length, TextRunProperties? textRunProperties)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (textRunProperties is not null && textRunProperties.Typeface is null)
        {
            throw new ArgumentNullException("textRunProperties.Typeface");
        }

        _length = length;
        _properties = textRunProperties;
    }

    /// <inheritdoc />
    public sealed override CharacterBufferReference CharacterBufferReference => default;

    /// <inheritdoc />
    public sealed override int Length => _length;

    /// <inheritdoc />
    public sealed override TextRunProperties? Properties => _properties;
}

/// <summary>
/// Represents the end of a paragraph.
/// </summary>
public class TextEndOfParagraph : TextEndOfLine
{
    public TextEndOfParagraph(int length) : base(length) { }

    /// <summary>
    /// Initializes an end-of-paragraph run with the supplied character length and optional run properties.
    /// </summary>
    public TextEndOfParagraph(int length, TextRunProperties? textRunProperties)
        : base(length, textRunProperties)
    {
    }
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
    /// <summary>Gets the line-breaking condition before the embedded object.</summary>
    public abstract LineBreakCondition BreakBefore { get; }

    /// <summary>Gets the line-breaking condition after the embedded object.</summary>
    public abstract LineBreakCondition BreakAfter { get; }

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
    private readonly object? _currentScope;
    private BreakRecord? _breakRecord;

    internal TextLineBreak(object? currentScope, object? continuationState)
    {
        _currentScope = currentScope;
        _breakRecord = continuationState is null ? null : new BreakRecord(continuationState);
    }

    private TextLineBreak(object? currentScope, BreakRecord? breakRecord)
    {
        _currentScope = currentScope;
        _breakRecord = breakRecord;
    }

    /// <summary>
    /// Creates an independently disposable copy of this line-break continuation.
    /// </summary>
    public TextLineBreak Clone()
        => new(_currentScope, _breakRecord?.Clone());

    /// <inheritdoc />
    public void Dispose()
    {
        _breakRecord = null;
        GC.SuppressFinalize(this);
    }

    internal object? TextModifierScope => _currentScope;

    internal object? ContinuationState => _breakRecord?.State;

    internal object? BreakRecordIdentity => _breakRecord;

    internal bool HasBreakRecord => _breakRecord is not null;

    private sealed class BreakRecord
    {
        internal BreakRecord(object state)
        {
            State = state;
        }

        internal object State { get; }

        internal BreakRecord Clone() => new(State);
    }
}

/// <summary>
/// Represents a reference to a character buffer used in text formatting.
/// </summary>
public struct CharacterBufferReference : IEquatable<CharacterBufferReference>
{
    private readonly object? _buffer;
    private readonly object? _identity;

    public CharacterBufferReference(string characterBuffer, int offsetToFirstChar)
    {
        ValidateOffset(characterBuffer?.Length ?? 0, offsetToFirstChar);
        _buffer = characterBuffer;
        _identity = new object();
        OffsetToFirstChar = offsetToFirstChar;
    }

    /// <summary>Initializes a reference to a range within a character array.</summary>
    public CharacterBufferReference(char[] characterArray, int offsetToFirstChar)
    {
        ValidateOffset(characterArray?.Length ?? 0, offsetToFirstChar);
        _buffer = characterArray;
        _identity = new object();
        OffsetToFirstChar = offsetToFirstChar;
    }

    /// <summary>
    /// Initializes a reference from an unmanaged character buffer. The characters are copied so this
    /// managed reference does not outlive the source pointer.
    /// </summary>
#pragma warning disable CS3021 // Attribute is part of the WPF public contract.
    [CLSCompliant(false)]
    public unsafe CharacterBufferReference(char* unsafeCharacterString, int characterLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterLength);

        if (unsafeCharacterString == null && characterLength != 0)
        {
            throw new ArgumentNullException(nameof(unsafeCharacterString));
        }

        _buffer = characterLength == 0
            ? string.Empty
            : new string(unsafeCharacterString, 0, characterLength);
        _identity = new object();
        OffsetToFirstChar = 0;
    }
#pragma warning restore CS3021

    /// <summary>Gets the character buffer string.</summary>
    public string CharacterBuffer => _buffer switch
    {
        string value => value,
        char[] value => new string(value),
        _ => string.Empty,
    };

    /// <summary>Gets the offset to the first character.</summary>
    public int OffsetToFirstChar { get; }

    public bool Equals(CharacterBufferReference other)
        => ReferenceEquals(_identity, other._identity) && OffsetToFirstChar == other.OffsetToFirstChar;

    public override bool Equals(object? obj)
        => obj is CharacterBufferReference other && Equals(other);

    public override int GetHashCode()
        => _identity is null ? 0 : HashCode.Combine(_identity, OffsetToFirstChar);

    public static bool operator ==(CharacterBufferReference left, CharacterBufferReference right)
        => left.Equals(right);

    public static bool operator !=(CharacterBufferReference left, CharacterBufferReference right)
        => !left.Equals(right);

    internal int Count => _buffer switch
    {
        string value => value.Length,
        char[] value => value.Length,
        _ => 0,
    };

    internal char GetCharacter(int index)
    {
        int absoluteIndex = checked(OffsetToFirstChar + index);
        return _buffer switch
        {
            string value => value[absoluteIndex],
            char[] value => value[absoluteIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private static void ValidateOffset(int characterCount, int offsetToFirstChar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offsetToFirstChar);
        int maximumOffset = Math.Max(0, characterCount - 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offsetToFirstChar, maximumOffset);
    }
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
public class CultureSpecificCharacterBufferRange
{
    public CultureSpecificCharacterBufferRange(CultureInfo? culture, CharacterBufferRange characterBufferRange)
    {
        CultureInfo = culture;
        CharacterBufferRange = characterBufferRange;
    }

    /// <summary>Gets the CultureInfo for the culture-specific range.</summary>
    public CultureInfo? CultureInfo { get; }

    /// <summary>Gets the character buffer range.</summary>
    public CharacterBufferRange CharacterBufferRange { get; }
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
        return new SimpleTextLine(textSource, firstCharIndex, paragraphWidth, paragraphProperties, null);
    }

    public override TextLine FormatLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextLineBreak? previousLineBreak,
        TextRunCache textRunCache)
    {
        ArgumentNullException.ThrowIfNull(textRunCache);
        return new SimpleTextLine(textSource, firstCharIndex, paragraphWidth, paragraphProperties, textRunCache);
    }

    public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties)
    {
        return MeasureParagraph(textSource, firstCharIndex, paragraphProperties, null);
    }

    public override MinMaxParagraphWidth FormatMinMaxParagraphWidth(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties,
        TextRunCache textRunCache)
    {
        ArgumentNullException.ThrowIfNull(textRunCache);
        return MeasureParagraph(textSource, firstCharIndex, paragraphProperties, textRunCache);
    }

    private static MinMaxParagraphWidth MeasureParagraph(
        TextSource textSource,
        int firstCharIndex,
        TextParagraphProperties paragraphProperties,
        TextRunCache? textRunCache)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(paragraphProperties);
        ArgumentOutOfRangeException.ThrowIfNegative(firstCharIndex);

        int sourceIndex = firstCharIndex;
        double currentLineWidth = 0;
        double maximumLineWidth = 0;
        double maximumUnbreakableWidth = 0;

        // A TextSource is callback-based and can be user code. Bound the walk so a malformed source that
        // never returns an end run cannot hang the formatter.
        for (int runCount = 0; runCount < 4096; runCount++)
        {
            TextRun run = textRunCache?.GetOrAdd(textSource, sourceIndex)
                ?? textSource.GetTextRun(sourceIndex);
            ArgumentNullException.ThrowIfNull(run);

            if (run.Length <= 0)
            {
                break;
            }

            if (run is TextEndOfParagraph)
            {
                maximumLineWidth = Math.Max(maximumLineWidth, currentLineWidth);
                break;
            }

            if (run is TextEndOfLine)
            {
                maximumLineWidth = Math.Max(maximumLineWidth, currentLineWidth);
                currentLineWidth = 0;
            }
            else
            {
                (double width, double unbreakableWidth) = TextRunMetrics.Measure(run);
                currentLineWidth += width;
                maximumUnbreakableWidth = Math.Max(maximumUnbreakableWidth, unbreakableWidth);
            }

            try
            {
                sourceIndex = checked(sourceIndex + run.Length);
            }
            catch (OverflowException)
            {
                break;
            }
        }

        maximumLineWidth = Math.Max(maximumLineWidth, currentLineWidth);
        return new MinMaxParagraphWidth(maximumUnbreakableWidth, maximumLineWidth);
    }
}

internal static class TextRunMetrics
{
    internal static (double Width, double UnbreakableWidth) Measure(TextRun run)
    {
        if (run is TextCharacters characters)
        {
            return MeasureCharacters(characters);
        }

        if (run is TextEmbeddedObject embeddedObject)
        {
            double width = Math.Max(0, embeddedObject.Format(double.MaxValue).Width);
            return (width, width);
        }

        if (run is TextHidden)
        {
            return (0, 0);
        }

        double emSize = Math.Max(0, run.Properties?.FontRenderingEmSize ?? 0);
        double widthEstimate = emSize * 0.5 * Math.Max(0, run.Length);
        return (widthEstimate, widthEstimate);
    }

    private static (double Width, double UnbreakableWidth) MeasureCharacters(TextCharacters characters)
    {
        CharacterBufferReference buffer = characters.CharacterBufferReference;
        double emSize = Math.Max(0, characters.Properties.FontRenderingEmSize);
        double totalWidth = 0;
        double currentWordWidth = 0;
        double longestWordWidth = 0;
        int availableLength = Math.Max(0, buffer.Count - buffer.OffsetToFirstChar);
        int materializedLength = Math.Min(characters.Length, availableLength);

        for (int index = 0; index < materializedLength; index++)
        {
            char character = buffer.GetCharacter(index);
            double characterWidth = emSize * (char.IsWhiteSpace(character) ? 0.33 : 0.5);
            totalWidth += characterWidth;

            if (char.IsWhiteSpace(character))
            {
                longestWordWidth = Math.Max(longestWordWidth, currentWordWidth);
                currentWordWidth = 0;
            }
            else
            {
                currentWordWidth += characterWidth;
            }
        }

        // WPF accepts a declared run length that extends beyond its current buffer. Keep the formatter
        // deterministic for such a source while still honoring the run's declared length.
        int unmaterializedLength = Math.Max(0, characters.Length - materializedLength);
        double unmaterializedWidth = emSize * 0.5 * unmaterializedLength;
        totalWidth += unmaterializedWidth;
        currentWordWidth += unmaterializedWidth;
        longestWordWidth = Math.Max(longestWordWidth, currentWordWidth);
        return (totalWidth, longestWordWidth);
    }

    internal static int CountTrailingWhitespace(TextRun run)
    {
        int count = 0;
        for (int index = Math.Max(0, run.Length) - 1; index >= 0; index--)
        {
            if (!TryGetCharacter(run, index, out char character) || !char.IsWhiteSpace(character))
            {
                break;
            }

            count++;
        }

        return count;
    }

    internal static int GetFittingCharacterCount(TextRun run, double availableWidth)
    {
        if (availableWidth <= 0)
        {
            return 0;
        }

        double width = 0;
        int length = Math.Max(0, run.Length);
        for (int index = 0; index < length; index++)
        {
            double nextWidth = GetCharacterWidth(run, index);
            if (width + nextWidth > availableWidth)
            {
                return index;
            }

            width += nextWidth;
        }

        return length;
    }

    internal static double MeasurePrefix(TextRun run, int length)
    {
        double width = 0;
        int end = Math.Min(Math.Max(0, length), Math.Max(0, run.Length));
        for (int index = 0; index < end; index++)
        {
            width += GetCharacterWidth(run, index);
        }

        return width;
    }

    internal static double GetCharacterWidth(TextRun run, int characterIndex)
    {
        double emSize = Math.Max(0, run.Properties?.FontRenderingEmSize ?? 0);
        return TryGetCharacter(run, characterIndex, out char character) && char.IsWhiteSpace(character)
            ? emSize * 0.33
            : emSize * 0.5;
    }

    internal static bool TryGetCharacter(TextRun run, int characterIndex, out char character)
    {
        if (run is TextCharacters characters)
        {
            CharacterBufferReference buffer = characters.CharacterBufferReference;
            int availableLength = Math.Max(0, buffer.Count - buffer.OffsetToFirstChar);
            if ((uint)characterIndex < (uint)Math.Min(characters.Length, availableLength))
            {
                character = buffer.GetCharacter(characterIndex);
                return true;
            }
        }

        character = default;
        return false;
    }

    internal static TextRun Slice(TextRun run, int length)
    {
        int sliceLength = Math.Min(Math.Max(0, length), Math.Max(0, run.Length));
        if (sliceLength == run.Length)
        {
            return run;
        }

        if (run is TextCharacters characters && sliceLength > 0)
        {
            CharacterBufferReference buffer = characters.CharacterBufferReference;
            int availableLength = Math.Max(0, buffer.Count - buffer.OffsetToFirstChar);
            int materializedLength = Math.Min(sliceLength, availableLength);
            if (materializedLength == sliceLength)
            {
                return new TextCharacters(
                    buffer.CharacterBuffer,
                    buffer.OffsetToFirstChar,
                    sliceLength,
                    characters.Properties);
            }
        }

        return new TextRunSlice(run, sliceLength);
    }

    internal static GlyphRun? CreateGlyphRun(TextRun run, double start, double baseline, double pixelsPerDip)
    {
        if (run is not TextCharacters characters || characters.Length <= 0)
        {
            return null;
        }

        TextRunProperties properties = characters.Properties;
        var characterValues = new List<char>(characters.Length);
        var glyphIndices = new List<ushort>(characters.Length);
        var advanceWidths = new List<double>(characters.Length);
        var clusterMap = new List<ushort>(characters.Length);
        var caretStops = new List<bool>(characters.Length + 1);

        properties.Typeface.TryGetGlyphTypeface(out GlyphTypeface? glyphTypeface);
        for (int index = 0; index < characters.Length; index++)
        {
            char character = TryGetCharacter(characters, index, out char materialized)
                ? materialized
                : '\ufffd';
            characterValues.Add(character);

            ushort glyphIndex = (ushort)character;
            if (glyphTypeface?.CharacterToGlyphMap.TryGetValue(character, out ushort mappedGlyph) == true)
            {
                glyphIndex = mappedGlyph;
            }

            glyphIndices.Add(glyphIndex);
            advanceWidths.Add(GetCharacterWidth(characters, index));
            clusterMap.Add((ushort)Math.Min(index, ushort.MaxValue));
            caretStops.Add(true);
        }

        caretStops.Add(true);
        return new GlyphRun
        {
            FontFamily = properties.Typeface.FontFamily,
            GlyphTypeface = glyphTypeface,
            FontRenderingEmSize = properties.FontRenderingEmSize,
            PixelsPerDip = (float)pixelsPerDip,
            GlyphIndices = glyphIndices,
            BaselineOrigin = new Point(start, baseline),
            AdvanceWidths = advanceWidths,
            GlyphOffsets = Enumerable.Repeat(default(Point), glyphIndices.Count).ToArray(),
            Characters = characterValues,
            DeviceFontName = properties.Typeface.FontFamily.Source,
            ClusterMap = clusterMap,
            CaretStops = caretStops,
            Language = Jalium.UI.Markup.XmlLanguage.GetLanguage(properties.CultureInfo.IetfLanguageTag),
        };
    }

    private sealed class TextRunSlice : TextRun
    {
        private readonly TextRun _source;

        internal TextRunSlice(TextRun source, int length)
        {
            _source = source;
            Length = length;
        }

        public override CharacterBufferReference CharacterBufferReference => _source.CharacterBufferReference;

        public override int Length { get; }

        public override TextRunProperties? Properties => _source.Properties;
    }
}

/// <summary>
/// Simple text line implementation for basic text layout.
/// </summary>
internal sealed class SimpleTextLine : TextLine
{
    private readonly TextSource _textSource;
    private readonly int _firstCharIndex;
    private readonly TextRun _run;
    private readonly IList<TextSpan<TextRun>> _runSpans;
    private readonly IList<TextCollapsedRange> _collapsedRanges;
    private readonly FlowDirection _flowDirection;
    private readonly double _naturalWidth;
    private readonly double _width;
    private readonly double _widthIncludingTrailingWhitespace;
    private readonly double _height;
    private readonly int _length;
    private readonly int _trailingWhitespaceLength;
    private readonly bool _hasOverflowed;
    private readonly bool _hasCollapsed;
    private readonly bool _isTruncated;

    public SimpleTextLine(
        TextSource textSource,
        int firstCharIndex,
        double paragraphWidth,
        TextParagraphProperties paragraphProperties,
        TextRunCache? textRunCache)
        : base(textSource?.PixelsPerDip ?? 1.0)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        ArgumentNullException.ThrowIfNull(paragraphProperties);
        ArgumentOutOfRangeException.ThrowIfNegative(firstCharIndex);

        _textSource = textSource;
        _firstCharIndex = firstCharIndex;
        _flowDirection = paragraphProperties.FlowDirection;

        var emSize = paragraphProperties.DefaultTextRunProperties.FontRenderingEmSize;
        _height = emSize * 1.2;

        _run = textRunCache?.GetOrAdd(textSource, firstCharIndex) ?? textSource.GetTextRun(firstCharIndex);
        ArgumentNullException.ThrowIfNull(_run);
        _length = Math.Max(0, _run.Length);
        _trailingWhitespaceLength = TextRunMetrics.CountTrailingWhitespace(_run);
        _runSpans = _length == 0
            ? Array.Empty<TextSpan<TextRun>>()
            : [new TextSpan<TextRun>(_length, _run)];
        _collapsedRanges = Array.Empty<TextCollapsedRange>();

        (double naturalWidth, double unbreakableWidth) = TextRunMetrics.Measure(_run);
        _naturalWidth = naturalWidth;
        double trailingWhitespaceWidth = TextRunMetrics.MeasurePrefix(
            _run,
            _length) - TextRunMetrics.MeasurePrefix(
            _run,
            _length - _trailingWhitespaceLength);
        double widthExcludingTrailingWhitespace = Math.Max(0, naturalWidth - trailingWhitespaceWidth);
        double constrainedWidth = double.IsPositiveInfinity(paragraphWidth)
            ? naturalWidth
            : Math.Max(0, paragraphWidth);
        _width = Math.Min(widthExcludingTrailingWhitespace, constrainedWidth);
        _widthIncludingTrailingWhitespace = Math.Min(naturalWidth, constrainedWidth);
        _hasOverflowed = naturalWidth > constrainedWidth;
        _isTruncated = _hasOverflowed
            && paragraphProperties.TextWrapping == TextWrapping.Wrap
            && unbreakableWidth > constrainedWidth;
    }

    private SimpleTextLine(
        SimpleTextLine source,
        TextRun symbol,
        int visibleLength,
        double visibleWidth,
        double symbolWidth)
        : base(source.PixelsPerDip)
    {
        _textSource = source._textSource;
        _firstCharIndex = source._firstCharIndex;
        _run = source._run;
        _flowDirection = source._flowDirection;
        _naturalWidth = source._naturalWidth;
        _height = source._height;
        _length = source._length;
        _hasOverflowed = source._hasOverflowed;
        _hasCollapsed = true;

        var spans = new List<TextSpan<TextRun>>(2);
        if (visibleLength > 0)
        {
            spans.Add(new TextSpan<TextRun>(visibleLength, TextRunMetrics.Slice(_run, visibleLength)));
        }

        if (symbol.Length > 0)
        {
            spans.Add(new TextSpan<TextRun>(symbol.Length, symbol));
        }

        _runSpans = spans;
        int collapsedLength = Math.Max(0, _length - visibleLength);
        _collapsedRanges = collapsedLength == 0
            ? Array.Empty<TextCollapsedRange>()
            : [new TextCollapsedRange(
                _firstCharIndex + visibleLength,
                collapsedLength,
                Math.Max(0, _naturalWidth - visibleWidth))];
        _width = visibleWidth + symbolWidth;
        _widthIncludingTrailingWhitespace = _width;
    }

    public override double Width => _width;
    public override double Height => _height;
    public override double Baseline => _height * 0.8;
    public override double TextBaseline => Baseline;
    public override int Length => _length;
    public override int TrailingWhitespaceLength => _trailingWhitespaceLength;
    public override int DependentLength => 0;
    public override int NewlineLength => 0;
    public override double Start => 0;
    public override double WidthIncludingTrailingWhitespace => _widthIncludingTrailingWhitespace;
    public override double OverhangLeading => 0;
    public override double OverhangTrailing => 0;
    public override double OverhangAfter => 0;
    public override double TextHeight => _height;
    public override double Extent => _height;
    public override double MarkerBaseline => 0;
    public override double MarkerHeight => 0;
    public override bool HasOverflowed => _hasOverflowed;
    public override bool HasCollapsed => _hasCollapsed;
    public override bool IsTruncated => _isTruncated;

    public override IList<TextSpan<TextRun>> GetTextRunSpans() => _runSpans;

    public override TextLine Collapse(params TextCollapsingProperties[] collapsingPropertiesList)
    {
        ArgumentNullException.ThrowIfNull(collapsingPropertiesList);
        if (collapsingPropertiesList.Length == 0)
        {
            return this;
        }

        TextCollapsingProperties collapsingProperties = collapsingPropertiesList[0];
        ArgumentNullException.ThrowIfNull(collapsingProperties);
        TextRun symbol = collapsingProperties.Symbol;
        ArgumentNullException.ThrowIfNull(symbol);

        double targetWidth = Math.Max(0, collapsingProperties.Width);
        if (_naturalWidth <= targetWidth || _length == 0)
        {
            return this;
        }

        double symbolWidth = TextRunMetrics.Measure(symbol).Width;
        double availableTextWidth = Math.Max(0, targetWidth - symbolWidth);
        int visibleLength = TextRunMetrics.GetFittingCharacterCount(_run, availableTextWidth);
        if (collapsingProperties.Style == TextCollapsingStyle.TrailingWord && visibleLength < _length)
        {
            while (visibleLength > 0
                   && TextRunMetrics.TryGetCharacter(_run, visibleLength - 1, out char trailingCharacter)
                   && !char.IsWhiteSpace(trailingCharacter))
            {
                visibleLength--;
            }

            while (visibleLength > 0
                   && TextRunMetrics.TryGetCharacter(_run, visibleLength - 1, out char whitespaceCharacter)
                   && char.IsWhiteSpace(whitespaceCharacter))
            {
                visibleLength--;
            }
        }

        double visibleWidth = TextRunMetrics.MeasurePrefix(_run, visibleLength);
        return new SimpleTextLine(
            this,
            symbol,
            visibleLength,
            visibleWidth,
            Math.Min(symbolWidth, targetWidth));
    }

    public override IList<TextCollapsedRange> GetTextCollapsedRanges() => _collapsedRanges;

    public override IEnumerable<IndexedGlyphRun> GetIndexedGlyphRuns()
    {
        double glyphStart = Start;
        int textSourceCharacterIndex = _firstCharIndex;
        foreach (TextSpan<TextRun> span in _runSpans)
        {
            GlyphRun? glyphRun = TextRunMetrics.CreateGlyphRun(
                span.Value,
                glyphStart,
                Baseline,
                PixelsPerDip);
            if (glyphRun is not null)
            {
                yield return new IndexedGlyphRun(textSourceCharacterIndex, span.Length, glyphRun);
                glyphStart += glyphRun.AdvanceWidths?.Sum() ?? 0;
            }

            textSourceCharacterIndex += span.Length;
        }
    }

    public override CharacterHit GetCharacterHitFromDistance(double distance)
    {
        if (distance <= Start || _length == 0)
        {
            return new CharacterHit(_firstCharIndex, 0);
        }

        double currentDistance = Start;
        for (int index = 0; index < _length; index++)
        {
            double advance = TextRunMetrics.GetCharacterWidth(_run, index);
            if (distance < currentDistance + advance)
            {
                int trailingLength = distance >= currentDistance + (advance / 2) ? 1 : 0;
                return new CharacterHit(_firstCharIndex + index, trailingLength);
            }

            currentDistance += advance;
        }

        return new CharacterHit(_firstCharIndex + _length - 1, 1);
    }

    public override double GetDistanceFromCharacterHit(CharacterHit characterHit)
    {
        int relativeIndex = Math.Clamp(characterHit.FirstCharacterIndex - _firstCharIndex, 0, _length);
        double distance = Start + TextRunMetrics.MeasurePrefix(_run, relativeIndex);
        if (characterHit.TrailingLength > 0 && relativeIndex < _length)
        {
            distance += TextRunMetrics.GetCharacterWidth(_run, relativeIndex);
        }

        return distance;
    }

    public override CharacterHit GetNextCaretCharacterHit(CharacterHit characterHit)
    {
        int position = characterHit.FirstCharacterIndex + Math.Max(1, characterHit.TrailingLength);
        return new CharacterHit(Math.Min(_firstCharIndex + _length, position), 0);
    }

    public override CharacterHit GetPreviousCaretCharacterHit(CharacterHit characterHit)
    {
        int position = characterHit.FirstCharacterIndex - (characterHit.TrailingLength == 0 ? 1 : 0);
        return new CharacterHit(Math.Max(_firstCharIndex, position), 0);
    }

    public override CharacterHit GetBackspaceCaretCharacterHit(CharacterHit characterHit)
        => GetPreviousCaretCharacterHit(characterHit);

    public override IList<TextBounds> GetTextBounds(int firstTextSourceCharacterIndex, int textLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textLength);
        int first = Math.Clamp(firstTextSourceCharacterIndex - _firstCharIndex, 0, _length);
        int last = Math.Clamp(first + textLength, first, _length);
        double start = Start + TextRunMetrics.MeasurePrefix(_run, first);
        double width = TextRunMetrics.MeasurePrefix(_run, last) - TextRunMetrics.MeasurePrefix(_run, first);
        var rectangle = new Rect(start, 0, width, Height);
        return
        [
            new TextBounds(
                rectangle,
                _flowDirection,
                [new TextRunBounds(rectangle, _firstCharIndex + first, last - first, _run)]),
        ];
    }

    public override TextLineBreak? GetTextLineBreak() => null;

    public override void Draw(DrawingContext drawingContext, Point origin, InvertAxes inversion)
    {
        // Basic text drawing - actual implementation would use the DrawingContext text APIs
    }
}
