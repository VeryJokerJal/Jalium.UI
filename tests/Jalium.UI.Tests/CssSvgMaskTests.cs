using System.Text;
using System.IO.Compression;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssSvgMaskTests
{
    [Theory]
    [InlineData("alpha", true)] [InlineData("luminance", true)] [InlineData("ALPHA", true)]
    [InlineData("inherit", true)] [InlineData("initial", true)] [InlineData("unset", true)]
    [InlineData("match-source", false)] [InlineData("alpha,luminance", false)] [InlineData("none", false)]
    public void MaskTypeGrammarIsRegistered(string value, bool valid) => Assert.Equal(valid, CssAuthoring.IsValueSupported("mask-type", value));

    [Theory]
    [InlineData("white", "", "match-source", 255)] [InlineData("black", "", "match-source", 0)]
    [InlineData("black", "mask-type='alpha'", "match-source", 255)]
    [InlineData("black", "mask-type='alpha'", "luminance", 0)]
    [InlineData("black", "", "alpha", 255)] [InlineData("red", "", "match-source", 54)]
    [InlineData("#808080", "color-interpolation='linearRGB'", "match-source", 55)]
    [InlineData("#808080", "color-interpolation='linearRGB'", "alpha", 255)]
    [InlineData("black", "mask-type='luminance' style='mask-type:ALPHA'", "match-source", 255)]
    public void SourceTypeAndExplicitModeSelectAlphaOrLuminance(string color, string attributes, string mode, int expected)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, Document($"<mask id='m' {attributes}><rect width='100%' height='100%' fill='{color}'/></mask>"), $"mask-image:url('m.svg#m');mask-mode:{mode};mask-type:alpha;"); Ready(owner);
        Assert.InRange((int)Pixel(Render(owner), 30, 20).A, Math.Max(0, expected - 1), expected + 1);
    }

    [Theory]
    [InlineData("objectBoundingBox", "width='.5' height='1'")]
    [InlineData("userSpaceOnUse", "width='40' height='60'")]
    public void ContentCoordinatesUseTheirOwnUnitsAndCssImageTilingIsIgnored(string units, string size)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, Document($"<mask id='m' maskContentUnits='{units}'><rect {size} fill='white'/></mask>"),
            "mask:url('m.svg#m') right bottom / 1px 1px repeat content-box; border-radius:20px;padding:10px;"); Ready(owner);
        var image = Render(owner); Assert.Equal(255, Pixel(image, 20, 20).A); Assert.Equal(0, Pixel(image, 60, 20).A);
    }

    [Theory]
    [InlineData("objectBoundingBox", "x='.25' y='0' width='.5' height='1'")]
    [InlineData("objectBoundingBox", "x='.25px' y='0' width='.5px' height='1px'")]
    [InlineData("objectBoundingBox", "x='calc(20% + .05px)' y='0' width='calc(25% + .25px)' height='100%'")]
    [InlineData("userSpaceOnUse", "x='20' y='0' width='40' height='60'")]
    [InlineData("userSpaceOnUse", "x='25%' y='0' width='50%' height='100%'")]
    public void ExplicitMaskRegionsClipInTheReferencingSpace(string units, string region)
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, Document($"<mask id='m' maskUnits='{units}' {region}><rect width='100%' height='100%' fill='white'/></mask>")); Ready(owner);
        var image = Render(owner); Assert.Equal(0, Pixel(image, 10, 20).A); Assert.Equal(255, Pixel(image, 30, 20).A); Assert.Equal(0, Pixel(image, 70, 20).A);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void FractionalTransformsSampleTheSvgMaskEdgeAndSourceEdgeSeparately(int dpi)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document("<mask id='m'><rect width='100%' height='100%' fill='white'/></mask>")); Ready(owner);
        owner.SetRootDpi(new(dpi, dpi)); Layout(owner);
        byte Draw()
        {
            var target = new RenderTargetBitmap(84 * dpi, 64 * dpi, 96 * dpi, 96 * dpi, PixelFormats.Bgra32);
            var context = new SoftwareDrawingContext(target); context.PushTransform(new TranslateTransform(.25 / dpi, 0)); owner.Render(context); context.Pop(); context.Close();
            return Pixel(target, 0, 20 * dpi).A;
        }
        var masked = Draw(); Css.SetStyle(owner, "mask:none;"); Layout(owner); var source = Draw();
        Assert.InRange((int)source, 190, 192);
        Assert.InRange((int)masked, 142, 145); // source coverage .75 times mask coverage .75
    }

    [Fact]
    public void UnspecifiedRegionDoesNotClipOverflowAndMaskClipDoesNotOverrideSvgGeometry()
    {
        var child = new Border { Width = 110, Height = 40, Background = Brushes.Red }; var owner = new Canvas(); owner.Children.Add(child);
        Attach(owner, Document("<mask id='m'><rect width='110' height='40' fill='white'/></mask>"), "mask:url('m.svg#m');mask-clip:content-box;padding:10px;");
        var root = new Canvas { Width = 120, Height = 80 }; root.Children.Add(owner); Ready(owner); Layout(root);
        Assert.Equal(255, Pixel(Render(root), 100, 20).A);
    }

    [Theory]
    [InlineData("width='0'")] [InlineData("height='-1'")]
    public void NonPositiveRegionsDisableTheWholeMask(string region)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document($"<mask id='m' {region}><rect width='100%' height='100%' fill='white'/></mask>")); Ready(owner);
        Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
    }

    [Theory]
    [InlineData("<mask id='m'/>")]
    [InlineData("<mask id='other'><rect width='100%' height='100%' fill='white'/></mask>")]
    [InlineData("<rect id='m' width='80' height='60' fill='white'/>")]
    public void EmptyMissingAndWrongElementReferencesStayTransparent(string elements)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document(elements)); Ready(owner);
        Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
    }

    [Theory]
    [InlineData("malformed")] [InlineData("dtd")] [InlineData("depth")] [InlineData("characters")]
    public void InvalidOrOverBudgetXmlFailsAsATransparentLayer(string kind)
    {
        var body = "<mask id='m'><rect width='100%' height='100%' fill='white'/></mask>";
        var xml = kind switch
        {
            "malformed" => "<svg><mask id='m'>",
            "dtd" => "<!DOCTYPE svg [<!ENTITY color 'white'>]>" + Document(body.Replace("white", "&color;")),
            "depth" => Document(string.Concat(Enumerable.Repeat("<g>", 257)) + body + string.Concat(Enumerable.Repeat("</g>", 257))),
            _ => Document(new string(' ', 8_000_100) + body),
        };
        var owner = new Border { Background = Brushes.Red }; Attach(owner, xml); Ready(owner);
        Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
    }

    [Fact]
    public void GzipAndEscapedUnicodeFragmentIdsUseTheApprovedResourceWithoutTheFragment()
    {
        using var data = new MemoryStream();
        using (var gzip = new GZipStream(data, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(Document("<mask id='白色'><rect width='100%' height='100%' fill='white'/></mask>")));
        var source = new Source("", bytes: data.ToArray()); var owner = new Border { Background = Brushes.Red };
        Attach(owner, source, "mask:url('mask.svg#%E7%99%BD%E8%89%B2');"); Ready(owner);
        Assert.Equal(255, Pixel(Render(owner), 30, 20).A); Assert.Equal("", source.Request!.Fragment);
    }

    [Fact]
    public void EmbeddedPixelsInvalidateTheirMaskAndRetiredMasksDoNotInvalidateTheOwner()
    {
        byte[] Png(byte alpha)
        {
            byte[] pixels = [255, 255, 255, alpha]; var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4)));
            using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
        }
        var uri = "data:image/png;base64," + Convert.ToBase64String(Png(128));
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document($"<mask id='m'><image href='{uri}' width='80' height='60'/></mask>")); Ready(owner);
        Assert.Equal(128, Pixel(Render(owner), 30, 20).A);
        var resource = ((CssMaskImages)owner.GetValue(CssMaskProperties.ImagesProperty)!).Resources.Single();
        var mask = Assert.IsType<SvgMaskImage>(resource.Get(CssNode.Get(owner)));
        var plan = mask.Definition!.Resolve(new Rect(0, 0, 80, 60), new Size(80, 60))!;
        BitmapImage Find(Drawing drawing) => drawing is ImageDrawing image ? Assert.IsType<BitmapImage>(image.ImageSource)
            : Find(Assert.IsType<DrawingGroup>(drawing).Children.Single());
        var bitmap = Find(plan); owner.ClearRenderDirty(); bitmap.StreamSource = new MemoryStream(Png(255)); Flush();
        Assert.True(owner.IsRenderDirty); Assert.Equal(255, Pixel(Render(owner), 30, 20).A);
        Css.SetStyle(owner, "mask:none;"); Layout(owner); owner.ClearRenderDirty(); bitmap.StreamSource = new MemoryStream(Png(0)); Flush(); Assert.False(owner.IsRenderDirty);
    }

    [Fact]
    public void SourceAncestorsSupplyColorAndMaskDisplayAndOpacityDoNotSuppressIt()
    {
        var owner = new Border { Background = Brushes.Red };
        Attach(owner, Document("<g fill='white' display='none'><mask id='m' display='none' opacity='0'><rect width='100%' height='100%'/></mask></g>")); Ready(owner);
        Assert.Equal(255, Pixel(Render(owner), 30, 20).A);
    }

    [Theory]
    [InlineData(null)] [InlineData(false)] [InlineData(true)]
    public void LoadingAndRemotePixelApprovalRemainPartOfTheMaskResource(bool? approval)
    {
        var resolver = new Source(Document("<mask id='m'><rect width='100%' height='100%' fill='white'/></mask>"), true, approval);
        var owner = new Border { Background = Brushes.Red }; Attach(owner, resolver, "mask:url('m.svg#m');", new Uri("https://example.test/styles.css")); Layout(owner);
        Assert.Equal(0, Pixel(Render(owner), 30, 20).A); resolver.Open(); Ready(owner);
        Assert.Equal(approval == true ? 255 : 0, Pixel(Render(owner), 30, 20).A);
        Assert.Equal("", resolver.Request!.Fragment); Assert.Equal(new Uri("https://example.test/styles.css"), resolver.Referrer);
    }

    [Fact]
    public void MaskTypesAreNotInheritedOrResetByTheMaskShorthand()
    {
        var parent = new Canvas(); Css.SetStyle(parent, "mask-type:alpha;"); var child = new Border { Width = 80, Height = 60 }; parent.Children.Add(child); Layout(parent);
        Assert.Equal(CssMaskMode.Luminance, child.GetValue(CssMaskProperties.TypeProperty));
        Css.SetStyle(child, "mask-type:inherit;mask:none;"); Layout(parent); Assert.Equal(CssMaskMode.Alpha, child.GetValue(CssMaskProperties.TypeProperty));
        Assert.DoesNotContain("mask-type", CssPropertyMetadata.Longhands("mask"));
        Assert.True(CssMaskStyleQuery.Matches("mask-type", "alpha", CssNode.Get(child), CssEngine.BuildLengthContext(child)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void LocalSvgMasksAreNeverPaintedDirectlyAndApplyToTheCompleteGroup(bool direct)
    {
        var svg = SvgImage.FromSvgString(Document("<mask id='m' mask-type='alpha'><rect width='100%' height='100%' opacity='.5'/></mask>" +
            "<g mask='url(#m)'><rect width='60' height='50' fill='red'/><rect x='20' y='10' width='50' height='40' fill='red'/></g>"));
        var pixels = RenderSvg(svg, direct);
        Assert.Equal(128, Alpha(pixels, 80, 10, 10)); Assert.Equal(128, Alpha(pixels, 80, 30, 30)); Assert.Equal(0, Alpha(pixels, 80, 75, 55));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NestedLocalMasksUseUnclippedObjectBoundsBeforeTheReferencingTransform(bool direct)
    {
        var svg = SvgImage.FromSvgString(Document("<mask id='half' maskContentUnits='objectBoundingBox'><rect width='.5' height='1' fill='white'/></mask>" +
            "<mask id='m' mask-type='alpha'><g mask='url(#half)'><rect x='10' y='10' width='40' height='40' fill='white'/></g></mask>" +
            "<g transform='translate(10)' mask='url(#m)' opacity='.5'><rect x='10' y='10' width='40' height='40' fill='red' stroke='red' stroke-width='10'/></g>"));
        var pixels = RenderSvg(svg, direct); Assert.Equal(128, Alpha(pixels, 80, 25, 30)); Assert.Equal(0, Alpha(pixels, 80, 45, 30)); Assert.Equal(0, Alpha(pixels, 80, 17, 30));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SvgGroupOpacityAndClipApplyOnlyOnceToOverlappingPaint(bool direct)
    {
        var svg = SvgImage.FromSvgString(Document("<clipPath id='c'><rect x='.5' width='60' height='60'/></clipPath>" +
            "<g opacity='.5' clip-path='url(#c)'><rect width='80' height='60' fill='red'/><rect width='80' height='60' fill='red'/></g>"));
        var pixels = RenderSvg(svg, direct);
        Assert.Equal(128, Alpha(pixels, 80, 30, 30)); Assert.InRange((int)Alpha(pixels, 80, 0, 30), 63, 65); Assert.InRange((int)Alpha(pixels, 80, 60, 30), 63, 65);
    }

    [Theory]
    [InlineData("<mask id='m'><g mask='url(#m)'><rect width='80' height='60' fill='white'/></g></mask>")]
    [InlineData("<mask id='m'><g mask='url(#n)'><rect width='80' height='60' fill='white'/></g></mask><mask id='n'><g mask='url(#m)'><rect width='80' height='60' fill='white'/></g></mask>")]
    public void ReferenceCyclesProduceTransparentMasks(string source)
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document(source)); Ready(owner); Assert.Equal(0, Pixel(Render(owner), 30, 20).A);
    }

    [Fact]
    public void OutlinedTextGradientsAndUseContributeToSvgMaskPixels()
    {
        using var native = RenderContext.GetOrCreateCurrent(RenderBackend.Software);
        var svg = Document("<defs><rect id='half' width='40' height='60'/><linearGradient id='fade'><stop stop-color='white'/><stop offset='1' stop-color='black'/></linearGradient></defs>" +
            "<mask id='m'><use href='#half' fill='white'/><text x='42' y='40' fill='white' font-size='24'>Ab</text></mask>");
        var owner = new Border { Background = Brushes.Red }; Attach(owner, svg); Ready(owner); var image = Render(owner);
        Assert.Equal(255, Pixel(image, 20, 20).A);
        var pixels = image.GetPixelBuffer(); Assert.Contains(Enumerable.Range(0, 60).SelectMany(y => Enumerable.Range(42, 35).Select(x => pixels[(y * 80 + x) * 4 + 3])), value => value > 0);
    }

    [Fact]
    public void WholeFrameRecordingPublishesTheResolvedSvgMaskPixels()
    {
        var owner = new Border { Background = Brushes.Red }; Attach(owner, Document("<mask id='m' mask-type='alpha'><rect width='40' height='60'/></mask>")); Ready(owner);
        var expected = Render(owner).GetPixelBuffer().ToArray(); var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); owner.Render(recorder); var commands = recorder.Commit();
        Css.SetStyle(owner, "mask:none;"); Layout(owner);
        var target = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); var drawing = new SoftwareDrawingContext(target); DrawingReplayer.Replay(commands, drawing); drawing.Close(); Assert.Equal(expected, target.GetPixelBuffer());
    }

    private static byte[] RenderSvg(SvgImage image, bool direct)
    {
        if (!direct) return SoftwareVectorRasterizer.Rasterize(image.Drawing!, 80, 60, new Rect(0, 0, 80, 60))!;
        var target = new RenderTargetBitmap(80, 60, 96, 96, PixelFormats.Bgra32); var context = new SoftwareDrawingContext(target); image.Drawing!.RenderTo(context); context.Close(); return target.GetPixelBuffer();
    }
    private static byte Alpha(byte[] data, int width, int x, int y) => data[(y * width + x) * 4 + 3];
    private static string Document(string body) => $"<svg xmlns='http://www.w3.org/2000/svg' width='80' height='60'>{body}</svg>";
    private static void Attach(FrameworkElement owner, string svg, string css = "mask:url('m.svg#m');") => Attach(owner, new Source(svg), css);
    private static void Attach(FrameworkElement owner, Source resolver, string css, Uri? baseUri = null)
    {
        Css.SetClass(owner, "owner"); var sheet = CssStyleSheet.Parse($".owner{{box-sizing:border-box;width:80px;height:60px;{css}}}", "svg-mask", baseUri ?? new Uri("file:///svg-mask/"), resolver);
        Assert.Empty(sheet.Diagnostics); Css.GetStyleSheets(owner).Add(sheet); Flush();
    }
    private static void Ready(FrameworkElement owner)
    {
        Layout(owner); var resources = ((CssMaskImages)owner.GetValue(CssMaskProperties.ImagesProperty)!).Resources;
        var end = DateTime.UtcNow.AddSeconds(10); while (resources.Any(resource => !resource.Ready.IsCompleted) && DateTime.UtcNow < end) { Flush(); Thread.Sleep(1); }
        Assert.All(resources, resource => Assert.True(resource.Ready.IsCompleted)); Layout(owner);
    }
    private static void Flush() { Dispatcher.CurrentDispatcher.ProcessQueue(); BitmapDecodeNotifier.FlushPendingForTests(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); }
    private static void Layout(FrameworkElement owner)
    { for (var i = 0; i < 5; i++) { Flush(); owner.Measure(new(500, 1000)); owner.Arrange(new(0, 0, owner.DesiredSize.Width, owner.DesiredSize.Height)); owner.UpdateLayout(); } }
    private static RenderTargetBitmap Render(FrameworkElement owner)
    { var image = new RenderTargetBitmap((int)owner.ActualWidth, (int)owner.ActualHeight, 96, 96, PixelFormats.Bgra32); var context = new SoftwareDrawingContext(image); owner.Render(context); context.Close(); return image; }
    private static Color Pixel(RenderTargetBitmap image, int x, int y)
    { var data = image.GetPixelBuffer(); var at = (y * image.PixelWidth + x) * 4; return Color.FromArgb(data[at + 3], data[at + 2], data[at + 1], data[at]); }
    private sealed class Source(string content, bool gated = false, bool? approval = true, byte[]? bytes = null) : ICssImageResourceResolver
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Uri? Request, Referrer;
        internal void Open() => _gate.TrySetResult();
        public ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default) => ResolveImageAsync(uri, null, token);
        public async ValueTask<CssResource> ResolveImageAsync(Uri uri, Uri? referrer, CancellationToken token = default)
        { Request = uri; Referrer = referrer; if (gated) await _gate.Task; return new(uri, new MemoryStream(bytes ?? Encoding.UTF8.GetBytes(content))) { ImagePixelAccessAllowed = approval }; }
    }
}
