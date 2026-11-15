using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssImageAnnotationTests
{
    [Theory]
    [InlineData("image('a.png')")]
    [InlineData("image(url(a.png))")]
    [InlineData("image('a.png',red)")]
    [InlineData("image(url('a.png'),currentcolor)")]
    [InlineData("image(ltr 'a.png')")]
    [InlineData("image(rtl url('a.png'),color-mix(in srgb,currentcolor,white))")]
    [InlineData("image(LTR 'a.png#xywh=1,2,3,4',transparent)")]
    [InlineData("image('a.png#xywh=percent:25,0,50,100',red)")]
    [InlineData("image('a.png#unknown',red)")]
    [InlineData("image(ltr red)")]
    [InlineData("image(rtl currentcolor)")]
    [InlineData("image-set(image(ltr 'a.png',red) 1x,image(rtl 'b.png',blue) 2x)")]
    [InlineData("cross-fade(image('a.png#xywh=0,0,8,8',red),image(rtl 'b.png',blue))")]
    public void AnnotationGrammarIsSharedByEveryCurrentImageConsumer(string value)
    {
        foreach (var property in new[] { "background-image", "content", "list-style-image", "shape-outside" })
            Assert.True(CssAuthoring.IsValueSupported(property, value), property + ": " + value);
    }

    [Theory]
    [InlineData("image()")]
    [InlineData("image(ltr)")]
    [InlineData("image(ltr,red)")]
    [InlineData("image(ltr rtl 'a')")]
    [InlineData("image(auto 'a')")]
    [InlineData("image(red,'a')")]
    [InlineData("image('a',)")]
    [InlineData("image('a' red)")]
    [InlineData("image('a','b')")]
    [InlineData("image('a',red,blue)")]
    [InlineData("image(linear-gradient(red,blue),red)")]
    [InlineData("image(image-set('a'),red)")]
    [InlineData("image(url(a b),red)")]
    public void InvalidAnnotationSyntaxRejectsTheDeclaration(string value)
    {
        var reader = new CssTokenReader(value);
        Assert.False(CssImageValue.TryRead(ref reader, CssCompileContext.Default, out _));
    }

    [Fact]
    public void LoadingFallbackHasNoNaturalDimensionsAndSwitchesAfterDecodeWithoutChangingSourceText()
    {
        var resolver = new Images(gated: true); resolver.Add("sprite.png", Sprite());
        var owner = new TextBlock { IsTextSelectionEnabled = true };
        var run = new Run("原始内容仍然保留"); owner.Inlines.Add(run); owner.Select(1, 3);
        Attach(owner, ".owner{content:image('sprite.png#xywh=20,0,20,10',red) / '裁剪图片';}", resolver);
        Layout(owner);
        Assert.True(Image(owner).Valid); Assert.False(Image(owner).HasNaturalRatio);
        Assert.Equal(new Size(300, 150), owner.DesiredSize); Assert.Equal(Colors.Red, Sample(owner));
        Assert.Equal("裁剪图片", owner.GetAutomationPeer()!.GetName());
        resolver.Open(); ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(20, 10), owner.DesiredSize); Assert.Equal(Colors.Blue, Sample(owner));
        Assert.Same(run, Assert.Single(owner.Inlines)); Assert.Equal(3, owner.SelectionLength);
        Assert.All(resolver.Requests, uri => Assert.Equal("", uri.Fragment));
        Css.SetStyle(owner, "content:normal;"); Layout(owner);
        Assert.Same(run, Assert.Single(owner.Inlines)); Assert.Equal(3, owner.SelectionLength);
    }

    [Fact]
    public void LoadingImageWithoutFallbackStillStartsDecodingAndLaterAcquiresNaturalSize()
    {
        var resolver = new Images(gated: true); resolver.Add("sprite.png", Sprite());
        var owner = new TextBlock { Text = "原文" };
        Attach(owner, ".owner{content:image('sprite.png#xywh=20,0,20,10');}", resolver); Layout(owner);
        Assert.False(Image(owner).Valid); Assert.Equal(default, Image(owner).NaturalSize);
        resolver.Open(); PumpUntil(() => Image(owner).Valid); Layout(owner);
        Assert.Equal(new Size(20, 10), owner.DesiredSize); Assert.Equal(Colors.Blue, Sample(owner));
    }

    [Theory]
    [InlineData("xywh=20,0,20,10", 20, 10)]
    [InlineData("xywh=pixel:20,0,20,10", 20, 10)]
    [InlineData("xywh=percent:50,0,50,50", 20, 10)]
    [InlineData("%78ywh=20%2C0%2C20%2C10", 20, 10)]
    [InlineData("xywh=0,0,5,5&xywh=20,0,20,10", 20, 10)]
    [InlineData("xywh=20,0,80,10", 20, 10)]
    [InlineData("xywh=30,0,80,10", 10, 10)]
    public void SpatialFragmentsUseOrientedNaturalCoordinatesAndClipOverflow(string fragment, double width, double height)
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new TextBlock();
        Attach(owner, $".owner{{content:image('sprite.png#{fragment}',red);}}", resolver);
        ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(width, height), owner.DesiredSize); Assert.Equal(Colors.Blue, Sample(owner));
    }

    [Theory]
    [InlineData("xywh=50,0,10,10")]
    [InlineData("xywh=0,30,10,10")]
    public void AFragmentOriginOutsideTheImageIsIgnoredAsSpecifiedByMediaFragments(string fragment)
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new TextBlock();
        Attach(owner, $".owner{{content:image('sprite.png#{fragment}',white);}}", resolver); ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(40, 20), owner.DesiredSize); Assert.Equal(Colors.Red, Sample(owner, 3, 3));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("xywh=-1,0,10,10")]
    [InlineData("xywh=1.5,0,10,10")]
    [InlineData("xywh=0,0,0,10")]
    [InlineData("xywh=percent:60,0,50,100")]
    [InlineData("xywh=foo:0,0,10,10")]
    [InlineData("xywh=0,0,10%ZZ,10")]
    public void UnknownOrInvalidFragmentsProduceTheFallbackWithoutFetchingTheUncroppedImage(string fragment)
    {
        var resolver = new Images(); var owner = new Border();
        Attach(owner, $".owner{{width:40px;height:20px;background-image:image('sprite.png#{fragment}',blue);}}", resolver); Layout(owner);
        Assert.Equal(Colors.Blue, Sample(owner)); Assert.Empty(resolver.Requests);
    }

    [Fact]
    public void FallbackIsNotAnUnderlayBehindATransparentLoadedImage()
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new TextBlock();
        Attach(owner, ".owner{content:image('sprite.png#xywh=20,10,20,10',red);}", resolver); ReadyContent(owner); Layout(owner);
        Assert.Equal(0, Sample(owner).A); Assert.Equal(new Size(20, 10), owner.DesiredSize);
    }

    [Theory]
    [InlineData("ltr", "ltr", false)]
    [InlineData("ltr", "rtl", true)]
    [InlineData("rtl", "ltr", true)]
    [InlineData("rtl", "rtl", false)]
    public void DirectionTagsMirrorTheCroppedPictureOnlyWhenTheElementDirectionDiffers(string tag, string actual, bool flipped)
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new Border();
        Attach(owner, $".owner{{direction:{actual};width:40px;height:20px;background-image:image({tag} 'sprite.png#xywh=0,0,40,10',white);background-size:100% 100%;background-repeat:no-repeat;image-rendering:pixelated;}}", resolver);
        ReadyBackground(owner); Layout(owner);
        Assert.Equal(flipped ? Colors.Blue : Colors.Red, Sample(owner, 3, 5));
        Assert.Equal(flipped ? Colors.Red : Colors.Blue, Sample(owner, 36, 5));
    }

    [Fact]
    public void InheritedDirectionUpdatesTheCachedPaintWithoutAnotherResourceRead()
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var root = new StackPanel(); var owner = new Border();
        Css.SetClass(owner, "picture"); root.Children.Add(owner);
        Attach(root, ".picture{width:40px;height:20px;background-image:image(ltr 'sprite.png#xywh=0,0,40,10',white);background-size:100% 100%;}", resolver);
        Layout(root); ReadyBackground(owner); Layout(root); Assert.Equal(Colors.Red, Sample(owner, 3, 5));
        root.FlowDirection = FlowDirection.RightToLeft; Layout(root); Assert.Equal(Colors.Blue, Sample(owner, 3, 5));
        root.FlowDirection = FlowDirection.LeftToRight; Layout(root); Assert.Equal(Colors.Red, Sample(owner, 3, 5));
        Assert.Single(resolver.Requests);
    }

    [Fact]
    public void InvalidSourceUsesALiveCurrentColorFallbackAndDoesNotClaimANaturalRatio()
    {
        var resolver = new Images(); var owner = new TextBlock { Foreground = new SolidColorBrush(Colors.Green) };
        Attach(owner, ".owner{content:image('missing.png',currentcolor);}", resolver); Layout(owner);
        PumpUntil(() => resolver.Disposed > 0); Flush(); Assert.Equal(Colors.Green, Sample(owner));
        Assert.False(Image(owner).HasNaturalRatio); owner.Foreground = new SolidColorBrush(Colors.Blue); Layout(owner);
        Assert.Equal(Colors.Blue, Sample(owner)); Assert.Single(resolver.Requests);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MarkerLoadingAndFailureRespectTheImageFallbackAndListStyleType(bool fallback)
    {
        var resolver = new Images(gated: true); var owner = new StackPanel(); owner.Children.Add(new TextBlock { Text = "项目" });
        Attach(owner, $".owner{{display:list-item;list-style:decimal inside image('bad.png'{(fallback ? ",red" : "")});font-size:20px;}}", resolver); Layout(owner);
        var marker = CssGeneratedContent.Get(owner)!.Marker!;
        if (fallback) { Assert.True(marker.HasImageContent); Assert.Equal(new Size(20, 20), Image(marker).NaturalSize); }
        else Assert.Equal("1. ", marker.Text);
        resolver.Open(); PumpUntil(() => resolver.Disposed > 0); for (var i = 0; i < 20; i++) { Flush(); Thread.Sleep(1); } Layout(owner);
        Assert.Equal(fallback, marker.HasImageContent);
        if (!fallback) Assert.Equal("1. ", marker.Text);
    }

    [Theory]
    [InlineData("content")] [InlineData("background-image")] [InlineData("shape-outside")]
    public void FragmentAndFallbackEditsReuseTheResidentSource(string property)
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new Border();
        Attach(owner, $".owner{{width:40px;height:20px;float:left;{property}:image('sprite.png#xywh=0,0,20,10',red);}}", resolver);
        void Ready()
        {
            if (property == "content") ReadyContent(owner);
            else if (property == "background-image") ReadyBackground(owner);
            else { Layout(owner); var shape = (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!; PumpUntil(() => shape.Resources.All(r => r.Ready.IsCompleted)); }
        }
        Ready(); Css.SetStyle(owner, $"{property}:image('./sprite.png#xywh=20,0,20,10',blue);"); Layout(owner); Ready();
        Assert.Single(resolver.Requests); Assert.Equal(1, resolver.Disposed);
        if (property != "shape-outside") Assert.Equal(Colors.Blue, Sample(owner));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ShapeCroppingAndMirroringUseOnlyApprovedPixelsAndFallbackWhenDenied(bool allowed)
    {
        var resolver = new Images(allowed: allowed); resolver.Add("mask.png", Mask()); var owner = new Border();
        Attach(owner, ".owner{width:60px;height:20px;float:left;shape-outside:image(ltr 'mask.png#xywh=0,0,30,20',rgb(0 0 0 / .25));}", resolver, new("https://images.invalid/"));
        Layout(owner); var value = (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;
        PumpUntil(() => value.Resources.All(r => r.Ready.IsCompleted)); Layout(owner);
        var band = value.Resolve(owner, new(60, 20), default, 60, 0, .5)!.Band(0, 20);
        Assert.Equal(allowed, band.Active);
        if (allowed)
        {
            Assert.InRange(band.Right, 39.9, 40.1);
            owner.FlowDirection = FlowDirection.RightToLeft; Layout(owner);
            band = value.Resolve(owner, new(60, 20), default, 60, 0, .5)!.Band(0, 20);
            Assert.InRange(band.Left, 19.9, 20.1); Assert.Equal(60, band.Right);
        }
    }

    [Fact]
    public void SvgFragmentsUseTheSvgNaturalViewportForBothPaintAndShapeCoordinates()
    {
        var resolver = new Images(); resolver.Add("vector.svg", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg' width='40' height='20'><rect x='20' width='20' height='20' fill='blue'/></svg>"));
        var owner = new TextBlock();
        Attach(owner, ".owner{content:image-set(image('vector.svg#xywh=20,0,20,20',red) 2x);width:80px;height:40px;float:left;shape-outside:image('vector.svg#xywh=20,0,20,20');}", resolver); ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(20, 20), Image(owner).NaturalSize); Assert.Equal(Colors.Blue, Sample(owner));
        var shape = (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;
        PumpUntil(() => shape.Resources.All(r => r.Ready.IsCompleted));
        Assert.Equal(80, shape.Resolve(owner, new(80, 40), default, 80, 0, .5)!.Band(0, 40).Right);
    }

    [Fact]
    public void ImageSetDensityAppliesAfterCroppingAndCrossFadeUsesTheResultingNaturalDimensions()
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new TextBlock();
        Attach(owner, ".owner{content:cross-fade(image-set(image('sprite.png#xywh=20,0,20,10',red) 2x),white);}", resolver); ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(10, 5), Image(owner).NaturalSize);
        var color = Sample(owner); Assert.Equal(128, color.R); Assert.Equal(128, color.G); Assert.Equal(255, color.B);
    }

    [Theory]
    [InlineData("content")] [InlineData("background-image")] [InlineData("shape-outside")]
    public void RetiredAnnotatedLoadsCannotReplaceNewDeclarations(string property)
    {
        var resolver = new Images(gated: true); resolver.Add("sprite.png", Sprite()); var owner = new Border();
        Attach(owner, $".owner{{width:40px;height:20px;float:left;{property}:image('sprite.png',red);}}", resolver); Layout(owner);
        var ready = property == "shape-outside" ? ((CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!).Resources.Select(r => r.Ready).ToArray()
            : (property == "content" ? Image(owner).ActiveResources : (CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!).Select(r => r.Ready).ToArray();
        Css.SetStyle(owner, $"{property}:image(blue);"); Layout(owner); resolver.Open(); PumpUntil(() => ready.All(t => t.IsCompleted)); Layout(owner);
        if (property != "shape-outside") Assert.Equal(Colors.Blue, Sample(owner));
        else Assert.Empty(((CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!).Resources);
    }

    [Fact]
    public void MediaFragmentsUseExifOrientedCoordinatesAfterPlatformDecode()
    {
        var resolver = new Images(); resolver.Add("oriented.jpg", Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/4QAiRXhpZgAATU0AKgAAAAgAAQESAAMAAAABAAYAAAAAAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAAUACgDAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD8X6/ynP8Av4CgD5D/AGqv+ZD/AO5o/wDddr/qo/0Zf/nNj/vW7/4PZ/zX/wCkM/8AOIf/AHn7/wCAofIdf9VB/wA14UAfrxX/AJV5/wClAFAHyH+1V/zIf/c0f+67X/VR/oy//ObH/et3/wAHs/5r/wDSGf8AnEP/ALz9/wDAUPkOv+qg/wCa8KAP14r/AMq8/wDSgCgD5D/aq/5kP/uaP/ddr/qo/wBGX/5zY/71u/8Ag9n/ADX/AOkM/wDOIf8A3n7/AOAofIdf9VB/zXhQB//Z"));
        var owner = new TextBlock();
        Attach(owner, ".owner{content:image('oriented.jpg#xywh=0,20,20,20',green);}", resolver); ReadyContent(owner); Layout(owner);
        Assert.Equal(new Size(20, 20), Image(owner).NaturalSize);
        var color = Sample(owner); Assert.InRange(color.B, 250, 255); Assert.InRange(color.R, 0, 5); Assert.Equal(255, color.A);
    }

    [Fact]
    public void SamplingHintChangesInvalidateTheAnnotatedRasterCache()
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new Border();
        Attach(owner, ".owner{width:80px;height:20px;background-image:image(ltr 'sprite.png#xywh=0,0,40,10',white);background-size:100% 100%;image-rendering:pixelated;}", resolver);
        ReadyBackground(owner); Layout(owner);
        // Background layers retain their own brush; paint the boundary to exercise the actual cache.
        var nearest = Sample(owner, 39, 5); Assert.Equal(Colors.Red, nearest);
        Css.SetStyle(owner, "image-rendering:smooth;"); Layout(owner);
        var linear = Sample(owner, 39, 5); Assert.NotEqual(nearest, linear); Assert.InRange(linear.B, 1, 254);
        Assert.Single(resolver.Requests);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PixelFragmentsOfMultiResolutionIconsAreIgnoredWhilePercentFragmentsStillCrop(bool percent)
    {
        var resolver = new Images(); resolver.Add("multi.ico", Convert.FromBase64String("AAABAAIAEBAAAAAAIABuAAAAJgAAACAgAAAAACAAdgAAAJQAAACJUE5HDQoaCgAAAA1JSERSAAAAEAAAABAIBgAAAB/z/2EAAAA1SURBVHicY/jPwPAfHf9jYATTHxn4/vMxfAQLMzL8w6b0PxMDhYBp1ACG0TBgGA0DhsEQBgCqcx//1FqxywAAAABJRU5ErkJggolQTkcNChoKAAAADUlIRFIAAAAgAAAAIAgGAAAAc3p69AAAAD1JREFUeJzt0bENAAAMwrCU/39uj+jAEu9ISJmF5WF+c0JZPIAJyuIBTFAWD2CCsngAE5TFA5igLB7ABHQdDtUDPm6EFiMAAAAASUVORK5CYII="));
        var owner = new TextBlock();
        Attach(owner, $".owner{{content:image('multi.ico#xywh={(percent ? "percent:50,0,50,100" : "0,0,4,4")}',green);}}", resolver);
        ReadyContent(owner); Layout(owner);
        var resource = Assert.Single(Image(owner).ActiveResources); Assert.True(resource.HasMultipleResolutions);
        var raw = resource.Get(CssNode.Get(Image(owner)), true)!;
        Assert.Equal(new Size(raw.Width * (percent ? .5 : 1), raw.Height), Image(owner).NaturalSize);
        var color = Sample(owner, (int)(owner.ActualWidth * .2), (int)(owner.ActualHeight * .5));
        Assert.Equal(percent ? Colors.Blue : Colors.Red, color);
    }

    [Fact]
    public void PendingFallbackAndResolvedAnnotationsKeepReplacedChildrenOutOfFocusAndAccessibilityUntilRestored()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var resolver = new Images(gated: true); resolver.Add("sprite.png", Sprite());
        var before = new TextBlock { Text = "之前", Focusable = true };
        var child = new TextBlock { Text = "内部", Focusable = true };
        var after = new TextBlock { Text = "之后", Focusable = true };
        foreach (var text in new[] { before, child, after }) KeyboardNavigation.SetIsTabStop(text, true);
        var owner = new StackPanel(); owner.Children.Add(child); Css.SetClass(owner, "picture");
        var root = new StackPanel(); root.Children.Add(before); root.Children.Add(owner); root.Children.Add(after);
        var window = new DisplayedTestWindow { Content = root, Width = 500, Height = 600, TitleBarStyle = WindowTitleBarStyle.Native };
        try
        {
            Layout(window); Assert.True(child.Focus()); var peer = owner.GetAutomationPeer()!; Assert.NotEmpty(peer.GetChildren());
            Attach(root, ".picture{content:image('sprite.png',red) / '图片说明';width:80px;height:40px;}", resolver); Layout(window);
            Assert.False(child.IsVisible); Assert.False(child.Focus()); Assert.Empty(peer.GetChildren()); Assert.Equal("图片说明", peer.GetName());
            Assert.True(before.Focus()); Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(after, Keyboard.FocusedElement);
            resolver.Open(); ReadyContent(owner); Layout(window); Assert.False(child.Focus()); Assert.Empty(peer.GetChildren());
            Css.SetStyle(owner, "content:normal;"); Layout(window);
            Assert.True(child.IsVisible); Assert.NotEmpty(peer.GetChildren()); Assert.True(before.Focus());
            Assert.True(KeyboardNavigation.MoveFocus(before)); Assert.Same(child, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void AnimatedAnnotatedPaintingRefreshesWhileTheCroppedShapeRetainsItsFirstFrame()
    {
        var resolver = new Images(); resolver.Add("animated.gif", Gif()); var owner = new Border();
        Attach(owner, ".owner{width:40px;height:10px;float:left;background-image:image(rtl 'animated.gif#xywh=0,0,4,1',red);background-size:100% 100%;shape-outside:image(rtl 'animated.gif#xywh=0,0,4,1');}", resolver);
        ReadyBackground(owner); Layout(owner);
        var shape = (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!; PumpUntil(() => shape.Resources.All(r => r.Ready.IsCompleted));
        var resource = Assert.Single((CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!);
        var bitmap = Assert.IsType<BitmapImage>(resource.Get(CssNode.Get(owner))); PumpUntil(() => bitmap.AnimatedSubstitute is not null);
        var animation = bitmap.AnimatedSubstitute!; animation.Stop();
        try
        {
            animation.CurrentFrameIndex = 0; Flush(); var first = Sample(owner, 5, 5); owner.ClearRenderDirty();
            animation.CurrentFrameIndex = 1; Flush(); Assert.True(owner.IsRenderDirty); var second = Sample(owner, 5, 5); Assert.NotEqual(first, second);
            var band = shape.Resolve(owner, new(40, 10), default, 40, 0, .5)!.Band(0, 10);
            Assert.Equal(40, band.Right); Assert.InRange(band.Left, 19.9, 20.1);
            Css.SetStyle(owner, "background-image:image(blue);shape-outside:none;"); Layout(owner); owner.ClearRenderDirty();
            animation.CurrentFrameIndex = 0; Flush(); Assert.False(owner.IsRenderDirty);
        }
        finally { animation.Stop(); }
    }

    private static byte[] Gif(){using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}}){writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);}writer.Write((byte)0x3b);return output.ToArray();}

    [Fact]
    public void EmptyDimensionlessSvgIsValidAndTransparentRatherThanPaintingItsFallback()
    {
        var resolver = new Images(); resolver.Add("empty.svg", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"));
        var owner = new TextBlock();
        Attach(owner, ".owner{content:image('empty.svg',red);shape-outside:image('empty.svg',red);float:left;}", resolver);
        ReadyContent(owner); Layout(owner);
        Assert.True(Image(owner).Valid); Assert.False(Image(owner).HasNaturalRatio);
        Assert.Equal(new Size(300, 150), Image(owner).NaturalSize); Assert.Equal(0, Sample(owner).A);
        var shape = (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;
        PumpUntil(() => shape.Resources.All(r => r.Ready.IsCompleted));
        Assert.False(shape.Resolve(owner, new(100, 100), default, 100, 0, .5)!.Band(0, 100).Active);
    }

    [Fact]
    public void AResidentAnnotatedLayoutRerasterizesAtTheCurrentHostDpi()
    {
        var resolver = new Images(); resolver.Add("sprite.png", Sprite()); var owner = new Border();
        Attach(owner, ".owner{width:40px;height:20px;background-image:image('sprite.png#xywh=20,0,20,10',white);}", resolver);
        ReadyBackground(owner); Layout(owner);
        var layout = CssBackgroundPaintProperties.Get(owner)!.Layers[0].Image!.CssImageLayout!;
        var first = Assert.IsType<ImageBrush>(layout.Resolve(40, 20));
        Assert.Equal(40, Assert.IsAssignableFrom<BitmapSource>(first.ImageSource).PixelWidth);
        // Retained drawing commands can still hold this layout while the owner changes DPI.
        VisualTreeHelper.SetRootDpi(owner, new(2, 2));
        var second = Assert.IsType<ImageBrush>(layout.Resolve(40, 20));
        Assert.Equal(80, Assert.IsAssignableFrom<BitmapSource>(second.ImageSource).PixelWidth);
        Assert.NotSame(first, second); Assert.Single(resolver.Requests);
    }

    private static CssGeneratedImage Image(FrameworkElement owner) => Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(owner))
        .Select(i => VisualTreeHelper.GetChild(owner, i)).OfType<CssGeneratedImage>().Single();
    private static void Attach(FrameworkElement owner, string source, Images resolver, Uri? baseUri = null)
    {
        if (Css.GetClass(owner).Length == 0) Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse(source, "image-annotations", baseUri ?? new Uri("file:///images/"), resolver);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
    }
    private static void ReadyContent(FrameworkElement owner)
    {
        Layout(owner); PumpUntil(() => Image(owner).ActiveResources.All(r => r.Ready.IsCompleted));
        foreach (var resource in Image(owner).ActiveResources) Preload(resource.Get(CssNode.Get(Image(owner)), true));
        Layout(owner);
    }
    private static void ReadyBackground(FrameworkElement owner)
    {
        Layout(owner); var resources = (CssBackgroundImageResource[])owner.GetValue(CssBackgroundImageResource.ActiveProperty)!;
        PumpUntil(() => resources.All(r => r.Ready.IsCompleted)); foreach (var resource in resources) Preload(resource.Get(CssNode.Get(owner))); Flush();
    }
    private static void Preload(ImageSource? source)
    {
        if (source is BitmapImage bitmap) { var task = bitmap.PreloadAsync(160, 80); PumpUntil(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
        Flush();
    }
    private static Color Sample(FrameworkElement owner, int x = -1, int y = -1)
    {
        var width = (int)Math.Max(1, owner.ActualWidth); var height = (int)Math.Max(1, owner.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(target);
        owner.Render(drawing); drawing.Close(); var bytes = target.GetPixelBuffer(); var index = ((y < 0 ? height / 2 : y) * width + (x < 0 ? width / 2 : x)) * 4;
        return Color.FromArgb(bytes[index + 3], bytes[index + 2], bytes[index + 1], bytes[index]);
    }
    private static void Layout(FrameworkElement owner)
    {
        for (var i = 0; i < 5; i++) { Flush(); owner.Measure(new(500, 1000)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); }
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void PumpUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(10); while (!condition() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); }
        Assert.True(condition(), "Annotated image operation timed out"); Flush();
    }
    private static byte[] Sprite() => Png((x, y) => y >= 10 ? x < 20 ? Colors.Green : Colors.Transparent : x < 20 ? Colors.Red : Colors.Blue);
    private static byte[] Mask() => Png((x, _) => x < 20 ? Colors.Blue : Colors.Transparent);
    private static byte[] Png(Func<int, int, Color> pixel)
    {
        var bytes = new byte[40 * 20 * 4];
        for (var y = 0; y < 20; y++) for (var x = 0; x < 40; x++) { var color = pixel(x, y); var i = (y * 40 + x) * 4; bytes[i] = color.B; bytes[i + 1] = color.G; bytes[i + 2] = color.R; bytes[i + 3] = color.A; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40, 20, 96, 96, PixelFormats.Bgra32, null, bytes, 160)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private sealed class Images(bool gated = false, bool? allowed = null) : ICssResourceResolver
    {
        internal readonly List<Uri> Requests = [];
        private readonly Dictionary<string, byte[]> _bytes = [];
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposed;
        internal void Add(string name, byte[] bytes) => _bytes[name] = bytes;
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            lock (Requests) Requests.Add(uri); if (gated) await _gate.Task;
            return new(uri, new Closed(_bytes.GetValueOrDefault(Path.GetFileName(uri.LocalPath), [1, 2, 3]), () => Interlocked.Increment(ref Disposed))) { ImagePixelAccessAllowed = allowed };
        }
    }
    private sealed class Closed(byte[] bytes, Action disposed) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) disposed(); }
    }
}
