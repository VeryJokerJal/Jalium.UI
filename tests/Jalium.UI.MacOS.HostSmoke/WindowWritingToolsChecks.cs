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
    internal static int Run(bool software = false)
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
        var cases = new Queue<(bool Custom, int Editor)>();
        foreach (bool custom in new[] { false, true }) for (int editor = 0; editor < 3; ++editor) cases.Enqueue((custom, editor));
        Window? owner = null; FrameworkElement? control = null; PasswordBox? password = null;
        var wait = Stopwatch.StartNew(); string label = ""; int completed = 0;
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
                    { completed,expected=6,passed=checks.Count,failures,checks,serviceAvailable=NSWritingToolsCoordinator.IsWritingToolsAvailable,
                        physicalInput=false,appleIntelligenceService=false,pid=Environment.ProcessId }));
                }
                Console.WriteLine($"macOS Writing Tools managed host: {completed}/6 cases; {checks.Count} checks; {failures.Count} failures");
                app.Stop(app); return;
            }
            label = $"custom={item.Custom},editor={item.Editor}";
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
            owner = new Window { Title="Jalium Writing Tools bridge",Width=500,Height=300,
                TitleBarStyle=item.Custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel };
            Application.Current!.MainWindow=owner;owner.Show();owner.UpdateLayout();control.Focus();app.Activate();owner.Activate();wait.Restart();
        }
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100), _ =>
        {
            if (owner is null || control is null) return;
            bool ready=app.Active && owner.IsActive && app.KeyWindow?.FirstResponder is NSView &&
                NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier==Environment.ProcessId;
            if (!ready && wait.Elapsed < TimeSpan.FromSeconds(12)) return;
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
            }
            catch(Exception error){failures.Add(label+": "+error);Console.Error.WriteLine("FAIL Writing Tools "+label+": "+error);}
            Next();
        });
        app.BeginInvokeOnMainThread(Next); app.Run();
        return failures.Count==0 && completed==6?0:1;
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
