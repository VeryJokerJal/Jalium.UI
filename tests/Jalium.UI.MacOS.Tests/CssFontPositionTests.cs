using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontPositionTests : IDisposable
{
    private readonly RenderContext _context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    public void Dispose() => _context.Dispose();

    [Theory]
    [InlineData("normal", 0)] [InlineData("sub", 1)] [InlineData("super", 2)]
    [InlineData("SuPeR", 2)] [InlineData("s\\75 per/**/", 2)]
    public void KeywordsUseTheRuntimeAndAuthoringGrammar(string value, int expected)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-variant-position", value));
        var text = Box(); Style(text, "font-variant-position:" + value);
        Assert.Equal((CssPositionVariant)expected, Position(text));
        Assert.Equal(CssPropertySupport.Supported, CssAuthoring.GetProperty("font-variant-position").Support);
        Assert.Contains("super", CssAuthoring.GetValues("font-variant-position"));
    }

    [Theory]
    [InlineData("")] [InlineData("none")] [InlineData("sub super")] [InlineData("super super")]
    [InlineData("normal sub")] [InlineData("1")] [InlineData("'super'")] [InlineData("super()")] [InlineData("super,sub")]
    public void InvalidDeclarationsPreserveThePreviousValue(string invalid)
    {
        Assert.False(CssAuthoring.IsValueSupported("font-variant-position", invalid));
        var text = Box(); Style(text, "font-variant-position:sub;font-variant-position:" + invalid);
        Assert.Equal(CssPositionVariant.Sub, Position(text));
    }

    [Theory]
    [InlineData("Seravek", "123", "super", true, 1)] [InlineData("Seravek", "123", "sub", true, 1)]
    [InlineData("Seravek", "123a\u0301", "super", true, 2)]
    [InlineData("Arial", "123", "super", true, 2)] [InlineData("Arial", "123", "sub", true, 2)]
    [InlineData("Arial", "123", "super", false, 3)] [InlineData("Seravek", "123", "super", false, 1)]
    public void ResolvedRunUsesNativeFormsOrSynthesizesEverySourceCharacter(string family, string source, string variant, bool synthesis, byte mode)
    {
        var text = Box(source, family); Style(text, $"font-variant-position:{variant};font-synthesis-position:{(synthesis ? "auto" : "none")}");
        string key = text.FontFamily.GetRenderingSource(text);
        using var paragraph = NativeTextParagraph.TryCreate([new(source, key, 32, 400, 0, Colors.Black)], family, 32, 10000, 44,
            TextAlignment.Left, FlowDirection.LeftToRight);
        Assert.NotNull(paragraph); Assert.Equal(Enumerable.Repeat(mode, source.Length), paragraph.PositionModes());
        using var reference = _context.CreateTextFormat(family, mode == 2 ? 20.8f : 32, 400, 0);
        Assert.Equal(JaliumResult.Ok, reference.SetFontFeatures([new(FontFeature.TagOf(variant == "sub" ? "subs" : "sups"), mode == 1 ? 1u : 0u)]));
        double expected = reference.MeasureText(source,10000,1000).Width;
        Assert.Equal(expected, Assert.Single(paragraph.Lines).Metrics.Line.Width, 3);
        Layout(text); var sourceCell=Assert.Single(text.GetSourceRangeSegments(0,source.Length));
        Assert.Equal(expected, sourceCell.EndX-sourceCell.StartX, 3);
        text.SelectAll(); Assert.Equal(source, text.SelectedText); Assert.Equal(source, text.Text); Assert.Equal(family, text.FontFamily.Source);
        Assert.Equal(0u, paragraph.Caret(0,0,false).TextPosition);
        Assert.Equal((uint)source.Length, paragraph.Caret(0,source.Length,false).TextPosition);
    }

    [Theory]
    [InlineData("inherit", 2)] [InlineData("unset", 2)] [InlineData("initial", 0)]
    [InlineData("font:32px Seravek", 0)] [InlineData("all:initial", 0)] [InlineData("all:unset", 2)]
    public void FontAndGlobalResetsFollowTheComputedParent(string value, int expected)
    {
        var child = Box(); var parent = new Border { Child = child };
        Style(parent, "font-variant-position:super;font-synthesis-position:none");
        Style(child, value.Contains(':') ? value : "font-variant-position:" + value); Evaluate(parent);
        Assert.Equal((CssPositionVariant)expected, Position(child));
        if (value.StartsWith("font:")) Assert.False((bool)child.GetValue(CssFontPosition.SynthesisProperty)!);
        if (value == "all:initial") Assert.True((bool)child.GetValue(CssFontPosition.SynthesisProperty)!);
    }

    [Fact]
    public void VariablesLayersAndInvalidComputedValuesRestoreTheRealInheritedValue()
    {
        var child = Box(); var parent = new Border { Child = child };
        Style(parent,"--position:sub;font-variant-position:super");
        Style(child,"font-variant-position:var(--position)!important;font-variant-position:normal"); Evaluate(parent);
        Assert.Equal(CssPositionVariant.Sub,Position(child));
        Style(child,"font-variant-position:var(--missing)"); Assert.Equal(CssPositionVariant.Super,Position(child));
        Style(child,""); Css.SetStyleSheet(parent,"@layer first,last;@layer first{TextBlock{font-variant-position:sub}}@layer last{TextBlock{font-variant-position:revert-layer}}");
        Evaluate(parent); Assert.Equal(CssPositionVariant.Sub,Position(child));
        Css.SetStyleSheets(parent,null); Style(child,""); Evaluate(parent); Assert.Equal(CssPositionVariant.Super,Position(child));
    }

    [Theory] [InlineData(24)] [InlineData(40)] [InlineData(80)] [InlineData(200)]
    public void SoftWrapDoesNotSwitchPartOfASyntheticRunBackToNative(int width)
    {
        const string source = "123123a\u0301";
        var actual=Box(source); var expected=Box(source); expected.FontSize=20.8;
        Style(actual,"font-variant-position:super;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;line-height:44px");
        Style(expected,"white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;line-height:44px;font-feature-settings:'sups' off");
        Layout(actual,width);Layout(expected,width);
        var first=actual.GetSourceRangeSegments(0,source.Length);var second=expected.GetSourceRangeSegments(0,source.Length);
        Assert.Equal(first.Count,second.Count);
        for(int i=0;i<first.Count;++i) Assert.Equal(second[i].EndX-second[i].StartX,first[i].EndX-first[i].StartX,3);
        actual.SelectAll(); Assert.Equal(source,actual.SelectedText);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SharedSourcesUseOneResolutionAcrossPhysicalShapingBoundaries(bool physical)
    {
        var root=new StackPanel();var prefix=Box("123");var suffix=Box("a\u0301");
        root.Children.Add(prefix);root.Children.Add(suffix);Css.SetStyleSheet(root,"TextBlock{display:inline}");
        Style(root,"display:block;font-family:Seravek;font-size:32px;line-height:44px;font-variant-position:super;white-space:pre-wrap;text-autospace:no-autospace");
        if(physical)Style(suffix,"padding-left:1px");
        using var reference=_context.CreateTextFormat("Seravek",20.8f,400,0);
        double expected=reference.MeasureText("123",10000,1000).Width;
        foreach(int width in new[]{40,200,40}) {
            Layout(root,width);var cell=Assert.Single(prefix.GetSourceRangeSegments(0,3));
            Assert.Equal(expected,cell.EndX-cell.StartX,3);
            prefix.SelectAll();Assert.Equal("123",prefix.SelectedText);Assert.Same(root,prefix.Parent);
        }
        suffix.Text="456";Layout(root,200);
        using var native=_context.CreateTextFormat("Seravek",32,400,0);native.SetFontFeatures([new(FontFeature.TagOf("sups"),1)]);
        var restored=Assert.Single(prefix.GetSourceRangeSegments(0,3));Assert.Equal(native.MeasureText("123",10000,1000).Width,restored.EndX-restored.StartX,3);
    }

    [Fact]
    public void ExplicitFeatureOffOverridesNativeSelectionAndNormalRestoresTheOriginalWidth()
    {
        var text=Box("123");Layout(text);double normal=text.DesiredSize.Width;
        Style(text,"font-variant-position:super");Layout(text);Assert.True(text.DesiredSize.Width<normal-1);
        Style(text,"font-variant-position:super;font-feature-settings:'sups' off");Layout(text);Assert.Equal(normal,text.DesiredSize.Width,3);
        Style(text,"font-variant-position:normal");Layout(text);Assert.Equal(normal,text.DesiredSize.Width,3);
    }

    [Fact]
    public void CompiledBuilderAndStyleQueriesUseTheSamePositionStorage()
    {
        var root=new Border{Child=new TextBlock()};var text=(TextBlock)root.Child!;
        Css.SetClass(text,"target");Style(root,"font-variant-position:super");
        Css.SetStyleSheet(root,"@container style(font-variant-position:super){.target{opacity:.25}}");Evaluate(root);Assert.Equal(.25,text.Opacity);
        Css.SetStyleSheets(root,null);var builder=new CssStyleSheetBuilder(1);
        var compound=builder.Compound("TextBlock",false,null,false,true,false,null,null,null,0,null,null);
        builder.Rule([builder.Selector([compound],[],1,0,0,false,false)],[builder.Declaration("font-variant-position","sub",false)],-1,null,-1);
        Css.GetStyleSheets(text).Add(builder.Build());Evaluate(root);Assert.Equal(CssPositionVariant.Sub,Position(text));
    }

    [Fact]
    public void DiscreteTransitionsAndKeyframesSampleTheCompletePosition()
    {
        var text=Box("123");var dp=CssFontPosition.VariantProperty;
        Style(text,"font-variant-position:normal");var from=text.GetValue(dp)!;
        Style(text,"font-variant-position:super;transition:font-variant-position 1s linear allow-discrete");var to=text.GetValue(dp)!;
        Assert.Null(CssTransitions.Create(text,dp,from,to,new(TimeSpan.FromSeconds(1),TimeSpan.Zero,new("linear"),false)));
        var data=Assert.IsType<CssTransitionData>(text.GetValue(CssTransitions.DataProperty)).Find(text,dp)!;
        var animation=Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(text,dp,from,to,data));var clock=animation.CreateClock();
        foreach(double progress in new[]{.1,.49,.5,.9,.1}){clock.SetProgressForSampling(progress);Assert.Equal(progress<.5?from:to,animation.GetCurrentValue(from,to,clock));}
        Css.SetStyleSheet(text,"@keyframes script{from{font-variant-position:sub}to{font-variant-position:super}}");Evaluate(text);
        var node=CssNode.Get(text);var tracks=CssKeyframes.BuildPropertyTracks(node,CssKeyframes.Find(node,"script")!);
        var sampler=new CssPropertySampler(dp,tracks[dp]);
        Assert.Equal(CssPositionVariant.Sub,sampler.Sample(.49,new("linear")));
        Assert.Equal(CssPositionVariant.Super,sampler.Sample(.5,new("linear")));
    }

    [Fact]
    public void SharedDocumentsAndMarkersInheritPositionWithoutReplacingSourceNodes()
    {
        var run=new Run("123");var span=new Span(run);var doc=new FlowDocument(new Paragraph(span)){FontFamily="Seravek",FontSize=32,PagePadding=new(0)};
        using var first=new FlowDocumentPresentation();using var second=new FlowDocumentPresentation();first.SetDocument(doc);second.SetDocument(doc);
        foreach(string mode in new[]{"super","sub","normal"}) {
            Style(doc,"font-variant-position:"+mode);Layout(first);Layout(second);
            Assert.Equal(Position(doc),Position(run));Assert.Same(span,run.Parent);Assert.Equal("123\n",doc.GetText());
        }
        var item=Box("正文");Style(item,"display:list-item;list-style-type:decimal;font-variant-position:super");Evaluate(item);
        Assert.Equal(CssPositionVariant.Super,Position(Assert.Single(CssGeneratedContent.Markers(CssNode.Get(item)))));
    }

    [Fact]
    public void PositionIdentitySurvivesCapsFontWidthsMaterializationAndFeatureMerging()
    {
        foreach(byte run in new byte[]{0,1,2,3})foreach(byte position in new byte[]{1,2})foreach(bool deny in new[]{false,true}) {
            var context=new FontFeatureRenderingSource.CapsContext("lt",true,12,1,position,deny,run);
            string key=FontFeatureRenderingSource.Wrap(CssFontFaces.BlockPrefix+"Arial",[],CssCapsVariant.SmallCaps,context);
            key=CssFontFaces.MaterializeSource(key,"123");key=FontWidthRenderingSource.ForFormattedText(key,4);
            key=FontFeatureRenderingSource.Wrap(key,[new(FontFeature.TagOf("kern"),0)]);
            Assert.True(FontFeatureRenderingSource.TryUnwrap(key,out var features,out _,out var caps,out var decoded));
            Assert.Equal(context,decoded);Assert.Equal(CssCapsVariant.SmallCaps,caps);Assert.Single(features);Assert.True(CssFontFaces.IsBlocked(key));
        }
    }

    private static CssPositionVariant Position(DependencyObject owner)=>(CssPositionVariant)owner.GetValue(CssFontPosition.VariantProperty)!;
    private static TextBlock Box(string text="123",string family="Seravek")=>new(){Text=text,FontFamily=family,FontSize=32};
    private static void Style(DependencyObject owner,string css){Css.SetStyle(owner,css);CssEngine.EvaluateElement(CssNode.Get(owner));}
    private static void Evaluate(DependencyObject owner){CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);}
    private static void Layout(FrameworkElement view,int width=200){for(int i=0;i<3;++i){view.Measure(new(width,400));view.Arrange(new(0,0,width,400));view.UpdateLayout();}}
}
