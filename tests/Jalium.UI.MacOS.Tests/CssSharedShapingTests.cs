using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSharedShapingTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("A", "V", 0d)] [InlineData("A", "V", 2d)] [InlineData("A", "V", -1d)]
    [InlineData("f", "fi", 0d)] [InlineData("f", "fi", 2d)] [InlineData("مر", "حبا", 3d)]
    [InlineData("אב", "AB", 3d)] [InlineData("abc אב", "ג def", 2d)]
    public void SharedSourcesUseOneNativeShape(string first, string second, double spacing)
    {
        using var f = new Fixture(first, second); f.Style(spacing); f.Layout();
        using var native = Reference(spacing, forced: false);
        var text = first + second;
        double expected = native.MeasureText(text, 100000, 1000).Width +
            (first == "مر" ? 0 : (GraphemeClusters.GetBoundaries(text).Length - 2) * spacing);
        Assert.Equal(expected, f.Width(), 3); f.AssertSources();
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void AForcedLigatureCrossesPlainAndNestedSourceBoundaries(bool nested)
    {
        using var f = new Fixture("f", "fi", nested); f.Style(2, extra: "font-feature-settings:'liga' 1;"); f.Layout();
        using var native = Reference(2, true);
        Assert.Equal(native.MeasureText("ffi", 100000, 1000).Width + 4, f.Width(), 3);
        f.AssertSources();
    }

    [Fact]
    public void EachSideContributesHalfItsTrackingAtTheSourceSeam()
    {
        using var f = new Fixture("A", "V"); f.Style(0); Css.SetStyle(f.A, "letter-spacing:2px"); Css.SetStyle(f.B, "letter-spacing:6px"); f.Layout();
        using var native = Reference(2, false);
        double natural = native.MeasureText("AV", 100000, 1000).Width;
        Assert.Equal(natural + 4, f.Width(), 3);
        Css.SetStyle(f.A, "letter-spacing:4px"); f.Layout(); Assert.Equal(natural + 5, f.Width(), 3);
        Css.SetStyle(f.A, "letter-spacing:2px"); f.Layout(); Assert.Equal(natural + 4, f.Width(), 3);
        f.AssertSources();
    }

    [Fact]
    public void PaintChangesKeepTheWholeShapeAndTheOriginalRun()
    {
        using var f = new Fixture("f", "fi"); f.B.Text = "";
        var run = new Run("fi"); f.B.Inlines.Add(run); var originalParent = run.Parent;
        f.Style(2, extra: "font-feature-settings:'liga' 1;"); f.Layout();
        using var native = Reference(2, true); double expected = native.MeasureText("ffi", 100000, 1000).Width + 4;
        Assert.Equal(expected, f.Width(), 3);
        Css.SetStyle(f.A, "color:red;text-decoration:underline;"); run.Foreground = Brushes.Blue; f.Layout();
        Assert.Equal(expected, f.Width(), 3); Assert.Same(run, f.B.Inlines[0]); Assert.Same(originalParent, run.Parent); f.AssertSources();
    }

    [Theory] [InlineData("padding-right:1px")] [InlineData("border-right:1px solid red")] [InlineData("margin-right:1px")]
    [InlineData("vertical-align:super")] [InlineData("vertical-align:5px")]
    public void AnAncestorFrameAlsoBreaksShapingAtItsPhysicalEdge(string barrier)
    {
        using var f = new Fixture("A", "V", nested: true); f.Style(0);
        Css.SetStyle(f.Root.Children[0], "display:inline;" + barrier); f.Layout(); using var native = Reference(0, false);
        Assert.Equal(native.MeasureText("A", 100000, 1000).Width, Assert.Single(f.A.GetSourceRangeSegments(0, 1)).EndX, 3);
        f.AssertSources();
    }

    [Theory] [InlineData("a", "\u0301b", 2d)] [InlineData("👩\u200D", "💻🙂", 3d)]
    public void AClusterSplitBetweenElementsKeepsItsNaturalWholeShape(string a, string b, double spacing)
    {
        using var f = new Fixture(a, b); f.Style(spacing); f.Layout(); using var native = Reference(spacing, false);
        string whole = a + b;
        Assert.Equal(native.MeasureText(whole, 100000, 1000).Width + (GraphemeClusters.GetBoundaries(whole).Length - 2) * spacing, f.Width(), 3);
        f.AssertSources();
    }

    [Fact]
    public void WrappingKeepsTheCrossSourcePairOnTheSameLineAndHitsItsSecondLetter()
    {
        using var f = new Fixture("A", "V AV"); f.Style(2, width: 40.6, extra: "white-space:normal;"); f.Layout(40.6);
        var a = Assert.Single(f.A.GetSourceRangeSegments(0, 1)); var v = Assert.Single(f.B.GetSourceRangeSegments(0, 1));
        Assert.Equal(a.Baseline, v.Baseline, 3); Assert.Equal(40.464, f.B.TransformToAncestor(f.Root).X + v.EndX, 3);
        var next = Assert.Single(f.B.GetSourceRangeSegments(2, 2)); Assert.True(next.Baseline > v.Baseline);
        var hit = Assert.IsType<TextPointer>(f.B.GetPositionFromPoint(new(v.StartX + .1, v.Baseline - 2), false));
        Assert.Equal(0, f.B.ContentStart.GetOffsetToPosition(hit)); f.AssertSources();
    }

    [Theory] [InlineData("padding-left:1px")] [InlineData("border-left:1px solid red")] [InlineData("margin-left:1px")]
    public void APhysicalInlineBoundaryBreaksShaping(string barrier)
    {
        using var f = new Fixture("A", "V"); f.Style(0); Css.SetStyle(f.B, barrier); f.Layout();
        using var native = Reference(0, false);
        Assert.Equal(native.MeasureText("A", 100000, 1000).Width, Assert.Single(f.A.GetSourceRangeSegments(0, 1)).EndX, 3);
        f.AssertSources();
    }

    [Theory]
    [InlineData("rtl-siblings")] [InlineData("rtl-padding")] [InlineData("rtl-frame")]
    [InlineData("rtl-generated")] [InlineData("combining-siblings")] [InlineData("combining-generated")] [InlineData("generated-images")]
    [InlineData("source-outset")] [InlineData("writing-tools-clear")] [InlineData("writing-tools-finish")] [InlineData("writing-tools-stale")]
    public void ExistingSourceContractsAlsoHoldWithALiveNativeContext(string path)
    {
        switch (path)
        {
            case "generated-images": new CssImageContentTests().MixedPseudoContentFragmentsAcrossLinesAndKeepsOriginalRunSelectionAndHits(); break;
            case "rtl-siblings": new CssSharedInlineTests().RightToLeftDirectionPlacesSiblingsFromTheRightEdge(); break;
            case "rtl-padding": new CssSharedInlineTests().RightToLeftLinePaddingIsAppliedAtThePhysicalLineEdges(); break;
            case "rtl-frame": new MacOSCssInlineBidiTests().RightToLeftCjkFramesKeepSourceAndPhysicalEdges(false); break;
            case "rtl-generated": new CssInlineFragmentTests().RightToLeftBaseDirectionPlacesInlineSourcesFromTheRightEdge(); break;
            case "combining-siblings": new CssSharedInlineTests().SourceBoundariesNeitherSplitWordsNorCombiningClusters(); break;
            case "combining-generated": new CssInlineFragmentTests().EmergencyWrapKeepsEmojiAndCrossSourceCombiningClustersTogether(); break;
            case "source-outset": new CssBorderImageInlineTests().AnonymousTextBodiesKeepHostCoordinatesAndForwardDocumentInk(true); break;
            default: new CssWritingToolsTests().CachedTextRestoresAfterEachPresentationLifetime(path[14..]); break;
        }
    }

    private NativeTextFormat Reference(double spacing, bool forced)
    {
        var format = _context.CreateTextFormat("Seravek", 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, format.SetFontFeatures([new(FontFeature.TagOf("kern"), 1),
            new(FontFeature.TagOf("liga"), forced || spacing == 0 ? 1u : 0u), new(FontFeature.TagOf("clig"), spacing == 0 ? 1u : 0u),
            new(FontFeature.TagOf("dlig"), 0), new(FontFeature.TagOf("hlig"), 0)])); return format;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly StackPanel Root = new(); internal readonly TextBlock A, B;
        private readonly string _first, _second; private readonly DependencyObject? _parentA, _parentB;
        internal Fixture(string a, string b, bool nested = false)
        {
            _first = a; _second = b; A = new() { Text = a, IsTextSelectionEnabled = true }; B = new() { Text = b, IsTextSelectionEnabled = true };
            if (nested) { var wrapper = new StackPanel(); wrapper.Children.Add(A); Root.Children.Add(wrapper); Css.SetStyle(wrapper, "display:inline"); }
            else Root.Children.Add(A);
            Root.Children.Add(B); _parentA = A.Parent; _parentB = B.Parent;
            Css.SetStyleSheet(Root, "TextBlock{display:inline}");
        }
        internal void Style(double spacing, double width = 320, string extra = "") => Css.SetStyle(Root,
            $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;white-space:nowrap;font-kerning:normal;letter-spacing:{spacing}px;text-autospace:no-autospace;" + extra);
        internal void Layout(double width = 320)
        {
            for (int i = 0; i < 3; i++) { CssEvaluationScheduler.FlushIfPending(Root.Dispatcher); Root.Measure(new(width, 500)); Root.Arrange(new(0, 0, width, 500)); Root.UpdateLayout(); }
        }
        internal double Width() => new[] { A, B }.SelectMany(text => text.GetSourceRangeSegments(0, text.Text.Length)
            .Select(range => text.TransformToAncestor(Root).X + range.EndX)).DefaultIfEmpty(0).Max();
        internal void AssertSources()
        {
            Assert.Equal(_first, A.Text); Assert.Equal(_second, B.Text); Assert.Same(_parentA, A.Parent); Assert.Same(_parentB, B.Parent);
            A.SelectAll(); B.SelectAll(); Assert.Equal(_first, A.SelectedText); Assert.Equal(_second, B.SelectedText);
        }
        public void Dispose() => Css.SetStyleSheets(Root, null);
    }
}
