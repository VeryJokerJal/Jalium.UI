using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssShapeImageSizingTests
{
    [Theory]
    [InlineData("viewBox='0 0 24 24'", 100, 100)]
    [InlineData("viewBox='0 0 24 12'", 200, 100)]
    [InlineData("width='40'", 40, 100)]
    [InlineData("height='20'", 200, 20)]
    [InlineData("", 200, 100)]
    [InlineData("width='0' height='20'", 0, 20)]
    [InlineData("width='0' viewBox='0 0 24 12'", 0, 0)]
    [InlineData("width='40' height='20'", 40, 20)]
    public void ShapeMetadataKeepsAbsentAndZeroNaturalLengths(string attributes, double width, double height)
    {
        var owner = Owner(Url(Svg(attributes)));
        var pixels = Value(owner).ResolvePixels(new(200, 100));
        Assert.NotNull(pixels); Assert.Equal(new Size(width, height), pixels.NaturalSize);
    }

    [Theory]
    [InlineData("", 5000, 200)] [InlineData("width='40'", 40, 200)]
    [InlineData("height='20'", 5000, 20)] [InlineData("viewBox='0 0 24 24'", 200, 200)]
    [InlineData("viewBox='0 0 24 12'", 400, 200)]
    public void RasterBudgetDoesNotReplaceTheLogicalDefaultObjectSize(string attributes, double width, double height)
    {
        var owner = Owner(Url(Svg(attributes))); var pixels = Value(owner).ResolvePixels(new(5000, 200));
        Assert.NotNull(pixels); Assert.InRange(pixels.Width, 1, 2048); Assert.InRange(pixels.Height, 1, 2048);
        Assert.Equal(new Size(width, height), pixels.NaturalSize);
    }

    [Fact]
    public void RasterBudgetKeepsGeneratedImagesDimensionlessAtTheirLogicalSize()
    {
        var owner = Owner("linear-gradient(black,black)"); var pixels = Value(owner).ResolvePixels(new(5000, 200));
        Assert.NotNull(pixels); Assert.False(pixels.Intrinsic.HasAny); Assert.Equal(new Size(5000, 200), pixels.NaturalSize);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SvgFragmentIsSelectedBeforeNonuniformOutputSizing(bool percent)
    {
        var svg = Svg("width='100' height='100' viewBox='0 0 100 100'", "<rect x='50' width='50' height='100' fill='black'/>");
        var owner = Owner("image(url(\"" + UriFor(svg) + (percent ? "#xywh=percent:50,0,50,100" : "#xywh=50,0,50,100") + "\"))");
        var pixels = Value(owner).ResolvePixels(new(200, 100)); Assert.NotNull(pixels);
        for (var y = 0; y < 100; y++) for (var x = 0; x < 200; x++) Assert.Equal(1, pixels.Alpha(x, y, 200, 100));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SvgShapeViewportMatchesTheImagesConcreteObjectRectangle(bool hasDisplaySource)
    {
        var xml = Svg("viewBox='0 0 24 24'");
        using var source = SvgImage.FromSvgString(xml);
        var image = new Image { Source = hasDisplaySource ? source : null };
        Apply(image, Url(xml), "object-fit:contain;");
        var shape = CssShapeOutsideProperties.Resolve(image, new(200, 100), default, 200);
        Assert.NotNull(shape); var band = shape.Band(20, 20);
        Assert.Equal(50, band.Left, 4); Assert.Equal(150, band.Right, 4);
    }

    [Theory]
    [InlineData(200, 100, 30, 60)] [InlineData(300, 150, 30, 85)]
    public void CrossFadeShapeNegotiatesPartialNaturalDimensionsInItsContext(double w, double h, double expectedW, double expectedH)
    {
        var owner = Owner("cross-fade(" + Url(Svg("width='40'")) + " 50%," + Url(Svg("width='20' height='20'")) + " 50%)");
        Assert.Equal(new Size(expectedW, expectedH), Value(owner).ResolvePixels(new(w, h))!.NaturalSize);
    }

    [Theory]
    [InlineData("width='0' height='20'")] [InlineData("width='40' height='0'")]
    [InlineData("width='0' height='0'")]
    public void WholeImageMirroringPreservesRealZeroMetadataWithoutDividingByIt(string attributes)
    {
        var owner = Owner("image(rtl " + Url(Svg(attributes)) + ")");
        var pixels = Value(owner).ResolvePixels(new(200, 100)); Assert.NotNull(pixels);
        for (var y = 0; y < 100; y++) for (var x = 0; x < 200; x++) Assert.Equal(1, pixels.Alpha(x, y, 200, 100));
        Assert.True(pixels.Intrinsic.Width == 0 || pixels.Intrinsic.Height == 0);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ImageSetAppliesRasterDensityAfterFragmentCoordinatesAndLeavesVectorsUnscaled(bool vector)
    {
        var uri = vector ? UriFor(Svg("width='40' height='20'", "<rect x='20' width='20' height='20' fill='black'/>")) : BitmapUri(40, 20, right: true);
        var owner = Owner("image-set(image(url(\"" + uri + "#xywh=20,0,20,10\")) 2x)");
        var pixels = Value(owner).ResolvePixels(new(200, 100)); Assert.NotNull(pixels);
        Assert.Equal(vector ? new Size(20, 10) : new Size(10, 5), pixels.NaturalSize);
        Assert.Equal(1, pixels.Alpha(100, 50, 200, 100));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void SharedShapeResourceRetainsEachCandidatesOwnDensity(bool vector)
    {
        var uri = vector ? UriFor(Svg("width='40' height='20'")) : BitmapUri(40, 20, right: false);
        var source = "url(\"" + uri + "\")";
        var owner = Owner("cross-fade(image-set(" + source + " 1x) 50%,image-set(" + source + " 2x) 50%)");
        var value = Value(owner); Assert.Single(value.Resources);
        var pixels = value.ResolvePixels(new(200, 100)); Assert.NotNull(pixels);
        Assert.Equal(vector ? new Size(40, 20) : new Size(30, 15), pixels.NaturalSize);
        Assert.Same(pixels, value.ResolvePixels(new(200, 100)));
    }

    [Theory]
    [InlineData("", 20, 20)] [InlineData("width='0' height='20'", 10, 20)]
    public void CrossFadeDistinguishesMissingDimensionsColorsAndRealZeroAxes(string attributes, double width, double height)
    {
        var owner = Owner("cross-fade(" + Url(Svg(attributes)) + " 25%," + Url(Svg("width='20' height='20'")) + " 25%,black 50%)");
        Assert.Equal(new Size(width, height), Value(owner).ResolvePixels(new(200, 100))!.NaturalSize);
    }

    [Fact]
    public void AColorFallbackDoesNotContributeOnePixelNaturalDimensionsToCrossFade()
    {
        var owner = Owner("cross-fade(image(url('data:image/svg+xml,%3Csvg'),black)," + Url(Svg("width='20' height='20'")) + ")");
        Assert.Equal(new Size(20, 20), Value(owner).ResolvePixels(new(200, 100))!.NaturalSize);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void ImageViewportMediaIsReevaluatedAfterContainChoosesTheObjectSize(bool hasDisplaySource)
    {
        var xml = Svg("viewBox='0 0 24 24'", "<style>rect{x:0}@media(min-width:150px){rect{x:12}}</style><rect width='12' height='24' fill='black'/>");
        using var source = SvgImage.FromSvgString(xml); var owner = new Image { Source = hasDisplaySource ? source : null };
        Apply(owner, Url(xml), "object-fit:contain;");
        var band = CssShapeOutsideProperties.Resolve(owner, new(200, 100), default, 200)!.Band(20, 20);
        Assert.Equal((50d, 100d, true), band);
    }

    [Fact]
    public void ShapeImageCandidateSwitchKeepsItsCssSizeAndRebuildsItsAlphaContour()
    {
        var low = BitmapUri(40, 20, right: false); var high = BitmapUri(80, 40, right: true);
        var owner = new Image(); Apply(owner, "image-set(url(\"" + low + "\") 1x,url(\"" + high + "\") 2x)", "object-fit:none;object-position:left top;");
        var first = CssShapeOutsideProperties.Resolve(owner, new(200, 100), default, 200)!.Band(0, 20);
        Assert.Equal((0d, 20d, true), first);
        VisualTreeHelper.SetRootDpi(owner, new(2, 2)); Flush(); Wait(owner);
        Assert.Equal(new Size(40, 20), Value(owner).ResolvePixels(new(200, 100))!.NaturalSize);
        Assert.Equal((20d, 40d, true), CssShapeOutsideProperties.Resolve(owner, new(200, 100), default, 200)!.Band(0, 20));
        VisualTreeHelper.SetRootDpi(owner, new(1, 1)); Flush(); Wait(owner);
        Assert.Equal(first, CssShapeOutsideProperties.Resolve(owner, new(200, 100), default, 200)!.Band(0, 20));
    }

    [Theory] [InlineData(360)] [InlineData(720)]
    public void FragmentReflowPreservesSourceRunsSelectionAndTextHitLocations(int width)
    {
        var xml = Svg("width='100' height='100' viewBox='0 0 100 100'", "<rect x='50' width='50' height='100' fill='black'/>");
        var owner = Owner("image(url(\"" + UriFor(xml) + "#xywh=50,0,50,100\"))");
        var run = new Run(string.Concat(Enumerable.Repeat("轮廓裁剪先在源坐标中完成，原始文字和选择范围继续保留。", 8)));
        var text = new TextBlock { IsTextSelectionEnabled = true }; text.Inlines.Add(run); Css.SetStyle(text, "display:inline;");
        var root = new StackPanel(); Css.SetStyle(root, "display:flow-root;font-size:16px;line-height:20px;"); root.Children.Add(owner); root.Children.Add(text);
        void Layout() { Flush(); root.Measure(new(width, 900)); root.Arrange(new(0, 0, width, 900)); root.UpdateLayout(); }
        double Start() { var at = text.TransformToAncestor(root); return at.X + text.GetSourceRangeSegments(0, text.Text.Length)[0].StartX; }
        Layout(); text.Select(2, 6); Assert.Equal(200, Start(), 4);
        Css.SetStyle(owner, "shape-outside:" + Url(xml) + ";"); Flush(); Wait(owner); Layout();
        Assert.Equal(150, Start(), 4); Assert.Same(run, text.Inlines[0]); Assert.Equal(run.Text.Substring(2, 6), text.SelectedText);
        var origin = text.TransformToAncestor(root);
        foreach (var line in text.GetSourceRangeSegments(0, text.Text.Length))
            Assert.Same(text, root.InputHitTest(new(origin.X + (line.StartX + line.EndX) / 2, origin.Y + line.Baseline - 2)));
    }

    private static string BitmapUri(int width, int height, bool right)
    {
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            bytes[(y * width + x) * 4 + 3] = (x >= width / 2) == right ? (byte)255 : (byte)0;
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4)));
        using var stream = new MemoryStream(); encoder.Save(stream); return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static string Svg(string attributes, string? body = null) => "<svg xmlns='http://www.w3.org/2000/svg' " + attributes + ">" +
        (body ?? "<rect width='100%' height='100%' fill='black'/>") + "</svg>";
    private static string UriFor(string xml) => "data:image/svg+xml," + Uri.EscapeDataString(xml);
    private static string Url(string xml) => "url(\"" + UriFor(xml) + "\")";
    private static Border Owner(string value) { var owner = new Border(); Apply(owner, value); return owner; }
    private static CssShapeImageValue Value(FrameworkElement owner) => (CssShapeImageValue)owner.GetValue(CssShapeOutsideProperties.ValueProperty)!;
    private static void Apply(FrameworkElement owner, string image, string style = "")
    {
        Css.SetClass(owner, "owner");
        var sheet = CssStyleSheet.Parse(".owner{float:left;width:200px;height:100px;shape-outside:" + image + ";" + style + "}");
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
        Wait(owner);
    }
    private static void Wait(FrameworkElement owner) { Assert.All(Value(owner).Resources, r => Assert.True(r.Ready.Wait(TimeSpan.FromSeconds(10)))); Flush(); }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
}
