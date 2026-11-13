using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed class SharedCapsScene
    {
        internal readonly StackPanel Root = new();
        internal readonly TextBlock[] Texts;
        private readonly DependencyObject?[] _parents;
        internal SharedCapsScene(string before, string source, string after, bool caps, bool nested, bool barrier)
        {
            Texts = [new() { Text = before, IsTextSelectionEnabled = true }, new() { Text = source, IsTextSelectionEnabled = true },
                new() { Text = after, IsTextSelectionEnabled = true }];
            foreach (var text in Texts)
            {
                if (nested) { var frame = new StackPanel(); Css.SetStyle(frame, "display:inline"); frame.Children.Add(text); Root.Children.Add(frame); }
                else Root.Children.Add(text);
            }
            _parents = Texts.Select(text => text.Parent).ToArray();
            Css.SetStyleSheet(Root, "TextBlock{display:inline}");
            if (barrier) Css.SetStyle(Root.Children[1], "display:inline;padding-left:1px;");
            Change(caps);
        }
        internal void Change(bool caps) => Css.SetStyle(Texts[1],
            "font-variant-caps:" + (caps ? "unicase" : "normal") + ";font-feature-settings:'smcp' off;");
        internal void Style(int width) => Css.SetStyle(Root,
            $"display:block;width:{width}px;font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;overflow-wrap:anywhere;text-autospace:no-autospace;");
        internal void AssertSources(params string[] sources)
        {
            for (int i = 0; i < Texts.Length; ++i)
            {
                var text = Texts[i]; text.SelectAll();
                if (text.Text != sources[i] || text.SelectedText != sources[i] || !ReferenceEquals(text.Parent, _parents[i]))
                    throw new Exception("Shared caps changed its authored text or source parent");
                text.Select(0, 0);
            }
        }
    }

    private sealed class SharedCapsReference : FrameworkElement, IDisposable
    {
        private sealed record Piece(int Source, string Text, double X, double Baseline);
        private readonly List<(NativeTextParagraph Paragraph, double X, double Baseline)> _paint = [];
        internal SharedCapsReference(SharedCapsScene literal, string originalMiddle, int width, bool barrier)
        {
            Width = width; Height = 240;
            var pieces = new List<Piece>();
            for (int i = 0; i < literal.Texts.Length; ++i)
            {
                var text = literal.Texts[i]; var origin = text.TransformToAncestor(literal.Root);
                foreach (var line in BoundaryLines(text))
                {
                    if (line.Length == 0) continue;
                    var cells = text.GetSourceRangeSegments(line.Start, line.Length);
                    if (cells.Count == 0) continue;
                    pieces.Add(new(i, text.Text.Substring(line.Start, line.Length), origin.X + cells[0].StartX, origin.Y + cells[0].Baseline));
                }
            }
            var metrics = TextMeasurement.GetFontMetrics("Seravek", 32, 400, 0);
            double strut = metrics.Ascent + (44 - metrics.Ascent - metrics.Descent) / 2;
            foreach (var row in pieces.GroupBy(piece => Math.Round(piece.Baseline, 3)))
            {
                var ordered = row.OrderBy(piece => piece.X).ToArray();
                // These fixtures are LTR. A decorated source edge separates
                // glyph shaping while leaving Unicode casing context intact.
                var groups = new List<Piece[]>(); var pending = new List<Piece>();
                foreach (var piece in ordered)
                {
                    // The authored padding is on source 1's left edge only.
                    // Its right neighbor can still share glyph shaping.
                    if (barrier && piece.Source == 1 && pending.Count > 0)
                    { groups.Add(pending.ToArray()); pending.Clear(); }
                    pending.Add(piece);
                }
                if (pending.Count > 0) groups.Add(pending.ToArray());
                foreach (var group in groups)
                {
                    var spans = group.Select(piece => new NativeTextParagraph.Span(piece.Text, "Seravek", 32, 400, 0,
                        Color.FromRgb(0x14, 0x63, 0x42))).ToArray();
                    var paragraph = NativeTextParagraph.TryCreate(spans, "Seravek", 32, 10000, 44,
                        TextAlignment.Left, FlowDirection.LeftToRight, noWrap:true) ?? throw new Exception("Shared caps literal reference unavailable");
                    bool shared = group.Length > 1 || group.Any(piece => piece.Source == 1 && originalMiddle.Contains('\u03a3'));
                    // The pre-existing ordinary direct paint path has a small
                    // CoreText placement offset. Keep that independent oracle
                    // for untouched plain sources, and the CSS strut otherwise.
                    double baseline = group[0].Baseline + (shared ? 0 : CapsBaselineReference.Baseline("Seravek", group[0].Text) - strut);
                    _paint.Add((paragraph, group[0].X, baseline));
                }
            }
        }
        protected override Size MeasureOverride(Size availableSize) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach (var (paragraph, x, baseline) in _paint)
            {
                var line = paragraph.Lines[0];
                context.DrawText(new FormattedText(paragraph.Text, "Seravek", 32) {
                    PlatformTextLine = line, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)),
                    MaxTextWidth = 10000, MaxTextHeight = 240 }, new(x, baseline - line.Metrics.Baseline));
            }
        }
        public void Dispose() { foreach (var item in _paint) item.Paragraph.Dispose(); }
    }

    internal static int RunSharedCapsContext()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 24, 40, 48, 200 })
            foreach (var row in new[] { ("middle", "Ο", "Σ", "Α", "σ"), ("final", "Ο", "Σ", "", "ς"),
                ("apostrophe", "Ο'", "Σ", "Α", "σ"), ("following", "Ο", "Σ'", "Α", "σ'"),
                ("accent", "Ο\u0301", "Σ", "Α", "σ"), ("break-before", "Ο\n", "Σ", "", "σ"),
                ("break-after", "Ο", "Σ", "\nΑ", "ς") })
            foreach (string kind in new[] { "siblings", "nested", "physical-edge" })
            {
                context.DefaultRenderingEngine = engine;
                string label = $"shared-caps-{engine}-{dpi}-{width}-{row.Item1}-{kind}";
                var errors = new List<string>(); int differentBytes = -1;
                try
                {
                    byte[] Capture(FrameworkElement view, int size)
                    {
                        view.SetRootDpi(new(dpi, dpi));
                        for (int i = 0; i < 3; ++i) { view.Measure(new(size, 240)); view.Arrange(new(0, 0, size, 240)); view.UpdateLayout(); }
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(size * dpi, 240 * dpi, dpi, dc => view.Render(dc), () => true)
                            ?? throw new Exception("Shared caps readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context))
                            throw new Exception("Shared caps readback changed backend/engine");
                        return Pixels(bitmap);
                    }
                    bool nested = kind != "siblings", barrier = kind == "physical-edge";
                    var actual = new SharedCapsScene(row.Item2, row.Item3, row.Item4, false, nested, barrier);
                    var literal = new SharedCapsScene(row.Item2, row.Item5, row.Item4, false, nested, barrier);
                    actual.Style(width); literal.Style(width);
                    var normal = Capture(actual.Root, width);
                    actual.Change(true); var changed = Capture(actual.Root, width); Capture(literal.Root, width);
                    using var reference = new SharedCapsReference(literal, row.Item3, width, barrier);
                    var expected = Capture(reference, width); differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    Save(changed, width * dpi, 240 * dpi, label + "-changed.png");
                    Save(expected, width * dpi, 240 * dpi, label + "-expected.png");
                    if (differentBytes != 0) throw new Exception($"Shared caps differs from literal reference ({differentBytes} bytes)");
                    for (int i = 0; i < actual.Texts.Length; ++i)
                    {
                        var text = actual.Texts[i]; var paint = literal.Texts[i];
                        var origin = text.TransformToAncestor(actual.Root); var wantedOrigin = paint.TransformToAncestor(literal.Root);
                        if (Math.Abs(origin.X - wantedOrigin.X) > .003 || Math.Abs(origin.Y - wantedOrigin.Y) > .003)
                            throw new Exception("Shared caps source allocation differs");
                        for (int at = 0; at < text.Text.Length; ++at)
                        {
                            var cells = text.GetSourceRangeSegments(at, 1); var wanted = paint.GetSourceRangeSegments(at, 1);
                            if (cells.Count != wanted.Count) throw new Exception("Shared caps source range differs");
                            for (int j = 0; j < cells.Count; ++j)
                            {
                                var cell = cells[j]; var other = wanted[j];
                                if (Math.Abs(cell.StartX - other.StartX) > .003 || Math.Abs(cell.EndX - other.EndX) > .003 || Math.Abs(cell.Baseline - other.Baseline) > .003)
                                    throw new Exception("Shared caps source geometry differs");
                                if (at > 0 && char.GetUnicodeCategory(text.Text[at]) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                                var hit = text.GetPositionFromPoint(new(cell.StartX + .01, cell.Baseline - 2), true);
                                if (hit is null || hit.DocumentStart.GetOffsetToPosition(hit) != at) throw new Exception("Shared caps point hit changed source offset");
                            }
                        }
                    }
                    actual.AssertSources(row.Item2, row.Item3, row.Item4);
                    actual.Style(400); Capture(actual.Root, 400); actual.Style(width);
                    if (!Capture(actual.Root, width).SequenceEqual(changed)) throw new Exception("Shared caps width round trip retained stale context");
                    actual.Change(false); var restored = Capture(actual.Root, width);
                    if (!restored.SequenceEqual(normal)) throw new Exception("Shared caps reset retained contextual glyphs");
                    Save(restored, width * dpi, 240 * dpi, label + "-restored.png");
                }
                catch (Exception error) { errors.Add(error.ToString()); failed++; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes, errors });
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "shared-caps.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
                nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
