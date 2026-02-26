namespace Jalium.UI.Gpu.Materials;

/// <summary>
/// 材质编译器 - 将样式/材质定义编译为 GPU 友好的 MaterialDescriptor
/// </summary>
public sealed class MaterialCompiler
{
    private readonly List<MaterialDescriptor> _compiledMaterials = new();
    private readonly Dictionary<string, MaterialTemplate> _templates = new();
    private uint _nextUniformOffset;

    /// <summary>
    /// 已编译材质列表
    /// </summary>
    public IReadOnlyList<MaterialDescriptor> CompiledMaterials => _compiledMaterials;

    /// <summary>
    /// 编译单个材质 - 从基础 Material + 可选渐变/纹理推断类别
    /// </summary>
    public MaterialDescriptor Compile(
        Material baseMaterial,
        GradientDef? gradient = null,
        TextureRef? texture = null,
        bool isText = false,
        bool isAnimatable = false)
    {
        var category = DetermineCategory(baseMaterial, gradient, texture, isText);
        var flags = MaterialFlags.None;

        if (baseMaterial.Opacity < 255)
            flags |= MaterialFlags.ForceBlend;
        if (isAnimatable)
            flags |= MaterialFlags.Animatable;
        if (category == MaterialCategory.SdfText)
            flags |= MaterialFlags.PremultipliedAlpha;

        uint uniformBlockOffset = 0;
        uint uniformBlockSize = 0;

        if (isAnimatable)
        {
            uniformBlockOffset = _nextUniformOffset;
            uniformBlockSize = 32; // MaterialData size
            _nextUniformOffset += uniformBlockSize;
        }

        var descriptor = new MaterialDescriptor(
            category: category,
            baseMaterial: baseMaterial,
            textureSlot: texture.HasValue ? (uint)_compiledMaterials.Count : 0,
            gradientBufferOffset: gradient.HasValue ? gradient.Value.StopsIndex : 0,
            sdfSmoothness: category == MaterialCategory.SdfText ? 0.5f : 1.0f,
            uniformBlockOffset: uniformBlockOffset,
            uniformBlockSize: uniformBlockSize,
            flags: flags);

        _compiledMaterials.Add(descriptor);
        return descriptor;
    }

    /// <summary>
    /// 批量编译 - 从 CompiledUIBundle 中的材质数组
    /// </summary>
    public MaterialDescriptor[] CompileBatch(
        Material[] materials,
        GradientDef[] gradients,
        SceneNode[] nodes)
    {
        var result = new MaterialDescriptor[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            GradientDef? gradient = mat.GradientIndex > 0 && mat.GradientIndex < gradients.Length
                ? gradients[mat.GradientIndex]
                : null;

            // 检查是否有节点使用此材质作为文本
            var isText = false;
            for (int j = 0; j < nodes.Length; j++)
            {
                if (nodes[j].MaterialIndex == i && nodes[j] is TextNode)
                {
                    isText = true;
                    break;
                }
            }

            // 检查是否有动画目标指向使用此材质的节点
            var isAnimatable = HasAnimatedNodes(i, nodes);

            result[i] = Compile(mat, gradient, isText: isText, isAnimatable: isAnimatable);
        }

        return result;
    }

    /// <summary>
    /// 创建样式模板（支持状态覆盖）
    /// </summary>
    public MaterialTemplate CreateTemplate(
        string key,
        MaterialDescriptor baseMaterial,
        Dictionary<TriggerType, MaterialDescriptor> stateOverrides)
    {
        var template = new MaterialTemplate(key, baseMaterial, stateOverrides);
        _templates[key] = template;
        return template;
    }

    /// <summary>
    /// 获取已注册的模板
    /// </summary>
    public MaterialTemplate? GetTemplate(string key)
    {
        return _templates.TryGetValue(key, out var template) ? template : null;
    }

    /// <summary>
    /// 生成所有编译材质的 GPU uniform 数据
    /// 返回的 byte[] 直接上传到 StructuredBuffer
    /// </summary>
    public byte[] GenerateUniformData()
    {
        const int materialDataSize = 32;
        var data = new byte[_compiledMaterials.Count * materialDataSize];

        for (int i = 0; i < _compiledMaterials.Count; i++)
        {
            var gpuData = _compiledMaterials[i].ToGpuData();
            Buffer.BlockCopy(gpuData, 0, data, i * materialDataSize, materialDataSize);
        }

        return data;
    }

    /// <summary>
    /// 按 MaterialCategory 分组（用于 batch 排序）
    /// </summary>
    public Dictionary<MaterialCategory, List<int>> GroupByCategory()
    {
        var groups = new Dictionary<MaterialCategory, List<int>>();

        for (int i = 0; i < _compiledMaterials.Count; i++)
        {
            var cat = _compiledMaterials[i].Category;
            if (!groups.TryGetValue(cat, out var list))
            {
                list = new List<int>();
                groups[cat] = list;
            }
            list.Add(i);
        }

        return groups;
    }

    /// <summary>
    /// 重置编译器状态
    /// </summary>
    public void Reset()
    {
        _compiledMaterials.Clear();
        _templates.Clear();
        _nextUniformOffset = 0;
    }

    private static MaterialCategory DetermineCategory(
        Material baseMaterial,
        GradientDef? gradient,
        TextureRef? texture,
        bool isText)
    {
        if (isText) return MaterialCategory.SdfText;
        if (texture.HasValue) return MaterialCategory.Textured;
        if (gradient.HasValue) return MaterialCategory.Gradient;
        return MaterialCategory.Solid;
    }

    private static bool HasAnimatedNodes(int materialIndex, SceneNode[] nodes)
    {
        // 简化检测：检查使用此材质的节点是否可能被动画化
        // 实际实现中应检查 AnimationTarget 数组
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].MaterialIndex == (uint)materialIndex && !nodes[i].IsVisible)
            {
                // 不可见节点可能是动画初始状态
                return true;
            }
        }
        return false;
    }
}
