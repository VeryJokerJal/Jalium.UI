using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgStyleSheetTests
{
    [Theory]
    [InlineData("rect{fill:red}")]
    [InlineData(".paint{fill:red}")]
    [InlineData("#shape{fill:red}")]
    [InlineData("g > rect.paint[data-kind='mark']{fill:red}")]
    [InlineData("rect:first-of-type{fill:red}")]
    [InlineData("rect:nth-child(1){fill:red}")]
    [InlineData("g:has(> .paint){fill:red}rect{fill:inherit}")]
    [InlineData(":is(#missing,.paint){fill:red}")]
    [InlineData(":where(#shape){fill:blue}.paint{fill:red}")]
    [InlineData("@namespace s url(http://www.w3.org/2000/svg);s|rect{fill:red}")]
    [InlineData("rect:lang(zh){fill:red}")]
    [InlineData(".\\70 aint{fill:red}")]
    [InlineData("@scope (g.layer){:scope > rect{fill:red}}")]
    [InlineData("rect{fill:red}DependencyObject{fill:blue!important}")]
    [InlineData("rect{fill:red}CssXmlElement{fill:blue!important}")]
    public void SharedSelectorsStyleTheActualXmlTree(string css)
    {
        var image = Parse(css, "<g class='layer' xml:lang='zh-CN'><rect id='shape' class='paint' data-kind='mark' width='80' height='60'/></g>");
        Equal(Pixel(image, 30, 20), 255, 0, 0, 255);
    }

    [Fact]
    public void AStrokeDashListWithATrailingCommaDoesNotOverrideAnEarlierValidDeclaration()
    {
        var image = Parse("line{stroke:red;stroke-width:8px;stroke-dasharray:40 40;stroke-dasharray:8,}",
            "<line x1='0' y1='30' x2='80' y2='30'/>");
        var line = Assert.Single(Geometries(image.Drawing!));
        Assert.Equal(new[] { 5d, 5d }, Assert.IsType<DashStyle>(line.Pen!.DashStyle).Dashes);

        static IEnumerable<GeometryDrawing> Geometries(Drawing drawing)
        {
            if (drawing is GeometryDrawing geometry) yield return geometry;
            if (drawing is DrawingGroup group)
                foreach (var child in group.Children)
                    foreach (var segment in Geometries(child)) yield return segment;
        }
    }

    [Theory]
    [InlineData("rect{fill:red}rect{fill:blue}#shape{fill:red}", "fill='lime'")]
    [InlineData("rect{fill:red!important}", "style='fill:blue'")]
    [InlineData("rect{fill:blue!important}", "style='fill:red!important;fill:lime'")]
    [InlineData("rect{fill:blue}", "style='fill:red;fill:invalid-color'")]
    [InlineData("@layer first,second;@layer first{rect{fill:red!important}}@layer second{rect{fill:blue!important}}", "")]
    [InlineData("@layer first{rect{fill:blue}}rect{fill:red}", "fill='lime'")]
    [InlineData("@layer first{rect{fill:red}}@layer second{rect{fill:blue;fill:revert-layer}}", "")]
    [InlineData("@layer first{rect{fill:red}}@layer second{rect{fill:var(--missing,revert-layer)}}", "")]
    public void CascadePriorityRetainsValidEarlierDeclarations(string css, string attributes)
    {
        var image = Parse(css, $"<rect id='shape' {attributes} width='80' height='60'/>"); Equal(Pixel(image, 30, 20), 255, 0, 0, 255);
    }

    [Theory]
    [InlineData(":root{--paint:red}rect{fill:var(--paint)}")]
    [InlineData("g{--paint:red}rect{fill:var(--paint,blue)}")]
    [InlineData("rect{--paint:var(--missing,red);fill:var(--paint)}")]
    [InlineData("rect{--a:var(--b);--b:var(--a);fill:var(--a,red)}")]
    [InlineData("g{fill:red}rect{fill:var(--missing)}")]
    [InlineData("g{fill:red}rect{fill:unset}")]
    [InlineData("g{fill:red}rect{fill:inherit}")]
    public void VariablesAndCssWideValuesResolveBeforeSvgPainting(string css)
    { Equal(Pixel(Parse(css, "<g><rect width='80' height='60'/></g>"), 30, 20), 255, 0, 0, 255); }

    [Theory]
    [InlineData("#f008", 255, 0, 0, 136)]
    [InlineData("rgb(255 0 0 / 50%)", 255, 0, 0, 128)]
    [InlineData("hsl(120 100% 50% / .5)", 0, 255, 0, 128)]
    [InlineData("color-mix(in srgb,red,blue)", 128, 0, 128, 255)]
    [InlineData("rgb(from red r g b / .5)", 255, 0, 0, 128)]
    public void SvgColorsUseTheSharedCssGrammarAndTrailingHexAlpha(string paint, int red, int green, int blue, int alpha)
    { Equal(Pixel(Parse($"rect{{fill:{paint}}}", "<rect width='80' height='60'/>") ,30,20),red,green,blue,alpha); }

    [Fact]
    public void GeometryPropertiesOverrideOnlyTheirApplicablePresentationAttributes()
    {
        var image = Parse("g{width:5px}rect{width:calc(25% + 20px);height:60px}", "<g><rect width='80' height='60' fill='red'/></g>");
        Equal(Pixel(image,20,20),255,0,0,255); Assert.Equal(0,Pixel(image,60,20).A);
    }

    [Fact]
    public void MediaSupportsAndStyleElementMediaUseTheSvgViewport()
    {
        var image=Parse("@media (min-width:70px){@supports(fill:red){rect{fill:red}}}","<rect width='80' height='60'/><style media='(min-width:90px)'>rect{fill:blue}</style>");
        Equal(Pixel(image,30,20),255,0,0,255);
    }

    [Fact]
    public void UseInstancesInheritVariablesAndColorFromTheirOwnInstance()
    {
        var image=Parse(".paint{fill:var(--paint,currentColor)}", "<defs><rect id='p' class='paint' width='40' height='60'/></defs><use href='#p' style='--paint:red'/><use href='#p' x='40' style='color:blue'/>");
        Equal(Pixel(image,20,20),255,0,0,255);Equal(Pixel(image,60,20),0,0,255,255);
    }

    [Fact]
    public void StyledGradientStopsKeepTheColorAlphaAndStopOpacity()
    {
        var image=Parse("stop{stop-color:rgb(255 0 0 / .5);stop-opacity:.5}","<defs><linearGradient id='g'><stop/><stop offset='1'/></linearGradient></defs><rect width='80' height='60' fill='url(#g)'/>");
        Equal(Pixel(image,30,20),255,0,0,64);
    }

    [Fact]
    public void DisplayAndVisibilityAreParsedDeclarationsAndOpacityIsAGroupEffect()
    {
        var image=Parse(".off{display: none !important}.hidden{visibility:hidden}.visible{visibility:visible}.fade{opacity:50%}","<g class='fade'><rect width='80' height='60' fill='red'/><rect width='80' height='60' fill='red'/></g><rect class='off' width='80' height='60' fill='blue'/><g class='hidden'><rect class='visible' x='60' width='20' height='60' fill='lime'/></g>");
        Equal(Pixel(image,30,20),255,0,0,128);Equal(Pixel(image,70,20),0,255,0,255);
    }

    [Fact]
    public void StyledClipPathsAndMaskTypesAffectOrdinarySvgContent()
    {
        var image=Parse("clipPath rect{width:40px;height:60px}mask{mask-type:alpha}.stencil{fill:black}#shape{clip-path:url(#c);mask:url(#m)}", "<defs><clipPath id='c'><rect width='80' height='60'/></clipPath><mask id='m'><rect class='stencil' width='80' height='60'/></mask></defs><rect id='shape' width='80' height='60' fill='red'/>");
        Equal(Pixel(image,20,20),255,0,0,255);Assert.Equal(0,Pixel(image,60,20).A);
    }

    [Fact]
    public void VariableKeywordFallbacksDoNotTurnIntoOpaquePaint()
    {
        var image=Parse("rect{fill:var(--missing, none)}", "<rect width='80' height='60'/>");
        Assert.Equal(0,Pixel(image,30,20).A);
    }

    [Fact]
    public void CssTransformLengthsAndNumericOpacityMathUseTheSharedParsers()
    {
        var image=Parse("rect{fill:red;transform:translate(10px,0);opacity:calc(.2 + .3)}", "<rect width='30' height='60'/>");
        Assert.Equal(0,Pixel(image,5,20).A);Equal(Pixel(image,20,20),255,0,0,128);
    }

    [Theory]
    [InlineData("@property --fade{syntax:'<number>';inherits:false;initial-value:.25}rect{opacity:var(--fade)}")]
    [InlineData("@property --fade{syntax:'<number>';inherits:true;initial-value:.25}rect{--fade:invalid;opacity:var(--fade)}")]
    public void RegisteredCustomPropertiesUseTheirTypedInitialValue(string css)
    { Equal(Pixel(Parse(css,"<rect width='80' height='60' fill='red'/>"),30,20),255,0,0,64); }

    [Fact]
    public void RegisteredLengthsSeeTheElementsOwnComputedFontSize()
    {
        var root=System.Xml.Linq.XElement.Parse("<svg xmlns='http://www.w3.org/2000/svg'><style>@property --size{syntax:'&lt;length&gt;';inherits:false;initial-value:1px}rect{font-size:24px;--size:2em;stroke-width:var(--size)}</style><rect/></svg>");
        var styles=new SvgCssStyles(root,new Size(80,60));Assert.Equal("48",styles.Get(root.Elements().Last(),"stroke-width"));
    }

    [Theory]
    [InlineData("rect{font:italic 700 20px serif}")]
    [InlineData("rect{--face:italic 700 20px serif;font:var(--face)}")]
    public void FontShorthandUsesTheSharedParserAndResetsItsLonghands(string css)
    {
        var root=System.Xml.Linq.XElement.Parse($"<svg xmlns='http://www.w3.org/2000/svg'><style>{css}</style><rect font-size='8' font-weight='300'/></svg>");
        var styles=new SvgCssStyles(root,new Size(80,60));var rect=root.Elements().Last();
        Assert.Equal("20",styles.Get(rect,"font-size"));Assert.Equal("700",styles.Get(rect,"font-weight"));Assert.Equal("italic",styles.Get(rect,"font-style"));
    }

    [Theory]
    [InlineData("xx-small")] [InlineData("x-small")] [InlineData("small")] [InlineData("medium")]
    [InlineData("large")] [InlineData("x-large")] [InlineData("xx-large")] [InlineData("xxx-large")]
    public void FontKeywordSizesAreConsistentBetweenShorthandAndLonghand(string keyword)
    {
        var root=System.Xml.Linq.XElement.Parse($"<svg xmlns='http://www.w3.org/2000/svg'><style>.a{{font:{keyword} serif}}.b{{font-size:{keyword}}}</style><rect class='a'/><rect class='b'/></svg>");
        var styles=new SvgCssStyles(root,new Size(80,60));var rects=root.Elements().Where(x=>x.Name.LocalName=="rect").ToArray();
        if(keyword=="medium")Assert.Equal("16",styles.Get(rects[0],"font-size"));
        Assert.Equal(styles.Get(rects[0],"font-size"),styles.Get(rects[1],"font-size"));
    }

    [Theory]
    [InlineData("initial")] [InlineData("inherit")] [InlineData("unset")]
    public void RegisteredLengthsFollowCssWideFontSizeValues(string value)
    {
        var root=System.Xml.Linq.XElement.Parse($"<svg xmlns='http://www.w3.org/2000/svg'><style>@property --size{{syntax:'&lt;length&gt;';inherits:false;initial-value:1px}}rect{{font-size:{value};--size:2em;stroke-width:var(--size)}}</style><rect/></svg>");
        var styles=new SvgCssStyles(root,new Size(80,60));Assert.Equal("32",styles.Get(root.Elements().Last(),"stroke-width"));
    }

    [Fact]
    public void HtmlNamespaceStyleElementsAlsoStyleSvgWithoutRenderingTheirText()
    {
        var image=Parse("", "<style xmlns='http://www.w3.org/1999/xhtml'>rect{fill:red}</style><rect width='80' height='60'/>");
        Equal(Pixel(image,30,20),255,0,0,255);
    }

    [Fact]
    public void PresentationAttributesDoNotAcceptImportantAndAllKeepsCustomProperties()
    {
        var image=Parse("rect{--paint:red;fill:blue;all:initial;fill:var(--paint)}", "<rect width='80' height='60' fill='lime!important' style='width:80px;height:60px'/>");
        Equal(Pixel(image,30,20),255,0,0,255);
    }

    [Fact]
    public void SelectorWorkHasAFiniteBudgetForUntrustedSvgDocuments()
    {
        var css=string.Concat(Enumerable.Repeat("rect{fill:red}",600));
        var body=string.Concat(Enumerable.Repeat("<rect width='80' height='60'/>",1000));
        var source=$"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><style>{css}</style>{body}</svg>";
        var error=Assert.Throws<InvalidDataException>(()=>SvgParser.Parse(source));Assert.Contains("work budget",error.Message);
        Assert.Null(SvgImage.FromSvgString(source).Drawing);
    }

    [Theory]
    [InlineData("#shape{mask-image:url(#empty);mask:url(#solid)}")]
    [InlineData("#shape{mask:url(#empty);mask-image:url(#solid)}")]
    [InlineData("#shape{mask:alpha url(#solid) right bottom / 1px repeat}")]
    public void LocalMaskShorthandAndLonghandShareTheirCascadeOrder(string css)
    {
        var image=Parse(css,"<defs><mask id='empty'/><mask id='solid'><rect width='80' height='60' fill='white'/></mask></defs><rect id='shape' width='80' height='60' fill='red'/>");
        Equal(Pixel(image,30,20),255,0,0,255);
    }

    [Theory]
    [InlineData("rect{width:50%;height:100%;fill:red}","")]
    [InlineData("","width='50%' height='100%' fill='red'")]
    public void PercentagesUseTheAppliedViewBoxInUserCoordinates(string css,string attributes)
    {
        var image=SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60' viewBox='0 0 160 120'><style>{css}</style><rect {attributes}/></svg>");
        Equal(Pixel(image,30,20),255,0,0,255);Assert.Equal(0,Pixel(image,60,20).A);
    }

    [Fact]
    public void RootCssDimensionsEstablishThePercentageViewport()
    {
        var image=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='30'><style>svg{width:80px;height:60px}rect{width:50%;height:100%;fill:red}</style><rect/></svg>");
        Assert.Equal(80,image.Width);Assert.Equal(60,image.Height);Equal(Pixel(image,30,20),255,0,0,255);Assert.Equal(0,Pixel(image,60,20).A);
    }

    [Fact]
    public void StyledTextOutlinesMatchEquivalentPresentationFonts()
    {
        using var native=RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        using var styled=SvgImage.FromSvgStringWithTextOutlines("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><style>text{font:italic 700 20px Arial;fill:red}</style><text x='5' y='35'>Ab</text></svg>");
        using var reference=SvgImage.FromSvgStringWithTextOutlines("<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><text x='5' y='35' font-size='20' font-weight='700' font-style='italic' font-family='Arial' fill='red'>Ab</text></svg>");
        var actual=SoftwareVectorRasterizer.Rasterize(styled.Drawing!,80,60,new Rect(0,0,80,60))!;
        Assert.Contains((byte)255,actual);Assert.Equal(SoftwareVectorRasterizer.Rasterize(reference.Drawing!,80,60,new Rect(0,0,80,60)),actual);
    }

    private static SvgImage Parse(string css,string body) => SvgImage.FromSvgString($"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'><style><![CDATA[{css}]]></style>{body}</svg>");
    private static Color Pixel(SvgImage image,int x,int y)
    {
        var bytes=SoftwareVectorRasterizer.Rasterize(image.Drawing!,80,60,new Rect(0,0,80,60))!;var at=(y*80+x)*4;return Color.FromArgb(bytes[at+3],bytes[at+2],bytes[at+1],bytes[at]);
    }
    private static void Equal(Color actual,int red,int green,int blue,int alpha)
    {Assert.InRange((int)actual.R,Math.Max(0,red-1),Math.Min(255,red+1));Assert.InRange((int)actual.G,Math.Max(0,green-1),Math.Min(255,green+1));Assert.InRange((int)actual.B,Math.Max(0,blue-1),Math.Min(255,blue+1));Assert.InRange((int)actual.A,Math.Max(0,alpha-1),Math.Min(255,alpha+1));}
}
