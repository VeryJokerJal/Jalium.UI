using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssCompositingPaintTests
{
    public static TheoryData<string,int,int,int,int> BrowserColors=>new()
    {
        {"normal",153,90,156,235},{"multiply",63,77,145,235},{"screen",158,158,214,235},
        {"overlay",79,120,204,235},{"darken",67,90,156,235},{"lighten",154,145,204,235},
        {"color-dodge",141,175,224,235},{"color-burn",46,55,186,235},{"hard-light",132,100,204,235},
        {"soft-light",86,129,204,235},{"difference",132,111,131,235},{"exclusion",141,137,151,235},
        {"hue",182,93,173,235},{"saturation",80,141,192,235},{"color",166,102,168,235},
        {"luminosity",55,133,191,235},{"plus-lighter",162,166,255,255},
    };
    [Theory] [MemberData(nameof(BrowserColors))]
    public void CssBlendGroupsMatchIndependentBrowserColors(string mode,int r,int g,int b,int a)
    {
        var root=new Canvas{Width=60,Height=40,Background=new SolidColorBrush(Color.FromArgb(193,39,164,219))};
        var source=new Border{Width=40,Height=30,Background=new SolidColorBrush(Color.FromArgb(171,197,62,133))};
        Css.SetStyle(source,"mix-blend-mode:"+mode);root.Children.Add(source);
        foreach(var recorded in new[]{false,true})Pixel(Render(root,recorded),10,10,r,g,b,a);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void IsolationStopsDescendantsBlendingWithTheParentBackdrop(bool recorded)
    {
        var root=new Canvas{Width=60,Height=40,Background=Brushes.Red};
        var group=new Canvas{Width=40,Height=30};var source=new Border{Width=40,Height=30,Background=Brushes.Blue};
        Css.SetStyle(source,"mix-blend-mode:multiply");group.Children.Add(source);root.Children.Add(group);
        Pixel(Render(root,recorded),10,10,0,0,0,255);
        Css.SetStyle(group,"isolation:isolate");Pixel(Render(root,recorded),10,10,0,0,255,255);
        Css.SetStyle(group,"");Pixel(Render(root,recorded),10,10,0,0,0,255);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void OpacityAndIdentityTransformsCreateIsolatedBlendingGroups(bool recorded)
    {
        var root=new Canvas{Width=60,Height=40,Background=Brushes.Red};
        var group=new Canvas{Width=40,Height=30};var source=new Border{Width=40,Height=30,Background=Brushes.Blue};
        Css.SetStyle(source,"mix-blend-mode:multiply");group.Children.Add(source);root.Children.Add(group);
        Css.SetStyle(group,"opacity:.5");Pixel(Render(root,recorded),10,10,128,0,128,255);
        Css.SetStyle(group,"transform:translate(0px)");Pixel(Render(root,recorded),10,10,0,0,255,255);
        Css.SetStyle(group,"transform:none");Pixel(Render(root,recorded),10,10,0,0,0,255);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReparentingABlendedChildInvalidatesIsolationDiscovery(bool recorded)
    {
        var root=new Canvas{Width=60,Height=40,Background=Brushes.Red};
        var left=new Canvas{Width=40,Height=30,Opacity=.5};var right=new Canvas{Width=40,Height=30,Opacity=.5};
        var source=new Border{Width=40,Height=30,Background=Brushes.Blue};Css.SetStyle(source,"mix-blend-mode:multiply");
        left.Children.Add(source);root.Children.Add(left);root.Children.Add(right);Pixel(Render(root,recorded),10,10,128,0,128,255);
        left.Children.Remove(source);right.Children.Add(source);Pixel(Render(root,recorded),10,10,128,0,128,255);
    }
    [Theory] [InlineData("mix-blend-mode")] [InlineData("background-blend-mode")] [InlineData("isolation")]
    public void CssAuthoringReportsActualCompositingProperties(string name)
        =>Assert.Equal(CssPropertySupport.Supported,CssAuthoring.GetProperty(name).Support);

    internal static byte[] Render(FrameworkElement root,bool recorded=false)
    {
        root.Measure(new(60,40));root.Arrange(new(0,0,60,40));root.UpdateLayout();
        var target=new RenderTargetBitmap(60,40,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(target);
        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();root.Render(recorder);DrawingReplayer.Replay(recorder.Commit(),context);}
        else root.Render(context);
        context.Close();return target.GetPixelBuffer();
    }
    internal static void Pixel(byte[] pixels,int x,int y,int r,int g,int b,int a)
    {
        var i=(y*60+x)*4;Assert.InRange(Math.Abs(pixels[i]-b),0,2);Assert.InRange(Math.Abs(pixels[i+1]-g),0,2);
        Assert.InRange(Math.Abs(pixels[i+2]-r),0,2);Assert.InRange(Math.Abs(pixels[i+3]-a),0,2);
    }
}
