// LiquidGlassHighlight.hlsl - D2D1 Custom Effect Pixel Shader
// Edge specular highlight with directional lighting
// Adapted from AndroidLiquidGlass reference for single-panel D2D1 integration

cbuffer constants : register(b0) {
    float4 glassRect;          // x, y, w, h in scene coordinates
    float  cornerRadius;
    float  highlightOpacity;   // Overall highlight strength (default 0.55)
    float2 lightDirection;     // Normalized light direction (default 0, -1)
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

float2 gradSdRoundedRect(float2 coord, float2 halfSize, float radius) {
    float2 cornerCoord = abs(coord) - (halfSize - float2(radius, radius));
    if (cornerCoord.x >= 0.0 || cornerCoord.y >= 0.0) {
        return sign(coord) * normalize(max(cornerCoord, float2(0.001, 0.001)));
    } else {
        float gradX = step(cornerCoord.y, cornerCoord.x);
        return sign(coord) * float2(gradX, 1.0 - gradX);
    }
}

float sdSuperEllipse(float2 coord, float2 halfSize, float n) {
    float2 p = abs(coord) / max(halfSize, float2(0.001, 0.001));
    float d = pow(pow(p.x, n) + pow(p.y, n), 1.0 / n) - 1.0;
    float scale = min(halfSize.x, halfSize.y);
    return d * scale;
}

float2 gradSdSuperEllipse(float2 coord, float2 halfSize, float n) {
    float2 p = abs(coord) / max(halfSize, float2(0.001, 0.001));
    float pn = pow(p.x, n) + pow(p.y, n);
    float pnInv = pow(max(pn, 0.0001), 1.0 / n - 1.0);
    float2 g = float2(
        sign(coord.x) * pnInv * pow(max(p.x, 0.0001), n - 1.0) / max(halfSize.x, 0.001),
        sign(coord.y) * pnInv * pow(max(p.y, 0.0001), n - 1.0) / max(halfSize.y, 0.001)
    );
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

    // Only render inside glass
    if (sd > 0.0) {
        discard;
        return float4(0, 0, 0, 0);
    }

    // Edge distance (positive = inside)
    float edgeDist = -sd;

    // Stroke band: thin bright line at edge (~0.75px from edge)
    float strokeCenter = 0.75;
    float blurSigma = 0.5;
    float strokeIntensity = exp(-((edgeDist - strokeCenter) * (edgeDist - strokeCenter)) / (2.0 * blurSigma * blurSigma));

    // Wider subtle glow
    float glowIntensity = exp(-(edgeDist * edgeDist) / (2.0 * 3.0 * 3.0)) * 0.15;

    float totalIntensity = strokeIntensity + glowIntensity;

    if (totalIntensity < 0.005) {
        return float4(0, 0, 0, 0);
    }

    // Surface gradient (outward normal)
    float gradR = min(r * 1.5, min(halfSize.x, halfSize.y));
    float2 grad = normalize(gradShape(centered, halfSize, gradR));

    // Directional lighting
    float2 lightDir = lightDirection;
    float dirFactor = dot(grad, lightDir);
    float lightMod = lerp(0.35, 1.0, smoothstep(-0.2, 1.0, dirFactor));

    float alpha = totalIntensity * lightMod * highlightOpacity;
    return float4(alpha, alpha, alpha, alpha);
}
