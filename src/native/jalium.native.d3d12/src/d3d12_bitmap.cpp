#include "d3d12_resources.h"
#include "d3d12_backend.h"

namespace jalium {

D3D12Bitmap::D3D12Bitmap(uint32_t width, uint32_t height)
    : width_(width)
    , height_(height)
{
}

void D3D12Bitmap::SetBitmapData(const uint8_t* data, uint32_t dataSize) {
    pixelData_.assign(data, data + dataSize);
    d3d12TextureValid_ = false;  // Force re-upload on next use
}

ID3D12Resource* D3D12Bitmap::GetOrCreateD3D12Texture(ID3D12Device* device, ID3D12GraphicsCommandList* cmdList) {
    if (!device || !cmdList) return nullptr;
    if (d3d12TextureValid_ && d3d12Texture_) return d3d12Texture_.Get();
    if (pixelData_.empty() || width_ == 0 || height_ == 0) return nullptr;

    // Create the default heap texture into a temporary ComPtr.
    // The old texture/upload buffer are kept alive in pendingRelease_ until GPU
    // finishes using them (the caller must ensure a fence wait before next re-upload).
    ComPtr<ID3D12Resource> newTexture;
    ComPtr<ID3D12Resource> newUploadBuffer;

    D3D12_RESOURCE_DESC texDesc = {};
    texDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    texDesc.Width = width_;
    texDesc.Height = height_;
    texDesc.DepthOrArraySize = 1;
    texDesc.MipLevels = 1;
    texDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.Layout = D3D12_TEXTURE_LAYOUT_UNKNOWN;
    texDesc.Flags = D3D12_RESOURCE_FLAG_NONE;

    D3D12_HEAP_PROPERTIES defaultHeap = {};
    defaultHeap.Type = D3D12_HEAP_TYPE_DEFAULT;

    HRESULT hr = device->CreateCommittedResource(
        &defaultHeap, D3D12_HEAP_FLAG_NONE, &texDesc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr,
        IID_PPV_ARGS(&newTexture));
    if (FAILED(hr)) return nullptr;

    // Create upload buffer for the texture data
    UINT64 uploadSize = 0;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint = {};
    UINT numRows = 0;
    UINT64 rowSizeBytes = 0;
    device->GetCopyableFootprints(&texDesc, 0, 1, 0, &footprint, &numRows, &rowSizeBytes, &uploadSize);

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

    // Validate pixelData_ has enough data before copying
    uint32_t srcRowPitch = width_ * 4;
    if (pixelData_.size() < (size_t)numRows * srcRowPitch) {
        return nullptr;
    }

    // Copy pixel data to upload buffer
    void* mapped = nullptr;
    hr = newUploadBuffer->Map(0, nullptr, &mapped);
    if (FAILED(hr) || !mapped) {
        return nullptr;
    }
    uint8_t* dst = static_cast<uint8_t*>(mapped) + footprint.Offset;
    for (UINT row = 0; row < numRows; row++) {
        memcpy(dst + row * footprint.Footprint.RowPitch,
               pixelData_.data() + row * srcRowPitch,
               srcRowPitch);
    }
    newUploadBuffer->Unmap(0, nullptr);

    // Issue copy command
    D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
    srcLoc.pResource = newUploadBuffer.Get();
    srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    srcLoc.PlacedFootprint = footprint;

    D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
    dstLoc.pResource = newTexture.Get();
    dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dstLoc.SubresourceIndex = 0;

    cmdList->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);

    // Transition to shader resource
    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = newTexture.Get();
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    cmdList->ResourceBarrier(1, &barrier);

    // Move old resources to pending-release list (GPU may still reference them
    // from a previous frame's draw calls). We keep only one generation of old
    // resources — any older ones are guaranteed complete by now since we waited
    // for the current frame's fence in BeginFrame before reaching this code.
    pendingRelease_.clear();  // release N-2 generation (safe — GPU done with it)
    if (d3d12Texture_ || d3d12UploadBuffer_) {
        // Keep N-1 generation alive (may still be in flight)
        pendingRelease_.push_back(std::move(d3d12Texture_));
        pendingRelease_.push_back(std::move(d3d12UploadBuffer_));
    }

    d3d12Texture_ = std::move(newTexture);
    d3d12UploadBuffer_ = std::move(newUploadBuffer);
    d3d12TextureValid_ = true;
    return d3d12Texture_.Get();
}

void D3D12Bitmap::ReleasePendingResources() {
    pendingRelease_.clear();
}

} // namespace jalium
