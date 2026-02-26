using Jalium.UI.Gpu.Pipeline;
using Jalium.UI.Gpu.Resources;

namespace Jalium.UI.Gpu.RenderGraph;

/// <summary>
/// 渲染图 - DAG 结构的渲染 Pass 管理器
/// 负责自动化资源屏障插入、Pass 排序、资源别名
/// </summary>
public sealed class RenderGraph
{
    private readonly List<RenderPass> _passes = new();
    private readonly Dictionary<uint, RenderGraphResource> _resources = new();
    private uint _nextResourceId;

    /// <summary>
    /// 所有 Pass
    /// </summary>
    public IReadOnlyList<RenderPass> Passes => _passes;

    /// <summary>
    /// 所有资源
    /// </summary>
    public IReadOnlyDictionary<uint, RenderGraphResource> Resources => _resources;

    /// <summary>
    /// 注册外部资源（Backbuffer、已存在的纹理等）
    /// </summary>
    public uint ImportResource(string name, RenderGraphResourceType type, TextureHandle? texture = null, nint buffer = 0)
    {
        var id = _nextResourceId++;
        var resource = new RenderGraphResource(id, name, type)
        {
            TextureHandle = texture,
            BufferHandle = buffer,
            IsTransient = false
        };
        _resources[id] = resource;
        return id;
    }

    /// <summary>
    /// 创建瞬态资源（RenderGraph 管理生命周期，可以别名复用）
    /// </summary>
    public uint CreateTransientTexture(string name, int width, int height, TextureFormat format, RenderGraphResourceType type = RenderGraphResourceType.RenderTarget)
    {
        var id = _nextResourceId++;
        var resource = new RenderGraphResource(id, name, type)
        {
            IsTransient = true,
            Width = width,
            Height = height,
            Format = format
        };
        _resources[id] = resource;
        return id;
    }

    /// <summary>
    /// 添加渲染 Pass
    /// </summary>
    public RenderPassBuilder AddPass(string name, RenderPassType type)
    {
        var pass = new RenderPass(name, type)
        {
            Index = _passes.Count
        };
        _passes.Add(pass);
        return new RenderPassBuilder(pass, this);
    }

    /// <summary>
    /// 编译渲染图：拓扑排序 + 自动插入资源屏障 + 资源别名
    /// </summary>
    public CompiledRenderGraph Compile()
    {
        // 1. 拓扑排序（基于资源读写依赖）
        var sortedPasses = TopologicalSort();

        // 2. 为每个 Pass 插入资源屏障
        InsertBarriers(sortedPasses);

        // 3. 分析瞬态资源的生命周期范围（用于别名）
        var resourceLifetimes = AnalyzeResourceLifetimes(sortedPasses);

        return new CompiledRenderGraph(sortedPasses, _resources, resourceLifetimes);
    }

    /// <summary>
    /// 拓扑排序 - 确保写入资源的 Pass 在读取该资源的 Pass 之前执行
    /// </summary>
    private List<RenderPass> TopologicalSort()
    {
        // 构建依赖图
        var inDegree = new int[_passes.Count];
        var adjacency = new List<int>[_passes.Count];
        for (int i = 0; i < _passes.Count; i++)
            adjacency[i] = new List<int>();

        // 对于每个资源，找到写入它的 Pass 和读取它的 Pass
        var resourceWriters = new Dictionary<uint, List<int>>();
        var resourceReaders = new Dictionary<uint, List<int>>();

        for (int i = 0; i < _passes.Count; i++)
        {
            foreach (var write in _passes[i].Writes)
            {
                if (!resourceWriters.TryGetValue(write.ResourceId, out var writers))
                {
                    writers = new List<int>();
                    resourceWriters[write.ResourceId] = writers;
                }
                writers.Add(i);
            }

            foreach (var read in _passes[i].Reads)
            {
                if (!resourceReaders.TryGetValue(read.ResourceId, out var readers))
                {
                    readers = new List<int>();
                    resourceReaders[read.ResourceId] = readers;
                }
                readers.Add(i);
            }
        }

        // 写入 → 读取 = 依赖边
        foreach (var (resourceId, readers) in resourceReaders)
        {
            if (!resourceWriters.TryGetValue(resourceId, out var writers))
                continue;

            foreach (var writer in writers)
            {
                foreach (var reader in readers)
                {
                    if (writer != reader)
                    {
                        adjacency[writer].Add(reader);
                        inDegree[reader]++;
                    }
                }
            }
        }

        // BFS 拓扑排序
        var queue = new Queue<int>();
        for (int i = 0; i < _passes.Count; i++)
        {
            if (inDegree[i] == 0)
                queue.Enqueue(i);
        }

        var sorted = new List<RenderPass>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(_passes[current]);

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        // 如果有环或未排序完全，保持原始顺序
        if (sorted.Count < _passes.Count)
        {
            return new List<RenderPass>(_passes);
        }

        return sorted;
    }

    /// <summary>
    /// 自动插入资源屏障
    /// </summary>
    private void InsertBarriers(List<RenderPass> sortedPasses)
    {
        // 追踪每个资源的当前状态
        var currentStates = new Dictionary<uint, ResourceState>();
        foreach (var res in _resources.Values)
        {
            currentStates[res.Id] = res.CurrentState;
        }

        foreach (var pass in sortedPasses)
        {
            pass.Barriers.Clear();

            // 处理读取资源的屏障
            foreach (var read in pass.Reads)
            {
                if (currentStates.TryGetValue(read.ResourceId, out var currentState) &&
                    currentState != read.RequiredState)
                {
                    pass.Barriers.Add(new ResourceBarrier(
                        read.ResourceId, currentState, read.RequiredState));
                    currentStates[read.ResourceId] = read.RequiredState;
                }
            }

            // 处理写入资源的屏障
            foreach (var write in pass.Writes)
            {
                if (currentStates.TryGetValue(write.ResourceId, out var currentState) &&
                    currentState != write.RequiredState)
                {
                    pass.Barriers.Add(new ResourceBarrier(
                        write.ResourceId, currentState, write.RequiredState));
                    currentStates[write.ResourceId] = write.RequiredState;
                }
            }
        }
    }

    /// <summary>
    /// 分析资源生命周期（首次使用 Pass ~ 最后使用 Pass）
    /// </summary>
    private Dictionary<uint, (int FirstPass, int LastPass)> AnalyzeResourceLifetimes(List<RenderPass> sortedPasses)
    {
        var lifetimes = new Dictionary<uint, (int First, int Last)>();

        for (int i = 0; i < sortedPasses.Count; i++)
        {
            var pass = sortedPasses[i];

            void Track(uint resourceId)
            {
                if (lifetimes.TryGetValue(resourceId, out var existing))
                {
                    lifetimes[resourceId] = (existing.First, i);
                }
                else
                {
                    lifetimes[resourceId] = (i, i);
                }
            }

            foreach (var read in pass.Reads) Track(read.ResourceId);
            foreach (var write in pass.Writes) Track(write.ResourceId);
        }

        return lifetimes;
    }
}

/// <summary>
/// 编译后的渲染图 - 可直接执行
/// </summary>
public sealed class CompiledRenderGraph
{
    /// <summary>
    /// 排序后的 Pass 列表
    /// </summary>
    public IReadOnlyList<RenderPass> SortedPasses { get; }

    /// <summary>
    /// 资源映射
    /// </summary>
    public IReadOnlyDictionary<uint, RenderGraphResource> Resources { get; }

    /// <summary>
    /// 资源生命周期
    /// </summary>
    public IReadOnlyDictionary<uint, (int FirstPass, int LastPass)> ResourceLifetimes { get; }

    public CompiledRenderGraph(
        List<RenderPass> sortedPasses,
        Dictionary<uint, RenderGraphResource> resources,
        Dictionary<uint, (int, int)> resourceLifetimes)
    {
        SortedPasses = sortedPasses;
        Resources = resources;
        ResourceLifetimes = resourceLifetimes;
    }

    /// <summary>
    /// 在指定 backend 上执行编译后的渲染图
    /// </summary>
    public void Execute(IRenderBackendEx backend, Commands.GpuCommandBuffer commandBuffer)
    {
        foreach (var pass in SortedPasses)
        {
            // 发出资源屏障
            foreach (var barrier in pass.Barriers)
            {
                if (Resources.TryGetValue(barrier.ResourceId, out var res))
                {
                    commandBuffer.ResourceBarrier(
                        res.BufferHandle != 0 ? res.BufferHandle : nint.Zero,
                        barrier.StateBefore,
                        barrier.StateAfter);
                }
            }

            // 根据 Pass 类型执行
            switch (pass.Type)
            {
                case RenderPassType.Geometry:
                    ExecuteGeometryPass(pass, backend, commandBuffer);
                    break;
                case RenderPassType.Compute:
                    ExecuteComputePass(pass, backend, commandBuffer);
                    break;
                case RenderPassType.Composite:
                    ExecuteCompositePass(pass, backend, commandBuffer);
                    break;
                case RenderPassType.Copy:
                    ExecuteCopyPass(pass, backend, commandBuffer);
                    break;
                case RenderPassType.Present:
                    // Present 由 EndFrame 处理
                    break;
            }
        }
    }

    private void ExecuteGeometryPass(RenderPass pass, IRenderBackendEx backend, Commands.GpuCommandBuffer cmd)
    {
        // 设置渲染目标
        if (pass.RenderTargetId.HasValue)
        {
            cmd.SetRenderTarget(pass.RenderTargetId.Value);
        }

        // 清除
        if (pass.ClearColor.HasValue)
        {
            var c = pass.ClearColor.Value;
            cmd.ClearRenderTarget(pass.RenderTargetId ?? 0, c.R, c.G, c.B, c.A);
        }

        // 设置视口和裁剪
        var vp = pass.ViewportSize;
        if (vp.Width > 0 && vp.Height > 0)
        {
            cmd.SetViewport(0, 0, vp.Width, vp.Height);
            cmd.SetScissor(0, 0, vp.Width, vp.Height);
        }

        // 设置管线状态
        if (pass.PipelineState.HasValue)
        {
            cmd.SetPipelineState(pass.PipelineState.Value);
        }

        // 执行绘制命令
        foreach (var command in pass.Commands)
        {
            switch (command)
            {
                case DrawRectBatchCommand rect:
                    cmd.DrawIndexedInstanced(6, rect.InstanceCount, 0, 0, rect.InstanceBufferOffset);
                    break;
                case DrawTextBatchCommand text:
                    cmd.DrawIndexedInstanced(6, text.GlyphCount, 0, 0, text.InstanceBufferOffset);
                    break;
                case DrawImageBatchCommand image:
                    cmd.DrawIndexedInstanced(6, image.InstanceCount, 0, 0, image.InstanceBufferOffset);
                    break;
                case SetClipCommand clip:
                    cmd.SetScissor(
                        (int)clip.ClipRect.X, (int)clip.ClipRect.Y,
                        (int)clip.ClipRect.Width, (int)clip.ClipRect.Height);
                    break;
            }
        }
    }

    private static void ExecuteComputePass(RenderPass pass, IRenderBackendEx backend, Commands.GpuCommandBuffer cmd)
    {
        if (pass.PipelineState.HasValue)
        {
            cmd.SetPipelineState(pass.PipelineState.Value);
        }

        var d = pass.DispatchSize;
        if (d.X > 0 && d.Y > 0)
        {
            cmd.Dispatch(d.X, d.Y, d.Z);
        }
    }

    private static void ExecuteCompositePass(RenderPass pass, IRenderBackendEx backend, Commands.GpuCommandBuffer cmd)
    {
        if (pass.PipelineState.HasValue)
        {
            cmd.SetPipelineState(pass.PipelineState.Value);
        }

        // 全屏三角形绘制（3 个顶点，由 SV_VertexID 生成）
        cmd.Draw(3, 1, 0, 0);
    }

    private static void ExecuteCopyPass(RenderPass pass, IRenderBackendEx backend, Commands.GpuCommandBuffer cmd)
    {
        foreach (var command in pass.Commands)
        {
            if (command is CaptureBackdropCommand capture)
            {
                cmd.CopyTextureRegion(
                    capture.Region,
                    capture.TargetTextureIndex);
            }
        }
    }
}
