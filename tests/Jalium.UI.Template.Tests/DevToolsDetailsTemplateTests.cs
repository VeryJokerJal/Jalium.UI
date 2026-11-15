using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsDetailsTemplateTests
{
    public DevToolsDetailsTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void HeadingsExposePlainTextWithoutInteractiveOrListParts()
    {
        var heading = new DevToolsHeading { Text = "长标题 " + new string('名',35) }; Layout(heading,200);
        Assert.Equal(AutomationControlType.Text,heading.GetAutomationPeer()!.GetAutomationControlType()); Assert.Equal(heading.Text,heading.GetAutomationPeer()!.GetName());
        Assert.Empty(Walk(heading).OfType<DevToolsActionButton>()); Assert.Empty(Walk(heading).OfType<ItemsPresenter>());
        Assert.Equal(heading.DisplayText,Assert.Single(Walk(heading).OfType<TextBlock>()).Text);
    }

    [Theory]
    [InlineData(200)] [InlineData(480)]
    public void LongValuesDetailsAndSwatchesFitTheSection(double width)
    {
        var items = new ObservableCollection<DevToolsDetailItem>
        {
            DevToolsDetailItem.FromValue("非常长的属性名称" + new string('X',40), new string('名',50), "很长的补充信息" + new string('D',60)),
            DevToolsDetailItem.FromValue("Background", new SolidColorBrush(Color.FromArgb(128,10,20,30))),
            DevToolsDetailItem.FromValue("Enabled", true)
        };
        var section = new DevToolsDetailSection { Title = "Resources", ShowCount = true, ItemsSource = items }; Layout(section, width);
        var rows = Walk(section).OfType<DevToolsDetailRow>().ToArray(); Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { false,true,false }, rows.Select(row => row.IsAlternate)); Assert.Contains('3', section.Heading);
        foreach (var row in rows)
        {
            Assert.True(row.TranslatePoint(new Point(row.ActualWidth,0),section).X <= width + .5);
            Assert.InRange(row.DesiredSize.Width,0,width + .5);
        }
        Assert.Equal(Visibility.Visible, Assert.IsType<Border>(rows[1].FindName("PART_Swatch")).Visibility);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<Border>(rows[0].FindName("PART_Swatch")).Visibility);
        Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(rows[0].FindName("PART_Detail")).Visibility);
        Assert.Contains(items[0].ValueText, rows[0].GetAutomationPeer()!.GetName());
        items.RemoveAt(0); Layout(section,width); Assert.Contains('2',section.Heading);
    }

    [Fact]
    public void SectionCommandsExpansionAndRetiredTemplatesRespectOwnership()
    {
        int calls = 0; object token = new();
        var section = new DevToolsDetailSection { Title = "Style", ItemsSource = new[] { DevToolsDetailItem.FromValue("Opacity",.5) }, ActionLabel = "View XAML", ActionCommandParameter = token };
        section.ActionCommand = new RelayCommand(value => { Assert.Same(token,value); calls++; }); Layout(section);
        var action = Assert.IsType<DevToolsActionButton>(section.FindName("PART_Action")); var header = Assert.IsType<DevToolsActionButton>(section.FindName("PART_Header"));
        action.PerformClick(); Assert.Equal(1,calls);
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(section.GetAutomationPeer()!.GetPattern(PatternInterface.ExpandCollapse));
        header.PerformClick(); Assert.False(section.IsExpanded); Assert.Equal(ExpandCollapseState.Collapsed,expand.ExpandCollapseState); expand.Expand(); Assert.True(section.IsExpanded);
        var original = section.Template; var blank = new ControlTemplate(typeof(DevToolsDetailSection)); blank.SetVisualTree(() => new Border()); section.Template = blank; section.ApplyTemplate();
        action.PerformClick(); header.PerformClick(); Assert.Equal(1,calls); Assert.True(section.IsExpanded);
        section.Template = original; Layout(section); action.PerformClick(); Assert.Equal(1,calls);
        Assert.IsType<DevToolsActionButton>(section.FindName("PART_Action")).PerformClick(); Assert.Equal(2,calls);
        section.ActionCommand = new RelayCommand(() => calls++, () => false); Assert.IsType<DevToolsActionButton>(section.FindName("PART_Action")).PerformClick(); Assert.Equal(2,calls);
        section.ActionLabel = ""; Assert.Equal(Visibility.Collapsed,Assert.IsType<DevToolsActionButton>(section.FindName("PART_Action")).Visibility);
        section.IsEnabled = false; Assert.Throws<InvalidOperationException>(expand.Collapse);
    }

    [Fact]
    public void EmptySectionsAndCustomItemTemplatesKeepTheirContracts()
    {
        var section = new DevToolsDetailSection { Title = "No resources", ShowCount = true }; Layout(section);
        Assert.Contains('0',section.Heading); Assert.Null(section.GetAutomationPeer()!.GetPattern(PatternInterface.ExpandCollapse));
        Assert.False(section.HasDetails); Assert.Equal(Visibility.Collapsed,Assert.IsType<DevToolsActionButton>(section.FindName("PART_Header")).Visibility);
        Assert.Equal(Visibility.Visible,Assert.IsType<TextBlock>(section.FindName("PART_StaticHeading")).Visibility);
        section.Items.Add(DevToolsDetailItem.FromValue("Custom",42));
        var custom = new DataTemplate(); custom.SetVisualTree(() => new TextBlock { Text = "consumer template" }); section.ItemTemplate = custom; Layout(section);
        Assert.Empty(Walk(section).OfType<DevToolsDetailRow>()); Assert.Contains(Walk(section).OfType<TextBlock>(), text => text.Text == "consumer template");
        AutomationProperties.SetName(section,"Custom section"); Assert.Equal("Custom section",section.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void ActionOnlySectionsRetainAnAccessibleWayToExpand()
    {
        var section = new DevToolsDetailSection { Title = "Template",ActionLabel = "View XAML",ActionCommand = new RelayCommand(() => { }) }; Layout(section);
        Assert.True(section.HasDetails); var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(section.GetAutomationPeer()!.GetPattern(PatternInterface.ExpandCollapse));
        expand.Collapse(); Assert.IsType<DevToolsActionButton>(section.FindName("PART_Header")).PerformClick(); Assert.True(section.IsExpanded);
        section.ActionCommand = null; Assert.False(section.HasDetails); Assert.Null(section.GetAutomationPeer()!.GetPattern(PatternInterface.ExpandCollapse));
    }

    [Fact]
    public void ValueSnapshotsCopyBrushesAndHandleUnprintableObjects()
    {
        var brush = new SolidColorBrush(Color.FromArgb(80,10,20,30)); var item = DevToolsDetailItem.FromValue("Color",brush);
        Assert.NotSame(brush,item.SwatchBrush); Assert.Equal("#500A141E",item.ValueText);
        brush.Color = Color.White; Assert.Equal(Color.FromArgb(80,10,20,30),Assert.IsType<SolidColorBrush>(item.SwatchBrush).Color);
        Assert.Equal(DevToolsResourceValueKind.Null,DevToolsDetailItem.FromValue("Empty",null).ValueKind);
        Assert.Equal("<Unprintable>",DevToolsDetailItem.FromValue("Object",new Unprintable()).ValueText);
    }

    [Fact]
    public void XamlViewerKeepsSelectableReadonlyTextAcrossTemplateReplacement()
    {
        var view = new DevToolsXamlView { Text = "<Border Name=\"中文\">\n  <TextBlock Text=\"long value\" />\n</Border>" }; Layout(view,200);
        var editor = Assert.IsType<TextBox>(view.FindName("PART_Editor")); Assert.True(editor.IsReadOnly); Assert.True(editor.AcceptsReturn); Assert.Equal(view.Text,editor.Text);
        Assert.Equal(VerticalAlignment.Top,editor.VerticalContentAlignment); Assert.Equal(HorizontalAlignment.Left,editor.HorizontalContentAlignment);
        Assert.Equal("XAML",AutomationProperties.GetName(editor));
        var original = view.Template; var blank = new ControlTemplate(typeof(DevToolsXamlView)); blank.SetVisualTree(() => new Border()); view.Template = blank; view.ApplyTemplate();
        view.Text = "<Grid />"; Assert.NotEqual(view.Text,editor.Text); view.Template = original; Layout(view,200);
        Assert.Equal(view.Text,Assert.IsType<TextBox>(view.FindName("PART_Editor")).Text);
    }

    private sealed class Unprintable { public override string ToString() => throw new InvalidOperationException("Unavailable"); }
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element, double width = 480) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width,1800)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
