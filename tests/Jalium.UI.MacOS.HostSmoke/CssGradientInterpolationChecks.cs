using System.Globalization;
using System.Text.Json;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal sealed record GradientInterpolationCase(string Name, string Caption, bool LinearLight, bool Radial, bool Alpha, bool Extended)
    {
        internal Color First => Extended ? Color.FromScRgb(.77f, 1.2f, -.2f, .2f) : Color.FromArgb(Alpha ? (byte)77 : (byte)255, 31, 123, 207);
        internal Color Last => Extended ? Color.FromScRgb(.43f, -.2f, 1.2f, .8f) : Color.FromArgb(Alpha ? (byte)207 : (byte)255, 211, 74, 16);
        internal double Opacity => Alpha && !Extended ? .6 : 1;
        internal GradientBrush Brush()
        {
            GradientBrush brush = Radial ? new RadialGradientBrush { Center = new(0, 24), GradientOrigin = new(0, 24), RadiusX = 128, RadiusY = 128 }
                : new LinearGradientBrush { StartPoint = new(0, 24), EndPoint = new(128, 24) };
            brush.MappingMode = BrushMappingMode.Absolute; brush.Opacity = Opacity;
            brush.ColorInterpolationMode = LinearLight ? ColorInterpolationMode.ScRgbLinearInterpolation : ColorInterpolationMode.SRgbLinearInterpolation;
            brush.GradientStops.Add(new(First, 0)); brush.GradientStops.Add(new(Last, 1)); return brush;
        }
        internal string Svg(bool mask = false, bool stroke = false)
        {
            var gradient = Radial ? "radialGradient" : "linearGradient";
            var geometry = Radial ? "cx='0' cy='24' r='128'" : "x1='0' y1='24' x2='128' y2='24'";
            var space = LinearLight ? "linearRGB" : "sRGB";
            var definition = "<defs><" + gradient + " id='g' gradientUnits='userSpaceOnUse' " + geometry + "><stop stop-color='#1f7bcf' stop-opacity='" + First.ScA.ToString("R", CultureInfo.InvariantCulture) + "'/><stop offset='1' stop-color='#d34a10' stop-opacity='" + Last.ScA.ToString("R", CultureInfo.InvariantCulture) + "'/></" + gradient + "></defs>";
            var paint = stroke ? "<path d='M0,24H160' fill='none' stroke='url(#g)' stroke-width='24' stroke-opacity='" + Opacity.ToString("R", CultureInfo.InvariantCulture) + "'/>"
                : "<rect width='160' height='48' fill='url(#g)' fill-opacity='" + Opacity.ToString("R", CultureInfo.InvariantCulture) + "'/>";
            var body = definition + paint;
            if (mask) body = "<defs><mask id='alpha' maskUnits='userSpaceOnUse' x='0' y='0' width='160' height='48' mask-type='alpha'>" + body + "</mask></defs><rect width='160' height='48' fill='#146342' mask='url(#alpha)'/>";
            return "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='48'><style>svg{--space:" + space + "} #g{color-interpolation:var(--space)}</style>" + body + "</svg>";
        }
    }

    internal static GradientInterpolationCase[] GradientInterpolationCases()
    {
        var rows = new List<GradientInterpolationCase>();
        foreach (var alpha in new[] { false, true }) foreach (var radial in new[] { false, true }) foreach (var linear in new[] { false, true })
            rows.Add(new($"{(radial ? "radial" : "linear")}-{(linear ? "light" : "srgb")}-{(alpha ? "alpha" : "opaque")}",
                $"{(radial ? "径向" : "线性")} · {(linear ? "线性光" : "sRGB")} · {(alpha ? "半透明" : "不透明")}", linear, radial, alpha, false));
        foreach (var radial in new[] { false, true }) foreach (var linear in new[] { false, true })
            rows.Add(new($"extended-{(radial ? "radial" : "linear")}-{(linear ? "light" : "srgb")}",
                $"扩展 scRGB · {(radial ? "径向" : "线性")} · {(linear ? "线性光" : "sRGB")}", linear, radial, true, true));
        return rows.ToArray();
    }

    internal static int RunGradientInterpolation()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        File.WriteAllText(Path.Combine(Output, "interpolation-cases.json"), JsonSerializer.Serialize(GradientInterpolationCases().Where(x => !x.Extended).Select(x => new { x.Name, x.Caption, Source = x.Svg() })));
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var dpi in new[] { 1, 2 }) foreach (var recorded in new[] { false, true }) foreach (var affine in new[] { false, true })
            foreach (var row in GradientInterpolationCases())
            foreach (var route in row.Extended ? new[] { "drawing", "image", "brush", "stroke" } : new[] { "drawing", "image", "brush", "stroke", "mask" })
            {
                context.DefaultRenderingEngine = engine;
                var label = $"interpolation-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";
                var errors = new List<string>(); var largest = 0; var samples = 0;
                try
                {
                    using var svg = row.Extended ? null : SvgImage.FromSvgString(row.Svg());
                    using var stroked = row.Extended ? null : SvgImage.FromSvgString(row.Svg(stroke: true));
                    using var mask = row.Extended ? null : SvgImage.FromSvgString(row.Svg(mask: true));
                    Drawing drawing = svg?.Drawing ?? new GeometryDrawing(row.Brush(), null, new RectangleGeometry(new(0, 0, 160, 48)));
                    ImageSource source = svg is null ? new DrawingImage(drawing) : svg;
                    var matrix = affine ? new Matrix(.86, .12, -.08, .92, 24, 16) : new Matrix(1, 0, 0, 1, 16, 12);
                    var pixels = Pixels(MacOSWritingToolsBitmapCapture.Capture(208 * dpi, 100 * dpi, dpi, dc =>
                    {
                        void Paint(DrawingContext target)
                        {
                            if (route == "drawing") drawing.RenderTo(target);
                            else if (route == "image") target.DrawImage(source, new Rect(0, 0, 160, 48));
                            else if (route == "brush") target.DrawRectangle(new ImageBrush(source) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 48));
                            else if (route == "mask") target.DrawImage(mask!, new Rect(0, 0, 160, 48));
                            else if (stroked is not null) stroked.Drawing!.RenderTo(target);
                            else target.DrawLine(new Pen(row.Brush(), 24), new Point(0, 24), new Point(160, 24));
                        }
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); recorder.PushTransform(new MatrixTransform(matrix)); Paint(recorder); recorder.Pop(); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else { dc.PushTransform(new MatrixTransform(matrix)); Paint(dc); dc.Pop(); }
                    }, () => true, context) ?? throw new Exception("Gradient capture unavailable"));
                    if (!matrix.TryInvert(out var inverse)) throw new Exception("Test matrix is singular");
                    foreach (var x in new[] { 4, 16, 40, 64, 88, 112, 144 })
                    {
                        var point = matrix.Transform(new Point(x + .5, 24.5));
                        var px = (int)Math.Floor(point.X * dpi); var py = (int)Math.Floor(point.Y * dpi);
                        var local = inverse.Transform(new Point((px + .5) / dpi, (py + .5) / dpi));
                        var t = row.Radial ? Math.Sqrt(local.X * local.X + (local.Y - 24) * (local.Y - 24)) / 128 : local.X / 128;
                        t = Math.Clamp(t, 0, 1);
                        var first = row.First; var last = row.Last;
                        double ColorChannel(byte a, byte b, float sa, float sb) => Math.Clamp(row.LinearLight ? EncodeGradientReference(sa + (sb - sa) * t) : (a + (b - a) * t) / 255d, 0, 1);
                        var alpha = Math.Clamp((first.ScA + (last.ScA - first.ScA) * t) * row.Opacity, 0, 1);
                        var expected = route == "mask" ? new[] { 66 / 255d, 99 / 255d, 20 / 255d, alpha }
                            : new[] { ColorChannel(first.B, last.B, first.ScB, last.ScB), ColorChannel(first.G, last.G, first.ScG, last.ScG), ColorChannel(first.R, last.R, first.ScR, last.ScR), alpha };
                        var at = (py * 208 * dpi + px) * 4;
                        for (var c = 0; c < 4; c++)
                        {
                            var actual = c == 3 ? pixels[at + 3] : pixels[at + c] * pixels[at + 3] / 255d;
                            var wanted = expected[c] * 255 * (c == 3 ? 1 : alpha);
                            var delta = (int)Math.Ceiling(Math.Abs(actual - wanted)); largest = Math.Max(largest, delta); samples++;
                            if (delta > 3) throw new Exception($"Analytic color at ({px},{py}) channel{c}: actual{actual:F3}, expected{wanted:F3}, delta{delta}");
                        }
                    }
                    Save(pixels, 208 * dpi, 100 * dpi, label + ".png");
                }
                catch (Exception error) { failures++; errors.Add(error.ToString()); }
                records.Add(new { label, largest, samples, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var rows = GradientInterpolationCases(); var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(rows.Length / (double)columns) * 112;
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) }; var sources = new List<SvgImage>();
                for (var i = 0; i < rows.Length; i++)
                {
                    var row = rows[i]; var x = 24 + i % columns * 360; var y = 16 + i / columns * 112;
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    ImageSource source;
                    if (row.Extended) source = new DrawingImage(new GeometryDrawing(row.Brush(), null, new RectangleGeometry(new(0, 0, 160, 48))));
                    else { var image = SvgImage.FromSvgString(row.Svg()); sources.Add(image); source = image; }
                    var preview = new Image { Source = source, Width = 160, Height = 48 }; Jalium.UI.Controls.Canvas.SetLeft(preview, x); Jalium.UI.Controls.Canvas.SetTop(preview, y + 30); canvas.Children.Add(preview);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context)!), viewport, height, $"interpolation-gallery-{backend}-{viewport}.png");
                foreach (var image in sources) image.Dispose();
            }
            File.WriteAllText(Path.Combine(Output, "interpolation.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, reference = "independent analytic color, premultiplied RGB and alpha", tolerance = 3, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false, liveWindow = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }

    private static double EncodeGradientReference(double value) => value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
}
