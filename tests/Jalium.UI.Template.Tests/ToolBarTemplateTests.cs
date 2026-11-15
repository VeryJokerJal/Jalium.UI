using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ToolBarTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void DefaultTemplateOwnsItemsOverflowAffordanceAndPopup()
    {
        var bar = Create();
        Layout(bar, 180);
        Assert.IsType<Border>(bar.GetVisualChild(0));
        Assert.IsType<ItemsPresenter>(bar.FindName("PART_ItemsPresenter"));
        var popup = Assert.IsType<Popup>(bar.FindName("PART_OverflowPopup"));
        Assert.IsType<Button>(bar.FindName("PART_OverflowButton"));
        var overflow = Assert.IsType<ToolBarOverflowPanel>(bar.FindName("PART_OverflowPanel"));
        Assert.True(bar.HasOverflowItems);
        Assert.NotEmpty(overflow.Children);
        Assert.Equal(Visibility.Visible, bar.OverflowButtonVisibility);
        Assert.False(popup.IsOpen);
        Layout(bar, 600);
        Assert.False(bar.HasOverflowItems);
        Assert.Empty(overflow.Children);
        Assert.Equal(Visibility.Collapsed, bar.OverflowButtonVisibility);
    }
    [Fact]
    public void RetemplatingRestoresContainersAndRetiresTheOldAffordance()
    {
        var bar = Create(); Layout(bar, 180);
        var oldOverflow = Assert.IsType<ToolBarOverflowPanel>(bar.FindName("PART_OverflowPanel"));
        var oldButton = Assert.IsType<Button>(bar.FindName("PART_OverflowButton"));
        var original = bar.Items.Cast<object>().ToArray();
        var empty = new ControlTemplate(typeof(ToolBar)); empty.SetVisualTree(() => new Border());
        bar.Template = empty; Layout(bar, 180);
        oldButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(bar.IsOverflowOpen);
        Assert.False(bar.HasOverflowItems);
        Assert.Empty(oldOverflow.Children);
        bar.ClearValue(Control.TemplateProperty); Layout(bar, 600);
        Assert.Equal(original, bar.Items.Cast<object>().ToArray());
        Assert.False(bar.HasOverflowItems);
        Assert.NotSame(oldOverflow, bar.FindName("PART_OverflowPanel"));
    }
    [Fact]
    public void OverflowStateUsesReadOnlyPresentationAndKeepsTheCallerBinding()
    {
        var bar = (ProbeToolBar)Create(new ProbeToolBar()); Layout(bar, 180);
        var model = new Model();
        BindingOperations.SetBinding(bar, ToolBar.IsOverflowOpenProperty, new Binding(nameof(Model.Open))
        { Source=model,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged });
        var expression=BindingOperations.GetBindingExpression(bar,ToolBar.IsOverflowOpenProperty);
        var button=Assert.IsType<Button>(bar.FindName("PART_OverflowButton"));
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(model.Open); Assert.True(bar.IsOverflowPresented); Assert.True(bar.ReadyWhenOpening);
        Assert.True(Assert.IsType<Popup>(bar.FindName("PART_OverflowPopup")).IsOpen);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.False(model.Open); Assert.False(bar.IsOverflowPresented);
        Assert.Same(expression,BindingOperations.GetBindingExpression(bar,ToolBar.IsOverflowOpenProperty));
    }
    private static ToolBar Create(ToolBar? bar=null)
    {
        bar??=new ToolBar();bar.Resources.MergedDictionaries.Add(Defaults.Value);
        for(int i=0;i<4;i++)bar.Items.Add(new Button {Content="Action "+i,Width=70,Height=24,MinWidth=0,MinHeight=0});
        return bar;
    }
    private static void Layout(UIElement element,double width)
    { element.InvalidateMeasure();element.Measure(new Size(width,100));element.Arrange(new Rect(0,0,width,Math.Max(40,element.DesiredSize.Height)));element.UpdateLayout(); }
    private sealed class ProbeToolBar:ToolBar
    {
        public bool ReadyWhenOpening {get;private set;}
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            if(e.Property==IsOverflowPresentedProperty && e.NewValue is true)
                ReadyWhenOpening=(FindName("PART_OverflowPanel") as ToolBarOverflowPanel)?.Visibility==Visibility.Visible;
            base.OnPropertyChanged(e);
        }
    }
    private sealed class Model:INotifyPropertyChanged
    {
        private bool _open;
        public bool Open {get=>_open;set{_open=value;PropertyChanged?.Invoke(this,new(nameof(Open)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
