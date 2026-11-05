using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssImageIntrinsicTests
{
    [Theory]
    [InlineData("viewBox='0 0 100 100'", 150, 150)]
    [InlineData("width='40'", 40, 150)]
    [InlineData("height='20'", 300, 20)]
    [InlineData("", 300, 150)]
    [InlineData("viewBox='0 0 24 12'", 300, 150)]
    [InlineData("width='40' viewBox='0 0 24 12'", 40, 20)]
    [InlineData("height='20' viewBox='0 0 24 12'", 40, 20)]
    [InlineData("width='40' height='20'", 40, 20)]
    [InlineData("width='100%' height='50%' viewBox='0 0 24 12'", 300, 150)]
    [InlineData("width='100%' height='50%'", 300, 150)]
    [InlineData("width='0' height='20'", 0, 20)]
    [InlineData("width='0'", 0, 150)]
    [InlineData("width='0' viewBox='0 0 24 12'", 0, 0)]
    [InlineData("height='0' viewBox='0 0 24 12'", 0, 0)]
    public void GeneratedContentNegotiatesMissingNaturalDimensions(string attributes, double width, double height)
    {
        var owner = Content(attributes);
        var image = Assert.IsType<CssGeneratedImage>(CssReplacedContent.Image(owner));
        Assert.True(image.Valid); Assert.Equal(new Size(width, height), image.NaturalSize);
    }

    [Theory]
    [InlineData("viewBox='0 0 24 12'", "width:100px;", 100, 50)]
    [InlineData("viewBox='0 0 24 12'", "height:30px;", 60, 30)]
    [InlineData("viewBox='0 0 24 12'", "width:100px;height:30px;", 100, 30)]
    [InlineData("width='40'", "height:50px;", 40, 50)]
    [InlineData("height='20'", "width:100px;", 100, 20)]
    [InlineData("width='40' height='20'", "width:120px;aspect-ratio:3;", 120, 40)]
    [InlineData("width='40' height='20'", "width:120px;aspect-ratio:auto 3;", 120, 60)]
    [InlineData("", "width:120px;aspect-ratio:auto 3;", 120, 40)]
    [InlineData("height='20'", "aspect-ratio:3;", 60, 20)]
    [InlineData("viewBox='0 0 24 24'", "aspect-ratio:3;", 300, 100)]
    [InlineData("viewBox='0 0 24 12'", "box-sizing:content-box;width:100px;padding:5px;border:2px solid red;", 114, 64)]
    [InlineData("viewBox='0 0 24 12'", "box-sizing:border-box;width:100px;padding:5px;border:2px solid red;", 100, 57)]
    [InlineData("viewBox='0 0 24 12'", "box-sizing:border-box;width:100px;aspect-ratio:2;padding:5px;border:2px solid red;", 100, 50)]
    [InlineData("width='40' height='20'", "min-width:100px;", 100, 50)]
    [InlineData("width='40' height='20'", "max-width:20px;", 20, 10)]
    [InlineData("width='40' height='20'", "max-height:10px;", 20, 10)]
    [InlineData("width='40' height='20'", "min-width:100px;max-height:30px;", 100, 30)]
    public void GeneratedAndNativeCssImagesShareRatioAndBoxSizing(string attributes, string style, double width, double height)
    {
        var owner = Content(attributes, style);
        Assert.Equal(width, owner.DesiredSize.Width, 4); Assert.Equal(height, owner.DesiredSize.Height, 4);
        using var svg = SvgImage.FromSvgString(Svg(attributes));
        var native = new Image { Source = svg }; Css.SetStyle(native, "object-fit:fill;" + style); Flush(); Layout(native);
        Assert.Equal(width, native.DesiredSize.Width, 4); Assert.Equal(height, native.DesiredSize.Height, 4);
        Assert.Equal(width, native.ActualWidth, 4); Assert.Equal(height, native.ActualHeight, 4);
    }

    [Theory]
    [InlineData("viewBox='0 0 24 12'", "none", 200, 100)]
    [InlineData("viewBox='0 0 24 12'", "scale-down", 200, 100)]
    [InlineData("width='40'", "none", 40, 100)]
    [InlineData("height='20'", "none", 200, 20)]
    [InlineData("width='40'", "contain", 200, 100)]
    [InlineData("height='20'", "cover", 200, 100)]
    [InlineData("width='40'", "scale-down", 40, 100)]
    [InlineData("width='40' height='20'", "none", 40, 20)]
    [InlineData("width='40' height='20'", "scale-down", 40, 20)]
    [InlineData("", "none", 200, 100)]
    [InlineData("viewBox='0 0 24 24'", "contain", 100, 100)]
    public void ObjectFitUsesActualNaturalDimensionsRatherThanACompletedFallback(string attributes, string fit, double width, double height)
    {
        foreach (var recorded in new[] { false, true })
        {
            using var svg = SvgImage.FromSvgString(Svg(attributes));
            var native = new Image { Source = svg, Width = 200, Height = 100 };
            Css.SetStyle(native, "object-fit:" + fit + ";object-position:left top;image-rendering:pixelated;"); Flush(); Layout(native);
            var pixels = Paint(native, 200, 100, recorded);
            AssertRectangle(pixels, 200, 100, new(0, 0, width, height));
            var owner = Content(attributes, "width:200px;height:100px;object-fit:" + fit + ";object-position:left top;image-rendering:pixelated;");
            AssertRectangle(Paint(owner, 200, 100, recorded), 200, 100, new(0, 0, width, height));
        }
    }

    [Fact]
    public void LegacyNativeImageSizeReturnsWhenCssObjectFitIsRemoved()
    {
        using var svg = SvgImage.FromSvgString(Svg("viewBox='0 0 24 12'")); var image = new Image { Source = svg };
        Assert.True(image.TryGetSourceSize(out _, out var legacy)); Assert.Equal(new Size(24, 12), legacy);
        Css.SetStyle(image, "object-fit:fill;"); Flush();
        Assert.True(image.TryGetSourceSize(out _, out var css)); Assert.Equal(new Size(300, 150), css);
        Css.SetStyle(image, ""); Flush(); Assert.True(image.TryGetSourceSize(out _, out legacy)); Assert.Equal(new Size(24, 12), legacy);
    }

    [Theory]
    [InlineData(200, 100, 30, 60)] [InlineData(300, 150, 30, 85)] [InlineData(20, 20, 30, 20)]
    public void CrossFadeNegotiatesPartialSourcesInTheConsumerContext(double w, double h, double expectedW, double expectedH)
    {
        using var svg = SvgImage.FromCssSvgString(Svg("width='40'"));
        var bitmap = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, new byte[1600], 80);
        var layout = new CssCrossFadeLayout([new(new ImageBrush(svg), 1, .5), new(new ImageBrush(bitmap), 1, .5)], [], 1);
        Assert.Equal(new Size(expectedW, expectedH), layout.IntrinsicSize(new(w, h)).CompleteSize);
    }

    [Fact]
    public void CrossFadeIgnoresInkBoundsAndColorsForNaturalSizing()
    {
        using var svg = SvgImage.FromCssSvgString("<svg xmlns='http://www.w3.org/2000/svg'><rect width='60' height='30'/></svg>");
        var bitmap = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, new byte[1600], 80);
        var layout = new CssCrossFadeLayout([new(new ImageBrush(svg), 1, .25), new(new ImageBrush(bitmap), 1, .25), new(Brushes.Red, 1, .5)], [], 1);
        Assert.Equal(new Size(20, 20), layout.NaturalSize);
    }

    [Theory]
    [InlineData("width='40'", 40, 100, 5, 10)]
    [InlineData("height='20'", 200, 20, 10, 5)]
    [InlineData("width='40' height='20'", 40, 20, 5, 5)]
    [InlineData("viewBox='0 0 24 24'", 100, 100, 10, 10)]
    [InlineData("width='40' viewBox='0 0 24 12'", 40, 20, 5, 10)]
    public void BorderAutoWidthsDistinguishKnownAndMissingAxes(string attributes, double w, double h, double left, double top)
    {
        using var svg = SvgImage.FromCssSvgString(Svg(attributes)); var owner = new Border();
        var source = new CssBorderImageSource(new ImageBrush(svg), 1, []);
        var resolved = source.Resolve(owner, new(200, 100)); Assert.NotNull(resolved); Assert.Equal(new Size(w, h), resolved.Value.Pixels);
        var slices = CssBorderImageMeasures.Uniform(5, CssUnit.None, number: true);
        var widths = new CssBorderImageMeasures(Enumerable.Repeat(new CssBorderImageMeasure(default, Auto: true), 4).ToArray(), default);
        var plan = CssBorderImagePainter.Plan(owner, new(0, 0, 200, 100), new Thickness(10), source, slices, widths, default, new(CssBorderImageRepeat.Stretch, CssBorderImageRepeat.Stretch));
        Assert.NotNull(plan); Assert.Equal(new Rect(left, top, 200 - 2 * left, 100 - 2 * top), plan.Middle);
    }

    [Theory] [InlineData("viewBox='0 0 24 24'", 150, 150)] [InlineData("width='40'", 40, 150)]
    public void ImageAnnotationsRetainPartialMetadata(string attributes, double w, double h)
    {
        var owner = Content(attributes, image: "image(url(\"$svg\"))");
        Assert.Equal(new Size(w, h), CssReplacedContent.Image(owner)!.NaturalSize);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SvgFragmentIsCroppedBeforeTheNonuniformOutputScale(bool recorded)
    {
        const string body = "<rect width='50' height='100' fill='red'/><rect x='50' width='50' height='100' fill='blue'/>";
        var owner = Content("width='100' height='100' viewBox='0 0 100 100'", "width:200px;height:100px;image-rendering:pixelated;",
            "image(url(\"$svg#xywh=50,0,50,100\"))", body);
        var p = Paint(owner, 200, 100, recorded);
        for (var i = 0; i < p.Length; i += 4) { Assert.Equal(255, p[i]); Assert.Equal(255, p[i + 3]); }
    }

    [Theory] [InlineData("width='0' height='20'")] [InlineData("width='40' height='0'")] [InlineData("width='0' height='0'")]
    public void AConcreteCssSizeCanOverrideZeroNaturalLengths(string attributes)
    {
        using var svg = SvgImage.FromSvgString(Svg(attributes)); var image = new Image { Source = svg };
        Css.SetStyle(image, "object-fit:fill;width:200px;height:100px;image-rendering:pixelated;"); Flush(); Layout(image);
        AssertRectangle(Paint(image, 200, 100, false), 200, 100, new(0, 0, 200, 100));
        var owner = Content(attributes, "width:200px;height:100px;image-rendering:pixelated;");
        AssertRectangle(Paint(owner, 200, 100, true), 200, 100, new(0, 0, 200, 100));
    }

    [Theory] [InlineData("width='0' height='20'")] [InlineData("width='40' height='0'")]
    public void MirroringTheWholeImageDoesNotDivideByAZeroNaturalLength(string attributes)
    {
        var owner = Content(attributes, "width:200px;height:100px;image-rendering:pixelated;", "image(rtl url(\"$svg\"))");
        AssertRectangle(Paint(owner, 200, 100, true), 200, 100, new(0, 0, 200, 100));
    }

    [Theory]
    [InlineData("", 0, 0)] [InlineData("aspect-ratio:3;", 0, 0)]
    [InlineData("width:120px;aspect-ratio:3;", 120, 40)] [InlineData("width:120px;height:30px;", 120, 30)]
    public void FailedSourcesRetainOnlyAuthoredBoxConstraints(string style, double width, double height)
    {
        var owner = Content("", style, "url('data:image/svg+xml,%3Csvg')");
        Assert.False(CssReplacedContent.Image(owner)!.Valid);
        Assert.Equal(width, owner.DesiredSize.Width, 4); Assert.Equal(height, owner.DesiredSize.Height, 4);
        using var svg = SvgImage.FromSvgString("<svg"); var image = new Image { Source = svg };
        Css.SetStyle(image, "object-fit:fill;" + style); Flush(); Layout(image);
        Assert.Equal(width, image.DesiredSize.Width, 4); Assert.Equal(height, image.DesiredSize.Height, 4);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ReloadingAnEqualDefaultSizeStillAnnouncesChangedNaturalMetadata(bool fixedFirst)
    {
        var directory = Path.Combine(Path.GetTempPath(), "jalium-css-intrinsic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.svg"); var second = Path.Combine(directory, "second.svg");
            var fixedSize = "width='300' height='150'";
            File.WriteAllText(first, Svg(fixedFirst ? fixedSize : ""));
            File.WriteAllText(second, Svg(fixedFirst ? "" : fixedSize));
            using var svg = SvgImage.FromFile(first); var image = new Image { Source = svg };
            Css.SetStyle(image, "object-fit:fill;aspect-ratio:auto 3;"); Flush(); Layout(image);
            svg.UriSource = new Uri(first); Layout(image); // Establish the first publication input.
            Assert.Equal(new Size(300, fixedFirst ? 150 : 100), image.DesiredSize);
            Assert.True(image.IsMeasureValid);
            svg.UriSource = new Uri(second);
            Assert.False(image.IsMeasureValid);
            Layout(image); Assert.Equal(new Size(300, fixedFirst ? 100 : 150), image.DesiredSize);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BorderImageUsesItsAreaAsTheDefaultObjectSize()
    {
        using var svg = SvgImage.FromCssSvgString(Svg("viewBox='0 0 24 24'"));
        var source = new CssBorderImageSource(new ImageBrush(svg), 1, []);
        var resolved = source.Resolve(new Border(), new(200, 100));
        Assert.NotNull(resolved); Assert.Equal(new Size(100, 100), resolved.Value.Pixels);
    }

    private static TextBlock Content(string attributes, string style = "", string? image = null, string? body = null)
    {
        var owner = new TextBlock { Text = "正文" }; Css.SetClass(owner, "owner");
        var uri = "data:image/svg+xml," + Uri.EscapeDataString(body is null ? Svg(attributes) : "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" + body + "</svg>");
        var value = image?.Replace("$svg", uri) ?? "url(\"" + uri + "\")";
        var sheet = CssStyleSheet.Parse(".owner{content:" + value + ";" + style + "}"); Assert.Empty(sheet.Diagnostics);
        Css.GetStyleSheets(owner).Add(sheet);
        Flush(); Layout(owner);
        var generated = Assert.IsType<CssGeneratedImage>(CssReplacedContent.Image(owner));
        var end = DateTime.UtcNow.AddSeconds(10);
        while (generated.ActiveResources.Any(resource => !resource.Ready.IsCompleted) && DateTime.UtcNow < end)
        { Flush(); Thread.Sleep(1); }
        Assert.All(generated.ActiveResources, resource => Assert.True(resource.Ready.IsCompletedSuccessfully));
        Flush(); Layout(owner); return owner;
    }
    private static string Svg(string attributes) => "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + "><rect width='100%' height='100%' fill='#146342'/></svg>";
    private static void Layout(FrameworkElement root)
    { root.Measure(new(600, 400)); root.Arrange(new(0, 0, root.DesiredSize.Width, root.DesiredSize.Height)); root.UpdateLayout(); }
    private static void Flush()
    { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }

    private static byte[] Paint(Visual root, int width, int height, bool recorded)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32); var dc = new SoftwareDrawingContext(bitmap);
        if (recorded) { var recorder = new DrawingRecorder(); ((UIElement)root).Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
        else ((UIElement)root).Render(dc);
        dc.Close(); return bitmap.GetPixelBuffer();
    }
    private static void AssertRectangle(byte[] pixels, int width, int height, Rect rect)
    {
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            Assert.Equal(rect.Contains(new Point(x + .5, y + .5)) ? (byte)255 : (byte)0, pixels[(y * width + x) * 4 + 3]);
    }
}
