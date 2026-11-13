using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDockTabRenderingTests
{
    [Fact]
    public void MutableBrushDirtiesCurrentChromeAndReleasesRetiredChrome()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0, 94, 166));
        var panel = new DockTabPanel { TabStripBorderBrush = brush };
        panel.Items.Add(new DockItem { Header = "Document", Content = new Border() });
        panel.Resources.MergedDictionaries.Add(MenuFlyoutTemplateTestHelper.Themes.Value);
        Layout(panel);
        var old = Assert.IsType<DockTabChrome>(panel.FindName("PART_ChromeOverlay"));
        var dirty = typeof(Visual).GetField("_isRenderDirty", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dirty.SetValue(old, false); brush.Color = Color.FromRgb(160, 55, 25);
        Assert.True((bool)dirty.GetValue(old)!);
        var blank = new ControlTemplate(typeof(DockTabPanel)); blank.SetVisualTree(() => new Border());
        panel.Template = blank; Layout(panel);
        dirty.SetValue(old, false); brush.Color = Color.FromRgb(32, 32, 32);
        Assert.False((bool)dirty.GetValue(old)!);
        panel.ClearValue(Control.TemplateProperty); Layout(panel);
        var current = Assert.IsType<DockTabChrome>(panel.FindName("PART_ChromeOverlay"));
        dirty.SetValue(current, false); brush.Color = Color.FromRgb(0, 94, 166);
        Assert.True((bool)dirty.GetValue(current)!);
    }

    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 240));
        element.Arrange(new Rect(0, 0, 400, 240)); element.UpdateLayout();
    }
}
