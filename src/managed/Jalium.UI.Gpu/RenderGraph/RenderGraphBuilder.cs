using Jalium.UI.Gpu.Pipeline;

namespace Jalium.UI.Gpu.RenderGraph;

/// <summary>
/// 渲染图构建器 - 从 CompiledUIBundle 的 DrawCommand[] 构建 RenderGraph
/// 将扁平的命令序列转换为多 Pass 的渲染图结构
/// </summary>
public sealed class RenderGraphBuilder
{
    /// <summary>
    /// 从 CompiledUIBundle 构建渲染图
    /// </summary>
    public RenderGraph Build(CompiledUIBundle bundle, int viewportWidth, int viewportHeight)
    {
        var graph = new RenderGraph();

        // 注册 Backbuffer
        var backbufferId = graph.ImportResource("Backbuffer", RenderGraphResourceType.Backbuffer);

        // 分析命令序列，提取需要的瞬态资源
        var analysis = AnalyzeCommands(bundle.DrawCommands);

        // 创建瞬态纹理
        var transientTextures = new Dictionary<uint, uint>();
        foreach (var texId in analysis.RequiredRenderTargets)
        {
            var rtId = graph.CreateTransientTexture(
                $"RT_{texId}", viewportWidth, viewportHeight,
                TextureFormat.RGBA8, RenderGraphResourceType.RenderTarget);
            transientTextures[texId] = rtId;
        }

        foreach (var texId in analysis.RequiredReadWriteTextures)
        {
            var rwId = graph.CreateTransientTexture(
                $"RW_{texId}", viewportWidth, viewportHeight,
                TextureFormat.RGBA8, RenderGraphResourceType.ReadWriteTexture);
            transientTextures[texId] = rwId;
        }

        // 构建 Pass
        BuildGeometryPasses(graph, bundle, backbufferId, transientTextures, viewportWidth, viewportHeight, analysis);
        BuildEffectPasses(graph, bundle, transientTextures, viewportWidth, viewportHeight, analysis);
        BuildCompositePasses(graph, bundle, backbufferId, transientTextures, viewportWidth, viewportHeight, analysis);

        // Present Pass
        graph.AddPass("Present", RenderPassType.Present)
            .Read(backbufferId, ResourceState.Present)
            .Build();

        return graph;
    }

    /// <summary>
    /// 分析命令序列 - 提取 Pass 结构信息
    /// </summary>
    private static CommandAnalysis AnalyzeCommands(DrawCommand[] commands)
    {
        var analysis = new CommandAnalysis();

        foreach (var cmd in commands)
        {
            switch (cmd)
            {
                case CaptureBackdropCommand capture:
                    analysis.HasBackdropCapture = true;
                    analysis.RequiredRenderTargets.Add(capture.TargetTextureIndex);
                    break;

                case ApplyBackdropFilterCommand filter:
                    analysis.HasBackdropFilter = true;
                    analysis.RequiredReadWriteTextures.Add(filter.OutputTextureIndex);
                    if (filter.BackdropTextureIndex > 0)
                        analysis.RequiredRenderTargets.Add(filter.BackdropTextureIndex);
                    break;

                case ApplyEffectCommand effect:
                    analysis.HasEffects = true;
                    analysis.RequiredRenderTargets.Add(effect.SourceTextureIndex);
                    analysis.RequiredRenderTargets.Add(effect.DestTextureIndex);
                    break;

                case CompositeLayerCommand composite:
                    analysis.HasCompositing = true;
                    analysis.RequiredRenderTargets.Add(composite.SourceTextureIndex);
                    break;

                case DrawRectBatchCommand:
                    analysis.OpaqueRectCommands.Add(cmd);
                    break;

                case DrawTextBatchCommand:
                    analysis.TextCommands.Add(cmd);
                    break;

                case DrawImageBatchCommand:
                    analysis.ImageCommands.Add(cmd);
                    break;

                case DrawPathCommand:
                    analysis.PathCommands.Add(cmd);
                    break;

                case SetClipCommand:
                    // 裁剪命令附加到下一个绘制命令
                    analysis.ClipCommands.Add(cmd);
                    break;
            }
        }

        return analysis;
    }

    /// <summary>
    /// 构建几何绘制 Pass
    /// </summary>
    private static void BuildGeometryPasses(
        RenderGraph graph,
        CompiledUIBundle bundle,
        uint backbufferId,
        Dictionary<uint, uint> transientTextures,
        int vpWidth, int vpHeight,
        CommandAnalysis analysis)
    {
        // 不透明矩形 Pass（前→后，利用深度测试减少 overdraw）
        if (analysis.OpaqueRectCommands.Count > 0)
        {
            graph.AddPass("Geometry.OpaqueRects", RenderPassType.Geometry)
                .SetRenderTarget(backbufferId, 0, 0, 0, 1) // 清除为透明黑
                .SetViewport(vpWidth, vpHeight)
                .SetPipeline(UIPipelines.TransparentRect) // UI 通常需要 alpha blend
                .AddCommands(analysis.ClipCommands.Concat(analysis.OpaqueRectCommands))
                .Build();
        }

        // 文本 Pass
        if (analysis.TextCommands.Count > 0)
        {
            graph.AddPass("Geometry.Text", RenderPassType.Geometry)
                .Write(backbufferId, ResourceState.RenderTarget)
                .SetViewport(vpWidth, vpHeight)
                .SetPipeline(UIPipelines.Text)
                .AddCommands(analysis.TextCommands)
                .Build();
        }

        // 图像 Pass
        if (analysis.ImageCommands.Count > 0)
        {
            graph.AddPass("Geometry.Images", RenderPassType.Geometry)
                .Write(backbufferId, ResourceState.RenderTarget)
                .SetViewport(vpWidth, vpHeight)
                .SetPipeline(UIPipelines.Image)
                .AddCommands(analysis.ImageCommands)
                .Build();
        }

        // 路径 Pass
        if (analysis.PathCommands.Count > 0)
        {
            graph.AddPass("Geometry.Paths", RenderPassType.Geometry)
                .Write(backbufferId, ResourceState.RenderTarget)
                .SetViewport(vpWidth, vpHeight)
                .SetPipeline(UIPipelines.Path)
                .AddCommands(analysis.PathCommands)
                .Build();
        }
    }

    /// <summary>
    /// 构建效果 Pass（Backdrop Capture + Blur + Filter）
    /// </summary>
    private static void BuildEffectPasses(
        RenderGraph graph,
        CompiledUIBundle bundle,
        Dictionary<uint, uint> transientTextures,
        int vpWidth, int vpHeight,
        CommandAnalysis analysis)
    {
        if (!analysis.HasBackdropCapture && !analysis.HasEffects && !analysis.HasBackdropFilter)
            return;

        // Backdrop 捕获 Pass
        if (analysis.HasBackdropCapture)
        {
            foreach (var cmd in bundle.DrawCommands)
            {
                if (cmd is CaptureBackdropCommand capture &&
                    transientTextures.TryGetValue(capture.TargetTextureIndex, out var targetId))
                {
                    graph.AddPass($"Capture.{capture.TargetTextureIndex}", RenderPassType.Copy)
                        .Read(0, ResourceState.CopySource) // 从 backbuffer 读
                        .Write(targetId, ResourceState.CopyDest)
                        .AddCommand(cmd)
                        .Build();
                }
            }
        }

        // 模糊 Pass（如果有 backdrop filter 需要模糊）
        foreach (var cmd in bundle.DrawCommands)
        {
            if (cmd is ApplyBackdropFilterCommand filter && filter.Params.NeedsBlur)
            {
                // 水平模糊
                if (transientTextures.TryGetValue(filter.BackdropTextureIndex, out var sourceId) &&
                    transientTextures.TryGetValue(filter.OutputTextureIndex, out var outputId))
                {
                    var tempId = graph.CreateTransientTexture(
                        $"BlurTemp_{filter.OutputTextureIndex}",
                        vpWidth, vpHeight, TextureFormat.RGBA8,
                        RenderGraphResourceType.ReadWriteTexture);

                    // 水平 blur
                    graph.AddPass($"Blur.H_{filter.OutputTextureIndex}", RenderPassType.Compute)
                        .Read(sourceId, ResourceState.ShaderResource)
                        .Write(tempId, ResourceState.UnorderedAccess)
                        .SetPipeline(UIPipelines.GaussianBlurH)
                        .SetDispatch((uint)((vpWidth + 255) / 256), (uint)vpHeight)
                        .Build();

                    // 垂直 blur
                    graph.AddPass($"Blur.V_{filter.OutputTextureIndex}", RenderPassType.Compute)
                        .Read(tempId, ResourceState.ShaderResource)
                        .Write(outputId, ResourceState.UnorderedAccess)
                        .SetPipeline(UIPipelines.GaussianBlurV)
                        .SetDispatch((uint)vpWidth, (uint)((vpHeight + 255) / 256))
                        .Build();
                }
            }
        }

        // Backdrop Filter Pass
        foreach (var cmd in bundle.DrawCommands)
        {
            if (cmd is ApplyBackdropFilterCommand filter && filter.Params.NeedsColorAdjustment)
            {
                if (transientTextures.TryGetValue(filter.OutputTextureIndex, out var outputId))
                {
                    graph.AddPass($"BackdropFilter.{filter.OutputTextureIndex}", RenderPassType.Compute)
                        .Read(outputId, ResourceState.ShaderResource) // 从模糊结果读
                        .Write(outputId, ResourceState.UnorderedAccess) // 就地修改
                        .SetPipeline(UIPipelines.BackdropFilter)
                        .SetDispatch(
                            (uint)((vpWidth + 15) / 16),
                            (uint)((vpHeight + 15) / 16))
                        .AddCommand(cmd)
                        .Build();
                }
            }
        }
    }

    /// <summary>
    /// 构建合成 Pass
    /// </summary>
    private static void BuildCompositePasses(
        RenderGraph graph,
        CompiledUIBundle bundle,
        uint backbufferId,
        Dictionary<uint, uint> transientTextures,
        int vpWidth, int vpHeight,
        CommandAnalysis analysis)
    {
        if (!analysis.HasCompositing)
            return;

        foreach (var cmd in bundle.DrawCommands)
        {
            if (cmd is CompositeLayerCommand composite &&
                transientTextures.TryGetValue(composite.SourceTextureIndex, out var sourceId))
            {
                graph.AddPass($"Composite.{composite.SourceTextureIndex}", RenderPassType.Composite)
                    .Read(sourceId, ResourceState.ShaderResource)
                    .Write(backbufferId, ResourceState.RenderTarget)
                    .SetViewport(vpWidth, vpHeight)
                    .SetPipeline(UIPipelines.Composite)
                    .AddCommand(cmd)
                    .Build();
            }
        }
    }
}

/// <summary>
/// 命令序列分析结果
/// </summary>
internal sealed class CommandAnalysis
{
    public bool HasBackdropCapture;
    public bool HasBackdropFilter;
    public bool HasEffects;
    public bool HasCompositing;

    public HashSet<uint> RequiredRenderTargets { get; } = new();
    public HashSet<uint> RequiredReadWriteTextures { get; } = new();

    public List<DrawCommand> OpaqueRectCommands { get; } = new();
    public List<DrawCommand> TextCommands { get; } = new();
    public List<DrawCommand> ImageCommands { get; } = new();
    public List<DrawCommand> PathCommands { get; } = new();
    public List<DrawCommand> ClipCommands { get; } = new();
}
