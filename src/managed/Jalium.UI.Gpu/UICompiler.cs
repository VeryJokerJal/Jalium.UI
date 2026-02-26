using System.Text;

namespace Jalium.UI.Gpu;

/// <summary>
/// UI 编译器 - 将 JALXAML 编译成 GPU 友好的 IR
/// </summary>
public sealed class UICompiler
{
    private readonly CompilerContext _context;
    private readonly LayoutEngine _layoutEngine = new();
    private readonly List<SceneNode> _nodes = new();
    private readonly List<Material> _materials = new();
    private readonly List<GradientDef> _gradients = new();
    private readonly List<GradientStop> _gradientStops = new();
    private readonly List<AnimationCurve> _curves = new();
    private readonly List<AnimationTarget> _animationTargets = new();
    private readonly List<float> _transforms = new();
    private readonly MemoryStream _animationValues = new();
    private readonly List<DrawCommand> _drawCommands = new();
    private readonly List<TextureRef> _textures = new();
    private readonly List<GlyphAtlasRef> _glyphAtlases = new();
    private readonly List<PathCache> _pathCaches = new();
    private readonly List<InteractiveRegion> _interactiveRegions = new();
    private readonly List<StateTransition> _stateTransitions = new();
    private readonly List<byte> _vertexData = new();
    private readonly List<ushort> _indexData = new();
    private readonly List<BackdropFilterParams> _backdropFilterParams = new();

    private uint _nextNodeId = 1;
    private readonly Dictionary<string, uint> _namedNodes = new();
    private readonly Dictionary<string, uint> _materialCache = new();
    private readonly Dictionary<uint, int> _nodeToInstanceIndex = new();
    private readonly List<string> _sourceFiles = new();

    /// <summary>
    /// 编译选项
    /// </summary>
    public CompilerOptions Options { get; set; } = new();

    /// <summary>
    /// 是否启用优化
    /// </summary>
    public bool EnableOptimization
    {
        get => Options.EnableBatching && Options.EnableClipping && Options.EnableTransformMerging;
        set
        {
            Options.EnableBatching = value;
            Options.EnableClipping = value;
            Options.EnableTransformMerging = value;
        }
    }

    /// <summary>
    /// 是否生成调试信息
    /// </summary>
    public bool GenerateDebugInfo
    {
        get => Options.GenerateDebugInfo;
        set => Options.GenerateDebugInfo = value;
    }

    public UICompiler()
    {
        _context = new CompilerContext(this);
    }

    /// <summary>
    /// 添加源文件
    /// </summary>
    public void AddSourceFile(string path)
    {
        if (!_sourceFiles.Contains(path))
        {
            _sourceFiles.Add(path);
        }
    }

    /// <summary>
    /// 编译所有添加的源文件
    /// </summary>
    public CompiledUIBundle? Compile()
    {
        if (_sourceFiles.Count == 0)
            return null;

        // 编译第一个文件作为主文件
        // 后续可以扩展为合并多个文件
        return Compile(_sourceFiles[0]);
    }

    /// <summary>
    /// 编译 JALXAML 文件
    /// </summary>
    public CompiledUIBundle Compile(string jalxamlPath)
    {
        var source = File.ReadAllText(jalxamlPath);
        return CompileSource(source);
    }

    /// <summary>
    /// 编译 JALXAML 源代码
    /// </summary>
    public CompiledUIBundle CompileSource(string source)
    {
        // 解析 JALXAML
        var ast = ParseJalxaml(source);

        // 编译 AST 到 IR
        CompileAst(ast);

        // 优化
        Optimize();

        // 生成绘制命令
        GenerateDrawCommands();

        // 打包
        return Package();
    }

    private JalxamlAst ParseJalxaml(string source)
    {
        var parser = new JalxamlParser();
        return parser.Parse(source);
    }

    private void CompileAst(JalxamlAst ast)
    {
        // 编译资源（样式、模板等）
        if (ast.Resources != null)
        {
            CompileResources(ast.Resources);
        }

        // 计算布局
        _layoutEngine.ComputeLayout(ast.Root, Options.ViewportWidth, Options.ViewportHeight);

        // 编译根元素
        CompileElement(ast.Root, 0);
    }

    private void CompileResources(ResourceDictionary resources)
    {
        // 编译样式为材质
        foreach (var style in resources.Styles)
        {
            CompileStyle(style);
        }

        // 编译动画为曲线
        foreach (var animation in resources.Animations)
        {
            CompileAnimation(animation);
        }
    }

    private uint CompileStyle(StyleDef style)
    {
        // 将样式编译为材质
        var material = new Material(
            background: ParseColor(style.Background),
            border: ParseColor(style.BorderBrush),
            foreground: ParseColor(style.Foreground),
            gradient: style.HasGradient ? CompileGradient(style.Gradient!) : 0,
            opacity: (byte)(style.Opacity * 255),
            blend: BlendMode.Normal
        );

        var index = (uint)_materials.Count;
        _materials.Add(material);

        // 缓存材质
        if (!string.IsNullOrEmpty(style.Key))
        {
            _materialCache[style.Key] = index;
        }

        // 编译状态变化（如 :hover, :pressed）
        foreach (var trigger in style.Triggers)
        {
            CompileTrigger(trigger, index);
        }

        return index;
    }

    private void CompileTrigger(TriggerDef trigger, uint baseMaterialIndex)
    {
        // 创建触发后的材质
        var triggeredMaterial = new Material(
            background: trigger.Background.HasValue ? ParseColor(trigger.Background.Value) : _materials[(int)baseMaterialIndex].BackgroundColor,
            border: trigger.BorderBrush.HasValue ? ParseColor(trigger.BorderBrush.Value) : _materials[(int)baseMaterialIndex].BorderColor,
            foreground: trigger.Foreground.HasValue ? ParseColor(trigger.Foreground.Value) : _materials[(int)baseMaterialIndex].ForegroundColor,
            opacity: trigger.Opacity.HasValue ? (byte)(trigger.Opacity.Value * 255) : _materials[(int)baseMaterialIndex].Opacity
        );

        var triggeredMaterialIndex = (uint)_materials.Count;
        _materials.Add(triggeredMaterial);

        // 如果有过渡动画，编译曲线
        uint animStart = 0, animCount = 0;
        if (trigger.Transition != null)
        {
            animStart = (uint)_animationTargets.Count;
            // 为每个变化的属性创建动画目标
            // 这里简化处理，实际需要更复杂的逻辑
            animCount = 1;
        }

        // 创建状态转换
        var transition = new StateTransition(
            trigger: MapTriggerType(trigger.Type),
            fromState: 0, // 默认状态
            toState: 1,   // 触发状态
            animStart: animStart,
            animCount: animCount,
            matStart: baseMaterialIndex,
            matCount: 1
        );
        _stateTransitions.Add(transition);
    }

    private uint CompileGradient(GradientAstDef gradient)
    {
        var stopsIndex = (uint)_gradientStops.Count;
        foreach (var stop in gradient.Stops)
        {
            _gradientStops.Add(stop);
        }

        var gradientIndex = (uint)_gradients.Count;
        _gradients.Add(new GradientDef(
            gradient.Type,
            gradient.Start,
            gradient.End,
            stopsIndex,
            (byte)gradient.Stops.Count
        ));

        return gradientIndex;
    }

    private void CompileAnimation(AnimationDef animation)
    {
        var curve = new AnimationCurve(
            animation.Easing,
            animation.DurationMs,
            animation.DelayMs,
            animation.RepeatCount,
            animation.AutoReverse
        );
        _curves.Add(curve);
    }

    private void CompileElement(ElementNode element, uint parentId)
    {
        var nodeId = _nextNodeId++;

        // 注册命名元素
        if (!string.IsNullOrEmpty(element.Name))
        {
            _namedNodes[element.Name] = nodeId;
        }

        // 创建变换
        var transformIndex = (uint)(_transforms.Count / 6);
        AddIdentityTransform();

        // 获取或创建材质
        var materialIndex = GetOrCreateMaterial(element);

        // 根据元素类型创建不同的节点
        SceneNode node = element.Type switch
        {
            ElementType.Rectangle or ElementType.Border or ElementType.Grid or ElementType.StackPanel =>
                new RectNode
                {
                    Id = nodeId,
                    ParentId = parentId,
                    TransformIndex = transformIndex,
                    MaterialIndex = materialIndex,
                    Bounds = CompileBounds(element),
                    CornerRadius = CompileCornerRadius(element),
                    BorderThickness = CompileBorderThickness(element),
                    ZIndex = element.ZIndex
                },

            ElementType.TextBlock or ElementType.Label =>
                new TextNode
                {
                    Id = nodeId,
                    ParentId = parentId,
                    TransformIndex = transformIndex,
                    MaterialIndex = materialIndex,
                    TextHash = ComputeTextHash(element.Text),
                    GlyphAtlasIndex = GetOrCreateGlyphAtlas(element),
                    Bounds = CompileBounds(element),
                    ZIndex = element.ZIndex
                },

            ElementType.Image =>
                new ImageNode
                {
                    Id = nodeId,
                    ParentId = parentId,
                    TransformIndex = transformIndex,
                    MaterialIndex = materialIndex,
                    TextureIndex = GetOrCreateTexture(element.Source),
                    Bounds = CompileBounds(element),
                    NineSlice = CompileNineSlice(element),
                    ZIndex = element.ZIndex
                },

            ElementType.Path or ElementType.Ellipse =>
                new PathNode
                {
                    Id = nodeId,
                    ParentId = parentId,
                    TransformIndex = transformIndex,
                    MaterialIndex = materialIndex,
                    PathCacheIndex = GetOrCreatePathCache(element),
                    Bounds = CompileBounds(element),
                    FillRule = element.FillRule,
                    ZIndex = element.ZIndex
                },

            _ => new RectNode
            {
                Id = nodeId,
                ParentId = parentId,
                TransformIndex = transformIndex,
                MaterialIndex = materialIndex,
                Bounds = CompileBounds(element),
                ZIndex = element.ZIndex
            }
        };

        _nodes.Add(node);

        // 编译 Backdrop Filter（如果有）
        if (HasBackdropFilter(element))
        {
            CompileBackdropFilter(element, nodeId, transformIndex);
        }

        // 编译交互区域
        if (element.IsInteractive)
        {
            CompileInteractiveRegion(element, nodeId);
        }

        // 递归编译子元素
        foreach (var child in element.Children)
        {
            CompileElement(child, nodeId);
        }
    }

    /// <summary>
    /// 检查元素是否有 Backdrop Filter
    /// </summary>
    private static bool HasBackdropFilter(ElementNode element)
    {
        return !string.IsNullOrEmpty(element.BackdropFilter) ||
               !string.IsNullOrEmpty(element.Material);
    }

    /// <summary>
    /// 编译 Backdrop Filter 节点
    /// </summary>
    private void CompileBackdropFilter(ElementNode element, uint parentNodeId, uint transformIndex)
    {
        // 解析滤镜参数
        BackdropFilterParams filterParams;

        if (!string.IsNullOrEmpty(element.Material))
        {
            // 使用材质预设
            filterParams = BackdropFilterParser.CreateFromMaterial(
                element.Material,
                element.MaterialTint,
                element.MaterialTintOpacity,
                element.MaterialBlurRadius);
        }
        else
        {
            // 解析滤镜字符串
            filterParams = BackdropFilterParser.Parse(element.BackdropFilter);
        }

        // 如果没有实际效果，跳过
        if (!filterParams.HasEffect)
            return;

        // 添加到参数数组
        var paramsIndex = (uint)_backdropFilterParams.Count;
        _backdropFilterParams.Add(filterParams);

        // 创建 BackdropFilterNode
        var filterNodeId = _nextNodeId++;
        var filterNode = new BackdropFilterNode
        {
            Id = filterNodeId,
            ParentId = parentNodeId,
            TransformIndex = transformIndex,
            MaterialIndex = 0,
            Params = filterParams,
            FilterRegion = CompileBounds(element),
            TargetNodeId = parentNodeId,
            InheritTransform = true,
            CornerRadius = CompileCornerRadius(element),
            ParamsIndex = paramsIndex,
            ZIndex = element.ZIndex - 1 // 滤镜在内容下方
        };

        _nodes.Add(filterNode);
    }

    private void CompileInteractiveRegion(ElementNode element, uint nodeId)
    {
        var flags = InteractionFlags.None;

        if (element.HasClickHandler) flags |= InteractionFlags.Click;
        if (element.HasHoverHandler) flags |= InteractionFlags.Hover;
        if (element.IsFocusable) flags |= InteractionFlags.Focus;
        if (element.AcceptsKeyInput) flags |= InteractionFlags.KeyInput;
        if (element.IsDraggable) flags |= InteractionFlags.Drag;
        if (element.IsScrollable) flags |= InteractionFlags.Scroll;

        var region = new InteractiveRegion(
            nodeId,
            CompileBounds(element),
            flags,
            element.HandlerIndex
        );
        _interactiveRegions.Add(region);
    }

    private void Optimize()
    {
        if (Options.EnableBatching)
        {
            // 1. 批处理合并 - 合并相邻的相同材质节点
            OptimizeBatching();
        }

        // 2. 图层合成策略
        OptimizeLayers();

        if (Options.EnableClipping)
        {
            // 3. 裁剪优化 - 移除完全被遮挡的节点
            OptimizeClipping();
        }

        if (Options.EnableTransformMerging)
        {
            // 4. 变换合并
            OptimizeTransforms();
        }
    }

    private void OptimizeBatching()
    {
        // 按材质和纹理对节点分组，重新排序以优化批处理
        // 原则：相同材质的节点尽量连续排列，减少状态切换

        if (_nodes.Count == 0)
            return;

        // 首先按 ZIndex 分组，然后在每个 ZIndex 层内按材质排序
        var nodesByZIndex = _nodes
            .GroupBy(n => n.ZIndex)
            .OrderBy(g => g.Key)
            .ToList();

        var reorderedNodes = new List<SceneNode>();

        foreach (var zGroup in nodesByZIndex)
        {
            // 分离不同类型的节点
            var rectNodes = zGroup.OfType<RectNode>().ToList();
            var textNodes = zGroup.OfType<TextNode>().ToList();
            var imageNodes = zGroup.OfType<ImageNode>().ToList();
            var pathNodes = zGroup.OfType<PathNode>().ToList();
            var effectNodes = zGroup.OfType<EffectNode>().ToList();

            // 对每种类型按材质/纹理分组排序以减少状态切换
            var sortedRects = rectNodes
                .OrderBy(n => n.MaterialIndex)
                .ToList();

            var sortedText = textNodes
                .OrderBy(n => n.GlyphAtlasIndex)
                .ThenBy(n => n.MaterialIndex)
                .ToList();

            var sortedImages = imageNodes
                .OrderBy(n => n.TextureIndex)
                .ThenBy(n => n.MaterialIndex)
                .ToList();

            var sortedPaths = pathNodes
                .OrderBy(n => n.MaterialIndex)
                .ToList();

            // 按渲染顺序添加：不透明节点先，然后半透明节点
            // 矩形 -> 图像 -> 路径 -> 文本 -> 效果
            foreach (var node in sortedRects.Where(n => _materials[(int)n.MaterialIndex].Opacity == 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedImages.Where(n => _materials[(int)n.MaterialIndex].Opacity == 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedPaths.Where(n => _materials[(int)n.MaterialIndex].Opacity == 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedText.Where(n => _materials[(int)n.MaterialIndex].Opacity == 255))
                reorderedNodes.Add(node);

            // 半透明节点（需要从后往前渲染）
            foreach (var node in sortedRects.Where(n => _materials[(int)n.MaterialIndex].Opacity < 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedImages.Where(n => _materials[(int)n.MaterialIndex].Opacity < 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedPaths.Where(n => _materials[(int)n.MaterialIndex].Opacity < 255))
                reorderedNodes.Add(node);
            foreach (var node in sortedText.Where(n => _materials[(int)n.MaterialIndex].Opacity < 255))
                reorderedNodes.Add(node);

            // 效果节点最后
            reorderedNodes.AddRange(effectNodes);
        }

        // 建立节点到实例索引的映射
        _nodeToInstanceIndex.Clear();
        for (int i = 0; i < reorderedNodes.Count; i++)
        {
            _nodeToInstanceIndex[reorderedNodes[i].Id] = i;
        }

        // 更新节点列表
        _nodes.Clear();
        _nodes.AddRange(reorderedNodes);
    }

    private void OptimizeLayers()
    {
        // 识别需要离屏渲染的图层
        // 1. 有混合模式的节点组
        // 2. 有效果（模糊、阴影）的节点
        // 3. 有透明度且有子节点的容器

        var layerRoots = new List<uint>();
        var childCounts = new Dictionary<uint, int>();

        // 计算每个节点的子节点数
        foreach (var node in _nodes)
        {
            if (node.ParentId != 0)
            {
                childCounts.TryGetValue(node.ParentId, out var count);
                childCounts[node.ParentId] = count + 1;
            }
        }

        foreach (var node in _nodes)
        {
            var material = _materials[(int)node.MaterialIndex];
            var hasChildren = childCounts.TryGetValue(node.Id, out var count) && count > 0;

            // 检查是否需要单独图层
            bool needsLayer = material.BlendMode != BlendMode.Normal ||
                              (material.Opacity < 255 && hasChildren);

            if (needsLayer)
            {
                layerRoots.Add(node.Id);
            }
        }

        // 为效果节点创建图层
        foreach (var effect in _nodes.OfType<EffectNode>())
        {
            if (!layerRoots.Contains(effect.TargetNodeId))
            {
                layerRoots.Add(effect.TargetNodeId);
            }
        }

        // 生成图层渲染命令
        // 图层需要先渲染到临时纹理，然后合成到主渲染目标
        foreach (var layerRootId in layerRoots.Distinct())
        {
            var layerNode = _nodes.FirstOrDefault(n => n.Id == layerRootId);
            if (layerNode == null)
                continue;

            // 收集该图层的所有子节点
            var layerNodes = CollectSubtree(layerRootId);

            // 标记这些节点需要离屏渲染
            // 实际实现会在绘制命令中插入 SetRenderTarget 和 CompositeLayer 命令
            foreach (var ln in layerNodes)
            {
                // 设置图层标记（通过 ClipIndex 复用或添加新字段）
            }
        }
    }

    private List<SceneNode> CollectSubtree(uint rootId)
    {
        var result = new List<SceneNode>();
        var queue = new Queue<uint>();
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var node = _nodes.FirstOrDefault(n => n.Id == id);
            if (node != null)
            {
                result.Add(node);

                // 添加子节点
                foreach (var child in _nodes.Where(n => n.ParentId == id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return result;
    }

    private void OptimizeClipping()
    {
        // 移除完全不可见的节点
        var viewportRect = new Rect(0, 0, (float)Options.ViewportWidth, (float)Options.ViewportHeight);
        var visibleNodes = new List<SceneNode>();

        foreach (var node in _nodes)
        {
            if (!node.IsVisible)
                continue;

            // 获取节点边界
            Rect bounds = GetNodeBounds(node);

            // 检查是否在视口内
            if (RectIntersects(bounds, viewportRect))
            {
                visibleNodes.Add(node);
            }
        }

        // 检查遮挡（从后向前）
        // 按 Z-index 排序，高 Z-index 在后面
        var sortedNodes = visibleNodes.OrderByDescending(n => n.ZIndex).ToList();
        var opaqueRects = new List<Rect>();
        var finalNodes = new List<SceneNode>();

        foreach (var node in sortedNodes)
        {
            var material = _materials[(int)node.MaterialIndex];
            var bounds = GetNodeBounds(node);

            // 检查是否被完全遮挡
            bool isOccluded = false;
            foreach (var occluderBounds in opaqueRects)
            {
                if (RectContains(occluderBounds, bounds))
                {
                    isOccluded = true;
                    break;
                }
            }

            if (!isOccluded)
            {
                finalNodes.Add(node);

                // 如果是不透明矩形节点，加入遮挡列表
                if (node is RectNode rectNode &&
                    material.Opacity == 255 &&
                    IsOpaqueBackground(material) &&
                    rectNode.CornerRadius.TopLeft < 0.1f &&
                    rectNode.CornerRadius.TopRight < 0.1f &&
                    rectNode.CornerRadius.BottomLeft < 0.1f &&
                    rectNode.CornerRadius.BottomRight < 0.1f)
                {
                    opaqueRects.Add(bounds);
                }
            }
        }

        // 恢复正确顺序（按 ZIndex 排序）
        finalNodes = finalNodes.OrderBy(n => n.ZIndex).ToList();

        // 更新节点列表
        _nodes.Clear();
        _nodes.AddRange(finalNodes);

        // 重建节点到实例索引的映射
        _nodeToInstanceIndex.Clear();
        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodeToInstanceIndex[_nodes[i].Id] = i;
        }
    }

    private static Rect GetNodeBounds(SceneNode node)
    {
        return node switch
        {
            RectNode r => r.Bounds,
            TextNode t => t.Bounds,
            ImageNode img => img.Bounds,
            PathNode p => p.Bounds,
            _ => Rect.Empty
        };
    }

    private void OptimizeTransforms()
    {
        // 合并嵌套的变换矩阵
        // 对于静态 UI，可以预计算父子变换的组合

        // 建立父子关系映射
        var nodeById = _nodes.ToDictionary(n => n.Id);
        var nodeToNewTransform = new Dictionary<uint, uint>();

        // 计算每个节点的世界变换（从根到节点的累积变换）
        foreach (var node in _nodes)
        {
            var worldMatrix = ComputeWorldTransform(node, nodeById);

            // 检查是否与现有变换相同
            var existingIndex = FindExistingTransform(worldMatrix);
            if (existingIndex >= 0)
            {
                nodeToNewTransform[node.Id] = (uint)existingIndex;
            }
            else
            {
                // 添加新变换
                var newIndex = _transforms.Count / 6;
                _transforms.AddRange(worldMatrix);
                nodeToNewTransform[node.Id] = (uint)newIndex;
            }
        }

        // 更新节点的变换索引
        // 由于 SceneNode 的属性是 init，我们需要重建节点
        var newNodes = new List<SceneNode>();
        foreach (var node in _nodes)
        {
            if (nodeToNewTransform.TryGetValue(node.Id, out var newTransformIndex))
            {
                var updatedNode = CloneNodeWithTransform(node, newTransformIndex);
                newNodes.Add(updatedNode);
            }
            else
            {
                newNodes.Add(node);
            }
        }

        _nodes.Clear();
        _nodes.AddRange(newNodes);
    }

    private float[] ComputeWorldTransform(SceneNode node, Dictionary<uint, SceneNode> nodeById)
    {
        var matrices = new List<float[]>();

        // 收集从根到当前节点的所有变换
        var current = node;
        while (current != null)
        {
            matrices.Add(GetTransformMatrix((int)current.TransformIndex));
            if (current.ParentId == 0 || !nodeById.TryGetValue(current.ParentId, out current))
                break;
        }

        // 反向（从根开始）并合并
        matrices.Reverse();

        var result = new float[] { 1, 0, 0, 1, 0, 0 }; // 单位矩阵
        foreach (var matrix in matrices)
        {
            if (!IsIdentityMatrix(matrix))
            {
                result = MultiplyMatrices(result, matrix);
            }
        }

        return result;
    }

    private static bool IsIdentityMatrix(float[] m)
    {
        return Math.Abs(m[0] - 1) < 0.0001f &&
               Math.Abs(m[1]) < 0.0001f &&
               Math.Abs(m[2]) < 0.0001f &&
               Math.Abs(m[3] - 1) < 0.0001f &&
               Math.Abs(m[4]) < 0.0001f &&
               Math.Abs(m[5]) < 0.0001f;
    }

    private int FindExistingTransform(float[] matrix)
    {
        for (int i = 0; i < _transforms.Count; i += 6)
        {
            bool match = true;
            for (int j = 0; j < 6 && i + j < _transforms.Count; j++)
            {
                if (Math.Abs(_transforms[i + j] - matrix[j]) > 0.0001f)
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i / 6;
        }
        return -1;
    }

    private static SceneNode CloneNodeWithTransform(SceneNode node, uint newTransformIndex)
    {
        return node switch
        {
            RectNode r => new RectNode
            {
                Id = r.Id,
                ParentId = r.ParentId,
                TransformIndex = newTransformIndex,
                MaterialIndex = r.MaterialIndex,
                ClipIndex = r.ClipIndex,
                IsVisible = r.IsVisible,
                ZIndex = r.ZIndex,
                Bounds = r.Bounds,
                CornerRadius = r.CornerRadius,
                BorderThickness = r.BorderThickness
            },
            TextNode t => new TextNode
            {
                Id = t.Id,
                ParentId = t.ParentId,
                TransformIndex = newTransformIndex,
                MaterialIndex = t.MaterialIndex,
                ClipIndex = t.ClipIndex,
                IsVisible = t.IsVisible,
                ZIndex = t.ZIndex,
                TextHash = t.TextHash,
                GlyphAtlasIndex = t.GlyphAtlasIndex,
                GlyphRunIndex = t.GlyphRunIndex,
                Bounds = t.Bounds
            },
            ImageNode img => new ImageNode
            {
                Id = img.Id,
                ParentId = img.ParentId,
                TransformIndex = newTransformIndex,
                MaterialIndex = img.MaterialIndex,
                ClipIndex = img.ClipIndex,
                IsVisible = img.IsVisible,
                ZIndex = img.ZIndex,
                TextureIndex = img.TextureIndex,
                UVRect = img.UVRect,
                Bounds = img.Bounds,
                NineSlice = img.NineSlice
            },
            PathNode p => new PathNode
            {
                Id = p.Id,
                ParentId = p.ParentId,
                TransformIndex = newTransformIndex,
                MaterialIndex = p.MaterialIndex,
                ClipIndex = p.ClipIndex,
                IsVisible = p.IsVisible,
                ZIndex = p.ZIndex,
                PathCacheIndex = p.PathCacheIndex,
                Bounds = p.Bounds,
                FillRule = p.FillRule
            },
            EffectNode e => new EffectNode
            {
                Id = e.Id,
                ParentId = e.ParentId,
                TransformIndex = newTransformIndex,
                MaterialIndex = e.MaterialIndex,
                ClipIndex = e.ClipIndex,
                IsVisible = e.IsVisible,
                ZIndex = e.ZIndex,
                EffectType = e.EffectType,
                EffectParamsIndex = e.EffectParamsIndex,
                TargetNodeId = e.TargetNodeId
            },
            _ => node
        };
    }

    #region Optimization Helpers

    private static bool RectIntersects(Rect a, Rect b)
    {
        return a.X < b.X + b.Width &&
               a.X + a.Width > b.X &&
               a.Y < b.Y + b.Height &&
               a.Y + a.Height > b.Y;
    }

    private static bool RectContains(Rect outer, Rect inner)
    {
        return outer.X <= inner.X &&
               outer.Y <= inner.Y &&
               outer.X + outer.Width >= inner.X + inner.Width &&
               outer.Y + outer.Height >= inner.Y + inner.Height;
    }

    private static bool IsOpaqueBackground(Material material)
    {
        // 检查背景色的 alpha 是否完全不透明
        return (material.BackgroundColor >> 24) == 255;
    }

    private float[] GetTransformMatrix(int index)
    {
        var result = new float[6];
        var offset = index * 6;
        for (int i = 0; i < 6 && offset + i < _transforms.Count; i++)
        {
            result[i] = _transforms[offset + i];
        }
        return result;
    }

    private static float[] MultiplyMatrices(float[] a, float[] b)
    {
        // 3x2 矩阵乘法
        // | a0 a1 |   | b0 b1 |
        // | a2 a3 | x | b2 b3 |
        // | a4 a5 |   | b4 b5 |
        return
        [
            a[0] * b[0] + a[1] * b[2],
            a[0] * b[1] + a[1] * b[3],
            a[2] * b[0] + a[3] * b[2],
            a[2] * b[1] + a[3] * b[3],
            a[4] * b[0] + a[5] * b[2] + b[4],
            a[4] * b[1] + a[5] * b[3] + b[5]
        ];
    }

    #endregion

    private void GenerateDrawCommands()
    {
        // 节点已经按优化后的顺序排列
        // 生成批处理绘制命令

        _drawCommands.Clear();

        // 当前批次状态
        var currentBatchType = DrawBatchType.None;
        uint currentMaterial = uint.MaxValue;
        uint currentTexture = uint.MaxValue;
        int batchStartIndex = 0;
        int batchCount = 0;

        // 用于计算实例缓冲区偏移
        var rectInstanceOffset = 0;
        var textInstanceOffset = 0;
        var imageInstanceOffset = 0;

        for (int i = 0; i < _nodes.Count; i++)
        {
            var node = _nodes[i];

            switch (node)
            {
                case RectNode rect:
                    {
                        var batchType = DrawBatchType.Rect;

                        // 检查是否可以合并到当前批次
                        if (currentBatchType != batchType || currentMaterial != node.MaterialIndex)
                        {
                            FlushCurrentBatch();
                            currentBatchType = batchType;
                            currentMaterial = node.MaterialIndex;
                            batchStartIndex = rectInstanceOffset;
                            batchCount = 0;
                        }

                        batchCount++;
                        rectInstanceOffset++;
                        break;
                    }

                case TextNode text:
                    {
                        FlushCurrentBatch();

                        // 获取文本内容并生成字形运行
                        var glyphCount = GetGlyphCount(text.TextHash);

                        _drawCommands.Add(new DrawTextBatchCommand
                        {
                            GlyphAtlasIndex = text.GlyphAtlasIndex,
                            InstanceBufferOffset = (uint)textInstanceOffset,
                            GlyphCount = glyphCount
                        });

                        textInstanceOffset += (int)glyphCount;
                        break;
                    }

                case ImageNode image:
                    {
                        var batchType = DrawBatchType.Image;

                        // 检查是否可以合并到当前批次（相同纹理）
                        if (currentBatchType != batchType || currentTexture != image.TextureIndex)
                        {
                            FlushCurrentBatch();
                            currentBatchType = batchType;
                            currentTexture = image.TextureIndex;
                            batchStartIndex = imageInstanceOffset;
                            batchCount = 0;
                        }

                        batchCount++;
                        imageInstanceOffset++;
                        break;
                    }

                case PathNode path:
                    {
                        FlushCurrentBatch();

                        // 路径需要单独绘制
                        _drawCommands.Add(new DrawPathCommand
                        {
                            PathCacheIndex = path.PathCacheIndex,
                            MaterialIndex = path.MaterialIndex,
                            TransformIndex = path.TransformIndex
                        });
                        break;
                    }

                case EffectNode effect:
                    {
                        FlushCurrentBatch();

                        // 效果需要离屏渲染
                        _drawCommands.Add(new ApplyEffectCommand
                        {
                            Effect = effect.EffectType,
                            SourceTextureIndex = 0, // 主渲染目标
                            DestTextureIndex = 1,   // 临时纹理
                            Parameters = GetEffectParameters(effect)
                        });
                        break;
                    }

                case BackdropFilterNode backdropFilter:
                    {
                        FlushCurrentBatch();

                        // 1. 捕获当前背景
                        _drawCommands.Add(new CaptureBackdropCommand
                        {
                            Region = backdropFilter.FilterRegion,
                            TargetTextureIndex = 2 // 背景捕获纹理
                        });

                        // 2. 应用 Backdrop Filter
                        _drawCommands.Add(new ApplyBackdropFilterCommand
                        {
                            Params = backdropFilter.Params,
                            Region = backdropFilter.FilterRegion,
                            BackdropTextureIndex = 2,
                            OutputTextureIndex = 3,
                            CornerRadius = backdropFilter.CornerRadius,
                            TransformIndex = backdropFilter.TransformIndex
                        });

                        // 3. 合成回主渲染目标
                        _drawCommands.Add(new CompositeLayerCommand
                        {
                            SourceTextureIndex = 3,
                            BlendMode = BlendMode.Normal,
                            Opacity = (byte)(backdropFilter.Params.Opacity * 255),
                            DestRect = backdropFilter.FilterRegion
                        });
                        break;
                    }
            }
        }

        // 刷新最后一个批次
        FlushCurrentBatch();

        // 添加提交命令
        _drawCommands.Add(new SubmitCommand());

        void FlushCurrentBatch()
        {
            if (batchCount == 0)
                return;

            switch (currentBatchType)
            {
                case DrawBatchType.Rect:
                    _drawCommands.Add(new DrawRectBatchCommand
                    {
                        InstanceBufferOffset = (uint)batchStartIndex,
                        InstanceCount = (uint)batchCount,
                        TextureIndex = 0 // 矩形不使用纹理
                    });
                    break;

                case DrawBatchType.Image:
                    _drawCommands.Add(new DrawImageBatchCommand
                    {
                        TextureIndex = currentTexture,
                        InstanceBufferOffset = (uint)batchStartIndex,
                        InstanceCount = (uint)batchCount
                    });
                    break;
            }

            currentBatchType = DrawBatchType.None;
            batchCount = 0;
        }
    }

    private enum DrawBatchType
    {
        None,
        Rect,
        Image,
        Text
    }

    private record struct GlyphRunData(uint TextHash, uint GlyphStart, uint GlyphCount);

    private uint GetGlyphCount(ulong textHash)
    {
        // 从文本缓存中查找字形数
        // 简化实现：假设每个字符一个字形
        // 实际需要从 TextLayout 结果获取
        return 1;
    }

    private ReadOnlyMemory<byte> GetEffectParameters(EffectNode effect)
    {
        // 根据效果类型生成参数
        var parameters = new byte[16];
        using var ms = new MemoryStream(parameters);
        using var writer = new BinaryWriter(ms);

        switch (effect.EffectType)
        {
            case EffectType.GaussianBlur:
            case EffectType.BackdropBlur:
                writer.Write(5f); // 模糊半径
                writer.Write(0f); // 保留
                writer.Write(0f);
                writer.Write(0f);
                break;

            case EffectType.BoxShadow:
            case EffectType.DropShadow:
                writer.Write(4f);  // X 偏移
                writer.Write(4f);  // Y 偏移
                writer.Write(8f);  // 模糊半径
                writer.Write(0f);  // 扩展
                break;
        }

        return parameters;
    }

    private CompiledUIBundle Package()
    {
        return new CompiledUIBundle
        {
            Version = RenderIR.Version,
            Nodes = _nodes.ToArray(),
            Materials = _materials.ToArray(),
            Gradients = _gradients.ToArray(),
            GradientStops = _gradientStops.ToArray(),
            Curves = _curves.ToArray(),
            AnimationTargets = _animationTargets.ToArray(),
            Transforms = _transforms.ToArray(),
            AnimationValues = _animationValues.ToArray(),
            DrawCommands = _drawCommands.ToArray(),
            Textures = _textures.ToArray(),
            GlyphAtlases = _glyphAtlases.ToArray(),
            PathCaches = _pathCaches.ToArray(),
            InteractiveRegions = _interactiveRegions.ToArray(),
            StateTransitions = _stateTransitions.ToArray(),
            BackdropFilterParams = _backdropFilterParams.ToArray()
        };
    }

    #region Helper Methods

    private void AddIdentityTransform()
    {
        // 3x2 仿射变换矩阵（单位矩阵）
        _transforms.AddRange([1, 0, 0, 1, 0, 0]);
    }

    private uint GetOrCreateMaterial(ElementNode element)
    {
        // 检查是否引用了已存在的样式
        if (!string.IsNullOrEmpty(element.StyleKey) && _materialCache.TryGetValue(element.StyleKey, out var existingIndex))
        {
            return existingIndex;
        }

        // 创建内联材质
        var material = new Material(
            background: ParseColor(element.Background),
            border: ParseColor(element.BorderBrush),
            foreground: ParseColor(element.Foreground),
            opacity: (byte)(element.Opacity * 255)
        );

        var index = (uint)_materials.Count;
        _materials.Add(material);
        return index;
    }

    private Rect CompileBounds(ElementNode element)
    {
        // 使用布局引擎计算的最终位置
        var slot = _layoutEngine.GetLayoutSlot(element);
        var rect = slot.FinalRect;

        // 如果有布局信息，使用它
        if (rect.Width > 0 || rect.Height > 0)
        {
            return new Rect(
                (float)rect.X,
                (float)rect.Y,
                (float)rect.Width,
                (float)rect.Height
            );
        }

        // 否则使用显式设置的值
        return new Rect(
            (float)element.Left,
            (float)element.Top,
            (float)(double.IsNaN(element.Width) ? 0 : element.Width),
            (float)(double.IsNaN(element.Height) ? 0 : element.Height)
        );
    }

    private CornerRadius CompileCornerRadius(ElementNode element)
    {
        return new CornerRadius(
            (float)element.CornerRadius.TopLeft,
            (float)element.CornerRadius.TopRight,
            (float)element.CornerRadius.BottomRight,
            (float)element.CornerRadius.BottomLeft
        );
    }

    private Thickness CompileBorderThickness(ElementNode element)
    {
        return new Thickness(
            (float)element.BorderThickness.Left,
            (float)element.BorderThickness.Top,
            (float)element.BorderThickness.Right,
            (float)element.BorderThickness.Bottom
        );
    }

    private Thickness CompileNineSlice(ElementNode element)
    {
        return new Thickness(
            (float)element.NineSlice.Left,
            (float)element.NineSlice.Top,
            (float)element.NineSlice.Right,
            (float)element.NineSlice.Bottom
        );
    }

    private static uint ParseColor(string? colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return 0;

        // 解析 #AARRGGBB 或 #RRGGBB 格式
        if (colorStr.StartsWith('#'))
        {
            var hex = colorStr[1..];
            if (hex.Length == 6)
            {
                return 0xFF000000 | Convert.ToUInt32(hex, 16);
            }
            else if (hex.Length == 8)
            {
                // ARGB -> RGBA (预乘 alpha)
                var argb = Convert.ToUInt32(hex, 16);
                return argb;
            }
        }

        return 0;
    }

    private static uint ParseColor(ColorDef color)
    {
        return ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }

    private static ulong ComputeTextHash(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // 使用简单的哈希算法
        ulong hash = 14695981039346656037UL;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private uint GetOrCreateGlyphAtlas(ElementNode element)
    {
        var fontId = element.FontFamily ?? "default";
        var fontSize = element.FontSize;
        var key = $"{fontId}_{fontSize}";

        for (int i = 0; i < _glyphAtlases.Count; i++)
        {
            var atlas = _glyphAtlases[i];
            if (atlas.FontId == fontId && Math.Abs(atlas.FontSize - fontSize) < 0.01f)
            {
                return (uint)i;
            }
        }

        var newAtlas = new GlyphAtlasRef(fontId, fontSize, 1024, 1024);
        var index = (uint)_glyphAtlases.Count;
        _glyphAtlases.Add(newAtlas);
        return index;
    }

    private uint GetOrCreateTexture(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return 0;

        for (int i = 0; i < _textures.Count; i++)
        {
            if (_textures[i].Path == source)
                return (uint)i;
        }

        var texture = new TextureRef(source, 0, 0, TextureFormat.RGBA8);
        var index = (uint)_textures.Count;
        _textures.Add(texture);
        return index;
    }

    private uint GetOrCreatePathCache(ElementNode element)
    {
        var pathHash = ComputeTextHash(element.PathData);

        for (int i = 0; i < _pathCaches.Count; i++)
        {
            if (_pathCaches[i].PathHash == pathHash)
                return (uint)i;
        }

        // 执行实际的路径三角化
        var tessellator = new PathTessellator
        {
            Tolerance = Options.PathTessellationTolerance
        };

        TessellationResult result;

        if (!string.IsNullOrEmpty(element.PathData))
        {
            // SVG 路径数据
            result = tessellator.Tessellate(element.PathData);
        }
        else if (element.Type == ElementType.Ellipse)
        {
            // 椭圆
            var bounds = CompileBounds(element);
            var cx = bounds.X + bounds.Width / 2;
            var cy = bounds.Y + bounds.Height / 2;
            result = tessellator.TessellateEllipse(cx, cy, bounds.Width / 2, bounds.Height / 2);
        }
        else if (element.CornerRadius.TopLeft > 0 || element.CornerRadius.TopRight > 0 ||
                 element.CornerRadius.BottomLeft > 0 || element.CornerRadius.BottomRight > 0)
        {
            // 圆角矩形
            var bounds = CompileBounds(element);
            result = tessellator.TessellateRoundedRect(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                (float)element.CornerRadius.TopLeft,
                (float)element.CornerRadius.TopRight,
                (float)element.CornerRadius.BottomRight,
                (float)element.CornerRadius.BottomLeft);
        }
        else
        {
            // 普通矩形
            var bounds = CompileBounds(element);
            result = tessellator.TessellateRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        // 将顶点和索引数据添加到全局缓冲区
        var vertexOffset = (uint)_vertexData.Count;
        var indexOffset = (uint)_indexData.Count;

        // 将 Vector2 顶点转换为字节数据
        foreach (var vertex in result.Vertices)
        {
            var xBytes = BitConverter.GetBytes(vertex.X);
            var yBytes = BitConverter.GetBytes(vertex.Y);
            _vertexData.AddRange(xBytes);
            _vertexData.AddRange(yBytes);
        }

        // 添加索引数据
        _indexData.AddRange(result.Indices);

        var cache = new PathCache(
            pathHash,
            vertexOffset,
            (uint)result.Vertices.Length,
            indexOffset,
            (uint)result.Indices.Length);

        var index = (uint)_pathCaches.Count;
        _pathCaches.Add(cache);
        return index;
    }

    private static TriggerType MapTriggerType(string triggerType)
    {
        return triggerType switch
        {
            "MouseEnter" or ":hover" => TriggerType.MouseEnter,
            "MouseLeave" => TriggerType.MouseLeave,
            "MouseDown" or ":pressed" or ":active" => TriggerType.MouseDown,
            "MouseUp" => TriggerType.MouseUp,
            "Focus" or ":focus" => TriggerType.Focus,
            "Blur" => TriggerType.Blur,
            _ => TriggerType.None
        };
    }

    #endregion
}

/// <summary>
/// 编译上下文
/// </summary>
internal sealed class CompilerContext
{
    public UICompiler Compiler { get; }

    public CompilerContext(UICompiler compiler)
    {
        Compiler = compiler;
    }
}

/// <summary>
/// 编译器选项
/// </summary>
public sealed class CompilerOptions
{
    /// <summary>
    /// 目标视口宽度（用于布局计算）
    /// </summary>
    public double ViewportWidth { get; set; } = 1920;

    /// <summary>
    /// 目标视口高度（用于布局计算）
    /// </summary>
    public double ViewportHeight { get; set; } = 1080;

    /// <summary>
    /// 是否启用批处理优化
    /// </summary>
    public bool EnableBatching { get; set; } = true;

    /// <summary>
    /// 是否启用裁剪优化
    /// </summary>
    public bool EnableClipping { get; set; } = true;

    /// <summary>
    /// 是否启用变换合并
    /// </summary>
    public bool EnableTransformMerging { get; set; } = true;

    /// <summary>
    /// 是否生成调试信息
    /// </summary>
    public bool GenerateDebugInfo { get; set; } = false;

    /// <summary>
    /// 字形图集最大尺寸
    /// </summary>
    public int MaxGlyphAtlasSize { get; set; } = 2048;

    /// <summary>
    /// 路径细分精度
    /// </summary>
    public float PathTessellationTolerance { get; set; } = 0.25f;
}

#region AST Types (JALXAML 抽象语法树)

/// <summary>
/// JALXAML AST 根节点
/// </summary>
public sealed class JalxamlAst
{
    public required ElementNode Root { get; init; }
    public ResourceDictionary? Resources { get; init; }
}

/// <summary>
/// 资源字典
/// </summary>
public sealed class ResourceDictionary
{
    public List<StyleDef> Styles { get; } = new();
    public List<AnimationDef> Animations { get; } = new();
    public List<TemplateDef> Templates { get; } = new();
}

/// <summary>
/// 样式定义
/// </summary>
public sealed class StyleDef
{
    public string? Key { get; init; }
    public string? TargetType { get; init; }
    public string? Background { get; init; }
    public string? BorderBrush { get; init; }
    public string? Foreground { get; init; }
    public double Opacity { get; init; } = 1.0;
    public bool HasGradient => Gradient != null;
    public GradientAstDef? Gradient { get; init; }
    public List<TriggerDef> Triggers { get; } = new();
}

/// <summary>
/// 触发器定义
/// </summary>
public sealed class TriggerDef
{
    public required string Type { get; init; }
    public ColorDef? Background { get; init; }
    public ColorDef? BorderBrush { get; init; }
    public ColorDef? Foreground { get; init; }
    public double? Opacity { get; init; }
    public TransitionDef? Transition { get; init; }
}

/// <summary>
/// 过渡动画定义
/// </summary>
public sealed class TransitionDef
{
    public uint DurationMs { get; init; }
    public EasingType Easing { get; init; }
}

/// <summary>
/// 颜色定义
/// </summary>
public readonly struct ColorDef
{
    public readonly byte A, R, G, B;

    public ColorDef(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }
}

/// <summary>
/// 渐变定义（AST 版本）
/// </summary>
public sealed class GradientAstDef
{
    public GradientType Type { get; init; }
    public Point Start { get; init; }
    public Point End { get; init; }
    public List<GradientStop> Stops { get; } = new();
}

/// <summary>
/// 动画定义
/// </summary>
public sealed class AnimationDef
{
    public string? Key { get; init; }
    public EasingType Easing { get; init; }
    public uint DurationMs { get; init; }
    public uint DelayMs { get; init; }
    public byte RepeatCount { get; init; } = 1;
    public bool AutoReverse { get; init; }
}

/// <summary>
/// 模板定义
/// </summary>
public sealed class TemplateDef
{
    public string? Key { get; init; }
    public required ElementNode Root { get; init; }
}

/// <summary>
/// 元素节点
/// </summary>
public sealed class ElementNode
{
    public required ElementType Type { get; init; }
    public string? Name { get; init; }
    public string? StyleKey { get; init; }

    // 布局
    public double Left { get; init; }
    public double Top { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public int ZIndex { get; init; }

    // Grid 布局
    public string? RowDefinitions { get; init; }
    public string? ColumnDefinitions { get; init; }
    public int GridRow { get; init; }
    public int GridColumn { get; init; }
    public int GridRowSpan { get; init; } = 1;
    public int GridColumnSpan { get; init; } = 1;

    // StackPanel/WrapPanel 布局
    public Orientation Orientation { get; init; } = Orientation.Vertical;

    // DockPanel 布局
    public Dock Dock { get; init; } = Dock.Top;

    // 对齐
    public HorizontalAlignment HorizontalAlignment { get; init; } = HorizontalAlignment.Stretch;
    public VerticalAlignment VerticalAlignment { get; init; } = VerticalAlignment.Stretch;
    public ThicknessDef Margin { get; init; }

    // 外观
    public string? Background { get; init; }
    public string? BorderBrush { get; init; }
    public string? Foreground { get; init; }
    public double Opacity { get; init; } = 1.0;
    public CornerRadiusDef CornerRadius { get; init; }
    public ThicknessDef BorderThickness { get; init; }

    // 文本
    public string? Text { get; init; }
    public string? FontFamily { get; init; }
    public float FontSize { get; init; } = 14;

    // 图像
    public string? Source { get; init; }
    public ThicknessDef NineSlice { get; init; }

    // 路径
    public string? PathData { get; init; }
    public FillRule FillRule { get; init; }

    // Backdrop Filter
    public string? BackdropFilter { get; init; }
    public string? Material { get; init; }
    public string? MaterialTint { get; init; }
    public float MaterialTintOpacity { get; init; } = 0.6f;
    public float MaterialBlurRadius { get; init; }

    // 交互
    public bool IsInteractive { get; init; }
    public bool HasClickHandler { get; init; }
    public bool HasHoverHandler { get; init; }
    public bool IsFocusable { get; init; }
    public bool AcceptsKeyInput { get; init; }
    public bool IsDraggable { get; init; }
    public bool IsScrollable { get; init; }
    public uint HandlerIndex { get; init; }

    // 子元素
    public List<ElementNode> Children { get; } = new();
}

/// <summary>
/// 水平对齐
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Stretch
}

/// <summary>
/// 垂直对齐
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Stretch
}

/// <summary>
/// 元素类型
/// </summary>
public enum ElementType
{
    // 布局
    Grid,
    StackPanel,
    Canvas,
    DockPanel,
    WrapPanel,

    // 内容
    Border,
    Rectangle,
    Ellipse,
    Path,

    // 文本
    TextBlock,
    Label,
    TextBox,

    // 控件
    Button,
    CheckBox,
    RadioButton,
    ComboBox,
    ListBox,
    ScrollViewer,
    TabControl,

    // 图像
    Image,

    // 其他
    ContentPresenter,
    ItemsPresenter,
    Custom
}

/// <summary>
/// 圆角半径定义（AST 版本）
/// </summary>
public readonly struct CornerRadiusDef
{
    public readonly double TopLeft, TopRight, BottomRight, BottomLeft;

    public CornerRadiusDef(double uniform)
    {
        TopLeft = TopRight = BottomRight = BottomLeft = uniform;
    }

    public CornerRadiusDef(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }
}

/// <summary>
/// 边距定义（AST 版本）
/// </summary>
public readonly struct ThicknessDef
{
    public readonly double Left, Top, Right, Bottom;

    public ThicknessDef(double uniform)
    {
        Left = Top = Right = Bottom = uniform;
    }

    public ThicknessDef(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}

#endregion

#region JALXAML Parser

/// <summary>
/// JALXAML 解析器 - 将 XML 格式的 JALXAML 解析为 AST
/// </summary>
public sealed class JalxamlParser
{
    private static readonly Dictionary<string, ElementType> ElementTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Grid"] = ElementType.Grid,
        ["StackPanel"] = ElementType.StackPanel,
        ["Canvas"] = ElementType.Canvas,
        ["DockPanel"] = ElementType.DockPanel,
        ["WrapPanel"] = ElementType.WrapPanel,
        ["Border"] = ElementType.Border,
        ["Rectangle"] = ElementType.Rectangle,
        ["Ellipse"] = ElementType.Ellipse,
        ["Path"] = ElementType.Path,
        ["TextBlock"] = ElementType.TextBlock,
        ["Label"] = ElementType.Label,
        ["TextBox"] = ElementType.TextBox,
        ["Button"] = ElementType.Button,
        ["CheckBox"] = ElementType.CheckBox,
        ["RadioButton"] = ElementType.RadioButton,
        ["ComboBox"] = ElementType.ComboBox,
        ["ListBox"] = ElementType.ListBox,
        ["ScrollViewer"] = ElementType.ScrollViewer,
        ["TabControl"] = ElementType.TabControl,
        ["Image"] = ElementType.Image,
        ["ContentPresenter"] = ElementType.ContentPresenter,
        ["ItemsPresenter"] = ElementType.ItemsPresenter,
    };

    private readonly Dictionary<string, StyleDef> _styles = new();
    private readonly Dictionary<string, AnimationDef> _animations = new();
    private readonly List<TemplateDef> _templates = new();
    private uint _handlerIndex;

    public JalxamlAst Parse(string source)
    {
        using var reader = new StringReader(source);
        var doc = System.Xml.Linq.XDocument.Parse(source);

        if (doc.Root == null)
            throw new InvalidOperationException("Empty JALXAML document");

        // 解析资源
        ResourceDictionary? resources = null;
        var resourcesElement = doc.Root.Element(doc.Root.Name.Namespace + doc.Root.Name.LocalName + ".Resources");
        if (resourcesElement != null)
        {
            resources = ParseResources(resourcesElement);
        }

        // 解析根元素
        var rootElement = ParseElement(doc.Root);

        return new JalxamlAst
        {
            Root = rootElement,
            Resources = resources
        };
    }

    private ResourceDictionary ParseResources(System.Xml.Linq.XElement element)
    {
        var resources = new ResourceDictionary();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName == "Style")
            {
                var style = ParseStyle(child);
                resources.Styles.Add(style);
                if (!string.IsNullOrEmpty(style.Key))
                {
                    _styles[style.Key] = style;
                }
            }
            else if (localName == "Storyboard" || localName == "Animation")
            {
                var animation = ParseAnimation(child);
                resources.Animations.Add(animation);
                if (!string.IsNullOrEmpty(animation.Key))
                {
                    _animations[animation.Key] = animation;
                }
            }
            else if (localName == "ControlTemplate" || localName == "DataTemplate")
            {
                var template = ParseTemplate(child);
                resources.Templates.Add(template);
            }
        }

        return resources;
    }

    private StyleDef ParseStyle(System.Xml.Linq.XElement element)
    {
        var key = GetAttributeValue(element, "x:Key") ?? GetAttributeValue(element, "Key");
        var targetType = GetAttributeValue(element, "TargetType");

        string? background = null;
        string? borderBrush = null;
        string? foreground = null;
        double opacity = 1.0;
        GradientAstDef? gradient = null;
        var triggers = new List<TriggerDef>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName == "Setter")
            {
                var property = GetAttributeValue(child, "Property");
                var value = GetAttributeValue(child, "Value");

                switch (property)
                {
                    case "Background":
                        background = value;
                        break;
                    case "BorderBrush":
                        borderBrush = value;
                        break;
                    case "Foreground":
                        foreground = value;
                        break;
                    case "Opacity":
                        if (double.TryParse(value, out var op))
                            opacity = op;
                        break;
                }
            }
            else if (localName.EndsWith(".Triggers", StringComparison.Ordinal) || localName == "Style.Triggers")
            {
                foreach (var triggerElement in child.Elements())
                {
                    triggers.Add(ParseTrigger(triggerElement));
                }
            }
        }

        return new StyleDef
        {
            Key = key,
            TargetType = targetType,
            Background = background,
            BorderBrush = borderBrush,
            Foreground = foreground,
            Opacity = opacity,
            Gradient = gradient
        };
    }

    private TriggerDef ParseTrigger(System.Xml.Linq.XElement element)
    {
        var triggerType = element.Name.LocalName;
        ColorDef? background = null;
        ColorDef? borderBrush = null;
        ColorDef? foreground = null;
        double? opacity = null;
        TransitionDef? transition = null;

        // 检查属性触发器的属性值
        var property = GetAttributeValue(element, "Property");
        var value = GetAttributeValue(element, "Value");

        if (triggerType == "Trigger" && property == "IsMouseOver" && value == "True")
        {
            triggerType = ":hover";
        }
        else if (triggerType == "Trigger" && property == "IsPressed" && value == "True")
        {
            triggerType = ":pressed";
        }
        else if (triggerType == "Trigger" && property == "IsFocused" && value == "True")
        {
            triggerType = ":focus";
        }

        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "Setter")
            {
                var setterProperty = GetAttributeValue(child, "Property");
                var setterValue = GetAttributeValue(child, "Value");

                switch (setterProperty)
                {
                    case "Background":
                        background = ParseColorValue(setterValue);
                        break;
                    case "BorderBrush":
                        borderBrush = ParseColorValue(setterValue);
                        break;
                    case "Foreground":
                        foreground = ParseColorValue(setterValue);
                        break;
                    case "Opacity":
                        if (double.TryParse(setterValue, out var op))
                            opacity = op;
                        break;
                }
            }
        }

        // 解析过渡
        var durationAttr = GetAttributeValue(element, "Duration");
        if (!string.IsNullOrEmpty(durationAttr))
        {
            transition = new TransitionDef
            {
                DurationMs = ParseDuration(durationAttr),
                Easing = EasingType.EaseInOut
            };
        }

        return new TriggerDef
        {
            Type = triggerType,
            Background = background,
            BorderBrush = borderBrush,
            Foreground = foreground,
            Opacity = opacity,
            Transition = transition
        };
    }

    private AnimationDef ParseAnimation(System.Xml.Linq.XElement element)
    {
        var key = GetAttributeValue(element, "x:Key") ?? GetAttributeValue(element, "Key");
        var durationStr = GetAttributeValue(element, "Duration") ?? "300";
        var easingStr = GetAttributeValue(element, "Easing") ?? "EaseInOut";
        var repeatStr = GetAttributeValue(element, "RepeatCount") ?? "1";
        var autoReverse = GetAttributeValue(element, "AutoReverse") == "True";

        return new AnimationDef
        {
            Key = key,
            DurationMs = ParseDuration(durationStr),
            Easing = ParseEasing(easingStr),
            RepeatCount = byte.TryParse(repeatStr, out var r) ? r : (byte)1,
            AutoReverse = autoReverse
        };
    }

    private TemplateDef ParseTemplate(System.Xml.Linq.XElement element)
    {
        var key = GetAttributeValue(element, "x:Key") ?? GetAttributeValue(element, "Key");

        // 找到模板的根元素
        var rootElement = element.Elements().FirstOrDefault();
        if (rootElement == null)
            throw new InvalidOperationException("Template must have a root element");

        return new TemplateDef
        {
            Key = key,
            Root = ParseElement(rootElement)
        };
    }

    private ElementNode ParseElement(System.Xml.Linq.XElement element)
    {
        var localName = element.Name.LocalName;

        // 跳过资源定义
        if (localName.EndsWith(".Resources", StringComparison.Ordinal))
            return ParseElement(element.Elements().First());

        var elementType = ElementTypeMap.TryGetValue(localName, out var type)
            ? type
            : ElementType.Custom;

        var node = new ElementNode
        {
            Type = elementType,
            Name = GetAttributeValue(element, "x:Name") ?? GetAttributeValue(element, "Name"),
            StyleKey = GetAttributeValue(element, "Style")?.TrimStart('{').TrimEnd('}')
                        .Replace("StaticResource ", "").Replace("DynamicResource ", "").Trim(),

            // 布局属性
            Left = ParseDouble(GetAttributeValue(element, "Canvas.Left")),
            Top = ParseDouble(GetAttributeValue(element, "Canvas.Top")),
            Width = ParseDouble(GetAttributeValue(element, "Width")),
            Height = ParseDouble(GetAttributeValue(element, "Height")),
            ZIndex = (int)ParseDouble(GetAttributeValue(element, "Panel.ZIndex")),

            // Grid 布局属性
            RowDefinitions = ParseRowColumnDefinitions(element, "Grid.RowDefinitions", "RowDefinition"),
            ColumnDefinitions = ParseRowColumnDefinitions(element, "Grid.ColumnDefinitions", "ColumnDefinition"),
            GridRow = (int)ParseDouble(GetAttributeValue(element, "Grid.Row")),
            GridColumn = (int)ParseDouble(GetAttributeValue(element, "Grid.Column")),
            GridRowSpan = Math.Max(1, (int)ParseDouble(GetAttributeValue(element, "Grid.RowSpan"), 1)),
            GridColumnSpan = Math.Max(1, (int)ParseDouble(GetAttributeValue(element, "Grid.ColumnSpan"), 1)),

            // StackPanel/WrapPanel 布局
            Orientation = ParseOrientation(GetAttributeValue(element, "Orientation")),

            // DockPanel 布局
            Dock = ParseDock(GetAttributeValue(element, "DockPanel.Dock")),

            // 对齐
            HorizontalAlignment = ParseHorizontalAlignment(GetAttributeValue(element, "HorizontalAlignment")),
            VerticalAlignment = ParseVerticalAlignment(GetAttributeValue(element, "VerticalAlignment")),
            Margin = ParseThickness(GetAttributeValue(element, "Margin")),

            // 外观属性
            Background = GetAttributeValue(element, "Background"),
            BorderBrush = GetAttributeValue(element, "BorderBrush"),
            Foreground = GetAttributeValue(element, "Foreground"),
            Opacity = ParseDouble(GetAttributeValue(element, "Opacity"), 1.0),
            CornerRadius = ParseCornerRadius(GetAttributeValue(element, "CornerRadius")),
            BorderThickness = ParseThickness(GetAttributeValue(element, "BorderThickness")),

            // 文本属性
            Text = GetAttributeValue(element, "Text") ?? element.Value.Trim(),
            FontFamily = GetAttributeValue(element, "FontFamily"),
            FontSize = (float)ParseDouble(GetAttributeValue(element, "FontSize"), 14),

            // 图像属性
            Source = GetAttributeValue(element, "Source"),

            // 路径属性
            PathData = GetAttributeValue(element, "Data"),
            FillRule = GetAttributeValue(element, "FillRule") == "NonZero" ? FillRule.NonZero : FillRule.EvenOdd,

            // Backdrop Filter 属性
            BackdropFilter = GetAttributeValue(element, "BackdropFilter"),
            Material = GetAttributeValue(element, "Material"),
            MaterialTint = GetAttributeValue(element, "MaterialTint"),
            MaterialTintOpacity = (float)ParseDouble(GetAttributeValue(element, "MaterialTintOpacity"), 0.6),
            MaterialBlurRadius = (float)ParseDouble(GetAttributeValue(element, "MaterialBlurRadius"), 0),

            // 交互属性
            IsInteractive = HasEventHandler(element),
            HasClickHandler = HasAttribute(element, "Click") || HasAttribute(element, "Command"),
            HasHoverHandler = HasAttribute(element, "MouseEnter") || HasAttribute(element, "MouseLeave"),
            IsFocusable = GetAttributeValue(element, "Focusable") != "False" && IsDefaultFocusable(elementType),
            AcceptsKeyInput = elementType == ElementType.TextBox,
            IsDraggable = HasAttribute(element, "DragStart") || HasAttribute(element, "DragMove"),
            IsScrollable = elementType == ElementType.ScrollViewer,
            HandlerIndex = HasEventHandler(element) ? _handlerIndex++ : 0
        };

        // 解析子元素
        foreach (var child in element.Elements())
        {
            var childName = child.Name.LocalName;

            // 跳过属性元素
            if (childName.Contains('.'))
                continue;

            node.Children.Add(ParseElement(child));
        }

        return node;
    }

    private static string? ParseRowColumnDefinitions(System.Xml.Linq.XElement element, string propertyElementName, string definitionElementName)
    {
        // 查找属性元素（如 Grid.RowDefinitions）
        var propElement = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName == propertyElementName);

        if (propElement == null)
            return null;

        // 解析子定义元素
        var definitions = new List<string>();
        foreach (var defElement in propElement.Elements().Where(e => e.Name.LocalName == definitionElementName))
        {
            var height = GetAttributeValue(defElement, "Height") ?? GetAttributeValue(defElement, "Width");
            definitions.Add(height ?? "*");
        }

        return definitions.Count > 0 ? string.Join(",", definitions) : null;
    }

    private static Orientation ParseOrientation(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "horizontal" => Orientation.Horizontal,
            "vertical" => Orientation.Vertical,
            _ => Orientation.Vertical
        };
    }

    private static Dock ParseDock(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "left" => Dock.Left,
            "top" => Dock.Top,
            "right" => Dock.Right,
            "bottom" => Dock.Bottom,
            _ => Dock.Left
        };
    }

    private static HorizontalAlignment ParseHorizontalAlignment(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            "stretch" => HorizontalAlignment.Stretch,
            _ => HorizontalAlignment.Stretch
        };
    }

    private static VerticalAlignment ParseVerticalAlignment(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "top" => VerticalAlignment.Top,
            "center" => VerticalAlignment.Center,
            "bottom" => VerticalAlignment.Bottom,
            "stretch" => VerticalAlignment.Stretch,
            _ => VerticalAlignment.Stretch
        };
    }

    #region Parse Helpers

    private static string? GetAttributeValue(System.Xml.Linq.XElement element, string name)
    {
        // x: 命名空间的属性（如 x:Name, x:Class, x:Key）
        if (name.StartsWith("x:", StringComparison.Ordinal))
        {
            var xName = System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
            var attr = element.Attribute(xName + name[2..]);
            return attr?.Value;
        }

        // 普通属性（无命名空间）
        return element.Attribute(name)?.Value;
    }

    private static bool HasAttribute(System.Xml.Linq.XElement element, string name)
    {
        return element.Attribute(name) != null;
    }

    private static bool HasEventHandler(System.Xml.Linq.XElement element)
    {
        return HasAttribute(element, "Click") ||
               HasAttribute(element, "Command") ||
               HasAttribute(element, "MouseEnter") ||
               HasAttribute(element, "MouseLeave") ||
               HasAttribute(element, "MouseDown") ||
               HasAttribute(element, "MouseUp") ||
               HasAttribute(element, "KeyDown") ||
               HasAttribute(element, "KeyUp") ||
               HasAttribute(element, "GotFocus") ||
               HasAttribute(element, "LostFocus");
    }

    private static bool IsDefaultFocusable(ElementType type)
    {
        return type switch
        {
            ElementType.Button or
            ElementType.CheckBox or
            ElementType.RadioButton or
            ElementType.ComboBox or
            ElementType.ListBox or
            ElementType.TextBox or
            ElementType.TabControl => true,
            _ => false
        };
    }

    private static double ParseDouble(string? value, double defaultValue = 0)
    {
        if (string.IsNullOrEmpty(value)) return defaultValue;
        if (value == "Auto" || value == "NaN") return double.NaN;
        if (value == "*") return double.PositiveInfinity;
        if (value.EndsWith('*'))
        {
            // 星号比例，暂时返回特殊值
            if (double.TryParse(value.TrimEnd('*'), out var ratio))
                return -ratio; // 负数表示比例
            return -1;
        }
        return double.TryParse(value, out var result) ? result : defaultValue;
    }

    private static CornerRadiusDef ParseCornerRadius(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new CornerRadiusDef(0);

        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        return parts.Length switch
        {
            1 => new CornerRadiusDef(ParseDouble(parts[0])),
            4 => new CornerRadiusDef(
                ParseDouble(parts[0]),
                ParseDouble(parts[1]),
                ParseDouble(parts[2]),
                ParseDouble(parts[3])),
            _ => new CornerRadiusDef(0)
        };
    }

    private static ThicknessDef ParseThickness(string? value)
    {
        if (string.IsNullOrEmpty(value)) return new ThicknessDef(0);

        var parts = value.Split(',', ' ').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

        return parts.Length switch
        {
            1 => new ThicknessDef(ParseDouble(parts[0])),
            2 => new ThicknessDef(ParseDouble(parts[0]), ParseDouble(parts[1]),
                                  ParseDouble(parts[0]), ParseDouble(parts[1])),
            4 => new ThicknessDef(
                ParseDouble(parts[0]),
                ParseDouble(parts[1]),
                ParseDouble(parts[2]),
                ParseDouble(parts[3])),
            _ => new ThicknessDef(0)
        };
    }

    private static ColorDef? ParseColorValue(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 6)
            {
                var rgb = Convert.ToUInt32(hex, 16);
                return new ColorDef(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }
            else if (hex.Length == 8)
            {
                var argb = Convert.ToUInt32(hex, 16);
                return new ColorDef((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
        }

        // 命名颜色
        return value.ToLowerInvariant() switch
        {
            "transparent" => new ColorDef(0, 0, 0, 0),
            "black" => new ColorDef(255, 0, 0, 0),
            "white" => new ColorDef(255, 255, 255, 255),
            "red" => new ColorDef(255, 255, 0, 0),
            "green" => new ColorDef(255, 0, 128, 0),
            "blue" => new ColorDef(255, 0, 0, 255),
            "yellow" => new ColorDef(255, 255, 255, 0),
            "orange" => new ColorDef(255, 255, 165, 0),
            "purple" => new ColorDef(255, 128, 0, 128),
            "gray" or "grey" => new ColorDef(255, 128, 128, 128),
            _ => null
        };
    }

    private static uint ParseDuration(string value)
    {
        // 解析 "0:0:0.3" 或 "300ms" 或 "0.3s" 格式
        if (value.EndsWith("ms", StringComparison.Ordinal))
        {
            return uint.TryParse(value[..^2], out var ms) ? ms : 300;
        }
        if (value.EndsWith("s", StringComparison.Ordinal))
        {
            return uint.TryParse(value[..^1], out var s) ? s * 1000 : 300;
        }
        if (value.Contains(':'))
        {
            var parts = value.Split(':');
            if (parts.Length >= 3 && double.TryParse(parts[2], out var seconds))
            {
                return (uint)(seconds * 1000);
            }
        }
        return uint.TryParse(value, out var result) ? result : 300;
    }

    private static EasingType ParseEasing(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "linear" => EasingType.Linear,
            "easein" => EasingType.EaseIn,
            "easeout" => EasingType.EaseOut,
            "easeinout" => EasingType.EaseInOut,
            "easeincubic" => EasingType.EaseInCubic,
            "easeoutcubic" => EasingType.EaseOutCubic,
            "easeinoutcubic" => EasingType.EaseInOutCubic,
            "easeinquad" => EasingType.EaseInQuad,
            "easeoutquad" => EasingType.EaseOutQuad,
            "easeinoutquad" => EasingType.EaseInOutQuad,
            "easeinexpo" => EasingType.EaseInExpo,
            "easeoutexpo" => EasingType.EaseOutExpo,
            "easeinoutexpo" => EasingType.EaseInOutExpo,
            "easeinback" => EasingType.EaseInBack,
            "easeoutback" => EasingType.EaseOutBack,
            "easeinoutback" => EasingType.EaseInOutBack,
            "easeinelastic" => EasingType.EaseInElastic,
            "easeoutelastic" => EasingType.EaseOutElastic,
            "easeinoutelastic" => EasingType.EaseInOutElastic,
            "easeinbounce" => EasingType.EaseInBounce,
            "easeoutbounce" => EasingType.EaseOutBounce,
            "easeinoutbounce" => EasingType.EaseInOutBounce,
            "spring" => EasingType.Spring,
            _ => EasingType.EaseInOut
        };
    }

    #endregion

    /// <summary>
    /// 从文件解析 JALXAML
    /// </summary>
    public JalxamlDocument? ParseFile(string path)
    {
        if (!File.Exists(path))
            return null;

        var source = File.ReadAllText(path);
        return ParseDocument(source);
    }

    /// <summary>
    /// 解析 JALXAML 为文档对象（包含代码生成所需的元数据）
    /// </summary>
    public JalxamlDocument ParseDocument(string source)
    {
        var ast = Parse(source);
        var document = new JalxamlDocument
        {
            ClassName = ExtractClassName(source),
            RootElement = ast.Root
        };

        // 收集命名元素
        CollectNamedElements(ast.Root, document.NamedElements);

        return document;
    }

    private static string? ExtractClassName(string source)
    {
        // 从 x:Class 属性提取类名
        var match = System.Text.RegularExpressions.Regex.Match(source, @"x:Class\s*=\s*""([^""]+)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static void CollectNamedElements(ElementNode element, List<NamedElementInfo> namedElements)
    {
        if (!string.IsNullOrEmpty(element.Name))
        {
            namedElements.Add(new NamedElementInfo
            {
                Name = element.Name,
                TypeName = GetElementTypeName(element.Type)
            });
        }

        foreach (var child in element.Children)
        {
            CollectNamedElements(child, namedElements);
        }
    }

    private static string GetElementTypeName(ElementType type)
    {
        return type switch
        {
            ElementType.Grid => "Jalium.UI.Controls.Grid",
            ElementType.StackPanel => "Jalium.UI.Controls.StackPanel",
            ElementType.Canvas => "Jalium.UI.Controls.Canvas",
            ElementType.DockPanel => "Jalium.UI.Controls.DockPanel",
            ElementType.WrapPanel => "Jalium.UI.Controls.WrapPanel",
            ElementType.Border => "Jalium.UI.Controls.Border",
            ElementType.Rectangle => "Jalium.UI.Shapes.Rectangle",
            ElementType.Ellipse => "Jalium.UI.Shapes.Ellipse",
            ElementType.Path => "Jalium.UI.Shapes.Path",
            ElementType.TextBlock => "Jalium.UI.Controls.TextBlock",
            ElementType.Label => "Jalium.UI.Controls.Label",
            ElementType.TextBox => "Jalium.UI.Controls.TextBox",
            ElementType.Button => "Jalium.UI.Controls.Button",
            ElementType.CheckBox => "Jalium.UI.Controls.CheckBox",
            ElementType.RadioButton => "Jalium.UI.Controls.RadioButton",
            ElementType.ComboBox => "Jalium.UI.Controls.ComboBox",
            ElementType.ListBox => "Jalium.UI.Controls.ListBox",
            ElementType.ScrollViewer => "Jalium.UI.Controls.ScrollViewer",
            ElementType.TabControl => "Jalium.UI.Controls.TabControl",
            ElementType.Image => "Jalium.UI.Controls.Image",
            ElementType.ContentPresenter => "Jalium.UI.Controls.ContentPresenter",
            ElementType.ItemsPresenter => "Jalium.UI.Controls.ItemsPresenter",
            _ => "Jalium.UI.FrameworkElement"
        };
    }
}

/// <summary>
/// JALXAML 文档 - 用于代码生成
/// </summary>
public sealed class JalxamlDocument
{
    /// <summary>
    /// x:Class 指定的完整类名
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// 根元素
    /// </summary>
    public ElementNode? RootElement { get; init; }

    /// <summary>
    /// 命名元素列表（带有 x:Name 的元素）
    /// </summary>
    public List<NamedElementInfo> NamedElements { get; } = new();
}

/// <summary>
/// 命名元素信息
/// </summary>
public sealed class NamedElementInfo
{
    /// <summary>
    /// 元素名称（x:Name 的值）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 元素类型的完整名称
    /// </summary>
    public string? TypeName { get; init; }
}

#endregion
