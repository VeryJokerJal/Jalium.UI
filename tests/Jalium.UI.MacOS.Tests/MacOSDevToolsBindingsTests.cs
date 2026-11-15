using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsBindingsTests
{
    [Fact]
    public void ActualBindingViewReusesRowsPreservesTargetDiagnosticsAndRevealsBothEndpoints()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        bool wasRecording = BindingDiagnostics.IsRecording;
        BindingDiagnostics.StopRecording(); BindingDiagnostics.Reset();
        var source = new TextBox { Name = "Source", Text = "Initial" };
        var target = new TextBox { Name = "Target" };
        target.SetBinding(TextBox.TextProperty, new Binding("Text") { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        target.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Text") { Source = source, Mode = BindingMode.OneWay });
        var body = new StackPanel(); body.Children.Add(source); body.Children.Add(target);
        var host = new Window { Title = "Binding target", Content = body }; Layout(host);
        var window = new DevToolsWindow(host);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content);
            Invoke(window, "RevealInInspector", target);
            workspace.SelectedPage = DevToolsPage.Bindings; Layout(window);
            var view = Assert.IsType<DevToolsBindingsView>(workspace.BindingsContent);
            var bindings = ((IEnumerable)view.BindingsSource!).Cast<DevToolsBindingItem>().ToArray(); Assert.Equal(2, bindings.Length);
            Assert.Equal("TextBox  #Target", view.SelectedElementName);
            var list = Part<ListBox>(view, "PART_BindingsList"); var row = list.ItemContainerGenerator.ContainerFromIndex(0);
            var flow = Assert.Single(Descendants<DevToolsBindingFlow>(Assert.IsType<ListBoxItem>(row)));
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.True(BindingDiagnostics.IsRecording); Assert.True(view.IsRecording);
            for (int i = 0; i < 160; i++) source.Text = $"Target change {i}";
            int captured = BindingDiagnostics.Snapshot().Count;
            Invoke(window, "RefreshBindingsOverview"); Invoke(window, "RefreshBindingsEvents"); Layout(window);
            Assert.Equal(captured, BindingDiagnostics.Snapshot().Count);
            Assert.Equal(captured, view.EventCount);
            Assert.Equal(120, ((IEnumerable)view.EventsSource!).Cast<DevToolsBindingEventItem>().Count());
            Assert.Same(bindings[0], ((IEnumerable)view.BindingsSource!).Cast<DevToolsBindingItem>().First());
            Assert.Same(row, list.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal("Target change 159", target.Text);
            Assert.All(bindings, item => Assert.NotEqual("T 0", item.TargetCountText));
            MouseClick(window, Part<Button>(flow, "PART_TargetButton"));
            Assert.Equal(DevToolsPage.Inspector, workspace.SelectedPage); Assert.Same(target, Selected(window));
            workspace.SelectedPage = DevToolsPage.Bindings; Layout(window);
            MouseClick(window, Part<Button>(flow, "PART_SourceButton"));
            Assert.Equal(DevToolsPage.Inspector, workspace.SelectedPage); Assert.Same(source, Selected(window));
            workspace.SelectedPage = DevToolsPage.Bindings; Layout(window);
            Assert.False(view.HasBindings); Assert.Equal("TextBox  #Source", view.SelectedElementName);
            Assert.Equal(captured, BindingDiagnostics.Snapshot().Count);
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.False(BindingDiagnostics.IsRecording);
            Invoke(window, "RevealInInspector", target); workspace.SelectedPage = DevToolsPage.Bindings; Layout(window);
            Assert.NotNull(typeof(DevToolsWindow).GetField("_bindingsRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            target.SetBinding(FrameworkElement.TagProperty, new Binding("Value") { Source = new InvalidSource(), ValidatesOnDataErrors = true });
            Invoke(window, "OnBindingsRefreshTimerTick", null!, EventArgs.Empty); Layout(window);
            var invalid = Assert.Single(((IEnumerable)view.BindingsSource!).Cast<DevToolsBindingItem>(), item => item.TargetProperty == "Tag");
            Assert.True(invalid.HasValidationError); Assert.Equal("Source validation failed", invalid.LastError);
            Assert.Contains(((IEnumerable)view.EventsSource!).Cast<DevToolsBindingEventItem>(), item => item.Kind == "Error" && item.Message == "Source validation failed");
            Assert.False(view.IsRecording);
            Part<DevToolsActionButton>(view, "PART_ResetButton").PerformClick();
            Assert.Equal(0, view.EventCount); Assert.False(view.HasEvents);
            workspace.SelectedPage = DevToolsPage.Inspector;
            Assert.Null(typeof(DevToolsWindow).GetField("_bindingsRefreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
        }
        finally
        {
            Mouse.Capture(null); Keyboard.ClearFocus(); window.CloseDevTools(); host.Close();
            BindingDiagnostics.StopRecording(); BindingDiagnostics.Reset(); if (wasRecording) BindingDiagnostics.StartRecording();
        }
    }

    private static void MouseClick(Window window, Button action)
    {
        var point = action.TranslatePoint(new Point(action.ActualWidth / 2, action.ActualHeight / 2), window);
        var path = new List<Visual>(); for (var current = window.HitTest(point)?.VisualHit as Visual; current != null; current = current.VisualParent) path.Add(current);
        Assert.Contains(action, path);
        foreach (var type in new[] { PlatformEventType.MouseDown, PlatformEventType.MouseUp })
        {
            var input = new PlatformEvent { Type = type, MouseX = (int)Math.Round(point.X * window.DpiScale), MouseY = (int)Math.Round(point.Y * window.DpiScale), Button = 0, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0u };
            typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { input });
        }
    }
    private static object? Selected(DevToolsWindow window) => typeof(DevToolsWindow).GetField("_selectedVisual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
    private sealed class InvalidSource : System.ComponentModel.IDataErrorInfo
    {
        public string Value => "Invalid";
        public string Error => "Source validation failed";
        public string this[string columnName] => Error;
    }
    private static void Invoke(DevToolsWindow window, string name, params object[] args) => typeof(DevToolsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(UIElement element) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(1040, 860)); element.Arrange(new Rect(0, 0, 1040, 860)); element.UpdateLayout(); } }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
}
