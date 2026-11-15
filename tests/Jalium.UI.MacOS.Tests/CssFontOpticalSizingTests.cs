using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class CssFontOpticalSizingTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _font=CssNativeFontResource.Create(File.ReadAllBytes(CssFontVariationSettingsTests.Fixture))!;
    public void Dispose(){_font.Dispose();_context.Dispose();}
    private static DependencyProperty Policy=>CssFontOpticalSizing.AutoProperty;
    private TextBlock Box()=>new(){Text="ABMABM",FontFamily=_font.Family,FontSize=32,IsTextSelectionEnabled=true};
    private static void Style(DependencyObject owner,string css){Css.SetStyle(owner,css);Evaluate(owner);}
    private static void Evaluate(DependencyObject owner){CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);}
    private static void Layout(FrameworkElement element,int width=300){for(int i=0;i<3;i++){element.Measure(new(width,400));element.Arrange(new(0,0,width,400));element.UpdateLayout();}}
    [Theory] [InlineData("auto",true)] [InlineData("none",false)] [InlineData("AUTO",true)] [InlineData("NoNe",false)]
    public void KeywordsSetAnInheritedDiscretePolicy(string value,bool expected){Assert.True(CssAuthoring.IsValueSupported("font-optical-sizing",value));var box=Box();Style(box,"font-optical-sizing:"+value);Assert.Equal(expected,box.GetValue(Policy));}
    [Theory] [InlineData("")] [InlineData("normal")] [InlineData("12")] [InlineData("12px")] [InlineData("auto none")] [InlineData("auto,none")] [InlineData("\"auto\"")]
    public void InvalidValuesPreserveThePreviousDeclaration(string value){Assert.False(CssAuthoring.IsValueSupported("font-optical-sizing",value));var box=Box();Style(box,"font-optical-sizing:none;font-optical-sizing:"+value);Assert.Equal(false,box.GetValue(Policy));}
    [Theory] [InlineData("inherit",false)] [InlineData("unset",false)] [InlineData("initial",true)] [InlineData("all:initial",true)] [InlineData("font:32px serif",true)] [InlineData("font-variant:normal",false)]
    public void InheritanceAndShorthandResetsFollowTheOpticalLonghand(string value,bool expected){var box=Box();var root=new Border{Child=box};Style(root,"font-optical-sizing:none");Style(box,value.Contains(':')?value:"font-optical-sizing:"+value);Evaluate(root);Assert.Equal(expected,box.GetValue(Policy));}
    [Fact]
    public void VariablesImportantAndRemovalRestoreTheCurrentInheritedValue(){var child=Box();var root=new Border{Child=child};Style(root,"font-optical-sizing:none");Style(child,"--opt:auto;font-optical-sizing:var(--opt)!important;font-optical-sizing:none");Assert.Equal(true,child.GetValue(Policy));Style(child,"");Assert.Equal(false,child.GetValue(Policy));Style(root,"font-optical-sizing:auto");Assert.Equal(true,child.GetValue(Policy));}
    [Fact]
    public void PolicySurvivesAllExistingPrivateFontWrappers(){var box=Box();Style(box,"font-optical-sizing:none;font-variation-settings:'UPWD' 175;font-variant-caps:unicase;font-variant-position:super;font-variant-emoji:text;font-width:87.5%");string key=FontFeatureRenderingSource.WithCapsFragment(box.FontFamily.GetRenderingSource(box),3,1,2,1);key=FontWidthRenderingSource.ForFormattedText(key,7);Assert.True(FontFeatureRenderingSource.TryUnwrap(key,out _,out _,out _,out var context));Assert.Equal((byte)2,context.OpticalSizing);Assert.Single(context.Variations!);Assert.Equal((byte)1,context.EmojiAfter);}
    [Theory] [InlineData(6)] [InlineData(12)] [InlineData(24)] [InlineData(48)] [InlineData(64)] [InlineData(128)]
    public void AutoAndNoneReachTheCorrectPerFaceNativeInstance(double size){var box=Box();box.FontSize=size;foreach(string policy in new[]{"auto","none"}){Style(box,"font-optical-sizing:"+policy);using var actual=TextMeasurement.CreateTextFormatFromFamilyList(_context,box.FontFamily.GetRenderingSource(box),(float)size,400,0);using var expected=_context.CreateTextFormat(_font.Family,(float)size,400,0);Assert.True(expected.TrySetFontVariations([new(0x6f70737a,policy=="auto"?Math.Clamp(size,6,128):12)]));Assert.Equal(expected.MeasureText(box.Text,10000,1000).Width,actual.MeasureText(box.Text,10000,1000).Width,3);Layout(box,80);box.SelectAll();Assert.Equal("ABMABM",box.SelectedText);}}
    [Theory] [InlineData("auto")] [InlineData("none")]
    public void PropertyOpszOverridesThePolicyAndOtherAxesRemainIndependent(string policy){var box=Box();Style(box,$"font-optical-sizing:{policy};font-variation-settings:'opsz' 96,'UPWD' 175");Assert.True(FontFeatureRenderingSource.TryUnwrap(box.FontFamily.GetRenderingSource(box),out _,out _,out _,out var context));Assert.Equal(policy=="auto"?1:2,context.OpticalSizing);var axes=CssFontOpticalSizing.Compose(context.OpticalSizing,[new(0x6f70737a,48),new(0x46565454,10)],context.Variations!);Assert.Equal(new[]{0x46565454u,0x55505744u,0x6f70737au},axes.Select(a=>a.Tag));Assert.Equal(96,axes.Single(a=>a.Tag==0x6f70737a).Value);}
    [Fact]
    public void AutoOverridesDescriptorOpszAndNoneRetainsAuthoredDescriptor(){var desc=new FontVariation[]{new(0x6f70737a,96),new(0x55505744,250)};Assert.Equal(new[]{new FontVariation(0x55505744,250)},CssFontOpticalSizing.Compose(1,desc,[]));Assert.Equal(desc,CssFontOpticalSizing.Compose(2,desc,[]));}
    [Fact]
    public void StyleQueriesAndDiscreteAnimationCompareTheKeyword(){var child=Box();var root=new Border{Child=child};Css.SetStyleSheet(root,"@container style(font-optical-sizing:none){TextBlock{opacity:.4}}");Style(root,"font-optical-sizing:none");Assert.Equal(.4,child.Opacity,6);Style(root,"font-optical-sizing:auto");Assert.Equal(1,child.Opacity,6);Style(root,"transition:font-optical-sizing 1s linear allow-discrete");var data=((CssTransitionData)root.GetValue(CssTransitions.DataProperty)!).Find(root,Policy)!;Assert.Null(CssTransitions.Create(root,Policy,true,false,data with {AllowDiscrete=false}));var animation=Assert.IsAssignableFrom<AnimationTimeline>(CssTransitions.Create(root,Policy,true,false,data));var clock=animation.CreateClock();foreach(double p in new[]{0,.25,.5,.75,1}){clock.SetProgressForSampling(p);Assert.Equal(p<.5,animation.GetCurrentValue(true,false,clock));}}
    [Fact]
    public void KeyframeAnimationAndFontUnitsUseTheAnimatedPolicy(){var box=new TextBlock{Text="MMMM",FontFamily="SF Pro",FontSize=32};Style(box,"font-optical-sizing:auto;width:10ch");Css.SetStyleSheet(box,"@keyframes opt{from{font-optical-sizing:auto}to{font-optical-sizing:none}}");Evaluate(box);var node=CssNode.Get(box);var tracks=CssKeyframes.BuildPropertyTracks(node,CssKeyframes.Find(node,"opt")!);var sampler=new CssPropertySampler(Policy,tracks[Policy]);try{foreach(double p in new[]{0d,.75,.25,1}){box.SetCssAnimatedValue(Policy,sampler.Sample(p,new("linear")));Layout(box);Assert.Equal(p<.5,box.GetValue(Policy));Assert.Equal(10*TextMeasurement.GetFontUnitMetrics(box.FontFamily.GetRenderingSource(box),32).ZeroAdvance,box.Width,3);}}finally{box.ClearCssAnimatedValue(Policy);}}
    [Fact]
    public void SharedDocumentReflowsWhenOpticalPolicyChangesAndRestoresItsSource()
    {
        const string source="MMMM MMMM MMMM MMMM MMMM";var run=new Run(source);var span=new Span(run);var paragraph=new Paragraph(span);var document=new FlowDocument(paragraph){FontFamily="SF Pro",FontSize=48,LineHeight=72,PagePadding=new(0)};
        double Pair(byte policy){using var f=_context.CreateTextFormat("SF Pro",48,400,0);Assert.True(f.TrySetFontOpticalVariations([],policy));return f.MeasureText("MMMM MMMM",10000,1000).Width;}
        double a=Pair(1),b=Pair(2);Assert.True(Math.Abs(a-b)>1);int width=(int)Math.Floor((a+b)/2);
        using var one=new FlowDocumentPresentation();using var two=new FlowDocumentPresentation();one.SetDocument(document);two.SetDocument(document);
        Style(span,"font-optical-sizing:auto");Layout(one,width);Layout(two,width);double before=one.ExtentHeight;
        Style(span,"font-optical-sizing:none");Layout(one,width);Layout(two,width);Assert.NotEqual(before,one.ExtentHeight);Assert.Equal(one.ExtentHeight,two.ExtentHeight);
        Style(span,"font-optical-sizing:auto");Layout(one,width);Layout(two,width);Assert.Equal(before,one.ExtentHeight);Assert.Same(span,run.Parent);Assert.Equal(source+"\n",document.GetText());
    }
    [Fact]
    public void CssFontSizeUnitsBecomeCssPixelsBeforeOpticalSelection(){foreach(string value in new[]{"24px","18pt",".25in","calc(16px + 8px)"}){var box=new TextBlock{Text="B",FontFamily=_font.Family};Style(box,"font-size:"+value+";font-optical-sizing:auto");Assert.Equal(24,box.FontSize,6);Assert.True(FontFeatureRenderingSource.TryUnwrap(box.FontFamily.GetRenderingSource(box),out _,out _,out _,out var context));Assert.Equal((byte)1,context.OpticalSizing);Layout(box);}}
    [Fact]
    public void InheritedPolicyUpdatesSourceWithoutReplacingNodes(){var run=new Run("ABM");var span=new Span(run);var block=new TextBlock{FontFamily=_font.Family,FontSize=32};block.Inlines.Add(span);Style(block,"font-optical-sizing:none");foreach(string policy in new[]{"auto","none","auto"}){Style(span,"font-optical-sizing:"+policy);Layout(block,80);Assert.True(FontFeatureRenderingSource.TryUnwrap(run.FontFamily.GetRenderingSource(run),out _,out _,out _,out var context));Assert.Equal(policy=="auto"?1:2,context.OpticalSizing);Assert.Same(span,run.Parent);Assert.Equal("ABM",run.Text);}}

    [Fact]
    public void AnimatedFontSizeReselectsTheAutomaticOpticalCoordinate()
    {
        var box=new TextBlock{Text="MMMM AVAV",FontFamily="SF Pro"};Style(box,"display:inline-block;width:max-content;font-optical-sizing:auto");Css.SetStyleSheet(box,"@keyframes size{from{font-size:12px}to{font-size:128px}}");Evaluate(box);
        var node=CssNode.Get(box);var tracks=CssKeyframes.BuildPropertyTracks(node,CssKeyframes.Find(node,"size")!);var sampler=new CssPropertySampler(TextBlock.FontSizeProperty,tracks[TextBlock.FontSizeProperty]);
        try{foreach(double progress in new[]{0d,.5,1,.25}){box.SetCssAnimatedValue(TextBlock.FontSizeProperty,sampler.Sample(progress,new("linear")));Layout(box,1000);Assert.Equal(12+116*progress,box.FontSize,6);using var reference=_context.CreateTextFormat("SF Pro",(float)box.FontSize,400,0);Assert.True(reference.TrySetFontVariations([new(0x6f70737a,box.FontSize)]));Assert.Equal(reference.MeasureText(box.Text,10000,1000).Width,box.DesiredSize.Width,3);}}
        finally{box.ClearCssAnimatedValue(TextBlock.FontSizeProperty);}
    }

    [Fact]
    public void PublicAccessibilityFontNamesDoNotExposePrivateRenderingIdentities()
    {
        foreach(Control editor in new Control[]{new TextBox{Text="AV🙂"},new RichTextBox(FlowDocument.FromText("AV🙂")),new EditControl{Text="AV🙂"}})
        {
            editor.FontFamily="Arial";if(editor is RichTextBox rich)rich.Document.FontFamily=editor.FontFamily;
            foreach(string policy in new[]{"none","auto"}){Style(editor,$"font-optical-sizing:{policy};font-variation-settings:'opsz' 32;font-width:87.5%;font-variant-caps:small-caps");var provider=(ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;Assert.Equal("Arial",provider.DocumentRange.GetAttributeValue(40005));Assert.Equal("AV🙂",provider.DocumentRange.GetText(-1).TrimEnd('\r','\n'));}
        }
        var plan=new CssFontRenderingPlan(1,0,[new("Arial",400,[new(0,0x10ffff)],false,false)]).Encode();
        string source=FontFeatureRenderingSource.Wrap(FontWidthRenderingSource.Wrap(plan,87.5),[],CssCapsVariant.Normal,new(OpticalSizing:2));Assert.Equal("Arial",AutomationTextStyles.FamilyName(source));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void IdenticalInheritedFacesKeepKerningAcrossRunAndSpanBoundaries(bool nested)
    {
        var plain=new TextBlock{Text="AVAV"};var last=new Run("VAV");var block=new TextBlock();block.Inlines.Add(new Run("A"));block.Inlines.Add(nested?new Span(last):last);var parent=last.Parent;
        foreach(var box in new[]{plain,block}){Css.SetStyleSheet(box,"@font-face{font-family:OptFace;src:local('SF Pro');font-variation-settings:'opsz' 96}");Style(box,"font-family:OptFace;font-size:48px;font-optical-sizing:auto");Layout(box,1000);}
        Assert.Equal(plain.DesiredSize.Width,block.DesiredSize.Width,3);
        Style(last,"font-optical-sizing:none");Layout(block,1000);Assert.NotEqual(plain.DesiredSize.Width,block.DesiredSize.Width);
        Style(last,"");Layout(block,1000);Assert.Equal(plain.DesiredSize.Width,block.DesiredSize.Width,3);Assert.Same(parent,last.Parent);Assert.Equal("VAV",last.Text);
    }
}
