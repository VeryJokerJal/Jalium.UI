#include "d3d12_resources.h"
#include "d3d12_backend.h"
#include "jalium_bitmap_stats.h"
#include <algorithm>
#include <cstring>

namespace jalium {

namespace {

// Compute mip-chain count for the given width/height. We stop when both
// dimensions reach 1, matching D3D11/D3D12 conventions.
inline UINT16 ComputeMipLevels(uint32_t width, uint32_t height) {
    UINT16 levels = 1;
    uint32_t w = width;
    uint32_t h = height;
    while (w > 1 || h > 1) {
        w = std::max(1u, w / 2);
        h = std::max(1u, h / 2);
        ++levels;
    }
    return levels;
}

// CPU 2x2 box-filter downsample for a single mip level.
// Source/dest are tightly packed BGRA (4 bytes per pixel). Premultiplied or
// straight alpha both work — we average channels uniformly so a fully
// transparent pixel doesn't bleed colour into its neighbour, since for
// straight alpha the caller's RGB happens to be meaningful only where A>0
// and for premultiplied alpha the math is exact.
void DownsampleBoxBgra(const uint8_t* src, uint32_t srcW, uint32_t srcH,
                       uint8_t* dst, uint32_t dstW, uint32_t dstH) {
    for (uint32_t y = 0; y < dstH; ++y) {
        const uint32_t y0 = std::min(srcH - 1, y * 2);
        const uint32_t y1 = std::min(srcH - 1, y * 2 + 1);
        const uint8_t* row0 = src + y0 * srcW * 4;
        const uint8_t* row1 = src + y1 * srcW * 4;
        uint8_t* drow = dst + y * dstW * 4;
        for (uint32_t x = 0; x < dstW; ++x) {
            const uint32_t x0 = std::min(srcW - 1, x * 2);
            const uint32_t x1 = std::min(srcW - 1, x * 2 + 1);
            const uint8_t* p00 = row0 + x0 * 4;
            const uint8_t* p01 = row0 + x1 * 4;
            const uint8_t* p10 = row1 + x0 * 4;
            const uint8_t* p11 = row1 + x1 * 4;
            for (int c = 0; c < 4; ++c) {
                uint32_t sum = (uint32_t)p00[c] + (uint32_t)p01[c] + (uint32_t)p10[c] + (uint32_t)p11[c];
                drow[x * 4 + c] = static_cast<uint8_t>((sum + 2) >> 2);
            }
        }
    }
}

} // namespace

// Premultiply straight (non-premultiplied) BGRA8 in place: RGB *= A / 255.
//
// Alpha contract (see jalium_api.h): the raw-pixel bitmap ABI
// (jalium_bitmap_create_from_pixels / jalium_bitmap_update_pixels) always
// receives STRAIGHT alpha. D3D12's bitmap PSO blends premultiplied
// (SrcBlend=ONE) and bitmap_quad.ps does NOT premultiply in the shader, so raw
// straight pixels would let anti-aliased / cross-alpha edges bleed bright RGB
// (the historical white-fringe bug). We premultiply here — once, at the two
// straight entry points — so the texture the shader samples is premultiplied,
// matching what the encoded path already gets for free (WIC decodes to
// 32bppPBGRA in CreateBitmapFromMemory). This is deliberately NOT done in the
// shared GetOrCreateD3D12Texture upload: that path is also fed by the WIC
// (already-premultiplied) decode and by the video surface's direct pixelData_
// writes, either of which would be double-premultiplied. Rows with A==255 copy
// through untouched, so opaque content is byte-stable.
void PremultiplyBgraInPlace(uint8_t* pixels, size_t pixelCount) {
    for (size_t p = 0; p < pixelCount; ++p) {
        uint8_t* px = pixels + p * 4;
        const uint8_t a = px[3];
        if (a == 255) {
            continue;                    // opaque → premultiply is identity
        }
        if (a == 0) {
            px[0] = px[1] = px[2] = 0;   // fully transparent → RGB carries no colour
            continue;
        }
        px[0] = static_cast<uint8_t>((px[0] * a + 127) / 255);
        px[1] = static_cast<uint8_t>((px[1] * a + 127) / 255);
        px[2] = static_cast<uint8_t>((px[2] * a + 127) / 255);
    }
}

// True when a run of STRAIGHT-alpha source pixels, once premultiplied, equals
// the already-premultiplied destination bytes. Used by the UpdatePackedPixels
// fast-skip: pixelData_ is stored premultiplied, but callers hand us straight
// pixels every frame, so a raw memcmp would spuriously miss on any
// semi-transparent content. Premultiplying each source pixel on the fly and
// comparing keeps the skip correct with zero extra allocation, and
// short-circuits on the first differing byte just like memcmp did.
bool PremultipliedRowEquals(const uint8_t* dstPremul, const uint8_t* srcStraight, size_t pixelCount) {
    for (size_t p = 0; p < pixelCount; ++p) {
        const uint8_t* d = dstPremul + p * 4;
        const uint8_t* s = srcStraight + p * 4;
        const uint8_t a = s[3];
        uint8_t b, g, r;
        if (a == 255) {
            b = s[0]; g = s[1]; r = s[2];
        } else if (a == 0) {
            b = g = r = 0;
        } else {
            b = static_cast<uint8_t>((s[0] * a + 127) / 255);
            g = static_cast<uint8_t>((s[1] * a + 127) / 255);
            r = static_cast<uint8_t>((s[2] * a + 127) / 255);
        }
        if (d[0] != b || d[1] != g || d[2] != r || d[3] != a) {
            return false;
        }
    }
    return true;
}

D3D12Bitmap::D3D12Bitmap(D3D12Backend* backend, uint32_t width, uint32_t height)
    : width_(width)
    , height_(height)
    , backend_(backend)
{
}

void D3D12Bitmap::SetBitmapData(const uint8_t* data, uint32_t dataSize) {
    // Same content-skip rationale as UpdatePackedPixels: callers (cached
    // bitmap thumbnails, image sources that re-decode every frame, etc.)
    // often hand us identical pixels every call. memcmp short-circuits on
    // the first differing byte so changed images are still cheap; matched
    // content keeps the cached d3d12Texture_ valid and skips the entire
    // GPU upload pipeline (5–10 ms/call on the user's profile).
    if (data != nullptr &&
        pixelData_.size() == dataSize &&
        d3d12TextureValid_ && d3d12Texture_ &&
        std::memcmp(pixelData_.data(), data, dataSize) == 0)
    {
        bitmap_stats::AddMemcmpShortCircuit();
        return;
    }
    pixelData_.assign(data, data + dataSize);
    d3d12TextureValid_ = false;  // Force re-upload on next use
}

bool D3D12Bitmap::UpdatePackedPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride) {
    if (!pixels || width == 0 || height == 0 || stride < width * 4u) {
        return false;
    }
    if (width != width_ || height != height_) {
        return false;  // Caller must recreate the bitmap when size changes.
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    const size_t requiredSize = rowBytes * height;

    // Fast skip when caller pushes the same pixels we already uploaded.
    // DevTools / live-thumbnail style code often reuses a single
    // WriteableBitmap and calls UpdatePackedPixels every frame even when
    // the content didn't actually change — this turns into a 5–10 ms/frame
    // GPU upload per bitmap on the user's profile. The compare short-circuits
    // at the first differing pixel, so genuinely-changed bitmaps pay only the
    // cost of the diverging prefix; identical bitmaps short-circuit fast and we
    // skip the entire upload pipeline (preserving the d3d12TextureValid_ flag
    // the cached upload path checks). pixelData_ is stored PREMULTIPLIED while
    // the incoming pixels are straight, so we compare against the premultiplied
    // form of each source pixel rather than raw bytes (opaque a==255 rows still
    // compare byte-for-byte).
    if (pixelData_.size() == requiredSize && d3d12TextureValid_ && d3d12Texture_) {
        const uint32_t widthPixels = width;
        bool same = true;
        for (uint32_t row = 0; row < height; ++row) {
            if (!PremultipliedRowEquals(pixelData_.data() + row * rowBytes,
                                        pixels + static_cast<size_t>(row) * stride,
                                        widthPixels))
            {
                same = false;
                break;
            }
        }
        if (same) {
            bitmap_stats::AddMemcmpShortCircuit();
            return true;  // No-op: caller's pixels match what we already uploaded.
        }
    }

    if (pixelData_.size() != requiredSize) {
        pixelData_.resize(requiredSize);
    }

    if (stride == rowBytes) {
        std::memcpy(pixelData_.data(), pixels, requiredSize);
    } else {
        for (uint32_t row = 0; row < height; ++row) {
            std::memcpy(pixelData_.data() + row * rowBytes,
                        pixels + static_cast<size_t>(row) * stride,
                        rowBytes);
        }
    }

    // ABI contract: incoming pixels are STRAIGHT alpha. Premultiply the packed
    // copy so the premultiplied-blend bitmap PSO renders WriteableBitmap /
    // streamed frames correctly. pixelData_ is never read back as straight by
    // any consumer (video surface writes raw opaque frames and bypasses this
    // path entirely), so premultiplying it in place is safe.
    PremultiplyBgraInPlace(pixelData_.data(), static_cast<size_t>(width) * height);

    isDynamic_ = true;          // From now on, prefer in-place upload over recreate.
    d3d12TextureValid_ = false; // Re-upload pixels on next render; main texture stays.
    return true;
}

ID3D12Resource* D3D12Bitmap::GetOrCreateD3D12Texture(ID3D12Device* device, ID3D12GraphicsCommandList* cmdList) {
    if (!device || !cmdList) return nullptr;
    if (d3d12TextureValid_ && d3d12Texture_) {
        bitmap_stats::AddFastPathHit();
        return d3d12Texture_.Get();
    }
    if (pixelData_.empty() || width_ == 0 || height_ == 0) return nullptr;

    // Dynamic path (video frame, WriteableBitmap): reuse the existing
    // default-heap texture so GPU memory stays flat across uploads. Without
    // this the previous code would CreateCommittedResource(8MB) every frame
    // for a 1080p video, which thrashes the D3D12 deferred-release queue and
    // blackouts the swap chain when memory pressure spikes.
    const bool dynamicPath = isDynamic_ && d3d12Texture_ &&
                             d3d12Texture_->GetDesc().Width == width_ &&
                             d3d12Texture_->GetDesc().Height == height_;

    // Single mip level by default — UI content is 1:1 pixel mapped (icons,
    // images at native resolution, ImageBrush tiles), and anisotropic
    // sampling already handles minor minification by sampling multiple
    // texels per fragment. The previous full mip chain forced a CPU 2×2
    // box-filter pass for every level — for 1080p that's 11 levels of
    // memcpy + filter, which dominated the slow upload path (5–10 ms per
    // 1080p bitmap on the user's profile, as confirmed by the
    // jalium_query_bitmap_upload_stats telemetry showing 4 fresh uploads
    // × 8 MB = 32 MB/frame all going through the non-dynamic mip-generating
    // path). Skipping mip generation cuts that to zero CPU time and makes
    // the upload pipeline a straight memcpy + CopyTextureRegion. Aliasing
    // is bounded by the anisotropic sampler the bitmap PSO already uses;
    // applications that genuinely scale a large image down by 4× or more
    // (rare in UI) can request mips through a future explicit knob.
    const UINT16 mipLevels = 1;

    ComPtr<ID3D12Resource> newTexture;
    ComPtr<ID3D12Resource> newUploadBuffer;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width = width_;
    texDesc.Height = height_;
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels = mipLevels;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    texDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

    if (!dynamicPath) {
        D3D12_HEAP_PROPERTIES defaultHeap = {};
        defaultHeap.Type = D3D12_HEAP_TYPE_DEFAULT;

        HRESULT hr = device->CreateCommittedResource(
            &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COPY_DEST, nullptr,
            IID_PPV_ARGS(&newTexture));
        if (FAILED(hr)) return nullptr;
        newTexture->SetName(L"JaliumBitmapTexture");  // [JALIUM-921 diag] name so #921 points at the culprit
    }

    // Compute footprints / sizes for every mip level so we can pack them all
    // into a single upload buffer.
    std::vector<D3D12_PLACED_SUBRESOURCE_FOOTPRINT> footprints(mipLevels);
    std::vector<UINT> numRowsArr(mipLevels);
    std::vector<UINT64> rowSizeBytesArr(mipLevels);
    UINT64 uploadSize = 0;
    device->GetCopyableFootprints(&texDesc, 0, mipLevels, 0,
                                  footprints.data(), numRowsArr.data(),
                                  rowSizeBytesArr.data(), &uploadSize);

    // Upload-buffer reuse: for the dynamic-bitmap path (video frames,
    // WriteableBitmap whose content is refreshed each frame) this used to
    // CreateCommittedResource(uploadSize) every frame. Each call is ~1 ms
    // on Windows, so 9 dynamic bitmaps × 1 ms = ~9 ms/frame just on
    // upload-heap allocation. We keep d3d12UploadBuffer_ alive across
    // frames and reuse it when its size is sufficient. Frame-fence gating
    // in BeginFrame ensures the previous frame's GPU consumption finished
    // before we Map/memcpy new pixels into the same buffer.
    HRESULT hr = S_OK;
    if (dynamicPath && d3d12UploadBuffer_) {
        D3D12_RESOURCE_DESC existingDesc = d3d12UploadBuffer_->GetDesc();
        if (existingDesc.Width >= uploadSize) {
            newUploadBuffer = d3d12UploadBuffer_;
        }
    }
    if (!newUploadBuffer) {
        D3D12_HEAP_PROPERTIES uploadHeap = {};
        uploadHeap.Type = D3D12_HEAP_TYPE_UPLOAD;
        D3D12_RESOURCE_DESC bufDesc = {};
        bufDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
        bufDesc.Width = uploadSize;
        bufDesc.Height = 1;
        bufDesc.DepthOrArraySize = 1;
        bufDesc.MipLevels = 1;
        bufDesc.SampleDesc.Count = 1;
        bufDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

        hr = device->CreateCommittedResource(
            &uploadHeap, D3D12_HEAP_FLAG_NONE, &bufDesc,
            D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
            IID_PPV_ARGS(&newUploadBuffer));
        if (FAILED(hr)) return nullptr;
    }

    // Validate pixelData_ has enough data for level 0 before copying.
    const uint32_t srcRowPitch0 = width_ * 4;
    if (pixelData_.size() < (size_t)numRowsArr[0] * srcRowPitch0) {
        return nullptr;
    }

    // Map upload buffer and copy every mip level into its placed-footprint slot.
    void* mapped = nullptr;
    hr = newUploadBuffer->Map(0, nullptr, &mapped);
    if (FAILED(hr) || !mapped) {
        return nullptr;
    }

    // mipLevels is hard-coded to 1, so this loop runs exactly once with the
    // raw pixelData_ as the source. (The loop is left intact rather than
    // unrolled so reintroducing CPU-generated mip chains for a future
    // opt-in path stays a one-line change.)
    for (UINT16 m = 0; m < mipLevels; ++m) {
        const uint32_t levelW = std::max(1u, width_ >> m);
        const uint32_t srcRowPitch = levelW * 4;
        const uint8_t* src = pixelData_.data();
        uint8_t* dst = static_cast<uint8_t*>(mapped) + footprints[m].Offset;
        const UINT rows = numRowsArr[m];
        const UINT dstRowPitch = footprints[m].Footprint.RowPitch;
        for (UINT row = 0; row < rows; ++row) {
            memcpy(dst + row * dstRowPitch,
                   src + row * srcRowPitch,
                   srcRowPitch);
        }
    }
    newUploadBuffer->Unmap(0, nullptr);

    ID3D12Resource* targetTexture = dynamicPath ? d3d12Texture_.Get() : newTexture.Get();

    // Dynamic path: transition the existing texture back to COPY_DEST so we can
    // overwrite its pixels. After upload we transition back to PIXEL_SHADER_RESOURCE.
    if (dynamicPath) {
        D3D12_RESOURCE_BARRIER toCopy = {};
        toCopy.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        toCopy.Transition.pResource = targetTexture;
        toCopy.Transition.StateBefore = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
        toCopy.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
        toCopy.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        cmdList->ResourceBarrier(1, &toCopy);
    }

    // Issue per-mip copy commands.
    for (UINT16 m = 0; m < mipLevels; ++m) {
        D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
        srcLoc.pResource = newUploadBuffer.Get();
        srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        srcLoc.PlacedFootprint = footprints[m];

        D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
        dstLoc.pResource = targetTexture;
        dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        dstLoc.SubresourceIndex = m;

        cmdList->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);
    }

    // Transition all subresources to shader resource state.
    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = targetTexture;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    cmdList->ResourceBarrier(1, &barrier);

    if (!dynamicPath) {
        // Slow path (static UI bitmap): retire the previous-generation
        // texture + upload buffer through the backend graveyard so they
        // survive until the most recently submitted fence is GPU-complete.
        // pendingRelease_ used to be cleared here unconditionally, which
        // relied on the renderer's BeginFrame fence wait having already
        // drained N-2 — true for the renderer path, but D3D12Bitmap is
        // also constructed/destroyed off-frame (cache synthesis, GC), so
        // funnel everything through the same fence-gated path for safety.
        if (backend_) {
            for (auto& r : pendingRelease_) {
                if (r) backend_->RetireGpuResource(std::move(r));
            }
        }
        pendingRelease_.clear();
        if (d3d12Texture_) {
            pendingRelease_.push_back(std::move(d3d12Texture_));
        }
        // d3d12UploadBuffer_ is retired below via the size-changed branch.
        d3d12Texture_ = std::move(newTexture);
    }
    // If we reused d3d12UploadBuffer_ above (newUploadBuffer aliases it), the
    // assignment below is a no-op and we don't push to pendingRelease_. Only
    // when we created a fresh buffer do we retire the old one for next-frame GC.
    if (newUploadBuffer.Get() != d3d12UploadBuffer_.Get()) {
        if (d3d12UploadBuffer_) {
            pendingRelease_.push_back(std::move(d3d12UploadBuffer_));
        }
        d3d12UploadBuffer_ = std::move(newUploadBuffer);
    }
    d3d12TextureValid_ = true;

    // Telemetry: classify the upload — dynamic-reuse path skips
    // CreateCommittedResource / mip generation; the slow path pays both.
    // GPU-resident accounting: dynamic-reuse overwrites in-place (delta 0);
    // slow path created a fresh texture so the bitmap's GPU footprint
    // grows by (uploadSize - previousPinnedBytes). The destructor below
    // releases the final tally.
    bitmap_stats::AddUpload(uploadSize);
    if (dynamicPath) {
        bitmap_stats::AddDynamicReuse();
    } else {
        int64_t delta = static_cast<int64_t>(uploadSize) -
                        static_cast<int64_t>(pinnedGpuBytes_);
        if (delta != 0) bitmap_stats::AddGpuResidentBytes(delta);
        pinnedGpuBytes_ = uploadSize;
    }

    return d3d12Texture_.Get();
}

D3D12Bitmap::~D3D12Bitmap() {
    if (pinnedGpuBytes_ != 0) {
        bitmap_stats::AddGpuResidentBytes(-static_cast<int64_t>(pinnedGpuBytes_));
        pinnedGpuBytes_ = 0;
    }

    // Forward live GPU resources to the backend's fence-gated graveyard.
    // The bitmap can be destroyed from *any* thread — worker pool (cache
    // eviction), GC finalizer, UI thread — and may be destroyed mid-frame
    // while the texture / upload buffer are still bound to an open command
    // list (CopyTextureRegion source) or sampled by an in-flight draw. If
    // we let the ComPtr members destruct here, that calls Release on the
    // last ref and triggers D3D12 ERROR #921
    // OBJECT_DELETED_WHILE_STILL_IN_USE on the next command queue execute.
    //
    // The backend keeps each ComPtr alive at refcount ≥ 1, tags it with the
    // latest submitted fence value, and frees it only after that fence is
    // GPU-complete (via ReclaimRetiredGpuResources, called from the renderer
    // at frame boundaries after its fence wait succeeds).
    if (backend_) {
        if (d3d12Texture_) {
            backend_->RetireGpuResource(std::move(d3d12Texture_));
        }
        if (d3d12UploadBuffer_) {
            backend_->RetireGpuResource(std::move(d3d12UploadBuffer_));
        }
        for (auto& r : pendingRelease_) {
            if (r) backend_->RetireGpuResource(std::move(r));
        }
        pendingRelease_.clear();
    }
    // Without a backend (defensive: shouldn't happen since the factory
    // always passes one), the ComPtr destructors run as normal.
}

void D3D12Bitmap::ReleasePendingResources() {
    if (backend_) {
        for (auto& r : pendingRelease_) {
            if (r) backend_->RetireGpuResource(std::move(r));
        }
        pendingRelease_.clear();
    } else {
        pendingRelease_.clear();
    }
}

} // namespace jalium
