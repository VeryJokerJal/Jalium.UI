using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed partial class CssMotionReferenceBoxTests
{
    public static IEnumerable<object[]> MotionPixels()
    {
        foreach (var backend in MotionBackends())
        foreach (var referenceBox in new[] { "content-box", "fill-box", "border-box", "stroke-box", "view-box" })
        foreach (var anchor in new[] { "auto", "percent", "edge-offset", "calc" })
        foreach (int angle in new[] { 0, 90, 45 })
        foreach (bool scale in new[] { false, true })
            yield return [referenceBox, anchor, angle, scale, backend.Backend, backend.Engine];
    }

    [Theory, MemberData(nameof(MotionPixels))]
    public void MotionPixelsMatchIndependentReferenceRectanglesAndPhysicalMatrices(string referenceBox, string anchor,
        int angle, bool scale, RenderBackend backend, RenderingEngine engine)
    {
        if (RunMotionChild(backend, engine, "motion", referenceBox, anchor, angle.ToString(), scale.ToString())) return;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = MotionSurface(backend);
        using var target = CreateMotionTarget(context, surface, engine);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var root = new Canvas(); var actual = MotionElement(); root.Children.Add(actual);
        Canvas.SetLeft(actual, 40); Canvas.SetTop(actual, 60);
        Css.SetStyle(actual, Style(referenceBox, anchor, angle) + (scale ? "transform:scale(.75);" : "")); Layout(root);
        var reference = MotionElement(); LayoutMotionReference(reference);
        var pivot = scale ? LiteralAnchor(referenceBox, "auto") : default;
        var ordinary = scale ? new Matrix(.75, 0, 0, .75, 0, 0) : Matrix.Identity;
        var literal = LiteralMotionMatrix(LiteralAnchor(referenceBox, anchor), angle);
        var physical = ordinary * new Matrix(1, 0, 0, 1, pivot.X, pivot.Y) * literal * new Matrix(1, 0, 0, 1, -pivot.X, -pivot.Y);
        CompareMotionPixels(context, target, root, reference, physical, pivot,
            $"motion-{referenceBox}-{anchor}-{angle}-{scale}-{backend}-{engine}");
    }

    public static IEnumerable<object[]> MotionSwitchPixels()
    {
        foreach (var backend in MotionBackends())
        foreach (var mutation in new[] { "insets", "size", "box", "origin-source" })
            yield return [mutation, backend.Backend, backend.Engine];
    }

    [Theory, MemberData(nameof(MotionSwitchPixels))]
    public void MotionMutationPixelsMoveAndRestoreTheOriginalContent(string mutation, RenderBackend backend, RenderingEngine engine)
    {
        if (RunMotionChild(backend, engine, "motion-switch", mutation)) return;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = MotionSurface(backend);
        using var target = CreateMotionTarget(context, surface, engine);
        Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        var root = new Canvas(); var actual = MotionElement(); root.Children.Add(actual);
        Canvas.SetLeft(actual, 40); Canvas.SetTop(actual, 60); var text = actual.Child;
        var reference = MotionElement();
        Css.SetStyle(actual, Style("content-box", "auto", 0)); Layout(root);
        byte[]? first = null;
        foreach (var state in new[] { "initial", "changed", "restored" })
        {
            bool changed = state == "changed";
            if (mutation == "insets") actual.Padding = reference.Padding = changed ? new Thickness(32, 8, 28, 16) : new Thickness(12, 8, 28, 16);
            if (mutation == "size") { actual.Width = reference.Width = changed ? 220 : 160; actual.Height = reference.Height = changed ? 140 : 100; }
            if (mutation == "box") Css.SetStyle(actual, Style(changed ? "border-box" : "content-box", "auto", 0));
            if (mutation == "origin-source")
            {
                if (changed) actual.RenderTransformOrigin = default;
                else actual.ClearValue(UIElement.RenderTransformOriginProperty);
            }
            Layout(root); LayoutMotionReference(reference);
            var anchor = changed ? mutation switch
            {
                "insets" => new Point(80, 44), "size" => new Point(100, 64),
                "box" => new Point(80, 50), _ => default,
            } : new Point(70, 44);
            var pixels = CompareMotionPixels(context, target, root, reference, LiteralMotionMatrix(anchor, 0), default,
                $"motion-switch-{mutation}-{state}-{backend}-{engine}");
            if (state == "initial") first = pixels;
            if (state == "restored") Assert.Equal(first, pixels);
            Assert.Same(text, actual.Child); Assert.Same(actual, text!.VisualParent);
        }
    }

    private static IEnumerable<(RenderBackend Backend, RenderingEngine Engine)> MotionBackends()
    {
        yield return (RenderBackend.Software, RenderingEngine.Impeller);
        yield return (RenderBackend.Metal, RenderingEngine.Impeller);
        yield return (RenderBackend.Metal, RenderingEngine.Vello);
    }

    private static Border MotionElement() => new()
    {
        Width = 160, Height = 100, Padding = new Thickness(12, 8, 28, 16), BorderThickness = new Thickness(2, 4, 6, 8),
        Background = new SolidColorBrush(Color.FromRgb(225, 236, 247)), BorderBrush = new SolidColorBrush(Color.FromRgb(36, 79, 124)),
        Child = new TextBlock { Text = "CSS 東 AB", FontFamily = "Hiragino Sans", FontSize = 16, LineHeight = 20,
            Foreground = Brushes.Black, IsTextSelectionEnabled = true },
    };

    private static Matrix LiteralMotionMatrix(Point anchor, int angle)
    {
        double radians = angle * (Math.PI / 180), c = Math.Cos(radians), s = Math.Sin(radians);
        return new Matrix(c, s, -s, c, 200 - anchor.X * c + anchor.Y * s, 100 - anchor.X * s - anchor.Y * c);
    }

    private static void LayoutMotionReference(FrameworkElement element)
    {
        var size = new Size(element.Width, element.Height); element.Measure(size); element.Arrange(new Rect(0, 0, size.Width, size.Height));
    }

    private static byte[] CompareMotionPixels(RenderContext context, RenderTarget target,
        Canvas root, Border reference, Matrix matrix, Point pivot, string name)
    {
        var actual = ReadMotionPixels(context, target, root.Render);
        var expected = ReadMotionPixels(context, target, dc =>
        {
            var offset = (IOffsetDrawingContext)dc; var transforms = (ITransformDrawingContext)dc;
            var saved = offset.Offset; offset.Offset = new Point(40, 60);
            transforms.PushTransform(new MatrixTransform(matrix), pivot.X, pivot.Y);
            try { reference.Render(dc); } finally { transforms.PopTransform(); offset.Offset = saved; }
        });
        SaveMotion(name, "actual", actual); SaveMotion(name, "reference", expected);
        Assert.Contains(actual.Where((_, i) => i % 4 == 3), a => a > 0); Assert.Equal(expected, actual);
        return actual;
    }

    private static NativePlatformWindow? MotionSurface(RenderBackend backend)
    {
        if (backend != RenderBackend.Metal) return null;
        var surface = new NativePlatformWindow("CSS motion reference pixels", 1500, 500, 480, 400, 1, 0);
        surface.Show(activate: false); return surface;
    }

    private static RenderTarget CreateMotionTarget(RenderContext context, NativePlatformWindow? surface, RenderingEngine engine)
    {
        var target = surface is null ? context.CreateOffscreenRenderTarget(480, 400) : context.CreateRenderTarget(surface.GetSurface(), 480, 400);
        target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
        target.SetDpi(96, 96); return target;
    }

    private static byte[] ReadMotionPixels(RenderContext context, RenderTarget target, Action<DrawingContext> draw)
    {
        // Keep one target alive across successive frames, just as a Window does.
        // Retained layers and target-bound drawing resources outlive a frame.
        target.SetFullInvalidation(); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
        using (var dc = new RenderTargetDrawingContext(target, context)) { draw(dc); dc.Close(); }
        target.EndDraw(); var pixels = new byte[480 * 400 * 4];
        Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 480 * 4, out var width, out var height));
        Assert.Equal(480, width); Assert.Equal(400, height); return pixels;
    }

    private static void SaveMotion(string name, string kind, byte[] pixels)
    {
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var source = BitmapSource.Create(480, 400, 96, 96, PixelFormats.Bgra32, null, pixels, 480 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(Path.Combine(folder, $"{name}-{kind}.png")); encoder.Save(stream);
    }

    private static bool RunMotionChild(RenderBackend backend, RenderingEngine engine, params string[] arguments)
    {
        if (backend != RenderBackend.Metal || Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") == "1") return false;
        var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")
            ?? throw new InvalidOperationException("Metal main-thread runner is required."))!;
        var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in command.Skip(1).Concat(arguments).Append(engine.ToString())) start.ArgumentList.Add(arg);
        start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal motion runner exceeded 90 seconds."); }
        var receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, receipt); Assert.Contains("BACKEND Metal", receipt); Assert.Contains("ENGINE " + engine, receipt);
        return true;
    }
}
