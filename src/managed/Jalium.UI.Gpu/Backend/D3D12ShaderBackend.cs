using System.Runtime.InteropServices;
using Jalium.UI.Gpu.Commands;
using Jalium.UI.Gpu.Pipeline;
using Jalium.UI.Gpu.Resources;
using Jalium.UI.Gpu.RenderGraph;
using Jalium.UI.Gpu.Shaders;

namespace Jalium.UI.Gpu.Backend;

/// <summary>
/// D3D12 Shader Pipeline 后端 - IRenderBackendEx 的完整实现
/// 通过 P/Invoke 调用扩展的 native jalium.native.d3d12 层
/// 共享现有 D3D12 device 和 command queue
/// </summary>
public sealed class D3D12ShaderBackend : IRenderBackendEx, IDisposable
{
    private readonly nint _nativeContext;
    private readonly ShaderCompiler _shaderCompiler;
    private readonly Dictionary<nint, nint> _allocatedResources = new();
    private ulong _fenceValue;
    private bool _disposed;

    /// <summary>
    /// 从现有 native context 创建（共享 D3D12 device）
    /// </summary>
    public D3D12ShaderBackend(nint nativeContext, string? shaderCacheDir = null)
    {
        _nativeContext = nativeContext;
        _shaderCompiler = new ShaderCompiler(shaderCacheDir);

        // 初始化 shader pipeline native 层
        var hr = NativeD3D12Pipeline.jalium_pipeline_init(_nativeContext);
        if (hr < 0)
            throw new InvalidOperationException($"Failed to initialize D3D12 shader pipeline: 0x{hr:X8}");
    }

    #region IRenderBackend (基础接口 - 委托给现有 native 层)

    public nint CreateVertexBuffer(ReadOnlySpan<float> data)
    {
        var bytes = MemoryMarshal.AsBytes(data);
        return NativeD3D12Pipeline.jalium_buffer_create(
            _nativeContext, bytes, bytes.Length, (int)BufferUsage.Vertex);
    }

    public nint CreateIndexBuffer(ReadOnlySpan<ushort> data)
    {
        var bytes = MemoryMarshal.AsBytes(data);
        return NativeD3D12Pipeline.jalium_buffer_create(
            _nativeContext, bytes, bytes.Length, (int)BufferUsage.Index);
    }

    public nint CreateInstanceBuffer(ReadOnlySpan<byte> data) =>
        NativeD3D12Pipeline.jalium_buffer_create(
            _nativeContext, data, data.Length, (int)BufferUsage.Instance);

    public nint CreateUniformBuffer(ReadOnlySpan<byte> data) =>
        NativeD3D12Pipeline.jalium_buffer_create(
            _nativeContext, data, data.Length, (int)BufferUsage.Uniform);

    public void UpdateBuffer(nint buffer, int offset, ReadOnlySpan<byte> data) =>
        NativeD3D12Pipeline.jalium_buffer_update(_nativeContext, buffer, offset, data, data.Length);

    public void DestroyBuffer(nint buffer)
    {
        if (buffer != nint.Zero)
        {
            NativeD3D12Pipeline.jalium_buffer_destroy(_nativeContext, buffer);
            _allocatedResources.Remove(buffer);
        }
    }

    public nint LoadTexture(string path, TextureFormat format) =>
        NativeD3D12Pipeline.jalium_texture_load(_nativeContext, path, (int)format);

    public nint CreateGlyphAtlas(string fontId, float fontSize, int width, int height) =>
        NativeD3D12Pipeline.jalium_glyph_atlas_create(_nativeContext, fontId, fontSize, width, height);

    public nint CreateRenderTargetTexture(int width, int height, TextureFormat format) =>
        NativeD3D12Pipeline.jalium_texture_create_rt(_nativeContext, width, height, (int)format);

    public void DestroyTexture(nint texture)
    {
        if (texture != nint.Zero)
            NativeD3D12Pipeline.jalium_texture_destroy(_nativeContext, texture);
    }

    public void BindVertexBuffer(nint buffer) =>
        NativeD3D12Pipeline.jalium_bind_vertex_buffer(_nativeContext, buffer);

    public void BindIndexBuffer(nint buffer) =>
        NativeD3D12Pipeline.jalium_bind_index_buffer(_nativeContext, buffer);

    public void BindInstanceBuffer(nint buffer) =>
        NativeD3D12Pipeline.jalium_bind_instance_buffer(_nativeContext, buffer);

    public void BindUniformBuffer(nint buffer) =>
        NativeD3D12Pipeline.jalium_bind_uniform_buffer(_nativeContext, buffer);

    public void BindTexture(int slot, nint texture) =>
        NativeD3D12Pipeline.jalium_bind_texture(_nativeContext, slot, texture);

    public void SetScissorRect(int x, int y, int width, int height) =>
        NativeD3D12Pipeline.jalium_set_scissor(_nativeContext, x, y, width, height);

    public void SetViewport(int x, int y, int width, int height) =>
        NativeD3D12Pipeline.jalium_set_viewport(_nativeContext, x, y, width, height);

    public void DrawIndexedInstanced(uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance) =>
        NativeD3D12Pipeline.jalium_draw_indexed_instanced(
            _nativeContext, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);

    public void DrawGlyphs(uint offset, uint count) =>
        NativeD3D12Pipeline.jalium_draw_glyphs(_nativeContext, offset, count);

    public void ApplyEffect(EffectType effect, uint sourceTexture, uint destTexture, ReadOnlySpan<byte> parameters) =>
        NativeD3D12Pipeline.jalium_apply_effect(_nativeContext, (int)effect, sourceTexture, destTexture, parameters, parameters.Length);

    public void CaptureBackdrop(Rect region, uint targetTextureIndex) =>
        NativeD3D12Pipeline.jalium_capture_backdrop(
            _nativeContext, region.X, region.Y, region.Width, region.Height, targetTextureIndex);

    public void ApplyBackdropFilter(BackdropFilterParams filterParams, Rect region, uint sourceTextureIndex, uint destTextureIndex, CornerRadius cornerRadius) =>
        NativeD3D12Pipeline.jalium_apply_backdrop_filter(_nativeContext, sourceTextureIndex, destTextureIndex);

    public void CompositeLayer(uint sourceTextureIndex, Rect destRect, BlendMode blendMode, byte opacity) =>
        NativeD3D12Pipeline.jalium_composite_layer(
            _nativeContext, sourceTextureIndex,
            destRect.X, destRect.Y, destRect.Width, destRect.Height,
            (int)blendMode, opacity);

    public void Submit() =>
        NativeD3D12Pipeline.jalium_submit(_nativeContext);

    #endregion

    #region IRenderBackendEx (扩展接口 - Shader Pipeline)

    public nint CompileShader(string source, string entryPoint, ShaderStage stage)
    {
        var target = stage switch
        {
            ShaderStage.Vertex => "vs_5_1",
            ShaderStage.Pixel => "ps_5_1",
            ShaderStage.Compute => "cs_5_1",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        NativeD3D12Pipeline.jalium_shader_compile(
            System.Text.Encoding.UTF8.GetBytes(source),
            source.Length, entryPoint, target,
            Shaders.ShaderCompileFlags.OptimizationLevel3,
            out var bytecodePtr, out var bytecodeSize,
            out var errorPtr, out var errorSize);

        if (errorPtr != nint.Zero)
            NativeD3D12Pipeline.jalium_shader_free_blob(errorPtr);

        return bytecodePtr;
    }

    public void DestroyShader(nint shader)
    {
        if (shader != nint.Zero)
            NativeD3D12Pipeline.jalium_shader_free_blob(shader);
    }

    public nint CreatePipelineState(PipelineStateDesc desc)
    {
        if (desc.IsCompute)
        {
            // 编译 compute shader
            var cs = _shaderCompiler.GetOrCompile(desc.ComputeShader);
            return NativeD3D12Pipeline.jalium_pso_create_compute(
                _nativeContext, cs.Bytecode, cs.Bytecode.Length, (int)desc.RootSignature);
        }

        // 编译 VS + PS
        var vs = _shaderCompiler.GetOrCompile(desc.VertexShader);
        var ps = _shaderCompiler.GetOrCompile(desc.PixelShader);

        return NativeD3D12Pipeline.jalium_pso_create_graphics(
            _nativeContext,
            vs.Bytecode, vs.Bytecode.Length,
            ps.Bytecode, ps.Bytecode.Length,
            (int)desc.InputLayout,
            (int)desc.BlendState.Mode,
            (int)desc.RasterizerState.CullMode,
            desc.DepthStencilState.EnableDepth,
            (int)desc.RtFormat,
            desc.SampleCount,
            (int)desc.RootSignature);
    }

    public void DestroyPipelineState(nint pso)
    {
        if (pso != nint.Zero)
            NativeD3D12Pipeline.jalium_pso_destroy(_nativeContext, pso);
    }

    public nint CreateRootSignature(RootSignatureType type) =>
        NativeD3D12Pipeline.jalium_root_signature_create(_nativeContext, (int)type);

    public void DestroyRootSignature(nint rootSig)
    {
        if (rootSig != nint.Zero)
            NativeD3D12Pipeline.jalium_root_signature_destroy(_nativeContext, rootSig);
    }

    public nint CreateBuffer(int size, BufferUsage usage) =>
        NativeD3D12Pipeline.jalium_buffer_create_empty(_nativeContext, size, (int)usage);

    public nint GetBufferMappedPointer(nint buffer) =>
        NativeD3D12Pipeline.jalium_buffer_get_mapped_ptr(_nativeContext, buffer);

    public nint CreateTexture2D(int width, int height, TextureFormat format, TextureUsage usage) =>
        NativeD3D12Pipeline.jalium_texture_create_2d(_nativeContext, width, height, (int)format, (int)usage);

    public DescriptorHandle CreateSrv(nint resource)
    {
        var index = NativeD3D12Pipeline.jalium_descriptor_create_srv(_nativeContext, resource);
        if (index < 0)
            throw new InvalidOperationException($"Failed to create SRV descriptor: native returned error code {index}");
        return new DescriptorHandle((uint)index, DescriptorType.SrvCbvUav);
    }

    public DescriptorHandle CreateCbv(nint buffer, int offset, int size)
    {
        var index = NativeD3D12Pipeline.jalium_descriptor_create_cbv(_nativeContext, buffer, offset, size);
        if (index < 0)
            throw new InvalidOperationException($"Failed to create CBV descriptor: native returned error code {index}");
        return new DescriptorHandle((uint)index, DescriptorType.SrvCbvUav);
    }

    public DescriptorHandle CreateUav(nint resource)
    {
        var index = NativeD3D12Pipeline.jalium_descriptor_create_uav(_nativeContext, resource);
        if (index < 0)
            throw new InvalidOperationException($"Failed to create UAV descriptor: native returned error code {index}");
        return new DescriptorHandle((uint)index, DescriptorType.SrvCbvUav);
    }

    public void FreeDescriptor(DescriptorHandle handle)
    {
        NativeD3D12Pipeline.jalium_descriptor_free(_nativeContext, (int)handle.HeapIndex);
    }

    public void ExecuteCommandBuffer(GpuCommandBuffer commands)
    {
        var cmds = commands.GetCommands();

        foreach (ref readonly var cmd in cmds)
        {
            switch (cmd.Type)
            {
                case GpuCommandType.SetPipelineStateHandle:
                    NativeD3D12Pipeline.jalium_cmd_set_pso(_nativeContext, cmd.Handle0);
                    break;
                case GpuCommandType.SetRootSignature:
                    NativeD3D12Pipeline.jalium_cmd_set_root_signature(_nativeContext, cmd.Handle0);
                    break;
                case GpuCommandType.SetVertexBuffer:
                    NativeD3D12Pipeline.jalium_bind_vertex_buffer(_nativeContext, cmd.Handle0);
                    break;
                case GpuCommandType.SetIndexBuffer:
                    NativeD3D12Pipeline.jalium_bind_index_buffer(_nativeContext, cmd.Handle0);
                    break;
                case GpuCommandType.ResourceBarrier:
                    NativeD3D12Pipeline.jalium_cmd_resource_barrier(
                        _nativeContext, cmd.Handle0, (int)cmd.UInt0, (int)cmd.UInt1);
                    break;
                case GpuCommandType.SetViewport:
                    NativeD3D12Pipeline.jalium_set_viewport(
                        _nativeContext, (int)cmd.Float0, (int)cmd.Float1, (int)cmd.Float2, (int)cmd.Float3);
                    break;
                case GpuCommandType.SetScissor:
                    NativeD3D12Pipeline.jalium_set_scissor(
                        _nativeContext, (int)cmd.UInt0, (int)cmd.UInt1, (int)cmd.UInt2, (int)cmd.UInt3);
                    break;
                case GpuCommandType.DrawIndexedInstanced:
                    NativeD3D12Pipeline.jalium_draw_indexed_instanced(
                        _nativeContext, cmd.UInt0, cmd.UInt1, cmd.UInt2, cmd.Int0, cmd.UInt3);
                    break;
                case GpuCommandType.Draw:
                    NativeD3D12Pipeline.jalium_draw(
                        _nativeContext, cmd.UInt0, cmd.UInt1, cmd.UInt2, cmd.UInt3);
                    break;
                case GpuCommandType.Dispatch:
                    NativeD3D12Pipeline.jalium_dispatch(
                        _nativeContext, cmd.UInt0, cmd.UInt1, cmd.UInt2);
                    break;
                case GpuCommandType.ClearRenderTarget:
                    NativeD3D12Pipeline.jalium_cmd_clear_rt(
                        _nativeContext, cmd.UInt0, cmd.Float0, cmd.Float1, cmd.Float2, cmd.Float3);
                    break;
            }
        }
    }

    public ulong Signal()
    {
        _fenceValue++;
        NativeD3D12Pipeline.jalium_fence_signal(_nativeContext, _fenceValue);
        return _fenceValue;
    }

    public void WaitForFence(ulong fenceValue) =>
        NativeD3D12Pipeline.jalium_fence_wait(_nativeContext, fenceValue);

    public nint DeviceHandle =>
        NativeD3D12Pipeline.jalium_get_device(_nativeContext);

    public nint CommandQueueHandle =>
        NativeD3D12Pipeline.jalium_get_command_queue(_nativeContext);

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _shaderCompiler.Dispose();
        NativeD3D12Pipeline.jalium_pipeline_shutdown(_nativeContext);
    }
}

/// <summary>
/// D3D12 Pipeline native P/Invoke 绑定
/// </summary>
internal static partial class NativeD3D12Pipeline
{
    private const string LibName = "jalium.native.d3d12";

    // ===== 初始化 =====

    [LibraryImport(LibName)]
    internal static partial int jalium_pipeline_init(nint context);

    [LibraryImport(LibName)]
    internal static partial void jalium_pipeline_shutdown(nint context);

    // ===== 缓冲区 =====

    [LibraryImport(LibName)]
    internal static partial nint jalium_buffer_create(nint context, ReadOnlySpan<byte> data, int size, int usage);

    [LibraryImport(LibName)]
    internal static partial nint jalium_buffer_create_empty(nint context, int size, int usage);

    [LibraryImport(LibName)]
    internal static partial nint jalium_buffer_get_mapped_ptr(nint context, nint buffer);

    [LibraryImport(LibName)]
    internal static partial void jalium_buffer_update(nint context, nint buffer, int offset, ReadOnlySpan<byte> data, int size);

    [LibraryImport(LibName)]
    internal static partial void jalium_buffer_destroy(nint context, nint buffer);

    // ===== 纹理 =====

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint jalium_texture_load(nint context, string path, int format);

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint jalium_glyph_atlas_create(nint context, string fontId, float fontSize, int width, int height);

    [LibraryImport(LibName)]
    internal static partial nint jalium_texture_create_rt(nint context, int width, int height, int format);

    [LibraryImport(LibName)]
    internal static partial nint jalium_texture_create_2d(nint context, int width, int height, int format, int usage);

    [LibraryImport(LibName)]
    internal static partial void jalium_texture_destroy(nint context, nint texture);

    // ===== 绑定 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_bind_vertex_buffer(nint context, nint buffer);

    [LibraryImport(LibName)]
    internal static partial void jalium_bind_index_buffer(nint context, nint buffer);

    [LibraryImport(LibName)]
    internal static partial void jalium_bind_instance_buffer(nint context, nint buffer);

    [LibraryImport(LibName)]
    internal static partial void jalium_bind_uniform_buffer(nint context, nint buffer);

    [LibraryImport(LibName)]
    internal static partial void jalium_bind_texture(nint context, int slot, nint texture);

    // ===== 状态 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_set_scissor(nint context, int x, int y, int width, int height);

    [LibraryImport(LibName)]
    internal static partial void jalium_set_viewport(nint context, int x, int y, int width, int height);

    // ===== 绘制 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_draw_indexed_instanced(nint context, uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance);

    [LibraryImport(LibName)]
    internal static partial void jalium_draw(nint context, uint vertexCount, uint instanceCount, uint startVertex, uint startInstance);

    [LibraryImport(LibName)]
    internal static partial void jalium_draw_glyphs(nint context, uint offset, uint count);

    [LibraryImport(LibName)]
    internal static partial void jalium_dispatch(nint context, uint x, uint y, uint z);

    // ===== 效果 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_apply_effect(nint context, int effectType, uint srcTex, uint dstTex, ReadOnlySpan<byte> parameters, int paramSize);

    [LibraryImport(LibName)]
    internal static partial void jalium_capture_backdrop(nint context, float x, float y, float w, float h, uint targetTexIndex);

    [LibraryImport(LibName)]
    internal static partial void jalium_apply_backdrop_filter(nint context, uint srcTexIndex, uint dstTexIndex);

    [LibraryImport(LibName)]
    internal static partial void jalium_composite_layer(nint context, uint srcTexIndex, float x, float y, float w, float h, int blendMode, byte opacity);

    [LibraryImport(LibName)]
    internal static partial void jalium_submit(nint context);

    // ===== Shader Pipeline 扩展 =====

    [LibraryImport(LibName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int jalium_shader_compile(byte[] sourceData, int sourceSize, string entryPoint, string target, Shaders.ShaderCompileFlags flags, out nint bytecodePtr, out int bytecodeSize, out nint errorPtr, out int errorSize);

    [LibraryImport(LibName)]
    internal static partial void jalium_shader_free_blob(nint blob);

    [LibraryImport(LibName)]
    internal static partial nint jalium_pso_create_graphics(nint context, byte[] vsBytecode, int vsSize, byte[] psBytecode, int psSize, int inputLayout, int blendMode, int cullMode, [MarshalAs(UnmanagedType.Bool)] bool depthEnable, int rtFormat, int sampleCount, int rootSigType);

    [LibraryImport(LibName)]
    internal static partial nint jalium_pso_create_compute(nint context, byte[] csBytecode, int csSize, int rootSigType);

    [LibraryImport(LibName)]
    internal static partial void jalium_pso_destroy(nint context, nint pso);

    [LibraryImport(LibName)]
    internal static partial nint jalium_root_signature_create(nint context, int type);

    [LibraryImport(LibName)]
    internal static partial void jalium_root_signature_destroy(nint context, nint rootSig);

    // ===== 描述符 =====

    [LibraryImport(LibName)]
    internal static partial int jalium_descriptor_create_srv(nint context, nint resource);

    [LibraryImport(LibName)]
    internal static partial int jalium_descriptor_create_cbv(nint context, nint buffer, int offset, int size);

    [LibraryImport(LibName)]
    internal static partial int jalium_descriptor_create_uav(nint context, nint resource);

    [LibraryImport(LibName)]
    internal static partial void jalium_descriptor_free(nint context, int index);

    // ===== 命令 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_cmd_set_pso(nint context, nint pso);

    [LibraryImport(LibName)]
    internal static partial void jalium_cmd_set_root_signature(nint context, nint rootSig);

    [LibraryImport(LibName)]
    internal static partial void jalium_cmd_resource_barrier(nint context, nint resource, int stateBefore, int stateAfter);

    [LibraryImport(LibName)]
    internal static partial void jalium_cmd_clear_rt(nint context, uint rtId, float r, float g, float b, float a);

    // ===== 同步 =====

    [LibraryImport(LibName)]
    internal static partial void jalium_fence_signal(nint context, ulong fenceValue);

    [LibraryImport(LibName)]
    internal static partial void jalium_fence_wait(nint context, ulong fenceValue);

    // ===== 设备信息 =====

    [LibraryImport(LibName)]
    internal static partial nint jalium_get_device(nint context);

    [LibraryImport(LibName)]
    internal static partial nint jalium_get_command_queue(nint context);
}
