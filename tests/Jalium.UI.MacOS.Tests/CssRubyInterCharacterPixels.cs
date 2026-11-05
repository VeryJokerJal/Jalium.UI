using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

public sealed partial class CssRubyInterCharacterTests
{
    public static IEnumerable<object[]> PixelCases()
    {
        foreach (var content in new[] { "one", "bopomofo", "tall", "latin", "han" })
        foreach (var align in new[] { "start", "center", "space-between", "space-around" })
        foreach (var direction in new[] { "ltr", "rtl" })
        {
            yield return [content, align, direction, RenderBackend.Software, RenderingEngine.Impeller];
            yield return [content, align, direction, RenderBackend.Metal, RenderingEngine.Impeller];
            yield return [content, align, direction, RenderBackend.Metal, RenderingEngine.Vello];
        }
    }

    [Theory, MemberData(nameof(PixelCases))]
    public void SideAnnotationPixelsMatchIndependentVerticalTextAtPhysicalSlots(string content, string align,
        string direction, RenderBackend backend, RenderingEngine engine)
    {
        if (backend == RenderBackend.Metal && Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_CHILD") != "1")
        {
            var command = System.Text.Json.JsonSerializer.Deserialize<string[]>(Environment.GetEnvironmentVariable("JALIUM_CSS_METAL_COMMAND")
                ?? throw new InvalidOperationException("Metal main-thread runner is required."))!;
            var start = new System.Diagnostics.ProcessStartInfo(command[0]) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in command.Skip(1).Concat(new[] { "side-ruby", content, align, direction, engine.ToString() })) start.ArgumentList.Add(arg);
            start.Environment["JALIUM_CSS_METAL_CHILD"] = "1";
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(90000)) { process.Kill(entireProcessTree: true); Assert.Fail("Metal side-ruby runner exceeded 90 seconds."); }
            string receipt = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, receipt); Assert.Contains("ENGINE " + engine, receipt); Assert.Contains("BACKEND Metal", receipt); return;
        }
        using var context = RenderContext.GetOrCreateCurrent(backend);
        using var surface = backend == RenderBackend.Metal ? new NativePlatformWindow("Jalium CSS side ruby pixels", 1500, 500, 320, 320, 1, 0) : null;
        surface?.Show(activate: false); Assert.Equal(backend, context.Backend); Console.WriteLine("BACKEND " + context.Backend);
        string value = content switch { "one" => "ㄓ", "bopomofo" => "ㄓㄨ", "tall" => "ㄓㄨㄥㄨ", "latin" => "Ab", _ => "注音" };
        var (ruby, basis, note) = Pair("horizontal-tb", "inter-character", direction, align); note.Text = value;
        Css.SetStyle(ruby, $"display:ruby;writing-mode:horizontal-tb;direction:{direction};ruby-position:inter-character;ruby-align:{align};font-family:'PingFang TC';font-size:32px;line-height:1.25;white-space:pre;text-autospace:no-autospace;color:black;");
        Layout(ruby, 240, 240);

        // No ruby role, computed Ruby mode, Ruby layout, or actual geometry is
        // used to build this reference. Upright cells advance 16px, the base
        // advances 32px. The base font area is independent of its 40px line;
        // the side annotation retains its own 20px inline-block line width.
        var canvas = new Canvas();
        var font = TextMeasurement.GetFontMetrics("PingFang TC", 32, 400, 0); double B = (double)font.Ascent + font.Descent;
        double natural = 16 * value.Length, height = Math.Max(B, natural), x = direction == "rtl" ? 188 : 0;
        // The side inline block contributes its full ascent as well as descent.
        // When the font exceeds the authored line, start alignment therefore
        // has no negative leading above either aligned content top.
        double baseTop = align == "start" ? Math.Max(0, 20 - B / 2) : height > 40 ? (height - B) / 2 : 20 - B / 2;
        double sideTop = align == "start" ? baseTop : Math.Max(0, 20 - height / 2);
        double extra = Math.Max(0, height - natural);
        bool distribute = content == "han" && align is "space-between" or "space-around";
        double edge = distribute && align == "space-around" ? extra / (2 * value.Length) : !distribute && align != "start" ? extra / 2 : 0;
        Add("字", 32, new(x, baseTop, 32, B), "horizontal-tb", false);
        Add(value, 16, new(x + 32, sideTop + edge, 20, distribute ? height - 2 * edge : natural), "vertical-rl", distribute);
        Layout(canvas, 240, 240);
        var actual = Pixels(ruby.Render); var expected = Pixels(canvas.Render);
        Save("actual", actual); Save("reference", expected);
        string? folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(folder))
            File.WriteAllText(Path.Combine(folder, $"side-ruby-{content}-{align}-{direction}-{backend}-{engine}-geometry.json"),
                System.Text.Json.JsonSerializer.Serialize(new { basis = ElementBounds(ruby, basis), note = ElementBounds(ruby, note),
                    baseGlyph = Bounds(ruby, basis, 0, 1), noteGlyph = Bounds(ruby, note, 0, value.Length), natural, height, x, baseTop, edge },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Assert.Contains(actual.Where((_, i) => i % 4 == 3), alpha => alpha > 0); Assert.Equal(expected, actual);

        void Add(string textValue, double size, Rect slot, string mode, bool justify)
        {
            var text = Text(textValue, size); text.Width = slot.Width; text.Height = slot.Height;
            var line = mode == "horizontal-tb" ? B.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px" : "20px";
            Css.SetStyle(text, $"font-family:'PingFang TC';font-size:{size}px;line-height:{line};white-space:pre;writing-mode:{mode};direction:{direction};text-orientation:upright;text-autospace:no-autospace;color:black;" +
                (justify ? "text-align:justify;text-align-last:justify;" : "text-align:start;"));
            Canvas.SetLeft(text, slot.X); Canvas.SetTop(text, slot.Y); canvas.Children.Add(text);
        }
        byte[] Pixels(Action<DrawingContext> draw)
        {
            using var target = surface is null ? context.CreateOffscreenRenderTarget(320, 320) : context.CreateRenderTarget(surface.GetSurface(), 320, 320);
            target.SetRenderingEngine(engine); Assert.Equal(engine, target.RenderingEngine); Console.WriteLine("ENGINE " + target.RenderingEngine);
            target.SetDpi(96, 96); target.RequestReadback(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
            using (var dc = new RenderTargetDrawingContext(target, context)) { dc.PushTransform(new TranslateTransform(32, 32)); draw(dc); dc.Pop(); dc.Close(); }
            target.EndDraw(); var pixels = new byte[320 * 320 * 4]; Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, 320 * 4, out _, out _)); return pixels;
        }
        void Save(string kind, byte[] pixels)
        {
            var folder = Environment.GetEnvironmentVariable("JALIUM_VERTICAL_SCREENSHOT_DIR"); if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder); var bitmap = BitmapSource.Create(320, 320, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, $"side-ruby-{content}-{align}-{direction}-{backend}-{engine}-{kind}.png")); encoder.Save(stream);
        }
    }
}
