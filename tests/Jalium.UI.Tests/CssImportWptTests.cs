using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssImportWptTests
{
    static CssImportWptTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    private sealed class Resources(string main) : ICssResourceResolver
    {
        internal int Requests;
        public ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests++;
            var file=uri.AbsolutePath.TrimStart('/');
            var css=file switch
            {
                "main.css"=>main,
                "basic-green.css"=>".target {color:green}", "basic-red.css"=>".target {color:red}",
                "layer-green.css"=>"@layer {.target {color:green}}", "layer-red.css"=>"@layer {.target {color:red}}",
                "layer-A-green.css"=>"@layer A {.target {color:green}}", "layer-A-red.css"=>"@layer A {.target {color:red}}",
                "layer-B-green.css"=>"@layer B {.target {color:green}}", "layer-B-red.css"=>"@layer B {.target {color:red}}",
                _=>throw new FileNotFoundException(file),
            };
            return ValueTask.FromResult(new CssResource(uri,new MemoryStream(Encoding.UTF8.GetBytes(css))));
        }
    }

    public static IEnumerable<object[]> LayerCases => new[]
    {
        new object[]{"A1","@import url(basic-green.css); @layer {.target{color:red}}"},
        new object[]{"A2","@import url(layer-red.css); .target{color:green}"},
        new object[]{"A3","@import url(basic-green.css); @import url(layer-red.css);"},
        new object[]{"A4","@import url(layer-A-red.css); @layer B {.target{color:green}} @layer A {.target{color:red}}"},
        new object[]{"B1","@import url(basic-red.css) layer; .target{color:green}"},
        new object[]{"B2","@import url(basic-red.css) layer; @import url(basic-green.css) layer;"},
        new object[]{"B3","@import url(basic-red.css) layer; @layer {.target{color:green}}"},
        new object[]{"B4","@import url(layer-red.css); @import url(basic-green.css) layer;"},
        new object[]{"C1","@import url(basic-red.css) layer(A); .target{color:green}"},
        new object[]{"C2","@import url(basic-red.css) layer(A); @import url(basic-green.css) layer(A);"},
        new object[]{"C3","@import url(basic-red.css) layer(A); @layer A {.target{color:green}}"},
        new object[]{"C4","@import url(layer-red.css) layer(A); @layer A {.target{color:green}}"},
        new object[]{"C5","@import url(layer-A-red.css) layer(A); @layer A.A {.target{color:green}}"},
        new object[]{"C6","@import url(layer-A-red.css) layer(A); @layer B {.target{color:green}} @layer A.B {.target{color:red}}"},
        new object[]{"C7","@import url(basic-green.css) layer(A); @import url(basic-red.css) layer(B); @import url(basic-green.css) layer(C);"},
        new object[]{"C8","@import url(basic-red.css) layer(A); @import url(basic-green.css) layer(B); @import url(basic-red.css) layer(A);"},
        new object[]{"C9","@import url(basic-red.css) layer(A); @import url(basic-red.css) layer(B.A); @import url(basic-green.css) layer(B);"},
        new object[]{"D1","@import url(basic-red.css) layer(A); @import url(basic-green.css) layer(B); @layer B,A;"},
        new object[]{"D2","@layer B; @import url(basic-green.css) layer(A); @layer B {.target{color:red}}"},
        new object[]{"D3","@layer B; @import url(basic-green.css) layer(A); @import url(basic-red.css) layer(B);"},
        new object[]{"D4","@layer C,B,A; @import url(basic-green.css) layer(A); @import url(basic-red.css) layer(B); @layer C {.target{color:red}}"},
        new object[]{"D5","@layer A.B,A.A; @import url(basic-green.css) layer(A.A); @import url(layer-B-red.css) layer(A);"},
        new object[]{"D6","@layer B,A; @import url(layer-A-red.css) layer(A); @import url(layer-A-red.css) layer(B); @layer A.B {.target{color:green}}"},
        new object[]{"E1","@import 'nonexist.css' layer(A); @layer B {.target{color:green}} @layer A {.target{color:red}}"},
    };

    [Theory]
    [Trait("WPT","css/css-cascade/layer-import.html")]
    [MemberData(nameof(LayerCases))]
    public async Task LayerImports_PreserveAllTwentyFourUpstreamWinners(string identifier,string css)
    {
        var sheet=await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),new Resources(css));
        var target=Target(); Css.GetStyleSheets(target).Add(sheet); Layout(target);
        Assert.Equal(Colors.Green,ColorOf(target));
        Assert.Equal(Pixels(new Border{Width=40,Height=40,Background=Brushes.Green}),Pixels(target));
        Assert.False(string.IsNullOrEmpty(identifier));
    }

    [Theory]
    [Trait("WPT","css/css-cascade/import-conditions.html")]
    [InlineData("supports(display:block)",true)]
    [InlineData("supports((display:flex))",true)]
    [InlineData("supports((display:block) and (display:flex))",true)]
    [InlineData("supports((display:block) and (foo:bar))",false)]
    [InlineData("supports((display:block) or (display:flex))",true)]
    [InlineData("supports((display:block) or (foo:bar))",true)]
    [InlineData("supports(not (display:flex))",false)]
    [InlineData("supports(display:block!important)",true)]
    [InlineData("supports(foo:bar)",false)]
    [InlineData("supports(supports(display:block))",false)]
    [InlineData("supports(())",false)]
    [InlineData("supports()",false)]
    [InlineData("supports(display:block) (width >= 0px)",true)]
    [InlineData("(width >= 0px) supports(foo:bar)",false)]
    [InlineData("(width >= 0px) supports(display:block)",false)]
    [InlineData("supports(selector(a))",true)]
    [InlineData("supports(selector(p a))",true)]
    [InlineData("supports(selector(p > a))",true)]
    [InlineData("supports(selector(p + a))",true)]
    [InlineData("supports(font-tech(invalid))",false)]
    [InlineData("supports(font-format(invalid))",false)]
    [InlineData("supports(at-rule(@import))",true)]
    [InlineData("supports(at-rule(@media) or at-rule(@doesnotexist))",true)]
    [InlineData("supports(at-rule(@doesnotexist))",false)]
    [InlineData("layer supports(selector(a))",true)]
    public async Task ImportConditions_PreserveSupportedUpstreamCases(string condition,bool matches)
    {
        var resources=new Resources("@import 'basic-green.css' "+condition+";");
        var sheet=await CssStyleSheet.LoadAsync(new Uri("https://example.test/main.css"),resources);
        var target=Target(); Css.GetStyleSheets(target).Add(CssStyleSheet.Parse("@layer {.target{color:red}}"));
        Css.GetStyleSheets(target).Add(sheet); Layout(target);
        Assert.Equal(matches?Colors.Green:Colors.Red,ColorOf(target));
        if(condition.StartsWith("supports(",StringComparison.Ordinal) && !matches) Assert.Equal(1,resources.Requests);
    }

    [Theory]
    [Trait("WPT","css/css-cascade/layer-property-override.html")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LayeredPropertyRegistrations_PreserveAllFourUpstreamWinners(int index)
    {
        const string green="@property --foo {syntax:'<color>';inherits:false;initial-value:green}";
        const string red="@property --foo {syntax:'<color>';inherits:false;initial-value:red}";
        var root=new Border{Width=100,Height=100}; Css.SetStyle(root,"background:var(--foo)");
        var initial=index switch
        {
            0=>green+" @layer {"+red+"}",
            1=>"@layer base, override; @layer override {"+green+"} @layer base {"+red+"}",
            2=>"@layer base, override; @layer override {"+green+"}",
            _=>"@layer base, override; @layer base {"+red+"}",
        };
        Css.SetStyleSheet(root,initial); Layout(root);
        if(index>=2) Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(index==2?"@layer base {"+red+"}":"@layer override {"+green+"}"));
        Layout(root); Assert.Equal(Colors.Green,Assert.IsType<SolidColorBrush>(root.Background).Color);
    }

    private static Border Target()
    {
        var target=new Border{Width=40,Height=40}; Css.SetClass(target,"target"); Css.SetStyle(target,"background:currentcolor"); return target;
    }
    private static Color ColorOf(Border target)
        => Assert.IsType<SolidColorBrush>(target.GetValue(CssDependencyPropertyLookup.Find(target.GetType(),"Foreground")!)).Color;
    private static void Layout(FrameworkElement target)
    {target.Measure(new(400,200)); target.Arrange(new(0,0,400,200)); target.UpdateLayout();}
    private static byte[] Pixels(Border target)
    {
        target.Measure(new(40,40)); target.Arrange(new(0,0,40,40)); target.UpdateLayout();
        var bitmap=new RenderTargetBitmap(40,40,96,96,PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(target);
        var pixels=new byte[40*40*4]; bitmap.CopyPixels(new(0,0,40,40),pixels,160,0); return pixels;
    }
}
