using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record FirstLineCase(string Caption, string Source, string Style,
        string? Literal = null, string InlineStyle = "", int Split = 0,
        bool Letter = false, bool Legacy = false, bool Ancestor = false, bool Soft = false, bool GroupSplit = false,
        int Columns = 0, int Clamp = 0)
    { internal int Length => Source.IndexOf('\n') + 1; }
    private const string FirstLineSource = "Alpha e\u0301cole ßeta 中文\nGamma delta remains ordinary.";
    private static FirstLineCase[] FirstLineCases() =>
    [
        new("首行字号 · 后续行恢复", FirstLineSource, "font-size:28px;color:#146342;"),
        new("相对字号 · em 继承", FirstLineSource, "font-size:2em;color:#146342;", "font-size:32px;color:#146342;"),
        new("字体与阴影 · 真实字形", FirstLineSource, "font-size:28px;font-weight:700;font-style:italic;text-shadow:1px 1px 1px #9bc5ac;color:#146342;"),
        new("字距与词距 · 首行排版", FirstLineSource, "font-size:28px;letter-spacing:1.5px;word-spacing:2px;color:#146342;"),
        new("行高缩小 · 字形越界", FirstLineSource, "font-size:20px;line-height:.6;color:#146342;"),
        new("大小写展开 · 原始索引", FirstLineSource, "font-size:28px;text-transform:uppercase;color:#146342;"),
        new("波浪装饰 · currentColor", FirstLineSource, "font-size:28px;text-decoration:underline wavy;text-underline-offset:3px;color:#146342;"),
        new("跨 Run 渐变 · 连续背景", FirstLineSource, "font-size:28px;background:linear-gradient(90deg,#bee8d1,#e9efb0);color:#146342;", Split:6),
        new("行内背景 · 绘制顺序", "Alpha beta gamma\nTail remains ordinary.", "font-size:28px;background:#e9efb0;color:#146342;", InlineStyle:"background:#bee8d1;"),
        new("透明度 · 交叠字形", "WW\nTail remains ordinary.", "font-size:48px;font-style:italic;letter-spacing:-25px;opacity:.5;color:#146342;", Split:1,GroupSplit:true),
        new("首字母 · 首行继承", FirstLineSource, "font-size:24px;color:#146342;", Letter:true),
        new("行内 em · 保留来源树", "Alpha beta\nTail remains ordinary.", "font-size:24px;color:#146342;", InlineStyle:"font-size:2em;color:#234d90;"),
        new("祖先与段落 · 首行嵌套", FirstLineSource, "font-size:48px;color:#146342;", Ancestor:true),
        new("兼容语法 · 单冒号", FirstLineSource, "font-size:28px;color:#146342;", Legacy:true),
        new("实际换行 · 宽度改变", "AAAA BBBB CCCC DDDD EEEE FFFF\nTail remains ordinary.", "font-size:32px;color:#146342;", Soft:true),
        new("多列排版 · 首行只出现一次", "Alpha\nbeta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau. Alpha beta gamma delta epsilon zeta eta theta.", "font-size:28px;color:#146342;", Columns:2),
        new("两行截断 · 保留原始全文", "Alpha\nbeta gamma delta epsilon zeta eta theta iota kappa lambda mu nu xi omicron pi rho sigma tau.", "font-size:28px;color:#146342;", Clamp:2)
    ];

    private static (StackPanel Root, TextBlock Body, Run Original) FirstLineRoot(FirstLineCase row, int width, bool reference, int dpi, bool selected = false)
    {
        var root = new StackPanel { Width=width, Height=220, Background=new SolidColorBrush(Color.FromRgb(0xf1,0xf5,0xf1)) };
        Css.SetClass(root,"first-line-root");
        var body = new TextBlock { IsTextSelectionEnabled=true, FontFamily=new FontFamily("monospace"),
            SelectionBrush=new SolidColorBrush(Color.FromArgb(180,0x1e,0x79,0x3f)) };
        Css.SetClass(body,"first-line-body");
        var run = new Run(row.Split>0 ? row.Source[..row.Split] : row.Source);
        if(reference)
        {
            var end = FirstLineLiteralBoundary(row,width,dpi);
            var inlineReference=row.Columns>0 || row.Clamp>0;
            var prefixBlock = new Span(); var prefix = new Span(); var tail = new Span();
            body.Inlines.Add(prefixBlock); body.Inlines.Add(tail); prefixBlock.Inlines.Add(prefix);
            var metricStyle = string.Join(";",(row.Literal??row.Style).Split(';').Where(declaration=>
                declaration.TrimStart().StartsWith("font-",StringComparison.Ordinal) || declaration.TrimStart().StartsWith("line-height:",StringComparison.Ordinal)));
            Css.SetStyle(prefixBlock,(inlineReference?"display:inline;":"display:block;")+"font-size:16px;line-height:1;color:#233c32;"+metricStyle+";");
            Css.SetStyle(prefix,"display:inline;font-size:16px;line-height:1;color:#233c32;"+(row.Literal??row.Style)+"white-space:"+(inlineReference?"pre-line":"normal")+";");
            Css.SetStyle(tail,(inlineReference?"display:inline;":"display:block;")+"font-size:16px;line-height:1;color:#233c32;");
            if(row.Letter)
            {
                var letter=new Run(row.Source[..1]); prefix.Inlines.Add(letter);
                Css.SetStyle(letter,"display:inline;font-size:2em;color:#234d90;");
                prefix.Inlines.Add(new Run(row.Source[1..end]));
            }
            else if(row.GroupSplit)
            {
                prefix.Inlines.Add(new Run(row.Source[..row.Split]));
                prefix.Inlines.Add(new Run(row.Source[row.Split..end]));
            }
            else
            {
                var first=new Run(row.Source[..end]); prefix.Inlines.Add(first);
                if(row.InlineStyle.Length>0) Css.SetStyle(first,"display:inline;"+row.InlineStyle);
            }
            var ordinary=new Run(row.Source[end..]); tail.Inlines.Add(ordinary);
            if(row.InlineStyle.Length>0) Css.SetStyle(ordinary,"display:inline;"+row.InlineStyle);
        }
        else
        {
            body.Inlines.Add(run); if(row.InlineStyle.Length>0) Css.SetStyle(run,"display:inline;"+row.InlineStyle);
            if(row.Split>0) body.Inlines.Add(new Run(row.Source[row.Split..]));
        }
        root.Children.Add(body);
        var rules=".first-line-root {display:block;} .first-line-body {display:block;font-size:16px;line-height:1;white-space:pre-line;color:#233c32;"+
            (row.Columns>0?"column-count:"+row.Columns+";column-gap:20px;column-fill:auto;height:120px;":"")+
            (row.Clamp>0?"line-clamp:"+row.Clamp+";":"")+"}";
        if(!reference)
        {
            rules += row.Ancestor
                ? ".first-line-root::first-line {font-size:2em;color:#146342;} .first-line-body::first-line {font-size:1.5em;}"
                : ".first-line-body"+(row.Legacy?":first-line":"::first-line")+" {"+row.Style+"}";
            if(row.Letter) rules += ".first-line-body::first-letter {font-size:2em;color:#234d90;}";
        }
        var sheet=CssStyleSheet.Parse(rules); if(sheet.Diagnostics.Count>0) throw new Exception(string.Join("; ",sheet.Diagnostics));
        Css.GetStyleSheets(root).Add(sheet); VisualTreeHelper.SetRootDpi(root,new(dpi,dpi));
        FirstLineLayout(root,width,220); body.Select(0,selected?FirstLineSelectionLength(row):0);
        return(root,body,run);
    }

    private static int FirstLineSelectionLength(FirstLineCase row)
        => Math.Min(10,row.Length>0?row.Length-1:row.Source.Length);

    private static int FirstLineLiteralBoundary(FirstLineCase row,int width,int dpi)
    {
        // An ordinary uniformly styled paragraph independently supplies the
        // first wrapping opportunity. Its smaller tail is then a separate block,
        // retaining all original UTF-16 characters and the forced break position.
        var probe=new TextBlock {FontFamily=new FontFamily("monospace")}; Css.SetClass(probe,"probe");
        if(row.Letter)
        {
            var letter=new Run(row.Source[..1]); probe.Inlines.Add(letter); probe.Inlines.Add(new Run(row.Source[1..]));
            Css.SetStyle(letter,"display:inline;font-size:2em;color:#234d90;");
        }
        else
        {
            var run=new Run(row.Source); probe.Inlines.Add(run);
            if(row.InlineStyle.Length>0) Css.SetStyle(run,"display:inline;"+row.InlineStyle);
        }
        Css.GetStyleSheets(probe).Add(CssStyleSheet.Parse(".probe {display:block;font-size:16px;line-height:1;white-space:pre-line;"+(row.Literal??row.Style)+"}"));
        VisualTreeHelper.SetRootDpi(probe,new(dpi,dpi)); FirstLineLayout(probe,row.Columns>0?(width-20*(row.Columns-1))/row.Columns:width,220);
        var shared=probe.DocumentSharedLine(0,1);
        var end=shared is null ? probe.GetDocumentLines(width).First().End :
            Enumerable.Range(0,row.Source.Length).TakeWhile(index=>ReferenceEquals(probe.DocumentSharedLine(index,index+1),shared)).Count();
        if(end<row.Source.Length && row.Source[end] is '\r' or '\n') end++;
        if(end<=0) throw new Exception("Literal first-line reference produced no source range");
        return end;
    }

    private static void FirstLineLayout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
        }
    }

    internal static int RunFirstLines()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var rows = FirstLineCases(); var records = new List<object>(); var failures = 0;
        var pairs = new Dictionary<(RenderingEngine Engine, int Scene, int Width, int Dpi, bool Selected),
            ((StackPanel Root, TextBlock Body, Run Original) Actual, (StackPanel Root, TextBlock Body, Run Original) Reference)>();
        var limited = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_CSS_FIRST_LINE_LIMIT"), out var limit) && limit > 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in rows) foreach (var width in new[] { 312, 672 }) foreach (var recorded in new[] { false, true }) foreach (var dpi in new[] { 1, 2 }) foreach (var selected in new[] {false,true})
                {
                    if (limited && records.Count >= limit) goto Complete;
                    var label = $"first-line-{backend}-{engine}-{Array.IndexOf(rows, row)}-{width}-{recorded}-{dpi}-{selected}";
                    var errors = new List<string>(); var largest = 0; var timing = new Dictionary<string, long>();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var key = (engine, Array.IndexOf(rows, row), width, dpi, selected);
                        if (!pairs.TryGetValue(key, out var pair))
                        {
                            var a = FirstLineRoot(row, width, false, dpi, selected); timing["actualLayoutMs"] = watch.ElapsedMilliseconds; watch.Restart();
                            var b = FirstLineRoot(row, width, true, dpi, selected); timing["referenceLayoutMs"] = watch.ElapsedMilliseconds; watch.Restart();
                            pair = (a, b); pairs[key] = pair;
                        }
                        else timing["reusedForRecording"] = 1;
                        var actual = pair.Actual; var expected = pair.Reference;
                        if (recorded) pairs.Remove(key);
                        for (var index = 0; index < row.Source.Length; index++)
                        {
                            var a = actual.Body.DocumentTextBounds(index, 1); var b = expected.Body.DocumentTextBounds(index, 1);
                            if (a.Count != b.Count) throw new Exception($"Source {index} bounds count {a.Count} != {b.Count}");
                            for (var n = 0; n < a.Count; n++)
                                if (Math.Abs(a[n].X - b[n].X) > .001 || Math.Abs(a[n].Y - b[n].Y) > .001 ||
                                    Math.Abs(a[n].Width - b[n].Width) > .001 || Math.Abs(a[n].Height - b[n].Height) > .001)
                                    throw new Exception($"Source {index} bounds {a[n]} != {b[n]}");
                        }
                        timing["sourceBoundsMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        if (!ReferenceEquals(actual.Original, actual.Body.Inlines[0]) || actual.Original.Text != (row.Split > 0 ? row.Source[..row.Split] : row.Source) ||
                            actual.Body.Text != row.Source || actual.Body.SelectedText != row.Source[..(selected?FirstLineSelectionLength(row):0)])
                            throw new Exception("Source Run, text or selection changed");
                        foreach (var index in new[] { 0, row.Length, Math.Min(row.Source.Length - 1, row.Length + 8) })
                            foreach (var bounds in actual.Body.DocumentTextBounds(index, 1))
                            {
                                var point = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                                var wanted = expected.Body.DocumentCharacterFromPoint(point); var found = actual.Body.DocumentCharacterFromPoint(point);
                                if (wanted != found) throw new Exception($"Point hit {found} != {wanted}");
                                var offset = actual.Body.TransformToAncestor(actual.Root);
                                if (!ReferenceEquals(actual.Root.InputHitTest(new(point.X + offset.X, point.Y + offset.Y)), actual.Body))
                                    throw new Exception("Private pseudo box leaked through input hit testing");
                            }
                        timing["hitMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        byte[] Capture((StackPanel Root,TextBlock Body,Run Original) tree,bool literal)
                        {
                            // These ordinary reference blocks have independent
                            // selection paint order. Compose the font-only
                            // ancestor's logical highlight below all its glyphs
                            // to model one inline context across the soft wrap.
                            var composeSelection=literal && selected && row.Ancestor;
                            var background=tree.Root.Background;
                            Geometry? selection=null; Rect? singleSelection=null;
                            if(composeSelection)
                            {
                                var offset=tree.Body.TransformToAncestor(tree.Root);
                                var length=FirstLineSelectionLength(row);
                                var boundary=FirstLineLiteralBoundary(row,width,dpi);
                                var prefixEnd=Math.Min(boundary,length);
                                // Document bounds retain a caret-sized box for
                                // collapsed trailing whitespace. A selection
                                // highlight ends at the last visible advance.
                                if(prefixEnd==boundary)
                                    while(prefixEnd>0 && char.IsWhiteSpace(row.Source[prefixEnd-1])) prefixEnd--;
                                var rectangles=tree.Body.DocumentTextBounds(0,prefixEnd).Concat(length>boundary
                                    ? tree.Body.DocumentTextBounds(boundary,length-boundary) : []).ToArray();
                                var geometry=new PathGeometry {FillRule=FillRule.Nonzero};
                                foreach(var rect in rectangles)
                                {
                                    var left=Math.Round((rect.Left+offset.X)*dpi)/dpi; var right=Math.Round((rect.Right+offset.X)*dpi)/dpi;
                                    var top=Math.Round((rect.Top+offset.Y)*dpi)/dpi; var bottom=Math.Round((rect.Bottom+offset.Y)*dpi)/dpi;
                                    geometry.Figures.Add(CssRoundedRectangleGeometry.Figure(new(left,top,right-left,bottom-top),default));
                                }
                                geometry.Freeze(); selection=geometry;
                                if(rectangles.Length==1) singleSelection=geometry.Bounds;
                                tree.Body.ClearSelection(); tree.Root.Background=null;
                            }
                            try
                            {
                                return Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,220*dpi,dpi,dc=>
                                {
                                    void Draw(DrawingContext target)
                                    {
                                        if(composeSelection)
                                        {
                                            target.DrawRectangle(background,null,new(0,0,width,220));
                                            if(singleSelection is {} rect) target.DrawRectangle(tree.Body.SelectionBrush,null,rect);
                                            else target.DrawGeometry(tree.Body.SelectionBrush,null,selection!);
                                        }
                                        tree.Root.Render(target);
                                    }
                                    if(recorded) {var recorder=new DrawingRecorder();recorder.BindWholeFrame();Draw(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}
                                    else Draw(dc);
                                },()=>true,context)??throw new Exception("First-line capture unavailable"));
                            }
                            finally {if(composeSelection){tree.Root.Background=background;tree.Body.Select(0,FirstLineSelectionLength(row));}}
                        }
                        var pixels=Capture(actual,false); var reference=Capture(expected,true);
                        int Ink(byte[] bytes) => Enumerable.Range(0,bytes.Length/4).Count(index => bytes[index*4] < 130 || bytes[index*4+1] < 130 || bytes[index*4+2] < 130);
                        if(Ink(pixels) < 8 || Ink(reference) < 8) throw new Exception("Native text capture contains no visible glyph ink");
                        timing["captureMs"] = watch.ElapsedMilliseconds; watch.Restart();
                        for (var index = 0; index < pixels.Length; index++)
                        {
                            var delta = Math.Abs(pixels[index] - reference[index]); largest = Math.Max(largest, delta);
                            if (delta > 3 && errors.Count < 4) errors.Add($"Pixel {index / 4}, channel {index % 4}: {pixels[index]} != {reference[index]}");
                        }
                        if (errors.Count > 0) { Save(pixels, width * dpi, 220 * dpi, label + ".png"); Save(reference, width * dpi, 220 * dpi, label + "-expected.png"); }
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    if (errors.Count > 0) failures++;
                    records.Add(new { label, largest, errors, timing }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {JsonSerializer.Serialize(timing)} {string.Join(" | ", errors)}");
                }
            }
            Complete:
            if (!limited)
            foreach (var viewport in new[] { 360, 720 })
            {
                var columns = viewport < 600 ? 1 : 2; var height = 32 + 8 / columns * 176;
                var gallery = new Canvas { Width = viewport, Height = height, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
                for (var index = 0; index < 8; index++)
                {
                    var x = 24 + index % columns * 360; var y = 16 + index / columns * 176;
                    var row = rows[new[] { 0, 2, 6, 7, 8, 10, 15, 16 }[index]];
                    var caption = new TextBlock { Text = row.Caption, FontSize = 15, Width = 312, Height = 22, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)) };
                    Jalium.UI.Controls.Canvas.SetLeft(caption, x); Jalium.UI.Controls.Canvas.SetTop(caption, y); gallery.Children.Add(caption);
                    var content = FirstLineRoot(row, 312, false, 1).Root;
                    content.Height=134;
                    Jalium.UI.Controls.Canvas.SetLeft(content, x); Jalium.UI.Controls.Canvas.SetTop(content, y + 30); gallery.Children.Add(content);
                }
                FirstLineLayout(gallery, viewport, height);
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(viewport, height, 1, dc => gallery.Render(dc), () => true, context)!), viewport, height,
                    $"first-line-gallery-{backend}-{viewport}.png");
            }
            File.WriteAllText(Path.Combine(Output, "first-line.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, limited,
                nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
