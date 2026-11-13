using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class CssFontFeaturePriorityTests : IDisposable
{
    private readonly RenderContext _context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
    private readonly CssNativeFontResource _font=CssNativeFontResource.Create(File.ReadAllBytes(Fixture))!;
    internal static string Fixture=>MacOSTestAssets.PathTo("FontFeaturePriority/selectable-variants.otf");
    public void Dispose(){_font.Dispose();_context.Dispose();}
    [Theory]
    [InlineData("super",0)] [InlineData("super",1)] [InlineData("super",2)] [InlineData("super",3)]
    [InlineData("sub",0)] [InlineData("sub",1)] [InlineData("sub",2)] [InlineData("sub",3)]
    public void ExplicitIndexOverridesNativePositionWithoutBooleanCoercion(string position,int index)
        => CheckPosition(position,index,false);
    [Theory]
    [InlineData("super",0)] [InlineData("super",1)] [InlineData("super",2)] [InlineData("super",3)]
    [InlineData("sub",0)] [InlineData("sub",1)] [InlineData("sub",2)] [InlineData("sub",3)]
    public void ExplicitIndexIsAppliedAfterWholeRunSynthesis(string position,int index)
        => CheckPosition(position,index,true);
    private void CheckPosition(string position,int index,bool synthetic)
    {
        string source=synthetic?"xz":"x",tag=position=="sub"?"subs":"sups";
        var text=Box(source);Style(text,$"font-variant-position:{position};font-feature-settings:'{(position=="sub"?"subs":"sups")}' {index}");
        using var actual=Paragraph(source,text.FontFamily.GetRenderingSource(text));
        using var reference=_context.CreateTextFormat(_font.Family,synthetic?20.8f:32,400,0);
        Assert.Equal(JaliumResult.Ok,reference.SetFontFeatures([new(FontFeature.TagOf(tag),(uint)index)]));
        Assert.NotNull(actual);Assert.NotNull(reference);Assert.Equal(reference.MeasureText(source,10000,1000).Width,actual.Lines[0].Metrics.Line.Width,3);
        Assert.Equal(Enumerable.Repeat(synthetic?(byte)2:(byte)1,source.Length),actual.PositionModes());
        foreach(int width in new[]{24,80,200}) {Layout(text,width);text.SelectAll();Assert.Equal(source,text.SelectedText);}
        Assert.Equal(source,text.Text);Assert.Equal(_font.Family,text.FontFamily.Source);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void ExplicitSmcpIndexSurvivesUnicaseFallback(int index)
    {
        var text=Box("A");Style(text,$"font-variant-caps:unicase;font-feature-settings:'smcp' {index}");
        using var actual=Paragraph("A",text.FontFamily.GetRenderingSource(text));
        using var reference=_context.CreateTextFormat(_font.Family,32,400,0);
        Assert.Equal(JaliumResult.Ok,reference.SetFontFeatures([new(FontFeature.TagOf("smcp"),(uint)index)]));
        Assert.NotNull(actual);Assert.NotNull(reference);Assert.Equal(reference.MeasureText("a",10000,1000).Width,actual.Lines[0].Metrics.Line.Width,3);
        Layout(text,200);text.SelectAll();Assert.Equal("A",text.SelectedText);
    }
    [Fact]
    public void AdjacentSourcesRetainTheirOwnExactSelectors()
    {
        string Key(int value){var text=Box("x");Style(text,$"font-variant-position:super;font-feature-settings:'sups' {value}");return text.FontFamily.GetRenderingSource(text);}
        using var actual=NativeTextParagraph.TryCreate([new("x",Key(2),32,400,0,Colors.Black),new("x",Key(3),32,400,0,Colors.Red)],_font.Family,32,10000,44,TextAlignment.Left,FlowDirection.LeftToRight);
        using var reference=Paragraph("bb",_font.Family);Assert.NotNull(actual);Assert.Equal(reference!.Lines[0].Metrics.Line.Width,actual.Lines[0].Metrics.Line.Width,3);
        Assert.Equal(new byte[]{1,1},actual.PositionModes());
    }
    [Fact]
    public void ReplacingRawSettingsDoesNotRetainThePreviousSelector()
    {
        var text=Box("x");
        foreach(int index in new[]{2,3,0,1,2}) {
            Style(text,$"font-variant-position:super;font-feature-settings:'sups' {index}");Layout(text,200);
            using var actual=Paragraph("x",text.FontFamily.GetRenderingSource(text));using var expected=_context.CreateTextFormat(_font.Family,32,400,0);expected.SetFontFeatures([new(FontFeature.TagOf("sups"),(uint)index)]);
            Assert.Equal(expected.MeasureText("x",10000,1000).Width,actual!.Lines[0].Metrics.Line.Width,3);
        }
    }
    [Theory] [InlineData(0,"x")] [InlineData(1,"b")] [InlineData(2,"c")] [InlineData(3,"d")]
    public void TrulyNonBooleanFeatureRetainsItsSelectionIndex(int index,string literal)
    {
        var text=Box("x");Style(text,$"font-feature-settings:'salt' {index}");
        using var actual=Paragraph("x",text.FontFamily.GetRenderingSource(text));using var expected=Paragraph(literal,_font.Family);
        Assert.Equal(expected!.Lines[0].Metrics.Line.Width,actual!.Lines[0].Metrics.Line.Width,3);
    }
    private TextBlock Box(string source)=>new(){Text=source,FontFamily=_font.Family,FontSize=32,IsTextSelectionEnabled=true};
    private NativeTextParagraph? Paragraph(string source,string family,double size=32)=>NativeTextParagraph.TryCreate([new(source,family,size,400,0,Colors.Black)],_font.Family,size,10000,44,TextAlignment.Left,FlowDirection.LeftToRight);
    private static void Style(DependencyObject owner,string css){Css.SetStyle(owner,css);CssEngine.EvaluateElement(CssNode.Get(owner));}
    private static void Layout(FrameworkElement view,int width){for(int i=0;i<3;i++){view.Measure(new(width,400));view.Arrange(new(0,0,width,400));view.UpdateLayout();}}
}
