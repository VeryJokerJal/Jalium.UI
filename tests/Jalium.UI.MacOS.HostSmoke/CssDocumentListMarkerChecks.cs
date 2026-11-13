using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunDocumentListMarkers()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failures = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int scale in new[] { 1, 2 })
        foreach (int width in new[] { 220, 360 })
        foreach (string kind in new[] { "decimal", "roman", "nested", "hidden", "pseudo", "inside", "grid", "grid-gap", "grid-rtl-gap", "grid-image", "columns", "empty", "image", "rtl", "large" })
        {
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_MARKER_CASES") is { Length: > 0 } selected && !selected.Split(',').Contains(kind)) continue;
            context.DefaultRenderingEngine = engine; string label = $"{engine}-{scale}-{width}-{kind}"; var errors = new List<string>();
            try
            {
                if (context.DefaultRenderingEngine != engine) throw new Exception("Requested engine unavailable");
                using var scene = new MarkerScene(kind, width, scale); scene.Layout();
                byte[] Capture(bool reference = false) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * scale, 300 * scale, scale, dc =>
                {
                    if (!reference) scene.Editor.Render(dc);
                    else
                    {
                        dc.DrawRectangle(Brushes.White, null, new(0, 0, width, 280));
                        dc.PushClip(new RectangleGeometry(new(3, 3, width - 6, 274)));
                        dc.PushTransform(new TranslateTransform(3, 3)); scene.Reference.Render(dc); dc.Pop(); dc.Pop();
                    }
                }, () => true) ?? throw new Exception("Native readback failed"));
                string raw = scene.Document.GetText(); byte[] normal = Capture(), reference = Capture(true);
                var beforeMetrics = MarkerMetrics(scene.Editor);
                var markers = MarkerVisuals(scene.Editor).Where(marker => marker.Visibility == Visibility.Visible && CssTableVisibility.Visible(marker.Origin.Target)).ToArray();
                Check(markers.Select(marker => marker.Text).SequenceEqual(scene.Expected()), "marker content differs from expected source numbering");
                Check(normal.SequenceEqual(reference), "editor and independent reader pixels differ");
                var ink = new List<int>();
                foreach (var marker in markers)
                {
                    var at = marker.TransformToAncestor(scene.Editor); var rect = new Rect(at.X, at.Y, marker.RenderSize.Width, marker.RenderSize.Height);
                    Check(double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0,
                        "visible marker has invalid native geometry");
                    int pixels = MarkerInk(normal, width * scale, 300 * scale, rect, scale); ink.Add(pixels);
                    if (rect.Bottom > 3 && rect.Top < 277 && rect.Right > 3 && rect.Left < width - 3) Check(pixels > 1, "visible marker has no native glyph/image ink");
                }
                scene.List.StartIndex = 11; scene.Layout(); byte[] changed = Capture();
                var changedMetrics = MarkerMetrics(scene.Editor);
                Check(MarkerVisuals(scene.Editor).Where(marker => marker.Visibility == Visibility.Visible && CssTableVisibility.Visible(marker.Origin.Target)).Select(marker => marker.Text).SequenceEqual(scene.Expected(11)), "counter mutation did not refresh");
                scene.List.StartIndex = scene.Start; scene.Layout(); byte[] restored = Capture();
                var restoredMetrics = MarkerMetrics(scene.Editor);
                Check(restored.SequenceEqual(normal), "restoring the start did not restore exact pixels");
                Check(scene.Document.GetText() == raw, "marker layout changed original source text");
                Save(normal, width * scale, 300 * scale, label + "-normal.png"); Save(reference, width * scale, 300 * scale, label + "-reader.png");
                Save(changed, width * scale, 300 * scale, label + "-changed.png"); Save(restored, width * scale, 300 * scale, label + "-restored.png");
                records.Add(new { label, errors, markerText = markers.Select(marker => marker.Text).ToArray(), ink,
                    readerDifferentBytes = normal.Zip(reference).Count(pair => pair.First != pair.Second), beforeMetrics, changedMetrics, restoredMetrics });
            }
            catch (Exception exception) { errors.Add(exception.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) ++failures; Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            void Check(bool value, string error) { if (!value) errors.Add(error); }
        }
        File.WriteAllText(Path.Combine(Output, "markers.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
            nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
        PopPool(pool); return failures == 0 ? 0 : 1;
    }

    private static object[] MarkerMetrics(Visual root) => MarkerVisuals(root).Select(marker =>
    {
        var at = marker.TransformToAncestor(root);
        return (object)new { marker.Text, marker.Visibility, fontSize = Metric(marker.FontSize), lineHeight = Metric(marker.LineHeight),
            desiredWidth = Metric(marker.DesiredSize.Width), desiredHeight = Metric(marker.DesiredSize.Height),
            renderWidth = Metric(marker.RenderSize.Width), renderHeight = Metric(marker.RenderSize.Height),
            x = Metric(at.X), y = Metric(at.Y), cssHeight = Metric(CssFlowProperties.LineMetrics(marker).Height) };
        static double? Metric(double value) => double.IsFinite(value) ? value : null;
    }).ToArray();

    private static IEnumerable<CssGeneratedText> MarkerVisuals(Visual root)
    {
        if (root is CssGeneratedText { Kind: CssPseudoElement.Marker } marker) yield return marker;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisualTreeHelper.GetChild(root, i) is Visual child)
                foreach (var value in MarkerVisuals(child)) yield return value;
    }
    private static int MarkerInk(byte[] pixels, int width, int height, Rect rect, int scale)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)Math.Floor(rect.Top * scale)); y < Math.Min(height, Math.Ceiling(rect.Bottom * scale)); y++)
        for (int x = Math.Max(0, (int)Math.Floor(rect.Left * scale)); x < Math.Min(width, Math.Ceiling(rect.Right * scale)); x++)
        {
            int at = (y * width + x) * 4;
            if (pixels[at + 3] > 200 && Math.Min(pixels[at], Math.Min(pixels[at + 1], pixels[at + 2])) < 150) count++;
        }
        return count;
    }

    private sealed class MarkerScene : IDisposable
    {
        internal readonly FlowDocument Document;
        internal readonly RichTextBox Editor;
        internal readonly FlowDocumentPresentation Reference = new();
        internal readonly DocList List;
        internal readonly int Start;
        private readonly int _width;
        private readonly string _kind;
        internal MarkerScene(string kind, int width, int scale)
        {
            _kind = kind; _width = width; Start = kind == "large" ? 9999 : 3;
            List = new() { StartIndex = Start, MarkerStyle = kind == "roman" ? TextMarkerStyle.UpperRoman : TextMarkerStyle.Decimal };
            List.ListItems.Add(new ListItem(new Paragraph(new Run("第一项 / CSS 文档"))));
            List.ListItems.Add(new ListItem(new Paragraph(new Run("第二项")))); List.ListItems.Add(new ListItem(new Paragraph(new Run("第三项"))));
            if (kind.EndsWith("-gap", StringComparison.Ordinal)) List.MarkerOffset = 12;
            if (kind == "hidden") Css.SetStyle(List.ListItems[1], "display:none;");
            if (kind == "empty") List.ListItems[0].Blocks.Clear();
            if (kind is "image" or "grid-image") Css.SetStyle(List, "list-style-image:linear-gradient(#146342,#2255ff);");
            if (kind == "inside") Css.SetStyle(List, "list-style-position:inside;");
            if (kind == "nested")
            {
                var nested = new DocList(new ListItem(new Paragraph(new Run("嵌套内容")))) { StartIndex = 7, MarkerStyle = TextMarkerStyle.UpperRoman };
                List.ListItems[0].Blocks.Add(nested);
            }
            Document = new(List) { PagePadding = new(0), FontFamily = "Helvetica Neue", FontSize = 20, LineHeight = 32 };
            string extra = kind.StartsWith("grid", StringComparison.Ordinal) ? "display:grid;grid-template-columns:1fr;" + (kind == "grid-rtl-gap" ? "direction:rtl;text-align:start;" : "")
                : kind == "columns" ? "columns:2;column-gap:16px;height:96px;column-fill:auto;" : kind == "rtl" ? "direction:rtl;text-align:start;" : "";
            Css.SetStyle(Document, "font-family:'Helvetica Neue';font-size:20px;line-height:32px;color:#146342;padding:12px;border:1px solid #146342;background:#eef5f0;" + extra);
            Css.SetStyleSheet(Document, "Paragraph{margin:0;padding:0;border:0;}List,ListItem{margin:0;}" +
                (kind == "pseudo" ? "ListItem::marker{content:'第' counter(list-item) '步 ';font-size:21px;color:#2255ff;letter-spacing:1px;}" : "") +
                (kind == "inside" ? "ListItem>Paragraph{display:inline;}" : ""));
            Editor = new(Document) { Template = null, Padding = new(3), BorderThickness = new(0), Background = Brushes.White, IsUndoEnabled = false };
            Editor.SetRootDpi(new DpiScale(scale, scale)); Reference.SetRootDpi(new DpiScale(scale, scale)); Reference.SetDocument(Document);
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                Editor.Measure(new(_width, 280)); Editor.Arrange(new(0, 0, _width, 280));
                Reference.SetViewportHeight(274); Reference.Measure(new(_width - 6, double.PositiveInfinity));
                Reference.Arrange(new(0, 0, _width - 6, Math.Max(274, Reference.DesiredSize.Height)));
            }
        }
        internal string[] Expected(int? start = null)
        {
            int first = start ?? Start;
            if (_kind is "image" or "grid-image") return [" ", " ", " "];
            if (_kind == "roman") return Enumerable.Range(first, 3).Select(value => CssCounterStyleMap.Empty.FormatMarker(value, "upper-roman")).ToArray();
            if (_kind == "pseudo") return Enumerable.Range(first, 3).Select(value => $"第{value}步 ").ToArray();
            if (_kind == "nested") return [$"{first}. ", "VII. ", $"{first + 1}. ", $"{first + 2}. "];
            return Enumerable.Range(first, _kind == "hidden" ? 2 : 3).Select(value => $"{value}. ").ToArray();
        }
        public void Dispose() => Reference.Dispose();
    }
}
