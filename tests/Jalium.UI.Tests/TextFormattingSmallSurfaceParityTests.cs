using System.Globalization;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Media.TextFormatting;

namespace Jalium.UI.Tests;

public sealed class TextFormattingSmallSurfaceParityTests
{
    [Fact]
    public void EndRuns_ExposeWpfConstructorsAndPreserveRunProperties()
    {
        Assert.NotNull(typeof(TextEndOfLine).GetConstructor([typeof(int), typeof(TextRunProperties)]));
        Assert.NotNull(typeof(TextEndOfParagraph).GetConstructor([typeof(int), typeof(TextRunProperties)]));

        var properties = new TestRunProperties(new Typeface("Arial"));
        var endOfLine = new TextEndOfLine(2, properties);
        var endOfParagraph = new TextEndOfParagraph(3, properties);

        Assert.Equal(2, endOfLine.Length);
        Assert.Equal(3, endOfParagraph.Length);
        Assert.Same(properties, endOfLine.Properties);
        Assert.Same(properties, endOfParagraph.Properties);
        Assert.Equal(default, endOfLine.CharacterBufferReference);
        Assert.Equal(default, endOfParagraph.CharacterBufferReference);
    }

    [Fact]
    public void EndRuns_ApplyWpfLengthAndTypefaceValidation()
    {
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfLine(0)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfLine(-1)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfParagraph(0)).ParamName);
        Assert.Equal("length", Assert.Throws<ArgumentOutOfRangeException>(() => new TextEndOfParagraph(-1)).ParamName);

        var properties = new TestRunProperties(null!);
        Assert.Equal(
            "textRunProperties.Typeface",
            Assert.Throws<ArgumentNullException>(() => new TextEndOfLine(1, properties)).ParamName);
        Assert.Equal(
            "textRunProperties.Typeface",
            Assert.Throws<ArgumentNullException>(() => new TextEndOfParagraph(1, properties)).ParamName);

        Assert.Null(new TextEndOfLine(1, null).Properties);
        Assert.Null(new TextEndOfParagraph(1, null).Properties);
    }

    [Fact]
    public void TextLineBreak_CloneSharesScopeButOwnsAnIndependentBreakRecord()
    {
        Assert.Null(typeof(TextLineBreak).GetConstructor(Type.EmptyTypes));
        Assert.Equal(
            typeof(TextLineBreak),
            typeof(TextLineBreak).GetMethod(
                nameof(TextLineBreak.Clone),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!.ReturnType);

        var scope = new object();
        var continuationState = new object();
        var original = new TextLineBreak(scope, continuationState);
        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Same(scope, original.TextModifierScope);
        Assert.Same(scope, clone.TextModifierScope);
        Assert.Same(continuationState, original.ContinuationState);
        Assert.Same(continuationState, clone.ContinuationState);
        Assert.NotSame(original.BreakRecordIdentity, clone.BreakRecordIdentity);

        original.Dispose();
        original.Dispose();
        Assert.False(original.HasBreakRecord);
        Assert.True(clone.HasBreakRecord);

        var disposedClone = original.Clone();
        Assert.Same(scope, disposedClone.TextModifierScope);
        Assert.False(disposedClone.HasBreakRecord);

        clone.Dispose();
        Assert.False(clone.HasBreakRecord);
    }

    [Fact]
    public void TextSource_PixelsPerDipDefaultsToOneAndStoresValuesVerbatim()
    {
        var source = new TestTextSource();
        Assert.Equal(1.0, source.PixelsPerDip);

        foreach (double value in new[]
                 {
                     1.25,
                     0.0,
                     -1.0,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                 })
        {
            source.PixelsPerDip = value;
            Assert.Equal(value, source.PixelsPerDip);
        }
    }

    private sealed class TestTextSource : TextSource
    {
        public override TextRun GetTextRun(int textSourceCharacterIndex) => new TextEndOfParagraph(1);

        public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
            => new(0, new CultureSpecificCharacterBufferRange(null, CharacterBufferRange.Empty));

        public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
            => textSourceCharacterIndex;
    }

    private sealed class TestRunProperties(Typeface typeface) : TextRunProperties
    {
        public override Typeface Typeface { get; } = typeface;

        public override double FontRenderingEmSize => 12.0;

        public override double FontHintingEmSize => 12.0;

        public override Brush? ForegroundBrush => null;

        public override Brush? BackgroundBrush => null;

        public override CultureInfo CultureInfo => CultureInfo.InvariantCulture;

        public override TextDecorationCollection? TextDecorations => null;

        public override TextEffectCollection? TextEffects => null;
    }
}
