using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsPrimitiveTemplateTests
{
    public DevToolsPrimitiveTemplateTests()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
    }

    [Fact]
    public void ActionLabelIconAndAppearanceAreTemplateOwnedAndLive()
    {
        var button = new DevToolsActionButton { Label = "Refresh" };
        Layout(button);
        Assert.Equal("REFRESH", Part<TextBlock>(button, "PART_Label").Text);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(button, "PART_Icon").Visibility);
        button.Icon = "↻";
        button.Label = "Start recording";
        button.Variant = DevToolsButtonVariant.Primary;
        Assert.Equal("↻", Part<TextBlock>(button, "PART_Icon").Text);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(button, "PART_Icon").Visibility);
        Assert.Equal("START RECORDING", Part<TextBlock>(button, "PART_Label").Text);
        Assert.Equal(Color.FromRgb(0xFF, 0xB2, 0x2E), ColorOf(Part<Border>(button, "PART_Surface").Background));
        button.Variant = DevToolsButtonVariant.Danger;
        Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), ColorOf(Part<Border>(button, "PART_Surface").Background));
        button.Variant = DevToolsButtonVariant.Default;
        button.IsActive = true;
        Assert.Equal(Color.FromRgb(0xFF, 0xB2, 0x2E), ColorOf(Part<Border>(button, "PART_Surface").BorderBrush));
    }

    [Theory]
    [InlineData(Key.Return)]
    [InlineData(Key.Space)]
    public void ActionUsesKeyboardCommandsAndDisabledAutomation(Key key)
    {
        var command = new CountingCommand();
        var parameter = new object();
        var button = new DevToolsActionButton { Label = "Refresh", Command = command, CommandParameter = parameter };
        Layout(button);
        SendKey(button, key, UIElement.KeyDownEvent);
        SendKey(button, key, UIElement.KeyUpEvent);
        Assert.Equal(1, command.Count);
        Assert.Same(parameter, command.LastParameter);
        var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke));
        Assert.Equal("Refresh", peer.GetName());
        invoke.Invoke();
        Assert.Equal(2, command.Count);
        command.SetEnabled(false);
        Assert.False(button.IsEnabled);
        Assert.Equal(0.45, Part<Border>(button, "PART_Surface").Opacity, 3);
        Assert.Throws<InvalidOperationException>(invoke.Invoke);
        SendKey(button, key, UIElement.KeyDownEvent);
        SendKey(button, key, UIElement.KeyUpEvent);
        Assert.Equal(2, command.Count);
        command.SetEnabled(true);
        Assert.True(button.IsEnabled);
        Assert.Equal(1, Part<Border>(button, "PART_Surface").Opacity, 3);
    }

    [Fact]
    public void ConsumerStyleResourcesAndTemplatesDoNotRequireDefaultVisualParts()
    {
        var foreground = new SolidColorBrush(Color.FromRgb(40, 90, 120));
        var background = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var style = new Style(typeof(DevToolsActionButton));
        style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(13, 7, 13, 7)));
        var button = new DevToolsActionButton { Label = "Copy", Style = style };
        button.Resources["DevTools.Control"] = background;
        Layout(button);
        Assert.Same(background, Part<Border>(button, "PART_Surface").Background);
        Assert.Same(foreground, Part<TextBlock>(button, "PART_Label").Foreground);
        Assert.Equal(new Thickness(13, 7, 13, 7), Part<Border>(button, "PART_Surface").Padding);
        var oldLabel = Part<TextBlock>(button, "PART_Label");
        button.Template = new ControlTemplate(typeof(DevToolsActionButton));
        button.Template.SetVisualTree(static () => new Border());
        Layout(button);
        string detachedText = oldLabel.Text;
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        button.Label = "After";
        button.Variant = DevToolsButtonVariant.Danger;
        button.PerformClick();
        Assert.Equal(1, clicks);
        Assert.Null(button.FindName("PART_Label"));
        Assert.Equal(detachedText, oldLabel.Text);
    }

    [Fact]
    public void SegmentsUseSilentSynchronizationAndDirectionalSelection()
    {
        var calls = new List<int>();
        var selector = new DevToolsSegmentedSelector();
        selector.AddSegment("≡", "Flat list", () => calls.Add(0));
        selector.AddSegment("▦", "Visual tree", () => calls.Add(1));
        selector.AddSegment("⧉", "Logical tree", () => calls.Add(2));
        selector.SetSelectedSilent(0);
        Layout(selector);
        Assert.Empty(calls);
        var first = Assert.IsType<DevToolsSegmentItem>(selector.ItemContainerGenerator.ContainerFromIndex(0));
        var third = Assert.IsType<DevToolsSegmentItem>(selector.ItemContainerGenerator.ContainerFromIndex(2));
        Assert.Equal(Orientation.Horizontal, Assert.IsType<StackPanel>(first.VisualParent).Orientation);
        Assert.True(third.TranslatePoint(new Point(), selector).X > first.TranslatePoint(new Point(), selector).X);
        Assert.Equal(28, third.ActualWidth);
        SendKey(selector, Key.Right, UIElement.PreviewKeyDownEvent);
        Assert.Equal(1, selector.SelectedIndex);
        Assert.Equal(new[] { 1 }, calls);
        var selected = Assert.IsType<DevToolsSegmentItem>(selector.Items[1]);
        Assert.Equal(Visibility.Visible, Part<Border>(selected, "PART_Dot").Visibility);
        Assert.Equal("Visual tree", UIElementAutomationPeer.CreatePeerForElement(selected)!.GetName());
        selected.IsEnabled = false;
        selector.SetSelectedSilent(2);
        SendKey(selector, Key.Left, UIElement.PreviewKeyDownEvent);
        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(new[] { 1, 0 }, calls);
        selector.IsEnabled = false;
        SendKey(selector, Key.Right, UIElement.PreviewKeyDownEvent);
        Assert.Equal(0, selector.SelectedIndex);
        selector.Items.Clear();
        var callbacks = (IDictionary)typeof(DevToolsSegmentedSelector).GetField("_callbacks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(selector)!;
        Assert.Empty(callbacks);
    }

    [Fact]
    public void PanelHasBoundedBodyAndThemeStatesRestoreOnKindChanges()
    {
        var body = new ScrollViewer { Content = new Border { Height = 1000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var panel = new DevToolsSurface { Kind = DevToolsSurfaceKind.Panel, Heading = "Renderer snapshot", Content = body };
        Layout(panel, 420, 240);
        Assert.Equal(Visibility.Visible, Part<StackPanel>(panel, "PART_Header").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<DevToolsStatusIndicator>(panel, "PART_Indicator").Visibility);
        Assert.True(body.ViewportHeight > 0 && body.ViewportHeight < 240);
        Assert.True(body.ExtentHeight >= 1000);
        panel.IndicatorBrush = new SolidColorBrush(Color.FromRgb(1, 90, 120));
        Assert.Equal(Visibility.Visible, Part<DevToolsStatusIndicator>(panel, "PART_Indicator").Visibility);
        panel.Kind = DevToolsSurfaceKind.Toolbar;
        Layout(panel, 420, 240);
        Assert.Equal(Visibility.Collapsed, Part<StackPanel>(panel, "PART_Header").Visibility);
        Assert.Equal(new Thickness(0, 0, 0, 1), Part<Border>(panel, "PART_Surface").BorderThickness);
        panel.Kind = DevToolsSurfaceKind.Card;
        Assert.Equal(new Thickness(1), Part<Border>(panel, "PART_Surface").BorderThickness);
        Assert.Equal(new Thickness(8), Part<Border>(panel, "PART_Surface").Padding);
        Assert.Equal("Renderer snapshot", UIElementAutomationPeer.CreatePeerForElement(panel)!.GetName());
        panel.Template = new ControlTemplate(typeof(DevToolsSurface));
        panel.Template.SetVisualTree(static () => new ContentPresenter());
        Layout(panel);
        panel.Kind = DevToolsSurfaceKind.Panel;
        Assert.Same(body, panel.Content);
    }

    [Fact]
    public void StatusAndCornerDecorationFollowPublicProperties()
    {
        var brush = new SolidColorBrush(Color.FromRgb(12, 130, 80));
        var frame = new DevToolsSurface { Kind = DevToolsSurfaceKind.CornerFrame, TickSize = 12, TickBrush = brush };
        Layout(frame);
        var ticks = Part<Grid>(frame, "PART_Ticks");
        Assert.Equal(Visibility.Visible, ticks.Visibility);
        Assert.All(ticks.Children.OfType<Border>(), tick => { Assert.Equal(12, tick.Width); Assert.Same(brush, tick.BorderBrush); });
        var status = new DevToolsStatusIndicator { Foreground = brush };
        Layout(status);
        Assert.Same(brush, Part<Border>(status, "PART_Core").Background);
        status.IsOn = false;
        Assert.NotSame(brush, Part<Border>(status, "PART_Core").Background);
        status.IsOn = true;
        Assert.Same(brush, Part<Border>(status, "PART_Core").Background);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.25)]
    [InlineData(1, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    public void MeterUsesExactFractionIncludingEmptyAndFull(double input, double expected)
    {
        var row = new DevToolsMeterRow { Label = "GPU", Fraction = input, ValueText = "2 ms" };
        Layout(row, 400, 40);
        var meter = Part<DevToolsMeter>(row, "PART_Meter");
        Assert.Equal(expected, row.Fraction);
        Assert.Equal(expected, meter.Value);
        Assert.Equal(1, meter.Maximum);
        Assert.Equal("GPU", UIElementAutomationPeer.CreatePeerForElement(meter)!.GetName());
        Assert.Equal(expected * 400, Part<Border>(meter, "PART_Indicator").Width, 3);
        row.Fraction = 0.5;
        Layout(row, 200, 40);
        Assert.Equal(100, Part<Border>(meter, "PART_Indicator").Width, 3);
    }

    [Fact]
    public void ProportionalSegmentsHandleEmptyInvalidLargeAndReplacedSources()
    {
        var a = new SolidColorBrush(Color.FromRgb(12, 130, 80));
        var b = new SolidColorBrush(Color.FromRgb(130, 40, 80));
        var bar = new DevToolsStackedBar { ItemsSource = new[] { new DevToolsBarSegment(double.MaxValue, a), new DevToolsBarSegment(double.MaxValue, b), new DevToolsBarSegment(double.NaN, a) } };
        Layout(bar, 400, 10);
        Assert.Null(UIElementAutomationPeer.CreatePeerForElement(bar));
        var first = Assert.IsType<DevToolsBarSegmentPresenter>(bar.ItemContainerGenerator.ContainerFromIndex(0));
        var second = Assert.IsType<DevToolsBarSegmentPresenter>(bar.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Equal(200, first.ActualWidth, 3);
        Assert.Equal(200, second.ActualWidth, 3);
        Assert.Same(a, first.Background);
        bar.ItemsSource = new[] { new DevToolsBarSegment(1, b), new DevToolsBarSegment(3, a) };
        Layout(bar, 400, 10);
        Assert.Equal(100, Assert.IsType<DevToolsBarSegmentPresenter>(bar.ItemContainerGenerator.ContainerFromIndex(0)).ActualWidth, 3);
        bar.ItemsSource = Array.Empty<DevToolsBarSegment>();
        Layout(bar, 400, 10);
        Assert.Null(bar.ItemContainerGenerator.ContainerFromIndex(0));
        var own = new DevToolsBarSegmentPresenter { Background = a };
        DevToolsProportionalPanel.SetWeight(own, 2);
        bar.ItemsSource = new[] { own };
        Layout(bar, 400, 10);
        bar.ItemsSource = Array.Empty<DevToolsBarSegmentPresenter>();
        Layout(bar, 400, 10);
        Assert.Same(a, own.Background);
        Assert.Equal(2, DevToolsProportionalPanel.GetWeight(own));
    }

    [Theory]
    [InlineData(DevToolsButtonVariant.Default, "Normal")]
    [InlineData(DevToolsButtonVariant.Default, "Hover")]
    [InlineData(DevToolsButtonVariant.Default, "Active")]
    [InlineData(DevToolsButtonVariant.Primary, "Primary")]
    [InlineData(DevToolsButtonVariant.Primary, "PrimaryPressed")]
    [InlineData(DevToolsButtonVariant.Danger, "Danger")]
    [InlineData(DevToolsButtonVariant.Danger, "DangerPressed")]
    public void ActionTextKeepsReadableContrastInSemanticStates(DevToolsButtonVariant variant, string state)
    {
        var button = new DevToolsActionButton { Label = "Action", Variant = variant };
        Layout(button);
        Assert.True(VisualStateManager.GoToState(button, state, false));
        Color foreground = ColorOf(Part<TextBlock>(button, "PART_Label").Foreground);
        Color background = ColorOf(Part<Border>(button, "PART_Surface").Background);
        // The active state uses a translucent amber layer over the control surface.
        if (background.A < 255)
        {
            var surface = Color.FromRgb(0x21, 0x26, 0x2F);
            double alpha = background.A / 255.0;
            background = Color.FromRgb((byte)(background.R * alpha + surface.R * (1-alpha)),
                (byte)(background.G * alpha + surface.G * (1-alpha)), (byte)(background.B * alpha + surface.B * (1-alpha)));
        }
        double a = Luminance(foreground), b = Luminance(background);
        Assert.True((Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05) >= 4.5, $"Unreadable {state} text");
    }

    [Fact]
    public void WorkspaceSelectionIsBindableAndOldTabsDetachWhenTemplateChanges()
    {
        var source = new PageSource();
        var inspector = new TextBlock { Text = "Inspector body" };
        var workspace = new DevToolsWorkspace { DataContext = source, InspectorContent = inspector, PerformanceContent = "Performance body" };
        workspace.SetBinding(DevToolsWorkspace.SelectedPageProperty, new Binding(nameof(PageSource.Page)) { Mode = BindingMode.TwoWay });
        Layout(workspace, 1040, 600);
        var originalTemplate = workspace.Template;
        var tabs = Part<TabControl>(workspace, "PART_Tabs");
        Assert.Equal(9, tabs.Items.Count);
        Assert.Same(inspector, Assert.IsType<TabItem>(tabs.Items[0]).Content);
        Assert.Equal(Color.FromRgb(0xDD, 0xE2, 0xE9), ColorOf(Assert.IsType<TabItem>(tabs.Items[0]).Foreground));
        source.Page = DevToolsPage.Tools;
        Assert.Equal(7, tabs.SelectedIndex);
        var changes = new List<DevToolsPageChangedEventArgs>();
        workspace.SelectedPageChanged += (_, e) => changes.Add(Assert.IsType<DevToolsPageChangedEventArgs>(e));
        tabs.SelectedIndex = 5;
        Assert.Equal(DevToolsPage.Performance, workspace.SelectedPage);
        Assert.Equal(DevToolsPage.Performance, source.Page);
        Assert.Single(changes);
        Assert.Equal(DevToolsPage.Tools, changes[0].OldPage);
        workspace.Template = new ControlTemplate(typeof(DevToolsWorkspace));
        workspace.Template.SetVisualTree(static () => new ContentPresenter());
        Layout(workspace);
        tabs.SelectedIndex = 3;
        Assert.Equal(DevToolsPage.Performance, workspace.SelectedPage);
        Assert.NotNull(workspace.GetBindingExpression(DevToolsWorkspace.SelectedPageProperty));
        source.Page = DevToolsPage.Events;
        Assert.Equal(DevToolsPage.Events, workspace.SelectedPage);
        Assert.Same(inspector, workspace.InspectorContent);
        workspace.Template = originalTemplate;
        Layout(workspace, 1040, 600);
        var replacementTabs = Part<TabControl>(workspace, "PART_Tabs");
        Assert.NotSame(tabs, replacementTabs);
        Assert.Equal(2, replacementTabs.SelectedIndex);
        Assert.Same(inspector, Assert.IsType<TabItem>(replacementTabs.Items[0]).Content);
    }

    [Fact]
    public void WorkspaceAutomationExposesOnlyCurrentPageContentAndRefreshesAfterChanges()
    {
        var workspace = new DevToolsWorkspace
        {
            InspectorContent = new DevToolsActionButton { Label = "Refresh" },
            PerformanceContent = new DevToolsSurface { Content = new DevToolsActionButton { Label = "Start capture" } },
        };
        Layout(workspace, 1040, 600);
        var tabs = Part<TabControl>(workspace, "PART_Tabs");
        var peer = Assert.IsType<TabControlAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(tabs));
        var children = peer.GetChildren();
        Assert.Equal(9, children.Count);
        var first = Assert.IsType<TabItemAutomationPeer>(children[0]);
        var perf = Assert.IsType<TabItemAutomationPeer>(children[5]);
        var realizedPerf = Assert.IsType<TabItemAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(Assert.IsType<TabItem>(tabs.Items[5])));
        Assert.Empty(realizedPerf.GetChildren());
        Assert.Contains(first.GetChildren(), child => child.GetName() == "Refresh");
        Assert.Empty(perf.GetChildren());
        Assert.Same(first, Assert.Single(peer.GetSelection()));
        perf.Select();
        _ = perf.GetChildren(); // Native bridges may ask before the new page is realized.
        _ = realizedPerf.GetChildren();
        Layout(workspace, 1040, 600);
        typeof(Jalium.UI.Threading.Dispatcher).GetMethod("ProcessQueue", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(workspace.Dispatcher, null);
        Assert.Equal(DevToolsPage.Performance, workspace.SelectedPage);
        Assert.Empty(first.GetChildren());
        Assert.Contains(Descendants(perf), child => child.GetName() == "Start capture");
        Assert.Contains(Descendants(realizedPerf), child => child.GetName() == "Start capture");
        Assert.Same(perf, Assert.Single(peer.GetSelection()));
        workspace.PerformanceContent = new DevToolsActionButton { Label = "Reset capture" };
        Layout(workspace, 1040, 600);
        Assert.Contains(Descendants(perf), child => child.GetName() == "Reset capture");
        Assert.DoesNotContain(Descendants(perf), child => child.GetName() == "Start capture");
        var dataTabs = new TabControl { ItemsSource = new[] { "One", "Two" } };
        Layout(dataTabs, 400, 200);
        var dataPeer = Assert.IsType<TabControlAutomationPeer>(UIElementAutomationPeer.CreatePeerForElement(dataTabs));
        var dataTab = Assert.IsType<TabItemAutomationPeer>(dataPeer.GetChildren()[1]);
        Assert.Equal("Two", dataTab.GetName());
        dataTab.Select();
        Assert.True(dataTab.IsSelected);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value) { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055)/1.055, 2.4); }
        return Channel(color.R)*0.2126 + Channel(color.G)*0.7152 + Channel(color.B)*0.0722;
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        var pending = new Stack<AutomationPeer>(peer.GetChildren());
        var seen = new HashSet<AutomationPeer>();
        while (pending.TryPop(out var child))
        {
            if (!seen.Add(child)) continue;
            yield return child;
            foreach (var nested in child.GetChildren()) pending.Push(nested);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredTemplateConstructionPreservesDiagnosticExclusion(bool excluded)
    {
        var control = new Control();
        bool? ignoredDuringConstruction = null;
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() =>
        {
            var child = new Border();
            ignoredDuringConstruction = DiagnosticsScope.ShouldIgnore(child);
            return child;
        });
        if (excluded) DiagnosticsScope.ExcludeRoot(control);
        try
        {
            control.Template = template;
            Layout(control);
            Assert.Equal(excluded, ignoredDuringConstruction);
            Assert.False(DiagnosticsScope.IsInIgnoredCreationScope);
        }
        finally { if (excluded) DiagnosticsScope.IncludeRoot(control); }
    }

    [Fact]
    public void TreeRowOwnsNoVisualsBeforeItsThemeAndUpdatesIndentAndChevron()
    {
        var row = new DevToolsTreeItem { Content = "Window", Level = 3, HasChildren = true };
        Assert.Equal(0, row.VisualChildrenCount);
        Layout(row, 400, 28);
        Assert.Equal(42, row.IndentWidth);
        var arrow = Part<Jalium.UI.Shapes.Path>(row, "PART_Chevron");
        Assert.Equal(-90, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
        row.IsExpanded = true;
        Assert.Equal(0, Assert.IsType<RotateTransform>(arrow.RenderTransform).Angle);
        row.Level = -4;
        Assert.Equal(0, row.IndentWidth);
        row.IndentSize = double.PositiveInfinity;
        Assert.Equal(0, row.IndentSize);
        row.HasChildren = false;
        Assert.Equal(Visibility.Hidden, Part<Button>(row, "PART_Expander").Visibility);
        row.IsSelected = true;
        Assert.Equal(Visibility.Visible, Part<Border>(row, "PART_Selection").Visibility);
    }

    [Fact]
    public void TreeRowRevealOnlyMovesCompositionAndSurvivesTemplateReplacement()
    {
        var row = new DevToolsTreeItem { Content = "Nested item", RevealProgress = 0.5 };
        Layout(row, 400, 28);
        var content = Part<Grid>(row, "PART_RevealContent");
        var before = row.DesiredSize;
        Assert.Equal(-14, Assert.IsType<TranslateTransform>(content.RenderTransform).Y, 3);
        row.Measure(new Size(400, 28));
        Assert.Equal(before, row.DesiredSize);
        row.RevealProgress = 0;
        Assert.Equal(-28, Assert.IsType<TranslateTransform>(content.RenderTransform).Y, 3);
        row.RevealProgress = 1;
        Assert.Null(content.RenderTransform);
        row.RevealProgress = 0.5;
        var original = row.Template;
        var replacement = new ControlTemplate(typeof(DevToolsTreeItem));
        replacement.SetVisualTree(static () => new Border());
        row.Template = replacement;
        Layout(row, 400, 28);
        Assert.Null(content.RenderTransform);
        row.Template = original;
        Layout(row, 400, 40);
        Assert.Equal(-20, Assert.IsType<TranslateTransform>(Part<Grid>(row, "PART_RevealContent").RenderTransform).Y, 3);
        row.RevealProgress = double.NaN;
        Assert.Equal(1, row.RevealProgress);
    }

    [Fact]
    public void TreeRowExpansionUsesKeyboardAutomationAndReleasesRetiredParts()
    {
        var row = new DevToolsTreeItem { Content = "Root", HasChildren = true };
        int changes = 0;
        row.ExpansionChanged += (_, _) => changes++;
        Layout(row);
        var expander = Part<Button>(row, "PART_Expander");
        expander.PerformClick();
        Assert.True(row.IsExpanded);
        SendKey(row, Key.Left, UIElement.PreviewKeyDownEvent);
        Assert.False(row.IsExpanded);
        var peer = UIElementAutomationPeer.CreatePeerForElement(row)!;
        Assert.Equal(AutomationControlType.TreeItem, peer.GetAutomationControlType());
        Assert.Equal("Root", peer.GetName());
        var expand = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
        expand.Expand();
        Assert.True(row.IsExpanded);
        Assert.Equal(3, changes);
        row.IsEnabled = false;
        Assert.Throws<InvalidOperationException>(expand.Collapse);
        row.Template = new ControlTemplate(typeof(DevToolsTreeItem));
        row.Template.SetVisualTree(static () => new Border());
        Layout(row);
        row.IsEnabled = true;
        expander.PerformClick();
        Assert.True(row.IsExpanded);
        row.HasChildren = false;
        Assert.Equal(ExpandCollapseState.LeafNode, expand.ExpandCollapseState);
        Assert.Throws<InvalidOperationException>(expand.Expand);
    }

    [Fact]
    public void InspectorBindsCommandsSearchAndViewWithoutWindowPartReferences()
    {
        var command = new CountingCommand();
        var inspector = new DevToolsInspector { SearchText = "Button", RefreshCommand = command, TreeContent = "Tree", DetailsContent = "Details" };
        int searchChanges = 0, viewChanges = 0;
        inspector.SearchTextChanged += (_, _) => searchChanges++;
        inspector.SelectedViewChanged += (_, _) => viewChanges++;
        Layout(inspector, 1040, 760);
        var search = Part<TextBox>(inspector, "PART_SearchBox");
        var selector = Part<DevToolsSegmentedSelector>(inspector, "PART_ViewSelector");
        Assert.Equal("Button", search.Text);
        search.Text = "TextBox";
        Assert.Equal("TextBox", inspector.SearchText);
        Assert.Equal(1, searchChanges);
        Assert.Equal(1, selector.SelectedIndex);
        selector.SelectedIndex = 2;
        Assert.Equal(DevToolsInspectorView.Logical, inspector.SelectedView);
        inspector.SelectedView = DevToolsInspectorView.Flat;
        Assert.Equal(0, selector.SelectedIndex);
        Assert.Equal(2, viewChanges);
        Part<DevToolsActionButton>(inspector, "PART_Refresh").PerformClick();
        Assert.Equal(1, command.Count);
        Assert.False(Part<DevToolsActionButton>(inspector, "PART_Delete").IsEnabled);
        inspector.CanDelete = true;
        inspector.IsPicking = true;
        Assert.True(Part<DevToolsActionButton>(inspector, "PART_Delete").IsEnabled);
        Assert.True(Part<DevToolsActionButton>(inspector, "PART_Pick").IsActive);
    }

    [Fact]
    public void InspectorRetemplatesSharedContentAndIgnoresOldSelectionEvents()
    {
        var tree = new ListBox();
        var details = new TextBlock { Text = "Properties" };
        var inspector = new DevToolsInspector { TreeContent = tree, DetailsContent = details };
        Layout(inspector, 1040, 760);
        var oldSelector = Part<DevToolsSegmentedSelector>(inspector, "PART_ViewSelector");
        var original = inspector.Template;
        var empty = new ControlTemplate(typeof(DevToolsInspector));
        empty.SetVisualTree(static () => new Border());
        inspector.Template = empty;
        Layout(inspector);
        Assert.False(inspector.FocusSearch());
        oldSelector.SelectedIndex = 0;
        Assert.Equal(DevToolsInspectorView.Visual, inspector.SelectedView);
        inspector.Template = original;
        Layout(inspector, 1040, 760);
        Assert.Same(tree, Part<ContentPresenter>(inspector, "PART_TreeContent").Content);
        Assert.Same(details, Part<ContentPresenter>(inspector, "PART_DetailsContent").Content);
        Assert.Equal(1, Part<DevToolsSegmentedSelector>(inspector, "PART_ViewSelector").SelectedIndex);
    }

    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static Color ColorOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void Layout(UIElement control, double width = 400, double height = 160)
    { control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height)); control.UpdateLayout(); }
    private static void SendKey(UIElement control, Key key, RoutedEvent route) => control.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, new Source { RootVisual = control }, 1, key) { RoutedEvent = route, Source = control });
    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed class CountingCommand : ICommand
    {
        private bool _enabled = true;
        public int Count { get; private set; }
        public object? LastParameter { get; private set; }
        public bool CanExecute(object? parameter) => _enabled;
        public void Execute(object? parameter) { Count++; LastParameter = parameter; }
        public event EventHandler? CanExecuteChanged;
        public void SetEnabled(bool enabled) { _enabled = enabled; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    private sealed class PageSource : INotifyPropertyChanged
    {
        private DevToolsPage _page;
        public DevToolsPage Page { get => _page; set { _page = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Page))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
