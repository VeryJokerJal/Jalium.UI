using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers content that sizes itself from the viewport it is being shown in — the
/// "fill the viewport, scroll internally" page shape.
/// </summary>
/// <remarks>
/// Without a way to express that intent as a constraint, the only thing such a page can
/// do is read <see cref="ScrollViewer.ViewportHeight"/> back and pin its own Height to
/// it. That closes a loop: the height it sets decides the extent, the extent decides
/// whether an Auto scroll bar appears, the bar decides the viewport, and the viewport is
/// what it reads next time. Whether that loop settles is entirely up to the framework —
/// there is no application code in this file.
/// <para>
/// These are the symptoms to watch for: a height that never stops changing at a fixed
/// window size, a scroll bar appearing for content that exactly fits, and a height that
/// lags a resize by one pass instead of tracking it.
/// </para>
/// </remarks>
[Collection("Application")]
public sealed class ScrollViewerViewportFeedbackStabilityTests : IDisposable
{
    public ScrollViewerViewportFeedbackStabilityTests() => ScrollBarTemplateTestHelper.Register();
    public void Dispose() => ScrollBarTemplateTestHelper.Reset();
    /// <summary>Pins its own height to the viewport of the ScrollViewer above it,
    /// reading that value during arrange — the shape a self-sizing page ends up with.</summary>
    private sealed class ViewportEchoingContent : Grid
    {
        public int HeightWrites { get; private set; }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (FindAncestorViewer() is { } host)
            {
                var viewport = host.ViewportHeight;
                if (viewport > 0 &&
                    (double.IsNaN(Height) || Math.Abs(Height - viewport) > 0.5))
                {
                    Height = viewport;
                    HeightWrites++;
                }
            }

            return base.ArrangeOverride(finalSize);
        }

        private ScrollViewer? FindAncestorViewer()
        {
            for (var current = VisualParent; current != null; current = current.VisualParent)
            {
                if (current is ScrollViewer viewer)
                {
                    return viewer;
                }
            }

            return null;
        }
    }

    private static (ScrollViewer viewer, ViewportEchoingContent content) Build()
    {
        var content = new ViewportEchoingContent();

        // Something tall enough to matter if the pinning ever stops working.
        content.Children.Add(new Border { Height = 2000 });

        var viewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        return (viewer, content);
    }

    private static void Layout(ScrollViewer viewer, double width, double height)
    {
        viewer.InvalidateMeasure();
        viewer.Measure(new Size(width, height));
        viewer.Arrange(new Rect(0, 0, width, height));
    }

    [Fact]
    public void SelfSizingContent_SettlesAtAFixedWindowSize()
    {
        var (viewer, content) = Build();

        for (var i = 0; i < 6; i++)
        {
            Layout(viewer, 800, 600);
        }

        var writesBefore = content.HeightWrites;

        // Steady state: further passes at the same size must not keep rewriting the height.
        for (var i = 0; i < 5; i++)
        {
            Layout(viewer, 800, 600);
        }

        Assert.Equal(writesBefore, content.HeightWrites);
    }

    [Fact]
    public void SelfSizingContent_DoesNotProvokeAScrollBarWhenItExactlyFits()
    {
        var (viewer, _) = Build();

        for (var i = 0; i < 6; i++)
        {
            Layout(viewer, 800, 600);
        }

        Assert.False(
            viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            "content pinned to the viewport still produced a vertical scroll bar");
    }

    [Fact]
    public void SelfSizingContent_ConvergesAtEveryStepOfAResize()
    {
        var (viewer, content) = Build();
        double[] heights = [600, 520, 700, 480, 900, 640];

        foreach (var windowHeight in heights)
        {
            // Two passes is what a real resize gets: one where the content learns the
            // new viewport, one where the size it picked takes effect.
            Layout(viewer, 800, windowHeight);
            Layout(viewer, 800, windowHeight);

            Assert.False(
                viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                $"a scroll bar was still showing at window height {windowHeight}");

            Assert.True(
                Math.Abs(content.ActualHeight - viewer.ViewportHeight) <= 0.5,
                $"at window height {windowHeight} the content settled at " +
                $"{content.ActualHeight} against a viewport of {viewer.ViewportHeight}");
        }
    }

    /// <summary>Content that sizes itself from the viewport it is given, instead of
    /// reading back the one it was given last time.</summary>
    private sealed class ViewportFillingContent : Grid
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            base.MeasureOverride(availableSize);

            // Takes the whole viewport on the scroll axis and scrolls internally — the
            // intent FillViewport exists to express. Without it this is measured against
            // infinity and there is nothing sensible to return.
            return new Size(
                double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
                double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        }
    }

    [Fact]
    public void FillViewportContent_LandsInOnePassWithNoBar()
    {
        var content = new ViewportFillingContent();
        ScrollViewer.SetFillViewport(content, FillViewportMode.Vertical);

        var viewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // The contrast with the read-back version above: the size arrives as a
        // constraint, so there is no lagging value and nothing to converge on.
        foreach (var windowHeight in new double[] { 600, 520, 700, 480, 900, 640 })
        {
            Layout(viewer, 800, windowHeight);

            Assert.False(
                viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                $"a scroll bar appeared at window height {windowHeight} after one pass");

            Assert.True(
                Math.Abs(content.ActualHeight - viewer.ViewportHeight) <= 0.5,
                $"at window height {windowHeight} the content was {content.ActualHeight} " +
                $"against a viewport of {viewer.ViewportHeight}");
        }
    }
}
