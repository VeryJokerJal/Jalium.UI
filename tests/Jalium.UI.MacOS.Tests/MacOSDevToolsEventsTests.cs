using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsEventsTests
{
    private static readonly RoutedEvent RouteEvent = EventManager.RegisterRoutedEvent("MacEventRoute", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(MacOSDevToolsEventsTests));

    [Fact]
    public void ActualEventViewBoundsItsCacheRetainsModelsAndRevealsHorizontallyScrolledNodes()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        bool recording = RoutedEventDiagnostics.IsRecording; var filter = RoutedEventDiagnostics.GetFilter();
        RoutedEventDiagnostics.StopRecording(); RoutedEventDiagnostics.Reset(); RoutedEventDiagnostics.SetFilter(Array.Empty<string>());
        Mouse.Capture(null); Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
        var leaf = new Button { Name = "Leaf", Content = "Target" }; UIElement child = leaf;
        for (int i = 0; i < 40; i++) child = new Border { Name = "Ancestor" + i, Child = child };
        var body = new StackPanel { Name = "Root" }; body.Children.Add(child);
        var host = new Window { Name = "TargetWindow", Title = "Event target", Content = body }; Layout(host);
        leaf.AddHandler(RouteEvent, new RoutedEventHandler((_, e) => e.Handled = true));
        var window = new DevToolsWindow(host);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Events; Layout(window);
            var view = Assert.IsType<DevToolsEventsView>(workspace.EventsContent);
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.True(view.IsRecording);
            for (int i = 0; i < 600; i++) leaf.RaiseEvent(new RoutedEventArgs(RouteEvent, leaf));
            var retained = RoutedEventDiagnostics.Snapshot().Last();
            Refresh(window); Layout(window);
            Assert.Equal(512, view.EventCount); Assert.Same(retained, RoutedEventDiagnostics.Snapshot().Last());
            var rows = ((IEnumerable)view.EventsSource!).Cast<DevToolsRoutedEventItem>().ToArray(); Assert.Equal(80, rows.Length);
            Assert.All(rows, row => Assert.True(row.Handled));
            leaf.RaiseEvent(new RoutedEventArgs(RouteEvent, leaf)); retained = RoutedEventDiagnostics.Snapshot().Last();
            Refresh(window); Layout(window);
            var current = ((IEnumerable)view.EventsSource!).Cast<DevToolsRoutedEventItem>().ToArray();
            Assert.Same(rows[0], current[1]); Assert.Same(rows[1], current[2]);
            var cache = Assert.IsAssignableFrom<IDictionary>(typeof(DevToolsWindow).GetField("_eventItemByEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)); Assert.Equal(80, cache.Count);
            var list = Part<ListBox>(view, "PART_EventList");
            var first = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
            var path = Assert.Single(Descendants<ListBox>(first));
            var panel = Assert.Single(Descendants<VirtualizingStackPanel>(path));
            Assert.Equal(Orientation.Horizontal, panel.Orientation);
            var wheelPoint = path.TranslatePoint(new Point(200, 20), window);
            var wheelObserved = new List<string>();
            foreach (var wheelEvent in new[] { UIElement.PreviewMouseWheelEvent, UIElement.MouseWheelEvent })
                window.AddHandler(wheelEvent, new MouseWheelEventHandler((_, args) => wheelObserved.Add($"{args.RoutedEvent?.Name}:{args.Source?.GetType().Name}; delta={args.HorizontalDelta},{args.VerticalDelta}; handled={args.Handled}")), true);
            var wheel = new PlatformEvent
            {
                Type = PlatformEventType.MouseWheel,
                MouseX = (int)Math.Round(wheelPoint.X * window.DpiScale),
                MouseY = (int)Math.Round(wheelPoint.Y * window.DpiScale),
                // The shared platform ABI uses positive X for rightward scrolling.
                WheelDeltaX = 1, WheelDeltaY = 0, WheelHasPreciseScrollingDeltas = true,
                WheelPhase = MouseWheelPhase.Changed, HasMouseButtonStates = true
            };
            typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { wheel });
            Assert.True(panel.HorizontalOffset > 0, $"Nonzero horizontal platform wheel input must scroll the nested path. point={wheelPoint}; hit={window.HitTest(wheelPoint)?.VisualHit?.GetType().Name}; panel={panel.ExtentWidth}/{panel.ViewportWidth}; observed={string.Join('|', wheelObserved)}");
            panel.SetHorizontalOffset(double.MaxValue);
            var viewport = new Size(panel.ViewportWidth, panel.ViewportHeight);
            // This managed display has no compositor layout tick to settle a
            // queued scroll and the newly measured item-width estimates.
            for (int pass = 0; pass < 3; pass++) { panel.InvalidateMeasure(); panel.Measure(viewport); panel.Arrange(new Rect(new Point(), viewport)); }
            var container = Enumerable.Range(0, path.Items.Count).Select(path.ItemContainerGenerator.ContainerFromIndex).OfType<ListBoxItem>()
                .First(row => row.VisualBounds.X >= 0 && row.VisualBounds.Right <= panel.ViewportWidth);
            var node = Assert.Single(Descendants<DevToolsEventNode>(container));
            var nodeItem = Assert.IsType<DevToolsEventNodeItem>(container.Content);
            Assert.True(nodeItem.VisualReference!.TryGetTarget(out var expected));
            MouseClick(window, node);
            Assert.Equal(DevToolsPage.Inspector, workspace.SelectedPage);
            Assert.Same(expected, typeof(DevToolsWindow).GetField("_selectedVisual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            Assert.Same(retained, RoutedEventDiagnostics.Snapshot().Last());
            workspace.SelectedPage = DevToolsPage.Events; Layout(window);
            view.FilterText = RouteEvent.Name + "; Other, Third"; Part<DevToolsActionButton>(view, "PART_ApplyFilter").PerformClick();
            Assert.Contains(RouteEvent.Name, RoutedEventDiagnostics.GetFilter()); Assert.Contains("Other", RoutedEventDiagnostics.GetFilter()); Assert.Contains("Third", RoutedEventDiagnostics.GetFilter());
            leaf.RaiseEvent(new RoutedEventArgs(RouteEvent, leaf)); Assert.Same(retained, RoutedEventDiagnostics.Snapshot().Last());
            view.FilterText = ""; Part<DevToolsActionButton>(view, "PART_ApplyFilter").PerformClick();
            leaf.RaiseEvent(new RoutedEventArgs(RouteEvent, leaf)); Assert.NotSame(retained, RoutedEventDiagnostics.Snapshot().Last());
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.False(RoutedEventDiagnostics.IsRecording);
            Part<DevToolsActionButton>(view, "PART_ResetButton").PerformClick(); Assert.False(view.HasEvents); Assert.Equal(0, view.EventCount); Assert.Equal(0, cache.Count);
        }
        finally
        {
            Mouse.Capture(null); Keyboard.ClearFocus(); window.CloseDevTools(); host.Close();
            RoutedEventDiagnostics.StopRecording(); RoutedEventDiagnostics.Reset(); RoutedEventDiagnostics.SetFilter(filter); if (recording) RoutedEventDiagnostics.StartRecording();
        }
    }

    private static void Refresh(DevToolsWindow window) => typeof(DevToolsWindow).GetMethod("RefreshEventsGraph", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static void MouseClick(Window window, DevToolsEventNode node)
    {
        var point = node.TranslatePoint(new Point(node.ActualWidth / 2, node.ActualHeight / 2), window);
        var path = new List<Visual>(); for (var current = window.HitTest(point)?.VisualHit as Visual; current != null; current = current.VisualParent) path.Add(current);
        Assert.Contains(node, path);
        foreach (var type in new[] { PlatformEventType.MouseDown, PlatformEventType.MouseUp })
        {
            var input = new PlatformEvent { Type = type, MouseX = (int)Math.Round(point.X * window.DpiScale), MouseY = (int)Math.Round(point.Y * window.DpiScale), Button = 0, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0u };
            typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { input });
        }
    }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(UIElement element) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(1040, 860)); element.Arrange(new Rect(0, 0, 1040, 860)); element.UpdateLayout(); } }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
