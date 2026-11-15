using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsAutomationTests
{
    [Fact]
    public void NarrowPropertiesCanScrollToTheSupportedPatterns()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var item = new DevToolsAutomationItem { PeerName = "Peer", Role = "Button" };
        for (int i = 0; i < 12; i++) item.Properties.Add(new("Property " + i, "Value " + i));
        item.Patterns.Add("Invoke");
        var view = new DevToolsAutomationView { ItemsSource = new[] { item }, SelectedItem = item };
        var window = new DisplayedTestWindow { Content = view };
        void LayoutNarrow() { for (int i = 0; i < 3; i++) { window.Measure(new Size(480, 760)); window.Arrange(new Rect(0, 0, 480, 760)); window.UpdateLayout(); } }
        try
        {
            LayoutNarrow(); var scroller = Assert.IsType<ScrollViewer>(view.FindName("PART_Details"));
            var surface = Assert.IsType<DevToolsSurface>(view.FindName("PART_DetailsSurface"));
            Assert.InRange(scroller.ActualWidth, surface.ActualWidth - 27, surface.ActualWidth - 25);
            var point = scroller.TranslatePoint(new Point(scroller.ActualWidth - 8, scroller.ActualHeight / 2), window);
            var hit = window.HitTest(point)?.VisualHit;
            bool inside = false;
            for (var current = hit; current != null; current = current.VisualParent) if (ReferenceEquals(current, scroller)) { inside = true; break; }
            Assert.True(inside, $"detail blank-area hit={hit?.GetType().Name}");
            Assert.True(scroller.ExtentHeight > scroller.ViewportHeight, $"extent={scroller.ExtentHeight}; viewport={scroller.ViewportHeight}");
            scroller.ScrollToBottom(); LayoutNarrow(); Assert.True(scroller.VerticalOffset > 0);
            var patterns = Assert.IsType<ItemsControl>(view.FindName("PART_Patterns"));
            double top = patterns.TranslatePoint(new Point(), scroller).Y;
            Assert.True(top >= 0 && top + patterns.ActualHeight <= scroller.ActualHeight + .5, $"patterns={top}; height={patterns.ActualHeight}; viewport={scroller.ActualHeight}");
        }
        finally { window.Close(); }
    }

    [Fact]
    public void ActualAutomationViewCompletesWideBranchesRestoresSelectionAndPausesOffPage()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var content = new StackPanel();
        for (int i = 0; i < 137; i++) content.Children.Add(new Button { Name = "Peer" + i.ToString("D3"), Content = "Peer " + i });
        var target = new Window { Title = "Automation target", Content = content }; Layout(target);
        var window = new DevToolsWindow(target);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Automation; Layout(window);
            var view = Assert.IsType<DevToolsAutomationView>(workspace.AutomationContent);
            Assert.True(view.IsBuilding); Assert.True(Timer(window)?.IsEnabled);
            Tick(window); int beforePause = view.NodeCount;
            workspace.SelectedPage = DevToolsPage.Inspector; Assert.False(Timer(window)?.IsEnabled);
            Tick(window); Assert.Equal(beforePause, view.NodeCount);
            workspace.SelectedPage = DevToolsPage.Automation; Finish(window, view); Layout(window);
            var nodes = Flatten(view.ItemsSource!).ToArray(); Assert.Equal(nodes.Length, view.NodeCount);
            Assert.Equal(137, nodes.Count(node => node.Role == "Button" && node.PeerName.StartsWith("Peer ", StringComparison.Ordinal)));
            var last = Assert.Single(nodes, node => node.Role == "Button" && node.PeerName == "Peer 136"); view.SelectedItem = last; Layout(window);
            Assert.Contains("Invoke", last.Patterns); Assert.Contains(last.Properties, property => property.Name == "AutomationId" && property.Value == "Peer136");
            var tree = Assert.IsType<TreeView>(view.FindName("PART_PeerTree"));
            var oldContainer = FindContainer(tree, last)!; Assert.NotNull(oldContainer); Assert.True(oldContainer.Focus());
            var identity = last.Identity;
            view.RefreshCommand!.Execute(null); Finish(window, view); Layout(window);
            window.Dispatcher.ProcessQueue(); Layout(window);
            Assert.NotSame(last, view.SelectedItem); Assert.Same(identity, view.SelectedItem!.Identity);
            Assert.Equal("Peer 136", view.SelectedItem.PeerName); Assert.False(view.IsBuilding); Assert.False(Timer(window)?.IsEnabled);
            var focused = Assert.IsType<TreeViewItem>(Keyboard.FocusedElement);
            Assert.Same(view.SelectedItem, focused.Header); Assert.NotSame(oldContainer, focused);
            view.RefreshCommand.Execute(null); Assert.True(view.IsBuilding);
            window.CloseDevTools(); Assert.False(Timer(window)?.IsEnabled); Assert.Empty(view.ItemsSource!); Assert.False(view.IsBuilding);
        }
        finally { Keyboard.ClearFocus(); window.CloseDevTools(); target.Close(); }
    }
    private static TreeViewItem? FindContainer(ItemsControl owner, DevToolsAutomationItem item)
    {
        foreach (var data in owner.Items)
        {
            var container = data as TreeViewItem ?? owner.ItemContainerGenerator.ContainerFromItem(data) as TreeViewItem;
            if (container == null) continue;
            if (ReferenceEquals(container.Header, item)) return container;
            if (FindContainer(container, item) is { } child) return child;
        }
        return null;
    }
    private static Jalium.UI.Threading.DispatcherTimer? Timer(DevToolsWindow window) => (Jalium.UI.Threading.DispatcherTimer?)typeof(DevToolsWindow).GetField("_uiaBuildTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private static void Tick(DevToolsWindow window) => typeof(DevToolsWindow).GetMethod("OnUiaBuildTimerTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { null, EventArgs.Empty });
    private static void Finish(DevToolsWindow window, DevToolsAutomationView view) { for (int pass = 0; view.IsBuilding && pass < 2000; pass++) Tick(window); Assert.False(view.IsBuilding); }
    private static IEnumerable<DevToolsAutomationItem> Flatten(IEnumerable<DevToolsAutomationItem> items) { foreach (var item in items) { yield return item; foreach (var child in Flatten(item.Children)) yield return child; } }
    private static void Layout(UIElement element) { element.Measure(new Size(1040, 860)); element.Arrange(new Rect(0, 0, 1040, 860)); element.UpdateLayout(); }
}
