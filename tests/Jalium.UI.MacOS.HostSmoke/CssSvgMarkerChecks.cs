using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SvgMarkerCase(string Name, string Caption, string Source, string Reference);
    private static class SvgMarkerCatalog
    {
        private const string Arrow = "<path d='M0,-4 L8,0 L0,4 Z' fill='red'/>";
        private const string Marker = "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='8' markerHeight='8' overflow='visible' orient='auto'>" + Arrow + "</marker></defs>";
        private static string At(double x, double y, double angle = 0) => FormattableString.Invariant($"<g transform='translate({x},{y}) rotate({angle})'>{Arrow}</g>");
        internal static SvgMarkerCase[] Cases()
        {
        var cases = new (string Name, string Source, string Reference)[]
        {
            ("end", Marker + "<line x1='20' y1='30' x2='80' y2='30' marker-end='url(#m)'/>", At(80, 30)),
            ("start-reverse", Marker.Replace("orient='auto'", "orient='auto-start-reverse'") + "<line x1='20' y1='30' x2='80' y2='30' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30, 180) + At(80, 30)),
            ("middle", Marker + "<polyline fill='none' points='20,30 80,30 80,70' marker-mid='url(#m)'/>", At(80, 30, 45)),
            ("cubic", Marker + "<path fill='none' d='M20,30 C20,50 60,50 80,30' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30, 90) + At(80, 30, -45)),
            ("quadratic", Marker + "<path fill='none' d='M20,30 Q20,70 80,30' marker-end='url(#m)'/>", At(80, 30, -33.690067525979785)),
            ("arc", Marker + "<path fill='none' d='M20,30 A20,20 0 0 1 40,50' marker-start='url(#m)' marker-end='url(#m)'/>", At(20, 30) + At(40, 50, 90)),
            ("subpaths", Marker + "<path fill='none' d='M20,30 H50 M80,50 H110' marker='url(#m)'/>", At(20, 30) + At(50, 30) + At(80, 50) + At(110, 50)),
            ("stylesheet", Marker + "<style>@supports (marker-end:url(#m)){g{--tip:url(#m);marker-end:var(--tip)}} .off{marker-end:none}</style><g><line x1='20' y1='30' x2='80' y2='30'/><line class='off' x1='20' y1='60' x2='80' y2='60'/></g>", At(80, 30)),
            ("stroke-units", Marker.Replace("markerUnits='userSpaceOnUse'", "markerUnits='strokeWidth'") + "<path fill='none' stroke-width='2' d='M20,30 H80' marker-end='url(#m)'/>", "<g transform='translate(80,30) scale(2)'>" + Arrow + "</g>"),
            ("context-stroke", Marker.Replace("fill='red'", "fill='context-stroke'") + "<path fill='none' stroke='blue' stroke-width='0' d='M20,30 H80' marker-end='url(#m)'/>", At(80, 30).Replace("fill='red'", "fill='blue'")),
            ("definition-inheritance", Marker.Replace(" fill='red'", "") + "<g fill='blue'><path fill='none' d='M20,30 H80' marker-end='url(#m)'/></g>", At(80, 30).Replace("fill='red'", "fill='black'")),
            ("opacity", Marker + "<g opacity='.5'><path fill='none' opacity='.5' d='M20,30 H80' marker-end='url(#m)'/></g>", "<g opacity='.25'>" + At(80, 30) + "</g>"),
        };
        var all = cases.ToList();
        all.Add(("viewport", "<defs><marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10' refX='5' refY='5'><rect x='-4' y='-4' width='18' height='18' fill='red'/></marker></defs><path fill='none' d='M20,30H80' marker-end='url(#m)'/>", "<defs><clipPath id='reference-viewport'><rect width='10' height='10'/></clipPath></defs><g transform='translate(75,25)' clip-path='url(#reference-viewport)'><rect x='-4' y='-4' width='18' height='18' fill='red'/></g>"));
        all.Add(("paint-order", Marker.Replace("orient='auto'", "orient='0'") + "<rect x='40' y='30' width='40' height='30' fill='blue' marker-start='url(#m)' paint-order='markers fill'/>", At(40,30) + "<rect x='40' y='30' width='40' height='30' fill='blue'/>"));
        foreach(var radial in new[]{false,true})
        {
            var gradient=radial?"<radialGradient id='g' gradientUnits='userSpaceOnUse' cx='60' cy='40' r='40'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></radialGradient>":"<linearGradient id='g'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
            var reference=radial?gradient:"<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='40' y1='30' x2='80' y2='30'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
            all.Add((radial?"context-radial":"context-linear", "<defs>"+gradient+"<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10'><rect width='20' height='20' fill='context-fill'/></marker></defs><rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/>","<defs>"+reference+"<clipPath id='reference-viewport'><rect x='30' y='20' width='20' height='20'/></clipPath></defs><rect x='40' y='30' width='40' height='30' fill='url(#g)'/><g clip-path='url(#reference-viewport)'><rect x='30' y='20' width='20' height='20' fill='url(#g)'/></g>"));
        }
        all.Add(("circle",Marker+"<circle cx='60' cy='40' r='20' fill='none' marker-end='url(#m)'/>",At(80,40,90)));
        all.Add(("zero",Marker+"<path fill='none' d='M80,30L80,30' marker-end='url(#m)'/>",At(80,30)));
        all.Add(("cycle","<marker id='m' markerUnits='userSpaceOnUse' markerWidth='10' markerHeight='10'><rect width='10' height='10' fill='red' marker-start='url(#m)'/></marker><path fill='none' d='M20,30H80' marker-end='url(#m)'/>","<defs><clipPath id='reference-viewport'><rect width='10' height='10'/></clipPath></defs><g transform='translate(80,30)' clip-path='url(#reference-viewport)'><rect width='10' height='10' fill='red'/></g>"));
        all.Add(("use",Marker+"<defs><path id='p' fill='none' d='M20,30H80'/></defs><use href='#p' x='10' y='10' marker-end='url(#m)'/>",At(90,40)));
        const string strokeGradient="<linearGradient id='g' gradientUnits='userSpaceOnUse' x1='40' y1='30' x2='80' y2='30'><stop stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>";
        all.Add(("context-stroke-gradient", "<defs>"+strokeGradient+"<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='10' refX='10' refY='5' overflow='visible'><line x1='0' y1='5' x2='20' y2='5' stroke='context-fill' stroke-width='6'/></marker></defs><rect x='40' y='30' width='40' height='30' fill='url(#g)' marker-start='url(#m)'/>", "<defs>"+strokeGradient+"</defs><rect x='40' y='30' width='40' height='30' fill='url(#g)'/><path d='M30,30H50' fill='none' stroke='url(#g)' stroke-width='6'/>"));
        all.Add(("marker-ellipse", "<marker id='m' markerUnits='userSpaceOnUse' markerWidth='20' markerHeight='20' refX='10' refY='10' orient='90' overflow='visible'><ellipse cx='10' cy='10' rx='8' ry='4' fill='red'/></marker><path fill='none' d='M20,30H80' marker-end='url(#m)'/>", "<ellipse cx='80' cy='30' rx='4' ry='8' fill='red'/>"));
        string[] captions=["路径末端沿切线定向","首端自动反向，末端正向","折线转角使用夹角平分线","三次曲线保留端点切线","二次曲线按实际方向定位","椭圆弧使用解析切线","多子路径保留中间顶点","CSS 条件、变量与继承","标记大小跟随描边宽度","上下文描边保留来源颜色","定义样式独立于引用路径","元素与父组透明度各应用一次","默认视口裁剪隐藏溢出","标记参与完整绘制顺序","上下文线性渐变连续","上下文径向渐变连续","圆形使用等效路径顶点","零长度线段保留标记","循环引用保留非循环绘制","use 实例继承标记属性","上下文渐变用于标记描边","旋转椭圆保留实际轮廓"];
        return all.Select((row,index)=>new SvgMarkerCase(row.Name,captions[index],row.Source,row.Reference)).ToArray();
        }
    }

    private static string SvgMarkerXml(string body, bool mask = false) => "<svg xmlns='http://www.w3.org/2000/svg' width='160' height='90'>" +
        (mask ? "<defs><mask id='acceptance-mask' maskUnits='userSpaceOnUse' x='0' y='0' width='160' height='90' mask-type='alpha'>" + body + "</mask></defs><rect width='160' height='90' fill='#146342' mask='url(#acceptance-mask)'/>" : body) + "</svg>";
    internal static int RunSvgMarkers()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        File.WriteAllText(Path.Combine(Output, "markers-cases.json"), JsonSerializer.Serialize(SvgMarkerCatalog.Cases()));
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var dpi in new[] { 1, 2 }) foreach (var recorded in new[] { false, true }) foreach (var affine in new[] { false, true })
            foreach (var route in new[] { "drawing", "image", "brush", "mask" }) foreach (var row in SvgMarkerCatalog.Cases())
            {
                context.DefaultRenderingEngine = engine;
                var label = $"markers-{backend}-{engine}-{dpi}-{recorded}-{affine}-{route}-{row.Name}";
                var errors = new List<string>(); var largest = 0; var differing = 0; var straightLargest = 0; var alphaLargest = 0;
                try
                {
                    using var actualImage = SvgImage.FromSvgString(SvgMarkerXml(row.Source));
                    using var expectedImage = SvgImage.FromSvgString(SvgMarkerXml(row.Reference));
                    using var actualMask = SvgImage.FromSvgString(SvgMarkerXml(row.Source, true));
                    using var expectedMask = SvgImage.FromSvgString(SvgMarkerXml(row.Reference, true));
                    var matrix = affine ? new Matrix(.86, .12, -.08, .92, 24, 16) : new Matrix(1, 0, 0, 1, 16, 12);
                    const int width = 208, height = 128;
                    byte[] Paint(bool source) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, height * dpi, dpi, dc =>
                    {
                        void Draw(DrawingContext target)
                        {
                            if (route == "drawing") (source ? actualImage : expectedImage).Drawing!.RenderTo(target);
                            else if (route == "image") target.DrawImage(source ? actualImage : expectedImage, new Rect(0, 0, 160, 90));
                            else if (route == "brush") target.DrawRectangle(new ImageBrush(source ? actualImage : expectedImage) { Stretch = Stretch.Fill }, null, new Rect(0, 0, 160, 90));
                            else target.DrawImage(source ? actualMask : expectedMask, new Rect(0, 0, 160, 90));
                        }
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); recorder.PushTransform(new MatrixTransform(matrix)); Draw(recorder); recorder.Pop(); DrawingReplayer.Replay(recorder.Commit(), dc); }
                        else { dc.PushTransform(new MatrixTransform(matrix)); Draw(dc); dc.Pop(); }
                    }, () => true, context) ?? throw new Exception("SVG marker capture unavailable"));
                    var left = Paint(true); var right = Paint(false);
                    var hasInk = Enumerable.Range(0, right.Length / 4).Any(i => right[i * 4 + 3] > 0);
                    if (!hasInk) throw new Exception("Reference has unexpected empty/nonempty coverage");
                    for (var i = 0; i < left.Length; i += 4)
                    {
                        var alpha = Math.Abs(left[i + 3] - right[i + 3]); alphaLargest = Math.Max(alphaLargest, alpha);
                        largest = Math.Max(largest, alpha); if (alpha > 3) differing++;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            straightLargest = Math.Max(straightLargest, Math.Abs(left[i + channel] - right[i + channel]));
                            // Capture exposes straight RGB. Compare compositing
                            // contributions so alpha0/1 does not turn RGB into a
                            // fictitious 255-level visible difference.
                            var l = (left[i + channel] * left[i + 3] + 127) / 255;
                            var r = (right[i + channel] * right[i + 3] + 127) / 255;
                            var delta = Math.Abs(l - r); largest = Math.Max(largest, delta); if (delta > 3) differing++;
                        }
                    }
                    Save(left, width * dpi, height * dpi, label + ".png"); Save(right, width * dpi, height * dpi, label + "-expected.png");
                    if (differing > 0) throw new Exception($"Explicit-graphics reference: {differing} channels exceed tolerance3, maximum{largest}");
                }
                catch (Exception error) { failures++; errors.Add(error.ToString()); }
                records.Add(new { label, largest, differing, straightLargest, alphaLargest, errors }); Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            foreach (var viewport in new[] { 360, 720 })
            {
                var gallerySources = new List<SvgImage>();
                var rows = SvgMarkerCatalog.Cases(); var columns = viewport < 600 ? 1 : 2; var height = 24 + (int)Math.Ceiling(rows.Length / (double)columns) * 112;
                var canvas = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var index = 0; index < rows.Length; index++)
                {
                    var row = rows[index]; var x = 24 + index % columns * 360; var y = 16 + index / columns * 112;
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), Width = 312, Height = 22 };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); canvas.Children.Add(caption);
                    var source = SvgImage.FromSvgString(SvgMarkerXml(row.Source)); gallerySources.Add(source);
                    var image = new Image { Source = source, Width = 160, Height = 90 };
                    Jalium.UI.Controls.Canvas.SetLeft(image, x); Jalium.UI.Controls.Canvas.SetTop(image, y + 22); canvas.Children.Add(image);
                }
                canvas.Measure(new(viewport, height)); canvas.Arrange(new(0, 0, viewport, height)); canvas.UpdateLayout();
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => canvas.Render(dc), () => true, context)!), viewport, height, $"markers-gallery-{backend}-{viewport}.png");
                foreach (var source in gallerySources) source.Dispose();
            }
            File.WriteAllText(Path.Combine(Output, "markers.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, comparison = "premultiplied BGR and alpha", nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false, liveWindow = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
