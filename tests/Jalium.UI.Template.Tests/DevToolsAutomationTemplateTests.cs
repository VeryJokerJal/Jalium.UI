using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsAutomationTemplateTests
{
    public DevToolsAutomationTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void AutomationDictionaryProvidesItsDefaultStyles()
    {
        Assert.True(Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry.TryGet("Jalium.UI.Managed.Themes.Controls.DevToolsAutomation.jalxaml", out _));
        var view = new DevToolsAutomationView(); Layout(view); Assert.NotNull(view.Template);
        var row = new DevToolsAutomationPeerRow(); Layout(row, 400, 60); Assert.NotNull(row.Template);
    }

    [Fact]
    public void EmptyCommandsCountersAndMissingPartsKeepTheirPublicContracts()
    {
        int refreshes = 0;
        var view = new DevToolsAutomationView { RefreshCommand = new RelayCommand(() => refreshes++), NodeCount = 137, IssueCount = 2 };
        Layout(view); Assert.False(view.HasItems); Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Empty").Visibility);
        Assert.Equal("137", Part<DevToolsTag>(view, "PART_NodeCount").Text);
        Part<DevToolsActionButton>(view, "PART_Refresh").PerformClick(); Assert.Equal(1, refreshes);
        var original = view.Template; view.Template = EmptyTemplate(); Layout(view);
        view.ItemsSource = new[] { Node("New") }; view.IsBuilding = true; view.StatusText = "Reading peers";
        Assert.True(view.HasItems); Assert.Null(view.FindName("PART_PeerTree"));
        view.Template = original; Layout(view); Assert.NotNull(view.FindName("PART_PeerTree"));
        Assert.Equal("Reading peers", Part<TextBlock>(view, "PART_Status").Text);
        view.IsEnabled = false; Assert.False(Part<DevToolsActionButton>(view, "PART_Refresh").IsEnabled);
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsAutomationView.HasItemsProperty, true));
        Assert.Throws<ArgumentException>(() => view.CompactWidth = double.NaN);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(480)]
    [InlineData(1040)]
    public void ResponsiveTreeAndDetailsFitAndRestoreTheirWidePlacement(double width)
    {
        var node = Node("A long peer name that stays inspectable in narrow layouts");
        node.Properties.Add(new("Name", node.PeerName, DevToolsAutomationValueKind.String));
        node.Patterns.Add("Invoke");
        var view = new DevToolsAutomationView { ItemsSource = new[] { node }, SelectedItem = node }; Layout(view, width);
        var tree = Part<DevToolsSurface>(view, "PART_TreeSurface"); var details = Part<DevToolsSurface>(view, "PART_DetailsSurface");
        var treeEnd = tree.TranslatePoint(new Point(tree.ActualWidth, tree.ActualHeight), view);
        var detailsStart = details.TranslatePoint(new Point(), view);
        if (width <= 480) Assert.True(detailsStart.Y >= treeEnd.Y);
        else Assert.True(detailsStart.X >= treeEnd.X);
        var detailsEnd = details.TranslatePoint(new Point(details.ActualWidth, details.ActualHeight), view);
        Assert.True(detailsEnd.X <= width + .5); Assert.True(detailsEnd.Y <= 760.5);
        Assert.Equal(Visibility.Visible, Part<ItemsControl>(view, "PART_Patterns").Visibility);
        node.Patterns.Clear(); Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_NoPatterns").Visibility);
        Layout(view, 1040); Assert.Equal(2, Grid.GetColumn(details)); Assert.Equal(0, Grid.GetRow(details));
    }

    [Fact]
    public void ResizedCompactRowsFillTheBodyAgainWhenReturningToWideLayout()
    {
        var node = Node("Peer");
        var view = new DevToolsAutomationView { ItemsSource = new[] { node }, SelectedItem = node }; Layout(view, 480);
        var body = Part<Grid>(view, "PART_Body");
        // A vertical splitter persists its two resulting pixel heights on these rows.
        body.RowDefinitions[0].Height = new GridLength(200);
        body.RowDefinitions[2].Height = new GridLength(450);
        Layout(view, 480); Layout(view, 1040);
        var tree = Part<DevToolsSurface>(view, "PART_TreeSurface"); var details = Part<DevToolsSurface>(view, "PART_DetailsSurface");
        Assert.True(tree.ActualHeight >= body.ActualHeight - .5, $"tree={tree.ActualHeight}; body={body.ActualHeight}");
        Assert.True(details.ActualHeight >= body.ActualHeight - .5, $"details={details.ActualHeight}; body={body.ActualHeight}");
        Layout(view, 480); Assert.True(tree.ActualHeight > 100); Assert.True(details.ActualHeight > 100);
        Layout(view, 1040);
        body.ColumnDefinitions[0].Width = new GridLength(210);
        body.ColumnDefinitions[2].Width = new GridLength(740);
        Layout(view, 1040); Layout(view, 480);
        Assert.True(tree.ActualWidth >= body.ActualWidth - .5);
        Assert.True(details.ActualWidth >= body.ActualWidth - .5);
    }

    [Fact]
    public void HierarchicalTemplatesSelectionAndRetiredPartsStaySynchronized()
    {
        var root = Node("Root"); var child = Node("Child"); var leaf = Node("Leaf");
        root.Children.Add(child); child.Children.Add(leaf);
        var view = new DevToolsAutomationView { ItemsSource = new[] { root }, SelectedItem = leaf };
        Layout(view); Assert.True(root.IsExpanded); Assert.True(child.IsExpanded); Assert.True(leaf.IsSelected);
        var tree = Part<TreeView>(view, "PART_PeerTree");
        Assert.Contains(Descendants<DevToolsAutomationPeerRow>(tree), row => ReferenceEquals(row.Item, leaf));
        Assert.Same(leaf, view.SelectedItem);
        var rootContainer = Assert.IsType<TreeViewItem>(tree.ItemContainerGenerator.ContainerFromIndex(0));
        rootContainer.IsSelected = true; Assert.Same(root, view.SelectedItem); Assert.False(leaf.IsSelected);
        var oldTemplate = view.Template; view.Template = EmptyTemplate(); Layout(view);
        tree.RaiseEvent(new RoutedPropertyChangedEventArgs<object?>(root, leaf, TreeView.SelectedItemChangedEvent));
        Assert.Same(root, view.SelectedItem); Assert.Null(tree.ItemsSource);
        view.Template = oldTemplate; Layout(view); Assert.Same(root, view.SelectedItem);
        root.Children.Add(Node("Later")); Layout(view);
        Assert.Contains(Descendants<DevToolsAutomationPeerRow>(view), row => row.Item?.PeerName == "Later");
    }

    [Fact]
    public void NodePropertiesAndCustomPresentationRemainBindable()
    {
        var item = Node("Initial"); var row = new DevToolsAutomationPeerRow { Item = item }; Layout(row, 380, 70);
        item.PeerName = "Updated"; Assert.Equal("Updated", Part<TextBlock>(row, "PART_Name").Text);
        var peer = UIElementAutomationPeer.CreatePeerForElement(row)!; Assert.Contains("Updated", peer.GetName());
        AutomationProperties.SetName(row, "Explicit peer"); Assert.Equal("Explicit peer", peer.GetName());
        item.IsIssue = true; Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), Assert.IsType<SolidColorBrush>(Part<TextBlock>(row, "PART_Name").Foreground).Color);
        var view = new DevToolsAutomationView { ItemsSource = new[] { item }, SelectedItem = item };
        var nodeTemplate = new DataTemplate(); nodeTemplate.SetVisualTree(() => new TextBlock { Text = "Custom peer" }); view.NodeTemplate = nodeTemplate;
        item.Properties.Add(new("Error", "Getter failed", DevToolsAutomationValueKind.Error)); Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom peer");
        var readout = Assert.Single(Descendants<DevToolsReadout>(view)); Assert.Equal("Getter failed", readout.ValueText);
        Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(readout.ValueBrush).Color);
    }

    [Fact]
    public void SourceReplacementDetachesTheViewObserverAndUpdatesEmptyState()
    {
        var old = new ObservedCollection(); var current = new ObservedCollection();
        var view = new DevToolsAutomationView { ItemsSource = old }; Layout(view);
        Assert.Equal(1, old.ViewObservers); old.Add(Node("Old")); Assert.True(view.HasItems);
        view.SelectedItem = old[0];
        view.ItemsSource = current; Assert.Equal(0, old.ViewObservers); Assert.Equal(1, current.ViewObservers); Assert.False(view.HasItems); Assert.Null(view.SelectedItem);
        current.Add(Node("Current")); Assert.True(view.HasItems); current.Clear(); Assert.False(view.HasItems);
        view.ItemsSource = null; Assert.Equal(0, current.ViewObservers);
    }

    private static DevToolsAutomationItem Node(string name) => new() { PeerName = name, ClassName = "Button", Role = "Button" };
    private static T Part<T>(Control control, string name) where T : class => Assert.IsAssignableFrom<T>(control.FindName(name));
    private static ControlTemplate EmptyTemplate() { var template = new ControlTemplate(typeof(DevToolsAutomationView)); template.SetVisualTree(() => new Border()); return template; }
    private static void Layout(UIElement element, double width = 1040, double height = 760) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); } }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual { for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class ObservedCollection : ObservableCollection<DevToolsAutomationItem>, INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _observers;
        public int ViewObservers => _observers?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsAutomationView), StringComparison.Ordinal) == true) ?? 0;
        event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged { add { _observers += value; CollectionChanged += value; } remove { _observers -= value; CollectionChanged -= value; } }
    }
}
