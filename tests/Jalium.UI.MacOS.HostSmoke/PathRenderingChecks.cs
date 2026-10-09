using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

/// <summary>Exercises managed geometry dispatch against explicit paths on the real GPU.</summary>
[SupportedOSPlatform("macos15.0")]
internal static class PathRenderingChecks
{
    private const int Extent = 128;

    internal static int RunAll()
    {
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Require(context.Backend == RenderBackend.Metal, "Metal backend unavailable");
        var solid = new SolidColorBrush(Color.FromArgb(128, 220, 40, 90));
        var gradient = new LinearGradientBrush(Color.FromArgb(128, 20, 160, 210),
            Color.FromArgb(128, 210, 80, 20), new Point(0, 0), new Point(1, 1));
        var image = new ImageBrush(BitmapImage.FromPixels(
            new byte[] { 10, 180, 230, 255, 230, 60, 40, 255, 80, 220, 20, 255, 180, 30, 210, 255 }, 2, 2, 8))
            { Opacity = 0.5 };
        var managedImage = new ImageBrush(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 10, 180, 230, 255, 230, 60, 40, 255, 80, 220, 20, 255, 180, 30, 210, 255 }, 8))
            { Opacity = 0.5 };
        var pen = new Pen(solid, 7.3) { StartLineCap = PenLineCap.Triangle, EndLineCap = PenLineCap.Square,
            DashCap = PenLineCap.Round, LineJoin = PenLineJoin.Bevel, DashStyle = new DashStyle([2, 1, 3], -1.7) };
        var gradientPen = new Pen(gradient, 7.3) { StartLineCap = PenLineCap.Triangle,
            EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Bevel };
        var imagePen = new Pen(image, 7.3) { StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Square, DashCap = PenLineCap.Triangle,
            DashStyle = new DashStyle([2, 1], 0.7) };
        var managedImagePen = new Pen(managedImage, 7.3) { StartLineCap = imagePen.StartLineCap,
            EndLineCap = imagePen.EndLineCap, DashCap = imagePen.DashCap, DashStyle = imagePen.DashStyle };
        (Brush? Fill, Pen? Stroke)[] styles = [(solid, null), (null, pen), (gradient, gradientPen),
            (image, imagePen), (managedImage, managedImagePen)];
        int passed = 0, total = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int dpi in new[] { 1, 2 })
        {
            using var view = new NSView(new CGRect(0, 0, Extent, Extent));
            using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), Extent * dpi, Extent * dpi);
            target.SetDpi(96 * dpi, 96 * dpi); target.SetRenderingEngine(engine);
            Require(target.RenderingEngine == engine,
                "Requested rendering engine unavailable; provide JALIUM_METALLIB_DIR with the generated shaders");
            using var drawing = new RenderTargetDrawingContext(target, context);
            byte[] Capture(Action paint)
            {
                Require(target.RequestReadback() == JaliumResult.Ok, "GPU readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(0, 0, 0, 0);
                try { paint(); }
                finally { target.EndDraw(); }
                var pixels = new byte[Extent * dpi * Extent * dpi * 4];
                Require(target.FetchReadback(pixels, (uint)(Extent * dpi * 4), out int width, out int height) == JaliumResult.Ok
                    && width == Extent * dpi && height == Extent * dpi, "GPU readback dimensions differ");
                Require(target.RenderingEngine == engine, "Requested rendering engine was not selected");
                return pixels;
            }
            foreach (int kind in Enumerable.Range(0, 4))
            foreach (int style in Enumerable.Range(0, styles.Length))
            foreach (var edge in new[] { EdgeMode.Unspecified, EdgeMode.Aliased, EdgeMode.Antialiased })
            foreach (bool transformed in new[] { false, true })
            {
                total++;
                string name = $"{engine}-{dpi}x-geometry-{kind}-style-{style}-{edge}-transform-{transformed}";
                try
                {
                    var (geometry, reference) = GeometryPair(kind);
                    if (transformed)
                    {
                        geometry.Transform = new MatrixTransform(new Matrix(0.85, 0.12, -0.18, 0.9, 20, 4));
                        reference.Transform = geometry.Transform;
                    }
                    var (fill, stroke) = styles[style];
                    byte[] actual = Capture(() => drawing.DrawGeometry(fill, stroke, geometry, edge));
                    byte[] expected = Capture(() => drawing.DrawGeometry(fill, stroke, reference, edge));
                    Save(name, dpi, actual);
                    if (kind != 3 || style != 0)
                        Require(Enumerable.Range(0, actual.Length / 4).Any(i => actual[i * 4 + 3] > 10),
                            "Primitive did not produce visible fill or stroke pixels");
                    int differing = actual.Zip(expected).Count(pair => Math.Abs(pair.First - pair.Second) > 1);
                    Require(differing == 0, $"Primitive differs from the explicit curve/pen path in {differing} channels");
                    if (style == 4)
                    {
                        byte[] bitmapImagePixels = Capture(() => drawing.DrawGeometry(image, imagePen, geometry, edge));
                        Require(actual.Zip(bitmapImagePixels).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                            "CopyPixels-backed image differs from the BitmapImage upload");
                    }
                    if (style == 0 && kind != 3)
                    {
                        var alpha = Enumerable.Range(0, actual.Length / 4).Select(i => actual[i * 4 + 3]).ToArray();
                        Require(alpha.Any(a => a >= 127), "Filled primitive disappeared");
                        if (edge == EdgeMode.Aliased)
                            Require(alpha.All(a => a <= 1 || a is >= 127 and <= 129), "Aliased primitive contains fractional edge coverage");
                        if (edge == EdgeMode.Antialiased && kind != 1)
                            Require(alpha.Any(a => a is > 1 and < 127), "Antialiased curve lost edge coverage");
                    }
                    passed++;
                }
                catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
            }
        }
        Console.WriteLine($"macOS managed-to-Metal primitive Path GPU checks: {passed}/{total} passed");
        return passed == total ? 0 : 1;
    }

    private static (Geometry Geometry, PathGeometry Reference) GeometryPair(int kind)
    {
        Geometry geometry = kind switch
        {
            0 => new EllipseGeometry(new Point(62.3, 60.7), 38.2, 26.1),
            1 => new RectangleGeometry(new Rect(24.1, 28.2, 76.4, 52.2)),
            2 => new RectangleGeometry(new Rect(24.1, 28.2, 76.4, 52.2), 13.2, 8.4),
            _ => new LineGeometry(new Point(26.2, 32.3), new Point(97.4, 84.6)),
        };
        var reference = new PathGeometry();
        var figure = new PathFigure { IsFilled = kind != 3, IsClosed = kind != 3 };
        void Line(double x, double y, bool smooth = false) => figure.Segments.Add(new LineSegment(new Point(x, y)) { IsSmoothJoin = smooth });
        void Arc(double x, double y, double rx, double ry) => figure.Segments.Add(
            new ArcSegment(new Point(x, y), new Size(rx, ry), 0, false, SweepDirection.Clockwise, true) { IsSmoothJoin = true });
        switch (kind)
        {
            case 0:
                figure.StartPoint = new Point(62.3 + 38.2, 60.7);
                Arc(62.3, 60.7 + 26.1, 38.2, 26.1); Arc(62.3 - 38.2, 60.7, 38.2, 26.1);
                Arc(62.3, 60.7 - 26.1, 38.2, 26.1); Arc(figure.StartPoint.X, figure.StartPoint.Y, 38.2, 26.1);
                break;
            case 1:
                figure.StartPoint = new Point(24.1, 28.2);
                Line(24.1 + 76.4, 28.2); Line(24.1 + 76.4, 28.2 + 52.2); Line(24.1, 28.2 + 52.2);
                break;
            case 2:
                double left = 24.1, top = 28.2, right = left + 76.4, bottom = top + 52.2, rx = 13.2, ry = 8.4;
                figure.StartPoint = new Point(left + rx, top);
                Line(right - rx, top, true); Arc(right, top + ry, rx, ry);
                Line(right, bottom - ry, true); Arc(right - rx, bottom, rx, ry);
                Line(left + rx, bottom, true); Arc(left, bottom - ry, rx, ry);
                Line(left, top + ry, true); Arc(left + rx, top, rx, ry);
                break;
            default:
                figure.StartPoint = new Point(26.2, 32.3); Line(97.4, 84.6); break;
        }
        reference.Figures.Add(figure);
        return (geometry, reference);
    }

    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Save(string name, int dpi, byte[] pixels)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_MANAGED_PATH_CAPTURE_DIR") is not string directory) return;
        // Keep representative captures; the full matrix is recorded in the test log.
        if (!name.Contains("-style-3-Antialiased-transform-True", StringComparison.Ordinal)) return;
        Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, name);
        File.WriteAllBytes(path + ".bgra", pixels);
        File.WriteAllText(path + ".json", $"{{\"Width\":{Extent * dpi},\"Height\":{Extent * dpi}}}\n");
    }
}
