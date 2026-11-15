using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using Jalium.UI.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssMaskTests
{
    [Theory]
    [InlineData("mask", "none")]
    [InlineData("mask", "image(black) alpha center / 20px 30% no-repeat padding-box content-box subtract")]
    [InlineData("mask", "no-clip border-box image(white) repeat-x 25% / cover luminance exclude")]
    [InlineData("mask", "linear-gradient(white,transparent),image-set('one.png' 1x,'two.png' 2x) alpha")]
    [InlineData("mask-image", "none,linear-gradient(white,transparent),image(red)")]
    [InlineData("mask-image", "cross-fade(image(black),image(white) 25%)")]
    [InlineData("mask-image", "url('m.svg#mask')")]
    [InlineData("mask-mode", "alpha,match-source,luminance")]
    [InlineData("mask-repeat", "repeat-x,space round,no-repeat")]
    [InlineData("mask-position", "right 4px bottom 2em,calc(100% - 2px) 10%")]
    [InlineData("mask-origin", "border-box,padding-box,content-box,fill-box,stroke-box,view-box")]
    [InlineData("mask-clip", "no-clip,border-box,padding-box,content-box,fill-box,stroke-box,view-box")]
    [InlineData("mask-size", "cover,contain,auto 20%,calc(50% - 3px) auto")]
    [InlineData("mask-composite", "add,subtract,intersect,exclude")]
    public void GrammarIsRegistered(string property, string value) => Assert.True(CssAuthoring.IsValueSupported(property, value));

    [Theory]
    [InlineData("mask", "image(black) image(white)")]
    [InlineData("mask", "image(black) alpha luminance")]
    [InlineData("mask", "center alpha / 10px")]
    [InlineData("mask", "image(white) / 10px")]
    [InlineData("mask", "center / -10px")]
    [InlineData("mask", "center / 10px / 20px")]
    [InlineData("mask", "no-clip no-clip")]
    [InlineData("mask", "border-box padding-box no-clip")]
    [InlineData("mask", "border-box border-box content-box")]
    [InlineData("mask", "image(white),")]
    [InlineData("mask-image", "red")]
    [InlineData("mask-image", "none image(white)")]
    [InlineData("mask-mode", "auto")]
    [InlineData("mask-origin", "no-clip")]
    [InlineData("mask-clip", "margin-box")]
    [InlineData("mask-size", "10px -2%")]
    [InlineData("mask-repeat", "repeat no-repeat space")]
    [InlineData("mask-composite", "source-over")]
    public void InvalidDeclarationIsRejected(string property, string value) => Assert.False(CssAuthoring.IsValueSupported(property, value));

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void OverlappingChildrenAreCompositedBeforeTheirMask(int dpi)
    {
        var owner = new Canvas();
        owner.Children.Add(new Border { Width = 50, Height = 40, Background = Brushes.Red });
        var second = new Border { Width = 50, Height = 40, Background = Brushes.Red };
        Canvas.SetLeft(second, 20); Canvas.SetTop(second, 20); owner.Children.Add(second);
        Attach(owner, "mask:image(rgb(0 0 0 / .5));"); Layout(owner);
        var image = Render(owner, dpi);
        Assert.Equal(128, Pixel(image, 8, 8, dpi).A); Assert.Equal(128, Pixel(image, 30, 30, dpi).A);
        Assert.Equal(128, Pixel(image, 65, 55, dpi).A); Assert.Equal(0, Pixel(image, 75, 5, dpi).A);
        Assert.Equal(new Size(80, 60), owner.DesiredSize);
    }

    [Theory]
    [InlineData("red", "alpha", 128)] [InlineData("red", "luminance", 27)]
    [InlineData("lime", "luminance", 91)] [InlineData("blue", "luminance", 9)]
    [InlineData("black", "match-source", 128)] [InlineData("black", "luminance", 0)]
    public void ModeUsesUnpremultipliedColorTimesAlpha(string color, string mode, int expected)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask:cross-fade(image({color}) 50%,image(transparent) 50%);mask-mode:{mode};"); Layout(owner);
        var actual = Pixel(Render(owner), 30, 20);
        Assert.InRange((int)actual.A, Math.Max(0, expected - 1), expected + 1);
        if (actual.A > 0) Assert.Equal(255, actual.R);
    }

    [Theory]
    [InlineData("add", 160)] [InlineData("subtract", 96)]
    [InlineData("intersect", 32)] [InlineData("exclude", 128)]
    public void LayerOperatorsUseTheCompletedLowerStack(string operation, int expected)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-image:image(rgb(0 0 0 / .5)),image(rgb(0 0 0 / .25));mask-composite:{operation},intersect;"); Layout(owner);
        Assert.InRange((int)Pixel(Render(owner), 30, 20).A, expected - 1, expected + 1);
    }

    [Theory]
    [InlineData("none,none", "intersect", 255)]
    [InlineData("none,image(rgb(0 0 0 / .5))", "intersect", 0)]
    [InlineData("image(rgb(0 0 0 / .5)),none", "subtract", 128)]
    [InlineData("image(rgb(0 0 0 / .5))", "intersect", 128)]
    public void NoneLayersAndTheBottomOperatorHaveDistinctSemantics(string images, string operation, int alpha)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-image:{images};mask-composite:{operation};"); Layout(owner);
        Assert.Equal(alpha, Pixel(Render(owner), 30, 20).A);
    }

    [Fact]
    public void ShorthandResetsLayerLonghandsAndClearingTheStyleRestoresTheUnmaskedContent()
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:image(white) luminance center / 10px no-repeat content-box padding-box exclude;"); Layout(owner);
        Css.SetStyle(owner, "mask:image(rgb(0 0 0 / .5));"); Layout(owner);
        Assert.Equal(CssMaskMode.MatchSource, ((CssMaskList<CssMaskMode>)owner.GetValue(CssMaskProperties.ModesProperty)!).At(0));
        Assert.Equal(CssMaskBox.Border, ((CssMaskList<CssMaskBox>)owner.GetValue(CssMaskProperties.OriginsProperty)!).At(0));
        Assert.Equal(CssMaskBox.Border, ((CssMaskList<CssMaskBox>)owner.GetValue(CssMaskProperties.ClipsProperty)!).At(0));
        Assert.Equal(CssMaskComposite.Add, ((CssMaskList<CssMaskComposite>)owner.GetValue(CssMaskProperties.CompositesProperty)!).At(0));
        Assert.Equal(CssBackgroundSizeMode.Auto, ((CssMaskList<CssBackgroundSize>)owner.GetValue(CssMaskProperties.SizesProperty)!).At(0).Mode);
        Assert.Equal(128, Pixel(Render(owner), 5, 5).A);
        Css.SetStyle(owner, "mask:none;"); Layout(owner); Assert.Equal(255, Pixel(Render(owner), 5, 5).A);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void PercentageSizingAndEdgePositionsResolveAgainstThePositioningArea(int dpi)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:image(white) right bottom / 50% 50% no-repeat;"); Layout(owner);
        var image = Render(owner, dpi);
        Assert.Equal(0, Pixel(image, 20, 15, dpi).A); Assert.Equal(255, Pixel(image, 60, 45, dpi).A);
        Assert.Equal(0, Pixel(image, 60, 15, dpi).A); Assert.Equal(0, Pixel(image, 20, 45, dpi).A);
        Css.SetStyle(owner, "width:120px;"); Layout(owner); image = Render(owner, dpi);
        Assert.Equal(0, Pixel(image, 50, 45, dpi).A); Assert.Equal(255, Pixel(image, 90, 45, dpi).A);
    }

    [Fact]
    public void NestedMasksAndParentOpacityMultiplyOncePerCompletedGroup()
    {
        var child = new Border { Background = Brushes.Red, Width = 80, Height = 60 };
        var owner = new Canvas { Opacity = .5 }; owner.Children.Add(child);
        Attach(owner, "mask:image(rgb(0 0 0 / .5));"); Css.SetStyle(child, "mask:image(rgb(0 0 0 / .5));"); Layout(owner);
        var root = new Canvas(); root.Children.Add(owner); Layout(root);
        Assert.InRange((int)Pixel(Render(root), 20, 20).A, 31, 33);
    }

    [Fact]
    public void AbortedMaskedUserDrawingRestoresParentPixelsClipAndOpacity()
    {
        var owner = new ThrowingElement(); Attach(owner, "mask:image(white);"); Layout(owner);
        var image = new RenderTargetBitmap(100, 80, 96, 96, PixelFormats.Bgra32); image.Clear(Colors.White);
        var drawing = new SoftwareDrawingContext(image);
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, 90, 70))); drawing.PushOpacity(.5);
        Assert.Throws<InvalidOperationException>(() => owner.Render(drawing));
        drawing.DrawRectangle(Brushes.Blue, null, new Rect(0, 0, 100, 80)); drawing.Pop(); drawing.Pop(); drawing.Close();
        Assert.Equal(255, Pixel(image, 20, 20).A); Assert.InRange((int)Pixel(image, 20, 20).R, 127, 128);
        Assert.Equal(Colors.White, Pixel(image, 95, 75));
    }

    [Theory]
    [InlineData("mask-mode", "alpha,luminance", "alpha,luminance")]
    [InlineData("mask-repeat", "repeat-x", "repeat no-repeat")]
    [InlineData("mask-origin", "padding-box", "padding-box")]
    [InlineData("mask-clip", "no-clip", "no-clip")]
    [InlineData("mask-composite", "exclude,add", "exclude,add")]
    [InlineData("mask-position", "right bottom", "100% 100%")]
    [InlineData("mask-position", "2em 10%", "32px 10%")]
    [InlineData("mask-size", "2em auto", "32px auto")]
    [InlineData("mask-size", "auto", "auto auto")]
    [InlineData("mask-image", "image(black)", "image(black)")]
    [InlineData("mask", "image(white) alpha center / 2em no-repeat", "image(white) alpha 50% 50% / 32px auto no-repeat")]
    public void StyleQueriesCompareComputedMaskValues(string name, string value, string equivalent)
    {
        var owner = new Border(); Attach(owner, $"font-size:16px;{name}:{value};"); Layout(owner);
        var node = CssNode.Get(owner); var lengths = CssEngine.BuildLengthContext(node);
        Assert.True(CssOrdinaryStyleQuery.IsSupported(name));
        Assert.True(CssOrdinaryStyleQuery.Matches(name, equivalent, node, lengths));
    }

    [Fact]
    public void ImageStyleQueriesDoNotStartFetchingTheirComparisonResource()
    {
        var owner = new Border(); Attach(owner, "mask:image(white);"); Layout(owner);
        Assert.False(CssOrdinaryStyleQuery.Matches("mask-image", "url('https://invalid.example/mask.png')", CssNode.Get(owner), CssEngine.BuildLengthContext(CssNode.Get(owner))));
    }

    [Fact]
    public void LoadingAndFailedImageLayersRemainTransparentUntilAValidDecodeIsPublished()
    {
        var resolver = new Images(gated: true); var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:url('mask.png') 0 0 / 100% 100% no-repeat;", resolver); Layout(owner);
        Assert.Equal(0, Pixel(Render(owner), 10, 20).A);
        resolver.Open(); Ready(owner); var image = Render(owner);
        Assert.Equal(128, Pixel(image, 10, 20).A); Assert.Equal(0, Pixel(image, 65, 20).A);
        Css.SetStyle(owner, "mask-image:url('bad.png');"); Ready(owner, decode: false);
        Assert.Equal(0, Pixel(Render(owner), 10, 20).A);
        Css.SetStyle(owner, "mask:none;"); Layout(owner); Assert.Equal(255, Pixel(Render(owner), 65, 20).A);
    }

    [Theory]
    [InlineData(true, 128)] [InlineData(false, 0)] [InlineData(null, 0)]
    public void RemoteMaskPixelsRequireTheResolversOriginApproval(bool? approved, int alpha)
    {
        var resolver = new Images(approval: approved); var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:url('mask.png') 0 0 / 100% 100% no-repeat;", resolver, new Uri("https://masks.example/style.css"));
        Ready(owner, decode: approved == true);
        Assert.Equal(alpha, Pixel(Render(owner), 10, 20).A); Assert.Equal(1, resolver.ImageRequests);
        Assert.Equal(new Uri("https://masks.example/style.css"), resolver.Referrer);
    }

    [Fact]
    public void InheritedSourcesSubscribeTheChildAndLonghandEditsReuseTheLoadedResource()
    {
        var resolver = new Images(gated: true); var child = new Border { Width = 80, Height = 60, Background = Brushes.Red };
        var parent = new Canvas(); parent.Children.Add(child);
        Attach(parent, "mask-image:url('mask.png');", resolver); Css.SetStyle(child, "mask-image:inherit;"); Layout(parent);
        var childImage = (CssMaskImages)child.GetValue(CssMaskProperties.ImagesProperty)!;
        Assert.NotSame(parent.GetValue(CssMaskProperties.ImagesProperty), childImage);
        Assert.Same(((CssMaskImages)parent.GetValue(CssMaskProperties.ImagesProperty)!).Resources[0], childImage.Resources[0]);
        resolver.Open(); Ready(parent); Ready(child); Assert.Equal(128, Pixel(Render(child), 10, 20).A);
        var resource = ((CssMaskImages)child.GetValue(CssMaskProperties.ImagesProperty)!).Resources[0];
        Css.SetStyle(child, "mask-image:inherit;mask-position:center;"); Layout(parent);
        Assert.Same(resource, ((CssMaskImages)child.GetValue(CssMaskProperties.ImagesProperty)!).Resources[0]); Assert.Equal(1, resolver.ImageRequests);
    }

    [Fact]
    public void FixedChildrenRemainInTheMaskGroupWithoutChangingTheirContainingBlock()
    {
        var child = new Border { Background = Brushes.Red };
        var owner = new Canvas(); owner.Children.Add(child); var root = new Canvas { Width = 160, Height = 120 }; root.Children.Add(owner);
        Canvas.SetLeft(owner, 10); Canvas.SetTop(owner, 10);
        Attach(owner, "mask:image(rgb(0 0 0 / .5));");
        Css.SetStyle(child, "position:fixed;left:20px;top:20px;width:40px;height:30px;"); Layout(root);
        Assert.Equal(new Point(20, 20), child.TransformToAncestor(root));
        Assert.Equal(128, Pixel(Render(root), 30, 30).A);
        Assert.Same(owner, child.VisualParent);
        Css.SetStyle(owner, "mask:none;"); Layout(root); Assert.Equal(255, Pixel(Render(root), 30, 30).A);
    }

    [Theory]
    [InlineData("space no-repeat", 0)] [InlineData("round no-repeat", 255)]
    public void SpaceLeavesDistributedGapsWhileRoundCoversTheWholeAxis(string repeat, int gapAlpha)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask:image(white) 0 0 / 30px 20px {repeat};"); Layout(owner);
        var image = Render(owner);
        Assert.Equal(255, Pixel(image, 5, 10).A); Assert.Equal(gapAlpha, Pixel(image, 40, 10).A);
        Assert.Equal(255, Pixel(image, 60, 10).A); Assert.Equal(0, Pixel(image, 5, 35).A);
    }

    [Theory]
    [InlineData("content-box", 0)] [InlineData("fill-box", 0)]
    [InlineData("stroke-box", 255)] [InlineData("view-box", 255)]
    public void GeometryBoxesUseTheOwnersActualBorderAndPadding(string box, int outerAlpha)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"border:5px solid blue;padding:10px;mask:image(white) {box};"); Layout(owner);
        var image = Render(owner);
        Assert.Equal(outerAlpha, Pixel(image, 10, 10).A); Assert.Equal(255, Pixel(image, 30, 30).A);
    }

    [Theory]
    [InlineData("border-box", 0)] [InlineData("no-clip", 255)]
    public void NoClipMasksOverflowInkOutsideTheBorderBox(string clip, int alpha)
    {
        var child = new Border { Width = 110, Height = 40, Background = Brushes.Red };
        var owner = new Canvas(); owner.Children.Add(child); Attach(owner, $"mask:image(white);mask-clip:{clip};");
        var root = new Canvas { Width = 120, Height = 80 }; root.Children.Add(owner); Layout(root);
        Assert.Equal(alpha, Pixel(Render(root), 100, 20).A);
    }

    [Fact]
    public void ATransparentMaskPreservesHitTestingTabOrderAndAutomationNames()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var first = new Button { Content = "第一项", Width = 80, Height = 24 };
        var second = new Button { Content = "第二项", Width = 80, Height = 24 };
        AutomationProperties.SetName(second, "原始第二项");
        var owner = new StackPanel(); owner.Children.Add(first); owner.Children.Add(second); Attach(owner, "mask:image(transparent);");
        var window = new DisplayedTestWindow { Width = 180, Height = 120, Content = owner };
        try
        {
            Layout(window); var image = Render(owner); Assert.Equal(0, Pixel(image, 10, 10).A);
            Assert.NotNull(VisualTreeHelper.HitTest(owner, new Point(10, 10)));
            Assert.True(first.Focus()); Assert.True(KeyboardNavigation.MoveFocus(first)); Assert.Same(second, Keyboard.FocusedElement);
            Assert.Equal("原始第二项", FrameworkElementAutomationPeer.CreatePeerForElement(second)!.GetName());
            Assert.True(KeyboardNavigation.MoveFocus(second, reverse: true)); Assert.Same(first, Keyboard.FocusedElement);
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void PositionTransitionsAndKeyframesInterpolateRepeatableLists()
    {
        var owner = new Border(); Attach(owner, "mask-position:0px 0px,20px 0px;"); Layout(owner);
        var from = owner.GetValue(CssMaskProperties.PositionsProperty)!;
        Css.SetStyle(owner, "mask-position:100px 0px,200px 0px,300px 0px;"); Layout(owner);
        var to = owner.GetValue(CssMaskProperties.PositionsProperty)!;
        var transition = new CssTransition(TimeSpan.FromSeconds(1), TimeSpan.Zero, new CssTimingFunction("linear"), false);
        var timeline = Assert.IsType<CssMaskAnimation>(CssTransitions.Create(owner, CssMaskProperties.PositionsProperty, from, to, transition));
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        var middle = Assert.IsType<CssMaskList<CssBackgroundPosition>>(timeline.GetCurrentValue(from, to, clock));
        Assert.Equal(new[] { 50d, 110, 150, 60, 100, 160 }, middle.Values.Select(value => value.X.Resolve(100, value.Lengths)));
        var sampler = new CssPropertySampler(CssMaskProperties.PositionsProperty, [new(0, from, null), new(1, to, null)]);
        var keyframe = Assert.IsType<CssMaskList<CssBackgroundPosition>>(sampler.Sample(.5, new CssTimingFunction("linear")));
        Assert.Equal(middle.Values.Select(value => value.X.Resolve(100, value.Lengths)), keyframe.Values.Select(value => value.X.Resolve(100, value.Lengths)));
    }

    [Fact]
    public void SizeInterpolationRetainsAutoAxesAndKeywordsUseAnExplicitDiscreteTransition()
    {
        var owner = new Border(); Attach(owner, "mask-size:10px auto;"); Layout(owner);
        var from = owner.GetValue(CssMaskProperties.SizesProperty)!;
        Css.SetStyle(owner, "mask-size:50px auto;"); Layout(owner); var to = owner.GetValue(CssMaskProperties.SizesProperty)!;
        var timeline = Assert.IsType<CssMaskAnimation>(CssMaskAnimation.Create(CssMaskProperties.SizesProperty, from, to));
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        var size = Assert.IsType<CssMaskList<CssBackgroundSize>>(timeline.GetCurrentValue(from, to, clock)).At(0);
        Assert.Equal(30, CssBackgroundSize.Resolve(size.Width!.Value, 100, size.Lengths)); Assert.Null(size.Height);
        Css.SetStyle(owner, "mask-size:cover;"); Layout(owner); var cover = owner.GetValue(CssMaskProperties.SizesProperty)!;
        Assert.Null(CssMaskAnimation.Create(CssMaskProperties.SizesProperty, from, cover));
        var transition = new CssTransition(TimeSpan.FromSeconds(1), TimeSpan.Zero, new CssTimingFunction("linear"), false);
        Assert.Null(CssTransitions.Create(owner, CssMaskProperties.SizesProperty, from, cover, transition));
        Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(owner, CssMaskProperties.SizesProperty, from, cover, transition with { AllowDiscrete = true }));
    }

    [Fact]
    public void SampledPositionUpdatesRepaintTheMaskWithoutRearrangingTheBox()
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:image(white) 0% 0 / 20px 100% no-repeat;"); Layout(owner);
        var from = owner.GetValue(CssMaskProperties.PositionsProperty)!;
        Css.SetStyle(owner, "mask-position:100% 0;"); Layout(owner); var to = owner.GetValue(CssMaskProperties.PositionsProperty)!;
        var timeline = CssMaskAnimation.Create(CssMaskProperties.PositionsProperty, from, to)!;
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        owner.SetValue(CssMaskProperties.PositionsProperty, timeline.GetCurrentValue(from, to, clock));
        Assert.True(owner.IsArrangeValid);
        var image = Render(owner); Assert.Equal(255, Pixel(image, 40, 20).A); Assert.Equal(0, Pixel(image, 10, 20).A);
    }

    [Fact]
    public void AnimatedMaskFramesRefreshCachedPixelsAndRetiredFramesStayQuiet()
    {
        var resolver = new Images(animated: true); var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask:url('animated.gif') 0 0 / 100% 100% no-repeat;", resolver); Ready(owner);
        var resource = ((CssMaskImages)owner.GetValue(CssMaskProperties.ImagesProperty)!).Resources[0];
        var bitmap = Assert.IsType<BitmapImage>(resource.Get(CssNode.Get(owner))); var animation = Assert.IsType<AnimatedBitmap>(bitmap.AnimatedSubstitute);
        animation.Stop();
        try
        {
            animation.CurrentFrameIndex = 0; Flush(); Assert.Equal(255, Pixel(Render(owner), 10, 20).A);
            owner.ClearRenderDirty(); animation.CurrentFrameIndex = 1; Flush(); Assert.True(owner.IsRenderDirty);
            Assert.Equal(0, Pixel(Render(owner), 10, 20).A); Assert.Equal(255, Pixel(Render(owner), 65, 20).A);
            Css.SetStyle(owner, "mask:none;"); Layout(owner); owner.ClearRenderDirty(); animation.CurrentFrameIndex = 0; Flush(); Assert.False(owner.IsRenderDirty);
        }
        finally { animation.Stop(); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void WholeFrameRecordingPreservesMaskedPixelsAndOwnsItsPublishedAlphaBytes(bool simplify)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, "mask:image(rgb(0 0 0 / .5));"); Layout(owner);
        var expected = Render(owner).GetPixelBuffer().ToArray();
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(simplify); owner.Render(recorder); var drawing = recorder.Commit();
        Css.SetStyle(owner, "mask:none;"); Layout(owner);
        var target = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); var context = new SoftwareDrawingContext(target);
        DrawingReplayer.Replay(drawing, context); context.Close(); Assert.Equal(expected, target.GetPixelBuffer());
        recorder.BindWholeFrame(); var mask = (IAlphaMaskDrawingContext)recorder; byte[] alpha = [128];
        mask.BeginAlphaMaskCapture(new Rect(0, 0, 80, 60)); recorder.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 80, 60)); mask.EndAlphaMaskCapture();
        mask.ApplyAlphaMask(alpha, 1, 1, new Rect(0, 0, 80, 60), new Rect(0, 0, 80, 60)); Array.Fill(alpha, (byte)0); drawing = recorder.Commit();
        target = new(80, 60, 96, 96, PixelFormats.Bgra32); context = new(target); DrawingReplayer.Replay(drawing, context); context.Close(); Assert.Equal(128, Pixel(target, 30, 20).A);
    }

    [Fact]
    public void ReplayClosesTheOpenMaskWhenTheDestinationThrows()
    {
        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); var mask = (IAlphaMaskDrawingContext)recorder;
        mask.BeginAlphaMaskCapture(new Rect(0, 0, 10, 10)); recorder.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 10, 10));
        mask.EndAlphaMaskCapture(); mask.ApplyAlphaMask([128], 1, 1, new Rect(0, 0, 10, 10), new Rect(0, 0, 10, 10));
        var drawing = recorder.Commit(); var target = new ThrowingMaskDestination();
        Assert.Throws<InvalidOperationException>(() => DrawingReplayer.Replay(drawing, target)); Assert.Equal(0, target.Depth);
    }

    [Fact]
    public void NoClipIncludesExistingBorderImageOutsetsInTheCapturedInk()
    {
        var owner = new Border(); Attach(owner, "border:5px solid transparent;border-image:image(red) 10 fill / 5px / 8px;mask:image(white) no-clip;");
        var root = new Canvas { Width = 120, Height = 100 }; Canvas.SetLeft(owner, 10); Canvas.SetTop(owner, 10); root.Children.Add(owner); Layout(root);
        Assert.Equal(new Size(80, 60), owner.DesiredSize); Assert.Equal(Colors.Red, Pixel(Render(root), 5, 5));
    }

    [Fact]
    public void WritingToolsTextContextForwardsTheMaskGroupToItsDestination()
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, "mask:image(rgb(0 0 0 / .5));"); Layout(owner);
        var bitmap = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); var target = new SoftwareDrawingContext(bitmap);
        var text = new MacOSWritingToolsPresentation.CssTextContext(target);
        using (MacOSWritingToolsPresentation.BeginCssTextInk(text)) owner.Render(text);
        target.Close(); Assert.Equal(128, Pixel(bitmap, 30, 20).A);
    }
    private sealed class ThrowingMaskDestination : DrawingContextAdapter, IAlphaMaskDrawingContext
    {
        internal int Depth;
        public bool SupportsAlphaMasks => true;
        public void BeginAlphaMaskCapture(Rect bounds) => Depth++;
        public void EndAlphaMaskCapture() => Depth--;
        public void ApplyAlphaMask(byte[] coverage, int width, int height, Rect maskBounds, Rect captureBounds) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => throw new InvalidOperationException("Fixture destination failure");
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point point, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource source, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clip) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private sealed class ThrowingElement : FrameworkElement
    {
        protected override void OnRender(DrawingContext context)
        { context.DrawRectangle(Brushes.Red, null, new Rect(RenderSize)); throw new InvalidOperationException("Fixture render failure"); }
    }
    private static void Attach(FrameworkElement owner, string declarations, Images? resolver = null, Uri? baseUri = null)
    {
        Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse($".owner{{box-sizing:border-box;width:80px;height:60px;{declarations}}}", "mask-fixture", baseUri ?? new Uri("file:///masks/"), resolver ?? CssStyleSheet.DefaultResolver);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
    }
    private static void Ready(FrameworkElement owner, bool decode = true)
    {
        Layout(owner); var resources = ((CssMaskImages)owner.GetValue(CssMaskProperties.ImagesProperty)!).Resources;
        PumpUntil(() => resources.All(resource => resource.Ready.IsCompleted)); Layout(owner);
        if (decode) foreach (var resource in resources) if (resource.Get(CssNode.Get(owner)) is BitmapImage bitmap)
        { var task = bitmap.PreloadAsync(80, 60); PumpUntil(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
        Layout(owner);
    }
    private static void PumpUntil(Func<bool> ready)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); } Assert.True(ready()); Flush(); }
    private sealed class Images(bool gated = false, bool? approval = true, bool animated = false) : ICssImageResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ImageRequests; internal Uri? Referrer;
        internal void Open() => _gate.TrySetResult();
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default) => ResolveImageAsync(uri, null, cancellationToken);
        public async ValueTask<CssResource> ResolveImageAsync(Uri uri, Uri? referrer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ImageRequests); Referrer = referrer;
            if (gated) await _gate.Task;
            byte[] bytes;
            if (animated) bytes = Gif();
            else if (uri.AbsolutePath.EndsWith("bad.png", StringComparison.Ordinal)) bytes = [1, 2, 3];
            else
            {
                byte[] pixels = [255, 255, 255, 128, 255, 255, 255, 0];
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8)));
                using var output = new MemoryStream(); encoder.Save(output); bytes = output.ToArray();
            }
            return new(uri, new MemoryStream(bytes)) { ImagePixelAccessAllowed = approval };
        }
        private static byte[] Gif()
        {
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)4); writer.Write((ushort)1);
            writer.Write(new byte[] { 0x80, 0, 0, 114, 180, 149, 0, 0, 0 });
            foreach (var samples in new[] { new[] { 0, 0, 1, 1 }, new[] { 1, 1, 0, 0 } })
            {
                writer.Write(new byte[] { 0x21, 0xf9, 4, 9, 1, 0, 1, 0, 0x2c, 0, 0, 0, 0, 4, 0, 1, 0, 0, 2, 4 });
                uint packed = 0; var bits = 0; foreach (var value in samples) { packed |= (uint)4 << bits; bits += 3; packed |= (uint)value << bits; bits += 3; }
                packed |= (uint)5 << bits; writer.Write(packed); writer.Write((byte)0);
            }
            writer.Write((byte)0x3b); return output.ToArray();
        }
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement owner)
    { for (var i = 0; i < 5; i++) { Flush(); owner.Measure(new(500, 1000)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); } }
    private static RenderTargetBitmap Render(FrameworkElement owner, int dpi = 1)
    {
        var image = new RenderTargetBitmap((int)owner.ActualWidth * dpi, (int)owner.ActualHeight * dpi, 96 * dpi, 96 * dpi, PixelFormats.Bgra32);
        var context = new SoftwareDrawingContext(image); owner.Render(context); context.Close(); return image;
    }
    private static Color Pixel(RenderTargetBitmap image, int x, int y, int dpi = 1)
    {
        var data = image.GetPixelBuffer(); var index = (y * dpi * image.PixelWidth + x * dpi) * 4;
        return Color.FromArgb(data[index + 3], data[index + 2], data[index + 1], data[index]);
    }
}
