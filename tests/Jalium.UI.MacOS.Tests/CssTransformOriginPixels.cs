using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed partial class CssTransformOriginTests
{
    public static IEnumerable<object[]> OriginPixels()
    {
        foreach (var backend in new[] { (RenderBackend.Software, RenderingEngine.Impeller),
            (RenderBackend.Metal, RenderingEngine.Impeller), (RenderBackend.Metal, RenderingEngine.Vello) })
        {
            foreach (var kind in new[] { "text", "border" })
            foreach (var origin in new[] { "default", "zero", "center" })
            foreach (var transform in new[] { "shrink", "grow", "rotate", "matrix" })
            foreach (var css in new[] { false, true })
                yield return [kind, origin, transform, css, backend.Item1, backend.Item2];
            foreach (var origin in new[] { "content-box", "fill-box", "border-box", "stroke-box", "view-box" })
            foreach (var transform in new[] { "shrink", "grow", "rotate", "matrix" })
                yield return ["reference", origin, transform, true, backend.Item1, backend.Item2];
        }
    }

    [Theory, MemberData(nameof(OriginPixels))]
    public void OriginPixelsMatchIndependentPhysicalMatrices(string kind, string origin, string transform, bool css,
        RenderBackend backend, RenderingEngine engine)
    {
        if (RunOriginChild(backend, engine, "origin", kind, origin, transform, css.ToString())) return;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = OriginSurface(backend);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var actualRoot = new Canvas(); var actual = OriginElement(kind); actualRoot.Children.Add(actual);
        Canvas.SetLeft(actual, 40); Canvas.SetTop(actual, 60);
        var (matrix, style) = Transform(transform);
        if (css)
            Css.SetStyle(actual, $"transform:{style};" + (kind == "reference" ? $"transform-box:{origin};" :
                origin == "zero" ? "transform-origin:0 0;" : origin == "center" ? "transform-origin:50% 50%;" : ""));
        else
        {
            actual.RenderTransform = new MatrixTransform(matrix);
            if (origin != "default") actual.RenderTransformOrigin = origin == "zero" ? default : new Point(.5, .5);
        }
        Layout(actualRoot);
        var reference = OriginElement(kind); LayoutReference(reference);
        var pivot = kind == "reference"
            ? origin is "content-box" or "fill-box" ? new Point(70, 44) : new Point(80, 50)
            : new Point(origin == "zero" || origin == "default" && !css ? 0 : 60,
                origin == "zero" || origin == "default" && !css ? 0 : 40);
        // Literal affine coefficients and physical pivots above are independent
        // of the actual element's origin, bounds, CSS parser and render matrix.
        var actualPixels = OriginReadback(context, surface, engine, actualRoot.Render);
        var referencePixels = OriginReadback(context, surface, engine, dc => DrawOriginReference(dc, reference, matrix, pivot));
        var name = $"origin-{kind}-{origin}-{transform}-{css}-{backend}-{engine}";
        SaveOrigin(name, "actual", actualPixels); SaveOrigin(name, "reference", referencePixels);
        Assert.Contains(actualPixels.Where((_, i) => i % 4 == 3), alpha => alpha > 0);
        Assert.Equal(referencePixels, actualPixels);
    }

    public static IEnumerable<object[]> OriginSwitchPixels()
    {
        foreach (var transform in new[] { "shrink", "grow", "rotate", "matrix" })
        {
            yield return [transform, RenderBackend.Software, RenderingEngine.Impeller];
            yield return [transform, RenderBackend.Metal, RenderingEngine.Impeller];
            yield return [transform, RenderBackend.Metal, RenderingEngine.Vello];
        }
    }

    [Theory, MemberData(nameof(OriginSwitchPixels))]
    public void SourceSwitchPixelsMoveAndRestoreWithoutReplacingTheTransform(string transform,
        RenderBackend backend, RenderingEngine engine)
    {
        if (RunOriginChild(backend, engine, "origin-switch", transform)) return;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = OriginSurface(backend);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var root = new Canvas(); var actual = OriginElement("border"); root.Children.Add(actual);
        Canvas.SetLeft(actual, 40); Canvas.SetTop(actual, 60);
        var (matrix, style) = Transform(transform); Css.SetStyle(actual, "transform:" + style + ";"); Layout(root);
        var shared = actual.RenderTransform; Assert.NotNull(shared);
        var reference = OriginElement("border"); LayoutReference(reference);
        byte[]? first = null;
        foreach (var state in new[] { "css", "native", "restored" })
        {
            if (state == "native") actual.RenderTransform = shared;
            if (state == "restored") actual.ClearValue(UIElement.RenderTransformProperty);
            Assert.Same(shared, actual.RenderTransform); Layout(root);
            var pivot = state == "native" ? default : new Point(60, 40);
            var actualPixels = OriginReadback(context, surface, engine, root.Render);
            var referencePixels = OriginReadback(context, surface, engine, dc => DrawOriginReference(dc, reference, matrix, pivot));
            var name = $"origin-switch-{transform}-{state}-{backend}-{engine}";
            SaveOrigin(name, "actual", actualPixels); SaveOrigin(name, "reference", referencePixels);
            Assert.Equal(referencePixels, actualPixels);
            if (state == "css") first = actualPixels;
            if (state == "restored") Assert.Equal(first, actualPixels);
            Assert.Same(DependencyProperty.UnsetValue, actual.ReadLocalValue(UIElement.RenderTransformOriginProperty));
        }
    }

    private static FrameworkElement OriginElement(string kind)
    {
        var text = new TextBlock { Text = "CSS 東 AB", FontFamily = "Hiragino Sans", FontSize = 16,
            LineHeight = 20, Foreground = Brushes.Black, IsTextSelectionEnabled = true };
        if (kind == "text") { text.Width = 120; text.Height = 80; return text; }
        return new Border { Width = kind == "reference" ? 160 : 120, Height = kind == "reference" ? 100 : 80,
            Child = text, Background = new SolidColorBrush(Color.FromRgb(225, 236, 247)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(36, 79, 124)),
            BorderThickness = new Thickness(2, 4, 6, 8), Padding = new Thickness(12, 8, 28, 16) };
    }

    private static void LayoutReference(FrameworkElement element)
    {
        var size = new Size(element.Width, element.Height); element.Measure(size);
        element.Arrange(new Rect(0, 0, size.Width, size.Height));
    }
    private static void DrawOriginReference(DrawingContext dc, FrameworkElement reference, Matrix matrix, Point pivot)
    {
        // Keep the backend's offset/transform stack precision identical. Flattening
        // this to one matrix introduces different float rounding in rotated edges.
        // Every coefficient, offset and pivot still comes from the literal oracle.
        var offset = (IOffsetDrawingContext)dc; var transforms = (ITransformDrawingContext)dc;
        var saved = offset.Offset; offset.Offset = new Point(40, 60);
        transforms.PushTransform(new MatrixTransform(matrix), pivot.X, pivot.Y);
        try { reference.Render(dc); } finally { transforms.PopTransform(); offset.Offset = saved; }
    }

    private static NativePlatformWindow? OriginSurface(RenderBackend backend)
    {
        if (backend != RenderBackend.Metal) return null;
        var surface = new NativePlatformWindow("CSS transform origin pixels", 1500, 500, 400, 320, 1, 0);
        surface.Show(activate: false); return surface;
    }

    private static byte[] OriginReadback(RenderContext context, NativePlatformWindow? surface, RenderingEngine engine, Action<DrawingContext> draw)
    {
        using var target = surface is null ? context.CreateOffscreenRenderTarget(400, 320) : context.CreateRenderTarget(surface.GetSurface(), 400, 320);
        target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
        target.SetDpi(96, 96); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[400 * 320 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 400 * 4, out var width, out var height));
        Assert.Equal(400, width); Assert.Equal(320, height); return pixels;
    }

    private static void SaveOrigin(string name, string kind, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var source = BitmapSource.Create(400, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 400 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(Path.Combine(folder, $"{name}-{kind}.png")); encoder.Save(stream);
    }

    private static bool RunOriginChild(RenderBackend backend, RenderingEngine engine, params string[] arguments)
    {
        if (backend != RenderBackend.Metal || Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") == "1") return false;
        var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")
            ?? throw new InvalidOperationException("Metal main-thread runner is required."))!;
        var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in command.Skip(1).Concat(arguments).Append(engine.ToString())) start.ArgumentList.Add(arg);
        start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal origin runner exceeded 90 seconds."); }
        var receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, receipt); Assert.Contains("BACKEND Metal", receipt); Assert.Contains("ENGINE " + engine, receipt);
        return true;
    }
}
