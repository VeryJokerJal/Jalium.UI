using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DockTabPanelLayoutTests
{
    [Fact]
    public void HorizontalOverflow_ShouldShowScrollBar_AndAutoScrollToSelectedTab()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();

        for (int i = 0; i < 12; i++)
        {
            panel.Items.Add(new DockItem
            {
                Header = $"Tab {i} Long Header",
                Content = new ProbeElement()
            });
        }

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(300, 220));

        Assert.True(panel.IsTabStripScrollableForTesting);
        Assert.Equal(0, panel.TabStripScrollOffsetForTesting);

        panel.SelectedIndex = panel.Items.Count - 1;
        host.UpdateLayoutPass(new Size(300, 220));

        Assert.True(panel.TabStripScrollOffsetForTesting > 0);
        Assert.True(panel.TabStripScrollBarRectForTesting.Width > 0);
    }

    [Fact]
    public void HorizontalOverflow_ManualScroll_ShouldNotResetToSelectedTabOnLayout()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel();

        for (int i = 0; i < 12; i++)
        {
            panel.Items.Add(new DockItem
            {
                Header = $"Tab {i} Long Header",
                Content = new ProbeElement()
            });
        }

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(300, 220));

        Assert.True(panel.IsTabStripScrollableForTesting);
        Assert.Equal(0, panel.TabStripScrollOffsetForTesting);

        panel.SetTabStripScrollOffsetForTesting(120);
        host.UpdateLayoutPass(new Size(300, 220));

        Assert.True(panel.TabStripScrollOffsetForTesting >= 119);
    }

    [Fact]
    public void VerticalLeftPlacement_ShouldLayoutContentOnRightSide()
    {
        var host = new LayoutHostPanel();
        var panel = new DockTabPanel
        {
            TabStripPlacement = Dock.Left,
            TabStripHeight = 96
        };

        var content = new ProbeElement();
        panel.Items.Add(new DockItem
        {
            Header = "Explorer",
            Content = content
        });

        host.Children.Add(panel);
        host.UpdateLayoutPass(new Size(520, 320));

        var contentPos = content.TransformToAncestor(panel);
        Assert.True(panel.IsVerticalTabStrip);
        Assert.True(panel.TabHeadersViewportRect.Width >= 95);
        Assert.True(contentPos.X >= 95);
        Assert.True(content.ActualWidth <= 425);
        Assert.True(content.ActualHeight <= 320);
    }

    [Fact]
    public void DockItem_HeaderLayout_ShouldUseNaturalWidth_WhenMaxWidthIsUnset()
    {
        TextMeasurement.ClearCache();
        RenderContext.Current?.Dispose();
        RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);

        try
        {
            var item = new DockItem
            {
                Header = "MainWindow.jalxaml.cs"
            };

            var text = item.CreateHeaderTextLayoutForTesting(120, 28);

            Assert.NotNull(text);
            Assert.Equal(1, text!.LineCount);
            Assert.Equal("MainWindow.jalxaml.cs", text.Text);
        }
        finally
        {
            TextMeasurement.ClearCache();
            RenderContext.Current?.Dispose();
        }
    }

    [Fact]
    public void DockItem_HeaderLayout_ShouldTrim_WhenMaxWidthIsSet()
    {
        TextMeasurement.ClearCache();
        RenderContext.Current?.Dispose();
        RenderContext.GetOrCreateCurrent(RenderBackend.D3D12, forceReplace: true);

        try
        {
            var item = new DockItem
            {
                Header = "MainWindow.jalxaml.cs",
                MaxWidth = 120
            };

            var text = item.CreateHeaderTextLayoutForTesting(120, 28);

            Assert.NotNull(text);
            Assert.Equal(1, text!.LineCount);
            Assert.EndsWith("...", text.Text, StringComparison.Ordinal);
        }
        finally
        {
            TextMeasurement.ClearCache();
            RenderContext.Current?.Dispose();
        }
    }

    private sealed class LayoutHostPanel : Panel, ILayoutManagerHost
    {
        private readonly LayoutManager _layoutManager = new();

        LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

        public void UpdateLayoutPass(Size availableSize)
        {
            _layoutManager.UpdateLayout(this, availableSize);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Measure(availableSize);
            }

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            foreach (var child in Children)
            {
                if (child.Visibility != Visibility.Collapsed)
                    child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
            }

            return finalSize;
        }
    }

    private sealed class ProbeElement : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? 100 : Math.Max(0, availableSize.Width);
            var height = double.IsInfinity(availableSize.Height) ? 24 : Math.Max(0, Math.Min(availableSize.Height, 24));
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            return finalSize;
        }
    }
}
