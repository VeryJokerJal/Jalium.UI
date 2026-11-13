using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed partial class CssRubyMergeLayoutTests
{
    [Theory]
    [InlineData("merge", 1)]
    [InlineData("auto", 2)]
    [InlineData("separate", 0)]
    [InlineData("collapse", 0)]
    public void StandardMergeKeywordsReachTheRuntimeAndCollapseIsRejected(string keyword, int expected)
    {
        var root = new StackPanel(); Css.SetStyle(root, $"display:ruby;ruby-merge:{keyword};"); Flush();
        Assert.Equal(expected, (int)CssWritingModeProperties.GetRubyMerge(root));
    }

    public static IEnumerable<object[]> LayoutCases()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr" })
        foreach (var direction in new[] { "ltr", "rtl" })
        foreach (var merge in new[] { "merge", "auto" }) yield return [mode, direction, merge];
    }

    [Theory, MemberData(nameof(LayoutCases))]
    public void LongAndShortAnnotationsShareTheirMeasuredSpaceAndKeepOriginalSources(string mode, string direction, string merge)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a = Create(mode, direction, merge, "とうとう", "と"); Layout(a.Root, mode, 240);
        var first = InlineBounds(a.Root, a.Bases[0], mode); var last = InlineBounds(a.Root, a.Bases[1], mode);
        var one = InlineBounds(a.Root, a.Notes[0], mode); var two = InlineBounds(a.Root, a.Notes[1], mode);
        Assert.Equal(40, Math.Abs(first.Start - last.Start), 3);
        Assert.Equal(80, mode == "horizontal-tb" ? a.Root.DesiredSize.Width : a.Root.DesiredSize.Height, 3);
        Assert.Equal(direction == "rtl" ? two.End : one.End, direction == "rtl" ? one.Start : two.Start, 3);
        Assert.Equal(64, one.End - one.Start, 3); Assert.Equal(16, two.End - two.Start, 3);
        a.Notes[0].Select(1, 2); Assert.Equal("うと", a.Notes[0].SelectedText);
        Assert.All(a.Bases.Concat(a.Notes), text => Assert.Same(a.Root, text.VisualParent)); Assert.Equal(4, a.Root.Children.Count);
    }

    [Theory, MemberData(nameof(LayoutCases))]
    public void MergedPairsFitTogetherAndReassociateIndependentlyAfterWrapping(string mode, string direction, string merge)
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a = Create(mode, direction, merge, "とうと", "と"); Layout(a.Root, mode, 70);
        Assert.Equal(Cross(a.Root, a.Bases[0], mode), Cross(a.Root, a.Bases[1], mode), 3);
        Assert.Equal(64, mode == "horizontal-tb" ? a.Root.DesiredSize.Width : a.Root.DesiredSize.Height, 3);
        Layout(a.Root, mode, 40);
        Assert.True(Math.Abs(Cross(a.Root, a.Bases[0], mode) - Cross(a.Root, a.Bases[1], mode)) > 30);
        for (int i = 0; i < 2; i++)
        {
            var b = InlineBounds(a.Root, a.Bases[i], mode); var n = InlineBounds(a.Root, a.Notes[i], mode);
            Assert.Equal((b.Start + b.End) / 2, (n.Start + n.End) / 2, 3);
        }
        Layout(a.Root, mode, 70); Assert.Equal(Cross(a.Root, a.Bases[0], mode), Cross(a.Root, a.Bases[1], mode), 3);
    }

    [Fact]
    public void IdenticalRawAnnotationIsHiddenAndReappearsWithoutChangingAuthoredVisibility()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a = Create("horizontal-tb", "ltr", "separate", "東", "と"); Layout(a.Root, "horizontal-tb", 240);
        Assert.False(a.Notes[0].IsVisible); Assert.Empty(a.Notes[0].DocumentTextBounds(0, 1)); Assert.Equal(Visibility.Visible, a.Notes[0].Visibility);
        a.Notes[0].Text = "とう"; Layout(a.Root, "horizontal-tb", 240); Assert.True(a.Notes[0].IsVisible);
        a.Notes[0].Text = "東"; Layout(a.Root, "horizontal-tb", 240); Assert.False(a.Notes[0].IsVisible);
        Css.SetStyle(a.Root, Style("horizontal-tb", "ltr", "merge")); Layout(a.Root, "horizontal-tb", 240);
        Assert.True(a.Notes[0].IsVisible); Assert.NotEmpty(a.Notes[0].DocumentTextBounds(0, 1));
        Css.SetStyle(a.Root, Style("horizontal-tb", "ltr", "separate")); Layout(a.Root, "horizontal-tb", 240); Assert.False(a.Notes[0].IsVisible);
    }

    [Fact]
    public void AutoDoesNotMergeWhenEveryAnnotationFitsItsOwnBase()
    {
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a = Create("horizontal-tb", "ltr", "auto", "とう", "と"); var b = Create("horizontal-tb", "ltr", "separate", "とう", "と");
        Layout(a.Root, "horizontal-tb", 240); Layout(b.Root, "horizontal-tb", 240);
        for (int i = 0; i < 2; i++) Assert.Equal(Bounds(b.Root,b.Notes[i]),Bounds(a.Root,a.Notes[i]));
    }

    [Fact]
    public void InvalidLegacySyntaxDoesNotReplaceAValidStandardDeclaration()
    {
        var root=new StackPanel();Css.SetStyle(root,"display:ruby;ruby-merge:MERGE;ruby-merge:collapse;");Flush();
        Assert.Equal(CssRubyMerge.Merge,CssWritingModeProperties.GetRubyMerge(root));
        Assert.Contains("merge",CssAuthoring.GetValues("ruby-merge"));Assert.DoesNotContain("collapse",CssAuthoring.GetValues("ruby-merge"));
        Assert.Equal((int)CssRubyMerge.Collapse,(int)CssRubyMerge.Merge);
    }

    [Fact]
    public void AuthoringInitialValuesAgreeWithTheActualRubyDefaults()
    {
        Assert.Equal("alternate",CssAuthoring.GetProperty("ruby-position").InitialValue);
        Assert.Equal("separate",CssAuthoring.GetProperty("ruby-merge").InitialValue);
        var root=new StackPanel();Assert.Equal(CssRubyPosition.Alternate,CssWritingModeProperties.GetRubyPosition(root));
        Assert.Equal(CssRubyMerge.Separate,CssWritingModeProperties.GetRubyMerge(root));
    }

    [Theory, MemberData(nameof(LayoutCases))]
    public void ATextPrefixSharesTheActualMergedAdvanceAndDoesNotForceAnEarlyBreak(string mode,string direction,string merge)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var host=new StackPanel();var prefix=Text("先",32);Css.SetStyle(prefix,"display:inline;");host.Children.Add(prefix);
        var a=Create(mode,direction,merge,"とうと","と");host.Children.Add(a.Root);
        Css.SetStyle(host,$"display:block;writing-mode:{mode};direction:{direction};font-size:32px;line-height:1.25;white-space:pre-wrap;");Layout(host,mode,100);
        // The ordinary prefix exposes its CSS line box; a Ruby base exposes
        // its font content area. Equal-font centers retain the same baseline.
        Assert.Equal(CrossCenter(host,prefix,mode),CrossCenter(host,a.Bases[0],mode),3);Assert.Equal(Cross(host,a.Bases[0],mode),Cross(host,a.Bases[1],mode),3);
        Layout(host,mode,86);Assert.Equal(CrossCenter(host,prefix,mode),CrossCenter(host,a.Bases[0],mode),3);
        Assert.True(Math.Abs(Cross(host,a.Bases[0],mode)-Cross(host,a.Bases[1],mode))>30);
        for(int i=0;i<2;i++){var b=InlineBounds(host,a.Bases[i],mode);var n=InlineBounds(host,a.Notes[i],mode);Assert.Equal((b.Start+b.End)/2,(n.Start+n.End)/2,3);}
    }

    [Theory]
    [InlineData("a  b","a b","white-space:normal;")]
    [InlineData("ß","ss","text-transform:uppercase;")]
    [InlineData("e\u0301","é","")]
    public void AutoHidingComparesExactRawTextBeforeWhiteSpaceTransformationAndNormalization(string basis,string annotation,string extra)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a=Create("horizontal-tb","ltr","separate",annotation,"と");a.Bases[0].Text=basis;
        Css.SetStyle(a.Root,Style("horizontal-tb","ltr","separate")+extra);Layout(a.Root,"horizontal-tb",240);
        Assert.True(a.Notes[0].IsVisible);Assert.NotEmpty(a.Notes[0].DocumentTextBounds(0,annotation.Length));
        a.Notes[0].Text=basis;Layout(a.Root,"horizontal-tb",240);Assert.False(a.Notes[0].IsVisible);
    }

    [Fact]
    public void RawTextComparisonIgnoresElementBoundariesAndRetiredOwnersCannotKeepAReparentedSourceHidden()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var basis=new StackPanel();var note=new TextBlock {FontSize=16};root.Children.Add(basis);root.Children.Add(note);
        basis.Children.Add(Text("東",32));basis.Children.Add(Text("京",32));note.Inlines.Add(new Run("東"));var span=new Span();span.Inlines.Add(new Run("京"));note.Inlines.Add(span);
        Css.SetStyle(root,Style("horizontal-tb","ltr","auto"));Css.SetStyle(basis,"display:ruby-base;");Css.SetStyle(note,"display:ruby-text;");Layout(root,"horizontal-tb",240);
        Assert.False(note.IsVisible);Assert.Empty(note.DocumentTextBounds(0,2));
        var ordinary=new StackPanel();root.Children.Remove(note);ordinary.Children.Add(note);Css.SetStyle(note,"display:block;");Layout(ordinary,"horizontal-tb",240);
        Assert.True(note.IsVisible);Assert.NotEmpty(note.DocumentTextBounds(0,2));Assert.Same(ordinary,note.VisualParent);
    }

    [Theory]
    [InlineData("merge",true)]
    [InlineData("auto",false)]
    [InlineData("separate",false)]
    public void MergeDisablesAnnotationCollapseWithoutOverwritingItsComputedStyle(string merge,bool visible)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a=Create("horizontal-tb","ltr",merge,"とうとう","と");Css.SetStyle(a.Notes[0],"display:ruby-text;font-size:16px;visibility:collapse;");Layout(a.Root,"horizontal-tb",240);
        Assert.Equal(visible,a.Notes[0].IsVisible);Assert.Equal(CssVisibilityMode.Collapse,CssDisplayProperties.ComputedVisibility(CssNode.Get(a.Notes[0])));
        Assert.Equal(Visibility.Visible,a.Notes[0].Visibility);
        if(visible)Assert.NotEmpty(a.Notes[0].DocumentTextBounds(0,4));else Assert.Empty(a.Notes[0].DocumentTextBounds(0,4));
    }

    [Fact]
    public void AutoHiddenAnnotationsSeparateTheRunsThatMayShareSpace()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var bases=new[]{Text("東",32),Text("あ",32),Text("京",32)};var notes=new[]{Text("とうと",16),Text("あ",16),Text("と",16)};
        foreach(var text in bases){root.Children.Add(text);Css.SetStyle(text,"display:ruby-base;font-size:32px;");}
        foreach(var text in notes){root.Children.Add(text);Css.SetStyle(text,"display:ruby-text;font-size:16px;");}
        Css.SetStyle(root,Style("horizontal-tb","ltr","auto"));Layout(root,"horizontal-tb",240);
        Assert.Equal(112,root.DesiredSize.Width,3);Assert.False(notes[1].IsVisible);Assert.Equal(0,notes[1].DocumentTextBounds(0,1).Count);
        Assert.Equal(8,InlineBounds(root,bases[0],"horizontal-tb").Start,3);Assert.Equal(48,InlineBounds(root,bases[1],"horizontal-tb").Start,3);
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("pretty")]
    [InlineData("stable")]
    public void WrapStyleProbesKeepCommittedRubyGeometryStable(string wrap)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a=Create("horizontal-tb","rtl","merge","とうと","と");Css.SetStyle(a.Root,Style("horizontal-tb","rtl","merge")+$"text-wrap-style:{wrap};");
        Layout(a.Root,"horizontal-tb",70);var initial=a.Bases.Concat(a.Notes).Select(t=>Bounds(a.Root,t)).ToArray();
        for(int i=0;i<4;i++){Layout(a.Root,"horizontal-tb",40);Layout(a.Root,"horizontal-tb",70);}
        Assert.Equal(initial,a.Bases.Concat(a.Notes).Select(t=>Bounds(a.Root,t)).ToArray());Assert.Equal(4,a.Root.Children.Count);
    }

    [Theory]
    [InlineData("horizontal-tb")]
    [InlineData("vertical-rl")]
    [InlineData("vertical-lr")]
    public void PerContainerMergeAndAlignmentOverrideIndividualAnnotationValues(string mode)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var root=new StackPanel();var a=Text("東",32);var b=Text("京",32);root.Children.Add(a);root.Children.Add(b);Css.SetStyle(a,"display:ruby-base;");Css.SetStyle(b,"display:ruby-base;");
        var rtc=new StackPanel();root.Children.Add(rtc);var first=Text("とう",16);var last=Text("と",16);rtc.Children.Add(first);rtc.Children.Add(last);
        Css.SetStyle(root,Style(mode,"ltr","separate"));Css.SetStyle(rtc,"display:ruby-text-container;ruby-merge:merge;ruby-align:center;");
        Css.SetStyle(first,"display:ruby-text;ruby-merge:separate;ruby-align:start;");Css.SetStyle(last,"display:ruby-text;ruby-align:start;");Layout(root,mode,240);
        Assert.Equal(8,InlineBounds(root,first,mode).Start,3);Assert.Equal(40,InlineBounds(root,last,mode).Start,3);
        Assert.Same(rtc,first.VisualParent);Assert.Same(root,a.VisualParent);
    }

    [Fact]
    public void StableHiddenAnnotationsDoNotToggleVisibilityDuringRepeatedMeasurements()
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a=Create("horizontal-tb","ltr","auto","東","と");Layout(a.Root,"horizontal-tb",240);int changes=0;
        a.Notes[0].IsVisibleChanged+=(_,_)=>changes++;
        for(int i=0;i<5;i++){a.Root.InvalidateMeasure();Layout(a.Root,"horizontal-tb",i%2==0?100:240);}
        Assert.Equal(0,changes);Assert.False(a.Notes[0].IsVisible);
    }

    [Theory]
    [InlineData("ltr","center")]
    [InlineData("rtl","center")]
    [InlineData("ltr","space-between")]
    [InlineData("rtl","space-between")]
    [InlineData("ltr","space-around")]
    [InlineData("rtl","space-around")]
    public void DefaultBopomofoSpacingAlsoAppliesAcrossMergedAnnotationBoundaries(string direction,string align)
    {
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var a=Create("horizontal-tb",direction,"merge","ㄓ","ㄨ");foreach(var n in a.Notes)n.FontFamily=new FontFamily("PingFang TC");
        Css.SetStyle(a.Root,Style("horizontal-tb",direction,"merge")+$"ruby-align:{align};");Layout(a.Root,"horizontal-tb",240);
        var first=InlineBounds(a.Root,a.Notes[0],"horizontal-tb");var last=InlineBounds(a.Root,a.Notes[1],"horizontal-tb");
        Assert.Equal(direction=="rtl"?last.End:first.End,direction=="rtl"?first.Start:last.Start,3);
        var original=a.Notes.Select(n=>Bounds(a.Root,n)).ToArray();
        Css.SetStyle(a.Root,Style("horizontal-tb",direction,"merge")+$"ruby-align:{align};text-justify:auto;");Layout(a.Root,"horizontal-tb",240);
        first=InlineBounds(a.Root,a.Notes[0],"horizontal-tb");last=InlineBounds(a.Root,a.Notes[1],"horizontal-tb");
        if(align!="center")Assert.True(Math.Max(first.End,last.End)-Math.Min(first.Start,last.Start)>32.1);
        Css.SetStyle(a.Root,Style("horizontal-tb",direction,"merge")+$"ruby-align:{align};");Layout(a.Root,"horizontal-tb",240);Assert.Equal(original,a.Notes.Select(n=>Bounds(a.Root,n)).ToArray());
    }

    private static (StackPanel Root, TextBlock[] Bases, TextBlock[] Notes) Create(string mode, string direction, string merge, string first, string last)
    {
        var root = new StackPanel(); var bases = new[] { Text("東", 32), Text("京", 32) }; var notes = new[] { Text(first, 16), Text(last, 16) };
        foreach (var b in bases) { root.Children.Add(b); Css.SetStyle(b, "display:ruby-base;font-size:32px;"); }
        foreach (var n in notes) { root.Children.Add(n); Css.SetStyle(n, "display:ruby-text;font-size:16px;"); }
        Css.SetStyle(root, Style(mode,direction,merge)); return (root,bases,notes);
    }
    private static string Style(string mode,string direction,string merge) => $"display:ruby;writing-mode:{mode};direction:{direction};ruby-position:over;ruby-align:center;ruby-merge:{merge};font-family:'Hiragino Sans';font-size:32px;line-height:1.25;white-space:pre-wrap;";
    private static TextBlock Text(string text,double size) => new() {Text=text,FontSize=size,FontFamily=new FontFamily("Hiragino Sans"),IsTextSelectionEnabled=true};
    private static Rect Bounds(FrameworkElement root,TextBlock text)
    {
        var r=Assert.Single(text.DocumentTextBounds(0,text.Text.Length));Assert.True(root.GetRenderMatrix().TryInvert(out var inverse));var m=Matrix.Multiply(text.GetRenderMatrix(),inverse);
        var a=m.Transform(r.TopLeft);var b=m.Transform(r.BottomRight);return new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y));
    }
    private static (double Start,double End) InlineBounds(FrameworkElement root,TextBlock text,string mode) {var b=Bounds(root,text);return mode=="horizontal-tb"?(b.Left,b.Right):(b.Top,b.Bottom);}
    private static double Cross(FrameworkElement root,TextBlock text,string mode) {var b=Bounds(root,text);return mode=="horizontal-tb"?b.Top:b.Left;}
    private static double CrossCenter(FrameworkElement root,TextBlock text,string mode) {var b=Bounds(root,text);return mode=="horizontal-tb"?(b.Top+b.Bottom)/2:(b.Left+b.Right)/2;}
    private static void Flush() {Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);}
    private static void Layout(FrameworkElement root,string mode,double inline)
    {var size=mode=="horizontal-tb"?new Size(inline,240):new Size(240,inline);for(int i=0;i<3;i++){Flush();root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();}}
}
