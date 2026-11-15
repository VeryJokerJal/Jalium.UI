using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgViewportTests
{
    public static IEnumerable<object[]> ViewBoxMappings()
    {
        foreach (var x in new[] { "Min", "Mid", "Max" })
        foreach (var y in new[] { "Min", "Mid", "Max" })
        foreach (var slice in new[] { false, true })
        foreach (var portrait in new[] { false, true })
        foreach (var recorded in new[] { false, true })
            yield return [x, y, slice, portrait, recorded];
    }

    [Theory] [MemberData(nameof(ViewBoxMappings))]
    public void ImagePreservesAllViewBoxAlignments(string x, string y, bool slice, bool portrait, bool recorded)
    {
        var width = portrait ? 100 : 200; var height = portrait ? 200 : 100;
        using var svg = SvgImage.FromSvgString(Svg("<rect x='15' y='25' width='20' height='20' fill='red'/>",
            "width='100' height='100' viewBox='5 5 100 100' preserveAspectRatio='x" + x + "Y" + y + (slice ? " slice'" : " meet'")));
        var scale = slice ? 2 : 1;
        var left = 10 * scale + Align(x, width - 100 * scale);
        var top = 20 * scale + Align(y, height - 100 * scale);
        var pixels = PaintImage(svg, width, height, recorded);
        for (var py = 0; py < height; py++) for (var px = 0; px < width; px++)
        {
            var inside = px >= left && px < left + 20 * scale && py >= top && py < top + 20 * scale;
            Assert.Equal(inside ? (byte)255 : (byte)0, pixels[(py * width + px) * 4 + 3]);
        }
    }

    [Theory]
    [InlineData("", 10, 20, 20, 20)]
    [InlineData("width='100%' height='100%'", 10, 20, 20, 20)]
    [InlineData("width='50%' height='100%'", 20, 20, 40, 20)]
    [InlineData("width='100' height='100'", 20, 20, 40, 20)]
    [InlineData("width='100'", 20, 20, 40, 20)]
    [InlineData("style='width:100px;height:50px'", 20, 40, 40, 40)]
    public void RootWithoutViewBoxDistinguishesFixedAndFluidUserSpaces(string attributes, int x, int y, int w, int h)
    {
        using var svg = SvgImage.FromSvgString(Svg("<rect x='10' y='20' width='20' height='20' fill='red'/>", attributes));
        AssertRectangle(PaintImage(svg, 200, 100), 200, 100, new(x, y, w, h));
    }

    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("viewBox='0 0 40 20'", 0, 0, 2)]
    [InlineData("width='80' viewBox='0 0 40 20'", 80, 0, 2)]
    [InlineData("height='20' viewBox='0 0 40 20'", 0, 20, 2)]
    [InlineData("width='80' height='20' viewBox='0 0 40 20'", 80, 20, 4)]
    [InlineData("width='100%' height='50%' viewBox='0 0 40 20'", 0, 0, 2)]
    [InlineData("style='width:calc(20px + 20px);height:20px'", 40, 20, 2)]
    [InlineData("style='--w:40px;width:var(--w);height:20px'", 40, 20, 2)]
    [InlineData("width='1in' height='36pt'", 96, 48, 2)]
    [InlineData("style='font-size:10px;width:4em;height:2em'", 40, 20, 2)]
    [InlineData("width='80' style='width:auto' height='20'", 0, 20, 0)]
    public void CssNaturalDimensionsDoNotUseViewBoxOrInkAsAbsoluteLengths(string attributes, double w, double h, double ratio)
    {
        using var svg = SvgImage.FromCssSvgString(Svg("<rect width='12' height='14'/>", attributes));
        Assert.Equal(new SvgIntrinsicDimensions(w, h, ratio), svg.CssIntrinsic);
        _ = svg.ImageForViewport(new(200, 100));
        Assert.Equal(new SvgIntrinsicDimensions(w, h, ratio), svg.CssIntrinsic);
    }

    [Theory]
    [InlineData("", "auto", 200, 100)]
    [InlineData("viewBox='0 0 100 100'", "auto", 100, 100)]
    [InlineData("width='40' viewBox='0 0 100 100'", "auto", 40, 40)]
    [InlineData("height='20' viewBox='0 0 100 100'", "auto", 20, 20)]
    [InlineData("width='40'", "auto", 40, 100)]
    [InlineData("height='20'", "auto", 200, 20)]
    [InlineData("width='40' height='20'", "auto", 40, 20)]
    [InlineData("width='100%' height='50%' viewBox='0 0 100 100'", "auto", 100, 100)]
    [InlineData("viewBox='0 0 100 100'", "contain", 100, 100)]
    [InlineData("viewBox='0 0 100 100'", "cover", 200, 200)]
    [InlineData("width='40'", "contain", 200, 100)]
    [InlineData("width='40'", "cover", 200, 100)]
    [InlineData("viewBox='0 0 100 100'", "80px auto", 80, 80)]
    [InlineData("height='20'", "80px auto", 80, 20)]
    [InlineData("width='40'", "auto 80px", 40, 80)]
    [InlineData("", "80px auto", 80, 100)]
    public void BackgroundSizingUsesPartialNaturalDimensionsAndRatio(string attributes, string size, double w, double h)
    {
        var owner = new Border { Width = 200, Height = 100 };
        var xml = Svg("<rect width='100%' height='100%' fill='red'/>", attributes);
        var sheet = CssStyleSheet.Parse("Border{background-image:url(\"data:image/svg+xml," + Uri.EscapeDataString(xml) + "\");background-size:" + size + ";background-repeat:no-repeat}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Layout(owner, 200, 100); Ready(owner);
        var brush = Assert.IsType<ImageBrush>(owner.Background);
        Assert.Equal(new Size(w, h), brush.CssBackgroundLayout!.ImageRect(new(0, 0, 200, 100), brush.ImageSource!).Size);
    }

    [Theory] [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 2)] [InlineData(true, 2)]
    public void MediaAndViewportUnitsUseLogicalImageSize(bool recorded, int dpi)
    {
        using var svg = SvgImage.FromCssSvgString(Svg("<style>rect{fill:red;width:10vw;height:20vh}@media(min-width:150px) and (orientation:landscape){rect{fill:blue}}</style><rect/>", "width='100' height='100' viewBox='0 0 100 100' preserveAspectRatio='none'"));
        foreach (var width in new[] { 100, 200, 100 })
        {
            var image = svg.ImageForViewport(new(width, 100));
            var pixels = Paint(image, width, 100, recorded, dpi);
            // Viewport units use CSS pixels before the viewBox scale.
            var at = ((5 * dpi) * (width * dpi) + 5 * dpi) * 4;
            Assert.Equal(255, pixels[at + (width == 200 ? 0 : 2)]);
            Assert.Equal(0, pixels[at + (width == 200 ? 2 : 0)]);
            var edge = width == 200 ? 40 : 10;
            Assert.Equal(0, pixels[(5 * dpi * width * dpi + (edge + 2) * dpi) * 4 + 3]);
        }
    }

    [Fact]
    public void ViewportCacheIsBoundedAndDrawingMutationInvalidatesMarkup()
    {
        using var svg = SvgImage.FromSvgString(Svg("<rect width='20' height='20' fill='red'/>", "width='100' height='100'"));
        var first = svg.ImageForViewport(new(200, 100));
        Assert.Same(first, svg.ImageForViewport(new(200, 100)));
        for (var i = 1; i <= 4; i++) _ = svg.ImageForViewport(new(200 + i, 100));
        Assert.NotSame(first, svg.ImageForViewport(new(200, 100)));
        Assert.Equal(100, svg.Width); Assert.Equal(100, svg.Height);
        Assert.Same(svg, svg.ImageForViewport(new(0, 100)));
        var group = Assert.IsType<DrawingGroup>(svg.Drawing);
        group.Children.Clear(); group.Children.Add(new GeometryDrawing(Brushes.Blue, null, new RectangleGeometry(new(0, 0, 100, 100))));
        Assert.Same(svg, svg.ImageForViewport(new(200, 100)));
        var pixels = PaintImage(svg, 200, 100); Assert.Equal(255, pixels[(50 * 200 + 100) * 4]);
    }

    [Fact]
    public void ViewportPlansBelongToTheirDispatcherAndForwardDrawOwnership()
    {
        using var svg = SvgImage.FromSvgString(Svg("<rect width='100%' height='100%' fill='red'/>", "width='100' height='100'"));
        var own = svg.ImageForViewport(new(200, 100));
        ImageSource? other = null; Exception? error = null;
        var thread = new Thread(() => { try { other = svg.ImageForViewport(new(200, 100)); } catch (Exception ex) { error = ex; } });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10))); Assert.Null(error); Assert.NotNull(other); Assert.NotSame(own, other);
        Assert.Equal(0, svg.LastDrawnTickMs); _ = Paint(own, 200, 100); Assert.True(svg.LastDrawnTickMs > 0);
        var clone = Assert.IsType<DrawingImage>(own.Clone()); Assert.Same(svg, clone.DrawOwner);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ConcreteViewportClipsOverflowAndKeepsWhitespace(bool recorded)
    {
        using var svg = SvgImage.FromSvgString(Svg("<rect x='-100' y='-100' width='500' height='500' fill='red'/>", "width='100' height='100' viewBox='0 0 100 100'"));
        var plan = Assert.IsType<DrawingImage>(svg.ImageForViewport(new(200, 100)));
        Assert.Equal(new Rect(0, 0, 200, 100), plan.Drawing!.Bounds);
        var bitmap = new RenderTargetBitmap(240, 140, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        void Draw(DrawingContext c) => c.DrawImage(plan, new(20, 20, 200, 100), BitmapScalingMode.NearestNeighbor);
        if (recorded) { var recorder = new DrawingRecorder(); Draw(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Draw(dc);
        dc.Close(); AssertRectangle(bitmap.GetPixelBuffer(), 240, 140, new(20, 20, 200, 100));
    }

    [Theory]
    [InlineData(false, false, 1)] [InlineData(false, true, 1)]
    [InlineData(true, false, 1)] [InlineData(true, true, 1)]
    [InlineData(false, false, 2)] [InlineData(false, true, 2)]
    [InlineData(true, false, 2)] [InlineData(true, true, 2)]
    public void CroppedBrushRetainsFractionalSourceCoordinates(bool css, bool recorded, int dpi)
    {
        var xml = Svg("<rect x='10.25' width='10.25' height='10.5' fill='red'/>", "width='20.5' height='10.5'");
        using var svg = css ? SvgImage.FromCssSvgString(xml) : SvgImage.FromSvgString(xml);
        var brush = new ImageBrush(svg) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new(10.25, 0, 10.25, 10.5), ScalingMode = BitmapScalingMode.NearestNeighbor };
        var bitmap = new RenderTargetBitmap(100 * dpi, 50 * dpi, 96 * dpi, 96 * dpi, PixelFormats.Bgra32);
        var dc = new SoftwareDrawingContext(bitmap);
        void Draw(DrawingContext c) => c.DrawRectangle(brush, null, new(0, 0, 100, 50));
        if (recorded) { var recorder = new DrawingRecorder(); Draw(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Draw(dc);
        dc.Close(); AssertRectangle(bitmap.GetPixelBuffer(), 100 * dpi, 50 * dpi, new(0, 0, 100 * dpi, 50 * dpi));
    }

    private static string Svg(string body, string attributes) => "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" + body + "</svg>";
    private static double Align(string alignment, double remaining) => alignment == "Min" ? 0 : alignment == "Mid" ? remaining / 2 : remaining;
    private static void Layout(FrameworkElement owner, int width, int height)
    { owner.Measure(new(width, height)); owner.Arrange(new(0, 0, width, height)); owner.UpdateLayout(); }
    private static byte[] PaintImage(SvgImage source, int width, int height, bool recorded = false)
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        Layout(image, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        if (recorded) { var recorder = new DrawingRecorder(); image.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else image.Render(dc);
        dc.Close(); return bitmap.GetPixelBuffer();
    }
    private static byte[] Paint(ImageSource source, int width, int height, bool recorded = false, int dpi = 1)
    {
        var bitmap = new RenderTargetBitmap(width * dpi, height * dpi, 96 * dpi, 96 * dpi, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        void Draw(DrawingContext c) => c.DrawImage(source, new(0, 0, width, height), BitmapScalingMode.NearestNeighbor);
        if (recorded) { var recorder = new DrawingRecorder(); Draw(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); } else Draw(dc);
        dc.Close(); return bitmap.GetPixelBuffer();
    }
    private static void AssertRectangle(byte[] pixels, int width, int height, Rect rectangle)
    {
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            Assert.Equal(rectangle.Contains(new Point(x + .5, y + .5)) ? (byte)255 : (byte)0, pixels[(y * width + x) * 4 + 3]);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ImageElementRebuildsViewBoxForItsConcreteSize(bool recorded)
    {
        using var svg=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 100 100'><rect x='10' y='20' width='20' height='20' fill='red'/></svg>");
        var image=new Image{Source=svg,Width=200,Height=100,Stretch=Stretch.Fill};
        image.Measure(new(200,100));image.Arrange(new(0,0,200,100));image.UpdateLayout();
        var bitmap=new RenderTargetBitmap(200,100,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(bitmap);
        if(recorded){var recorder=new DrawingRecorder();image.Render(recorder);DrawingReplayer.Replay(recorder.Commit(),context);}else image.Render(context);
        context.Close();var p=bitmap.GetPixelBuffer();Assert.Equal(0,p[(25*200+25)*4+3]);Assert.Equal(255,p[(25*200+65)*4+3]);
    }
    [Theory] [InlineData(100)] [InlineData(200)]
    public void CssBackgroundMediaUsesTheConcreteImageViewport(int width)
    {
        const string xml="<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'><style>rect{fill:red}@media(min-width:150px){rect{fill:blue}}</style><rect width='100%' height='100%'/></svg>";
        var box=new Border{Width=width,Height=100};Css.GetStyleSheets(box).Add(CssStyleSheet.Parse("Border{background-image:url(\"data:image/svg+xml,"+Uri.EscapeDataString(xml)+"\");background-size:"+width+"px 100px;background-repeat:no-repeat}"));
        box.Measure(new(width,100));box.Arrange(new(0,0,width,100));box.UpdateLayout();
        Ready(box);
        var bitmap=new RenderTargetBitmap(width,100,96,96,PixelFormats.Bgra32);bitmap.Render(box);
        var p=bitmap.GetPixelBuffer();var at=(50*width+width/2)*4;Assert.Equal(255,p[at+(width==200?0:2)]);Assert.Equal(0,p[at+(width==200?2:0)]);
    }
    private static void Ready(FrameworkElement owner)
    {
        var resources = Assert.IsType<CssBackgroundImageResource[]>(owner.GetValue(CssBackgroundImageResource.ActiveProperty));
        var end = DateTime.UtcNow.AddSeconds(10);
        while (resources.Any(resource => !resource.Ready.IsCompleted) && DateTime.UtcNow < end)
        { Dispatcher.CurrentDispatcher.ProcessQueue(); Thread.Sleep(1); }
        Assert.All(resources, resource => Assert.True(resource.Ready.IsCompletedSuccessfully));
        Dispatcher.CurrentDispatcher.ProcessQueue();
        BitmapDecodeNotifier.FlushPendingForTests();
        CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
    }
    [Fact]
    public void DirectDrawImageRetainsIntrinsicCanvasSizing()
    {
        using var svg=SvgImage.FromSvgString("<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100' viewBox='0 0 100 100'><style>@media(min-width:150px){rect{fill:blue}}</style><rect x='10' y='20' width='20' height='20' fill='red'/></svg>");
        var bitmap=new RenderTargetBitmap(200,100,96,96,PixelFormats.Bgra32);var context=new SoftwareDrawingContext(bitmap);context.DrawImage(svg,new Rect(0,0,200,100),BitmapScalingMode.NearestNeighbor);context.Close();var p=bitmap.GetPixelBuffer();Assert.Equal(255,p[(25*200+25)*4+2]);Assert.Equal(0,p[(25*200+65)*4+3]);
    }
}
