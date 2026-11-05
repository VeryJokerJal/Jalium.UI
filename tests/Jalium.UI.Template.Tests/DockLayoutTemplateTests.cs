using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DockLayoutTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>{Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;});
    [Fact]
    public void ConsumerContentRetainsItsIdentityAcrossRootTemplateReplacement()
    {
        var content=new DockTabPanel();content.Items.Add(new DockItem{Header="文档",Content=new TextBlock{Text="内容"}});var layout=new DockLayout{Content=content};Layout(layout);
        Assert.Same(content,Assert.IsType<ContentPresenter>(layout.FindName("PART_Content")).Content);
        var blank=new ControlTemplate(typeof(DockLayout));blank.SetVisualTree(()=>new Border());layout.Template=blank;Layout(layout);
        layout.ClearValue(Control.TemplateProperty);Layout(layout);Assert.Same(content,Assert.IsType<ContentPresenter>(layout.FindName("PART_Content")).Content);Assert.Single(content.Items);
    }
    [Fact]
    public void DefaultTemplateOwnsTheTargetHighlightAndHonorsConsumerBrushes()
    {
        var brush=Brushes.Red;var layout=new DockLayout{HighlightBrush=brush};Layout(layout);var highlight=Assert.IsType<Border>(layout.FindName("PART_Highlight"));Assert.Same(brush,highlight.BorderBrush);Assert.Equal(Visibility.Collapsed,highlight.Visibility);
        Assert.True(VisualStateManager.GoToState(layout,"Highlighted",false));Assert.Equal(Visibility.Visible,highlight.Visibility);
        layout.IsEnabled=false;Assert.Equal("Disabled",VisualStateManager.GetCurrentStateName(layout,"CommonStates"));
    }
    [Fact]
    public void FloatingRestoreFindsTheContentOwnerThroughItsTemplate()
    {
        var item = new DockItem { Header = "文档", CanClose = false };
        var panel = new DockTabPanel();
        panel.Items.Add(item);
        var layout = new DockLayout { Content = panel };
        Layout(layout);
        Assert.NotSame(layout, panel.VisualParent);

        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var capture = typeof(DockItem).GetMethod("CaptureFloatingRestoreContext", flags)!;
        var restore = typeof(DockItem).GetMethod("EnsureSourcePanelAttached", flags)!;
        var context = capture.Invoke(null, new object[] { panel, item })!;
        Assert.Same(layout, context.GetType().GetProperty("SourceContentHost")!.GetValue(context));

        layout.Content = null;
        Layout(layout);
        Assert.Null(panel.VisualParent);
        Assert.True((bool)restore.Invoke(null, new[] { context })!);
        Layout(layout);
        Assert.Same(panel, layout.Content);
        Assert.NotNull(panel.VisualParent);
        Assert.Single(panel.Items);
    }
    private static void Layout(Control control){if(!control.Resources.MergedDictionaries.Contains(Defaults.Value))control.Resources.MergedDictionaries.Add(Defaults.Value);for(var pass=0;pass<3;pass++){control.InvalidateMeasure();control.Measure(new Size(640,480));control.Arrange(new Rect(0,0,640,480));control.UpdateLayout();}}
}
