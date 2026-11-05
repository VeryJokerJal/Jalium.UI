using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DockTabPanelTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultTemplateOwnsChromeHeadersContentAndScrollBar()
    {
        var panel = Create(); Layout(panel);
        Assert.Equal(100, panel.MinWidth); Assert.False(panel.HasLocalValue(FrameworkElement.MinWidthProperty));
        Assert.False(panel.HasLocalValue(UIElement.ClipToBoundsProperty));
        Assert.IsType<Border>(panel.GetVisualChild(0));
        var layout = Assert.IsType<DockTabLayoutPanel>(panel.FindName("PART_Layout"));
        Assert.Equal(5, layout.Children.Count);
        var presenter = Assert.IsType<ItemsPresenter>(panel.FindName("PART_ItemsPresenter"));
        var items = Assert.IsType<StackPanel>(presenter.GetVisualChild(0));
        Assert.Equal(Orientation.Horizontal, items.Orientation);
        Assert.Same(panel.Items[0], items.Children[0]);
        var host = Assert.IsType<TransitioningContentControl>(panel.FindName("PART_SelectedContentHost"));
        Assert.Same(((DockItem)panel.Items[0]).Content, host.Content);
        Assert.IsType<DockTabChrome>(panel.FindName("PART_ChromeOverlay"));
        var bar = Assert.IsType<ScrollBar>(panel.FindName("PART_TabStripScrollBar"));
        Assert.True(bar.Maximum > 0); Assert.False(bar.IsTabStop);
    }

    [Theory]
    [InlineData(Dock.Top)]
    [InlineData(Dock.Bottom)]
    [InlineData(Dock.Left)]
    [InlineData(Dock.Right)]
    public void PlacementRetainsSelectionAndAllocatesBothAreas(Dock dock)
    {
        var panel = Create(); panel.TabStripPlacement = dock; Layout(panel);
        panel.SelectedIndex = 11; Layout(panel);
        var host = Assert.IsType<TransitioningContentControl>(panel.FindName("PART_SelectedContentHost"));
        Assert.Same(((DockItem)panel.Items[11]).Content, host.Content);
        Assert.True(host.ActualWidth > 0 && host.ActualHeight > 0);
        var bar = Assert.IsType<ScrollBar>(panel.FindName("PART_TabStripScrollBar"));
        Assert.Equal(dock is Dock.Left or Dock.Right ? Orientation.Vertical : Orientation.Horizontal, bar.Orientation);
        Assert.True(bar.Value > 0);
        Assert.Equal(dock.ToString(), VisualStateManager.GetCurrentStateName(panel, "PlacementStates"));
        Assert.Equal("Overflowing", VisualStateManager.GetCurrentStateName(panel, "OverflowStates"));
    }

    [Fact]
    public void RetiredTemplateReleasesContentAndCannotStealItBack()
    {
        var panel = Create(); Layout(panel);
        var oldHost = Assert.IsType<TransitioningContentControl>(panel.FindName("PART_SelectedContentHost"));
        var oldItems = Assert.IsType<ItemsPresenter>(panel.FindName("PART_ItemsPresenter"));
        var selected = panel.SelectedContent;
        var blank = new ControlTemplate(typeof(DockTabPanel)); blank.SetVisualTree(() => new Border());
        panel.Template = blank; Layout(panel);
        Assert.Null(oldHost.Content); Assert.Same(selected, panel.SelectedContent);
        oldItems.InvalidateMeasure(); oldItems.Measure(new Size(500, 300));
        Assert.Equal(default(Size), oldItems.DesiredSize);
        panel.ClearValue(Control.TemplateProperty); Layout(panel);
        var restored = Assert.IsType<TransitioningContentControl>(panel.FindName("PART_SelectedContentHost"));
        Assert.NotSame(oldHost, restored); Assert.Same(selected, restored.Content);
        Assert.Equal(12, Assert.IsAssignableFrom<Panel>(Assert.IsType<ItemsPresenter>(panel.FindName("PART_ItemsPresenter")).GetVisualChild(0)).Children.Count);
    }

    [Fact]
    public void CssInsetsAllocateTheActualDockLayoutArea()
    {
        var panel = Create(); Css.SetStyle(panel, "border:3px solid lime;padding:12px"); Layout(panel);
        var layout = Assert.IsType<DockTabLayoutPanel>(panel.FindName("PART_Layout"));
        Assert.Equal(new Point(15, 15), layout.TransformToAncestor(panel));
        Assert.Equal(new Size(370, 210), layout.RenderSize);
        var rect = (Rect)typeof(DockTabPanel).GetMethod("GetTabStripInteractionRect", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null)!;
        Assert.Equal(15, rect.X); Assert.Equal(15, rect.Y); Assert.Equal(370, rect.Width);
    }

    [Fact]
    public void OptionalScrollPartKeepsCallerBindingAndRetiredInputIsInert()
    {
        var panel = Create(); var source = new ProgressBar(); ProbeBar? old = null;
        var template = new ControlTemplate(typeof(DockTabPanel));
        template.SetVisualTree(() =>
        {
            var layout = new DockTabLayoutPanel { Name = "PART_Layout" };
            layout.Children.Add(new ItemsPresenter { Name = "PART_ItemsPresenter" });
            old = new ProbeBar { Name = "PART_TabStripScrollBar" };
            BindingOperations.SetBinding(old, RangeBase.ValueProperty,
                new Binding(nameof(ProgressBar.Value)) { Source = source, Mode = BindingMode.TwoWay });
            layout.Children.Add(old); return layout;
        });
        panel.Template = template; Layout(panel);
        old!.PressRight(); Assert.True(old.Value > 0);
        Assert.NotNull(BindingOperations.GetBindingExpression(old, RangeBase.ValueProperty));
        var offset = Offset(panel);
        var blank = new ControlTemplate(typeof(DockTabPanel)); blank.SetVisualTree(() => new Border());
        panel.Template = blank; Layout(panel);
        old.PressRight(); Assert.Equal(offset, Offset(panel));
    }

    [Fact]
    public void CallerScrollBarThicknessControlsItsActualAllocation()
    {
        var panel = Create(); Layout(panel);
        var bar = Assert.IsType<ScrollBar>(panel.FindName("PART_TabStripScrollBar"));
        bar.Height = 18; Layout(panel); Assert.Equal(18, bar.RenderSize.Height);
        panel.TabStripPlacement = Dock.Left; panel.TabStripHeight = 120;
        bar.ClearValue(FrameworkElement.HeightProperty); bar.Width = 24; Layout(panel);
        Assert.Equal(24, bar.RenderSize.Width);
    }

    [Fact]
    public void ContentTransferReleasesThePreviousTemplateHost()
    {
        var first = Create(); Layout(first);
        var shared = first.SelectedContent;
        var oldHost = Assert.IsType<TransitioningContentControl>(first.FindName("PART_SelectedContentHost"));
        var second = new DockTabPanel(); second.Resources.MergedDictionaries.Add(Defaults.Value);
        second.Items.Add(new DockItem { Header = "Transferred", Content = shared }); Layout(second);
        Assert.Null(oldHost.Content);
        Assert.Same(shared, Assert.IsType<TransitioningContentControl>(second.FindName("PART_SelectedContentHost")).Content);
    }

    [Fact]
    public void PlacementAndNarrowingKeepPreviouslyVisibleSelectionInView()
    {
        var panel = Create(); Layout(panel); panel.SelectedIndex = 11; Layout(panel);
        panel.TabStripPlacement = Dock.Bottom; Layout(panel);
        Assert.True(Offset(panel) > 0);
        var selected = (DockItem)panel.Items[11];
        panel.Measure(new Size(240, 240)); panel.Arrange(new Rect(0, 0, 240, 240)); panel.UpdateLayout();
        var position = selected.TransformToAncestor(panel);
        Assert.InRange(position.X, -0.5, 240);
        Assert.True(position.X + selected.ActualWidth <= 240.5);
        // Moving the selection out of view manually remains a deliberate choice.
        typeof(DockTabPanel).GetMethod("SetTabStripScrollOffsetInternal", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, [0d, true]);
        panel.Measure(new Size(220, 240)); panel.Arrange(new Rect(0, 0, 220, 240)); panel.UpdateLayout();
        Assert.Equal(0, Offset(panel));
    }

    private static double Offset(DockTabPanel panel) => (double)typeof(DockTabPanel)
        .GetField("_tabStripScrollOffset", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;

    private static DockTabPanel Create()
    {
        var panel = new DockTabPanel(); panel.Resources.MergedDictionaries.Add(Defaults.Value);
        for (var i = 0; i < 12; i++) panel.Items.Add(new DockItem { Header = $"Document {i} Long Header", Content = new Border() });
        return panel;
    }

    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 240));
        element.Arrange(new Rect(0, 0, 400, 240)); element.UpdateLayout();
    }

    private sealed class ProbeBar : ScrollBar
    {
        public void PressRight() => OnKeyDown(new KeyEventArgs(Keyboard.PrimaryDevice,
            new Source { RootVisual = this }, Environment.TickCount, Key.Right));
    }

    private sealed class Source : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        protected override CompositionTarget? GetCompositionTargetCore() => null;
        public override bool IsDisposed => false;
    }
}
