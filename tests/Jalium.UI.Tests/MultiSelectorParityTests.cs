using System.Collections;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class MultiSelectorParityTests
{
    [Fact]
    public void SelectedItemsUsesWpfIListSurfaceAndRemainsMutable()
    {
        Assert.Equal(
            typeof(IList),
            typeof(MultiSelector).GetProperty(nameof(MultiSelector.SelectedItems))!.PropertyType);

        var selector = new ProbeMultiSelector();
        selector.Items.Add("first");
        selector.Items.Add("second");

        selector.SelectAll();

        Assert.Equal(new object[] { "first", "second" }, selector.SelectedItems.Cast<object>());
        selector.SelectedItems.Remove("first");
        Assert.Equal("second", selector.SelectedItem);
    }

    private sealed class ProbeMultiSelector : MultiSelector;
}
