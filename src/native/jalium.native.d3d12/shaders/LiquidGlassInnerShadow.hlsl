// LiquidGlassInnerShadow.hlsl - D2D1 Custom Effect Pixel Shader
// Inner shadow using offset SDF for depth perception
// Adapted from AndroidLiquidGlass reference for single-panel D2D1 integration

cbuffer constants : register(b0) {
    float4 glassRect;          // x, y, w, h in scene coordinates
    float  cornerRadius;
    float  shadowOffset;       // Shadow offset downward (default 3px)
    float  shadowRadius;       // Shadow blur radius (default 8px)
    float  shadowOpacity;      // Shadow strength (default 0.12)
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

float4 main(
    float4 pos       : SV_POSITION,
    float4 posScene  : SCENE_POSITION,
    float4 uv0       : TEXCOORD0
) : SV_Target {
    float2 pixelCoord = posScene.xy;

    // Glass panel SDF
    float2 glassCenter = glassRect.xy + glassRect.zw * 0.5;
    float2 halfSize = glassRect.zw * 0.5;
    float2 centered = pixelCoord - glassCenter;
    float r = min(cornerRadius, min(halfSize.x, halfSize.y));
    float sd = sdShape(centered, halfSize, r);

    // Outside glass - transparent
    if (sd > 0.0) {
        discard;
        return float4(0, 0, 0, 0);
    }

    // Inner shadow using offset SDF (simulates light from top)
    float2 offsetVec = float2(0.0, shadowOffset);
    float sdOffset = sdShape(centered + offsetVec, halfSize, r);

    float shadowIntensity = 0.0;
    if (sdOffset > -shadowRadius) {
        shadowIntensity = smoothstep(-shadowRadius, 0.0, sdOffset);
    }

    // Edge shadow
    float edgeShadow = smoothstep(shadowRadius, 0.0, -sd);

    // Mask: fade shadow near edge to avoid highlight overlap
    float edgeMask = smoothstep(0.0, 4.0, -sd);

    float totalShadow = max(shadowIntensity, edgeShadow * 0.2) * shadowOpacity * edgeMask;

    return float4(0, 0, 0, totalShadow);
}
