using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Sends SDK messages to our own NSView. The helper is test-only and never
// belongs to the production native payload. No HID/CGEvent injection is used.
internal static class WindowTrackpadGestureChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendSample(nint view, int boundary, int kind, int phase,
        double magnification, double rotation, double x, double y, double timestamp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertPoint(nint source, nint destination, double x, double y, out double outX, out double outY);

    internal static int Run(bool software = false)
    {
        string bridge = Environment.GetEnvironmentVariable("JALIUM_GESTURE_TEST_BRIDGE")
            ?? throw new InvalidOperationException("JALIUM_GESTURE_TEST_BRIDGE must identify the test-only SDK helper.");
        string output = Environment.GetEnvironmentVariable("JALIUM_GESTURE_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_GESTURE_OUTPUT is required.");
        Directory.CreateDirectory(output);
        nint helper = NativeLibrary.Load(bridge);
        var send = Marshal.GetDelegateForFunctionPointer<SendSample>(NativeLibrary.GetExport(helper,"jalium_test_apple_gesture_send"));
        var convert = Marshal.GetDelegateForFunctionPointer<ConvertPoint>(NativeLibrary.GetExport(helper,"jalium_test_apple_gesture_convert_point"));
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();app.Delegate=host;app.FinishLaunching();
        using(var launch=NSNotification.FromName(NSApplication.DidFinishLaunchingNotification,app))host.DidFinishLaunching(launch);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var checks = new List<string>();var failures = new List<string>();var states = new List<object>();
        var cases = new Queue<(bool Custom,string Kind)>();
        foreach(bool custom in new[]{false,true})foreach(string kind in new[]{"owner","overlay","native"})cases.Enqueue((custom,kind));
        Window? owner=null,source=null;Popup? popup=null;Border? subject=null;Canvas? canvas=null;
        int completed=0;string label="";var wait=Stopwatch.StartNew();
        void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException(message);checks.Add(label+": "+message);Console.WriteLine("PASS gestures "+label+": "+message);}
        void Next()
        {
            if(popup!=null)popup.IsOpen=false;popup=null;owner?.Close();owner=source=null;subject=null;
            if(!cases.TryDequeue(out var item))
            {
                File.WriteAllText(Path.Combine(output,"host-result.json"),JsonSerializer.Serialize(new
                {completed,expected=6,passed=checks.Count,failures,checks,states,physicalInput=false,backend=software?"Software":"Metal",nativeLibraries=NativeLibraries(),pid=Environment.ProcessId}));
                app.Stop(app);return;
            }
            var context=RenderContext.GetOrCreateCurrent(software?RenderBackend.Software:RenderBackend.Metal,forceReplace:true);
            if(!software)context.DefaultRenderingEngine=RenderingEngine.Impeller;
            label=$"custom={item.Custom},kind={item.Kind}";
            var text=new TextBlock{Text="缩放与旋转 · 中文🙂",FontSize=20,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            subject=new Border{Width=220,Height=110,IsManipulationEnabled=true,CornerRadius=new CornerRadius(10),
                Background=new SolidColorBrush(Color.FromRgb(45,110,165)),Child=text,RenderTransformOrigin=new Point(.5,.5)};
            canvas=new Canvas{Height=190,ClipToBounds=true};
            var anchor=new Button{Content="手势区域",Width=90,Height=32};canvas.Children.Add(anchor);
            if(item.Kind=="owner"){canvas.Children.Add(subject);Canvas.SetLeft(subject,50);Canvas.SetTop(subject,40);}
            var panel=new StackPanel{Margin=new Thickness(20),Spacing=8};
            panel.Children.Add(new TextBlock{Text="窗口手势检查",FontSize=18});panel.Children.Add(canvas);
            panel.Children.Add(new TextBox{Text="编辑与键盘焦点保持独立",Height=36});
            panel.Children.Add(new TextBlock{Text="检查缩放、旋转、取消和新序列。",TextWrapping=TextWrapping.Wrap});
            owner=new Window{Title="Jalium Window v246 · 手势",Width=640,Height=460,MinWidth=520,MinHeight=420,
                TitleBarStyle=item.Custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel,ShowActivated=false};
            Application.Current!.MainWindow=owner;owner.Show();owner.UpdateLayout();
            Canvas.SetLeft(anchor,item.Kind=="native"?Math.Max(0,canvas.ActualWidth-45):30);Canvas.SetTop(anchor,5);
            if(item.Kind!="owner")
            {
                popup=new Popup{PlacementTarget=anchor,Placement=PlacementMode.Right,Child=subject,StaysOpen=true,
                    ShouldConstrainToRootBounds=item.Kind=="overlay",PreferExternalWindow=item.Kind=="native"};popup.IsOpen=true;owner.UpdateLayout();
            }
            source=Window.GetMacOSPhysicalWindow(subject);wait.Restart();
        }
        using var timer=NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(100),_=>
        {
            if(owner==null||subject==null||canvas==null)return;
            source=Window.GetMacOSPhysicalWindow(subject);
            if(source==null||source.Handle==0||subject.ActualWidth<=0)
            {if(wait.Elapsed.TotalSeconds<10)return;failures.Add(label+": actual popup/subject never became ready");Next();return;}
            try
            {
                var currentOwner=owner;var currentSource=source;var currentSubject=subject;var currentCanvas=canvas;
                bool external=label.EndsWith("kind=native",StringComparison.Ordinal);
                Require(source.IsMacOSPopupHost==external,"expected physical popup host is installed");
                Require(RenderContext.Current?.Backend==(software?RenderBackend.Software:RenderBackend.Metal),"actual render backend matches the requested case");
                var native=ObjCRuntime.Runtime.GetNSObject<NSView>(source.Handle)!;
                Require(native.Window!=null&&native.Window.IsVisible,"actual NSView belongs to a visible AppKit window");
                states.Add(new{label,appActive=app.Active,nativeKey=app.KeyWindow?.Handle==native.Window!.Handle,
                    frontPid=NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,hostPid=Environment.ProcessId});
                var deltas=new List<ManipulationDeltaEventArgs>();var ends=new List<ManipulationCompletedEventArgs>();int started=0,contacts=0;
                var focus=Keyboard.FocusedElement;double visualScale=1,visualRotation=0;
                currentSubject.AddHandler(UIElement.ManipulationStartingEvent,new RoutedEventHandler((_,e)=>
                    ((ManipulationStartingEventArgs)e).ManipulationContainer=currentCanvas));
                currentSubject.AddHandler(UIElement.ManipulationStartedEvent,new RoutedEventHandler((_,_)=>started++));
                currentSubject.AddHandler(UIElement.ManipulationDeltaEvent,new RoutedEventHandler((_,e)=>
                {
                    var args=(ManipulationDeltaEventArgs)e;deltas.Add(args);contacts+=args.Manipulators.Count();
                    visualScale*=args.DeltaManipulation.Scale.X;visualRotation+=args.DeltaManipulation.Rotation;
                    currentSubject.RenderTransform=new TransformGroup{Children={new ScaleTransform(visualScale,visualScale),new RotateTransform(visualRotation)}};
                }));
                currentSubject.AddHandler(UIElement.ManipulationCompletedEvent,new RoutedEventHandler((_,e)=>ends.Add((ManipulationCompletedEventArgs)e)));
                Point WindowPoint()=>currentSubject.TranslatePoint(new(currentSubject.ActualWidth/2,currentSubject.ActualHeight/2),currentSource);
                double timestamp=1;
                void Send(int phase,double magnification=0,double rotation=0,int kind=1,int boundary=0)
                {
                    var point=WindowPoint();timestamp+=.02;
                    Require(send(currentSource.Handle,boundary,kind,phase,magnification,rotation,point.X,point.Y,timestamp)==0,"own SDK message dispatched");
                }
                var initialPoint=WindowPoint();
                Require(convert(source.Handle,owner.Handle,initialPoint.X,initialPoint.Y,out double ox,out double oy)==0,"independent AppKit cross-window conversion");
                var expectedOrigin=owner.TranslatePoint(new(ox,oy),canvas);
                Send(0,boundary:1,kind:0);Send(1,.25);Send(4,rotation:30,kind:2);Send(4,-.1);
                Send(8);Send(8,kind:2);Send(0,boundary:2,kind:0);
                Require(started==1&&ends.Count==1,"actual NSView reaches one managed manipulation");
                if(external)Require(native.Window!.CanBecomeKeyWindow,"non-focusable gesture content permits native key delivery");
                Require(Math.Abs(ends[0].TotalManipulation.Scale.X-1.125)<1e-6&&Math.Abs(ends[0].TotalManipulation.Rotation+30)<1e-6,"native scale and clockwise rotation accumulate");
                Require(deltas.All(e=>ReferenceEquals(e.ManipulationContainer,currentCanvas)),"requested logical container is retained");
                Require(deltas.Count>0&&Math.Abs(deltas[0].ManipulationOrigin.X-expectedOrigin.X)<.01&&Math.Abs(deltas[0].ManipulationOrigin.Y-expectedOrigin.Y)<.01,"origin agrees with independent AppKit view conversion");
                Require(contacts==0&&!Manipulation.IsManipulationActive(subject),"indirect input has no fabricated contacts or retained session");
                Require(Math.Abs(subject.RenderTransform.Value.M11-1.125*Math.Cos(Math.PI/6))<1e-6,"real control transform reflects SDK deltas");
                int before=deltas.Count;Send(1,.1);Send(16);int cancelled=deltas.Count;Send(4,.2);
                Require(cancelled==before+1&&deltas.Count==cancelled&&ends.Count==2,"cancelled SDK tail cannot change the control");
                Send(0,boundary:1,kind:0);Send(1,.1);subject.IsEnabled=false;subject.IsEnabled=true;int retired=deltas.Count;Send(4,.3);
                Require(deltas.Count==retired&&!Manipulation.IsManipulationActive(subject),"managed target roundtrip retires native sequence");
                Send(0,boundary:1,kind:0);Send(1,.2);Send(8);Send(0,boundary:2,kind:0);
                Require(started==4&&ends.Count==4,"fresh SDK gesture survives earlier cancellation");
                Require(ReferenceEquals(focus,Keyboard.FocusedElement),"gestures preserve keyboard focus");
                owner.Width=owner.MinWidth;owner.Height=owner.MinHeight;owner.UpdateLayout();
                source.UpdateLayout();
                Require(owner.ActualWidth>=owner.MinWidth&&owner.ActualHeight>=owner.MinHeight,"minimum window geometry remains usable");
                var dpi=currentSource.DpiScale;
                var bitmap=MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(source.ActualWidth*dpi),
                    (int)Math.Ceiling(source.ActualHeight*dpi),dpi,dc=>source.Render(dc),()=>source.Handle!=0);
                Require(bitmap!=null,"actual native backend renders the resulting control tree");
                var rendered=new byte[bitmap!.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(rendered,bitmap.PixelWidth*4,0);
                int subjectPixels=0;
                for(int index=0;index<rendered.Length;index+=4)
                    if(rendered[index+3]>200&&Math.Abs(rendered[index]-165)<=3&&Math.Abs(rendered[index+1]-110)<=3&&Math.Abs(rendered[index+2]-45)<=3)subjectPixels++;
                Require(subjectPixels>=250,"captured affine subject retains its visible blue fill");
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap!));
                using(var file=File.Create(Path.Combine(output,$"{completed}-gesture.png")))encoder.Save(file);
                completed++;
            }
            catch(Exception error){failures.Add(label+": "+error);Console.Error.WriteLine("FAIL gestures "+label+": "+error);}
            Next();
        });
        app.BeginInvokeOnMainThread(Next);app.Run();
        Console.WriteLine($"macOS trackpad SDK host: {completed}/6; {checks.Count} checks; {failures.Count} failures");
        return failures.Count==0&&completed==6?0:1;
    }

    private static Dictionary<string,string> NativeLibraries()
    {
        var result=new Dictionary<string,string>();for(uint i=0;i<ImageCount();i++)
        {string? path=Marshal.PtrToStringUTF8(ImageName(i));if(path!=null&&Path.GetFileName(path).StartsWith("libjalium.native.",StringComparison.Ordinal))result[Path.GetFileName(path)]=Path.GetFullPath(path);}
        return result;
    }
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_image_count")]private static extern uint ImageCount();
    [DllImport("/usr/lib/libSystem.B.dylib",EntryPoint="_dyld_get_image_name")]private static extern nint ImageName(uint index);
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {if(_started)return;_started=true;base.DidFinishLaunching(notification);}
        protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings{DisableDefaults=true}).Build()
            .UseApplication(new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown});
    }
}
