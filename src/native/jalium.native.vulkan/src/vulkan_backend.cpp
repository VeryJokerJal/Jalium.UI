#include "vulkan_backend.h"

#include "vulkan_render_target.h"
#include "vulkan_runtime.h"

#include <cstring>
#include <cwchar>

#define STB_IMAGE_IMPLEMENTATION
#define STBI_NO_STDIO
#define STBI_FAILURE_USERMSG
#include <stb_image.h>

namespace jalium {

namespace {

Bitmap* DecodeBitmapWithStb(const uint8_t* data, uint32_t dataSize)
{
    int width = 0;
    int height = 0;
    int channels = 0;
    stbi_uc* decodedPixels = stbi_load_from_memory(
        data,
        static_cast<int>(dataSize),
        &width,
        &height,
        &channels,
        STBI_rgb_alpha);
    if (!decodedPixels || width <= 0 || height <= 0) {
        if (decodedPixels) {
            stbi_image_free(decodedPixels);
        }
        return nullptr;
    }

    const size_t pixelDataSize = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
    if (pixelDataSize == 0) {
        stbi_image_free(decodedPixels);
        return nullptr;
    }

    std::vector<uint8_t> bgraPixels(pixelDataSize, 0);
    // STBI_rgb_alpha guarantees decodedPixels has exactly pixelDataSize bytes (RGBA).
    // Convert RGBA → BGRA for Vulkan surface compatibility.
    for (size_t offset = 0; offset + 3 < pixelDataSize; offset += 4u) {
        bgraPixels[offset + 0] = decodedPixels[offset + 2];
        bgraPixels[offset + 1] = decodedPixels[offset + 1];
        bgraPixels[offset + 2] = decodedPixels[offset + 0];
        bgraPixels[offset + 3] = decodedPixels[offset + 3];
    }

    stbi_image_free(decodedPixels);
    return new VulkanBitmap(static_cast<uint32_t>(width), static_cast<uint32_t>(height), std::move(bgraPixels));
}

} // namespace

bool VulkanBackend::Initialize()
{
    if (initialized_) {
        return true;
    }

    if (!IsVulkanRuntimeAvailable()) {
        return false;
    }

#ifndef _WIN32
    // Initialize cross-platform text engine (FreeType + HarfBuzz)
    textEngine_ = std::make_unique<TextEngine>();
    JaliumResult textResult = textEngine_->Initialize();
    if (textResult != JALIUM_OK) {
        // Text engine is optional — degrade gracefully
        textEngine_.reset();
    }
#endif

    initialized_ = true;
    return true;
}

JaliumResult VulkanBackend::GetAdapterInfo(JaliumAdapterInfo* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumAdapterInfo{};
    if (!adapterInfoValid_) return JALIUM_ERROR_NOT_SUPPORTED;
    *out = adapterInfo_;
    return JALIUM_OK;
}

void VulkanBackend::RegisterAdapterInfo(VkPhysicalDevice physicalDevice,
                                        const VkPhysicalDeviceProperties& props,
                                        const VkPhysicalDeviceMemoryProperties& memProps)
{
    // 同一物理设备重复调用时短路：第一帧之后每帧都会"重新"经过这条路径，
    // 但 cache 已经稳定，避免重复转换/转码。
    if (adapterInfoValid_ && cachedPhysicalDevice_ == physicalDevice) {
        return;
    }

    JaliumAdapterInfo info{};

    // VkPhysicalDeviceProperties::deviceName 是定长 char[VK_MAX_PHYSICAL_DEVICE_NAME_SIZE]
    // (= 256, UTF-8)。Jalium 用 wchar_t[128]，做 UTF-8→UTF-16 转换 + 截断。
    // 在 Windows 上用 MultiByteToWideChar；其它平台用 std::mbstowcs 作为最简兜底
    // （足够覆盖 ASCII GPU 名字，特殊字符会被替换为 '?' 但不会越界）。
#if defined(_WIN32)
    int wlen = MultiByteToWideChar(
        CP_UTF8, 0, props.deviceName, -1, nullptr, 0);
    if (wlen > 0) {
        const int cap = static_cast<int>(_countof(info.name));
        if (wlen > cap) wlen = cap;
        MultiByteToWideChar(CP_UTF8, 0, props.deviceName, -1, info.name, wlen);
        info.name[cap - 1] = L'\0';
    }
#else
    std::mbstowcs(info.name, props.deviceName,
                  sizeof(info.name) / sizeof(info.name[0]) - 1);
    info.name[sizeof(info.name) / sizeof(info.name[0]) - 1] = L'\0';
#endif

    // adapterType: 直接映射 VkPhysicalDeviceType → JaliumGpuAdapterType。
    switch (props.deviceType) {
    case VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU:
        info.adapterType = JALIUM_GPU_ADAPTER_TYPE_DISCRETE;
        break;
    case VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU:
        info.adapterType = JALIUM_GPU_ADAPTER_TYPE_INTEGRATED;
        break;
    case VK_PHYSICAL_DEVICE_TYPE_CPU:
        info.adapterType = JALIUM_GPU_ADAPTER_TYPE_SOFTWARE;
        break;
    case VK_PHYSICAL_DEVICE_TYPE_VIRTUAL_GPU:
        // virtual GPU (云端 / 容器) 没专属类别，归 Discrete 让 DevTools 至少
        // 不显示 "Unknown"；UI 已经能从 vendorId 看出端倪。
        info.adapterType = JALIUM_GPU_ADAPTER_TYPE_DISCRETE;
        break;
    case VK_PHYSICAL_DEVICE_TYPE_OTHER:
    default:
        // Vulkan 把"未分类"GPU 归到 OTHER；按 D3D12 端 fallback 习惯，
        // 凭 DEVICE_LOCAL 堆大小猜测：>= 256 MiB 视作离散，否则集成。
        {
            VkDeviceSize deviceLocalBytes = 0;
            for (uint32_t i = 0; i < memProps.memoryHeapCount; ++i) {
                if ((memProps.memoryHeaps[i].flags & VK_MEMORY_HEAP_DEVICE_LOCAL_BIT) != 0) {
                    deviceLocalBytes += memProps.memoryHeaps[i].size;
                }
            }
            info.adapterType = deviceLocalBytes >= (256ull << 20)
                ? JALIUM_GPU_ADAPTER_TYPE_DISCRETE
                : JALIUM_GPU_ADAPTER_TYPE_INTEGRATED;
        }
        break;
    }

    // VRAM 估算：DEVICE_LOCAL 总和 → dedicatedVideoMemory；
    //            HOST_VISIBLE && !DEVICE_LOCAL 总和 → sharedSystemMemory。
    // 这跟 DXGI_ADAPTER_DESC1 的语义并不完全对齐——DXGI 的 dedicated 是
    // "GPU 独占且 CPU 不可见"的部分，UMA iGPU 那一栏会是 0，分享内存进
    // sharedSystemMemory。Vulkan 的 DEVICE_LOCAL 在 UMA 上同样涵盖系统内存，
    // 所以 iGPU 这里会把"看得到的系统内存"算进 dedicatedVideoMemory。
    // DevTools 文案标注 "GPU memory (Vulkan: best-effort)" 已经给宿主对齐预期。
    uint64_t deviceLocalBytes = 0;
    uint64_t hostVisibleNonDeviceLocalBytes = 0;
    for (uint32_t i = 0; i < memProps.memoryHeapCount; ++i) {
        const auto& heap = memProps.memoryHeaps[i];
        const bool deviceLocal = (heap.flags & VK_MEMORY_HEAP_DEVICE_LOCAL_BIT) != 0;
        if (deviceLocal) {
            deviceLocalBytes += heap.size;
        } else {
            // 找一条引用此 heap 的 host-visible memory type 才算共享系统内存。
            for (uint32_t t = 0; t < memProps.memoryTypeCount; ++t) {
                if (memProps.memoryTypes[t].heapIndex == i &&
                    (memProps.memoryTypes[t].propertyFlags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0) {
                    hostVisibleNonDeviceLocalBytes += heap.size;
                    break;
                }
            }
        }
    }
    info.dedicatedVideoMemory = deviceLocalBytes;
    info.sharedSystemMemory   = hostVisibleNonDeviceLocalBytes;

    info.vendorId = props.vendorID;
    info.deviceId = props.deviceID;

    adapterInfo_ = info;
    cachedPhysicalDevice_ = physicalDevice;
    adapterInfoValid_ = true;
}

RenderTarget* VulkanBackend::CreateRenderTarget(void* hwnd, int32_t width, int32_t height)
{
    JaliumSurfaceDescriptor surface {};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetForSurface(&surface, width, height);
}

RenderTarget* VulkanBackend::CreateRenderTargetForComposition(void* hwnd, int32_t width, int32_t height)
{
    JaliumSurfaceDescriptor surface {};
    surface.platform = JALIUM_PLATFORM_WINDOWS;
    surface.kind = JALIUM_SURFACE_KIND_COMPOSITION_TARGET;
    surface.handle0 = reinterpret_cast<intptr_t>(hwnd);
    return CreateRenderTargetForCompositionSurface(&surface, width, height);
}

RenderTarget* VulkanBackend::CreateRenderTargetForSurface(
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    if (!Initialize() || !surface || surface->handle0 == 0) {
        return nullptr;
    }

    auto* rt = new VulkanRenderTarget(this, *surface, width, height, false);
    if (!rt->IsInitialized()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

RenderTarget* VulkanBackend::CreateRenderTargetForCompositionSurface(
    const JaliumSurfaceDescriptor* surface,
    int32_t width,
    int32_t height)
{
    if (!Initialize() || !surface || surface->handle0 == 0) {
        return nullptr;
    }

    auto* rt = new VulkanRenderTarget(this, *surface, width, height, true);
    if (!rt->IsInitialized()) {
        delete rt;
        return nullptr;
    }
    return rt;
}

Brush* VulkanBackend::CreateSolidBrush(float r, float g, float b, float a)
{
    return new VulkanSolidBrush(r, g, b, a);
}

Brush* VulkanBackend::CreateLinearGradientBrush(
    float startX, float startY, float endX, float endY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new VulkanLinearGradientBrush(startX, startY, endX, endY, stops, stopCount);
}

Brush* VulkanBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t /*spreadMethod*/)
{
    return new VulkanRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount);
}

TextFormat* VulkanBackend::CreateTextFormat(
    const wchar_t* fontFamily,
    float fontSize,
    int32_t fontWeight,
    int32_t fontStyle)
{
#ifdef _WIN32
    if (!Initialize()) {
        return nullptr;
    }

    return new VulkanTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
#else
    if (!Initialize()) {
        return nullptr;
    }

    // Use cross-platform text engine if available
    if (textEngine_) {
        return textEngine_->CreateTextFormat(fontFamily, fontSize, fontWeight, fontStyle);
    }

    // Fallback: stub text format with approximate metrics
    return new VulkanTextFormat(textEngine_.get(), fontFamily, fontSize, fontWeight, fontStyle);
#endif
}

Bitmap* VulkanBackend::CreateBitmapFromMemory(const uint8_t* data, uint32_t dataSize)
{
    if (!Initialize() || !data || dataSize == 0) {
        return nullptr;
    }

    return DecodeBitmapWithStb(data, dataSize);
}

Bitmap* VulkanBackend::CreateBitmapFromPixels(const uint8_t* pixels, uint32_t width, uint32_t height, uint32_t stride)
{
    if (!Initialize() || !pixels || width == 0 || height == 0 || stride < width * 4u) {
        return nullptr;
    }

    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    std::vector<uint8_t> packedPixels(static_cast<size_t>(width) * static_cast<size_t>(height) * 4u, 0);
    for (uint32_t row = 0; row < height; ++row) {
        const auto* sourceRow = pixels + static_cast<size_t>(row) * stride;
        auto* destRow = packedPixels.data() + static_cast<size_t>(row) * rowBytes;
        std::memcpy(destRow, sourceRow, rowBytes);
    }

    return new VulkanBitmap(width, height, std::move(packedPixels));
}

// ── InkCanvas GPU pipeline ──────────────────────────────────────────────────

void VulkanBackend::RegisterDeviceContext(const VulkanDeviceContext& ctx)
{
    std::lock_guard<std::mutex> lk(inkMutex_);
    if (deviceContext_.valid && deviceContext_.device == ctx.device) {
        return;  // same device already registered
    }
    // A different device superseded the old one (e.g. the first window closed
    // and a new one opened). Drop the stale pipeline so it rebuilds on demand.
    if (deviceContext_.valid && deviceContext_.device != ctx.device) {
        brushPipeline_.reset();
        brushPipelineAttempted_ = false;
    }
    deviceContext_ = ctx;
}

VulkanBrushPipeline* VulkanBackend::EnsureBrushPipeline()
{
    if (brushPipeline_ && brushPipeline_->IsReady()) return brushPipeline_.get();
    if (brushPipelineAttempted_) return nullptr;
    if (!deviceContext_.valid || deviceContext_.device == VK_NULL_HANDLE) return nullptr;
    brushPipelineAttempted_ = true;
    auto pipeline = std::make_unique<VulkanBrushPipeline>(deviceContext_);
    if (!pipeline->Initialize()) {
        return nullptr;  // DXC missing or GPU object failure → CPU fallback
    }
    brushPipeline_ = std::move(pipeline);
    return brushPipeline_.get();
}

void* VulkanBackend::CreateInkLayerBitmap(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) return nullptr;
    std::lock_guard<std::mutex> lk(inkMutex_);
    VulkanBrushPipeline* pipeline = EnsureBrushPipeline();
    if (!pipeline) return nullptr;
    auto* bitmap = new (std::nothrow) VulkanInkLayerBitmap(pipeline);
    if (!bitmap) return nullptr;
    if (!bitmap->Initialize(width, height)) {
        delete bitmap;
        return nullptr;
    }
    return bitmap;
}

void VulkanBackend::DestroyInkLayerBitmap(void* bitmap)
{
    if (!bitmap) return;
    std::lock_guard<std::mutex> lk(inkMutex_);
    delete reinterpret_cast<VulkanInkLayerBitmap*>(bitmap);
}

int32_t VulkanBackend::ResizeInkLayerBitmap(void* bitmap, uint32_t width, uint32_t height)
{
    if (!bitmap || width == 0 || height == 0) return -1;
    std::lock_guard<std::mutex> lk(inkMutex_);
    return reinterpret_cast<VulkanInkLayerBitmap*>(bitmap)->Resize(width, height) ? 0 : -2;
}

void VulkanBackend::ClearInkLayerBitmap(void* bitmap, float r, float g, float b, float a)
{
    if (!bitmap) return;
    std::lock_guard<std::mutex> lk(inkMutex_);
    reinterpret_cast<VulkanInkLayerBitmap*>(bitmap)->Clear(r, g, b, a);
}

void* VulkanBackend::CreateBrushShader(const char* shaderKey, const char* brushMainHlsl, int32_t blendMode)
{
    if (!brushMainHlsl) return nullptr;
    std::lock_guard<std::mutex> lk(inkMutex_);
    VulkanBrushPipeline* pipeline = EnsureBrushPipeline();
    if (!pipeline) return nullptr;
    auto shader = pipeline->CreateBrushShader(
        shaderKey, brushMainHlsl, static_cast<VulkanBrushBlendMode>(blendMode));
    return shader.release();
}

void VulkanBackend::DestroyBrushShader(void* shader)
{
    if (!shader) return;
    std::lock_guard<std::mutex> lk(inkMutex_);
    delete reinterpret_cast<VulkanBrushShader*>(shader);
}

int32_t VulkanBackend::DispatchBrush(void* bitmap, void* shader,
                                     const void* strokePoints, uint32_t pointCount,
                                     const void* constants,
                                     const void* extraParams, uint32_t extraParamsSize)
{
    if (!bitmap || !shader || !strokePoints || !constants) return -1;
    std::lock_guard<std::mutex> lk(inkMutex_);
    return reinterpret_cast<VulkanInkLayerBitmap*>(bitmap)->DispatchBrush(
        reinterpret_cast<VulkanBrushShader*>(shader),
        strokePoints, pointCount, constants, extraParams, extraParamsSize);
}

IRenderBackend* CreateVulkanBackend()
{
    auto* backend = new VulkanBackend();
    if (!backend->Initialize()) {
        delete backend;
        return nullptr;
    }

    return backend;
}

} // namespace jalium
