using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;
using ObjCRuntime;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class WindowFontChecks
{
    private const string Sample = "MMMM iii1 abc אבג xyz tail";
    private const string MonoPath = "/System/Library/Fonts/Supplemental/Andale Mono.ttf";
    private const int CheckCount = 34;
    private static readonly string[] WeightFaces = ["AvenirNext-UltraLight", "AvenirNext-UltraLight", "AvenirNext-UltraLight",
        "AvenirNext-Regular", "AvenirNext-Medium", "AvenirNext-DemiBold", "AvenirNext-Bold", "AvenirNext-Heavy", "AvenirNext-Heavy"];
    internal static int RunAll()
    {
        int failed = 0;
        for (int index = 0; index < CheckCount; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.ArgumentList.Add($"--window-font-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000)) { process.Kill(); failed++; }
            else if (process.ExitCode != 0) failed++;
        }
        Console.WriteLine($"macOS Window font host checks: {CheckCount - failed}/{CheckCount} passed");
        return failed == 0 ? 0 : 1;
    }
    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-font-case=".Length), out int index) || (uint)index >= CheckCount) return 2;
        JaliumMacApplication.Initialize(); NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        try
        {
            switch (index)
            {
                case 0: CheckStackPixels(context); break;
                case 1: CheckSystemFont(context); break;
                case 2: CheckTextBox(context); break;
                case 3: CheckEditor(context); break;
                case 4: CheckRichCss(context); break;
                case 5: CheckWeightPixels(context); break;
                case 6: CheckLiveWeights(context); break;
                case 7: CheckPrivateWeightPixels(context); break;
                case 8: CheckCascadePixels(context); break;
                case 9: CheckLiveCascades(context); break;
                case 10: CheckCssSubsetPixels(context); break;
                case 11: CheckLiveCssSubset(context); break;
                case 12: CheckWidthPixels(context); break;
                case 13: CheckLiveWidths(context); break;
                case 14: CheckMatchingPixels(context); break;
                case 15: CheckLiveMatching(context); break;
                case 16: CheckRichUndoPixels(context, false); break;
                case 17: CheckRichUndoPixels(context, true); break;
                case 18: CheckRichUndoPixels(context, false, grouped: true); break;
                case 19: CheckRichUndoPixels(context, true, grouped: true); break;
                case 20: CheckRichUndoPixels(context, false, wholeRun: true); break;
                case 21: CheckRichUndoPixels(context, true, wholeRun: true); break;
                case 22: CheckRichRangeUndoPixels(context, false, multiline: true); break;
                case 23: CheckRichRangeUndoPixels(context, true, multiline: true); break;
                case 24: CheckRichRangeUndoPixels(context, false, multiline: false); break;
                case 25: CheckRichRangeUndoPixels(context, true, multiline: false); break;
                case 26: CheckRichRecordedPixels(context, false, retainedFrame: false); break;
                case 27: CheckRichRecordedPixels(context, true, retainedFrame: false); break;
                case 28: CheckRichRecordedPixels(context, false, retainedFrame: true); break;
                case 29: CheckRichRecordedPixels(context, true, retainedFrame: true); break;
                case 30: CheckTextBoxSelectionPixels(context, "SF Pro", FontWeights.Bold, FontStyles.Oblique, FontStretches.Condensed); break;
                case 31: CheckTextBoxSelectionPixels(context, "SF Pro", FontWeights.Bold, FontStyles.Normal, FontStretches.Normal); break;
                case 32: CheckTextBoxSelectionPixels(context, "Helvetica Neue", FontWeights.Normal, FontStyles.Italic, FontStretches.Normal); break;
                case 33: CheckTextBoxSelectionPixels(context, "SF Pro", FontWeights.Bold, FontStyles.Oblique, FontStretches.Normal, 90.25); break;
            }
            Console.WriteLine($"PASS: native Window font case {index}"); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine($"FAIL: native Window font case {index}: {error}"); return 1; }
    }

    private static void CheckTextBoxSelectionPixels(RenderContext context, string family, FontWeight weight,
        FontStyle style, FontStretch stretch, double? cssWidth = null)
    {
        const string first = "窄斜体 · Miii 中文🙂 e\u0301";
        const string second = "变量字体字宽和倾斜应保留";
        var box = new TextBox
        {
            Text = first + "\n" + second, FontFamily = new FontFamily(family), FontSize = 21,
            Padding = new Thickness(12), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            Foreground = Brushes.Black, Background = Brushes.White, SelectionBrush = Brushes.Blue, SelectionOpacity = 1,
            VerticalContentAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, IsReadOnly = true,
            IsInactiveSelectionHighlightEnabled = true,
        };
        var window = NewWindow(box);
        try
        {
            window.Show(); window.UpdateLayout();
            int phase = 0;
            foreach (bool styled in new[] { false, true, false })
            {
                phase++;
                box.FontWeight = styled ? weight : FontWeights.Normal;
                box.FontStyle = styled ? style : FontStyles.Normal;
                box.FontStretch = styled ? stretch : FontStretches.Normal;
                if (cssWidth.HasValue)
                {
                    box.ClearValue(Control.FontStretchProperty);
                    Css.SetStyle(box, $"font-width:{(styled ? cssWidth.Value : 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}%");
                }
                box.Measure(new Size(360, 96)); box.Arrange(new Rect(0, 0, 360, 96));
                string source = box.FontFamily.GetRenderingSource(box);
                int nativeWeight = box.FontWeight.ToOpenTypeWeight(), nativeStyle = box.FontStyle.ToOpenTypeStyle();
                double lineHeight = Math.Round(TextMeasurement.GetFontMetrics(source, box.FontSize, nativeWeight, nativeStyle).LineHeight);
                using var firstLayout = NativeTextParagraph.TryCreate([new(first, source, 21, nativeWeight, nativeStyle, Colors.Black)],
                    source, 21, 336, lineHeight, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)!;
                using var secondLayout = NativeTextParagraph.TryCreate([new(second, source, 21, nativeWeight, nativeStyle, Colors.Black)],
                    source, 21, 336, lineHeight, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)!;
                Require(firstLayout is not null && secondLayout is not null, "Selection font shaping unavailable");
                var ranges = new (string Name, int Start, int Length)[]
                {
                    ("line", 0, first.Length), ("latin", first.IndexOf("Miii", StringComparison.Ordinal), 4),
                    ("emoji", first.IndexOf("🙂", StringComparison.Ordinal), 2), ("combining", first.Length - 2, 2),
                    ("second-line", first.Length + 1 + 2, 5), ("multiline", first.Length - 2, 6),
                };
                foreach (var wrapping in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
                {
                    box.TextWrapping = wrapping;
                    box.Measure(new Size(360, 96)); box.Arrange(new Rect(0, 0, 360, 96));
                    foreach (int dpi in new[] { 1, 2 })
                    {
                        using var surface = new FontSurface(context, dpi);
                        foreach (var range in ranges)
                        {
                            box.Select(range.Start, range.Length);
                            int selectionEnd = range.Start + range.Length;
                            bool onSecondLine = selectionEnd > first.Length;
                            int caretColumn = onSecondLine ? selectionEnd - first.Length - 1 : selectionEnd;
                            double expectedCaret = Math.Round(12 + (onSecondLine ? secondLayout : firstLayout).Caret(0, caretColumn, false).X);
                            Require(Math.Abs(((IImeSupport)box).GetImeCaretRectangle().X - expectedCaret) < .001,
                                "Selection endpoint and IME caret use different fonts");
                            byte[] Reference() => surface.Capture(() =>
                            {
                                var drawing = new DrawingRecorder(); drawing.BindWholeFrame();
                                drawing.PushClip(new RectangleGeometry(new Rect(12, 12, 336, 72)));
                                var lines = new[] { first, second }; var layouts = new[] { firstLayout, secondLayout };
                                int offset = 0, end = range.Start + range.Length;
                                for (int row = 0; row < lines.Length; row++)
                                {
                                    int startInLine = Math.Clamp(range.Start - offset, 0, lines[row].Length);
                                    int endInLine = Math.Clamp(end - offset, 0, lines[row].Length);
                                    double y = 12 + row * lineHeight;
                                    if (endInLine > startInLine)
                                    {
                                        double left = layouts[row].Caret(0, startInLine, false).X;
                                        double right = layouts[row].Caret(0, endInLine, false).X;
                                        drawing.DrawRectangle(Brushes.Blue, null,
                                            new Rect(Math.Round(12 + left), y, Math.Max(Math.Round(right - left), 1), lineHeight));
                                    }
                                    if (row == 0 && range.Start <= lines[row].Length && end > lines[row].Length)
                                        drawing.DrawRectangle(Brushes.Blue, null,
                                            new Rect(Math.Round(12 + layouts[row].Caret(0, lines[row].Length, false).X), y, Math.Round(21 * .3), lineHeight));
                                    offset += lines[row].Length + 1;
                                }
                                for (int row = 0; row < lines.Length; row++)
                                    drawing.DrawText(new FormattedText(lines[row], source, 21)
                                    {
                                        FontWeight = nativeWeight, FontStyle = nativeStyle, Foreground = Brushes.Black,
                                        MaxTextWidth = wrapping == TextWrapping.NoWrap ? double.MaxValue : 336, MaxTextHeight = lineHeight,
                                    }, new Point(12, 12 + row * lineHeight));
                                drawing.Pop(); DrawingReplayer.Replay(drawing.Commit(), surface.Drawing);
                            });
                            var expected = Reference(); var actual = surface.Capture(() => box.Render(surface.Drawing));
                            string capture = $"textbox-selection-{family.Replace(' ', '-')}-{nativeWeight}-{nativeStyle}-{stretch.ToOpenTypeStretch()}-{cssWidth ?? 100}-phase{phase}-{wrapping}-{range.Name}";
                            Save(capture + "-reference", dpi, expected); Save(capture + "-actual", dpi, actual);
                            Console.WriteLine($"TextBox {family} {nativeWeight}/{nativeStyle} phase{phase} {wrapping} {range.Name} {dpi}x: selection pixels match={actual.SequenceEqual(expected)}, end={firstLayout.Caret(0, first.Length, false).X:F3}");
                            Require(actual.SequenceEqual(expected), $"TextBox {family} {nativeWeight}/{nativeStyle} {range.Name} at {dpi}x: rendered selection differs from its shaped font");
                        }
                    }
                }
            }
        }
        finally { Css.SetStyle(box, null); DrawingObjectPool.Clear(); window.Close(); }
    }

    private static void CheckRichRecordedPixels(RenderContext context, bool privateFont, bool retainedFrame)
    {
        using var resource = privateFont
            ? CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc")) : null;
        Require(!privateFont || resource is not null, "Recorded private font collection unavailable");
        if (retainedFrame)
        {
            CheckRetainedParagraphPixels(context, resource?.Family ?? "Helvetica Neue", privateFont);
            return;
        }
        var run = new Run(Sample) { FontFamily = new FontFamily(resource?.Family ?? "Helvetica Neue"),
            FontSize = 20, FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, Foreground = Brushes.Black };
        var box = new RichTextBox(new FlowDocument(new Paragraph(run) { Margin = new Thickness(0) }))
        {
            Padding = new Thickness(12), BorderThickness = new Thickness(0), Background = Brushes.White,
        };
        var window = NewWindow(box);
        try
        {
            window.Show();
            foreach (int dpi in new[] { 1, 2 })
            {
                DrawingObjectPool.Clear();
                using var surface = new FontSurface(context, dpi);
                box.Measure(new Size(360, 96)); box.Arrange(new Rect(0, 0, 360, 96));
                run.Foreground = Brushes.Black;
                byte[] Reference() => surface.Capture(() =>
                {
                    var content = new Rect(12, 12, 336, 72);
                    surface.Drawing.PushClip(new RectangleGeometry(content));
                    try
                    {
                        typeof(RichTextBox).GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(box, [surface.Drawing, content]);
                    }
                    finally { surface.Drawing.Pop(); }
                });
                var expected = Reference(); Require(InkPixels(expected) > 100, "Recorded reference glyphs missing");
                var initial = surface.Capture(() => box.Render(surface.Drawing));
                Save($"rich-recorded-{(privateFont ? "private" : "installed")}-reference", dpi, expected);
                Save($"rich-recorded-{(privateFont ? "private" : "installed")}-before", dpi, initial);
                Console.WriteLine($"Recorded rich {dpi}x: reference ink={InkPixels(expected)}, Visual.Render ink={InkPixels(initial)}");
                Require(initial.SequenceEqual(expected), "Actual Visual.Render differs from shaped document");

                var original = NativeParagraph(box);
                run.Foreground = Brushes.Red;
                var changedReference = Reference();
                Require(original.IsDisposed, "Rebuilt layout did not dispose its former owner");
                Require(!expected.SequenceEqual(changedReference), "Run color mutation did not reach shaping");
                var refreshed = surface.Capture(() => box.Render(surface.Drawing));
                Save($"rich-recorded-{(privateFont ? "private" : "installed")}-after", dpi, refreshed);
                Require(refreshed.SequenceEqual(changedReference), "Actual Visual.Render reused an obsolete shaped paragraph");
                var clean = surface.Capture(() => box.Render(surface.Drawing));
                Require(clean.SequenceEqual(refreshed), "Clean cached frame changed rich glyphs");
            }
        }
        finally { DrawingObjectPool.Clear(); window.Close(); }
    }

    private static void CheckRetainedParagraphPixels(RenderContext context, string family, bool privateFont)
    {
        foreach (int dpi in new[] { 1, 2 })
        {
            DrawingObjectPool.Clear();
            using var surface = new FontSurface(context, dpi);
            using var paragraph = NativeTextParagraph.TryCreate(
                [new(Sample, family, 20, 700, 1, Colors.Black)], family, 20, 336, 24,
                TextAlignment.Left, FlowDirection.LeftToRight)
                ?? throw new InvalidOperationException("Retained paragraph unavailable");
            var expected = surface.Capture(() => paragraph.DrawLine(surface.Target.Handle, 0, 12, 12));
            Require(InkPixels(expected) > 100, "Retained reference glyphs missing");
            var recorder = new DrawingRecorder(); recorder.BindWholeFrame();
            recorder.DrawText(new FormattedText(Sample, family, 20)
            {
                PlatformTextLine = paragraph.Lines[0], Foreground = Brushes.Black,
                MaxTextWidth = 336, MaxTextHeight = 72,
            }, new Point(12, 12));
            var recorded = recorder.Commit();
            var before = surface.Capture(() => DrawingReplayer.Replay(recorded, surface.Drawing));
            Require(before.SequenceEqual(expected), "Fresh retained shaping differs");
            Save($"rich-retained-{(privateFont ? "private" : "installed")}-before", dpi, before);
            paragraph.Dispose(); Require(paragraph.IsDisposed, "Layout owner remained open");
            bool rejected = false;
            try { paragraph.Caret(0, 0, false); }
            catch (ObjectDisposedException) { rejected = true; }
            Require(rejected, "Retained drawing reopened the disposed layout API");
            var retained = surface.Capture(() => DrawingReplayer.Replay(recorded, surface.Drawing));
            Save($"rich-retained-{(privateFont ? "private" : "installed")}-after-dispose", dpi, retained);
            Require(retained.SequenceEqual(before), "Recorded frame lost its immutable paragraph after layout disposal");
            var worker = Task.Run(() => surface.Capture(() => DrawingReplayer.Replay(recorded, surface.Drawing))).GetAwaiter().GetResult();
            Require(worker.SequenceEqual(before), "Render worker lost the retained shaped paragraph");
        }
    }

    private static void CheckRichUndoPixels(RenderContext context, bool privateFont, bool grouped = false, bool wholeRun = false)
    {
        using var resource = privateFont
            ? CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc")) : null;
        Require(!privateFont || resource is not null, "Undo private font collection unavailable");
        string family = resource?.Family ?? "Helvetica Neue";
        var run = new Run(wholeRun ? Sample[..4] : Sample) { FontFamily = new FontFamily(family), FontSize = 20,
            FontWeight = FontWeights.Black, FontStyle = FontStyles.Italic, Foreground = Brushes.Black };
        Css.SetStyle(run, "font-width:100%");
        var paragraph = new Paragraph(run) { Margin = new Thickness(0) };
        if (wholeRun)
        {
            var tail = new Run(Sample[4..]) { FontFamily = run.FontFamily, FontSize = run.FontSize,
                FontWeight = run.FontWeight, FontStyle = run.FontStyle, Foreground = run.Foreground };
            Css.SetStyle(tail, "font-width:100%"); paragraph.Inlines.Add(tail);
        }
        var document = new FlowDocument(paragraph);
        var box = new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        Jalium.UI.Automation.AutomationProperties.SetAutomationId(box, "rich-window-editor");
        var window = NewWindow(box);
        try
        {
            window.Show(); window.UpdateLayout(); Require(box.Focus(), "Rich undo focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var support = (IImeSupport)box;
            NSAccessibilityElement? ax = null;
            if (wholeRun)
            {
                var root = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
                ax = RichAccessibilityElements(root).Single(element => element.AccessibilityIdentifier == "rich-window-editor");
                Require(ax.AccessibilityRole == "AXTextArea", "Rich editor native AX role differs");
            }
            var contentEvents = new List<(UndoAction Action, string Text, string Selection, int Caret)>();
            box.TextChanged += (_, args) =>
            {
                contentEvents.Add((args.UndoAction, document.GetText(), box.Selection.Text, box.CaretPosition!.DocumentOffset));
                if (ax is not null)
                    Require(ax.AccessibilityValue?.ToString() == document.GetText() &&
                        ax.AccessibilitySelectedText == box.Selection.Text,
                        "Rich content notification exposed stale native AX text or selection");
            };
            foreach (int dpi in new[] { 1, 2 })
            {
                using var surface = new FontSurface(context, dpi);
                byte[] Capture() => surface.Capture(() => typeof(RichTextBox)
                    .GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(box, [surface.Drawing, new Rect(12, 12, 340, 72)]));
                Require(support.TrySetImeSelection(0, 0), "Rich undo initial anchor rejected");
                Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
                Require(box.Selection.Text == "MMMM", "Rich undo native word selection differs");
                Rect initialCaret = support.GetImeCaretRectangle();
                int eventStart = contentEvents.Count;
                if (ax is not null)
                    Require(ax.AccessibilityValue?.ToString() == Sample + Environment.NewLine &&
                        ax.AccessibilityNumberOfCharacters == document.GetText().Length &&
                        ax.AccessibilitySelectedText == "MMMM" && ax.GetAccessibilityString(new NSRange(0, 4)) == "MMMM",
                        "Rich native AX text, UTF-16 count or selection differs");
                byte[] initial = Capture(); Require(InkPixels(initial) > 100, "Rich undo initial glyphs missing");
                if (grouped)
                {
                    using (box.DeclareChangeBlock())
                    {
                        Require(support.TryReplaceImeText(0, 4, "Window"), "Grouped rich undo replacement rejected");
                        using (box.DeclareChangeBlock())
                        {
                            run.FontSize = 24;
                            run.FontStyle = FontStyles.Normal;
                            Require(support.TryReplaceImeText(6, 0, "++"), "Grouped rich undo second edit rejected");
                        }
                    }
                }
                else Require(support.TryReplaceImeText(0, 4, "Window"), "Rich undo replacement rejected");
                window.UpdateLayout(); Rect replacedCaret = support.GetImeCaretRectangle();
                byte[] replaced = Capture(); Require(!initial.SequenceEqual(replaced), "Rich undo replacement did not change glyphs");
                Require(contentEvents.Count == eventStart + 1 && contentEvents[^1].Action == UndoAction.Create &&
                    contentEvents[^1].Selection == "" && contentEvents[^1].Caret == (grouped ? 8 : 6),
                    "Rich edit did not publish one stable content notification");
                string prefix = $"{(wholeRun ? "rich-whole-run-undo" : grouped ? "rich-group-undo" : "rich-undo")}-{(privateFont ? "private" : "installed")}";
                Save(prefix + "-before", dpi, initial); Save(prefix + "-replaced", dpi, replaced);
                for (int repetition = 0; repetition < 3; repetition++)
                {
                    Require(NSApplication.SharedApplication.SendAction(new Selector("undo:"), view, null), "AppKit rejected rich undo action");
                    window.UpdateLayout();
                    Require(ReferenceEquals(box.Document, document) && ReferenceEquals(document.Parent, box) &&
                        ReferenceEquals(paragraph.Inlines[0], run) && run.Text == (wholeRun ? Sample[..4] : Sample), "Rich undo lost document node ownership");
                    Require(box.Selection.Text == "MMMM" && support.GetImeCaretRectangle() == initialCaret,
                        "Rich undo lost the original native word selection or caret geometry");
                    byte[] undone = Capture(); Require(initial.SequenceEqual(undone), "Rich undo lost formatted glyph pixels");
                    Require(contentEvents.Count == eventStart + 2 + repetition * 2 &&
                        contentEvents[^1].Action == UndoAction.Undo && contentEvents[^1].Selection == "MMMM" &&
                        contentEvents[^1].Caret == 4, "Native undo content notification differs");
                    if (repetition == 0) Save(prefix + "-undone", dpi, undone);
                    Require(NSApplication.SharedApplication.SendAction(new Selector("redo:"), view, null), "AppKit rejected rich redo action");
                    window.UpdateLayout();
                    Require(run.Text == (grouped ? "Window++" : "Window") + (wholeRun ? "" : Sample[4..]) && support.GetImeCaretRectangle() == replacedCaret,
                        "Rich redo lost replacement text or caret geometry");
                    byte[] redone = Capture(); Require(replaced.SequenceEqual(redone), "Rich redo lost formatted glyph pixels");
                    Require(contentEvents.Count == eventStart + 3 + repetition * 2 &&
                        contentEvents[^1].Action == UndoAction.Redo && contentEvents[^1].Selection == "",
                        "Native redo content notification differs");
                    if (repetition == 0) Save(prefix + "-redone", dpi, redone);
                }
                Require(box.Undo(), "Rich undo final restore rejected");
            }
            // The lab's font buttons retain this Run reference. It must still
            // control the visible document after the native undo/redo actions.
            run.FontWeight = FontWeights.Normal;
            window.UpdateLayout(); Require(ReferenceEquals(paragraph.Inlines[0], run), "Rich undo detached the external Run reference");
            if (ax is not null)
            {
                box.Visibility = Visibility.Collapsed;
                string? hiddenValue = ax.AccessibilityValue?.ToString();
                nint hiddenCount = ax.AccessibilityNumberOfCharacters;
                bool hiddenSelection = ax.IsAccessibilitySelectorAllowed(new Selector("accessibilitySelectedTextRange"));
                Require(string.IsNullOrEmpty(hiddenValue) && hiddenCount == 0 && !hiddenSelection &&
                    ax.GetAccessibilityString(new NSRange(0, 1)) is null,
                    $"Hidden cached rich editor exposes native AX content: value={hiddenValue ?? "<nil>"}, count={hiddenCount}, selection={hiddenSelection}");
                box.Visibility = Visibility.Visible;
                Require(ax.AccessibilityValue?.ToString() == document.GetText(), "Reused rich editor lost native AX content");
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<NSAccessibilityElement> RichAccessibilityElements(NSAccessibilityElement root)
    {
        yield return root;
        foreach (var child in (root.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>())
            foreach (var element in RichAccessibilityElements(child)) yield return element;
    }

    private static void CheckRichRangeUndoPixels(RenderContext context, bool privateFont, bool multiline)
    {
        using var resource = privateFont
            ? CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc")) : null;
        Require(!privateFont || resource is not null, "Range editing private font collection unavailable");
        string family = resource?.Family ?? "Helvetica Neue";
        var source = new Run(multiline ? "alphaOMEGA" : "alpha") { FontSize = 20 };
        var nested = new Bold(new Italic(source));
        var first = new Paragraph(nested) { Margin = new Thickness(0), FontFamily = family, Foreground = Brushes.Black };
        var document = new FlowDocument(first);
        var middle = new Paragraph(new Run("middle")) { Margin = new Thickness(0), FontFamily = family, FontSize = 14, Foreground = Brushes.Black };
        var suffix = new Run("OMEGA"); var suffixStyle = new Italic(suffix);
        var last = new Paragraph(suffixStyle) { Margin = new Thickness(0), FontFamily = family, FontSize = 24, Foreground = Brushes.Black };
        if (!multiline) { document.Blocks.Add(middle); document.Blocks.Add(last); }
        string originalText = document.GetText();
        int selectionStart = multiline ? 5 : 3, selectionLength = multiline ? 2 : 12;
        string expected = multiline ? "alphaA\nBEGA\n" : "alpWindowEGA\n";
        var box = new RichTextBox(document) { Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        Jalium.UI.Automation.AutomationProperties.SetAutomationId(box, "rich-range-editor");
        var window = NewWindow(box);
        try
        {
            window.Show(); window.UpdateLayout(); Require(box.Focus(), "Rich range focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            var root = (view.AccessibilityChildren ?? []).OfType<NSAccessibilityElement>().Single();
            var ax = RichAccessibilityElements(root).Single(element => element.AccessibilityIdentifier == "rich-range-editor");
            var support = (IImeSupport)box;
            var events = new List<UndoAction>();
            box.TextChanged += (_, args) =>
            {
                events.Add(args.UndoAction);
                Require(ax.AccessibilityValue?.ToString() == document.GetText() &&
                    ax.AccessibilitySelectedText == box.Selection.Text &&
                    ax.AccessibilityNumberOfCharacters == document.GetText().Length,
                    "Native AX range editing notification exposed an intermediate document");
            };
            foreach (int dpi in new[] { 1, 2 })
            {
                using var surface = new FontSurface(context, dpi);
                byte[] Capture() => surface.Capture(() => typeof(RichTextBox)
                    .GetMethod("RenderDocument", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(box, [surface.Drawing, new Rect(12, 12, 340, 72)]));
                Require(support.TrySetImeSelection(selectionStart, selectionLength), "Rich range selection rejected");
                string originalSelection = box.Selection.Text;
                Rect beforeCaret = support.GetImeCaretRectangle();
                byte[] before = Capture(); Require(InkPixels(before) > 100, "Rich range initial glyphs missing");
                int eventStart = events.Count;
                Require(support.TryReplaceImeText(selectionStart, selectionLength, multiline ? "A\r\nB" : "Window"),
                    "Rich range replacement rejected");
                window.UpdateLayout();
                Require(document.GetText() == expected && box.Selection.IsEmpty &&
                    box.CaretPosition!.DocumentOffset == selectionStart + (multiline ? 3 : 6),
                    "Rich range replacement or normalized caret differs");
                Require(events.Count == eventStart + 1 && events[^1] == UndoAction.Create,
                    "Rich range edit did not create one content event");
                if (multiline) Require(document.Blocks.Count == 2, "Multiline input was stored inside a Run");
                else Require(document.Blocks.Count == 1 && suffix.GetEffectiveFontSize() == 24 &&
                    suffix.GetEffectiveFontStyle() == FontStyles.Italic, "Cross-paragraph edit changed suffix formatting");
                Rect afterCaret = support.GetImeCaretRectangle();
                byte[] after = Capture(); Require(!before.SequenceEqual(after), "Rich range glyphs did not change");
                string prefix = $"rich-{(multiline ? "multiline" : "cross-paragraph")}-undo-{(privateFont ? "private" : "installed")}";
                Save(prefix + "-before", dpi, before); Save(prefix + "-replaced", dpi, after);
                for (int repetition = 0; repetition < 3; repetition++)
                {
                    Require(NSApplication.SharedApplication.SendAction(new Selector("undo:"), view, null), "AppKit rejected rich range undo");
                    window.UpdateLayout();
                    Require(document.GetText() == originalText && ReferenceEquals(first.Inlines[0], nested) &&
                        ReferenceEquals(((Italic)nested.Inlines[0]).Inlines[0], source), "Rich range undo lost original inline identities");
                    if (!multiline) Require(ReferenceEquals(document.Blocks[1], middle) && ReferenceEquals(document.Blocks[2], last) &&
                        ReferenceEquals(last.Inlines[0], suffixStyle) && ReferenceEquals(suffixStyle.Inlines[0], suffix),
                        "Rich range undo lost the original paragraph or suffix owner");
                    Require(box.Selection.Text == originalSelection && support.GetImeCaretRectangle() == beforeCaret,
                        "Rich range undo lost selection or caret geometry");
                    byte[] undone = Capture(); Require(before.SequenceEqual(undone), "Rich range undo lost formatted glyph pixels");
                    Require(events.Count == eventStart + 2 + 2 * repetition && events[^1] == UndoAction.Undo,
                        "Rich range native undo notification differs");
                    if (repetition == 0) Save(prefix + "-undone", dpi, undone);
                    Require(NSApplication.SharedApplication.SendAction(new Selector("redo:"), view, null), "AppKit rejected rich range redo");
                    window.UpdateLayout();
                    Require(document.GetText() == expected && box.Selection.IsEmpty && support.GetImeCaretRectangle() == afterCaret,
                        "Rich range redo lost text, selection or caret geometry");
                    byte[] redone = Capture(); Require(after.SequenceEqual(redone), "Rich range redo lost formatted glyph pixels");
                    Require(events.Count == eventStart + 3 + 2 * repetition && events[^1] == UndoAction.Redo,
                        "Rich range native redo notification differs");
                    if (repetition == 0) Save(prefix + "-redone", dpi, redone);
                }
                Require(box.Undo(), "Rich range final restoration rejected");
            }
            box.Visibility = Visibility.Collapsed;
            Require(string.IsNullOrEmpty(ax.AccessibilityValue?.ToString()) && ax.AccessibilityNumberOfCharacters == 0 &&
                !ax.IsAccessibilitySelectorAllowed(new Selector("accessibilitySelectedTextRange")) &&
                ax.GetAccessibilityString(new NSRange(0, 1)) is null, "Hidden cached rich range editor still exposes native AX text");
            box.Visibility = Visibility.Visible;
            Require(ax.AccessibilityValue?.ToString() == originalText, "Reused rich range editor lost native AX text");
        }
        finally { window.Close(); }
    }

    private static void CheckMatchingPixels(RenderContext context)
    {
        using var collection = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc"))
            ?? throw new InvalidOperationException("Normal-width collection unavailable");
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi);
            foreach (string family in new[] { "Helvetica Neue", collection.Family })
            foreach ((int weight, string face) in new[] { (199, "HelveticaNeue-UltraLight"), (450, "HelveticaNeue-Medium"),
                (501, "HelveticaNeue-Bold"), (750, "HelveticaNeue-Bold"), (900, "HelveticaNeue-Bold") })
            foreach (int style in new[] { 0, 1, 2 })
            {
                using var reference = context.CreateTextFormat(face, 20, 400, style);
                reference.SetNoWrap(true); reference.SetSubpixelPositioning(true);
                var expected = surface.Capture(() => surface.Target.DrawText(Sample, reference, 12, 12, 340, 48, surface.Ink));
                var actual = surface.Capture(() => DrawWeight(surface, family, weight, style));
                Require(actual.SequenceEqual(expected) && InkPixels(actual) > 100,
                    $"Normal-width matched glyph pixels differ: {family}/{weight}/{style}/{dpi}");
                var label = new TextBlock { FontSize = 20 };
                Css.SetStyle(label, $"font-family:'{family}';font-weight:{weight};font-width:100%;font-style:{(style == 0 ? "normal" : style == 1 ? "italic" : "oblique")}");
                string source = label.FontFamily.GetRenderingSource(label);
                var css = surface.Capture(() => DrawWeight(surface, source, weight, style));
                Require(css.SequenceEqual(expected), $"CSS normal-width matched glyph pixels differ: {family}/{weight}/{style}/{dpi}");
                using var paragraph = NativeTextParagraph.TryCreate([new(Sample, source, 20, weight, style, Colors.Black)],
                    "Helvetica Neue", 20, 340, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
                    ?? throw new InvalidOperationException("Matched paragraph unavailable");
                using var explicitParagraph = NativeTextParagraph.TryCreate([new(Sample, face, 20, 400, style, Colors.Black)],
                    "Helvetica Neue", 20, 340, 48, TextAlignment.Left, FlowDirection.LeftToRight, noWrap: true)
                    ?? throw new InvalidOperationException("Explicit reference paragraph unavailable");
                var paragraphPixels = surface.Capture(() => paragraph.DrawLine(surface.Target.Handle, 0, 12, 12));
                var referencePixels = surface.Capture(() => explicitParagraph.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(paragraphPixels.SequenceEqual(referencePixels),
                    $"CSS matched paragraph glyph pixels differ: {family}/{weight}/{style}/{dpi}");
                if (weight == 900 && style < 2) Save($"matching-{(family == "Helvetica Neue" ? "installed" : "private")}-{(style == 0 ? "upright" : "italic")}", dpi, actual);
                Css.SetStyle(label, string.Empty);
            }
        }
    }

    private static void CheckLiveMatching(RenderContext context)
    {
        using var collection = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/HelveticaNeue.ttc"))
            ?? throw new InvalidOperationException("Live normal-width collection unavailable");
        foreach (string family in new[] { "Helvetica Neue", collection.Family })
        for (int kind = 0; kind < 3; kind++)
        {
            var run = new Run(Sample) { FontFamily = new FontFamily(family), FontSize = 20 };
            var document = new FlowDocument { FontFamily = family, FontSize = 20 };
            document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
            Control control = kind switch { 0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
                1 => new EditControl { Text = Sample, ShowLineNumbers = false }, _ => new RichTextBox(document) };
            control.FontFamily = new FontFamily(family); control.FontSize = 20;
            control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
            var window = NewWindow(control);
            try
            {
                window.Show(); window.UpdateLayout(); Require(control.Focus(), "Matched editor focus rejected");
                var support = (IImeSupport)control; var view = Runtime.GetNSObject<NSView>(window.Handle)!;
                foreach ((int weight, int style, int width, bool css) in new[] { (400, 0, 100, false), (900, 0, 100, false),
                    (900, 1, 100, true), (900, 0, 75, true), (900, 0, 100, true), (450, 0, 100, true),
                    (501, 1, 125, true), (900, 0, 100, false) })
                {
                    control.FontWeight = FontWeight.FromOpenTypeWeight(weight); run.FontWeight = control.FontWeight;
                    control.FontStyle = FontStyle.FromOpenTypeStyle(style); run.FontStyle = control.FontStyle;
                    Css.SetStyle(control, css ? $"font-width:{width}%" : string.Empty);
                    Css.SetStyle(run, css ? $"font-width:{width}%" : string.Empty);
                    window.UpdateLayout(); Require(support.TrySetImeSelection(0, 0), "Matched start selection rejected");
                    double origin = support.GetImeCaretRectangle().X;
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask);
                    Require(support.TryGetImeSurroundingText(out var text) && text.CursorIndex == Sample.Length,
                        "Matched editor Command-Right failed");
                    string face = width == 75 ? "HelveticaNeue-CondensedBlack" :
                        weight == 400 ? "HelveticaNeue" : weight == 450 ? "HelveticaNeue-Medium" : "HelveticaNeue-Bold";
                    using var reference = context.CreateTextFormat(face, 20, 400, style); reference.SetNoWrap(true);
                    Require(reference.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret),
                        "Matched reference caret unavailable");
                    double expected = kind == 0 ? Math.Round(caret.CaretX) : caret.CaretX;
                    Require(Math.Abs(support.GetImeCaretRectangle().X - origin - expected) < .1,
                        $"Live matched editor retained wrong geometry: {kind}/{family}/{weight}/{style}/{width}/{css}");
                    Require(support.TrySetImeSelection(0, 0), "Matched word anchor rejected");
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
                    Require(support.TryGetImeSurroundingText(out var word) && word.AnchorIndex == 0 && word.CursorIndex == 4,
                        "Matched editor Option-Shift selection failed");
                }
            }
            finally { Css.SetStyle(control, string.Empty); Css.SetStyle(run, string.Empty); window.Close(); }
        }
    }

    private static void CheckWidthPixels(RenderContext context)
    {
        using var variable = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/SFNS.ttf"))
            ?? throw new InvalidOperationException("Private width fixture unavailable");
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi);
            var condensed = surface.Capture(() => surface.Drawing.DrawText(new FormattedText(Sample, "Helvetica Neue", 20)
                { FontWeight = 700, FontStretch = 3, Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
            using var face = context.CreateTextFormat("HelveticaNeue-CondensedBold", 20); face.SetNoWrap(true); face.SetSubpixelPositioning(true);
            var facePixels = surface.Capture(() => surface.Target.DrawText(Sample, face, 12, 12, 340, 48, surface.Ink));
            Require(condensed.SequenceEqual(facePixels), $"FormattedText width differs from real condensed face: {dpi}"); Save("width-condensed-face", dpi, condensed);
            foreach (string family in new[] { "SF Pro", variable.Family })
            {
                var profiles = new List<byte[]>();
                foreach ((int stretch, float width) in new[] { (3, 75f), (5, 100f), (7, 125f) })
                {
                    var actual = surface.Capture(() => surface.Drawing.DrawText(new FormattedText(Sample, family, 20)
                        { FontWeight = 650, FontStretch = stretch, Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
                    using var reference = context.CreateTextFormat(family, 20, 650, 0, width);
                    reference.SetNoWrap(true); reference.SetSubpixelPositioning(true);
                    var expected = surface.Capture(() => surface.Target.DrawText(Sample, reference, 12, 12, 340, 48, surface.Ink));
                    Require(actual.SequenceEqual(expected) && InkPixels(actual) > 100 && !profiles.Any(row => row.SequenceEqual(actual)),
                        $"Variable width GPU selection differs: {family}/{width}/{dpi}"); profiles.Add(actual);
                    Save($"width-{(family == "SF Pro" ? "system" : "private")}-{width}", dpi, actual);
                }
            }
            byte[] DrawStyle(int style) => surface.Capture(() => surface.Drawing.DrawText(new FormattedText(Sample, "SF Pro", 20)
                { FontWeight = 650, FontStyle = style, FontStretch = 3, Foreground = Brushes.Black,
                    MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
            var italic = DrawStyle(1); var upright = DrawStyle(0);
            using var italicReference = context.CreateTextFormat("SF Pro", 20, 650, 1, 75);
            italicReference.SetNoWrap(true); italicReference.SetSubpixelPositioning(true);
            var nativeItalic = surface.Capture(() => surface.Target.DrawText(Sample, italicReference, 12, 12, 340, 48, surface.Ink));
            Require(italic.SequenceEqual(nativeItalic), "Width italic formatted/native rendering differs");
            Require(!italic.SequenceEqual(upright) && InkPixels(italic) > 100, "Width selection lost its oblique glyphs");
            Save("width-system-italic", dpi, italic);
        }
    }

    private static void CheckLiveWidths(RenderContext context)
    {
        for (int kind = 0; kind < 3; kind++)
        {
            var run = new Run(Sample) { FontSize = 20 };
            var document = new FlowDocument { FontFamily = "SF Pro", FontSize = 20 };
            document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
            Control control = kind switch { 0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
                1 => new EditControl { Text = Sample, ShowLineNumbers = false }, _ => new RichTextBox(document) };
            control.FontSize = 20; control.FontFamily = new FontFamily("SF Pro"); control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
            var window = NewWindow(control);
            try
            {
                window.Show(); window.UpdateLayout(); Require(control.Focus(), "Width editor focus rejected");
                var support = (IImeSupport)control;
                foreach (float percentage in new[] { 83.2f, 92.8f, 75f, 100f })
                {
                    string width = percentage.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Css.SetStyle(control, "font-width:" + width + "%"); Css.SetStyle(run, "font-width:" + width + "%");
                    window.UpdateLayout(); Require(support.TrySetImeSelection(0, 0), "Width start caret rejected");
                    double origin = support.GetImeCaretRectangle().X;
                    Require(support.TrySetImeSelection(Sample.Length, 0), "Width end caret rejected");
                    using var reference = context.CreateTextFormat("SF Pro", 20, 400, 0, percentage); reference.SetNoWrap(true);
                    Require(reference.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var caret), "Width reference hit test failed");
                    double expected = kind == 0 ? Math.Round(caret.CaretX) : caret.CaretX;
                    Require(Math.Abs(support.GetImeCaretRectangle().X - origin - expected) < .1, $"Live width retained old IME geometry: {kind}/{width}");
                    support.TrySetImeSelection(0, 0); Send(Runtime.GetNSObject<NSView>(window.Handle)!, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask);
                    Require(support.TryGetImeSurroundingText(out var surrounding) && surrounding.CursorIndex == 4,
                        $"Width changed native word navigation: {kind}/{width}");
                }
            }
            finally { Css.SetStyle(control, string.Empty); Css.SetStyle(run, string.Empty); window.Close(); }
        }
        var label = new TextBlock { Text = Sample, FontSize = 20, FontFamily = new FontFamily("SF Pro") };
        var parent = new StackPanel(); parent.Children.Add(label); Css.SetStyle(label, "width:10ch");
        var rulerWindow = NewWindow(new TextBox()); rulerWindow.Content = parent;
        try
        {
            rulerWindow.Show();
            foreach (float width in new[] { 83.2f, 92.8f, 83.2f })
            {
                Css.SetStyle(parent, "font-width:" + width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
                rulerWindow.UpdateLayout();
                using var font = context.CreateTextFormat("SF Pro", 20, 400, 0, width);
                Require(Math.Abs(label.Width - font.GetFontUnitMetrics().ZeroAdvance * 10) < .01,
                    $"Live inherited ch ruler kept normal width: {width}/{label.Width}");
            }
        }
        finally { Css.SetStyle(parent, string.Empty); Css.SetStyle(label, string.Empty); rulerWindow.Close(); }
    }

    private static void CheckSystemFont(RenderContext context)
    {
        using var system = NSFont.SystemFontOfSize(20) ?? throw new InvalidOperationException("AppKit system font unavailable");
        using var attributed = new NSAttributedString(Sample, new NSStringAttributes { Font = system });
        var expected = attributed.Size;
        foreach (string name in new[] { FrameworkElement.DefaultFontFamilyName, "system-ui", "sans-serif" })
        {
            using var font = TextMeasurement.CreateTextFormatFromFamilyList(context, name, 20, 400, 0);
            var actual = font.MeasureText(Sample, 100000, 1000);
            Require(Math.Abs(actual.Width - expected.Width) < .001 && Math.Abs(actual.Ascent - system.Ascender) < .001,
                $"System font mismatch: {name}, actual={actual.Width}/{actual.Ascent}, AppKit={expected.Width}/{system.Ascender}");
        }
        Require(NativeMethods.FontFamilyIsAvailable("__MissingWindowFont40__") == 0 && NativeMethods.FontGetSystemFamilyCount() > 20,
            "AppKit host did not load current native font catalog");
    }

    private const string CascadeSample = "abc אבג tail";
    private static NativeTextParagraph CascadeReference(string hebrew) => NativeTextParagraph.TryCreate(
        [new("abc ", "Avenir Next", 20, 400, 0, Colors.Black),
         new("אבג", hebrew, 20, 400, 0, Colors.Black),
         new(" tail", "Avenir Next", 20, 400, 0, Colors.Black)],
        "Avenir Next", 20, 340, 30, TextAlignment.Left, FlowDirection.LeftToRight)
        ?? throw new InvalidOperationException("Explicit cascade reference unavailable");

    private static void CheckCascadePixels(RenderContext context)
    {
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi);
            foreach (string hebrew in new[] { "Arial Hebrew", "New Peninim MT" })
            {
                string next = hebrew == "Arial Hebrew" ? "New Peninim MT" : "Arial Hebrew";
                string family = $"Avenir Next, {hebrew}, {next}";
                using var reference = CascadeReference(hebrew);
                using var paragraph = NativeTextParagraph.TryCreate([new(CascadeSample, family, 20, 400, 0, Colors.Black)],
                    family, 20, 340, 30, TextAlignment.Left, FlowDirection.LeftToRight)
                    ?? throw new InvalidOperationException("Ordered cascade paragraph unavailable");
                var expected = surface.Capture(() => reference.DrawLine(surface.Target.Handle, 0, 12, 12));
                var actual = surface.Capture(() => paragraph.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(InkPixels(actual) > 100 && actual.SequenceEqual(expected), $"Mixed cascade GPU pixels differ: {hebrew}/{dpi}");
                Save(hebrew == "Arial Hebrew" ? "cascade-arial" : "cascade-peninim", dpi, actual);
                foreach (string text in new[] { "אבג", "abc tail" })
                {
                    using var primary = context.CreateTextFormat("Avenir Next", 20);
                    using var face = context.CreateTextFormat(text == "אבג" ? hebrew : "Avenir Next", 20);
                    var metrics = primary.GetFontMetrics();
                    // A fallback glyph uses the primary face's line box. Give the
                    // explicit script-font reference that same box before comparing pixels.
                    NativeMethods.TextFormatSetLineSpacing(face.Handle, 1, metrics.LineHeight, metrics.Baseline);
                    face.SetNoWrap(true); face.SetSubpixelPositioning(true);
                    var formatted = surface.Capture(() => surface.Drawing.DrawText(new FormattedText(text, family, 20)
                        { Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
                    var explicitFace = surface.Capture(() => surface.Target.DrawText(text, face, 12, 12, 10000, 48, surface.Ink));
                    Require(formatted.SequenceEqual(explicitFace),
                        $"FormattedText chose the wrong cascade glyphs: {text}/{hebrew}/{dpi}");
                }
            }
        }
    }

    private static void CheckLiveCascades(RenderContext context)
    {
        foreach (int kind in new[] { 0, 1, 2 })
        {
            var document = new FlowDocument { FontSize = 20 };
            document.Blocks.Add(new Paragraph(new Run(CascadeSample)) { Margin = new Thickness(0) });
            Control control = kind switch
            {
                0 => new TextBox { Text = CascadeSample, TextWrapping = TextWrapping.NoWrap },
                1 => new EditControl { Text = CascadeSample, ShowLineNumbers = false },
                _ => new RichTextBox(document)
            };
            control.FontSize = 20; control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
            var window = NewWindow(control);
            try
            {
                window.Show(); window.UpdateLayout(); Require(control.Focus(), "Cascade native editor focus rejected");
                var support = (IImeSupport)control; var view = Runtime.GetNSObject<NSView>(window.Handle)!;
                foreach (string hebrew in new[] { "Arial Hebrew", "New Peninim MT", "Arial Hebrew" })
                {
                    string next = hebrew == "Arial Hebrew" ? "New Peninim MT" : "Arial Hebrew";
                    control.FontFamily = new FontFamily($"Avenir Next, {hebrew}, {next}");
                    window.UpdateLayout(); Require(support.TrySetImeSelection(0, 0), "Cascade editor anchor rejected");
                    double origin = support.GetImeCaretRectangle().X;
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask);
                    Require(support.TryGetImeSurroundingText(out var selected) && selected.CursorIndex == CascadeSample.Length,
                        "Cascade native Command-Right did not reach the line end");
                    using var reference = CascadeReference(hebrew);
                    double width = reference.Caret(0, CascadeSample.Length, false).X;
                    Require(Math.Abs(support.GetImeCaretRectangle().X - origin - (kind == 0 ? Math.Round(width) : width)) < .1,
                        $"Live native editor retained a stale fallback: editor={kind}, font={hebrew}");
                    Require(support.TrySetImeSelection(0, 0), "Cascade word anchor rejected");
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
                    Require(support.TryGetImeSurroundingText(out var word) && word.AnchorIndex == 0 && word.CursorIndex == 3,
                        "Cascade native Option-Shift selection differs from the word reference");
                }
            }
            finally { window.Close(); }
        }
    }

    private const string CssSubsetRule = "@font-face {font-family: HostSubset43; src:url('hebrew.ttf'); unicode-range:U+590-5FF; font-display:block;}";
    private const string CssSubsetFamily = "'Avenir Next', HostSubset43, 'New Peninim MT'";
    private sealed class SubsetResolver : ICssResourceResolver
    {
        private readonly TaskCompletionSource<byte[]> _bytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete() => _bytes.TrySetResult(File.ReadAllBytes("/System/Library/Fonts/ArialHB.ttc"));
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken token = default)
            => new(uri, new MemoryStream(await _bytes.Task.WaitAsync(token)), "font/collection");
    }
    private static void CheckCssSubsetPixels(RenderContext context)
    {
        var resolver = new SubsetResolver(); var owner = new TextBlock { Text = CascadeSample, FontSize = 20 };
        Css.GetStyleSheets(owner).Add(CssStyleSheet.Parse(CssSubsetRule, "Subset host43", new Uri("https://subset43.invalid/"), resolver));
        Css.SetStyle(owner, "font-family:" + CssSubsetFamily);
        try
        {
            string source = owner.FontFamily.GetRenderingSource(owner);
            Require(!CssFontFaces.IsBlocked(source), "Partial CSS face hid the whole line");
            using var before = NativeTextParagraph.TryCreate([new(CascadeSample, source, 20, 400, 0, Colors.Black)],
                "Avenir Next", 20, 340, 30, TextAlignment.Left, FlowDirection.LeftToRight)!;
            using var visible = NativeTextParagraph.TryCreate([new("abc ", "Avenir Next", 20, 400, 0, Colors.Black),
                new("אבג", "New Peninim MT", 20, 400, 0, Colors.Black, 0), new(" tail", "Avenir Next", 20, 400, 0, Colors.Black)],
                "Avenir Next", 20, 340, 30, TextAlignment.Left, FlowDirection.LeftToRight)!;
            for (int dpi = 1; dpi <= 2; dpi++)
            {
                using var surface = new FontSurface(context, dpi);
                var pixels = surface.Capture(() => before.DrawLine(surface.Target.Handle, 0, 12, 12));
                var expected = surface.Capture(() => visible.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(InkPixels(pixels) > 100 && pixels.SequenceEqual(expected), "Partial CSS paragraph pixels differ from explicit invisible Hebrew");
                Save("css-partial-block", dpi, pixels);
                var formatted = surface.Capture(() => surface.Drawing.DrawText(new FormattedText(CascadeSample, source, 20)
                    { Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
                var whole = surface.Capture(() => surface.Drawing.DrawText(new FormattedText(CascadeSample, "Avenir Next, New Peninim MT", 20)
                    { Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12)));
                Require(TextMeasurement.HitTestTextRangeWrapped(CascadeSample, source, 20, 400, 0, float.PositiveInfinity, 4, 3, out var range), "Subset range unavailable");
                int left = (int)Math.Floor((12 + range.X - 3) * dpi), right = (int)Math.Ceiling((12 + range.X + range.Width + 3) * dpi);
                int outsideInk = 0;
                for (int i = 0; i < formatted.Length / 4; i++)
                {
                    int x = i % (360 * dpi);
                    if (x < left || x > right)
                    {
                        Require(formatted.AsSpan(i * 4, 4).SequenceEqual(whole.AsSpan(i * 4, 4)), "FormattedText changed visible fallback ink or its baseline");
                        if (formatted[i * 4] < 240) outsideInk++;
                    }
                    else Require(formatted[i * 4] >= 240, "FormattedText painted a waiting Hebrew glyph");
                }
                Require(outsideInk > 100, "FormattedText hid the Latin remainder"); Save("css-formatted-block", dpi, formatted);
            }
            resolver.Complete(); Css.WaitForFontsAsync(owner).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            CssFontFaces.FlushNotifications(owner.Dispatcher); source = owner.FontFamily.GetRenderingSource(owner);
            using var after = NativeTextParagraph.TryCreate([new(CascadeSample, source, 20, 400, 0, Colors.Black)],
                "Avenir Next", 20, 340, 30, TextAlignment.Left, FlowDirection.LeftToRight)!;
            using var reference = CascadeReference("Arial Hebrew");
            for (int dpi = 1; dpi <= 2; dpi++)
            {
                using var surface = new FontSurface(context, dpi);
                var pixels = surface.Capture(() => after.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(pixels.SequenceEqual(surface.Capture(() => reference.DrawLine(surface.Target.Handle, 0, 12, 12))), "Loaded unicode subset did not choose Arial Hebrew");
                Save("css-subset-loaded", dpi, pixels);
            }
        }
        finally { resolver.Complete(); Css.SetStyleSheets(owner, null); }
    }

    private static void CheckLiveCssSubset(RenderContext context)
    {
        foreach (bool rich in new[] { false, true })
        {
            var resolver = new SubsetResolver(); var run = new Run(CascadeSample) { FontSize = 20 };
            var document = new FlowDocument { FontFamily = "Avenir Next", FontSize = 20 };
            document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
            Control control = rich ? new RichTextBox(document) : new TextBox { Text = CascadeSample, TextWrapping = TextWrapping.NoWrap };
            control.FontSize = 20; control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
            var window = NewWindow(control); DependencyObject styled = rich ? run : control;
            Css.GetStyleSheets(control).Add(CssStyleSheet.Parse(CssSubsetRule, "Live subset43", new Uri("https://subset43.invalid/"), resolver));
            Css.SetStyle(styled, "font-family:" + CssSubsetFamily);
            try
            {
                window.Show(); window.UpdateLayout(); Require(control.Focus(), "CSS subset editor focus rejected");
                var support = (IImeSupport)control; var view = Runtime.GetNSObject<NSView>(window.Handle)!;
                double Width()
                {
                    Require(support.TrySetImeSelection(0, 0), "Subset caret anchor rejected"); double origin = support.GetImeCaretRectangle().X;
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask);
                    Require(support.TryGetImeSurroundingText(out var text) && text.CursorIndex == CascadeSample.Length, "Subset native Command-Right failed");
                    return support.GetImeCaretRectangle().X - origin;
                }
                double before = Width(); using var expectedBefore = CascadeReference("New Peninim MT");
                Require(Math.Abs(before - (rich ? expectedBefore.Caret(0, CascadeSample.Length, false).X : Math.Round(expectedBefore.Caret(0, CascadeSample.Length, false).X))) < .1,
                    "Pending subset changed fallback caret geometry");
                resolver.Complete(); Css.WaitForFontsAsync(control).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                CssFontFaces.FlushNotifications(control.Dispatcher); window.UpdateLayout();
                double after = Width(); using var expectedAfter = CascadeReference("Arial Hebrew");
                Require(before != after && Math.Abs(after - (rich ? expectedAfter.Caret(0, CascadeSample.Length, false).X : Math.Round(expectedAfter.Caret(0, CascadeSample.Length, false).X))) < .1,
                    "Arriving CSS subset did not refresh public IME caret geometry");
            }
            finally { resolver.Complete(); Css.SetStyleSheets(control, null); window.Close(); }
        }
    }

    private static void CheckWeightPixels(RenderContext context)
    {
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi);
            var systemProfiles = new List<byte[]>();
            for (int weight = 100; weight <= 900; weight += 100)
            {
                foreach (int style in new[] { 0, 1, 2 })
                {
                    using var reference = context.CreateTextFormat(WeightFaces[weight / 100 - 1], 20, 400, style);
                    reference.SetNoWrap(true); reference.SetSubpixelPositioning(true);
                    var expected = surface.Capture(() => surface.Target.DrawText(Sample, reference, 12, 12, 340, 48, surface.Ink));
                    var actual = surface.Capture(() => DrawWeight(surface, "Avenir Next", weight, style));
                    Require(actual.SequenceEqual(expected), $"Named weight GPU pixels differ: weight={weight}, style={style}, dpi={dpi}");
                    if (style == 0 && weight is 100 or 500 or 900) Save($"named-weight-{weight}", dpi, actual);
                }
                var system = surface.Capture(() => DrawWeight(surface, "system-ui", weight, 0));
                Require(InkPixels(system) > 100 && !systemProfiles.Any(previous => previous.SequenceEqual(system)),
                    $"System weight glyphs collapsed: weight={weight}, dpi={dpi}");
                systemProfiles.Add(system); if (weight is 100 or 500 or 900) Save($"system-weight-{weight}", dpi, system);
            }
        }
    }

    private static void CheckLiveWeights(RenderContext context)
    {
        foreach (int kind in new[] { 0, 1, 2 })
        {
            var run = new Run(Sample) { FontFamily = new FontFamily("Avenir Next"), FontSize = 20 };
            var document = new FlowDocument { FontFamily = "Avenir Next", FontSize = 20 };
            document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
            Control control = kind switch
            {
                0 => new TextBox { Text = Sample, TextWrapping = TextWrapping.NoWrap },
                1 => new EditControl { Text = Sample, ShowLineNumbers = false },
                _ => new RichTextBox(document)
            };
            control.FontFamily = new FontFamily("Avenir Next"); control.FontSize = 20;
            control.Padding = new Thickness(0); control.BorderThickness = new Thickness(0);
            var window = NewWindow(control);
            try
            {
                window.Show(); window.UpdateLayout(); Require(control.Focus(), "Weighted native editor focus rejected");
                var support = (IImeSupport)control; var view = Runtime.GetNSObject<NSView>(window.Handle)!;
                for (int weight = 100; weight <= 900; weight += 100)
                {
                    control.FontWeight = FontWeight.FromOpenTypeWeight(weight); run.FontWeight = control.FontWeight;
                    window.UpdateLayout(); Require(support.TrySetImeSelection(0, 0), "Weighted editor selection rejected");
                    double first = support.GetImeCaretRectangle().X;
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.CommandKeyMask);
                    Require(support.TryGetImeSurroundingText(out var selected) && selected.CursorIndex == Sample.Length,
                        "Weighted native Command-Right did not reach the line end");
                    using var font = context.CreateTextFormat(WeightFaces[weight / 100 - 1], 20);
                    font.SetNoWrap(true);
                    Require(font.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var reference),
                        "Weight caret reference unavailable");
                    double expectedX = kind == 0 ? Math.Round(reference.CaretX) : reference.CaretX;
                    double actualX = support.GetImeCaretRectangle().X - first;
                    Require(Math.Abs(actualX - expectedX) < .1,
                        $"Live native editor kept wrong weight geometry: editor={kind}, weight={weight}, actual={actualX}, expected={expectedX}, origin={first}");
                    Require(support.TrySetImeSelection(0, 0), "Weighted word anchor rejected");
                    Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
                    Require(support.TryGetImeSurroundingText(out var word) && word.AnchorIndex == 0 && word.CursorIndex == 4,
                        "Weighted native Option-Shift selection differs from AppKit word movement");
                }
            }
            finally { window.Close(); }
        }
    }

    private static void CheckPrivateWeightPixels(RenderContext context)
    {
        using var collection = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/Avenir Next.ttc"))
            ?? throw new InvalidOperationException("Private weighted collection unavailable");
        using var variable = CssNativeFontResource.Create(File.ReadAllBytes("/System/Library/Fonts/SFNS.ttf"))
            ?? throw new InvalidOperationException("Private variable font unavailable");
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi); var profiles = new List<byte[]>();
            for (int weight = 100; weight <= 900; weight += 100)
            {
                foreach (int style in new[] { 0, 1, 2 })
                {
                    var actual = surface.Capture(() => DrawWeight(surface, collection.Family, weight, style));
                    var expected = surface.Capture(() => DrawWeight(surface, WeightFaces[weight / 100 - 1], 400, style));
                    Require(actual.SequenceEqual(expected), $"Private collection GPU selected wrong face: {weight}/{style}/{dpi}");
                    if (style == 0 && weight is 600 or 900) Save($"collection-weight-{weight}", dpi, actual);
                }
                var pixels = surface.Capture(() => DrawWeight(surface, variable.Family, weight, 0));
                Require(InkPixels(pixels) > 100 && !profiles.Any(previous => previous.SequenceEqual(pixels)),
                    $"Private variable glyphs did not use distinct weights: {weight}/{dpi}");
                profiles.Add(pixels); if (weight is 100 or 500 or 900) Save($"variable-weight-{weight}", dpi, pixels);
            }
        }
    }
    private static void DrawWeight(FontSurface surface, string family, int weight, int style)
        => surface.Drawing.DrawText(new FormattedText(Sample, family, 20) { FontWeight = weight, FontStyle = style,
            Foreground = Brushes.Black, MaxTextWidth = double.PositiveInfinity, MaxTextHeight = 48 }, new Point(12, 12));

    private static void CheckStackPixels(RenderContext context)
    {
        using var mono = context.CreateTextFormat("Menlo", 20); mono.SetNoWrap(true); mono.SetSubpixelPositioning(true);
        using var installed = context.CreateTextFormat("Andale Mono", 20); installed.SetNoWrap(true); installed.SetSubpixelPositioning(true);
        foreach (int dpi in new[] { 1, 2 })
        {
            using var surface = new FontSurface(context, dpi);
            var expected = surface.Capture(() => surface.Target.DrawText(Sample, mono, 12, 12, 340, 48, surface.Ink));
            foreach (string family in new[] { "Menlo, Helvetica", "__MissingWindowFont40__, Menlo", "\"Missing, Font40\", Menlo" })
                Require(expected.SequenceEqual(surface.Capture(() => Draw(surface.Drawing, family))), "Family stack glyph pixels differ from Menlo");
            const string arrival = "DrawWindowFont40, Helvetica";
            var before = surface.Capture(() => Draw(surface.Drawing, arrival));
            using (var resource = Register("DrawWindowFont40"))
            {
                TextMeasurement.ClearCache();
                var after = surface.Capture(() => Draw(surface.Drawing, arrival));
                var reference = surface.Capture(() => surface.Target.DrawText(Sample, installed, 12, 12, 340, 48, surface.Ink));
                Require(!before.SequenceEqual(after), "Font arrival did not change cached drawing glyphs");
                Require(after.SequenceEqual(reference), "Font arrival drawing disagrees with registered font bytes");
                Save("font-stack", dpi, expected); Save("font-arrival", dpi, after);
                surface.Drawing.ClearCache(); TextMeasurement.ClearCache();
            }
        }
    }

    private static void CheckTextBox(RenderContext context)
    {
        var box = new TextBox { Text = Sample, FontFamily = new FontFamily("__MissingWindowFont40__, Menlo"), FontSize = 20,
            Padding = new Thickness(0), BorderThickness = new Thickness(0), TextWrapping = TextWrapping.NoWrap };
        var window = NewWindow(box);
        try
        {
            window.Show(); window.UpdateLayout(); Require(box.Focus(), "TextBox font focus rejected");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!; var support = (IImeSupport)box;
            Require(support.TrySetImeSelection(Sample.Length, 0), "Font caret selection rejected");
            using var reference = context.CreateTextFormat("Menlo", 20);
            Require(reference.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var wanted), "Menlo caret reference failed");
            var caret = support.GetImeCaretRectangle();
            Require(Math.Abs(caret.X - wanted.CaretX) < .1, "TextBox fallback font differs between IME and layout");
            Require(support.TrySetImeSelection(0, 0), "Font word selection rejected");
            Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask | NSEventModifierMask.ShiftKeyMask);
            Require(support.TryGetImeSurroundingText(out var selection) && selection.AnchorIndex == 0 && selection.CursorIndex == 4,
                "Font stack broke native Option-Shift word movement");
        }
        finally { window.Close(); }
    }

    private static void CheckEditor(RenderContext context)
    {
        const string family = "HostEditorFont40, Helvetica";
        var box = new EditControl { Text = Sample, FontFamily = new FontFamily(family), FontSize = 20, ShowLineNumbers = false };
        var window = NewWindow(box);
        try
        {
            window.Show(); window.UpdateLayout(); Require(box.Focus(), "Editor font focus rejected");
            var support = (IImeSupport)box;
            Require(support.TrySetImeSelection(Sample.Length, 0), "Editor font selection rejected");
            var before = support.GetImeCaretRectangle();
            using var resource = Register("HostEditorFont40"); TextMeasurement.ClearCache();
            box.OnFontResourcesChanged(); window.UpdateLayout();
            Require(support.TrySetImeSelection(Sample.Length, 0), "Updated editor selection rejected");
            var after = support.GetImeCaretRectangle();
            using var font = context.CreateTextFormat("Andale Mono", 20);
            Require(font.HitTestTextPosition(Sample, 100000, 1000, (uint)Sample.Length, false, out var expected), "Editor reference failed");
            var editor = (EditorView)typeof(EditControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(box)!;
            var end = editor.GetPointFromOffset(Sample.Length, false);
            Require(before.X != after.X && Math.Abs(end.X - expected.CaretX) < .1, "Live editor kept its pre-arrival font geometry");
            var view = Runtime.GetNSObject<NSView>(window.Handle)!;
            Require(support.TrySetImeSelection(0, 0), "Updated editor word selection rejected");
            Send(view, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask);
            Require(support.TryGetImeSurroundingText(out var word) && word.CursorIndex == 4, "Updated editor native word movement failed");
        }
        finally { window.Close(); }
    }

    private static void CheckRichCss(RenderContext context)
    {
        var resolver = new FontResolver(); var run = new Run(Sample) { FontSize = 20, Foreground = Brushes.Black };
        var document = new FlowDocument { FontSize = 20, FontFamily = "Helvetica", Foreground = Brushes.Black };
        document.Blocks.Add(new Paragraph(run) { Margin = new Thickness(0) });
        var box = new RichTextBox(document) { Width = 360, Height = 160, Padding = new Thickness(0), BorderThickness = new Thickness(0) };
        var window = NewWindow(box);
        Css.GetStyleSheets(box).Add(CssStyleSheet.Parse("@font-face {font-family: HostCss40; src: url('mono.ttf'); font-display: block;}",
            "Window host font", new Uri("https://window-font-test.invalid/"), resolver));
        Css.SetStyle(run, "font-family: HostCss40, Helvetica");
        try
        {
            window.Show(); window.UpdateLayout(); Require(box.Focus(), "Rich font focus rejected");
            var before = NativeParagraph(box);
            Require(CssFontFaces.IsBlocked(run.FontFamily.GetRenderingSource(run)), "CSS block period was unavailable");
            foreach (int dpi in new[] { 1, 2 })
            {
                using var surface = new FontSurface(context, dpi);
                var pixels = surface.Capture(() => before.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(InkPixels(pixels) == 0, "Blocked rich paragraph exposed fallback glyphs"); Save("rich-font-blocked", dpi, pixels);
            }
            resolver.Complete(); Css.WaitForFontsAsync(box).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            CssFontFaces.FlushNotifications(box.Dispatcher); window.UpdateLayout();
            Require(Css.GetFontLoadErrors(box).Count == 0, "Private CSS font failed to load");
            var after = NativeParagraph(box); Require(before.IsDisposed, "Rich paragraph retained blocked font after arrival");
            using var expected = NativeTextParagraph.TryCreate([new(Sample, "Andale Mono", 20, 400, 0, Colors.Black)],
                "Andale Mono", 20, 360, 30, TextAlignment.Left, FlowDirection.LeftToRight)!;
            Require(Math.Abs(after.Caret(0, Sample.Length, false).X - expected.Caret(0, Sample.Length, false).X) < .1,
                "CSS-loaded rich paragraph used a substituted font");
            foreach (int dpi in new[] { 1, 2 })
            {
                using var surface = new FontSurface(context, dpi);
                var pixels = surface.Capture(() => after.DrawLine(surface.Target.Handle, 0, 12, 12));
                var reference = surface.Capture(() => expected.DrawLine(surface.Target.Handle, 0, 12, 12));
                Require(InkPixels(pixels) > 100 && pixels.SequenceEqual(reference), "Loaded rich CSS glyph pixels differ from the same font");
                Save("rich-font-loaded", dpi, pixels);
            }
            var support = (IImeSupport)box; Require(support.TrySetImeSelection(0, 0), "CSS rich word selection rejected");
            Send(Runtime.GetNSObject<NSView>(window.Handle)!, 0x7c, "\uf703", NSEventModifierMask.AlternateKeyMask);
            Require(support.TryGetImeSurroundingText(out var word) && word.CursorIndex == 4, "Loaded rich native word movement failed");
        }
        finally { resolver.Complete(); Css.SetStyleSheets(box, null); window.Close(); }
    }

    private static Window NewWindow(Control editor)
    {
        var window = new Window { Content = editor, Width = 420, Height = 200, TitleBarStyle = WindowTitleBarStyle.Native, ShowActivated = false };
        if (Application.Current is { } application) application.MainWindow = window;
        else _ = new Application { MainWindow = window, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        return window;
    }
    private static void Draw(RenderTargetDrawingContext drawing, string family) => drawing.DrawText(new FormattedText(Sample, family, 20)
        { Foreground = Brushes.Black, MaxTextWidth = double.MaxValue, MaxTextHeight = 48 }, new Point(12, 12));
    private sealed class FontSurface : IDisposable
    {
        private readonly NSView _view; private readonly int _dpi;
        internal RenderTarget Target { get; }
        internal RenderTargetDrawingContext Drawing { get; }
        internal NativeBrush Ink { get; }
        internal FontSurface(RenderContext context, int dpi)
        {
            _dpi = dpi; _view = new NSView(new CGRect(0, 0, 360, 96));
            Target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(_view.Handle), 360 * dpi, 96 * dpi);
            Target.SetDpi(96 * dpi, 96 * dpi); Drawing = new(Target, context); Ink = context.CreateSolidBrush(0, 0, 0);
        }
        internal byte[] Capture(Action paint)
        {
            Require(Target.RequestReadback() == JaliumResult.Ok, "Font GPU readback failed"); Target.BeginDraw(); Target.Clear(1, 1, 1);
            paint(); Target.EndDraw(); var pixels = new byte[360 * _dpi * 96 * _dpi * 4];
            Require(Target.FetchReadback(pixels, (uint)(360 * _dpi * 4), out int width, out int height) == JaliumResult.Ok &&
                width == 360 * _dpi && height == 96 * _dpi, "Font GPU physical dimensions differ"); return pixels;
        }
        public void Dispose() { Drawing.Dispose(); Ink.Dispose(); Target.Dispose(); _view.Dispose(); }
    }
    private static int InkPixels(byte[] pixels) => Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] < 240);
    private static void Save(string name, int dpi, byte[] pixels)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_MANAGED_FONT_CAPTURE_DIR") is not string directory) return;
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, $"{name}-{dpi}x");
        File.WriteAllBytes(path + ".bgra", pixels);
        File.WriteAllText(path + ".json", $"{{\"Width\":{360 * dpi},\"Height\":{96 * dpi},\"InkPixels\":{InkPixels(pixels)}}}\n");
    }
    private static NativeTextParagraph NativeParagraph(RichTextBox box)
    {
        object layout = typeof(RichTextBox).GetMethod("EnsureLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, [box.RenderSize.Width])!;
        object block = ((IList)layout.GetType().GetProperty("Blocks")!.GetValue(layout)!)[0]!;
        return (NativeTextParagraph)block.GetType().GetProperty("NativeParagraph")!.GetValue(block)!;
    }
    private static void Send(NSView view, ushort scan, string text, NSEventModifierMask flags)
    {
        using var down = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, flags, 1, view.Window!.WindowNumber, null, text, text, false, scan)!;
        using var up = NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, flags, 1.1, view.Window.WindowNumber, null, text, text, false, scan)!;
        view.KeyDown(down); view.KeyUp(up);
    }
    private static unsafe PrivateFont Register(string family)
    {
        byte[] bytes = File.ReadAllBytes(MonoPath);
        fixed (byte* pointer = bytes)
        {
            nint handle = NativeMethods.FontResourceRegister(family, (nint)pointer, (uint)bytes.Length);
            Require(handle != 0, "Host private font registration failed"); return new(handle);
        }
    }
    private sealed class PrivateFont(nint handle) : IDisposable
    {
        public void Dispose() => NativeMethods.FontResourceRelease(handle);
    }
    private sealed class FontResolver : ICssResourceResolver
    {
        private readonly TaskCompletionSource<byte[]> _bytes = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Complete() => _bytes.TrySetResult(File.ReadAllBytes(MonoPath));
        public async ValueTask<CssResource> ResolveAsync(Uri uri, CancellationToken cancellationToken = default)
            => new(uri, new MemoryStream(await _bytes.Task.WaitAsync(cancellationToken)), "font/ttf");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
