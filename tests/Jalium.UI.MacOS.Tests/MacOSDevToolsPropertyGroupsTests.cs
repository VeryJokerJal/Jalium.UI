using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsPropertyGroupsTests
{
    [Fact]
    public void InspectorGroupsEveryDependencyPropertyOnceAndOwnsItsModelsAcrossRefresh()
    {
        Initialize(); var child = new Border { Name = "Selected", Padding = new Thickness(4), Opacity = .5 };
        var target = new DisplayedTestWindow { Content = child }; Layout(target); var tools = new DevToolsWindow(target);
        try
        {
            Visible(tools); Select(tools, child); Layout(tools);
            var groups = Properties(tools).Items.OfType<DevToolsPropertyGroup>().ToArray(); Assert.NotEmpty(groups); Assert.All(groups, group => Assert.True(group.IsExpanded));
            var models = groups.SelectMany(group => Assert.IsAssignableFrom<IEnumerable<DevToolsDependencyPropertyModel>>(group.ItemsSource)).ToArray();
            Assert.Equal(models.Length, models.Select(model => model.Property).Distinct().Count());
            var expected = child.GetType().GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.FieldType == typeof(DependencyProperty)).Select(field => (DependencyProperty)field.GetValue(null)!).Distinct().ToArray();
            Assert.Equal(expected.Length, models.Length);
            foreach (var group in groups)
            {
                Assert.All(Assert.IsAssignableFrom<IEnumerable<DevToolsDependencyPropertyModel>>(group.ItemsSource), model => Assert.Equal(group.Category.ToString(), model.EditorProperty.Category));
                Assert.Equal(group.Items.Count, Walk(group).OfType<DevToolsPropertyRow>().Count());
            }
            Select(tools, child); Layout(tools); Assert.All(models, model => Assert.Equal(DevToolsPropertyOrigin.Unavailable, model.Origin));
            var current = Properties(tools).Items.OfType<DevToolsPropertyGroup>().SelectMany(group => Assert.IsAssignableFrom<IEnumerable<DevToolsDependencyPropertyModel>>(group.ItemsSource)).ToArray();
            Assert.Equal(expected.Length, current.Length); Select(tools, null); Assert.Empty(Properties(tools).Items);
            Assert.All(current, model => Assert.Equal(DevToolsPropertyOrigin.Unavailable, model.Origin));
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    [Fact]
    public void CollapsingARealCategoryKeepsItsLiveModelAndDoesNotRebuildTheSelection()
    {
        Initialize(); var child = new Border { Width = 220 }; var target = new DisplayedTestWindow { Content = child }; Layout(target); var tools = new DevToolsWindow(target);
        try
        {
            Visible(tools); Select(tools, child); Layout(tools);
            var group = Properties(tools).Items.OfType<DevToolsPropertyGroup>().Single(value => value.Category == DevToolsPropertyCategory.Layout);
            var model = Assert.IsAssignableFrom<IEnumerable<DevToolsDependencyPropertyModel>>(group.ItemsSource).Single(value => value.Property == FrameworkElement.WidthProperty);
            var header = Assert.IsType<DevToolsActionButton>(group.FindName("PART_Header")); header.PerformClick(); Assert.False(group.IsExpanded);
            child.Width = 280; Layout(target); Assert.Equal(280d, model.EditorProperty.Value);
            header.PerformClick(); Layout(tools); Assert.True(group.IsExpanded); Assert.Contains(group, Properties(tools).Items.OfType<DevToolsPropertyGroup>());
            var row = Walk(group).OfType<DevToolsPropertyRow>().Single(value => ReferenceEquals(value.Model, model));
            Assert.Equal("280", Assert.IsType<PropertyGridEditor>(row.FindName("PART_Editor")).ValueText);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static void Visible(Window window) => typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Visibility.Visible]);
    private static DevToolsDetailsList Properties(DevToolsWindow tools) => (DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!;
    private static void Select(DevToolsWindow tools, Visual? element) => typeof(DevToolsWindow).GetMethod("UpdatePropertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [element]);
    private static IEnumerable<Visual> Walk(Visual root) { yield return root; for (int index = 0; index < root.VisualChildrenCount; index++) if (root.GetVisualChild(index) is { } child) foreach (var item in Walk(child)) yield return item; }
    private static void Layout(UIElement element) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(1040,860)); element.Arrange(new Rect(0,0,1040,860)); element.UpdateLayout(); } }
}
