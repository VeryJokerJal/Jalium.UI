using System.Collections;
using System.Reflection;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record BoundaryLine(int Start, int Length);
    private static BoundaryLine[] BoundaryLines(TextBlock text) => ((IEnumerable)typeof(TextBlock)
        .GetField("_layoutLines", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(text)!)
        .Cast<object>().Select(line => new BoundaryLine((int)line.GetType().GetProperty("StartIndex")!.GetValue(line)!,
            (int)line.GetType().GetProperty("Length")!.GetValue(line)!)).ToArray();

    // Only literal expected text enters the reference paragraphs. The ordinary
    // reference control supplies line breaks; a fixed regression below also
    // pins the formerly incorrect width-40 break independently.
    private sealed class BoundaryReference : FrameworkElement, IDisposable
    {
        private readonly List<(BoundaryLine Source, NativeTextParagraph Paragraph)> _lines = [];
        private readonly double _baseline;
        internal BoundaryReference(TextBlock reference, string expected, int width)
        {
            Width = width; Height = 240;
            var metrics = TextMeasurement.GetFontMetrics("Seravek", 32, 400, 0);
            _baseline = metrics.Ascent + (44 - metrics.Ascent - metrics.Descent) / 2;
            foreach (var line in BoundaryLines(reference))
            {
                string text = expected.Substring(line.Start, line.Length);
                if (text.Length == 0) continue;
                var paragraph = NativeTextParagraph.TryCreate([new(text, "Seravek", 32, 400, 0,
                    Color.FromRgb(0x14, 0x63, 0x42))], "Seravek", 32, 10000, 44, TextAlignment.Left,
                    FlowDirection.LeftToRight, noWrap:true) ?? throw new Exception("Boundary reference unavailable");
                _lines.Add((line, paragraph));
            }
        }
        protected override Size MeasureOverride(Size availableSize) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            for (int i = 0; i < _lines.Count; ++i)
            {
                var paragraph = _lines[i].Paragraph; var line = paragraph.Lines[0];
                context.DrawText(new FormattedText(paragraph.Text, "Seravek", 32) {
                    PlatformTextLine = line, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)),
                    MaxTextWidth = 10000, MaxTextHeight = 240 }, new(0, i * 44 + _baseline - line.Metrics.Baseline));
            }
        }
        internal void CheckGeometry(TextBlock actual)
        {
            for (int lineIndex = 0; lineIndex < _lines.Count; ++lineIndex)
            {
                var (line, paragraph) = _lines[lineIndex];
                for (int at = 0; at < line.Length; ++at)
                {
                    // Combining marks share their base's source grapheme.
                    if (at > 0 && char.GetUnicodeCategory(paragraph.Text[at]) is System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                    var expected = paragraph.Selection(0, at, 1);
                    var actualCells = actual.GetSourceRangeSegments(line.Start + at, 1);
                    if (expected.Length != 1 || actualCells.Count != 1) throw new Exception("Boundary source range is missing");
                    var cell = actualCells[0]; var wanted = expected[0];
                    if (Math.Abs(cell.StartX - wanted.X) > .003 || Math.Abs(cell.EndX - wanted.X - wanted.Width) > .003 ||
                        Math.Abs(cell.Baseline - (lineIndex * 44 + _baseline)) > .003)
                        throw new Exception("Boundary source geometry differs from literal glyph reference");
                    var hit = actual.GetPositionFromPoint(new(cell.StartX + .01, cell.Baseline - 2), true);
                    if (hit is null || hit.DocumentStart.GetOffsetToPosition(hit) != line.Start + at)
                        throw new Exception("Boundary point hit changed the source offset");
                }
            }
        }
        public void Dispose() { foreach (var line in _lines) line.Paragraph.Dispose(); }
    }

    internal static int RunFontCapsBoundaries()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failed = 0;
        const string style = "font-family:Seravek;font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;overflow-wrap:anywhere;text-autospace:no-autospace;";
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 24, 40, 48, 200 })
            foreach (var row in new[] { ("middle", "Ο", "ΣΑ", "Οσα", "οσα"),
                ("final", "Ο", "Σ", "Ος", "ος"), ("apostrophe", "Ο'", "ΣΑ", "Ο'σα", "ο'σα"),
                ("following", "Ο", "Σ'Α", "Οσ'α", "οσ'α"), ("accent", "Ο\u0301", "ΣΑ", "Ο\u0301σα", "ο\u0301σα"),
                ("hard-break", "Ο\n", "ΣΑ", "Ο\nσα", "ο\nσα") })
            foreach (bool mixed in new[] { false, true })
            {
                context.DefaultRenderingEngine = engine;
                string label = $"caps-boundary-{engine}-{dpi}-{width}-{row.Item1}-{(mixed ? "mixed" : "plain")}";
                var errors = new List<string>(); int difference = -1;
                try
                {
                    byte[] Capture(FrameworkElement view, int size)
                    {
                        view.SetRootDpi(new(dpi, dpi));
                        for (int i = 0; i < 3; ++i) { view.Measure(new(size, 240)); view.Arrange(new(0, 0, size, 240)); view.UpdateLayout(); }
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(size * dpi, 240 * dpi, dpi, dc => view.Render(dc), () => true)
                            ?? throw new Exception("Boundary readback failed");
                        if (context.Backend != RenderBackend.Metal || context.DefaultRenderingEngine != engine || !ReferenceEquals(RenderContext.Current, context))
                            throw new Exception("Boundary readback changed backend/engine");
                        return Pixels(bitmap);
                    }
                    string source = row.Item2 + row.Item3, expectedText = mixed ? row.Item4 : row.Item5;
                    var actual = new TextBlock(); Run? tail = null;
                    if (mixed) { actual.Inlines.Add(new Run(row.Item2)); tail = new Run(row.Item3); actual.Inlines.Add(tail); }
                    else actual.Text = source;
                    var parent = tail?.Parent;
                    var literal = new TextBlock { Text = expectedText };
                    Css.SetStyle(actual, style); Css.SetStyle(literal, style);
                    var normal = Capture(actual, width);
                    Css.SetStyle(tail ?? (DependencyObject)actual, (mixed ? "" : style) + "font-variant-caps:unicase;font-feature-settings:'smcp' off");
                    var changed = Capture(actual, width); Capture(literal, width);
                    if (!BoundaryLines(actual).SequenceEqual(BoundaryLines(literal))) throw new Exception("Boundary wrapping differs from literal text");
                    if (mixed && row.Item1 == "middle" && width == 40 && !BoundaryLines(actual).Select(line => line.Length).SequenceEqual(new[] { 1, 2 }))
                        throw new Exception("Width-40 final-sigma regression returned");
                    using var reference = new BoundaryReference(literal, expectedText, width);
                    var expected = Capture(reference, width); difference = changed.Zip(expected).Count(pair => pair.First != pair.Second);
                    if (difference != 0) throw new Exception($"Boundary paint differs from literal reference ({difference} bytes)");
                    reference.CheckGeometry(actual);
                    actual.SelectAll(); if (actual.SelectedText != source) throw new Exception("Boundary selection changed authored text");
                    actual.Select(0, 0);
                    Capture(actual, 400); var resized = Capture(actual, width);
                    if (!resized.SequenceEqual(changed)) throw new Exception("Resize retained stale fragment context");
                    if (tail is not null && (tail.Text != row.Item3 || !ReferenceEquals(tail.Parent, parent) || !ReferenceEquals(actual.Inlines[1], tail)))
                        throw new Exception("Boundary run ownership changed");
                    Css.SetStyle(tail ?? (DependencyObject)actual, (mixed ? "" : style) + "font-variant-caps:normal");
                    var restored = Capture(actual, width);
                    if (!restored.SequenceEqual(normal)) throw new Exception("Boundary reset retained contextual glyphs");
                    foreach (var frame in new[] { ("changed", changed), ("expected", expected), ("restored", restored) })
                        Save(frame.Item2, width * dpi, 240 * dpi, label + "-" + frame.Item1 + ".png");
                }
                catch (Exception error) { errors.Add(error.ToString()); failed++; }
                records.Add(new { label, backend = context.Backend.ToString(), engine = engine.ToString(), differentBytes = difference, errors });
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
            }
            File.WriteAllText(Path.Combine(Output, "caps-boundary.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
                nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
