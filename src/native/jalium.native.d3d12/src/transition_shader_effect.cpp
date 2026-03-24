#include <initguid.h>
#include "transition_shader_effect.h"
#include <algorithm>
#include <cstring>
#include <cmath>

namespace jalium {

// ============================================================================
// Embedded HLSL pixel shader source (ps_4_0)
// Combines all 10 transition modes into a single shader with mode branching.
// ============================================================================

static const char* s_transitionShaderSource = R"HLSL(

Texture2D OldContent : register(t0);
SamplerState OldSampler : register(s0);
Texture2D NewContent : register(t1);
SamplerState NewSampler : register(s1);

cbuffer constants : register(b0)
{
    float progress;
    float mode;
    float resolutionX;
    float resolutionY;
};

// ---- Utility functions ----

float hash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float noise2D(float2 st)
{
    float2 i = floor(st);
    float2 f = frac(st);
    float a = hash(i);
    float b = hash(i + float2(1.0, 0.0));
    float c = hash(i + float2(0.0, 1.0));
    float d = hash(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

// ---- Mode 0: Dissolve ----

float4 DoDissolve(float2 uv)
{
    float n = noise2D(uv * 40.0);
    float threshold = progress * 1.2 - 0.1;
    float edge = smoothstep(threshold - 0.05, threshold + 0.05, n);
    float4 oldColor = OldContent.Sample(OldSampler, uv);
    float4 newColor = NewContent.Sample(NewSampler, uv);
    float edgeMask = smoothstep(threshold - 0.08, threshold - 0.03, n) *
                     (1.0 - smoothstep(threshold - 0.03, threshold + 0.02, n));
    float3 edgeColor = float3(1.0, 0.5, 0.1) * edgeMask * 2.0;
    float4 result = lerp(newColor, oldColor, edge);
    result.rgb += edgeColor;
    return result;
}

// ---- Mode 1: Pixelate ----

float4 DoPixelate(float2 uv)
{
    float2 res = float2(resolutionX, resolutionY);
    float maxBlock = 40.0;
    float blockSize = max(1.0, maxBlock * sin(progress * 3.14159));
    float2 blockUV = floor(uv * res / blockSize) * blockSize / res;

    if (progress < 0.5)
        return OldContent.Sample(OldSampler, blockUV);
    else
        return NewContent.Sample(NewSampler, blockUV);
}

// ---- Mode 2: Glitch ----

float4 DoGlitch(float2 uv)
{
    float intensity = sin(progress * 3.14159) * 0.8 + 0.2;

    float lineNoise = hash(float2(floor(uv.y * 30.0), floor(progress * 20.0)));
    float displacement = (lineNoise - 0.5) * 0.15 * intensity;

    float shift = displacement * intensity;
    float2 uvR = uv + float2(shift, 0);
    float2 uvG = uv;
    float2 uvB = uv - float2(shift, 0);

    float blockSwitch = hash(float2(floor(uv.x * 8.0), floor(uv.y * 12.0 + progress * 5.0)));
    bool useNew = blockSwitch < progress;

    float r, g, b;
    if (useNew)
    {
        r = NewContent.Sample(NewSampler, uvR).r;
        g = NewContent.Sample(NewSampler, uvG).g;
        b = NewContent.Sample(NewSampler, uvB).b;
    }
    else
    {
        r = OldContent.Sample(OldSampler, uvR).r;
        g = OldContent.Sample(OldSampler, uvG).g;
        b = OldContent.Sample(OldSampler, uvB).b;
    }

    float2 res = float2(resolutionX, resolutionY);
    float scanline = sin(uv.y * res.y * 2.0) * 0.03 * intensity;

    return float4(r + scanline, g + scanline, b + scanline, 1.0);
}

// ---- Mode 3: ChromaticSplit ----

float4 DoChromaticSplit(float2 uv)
{
    float spread = (1.0 - progress) * 0.08;

    float oldR = OldContent.Sample(OldSampler, uv + float2(spread, spread * 0.5)).r;
    float oldG = OldContent.Sample(OldSampler, uv).g;
    float oldB = OldContent.Sample(OldSampler, uv - float2(spread, spread * 0.5)).b;
    float4 oldSplit = float4(oldR, oldG, oldB, 1.0);

    float newSpread = progress * 0.08;
    float newR = NewContent.Sample(NewSampler, uv + float2(newSpread, newSpread * 0.5)).r;
    float newG = NewContent.Sample(NewSampler, uv).g;
    float newB = NewContent.Sample(NewSampler, uv - float2(newSpread, newSpread * 0.5)).b;
    float4 newSplit = float4(newR, newG, newB, 1.0);

    return lerp(oldSplit, newSplit, progress);
}

// ---- Mode 4: LiquidMorph ----

float4 DoLiquidMorph(float2 uv)
{
    float time = progress * 6.28318;
    float strength = sin(progress * 3.14159) * 0.12;

    float2 distortion = float2(
        sin(uv.y * 15.0 + time) * strength,
        cos(uv.x * 15.0 + time * 1.3) * strength
    );

    distortion += float2(
        sin(uv.y * 8.0 - time * 0.7) * strength * 0.5,
        cos(uv.x * 8.0 - time * 0.5) * strength * 0.5
    );

    float4 oldColor = OldContent.Sample(OldSampler, uv + distortion);
    float4 newColor = NewContent.Sample(NewSampler, uv - distortion * 0.5);

    float blend = smoothstep(0.2, 0.8, progress);
    return lerp(oldColor, newColor, blend);
}

// ---- Mode 5: WaveDistortion ----

float4 DoWaveDistortion(float2 uv)
{
    float amplitude = sin(progress * 3.14159) * 0.15;
    float frequency = 8.0;
    float speed = progress * 12.56636;

    float wave = sin(uv.y * frequency + speed) * amplitude;
    float2 oldUV = uv + float2(wave, wave * 0.3);
    float2 newUV = uv - float2(wave * 0.5, wave * 0.2);

    float4 oldColor = OldContent.Sample(OldSampler, oldUV);
    float4 newColor = NewContent.Sample(NewSampler, newUV);

    return lerp(oldColor, newColor, progress);
}

// ---- Mode 6: WindBlow ----

float4 DoWindBlow(float2 uv)
{
    float columnNoise = hash(float2(floor(uv.x * 30.0), 0));
    float rowNoise = hash(float2(0, floor(uv.y * 50.0)));

    float threshold = progress * 1.5 - columnNoise * 0.3 - rowNoise * 0.2;

    if (threshold > 0.5)
    {
        return NewContent.Sample(NewSampler, uv);
    }

    float displ = max(0, threshold) * 0.5;
    float2 blownUV = uv + float2(
        displ * (1.0 + columnNoise),
        displ * 0.3 * sin(uv.y * 20.0)
    );

    blownUV = clamp(blownUV, 0.0, 1.0);

    float4 oldColor = OldContent.Sample(OldSampler, blownUV);
    oldColor.a *= 1.0 - displ * 2.0;

    float4 newColor = NewContent.Sample(NewSampler, uv);
    return lerp(newColor, oldColor, oldColor.a);
}

// ---- Mode 7: RippleReveal ----

float4 DoRippleReveal(float2 uv)
{
    float2 center = float2(0.5, 0.5);
    float dist = length(uv - center);
    float maxDist = length(float2(0.5, 0.5));

    float rippleRadius = progress * maxDist * 1.3;
    float rippleWidth = 0.08;

    float mask = smoothstep(rippleRadius, rippleRadius - rippleWidth, dist);

    float rippleDist = abs(dist - rippleRadius);
    float waveStrength = (1.0 - smoothstep(0.0, rippleWidth * 2.0, rippleDist))
                       * sin(rippleDist * 60.0) * 0.015
                       * (1.0 - progress);
    float2 waveOffset = normalize(uv - center + 0.001) * waveStrength;

    float4 oldColor = OldContent.Sample(OldSampler, uv + waveOffset);
    float4 newColor = NewContent.Sample(NewSampler, uv);

    return lerp(oldColor, newColor, mask);
}

// ---- Mode 8: ClockWipe ----

float4 DoClockWipe(float2 uv)
{
    float2 center = float2(0.5, 0.5);
    float2 dir = uv - center;

    float angle = atan2(dir.x, -dir.y);
    float normalizedAngle = (angle + 3.14159) / 6.28318;

    float threshold = progress;

    if (normalizedAngle < threshold)
        return NewContent.Sample(NewSampler, uv);
    else
        return OldContent.Sample(OldSampler, uv);
}

// ---- Mode 9: ThermalFade ----

float4 DoThermalFade(float2 uv)
{
    float4 oldColor = OldContent.Sample(OldSampler, uv);
    float4 newColor = NewContent.Sample(NewSampler, uv);

    float lum = dot(oldColor.rgb, float3(0.299, 0.587, 0.114));

    float3 thermal;
    if (lum < 0.2)
        thermal = lerp(float3(0, 0, 0.5), float3(0, 0, 1), lum / 0.2);
    else if (lum < 0.4)
        thermal = lerp(float3(0, 0, 1), float3(0, 1, 0), (lum - 0.2) / 0.2);
    else if (lum < 0.6)
        thermal = lerp(float3(0, 1, 0), float3(1, 1, 0), (lum - 0.4) / 0.2);
    else if (lum < 0.8)
        thermal = lerp(float3(1, 1, 0), float3(1, 0, 0), (lum - 0.6) / 0.2);
    else
        thermal = lerp(float3(1, 0, 0), float3(1, 1, 1), (lum - 0.8) / 0.2);

    float4 result;
    if (progress < 0.4)
    {
        float t = progress / 0.4;
        result = lerp(oldColor, float4(thermal, 1), t);
    }
    else if (progress < 0.6)
    {
        float t = (progress - 0.4) / 0.2;
        float glow = t * 0.3;
        result = float4(thermal + glow, 1);
    }
    else
    {
        float t = (progress - 0.6) / 0.4;
        float newLum = dot(newColor.rgb, float3(0.299, 0.587, 0.114));
        float3 newThermal = float3(1, max(0.5, newLum), newLum * 0.5);
        float4 thermalNew = lerp(float4(newThermal, 1), newColor, t);
        result = lerp(float4(thermal, 1), thermalNew, t);
    }

    return result;
}

// ---- Main entry point ----

float4 main(
    float4 pos       : SV_POSITION,
    float4 scenePos  : SCENE_POSITION,
    float2 uv0       : TEXCOORD0,
    float2 uv1       : TEXCOORD1
) : SV_Target
{
    // Use uv0 for OldContent, uv1 for NewContent to handle inputs
    // with different sizes/offsets correctly in the D2D1 effect graph.
    float2 uv = uv0;
    // Note: Individual Do*() functions use 'uv' for both textures, which is
    // correct when both inputs share the same coordinate space (same size/offset).
    int m = (int)mode;

    if (m == 0) return DoDissolve(uv);
    if (m == 1) return DoPixelate(uv);
    if (m == 2) return DoGlitch(uv);
    if (m == 3) return DoChromaticSplit(uv);
    if (m == 4) return DoLiquidMorph(uv);
    if (m == 5) return DoWaveDistortion(uv);
    if (m == 6) return DoWindBlow(uv);
    if (m == 7) return DoRippleReveal(uv);
    if (m == 8) return DoClockWipe(uv);
    if (m == 9) return DoThermalFade(uv);

    // Fallback: simple crossfade
    float4 oldColor = OldContent.Sample(OldSampler, uv);
    float4 newColor = NewContent.Sample(NewSampler, uv);
    return lerp(oldColor, newColor, progress);
}

)HLSL";

// ============================================================================
// Effect XML registration
// ============================================================================

static const PCWSTR s_transitionEffectXml =
    L"<?xml version='1.0'?>"
    L"<Effect>"
    L"    <Property name='DisplayName' type='string' value='TransitionShader'/>"
    L"    <Property name='Author' type='string' value='Jalium'/>"
    L"    <Property name='Category' type='string' value='Custom'/>"
    L"    <Property name='Description' type='string' value='Content transition blend effect with 10 shader modes'/>"
    L"    <Inputs>"
    L"        <Input name='OldContent'/>"
    L"        <Input name='NewContent'/>"
    L"    </Inputs>"
    L"    <Property name='TransitionParams' type='vector4'>"
    L"        <Property name='DisplayName' type='string' value='Transition Parameters'/>"
    L"        <Property name='Default' type='vector4' value='(0,0,800,600)'/>"
    L"    </Property>"
    L"</Effect>";

// ============================================================================
// Property bindings
// ============================================================================

static const D2D1_PROPERTY_BINDING s_transitionBindings[] = {
    D2D1_VALUE_TYPE_BINDING(L"TransitionParams", &TransitionShaderEffect::SetTransitionParams, &TransitionShaderEffect::GetTransitionParams),
};

// ============================================================================
// Registration
// ============================================================================

HRESULT TransitionShaderEffect::Register(ID2D1Factory1* factory) {
    return factory->RegisterEffectFromString(
        CLSID_TransitionShaderEffect,
        s_transitionEffectXml,
        s_transitionBindings,
        ARRAYSIZE(s_transitionBindings),
        &TransitionShaderEffect::CreateEffect);
}

HRESULT __stdcall TransitionShaderEffect::CreateEffect(IUnknown** ppEffectImpl) {
    auto* effect = new (std::nothrow) TransitionShaderEffect();
    if (!effect) return E_OUTOFMEMORY;
    *ppEffectImpl = static_cast<ID2D1EffectImpl*>(effect);
    return S_OK;
}

// ============================================================================
// Constructor
// ============================================================================

TransitionShaderEffect::TransitionShaderEffect() {
    constants_.progress = 0.0f;
    constants_.mode = 0.0f;
    constants_.resolutionX = 800.0f;
    constants_.resolutionY = 600.0f;
}

// ============================================================================
// IUnknown
// ============================================================================

ULONG TransitionShaderEffect::AddRef() {
    return InterlockedIncrement(&refCount_);
}

ULONG TransitionShaderEffect::Release() {
    ULONG count = InterlockedDecrement(&refCount_);
    if (count == 0) delete this;
    return count;
}

HRESULT TransitionShaderEffect::QueryInterface(REFIID riid, void** ppOutput) {
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

HRESULT TransitionShaderEffect::Initialize(ID2D1EffectContext* ctx, ID2D1TransformGraph* graph) {
    // Compile the pixel shader at runtime
    ComPtr<ID3DBlob> shaderBlob;
    ComPtr<ID3DBlob> errorBlob;

    HRESULT hr = D3DCompile(
        s_transitionShaderSource,
        strlen(s_transitionShaderSource),
        "TransitionShader.hlsl",
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
            OutputDebugStringA("TransitionShader compile error: ");
            OutputDebugStringA(static_cast<const char*>(errorBlob->GetBufferPointer()));
        }
        return hr;
    }

    // Load the compiled shader into D2D1
    hr = ctx->LoadPixelShader(
        GUID_TransitionPixelShader,
        static_cast<const BYTE*>(shaderBlob->GetBufferPointer()),
        static_cast<UINT32>(shaderBlob->GetBufferSize()));

    if (FAILED(hr)) return hr;

    // Set this transform as the single node in the graph
    return graph->SetSingleTransformNode(static_cast<ID2D1TransformNode*>(this));
}

HRESULT TransitionShaderEffect::PrepareForRender(D2D1_CHANGE_TYPE changeType) {
    if (!drawInfo_) return E_FAIL;

    // Push constant buffer to GPU
    return drawInfo_->SetPixelShaderConstantBuffer(
        reinterpret_cast<const BYTE*>(&constants_),
        sizeof(constants_));
}

// ============================================================================
// ID2D1DrawTransform
// ============================================================================

HRESULT TransitionShaderEffect::SetDrawInfo(ID2D1DrawInfo* drawInfo) {
    drawInfo_ = drawInfo;
    return drawInfo->SetPixelShader(GUID_TransitionPixelShader);
}

// ============================================================================
// ID2D1Transform
// ============================================================================

HRESULT TransitionShaderEffect::MapOutputRectToInputRects(
    const D2D1_RECT_L* pOutputRect,
    D2D1_RECT_L* pInputRects,
    UINT32 inputRectCount) const
{
    // Expand input rects slightly for UV-distorting shaders (LiquidMorph, WaveDistortion, etc.)
    LONG expand = 20;
    for (UINT32 i = 0; i < inputRectCount; ++i) {
        pInputRects[i].left   = pOutputRect->left   - expand;
        pInputRects[i].top    = pOutputRect->top    - expand;
        pInputRects[i].right  = pOutputRect->right  + expand;
        pInputRects[i].bottom = pOutputRect->bottom + expand;
    }
    return S_OK;
}

HRESULT TransitionShaderEffect::MapInputRectsToOutputRect(
    const D2D1_RECT_L* pInputRects,
    const D2D1_RECT_L* pInputOpaqueSubRects,
    UINT32 inputRectCount,
    D2D1_RECT_L* pOutputRect,
    D2D1_RECT_L* pOutputOpaqueSubRect)
{
    if (inputRectCount < 1) return E_INVALIDARG;

    inputRect_ = pInputRects[0];
    *pOutputRect = pInputRects[0];

    if (pOutputOpaqueSubRect) {
        *pOutputOpaqueSubRect = D2D1_RECT_L{0, 0, 0, 0};
    }

    return S_OK;
}

HRESULT TransitionShaderEffect::MapInvalidRect(
    UINT32 inputIndex,
    D2D1_RECT_L invalidInputRect,
    D2D1_RECT_L* pInvalidOutputRect) const
{
    *pInvalidOutputRect = inputRect_;
    return S_OK;
}

// ============================================================================
// Property accessors
// ============================================================================

HRESULT TransitionShaderEffect::SetTransitionParams(D2D1_VECTOR_4F value) {
    constants_.progress = value.x;
    constants_.mode = value.y;
    constants_.resolutionX = value.z;
    constants_.resolutionY = value.w;
    return S_OK;
}

D2D1_VECTOR_4F TransitionShaderEffect::GetTransitionParams() const {
    return D2D1_VECTOR_4F{
        constants_.progress,
        constants_.mode,
        constants_.resolutionX,
        constants_.resolutionY
    };
}

} // namespace jalium
