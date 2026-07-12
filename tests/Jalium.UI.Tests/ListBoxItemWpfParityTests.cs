using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ListBoxItemWpfParityTests
{
    [Fact]
    public void SelectionDependencyPropertyAndEventsAreSelectorOwners()
    {
        Assert.Same(Selector.IsSelectedProperty, ListBoxItem.IsSelectedProperty);
        Assert.Same(Selector.SelectedEvent, ListBoxItem.SelectedEvent);
        Assert.Same(Selector.UnselectedEvent, ListBoxItem.UnselectedEvent);

        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            ListBoxItem.IsSelectedProperty.GetMetadata(typeof(ListBoxItem)));
        Assert.True(metadata.BindsTwoWayByDefault);
        Assert.True(metadata.Journal);
    }

    [Fact]
    public void SelectionChangesRaiseAttachedAndInstanceEventsThroughVirtualHooks()
    {
        var item = new ProbeListBoxItem();
        var selectedCount = 0;
        var unselectedCount = 0;
        var attachedSelectedCount = 0;
        item.Selected += (_, _) => selectedCount++;
        item.Unselected += (_, _) => unselectedCount++;
        Selector.AddSelectedHandler(item, (_, _) => attachedSelectedCount++);

        item.IsSelected = true;
        item.IsSelected = false;

        Assert.Equal(1, selectedCount);
        Assert.Equal(1, unselectedCount);
        Assert.Equal(1, attachedSelectedCount);
        Assert.Equal(1, item.SelectedHookCount);
        Assert.Equal(1, item.UnselectedHookCount);
    }

    private sealed class ProbeListBoxItem : ListBoxItem
    {
        public int SelectedHookCount { get; private set; }
        public int UnselectedHookCount { get; private set; }

        protected override void OnSelected(RoutedEventArgs e)
        {
            SelectedHookCount++;
            base.OnSelected(e);
        }

        protected override void OnUnselected(RoutedEventArgs e)
        {
            UnselectedHookCount++;
            base.OnUnselected(e);
        }
    }
}
