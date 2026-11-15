using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssFontSizeAdjustTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _primary=Load("Primary"),_secondary=Load("Secondary"),_missing=Load("Missing");
    internal static string Fixture(string name) => MacOSTestAssets.PathTo("FontSizeAdjust/" + name + ".ttf");
    private static CssNativeFontResource Load(string name)=>CssNativeFontResource.Create(File.ReadAllBytes(Fixture(name)))??throw new Exception("Fixture registration failed");
    public void Dispose(){_primary.Dispose();_secondary.Dispose();_missing.Dispose();_context.Dispose();}
    private static DependencyProperty Adjustment=>CssFontSizeAdjust.ValueProperty;
    private TextBlock Box(string text="xA")=>new(){Text=text,IsTextSelectionEnabled=true};
    private static void Style(DependencyObject owner,string css){Css.SetStyle(owner,css);Evaluate(owner);}
    private static void Evaluate(DependencyObject owner){CssEvaluationScheduler.InvalidateSubtree(CssNode.Get(owner));CssEvaluationScheduler.FlushIfPending(owner.Dispatcher);}
    private static void Layout(FrameworkElement owner,int width=1000){for(int i=0;i<3;i++){owner.Measure(new(width,500));owner.Arrange(new(0,0,width,500));owner.UpdateLayout();}}
    private string Family=>"'"+_primary.Family+"','"+_secondary.Family+"'";
    private string Base=>"display:inline-block;width:max-content;font-family:"+Family+";font-size:32px;text-autospace:no-autospace;";

    [Theory]
    [InlineData("none",0,0)] [InlineData("0",1,0)] [InlineData(".5",1,.5)] [InlineData("ex-height .5",1,.5)]
    [InlineData("cap-height .75",2,.75)] [InlineData("ch-width .6",3,.6)] [InlineData("ic-width 1",4,1)] [InlineData("ic-height 1.2",5,1.2)]
    [InlineData("CAP-HEIGHT 0.8",2,.8)] [InlineData("calc(.5 + .25)",1,.75)] [InlineData("cap-height calc(-.5)",2,0)]
    public void ValidValuesStoreMetricAndUnclampedNonNegativeAspect(string value,int metric,double aspect)
    {
        Assert.True(CssAuthoring.IsValueSupported("font-size-adjust",value));var box=Box();Style(box,Base+"font-size-adjust:"+value);var actual=Assert.IsType<CssFontSizeAdjust>(box.GetValue(Adjustment));Assert.Equal(metric,actual.Metric);Assert.Equal(aspect,actual.Aspect,8);Assert.Equal(32,box.FontSize);
    }
    [Theory]
    [InlineData("")] [InlineData("auto")] [InlineData("-1")] [InlineData("50%")] [InlineData("1px")] [InlineData(".5 ex-height")]
    [InlineData("ex-height")] [InlineData("cap-height none")] [InlineData("ch .5")] [InlineData("ic 1")] [InlineData("none .5")] [InlineData("none from-font")]
    [InlineData("from-font none")] [InlineData("from-font .5")] [InlineData("from-font cap-height")] [InlineData("from-font from-font")]
    [InlineData("ex-height .5 from-font")] [InlineData("cap-height .5 .8")] [InlineData("ic-height from-font none")]
    public void InvalidDeclarationsKeepTheEarlierValue(string value)
    {Assert.False(CssAuthoring.IsValueSupported("font-size-adjust",value));var box=Box();Style(box,Base+"font-size-adjust:.75;font-size-adjust:"+value);Assert.Equal(new CssFontSizeAdjust(1,.75),box.GetValue(Adjustment));}

    [Theory] [InlineData(1,.5)] [InlineData(2,.75)] [InlineData(3,.6)] [InlineData(4,1)] [InlineData(5,1.2)]
    public void NativeRatiosBelongToTheSelectedFaceAndDoNotUseGlyphFallback(byte metric,double expected)
    {
        foreach(var (font,factor) in new[]{(_primary,1d),(_secondary,.5)}){using var f=_context.CreateTextFormat(font.Family,32,400,0);Assert.True(f.TryGetFontAspectMetric(metric,out double actual));Assert.Equal(expected*factor,actual,5);}
        using var missing=_context.CreateTextFormat(_missing.Family,32,400,0);Assert.True(missing.TryGetFontAspectMetric(metric,out double absent));Assert.Equal(0,absent);
    }
    [Theory] [InlineData("ex-height",1,.5)] [InlineData("cap-height",2,.75)] [InlineData("ch-width",3,.6)] [InlineData("ic-width",4,1)] [InlineData("ic-height",5,1.2)]
    public void FromFontComputesANumberBeforeInheritance(string keyword,int metric,double expected)
    {
        var child=Box("A");var root=new Border{Child=child};Style(root,"font-family:'"+_primary.Family+"';font-size:32px;font-size-adjust:"+keyword+" from-font");Style(child,"font-family:'"+_secondary.Family+"'");Layout(root);Assert.Equal(metric,((CssFontSizeAdjust)root.GetValue(Adjustment)!).Metric);Assert.Equal(expected,((CssFontSizeAdjust)root.GetValue(Adjustment)!).Aspect,5);Assert.Equal(root.GetValue(Adjustment),child.GetValue(Adjustment));
        using var actual=TextMeasurement.CreateTextFormatFromFamilyList(_context,child.FontFamily.GetRenderingSource(child),32,400,0);Assert.Equal(64,actual.UsedFontSize,3);
        Style(child,"font-family:'"+_secondary.Family+"';font-size-adjust:"+keyword+" from-font");Assert.Equal(expected*.5,((CssFontSizeAdjust)child.GetValue(Adjustment)!).Aspect,5);
    }
    [Theory] [InlineData("ex-height",.75)] [InlineData("cap-height",1.125)] [InlineData("ch-width",.9)] [InlineData("ic-width",1.5)] [InlineData("ic-height",1.8)]
    public void EachFallbackUsesItsOwnScaleAndSourceIndicesRemainUnchanged(string metric,double target)
    {
        var box=Box();Style(box,Base+"font-size-adjust:"+metric+" "+target.ToString(CultureInfo.InvariantCulture));Layout(box);
        using var one=_context.CreateTextFormat(_primary.Family,48,400,0);using var two=_context.CreateTextFormat(_secondary.Family,96,400,0);
        using var actual=TextMeasurement.CreateTextFormatFromFamilyList(_context,box.FontFamily.GetRenderingSource(box),32,400,0);
        double a=one.MeasureText("x",10000,1000).Width,b=two.MeasureText("A",10000,1000).Width;
        Assert.Equal(48,actual.UsedFontSize,3);Assert.Equal(a+b,actual.MeasureText("xA",10000,1000).Width,3);Assert.Equal(a+b,box.DesiredSize.Width,3);
        Assert.True(actual.HitTestTextPosition("xA",10000,1000,1,false,out var caret));Assert.Equal(a,caret.CaretX,3);
        box.SelectAll();Assert.Equal("xA",box.SelectedText);
    }
    [Theory]
    [InlineData("VerticalVariable",400,1.2)] [InlineData("VerticalVariable",550,1.3)] [InlineData("VerticalVariable",700,1.4)]
    [InlineData("VerticalMapped",400,1.2)] [InlineData("VerticalMapped",550,1.3)] [InlineData("VerticalMapped",700,1.4)]
    [InlineData("VerticalAvar",550,1.25)] [InlineData("VerticalAvar2",550,1.275)]
    public void VerticalMetricUsesVvarAndNormalizedAvarCoordinates(string name,double weight,double expected)
    {
        using var resource=Load(name);using var format=_context.CreateTextFormat(resource.Family,32,400,0);
        Assert.True(format.TrySetFontOpticalVariations([new(0x77676874,weight)],2));
        Assert.True(format.TryGetFontAspectMetric(5,out double ratio));Assert.Equal(expected,ratio,5);
        Assert.True(format.TrySetFontSizeAdjust(new(5,2.4),new(Variations:[new(0x77676874,weight)],OpticalSizing:2)));
        Assert.Equal(32*2.4/expected,format.UsedFontSize,3);
        var box=Box("水");Style(box,"font-family:'"+resource.Family+"';font-size:32px;font-variation-settings:'wght' "+weight.ToString(CultureInfo.InvariantCulture)+";font-size-adjust:ic-height from-font");
        Assert.Equal(expected,Assert.IsType<CssFontSizeAdjust>(box.GetValue(Adjustment)).Aspect,5);
    }
    [Fact]
    public void UnitsUseAdjustedMetricsWhileEmAndNumericLineHeightKeepComputedSize()
    {
        var box=Box("x");Style(box,Base+"font-size-adjust:.75;width:10ch;padding-left:2em;line-height:1.25");Layout(box);Assert.Equal(32,box.FontSize);Assert.Equal(64,box.Padding.Left,6);Assert.Equal(288,box.Width,3);
        var line=CssComputedLineHeightValue.Inherited(CssNode.Get(box));Assert.Equal(CssLineHeightKind.Number,line.Kind);Assert.Equal(1.25,line.Value,6);
        var metrics=TextMeasurement.GetFontUnitMetrics(box.FontFamily.GetRenderingSource(box),32);Assert.Equal(24,metrics.XHeight,3);Assert.Equal(28.8,metrics.ZeroAdvance,3);
    }
    [Fact]
    public void ZeroHasNoAdvanceOrFontRelativeRulersAndNoneRestoresTheFont()
    {
        var box=Box("x");Style(box,Base+"font-size-adjust:0;width:10ch");Layout(box);using var actual=TextMeasurement.CreateTextFormatFromFamilyList(_context,box.FontFamily.GetRenderingSource(box),32,400,0);Assert.Equal(0,actual.UsedFontSize);Assert.Equal(0,actual.MeasureText("x",10000,1000).Width,8);Assert.Equal(0,box.Width,8);Assert.Equal(0,TextMeasurement.GetFontUnitMetrics(box.FontFamily.GetRenderingSource(box),32).XHeight,8);
        Style(box,Base+"font-size-adjust:none");Layout(box);Assert.True(box.DesiredSize.Width>0);Assert.Equal(32,box.FontSize);
    }
    [Fact]
    public void GlobalValuesVariablesAndFontShorthandResetTheIndependentProperty()
    {
        var box=Box();var root=new Border{Child=box};Style(root,"font-size-adjust:.75");foreach(var (value,expected) in new[]{("inherit",.75),("unset",.75),("initial",0d)}){Style(box,"font-size-adjust:"+value);Assert.Equal(expected==0?CssFontSizeAdjust.None:new(1,expected),box.GetValue(Adjustment));}
        Style(box,"--a:.5;font-size-adjust:var(--a)!important;font-size-adjust:.75");Assert.Equal(new CssFontSizeAdjust(1,.5),box.GetValue(Adjustment));Style(box,"font-size-adjust:.5;font:32px serif");Assert.Equal(CssFontSizeAdjust.None,box.GetValue(Adjustment));Style(box,"font-size-adjust:.5;font-variant:normal");Assert.Equal(new CssFontSizeAdjust(1,.5),box.GetValue(Adjustment));Style(box,"");Assert.Equal(new CssFontSizeAdjust(1,.75),box.GetValue(Adjustment));
    }
    [Fact]
    public void QueriesAndContinuousAnimationUseTheComputedMetricNumberPair()
    {
        var child=Box("x");var root=new Border{Child=child};Style(root,"font-family:'"+_primary.Family+"';font-size:32px;font-size-adjust:from-font");Css.SetStyleSheet(root,"@container style(font-size-adjust:.5){TextBlock{opacity:.4}}");Evaluate(root);Assert.Equal(.4,child.Opacity,6);
        Style(child,Base+"width:10ch;transition:font-size-adjust 1s linear");var data=((CssTransitionData)child.GetValue(CssTransitions.DataProperty)!).Find(child,Adjustment)!;var animation=Assert.IsAssignableFrom<AnimationTimeline>(CssTransitions.Create(child,Adjustment,new CssFontSizeAdjust(1,.5),new CssFontSizeAdjust(1,1),data));var clock=animation.CreateClock();
        try{foreach(double p in new[]{0d,.25,.5,1}){clock.SetProgressForSampling(p);var value=Assert.IsType<CssFontSizeAdjust>(animation.GetCurrentValue(null!,null!,clock));Assert.Equal(.5+.5*p,value.Aspect,6);child.SetCssAnimatedValue(Adjustment,value);Layout(root);Assert.Equal(10*32*(value.Aspect/.5)*.6,child.Width,3);}}
        finally{child.ClearCssAnimatedValue(Adjustment);}
        Assert.Null(CssTransitions.Create(child,Adjustment,new CssFontSizeAdjust(1,.5),new CssFontSizeAdjust(2,.5),data));Assert.NotNull(CssTransitions.Create(child,Adjustment,new CssFontSizeAdjust(1,.5),new CssFontSizeAdjust(2,.5),data with{AllowDiscrete=true}));
    }
    [Theory] [InlineData("normal",1d)] [InlineData("sub",.65)] [InlineData("super",.65)]
    public void FallbackSelectedAfterCapsKeepsTheFaceAdjustmentAndPositionScale(string position,double scale)
    {
        var box=Box();Style(box,Base+"font-size-adjust:.75;font-variant-caps:small-caps;font-variant-position:"+position);Layout(box);
        using var x=_context.CreateTextFormat(_secondary.Family,(float)(96*.8*scale),400,0);using var a=_context.CreateTextFormat(_secondary.Family,(float)(96*scale),400,0);
        double expected=x.MeasureText("X",10000,1000).Width+a.MeasureText("A",10000,1000).Width;
        Assert.Equal(expected,box.DesiredSize.Width,3);box.SelectAll();Assert.Equal("xA",box.SelectedText);
    }
    [Theory] [InlineData(".75",24)] [InlineData("from-font",16)]
    public void RegisteredLengthsUseTheCurrentAdjustedMetrics(string adjustment,double expected)
    {
        var box=Box("x");Css.SetStyleSheet(box,"@property --ruler{syntax:'<length>';inherits:false;initial-value:1px}");
        Style(box,Base+"font-size-adjust:"+adjustment+";--ruler:1ex;width:var(--ruler)");Layout(box);Assert.Equal(expected,box.Width,4);
    }
    [Theory] [InlineData(400,32)] [InlineData(700,27.4285714286)]
    public void RegisteredLengthSeesAxesDeclaredAlongsideTheAdjustment(int weight,double expected)
    {
        using var resource=Load("VerticalVariable");var box=Box("x");
        Css.SetStyleSheet(box,"@property --ruler{syntax:'<length>';inherits:false;initial-value:1px}");
        Style(box,"font-family:'"+resource.Family+"';font-size:32px;font-variation-settings:'wght' "+weight+";font-size-adjust:ic-height 2.4;--ruler:1ex;width:var(--ruler)");Layout(box);
        Assert.Equal(expected,box.Width,3);
    }
    [Fact]
    public void RegisteredFontMetricCycleFallsBackInsteadOfOscillating()
    {
        var box=Box("x");Css.SetStyleSheet(box,"@property --aspect{syntax:'<number>';inherits:false;initial-value:.5}");
        Style(box,Base+"--aspect:calc(1ex / 32px);font-size-adjust:var(--aspect)");Layout(box);
        Assert.Equal(CssFontSizeAdjust.None,box.GetValue(Adjustment));
        Assert.Equal("0.5",CssEngine.EnsureState(CssNode.Get(box)).CustomProperties!["--aspect"]);
    }
    [Fact]
    public void RegisteredAxisCycleUsesTheInitialValueAndNormalCoordinates()
    {
        using var resource=Load("VerticalVariable");var box=Box("x");
        Css.SetStyleSheet(box,"@property --weight{syntax:'<number>';inherits:false;initial-value:400}");
        Style(box,"font-family:'"+resource.Family+"';font-size:32px;font-variation-settings:'wght' var(--weight);font-size-adjust:ic-height 2.4;--weight:calc(1ex / 1px * 10)");Layout(box);
        Assert.Equal("400",CssEngine.EnsureState(CssNode.Get(box)).CustomProperties!["--weight"]);
        Assert.Empty(Assert.IsType<CssFontVariationSettings>(box.GetValue(CssFontVariationSettings.SettingsProperty)).Axes);
    }
    [Fact]
    public void MissingFromFontMetricComputesToNoneAndRestoresAfterFamilyChange()
    {
        var box=Box("A");Style(box,"font-size:32px;font-family:'"+_missing.Family+"';font-size-adjust:from-font");Assert.Equal(CssFontSizeAdjust.None,box.GetValue(Adjustment));
        Style(box,"font-size:32px;font-family:'"+_primary.Family+"';font-size-adjust:from-font");Assert.Equal(new CssFontSizeAdjust(1,.5),box.GetValue(Adjustment));
    }
    [Fact]
    public void PrivateFontIdentitySurvivesCapsPositionEmojiWidthAndSourceMaterialization()
    {
        var box=Box();Style(box,Base+"font-size-adjust:.75;font-variant-caps:unicase;font-variant-position:super;font-variant-emoji:text;font-width:87.5%");var key=FontFeatureRenderingSource.WithCapsFragment(box.FontFamily.GetRenderingSource(box),3,1,2,1);key=FontWidthRenderingSource.ForFormattedText(key,7);key=CssFontFaces.MaterializeSource(key,"xA");Assert.True(FontFeatureRenderingSource.TryUnwrap(key,out _,out _,out _,out var context));Assert.Equal(new CssFontSizeAdjust(1,.75),context.SizeAdjust);Assert.Equal((byte)1,context.OpticalSizing);Assert.Equal((byte)1,context.EmojiAfter);
    }
}
