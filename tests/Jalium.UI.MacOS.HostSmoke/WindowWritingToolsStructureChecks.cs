#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

internal static partial class WindowWritingToolsStructureChecks
{
    internal static int Run(string argument)
    {
        int index = int.Parse(argument.Split('=').Last());
        if (index is < 0 or >= 60) return 2;
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        string output = Environment.GetEnvironmentVariable("JALIUM_WRITING_STRUCTURE_OUTPUT")!;
        Directory.CreateDirectory(output);
        bool custom = index % 2 == 1, software = index % 4 >= 2;
        int model = new[] { 0, 1, 4, 5, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }[index / 4];
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var editor = new RichTextBox(Model(model)) { Height = 472, FontSize = 18 };
        editor.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "列表与表格结构往返", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "内容经过原生文本服务后，层级、段落与编号应保持。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(editor);
        var status = new TextBlock { Text = "检查准备中", FontSize = 15, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var owner = new Window { Title = "Jalium Writing Tools Structure " + index, Width = custom ? 420 : 760, Height = 660,
            MinWidth = 400, MinHeight = 620, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = panel };
        owner.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); Application.Current!.MainWindow = owner;
        var checks = new List<string>(); var failures = new List<string>(); var scenarios = new List<object>(); var focus = new List<object>();
        int unavailable = 0, phase = 0; bool finished = false; var watch = Stopwatch.StartNew();
        string before = "", shape = ""; Block[] original = []; var contexts = new List<object>(); var geometryRecords = new List<object>(); var replacements = new List<object>(); var markerPixels = new List<object>(); string previewKind = "not-requested";
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Attempt(string name, Action body)
        {
            int start = checks.Count; Exception? error = null;
            try { body(); } catch (Exception failure) { error = failure; failures.Add(name + ": " + failure); }
            scenarios.Add(new { name, passed = error is null, checks = checks.Skip(start).ToArray(), error = error?.ToString() });
        }
        void Observe()
        {
            var view = Runtime.GetNSObject<NSView>(owner.Handle)!; var nativeWindow = view.Window!;
            bool active = app.Active; int? pid = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier;
            bool available = active && pid == Environment.ProcessId;
            if (!available) unavailable++;
            focus.Add(new { available, appActive = active, frontmostPID = pid, nativeKey = nativeWindow.IsKeyWindow,
                ownResponder = nativeWindow.FirstResponder?.Handle.ToString(), expectedResponder = view.Handle.ToString() });
            Require(ReferenceEquals(Keyboard.FocusedElement, editor), "logical rich-editor focus is retained");
            Require(nativeWindow.FirstResponder?.Handle == view.Handle, "subject window retains its native input responder");
            if (available) Require(nativeWindow.IsKeyWindow && app.KeyWindow?.Handle == nativeWindow.Handle, "foreground subject remains the native key window");
        }
        NSWritingToolsCoordinatorContext Context(out NSWritingToolsCoordinator coordinator, out INSWritingToolsCoordinatorDelegate client)
        {
            var view = Runtime.GetNSObject<NSView>(owner.Handle)!; coordinator = view.WritingToolsCoordinator!;
            Require(coordinator?.WeakDelegate is not null, "focused rich editor exposes the actual SDK delegate");
            client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
            string Read(NSObject value, string key)
            {
                try { using var name = new NSString(key); return value.ValueForKey(name)?.ToString() ?? "null"; }
                catch (Exception error) { return error.GetType().Name; }
            }
            string stoppedBefore = Read(coordinator.WeakDelegate!, "stopped"), currentBefore = Read(coordinator.WeakDelegate!, "current");
            string inputBefore = Read(coordinator.WeakDelegate!, "inputGeneration"), viewInputBefore = Read(view, "textInputGeneration");
            NSWritingToolsCoordinatorContext? context = null; int calls = 0;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { calls++; context = values.FirstOrDefault(); });
            contexts.Add(new { phase, calls, nativeText = context?.AttributedString.Value, managedText = editor.Document.GetText(), coordinator = coordinator.Handle.ToString(), stoppedBefore, currentBefore, inputBefore, viewInputBefore,
                stoppedAfter = Read(coordinator.WeakDelegate!, "stopped"), currentAfter = Read(coordinator.WeakDelegate!, "current"),
                nativeEditor = Read(coordinator.WeakDelegate!, "editor"), nativeSession = Read(coordinator.WeakDelegate!, "session") });
            Require(calls == 1 && context?.AttributedString.Value == editor.Document.GetText(), "full SDK context retains the exact document projection; calls=" + calls + "; native=" + context?.AttributedString.Value + "; managed=" + editor.Document.GetText());
            return context!;
        }
        void Replace(NSWritingToolsCoordinator coordinator, INSWritingToolsCoordinatorDelegate client, NSWritingToolsCoordinatorContext context, NSAttributedString proposed)
        {
            NSAttributedString? accepted = null; int calls = 0;
            client.ReplaceRange(coordinator, new NSRange(0, (nint)context.AttributedString.Value.Length), context, proposed,
                NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => { calls++; accepted = value; });
            string proposalPath = Path.Combine(output, "received-fragment.json");
            string? packet = File.Exists(proposalPath) ? File.ReadAllText(proposalPath) : null;
            replacements.Add(new { phase, calls, proposed = proposed.Value, accepted = accepted?.Value,
                managedAfter = editor.Document.GetText(), shapeAfter = Shape(editor.Document.Blocks), packet });
            Require(calls == 1 && accepted?.Value == proposed.Value, "SDK replacement completes once with the accepted projection");
        }
        bool Same(Rect left, Rect right) => !left.IsEmpty && !right.IsEmpty &&
            Math.Abs(left.X - right.X) < 0.02 && Math.Abs(left.Y - right.Y) < 0.02 &&
            Math.Abs(left.Width - right.Width) < 0.02 && Math.Abs(left.Height - right.Height) < 0.02;
        void CheckGeometry(string stage, bool preview)
        {
            owner.UpdateLayout(); var ime = (IImeSupport)editor; int count = 0, visible = 0;
            string projection = editor.Document.GetText();
            foreach (var paragraph in editor.Document.EnumerateParagraphs())
            {
                int start = paragraph.ContentStart.DocumentOffset, length = TextElement.GetContentLength(paragraph);
                Require(ime.TryGetImeTextRangeGeometry(start, length, false, out var range), "paragraph exposes an actual first-line IME rectangle");
                var rect = range.Rectangle;
                Require(double.IsFinite(rect.X) && double.IsFinite(rect.Y) && rect.Width >= 0 && rect.Height > 0,
                    "paragraph geometry is finite after mixed table/list layout");
                Require(paragraph.ContentStart.HasValidLayout, "public text position reports the current actual layout");
                Require(paragraph.ContentStart.IsAtLineStartPosition &&
                    paragraph.ContentStart.GetLineStartPosition(0)?.DocumentOffset == start, "public line start uses the displayed paragraph line");
                for (int at = start; at < start + length;)
                {
                    int end = Math.Min(start + length, GraphemeClusters.NextBoundary(projection, at));
                    Require(ime.TryGetImeTextRangeGeometry(at, end - at, false, out var expected), "native range reaches each authored grapheme");
                    var forward = editor.Document.GetPositionAtOffset(at, LogicalDirection.Forward)!;
                    var backward = editor.Document.GetPositionAtOffset(end, LogicalDirection.Backward)!;
                    var forwardRect = forward.GetCharacterRect(LogicalDirection.Forward);
                    var backwardRect = backward.GetCharacterRect(LogicalDirection.Backward);
                    Require(Same(expected.Rectangle, forwardRect) && Same(expected.Rectangle, backwardRect),
                        "public adjacent-character rectangles match native graphemes; at=" + at + "; expected=" + expected.Rectangle +
                        "; forward=" + forwardRect + "; backward=" + backwardRect);
                    at = end;
                }
                if (range.Length < length)
                {
                    var next = paragraph.ContentStart.GetLineStartPosition(1, out int moved);
                    Require(moved == 1 && next is not null && next.DocumentOffset > start &&
                        next.GetCharacterRect(LogicalDirection.Forward).Top > rect.Top,
                        "public line navigation reaches the next displayed wrap or explicit line");
                }
                var firstCharacter = paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var point = new Point(firstCharacter.Left + Math.Max(0.01, firstCharacter.Width / 2),
                    firstCharacter.Top + Math.Min(1, firstCharacter.Height / 2));
                bool inViewport = new Rect(0, 0, editor.ActualWidth, editor.ActualHeight).Contains(point);
                int hit = -1;
                if (length > 0 && inViewport)
                {
                    Require(ime.TryGetImeCharacterIndex(point, false, out hit), "IME point query reaches visible paragraph text");
                    var position = editor.GetPositionFromPoint(point, true);
                    Require(position is not null && hit == position.DocumentOffset && hit >= start && hit <= start + length,
                        "native-facing and public point mappings retain the paragraph's source range");
                    Require(hit <= GraphemeClusters.NextBoundary(projection, start),
                        $"visible first-grapheme hit remains within the actually sampled source character; start={start}; hit={hit}; point={point}; first={firstCharacter}");
                    visible++;
                }
                geometryRecords.Add(new { stage, kind = "paragraph", start, length, rectangle = rect, inViewport, hit }); count++;
            }
            foreach (var cell in EmptyCells(editor.Document.Blocks))
            {
                int start = cell.ContentStart.DocumentOffset;
                Require(ime.TryGetImeTextRangeGeometry(start, 0, false, out var range), "zero-block cell exposes an IME insertion rectangle");
                var rect = cell.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Require(cell.ContentStart.HasValidLayout && Same(range.Rectangle, rect) && rect.Width == 0 && rect.Height > 0,
                    "zero-block public rectangle stays in its own cell and has a real line height");
                var point = new Point(rect.X + 0.1, rect.Y + Math.Min(1, rect.Height / 2));
                bool inViewport = new Rect(0, 0, editor.ActualWidth, editor.ActualHeight).Contains(point);
                if (inViewport)
                {
                    Require(editor.GetPositionFromPoint(point, true)?.DocumentOffset == start,
                        "point mapping reaches the same zero-block cell insertion position");
                    visible++;
                }
                geometryRecords.Add(new { stage, kind = "empty-cell", start, length = 0, rectangle = rect, inViewport }); count++;
            }
            Require(count > 0 && visible > 0, "document retains visible and addressable paragraphs or empty cells");
            if (!preview) return;
            var source = ((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Text)!).Source;
            var first = editor.Document.EnumerateParagraphs().FirstOrDefault(paragraph => TextElement.GetContentLength(paragraph) > 0);
            int atPreview = first?.ContentStart.DocumentOffset ?? 0;
            int extent = first is null ? projection.Length : TextElement.GetContentLength(first);
            var image = MacOSWritingToolsPresentation.CreatePreview(editor, source, atPreview, extent, Matrix.Identity,
                owner.DpiScale, null, () => owner.Handle != 0);
            if (first is null)
            {
                previewKind = "structural-empty";
                if (image is not null) File.WriteAllBytes(Path.Combine(output, "unexpected-empty-preview.png"), image.Png);
                Require(image is null, "pure structural storage does not fabricate a glyph preview");
                return;
            }
            previewKind = "glyphs";
            Require(image is not null && image.Png.Length > 0, "real backend creates a preview of table/list text");
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(image!.Png); bitmap.EndInit();
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            bool ink = false, transparent = false;
            for (int i = 3; i < pixels.Length; i += 4) { ink |= pixels[i] > 20; transparent |= pixels[i] < 4; }
            Require(ink && transparent, "glyph preview contains ink and transparent background");
            Require(ime.TryGetImeTextRangeGeometry(atPreview, extent, false, out var firstLine) &&
                image.Frame.Width <= firstLine.Rectangle.Width + 4 * firstLine.Rectangle.Height &&
                image.Frame.Height <= editor.ActualHeight, "glyph preview excludes the table's full opaque surface");
            File.WriteAllBytes(Path.Combine(output, "preview.png"), image.Png);
        }
        BitmapSource Capture(string phase)
        {
            owner.UpdateLayout(); double dpi = owner.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(owner.ActualWidth * dpi), (int)Math.Ceiling(owner.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(owner.Background, null, new Rect(0, 0, owner.ActualWidth, owner.ActualHeight)); owner.Render(drawing); }, () => owner.Handle != 0)
                ?? throw new InvalidOperationException("Backend readback unavailable.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, phase + ".png")); encoder.Save(file);
            return bitmap;
        }
        owner.Show(); owner.UpdateLayout(); owner.Activate(); editor.Focus();
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ =>
        {
            if (finished) return;
            bool ready = app.Active && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId;
            if (!ready && watch.Elapsed.TotalSeconds < 12) return;
            try
            {
                if (phase == 0)
                {
                Observe(); Capture("before");
                Attempt("table-geometry-before", () => CheckGeometry("before", true));
                before = editor.Document.GetText(); shape = Shape(editor.Document.Blocks); original = editor.Document.Blocks.ToArray();
                Attempt("unchanged-attributed-proposal", () =>
                {
                    var context = Context(out var coordinator, out var client); Replace(coordinator, client, context, context.AttributedString);
                    Require(editor.Document.GetText() == before, "unchanged attributed proposal preserves exact Unicode and table storage");
                    Require(Shape(editor.Document.Blocks) == shape, "unchanged attributed proposal preserves mixed container order and item ownership");
                });
                Attempt("undo-unchanged", () =>
                {
                    Require(editor.Undo(), "unchanged attributed replacement creates one undo unit");
                    Require(editor.Document.GetText() == before && Shape(editor.Document.Blocks) == shape, "Undo restores the source projection and structure");
                    Require(original.Length == editor.Document.Blocks.Count && original.Select((block, i) => ReferenceEquals(block, editor.Document.Blocks[i])).All(x => x), "Undo restores original root identities");
                });
                Attempt("redo-unchanged", () =>
                {
                    Require(editor.Redo() && editor.Document.GetText() == before && Shape(editor.Document.Blocks) == shape, "Redo restores the unchanged structured proposal");
                });
                editor.Document = Model(model); editor.Focus(); owner.UpdateLayout();
                // Document replacement retires the text-service session. Let the
                // normal AppKit/dispatcher turn attach its successor before a new operation.
                phase = 1;
                return;
                }
                finished = true;
                Observe();
                original = editor.Document.Blocks.ToArray();
                var expected = Model(model); var run = expected.EnumerateParagraphs().SelectMany(p => p.Inlines.OfType<Run>()).FirstOrDefault(r => r.Text.Any(char.IsLetter));
                bool inserting = run is null;
                if (run is null) EmptyCells(expected.Blocks).First().Blocks.Add(P("新"));
                else { int local = Array.FindIndex(run.Text.ToCharArray(), char.IsLetter); run.Text = run.Text[..local] + "新" + run.Text[(local + 1)..]; }
                string after = expected.GetText(), afterShape = Shape(expected.Blocks);
                Attempt("edited-attributed-proposal", () =>
                {
                    var context = Context(out var coordinator, out var client); using var proposed = new NSMutableAttributedString(context.AttributedString);
                    if (inserting)
                    {
                        using var inserted = new NSMutableAttributedString(context.AttributedString.Substring(0, 1));
                        inserted.Replace(new NSRange(0, 1), "新\n");
                        var authoredRange = new NSRange(0, inserted.Length);
                        inserted.RemoveAttribute("JaliumWritingToolsEmptyCell", authoredRange);
                        inserted.RemoveAttribute("JaliumWritingToolsStorageSeparator", authoredRange);
                        inserted.RemoveAttribute(NSStringAttributeKey.WritingToolsExclusion.ToString(), authoredRange);
                        proposed.Insert(inserted, 0);
                    }
                    else { int at = Array.FindIndex(before.ToCharArray(), char.IsLetter); proposed.Replace(new NSRange(at, 1), "新"); }
                    Replace(coordinator, client, context, proposed);
                    Require(editor.Document.GetText() == after, inserting ? "SDK insertion adds authored text inside the original empty cell" : "edited attributed proposal changes only its authored character");
                    Require(Shape(editor.Document.Blocks) == afterShape, "edited proposal preserves list numbering, item grouping, table spans and mixed nesting");
                });
                Attempt("undo-edited", () =>
                {
                    Require(editor.Undo() && editor.Document.GetText() == before && Shape(editor.Document.Blocks) == shape, "one Undo restores the edited source structure");
                    Require(original.Length == editor.Document.Blocks.Count && original.Select((block, i) => ReferenceEquals(block, editor.Document.Blocks[i])).All(x => x), "edited Undo restores original root identities");
                });
                Attempt("redo-edited", () => Require(editor.Redo() && editor.Document.GetText() == after && Shape(editor.Document.Blocks) == afterShape, "one Redo restores the edited mixed structure"));
                Observe(); status.Text = failures.Count == 0 ? "结构、编号、撤销与重做检查通过。" : "检查发现问题，请查看记录。";
                Attempt("table-geometry-after", () => CheckGeometry("after", false));
                Attempt("explicit-line-break-geometry", () =>
                {
                    var previous = editor.Document;
                    try
                    {
                        editor.Document = new FlowDocument(P("第一行 Miii🙂\n第二行 e\u0301 中文")); owner.UpdateLayout();
                        CheckGeometry("explicit-line-break", false); Capture("explicit-line-break");
                    }
                    finally { editor.Document = previous; owner.UpdateLayout(); }
                });
                if (model is 17 or 18) Attempt("actual-list-marker-pixels", () =>
                {
                    var list = (DocList)editor.Document.Blocks[0]; var saved = list.MarkerStyle;
                    try
                    {
                        var first = editor.Document.EnumerateParagraphs().First();
                        list.MarkerStyle = TextMarkerStyle.Box; owner.UpdateLayout();
                        var box = Capture("marker-box"); var boxRect = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        list.MarkerStyle = TextMarkerStyle.None; owner.UpdateLayout();
                        var none = Capture("marker-none"); var noneRect = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                        Require(boxRect.Left >= noneRect.Left && Math.Abs(boxRect.Top - noneRect.Top) < .01 && Math.Abs(boxRect.Height - noneRect.Height) < .01,
                            "Box reserves its measured glyph width and keeps the same text baseline");
                        CheckGeometry("marker-none", false);
                        var point = editor.TranslatePoint(new Point(noneRect.Left - 20, noneRect.Top), owner);
                        double dpi = owner.DpiScale;
                        int left = Math.Max(0, (int)Math.Ceiling(point.X * dpi)), top = Math.Max(0, (int)Math.Ceiling(point.Y * dpi));
                        int right = Math.Min(box.PixelWidth, (int)Math.Floor((point.X + 19) * dpi));
                        int bottom = Math.Min(box.PixelHeight, (int)Math.Floor((point.Y + noneRect.Height) * dpi));
                        var a = new byte[box.PixelWidth * box.PixelHeight * 4]; var b = new byte[none.PixelWidth * none.PixelHeight * 4];
                        box.CopyPixels(a, box.PixelWidth * 4, 0); none.CopyPixels(b, none.PixelWidth * 4, 0);
                        int changed = 0, contrast = 0; bool uniform = right > left && bottom > top;
                        int sample = (top * none.PixelWidth + left) * 4;
                        for (int y = top; y < bottom; ++y) for (int x = left; x < right; ++x)
                        {
                            int pixel = (y * box.PixelWidth + x) * 4, difference = 0;
                            for (int channel = 0; channel < 3; ++channel)
                            {
                                difference = Math.Max(difference, Math.Abs(a[pixel + channel] - b[pixel + channel]));
                                uniform &= b[pixel + channel] == b[sample + channel];
                            }
                            if (difference != 0) changed++;
                            contrast = Math.Max(contrast, difference);
                        }
                        markerPixels.Add(new { saved, left, top, right, bottom, changed, contrast, uniform, boxRect, noneRect });
                        Require(changed > 0 && contrast > 64, "Box paints an actual contrasting marker in the gutter on this backend");
                        Require(uniform, "None leaves the marker gutter free of glyphs");
                    }
                    finally { list.MarkerStyle = saved; owner.UpdateLayout(); }
                });
                Capture("after");
            }
            catch (Exception failure) { finished = true; failures.Add("scene: " + failure); }
            finally
            {
                if (finished)
                {
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
                {
                    index, model, custom, software, passed = failures.Count == 0 && unavailable == 0, qualifiedWithoutForeground = failures.Count == 0,
                    failures, checks, scenarios, focus, contexts, geometryRecords, replacements, previewKind, foregroundUnavailable = unavailable, processId = Environment.ProcessId, markerPixels,
                    physicalInput = false, desktopScreenshot = false, appleIntelligenceService = false,
                    serviceAvailable = NSWritingToolsCoordinator.IsWritingToolsAvailable, renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD")
                }));
                owner.Close(); host.AllowQuit = true; app.Terminate(app);
                }
            }
        });
        app.Run(); return 0;
    }

    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        private bool _started; internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
    }
    private static FlowDocument Model(int index)
    {
        Block value = index switch
        {
            0 => MakeList([MakeTable([P("列表内的表格🙂")])]),
            1 => MakeTable([MakeList([P("表格内的列表")], [P("第二项🙂")])]),
            2 => MakeList([MakeTable([MakeList([P("内层编号")])])]),
            3 => MakeTable([MakeList([MakeTable([P("内层表格")])])]),
            4 => MakeList([MakeTable([MakeList([MakeTable([P("交错四层🙂")])])])]),
            5 => MakeList([P("前段"), MakeTable([P("中间表格")]), P("后段🙂")], [P("第二项")]),
            6 => MakeList([MakeTable([P("表一")]), MakeTable([P("表二")]), P("同一项末段")]),
            7 => MakeList([P("外层"), MakeList([P("内层前段"), MakeTable([P("内层表格")]), P("内层后段")])], [P("外层第二项")]),
            8 => MakeList([MakeTable([P("第一项左")], [P("第一项右")])], [MakeTable([P("第二项左")], [P("第二项右")])]),
            9 => SpanningTable(),
            11 => MakeList([P("\t3.\t这也是用户文字🙂")]),
            12 => MakeTable([], [P("空单元格旁的文字🙂")]),
            13 => RawGrid(false),
            14 => RawGrid(true),
            15 => MakeTable([MakeTable([], [P("内层文字🙂")])], []),
            16 => LongParagraph(),
            17 => MarkerList(TextMarkerStyle.None),
            18 => MarkerList(TextMarkerStyle.Box),
            19 => P("Miii 中文🙂\n第二行 e\u0301\n末行"),
            20 => MakeTable([MakeList([P("前段\n第二行🙂"), P("\n")])], [P("旁边\r\n保持")]),
            _ => MakeList([P(""), MakeTable([P("")], [P("中文 e\u0301 🙂")]), P("")], [P("")])
        };
        return new FlowDocument(value);
    }
    private static DocList MarkerList(TextMarkerStyle marker)
    {
        var list = MakeList([P("第一项 Miii 中文🙂")], [P("第二项 e\u0301")]);
        list.MarkerStyle = marker; return list;
    }
    private static Paragraph LongParagraph()
    {
        var paragraph = P(string.Concat(Enumerable.Repeat("Miii 中文 e\u0301 👩‍👧 العربية ", 8)));
        paragraph.FlowDirection = FlowDirection.RightToLeft;
        ((Run)paragraph.Inlines[0]).FontStyle = FontStyles.Italic;
        return paragraph;
    }
    private static Table RawGrid(bool allEmpty)
    {
        var table = MakeTable(allEmpty ? [] : [P("左上")], []);
        var row = new TableRow(); row.Cells.Add(new TableCell());
        row.Cells.Add(allEmpty ? new TableCell() : new TableCell(P("右下🙂")));
        table.RowGroups[0].Rows.Add(row); return table;
    }
    private static IEnumerable<TableCell> EmptyCells(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Table table)
                foreach (var cell in table.RowGroups.SelectMany(group => group.Rows).SelectMany(row => row.Cells))
                {
                    if (cell.Blocks.Count == 0) yield return cell;
                    foreach (var inner in EmptyCells(cell.Blocks)) yield return inner;
                }
            else
            {
                IEnumerable<Block> children = block switch
                { Section section => section.Blocks, DocList list => list.ListItems.SelectMany(item => item.Blocks), _ => Array.Empty<Block>() };
                foreach (var cell in EmptyCells(children)) yield return cell;
            }
        }
    }
    private static Paragraph P(string text) => new(new Run(text));
    private static DocList MakeList(params Block[][] contents)
    {
        var list = new DocList { MarkerStyle = TextMarkerStyle.Decimal, StartIndex = 3 };
        foreach (var blocks in contents) { var item = new ListItem(); foreach (var block in blocks) item.Blocks.Add(block); list.ListItems.Add(item); }
        return list;
    }
    private static Table MakeTable(params Block[][] contents)
    {
        var table = new Table(); var group = new TableRowGroup(); var row = new TableRow();
        table.RowGroups.Add(group); group.Rows.Add(row);
        foreach (var blocks in contents)
        { table.Columns.Add(new TableColumn()); var cell = new TableCell(); foreach (var block in blocks) cell.Blocks.Add(block); row.Cells.Add(cell); }
        return table;
    }
    private static Table SpanningTable()
    {
        var table = MakeTable([MakeList([P("跨行内列表"), P("同项续段🙂")])], [P("跨列标题")]);
        table.Columns.Add(new TableColumn()); var group = table.RowGroups[0]; group.Rows[0].Cells[0].RowSpan = 2; group.Rows[0].Cells[1].ColumnSpan = 2;
        var second = new TableRow(); second.Cells.Add(new TableCell(MakeList([MakeTable([P("右下嵌套")])])) { ColumnSpan = 2 }); group.Rows.Add(second); return table;
    }
    private static TextElement[] Owners(Paragraph paragraph)
    {
        var result = new List<TextElement>();
        for (DependencyObject? owner = paragraph.Parent; owner is TextElement element; owner = element.Parent)
            if (element is ListItem or TableCell) result.Add(element);
        result.Reverse(); return result.ToArray();
    }
    private static string Shape(IEnumerable<Block> blocks) => string.Join("|", blocks.Select(block => block switch
    {
        Paragraph paragraph => "P(" + string.Concat(paragraph.Inlines.OfType<Run>().Select(run => run.Text)) + ")",
        Section section => "S[" + Shape(section.Blocks) + "]",
        DocList list => $"L({list.MarkerStyle},{list.StartIndex})[" + string.Join(";", list.ListItems.Select(item => Shape(item.Blocks))) + "]",
        Table table => "T" + table.Columns.Count + "[" + string.Join(";", table.RowGroups.SelectMany(group => group.Rows).Select(row => string.Join(",", row.Cells.Select(cell => $"({cell.RowSpan},{cell.ColumnSpan})" + Shape(cell.Blocks))))) + "]",
        _ => throw new InvalidOperationException(block.GetType().Name)
    }));
}
