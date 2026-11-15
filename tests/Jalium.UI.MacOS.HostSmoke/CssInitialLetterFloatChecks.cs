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
    private const string InitialFloatTail = "Ordinary text continues around the initial letter and each floating box. " +
        "It returns to the full line width after all exclusions end.";
    private const string InitialFloatSoftPrefix = "The first ordinary line continues through another short line. " +
        "The first ordinary line continues through another short line. ";
    private static readonly Color InitialFloatInk = Color.FromRgb(0x48, 0x72, 0x9a);
    private sealed record InitialFloatGlyph(FormattedText Text, Rect Ink, double Cap, double Top, double Height);
    private sealed record InitialFloatTree(StackPanel Root, TextBlock Head, TextBlock Body, Border[] Floats, Rect Initial);

    // This reference contains no initial-letter declarations and calls none of
    // its sizing, exclusion or projection helpers. Native font metrics supply
    // the glyph. An ordinary polygon float describes the independently known
    // union of the initial's rectangle and the float margin boxes/contours.
    private sealed class InitialFloatOracleRoot(InitialFloatGlyph glyph, Rect initial, Rect[] floats) : StackPanel
    {
        internal readonly List<TextBlock> Texts = [];
        private readonly Dictionary<(string, FlowDirection), NativeTextParagraph> _paragraphs = [];
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach (var rect in floats) context.DrawRectangle(new SolidColorBrush(InitialFloatInk), null, rect);
            context.DrawText(glyph.Text, new(initial.Left - glyph.Ink.Left, initial.Top + glyph.Cap - glyph.Text.Baseline));
            // Draw ordinary native text at its float-layout coordinates. This
            // avoids fractional child-edge clipping changing antialiased ink.
            foreach (var text in Texts)
            {
                var offset = text.TransformToAncestor(this);
                foreach (var line in text.GetDocumentLines(text.RenderSize.Width))
                {
                    var content = text.Text.Substring(line.Start, line.End - line.Start);
                    if (content.Length == 0) continue;
                    var key = (content, text.FlowDirection);
                    if (!_paragraphs.TryGetValue(key, out var paragraph))
                    {
                        paragraph = NativeTextParagraph.TryCreate([new(content, "Georgia", 16, 400, 0, Color.FromRgb(0x23, 0x3c, 0x32))],
                            "Georgia", 16, 1000000, 24, TextAlignment.Left, text.FlowDirection, noWrap: true)
                            ?? throw new Exception("Initial/float reference paragraph unavailable");
                        _paragraphs.Add(key, paragraph);
                    }
                    var left = text.DocumentTextBounds(line.Start, 0)[0].X - paragraph.Caret(0, 0, backward: false).X;
                    var formatted = new FormattedText(content, "Georgia", 16) { PlatformTextLine = paragraph.Lines[0] };
                    context.DrawText(formatted, new(left + offset.X, line.Top + offset.Y));
                }
            }
        }
    }

    private static InitialFloatGlyph InitialFloatOracleGlyph()
    {
        var units = TextMeasurement.GetFontUnitMetrics("Georgia", 16);
        var font = TextMeasurement.GetFontMetrics("Georgia", 16);
        var cap = 48 + units.CapHeight; var size = cap / units.CapHeight * 16;
        var measured = new FormattedText("A", "Georgia", size) { Foreground = new SolidColorBrush(InitialInk) };
        TextMeasurement.MeasureText(measured);
        if (!TextMeasurement.TryGetFontInkBounds(measured, out var ink)) throw new Exception("Initial/float reference ink unavailable");
        var paragraph = NativeTextParagraph.TryCreate([new("A", "Georgia", size, 400, 0, InitialInk)],
            "Georgia", size, 1000000, 0, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
            ?? throw new Exception("Initial/float reference native glyph unavailable");
        var glyph = new FormattedText("A", "Georgia", size) { Foreground = new SolidColorBrush(InitialInk),
            PlatformTextLine = paragraph.Lines[0], Baseline = paragraph.Lines[0].Metrics.Baseline };
        return new(glyph, ink, cap, font.Ascent + (24 - font.Ascent - font.Descent) / 2 + 48 - cap,
            Math.Max(cap, cap + ink.Bottom));
    }

    private static InitialFloatTree InitialFloatScene(bool oracle, bool rtl, string state, int width, int dpi)
    {
        static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var glyph = InitialFloatOracleGlyph(); var margin = state == "margins";
        var dropped = state is "blocked" or "shape-blocked";
        var origin = dropped ? state == "blocked" ? 100d : 80d : 0;
        var initialInset = state switch { "before" or "after" => 40d, "stacked" or "shape" => 80d, "margins" => 102d, _ => 0 };
        var initial = new Rect(rtl ? width - initialInset - glyph.Ink.Width : initialInset,
            origin + glyph.Top, glyph.Ink.Width, glyph.Height);
        var laterTop = initial.Bottom;
        if (state == "later-soft")
        {
            // Determine the source anchor using ordinary text around an
            // ordinary rectangular float, independently of initial-letter.
            var prefixRoot = new StackPanel { Width = width, Height = 300,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
            Css.SetStyle(prefixRoot, InitialBodyStyle);
            var exclusion = new Border(); Css.SetStyle(exclusion, "float:inline-start;width:" + N(glyph.Ink.Width) +
                "px;height:" + N(initial.Bottom) + "px;"); prefixRoot.Children.Add(exclusion);
            var prefix = new TextBlock { Text = InitialFloatSoftPrefix }; Css.SetStyle(prefix, "display:inline;white-space:pre-line;"); prefixRoot.Children.Add(prefix);
            FirstLetterLayout(prefixRoot, width, 300);
            laterTop = Math.Max(laterTop, prefix.GetDocumentLines(prefix.RenderSize.Width)[^1].Top + prefix.TransformToAncestor(prefixRoot).Y);
        }
        Rect At(double inset, double y, double w, double h) => new(rtl ? width - inset - w : inset, y, w, h);
        var floating = state switch
        {
            "before" or "after" => new[] { At(0, 0, 40, 50) },
            "stacked" => new[] { At(0, 0, 40, 50), At(0, 50, 80, 50) },
            "blocked" => new[] { At(0, 0, 40, 50), At(0, 50, width - 40, 50) },
            "later-hard" or "later-soft" => new[] { At(0, laterTop, 40, 35) },
            "shape" => new[] { At(0, 0, 80, 80) },
            "shape-blocked" => new[] { At(0, 0, width - 32, 80) },
            "margins" => new[] { At(rtl ? 10 : 6, 5, 80, 80) },
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        StackPanel root = oracle ? new InitialFloatOracleRoot(glyph, initial, floating) : new StackPanel();
        root.Width = width; root.Height = 300; root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        root.Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1));
        Css.SetStyle(root, InitialBodyStyle + (oracle && origin > 0 ? "padding-top:" + N(origin) + "px;" : ""));
        var head = new TextBlock { Text = "A", IsTextSelectionEnabled = true };
        if (!oracle) Css.SetStyle(head, "display:inline;initial-letter:3;initial-letter-wrap:none;color:#146342;" +
            (margin ? "margin:4px 6px 8px;" : ""));
        var boxes = new List<Border>();
        void Float(int index, string extra = "")
        {
            if (oracle) return;
            var rect = floating[index]; var box = new Border(); boxes.Add(box);
            Css.SetStyle(box, "float:inline-start;width:" + N(rect.Width) + "px;height:" + N(rect.Height) +
                "px;background:#48729a;" + extra); root.Children.Add(box);
        }
        TextBlock Body(string value)
        {
            var text = new TextBlock { Text = value, IsTextSelectionEnabled = true };
            // Keep the soft-break seam undecorated so ordinary text uses its
            // normal cross-source shaping. A leading gutter preserves the
            // prefix's negative ink bearing without decorating that seam.
            var gutter = !oracle || rtl ? "" : state != "later-soft" ? "padding:0 1px;margin:0 -1px;"
                : value == InitialFloatSoftPrefix ? "padding-left:1px;margin-left:-1px;" : "";
            Css.SetStyle(text, "display:inline;white-space:pre-line;" + gutter); root.Children.Add(text);
            if (root is InitialFloatOracleRoot reference && rtl && (state != "later-hard" || value != InitialFloatTail))
            { text.Opacity = 0; reference.Texts.Add(text); }
            return text;
        }
        if (oracle)
        {
            var edge = initialInset + glyph.Ink.Width + (margin ? 6 : 0);
            var steps = new List<(double Edge, double Bottom)> { (edge, initial.Bottom - origin + (margin ? 8 : 0)) };
            if (state == "stacked") steps.Add((80, 100));
            if (state is "before" or "after" && 50 > initial.Bottom) steps.Add((40, 50));
            if (state is "later-hard" or "later-soft")
            {
                if (laterTop > initial.Bottom) steps.Add((0, laterTop));
                steps.Add((40, laterTop + 35));
            }
            if (margin) steps.Add((96, 100));
            // In the circle case the initial ends below the circle's last
            // vertex (y=60), so the ordinary text then has its full width.
            var points = new List<string> { N(rtl ? width : 0) + "px 0px", N(rtl ? width - edge : edge) + "px 0px" };
            foreach (var step in steps)
            {
                var x = rtl ? width - step.Edge : step.Edge;
                points.Add(N(x) + "px " + N(step.Bottom) + "px");
                var next = steps.IndexOf(step) + 1;
                if (next < steps.Count) points.Add(N(rtl ? width - steps[next].Edge : steps[next].Edge) + "px " + N(step.Bottom) + "px");
            }
            points.Add(N(rtl ? width : 0) + "px " + N(steps[^1].Bottom) + "px");
            var exclusion = new Border();
            Css.SetStyle(exclusion, "float:inline-start;width:" + width + "px;height:" + N(steps[^1].Bottom) +
                "px;shape-outside:polygon(" + string.Join(",", points) + ");"); root.Children.Add(exclusion);
        }
        else
        {
            if (state is "before" or "stacked" or "blocked") Float(0);
            if (state is "stacked" or "blocked") Float(1, "clear:inline-start;");
            if (state is "shape" or "shape-blocked") Float(0, state == "shape" ? "shape-outside:circle(20px at 40px 40px);" : "shape-outside:circle(10px at 50% 40px);");
            if (margin) Float(0, "margin:5px 10px 15px 6px;");
            root.Children.Add(head);
            if (state == "after") Float(0);
        }
        if (state is "later-hard" or "later-soft")
        {
            Body(state == "later-hard" ? "brief\n" : InitialFloatSoftPrefix);
            Float(0);
        }
        var body = Body(state is "later-hard" or "later-soft" ? InitialFloatTail : "brief " + InitialFloatTail);
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, width, 300);
        return new(root, head, body, boxes.ToArray(), initial);
    }

    internal static int RunInitialLetterFloats()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            {
                context.DefaultRenderingEngine = engine;
                foreach (var rtl in new[] { false, true }) foreach (var state in new[] { "before", "after", "stacked", "blocked", "later-hard", "later-soft", "shape", "shape-blocked", "margins" })
                foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = InitialFloatScene(false, rtl, state, width, dpi); var expected = InitialFloatScene(true, rtl, state, width, dpi);
                    foreach (var recorded in new[] { false, true })
                    {
                        var label = $"initial-floats-{backend}-{engine}-{rtl}-{state}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>(); var largest = 0;
                        try
                        {
                            byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 300 * dpi, dpi, dc =>
                            {
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                                else root.Render(dc);
                            }, () => true, context) ?? throw new Exception("Initial/float capture unavailable"));
                            var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                            for (var index = 0; index < pixels.Length; index++) largest = Math.Max(largest, Math.Abs(pixels[index] - reference[index]));
                            if (largest > 3) errors.Add($"Maximum channel difference {largest}");
                            if (largest > 3 || !recorded && dpi == 1 &&
                                (width == 312 || state is "stacked" or "blocked" or "later-soft" or "margins"))
                            { Save(pixels, width * dpi, 300 * dpi, label + ".png"); Save(reference, width * dpi, 300 * dpi, label + "-expected.png"); }
                            var allocation = actual.Head.CssInlineBox ?? throw new Exception("Initial allocation unavailable");
                            var delta = actual.Head.TransformToAncestor(actual.Root); var local = allocation.Fragments.Single().Rect;
                            var rect = new Rect(local.X + delta.X, local.Y + delta.Y, local.Width, local.Height);
                            if (Math.Abs(rect.X - actual.Initial.X) > .001 || Math.Abs(rect.Y - actual.Initial.Y) > .001 ||
                                Math.Abs(rect.Width - actual.Initial.Width) > .001 || Math.Abs(rect.Height - actual.Initial.Height) > .001)
                                errors.Add($"Initial geometry {rect} != independent {actual.Initial}");
                            if (actual.Head.Text != "A" || actual.Head.FontSize != 16) errors.Add("Authored initial source/font changed");
                            actual.Head.SelectAll(); if (actual.Head.SelectedText != "A") errors.Add("Initial selection changed"); actual.Head.Select(0, 0);
                            actual.Body.SelectAll(); if (actual.Body.SelectedText != actual.Body.Text) errors.Add("Body selection changed"); actual.Body.Select(0, 0);
                            if (!actual.Root.IsMeasureValid || !actual.Root.IsArrangeValid) errors.Add("Layout did not settle");
                        }
                        catch (Exception error) { errors.Add(error.ToString()); }
                        if (errors.Count > 0) failures++;
                        records.Add(new { label, largest, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-floats.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
                tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false,
                oracle = "Independent native font metrics/glyphs, known float geometry and ordinary polygon exclusion envelopes; CSS Inline 3 section 7.9.3 and WPT initial-letter-float-001 through 004" }));
            return failures == 0 ? 0 : 1;
        }
        finally { context.Dispose(); PopPool(pool); }
    }
}
