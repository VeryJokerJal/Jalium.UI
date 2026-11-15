using AppKit;
using CoreGraphics;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssRichTextBoxChecks
{
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;white-space:normal;";
    private static readonly string[] Modes = ["inline", "block", "inline-block", "flow-root", "flex", "inline-flex", "grid", "inline-grid", "table", "inline-table", "table-row", "table-cell", "contents", "none"];
    private static readonly string[] Styles = ["display:block;padding:8px;border:2px solid #146342;border-radius:5px;background:#eef5f0;margin:4px 7px;", "display:inline;box-decoration-break:clone;padding:3px;border:1px solid #146342;", "display:grid;width:140px;grid-template-columns:50px 70px;gap:7px;padding:4px;", "display:flex;width:160px;gap:8px;justify-content:space-between;", "display:inline-block;width:50%;box-sizing:border-box;border:3px solid #146342;padding:5px;", "display:block;font-size:21px;line-height:1.5;letter-spacing:1px;", "display:block;position:relative;left:9px;top:6px;", "display:contents;padding:30px;border:9px solid red;background:red;"];

    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Keyboard.Initialize();
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_RICH_TEXT_BOXES_BASELINE") == "1";
        var filter = Environment.GetEnvironmentVariable("JALIUM_CSS_RICH_TEXT_BOXES_CASES")?.Split(',');
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (var target in new[] { "run", "span", "paragraph", "section", "document" })
            {
                foreach (var mode in Modes)
                {
                    using var scene = new Scene(target); scene.Style($"display:{mode};grid-template-columns:32px 48px;gap:5px;");
                    Compare(engine, target + "-" + mode, scene);
                }
                for (var i = 0; i < Styles.Length; i++)
                {
                    using var scene = new Scene(target); scene.Style(Styles[i]); Compare(engine, target + "-style-" + i, scene);
                }
                foreach (var kind in new[] { "selection", "edited", "restore", "unicode", "columns", "clamp", "wrap" })
                {
                    using var scene = new Scene(target); scene.Style("display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;background:#eef5f0;");
                    if (kind == "selection") scene.Select(2, 7);
                    if (kind == "edited") { scene.Layout(); scene.Runs[1].Text = "BX "; }
                    if (kind == "restore") { scene.Style("display:grid;padding:8px;grid-template-columns:32px 48px;gap:5px;"); scene.Layout(); scene.Style(""); }
                    if (kind == "unicode") scene.Words(["甲乙e\u0301 ", "丙丁 ", "👨‍👩‍👧‍👦 ", "戊己"]);
                    if (kind is "columns" or "clamp" or "wrap")
                    {
                        scene.Words(["AA BB CC ", "DD EE FF ", "GG HH II ", "JJ KK LL MM NN OO"]);
                        scene.RootStyle(kind == "columns" ? "display:block;columns:2;column-gap:12px;height:48px;column-fill:auto;" : kind == "clamp" ? "display:block;line-clamp:2 no-ellipsis;width:90px;" : "display:block;width:90px;");
                    }
                    Compare(engine, target + "-" + kind, scene);
                }
            }
        Write("native.json", new { baseline, count = records.Count, failed, cases = records });
        if (!baseline && filter is null) Reader(context);
        return baseline || failed == 0 ? 0 : 1;

        void Compare(RenderingEngine engine, string kind, Scene scene)
        {
            if (filter is not null && !filter.Any(kind.Contains)) return;
            scene.Layout();
            var actual = Capture(context, CanvasFor(scene.Editor), engine, 280, 280, 1);
            var expected = Capture(context, CanvasFor(scene.Reference), engine, 280, 280, 1);
            var differentPixels = 0; var maxChannelDelta = 0;
            for (var pixel = 0; pixel < actual.Length; pixel += 4)
            {
                var differs = false;
                for (var channel = 0; channel < 4; channel++)
                { var delta = Math.Abs(actual[pixel + channel] - expected[pixel + channel]); differs |= delta != 0; maxChannelDelta = Math.Max(maxChannelDelta, delta); }
                if (differs) differentPixels++;
            }
            var height = (double)typeof(RichTextBox).GetMethod("GetVerticalScrollExtentHeight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scene.Editor, [24d])!;
            var samePixels = actual.SequenceEqual(expected);
            var textProvider = (ITextProvider)scene.Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
            var hasTextBounds = textProvider.DocumentRange.GetBoundingRectangles().Length > 0;
            // Independently protect the document-root regression. Non-root
            // fragmentation combinations retain their separate conformance audit.
            bool? expectedTextBounds = kind.StartsWith("document-", StringComparison.Ordinal)
                ? kind != "document-none" : kind == "section-none" ? false : null;
            // This fixture has two 24px text rows. Bounds alone can survive
            // a collapsed source box, so protect the scroll/paint extent too.
            var expectedRootMinimumHeight = kind.StartsWith("document-", StringComparison.Ordinal) && kind != "document-none" ? 48 : 0;
            double[]? retainedPrefixBounds = null;
            var retainedPrefixInkPixels = 0;
            bool? retainedPrefixVisible = null;
            if (kind == "section-clamp")
            {
                retainedPrefixBounds = textProvider.DocumentRange.FindText("AA", false, false)?.GetBoundingRectangles() ?? [];
                // These are canvas coordinates at 1x. Exclude the rectangle's
                // edges so an empty inline border cannot stand in for glyphs.
                foreach (var rectangle in retainedPrefixBounds.Chunk(4))
                    for (var y = Math.Max(0, (int)Math.Ceiling(rectangle[1] + 2)); y < Math.Min(280, (int)Math.Floor(rectangle[1] + rectangle[3] - 2)); y++)
                    for (var x = Math.Max(0, (int)Math.Ceiling(rectangle[0] + 4)); x < Math.Min(280, (int)Math.Floor(rectangle[0] + rectangle[2] - 2)); x++)
                    {
                        var at = (y * 280 + x) * 4;
                        if (actual[at] < 180 && actual[at + 1] < 180 && actual[at + 2] < 180) retainedPrefixInkPixels++;
                    }
                retainedPrefixVisible = retainedPrefixBounds.Length > 0 && retainedPrefixInkPixels > 0;
            }
            var passed = samePixels && Math.Abs(height - scene.Reference.ExtentHeight) < .0001 &&
                (expectedTextBounds is null || hasTextBounds == expectedTextBounds) && height >= expectedRootMinimumHeight &&
                retainedPrefixVisible != false && (kind != "section-clamp" || Math.Abs(height - 48) < .0001);
            if (!passed) failed++;
            Save(actual, 280, 280, $"editor-{engine}-{kind}.png"); Save(expected, 280, 280, $"reference-{engine}-{kind}.png");
            scene.OriginalNodes();
            records.Add(new { engine = engine.ToString(), kind, passed, samePixels, hasTextBounds, expectedTextBounds, expectedRootMinimumHeight, retainedPrefixVisible, retainedPrefixInkPixels, retainedPrefixBounds, differentPixels, maxChannelDelta, height, referenceHeight = scene.Reference.ExtentHeight });
        }
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Run[] Runs = [new("AA "), new("BB "), new("CC "), new("DD"), new("ZZ")];
        internal readonly Span Span;
        internal readonly Paragraph Paragraph;
        internal readonly Section Section;
        internal readonly FlowDocument Document;
        internal readonly RichTextBox Editor;
        internal readonly FlowDocumentPresentation Reference = new() { ClipToBounds = true };
        private readonly DependencyObject _target;
        private readonly Dictionary<TextElement, TextElement?> _parents = [];
        internal Scene(string target)
        {
            Span = new(Runs[1]); Span.Inlines.Add(Runs[2]);
            Paragraph = new(Runs[0]); Paragraph.Inlines.Add(Span); Paragraph.Inlines.Add(Runs[3]);
            Section = new(Paragraph); Section.Blocks.Add(new Paragraph(Runs[4]));
            Document = new(Section) { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
            Css.SetStyle(Document, Font); Css.SetStyleSheet(Document, "Paragraph,Section{margin:0;padding:0;border:0;}");
            var selection = new SolidColorBrush(Color.FromArgb(96, 20, 99, 66));
            Editor = new(Document) { Width = 180, Height = 240, Padding = new(0), BorderThickness = new(0), Background = Brushes.White, Template = null, IsReadOnly = true, SelectionBrush = selection };
            Reference.SetDocument(Document); Reference.SetViewportWidth(180); Reference.SelectionBrush = selection;
            _target = target switch { "run" => Runs[1], "span" => Span, "paragraph" => Paragraph, "section" => Section, _ => Document };
            foreach (var node in Runs.Cast<TextElement>().Append(Span).Append(Paragraph).Append(Section)) _parents[node] = node.Parent;
        }
        internal void Style(string style) => Css.SetStyle(_target, ReferenceEquals(_target, Document) ? Font + style : style);
        internal void RootStyle(string style) => Css.SetStyle(Document, Font + style);
        internal void Words(string[] words) { for (var i = 0; i < words.Length; i++) Runs[i].Text = words[i]; }
        internal void Select(int start, int length)
        {
            ((IImeSupport)Editor).TrySetImeSelection(start, length);
            var selection = new TextSelection(Document.ContentStart, Document.ContentStart); selection.SelectOffsets(Document, start, length); Reference.SetSelection(selection);
        }
        internal void Layout()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
                Editor.Measure(new(180, 240)); Editor.Arrange(new(0, 0, 180, 240));
                Reference.Measure(new(180, double.PositiveInfinity)); Reference.Arrange(new(0, 0, 180, 240));
            }
        }
        internal void OriginalNodes()
        {
            if (!ReferenceEquals(Editor.Document, Document) || _parents.Any(pair => !ReferenceEquals(pair.Key.Parent, pair.Value)) || !ReferenceEquals(Span.Inlines[0], Runs[1]))
                throw new InvalidOperationException("Editor changed original source nodes");
        }
        public void Dispose() { Keyboard.ClearFocus(); Reference.Dispose(); }
    }

    private static Canvas CanvasFor(FrameworkElement root)
    {
        var canvas = new Canvas { Background = Brushes.White };
        var clip = new Border { Width = 180, Height = 240, ClipToBounds = true, Child = root };
        canvas.Children.Add(clip); Canvas.SetLeft(clip, 16); Canvas.SetTop(clip, 16);
        for (var i = 0; i < 3; i++) { canvas.Measure(new(280, 280)); canvas.Arrange(new(0, 0, 280, 280)); }
        return canvas;
    }

    private static void Reader(RenderContext context)
    {
        var records = new List<object>();
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in new[] { 320, 375, 720 })
        {
            var height = width == 320 ? 420 : 640;
            const string original = "留一段给山风，也留一段给自己。";
            var run = new Run(original); var emphasis = new Span(run);
            var first = new Paragraph(new Run("沿途的发现值得慢慢记录。")); first.Inlines.Add(emphasis); first.Inlines.Add(new Run("再继续往前走。"));
            var second = new Paragraph(new Run("记录光影的变化，也记录走过的路。"));
            var third = new Paragraph(new Run("把小小的发现留在这里，下一次还可以继续补写。"));
            var document = new FlowDocument { PagePadding = new(0), FontFamily = "PingFang SC", FontSize = 18, LineHeight = 28 };
            document.Blocks.Add(first); document.Blocks.Add(second); document.Blocks.Add(third);
            Css.SetStyle(document, "display:block;font-family:'PingFang SC';font-size:18px;line-height:28px;color:#233c32;white-space:normal;");
            Css.SetStyleSheet(document, "Paragraph{margin:0 0 12px;padding:0;border:0;}");
            var editor = new RichTextBox(document) { Template = null, Background = Brushes.White, SelectionBrush = new SolidColorBrush(Color.FromArgb(96, 20, 99, 66)) };
            Css.SetClass(editor, "editor"); AutomationProperties.SetName(editor, "阅读笔记"); KeyboardNavigation.SetTabIndex(editor, 0);
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            var title = Label("把想法写进结构", "title"); AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
            root.Children.Add(title); root.Children.Add(Label("展开重点继续写，原文与格式都保留。", "description")); root.Children.Add(editor);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var toggle = Action("展开重点", 1); var append = Action("补写一句", 2); var undo = Action("撤销", 3); var redo = Action("重做", 4);
            foreach (var action in new[] { toggle, append, undo, redo }) actions.Children.Add(action);
            var status = Label("正文可以继续写，重点也可以单独展开。", "status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root{display:block;padding:20px;box-sizing:border-box;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:white;}
                .title{display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px;}
                .description{display:block;margin:0 0 16px;}
                .editor{display:block;box-sizing:border-box;max-width:520px;height:184px;min-height:120px;padding:10px;border:2px solid #708f7e;border-radius:4px;background:white;caret-color:#146342;caret-animation:manual;margin:0 0 16px;}
                .editor:focus-visible{border-color:#146342;}
                .actions{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px;}
                .action{display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342;}
                .action:hover{background:#c5e2d1;}.action:active{background:#b4d7c2;}.action:focus-visible{border-color:#146342;}
                .action:disabled{background:#edf4ef;color:#526e5f;}
                .status{display:block;max-width:520px;min-height:48px;font-size:16px;line-height:24px;}
                """));
            var shown = false;
            void Refresh()
            {
                Css.SetStyle(emphasis, shown ? "display:block;margin:10px 0;padding:10px;border:1px solid #aac6b6;background:#eef5f0;" : "display:inline;padding:2px 4px;border-bottom:1px solid #aac6b6;background:#eef5f0;");
                ((TextBlock)toggle.Child!).Text = shown ? "收起重点" : "展开重点"; AutomationProperties.SetName(toggle, shown ? "收起重点" : "展开重点");
                undo.IsEnabled = editor.CanUndo; redo.IsEnabled = editor.CanRedo;
            }
            toggle.Activate = () => { shown = !shown; Refresh(); status.Text = shown ? "重点已展开，接着写下新的发现。" : "重点已收起，原文继续连读。"; editor.Focus(); };
            append.Activate = () =>
            {
                var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                provider.DocumentRange.FindText("山风", false, false)!.Select(); editor.Focus();
                var text = new TextCompositionEventArgs(UIElement.TextInputEvent, "春风🙂", 1); editor.RaiseEvent(text);
                if (!text.Handled) throw new InvalidOperationException("Editor text input unhandled");
                Refresh(); status.Text = "已补写春风与笑脸，可以撤销再试。";
            };
            undo.Activate = () => { if (!editor.Undo()) throw new InvalidOperationException("Editor undo unavailable"); Refresh(); status.Text = "已撤销补写，原文与格式保留。"; editor.Focus(); };
            redo.Activate = () => { if (!editor.Redo()) throw new InvalidOperationException("Editor redo unavailable"); Refresh(); status.Text = "已重做补写，继续记录这一刻。"; editor.Focus(); };
            foreach (var action in new[] { toggle, append, undo, redo })
            {
                action.GotKeyboardFocus += (_, _) => action.BringIntoView();
                action.MouseLeftButtonUp += (_, _) => { if (action.IsEnabled) action.Activate?.Invoke(); };
                action.PreviewKeyDown += (_, e) => { if (action.IsEnabled && e.Key is Key.Enter or Key.Space) { action.Activate?.Invoke(); e.Handled = true; } };
            }
            editor.GotKeyboardFocus += (_, _) => editor.BringIntoView();
            Refresh(); editor.IsUndoEnabled = false; editor.IsUndoEnabled = true; Refresh();
            var viewport = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            byte[]? initial = null;
            foreach (var phase in new[] { "initial", "selected", "expanded", "typed", "undo", "redo", "2x", "restored" })
            {
                Layout(viewport, width, height);
                if (phase == "initial")
                {
                    if (!editor.Focus() || !editor.MoveFocus(FocusNavigationDirection.Next) || !toggle.IsFocused || !toggle.MoveFocus(FocusNavigationDirection.Next) || !append.IsFocused)
                        throw new InvalidOperationException("Editor managed Tab order differs");
                    editor.Focus(); Layout(viewport, width, height);
                    Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-focused.png");
                    Keyboard.ClearFocus(); viewport.ScrollToTop();
                }
                if (phase == "selected")
                {
                    editor.Focus(); ((ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).DocumentRange.FindText("山风", false, false)!.Select();
                    if (editor.Selection.Text != "山风") throw new InvalidOperationException("Editor original selection differs");
                }
                if (phase == "expanded") Press(toggle, Key.Enter);
                if (phase == "typed") Invoke(append);
                if (phase == "undo") Invoke(undo);
                if (phase == "redo") Invoke(redo);
                if (phase == "restored")
                {
                    Invoke(undo); shown = false; Refresh(); editor.IsUndoEnabled = false; editor.IsUndoEnabled = true; Refresh();
                    ((IImeSupport)editor).TrySetImeSelection(0, 0); Keyboard.ClearFocus(); viewport.ScrollToTop();
                    status.Text = "正文可以继续写，重点也可以单独展开。";
                }
                Layout(viewport, width, height);
                if (editor.IsFocused)
                {
                    editor.ScrollToAutomationOffset(((IAutomationTextViewSource)editor.GetAutomationPeer()!).CaretIndex); Layout(viewport, width, height);
                    var caret = editor.GetImeCaretRectangle(); var bounds = editor.AutomationTextViewport;
                    if (caret.Top < bounds.Top - .01 || caret.Bottom > bounds.Bottom + .01) throw new InvalidOperationException("Editor caret outside its viewport");
                }
                var provider = (ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                var exposed = provider.DocumentRange.GetText(-1);
                if (!exposed.Contains(run.Text) || !ReferenceEquals(run, emphasis.Inlines[0]) || !ReferenceEquals(first, document.Blocks[0]) || title.GetAutomationPeer()!.GetHeadingLevel() != AutomationHeadingLevel.Level1 || editor.GetAutomationPeer()!.GetChildren().Count != 0)
                    throw new InvalidOperationException($"Editor semantics differ: exposed={exposed.Contains(run.Text)}, run={ReferenceEquals(run, emphasis.Inlines[0])}, paragraph={ReferenceEquals(first, document.Blocks[0])}, heading={title.GetAutomationPeer()!.GetHeadingLevel()}, children={editor.GetAutomationPeer()!.GetChildren().Count}");
                if ((phase is "typed" or "redo" or "2x") && !run.Text.Contains("春风🙂") || (phase is "undo" or "restored") && run.Text != original)
                    throw new InvalidOperationException("Editor edit history differs");
                foreach (var action in new[] { toggle, append, undo, redo })
                {
                    var at = action.TransformToAncestor(root);
                    if (action.ActualHeight < 44 || at.X < 19 || at.X + action.ActualWidth > width - 19 || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button)
                        throw new InvalidOperationException("Editor action bounds or semantics differ");
                }
                var scale = phase == "2x" ? 2 : 1; var pixels = Capture(context, viewport, engine, width, height, scale);
                Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                if (phase == "initial") initial = pixels;
                if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored editor pixels differ");
                if (viewport.ExtentHeight > height)
                {
                    viewport.ScrollToBottom(); Layout(viewport, width, height); var at = redo.TransformToAncestor(viewport);
                    if (at.Y < 0 || at.Y + redo.ActualHeight > height) throw new InvalidOperationException("Editor actions unreachable after scroll");
                    Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-{phase}-bottom.png");
                    viewport.ScrollToTop(); Layout(viewport, width, height);
                }
                records.Add(new { engine = engine.ToString(), width, height, phase, shown, text = run.Text, editor.VerticalOffset, exposureMatches = exposed.Contains(run.Text), undo = editor.CanUndo, redo = editor.CanRedo });
            }
            Keyboard.ClearFocus();
        }
        Write("reader.json", new { count = records.Count, cases = records });
    }

    private static TextBlock Label(string text, string css) { var label = new TextBlock { Text = text }; Css.SetClass(label, css); return label; }
    private static DisplayAction Action(string text, int index)
    {
        var action = new DisplayAction { Child = Label(text, ""), Focusable = true };
        Css.SetClass(action, "action"); AutomationProperties.SetName(action, text); KeyboardNavigation.SetTabIndex(action, index); return action;
    }
    private static void Invoke(DisplayAction action) => ((IInvokeProvider)action.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void Press(DisplayAction action, Key key)
    {
        if (!action.Focus()) throw new InvalidOperationException("Editor action could not receive focus");
        var args = new KeyEventArgs(UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 1); action.RaiseEvent(args);
        if (!args.Handled) throw new InvalidOperationException("Editor action key unhandled");
    }
    private sealed class DisplayAction : Border
    {
        internal System.Action? Activate;
        protected override AutomationPeer OnCreateAutomationPeer() => new DisplayActionPeer(this);
    }
    private sealed class DisplayActionPeer(DisplayAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() { if (!owner.IsEnabled) throw new InvalidOperationException("Editor action disabled"); owner.Activate?.Invoke(); }
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var i = 0; i < 4; i++) { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); }
    }

    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok)
            throw new InvalidOperationException("Requested editor renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation();
        target.BeginDraw(); target.Clear(1, 1, 1, 1); drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale)
            throw new InvalidOperationException("Editor readback failed");
        return pixels;
    }
    private static void Write(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_RICH_TEXT_BOXES_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value));
    }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_RICH_TEXT_BOXES_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name)); encoder.Save(file);
    }
}
