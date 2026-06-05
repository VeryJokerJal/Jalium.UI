#pragma once

#include "d3d12_backend.h"
#include <d3d12.h>
#include <wrl/client.h>
#include <cstdint>
#include <new>
#include <utility>

namespace jalium {

using Microsoft::WRL::ComPtr;

// Persistent offscreen RGBA texture holding a visual subtree's rasterized
// CONTENT for the retained-layer composited-animation fast path. A content-clean
// subtree under an Opacity / RenderTransform animation is rendered into this
// texture ONCE; subsequent frames composite it as a transformed/opacity quad
// (via the renderer's normal bitmap path) instead of re-emitting the whole
// subtree. Owns its texture + a single-entry RTV heap; the composite samples it
// through D3D12DirectRenderer::AddBitmap which creates the SRV per batch, so no
// SRV heap lives here.
//
// Lifetime: the GPU texture is released through D3D12Backend::RetireGpuResource
// (fence-gated graveyard) — never a bare ComPtr::Reset() while a composite quad
// referencing it may still be in flight (D3D12 ERROR #921). Header-only so it
// adds no new CMake source.
class D3D12RetainedLayer
{
public:
    D3D12RetainedLayer(ID3D12Device* device, D3D12Backend* backend)
        : device_(device), backend_(backend) {}
    ~D3D12RetainedLayer() { Release(); }

    D3D12RetainedLayer(const D3D12RetainedLayer&)            = delete;
    D3D12RetainedLayer& operator=(const D3D12RetainedLayer&) = delete;

    // Ensure the backing texture matches (pw, ph, format). Recreates (retiring
    // the old texture) on any change. Returns false on allocation failure (the
    // layer is then left with no texture; callers must null-check Texture()).
    bool EnsureSize(uint32_t pw, uint32_t ph, DXGI_FORMAT format)
    {
        if (pw == 0) pw = 1;
        if (ph == 0) ph = 1;
        if (texture_ && pw_ == pw && ph_ == ph && format_ == format) {
            return true;
        }

        Release();
        if (!device_) return false;
        format_ = format;

        D3D12_HEAP_PROPERTIES heapProps = {};
        heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

        D3D12_RESOURCE_DESC desc = {};
        desc.Dimension          = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        desc.Width              = pw;
        desc.Height             = ph;
        desc.DepthOrArraySize   = 1;
        desc.MipLevels          = 1;
        desc.Format             = format;
        desc.SampleDesc.Count   = 1;
        desc.SampleDesc.Quality = 0;
        desc.Layout             = D3D12_TEXTURE_LAYOUT_UNKNOWN;
        desc.Flags              = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

        D3D12_CLEAR_VALUE clear = {};
        clear.Format   = format;
        clear.Color[0] = clear.Color[1] = clear.Color[2] = clear.Color[3] = 0.0f;

        HRESULT hr = device_->CreateCommittedResource(
            &heapProps, D3D12_HEAP_FLAG_NONE, &desc,
            D3D12_RESOURCE_STATE_COMMON, &clear, IID_PPV_ARGS(&texture_));
        if (FAILED(hr)) { texture_.Reset(); return false; }
        state_ = D3D12_RESOURCE_STATE_COMMON;

        D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
        rtvHeapDesc.Type           = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        rtvHeapDesc.NumDescriptors = 1;
        rtvHeapDesc.Flags          = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        hr = device_->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&rtvHeap_));
        if (FAILED(hr)) { texture_.Reset(); rtvHeap_.Reset(); return false; }

        rtvCpu_ = rtvHeap_->GetCPUDescriptorHandleForHeapStart();
        device_->CreateRenderTargetView(texture_.Get(), nullptr, rtvCpu_);

        pw_ = pw;
        ph_ = ph;
        return true;
    }

    // Retire the texture through the fence-gated graveyard; drop the RTV heap
    // (its CPU descriptors are consumed at command-list record time, so it is
    // safe to drop once recording has moved on).
    void Release()
    {
        if (texture_) {
            if (backend_) {
                backend_->RetireGpuResource(std::move(texture_));
            }
            texture_.Reset();
        }
        rtvHeap_.Reset();
        rtvCpu_ = {};
        pw_ = ph_ = 0;
        state_ = D3D12_RESOURCE_STATE_COMMON;
    }

    ID3D12Resource*             Texture() const { return texture_.Get(); }
    D3D12_CPU_DESCRIPTOR_HANDLE Rtv()     const { return rtvCpu_; }
    D3D12_RESOURCE_STATES       State()   const { return state_; }
    void                        SetState(D3D12_RESOURCE_STATES s) { state_ = s; }
    uint32_t                    PixelWidth()  const { return pw_; }
    uint32_t                    PixelHeight() const { return ph_; }
    DXGI_FORMAT                 Format()  const { return format_; }

private:
    ID3D12Device*                device_  = nullptr;
    D3D12Backend*                backend_ = nullptr;

    ComPtr<ID3D12Resource>       texture_;
    D3D12_RESOURCE_STATES        state_   = D3D12_RESOURCE_STATE_COMMON;
    uint32_t                     pw_ = 0;
    uint32_t                     ph_ = 0;
    DXGI_FORMAT                  format_ = DXGI_FORMAT_R8G8B8A8_UNORM;

    ComPtr<ID3D12DescriptorHeap> rtvHeap_;
    D3D12_CPU_DESCRIPTOR_HANDLE  rtvCpu_ = {};
};

} // namespace jalium
