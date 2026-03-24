#pragma once
// Embedded HLSL shader sources for D3D12DirectRenderer.
// Keep in sync with shaders/*.hlsl files.

namespace jalium {
namespace shader_source {

static const char kSdfRectVS[] = R"HLSL(
cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct Instance
{
    float2 position;
    float2 size;
    float4 fillColor;
    float4 borderColor;
    float4 cornerRadius;
    float  borderWidth;
    float  opacity;
    uint   gradientType;
    uint   stopCount;
    float4 gradGeom;
    float4 stop01PosR;
    float4 stop01AG;
    float4 stop12BA;
    float4 stop23GB;
    float4 stop3Color;
    float4 _pad;
};

StructuredBuffer<Instance> instances : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos      : SV_Position;
    float2 localPos     : TEXCOORD0;
    float2 rectSize     : TEXCOORD1;
    float4 cornerRadius : TEXCOORD2;
    float4 fillColor    : COLOR0;
    float4 borderColor  : COLOR1;
    float  borderWidth  : TEXCOORD3;
    nointerpolation uint  gradientType : TEXCOORD4;
    nointerpolation uint  stopCount    : TEXCOORD5;
    nointerpolation float4 gradGeom    : TEXCOORD6;
    nointerpolation float4 stop01PosR  : TEXCOORD7;
    nointerpolation float4 stop01AG    : TEXCOORD8;
    nointerpolation float4 stop12BA    : TEXCOORD9;
    nointerpolation float4 stop23GB    : TEXCOORD10;
    nointerpolation float4 stop3Color  : TEXCOORD11;
    nointerpolation float2 shapeParams : TEXCOORD12; // x = shapeType, y = shapeN
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    Instance inst = instances[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];

    float2 expand = float2(1.0, 1.0);
    float2 pixelPos = inst.position - expand + corner * (inst.size + expand * 2.0);

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);

    o.localPos     = corner * (inst.size + expand * 2.0) - expand;
    o.rectSize     = inst.size;
    o.cornerRadius = inst.cornerRadius;
    o.fillColor    = inst.fillColor * inst.opacity;
    o.borderColor  = inst.borderColor * inst.opacity;
    o.borderWidth  = inst.borderWidth;

    o.gradientType = inst.gradientType;
    o.stopCount    = inst.stopCount;
    o.gradGeom     = inst.gradGeom - float4(inst.position, inst.position);

    float op = inst.opacity;
    o.stop01PosR  = float4(inst.stop01PosR.x,       inst.stop01PosR.yzw * op);
    o.stop01AG    = float4(inst.stop01AG.x * op,     inst.stop01AG.y,            inst.stop01AG.zw * op);
    o.stop12BA    = float4(inst.stop12BA.xy * op,    inst.stop12BA.z,            inst.stop12BA.w * op);
    o.stop23GB    = float4(inst.stop23GB.xy * op,    inst.stop23GB.z * op,       inst.stop23GB.w);
    o.stop3Color  = inst.stop3Color * op;
    o.shapeParams = inst._pad.xy; // shapeType, shapeN

    return o;
}
)HLSL";

static const char kSdfRectPS[] = R"HLSL(
struct PsInput
{
    float4 clipPos      : SV_Position;
    float2 localPos     : TEXCOORD0;
    float2 rectSize     : TEXCOORD1;
    float4 cornerRadius : TEXCOORD2;
    float4 fillColor    : COLOR0;
    float4 borderColor  : COLOR1;
    float  borderWidth  : TEXCOORD3;
    nointerpolation uint  gradientType : TEXCOORD4;
    nointerpolation uint  stopCount    : TEXCOORD5;
    nointerpolation float4 gradGeom    : TEXCOORD6;
    nointerpolation float4 stop01PosR  : TEXCOORD7;
    nointerpolation float4 stop01AG    : TEXCOORD8;
    nointerpolation float4 stop12BA    : TEXCOORD9;
    nointerpolation float4 stop23GB    : TEXCOORD10;
    nointerpolation float4 stop3Color  : TEXCOORD11;
    nointerpolation float2 shapeParams : TEXCOORD12;
};

float sdSuperEllipseRect(float2 p, float2 halfSize, float n)
{
    float2 q = abs(p) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n) - 1.0;
    return d * min(halfSize.x, halfSize.y);
}

float sdRoundedBox(float2 p, float2 b, float4 r)
{
    r.xy = (p.x > 0.0) ? r.xy : r.wz;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r.x;
}

float4 SampleGradient(PsInput input, float t)
{
    t = saturate(t);
    uint count = input.stopCount;
    if (count == 0) return float4(0, 0, 0, 0);

    float  pos[4];
    float4 col[4];

    pos[0] = input.stop01PosR.x;
    col[0] = float4(input.stop01PosR.yzw, input.stop01AG.x);
    pos[1] = input.stop01AG.y;
    col[1] = float4(input.stop01AG.zw, input.stop12BA.xy);
    pos[2] = input.stop12BA.z;
    col[2] = float4(input.stop12BA.w, input.stop23GB.xyz);
    pos[3] = input.stop23GB.w;
    col[3] = input.stop3Color;

    if (t <= pos[0] || count == 1) return col[0];
    if (t >= pos[count - 1]) return col[count - 1];

    [unroll]
    for (uint i = 0; i < 3; i++)
    {
        if (i + 1 < count && t >= pos[i] && t <= pos[i + 1])
        {
            float range = pos[i + 1] - pos[i];
            float local = (range > 0.0) ? (t - pos[i]) / range : 0.0;
            return lerp(col[i], col[i + 1], local);
        }
    }
    return col[count - 1];
}

float4 main(PsInput input) : SV_Target
{
    float2 halfSize = input.rectSize * 0.5;
    float2 p = input.localPos - halfSize;

    float4 r = float4(
        input.cornerRadius.y,
        input.cornerRadius.x,
        input.cornerRadius.w,
        input.cornerRadius.z);

    float maxR = min(halfSize.x, halfSize.y);
    r = min(r, maxR);

    float dist;
    if (input.shapeParams.x > 0.5)
        dist = sdSuperEllipseRect(p, halfSize, input.shapeParams.y);
    else
        dist = sdRoundedBox(p, halfSize, r);
    float aa = max(fwidth(dist), 0.0001);
    float fillAlpha = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist);

    float4 fill;
    if (input.gradientType == 1)
    {
        float2 start = input.gradGeom.xy;
        float2 end_  = input.gradGeom.zw;
        float2 dir   = end_ - start;
        float lenSq  = dot(dir, dir);
        float t = (lenSq > 0.0) ? dot(input.localPos - start, dir) / lenSq : 0.0;
        fill = SampleGradient(input, t);
    }
    else if (input.gradientType == 2)
    {
        float2 center = input.gradGeom.xy;
        float2 radius = input.gradGeom.zw;
        float2 d = (input.localPos - center) / max(radius, float2(0.001, 0.001));
        float t = length(d);
        fill = SampleGradient(input, t);
    }
    else
    {
        fill = input.fillColor;
    }

    float4 color;
    if (input.borderWidth > 0.0)
    {
        float fillMask = 1.0 - smoothstep(-aa * 0.5, aa * 0.5, dist + input.borderWidth);
        float borderMask = fillAlpha - fillMask;
        color = fill * fillMask + input.borderColor * max(borderMask, 0.0);
        color.a = max(color.a, 0.0);
    }
    else
    {
        color = fill * fillAlpha;
    }

    if (color.a < 1.0 / 255.0) discard;
    return color;
}
)HLSL";

static const char kBitmapTextVS[] = R"HLSL(
cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct GlyphInstance
{
    float2 position;
    float2 size;
    float2 uvMin;
    float2 uvMax;
    float4 color;
};

StructuredBuffer<GlyphInstance> glyphs : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    GlyphInstance g = glyphs[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];
    float2 pixelPos = g.position + corner * g.size;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.uv = lerp(g.uvMin, g.uvMax, corner);
    o.color = g.color;
    return o;
}
)HLSL";

static const char kBitmapTextPS[] = R"HLSL(
Texture2D<float> glyphAtlas : register(t1);
SamplerState glyphSampler : register(s0);

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float4 color   : COLOR0;
};

float4 main(PsInput input) : SV_Target
{
    float alpha = glyphAtlas.Sample(glyphSampler, input.uv);
    float contrast = saturate(alpha * 1.2 - 0.1);
    alpha = lerp(alpha, contrast, 0.3);

    // input.color.rgb is already premultiplied by colorA on the CPU side.
    // Scale both rgb and a by the glyph atlas alpha.
    float4 color = input.color * alpha;

    if (color.a < 1.0 / 255.0) discard;
    return color;
}
)HLSL";

static const char kBitmapQuadVS[] = R"HLSL(
cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct BitmapInstance
{
    float2 position;
    float2 size;
    float2 uvMin;
    float2 uvMax;
    float  opacity;
    float3 _pad;
};

StructuredBuffer<BitmapInstance> bitmaps : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float  opacity : TEXCOORD1;
};

VsOutput main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    BitmapInstance b = bitmaps[instanceId + baseInstanceOffset];
    float2 corner = corners[vertexId];
    float2 pixelPos = b.position + corner * b.size;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.uv = lerp(b.uvMin, b.uvMax, corner);
    o.opacity = b.opacity;
    return o;
}
)HLSL";

static const char kBitmapQuadPS[] = R"HLSL(
Texture2D<float4> bitmapTexture : register(t1);
SamplerState bitmapSampler : register(s0);

struct PsInput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
    float  opacity : TEXCOORD1;
};

float4 main(PsInput input) : SV_Target
{
    float4 color = bitmapTexture.Sample(bitmapSampler, input.uv);
    color *= input.opacity;
    if (color.a < 1.0 / 255.0) discard;
    return color;
}
)HLSL";

static const char kCustomEffectVS[] = R"HLSL(
cbuffer ShaderGeometry : register(b1)
{
    float4 rect;        // x, y, width, height (DIPs)
    float2 screenSize;  // viewport size in DIPs
    float2 _pad;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float2 uv      : TEXCOORD0;
};

VsOutput main(uint vertexId : SV_VertexID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    float2 corner = corners[vertexId];
    float2 pixelPos = rect.xy + corner * rect.zw;

    VsOutput o;
    o.clipPos = float4(
        pixelPos.x / screenSize.x * 2.0 - 1.0,
        1.0 - pixelPos.y / screenSize.y * 2.0,
        0.0, 1.0);
    o.uv = corner;
    return o;
}
)HLSL";

static const char kGaussianBlurCS[] = R"HLSL(
cbuffer BlurConstants : register(b0)
{
    uint  g_Direction;
    float g_Radius;
    uint  g_TexWidth;
    uint  g_TexHeight;
};

Texture2D<float4>   g_Input  : register(t0);
RWTexture2D<float4> g_Output : register(u0);

// All textures and RTV use UNORM (sRGB passthrough) — no gamma conversion needed.
// Identity stubs kept so call sites compile without changes.
float SrgbToLinearCh(float s) { return s; }
float3 SrgbToLinear(float3 s) { return s; }
float LinearToSrgbCh(float l) { return l; }
float3 LinearToSrgb(float3 l) { return l; }

#define MAX_KERNEL_RADIUS 64
#define THREAD_GROUP_SIZE 256
#define CACHE_SIZE (THREAD_GROUP_SIZE + 2 * MAX_KERNEL_RADIUS)

groupshared float4 sharedCache[CACHE_SIZE];

float GaussianWeight(float d, float sigma)
{
    float x = d / max(sigma, 0.0001f);
    return exp(-0.5f * x * x);
}

[numthreads(THREAD_GROUP_SIZE, 1, 1)]
void main(uint3 groupId : SV_GroupID,
          uint  groupIndex : SV_GroupIndex,
          uint3 dispatchId : SV_DispatchThreadID)
{
    int kernelRadius = (int)min(g_Radius, (float)MAX_KERNEL_RADIUS);
    if (kernelRadius < 1) kernelRadius = 1;

    float sigma = g_Radius / 3.0f;
    if (sigma < 0.5f) sigma = 0.5f;

    int lineLen, lineCount;
    if (g_Direction == 0) {
        lineLen   = (int)g_TexWidth;
        lineCount = (int)g_TexHeight;
    } else {
        lineLen   = (int)g_TexHeight;
        lineCount = (int)g_TexWidth;
    }

    int lineIndex = (int)groupId.y;
    if (lineIndex >= lineCount) return;

    int tileStart = (int)groupId.x * THREAD_GROUP_SIZE;
    int cacheBase = tileStart - kernelRadius;

    for (int i = (int)groupIndex; i < THREAD_GROUP_SIZE + 2 * kernelRadius; i += THREAD_GROUP_SIZE)
    {
        int coord = clamp(cacheBase + i, 0, lineLen - 1);
        int2 texCoord;
        if (g_Direction == 0)
            texCoord = int2(coord, lineIndex);
        else
            texCoord = int2(lineIndex, coord);

        float4 sample_ = g_Input.Load(int3(texCoord, 0));
        sharedCache[i] = sample_;
    }

    GroupMemoryBarrierWithGroupSync();

    int pos = tileStart + (int)groupIndex;
    if (pos >= lineLen) return;

    float4 sum = float4(0, 0, 0, 0);
    float  weightSum = 0.0f;
    int cacheCenter = (int)groupIndex + kernelRadius;

    for (int k = -kernelRadius; k <= kernelRadius; k++)
    {
        float w = GaussianWeight((float)k, sigma);
        sum += sharedCache[cacheCenter + k] * w;
        weightSum += w;
    }

    sum /= max(weightSum, 0.0001f);

    int2 outCoord;
    if (g_Direction == 0)
        outCoord = int2(pos, lineIndex);
    else
        outCoord = int2(lineIndex, pos);

    g_Output[outCoord] = sum;
}
)HLSL";

static const char kTriangleVS[] = R"HLSL(
cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

struct VsInput
{
    float2 position : POSITION;
    float4 color    : COLOR0;
};

struct VsOutput
{
    float4 clipPos : SV_Position;
    float4 color   : COLOR0;
};

VsOutput main(VsInput input)
{
    VsOutput o;
    o.clipPos = float4(
        input.position.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - input.position.y * invScreenSize.y * 2.0,
        0.0, 1.0);

    o.color = input.color;
    return o;
}
)HLSL";

static const char kTrianglePS[] = R"HLSL(
struct PsInput
{
    float4 clipPos : SV_Position;
    float4 color   : COLOR0;
};

float4 main(PsInput input) : SV_Target
{
    if (input.color.a < 1.0 / 255.0) discard;
    return input.color;
}
)HLSL";

// ============================================================================
// Liquid Glass — full-screen quad vertex shader (reuses bitmap layout)
// ============================================================================

static const char kLiquidGlassVS[] = R"HLSL(
cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

cbuffer LiquidGlassGeom : register(b2)
{
    float4 glassRect;   // x, y, w, h in pixels
};

struct VsOutput
{
    float4 clipPos   : SV_Position;
    float2 screenPos : TEXCOORD0;   // pixel position on screen
};

VsOutput main(uint vertexId : SV_VertexID)
{
    static const float2 corners[6] = {
        float2(0, 0), float2(1, 0), float2(1, 1),
        float2(0, 0), float2(1, 1), float2(0, 1)
    };

    float2 corner = corners[vertexId];

    // Expand quad by padding for outer shadow + fusion bridge bleed
    float padding = 32.0;
    float2 pos = glassRect.xy - padding + corner * (glassRect.zw + padding * 2.0);

    VsOutput o;
    o.clipPos = float4(
        pos.x * invScreenSize.x * 2.0 - 1.0,
        1.0 - pos.y * invScreenSize.y * 2.0,
        0.0, 1.0);
    o.screenPos = pos;
    return o;
}
)HLSL";

// ============================================================================
// Liquid Glass — pixel shader (SDF refraction, highlight, inner shadow, fusion)
// Ported from the original D2D1 custom effect (liquid_glass_effects.cpp).
// ============================================================================

static const char kLiquidGlassPS[] = R"HLSL(
Texture2D<float4> blurredTex : register(t1);   // Gaussian-blurred background snapshot
SamplerState      linearSamp : register(s0);

cbuffer FrameConstants : register(b0)
{
    float2 screenSize;
    float2 invScreenSize;
};

cbuffer LiquidGlassParams : register(b1)
{
    // Register 0: glass rect
    float4 glassRect;            // x, y, w, h

    // Register 1: refraction params
    float  cornerRadius;
    float  refractionHeight;     // depth zone
    float  refractionAmount;     // UV offset strength
    float  chromaticAberration;

    // Register 2: tint / vibrancy
    float  vibrancy;
    float  tintR, tintG, tintB;

    // Register 3: tint opacity, highlight, light position
    float  tintOpacity;
    float  highlightOpacity;
    float  lightPosX, lightPosY; // screen-space mouse position (-1 = no mouse)

    // Register 4: shadow
    float  shadowOffset;
    float  shadowRadius;
    float  shadowOpacity;
    float  blurTexW;             // blur texture width (for UV mapping)

    // Register 5: screen size + shape
    float  scrW, scrH;
    float  shapeType;            // 0 = RoundedRect, 1 = SuperEllipse
    float  shapeN;               // SuperEllipse exponent

    // Register 6: fusion
    float  neighborCount;
    float  fusionRadius;
    float  blurTexH;             // blur texture height (for UV mapping)
    float  _pad2;

    // Registers 7-10: neighbor rects (x, y, w, h)
    float4 n0Rect, n1Rect, n2Rect, n3Rect;

    // Register 11: neighbor corner radii
    float4 neighborRadii;
};

struct PsInput
{
    float4 clipPos   : SV_Position;
    float2 screenPos : TEXCOORD0;
};

// --- SDF functions (matching original D2D1 implementation) ---

float sdRoundedRect(float2 coord, float2 halfSize, float radius)
{
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

// Numerical gradient for rounded rect (avoids sign() discontinuity artifacts)
float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius)
{
    const float e = 0.5;
    float dx = sdRoundedRect(coord + float2(e, 0), halfSize, radius)
             - sdRoundedRect(coord - float2(e, 0), halfSize, radius);
    float dy = sdRoundedRect(coord + float2(0, e), halfSize, radius)
             - sdRoundedRect(coord - float2(0, e), halfSize, radius);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float sdSuperEllipse(float2 coord, float2 halfSize, float n)
{
    float2 p = abs(coord) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - 1.0;
    return d * min(halfSize.x, halfSize.y);
}

float2 gradSdSuperEllipse(float2 coord, float2 halfSize, float n)
{
    const float e = 0.5;
    float dx = sdSuperEllipse(coord + float2(e, 0), halfSize, n)
             - sdSuperEllipse(coord - float2(e, 0), halfSize, n);
    float dy = sdSuperEllipse(coord + float2(0, e), halfSize, n)
             - sdSuperEllipse(coord - float2(0, e), halfSize, n);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float sdShape(float2 coord, float2 halfSize, float radius)
{
    if (shapeType > 0.5)
        return sdSuperEllipse(coord, halfSize, shapeN);
    return sdRoundedRect(coord, halfSize, radius);
}

float2 gradShape(float2 coord, float2 halfSize, float radius)
{
    if (shapeType > 0.5)
        return gradSdSuperEllipse(coord, halfSize, shapeN);
    return gradSdRoundedRect(coord, halfSize, radius);
}

// Smooth minimum for SDF fusion (polynomial, C1 continuous)
float smin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// Evaluate SDF for a neighbor
float neighborSdf(float2 pixelCoord, float4 nRect, float nRadius)
{
    float2 nCenter = nRect.xy + nRect.zw * 0.5;
    float2 nHalf = nRect.zw * 0.5;
    float nr = min(nRadius, min(nHalf.x, nHalf.y));
    return sdRoundedRect(pixelCoord - nCenter, nHalf, nr);
}

// Combined SDF: self shape + all neighbors via smooth min
float evalCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r)
{
    float d = sdShape(pixelCoord - center, halfSize, r);
    int nCount = (int)neighborCount;
    float k = fusionRadius;

    if (nCount > 0) d = smin(d, neighborSdf(pixelCoord, n0Rect, neighborRadii.x), k);
    if (nCount > 1) d = smin(d, neighborSdf(pixelCoord, n1Rect, neighborRadii.y), k);
    if (nCount > 2) d = smin(d, neighborSdf(pixelCoord, n2Rect, neighborRadii.z), k);
    if (nCount > 3) d = smin(d, neighborSdf(pixelCoord, n3Rect, neighborRadii.w), k);

    return d;
}

// Numerical gradient of the combined SDF
float2 gradCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r)
{
    const float e = 0.5;
    float dx = evalCombinedSd(pixelCoord + float2(e, 0), center, halfSize, r)
             - evalCombinedSd(pixelCoord - float2(e, 0), center, halfSize, r);
    float dy = evalCombinedSd(pixelCoord + float2(0, e), center, halfSize, r)
             - evalCombinedSd(pixelCoord - float2(0, e), center, halfSize, r);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float circleMap(float x)
{
    return 1.0 - sqrt(1.0 - x * x);
}

float3 applyVibrancy(float3 color, float amount)
{
    float luminance = dot(color, float3(0.213, 0.715, 0.072));
    return lerp(float3(luminance, luminance, luminance), color, amount);
}

// All textures and RTV use UNORM (sRGB passthrough) — no gamma conversion needed.
// SrgbToLinear is a no-op identity to avoid changing every call site.
float3 SrgbToLinear(float3 s)
{
    return s;
}

float4 main(PsInput input) : SV_Target
{
    float2 pixelCoord = input.screenPos;
    float2 blurInvSize = 1.0 / float2(blurTexW, blurTexH);

    // Glass panel geometry
    float2 glassCenter = glassRect.xy + glassRect.zw * 0.5;
    float2 halfSize = glassRect.zw * 0.5;
    float2 centered = pixelCoord - glassCenter;
    float r = min(cornerRadius, min(halfSize.x, halfSize.y));
    int nCount = (int)neighborCount;

    // Evaluate combined SDF (self + neighbors via smooth min)
    float sd;
    float selfSd;
    if (nCount > 0) {
        sd = evalCombinedSd(pixelCoord, glassCenter, halfSize, r);
        selfSd = sdShape(centered, halfSize, r);
    } else {
        sd = sdShape(centered, halfSize, r);
        selfSd = sd;
    }

    // Voronoi ownership: pixels outside our body that are closer to a neighbor
    // should be rendered by that neighbor, not us.
    if (nCount > 0 && selfSd > 0.0) {
        float minNSd = 1e10;
        float2 closestNC = float2(0, 0);

        if (nCount > 0) {
            float d = neighborSdf(pixelCoord, n0Rect, neighborRadii.x);
            if (d < minNSd) { minNSd = d; closestNC = n0Rect.xy + n0Rect.zw * 0.5; }
        }
        if (nCount > 1) {
            float d = neighborSdf(pixelCoord, n1Rect, neighborRadii.y);
            if (d < minNSd) { minNSd = d; closestNC = n1Rect.xy + n1Rect.zw * 0.5; }
        }
        if (nCount > 2) {
            float d = neighborSdf(pixelCoord, n2Rect, neighborRadii.z);
            if (d < minNSd) { minNSd = d; closestNC = n2Rect.xy + n2Rect.zw * 0.5; }
        }
        if (nCount > 3) {
            float d = neighborSdf(pixelCoord, n3Rect, neighborRadii.w);
            if (d < minNSd) { minNSd = d; closestNC = n3Rect.xy + n3Rect.zw * 0.5; }
        }

        // Primary: neighbor is clearly closer -> yield
        if (minNSd < selfSd)
            return float4(0, 0, 0, 0);
        // Tie-break: panel whose center is "greater" (X then Y) yields
        if (minNSd - selfSd < 0.5) {
            bool yield_ = (glassCenter.x > closestNC.x + 0.01) ||
                          (abs(glassCenter.x - closestNC.x) <= 0.01 && glassCenter.y > closestNC.y + 0.01);
            if (yield_) return float4(0, 0, 0, 0);
        }
    }

    // Compute AA width early (needed for both outer shadow threshold and glass mask)
    float aaW = max(fwidth(sd), 0.5);

    // === OUTSIDE GLASS: outer shadow ===
    if (sd > aaW) {
        float2 shadowOff = float2(0.0, 4.0);
        float sdShadow;
        if (nCount > 0) {
            sdShadow = evalCombinedSd(pixelCoord - shadowOff, glassCenter, halfSize, r);
        } else {
            sdShadow = sdShape(centered - shadowOff, halfSize, r);
        }
        float outerShadow = 0.0;
        if (sdShadow > 0.0) {
            outerShadow = smoothstep(24.0, 0.0, sdShadow) * 0.1;
        }
        return float4(0, 0, 0, outerShadow);
    }

    // Anti-aliased glass mask — use fwidth for proper 1px AA regardless of SDF gradient magnitude.
    // sdSuperEllipse has non-unit gradient near corners, so fixed smoothstep would be too wide.
    float glassMask = 1.0 - smoothstep(-aaW, aaW, sd);

    // === REFRACTION ===
    float4 refracted;
    float2 baseUV = pixelCoord * blurInvSize;

    if (-sd < refractionHeight) {
        // Near edge: apply refraction displacement
        float sdClamped = min(sd, 0.0);
        float2 grad;
        if (nCount > 0) {
            grad = gradCombinedSd(pixelCoord, glassCenter, halfSize, r);
        } else {
            float gradR = min(r * 1.5, min(halfSize.x, halfSize.y));
            grad = normalize(gradShape(centered, halfSize, gradR));
        }

        float d = circleMap(1.0 - (-sdClamped) / refractionHeight) * (-refractionAmount);

        // Depth effect: add radial component from panel center.
        // For fused panels, fade in bridge area to avoid refraction seam.
        float2 normalizedCenter = centered / max(length(centered), 0.001);
        float depthBlend = (nCount > 0) ? saturate(-selfSd / 8.0) : 1.0;
        grad = normalize(grad + normalizedCenter * depthBlend);

        float2 displacement = d * grad;
        float2 refractedUV = baseUV + displacement * blurInvSize;

        // Chromatic aberration (7-color spectral sampling)
        if (chromaticAberration > 0.01) {
            float dispersionIntensity = chromaticAberration *
                ((centered.x * centered.y) / max(halfSize.x * halfSize.y, 1.0));
            float2 dispersedUV = (d * grad * dispersionIntensity) * blurInvSize;

            refracted = float4(0, 0, 0, 0);

            float4 red    = blurredTex.Sample(linearSamp, refractedUV + dispersedUV);
            red.rgb = SrgbToLinear(red.rgb);
            refracted.r += red.r / 3.5; refracted.a += red.a / 7.0;

            float4 orange = blurredTex.Sample(linearSamp, refractedUV + dispersedUV * (2.0 / 3.0));
            orange.rgb = SrgbToLinear(orange.rgb);
            refracted.r += orange.r / 3.5; refracted.g += orange.g / 7.0; refracted.a += orange.a / 7.0;

            float4 yellow = blurredTex.Sample(linearSamp, refractedUV + dispersedUV * (1.0 / 3.0));
            yellow.rgb = SrgbToLinear(yellow.rgb);
            refracted.r += yellow.r / 3.5; refracted.g += yellow.g / 3.5; refracted.a += yellow.a / 7.0;

            float4 green  = blurredTex.Sample(linearSamp, refractedUV);
            green.rgb = SrgbToLinear(green.rgb);
            refracted.g += green.g / 3.5; refracted.a += green.a / 7.0;

            float4 cyan   = blurredTex.Sample(linearSamp, refractedUV - dispersedUV * (1.0 / 3.0));
            cyan.rgb = SrgbToLinear(cyan.rgb);
            refracted.g += cyan.g / 3.5; refracted.b += cyan.b / 3.0; refracted.a += cyan.a / 7.0;

            float4 blue   = blurredTex.Sample(linearSamp, refractedUV - dispersedUV * (2.0 / 3.0));
            blue.rgb = SrgbToLinear(blue.rgb);
            refracted.b += blue.b / 3.0; refracted.a += blue.a / 7.0;

            float4 purple = blurredTex.Sample(linearSamp, refractedUV - dispersedUV);
            purple.rgb = SrgbToLinear(purple.rgb);
            refracted.r += purple.r / 7.0; refracted.b += purple.b / 3.0; refracted.a += purple.a / 7.0;
        } else {
            refracted = blurredTex.Sample(linearSamp, refractedUV);
            refracted.rgb = SrgbToLinear(refracted.rgb);
        }
    } else {
        // Deep inside: sample blurred background directly
        refracted = blurredTex.Sample(linearSamp, baseUV);
        refracted.rgb = SrgbToLinear(refracted.rgb);
    }

    // === VIBRANCY + TINT ===
    refracted.rgb = applyVibrancy(refracted.rgb, vibrancy);
    // Tint color comes from managed code in sRGB — linearize for blending
    float3 tintLinear = SrgbToLinear(float3(tintR, tintG, tintB));
    refracted.rgb = lerp(refracted.rgb, tintLinear, tintOpacity);

    // === HIGHLIGHT (mouse-following point light) ===
    float edgeDist = -sd;
    float strokeCenter = 0.75;
    float blurSigma = 0.5;
    float strokeIntensity = exp(-((edgeDist - strokeCenter) * (edgeDist - strokeCenter)) / (2.0 * blurSigma * blurSigma));
    float glowIntensity = exp(-(edgeDist * edgeDist) / 18.0) * 0.15;
    float totalHighlight = strokeIntensity + glowIntensity;

    if (totalHighlight > 0.005) {
        float2 hlGrad;
        if (nCount > 0) {
            hlGrad = gradCombinedSd(pixelCoord, glassCenter, halfSize, r);
        } else {
            float gradR2 = min(r * 1.5, min(halfSize.x, halfSize.y));
            hlGrad = normalize(gradShape(centered, halfSize, gradR2));
        }

        float lightMod;
        if (lightPosX >= 0.0) {
            // Mouse-following point light
            float2 lightPos = float2(lightPosX, lightPosY);
            float2 toLight = lightPos - pixelCoord;
            float lightDist = length(toLight);
            float2 lightDir = normalize(toLight);

            // Directional modulation from surface normal vs light direction
            float dirFactor = dot(hlGrad, lightDir);
            lightMod = smoothstep(-0.3, 1.0, dirFactor);

            // Radial falloff: bright near mouse, fading outward
            float falloffRadius = max(halfSize.x, halfSize.y) * 1.5;
            float radialFalloff = 1.0 - saturate(lightDist / falloffRadius);
            radialFalloff = radialFalloff * radialFalloff;

            // Specular-like hotspot near the mouse
            float spec = exp(-(lightDist * lightDist) / (falloffRadius * falloffRadius * 0.15)) * 0.6;

            lightMod = lightMod * (radialFalloff * 0.8 + 0.2) + spec;
        } else {
            // No mouse: dual-corner highlight (top-left + bottom-right)
            float2 lightDir1 = normalize(float2(-1.0, -1.0));
            float dir1 = dot(hlGrad, lightDir1);
            float hl1 = smoothstep(-0.2, 1.0, dir1);

            float2 lightDir2 = normalize(float2(1.0, 1.0));
            float dir2 = dot(hlGrad, lightDir2);
            float hl2 = smoothstep(-0.2, 1.0, dir2) * 0.5;

            lightMod = lerp(0.15, 0.31, max(hl1, hl2));
        }

        float hlAlpha = totalHighlight * lightMod * highlightOpacity;
        refracted.rgb += float3(hlAlpha, hlAlpha, hlAlpha);
    }

    // === INNER SHADOW ===
    float sdOffset;
    if (nCount > 0) {
        sdOffset = evalCombinedSd(pixelCoord + float2(0.0, shadowOffset), glassCenter, halfSize, r);
    } else {
        float2 shOff = float2(0.0, shadowOffset);
        sdOffset = sdShape(centered + shOff, halfSize, r);
    }

    float shIntensity = 0.0;
    if (sdOffset > -shadowRadius) {
        shIntensity = smoothstep(-shadowRadius, 0.0, sdOffset);
    }
    float edgeShadow = smoothstep(shadowRadius, 0.0, -selfSd);
    float edgeMask = smoothstep(0.0, 4.0, -selfSd);
    float totalShadow = max(shIntensity, edgeShadow * 0.2) * shadowOpacity * edgeMask;
    refracted.rgb = lerp(refracted.rgb, float3(0, 0, 0), totalShadow);

    // === COMPOSITE (premultiplied alpha) ===
    float4 result;
    result.rgb = max(refracted.rgb, 0.0) * glassMask;
    result.a = glassMask;
    return result;
}
)HLSL";

} // namespace shader_source
} // namespace jalium
