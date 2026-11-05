using System.Runtime.CompilerServices;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class GeneratedGridResourceTests
{
    [Fact]
    public void GeneratedNonVisualResourcesUpdateAndExplicitAssignmentsReplaceThem()
    {
        Initialize();
        var owner = new GeneratedGridResourceView();
        owner.Measure(new Size(400, 200)); owner.Arrange(new Rect(0, 0, 400, 200));
        var column = Assert.IsType<DataGrid>(owner.Content).Columns.Single();
        Assert.Equal(250, column.Width.Value);
        Assert.Equal(250, column.ActualWidth);
        Assert.Equal("Name", column.Header);
        owner.Resources["GridColumnTitle"] = "名称";
        Assert.Equal("名称", column.Header);
        column.Header = "Custom";
        owner.Resources["GridColumnTitle"] = "Name";
        Assert.Equal("Custom", column.Header);
    }

    [Fact]
    public void RemovedNonVisualResourceTargetsAreNotRetainedByTheirHost()
    {
        Initialize();
        var owner = new GeneratedGridResourceView();
        var weak = RemoveColumn(owner);
        Collect();
        Assert.False(weak.IsAlive);
        owner.Resources["GridColumnTitle"] = "Changed";
        GC.KeepAlive(owner);
    }

    [Fact]
    public void ARetainedResourceTargetDoesNotRetainItsFormerHost()
    {
        Initialize();
        var (column, weak) = ReleaseOwner();
        Collect();
        Assert.False(weak.IsAlive);
        GC.KeepAlive(column);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RemoveColumn(GeneratedGridResourceView owner)
    {
        var grid = Assert.IsType<DataGrid>(owner.Content);
        var column = grid.Columns.Single(); grid.Columns.Clear();
        return new WeakReference(column);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DataGridColumn, WeakReference) ReleaseOwner()
    {
        var owner = new GeneratedGridResourceView();
        var grid = Assert.IsType<DataGrid>(owner.Content);
        var column = grid.Columns.Single(); grid.Columns.Clear();
        Assert.Null(typeof(DataGridColumn).GetField("_dataGridOwner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(column));
        owner.Content = null;
        owner.Measure(new Size(400, 200)); owner.Arrange(new Rect(0, 0, 400, 200)); owner.UpdateLayout();
        return (column, new WeakReference(owner));
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void Initialize() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }
}

public partial class GeneratedGridResourceView : UserControl
{
    public GeneratedGridResourceView() => InitializeComponent();
}
