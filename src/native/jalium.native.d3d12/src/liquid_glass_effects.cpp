#include <initguid.h>
#include "liquid_glass_effects.h"
#include <cstring>
#include <algorithm>

namespace jalium {

// ============================================================================
// Embedded HLSL pixel shader source (consolidated: refraction + highlight +
// inner shadow + composite in a single pass)
// ============================================================================

static const char* s_liquidGlassShaderSource = R"HLSL(
// LiquidGlass.hlsl - Consolidated D2D1 Custom Effect Pixel Shader
// Combines refraction, highlight, inner shadow, and composite in one pass.
// Input 0 (t0): Original background snapshot
// Input 1 (t1): Gaussian-blurred background

Texture2D OriginalBg : register(t0);
Texture2D BlurredBg  : register(t1);
SamplerState Sampler0 : register(s0);

cbuffer constants : register(b0) {
    // Register 0
    float4 glassRect;          // x, y, w, h

    // Register 1
    float  cornerRadius;
    float  refractionHeight;
    float  refractionAmount;
    float  chromaticAberration;

    // Register 2
    float  vibrancy;
    float  tintR;
    float  tintG;
    float  tintB;

    // Register 3
    float  tintOpacity;
    float  highlightOpacity;
    float  lightPosX;       // screen-space mouse X (-1 = no mouse)
    float  lightPosY;       // screen-space mouse Y

    // Register 4
    float  shadowOffset;
    float  shadowRadius;
    float  shadowOpacity;
    float  _pad0;

    // Register 5
    float  screenSizeX;
    float  screenSizeY;
    float  shapeType;       // 0 = RoundedRect, 1 = SuperEllipse
    float  shapeN;          // SuperEllipse exponent (default 4.0)

    // Register 6: Fusion
    float4 fusionParams;    // (neighborCount, fusionRadius, 0, 0)

    // Register 7-10: Neighbor rects (screen-space x, y, w, h)
    float4 neighborRect0;
    float4 neighborRect1;
    float4 neighborRect2;
    float4 neighborRect3;

    // Register 11: Neighbor corner radii
    float4 neighborRadii;
};

// --- SDF functions ---

float sdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius) {
    // Use numerical differentiation to avoid sign() discontinuity at coord.x=0 or coord.y=0.
    // This prevents the vertical/horizontal line artifacts on wide/tall glass panels.
    const float e = 0.5;
    float dx = sdRoundedRect(coord + float2(e, 0), halfSize, radius)
             - sdRoundedRect(coord - float2(e, 0), halfSize, radius);
    float dy = sdRoundedRect(coord + float2(0, e), halfSize, radius)
             - sdRoundedRect(coord - float2(0, e), halfSize, radius);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float sdSuperEllipse(float2 coord, float2 halfSize, float n) {
    float2 p = abs(coord) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - 1.0;
    float scale = min(halfSize.x, halfSize.y);
    return d * scale;
}

float2 gradSdSuperEllipse(float2 coord, float2 halfSize, float n) {
    // Use numerical differentiation to avoid sign() discontinuity at coord.x=0 or coord.y=0.
    const float e = 0.5;
    float dx = sdSuperEllipse(coord + float2(e, 0), halfSize, n)
             - sdSuperEllipse(coord - float2(e, 0), halfSize, n);
    float dy = sdSuperEllipse(coord + float2(0, e), halfSize, n)
             - sdSuperEllipse(coord - float2(0, e), halfSize, n);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float sdShape(float2 coord, float2 halfSize, float radius) {
    if (shapeType > 0.5) {
        return sdSuperEllipse(coord, halfSize, shapeN);
    }
    return sdRoundedRect(coord, halfSize, radius);
}

float2 gradShape(float2 coord, float2 halfSize, float radius) {
    if (shapeType > 0.5) {
        return gradSdSuperEllipse(coord, halfSize, shapeN);
    }
    return gradSdRoundedRect(coord, halfSize, radius);
}

// --- Smooth minimum for SDF fusion (polynomial, C1 continuous) ---

float smin(float a, float b, float k) {
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

// Evaluate SDF for a neighbor (always rounded rect)
float neighborSdf(float2 pixelCoord, float4 nRect, float nRadius) {
    float2 nCenter = nRect.xy + nRect.zw * 0.5;
    float2 nHalf = nRect.zw * 0.5;
    float nr = min(nRadius, min(nHalf.x, nHalf.y));
    return sdRoundedRect(pixelCoord - nCenter, nHalf, nr);
}

// Combined SDF: self shape + all neighbors via smooth min
float evalCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r) {
    float d = sdShape(pixelCoord - center, halfSize, r);

    int nCount = (int)fusionParams.x;
    float k = fusionParams.y;

    if (nCount > 0) d = smin(d, neighborSdf(pixelCoord, neighborRect0, neighborRadii.x), k);
    if (nCount > 1) d = smin(d, neighborSdf(pixelCoord, neighborRect1, neighborRadii.y), k);
    if (nCount > 2) d = smin(d, neighborSdf(pixelCoord, neighborRect2, neighborRadii.z), k);
    if (nCount > 3) d = smin(d, neighborSdf(pixelCoord, neighborRect3, neighborRadii.w), k);

    return d;
}

// Numerical gradient of the combined SDF
float2 gradCombinedSd(float2 pixelCoord, float2 center, float2 halfSize, float r) {
    const float e = 0.5;
    float dx = evalCombinedSd(pixelCoord + float2(e, 0), center, halfSize, r)
             - evalCombinedSd(pixelCoord - float2(e, 0), center, halfSize, r);
    float dy = evalCombinedSd(pixelCoord + float2(0, e), center, halfSize, r)
             - evalCombinedSd(pixelCoord - float2(0, e), center, halfSize, r);
    float2 g = float2(dx, dy);
    float len = length(g);
    return len > 0.001 ? g / len : float2(0, 1);
}

float circleMap(float x) {
    return 1.0 - sqrt(1.0 - x * x);
}

float3 applyVibrancy(float3 color, float amount) {
    float luminance = dot(color, float3(0.213, 0.715, 0.072));
    return lerp(float3(luminance, luminance, luminance), color, amount);
}

float4 main(
    float4 pos       : SV_POSITION,
    float4 posScene  : SCENE_POSITION,
    float4 uv0       : TEXCOORD0,
    float4 uv1       : TEXCOORD1
) : SV_Target {
    float2 pixelCoord = posScene.xy;
    float2 screenSize = float2(screenSizeX, screenSizeY);

    // Glass panel geometry
    float2 glassCenter = glassRect.xy + glassRect.zw * 0.5;
    float2 halfSize = glassRect.zw * 0.5;
    float2 centered = pixelCoord - glassCenter;
    float r = min(cornerRadius, min(halfSize.x, halfSize.y));
    int nCount = (int)fusionParams.x;

    // Evaluate combined SDF (self + neighbors via smooth min)
    // selfSd = this panel's own SDF (for Voronoi ownership + inner shadow clamping)
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
    // should be rendered by that neighbor, not us. This prevents double-rendering
    // in the fusion bridge area.
    if (nCount > 0 && selfSd > 0.0) {
        float minNSd = 1e10;
        float2 closestNC = float2(0, 0);

        if (nCount > 0) {
            float d = neighborSdf(pixelCoord, neighborRect0, neighborRadii.x);
            if (d < minNSd) { minNSd = d; closestNC = neighborRect0.xy + neighborRect0.zw * 0.5; }
        }
        if (nCount > 1) {
            float d = neighborSdf(pixelCoord, neighborRect1, neighborRadii.y);
            if (d < minNSd) { minNSd = d; closestNC = neighborRect1.xy + neighborRect1.zw * 0.5; }
        }
        if (nCount > 2) {
            float d = neighborSdf(pixelCoord, neighborRect2, neighborRadii.z);
            if (d < minNSd) { minNSd = d; closestNC = neighborRect2.xy + neighborRect2.zw * 0.5; }
        }
        if (nCount > 3) {
            float d = neighborSdf(pixelCoord, neighborRect3, neighborRadii.w);
            if (d < minNSd) { minNSd = d; closestNC = neighborRect3.xy + neighborRect3.zw * 0.5; }
        }

        // Primary: neighbor is clearly closer -> yield
        if (minNSd < selfSd) {
            return float4(0, 0, 0, 0);
        }
        // Tie-break: when distances are nearly equal (within 0.5px),
        // the panel whose center is "greater" (X then Y) yields.
        // This ensures exactly one panel renders at each pixel on the
        // Voronoi boundary, eliminating the double-rendering seam.
        if (minNSd - selfSd < 0.5) {
            bool yield_ = (glassCenter.x > closestNC.x + 0.01) ||
                          (abs(glassCenter.x - closestNC.x) <= 0.01 && glassCenter.y > closestNC.y + 0.01);
            if (yield_) return float4(0, 0, 0, 0);
        }
    }

    // === OUTSIDE GLASS: return transparent (preserve render target) ===
    if (sd > 0.5) {
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

    // Anti-aliased glass mask
    float glassMask = smoothstep(0.5, -0.5, sd);

    // === REFRACTION ===
    float4 refracted;

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
        // For fused panels, this vector differs per panel (centered = pixel - ownCenter),
        // so in the bridge area (selfSd > 0) we fade it out to avoid a refraction seam
        // at the Voronoi boundary where the two panels' radial directions are opposite.
        float2 normalizedCenter = centered / max(length(centered), 0.001);
        float depthBlend = (nCount > 0) ? saturate(-selfSd / 8.0) : 1.0;
        grad = normalize(grad + normalizedCenter * depthBlend);

        float2 displacement = d * grad;
        float2 refractedUV = uv1.xy + displacement / screenSize;

        // Chromatic aberration
        if (chromaticAberration > 0.01) {
            float dispersionIntensity = chromaticAberration *
                ((centered.x * centered.y) / max(halfSize.x * halfSize.y, 1.0));
            float2 dispersedUV = (d * grad * dispersionIntensity) / screenSize;

            refracted = float4(0, 0, 0, 0);

            float4 red    = BlurredBg.Sample(Sampler0, refractedUV + dispersedUV);
            refracted.r += red.r / 3.5; refracted.a += red.a / 7.0;

            float4 orange = BlurredBg.Sample(Sampler0, refractedUV + dispersedUV * (2.0 / 3.0));
            refracted.r += orange.r / 3.5; refracted.g += orange.g / 7.0; refracted.a += orange.a / 7.0;

            float4 yellow = BlurredBg.Sample(Sampler0, refractedUV + dispersedUV * (1.0 / 3.0));
            refracted.r += yellow.r / 3.5; refracted.g += yellow.g / 3.5; refracted.a += yellow.a / 7.0;

            float4 green  = BlurredBg.Sample(Sampler0, refractedUV);
            refracted.g += green.g / 3.5; refracted.a += green.a / 7.0;

            float4 cyan   = BlurredBg.Sample(Sampler0, refractedUV - dispersedUV * (1.0 / 3.0));
            refracted.g += cyan.g / 3.5; refracted.b += cyan.b / 3.0; refracted.a += cyan.a / 7.0;

            float4 blue   = BlurredBg.Sample(Sampler0, refractedUV - dispersedUV * (2.0 / 3.0));
            refracted.b += blue.b / 3.0; refracted.a += blue.a / 7.0;

            float4 purple = BlurredBg.Sample(Sampler0, refractedUV - dispersedUV);
            refracted.r += purple.r / 7.0; refracted.b += purple.b / 3.0; refracted.a += purple.a / 7.0;
        } else {
            refracted = BlurredBg.Sample(Sampler0, refractedUV);
        }
    } else {
        // Deep inside: sample blurred background directly
        refracted = BlurredBg.Sample(Sampler0, uv1.xy);
    }

    // === VIBRANCY + TINT ===
    refracted.rgb = applyVibrancy(refracted.rgb, vibrancy);
    refracted.rgb = lerp(refracted.rgb, float3(tintR, tintG, tintB), tintOpacity);

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
            radialFalloff = radialFalloff * radialFalloff; // quadratic falloff

            // Specular-like hotspot near the mouse
            float specular = exp(-(lightDist * lightDist) / (falloffRadius * falloffRadius * 0.15)) * 0.6;

            lightMod = lightMod * (radialFalloff * 0.8 + 0.2) + specular;
        } else {
            // No mouse: dual-corner highlight (top-left + bottom-right)
            // Primary light from upper-left
            float2 lightDir1 = normalize(float2(-1.0, -1.0));
            float dir1 = dot(hlGrad, lightDir1);
            float hl1 = smoothstep(-0.2, 1.0, dir1);

            // Secondary light from lower-right (dimmer)
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
    // Use selfSd (not combined sd) so inner shadow stays within this panel's body.
    // In the fusion bridge area selfSd > 0 -> edgeMask = 0 -> no shadow overflow.
    float edgeShadow = smoothstep(shadowRadius, 0.0, -selfSd);
    float edgeMask = smoothstep(0.0, 4.0, -selfSd);
    float totalShadow = max(shIntensity, edgeShadow * 0.2) * shadowOpacity * edgeMask;
    refracted.rgb = lerp(refracted.rgb, float3(0, 0, 0), totalShadow);

    // === COMPOSITE (premultiplied alpha) ===
    // Glass interior uses alpha = glassMask so SOURCE_OVER compositing gives:
    //   final = refracted * glassMask + dest * (1 - glassMask)
    // This anti-aliases the glass edge against the actual render target content,
    // not the snapshot, avoiding stale-background artifacts.
    float4 result;
    result.rgb = refracted.rgb * glassMask;
    result.a = glassMask;
    return result;
}
)HLSL";

// ============================================================================
// Effect registration XML
// ============================================================================

static const PCWSTR s_liquidGlassXml =
    L"<?xml version='1.0'?>"
    L"<Effect>"
    L"    <Property name='DisplayName' type='string' value='LiquidGlass'/>"
    L"    <Property name='Author' type='string' value='Jalium'/>"
    L"    <Property name='Category' type='string' value='Custom'/>"
    L"    <Property name='Description' type='string' value='Liquid glass refraction effect with highlight and shadow'/>"
    L"    <Inputs>"
    L"        <Input name='Background'/>"
    L"        <Input name='Blurred'/>"
    L"    </Inputs>"
    L"    <Property name='GlassRect' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Glass Rectangle'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,100,100)'/>"
    L"    </Property>"
    L"    <Property name='RefractionParams' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Refraction Parameters'/>"
    L"        <Property name='Default' type='vector4' value='(8,40,60,0)'/>"
    L"    </Property>"
    L"    <Property name='TintColor' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Tint Color'/>"
    L"        <Property name='Default' type='vector4' value='(1.5,0.08,0.08,0.08)'/>"
    L"    </Property>"
    L"    <Property name='TintAndHighlight' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Tint And Highlight'/>"
    L"        <Property name='Default' type='vector4' value='(0.3,0.55,0,-1)'/>"
    L"    </Property>"
    L"    <Property name='ShadowParams' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Shadow Parameters'/>"
    L"        <Property name='Default' type='vector4' value='(3,8,0.12,0)'/>"
    L"    </Property>"
    L"    <Property name='ScreenSize' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Screen Size'/>"
    L"        <Property name='Default' type='vector4' value='(1920,1080,0,0)'/>"
    L"    </Property>"
    L"    <Property name='FusionParams' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Fusion Parameters'/>"
    L"        <Property name='Default' type='vector4' value='(0,30,0,0)'/>"
    L"    </Property>"
    L"    <Property name='NeighborRect0' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Neighbor Rect 0'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,0,0)'/>"
    L"    </Property>"
    L"    <Property name='NeighborRect1' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Neighbor Rect 1'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,0,0)'/>"
    L"    </Property>"
    L"    <Property name='NeighborRect2' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Neighbor Rect 2'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,0,0)'/>"
    L"    </Property>"
    L"    <Property name='NeighborRect3' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Neighbor Rect 3'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,0,0)'/>"
    L"    </Property>"
    L"    <Property name='NeighborRadii' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Neighbor Radii'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,0,0)'/>"
    L"    </Property>"
    L"</Effect>";

// ============================================================================
// Property bindings
// ============================================================================

static const D2D1_PROPERTY_BINDING s_bindings[] = {
    D2D1_VALUE_TYPE_BINDING(L"GlassRect",        &LiquidGlassEffect::SetGlassRect,        &LiquidGlassEffect::GetGlassRect),
    D2D1_VALUE_TYPE_BINDING(L"RefractionParams",  &LiquidGlassEffect::SetRefractionParams,  &LiquidGlassEffect::GetRefractionParams),
    D2D1_VALUE_TYPE_BINDING(L"TintColor",         &LiquidGlassEffect::SetTintColor,         &LiquidGlassEffect::GetTintColor),
    D2D1_VALUE_TYPE_BINDING(L"TintAndHighlight",  &LiquidGlassEffect::SetTintAndHighlight,  &LiquidGlassEffect::GetTintAndHighlight),
    D2D1_VALUE_TYPE_BINDING(L"ShadowParams",      &LiquidGlassEffect::SetShadowParams,      &LiquidGlassEffect::GetShadowParams),
    D2D1_VALUE_TYPE_BINDING(L"ScreenSize",        &LiquidGlassEffect::SetScreenSize,        &LiquidGlassEffect::GetScreenSize),
    D2D1_VALUE_TYPE_BINDING(L"FusionParams",      &LiquidGlassEffect::SetFusionParams,      &LiquidGlassEffect::GetFusionParams),
    D2D1_VALUE_TYPE_BINDING(L"NeighborRect0",     &LiquidGlassEffect::SetNeighborRect0,     &LiquidGlassEffect::GetNeighborRect0),
    D2D1_VALUE_TYPE_BINDING(L"NeighborRect1",     &LiquidGlassEffect::SetNeighborRect1,     &LiquidGlassEffect::GetNeighborRect1),
    D2D1_VALUE_TYPE_BINDING(L"NeighborRect2",     &LiquidGlassEffect::SetNeighborRect2,     &LiquidGlassEffect::GetNeighborRect2),
    D2D1_VALUE_TYPE_BINDING(L"NeighborRect3",     &LiquidGlassEffect::SetNeighborRect3,     &LiquidGlassEffect::GetNeighborRect3),
    D2D1_VALUE_TYPE_BINDING(L"NeighborRadii",     &LiquidGlassEffect::SetNeighborRadii,     &LiquidGlassEffect::GetNeighborRadii),
};

// ============================================================================
// Registration
// ============================================================================

HRESULT LiquidGlassEffect::Register(ID2D1Factory1* factory) {
    return factory->RegisterEffectFromString(
        CLSID_LiquidGlassEffect,
        s_liquidGlassXml,
        s_bindings,
        ARRAYSIZE(s_bindings),
        &LiquidGlassEffect::CreateEffect);
}

HRESULT __stdcall LiquidGlassEffect::CreateEffect(IUnknown** ppEffectImpl) {
    auto* effect = new (std::nothrow) LiquidGlassEffect();
    if (!effect) return E_OUTOFMEMORY;
    *ppEffectImpl = static_cast<ID2D1EffectImpl*>(effect);
    return S_OK;
}

// ============================================================================
// Constructor
// ============================================================================

LiquidGlassEffect::LiquidGlassEffect() {
    // Set defaults
    constants_.cornerRadius = 8.0f;
    constants_.refractionHeight = 40.0f;
    constants_.refractionAmount = 60.0f;
    constants_.chromaticAberration = 0.0f;
    constants_.vibrancy = 1.5f;
    constants_.tintR = 0.08f;
    constants_.tintG = 0.08f;
    constants_.tintB = 0.08f;
    constants_.tintOpacity = 0.3f;
    constants_.highlightOpacity = 0.85f;
    constants_.lightPosX = 0.0f;
    constants_.lightPosY = -1.0f;
    constants_.shadowOffset = 3.0f;
    constants_.shadowRadius = 8.0f;
    constants_.shadowOpacity = 0.12f;
    constants_.screenSizeX = 1920.0f;
    constants_.screenSizeY = 1080.0f;
}

// ============================================================================
// IUnknown
// ============================================================================

ULONG LiquidGlassEffect::AddRef() {
    return InterlockedIncrement(&refCount_);
}

ULONG LiquidGlassEffect::Release() {
    ULONG count = InterlockedDecrement(&refCount_);
    if (count == 0) delete this;
    return count;
}

HRESULT LiquidGlassEffect::QueryInterface(REFIID riid, void** ppOutput) {
    if (!ppOutput) return E_INVALIDARG;
    *ppOutput = nullptr;

    if (riid == __uuidof(ID2D1EffectImpl)) {
        *ppOutput = static_cast<ID2D1EffectImpl*>(this);
    } else if (riid == __uuidof(ID2D1DrawTransform)) {
        *ppOutput = static_cast<ID2D1DrawTransform*>(this);
    } else if (riid == __uuidof(ID2D1Transform)) {
        *ppOutput = static_cast<ID2D1Transform*>(this);
    } else if (riid == __uuidof(ID2D1TransformNode)) {
        *ppOutput = static_cast<ID2D1TransformNode*>(this);
    } else if (riid == __uuidof(IUnknown)) {
        *ppOutput = static_cast<IUnknown*>(static_cast<ID2D1EffectImpl*>(this));
    } else {
        return E_NOINTERFACE;
    }

    AddRef();
    return S_OK;
}

// ============================================================================
// ID2D1EffectImpl
// ============================================================================

HRESULT LiquidGlassEffect::Initialize(ID2D1EffectContext* ctx, ID2D1TransformGraph* graph) {
    // Compile the pixel shader at runtime
    ComPtr<ID3DBlob> shaderBlob;
    ComPtr<ID3DBlob> errorBlob;

    HRESULT hr = D3DCompile(
        s_liquidGlassShaderSource,
        strlen(s_liquidGlassShaderSource),
        "LiquidGlass.hlsl",
        nullptr,  // defines
        nullptr,  // includes
        "main",
        "ps_4_0",
        D3DCOMPILE_OPTIMIZATION_LEVEL3,
        0,
        &shaderBlob,
        &errorBlob);

    if (FAILED(hr)) {
        if (errorBlob) {
            OutputDebugStringA(static_cast<const char*>(errorBlob->GetBufferPointer()));
        }
        return hr;
    }

    // Load the compiled shader into D2D1
    hr = ctx->LoadPixelShader(
        GUID_LiquidGlassPixelShader,
        static_cast<const BYTE*>(shaderBlob->GetBufferPointer()),
        static_cast<UINT32>(shaderBlob->GetBufferSize()));

    if (FAILED(hr)) return hr;

    // Set this transform as the single node in the graph
    return graph->SetSingleTransformNode(static_cast<ID2D1TransformNode*>(this));
}

HRESULT LiquidGlassEffect::PrepareForRender(D2D1_CHANGE_TYPE changeType) {
    if (!drawInfo_) return E_FAIL;

    // Push constant buffer to GPU
    return drawInfo_->SetPixelShaderConstantBuffer(
        reinterpret_cast<const BYTE*>(&constants_),
        sizeof(constants_));
}

// ============================================================================
// ID2D1DrawTransform
// ============================================================================

HRESULT LiquidGlassEffect::SetDrawInfo(ID2D1DrawInfo* drawInfo) {
    drawInfo_ = drawInfo;
    return drawInfo->SetPixelShader(GUID_LiquidGlassPixelShader);
}

// ============================================================================
// ID2D1Transform
// ============================================================================

HRESULT LiquidGlassEffect::MapOutputRectToInputRects(
    const D2D1_RECT_L* pOutputRect,
    D2D1_RECT_L* pInputRects,
    UINT32 inputRectCount) const
{
    // Expand input rects by the maximum refraction displacement so that
    // D2D1 provides enough texture data for displaced UV sampling.
    // Without this, refracted samples near the edge hit the input boundary
    // and get clamped, creating a rounded-rect "clipping mask" artifact.
    // Max displacement = refractionAmount * (1 + chromaticAberration) + margin.
    float maxDisp = constants_.refractionAmount * (1.0f + constants_.chromaticAberration)
                  + constants_.fusionRadius + 20.0f;
    LONG expand = static_cast<LONG>(maxDisp + 1.0f);
    for (UINT32 i = 0; i < inputRectCount; ++i) {
        pInputRects[i].left   = pOutputRect->left   - expand;
        pInputRects[i].top    = pOutputRect->top    - expand;
        pInputRects[i].right  = pOutputRect->right  + expand;
        pInputRects[i].bottom = pOutputRect->bottom + expand;
    }
    return S_OK;
}

HRESULT LiquidGlassEffect::MapInputRectsToOutputRect(
    const D2D1_RECT_L* pInputRects,
    const D2D1_RECT_L* pInputOpaqueSubRects,
    UINT32 inputRectCount,
    D2D1_RECT_L* pOutputRect,
    D2D1_RECT_L* pOutputOpaqueSubRect)
{
    if (inputRectCount < 2) return E_INVALIDARG;

    // Output is the union of both inputs
    pOutputRect->left   = (std::min)(pInputRects[0].left,   pInputRects[1].left);
    pOutputRect->top    = (std::min)(pInputRects[0].top,    pInputRects[1].top);
    pOutputRect->right  = (std::max)(pInputRects[0].right,  pInputRects[1].right);
    pOutputRect->bottom = (std::max)(pInputRects[0].bottom, pInputRects[1].bottom);

    inputRect_ = *pOutputRect;

    // No opaque sub-rect (effect has transparency)
    *pOutputOpaqueSubRect = D2D1_RECT_L{0, 0, 0, 0};
    return S_OK;
}

HRESULT LiquidGlassEffect::MapInvalidRect(
    UINT32 inputIndex,
    D2D1_RECT_L invalidInputRect,
    D2D1_RECT_L* pInvalidOutputRect) const
{
    // Any input change invalidates the entire output (due to refraction displacement)
    *pInvalidOutputRect = inputRect_;
    return S_OK;
}

// ============================================================================
// Property setters/getters
// ============================================================================

HRESULT LiquidGlassEffect::SetGlassRect(D2D1_VECTOR_4F value) {
    constants_.glassX = value.x;
    constants_.glassY = value.y;
    constants_.glassW = value.z;
    constants_.glassH = value.w;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetGlassRect() const {
    return {constants_.glassX, constants_.glassY, constants_.glassW, constants_.glassH};
}

HRESULT LiquidGlassEffect::SetRefractionParams(D2D1_VECTOR_4F value) {
    constants_.cornerRadius = value.x;
    constants_.refractionHeight = value.y;
    constants_.refractionAmount = value.z;
    constants_.chromaticAberration = value.w;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetRefractionParams() const {
    return {constants_.cornerRadius, constants_.refractionHeight, constants_.refractionAmount, constants_.chromaticAberration};
}

HRESULT LiquidGlassEffect::SetTintColor(D2D1_VECTOR_4F value) {
    constants_.vibrancy = value.x;
    constants_.tintR = value.y;
    constants_.tintG = value.z;
    constants_.tintB = value.w;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetTintColor() const {
    return {constants_.vibrancy, constants_.tintR, constants_.tintG, constants_.tintB};
}

HRESULT LiquidGlassEffect::SetTintAndHighlight(D2D1_VECTOR_4F value) {
    constants_.tintOpacity = value.x;
    constants_.highlightOpacity = value.y;
    constants_.lightPosX = value.z;
    constants_.lightPosY = value.w;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetTintAndHighlight() const {
    return {constants_.tintOpacity, constants_.highlightOpacity, constants_.lightPosX, constants_.lightPosY};
}

HRESULT LiquidGlassEffect::SetShadowParams(D2D1_VECTOR_4F value) {
    constants_.shadowOffset = value.x;
    constants_.shadowRadius = value.y;
    constants_.shadowOpacity = value.z;
    constants_._pad0 = 0;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetShadowParams() const {
    return {constants_.shadowOffset, constants_.shadowRadius, constants_.shadowOpacity, 0};
}

HRESULT LiquidGlassEffect::SetScreenSize(D2D1_VECTOR_4F value) {
    constants_.screenSizeX = value.x;
    constants_.screenSizeY = value.y;
    constants_.shapeType = value.z;
    constants_.shapeN = value.w;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetScreenSize() const {
    return {constants_.screenSizeX, constants_.screenSizeY, constants_.shapeType, constants_.shapeN};
}

HRESULT LiquidGlassEffect::SetFusionParams(D2D1_VECTOR_4F value) {
    constants_.neighborCount = value.x;
    constants_.fusionRadius = value.y;
    constants_._pad1 = 0;
    constants_._pad2 = 0;
    return S_OK;
}

D2D1_VECTOR_4F LiquidGlassEffect::GetFusionParams() const {
    return {constants_.neighborCount, constants_.fusionRadius, 0, 0};
}

HRESULT LiquidGlassEffect::SetNeighborRect0(D2D1_VECTOR_4F value) {
    constants_.n0x = value.x; constants_.n0y = value.y;
    constants_.n0w = value.z; constants_.n0h = value.w;
    return S_OK;
}
D2D1_VECTOR_4F LiquidGlassEffect::GetNeighborRect0() const {
    return {constants_.n0x, constants_.n0y, constants_.n0w, constants_.n0h};
}

HRESULT LiquidGlassEffect::SetNeighborRect1(D2D1_VECTOR_4F value) {
    constants_.n1x = value.x; constants_.n1y = value.y;
    constants_.n1w = value.z; constants_.n1h = value.w;
    return S_OK;
}
D2D1_VECTOR_4F LiquidGlassEffect::GetNeighborRect1() const {
    return {constants_.n1x, constants_.n1y, constants_.n1w, constants_.n1h};
}

HRESULT LiquidGlassEffect::SetNeighborRect2(D2D1_VECTOR_4F value) {
    constants_.n2x = value.x; constants_.n2y = value.y;
    constants_.n2w = value.z; constants_.n2h = value.w;
    return S_OK;
}
D2D1_VECTOR_4F LiquidGlassEffect::GetNeighborRect2() const {
    return {constants_.n2x, constants_.n2y, constants_.n2w, constants_.n2h};
}

HRESULT LiquidGlassEffect::SetNeighborRect3(D2D1_VECTOR_4F value) {
    constants_.n3x = value.x; constants_.n3y = value.y;
    constants_.n3w = value.z; constants_.n3h = value.w;
    return S_OK;
}
D2D1_VECTOR_4F LiquidGlassEffect::GetNeighborRect3() const {
    return {constants_.n3x, constants_.n3y, constants_.n3w, constants_.n3h};
}

HRESULT LiquidGlassEffect::SetNeighborRadii(D2D1_VECTOR_4F value) {
    constants_.n0r = value.x; constants_.n1r = value.y;
    constants_.n2r = value.z; constants_.n3r = value.w;
    return S_OK;
}
D2D1_VECTOR_4F LiquidGlassEffect::GetNeighborRadii() const {
    return {constants_.n0r, constants_.n1r, constants_.n2r, constants_.n3r};
}

} // namespace jalium
