using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Interop;
using ObjCRuntime;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

// Own-process AppKit SDK and renderer checks, without device-event injection.
internal static class WindowEmbeddedObjectChecks
{
    private const string Body = "Miii中文🙂e\u0301";

    internal static int Run(string argument)
    {
        int index = int.Parse(argument[(argument.IndexOf('=') + 1)..]);
        if (index is < 0 or > 11) throw new ArgumentOutOfRangeException(nameof(argument));
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        string output = Environment.GetEnvironmentVariable("JALIUM_EMBEDDED_OBJECT_OUTPUT") ?? throw new InvalidOperationException("Output directory required.");
        Directory.CreateDirectory(output);
        bool software = index >= 6, custom = index % 6 >= 3;
        string kind = new[] { "owner", "overlay", "native" }[index % 3];
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        if (software) RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        else RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var editor = Editor(170); var reference = Editor(72);
        reference.IsReadOnly = true; reference.Document = new FlowDocument(new Paragraph(Styled(Body)));
        AutomationProperties.SetName(editor, "嵌入对象后的文字选区");
        AutomationProperties.SetName(reference, "单独文本参照");
        var content = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "文字范围与嵌入对象", FontSize = 18 });
        content.Children.Add(editor);
        content.Children.Add(new TextBlock { Text = "单独文本参照", FontSize = 14 });
        content.Children.Add(reference);
        var subject = new Border { Width = 360, Height = 360, Child = content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        subject.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        subject.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var area = new Canvas { Height = 400 }; var anchor = new Button { Content = "文字检查", Width = 100, Height = 40 };
        area.Children.Add(anchor);
        if (kind == "owner") { area.Children.Remove(anchor); area.Children.Add(subject); Canvas.SetLeft(subject, 5); Canvas.SetTop(subject, 5); }
        var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = "嵌入对象后的中文、斜体与 emoji", FontSize = 22, TextWrapping = TextWrapping.Wrap }); root.Children.Add(area);
        var owner = new Window { Title = "Jalium Window v300 · 文本投影", Width = kind == "owner" && !custom ? 760 : 420, Height = 540,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Application.Current!.MainWindow = owner; owner.Show(); owner.UpdateLayout();
        Popup? popup = null;
        if (kind != "owner")
        {
            Canvas.SetLeft(anchor, kind == "native" ? area.ActualWidth - 20 : 5); Canvas.SetTop(anchor, 5);
            popup = new Popup { Child = subject, PlacementTarget = anchor, Placement = PlacementMode.Right,
                PreferExternalWindow = kind == "native", ShouldConstrainToRootBounds = kind == "overlay", StaysOpen = true };
            popup.IsOpen = true; owner.UpdateLayout();
        }
        var checks = new List<string>(); var failures = new List<string>(); var focus = new List<object>(); var scenes = new List<object>();
        var names = new[] { "inline", "block", "nested" }; int next = 0, completed = 0; bool running = false, finished = false, foregroundQualified = true;
        Scene? scene = null; Window? surface = null; NSView? nativeView = null; var wait = Stopwatch.StartNew();
        void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(names[Math.Min(next - 1, names.Length - 1)] + ": " + message);
            checks.Add(message);
        }
        bool Observe(bool record = true)
        {
            var view = nativeView; var window = surface;
            int pid = Environment.ProcessId;
            int front = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier ?? -1;
            bool ready = app.Active && window?.IsActive == true && front == pid &&
                app.KeyWindow?.Handle == view?.Window?.Handle && app.KeyWindow?.FirstResponder?.Handle == view?.Handle;
            if (record)
            {
                foregroundQualified &= ready;
                focus.Add(new { processId = pid, frontmost = front, active = app.Active, logicalActive = window?.IsActive,
                    keyWindow = (long)(nint)(app.KeyWindow?.Handle ?? default), responder = (long)(nint)(app.KeyWindow?.FirstResponder?.Handle ?? default), ownResponder = (long)(nint)(view?.Handle ?? default), ready });
            }
            return ready;
        }
        void Finish()
        {
            if (finished) return;
            finished = true;
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                index, kind, custom, software, completed, expected = names.Length, passed = failures.Count == 0 && completed == names.Length,
                checks, failures, focus, scenes, requireForeground = true, foregroundQualified = foregroundQualified && focus.Count > 0,
                renderThreadOverride = Environment.GetEnvironmentVariable("JALIUM_RENDER_THREAD"), physicalInput = false, embeddedChildPainting = false,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(UIElement).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location, Assembly.GetExecutingAssembly().Location }
            }));
            popup?.SetCurrentValue(Popup.IsOpenProperty, false); owner.Close(); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        }
        void Next()
        {
            if (next == names.Length) { Finish(); return; }
            scene = Create(names[next++]); editor.Document = scene.Document;
            owner.UpdateLayout(); surface = Window.GetMacOSPhysicalWindow(editor)!; surface.UpdateLayout();
            app.Activate(); surface.Activate(); editor.Focus(); nativeView = Runtime.GetNSObject<NSView>(surface.Handle)!;
            nativeView.Window!.MakeKeyWindow(); nativeView.Window.MakeFirstResponder(nativeView);
            wait.Restart();
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(80), _ =>
        {
            if (finished || running || scene is null || nativeView is null || surface is null) return;
            if (!Observe(record: false) && wait.Elapsed < TimeSpan.FromSeconds(12)) return;
            running = true;
            try
            {
                Require(Observe(), "actual foreground, key window and responder agree");
                Require(surface.IsMacOSPopupHost == (kind == "native"), "the expected physical host owns the editor");
                editor.Selection.Select(scene.Target.ContentStart, scene.Target.ContentEnd); surface.UpdateLayout();
                var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(surface.ActualWidth * surface.DpiScale),
                    (int)Math.Ceiling(surface.ActualHeight * surface.DpiScale), surface.DpiScale,
                    drawing => { drawing.DrawRectangle(surface.Background, null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)); surface.Render(drawing); }, () => surface.Handle != 0);
                Require(bitmap is not null, "the normal backend renders the selected text");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                using (var file = File.Create(Path.Combine(output, names[next - 1] + ".png"))) encoder.Save(file);
                var source = ((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Text)!).Source;
                scenes.Add(new { name = names[next - 1], expectedText = scene.Expected, actualText = source.Text, selection = new[] { source.SelectionStart, source.SelectionLength } });
                Require(source.Text == scene.Expected && editor.Document.ContentEnd.DocumentOffset == scene.Expected.Length, "text services and logical positions share the object projection");
                int start = scene.Expected.IndexOf(Body, StringComparison.Ordinal);
                Require(source.SelectionStart == start && source.SelectionLength == Body.Length && editor.Selection.Text == Body, "selection after objects contains the intended UTF16 text");
                var styles = editor.GetAutomationTextStyles();
                Require(styles.Max(span => span.Start + span.Length) == scene.Expected.Length && styles.Any(span => span.Start == start && span.Length == Body.Length), "style spans remain aligned after objects");
                var referenceRun = reference.Document.EnumerateParagraphs().Single().Inlines.OfType<Run>().Single();
                var from = scene.Target.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                var plain = referenceRun.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Require(from.Height > 0 && plain.Height > 0, "both editors expose real text geometry");
                foreach (int end in new[] { 1, 4, 6, 8, 10 })
                {
                    var to = scene.Target.ContentStart.GetPositionAtOffset(end)!.GetCharacterRect(LogicalDirection.Forward);
                    var expected = referenceRun.ContentStart.GetPositionAtOffset(end)!.GetCharacterRect(LogicalDirection.Forward);
                    Require(Math.Abs((to.X - from.X) - (expected.X - plain.X)) < 0.5, "Miii, Chinese, emoji and combining-mark advances match the independent editor at " + end);
                }
                var coordinator = nativeView.WritingToolsCoordinator;
                Require(coordinator?.WeakDelegate is not null, "the eligible editor exposes the real Writing Tools delegate");
                var client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
                NSWritingToolsCoordinatorContext? context = null; int calls = 0;
                client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { ++calls; context = values.FirstOrDefault(); });
                Require(calls == 1 && context?.AttributedString.Value == scene.Expected, "SDK context preserves object slots and the full document");
                NSBezierPath[]? paths = null;
                client.RequestsBoundingBezierPaths(coordinator, new NSRange(start, 1), context!, values => paths = values);
                Require(paths is { Length: > 0 } && paths.All(path => path.Bounds.Width > 0), "SDK geometry resolves the M after embedded objects");
                NSTextPreview[]? previews = null;
                client.RequestsPreviewForTextAnimation(coordinator, NSWritingToolsCoordinatorTextAnimation.Remove, new NSRange(start, 4), context!, values => previews = values);
                Require(previews is { Length: > 0 } && previews[0].PreviewImage.Width > 0 && previews[0].PreviewImage.Height > 0, "SDK animation preview resolves the actual Miii glyphs after objects");
                using (var text = new NSString("验收🙂")) InsertText(nativeView.Handle, new ObjCRuntime.Selector("insertText:replacementRange:").Handle, text.Handle, new NSRange(nint.MaxValue, 0));
                string edited = scene.Expected.Replace(Body, "验收🙂", StringComparison.Ordinal);
                Require(source.Text == edited && scene.Objects.All(item => ReferenceEquals(item.GetFlowDocument(), editor.Document)), "native insertion edits the selected text while retaining embedded nodes");
                editor.Undo(); Require(source.Text == scene.Expected, "one Undo restores the object-aware projection");
                editor.Redo(); Require(source.Text == edited, "one Redo restores the native edit");
                editor.IsReadOnly = true;
                using (var text = new NSString("不可写")) InsertText(nativeView.Handle, new ObjCRuntime.Selector("insertText:replacementRange:").Handle, text.Handle, new NSRange(0, 1));
                Require(source.Text == edited, "read-only native insertion leaves the document intact"); editor.IsReadOnly = false;
                if (next == names.Length) CheckDecorationColors(reference, editor, nativeView, surface, output, Require);
                Require(Observe(), "foreground and responder remain current after editing");
                completed++;
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally { try { Next(); } finally { running = false; } }
        });
        app.BeginInvokeOnMainThread(Next);
        try { app.Run(); return failures.Count == 0 && completed == names.Length ? 0 : 1; }
        finally { timer.Invalidate(); owner.Close(); using var done = NSNotification.FromName(NSApplication.WillTerminateNotification, app); host.WillTerminate(done); app.Delegate = null!; }
    }

    private static RichTextBox Editor(double height) => new() { Height = height, Background = Brushes.White, Foreground = Brushes.Black, FontSize = 16, Padding = new Thickness(8) };
    private static Run Styled(string text) => new(text) { FontFamily = new FontFamily("Avenir Next Condensed"), FontSize = 29, FontStyle = FontStyles.Italic, FontWeight = FontWeights.Bold };
    private static void CheckDecorationColors(RichTextBox reference, RichTextBox editor, NSView view, Window surface, string output, Action<bool, string> require)
    {
        reference.IsReadOnly = false;
        reference.Language = XmlLanguage.GetLanguage("en-GB");
        require(reference.Focus(), "the independent editor accepts formatting focus");
        var source = ((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(reference)!.GetPattern(PatternInterface.Text)!).Source;
        var coordinator = view.WritingToolsCoordinator;
        require(coordinator?.WeakDelegate is not null, "the independent editor owns its own SDK coordinator");
        var client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
        NSWritingToolsCoordinatorContext? Context()
        {
            NSWritingToolsCoordinatorContext? result = null;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => result = values.FirstOrDefault());
            return result;
        }
        var context = Context();
        require(context?.AttributedString.Value == Body + "\n", "the native formatting context belongs to the independent editor");
        require((context?.AttributedString.GetAttribute("NSLanguage", 0, out _) as NSString)?.ToString() == "en-gb",
            "the SDK context preserves the independent editor language");
        const string changed = "颜色🙂";
        using var proposal = new NSMutableAttributedString(changed);
        using var underline = new NSString("NSUnderline"); using var strike = new NSString("NSStrikethrough");
        using var underlineColor = new NSString("NSUnderlineColor"); using var strikeColor = new NSString("NSStrikethroughColor");
        using var enabled = NSNumber.FromInt32(1);
        using var languageKey = new NSString("NSLanguage"); using var language = new NSString("ja-JP");
        var range = new NSRange(0, changed.Length);
        proposal.AddAttribute(underline, enabled, range); proposal.AddAttribute(strike, enabled, range);
        proposal.AddAttribute(underlineColor, NSColor.Red, range); proposal.AddAttribute(strikeColor, NSColor.Blue, range);
        proposal.AddAttribute(languageKey, language, range);
        NSAttributedString? accepted = null;
        client.ReplaceRange(coordinator, new NSRange(0, Body.Length), context!, proposal,
            NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => accepted = value);
        require(accepted?.Value == changed && source.Text == changed + "\n", "the SDK accepts a replacement with independent decoration colors");
        var style = reference.GetAutomationTextStyles().First().Style;
        require(style.Underline != 0 && style.Strikethrough != 0 && style.UnderlineColor == 0xffff0000u && style.StrikethroughColor == 0xff0000ffu,
            "managed underline red and strike blue survive the native proposal");
        require(style.Language == "ja-jp", "the imported run preserves the proposed language");
        var updated = Context();
        bool Color(string key, double red, double green, double blue)
        {
            using var color = (updated?.AttributedString.GetAttribute(key, 0, out _) as NSColor)?.UsingColorSpace(NSColorSpace.SRGBColorSpace);
            return color is not null && Math.Abs((double)color.RedComponent - red) < .005 && Math.Abs((double)color.GreenComponent - green) < .005 &&
                Math.Abs((double)color.BlueComponent - blue) < .005 && (double)color.AlphaComponent > .999;
        }
        require(updated?.AttributedString.Value == changed + "\n" && Color("NSUnderlineColor", 1, 0, 0) && Color("NSStrikethroughColor", 0, 0, 1),
            "a fresh SDK context returns both independent decoration colors");
        require((updated?.AttributedString.GetAttribute("NSLanguage", 0, out _) as NSString)?.ToString() == "ja-jp",
            "a fresh SDK context returns the imported run language");
        surface.UpdateLayout();
        var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(surface.ActualWidth * surface.DpiScale),
            (int)Math.Ceiling(surface.ActualHeight * surface.DpiScale), surface.DpiScale,
            drawing => { drawing.DrawRectangle(surface.Background, null, new Rect(0, 0, surface.ActualWidth, surface.ActualHeight)); surface.Render(drawing); }, () => surface.Handle != 0)
            ?? throw new InvalidOperationException("Decoration renderer readback unavailable.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "decoration.png"))) encoder.Save(file);
        reference.Undo(); style = reference.GetAutomationTextStyles().First().Style;
        require(source.Text == Body + "\n" && style.Underline == 0 && style.Strikethrough == 0 && style.Language == "en-gb",
            "Undo restores the undecorated independent editor and its language");
        reference.IsReadOnly = true;
        require(editor.Focus(), "the original object-aware editor regains focus after formatting");
    }
    private sealed record Scene(FlowDocument Document, Run Target, TextElement[] Objects, string Expected);
    private static Scene Create(string kind)
    {
        var objects = new List<TextElement>(); var paragraph = new Paragraph(new Run("A")); var target = Styled(Body); string middle = "A";
        if (kind != "block") { var embedded = new InlineUIContainer(); paragraph.Inlines.Add(embedded); objects.Add(embedded); middle += "\uFFFC"; }
        paragraph.Inlines.Add(target); paragraph.Inlines.Add(new Run("Z")); middle += Body + "Z\n";
        var blocks = new List<Block>();
        if (kind != "inline") { var embedded = new BlockUIContainer(); blocks.Add(embedded); objects.Add(embedded); middle = "\uFFFC" + middle; }
        blocks.Add(paragraph); var document = new FlowDocument(new Paragraph(new Run("前缀")));
        if (kind == "nested")
        {
            var section = new Section(); foreach (var block in blocks) section.Blocks.Add(block);
            var table = new Table(); var group = new TableRowGroup(); var row = new TableRow(); row.Cells.Add(new TableCell(section)); group.Rows.Add(row); table.RowGroups.Add(group);
            var list = new DocList(); var item = new ListItem(); item.Blocks.Add(table); list.ListItems.Add(item); document.Blocks.Add(list); middle += "\t\n";
        }
        else foreach (var block in blocks) document.Blocks.Add(block);
        document.Blocks.Add(new Paragraph(new Run("末尾")));
        return new(document, target, objects.ToArray(), "前缀\n" + middle + "末尾\n");
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void InsertText(nint view, nint selector, nint text, NSRange range);
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}
