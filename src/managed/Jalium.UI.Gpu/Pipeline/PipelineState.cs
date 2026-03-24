using Jalium.UI.Gpu.Shaders;

namespace Jalium.UI.Gpu.Pipeline;

/// <summary>
/// GPU 管线状态描述符 - 完整描述一个 PSO 的配置
/// </summary>
public readonly struct PipelineStateDesc : IEquatable<PipelineStateDesc>
{
    /// <summary>
    /// 顶点着色器类型
    /// </summary>
    public readonly ShaderType VertexShader;

    /// <summary>
    /// 像素着色器类型（Compute PSO 时为 None）
    /// </summary>
    public readonly ShaderType PixelShader;

    /// <summary>
    /// 输入布局
    /// </summary>
    public readonly InputLayoutType InputLayout;

    /// <summary>
    /// 混合状态
    /// </summary>
    public readonly BlendStateDesc BlendState;

    /// <summary>
    /// 光栅化状态
    /// </summary>
    public readonly RasterizerStateDesc RasterizerState;

    /// <summary>
    /// 深度模板状态
    /// </summary>
    public readonly DepthStencilStateDesc DepthStencilState;

    /// <summary>
    /// 渲染目标格式
    /// </summary>
    public readonly RenderTargetFormat RtFormat;

    /// <summary>
    /// 采样数（MSAA）
    /// </summary>
    public readonly byte SampleCount;

    /// <summary>
    /// 根签名类型
    /// </summary>
    public readonly RootSignatureType RootSignature;

    /// <summary>
    /// 是否为 Compute PSO
    /// </summary>
    public readonly bool IsCompute;

    /// <summary>
    /// Compute 着色器类型
    /// </summary>
    public readonly ShaderType ComputeShader;

    public PipelineStateDesc(
        ShaderType vertexShader,
        ShaderType pixelShader,
        InputLayoutType inputLayout,
        BlendStateDesc blendState,
        RasterizerStateDesc rasterizerState,
        DepthStencilStateDesc depthStencilState,
        RenderTargetFormat rtFormat = RenderTargetFormat.R8G8B8A8_UNorm,
        byte sampleCount = 1,
        RootSignatureType rootSignature = RootSignatureType.Standard)
    {
        VertexShader = vertexShader;
        PixelShader = pixelShader;
        InputLayout = inputLayout;
        BlendState = blendState;
        RasterizerState = rasterizerState;
        DepthStencilState = depthStencilState;
        RtFormat = rtFormat;
        SampleCount = sampleCount;
        RootSignature = rootSignature;
        IsCompute = false;
        ComputeShader = default;
    }

    /// <summary>
    /// 创建 Compute PSO 描述符
    /// </summary>
    public static PipelineStateDesc CreateCompute(
        ShaderType computeShader,
        RootSignatureType rootSignature = RootSignatureType.Compute)
    {
        return new PipelineStateDesc(
            isCompute: true,
            computeShader: computeShader,
            rootSignature: rootSignature);
    }

    // Private constructor for Compute PSO
    private PipelineStateDesc(
        bool isCompute,
        ShaderType computeShader,
        RootSignatureType rootSignature)
    {
        IsCompute = isCompute;
        ComputeShader = computeShader;
        RootSignature = rootSignature;
        VertexShader = default;
        PixelShader = default;
        InputLayout = default;
        BlendState = default;
        RasterizerState = default;
        DepthStencilState = default;
        RtFormat = default;
        SampleCount = 1;
    }

    public ulong GetHash()
    {
        ulong hash = 14695981039346656037UL; // FNV offset basis
        const ulong prime = 1099511628211UL;

        hash = (hash ^ (ulong)VertexShader) * prime;
        hash = (hash ^ (ulong)PixelShader) * prime;
        hash = (hash ^ (ulong)InputLayout) * prime;
        hash = (hash ^ (ulong)BlendState.Mode) * prime;
        hash = (hash ^ (ulong)(BlendState.AlphaToCoverage ? 1 : 0)) * prime;
        hash = (hash ^ (ulong)RasterizerState.CullMode) * prime;
        hash = (hash ^ (ulong)RasterizerState.FillMode) * prime;
        hash = (hash ^ (ulong)(RasterizerState.ScissorEnable ? 1 : 0)) * prime;
        hash = (hash ^ (ulong)(RasterizerState.MultisampleEnable ? 1 : 0)) * prime;
        hash = (hash ^ (ulong)(DepthStencilState.EnableDepth ? 1 : 0)) * prime;
        hash = (hash ^ (ulong)RtFormat) * prime;
        hash = (hash ^ SampleCount) * prime;
        hash = (hash ^ (ulong)RootSignature) * prime;
        hash = (hash ^ (ulong)(IsCompute ? 1 : 0)) * prime;
        hash = (hash ^ (ulong)ComputeShader) * prime;

        return hash;
    }

    public bool Equals(PipelineStateDesc other) => GetHash() == other.GetHash();
    public override bool Equals(object? obj) => obj is PipelineStateDesc other && Equals(other);
    public override int GetHashCode() => (int)GetHash();
}

/// <summary>
/// 混合状态描述符
/// </summary>
public readonly struct BlendStateDesc
{
    public readonly BlendMode Mode;
    public readonly bool AlphaToCoverage;

    public BlendStateDesc(BlendMode mode, bool alphaToCoverage = false)
    {
        Mode = mode;
        AlphaToCoverage = alphaToCoverage;
    }

    /// <summary>
    /// 无混合（不透明）
    /// </summary>
    public static BlendStateDesc Opaque => new(BlendMode.Normal, false);

    /// <summary>
    /// Alpha 混合（透明）
    /// </summary>
    public static BlendStateDesc AlphaBlend => new(BlendMode.Normal, false);

    /// <summary>
    /// 预乘 Alpha 混合
    /// </summary>
    public static BlendStateDesc PremultipliedAlpha => new(BlendMode.Normal, false);
}

/// <summary>
/// 光栅化状态描述符
/// </summary>
public readonly struct RasterizerStateDesc
{
    public readonly CullMode CullMode;
    public readonly FillMode FillMode;
    public readonly bool ScissorEnable;
    public readonly bool MultisampleEnable;

    public RasterizerStateDesc(
        CullMode cullMode = CullMode.None,
        FillMode fillMode = FillMode.Solid,
        bool scissorEnable = true,
        bool multisampleEnable = false)
    {
        CullMode = cullMode;
        FillMode = fillMode;
        ScissorEnable = scissorEnable;
        MultisampleEnable = multisampleEnable;
    }

    public static RasterizerStateDesc Default => new(CullMode.None, FillMode.Solid, true);
    public static RasterizerStateDesc BackCull => new(CullMode.Back, FillMode.Solid, true);
}

/// <summary>
/// 深度模板状态描述符
/// </summary>
public readonly struct DepthStencilStateDesc
{
    public readonly bool EnableDepth;
    public readonly bool DepthWrite;
    public readonly ComparisonFunc DepthFunc;

    public DepthStencilStateDesc(
        bool enableDepth = false,
        bool depthWrite = false,
        ComparisonFunc depthFunc = ComparisonFunc.LessEqual)
    {
        EnableDepth = enableDepth;
        DepthWrite = depthWrite;
        DepthFunc = depthFunc;
    }

    public static DepthStencilStateDesc Disabled => new(false, false);
    public static DepthStencilStateDesc DepthReadWrite => new(true, true, ComparisonFunc.LessEqual);
    public static DepthStencilStateDesc DepthReadOnly => new(true, false, ComparisonFunc.LessEqual);
}

#region Enums

public enum CullMode : byte
{
    None,
    Front,
    Back
}

public enum FillMode : byte
{
    Solid,
    Wireframe
}

public enum ComparisonFunc : byte
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always
}

public enum RenderTargetFormat : byte
{
    R8G8B8A8_UNorm,
    B8G8R8A8_UNorm,
    R16G16B16A16_Float,
    R32G32B32A32_Float,
    R8_UNorm,
    R32_Float
}

public enum InputLayoutType : byte
{
    /// <summary>
    /// 矩形：顶点 (pos+uv) + 实例 (80B)
    /// </summary>
    RectInstanced,

    /// <summary>
    /// 文本：顶点 (pos+uv) + 字形实例
    /// </summary>
    TextInstanced,

    /// <summary>
    /// 图像：顶点 (pos+uv) + 图像实例
    /// </summary>
    ImageInstanced,

    /// <summary>
    /// 路径：顶点 (pos+uv) 无实例
    /// </summary>
    PathDirect,

    /// <summary>
    /// 无输入（全屏三角形，由 SV_VertexID 生成）
    /// </summary>
    None
}

public enum RootSignatureType : byte
{
    /// <summary>
    /// 标准图形根签名：FrameCB + MaterialSB + GradientSB + Texture + Sampler
    /// </summary>
    Standard,

    /// <summary>
    /// 路径根签名：FrameCB + PathCB + MaterialSB + Sampler
    /// </summary>
    Path,

    /// <summary>
    /// Compute 根签名：CB + SRV + UAV
    /// </summary>
    Compute,

    /// <summary>
    /// 合成根签名：CB + SRV + Sampler
    /// </summary>
    Composite
}

#endregion

/// <summary>
/// 预定义 UI 管线配置
/// </summary>
public static class UIPipelines
{
    /// <summary>
    /// 不透明矩形 - 无混合，前→后渲染
    /// </summary>
    public static PipelineStateDesc OpaqueRect => new(
        vertexShader: ShaderType.UIRectVS,
        pixelShader: ShaderType.UIRectPS,
        inputLayout: InputLayoutType.RectInstanced,
        blendState: BlendStateDesc.Opaque,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled);

    /// <summary>
    /// 透明矩形 - Alpha 混合，后→前渲染
    /// </summary>
    public static PipelineStateDesc TransparentRect => new(
        vertexShader: ShaderType.UIRectVS,
        pixelShader: ShaderType.UIRectPS,
        inputLayout: InputLayoutType.RectInstanced,
        blendState: BlendStateDesc.AlphaBlend,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled);

    /// <summary>
    /// 文本渲染
    /// </summary>
    public static PipelineStateDesc Text => new(
        vertexShader: ShaderType.TextVS,
        pixelShader: ShaderType.TextPS,
        inputLayout: InputLayoutType.TextInstanced,
        blendState: BlendStateDesc.PremultipliedAlpha,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled);

    /// <summary>
    /// 图像渲染
    /// </summary>
    public static PipelineStateDesc Image => new(
        vertexShader: ShaderType.ImageVS,
        pixelShader: ShaderType.ImagePS,
        inputLayout: InputLayoutType.ImageInstanced,
        blendState: BlendStateDesc.AlphaBlend,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled);

    /// <summary>
    /// 路径渲染
    /// </summary>
    public static PipelineStateDesc Path => new(
        vertexShader: ShaderType.PathVS,
        pixelShader: ShaderType.PathPS,
        inputLayout: InputLayoutType.PathDirect,
        blendState: BlendStateDesc.AlphaBlend,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled,
        rootSignature: RootSignatureType.Path);

    /// <summary>
    /// 水平高斯模糊 Compute
    /// </summary>
    public static PipelineStateDesc GaussianBlurH =>
        PipelineStateDesc.CreateCompute(ShaderType.GaussianBlurHorizontalCS);

    /// <summary>
    /// 垂直高斯模糊 Compute
    /// </summary>
    public static PipelineStateDesc GaussianBlurV =>
        PipelineStateDesc.CreateCompute(ShaderType.GaussianBlurVerticalCS);

    /// <summary>
    /// Backdrop Filter Compute
    /// </summary>
    public static PipelineStateDesc BackdropFilter =>
        PipelineStateDesc.CreateCompute(ShaderType.BackdropFilterCS);

    /// <summary>
    /// 图层合成
    /// </summary>
    public static PipelineStateDesc Composite => new(
        vertexShader: ShaderType.CompositeVS,
        pixelShader: ShaderType.CompositePS,
        inputLayout: InputLayoutType.None,
        blendState: BlendStateDesc.AlphaBlend,
        rasterizerState: RasterizerStateDesc.Default,
        depthStencilState: DepthStencilStateDesc.Disabled,
        rootSignature: RootSignatureType.Composite);
}
