namespace Jalium.UI.Gpu;

/// <summary>
/// GPU Render Intermediate Representation (IR)
/// UI 编译后的中间表示，类似于 Shader 管线的概念
/// </summary>
public static class RenderIR
{
    // 版本号，用于验证编译产物兼容性
    public const int Version = 3;
}

#region Scene Graph Nodes

/// <summary>
/// 场景节点基类 - 轻量级描述符，不是控件对象
/// </summary>
public abstract class SceneNode
{
    /// <summary>
    /// 节点唯一标识
    /// </summary>
    public uint Id { get; init; }

    /// <summary>
    /// 父节点 ID（0 表示根节点）
    /// </summary>
    public uint ParentId { get; init; }

    /// <summary>
    /// 变换矩阵索引（指向变换缓冲区）
    /// </summary>
    public uint TransformIndex { get; init; }

    /// <summary>
    /// 材质索引（指向材质缓冲区）
    /// </summary>
    public uint MaterialIndex { get; init; }

    /// <summary>
    /// 裁剪区域索引（0 表示无裁剪）
    /// </summary>
    public uint ClipIndex { get; init; }

    /// <summary>
    /// 可见性标志
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// 层级（用于排序和批处理）
    /// </summary>
    public int ZIndex { get; init; }
}

/// <summary>
/// 矩形节点 - 最常见的 UI 元素
/// </summary>
public sealed class RectNode : SceneNode
{
    /// <summary>
    /// 边界（相对于父节点）
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// 圆角半径（4个角）
    /// </summary>
    public CornerRadius CornerRadius { get; init; }

    /// <summary>
    /// 边框宽度
    /// </summary>
    public Thickness BorderThickness { get; init; }
}

/// <summary>
/// 文本节点
/// </summary>
public sealed class TextNode : SceneNode
{
    /// <summary>
    /// 文本内容的哈希（用于缓存查找）
    /// </summary>
    public ulong TextHash { get; init; }

    /// <summary>
    /// 字形图集索引
    /// </summary>
    public uint GlyphAtlasIndex { get; init; }

    /// <summary>
    /// 字形运行索引（指向预计算的字形布局）
    /// </summary>
    public uint GlyphRunIndex { get; init; }

    /// <summary>
    /// 文本边界
    /// </summary>
    public Rect Bounds { get; set; }
}

/// <summary>
/// 图像节点
/// </summary>
public sealed class ImageNode : SceneNode
{
    /// <summary>
    /// 纹理索引
    /// </summary>
    public uint TextureIndex { get; init; }

    /// <summary>
    /// UV 坐标（用于图集采样）
    /// </summary>
    public Rect UVRect { get; init; }

    /// <summary>
    /// 目标边界
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// 九宫格切片边距（0 表示无切片）
    /// </summary>
    public Thickness NineSlice { get; init; }
}

/// <summary>
/// 路径节点（矢量图形）
/// </summary>
public sealed class PathNode : SceneNode
{
    /// <summary>
    /// 路径缓存索引
    /// </summary>
    public uint PathCacheIndex { get; init; }

    /// <summary>
    /// 边界
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// 填充规则
    /// </summary>
    public FillRule FillRule { get; init; }
}

/// <summary>
/// 效果节点（模糊、阴影等）
/// </summary>
public sealed class EffectNode : SceneNode
{
    /// <summary>
    /// 效果类型
    /// </summary>
    public EffectType EffectType { get; init; }

    /// <summary>
    /// 效果参数索引
    /// </summary>
    public uint EffectParamsIndex { get; init; }

    /// <summary>
    /// 目标节点 ID（应用效果的节点）
    /// </summary>
    public uint TargetNodeId { get; init; }
}

/// <summary>
/// Backdrop Filter 节点 - 用于 CSS backdrop-filter 风格的效果
/// </summary>
public sealed class BackdropFilterNode : SceneNode
{
    /// <summary>
    /// 滤镜参数
    /// </summary>
    public BackdropFilterParams Params { get; init; }

    /// <summary>
    /// 滤镜应用区域（相对于父节点）
    /// </summary>
    public Rect FilterRegion { get; init; }

    /// <summary>
    /// 目标节点 ID（如果指定，只对该节点应用滤镜）
    /// </summary>
    public uint TargetNodeId { get; init; }

    /// <summary>
    /// 是否继承父节点变换
    /// </summary>
    public bool InheritTransform { get; init; } = true;

    /// <summary>
    /// 圆角半径（用于滤镜区域裁剪）
    /// </summary>
    public CornerRadius CornerRadius { get; init; }

    /// <summary>
    /// 滤镜参数索引（用于序列化）
    /// </summary>
    public uint ParamsIndex { get; init; }
}

/// <summary>
/// 填充规则
/// </summary>
public enum FillRule
{
    EvenOdd,
    NonZero
}

/// <summary>
/// 效果类型
/// </summary>
public enum EffectType
{
    None,
    BoxShadow,
    DropShadow,
    GaussianBlur,
    BackdropBlur,
    ColorMatrix,

    // Backdrop Filter 系列
    BackdropBrightness,      // 亮度调整
    BackdropContrast,        // 对比度调整
    BackdropSaturate,        // 饱和度调整
    BackdropGrayscale,       // 灰度转换
    BackdropSepia,           // 褐色调
    BackdropInvert,          // 颜色反转
    BackdropHueRotate,       // 色相旋转
    BackdropOpacity,         // 不透明度

    // 复合材质效果
    BackdropFrostedGlass,    // 毛玻璃效果
    BackdropMica,            // 云母材质（Windows 11）
    BackdropMicaAlt,         // 云母材质变体
    BackdropAcrylic,         // 亚克力效果（Windows 10/11）

    // 组合滤镜链
    BackdropFilterChain,     // 多个滤镜组合

    // 元素效果系列（追加在末尾，不破坏已有值）
    OuterGlow,               // 外发光效果
    InnerShadow,             // 内阴影效果
    Emboss,                  // 浮雕效果
    Shader,                  // 自定义着色器效果
    ElementEffectGroup,      // 效果组合
}

/// <summary>
/// 模糊类型
/// </summary>
public enum BlurType : byte
{
    /// <summary>
    /// 标准高斯模糊
    /// </summary>
    Gaussian,

    /// <summary>
    /// 盒式模糊（性能更好）
    /// </summary>
    Box,

    /// <summary>
    /// 磨砂模糊（带噪声）
    /// </summary>
    Frosted,

    /// <summary>
    /// 方向模糊
    /// </summary>
    Directional,

    /// <summary>
    /// 径向模糊
    /// </summary>
    Radial,

    /// <summary>
    /// 缩放模糊
    /// </summary>
    Zoom
}

/// <summary>
/// 材质类型
/// </summary>
public enum MaterialType : byte
{
    /// <summary>
    /// 无特殊材质
    /// </summary>
    None,

    /// <summary>
    /// Windows Acrylic 亚克力效果
    /// </summary>
    Acrylic,

    /// <summary>
    /// Windows 11 Mica 云母效果
    /// </summary>
    Mica,

    /// <summary>
    /// Windows 11 Mica Alt 云母变体
    /// </summary>
    MicaAlt,

    /// <summary>
    /// 毛玻璃效果
    /// </summary>
    FrostedGlass,

    /// <summary>
    /// 自定义材质
    /// </summary>
    Custom
}

/// <summary>
/// Backdrop Filter 参数 - 64 字节对齐，GPU 友好
/// </summary>
public readonly struct BackdropFilterParams
{
    // ===== 模糊参数 (16 bytes) =====
    /// <summary>
    /// 模糊半径（像素）
    /// </summary>
    public readonly float BlurRadius;

    /// <summary>
    /// 高斯模糊 Sigma 值
    /// </summary>
    public readonly float BlurSigma;

    /// <summary>
    /// 模糊类型
    /// </summary>
    public readonly BlurType BlurType;

    /// <summary>
    /// 保留字段（对齐）
    /// </summary>
    public readonly byte Reserved1;

    /// <summary>
    /// 保留字段（对齐）
    /// </summary>
    public readonly ushort Reserved2;

    /// <summary>
    /// 噪声强度（用于磨砂效果）
    /// </summary>
    public readonly float NoiseIntensity;

    // ===== 颜色调整参数 (16 bytes) =====
    /// <summary>
    /// 亮度系数（1.0 = 原始）
    /// </summary>
    public readonly float Brightness;

    /// <summary>
    /// 对比度系数（1.0 = 原始）
    /// </summary>
    public readonly float Contrast;

    /// <summary>
    /// 饱和度系数（1.0 = 原始，0 = 灰度）
    /// </summary>
    public readonly float Saturation;

    /// <summary>
    /// 色相旋转角度（弧度）
    /// </summary>
    public readonly float HueRotation;

    // ===== 色彩变换参数 (16 bytes) =====
    /// <summary>
    /// 灰度程度（0-1）
    /// </summary>
    public readonly float Grayscale;

    /// <summary>
    /// 褐色程度（0-1）
    /// </summary>
    public readonly float Sepia;

    /// <summary>
    /// 反转程度（0-1）
    /// </summary>
    public readonly float Invert;

    /// <summary>
    /// 不透明度（0-1）
    /// </summary>
    public readonly float Opacity;

    // ===== 材质参数 (16 bytes) =====
    /// <summary>
    /// 色调颜色（ARGB）
    /// </summary>
    public readonly uint TintColor;

    /// <summary>
    /// 色调不透明度（0-1）
    /// </summary>
    public readonly float TintOpacity;

    /// <summary>
    /// 光度/亮度调整
    /// </summary>
    public readonly float Luminosity;

    /// <summary>
    /// 材质类型
    /// </summary>
    public readonly MaterialType MaterialType;

    /// <summary>
    /// 保留字段（对齐到 64 bytes）
    /// </summary>
    public readonly byte Reserved3;

    /// <summary>
    /// 保留字段
    /// </summary>
    public readonly ushort Reserved4;

    /// <summary>
    /// 创建默认参数（无效果）
    /// </summary>
    public static BackdropFilterParams Default => new(
        blurRadius: 0,
        brightness: 1.0f,
        contrast: 1.0f,
        saturation: 1.0f,
        opacity: 1.0f
    );

    /// <summary>
    /// 构造函数
    /// </summary>
    public BackdropFilterParams(
        float blurRadius = 0,
        float blurSigma = 0,
        BlurType blurType = BlurType.Gaussian,
        float noiseIntensity = 0,
        float brightness = 1.0f,
        float contrast = 1.0f,
        float saturation = 1.0f,
        float hueRotation = 0,
        float grayscale = 0,
        float sepia = 0,
        float invert = 0,
        float opacity = 1.0f,
        uint tintColor = 0,
        float tintOpacity = 0,
        float luminosity = 1.0f,
        MaterialType materialType = MaterialType.None)
    {
        BlurRadius = blurRadius;
        BlurSigma = blurSigma > 0 ? blurSigma : blurRadius / 3.0f;
        BlurType = blurType;
        Reserved1 = 0;
        Reserved2 = 0;
        NoiseIntensity = noiseIntensity;

        Brightness = brightness;
        Contrast = contrast;
        Saturation = saturation;
        HueRotation = hueRotation;

        Grayscale = grayscale;
        Sepia = sepia;
        Invert = invert;
        Opacity = opacity;

        TintColor = tintColor;
        TintOpacity = tintOpacity;
        Luminosity = luminosity;
        MaterialType = materialType;
        Reserved3 = 0;
        Reserved4 = 0;
    }

    /// <summary>
    /// 创建高斯模糊参数
    /// </summary>
    public static BackdropFilterParams CreateBlur(float radius, BlurType type = BlurType.Gaussian)
        => new(blurRadius: radius, blurType: type);

    /// <summary>
    /// 创建亮度调整参数
    /// </summary>
    public static BackdropFilterParams CreateBrightness(float factor)
        => new(brightness: factor);

    /// <summary>
    /// 创建对比度调整参数
    /// </summary>
    public static BackdropFilterParams CreateContrast(float factor)
        => new(contrast: factor);

    /// <summary>
    /// 创建饱和度调整参数
    /// </summary>
    public static BackdropFilterParams CreateSaturate(float factor)
        => new(saturation: factor);

    /// <summary>
    /// 创建灰度效果参数
    /// </summary>
    public static BackdropFilterParams CreateGrayscale(float amount = 1.0f)
        => new(grayscale: amount);

    /// <summary>
    /// 创建褐色效果参数
    /// </summary>
    public static BackdropFilterParams CreateSepia(float amount = 1.0f)
        => new(sepia: amount);

    /// <summary>
    /// 创建反转效果参数
    /// </summary>
    public static BackdropFilterParams CreateInvert(float amount = 1.0f)
        => new(invert: amount);

    /// <summary>
    /// 创建色相旋转参数
    /// </summary>
    public static BackdropFilterParams CreateHueRotate(float degrees)
        => new(hueRotation: degrees * MathF.PI / 180.0f);

    /// <summary>
    /// 创建不透明度参数
    /// </summary>
    public static BackdropFilterParams CreateOpacity(float amount)
        => new(opacity: amount);

    /// <summary>
    /// 创建毛玻璃效果参数
    /// </summary>
    public static BackdropFilterParams CreateFrostedGlass(
        float blurRadius = 20,
        float noiseIntensity = 0.03f,
        uint tintColor = 0x80FFFFFF,
        float tintOpacity = 0.5f,
        float saturation = 1.2f)
        => new(
            blurRadius: blurRadius,
            blurType: BlurType.Frosted,
            noiseIntensity: noiseIntensity,
            saturation: saturation,
            tintColor: tintColor,
            tintOpacity: tintOpacity,
            materialType: MaterialType.FrostedGlass);

    /// <summary>
    /// 创建 Windows Acrylic 亚克力效果参数
    /// </summary>
    public static BackdropFilterParams CreateAcrylic(
        uint tintColor = 0x99FFFFFF,
        float tintOpacity = 0.6f,
        float blurRadius = 30,
        float noiseIntensity = 0.02f)
        => new(
            blurRadius: blurRadius,
            blurType: BlurType.Gaussian,
            noiseIntensity: noiseIntensity,
            saturation: 1.25f,
            tintColor: tintColor,
            tintOpacity: tintOpacity,
            luminosity: 1.0f,
            materialType: MaterialType.Acrylic);

    /// <summary>
    /// 创建 Windows 11 Mica 云母效果参数
    /// </summary>
    public static BackdropFilterParams CreateMica(bool isAlt = false)
        => new(
            blurRadius: 0,
            saturation: isAlt ? 0.0f : 1.0f,
            tintColor: isAlt ? 0xB3FFFFFF : 0x80FFFFFF,
            tintOpacity: isAlt ? 0.8f : 0.6f,
            luminosity: 1.0f,
            materialType: isAlt ? MaterialType.MicaAlt : MaterialType.Mica);

    /// <summary>
    /// 合并两个滤镜参数（用于滤镜链）
    /// </summary>
    public BackdropFilterParams Combine(BackdropFilterParams other)
        => new(
            blurRadius: BlurRadius > 0 ? BlurRadius : other.BlurRadius,
            blurSigma: BlurSigma > 0 ? BlurSigma : other.BlurSigma,
            blurType: BlurRadius > 0 ? BlurType : other.BlurType,
            noiseIntensity: NoiseIntensity > 0 ? NoiseIntensity : other.NoiseIntensity,
            brightness: Brightness != 1.0f ? Brightness : other.Brightness,
            contrast: Contrast != 1.0f ? Contrast : other.Contrast,
            saturation: Saturation != 1.0f ? Saturation : other.Saturation,
            hueRotation: HueRotation != 0 ? HueRotation : other.HueRotation,
            grayscale: Grayscale > 0 ? Grayscale : other.Grayscale,
            sepia: Sepia > 0 ? Sepia : other.Sepia,
            invert: Invert > 0 ? Invert : other.Invert,
            opacity: Opacity != 1.0f ? Opacity : other.Opacity,
            tintColor: TintColor != 0 ? TintColor : other.TintColor,
            tintOpacity: TintOpacity > 0 ? TintOpacity : other.TintOpacity,
            luminosity: Luminosity != 1.0f ? Luminosity : other.Luminosity,
            materialType: MaterialType != MaterialType.None ? MaterialType : other.MaterialType);

    /// <summary>
    /// 是否有任何效果
    /// </summary>
    public bool HasEffect =>
        BlurRadius > 0 ||
        Brightness != 1.0f ||
        Contrast != 1.0f ||
        Saturation != 1.0f ||
        HueRotation != 0 ||
        Grayscale > 0 ||
        Sepia > 0 ||
        Invert > 0 ||
        Opacity != 1.0f ||
        TintOpacity > 0 ||
        MaterialType != MaterialType.None;

    /// <summary>
    /// 是否需要模糊
    /// </summary>
    public bool NeedsBlur => BlurRadius > 0;

    /// <summary>
    /// 是否需要颜色调整
    /// </summary>
    public bool NeedsColorAdjustment =>
        Brightness != 1.0f ||
        Contrast != 1.0f ||
        Saturation != 1.0f ||
        HueRotation != 0 ||
        Grayscale > 0 ||
        Sepia > 0 ||
        Invert > 0;
}

#endregion

#region Materials (编译后的样式)

/// <summary>
/// 材质 - 编译后的样式，GPU 友好
/// </summary>
public readonly struct Material
{
    /// <summary>
    /// 背景色（预乘 alpha）
    /// </summary>
    public readonly uint BackgroundColor;

    /// <summary>
    /// 边框色（预乘 alpha）
    /// </summary>
    public readonly uint BorderColor;

    /// <summary>
    /// 前景色/文本色（预乘 alpha）
    /// </summary>
    public readonly uint ForegroundColor;

    /// <summary>
    /// 渐变索引（0 表示纯色）
    /// </summary>
    public readonly uint GradientIndex;

    /// <summary>
    /// 不透明度（0-255）
    /// </summary>
    public readonly byte Opacity;

    /// <summary>
    /// 混合模式
    /// </summary>
    public readonly BlendMode BlendMode;

    public Material(uint background, uint border, uint foreground, uint gradient = 0, byte opacity = 255, BlendMode blend = BlendMode.Normal)
    {
        BackgroundColor = background;
        BorderColor = border;
        ForegroundColor = foreground;
        GradientIndex = gradient;
        Opacity = opacity;
        BlendMode = blend;
    }
}

/// <summary>
/// 混合模式
/// </summary>
public enum BlendMode : byte
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    SoftLight,
    HardLight,
    Difference,
    Exclusion
}

/// <summary>
/// 渐变定义
/// </summary>
public readonly struct GradientDef
{
    /// <summary>
    /// 渐变类型
    /// </summary>
    public readonly GradientType Type;

    /// <summary>
    /// 起点（线性渐变）或中心点（径向渐变）
    /// </summary>
    public readonly Point Start;

    /// <summary>
    /// 终点（线性渐变）或边缘点（径向渐变）
    /// </summary>
    public readonly Point End;

    /// <summary>
    /// 渐变停止点数组起始索引
    /// </summary>
    public readonly uint StopsIndex;

    /// <summary>
    /// 渐变停止点数量
    /// </summary>
    public readonly byte StopsCount;

    public GradientDef(GradientType type, Point start, Point end, uint stopsIndex, byte stopsCount)
    {
        Type = type;
        Start = start;
        End = end;
        StopsIndex = stopsIndex;
        StopsCount = stopsCount;
    }
}

/// <summary>
/// 渐变类型
/// </summary>
public enum GradientType : byte
{
    Linear,
    Radial,
    Conic
}

/// <summary>
/// 渐变停止点
/// </summary>
public readonly struct GradientStop
{
    public readonly float Offset;
    public readonly uint Color;

    public GradientStop(float offset, uint color)
    {
        Offset = offset;
        Color = color;
    }
}

#endregion

#region Animations (时间函数)

/// <summary>
/// 动画曲线 - 编译后的时间函数
/// </summary>
public readonly struct AnimationCurve
{
    /// <summary>
    /// 缓动函数类型
    /// </summary>
    public readonly EasingType Easing;

    /// <summary>
    /// 自定义贝塞尔控制点（如果 Easing == Custom）
    /// </summary>
    public readonly float P1X, P1Y, P2X, P2Y;

    /// <summary>
    /// 持续时间（毫秒）
    /// </summary>
    public readonly uint DurationMs;

    /// <summary>
    /// 延迟（毫秒）
    /// </summary>
    public readonly uint DelayMs;

    /// <summary>
    /// 重复次数（0 = 无限）
    /// </summary>
    public readonly byte RepeatCount;

    /// <summary>
    /// 是否反向
    /// </summary>
    public readonly bool AutoReverse;

    public AnimationCurve(EasingType easing, uint durationMs, uint delayMs = 0, byte repeatCount = 1, bool autoReverse = false)
    {
        Easing = easing;
        DurationMs = durationMs;
        DelayMs = delayMs;
        RepeatCount = repeatCount;
        AutoReverse = autoReverse;
        P1X = P1Y = P2X = P2Y = 0;
    }

    public AnimationCurve(float p1x, float p1y, float p2x, float p2y, uint durationMs, uint delayMs = 0, byte repeatCount = 1, bool autoReverse = false)
    {
        Easing = EasingType.CubicBezier;
        P1X = p1x;
        P1Y = p1y;
        P2X = p2x;
        P2Y = p2y;
        DurationMs = durationMs;
        DelayMs = delayMs;
        RepeatCount = repeatCount;
        AutoReverse = autoReverse;
    }
}

/// <summary>
/// 缓动函数类型
/// </summary>
public enum EasingType : byte
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    EaseInQuad,
    EaseOutQuad,
    EaseInOutQuad,
    EaseInCubic,
    EaseOutCubic,
    EaseInOutCubic,
    EaseInQuart,
    EaseOutQuart,
    EaseInOutQuart,
    EaseInQuint,
    EaseOutQuint,
    EaseInOutQuint,
    EaseInExpo,
    EaseOutExpo,
    EaseInOutExpo,
    EaseInCirc,
    EaseOutCirc,
    EaseInOutCirc,
    EaseInBack,
    EaseOutBack,
    EaseInOutBack,
    EaseInElastic,
    EaseOutElastic,
    EaseInOutElastic,
    EaseInBounce,
    EaseOutBounce,
    EaseInOutBounce,
    CubicBezier,
    Spring
}

/// <summary>
/// 动画目标 - 描述要动画化的属性
/// </summary>
public readonly struct AnimationTarget
{
    /// <summary>
    /// 目标节点 ID
    /// </summary>
    public readonly uint NodeId;

    /// <summary>
    /// 目标属性类型
    /// </summary>
    public readonly AnimatableProperty Property;

    /// <summary>
    /// 起始值索引
    /// </summary>
    public readonly uint FromValueIndex;

    /// <summary>
    /// 结束值索引
    /// </summary>
    public readonly uint ToValueIndex;

    /// <summary>
    /// 动画曲线索引
    /// </summary>
    public readonly uint CurveIndex;

    public AnimationTarget(uint nodeId, AnimatableProperty property, uint fromIndex, uint toIndex, uint curveIndex)
    {
        NodeId = nodeId;
        Property = property;
        FromValueIndex = fromIndex;
        ToValueIndex = toIndex;
        CurveIndex = curveIndex;
    }
}

/// <summary>
/// 可动画属性
/// </summary>
public enum AnimatableProperty : byte
{
    // 位置
    X,
    Y,

    // 变换
    TranslateX,
    TranslateY,
    ScaleX,
    ScaleY,
    Rotation,

    // 尺寸
    Width,
    Height,

    // 材质
    Opacity,
    BackgroundColor,
    BorderColor,
    ForegroundColor,

    // 圆角
    CornerRadiusTopLeft,
    CornerRadiusTopRight,
    CornerRadiusBottomRight,
    CornerRadiusBottomLeft,

    // 边框
    BorderThicknessLeft,
    BorderThicknessTop,
    BorderThicknessRight,
    BorderThicknessBottom
}

#endregion

#region Draw Commands (GPU 指令)

/// <summary>
/// 绘制命令 - GPU 友好的绘制指令
/// </summary>
public abstract class DrawCommand
{
    /// <summary>
    /// 命令类型
    /// </summary>
    public abstract DrawCommandType CommandType { get; }
}

/// <summary>
/// 绘制命令类型
/// </summary>
public enum DrawCommandType : byte
{
    /// <summary>
    /// 设置渲染目标
    /// </summary>
    SetRenderTarget,

    /// <summary>
    /// 清除渲染目标
    /// </summary>
    Clear,

    /// <summary>
    /// 设置裁剪区域
    /// </summary>
    SetClip,

    /// <summary>
    /// 设置变换
    /// </summary>
    SetTransform,

    /// <summary>
    /// 绘制矩形批次
    /// </summary>
    DrawRectBatch,

    /// <summary>
    /// 绘制文本批次
    /// </summary>
    DrawTextBatch,

    /// <summary>
    /// 绘制图像批次
    /// </summary>
    DrawImageBatch,

    /// <summary>
    /// 绘制路径
    /// </summary>
    DrawPath,

    /// <summary>
    /// 绘制路径批次 - 合并多个路径到单次提交
    /// </summary>
    DrawPathBatch,

    /// <summary>
    /// 应用效果
    /// </summary>
    ApplyEffect,

    /// <summary>
    /// 应用 Backdrop Filter
    /// </summary>
    ApplyBackdropFilter,

    /// <summary>
    /// 捕获背景
    /// </summary>
    CaptureBackdrop,

    /// <summary>
    /// 合成图层
    /// </summary>
    CompositeLayer,

    /// <summary>
    /// 提交
    /// </summary>
    Submit
}

/// <summary>
/// 矩形批次绘制命令
/// </summary>
public sealed class DrawRectBatchCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.DrawRectBatch;

    /// <summary>
    /// 实例数据缓冲区偏移
    /// </summary>
    public uint InstanceBufferOffset { get; init; }

    /// <summary>
    /// 实例数量
    /// </summary>
    public uint InstanceCount { get; init; }

    /// <summary>
    /// 使用的纹理索引（0 表示无纹理）
    /// </summary>
    public uint TextureIndex { get; init; }
}

/// <summary>
/// 文本批次绘制命令
/// </summary>
public sealed class DrawTextBatchCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.DrawTextBatch;

    /// <summary>
    /// 字形图集索引
    /// </summary>
    public uint GlyphAtlasIndex { get; init; }

    /// <summary>
    /// 字形实例缓冲区偏移
    /// </summary>
    public uint InstanceBufferOffset { get; init; }

    /// <summary>
    /// 字形数量
    /// </summary>
    public uint GlyphCount { get; init; }
}

/// <summary>
/// 设置裁剪区域命令
/// </summary>
public sealed class SetClipCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.SetClip;

    /// <summary>
    /// 裁剪矩形
    /// </summary>
    public Rect ClipRect { get; init; }

    /// <summary>
    /// 是否与当前裁剪区域相交
    /// </summary>
    public bool Intersect { get; init; }
}

/// <summary>
/// 应用效果命令
/// </summary>
public sealed class ApplyEffectCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.ApplyEffect;

    /// <summary>
    /// 效果类型
    /// </summary>
    public EffectType Effect { get; init; }

    /// <summary>
    /// 源纹理索引
    /// </summary>
    public uint SourceTextureIndex { get; init; }

    /// <summary>
    /// 目标纹理索引
    /// </summary>
    public uint DestTextureIndex { get; init; }

    /// <summary>
    /// 效果参数
    /// </summary>
    public ReadOnlyMemory<byte> Parameters { get; init; }
}

/// <summary>
/// 应用 Backdrop Filter 命令
/// </summary>
public sealed class ApplyBackdropFilterCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.ApplyBackdropFilter;

    /// <summary>
    /// 滤镜参数
    /// </summary>
    public BackdropFilterParams Params { get; init; }

    /// <summary>
    /// 滤镜应用区域
    /// </summary>
    public Rect Region { get; init; }

    /// <summary>
    /// 背景纹理索引（捕获的背景）
    /// </summary>
    public uint BackdropTextureIndex { get; init; }

    /// <summary>
    /// 输出纹理索引
    /// </summary>
    public uint OutputTextureIndex { get; init; }

    /// <summary>
    /// 圆角半径
    /// </summary>
    public CornerRadius CornerRadius { get; init; }

    /// <summary>
    /// 变换索引
    /// </summary>
    public uint TransformIndex { get; init; }
}

/// <summary>
/// 捕获背景命令
/// </summary>
public sealed class CaptureBackdropCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.CaptureBackdrop;

    /// <summary>
    /// 捕获区域
    /// </summary>
    public Rect Region { get; init; }

    /// <summary>
    /// 目标纹理索引
    /// </summary>
    public uint TargetTextureIndex { get; init; }
}

/// <summary>
/// 图像批次绘制命令
/// </summary>
public sealed class DrawImageBatchCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.DrawImageBatch;

    /// <summary>
    /// 纹理索引
    /// </summary>
    public uint TextureIndex { get; init; }

    /// <summary>
    /// 实例数据缓冲区偏移
    /// </summary>
    public uint InstanceBufferOffset { get; init; }

    /// <summary>
    /// 实例数量
    /// </summary>
    public uint InstanceCount { get; init; }
}

/// <summary>
/// 路径绘制命令
/// </summary>
public sealed class DrawPathCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.DrawPath;

    /// <summary>
    /// 路径缓存索引
    /// </summary>
    public uint PathCacheIndex { get; init; }

    /// <summary>
    /// 材质索引
    /// </summary>
    public uint MaterialIndex { get; init; }

    /// <summary>
    /// 变换索引
    /// </summary>
    public uint TransformIndex { get; init; }
}

/// <summary>
/// 路径批次绘制命令 - 将多个路径合并为单次 GPU 提交
/// 所有路径共享同一全局顶点/索引缓冲区，通过偏移区分。
/// </summary>
public sealed class DrawPathBatchCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.DrawPathBatch;

    /// <summary>
    /// 批次中的路径条目（每个路径的缓存索引、材质和变换）
    /// </summary>
    public PathBatchEntry[] Entries { get; init; } = [];

    /// <summary>
    /// 批次中路径的数量
    /// </summary>
    public int Count => Entries.Length;
}

/// <summary>
/// 路径批次中的单个路径条目
/// </summary>
public readonly struct PathBatchEntry
{
    /// <summary>
    /// 路径缓存索引
    /// </summary>
    public readonly uint PathCacheIndex;

    /// <summary>
    /// 材质索引
    /// </summary>
    public readonly uint MaterialIndex;

    /// <summary>
    /// 变换索引
    /// </summary>
    public readonly uint TransformIndex;

    public PathBatchEntry(uint pathCacheIndex, uint materialIndex, uint transformIndex)
    {
        PathCacheIndex = pathCacheIndex;
        MaterialIndex = materialIndex;
        TransformIndex = transformIndex;
    }
}

/// <summary>
/// 设置渲染目标命令
/// </summary>
public sealed class SetRenderTargetCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.SetRenderTarget;

    /// <summary>
    /// 渲染目标索引（0 = 主目标，> 0 = 离屏目标）
    /// </summary>
    public uint RenderTargetIndex { get; init; }

    /// <summary>
    /// 是否清除
    /// </summary>
    public bool Clear { get; init; }

    /// <summary>
    /// 清除颜色
    /// </summary>
    public uint ClearColor { get; init; }
}

/// <summary>
/// 清除命令
/// </summary>
public sealed class ClearCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.Clear;

    /// <summary>
    /// 清除颜色
    /// </summary>
    public uint Color { get; init; }
}

/// <summary>
/// 合成图层命令
/// </summary>
public sealed class CompositeLayerCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.CompositeLayer;

    /// <summary>
    /// 源纹理索引
    /// </summary>
    public uint SourceTextureIndex { get; init; }

    /// <summary>
    /// 混合模式
    /// </summary>
    public BlendMode BlendMode { get; init; }

    /// <summary>
    /// 不透明度
    /// </summary>
    public byte Opacity { get; init; }

    /// <summary>
    /// 目标区域
    /// </summary>
    public Rect DestRect { get; init; }
}

/// <summary>
/// 设置变换命令
/// </summary>
public sealed class SetTransformCommand : DrawCommand
{
    public override DrawCommandType CommandType => DrawCommandType.SetTransform;

    /// <summary>
    /// 变换索引
    /// </summary>
    public uint TransformIndex { get; init; }
}

#endregion

#region Compiled UI Bundle

/// <summary>
/// 编译后的 UI Bundle - 包含所有 GPU 需要的数据
/// </summary>
public sealed class CompiledUIBundle
{
    /// <summary>
    /// Bundle 版本
    /// </summary>
    public int Version { get; init; } = RenderIR.Version;

    /// <summary>
    /// 场景节点数组
    /// </summary>
    public required SceneNode[] Nodes { get; init; }

    /// <summary>
    /// 材质数组
    /// </summary>
    public required Material[] Materials { get; init; }

    /// <summary>
    /// 渐变定义数组
    /// </summary>
    public required GradientDef[] Gradients { get; init; }

    /// <summary>
    /// 渐变停止点数组
    /// </summary>
    public required GradientStop[] GradientStops { get; init; }

    /// <summary>
    /// 动画曲线数组
    /// </summary>
    public required AnimationCurve[] Curves { get; init; }

    /// <summary>
    /// 动画目标数组
    /// </summary>
    public required AnimationTarget[] AnimationTargets { get; init; }

    /// <summary>
    /// 变换矩阵数组（3x2 仿射变换，每个 6 个 float）
    /// </summary>
    public required float[] Transforms { get; init; }

    /// <summary>
    /// 动画值数组（各种类型的值）
    /// </summary>
    public required byte[] AnimationValues { get; init; }

    /// <summary>
    /// 预编译的绘制命令序列
    /// </summary>
    public required DrawCommand[] DrawCommands { get; init; }

    /// <summary>
    /// 纹理资源引用
    /// </summary>
    public required TextureRef[] Textures { get; init; }

    /// <summary>
    /// 字形图集引用
    /// </summary>
    public required GlyphAtlasRef[] GlyphAtlases { get; init; }

    /// <summary>
    /// 路径缓存
    /// </summary>
    public required PathCache[] PathCaches { get; init; }

    /// <summary>
    /// 路径顶点数据（float X, Y 对）
    /// </summary>
    public byte[] VertexData { get; init; } = [];

    /// <summary>
    /// 路径索引数据
    /// </summary>
    public ushort[] IndexData { get; init; } = [];

    /// <summary>
    /// 原始路径数据字符串（SVG mini-language），与 PathCaches 索引对应
    /// </summary>
    public string[] PathDataStrings { get; init; } = [];

    /// <summary>
    /// 交互区域映射（用于 hit testing）
    /// </summary>
    public required InteractiveRegion[] InteractiveRegions { get; init; }

    /// <summary>
    /// 状态转换表（用于状态机驱动的 UI 更新）
    /// </summary>
    public required StateTransition[] StateTransitions { get; init; }

    /// <summary>
    /// Backdrop Filter 参数数组
    /// </summary>
    public BackdropFilterParams[] BackdropFilterParams { get; init; } = [];
}

/// <summary>
/// 纹理引用
/// </summary>
public readonly struct TextureRef
{
    /// <summary>
    /// 资源路径/标识符
    /// </summary>
    public readonly string Path;

    /// <summary>
    /// 宽度
    /// </summary>
    public readonly ushort Width;

    /// <summary>
    /// 高度
    /// </summary>
    public readonly ushort Height;

    /// <summary>
    /// 格式
    /// </summary>
    public readonly TextureFormat Format;

    public TextureRef(string path, ushort width, ushort height, TextureFormat format)
    {
        Path = path;
        Width = width;
        Height = height;
        Format = format;
    }
}

/// <summary>
/// 纹理格式
/// </summary>
public enum TextureFormat : byte
{
    RGBA8,
    BGRA8,
    R8,
    BC1,
    BC3,
    BC7,
    ASTC
}

/// <summary>
/// 字形图集引用
/// </summary>
public readonly struct GlyphAtlasRef
{
    /// <summary>
    /// 字体标识符
    /// </summary>
    public readonly string FontId;

    /// <summary>
    /// 字体大小
    /// </summary>
    public readonly float FontSize;

    /// <summary>
    /// 图集宽度
    /// </summary>
    public readonly ushort Width;

    /// <summary>
    /// 图集高度
    /// </summary>
    public readonly ushort Height;

    public GlyphAtlasRef(string fontId, float fontSize, ushort width, ushort height)
    {
        FontId = fontId;
        FontSize = fontSize;
        Width = width;
        Height = height;
    }
}

/// <summary>
/// 路径缓存
/// </summary>
public readonly struct PathCache
{
    /// <summary>
    /// 路径数据哈希
    /// </summary>
    public readonly ulong PathHash;

    /// <summary>
    /// 顶点数据偏移
    /// </summary>
    public readonly uint VertexOffset;

    /// <summary>
    /// 顶点数量
    /// </summary>
    public readonly uint VertexCount;

    /// <summary>
    /// 索引数据偏移
    /// </summary>
    public readonly uint IndexOffset;

    /// <summary>
    /// 索引数量
    /// </summary>
    public readonly uint IndexCount;

    public PathCache(ulong pathHash, uint vertexOffset, uint vertexCount, uint indexOffset, uint indexCount)
    {
        PathHash = pathHash;
        VertexOffset = vertexOffset;
        VertexCount = vertexCount;
        IndexOffset = indexOffset;
        IndexCount = indexCount;
    }
}

/// <summary>
/// 交互区域 - 用于 hit testing
/// </summary>
public readonly struct InteractiveRegion
{
    /// <summary>
    /// 节点 ID
    /// </summary>
    public readonly uint NodeId;

    /// <summary>
    /// 边界（屏幕空间）
    /// </summary>
    public readonly Rect Bounds;

    /// <summary>
    /// 交互类型标志
    /// </summary>
    public readonly InteractionFlags Flags;

    /// <summary>
    /// 事件处理器索引
    /// </summary>
    public readonly uint HandlerIndex;

    /// <summary>
    /// 裁剪边界（由祖先 ClipToBounds 元素决定）。
    /// 如果 IsEmpty 则不应用裁剪。
    /// </summary>
    public readonly Rect ClipBounds;

    public InteractiveRegion(uint nodeId, Rect bounds, InteractionFlags flags, uint handlerIndex, Rect clipBounds = default)
    {
        NodeId = nodeId;
        Bounds = bounds;
        Flags = flags;
        HandlerIndex = handlerIndex;
        ClipBounds = clipBounds;
    }
}

/// <summary>
/// 交互类型标志
/// </summary>
[Flags]
public enum InteractionFlags : byte
{
    None = 0,
    Click = 1,
    DoubleClick = 2,
    Hover = 4,
    Drag = 8,
    Scroll = 16,
    Focus = 32,
    KeyInput = 64
}

/// <summary>
/// 状态转换 - 用于状态机驱动的 UI 更新
/// </summary>
public readonly struct StateTransition
{
    /// <summary>
    /// 触发条件类型
    /// </summary>
    public readonly TriggerType Trigger;

    /// <summary>
    /// 源状态 ID
    /// </summary>
    public readonly uint FromStateId;

    /// <summary>
    /// 目标状态 ID
    /// </summary>
    public readonly uint ToStateId;

    /// <summary>
    /// 要应用的动画目标起始索引
    /// </summary>
    public readonly uint AnimationStartIndex;

    /// <summary>
    /// 要应用的动画目标数量
    /// </summary>
    public readonly uint AnimationCount;

    /// <summary>
    /// 要更新的材质映射起始索引
    /// </summary>
    public readonly uint MaterialUpdateStartIndex;

    /// <summary>
    /// 要更新的材质数量
    /// </summary>
    public readonly uint MaterialUpdateCount;

    public StateTransition(TriggerType trigger, uint fromState, uint toState,
        uint animStart, uint animCount, uint matStart, uint matCount)
    {
        Trigger = trigger;
        FromStateId = fromState;
        ToStateId = toState;
        AnimationStartIndex = animStart;
        AnimationCount = animCount;
        MaterialUpdateStartIndex = matStart;
        MaterialUpdateCount = matCount;
    }
}

/// <summary>
/// 触发条件类型
/// </summary>
public enum TriggerType : byte
{
    None,
    MouseEnter,
    MouseLeave,
    MouseDown,
    MouseUp,
    Focus,
    Blur,
    PropertyChanged,
    DataChanged,
    KeyDown,
    KeyUp,
    TextInput
}

#endregion

#region Basic Types

/// <summary>
/// 矩形
/// </summary>
public readonly struct Rect
{
    public readonly float X, Y, Width, Height;

    public Rect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public static readonly Rect Empty = new(0, 0, 0, 0);

    /// <summary>
    /// 计算两个矩形的交集。如果不相交则返回 Empty。
    /// </summary>
    public Rect Intersect(Rect other)
    {
        float x1 = MathF.Max(X, other.X);
        float y1 = MathF.Max(Y, other.Y);
        float x2 = MathF.Min(X + Width, other.X + other.Width);
        float y2 = MathF.Min(Y + Height, other.Y + other.Height);

        if (x2 > x1 && y2 > y1)
            return new Rect(x1, y1, x2 - x1, y2 - y1);

        return Empty;
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// 点
/// </summary>
public readonly struct Point
{
    public readonly float X, Y;

    public Point(float x, float y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// 圆角半径
/// </summary>
public readonly struct CornerRadius
{
    public readonly float TopLeft, TopRight, BottomRight, BottomLeft;

    public CornerRadius(float uniform)
    {
        TopLeft = TopRight = BottomRight = BottomLeft = uniform;
    }

    public CornerRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }
}

/// <summary>
/// 边距/边框厚度
/// </summary>
public readonly struct Thickness
{
    public readonly float Left, Top, Right, Bottom;

    public Thickness(float uniform)
    {
        Left = Top = Right = Bottom = uniform;
    }

    public Thickness(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    public Thickness(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}

#endregion
