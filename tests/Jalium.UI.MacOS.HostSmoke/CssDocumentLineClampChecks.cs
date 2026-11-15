using AppKit;
using CoreGraphics;
using Jalium.UI;
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
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssDocumentLineClampChecks
{
    private const string Words = "MMMM MMMM MMMM MMMM MMMM MMMM M";
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;color:#146342;white-space:normal;text-autospace:no-autospace;width:150px;text-align:start;";
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Keyboard.Initialize();
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_LINE_CLAMP_BASELINE") == "1";
        var records = new List<object>(); var failed = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var scope in new[] { "paragraph", "section", "list", "item", "cell", "root" })
        foreach (var marker in new[] { "no-ellipsis", "ellipsis", "'++'", "auto" })
        {
            var indented = scope is "list" or "item";
            var first = indented ? "MMMM MMMM" : "MMMM MMMM MMMM";
            var last = !indented && marker == "'++'" ? "MMMM MMMM" : first;
            var markerText = marker == "ellipsis" ? "…" : marker == "'++'" ? "++" : "";
            var style = marker == "auto" ? "line-clamp:auto no-ellipsis;height:48px;" : $"line-clamp:2 {marker};";
            using var actual = Sample(scope, Words, style); Layout(actual.Canvas, 240, 160);
            var pixels = Capture(context, actual.Canvas, engine, 240, 160, 1); Save(pixels, 240, 160, $"clamp-{engine}-{scope}-{marker.Replace("'", "")}.png");
            using var reference = Sample(scope, first + "\n" + last, marker == "auto" ? "height:48px;" : "");
            Css.SetStyle(reference.Source, "white-space:pre;"); Layout(reference.Canvas, 240, 160);
            if (markerText.Length > 0)
            {
                var text = new TextBlock { Text = markerText }; Css.SetStyle(text, Font + "white-space:pre;"); reference.Canvas.Children.Add(text);
                var at = reference.Body.Paragraphs[0].TransformToAncestor(reference.Canvas);
                var advance = new FormattedText(last, "Courier New", 16); TextMeasurement.MeasureText(advance);
                Canvas.SetLeft(text, at.X + advance.WidthIncludingTrailingWhitespace); Canvas.SetTop(text, at.Y + 24); Layout(reference.Canvas, 240, 160);
            }
            var expected = Capture(context, reference.Canvas, engine, 240, 160, 1); Save(expected, 240, 160, $"reference-{engine}-{scope}-{marker.Replace("'", "")}.png");
            var equal = pixels.SequenceEqual(expected); if (!equal) failed++;
            if (string.Concat(actual.Source.Inlines.OfType<Run>().Select(run => run.Text)) != Words || !ReferenceEquals(actual.Source.Inlines, actual.Body.Paragraphs[0].Inlines)) throw new InvalidOperationException("Original document source changed");
            records.Add(new { engine = engine.ToString(), scope, marker, samePixelsAsExplicitPrefixAndMarker = equal });
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        {
            using var actual = Sample("paragraph", Words, "line-clamp:2 no-ellipsis;orphans:1;widows:1;");
            using var reference = Sample("paragraph", "MMMM\nMMMM", "white-space:pre;orphans:1;widows:1;");
            foreach (var item in new[] { actual, reference }) Css.SetStyle(item.Document, Font + "columns:2;column-gap:20px;");
            Layout(actual.Canvas, 240, 160); Layout(reference.Canvas, 240, 160);
            var pixels = Capture(context, actual.Canvas, engine, 240, 160, 1); var expected = Capture(context, reference.Canvas, engine, 240, 160, 1);
            Save(pixels, 240, 160, $"columns-{engine}.png"); Save(expected, 240, 160, $"columns-reference-{engine}.png");
            var equal = pixels.SequenceEqual(expected); if (!equal) failed++;
            records.Add(new { engine = engine.ToString(), scope = "paragraph-before-columns", samePixelsAsExplicitPrefixAndMarker = equal });
        }
        Reader(context, baseline);
        Write("native.json", new { baseline, count = records.Count, failed, cases = records });
        return baseline ? 0 : failed == 0 ? 0 : 1;
    }

    private sealed record DocumentSample(FlowDocument Document, Paragraph Source, FlowDocumentPresentation Body, Canvas Canvas) : IDisposable
    { public void Dispose() => Body.Dispose(); }
    private static DocumentSample Sample(string scope, string words, string style)
    {
        var source = new Paragraph(new Run(words[..Math.Min(7, words.Length)])); source.Inlines.Add(new Run(words[Math.Min(7, words.Length)..]));
        Block child = source; DependencyObject target = source;
        if (scope == "section") { var section = new Section(source); child = section; target = section; }
        if (scope is "list" or "item") { var item = new ListItem(source); var list = new DocList(); list.ListItems.Add(item); child = list; target = scope == "list" ? list : item; }
        if (scope == "cell")
        { var cell = new TableCell(source); var row = new TableRow(); row.Cells.Add(cell); var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.RowGroups.Add(group); child = table; target = cell; }
        var doc = new FlowDocument { PagePadding = new(0), FontFamily = "Courier New", FontSize = 16, LineHeight = 24 };
        doc.Blocks.Add(child); Css.SetStyle(doc, Font); Css.SetStyleSheet(doc, "Paragraph,Section,List,ListItem,Table,TableCell{margin:0;padding:0;border:0;}Table{border-spacing:0;}");
        if (scope == "root") target = doc;
        Css.SetStyle(target, (ReferenceEquals(target, doc) ? Font : "") + style);
        var body = new FlowDocumentPresentation(); body.SetDocument(doc); var canvas = new Canvas { Background = Brushes.White }; canvas.Children.Add(body); Canvas.SetLeft(body, 16); Canvas.SetTop(body, 16);
        body.Width = 150; return new(doc, source, body, canvas);
    }

    private static void Reader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 320, 375, 720 })
        {
            var height = width == 320 ? 420 : 640;
            const string first = "在山林与河岸之间，";
            const string second = "慢慢读完一段旅程。把脚步交给小路，把目光留给沿途的风景。拐过石桥，又看见一盏温暖的灯。每一行都值得从容读完，也值得留下一点期待。";
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("把长文留给想读的人", "title")); root.Children.Add(Label("先看摘要，展开后继续阅读。", "description"));
            var note = new StackPanel(); Css.SetClass(note, "note"); root.Children.Add(note); note.Children.Add(Label("阅读随记", "caption"));
            var a = new Run(first); var b = new Run(second); var source = new Paragraph(a); source.Inlines.Add(b);
            var following = new Paragraph(new Run("另起一段，把今天的发现留给下一次阅读。")); var section = new Section(source); section.Blocks.Add(following);
            var doc = new FlowDocument { PagePadding = new(0), FontFamily = "PingFang SC", FontSize = 18, LineHeight = 28 }; doc.Blocks.Add(section);
            Css.SetStyle(doc, "font-family:'PingFang SC';font-size:18px;line-height:28px;color:#233c32;white-space:normal;text-wrap:balance;");
            Css.SetStyleSheet(doc, "Section,Paragraph{margin:0;padding:0;border:0;}Paragraph+Paragraph{margin-top:12px;}");
            using var sample = new FlowDocumentPresentation(); sample.SetDocument(doc); Css.SetClass(sample, "sample"); note.Children.Add(sample);
            note.Children.Add(Label("展开后可阅读并选择完整原文。", "explanation"));
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var toggle = Action("展开全文", 0); var edit = Action("补写一句", 1); var restore = Action("恢复原文", 2);
            actions.Children.Add(toggle); actions.Children.Add(edit); actions.Children.Add(restore);
            var status = Label("", "status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); root.Children.Add(status);
            Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root { display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:#ffffff; }
                .title { display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px; }
                .description { display:block;margin:0 0 20px; }
                .note { display:block;padding:16px;background:#eef5f0;margin:0 0 20px; }
                .caption { display:block;margin:0 0 12px;font-weight:600; }
                .sample { display:block;font-size:18px;line-height:28px;max-width:440px;margin:0 0 16px;text-wrap:balance; }
                .source { display:inline; }
                .explanation { display:block; }
                .actions { display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px; }
                .action { display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342; }
                .action:hover { background:#c5e2d1; }
                .action:active { background:#b4d7c2; }
                .action:focus-visible { border-color:#146342; }
                .status { display:block;font-size:16px;line-height:24px; }
                """));
            var expanded = false;
            void Apply()
            {
                Css.SetStyle(section, expanded ? "line-clamp:none;" : "line-clamp:3;");
                ((TextBlock)toggle.Child!).Text = expanded ? "收起全文" : "展开全文";
                AutomationProperties.SetName(toggle, expanded ? "收起全文" : "展开全文");
                status.Text = expanded ? "已展开，可阅读完整内容。" : "已收起，显示三行摘要。";
            }
            toggle.Activate = () => { expanded = !expanded; Apply(); };
            edit.Activate = () => { b.Text += "走到下一处风景时，再补写一句。"; Apply(); };
            restore.Activate = () => { a.Text = first; b.Text = second; expanded = false; Apply(); };
            foreach (var action in new[] { toggle, edit, restore })
            {
                action.MouseLeftButtonUp += (_, _) => action.Activate?.Invoke();
                action.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { action.Activate?.Invoke(); e.Handled = true; } };
            }
            Apply(); var viewport = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "expanded", "edited", "collapsed", "2x", "restored" })
            {
                if (phase == "expanded") Press(toggle, Key.Enter);
                if (phase == "edited") ((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                if (phase == "collapsed") Press(toggle, Key.Space);
                if (phase == "restored") ((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                var scale = phase == "2x" ? 2 : 1; Layout(viewport, width, height);
                if (!baseline && phase == "initial")
                {
                    if (!toggle.Focus() || !toggle.MoveFocus(FocusNavigationDirection.Next) || !edit.IsFocused ||
                        !edit.MoveFocus(FocusNavigationDirection.Next) || !restore.IsFocused || !restore.MoveFocus(FocusNavigationDirection.Previous) || !edit.IsFocused)
                        throw new InvalidOperationException("Managed reader focus traversal differs");
                }
                Keyboard.ClearFocus(); Layout(viewport, width, height);
                var pixels = Capture(context, viewport, engine, width, height, scale); Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                var lineCount = sample.Paragraphs.Where(text => text.IsVisible).Sum(text => text.GetDocumentLines(text.ActualWidth).Count);
                if (!baseline)
                {
                    if (!expanded && lineCount != 3 || expanded && lineCount <= 3) throw new InvalidOperationException("Reader line count differs");
                    var provider = (ITextProvider)sample.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                    if (provider.DocumentRange.GetText(-1) != doc.GetText() || a.Text != first || !b.Text.StartsWith(second) || !ReferenceEquals(source.Inlines, sample.Paragraphs[0].Inlines)) throw new InvalidOperationException("Original reader text changed");
                    if (!expanded && provider.DocumentRange.FindText("另起一段", false, false)!.GetBoundingRectangles().Length != 0) throw new InvalidOperationException("Suppressed paragraph has visible accessibility bounds");
                    foreach (var action in new[] { toggle, edit, restore })
                    {
                        var at = action.TransformToAncestor(root);
                        if (action.ActualHeight < 44 || at.X < 19 || at.X + action.ActualWidth > width - 19 || !action.Focusable || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName())) throw new InvalidOperationException("Reader action layout or semantics differ");
                    }
                    if (AutomationProperties.GetLiveSetting(status) != AutomationLiveSetting.Polite) throw new InvalidOperationException("Reader live status differs");
                    if (phase == "initial") initial = pixels;
                    if (phase == "restored" && !pixels.SequenceEqual(initial!)) throw new InvalidOperationException("Restored reader pixels differ");
                }
                records.Add(new { engine = engine.ToString(), width, height, phase, expanded, visibleTextLines = lineCount, viewport.ExtentHeight, viewport.ViewportHeight });
            }
            viewport.ScrollToBottom(); Layout(viewport, width, height);
            if (!baseline)
            {
                var at = restore.TransformToAncestor(viewport);
                if (at.Y < 0 || at.Y + restore.ActualHeight > height) throw new InvalidOperationException("Scrolled reader action is not reachable");
            }
            Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-scrolled.png");
        }
        Write("reader.json", new { baseline, count = records.Count, cases = records });
    }

    private static void Press(ClampAction action, Key key)
    {
        if (!action.Focus()) throw new InvalidOperationException("Reader action could not receive managed focus");
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        action.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Clamp action key unhandled");
    }
    private sealed class ClampAction : Border { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new ClampActionPeer(this); }
    private sealed class ClampActionPeer(ClampAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "ClampAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static ClampAction Action(string text, int tabIndex)
    {
        var result = new ClampAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex); AutomationProperties.SetName(result, text); return result;
    }
    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Requested paragraph renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        try { root.Render(drawing); } finally { drawing.Pop(); target.EndDraw(); }
        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Paragraph readback failed");
        return pixels;
    }
    private static void Write(string name, object value)
    { if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_LINE_CLAMP_EVIDENCE_DIR") is { Length: > 0 } path) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value)); } }
    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_LINE_CLAMP_EVIDENCE_DIR") is not { Length: > 0 } path) return;
        Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name)); encoder.Save(file);
    }
    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); }
    }
}
