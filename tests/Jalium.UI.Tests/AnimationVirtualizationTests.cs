using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using Jalium.UI.Animation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Controls.Virtualization;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

// Container-lifecycle integration of the animation engine: recycling stops and
// discards animations, the base ClearContainerForItem resets animation-typical
// local values, a collection Reset preserves the recycle pool, and
// UIElementCollection.Clear notifies the virtualizing panel. Serialized via the
// "Application" collection because the tests drive the process-wide
// AnimationManager.
[Collection("Application")]
public class AnimationVirtualizationTests
{
    private static long Ticks(double seconds) => (long)(seconds * Stopwatch.Frequency);

    private sealed class TestListBox : ListBox
    {
        public Panel? Host => ItemsHost;
    }

    private sealed class ClearNotifyPanel : VirtualizingStackPanel
    {
        public int ClearNotifications;

        protected override void OnClearChildren()
        {
            ClearNotifications++;
            base.OnClearChildren();
        }
    }

    private sealed class ResettableCollection<T> : ObservableCollection<T>
    {
        public void RaiseReset()
            => OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private static TestListBox CreateRealizedListBox(int itemCount, out VirtualizingStackPanel host)
    {
        var listBox = new TestListBox { Width = 320, Height = 240 };
        for (var i = 0; i < itemCount; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        return listBox;
    }

    private static void ScrollPanelTo(VirtualizingStackPanel host, double offset)
    {
        // Headless re-measure rule: SetVerticalOffset only invalidates the panel;
        // the ListBox would short-circuit a same-size Measure, so re-measure the
        // panel directly to apply the new offset.
        host.SetVerticalOffset(offset);
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));
    }

    private static ContainerRecyclePool GetRecyclePool(ItemContainerGenerator generator)
    {
        var field = typeof(ItemContainerGenerator).GetField("_recyclePool", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<ContainerRecyclePool>(field!.GetValue(generator));
    }

    private static HashSet<DependencyObject> CaptureRealizedContainers(ItemContainerGenerator generator, int itemCount)
    {
        var set = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < itemCount; i++)
        {
            if (generator.ContainerFromIndex(i) is { } container)
            {
                set.Add(container);
            }
        }

        return set;
    }

    // ── Recycle-time animation hygiene ──────────────────────────────────────

    [Fact]
    public void RecycledContainer_HasAnimationsStopped_AndAnimatedLayerDiscarded()
    {
        var listBox = CreateRealizedListBox(5000, out var host);
        var container = Assert.IsAssignableFrom<UIElement>(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(10)
        }));
        AnimationManager.ProcessFrame(t0 + Ticks(0.5));
        Assert.True(container.HasAnimation(UIElement.OpacityProperty));
        Assert.True(container.HasAnimatedValue(UIElement.OpacityProperty));

        ScrollPanelTo(host, 3000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0)); // recycled out

        Assert.False(container.HasAnimation(UIElement.OpacityProperty));
        Assert.False(container.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(1.0, (double)container.GetValue(UIElement.OpacityProperty)!);
    }

    [Fact]
    public void RecycledContainer_CompletedHoldEndResidue_IsDiscardedToBase()
    {
        // A HoldEnd animation that already completed leaves its final value in
        // the animated layer. Recycling must hard-discard it (no HoldEnd
        // promotion) or the pooled container carries the ghost value forever.
        var listBox = CreateRealizedListBox(5000, out var host);
        var container = Assert.IsAssignableFrom<UIElement>(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 0.3,
            Duration = TimeSpan.FromSeconds(0.5),
            FillBehavior = FillBehavior.HoldEnd
        }));
        AnimationManager.ProcessFrame(t0 + Ticks(2.0)); // completes, holds 0.3
        Assert.Equal(0.3, (double)container.GetValue(UIElement.OpacityProperty)!, 3);

        ScrollPanelTo(host, 3000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        Assert.False(container.HasAnimatedValue(UIElement.OpacityProperty));
        Assert.Equal(1.0, (double)container.GetValue(UIElement.OpacityProperty)!);
    }

    [Fact]
    public void StandardModeDiscard_AlsoStopsAnimations()
    {
        // The non-recycling discard path must stop animations too — otherwise a
        // running clock keeps the dead container registered with the manager.
        var listBox = new TestListBox { Width = 320, Height = 240 };
        VirtualizingPanel.SetVirtualizationMode(listBox, VirtualizationMode.Standard);
        for (var i = 0; i < 5000; i++)
        {
            listBox.Items.Add($"Item {i}");
        }

        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var container = Assert.IsAssignableFrom<UIElement>(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        long t0 = Stopwatch.GetTimestamp();
        AnimationEngineTests.RunInsideFrame(t0, _ => container.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromSeconds(10)
        }));
        Assert.True(container.HasAnimation(UIElement.OpacityProperty));

        ScrollPanelTo(host, 3000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0)); // discarded

        Assert.False(container.HasAnimation(UIElement.OpacityProperty));
        Assert.False(container.HasAnimatedValue(UIElement.OpacityProperty));
    }

    // ── ClearContainerForItem visual-state hygiene ──────────────────────────

    [Fact]
    public void ClearContainerForItem_ResetsAnimationTypicalLocalValues()
    {
        var listBox = CreateRealizedListBox(5000, out var host);
        var container = Assert.IsAssignableFrom<UIElement>(listBox.ItemContainerGenerator.ContainerFromIndex(0));

        container.SetValue(UIElement.RenderTransformProperty, new TranslateTransform { Y = 12 });
        container.SetValue(UIElement.OpacityProperty, 0.4);
        container.SetValue(UIElement.ClipToBoundsProperty, true);
        container.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);

        ScrollPanelTo(host, 3000);
        Assert.Null(listBox.ItemContainerGenerator.ContainerFromIndex(0)); // recycled through ClearContainerForItem

        Assert.Null(container.GetValue(UIElement.RenderTransformProperty));
        Assert.Equal(1.0, (double)container.GetValue(UIElement.OpacityProperty)!);
        Assert.False((bool)container.GetValue(UIElement.ClipToBoundsProperty)!);
        Assert.Equal(Visibility.Visible, (Visibility)container.GetValue(UIElement.VisibilityProperty)!);
    }

    // ── Collection Reset preserves the recycle pool ─────────────────────────

    [Fact]
    public void CollectionReset_PreservesTheRecyclePool_AndReusesContainerInstances()
    {
        var items = new ResettableCollection<string>();
        for (var i = 0; i < 50; i++)
        {
            items.Add($"Item {i}");
        }

        var listBox = new TestListBox { Width = 320, Height = 240, ItemsSource = items };
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var generator = listBox.ItemContainerGenerator;

        var before = CaptureRealizedContainers(generator, 50);
        Assert.NotEmpty(before);
        var pool = GetRecyclePool(generator);

        items.RaiseReset();

        Assert.True(pool.Count > 0, "a collection Reset must recycle realized containers into the pool, not drop them");
        Assert.Null(generator.ContainerFromIndex(0)); // bookkeeping cleared, containers pooled

        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        var reused = Assert.IsAssignableFrom<DependencyObject>(generator.ContainerFromIndex(0));
        Assert.Contains(reused, before); // popped from the pool, not rebuilt
        Assert.Equal("Item 0", generator.ItemFromContainer(reused));
    }

    [Fact]
    public void ItemsSourceRebind_StillTearsDownThePool_AndBuildsFreshContainers()
    {
        // RemoveAll (RefreshItems / ItemsSource / ItemTemplate rebind paths)
        // keeps its full-teardown semantics: only a pure collection Reset pools.
        var listBox = new TestListBox { Width = 320, Height = 240 };
        listBox.ItemsSource = Enumerable.Range(0, 50).Select(i => $"First {i}").ToList();
        listBox.Measure(new Size(320, 240));
        listBox.Arrange(new Rect(0, 0, 320, 240));
        var host = Assert.IsType<VirtualizingStackPanel>(listBox.Host);
        var generator = listBox.ItemContainerGenerator;

        var before = CaptureRealizedContainers(generator, 50);
        Assert.NotEmpty(before);

        listBox.ItemsSource = Enumerable.Range(0, 50).Select(i => $"Second {i}").ToList();
        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        var fresh = Assert.IsAssignableFrom<DependencyObject>(generator.ContainerFromIndex(0));
        Assert.DoesNotContain(fresh, before);
        Assert.Equal("Second 0", generator.ItemFromContainer(fresh));
    }

    // ── UIElementCollection.Clear → VirtualizingPanel.OnClearChildren ───────

    [Fact]
    public void ChildrenClear_InvokesOnClearChildren()
    {
        var panel = new ClearNotifyPanel();
        panel.Children.Add(new Border());
        panel.Children.Add(new Border());

        panel.Children.Clear();

        Assert.Equal(1, panel.ClearNotifications);
        Assert.Empty(panel.Children);
    }

    [Fact]
    public void ExternalChildrenClear_LeavesNoZombies_PanelRecoversOnNextMeasure()
    {
        // D13: an external Children.Clear() previously left the panel's
        // realized-container bookkeeping pointing at detached zombies; with
        // OnClearChildren wired the next measure re-realizes cleanly.
        var listBox = CreateRealizedListBox(200, out var host);
        Assert.True(host.Children.Count > 0);

        host.Children.Clear();
        Assert.Empty(host.Children);

        host.Measure(new Size(320, 240));
        host.Arrange(new Rect(0, 0, 320, 240));

        Assert.True(host.Children.Count > 0, "the panel must re-realize after an external Children.Clear");
        var container = Assert.IsAssignableFrom<UIElement>(listBox.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Same(host, ((Visual)container).VisualParent);
        Assert.Equal("Item 0", listBox.ItemContainerGenerator.ItemFromContainer(container));
    }
}

// DevTools reveal integration: containers bound while a reveal is active enter
// at the published staggered progress instead of flashing fully revealed
// (RC3), and the headless expand/collapse path stays synchronous with no
// animation registration.
[Collection("Application")]
public class DevToolsRevealTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static Grid GetRowContent(InspectorRowContainer container)
    {
        var field = typeof(InspectorRowContainer).GetField("_content", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<Grid>(field!.GetValue(container));
    }

    private static InspectorTreeList CreateRealizedTree(int rowCount, Action<InspectorTreeList>? beforeLayout = null)
    {
        var tree = new InspectorTreeList { Width = 300, Height = 200 };
        var rows = new List<InspectorRow>();
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(new InspectorRow(new Border(), depth: 0) { Index = i });
        }

        tree.ItemsSource = rows;
        beforeLayout?.Invoke(tree);

        tree.Measure(new Size(300, 200));
        tree.Arrange(new Rect(0, 0, 300, 200));
        return tree;
    }

    [Fact]
    public void ContainerBoundDuringActiveReveal_EntersAtTheIntervalProgress_NotFullyRevealed()
    {
        var tree = CreateRealizedTree(10, t => t.SetActiveReveal(2, 6, _ => 0.25));

        var inRange = Assert.IsType<InspectorRowContainer>(tree.ItemContainerGenerator.ContainerFromIndex(3));
        var translate = Assert.IsType<TranslateTransform>(GetRowContent(inRange).RenderTransform);
        Assert.True(translate.Y < 0, "a row inside the active reveal range must enter partially hidden");

        // Outside the range the provider is bypassed: fully revealed, no transform.
        var outside = Assert.IsType<InspectorRowContainer>(tree.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Null(GetRowContent(outside).RenderTransform);
    }

    [Fact]
    public void ContainerBoundAfterClearActiveReveal_EntersFullyRevealed()
    {
        var tree = CreateRealizedTree(10, t =>
        {
            t.SetActiveReveal(0, 10, _ => 0.0);
            t.ClearActiveReveal();
        });

        for (var i = 0; i < 10; i++)
        {
            if (tree.ItemContainerGenerator.ContainerFromIndex(i) is InspectorRowContainer container)
            {
                Assert.Null(GetRowContent(container).RenderTransform);
            }
        }
    }

    [Fact]
    public void HeadlessToggleExpand_IsSynchronous_WithoutStartingAReveal()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var host = new Window
            {
                Title = "Host",
                Content = new StackPanel
                {
                    Children =
                    {
                        new Button { Content = "One" },
                        new Border { Child = new TextBlock { Text = "Two" } }
                    }
                }
            };

            var devTools = new DevToolsWindow(host);
            try
            {
                var rowsField = typeof(DevToolsWindow).GetField("_rows", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(rowsField);
                var rows = (System.Collections.IList)rowsField!.GetValue(devTools)!;

                InspectorRow? collapsible = null;
                foreach (var entry in rows)
                {
                    if (entry is InspectorRow { HasChildren: true, IsExpanded: false } candidate)
                    {
                        collapsible = candidate;
                        break;
                    }
                }

                Assert.NotNull(collapsible);
                var before = rows.Count;

                var toggle = typeof(DevToolsWindow).GetMethod("ToggleExpand", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(toggle);

                // No realized containers headless → the expand happens
                // synchronously in the same call and no reveal is registered.
                toggle!.Invoke(devTools, new object?[] { collapsible });
                Assert.True(rows.Count > before, "headless expand must splice children in synchronously");
                Assert.False(GetRevealActive(devTools));

                toggle.Invoke(devTools, new object?[] { collapsible });
                Assert.Equal(before, rows.Count);
                Assert.False(GetRevealActive(devTools));
            }
            finally
            {
                devTools.CloseDevTools();
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static bool GetRevealActive(DevToolsWindow devTools)
    {
        var field = typeof(DevToolsWindow).GetField("_revealActive", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (bool)field!.GetValue(devTools)!;
    }
}
