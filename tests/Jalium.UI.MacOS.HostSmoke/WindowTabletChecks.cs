using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Ink;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Calls our own NSView with a test-only NSEvent. No physical input is claimed.
internal static class WindowTabletChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendSample(nint view, int kind, uint device, int tool, int entering, uint buttons,
        float pressure, float tiltX, float tiltY, float rotation, double x, double y,
        double timestamp, int button, int clicks);

    internal static int Run(bool software = false)
    {
        string output = Environment.GetEnvironmentVariable("JALIUM_TABLET_OUTPUT") ?? throw new InvalidOperationException("JALIUM_TABLET_OUTPUT is required.");
        string bridge = Environment.GetEnvironmentVariable("JALIUM_GESTURE_TEST_BRIDGE") ?? throw new InvalidOperationException("The test-only SDK bridge is required.");
        Directory.CreateDirectory(output);
        nint helper = NativeLibrary.Load(bridge);
        var send = Marshal.GetDelegateForFunctionPointer<SendSample>(NativeLibrary.GetExport(helper,"jalium_test_apple_tablet_send"));
        var app = NSApplication.SharedApplication;app.ActivationPolicy=NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();app.Delegate=host;app.FinishLaunching();
        using(var launch=NSNotification.FromName(NSApplication.DidFinishLaunchingNotification,app))host.DidFinishLaunching(launch);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var checks=new List<string>();var failures=new List<string>();var states=new List<object>();
        var cases=new Queue<(bool Custom,string Kind)>();
        foreach(bool custom in new[]{false,true})foreach(string kind in new[]{"owner","overlay","native"})cases.Enqueue((custom,kind));
        Window? owner=null,source=null;Popup? popup=null;Border? subject=null;Canvas? area=null;
        Button? button=null;TextBox? edit=null;InkCanvas? ink=null;TextBlock? status=null;
        string label="";int completed=0;var wait=Stopwatch.StartNew();
        void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(label+": "+message);Console.WriteLine("PASS tablet "+label+": "+message);}
        void Next()
        {
            if(popup!=null)popup.IsOpen=false;popup=null;owner?.Close();owner=source=null;
            if(!cases.TryDequeue(out var item))
            {
                File.WriteAllText(Path.Combine(output,"host-result.json"),JsonSerializer.Serialize(new{completed,expected=6,passed=checks.Count,failures,checks,states,
                    physicalInput=false,backend=software?"Software":"Metal",nativeLibraries=NativeLibraries(),pid=Environment.ProcessId}));
                app.Stop(app);
                // A timer does not dispatch an NSEvent, so Stop alone cannot
                // make NSApplication.Run observe its termination flag.
                using var wake=NSEvent.OtherEvent(NSEventType.ApplicationDefined,default,0,0,0,null,0,0,0);
                app.PostEvent(wake,false);return;
            }
            var context=RenderContext.GetOrCreateCurrent(software?RenderBackend.Software:RenderBackend.Metal,forceReplace:true);
            if(!software)context.DefaultRenderingEngine=RenderingEngine.Impeller;
            label=$"custom={item.Custom},kind={item.Kind}";
            button=new Button{Content="笔点击：0",Height=32};edit=new TextBox{Text="笔输入与编辑 · 中文🙂",Height=34};
            ink=new InkCanvas{Height=180,Background=new SolidColorBrush(Color.FromRgb(240,246,252)),
                DefaultDrawingAttributes=new DrawingAttributes{Color=Color.FromRgb(32,96,170),Width=5,Height=5,IgnorePressure=false}};
            status=new TextBlock{Text="悬停不按下；压力、倾斜与笔端状态保留。",TextWrapping=TextWrapping.Wrap};
            var content=new StackPanel{Margin=new Thickness(10),Spacing=6};
            content.Children.Add(new TextBlock{Text="数位板笔输入",FontSize=18});content.Children.Add(button);content.Children.Add(edit);content.Children.Add(ink);content.Children.Add(status);
            subject=new Border{Width=440,Height=340,CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Color.FromRgb(38,38,42)),Child=content};
            area=new Canvas{Height=360,ClipToBounds=true};var anchor=new Button{Content="笔输入区域",Width=110,Height=32};area.Children.Add(anchor);
            if(item.Kind=="owner"){area.Children.Add(subject);Canvas.SetLeft(subject,10);Canvas.SetTop(subject,8);}
            var panel=new StackPanel{Margin=new Thickness(16),Spacing=8};panel.Children.Add(new TextBlock{Text="窗口与数位板",FontSize=18});panel.Children.Add(area);
            panel.Children.Add(new TextBlock{Text="检查笔事件、单次点击、墨迹压力及取消恢复。",TextWrapping=TextWrapping.Wrap});
            owner=new Window{Title="Jalium Window v250 · 数位板",Width=680,Height=620,MinWidth=560,MinHeight=560,
                TitleBarStyle=item.Custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel};
            Application.Current!.MainWindow=owner;owner.Show();owner.UpdateLayout();
            if(item.Kind!="owner")
            {
                Canvas.SetLeft(anchor,item.Kind=="native"?area.ActualWidth-50:5);Canvas.SetTop(anchor,2);
                popup=new Popup{Child=subject,PlacementTarget=anchor,Placement=PlacementMode.Right,StaysOpen=true,
                    ShouldConstrainToRootBounds=item.Kind=="overlay",PreferExternalWindow=item.Kind=="native"};popup.IsOpen=true;owner.UpdateLayout();
            }
            source=Window.GetMacOSPhysicalWindow(subject);wait.Restart();
        }
        Action? continuation=null;
        using var timer=NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100),_=>
        {
            if(continuation!=null)
            {
                Action step=continuation;continuation=null;
                try{step();}catch(Exception error){continuation=null;failures.Add(label+": "+error);Console.Error.WriteLine("FAIL deferred tablet "+label+": "+error);}
                if(continuation==null)Next();return;
            }
            if(owner==null||subject==null||ink==null||edit==null||button==null||status==null)return;
            source=Window.GetMacOSPhysicalWindow(subject);
            if(source==null||source.Handle==0||ink.ActualWidth<=0){if(wait.Elapsed.TotalSeconds<10)return;failures.Add(label+": tablet content never became ready");Next();return;}
            try
            {
                var currentOwner=owner;var currentSource=source;var currentInk=ink;var currentButton=button;var currentEdit=edit;
                Require(source.IsMacOSPopupHost==label.EndsWith("kind=native",StringComparison.Ordinal),"expected physical host installed");
                Require(RenderContext.Current?.Backend==(software?RenderBackend.Software:RenderBackend.Metal),"actual render backend matches case");
                var native=ObjCRuntime.Runtime.GetNSObject<NSView>(source.Handle)!;
                Require(native.Window!=null&&native.Window.IsVisible,"visible own AppKit NSView");
                Require(native.Window!.CanBecomeKeyWindow,"focusable tablet content permits native key delivery");
                native.Window.MakeKeyWindow();
                Require(native.Window.MakeFirstResponder(native),"own SDK content responder is ready before dispatch");
                states.Add(new{label,appActive=app.Active,nativeKey=app.KeyWindow?.Handle==native.Window!.Handle,
                    frontPid=NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,hostPid=Environment.ProcessId});
                int clicks=0,penDown=0,penUp=0,mouseDown=0,mouseUp=0,rangeIn=0,rangeOut=0,cancel=0;
                var points=new List<PointerPoint>();var mouseClicks=new List<int>();
                currentButton.Click+=(_,_)=>{clicks++;currentButton.Content="笔点击："+clicks;};
                subject.AddHandler(UIElement.PointerDownEvent,new RoutedEventHandler((_,e)=>{var p=((PointerEventArgs)e).GetCurrentPoint(subject);if(p.PointerDeviceType==PointerDeviceType.Pen){penDown++;points.Add(p);}}));
                subject.AddHandler(UIElement.PointerUpEvent,new RoutedEventHandler((_,e)=>{if(((PointerEventArgs)e).Pointer.PointerDeviceType==PointerDeviceType.Pen)penUp++;}));
                subject.AddHandler(UIElement.PointerMoveEvent,new RoutedEventHandler((_,e)=>{var p=((PointerEventArgs)e).GetCurrentPoint(subject);if(p.PointerDeviceType==PointerDeviceType.Pen)points.Add(p);}));
                subject.AddHandler(UIElement.PointerCancelEvent,new RoutedEventHandler((_,_)=>cancel++));
                subject.AddHandler(UIElement.MouseDownEvent,new MouseButtonEventHandler((_,e)=>{mouseDown++;mouseClicks.Add(e.ClickCount);}),true);
                subject.AddHandler(UIElement.MouseUpEvent,new MouseButtonEventHandler((_,_)=>mouseUp++),true);
                subject.AddHandler(UIElement.StylusInRangeEvent,new StylusEventHandler((_,_)=>rangeIn++),true);
                subject.AddHandler(UIElement.StylusOutOfRangeEvent,new StylusEventHandler((_,_)=>rangeOut++),true);
                double time=1;uint device=1000u+(uint)completed;
                void Send(int kind,UIElement target,Point at,uint buttons=0,float pressure=0,int tool=1,int entering=1,int buttonNumber=0,int clickCount=1)
                {var local=target.TranslatePoint(at,currentSource);time+=.02;Require(send(currentSource.Handle,kind,device,tool,entering,buttons,pressure,.2f,-.4f,-30,local.X,local.Y,time,buttonNumber,clickCount)==0,"own SDK message dispatched");}
                Point Center(FrameworkElement element)=>new(element.ActualWidth/2,element.ActualHeight/2);
                Send(0,currentButton,Center(currentButton));var focus=Keyboard.FocusedElement;
                Send(1,currentButton,Center(currentButton));
                Require(penDown==0&&mouseDown==0&&points.Count==1&&!points[0].IsInContact,"hover is a pen move without a press");
                Require(ReferenceEquals(focus,Keyboard.FocusedElement),"hover preserves keyboard focus");
                Send(2,currentButton,Center(currentButton),1,.625f,clickCount:2);
                Console.WriteLine($"TABLET-DOWN {label}: pen={penDown} pressed={currentButton.IsPressed} mouse={mouseDown} cancel={cancel} ids={string.Join(',',points.Select(p=>p.PointerId))}");
                Require(penDown==1&&currentButton.IsPressed,"pen down reaches Pointer and ordinary Button press");
                Send(4,currentButton,Center(currentButton));
                Console.WriteLine($"TABLET-UP {label}: up={penUp} clicks={clicks} pressed={currentButton.IsPressed} mouse={mouseDown}/{mouseUp} cancel={cancel} ids={string.Join(',',points.Select(p=>p.PointerId))}");
                Require(penUp==1&&clicks==1&&!currentButton.IsPressed&&mouseDown==1&&mouseUp==1,"one pen tap produces one button click");
                Require(mouseClicks.Single()==2,"original driver click count retained");
                var point=points.First(p=>p.IsInContact);
                Require(Math.Abs(point.Properties.Pressure-.625)<1e-6&&Math.Abs(point.Properties.XTilt-18)<.001&&Math.Abs(point.Properties.YTilt+36)<.001&&point.Properties.Twist==330,"pressure tilt and rotation reach routed pointer data");
                Require(points.Select(p=>p.PointerId).Distinct().Count()==1,"hover and contact share pen identity");
                Send(1,currentButton,Center(currentButton));Send(2,currentButton,Center(currentButton),1,.5f);Send(4,currentButton,Center(currentButton));
                Require(clicks==2&&rangeIn==1&&rangeOut==0,"successive contacts preserve one proximity interval");
                Send(0,currentButton,Center(currentButton),entering:0);
                // Range exits are verified on a later timer turn, after the
                // native retirement queue has had an opportunity to run.
                device+=100;
                int beforeMouse=mouseDown;
                Send(0,currentInk,new(20,35));Send(1,currentInk,new(20,35));
                int priorStrokes=currentInk.Strokes.Count;
                Send(2,currentInk,new(20,35),1,.2f);Send(3,currentInk,new(90,55),1,.9f);Send(5,currentInk,new(140,85),1,.4f);Send(4,currentInk,new(180,95));
                Require(currentInk.Strokes.Count==priorStrokes+1,"one pen stroke commits once through real InkCanvas RTS plugins");
                Require(mouseDown==beforeMouse,"handled stylus stroke does not duplicate a mouse stroke");
                var pressures=currentInk.Strokes.Last().StylusPoints.Select(p=>p.PressureFactor).ToArray();
                Require(pressures.Any(p=>Math.Abs(p-.2)<.001)&&pressures.Any(p=>Math.Abs(p-.9)<.001),"committed ink retains native pressure variation");
                Send(2,currentInk,new(35,120),1,.3f);Send(3,currentInk,new(100,135),1,.8f);Send(4,currentInk,new(165,145));
                Require(currentInk.Strokes.Count==priorStrokes+2,"back-to-back strokes commit before the same pen ID is reused");
                device+=100;Send(0,currentInk,new(220,110),tool:3);Send(1,currentInk,new(220,110),tool:3);
                var eraserPoint=points.Last();Require(eraserPoint.Properties.IsEraser&&eraserPoint.Properties.IsInverted&&!eraserPoint.IsInContact,"eraser hover retains physical end and inversion");
                Require(eraserPoint.Properties.XTilt==18&&eraserPoint.Properties.YTilt==-36,"eraser hover retains tilt");
                status.Text=$"笔点击 {clicks} 次 · 墨迹 {currentInk.Strokes.Count} 条 · 压力 {point.Properties.Pressure:F3}";
                void Finish()
                {
                    currentOwner.Width=currentOwner.MinWidth;
                    currentOwner.Height=currentOwner.MinHeight;
                    currentOwner.UpdateLayout();
                    currentSource.UpdateLayout();
                    wait.Restart();
                    var currentPopup=popup;
                    var currentSubject=subject!;
                    var editorText=currentEdit.Text;
                    int selectionStart=currentEdit.SelectionStart,selectionLength=currentEdit.SelectionLength;
                    CoreGraphics.CGPoint Screen(UIElement element,Point point,Window physical)
                    {
                        var local=element.TranslatePoint(point,physical);
                        var view=ObjCRuntime.Runtime.GetNSObject<NSView>(physical.Handle)!;
                        var windowPoint=view.ConvertPointToView(new CoreGraphics.CGPoint(local.X,local.Y),null);
                        return view.Window!.ConvertPointToScreen(windowPoint);
                    }
                    void VerifyAnchor(string phase)
                    {
                        var anchor=(FrameworkElement)currentPopup!.PlacementTarget!;
                        var expected=Screen(anchor,new Point(anchor.ActualWidth,0),currentOwner);
                        var actual=Screen(currentSubject,new Point(),currentSource);
                        states.Add(new{label,phase,expectedX=(double)expected.X,expectedY=(double)expected.Y,
                            actualX=(double)actual.X,actualY=(double)actual.Y});
                        Require(Math.Abs((double)(expected.X-actual.X))<=1&&Math.Abs((double)(expected.Y-actual.Y))<=1,
                            "popup screen origin follows the visible anchor after "+phase);
                        Require(ReferenceEquals(Keyboard.FocusedElement,currentEdit)&&currentEdit.Text==editorText&&
                            currentEdit.SelectionStart==selectionStart&&currentEdit.SelectionLength==selectionLength,
                            "popup layout preserves editor focus text and selection");
                    }
                    void AwaitLayout(Action next)
                    {
                        var root=currentSubject.VisualParent as FrameworkElement;
                        bool pending=currentPopup!=null&&typeof(Popup).GetField("_macOSOwnerCheckToken",
                            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(currentPopup)!=null;
                        bool ready=currentOwner.IsMeasureValid&&currentOwner.IsArrangeValid&&
                            currentSource.IsMeasureValid&&currentSource.IsArrangeValid&&
                            currentSubject.IsMeasureValid&&currentSubject.IsArrangeValid&&
                            root is {IsMeasureValid:true,IsArrangeValid:true}&&!pending;
                        if(ready){next();return;}
                        if(wait.Elapsed.TotalSeconds>=10)throw new InvalidOperationException("popup layout did not settle within the existing readiness budget");
                        continuation=()=>AwaitLayout(next);
                    }
                    void Capture()
                    {
                        status.Text=$"笔点击 {clicks} 次 · 墨迹 {currentInk.Strokes.Count} 条 · 压力 {point.Properties.Pressure:F3}";
                        currentSource.UpdateLayout();
                        if(Environment.GetEnvironmentVariable("JALIUM_POPUP_LAYOUT_OBSERVE")=="1"&&
                            label=="custom=True,kind=overlay"&&!File.Exists(Path.Combine(output,"continue.flag")))
                        {
                            string ready=Path.Combine(output,"observe-ready.json");
                            if(!File.Exists(ready))
                            {
                                var view=ObjCRuntime.Runtime.GetNSObject<NSView>(currentOwner.Handle)!;
                                File.WriteAllText(ready,JsonSerializer.Serialize(new{label,pid=Environment.ProcessId,
                                    windowTitle=currentOwner.Title,windowNumber=(long)view.Window!.WindowNumber,
                                    ownerWidth=currentOwner.ActualWidth,ownerHeight=currentOwner.ActualHeight}));
                            }
                            continuation=Capture;return;
                        }
                        var dpi=currentSource.DpiScale;
                        var bitmap=MacOSWritingToolsBitmapCapture.Capture(
                            (int)Math.Ceiling(currentSource.ActualWidth*dpi),(int)Math.Ceiling(currentSource.ActualHeight*dpi),
                            dpi,dc=>currentSource.Render(dc),()=>currentSource.Handle!=0);
                        Require(bitmap!=null,"real backend renders resulting ink and controls");
                        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                        using(var file=File.Create(Path.Combine(output,$"{completed}-tablet.png")))encoder.Save(file);
                        states.Add(new{label,clicks,penDown,penUp,mouseDown,mouseUp,rangeIn,rangeOut,cancel,strokes=currentInk.Strokes.Count});
                        completed++;
                    }
                    continuation=()=>AwaitLayout(()=>
                    {
                        Require(currentOwner.ActualWidth>=currentOwner.MinWidth&&currentOwner.ActualHeight>=currentOwner.MinHeight,
                            "minimum window remains usable");
                        if(currentPopup?.ShouldConstrainToRootBounds==true)
                        {
                            var origin=currentSubject.TranslatePoint(new Point(),currentOwner);
                            states.Add(new{label,phase="minimum-settled",x=origin.X,y=origin.Y,
                                width=currentSubject.ActualWidth,height=currentSubject.ActualHeight,
                                ownerWidth=currentOwner.ActualWidth,ownerHeight=currentOwner.ActualHeight,
                                rootLeft=Canvas.GetLeft((UIElement)currentSubject.VisualParent!),
                                rootWidth=((FrameworkElement)currentSubject.VisualParent!).Width,
                                rootActualWidth=((FrameworkElement)currentSubject.VisualParent!).ActualWidth,
                                childX=currentSubject.VisualBounds.X,childY=currentSubject.VisualBounds.Y});
                            Require(origin.X>=-.01&&origin.Y>=-.01&&
                                origin.X+currentSubject.ActualWidth<=currentOwner.ActualWidth+.01&&
                                origin.Y+currentSubject.ActualHeight<=currentOwner.ActualHeight+.01,
                                "constrained popup stays inside the settled minimum window");
                        }
                        if(currentPopup?.PlacementTarget is not FrameworkElement anchor)
                        {Capture();return;}
                        Canvas.SetLeft(anchor,-32);Canvas.SetTop(anchor,100);
                        currentOwner.UpdateLayout();wait.Restart();
                        continuation=()=>AwaitLayout(()=>
                        {
                            VerifyAnchor("anchor layout movement");
                            anchor.RenderTransform=new TranslateTransform(18,12);
                            currentOwner.UpdateLayout();wait.Restart();
                            continuation=()=>AwaitLayout(()=>
                            {
                                VerifyAnchor("anchor render transform");
                                int before=clicks;
                                var edge=new Point(currentButton.ActualWidth-2,currentButton.ActualHeight/2);
                                Send(1,currentButton,edge);
                                Send(2,currentButton,edge,1,.45f);
                                Send(4,currentButton,edge);
                                Require(clicks==before+1&&!currentButton.IsPressed,
                                    "the moved popup right edge remains hit testable and clicks once");
                                Require(Mouse.Captured is null,"popup click releases its mouse capture");
                                wait.Restart();
                                continuation=()=>AwaitLayout(Capture);
                            });
                        });
                    });
                }
                continuation=()=>
                {
                    Require(rangeOut>=1,"queued proximity exit reaches the managed stylus pipeline");
                    Require(Tablet.CurrentStylusDevice?.Id==unchecked((int)eraserPoint.PointerId),"an older proximity exit preserves the newer active pen");
                    device+=100;Send(0,currentButton,Center(currentButton));Send(1,currentButton,Center(currentButton));
                    int expectedClicks=clicks;
                    Send(2,currentButton,Center(currentButton),1,.5f);
                    Require(currentButton.IsPressed,"compatibility press is active before hide");
                    currentOwner.Hide();currentOwner.Show();currentOwner.UpdateLayout();
                    if(popup!=null&&!popup.IsOpen){popup.IsOpen=true;currentOwner.UpdateLayout();}
                    wait.Restart();
                    void Restored()
                    {
                        var physical=Window.GetMacOSPhysicalWindow(subject);
                        if(physical==null||physical.Handle==0||subject.ActualWidth<=0)
                        {if(wait.Elapsed.TotalSeconds<10){continuation=Restored;return;}Require(false,"restored tablet surface becomes ready");}
                        currentSource=physical!;
                        var restored=ObjCRuntime.Runtime.GetNSObject<NSView>(currentSource.Handle)!;
                        if(restored.Window==null||!restored.Window.IsVisible)
                        {if(wait.Elapsed.TotalSeconds<10){continuation=Restored;return;}Require(false,"restored native window is visible");}
                        restored.Window!.MakeKeyWindow();Require(restored.Window.MakeFirstResponder(restored),"restored SDK responder ready");
                        int before=penDown;Send(3,currentButton,Center(currentButton),1,.7f);
                        Require(penDown==before&&!currentButton.IsPressed&&clicks==expectedClicks,"held tip after hide/reopen cannot revive an old press");
                        Send(2,currentButton,Center(currentButton),1,.6f);
                        Require(currentButton.IsPressed,"fresh pen down recovers after hide/reopen");
                        continuation=()=>
                        {
                            Require(currentButton.IsPressed,"older queued cancellation cannot release the newer press");
                            Send(4,currentButton,Center(currentButton));
                            Require(clicks==expectedClicks+1&&!currentButton.IsPressed,"fresh press releases and clicks once after recovery");
                            device+=100;Send(0,currentEdit,Center(currentEdit));Send(1,currentEdit,Center(currentEdit));
                            string original=currentEdit.Text;Send(2,currentEdit,Center(currentEdit),1,.4f);Send(4,currentEdit,Center(currentEdit));
                            Require(ReferenceEquals(Keyboard.FocusedElement,currentEdit)&&currentEdit.Text==original,"pen click focuses the editor without changing its text");
                            var preview=(RealTimeInkPreviewStylusPlugIn)typeof(InkCanvas).GetField("_realTimePreviewPlugIn",
                                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(currentInk)!;
                            var renderer=(Jalium.UI.Input.StylusPlugIns.DynamicRenderer)typeof(InkCanvas).GetField("_dynamicRenderer",
                                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(currentInk)!;
                            device+=100;
                            Send(0,currentInk,new(250,25),tool:3);
                            Send(1,currentInk,new(250,25),tool:3);
                            Send(2,currentInk,new(250,25),1,.6f,tool:3);
                            Require(currentInk.ActiveEditingMode==currentInk.EditingModeInverted,
                                "inverted contact selects the configured eraser mode");
                            int beforeExit=rangeOut,beforeCancel=cancel;
                            Send(0,currentInk,new(250,25),tool:3,entering:0);
                            continuation=()=>
                            {
                                Require(rangeOut==beforeExit+1&&cancel==beforeCancel+1,
                                    "contact proximity exit produces one range exit and one cancellation");
                                Require(currentInk.ActiveEditingMode==currentInk.EditingMode&&Tablet.CurrentStylusDevice==null,
                                    "physical range exit restores ink mode and current device");
                                Require(!preview.HasActiveSessions&&renderer.CurrentPreviewStroke==null,
                                    "canceled eraser leaves no ink preview");
                                device+=100;int strokes=currentInk.Strokes.Count;
                                Send(0,currentInk,new(20,35));Send(1,currentInk,new(20,35));
                                Send(2,currentInk,new(20,35),1,.3f);Send(3,currentInk,new(90,55),1,.8f);
                                Require(preview.HasActiveSessions&&renderer.CurrentPreviewStroke!=null,
                                    "normal contact has both real-time and fallback previews");
                                beforeExit=rangeOut;beforeCancel=cancel;
                                Send(0,currentInk,new(90,55),entering:0);
                                continuation=()=>
                                {
                                    Require(rangeOut==beforeExit+1&&cancel==beforeCancel+1,
                                        "incomplete ink contact ends its range exactly once");
                                    Require(!preview.HasActiveSessions&&renderer.CurrentPreviewStroke==null,
                                        "incomplete ink contact discards both previews");
                                    Require(currentInk.Strokes.Count==strokes,
                                        "cancellation never commits the incomplete stroke");
                                    device+=100;Send(0,currentInk,new(20,160));Send(1,currentInk,new(20,160));
                                    Send(2,currentInk,new(20,160),1,.3f);Send(3,currentInk,new(110,165),1,.7f);
                                    Send(4,currentInk,new(180,165));
                                    Require(currentInk.Strokes.Count==strokes+1&&!preview.HasActiveSessions&&
                                        renderer.CurrentPreviewStroke==null,"fresh stroke after cancellation commits once");
                                    Finish();
                                };
                            };
                        };
                    }
                    continuation=Restored;
                };
            }
            catch(Exception error){failures.Add(label+": "+error);Console.Error.WriteLine("FAIL tablet "+label+": "+error);}
            if(continuation==null)Next();
        });
        app.BeginInvokeOnMainThread(Next);app.Run();return failures.Count==0&&completed==6?0:1;
    }
    private static Dictionary<string,string> NativeLibraries(){var result=new Dictionary<string,string>();for(uint i=0;i<ImageCount();i++){string? path=Marshal.PtrToStringUTF8(ImageName(i));if(path!=null&&Path.GetFileName(path).StartsWith("libjalium.native.",StringComparison.Ordinal))result[Path.GetFileName(path)]=Path.GetFullPath(path);}return result;}
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_image_count")]private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_get_image_name")]private static extern nint ImageName(uint i);
    private sealed class ValidationDelegate:JaliumMacApplicationDelegate{private bool started;public override void DidFinishLaunching(NSNotification n){if(started)return;started=true;base.DidFinishLaunching(n);}protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings{DisableDefaults=true}).Build().UseApplication(new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown});}
}
