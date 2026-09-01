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

// kSdfRectPS intentionally has no embedded duplicate: the active pixel shader
// is precompiled from shaders/sdf_rect.ps.hlsl into d3d12_shader_bytecode.h.
// Keeping one source of truth prevents the continuous-corner math from drifting.

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
    float  samplerIdx;
    float2 _pad;
};

StructuredBuffer<BitmapInstance> bitmaps : register(t0);

cbuffer InstanceOffset : register(b1)
{
    uint baseInstanceOffset;
};

struct VsOutput
{
    float4 clipPos     : SV_Position;
    float2 uv          : TEXCOORD0;
    float  opacity     : TEXCOORD1;
    nointerpolation float samplerIdx : TEXCOORD2;
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
    o.samplerIdx = b.samplerIdx;
    return o;
}
)HLSL";

static const char kBitmapQuadPS[] = R"HLSL(
Texture2D<float4> bitmapTexture : register(t1);
SamplerState linearSampler : register(s0);
SamplerState pointSampler  : register(s1);
SamplerState anisoSampler  : register(s2);

struct PsInput
{
    float4 clipPos     : SV_Position;
    float2 uv          : TEXCOORD0;
    float  opacity     : TEXCOORD1;
    nointerpolation float samplerIdx : TEXCOORD2;
};

float4 main(PsInput input) : SV_Target
{
    int idx = (int)input.samplerIdx;
    float4 color;
    if (idx == 1)
        color = bitmapTexture.Sample(pointSampler, input.uv);
    else if (idx == 2)
        color = bitmapTexture.Sample(anisoSampler, input.uv);
    else
        color = bitmapTexture.Sample(linearSampler, input.uv);
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

float sdContinuousCornerRect(float2 coord, float2 halfSize, float radius, float exponent)
{
    // Straight sides plus one local quarter-Lame patch per corner. Applying a
    // single Lame curve to a wide control visibly bows its entire top/bottom.
    float2 b = max(halfSize, float2(0.0, 0.0));
    float r = min(max(radius, 0.0), min(b.x, b.y));
    float2 ap = abs(coord);
    if (r <= 0.0001) {
        float2 d = ap - b;
        return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
    }

    float2 q = ap - (b - r.xx);
    if (q.x <= 0.0 || q.y <= 0.0)
        return max(ap.x - b.x, ap.y - b.y);

    float n = (exponent >= 2.0 && exponent <= 16.0) ? exponent : 4.0;
    float2 u = max(q / r, float2(0.0, 0.0));
    float lp = pow(pow(u.x, n) + pow(u.y, n), 1.0 / n);
    float lpPower = pow(max(lp, 0.0001), n - 1.0);
    float2 gradient = float2(
        pow(u.x, n - 1.0) / (r * lpPower),
        pow(u.y, n - 1.0) / (r * lpPower));
    return (lp - 1.0) / max(length(gradient), 0.0001);
}

float2 gradSdContinuousCornerRect(float2 coord, float2 halfSize, float radius, float n)
{
    const float e = 0.5;
    float dx = sdContinuousCornerRect(coord + float2(e, 0), halfSize, radius, n)
             - sdContinuousCornerRect(coord - float2(e, 0), halfSize, radius, n);
    float dy = sdContinuousCornerRect(coord + float2(0, e), halfSize, radius, n)
             - sdContinuousCornerRect(coord - float2(0, e), halfSize, radius, n);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float sdShape(float2 coord, float2 halfSize, float radius)
{
    if (shapeType > 0.5)
        return sdContinuousCornerRect(coord, halfSize, radius, shapeN);
    return sdRoundedRect(coord, halfSize, radius);
}

float2 gradShape(float2 coord, float2 halfSize, float radius)
{
    if (shapeType > 0.5)
        return gradSdContinuousCornerRect(coord, halfSize, radius, shapeN);
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
    // fwidth keeps the one-pixel transition stable through corner and fusion gradients.
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
            float gradR = (shapeType > 0.5) ? r : min(r * 1.5, min(halfSize.x, halfSize.y));
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
            float gradR2 = (shapeType > 0.5) ? r : min(r * 1.5, min(halfSize.x, halfSize.y));
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

// ============================================================================
// Vello blur / filter compute shaders (16x16 thread groups, 1 pixel per thread)
// All use root sig: b0=constants, t0=SRV Texture2D, u0=UAV RWTexture2D
// ============================================================================

static const char kVelloBlurHorizCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight, kernelSize, pad0; float kernel[16]; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    int r = (int)kernelSize / 2;
    float4 sum = float4(0,0,0,0);
    for (int k = -r; k <= r; k++) {
        int sx = clamp((int)dtid.x + k, 0, (int)texWidth - 1);
        sum += g_Input.Load(int3(sx, dtid.y, 0)) * kernel[k + r];
    }
    g_Output[dtid.xy] = sum;
}
)HLSL";

static const char kVelloBlurVertCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight, kernelSize, pad0; float kernel[16]; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    int r = (int)kernelSize / 2;
    float4 sum = float4(0,0,0,0);
    for (int k = -r; k <= r; k++) {
        int sy = clamp((int)dtid.y + k, 0, (int)texHeight - 1);
        sum += g_Input.Load(int3(dtid.x, sy, 0)) * kernel[k + r];
    }
    g_Output[dtid.xy] = sum;
}
)HLSL";

static const char kVelloDownsampleCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight, srcWidth, srcHeight; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    int sx = min((int)dtid.x * 2, (int)srcWidth - 1);
    int sy = min((int)dtid.y * 2, (int)srcHeight - 1);
    int sx1 = min(sx + 1, (int)srcWidth - 1);
    int sy1 = min(sy + 1, (int)srcHeight - 1);
    float4 a = g_Input.Load(int3(sx, sy, 0));
    float4 b = g_Input.Load(int3(sx1, sy, 0));
    float4 c = g_Input.Load(int3(sx, sy1, 0));
    float4 d = g_Input.Load(int3(sx1, sy1, 0));
    g_Output[dtid.xy] = (a + b + c + d) * 0.25;
}
)HLSL";

static const char kVelloUpsampleCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight, srcWidth, srcHeight; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    float sx = ((float)dtid.x + 0.5) * (float)srcWidth / (float)texWidth - 0.5;
    float sy = ((float)dtid.y + 0.5) * (float)srcHeight / (float)texHeight - 0.5;
    int ix = (int)floor(sx); int iy = (int)floor(sy);
    float fx = sx - (float)ix; float fy = sy - (float)iy;
    int ix1 = min(ix + 1, (int)srcWidth - 1);
    int iy1 = min(iy + 1, (int)srcHeight - 1);
    ix = clamp(ix, 0, (int)srcWidth - 1);
    iy = clamp(iy, 0, (int)srcHeight - 1);
    float4 a = g_Input.Load(int3(ix, iy, 0));
    float4 b = g_Input.Load(int3(ix1, iy, 0));
    float4 c = g_Input.Load(int3(ix, iy1, 0));
    float4 d = g_Input.Load(int3(ix1, iy1, 0));
    g_Output[dtid.xy] = lerp(lerp(a, b, fx), lerp(c, d, fx), fy);
}
)HLSL";

static const char kVelloColorMatrixCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight, p0, p1; float4 row0, row1, row2, row3; float4 offsets; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    float4 c = g_Input.Load(int3(dtid.xy, 0));
    float4 o;
    o.r = dot(c, row0) + offsets.x;
    o.g = dot(c, row1) + offsets.y;
    o.b = dot(c, row2) + offsets.z;
    o.a = dot(c, row3) + offsets.w;
    g_Output[dtid.xy] = saturate(o);
}
)HLSL";

static const char kVelloOffsetCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight; int offsetX, offsetY; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    int sx = (int)dtid.x - offsetX;
    int sy = (int)dtid.y - offsetY;
    if (sx >= 0 && sx < (int)texWidth && sy >= 0 && sy < (int)texHeight)
        g_Output[dtid.xy] = g_Input.Load(int3(sx, sy, 0));
    else
        g_Output[dtid.xy] = float4(0,0,0,0);
}
)HLSL";

static const char kVelloMorphologyCS[] = R"HLSL(
cbuffer CB : register(b0) { uint texWidth, texHeight; int radiusX, radiusY; uint isDilate, p0, p1, p2; };
Texture2D<float4> g_Input : register(t0);
RWTexture2D<float4> g_Output : register(u0);
[numthreads(16,16,1)]
void main(uint3 dtid : SV_DispatchThreadID) {
    if (dtid.x >= texWidth || dtid.y >= texHeight) return;
    float4 result = g_Input.Load(int3(dtid.xy, 0));
    for (int dy = -radiusY; dy <= radiusY; dy++) {
        for (int dx = -radiusX; dx <= radiusX; dx++) {
            int sx = clamp((int)dtid.x + dx, 0, (int)texWidth - 1);
            int sy = clamp((int)dtid.y + dy, 0, (int)texHeight - 1);
            float4 s = g_Input.Load(int3(sx, sy, 0));
            if (isDilate) result = max(result, s);
            else result = min(result, s);
        }
    }
    g_Output[dtid.xy] = result;
}
)HLSL";

} // namespace shader_source
} // namespace jalium
