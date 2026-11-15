using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record CombiningCase(string Name, string Family, string Language, string Variant,
        string Source, string Marks, string BasePaint, string MarkPaint, double PaintSize,
        string? MarkFamily = null, bool DisableSynthesis = false, bool MarkDisablesSynthesis = false);

    private sealed class CombiningReference : FrameworkElement, IDisposable
    {
        private readonly List<(NativeTextParagraph Paragraph, double X)> _pieces = [];
        private readonly double _baseline;
        internal CombiningReference(CombiningCase row, bool edge, int width)
        {
            Width = width; Height = 160;
            double Baseline(string family) { var f = TextMeasurement.GetFontMetrics(family, 32, 400, 0); return f.Ascent + (44 - f.Ascent - f.Descent) / 2; }
            _baseline = Math.Max(Baseline(row.Family), Baseline(row.MarkFamily ?? row.Family));
            NativeTextParagraph.Span Span(string text, string family) => new(text, family, row.PaintSize, 400, 0, Color.FromRgb(0x14, 0x63, 0x42));
            NativeTextParagraph Make(params NativeTextParagraph.Span[] spans) => NativeTextParagraph.TryCreate(spans.Where(span => span.Text.Length > 0).ToArray(),
                row.Family, 32, 10000, 44, TextAlignment.Left, FlowDirection.LeftToRight, noWrap:true)
                ?? throw new Exception("Combining literal reference unavailable");
            if (edge)
            {
                var first = Make(Span(row.BasePaint, row.Family)); _pieces.Add((first, 0));
                if (row.MarkPaint.Length > 0) _pieces.Add((Make(Span(row.MarkPaint, row.MarkFamily ?? row.Family)), first.Lines[0].Metrics.Line.Width + 1));
            }
            else _pieces.Add((Make(Span(row.BasePaint, row.Family), Span(row.MarkPaint, row.MarkFamily ?? row.Family)), 0));
        }
        protected override Size MeasureOverride(Size availableSize) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach (var (paragraph, x) in _pieces)
            {
                var line = paragraph.Lines[0];
                context.DrawText(new FormattedText(paragraph.Text, "Arial", 32) { PlatformTextLine = line,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)), MaxTextWidth = 10000, MaxTextHeight = 160 },
                    new(x, _baseline - line.Metrics.Baseline));
            }
        }
        public void Dispose() { foreach (var item in _pieces) item.Paragraph.Dispose(); }
    }

    internal static int RunCapsCombiningContext()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var rows = new[] {
            new CombiningCase("turkish", "Seravek", "tr", "unicase", "I", "\u0307", "i", "", 32),
            new CombiningCase("azerbaijani", "Seravek", "az-Latn", "unicase", "I", "\u0307", "i", "", 32),
            new CombiningCase("lithuanian-i", "Arial", "lt", "small-caps", "i", "\u0307", "I", "", 25.6),
            new CombiningCase("lithuanian-j", "Arial", "lt", "small-caps", "j", "\u0307", "J", "", 25.6),
            new CombiningCase("turkish-synthetic", "Arial", "tr", "unicase", "I", "\u0307", "İ", "", 25.6),
            new CombiningCase("mixed-face", "Seravek", "tr", "unicase", "I", "\u0307", "i", "", 32, "Arial"),
            new CombiningCase("cyrillic-tag", "Seravek", "az-Cyrl", "unicase", "I", "\u0307", "i", "\u0307", 32),
            new CombiningCase("none", "Arial", "lt", "small-caps", "i", "\u0307", "i", "\u0307", 32, DisableSynthesis:true),
            new CombiningCase("more-above", "Seravek", "lt", "unicase", "I", "\u0301", "i\u0307", "\u0301", 32),
            new CombiningCase("mark-policy", "Seravek", "tr", "unicase", "I", "\u0307", "i", "\u0307", 32, MarkDisablesSynthesis:true),
        };
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 24, 40, 220 })
            foreach (var row in rows) foreach (bool edge in new[] { false, true })
            {
                context.DefaultRenderingEngine = engine;
                string label = $"caps-combining-{engine}-{dpi}-{width}-{row.Name}-{(edge ? "edge" : "joined")}";
                var errors = new List<string>(); int differentBytes = -1;
                try
                {
                    byte[] Capture(FrameworkElement view, int size)
                    {
                        view.SetRootDpi(new(dpi, dpi));
                        for (int i = 0; i < 3; ++i) { view.Measure(new(size, 160)); view.Arrange(new(0, 0, size, 160)); view.UpdateLayout(); }
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(size * dpi, 160 * dpi, dpi, dc => view.Render(dc), () => true)
                            ?? throw new Exception("Combining readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context))
                            throw new Exception("Combining readback changed backend/engine");
                        return Pixels(bitmap);
                    }
                    var root = new StackPanel { Language = XmlLanguage.GetLanguage(row.Language) };
                    var first = new TextBlock { Text = row.Source, IsTextSelectionEnabled = true };
                    var marks = new TextBlock { Text = row.Marks, IsTextSelectionEnabled = true };
                    root.Children.Add(first); root.Children.Add(marks); Css.SetStyleSheet(root, "TextBlock{display:inline}");
                    Css.SetStyle(marks, (edge ? "padding-left:1px;" : "") +
                        (row.MarkFamily is { } family ? $"font-family:'{family}';" : "") +
                        (row.MarkDisablesSynthesis ? "font-synthesis-small-caps:none;" : ""));
                    void Style(bool changed) => Css.SetStyle(root, $"display:block;font-family:'{row.Family}';font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-feature-settings:'smcp' off;font-variant-caps:{(changed ? row.Variant : "normal")};font-synthesis-small-caps:{(row.DisableSynthesis ? "none" : "auto")};");
                    Style(false); var normal = Capture(root, width); Style(true); var changed = Capture(root, width);
                    using var reference = new CombiningReference(row, edge, width); var expected = Capture(reference, width);
                    differentBytes = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    Save(changed, width * dpi, 160 * dpi, label + "-changed.png"); Save(expected, width * dpi, 160 * dpi, label + "-expected.png");
                    if (differentBytes != 0) throw new Exception($"Combining paint differs from literal reference ({differentBytes} bytes)");
                    var sourceGeometry = new List<object>();
                    foreach (var text in new[] { first, marks })
                    {
                        string original = ReferenceEquals(text, first) ? row.Source : row.Marks;
                        text.SelectAll(); if (text.SelectedText != original || text.Text != original || !ReferenceEquals(text.Parent, root))
                            throw new Exception("Combining source ownership or selection changed");
                        text.Select(0, 0);
                        foreach (var cell in text.GetSourceRangeSegments(0, original.Length))
                            sourceGeometry.Add(new { original, cell.StartX, cell.EndX, cell.Baseline, origin = text.TransformToAncestor(root) });
                    }
                    File.WriteAllText(Path.Combine(Output, label + "-geometry.json"), JsonSerializer.Serialize(sourceGeometry));
                    Capture(root, 400); if (!Capture(root, width).SequenceEqual(changed)) throw new Exception("Combining resize retained stale context");
                    Style(false); var restored = Capture(root, width); if (!restored.SequenceEqual(normal)) throw new Exception("Combining reset retained a synthesized glyph");
                    Save(restored, width * dpi, 160 * dpi, label + "-restored.png");
                }
                catch (Exception error) { errors.Add(error.ToString()); failed++; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes, errors });
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "caps-combining.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
                nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
