using System.Globalization;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Tests;

public sealed class TextFormattingFinalGapParityTests
{
    [Fact]
    public void MissingTypes_ExposeExactWpfShapesAndParameterNames()
    {
        Assert.True(typeof(CharacterBufferRange).IsValueType);
        Assert.Contains(typeof(IEquatable<CharacterBufferRange>), typeof(CharacterBufferRange).GetInterfaces());

        Assert.True(typeof(IndexedGlyphRun).IsSealed);
        Assert.Empty(typeof(IndexedGlyphRun).GetConstructors());
        Assert.Equal(typeof(GlyphRun), typeof(IndexedGlyphRun).GetProperty(nameof(IndexedGlyphRun.GlyphRun))!.PropertyType);

        Assert.True(typeof(TextCollapsedRange).IsSealed);
        Assert.Empty(typeof(TextCollapsedRange).GetConstructors());

        ConstructorInfo endOfSegmentConstructor = Assert.Single(typeof(TextEndOfSegment).GetConstructors());
        Assert.Equal("length", Assert.Single(endOfSegmentConstructor.GetParameters()).Name);
        Assert.Equal(typeof(TextRun), typeof(TextEndOfSegment).BaseType);
        Assert.False(typeof(TextEndOfSegment).IsSealed);

        Assert.True(typeof(TextModifier).IsAbstract);
        Assert.Equal(typeof(TextRun), typeof(TextModifier).BaseType);
        MethodInfo modifyProperties = typeof(TextModifier).GetMethod(nameof(TextModifier.ModifyProperties))!;
        Assert.True(modifyProperties.IsAbstract);
        Assert.Equal("properties", Assert.Single(modifyProperties.GetParameters()).Name);

        Assert.True(typeof(TextRunTypographyProperties).IsAbstract);
        Assert.Equal(43, typeof(TextRunTypographyProperties).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Length);
        Assert.All(
            typeof(TextRunTypographyProperties).GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            property => Assert.True(property.GetMethod!.IsAbstract));
        MethodInfo onPropertiesChanged = typeof(TextRunTypographyProperties).GetMethod(
            "OnPropertiesChanged",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(onPropertiesChanged.IsFamily);
        Assert.False(onPropertiesChanged.IsVirtual);

        ConstructorInfo markerConstructor = Assert.Single(typeof(TextSimpleMarkerProperties).GetConstructors());
        Assert.Equal(
            [typeof(TextMarkerStyle), typeof(double), typeof(int), typeof(TextParagraphProperties)],
            markerConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["style", "offset", "autoNumberingIndex", "textParagraphProperties"],
            markerConstructor.GetParameters().Select(parameter => parameter.Name));

        ConstructorInfo cultureRangeConstructor = Assert.Single(
            typeof(CultureSpecificCharacterBufferRange).GetConstructors());
        Assert.Equal(
            [typeof(CultureInfo), typeof(CharacterBufferRange)],
            cultureRangeConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            ["culture", "characterBufferRange"],
            cultureRangeConstructor.GetParameters().Select(parameter => parameter.Name));
        Assert.Equal(
            typeof(CharacterBufferRange),
            typeof(CultureSpecificCharacterBufferRange).GetProperty(
                nameof(CultureSpecificCharacterBufferRange.CharacterBufferRange))!.PropertyType);
        Assert.Null(typeof(CultureSpecificCharacterBufferRange).GetProperty("Length"));
    }

    [Fact]
    public void TextLine_ExposesExactFinalGapContracts()
    {
        ConstructorInfo pixelsPerDipConstructor = Assert.Single(
            typeof(TextLine).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            constructor => constructor.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(double));
        Assert.True(pixelsPerDipConstructor.IsFamily);
        Assert.Equal("pixelsPerDip", Assert.Single(pixelsPerDipConstructor.GetParameters()).Name);

        AssertAbstractProperty<int>(nameof(TextLine.DependentLength));
        AssertAbstractProperty<double>(nameof(TextLine.Extent));
        AssertAbstractProperty<int>(nameof(TextLine.TrailingWhitespaceLength));

        PropertyInfo pixelsPerDip = typeof(TextLine).GetProperty(nameof(TextLine.PixelsPerDip))!;
        Assert.Equal(typeof(double), pixelsPerDip.PropertyType);
        Assert.NotNull(pixelsPerDip.GetMethod);
        Assert.NotNull(pixelsPerDip.SetMethod);

        PropertyInfo isTruncated = typeof(TextLine).GetProperty(nameof(TextLine.IsTruncated))!;
        Assert.True(isTruncated.GetMethod!.IsVirtual);
        Assert.False(isTruncated.GetMethod.IsAbstract);

        MethodInfo collapse = typeof(TextLine).GetMethod(nameof(TextLine.Collapse))!;
        Assert.True(collapse.IsAbstract);
        Assert.Equal(typeof(TextLine), collapse.ReturnType);
        ParameterInfo collapseParameter = Assert.Single(collapse.GetParameters());
        Assert.Equal("collapsingPropertiesList", collapseParameter.Name);
        Assert.NotNull(collapseParameter.GetCustomAttribute<ParamArrayAttribute>());

        Assert.Equal(
            typeof(IEnumerable<IndexedGlyphRun>),
            typeof(TextLine).GetMethod(nameof(TextLine.GetIndexedGlyphRuns))!.ReturnType);
        Assert.Equal(
            typeof(IList<TextCollapsedRange>),
            typeof(TextLine).GetMethod(nameof(TextLine.GetTextCollapsedRanges))!.ReturnType);
        Assert.Equal(
            typeof(IList<TextSpan<TextRun>>),
            typeof(TextLine).GetMethod(nameof(TextLine.GetTextRunSpans))!.ReturnType);

        PropertyInfo typography = typeof(TextRunProperties).GetProperty(
            nameof(TextRunProperties.TypographyProperties))!;
        Assert.Equal(typeof(TextRunTypographyProperties), typography.PropertyType);
        Assert.True(typography.GetMethod!.IsVirtual);
        Assert.False(typography.GetMethod.IsAbstract);
    }

    [Fact]
    public unsafe void CharacterBufferRange_PreservesLiveRangesIdentityAndValidation()
    {
        char[] characters = "range".ToCharArray();
        var range = new CharacterBufferRange(characters, 1, 3);
        CharacterBufferRange copy = range;

        Assert.Equal(3, range.Length);
        Assert.Equal(1, range.CharacterBufferReference.OffsetToFirstChar);
        Assert.Equal(range, copy);
        Assert.NotEqual(range, new CharacterBufferRange(characters, 1, 3));

        characters[2] = 'X';
        Assert.Equal('X', range.GetCharacter(1));

        CharacterBufferRange pointerRange;
        fixed (char* pointer = characters)
        {
            pointerRange = new CharacterBufferRange(pointer + 1, 2);
        }

        Assert.Equal("aX", pointerRange.CharacterBufferReference.CharacterBuffer);
        Assert.Equal(2, pointerRange.Length);
        Assert.Equal(CharacterBufferRange.Empty, default);
        Assert.Equal(
            "characterLength",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CharacterBufferRange(characters, 3, 3)).ParamName);
    }

    [Fact]
    public void EndOfSegmentAndCultureRange_PreserveTextData()
    {
        var endOfSegment = new TextEndOfSegment(2);
        Assert.Equal(2, endOfSegment.Length);
        Assert.Equal(default, endOfSegment.CharacterBufferReference);
        Assert.Null(endOfSegment.Properties);
        Assert.Equal(
            "length",
            Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfSegment(0)).ParamName);

        var characterRange = new CharacterBufferRange("culture", 0, 7);
        var cultureRange = new CultureSpecificCharacterBufferRange(CultureInfo.GetCultureInfo("en-US"), characterRange);
        Assert.Equal("en-US", cultureRange.CultureInfo!.Name);
        Assert.Equal(characterRange, cultureRange.CharacterBufferRange);
    }

    [Theory]
    [InlineData(TextMarkerStyle.Decimal, 28, "28.")]
    [InlineData(TextMarkerStyle.LowerLatin, 27, "aa.")]
    [InlineData(TextMarkerStyle.UpperLatin, 52, "AZ.")]
    [InlineData(TextMarkerStyle.LowerRoman, 14, "xiv.")]
    [InlineData(TextMarkerStyle.UpperRoman, 14, "XIV.")]
    public void TextSimpleMarkerProperties_GeneratesNumberedMarkerText(
        TextMarkerStyle style,
        int index,
        string expected)
    {
        var runProperties = new TestRunProperties();
        var marker = new TextSimpleMarkerProperties(
            style,
            offset: 16,
            autoNumberingIndex: index,
            new TestParagraphProperties(runProperties));

        Assert.Equal(16, marker.Offset);
        TextCharacters run = Assert.IsType<TextCharacters>(marker.TextSource.GetTextRun(0));
        Assert.Equal(expected, ReadText(run));
        Assert.Same(runProperties, run.Properties);

        TextSpan<CultureSpecificCharacterBufferRange> preceding = marker.TextSource.GetPrecedingText(expected.Length);
        Assert.Equal(expected.Length, preceding.Length);
        Assert.Equal(expected.Length, preceding.Value.CharacterBufferRange.Length);
    }

    [Fact]
    public void TextSimpleMarkerProperties_HandlesSymbolsNoneAndInvalidInputs()
    {
        var paragraphProperties = new TestParagraphProperties(new TestRunProperties());
        var symbol = new TextSimpleMarkerProperties(TextMarkerStyle.Disc, 5, 0, paragraphProperties);
        TextCharacters symbolRun = Assert.IsType<TextCharacters>(symbol.TextSource.GetTextRun(0));
        Assert.Equal("\u009f", ReadText(symbolRun));
        Assert.Equal("Wingdings", symbolRun.Properties.Typeface.FontFamily.Source);

        var none = new TextSimpleMarkerProperties(TextMarkerStyle.None, 3, 0, paragraphProperties);
        Assert.Null(none.TextSource);

        Assert.Equal(
            "autoNumberingIndex",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new TextSimpleMarkerProperties(TextMarkerStyle.Decimal, 0, 0, paragraphProperties)).ParamName);
        Assert.Equal(
            "style",
            Assert.Throws<ArgumentException>(
                () => new TextSimpleMarkerProperties((TextMarkerStyle)100, 0, 1, paragraphProperties)).ParamName);
        Assert.Equal(
            "textParagraphProperties",
            Assert.Throws<ArgumentNullException>(
                () => new TextSimpleMarkerProperties(TextMarkerStyle.Disc, 0, 0, null!)).ParamName);
    }

    [Fact]
    public void SimpleTextLine_ProvidesRunSpansGlyphsMetricsAndCharacterCollapse()
    {
        var runProperties = new TestRunProperties(fontRenderingEmSize: 10);
        var run = new TextCharacters("hello world   ", runProperties);
        var source = new TestTextSource(run) { PixelsPerDip = 1.5 };
        var paragraphProperties = new TestParagraphProperties(runProperties);
        using TextFormatter formatter = TextFormatter.Create();
        using TextLine line = formatter.FormatLine(source, 0, 20, paragraphProperties, null);

        Assert.Equal(1.5, line.PixelsPerDip);
        Assert.Equal(3, line.TrailingWhitespaceLength);
        Assert.Equal(0, line.DependentLength);
        Assert.Equal(line.Height, line.Extent);
        Assert.True(line.HasOverflowed);
        Assert.True(line.IsTruncated);

        TextSpan<TextRun> span = Assert.Single(line.GetTextRunSpans());
        Assert.Equal(run.Length, span.Length);
        Assert.Same(run, span.Value);

        IndexedGlyphRun indexedGlyphRun = Assert.Single(line.GetIndexedGlyphRuns());
        Assert.Equal(0, indexedGlyphRun.TextSourceCharacterIndex);
        Assert.Equal(run.Length, indexedGlyphRun.TextSourceLength);
        Assert.Equal(run.Length, indexedGlyphRun.GlyphRun.Characters!.Count);
        Assert.Equal(run.Length, indexedGlyphRun.GlyphRun.GlyphIndices!.Count);
        Assert.Equal(run.Length, indexedGlyphRun.GlyphRun.AdvanceWidths!.Count);
        Assert.Equal(1.5f, indexedGlyphRun.GlyphRun.PixelsPerDip);

        using TextLine collapsed = line.Collapse(new TextTrailingCharacterEllipsis(25, runProperties));
        Assert.NotSame(line, collapsed);
        Assert.True(collapsed.HasCollapsed);
        Assert.False(collapsed.IsTruncated);
        Assert.InRange(collapsed.Width, 0, 25);
        Assert.Equal(2, collapsed.GetTextRunSpans().Count);
        Assert.Equal(2, collapsed.GetIndexedGlyphRuns().Count());

        TextCollapsedRange collapsedRange = Assert.Single(collapsed.GetTextCollapsedRanges());
        Assert.Equal(4, collapsedRange.TextSourceCharacterIndex);
        Assert.Equal(run.Length - 4, collapsedRange.Length);
        Assert.True(collapsedRange.Width > 0);
        Assert.Empty(line.GetTextCollapsedRanges());
    }

    [Fact]
    public void SimpleTextLine_WordCollapseAndCaretMetricsHonorTextBoundaries()
    {
        var runProperties = new TestRunProperties(fontRenderingEmSize: 10);
        var run = new TextCharacters("hello world", runProperties);
        var paragraphProperties = new TestParagraphProperties(runProperties);
        using TextFormatter formatter = TextFormatter.Create();
        using TextLine line = formatter.FormatLine(
            new TestTextSource(run),
            10,
            100,
            paragraphProperties,
            null);
        using TextLine collapsed = line.Collapse(new TextTrailingWordEllipsis(35, runProperties));

        TextCollapsedRange range = Assert.Single(collapsed.GetTextCollapsedRanges());
        Assert.Equal(15, range.TextSourceCharacterIndex);
        Assert.Equal(6, range.Length);

        Assert.Equal(new CharacterHit(10, 0), line.GetCharacterHitFromDistance(0));
        Assert.Equal(5, line.GetDistanceFromCharacterHit(new CharacterHit(11, 0)));
        Assert.Equal(new CharacterHit(11, 0), line.GetNextCaretCharacterHit(new CharacterHit(10, 0)));
        Assert.Equal(new CharacterHit(10, 0), line.GetPreviousCaretCharacterHit(new CharacterHit(11, 0)));

        TextBounds bounds = Assert.Single(line.GetTextBounds(10, 5));
        Assert.Equal(25, bounds.Rectangle.Width);
        TextRunBounds runBounds = Assert.Single(bounds.TextRunBounds!);
        Assert.Equal(10, runBounds.TextSourceCharacterIndex);
        Assert.Equal(5, runBounds.Length);
    }

    private static void AssertAbstractProperty<T>(string propertyName)
    {
        PropertyInfo property = typeof(TextLine).GetProperty(propertyName)!;
        Assert.Equal(typeof(T), property.PropertyType);
        Assert.True(property.GetMethod!.IsAbstract);
    }

    private static string ReadText(TextCharacters run)
    {
        CharacterBufferReference reference = run.CharacterBufferReference;
        return new string(
            reference.CharacterBuffer
                .Skip(reference.OffsetToFirstChar)
                .Take(run.Length)
                .ToArray());
    }

    private sealed class TestTextSource(TextRun run) : TextSource
    {
        public override TextRun GetTextRun(int textSourceCharacterIndex)
            => textSourceCharacterIndex == 0 || textSourceCharacterIndex == 10
                ? run
                : new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(
            int textSourceCharacterIndexLimit)
            => new(0, new CultureSpecificCharacterBufferRange(null, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(
            int textSourceCharacterIndex)
            => textSourceCharacterIndex;
    }

    private sealed class TestRunProperties(double fontRenderingEmSize = 12) : TextRunProperties
    {
        public override Typeface Typeface { get; } = new("Arial");
        public override double FontRenderingEmSize { get; } = fontRenderingEmSize;
        public override double FontHintingEmSize => FontRenderingEmSize;
        public override Brush? ForegroundBrush => null;
        public override Brush? BackgroundBrush => null;
        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;
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
}
