#include "d3d12_resources.h"
#include "d3d12_backend.h"
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

D3D12Bitmap::D3D12Bitmap(uint32_t width, uint32_t height)
    : width_(width)
    , height_(height)
{
}

void D3D12Bitmap::SetBitmapData(const uint8_t* data, uint32_t dataSize) {
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

    isDynamic_ = true;          // From now on, prefer in-place upload over recreate.
    d3d12TextureValid_ = false; // Re-upload pixels on next render; main texture stays.
    return true;
}

ID3D12Resource* D3D12Bitmap::GetOrCreateD3D12Texture(ID3D12Device* device, ID3D12GraphicsCommandList* cmdList) {
    if (!device || !cmdList) return nullptr;
    if (d3d12TextureValid_ && d3d12Texture_) return d3d12Texture_.Get();
    if (pixelData_.empty() || width_ == 0 || height_ == 0) return nullptr;

    // Dynamic path (video frame, WriteableBitmap): single mip level, reuse the
    // existing default-heap texture so GPU memory stays flat across uploads.
    // Without this the previous code would CreateCommittedResource(8MB) every frame
    // for a 1080p video, which thrashes the D3D12 deferred-release queue and
    // blackouts the swap chain when memory pressure spikes.
    const bool dynamicPath = isDynamic_ && d3d12Texture_ &&
                             d3d12Texture_->GetDesc().Width == width_ &&
                             d3d12Texture_->GetDesc().Height == height_;

    const UINT16 mipLevels = dynamicPath ? UINT16{1} : ComputeMipLevels(width_, height_);

    // Generate per-level pixel buffers. Level 0 references pixelData_ directly;
    // subsequent levels are CPU-downsampled with a 2x2 box filter.
    std::vector<std::vector<uint8_t>> mipPixels;
    mipPixels.reserve(mipLevels);
    mipPixels.emplace_back();  // level 0 stays empty — we'll alias pixelData_

    for (UINT16 m = 1; m < mipLevels; ++m) {
        const uint32_t prevW = std::max(1u, width_ >> (m - 1));
        const uint32_t prevH = std::max(1u, height_ >> (m - 1));
        const uint32_t curW = std::max(1u, width_ >> m);
        const uint32_t curH = std::max(1u, height_ >> m);
        std::vector<uint8_t> level(static_cast<size_t>(curW) * curH * 4);
        const uint8_t* src = (m == 1) ? pixelData_.data() : mipPixels[m - 1].data();
        DownsampleBoxBgra(src, prevW, prevH, level.data(), curW, curH);
        mipPixels.emplace_back(std::move(level));
    }

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

    HRESULT hr = device->CreateCommittedResource(
        &uploadHeap, D3D12_HEAP_FLAG_NONE, &bufDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ, nullptr,
        IID_PPV_ARGS(&newUploadBuffer));
    if (FAILED(hr)) return nullptr;

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

    for (UINT16 m = 0; m < mipLevels; ++m) {
        const uint32_t levelW = std::max(1u, width_ >> m);
        const uint32_t srcRowPitch = levelW * 4;
        const uint8_t* src = (m == 0) ? pixelData_.data() : mipPixels[m].data();
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
        // Slow path (static UI bitmap): retire N-2 generation (already drained
        // by the current frame's fence wait) and stage N-1 for next-frame retire.
        pendingRelease_.clear();
        if (d3d12Texture_ || d3d12UploadBuffer_) {
            pendingRelease_.push_back(std::move(d3d12Texture_));
            pendingRelease_.push_back(std::move(d3d12UploadBuffer_));
        }
        d3d12Texture_ = std::move(newTexture);
    }
    // Always retire the previous upload buffer; the new one carries this frame's pixels.
    if (d3d12UploadBuffer_) {
        pendingRelease_.push_back(std::move(d3d12UploadBuffer_));
    }
    d3d12UploadBuffer_ = std::move(newUploadBuffer);
    d3d12TextureValid_ = true;
    return d3d12Texture_.Get();
}

void D3D12Bitmap::ReleasePendingResources() {
    pendingRelease_.clear();
}

} // namespace jalium
