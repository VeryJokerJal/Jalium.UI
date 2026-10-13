using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>
/// Reads real Metal pixels produced by Window's full and dirty-region background
/// paths. No desktop capture, application activation or accessibility inspection.
/// </summary>
[SupportedOSPlatform("macos15.0")]
internal static class WindowBackgroundAlphaChecks
{
    internal static int RunAll()
    {
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Require(context.Backend == RenderBackend.Metal, "Metal backend unavailable");
        context.DefaultRenderingEngine = RenderingEngine.Impeller;
        MethodInfo clear = typeof(Window).GetMethod("ClearBackground", BindingFlags.NonPublic | BindingFlags.Instance,
            null, [typeof(RenderTarget), typeof(Rect?)], null)!;
        Color[] colors = [Color.FromArgb(0, 245, 250, 252), Color.FromArgb(64, 60, 170, 220),
            Color.FromArgb(96, 245, 250, 252), Color.FromArgb(128, 210, 90, 160),
            Color.FromArgb(255, 60, 170, 220)];
        int passed = 0;
        foreach (RenderingEngine engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 })
        foreach (Color color in colors)
        {
            string name = $"{engine}-alpha-{color.A}-{dpi}x";
            try
            {
                using var view = new NSView(new CGRect(0, 0, 48, 48));
                using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), 48 * dpi, 48 * dpi);
                Require(target.Backend == RenderBackend.Metal, "Target did not retain Metal backend");
                target.SetRenderingEngine(engine);
                target.SetDpi(96 * dpi, 96 * dpi);
                var window = new Window { AllowsTransparency = true, Background = new SolidColorBrush(color) };
                byte[] Capture(Action paint)
                {
                    Require(target.RequestReadback() == JaliumResult.Ok, "Readback request failed");
                    target.SetFullInvalidation(); target.BeginDraw();
                    Require(target.RenderingEngine == engine, "Requested rendering engine was not selected");
                    try { paint(); }
                    finally { target.EndDraw(); }
                    byte[] result = new byte[48 * dpi * 48 * dpi * 4];
                    Require(target.FetchReadback(result, (uint)(48 * dpi * 4), out int width, out int height) == JaliumResult.Ok
                        && width == 48 * dpi && height == 48 * dpi, "Readback dimensions differ");
                    return result;
                }
                byte[] Pixel(byte[] bytes, int x, int y) => bytes.AsSpan(((y * dpi) * (48 * dpi) + x * dpi) * 4, 4).ToArray();
                byte[] reference = Capture(() =>
                {
                    target.Clear(0, 0, 0, 0);
                    using var brush = context.CreateSolidBrush(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
                    target.FillRectangle(0, 0, 48, 48, brush);
                });
                byte[] expected = [(byte)Math.Round(color.B * color.A / 255.0),
                    (byte)Math.Round(color.G * color.A / 255.0), (byte)Math.Round(color.R * color.A / 255.0), color.A];
                Require(Near(Pixel(reference, 16, 16), expected), "Independent native brush does not match premultiplied pixel expectation");
                byte[] full = Capture(() => clear.Invoke(window, [target, null]));
                Save(name + "-reference", dpi, reference); Save(name + "-full", dpi, full);
                bool fullCorrect = Near(Pixel(full, 16, 16), expected);
                var seed = Color.FromRgb(12, 28, 44);
                window.Background = new SolidColorBrush(seed);
                byte[] initial = Capture(() => clear.Invoke(window, [target, null]));
                window.Background = new SolidColorBrush(color);
                bool partialCorrect = true;
                byte[] partial = initial;
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    partial = Capture(() =>
                    {
                        target.PushClip(8, 8, 20, 20);
                        try { clear.Invoke(window, [target, (Rect?)new Rect(8, 8, 20, 20)]); }
                        finally { target.PopClip(); }
                    });
                    partialCorrect &= Near(Pixel(partial, 16, 16), expected)
                        && Pixel(partial, 3, 3).SequenceEqual(Pixel(initial, 3, 3));
                }
                Save(name + "-partial", dpi, partial);
                Console.WriteLine($"{name}: expected BGRA={string.Join(',', expected)}, reference={string.Join(',', Pixel(reference,16,16))}, " +
                    $"full={string.Join(',', Pixel(full,16,16))}, partial={string.Join(',', Pixel(partial,16,16))}, outside retained={Pixel(partial,3,3).SequenceEqual(Pixel(initial,3,3))}");
                Require(fullCorrect && partialCorrect, "Window background differs from native brush or repeated dirty redraw changed background/outside pixels");
                passed++;
            }
            catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
        }
        Console.WriteLine($"macOS Window background alpha GPU checks: {passed}/20 passed");
        return passed == 20 ? 0 : 1;
    }

    private static bool Near(byte[] actual, byte[] expected) => actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 1);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Save(string name, int dpi, byte[] pixels)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_WINDOW_ALPHA_CAPTURE_DIR") is not string directory) return;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".bgra"), pixels);
        File.WriteAllText(Path.Combine(directory, name + ".json"), $"{{\"Width\":{48 * dpi},\"Height\":{48 * dpi},\"Dpi\":{dpi}}}\n");
    }
}
