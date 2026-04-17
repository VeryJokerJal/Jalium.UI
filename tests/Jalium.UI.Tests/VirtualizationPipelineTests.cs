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

    [Fact]
    public void ListBox_Virtualization_ScrollDownThenUp_ShouldPreserveItemOrder()
    {
        // Regression: after scrolling down then back to the top, recycled
        // containers must show their new item's content — not the content
        // they previously held. The bug was that ItemContainerGenerator
        // pulled containers out of the recycle pool without flagging them
        // for PrepareItemContainer, so .Content stayed stale.
        var listBox = new TestListBox
        {
            Width = 320,
            Height = 240,
        };

        for (var i = 1; i <= 1000; i++)
            listBox.Items.Add($"Person {i}");

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);

        // Verify initial order: top-realized container should be "Person 1".
        AssertFirstRealizedMatchesIndex(host, listBox, expectedIndex: 0);

        // Scroll far enough down to recycle the initial window.
        host.SetVerticalOffset(500);
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        // Scroll back to the top — the containers previously holding 50+/60+
        // items should have been re-prepared with Person 1..N.
        host.SetVerticalOffset(0);
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));

        AssertFirstRealizedMatchesIndex(host, listBox, expectedIndex: 0);

        // And walk the next few realized containers — they must be contiguous.
        for (int i = 0; i < 5; i++)
        {
            var container = listBox.ItemContainerGenerator.ContainerFromIndex(i);
            Assert.NotNull(container);
            var item = listBox.ItemContainerGenerator.ItemFromContainer(container!);
            Assert.Equal($"Person {i + 1}", item);
            if (container is ListBoxItem lbi)
                Assert.Equal($"Person {i + 1}", lbi.Content);
        }
    }

    private static void AssertFirstRealizedMatchesIndex(VirtualizingStackPanel host, TestListBox listBox, int expectedIndex)
    {
        var container = listBox.ItemContainerGenerator.ContainerFromIndex(expectedIndex);
        Assert.NotNull(container);
        var item = listBox.ItemContainerGenerator.ItemFromContainer(container!);
        Assert.Equal($"Person {expectedIndex + 1}", item);

        if (container is ListBoxItem lbi)
            Assert.Equal($"Person {expectedIndex + 1}", lbi.Content);
    }

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }
}
