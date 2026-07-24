using System.Collections.ObjectModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using static Jalium.UI.Tests.VisualTreeTestHelpers;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression coverage for the stale two-source <c>VisualChildrenCount</c> crash.
///
/// A control that publishes a cached visual-children count of N (via
/// <see cref="Visual"/>'s virtual override) and then clears the backing field WITHOUT a
/// re-sync leaves the public <c>DependencyObject.VisualChildrenCount</c> shim stranded at N
/// while the live tree holds fewer children. A subsequent traversal that reads the loop
/// bound from the stale field and fetches children through the live delegate accessor
/// (e.g. <c>FrameworkElement.ReEvaluateImplicitStylesRecursive</c> on attach) walks off the
/// end of the child list and throws <see cref="ArgumentOutOfRangeException"/>.
///
/// The reported crash was a ListBox given an explicit ItemContainerStyle; the same ordering
/// hazard existed across a whole family of producers. Every test asserts
/// <see cref="VisualTreeTestHelpers.AssertShimConsistent"/> (cached field == live virtual and
/// the cached bound is walkable) plus no throw on the traversal path.
/// </summary>
[Collection("Application")]
public class StaleVisualChildrenCountRegressionTests
{
    private static Style HeightOnlyItemStyle() =>
        new Style(typeof(ListBoxItem)) { Setters = { new Setter(FrameworkElement.HeightProperty, 24.0) } };

    // ---- The reported crash: own-container ListBox + explicit ItemContainerStyle. ----
    [Fact]
    public void OwnContainerListBox_SetItemContainerStyleAfterMeasure_DoesNotThrow_KeepsTemplate()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var lb = new ShimTestListBox { Width = 320, Height = 240 };
            for (var i = 0; i < 50; i++)
                lb.Items.Add(new ListBoxItem { Content = $"Item {i}" });

            lb.Measure(new Size(320, 240));
            lb.Arrange(new Rect(0, 0, 320, 240));

            // Assigning ItemContainerStyle after the containers are live + templated evicts
            // and re-inserts them, driving the exact reported RealizeContainer -> AddVisualChild
            // -> ReEvaluateImplicitStylesRecursive stack.
            var ex = Record.Exception(() =>
            {
                lb.ItemContainerStyle = HeightOnlyItemStyle();
                lb.Measure(new Size(320, 240));
                lb.Arrange(new Rect(0, 0, 320, 240));
            });
            Assert.Null(ex);

            for (var i = 0; i < lb.Items.Count; i++)
            {
                var container = lb.ItemContainerGenerator.ContainerFromIndex(i);
                if (container == null)
                    continue; // virtualized away — nothing realized to check
                AssertShimConsistent(container);
                Assert.NotNull(((Control)container).Template); // theme template retained (blank-item fix)
                Assert.Equal(24.0, ((FrameworkElement)container).Height); // explicit setter still wins
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Generated-container path + a forced re-realize. ----
    [Fact]
    public void GeneratedContainerListBox_ItemContainerStyle_ReRealize_DoesNotThrow_KeepsTemplate()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var items = new ObservableCollection<string>();
            for (var i = 0; i < 50; i++) items.Add($"Item {i}");
            var lb = new ShimTestListBox
            {
                Width = 320,
                Height = 240,
                ItemsSource = items,
                ItemContainerStyle = HeightOnlyItemStyle(),
            };

            var ex = Record.Exception(() =>
            {
                lb.Measure(new Size(320, 240));
                lb.Arrange(new Rect(0, 0, 320, 240));
                // Force a second realization pass over the (now templated) generated containers.
                lb.Host?.InvalidateMeasure();
                lb.Measure(new Size(320, 240));
                lb.Arrange(new Rect(0, 0, 320, 240));
            });
            Assert.Null(ex);

            var container = lb.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.NotNull(container);
            AssertShimConsistent(container!);
            Assert.NotNull(((Control)container!).Template);              // not blank
            Assert.NotNull(FindDescendant<ContentPresenter>(container)); // has a content site
            Assert.Equal(24.0, ((FrameworkElement)container).Height);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Desync isolation with no ItemsControl (converts scratch fact 3). ----
    [Fact]
    public void ListBoxItem_ExplicitStyleThenReattach_ShimConsistent_DoesNotThrow()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var item = new ListBoxItem { Content = "hello" };
            var panel1 = new StackPanel();
            panel1.Children.Add(item); // theme template applied, cached field becomes 1
            AssertShimConsistent(item);

            item.Style = HeightOnlyItemStyle(); // style/template re-resolution path
            AssertShimConsistent(item);

            panel1.Children.Remove(item);
            var panel2 = new StackPanel();
            var ex = Record.Exception(() => panel2.Children.Add(item)); // reattach -> ReEvaluateImplicitStylesRecursive
            Assert.Null(ex);
            AssertShimConsistent(item);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: ContentControl.RebuildDirectContent (converts scratch fact 4). ----
    [Fact]
    public void ContentControl_ContentNonNullThenNull_AttachDoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        try
        {
            var cc = new ContentControl(); // _usesDirectContent defaults true
            cc.Content = new Button();
            cc.Content = null; // RemoveVisualChild(_contentElement) then null — pre-fix stranded field=1
            AssertShimConsistent(cc);
            Assert.Null(PrivateFieldValue(cc, "_contentElement"));

            var panel = new StackPanel();
            Assert.Null(Record.Exception(() => panel.Children.Add(cc)));
            AssertShimConsistent(cc);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: ContentPresenter.OnContentChanged. ----
    [Fact]
    public void ContentPresenter_ContentToNull_AttachDoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        try
        {
            var cp = new ContentPresenter { Content = new Button() };
            cp.Measure(new Size(100, 40)); // realize _contentElement
            cp.Content = null;
            AssertShimConsistent(cp);

            var panel = new StackPanel();
            Assert.Null(Record.Exception(() =>
            {
                panel.Children.Add(cp);
                panel.Measure(new Size(100, 40));
            }));
            AssertShimConsistent(cp);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: Page.ClearVisualTree (both _templateRoot and _contentVisual). ----
    [Fact]
    public void Page_ContentNonNullThenNull_DoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var page = new Page();
            page.Content = new Button();
            page.Content = null; // ClearVisualTree removes-before-null
            AssertShimConsistent(page);

            Assert.Null(Record.Exception(() => page.Measure(new Size(320, 240))));
            AssertShimConsistent(page);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: TabControl.RemoveSelectedContentPresenter. ----
    [Fact]
    public void TabControl_ClearItems_DoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var tc = new TabControl { Width = 320, Height = 240 };
            tc.Items.Add(new TabItem { Header = "A", Content = new Button() });
            tc.Items.Add(new TabItem { Header = "B", Content = new Button() });
            tc.SelectedIndex = 0;
            tc.Measure(new Size(320, 240));
            tc.Arrange(new Rect(0, 0, 320, 240));

            var ex = Record.Exception(() =>
            {
                tc.Items.Clear(); // drops the selected content presenter
                tc.Measure(new Size(320, 240));
                tc.Arrange(new Rect(0, 0, 320, 240));
            });
            Assert.Null(ex);
            AssertShimConsistent(tc);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: Track part setters cleared to null. ----
    [Fact]
    public void Track_PartsSetToNull_DoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        try
        {
            var track = new Track
            {
                Thumb = new Thumb(),
                DecreaseRepeatButton = new RepeatButton(),
                IncreaseRepeatButton = new RepeatButton(),
            };
            track.Measure(new Size(20, 200));

            track.Thumb = null;
            track.DecreaseRepeatButton = null;
            track.IncreaseRepeatButton = null;
            AssertShimConsistent(track);

            var panel = new StackPanel();
            Assert.Null(Record.Exception(() =>
            {
                panel.Children.Add(track);
                panel.Measure(new Size(20, 200));
            }));
            AssertShimConsistent(track);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: StatusBarItem.OnContentChanged. ----
    [Fact]
    public void StatusBarItem_ContentToNull_DoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        try
        {
            var sbi = new StatusBarItem { Content = new Button() };
            sbi.Measure(new Size(100, 40));
            sbi.Content = null;
            AssertShimConsistent(sbi);

            var panel = new StackPanel();
            Assert.Null(Record.Exception(() =>
            {
                panel.Children.Add(sbi);
                panel.Measure(new Size(100, 40));
            }));
            AssertShimConsistent(sbi);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer: AdornedElementPlaceholder.Child setter cleared to null. ----
    [Fact]
    public void AdornedElementPlaceholder_ChildToNull_DoesNotThrow_ShimConsistent()
    {
        ResetApplicationState();
        try
        {
            var placeholder = new AdornedElementPlaceholder { Child = new Button() };
            placeholder.Measure(new Size(100, 40));
            placeholder.Child = null; // RemoveVisualChild before reassign — pre-fix stranded field=1
            AssertShimConsistent(placeholder);

            var panel = new StackPanel();
            Assert.Null(Record.Exception(() =>
            {
                panel.Children.Add(placeholder);
                panel.Measure(new Size(100, 40));
            }));
            AssertShimConsistent(placeholder);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    // ---- Producer (different flavor): TransitioningContentControl flips _isTransitioning
    //      without an Add/RemoveVisualChild, so the fix re-syncs the cached field explicitly. ----
    [Fact]
    public void TransitioningContentControl_ContentSwap_ShimConsistent_DoesNotThrow()
    {
        ResetApplicationState();
        _ = Application.Current ?? new Application();
        try
        {
            var tcc = new TransitioningContentControl
            {
                Width = 200,
                Height = 100,
                Transition = new CrossfadeTransition(),
                Content = new Button { Content = "A" },
            };
            tcc.Measure(new Size(200, 100));
            tcc.Arrange(new Rect(0, 0, 200, 100));

            var ex = Record.Exception(() =>
            {
                tcc.Content = new TextBlock { Text = "B" }; // may start a transition (_isTransitioning=true)
                AssertShimConsistent(tcc);
                tcc.Measure(new Size(200, 100));
                tcc.Arrange(new Rect(0, 0, 200, 100));
            });
            Assert.Null(ex);
            AssertShimConsistent(tcc);
        }
        finally
        {
            ResetApplicationState();
        }
    }
}
