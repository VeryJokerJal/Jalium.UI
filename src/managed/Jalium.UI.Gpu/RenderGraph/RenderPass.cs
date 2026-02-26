using Jalium.UI.Gpu.Pipeline;
using Jalium.UI.Gpu.Resources;

namespace Jalium.UI.Gpu.RenderGraph;

/// <summary>
/// 渲染 Pass 类型
/// </summary>
public enum RenderPassType : byte
{
    /// <summary>
    /// 几何绘制 Pass（矩形、文本、图像）
    /// </summary>
    Geometry,

    /// <summary>
    /// 后处理效果 Pass（模糊、颜色矩阵）
    /// </summary>
    Effect,

    /// <summary>
    /// 图层合成 Pass
    /// </summary>
    Composite,

    /// <summary>
    /// Compute Shader Pass
    /// </summary>
    Compute,

    /// <summary>
    /// 复制操作 Pass（捕获 backdrop）
    /// </summary>
    Copy,

    /// <summary>
    /// Present Pass
    /// </summary>
    Present
}

/// <summary>
/// 资源状态（匹配 D3D12_RESOURCE_STATES）
/// </summary>
public enum ResourceState : byte
{
    Common,
    ShaderResource,
    RenderTarget,
    UnorderedAccess,
    CopySource,
    CopyDest,
    Present,
    DepthWrite,
    DepthRead
}

/// <summary>
/// 资源访问声明 - 描述 Pass 对资源的读写需求
/// </summary>
public readonly struct ResourceAccess
{
    /// <summary>
    /// 资源 ID（由 RenderGraph 分配）
    /// </summary>
    public readonly uint ResourceId;

    /// <summary>
    /// 需要的资源状态
    /// </summary>
    public readonly ResourceState RequiredState;

    public ResourceAccess(uint resourceId, ResourceState requiredState)
    {
        ResourceId = resourceId;
        RequiredState = requiredState;
    }
}

/// <summary>
/// 资源屏障
/// </summary>
public readonly struct ResourceBarrier
{
    /// <summary>
    /// 资源 ID
    /// </summary>
    public readonly uint ResourceId;

    /// <summary>
    /// 转换前状态
    /// </summary>
    public readonly ResourceState StateBefore;

    /// <summary>
    /// 转换后状态
    /// </summary>
    public readonly ResourceState StateAfter;

    public ResourceBarrier(uint resourceId, ResourceState before, ResourceState after)
    {
        ResourceId = resourceId;
        StateBefore = before;
        StateAfter = after;
    }
}

/// <summary>
/// 渲染 Pass 定义
/// </summary>
public sealed class RenderPass
{
    /// <summary>
    /// Pass 名称（调试用）
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Pass 类型
    /// </summary>
    public RenderPassType Type { get; }

    /// <summary>
    /// Pass 索引（在 RenderGraph 中的位置）
    /// </summary>
    public int Index { get; internal set; }

    /// <summary>
    /// 资源读取声明
    /// </summary>
    public List<ResourceAccess> Reads { get; } = new();

    /// <summary>
    /// 资源写入声明
    /// </summary>
    public List<ResourceAccess> Writes { get; } = new();

    /// <summary>
    /// 此 Pass 执行前需要的资源屏障
    /// </summary>
    public List<ResourceBarrier> Barriers { get; } = new();

    /// <summary>
    /// 此 Pass 包含的绘制命令
    /// </summary>
    public List<DrawCommand> Commands { get; } = new();

    /// <summary>
    /// 管线状态（Geometry/Composite Pass 用）
    /// </summary>
    public PipelineStateDesc? PipelineState { get; set; }

    /// <summary>
    /// 渲染目标资源 ID
    /// </summary>
    public uint? RenderTargetId { get; set; }

    /// <summary>
    /// 视口尺寸
    /// </summary>
    public (int Width, int Height) ViewportSize { get; set; }

    /// <summary>
    /// 清除颜色（如果需要清除 RT）
    /// </summary>
    public (float R, float G, float B, float A)? ClearColor { get; set; }

    /// <summary>
    /// Compute dispatch 尺寸
    /// </summary>
    public (uint X, uint Y, uint Z) DispatchSize { get; set; }

    public RenderPass(string name, RenderPassType type)
    {
        Name = name;
        Type = type;
    }
}

/// <summary>
/// 渲染图中的资源节点
/// </summary>
public sealed class RenderGraphResource
{
    /// <summary>
    /// 资源 ID
    /// </summary>
    public uint Id { get; }

    /// <summary>
    /// 资源名称（调试用）
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 资源类型
    /// </summary>
    public RenderGraphResourceType Type { get; }

    /// <summary>
    /// 纹理句柄（如果是已存在的纹理）
    /// </summary>
    public TextureHandle? TextureHandle { get; set; }

    /// <summary>
    /// 缓冲区句柄（如果是已存在的缓冲区）
    /// </summary>
    public nint BufferHandle { get; set; }

    /// <summary>
    /// 当前资源状态
    /// </summary>
    public ResourceState CurrentState { get; set; } = ResourceState.Common;

    /// <summary>
    /// 是否为瞬态资源（RenderGraph 内部管理生命周期）
    /// </summary>
    public bool IsTransient { get; set; }

    /// <summary>
    /// 纹理宽度
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 纹理高度
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 纹理格式
    /// </summary>
    public TextureFormat Format { get; set; }

    public RenderGraphResource(uint id, string name, RenderGraphResourceType type)
    {
        Id = id;
        Name = name;
        Type = type;
    }
}

/// <summary>
/// 渲染图资源类型
/// </summary>
public enum RenderGraphResourceType : byte
{
    /// <summary>
    /// 渲染目标纹理
    /// </summary>
    RenderTarget,

    /// <summary>
    /// 可读写纹理（UAV）
    /// </summary>
    ReadWriteTexture,

    /// <summary>
    /// 只读纹理（SRV）
    /// </summary>
    ReadOnlyTexture,

    /// <summary>
    /// 缓冲区
    /// </summary>
    Buffer,

    /// <summary>
    /// Backbuffer（交换链目标）
    /// </summary>
    Backbuffer
}

/// <summary>
/// Render Pass 构建器 - fluent API
/// </summary>
public sealed class RenderPassBuilder
{
    private readonly RenderPass _pass;
    private readonly RenderGraph _graph;

    internal RenderPassBuilder(RenderPass pass, RenderGraph graph)
    {
        _pass = pass;
        _graph = graph;
    }

    /// <summary>
    /// 声明读取资源
    /// </summary>
    public RenderPassBuilder Read(uint resourceId, ResourceState state = ResourceState.ShaderResource)
    {
        _pass.Reads.Add(new ResourceAccess(resourceId, state));
        return this;
    }

    /// <summary>
    /// 声明写入资源
    /// </summary>
    public RenderPassBuilder Write(uint resourceId, ResourceState state = ResourceState.RenderTarget)
    {
        _pass.Writes.Add(new ResourceAccess(resourceId, state));
        return this;
    }

    /// <summary>
    /// 设置管线状态
    /// </summary>
    public RenderPassBuilder SetPipeline(PipelineStateDesc pso)
    {
        _pass.PipelineState = pso;
        return this;
    }

    /// <summary>
    /// 设置渲染目标
    /// </summary>
    public RenderPassBuilder SetRenderTarget(uint resourceId, float? clearR = null, float? clearG = null, float? clearB = null, float? clearA = null)
    {
        _pass.RenderTargetId = resourceId;
        _pass.Writes.Add(new ResourceAccess(resourceId, ResourceState.RenderTarget));

        if (clearR.HasValue)
        {
            _pass.ClearColor = (clearR.Value, clearG ?? 0, clearB ?? 0, clearA ?? 1);
        }

        return this;
    }

    /// <summary>
    /// 设置视口
    /// </summary>
    public RenderPassBuilder SetViewport(int width, int height)
    {
        _pass.ViewportSize = (width, height);
        return this;
    }

    /// <summary>
    /// 添加绘制命令
    /// </summary>
    public RenderPassBuilder AddCommand(DrawCommand command)
    {
        _pass.Commands.Add(command);
        return this;
    }

    /// <summary>
    /// 添加多个绘制命令
    /// </summary>
    public RenderPassBuilder AddCommands(IEnumerable<DrawCommand> commands)
    {
        _pass.Commands.AddRange(commands);
        return this;
    }

    /// <summary>
    /// 设置 Compute dispatch 尺寸
    /// </summary>
    public RenderPassBuilder SetDispatch(uint x, uint y, uint z = 1)
    {
        _pass.DispatchSize = (x, y, z);
        return this;
    }

    /// <summary>
    /// 完成构建
    /// </summary>
    public RenderGraph Build() => _graph;
}
