using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Text.Json;
using ObjCRuntime;

namespace Jalium.UI.MacOS;

// Calls the real SDK delegate through the native -> managed bridge. This does
// not invoke Apple Intelligence or inject operating-system input events.
internal static class WindowWritingToolsChecks
{
    // A copied validation bundle can select this fixture without shell-based
    // GUI launching. CUA/LaunchServices then supplies normal application startup.
    internal static bool TryRunBundled(out int exitCode)
    {
        exitCode = 0;
        string file = Path.Combine(NSBundle.MainBundle.ResourcePath, "jalium-writing-tools-launch.json");
        if (!File.Exists(file)) return false;
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var configuration = document.RootElement;
        string backend = configuration.GetProperty("backend").GetString()!;
        if (backend is not ("metal" or "software" or "both")) throw new InvalidDataException("Invalid validation backend.");
        string output = Path.GetFullPath(configuration.GetProperty("resultsDirectory").GetString()!);
        string temporary = Path.GetFullPath(configuration.GetProperty("temporaryDirectory").GetString()!);
        if (!Directory.Exists(output) || !Directory.Exists(temporary)) throw new DirectoryNotFoundException("Validation directories must be prepared first.");
        File.WriteAllText(Path.Combine(output, "launch.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, bundle = NSBundle.MainBundle.BundlePath }));
        Environment.SetEnvironmentVariable("JALIUM_WRITING_TOOLS_OUTPUT", output);
        Environment.SetEnvironmentVariable("TMPDIR", temporary);
        Environment.SetEnvironmentVariable("JALIUM_WRITING_TOOLS_HOLD_SECONDS", configuration.GetProperty("holdSeconds").GetInt32().ToString());
        Environment.SetEnvironmentVariable("JALIUM_WRITING_TOOLS_STARTUP_SECONDS", configuration.GetProperty("startupSeconds").GetInt32().ToString());
        exitCode = Run(backend == "software", backend == "both");
        return true;
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_image_count")]
    private static extern uint DyldImageCount();
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_dyld_get_image_name")]
    private static extern nint DyldImageName(uint index);
    private static string[] NativeImages()
    {
        var images = new List<string>();
        uint count = DyldImageCount();
        for (uint index = 0; index < count; index++)
            if (System.Runtime.InteropServices.Marshal.PtrToStringUTF8(DyldImageName(index)) is { } image && image.Contains("/libjalium.native.", StringComparison.Ordinal))
                images.Add(image);
        return images.Distinct().Order().ToArray();
    }

    internal static int Run(bool software = false, bool both = false)
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(15, 2)) return 77;
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        if (software) RenderContext.GetOrCreateCurrent(RenderBackend.Software,forceReplace:true);
        else RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var failures = new List<string>(); var checks = new List<string>();
        var cases = new Queue<(bool Software, bool Custom, int Editor)>();
        foreach (bool backend in both ? new[] { false, true } : new[] { software })
            foreach (bool custom in new[] { false, true }) for (int editor = 0; editor < 3; ++editor) cases.Enqueue((backend, custom, editor));
        int expected = cases.Count, caseIndex = -1;
        int startupSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_TOOLS_STARTUP_SECONDS"), out int requestedStartup) ? Math.Clamp(requestedStartup, 12, 600) : 12;
        Window? owner = null; FrameworkElement? control = null; PasswordBox? password = null;
        var wait = Stopwatch.StartNew(); string label = ""; int completed = 0;
        int holdSeconds = int.TryParse(Environment.GetEnvironmentVariable("JALIUM_WRITING_TOOLS_HOLD_SECONDS"), out int requestedHold) ? Math.Clamp(requestedHold, 0, 120) : 0;
        bool holding = false; NSTimer? holdTimer = null;
        void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(label + ": " + message); Console.WriteLine("PASS Writing Tools " + label + ": " + message);
        }
        void Next()
        {
            owner?.Close(); owner = null;
            if (!cases.TryDequeue(out var item))
            {
                string? output = Environment.GetEnvironmentVariable("JALIUM_WRITING_TOOLS_OUTPUT");
                if (output is not null)
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output,"host-result.json"),JsonSerializer.Serialize(new
                    { completed,expected,passed=checks.Count,failures,checks,serviceAvailable=NSWritingToolsCoordinator.IsWritingToolsAvailable,
                        physicalInput=false,appleIntelligenceService=false,pid=Environment.ProcessId,nativeImages=NativeImages() }));
                }
                Console.WriteLine($"macOS Writing Tools managed host: {completed}/{expected} cases; {checks.Count} checks; {failures.Count} failures");
                app.Stop(app); using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false); return;
            }
            caseIndex++;
            if (RenderContext.Current?.Backend != (item.Software ? RenderBackend.Software : RenderBackend.Metal))
            {
                var context = RenderContext.GetOrCreateCurrent(item.Software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
                if (!item.Software) context.DefaultRenderingEngine = RenderingEngine.Impeller;
            }
            label = $"software={item.Software},custom={item.Custom},editor={item.Editor}";
            control = item.Editor switch
            {
                0 => new TextBox { Text="ab🙂中文 Miii", AcceptsReturn=true },
                1 => new RichTextBox(new FlowDocument(new Paragraph(new Run("ab🙂中文 Miii")))),
                _ => new EditControl { Text="ab🙂中文 Miii" }
            };
            ((Control)control).Foreground = Brushes.Red; ((Control)control).FontSize=22;
            ((Control)control).FontStyle=FontStyles.Italic; control.Height=130;
            password = new PasswordBox { Password="private secret",Height=40 };
            var panel = new StackPanel { Margin=new Thickness(24),Spacing=12 }; panel.Children.Add(control);panel.Children.Add(password);
            owner = new Window { Title="Jalium Writing Tools · " + (item.Software ? "Software" : "Metal") + " · " + (item.Custom ? "Custom" : "Native"),Width=500,Height=300,
                TitleBarStyle=item.Custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel };
            Application.Current!.MainWindow=owner;owner.Show();owner.UpdateLayout();control.Focus();app.Activate();owner.Activate();wait.Restart();
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ =>
        {
            if (holding || owner is null || control is null) return;
            bool ready=app.Active && owner.IsActive && app.KeyWindow?.FirstResponder is NSView &&
                NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier==Environment.ProcessId;
            if (!ready && wait.Elapsed < TimeSpan.FromSeconds(caseIndex == 0 ? startupSeconds : 12)) return;
            try
            {
                Require(ready,"actual AppKit and system foreground agree");
                // The window's content root also owns the material sibling;
                // the actual input/render view is Window.Handle.
                var view=Runtime.GetNSObject<NSView>(owner!.Handle)!;
                Require(app.KeyWindow!.FirstResponder.Handle==view.Handle,"actual native input view holds first responder");
                var coordinator=view.WritingToolsCoordinator;
                Require(coordinator is not null,"eligible focused editor attaches a real coordinator");
                var nativeClient=coordinator!.WeakDelegate;
                Require(nativeClient is not null,"coordinator retains its native delegate");
                // The delegate is a native Objective-C object, so the ordinary
                // managed model cast is insufficient. Use the SDK protocol wrapper.
                var client=Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(nativeClient!.Handle,false)!;
                Require(client is not null,"SDK protocol wrapper resolves the native delegate");
                var source=((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(control)!.GetPattern(PatternInterface.Text)!).Source;
                string original=source.Text;
                NSWritingToolsCoordinatorContext? Context()
                {
                    NSWritingToolsCoordinatorContext? context=null;int calls=0;
                    client.RequestsContexts(coordinator,NSWritingToolsCoordinatorContextScope.FullDocument,values=>{++calls;context=values.FirstOrDefault();});
                    Require(calls==1,"context completion runs exactly once");return context;
                }
                var context=Context();Require(context?.AttributedString.Value==original,"context preserves original UTF16 and document projection");
                var platform = (Jalium.UI.Controls.Platform.IPlatformWindow)typeof(Window)
                    .GetField("_platformWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner)!;
                var handler = (Action<Jalium.UI.Controls.Platform.PlatformEvent>)platform.GetType()
                    .GetField("_eventHandler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(platform)!;
                byte[]? selectionReply = null; int selectionRequests = 0;
                platform.SetEventHandler(evt =>
                {
                    handler(evt);
                    if (evt.TextServicesRequest is { Kind: 3 } request)
                    { selectionRequests++; selectionReply = request.Response; }
                });
                bool Applied()
                {
                    if (selectionReply is null) return false;
                    using var json = JsonDocument.Parse(selectionReply);
                    return json.RootElement.TryGetProperty("applied", out var applied) && applied.GetBoolean();
                }
                try
                {
                    using var requested = NSValue.FromRange(new NSRange(0, 2));
                    int selectionCompletions = 0;
                    client.SelectRanges(coordinator, [requested], context!, () => selectionCompletions++);
                    Require(selectionRequests == 1 && selectionCompletions == 1, "SDK selection enters the managed request and completes once");
                    Require(Applied() && source.SelectionStart == 0 && source.SelectionLength == 2, "exact SDK selection is acknowledged");
                    source.Select(4, 2);
                    bool reselected = false;
                    void ChooseAnotherRange()
                    {
                        if (reselected) return;
                        reselected = true; source.Select(4, 2);
                    }
                    RoutedEventHandler routed = (_, _) => ChooseAnotherRange();
                    EventHandler ordinary = (_, _) => ChooseAnotherRange();
                    if (control is TextBoxBase textBox) textBox.SelectionChanged += routed;
                    else ((EditControl)control).SelectionChanged += ordinary;
                    try
                    {
                        selectionReply = null;
                        client.SelectRanges(coordinator, [requested], context!, () => selectionCompletions++);
                        Require(reselected && selectionRequests == 2 && selectionCompletions == 2, "reentrant SDK selection still completes exactly once");
                        Require(!Applied(), "bridge refuses to acknowledge a range changed by SelectionChanged");
                        Require(source.SelectionStart == 4 && source.SelectionLength == 2, "SDK selection preserves the application's replacement range");
                    }
                    finally
                    {
                        if (control is TextBoxBase restoringBox) restoringBox.SelectionChanged -= routed;
                        else ((EditControl)control).SelectionChanged -= ordinary;
                    }
                }
                finally { platform.SetEventHandler(handler); }
                NSBezierPath[]? paths=null;
                client.RequestsBoundingBezierPaths(coordinator,new NSRange(0,2),context!,values=>paths=values);
                Require(paths is {Length:>0} && paths.All(path=>path.Bounds.Width>0),"bridge returns actual text geometry");
                NSTextPreview[]? previews=null;
                client.RequestsPreviewForTextAnimation(coordinator,NSWritingToolsCoordinatorTextAnimation.Remove,new NSRange(0,2),context!,values=>previews=values);
                Require(previews is {Length:>0} && previews[0].PreviewImage.Width>0,"bridge returns an actual native glyph preview");
                int animationCalls=0;
                client.PrepareForTextAnimation(coordinator,NSWritingToolsCoordinatorTextAnimation.Remove,new NSRange(0,2),context!,()=>++animationCalls);
                Require(MacOSWritingToolsPresentation.IsPresenting(control),"preparation hides the requested glyph range");
                client.FinishTextAnimation(coordinator,NSWritingToolsCoordinatorTextAnimation.Remove,new NSRange(0,2),context!,()=>++animationCalls);
                Require(animationCalls==2 && !MacOSWritingToolsPresentation.IsPresenting(control),"finish restores presentation without changing text");
                NSAttributedString? accepted=null;
                using var proposed=new NSAttributedString("UV🙂");
                client.ReplaceRange(coordinator,new NSRange(2,2),context!,proposed,
                    NSWritingToolsCoordinatorTextReplacementReason.Noninteractive,null!,value=>accepted=value);
                Require(accepted?.Value=="UV🙂" && source.Text.Contains("abUV🙂中文",StringComparison.Ordinal),"SDK replacement commits through the actual managed control");
                switch(control) { case TextBoxBase box: box.Undo();break;case EditControl editor:editor.Undo();break; }
                Require(source.Text==original,"one Undo restores the original text");
                switch(control) { case TextBoxBase box:box.Redo();break;case EditControl editor:editor.Redo();break; }
                Require(source.Text.Contains("abUV🙂中文",StringComparison.Ordinal),"one Redo restores the committed replacement");
                TextCompositionEventHandler editDuringPreview=(_,_)=>control.RaiseEvent(new TextCompositionEventArgs(
                    UIElement.TextInputEvent,"Q",0) {ImeReplacementRange=(0,1)});
                control.PreviewTextInput+=editDuringPreview;
                try
                {
                    accepted=null;
                    client.ReplaceRange(coordinator,new NSRange(0,1),context!,proposed,
                        NSWritingToolsCoordinatorTextReplacementReason.Noninteractive,null!,value=>accepted=value);
                    Require(accepted is null && source.Text.StartsWith("QbUV🙂中文",StringComparison.Ordinal),
                        "preview edit rejects the stale proposal without overwriting the callback edit");
                }
                finally {control.PreviewTextInput-=editDuringPreview;}
                switch(control){case TextBoxBase box:box.IsReadOnly=true;break;case EditControl editor:editor.IsReadOnly=true;break;}
                var readCoordinator=view.WritingToolsCoordinator;
                Require(readCoordinator is not null,"read-only editor retains eligible reading support");
                var readClient=Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(readCoordinator!.WeakDelegate!.Handle,false)!;
                NSWritingToolsCoordinatorContext? readContext=null;
                readClient.RequestsContexts(readCoordinator,NSWritingToolsCoordinatorContextScope.FullDocument,values=>readContext=values.FirstOrDefault());
                string beforeRead=source.Text;accepted=null;
                readClient.ReplaceRange(readCoordinator,new NSRange(0,1),readContext!,proposed,
                    NSWritingToolsCoordinatorTextReplacementReason.Noninteractive,null!,value=>accepted=value);
                Require(accepted is null && source.Text==beforeRead,"actual read-only bridge rejects SDK replacement");
                switch(control){case TextBoxBase box:box.IsReadOnly=false;break;case EditControl editor:editor.IsReadOnly=false;break;}
                Require(password!.Focus(),"password target takes keyboard focus");
                Require(view.WritingToolsCoordinator is null,"password focus removes the text-service coordinator");
                Require(control.Focus(),"original editor regains keyboard focus");
                Require(view.WritingToolsCoordinator is { } successor && successor.Handle!=coordinator.Handle,"returning to an editor creates a fresh coordinator");
                string beforeStale=source.Text;accepted=null;
                client.ReplaceRange(coordinator,new NSRange(0,1),context!,proposed,
                    NSWritingToolsCoordinatorTextReplacementReason.Noninteractive,null!,value=>accepted=value);
                Require(accepted is null && source.Text==beforeStale,"retired SDK context cannot edit the returning editor");
                completed++;
                if (completed == expected && holdSeconds > 0)
                {
                    holding = true; holdTimer = NSTimer.CreateScheduledTimer(TimeSpan.FromSeconds(holdSeconds), _ => Next());
                    return;
                }
            }
            catch(Exception error){failures.Add(label+": "+error);Console.Error.WriteLine("FAIL Writing Tools "+label+": "+error);}
            Next();
        });
        app.BeginInvokeOnMainThread(Next); app.Run();
        holdTimer?.Dispose();
        return failures.Count==0 && completed==expected?0:1;
    }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {if(_started)return;_started=true;base.DidFinishLaunching(notification);}
        protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings {DisableDefaults=true}).Build()
            .UseApplication(new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown});
    }
}
