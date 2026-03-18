using System.Runtime.InteropServices;
using Jalium.UI.Gpu.Commands;
using Jalium.UI.Gpu.Materials;
using Jalium.UI.Gpu.Pipeline;
using Jalium.UI.Gpu.RenderGraph;
using Jalium.UI.Gpu.Resources;
using Jalium.UI.Gpu.Shaders;

namespace Jalium.UI.Gpu;

/// <summary>
/// GPU 实例数据布局 - 与着色器 InstanceData 一一对应。
/// 修改此结构体时同步更新着色器端定义。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GpuInstanceData
{
    public float X;                    // 0
    public float Y;                    // 4
    public float Width;                // 8
    public float Height;               // 12
    public float TexU0;                // 16
    public float TexV0;                // 20
    public float TexU1;                // 24
    public float TexV1;                // 28
    public uint BackgroundColor;       // 32
    public float CornerRadiusTopLeft;  // 36
    public float CornerRadiusTopRight; // 40
    public float CornerRadiusBottomRight; // 44
    public float CornerRadiusBottomLeft;  // 48
    public float BorderThicknessLeft;  // 52
    public float BorderThicknessTop;   // 56
    public float BorderThicknessRight; // 60
    public float BorderThicknessBottom;// 64
    public uint BorderColor;           // 68
    public int Padding;                // 72
    // total: 76 bytes, padded to 80 with Pack=4 alignment

    public const int SizeInBytes = 80;
}

/// <summary>
/// GPU Shader Pipeline 运行时 V2
/// 完整的 Shader 管线执行引擎：
///   JALXAML → CompiledUIBundle → RenderGraph → CommandBuffer → GPU
///
/// 与 UIRuntime V1 的区别：
/// - V1: 通过 IRenderBackend 逐命令执行（类似 immediate mode）
/// - V2: 构建 RenderGraph → 自动 barrier → 录制 CommandBuffer → 一次提交（类似 Vulkan/Metal）
/// </summary>
public sealed class UIRuntimeV2 : IDisposable, IUIRuntimeHost
{
    private readonly IRenderBackendEx _backend;
    private readonly GpuResourceManager _resourceManager;
    private readonly PipelineCache _pipelineCache;
    private readonly DescriptorHeapManager _descriptorHeap;
    private readonly TextureManager _textureManager;
    private readonly MaterialCompiler _materialCompiler;
    private readonly RenderGraphBuilder _graphBuilder;
    private readonly GpuCommandBuffer _commandBuffer;

    // 复用 V1 的子系统
    private readonly AnimationController _animationController;
    private readonly InputHandler _inputHandler;
    private readonly StateManager _stateManager;

    // 当前状态
    private CompiledUIBundle? _currentBundle;
    private CompiledRenderGraph? _compiledGraph;
    private MaterialDescriptor[]? _materialDescriptors;
    private bool _isDirty = true;
    private bool _disposed;

    // GPU 资源句柄
    private nint _vertexBuffer;
    private nint _indexBuffer;
    private nint _instanceBuffer;
    private int _instanceBufferSize;
    private nint _materialBuffer;  // StructuredBuffer<MaterialData>
    private int _materialBufferSize;
    private nint _frameConstantsBuffer;
    private readonly Dictionary<uint, TextureHandle> _textures = new();
    private readonly Dictionary<uint, TextureHandle> _glyphAtlases = new();

    // IUIRuntimeHost explicit implementation
    CompiledUIBundle? IUIRuntimeHost.CurrentBundle => _currentBundle;
    IRenderBackend IUIRuntimeHost.Backend => _backend;
    nint IUIRuntimeHost.InstanceBuffer => _instanceBuffer;
    AnimationController IUIRuntimeHost.Animator => _animationController;
    InteractiveRegion? IUIRuntimeHost.HitTest(float x, float y) => HitTest(x, y);
    void IUIRuntimeHost.TriggerStateTransition(uint nodeId, TriggerType trigger) => TriggerStateTransition(nodeId, trigger);
    void IUIRuntimeHost.UpdateNodeProperty(uint nodeId, AnimatableProperty property, float value) => UpdateNodeProperty(nodeId, property, value);

    public UIRuntimeV2(IRenderBackendEx backend)
    {
        _backend = backend;
        _resourceManager = new GpuResourceManager(backend);
        _descriptorHeap = new DescriptorHeapManager(backend);
        _textureManager = new TextureManager(backend, _descriptorHeap);
        _pipelineCache = new PipelineCache(backend);
        _materialCompiler = new MaterialCompiler();
        _graphBuilder = new RenderGraphBuilder();
        _commandBuffer = new GpuCommandBuffer();

        // 初始化共享子系统（复用 V1 的设计，通过 IUIRuntimeHost 接口）
        _animationController = new AnimationController(this);
        _inputHandler = new InputHandler(this);
        _stateManager = new StateManager(this);
    }

    /// <summary>
    /// 加载编译后的 UI Bundle
    /// </summary>
    public void LoadBundle(CompiledUIBundle bundle)
    {
        // 验证版本
        if (bundle.Version != RenderIR.Version)
        {
            throw new InvalidOperationException(
                $"Bundle version mismatch. Expected {RenderIR.Version}, got {bundle.Version}");
        }

        _currentBundle = bundle;

        // Step 1: 编译材质
        _materialCompiler.Reset();
        _materialDescriptors = _materialCompiler.CompileBatch(
            bundle.Materials, bundle.Gradients, bundle.Nodes);

        // Step 2: 上传静态 GPU 资源
        UploadStaticResources();

        // Step 3: 预热 PSO 缓存
        _pipelineCache.WarmUp();

        // Step 4: 初始化子系统
        _stateManager.Initialize(bundle.StateTransitions);
        _animationController.Initialize(bundle.Curves, bundle.AnimationTargets, bundle.AnimationValues);

        _isDirty = true;
    }

    /// <summary>
    /// 从文件加载 Bundle
    /// </summary>
    public void LoadFromFile(string path)
    {
        var bundle = BundleSerializer.Load(path);
        LoadBundle(bundle);
    }

    /// <summary>
    /// 每帧更新 - 动画、状态机
    /// </summary>
    public void Update(double deltaTime)
    {
        if (_animationController.Update(deltaTime))
        {
            _isDirty = true;
        }

        if (_stateManager.HasPendingTransitions)
        {
            _stateManager.ProcessTransitions();
            _isDirty = true;
        }
    }

    /// <summary>
    /// 渲染 - 构建 RenderGraph → 录制 CommandBuffer → 提交 GPU
    /// </summary>
    public void Render(int viewportWidth, int viewportHeight)
    {
        if (_currentBundle == null) return;

        _resourceManager.BeginFrame();

        // 更新帧常量（视口、时间、DPI）
        UpdateFrameConstants(viewportWidth, viewportHeight);

        // 如果 bundle 或视口改变，重新构建 RenderGraph
        if (_isDirty || _compiledGraph == null)
        {
            var graph = _graphBuilder.Build(_currentBundle, viewportWidth, viewportHeight);
            _compiledGraph = graph.Compile();
            _isDirty = false;
        }

        // 录制命令缓冲区
        _commandBuffer.Reset();
        RecordCommands(viewportWidth, viewportHeight);

        // 执行
        _backend.ExecuteCommandBuffer(_commandBuffer);

        _resourceManager.EndFrame();
    }

    /// <summary>
    /// 处理输入事件
    /// </summary>
    public void HandleInput(InputEvent input)
    {
        _inputHandler.Handle(input);
    }

    /// <summary>
    /// Hit testing
    /// </summary>
    public InteractiveRegion? HitTest(float x, float y)
    {
        if (_currentBundle == null) return null;

        for (int i = _currentBundle.InteractiveRegions.Length - 1; i >= 0; i--)
        {
            var region = _currentBundle.InteractiveRegions[i];
            if (x >= region.Bounds.X && x < region.Bounds.X + region.Bounds.Width &&
                y >= region.Bounds.Y && y < region.Bounds.Y + region.Bounds.Height)
            {
                // 如果有裁剪边界，检查点是否在裁剪区域内
                if (!region.ClipBounds.IsEmpty &&
                    (x < region.ClipBounds.X || x >= region.ClipBounds.X + region.ClipBounds.Width ||
                     y < region.ClipBounds.Y || y >= region.ClipBounds.Y + region.ClipBounds.Height))
                {
                    continue;
                }

                return region;
            }
        }

        return null;
    }

    /// <summary>
    /// 触发状态转换（由 InputHandler 调用）
    /// </summary>
    internal void TriggerStateTransition(uint nodeId, TriggerType trigger)
    {
        _stateManager.Trigger(nodeId, trigger);
    }

    /// <summary>
    /// 更新节点属性（由 AnimationController 调用）
    /// </summary>
    // 预计算的属性偏移量（从 GpuInstanceData 结构体布局派生，编译时验证）
    private static readonly int OffsetX = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.X));
    private static readonly int OffsetY = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.Y));
    private static readonly int OffsetWidth = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.Width));
    private static readonly int OffsetHeight = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.Height));
    private static readonly int OffsetBackgroundColor = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.BackgroundColor));
    private static readonly int OffsetCornerRadiusTL = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.CornerRadiusTopLeft));
    private static readonly int OffsetCornerRadiusTR = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.CornerRadiusTopRight));
    private static readonly int OffsetCornerRadiusBR = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.CornerRadiusBottomRight));
    private static readonly int OffsetCornerRadiusBL = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.CornerRadiusBottomLeft));
    private static readonly int OffsetBorderLeft = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.BorderThicknessLeft));
    private static readonly int OffsetBorderTop = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.BorderThicknessTop));
    private static readonly int OffsetBorderRight = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.BorderThicknessRight));
    private static readonly int OffsetBorderBottom = (int)Marshal.OffsetOf<GpuInstanceData>(nameof(GpuInstanceData.BorderThicknessBottom));

    // 复用的 4 字节写入缓冲区，避免每帧 BitConverter.GetBytes 分配
    [ThreadStatic]
    private static byte[]? t_floatBuffer;

    private static byte[] WriteFloat(float value)
    {
        var buf = t_floatBuffer ??= new byte[4];
        BitConverter.TryWriteBytes(buf, value);
        return buf;
    }

    internal void UpdateNodeProperty(uint nodeId, AnimatableProperty property, float value)
    {
        if (_currentBundle == null) return;

        var nodeIndex = -1;
        for (int i = 0; i < _currentBundle.Nodes.Length; i++)
        {
            if (_currentBundle.Nodes[i].Id == nodeId)
            {
                nodeIndex = i;
                break;
            }
        }

        if (nodeIndex < 0) return;

        var baseOffset = nodeIndex * GpuInstanceData.SizeInBytes;

        var (offset, data) = property switch
        {
            AnimatableProperty.X => (baseOffset + OffsetX, WriteFloat(value)),
            AnimatableProperty.Y => (baseOffset + OffsetY, WriteFloat(value)),
            AnimatableProperty.Width => (baseOffset + OffsetWidth, WriteFloat(value)),
            AnimatableProperty.Height => (baseOffset + OffsetHeight, WriteFloat(value)),
            AnimatableProperty.Opacity => (baseOffset + OffsetBackgroundColor, UpdateColorAlpha(nodeIndex, value)),
            AnimatableProperty.CornerRadiusTopLeft => (baseOffset + OffsetCornerRadiusTL, WriteFloat(value)),
            AnimatableProperty.CornerRadiusTopRight => (baseOffset + OffsetCornerRadiusTR, WriteFloat(value)),
            AnimatableProperty.CornerRadiusBottomRight => (baseOffset + OffsetCornerRadiusBR, WriteFloat(value)),
            AnimatableProperty.CornerRadiusBottomLeft => (baseOffset + OffsetCornerRadiusBL, WriteFloat(value)),
            AnimatableProperty.BorderThicknessLeft => (baseOffset + OffsetBorderLeft, WriteFloat(value)),
            AnimatableProperty.BorderThicknessTop => (baseOffset + OffsetBorderTop, WriteFloat(value)),
            AnimatableProperty.BorderThicknessRight => (baseOffset + OffsetBorderRight, WriteFloat(value)),
            AnimatableProperty.BorderThicknessBottom => (baseOffset + OffsetBorderBottom, WriteFloat(value)),
            _ => (-1, Array.Empty<byte>())
        };

        if (offset >= 0 && data.Length > 0)
        {
            _backend.UpdateBuffer(_instanceBuffer, offset, data);
        }

        _isDirty = true;
    }

    #region Private Methods

    private void ReleaseGpuBuffers()
    {
        if (_vertexBuffer != nint.Zero) { _backend.DestroyBuffer(_vertexBuffer); _vertexBuffer = nint.Zero; }
        if (_indexBuffer != nint.Zero) { _backend.DestroyBuffer(_indexBuffer); _indexBuffer = nint.Zero; }
        if (_instanceBuffer != nint.Zero)
        {
            _resourceManager.ReleaseBuffer(_instanceBuffer, _instanceBufferSize, BufferUsage.Instance);
            _instanceBuffer = nint.Zero;
            _instanceBufferSize = 0;
        }
        if (_materialBuffer != nint.Zero)
        {
            _resourceManager.ReleaseBuffer(_materialBuffer, _materialBufferSize, BufferUsage.Storage);
            _materialBuffer = nint.Zero;
            _materialBufferSize = 0;
        }
        if (_frameConstantsBuffer != nint.Zero)
        {
            _resourceManager.ReleaseBuffer(_frameConstantsBuffer, 256, BufferUsage.Uniform);
            _frameConstantsBuffer = nint.Zero;
        }
    }

    private void UploadStaticResources()
    {
        if (_currentBundle == null) return;

        // 归还旧缓冲区到池中
        ReleaseGpuBuffers();

        // 标准矩形顶点（不池化 —— 每次都相同大小且只创建一次）
        float[] rectVertices = [0f, 0f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f, 1f, 0f, 1f, 0f, 1f];
        ushort[] rectIndices = [0, 1, 2, 0, 2, 3];

        _vertexBuffer = _backend.CreateVertexBuffer(rectVertices);
        _indexBuffer = _backend.CreateIndexBuffer(rectIndices);

        // 实例数据 —— 通过池获取
        var instanceData = GenerateInstanceData();
        _instanceBufferSize = instanceData.Length;
        _instanceBuffer = _resourceManager.AcquireBuffer(_instanceBufferSize, BufferUsage.Instance);
        _backend.UpdateBuffer(_instanceBuffer, 0, instanceData);

        // 材质 StructuredBuffer —— 通过池获取
        var materialData = _materialCompiler.GenerateUniformData();
        _materialBufferSize = Math.Max(materialData.Length, 256);
        _materialBuffer = _resourceManager.AcquireBuffer(_materialBufferSize, BufferUsage.Storage);
        _backend.UpdateBuffer(_materialBuffer, 0, materialData);

        // 帧常量缓冲区 —— 通过池获取
        _frameConstantsBuffer = _resourceManager.AcquireBuffer(256, BufferUsage.Uniform);

        // 纹理
        foreach (var (index, texRef) in _currentBundle.Textures.Select((t, i) => ((uint)i, t)))
        {
            _textures[index] = _textureManager.LoadTexture(texRef.Path, texRef.Format);
        }

        // 字形图集
        foreach (var (index, atlas) in _currentBundle.GlyphAtlases.Select((a, i) => ((uint)i, a)))
        {
            _glyphAtlases[index] = _textureManager.CreateGlyphAtlas(
                atlas.FontId, atlas.FontSize, atlas.Width, atlas.Height);
        }
    }

    private byte[] GenerateInstanceData()
    {
        if (_currentBundle == null) return [];

        var data = new byte[_currentBundle.Nodes.Length * GpuInstanceData.SizeInBytes];
        using var ms = new MemoryStream(data);
        using var writer = new BinaryWriter(ms);

        foreach (var node in _currentBundle.Nodes)
        {
            if (node is RectNode rect)
            {
                writer.Write(rect.Bounds.X);
                writer.Write(rect.Bounds.Y);
                writer.Write(rect.Bounds.Width);
                writer.Write(rect.Bounds.Height);
                writer.Write(0f); writer.Write(0f); writer.Write(1f); writer.Write(1f);

                var material = _currentBundle.Materials[rect.MaterialIndex];
                writer.Write(material.BackgroundColor);
                writer.Write(rect.CornerRadius.TopLeft);
                writer.Write(rect.CornerRadius.TopRight);
                writer.Write(rect.CornerRadius.BottomRight);
                writer.Write(rect.CornerRadius.BottomLeft);
                writer.Write(rect.BorderThickness.Left);
                writer.Write(rect.BorderThickness.Top);
                writer.Write(rect.BorderThickness.Right);
                writer.Write(rect.BorderThickness.Bottom);
                writer.Write(material.BorderColor);
                writer.Write(0); // padding
            }
            else
            {
                writer.Write(new byte[GpuInstanceData.SizeInBytes]);
            }
        }

        return data;
    }

    private void UpdateFrameConstants(int viewportWidth, int viewportHeight)
    {
        // 16 bytes: ViewportSize(8) + Time(4) + DpiScale(4)
        var data = new byte[16];
        using var ms = new MemoryStream(data);
        using var writer = new BinaryWriter(ms);

        writer.Write((float)viewportWidth);
        writer.Write((float)viewportHeight);
        writer.Write((float)Environment.TickCount64 / 1000.0);
        writer.Write(1.0f); // DPI scale

        _backend.UpdateBuffer(_frameConstantsBuffer, 0, data);
    }

    private void RecordCommands(int viewportWidth, int viewportHeight)
    {
        if (_compiledGraph == null) return;

        // 绑定全局资源
        _commandBuffer.SetVertexBuffer(_vertexBuffer, 16, 0);
        _commandBuffer.SetIndexBuffer(_indexBuffer);
        _commandBuffer.SetConstantBuffer(0, _frameConstantsBuffer, 0, 256);

        // 执行编译后的渲染图
        _compiledGraph.Execute(_backend, _commandBuffer);
    }

    private byte[] UpdateColorAlpha(int nodeIndex, float opacity)
    {
        if (_currentBundle == null) return [];

        var node = _currentBundle.Nodes[nodeIndex];
        var material = _currentBundle.Materials[node.MaterialIndex];
        var alpha = (byte)(Math.Clamp(opacity, 0f, 1f) * 255);
        var color = (material.BackgroundColor & 0x00FFFFFF) | ((uint)alpha << 24);

        return BitConverter.GetBytes(color);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pipelineCache.Dispose();
        _textureManager.Dispose();
        _descriptorHeap.Dispose();

        // 释放缓冲区（Dispose 时直接销毁，不归还池）
        if (_vertexBuffer != nint.Zero) _backend.DestroyBuffer(_vertexBuffer);
        if (_indexBuffer != nint.Zero) _backend.DestroyBuffer(_indexBuffer);
        if (_instanceBuffer != nint.Zero) _backend.DestroyBuffer(_instanceBuffer);
        if (_materialBuffer != nint.Zero) _backend.DestroyBuffer(_materialBuffer);
        if (_frameConstantsBuffer != nint.Zero) _backend.DestroyBuffer(_frameConstantsBuffer);

        // 最后释放资源管理器（包含池）
        _resourceManager.Dispose();
    }
}
