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
    private const string BlockFlowTail = "Following paragraphs continue around the initial letter until its bottom. " +
        "Ordinary text then returns to the full available width and keeps its original reading order.";
    private sealed record BlockFlowTree(StackPanel Root, FrameworkElement First, TextBlock? Original, TextBlock Body);

    private sealed class BlockFlowOracleRoot : StackPanel
    {
        internal TextBlock? Brief;
        private readonly Dictionary<string, NativeTextParagraph> _paragraphs = [];
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (Brief is not { } brief) return;
            var offset = brief.TransformToAncestor(this);
            foreach (var line in brief.GetDocumentLines(brief.RenderSize.Width))
            {
                var content = brief.Text.Substring(line.Start, line.End - line.Start);
                if (content.Length == 0) continue;
                if (!_paragraphs.TryGetValue(content, out var paragraph))
                {
                    paragraph = NativeTextParagraph.TryCreate([new(content, "Georgia", 16, 400, 0, Color.FromRgb(0x23, 0x3c, 0x32))],
                        "Georgia", 16, 1000000, 24, TextAlignment.Left, brief.FlowDirection, noWrap: true)
                        ?? throw new Exception("Reference brief paragraph unavailable");
                    _paragraphs.Add(content, paragraph);
                }
                var left = brief.DocumentTextBounds(line.Start, 0)[0].X - paragraph.Caret(0, 0, backward: false).X;
                var text = new FormattedText(content, "Georgia", 16) { PlatformTextLine = paragraph.Lines[0] };
                context.DrawText(text, new(left + offset.X, line.Top + offset.Y));
            }
        }
    }

    // The reference uses native font metrics and ordinary floats. It never
    // declares initial-letter or calls its layout, size or projection helpers.
    private sealed class BlockFlowGlyph(FormattedText glyph, Rect ink, double cap) : FrameworkElement
    {
        protected override void OnRender(DrawingContext context) => context.DrawText(glyph, new(-ink.Left, cap - glyph.Baseline));
    }

    private static (BlockFlowGlyph Glyph, double Bottom) BlockFlowOracleGlyph(string content, bool rtl)
    {
        var units = TextMeasurement.GetFontUnitMetrics("Georgia", 16);
        var font = TextMeasurement.GetFontMetrics("Georgia", 16);
        var cap = 48 + units.CapHeight;
        var size = cap / units.CapHeight * 16;
        var measured = new FormattedText(content, "Georgia", size) { Foreground = new SolidColorBrush(InitialInk) };
        TextMeasurement.MeasureText(measured);
        if (!TextMeasurement.TryGetFontInkBounds(measured, out var ink)) throw new Exception("Block-flow reference ink unavailable");
        var paragraph = NativeTextParagraph.TryCreate([new(content, "Georgia", size, 400, 0, InitialInk)],
            "Georgia", size, 1000000, 0, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
            ?? throw new Exception("Block-flow reference native glyph unavailable");
        var glyph = new FormattedText(content, "Georgia", size) { Foreground = new SolidColorBrush(InitialInk),
            PlatformTextLine = paragraph.Lines[0], Baseline = paragraph.Lines[0].Metrics.Baseline };
        var top = font.Ascent + (24 - font.Ascent - font.Descent) / 2 + 48 - cap;
        var height = Math.Max(cap, cap + ink.Bottom);
        var box = new BlockFlowGlyph(glyph, ink, cap);
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture) + "px";
        Css.SetStyle(box, "float:" + (rtl ? "right" : "left") + ";width:" + Number(ink.Width) + ";height:" + Number(height) + ";margin-top:" + Number(top) + ";");
        return (box, top + height);
    }

    private static BlockFlowTree BlockFlowScene(bool oracle, bool pseudo, bool definite, bool rtl, string state, int width, int dpi)
    {
        StackPanel root = oracle ? new BlockFlowOracleRoot() : new StackPanel();
        root.Width = width; root.Height = 360; root.Background = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf1));
        root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Css.SetStyle(root, InitialBodyStyle);
        var firstStyle = InitialBodyStyle + (definite ? "height:24px;" : "");
        FrameworkElement first; TextBlock? original = null;
        var reference = oracle ? BlockFlowOracleGlyph("A", rtl) : default;
        if (!oracle && pseudo)
        {
            original = new TextBlock { Text = "A brief", IsTextSelectionEnabled = true };
            Css.SetClass(original, "block-flow-initial");
            Css.GetStyleSheets(original).Add(CssStyleSheet.Parse(".block-flow-initial{" + firstStyle + "}" +
                ".block-flow-initial::first-letter{initial-letter:3;initial-letter-wrap:none;color:#146342;}"));
            first = original;
        }
        else
        {
            var panel = new StackPanel(); first = panel; Css.SetStyle(first, firstStyle);
            if (oracle) panel.Children.Add(reference.Glyph);
            else
            {
                original = new TextBlock { Text = "A", IsTextSelectionEnabled = true };
                Css.SetStyle(original, "display:inline;initial-letter:3;initial-letter-wrap:none;color:#146342;");
                panel.Children.Add(original);
            }
            // A float is not an inline predecessor. Explicitly retain the
            // authored post-initial space in the ordinary reference.
            // The plain LTR reference keeps a one-pixel ink gutter on each
            // side; negative margins preserve its ordinary inline advance.
            var brief = new TextBlock { Text = pseudo ? " brief" : "brief" };
            Css.SetStyle(brief, "display:inline;" + (pseudo ? "white-space:pre;" : "") +
                (oracle && !pseudo && !rtl ? "padding:0 1px;margin:0 -1px;" : "")); panel.Children.Add(brief);
            if (oracle && (pseudo || rtl))
            {
                // Draw ordinary text in root coordinates after measuring its
                // float geometry, avoiding fractional child-edge ink clipping.
                brief.Opacity = 0; ((BlockFlowOracleRoot)root).Brief = brief;
            }
        }
        if (state == "nested")
        {
            var wrapper = new StackPanel(); wrapper.Children.Add(first);
            Css.SetStyle(wrapper, "display:block;margin-left:12px;padding-left:8px;padding-top:5px;border-left:2px solid #146342;");
            root.Children.Add(wrapper);
            var empty = new StackPanel(); Css.SetStyle(empty, "display:block;height:0;margin-bottom:3px;"); root.Children.Add(empty);
        }
        else root.Children.Add(first);
        var next = new StackPanel();
        var stateStyle = state switch
        {
            "clear-left" => "clear:left;", "clear-right" => "clear:right;", "flow-root" => "display:flow-root;",
            "decorated" => "padding:4px 8px;border:1px solid #146342;background:#e1eee4;",
            _ => "",
        };
        // Ordinary BFCs may sit beside floats. Initial-letter's independent
        // formatting-context rule instead clears the whole previous initial.
        if (oracle && state == "flow-root") stateStyle += "clear:both;";
        if (oracle && state == "next-initial")
            stateStyle += "padding-top:" + Math.Max(0, reference.Bottom - 24).ToString("R", CultureInfo.InvariantCulture) + "px;";
        Css.SetStyle(next, InitialBodyStyle + stateStyle);
        if (state == "next-initial")
        {
            if (oracle) next.Children.Add(BlockFlowOracleGlyph("B", rtl).Glyph);
            else
            {
                var head = new TextBlock { Text = "B" };
                Css.SetStyle(head, "display:inline;initial-letter:3;initial-letter-wrap:none;color:#146342;"); next.Children.Add(head);
            }
        }
        var body = new TextBlock { Text = BlockFlowTail, IsTextSelectionEnabled = true };
        Css.SetStyle(body, "display:inline;"); next.Children.Add(body); root.Children.Add(next);
        VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); FirstLetterLayout(root, width, 360);
        return new(root, first, original, body);
    }

    internal static int RunInitialLetterBlockFlow()
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
                foreach (var pseudo in new[] { false, true }) foreach (var definite in new[] { false, true }) foreach (var rtl in new[] { false, true })
                foreach (var state in new[] { "normal", "clear-left", "clear-right", "flow-root", "next-initial", "nested", "decorated" })
                foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
                {
                    var actual = BlockFlowScene(false, pseudo, definite, rtl, state, width, dpi);
                    var expected = BlockFlowScene(true, pseudo, definite, rtl, state, width, dpi);
                    foreach (var recorded in new[] { false, true })
                    {
                        var label = $"initial-block-flow-{backend}-{engine}-{pseudo}-{definite}-{rtl}-{state}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>(); var largest = 0;
                        try
                        {
                            byte[] Capture(StackPanel root) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 360 * dpi, dpi, dc =>
                            {
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); root.Render(recorder); DrawingReplayer.Replay(recorder.Commit(), dc); }
                                else root.Render(dc);
                            }, () => true, context) ?? throw new Exception("Block-flow capture unavailable"));
                            var pixels = Capture(actual.Root); var reference = Capture(expected.Root);
                            for (var index = 0; index < pixels.Length; index++) largest = Math.Max(largest, Math.Abs(pixels[index] - reference[index]));
                            if (largest > 3) errors.Add($"Maximum channel difference {largest}");
                            if (largest > 3 || !recorded && !definite && dpi == 1 &&
                                (!rtl && state is "normal" or "next-initial" or "decorated" || rtl && state == "normal" && width == 312))
                            { Save(pixels, width * dpi, 360 * dpi, label + ".png"); Save(reference, width * dpi, 360 * dpi, label + "-expected.png"); }
                            if (actual.Original!.Text != (pseudo ? "A brief" : "A") || actual.Original.FontSize != 16)
                                errors.Add("Authored initial source or font changed");
                            actual.Original.SelectAll();
                            if (actual.Original.SelectedText != actual.Original.Text) errors.Add("Initial selection changed");
                            actual.Original.Select(0, 0); actual.Body.SelectAll();
                            if (actual.Body.SelectedText != BlockFlowTail) errors.Add("Following selection changed");
                            actual.Body.Select(0, 0);
                            if (!actual.Root.IsMeasureValid || !actual.Root.IsArrangeValid) errors.Add("Layout did not settle");
                        }
                        catch (Exception error) { errors.Add(error.ToString()); }
                        if (errors.Count > 0) failures++;
                        records.Add(new { label, largest, errors }); Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")} {label} {string.Join(" | ", errors)}");
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output, "initial-letter-block-flow.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
                tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false,
                oracle = "Independent font metrics and native glyphs; ordinary floats and the WPT short-paragraph clearance reference pattern" }));
            return failures == 0 ? 0 : 1;
        }
        finally
        {
            // The native backend must close while its autorelease pool still
            // exists. The using declaration's second Dispose is idempotent.
            context.Dispose();
            PopPool(pool);
        }
    }
}
