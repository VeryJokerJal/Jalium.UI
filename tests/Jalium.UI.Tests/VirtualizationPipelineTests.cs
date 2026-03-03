using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class VirtualizationPipelineTests
{
    [Fact]
    public void VirtualizingPanel_Defaults_ShouldMatchWpfLikeSettings()
    {
        var panel = new VirtualizingStackPanel();

        Assert.True(VirtualizingPanel.GetIsVirtualizing(panel));
        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(panel));

        var cacheLength = VirtualizingPanel.GetCacheLength(panel);
        Assert.Equal(1.0, cacheLength.CacheBeforeViewport);
        Assert.Equal(1.0, cacheLength.CacheAfterViewport);
    }

    [Fact]
    public void ListBox_Virtualization_ShouldRealizeVisibleRangeOnly()
    {
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 240
        };

        for (var i = 0; i < 10_000; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        Assert.True(host.Children.Count < 1000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(5000));
    }

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }
}
