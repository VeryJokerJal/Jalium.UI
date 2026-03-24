#pragma once

#include "d3d12_backend.h"
#include <d2d1effectauthor.h>
#include <d2d1effecthelpers.h>
#include <d3dcompiler.h>

namespace jalium {

// {7E3F1A2B-8C4D-5E6F-A0B1-C2D3E4F50001}
DEFINE_GUID(CLSID_LiquidGlassEffect,
    0x7e3f1a2b, 0x8c4d, 0x5e6f, 0xa0, 0xb1, 0xc2, 0xd3, 0xe4, 0xf5, 0x00, 0x01);

// {7E3F1A2B-8C4D-5E6F-A0B1-C2D3E4F50002}
DEFINE_GUID(GUID_LiquidGlassPixelShader,
    0x7e3f1a2b, 0x8c4d, 0x5e6f, 0xa0, 0xb1, 0xc2, 0xd3, 0xe4, 0xf5, 0x00, 0x02);

/// Constant buffer layout for the liquid glass pixel shader.
/// Must match the cbuffer in the HLSL shader exactly (192 bytes, 12 float4 registers).
struct LiquidGlassConstants {
    // Register 0: Glass rectangle (x, y, w, h)
    float glassX, glassY, glassW, glassH;

    // Register 1: Refraction parameters
    float cornerRadius;          // Border radius (default 8)
    float refractionHeight;      // Depth zone (default 40)
    float refractionAmount;      // UV displacement (default 60)
    float chromaticAberration;   // Color dispersion 0-1 (default 0)

    // Register 2: Tint color
    float vibrancy;              // Saturation enhancement (default 1.5)
    float tintR, tintG, tintB;

    // Register 3: Tint opacity + highlight + light position (screen-space)
    float tintOpacity;
    float highlightOpacity;      // Edge highlight (default 0.55)
    float lightPosX, lightPosY;  // Screen-space light position (-1 = no mouse, use dual-corner)

    // Register 4: Shadow parameters
    float shadowOffset;          // Downward offset (default 3)
    float shadowRadius;          // Blur radius (default 8)
    float shadowOpacity;         // Strength (default 0.12)
    float _pad0;

    // Register 5: Screen size + shape parameters
    float screenSizeX, screenSizeY;
    float shapeType;    // 0 = RoundedRect, 1 = SuperEllipse
    float shapeN;       // SuperEllipse exponent (default 4.0)

    // Register 6: Fusion parameters
    float neighborCount;         // Number of active neighbors (0-4)
    float fusionRadius;          // Smooth min radius in pixels (default 30)
    float _pad1, _pad2;

    // Register 7-10: Neighbor rectangles (x, y, w, h in screen space)
    float n0x, n0y, n0w, n0h;
    float n1x, n1y, n1w, n1h;
    float n2x, n2y, n2w, n2h;
    float n3x, n3y, n3w, n3h;

    // Register 11: Neighbor corner radii
    float n0r, n1r, n2r, n3r;
};
static_assert(sizeof(LiquidGlassConstants) == 192, "Constants must be 192 bytes (12 float4 registers)");

/// Single D2D1 custom effect that implements the full liquid glass pipeline:
/// refraction + highlight + inner shadow + composite in one pass.
/// Inputs:
///   0 = Original background (snapshot)
///   1 = Blurred background (Gaussian-blurred snapshot)
class LiquidGlassEffect : public ID2D1EffectImpl, public ID2D1DrawTransform {
public:
    /// Registers the effect with the D2D1 factory. Call once during backend init.
    static HRESULT Register(ID2D1Factory1* factory);

    /// Factory method for D2D1 effect creation.
    static HRESULT __stdcall CreateEffect(IUnknown** ppEffectImpl);

    // ----- IUnknown -----
    IFACEMETHODIMP_(ULONG) AddRef() override;
    IFACEMETHODIMP_(ULONG) Release() override;
    IFACEMETHODIMP QueryInterface(REFIID riid, void** ppOutput) override;

    // ----- ID2D1EffectImpl -----
    IFACEMETHODIMP Initialize(ID2D1EffectContext* ctx, ID2D1TransformGraph* graph) override;
    IFACEMETHODIMP PrepareForRender(D2D1_CHANGE_TYPE changeType) override;
    IFACEMETHODIMP SetGraph(ID2D1TransformGraph* graph) override { return E_NOTIMPL; }

    // ----- ID2D1DrawTransform -----
    IFACEMETHODIMP SetDrawInfo(ID2D1DrawInfo* drawInfo) override;

    // ----- ID2D1Transform -----
    IFACEMETHODIMP MapOutputRectToInputRects(
        const D2D1_RECT_L* pOutputRect,
        D2D1_RECT_L* pInputRects,
        UINT32 inputRectCount) const override;

    IFACEMETHODIMP MapInputRectsToOutputRect(
        const D2D1_RECT_L* pInputRects,
        const D2D1_RECT_L* pInputOpaqueSubRects,
        UINT32 inputRectCount,
        D2D1_RECT_L* pOutputRect,
        D2D1_RECT_L* pOutputOpaqueSubRect) override;

    IFACEMETHODIMP MapInvalidRect(
        UINT32 inputIndex,
        D2D1_RECT_L invalidInputRect,
        D2D1_RECT_L* pInvalidOutputRect) const override;

    // ----- ID2D1TransformNode -----
    IFACEMETHODIMP_(UINT32) GetInputCount() const override { return 2; }

    // ----- Property setters/getters (for D2D1 property system) -----
    // Property 0: GlassRect (float4: x, y, w, h)
    HRESULT SetGlassRect(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetGlassRect() const;

    // Property 1: RefractionParams (float4: cornerRadius, refractionHeight, refractionAmount, chromaticAberration)
    HRESULT SetRefractionParams(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetRefractionParams() const;

    // Property 2: TintColor (float4: vibrancy, tintR, tintG, tintB)
    HRESULT SetTintColor(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetTintColor() const;

    // Property 3: TintAndHighlight (float4: tintOpacity, highlightOpacity, lightPosX, lightPosY)
    HRESULT SetTintAndHighlight(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetTintAndHighlight() const;

    // Property 4: ShadowParams (float4: shadowOffset, shadowRadius, shadowOpacity, 0)
    HRESULT SetShadowParams(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetShadowParams() const;

    // Property 5: ScreenSize (float4: screenSizeX, screenSizeY, shapeType, shapeN)
    HRESULT SetScreenSize(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetScreenSize() const;

    // Property 6: FusionParams (float4: neighborCount, fusionRadius, 0, 0)
    HRESULT SetFusionParams(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetFusionParams() const;

    // Property 7-10: NeighborRect0..3 (float4: x, y, w, h)
    HRESULT SetNeighborRect0(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetNeighborRect0() const;
    HRESULT SetNeighborRect1(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetNeighborRect1() const;
    HRESULT SetNeighborRect2(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetNeighborRect2() const;
    HRESULT SetNeighborRect3(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetNeighborRect3() const;

    // Property 11: NeighborRadii (float4: r0, r1, r2, r3)
    HRESULT SetNeighborRadii(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetNeighborRadii() const;

private:
    LiquidGlassEffect();

    LONG refCount_ = 1;
    ComPtr<ID2D1DrawInfo> drawInfo_;
    LiquidGlassConstants constants_ = {};
    D2D1_RECT_L inputRect_ = {};
};

} // namespace jalium
