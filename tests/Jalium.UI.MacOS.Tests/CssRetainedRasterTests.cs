using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssRetainedRasterTests
{
    public static IEnumerable<object[]> CachedFrames()
    {
        foreach (var backend in Backends())
        foreach (int dpi in new[] { 96, 120, 192 })
        foreach (var position in new[] { "integer", "quarter", "third", "negative", "outside" })
        foreach (var content in new[] { "text", "ruby", "border" })
        foreach (bool motion in new[] { false, true })
            yield return [position, content, motion, dpi, backend.Backend, backend.Engine];
    }

    [Theory, MemberData(nameof(CachedFrames))]
    public void CachedFramesPreservePhysicalOriginCoverageAndOriginalContent(string position, string content, bool motion,
        int dpi, RenderBackend backend, RenderingEngine engine)
    {
        if (Child(backend, engine, "retained", position, content, motion.ToString(), dpi.ToString())) return;
        using var cache = new CacheScope();
        double scale = dpi / 96.0, fraction = Fraction(position);
        double left = position == "outside" ? -140 : 40 + fraction, top = 60 + fraction;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = Surface(backend);
        using var target = Target(context, surface, engine, dpi);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var root = new Canvas(); var actual = Element(content); var original = actual.Child;
        root.Children.Add(actual); Canvas.SetLeft(actual, left); Canvas.SetTop(actual, top);
        SetPose(actual, motion, 0); Layout(root);
        var reference = Element(content); LayoutReference(reference);
        byte[]? first = null;
        var trace = new List<object>();
        foreach (var frame in new[] { "cold", "cached", "moved", "moved-cached", "restored", "restored-cached",
            "phase-shift", "phase-cached", "phase-return", "phase-return-cached" })
        {
            // Exercise both reuse on the same physical phase and a new raster
            // phase, including a return to the original source and geometry.
            double distance = frame switch { "moved" or "moved-cached" => 7 / scale,
                "phase-shift" or "phase-cached" => .25 / scale, _ => 0 };
            if (frame is "moved" or "restored" or "phase-shift" or "phase-return")
            { SetPose(actual, motion, distance); Layout(root); }
            bool contentDirty = actual.IsRenderDirty || actual.IsSubtreeDirty;
            int composites = 0;
            var painted = Read(context, target, root.Render, dc => composites = dc.CompositedLayerCountForTests);
            var expected = ReadReference(context, target, dc =>
            {
                var offsets = (IOffsetDrawingContext)dc; var transforms = (ITransformDrawingContext)dc;
                offsets.Offset = new Point(left, top);
                transforms.PushTransform(new MatrixTransform(new Matrix(1, 0, 0, 1, 130 + distance, 56)), 0, 0);
                try { reference.Render(dc); } finally { transforms.PopTransform(); }
            });
            var name = $"retained-{position}-{content}-{motion}-{dpi}-{frame}-{backend}-{engine}";
            Save(name, "actual", painted, dpi); Save(name, "reference", expected, dpi);
            Assert.Contains(painted.Where((_, i) => i % 4 == 3), alpha => alpha > 0);
            Assert.Equal(expected, painted);
            if (position != "outside" && frame.Contains("cached", StringComparison.Ordinal))
            {
                Assert.False(contentDirty, "The warm frame must have clean original content.");
                Assert.True(composites > 0, "A clean visible subtree must use its retained layer.");
            }
            trace.Add(new { frame, contentDirty, composites, distance, exactPixels = true });
            if (frame == "cold") first = painted;
            if (frame is "cached" or "restored" or "restored-cached" or "phase-return" or "phase-return-cached") Assert.Equal(first, painted);
            Assert.Same(original, actual.Child);
            if (original is not null) Assert.Same(actual, original.VisualParent);
        }
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder,
            $"retained-{position}-{content}-{motion}-{dpi}-{backend}-{engine}-frames.json"),
            System.Text.Json.JsonSerializer.Serialize(trace));
        Visual.ReleaseRetainedLayersRecursive(root);
    }

    public static IEnumerable<object[]> CaptureTransforms()
    {
        foreach (var backend in Backends())
        foreach (int dpi in new[] { 96, 120, 192 })
        foreach (var position in new[] { "integer", "quarter", "third" })
        foreach (var transform in new[] { "identity", "mirror-x", "mirror-y", "mirror-both" })
        foreach (var content in new[] { "text", "ruby" }) yield return [position, content, transform, dpi, backend.Backend, backend.Engine];
    }

    [Theory, MemberData(nameof(CaptureTransforms))]
    public void NativeCapturePreservesThePixelGridAndMirrorOrientation(string position, string content, string transform, int dpi,
        RenderBackend backend, RenderingEngine engine)
    {
        if (Child(backend, engine, "native-retained", position, content, transform, dpi.ToString())) return;
        using var cache = new CacheScope();
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = Surface(backend);
        using var target = Target(context, surface, engine, dpi);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        Assert.True(target.SupportsRetainedLayers());
        var reference = Element(content); LayoutReference(reference);
        double f = Fraction(position), left = 40 + f, top = 60 + f;
        double sx = transform is "mirror-x" or "mirror-both" ? -1 : 1;
        double sy = transform is "mirror-y" or "mirror-both" ? -1 : 1;
        var matrix = new Matrix(sx, 0, 0, sy, sx < 0 ? 420 : 0, sy < 0 ? 300 : 0);
        // Native capture covers the requested rectangle, including the Ruby
        // glyph ink which can extend beyond its layout box under reflection.
        var capture = new Rect(left - 24, top - 24, 208, 148);
        nint layer = 0;
        try
        {
            var actual = Read(context, target, dc =>
            {
                var transforms = (ITransformDrawingContext)dc; transforms.PushTransform(new MatrixTransform(matrix), 0, 0);
                layer = target.RealizeLayerBegin(0, (float)capture.X, (float)capture.Y, (float)capture.Width, (float)capture.Height);
                Assert.NotEqual(0, layer);
                ((IOffsetDrawingContext)dc).Offset = new Point(left, top); reference.Render(dc);
                target.RealizeLayerEnd(layer);
                target.CompositeLayer(layer, (float)capture.X, (float)capture.Y, (float)capture.Width, (float)capture.Height, 1);
                transforms.PopTransform();
            });
            var expected = ReadReference(context, target, dc =>
            {
                var transforms = (ITransformDrawingContext)dc; transforms.PushTransform(new MatrixTransform(matrix), 0, 0);
                ((IOffsetDrawingContext)dc).Offset = new Point(left, top); reference.Render(dc); transforms.PopTransform();
            });
            var name = $"retained-native-{position}-{content}-{transform}-{dpi}-{backend}-{engine}";
            Save(name, "actual", actual, dpi); Save(name, "reference", expected, dpi);
            Assert.Contains(actual.Where((_, i) => i % 4 == 3), a => a > 0); Assert.Equal(expected, actual);
            Assert.True(HasDarkGlyphs(actual), "The reflected original text must remain visible; a border alone is insufficient.");
            Assert.True(HasDarkGlyphs(expected), "The independent reference must contain the original glyphs.");
        }
        finally { if (layer != 0) target.DestroyRetainedLayer(layer); }
    }

    private static IEnumerable<(RenderBackend Backend, RenderingEngine Engine)> Backends()
    {
        yield return (RenderBackend.Software, RenderingEngine.Impeller);
        yield return (RenderBackend.Metal, RenderingEngine.Impeller);
        yield return (RenderBackend.Metal, RenderingEngine.Vello);
    }
    private static bool HasDarkGlyphs(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] > 128 && pixels[i] < 30 && pixels[i + 1] < 30 && pixels[i + 2] < 30) return true;
        return false;
    }
    private sealed class CacheScope : IDisposable
    {
        private readonly IRenderCacheHost? _previous = Visual.RenderCacheHost;
        public CacheScope() => Visual.RenderCacheHost = new MediaRenderCacheHost();
        public void Dispose() => Visual.RenderCacheHost = _previous;
    }
    private static byte[] ReadReference(RenderContext context, RenderTarget target, Action<DrawingContext> action)
    {
        var previous = Visual.RenderCacheHost;
        try { Visual.RenderCacheHost = null; return Read(context, target, action); }
        finally { Visual.RenderCacheHost = previous; }
    }
    private static double Fraction(string position) => position switch { "quarter" => .25, "third" => 2.0 / 3, "negative" => -.25, _ => 0 };
    private static void SetPose(Border box, bool motion, double distance)
    {
        if (motion) Css.SetStyle(box, "offset-path:path('M200 100 L500 100');transform-box:content-box;offset-anchor:50% 50%;offset-rotate:0deg;offset-distance:" + distance.ToString("R", CultureInfo.InvariantCulture) + "px;");
        else box.RenderTransform = new MatrixTransform(new Matrix(1, 0, 0, 1, 130 + distance, 56));
    }
    private static Border Element(string kind)
    {
        var box = new Border { Width = 160, Height = 100, Padding = new Thickness(12, 8, 28, 16), BorderThickness = new Thickness(2, 4, 6, 8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(36, 79, 124)), Background = new SolidColorBrush(Color.FromRgb(225, 236, 247)) };
        TextBlock Text(string value, double size) => new() { Text = value, FontFamily = "Hiragino Sans", FontSize = size, Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        if (kind == "text") box.Child = Text("CSS 東 AB", 16);
        else if (kind == "border") box.Child = new Border { Width = 23, Height = 19, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Color.FromRgb(21, 128, 61)) };
        else
        {
            var host = new StackPanel(); var basis = Text("東", 32); var note = Text("とうとうとう", 16);
            host.Children.Add(basis); host.Children.Add(note); box.Child = host;
            Css.SetStyle(host, "display:ruby;ruby-merge:separate;ruby-align:center;font-family:'Hiragino Sans';font-size:32px;line-height:40px;white-space:pre-wrap;text-autospace:no-autospace;");
            Css.SetStyle(basis, "display:ruby-base;font-size:32px;"); Css.SetStyle(note, "display:ruby-text;font-size:16px;line-height:96px;");
        }
        return box;
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(480, 400)); root.Arrange(new Rect(0, 0, 480, 400)); root.UpdateLayout();
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher ?? Jalium.UI.Threading.Dispatcher.CurrentDispatcher); root.UpdateLayout();
    }
    private static void LayoutReference(FrameworkElement element)
    {
        element.Measure(new Size(160, 100)); element.Arrange(new Rect(0, 0, 160, 100)); element.UpdateLayout();
    }
    private static NativePlatformWindow? Surface(RenderBackend backend)
    {
        if (backend != RenderBackend.Metal) return null;
        var window = new NativePlatformWindow("CSS cached raster fidelity", 1500, 500, 480, 400, 1, 0); window.Show(activate: false); return window;
    }
    private static RenderTarget Target(RenderContext context, NativePlatformWindow? surface, RenderingEngine engine, int dpi)
    {
        int width = 480 * dpi / 96, height = 400 * dpi / 96;
        var target = surface is null ? context.CreateOffscreenRenderTarget(width, height) : context.CreateRenderTarget(surface.GetSurface(), width, height);
        target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
        target.SetDpi(dpi, dpi); return target;
    }
    private static byte[] Read(RenderContext context, RenderTarget target, Action<DrawingContext> action,
        Action<RenderTargetDrawingContext>? inspect = null)
    {
        target.SetFullInvalidation(); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { action(dc); inspect?.Invoke(dc); dc.Close(); }
        target.EndDraw(); var bytes = new byte[target.Width * target.Height * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(bytes, (uint)target.Width * 4, out var w, out var h));
        Assert.Equal(target.Width, w); Assert.Equal(target.Height, h); return bytes;
    }
    private static void Save(string name, string suffix, byte[] pixels, int dpi)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder); int width = 480 * dpi / 96, height = 400 * dpi / 96;
        var source = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(Path.Combine(folder, name + "-" + suffix + ".png")); encoder.Save(stream);
    }
    private static bool Child(RenderBackend backend, RenderingEngine engine, params string[] arguments)
    {
        if (backend != RenderBackend.Metal || Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") == "1") return false;
        var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND") ?? throw new InvalidOperationException("Metal main-thread runner required"))!;
        var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in command.Skip(1).Concat(arguments).Append(engine.ToString())) start.ArgumentList.Add(arg);
        start.Environment["JALIUM_CSS_METAL_CHILD"] = "1"; using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000)) { process.Kill(true); Assert.Fail("Metal raster child exceeded 90 seconds"); }
        var receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, receipt); Assert.Contains("BACKEND Metal", receipt); Assert.Contains("ENGINE " + engine, receipt); return true;
    }
}
