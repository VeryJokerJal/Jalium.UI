using System.Collections;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class GridDefinitionCollectionWpfParityTests
{
    [Fact]
    public void RowAndColumnCollectionsExposeNonGenericCollectionState()
    {
        var grid = new Grid();

        Assert.False(grid.RowDefinitions.IsReadOnly);
        Assert.False(grid.RowDefinitions.IsSynchronized);
        Assert.Same(((ICollection)grid.RowDefinitions).SyncRoot, grid.RowDefinitions.SyncRoot);

        Assert.False(grid.ColumnDefinitions.IsReadOnly);
        Assert.False(grid.ColumnDefinitions.IsSynchronized);
        Assert.Same(((ICollection)grid.ColumnDefinitions).SyncRoot, grid.ColumnDefinitions.SyncRoot);
    }
}
