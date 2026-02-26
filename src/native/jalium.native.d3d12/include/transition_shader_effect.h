#pragma once

#include "d3d12_backend.h"
#include <d2d1effectauthor.h>
#include <d2d1effecthelpers.h>
#include <d3dcompiler.h>

namespace jalium {

// {7E3F1A2B-8C4D-5E6F-A0B1-C2D3E4F50010}
DEFINE_GUID(CLSID_TransitionShaderEffect,
    0x7e3f1a2b, 0x8c4d, 0x5e6f, 0xa0, 0xb1, 0xc2, 0xd3, 0xe4, 0xf5, 0x00, 0x10);

// {7E3F1A2B-8C4D-5E6F-A0B1-C2D3E4F50011}
DEFINE_GUID(GUID_TransitionPixelShader,
    0x7e3f1a2b, 0x8c4d, 0x5e6f, 0xa0, 0xb1, 0xc2, 0xd3, 0xe4, 0xf5, 0x00, 0x11);

/// Constant buffer layout for the transition pixel shader.
/// Must match the cbuffer in the HLSL shader exactly (16 bytes, 1 float4 register).
struct TransitionConstants {
    float progress;                    // 0.0 - 1.0 transition progress
    float mode;                        // 0-9 shader mode index
    float resolutionX, resolutionY;    // physical pixel dimensions of transition area
};
static_assert(sizeof(TransitionConstants) == 16, "Constants must be 16 bytes (1 float4 register)");

/// Single D2D1 custom effect that implements 10 content transition blend modes.
/// Inputs:
///   0 = Old content (offscreen bitmap captured before transition)
///   1 = New content (offscreen bitmap captured during transition)
class TransitionShaderEffect : public ID2D1EffectImpl, public ID2D1DrawTransform {
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

    // ----- Property setter/getter (for D2D1 property system) -----
    // Property 0: TransitionParams (float4: progress, mode, resolutionX, resolutionY)
    HRESULT SetTransitionParams(D2D1_VECTOR_4F value);
    D2D1_VECTOR_4F GetTransitionParams() const;

private:
    TransitionShaderEffect();

    LONG refCount_ = 1;
    ComPtr<ID2D1DrawInfo> drawInfo_;
    TransitionConstants constants_ = {};
    D2D1_RECT_L inputRect_ = {};
};

} // namespace jalium
