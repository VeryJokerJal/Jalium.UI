using System.Text.Json;
using System.Runtime.InteropServices;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    // Independent platform reference for the pre-synthesis source baseline.
    // CoreText's fixed-line frame placement can differ from CTFont's ascent,
    // particularly when Chinese/emoji fallback fonts share a Latin line.
    private static class CapsBaselineReference
    {
        private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CT = "/System/Library/Frameworks/CoreText.framework/CoreText";
        private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private static readonly nint Foundation = NativeLibrary.Load(CF), Text = NativeLibrary.Load(CT);
        [StructLayout(LayoutKind.Sequential)] private struct Range(nint start, nint length) { internal nint Start = start, Length = length; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { internal double X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rectangle(double width, double height) { internal double X = 0, Y = 0, Width = width, Height = height; }
        [StructLayout(LayoutKind.Sequential)] private struct Setting(int specifier, nint value) { internal int Specifier = specifier; internal nuint Size = 8; internal nint Value = value; }
        [DllImport(CF, CharSet = CharSet.Unicode)] private static extern nint CFStringCreateWithCharacters(nint allocator, [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] text, nint length);
        [DllImport(CF)] private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keys, nint values);
        [DllImport(CF)] private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);
        [DllImport(CF)] private static extern nint CFAttributedStringCreate(nint allocator, nint text, nint attributes);
        [DllImport(CF)] private static extern nint CFArrayGetCount(nint array);
        [DllImport(CF)] private static extern void CFRelease(nint value);
        [DllImport(CT)] private static extern nint CTFontCreateWithName(nint name, double size, nint matrix);
        [DllImport(CT)] private static extern double CTFontGetAscent(nint font);
        [DllImport(CT)] private static extern double CTFontGetDescent(nint font);
        [DllImport(CT)] private static extern double CTFontGetLeading(nint font);
        [DllImport(CT)] private static extern nint CTParagraphStyleCreate([In] Setting[] settings, nint count);
        [DllImport(CT)] private static extern nint CTFramesetterCreateWithAttributedString(nint text);
        [DllImport(CT)] private static extern nint CTFramesetterCreateFrame(nint setter, Range range, nint path, nint attributes);
        [DllImport(CT)] private static extern nint CTFrameGetLines(nint frame);
        [DllImport(CT)] private static extern void CTFrameGetLineOrigins(nint frame, Range range, [Out] Point[] origins);
        [DllImport(CG)] private static extern nint CGPathCreateWithRect(Rectangle rectangle, nint transform);
        internal static double Baseline(string family, string source)
        {
            var owners = new List<nint>(); nint number = Marshal.AllocHGlobal(8);
            nint Own(nint value) { if (value == 0) throw new Exception("CoreText source baseline reference unavailable"); owners.Add(value); return value; }
            nint String(string value) => Own(CFStringCreateWithCharacters(0, value.ToCharArray(), value.Length));
            nint Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(Text, name));
            try
            {
                nint font = Own(CTFontCreateWithName(String(family), 32, 0));
                double ascent = CTFontGetAscent(font), descent = CTFontGetDescent(font), leading = CTFontGetLeading(font);
                Marshal.Copy(new[] { ascent + descent + leading }, 0, number, 1);
                nint style = Own(CTParagraphStyleCreate([new(8, number), new(9, number)], 2));
                nint attributes = Own(CFDictionaryCreateMutable(0, 2, NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryKeyCallBacks"),
                    NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryValueCallBacks")));
                CFDictionarySetValue(attributes, Key("kCTFontAttributeName"), font);
                CFDictionarySetValue(attributes, Key("kCTParagraphStyleAttributeName"), style);
                nint attributed = Own(CFAttributedStringCreate(0, String(source), attributes));
                nint setter = Own(CTFramesetterCreateWithAttributedString(attributed));
                nint path = Own(CGPathCreateWithRect(new(10000, 4000), 0));
                nint frame = Own(CTFramesetterCreateFrame(setter, new(0, 0), path, 0));
                if (CFArrayGetCount(CTFrameGetLines(frame)) == 0) throw new Exception("CoreText source baseline reference has no line");
                var origins = new Point[1]; CTFrameGetLineOrigins(frame, new(0, 1), origins);
                return (44 - ascent - descent) / 2 + 4000 - origins[0].Y;
            }
            finally { for (int i = owners.Count - 1; i >= 0; --i) CFRelease(owners[i]); Marshal.FreeHGlobal(number); }
        }
    }
    private sealed record SynthesisPaint(string Text, double Size, string Features = "");
    private sealed record SynthesisCase(string Name, string Variant, string Source, SynthesisPaint[] Paint,
        string Family = "Arial", string Language = "en-US", string Css = "", bool Changes = true);

    // Reference glyphs/casing/sizes are explicit fixture data. The reference
    // never requests semantic caps or calls the synthesis implementation.
    private sealed class SynthesisReference : FrameworkElement, IDisposable
    {
        private readonly NativeTextParagraph _glyphs;
        private readonly double _baseline;
        internal uint[]? Ranges { get; set; }
        internal int GlyphVisibility { get; set; }
        private static readonly Color Ink = Color.FromRgb(0x14, 0x63, 0x42);
        internal SynthesisReference(SynthesisCase row, int width, bool shared)
        {
            Width = width; Height = 240;
            string Family(SynthesisPaint paint) => paint.Features == "" ? row.Family :
                FontFeatureRenderingSource.Wrap(row.Family, [new(FontFeature.TagOf(paint.Features), 1)]);
            var spans = row.Paint.Select(p => new NativeTextParagraph.Span(p.Text, Family(p), p.Size, 400, 0, Ink)).ToArray();
            _glyphs = NativeTextParagraph.TryCreate(spans, row.Family, 32, width, 44,
                TextAlignment.Left, FlowDirection.LeftToRight) ?? throw new Exception("Synthesis reference paragraph unavailable");
            if (shared) {
                var font = TextMeasurement.GetFontMetrics(row.Family, 32, 400, 0);
                _baseline = font.Ascent + (44 - font.Ascent - font.Descent) / 2;
            } else _baseline = CapsBaselineReference.Baseline(row.Family, row.Source);
        }
        protected override Size MeasureOverride(Size availableSize) => new(Width, Height);
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            foreach (var line in _glyphs.Lines)
            {
                var text = new FormattedText(_glyphs.Text, "Arial", 32) { PlatformTextLine = line, Foreground = new SolidColorBrush(Ink),
                    GlyphVisibility = GlyphVisibility, GlyphRanges = Ranges, MaxTextWidth = 10000, MaxTextHeight = 240 };
                double shift = _baseline - line.Metrics.Baseline;
                context.DrawText(text, new(0, line.Metrics.Line.Y + shift));
            }
        }
        public void Dispose() => _glyphs.Dispose();
    }

    internal static int RunFontCapsSynthesis()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var cases = new SynthesisCase[]
        {
            new("small", "small-caps", "aßi", [new("ASSI", 25.6)]),
            new("all-small", "all-small-caps", "aAßI", [new("AASSI", 25.6)]),
            new("petite", "petite-caps", "aßi", [new("ASSI", 25.6)]),
            new("all-petite", "all-petite-caps", "aAßI", [new("AASSI", 25.6)]),
            new("unicase", "unicase", "ABxy", [new("AB", 25.6), new("xy", 32)]),
            new("turkish", "small-caps", "iı", [new("İI", 25.6)], Language:"tr"),
            new("lithuanian", "small-caps", "i\u0307", [new("I", 25.6)], Language:"lt"),
            new("combining", "small-caps", "e\u0301ß", [new("E\u0301SS", 25.6)]),
            new("caseless", "small-caps", "ab 中文😀", [new("AB", 25.6), new(" 中文😀", 32)]),
            new("sentence", "small-caps", "Straße", [new("S", 32), new("TRASSE", 25.6)]),
            new("none", "small-caps", "aßi", [new("aßi", 32)], Css:"font-synthesis-small-caps:none;", Changes:false),
            new("explicit-off", "small-caps", "aßi", [new("ASSI", 25.6)], Css:"font-feature-settings:'smcp' off;"),
            new("native-only", "small-caps", "abc", [new("abc", 32, "smcp")], "Seravek", Css:"font-synthesis-small-caps:none;"),
            new("native-unicase", "unicase", "ABxy", [new("ab", 32, "smcp"), new("xy", 32)], "Seravek"),
            new("raw-unsupported", "normal", "aßi", [new("aßi", 32)], Css:"font-feature-settings:'smcp' on;", Changes:false),
            new("roman", "small-caps", "ⅰⅱ", [new("ⅠⅡ", 25.6)], "Arial Unicode MS"),
            new("enclosed", "small-caps", "ⓐⓑ", [new("ⒶⒷ", 25.6)], "Arial Unicode MS"),
            new("titlecase", "all-small-caps", "ǅǈ", [new("ǄǇ", 25.6)]),
            new("cased-mark", "small-caps", "Α\u0345", [new("Α", 32), new("Ι", 25.6)], "Arial Unicode MS"),
            new("unicase-final", "unicase", "ΟΣ", [new("ος", 32)], "Seravek", Css:"font-feature-settings:'smcp' off;", Language:"el"),
            new("unicase-middle", "unicase", "ΟΣΑ", [new("οσα", 32)], "Seravek", Css:"font-feature-settings:'smcp' off;", Language:"el"),
            new("unicase-context", "unicase", "ΑΟΣ'Α", [new("αοσ'α", 32)], "Seravek", Css:"font-feature-settings:'smcp' off;", Language:"el"),
            new("turkic-deletion", "unicase", "I\u0307", [new("i", 32)], "Seravek", Css:"font-feature-settings:'smcp' off;", Language:"tr"),
        };
        string[] kinds = ["text", "inline-run", "inline-span", "split", "reader-run", "reader-span", "reader-paragraph", "reader-section", "reader-root",
            "editor-run", "editor-span", "editor-paragraph", "editor-section", "editor-root", "writing-tools"];
        var records = new List<object>(); int failed = 0;
        try
        {
            foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (int dpi in new[] { 1, 2 }) foreach (int width in new[] { 220, 420 })
            foreach (var row in cases) foreach (string kind in kinds)
            {
                if (kind == "writing-tools" && row.Name is not ("small" or "turkic-deletion")) continue;
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_SYNTHESIS_CASES") is { Length: > 0 } chosen && !chosen.Split(',').Contains(row.Name)) continue;
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_CAPS_SYNTHESIS_KINDS") is { Length: > 0 } chosenKinds && !chosenKinds.Split(',').Contains(kind)) continue;
                context.DefaultRenderingEngine = engine;
                string label = $"caps-synthesis-{engine}-{dpi}-{width}-{row.Name}-{kind}"; var errors = new List<string>();
                try
                {
                    byte[] Capture(FrameworkElement view)
                    {
                        Check(context.Backend == RenderBackend.Metal && context.DefaultRenderingEngine == engine && ReferenceEquals(RenderContext.Current, context), "Requested synthesis backend/engine unavailable");
                        view.SetRootDpi(new(dpi, dpi)); view.Measure(new(width, 240)); view.Arrange(new(0, 0, width, 240)); view.UpdateLayout();
                        var bitmap = MacOSWritingToolsBitmapCapture.Capture(width * dpi, 240 * dpi, dpi, dc => view.Render(dc), () => true) ?? throw new Exception("Synthesis readback failed");
                        Check(context.Backend == RenderBackend.Metal && context.DefaultRenderingEngine == engine && ReferenceEquals(RenderContext.Current, context), "Readback changed synthesis backend/engine");
                        return Pixels(bitmap);
                    }
                    var options = new CapsFixtureOptions(row.Variant, row.Family, row.Source, "", row.Css, Language:row.Language);
                    using var scene = new CapsScene(kind, width, dpi, false, options:options);
                    scene.View.Language = XmlLanguage.GetLanguage(row.Language); scene.Layout(); var normal = Capture(scene.View);
                    scene.Change(true); scene.Layout(); scene.AssertSources(); var changed = Capture(scene.View);
                    // Contextual unicase now uses the CSS strut in ordinary
                    // controls and document projections, including one Run.
                    bool shared = row.Source.Contains('\u03a3') || kind == "split" && row.Source.Length > 4;
                    using var reference = new SynthesisReference(row, width, shared); var expected = Capture(reference);
                    int difference = changed.Zip(expected).Count(p => p.First != p.Second);
                    Check(difference == 0, $"Synthesis differs from explicit casing/font-size reference ({difference} bytes)");
                    Check(!normal.SequenceEqual(changed) == row.Changes, "Synthesis visible change differs from fixture");
                    if (kind == "writing-tools")
                    {
                        var editor = (RichTextBox)scene.View;
                        var source = ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
                        bool deletion = row.Name == "turkic-deletion";
                        int sourceStart = deletion ? 0 : 1, sourceLength = deletion ? 2 : 1;
                        uint paintStart = deletion ? 0u : 1u, paintLength = deletion ? 1u : 2u;
                        try
                        {
                            MacOSWritingToolsPresentation.Prepare(editor, source, "caps-synthesis", sourceStart, sourceLength, 0, () => true);
                            var hidden = Capture(editor); reference.Ranges = [paintStart, paintLength]; reference.GlyphVisibility = 2; reference.InvalidateVisual();
                            Check(hidden.SequenceEqual(Capture(reference)), "Source mask did not cover the mapped paint interval");
                            var preview = MacOSWritingToolsPresentation.CreatePreview(editor, source, sourceStart, sourceLength, Matrix.Identity, dpi, null, () => true);
                            reference.GlyphVisibility = 1; reference.InvalidateVisual();
                            Check(preview is not null && Canvas(preview, width * dpi, 240 * dpi, dpi).SequenceEqual(Capture(reference)), "Source preview did not preserve the complete mapped glyph interval");
                            Save(hidden, width * dpi, 240 * dpi, label + "-hidden.png");
                        }
                        finally { MacOSWritingToolsPresentation.Clear(editor); }
                        Check(Capture(editor).SequenceEqual(changed), "Synthesis source mask leaked");
                    }
                    if (row.Name == "small" && kind == "text") CheckOrdinaryInline();
                    void CheckOrdinaryInline()
                    {
                        string inlineLabel = $"caps-synthesis-{engine}-{dpi}-{width}-ordinary-inline-context";
                        var inlineErrors = new List<string>();
                        try
                        {
                            var text = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)) };
                            var first = new Run("Ο"); var last = new Run("Σ"); text.Inlines.Add(first); text.Inlines.Add(last);
                            var reference = new TextBlock { Text = "Ος", Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x63, 0x42)) };
                            const string style = "font-family:Seravek;font-size:32px;line-height:44px;";
                            var firstParent = first.Parent; var lastParent = last.Parent;
                            Css.SetStyle(text, style); Css.SetStyle(reference, style);
                            var normalInline = Capture(text);
                            Css.SetStyle(last, "font-variant-caps:unicase;font-feature-settings:'smcp' off");
                            var changedInline = Capture(text); var legacyReference = Capture(reference);
                            File.WriteAllText(Path.Combine(Output, inlineLabel + "-metrics.json"), JsonSerializer.Serialize(new {
                                actual = CapsMetrics(text), reference = CapsMetrics(reference),
                                font = TextMeasurement.GetFontMetrics("Seravek", 32, 400, 0),
                                ordinarySourceBaseline = CapsBaselineReference.Baseline("Seravek", "ΟΣ") }));
                            // The declared CSS strut anchors both controls' logical
                            // baselines. The legacy direct CoreText paint path has
                            // a separate fractional placement offset, recorded here.
                            using var strutReference = new SynthesisReference(new("inline-strut", "normal", "ΟΣ", [new("Ος", 32)], "Seravek"), width, true);
                            var expectedInline = Capture(strutReference);
                            Save(legacyReference, width * dpi, 240 * dpi, inlineLabel + "-legacy-reference.png");
                            Require(changedInline.SequenceEqual(expectedInline), "Ordinary inline caps differs from independent whole-text reference");
                            Require(!changedInline.SequenceEqual(normalInline), "Ordinary inline caps did not change its source capital");
                            text.SelectAll(); Require(text.SelectedText == "ΟΣ" && first.Text == "Ο" && last.Text == "Σ" &&
                                ReferenceEquals(first.Parent, firstParent) && ReferenceEquals(last.Parent, lastParent) &&
                                ReferenceEquals(text.Inlines[0], first) && ReferenceEquals(text.Inlines[1], last), "Ordinary inline source ownership changed");
                            text.Select(0, 0);
                            Css.SetStyle(last, "font-variant-caps:normal");
                            var restoredInline = Capture(text); Require(restoredInline.SequenceEqual(normalInline), "Ordinary inline caps reset retained a shape");
                            foreach (var frame in new[] { ("normal", normalInline), ("changed", changedInline), ("expected", expectedInline), ("restored", restoredInline) })
                                Save(frame.Item2, width * dpi, 240 * dpi, inlineLabel + "-" + frame.Item1 + ".png");
                            void Require(bool condition, string message) { if (!condition) inlineErrors.Add(message); }
                        }
                        catch (Exception error) { inlineErrors.Add(error.ToString()); }
                        records.Add(new { label = inlineLabel, backend = context.Backend.ToString(), engine = engine.ToString(), errors = inlineErrors });
                        if (inlineErrors.Count > 0) failed++;
                        Console.WriteLine((inlineErrors.Count == 0 ? "PASS " : "FAIL ") + inlineLabel + " " + string.Join(';', inlineErrors));
                    }
                    var metrics = CapsMetrics(scene.View);
                    scene.Change(false); scene.Layout(); scene.AssertSources(); var restored = Capture(scene.View);
                    Check(restored.SequenceEqual(normal), "Synthesis reset retained old text or geometry");
                    foreach (var item in new[] { ("normal", normal), ("changed", changed), ("expected", expected), ("restored", restored) })
                        Save(item.Item2, width * dpi, 240 * dpi, label + "-" + item.Item1 + ".png");
                    records.Add(new { label, backend = context.Backend.ToString(), engine = context.DefaultRenderingEngine.ToString(), errors, differentBytes = difference, metrics });
                }
                catch (Exception error) { errors.Add(error.ToString()); records.Add(new { label, errors }); }
                if (errors.Count > 0) failed++;
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join(';', errors));
                void Check(bool value, string message) { if (!value) errors.Add(message); }
            }
            File.WriteAllText(Path.Combine(Output, "caps-synthesis.json"), JsonSerializer.Serialize(new { count = records.Count, failed, records,
                nativeLibraries = NativeLibraries(), physicalInput = false, voiceOver = false }));
            return failed == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }
}
