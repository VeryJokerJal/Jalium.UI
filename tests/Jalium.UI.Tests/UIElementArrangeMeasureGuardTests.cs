using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class UIElementArrangeMeasureGuardTests
{
    private sealed class RecordingElement : FrameworkElement
    {
        public List<Size> MeasureConstraints { get; } = new();
        public int ArrangeOverrideCount { get; private set; }
        public Size DesiredResult { get; set; } = new Size(30, 20);

        protected override Size MeasureOverride(Size availableSize)
        {
            MeasureConstraints.Add(availableSize);
            return DesiredResult;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ArrangeOverrideCount++;
            return base.ArrangeOverride(finalSize);
        }
    }

    [Fact]
    public void Arrange_ShouldMeasureWithSlotSize_WhenNeverMeasured()
    {
        // WPF contract: a parent may Arrange a child it never measured; the child then
        // measures itself with the slot size ("Measure+Arrange at the same size" means
        // "size to the slot") instead of arranging with a stale DesiredSize of (0,0).
        var element = new RecordingElement();

        element.Arrange(new Rect(0, 0, 100, 50));

        var constraint = Assert.Single(element.MeasureConstraints);
        Assert.Equal(100, constraint.Width);
        Assert.Equal(50, constraint.Height);
        Assert.True(element.IsMeasureValid);
        Assert.Equal(30, element.DesiredSize.Width);
        Assert.Equal(20, element.DesiredSize.Height);
        // Default alignment is Stretch, so the element fills its slot.
        Assert.Equal(100, element.RenderSize.Width);
        Assert.Equal(50, element.RenderSize.Height);
    }

    [Fact]
    public void Arrange_ShouldRemeasureWithPreviousConstraint_WhenMeasureInvalidated()
    {
        // Mid-pass InvalidateMeasure (after the LayoutManager measure queue already
        // drained) must not let the element arrange with a stale DesiredSize: Arrange
        // re-runs Measure first, with the LAST REAL constraint — not the arrange rect.
        var element = new RecordingElement();
        element.Measure(new Size(200, 100));
        element.Arrange(new Rect(0, 0, 80, 40));
        element.MeasureConstraints.Clear();
        element.DesiredResult = new Size(50, 25);

        element.InvalidateMeasure();
        element.Arrange(new Rect(0, 0, 80, 40));

        var constraint = Assert.Single(element.MeasureConstraints);
        Assert.Equal(200, constraint.Width);
        Assert.Equal(100, constraint.Height);
        Assert.Equal(50, element.DesiredSize.Width);
        Assert.Equal(25, element.DesiredSize.Height);
        // The arrange itself still ran (same rect, but arrange was invalidated
        // together with measure).
        Assert.Equal(2, element.ArrangeOverrideCount);
        Assert.True(element.IsMeasureValid);
        Assert.True(element.IsArrangeValid);
    }

    [Fact]
    public void Arrange_ShouldNotRemeasure_WhenMeasureValid()
    {
        // The guard must stay out of the hot path: a valid measure is never re-run.
        var element = new RecordingElement();
        element.Measure(new Size(200, 100));
        element.MeasureConstraints.Clear();

        element.Arrange(new Rect(0, 0, 80, 40));

        Assert.Empty(element.MeasureConstraints);
        Assert.Equal(1, element.ArrangeOverrideCount);
    }

    [Fact]
    public void Arrange_ShouldKeepCollapsedShortCircuit_WithoutMeasuring()
    {
        // Collapsed elements skip arrange entirely; the guard must not force-measure them.
        var element = new RecordingElement { Visibility = Visibility.Collapsed };

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.Empty(element.MeasureConstraints);
        Assert.Equal(0, element.ArrangeOverrideCount);
        Assert.True(element.IsArrangeValid);
        Assert.Equal(0, element.RenderSize.Width);
        Assert.Equal(0, element.RenderSize.Height);
    }

    [Fact]
    public void Arrange_ShouldRemeasureWithLatestConstraint_AfterMeasuredWhileCollapsed()
    {
        // The ScrollViewer scroll-bar shape. A bar measured at (16,600) while visible goes
        // Collapsed when the content stops overflowing; the window then shrinks, so the
        // parent measures it at (16,300) — which hits Measure's Collapsed short-circuit —
        // and finally the content overflows again, flipping the bar back to Visible and
        // arranging it. The guard re-measures here, and it must use the constraint the
        // parent last offered (16,300), not the (16,600) the bar last really measured at:
        // otherwise every Collapsed->Visible transition measures the whole template subtree
        // against a height that no longer exists.
        var element = new RecordingElement();

        element.Measure(new Size(16, 600));
        element.Visibility = Visibility.Collapsed;
        element.Measure(new Size(16, 300));
        element.MeasureConstraints.Clear();

        element.Visibility = Visibility.Visible;
        element.Arrange(new Rect(0, 0, 16, 300));

        var constraint = Assert.Single(element.MeasureConstraints);
        Assert.Equal(16, constraint.Width);
        Assert.Equal(300, constraint.Height);
    }

    [Fact]
    public void Arrange_ShouldMeasureWithSlotSize_WhenOnlyEverMeasuredWhileCollapsed()
    {
        // The one state that makes _neverMeasured worth its own field. Measure()'s
        // Collapsed short-circuit marks measure valid WITHOUT running MeasureCore, so it
        // leaves _neverMeasured set and _previousAvailableSize at (0,0). Flipping back to
        // Visible invalidates measure, and the arrange that follows must fall back to the
        // slot size: re-measuring with the recorded (0,0) would yield DesiredSize (0,0)
        // and hand ArrangeCore the very desired-minus-margin underflow this guard exists
        // to prevent.
        var element = new RecordingElement { Visibility = Visibility.Collapsed };
        element.Measure(new Size(200, 100));
        Assert.Empty(element.MeasureConstraints); // Collapsed short-circuit: MeasureCore never ran.

        element.Visibility = Visibility.Visible;
        element.Arrange(new Rect(0, 0, 100, 50));

        var constraint = Assert.Single(element.MeasureConstraints);
        Assert.Equal(100, constraint.Width);
        Assert.Equal(50, constraint.Height);
        Assert.Equal(30, element.DesiredSize.Width);
        Assert.Equal(20, element.DesiredSize.Height);
    }

    [Fact]
    public void Arrange_ShouldProduceRealLayout_WhenSubtreeArrangesBeforeMeasure()
    {
        // The theme-switch shape: OnTemplateChanged applies a template outside the
        // measure pass, so a subtree reaches Arrange with DesiredSize (0,0). The
        // ArrangeCore clamp merely stops the negative-Size crash (render size collapses
        // to 0); the guard should instead produce the real layout on the first arrange.
        var child = new Border { Width = 10, Height = 10 };
        var element = new Border
        {
            Margin = new Thickness(9, 0, 9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = child,
        };

        element.Arrange(new Rect(0, 0, 100, 50));

        Assert.True(element.IsMeasureValid);
        Assert.Equal(28, element.DesiredSize.Width); // 10 content + 9+9 margin
        Assert.Equal(10, element.DesiredSize.Height);
        Assert.Equal(10, element.RenderSize.Width);
        Assert.Equal(10, element.RenderSize.Height);
        Assert.Equal(10, child.RenderSize.Width);
        Assert.Equal(10, child.RenderSize.Height);
    }
}
