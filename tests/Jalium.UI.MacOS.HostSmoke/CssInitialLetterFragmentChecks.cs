using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record InitialFragmentReferenceRow(FormattedText Text, Point Origin, Rect Box, bool Left, bool Right);

    private sealed class InitialFragmentsReferenceTail : TextBlock
    {
        // Use ordinary-float line geometry, allowing glyph antialias coverage
        // across its fractional start edge just as a shared paragraph does.
        internal void DrawReference(DrawingContext context, Point offset)
        {
            var family = FontFamily.GetRenderingSource(this);
            var font = TextMeasurement.GetFontMetrics(family, FontSize);
            foreach (var line in GetDocumentLines(RenderSize.Width))
            {
                var content = Text.Substring(line.Start, line.End - line.Start);
                if (content.Length == 0) continue;
                var glyph = new FormattedText(content, family, FontSize)
                    { Foreground = Foreground, MaxTextWidth = double.MaxValue, MaxTextHeight = Math.Max(line.Height, FontSize * 2) };
                glyph.ApplyTextOptionsFrom(this);
                if (!TextMeasurement.MeasureText(glyph)) throw new Exception("Reference body measurement unavailable");
                var first = DocumentTextBounds(line.Start, 0)[0];
                var baseline = font.Ascent + (line.Height - font.Ascent - font.Descent) / 2;
                context.DrawText(glyph, new(offset.X + first.X, offset.Y + line.Top + baseline - glyph.Baseline));
            }
        }
    }

    // The reference lays out no initial-letter boxes. Native font metrics
    // supply glyph/fragment geometry; ordinary CSS painting primitives and an
    // empty float supply decorations and the final body exclusion.
    private sealed class InitialFragmentsReference(InitialFragmentReferenceRow[] rows) : StackPanel
    {
        internal InitialFragmentsReferenceTail? Tail;
        private readonly Border _borderStyle = CreateBorderStyle();
        private Border? _borderPainter;
        private static Border CreateBorderStyle()
        {
            var border = new Border(); Css.SetStyle(border, "border:2px solid #146342;"); return border;
        }
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            var background = new SolidColorBrush(Color.FromRgb(0xe1, 0xee, 0xe4));
            VisualTreeHelper.SetRootDpi(_borderStyle, DpiScale);
            foreach (var row in rows)
                context.DrawGeometry(background, null, new CssRoundedRectangleGeometry(row.Box, default));
            foreach (var row in rows) context.DrawText(row.Text, row.Origin);
            foreach (var row in rows)
                CssBorderAdornment.Draw(_borderStyle, context, ref _borderPainter, row.Box,
                    borderThickness: new(row.Left ? 2 : 0, 2, row.Right ? 2 : 0, 2), fragmentRadii: default(CssUsedBorderRadii));
            if (Tail is { } tail) tail.DrawReference(context, tail.TransformToAncestor(this));
        }
    }

    private static (StackPanel Root, TextBlock Head, TextBlock Tail) InitialFragmentsActual(string decoration, int width, int dpi)
    {
        var root = new StackPanel { Width = width, Height = 400, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        Css.SetStyle(root, InitialBodyStyle);
        var head = new TextBlock { Text = "Alpha beta eta", IsTextSelectionEnabled = true };
        var tail = new TextBlock { Text = "continues beside the final large line. Each fragment keeps its own decoration and reading order." };
        Css.SetStyle(head, "display:inline;initial-letter:3;color:#146342;background:#e1eee4;padding:3px;border:2px solid #146342;box-decoration-break:" + decoration + ";");
        Css.SetStyle(tail, "display:inline;"); root.Children.Add(head); root.Children.Add(tail);
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, width, 400);
        return (root, head, tail);
    }

    private static (StackPanel Root, TextBlock Tail, InitialFragmentReferenceRow[] Rows) InitialFragmentsOracle(string decoration, int width, int dpi)
    {
        var units = TextMeasurement.GetFontUnitMetrics("Georgia", 16); var font = TextMeasurement.GetFontMetrics("Georgia", 16);
        var cap = 48 + units.CapHeight; var size = cap / units.CapHeight * 16;
        var ordinaryBaseline = font.Ascent + (24 - font.Ascent - font.Descent) / 2;
        var relativeTop = ordinaryBaseline + 48 - cap;
        string[] words = ["Alpha", "beta", "eta"]; var rows = new List<InitialFragmentReferenceRow>();
        var first = 0; var y = 0d; var lastTop = 0d;
        while (first < words.Length)
        {
            var end = first + 1; var left = decoration == "clone" || first == 0;
            Rect Ink(string content)
            {
                var formatted = new FormattedText(content, "Georgia", size);
                if (!TextMeasurement.TryGetFontInkBounds(formatted, out var ink)) throw new Exception("Reference fragment ink unavailable");
                return ink;
            }
            for (var candidate = first + 2; candidate <= words.Length; candidate++)
            {
                var candidateInk = Ink(string.Join(' ', words[first..candidate]));
                var candidateRight = decoration == "clone" || candidate == words.Length;
                if (candidateInk.Width + (left ? 5 : 0) + (candidateRight ? 5 : 0) > width) break;
                end = candidate;
            }
            var content = string.Join(' ', words[first..end]); var bounds = Ink(content);
            var right = decoration == "clone" || end == words.Length;
            var top = Math.Min(0, cap + bounds.Top);
            var height = Math.Max(cap, cap + bounds.Bottom) - top + 10;
            var box = new Rect(0, y + relativeTop - 5 + top, bounds.Width + (left ? 5 : 0) + (right ? 5 : 0), height);
            var paragraph = NativeTextParagraph.TryCreate([new(content, "Georgia", size, 400, 0, InitialInk)],
                "Georgia", size, 1000000, 0, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
                ?? throw new Exception("Reference fragment paragraph unavailable");
            var glyph = new FormattedText(content, "Georgia", size) { Foreground = new SolidColorBrush(InitialInk),
                PlatformTextLine = paragraph.Lines[0], Baseline = paragraph.Lines[0].Metrics.Baseline };
            rows.Add(new(glyph, new((left ? 5 : 0) - bounds.Left, y + ordinaryBaseline + 48 - glyph.Baseline), box, left, right));
            lastTop = y; y += height; first = end;
        }
        var shift = Math.Max(0, -rows[0].Box.Top);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            rows[index] = row with { Origin = new(row.Origin.X, row.Origin.Y + shift),
                Box = new(row.Box.X, row.Box.Y + shift, row.Box.Width, row.Box.Height) };
        }
        lastTop += shift;
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "px";
        var root = new InitialFragmentsReference(rows.ToArray())
            { Width = width, Height = 400, Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1)) };
        Css.SetStyle(root, InitialBodyStyle + "padding-top:" + Number(lastTop));
        var final = rows[^1].Box;
        var exclusion = new Border(); Css.SetStyle(exclusion, "display:block;float:left;width:" + Number(final.Width) +
            ";height:" + Number(final.Height) + ";margin-top:" + Number(final.Y - lastTop));
        var tail = new InitialFragmentsReferenceTail { Text = "continues beside the final large line. Each fragment keeps its own decoration and reading order." };
        Css.SetStyle(tail, "display:inline;"); tail.Opacity = 0; root.Tail = tail;
        root.Children.Add(exclusion); root.Children.Add(tail);
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, width, 400);
        return (root, tail, rows.ToArray());
    }

    internal static int RunInitialLetterFragments()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var decoration in new[] { "slice", "clone" }) foreach (var width in new[] { 260, 500 }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = InitialFragmentsActual(decoration, width, dpi); var expected = InitialFragmentsOracle(decoration, width, dpi);
                    foreach (var recorded in new[] { false, true })
                    {
                        var label = $"initial-fragments-{backend}-{engine}-{decoration}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>(); var largest = 0;
                        try
                        {
                            var allocation = actual.Head.CssInlineBox ?? throw new Exception("Initial has no shared fragments");
                            if (allocation.Fragments.Length != expected.Rows.Length) throw new Exception("Reference fragment count differs");
                            var offset = actual.Head.TransformToAncestor(actual.Root);
                            for (var index = 0; index < expected.Rows.Length; index++)
                            {
                                var a = allocation.Fragments[index].Rect; var b = expected.Rows[index].Box;
                                if (Math.Abs(a.X + offset.X - b.X) > .001 || Math.Abs(a.Y + offset.Y - b.Y) > .001 ||
                                    Math.Abs(a.Width - b.Width) > .001 || Math.Abs(a.Height - b.Height) > .001)
                                    throw new Exception($"Fragment {index}: {a} + {offset} != {b}");
                            }
                            var actualTail = actual.Tail.TransformToAncestor(actual.Root);
                            var referenceTail = expected.Tail.TransformToAncestor(expected.Root);
                            for (var index = 0; index < actual.Tail.Text.Length; index++)
                            {
                                var a = actual.Tail.DocumentTextBounds(index, 1); var b = expected.Tail.DocumentTextBounds(index, 1);
                                if (a.Count != b.Count) throw new Exception($"Body source {index}: range count differs");
                                for (var range = 0; range < a.Count; range++)
                                    if (Math.Abs(a[range].X + actualTail.X - b[range].X - referenceTail.X) > .001 ||
                                        Math.Abs(a[range].Y + actualTail.Y - b[range].Y - referenceTail.Y) > .001 ||
                                        Math.Abs(a[range].Width - b[range].Width) > .001 || Math.Abs(a[range].Height - b[range].Height) > .001)
                                        throw new Exception($"Body source {index}: {a[range]} + {actualTail} != {b[range]} + {referenceTail}");
                            }
                            byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 400 * dpi, dpi, dc =>
                            {
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                                else root.Render(dc);
                            }, () => true, context) ?? throw new Exception("Fragment capture unavailable"));
                            var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                            for (var index = 0; index < pixels.Length; index++)
                            {
                                var difference = Math.Abs(pixels[index] - reference[index]); largest = Math.Max(largest, difference);
                                if (difference > 3 && errors.Count < 4) errors.Add($"Pixel {index / 4} channel {index % 4}: {pixels[index]} != {reference[index]}");
                            }
                            if (errors.Count > 0 || !recorded && dpi == 1)
                            { Save(pixels, width * dpi, 400 * dpi, label + ".png"); Save(reference, width * dpi, 400 * dpi, label + "-expected.png"); }
                            actual.Head.SelectAll(); if (actual.Head.SelectedText != actual.Head.Text || actual.Head.FontSize != 16) throw new Exception("Authored text/font changed");
                            actual.Head.Select(0, 0);
                            if (!actual.Root.IsMeasureValid || !actual.Root.IsArrangeValid) throw new Exception("Fragment layout did not settle");
                        }
                        catch (Exception error) { errors.Add(error.ToString()); }
                        if (errors.Count > 0) failures++;
                        records.Add(new { label, largest, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-fragments.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
                tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
