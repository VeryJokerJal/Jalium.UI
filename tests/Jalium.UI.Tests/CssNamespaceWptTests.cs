using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssNamespaceWptTests
{
    static CssNamespaceWptTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(TypeConverterRegistry).Module.ModuleHandle);

    [Fact]
    [Trait("WPT","css/css-namespaces/prefix-001.xml")]
    public void PrefixCaseSensitivity_ProducesTheLimeReference()
    {
        var target=Named("y","test");
        Css.SetStyleSheet(target,"@namespace Foo 'y'; @namespace foo 'x'; test{background:red} Foo|test{background:lime} foo|test{background:red} FOO|test{background:red}");
        CompareLime(target);
    }

    [Fact]
    [Trait("WPT","css/css-namespaces/prefix-002.xml")]
    public void EmptyNamespacePrefix_MatchesUnnamespacedElement()
    {
        var target=Named("","t");
        Css.SetStyleSheet(target,"@namespace foo ''; t{background:red} foo|t{background:lime}");
        CompareLime(target);
    }

    [Fact]
    [Trait("WPT","css/css-namespaces/scope-001.xml")]
    public void NamespacePrefixes_AreNotSharedAcrossSheets()
    {
        var target=Named("test","test");
        Css.GetStyleSheets(target).Add(CssStyleSheet.Parse("@namespace x url('test'); test{background:lime}"));
        Css.GetStyleSheets(target).Add(CssStyleSheet.Parse("x|test{background:red}"));
        CompareLime(target);
    }

    [Fact]
    [Trait("WPT","css/css-conditional/at-supports-namespace-002.html")]
    public void SupportsNamespaceValidity_PaintsTheOriginalGreenSquare()
    {
        var root=new StackPanel();
        for(var i=1;i<=4;i++)
        {
            var box=new Border {Width=100,Height=25}; Css.SetClass(box,"test"+i); root.Children.Add(box);
        }
        Css.SetStyleSheet(root,"""
            @namespace x "http://www.w3.org/1999/xlink";
            @supports (background:green) {}
            @namespace y "http://www.w3.org/1999/xlink";
            .test1 {background:red}
            @supports selector(x|y) {.test1 {background:green}}
            .test2 {background:green}
            @supports selector(y|x) {.test2 {background:red}}
            .test3 {background:red}
            @supports not selector(y|x) {.test3 {background:green}}
            .test4, x|y {background:green}
            """);
        var reference=new Border {Width=100,Height=100,Background=Brushes.Green};
        Layout(root,100,100); Layout(reference,100,100);
        Assert.Equal(Pixels(reference,100,100),Pixels(root,100,100));
    }

    [Fact]
    [Trait("WPT","css/selectors/nth-of-type-namespace.html")]
    public void NthOfType_CountsEachNamespaceSeparately()
    {
        var root=new StackPanel(); var targets=new List<Border>();
        foreach(var ns in new[]{"http://www.w3.org/1999/xhtml","http://dummy1/","http://dummy2/"})
        {
            for(var i=0;i<100;i++)
            {
                var node=Named(ns,"span"); root.Children.Add(node);
                if(i==99) {Css.SetAttribute(node,"test-span",string.Empty); targets.Add(node);}
            }
        }
        Css.SetStyleSheet(root,"[test-span]:nth-of-type(100){color:green}"); Layout(root,100,6000);
        foreach(var target in targets)
        {
            var foreground=CssDependencyPropertyLookup.Find(target.GetType(),"Foreground")!;
            Assert.Equal(Colors.Green,Assert.IsType<SolidColorBrush>(target.GetValue(foreground)).Color);
        }
    }

    private static Border Named(string ns,string name)
    {
        var target=new Border {Width=40,Height=20}; XamlBuilder.SetXmlIdentity(target,ns,name); return target;
    }
    private static void CompareLime(Border target)
    {
        var reference=new Border {Width=40,Height=20,Background=Brushes.Lime};
        Layout(target,40,20); Layout(reference,40,20);
        Assert.Equal(Colors.Lime,Assert.IsType<SolidColorBrush>(target.Background).Color);
        Assert.Equal(Pixels(reference,40,20),Pixels(target,40,20));
    }
    private static void Layout(FrameworkElement root,int width,int height)
    {root.Measure(new(width,height)); root.Arrange(new(0,0,width,height)); root.UpdateLayout();}
    private static byte[] Pixels(Visual root,int width,int height)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(root);
        var pixels=new byte[width*height*4]; bitmap.CopyPixels(new(0,0,width,height),pixels,width*4,0); return pixels;
    }
}
