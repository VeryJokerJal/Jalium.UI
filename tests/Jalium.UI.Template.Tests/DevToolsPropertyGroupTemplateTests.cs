using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsPropertyGroupTemplateTests
{
    public DevToolsPropertyGroupTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    [Fact]
    public void CategoryRowsCountAlternationAndPaletteAreTemplateOwned()
    {
        using var source = new Models(); var group = new DevToolsPropertyGroup { Category = DevToolsPropertyCategory.Layout, ItemsSource = source.Items };
        Layout(group); var rows = Rows(group); Assert.Equal(3, rows.Length);
        Assert.Equal(HorizontalAlignment.Stretch, group.HorizontalContentAlignment);
        Assert.Equal(source.Items, rows.Select(row => row.Model)); Assert.Equal(new[] { false, true, false }, rows.Select(row => row.IsAlternate));
        Assert.Equal(group.Heading, Header(group).Label); Assert.Same(group.TryFindResource("DevTools.Success"), group.Foreground);
        Assert.All(rows, row => { Assert.Equal(group.NameColumnWidth, row.NameColumnWidth); Assert.Same(group.Foreground, row.NameForeground); });
        group.Category = DevToolsPropertyCategory.Typography; Assert.Same(group.TryFindResource("DevTools.TokenKeyword"), group.Foreground);
        var custom = new SolidColorBrush(Color.White); group.Foreground = custom; Assert.All(rows, row => Assert.Same(custom, row.NameForeground));
        source.Items.RemoveAt(0); Layout(group); Assert.Equal(2, Rows(group).Length); Assert.Contains("2", group.Heading);
    }
    [Fact]
    public void CollapseAndAutomationKeepLiveValuesAndRestoreTheirEditors()
    {
        using var source = new Models(); var group = new DevToolsPropertyGroup { ItemsSource = source.Items }; Layout(group);
        var peer = group.GetAutomationPeer()!; Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        expand.Collapse(); Layout(group); Assert.False(group.IsExpanded); Assert.False(Header(group).IsActive); Assert.Equal(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        Assert.Equal(Visibility.Collapsed, Assert.IsType<ItemsPresenter>(group.FindName("PART_ItemsPresenter")).Visibility);
        source.Target.Opacity = .75; expand.Expand(); Layout(group);
        Assert.True(group.IsExpanded); Assert.True(Header(group).IsActive); Assert.Equal(Convert.ToString(.75), Editor(Rows(group)[0]).ValueText);
        ((IToggleProvider)Header(group).GetAutomationPeer()!).Toggle(); Assert.False(group.IsExpanded);
        group.IsEnabled = false; Assert.Throws<InvalidOperationException>(expand.Expand);
    }
    [Fact]
    public void ReplacedTemplatesRetireOldHeadersWithoutDisposingExternalModels()
    {
        using var source = new Models(); var group = new DevToolsPropertyGroup { ItemsSource = source.Items }; Layout(group); var original = group.Template; var retired = Header(group);
        var blank = new ControlTemplate(typeof(DevToolsPropertyGroup)); blank.SetVisualTree(() => new Border()); group.Template = blank; group.ApplyTemplate();
        retired.PerformClick(); Assert.True(group.IsExpanded); source.Target.Opacity = .6;
        group.Template = original; Layout(group); retired.PerformClick(); Assert.True(group.IsExpanded);
        Assert.Equal(Convert.ToString(.6), Editor(Rows(group)[0]).ValueText);
        group.ItemsSource = null; Layout(group); Assert.Empty(Rows(group)); Assert.False(Header(group).IsEnabled);
        Assert.Null(group.GetAutomationPeer()!.GetPattern(PatternInterface.ExpandCollapse)); Assert.Equal(.6, source.Items[0].EditorProperty.Value);
    }
    [Theory]
    [InlineData(200)] [InlineData(480)]
    public void ResponsiveRowsFitAndConsumersCanReplaceTheirItemTemplate(double width)
    {
        using var source = new Models(); var group = new DevToolsPropertyGroup { ItemsSource = source.Items }; Layout(group, width);
        Assert.InRange(group.DesiredSize.Width, 0, width + .5);
        foreach (var row in Rows(group)) Assert.True(row.TranslatePoint(new Point(row.ActualWidth, 0), group).X <= width + .5);
        group.ItemTemplate = new DataTemplate(); group.ItemTemplate.SetVisualTree(() => new TextBlock { Text = "custom" }); Layout(group, width); Assert.Empty(Rows(group));
    }
    private sealed class Models : IDisposable
    {
        public Border Target { get; } = new() { Opacity = .5, Padding = new Thickness(1.25) };
        public ObservableCollection<DevToolsDependencyPropertyModel> Items { get; }
        private readonly DevToolsDependencyPropertyModel[] _owned;
        public Models() { _owned = [new(Target, UIElement.OpacityProperty), new(Target, UIElement.IsEnabledProperty), new(Target, Border.PaddingProperty)]; Items = new(_owned); }
        public void Dispose() { foreach (var model in _owned) model.Dispose(); }
    }
    private static PropertyGridEditor Editor(DevToolsPropertyRow row) => Assert.IsType<PropertyGridEditor>(row.FindName("PART_Editor"));
    private static DevToolsActionButton Header(DevToolsPropertyGroup group) => Assert.IsType<DevToolsActionButton>(group.FindName("PART_Header"));
    private static DevToolsPropertyRow[] Rows(Visual root) => Walk(root).OfType<DevToolsPropertyRow>().ToArray();
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element, double width = 480) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 1800)); element.Arrange(new Rect(0,0,width,element.DesiredSize.Height)); element.UpdateLayout(); } }
}
