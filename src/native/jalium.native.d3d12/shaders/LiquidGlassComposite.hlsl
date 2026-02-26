// LiquidGlassComposite.hlsl - D2D1 Custom Effect Pixel Shader
// Final compositing of all liquid glass layers
// Adapted from AndroidLiquidGlass reference for single-panel D2D1 integration

// D2D1 custom effect inputs (4 textures)
Texture2D BackgroundTex  : register(t0);   // Original background (snapshot)
Texture2D RefractionTex  : register(t1);   // Refracted content
Texture2D HighlightTex   : register(t2);   // Edge highlights
Texture2D InnerShadowTex : register(t3);   // Inner shadows
SamplerState Sampler0 : register(s0);

cbuffer constants : register(b0) {
    float4 glassRect;          // x, y, w, h in scene coordinates
    float  cornerRadius;
    float  vibrancy;           // Saturation enhancement (default 1.5)
    float2 _pad0;
    float4 tintColor;          // RGBA tint overlay
    float2 screenSize;
    float  shapeType;          // 0 = RoundedRect, 1 = SuperEllipse
    float  shapeN;             // SuperEllipse exponent (default 4.0)
};

// --- SDF functions ---

float sdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    float outside = length(max(cornerCoord, 0.0)) - radius;
    float inside = min(max(cornerCoord.x, cornerCoord.y), 0.0);
    return outside + inside;
}

float sdSuperEllipse(float2 coord, float2 halfSize, float n) {
    float2 p = abs(coord) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - 1.0;
    float scale = min(halfSize.x, halfSize.y);
    return d * scale;
}

float sdShape(float2 coord, float2 halfSize, float radius) {
    if (shapeType > 0.5) {
        return sdSuperEllipse(coord, halfSize, shapeN);
    }
    return sdRoundedRect(coord, halfSize, radius);
}

float3 applyVibrancy(float3 color, float amount) {
    float luminance = dot(color, float3(0.213, 0.715, 0.072));
    return lerp(float3(luminance, luminance, luminance), color, amount);
}

float4 main(
    float4 pos       : SV_POSITION,
    float4 posScene  : SCENE_POSITION,
    float4 uv0       : TEXCOORD0,
    float4 uv1       : TEXCOORD1,
    float4 uv2       : TEXCOORD2,
    float4 uv3       : TEXCOORD3
) : SV_Target {
    float2 pixelCoord = posScene.xy;

    // Glass panel SDF
    float2 glassCenter = glassRect.xy + glassRect.zw * 0.5;
    float2 halfSize = glassRect.zw * 0.5;
    float r = min(cornerRadius, min(halfSize.x, halfSize.y));

    // Background
    float4 background = BackgroundTex.Sample(Sampler0, uv0.xy);

    // Outer shadow (offset SDF evaluated at current pixel)
    float2 shadowOffset = float2(0.0, 4.0);
    float2 shadowCentered = pixelCoord - shadowOffset - glassCenter;
    float sdShadow = sdShape(shadowCentered, halfSize, r);
    if (sdShadow > 0.0) {
        float outerShadow = smoothstep(24.0, 0.0, sdShadow) * 0.1;
        background.rgb *= (1.0 - outerShadow);
    }

    // Glass mask (anti-aliased)
    float2 centered = pixelCoord - glassCenter;
    float sd = sdShape(centered, halfSize, r);

    if (sd > 0.5) {
        return background;
    }

    float glassMask = smoothstep(0.5, -0.5, sd);

    // Refraction layer
    float4 refraction = RefractionTex.Sample(Sampler0, uv1.xy);

    // Apply vibrancy (saturation enhancement)
    refraction.rgb = applyVibrancy(refraction.rgb, vibrancy);

    // Surface tint
    refraction.rgb += tintColor.rgb * tintColor.a;

    // Highlight (additive blend)
    float4 highlight = HighlightTex.Sample(Sampler0, uv2.xy);
    refraction.rgb += highlight.rgb;

    // Inner shadow (subtractive blend)
    float4 innerShadow = InnerShadowTex.Sample(Sampler0, uv3.xy);
    refraction.rgb = lerp(refraction.rgb, float3(0, 0, 0), innerShadow.a);

    // Final composite
    float4 result;
    result.rgb = lerp(background.rgb, refraction.rgb, glassMask);
    result.a = 1.0;
    return result;
}
