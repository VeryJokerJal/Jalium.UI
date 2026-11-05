using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SwipeControlTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void DeclaredActionsInheritAndRefreshTheOwningControlsDataContext()
    {
        var control=Assert.IsType<SwipeControl>(Jalium.UI.Markup.XamlReader.Parse("<SwipeControl xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><SwipeControl.LeftItems><SwipeItems><SwipeItem Text='Archive' Command='{Binding Action}'/></SwipeItems></SwipeControl.LeftItems></SwipeControl>"));
        control.Resources.MergedDictionaries.Add(Defaults.Value); var first=new CommandModel(); var second=new CommandModel(); control.DataContext=first; Layout(control);
        var item=Assert.Single(control.LeftItems!); Assert.Same(first.Action,item.Command);
        control.DataContext=second; Assert.Same(second.Action,item.Command); control.InvokeItem(item); Assert.Equal(0,first.Calls); Assert.Equal(1,second.Calls);
        control.LeftItems=new SwipeItems(); Assert.Null(item.Command);
    }
    private sealed class CommandModel
    {
        public int Calls; public System.Windows.Input.ICommand Action {get;}
        public CommandModel()=>Action=new RelayCommand(()=>Calls++);
    }
    [Fact]
    public void RuntimeAndCompiledSwipeCollectionsKeepAllDeclaredActions()
    {
        var parsed=Assert.IsType<SwipeItems>(Jalium.UI.Markup.XamlReader.Parse("<SwipeItems xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><SwipeItem Text='Archive'/><SwipeItem Text='Delete'/></SwipeItems>"));
        var generated=Assert.IsType<SwipeItems>(new GeneratedTransformGroupTheme()["SwipeActions"]);
        Assert.Equal(new[]{"Archive","Delete"},parsed.Select(item=>item.Text));
        Assert.Equal(new[]{"Archive","Delete"},generated.Select(item=>item.Text));
    }
    [Fact]
    public void AllFourEdgesTranslateTheTemplateContentAndExposeRealButtons()
    {
        var control=Create(); control.LeftItems=[new SwipeItem {Text="左"}]; control.RightItems=[new SwipeItem {Text="右"}]; control.TopItems=[new SwipeItem {Text="上"}]; control.BottomItems=[new SwipeItem {Text="下"}]; Layout(control);
        foreach(var direction in new[]{SwipeDirection.Left,SwipeDirection.Right,SwipeDirection.Top,SwipeDirection.Bottom})
        {
            Assert.True(control.Open(direction)); Layout(control); Assert.True(control.IsOpen); Assert.Equal(direction,control.RevealedDirection);
            var button=Assert.Single(Descendants<SwipeActionButton>(control)); Assert.Same(control,button.Owner); Assert.Same(control.RevealedItems[0],button.Item); Assert.True(button.IsEnabled);
            Assert.Same(control.ContentTranslation,Assert.IsType<Border>(Assert.IsType<ContentPresenter>(control.FindName("PART_Content")).VisualParent).RenderTransform);
            Assert.Equal(direction.ToString(),VisualStateManager.GetCurrentStateName(control,"DirectionStates"));
        }
        control.Close(); Assert.False(control.IsOpen); Assert.Equal(0,control.HorizontalOffset); Assert.Equal(0,control.VerticalOffset);
    }
    [Fact]
    public void ActionsInvokeExactlyOnceAndHonorAvailabilityAndRemainOpen()
    {
        var invoked=0; var executed=0; var item=new SwipeItem {Text="保留",BehaviorOnInvoked=BehaviorOnInvoked.RemainOpen,Command=new RelayCommand(()=>executed++)}; item.Invoked+=(_,_)=>invoked++;
        var control=Create(); control.LeftItems=[item]; Layout(control); control.Open(SwipeDirection.Left); Layout(control);
        Assert.Single(Descendants<SwipeActionButton>(control)).PerformClick(); Assert.Equal(1,invoked); Assert.Equal(1,executed); Assert.True(control.IsOpen);
        item.BehaviorOnInvoked=BehaviorOnInvoked.Close; Assert.True(control.InvokeItem(item)); Assert.Equal(2,invoked); Assert.Equal(2,executed); Assert.False(control.IsOpen);
        item.Command=new RelayCommand(()=>executed++,()=>false); control.Open(SwipeDirection.Left); Layout(control);
        Assert.False(control.InvokeItem(item)); Assert.False(Assert.Single(Descendants<SwipeActionButton>(control)).IsEnabled); Assert.Equal(2,executed);
    }
    [Fact]
    public void ReplacingTemplatesKeepsContentButDisconnectsRetiredActionButtons()
    {
        var content=new TextBlock {Text="内容"}; var calls=0; var item=new SwipeItem {Text="操作",Command=new RelayCommand(()=>calls++)}; var control=Create(); control.Content=content; control.RightItems=[item]; Layout(control); control.Open(SwipeDirection.Right); Layout(control);
        var retired=Assert.Single(Descendants<SwipeActionButton>(control)); var blank=new ControlTemplate(typeof(SwipeControl)); blank.SetVisualTree(()=>new Border()); control.Template=blank; Layout(control);
        retired.PerformClick(); Assert.Equal(0,calls); Assert.True(control.IsOpen);
        control.ClearValue(Control.TemplateProperty); Layout(control); Assert.Same(content,Assert.IsType<ContentPresenter>(control.FindName("PART_Content")).Content); Assert.Single(Descendants<SwipeActionButton>(control)).PerformClick(); Assert.Equal(1,calls); Assert.False(control.IsOpen);
        control.RightItems.Add(new SwipeItem {Text="新增"}); control.Open(SwipeDirection.Right); control.RefreshItems(); Layout(control); Assert.Equal(2,Descendants<SwipeActionButton>(control).Count());
    }
    [Fact]
    public void PointerThresholdRevealsOrExecutesAndDisabledStateClosesTheActions()
    {
        var calls=0; var control=Create(); control.LeftItems=[new SwipeItem {Text="执行",Command=new RelayCommand(()=>calls++)}]; Layout(control);
        Drag(control,new Point(10,20),new Point(110,20)); Assert.True(control.IsOpen); Assert.Equal(0,calls);
        control.Close(); control.LeftItems.Mode=SwipeMode.Execute; Drag(control,new Point(10,20),new Point(110,20)); Assert.Equal(1,calls); Assert.False(control.IsOpen);
        control.LeftItems.Mode=SwipeMode.Reveal; control.Open(SwipeDirection.Left); control.IsEnabled=false; Assert.False(control.IsOpen); Assert.False(control.Open(SwipeDirection.Left));
    }
    private static void Drag(SwipeControl control,Point start,Point end)
    {
        MouseButtonEventArgs Button(RoutedEvent routed,Point point,MouseButtonState state)=>new(routed,point,MouseButton.Left,state,1,state,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,ModifierKeys.None,Environment.TickCount);
        control.RaiseEvent(Button(UIElement.MouseDownEvent,start,MouseButtonState.Pressed));
        control.RaiseEvent(new MouseEventArgs(UIElement.MouseMoveEvent,end,MouseButtonState.Pressed,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,MouseButtonState.Released,ModifierKeys.None,Environment.TickCount));
        control.RaiseEvent(Button(UIElement.MouseUpEvent,end,MouseButtonState.Released));
    }
    private static SwipeControl Create() { var control=new SwipeControl {Content=new TextBlock {Text="内容"}}; control.Resources.MergedDictionaries.Add(Defaults.Value); return control; }
    private static IEnumerable<T> Descendants<T>(Visual root) where T:Visual
    {
        for(var i=0;i<root.VisualChildrenCount;i++){var child=root.GetVisualChild(i);if(child is T match)yield return match;if(child is not null)foreach(var item in Descendants<T>(child))yield return item;}
    }
    private static void Layout(FrameworkElement element) { for(var pass=0;pass<3;pass++){element.InvalidateMeasure();element.Measure(new Size(360,160));element.Arrange(new Rect(0,0,360,160));element.UpdateLayout();} }
}
