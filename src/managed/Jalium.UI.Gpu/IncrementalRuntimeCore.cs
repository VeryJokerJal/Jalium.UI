namespace Jalium.UI.Gpu;

[Flags]
public enum DirtyFlags : byte
{
    None = 0,
    Layout = 1 << 0,
    Render = 1 << 1,
    Transform = 1 << 2,
    Clip = 1 << 3,
    Resource = 1 << 4,
    Input = 1 << 5,
    All = Layout | Render | Transform | Clip | Resource | Input
}

public readonly record struct RenderPropertyMetadata(
    ushort PropertyId,
    DirtyFlags DirtyFlags,
    float DefaultValue = 0f);

public sealed class NodeStore
{
    private static readonly List<uint> EmptyChildren = [];
    private readonly Dictionary<uint, SceneNode> _nodes = new();
    private readonly Dictionary<uint, uint> _parents = new();
    private readonly Dictionary<uint, List<uint>> _children = new();

    public IReadOnlyCollection<SceneNode> Nodes => _nodes.Values;

    public void Clear()
    {
        _nodes.Clear();
        _parents.Clear();
        _children.Clear();
    }

    public void Load(IReadOnlyList<SceneNode> nodes)
    {
        Clear();

        foreach (var node in nodes)
        {
            _nodes[node.Id] = node;

            if (node.ParentId == 0)
            {
                continue;
            }

            _parents[node.Id] = node.ParentId;

            if (!_children.TryGetValue(node.ParentId, out var childList))
            {
                childList = [];
                _children[node.ParentId] = childList;
            }

            childList.Add(node.Id);
        }
    }

    public bool TryGet(uint nodeId, out SceneNode? node)
    {
        if (_nodes.TryGetValue(nodeId, out var value))
        {
            node = value;
            return true;
        }

        node = null;
        return false;
    }

    public bool TryGetParent(uint nodeId, out uint parentId) => _parents.TryGetValue(nodeId, out parentId);

    public IReadOnlyList<uint> GetChildren(uint nodeId) =>
        _children.TryGetValue(nodeId, out var childList) ? childList : EmptyChildren;
}

public sealed class PropertyStore
{
    private readonly Dictionary<(uint NodeId, ushort PropertyId), float> _floatValues = new();
    private readonly Dictionary<uint, DirtyFlags> _dirtyFlagsByNode = new();
    private readonly HashSet<uint> _reusableDirtySet = new();

    public void Clear()
    {
        _floatValues.Clear();
        _dirtyFlagsByNode.Clear();
    }

    /// <summary>
    /// 浮点属性变化的最小阈值。小于此值的变化会被忽略以避免不必要的重绘。
    /// </summary>
    private const float FloatChangeEpsilon = 1e-4f;

    public bool SetFloat(uint nodeId, ushort propertyId, float value, in RenderPropertyMetadata metadata)
    {
        var key = (nodeId, propertyId);
        if (_floatValues.TryGetValue(key, out var oldValue) && Math.Abs(oldValue - value) < FloatChangeEpsilon)
        {
            return false;
        }

        _floatValues[key] = value;
        MarkDirty(nodeId, metadata.DirtyFlags);
        return true;
    }

    public bool TryGetFloat(uint nodeId, ushort propertyId, out float value) =>
        _floatValues.TryGetValue((nodeId, propertyId), out value);

    public void MarkDirty(uint nodeId, DirtyFlags flags)
    {
        if (flags == DirtyFlags.None)
        {
            return;
        }

        _dirtyFlagsByNode.TryGetValue(nodeId, out var existing);
        _dirtyFlagsByNode[nodeId] = existing | flags;
    }

    public bool HasDirty(DirtyFlags mask)
    {
        if (mask == DirtyFlags.None)
        {
            return false;
        }

        foreach (var flags in _dirtyFlagsByNode.Values)
        {
            if ((flags & mask) != 0)
            {
                return true;
            }
        }

        return false;
    }

    public HashSet<uint> GetDirtyNodes(DirtyFlags mask)
    {
        _reusableDirtySet.Clear();
        if (mask == DirtyFlags.None)
        {
            return _reusableDirtySet;
        }

        foreach (var (nodeId, flags) in _dirtyFlagsByNode)
        {
            if ((flags & mask) != 0)
            {
                _reusableDirtySet.Add(nodeId);
            }
        }

        return _reusableDirtySet;
    }

    public void ClearDirty(DirtyFlags mask)
    {
        if (mask == DirtyFlags.None)
        {
            return;
        }

        if (mask == DirtyFlags.All)
        {
            // Fast path: clearing all flags means just clear the dictionary
            _dirtyFlagsByNode.Clear();
            return;
        }

        // Use reusable set to collect keys to remove, avoiding Keys.ToArray() allocation
        _reusableDirtySet.Clear();
        foreach (var (nodeId, flags) in _dirtyFlagsByNode)
        {
            var next = flags & ~mask;
            if (next == DirtyFlags.None)
            {
                _reusableDirtySet.Add(nodeId);
            }
            else
            {
                _dirtyFlagsByNode[nodeId] = next;
            }
        }

        foreach (var nodeId in _reusableDirtySet)
        {
            _dirtyFlagsByNode.Remove(nodeId);
        }
    }
}

public sealed class ResourceStore
{
    private readonly Dictionary<uint, object?> _resources = new();

    public int Count => _resources.Count;

    public void Clear() => _resources.Clear();

    public void Set(uint resourceId, object? resource) => _resources[resourceId] = resource;

    public bool TryGet<T>(uint resourceId, out T? resource) where T : class
    {
        if (_resources.TryGetValue(resourceId, out var value) && value is T typedValue)
        {
            resource = typedValue;
            return true;
        }

        resource = null;
        return false;
    }
}

public sealed class ReactiveGraph
{
    private readonly Dictionary<uint, List<ReactiveEdge>> _edges = new();
    private readonly Queue<ReactiveInvalidation> _pending = new();
    private readonly HashSet<ReactiveInvalidation> _pendingLookup = [];
    private readonly object _pendingLock = new();

    public bool HasPendingInvalidations
    {
        get { lock (_pendingLock) { return _pending.Count > 0; } }
    }

    public void Clear()
    {
        _edges.Clear();
        lock (_pendingLock)
        {
            _pending.Clear();
            _pendingLookup.Clear();
        }
    }

    public void AddEdge(uint fromNodeId, uint toNodeId, DirtyFlags dirtyFlags)
    {
        if (dirtyFlags == DirtyFlags.None)
        {
            return;
        }

        if (!_edges.TryGetValue(fromNodeId, out var edgeList))
        {
            edgeList = [];
            _edges[fromNodeId] = edgeList;
        }

        var newEdge = new ReactiveEdge(toNodeId, dirtyFlags);
        if (!edgeList.Contains(newEdge))
        {
            edgeList.Add(newEdge);
        }
    }

    public void Invalidate(uint nodeId, DirtyFlags dirtyFlags)
    {
        if (dirtyFlags == DirtyFlags.None)
        {
            return;
        }

        var invalidation = new ReactiveInvalidation(nodeId, dirtyFlags);
        lock (_pendingLock)
        {
            if (_pendingLookup.Add(invalidation))
            {
                _pending.Enqueue(invalidation);
            }
        }
    }

    public void Propagate(Action<uint, DirtyFlags> onVisit)
    {
        ArgumentNullException.ThrowIfNull(onVisit);

        var visited = new HashSet<ReactiveInvalidation>();

        lock (_pendingLock)
        {
            while (_pending.Count > 0)
            {
                var current = _pending.Dequeue();
                _pendingLookup.Remove(current);

                if (!visited.Add(current))
                {
                    continue;
                }

                onVisit(current.NodeId, current.DirtyFlags);

                if (!_edges.TryGetValue(current.NodeId, out var edges))
                {
                    continue;
                }

                foreach (var edge in edges)
                {
                    var nextFlags = current.DirtyFlags | edge.DirtyFlags;
                    var next = new ReactiveInvalidation(edge.TargetNodeId, nextFlags);
                    if (_pendingLookup.Add(next))
                    {
                        _pending.Enqueue(next);
                    }
                }
            }
        }
    }

    private readonly record struct ReactiveEdge(uint TargetNodeId, DirtyFlags DirtyFlags);
    private readonly record struct ReactiveInvalidation(uint NodeId, DirtyFlags DirtyFlags);
}

public enum DisplayCommandType : byte
{
    PushClip,
    PopClip,
    PushTransform,
    PopTransform,
    DrawRect,
    DrawText,
    DrawImage,
    NativeCommand
}

public readonly struct DisplayCommand
{
    public DisplayCommandType Type { get; }
    public uint NodeId { get; }
    public Rect Rect { get; }
    public uint InstanceOffset { get; }
    public uint InstanceCount { get; }
    public uint ResourceIndex { get; }
    public uint TransformIndex { get; }
    public DrawCommand? NativeCommand { get; }

    private DisplayCommand(
        DisplayCommandType type,
        uint nodeId = 0,
        Rect rect = default,
        uint instanceOffset = 0,
        uint instanceCount = 0,
        uint resourceIndex = 0,
        uint transformIndex = 0,
        DrawCommand? nativeCommand = null)
    {
        Type = type;
        NodeId = nodeId;
        Rect = rect;
        InstanceOffset = instanceOffset;
        InstanceCount = instanceCount;
        ResourceIndex = resourceIndex;
        TransformIndex = transformIndex;
        NativeCommand = nativeCommand;
    }

    public static DisplayCommand PushClip(Rect clipRect) =>
        new(DisplayCommandType.PushClip, rect: clipRect);

    public static DisplayCommand PopClip() =>
        new(DisplayCommandType.PopClip);

    public static DisplayCommand PushTransform(uint transformIndex) =>
        new(DisplayCommandType.PushTransform, transformIndex: transformIndex);

    public static DisplayCommand PopTransform() =>
        new(DisplayCommandType.PopTransform);

    public static DisplayCommand DrawRect(uint nodeId, uint instanceOffset, uint instanceCount, uint textureIndex) =>
        new(
            DisplayCommandType.DrawRect,
            nodeId: nodeId,
            instanceOffset: instanceOffset,
            instanceCount: instanceCount,
            resourceIndex: textureIndex);

    public static DisplayCommand DrawText(uint nodeId, uint instanceOffset, uint glyphCount, uint glyphAtlasIndex) =>
        new(
            DisplayCommandType.DrawText,
            nodeId: nodeId,
            instanceOffset: instanceOffset,
            instanceCount: glyphCount,
            resourceIndex: glyphAtlasIndex);

    public static DisplayCommand DrawImage(uint nodeId, uint instanceOffset, uint instanceCount, uint textureIndex) =>
        new(
            DisplayCommandType.DrawImage,
            nodeId: nodeId,
            instanceOffset: instanceOffset,
            instanceCount: instanceCount,
            resourceIndex: textureIndex);

    public static DisplayCommand Native(DrawCommand command) =>
        new(DisplayCommandType.NativeCommand, nativeCommand: command);
}

public sealed class DisplayList
{
    private readonly List<DisplayCommand> _commands = [];

    public IReadOnlyList<DisplayCommand> Commands => _commands;

    public int Count => _commands.Count;

    public void Clear() => _commands.Clear();

    public void Add(in DisplayCommand command) => _commands.Add(command);
}
