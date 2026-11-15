using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssUnicodeBidiTests
{
    [Theory]
    [InlineData("normal", 0)] [InlineData("embed", 1)] [InlineData("isolate", 2)]
    [InlineData("bidi-override", 3)] [InlineData("isolate-override", 4)] [InlineData("plaintext", 5)]
    [InlineData("ISOLATE-OVERRIDE", 4)]
    public void AuthorValuesReachComputedStyleAndQueries(string value, int expected)
    {
        Assert.True(Css.Supports("unicode-bidi", value));
        Assert.True(CssAuthoring.IsValueSupported("unicode-bidi", value.ToLowerInvariant()));
        var text = new TextBlock(); Attach(text, "unicode-bidi:" + value);
        Assert.Equal((CssUnicodeBidi)expected, CssUnicodeBidiProperties.Value(text));
        var node = CssNode.Get(text);
        Assert.True(CssOrdinaryStyleQuery.IsSupported("unicode-bidi"));
        Assert.True(CssOrdinaryStyleQuery.Matches("unicode-bidi", value, node, CssEngine.BuildLengthContext(node)));
    }

    [Theory]
    [InlineData("auto")] [InlineData("isolate override")] [InlineData("isolate,embed")]
    [InlineData("bidi-override none")] [InlineData("'plaintext'")] [InlineData("embed()")] [InlineData("123")]
    public void InvalidGrammarIsRejectedWithoutReplacingTheEarlierValue(string value)
    {
        Assert.False(Css.Supports("unicode-bidi", value));
        var text = new TextBlock(); Attach(text, "unicode-bidi:isolate;unicode-bidi:" + value);
        Assert.Equal(CssUnicodeBidi.Isolate, CssUnicodeBidiProperties.Value(text));
    }

    [Fact]
    public void NonInheritedPropertyStillSupportsExplicitInheritanceAndExcludesAll()
    {
        var root = new StackPanel(); var child = new TextBlock(); root.Children.Add(child);
        Attach(root, "unicode-bidi:bidi-override;direction:rtl");
        Assert.Equal(CssUnicodeBidi.Normal, CssUnicodeBidiProperties.Value(child));
        Assert.Equal(FlowDirection.RightToLeft, child.FlowDirection);
        Attach(child, "unicode-bidi:inherit;all:initial");
        Assert.Equal(CssUnicodeBidi.BidiOverride, CssUnicodeBidiProperties.Value(child));
        Attach(child, "unicode-bidi:unset");
        Assert.Equal(CssUnicodeBidi.Normal, CssUnicodeBidiProperties.Value(child));
        Attach(child, "unicode-bidi:initial");
        Assert.Equal(CssUnicodeBidi.Normal, CssUnicodeBidiProperties.Value(child));
    }

    [Fact]
    public void VariablesAndLayerRollbackKeepOrdinaryCascadeSemantics()
    {
        var text = new TextBlock(); Css.SetClass(text, "subject");
        var sheet = CssStyleSheet.Parse("@layer base,over;@layer base{.subject{--scope:isolate;unicode-bidi:var(--scope)}}@layer over{.subject{unicode-bidi:plaintext;unicode-bidi:revert-layer}}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(text).Add(sheet); Layout(text);
        Assert.Equal(CssUnicodeBidi.Isolate, CssUnicodeBidiProperties.Value(text));
        Css.SetStyle(text, "--scope:bidi-override"); Layout(text);
        Assert.Equal(CssUnicodeBidi.BidiOverride, CssUnicodeBidiProperties.Value(text));
    }

    [Fact]
    public void GeneratedStylesheetUsesTheSamePropertyContract()
    {
        var builder = new CssStyleSheetBuilder(1);
        var compound = builder.Compound(null, false, null, false, false, false, null, ["subject"], null, 0, null, null);
        var selector = builder.Selector([compound], [], 256, 0, 0, false, false);
        builder.Rule([selector], [builder.Declaration("unicode-bidi", "isolate-override", false)], -1, null, -1);
        var text = new TextBlock(); Css.SetClass(text, "subject"); Css.GetStyleSheets(text).Add(builder.Build()); Layout(text);
        Assert.Equal(CssUnicodeBidi.IsolateOverride, CssUnicodeBidiProperties.Value(text));
    }

    [Fact]
    public void DirectionControlsCannotBeAnimatedEvenWithDiscreteTransitions()
    {
        var text = new TextBlock(); Attach(text, "transition:all 1s allow-discrete;unicode-bidi:isolate");
        var transitions = Assert.IsType<CssTransitionData>(text.GetValue(CssTransitions.DataProperty));
        Assert.Null(transitions.Find(text, CssUnicodeBidiProperties.ValueProperty));
        Assert.Null(transitions.Find(text, FrameworkElement.FlowDirectionProperty));
        Assert.False(CssCoreProperties.TryGetTransitionTargetName("direction", out _));
        Assert.False(CssCoreProperties.TryGetTransitionTargetName("unicode-bidi", out _));
        var sheet = CssStyleSheet.Parse("@keyframes flow{from{unicode-bidi:normal;direction:ltr;color:red}to{unicode-bidi:plaintext;direction:rtl;color:blue}}");
        var frames = CssKeyframes.CompileFrames(Assert.Single(sheet.Keyframes));
        Assert.Equal(2, frames.Length);
        Assert.All(frames, frame => { Assert.Single(frame.Declarations); Assert.DoesNotContain(frame.Declarations, value => value.Name == "unicode-bidi"); });
        Assert.False(CssFirstLine.Applies("unicode-bidi"));
        Assert.Contains(CssListProperties.MarkerDefaults.Declarations, value => value.PropertyName == "unicode-bidi" && value.RawValue == "isolate");
    }

    [Theory]
    [InlineData(1, false, "", "")] [InlineData(2, false, "", "")]
    [InlineData(1, true, "\u202B", "\u202C")] [InlineData(2, true, "\u2067", "\u2069")]
    [InlineData(3, false, "\u202E", "\u202C")] [InlineData(3, true, "\u202E", "\u202C")]
    [InlineData(4, true, "\u2068\u202E", "\u202C\u2069")]
    [InlineData(4, false, "\u202E", "\u202C")] [InlineData(5, true, "\u2068", "\u2069")]
    public void FormattingScopesMatchWritingModesControlEquivalents(int mode, bool inline, string open, string close)
    {
        var projection = new CssBidiProjection("CSS אב", [new(0, 6, (CssUnicodeBidi)mode, FlowDirection.RightToLeft, inline)]);
        Assert.Equal(open + "CSS אב" + close, projection.Text);
        Assert.False(projection.NaturalParagraphDirection);
        Assert.Equal("CSS אב", projection.Source);
    }

    [Fact]
    public void RootPlaintextResolvesParagraphDirectionWithoutCreatingAnIsolate()
    {
        var projection = new CssBidiProjection("אב CSS", [new(0, 6, CssUnicodeBidi.Plaintext, FlowDirection.LeftToRight, false)]);
        Assert.True(projection.NaturalParagraphDirection); Assert.Equal(projection.Source, projection.Text);
    }

    [Fact]
    public void NestedScopesRestartAfterParagraphBreaksAndKeepLineSeparatorInsideTheScope()
    {
        const string source = "aאב\r\nCD\u2028ef\nZ";
        var projection = new CssBidiProjection(source, [new(0, source.Length, CssUnicodeBidi.Isolate, FlowDirection.LeftToRight),
            new(1, source.Length - 2, CssUnicodeBidi.BidiOverride, FlowDirection.RightToLeft)]);
        Assert.Equal("\u2066a\u202Eאב\u202C\u2069\r\n\u2066\u202ECD\u2028ef\u202C\u2069\n\u2066Z\u2069", projection.Text);
    }

    [Fact]
    public void Utf16SourcePositionsRoundTripAcrossControlsAndNestedBoundaryAffinity()
    {
        const string source = "A🙂אבe\u0301Z";
        var projection = new CssBidiProjection(source, [new(1, 6, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft),
            new(3, 2, CssUnicodeBidi.BidiOverride, FlowDirection.LeftToRight)]);
        for (int i = 0; i <= source.Length; i++)
        {
            Assert.Equal(i, projection.SourcePosition(projection.NativePosition(i)));
            Assert.Equal(i, projection.SourcePosition(projection.NativePosition(i, true)));
        }
        Assert.True(projection.NativePosition(3, true) < projection.NativePosition(3));
        Assert.True(projection.NativePosition(5, true) < projection.NativePosition(5));
        var range = projection.NativeRange(3, 2);
        Assert.Equal("אב", projection.Text.Substring(range.Start, range.Length));
        Assert.Equal(0, projection.NativeRange(3, 0).Length);
    }

    [Fact]
    public void EmptyScopesAreHarmlessAndMalformedRangesCannotSplitScalarsOrCross()
    {
        var empty = new CssBidiProjection("", [new(0, 0, CssUnicodeBidi.BidiOverride, FlowDirection.RightToLeft)]);
        Assert.Empty(empty.Source); Assert.Equal(0, empty.SourcePosition(empty.NativePosition(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CssBidiProjection("abc", [new(2, 2, CssUnicodeBidi.Embed, FlowDirection.LeftToRight)]));
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("abcde", [new(0, 3, CssUnicodeBidi.Embed, FlowDirection.LeftToRight),new(2, 3, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft)]));
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("A🙂B", [new(2, 1, CssUnicodeBidi.Embed, FlowDirection.LeftToRight)]));
    }

    [Fact]
    public void RecordedFormatAndPoolPreserveNaturalDirectionAsAnIndependentValue()
    {
        var text = new FormattedText("אב CSS", "DejaVu Sans", 20) { NaturalBidiDirection = true };
        var snapshot = text.CreateRenderSnapshot(null); text.NaturalBidiDirection = false;
        Assert.True(snapshot.NaturalBidiDirection);
        Assert.NotSame(DrawingObjectPool.CanonicalizeFormattedText(text), DrawingObjectPool.CanonicalizeFormattedText(snapshot));
    }

    [Fact]
    public void SoftBreaksKeepScopesAndPlaintextInsideTheSameParagraph()
    {
        const string source = "אב CSS DEF";
        var projection = new CssBidiProjection(source,
            [new(0, source.Length, CssUnicodeBidi.Plaintext, FlowDirection.LeftToRight, false),
             new(3, 7, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft)], [3, 7]);
        Assert.True(projection.NaturalParagraphDirection);
        Assert.Equal("אב \u2028\u2067CSS \u2028DEF\u2069", projection.Text);
        Assert.Equal(3, projection.SourcePosition(projection.NativePosition(3)));
        Assert.Equal(3, projection.SourcePosition(projection.NativePosition(3, true)));
        Assert.Equal("אב ", projection.Text[..projection.NativePosition(3, true)]);
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("A🙂B", [], [2]));
        Assert.Throws<ArgumentException>(() => new CssBidiProjection("ABC", [], [0]));
    }

    [Fact]
    public void FontSpansPartitionAllProjectedControlsAtSourceStyleBoundaries()
    {
        const string source = "A🙂 אב\r\nCD";
        var projection = new CssBidiProjection(source,
            [new(0, source.Length, CssUnicodeBidi.BidiOverride, FlowDirection.RightToLeft, false),
             new(3, 3, CssUnicodeBidi.Isolate, FlowDirection.LeftToRight)], [3]);
        var ranges = new[] { projection.NativeStyleRange(0, 3), projection.NativeStyleRange(3, 5), projection.NativeStyleRange(8, 2) };
        Assert.Equal(0, ranges[0].Start);
        Assert.Equal(projection.Text.Length, ranges.Sum(range => range.Length));
        for (int i = 1; i < ranges.Length; i++) Assert.Equal(ranges[i - 1].Start + ranges[i - 1].Length, ranges[i].Start);
        Assert.Equal(projection.Text, string.Concat(ranges.Select(range => projection.Text.Substring(range.Start, range.Length))));
        Assert.Equal("A🙂", projection.Source[..3]);
        Assert.Equal(0, projection.NativeStyleRange(3, 0).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => projection.NativeStyleRange(2, 1));
    }

    private static void Attach(FrameworkElement owner, string style) { Css.SetStyle(owner, style); Layout(owner); }

    [Theory]
    [InlineData("123 אב", 1)] [InlineData("١ A", 0)]
    [InlineData("\u061BA", 1)] [InlineData("\u07C0A", 1)]
    [InlineData("\U00010D40A", 0)] [InlineData("\U0001E900A", 1)]
    [InlineData("\u200FA", 1)] [InlineData("\u061CA", 1)]
    [InlineData("\u0301A", 0)] [InlineData("\uD800A", 0)]
    [InlineData("\u2068אב\u2069A", 0)]
    [InlineData("\u2066A\u2067B\u2069C\u2069אב", 1)]
    [InlineData("\u2069אב", 1)] [InlineData("\u202EAB\u202Cאב", 0)]
    public void PlaintextFirstStrongUsesUnicodeClassesAndSkipsIsolates(string source, int direction)
        => Assert.Equal((FlowDirection)direction, CssBidiDirection.FirstStrong(source));

    [Theory]
    [InlineData("")] [InlineData("123 !")] [InlineData("\u2068אב")]
    [InlineData("\u2067A\u2069")] [InlineData("\u0301\uD800")]
    public void PlaintextNeutralOrIsolatedContentHasNoEnclosingStrongDirection(string source)
        => Assert.Null(CssBidiDirection.FirstStrong(source));

    [Theory]
    [InlineData("\n")] [InlineData("\r\n")] [InlineData("\r")]
    [InlineData("\u0085")] [InlineData("\u2029")]
    [InlineData("\u001C")] [InlineData("\u001D")] [InlineData("\u001E")]
    public void PlaintextLineDirectionCarriesAcrossNeutralAndEmptyParagraphs(string separator)
    {
        string source = "123" + separator + "אב" + separator + "!" + separator + "A" + separator;
        var projection = new CssBidiProjection(source,
            [new(0, source.Length, CssUnicodeBidi.Plaintext, FlowDirection.LeftToRight, false)]);
        int position = 0;
        foreach (var (text, expected) in new[] { ("123", FlowDirection.LeftToRight), ("אב", FlowDirection.RightToLeft),
            ("!", FlowDirection.RightToLeft), ("A", FlowDirection.LeftToRight), ("", FlowDirection.LeftToRight) })
        {
            Assert.Equal(expected, projection.LineDirection(position, FlowDirection.LeftToRight));
            position += text.Length + separator.Length;
        }
        Assert.Equal(FlowDirection.RightToLeft, projection.LineDirection(0, FlowDirection.RightToLeft));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void PlaintextLineDirectionUsesProjectedScopesAndKeepsSoftRowsTogether(bool rtl)
    {
        const string source = "אב A\u2028!\nאב\n";
        var fallback = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var projection = new CssBidiProjection(source,
            [new(0, source.Length, CssUnicodeBidi.Plaintext, fallback, false),
             new(0, 2, CssUnicodeBidi.Isolate, FlowDirection.RightToLeft)], [3]);
        Assert.Equal(FlowDirection.LeftToRight, projection.LineDirection(0, fallback));
        Assert.Equal(FlowDirection.LeftToRight, projection.LineDirection(5, fallback));
        Assert.Equal(FlowDirection.RightToLeft, projection.LineDirection(7, fallback));
        Assert.Equal(FlowDirection.RightToLeft, projection.LineDirection(source.Length, fallback));
        var ordinary = new CssBidiProjection(source, []);
        Assert.Equal(fallback, ordinary.LineDirection(0, fallback));
    }
    [Theory]
    [InlineData("\u2028", false)] [InlineData("\u2029", true)] [InlineData("\u0085", true)]
    public void SourceSeparatorsKeepLineAndParagraphControlBoundariesDistinct(string separator, bool paragraph)
    {
        string source = "אב" + separator + "AB";
        var projection = new CssBidiProjection(source,
            [new(0, source.Length, CssUnicodeBidi.BidiOverride, FlowDirection.RightToLeft, false)]);
        string expected = paragraph ? "\u202Eאב\u202C" + separator + "\u202EAB\u202C" : "\u202E" + source + "\u202C";
        Assert.Equal(expected, projection.Text);
        for (int i = 0; i < source.Length; i++)
        {
            var range = projection.NativeRange(i, 1);
            Assert.Equal(source.Substring(i, 1), projection.Text.Substring(range.Start, range.Length));
            Assert.Equal(i, projection.SourcePosition(range.Start));
        }
        Assert.Equal(source.Length, projection.SourcePosition(projection.Text.Length));
    }

    private static void Layout(FrameworkElement owner)
    {
        for (int i = 0; i < 3; i++) { Dispatcher.GetForCurrentThread().ProcessQueue(); owner.Measure(new(400, 160)); owner.Arrange(new(0, 0, 400, 160)); owner.UpdateLayout(); }
    }
}
