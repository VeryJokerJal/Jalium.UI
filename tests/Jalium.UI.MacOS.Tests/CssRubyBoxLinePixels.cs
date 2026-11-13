using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed partial class CssRubyBoxLineTests
{
    public static IEnumerable<object[]> LinePixels()
    {
        foreach (var mode in new[] { "horizontal-tb", "vertical-rl", "vertical-lr", "sideways-rl", "sideways-lr" })
        foreach (var direction in new[] { "ltr", "rtl" })
        foreach (var merge in new[] { "separate", "merge", "auto" })
        foreach (var scene in new[] { "tight", "roomy", "under", "zero", "levels", "structure" })
        {
            yield return [mode, direction, merge, scene, RenderBackend.Software, RenderingEngine.Impeller];
            yield return [mode, direction, merge, scene, RenderBackend.Metal, RenderingEngine.Impeller];
            yield return [mode, direction, merge, scene, RenderBackend.Metal, RenderingEngine.Vello];
        }
    }

    [Theory, MemberData(nameof(LinePixels))]
    public void RubyLinePixelsMatchIndependentFontAreasAndPhysicalSlots(string mode, string direction, string merge, string scene,
        RenderBackend backend, RenderingEngine engine)
    {
        if (backend == RenderBackend.Metal && Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") != "1")
        {
            var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")
                ?? throw new InvalidOperationException("Metal main-thread runner is required."))!;
            var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in command.Skip(1).Concat(new[] { "ruby-line", mode, direction, merge, scene, engine.ToString() })) start.ArgumentList.Add(arg);
            start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal Ruby line runner exceeded 90 seconds."); }
            string receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, receipt); Assert.Contains("ENGINE " + engine, receipt); Assert.Contains("BACKEND Metal", receipt); return;
        }
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = backend == RenderBackend.Metal ? new NativePlatformWindow("Jalium CSS Ruby line pixels", 1500, 500, 400, 400, 1, 0) : null;
        surface?.Show(activate: false); Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        double height = scene is "roomy" or "structure" ? 80 : scene == "zero" ? 0 : 40;
        string position = scene == "levels" ? "alternate" : scene is "under" or "zero" ? "under" : "over";
        var actualRoot = new StackPanel(); var originals = new List<TextBlock>(); var containers = new List<Border>();
        for (int row = 0; row < 3; row++)
        {
            var basis = Text("東", 32); var note = Text("とう", 16); originals.Add(basis); originals.Add(note);
            Css.SetStyle(basis, "display:ruby-base;font-size:32px;");
            Css.SetStyle(note, "display:ruby-text;font-size:16px;line-height:96px;");
            if (scene == "structure")
            {
                foreach (var (child, role) in new[] { (basis, "ruby-base-container"), (note, "ruby-text-container") })
                {
                    var box = new Border { Child = child }; actualRoot.Children.Add(box); containers.Add(box);
                    Css.SetStyle(box, $"display:{role};margin:37px;padding:23px;border:7px solid red;width:190px;height:170px;min-width:180px;min-height:160px;" +
                        "border-image-source:linear-gradient(red,blue);border-image-slice:1;border-image-width:9px;");
                }
            }
            else
            {
                actualRoot.Children.Add(basis);
                if (scene != "levels") actualRoot.Children.Add(note);
                else
                {
                    var over = new StackPanel(); over.Children.Add(note); actualRoot.Children.Add(over); Css.SetStyle(over, "display:ruby-text-container;");
                    var second = new StackPanel(); var small = Text("と", 8); originals.Add(small); second.Children.Add(small); actualRoot.Children.Add(second);
                    Css.SetStyle(second, "display:ruby-text-container;"); Css.SetStyle(small, "display:ruby-text;font-size:8px;line-height:190px;");
                }
            }
        }
        Css.SetStyle(actualRoot, RootStyle(mode, merge, height) + $"direction:{direction};white-space:pre-wrap;ruby-position:{position};color:black;");
        var available = mode == "horizontal-tb" ? new Size(34, 300) : new Size(300, 34); Layout(actualRoot, available);

        // Independent reference: no Ruby roles, normalization, allocated slots,
        // Ruby layout helpers, or actual geometry. Font areas come from the
        // font provider; positions below are the explicit six scene contracts.
        var reference = new Canvas();
        double B = FontHeight(32), N = FontHeight(16), S = FontHeight(8);
        double advance = scene is "roomy" or "structure" ? 80 : scene == "levels" ? B + N + S : B + N;
        double baseTop = scene switch
        {
            "roomy" or "structure" => 40 - B / 2,
            "under" => 20 - B / 2,
            "zero" => -B / 2,
            "levels" => 20 - B / 2 + 2 * (B + N + S - 40) / 3,
            _ => B / 2 + N - 20,
        };
        double inline = direction == "rtl" ? 2 : 0;
        for (int row = 0; row < 3; row++)
        {
            Add("東", 32, row, inline, baseTop, 32, B);
            Add("とう", 16, row, inline, baseTop + (scene is "under" or "zero" ? B : -N), 32, N);
            if (scene == "levels") Add("と", 8, row, inline + 12, baseTop + B, 8, S);
        }
        Layout(reference, new Size(400, 400));
        var actual = Pixels(actualRoot.Render); var expected = Pixels(reference.Render); Save("actual", actual); Save("reference", expected);
        Assert.All(originals, text =>
        {
            var matrix = text.GetRenderMatrix();
            Assert.All(new[] { matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.OffsetX, matrix.OffsetY,
                text.RenderSize.Width, text.RenderSize.Height }, value => Assert.True(double.IsFinite(value)));
        });
        var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(folder))
            File.WriteAllText(Path.Combine(folder, $"ruby-line-{mode}-{direction}-{merge}-{scene}-{backend}-{engine}-geometry.json"),
                System.Text.Json.JsonSerializer.Serialize(new { B, N, S, advance, baseTop, actualRoot.DesiredSize,
                    sources = originals.Select(text => new { text.Text, text.LineHeight, text.RenderSize, matrix = text.GetRenderMatrix(), bounds = text.DocumentTextBounds(0, text.Text.Length) }),
                    structural = containers.Select(box => new { box.Width, box.Height, box.Margin, box.Padding, box.BorderThickness, box.RenderSize }),
                    reference = reference.Children.OfType<TextBlock>().Select(text => new { text.Text, text.RenderSize, matrix = text.GetRenderMatrix(), bounds = text.DocumentTextBounds(0, text.Text.Length) }) },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Assert.Contains(actual.Where((_, i) => i % 4 == 3), alpha => alpha > 0); Assert.Equal(expected, actual);

        static TextBlock Text(string value, double size) => new() { Text = value, FontSize = size, IsTextSelectionEnabled = true };
        static double FontHeight(double size) { var font = TextMeasurement.GetFontMetrics("Hiragino Sans", size, 400, 0); return (double)font.Ascent + font.Descent; }
        void Add(string value, double size, int row, double x, double y, double width, double cross)
        {
            double top = row * advance + y;
            Rect slot = mode switch
            {
                "horizontal-tb" => new(x, top, width, cross),
                "vertical-lr" => new((row + 1) * advance - y - cross, x, cross, width),
                "sideways-lr" => new(top, 34 - x - width, cross, width),
                _ => new(300 - top - cross, x, cross, width),
            };
            var text = Text(value, size); text.Width = slot.Width; text.Height = slot.Height;
            Css.SetStyle(text, $"font-family:'Hiragino Sans';font-size:{size}px;line-height:{cross.ToString("R", CultureInfo.InvariantCulture)}px;writing-mode:{mode};direction:{direction};white-space:pre;text-autospace:no-autospace;color:black;");
            Canvas.SetLeft(text, slot.X); Canvas.SetTop(text, slot.Y); reference.Children.Add(text);
        }
        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = surface is null ? context.CreateOffscreenRenderTarget(400, 400) : context.CreateRenderTarget(surface.GetSurface(), 400, 400);
            target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
            target.SetDpi(96, 96); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { dc.PushTransform(new TranslateTransform(32, 32)); draw(dc); dc.Pop(); dc.Close(); }
            target.EndDraw(); var pixels = new byte[400 * 400 * 4]; Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 400 * 4, out _, out _)); return pixels;
        }
        void Save(string kind, byte[] pixels)
        {
            var path = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path); var image = BitmapSource.Create(400, 400, 96, 96, PixelFormats.Bgra32, null, pixels, 400 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(path, $"ruby-line-{mode}-{direction}-{merge}-{scene}-{backend}-{engine}-{kind}.png")); encoder.Save(stream);
        }
    }
}
