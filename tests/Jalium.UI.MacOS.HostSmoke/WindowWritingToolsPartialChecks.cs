#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
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
    internal static int RunPartial(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 28) return 2;
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        int model = index / 4; bool custom = index % 2 != 0, software = index % 4 >= 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_WRITING_PARTIAL_OUTPUT")!; Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        var render = RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (!software) render.DefaultRenderingEngine = RenderingEngine.Impeller;
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var paragraphs = Enumerable.Range(0, model >= 5 ? 3 : 1).Select(i =>
            new Paragraph(new Run("前缀旧字词后缀" + (model >= 5 ? i.ToString() : ""))
            { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 21, FontStyle = FontStyles.Italic })).ToArray();
        Block root = paragraphs[0];
        if (model == 0) root = MakeList([root]);
        if (model == 1) root = MakeTable([root], [P("邻格保持原文")]);
        if (model == 2) root = MakeList([MakeTable([root], [P("邻格保持原文")])]);
        if (model == 3) root = MakeTable([MakeList([root])], [P("邻格保持原文")]);
        if (model == 4) root = MakeList([new Section(root) { Padding = new Thickness(6) }]);
        if (model == 5) root = MakeList(paragraphs.Select(p => new Block[] { p }).ToArray());
        if (model == 6) root = MakeTable(paragraphs.Select(p => new Block[] { p }).ToArray());
        var document = new FlowDocument(P("范围前原文")); document.Blocks.Add(root); document.Blocks.Add(P("范围后原文"));
        if (model == 4) document.FlowDirection = FlowDirection.RightToLeft;
        var editor = new RichTextBox(document) { Height = 460, FontSize = 19 };
        editor.SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "局部替换保留原有结构", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "列表、表格、范围外文字与选区应保持正确。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(editor); var status = new TextBlock { Text = "检查准备中", FontSize = 15, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var owner = new Window { Title = "Jalium Window v295 · 局部文本 " + index, Width = custom ? 440 : 760, Height = 670,
            MinWidth = 420, MinHeight = 630, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = panel };
        owner.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); Application.Current!.MainWindow = owner;
        var checks = new List<string>(); var failures = new List<string>(); var scenarios = new List<object>(); var focus = new List<object>();
        var packets = new List<object>(); var geometry = new List<object>(); int unavailable = 0; bool done = false;
        string? lastPacket = null; var watch = Stopwatch.StartNew();
        editor.PreviewTextInput += (_, input) => { if (input.NativeTextServicesPayload is { } value) lastPacket = value; };
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Attempt(string name, Action body)
        {
            int start = checks.Count; Exception? error = null;
            try { body(); } catch (Exception ex) { error = ex; failures.Add(name + ": " + ex); }
            scenarios.Add(new { name, passed = error is null, checks = checks.Skip(start).ToArray(), error = error?.ToString() });
        }
        void Observe(NSView view)
        {
            int frontmost = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool active = app.Active && frontmost == Environment.ProcessId && view.Window!.IsKeyWindow && view.Window.FirstResponder?.Handle == view.Handle;
            focus.Add(new { active, appActive = app.Active, frontmost, processId = Environment.ProcessId,
                nativeKey = view.Window!.IsKeyWindow, responder = view.Window.FirstResponder?.Handle.ToString(), ownResponder = view.Handle.ToString() });
            if (!active) ++unavailable;
            Require(active && ReferenceEquals(Keyboard.FocusedElement, editor), "own logical focus, native responder and system foreground agree");
        }
        (NSWritingToolsCoordinator Coordinator, INSWritingToolsCoordinatorDelegate Client, NSWritingToolsCoordinatorContext Context) Context(NSView view)
        {
            var coordinator = view.WritingToolsCoordinator!;
            Require(coordinator?.WeakDelegate is not null, "actual AppKit writing-tools delegate is installed");
            var client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
            NSWritingToolsCoordinatorContext? context = null; int calls = 0;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { ++calls; context = values.FirstOrDefault(); });
            Require(calls == 1 && context?.AttributedString.Value == document.GetText(), "SDK context matches the full source projection exactly");
            return (coordinator, client, context!);
        }
        void Replace((NSWritingToolsCoordinator Coordinator, INSWritingToolsCoordinatorDelegate Client, NSWritingToolsCoordinatorContext Context) source,
            int start, int length, NSAttributedString proposed)
        {
            string before = document.GetText(); NSAttributedString? accepted = null; int calls = 0; lastPacket = null;
            source.Client.ReplaceRange(source.Coordinator, new NSRange(start, length), source.Context, proposed,
                NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => { ++calls; accepted = value; });
            string after = document.GetText(); int suffix = before.Length - start - length, acceptedLength = after.Length - start - suffix;
            packets.Add(new { start, length, before, proposed = proposed.Value, after, accepted = accepted?.Value, payload = lastPacket });
            Require(calls == 1 && accepted is not null, "SDK partial replacement completes once with an accepted value");
            Require(after.AsSpan(0, start).SequenceEqual(before.AsSpan(0, start)) && suffix >= 0 && acceptedLength >= 0 &&
                after.AsSpan(after.Length - suffix).SequenceEqual(before.AsSpan(before.Length - suffix)), "SDK replacement preserves both outside ranges");
            Require(accepted!.Value == after.Substring(start, acceptedLength), "accepted SDK text is the actual inserted projection");
        }
        void CheckPacket(int start)
        {
            Require(lastPacket is not null, "managed input received the actual native packet");
            using var packet = JsonDocument.Parse(lastPacket!);
            Require(packet.RootElement.GetProperty("sourceStart").GetInt32() <= start, "native source anchor uses absolute document coordinates");
            Require(packet.RootElement.GetProperty("source").GetProperty("paragraphs").EnumerateArray().Any(row => row.TryGetProperty("origin", out _)),
                "original source carries stable paragraph provenance");
            var paths = packet.RootElement.GetProperty("source").GetProperty("paragraphs").EnumerateArray()
                .Where(row => row.TryGetProperty("ancestors", out _)).SelectMany(row => row.GetProperty("ancestors").EnumerateArray()).ToArray();
            Require(paths.Length > 0 && paths.All(path => path.TryGetProperty("origin", out _)), "source retains stable identities for every existing enclosure");
        }
        void Capture(string name)
        {
            owner.UpdateLayout(); double dpi = owner.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(owner.ActualWidth * dpi), (int)Math.Ceiling(owner.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(owner.Background, null, new Rect(0, 0, owner.ActualWidth, owner.ActualHeight)); owner.Render(drawing); }, () => owner.Handle != 0);
            Require(bitmap is not null, "actual requested backend produces a readback");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap!));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        var originalOwners = PartialNodes(document).Where(node => node is DocList or ListItem or Table or TableRowGroup or TableRow or TableCell or Section).ToArray();
        void OwnersRemain()
        {
            var now = PartialNodes(document).Where(node => node is DocList or ListItem or Table or TableRowGroup or TableRow or TableCell or Section).ToArray();
            Require(now.Length == originalOwners.Length && originalOwners.Select((value, at) => ReferenceEquals(value, now[at])).All(value => value),
                "all original container objects and their order remain in the document");
        }
        owner.Show(); owner.UpdateLayout(); owner.Activate(); editor.Focus();
        using var timer = NSTimer.CreateRepeatingScheduledTimer(.08, _ =>
        {
            if (done) return;
            var view = owner.Handle == 0 ? null : Runtime.GetNSObject<NSView>(owner.Handle);
            if (view?.Window is not { } native) { if (watch.Elapsed.TotalSeconds < 15) return; failures.Add("native view unavailable"); done = true; }
            else
            {
                native.MakeKeyAndOrderFront(null); app.ActivateIgnoringOtherApps(true); native.MakeFirstResponder(view); editor.Focus();
                if (!(app.Active && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId && native.IsKeyWindow))
                { if (watch.Elapsed.TotalSeconds < 15) return; unavailable++; failures.Add("foreground qualification unavailable"); done = true; }
                else
                {
                    done = true; string before = document.GetText(), shape = Shape(document.Blocks);
                    int start = paragraphs[0].ContentStart.DocumentOffset + 2; const string replacement = "Miii 中文🙂";
                    Attempt("word-through-renewed-native-context", () =>
                    {
                        Observe(view); Capture("before"); var old = Context(view);
                        using var proposed = new NSMutableAttributedString(old.Context.AttributedString.Substring(start, 3));
                        proposed.Replace(new NSRange(0, proposed.Length), replacement); using var renewed = Context(view).Context;
                        Replace(old, start, 3, proposed);
                        Require(document.GetText() == before[..start] + replacement + before[(start + 3)..], "word replacement changes exactly the selected source characters");
                        OwnersRemain(); CheckPacket(start); owner.UpdateLayout();
                        editor.Selection.Select(document.GetPositionAtOffset(start, LogicalDirection.Forward)!, document.GetPositionAtOffset(start + replacement.Length, LogicalDirection.Backward)!);
                        Require(editor.Selection.Text == replacement, "visible selection covers the inserted Latin, Chinese and emoji text");
                        var ime = (IImeSupport)editor; string text = document.GetText();
                        for (int at = start; at < start + replacement.Length;)
                        {
                            int end = Math.Min(start + replacement.Length, GraphemeClusters.NextBoundary(text, at));
                            Require(ime.TryGetImeTextRangeGeometry(at, end - at, false, out var value) && value.Rectangle.Height > 0,
                                "inserted grapheme has actual IME layout geometry");
                            var rectangle = document.GetPositionAtOffset(at, LogicalDirection.Forward)!.GetCharacterRect(LogicalDirection.Forward);
                            Require(!rectangle.IsEmpty && double.IsFinite(rectangle.X) && double.IsFinite(rectangle.Y), "public caret geometry remains finite after rebasing");
                            geometry.Add(new { at, end, rectangle }); at = end;
                        }
                        status.Text = "局部文字、层级与选区检查通过。"; Capture("word"); Observe(view);
                    });
                    Attempt("undo-word", () => { Require(editor.Undo() && document.GetText() == before && Shape(document.Blocks) == shape, "one Undo restores the exact source hierarchy"); OwnersRemain(); });
                    Attempt("redo-word", () => { Require(editor.Redo() && document.GetText() == before[..start] + replacement + before[(start + 3)..], "one Redo restores the partial edit"); OwnersRemain(); });
                    Attempt("restore-before-insertion", () => Require(editor.Undo() && document.GetText() == before, "source restored for the next SDK operation"));
                    Attempt("empty-range-insertion", () =>
                    {
                        Observe(view); var source = Context(view); using var proposed = new NSMutableAttributedString(source.Context.AttributedString.Substring(start, 1));
                        proposed.Replace(new NSRange(0, proposed.Length), "新🙂"); Replace(source, start, 0, proposed);
                        Require(document.GetText() == before.Insert(start, "新🙂"), "zero-length SDK edit inserts inside the original owner"); OwnersRemain(); CheckPacket(start);
                    });
                    Attempt("undo-insertion", () => Require(editor.Undo() && document.GetText() == before && Shape(document.Blocks) == shape, "insertion Undo restores source hierarchy"));
                    if (model >= 5) Attempt("cross-owner-replacement", () =>
                    {
                        Observe(view); int end = paragraphs[^1].ContentStart.DocumentOffset + 5; var source = Context(view);
                        using var proposed = new NSMutableAttributedString(source.Context.AttributedString.Substring(start, end - start));
                        string[] words = ["甲一", "乙二", "丙三"];
                        for (int i = paragraphs.Length - 1; i >= 0; --i)
                        {
                            int from = Math.Max(start, paragraphs[i].ContentStart.DocumentOffset), to = Math.Min(end, paragraphs[i].ContentEnd.DocumentOffset);
                            proposed.Replace(new NSRange(from - start, to - from), words[i]);
                        }
                        Replace(source, start, end - start, proposed); OwnersRemain(); CheckPacket(start);
                        Require(new[] { "前缀甲一", "乙二", "丙三后缀2" }.SequenceEqual(paragraphs.Select(p => new TextRange(p.ContentStart, p.ContentEnd).Text)),
                            "cross-owner edit keeps each item or cell and both unselected edge runs");
                        Require(editor.Undo() && document.GetText() == before, "cross-owner edit has one Undo record");
                    });
                    Attempt("final-selection-and-readback", () =>
                    {
                        var source = Context(view); using var proposed = new NSMutableAttributedString(source.Context.AttributedString.Substring(start, 3));
                        proposed.Replace(new NSRange(0, proposed.Length), replacement); Replace(source, start, 3, proposed); OwnersRemain();
                        editor.Selection.Select(document.GetPositionAtOffset(start, LogicalDirection.Forward)!, document.GetPositionAtOffset(start + replacement.Length, LogicalDirection.Backward)!);
                        status.Text = failures.Count == 0 ? "文字替换、容器、撤销与选区检查通过。" : "检查发现问题，请查看记录。";
                        Capture("after"); Observe(view);
                    });
                }
            }
            if (!done) return;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { index, model, custom, software,
                passed = failures.Count == 0 && unavailable == 0, checks, failures, scenarios, focus, packets, geometry, foregroundUnavailable = unavailable,
                physicalInput = false, desktopScreenshot = false, actualWritingToolsService = false, renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD") }));
            owner.Close(); host.AllowQuit = true; app.Terminate(app);
        });
        app.Run(); return failures.Count == 0 && unavailable == 0 ? 0 : 1;
    }

    private static IEnumerable<DependencyObject> PartialNodes(DependencyObject value)
    {
        yield return value;
        IEnumerable<DependencyObject> children = value switch
        {
            FlowDocument d => d.Blocks, Section s => s.Blocks, Paragraph p => p.Inlines, Span s => s.Inlines,
            DocList l => l.ListItems, ListItem i => i.Blocks, Table t => t.RowGroups, TableRowGroup g => g.Rows,
            TableRow r => r.Cells, TableCell c => c.Blocks, _ => []
        };
        foreach (var child in children) foreach (var node in PartialNodes(child)) yield return node;
    }
}
