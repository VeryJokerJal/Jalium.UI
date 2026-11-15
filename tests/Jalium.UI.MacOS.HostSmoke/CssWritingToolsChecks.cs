using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

// Real native readback, without an Apple Intelligence session or physical input.
internal static partial class CssWritingToolsChecks
{
    private const string Font = "font-family:'Helvetica Neue';font-size:30px;line-height:42px;color:red;white-space:normal;";
    private const string Frame = "padding:8px;border:2px solid #146342;background:#eef5f0;border-radius:5px;";
    private const string NoFrameInk = "background:transparent;border-color:transparent;box-shadow:none;";
    private static string Output => Environment.GetEnvironmentVariable("JALIUM_CSS_WRITING_TOOLS_OUTPUT") ?? throw new InvalidOperationException("Set JALIUM_CSS_WRITING_TOOLS_OUTPUT");

    internal static int RunAll()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output);
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failures = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var scale in new[] { 1, 2 })
        foreach (var kind in new[] { "block", "span", "grid", "columns", "transform", "hidden", "selection", "clamp", "shadow", "unicode", "native", "rtl", "paint-wrap", "hyphen", "scroll", "empty-clamp-decoration" })
        {
            context.DefaultRenderingEngine = engine;
            if (context.DefaultRenderingEngine != engine) throw new Exception("Requested rendering engine unavailable");
            var errors = new List<string>(); var label = $"{engine}-{scale}-{kind}";
            try
            {
                using var scene = new Scene(kind, 0, scale); scene.Layout(); var source = scene.Source;
                var raw = scene.Document.GetText(); var text = source.Text;
                int first = text.IndexOf(scene.Runs[1].Text, StringComparison.Ordinal), length = scene.Runs[1].Text.Length;
                int second = text.IndexOf(scene.Runs[3].Text, StringComparison.Ordinal), secondLength = scene.Runs[3].Text.Length;
                var matrix = kind == "transform" ? new Matrix(1, .08, .1, 1, 8, 6) : Matrix.Identity;
                byte[] Paint(Scene? painted = null, int? captureScale = null)
                {
                    var dpi = captureScale ?? scale;
                    return Pixels(MacOSWritingToolsBitmapCapture.Capture(380 * dpi, 240 * dpi, dpi, dc =>
                        { dc.PushTransform(new MatrixTransform(matrix)); (painted ?? scene).Editor.Render(dc); dc.Pop(); }, () => true) ?? throw new Exception("Native readback failed"));
                }
                byte[] Oracle(int mode, int? dpi = null) { using var expected = new Scene(kind, mode, dpi ?? scale); expected.Layout(); return Paint(expected, dpi); }
                var normal = Paint();
                MacOSWritingToolsPresentation.Prepare(scene.Editor, source, "first", first, length, 0, () => true);
                var hidden = Paint(); MacOSWritingToolsPresentation.Finish(scene.Editor, "first");
                Check(Paint().SequenceEqual(normal), "finish did not restore exact pixels");
                var expectedHidden = Oracle(1);
                Check(hidden.SequenceEqual(expectedHidden), "hiding changed background, selection, neighboring glyphs or retained selected ink");
                var preview = MacOSWritingToolsPresentation.CreatePreview(scene.Editor, source, first, length, matrix, scale, null, () => true);
                var selected = preview is null ? [] : Canvas(preview, 380 * scale, 240 * scale, scale);
                var expectedSelected = Oracle(2);
                Check(preview is not null && selected.SequenceEqual(expectedSelected), "preview did not match the independently painted selected text");
                Check(Paint().SequenceEqual(normal), "preview or paint overrides leaked into the next frame");
                MacOSWritingToolsPresentation.Prepare(scene.Editor, source, "first", first, length, 0, () => true);
                MacOSWritingToolsPresentation.Prepare(scene.Editor, source, "second", second, secondLength, 0, () => true);
                var both = Paint(); MacOSWritingToolsPresentation.Finish(scene.Editor, "first");
                var onlySecond = Paint(); MacOSWritingToolsPresentation.Clear(scene.Editor);
                var expectedBoth = Oracle(3); var expectedSecond = Oracle(4);
                Check(both.SequenceEqual(expectedBoth) && onlySecond.SequenceEqual(expectedSecond), "independent animation ranges interfered");
                bool current = true;
                MacOSWritingToolsPresentation.Prepare(scene.Editor, source, "stale", first, length, 0, () => current); _ = Paint(); current = false;
                scene.Editor.InvalidateVisual();
                Check(Paint().SequenceEqual(normal), "a stale session kept hiding glyphs"); MacOSWritingToolsPresentation.Clear(scene.Editor);
                MacOSWritingToolsPresentation.SetState(scene.Editor, source, 1,
                    JsonSerializer.SerializeToElement(new { ranges = new[] { new[] { first, length } } }), () => true);
                var inactive = Paint();
                Check(Gray(inactive) > Gray(normal), "inactive text did not paint its grey preview");
                int? inactiveAlphaDifferences = null, refreshedDpiAlphaDifferences = null;
                if (kind == "block")
                {
                    inactiveAlphaDifferences = InactiveAlphaDifference(scene.Editor, preview!);
                    Check(inactiveAlphaDifferences == 0, "inactive preview did not retain native glyph resolution");
                    int changedDpi = scale == 1 ? 2 : 1;
                    scene.Editor.SetRootDpi(new DpiScale(changedDpi, changedDpi)); scene.Layout(); scene.Editor.InvalidateVisual();
                    _ = Paint(captureScale: changedDpi);
                    var fresh = MacOSWritingToolsPresentation.CreatePreview(scene.Editor, source, first, length, Matrix.Identity, changedDpi, null, () => true) ?? throw new Exception("DPI preview failed");
                    refreshedDpiAlphaDifferences = InactiveAlphaDifference(scene.Editor, fresh);
                    Check(refreshedDpiAlphaDifferences == 0, "DPI change reused an older inactive bitmap");
                    scene.Editor.SetRootDpi(new DpiScale(scale, scale)); scene.Layout(); scene.Editor.InvalidateVisual();
                }
                MacOSWritingToolsPresentation.Clear(scene.Editor); Check(Paint().SequenceEqual(normal), "inactive state did not restore exact pixels");
                Check(raw == scene.Document.GetText() && text == source.Text, "presentation edited source text"); scene.OriginalNodes();
                Save(normal, 380 * scale, 240 * scale, label + "-normal.png"); Save(hidden, 380 * scale, 240 * scale, label + "-hidden.png");
                Save(inactive, 380 * scale, 240 * scale, label + "-inactive.png");
                Save(expectedHidden, 380 * scale, 240 * scale, label + "-expected-hidden.png"); Save(expectedSelected, 380 * scale, 240 * scale, label + "-expected-preview.png");
                if (preview is not null) File.WriteAllBytes(Path.Combine(Output, label + "-preview.png"), preview.Png);
                records.Add(new { label, errors, first, length, second, secondLength, frame = preview?.Frame, inactiveAlphaDifferences, refreshedDpiAlphaDifferences, hiddenDifferentBytes = hidden.Zip(expectedHidden).Count(pair => pair.First != pair.Second), previewDifferentBytes = selected.Length == expectedSelected.Length ? selected.Zip(expectedSelected).Count(pair => pair.First != pair.Second) : -1 });
            }
            catch (Exception exception) { errors.Add(exception.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) { ++failures; Console.WriteLine("FAIL " + label + " " + string.Join("; ", errors)); }
            else Console.WriteLine("PASS " + label);
            void Check(bool value, string error) { if (!value) errors.Add(error); }
        }
        File.WriteAllText(Path.Combine(Output, "checks.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, physicalInput = false, appleIntelligence = false, nativeLibraries = NativeLibraries() }));
        PopPool(pool); return failures == 0 ? 0 : 1;
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Run[] Runs = [new("AA "), new("fff"), new("      "), new("Miii")];
        internal readonly Span Span;
        internal readonly Paragraph Paragraph;
        internal readonly Section Section;
        internal readonly FlowDocument Document;
        internal readonly RichTextBox Editor;
        private readonly DependencyObject _target;
        private readonly string _targetStyle, _rootStyle;
        private readonly bool _native, _scroll;
        private readonly Dictionary<TextElement, FrameworkContentElement?> _parents = [];
        internal IAutomationTextProviderSource Source => ((AutomationTextProvider)Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
        internal Scene(string kind, int paint = 0, double dpi = 1)
        {
            _native = kind == "native"; _scroll = kind == "scroll";
            Span = new(Runs[1]); Span.Inlines.Add(Runs[2]); Paragraph = new(Runs[0]); Paragraph.Inlines.Add(Span); Paragraph.Inlines.Add(Runs[3]);
            Section = new(Paragraph); Section.Blocks.Add(new Paragraph(new Run("正文 / CSS 编辑预览")));
            Document = new(Section) { PagePadding = new(0), FontFamily = "Helvetica Neue", FontSize = 30, LineHeight = 42 };
            _target = kind == "span" ? Span : kind == "grid" ? Paragraph : Section;
            _rootStyle = _native ? "font-family:'Helvetica Neue';font-size:30px;color:red;" : Font + "display:block;" + Frame + (kind == "columns" ? "columns:2;column-gap:14px;height:126px;column-fill:auto;" : kind == "clamp" ? "width:220px;line-clamp:2;" : kind == "paint-wrap" ? "width:120px;overflow-wrap:anywhere;" : kind == "hyphen" ? "width:110px;hyphens:manual;" : kind == "rtl" ? "direction:rtl;" : "");
            _targetStyle = _native ? "" : kind == "span" ? "display:inline;box-decoration-break:clone;" + Frame : kind == "grid" ? "display:grid;grid-template-columns:40px 110px 70px;gap:6px;" : "display:block;";
            if (!_native) Css.SetStyleSheet(Document, "Paragraph,Section{margin:0;padding:0;border:0;}");
            Css.SetStyle(Runs[1], "font-style:italic;" + (kind == "shadow" ? "text-shadow:2px 2px 1px #165f3f;text-decoration:underline;" : kind == "unicode" ? "text-transform:uppercase;letter-spacing:1px;" : ""));
            if (kind == "hidden") { Runs[0].Text = "👨‍👩‍👧‍👦前置隐藏"; Css.SetStyle(Runs[0], "display:none;"); }
            if (kind == "unicode") Runs[1].Text = "ße\u0301🙂";
            if (kind == "paint-wrap") { Runs[1].Text = "ßßßßßßßß"; Css.SetStyle(Runs[1], "text-transform:uppercase;letter-spacing:1px;"); }
            if (kind == "hyphen") { Runs[1].Text = "ab\u00adcd"; Css.SetStyle(Runs[1], "hyphens:manual;hyphenate-character:'↩';"); }
            if (kind == "empty-clamp-decoration") Css.SetStyle(Section.Blocks[1], "width:1px;line-clamp:1;text-decoration:underline;");
            if (_scroll) Section.Blocks.Insert(0, new Paragraph(new Run(string.Concat(Enumerable.Repeat("上文\n", 12)))));
            Editor = new(Document) { Template = null, Padding = new(3), BorderThickness = new(0), Background = Brushes.White, IsReadOnly = true,
                SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 165, 70, 190)) };
            Editor.SetRootDpi(new DpiScale(dpi, dpi));
            RestorePaint();
            if (paint is 1 or 3) Runs[1].Foreground = Brushes.Transparent;
            if (paint is 3 or 4) Runs[3].Foreground = Brushes.Transparent;
            if (paint == 2) InkOnly(1);
            foreach (var node in Runs.Cast<TextElement>().Append(Span).Append(Paragraph).Append(Section)) _parents[node] = node.Parent;
            if (kind == "selection") { Layout(); Source.Select(Source.Text.IndexOf("fff", StringComparison.Ordinal), 3); }
        }
        internal void Layout()
        {
            for (int index = 0; index < 3; index++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); Editor.Measure(new(350, 220)); Editor.Arrange(new(0, 0, 350, 220)); }
            if (_scroll) { Source.ScrollIntoView(Source.Text.IndexOf(Runs[1].Text, StringComparison.Ordinal), Runs[1].Text.Length); Editor.Arrange(new(0, 0, 350, 220)); }
            if (_native && typeof(RichTextBox).GetField("_cssDocumentView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(Editor) is not null) throw new Exception("Native compatibility fixture used the CSS view");
        }
        internal void InkOnly(int selected)
        {
            Editor.SelectionBrush = Brushes.Transparent;
            Document.Foreground = Brushes.Transparent;
            Runs[selected].Foreground = Brushes.Red;
            Editor.Background = Brushes.Transparent;
            if (!_native) { Css.SetStyle(Document, _rootStyle + NoFrameInk); Css.SetStyle(_target, _targetStyle + NoFrameInk); }
            foreach (var run in Runs.Where((_, index) => index != selected)) run.Foreground = Brushes.Transparent;
            ((Paragraph)Section.Blocks[1]).Foreground = Brushes.Transparent;
        }
        internal void RestorePaint()
        {
            Editor.SelectionBrush = new SolidColorBrush(Color.FromArgb(120, 165, 70, 190));
            Editor.Background = Brushes.White; Css.SetStyle(Document, _rootStyle); Css.SetStyle(_target, _targetStyle);
            foreach (var run in Runs) run.ClearValue(TextElement.ForegroundProperty);
            ((Paragraph)Section.Blocks[1]).ClearValue(TextElement.ForegroundProperty);
        }
        internal void OriginalNodes()
        { if (!ReferenceEquals(Editor.Document, Document) || _parents.Any(pair => !ReferenceEquals(pair.Key.Parent, pair.Value))) throw new Exception("Source identity changed"); }
        public void Dispose() => MacOSWritingToolsPresentation.Clear(Editor);
    }

    private static byte[] Pixels(BitmapSource bitmap) { var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels; }
    private static int Gray(byte[] pixels) { int count = 0; for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + 3] > 40 && Math.Abs(pixels[i] - 128) < 3 && Math.Abs(pixels[i + 1] - 128) < 3 && Math.Abs(pixels[i + 2] - 128) < 3) count++; return count; }
    private static int InactiveAlphaDifference(RichTextBox editor, MacOSWritingToolsPresentation.Preview expected)
    {
        var capture = new InactiveCapture(); MacOSWritingToolsPresentation.DrawInactive(editor, capture);
        var actual = capture.Bitmap;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(expected.Png); bitmap.EndInit();
        if (actual is null || capture.Frame != expected.Frame || actual.PixelWidth != bitmap.PixelWidth || actual.PixelHeight != bitmap.PixelHeight) return -1;
        var left = Pixels(actual); var right = Pixels(bitmap); int count = 0;
        for (int index = 3; index < left.Length; index += 4) if (left[index] != right[index]) count++;
        return count;
    }
    private sealed class InactiveCapture : DrawingContextAdapter
    {
        internal BitmapSource? Bitmap;
        internal Rect Frame;
        public override void DrawImage(ImageSource image, Rect frame) { Bitmap = image as BitmapSource; Frame = frame; }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radii) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
    private static Dictionary<string, object> NativeLibraries()
    {
        var result = new Dictionary<string, object>();
        for (uint index = 0; index < ImageCount(); index++)
            if (Marshal.PtrToStringUTF8(ImageName(index)) is { } path && Path.GetFileName(path).StartsWith("libjalium.native.", StringComparison.Ordinal))
                result[Path.GetFileName(path)] = new { path = Path.GetFullPath(path), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() };
        return result;
    }
    private static byte[] Canvas(MacOSWritingToolsPresentation.Preview preview, int width, int height, double dpi)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(preview.Png); bitmap.EndInit();
        var source = Pixels(bitmap); var canvas = new byte[width * height * 4]; int x = (int)Math.Round(preview.Frame.X * dpi), y = (int)Math.Round(preview.Frame.Y * dpi);
        for (int row = 0; row < bitmap.PixelHeight; row++) for (int col = 0; col < bitmap.PixelWidth; col++)
            if (x + col >= 0 && x + col < width && y + row >= 0 && y + row < height)
                Buffer.BlockCopy(source, (row * bitmap.PixelWidth + col) * 4, canvas, ((y + row) * width + x + col) * 4, 4);
        return canvas;
    }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(Output, name)); encoder.Save(file);
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")] private static extern nint Class(string name);
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")] private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")] private static extern nint ImageName(uint index);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")] private static extern nint Selector(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern nint Send(nint target, nint selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void Integer(nint target, nint selector, nint value);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPush")] private static extern nint PushPool();
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_autoreleasePoolPop")] private static extern void PopPool(nint pool);
}
