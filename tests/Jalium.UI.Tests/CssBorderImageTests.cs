using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssBorderImageTests
{
    [Theory]
    [InlineData("border-image", "url('nine.png') 10 fill / 5px / 2 round space")]
    [InlineData("border-image", "30% / / 2 repeat url('nine.png')")]
    [InlineData("border-image", "repeat 10 / auto url('nine.png')")]
    [InlineData("border-image", "none")]
    [InlineData("border-image", "image(ltr 'nine.png',red) 10")]
    [InlineData("border-image-source", "cross-fade(image-set('nine.png' 1x,'high.png' 2x),red)")]
    [InlineData("border-image-slice", "fill 10 20% 30 40")]
    [InlineData("border-image-slice", "calc(100% / 3) fill")]
    [InlineData("border-image-width", "auto 2 10% calc(1em + 2px)")]
    [InlineData("border-image-width", "calc(10% + 2px)")]
    [InlineData("border-image-width", "calc(-1px) calc(2 * sibling-count())")]
    [InlineData("border-image-outset", "0 1em calc(1 + 2)")]
    [InlineData("border-image-repeat", "round space")]
    public void GrammarIsAvailableInAuthoring(string name, string value) => Assert.True(CssAuthoring.IsValueSupported(name, value));

    [Theory]
    [InlineData("border-image", "url('nine.png') / 2")]
    [InlineData("border-image", "10 /")]
    [InlineData("border-image", "10 / /")]
    [InlineData("border-image", "10 / 2 / 3 / 4")]
    [InlineData("border-image", "10 url('a') url('b')")]
    [InlineData("border-image-slice", "fill")]
    [InlineData("border-image-slice", "10px")]
    [InlineData("border-image-slice", "10 2px")]
    [InlineData("border-image-slice", "10 fill 20")]
    [InlineData("border-image-slice", "fill 10 fill")]
    [InlineData("border-image-slice", "-1")]
    [InlineData("border-image-slice", "calc(20% + 5)")]
    [InlineData("border-image-width", "-1px")]
    [InlineData("border-image-width", "10 20 30 40 50")]
    [InlineData("border-image-width", "calc(1 + 10px)")]
    [InlineData("border-image-outset", "10%")]
    [InlineData("border-image-outset", "calc(2px + 10%)")]
    [InlineData("border-image-outset", "auto")]
    [InlineData("border-image-repeat", "no-repeat")]
    [InlineData("border-image-repeat", "repeat stretch round")]
    public void InvalidGrammarIsRejectedAsAWhole(string name, string value) => Assert.False(CssAuthoring.IsValueSupported(name, value));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NineSlicePixelsReplaceTheOrdinaryBorderAndOptionallyFillItsCenter(bool fill)
    {
        var resolver = new Images(); var owner = new Border();
        Attach(owner, $".owner{{box-sizing:border-box;width:80px;height:60px;background:white;border:5px solid green;border-image:url('nine.png') 10{(fill ? " fill" : "")};}}", resolver);
        Ready(owner); Layout(owner);
        Assert.Equal(Colors.Red, Sample(owner, 2, 2)); Assert.Equal(Colors.Lime, Sample(owner, 40, 2));
        Assert.Equal(Colors.Blue, Sample(owner, 77, 2)); Assert.Equal(Colors.Cyan, Sample(owner, 2, 30));
        Assert.Equal(fill ? Colors.Purple : Colors.White, Sample(owner, 40, 30));
        Assert.Equal(Colors.Yellow, Sample(owner, 77, 30)); Assert.Equal(Colors.Black, Sample(owner, 40, 57));
    }

    [Fact]
    public void CenterFillRemainsBelowChildrenAndWorksWithNoBackground()
    {
        foreach (var owner in new FrameworkElement[] { new Border(), new StackPanel() })
        {
            var child = new Border { Width = 20, Height = 20, Background = Brushes.Blue };
            if (owner is Border border) border.Child = child; else ((StackPanel)owner).Children.Add(child);
            Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('nine.png') 10 fill;}", new());
            Ready(owner); Layout(owner);
            var at = child.TransformToAncestor(owner);
            Assert.Equal(Colors.Blue, Sample(owner, (int)at.X + 5, (int)at.Y + 5));
            Assert.Equal(Colors.Purple, Sample(owner, 60, 40));
        }
    }

    [Fact]
    public void LoadingAndInvalidSourcesRetainTheNormalBorderButTransparentValidImagesSuppressIt()
    {
        var resolver = new Images(gated: true); var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('bad.png') 10;}", resolver); Layout(owner);
        Assert.Equal(Colors.Green, Sample(owner, 2, 2)); resolver.Open(); Ready(owner, decode: false); Layout(owner);
        Assert.Equal(Colors.Green, Sample(owner, 2, 2));
        Css.SetStyle(owner, "border-image:image(transparent) 10;"); Layout(owner);
        Assert.Equal(0, Sample(owner, 2, 2).A);
    }

    [Fact]
    public void WidthOverlapShrinksAllSidesProportionallyAndOutsetsOnlyExpandInk()
    {
        var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('nine.png') 10 / 100px / 8px;}", new());
        Ready(owner); Layout(owner);
        Assert.Equal(new Size(80, 60), owner.DesiredSize);
        Assert.Equal(8, owner.GetExtraDirtyPadding());
        Assert.Equal(96, owner.GetDirtyRenderBounds().Width);
        Assert.Equal(Colors.Red, Sample(owner, 2, 2));
        Assert.Equal(Colors.Blue, Sample(owner, 77, 2));
        Css.SetStyle(owner, "border-image:none;"); Layout(owner); Assert.Equal(0, owner.GetExtraDirtyPadding());
    }

    [Fact]
    public void BorderShorthandResetsTheImageWhileIndividualBorderSidesDoNot()
    {
        var owner = new Border(); Attach(owner, ".owner{width:80px;height:60px;border:5px solid green;border-image:image(red) 10;}", new()); Layout(owner);
        Css.SetStyle(owner, "border-left:8px solid blue;"); Layout(owner); Assert.NotNull(owner.GetValue(CssBorderImageProperties.SourceProperty));
        Css.SetStyle(owner, "border:6px solid blue;"); Layout(owner); Assert.Null(owner.GetValue(CssBorderImageProperties.SourceProperty));
        Assert.Equal(Colors.Blue, Sample(owner, 2, 2));
    }

    [Fact]
    public void RepeatsAreCenteredRoundUsesWholeTilesAndSpaceDistributesOuterGaps()
    {
        var repeat = CssBorderImagePainter.Tiles(0, 31, 10, CssBorderImageRepeat.Repeat);
        Assert.Equal(-9.5, repeat.Start); Assert.Equal(5, repeat.Count);
        var round = CssBorderImagePainter.Tiles(0, 31, 10, CssBorderImageRepeat.Round);
        Assert.Equal(3, round.Count); Assert.Equal(31d / 3, round.Tile);
        var space = CssBorderImagePainter.Tiles(0, 31, 10, CssBorderImageRepeat.Space);
        Assert.Equal(3, space.Count); Assert.Equal(.25, space.Start); Assert.Equal(10.25, space.Step);
        Assert.Equal(0, CssBorderImagePainter.Tiles(0, 4, 10, CssBorderImageRepeat.Space).Count);
    }

    [Fact]
    public void TransparentSourcesSuppressCssBordersOnDirectAndTemplatedRenderers()
    {
        foreach (var owner in new FrameworkElement[] { new Border(), new StackPanel(), new TextBlock(), new ContentControl(), new TextBox(), new PasswordBox(), new Image(), new Button() })
        {
            Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;background:white;border:5px solid green;border-image:image(transparent) 10;}", new());
            Layout(owner); var color = Sample(owner, 2, 2);
            Assert.True(color.A == 0 || color.R == color.G && color.G == color.B, owner.GetType().Name + ": " + color);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SemiTransparentFillAndEdgesAreCompositedOnce(bool background)
    {
        var owner = new Border();
        Attach(owner, $".owner{{box-sizing:border-box;width:80px;height:60px;{(background ? "background:white;" : "")}border:5px solid green;border-image:image(rgb(255 0 0 / .5)) 10 fill;}}", new()); Layout(owner);
        foreach (var point in new[] { new Point(2, 2), new Point(40, 2), new Point(40, 30) })
        {
            var color = Sample(owner, (int)point.X, (int)point.Y);
            Assert.Equal(background ? 255 : 128, color.A); Assert.Equal(255, color.R); Assert.InRange(color.G, background ? 126 : 0, background ? 128 : 0);
        }
    }

    [Fact]
    public void SourceInheritanceSubscribesTheChildAndLonghandEditsReuseItsResidentImage()
    {
        var resolver = new Images(gated: true); var parent = new StackPanel(); var child = new Border(); parent.Children.Add(child); Css.SetClass(child, "child");
        Attach(parent, ".owner{border-image-source:url('nine.png');}.child{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image-source:inherit;border-image-slice:10;}", resolver);
        Layout(parent); resolver.Open(); Ready(child); Layout(parent);
        Assert.Equal(Colors.Red, Sample(child, 2, 2));
        var source = ((CssBorderImageSource)child.GetValue(CssBorderImageProperties.SourceProperty)!).Resources.Single();
        Css.SetStyle(child, "border-image-slice:20%;border-image-repeat:round space;"); Layout(parent); Ready(child);
        Assert.Same(source, ((CssBorderImageSource)child.GetValue(CssBorderImageProperties.SourceProperty)!).Resources.Single());
    }

    [Fact]
    public void OutsetPixelsDoNotExpandInputOrLayoutBounds()
    {
        var resolver = new Images(); var root = new Canvas { Width = 160, Height = 120, Background = Brushes.White }; var owner = new Border();
        root.Children.Add(owner); Canvas.SetLeft(owner, 30); Canvas.SetTop(owner, 30);
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('nine.png') 10 / 10px / 10px;}", resolver);
        Ready(owner); Layout(root);
        Assert.Equal(new Size(80, 60), owner.RenderSize); Assert.NotSame(owner, root.InputHitTest(new Point(25, 25)));
        Assert.Same(owner, root.InputHitTest(new Point(32, 32)));
        Assert.Equal(Colors.Red, Sample(root, 25, 25));
    }

    [Fact]
    public void PercentageMathUsesImageDimensionsRatherThanBecomingAFractionalPixelCount()
    {
        var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('nine.png') calc(100% / 3) fill;}", new());
        Ready(owner); Layout(owner);
        Assert.Equal(Colors.Purple, Sample(owner, 70, 45));
    }

    [Fact]
    public void WidthMathResolvesPercentagesAgainstTheCorrespondingImageAreaAxis()
    {
        var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;background:white;border:5px solid green;border-image:url('nine.png') 10 / calc(10% + 2px);}", new());
        Ready(owner); Layout(owner);
        Assert.Equal(Colors.Cyan, Sample(owner, 8, 25)); Assert.Equal(Colors.White, Sample(owner, 11, 25));
        Assert.Equal(Colors.Lime, Sample(owner, 40, 7)); Assert.Equal(Colors.White, Sample(owner, 40, 9));
    }

    [Fact]
    public void DenseTwoDimensionalRepeatsUseBoundedSamplingAndKeepTheirAlpha()
    {
        var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:linear-gradient(rgb(255 0 0 / .5),rgb(0 0 255 / .5)) 49% fill / 5px repeat;}", new());
        Layout(owner); var before = GC.GetAllocatedBytesForCurrentThread(); var color = Sample(owner, 40, 30);
        Assert.InRange(color.A, 127, 129); Assert.InRange(color.R, 100, 155); Assert.InRange(color.B, 100, 155);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 8_000_000);
    }

    [Fact]
    public void ImageEdgesPaintWithTheBackgroundBelowContentThatOverlapsTheirDrawingArea()
    {
        var child = new Border { Background = Brushes.Blue };
        var owner = new Border { Child = child };
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:0;border-image:image(red) 10 fill / 10px;}", new());
        Layout(owner); Assert.Equal(Colors.Blue, Sample(owner, 2, 2)); Assert.Equal(Colors.Blue, Sample(owner, 40, 30));
    }

    [Fact]
    public void HugeComputedWidthsNormalizeWithoutInfinityTimesZero()
    {
        var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('nine.png') 10 / calc(infinity);}", new());
        Ready(owner); Layout(owner); Assert.Equal(Colors.Red, Sample(owner, 2, 2)); Assert.Equal(Colors.Blue, Sample(owner, 77, 2));
    }

    [Fact]
    public void TemplateChromePaintsBelowItsContentAndRefreshesWhenTheSourceOrTemplateChanges()
    {
        var blue = new Border { Width = 20, Height = 20, Background = Brushes.Blue };
        var chrome = new Border { Background = Brushes.White, Child = blue };
        var template = new ControlTemplate(typeof(Control)); template.SetVisualTree(() => chrome);
        var owner = new Control { Template = template };
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:0;background:white;border-image:image(red) 10 fill / 10px / 4px;}", new());
        Layout(owner); var position = blue.TransformToAncestor(owner);
        Assert.Equal(Colors.Blue, Sample(owner, (int)position.X + 2, (int)position.Y + 2)); Assert.Equal(Colors.Red, Sample(owner, 60, 40));
        Assert.Equal(4, chrome.GetExtraDirtyPadding());
        Css.SetStyle(owner, "border-image:image(green) 10 fill / 10px / 4px;"); Layout(owner); Assert.Equal(Colors.Green, Sample(owner, 60, 40));
        var next = new Border { Background = Brushes.White };
        var replacement = new ControlTemplate(typeof(Control)); replacement.SetVisualTree(() => next); owner.Template = replacement;
        Layout(owner); Assert.Equal(Colors.Green, Sample(owner, 60, 40)); Assert.Null(chrome.CssBorderImageTemplatePaintOwner);
        Css.SetStyle(owner, "border-image:none;"); Layout(owner); Assert.Equal(Colors.White, Sample(owner, 60, 40)); Assert.Equal(0, next.GetExtraDirtyPadding());
    }

    [Fact]
    public void AnimatedSlicesRefreshTheirCacheAndRetiredFramesCannotInvalidateTheOwner()
    {
        var resolver = new Images(); resolver.Add("animated.gif", Gif()); var owner = new Border();
        Attach(owner, ".owner{box-sizing:border-box;width:80px;height:60px;border:5px solid green;border-image:url('animated.gif') 0 fill / 0;}", resolver);
        Ready(owner); Layout(owner);
        var resource = ((CssBorderImageSource)owner.GetValue(CssBorderImageProperties.SourceProperty)!).Resources.Single();
        var bitmap = (BitmapImage)resource.Get(CssNode.Get(owner))!; PumpUntil(() => bitmap.AnimatedSubstitute is not null);
        var animation = bitmap.AnimatedSubstitute!; animation.Stop();
        try
        {
            animation.CurrentFrameIndex = 0; Flush(); var first = Sample(owner, 10, 30); owner.ClearRenderDirty();
            animation.CurrentFrameIndex = 1; Flush(); Assert.True(owner.IsRenderDirty); var second = Sample(owner, 10, 30);
            Assert.NotEqual(first, second); Assert.Equal(255, first.A); Assert.Equal(0, second.A);
            Css.SetStyle(owner, "border-image:none;"); Layout(owner); owner.ClearRenderDirty(); animation.CurrentFrameIndex = 0; Flush(); Assert.False(owner.IsRenderDirty);
        }
        finally { animation.Stop(); }
    }

    private static void Attach(FrameworkElement owner, string source, Images resolver)
    {
        Css.SetClass(owner, "owner"); var sheet = CssStyleSheet.Parse(source, "border-image", new Uri("file:///images/"), resolver);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
    }
    private static void Ready(FrameworkElement owner, bool decode = true)
    {
        Layout(owner); var resources = (CssBackgroundImageResource[])owner.GetValue(CssBorderImageProperties.ResourcesProperty)!;
        PumpUntil(() => resources.All(r => r.Ready.IsCompleted)); Layout(owner);
        if (decode) foreach (var resource in resources) if (resource.Get(CssNode.Get(owner)) is BitmapImage bitmap)
        { var task = bitmap.PreloadAsync(200, 150); PumpUntil(() => task.IsCompleted); Assert.True(task.IsCompletedSuccessfully); }
        Layout(owner);
    }
    private static void Layout(FrameworkElement owner)
    { for (var i = 0; i < 5; i++) { Flush(); owner.Measure(new(500, 1000)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); } }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void PumpUntil(Func<bool> condition)
    { var end = DateTime.UtcNow.AddSeconds(10); while (!condition() && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); } Assert.True(condition()); Flush(); }
    private static Color Sample(FrameworkElement owner, int x, int y)
    {
        var width = (int)owner.ActualWidth; var height = (int)owner.ActualHeight;
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(target);
        owner.Render(dc); dc.Close(); var pixels = target.GetPixelBuffer(); var at = (y * width + x) * 4;
        return Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]);
    }
    private static byte[] Nine()
    {
        Color[] colors = [Colors.Red, Colors.Lime, Colors.Blue, Colors.Cyan, Colors.Purple, Colors.Yellow, Colors.White, Colors.Black, Colors.Magenta];
        var pixels = new byte[30 * 30 * 4];
        for (var y = 0; y < 30; y++) for (var x = 0; x < 30; x++)
        { var c = colors[(y / 10) * 3 + x / 10]; var at = (y * 30 + x) * 4; pixels[at] = c.B; pixels[at + 1] = c.G; pixels[at + 2] = c.R; pixels[at + 3] = c.A; }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, pixels, 120)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static byte[] Gif(){using var output=new MemoryStream();using var writer=new BinaryWriter(output);writer.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));writer.Write((ushort)4);writer.Write((ushort)1);writer.Write(new byte[]{0x80,0,0,114,180,149,0,0,0});foreach(var samples in new[]{new[]{0,0,1,1},new[]{1,1,0,0}}){writer.Write(new byte[]{0x21,0xf9,4,9,1,0,1,0,0x2c,0,0,0,0,4,0,1,0,0,2,4});uint packed=0;var bits=0;foreach(var value in samples){packed|=(uint)4<<bits;bits+=3;packed|=(uint)value<<bits;bits+=3;}packed|=(uint)5<<bits;writer.Write(packed);writer.Write((byte)0);}writer.Write((byte)0x3b);return output.ToArray();}

    private sealed class Images(bool gated = false) : ICssResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, byte[]> _bytes = [];
        internal void Add(string name, byte[] bytes) => _bytes[name] = bytes;
        internal void Open() => _gate.TrySetResult();
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
        { if (gated) await _gate.Task; return new(uri, new MemoryStream(_bytes.GetValueOrDefault(Path.GetFileName(uri.LocalPath), Path.GetFileName(uri.LocalPath) == "bad.png" ? [1, 2, 3] : Nine()))); }
    }
}
