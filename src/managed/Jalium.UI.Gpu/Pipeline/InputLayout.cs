namespace Jalium.UI.Gpu.Pipeline;

/// <summary>
/// 输入元素描述 - 匹配 D3D12_INPUT_ELEMENT_DESC
/// </summary>
public readonly struct InputElementDesc
{
    /// <summary>
    /// 语义名称 (POSITION, TEXCOORD, INST_POSITION, ...)
    /// </summary>
    public readonly string SemanticName;

    /// <summary>
    /// 语义索引
    /// </summary>
    public readonly int SemanticIndex;

    /// <summary>
    /// 顶点格式
    /// </summary>
    public readonly VertexFormat Format;

    /// <summary>
    /// 输入槽 (0 = per-vertex, 1 = per-instance)
    /// </summary>
    public readonly int InputSlot;

    /// <summary>
    /// 在缓冲区中的偏移量
    /// </summary>
    public readonly int AlignedByteOffset;

    /// <summary>
    /// 输入分类
    /// </summary>
    public readonly InputClassification Classification;

    /// <summary>
    /// 实例步进率 (0 = per-vertex, 1 = per-instance)
    /// </summary>
    public readonly int InstanceDataStepRate;

    public InputElementDesc(
        string semanticName,
        int semanticIndex,
        VertexFormat format,
        int inputSlot,
        int alignedByteOffset,
        InputClassification classification = InputClassification.PerVertex,
        int instanceDataStepRate = 0)
    {
        SemanticName = semanticName;
        SemanticIndex = semanticIndex;
        Format = format;
        InputSlot = inputSlot;
        AlignedByteOffset = alignedByteOffset;
        Classification = classification;
        InstanceDataStepRate = instanceDataStepRate;
    }
}

/// <summary>
/// 输入布局定义
/// </summary>
public readonly struct InputLayoutDesc
{
    public readonly InputElementDesc[] Elements;

    public InputLayoutDesc(InputElementDesc[] elements)
    {
        Elements = elements;
    }
}

/// <summary>
/// 顶点格式
/// </summary>
public enum VertexFormat : byte
{
    Float,
    Float2,
    Float3,
    Float4,
    UInt,
    UInt2,
    UInt4,
    Int,
    Int2,
    Int4,
    Half2,
    Half4
}

/// <summary>
/// 输入分类
/// </summary>
public enum InputClassification : byte
{
    PerVertex,
    PerInstance
}

/// <summary>
/// 预定义输入布局 - 匹配现有 UIRuntime 数据格式
/// </summary>
public static class UIInputLayouts
{
    /// <summary>
    /// 矩形实例化布局
    /// Slot 0: 顶点 {float2 pos, float2 uv} = 16B (匹配 UIRuntime.RectVertices)
    /// Slot 1: 实例 80B (匹配 UIRuntime.GenerateInstanceData)
    /// </summary>
    public static InputLayoutDesc RectInstanced => new([
        // Slot 0: Per-vertex
        new("POSITION", 0, VertexFormat.Float2, 0, 0),
        new("TEXCOORD", 0, VertexFormat.Float2, 0, 8),

        // Slot 1: Per-instance (80B stride)
        new("INST_POSITION",     0, VertexFormat.Float2, 1, 0,  InputClassification.PerInstance, 1),
        new("INST_SIZE",         0, VertexFormat.Float2, 1, 8,  InputClassification.PerInstance, 1),
        new("INST_UV",           0, VertexFormat.Float4, 1, 16, InputClassification.PerInstance, 1),
        new("INST_COLOR",        0, VertexFormat.UInt,   1, 32, InputClassification.PerInstance, 1),
        new("INST_CORNER_RADIUS",0, VertexFormat.Float4, 1, 36, InputClassification.PerInstance, 1),
        new("INST_BORDER_THICK", 0, VertexFormat.Float4, 1, 52, InputClassification.PerInstance, 1),
        new("INST_BORDER_COLOR", 0, VertexFormat.UInt,   1, 68, InputClassification.PerInstance, 1),
        new("INST_PADDING",      0, VertexFormat.Float2, 1, 72, InputClassification.PerInstance, 1),
    ]);

    /// <summary>
    /// 文本实例化布局
    /// Slot 0: 顶点 {float2 pos, float2 uv}
    /// Slot 1: 字形实例 {float2 pos, float2 size, float4 uv, uint color}
    /// </summary>
    public static InputLayoutDesc TextInstanced => new([
        // Slot 0: Per-vertex
        new("POSITION", 0, VertexFormat.Float2, 0, 0),
        new("TEXCOORD", 0, VertexFormat.Float2, 0, 8),

        // Slot 1: Per-glyph instance (28B stride)
        new("GLYPH_POS",   0, VertexFormat.Float2, 1, 0,  InputClassification.PerInstance, 1),
        new("GLYPH_SIZE",  0, VertexFormat.Float2, 1, 8,  InputClassification.PerInstance, 1),
        new("GLYPH_UV",    0, VertexFormat.Float4, 1, 16, InputClassification.PerInstance, 1),
        new("GLYPH_COLOR", 0, VertexFormat.UInt,   1, 32, InputClassification.PerInstance, 1),
    ]);

    /// <summary>
    /// 图像实例化布局
    /// </summary>
    public static InputLayoutDesc ImageInstanced => new([
        // Slot 0: Per-vertex
        new("POSITION", 0, VertexFormat.Float2, 0, 0),
        new("TEXCOORD", 0, VertexFormat.Float2, 0, 8),

        // Slot 1: Per-image instance
        new("INST_POSITION",  0, VertexFormat.Float2, 1, 0,  InputClassification.PerInstance, 1),
        new("INST_SIZE",      0, VertexFormat.Float2, 1, 8,  InputClassification.PerInstance, 1),
        new("INST_UV",        0, VertexFormat.Float4, 1, 16, InputClassification.PerInstance, 1),
        new("INST_COLOR",     0, VertexFormat.UInt,   1, 32, InputClassification.PerInstance, 1),
        new("INST_NINESLICE", 0, VertexFormat.Float4, 1, 36, InputClassification.PerInstance, 1),
    ]);

    /// <summary>
    /// 路径直接布局（无实例化）
    /// Slot 0: 顶点 {float2 pos, float2 uv}
    /// </summary>
    public static InputLayoutDesc PathDirect => new([
        new("POSITION", 0, VertexFormat.Float2, 0, 0),
        new("TEXCOORD", 0, VertexFormat.Float2, 0, 8),
    ]);

    /// <summary>
    /// 获取指定布局类型的描述
    /// </summary>
    public static InputLayoutDesc GetLayout(InputLayoutType type) => type switch
    {
        InputLayoutType.RectInstanced => RectInstanced,
        InputLayoutType.TextInstanced => TextInstanced,
        InputLayoutType.ImageInstanced => ImageInstanced,
        InputLayoutType.PathDirect => PathDirect,
        InputLayoutType.None => new([]),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// 获取指定布局类型的顶点步进字节数
    /// </summary>
    public static int GetVertexStride(InputLayoutType type) => type switch
    {
        InputLayoutType.RectInstanced => 16,
        InputLayoutType.TextInstanced => 16,
        InputLayoutType.ImageInstanced => 16,
        InputLayoutType.PathDirect => 16,
        InputLayoutType.None => 0,
        _ => 0
    };

    /// <summary>
    /// 获取指定布局类型的实例步进字节数
    /// </summary>
    public static int GetInstanceStride(InputLayoutType type) => type switch
    {
        InputLayoutType.RectInstanced => 80,
        InputLayoutType.TextInstanced => 36,
        InputLayoutType.ImageInstanced => 52,
        InputLayoutType.PathDirect => 0,
        InputLayoutType.None => 0,
        _ => 0
    };
}
