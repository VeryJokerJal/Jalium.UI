namespace Jalium.UI.Gpu.Materials;

/// <summary>
/// 增强材质描述符 - 将样式编译为 GPU shader 参数绑定
/// 包装现有 Material + 扩展 shader 相关信息
/// </summary>
public readonly struct MaterialDescriptor
{
    /// <summary>
    /// 材质分类 - 决定使用哪个 shader 变体
    /// </summary>
    public readonly MaterialCategory Category;

    /// <summary>
    /// 基础材质数据（保持与 RenderIR.Material 兼容）
    /// </summary>
    public readonly Material BaseMaterial;

    /// <summary>
    /// 纹理绑定槽（Category == Textured/NineSlice 时有效）
    /// </summary>
    public readonly uint TextureSlot;

    /// <summary>
    /// 渐变缓冲区偏移（Category == Gradient 时有效）
    /// </summary>
    public readonly uint GradientBufferOffset;

    /// <summary>
    /// SDF 平滑度参数（控制抗锯齿宽度）
    /// </summary>
    public readonly float SdfSmoothness;

    /// <summary>
    /// 自定义 shader 索引（Category == Custom 时有效）
    /// </summary>
    public readonly uint CustomShaderIndex;

    /// <summary>
    /// 动态 uniform 块偏移（用于动画更新）
    /// </summary>
    public readonly uint UniformBlockOffset;

    /// <summary>
    /// 动态 uniform 块大小
    /// </summary>
    public readonly uint UniformBlockSize;

    /// <summary>
    /// 材质标志位
    /// </summary>
    public readonly MaterialFlags Flags;

    public MaterialDescriptor(
        MaterialCategory category,
        Material baseMaterial,
        uint textureSlot = 0,
        uint gradientBufferOffset = 0,
        float sdfSmoothness = 1.0f,
        uint customShaderIndex = 0,
        uint uniformBlockOffset = 0,
        uint uniformBlockSize = 0,
        MaterialFlags flags = MaterialFlags.None)
    {
        Category = category;
        BaseMaterial = baseMaterial;
        TextureSlot = textureSlot;
        GradientBufferOffset = gradientBufferOffset;
        SdfSmoothness = sdfSmoothness;
        CustomShaderIndex = customShaderIndex;
        UniformBlockOffset = uniformBlockOffset;
        UniformBlockSize = uniformBlockSize;
        Flags = flags;
    }

    /// <summary>
    /// 是否需要纹理采样
    /// </summary>
    public bool RequiresTexture => Category is MaterialCategory.Textured or MaterialCategory.NineSlice or MaterialCategory.SdfText;

    /// <summary>
    /// 是否需要渐变 SSBO
    /// </summary>
    public bool RequiresGradient => Category == MaterialCategory.Gradient;

    /// <summary>
    /// 是否需要 Alpha 混合
    /// </summary>
    public bool RequiresBlending => BaseMaterial.Opacity < 255 || Flags.HasFlag(MaterialFlags.ForceBlend);

    /// <summary>
    /// 生成传给 shader 的 MaterialData 结构体字节（匹配 HLSL MaterialData）
    /// </summary>
    public byte[] ToGpuData()
    {
        // 32 bytes: 匹配 HLSL StructuredBuffer<MaterialData>
        var data = new byte[32];
        using var ms = new MemoryStream(data);
        using var w = new BinaryWriter(ms);

        w.Write(BaseMaterial.BackgroundColor);   // 4
        w.Write(BaseMaterial.BorderColor);       // 4
        w.Write(BaseMaterial.ForegroundColor);   // 4
        w.Write(BaseMaterial.GradientIndex);     // 4
        w.Write(BaseMaterial.Opacity / 255f);    // 4 (float)
        w.Write((uint)BaseMaterial.BlendMode);   // 4
        w.Write(SdfSmoothness);                  // 4
        w.Write((uint)Flags);                    // 4

        return data;
    }
}

/// <summary>
/// 材质分类 - 决定 shader 分派
/// </summary>
public enum MaterialCategory : byte
{
    /// <summary>
    /// 纯色材质 - 最简单的 shader 路径
    /// </summary>
    Solid,

    /// <summary>
    /// 渐变材质 - 需要采样渐变 SSBO
    /// </summary>
    Gradient,

    /// <summary>
    /// 纹理材质 - 需要纹理采样
    /// </summary>
    Textured,

    /// <summary>
    /// 九宫格纹理 - 需要特殊 UV 计算
    /// </summary>
    NineSlice,

    /// <summary>
    /// SDF 文本 - 使用字形图集 + SDF 采样
    /// </summary>
    SdfText,

    /// <summary>
    /// 后处理效果 - 模糊、颜色矩阵等
    /// </summary>
    Effect,

    /// <summary>
    /// 自定义 shader
    /// </summary>
    Custom
}

/// <summary>
/// 材质标志位
/// </summary>
[Flags]
public enum MaterialFlags : uint
{
    None = 0,

    /// <summary>
    /// 强制使用 Alpha 混合（即使 Opacity == 255）
    /// </summary>
    ForceBlend = 1 << 0,

    /// <summary>
    /// 使用预乘 Alpha
    /// </summary>
    PremultipliedAlpha = 1 << 1,

    /// <summary>
    /// 双面渲染（路径填充）
    /// </summary>
    DoubleSided = 1 << 2,

    /// <summary>
    /// 可动画化 - 有动态 uniform 块
    /// </summary>
    Animatable = 1 << 3,

    /// <summary>
    /// 使用 SDF 圆角
    /// </summary>
    SdfRoundedCorners = 1 << 4,

    /// <summary>
    /// 需要 Backdrop Filter
    /// </summary>
    BackdropFilter = 1 << 5
}

/// <summary>
/// 材质模板 - 样式编译结果，支持状态覆盖
/// </summary>
public sealed class MaterialTemplate
{
    /// <summary>
    /// 模板键（样式标识）
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 基础材质描述符（默认状态）
    /// </summary>
    public MaterialDescriptor BaseMaterial { get; }

    /// <summary>
    /// 状态覆盖（hover, pressed, focused, disabled 等）
    /// </summary>
    public IReadOnlyDictionary<TriggerType, MaterialDescriptor> StateOverrides { get; }

    public MaterialTemplate(
        string key,
        MaterialDescriptor baseMaterial,
        Dictionary<TriggerType, MaterialDescriptor> stateOverrides)
    {
        Key = key;
        BaseMaterial = baseMaterial;
        StateOverrides = stateOverrides;
    }

    /// <summary>
    /// 获取指定状态的材质描述符
    /// </summary>
    public MaterialDescriptor GetForState(TriggerType trigger)
    {
        return StateOverrides.TryGetValue(trigger, out var descriptor)
            ? descriptor
            : BaseMaterial;
    }
}
