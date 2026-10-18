using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Selector = Jalium.UI.Controls.Primitives.Selector;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Own NSView SDK events, actual controls and native AX. No system input injection.
internal static class WindowSelectionTransactionChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(),out int index)||(uint)index>=12)return 2;
        string output=Environment.GetEnvironmentVariable("JALIUM_SELECTION_TRANSACTION_OUTPUT")!;Directory.CreateDirectory(output);
        bool custom=index%2==1,software=index>=6;int kind=index%6/2;
        var app=NSApplication.SharedApplication;app.ActivationPolicy=NSApplicationActivationPolicy.Regular;
        using var applicationDelegate=new Delegate();app.Delegate=applicationDelegate;app.FinishLaunching();
        using(var note=NSNotification.FromName(NSApplication.DidFinishLaunchingNotification,app))applicationDelegate.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software?RenderBackend.Software:RenderBackend.Metal,forceReplace:true);Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var list=new ListBox{Height=116,SelectionMode=SelectionMode.Extended};Name(list,"重复项目多选","tx-list");
        var combo=new CallbackCombo{Height=40};Name(combo,"回调后的选择","tx-combo");
        var repeat=new ComboBox{Height=40};Name(repeat,"相同对象的不同行","tx-repeat-combo");
        var editor=new TextBox{Text="保留中文🙂 Miii",FontSize=20,Height=48};Name(editor,"编辑与选区","tx-editor");
        var status=new TextBlock{Text="检查准备中",TextWrapping=TextWrapping.Wrap,FontSize=15};
        var body=new StackPanel{Margin=new Thickness(16),Spacing=10};body.Children.Add(new TextBlock{Text="重复项目与选择回调",FontSize=22});body.Children.Add(list);body.Children.Add(combo);body.Children.Add(repeat);body.Children.Add(editor);body.Children.Add(status);
        var actions=new WrapPanel{ItemWidth=150,ItemHeight=42};
        Button Action(string text,string id){var b=new Button{Content=text,Width=144,Height=38};Name(b,text,id);actions.Children.Add(b);return b;}
        var add=Action("添加第二行","tx-add");var insert=Action("插入第二行","tx-insert");var reentry=Action("键盘与回调","tx-reentry");var duplicate=Action("重复项高亮","tx-duplicate");
        var getter=Action("值读取回调","tx-getter");var throwing=Action("异常后继续","tx-throw");var capture=Action("记录画面","tx-capture");var next=Action("下一组项目","tx-next");body.Children.Add(actions);
        var subject=new Border{Width=660,Height=620,Child=body,CornerRadius=new CornerRadius(8),BorderThickness=new Thickness(1)};subject.SetResourceReference(Border.BackgroundProperty,"LayerFillColorDefaultBrush");subject.SetResourceReference(Border.BorderBrushProperty,"ControlStrokeColorDefaultBrush");
        var anchor=new Button{Content="展开检查区域",Width=160,Height=40};var area=new Canvas{Height=640};area.Children.Add(kind==0?subject:anchor);Canvas.SetLeft(subject,5);Canvas.SetTop(subject,5);Canvas.SetLeft(anchor,5);Canvas.SetTop(anchor,5);
        var panel=new StackPanel{Margin=new Thickness(20),Spacing=12};panel.Children.Add(new TextBlock{Text="窗口选择与回调",FontSize=24});panel.Children.Add(area);
        var owner=new Window{Title="Jalium Selection Transactions "+index,Left=40,Top=40,Width=740,Height=780,MinWidth=720,MinHeight=760,TitleBarStyle=custom?WindowTitleBarStyle.Custom:WindowTitleBarStyle.Native,Content=panel};owner.SetResourceReference(Window.BackgroundProperty,"SolidBackgroundFillColorBaseBrush");Application.Current!.MainWindow=owner;
        Popup? popup=kind==0?null:new Popup{Child=subject,PlacementTarget=anchor,Placement=PlacementMode.Bottom,PreferExternalWindow=kind==2,ShouldConstrainToRootBounds=kind==1,StaysOpen=true,PopupAnimation=PopupAnimation.None};
        var getSurface=typeof(Window).GetMethod("GetMacOSPhysicalWindow",BindingFlags.Static|BindingFlags.NonPublic)!;
        var checks=new List<string>();var failures=new List<string>();var scenarios=new List<object>();ObservableCollection<object> source=new();object? alias=null;object[] comboRows=[];
        int dataKind=-1,pendingKind=0,pendingAction=0,phase=0,captures=0,addResult=-1,coherentEvents=0,keyPackets=0;bool running=false,closed=false,validateEvents=false;Window? surface=null;NSView? view=null;var watch=Stopwatch.StartNew();
        void Trace(string text)=>File.AppendAllText(Path.Combine(output,"progress.log"),watch.Elapsed.TotalSeconds.ToString("F3")+" "+text+Environment.NewLine);
        void Require(bool condition,string text){if(!condition)throw new InvalidOperationException(text);checks.Add(text);}
        void Attempt(string name,Action action){int start=checks.Count;Exception? error=null;try{action();}catch(Exception e){error=e;failures.Add("kind="+dataKind+" "+name+": "+e);}scenarios.Add(new{dataKind,name,passed=error is null,checks=checks.Skip(start).ToArray(),error=error?.ToString()});}
        ListBoxAutomationPeer Peer()=>(ListBoxAutomationPeer)list.GetAutomationPeer()!;
        ListBoxItemAutomationPeer[] Rows()=>Peer().GetChildren().Cast<ListBoxItemAutomationPeer>().ToArray();
        int[] Selected(){var rows=Rows();var selected=Peer().GetSelection();return rows.Select((row,i)=>(row,i)).Where(x=>selected.Any(p=>ReferenceEquals(p,x.row))).Select(x=>x.i).ToArray();}
        void ReadSurface(){surface=(Window?)getSurface.Invoke(null,[editor]);view=surface is {Handle:var h}&&h!=0?Runtime.GetNSObject<NSView>(h):null;}
        void Layout(){owner.UpdateLayout();ReadSurface();if(surface!=null&&!ReferenceEquals(surface,owner))surface.UpdateLayout();}
        void PrepareRows(){if(closed)return;for(int i=0;i<source.Count;i++)if(list.ItemContainerGenerator.ContainerFromIndex(i)is FrameworkElement row)Name(row,i<2?"重复条目🙂":"普通项目","tx-row-"+i);}
        void Record()
        {
            ReadSurface();PrepareRows();var state=new{index,custom,software,kind,dataKind,pendingKind,pendingAction,running,closed,phase,captures,addResult,coherentEvents,keyPackets,processId=Environment.ProcessId,appRunning=app.Running,selectedRows=Selected(),selectedCount=list.SelectedItems.Count,
                realizedSelected=Enumerable.Range(0,source.Count).Select(i=>list.ItemContainerGenerator.ContainerFromIndex(i)is ListBoxItem row&&row.IsSelected).ToArray(),comboIndex=combo.SelectedIndex,comboValue=combo.SelectedValue,comboText=combo.SelectionBoxItem?.ToString(),text=editor.Text,selectionStart=editor.SelectionStart,selectionLength=editor.SelectionLength,
                focused=editor.IsKeyboardFocused,ownerNativeKey=closed||owner.Handle==0?null:Runtime.GetNSObject<NSView>(owner.Handle)?.Window?.IsKeyWindow,surfaceNativeKey=view?.Window?.IsKeyWindow,appActive=app.Active,frontmostPID=NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                ownerBackend=owner.RenderTarget?.Backend.ToString(),surfaceBackend=surface?.RenderTarget?.Backend.ToString(),checks,failures,scenarios,ownNSViewSDK=true,physicalInput=false,desktopScreenshot=false};File.WriteAllText(Path.Combine(output,"state.tmp"),JsonSerializer.Serialize(state));File.Move(Path.Combine(output,"state.tmp"),Path.Combine(output,"state.json"),true);
        }
        static int Key(object? row)=>row is Row normal?normal.Key:row is GetterRow getter?getter.Key:-1;
        void CheckCombo(int index){Require(combo.SelectedIndex==index&&ReferenceEquals(combo.SelectedItem,comboRows[index])&&Equals(combo.SelectedValue,Key(comboRows[index])),"index, item and value agree with the newest request");Require(ReferenceEquals(combo.SelectionBoxItem,comboRows[index]),"displayed combo value follows the newest row");}
        void ResetCombo(object[] rows){combo.Hook=null;validateEvents=false;comboRows=rows;combo.ItemsSource=new ObservableCollection<object>(rows);combo.SelectedValuePath=nameof(Row.Key);combo.SelectedIndex=0;Layout();}
        combo.SelectionChanged+=(_,_)=>{if(validateEvents){Require(combo.SelectedIndex>=0&&ReferenceEquals(combo.SelectedItem,comboRows[combo.SelectedIndex])&&Equals(combo.SelectedValue,Key(comboRows[combo.SelectedIndex])),"SelectionChanged observes coherent current properties");coherentEvents++;}};
        void Keyboard()
        {
            Require(combo.Focus(),"combo receives own NSView keyboard focus");ReadSurface();Require(view?.Window!=null,"SDK keyboard target has a current native window");
            using(var key=NSEvent.KeyEvent(NSEventType.KeyDown,CGPoint.Empty,0,watch.Elapsed.TotalSeconds+ ++keyPackets,view!.Window!.WindowNumber,null,"\uf701","\uf701",false,125)!)view.KeyDown(key);
            using(var key=NSEvent.KeyEvent(NSEventType.KeyUp,CGPoint.Empty,0,watch.Elapsed.TotalSeconds+ ++keyPackets,view!.Window!.WindowNumber,null,"\uf701","\uf701",false,125)!)view.KeyUp(key);
        }
        void SwitchData(int shape)
        {
            dataKind=shape;var shared=new Row(7);alias=shape==2?new Row(7):shared;source=shape==1?new ObservableCollection<object>{new ListBoxItem{Content=shared},new ListBoxItem{Content=shared},new ListBoxItem{Content=new Row(9)}}:new ObservableCollection<object>{shared,shape==2?new Row(7):shared,new Row(9)};
            list.ItemsSource=source;list.SelectionMode=SelectionMode.Extended;ResetCombo([new Row(1),new Row(2),new Row(3)]);repeat.ItemsSource=new ObservableCollection<object>{shared,shared,new Row(9)};repeat.SelectedIndex=1;Layout();
            Attempt("actual-backend",()=>Require(owner.RenderTarget?.Backend==(software?RenderBackend.Software:RenderBackend.Metal)&&surface?.RenderTarget?.Backend==(software?RenderBackend.Software:RenderBackend.Metal)&&(surface!=owner)==(kind==2),"owner and surface use the requested backend and hosting mode"));
            Attempt("first-public-add-positive",()=>{list.SelectedItems.Add(alias);Require(Selected().SequenceEqual(new[]{0})&&list.SelectedItems.Count==1,"first public add selects exactly one occurrence");});
            editor.Select(1,2);editor.Focus();next.IsEnabled=shape<2;status.Text="准备验证第 "+(shape+1)+" 组";phase++;
        }
        void RunAction(int action)
        {
            if(action==1)Attempt("second-public-add",()=>{addResult=list.SelectedItems.Add(alias);Layout();Require(addResult==1&&list.SelectedItems.Count==2&&Selected().SequenceEqual(new[]{0,1}),"second public add selects the next occurrence and returns its collection index");});
            else if(action==2)Attempt("public-insert",()=>{list.UnselectAll();list.SelectedItems.Add(alias);list.SelectedItems.Insert(0,alias);Layout();Require(list.SelectedItems.Count==2&&Selected().SequenceEqual(new[]{0,1}),"public insert adds an independent occurrence");});
            else if(action==3)
            {
                for(int requested=0;requested<3;requested++){int choice=requested;Attempt("sdk-property-reentry-"+choice,()=>{ResetCombo([new Row(1),new Row(2),new Row(3)]);bool invoked=false;combo.Hook=property=>{if(invoked||property!=Selector.SelectedItemProperty||combo.SelectedIndex!=1)return;invoked=true;if(choice==0)combo.SelectedIndex=2;else if(choice==1)combo.SelectedItem=comboRows[2];else combo.SelectedValue=3;};validateEvents=true;try{Keyboard();}finally{validateEvents=false;combo.Hook=null;}Require(invoked,"actual native arrow reaches the scalar callback");CheckCombo(2);});}
            }
            else if(action==4)Attempt("duplicate-combo-positive",()=>{repeat.IsDropDownOpen=true;try{Layout();repeat.SelectedIndex=1;var first=repeat.ItemContainerGenerator.ContainerFromIndex(0)as ComboBoxItem;var second=repeat.ItemContainerGenerator.ContainerFromIndex(1)as ComboBoxItem;Require(first!=null&&second!=null&&!ReferenceEquals(first,second)&&!first.IsSelected&&second.IsSelected,"actual dropdown highlights only the selected repeated row; first="+first?.IsSelected+" second="+second?.IsSelected+" selectedIndex="+repeat.SelectedIndex+" firstIndex="+(first==null?-1:repeat.ItemContainerGenerator.IndexFromContainer(first))+" secondIndex="+(second==null?-1:repeat.ItemContainerGenerator.IndexFromContainer(second)));}finally{repeat.IsDropDownOpen=false;}});
            else if(action==5)Attempt("value-getter-reentry",()=>{var rows=new[]{new GetterRow(1),new GetterRow(2),new GetterRow(3)};ResetCombo(rows);bool invoked=false;rows[1].Read=()=>{if(!invoked){invoked=true;combo.SelectedIndex=2;}};combo.SelectedIndex=1;Require(invoked,"selected value getter issues a new request");CheckCombo(2);});
            else if(action==6)Attempt("throwing-property-reentry",()=>{ResetCombo([new Row(1),new Row(2),new Row(3)]);bool invoked=false;var expected=new MarkerException();combo.Hook=property=>{if(!invoked&&property==Selector.SelectedItemProperty){invoked=true;combo.SelectedValue=3;throw expected;}};Exception? caught=null;try{combo.SelectedIndex=1;}catch(Exception e){caught=e;}finally{combo.Hook=null;}Require(invoked&&ReferenceEquals(caught,expected),"first callback exception reaches the caller");CheckCombo(2);combo.SelectedIndex=0;CheckCombo(0);combo.SelectedIndex=2;});
            else if(action==7)
            {
                void Save(Window window,string name){var bitmap=MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth*window.DpiScale),(int)Math.Ceiling(window.ActualHeight*window.DpiScale),window.DpiScale,drawing=>{drawing.DrawRectangle(window.Background,null,new Rect(0,0,window.ActualWidth,window.ActualHeight));window.Render(drawing);},()=>window.Handle!=0)??throw new InvalidOperationException("backend readback unavailable");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);}
                Layout();Save(owner,"kind-"+dataKind+"-owner");if(surface!=null&&!ReferenceEquals(surface,owner))Save(surface,"kind-"+dataKind+"-surface");captures++;
            }
            editor.Select(1,2);editor.Focus();status.Text="当前选择 "+list.SelectedItems.Count+" 行，回调目标 "+combo.SelectedIndex;phase++;
        }
        add.Click+=(_,_)=>pendingAction=1;insert.Click+=(_,_)=>pendingAction=2;reentry.Click+=(_,_)=>pendingAction=3;duplicate.Click+=(_,_)=>pendingAction=4;getter.Click+=(_,_)=>pendingAction=5;throwing.Click+=(_,_)=>pendingAction=6;capture.Click+=(_,_)=>pendingAction=7;next.Click+=(_,_)=>{if(dataKind<2)pendingKind=dataKind+1;};
        owner.Show();owner.UpdateLayout();if(popup!=null)popup.IsOpen=true;Layout();Trace("owner ready");
        using var pulse=NSTimer.CreateRepeatingScheduledTimer(.03,_=>
        {
            if(running||closed)return;
            try
            {
                if(File.Exists(Path.Combine(output,"finish.signal"))||watch.Elapsed.TotalSeconds>120){if(watch.Elapsed.TotalSeconds>120)failures.Add("host watchdog expired");closed=true;try{if(popup!=null)popup.IsOpen=false;owner.Close();Record();}finally{app.Stop(app);using var wake=NSEvent.OtherEvent(NSEventType.ApplicationDefined,default,0,0,0,null,0,0,0);app.PostEvent(wake,false);}return;}
                ReadSurface();if(owner.RenderTarget==null||surface?.RenderTarget==null||view?.Window==null)return;
                if(pendingKind>=0){running=true;int nextKind=pendingKind;pendingKind=-1;SwitchData(nextKind);running=false;}
                else if(pendingAction>0){running=true;int action=pendingAction;pendingAction=0;RunAction(action);running=false;}
                Record();
            }
            catch(Exception e){failures.Add(e.ToString());running=false;Trace(e.ToString());Record();}
        });
        try{app.Run();return failures.Count==0?0:1;}finally{pulse.Invalidate();if(popup!=null)popup.IsOpen=false;owner.Close();using var note=NSNotification.FromName(NSApplication.WillTerminateNotification,app);applicationDelegate.WillTerminate(note);app.Delegate=null!;}
    }
    private static void Name(DependencyObject value,string name,string id){AutomationProperties.SetName(value,name);AutomationProperties.SetAutomationId(value,id);}
    private sealed record Row(int Key){public override string ToString()=>Key==7?"重复条目🙂 Miii":"项目 "+Key+" · 中文🙂";}
    private sealed class GetterRow(int key){internal Action? Read;public int Key{get{Read?.Invoke();return key;}}public override string ToString()=>"值读取项目 "+key;}
    private sealed class MarkerException:Exception;
    private sealed class CallbackCombo:ComboBox
    {
        internal Action<DependencyProperty?>? Hook;
        internal CallbackCombo(){SetValue(DefaultStyleKeyProperty,typeof(ComboBox));}
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);Hook?.Invoke(e.Property);}
    }
    private sealed class Delegate:JaliumMacApplicationDelegate
    {private bool started;public override void DidFinishLaunching(NSNotification note){if(started)return;started=true;base.DidFinishLaunching(note);}protected override JaliumApp CreateHostedApp()=>AppBuilder.CreateBuilder(new AppBuilderSettings{DisableDefaults=true}).Build().UseApplication(new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown});}
}
