using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsResourcesTests
{
    [Theory]
    [InlineData(480)]
    [InlineData(1040)]
    public void KeyboardEndpointsKeepMixedResourceRowsInsideTheViewport(double width)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var target = new Button { Name = "ResourceTarget", Content = "Target" };
        var body = new Border { Child = target };
        for (int i = 0; i < 2000; i++) body.Resources["Qa.Item" + i.ToString("D4")] = i;
        var host = new Window { Content = body }; Layout(host, width);
        var window = new DevToolsWindow(host);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            typeof(DevToolsWindow).GetMethod("RevealInInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { target });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Resources; Layout(window);
            var view = Assert.IsType<DevToolsResourcesView>(workspace.ResourcesContent);
            view.FilterText = "Qa.Item"; view.RefreshCommand!.Execute(null); Layout(window, width);
            var list = Assert.IsType<ListBox>(view.FindName("PART_ResourceList"));
            var scroller = Descendants<ScrollViewer>(list).Single();
            scroller.ScrollToVerticalOffset(5160); Layout(window, width);
            view.FilterText = ""; view.RefreshCommand.Execute(null); Layout(window, width);
            foreach (var key in new[] { Key.End, Key.Home, Key.End })
            {
                list.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, key, ModifierKeys.None, true, false, 0));
                window.UpdateLayout(); list.Dispatcher.ProcessQueue(); window.UpdateLayout();
                int index = key == Key.End ? list.Items.Count - 1 : 0;
                var row = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(index));
                var panel = Assert.IsType<VirtualizingStackPanel>(row.VisualParent);
                Assert.True(row.VisualBounds.Bottom > 0 && row.VisualBounds.Top < panel.ViewportHeight,
                    $"{key}: index={index}; row={row.VisualBounds}; viewport={panel.ViewportHeight}; offset={panel.VerticalOffset}; extent={panel.ExtentHeight}");
            }
        }
        finally { Keyboard.ClearFocus(); window.CloseDevTools(); host.Close(); }
    }

    [Fact]
    public void ActualResourcesViewReusesBoundedModelsFiltersWinnersAndStopsInactiveTimers()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var target = new Button { Name = "ResourceTarget", Content = "Target", Style = new Style(typeof(Button)) };
        var body = new Border { Name = "ResourceOwner", Child = target };
        body.Resources["Qa.Shared"] = "parent";
        target.Style.Resources["Qa.Shared"] = "style";
        target.Resources["Qa.Shared"] = null;
        for (int i = 0; i < 1500; i++) body.Resources["Qa.Item" + i.ToString("D4")] = i;
        var host = new Window { Title = "Resources target", Content = body }; Layout(host);
        var window = new DevToolsWindow(host);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            typeof(DevToolsWindow).GetMethod("RevealInInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { target });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Resources; Layout(window);
            var view = Assert.IsType<DevToolsResourcesView>(workspace.ResourcesContent);
            var initial = Rows(view); Assert.True(initial.Length > 1500); Assert.True(view.DeclarationCount >= 1503);
            Assert.Equal(3, initial.Count(row => row.RowKind == DevToolsResourceRowKind.Resource && row.KeyText == "Qa.Shared"));
            var winner = Assert.Single(initial, row => row.RowKind == DevToolsResourceRowKind.Resource && row.KeyText == "Qa.Shared" && row.IsWinner);
            Assert.Equal("<null>", winner.ValueText);
            var retained = Assert.Single(initial, row => row.KeyText == "Qa.Item1499");
            view.RefreshCommand!.Execute(null); Layout(window);
            Assert.Same(retained, Assert.Single(Rows(view), row => row.KeyText == retained.KeyText));
            var list = Assert.IsType<ListBox>(view.FindName("PART_ResourceList"));
            Assert.InRange(Enumerable.Range(0, initial.Length).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null), 1, 100);
            list.ScrollIntoView(retained); Layout(window);
            Assert.NotNull(list.ItemContainerGenerator.ContainerFromItem(retained));
            view.FilterText = "Qa.Shared"; Assert.NotNull(Timer(window));
            view.RefreshCommand.Execute(null); Layout(window); Assert.Null(Timer(window));
            Assert.Equal(3, Rows(view).Count(row => row.RowKind == DevToolsResourceRowKind.Resource));
            view.ToggleWinnersOnlyCommand!.Execute(null); Layout(window);
            Assert.True(view.IsWinnersOnly); Assert.Single(Rows(view), row => row.RowKind == DevToolsResourceRowKind.Resource);
            var cache = Assert.IsAssignableFrom<IDictionary>(typeof(DevToolsWindow).GetField("_resourceItemByIdentity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.Equal(Rows(view).Length, cache.Count);
            view.FilterText = "missing"; Assert.NotNull(Timer(window));
            workspace.SelectedPage = DevToolsPage.Inspector; Assert.Null(Timer(window));
            view.FilterText = "Qa.Item1499"; Assert.Null(Timer(window));
            workspace.SelectedPage = DevToolsPage.Resources; Layout(window);
            Assert.Single(Rows(view), row => row.RowKind == DevToolsResourceRowKind.Resource);
            view.FilterText = "close"; Assert.NotNull(Timer(window));
            window.CloseDevTools(); Assert.Null(Timer(window));
        }
        finally { window.CloseDevTools(); host.Close(); }
    }

    private static object? Timer(DevToolsWindow window) => typeof(DevToolsWindow).GetField("_resourcesSearchTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual { for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static DevToolsResourceItem[] Rows(DevToolsResourcesView view) => ((IEnumerable)view.ItemsSource!).Cast<DevToolsResourceItem>().ToArray();
    private static void Layout(UIElement element, double width = 1040) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 860)); element.Arrange(new Rect(0, 0, width, 860)); element.UpdateLayout(); } }
}
