using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunDocumentPaintRefresh()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); var failures = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var dpi in new[] { 1, 2 })
        foreach (var width in new[] { 220, 360 })
        foreach (var editor in new[] { false, true })
        foreach (var kind in new[] { "run", "span", "paragraph", "grid", "variable", "brush-run", "brush-background", "brush-root", "brush-empty" })
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_PAINT_CASES") is { Length: > 0 } cases && !cases.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"{engine}-{dpi}-{width}-{(editor ? "editor" : "reader")}-{kind}";
            var errors = new List<string>();
            try
            {
                if (context.DefaultRenderingEngine != engine) throw new Exception("Requested engine unavailable");
                using var actual = new PaintScene(kind, width, dpi, editor, false); actual.Layout();
                byte[] Capture(PaintScene scene) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 220 * dpi, dpi,
                    dc => scene.View.Render(dc), () => true) ?? throw new Exception("Native readback failed"));
                var original = actual.Document.GetText(); var beforeRenderMetrics = PaintMetrics(actual.View); var normal = Capture(actual); var normalMetrics = PaintMetrics(actual.View);
                actual.Change(true); actual.Layout(); var changed = Capture(actual); var changedMetrics = PaintMetrics(actual.View);
                using var expected = new PaintScene(kind, width, dpi, editor, true); expected.Layout(); var oracle = Capture(expected); var expectedMetrics = PaintMetrics(expected.View);
                Check(!normal.SequenceEqual(oracle), "oracle paint change had no visible effect");
                Check(changed.SequenceEqual(oracle), "current view differs from independently styled fresh document");
                var editing = actual.View is RichTextBox && kind.StartsWith("brush-", StringComparison.Ordinal);
                if (editing)
                {
                    var textEditor = (RichTextBox)actual.View; textEditor.IsUndoEnabled = true; textEditor.AppendText("追加"); actual.Layout();
                    Check(ReferenceEquals(textEditor.Document, actual.Document), "append replaced the original styled document");
                    var edited = Capture(actual); Check(actual.Document.GetText() != original, "editor did not append text");
                    Check(textEditor.Undo(), "editor could not undo append"); actual.Layout(); var undone = Capture(actual);
                    Check(actual.Document.GetText() == original && undone.SequenceEqual(changed), "undo did not preserve the current paint");
                    Check(textEditor.Redo(), "editor could not redo append"); actual.Layout(); var redone = Capture(actual);
                    Check(redone.SequenceEqual(edited), "redo did not restore exact pixels");
                    Check(textEditor.Undo(), "editor could not restore original text"); actual.Layout();
                    Save(edited, width * dpi, 220 * dpi, label + "-edited.png"); Save(undone, width * dpi, 220 * dpi, label + "-undone.png");
                    Save(redone, width * dpi, 220 * dpi, label + "-redone.png");
                }
                actual.Change(false); actual.Layout(); var restored = Capture(actual); var restoredMetrics = PaintMetrics(actual.View);
                Check(restored.SequenceEqual(normal), "restoring paint did not restore exact pixels");
                Check(original == actual.Document.GetText(), "paint changed original document text");
                Save(normal, width * dpi, 220 * dpi, label + "-normal.png"); Save(changed, width * dpi, 220 * dpi, label + "-changed.png");
                Save(oracle, width * dpi, 220 * dpi, label + "-expected.png"); Save(restored, width * dpi, 220 * dpi, label + "-restored.png");
                records.Add(new { label, errors, editing, changedDifferentBytes = changed.Zip(oracle).Count(pair => pair.First != pair.Second),
                    restoredDifferentBytes = restored.Zip(normal).Count(pair => pair.First != pair.Second), beforeRenderMetrics, normalMetrics, changedMetrics, expectedMetrics, restoredMetrics });
            }
            catch (Exception e) { errors.Add(e.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) ++failures; Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            void Check(bool value, string error) { if (!value) errors.Add(error); }
        }
        File.WriteAllText(Path.Combine(Output, "paint.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failures == 0 ? 0 : 1;
    }

    private static object[] PaintMetrics(Visual root)
    {
        var result = new List<object>(); Walk(root); return result.ToArray();
        void Walk(Visual node)
        {
            if (node is FrameworkElement element)
            {
                var at = element.TransformToAncestor(root);
                result.Add(new { type = element.GetType().Name, source = (element as ICssStyledBox)?.StyleSource.GetType().Name,
                    x = Metric(at.X), y = Metric(at.Y), width = Metric(element.RenderSize.Width), height = Metric(element.RenderSize.Height),
                    dpi = VisualTreeHelper.GetDpi(element).DpiScaleY,
                    element.IsMeasureValid, element.IsArrangeValid,
                    text = (element as TextBlock)?.Text, font = (element as TextBlock)?.FontFamily.ToString(), fontSize = (element as TextBlock)?.FontSize,
                    fragments = element is TextBlock text && (bool)typeof(TextBlock).GetProperty("HasCssInlineFragments", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(text)!,
                    layoutDirty = element is TextBlock dirty && (bool)typeof(TextBlock).GetField("_layoutDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dirty)!,
                    whitespace = element is TextBlock white && (bool)typeof(TextBlock).GetField("_cssInlineWhitespace", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(white)! });
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (VisualTreeHelper.GetChild(node, i) is Visual child) Walk(child);
        }
        static double? Metric(double value) => double.IsFinite(value) ? value : null;
    }

    private sealed class PaintScene : IDisposable
    {
        internal readonly FlowDocument Document;
        internal readonly FrameworkElement View;
        private readonly RichTextBox? _editor;
        private readonly FlowDocumentPresentation? _reader;
        private readonly Run _run = new("样式更新");
        private readonly Span _span;
        private readonly Paragraph _paragraph;
        private readonly Section _section;
        private readonly SolidColorBrush _brush = new(Color.FromRgb(20, 99, 66));
        private readonly string _kind;
        private readonly int _width;
        internal PaintScene(string kind, int width, int dpi, bool editor, bool changed)
        {
            _kind = kind; _width = width; _span = new(_run);
            _paragraph = new(new Run("前文 ")); _paragraph.Inlines.Add(_span); _paragraph.Inlines.Add(new Run(" 尾文"));
            _section = new(_paragraph); _section.Blocks.Add(new Paragraph(new Run("第二段 / CSS 绘制")));
            Document = new(_section) { PagePadding = new(0), FontFamily = "Helvetica Neue", FontSize = 20, LineHeight = 32 };
            var foreground = kind is "brush-root" or "brush-empty" ? "#ffffff" : "#146342";
            Css.SetStyle(Document, $"font-family:'Helvetica Neue';font-size:20px;line-height:32px;color:{foreground};background:#eef5f0;padding:12px;border:1px solid #146342;");
            Css.SetStyleSheet(Document, "Paragraph,Section{margin:0;padding:0;border:0;}");
            if (kind == "brush-run") _run.Foreground = _brush;
            if (kind == "brush-background") { _span.Background = _brush; _run.Foreground = Brushes.White; }
            if (kind is "brush-root" or "brush-empty") Document.Background = _brush;
            if (kind == "brush-empty") Document.Blocks.Clear();
            Change(changed);
            if (editor) View = _editor = new(Document) { Template = null, Padding = new(0), BorderThickness = new(0), Background = Brushes.White, IsUndoEnabled = false };
            else { _reader = new(); _reader.SetDocument(Document); View = _reader; }
            View.SetRootDpi(new(dpi, dpi));
        }
        internal void Change(bool changed)
        {
            string ink = changed ? "#2255ff" : "#146342", fill = changed ? "#fff0d8" : "#d6eade";
            if (_kind.StartsWith("brush-", StringComparison.Ordinal)) { _brush.Color = changed ? Color.FromRgb(34, 85, 255) : Color.FromRgb(20, 99, 66); return; }
            if (_kind == "variable") { Css.SetStyle(Document, $"--ink:{ink};--fill:{fill};font-family:'Helvetica Neue';font-size:20px;line-height:32px;color:var(--ink);background:var(--fill);padding:12px;border:1px solid currentColor;"); return; }
            var target = _kind switch { "run" => (DependencyObject)_run, "span" => _span, "paragraph" => _paragraph, _ => _section };
            string layout = _kind == "span" ? "display:inline;padding:2px 4px;border:1px solid currentColor;box-decoration-break:clone;" : _kind == "grid" ? "display:grid;grid-template-columns:1fr;" : "";
            Css.SetStyle(target, layout + $"color:{ink};background:{fill};");
        }
        internal void Layout()
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                _reader?.SetViewportHeight(220); View.Measure(new(_width, 220)); View.Arrange(new(0, 0, _width, 220));
                View.UpdateLayout();
            }
        }
        public void Dispose() { _reader?.Dispose(); }
    }
}
