#pragma warning disable WPF0001
using AppKit;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;

namespace Jalium.UI.MacOS;

// Actual SDK coordinator callbacks and renderer readbacks in this process.
// No input injection, system preference writes, or Apple Intelligence service.
internal static class WindowWritingToolsZeroInlineChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 24) return 2;
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        string output = Environment.GetEnvironmentVariable("JALIUM_WRITING_ZERO_OUTPUT")!;
        Directory.CreateDirectory(output);
        bool custom = index % 2 == 1, software = index % 4 >= 2; int model = index / 4;
        bool foreground = true; // Production text-service adapters require actual activation.
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        var first = new Run("Miii") { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 23,
            FontStyle = FontStyles.Italic, FontStretch = FontStretches.Condensed };
        var last = new Run(" 中文🙂 e\u0301") { FontFamily = first.FontFamily, FontSize = first.FontSize, FontStyle = first.FontStyle };
        var paragraph = new Paragraph(first); var kept = new List<Inline>();
        var button = new Button { Content = "保留控件", Width = 96, Height = 28 };
        var link = new Hyperlink(new Run()) { NavigateUri = new Uri("https://jalium.invalid/zero-inline"), TargetName = "kept" };
        int clicks = 0, commands = 0, navigations = 0; object parameter = new();
        link.Command = new Command(value => { if (!ReferenceEquals(value, parameter)) throw new InvalidOperationException("Command parameter changed."); commands++; });
        link.CommandParameter = parameter; link.Click += (_, _) => clicks++;
        link.RequestNavigate += (_, e) => { navigations++; e.Handled = true; };
        void Keep(Inline item) { paragraph.Inlines.Add(item); kept.Add(item); }
        switch (model)
        {
            case 0: Keep(new InlineUIContainer(button)); break;
            case 1: Keep(new LineBreak()); break;
            case 2: Keep(link); break;
            case 3: Keep(new Span(link)); break;
            case 4: Keep(new Run()); break;
            case 5:
                Keep(new InlineUIContainer(button)); Keep(link); Keep(new Span(new Hyperlink(new Run())));
                Keep(new Run()); Keep(new LineBreak()); Keep(new InlineUIContainer(new Button { Content = "第二控件", Width = 96, Height = 28 })); break;
        }
        paragraph.Inlines.Add(last);
        var editor = new RichTextBox(new FlowDocument(paragraph)) { Height = 160, Foreground = Brushes.Black, Background = Brushes.White };
        var owner = new Window { Title = "Jalium Writing Tools Zero Inline " + index, Width = 680, Height = 220,
            ShowActivated = false, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = editor };
        Application.Current!.MainWindow = owner;
        var checks = new List<string>(); var scenarios = new List<string>(); var failures = new List<string>(); var captures = new List<object>();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Identity(string text)
        {
            Require(ReferenceEquals(editor.Document.Blocks[0], paragraph), "original paragraph survives");
            Require(editor.Document.GetText() == text, "exact UTF-16 text survives");
            foreach (var item in kept) Require(paragraph.Inlines.Contains(item) && ReferenceEquals(item.Parent, paragraph), "semantic sibling retains identity and owner");
            foreach (var container in kept.OfType<InlineUIContainer>()) Require(container.Child is Button, "embedded control remains attached");
            if (model is 0 or 5) Require(ReferenceEquals(kept.OfType<InlineUIContainer>().First().Child, button), "original button instance survives");
            if (model is 2 or 3 or 5) Require(link.NavigateUri?.AbsoluteUri == "https://jalium.invalid/zero-inline" && link.TargetName == "kept" &&
                (model == 3 ? ReferenceEquals(kept[0], link.Parent) : ReferenceEquals(paragraph, link.Parent)), "empty hyperlink identity and navigation survive");
        }
        NSWritingToolsCoordinatorContext Context(out NSWritingToolsCoordinator coordinator, out INSWritingToolsCoordinatorDelegate client)
        {
            var view = Runtime.GetNSObject<NSView>(owner.Handle)!; coordinator = view.WritingToolsCoordinator!;
            Require(coordinator?.WeakDelegate is not null, "actual SDK coordinator and delegate exist");
            client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
            NSWritingToolsCoordinatorContext? context = null; int calls = 0;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { calls++; context = values.FirstOrDefault(); });
            Require(calls == 1 && context?.AttributedString.Value == editor.Document.GetText(), "SDK returns one exact full-document context");
            return context!;
        }
        void Replace(NSWritingToolsCoordinator coordinator, INSWritingToolsCoordinatorDelegate client, NSWritingToolsCoordinatorContext context, NSAttributedString proposal, bool reject = false)
        {
            int calls = 0; NSAttributedString? accepted = null;
            client.ReplaceRange(coordinator, new NSRange(0, context.AttributedString.Length), context, proposal,
                NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => { calls++; accepted = value; });
            Require(calls == 1 && (reject ? accepted is null : accepted?.Value == proposal.Value), reject ? "ambiguous object replacement is rejected once" : "native-to-managed replacement accepts once");
        }
        byte[] Capture(string stage, int dpi)
        {
            owner.UpdateLayout();
            var bitmap = MacOSWritingToolsBitmapCapture.Capture(680 * dpi, 180 * dpi, dpi,
                drawing => { drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 680, 180)); editor.Render(drawing); }, () => owner.Handle != 0)
                ?? throw new InvalidOperationException("Renderer readback unavailable.");
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Require(bitmap.PixelWidth == 680 * dpi && bitmap.PixelHeight == 180 * dpi && pixels.Any(value => value < 200), "readback contains painted content at requested DPI");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(output, stage + "-" + dpi + "x.png"); using (var file = File.Create(path)) encoder.Save(file);
            captures.Add(new { stage, dpi, width = bitmap.PixelWidth, height = bitmap.PixelHeight, file = Path.GetFileName(path), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)) });
            return pixels;
        }
        owner.Show(); owner.UpdateLayout(); editor.Focus();
        var inputView = Runtime.GetNSObject<NSView>(owner.Handle)!;
        inputView.Window!.MakeFirstResponder(inputView); inputView.Window.MakeKeyWindow();
        app.Activate(); owner.Activate();
        NSTimer? holdTimer = null;
        void Finish() { owner.Close(); host.AllowQuit = true; app.Terminate(app); }
        var watch = Stopwatch.StartNew(); bool done = false;
        int waitSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_ZERO_WAIT_SECONDS"), out int seconds) ? Math.Clamp(seconds, 1, 600) : 20;
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(80), _ =>
        {
            if (done || watch.Elapsed < TimeSpan.FromMilliseconds(200)) return;
            bool ready = inputView.WritingToolsCoordinator is not null && (!foreground || app.Active && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId);
            if (!ready && watch.Elapsed < TimeSpan.FromSeconds(waitSeconds)) return;
            done = true;
            try
            {
                if (foreground) Require(app.Active && NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier == Environment.ProcessId, "AppKit and system foreground agree");
                Require(ReferenceEquals(Keyboard.FocusedElement, editor), "logical rich-editor focus is retained");
                string before = editor.Document.GetText(); var originals = new Dictionary<int, byte[]>();
                foreach (int dpi in new[] { 1, 2 }) originals[dpi] = Capture("before", dpi);
                var context = Context(out var coordinator, out var client); Replace(coordinator, client, context, context.AttributedString); Identity(before);
                foreach (int dpi in new[] { 1, 2 }) Require(originals[dpi].SequenceEqual(Capture("unchanged", dpi)), "unchanged acceptance keeps exact painted pixels");
                scenarios.Add("unchanged");
                context = Context(out coordinator, out client);
                using (var proposal = new NSMutableAttributedString(context.AttributedString))
                using (var key = new NSString("NSFont"))
                using (var font = NSFont.BoldSystemFontOfSize(29))
                {
                    proposal.AddAttribute(key, font, new NSRange(0, proposal.Length - 1));
                    Replace(coordinator, client, context, proposal); Identity(before);
                    Require(first.FontSize == 29 && first.FontWeight.ToOpenTypeWeight() >= 700, "native font delta reaches original run");
                    Require(kept.Where(item => item is LineBreak or InlineUIContainer).All(item => item.FontSize == 29), "native font delta reaches retained inline object");
                    foreach (int dpi in new[] { 1, 2 }) Capture("format", dpi);
                    Require(editor.Undo(), "font delta has one Undo"); Identity(before);
                    foreach (int dpi in new[] { 1, 2 }) Require(originals[dpi].SequenceEqual(Capture("format-undo", dpi)), "font Undo restores exact pixels");
                    Require(editor.Redo(), "font delta has one Redo"); Identity(before); Require(editor.Undo(), "return to baseline before rewrite");
                }
                scenarios.Add("format-and-history");
                context = Context(out coordinator, out client); using var rewritten = new NSMutableAttributedString(context.AttributedString);
                rewritten.Replace(new NSRange(0, 4), "窄斜体 Miii"); string changed = rewritten.Value;
                Replace(coordinator, client, context, rewritten); Identity(changed);
                foreach (int dpi in new[] { 1, 2 }) Capture("rewritten", dpi);
                Require(editor.Undo(), "rewrite has one Undo"); Identity(before);
                foreach (int dpi in new[] { 1, 2 }) Require(originals[dpi].SequenceEqual(Capture("rewrite-undo", dpi)), "rewrite Undo restores exact pixels");
                Require(editor.Redo(), "rewrite has one Redo"); Identity(changed); scenarios.Add("rewrite-and-history");
                if (model is 2 or 3 or 5) { link.DoClick(); Require((clicks, commands, navigations) == (1, 1, 1), "retained empty hyperlink raises click, command and navigation"); }
                Identity(changed); scenarios.Add("semantic-behavior");
                context = Context(out coordinator, out client);
                if (model is 0 or 1 or 5)
                {
                    using var ambiguous = new NSMutableAttributedString(context.AttributedString);
                    int at = ambiguous.Value.IndexOf(model == 1 ? '\n' : '\ufffc');
                    Require(at >= 0, "embedded object slot exists in real SDK text"); ambiguous.Replace(new NSRange(at, 1), "");
                    Replace(coordinator, client, context, ambiguous, reject: true); Identity(changed);
                }
                else { Replace(coordinator, client, context, context.AttributedString); Identity(changed); }
                scenarios.Add("subsequent-acceptance-or-atomic-rejection");
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { index, custom, software, model,
                    passed = checks.Count, scenarios, expectedScenarios = 5, failures, checks, captures, pid = Environment.ProcessId, appActive = app.Active,
                    frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier, foregroundRequired = foreground, physicalInput = false, appleIntelligenceService = false }));
                Console.WriteLine($"Writing Tools zero inline {index}: {checks.Count} checks; {scenarios.Count}/5 scenarios; {failures.Count} failures");
                if (int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_ZERO_HOLD_SECONDS"), out int hold) && hold is > 0 and <= 180)
                    holdTimer = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(hold), _ => Finish());
                else Finish();
            }
        });
        app.Run(); return failures.Count == 0 && scenarios.Count == 5 ? 0 : 1;
    }
    private sealed class Command(Action<object?> action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action(parameter);
    }
    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        private bool _started; internal bool AllowQuit;
        public override void DidFinishLaunching(NSNotification notification) { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
        public override NSApplicationTerminateReply ApplicationShouldTerminate(NSApplication sender) => AllowQuit ? NSApplicationTerminateReply.Now : NSApplicationTerminateReply.Cancel;
    }
}
