#pragma once

#include "jalium_backend.h"

#include <d3d12.h>
#include <dxgi1_6.h>
#include <dwrite_3.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <mutex>
#include <vector>

namespace jalium {

using Microsoft::WRL::ComPtr;

/// Diagnostic init logger: writes msg to OutputDebugStringA AND, if the env var
/// JALIUM_INIT_LOG points to a file, appends msg there. Surfaces GPU device /
/// swap-chain creation + any fallback/degradation without needing a debugger.
void JaliumD3D12InitLog(const char* msg);

/// D3D12 rendering backend implementation.
class D3D12Backend : public IRenderBackend {
public:
    D3D12Backend();
    ~D3D12Backend() override;

    /// Initializes the D3D12 backend.
    bool Initialize(void* preferredWindow = nullptr);

    // IRenderBackend implementation
    JaliumBackend GetType() const override { return JALIUM_BACKEND_D3D12; }
    const wchar_t* GetName() const override { return L"Direct3D 12"; }
    JaliumResult CheckDeviceStatus() override;
    JaliumResult GetAdapterInfo(JaliumAdapterInfo* out) const override;

    RenderTarget* CreateRenderTarget(void* hwnd, int32_t width, int32_t height) override;
    RenderTarget* CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height) override;
    Brush* CreateSolidBrush(float r, float g, float b, float a) override;
    Brush* CreateLinearGradientBrush(
        float startX, float startY, float endX, float endY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    Brush* CreateRadialGradientBrush(
        float centerX, float centerY, float radiusX, float radiusY,
        float originX, float originY,
        const JaliumGradientStop* stops, uint32_t stopCount,
        uint32_t spreadMethod = 0) override;
    TextFormat* CreateTextFormat(
        const wchar_t* fontFamily, float fontSize,
        int32_t fontWeight, int32_t fontStyle) override;
    Bitmap* CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize) override;
    Bitmap* CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) override;
    VideoSurface* CreateVideoSurface(uint32_t width, uint32_t height, uint32_t formatHint) override;
    VideoSurface* WrapExternalVideoSurface(const JaliumVideoSurfaceDescriptor* descriptor) override;

    // Ink-layer / brush-shader pipeline — implementations live in
    // d3d12_ink_layer.cpp so the backend TU doesn't pull the whole
    // brush-shader header chain unnecessarily.
    void*   CreateInkLayerBitmap(uint32_t width, uint32_t height) override;
    void    DestroyInkLayerBitmap(void* bitmap) override;
    int32_t ResizeInkLayerBitmap(void* bitmap, uint32_t width, uint32_t height) override;
    void    ClearInkLayerBitmap(void* bitmap, float r, float g, float b, float a) override;
    void*   CreateBrushShader(const char* shaderKey, const char* brushMainHlsl, int32_t blendMode) override;
    void    DestroyBrushShader(void* shader) override;
    int32_t DispatchBrush(void* bitmap, void* shader,
                          const void* strokePoints, uint32_t pointCount,
                          const void* constants,
                          const void* extraParams, uint32_t extraParamsSize) override;

    // Accessors for internal components
    ID3D12Device* GetDevice() const { return device_.Get(); }
    ID3D12CommandQueue* GetCommandQueue() const { return commandQueue_.Get(); }
    IDWriteFactory5* GetDWriteFactory() const { return dwriteFactory_.Get(); }
    IDXGIFactory6* GetDXGIFactory() const { return dxgiFactory_.Get(); }

    IWICImagingFactory* GetWICFactory() const { return wicFactory_.Get(); }

    // ──────────────────────────────────────────────────────────────────
    // Fence-gated GPU-resource graveyard
    // ──────────────────────────────────────────────────────────────────
    //
    // Any code path that drops the last reference to a D3D12 resource which
    // *might* still be referenced by an open or in-flight command list
    // (bitmap textures + upload buffers being the primary case — a
    // BitmapImage can be GC'd or LRU-evicted from any thread at any time)
    // must hand the ComPtr to RetireGpuResource instead of letting it
    // destruct directly.  The backend keeps the resource alive at refcount
    // ≥ 1, tags it with the highest fence value seen so far, and frees it
    // from ReclaimRetiredGpuResources once the renderer reports that fence
    // is GPU-complete.
    //
    // Threading: RetireGpuResource is safe from any thread (worker pool,
    // finalizer, UI). ReclaimRetiredGpuResources / NoteSubmittedFenceValue
    // are expected to be called from the renderer's UI thread at frame
    // boundaries; both take the same mutex.
    void RetireGpuResource(ComPtr<ID3D12Resource>&& resource);
    void NoteSubmittedFenceValue(uint64_t fenceValue);
    void ReclaimRetiredGpuResources(uint64_t completedFenceValue);

private:
    bool CreateD3D12Device(void* preferredWindow = nullptr);
    bool CreateDWriteFactory();
    bool CreateWICFactory();
    void ReleasePartialInit();

    // D3D12 resources
    ComPtr<IDXGIFactory6> dxgiFactory_;
    ComPtr<ID3D12Device> device_;
    ComPtr<ID3D12CommandQueue> commandQueue_;

    // DirectWrite resources
    ComPtr<IDWriteFactory5> dwriteFactory_;

    // WIC resources
    ComPtr<IWICImagingFactory> wicFactory_;

    JaliumGpuPreference gpuPrefFromEnv_ = JALIUM_GPU_PREFERENCE_AUTO;
    bool initialized_ = false;

    /// Cached description of the adapter currently backing <see cref="device_"/>.<br/>
    /// Captured at the end of <see cref="CreateD3D12Device"/> so <see cref="GetAdapterInfo"/>
    /// doesn't need to re-enumerate or query DXGI on every call. Set to a zero-initialized
    /// desc + has_ = false when no device has been created yet.
    bool adapterDescValid_ = false;
    DXGI_ADAPTER_DESC1 adapterDesc_{};

    // Graveyard storage. resource_ holds the live ref, fenceValue is the
    // largest renderer fence value submitted at retirement time — when
    // GetCompletedValue() ≥ fenceValue the resource is GPU-idle.
    struct RetiredResource {
        ComPtr<ID3D12Resource> resource;
        uint64_t fenceValue;
    };
    std::mutex graveyardMutex_;
    std::vector<RetiredResource> graveyard_;
    // Highest fence value the renderer has submitted (passed to Signal).
    // Used as the "this resource must outlive at least up to" tag for any
    // resource retired before the next Signal lands.
    uint64_t lastSubmittedFenceValue_ = 0;
};

/// Factory function to create D3D12 backend.
IRenderBackend* CreateD3D12Backend();

} // namespace jalium
