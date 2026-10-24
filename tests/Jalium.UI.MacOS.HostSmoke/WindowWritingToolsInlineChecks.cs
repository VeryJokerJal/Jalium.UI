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

// Own-process SDK calls and renderer readbacks, without operating-system input
// injection or a claim that the Apple Intelligence service was exercised.
internal static class WindowWritingToolsInlineChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or >= 12) return 2;
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        string output = Environment.GetEnvironmentVariable("JALIUM_WRITING_INLINE_OUTPUT")!;
        Directory.CreateDirectory(output);
        bool custom = index % 2 == 1, software = index % 4 >= 2; int model = index / 4;
        bool foreground = Environment.GetEnvironmentVariable("JALIUM_WRITING_INLINE_FOREGROUND") == "1";
        var app = NSApplication.SharedApplication; app.ActivationPolicy = foreground ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Accessory;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        var run = new Run("Miii 中文🙂 e\u0301") { FontFamily = new FontFamily("Helvetica Neue"), FontSize = 23,
            FontStyle = FontStyles.Italic, FontWeight = FontWeights.Bold, FontStretch = FontStretches.Condensed };
        var link = new Hyperlink() { NavigateUri = new Uri("https://jalium.invalid/inline"), TargetName = "kept" };
        int clicks = 0, commands = 0, navigations = 0; object parameter = new();
        link.Command = new Command(value => { if (!ReferenceEquals(value, parameter)) throw new InvalidOperationException("Command parameter changed."); commands++; });
        link.CommandParameter = parameter; link.Click += (_, _) => clicks++;
        link.RequestNavigate += (_, e) => { navigations++; e.Handled = true; };
        if (model == 2) link.Inlines.Add(run);
        var span = new Span(model == 2 ? link : run);
        Brush parentBackground = model == 1 ? new LinearGradientBrush(Colors.Red, Colors.Blue, 0) : Color(0x80ff0000);
        span.Background = parentBackground; if (model == 0) run.Background = Color(0x800000ff);
        var childBackground = run.Background; var paragraph = new Paragraph(span);
        var editor = new RichTextBox(new FlowDocument(paragraph)) { Height = 120, Foreground = Brushes.Black, Background = Brushes.White };
        var owner = new Window { Title = "Jalium Writing Tools Inline " + index, Width = 640, Height = 180,
            ShowActivated = false, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = editor };
        Application.Current!.MainWindow = owner;
        var checks = new List<string>(); var failures = new List<string>(); var captures = new List<object>();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void Identity(string expected)
        {
            Require(ReferenceEquals(editor.Document.Blocks[0], paragraph) && ReferenceEquals(paragraph.Inlines[0], span), "original paragraph and span instances survive");
            Require(ReferenceEquals(span.Background, parentBackground) && ReferenceEquals(run.Background, childBackground), "raw painted background layers remain unchanged");
            Require(editor.Document.GetText() == expected, "exact UTF-16 document text survives");
            Require(run.FontStretch == FontStretches.Condensed, "unrepresented font width remains on the original run");
            if (model == 2) Require(ReferenceEquals(span.Inlines[0], link) && ReferenceEquals(link.Inlines[0], run) &&
                link.NavigateUri?.AbsoluteUri == "https://jalium.invalid/inline" && link.TargetName == "kept", "hyperlink identity, navigation and target survive");
            else Require(ReferenceEquals(span.Inlines[0], run), "original run identity survives");
        }
        NSWritingToolsCoordinatorContext Context(out NSWritingToolsCoordinator coordinator, out INSWritingToolsCoordinatorDelegate client)
        {
            var view = Runtime.GetNSObject<NSView>(owner.Handle)!; coordinator = view.WritingToolsCoordinator!;
            Require(coordinator?.WeakDelegate is not null, "actual SDK coordinator and native delegate exist");
            client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
            NSWritingToolsCoordinatorContext? result = null; int calls = 0;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { calls++; result = values.FirstOrDefault(); });
            Require(calls == 1 && result?.AttributedString.Value == editor.Document.GetText(), "SDK returns one exact full-document context");
            using var key = new NSString("JaliumWritingToolsInlineContext");
            var attributes = result!.AttributedString.GetAttributes(0, out _);
            Require(attributes[key] is NSString path && path.ToString().Length > 0, "private semantic ancestry reaches the actual attributed context");
            using var backgroundKey = new NSString("NSBackgroundColor");
            if (model == 1) Require(attributes[backgroundKey] is null, "a gradient is retained in the document without an invented native solid color");
            else Require(attributes[backgroundKey] is NSColor, "painted inline background reaches native attributed text");
            return result;
        }
        void Replace(NSWritingToolsCoordinator coordinator, INSWritingToolsCoordinatorDelegate client, NSWritingToolsCoordinatorContext context, NSAttributedString proposal)
        {
            int calls = 0; NSAttributedString? accepted = null;
            client.ReplaceRange(coordinator, new NSRange(0, context.AttributedString.Length), context, proposal,
                NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => { calls++; accepted = value; });
            Require(calls == 1 && accepted?.Value == proposal.Value, "native-to-managed replacement completes once with the accepted proposal");
        }
        byte[] Capture(string stage, int dpi)
        {
            owner.UpdateLayout();
            var bitmap = MacOSWritingToolsBitmapCapture.Capture(640 * dpi, 160 * dpi, dpi,
                drawing => { drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 160)); editor.Render(drawing); }, () => owner.Handle != 0)
                ?? throw new InvalidOperationException("Renderer readback unavailable.");
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Require(bitmap.PixelWidth == 640 * dpi && bitmap.PixelHeight == 160 * dpi && pixels.Any(value => value < 200), "readback contains painted content at the requested DPI");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(output, stage + "-" + dpi + "x.png"); using (var file = File.Create(path)) encoder.Save(file);
            captures.Add(new { stage, dpi, width = bitmap.PixelWidth, height = bitmap.PixelHeight, file = Path.GetFileName(path), sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels)) });
            return pixels;
        }
        owner.Show(); owner.UpdateLayout(); editor.Focus();
        var inputView = Runtime.GetNSObject<NSView>(owner.Handle)!;
        inputView.Window!.MakeFirstResponder(inputView); inputView.Window.MakeKeyWindow();
        if (foreground) { app.Activate(); owner.Activate(); }
        NSTimer? holdTimer = null;
        void Finish() { owner.Close(); host.AllowQuit = true; app.Terminate(app); }
        var watch = Stopwatch.StartNew(); bool done = false;
        int waitSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_INLINE_WAIT_SECONDS"), out int seconds) ? Math.Clamp(seconds, 1, 600) : 20;
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
                foreach (int dpi in new[] { 1, 2 }) Require(originals[dpi].SequenceEqual(Capture("accepted-unchanged", dpi)), "unchanged acceptance produces identical glyph and background pixels");
                context = Context(out coordinator, out client); using var proposal = new NSMutableAttributedString(context.AttributedString);
                proposal.Replace(new NSRange(0, 4), "Miii 改写"); string changed = proposal.Value;
                Replace(coordinator, client, context, proposal); Identity(changed);
                foreach (int dpi in new[] { 1, 2 }) Capture("rewritten", dpi);
                Require(editor.Undo(), "one Undo accepts the rewritten transaction"); Identity(before);
                foreach (int dpi in new[] { 1, 2 }) Require(originals[dpi].SequenceEqual(Capture("undo", dpi)), "Undo restores the exact source pixels");
                Require(editor.Redo(), "one Redo restores the rewritten transaction"); Identity(changed);
                if (model == 2) { link.DoClick(); Require((clicks, commands, navigations) == (1, 1, 1), "real retained hyperlink still raises click, command and navigation"); }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { index, custom, software, model,
                    passed = checks.Count, failures, checks, captures, pid = Environment.ProcessId, appActive = app.Active,
                    frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier, physicalInput = false, appleIntelligenceService = false }));
                Console.WriteLine($"Writing Tools inline {index}: {checks.Count} checks; {failures.Count} failures");
                if (int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_INLINE_HOLD_SECONDS"), out int hold) && hold is > 0 and <= 180)
                    holdTimer = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(hold), _ => Finish());
                else Finish();
            }
        });
        app.Run(); return failures.Count == 0 ? 0 : 1;
    }
    private static SolidColorBrush Color(uint argb) => new(Jalium.UI.Media.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
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
