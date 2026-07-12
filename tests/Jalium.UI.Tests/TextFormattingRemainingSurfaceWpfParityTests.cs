using System.Globalization;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Tests;

public sealed class TextFormattingRemainingSurfaceWpfParityTests
{
    [Fact]
    public unsafe void CharacterBufferReference_ArrayAndPointerConstructorsPreserveTheirRanges()
    {
        char[] buffer = "abcd".ToCharArray();
        var arrayReference = new CharacterBufferReference(buffer, 1);
        var copiedReference = arrayReference;

        Assert.Equal("abcd", arrayReference.CharacterBuffer);
        Assert.Equal(1, arrayReference.OffsetToFirstChar);
        Assert.Equal(arrayReference, copiedReference);
        Assert.NotEqual(arrayReference, new CharacterBufferReference(buffer, 1));

        buffer[1] = 'x';
        Assert.Equal("axcd", arrayReference.CharacterBuffer);

        CharacterBufferReference pointerReference;
        fixed (char* pointer = buffer)
        {
            pointerReference = new CharacterBufferReference(pointer + 1, 2);
        }

        Assert.Equal("xc", pointerReference.CharacterBuffer);
        Assert.Equal(0, pointerReference.OffsetToFirstChar);
        Assert.Equal("offsetToFirstChar", Assert.Throws<ArgumentOutOfRangeException>(
            () => new CharacterBufferReference(buffer, -1)).ParamName);
        Assert.Equal("offsetToFirstChar", Assert.Throws<ArgumentOutOfRangeException>(
            () => new CharacterBufferReference(buffer, buffer.Length)).ParamName);
    }

    [Fact]
    public unsafe void TextCharacters_ArrayAndPointerConstructorsPreserveBufferLengthAndProperties()
    {
        var properties = new TestRunProperties();
        char[] buffer = "sample".ToCharArray();
        var fromArray = new TextCharacters(buffer, 2, 3, properties);

        Assert.Equal("sample", fromArray.CharacterBufferReference.CharacterBuffer);
        Assert.Equal(2, fromArray.CharacterBufferReference.OffsetToFirstChar);
        Assert.Equal(3, fromArray.Length);
        Assert.Same(properties, fromArray.Properties);

        TextCharacters fromPointer;
        fixed (char* pointer = buffer)
        {
            fromPointer = new TextCharacters(pointer + 1, 4, properties);
        }

        Assert.Equal("ampl", fromPointer.CharacterBufferReference.CharacterBuffer);
        Assert.Equal(0, fromPointer.CharacterBufferReference.OffsetToFirstChar);
        Assert.Equal(4, fromPointer.Length);
        Assert.Same(properties, fromPointer.Properties);
    }

    [Fact]
    public void TextCharacters_ApplyWpfRunValidation()
    {
        var properties = new TestRunProperties();

        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextCharacters("x", 0, 0, properties)).ParamName);
        Assert.Equal("textRunProperties", Assert.Throws<ArgumentNullException>(
            () => new TextCharacters("x", null!)).ParamName);
        Assert.Equal("textRunProperties.Typeface", Assert.Throws<ArgumentNullException>(
            () => new TextCharacters("x", new TestRunProperties(hasTypeface: false))).ParamName);
        Assert.Equal("textRunProperties.CultureInfo", Assert.Throws<ArgumentNullException>(
            () => new TextCharacters("x", new TestRunProperties(hasCultureInfo: false))).ParamName);
        Assert.Equal("textRunProperties.FontRenderingEmSize", Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextCharacters("x", new TestRunProperties(fontRenderingEmSize: 0))).ParamName);
    }

    [Fact]
    public void TextRunPropertiesAndParagraphProperties_ExposeWpfDefaults()
    {
        var runProperties = new TestRunProperties(fontRenderingEmSize: 11);
        Assert.Equal(1.0, runProperties.PixelsPerDip);
        runProperties.PixelsPerDip = 1.75;
        Assert.Equal(1.75, runProperties.PixelsPerDip);

        var paragraphProperties = new TestParagraphProperties(runProperties);
        Assert.False(paragraphProperties.AlwaysCollapsible);
        Assert.Null(paragraphProperties.TextDecorations);
        Assert.Equal(44, paragraphProperties.DefaultIncrementalTab);
    }

    [Fact]
    public void TextEmbeddedObject_ExposesLineBreakConditions()
    {
        Assert.True(typeof(TextEmbeddedObject).GetProperty(
            nameof(TextEmbeddedObject.BreakBefore),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetMethod!.IsAbstract);
        Assert.True(typeof(TextEmbeddedObject).GetProperty(
            nameof(TextEmbeddedObject.BreakAfter),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.GetMethod!.IsAbstract);

        var embeddedObject = new TestEmbeddedObject();
        Assert.Equal(LineBreakCondition.BreakRestrained, embeddedObject.BreakBefore);
        Assert.Equal(LineBreakCondition.BreakPossible, embeddedObject.BreakAfter);
    }

    [Fact]
    public void TextFormatter_CacheOverloadsReuseRunsAndDriveLineAndParagraphFormatting()
    {
        var runProperties = new TestRunProperties(fontRenderingEmSize: 10);
        var run = new TextCharacters("hello world", runProperties);
        var source = new TrackingTextSource(run);
        var paragraphProperties = new TestParagraphProperties(runProperties);
        var cache = new TextRunCache();
        using TextFormatter formatter = TextFormatter.Create();

        using TextLine firstLine = formatter.FormatLine(source, 0, 100, paragraphProperties, null, cache);
        Assert.Equal(run.Length, firstLine.Length);
        TextSpan<TextRun> firstSpan = Assert.Single(firstLine.GetTextRunSpans());
        Assert.Equal(run.Length, firstSpan.Length);
        Assert.Same(run, firstSpan.Value);
        Assert.InRange(firstLine.Width, 0.001, 100);
        Assert.Equal(1, source.GetTextRunCallCount);

        using TextLine secondLine = formatter.FormatLine(source, 0, 100, paragraphProperties, null, cache);
        Assert.Equal(firstLine.Width, secondLine.Width);
        Assert.Equal(1, source.GetTextRunCallCount);

        MinMaxParagraphWidth widths = formatter.FormatMinMaxParagraphWidth(source, 0, paragraphProperties, cache);
        Assert.Equal(2, source.GetTextRunCallCount);
        Assert.True(widths.MinWidth > 0);
        Assert.True(widths.MaxWidth > widths.MinWidth);

        cache.Change(0, addition: 1, removal: 0);
        using TextLine afterChange = formatter.FormatLine(source, 0, 100, paragraphProperties, null, cache);
        Assert.Equal(3, source.GetTextRunCallCount);

        cache.Invalidate();
        using TextLine afterInvalidate = formatter.FormatLine(source, 0, 100, paragraphProperties, null, cache);
        Assert.Equal(4, source.GetTextRunCallCount);
    }

    private sealed class TrackingTextSource(TextRun run) : TextSource
    {
        public int GetTextRunCallCount { get; private set; }

        public override TextRun GetTextRun(int textSourceCharacterIndex)
        {
            GetTextRunCallCount++;
            return textSourceCharacterIndex == 0 ? run : new TextEndOfParagraph(1);
        }

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
            => new(0, new CultureSpecificCharacterBufferRange(null, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
            => textSourceCharacterIndex;
    }

    private sealed class TestRunProperties(
        bool hasTypeface = true,
        bool hasCultureInfo = true,
        double fontRenderingEmSize = 12) : TextRunProperties
    {
        public override Typeface Typeface { get; } = hasTypeface ? new Typeface("Arial") : null!;

        public override double FontRenderingEmSize { get; } = fontRenderingEmSize;

        public override double FontHintingEmSize => FontRenderingEmSize;

        public override Brush? ForegroundBrush => null;

        public override Brush? BackgroundBrush => null;

        public override CultureInfo CultureInfo { get; } = hasCultureInfo ? CultureInfo.InvariantCulture : null!;

        public override TextDecorationCollection? TextDecorations => null;

        public override TextEffectCollection? TextEffects => null;
    }

    private sealed class TestParagraphProperties(TextRunProperties defaultRunProperties) : TextParagraphProperties
    {
        public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
        public override TextAlignment TextAlignment => TextAlignment.Left;
        public override double LineHeight => double.NaN;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties { get; } = defaultRunProperties;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override double Indent => 0;
    }

    private sealed class TestEmbeddedObject : TextEmbeddedObject
    {
        public override LineBreakCondition BreakBefore => LineBreakCondition.BreakRestrained;
        public override LineBreakCondition BreakAfter => LineBreakCondition.BreakPossible;
        public override bool HasFixedSize => true;
        public override CharacterBufferReference CharacterBufferReference => default;
        public override int Length => 1;
        public override TextRunProperties? Properties => null;
        public override TextEmbeddedObjectMetrics Format(double remainingParagraphWidth) => new(8, 12, 9);
        public override Rect ComputeBoundingBox(bool rightToLeft, bool sideways) => new(0, 0, 8, 12);
        public override void Draw(DrawingContext drawingContext, Point origin, bool rightToLeft, bool sideways)
        {
        }
    }
}
