using Jalium.UI.Gpu;
using GpuCornerRadius = Jalium.UI.Gpu.CornerRadius;
using GpuRect = Jalium.UI.Gpu.Rect;
using GpuThickness = Jalium.UI.Gpu.Thickness;

namespace Jalium.UI.Tests;

public class IncrementalRuntimeCoreTests
{
    [Fact]
    public void NodeStore_ShouldTrackParentAndChildren()
    {
        var store = new NodeStore();
        var parent = new RectNode
        {
            Id = 10,
            ParentId = 0,
            Bounds = new GpuRect(0, 0, 100, 100),
            CornerRadius = new GpuCornerRadius(0, 0, 0, 0),
            BorderThickness = new GpuThickness(0, 0, 0, 0),
            MaterialIndex = 0
        };
        var child = new RectNode
        {
            Id = 11,
            ParentId = 10,
            Bounds = new GpuRect(10, 10, 20, 20),
            CornerRadius = new GpuCornerRadius(0, 0, 0, 0),
            BorderThickness = new GpuThickness(0, 0, 0, 0),
            MaterialIndex = 0
        };

        store.Load([parent, child]);

        Assert.True(store.TryGetParent(11, out var parentId));
        Assert.Equal(10u, parentId);
        Assert.Single(store.GetChildren(10));
        Assert.Equal(11u, store.GetChildren(10)[0]);
    }

    [Fact]
    public void PropertyStore_ShouldNotMarkDirtyForSameValue()
    {
        var store = new PropertyStore();
        var metadata = new RenderPropertyMetadata(1, DirtyFlags.Render);

        var changed = store.SetFloat(7, 1, 12f, metadata);
        Assert.True(changed);
        Assert.True(store.HasDirty(DirtyFlags.Render));

        store.ClearDirty(DirtyFlags.All);
        var unchanged = store.SetFloat(7, 1, 12f, metadata);

        Assert.False(unchanged);
        Assert.False(store.HasDirty(DirtyFlags.Render));
    }

    [Fact]
    public void ReactiveGraph_ShouldPropagateToParentWithMergedFlags()
    {
        var graph = new ReactiveGraph();
        graph.AddEdge(fromNodeId: 2, toNodeId: 1, dirtyFlags: DirtyFlags.Layout | DirtyFlags.Render);
        graph.Invalidate(nodeId: 2, dirtyFlags: DirtyFlags.Render);

        var result = new Dictionary<uint, DirtyFlags>();
        graph.Propagate((nodeId, flags) =>
        {
            if (result.TryGetValue(nodeId, out var existing))
            {
                result[nodeId] = existing | flags;
            }
            else
            {
                result[nodeId] = flags;
            }
        });

        Assert.Equal(DirtyFlags.Render, result[2]);
        Assert.Equal(DirtyFlags.Layout | DirtyFlags.Render, result[1]);
    }
}
