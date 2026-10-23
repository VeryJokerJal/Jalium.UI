using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Actual NSWindow/NSView, with an explicitly controlled managed owner DPI.
// Screen hardware, input injection and physical mixed-DPI acceptance are separate.
internal static class WindowPlacementBoundaryChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || (uint)index>=12) return 2;
        string output=Environment.GetEnvironmentVariable("JALIUM_PLACEMENT_OUTPUT")!;Directory.CreateDirectory(output);
        bool custom=index%2==1,software=index>=6;double simulatedOwnerScale=new[]{1d,1.5d,2d}[index%6/2];
        var app=NSApplication.SharedApplication;app.ActivationPolicy=NSApplicationActivationPolicy.Regular;
        using var hostDelegate=new HostDelegate();app.Delegate=hostDelegate;app.FinishLaunching();
        using(var note=NSNotification.FromName(NSApplication.DidFinishLaunchingNotification,app))hostDelegate.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software?RenderBackend.Software:RenderBackend.Metal,forceReplace:true);
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var combo=new ComboBox { Width=300,Height=40 };Name(combo,"重复项目选择","boundary-combo");
        var anchor=new Button { Content="展开编辑区域",Width=150,Height=40 };Name(anchor,"展开编辑区域","boundary-anchor");
        var canvas=new Canvas { Height=420 };canvas.Children.Add(anchor);Canvas.SetLeft(anchor,40);Canvas.SetTop(anchor,60);
        var panel=new StackPanel { Margin=new Thickness(24),Spacing=14 };panel.Children.Add(new TextBlock { Text="窗口位置与选择",FontSize=24 });panel.Children.Add(combo);panel.Children.Add(canvas);
        var actions=new WrapPanel { ItemWidth=154,ItemHeight=44 };
        Button Action(string text,string id){var b=new Button{Content=text,Width=148,Height=40};Name(b,text,id);actions.Children.Add(b);return b;}
        var move=Action("移动锚点","boundary-move");var reopen=Action("重新打开","boundary-reopen");var capture=Action("记录画面","boundary-capture");var next=Action("下一组项目","boundary-next");panel.Children.Add(actions);
        var owner=new Window { Title="Jalium Placement Boundary "+index,Left=60,Top=60,Width=760,Height=740,MinWidth=520,MinHeight=680,TitleBarStyle=custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel };
        owner.SetResourceReference(Window.BackgroundProperty,"SolidBackgroundFillColorBaseBrush");Application.Current!.MainWindow=owner;
        var editor=new TextBox { Text="保留中文🙂 Miii",FontSize=20,Height=48 };Name(editor,"弹窗文字","boundary-editor");
        var cardContent=new StackPanel { Margin=new Thickness(16),Spacing=12 };cardContent.Children.Add(new TextBlock { Text="编辑与定位",FontSize=22 });cardContent.Children.Add(editor);cardContent.Children.Add(new TextBlock { Text="移动和重新打开后保留文字与选区。",TextWrapping=TextWrapping.Wrap });
        var card=new Border { Width=300,Height=210,CornerRadius=new CornerRadius(8),Child=cardContent,BorderThickness=new Thickness(1) };card.SetResourceReference(Border.BackgroundProperty,"LayerFillColorDefaultBrush");card.SetResourceReference(Border.BorderBrushProperty,"ControlStrokeColorDefaultBrush");
        var popup=new Popup { PlacementTarget=anchor,Placement=PlacementMode.Bottom,Child=card,PreferExternalWindow=true,ShouldConstrainToRootBounds=false,StaysOpen=true,PopupAnimation=PopupAnimation.None };
        var dpi=typeof(Window).GetField("_dpiScale",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var getSurface=typeof(Window).GetMethod("GetMacOSPhysicalWindow",BindingFlags.Static|BindingFlags.NonPublic)!;
        var checks=new List<string>();var failures=new List<string>();var scenarios=new List<object>();ObservableCollection<object> source=new();
        int dataKind=-1,pending=0,phase=0,captures=0;bool running=false,closed=false;Window? surface=null;NSView? ownerView=null,popupView=null;CGPoint expectedPoint=default;
        var watch=Stopwatch.StartNew();
        void Trace(string message)=>File.AppendAllText(Path.Combine(output,"progress.log"),watch.Elapsed.TotalSeconds.ToString("F3")+" "+message+Environment.NewLine);
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        void Attempt(string name,Action action){int start=checks.Count;Exception? error=null;try{action();}catch(Exception e){error=e;failures.Add("kind="+dataKind+" "+name+": "+e);}scenarios.Add(new{dataKind,name,passed=error is null,checks=checks.Skip(start).ToArray(),error=error?.ToString()});}
        void Controlled(Action action){double before=owner.DpiScale;dpi.SetValue(owner,simulatedOwnerScale);try{action();}finally{dpi.SetValue(owner,before);}}
        CGPoint Desired()
        {
            Point logical=anchor.TranslatePoint(new Point(0,anchor.ActualHeight),owner);
            var root=ownerView!.Window!.ConvertPointToScreen(ownerView.ConvertPointToView(CGPoint.Empty,null));
            double scale=ownerView.Window.BackingScaleFactor;
            return new CGPoint(root.X+logical.X*simulatedOwnerScale/scale,root.Y-logical.Y*simulatedOwnerScale/scale);
        }
        void ReadSurface(){surface=(Window?)getSurface.Invoke(null,[editor]);popupView=surface is { Handle: var handle } && handle!=0?Runtime.GetNSObject<NSView>(handle):null;}
        void VerifyPoint(string stage)
        {
            ReadSurface();Require(surface!=null&&!ReferenceEquals(surface,owner)&&popupView?.Window!=null,"external popup has its own actual native window");
            var frame=popupView!.Window!.Frame;double error=Math.Max(Math.Abs(frame.X-expectedPoint.X),Math.Abs(frame.Y+frame.Height-expectedPoint.Y));
            Require(error<=1.1,stage+" uses the requested physical point; error="+error.ToString("F3")+" native="+frame+" expected="+expectedPoint);
            Require(Math.Abs(popupView.Bounds.Width-300)<1.1&&Math.Abs(popupView.Bounds.Height-210)<1.1,"native popup keeps its logical client size");
        }
        void Open()
        {
            Controlled(()=>{expectedPoint=Desired();popup.IsOpen=true;surface=(Window?)getSurface.Invoke(null,[editor]);surface?.UpdateLayout();});
        }
        void Record()
        {
            ReadSurface();var frame=popupView?.Window?.Frame??CGRect.Empty;var local=surface is null?default:editor.TranslatePoint(default,surface);
            double desktopTop=NSScreen.Screens[0].Frame.Y+NSScreen.Screens[0].Frame.Height;
            var state=new{index,custom,software,simulatedOwnerScale,actualOwnerScale=owner.DpiScale,actualPopupScale=surface?.DpiScale,dataKind,pending,running,closed,phase,captures,processId=Environment.ProcessId,appRunning=app.Running,
                expectedX=expectedPoint.X,expectedY=expectedPoint.Y,nativeX=frame.X,nativeY=frame.Y+frame.Height,expectedAXEditorX=(double)frame.X+local.X,expectedAXEditorY=desktopTop-(double)(frame.Y+frame.Height)+local.Y,
                ownerNativeKey=ownerView?.Window?.IsKeyWindow,popupNativeKey=popupView?.Window?.IsKeyWindow,nativeKeyWindow=(long)(app.KeyWindow?.Handle??0),popupResponder=(long)(popupView?.Window?.FirstResponder?.Handle??0),popupViewHandle=(long)(popupView?.Handle??0),
                selected=combo.SelectedIndex,text=editor.Text,selectionStart=editor.SelectionStart,selectionLength=editor.SelectionLength,focused=editor.IsKeyboardFocused,
                ownerBackend=owner.RenderTarget?.Backend.ToString(),popupBackend=surface?.RenderTarget?.Backend.ToString(),checks,failures,scenarios,
                ownNSViewSDK=true,controlledOwnerDPI=true,physicalMixedDPI=false,physicalInput=false,desktopScreenshot=false,appActive=app.Active,frontmostPID=NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier};
            File.WriteAllText(Path.Combine(output,"state.tmp"),JsonSerializer.Serialize(state));File.Move(Path.Combine(output,"state.tmp"),Path.Combine(output,"state.json"),true);
        }
        void ComboChecks(int shape)
        {
            object one=shape==2?new EqualItem { Content="重复项🙂" }:new Row(7);object two=shape==1?one:shape==2?new EqualItem { Content="重复项🙂" }:new Row(7);object third=new Row(9);
            source=new ObservableCollection<object>{one,two,third};combo.ItemsSource=source;owner.UpdateLayout();
            Attempt("native-combo-arrow",()=>{combo.SelectedIndex=0;Require(combo.Focus(),"combo accepts SDK keyboard focus");using var key=NSEvent.KeyEvent(NSEventType.KeyDown,CGPoint.Empty,0,watch.Elapsed.TotalSeconds+1,ownerView!.Window!.WindowNumber,null,"\uf701","\uf701",false,125)!;ownerView.KeyDown(key);Require(combo.SelectedIndex==1&&ReferenceEquals(combo.SelectedItem,two),"actual NSView arrow selects the second occurrence");});
            Attempt("combo-collection-continuation",()=>{
                combo.SelectedIndex=1;source.RemoveAt(0);Require(combo.SelectedIndex==0&&ReferenceEquals(combo.SelectedItem,two),"removing equal earlier row retains selected occurrence");
                source.Insert(0,one);Require(combo.SelectedIndex==1&&ReferenceEquals(combo.SelectedItem,two),"inserting before selection only shifts index");
                source[0]=new Row(7);Require(combo.SelectedIndex==1&&ReferenceEquals(combo.SelectedItem,two),"replacing equal sibling never retargets selection");
                object replacement=shape==2?new EqualItem { Content="替换项🙂" }:new Row(7);source[1]=replacement;Require(combo.SelectedIndex==1&&ReferenceEquals(combo.SelectedItem,replacement),"replacement retains the selected slot");
                source.Move(1,0);Require(combo.SelectedIndex==0&&ReferenceEquals(combo.SelectedItem,replacement),"moving selected row retains its identity");
                source.RemoveAt(0);Require(combo.SelectedIndex==-1&&combo.SelectedItem is null,"removing selected occurrence clears it");});
        }
        void Begin(int shape)
        {
            Trace("begin "+shape);popup.IsOpen=false;dataKind=shape;ComboChecks(shape);Trace("combo complete "+shape);Canvas.SetLeft(anchor,40);owner.UpdateLayout();Open();Trace("popup opened "+shape);
            Attempt("initial-popup-physical-position",()=>VerifyPoint("first open"));
            Attempt("actual-popup-backend",()=>Require(owner.RenderTarget?.Backend==(software?RenderBackend.Software:RenderBackend.Metal)&&surface?.RenderTarget?.Backend==(software?RenderBackend.Software:RenderBackend.Metal),"actual owner and popup use the requested backend"));
            editor.Select(1,2);Attempt("popup-sdk-focus",()=>Require(editor.Focus(),"popup editor receives focus after placement"));
            next.IsEnabled=shape<2;phase++;
        }
        void Move()
        {
            Controlled(()=>{Canvas.SetLeft(anchor,Canvas.GetLeft(anchor)+24);owner.UpdateLayout();expectedPoint=Desired();popup.UpdatePosition();});
            Attempt("live-popup-physical-position",()=>VerifyPoint("live move"));phase++;Record();
        }
        void Reopen()
        {
            var previous=surface;popup.IsOpen=false;Open();
            Attempt("reopened-popup-physical-position",()=>{Require(!ReferenceEquals(previous,surface),"reopen creates a new managed surface");VerifyPoint("reopen");Require(editor.Text=="保留中文🙂 Miii","popup content survives reopen");});
            editor.Select(1,2);editor.Focus();phase++;Record();
        }
        void Capture()
        {
            void Save(Window window,string name)
            {
                var bitmap=MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth*window.DpiScale),(int)Math.Ceiling(window.ActualHeight*window.DpiScale),window.DpiScale,
                    drawing=>{drawing.DrawRectangle(window.Background,null,new Rect(0,0,window.ActualWidth,window.ActualHeight));window.Render(drawing);},()=>window.Handle!=0)??throw new InvalidOperationException("backend readback unavailable");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);
            }
            Save(owner,"kind-"+dataKind+"-owner");Save(surface!,"kind-"+dataKind+"-popup");captures++;Record();
        }
        move.Click+=(_,_)=>Move();reopen.Click+=(_,_)=>Reopen();capture.Click+=(_,_)=>Capture();next.Click+=(_,_)=>{if(dataKind<2)pending=dataKind+1;};
        owner.Show();owner.UpdateLayout();Trace("owner shown");
        using var pulse=NSTimer.CreateRepeatingScheduledTimer(.03,_=>{
            if(running||closed)return;
            try{
                if(File.Exists(Path.Combine(output,"finish.signal"))||watch.Elapsed.TotalSeconds>120){if(watch.Elapsed.TotalSeconds>120)failures.Add("host watchdog expired");closed=true;try{popup.IsOpen=false;owner.Close();Record();File.WriteAllText(Path.Combine(output,"host-result.json"),File.ReadAllText(Path.Combine(output,"state.json")));}catch(Exception error){Trace("shutdown "+error);failures.Add(error.ToString());}finally{app.Stop(app);using var wake=NSEvent.OtherEvent(NSEventType.ApplicationDefined,default,0,0,0,null,0,0,0);app.PostEvent(wake,false);}return;}
                if(owner.RenderTarget==null||anchor.ActualHeight==0)return;ownerView=Runtime.GetNSObject<NSView>(owner.Handle);if(ownerView?.Window==null)return;
                if(pending>=0){running=true;int shape=pending;pending=-1;Begin(shape);running=false;}Record();
            }catch(Exception error){failures.Add(error.ToString());running=false;Record();}
        });
        try{app.Run();return failures.Count==0?0:1;}finally{pulse.Invalidate();popup.IsOpen=false;owner.Close();using var note=NSNotification.FromName(NSApplication.WillTerminateNotification,app);hostDelegate.WillTerminate(note);app.Delegate=null!;}
    }
    private static void Name(DependencyObject value,string text,string id){AutomationProperties.SetName(value,text);AutomationProperties.SetAutomationId(value,id);}
    private sealed record Row(int Key){public override string ToString()=>"重复项🙂";}
    private sealed class EqualItem:ComboBoxItem{public override bool Equals(object? value)=>value is EqualItem;public override int GetHashCode()=>7;}
    private sealed class HostDelegate:JaliumMacApplicationDelegate
    {private bool started;public override void DidFinishLaunching(NSNotification note){if(started)return;started=true;base.DidFinishLaunching(note);}protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings{DisableDefaults=true}).Build().UseApplication(new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown});}
}
