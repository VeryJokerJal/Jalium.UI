using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssMaskBorderTests
{
    [Theory]
    [InlineData("mask-border", "none")]
    [InlineData("mask-border", "url('nine.png') 10 fill / 8px / 2 round space luminance")]
    [InlineData("mask-border", "luminance round 10% / / 4px image(white)")]
    [InlineData("mask-border", "alpha 0 fill")]
    [InlineData("mask-border-source", "image-set('one.png' 1x,'two.png' 2x)")]
    [InlineData("mask-border-source", "cross-fade(image(black),image(white) 25%)")]
    [InlineData("mask-border-slice", "fill 10 20% 30 40")]
    [InlineData("mask-border-slice", "calc(100% / 3) fill")]
    [InlineData("mask-border-width", "auto 2 10% calc(1em + 2px)")]
    [InlineData("mask-border-width", "calc(-1px) calc(2 * sibling-count())")]
    [InlineData("mask-border-outset", "0 1em calc(1 + 2)")]
    [InlineData("mask-border-repeat", "round space")]
    [InlineData("mask-border-mode", "alpha")]
    [InlineData("mask-border-mode", "luminance")]
    public void GrammarIsAvailable(string name, string value) => Assert.True(CssAuthoring.IsValueSupported(name, value));

    [Theory]
    [InlineData("mask-border", "url('nine.png') / 2")]
    [InlineData("mask-border", "10 /")]
    [InlineData("mask-border", "10 / /")]
    [InlineData("mask-border", "10 / 2 / 3 / 4")]
    [InlineData("mask-border", "image(white) image(black)")]
    [InlineData("mask-border", "alpha luminance")]
    [InlineData("mask-border", "10,20")]
    [InlineData("mask-border-source", "black")]
    [InlineData("mask-border-slice", "fill")]
    [InlineData("mask-border-slice", "10px")]
    [InlineData("mask-border-slice", "10 fill 20")]
    [InlineData("mask-border-slice", "fill 10 fill")]
    [InlineData("mask-border-slice", "-1")]
    [InlineData("mask-border-slice", "calc(20% + 5)")]
    [InlineData("mask-border-width", "-1px")]
    [InlineData("mask-border-width", "10 20 30 40 50")]
    [InlineData("mask-border-outset", "10%")]
    [InlineData("mask-border-outset", "calc(2px + 10%)")]
    [InlineData("mask-border-outset", "auto")]
    [InlineData("mask-border-repeat", "no-repeat")]
    [InlineData("mask-border-mode", "match-source")]
    [InlineData("mask-border-mode", "alpha,luminance")]
    public void InvalidDeclarationIsRejectedAsAWhole(string name, string value) => Assert.False(CssAuthoring.IsValueSupported(name, value));

    [Theory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 2)] [InlineData(true, 2)]
    public void NineSlicesUseSourceAlphaAndTheUnfilledMiddleIsOpaque(bool fill, int dpi)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-border:url('nine.png') 10{(fill ? " fill" : "")} / 8px;"); Ready(owner);
        var image = Render(owner, dpi);
        foreach (var (x, y, alpha) in new[] { (2, 2, 32), (40, 2, 64), (77, 2, 96), (2, 30, 128),
                     (40, 30, fill ? 160 : 255), (77, 30, 192), (2, 57, 224), (40, 57, 255), (77, 57, 0) })
            Assert.Equal(alpha, Pixel(image, x, y, dpi).A);
        Assert.Equal(new Size(80, 60), owner.DesiredSize);
        Assert.Equal(0, owner.GetExtraDirtyPadding());
    }

    [Theory]
    [InlineData(false, "alpha", 255)] [InlineData(false, "luminance", 255)]
    [InlineData(true, "alpha", 0)] [InlineData(true, "luminance", 0)]
    public void DefaultZeroSlicesPreserveTheCenterUnlessFillRequestsSourcePixels(bool fill, string mode, int expected)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-border:image(transparent) 0{(fill ? " fill" : "")} {mode};"); Layout(owner);
        Assert.Equal(expected, Pixel(Render(owner), 30, 20).A);
        Assert.True(((CssBorderImageMeasures)owner.GetValue(CssMaskBorderProperties.WidthProperty)!).Sides.All(side => side.Auto));
    }

    [Theory]
    [InlineData("alpha", "red", 128)] [InlineData("luminance", "red", 27)]
    [InlineData("luminance", "lime", 91)] [InlineData("luminance", "blue", 9)]
    public void BorderModeIsIndependentOfOrdinaryMaskMode(string mode, string color, int expected)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-border:cross-fade(image({color}) 50%,image(transparent) 50%) 0 fill {mode};mask-mode:luminance;"); Layout(owner);
        Assert.InRange((int)Pixel(Render(owner), 30, 20).A, expected - 1, expected + 1);
    }

    [Fact]
    public void MaskShorthandResetsAllSixBorderPropertiesButMaskImageDoesNot()
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask-border:image(black) 20 fill / 8px / 3px round luminance;"); Layout(owner);
        Css.SetStyle(owner, "mask-image:none;"); Layout(owner);
        Assert.True(CssMaskBorderProperties.Active(owner)); Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
        Css.SetStyle(owner, "mask:none;"); Layout(owner);
        Assert.False(CssMaskBorderProperties.Active(owner)); Assert.Equal(255, Pixel(Render(owner), 30, 20).A);
        var node = CssNode.Get(owner); var lengths = CssEngine.BuildLengthContext(node);
        foreach (var name in CssCoreProperties.MaskBorderNames) Assert.True(CssMaskStyleQuery.Matches(name, "initial", node, lengths), name);
        Assert.Equal(14, CssPropertyMetadata.Longhands("mask").Count());
        Css.SetStyle(owner, ""); Layout(owner); Assert.True(CssMaskBorderProperties.Active(owner));
    }

    [Fact]
    public void MaskWideKeywordsAndCascadeLayersResetOrRestoreBorderPropertiesTogether()
    {
        var owner = new Border { Background = Brushes.Red };
        Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse("@layer base,top;@layer base{.owner{width:80px;height:60px;mask-border:image(black) 0 fill luminance}}@layer top{.owner{mask:revert-layer}}", "mask-border-layer");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Layout(owner);
        Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
        Css.SetStyle(owner, "mask:unset;"); Layout(owner); Assert.Equal(255, Pixel(Render(owner), 30, 20).A);
        Assert.True(CssMaskStyleQuery.Matches("mask", "initial", CssNode.Get(owner), CssEngine.BuildLengthContext(CssNode.Get(owner))));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void OrdinaryAndBorderMasksMultiplyAfterChildrenAndBeforeParentOpacity(int dpi)
    {
        var owner = new Canvas();
        owner.Children.Add(new Border { Width = 60, Height = 50, Background = Brushes.Red });
        var second = new Border { Width = 50, Height = 40, Background = Brushes.Red };
        Canvas.SetLeft(second, 20); Canvas.SetTop(second, 10); owner.Children.Add(second);
        Attach(owner, "mask-border:image(rgb(0 0 0 / .5)) 0 fill;mask-image:image(rgb(0 0 0 / .5));");
        var parent = new Canvas { Width = 80, Height = 60, Opacity = .5 }; parent.Children.Add(owner); Layout(parent);
        var root = new Canvas { Width = 80, Height = 60 }; root.Children.Add(parent); Layout(root);
        var image = Render(root, dpi);
        Assert.Equal(32, Pixel(image, 10, 10, dpi).A); Assert.Equal(32, Pixel(image, 30, 30, dpi).A);
        Assert.Equal(32, Pixel(image, 65, 30, dpi).A);
    }

    [Fact]
    public void OutsetsRevealExistingOverflowWithoutChangingGeometryOrCreatingInk()
    {
        var owner = new Canvas(); owner.Children.Add(new Border { Width = 110, Height = 40, Background = Brushes.Red });
        Attach(owner, "mask-border:image(white) 0 fill / 4px / 30px;");
        var root = new Canvas { Width = 120, Height = 80 }; root.Children.Add(owner); Layout(root);
        Assert.Equal(255, Pixel(Render(root), 100, 20).A); Assert.Equal(new Size(80, 60), owner.DesiredSize);
        Css.SetStyle(owner, "mask-border-outset:0;"); Layout(root); Assert.Equal(0, Pixel(Render(root), 100, 20).A);
        Assert.Equal(0, owner.GetExtraDirtyPadding());
    }

    [Theory]
    [InlineData("repeat")] [InlineData("round")] [InlineData("space")] [InlineData("stretch")]
    public void RepetitionHasNoTileCountCutoff(string repeat)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, $"mask-border:image(white) 1 fill / .001px {repeat};"); Layout(owner);
        var image = Render(owner);
        if (repeat != "space") Assert.Equal(255, Pixel(image, 40, 30).A);
        else
        {
            static bool InTile(double position, double extent, double tile)
            {
                var count = Math.Floor((extent - .002) / tile);
                var gap = (extent - .002 - count * tile) / (count + 1);
                var local = (position - .001 - gap) % (tile + gap);
                return local >= 0 && local < tile;
            }
            for (var y = 1; y < 59; y++) for (var x = 1; x < 79; x++)
                Assert.Equal(InTile(x + .5, 80, .078) && InTile(y + .5, 60, .058) ? 255 : 0, Pixel(image, x, y).A);
        }
        Assert.Equal(new Size(80, 60), owner.DesiredSize);
    }

    [Fact]
    public void OversizedWidthsNormalizeAllSidesAndNegativeCalculatedMeasuresClampAtUse()
    {
        var owner = new FlatInkElement();
        Attach(owner, "border:5px solid transparent;mask-border:url('nine.png') 10 / calc(infinity);"); Ready(owner);
        Assert.Equal(32, Pixel(Render(owner), 2, 2).A); Assert.Equal(96, Pixel(Render(owner), 77, 2).A);
        Css.SetStyle(owner, "mask-border-width:calc(-1px);"); Layout(owner);
        Assert.Equal(255, Pixel(Render(owner), 2, 2).A);
    }

    [Theory]
    [InlineData(null)] [InlineData(false)] [InlineData(true)]
    public void RemoteSourcesRequirePixelAccessAndLoadingOrFailureIsTransparent(bool? approval)
    {
        var resolver = new Images(gated: true, approval: approval); var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask-border:url('nine.png') 10 fill / 8px;", resolver, new Uri("https://example.test/masks/style.css")); Layout(owner);
        Assert.Equal(0, Pixel(Render(owner), 40, 30).A); resolver.Open(); Ready(owner, decode: approval == true);
        Assert.Equal(approval == true ? 160 : 0, Pixel(Render(owner), 40, 30).A);
        Assert.Equal(new Uri("https://example.test/masks/style.css"), resolver.Referrer);
        Css.SetStyle(owner, "mask-border-source:url('bad.png');"); Ready(owner, decode: false);
        Assert.Equal(0, Pixel(Render(owner), 40, 30).A);
    }

    [Fact]
    public void InheritedSourcesAreMaterializedForTheChildAndUnrelatedUpdatesReuseResources()
    {
        var resolver = new Images(); var parent = new Canvas();
        Attach(parent, "mask-border-source:url('nine.png');", resolver);
        var child = new Border { Width = 40, Height = 30, Background = Brushes.Red };
        parent.Children.Add(child); Css.SetStyle(child, "mask-border:inherit;mask-border-slice:10 fill;mask-border-width:8px;"); Ready(parent); Ready(child);
        var source = (CssMaskBorderSource)child.GetValue(CssMaskBorderProperties.SourceProperty)!;
        Assert.NotSame(parent.GetValue(CssMaskBorderProperties.SourceProperty), source);
        Assert.Equal(160, Pixel(Render(child), 20, 15).A);
        var resource = source.Resources.Single(); var count = resolver.Requests;
        Css.SetStyle(child, "mask-border:inherit;mask-border-slice:10 fill;mask-border-width:8px;color:blue;"); Ready(child);
        Assert.Same(resource, ((CssMaskBorderSource)child.GetValue(CssMaskBorderProperties.SourceProperty)!).Resources.Single());
        Assert.Equal(count, resolver.Requests);
    }

    [Fact]
    public void StyleQueriesCompareComputedMeasuresWithoutFetchingTheExpectedImage()
    {
        var resolver = new Images(); var owner = new Border();
        Attach(owner, "font-size:10px;mask-border:url('nine.png') 10 fill / 1em / 2px round alpha;", resolver); Ready(owner);
        var node = CssNode.Get(owner); var lengths = CssEngine.BuildLengthContext(node); var count = resolver.Requests;
        Assert.True(CssMaskStyleQuery.Matches("mask-border-width", "10px", node, lengths));
        Assert.True(CssMaskStyleQuery.Matches("mask-border-outset", "calc(1px + 1px)", node, lengths));
        Assert.True(CssMaskStyleQuery.Matches("mask-border-slice", "calc(5 + 5) fill", node, lengths));
        Assert.False(CssMaskStyleQuery.Matches("mask-border-source", "url('unrequested.png')", node, lengths));
        Assert.True(CssMaskStyleQuery.Matches("mask-border-source", null, node, lengths));
        Flush(); Assert.Equal(count, resolver.Requests);
    }

    [Theory]
    [InlineData("mask-border-width", "10px 20%", "30px 40%", 20, 30)]
    [InlineData("mask-border-outset", "1 2", "3 4", 2, 3)]
    [InlineData("mask-border-slice", "10% 20 fill", "30% 40 fill", 20, 30)]
    public void MeasuresInterpolateThroughTransitionsAndKeyframes(string name, string a, string b, double top, double right)
    {
        var owner = new Border(); Attach(owner, $"{name}:{a};"); Layout(owner);
        var property = CssPropertyRegistry.LookupForCompile(name, out _)!.StorageProperty!; var from = owner.GetValue(property)!;
        Css.SetStyle(owner, $"{name}:{b};"); Layout(owner); var to = owner.GetValue(property)!;
        var transition = new CssTransition(TimeSpan.FromSeconds(1), TimeSpan.Zero, new CssTimingFunction("linear"), false);
        var timeline = Assert.IsType<CssMaskAnimation>(CssTransitions.Create(owner, property, from, to, transition));
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        var value = Assert.IsType<CssBorderImageMeasures>(timeline.GetCurrentValue(from, to, clock));
        Assert.Equal(top, value.At(0, 100), 10); Assert.Equal(right, value.At(1, 100), 10);
        var sampler = new CssPropertySampler(property, [new(0, from, null), new(1, to, null)]);
        var keyframe = Assert.IsType<CssBorderImageMeasures>(sampler.Sample(.5, new CssTimingFunction("linear")));
        Assert.Equal(top, keyframe.At(0, 100), 10); Assert.Equal(right, keyframe.At(1, 100), 10);
    }

    [Fact]
    public void FillAndAutoChangesUseDiscreteTransitionsWhileSameAutoSidesStayAuto()
    {
        var owner = new Border(); Attach(owner, "mask-border-width:auto 10px;"); Layout(owner);
        var property = CssMaskBorderProperties.WidthProperty; var from = owner.GetValue(property)!;
        Css.SetStyle(owner, "mask-border-width:auto 30px;"); Layout(owner); var to = owner.GetValue(property)!;
        var timeline = CssMaskAnimation.Create(property, from, to)!; var clock = timeline.CreateClock(); clock.SetProgressForSampling(.5);
        var value = (CssBorderImageMeasures)timeline.GetCurrentValue(from, to, clock); Assert.True(value.Sides[0].Auto); Assert.Equal(20, value.At(1, 100));
        Css.SetStyle(owner, "mask-border-width:20px;"); Layout(owner); to = owner.GetValue(property)!;
        var transition = new CssTransition(TimeSpan.FromSeconds(1), TimeSpan.Zero, new CssTimingFunction("linear"), false);
        Assert.Null(CssTransitions.Create(owner, property, from, to, transition));
        Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(owner, property, from, to, transition with { AllowDiscrete = true }));
    }

    [Fact]
    public void ExplicitTransitionNamesResolveToTheirBorderMaskProperties()
    {
        var owner = new Border(); Attach(owner, "transition:mask-border-width 1s linear,mask-border-mode 2s linear allow-discrete;"); Layout(owner);
        var data = Assert.IsType<CssTransitionData>(owner.GetValue(CssTransitions.DataProperty));
        Assert.Equal(TimeSpan.FromSeconds(1), data.Find(owner, CssMaskBorderProperties.WidthProperty)!.Duration);
        var mode = data.Find(owner, CssMaskBorderProperties.ModeProperty)!;
        Assert.Equal(TimeSpan.FromSeconds(2), mode.Duration); Assert.True(mode.AllowDiscrete);
        Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(owner, CssMaskBorderProperties.ModeProperty, CssMaskMode.Alpha, CssMaskMode.Luminance, mode));
        Css.SetStyle(owner, "transition:mask-border 1s linear allow-discrete,mask-border-width 2s linear;"); Layout(owner);
        data = (CssTransitionData)owner.GetValue(CssTransitions.DataProperty)!;
        Assert.Equal(TimeSpan.FromSeconds(1), data.Find(owner, CssMaskBorderProperties.ModeProperty)!.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), data.Find(owner, CssMaskBorderProperties.WidthProperty)!.Duration);
        Assert.Null(data.Find(owner, CssMaskProperties.PositionsProperty));
        Css.SetStyle(owner, "transition:mask 3s linear;"); Layout(owner);
        data = (CssTransitionData)owner.GetValue(CssTransitions.DataProperty)!;
        Assert.Equal(TimeSpan.FromSeconds(3), data.Find(owner, CssMaskProperties.PositionsProperty)!.Duration);
        Assert.Equal(TimeSpan.FromSeconds(3), data.Find(owner, CssMaskBorderProperties.WidthProperty)!.Duration);
    }

    [Fact]
    public void ASourceThatFinishesOffThePropertyRebindsWhenItsDiscreteValueBecomesActive()
    {
        var resolver = new Images(gated: true); var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask-border:image(white) 0 fill;"); Layout(owner);
        var node = CssNode.Get(owner); var descriptor = CssPropertyRegistry.LookupForCompile("mask-border-source", out _)!;
        var reader = new CssTokenReader("url('nine.png')");
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { BaseUri = new Uri("file:///mask-border/"), ResourceResolver = resolver })!;
        var sink = new CssEngine.CssSetterCollector(); Assert.True(compiled.TryApply(new CssApplyContext(node, CssEngine.BuildLengthContext(node), new()), sink));
        var pending = (CssMaskBorderSource)sink.Values[CssMaskBorderProperties.SourceProperty].Value!;
        Assert.Equal(255, Pixel(Render(owner), 40, 30).A); resolver.Open(); PumpUntil(() => pending.Resources.All(value => value.Ready.IsCompleted));
        var current = owner.GetValue(CssMaskBorderProperties.SourceProperty)!;
        var transition = new CssTransition(TimeSpan.FromSeconds(1), TimeSpan.Zero, new CssTimingFunction("linear"), true);
        var timeline = Assert.IsType<CssDiscreteAnimation>(CssTransitions.Create(owner, CssMaskBorderProperties.SourceProperty, current, pending, transition));
        var clock = timeline.CreateClock(); clock.SetProgressForSampling(.75);
        owner.SetValue(CssMaskBorderProperties.SourceProperty, timeline.GetCurrentValue(current, pending, clock));
        Render(owner); var bitmap = Assert.IsType<BitmapImage>(Assert.IsType<ImageBrush>(pending.ResolveImage(owner)!.Brush).ImageSource);
        var task = bitmap.PreloadAsync(80, 60); PumpUntil(() => task.IsCompleted); Flush();
        Assert.Equal(160, Pixel(Render(owner), 40, 30).A);
    }

    [Fact]
    public void ImageSetDensityChangesAutomaticBorderWidthsWithTheHostDpi()
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, "mask-border:image-set('nine.png' 1x,'two.png' 2x) 10 fill;"); Ready(owner);
        Assert.Equal(32, Pixel(Render(owner), 7, 7).A);
        owner.SetRootDpi(new(2, 2)); Ready(owner);
        Assert.Equal(160, Pixel(Render(owner), 7, 7).A);
    }

    [Fact]
    public void AnimatedSourceFramesRefreshBothTheNineSlicePatchesAndTheGroupMask()
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, "mask-border:url('moving.gif') 0 fill;", new Images(animated: true)); Ready(owner);
        var resource = ((CssMaskBorderSource)owner.GetValue(CssMaskBorderProperties.SourceProperty)!).Resources.Single();
        var bitmap = Assert.IsType<BitmapImage>(resource.Get(CssNode.Get(owner)));
        // Preload completes when pixels are available. Multi-frame promotion
        // follows that completion on the decode worker, so wait for its publish.
        PumpUntil(() => bitmap.AnimatedSubstitute is not null);
        var animation = Assert.IsType<AnimatedBitmap>(bitmap.AnimatedSubstitute); animation.Stop();
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
    public void RecordedMasksOwnTheirPixelsAndReplayAfterStylesChange(bool simplify)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, "mask-border:url('nine.png') 10 fill / 8px;"); Ready(owner);
        var expected = Render(owner).GetPixelBuffer().ToArray(); var recorder = new DrawingRecorder(); recorder.BindWholeFrame(simplify);
        owner.Render(recorder); var commands = recorder.Commit(); Css.SetStyle(owner, "mask:none;"); Layout(owner);
        var target = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(target);
        DrawingReplayer.Replay(commands, drawing); drawing.Close(); Assert.Equal(expected, target.GetPixelBuffer());
    }

    [Fact]
    public void TransparentBorderMasksPreserveHitTestingFocusAndAutomation()
    {
        Keyboard.Initialize(); Keyboard.ClearFocus();
        var first = new Button { Content = "第一项", Width = 80, Height = 24 }; var second = new Button { Content = "第二项", Width = 80, Height = 24 };
        AutomationProperties.SetName(second, "第二项原始名称"); var owner = new StackPanel(); owner.Children.Add(first); owner.Children.Add(second);
        Attach(owner, "mask-border:image(transparent) 0 fill;"); var window = new DisplayedTestWindow { Width = 180, Height = 120, Content = owner };
        try
        {
            Layout(window); Assert.Equal(0, Pixel(Render(owner), 10, 10).A);
            Assert.NotNull(VisualTreeHelper.HitTest(owner, new Point(10, 10)));
            Assert.True(first.Focus()); Assert.True(KeyboardNavigation.MoveFocus(first)); Assert.Same(second, Keyboard.FocusedElement);
            Assert.Equal("第二项原始名称", FrameworkElementAutomationPeer.CreatePeerForElement(second)!.GetName());
        }
        finally { Keyboard.ClearFocus(); window.Close(); }
    }

    private static void Attach(FrameworkElement owner, string declarations, Images? images = null, Uri? baseUri = null)
    {
        Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse($".owner{{box-sizing:border-box;width:80px;height:60px;{declarations}}}", "mask-border-fixture", baseUri ?? new Uri("file:///mask-border/"), images ?? new Images());
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
    }
    private static void Ready(FrameworkElement owner, bool decode = true)
    {
        Layout(owner); var resources = ((CssMaskBorderSource)owner.GetValue(CssMaskBorderProperties.SourceProperty)!).Resources;
        PumpUntil(() => resources.All(resource => resource.Ready.IsCompleted)); Layout(owner);
        if (decode) foreach (var resource in resources) if (resource.Get(CssNode.Get(owner)) is BitmapImage bitmap)
        { var task = bitmap.PreloadAsync(80, 60); PumpUntil(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
        Layout(owner);
    }
    private static void PumpUntil(Func<bool> ready)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); } Assert.True(ready()); Flush(); }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement owner)
    { for (var i = 0; i < 5; i++) { Flush(); owner.Measure(new(500, 1000)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); } }
    private static RenderTargetBitmap Render(FrameworkElement owner, int dpi = 1)
    {
        var target = new RenderTargetBitmap((int)owner.ActualWidth * dpi, (int)owner.ActualHeight * dpi, 96 * dpi, 96 * dpi, PixelFormats.Bgra32);
        var drawing = new SoftwareDrawingContext(target); owner.Render(drawing); drawing.Close(); return target;
    }
    private static Color Pixel(RenderTargetBitmap target, int x, int y, int dpi = 1)
    { var data = target.GetPixelBuffer(); var at = (y * dpi * target.PixelWidth + x * dpi) * 4; return Color.FromArgb(data[at + 3], data[at + 2], data[at + 1], data[at]); }
    private sealed class FlatInkElement : Border
    {
        protected override void OnRender(DrawingContext context) => context.DrawRectangle(Brushes.Red, null, new Rect(RenderSize));
    }
    private sealed class Images(bool gated = false, bool? approval = true, bool animated = false) : ICssImageResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Requests; internal Uri? Referrer;
        internal void Open() => _gate.TrySetResult();
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default) => ResolveImageAsync(uri, null, cancellationToken);
        public async ValueTask<CssResource> ResolveImageAsync(Uri uri, Uri? referrer, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Requests); Referrer = referrer; if (gated) await _gate.Task;
            return new(uri, new MemoryStream(uri.AbsolutePath.EndsWith("bad.png", StringComparison.Ordinal) ? [1, 2, 3] : animated ? Gif() : Nine())) { ImagePixelAccessAllowed = approval };
        }
    }
    private static byte[] Gif()
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)4); writer.Write((ushort)1);
        writer.Write(new byte[] { 0x80, 0, 0, 114, 180, 149, 0, 0, 0 });
        foreach (var samples in new[] { new[] { 0, 0, 1, 1 }, new[] { 1, 1, 0, 0 } })
        {
            writer.Write(new byte[] { 0x21, 0xf9, 4, 9, 1, 0, 1, 0, 0x2c, 0, 0, 0, 0, 4, 0, 1, 0, 0, 2, 4 });
            uint packed = 0; var bits = 0;
            foreach (var value in samples) { packed |= (uint)4 << bits; bits += 3; packed |= (uint)value << bits; bits += 3; }
            packed |= (uint)5 << bits; writer.Write(packed); writer.Write((byte)0);
        }
        writer.Write((byte)0x3b); return output.ToArray();
    }
    private static byte[] Nine()
    {
        byte[] alpha = [32, 64, 96, 128, 160, 192, 224, 255, 0]; var pixels = new byte[30 * 30 * 4];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 30; x++)
        { var at = (y * 30 + x) * 4; pixels[at] = pixels[at + 1] = pixels[at + 2] = 255; pixels[at + 3] = alpha[y / 10 * 3 + x / 10]; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, pixels, 120)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
