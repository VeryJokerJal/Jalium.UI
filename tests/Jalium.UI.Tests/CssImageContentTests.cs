using System.Text;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssImageContentTests
{
    [Theory]
    [InlineData("url('icon.png')")]
    [InlineData("'前' url('icon.png') '后'")]
    [InlineData("counter(n) url('icon.png') close-quote")]
    [InlineData("linear-gradient(green,green) / '标识'")]
    [InlineData("image-set('low.png' 1x,'high.png' 2x) / ''")]
    public void ContentParsesImagesAlongsideTextCountersQuotesAndAlternatives(string css)
    {
        var element=new StackPanel();Add(element,"StackPanel::before{content:"+css+";}",new());Layout(element);
        var value=Assert.IsType<CssGeneratedContentValue>(Before(element).GetValue(CssGeneratedContent.ContentProperty));
        Assert.Contains(value.Sequence!.Parts,p=>p.Kind==CssContentKind.Image);Assert.False(value.IsNone);
    }

    [Theory]
    [InlineData("url('icon.png') / url('alt.png')")]
    [InlineData("url('icon.png'),url('fallback.png')")]
    [InlineData("image-set()")]
    [InlineData("url(bad url)")]
    public void InvalidImageContentDoesNotReplaceAnEarlierValidDeclaration(string css)
    {var e=new StackPanel();Add(e,"StackPanel::before{content:'保留';content:"+css+";}",new(),allowDiagnostics:true);Layout(e);Assert.Equal("保留",Before(e).Text);}

    [Fact]
    public void AReplacedPseudoUsesNaturalSizeThenAuthoredSizeRatioAndObjectFit()
    {
        var resolver=new Images();resolver.Add("icon.svg",Svg(40,20));var root=new StackPanel();Add(root,"StackPanel::before{content:url('icon.svg') / '品牌';}",resolver);Layout(root);PumpUntil(()=>Before(root).HasImageContent&&Image(Before(root)).Valid);Layout(root);
        var before=Before(root);Assert.Equal(40,before.DesiredSize.Width,3);Assert.Equal(20,before.DesiredSize.Height,3);Assert.Equal("品牌",before.GetAutomationPeer()!.GetName());
        Add(root,"StackPanel::before{width:100px;box-sizing:content-box;padding:5px;border:2px solid green;object-fit:contain;}",resolver);Layout(root);Assert.Equal(114,before.DesiredSize.Width,3);Assert.Equal(64,before.DesiredSize.Height,3);Assert.Equal(100,Image(before).ActualWidth,3);Assert.Equal(50,Image(before).ActualHeight,3);
    }

    [Fact]
    public void MixedPseudoContentFragmentsAcrossLinesAndKeepsOriginalRunSelectionAndHits()
    {
        var resolver=new Images();resolver.Add("icon.svg",Svg(24,16));var root=new TextBlock{IsTextSelectionEnabled=true};var run=new Run("原始正文仍然属于原来的文本控件，图片加载和片段换行不会改写原始 Run。");root.Inlines.Add(run);root.Tag="章";
        Add(root,"TextBlock{display:block;font-size:16px;line-height:24px;white-space:normal;counter-reset:n 7;}TextBlock::before{content:'开始 ' counter(n) url('icon.svg') attr(Tag) ' 这一段生成内容需要跨过多行继续排版。' / '章节' counter(n);color:green;}",resolver);
        Layout(root,180);PumpUntil(()=>Before(root).HasImageContent&&Image(Before(root)).Valid);Layout(root,180);root.Select(2,6);var before=Before(root);
        Assert.Contains("开始 7章",before.Text);Assert.Equal("章节7",before.Alternative);Assert.True(before.IsCssInlineFrame);Assert.True(before.CssInlineBox!.Fragments.Length>1);Assert.Same(run,root.Inlines[0]);Assert.Single(root.Inlines);Assert.Equal(run.Text.Substring(2,6),root.SelectedText);Assert.Empty(CssNode.Get(before).EnumerateChildren());
        var image=Image(before);var offset=image.TransformToAncestor(root);Assert.Same(root,root.InputHitTest(new(offset.X+image.ActualWidth/2,offset.Y+image.ActualHeight/2)));
        var following=Enumerable.Range(0,VisualTreeHelper.GetChildrenCount(before)).Select(i=>VisualTreeHelper.GetChild(before,i)).OfType<TextBlock>().Last();
        var firstGlyph=following.GetSourceRangeSegments(0,1).Single();var followingOffset=following.TransformToAncestor(root);
        Assert.True(followingOffset.X+firstGlyph.StartX>=offset.X+image.ActualWidth-.01,"Text following the image lost its first-line offset after wrapping");
        root.Measure(new(0,1000));root.Measure(new(double.PositiveInfinity,1000));Layout(root,260);Layout(root,180);Assert.Same(run,root.Inlines[0]);Assert.Equal(run.Text.Substring(2,6),root.SelectedText);
    }

    [Theory]
    [InlineData("inside")] [InlineData("outside")]
    public void ListImagesInheritAndUseTheirNaturalSizeFollowedByASpace(string position)
    {
        var resolver=new Images();resolver.Add("bullet.svg",Svg(20,10));var (root,rows)=List();Add(root,$".list{{list-style:decimal {position} url('bullet.svg');padding-left:40px;}}.item{{display:list-item;}}",resolver);Layout(root);PumpUntil(()=>Marker(rows[0]).HasImageContent);Layout(root);
        Assert.Equal(" ",Marker(rows[0]).Text);Assert.Equal(20,Image(Marker(rows[0])).DesiredSize.Width,3);Assert.Equal(10,Image(Marker(rows[0])).DesiredSize.Height,3);Assert.True(Marker(rows[0]).DesiredSize.Width>20);Assert.Equal(3,rows.Length);Assert.All(rows,row=>Assert.Single(row.Children));Assert.Single(resolver.Requests);Assert.Equal(1,resolver.Disposed);
        var marker=Marker(rows[0]);var box=Image(marker);var at=box.TransformToAncestor(root);Assert.Same(rows[0],root.InputHitTest(new(at.X+10,at.Y+5)));
    }

    [Fact]
    public void PendingAndFailedListImagesFallBackToTheCounterStyleAndNoneCanRecover()
    {
        var resolver=new Images(gated:true);resolver.Add("bad.png",new byte[]{1,2,3});var(root,rows)=List();Add(root,".list{list-style:decimal url('bad.png');}.item{display:list-item;}",resolver);Layout(root);Assert.Equal("1. ",Marker(rows[0]).Text);resolver.Open();PumpUntil(()=>resolver.Disposed>0);Flush();Layout(root);Assert.Equal("1. ",Marker(rows[0]).Text);
        Css.SetStyle(root,"list-style-type:none;");Layout(root);Assert.Equal(Visibility.Collapsed,Marker(rows[0]).Visibility);
        Css.SetStyle(root,"list-style:circle;");Layout(root);Assert.Equal("◦ ",Marker(rows[0]).Text);Assert.Equal(Visibility.Visible,Marker(rows[0]).Visibility);
    }

    [Fact]
    public void AValidImageMarkerRemainsVisibleWithTypeNoneAndExplicitContentOverridesIt()
    {
        var resolver=new Images();resolver.Add("bullet.svg",Svg(20,10));var(root,rows)=List();Add(root,".list{list-style:none url('bullet.svg');}.item{display:list-item;}",resolver);Layout(root);PumpUntil(()=>Marker(rows[0]).HasImageContent);Layout(root);Assert.Equal(Visibility.Visible,Marker(rows[0]).Visibility);
        Add(root,".item::marker{content:'步骤' counter(list-item) ' ';}",resolver);Layout(root);Assert.False(Marker(rows[0]).HasImageContent);Assert.Equal("步骤1 ",Marker(rows[0]).Text);
        Add(root,".item::marker{content:normal;}",resolver);Layout(root);Assert.True(Marker(rows[0]).HasImageContent);
    }

    [Theory]
    [InlineData("none disc url('icon.png')")]
    [InlineData("url('a') url('b')")]
    [InlineData("inside outside")]
    public void AmbiguousOrRepeatedListShorthandComponentsAreRejected(string shorthand)
    {var(root,rows)=List();Add(root,".item{display:list-item;list-style:decimal;list-style:"+shorthand+";}",new());Layout(root);Assert.Equal("1. ",Marker(rows[0]).Text);}

    [Fact]
    public void GradientMarkersUseOneEmAndReactToMarkerFontAndCurrentColor()
    {
        var(root,rows)=List();Add(root,".item{display:list-item;list-style-type:none;list-style-image:linear-gradient(currentcolor,currentcolor);color:green;}.item::marker{font-size:20px;}",new());Layout(root);Assert.Equal(20,Image(Marker(rows[0])).DesiredSize.Width,3);Assert.Equal(20,Image(Marker(rows[0])).DesiredSize.Height,3);
        var color=Sample(Image(Marker(rows[0])));Assert.Equal(Colors.Green,color);
        Add(root,".item::marker{font-size:24px;color:blue;}",new());Layout(root);Assert.Equal(24,Image(Marker(rows[0])).DesiredSize.Width,3);Assert.Equal(Colors.Blue,Sample(Image(Marker(rows[0]))));
    }

    [Fact]
    public void OrdinaryElementReplacementSuppressesOriginalBoxesWithoutChangingTextChildrenOrSelection()
    {
        var resolver=new Images();resolver.Add("logo.svg",Svg(40,20));var owner=new TextBlock{Text="原来的正文",IsTextSelectionEnabled=true};owner.Select(1,3);var original=owner.Inlines[0];var root=new StackPanel();root.Children.Add(owner);Css.SetClass(owner,"replace");Add(root,".replace{content:url('logo.svg') / '品牌图';width:100px;}",resolver);Layout(root);PumpUntil(()=>CssReplacedContent.Image(owner)?.Valid==true);Layout(root);
        Assert.True(CssReplacedContent.IsActive(owner));Assert.Equal("原来的正文",owner.Text);Assert.Same(original,owner.Inlines[0]);Assert.Equal("来的正",owner.SelectedText);Assert.Equal("品牌图",owner.GetAutomationPeer()!.GetName());Assert.Equal(50,owner.DesiredSize.Height,3);Assert.Single(root.Children);Assert.Equal(Colors.Green,Sample(owner));
        Css.SetStyle(owner,"content:normal;");Layout(root);Assert.False(CssReplacedContent.IsActive(owner));Assert.Same(original,owner.Inlines[0]);Assert.Equal("来的正",owner.SelectedText);Assert.Equal("原来的正文",owner.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void AReplacedContainerKeepsTheAuthoredTreeButHidesDescendantsFromInputAndCounters()
    {
        var child=new TextBlock{Text="原始子级",Focusable=true};var owner=new StackPanel();owner.Children.Add(child);var tail=new StackPanel();var root=new StackPanel();root.Children.Add(owner);root.Children.Add(tail);Css.SetClass(owner,"replace");Css.SetClass(child,"child");Css.SetClass(tail,"tail");
        Css.SetStyle(root,"counter-reset:n 0;");Add(root,".replace{content:linear-gradient(green,green);width:80px;height:30px;}.child{counter-increment:n 9;}.tail::before{content:counter(n);}",new());Layout(root);Assert.Same(child,owner.Children[0]);Assert.Same(owner,VisualTreeHelper.GetParent(child));Assert.False(child.IsVisible);Assert.Equal("0",Before(tail).Text);var at=owner.TransformToAncestor(root);Assert.Same(owner,root.InputHitTest(new(at.X+20,at.Y+15)));
        Css.SetStyle(owner,"content:normal;");Layout(root);Assert.True(child.IsVisible);Assert.Same(child,owner.Children[0]);Assert.Equal("9",Before(tail).Text);
    }

    [Fact]
    public void NonImageContentOnAnOrdinaryElementDoesNotRewriteItsNativeContent()
    {var owner=new TextBlock{Text="正文"};Css.SetStyle(owner,"content:'不替换';");Layout(owner);Assert.False(CssReplacedContent.IsActive(owner));Assert.Equal("正文",owner.Text);}

    [Theory]
    [InlineData("",40,20)]
    [InlineData("max-width:20px;",20,10)]
    [InlineData("min-width:80px;",80,40)]
    [InlineData("max-height:10px;",20,10)]
    [InlineData("min-height:40px;",80,40)]
    [InlineData("max-width:20px;max-height:15px;",20,10)]
    [InlineData("max-width:30px;max-height:10px;",20,10)]
    [InlineData("min-width:60px;min-height:40px;",80,40)]
    [InlineData("min-width:80px;min-height:30px;",80,40)]
    [InlineData("min-width:80px;max-height:30px;",80,30)]
    [InlineData("max-width:20px;min-height:30px;",20,30)]
    [InlineData("width:100px;max-width:80px;",80,40)]
    [InlineData("height:50px;max-height:30px;",60,30)]
    [InlineData("width:100px;max-height:20px;",100,20)]
    [InlineData("height:50px;max-width:60px;",60,50)]
    [InlineData("aspect-ratio:4;",40,10)]
    [InlineData("width:100px;aspect-ratio:4;padding:5px;border:2px solid green;",100,25)]
    [InlineData("height:50px;aspect-ratio:4;padding:5px;border:2px solid green;",200,50)]
    [InlineData("min-width:100px;aspect-ratio:2;padding:5px;border:2px solid green;",100,50)]
    public void ReplacedImagesResolveAutoAxesAndConflictingMinMaxConstraints(string sizing,double width,double height)
    {
        foreach(var pseudo in new[]{false,true})
        {
            var resolver=new Images();resolver.Add("icon.svg",Svg(40,20));
            var root=new StackPanel();var owner=new TextBlock{Text="源文本"};root.Children.Add(owner);
            Add(root,$"TextBlock{(pseudo?"::before":"")}{{content:url('icon.svg');{sizing}}}",resolver);
            Layout(root);var box=pseudo?Before(owner):owner;
            PumpUntil(()=>Image(box).Valid);Layout(root);
            Assert.Equal(width,box.DesiredSize.Width,3);Assert.Equal(height,box.DesiredSize.Height,3);
            Assert.Equal(width,box.ActualWidth,3);Assert.Equal(height,box.ActualHeight,3);
        }
    }

    [Fact]
    public void ContentBoxRatioAndObjectPositionUseTheImageAreaAndRearrangeOnStyleChanges()
    {
        var resolver=new Images();resolver.Add("icon.svg",Svg(40,20));var root=new StackPanel();
        Add(root,"StackPanel::before{content:url('icon.svg');box-sizing:content-box;width:100px;height:100px;padding:5px;border:2px solid green;object-fit:contain;object-position:left top;}",resolver);
        Layout(root);PumpUntil(()=>Image(Before(root)).Valid);Layout(root);var before=Before(root);var image=Image(before);
        Assert.Equal(new Size(114,114),before.DesiredSize);Assert.Equal(new Rect(7,7,100,50),image.VisualBounds);
        Add(root,"StackPanel::before{object-position:right bottom;}",resolver);Layout(root);
        Assert.Equal(new Rect(7,57,100,50),image.VisualBounds);
        Add(root,"StackPanel::before{height:auto;aspect-ratio:4;}",resolver);Layout(root);
        Assert.Equal(new Size(114,39),before.DesiredSize);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DpiAndDynamicAlternativesReselectContentImagesWithoutRefetchingEquivalentValues(bool raster)
    {
        var resolver=new Images();resolver.Add("low.img",raster?Png(40,20):Svg(40,20));resolver.Add("high.img",raster?Png(80,40):Svg(80,40));
        var root=new StackPanel();var owner=new TextBlock{Text="源文本",Tag="第一版"};root.Children.Add(owner);Css.SetClass(owner,"replace");
        Add(root,".replace{content:image-set('low.img' 1x,'high.img' 2x) / attr(Tag);}",resolver);
        Layout(root);PumpUntil(()=>Image(owner).Valid);Layout(root);
        Assert.Equal(new Size(40,20),owner.DesiredSize);Assert.Equal("第一版",owner.GetAutomationPeer()!.GetName());
        owner.Tag="第二版";Layout(root);Assert.Equal("第二版",owner.GetAutomationPeer()!.GetName());Assert.Single(resolver.Requests);
        VisualTreeHelper.SetRootDpi(root,new(2,2));Layout(root);PumpUntil(()=>resolver.Requests.Count==2&&Image(owner).Valid);Layout(root);
        Assert.Equal(raster?new Size(40,20):new Size(80,40),owner.DesiredSize);Assert.Equal(new[]{"low.img","high.img"},resolver.Requests);
        VisualTreeHelper.SetRootDpi(root,new(1,1));Layout(root);Assert.Equal(new Size(40,20),owner.DesiredSize);Assert.Equal(2,resolver.Disposed);Assert.Equal(2,resolver.Requests.Count);
    }

    [Fact]
    public void RetiredContentImagesCannotResurrectOrReplaceANewerImageWhenLoadingFinishes()
    {
        var resolver=new Images(gated:true);resolver.Add("old.svg",Svg(40,20));var root=new StackPanel();
        Add(root,"StackPanel{content:url('old.svg');}StackPanel::before{content:'前' url('old.svg');}",resolver);Layout(root);
        var resource=Image(root).ActiveResource!;
        Css.SetStyle(root,"content:linear-gradient(blue,blue);width:50px;height:20px;");Layout(root);
        resolver.Open();PumpUntil(()=>resource.Ready.IsCompleted);Layout(root);
        Assert.Equal(Colors.Blue,Sample(root));Assert.Equal(Visibility.Collapsed,Before(root).Visibility);
        Css.SetStyle(root,"content:normal;");Layout(root);PumpUntil(()=>Image(Before(root)).Valid);Layout(root);
        Add(root,"StackPanel::before{content:'已恢复';}",resolver);Layout(root);Assert.False(Before(root).HasImageContent);Assert.Equal("已恢复",Before(root).Text);
    }

    [Fact]
    public void ReplacementRefreshesCachedAutomationChildrenAndRestoresTheirOriginalIdentity()
    {
        var child=new TextBlock{Text="子级名称"};var root=new StackPanel();root.Children.Add(child);Layout(root);
        var peer=root.GetAutomationPeer()!;var childPeer=Assert.Single(peer.GetChildren());Assert.Equal("子级名称",childPeer.GetName());
        Css.SetStyle(root,"content:linear-gradient(green,green) / '容器图片';width:80px;height:30px;");Layout(root);
        Assert.Empty(peer.GetChildren());Assert.Equal("容器图片",peer.GetName());Assert.True(childPeer.IsOffscreen());
        Css.SetStyle(root,"content:normal;");Layout(root);Assert.Same(childPeer,Assert.Single(peer.GetChildren()));Assert.False(childPeer.IsOffscreen());
    }

    [Theory]
    [InlineData("Border")] [InlineData("ContentControl")] [InlineData("Grid")]
    public void ReplacementRetainsNativeContainerContentAndHidesItsAccessibleChildren(string kind)
    {
        var child=new TextBlock{Text="原来的内容"};
        FrameworkElement owner=kind switch
        {
            "Border"=>new Border{Child=child},
            "ContentControl"=>new ContentControl{Content=child},
            _=>new Grid(),
        };
        if(owner is Grid grid)grid.Children.Add(child);
        Layout(owner);var peer=owner.GetAutomationPeer()!;Assert.NotEmpty(peer.GetChildren());
        Css.SetStyle(owner,"content:linear-gradient(green,green) / '图片内容';width:80px;height:30px;border:2px solid blue;padding:3px;");Layout(owner);
        Assert.False(child.IsVisible);Assert.Empty(peer.GetChildren());Assert.Equal("图片内容",peer.GetName());Assert.Equal(Colors.Green,Sample(owner));
        Css.SetStyle(owner,"content:normal;");Layout(owner);Assert.True(child.IsVisible);Assert.NotEmpty(peer.GetChildren());
        Assert.Same(owner,VisualTreeHelper.GetParent(child));Assert.Equal("原来的内容",child.Text);
    }

    [Fact]
    public void ReplacedDescendantsLeaveKeyboardFocusAndReturnToTabOrderAfterRestoration()
    {
        Keyboard.Initialize();Keyboard.ClearFocus();
        var before=new TextBlock{Text="之前",Focusable=true};var child=new TextBlock{Text="内部",Focusable=true};var after=new TextBlock{Text="之后",Focusable=true};
        foreach(var text in new[]{before,child,after})KeyboardNavigation.SetIsTabStop(text,true);
        var owner=new StackPanel();owner.Children.Add(child);var root=new StackPanel();root.Children.Add(before);root.Children.Add(owner);root.Children.Add(after);
        var window=new DisplayedTestWindow{Content=root,Width=260,Height=500,TitleBarStyle=WindowTitleBarStyle.Native};
        try
        {
            Layout(window);Assert.True(child.Focus());
            Css.SetStyle(owner,"content:linear-gradient(green,green);width:80px;height:30px;");Layout(window);
            Assert.NotSame(child,Keyboard.FocusedElement);Assert.False(child.Focus());Assert.True(before.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(before));Assert.Same(after,Keyboard.FocusedElement);
            Css.SetStyle(owner,"content:normal;");Layout(window);Assert.True(before.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(before));Assert.Same(child,Keyboard.FocusedElement);
        }
        finally{Keyboard.ClearFocus();window.Close();}
    }

    [Fact]
    public void CurrentColorImagesObserveMutableInheritedForegroundBrushes()
    {
        var ink=new SolidColorBrush(Colors.Green);var root=new TextBlock{Text="原文",Foreground=ink};
        Css.SetStyle(root,"content:linear-gradient(currentcolor,currentcolor);width:60px;height:30px;");Layout(root);
        Assert.Equal(Colors.Green,Sample(root));ink.Color=Colors.Blue;Layout(root);Assert.Equal(Colors.Blue,Sample(root));
        Css.SetStyle(root,"content:normal;");Layout(root);ink.Color=Colors.Red;Layout(root);Assert.False(CssReplacedContent.IsActive(root));Assert.Equal("原文",root.Text);
    }

    [Fact]
    public void NativeRoundedCornersStillClipTheReplacementImage()
    {
        var owner=new Border{CornerRadius=new(20),Background=Brushes.Red};
        Css.SetStyle(owner,"content:linear-gradient(green,green);width:60px;height:60px;");Layout(owner);
        var bitmap=new RenderTargetBitmap(60,60,96,96,PixelFormats.Bgra32);var drawing=new SoftwareDrawingContext(bitmap);owner.Render(drawing);drawing.Close();var pixels=bitmap.GetPixelBuffer();
        Assert.Equal(0,pixels[3]);Assert.Equal(255,pixels[(30*60+30)*4+3]);Assert.Equal(Colors.Green,Sample(owner));
    }

    [Fact]
    public void AnimatedContentRequestsRepaintAndStopsNotifyingAfterItIsRetired()
    {
        var resolver=new Images();resolver.Add("animated.gif",Gif());var owner=new TextBlock{Text="原文"};
        Add(owner,"TextBlock{content:url('animated.gif');width:40px;height:10px;}",resolver);Layout(owner);PumpUntil(()=>Image(owner).Valid);
        var image=Image(owner);var brush=(ImageBrush)typeof(CssGeneratedImage).GetField("_brush",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(image)!;
        var bitmap=Assert.IsType<BitmapImage>(brush.ImageSource);var task=bitmap.PreloadAsync(40,10);PumpUntil(()=>task.IsCompleted);Assert.True(task.IsCompletedSuccessfully);
        PumpUntil(()=>bitmap.AnimatedSubstitute is{IsPlaying:true});var animation=Assert.IsType<AnimatedBitmap>(bitmap.AnimatedSubstitute);animation.Stop();
        try
        {
            Flush();owner.ClearRenderDirty();image.ClearRenderDirty();animation.CurrentFrameIndex=1;Flush();Assert.True(image.IsRenderDirty);Assert.True(owner.IsSubtreeDirty);
            Css.SetStyle(owner,"content:normal;");Layout(owner);owner.ClearRenderDirty();animation.CurrentFrameIndex=0;Flush();Assert.False(owner.IsRenderDirty);Assert.Equal("原文",owner.Text);
        }
        finally{animation.Stop();}
    }

    private static CssGeneratedText Before(FrameworkElement owner)=>CssGeneratedContent.Get(owner)!.Before!;
    private static CssGeneratedText Marker(FrameworkElement owner)=>CssGeneratedContent.Get(owner)!.Marker!;
    private static CssGeneratedImage Image(FrameworkElement owner)
    {for(var i=0;i<VisualTreeHelper.GetChildrenCount(owner);i++)if(VisualTreeHelper.GetChild(owner,i) is CssGeneratedImage image)return image;throw new Exception("No image formatting child");}
    private static Color Sample(FrameworkElement element)
    {var width=(int)Math.Max(1,Math.Ceiling(element.ActualWidth));var height=(int)Math.Max(1,Math.Ceiling(element.ActualHeight));var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Bgra32);var dc=new SoftwareDrawingContext(bitmap);element.Render(dc);dc.Close();var data=bitmap.GetPixelBuffer();var i=((height/2)*width+width/2)*4;return Color.FromArgb(data[i+3],data[i+2],data[i+1],data[i]);}
    private static (StackPanel Root,StackPanel[] Rows) List()
    {var root=new StackPanel();Css.SetClass(root,"list");var rows=Enumerable.Range(0,3).Select(i=>{var row=new StackPanel();Css.SetClass(row,"item");var body=new TextBlock{Text="列表正文"+i};Css.SetStyle(body,"display:inline;");row.Children.Add(body);root.Children.Add(row);return row;}).ToArray();return(root,rows);}
    private static void Add(FrameworkElement root,string css,Images resolver,bool allowDiagnostics=false)
    {var sheet=CssStyleSheet.Parse(css,"image-content",new Uri("file:///assets/"),resolver);if(!allowDiagnostics)Assert.Empty(sheet.Diagnostics);Css.GetStyleSheets(root).Add(sheet);Flush();}
    private static void Layout(FrameworkElement root,double width=260)
    {for(var i=0;i<6;i++){Flush();root.Measure(new(width,1000));root.Arrange(new(0,0,width,Math.Max(1,root.DesiredSize.Height)));root.UpdateLayout();}}
    private static void Flush(){Dispatcher.CurrentDispatcher.ProcessQueue();BitmapDecodeNotifier.FlushPendingForTests();CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);}
    private static void PumpUntil(Func<bool> condition)
    {var stop=DateTime.UtcNow.AddSeconds(10);while(!condition()&&DateTime.UtcNow<stop){Flush();Thread.Sleep(1);}Assert.True(condition(),"Image content timed out");Flush();}
    private static byte[] Svg(int width,int height)=>Encoding.UTF8.GetBytes($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'><rect width='{width}' height='{height}' fill='green'/></svg>");
    private static byte[] Png(int width,int height)
    {
        var pixels=new byte[width*height*4];for(var i=0;i<pixels.Length;i+=4){pixels[i+1]=128;pixels[i+3]=255;}
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4)));
        using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    private static byte[] Gif()
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});
        foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}}){writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);}writer.Write((byte)0x3b);return output.ToArray();
    }
    private sealed class Images(bool gated=false):ICssResourceResolver
    {
        internal readonly List<string> Requests=[];private readonly Dictionary<string,byte[]> _data=[];private readonly TaskCompletionSource _gate=new(TaskCreationOptions.RunContinuationsAsynchronously);internal int Disposed;
        internal void Add(string name,byte[] bytes)=>_data[name]=bytes;internal void Open()=>_gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri,CancellationToken cancellationToken=default)
        {var name=Path.GetFileName(uri.LocalPath);lock(Requests)Requests.Add(name);if(gated)await _gate.Task;return new(uri,new Closed(_data.GetValueOrDefault(name,new byte[]{1,2,3}),()=>Interlocked.Increment(ref Disposed)));}
    }
    private sealed class Closed(byte[] bytes,Action closed):MemoryStream(bytes,writable:false)
    {protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)closed();}}
}
