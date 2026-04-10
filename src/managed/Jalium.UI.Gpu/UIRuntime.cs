namespace Jalium.UI.Gpu;

/// <summary>
/// UI 运行时宿主接口 - 抽象 AnimationController/InputHandler/StateManager 对运行时的依赖
/// UIRuntime (V1) 和 UIRuntimeV2 都实现此接口
/// </summary>
internal interface IUIRuntimeHost
{
    CompiledUIBundle? CurrentBundle { get; }
    IRenderBackend Backend { get; }
    nint InstanceBuffer { get; }
    AnimationController Animator { get; }
    InteractiveRegion? HitTest(float x, float y);
    void TriggerStateTransition(uint nodeId, TriggerType trigger);
    void UpdateNodeProperty(uint nodeId, AnimatableProperty property, float value);
}

/// <summary>
/// UI 运行时 - 执行编译后的 UI Bundle
/// 类似于 GPU 着色器管线的运行时
/// </summary>
public sealed class UIRuntime : IDisposable, IUIRuntimeHost
{
    private readonly IRenderBackend _backend;
    private readonly AnimationController _animationController;
    private readonly InputHandler _inputHandler;
    private readonly StateManager _stateManager;
    private readonly NodeStore _nodeStore = new();
    private readonly PropertyStore _propertyStore = new();
    private readonly ResourceStore _resourceStore = new();
    private readonly ReactiveGraph _reactiveGraph = new();
    private readonly DisplayList _displayList = new();
    private readonly Dictionary<uint, int> _nodeIndexById = new();

    private CompiledUIBundle? _currentBundle;
    private bool _isDirty = true;
    private bool _displayListDirty = true;
    private readonly Stack<Rect> _clipRectStack = new();

    // GPU 资源
    private nint _vertexBuffer;
    private nint _indexBuffer;
    private nint _instanceBuffer;
    private nint _uniformBuffer;

    // 绑定状态缓存 - 避免冗余 GPU 绑定调用
    private nint _boundVertexBuffer;
    private nint _boundIndexBuffer;
    private nint _boundInstanceBuffer;
    private nint _boundUniformBuffer;
    private nint _boundTexture0;
    private readonly Dictionary<uint, nint> _textureHandles = new();
    private readonly Dictionary<uint, nint> _glyphAtlasHandles = new();

    // IUIRuntimeHost explicit implementation
    CompiledUIBundle? IUIRuntimeHost.CurrentBundle => _currentBundle;
    IRenderBackend IUIRuntimeHost.Backend => _backend;
    nint IUIRuntimeHost.InstanceBuffer => _instanceBuffer;
    AnimationController IUIRuntimeHost.Animator => _animationController;
    InteractiveRegion? IUIRuntimeHost.HitTest(float x, float y) => HitTest(x, y);
    void IUIRuntimeHost.TriggerStateTransition(uint nodeId, TriggerType trigger) => TriggerStateTransition(nodeId, trigger);
    void IUIRuntimeHost.UpdateNodeProperty(uint nodeId, AnimatableProperty property, float value) => UpdateNodeProperty(nodeId, property, value);

    // Internal accessors for backward compatibility
    internal CompiledUIBundle? CurrentBundle => _currentBundle;
    internal IRenderBackend Backend => _backend;
    internal nint InstanceBuffer => _instanceBuffer;
    internal AnimationController Animator => _animationController;

    public UIRuntime(IRenderBackend backend)
    {
        _backend = backend;
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
            throw new InvalidOperationException($"Bundle version mismatch. Expected {RenderIR.Version}, got {bundle.Version}");
        }

        _currentBundle = bundle;
        InitializeRuntimeStores(bundle);

        // 上传 GPU 资源
        UploadResources();

        // 初始化状态机
        _stateManager.Initialize(bundle.StateTransitions);

        // 初始化动画控制器
        _animationController.Initialize(bundle.Curves, bundle.AnimationTargets, bundle.AnimationValues);

        _isDirty = true;
        _displayListDirty = true;
    }

    /// <summary>
    /// 从文件加载编译后的 Bundle
    /// </summary>
    public void LoadFromFile(string path)
    {
        var bundle = BundleSerializer.Load(path);
        LoadBundle(bundle);
    }

    private void InitializeRuntimeStores(CompiledUIBundle bundle)
    {
        _nodeStore.Load(bundle.Nodes);
        _propertyStore.Clear();
        _resourceStore.Clear();
        _reactiveGraph.Clear();
        _nodeIndexById.Clear();

        for (int i = 0; i < bundle.Nodes.Length; i++)
        {
            var node = bundle.Nodes[i];
            _nodeIndexById[node.Id] = i;

            if (node.ParentId != 0)
            {
                // Layout/render dirtiness bubbles up to parent containers.
                _reactiveGraph.AddEdge(node.Id, node.ParentId, DirtyFlags.Layout | DirtyFlags.Render);
            }
        }
    }

    private void ReleaseGpuBuffers()
    {
        if (_vertexBuffer != nint.Zero) { _backend.DestroyBuffer(_vertexBuffer); _vertexBuffer = nint.Zero; }
        if (_indexBuffer != nint.Zero) { _backend.DestroyBuffer(_indexBuffer); _indexBuffer = nint.Zero; }
        if (_instanceBuffer != nint.Zero) { _backend.DestroyBuffer(_instanceBuffer); _instanceBuffer = nint.Zero; }
        if (_uniformBuffer != nint.Zero) { _backend.DestroyBuffer(_uniformBuffer); _uniformBuffer = nint.Zero; }

        try
        {
            foreach (var handle in _textureHandles.Values)
                _backend.DestroyTexture(handle);
        }
        finally
        {
            _textureHandles.Clear();
        }

        try
        {
            foreach (var handle in _glyphAtlasHandles.Values)
                _backend.DestroyTexture(handle);
        }
        finally
        {
            _glyphAtlasHandles.Clear();
        }
    }

    private void UploadResources()
    {
        if (_currentBundle == null) return;

        // 释放旧缓冲区
        ReleaseGpuBuffers();

        // 创建顶点/索引缓冲区（矩形的标准网格）
        _vertexBuffer = _backend.CreateVertexBuffer(RectVertices);
        _indexBuffer = _backend.CreateIndexBuffer(RectIndices);

        // 创建实例缓冲区
        var instanceData = GenerateInstanceData();
        _instanceBuffer = _backend.CreateInstanceBuffer(instanceData);

        // 创建 Uniform 缓冲区（材质、变换等）
        var uniformData = GenerateUniformData();
        _uniformBuffer = _backend.CreateUniformBuffer(uniformData);

        // 上传纹理
        foreach (var (index, texRef) in _currentBundle.Textures.Select((t, i) => ((uint)i, t)))
        {
            var handle = _backend.LoadTexture(texRef.Path, texRef.Format);
            _textureHandles[index] = handle;
            _resourceStore.Set(index, handle);
        }

        // 上传字形图集
        foreach (var (index, atlasRef) in _currentBundle.GlyphAtlases.Select((a, i) => ((uint)i, a)))
        {
            var handle = _backend.CreateGlyphAtlas(atlasRef.FontId, atlasRef.FontSize, atlasRef.Width, atlasRef.Height);
            _glyphAtlasHandles[index] = handle;
            _resourceStore.Set(index + 10_000, handle);
        }
    }

    private byte[] GenerateInstanceData()
    {
        if (_currentBundle == null) return [];

        // 每个实例：位置(8) + 尺寸(8) + UV(16) + 颜色(4) + 圆角(16) + 边框(16) = 68 bytes
        // 对齐到 80 bytes
        const int instanceSize = 80;
        var data = new byte[_currentBundle.Nodes.Length * instanceSize];

        using var ms = new MemoryStream(data);
        using var writer = new BinaryWriter(ms);

        foreach (var node in _currentBundle.Nodes)
        {
            if (node is RectNode rect)
            {
                // 位置
                writer.Write(rect.Bounds.X);
                writer.Write(rect.Bounds.Y);

                // 尺寸
                writer.Write(rect.Bounds.Width);
                writer.Write(rect.Bounds.Height);

                // UV（全纹理）
                writer.Write(0f); writer.Write(0f);
                writer.Write(1f); writer.Write(1f);

                // 颜色（从材质获取）
                if (rect.MaterialIndex < 0 || rect.MaterialIndex >= _currentBundle.Materials.Length)
                    continue;
                var material = _currentBundle.Materials[rect.MaterialIndex];
                writer.Write(material.BackgroundColor);

                // 圆角
                writer.Write(rect.CornerRadius.TopLeft);
                writer.Write(rect.CornerRadius.TopRight);
                writer.Write(rect.CornerRadius.BottomRight);
                writer.Write(rect.CornerRadius.BottomLeft);

                // 边框
                writer.Write(rect.BorderThickness.Left);
                writer.Write(rect.BorderThickness.Top);
                writer.Write(rect.BorderThickness.Right);
                writer.Write(rect.BorderThickness.Bottom);

                // 边框颜色
                writer.Write(material.BorderColor);

                // 填充到 80 bytes
                writer.Write(0); // 4 bytes padding
            }
            else
            {
                // 其他节点类型的默认数据
                writer.Write(new byte[instanceSize]);
            }
        }

        return data;
    }

    private byte[] GenerateUniformData()
    {
        if (_currentBundle == null) return [];

        // Uniform 数据包含：
        // - 视口尺寸 (8 bytes)
        // - 时间 (4 bytes)
        // - 材质数组
        // - 变换矩阵数组
        // - 渐变数据

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // 视口尺寸（会在渲染时更新）
        writer.Write(1920f);
        writer.Write(1080f);

        // 时间
        writer.Write(0f);

        // 填充到 16 bytes 对齐
        writer.Write(0f);

        // 材质数组
        foreach (var material in _currentBundle.Materials)
        {
            writer.Write(material.BackgroundColor);
            writer.Write(material.BorderColor);
            writer.Write(material.ForegroundColor);
            writer.Write(material.GradientIndex);
            writer.Write(material.Opacity);
            writer.Write((byte)material.BlendMode);
            writer.Write((short)0); // padding
        }

        // 变换矩阵数组
        foreach (var f in _currentBundle.Transforms)
        {
            writer.Write(f);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 更新 - 每帧调用
    /// </summary>
    public void Update(double deltaTime)
    {
        // 动画通过 UpdateNodeProperty() 直接更新 Instance Buffer 并设置细粒度脏标记，
        // 不需要在此处设置全局 _isDirty。
        _animationController.Update(deltaTime);

        // 状态转换同理，通过动画系统更新属性。
        if (_stateManager.HasPendingTransitions)
        {
            _stateManager.ProcessTransitions();
        }

        FlushReactiveInvalidations();
    }

    /// <summary>
    /// 渲染
    /// </summary>
    public void Render(int viewportWidth, int viewportHeight)
    {
        if (_currentBundle == null) return;

        FlushReactiveInvalidations();

        if (!_isDirty && !_propertyStore.HasDirty(DirtyFlags.All))
        {
            return;
        }

        // 重置绑定状态缓存
        _boundVertexBuffer = 0;
        _boundIndexBuffer = 0;
        _boundInstanceBuffer = 0;
        _boundUniformBuffer = 0;
        _boundTexture0 = 0;

        // 更新 Uniform（视口尺寸、时间）—— 每帧都需要更新，
        // 因为 Time 用于 shader 动画效果（发光、脉冲等）。
        UpdateUniformBuffer(viewportWidth, viewportHeight);
        _isDirty = false;

        if (_displayListDirty)
        {
            RebuildDisplayList(_currentBundle);
            _displayListDirty = false;
        }

        var dirtyRenderNodes = _propertyStore.GetDirtyNodes(
            DirtyFlags.Layout | DirtyFlags.Render | DirtyFlags.Transform | DirtyFlags.Clip | DirtyFlags.Resource);

        RenderDisplayList(dirtyRenderNodes, viewportWidth, viewportHeight);

        _propertyStore.ClearDirty(DirtyFlags.All);
    }

    private void UpdateUniformBuffer(int width, int height)
    {
        // 更新视口尺寸和时间
        var data = new byte[16];
        using var ms = new MemoryStream(data);
        using var writer = new BinaryWriter(ms);

        writer.Write((float)width);
        writer.Write((float)height);
        writer.Write((float)Environment.TickCount64 / 1000.0);

        _backend.UpdateBuffer(_uniformBuffer, 0, data);
    }

    private void FlushReactiveInvalidations()
    {
        if (!_reactiveGraph.HasPendingInvalidations)
        {
            return;
        }

        _reactiveGraph.Propagate((nodeId, flags) =>
        {
            _propertyStore.MarkDirty(nodeId, flags);
            if ((flags & (DirtyFlags.Layout | DirtyFlags.Render | DirtyFlags.Transform | DirtyFlags.Clip | DirtyFlags.Resource)) != 0)
            {
                _displayListDirty = true;
            }
        });
    }

    private void RebuildDisplayList(CompiledUIBundle bundle)
    {
        _displayList.Clear();

        var hasClip = false;
        var hasTransform = false;

        foreach (var command in bundle.DrawCommands)
        {
            switch (command)
            {
                case SetClipCommand setClip:
                    if (hasClip)
                    {
                        _displayList.Add(DisplayCommand.PopClip());
                    }

                    _displayList.Add(DisplayCommand.PushClip(setClip.ClipRect));
                    hasClip = true;
                    break;

                case SetTransformCommand setTransform:
                    if (hasTransform)
                    {
                        _displayList.Add(DisplayCommand.PopTransform());
                    }

                    _displayList.Add(DisplayCommand.PushTransform(setTransform.TransformIndex));
                    hasTransform = true;
                    break;

                case DrawRectBatchCommand rectBatch:
                    _displayList.Add(DisplayCommand.DrawRect(
                        0,
                        rectBatch.InstanceBufferOffset,
                        rectBatch.InstanceCount,
                        rectBatch.TextureIndex));
                    break;

                case DrawTextBatchCommand textBatch:
                    _displayList.Add(DisplayCommand.DrawText(
                        ResolveNodeId((int)textBatch.InstanceBufferOffset),
                        textBatch.InstanceBufferOffset,
                        textBatch.GlyphCount,
                        textBatch.GlyphAtlasIndex));
                    break;

                case DrawImageBatchCommand imageBatch:
                    _displayList.Add(DisplayCommand.DrawImage(
                        0,
                        imageBatch.InstanceBufferOffset,
                        imageBatch.InstanceCount,
                        imageBatch.TextureIndex));
                    break;

                default:
                    _displayList.Add(DisplayCommand.Native(command));
                    break;
            }
        }

        if (hasTransform)
        {
            _displayList.Add(DisplayCommand.PopTransform());
        }

        if (hasClip)
        {
            _displayList.Add(DisplayCommand.PopClip());
        }
    }

    /// <summary>
    /// Precomputes a boolean array marking which node indices are dirty,
    /// enabling O(1) per-index lookup instead of O(1) HashSet per node ID.
    /// </summary>
    private bool[]? PrecomputeDirtyIndexMap(IReadOnlySet<uint> dirtyRenderNodes)
    {
        if (_currentBundle == null || dirtyRenderNodes.Count == 0) return null;

        var nodes = _currentBundle.Nodes;
        var map = new bool[nodes.Length];
        foreach (var nodeId in dirtyRenderNodes)
        {
            if (_nodeIndexById.TryGetValue(nodeId, out var index) && index < map.Length)
            {
                map[index] = true;
            }
        }
        return map;
    }

    private static bool IsBatchDirty(bool[]? dirtyIndexMap, uint instanceOffset, uint instanceCount)
    {
        if (dirtyIndexMap == null) return true;
        var end = (int)(instanceOffset + instanceCount);
        if (end > dirtyIndexMap.Length) end = dirtyIndexMap.Length;
        for (int i = (int)instanceOffset; i < end; i++)
        {
            if (dirtyIndexMap[i])
                return true;
        }
        return false;
    }

    private void RenderDisplayList(IReadOnlySet<uint> dirtyRenderNodes, int viewportWidth, int viewportHeight)
    {
        _clipRectStack.Clear();
        var shouldFilterByNode = dirtyRenderNodes.Count > 0;
        var dirtyIndexMap = shouldFilterByNode ? PrecomputeDirtyIndexMap(dirtyRenderNodes) : null;
        var hasDrawnAnything = false;

        foreach (var displayCommand in _displayList.Commands)
        {
            if (shouldFilterByNode)
            {
                if (displayCommand.NodeId != 0)
                {
                    // 单节点命令（文本等）
                    if (!dirtyRenderNodes.Contains(displayCommand.NodeId))
                        continue;
                }
                else if (displayCommand.Type is DisplayCommandType.DrawRect or DisplayCommandType.DrawImage)
                {
                    // 批次命令 - 使用预计算的脏索引位图检查
                    if (!IsBatchDirty(dirtyIndexMap, displayCommand.InstanceOffset, displayCommand.InstanceCount))
                        continue;
                }
            }

            switch (displayCommand.Type)
            {
                case DisplayCommandType.PushClip:
                    _clipRectStack.Push(displayCommand.Rect);
                    _backend.SetScissorRect(
                        (int)displayCommand.Rect.X,
                        (int)displayCommand.Rect.Y,
                        (int)displayCommand.Rect.Width,
                        (int)displayCommand.Rect.Height);
                    break;

                case DisplayCommandType.PopClip:
                    if (_clipRectStack.Count > 0)
                        _clipRectStack.Pop();
                    if (_clipRectStack.Count > 0)
                    {
                        var parentClip = _clipRectStack.Peek();
                        _backend.SetScissorRect(
                            (int)parentClip.X, (int)parentClip.Y,
                            (int)parentClip.Width, (int)parentClip.Height);
                    }
                    else
                    {
                        _backend.SetScissorRect(0, 0, viewportWidth, viewportHeight);
                    }
                    break;

                case DisplayCommandType.PushTransform:
                case DisplayCommandType.PopTransform:
                    // Transform stack is baked into instance data. Reserved for future GPU-side transform stack.
                    break;

                case DisplayCommandType.DrawRect:
                    ExecuteRectBatch(new DrawRectBatchCommand
                    {
                        InstanceBufferOffset = displayCommand.InstanceOffset,
                        InstanceCount = displayCommand.InstanceCount,
                        TextureIndex = displayCommand.ResourceIndex
                    });
                    hasDrawnAnything = true;
                    break;

                case DisplayCommandType.DrawText:
                    ExecuteTextBatch(new DrawTextBatchCommand
                    {
                        GlyphAtlasIndex = displayCommand.ResourceIndex,
                        InstanceBufferOffset = displayCommand.InstanceOffset,
                        GlyphCount = displayCommand.InstanceCount
                    });
                    hasDrawnAnything = true;
                    break;

                case DisplayCommandType.DrawImage:
                    ExecuteImageBatch(new DrawImageBatchCommand
                    {
                        TextureIndex = displayCommand.ResourceIndex,
                        InstanceBufferOffset = displayCommand.InstanceOffset,
                        InstanceCount = displayCommand.InstanceCount
                    });
                    hasDrawnAnything = true;
                    break;

                case DisplayCommandType.NativeCommand:
                    if (displayCommand.NativeCommand != null)
                    {
                        ExecuteCommand(displayCommand.NativeCommand);
                        hasDrawnAnything = true;
                    }
                    break;
            }
        }

        if (!hasDrawnAnything && _currentBundle != null && dirtyRenderNodes.Count == 0)
        {
            foreach (var command in _currentBundle.DrawCommands)
            {
                ExecuteCommand(command);
            }
        }
    }

    private uint ResolveNodeId(int instanceIndex)
    {
        if (_currentBundle == null || instanceIndex < 0 || instanceIndex >= _currentBundle.Nodes.Length)
        {
            return 0;
        }

        return _currentBundle.Nodes[instanceIndex].Id;
    }

    private void ExecuteCommand(DrawCommand command)
    {
        switch (command)
        {
            case DrawRectBatchCommand rectBatch:
                ExecuteRectBatch(rectBatch);
                break;

            case DrawTextBatchCommand textBatch:
                ExecuteTextBatch(textBatch);
                break;

            case DrawImageBatchCommand imageBatch:
                ExecuteImageBatch(imageBatch);
                break;

            case SetClipCommand setClip:
                ExecuteSetClip(setClip);
                break;

            case ApplyEffectCommand applyEffect:
                ExecuteApplyEffect(applyEffect);
                break;

            case CaptureBackdropCommand captureBackdrop:
                ExecuteCaptureBackdrop(captureBackdrop);
                break;

            case ApplyBackdropFilterCommand applyBackdropFilter:
                ExecuteApplyBackdropFilter(applyBackdropFilter);
                break;

            case CompositeLayerCommand compositeLayer:
                ExecuteCompositeLayer(compositeLayer);
                break;

            case SubmitCommand:
                _backend.Submit();
                break;
        }
    }

    private void BindCommonBuffers()
    {
        if (_boundVertexBuffer != _vertexBuffer)
        {
            _backend.BindVertexBuffer(_vertexBuffer);
            _boundVertexBuffer = _vertexBuffer;
        }
        if (_boundIndexBuffer != _indexBuffer)
        {
            _backend.BindIndexBuffer(_indexBuffer);
            _boundIndexBuffer = _indexBuffer;
        }
        if (_boundInstanceBuffer != _instanceBuffer)
        {
            _backend.BindInstanceBuffer(_instanceBuffer);
            _boundInstanceBuffer = _instanceBuffer;
        }
        if (_boundUniformBuffer != _uniformBuffer)
        {
            _backend.BindUniformBuffer(_uniformBuffer);
            _boundUniformBuffer = _uniformBuffer;
        }
    }

    private void BindTextureIfNeeded(uint textureIndex, Dictionary<uint, nint> handles)
    {
        if (textureIndex > 0 && handles.TryGetValue(textureIndex, out var textureHandle))
        {
            if (_boundTexture0 != textureHandle)
            {
                _backend.BindTexture(0, textureHandle);
                _boundTexture0 = textureHandle;
            }
        }
    }

    private void ExecuteRectBatch(DrawRectBatchCommand batch)
    {
        BindCommonBuffers();
        BindTextureIfNeeded(batch.TextureIndex, _textureHandles);

        _backend.DrawIndexedInstanced(
            indexCount: 6,
            instanceCount: batch.InstanceCount,
            firstIndex: 0,
            baseVertex: 0,
            firstInstance: batch.InstanceBufferOffset
        );
    }

    private void ExecuteImageBatch(DrawImageBatchCommand batch)
    {
        BindCommonBuffers();
        BindTextureIfNeeded(batch.TextureIndex, _textureHandles);

        _backend.DrawIndexedInstanced(
            indexCount: 6,
            instanceCount: batch.InstanceCount,
            firstIndex: 0,
            baseVertex: 0,
            firstInstance: batch.InstanceBufferOffset
        );
    }

    private void ExecuteTextBatch(DrawTextBatchCommand batch)
    {
        if (!_glyphAtlasHandles.TryGetValue(batch.GlyphAtlasIndex, out var atlasHandle))
            return;

        _backend.BindTexture(0, atlasHandle);
        _backend.DrawGlyphs(batch.InstanceBufferOffset, batch.GlyphCount);
    }

    private void ExecuteSetClip(SetClipCommand command)
    {
        _backend.SetScissorRect(
            (int)command.ClipRect.X,
            (int)command.ClipRect.Y,
            (int)command.ClipRect.Width,
            (int)command.ClipRect.Height
        );
    }

    private void ExecuteApplyEffect(ApplyEffectCommand command)
    {
        // 后处理效果
        _backend.ApplyEffect(
            command.Effect,
            command.SourceTextureIndex,
            command.DestTextureIndex,
            command.Parameters.Span
        );
    }

    private void ExecuteCaptureBackdrop(CaptureBackdropCommand command)
    {
        // 捕获当前渲染目标的指定区域到纹理
        _backend.CaptureBackdrop(
            command.Region,
            command.TargetTextureIndex
        );
    }

    private void ExecuteApplyBackdropFilter(ApplyBackdropFilterCommand command)
    {
        // 应用 Backdrop Filter
        _backend.ApplyBackdropFilter(
            command.Params,
            command.Region,
            command.BackdropTextureIndex,
            command.OutputTextureIndex,
            command.CornerRadius
        );
    }

    private void ExecuteCompositeLayer(CompositeLayerCommand command)
    {
        // 合成图层到主渲染目标
        _backend.CompositeLayer(
            command.SourceTextureIndex,
            command.DestRect,
            command.BlendMode,
            command.Opacity
        );
    }

    /// <summary>
    /// Hit testing - 查找指定位置的交互区域
    /// </summary>
    public InteractiveRegion? HitTest(float x, float y)
    {
        if (_currentBundle == null) return null;

        // 从后往前遍历（Z-order）
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
    /// 处理输入事件
    /// </summary>
    public void HandleInput(InputEvent input)
    {
        _inputHandler.Handle(input);
    }

    /// <summary>
    /// 触发状态转换
    /// </summary>
    internal void TriggerStateTransition(uint nodeId, TriggerType trigger)
    {
        _stateManager.Trigger(nodeId, trigger);
    }

    /// <summary>
    /// 更新节点属性（由动画控制器调用）
    /// </summary>
    internal void UpdateNodeProperty(uint nodeId, AnimatableProperty property, float value)
    {
        if (_currentBundle == null) return;

        if (!_nodeIndexById.TryGetValue(nodeId, out var nodeIndex))
        {
            return;
        }

        var metadata = GetPropertyMetadata(property);

        // 实例缓冲区布局（80 bytes per instance）:
        // offset 0:  位置 X (4 bytes)
        // offset 4:  位置 Y (4 bytes)
        // offset 8:  宽度 (4 bytes)
        // offset 12: 高度 (4 bytes)
        // offset 16: UV (16 bytes)
        // offset 32: 颜色 (4 bytes)
        // offset 36: 圆角 (16 bytes)
        // offset 52: 边框厚度 (16 bytes)
        // offset 68: 边框颜色 (4 bytes)
        // offset 72: padding (8 bytes)
        const int instanceSize = 80;
        var baseOffset = nodeIndex * instanceSize;

        // 根据属性类型计算偏移量并更新
        var (offset, data) = property switch
        {
            AnimatableProperty.X => (baseOffset + 0, BitConverter.GetBytes(value)),
            AnimatableProperty.Y => (baseOffset + 4, BitConverter.GetBytes(value)),
            AnimatableProperty.Width => (baseOffset + 8, BitConverter.GetBytes(value)),
            AnimatableProperty.Height => (baseOffset + 12, BitConverter.GetBytes(value)),
            AnimatableProperty.Opacity => (baseOffset + 32, UpdateColorAlpha(nodeIndex, value)),
            AnimatableProperty.CornerRadiusTopLeft => (baseOffset + 36, BitConverter.GetBytes(value)),
            AnimatableProperty.CornerRadiusTopRight => (baseOffset + 40, BitConverter.GetBytes(value)),
            AnimatableProperty.CornerRadiusBottomRight => (baseOffset + 44, BitConverter.GetBytes(value)),
            AnimatableProperty.CornerRadiusBottomLeft => (baseOffset + 48, BitConverter.GetBytes(value)),
            AnimatableProperty.BorderThicknessLeft => (baseOffset + 52, BitConverter.GetBytes(value)),
            AnimatableProperty.BorderThicknessTop => (baseOffset + 56, BitConverter.GetBytes(value)),
            AnimatableProperty.BorderThicknessRight => (baseOffset + 60, BitConverter.GetBytes(value)),
            AnimatableProperty.BorderThicknessBottom => (baseOffset + 64, BitConverter.GetBytes(value)),
            _ => (-1, Array.Empty<byte>())
        };

        if (offset >= 0 && data.Length > 0)
        {
            // 增量更新：只更新变化的部分
            _backend.UpdateBuffer(_instanceBuffer, offset, data);
        }

        _propertyStore.SetFloat(nodeId, metadata.PropertyId, value, metadata);
        _reactiveGraph.Invalidate(nodeId, metadata.DirtyFlags);

        // DisplayList 仅在 Layout/Transform/Clip 变化时重建（影响绘制命令结构）。
        // Render-only 变化（Opacity、Color、CornerRadius）只更新 Instance Buffer 数据，
        // DisplayList 结构不变，无需重建。
        _displayListDirty |= (metadata.DirtyFlags &
            (DirtyFlags.Layout | DirtyFlags.Transform | DirtyFlags.Clip)) != 0;
    }

    private static RenderPropertyMetadata GetPropertyMetadata(AnimatableProperty property)
    {
        var propertyId = unchecked((ushort)property);
        var dirtyFlags = property switch
        {
            AnimatableProperty.X or
            AnimatableProperty.Y or
            AnimatableProperty.Width or
            AnimatableProperty.Height or
            AnimatableProperty.BorderThicknessLeft or
            AnimatableProperty.BorderThicknessTop or
            AnimatableProperty.BorderThicknessRight or
            AnimatableProperty.BorderThicknessBottom => DirtyFlags.Layout | DirtyFlags.Render,

            AnimatableProperty.TranslateX or
            AnimatableProperty.TranslateY or
            AnimatableProperty.ScaleX or
            AnimatableProperty.ScaleY or
            AnimatableProperty.Rotation => DirtyFlags.Transform | DirtyFlags.Render,

            AnimatableProperty.Opacity or
            AnimatableProperty.BackgroundColor or
            AnimatableProperty.BorderColor or
            AnimatableProperty.ForegroundColor or
            AnimatableProperty.CornerRadiusTopLeft or
            AnimatableProperty.CornerRadiusTopRight or
            AnimatableProperty.CornerRadiusBottomRight or
            AnimatableProperty.CornerRadiusBottomLeft => DirtyFlags.Render,

            _ => DirtyFlags.Render
        };

        return new RenderPropertyMetadata(propertyId, dirtyFlags);
    }

    /// <summary>
    /// 更新颜色的Alpha通道
    /// </summary>
    private byte[] UpdateColorAlpha(int nodeIndex, float opacity)
    {
        if (_currentBundle == null) return [];

        var node = _currentBundle.Nodes[nodeIndex];
        if (node.MaterialIndex < 0 || node.MaterialIndex >= _currentBundle.Materials.Length)
            return [];
        var material = _currentBundle.Materials[node.MaterialIndex];

        // 从现有颜色获取RGB，用新的opacity替换Alpha
        var alpha = (byte)(Math.Clamp(opacity, 0f, 1f) * 255);
        var color = (material.BackgroundColor & 0x00FFFFFF) | ((uint)alpha << 24);

        return BitConverter.GetBytes(color);
    }

    public void Dispose()
    {
        ReleaseGpuBuffers();

        _resourceStore.Clear();
        _propertyStore.Clear();
        _nodeStore.Clear();
        _reactiveGraph.Clear();
        _displayList.Clear();
        _nodeIndexById.Clear();
    }

    // 标准矩形顶点（单位正方形）
    private static readonly float[] RectVertices =
    [
        // 位置 (x, y) + UV (u, v)
        0f, 0f, 0f, 0f,  // 左上
        1f, 0f, 1f, 0f,  // 右上
        1f, 1f, 1f, 1f,  // 右下
        0f, 1f, 0f, 1f   // 左下
    ];

    private static readonly ushort[] RectIndices = [0, 1, 2, 0, 2, 3];
}

#region Animation Controller

/// <summary>
/// 动画控制器 - 管理所有运行中的动画
/// </summary>
internal sealed class AnimationController
{
    private readonly IUIRuntimeHost _runtime;
    private AnimationCurve[] _curves = [];
    private AnimationTarget[] _targets = [];
    private byte[] _values = [];

    private readonly List<ActiveAnimation> _activeAnimations = new();

    public AnimationController(IUIRuntimeHost runtime)
    {
        _runtime = runtime;
    }

    public void Initialize(AnimationCurve[] curves, AnimationTarget[] targets, byte[] values)
    {
        _curves = curves;
        _targets = targets;
        _values = values;
    }

    public bool Update(double deltaTime)
    {
        if (_activeAnimations.Count == 0)
            return false;

        var hasUpdates = false;

        for (int i = _activeAnimations.Count - 1; i >= 0; i--)
        {
            var anim = _activeAnimations[i];
            anim.ElapsedMs += (float)(deltaTime * 1000);

            var target = _targets[anim.TargetIndex];
            var curve = _curves[target.CurveIndex];

            // 计算进度
            var progress = Math.Clamp((anim.ElapsedMs - curve.DelayMs) / curve.DurationMs, 0f, 1f);

            // 应用缓动
            var easedProgress = ApplyEasing(progress, curve);

            // 插值
            var fromValue = ReadValue(target.FromValueIndex);
            var toValue = ReadValue(target.ToValueIndex);
            var currentValue = Lerp(fromValue, toValue, easedProgress);

            // 更新节点属性
            _runtime.UpdateNodeProperty(target.NodeId, target.Property, currentValue);
            hasUpdates = true;

            // 检查是否完成
            if (progress >= 1f)
            {
                if (curve.AutoReverse && !anim.IsReversing)
                {
                    anim.IsReversing = true;
                    anim.ElapsedMs = 0;
                }
                else if (curve.RepeatCount == 0 || anim.RepeatCount < curve.RepeatCount - 1)
                {
                    anim.RepeatCount++;
                    anim.IsReversing = false;
                    anim.ElapsedMs = 0;
                }
                else
                {
                    _activeAnimations.RemoveAt(i);
                }
            }
        }

        return hasUpdates;
    }

    public void StartAnimation(uint targetIndex)
    {
        _activeAnimations.Add(new ActiveAnimation { TargetIndex = targetIndex });
    }

    private float ReadValue(uint index)
    {
        if (index * 4 + 4 > _values.Length) return 0;
        return BitConverter.ToSingle(_values, (int)(index * 4));
    }

    private static float ApplyEasing(float t, AnimationCurve curve)
    {
        return curve.Easing switch
        {
            EasingType.Linear => t,
            EasingType.EaseIn => t * t,
            EasingType.EaseOut => 1 - (1 - t) * (1 - t),
            EasingType.EaseInOut => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2,
            EasingType.EaseInCubic => t * t * t,
            EasingType.EaseOutCubic => 1 - MathF.Pow(1 - t, 3),
            EasingType.EaseInOutCubic => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2,
            EasingType.CubicBezier => CubicBezier(t, curve.P1X, curve.P1Y, curve.P2X, curve.P2Y),
            _ => t
        };
    }

    private static float CubicBezier(float t, float p1x, float p1y, float p2x, float p2y)
    {
        // 简化的三次贝塞尔曲线
        // 实际实现需要牛顿迭代法求解
        var u = 1 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;

        return 3 * uu * t * p1y + 3 * u * tt * p2y + ttt;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private sealed class ActiveAnimation
    {
        public uint TargetIndex;
        public float ElapsedMs;
        public int RepeatCount;
        public bool IsReversing;
    }
}

#endregion

#region Input Handler

/// <summary>
/// 输入处理器
/// </summary>
internal sealed class InputHandler
{
    private readonly IUIRuntimeHost _runtime;
    private InteractiveRegion? _hoveredRegion;
    private InteractiveRegion? _pressedRegion;
    private InteractiveRegion? _focusedRegion;

    public InputHandler(IUIRuntimeHost runtime)
    {
        _runtime = runtime;
    }

    public void Handle(InputEvent input)
    {
        switch (input)
        {
            case MouseMoveEvent move:
                HandleMouseMove(move);
                break;

            case MouseButtonEvent button:
                HandleMouseButton(button);
                break;

            case KeyEvent key:
                HandleKey(key);
                break;
        }
    }

    private void HandleMouseMove(MouseMoveEvent move)
    {
        var region = _runtime.HitTest(move.X, move.Y);

        if (!Equals(region, _hoveredRegion))
        {
            // 触发 MouseLeave
            if (_hoveredRegion.HasValue)
            {
                _runtime.TriggerStateTransition(_hoveredRegion.Value.NodeId, TriggerType.MouseLeave);
            }

            // 触发 MouseEnter
            if (region.HasValue && region.Value.Flags.HasFlag(InteractionFlags.Hover))
            {
                _runtime.TriggerStateTransition(region.Value.NodeId, TriggerType.MouseEnter);
            }

            _hoveredRegion = region;
        }
    }

    private void HandleMouseButton(MouseButtonEvent button)
    {
        if (button.IsDown)
        {
            var region = _runtime.HitTest(button.X, button.Y);

            if (region.HasValue)
            {
                _pressedRegion = region;

                if (region.Value.Flags.HasFlag(InteractionFlags.Click))
                {
                    _runtime.TriggerStateTransition(region.Value.NodeId, TriggerType.MouseDown);
                }

                if (region.Value.Flags.HasFlag(InteractionFlags.Focus))
                {
                    if (!Equals(_focusedRegion, region))
                    {
                        // 失去焦点
                        if (_focusedRegion.HasValue)
                        {
                            _runtime.TriggerStateTransition(_focusedRegion.Value.NodeId, TriggerType.Blur);
                        }

                        // 获得焦点
                        _runtime.TriggerStateTransition(region.Value.NodeId, TriggerType.Focus);
                        _focusedRegion = region;
                    }
                }
            }
        }
        else
        {
            if (_pressedRegion.HasValue)
            {
                _runtime.TriggerStateTransition(_pressedRegion.Value.NodeId, TriggerType.MouseUp);
                _pressedRegion = null;
            }
        }
    }

    private void HandleKey(KeyEvent key)
    {
        if (_focusedRegion.HasValue && _focusedRegion.Value.Flags.HasFlag(InteractionFlags.KeyInput))
        {
            var region = _focusedRegion.Value;

            // 触发键盘状态转换
            if (key.IsDown)
            {
                _runtime.TriggerStateTransition(region.NodeId, TriggerType.KeyDown);

                // 处理特定按键
                switch (key.KeyCode)
                {
                    case 0x09: // VK_TAB - Tab键导航
                        if (key.Shift)
                        {
                            MoveFocusToPrevious();
                        }
                        else
                        {
                            MoveFocusToNext();
                        }
                        break;

                    case 0x0D: // VK_RETURN - Enter键
                        _runtime.TriggerStateTransition(region.NodeId, TriggerType.MouseDown);
                        _runtime.TriggerStateTransition(region.NodeId, TriggerType.MouseUp);
                        break;

                    case 0x20: // VK_SPACE - 空格键激活
                        if (region.Flags.HasFlag(InteractionFlags.Click))
                        {
                            _runtime.TriggerStateTransition(region.NodeId, TriggerType.MouseDown);
                        }
                        break;

                    case 0x1B: // VK_ESCAPE - Escape键失去焦点
                        if (_focusedRegion.HasValue)
                        {
                            _runtime.TriggerStateTransition(_focusedRegion.Value.NodeId, TriggerType.Blur);
                            _focusedRegion = null;
                        }
                        break;
                }

                // 触发字符输入事件（用于文本输入控件）
                if (key.KeyCode >= 0x30 && key.KeyCode <= 0x5A) // 0-9, A-Z
                {
                    _runtime.TriggerStateTransition(region.NodeId, TriggerType.TextInput);
                }
            }
            else
            {
                _runtime.TriggerStateTransition(region.NodeId, TriggerType.KeyUp);

                // 空格键释放时触发点击
                if (key.KeyCode == 0x20 && region.Flags.HasFlag(InteractionFlags.Click))
                {
                    _runtime.TriggerStateTransition(region.NodeId, TriggerType.MouseUp);
                }
            }
        }
    }

    /// <summary>
    /// 将焦点移动到下一个可聚焦元素
    /// </summary>
    private void MoveFocusToNext()
    {
        var regions = GetFocusableRegions();
        if (regions.Length == 0) return;

        var currentIndex = -1;
        if (_focusedRegion.HasValue)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].NodeId == _focusedRegion.Value.NodeId)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        var nextIndex = (currentIndex + 1) % regions.Length;
        SetFocus(regions[nextIndex]);
    }

    /// <summary>
    /// 将焦点移动到上一个可聚焦元素
    /// </summary>
    private void MoveFocusToPrevious()
    {
        var regions = GetFocusableRegions();
        if (regions.Length == 0) return;

        var currentIndex = 0;
        if (_focusedRegion.HasValue)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].NodeId == _focusedRegion.Value.NodeId)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        var prevIndex = (currentIndex - 1 + regions.Length) % regions.Length;
        SetFocus(regions[prevIndex]);
    }

    /// <summary>
    /// 获取所有可聚焦的交互区域
    /// </summary>
    private InteractiveRegion[] GetFocusableRegions()
    {
        var bundle = _runtime.CurrentBundle;
        if (bundle == null) return [];

        return bundle.InteractiveRegions
            .Where(r => r.Flags.HasFlag(InteractionFlags.Focus))
            .OrderBy(r => r.Bounds.Y)
            .ThenBy(r => r.Bounds.X)
            .ToArray();
    }

    /// <summary>
    /// 设置焦点到指定区域
    /// </summary>
    private void SetFocus(InteractiveRegion region)
    {
        // 失去当前焦点
        if (_focusedRegion.HasValue)
        {
            _runtime.TriggerStateTransition(_focusedRegion.Value.NodeId, TriggerType.Blur);
        }

        // 获得新焦点
        _runtime.TriggerStateTransition(region.NodeId, TriggerType.Focus);
        _focusedRegion = region;
    }
}

#endregion

#region State Manager

/// <summary>
/// 状态管理器 - 管理 UI 状态机
/// </summary>
internal sealed class StateManager
{
    private readonly IUIRuntimeHost _runtime;
    private StateTransition[] _transitions = [];
    private readonly Dictionary<uint, uint> _nodeStates = new(); // nodeId -> stateId
    private readonly Queue<(uint nodeId, TriggerType trigger)> _pendingTriggers = new();

    public bool HasPendingTransitions => _pendingTriggers.Count > 0;

    public StateManager(IUIRuntimeHost runtime)
    {
        _runtime = runtime;
    }

    public void Initialize(StateTransition[] transitions)
    {
        _transitions = transitions;
        _nodeStates.Clear();
        _pendingTriggers.Clear();
    }

    public void Trigger(uint nodeId, TriggerType trigger)
    {
        _pendingTriggers.Enqueue((nodeId, trigger));
    }

    public void ProcessTransitions()
    {
        while (_pendingTriggers.TryDequeue(out var pending))
        {
            var currentState = _nodeStates.TryGetValue(pending.nodeId, out var state) ? state : 0u;

            // 查找匹配的转换
            foreach (var transition in _transitions)
            {
                if (transition.Trigger == pending.trigger &&
                    transition.FromStateId == currentState)
                {
                    // 执行转换
                    _nodeStates[pending.nodeId] = transition.ToStateId;

                    // 启动动画：遍历该转换关联的所有动画目标
                    if (transition.AnimationCount > 0)
                    {
                        for (uint i = 0; i < transition.AnimationCount; i++)
                        {
                            var animTargetIndex = transition.AnimationStartIndex + i;
                            _runtime.Animator.StartAnimation(animTargetIndex);
                        }
                    }

                    // 应用即时材质更新（用于非动画的状态变化，如颜色切换）
                    if (transition.MaterialUpdateCount > 0)
                    {
                        ApplyMaterialUpdates(pending.nodeId, transition.MaterialUpdateStartIndex, transition.MaterialUpdateCount);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// 应用材质即时更新
    /// </summary>
    private void ApplyMaterialUpdates(uint nodeId, uint startIndex, uint count)
    {
        // 查找节点以获取其在实例缓冲区中的索引
        var bundle = _runtime.CurrentBundle;
        if (bundle == null) return;

        var nodeIndex = -1;
        for (int i = 0; i < bundle.Nodes.Length; i++)
        {
            if (bundle.Nodes[i].Id == nodeId)
            {
                nodeIndex = i;
                break;
            }
        }

        if (nodeIndex < 0) return;

        // 获取更新的材质并应用到实例缓冲区
        // 材质更新存储在动画值数组中，每个更新包含：颜色(4 bytes)
        for (uint i = 0; i < count; i++)
        {
            var updateIndex = startIndex + i;
            var offset = (int)(updateIndex * 4);

            if (offset + 4 <= bundle.AnimationValues.Length)
            {
                var color = BitConverter.ToUInt32(bundle.AnimationValues, offset);

                // 更新实例缓冲区中的颜色（offset 32）
                const int instanceSize = 80;
                var bufferOffset = nodeIndex * instanceSize + 32;
                _runtime.Backend.UpdateBuffer(
                    _runtime.InstanceBuffer,
                    bufferOffset,
                    BitConverter.GetBytes(color)
                );
            }
        }
    }
}

#endregion

#region Input Events

/// <summary>
/// 输入事件基类
/// </summary>
public abstract class InputEvent { }

/// <summary>
/// 鼠标移动事件
/// </summary>
public sealed class MouseMoveEvent : InputEvent
{
    public required float X { get; init; }
    public required float Y { get; init; }
}

/// <summary>
/// 鼠标按钮事件
/// </summary>
public sealed class MouseButtonEvent : InputEvent
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public required int Button { get; init; }
    public required bool IsDown { get; init; }
}

/// <summary>
/// 键盘事件
/// </summary>
public sealed class KeyEvent : InputEvent
{
    public required int KeyCode { get; init; }
    public required bool IsDown { get; init; }
    public required bool Alt { get; init; }
    public required bool Ctrl { get; init; }
    public required bool Shift { get; init; }
}

#endregion

#region Render Backend Interface

/// <summary>
/// 渲染后端接口 - 抽象 GPU API
/// </summary>
public interface IRenderBackend
{
    // 缓冲区管理
    nint CreateVertexBuffer(ReadOnlySpan<float> data);
    nint CreateIndexBuffer(ReadOnlySpan<ushort> data);
    nint CreateInstanceBuffer(ReadOnlySpan<byte> data);
    nint CreateUniformBuffer(ReadOnlySpan<byte> data);
    void UpdateBuffer(nint buffer, int offset, ReadOnlySpan<byte> data);
    void DestroyBuffer(nint buffer);

    // 纹理管理
    nint LoadTexture(string path, TextureFormat format);
    nint CreateGlyphAtlas(string fontId, float fontSize, int width, int height);
    nint CreateRenderTargetTexture(int width, int height, TextureFormat format);
    void DestroyTexture(nint texture);

    // 绑定
    void BindVertexBuffer(nint buffer);
    void BindIndexBuffer(nint buffer);
    void BindInstanceBuffer(nint buffer);
    void BindUniformBuffer(nint buffer);
    void BindTexture(int slot, nint texture);

    // 状态
    void SetScissorRect(int x, int y, int width, int height);
    void SetViewport(int x, int y, int width, int height);

    // 绘制
    void DrawIndexedInstanced(uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance);
    void DrawGlyphs(uint offset, uint count);

    // 效果
    void ApplyEffect(EffectType effect, uint sourceTexture, uint destTexture, ReadOnlySpan<byte> parameters);

    // Backdrop Filter
    /// <summary>
    /// 捕获当前渲染目标的指定区域到纹理
    /// </summary>
    void CaptureBackdrop(Rect region, uint targetTextureIndex);

    /// <summary>
    /// 应用 Backdrop Filter 效果
    /// </summary>
    void ApplyBackdropFilter(
        BackdropFilterParams filterParams,
        Rect region,
        uint sourceTextureIndex,
        uint destTextureIndex,
        CornerRadius cornerRadius);

    /// <summary>
    /// 合成图层到主渲染目标
    /// </summary>
    void CompositeLayer(
        uint sourceTextureIndex,
        Rect destRect,
        BlendMode blendMode,
        byte opacity);

    // 提交
    void Submit();
}

#endregion

#region Bundle Serialization

/// <summary>
/// Bundle 序列化器
/// </summary>
public static class BundleSerializer
{
    private const ushort CurrentVersion = (ushort)RenderIR.Version;
    private const ushort LegacyInteractiveRegionVersion = 1;
    private const ushort LegacyNoPathDataVersion = 2;
    private static ReadOnlySpan<byte> JuibMagic => "JUIB"u8;
    private static ReadOnlySpan<byte> LegacyUicMagic => "JUIC"u8;
    private static ReadOnlySpan<byte> LegacyJuibMagicFromUInt32 => [0x42, 0x49, 0x55, 0x4A];

    // 节点类型标识
    private const byte NodeType_Rect = 1;
    private const byte NodeType_Text = 2;
    private const byte NodeType_Image = 3;
    private const byte NodeType_Path = 4;
    private const byte NodeType_Effect = 5;
    private const byte NodeType_BackdropFilter = 6;

    // 绘制命令类型标识
    private const byte CmdType_SetRenderTarget = 1;
    private const byte CmdType_Clear = 2;
    private const byte CmdType_SetClip = 3;
    private const byte CmdType_SetTransform = 4;
    private const byte CmdType_DrawRectBatch = 5;
    private const byte CmdType_DrawTextBatch = 6;
    private const byte CmdType_DrawImageBatch = 7;
    private const byte CmdType_DrawPath = 8;
    private const byte CmdType_ApplyEffect = 9;
    private const byte CmdType_CompositeLayer = 10;
    private const byte CmdType_Submit = 11;
    private const byte CmdType_ApplyBackdropFilter = 12;
    private const byte CmdType_CaptureBackdrop = 13;

    /// <summary>
    /// 保存 Bundle 到文件
    /// </summary>
    public static void Save(CompiledUIBundle bundle, string path)
    {
        using var fs = File.Create(path);
        Save(bundle, fs);
    }

    /// <summary>
    /// 保存 Bundle 到流
    /// </summary>
    public static void Save(CompiledUIBundle bundle, Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // 写入头部
        WriteHeader(writer);

        // 写入各部分
        WriteNodeArray(writer, bundle.Nodes);
        WriteMaterialArray(writer, bundle.Materials);
        WriteGradientArray(writer, bundle.Gradients);
        WriteGradientStopArray(writer, bundle.GradientStops);
        WriteCurveArray(writer, bundle.Curves);
        WriteAnimationTargetArray(writer, bundle.AnimationTargets);
        WriteFloatArray(writer, bundle.Transforms);
        WriteByteArray(writer, bundle.AnimationValues);
        WriteDrawCommandArray(writer, bundle.DrawCommands);
        WriteTextureRefArray(writer, bundle.Textures);
        WriteGlyphAtlasRefArray(writer, bundle.GlyphAtlases);
        WritePathCacheArray(writer, bundle.PathCaches);
        WriteByteArray(writer, bundle.VertexData);
        WriteUShortArray(writer, bundle.IndexData);
        WriteStringArray(writer, bundle.PathDataStrings);
        WriteInteractiveRegionArray(writer, bundle.InteractiveRegions);
        WriteStateTransitionArray(writer, bundle.StateTransitions);
        WriteBackdropFilterParamsArray(writer, bundle.BackdropFilterParams);
    }

    /// <summary>
    /// 从文件加载 Bundle
    /// </summary>
    public static CompiledUIBundle Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>
    /// 从内存加载 Bundle（用于 Source Generator 嵌入的二进制数据）
    /// </summary>
    public static CompiledUIBundle Load(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        return Load(ms);
    }

    /// <summary>
    /// 从流加载 Bundle
    /// </summary>
    public static CompiledUIBundle Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // 验证头部
        var version = ReadAndValidateHeader(reader);

        // 读取各部分
        var nodes = ReadNodeArray(reader);
        var materials = ReadMaterialArray(reader);
        var gradients = ReadGradientArray(reader);
        var gradientStops = ReadGradientStopArray(reader);
        var curves = ReadCurveArray(reader);
        var animTargets = ReadAnimationTargetArray(reader);
        var transforms = ReadFloatArray(reader);
        var animValues = ReadByteArray(reader);
        var drawCmds = ReadDrawCommandArray(reader);
        var textures = ReadTextureRefArray(reader);
        var glyphAtlases = ReadGlyphAtlasRefArray(reader);
        var pathCaches = ReadPathCacheArray(reader);

        // v3+ 包含顶点/索引缓冲区和原始路径数据
        byte[] vertexData = [];
        ushort[] indexData = [];
        string[] pathDataStrings = [];
        if (version >= 3)
        {
            vertexData = ReadByteArray(reader);
            indexData = ReadUShortArray(reader);
            pathDataStrings = ReadStringArray(reader);
        }

        return new CompiledUIBundle
        {
            Version = RenderIR.Version,
            Nodes = nodes,
            Materials = materials,
            Gradients = gradients,
            GradientStops = gradientStops,
            Curves = curves,
            AnimationTargets = animTargets,
            Transforms = transforms,
            AnimationValues = animValues,
            DrawCommands = drawCmds,
            Textures = textures,
            GlyphAtlases = glyphAtlases,
            PathCaches = pathCaches,
            VertexData = vertexData,
            IndexData = indexData,
            PathDataStrings = pathDataStrings,
            InteractiveRegions = ReadInteractiveRegionArray(reader, version),
            StateTransitions = ReadStateTransitionArray(reader),
            BackdropFilterParams = ReadBackdropFilterParamsArray(reader)
        };
    }

    private static void WriteHeader(BinaryWriter writer)
    {
        writer.Write(JuibMagic);
        writer.Write(CurrentVersion);
    }

    private static ushort ReadAndValidateHeader(BinaryReader reader)
    {
        Span<byte> magic = stackalloc byte[4];
        var bytesRead = reader.Read(magic);
        if (bytesRead != magic.Length)
            throw new InvalidDataException("Invalid bundle file format: missing header.");

        if (!IsSupportedMagic(magic))
            throw new InvalidDataException("Invalid bundle file format.");

        var version = reader.ReadUInt16();
        if (version != CurrentVersion && version != LegacyInteractiveRegionVersion
            && version != LegacyNoPathDataVersion)
            throw new InvalidDataException($"Unsupported bundle version: {version}");

        return version;
    }

    private static bool IsSupportedMagic(ReadOnlySpan<byte> magic)
    {
        return magic.SequenceEqual(JuibMagic) ||
               magic.SequenceEqual(LegacyUicMagic) ||
               magic.SequenceEqual(LegacyJuibMagicFromUInt32);
    }

    #region Node Serialization

    private static void WriteNodeArray(BinaryWriter writer, SceneNode[] nodes)
    {
        writer.Write(nodes.Length);
        foreach (var node in nodes)
        {
            WriteNode(writer, node);
        }
    }

    private static void WriteNode(BinaryWriter writer, SceneNode node)
    {
        // 写入公共字段
        switch (node)
        {
            case RectNode rect:
                writer.Write(NodeType_Rect);
                WriteBaseNode(writer, node);
                WriteRect(writer, rect.Bounds);
                WriteCornerRadius(writer, rect.CornerRadius);
                WriteThickness(writer, rect.BorderThickness);
                break;

            case TextNode text:
                writer.Write(NodeType_Text);
                WriteBaseNode(writer, node);
                writer.Write(text.TextHash);
                writer.Write(text.GlyphAtlasIndex);
                writer.Write(text.GlyphRunIndex);
                WriteRect(writer, text.Bounds);
                break;

            case ImageNode image:
                writer.Write(NodeType_Image);
                WriteBaseNode(writer, node);
                writer.Write(image.TextureIndex);
                WriteRect(writer, image.UVRect);
                WriteRect(writer, image.Bounds);
                WriteThickness(writer, image.NineSlice);
                break;

            case PathNode path:
                writer.Write(NodeType_Path);
                WriteBaseNode(writer, node);
                writer.Write(path.PathCacheIndex);
                WriteRect(writer, path.Bounds);
                writer.Write((byte)path.FillRule);
                break;

            case EffectNode effect:
                writer.Write(NodeType_Effect);
                WriteBaseNode(writer, node);
                writer.Write((byte)effect.EffectType);
                writer.Write(effect.EffectParamsIndex);
                writer.Write(effect.TargetNodeId);
                break;

            case BackdropFilterNode backdropFilter:
                writer.Write(NodeType_BackdropFilter);
                WriteBaseNode(writer, node);
                WriteBackdropFilterParams(writer, backdropFilter.Params);
                WriteRect(writer, backdropFilter.FilterRegion);
                writer.Write(backdropFilter.TargetNodeId);
                writer.Write(backdropFilter.InheritTransform);
                WriteCornerRadius(writer, backdropFilter.CornerRadius);
                break;
        }
    }

    private static void WriteBaseNode(BinaryWriter writer, SceneNode node)
    {
        writer.Write(node.Id);
        writer.Write(node.ParentId);
        writer.Write(node.TransformIndex);
        writer.Write(node.MaterialIndex);
        writer.Write(node.ClipIndex);
        writer.Write(node.IsVisible);
        writer.Write(node.ZIndex);
    }

    private static SceneNode[] ReadNodeArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var nodes = new SceneNode[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = ReadNode(reader);
        }
        return nodes;
    }

    private static SceneNode ReadNode(BinaryReader reader)
    {
        var nodeType = reader.ReadByte();

        var id = reader.ReadUInt32();
        var parentId = reader.ReadUInt32();
        var transformIndex = reader.ReadUInt32();
        var materialIndex = reader.ReadUInt32();
        var clipIndex = reader.ReadUInt32();
        var isVisible = reader.ReadBoolean();
        var zIndex = reader.ReadInt32();

        return nodeType switch
        {
            NodeType_Rect => new RectNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                Bounds = ReadRect(reader),
                CornerRadius = ReadCornerRadius(reader),
                BorderThickness = ReadThickness(reader)
            },
            NodeType_Text => new TextNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                TextHash = reader.ReadUInt64(),
                GlyphAtlasIndex = reader.ReadUInt32(),
                GlyphRunIndex = reader.ReadUInt32(),
                Bounds = ReadRect(reader)
            },
            NodeType_Image => new ImageNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                TextureIndex = reader.ReadUInt32(),
                UVRect = ReadRect(reader),
                Bounds = ReadRect(reader),
                NineSlice = ReadThickness(reader)
            },
            NodeType_Path => new PathNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                PathCacheIndex = reader.ReadUInt32(),
                Bounds = ReadRect(reader),
                FillRule = (FillRule)reader.ReadByte()
            },
            NodeType_Effect => new EffectNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                EffectType = (EffectType)reader.ReadByte(),
                EffectParamsIndex = reader.ReadUInt32(),
                TargetNodeId = reader.ReadUInt32()
            },
            NodeType_BackdropFilter => new BackdropFilterNode
            {
                Id = id,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                ClipIndex = clipIndex,
                IsVisible = isVisible,
                ZIndex = zIndex,
                Params = ReadBackdropFilterParams(reader),
                FilterRegion = ReadRect(reader),
                TargetNodeId = reader.ReadUInt32(),
                InheritTransform = reader.ReadBoolean(),
                CornerRadius = ReadCornerRadius(reader)
            },
            _ => throw new InvalidDataException($"Unknown node type: {nodeType}")
        };
    }

    #endregion

    #region Material Serialization

    private static void WriteMaterialArray(BinaryWriter writer, Material[] materials)
    {
        writer.Write(materials.Length);
        foreach (var m in materials)
        {
            writer.Write(m.BackgroundColor);
            writer.Write(m.BorderColor);
            writer.Write(m.ForegroundColor);
            writer.Write(m.GradientIndex);
            writer.Write(m.Opacity);
            writer.Write((byte)m.BlendMode);
        }
    }

    private static Material[] ReadMaterialArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var materials = new Material[count];
        for (int i = 0; i < count; i++)
        {
            materials[i] = new Material(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadByte(),
                (BlendMode)reader.ReadByte()
            );
        }
        return materials;
    }

    #endregion

    #region Gradient Serialization

    private static void WriteGradientArray(BinaryWriter writer, GradientDef[] gradients)
    {
        writer.Write(gradients.Length);
        foreach (var g in gradients)
        {
            writer.Write((byte)g.Type);
            writer.Write(g.Start.X);
            writer.Write(g.Start.Y);
            writer.Write(g.End.X);
            writer.Write(g.End.Y);
            writer.Write(g.StopsIndex);
            writer.Write(g.StopsCount);
        }
    }

    private static GradientDef[] ReadGradientArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var gradients = new GradientDef[count];
        for (int i = 0; i < count; i++)
        {
            gradients[i] = new GradientDef(
                (GradientType)reader.ReadByte(),
                new Point(reader.ReadSingle(), reader.ReadSingle()),
                new Point(reader.ReadSingle(), reader.ReadSingle()),
                reader.ReadUInt32(),
                reader.ReadByte()
            );
        }
        return gradients;
    }

    private static void WriteGradientStopArray(BinaryWriter writer, GradientStop[] stops)
    {
        writer.Write(stops.Length);
        foreach (var s in stops)
        {
            writer.Write(s.Offset);
            writer.Write(s.Color);
        }
    }

    private static GradientStop[] ReadGradientStopArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var stops = new GradientStop[count];
        for (int i = 0; i < count; i++)
        {
            stops[i] = new GradientStop(reader.ReadSingle(), reader.ReadUInt32());
        }
        return stops;
    }

    #endregion

    #region Animation Serialization

    private static void WriteCurveArray(BinaryWriter writer, AnimationCurve[] curves)
    {
        writer.Write(curves.Length);
        foreach (var c in curves)
        {
            writer.Write((byte)c.Easing);
            writer.Write(c.P1X);
            writer.Write(c.P1Y);
            writer.Write(c.P2X);
            writer.Write(c.P2Y);
            writer.Write(c.DurationMs);
            writer.Write(c.DelayMs);
            writer.Write(c.RepeatCount);
            writer.Write(c.AutoReverse);
        }
    }

    private static AnimationCurve[] ReadCurveArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var curves = new AnimationCurve[count];
        for (int i = 0; i < count; i++)
        {
            var easing = (EasingType)reader.ReadByte();
            var p1x = reader.ReadSingle();
            var p1y = reader.ReadSingle();
            var p2x = reader.ReadSingle();
            var p2y = reader.ReadSingle();
            var duration = reader.ReadUInt32();
            var delay = reader.ReadUInt32();
            var repeat = reader.ReadByte();
            var autoReverse = reader.ReadBoolean();

            curves[i] = easing == EasingType.CubicBezier
                ? new AnimationCurve(p1x, p1y, p2x, p2y, duration, delay, repeat, autoReverse)
                : new AnimationCurve(easing, duration, delay, repeat, autoReverse);
        }
        return curves;
    }

    private static void WriteAnimationTargetArray(BinaryWriter writer, AnimationTarget[] targets)
    {
        writer.Write(targets.Length);
        foreach (var t in targets)
        {
            writer.Write(t.NodeId);
            writer.Write((byte)t.Property);
            writer.Write(t.FromValueIndex);
            writer.Write(t.ToValueIndex);
            writer.Write(t.CurveIndex);
        }
    }

    private static AnimationTarget[] ReadAnimationTargetArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var targets = new AnimationTarget[count];
        for (int i = 0; i < count; i++)
        {
            targets[i] = new AnimationTarget(
                reader.ReadUInt32(),
                (AnimatableProperty)reader.ReadByte(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32()
            );
        }
        return targets;
    }

    #endregion

    #region Draw Command Serialization

    private static void WriteDrawCommandArray(BinaryWriter writer, DrawCommand[] commands)
    {
        writer.Write(commands.Length);
        foreach (var cmd in commands)
        {
            WriteDrawCommand(writer, cmd);
        }
    }

    private static void WriteDrawCommand(BinaryWriter writer, DrawCommand cmd)
    {
        switch (cmd)
        {
            case SetRenderTargetCommand setRt:
                writer.Write(CmdType_SetRenderTarget);
                writer.Write(setRt.RenderTargetIndex);
                writer.Write(setRt.Clear);
                writer.Write(setRt.ClearColor);
                break;

            case ClearCommand clear:
                writer.Write(CmdType_Clear);
                writer.Write(clear.Color);
                break;

            case SetClipCommand setClip:
                writer.Write(CmdType_SetClip);
                WriteRect(writer, setClip.ClipRect);
                writer.Write(setClip.Intersect);
                break;

            case SetTransformCommand setTrans:
                writer.Write(CmdType_SetTransform);
                writer.Write(setTrans.TransformIndex);
                break;

            case DrawRectBatchCommand drawRect:
                writer.Write(CmdType_DrawRectBatch);
                writer.Write(drawRect.InstanceBufferOffset);
                writer.Write(drawRect.InstanceCount);
                writer.Write(drawRect.TextureIndex);
                break;

            case DrawTextBatchCommand drawText:
                writer.Write(CmdType_DrawTextBatch);
                writer.Write(drawText.GlyphAtlasIndex);
                writer.Write(drawText.InstanceBufferOffset);
                writer.Write(drawText.GlyphCount);
                break;

            case DrawImageBatchCommand drawImage:
                writer.Write(CmdType_DrawImageBatch);
                writer.Write(drawImage.TextureIndex);
                writer.Write(drawImage.InstanceBufferOffset);
                writer.Write(drawImage.InstanceCount);
                break;

            case DrawPathCommand drawPath:
                writer.Write(CmdType_DrawPath);
                writer.Write(drawPath.PathCacheIndex);
                writer.Write(drawPath.MaterialIndex);
                writer.Write(drawPath.TransformIndex);
                break;

            case ApplyEffectCommand applyEffect:
                writer.Write(CmdType_ApplyEffect);
                writer.Write((byte)applyEffect.Effect);
                writer.Write(applyEffect.SourceTextureIndex);
                writer.Write(applyEffect.DestTextureIndex);
                var paramSpan = applyEffect.Parameters.Span;
                writer.Write(paramSpan.Length);
                writer.Write(paramSpan);
                break;

            case CompositeLayerCommand composite:
                writer.Write(CmdType_CompositeLayer);
                writer.Write(composite.SourceTextureIndex);
                writer.Write((byte)composite.BlendMode);
                writer.Write(composite.Opacity);
                WriteRect(writer, composite.DestRect);
                break;

            case SubmitCommand:
                writer.Write(CmdType_Submit);
                break;

            case ApplyBackdropFilterCommand applyBackdrop:
                writer.Write(CmdType_ApplyBackdropFilter);
                WriteBackdropFilterParams(writer, applyBackdrop.Params);
                WriteRect(writer, applyBackdrop.Region);
                writer.Write(applyBackdrop.BackdropTextureIndex);
                writer.Write(applyBackdrop.OutputTextureIndex);
                WriteCornerRadius(writer, applyBackdrop.CornerRadius);
                break;

            case CaptureBackdropCommand captureBackdrop:
                writer.Write(CmdType_CaptureBackdrop);
                WriteRect(writer, captureBackdrop.Region);
                writer.Write(captureBackdrop.TargetTextureIndex);
                break;
        }
    }

    private static DrawCommand[] ReadDrawCommandArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var commands = new DrawCommand[count];
        for (int i = 0; i < count; i++)
        {
            commands[i] = ReadDrawCommand(reader);
        }
        return commands;
    }

    private static DrawCommand ReadDrawCommand(BinaryReader reader)
    {
        var cmdType = reader.ReadByte();
        return cmdType switch
        {
            CmdType_SetRenderTarget => new SetRenderTargetCommand
            {
                RenderTargetIndex = reader.ReadUInt32(),
                Clear = reader.ReadBoolean(),
                ClearColor = reader.ReadUInt32()
            },
            CmdType_Clear => new ClearCommand { Color = reader.ReadUInt32() },
            CmdType_SetClip => new SetClipCommand
            {
                ClipRect = ReadRect(reader),
                Intersect = reader.ReadBoolean()
            },
            CmdType_SetTransform => new SetTransformCommand { TransformIndex = reader.ReadUInt32() },
            CmdType_DrawRectBatch => new DrawRectBatchCommand
            {
                InstanceBufferOffset = reader.ReadUInt32(),
                InstanceCount = reader.ReadUInt32(),
                TextureIndex = reader.ReadUInt32()
            },
            CmdType_DrawTextBatch => new DrawTextBatchCommand
            {
                GlyphAtlasIndex = reader.ReadUInt32(),
                InstanceBufferOffset = reader.ReadUInt32(),
                GlyphCount = reader.ReadUInt32()
            },
            CmdType_DrawImageBatch => new DrawImageBatchCommand
            {
                TextureIndex = reader.ReadUInt32(),
                InstanceBufferOffset = reader.ReadUInt32(),
                InstanceCount = reader.ReadUInt32()
            },
            CmdType_DrawPath => new DrawPathCommand
            {
                PathCacheIndex = reader.ReadUInt32(),
                MaterialIndex = reader.ReadUInt32(),
                TransformIndex = reader.ReadUInt32()
            },
            CmdType_ApplyEffect => new ApplyEffectCommand
            {
                Effect = (EffectType)reader.ReadByte(),
                SourceTextureIndex = reader.ReadUInt32(),
                DestTextureIndex = reader.ReadUInt32(),
                Parameters = reader.ReadBytes(reader.ReadInt32())
            },
            CmdType_CompositeLayer => new CompositeLayerCommand
            {
                SourceTextureIndex = reader.ReadUInt32(),
                BlendMode = (BlendMode)reader.ReadByte(),
                Opacity = reader.ReadByte(),
                DestRect = ReadRect(reader)
            },
            CmdType_Submit => new SubmitCommand(),
            CmdType_ApplyBackdropFilter => new ApplyBackdropFilterCommand
            {
                Params = ReadBackdropFilterParams(reader),
                Region = ReadRect(reader),
                BackdropTextureIndex = reader.ReadUInt32(),
                OutputTextureIndex = reader.ReadUInt32(),
                CornerRadius = ReadCornerRadius(reader)
            },
            CmdType_CaptureBackdrop => new CaptureBackdropCommand
            {
                Region = ReadRect(reader),
                TargetTextureIndex = reader.ReadUInt32()
            },
            _ => throw new InvalidDataException($"Unknown command type: {cmdType}")
        };
    }

    #endregion

    #region Resource Reference Serialization

    private static void WriteTextureRefArray(BinaryWriter writer, TextureRef[] textures)
    {
        writer.Write(textures.Length);
        foreach (var t in textures)
        {
            WriteString(writer, t.Path);
            writer.Write(t.Width);
            writer.Write(t.Height);
            writer.Write((byte)t.Format);
        }
    }

    private static TextureRef[] ReadTextureRefArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var textures = new TextureRef[count];
        for (int i = 0; i < count; i++)
        {
            textures[i] = new TextureRef(
                ReadString(reader),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                (TextureFormat)reader.ReadByte()
            );
        }
        return textures;
    }

    private static void WriteGlyphAtlasRefArray(BinaryWriter writer, GlyphAtlasRef[] atlases)
    {
        writer.Write(atlases.Length);
        foreach (var a in atlases)
        {
            WriteString(writer, a.FontId);
            writer.Write(a.FontSize);
            writer.Write(a.Width);
            writer.Write(a.Height);
        }
    }

    private static GlyphAtlasRef[] ReadGlyphAtlasRefArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var atlases = new GlyphAtlasRef[count];
        for (int i = 0; i < count; i++)
        {
            atlases[i] = new GlyphAtlasRef(
                ReadString(reader),
                reader.ReadSingle(),
                reader.ReadUInt16(),
                reader.ReadUInt16()
            );
        }
        return atlases;
    }

    private static void WritePathCacheArray(BinaryWriter writer, PathCache[] paths)
    {
        writer.Write(paths.Length);
        foreach (var p in paths)
        {
            writer.Write(p.PathHash);
            writer.Write(p.VertexOffset);
            writer.Write(p.VertexCount);
            writer.Write(p.IndexOffset);
            writer.Write(p.IndexCount);
        }
    }

    private static PathCache[] ReadPathCacheArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var paths = new PathCache[count];
        for (int i = 0; i < count; i++)
        {
            paths[i] = new PathCache(
                reader.ReadUInt64(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32()
            );
        }
        return paths;
    }

    #endregion

    #region Interaction Serialization

    private static void WriteInteractiveRegionArray(BinaryWriter writer, InteractiveRegion[] regions)
    {
        writer.Write(regions.Length);
        foreach (var r in regions)
        {
            writer.Write(r.NodeId);
            WriteRect(writer, r.Bounds);
            writer.Write((byte)r.Flags);
            writer.Write(r.HandlerIndex);
            WriteRect(writer, r.ClipBounds);
        }
    }

    private static InteractiveRegion[] ReadInteractiveRegionArray(BinaryReader reader, ushort version)
    {
        var count = reader.ReadInt32();
        var regions = new InteractiveRegion[count];
        for (int i = 0; i < count; i++)
        {
            regions[i] = new InteractiveRegion(
                reader.ReadUInt32(),
                ReadRect(reader),
                (InteractionFlags)reader.ReadByte(),
                reader.ReadUInt32(),
                version > LegacyInteractiveRegionVersion ? ReadRect(reader) : Rect.Empty
            );
        }
        return regions;
    }

    private static void WriteStateTransitionArray(BinaryWriter writer, StateTransition[] transitions)
    {
        writer.Write(transitions.Length);
        foreach (var t in transitions)
        {
            writer.Write((byte)t.Trigger);
            writer.Write(t.FromStateId);
            writer.Write(t.ToStateId);
            writer.Write(t.AnimationStartIndex);
            writer.Write(t.AnimationCount);
            writer.Write(t.MaterialUpdateStartIndex);
            writer.Write(t.MaterialUpdateCount);
        }
    }

    private static StateTransition[] ReadStateTransitionArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var transitions = new StateTransition[count];
        for (int i = 0; i < count; i++)
        {
            transitions[i] = new StateTransition(
                (TriggerType)reader.ReadByte(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt32()
            );
        }
        return transitions;
    }

    #endregion

    #region Primitive Serialization

    private static void WriteFloatArray(BinaryWriter writer, float[] arr)
    {
        writer.Write(arr.Length);
        foreach (var f in arr) writer.Write(f);
    }

    private static float[] ReadFloatArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var arr = new float[count];
        for (int i = 0; i < count; i++) arr[i] = reader.ReadSingle();
        return arr;
    }

    private static void WriteByteArray(BinaryWriter writer, byte[] arr)
    {
        writer.Write(arr.Length);
        writer.Write(arr);
    }

    private static byte[] ReadByteArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        return reader.ReadBytes(count);
    }

    private static void WriteUShortArray(BinaryWriter writer, ushort[] arr)
    {
        writer.Write(arr.Length);
        foreach (var v in arr)
            writer.Write(v);
    }

    private static ushort[] ReadUShortArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var arr = new ushort[count];
        for (int i = 0; i < count; i++)
            arr[i] = reader.ReadUInt16();
        return arr;
    }

    private static void WriteStringArray(BinaryWriter writer, string[] arr)
    {
        writer.Write(arr.Length);
        foreach (var s in arr)
            writer.Write(s ?? string.Empty);
    }

    private static string[] ReadStringArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var arr = new string[count];
        for (int i = 0; i < count; i++)
            arr[i] = reader.ReadString();
        return arr;
    }

    private static void WriteRect(BinaryWriter writer, Rect rect)
    {
        writer.Write(rect.X);
        writer.Write(rect.Y);
        writer.Write(rect.Width);
        writer.Write(rect.Height);
    }

    private static Rect ReadRect(BinaryReader reader)
    {
        return new Rect(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteCornerRadius(BinaryWriter writer, CornerRadius cr)
    {
        writer.Write(cr.TopLeft);
        writer.Write(cr.TopRight);
        writer.Write(cr.BottomRight);
        writer.Write(cr.BottomLeft);
    }

    private static CornerRadius ReadCornerRadius(BinaryReader reader)
    {
        return new CornerRadius(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteThickness(BinaryWriter writer, Thickness t)
    {
        writer.Write(t.Left);
        writer.Write(t.Top);
        writer.Write(t.Right);
        writer.Write(t.Bottom);
    }

    private static Thickness ReadThickness(BinaryReader reader)
    {
        return new Thickness(
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle(),
            reader.ReadSingle()
        );
    }

    private static void WriteString(BinaryWriter writer, string? str)
    {
        if (string.IsNullOrEmpty(str))
        {
            writer.Write(0);
        }
        else
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(str);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == 0)
            return string.Empty;
        var bytes = reader.ReadBytes(length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    #endregion

    #region BackdropFilterParams Serialization

    private static void WriteBackdropFilterParamsArray(BinaryWriter writer, BackdropFilterParams[] filters)
    {
        writer.Write(filters.Length);
        foreach (var f in filters)
        {
            WriteBackdropFilterParams(writer, f);
        }
    }

    private static BackdropFilterParams[] ReadBackdropFilterParamsArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var filters = new BackdropFilterParams[count];
        for (int i = 0; i < count; i++)
        {
            filters[i] = ReadBackdropFilterParams(reader);
        }
        return filters;
    }

    private static void WriteBackdropFilterParams(BinaryWriter writer, BackdropFilterParams param)
    {
        // Blur parameters (16 bytes)
        writer.Write(param.BlurRadius);
        writer.Write(param.BlurSigma);
        writer.Write((byte)param.BlurType);
        writer.Write((byte)0); // Reserved1
        writer.Write((ushort)0); // Reserved2
        writer.Write(param.NoiseIntensity);

        // Color adjustments (16 bytes)
        writer.Write(param.Brightness);
        writer.Write(param.Contrast);
        writer.Write(param.Saturation);
        writer.Write(param.HueRotation);

        // Color transforms (16 bytes)
        writer.Write(param.Grayscale);
        writer.Write(param.Sepia);
        writer.Write(param.Invert);
        writer.Write(param.Opacity);

        // Material parameters (16 bytes)
        writer.Write(param.TintColor);
        writer.Write(param.TintOpacity);
        writer.Write(param.Luminosity);
        writer.Write((byte)param.MaterialType);
        writer.Write((byte)0); // Reserved3
        writer.Write((ushort)0); // Reserved4
    }

    private static BackdropFilterParams ReadBackdropFilterParams(BinaryReader reader)
    {
        // Blur parameters
        var blurRadius = reader.ReadSingle();
        var blurSigma = reader.ReadSingle();
        var blurType = (BlurType)reader.ReadByte();
        reader.ReadByte(); // Reserved1
        reader.ReadUInt16(); // Reserved2
        var noiseIntensity = reader.ReadSingle();

        // Color adjustments
        var brightness = reader.ReadSingle();
        var contrast = reader.ReadSingle();
        var saturation = reader.ReadSingle();
        var hueRotation = reader.ReadSingle();

        // Color transforms
        var grayscale = reader.ReadSingle();
        var sepia = reader.ReadSingle();
        var invert = reader.ReadSingle();
        var opacity = reader.ReadSingle();

        // Material parameters
        var tintColor = reader.ReadUInt32();
        var tintOpacity = reader.ReadSingle();
        var luminosity = reader.ReadSingle();
        var materialType = (MaterialType)reader.ReadByte();
        reader.ReadByte(); // Reserved3
        reader.ReadUInt16(); // Reserved4

        return new BackdropFilterParams(
            blurRadius: blurRadius,
            blurSigma: blurSigma,
            blurType: blurType,
            noiseIntensity: noiseIntensity,
            brightness: brightness,
            contrast: contrast,
            saturation: saturation,
            hueRotation: hueRotation,
            grayscale: grayscale,
            sepia: sepia,
            invert: invert,
            opacity: opacity,
            tintColor: tintColor,
            tintOpacity: tintOpacity,
            luminosity: luminosity,
            materialType: materialType
        );
    }

    #endregion
}

/// <summary>
/// 提交命令
/// </summary>
public sealed class SubmitCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.Submit;
}

#endregion

#region Extended Render Backend Interface (Shader Pipeline)

/// <summary>
/// 扩展渲染后端接口 - 支持完整的 GPU Shader Pipeline
/// 在 IRenderBackend 基础上增加：Shader 编译、PSO 管理、资源创建、命令缓冲区执行
/// </summary>
public interface IRenderBackendEx : IRenderBackend
{
    // ===== Shader 编译 =====

    /// <summary>
    /// 编译着色器
    /// </summary>
    nint CompileShader(string source, string entryPoint, Shaders.ShaderStage stage);

    /// <summary>
    /// 销毁着色器
    /// </summary>
    void DestroyShader(nint shader);

    // ===== Pipeline State Object =====

    /// <summary>
    /// 创建图形/计算管线状态对象
    /// </summary>
    nint CreatePipelineState(Pipeline.PipelineStateDesc desc);

    /// <summary>
    /// 销毁管线状态对象
    /// </summary>
    void DestroyPipelineState(nint pso);

    // ===== Root Signature =====

    /// <summary>
    /// 创建根签名
    /// </summary>
    nint CreateRootSignature(Pipeline.RootSignatureType type);

    /// <summary>
    /// 销毁根签名
    /// </summary>
    void DestroyRootSignature(nint rootSig);

    // ===== 扩展资源管理 =====

    /// <summary>
    /// 创建 GPU 缓冲区
    /// </summary>
    nint CreateBuffer(int size, Resources.BufferUsage usage);

    /// <summary>
    /// 获取缓冲区的 CPU 映射指针（上传堆缓冲区创建时即映射）
    /// </summary>
    nint GetBufferMappedPointer(nint buffer);

    /// <summary>
    /// 创建 2D 纹理
    /// </summary>
    nint CreateTexture2D(int width, int height, TextureFormat format, Resources.TextureUsage usage);

    /// <summary>
    /// 创建 Shader Resource View
    /// </summary>
    Resources.DescriptorHandle CreateSrv(nint resource);

    /// <summary>
    /// 创建 Constant Buffer View
    /// </summary>
    Resources.DescriptorHandle CreateCbv(nint buffer, int offset, int size);

    /// <summary>
    /// 创建 Unordered Access View
    /// </summary>
    Resources.DescriptorHandle CreateUav(nint resource);

    /// <summary>
    /// 释放描述符索引，使其可被后续分配复用
    /// </summary>
    void FreeDescriptor(Resources.DescriptorHandle handle);

    // ===== 命令缓冲区执行 =====

    /// <summary>
    /// 执行录制好的命令缓冲区
    /// </summary>
    void ExecuteCommandBuffer(Commands.GpuCommandBuffer commands);

    // ===== 同步 =====

    /// <summary>
    /// 发出 GPU fence 信号，返回 fence 值
    /// </summary>
    ulong Signal();

    /// <summary>
    /// 等待 GPU fence 完成
    /// </summary>
    void WaitForFence(ulong fenceValue);

    // ===== 设备信息 =====

    /// <summary>
    /// 获取 native 设备句柄（用于共享资源）
    /// </summary>
    nint DeviceHandle { get; }

    /// <summary>
    /// 获取 native 命令队列句柄
    /// </summary>
    nint CommandQueueHandle { get; }
}

#endregion
