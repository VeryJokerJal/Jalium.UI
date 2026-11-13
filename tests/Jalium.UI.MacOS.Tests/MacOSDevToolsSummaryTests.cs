using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsSummaryTests
{
    [Fact]
    public void InspectorUsesTheReusableSummaryAndKeepsTheSelectedNameLive()
    {
        Initialize(); var child = new Border { Name = "Selected", Child = new TextBlock { Text = "Content" } };
        var grid = new Grid(); grid.Children.Add(child); var target = new DisplayedTestWindow { Content = grid }; Layout(target);
        var tools = new DevToolsWindow(target);
        try
        {
            Select(tools, child); var summary = Assert.Single(Properties(tools).Items.OfType<DevToolsElementSummary>());
            Assert.Same(summary, Properties(tools).Items[0]); Assert.Equal("Border", summary.TypeName); Assert.Equal("Decorator", summary.BaseTypeName);
            Assert.Equal("Selected", summary.ElementName); Assert.True(summary.ShowStatistics); Assert.True(summary.Depth > 0);
            Assert.Equal(1, summary.ChildrenCount); Assert.Equal(1, summary.DescendantCount);
            child.Name = "Renamed"; Assert.Equal("Renamed", summary.ElementName); Assert.Equal("#Renamed", summary.NameText);
            Assert.Same(summary, Assert.Single(Properties(tools).Items.OfType<DevToolsElementSummary>()));
            Select(tools, null); Assert.Empty(Properties(tools).Items);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    [Fact]
    public void BreadcrumbCommandsRevealTheRequestedAncestorInTheActualInspector()
    {
        Initialize(); var child = new Border { Name = "Selected" }; var grid = new Grid(); grid.Children.Add(child);
        var target = new DisplayedTestWindow { Content = grid }; Layout(target); var tools = new DevToolsWindow(target);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [Visibility.Visible]);
            typeof(DevToolsWindow).GetMethod("RevealInInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [child]); Layout(tools);
            var path = Assert.Single(Properties(tools).Items.OfType<DevToolsBreadcrumb>());
            var nodes = Assert.IsAssignableFrom<IEnumerable<DevToolsBreadcrumbNode>>(path.ItemsSource).ToArray(); Assert.True(nodes.Length >= 2);
            Assert.True(nodes[^1].IsCurrent); Assert.True(nodes[^1].TryGetTarget(out var current)); Assert.Same(child, current);
            Assert.False(path.NavigateCommand!.CanExecute(nodes[^1]));
            var ancestor = nodes.Single(node => node.TryGetTarget(out var value) && ReferenceEquals(value, grid));
            Assert.True(path.NavigateCommand.CanExecute(ancestor)); path.NavigateCommand.Execute(ancestor);
            Assert.Equal("Grid", Assert.Single(Properties(tools).Items.OfType<DevToolsElementSummary>()).TypeName);
        }
        finally { tools.CloseDevTools(); target.Close(); }
    }
    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
    private static DevToolsDetailsList Properties(DevToolsWindow tools) => (DevToolsDetailsList)typeof(DevToolsWindow).GetField("_propertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tools)!;
    private static void Select(DevToolsWindow tools, Visual? element) => typeof(DevToolsWindow).GetMethod("UpdatePropertiesPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tools, [element]);
    private static void Layout(UIElement element) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(1040,860)); element.Arrange(new Rect(0,0,1040,860)); element.UpdateLayout(); } }
}
