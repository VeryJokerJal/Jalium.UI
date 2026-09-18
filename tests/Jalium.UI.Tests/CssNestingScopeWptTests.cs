using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssNestingScopeWptTests
{
    static CssNestingScopeWptTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Fact]
    [Trait("WPT","css/css-nesting/nesting-basic.html")]
    public void BasicNesting_PaintsTheTwelveSupportedGreenSquares()
    {
        var root = new StackPanel { Spacing = 8 };
        var body = new StackPanel { Spacing = 8 }; root.Children.Add(body);
        StackPanel Box(string classes, bool child = true)
        {
            var box = new StackPanel(); Css.SetClass(box,"test " + classes); body.Children.Add(box);
            if (child) box.Children.Add(new StackPanel());
            return box;
        }
        Box("test-1"); Box("test-2"); var third = Box("test-3"); Css.SetClass(third.Children[0],"test-3-child");
        var fourth = Box("test-4",false);
        var bold = new Border(); var span = new Border { Child=bold }; var section = new Border { Child=span }; fourth.Children.Add(section);
        Css.SetClass(bold,"native-b"); Css.SetClass(span,"native-span"); Css.SetClass(section,"native-section");
        Box("test-6"); var seventh = Box("t7- t7--"); Css.SetClass(seventh.Children[0],"test-7-child");
        Box("test-8"); Box("test-9 t9-- t9-"); Box("test-10"); Box("test-11"); Box("test-12",false); Box("test-14",false);
        Css.SetStyleSheet(root,"""
            .test {background-color:red; width:30px; height:30px; display:grid}
            .test-1 { & > StackPanel {background-color:green} }
            .test-2 { & > StackPanel {background-color:green} }
            .test-3 { & .test-3-child {background-color:green} }
            .native-span > .native-b {
              .test-4 .native-section & {display:inline-block; background-color:green; width:100%; height:100%}
              .test-4 .native-section > & {background-color:red}
            }
            .test-6 { &.test {background-color:green} }
            .test-7, .t7- { & + .test-7-child, &.t7-- {background-color:green} }
            .test-8 { & {background-color:green} }
            .test-9 { &:is(.t9-, &.t9--) {background-color:green} }
            .test-10 { & {background-color:red} background-color:green }
            .test-11 { & {background-color:red} background-color:green!important }
            & .test-12 {background-color:green}
            & > .test-12 {background-color:red!important}
            StackPanel.test-14 {StackPanel& {background-color:green}}
            """);
        const int width=30, height=448;
        Layout(root,width,height);
        var reference = new Canvas();
        for (var i=0;i<12;i++)
        {
            var box = new Border { Width=30,Height=30,Background=Brushes.Green };
            Canvas.SetLeft(box,0); Canvas.SetTop(box,i*38); reference.Children.Add(box);
        }
        Layout(reference,width,height); Assert.Equal(Pixels(reference,width,height),Pixels(root,width,height));
    }

    [Fact]
    [Trait("WPT","css/css-cascade/scope-proximity.html")]
    public void ScopeProximity_PreservesAllFiveUpstreamComparisonCases()
    {
        var items = new List<Border>(); var root = new Border(); var current = root;
        foreach (var name in new[] {"light","dark","light","dark"})
        {
            var scope = new Border(); current.Child=scope; Css.SetClass(scope,name);
            var item = new Border {Name="item" + (items.Count+1)}; scope.Child=item; items.Add(item); current=item;
        }
        Css.SetStyleSheet(root,"@scope (.light) {[id] {border-color:rgb(100,100,100)}} @scope (.dark) {[id] {border-color:rgb(200,200,200)}}");
        Layout(root); for(var i=0;i<4;i++) Assert.Equal(Color.FromRgb((byte)(i%2==0?100:200),(byte)(i%2==0?100:200),(byte)(i%2==0?100:200)),ColorOf(items[i].BorderBrush));

        var itemTarget = new Border {Name="item"}; var inner = new Border {Child=itemTarget}; var outer = new Border {Child=inner};
        Css.SetClass(outer,"a"); Css.SetClass(inner,"b");
        Css.SetStyleSheet(outer,"@scope (.b) {[id] {border-color:green}} @scope (.a) {[id] {border-color:red}}");
        Layout(outer); Assert.Equal(Colors.Green,ColorOf(itemTarget.BorderBrush));
        Css.SetStyleSheet(outer,"@scope (.a) {Border[id] {border-color:green}} @scope (.b) {[id] {border-color:red}}");
        Layout(outer); Assert.Equal(Colors.Green,ColorOf(itemTarget.BorderBrush));
        Css.SetClass(outer,"foo"); Css.SetClass(inner,"foo bar");
        Css.SetStyleSheet(outer,"@scope (.foo) {.bar Border[id] {border-color:green}}");
        Layout(outer); Assert.Equal(Colors.Green,ColorOf(itemTarget.BorderBrush));
        outer.Name="outer"; itemTarget.Name="inner"; Css.SetClass(outer,"scope"); Css.SetClass(inner,""); Css.SetClass(itemTarget,"scope");
        Css.SetStyleSheet(outer,"@scope (.scope) {:where(&) {border-color:green}} @scope (#outer) {:where(:scope) :where(#inner) {border-color:red}}");
        Layout(outer); Assert.Equal(Colors.Green,ColorOf(itemTarget.BorderBrush));
    }

    [Fact]
    [Trait("WPT","css/css-cascade/scope-declarations.html")]
    public void DirectScopeDeclarations_HaveZeroSpecificityAndPreserveOrder()
    {
        var root = new StackPanel();
        var a = new Border(); var b = new Border(); var c = new Border();
        root.Children.Add(a); root.Children.Add(b); root.Children.Add(c);
        Css.SetClass(a,"a"); Css.SetClass(b,"b"); Css.SetClass(c,"c");
        Css.SetStyleSheet(root,"""
            @scope (.a) {:where(:scope) {z-index:1} z-index:2}
            @scope (.b) {z-index:1; :where(:scope) {z-index:2}}
            @scope (.c) {:scope {z-index:1} z-index:2}
            """);
        Layout(root); Assert.Equal(2,Panel.GetZIndex(a)); Assert.Equal(2,Panel.GetZIndex(b)); Assert.Equal(1,Panel.GetZIndex(c));
        Css.SetStyleSheet(a,"@scope {z-index:5}"); Layout(root); Assert.Equal(5,Panel.GetZIndex(a));
    }

    private static Color ColorOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void Layout(FrameworkElement element,int width=300,int height=200)
    { element.Measure(new(width,height)); element.Arrange(new(0,0,width,height)); element.UpdateLayout(); }
    private static byte[] Pixels(Visual visual,int width,int height)
    {
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormat.Bgra32); bitmap.Clear(Colors.White); bitmap.Render(visual);
        var pixels = new byte[width*height*4]; bitmap.CopyPixels(new(0,0,width,height),pixels,width*4,0); return pixels;
    }
}
