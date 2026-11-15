using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Checks rendered subpixel motion through managed drawing and CoreText on the real GPU.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class SubpixelMotionChecks
{
    private const int Extent = 128;

    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Require(context.Backend == RenderBackend.Metal, "Metal backend unavailable");
        int passed = 0, total = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 })
        foreach (uint samples in new uint[] { 1, 4 })
        {
            using var view = new NSView(new CGRect(0, 0, Extent, Extent));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), Extent * dpi, Extent * dpi);
            target.SetDpi(96 * dpi, 96 * dpi);
            target.SetRenderingEngine(engine);
            target.SetPathMsaaSampleCount(samples);
            Require(target.RenderingEngine == engine, "Requested rendering engine unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context);
            var text = new FormattedText("MMMM", "Helvetica", 16) { Foreground = Brushes.White };
            var textBlock = new TextBlock { Text = "MMMM", FontFamily = new FontFamily("Helvetica"), FontSize = 16,
                Foreground = Brushes.White, Width = 100, Height = 32 };
            textBlock.Measure(new Size(100, 32));
            textBlock.Arrange(new Rect(0, 0, 100, 32));
            var path = Geometry.Parse("M0,0 L32,0 L32,16 L0,16 Z");
            var pen = new Pen(Brushes.White, 1);

            byte[] Readback()
            {
                var pixels = new byte[Extent * dpi * Extent * dpi * 4];
                Require(target.FetchReadback(pixels, (uint)(Extent * dpi * 4), out int width, out int height) == JaliumResult.Ok
                    && width == Extent * dpi && height == Extent * dpi, "Readback dimensions differ");
                return pixels;
            }

            byte[] Capture(int scene, int placement, int axis, double shift)
            {
                Require(target.RequestReadback() == JaliumResult.Ok, "Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
                double dx = axis == 0 ? shift / dpi : 0, dy = axis == 1 ? shift / dpi : 0;
                drawing.Offset = new Point(16 + (placement == 0 ? dx : 0), 16 + (placement == 0 ? dy : 0));
                if (placement != 0)
                    drawing.PushTransform(new MatrixTransform(new Matrix(placement == 2 ? 1.037 : 1, 0, 0,
                        placement == 2 ? .973 : 1, dx, dy)));
                try
                {
                    switch (scene)
                    {
                        case 0: drawing.DrawText(text, Point.Zero); break;
                        case 1: textBlock.Render(drawing); break;
                        case 2: drawing.DrawGeometry(null, pen, path, EdgeMode.Antialiased); break;
                        case 3: drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 32, 16)); break;
                    }
                }
                finally
                {
                    if (placement != 0) drawing.Pop();
                    target.EndDraw();
                }
                var pixels = Readback();
                if (engine == RenderingEngine.Impeller && samples == 4 && placement == 0 && axis == 1 &&
                    (shift == 0 || shift == .125) && Environment.GetEnvironmentVariable("JALIUM_SUBPIXEL_CAPTURE_DIR") is string directory)
                {
                    Directory.CreateDirectory(directory);
                    string name = System.IO.Path.Combine(directory, $"scene{scene}-{dpi}x-phase{shift:F3}");
                    File.WriteAllBytes(name + ".bgra", pixels);
                    File.WriteAllText(name + ".json", $"{{\"Width\":{Extent * dpi},\"Height\":{Extent * dpi}}}\n");
                }
                return pixels;
            }

            foreach (int scene in Enumerable.Range(0, 4))
            foreach (int placement in Enumerable.Range(0, 3))
            foreach (int axis in Enumerable.Range(0, 2))
            {
                total++;
                string name = $"{engine}-{dpi}x-msaa{samples}-scene{scene}-placement{placement}-axis{axis}";
                try
                {
                    var baseline = Capture(scene, placement, axis, 0);
                    double origin = Centroid(baseline, Extent * dpi, axis);
                    double worst = 0;
                    byte[]? previous = null;
                    // Step forward, then backward across device-pixel boundaries.
                    foreach (int step in Enumerable.Range(-8, 25).Concat(Enumerable.Range(-8, 25).Reverse()))
                    {
                        double shift = step * .125;
                        var pixels = Capture(scene, placement, axis, shift);
                        double error = Math.Abs(Centroid(pixels, Extent * dpi, axis) - origin - shift);
                        worst = Math.Max(worst, error);
                        Require(error < .08, $"{shift:F3}px motion deviated by {error:F4}px");
                        // Adjacent distinct phases must produce distinct images. The
                        // turn-around frame is intentionally rendered twice.
                        if (previous is not null && step != 16)
                            Require(!pixels.AsSpan().SequenceEqual(previous), "A fractional frame repeated a whole-pixel image");
                        previous = pixels;
                    }
                    Require(Capture(scene, placement, axis, 0).AsSpan().SequenceEqual(baseline),
                        "Returning to rest changed the cached/static pixels");
                    passed++;
                    Console.WriteLine($"PASS {name}: max placement error {worst:F4}px");
                }
                catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
            }

            total++;
            try
            {
                using var format = context.CreateTextFormat("Helvetica", 16);
                using var nativeBrush = context.CreateSolidBrush(1, 1, 1);
                byte[] NativeText()
                {
                    Require(target.RequestReadback() == JaliumResult.Ok, "Native text readback request failed");
                    target.SetFullInvalidation(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
                    target.DrawText("MMMM", format, 16 + .125f / dpi, 16 + .125f / dpi, 100, 28, nativeBrush);
                    target.EndDraw();
                    return Readback();
                }
                var ideal = NativeText();
                format.SetTextFormattingMode((int)TextFormattingMode.Display);
                var display = NativeText();
                Require(!ideal.AsSpan().SequenceEqual(display), "Display policy reused the cached Ideal glyph pixels");
                format.SetSubpixelPositioning(true);
                Require(NativeText().AsSpan().SequenceEqual(ideal), "Explicit subpixel placement did not override Display");
                format.SetSubpixelPositioning(false);
                format.SetTextHintingMode((int)TextHintingMode.Animated);
                Require(NativeText().AsSpan().SequenceEqual(ideal), "Animated text retained Display snapping");
                format.SetTextHintingMode((int)TextHintingMode.Fixed);
                Require(NativeText().AsSpan().SequenceEqual(display), "Fixed Display text lost its explicit snapping policy");
                format.SetTextFormattingMode((int)TextFormattingMode.Ideal);
                Require(NativeText().AsSpan().SequenceEqual(ideal), "Returning to Ideal reused stale Display pixels");
                passed++;
                Console.WriteLine($"PASS {engine}-{dpi}x-msaa{samples}-text-policy-cache");
            }
            catch (Exception error) { Console.Error.WriteLine($"FAIL text-policy-cache: {error.Message}"); }
        }
        Console.WriteLine($"Subpixel motion: {passed}/{total} GPU scenarios passed");
        return passed == total ? 0 : 1;
    }

    private static double Centroid(byte[] pixels, int width, int axis)
    {
        double mass = 0, moment = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            int pixel = offset / 4;
            double alpha = pixels[offset + 3];
            mass += alpha;
            moment += alpha * ((axis == 0 ? pixel % width : pixel / width) + .5);
        }
        Require(mass > 0, "Scene rendered no visible pixels");
        return moment / mass;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
