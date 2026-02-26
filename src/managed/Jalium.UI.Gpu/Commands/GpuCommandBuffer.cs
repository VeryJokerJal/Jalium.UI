using System.Runtime.InteropServices;
using Jalium.UI.Gpu.Pipeline;
using Jalium.UI.Gpu.RenderGraph;
using Jalium.UI.Gpu.Resources;

namespace Jalium.UI.Gpu.Commands;

/// <summary>
/// GPU 命令缓冲区 - 录制式命令列表
/// 分离录制阶段和执行阶段，支持多线程录制
/// 内部使用 struct GpuCommand 最小化 GC 压力
/// </summary>
public sealed class GpuCommandBuffer
{
    private readonly List<GpuCommand> _commands = new();

    /// <summary>
    /// 已录制的命令数
    /// </summary>
    public int CommandCount => _commands.Count;

    #region State Setting

    /// <summary>
    /// 设置管线状态
    /// </summary>
    public void SetPipelineState(PipelineStateDesc desc)
    {
        _commands.Add(GpuCommand.CreateSetPipelineState(desc));
    }

    /// <summary>
    /// 设置管线状态（通过已编译的 PSO handle）
    /// </summary>
    public void SetPipelineState(nint pso)
    {
        _commands.Add(GpuCommand.CreateSetPipelineStateHandle(pso));
    }

    /// <summary>
    /// 设置根签名
    /// </summary>
    public void SetRootSignature(nint rootSig)
    {
        _commands.Add(GpuCommand.CreateSetRootSignature(rootSig));
    }

    /// <summary>
    /// 绑定顶点缓冲区
    /// </summary>
    public void SetVertexBuffer(nint buffer, uint stride, int slot = 0)
    {
        _commands.Add(GpuCommand.CreateSetVertexBuffer(buffer, stride, slot));
    }

    /// <summary>
    /// 绑定索引缓冲区
    /// </summary>
    public void SetIndexBuffer(nint buffer)
    {
        _commands.Add(GpuCommand.CreateSetIndexBuffer(buffer));
    }

    /// <summary>
    /// 绑定常量缓冲区
    /// </summary>
    public void SetConstantBuffer(int slot, nint buffer, int offset, int size)
    {
        _commands.Add(GpuCommand.CreateSetConstantBuffer(slot, buffer, offset, size));
    }

    /// <summary>
    /// 绑定 Shader Resource View
    /// </summary>
    public void SetShaderResource(int slot, DescriptorHandle srv)
    {
        _commands.Add(GpuCommand.CreateSetShaderResource(slot, srv));
    }

    /// <summary>
    /// 绑定 Unordered Access View
    /// </summary>
    public void SetUnorderedAccess(int slot, DescriptorHandle uav)
    {
        _commands.Add(GpuCommand.CreateSetUnorderedAccess(slot, uav));
    }

    /// <summary>
    /// 绑定采样器
    /// </summary>
    public void SetSampler(int slot, DescriptorHandle sampler)
    {
        _commands.Add(GpuCommand.CreateSetSampler(slot, sampler));
    }

    #endregion

    #region Resource Transitions

    /// <summary>
    /// 资源状态屏障
    /// </summary>
    public void ResourceBarrier(nint resource, ResourceState before, ResourceState after)
    {
        _commands.Add(GpuCommand.CreateResourceBarrier(resource, before, after));
    }

    #endregion

    #region Render Target

    /// <summary>
    /// 设置渲染目标
    /// </summary>
    public void SetRenderTarget(uint renderTargetId, nint dsv = 0)
    {
        _commands.Add(GpuCommand.CreateSetRenderTarget(renderTargetId, dsv));
    }

    /// <summary>
    /// 清除渲染目标
    /// </summary>
    public void ClearRenderTarget(uint renderTargetId, float r, float g, float b, float a)
    {
        _commands.Add(GpuCommand.CreateClearRenderTarget(renderTargetId, r, g, b, a));
    }

    /// <summary>
    /// 设置视口
    /// </summary>
    public void SetViewport(float x, float y, float width, float height)
    {
        _commands.Add(GpuCommand.CreateSetViewport(x, y, width, height));
    }

    /// <summary>
    /// 设置裁剪矩形
    /// </summary>
    public void SetScissor(int x, int y, int width, int height)
    {
        _commands.Add(GpuCommand.CreateSetScissor(x, y, width, height));
    }

    #endregion

    #region Drawing

    /// <summary>
    /// 索引实例化绘制
    /// </summary>
    public void DrawIndexedInstanced(uint indexCount, uint instanceCount, uint startIndex, int baseVertex, uint startInstance)
    {
        _commands.Add(GpuCommand.CreateDrawIndexedInstanced(indexCount, instanceCount, startIndex, baseVertex, startInstance));
    }

    /// <summary>
    /// 非索引绘制
    /// </summary>
    public void Draw(uint vertexCount, uint instanceCount, uint startVertex, uint startInstance)
    {
        _commands.Add(GpuCommand.CreateDraw(vertexCount, instanceCount, startVertex, startInstance));
    }

    /// <summary>
    /// Compute Shader 分派
    /// </summary>
    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        _commands.Add(GpuCommand.CreateDispatch(groupCountX, groupCountY, groupCountZ));
    }

    /// <summary>
    /// 复制纹理区域
    /// </summary>
    public void CopyTextureRegion(Rect sourceRegion, uint destTextureIndex)
    {
        _commands.Add(GpuCommand.CreateCopyTextureRegion(sourceRegion, destTextureIndex));
    }

    #endregion

    /// <summary>
    /// 获取所有已录制的命令
    /// </summary>
    public ReadOnlySpan<GpuCommand> GetCommands() => CollectionsMarshal.AsSpan(_commands);

    /// <summary>
    /// 重置命令缓冲区（复用内存）
    /// </summary>
    public void Reset()
    {
        _commands.Clear();
    }
}

/// <summary>
/// GPU 命令类型
/// </summary>
public enum GpuCommandType : byte
{
    SetPipelineState,
    SetPipelineStateHandle,
    SetRootSignature,
    SetVertexBuffer,
    SetIndexBuffer,
    SetConstantBuffer,
    SetShaderResource,
    SetUnorderedAccess,
    SetSampler,
    ResourceBarrier,
    SetRenderTarget,
    ClearRenderTarget,
    SetViewport,
    SetScissor,
    DrawIndexedInstanced,
    Draw,
    Dispatch,
    CopyTextureRegion
}

/// <summary>
/// GPU 命令 - struct 实现，使用 discriminated union 模式最小化 GC
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 64)]
public struct GpuCommand
{
    [FieldOffset(0)]
    public GpuCommandType Type;

    // 通用字段（union 方式复用内存）
    [FieldOffset(8)]  public nint Handle0;
    [FieldOffset(16)] public nint Handle1;
    [FieldOffset(24)] public uint UInt0;
    [FieldOffset(28)] public uint UInt1;
    [FieldOffset(32)] public uint UInt2;
    [FieldOffset(36)] public int Int0;
    [FieldOffset(40)] public uint UInt3;
    [FieldOffset(44)] public float Float0;
    [FieldOffset(48)] public float Float1;
    [FieldOffset(52)] public float Float2;
    [FieldOffset(56)] public float Float3;

    // PSO desc 需要额外存储（通过 handle 引用）
    [FieldOffset(8)]  public PipelineStateDesc PipelineDesc;

    #region Factory Methods

    public static GpuCommand CreateSetPipelineState(PipelineStateDesc desc) =>
        new() { Type = GpuCommandType.SetPipelineState, PipelineDesc = desc };

    public static GpuCommand CreateSetPipelineStateHandle(nint pso) =>
        new() { Type = GpuCommandType.SetPipelineStateHandle, Handle0 = pso };

    public static GpuCommand CreateSetRootSignature(nint rootSig) =>
        new() { Type = GpuCommandType.SetRootSignature, Handle0 = rootSig };

    public static GpuCommand CreateSetVertexBuffer(nint buffer, uint stride, int slot) =>
        new() { Type = GpuCommandType.SetVertexBuffer, Handle0 = buffer, UInt0 = stride, Int0 = slot };

    public static GpuCommand CreateSetIndexBuffer(nint buffer) =>
        new() { Type = GpuCommandType.SetIndexBuffer, Handle0 = buffer };

    public static GpuCommand CreateSetConstantBuffer(int slot, nint buffer, int offset, int size) =>
        new() { Type = GpuCommandType.SetConstantBuffer, Handle0 = buffer, Int0 = slot, UInt0 = (uint)offset, UInt1 = (uint)size };

    public static GpuCommand CreateSetShaderResource(int slot, DescriptorHandle srv) =>
        new() { Type = GpuCommandType.SetShaderResource, Int0 = slot, UInt0 = srv.HeapIndex };

    public static GpuCommand CreateSetUnorderedAccess(int slot, DescriptorHandle uav) =>
        new() { Type = GpuCommandType.SetUnorderedAccess, Int0 = slot, UInt0 = uav.HeapIndex };

    public static GpuCommand CreateSetSampler(int slot, DescriptorHandle sampler) =>
        new() { Type = GpuCommandType.SetSampler, Int0 = slot, UInt0 = sampler.HeapIndex };

    public static GpuCommand CreateResourceBarrier(nint resource, ResourceState before, ResourceState after) =>
        new() { Type = GpuCommandType.ResourceBarrier, Handle0 = resource, UInt0 = (uint)before, UInt1 = (uint)after };

    public static GpuCommand CreateSetRenderTarget(uint rtId, nint dsv) =>
        new() { Type = GpuCommandType.SetRenderTarget, UInt0 = rtId, Handle1 = dsv };

    public static GpuCommand CreateClearRenderTarget(uint rtId, float r, float g, float b, float a) =>
        new() { Type = GpuCommandType.ClearRenderTarget, UInt0 = rtId, Float0 = r, Float1 = g, Float2 = b, Float3 = a };

    public static GpuCommand CreateSetViewport(float x, float y, float w, float h) =>
        new() { Type = GpuCommandType.SetViewport, Float0 = x, Float1 = y, Float2 = w, Float3 = h };

    public static GpuCommand CreateSetScissor(int x, int y, int w, int h) =>
        new() { Type = GpuCommandType.SetScissor, UInt0 = (uint)x, UInt1 = (uint)y, UInt2 = (uint)w, UInt3 = (uint)h };

    public static GpuCommand CreateDrawIndexedInstanced(uint indexCount, uint instanceCount, uint startIndex, int baseVertex, uint startInstance) =>
        new() { Type = GpuCommandType.DrawIndexedInstanced, UInt0 = indexCount, UInt1 = instanceCount, UInt2 = startIndex, Int0 = baseVertex, UInt3 = startInstance };

    public static GpuCommand CreateDraw(uint vertexCount, uint instanceCount, uint startVertex, uint startInstance) =>
        new() { Type = GpuCommandType.Draw, UInt0 = vertexCount, UInt1 = instanceCount, UInt2 = startVertex, UInt3 = startInstance };

    public static GpuCommand CreateDispatch(uint x, uint y, uint z) =>
        new() { Type = GpuCommandType.Dispatch, UInt0 = x, UInt1 = y, UInt2 = z };

    public static GpuCommand CreateCopyTextureRegion(Rect region, uint destTextureIndex) =>
        new() { Type = GpuCommandType.CopyTextureRegion, Float0 = region.X, Float1 = region.Y, Float2 = region.Width, Float3 = region.Height, UInt0 = destTextureIndex };

    #endregion
}
