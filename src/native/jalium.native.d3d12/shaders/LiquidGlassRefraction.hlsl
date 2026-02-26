// LiquidGlassRefraction.hlsl - D2D1 Custom Effect Pixel Shader
// Core liquid glass refraction with SDF-based UV displacement and chromatic aberration
// Adapted from AndroidLiquidGlass reference for single-panel D2D1 integration

// D2D1 custom effect inputs
Texture2D InputTexture : register(t0);    // Blurred background
SamplerState InputSampler : register(s0);

cbuffer constants : register(b0) {
    float4 glassRect;          // x, y, w, h in scene coordinates
    float  cornerRadius;
    float  refractionHeight;   // Depth zone for refraction (default 40px)
    float  refractionAmount;   // UV displacement strength (default 60px)
    float  chromaticAberration;// Color dispersion (0-1)
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

float circleMap(float x) {
    return 1.0 - sqrt(1.0 - x * x);
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

    // Outside the glass shape - transparent
    if (sd > 0.0) {
        discard;
        return float4(0, 0, 0, 0);
    }

    // Deep inside -> sample blurred background directly
    if (-sd >= refractionHeight) {
        return InputTexture.Sample(InputSampler, uv0.xy);
    }

    sd = min(sd, 0.0);

    // Compute surface gradient (outward normal)
    float gradR = min(r * 1.5, min(halfSize.x, halfSize.y));
    float2 grad = normalize(gradShape(centered, halfSize, gradR));

    // Refraction displacement via circle map
    float d = circleMap(1.0 - (-sd) / refractionHeight) * (-refractionAmount);

    // Add depth effect: radial component from center
    float2 normalizedCenter = centered / max(length(centered), 0.001);
    grad = normalize(grad + normalizedCenter);

    float2 displacement = d * grad;
    float2 refractedUV = uv0.xy + displacement / screenSize;

    // Chromatic aberration
    if (chromaticAberration > 0.01) {
        float dispersionIntensity = chromaticAberration *
            ((centered.x * centered.y) / max(halfSize.x * halfSize.y, 1.0));
        float2 dispersedUV = (d * grad * dispersionIntensity) / screenSize;

        float4 color = float4(0, 0, 0, 0);

        float4 red    = InputTexture.Sample(InputSampler, refractedUV + dispersedUV);
        color.r += red.r / 3.5; color.a += red.a / 7.0;

        float4 orange = InputTexture.Sample(InputSampler, refractedUV + dispersedUV * (2.0 / 3.0));
        color.r += orange.r / 3.5; color.g += orange.g / 7.0; color.a += orange.a / 7.0;

        float4 yellow = InputTexture.Sample(InputSampler, refractedUV + dispersedUV * (1.0 / 3.0));
        color.r += yellow.r / 3.5; color.g += yellow.g / 3.5; color.a += yellow.a / 7.0;

        float4 green  = InputTexture.Sample(InputSampler, refractedUV);
        color.g += green.g / 3.5; color.a += green.a / 7.0;

        float4 cyan   = InputTexture.Sample(InputSampler, refractedUV - dispersedUV * (1.0 / 3.0));
        color.g += cyan.g / 3.5; color.b += cyan.b / 3.0; color.a += cyan.a / 7.0;

        float4 blue   = InputTexture.Sample(InputSampler, refractedUV - dispersedUV * (2.0 / 3.0));
        color.b += blue.b / 3.0; color.a += blue.a / 7.0;

        float4 purple = InputTexture.Sample(InputSampler, refractedUV - dispersedUV);
        color.r += purple.r / 7.0; color.b += purple.b / 3.0; color.a += purple.a / 7.0;

        return color;
    }

    return InputTexture.Sample(InputSampler, refractedUV);
}
