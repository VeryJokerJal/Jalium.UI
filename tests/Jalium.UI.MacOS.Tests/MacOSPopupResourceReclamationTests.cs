using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Diagnostics;
using Jalium.UI.Hosting;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Rendering;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSPopupResourceReclamationTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<object[]> FrameCases()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (bool custom in new[] { false, true })
        foreach (int witness in new[] { 0, 1, 2 })
            yield return [depth, custom, witness];
    }

    [Theory, MemberData(nameof(FrameCases))]
    public void OnlyThePresentingWindowsFullFrameCanProveTheImageOffscreen(int depth, bool custom, int witness)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope();
        using var scene = new Scene(depth, custom, pixels.Bitmap);
        long painted = Environment.TickCount64 - 2000;
        Stamp(scene.Image, painted); Stamp(scene.Probe, painted);
        long ownerFrame = painted + (witness == 0 ? 100 : -100);
        long surfaceFrame = painted + (witness == 1 ? 100 : -100);
        if (depth == 0) surfaceFrame = painted + (witness == 2 ? -100 : 100);
        Frame(scene.Owner, ownerFrame); Frame(scene.Surface, surfaceFrame);
        using var reclaimer = Reclaimer(); Track(reclaimer, scene.Image); Track(reclaimer, scene.Probe);
        var original = pixels.Bitmap.RawPixelData;
        Assert.True(scene.Image.IsVisible); Assert.NotNull(original);
        Assert.Same(scene.Owner, Window.GetWindow(scene.Image));
        Assert.Same(scene.Surface, Window.GetMacOSPhysicalWindow(scene.Image));
        reclaimer.ScanAndReclaim();
        bool offscreen = surfaceFrame > painted;
        Assert.Equal(offscreen ? 1 : 0, scene.Probe.Reclaims);
        if (offscreen) Assert.Null(pixels.Bitmap.RawPixelData);
        else Assert.Same(original, pixels.Bitmap.RawPixelData);
        Assert.NotNull(pixels.Bitmap.ImageData);
        Assert.Same(scene.Owner, Window.GetWindow(scene.Image));
    }

    public static IEnumerable<object[]> RetiredCases()
    {
        foreach (int depth in new[] { 0, 1, 2 })
        foreach (string retirement in new[] { "hidden", "collapsed", "detached" })
            yield return [depth, retirement];
    }

    [Theory, MemberData(nameof(RetiredCases))]
    public void HiddenAndDetachedContentStillReleasesDecodedPixels(int depth, string retirement)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope(); using var scene = new Scene(depth, false, pixels.Bitmap);
        Stamp(scene.Image, Environment.TickCount64 - 2000); Stamp(scene.Probe, Environment.TickCount64 - 2000);
        using var reclaimer = Reclaimer(); Track(reclaimer, scene.Image); Track(reclaimer, scene.Probe);
        if (retirement == "detached") scene.Surface.Content = null;
        else scene.Panel.Visibility = retirement == "hidden" ? Visibility.Hidden : Visibility.Collapsed;
        reclaimer.ScanAndReclaim();
        Assert.Null(pixels.Bitmap.RawPixelData); Assert.NotNull(pixels.Bitmap.ImageData);
        Assert.Equal(1, scene.Probe.Reclaims);
    }

    [Theory, InlineData(1), InlineData(2)]
    public void ARecentlyPaintedImageIsNotReclaimedEvenWithAnOffscreenWitness(int depth)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope(); using var scene = new Scene(depth, false, pixels.Bitmap);
        long now = Environment.TickCount64; Stamp(scene.Image, now); Stamp(scene.Probe, now);
        Frame(scene.Owner, now + 100); Frame(scene.Surface, now + 100);
        using var reclaimer = Reclaimer(); Track(reclaimer, scene.Image); Track(reclaimer, scene.Probe);
        reclaimer.ScanAndReclaim(); Assert.NotNull(pixels.Bitmap.RawPixelData); Assert.Equal(0, scene.Probe.Reclaims);
    }

    [Theory, InlineData(1), InlineData(2)]
    public void ASharedSourcesNewerDrawStillProtectsItsPixels(int depth)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope(); using var scene = new Scene(depth, false, pixels.Bitmap);
        long old = Environment.TickCount64 - 2000; Stamp(scene.Image, old);
        Frame(scene.Owner, old + 100); Frame(scene.Surface, old + 100);
        typeof(ImageSource).GetMethod("MarkDrawn", Private)!.Invoke(pixels.Bitmap, null);
        using var reclaimer = Reclaimer(); Track(reclaimer, scene.Image);
        var original = pixels.Bitmap.RawPixelData; reclaimer.ScanAndReclaim();
        Assert.Same(original, pixels.Bitmap.RawPixelData);
    }

    [Theory, InlineData(1), InlineData(2)]
    public void DisabledResourceReclamationLeavesTheSurfaceAlone(int depth)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope(); using var scene = new Scene(depth, false, pixels.Bitmap);
        long old = Environment.TickCount64 - 2000; Stamp(scene.Image, old); Stamp(scene.Probe, old);
        Frame(scene.Owner, old + 100); Frame(scene.Surface, old + 100);
        using var reclaimer = new ResourceReclaimer(new ResourceReclamationOptions
        { IdleTimeoutMs = 250, EvictDrawingCache = false, ReclaimBackendCaches = false, InvokeReclaimableResources = false });
        Track(reclaimer, scene.Image); Track(reclaimer, scene.Probe); reclaimer.ScanAndReclaim();
        Assert.NotNull(pixels.Bitmap.RawPixelData); Assert.Equal(0, scene.Probe.Reclaims);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AnEqualPhysicalFrameDoesNotProveThePopupOffscreen(bool custom)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var pixels = new BitmapScope(); using var scene = new Scene(1, custom, pixels.Bitmap);
        long old = Environment.TickCount64 - 2000; Stamp(scene.Image, old); Stamp(scene.Probe, old);
        Frame(scene.Owner, old + 100); Frame(scene.Surface, old);
        using var reclaimer = Reclaimer(); Track(reclaimer, scene.Image); Track(reclaimer, scene.Probe);
        reclaimer.ScanAndReclaim(); Assert.NotNull(pixels.Bitmap.RawPixelData); Assert.Equal(0, scene.Probe.Reclaims);
    }

    [Theory, InlineData(false, false), InlineData(true, false), InlineData(false, true)]
    public void AnImagesOwnLaterDrawDoesNotPreventIdleReclamation(bool throwAfterDraw, bool cached)
    {
        using var pixels = new BitmapScope();
        typeof(ImageSource).GetProperty("ReclaimGraceMs", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0L);
        var image = PreparedImage(pixels.Bitmap);
        Action<ImageSource> draw = source =>
        {
            AdvanceTick(); MarkDrawn(source);
            if (throwAfterDraw) throw new InvalidOperationException("draw failed after realizing the source");
        };
        using DrawingContext drawing = cached ? new CacheStampDrawingContext(draw) : new StampDrawingContext(draw);
        var previousCache = Visual.RenderCacheHost;
        try
        {
            if (cached) Visual.RenderCacheHost = new MediaRenderCacheHost();
            if (throwAfterDraw) Assert.Throws<InvalidOperationException>(() => image.Render(drawing));
            else image.Render(drawing);
            if (cached)
            {
                long before = Visual.RetainedCacheReplaysTotal;
                image.Render(drawing); Assert.True(Visual.RetainedCacheReplaysTotal > before);
            }
            Assert.True(Drawn(pixels.Bitmap) > image.LastRenderedTickMs);
            image.Visibility = Visibility.Collapsed;
            image.ReclaimIdleResources();
            Assert.Null(pixels.Bitmap.RawPixelData);
            Assert.NotNull(pixels.Bitmap.ImageData);
        }
        finally { Visual.RenderCacheHost = previousCache; }
    }

    [Fact]
    public void AnotherConsumersLaterDrawStillProtectsTheSharedBitmap()
    {
        using var pixels = new BitmapScope();
        typeof(ImageSource).GetProperty("ReclaimGraceMs", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0L);
        var image = PreparedImage(pixels.Bitmap);
        using var drawing = new StampDrawingContext(source => { AdvanceTick(); MarkDrawn(source); });
        image.Render(drawing); AdvanceTick(); MarkDrawn(pixels.Bitmap);
        image.Visibility = Visibility.Collapsed; var original = pixels.Bitmap.RawPixelData;
        image.ReclaimIdleResources();
        Assert.Same(original, pixels.Bitmap.RawPixelData);
    }

    [Fact]
    public void ReplacingTheSourceDiscardsThePreviousSourcesOwnDrawStamp()
    {
        using var pixels = new BitmapScope();
        using var replacement = BitmapImage.FromBytes([0x89, 0x50, 0x4e, 0x47]);
        typeof(ImageSource).GetProperty("ReclaimGraceMs", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0L);
        var image = PreparedImage(pixels.Bitmap);
        using var drawing = new StampDrawingContext(source =>
        {
            AdvanceTick(); MarkDrawn(replacement); AdvanceTick(); MarkDrawn(source);
        });
        image.Render(drawing);
        Assert.True(Drawn(replacement) > image.LastRenderedTickMs);
        Assert.True(Drawn(replacement) < Drawn(pixels.Bitmap));
        image.Source = replacement; image.Visibility = Visibility.Hidden;
        var original = replacement.RawPixelData; Assert.NotNull(original); image.ReclaimIdleResources();
        Assert.Same(original, replacement.RawPixelData);
    }

    private static Image PreparedImage(BitmapImage source)
    {
        var image = new Image { Source = source, Width = 80, Height = 60 };
        image.Measure(new Size(80, 60)); image.Arrange(new Rect(0, 0, 80, 60)); return image;
    }

    [Fact]
    public void ReplacingTheSourceDuringDrawingDoesNotClaimTheNewSourcesOtherConsumer()
    {
        using var pixels = new BitmapScope();
        using var replacement = BitmapImage.FromBytes([0x89, 0x50, 0x4e, 0x47]);
        typeof(ImageSource).GetProperty("ReclaimGraceMs", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, 0L);
        var image = PreparedImage(pixels.Bitmap);
        using var drawing = new StampDrawingContext(source =>
        {
            MarkDrawn(source); AdvanceTick(); MarkDrawn(replacement); image.Source = replacement;
        });
        image.Render(drawing);
        Assert.True(Drawn(replacement) > image.LastRenderedTickMs);
        image.Visibility = Visibility.Collapsed;
        var original = replacement.RawPixelData; Assert.NotNull(original); image.ReclaimIdleResources();
        Assert.Same(original, replacement.RawPixelData);
    }

    private static void AdvanceTick()
    {
        long tick = Environment.TickCount64;
        Assert.True(SpinWait.SpinUntil(() => Environment.TickCount64 > tick, TimeSpan.FromSeconds(1)));
    }
    private static void MarkDrawn(ImageSource source) => typeof(ImageSource).GetMethod("MarkDrawn", Private)!.Invoke(source, null);
    private static long Drawn(ImageSource source) => (long)typeof(ImageSource).GetProperty("LastDrawnTickMs", Private)!.GetValue(source)!;

    private sealed class CacheStampDrawingContext(Action<ImageSource> draw) : StampDrawingContext(draw), ICacheableDrawingContext;
    private class StampDrawingContext(Action<ImageSource> draw) : DrawingContextAdapter
    {
        public override void DrawImage(ImageSource imageSource, Rect rectangle) => draw(imageSource);
        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }
        public override void DrawText(FormattedText formattedText, Point origin) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private static ResourceReclaimer Reclaimer() => new(new ResourceReclamationOptions
    { IdleTimeoutMs = 250, EvictDrawingCache = false, ReclaimBackendCaches = false });
    private static void Stamp(Visual visual, long value) => typeof(Visual).GetField("_lastRenderedTickMs", Private)!.SetValue(visual, value);
    private static void Frame(Window window, long value) => typeof(Window).GetField("_lastFullFramePaintStartTickMs", Private)!.SetValue(window, value);
    private static void Track(ResourceReclaimer reclaimer, Visual visual) => typeof(ResourceReclaimer).GetMethod("OnVisualRendered", Private)!.Invoke(reclaimer, [visual]);

    private sealed class ReclaimableProbe : Border, IReclaimableResource
    { internal int Reclaims; public void ReclaimIdleResources() => Reclaims++; }
    private sealed class HeadlessWindow : Window
    {
        internal HeadlessWindow(bool custom)
        {
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native;
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", Private)!.Invoke(this, [Visibility.Visible]);
        }
    }
    private sealed class Scene : IDisposable
    {
        internal readonly List<Window> Windows = [];
        internal readonly Window Owner, Surface;
        internal readonly StackPanel Panel = new();
        internal readonly Image Image;
        internal readonly ReclaimableProbe Probe = new() { Width = 20, Height = 20 };
        internal Scene(int depth, bool custom, BitmapImage bitmap)
        {
            Owner = new HeadlessWindow(custom) { Width = 400, Height = 300 }; Windows.Add(Owner);
            Window parent = Owner;
            for (int i = 0; i < depth; i++)
            {
                var next = new HeadlessWindow(custom) { Width = 240, Height = 160 };
                typeof(Window).GetField("_macOSPopupParent", Private)!.SetValue(next, parent);
                Windows.Add(next); parent = next;
            }
            Surface = parent; Image = new Image { Source = bitmap, Width = 80, Height = 60 };
            Panel.Children.Add(Image); Panel.Children.Add(Probe); Surface.Content = Panel;
            Surface.Measure(new Size(240, 160)); Surface.Arrange(new Rect(0, 0, 240, 160));
        }
        public void Dispose()
        {
            foreach (var window in Windows.AsEnumerable().Reverse())
            { window.Content = null; typeof(Window).GetField("_macOSPopupParent", Private)!.SetValue(window, null); window.Close(); }
        }
    }
    private sealed class BitmapScope : IDisposable
    {
        private readonly object? _decoder = typeof(BitmapImage).GetField("s_decoder", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        private readonly PropertyInfo _grace = typeof(ImageSource).GetProperty("ReclaimGraceMs", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _oldGrace;
        internal readonly BitmapImage Bitmap;
        internal BitmapScope()
        {
            _oldGrace = _grace.GetValue(null); BitmapImage.SetDecoder(new Decoder());
            Bitmap = BitmapImage.FromBytes([0x89, 0x50, 0x4e, 0x47]);
        }
        public void Dispose()
        { Bitmap.Dispose(); typeof(BitmapImage).GetField("s_decoder", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, _decoder); _grace.SetValue(null, _oldGrace); }
    }
    private sealed class Decoder : INativeImageDecoder
    {
        public DecodedImage Decode(ReadOnlySpan<byte> data, NativePixelFormat format = NativePixelFormat.Bgra8) => new(new byte[4 * 3 * 4], 4, 3, 16, format);
        public DecodedImage Decode(Stream data, NativePixelFormat format = NativePixelFormat.Bgra8) => Decode(ReadOnlySpan<byte>.Empty, format);
        public DecodedImage DecodeFile(string path, NativePixelFormat format = NativePixelFormat.Bgra8) => Decode(ReadOnlySpan<byte>.Empty, format);
        public bool TryReadDimensions(ReadOnlySpan<byte> data, out int width, out int height) { width = 4; height = 3; return true; }
    }
}
