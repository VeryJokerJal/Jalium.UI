#pragma once

#include "jalium_backend.h"

#include <d3d12.h>
#include <dxgi1_6.h>
#include <d2d1_3.h>
#include <d3d11on12.h>
#include <dwrite_3.h>
#include <wincodec.h>
#include <wrl/client.h>

namespace jalium {

using Microsoft::WRL::ComPtr;

/// D3D12 rendering backend implementation.
/// Uses D2D1 on top of D3D12 for 2D rendering.
class D3D12Backend : public IRenderBackend {
public:
    D3D12Backend();
    ~D3D12Backend() override;

    /// Initializes the D3D12 backend.
    bool Initialize();

    // IRenderBackend implementation
    JaliumBackend GetType() const override { return JALIUM_BACKEND_D3D12; }
    const wchar_t* GetName() const override { return L"Direct3D 12"; }

    RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount) override;
    Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount) override;
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily, float fontSize,
        int32_t fontWeight, int32_t fontStyle) override;
    Bitmap* CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) override;

    // Accessors for internal components
    ID3D12Device* GetDevice() const { return device_.Get(); }
    ID3D12CommandQueue* GetCommandQueue() const { return commandQueue_.Get(); }
    ID2D1Factory3* GetD2DFactory() const { return d2dFactory_.Get(); }
    ID2D1Device2* GetD2DDevice() const { return d2dDevice_.Get(); }
    IDWriteFactory5* GetDWriteFactory() const { return dwriteFactory_.Get(); }
    ID3D11On12Device* GetD3D11On12Device() const { return d3d11On12Device_.Get(); }
    ID3D11DeviceContext* GetD3D11Context() const { return d3d11Context_.Get(); }
    IDXGIFactory6* GetDXGIFactory() const { return dxgiFactory_.Get(); }

    IWICImagingFactory* GetWICFactory() const { return wicFactory_.Get(); }

private:
    bool CreateD3D12Device();
    bool CreateD2DDevice();
    bool CreateDWriteFactory();
    bool CreateWICFactory();

    // D3D12 resources
    ComPtr<IDXGIFactory6> dxgiFactory_;
    ComPtr<ID3D12Device> device_;
    ComPtr<ID3D12CommandQueue> commandQueue_;

    // D3D11on12 interop for D2D
    ComPtr<ID3D11Device> d3d11Device_;
    ComPtr<ID3D11DeviceContext> d3d11Context_;
    ComPtr<ID3D11On12Device> d3d11On12Device_;

    // D2D resources
    ComPtr<ID2D1Factory3> d2dFactory_;
    ComPtr<ID2D1Device2> d2dDevice_;

    // DirectWrite resources
    ComPtr<IDWriteFactory5> dwriteFactory_;

    // WIC resources
    ComPtr<IWICImagingFactory> wicFactory_;

    bool initialized_ = false;
};

/// Factory function to create D3D12 backend.
IRenderBackend* CreateD3D12Backend();

} // namespace jalium
