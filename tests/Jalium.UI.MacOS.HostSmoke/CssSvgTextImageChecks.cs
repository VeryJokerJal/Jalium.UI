using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SvgTextImageCase(string Name, string Caption, string Body, Func<Drawing>? Reference = null);

    private static SvgTextImageCase[] SvgTextImageCases() =>
    [
        new("kerning", "字距与连字 · AVfi", "<text x='12' y='60'>AVfi</text>",
            () => new GeometryDrawing(Brushes.Red, null, TextPaintOutline("AVfi", 12, 60))),
        new("chinese", "中文回退字形 · 轮廓", "<text x='12' y='60'>轮廓</text>",
            () => new GeometryDrawing(Brushes.Red, null, TextPaintOutline("轮廓", 12, 60))),
        new("combining", "组合附加符号 · é", "<text x='12' y='60'>e&#x301;</text>",
            () => new GeometryDrawing(Brushes.Red, null, TextPaintOutline("e\u0301", 12, 60))),
        new("arabic", "阿拉伯文字形 · سلام", "<text x='12' y='60'>سلام</text>"),
        new("italic", "斜体真实墨迹边界", "<text x='12' y='60' style='font-style:italic'>f</text>"),
        new("opacity", "整段文字透明度", "<text x='12' y='60' opacity='.5'>AV</text>"),
        new("overlap-fill", "重叠填充不重复加深", "<text x='12' y='60' dx='0 -24' fill-opacity='.5'>HH</text>"),
        new("chunks", "绝对坐标分隔文字块", "<text x='12 24' y='60' fill-opacity='.5'>HH</text>"),
        new("rotate", "字形围绕基线旋转", "<text x='12' y='60' rotate='12'>H</text>",
            () => new GeometryDrawing(Brushes.Red, null, TextPaintOutline("H", 12, 60, angle: 12))),
        new("text-length", "指定长度与字形缩放", "<text x='12' y='60' textLength='92' lengthAdjust='spacingAndGlyphs'>AV</text>"),
        new("span", "子文字独立颜色", "<text x='12' y='60'>A<tspan fill='blue'>V</tspan></text>"),
        new("gradient", "轮廓内的渐变填充", "<defs><linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><text x='12' y='60' fill='url(#g)'>AV</text>"),
        new("linear-user-space", "用户空间线性渐变", "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='100' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><text x='28' y='60' fill='url(#g)'>AV</text>"),
        new("radial-user-space", "用户空间径向渐变", "<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='48' cy='42' r='42'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><text x='28' y='60' fill='url(#g)'>AV</text>"),
        new("linear-user-rotate", "旋转字形中的线性渐变", "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='100' y2='0'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient></defs><text x='28' y='60' rotate='12' fill='url(#g)'>AV</text>"),
        new("radial-user-stretch", "拉伸字形中的径向渐变", "<defs><radialGradient id='g' gradientUnits='userSpaceOnUse' cx='48' cy='42' r='42'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient></defs><text x='28' y='60' textLength='92' lengthAdjust='spacingAndGlyphs' fill='url(#g)'>AV</text>"),
        new("clip", "轮廓经过裁剪", "<defs><clipPath id='c'><rect x='24' width='100' height='90'/></clipPath></defs><text x='12' y='60' clip-path='url(#c)'>AV</text>"),
        new("mask", "轮廓经过亮度遮罩", "<defs><mask id='m'><rect x='24' width='100' height='90' fill='white'/></mask></defs><text x='12' y='60' mask='url(#m)'>AV</text>"),
        new("transform", "SVG 分组缩放与位移", "<g transform='translate(8 3) scale(.9 .85)'><text x='12' y='60'>AV</text></g>"),
        new("blend", "文字与背景混合", "<rect x='8' y='16' width='132' height='62' fill='#80ff80'/><text x='12' y='60' style='mix-blend-mode:multiply'>AV</text>"),
    ];

    internal static int RunSvgTextImages()
    {
        var pool = PushPool();
        var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1);
        Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>();
        var failures = 0;
        string Xml(SvgTextImageCase row) => "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='90' fill='red'><style>text{font:48px Helvetica}</style>" + row.Body + "</svg>";
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var dpi in new[] { 1, 2 })
            foreach (var recorded in new[] { false, true })
            foreach (var affine in new[] { false, true })
            foreach (var route in new[] { "drawing", "image", "brush" })
            foreach (var row in SvgTextImageCases())
            {
                context.DefaultRenderingEngine = engine;
                var label = $"text-image-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";
                var errors = new List<string>();
                var largest = 0;
                var differing = 0;
                try
                {
                    using var image = SvgImage.FromSvgString(Xml(row));
                    using var outlineImage = SvgImage.FromSvgStringWithTextOutlines(Xml(row));
                    var reference = row.Reference?.Invoke() ?? outlineImage.Drawing!;
                    var referenceFrame = new DrawingGroup();
                    referenceFrame.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 160, 90))));
                    referenceFrame.Children.Add(reference);
                    var referenceImage = row.Reference is null ? (ImageSource)outlineImage : new DrawingImage(referenceFrame);
                    var matrix = affine ? new Matrix(.86, .12, -.08, .92, 24, 16) : new Matrix(1, 0, 0, 1, 16, 12);
                    const int width = 208, height = 128;
                    byte[] Paint(bool actual) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, height * dpi, dpi, dc =>
                    {
                        void Draw(DrawingContext target)
                        {
                            if (route == "drawing") (actual ? image.Drawing! : reference).RenderTo(target);
                            else if (route == "image") target.DrawImage(actual ? image : referenceImage, new Rect(0, 0, 160, 90));
                            else target.DrawRectangle(new ImageBrush(actual ? image : referenceImage) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
                        }
                        if (recorded)
                        {
                            var recorder = new DrawingRecorder();
                            recorder.BindWholeFrame(); recorder.PushTransform(new MatrixTransform(matrix));
                            Draw(recorder); recorder.Pop(); DrawingReplayer.Replay(recorder.Commit(), dc);
                        }
                        else
                        {
                            dc.PushTransform(new MatrixTransform(matrix)); Draw(dc); dc.Pop();
                        }
                    }, () => true, context) ?? throw new Exception("SVG text image capture unavailable"));
                    var actual = Paint(true);
                    var expected = Paint(false);
                    if (!Enumerable.Range(0, actual.Length / 4).Any(i => actual[4 * i + 3] > 0)) throw new Exception("Ordinary SVG text has no ink");
                    if (!Enumerable.Range(0, expected.Length / 4).Any(i => expected[4 * i + 3] > 0)) throw new Exception("Reference has no ink");
                    for (var i = 0; i < expected.Length; i++)
                    {
                        var delta = Math.Abs(actual[i] - expected[i]); largest = Math.Max(largest, delta);
                        if (delta > 3) differing++;
                    }
                    Save(actual, width * dpi, height * dpi, label + ".png");
                    Save(expected, width * dpi, height * dpi, label + "-expected.png");
                    if (differing > 0) throw new Exception($"Outline reference: {differing} channels exceed tolerance 3; maximum {largest}");
                }
                catch (Exception error) { failures++; errors.Add(error.ToString()); }
                records.Add(new { label, largest, differing, reference = row.Reference is null ? "explicit SVG outline mode" : "independent native glyph outline", errors });
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var cases = SvgTextImageCases();
                var columns = viewport < 600 ? 1 : 2;
                var height = 24 + (int)Math.Ceiling(cases.Length / (double)columns) * 112;
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var i = 0; i < cases.Length; i++)
                {
                    var row = cases[i]; var x = 24 + i % columns * 360; var y = 16 + i / columns * 112;
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    var preview = new Image { Source = SvgImage.FromSvgString(Xml(row)), Width = 160, Height = 90 };
                    Jalium.UI.Controls.Canvas.SetLeft(preview, x); Jalium.UI.Controls.Canvas.SetTop(preview, y + 22); canvas.Children.Add(preview);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                var capture = MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context) ?? throw new Exception("SVG text gallery capture unavailable");
                Save(Pixels(capture), viewport, height, $"text-image-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output, "text-image.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false, liveWindow = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
