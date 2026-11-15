// Exercise the actual shipped fine shader without a pre-dispatch clear.
// Empty tiles and allocator failure must overwrite dirty output, while pixels
// outside target_width/height in a larger pooled texture must remain untouched.
#include <d3d12.h>
#include <d3d12sdklayers.h>
#include <wrl/client.h>

#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>

#include "d3d12_vello_bytecode.h"
#include "jalium_vello_encode.h"

using Microsoft::WRL::ComPtr;

static void Check(HRESULT result)
{
    if (FAILED(result)) throw std::runtime_error("D3D12 operation failed");
}

static ComPtr<ID3D12Resource> Buffer(ID3D12Device* device, uint64_t bytes,
                                    D3D12_HEAP_TYPE type)
{
    D3D12_HEAP_PROPERTIES heap = {};
    heap.Type = type;
    D3D12_RESOURCE_DESC desc = {};
    desc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    desc.Width = bytes;
    desc.Height = desc.DepthOrArraySize = desc.MipLevels = 1;
    desc.SampleDesc.Count = 1;
    desc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    ComPtr<ID3D12Resource> resource;
    Check(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &desc,
        type == D3D12_HEAP_TYPE_UPLOAD ? D3D12_RESOURCE_STATE_GENERIC_READ
                                     : D3D12_RESOURCE_STATE_COPY_DEST,
        nullptr, IID_PPV_ARGS(&resource)));
    return resource;
}

static void Upload(ID3D12Resource* resource, const void* source, size_t bytes)
{
    void* mapped = nullptr;
    D3D12_RANGE empty = {0, 0};
    Check(resource->Map(0, &empty, &mapped));
    std::memcpy(mapped, source, bytes);
    resource->Unmap(0, nullptr);
}

static void Transition(ID3D12GraphicsCommandList* cmd, ID3D12Resource* resource,
                       D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
{
    D3D12_RESOURCE_BARRIER barrier = {};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition = {resource, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, before, after};
    cmd->ResourceBarrier(1, &barrier);
}

int main()
try {
    ComPtr<ID3D12Debug> debug;
    if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debug)))) debug->EnableDebugLayer();
    ComPtr<ID3D12Device> device;
    if (FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device)))) {
        std::cout << "SKIP: no D3D12 device\n";
        return 77;
    }
    ComPtr<ID3D12InfoQueue> messages;
    device.As(&messages);

    D3D12_DESCRIPTOR_RANGE ranges[2] = {};
    ranges[0] = {D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 5, 0, 0, 0};
    ranges[1] = {D3D12_DESCRIPTOR_RANGE_TYPE_UAV, 2, 0, 0, 0};
    D3D12_ROOT_PARAMETER params[3] = {};
    params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
    for (int i = 0; i < 2; i++) {
        params[i + 1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        params[i + 1].DescriptorTable = {1, &ranges[i]};
    }
    D3D12_ROOT_SIGNATURE_DESC rootDesc = {};
    rootDesc.NumParameters = 3;
    rootDesc.pParameters = params;
    ComPtr<ID3DBlob> serialized;
    Check(D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1,
                                      &serialized, nullptr));
    ComPtr<ID3D12RootSignature> root;
    Check(device->CreateRootSignature(0, serialized->GetBufferPointer(),
                                      serialized->GetBufferSize(), IID_PPV_ARGS(&root)));
    D3D12_COMPUTE_PIPELINE_STATE_DESC pipelineDesc = {};
    pipelineDesc.pRootSignature = root.Get();
    pipelineDesc.CS = {vello_bytecode::kFine, vello_bytecode::kFineSize};
    ComPtr<ID3D12PipelineState> pipeline;
    Check(device->CreateComputePipelineState(&pipelineDesc, IID_PPV_ARGS(&pipeline)));

    D3D12_RESOURCE_DESC outputDesc = {};
    outputDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    outputDesc.Width = outputDesc.Height = 32;
    outputDesc.DepthOrArraySize = outputDesc.MipLevels = 1;
    outputDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    outputDesc.SampleDesc.Count = 1;
    outputDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
    D3D12_HEAP_PROPERTIES outputHeap = {};
    outputHeap.Type = D3D12_HEAP_TYPE_DEFAULT;
    ComPtr<ID3D12Resource> output;
    Check(device->CreateCommittedResource(&outputHeap, D3D12_HEAP_FLAG_NONE, &outputDesc,
        D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&output)));
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint = {};
    uint64_t textureBytes = 0;
    device->GetCopyableFootprints(&outputDesc, 0, 1, 0, &footprint, nullptr, nullptr,
                                  &textureBytes);
    auto initial = Buffer(device.Get(), textureBytes, D3D12_HEAP_TYPE_UPLOAD);
    auto readback = Buffer(device.Get(), textureBytes, D3D12_HEAP_TYPE_READBACK);
    auto config = Buffer(device.Get(), 256, D3D12_HEAP_TYPE_UPLOAD);
    auto ptcl = Buffer(device.Get(), 1024, D3D12_HEAP_TYPE_UPLOAD);
    std::vector<uint8_t> dirty(textureBytes, 0x7f);
    Upload(initial.Get(), dirty.data(), dirty.size());
    jalium::VelloConfig cfg = {};
    cfg.width_in_tiles = cfg.height_in_tiles = 2;
    cfg.target_width = 19;
    cfg.target_height = 21;
    Upload(config.Get(), &cfg, sizeof(cfg));

    D3D12_DESCRIPTOR_HEAP_DESC heapDesc = {};
    heapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
    heapDesc.NumDescriptors = 7;
    heapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
    ComPtr<ID3D12DescriptorHeap> descriptors;
    Check(device->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&descriptors)));
    const uint32_t stride = device->GetDescriptorHandleIncrementSize(heapDesc.Type);
    auto cpu = [&](uint32_t i) {
        auto handle = descriptors->GetCPUDescriptorHandleForHeapStart();
        handle.ptr += i * stride;
        return handle;
    };
    for (uint32_t i = 0; i < 5; i++) {
        D3D12_SHADER_RESOURCE_VIEW_DESC view = {};
        view.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        if (i < 3) {
            view.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
            view.Buffer.StructureByteStride = i == 0 ? jalium::kVelloStrideSegment : 4;
            view.Buffer.NumElements = i == 1 ? 256 : 1;
        } else {
            view.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            view.Format = outputDesc.Format;
            view.Texture2D.MipLevels = 1;
        }
        device->CreateShaderResourceView(i == 1 ? ptcl.Get() : nullptr, &view, cpu(i));
    }
    D3D12_UNORDERED_ACCESS_VIEW_DESC blendView = {};
    blendView.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
    blendView.Buffer.NumElements = 1;
    blendView.Buffer.StructureByteStride = 4;
    device->CreateUnorderedAccessView(nullptr, nullptr, &blendView, cpu(5));
    D3D12_UNORDERED_ACCESS_VIEW_DESC outputView = {};
    outputView.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
    outputView.Format = outputDesc.Format;
    device->CreateUnorderedAccessView(output.Get(), nullptr, &outputView, cpu(6));

    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    ComPtr<ID3D12CommandQueue> queue;
    Check(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)));
    ComPtr<ID3D12CommandAllocator> allocator;
    Check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator)));
    ComPtr<ID3D12GraphicsCommandList> cmd;
    Check(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(),
                                    pipeline.Get(), IID_PPV_ARGS(&cmd)));
    ComPtr<ID3D12Fence> fence;
    Check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)));

    for (uint32_t failed = 0; failed < 2; failed++) {
        uint32_t commands[256] = {}; // Empty tiles: blend offset 0, CMD_END 0.
        commands[0] = failed ? UINT32_MAX : 0;
        Upload(ptcl.Get(), commands, sizeof(commands));
        if (failed) {
            Check(allocator->Reset());
            Check(cmd->Reset(allocator.Get(), pipeline.Get()));
            Transition(cmd.Get(), output.Get(), D3D12_RESOURCE_STATE_COPY_SOURCE,
                        D3D12_RESOURCE_STATE_COPY_DEST);
        }
        D3D12_TEXTURE_COPY_LOCATION texture = {};
        texture.pResource = output.Get();
        texture.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
        D3D12_TEXTURE_COPY_LOCATION buffer = {};
        buffer.pResource = initial.Get();
        buffer.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
        buffer.PlacedFootprint = footprint;
        cmd->CopyTextureRegion(&texture, 0, 0, 0, &buffer, nullptr);
        Transition(cmd.Get(), output.Get(), D3D12_RESOURCE_STATE_COPY_DEST,
                    D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        ID3D12DescriptorHeap* heap = descriptors.Get();
        cmd->SetDescriptorHeaps(1, &heap);
        cmd->SetComputeRootSignature(root.Get());
        cmd->SetComputeRootConstantBufferView(0, config->GetGPUVirtualAddress());
        auto gpu = descriptors->GetGPUDescriptorHandleForHeapStart();
        cmd->SetComputeRootDescriptorTable(1, gpu);
        gpu.ptr += 5 * stride;
        cmd->SetComputeRootDescriptorTable(2, gpu);
        cmd->Dispatch(2, 2, 1);
        Transition(cmd.Get(), output.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                    D3D12_RESOURCE_STATE_COPY_SOURCE);
        buffer.pResource = readback.Get();
        cmd->CopyTextureRegion(&buffer, 0, 0, 0, &texture, nullptr);
        Check(cmd->Close());
        ID3D12CommandList* lists[] = {cmd.Get()};
        queue->ExecuteCommandLists(1, lists);
        Check(queue->Signal(fence.Get(), failed + 1));
        HANDLE event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!event) throw std::runtime_error("CreateEvent failed");
        HRESULT eventResult = fence->SetEventOnCompletion(failed + 1, event);
        DWORD waitResult = SUCCEEDED(eventResult) ? WaitForSingleObject(event, 15000) : WAIT_FAILED;
        CloseHandle(event);
        if (waitResult != WAIT_OBJECT_0) throw std::runtime_error("GPU fence timed out");
        void* mapped = nullptr;
        Check(readback->Map(0, nullptr, &mapped));
        std::vector<uint8_t> pixels(textureBytes);
        std::memcpy(pixels.data(), mapped, pixels.size());
        D3D12_RANGE empty = {0, 0};
        readback->Unmap(0, &empty);
        for (uint32_t y = 0; y < 32; y++) for (uint32_t x = 0; x < 32; x++) {
            const uint8_t expected = x < cfg.target_width && y < cfg.target_height ? 0 : 0x7f;
            for (uint32_t c = 0; c < 4; c++) {
                if (pixels[y * footprint.Footprint.RowPitch + x * 4 + c] != expected) {
                    throw std::runtime_error(failed ? "Failed scene left stale pixels"
                                                    : "Empty scene left stale pixels");
                }
            }
        }
    }
    if (messages) {
        for (uint64_t i = 0; i < messages->GetNumStoredMessages(); i++) {
            SIZE_T size = 0;
            Check(messages->GetMessage(i, nullptr, &size));
            std::vector<uint8_t> storage(size);
            auto* message = reinterpret_cast<D3D12_MESSAGE*>(storage.data());
            Check(messages->GetMessage(i, message, &size));
            if (message->Severity <= D3D12_MESSAGE_SEVERITY_ERROR) {
                throw std::runtime_error(message->pDescription);
            }
        }
    }
    std::cout << "PASS: fine overwrites empty/failed tiles and preserves pixels outside the region\n";
    return 0;
} catch (const std::exception& error) {
    std::cerr << error.what() << '\n';
    return 1;
}
