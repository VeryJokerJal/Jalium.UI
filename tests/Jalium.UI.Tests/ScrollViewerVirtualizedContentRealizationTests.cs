using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Drives the chain a real virtualized list uses — ListBox templated as
/// ScrollViewer &gt; ItemsPresenter, with a virtualizing panel underneath — and asserts
/// the panel ends up with the containers its viewport needs, and no more.
/// </summary>
/// <remarks>
/// Exercising the panel on its own misses what this covers. Alone it is handed a
/// constraint directly and behaves; in the chain the ScrollViewer decides what
/// constraint it ever sees. When the ItemsPresenter is picked up as the viewer's
/// <c>IScrollInfo</c> the panel is measured against the viewport and virtualizes
/// properly; when it is not, the panel is measured unbounded, treats the entire
/// extent as its viewport, and realizes every item.
/// </remarks>
[Collection("Application")]
public sealed class ScrollViewerVirtualizedContentRealizationTests
{
    private const double CellWidth = 100;
    private const double CellHeight = 50;
    private const double ViewerWidth = 320;   // three columns
    private const double ViewerHeight = 300;  // six rows

    /// <summary>
    /// Derives its cell width from the constraint, the way a responsive card grid does —
    /// which means writing a layout dependency property from inside its own measure.
    /// </summary>
    /// <remarks>
    /// That write invalidates measure while measure is still running. Whether the
    /// invalidation survives the pass that raised it is the whole question: if it is
    /// swallowed, a panel that realized against a viewport it did not have yet stays
    /// stuck at whatever it realized first.
    /// </remarks>
    private sealed class SelfSizingWrapPanel : VirtualizingWrapPanel
    {
        private const double Columns = 3;

        public SelfSizingWrapPanel()
        {
            ItemHeight = CellHeight;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (double.IsFinite(availableSize.Width) && availableSize.Width > 0)
            {
                var cell = Math.Max(1, availableSize.Width / Columns);
                if (double.IsNaN(ItemWidth) || Math.Abs(ItemWidth - cell) > 0.01)
                {
                    ItemWidth = cell;
                }
            }

            return base.MeasureOverride(availableSize);
        }
    }

    private sealed class ProbeListBox : ListBox
    {
        public ProbeListBox(int itemCount, bool selfSizingPanel = false)
        {
            // The same shape a real virtualized list uses, spelled out here rather than
            // taken from a theme so the test does not depend on one being loaded.
            var template = new ControlTemplate(typeof(ListBox));
            template.SetVisualTree(() => new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new ItemsPresenter(),
            });

            Template = template;
            ItemsPanel = new ItemsPanelTemplate
            {
                PanelType = selfSizingPanel ? typeof(SelfSizingWrapPanel) : typeof(VirtualizingWrapPanel),
            };
            ItemsSource = Enumerable.Range(0, itemCount).Select(i => $"item {i}").ToList();
        }

        // Pinned on both axes. An unpinned container collapses and virtualization
        // silently stops meaning anything.
        protected override FrameworkElement GetContainerForItem(object item)
            => new Border
            {
                Width = CellWidth,
                MinWidth = CellWidth,
                Height = CellHeight,
                MinHeight = CellHeight,
            };
    }

    private static T? FindDescendant<T>(Visual root) where T : Visual
    {
        if (root is T match)
        {
            return match;
        }

        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is { } child && FindDescendant<T>(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static Size PreviousConstraintOf(UIElement element)
        => (Size)typeof(UIElement)
            .GetProperty("PreviousAvailableSize", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(element)!;

    /// <summary>Runs measure/arrange until the tree stops asking for another pass.</summary>
    /// <remarks>
    /// Re-measuring an invalidated element with the constraint that element itself last
    /// saw is the step that matters, and it is what LayoutManager does. Measuring from
    /// the root is not equivalent: a parent whose own constraint did not change
    /// short-circuits and never reaches the child, so an invalidation raised deep in the
    /// tree — by a panel that only learns its viewport at arrange time — goes nowhere.
    /// </remarks>
    private static (VirtualizingWrapPanel? panel, int passes) Settle(
        ProbeListBox list, Size constraint, int maxPasses = 10)
    {
        var bounds = new Rect(0, 0, constraint.Width, constraint.Height);
        var passes = 0;

        // The template is what puts a ScrollViewer and an ItemsPresenter between the list
        // and its panel; without it there is no chain to test.
        list.ApplyTemplate();

        for (; passes < maxPasses; passes++)
        {
            list.Measure(constraint);
            list.Arrange(bounds);

            if (FindDescendant<VirtualizingWrapPanel>(list) is { IsMeasureValid: false } panel)
            {
                panel.Measure(PreviousConstraintOf(panel));
                list.InvalidateArrange();
                list.Arrange(bounds);
                continue;
            }

            if (list.IsMeasureValid && list.IsArrangeValid)
            {
                break;
            }

            list.InvalidateMeasure();
        }

        return (FindDescendant<VirtualizingWrapPanel>(list), passes);
    }

    [Fact]
    public void TemplatedList_HandsThePanelItsViewportNotAnUnboundedConstraint()
    {
        var list = new ProbeListBox(itemCount: 600);
        Settle(list, new Size(ViewerWidth, ViewerHeight));

        var viewer = FindDescendant<ScrollViewer>(list);
        Assert.NotNull(viewer);

        var scrollInfo = typeof(ScrollViewer)
            .GetProperty("ScrollInfo", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(viewer);

        Assert.True(
            scrollInfo != null,
            "the viewer never picked up the presenter as its IScrollInfo, so the panel " +
            "is measured unbounded and cannot virtualize");
    }

    [Fact]
    public void TemplatedList_VirtualizesInsteadOfRealizingEveryItem()
    {
        var list = new ProbeListBox(itemCount: 600);
        var (panel, _) = Settle(list, new Size(ViewerWidth, ViewerHeight));

        Assert.NotNull(panel);

        // Six visible rows of three, plus a cache band. Realizing all 600 means the
        // panel is treating the whole extent as its viewport.
        Assert.True(
            panel!.Children.Count < 200,
            $"{panel.Children.Count} of 600 containers realized for a six-row viewport — " +
            "virtualization is not in effect");
    }

    [Fact]
    public void TemplatedList_FillsTheViewportNotJustOneRow()
    {
        var list = new ProbeListBox(itemCount: 600);
        var (panel, _) = Settle(list, new Size(ViewerWidth, ViewerHeight));

        Assert.NotNull(panel);
        Assert.True(
            panel!.Children.Count >= 18,
            $"only {panel.Children.Count} containers realized for a six-row viewport");
    }

    [Fact]
    public void TemplatedList_WithASelfSizingPanel_StillFillsTheViewport()
    {
        // A panel that derives its cell size from the constraint invalidates measure from
        // inside measure. That must not cost it the containers it has not realized yet.
        var list = new ProbeListBox(itemCount: 600, selfSizingPanel: true);
        var (panel, _) = Settle(list, new Size(ViewerWidth, ViewerHeight));

        Assert.NotNull(panel);
        Assert.True(
            panel!.Children.Count >= 18,
            $"only {panel.Children.Count} containers realized for a six-row viewport");
        Assert.True(
            panel.Children.Count < 200,
            $"{panel.Children.Count} of 600 realized — virtualization is not in effect");
    }

    [Fact]
    public void TemplatedList_LayoutSettlesInsteadOfLooping()
    {
        var list = new ProbeListBox(itemCount: 600);
        var (_, passes) = Settle(list, new Size(ViewerWidth, ViewerHeight));

        Assert.True(passes < 9, $"layout needed {passes + 1} passes to settle");
    }

    [Theory]
    [InlineData(false, 700)]
    [InlineData(true, 300)]
    public void TemplatedList_WheelSequence_ReachesTheLastVirtualizedRow(
        bool useSmoothInertia,
        int wheelEvents)
    {
        var list = new ProbeListBox(itemCount: 600);
        Settle(list, new Size(ViewerWidth, ViewerHeight));
        var viewer = Assert.IsType<ScrollViewer>(FindDescendant<ScrollViewer>(list));
        viewer.IsScrollInertiaEnabled = useSmoothInertia;
        viewer.ScrollInertiaDurationMs = 300;

        for (var index = 0; index < wheelEvents; index++)
        {
            viewer.RaiseEvent(new MouseWheelEventArgs(
                UIElement.MouseWheelEvent,
                new Point(8, 8),
                delta: -120,
                leftButton: MouseButtonState.Released,
                middleButton: MouseButtonState.Released,
                rightButton: MouseButtonState.Released,
                xButton1: MouseButtonState.Released,
                xButton2: MouseButtonState.Released,
                modifiers: ModifierKeys.None,
                timestamp: index + 1));
        }

        var advance = typeof(ScrollViewer).GetMethod(
            "AdvanceSmoothScrollByMilliseconds",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var isSmoothScrolling = typeof(ScrollViewer).GetField(
            "_isSmoothScrolling",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(advance);
        Assert.NotNull(isSmoothScrolling);
        for (var frame = 0;
             frame < 256 && (bool)isSmoothScrolling!.GetValue(viewer)!;
             frame++)
        {
            advance!.Invoke(viewer, [16L]);
        }

        var (panel, _) = Settle(list, new Size(ViewerWidth, ViewerHeight));

        Assert.False((bool)isSmoothScrolling!.GetValue(viewer)!);
        Assert.InRange(
            Math.Abs(viewer.VerticalOffset - viewer.ScrollableHeight),
            0,
            0.5);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(599));
        Assert.NotNull(panel);
    }
}
