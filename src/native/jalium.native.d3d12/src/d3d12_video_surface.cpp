// D3D12VideoSurface — stage 2 BGRA8 video staging on D3D12. Reuses the entire
// D3D12Bitmap upload / texture / draw machinery (default-heap texture, fence-
// gated retire, SRV cache) — this class only adds a Lock/Unlock pair that
// lets a managed decoder write straight into the bitmap's CPU pixelData_ vector
// without going through WriteableBitmap as an intermediary.
//
// Stage 3 (DXVA hardware decode) will introduce a sibling kind that wraps an
// imported ID3D11Texture2D shared handle and skips this CPU staging path
// entirely; until then this is the path 1080p video traffic flows through.

#include "d3d12_resources.h"
#include "d3d12_backend.h"
#include "d3d12_render_target.h"

#include <dxgi1_2.h>
#include <cstring>

namespace jalium {

D3D12VideoSurface::D3D12VideoSurface(D3D12Backend* backend, uint32_t width, uint32_t height)
    : bitmap(backend, width, height)
{
    // Allocate the CPU staging buffer up front so Lock can hand back a stable
    // pointer the decoder pump writes into. The default-heap texture itself
    // is lazily created on the first GetOrCreateD3D12Texture pass.
    bitmap.pixelData_.assign(static_cast<size_t>(width) * height * 4u, 0);
    bitmap.isDynamic_ = true;
}

bool D3D12VideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride) return false;
    if (bitmap.pixelData_.empty()) return false;
    *outPtr    = bitmap.pixelData_.data();
    *outStride = bitmap.width_ * 4u;
    return true;
}

bool D3D12VideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/)
{
    // Decoder just finished writing the new frame. Invalidating the GPU texture
    // tells the next GetOrCreateD3D12Texture pass to re-upload from pixelData_
    // — but it keeps the default-heap texture itself alive, so the upload is
    // an in-place CopyTextureRegion rather than a CreateCommittedResource per
    // frame (the same fast path D3D12Bitmap.UpdatePackedPixels enables).
    bitmap.isDynamic_ = true;
    bitmap.d3d12TextureValid_ = false;
    return true;
}

// ─── Backend factory ──────────────────────────────────────────────────────

VideoSurface* D3D12Backend::CreateVideoSurface(uint32_t width, uint32_t height,
                                                uint32_t /*formatHint*/)
{
    if (width == 0 || height == 0) return nullptr;
    return new D3D12VideoSurface(this, width, height);
}

VideoSurface* D3D12Backend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!descriptor) return nullptr;

    if (descriptor->kind == JALIUM_VS_KIND_D3D11_SHARED) {
        // Stage 3b.2: DXVA decoder (stage 3b.1) gave us a D3D11
        // SHARED_NTHANDLE BGRA8 texture; import it into our D3D12 device.
        // Requires the source D3D11 device and our D3D12 device to be on the
        // same IDXGIAdapter (LUID match). Single-GPU systems hit this
        // automatically; multi-GPU laptops (Intel iGPU + NVIDIA dGPU) may
        // pick different default adapters - OpenSharedHandle reports
        // E_INVALIDARG / E_ACCESSDENIED in that case and we fall back to the
        // BGRA staging path (stage 2 D3D12VideoSurface).
        if (descriptor->handle0 == 0 ||
            descriptor->width == 0 || descriptor->height == 0 ||
            !device_) {
            return nullptr;
        }
        auto ntHandle = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(descriptor->handle0));

        ComPtr<ID3D12Resource> imported;
        HRESULT hr = device_->OpenSharedHandle(ntHandle, IID_PPV_ARGS(imported.GetAddressOf()));
        if (FAILED(hr) || !imported) {
            return nullptr;
        }
        return new ImportedD3D12VideoSurface(
            this, std::move(imported), descriptor->width, descriptor->height);
    }

    // Other kinds: BGRA8_CPU goes through CreateVideoSurface (this entry rejects);
    // VkImage / AHardwareBuffer / IOSurface / Metal / CVPixelBuffer are Vulkan /
    // Apple / Android specific and the D3D12 backend never supports them.
    return nullptr;
}

// --- ImportedD3D12VideoSurface --------------------------------------------

ImportedD3D12VideoSurface::ImportedD3D12VideoSurface(
    D3D12Backend* backend, ComPtr<ID3D12Resource> tex, uint32_t width, uint32_t height)
    : importedTexture(std::move(tex))
    , bitmap(backend, width, height)
{
    // Hand the imported texture to the embedded bitmap so DrawBitmap's existing
    // SRV / shader path can sample it. Mark it valid + dynamic so
    // GetOrCreateD3D12Texture takes the fast path and skips upload from
    // pixelData_ (which stays empty for imported surfaces).
    bitmap.d3d12Texture_ = importedTexture;
    bitmap.d3d12TextureValid_ = true;
    bitmap.isDynamic_ = true;

    // Stage 3b.3: try to surface the underlying IDXGIKeyedMutex so we can sync
    // with the D3D11 writer. Whether this succeeds depends on (a) the resource
    // having been created with D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX on the
    // D3D11 side (stage 3b.1 always does), and (b) the driver allowing
    // QueryInterface from a D3D12-imported resource. When it fails, keyedMutex
    // stays null and Acquire/Release reader lock degrade to no-ops — the
    // stage 3b.2 in-order-execution assumption takes over (correct in practice
    // for single-adapter / same-driver pipelines).
    importedTexture.As(&keyedMutex);
}

ImportedD3D12VideoSurface::~ImportedD3D12VideoSurface()
{
    // Defensive: if we crashed mid-frame holding the reader lock, releasing
    // it on destroy avoids a permanent stall on the writer side.
    if (holdsReaderLock && keyedMutex) {
        keyedMutex->ReleaseSync(0);
        holdsReaderLock = false;
    }
}

bool ImportedD3D12VideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    // Imported D3D11 shared texture - caller cannot write to it from CPU.
    if (outPtr)    *outPtr = nullptr;
    if (outStride) *outStride = 0;
    return false;
}

bool ImportedD3D12VideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/)
{
    // No-op on the surface ABI: cross-device sync happens at draw time via
    // AcquireReaderLock / ReleaseReaderLock (driven by DrawVideoSurface).
    return true;
}

void ImportedD3D12VideoSurface::AcquireReaderLock()
{
    if (!keyedMutex || holdsReaderLock) return;
    // MF writer (stage 3b.1) released to key 1 after each CopyResource + Flush.
    // We grab key 1 here. INFINITE wait — if MF is mid-write we yield until it
    // finishes; in practice the wait is sub-millisecond on a real DXVA stream.
    HRESULT hr = keyedMutex->AcquireSync(1, INFINITE);
    holdsReaderLock = SUCCEEDED(hr);
}

void ImportedD3D12VideoSurface::ReleaseReaderLock()
{
    if (!keyedMutex || !holdsReaderLock) return;
    // Release to key 0 so MF's next CopyResource (stage 3b.1's
    // writeMutex->AcquireSync(0, 16ms)) can pick it up immediately.
    keyedMutex->ReleaseSync(0);
    holdsReaderLock = false;
}

// ─── Render-target draw routing ──────────────────────────────────────────

void D3D12RenderTarget::DrawVideoSurface(VideoSurface* surface,
                                          float x, float y, float w, float h,
                                          float opacity, int scalingMode)
{
    if (!surface) return;
    if (auto* vs = dynamic_cast<D3D12VideoSurface*>(surface)) {
        // Stage 2 path: BGRA staging texture authored by managed Lock/Unlock.
        // The wrapped bitmap's d3d12TextureValid_=false triggers a fresh
        // CopyTextureRegion from pixelData_ inside GetOrCreateD3D12Texture.
        DrawBitmap(&vs->bitmap, x, y, w, h, opacity, scalingMode);
        return;
    }
    if (auto* imp = dynamic_cast<ImportedD3D12VideoSurface*>(surface)) {
        // Stage 3b.2 path: DXVA-decoded shared NT texture imported via
        // OpenSharedHandle. The wrapped bitmap's d3d12Texture_ is the imported
        // resource itself + d3d12TextureValid_=true, so GetOrCreateD3D12Texture
        // takes the fast path and we sample the GPU-decoded pixels directly
        // (no staging upload, no CPU copy).
        //
        // Stage 3b.3: acquire keyed-mutex reader lock around the sample so
        // we don't read a half-written frame on systems where D3D11/D3D12
        // GPU work isn't strictly in-order (multi-queue drivers, async copy).
        imp->AcquireReaderLock();
        DrawBitmap(&imp->bitmap, x, y, w, h, opacity, scalingMode);
        imp->ReleaseReaderLock();
        return;
    }
}

} // namespace jalium
