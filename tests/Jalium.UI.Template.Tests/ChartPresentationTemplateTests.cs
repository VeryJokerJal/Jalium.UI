using System.Collections.ObjectModel;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class ChartPresentationTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>{Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;});
    [Fact]
    public void LegendItemsUpdateTextBrushVisibilityAndCollectionWithoutManualRendering()
    {
        var item=new ChartLegendItem{Label="收入",Brush=Brushes.Red}; var items=new ObservableCollection<ChartLegendItem>{item}; var legend=new ChartLegend{Items=items}; Layout(legend);
        var host=Assert.IsType<ItemsControl>(legend.FindName("PART_Items")); Assert.Single(host.Items);
        Assert.Contains(Descendants<TextBlock>(legend),text=>text.Text=="收入");
        item.Label="利润"; item.Brush=Brushes.Blue; Layout(legend); Assert.Contains(Descendants<TextBlock>(legend),text=>text.Text=="利润"); Assert.Contains(Descendants<Jalium.UI.Shapes.Rectangle>(legend),rectangle=>ReferenceEquals(rectangle.Fill,Brushes.Blue));
        item.IsVisible=false; Layout(legend); Assert.Contains(Descendants<Border>(legend),border=>border.Visibility==Visibility.Collapsed);
        items.Add(new ChartLegendItem{Label="成本"}); Layout(legend); Assert.Equal(2,host.Items.Count);
        legend.Orientation=Orientation.Vertical; Layout(legend); Assert.Equal(Orientation.Vertical,Assert.Single(Descendants<WrapPanel>(legend)).Orientation);
    }
    [Fact]
    public void LegendCanReplaceItemAndControlTemplatesAndReleaseTheRetiredItemsSource()
    {
        var items=new[]{new ChartLegendItem{Label="series"}}; var legend=new ChartLegend{Items=items}; Layout(legend); var retired=Assert.IsType<ItemsControl>(legend.FindName("PART_Items"));
        var itemTemplate=new DataTemplate(); itemTemplate.SetVisualTree(()=>new TextBlock{Text="custom legend"}); legend.ItemTemplate=itemTemplate; Layout(legend); Assert.Contains(Descendants<TextBlock>(legend),text=>text.Text=="custom legend");
        var blank=new ControlTemplate(typeof(ChartLegend)); blank.SetVisualTree(()=>new Border()); legend.Template=blank; Layout(legend); Assert.Null(retired.ItemsSource);
        legend.ClearValue(Control.TemplateProperty); Layout(legend); Assert.Same(items,Assert.IsType<ItemsControl>(legend.FindName("PART_Items")).ItemsSource); Assert.Contains(Descendants<TextBlock>(legend),text=>text.Text=="custom legend");
    }
    [Fact]
    public void TooltipOwnsFormattedValuesAndKeepsConsumerContentAcrossTemplates()
    {
        var content=new TextBlock{Text="附加信息"}; var tooltip=new ChartTooltip{SeriesTitle="收入",XValue="周一",YValue="120",SeriesBrush=Brushes.Red,Content=content}; Layout(tooltip);
        Assert.Equal("X: 周一",Assert.IsType<TextBlock>(tooltip.FindName("PART_XValue")).Text); Assert.Equal("Y: 120",Assert.IsType<TextBlock>(tooltip.FindName("PART_YValue")).Text);
        Assert.Equal(AutomationControlType.ToolTip,tooltip.GetAutomationPeer()!.GetAutomationControlType()); tooltip.XValue=null; tooltip.SeriesBrush=null; Assert.Equal(Visibility.Collapsed,Assert.IsType<TextBlock>(tooltip.FindName("PART_XValue")).Visibility);
        Assert.Equal(Visibility.Collapsed,Assert.IsType<Jalium.UI.Shapes.Rectangle>(tooltip.FindName("PART_Marker")).Visibility);
        var blank=new ControlTemplate(typeof(ChartTooltip)); blank.SetVisualTree(()=>new Border()); tooltip.Template=blank; Layout(tooltip); tooltip.ClearValue(Control.TemplateProperty); Layout(tooltip); Assert.Same(content,Assert.IsType<ContentPresenter>(tooltip.FindName("PART_Content")).Content);
    }
    private static IEnumerable<T> Descendants<T>(Visual root) where T:Visual{for(var i=0;i<root.VisualChildrenCount;i++){var child=root.GetVisualChild(i);if(child is T match)yield return match;if(child is not null)foreach(var next in Descendants<T>(child))yield return next;}}
    private static void Layout(Control control){if(!control.Resources.MergedDictionaries.Contains(Defaults.Value))control.Resources.MergedDictionaries.Add(Defaults.Value);for(var pass=0;pass<3;pass++){control.InvalidateMeasure();control.Measure(new Size(360,240));control.Arrange(new Rect(0,0,360,240));control.UpdateLayout();}}
}
