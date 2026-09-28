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
    PackedBgraLayout layout{};
    if (TryComputeTightlyPackedBgraLayout(width, height, layout)) {
        bitmap.pixelData_.assign(layout.packedBytes, 0);
    }
    bitmap.isDynamic_ = true;
}

bool D3D12VideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    if (!outPtr || !outStride) return false;
    if (bitmap.pixelData_.empty()) return false;
    *outPtr    = bitmap.pixelData_.data();
    *outStride = static_cast<uint32_t>(
        static_cast<uint64_t>(bitmap.width_) * 4u);
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
    PackedBgraLayout layout{};
    if (!TryComputeTightlyPackedBgraLayout(width, height, layout)) return nullptr;
    return new D3D12VideoSurface(this, width, height);
}

VideoSurface* D3D12Backend::WrapExternalVideoSurface(
    const JaliumVideoSurfaceDescriptor* descriptor)
{
    if (!descriptor) return nullptr;

    if (descriptor->kind == JALIUM_VS_KIND_D3D11_SHARED) {
        // The producer must publish a complete immutable frame, not a live
        // decoder-pool slot. Opening a handle neither waits for D3D11 writes
        // nor prevents the next decode from overwriting a queued frame.
        if ((descriptor->descriptor_flags & JALIUM_VS_FLAG_IMMUTABLE_READY) == 0)
            return nullptr;
        // Import the SHARED_NTHANDLE packed RGB texture into our D3D12 device.
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
        const auto desc = imported->GetDesc();
        const bool supportedFormat =
            (descriptor->format_hint == JALIUM_VS_FORMAT_BGRA8 && desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM) ||
            (descriptor->format_hint == JALIUM_VS_FORMAT_BGRX8 && desc.Format == DXGI_FORMAT_B8G8R8X8_UNORM);
        if (desc.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
            desc.Width != descriptor->width || desc.Height != descriptor->height ||
            desc.DepthOrArraySize != 1 || desc.MipLevels != 1 ||
            desc.SampleDesc.Count != 1 || !supportedFormat ||
            (desc.Flags & D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE) != 0)
            return nullptr;
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
    // SRV / shader path can sample it. Mark it valid so
    // GetOrCreateD3D12Texture takes the fast path and skips upload from
    // pixelData_ (which stays empty for imported surfaces).
    bitmap.d3d12Texture_ = importedTexture;
    bitmap.d3d12TextureValid_ = true;
    // The shared resource is read-only on this device. COMMON permits implicit
    // shader-read promotion. The producer has already completed its writes and
    // will never use this allocation again, including while playback is paused.
    bitmap.isDynamic_ = false;
}

ImportedD3D12VideoSurface::~ImportedD3D12VideoSurface() = default;

bool ImportedD3D12VideoSurface::Lock(uint8_t** outPtr, uint32_t* outStride)
{
    // Imported D3D11 shared texture - caller cannot write to it from CPU.
    if (outPtr)    *outPtr = nullptr;
    if (outStride) *outStride = 0;
    return false;
}

bool ImportedD3D12VideoSurface::Unlock(const JaliumVideoSurfaceDirtyRect* /*dirty*/)
{
    // Immutable imported frames cannot be CPU-mapped or updated.
    return false;
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
        // AddBitmap retains the texture through the submission fence. Releasing
        // a decoder frame or navigating away cannot destroy in-flight pixels.
        DrawBitmap(&imp->bitmap, x, y, w, h, opacity, scalingMode);
        return;
    }
}

} // namespace jalium
