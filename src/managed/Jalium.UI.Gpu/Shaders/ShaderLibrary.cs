namespace Jalium.UI.Gpu.Shaders;

/// <summary>
/// HLSL Shader 源码库 - 所有 UI 原语的着色器
/// 每个着色器以 const string 形式嵌入，运行时通过 ShaderCompiler 编译
/// </summary>
public static class ShaderLibrary
{
    #region Common Constant Buffers

    /// <summary>
    /// 帧级常量缓冲区 - 所有着色器共享
    /// </summary>
    public const string CommonConstants = """
        cbuffer FrameConstants : register(b0)
        {
            float2 ViewportSize;    // 视口尺寸（像素）
            float  Time;            // 时间（秒）
            float  DpiScale;        // DPI 缩放因子
            float4x4 ProjectionMatrix; // 正交投影矩阵
        };
        """;

    /// <summary>
    /// 材质常量缓冲区
    /// </summary>
    public const string MaterialConstants = """
        cbuffer MaterialConstants : register(b1)
        {
            uint  MaterialCount;
            uint  Padding0;
            uint  Padding1;
            uint  Padding2;
        };

        struct MaterialData
        {
            uint  BackgroundColor;  // ARGB 预乘
            uint  BorderColor;
            uint  ForegroundColor;
            uint  GradientIndex;    // 0 = 纯色
            float Opacity;
            uint  BlendMode;
            float SdfSmoothness;    // SDF AA 控制
            uint  Flags;            // 材质标志位
        };

        StructuredBuffer<MaterialData> Materials : register(t0);
        """;

    /// <summary>
    /// 渐变数据缓冲区
    /// </summary>
    public const string GradientBuffer = """
        struct GradientDef
        {
            uint  Type;         // 0=Linear, 1=Radial, 2=Conic
            float2 Start;
            float2 End;
            uint  StopsIndex;
            uint  StopsCount;
            uint  Padding;
        };

        struct GradientStop
        {
            float Offset;
            uint  Color;
        };

        StructuredBuffer<GradientDef>  Gradients     : register(t1);
        StructuredBuffer<GradientStop> GradientStops : register(t2);
        """;

    #endregion

    #region Utility Functions

    /// <summary>
    /// 公共工具函数 - 颜色解包、SDF、采样等
    /// </summary>
    public const string UtilityFunctions = """
        // ARGB uint → float4 (预乘 alpha)
        float4 UnpackColor(uint packed)
        {
            float4 c;
            c.a = ((packed >> 24) & 0xFF) / 255.0;
            c.r = ((packed >> 16) & 0xFF) / 255.0;
            c.g = ((packed >> 8)  & 0xFF) / 255.0;
            c.b = ((packed)       & 0xFF) / 255.0;
            return c;
        }

        // SDF 圆角矩形 - 支持四个不同圆角半径
        // p: 相对于矩形中心的坐标
        // b: 矩形半尺寸 (width/2, height/2)
        // r: 四个圆角半径 (topRight, bottomRight, bottomLeft, topLeft)
        float sdRoundedBox(float2 p, float2 b, float4 r)
        {
            r.xy = (p.x > 0.0) ? r.xy : r.zw;
            r.x  = (p.y > 0.0) ? r.x  : r.y;
            float2 q = abs(p) - b + r.x;
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
        }

        // SDF 矩形（无圆角）
        float sdBox(float2 p, float2 b)
        {
            float2 d = abs(p) - b;
            return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
        }

        // SDF 抗锯齿 - 基于屏幕空间导数
        float sdfAA(float dist, float smoothness)
        {
            float fw = fwidth(dist) * smoothness;
            return 1.0 - smoothstep(-fw, fw, dist);
        }

        // 采样渐变色
        float4 SampleGradient(uint gradientIndex, float2 uv, float2 localPos, float2 size)
        {
            GradientDef grad = Gradients[gradientIndex];
            float t = 0;

            if (grad.Type == 0) // Linear
            {
                float2 dir = grad.End - grad.Start;
                float2 pos = localPos / size;
                t = dot(pos - grad.Start, dir) / dot(dir, dir);
            }
            else if (grad.Type == 1) // Radial
            {
                float2 pos = localPos / size;
                float2 center = grad.Start;
                float radius = length(grad.End - grad.Start);
                t = length(pos - center) / max(radius, 0.001);
            }
            else // Conic
            {
                float2 pos = localPos / size;
                float2 center = grad.Start;
                float2 d = pos - center;
                t = (atan2(d.y, d.x) / 3.14159265 + 1.0) * 0.5;
            }

            t = saturate(t);

            // 查找渐变停止点并插值
            float4 result = float4(0, 0, 0, 0);
            for (uint i = 0; i < grad.StopsCount - 1; i++)
            {
                GradientStop s0 = GradientStops[grad.StopsIndex + i];
                GradientStop s1 = GradientStops[grad.StopsIndex + i + 1];

                if (t >= s0.Offset && t <= s1.Offset)
                {
                    float localT = (t - s0.Offset) / max(s1.Offset - s0.Offset, 0.001);
                    result = lerp(UnpackColor(s0.Color), UnpackColor(s1.Color), localT);
                    break;
                }
            }

            // 小于第一个停止点
            if (t < GradientStops[grad.StopsIndex].Offset)
                result = UnpackColor(GradientStops[grad.StopsIndex].Color);

            // 大于最后一个停止点
            if (t > GradientStops[grad.StopsIndex + grad.StopsCount - 1].Offset)
                result = UnpackColor(GradientStops[grad.StopsIndex + grad.StopsCount - 1].Color);

            return result;
        }
        """;

    #endregion

    #region UI Rect Shader

    /// <summary>
    /// 矩形顶点着色器 - 实例化渲染
    /// 输入：单位正方形顶点 + 80B per-instance 数据
    /// 输出：屏幕空间位置 + 插值数据给 PS
    /// </summary>
    public const string UIRectVS = """
        struct VSInput
        {
            // Per-vertex (Slot 0, 16B stride)
            float2 Position : POSITION;
            float2 TexCoord : TEXCOORD0;

            // Per-instance (Slot 1, 80B stride)
            float2 InstPosition      : INST_POSITION;      // offset 0
            float2 InstSize          : INST_SIZE;           // offset 8
            float4 InstUV            : INST_UV;             // offset 16
            uint   InstColor         : INST_COLOR;          // offset 32
            float4 InstCornerRadius  : INST_CORNER_RADIUS;  // offset 36
            float4 InstBorderThick   : INST_BORDER_THICK;   // offset 52
            uint   InstBorderColor   : INST_BORDER_COLOR;   // offset 68
            float2 InstPadding       : INST_PADDING;        // offset 72
        };

        struct VSOutput
        {
            float4 Position       : SV_POSITION;
            float2 LocalPos       : TEXCOORD0;   // 相对于矩形左上角
            float2 RectSize       : TEXCOORD1;   // 矩形尺寸
            float4 CornerRadius   : TEXCOORD2;   // 四个圆角
            float4 BorderThick    : TEXCOORD3;   // 四边边框厚度
            nointerpolation uint  FillColor      : COLOR0;
            nointerpolation uint  BorderColor    : COLOR1;
            nointerpolation uint  MaterialIndex  : TEXCOORD4;
        };

        VSOutput VSMain(VSInput input, uint instanceId : SV_InstanceID)
        {
            VSOutput output;

            // 将单位正方形缩放到实例尺寸并平移
            float2 worldPos = input.Position * input.InstSize + input.InstPosition;

            // 转换到 NDC（正交投影）
            float2 ndc = (worldPos / ViewportSize) * 2.0 - 1.0;
            ndc.y = -ndc.y; // 翻转 Y 轴（屏幕坐标 → NDC）

            output.Position     = float4(ndc, 0.0, 1.0);
            output.LocalPos     = input.Position * input.InstSize; // 矩形内局部坐标
            output.RectSize     = input.InstSize;
            output.CornerRadius = input.InstCornerRadius;
            output.BorderThick  = input.InstBorderThick;
            output.FillColor    = input.InstColor;
            output.BorderColor  = input.InstBorderColor;
            output.MaterialIndex = instanceId;

            return output;
        }
        """;

    /// <summary>
    /// 矩形像素着色器 - SDF 圆角 + 边框 + 渐变 + AA
    /// </summary>
    public const string UIRectPS = """
        struct PSInput
        {
            float4 Position       : SV_POSITION;
            float2 LocalPos       : TEXCOORD0;
            float2 RectSize       : TEXCOORD1;
            float4 CornerRadius   : TEXCOORD2;
            float4 BorderThick    : TEXCOORD3;
            nointerpolation uint  FillColor      : COLOR0;
            nointerpolation uint  BorderColor    : COLOR1;
            nointerpolation uint  MaterialIndex  : TEXCOORD4;
        };

        Texture2D    DiffuseTexture : register(t3);
        SamplerState LinearSampler  : register(s0);

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float2 halfSize = input.RectSize * 0.5;
            float2 centerPos = input.LocalPos - halfSize;

            // SDF 圆角矩形
            // r = (topRight, bottomRight, bottomLeft, topLeft)
            float4 r = input.CornerRadius;
            float dist = sdRoundedBox(centerPos, halfSize, r);

            // 抗锯齿
            float alpha = sdfAA(dist, 1.0);
            if (alpha <= 0.0)
                discard;

            // 填充颜色
            float4 fillColor = UnpackColor(input.FillColor);

            // 边框
            float4 borderColor = UnpackColor(input.BorderColor);
            float avgBorder = (input.BorderThick.x + input.BorderThick.y +
                               input.BorderThick.z + input.BorderThick.w) * 0.25;

            if (avgBorder > 0.0)
            {
                // 内部 SDF（收缩边框厚度）
                float2 innerHalfSize = halfSize - avgBorder;
                float4 innerR = max(r - avgBorder, 0.0);
                float innerDist = sdRoundedBox(centerPos, innerHalfSize, innerR);
                float innerAlpha = sdfAA(innerDist, 1.0);

                // 混合：外部是边框色，内部是填充色
                fillColor = lerp(borderColor, fillColor, innerAlpha);
            }

            fillColor.a *= alpha;
            return fillColor;
        }
        """;

    #endregion

    #region Text Shader

    /// <summary>
    /// 文本顶点着色器 - 字形实例化
    /// </summary>
    public const string TextVS = """
        struct TextVSInput
        {
            float2 Position : POSITION;
            float2 TexCoord : TEXCOORD0;

            // Per-glyph instance
            float2 GlyphPos    : GLYPH_POS;     // 字形屏幕位置
            float2 GlyphSize   : GLYPH_SIZE;    // 字形尺寸
            float4 GlyphUV     : GLYPH_UV;      // 字形在图集中的 UV (u0,v0,u1,v1)
            uint   GlyphColor  : GLYPH_COLOR;   // 文本颜色
        };

        struct TextVSOutput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
            nointerpolation uint Color : COLOR0;
        };

        TextVSOutput TextVSMain(TextVSInput input)
        {
            TextVSOutput output;

            float2 worldPos = input.Position * input.GlyphSize + input.GlyphPos;
            float2 ndc = (worldPos / ViewportSize) * 2.0 - 1.0;
            ndc.y = -ndc.y;

            output.Position = float4(ndc, 0.0, 1.0);

            // 插值 UV：从 GlyphUV 范围映射
            output.TexCoord = lerp(input.GlyphUV.xy, input.GlyphUV.zw, input.TexCoord);
            output.Color = input.GlyphColor;

            return output;
        }
        """;

    /// <summary>
    /// 文本像素着色器 - SDF 文本渲染 + subpixel AA
    /// </summary>
    public const string TextPS = """
        struct TextPSInput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
            nointerpolation uint Color : COLOR0;
        };

        Texture2D    GlyphAtlas   : register(t3);
        SamplerState AtlasSampler : register(s0);

        float4 TextPSMain(TextPSInput input) : SV_TARGET
        {
            float4 textColor = UnpackColor(input.Color);

            // SDF 采样
            float dist = GlyphAtlas.Sample(AtlasSampler, input.TexCoord).r;

            // SDF 阈值 0.5，带 AA
            float fw = fwidth(dist);
            float alpha = smoothstep(0.5 - fw, 0.5 + fw, dist);

            if (alpha <= 0.01)
                discard;

            // Subpixel AA - 水平方向 RGB 子像素偏移采样
            float2 texelSize = float2(ddx(input.TexCoord.x), 0);
            float distR = GlyphAtlas.Sample(AtlasSampler, input.TexCoord - texelSize * 0.333).r;
            float distB = GlyphAtlas.Sample(AtlasSampler, input.TexCoord + texelSize * 0.333).r;

            float3 subpixelAlpha;
            subpixelAlpha.r = smoothstep(0.5 - fw, 0.5 + fw, distR);
            subpixelAlpha.g = alpha;
            subpixelAlpha.b = smoothstep(0.5 - fw, 0.5 + fw, distB);

            float4 result;
            result.rgb = textColor.rgb * subpixelAlpha;
            result.a = max(max(subpixelAlpha.r, subpixelAlpha.g), subpixelAlpha.b) * textColor.a;

            return result;
        }
        """;

    #endregion

    #region Image Shader

    /// <summary>
    /// 图像顶点着色器 - 纹理映射 + 九宫格
    /// </summary>
    public const string ImageVS = """
        struct ImageVSInput
        {
            float2 Position : POSITION;
            float2 TexCoord : TEXCOORD0;

            // Per-instance
            float2 InstPosition : INST_POSITION;
            float2 InstSize     : INST_SIZE;
            float4 InstUV       : INST_UV;         // UV 范围 (u0,v0,u1,v1)
            uint   InstColor    : INST_COLOR;       // 色调
            float4 NineSlice    : INST_NINESLICE;   // 九宫格边距 (left,top,right,bottom)
        };

        struct ImageVSOutput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
            nointerpolation uint TintColor : COLOR0;
        };

        ImageVSOutput ImageVSMain(ImageVSInput input)
        {
            ImageVSOutput output;

            float2 worldPos = input.Position * input.InstSize + input.InstPosition;
            float2 ndc = (worldPos / ViewportSize) * 2.0 - 1.0;
            ndc.y = -ndc.y;

            output.Position = float4(ndc, 0.0, 1.0);
            output.TexCoord = lerp(input.InstUV.xy, input.InstUV.zw, input.TexCoord);
            output.TintColor = input.InstColor;

            return output;
        }
        """;

    /// <summary>
    /// 图像像素着色器 - 纹理采样 + blend mode
    /// </summary>
    public const string ImagePS = """
        struct ImagePSInput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
            nointerpolation uint TintColor : COLOR0;
        };

        Texture2D    ImageTexture  : register(t3);
        SamplerState ImageSampler  : register(s0);

        float4 ImagePSMain(ImagePSInput input) : SV_TARGET
        {
            float4 texColor = ImageTexture.Sample(ImageSampler, input.TexCoord);
            float4 tint = UnpackColor(input.TintColor);

            // 应用色调（乘法混合）
            float4 result = texColor * tint;

            if (result.a <= 0.01)
                discard;

            return result;
        }
        """;

    #endregion

    #region Path Shader

    /// <summary>
    /// 路径顶点着色器 - 三角网格路径渲染
    /// </summary>
    public const string PathVS = """
        struct PathVSInput
        {
            float2 Position : POSITION;
            float2 TexCoord : TEXCOORD0;
        };

        cbuffer PathConstants : register(b2)
        {
            float4x4 PathTransform;
            uint     PathMaterialIndex;
            float3   PathPadding;
        };

        struct PathVSOutput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
        };

        PathVSOutput PathVSMain(PathVSInput input)
        {
            PathVSOutput output;

            float2 worldPos = mul(float4(input.Position, 0, 1), PathTransform).xy;
            float2 ndc = (worldPos / ViewportSize) * 2.0 - 1.0;
            ndc.y = -ndc.y;

            output.Position = float4(ndc, 0.0, 1.0);
            output.TexCoord = input.TexCoord;

            return output;
        }
        """;

    /// <summary>
    /// 路径像素着色器 - 填充/描边
    /// </summary>
    public const string PathPS = """
        struct PathPSInput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
        };

        float4 PathPSMain(PathPSInput input) : SV_TARGET
        {
            MaterialData mat = Materials[PathMaterialIndex];
            float4 color = UnpackColor(mat.BackgroundColor);
            color.a *= mat.Opacity;

            if (color.a <= 0.01)
                discard;

            return color;
        }
        """;

    #endregion

    #region Gaussian Blur Compute Shader

    /// <summary>
    /// 分离式高斯模糊 - 水平 Pass
    /// </summary>
    public const string GaussianBlurHorizontalCS = """
        Texture2D<float4>   InputTexture  : register(t0);
        RWTexture2D<float4> OutputTexture : register(u0);

        cbuffer BlurConstants : register(b0)
        {
            float  BlurRadius;
            float  BlurSigma;
            float2 TextureSize;
        };

        // 高斯权重计算
        float GaussianWeight(float x, float sigma)
        {
            return exp(-(x * x) / (2.0 * sigma * sigma)) / (sqrt(2.0 * 3.14159265) * sigma);
        }

        [numthreads(256, 1, 1)]
        void BlurHorizontalCS(uint3 dispatchId : SV_DispatchThreadID)
        {
            if (dispatchId.x >= (uint)TextureSize.x || dispatchId.y >= (uint)TextureSize.y)
                return;

            int radius = (int)BlurRadius;
            float sigma = BlurSigma > 0 ? BlurSigma : BlurRadius / 3.0;

            float4 result = float4(0, 0, 0, 0);
            float weightSum = 0;

            for (int i = -radius; i <= radius; i++)
            {
                int sampleX = clamp((int)dispatchId.x + i, 0, (int)TextureSize.x - 1);
                float weight = GaussianWeight((float)i, sigma);

                result += InputTexture[uint2(sampleX, dispatchId.y)] * weight;
                weightSum += weight;
            }

            OutputTexture[dispatchId.xy] = result / weightSum;
        }
        """;

    /// <summary>
    /// 分离式高斯模糊 - 垂直 Pass
    /// </summary>
    public const string GaussianBlurVerticalCS = """
        Texture2D<float4>   InputTexture  : register(t0);
        RWTexture2D<float4> OutputTexture : register(u0);

        cbuffer BlurConstants : register(b0)
        {
            float  BlurRadius;
            float  BlurSigma;
            float2 TextureSize;
        };

        float GaussianWeight(float x, float sigma)
        {
            return exp(-(x * x) / (2.0 * sigma * sigma)) / (sqrt(2.0 * 3.14159265) * sigma);
        }

        [numthreads(1, 256, 1)]
        void BlurVerticalCS(uint3 dispatchId : SV_DispatchThreadID)
        {
            if (dispatchId.x >= (uint)TextureSize.x || dispatchId.y >= (uint)TextureSize.y)
                return;

            int radius = (int)BlurRadius;
            float sigma = BlurSigma > 0 ? BlurSigma : BlurRadius / 3.0;

            float4 result = float4(0, 0, 0, 0);
            float weightSum = 0;

            for (int i = -radius; i <= radius; i++)
            {
                int sampleY = clamp((int)dispatchId.y + i, 0, (int)TextureSize.y - 1);
                float weight = GaussianWeight((float)i, sigma);

                result += InputTexture[uint2(dispatchId.x, sampleY)] * weight;
                weightSum += weight;
            }

            OutputTexture[dispatchId.xy] = result / weightSum;
        }
        """;

    #endregion

    #region Backdrop Filter Compute Shader

    /// <summary>
    /// Backdrop Filter Compute Shader - 颜色矩阵 + 材质效果
    /// </summary>
    public const string BackdropFilterCS = """
        Texture2D<float4>   BackdropTexture : register(t0);
        Texture2D<float4>   BlurredTexture  : register(t1);  // 预模糊的背景
        RWTexture2D<float4> OutputTexture   : register(u0);

        cbuffer FilterConstants : register(b0)
        {
            // 模糊参数 (16 bytes)
            float  BlurRadius;
            float  BlurSigma;
            uint   BlurType;       // 0=Gaussian, 1=Box, 2=Frosted, ...
            float  NoiseIntensity;

            // 颜色调整 (16 bytes)
            float  Brightness;
            float  Contrast;
            float  Saturation;
            float  HueRotation;

            // 色彩变换 (16 bytes)
            float  Grayscale;
            float  Sepia;
            float  Invert;
            float  FilterOpacity;

            // 材质参数 (16 bytes)
            uint   TintColor;
            float  TintOpacity;
            float  Luminosity;
            uint   MaterialType;  // 0=None, 1=Acrylic, 2=Mica, ...

            // 区域 (16 bytes)
            float4 FilterRegion;  // x, y, width, height
        };

        // 伪随机噪声
        float Hash(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        // RGB → HSL
        float3 RGBtoHSL(float3 c)
        {
            float maxC = max(c.r, max(c.g, c.b));
            float minC = min(c.r, min(c.g, c.b));
            float l = (maxC + minC) * 0.5;
            float s = 0, h = 0;

            if (maxC != minC)
            {
                float d = maxC - minC;
                s = l > 0.5 ? d / (2.0 - maxC - minC) : d / (maxC + minC);

                if (maxC == c.r) h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
                else if (maxC == c.g) h = (c.b - c.r) / d + 2.0;
                else h = (c.r - c.g) / d + 4.0;
                h /= 6.0;
            }

            return float3(h, s, l);
        }

        float HueToRGB(float p, float q, float t)
        {
            if (t < 0.0) t += 1.0;
            if (t > 1.0) t -= 1.0;
            if (t < 1.0/6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0/2.0) return q;
            if (t < 2.0/3.0) return p + (q - p) * (2.0/3.0 - t) * 6.0;
            return p;
        }

        float3 HSLtoRGB(float3 hsl)
        {
            float h = hsl.x, s = hsl.y, l = hsl.z;
            if (s == 0) return float3(l, l, l);

            float q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            float p = 2.0 * l - q;
            return float3(
                HueToRGB(p, q, h + 1.0/3.0),
                HueToRGB(p, q, h),
                HueToRGB(p, q, h - 1.0/3.0)
            );
        }

        [numthreads(16, 16, 1)]
        void BackdropFilterMain(uint3 dispatchId : SV_DispatchThreadID)
        {
            float2 pos = float2(dispatchId.xy);

            // 检查是否在滤镜区域内
            if (pos.x < FilterRegion.x || pos.x >= FilterRegion.x + FilterRegion.z ||
                pos.y < FilterRegion.y || pos.y >= FilterRegion.y + FilterRegion.w)
            {
                OutputTexture[dispatchId.xy] = BackdropTexture[dispatchId.xy];
                return;
            }

            // 基础颜色：使用模糊或原始背景
            float4 color = (BlurRadius > 0) ?
                BlurredTexture[dispatchId.xy] :
                BackdropTexture[dispatchId.xy];

            // 亮度
            color.rgb *= Brightness;

            // 对比度
            color.rgb = (color.rgb - 0.5) * Contrast + 0.5;

            // 饱和度
            float gray = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
            color.rgb = lerp(float3(gray, gray, gray), color.rgb, Saturation);

            // 灰度
            if (Grayscale > 0)
            {
                float g = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, float3(g, g, g), Grayscale);
            }

            // 褐色
            if (Sepia > 0)
            {
                float3 sepiaColor = float3(
                    dot(color.rgb, float3(0.393, 0.769, 0.189)),
                    dot(color.rgb, float3(0.349, 0.686, 0.168)),
                    dot(color.rgb, float3(0.272, 0.534, 0.131))
                );
                color.rgb = lerp(color.rgb, sepiaColor, Sepia);
            }

            // 反转
            if (Invert > 0)
            {
                color.rgb = lerp(color.rgb, 1.0 - color.rgb, Invert);
            }

            // 色相旋转
            if (HueRotation != 0)
            {
                float3 hsl = RGBtoHSL(color.rgb);
                hsl.x = frac(hsl.x + HueRotation / 6.28318530);
                color.rgb = HSLtoRGB(hsl);
            }

            // 不透明度
            color.a *= FilterOpacity;

            // 材质色调
            if (TintOpacity > 0)
            {
                float4 tint = UnpackColor(TintColor);
                color.rgb = lerp(color.rgb, tint.rgb, TintOpacity);
            }

            // 噪声（磨砂效果）
            if (NoiseIntensity > 0)
            {
                float noise = Hash(pos + Time) * 2.0 - 1.0;
                color.rgb += noise * NoiseIntensity;
            }

            color.rgb = saturate(color.rgb);
            OutputTexture[dispatchId.xy] = color;
        }
        """;

    #endregion

    #region Composite Shader

    /// <summary>
    /// 合成顶点着色器 - 全屏四边形
    /// </summary>
    public const string CompositeVS = """
        struct CompositeVSOutput
        {
            float4 Position : SV_POSITION;
            float2 TexCoord : TEXCOORD0;
        };

        CompositeVSOutput CompositeVSMain(uint vertexId : SV_VertexID)
        {
            CompositeVSOutput output;

            // 全屏三角形（覆盖 [-1,1] NDC 空间）
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            output.Position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            output.TexCoord = float2(uv.x, 1.0 - uv.y);

            return output;
        }
        """;

    /// <summary>
    /// 合成像素着色器 - 12 种 blend mode
    /// </summary>
    public const string CompositePS = """
        Texture2D    SourceTexture   : register(t0);
        SamplerState CompositeSampler : register(s0);

        cbuffer CompositeConstants : register(b0)
        {
            uint   BlendModeValue; // BlendMode enum
            float  CompositeOpacity;
            float4 DestRect;       // 目标区域 (归一化坐标)
            float2 CompPadding;
        };

        // Blend mode 函数
        float3 BlendNormal(float3 base, float3 blend) { return blend; }
        float3 BlendMultiply(float3 base, float3 blend) { return base * blend; }
        float3 BlendScreen(float3 base, float3 blend) { return 1.0 - (1.0 - base) * (1.0 - blend); }

        float3 BlendOverlay(float3 base, float3 blend)
        {
            return float3(
                base.r < 0.5 ? 2.0 * base.r * blend.r : 1.0 - 2.0 * (1.0 - base.r) * (1.0 - blend.r),
                base.g < 0.5 ? 2.0 * base.g * blend.g : 1.0 - 2.0 * (1.0 - base.g) * (1.0 - blend.g),
                base.b < 0.5 ? 2.0 * base.b * blend.b : 1.0 - 2.0 * (1.0 - base.b) * (1.0 - blend.b)
            );
        }

        float3 BlendDarken(float3 base, float3 blend) { return min(base, blend); }
        float3 BlendLighten(float3 base, float3 blend) { return max(base, blend); }

        float3 BlendColorDodge(float3 base, float3 blend)
        {
            return float3(
                blend.r >= 1.0 ? 1.0 : min(1.0, base.r / (1.0 - blend.r)),
                blend.g >= 1.0 ? 1.0 : min(1.0, base.g / (1.0 - blend.g)),
                blend.b >= 1.0 ? 1.0 : min(1.0, base.b / (1.0 - blend.b))
            );
        }

        float3 BlendColorBurn(float3 base, float3 blend)
        {
            return float3(
                blend.r <= 0.0 ? 0.0 : max(0.0, 1.0 - (1.0 - base.r) / blend.r),
                blend.g <= 0.0 ? 0.0 : max(0.0, 1.0 - (1.0 - base.g) / blend.g),
                blend.b <= 0.0 ? 0.0 : max(0.0, 1.0 - (1.0 - base.b) / blend.b)
            );
        }

        float3 BlendSoftLight(float3 base, float3 blend)
        {
            return float3(
                blend.r < 0.5 ? base.r - (1.0 - 2.0*blend.r)*base.r*(1.0-base.r)
                              : base.r + (2.0*blend.r-1.0)*(sqrt(base.r)-base.r),
                blend.g < 0.5 ? base.g - (1.0 - 2.0*blend.g)*base.g*(1.0-base.g)
                              : base.g + (2.0*blend.g-1.0)*(sqrt(base.g)-base.g),
                blend.b < 0.5 ? base.b - (1.0 - 2.0*blend.b)*base.b*(1.0-base.b)
                              : base.b + (2.0*blend.b-1.0)*(sqrt(base.b)-base.b)
            );
        }

        float3 BlendHardLight(float3 base, float3 blend) { return BlendOverlay(blend, base); }
        float3 BlendDifference(float3 base, float3 blend) { return abs(base - blend); }
        float3 BlendExclusion(float3 base, float3 blend) { return base + blend - 2.0 * base * blend; }

        float4 CompositePSMain(float4 pos : SV_POSITION, float2 uv : TEXCOORD0) : SV_TARGET
        {
            float4 src = SourceTexture.Sample(CompositeSampler, uv);
            src.a *= CompositeOpacity;

            if (src.a <= 0.01)
                discard;

            return src; // 混合由 Output Merger blend state 处理
        }
        """;

    #endregion

    #region Shader Entry Points

    /// <summary>
    /// 获取指定着色器的完整 HLSL 源码（包含公共部分）
    /// </summary>
    public static string GetFullSource(ShaderType type) => type switch
    {
        ShaderType.UIRectVS => string.Concat(CommonConstants, UtilityFunctions, UIRectVS),
        ShaderType.UIRectPS => string.Concat(CommonConstants, MaterialConstants, GradientBuffer, UtilityFunctions, UIRectPS),
        ShaderType.TextVS => string.Concat(CommonConstants, TextVS),
        ShaderType.TextPS => string.Concat(CommonConstants, UtilityFunctions, TextPS),
        ShaderType.ImageVS => string.Concat(CommonConstants, ImageVS),
        ShaderType.ImagePS => string.Concat(CommonConstants, UtilityFunctions, ImagePS),
        ShaderType.PathVS => string.Concat(CommonConstants, PathVS),
        ShaderType.PathPS => string.Concat(CommonConstants, MaterialConstants, UtilityFunctions, PathPS),
        ShaderType.GaussianBlurHorizontalCS => GaussianBlurHorizontalCS,
        ShaderType.GaussianBlurVerticalCS => GaussianBlurVerticalCS,
        ShaderType.BackdropFilterCS => string.Concat(CommonConstants, UtilityFunctions, BackdropFilterCS),
        ShaderType.CompositeVS => CompositeVS,
        ShaderType.CompositePS => string.Concat(CompositePS),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// 获取指定着色器的入口点名称
    /// </summary>
    public static string GetEntryPoint(ShaderType type) => type switch
    {
        ShaderType.UIRectVS => "VSMain",
        ShaderType.UIRectPS => "PSMain",
        ShaderType.TextVS => "TextVSMain",
        ShaderType.TextPS => "TextPSMain",
        ShaderType.ImageVS => "ImageVSMain",
        ShaderType.ImagePS => "ImagePSMain",
        ShaderType.PathVS => "PathVSMain",
        ShaderType.PathPS => "PathPSMain",
        ShaderType.GaussianBlurHorizontalCS => "BlurHorizontalCS",
        ShaderType.GaussianBlurVerticalCS => "BlurVerticalCS",
        ShaderType.BackdropFilterCS => "BackdropFilterMain",
        ShaderType.CompositeVS => "CompositeVSMain",
        ShaderType.CompositePS => "CompositePSMain",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// 获取 shader model target
    /// </summary>
    public static string GetTarget(ShaderType type) => type switch
    {
        ShaderType.UIRectVS or ShaderType.TextVS or ShaderType.ImageVS
            or ShaderType.PathVS or ShaderType.CompositeVS => "vs_5_1",
        ShaderType.UIRectPS or ShaderType.TextPS or ShaderType.ImagePS
            or ShaderType.PathPS or ShaderType.CompositePS => "ps_5_1",
        ShaderType.GaussianBlurHorizontalCS or ShaderType.GaussianBlurVerticalCS
            or ShaderType.BackdropFilterCS => "cs_5_1",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    #endregion
}

/// <summary>
/// 着色器类型枚举
/// </summary>
public enum ShaderType
{
    UIRectVS,
    UIRectPS,
    TextVS,
    TextPS,
    ImageVS,
    ImagePS,
    PathVS,
    PathPS,
    GaussianBlurHorizontalCS,
    GaussianBlurVerticalCS,
    BackdropFilterCS,
    CompositeVS,
    CompositePS
}

/// <summary>
/// 着色器阶段
/// </summary>
public enum ShaderStage
{
    Vertex,
    Pixel,
    Compute
}
