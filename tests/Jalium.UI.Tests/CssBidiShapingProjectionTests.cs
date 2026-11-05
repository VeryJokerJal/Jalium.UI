using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssBidiShapingProjectionTests
{
    [Theory] [InlineData('\u200C')] [InlineData('\u200B')]
    public void PhysicalEdgesRetainParagraphScopesAndExactSourceOwnership(char separator)
    {
        const string source="AV אב";
        var projected=new CssBidiProjection(source,[new(0,5,CssUnicodeBidi.BidiOverride,FlowDirection.RightToLeft)],
            [3], [1],separator);
        Assert.Equal("\u202EA"+separator+"V \u2028אב\u202C",projected.Text);
        for(int i=0;i<=source.Length;i++)
        {
            Assert.Equal(i,projected.SourcePosition(projected.NativePosition(i)));
            Assert.Equal(i,projected.SourcePosition(projected.NativePosition(i,true)));
        }
        var first=projected.NativeRange(0,1);var second=projected.NativeRange(1,1);
        Assert.Equal("A",projected.Text.Substring(first.Start,first.Length));
        Assert.Equal("V",projected.Text.Substring(second.Start,second.Length));
        var fonts=new[]{projected.NativeStyleRange(0,1),projected.NativeStyleRange(1,4)};
        Assert.Equal(0,fonts[0].Start);Assert.Equal(projected.Text.Length,fonts.Sum(range=>range.Length));
    }

    [Fact]
    public void PhysicalEdgePositionsMustBeInteriorScalarBoundaries()
    {
        Assert.Throws<ArgumentException>(()=>new CssBidiProjection("A🙂B",[],shapingBreaks:[2]));
        Assert.Throws<ArgumentException>(()=>new CssBidiProjection("ABC",[],shapingBreaks:[0]));
        Assert.Throws<ArgumentException>(()=>new CssBidiProjection("ABC",[],shapingBreaks:[3]));
    }

    [Theory] [InlineData("-")] [InlineData("--")] [InlineData("")] [InlineData("a\u0301🙂")]
    public void ConditionalHyphenOwnsItsWholeDisplayWithoutChangingSourceOffsets(string display)
    {
        const string source = "A\u00ADB";
        var projection = new CssBidiProjection(source, [new(0, 3, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft)],
            [2], [1], discretionaryHyphens: new Dictionary<int, string> { [1] = display });
        Assert.Equal(source, projection.Source);
        Assert.Equal("\u2067A\u200C" + display + "\u2028B\u2069", projection.Text);
        var hyphen = projection.NativeRange(1, 1);
        Assert.Equal(display, projection.Text.Substring(hyphen.Start, hyphen.Length));
        for (int i = 0; i < display.Length; i++) Assert.Equal(1, projection.SourcePosition(hyphen.Start + i));
        Assert.Equal(2, projection.SourcePosition(projection.NativePosition(2)));
        var fonts = new[] { projection.NativeStyleRange(0, 1), projection.NativeStyleRange(1, 1), projection.NativeStyleRange(2, 1) };
        Assert.Equal(0, fonts[0].Start);
        Assert.Equal(fonts[0].Start + fonts[0].Length, fonts[1].Start);
        Assert.Equal(fonts[1].Start + fonts[1].Length, fonts[2].Start);
        Assert.Equal(projection.Text.Length, fonts.Sum(font => font.Length));
    }

    [Fact]
    public void ConditionalHyphenCannotReplaceAuthorTextOrAnOutOfRangePosition()
    {
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("A\u00ADB", [], discretionaryHyphens: new Dictionary<int, string> { [0] = "-" }));
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("A\u00ADB", [], discretionaryHyphens: new Dictionary<int, string> { [3] = "-" }));
    }

    [Fact]
    public void DisplayScalarsCanExpandContractAndDisappearInsideUnchangedSourceScopes()
    {
        const string source = "Aß🙂İ\u0307B";
        var projection = new CssBidiProjection(source, [new(0, source.Length, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft)],
            [6], [1], displayReplacements: new Dictionary<int, string> { [1] = "SS", [2] = "X", [4] = "i\u0307", [5] = "" });
        Assert.Equal(source, projection.Source); Assert.Equal("\u2067A\u200CSSXi\u0307\u2028B\u2069", projection.Text);
        var expanded = projection.NativeRange(1, 1); Assert.Equal("SS", projection.Text.Substring(expanded.Start, expanded.Length));
        Assert.Equal((1, 1), projection.SourceRange(expanded.Start, 1));
        Assert.Equal((1, 1), projection.SourceRange(expanded.Start + 1, 1));
        var supplementary = projection.NativeRange(2, 2); Assert.Equal("X", projection.Text.Substring(supplementary.Start, supplementary.Length));
        Assert.Equal((2, 2), projection.SourceRange(supplementary.Start, supplementary.Length));
        var combining = projection.NativeRange(4, 2); Assert.Equal("i\u0307", projection.Text.Substring(combining.Start, combining.Length));
        Assert.Equal((4, 2), projection.SourceRange(combining.Start, combining.Length)); Assert.Equal(0, projection.NativeRange(5, 1).Length);
        var fonts = new[] { projection.NativeStyleRange(0, 1), projection.NativeStyleRange(1, 3), projection.NativeStyleRange(4, 2), projection.NativeStyleRange(6, 1) };
        Assert.Equal(projection.Text.Length, fonts.Sum(range => range.Length));
        for (int i = 1; i < fonts.Length; i++) Assert.Equal(fonts[i - 1].Start + fonts[i - 1].Length, fonts[i].Start);
    }

    [Fact]
    public void ConditionalDisplayOverridesTheHiddenMappedSoftHyphen()
    {
        var projection = new CssBidiProjection("ß\u00AD", [], discretionaryHyphens: new Dictionary<int, string> { [1] = "--" },
            displayReplacements: new Dictionary<int, string> { [0] = "SS", [1] = "" });
        Assert.Equal("SS--", projection.Text); var hyphen = projection.NativeRange(1, 1);
        Assert.Equal((1, 1), projection.SourceRange(hyphen.Start, hyphen.Length));
    }

    [Theory] [InlineData(-1)] [InlineData(2)] [InlineData(4)]
    public void DisplayMapsRejectOutOfRangeAndInteriorSurrogateKeys(int index)
        => Assert.Throws<ArgumentException>(() => new CssBidiProjection("A🙂B", [], displayReplacements: new Dictionary<int, string> { [index] = "X" }));

    [Fact]
    public void SoftDisplayBreaksSplitExpandedDisplaySlicesWhilePreservingSourceOwnership()
    {
        const string source = "MßﬃM";
        var projection = new CssBidiProjection(source, [new(0, source.Length, CssUnicodeBidi.BidiOverride, FlowDirection.RightToLeft)],
            displayReplacements: new Dictionary<int, string> { [1] = "SS", [2] = "FFI" },
            softDisplayBreaks: [(1, 1), (2, 2)]);
        Assert.Equal("\u202EMS\u2028SFF\u2028IM\u202C", projection.Text);
        var sharpS = projection.NativeRange(1, 1);
        Assert.Equal("S\u2028S", projection.Text.Substring(sharpS.Start, sharpS.Length));
        Assert.Equal((1, 1), projection.SourceRange(sharpS.Start, 1));
        Assert.Equal((1, 1), projection.SourceRange(sharpS.Start + 2, 1));
        Assert.Equal(sharpS.Start + 1, projection.NativeDisplayPosition(1, 1, trailing: true));
        Assert.Equal(sharpS.Start + 2, projection.NativeDisplayPosition(1, 1, trailing: false));
        var ligature = projection.NativeRange(2, 1);
        Assert.Equal("FF\u2028I", projection.Text.Substring(ligature.Start, ligature.Length));
        Assert.Equal(ligature.Start + 2, projection.NativeDisplayPosition(2, 2, trailing: true));
        Assert.Equal(ligature.Start + 3, projection.NativeDisplayPosition(2, 2, trailing: false));
        var fonts = new[] { projection.NativeStyleRange(0, 1), projection.NativeStyleRange(1, 1), projection.NativeStyleRange(2, 1), projection.NativeStyleRange(3, 1) };
        Assert.Equal(projection.Text.Length, fonts.Sum(range => range.Length));
        for (int i = 1; i < fonts.Length; i++) Assert.Equal(fonts[i - 1].Start + fonts[i - 1].Length, fonts[i].Start);
    }
}
