#include "vulkan_backend.h"

#include "jalium_string_util.h"
#include "vulkan_render_target.h"
#include "vulkan_runtime.h"

#include <cstring>
#include <cwchar>

#define STB_IMAGE_STATIC
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

    // VkPhysicalDeviceProperties::deviceName 是定长 UTF-8 char[256]。
    // 公共 ABI 固定使用两字节 UTF-16，避免 wchar_t 在 Linux 上变成四字节。
    Utf8ToFixedUtf16(props.deviceName, info.name);

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

    // ── VRAM 口径：对齐 D3D12 端的 DXGI_ADAPTER_DESC1 语义 ──────────────────
    // DXGI 的分栏（d3d12_backend.cpp::GetAdapterInfo 直接透传 DXGI 值）：
    //   DedicatedVideoMemory = GPU 专用、CPU 不可见的显存。UMA iGPU 上只剩
    //                          BIOS carve-out（0，或 512MB~2GB 的小值）。
    //   SharedSystemMemory   = GPU 可用的共享系统内存。
    // Vulkan 没有直接等价物，按"堆是否被 HOST_VISIBLE memory type 引用"重建
    // 同一口径（旧逻辑把 DEVICE_LOCAL 堆总和一律报成 dedicated，Intel/AMD
    // iGPU 会把 CPU 可见的系统内存冒充专用显存，DevTools 里两端读数打架）：
    //   - DEVICE_LOCAL 且无任何 HOST_VISIBLE type 引用的堆
    //       → CPU 不可见的真显存 / iGPU BIOS carve-out → dedicated。
    //   - DEVICE_LOCAL 且被 HOST_VISIBLE type 引用的堆：
    //       * 集成 / 软件设备（UMA）→ 这就是共享系统内存本体，DXGI 记在
    //         SharedSystemMemory、dedicated 归 0（软件设备对照 WARP 同样是
    //         dedicated=0 + shared=系统内存）→ 计入 shared。
    //       * 独立 GPU → resizable-BAR 全显存 / 小 BAR 窗口：CPU 可见但仍是
    //         板载显存，DXGI 一样算 DedicatedVideoMemory → 计入 dedicated。
    //   - 非 DEVICE_LOCAL 堆（host 内存）：有 HOST_VISIBLE type 引用才计入
    //     shared（与旧逻辑一致，排除理论上 CPU 不可访问的异形堆）。
    // UMA 判定跟随上面 adapterType 的归类结果（INTEGRATED / SOFTWARE 直接来
    // 自 VkPhysicalDeviceType；OTHER 的堆大小启发式归类也顺带生效），语义上
    // 对应 D3D12 端用 D3D12_FEATURE_ARCHITECTURE1.UMA 的精确分类。
    const bool umaLike = info.adapterType != JALIUM_GPU_ADAPTER_TYPE_DISCRETE;
    uint64_t dedicatedBytes = 0;
    uint64_t sharedBytes = 0;
    for (uint32_t i = 0; i < memProps.memoryHeapCount; ++i) {
        const auto& heap = memProps.memoryHeaps[i];
        // 堆的 CPU 可见性 = 存在引用此 heap 的 HOST_VISIBLE memory type
        // （HEAP flag 只有 DEVICE_LOCAL；可见性挂在 memory type 上）。
        bool hostVisible = false;
        for (uint32_t t = 0; t < memProps.memoryTypeCount; ++t) {
            if (memProps.memoryTypes[t].heapIndex == i &&
                (memProps.memoryTypes[t].propertyFlags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT) != 0) {
                hostVisible = true;
                break;
            }
        }
        const bool deviceLocal = (heap.flags & VK_MEMORY_HEAP_DEVICE_LOCAL_BIT) != 0;
        if (deviceLocal) {
            if (hostVisible && umaLike) {
                sharedBytes += heap.size;
            } else {
                dedicatedBytes += heap.size;
            }
        } else if (hostVisible) {
            sharedBytes += heap.size;
        }
    }
    info.dedicatedVideoMemory = dedicatedBytes;
    info.sharedSystemMemory   = sharedBytes;

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
    uint32_t spreadMethod)
{
    return new VulkanLinearGradientBrush(startX, startY, endX, endY, stops, stopCount, spreadMethod);
}

Brush* VulkanBackend::CreateRadialGradientBrush(
    float centerX, float centerY, float radiusX, float radiusY,
    float originX, float originY,
    const JaliumGradientStop* stops, uint32_t stopCount,
    uint32_t spreadMethod)
{
    return new VulkanRadialGradientBrush(centerX, centerY, radiusX, radiusY, originX, originY, stops, stopCount, spreadMethod);
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

JaliumResult VulkanBackend::CheckDeviceStatus()
{
    std::lock_guard<std::mutex> lk(inkMutex_);
    // Aggregate across every live render target's generation — Vulkan has one
    // VkDevice per render target, so checking only the most recently
    // registered generation (the old behavior) let a second window mask the
    // first window's loss. ANY latched loss reports DEVICE_LOST; expired
    // entries (render target already gone without unregistering) are pruned
    // as we walk. deviceGeneration_ needs no separate check: it is always a
    // member of this list (RegisterDeviceContext appends before its
    // same-device short-circuit). Note this stays a PASSIVE latch — Vulkan
    // has no GetDeviceRemovedReason-style probe, so a dead device that no
    // call has touched yet still reports OK (documented platform difference,
    // see the header).
    bool anyLost = false;
    for (auto it = registeredGenerations_.begin(); it != registeredGenerations_.end();) {
        std::shared_ptr<VulkanDeviceGeneration> generation = it->lock();
        if (!generation) {
            it = registeredGenerations_.erase(it);
            continue;
        }
        if (generation->IsLost()) {
            anyLost = true;
        }
        ++it;
    }
    return anyLost ? JALIUM_ERROR_DEVICE_LOST : JALIUM_OK;
}

void VulkanBackend::RegisterDeviceContext(std::shared_ptr<VulkanDeviceGeneration> generation)
{
    if (!generation || !generation->ctx.valid) return;
    std::lock_guard<std::mutex> lk(inkMutex_);

    // Track (weakly) for CheckDeviceStatus loss aggregation — BEFORE the
    // same-device short-circuit below, so every generation that reaches the
    // backend stays observable even when the ink pipeline ignores the
    // registration. Dedup by generation identity; prune expired entries in
    // the same walk.
    bool alreadyTracked = false;
    for (auto it = registeredGenerations_.begin(); it != registeredGenerations_.end();) {
        std::shared_ptr<VulkanDeviceGeneration> tracked = it->lock();
        if (!tracked) {
            it = registeredGenerations_.erase(it);
            continue;
        }
        if (tracked == generation) {
            alreadyTracked = true;
        }
        ++it;
    }
    if (!alreadyTracked) {
        registeredGenerations_.push_back(generation);
    }

    if (deviceGeneration_ && deviceGeneration_->ctx.device == generation->ctx.device) {
        return;  // same device already registered (ink pipeline unchanged)
    }
    // A different device superseded the old one (second window, or a fresh
    // render target after device-lost recovery). Drop only the backend's
    // references; ink bitmaps / brush shaders still alive on the old
    // generation co-own the pipeline and the generation, so the old VkDevice
    // outlives them and their destructors stay safe.
    if (deviceGeneration_) {
        brushPipeline_.reset();
        brushPipelineAttempted_ = false;
    }
    deviceGeneration_ = std::move(generation);
}

void VulkanBackend::UnregisterDeviceContext(const std::shared_ptr<VulkanDeviceGeneration>& generation)
{
    if (!generation) return;
    std::lock_guard<std::mutex> lk(inkMutex_);
    // Drop the dying render target's generation from the CheckDeviceStatus
    // aggregation: after Destroy nobody can act on its loss anymore, and a
    // stale latched loss must not permanently poison the aggregate for the
    // surviving windows (the recovery chain destroys the lost RT and
    // registers its healthy replacement, flipping the aggregate back to OK).
    // Expired entries are pruned in the same walk.
    for (auto it = registeredGenerations_.begin(); it != registeredGenerations_.end();) {
        std::shared_ptr<VulkanDeviceGeneration> tracked = it->lock();
        if (!tracked || tracked == generation) {
            it = registeredGenerations_.erase(it);
            continue;
        }
        ++it;
    }
    if (deviceGeneration_ == generation) {
        brushPipeline_.reset();
        brushPipelineAttempted_ = false;
        deviceGeneration_.reset();
    }
}

std::shared_ptr<VulkanDeviceGeneration>
VulkanBackend::AcquireExternalImportGeneration()
{
    std::lock_guard<std::mutex> lk(inkMutex_);
    if (!deviceGeneration_ || deviceGeneration_->IsLost() ||
        !deviceGeneration_->ctx.valid ||
        !deviceGeneration_->ctx.dmaBufImportEnabled) {
        return nullptr;
    }
    return deviceGeneration_;
}

std::shared_ptr<VulkanBrushPipeline> VulkanBackend::EnsureBrushPipeline()
{
    if (brushPipeline_ && brushPipeline_->IsReady()) return brushPipeline_;
    if (brushPipelineAttempted_) return nullptr;
    if (!deviceGeneration_ || !deviceGeneration_->ctx.valid ||
        deviceGeneration_->ctx.device == VK_NULL_HANDLE ||
        deviceGeneration_->IsLost()) {
        // No device yet, or the registered generation is lost — building GPU
        // objects on it would fail call by call. The recovery chain registers
        // the replacement render target's healthy generation, which also
        // resets brushPipelineAttempted_.
        return nullptr;
    }
    brushPipelineAttempted_ = true;
    auto pipeline = std::make_shared<VulkanBrushPipeline>(deviceGeneration_);
    if (!pipeline->Initialize()) {
        return nullptr;  // DXC missing or GPU object failure → CPU fallback
    }
    brushPipeline_ = std::move(pipeline);
    return brushPipeline_;
}

void* VulkanBackend::CreateInkLayerBitmap(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) return nullptr;
    std::lock_guard<std::mutex> lk(inkMutex_);
    std::shared_ptr<VulkanBrushPipeline> pipeline = EnsureBrushPipeline();
    if (!pipeline) return nullptr;
    auto* bitmap = new (std::nothrow) VulkanInkLayerBitmap(std::move(pipeline));
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
    std::shared_ptr<VulkanBrushPipeline> pipeline = EnsureBrushPipeline();
    if (!pipeline) return nullptr;
    // The shader captures the pipeline via shared_from_this inside
    // CreateBrushShader, so the raw handle handed to managed code co-owns the
    // pipeline + device generation.
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
