using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.MillionScroll;

namespace Jalium.UI.Sample.Tests;

public sealed class MillionScrollTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void MillionRowsRemainVirtualizedAndUseTheCompiledRowTemplate()
    {
        var surface = Create(); Layout(surface);
        var list = Assert.IsType<MillionScrollListBox>(surface.FindName("PART_List"));
        Assert.Equal(1_000_000, list.Items.Count);
        Assert.NotNull(list.Host); Assert.InRange(list.Host!.Children.Count, 1, 200);
        Assert.NotNull(list.ItemTemplate);
        Assert.Equal(1_000_000, Assert.IsType<MillionRow>(surface.RowsSource[999_999]).RowNumber);
        Assert.Equal(1_000_000d, Assert.IsType<NumberBox>(surface.FindName("PART_JumpRow")).Maximum);
        surface.JumpToIndex(999_999); Layout(surface);
        Assert.Contains(list.Host!.Children.Cast<DependencyObject>(), child => list.ItemContainerGenerator.IndexFromContainer(child) == 999_999);
    }
    [Fact]
    public void TemplateReplacementRetainsSourceSelectionAndDisconnectsActions()
    {
        var surface = Create(); Layout(surface);
        surface.RequestedRows = 200; surface.ApplyRows(); Layout(surface);
        var rows = surface.RowsSource; var list = Assert.IsType<MillionScrollListBox>(surface.FindName("PART_List")); list.SelectedIndex = 123;
        surface.JumpToIndex(123); Layout(surface);
        var retainedOffset = Assert.IsType<VirtualizingStackPanel>(list.Host).VerticalOffset;
        Assert.True(retainedOffset > 0);
        surface.ScrollSpeed = 80; Assert.Equal("80 px/tick", surface.SpeedText);
        var old = Assert.IsType<Button>(surface.FindName("PART_ApplyRows"));
        var blank = new ControlTemplate(typeof(MillionScrollSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.RequestedRows = 321; old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.Equal(200, surface.RowCount);
        surface.ClearValue(Control.TemplateProperty); Layout(surface, 480);
        Assert.Same(rows, surface.RowsSource); Assert.Equal(123, surface.SelectedRowIndex);
        Assert.Equal(123, Assert.IsType<MillionScrollListBox>(surface.FindName("PART_List")).SelectedIndex);
        Assert.Equal(retainedOffset, Assert.IsType<VirtualizingStackPanel>(Assert.IsType<MillionScrollListBox>(surface.FindName("PART_List")).Host).VerticalOffset, 1);
        Assert.Equal("Compact", VisualStateManager.GetCurrentStateName(surface, "LayoutStates"));
        var current = Assert.IsType<Button>(surface.FindName("PART_ApplyRows")); current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, current));
        Assert.Equal(321, surface.RowsSource.Count);
    }
    [Fact]
    public void RunOptionsConfigureRowsSpeedAndActualBackendLabel()
    {
        var surface = Create();
        var options = MillionScrollRunOptions.Parse(["--rows=5000", "--benchmark-seconds=2", "--warmup-seconds=0", "--speed=120"], 1_000_000);
        surface.Configure(options, RenderBackend.Metal, RenderingEngine.Impeller); Layout(surface);
        Assert.Equal(5000, surface.RowCount); Assert.Equal(5000, surface.RowsSource.Count);
        Assert.Equal(120, surface.ScrollSpeed); Assert.Equal("Metal / Impeller", surface.BackendText);
        Assert.Equal(5000, surface.JumpRow);
        surface.ToggleAutoScroll(); Assert.True(surface.IsAutoScrolling); Assert.Equal("Stop auto scroll", surface.AutoButtonText);
        surface.ToggleAutoScroll(); Assert.False(surface.IsAutoScrolling);
        surface.JumpRow = 5000; surface.RequestedRows = 1000; surface.ApplyRows(); Layout(surface);
        Assert.Equal(1000, surface.JumpRow);
        var jump = Assert.IsType<NumberBox>(surface.FindName("PART_JumpRow"));
        Assert.Equal(1000d, jump.Maximum); Assert.Equal(1000d, jump.Value);
        Assert.Equal("1000", jump.Text);
    }
    private static MillionScrollSurface Create()
    {
        var surface = new MillionScrollSurface(); surface.Resources.MergedDictionaries.Add(Defaults.Value); return surface;
    }
    private static void Layout(FrameworkElement element, double width = 1280)
    {
        element.InvalidateMeasure(); element.Measure(new Size(width, 860)); element.Arrange(new Rect(0, 0, width, 860)); element.UpdateLayout();
    }
}
