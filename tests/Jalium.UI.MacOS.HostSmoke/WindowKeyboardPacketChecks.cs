using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Input;

namespace Jalium.UI.MacOS;

internal static class WindowKeyboardPacketChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(),out int index)||(uint)index>=12) return 2;
        string output=Environment.GetEnvironmentVariable("JALIUM_KEYBOARD_PACKET_OUTPUT")!;Directory.CreateDirectory(output);
        bool custom=index%2==1,software=index>=6;int kind=index%6/2;
        var app=NSApplication.SharedApplication;app.ActivationPolicy=NSApplicationActivationPolicy.Regular;
        using var host=new Delegate();app.Delegate=host;app.FinishLaunching();
        using(var note=NSNotification.FromName(NSApplication.DidFinishLaunchingNotification,app))host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software?RenderBackend.Software:RenderBackend.Metal,forceReplace:true);
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var target=new TextBox { Text="重入验证 · 中文🙂",Height=44 };
        AutomationProperties.SetName(target,"键盘重入后继续编辑");
        var content=new StackPanel { Margin=new Thickness(16),Spacing=12 };
        content.Children.Add(new TextBlock { Text="键盘事件有效期",FontSize=20 });content.Children.Add(target);
        content.Children.Add(new TextBox { Text="第二个焦点目标",Height=36 });
        content.Children.Add(new TextBlock { Text="新的输入到达后，旧输入应停止。",FontSize=15,TextWrapping=TextWrapping.Wrap });
        var status=new TextBlock { Text="检查准备中",FontSize=15,TextWrapping=TextWrapping.Wrap };content.Children.Add(status);
        var subject=new Border { Width=360,Height=280,Child=content,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1) };
        subject.SetResourceReference(Border.BackgroundProperty,"LayerFillColorDefaultBrush");subject.SetResourceReference(Border.BorderBrushProperty,"ControlStrokeColorDefaultBrush");
        var area=new Canvas { Height=310 };var anchor=new Button { Content="键盘检查区域",Width=140,Height=40 };
        area.Children.Add(kind==0?subject:anchor);Canvas.SetLeft(subject,5);Canvas.SetTop(subject,5);Canvas.SetLeft(anchor,5);Canvas.SetTop(anchor,5);
        var panel=new StackPanel { Margin=new Thickness(20),Spacing=12 };
        panel.Children.Add(new TextBlock { Text="窗口与键盘重入",FontSize=24 });panel.Children.Add(area);
        var normal=new Button { Content="默认操作",IsDefault=true,Width=130,Height=40 };panel.Children.Add(normal);
        var owner=new ProbeWindow { Title="Jalium Keyboard Packet Lifetime",Width=kind==0?760:420,Height=540,
            MinWidth=420,MinHeight=500,TitleBarStyle=custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel };
        owner.SetResourceReference(Window.BackgroundProperty,"SolidBackgroundFillColorBaseBrush");Application.Current!.MainWindow=owner;
        Popup? popup=kind==0?null:new Popup { Child=subject,PlacementTarget=anchor,Placement=PlacementMode.Bottom,
            PreferExternalWindow=kind==2,ShouldConstrainToRootBounds=kind==1,StaysOpen=true,PopupAnimation=PopupAnimation.None };
        var checks=new List<string>();var failures=new List<string>();var scenarios=new List<object>();var traces=new List<object>();
        string phase="",replacement="down";Key oldKey=Key.K;bool nested=false,running=false,done=false;int oldExec=0,newExec=0,tail=0,newEvents=0,clicks=0,nestedLoops=0;
        Window? surface=null;NSView? sourceView=null,ownerView=null;var watch=Stopwatch.StartNew();
        void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message);checks.Add(message); }
        void Record(string kind,Key key) => traces.Add(new { phase,kind,key=key.ToString(),tick=Environment.TickCount64,oldExec,newExec,tail,newEvents,pressed=target.IsPressed });
        void Send(NSView view,Key key,bool up=false,bool command=false,string? text=null)
        {
            ushort code=key switch { Key.K=>40,Key.L=>37,Key.Space=>49,Key.Enter=>36,Key.Tab=>48,_=>0 };
            text??=key switch { Key.K=>"k",Key.L=>"l",Key.Space=>" ",Key.Enter=>"\r",Key.Tab=>"\t",_=>"a" };
            using var e=NSEvent.KeyEvent(up?NSEventType.KeyUp:NSEventType.KeyDown,CGPoint.Empty,command?NSEventModifierMask.CommandKeyMask:0,
                watch.Elapsed.TotalSeconds+1,view.Window!.WindowNumber,null,text,text,false,code)!;
            if(up)view.KeyUp(e);else view.KeyDown(e);
        }
        void Replace()
        {
            if(nested)return;nested=true;bool completed=false;Exception? error=null;
            using var timer=NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(1),_=>
            {
                try
                {
                    NSView view=phase=="pressed-up-cross-host"?ownerView!:sourceView!;
                    if(replacement=="text")Send(view,Key.A,text:"新");
                    else Send(view,replacement=="space"?Key.Space:Key.L,up:replacement=="up",command:replacement is "down" or "up");
                }
                catch(Exception failure){error=failure;}finally{completed=true;}
            });
            var elapsed=Stopwatch.StartNew();while(!completed&&elapsed.Elapsed.TotalSeconds<2)
            {using var until=NSDate.FromTimeIntervalSinceNow(.01);NSRunLoop.Current.RunUntil(until);}
            Require(completed&&error==null,"nested own AppKit run loop delivers the newer SDK packet"+(error==null?"":": "+error));nestedLoops++;
        }
        owner.Preview=key=> { Record("window-preview",key);if(phase=="window"&&key==oldKey)Replace(); };
        owner.PreviewRelease=key=> { Record("window-preview-up",key);if(phase.StartsWith("window-up",StringComparison.Ordinal)&&key==oldKey)Replace(); };
        target.PreviewKeyDown+=(_,e)=> { Record("preview-down",e.Key);if(e.Key==oldKey&&phase is "preview" or "default-enter" or "default-tab" or "pressed-down")Replace(); };
        target.KeyDown+=(_,e)=> { Record("bubble-first",e.Key);if(e.Key==oldKey&&phase=="bubble")Replace(); };
        target.KeyDown+=(_,e)=> { if(e.Key==oldKey&&phase!="text")tail++;if(e.Key==Key.L)newEvents++;Record("bubble-last",e.Key); };
        target.PreviewKeyUp+=(_,e)=> { Record("preview-up",e.Key);if(e.Key==oldKey&&phase is "up" or "pressed-up-cross-host")Replace(); };
        target.KeyUp+=(_,e)=> { if(e.Key==oldKey&&(phase=="up"||phase.StartsWith("window-up",StringComparison.Ordinal)||phase=="consumed-up"))tail++;if(e.Key==Key.L)newEvents++;Record("bubble-up",e.Key); };
        target.PreviewTextInput+=(_,e)=> { if(e.Text=="旧"&&phase=="text")Replace(); };
        target.AddHandler(UIElement.TextInputEvent,new TextCompositionEventHandler((_,e)=> { if(e.Text=="旧"&&phase=="text")tail++;if(e.Text=="新")newEvents++;Record("text-"+e.Text,Key.None); }),handledEventsToo:true);
        target.InputBindings.Add(new KeyBinding(new Command(()=> { if(phase=="query")Replace();return true; },()=>oldExec++),new KeyGesture(Key.K,ModifierKeys.Control)));
        target.InputBindings.Add(new KeyBinding(new Command(()=>true,()=>newExec++),new KeyGesture(Key.L,ModifierKeys.Control)));
        normal.Click+=(_,_)=>clicks++;
        void Attempt(string name,Action action)
        {
            int start=checks.Count;Exception? error=null;
            try{action();}catch(Exception failure){error=failure;failures.Add(name+": "+failure);}
            scenarios.Add(new { name,passed=error==null,checks=checks.Skip(start).ToArray(),error=error?.ToString() });
        }
        void Prepare(string name,Key key, string next="down")
        {
            owner.ConsumeUp=false;phase="";Send(sourceView!,Key.Enter,up:true);Send(sourceView!,Key.K,up:true,command:true);Send(sourceView!,Key.L,up:true,command:true);Send(sourceView!,Key.Space,up:true);
            Require(target.Focus(),"editor accepts focus before each packet scenario");target.Select(target.Text.Length,0);
            phase=name;oldKey=key;replacement=next;nested=false;oldExec=newExec=tail=newEvents=clicks=0;
        }
        void Capture(string name,Window window)
        {
            var image=MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth*window.DpiScale),(int)Math.Ceiling(window.ActualHeight*window.DpiScale),window.DpiScale,
                drawing=> { drawing.DrawRectangle(window.Background,null,new Rect(0,0,window.ActualWidth,window.ActualHeight));window.Render(drawing); },()=>window.Handle!=0)
                ??throw new InvalidOperationException("backend readback unavailable");
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
        }
        void Finish()
        {
            if(done)return;done=true;if(popup!=null)popup.IsOpen=false;owner.Close();
            File.WriteAllText(Path.Combine(output,"host-result.json"),JsonSerializer.Serialize(new { index,custom,software,kind=new[]{"owner","overlay","native"}[kind],passed=failures.Count==0,checks,failures,scenarios,traces,nestedLoops,
                processId=Environment.ProcessId,physicalInput=false,desktopScreenshot=false,ownNSViewSDK=true,actualNestedRunLoop=true,
                appActive=app.Active,frontmostPID=NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier }));
            app.Stop(app);using var wake=NSEvent.OtherEvent(NSEventType.ApplicationDefined,default,0,0,0,null,0,0,0);app.PostEvent(wake,false);
        }
        owner.Show();owner.UpdateLayout();if(popup!=null)popup.IsOpen=true;target.Focus();
        using var pulse=NSTimer.CreateRepeatingScheduledTimer(.03,_=>
        {
            if(done||running)return;
            try
            {
                if(watch.Elapsed.TotalSeconds>15)throw new TimeoutException("keyboard scene did not become ready");
                surface=(Window?)typeof(Window).GetMethod("GetMacOSPhysicalWindow",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[target]);
                if(surface==null||surface.Handle==0||surface.RenderTarget==null||target.ActualWidth==0)return;
                sourceView=Runtime.GetNSObject<NSView>(surface.Handle);ownerView=Runtime.GetNSObject<NSView>(owner.Handle);if(sourceView?.Window==null||ownerView?.Window==null)return;
                running=true;
                Require((surface!=owner)==(kind==2),"physical owner/overlay/native scene matches the requested configuration");
                Require(owner.RenderTarget?.Backend==(software?RenderBackend.Software:RenderBackend.Metal)&&surface.RenderTarget.Backend==(software?RenderBackend.Software:RenderBackend.Metal),"both actual render targets match the requested backend");
                foreach(string name in new[]{"window","preview","bubble","query"})
                Attempt(name,()=> { Prepare(name,Key.K);Send(sourceView,Key.K,command:true);Require(nested&&newExec==1,"newer shortcut executes exactly once");Require(oldExec==0,"older shortcut cannot execute after reentry");Require(tail==(name=="query"?1:0),"old route stops at the callback that received newer input");Require(ReferenceEquals(Keyboard.FocusedElement,target),"logical focus stays on the same editor"); });
                Attempt("new-key-up",()=> { Prepare("preview",Key.K,"up");Send(sourceView,Key.K,command:true);Require(nested&&newEvents==1,"newer key-up reaches its bubble handler");Require(oldExec==0&&tail==0,"newer key-up retires the older key-down route and command"); });
                Attempt("old-key-up",()=> { Prepare("up",Key.K);Send(sourceView,Key.K,up:true,command:true);Require(nested&&newExec==1,"newer down command survives old key-up reentry");Require(tail==0,"older key-up cannot bubble after replacement"); });
                foreach(bool consume in new[]{false,true})
                Attempt(consume?"window-up-consumed":"window-up",()=>
                {
                    Prepare("",Key.Enter);Send(sourceView,Key.Enter);phase=consume?"window-up-consumed":"window-up";
                    owner.ConsumeUp=consume;tail=newExec=0;Send(sourceView,Key.Enter,up:true);
                    Require(nested&&newExec==1,"new shortcut completes inside the window key-up hook");
                    Require(!target.IsPressed&&tail==0,"window hook retirement clears the old press and stops its route");owner.ConsumeUp=false;
                });
                Attempt("consumed-up",()=>
                {
                    Prepare("",Key.Enter);Send(sourceView,Key.Enter);phase="consumed-up";owner.ConsumeUp=true;tail=0;
                    Send(sourceView,Key.Enter,up:true);Require(!target.IsPressed&&tail==0,"consuming window key-up releases its own press without bubbling");owner.ConsumeUp=false;
                });
                Attempt("text",()=> { Prepare("text",Key.A,"text");string before=target.Text;Send(sourceView,Key.A,text:"旧");Require(nested&&newEvents==1&&tail==0,"newer native text replaces the old text event before bubbling");Require(target.Text==before+"新","editor receives only the new Chinese text"); });
                foreach(var item in new[]{(Name:"default-enter",Key:Key.Enter),(Name:"default-tab",Key:Key.Tab)})
                Attempt(item.Name,()=> { Prepare(item.Name,item.Key);Send(sourceView,item.Key);Require(nested&&newExec==1,"new shortcut completes before old default handling");Require(clicks==0&&ReferenceEquals(Keyboard.FocusedElement,target),"stale default button and focus navigation do not run"); });
                Attempt("pressed-down",()=> { Prepare("pressed-down",Key.Enter,"space");Send(sourceView,Key.Enter);Require(nested&&target.IsPressed,"old down cleanup preserves the newer space press");phase="";Send(sourceView,Key.Space,up:true);Require(!target.IsPressed,"newest space release clears the pressed state"); });
                Attempt("pressed-up-cross-host",()=> { Prepare("",Key.Enter);Send(sourceView,Key.Enter);phase="pressed-up-cross-host";replacement="space";nested=false;Send(sourceView,Key.Enter,up:true);Require(nested&&target.IsPressed,"old up cleanup preserves the newest logical-owner press across physical hosts");phase="";Send(ownerView,Key.Space,up:true);Require(!target.IsPressed,"latest release clears shared logical-owner pressed state"); });
                Attempt("normal-controls",()=> { Prepare("",Key.K);Send(sourceView,Key.K,command:true);Require(oldExec==1,"unreplaced native shortcut executes once");Send(sourceView,Key.Enter);Require(clicks==1,"unreplaced native Enter still invokes the default button");Send(sourceView,Key.Enter,up:true);Require(target.Text.Contains("中文🙂"),"Chinese and emoji content remains available"); });
                phase="";status.Text="检查已完成，文字仍可编辑。";target.Focus();Capture("source-final",surface);if(surface!=owner)Capture("owner-final",owner);Finish();
            }
            catch(Exception error){failures.Add(error.ToString());try{if(surface!=null)Capture("failure-readback",surface);}catch(Exception capture){failures.Add(capture.ToString());}Finish();}
        });
        try{app.Run();return failures.Count==0?0:1;}
        finally{pulse.Invalidate();if(popup!=null)popup.IsOpen=false;owner.Close();using var note=NSNotification.FromName(NSApplication.WillTerminateNotification,app);host.WillTerminate(note);app.Delegate=null!;}
    }
    private sealed class ProbeWindow:Window
    {
        internal Action<Key>? Preview,PreviewRelease;internal bool ConsumeUp;
        protected override bool OnPreviewWindowKeyDown(Key key,ModifierKeys modifiers,bool isRepeat){Preview?.Invoke(key);return false;}
        protected override bool OnPreviewWindowKeyUp(Key key,ModifierKeys modifiers){PreviewRelease?.Invoke(key);return ConsumeUp;}
    }
    private sealed class Command(Func<bool> query,Action execute):ICommand
    { public bool CanExecute(object? p)=>query();public void Execute(object? p)=>execute();public event EventHandler? CanExecuteChanged {add{}remove{}} }
    private sealed class Delegate:JaliumMacApplicationDelegate
    {
        private bool started;
        public override void DidFinishLaunching(NSNotification note){if(started)return;started=true;base.DidFinishLaunching(note);}
        protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings {DisableDefaults=true}).Build().UseApplication(new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown});
    }
}
