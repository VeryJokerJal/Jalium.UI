using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class VirtualizingWrapPanelOffsetTests
{
    private static readonly Size Viewport = new(320, 120);

    [Fact]
    public void SetVerticalOffset_UsesArrangeUntilViewportNeedsAnUnrealizedRow()
    {
        var (control, panel) = CreatePanel();
        var first = Assert.IsAssignableFrom<UIElement>(
            control.ItemContainerGenerator.ContainerFromIndex(0));
        var createdBeforeScroll = control.ContainerCreateCount;

        panel.SetVerticalOffset(1);

        Assert.True(panel.IsMeasureValid);
        Assert.False(panel.IsArrangeValid);

        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        Assert.Equal(-1, first.VisualBounds.Y);
        Assert.Equal(createdBeforeScroll, control.ContainerCreateCount);
        Assert.True(panel.IsMeasureValid);
        Assert.True(panel.IsArrangeValid);

        // At 40px the viewport advances into the next row, which was not part of
        // the zero-cache realization window and therefore requires Measure.
        panel.SetVerticalOffset(40);

        Assert.False(panel.IsMeasureValid);
        Assert.False(panel.IsArrangeValid);
    }

    [Fact]
    public void DisjointJump_RecyclesOldWindowBeforeRealizingNewViewport()
    {
        var (control, panel) = CreatePanel();
        var originalContainers = panel.Children.Cast<UIElement>().ToHashSet();
        var createdBeforeJump = control.ContainerCreateCount;

        panel.SetVerticalOffset(800);
        panel.Measure(Viewport);
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        Assert.NotNull(control.ItemContainerGenerator.ContainerFromIndex(80));
        Assert.Equal(createdBeforeJump, control.ContainerCreateCount);
        Assert.Equal(originalContainers.Count, panel.Children.Count);
        Assert.All(panel.Children.Cast<UIElement>(), child => Assert.Contains(child, originalContainers));
    }

    [Fact]
    public void TransientWideMeasure_UsesStableScrollOwnerCrossAxisAndPreservesOffset()
    {
        var (control, panel) = CreatePanel();
        panel.SetVerticalOffset(800);
        panel.Measure(Viewport);
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        var expectedOffset = panel.VerticalOffset;
        var expectedExtent = panel.ExtentHeight;

        var owner = new ScrollViewer
        {
            Width = Viewport.Width,
            Height = Viewport.Height
        };
        owner.Measure(Viewport);
        owner.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));
        panel.ScrollOwner = owner;

        // Grid can briefly offer the full window width during a sibling-column update. The
        // already-established ScrollViewer viewport remains the real wrapping constraint.
        panel.InvalidateMeasure();
        panel.Measure(new Size(800, Viewport.Height));

        Assert.Equal(expectedExtent, panel.ExtentHeight, precision: 3);
        Assert.Equal(expectedOffset, panel.VerticalOffset, precision: 3);
    }

    [Fact]
    public void TransientWideOwnerAndMeasure_WaitsForWideArrangeBeforeReflowing()
    {
        var (control, panel) = CreatePanel();
        panel.SetVerticalOffset(800);
        panel.Measure(Viewport);
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        var narrowExtent = panel.ExtentHeight;
        var narrowOffset = panel.VerticalOffset;
        var wideViewport = new Size(800, Viewport.Height);

        // A parent Grid can briefly measure and arrange the owning ScrollViewer at the full
        // window width before this panel has actually been arranged into that wider viewport.
        // The owner metric is therefore not, by itself, proof that the panel's wrap width changed.
        var owner = new ScrollViewer
        {
            Width = wideViewport.Width,
            Height = wideViewport.Height
        };
        owner.Measure(wideViewport);
        owner.Arrange(new Rect(0, 0, wideViewport.Width, wideViewport.Height));
        panel.ScrollOwner = owner;

        Assert.Equal(wideViewport.Width, owner.ViewportWidth, precision: 3);
        Assert.Equal(Viewport.Width, panel.RenderSize.Width, precision: 3);

        panel.InvalidateMeasure();
        panel.Measure(wideViewport);

        // The last committed panel arrange is still 320px wide (four items per row). Accepting
        // the simultaneous 800px measure/owner probe here would jump to ten items per row,
        // collapse the extent from 1000px to 400px, and irreversibly clamp offset 800 to 280.
        Assert.Equal(narrowExtent, panel.ExtentHeight, precision: 3);
        Assert.Equal(narrowOffset, panel.VerticalOffset, precision: 3);

        // Once the panel itself is genuinely arranged at 800px, its corrective measure may
        // commit the wider ten-column layout and coerce the offset against the new extent.
        panel.Arrange(new Rect(0, 0, wideViewport.Width, wideViewport.Height));
        Assert.False(panel.IsMeasureValid);

        panel.Measure(wideViewport);

        Assert.Equal(400, panel.ExtentHeight, precision: 3);
        Assert.Equal(280, panel.VerticalOffset, precision: 3);
    }

    [Fact]
    public void CommittedResize_WideNarrowWideNarrow_ReflowsInBothDirections()
    {
        var (_, panel) = CreatePanel();
        Assert.Equal(1000, panel.ExtentHeight, precision: 3); // four columns

        ResizePanelUntilSettled(panel, new Size(800, Viewport.Height));
        Assert.Equal(800, panel.RenderSize.Width, precision: 3);
        Assert.Equal(400, panel.ExtentHeight, precision: 3); // ten columns

        ResizePanelUntilSettled(panel, new Size(240, Viewport.Height));
        Assert.Equal(240, panel.RenderSize.Width, precision: 3);
        Assert.Equal(1360, panel.ExtentHeight, precision: 3); // three columns

        ResizePanelUntilSettled(panel, new Size(640, Viewport.Height));
        Assert.Equal(640, panel.RenderSize.Width, precision: 3);
        Assert.Equal(520, panel.ExtentHeight, precision: 3); // eight columns

        ResizePanelUntilSettled(panel, new Size(160, Viewport.Height));
        Assert.Equal(160, panel.RenderSize.Width, precision: 3);
        Assert.Equal(2000, panel.ExtentHeight, precision: 3); // two columns
        Assert.InRange(panel.VerticalOffset, 0, panel.ExtentHeight - panel.ViewportHeight);
    }

    [Fact]
    public void ScrollOwnerShrink_CapsAStaleWideMeasureImmediately()
    {
        var (_, panel) = CreatePanel();
        ResizePanelUntilSettled(panel, new Size(800, Viewport.Height));
        Assert.Equal(400, panel.ExtentHeight, precision: 3); // ten columns

        var owner = new ScrollViewer
        {
            Width = Viewport.Width,
            Height = Viewport.Height
        };
        owner.Measure(Viewport);
        owner.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));
        panel.ScrollOwner = owner;

        // The parent still offers the old 800px constraint for one layout pass, but the
        // ScrollViewer has already committed a 320px viewport. The owner must win downward.
        panel.InvalidateMeasure();
        panel.Measure(new Size(800, Viewport.Height));

        Assert.Equal(Viewport.Width, owner.ViewportWidth, precision: 3);
        Assert.Equal(800, panel.RenderSize.Width, precision: 3);
        Assert.Equal(1000, panel.ExtentHeight, precision: 3); // four columns
        Assert.InRange(panel.VerticalOffset, 0, panel.ExtentHeight - panel.ViewportHeight);
    }

    [Fact]
    public void AutoScrollbarMeasureGutter_CrossingAColumnThreshold_DoesNotLoopLayout()
    {
        var (_, panel) = CreatePanel();
        var owner = new ScrollViewer
        {
            Width = Viewport.Width,
            Height = Viewport.Height
        };
        owner.Measure(Viewport);
        owner.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));
        panel.ScrollOwner = owner;

        // A reserved scrollbar gutter can offer 308px during Measure while the IScrollInfo
        // content is arranged at its 320px viewport. With 80px cells those widths straddle
        // a wrap threshold (three versus four columns); accepting both would invalidate
        // Measure on every Arrange forever.
        panel.InvalidateMeasure();
        panel.Measure(new Size(308, Viewport.Height));
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));
        Assert.False(panel.IsMeasureValid);

        panel.Measure(new Size(308, Viewport.Height));
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        Assert.Equal(1000, panel.ExtentHeight, precision: 3); // four columns
        Assert.True(panel.IsMeasureValid);
        Assert.True(panel.IsArrangeValid);

        // An unrelated subsequent layout pass with the same gutter pair remains stable too.
        panel.InvalidateMeasure();
        panel.Measure(new Size(308, Viewport.Height));
        panel.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));
        Assert.True(panel.IsMeasureValid);

        // A real owner shrink invalidates the old 308->320 correction and must win at once.
        owner.Width = 240;
        owner.Measure(new Size(240, Viewport.Height));
        owner.Arrange(new Rect(0, 0, 240, Viewport.Height));
        panel.InvalidateMeasure();
        panel.Measure(new Size(308, Viewport.Height));
        panel.Arrange(new Rect(0, 0, 240, Viewport.Height));

        Assert.Equal(1360, panel.ExtentHeight, precision: 3); // three columns
        Assert.True(panel.IsMeasureValid);
    }

    [Fact]
    public void ArrangeWidthDifferenceWithinTheSameColumnCount_DoesNotInvalidateMeasure()
    {
        var (_, panel) = CreatePanel();

        panel.InvalidateMeasure();
        panel.Measure(new Size(327, Viewport.Height));
        panel.Arrange(new Rect(0, 0, 327, Viewport.Height));

        Assert.Equal(1000, panel.ExtentHeight, precision: 3); // four columns
        Assert.Equal(327, panel.ExtentWidth, precision: 3);
        Assert.True(panel.IsMeasureValid);
        Assert.True(panel.IsArrangeValid);
    }

    private static (TrackingWrapItemsControl Control, VirtualizingWrapPanel Panel) CreatePanel()
    {
        var control = new TrackingWrapItemsControl
        {
            Width = Viewport.Width,
            Height = Viewport.Height,
            ItemsSource = Enumerable.Range(0, 100).Select(index => $"Item {index}").ToList(),
        };
        VirtualizingPanel.SetCacheLength(control, new VirtualizationCacheLength(0));

        var panel = Assert.IsType<VirtualizingWrapPanel>(control.Host);
        panel.ItemWidth = 80;
        panel.ItemHeight = 40;

        control.Measure(Viewport);
        control.Arrange(new Rect(0, 0, Viewport.Width, Viewport.Height));

        Assert.True(panel.IsMeasureValid);
        Assert.True(panel.IsArrangeValid);
        Assert.NotEmpty(panel.Children);
        return (control, panel);
    }

    private static void ResizePanelUntilSettled(VirtualizingWrapPanel panel, Size size)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            panel.InvalidateMeasure();
            panel.Measure(size);
            panel.Arrange(new Rect(0, 0, size.Width, size.Height));
        }
    }

    private sealed class TrackingWrapItemsControl : ItemsControl
    {
        public TrackingWrapItemsControl()
        {
            ItemsPanel = new ItemsPanelTemplate { PanelType = typeof(VirtualizingWrapPanel) };
        }

        public Panel? Host => ItemsHost;
        public int ContainerCreateCount { get; private set; }

        protected override FrameworkElement GetContainerForItem(object item)
        {
            ContainerCreateCount++;
            return new Border { Width = 80, Height = 40 };
        }
    }
}
