#include "vulkan_render_target.h"
#include "jalium_internal.h"

#include "vulkan_backend.h"
#include "vulkan_embedded_shaders.h"
#include "vulkan_minimal.h"
#include "vulkan_resources.h"
#include "vulkan_runtime.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
#include <cstdio>
#include <cstdint>

#ifdef __ANDROID__
#include <android/log.h>
#define VK_LOG(fmt, ...) __android_log_print(ANDROID_LOG_INFO, "JaliumVulkan", fmt, ##__VA_ARGS__)
#elif defined(_WIN32)
#define VK_LOG(fmt, ...) do { char _vk_buf[512]; snprintf(_vk_buf, sizeof(_vk_buf), fmt "\n", ##__VA_ARGS__); OutputDebugStringA(_vk_buf); } while(0)
#else
#define VK_LOG(fmt, ...) fprintf(stderr, fmt "\n", ##__VA_ARGS__)
#endif

#ifdef _WIN32
#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#else
#include "text_engine.h"
#include "text_layout.h"
#include "glyph_atlas.h"
#endif

namespace jalium {

namespace {

#ifdef _WIN32
using Microsoft::WRL::ComPtr;

class DxgiDesktopDuplicator {
public:
    bool Capture(int32_t screenX, int32_t screenY, int32_t width, int32_t height, std::vector<uint8_t>& outPixels)
    {
        if (width <= 0 || height <= 0) {
            outPixels.clear();
            return false;
        }

        if (!EnsureForRect(screenX, screenY, width, height)) {
            return false;
        }

        DXGI_OUTDUPL_FRAME_INFO frameInfo {};
        ComPtr<IDXGIResource> desktopResource;
        HRESULT hr = duplication_->AcquireNextFrame(16, &frameInfo, &desktopResource);
        if (hr == DXGI_ERROR_WAIT_TIMEOUT) {
            return TryReadStagingRect(screenX, screenY, width, height, outPixels);
        }
        if (hr == DXGI_ERROR_ACCESS_LOST) {
            Reset();
            return false;
        }
        if (FAILED(hr) || !desktopResource) {
            return false;
        }

        ComPtr<ID3D11Texture2D> frameTexture;
        hr = desktopResource.As(&frameTexture);
        if (FAILED(hr) || !frameTexture) {
            duplication_->ReleaseFrame();
            return false;
        }

        EnsureStagingTexture();
        if (!stagingTexture_) {
            duplication_->ReleaseFrame();
            return false;
        }

        context_->CopyResource(stagingTexture_.Get(), frameTexture.Get());
        duplication_->ReleaseFrame();
        return TryReadStagingRect(screenX, screenY, width, height, outPixels);
    }

private:
    bool EnsureForRect(int32_t screenX, int32_t screenY, int32_t width, int32_t height)
    {
        if (duplication_ && IsRectWithinOutput(screenX, screenY, width, height)) {
            return true;
        }

        Reset();

        ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) {
            return false;
        }

        for (UINT adapterIndex = 0;; ++adapterIndex) {
            ComPtr<IDXGIAdapter1> adapter;
            if (factory->EnumAdapters1(adapterIndex, &adapter) == DXGI_ERROR_NOT_FOUND) {
                break;
            }

            for (UINT outputIndex = 0;; ++outputIndex) {
                ComPtr<IDXGIOutput> output;
                if (adapter->EnumOutputs(outputIndex, &output) == DXGI_ERROR_NOT_FOUND) {
                    break;
                }

                DXGI_OUTPUT_DESC desc {};
                if (FAILED(output->GetDesc(&desc))) {
                    continue;
                }

                const RECT rect = desc.DesktopCoordinates;
                if (screenX < rect.left || screenY < rect.top ||
                    screenX + width > rect.right || screenY + height > rect.bottom) {
                    continue;
                }

                UINT creationFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#if defined(_DEBUG)
                creationFlags |= D3D11_CREATE_DEVICE_DEBUG;
#endif
                D3D_FEATURE_LEVEL featureLevel = D3D_FEATURE_LEVEL_11_0;
                HRESULT hr = D3D11CreateDevice(
                    adapter.Get(),
                    D3D_DRIVER_TYPE_UNKNOWN,
                    nullptr,
                    creationFlags,
                    nullptr,
                    0,
                    D3D11_SDK_VERSION,
                    &device_,
                    &featureLevel,
                    &context_);
                if (FAILED(hr)) {
                    continue;
                }

                ComPtr<IDXGIOutput1> output1;
                hr = output.As(&output1);
                if (FAILED(hr) || !output1) {
                    Reset();
                    continue;
                }

                hr = output1->DuplicateOutput(device_.Get(), &duplication_);
                if (FAILED(hr) || !duplication_) {
                    Reset();
                    continue;
                }

                outputDesc_ = desc;
                return true;
            }
        }

        return false;
    }

    bool IsRectWithinOutput(int32_t screenX, int32_t screenY, int32_t width, int32_t height) const
    {
        const RECT rect = outputDesc_.DesktopCoordinates;
        return screenX >= rect.left && screenY >= rect.top &&
            screenX + width <= rect.right &&
            screenY + height <= rect.bottom;
    }

    void EnsureStagingTexture()
    {
        if (!duplication_) {
            return;
        }

        DXGI_OUTDUPL_DESC duplicationDesc {};
        duplication_->GetDesc(&duplicationDesc);
        if (stagingTexture_ &&
            duplicationDesc.ModeDesc.Width == stagingWidth_ &&
            duplicationDesc.ModeDesc.Height == stagingHeight_) {
            return;
        }

        stagingTexture_.Reset();
        stagingWidth_ = duplicationDesc.ModeDesc.Width;
        stagingHeight_ = duplicationDesc.ModeDesc.Height;

        D3D11_TEXTURE2D_DESC desc {};
        desc.Width = stagingWidth_;
        desc.Height = stagingHeight_;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = duplicationDesc.ModeDesc.Format;
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_STAGING;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        device_->CreateTexture2D(&desc, nullptr, &stagingTexture_);
    }

    bool TryReadStagingRect(int32_t screenX, int32_t screenY, int32_t width, int32_t height, std::vector<uint8_t>& outPixels)
    {
        if (!stagingTexture_) {
            return false;
        }

        D3D11_MAPPED_SUBRESOURCE mapped {};
        HRESULT hr = context_->Map(stagingTexture_.Get(), 0, D3D11_MAP_READ, 0, &mapped);
        if (FAILED(hr)) {
            return false;
        }

        const int32_t sourceX = screenX - outputDesc_.DesktopCoordinates.left;
        const int32_t sourceY = screenY - outputDesc_.DesktopCoordinates.top;
        outPixels.assign(static_cast<size_t>(width) * static_cast<size_t>(height) * 4u, 0);

        const auto* sourceBytes = static_cast<const uint8_t*>(mapped.pData);
        for (int32_t row = 0; row < height; ++row) {
            const auto* sourceRow = sourceBytes + static_cast<size_t>(sourceY + row) * mapped.RowPitch + static_cast<size_t>(sourceX) * 4u;
            auto* destRow = outPixels.data() + static_cast<size_t>(row) * static_cast<size_t>(width) * 4u;
            std::memcpy(destRow, sourceRow, static_cast<size_t>(width) * 4u);
        }

        context_->Unmap(stagingTexture_.Get(), 0);
        for (int i = 0; i < width * height; ++i) {
            outPixels[static_cast<size_t>(i) * 4u + 3] = 255;
        }

        return true;
    }

    void Reset()
    {
        stagingTexture_.Reset();
        duplication_.Reset();
        context_.Reset();
        device_.Reset();
        stagingWidth_ = 0;
        stagingHeight_ = 0;
        std::memset(&outputDesc_, 0, sizeof(outputDesc_));
    }

    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGIOutputDuplication> duplication_;
    ComPtr<ID3D11Texture2D> stagingTexture_;
    DXGI_OUTPUT_DESC outputDesc_ {};
    UINT stagingWidth_ = 0;
    UINT stagingHeight_ = 0;
};

DxgiDesktopDuplicator& GetDxgiDesktopDuplicator()
{
    static DxgiDesktopDuplicator duplicator;
    return duplicator;
}
#endif

template <typename T>
T LoadInstanceProc(PFN_vkGetInstanceProcAddr getProc, VkInstance instance, const char* name)
{
    return reinterpret_cast<T>(getProc ? getProc(instance, name) : nullptr);
}

template <typename T>
T LoadDeviceProc(PFN_vkGetDeviceProcAddr getProc, VkDevice device, const char* name)
{
    return reinterpret_cast<T>(getProc ? getProc(device, name) : nullptr);
}

uint32_t ClampExtent(uint32_t value, uint32_t minValue, uint32_t maxValue)
{
    return std::max(minValue, std::min(value, maxValue));
}

/// Returns a B8G8R8A8 format matching the sRGB-ness of the swapchain format.
/// CPU canvas writes BGRA bytes, so we always want B8G8R8A8 channel order.
/// When the swapchain is SRGB, the upload image must also be SRGB so that
/// the sRGB→linear sample conversion and linear→sRGB write conversion cancel out.
VkFormat GetUploadImageFormat(VkFormat swapchainFormat)
{
    switch (swapchainFormat) {
        case VK_FORMAT_R8G8B8A8_SRGB:
        case VK_FORMAT_B8G8R8A8_SRGB:
            return VK_FORMAT_B8G8R8A8_SRGB;
        default:
            return VK_FORMAT_B8G8R8A8_UNORM;
    }
}

float SignedArea2D(const std::vector<float>& points)
{
    if (points.size() < 6) {
        return 0.0f;
    }

    float area = 0.0f;
    const size_t count = points.size() / 2;
    for (size_t i = 0; i < count; ++i) {
        const size_t j = (i + 1) % count;
        area += points[i * 2] * points[j * 2 + 1] - points[j * 2] * points[i * 2 + 1];
    }

    return area * 0.5f;
}

bool PointInTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
{
    const float v0x = cx - ax;
    const float v0y = cy - ay;
    const float v1x = bx - ax;
    const float v1y = by - ay;
    const float v2x = px - ax;
    const float v2y = py - ay;

    const float dot00 = v0x * v0x + v0y * v0y;
    const float dot01 = v0x * v1x + v0y * v1y;
    const float dot02 = v0x * v2x + v0y * v2y;
    const float dot11 = v1x * v1x + v1y * v1y;
    const float dot12 = v1x * v2x + v1y * v2y;

    const float denominator = dot00 * dot11 - dot01 * dot01;
    if (std::fabs(denominator) < 0.00001f) {
        return false;
    }

    const float invDenominator = 1.0f / denominator;
    const float u = (dot11 * dot02 - dot01 * dot12) * invDenominator;
    const float v = (dot00 * dot12 - dot01 * dot02) * invDenominator;
    return u >= 0.0f && v >= 0.0f && (u + v) <= 1.0f;
}

bool TriangulateSimplePolygon(const std::vector<float>& inputPoints, std::vector<float>& triangleVertices)
{
    triangleVertices.clear();
    if (inputPoints.size() < 6) {
        return false;
    }

    std::vector<float> points = inputPoints;
    if (points.size() >= 8) {
        const size_t last = points.size() - 2;
        if (std::fabs(points[0] - points[last]) < 0.00001f && std::fabs(points[1] - points[last + 1]) < 0.00001f) {
            points.resize(last);
        }
    }

    const size_t count = points.size() / 2;
    if (count < 3) {
        return false;
    }

    std::vector<int> indices(count);
    for (size_t i = 0; i < count; ++i) {
        indices[i] = static_cast<int>(i);
    }

    const bool isCcw = SignedArea2D(points) > 0.0f;
    int guard = 0;
    while (indices.size() > 3 && guard < 65536) {
        bool earFound = false;
        for (size_t i = 0; i < indices.size(); ++i) {
            const int prev = indices[(i + indices.size() - 1) % indices.size()];
            const int curr = indices[i];
            const int next = indices[(i + 1) % indices.size()];

            const float ax = points[prev * 2];
            const float ay = points[prev * 2 + 1];
            const float bx = points[curr * 2];
            const float by = points[curr * 2 + 1];
            const float cx = points[next * 2];
            const float cy = points[next * 2 + 1];

            const float cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (isCcw ? (cross <= 0.00001f) : (cross >= -0.00001f)) {
                continue;
            }

            bool containsOtherPoint = false;
            for (size_t j = 0; j < indices.size(); ++j) {
                if (j == i || j == (i + 1) % indices.size() || j == (i + indices.size() - 1) % indices.size()) {
                    continue;
                }

                const int candidate = indices[j];
                if (PointInTriangle(points[candidate * 2], points[candidate * 2 + 1], ax, ay, bx, by, cx, cy)) {
                    containsOtherPoint = true;
                    break;
                }
            }

            if (containsOtherPoint) {
                continue;
            }

            triangleVertices.push_back(ax);
            triangleVertices.push_back(ay);
            triangleVertices.push_back(bx);
            triangleVertices.push_back(by);
            triangleVertices.push_back(cx);
            triangleVertices.push_back(cy);
            indices.erase(indices.begin() + static_cast<std::ptrdiff_t>(i));
            earFound = true;
            break;
        }

        if (!earFound) {
            return false;
        }

        ++guard;
    }

    if (guard >= 65536) {
#ifdef _WIN32
        OutputDebugStringA("[Vulkan] Triangulation guard limit reached\n");
#else
        VK_LOG("[Vulkan] Triangulation guard limit reached\n");
#endif
    }

    if (indices.size() == 3) {
        triangleVertices.push_back(points[indices[0] * 2]);
        triangleVertices.push_back(points[indices[0] * 2 + 1]);
        triangleVertices.push_back(points[indices[1] * 2]);
        triangleVertices.push_back(points[indices[1] * 2 + 1]);
        triangleVertices.push_back(points[indices[2] * 2]);
        triangleVertices.push_back(points[indices[2] * 2 + 1]);
    }

    return triangleVertices.size() >= 6;
}

struct SolidRectPushConstants {
    float rect[4];
    float color[4];
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
    float innerRoundedClipRect[4];
    float innerRoundedClipRadius[2];
    float padding2[2];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding3[2];
};

struct BitmapQuadPushConstants {
    float rect[4];
    float uvOpacity[4];
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
    float innerRoundedClipRect[4];
    float innerRoundedClipRadius[2];
    float padding2[2];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding3[2];
};

struct TriangleFillPushConstants {
    float color[4];
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
};

struct TransitionPushConstants {
    float rect[4];
    float progressOpacity[4];
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding2[2];
};

struct BlurPushConstants {
    float rect[4];
    float blurInfo1[4];
    float blurInfo2[4];
    float blurTint[4];
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding2[2];
};

struct LiquidGlassPushConstants {
    float rect[4];
    float glassInfo1[4];
    float glassInfo2[4];
    float tintColor[4];
    float lightInfo[4];
    float screenSize[2];
    float padding[2];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding2[2];
};

struct BackdropPushConstants {
    float rect[4];
    float backdropInfo1[4];
    float tintColor[4];
    float extraInfo[4];
    float screenSize[2];
    float padding[2];
    float cornerRadii[4];
    float quadPoint01[4];
    float quadPoint23[4];
    float geometryFlags[2];
    float padding2[2];
};

struct GlowPushConstants {
    float rect[4];
    float glowColor[4];
    float glowInfo1[4];
    float glowInfo2[4];
    float screenSize[2];
    float padding[2];
};

} // namespace

class VulkanRenderTarget::Impl {
public:
    PFN_vkGetInstanceProcAddr getInstanceProcAddr = nullptr;
    PFN_vkGetDeviceProcAddr getDeviceProcAddr = nullptr;

    PFN_vkCreateInstance createInstance = nullptr;
    PFN_vkDestroyInstance destroyInstance = nullptr;
    PFN_vkEnumeratePhysicalDevices enumeratePhysicalDevices = nullptr;
    PFN_vkGetPhysicalDeviceQueueFamilyProperties getPhysicalDeviceQueueFamilyProperties = nullptr;
    PFN_vkGetPhysicalDeviceSurfaceSupportKHR getPhysicalDeviceSurfaceSupport = nullptr;
    PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR getSurfaceCapabilities = nullptr;
    PFN_vkGetPhysicalDeviceSurfaceFormatsKHR getSurfaceFormats = nullptr;
    PFN_vkGetPhysicalDeviceSurfacePresentModesKHR getSurfacePresentModes = nullptr;
#ifdef _WIN32
    PFN_vkCreateWin32SurfaceKHR createWin32Surface = nullptr;
#elif defined(__ANDROID__)
    PFN_vkCreateAndroidSurfaceKHR createAndroidSurface = nullptr;
#elif defined(__linux__)
    PFN_vkCreateXlibSurfaceKHR createXlibSurface = nullptr;
#endif
    PFN_vkDestroySurfaceKHR destroySurface = nullptr;
    PFN_vkCreateDevice createDevice = nullptr;
    PFN_vkDestroyDevice destroyDevice = nullptr;
    PFN_vkGetDeviceQueue getDeviceQueue = nullptr;
    PFN_vkCreateSwapchainKHR createSwapchain = nullptr;
    PFN_vkDestroySwapchainKHR destroySwapchain = nullptr;
    PFN_vkGetSwapchainImagesKHR getSwapchainImages = nullptr;
    PFN_vkAcquireNextImageKHR acquireNextImage = nullptr;
    PFN_vkQueuePresentKHR queuePresent = nullptr;
    PFN_vkCreateCommandPool createCommandPool = nullptr;
    PFN_vkDestroyCommandPool destroyCommandPool = nullptr;
    PFN_vkAllocateCommandBuffers allocateCommandBuffers = nullptr;
    PFN_vkGetPhysicalDeviceMemoryProperties getPhysicalDeviceMemoryProperties = nullptr;
    PFN_vkCreateBuffer createBuffer = nullptr;
    PFN_vkDestroyBuffer destroyBuffer = nullptr;
    PFN_vkGetBufferMemoryRequirements getBufferMemoryRequirements = nullptr;
    PFN_vkCreateImage createImage = nullptr;
    PFN_vkDestroyImage destroyImage = nullptr;
    PFN_vkGetImageMemoryRequirements getImageMemoryRequirements = nullptr;
    PFN_vkAllocateMemory allocateMemory = nullptr;
    PFN_vkFreeMemory freeMemory = nullptr;
    PFN_vkBindBufferMemory bindBufferMemory = nullptr;
    PFN_vkBindImageMemory bindImageMemory = nullptr;
    PFN_vkMapMemory mapMemory = nullptr;
    PFN_vkUnmapMemory unmapMemory = nullptr;
    PFN_vkCreateImageView createImageView = nullptr;
    PFN_vkDestroyImageView destroyImageView = nullptr;
    PFN_vkCreateSampler createSampler = nullptr;
    PFN_vkDestroySampler destroySampler = nullptr;
    PFN_vkCreateDescriptorSetLayout createDescriptorSetLayout = nullptr;
    PFN_vkDestroyDescriptorSetLayout destroyDescriptorSetLayout = nullptr;
    PFN_vkCreateDescriptorPool createDescriptorPool = nullptr;
    PFN_vkDestroyDescriptorPool destroyDescriptorPool = nullptr;
    PFN_vkAllocateDescriptorSets allocateDescriptorSets = nullptr;
    PFN_vkUpdateDescriptorSets updateDescriptorSets = nullptr;
    PFN_vkCreatePipelineLayout createPipelineLayout = nullptr;
    PFN_vkDestroyPipelineLayout destroyPipelineLayout = nullptr;
    PFN_vkCreateShaderModule createShaderModule = nullptr;
    PFN_vkDestroyShaderModule destroyShaderModule = nullptr;
    PFN_vkCreateRenderPass createRenderPass = nullptr;
    PFN_vkDestroyRenderPass destroyRenderPass = nullptr;
    PFN_vkCreateFramebuffer createFramebuffer = nullptr;
    PFN_vkDestroyFramebuffer destroyFramebuffer = nullptr;
    PFN_vkCreateGraphicsPipelines createGraphicsPipelines = nullptr;
    PFN_vkDestroyPipeline destroyPipeline = nullptr;
    PFN_vkResetCommandBuffer resetCommandBuffer = nullptr;
    PFN_vkBeginCommandBuffer beginCommandBuffer = nullptr;
    PFN_vkEndCommandBuffer endCommandBuffer = nullptr;
    PFN_vkCmdPipelineBarrier cmdPipelineBarrier = nullptr;
    PFN_vkCmdClearColorImage cmdClearColorImage = nullptr;
    PFN_vkCmdCopyBufferToImage cmdCopyBufferToImage = nullptr;
    PFN_vkCmdBeginRenderPass cmdBeginRenderPass = nullptr;
    PFN_vkCmdEndRenderPass cmdEndRenderPass = nullptr;
    PFN_vkCmdBindPipeline cmdBindPipeline = nullptr;
    PFN_vkCmdBindDescriptorSets cmdBindDescriptorSets = nullptr;
    PFN_vkCmdSetViewport cmdSetViewport = nullptr;
    PFN_vkCmdSetScissor cmdSetScissor = nullptr;
    PFN_vkCmdPushConstants cmdPushConstants = nullptr;
    PFN_vkCmdDraw cmdDraw = nullptr;
    PFN_vkCmdBindVertexBuffers cmdBindVertexBuffers = nullptr;
    PFN_vkQueueSubmit queueSubmit = nullptr;
    PFN_vkCreateSemaphore createSemaphore = nullptr;
    PFN_vkDestroySemaphore destroySemaphore = nullptr;
    PFN_vkCreateFence createFence = nullptr;
    PFN_vkDestroyFence destroyFence = nullptr;
    PFN_vkWaitForFences waitForFences = nullptr;
    PFN_vkResetFences resetFences = nullptr;
    PFN_vkDeviceWaitIdle deviceWaitIdle = nullptr;

    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice physicalDevice = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    VkQueue queue = VK_NULL_HANDLE;
    VkSurfaceKHR surface = VK_NULL_HANDLE;
    VkSwapchainKHR swapchain = VK_NULL_HANDLE;
    VkCommandPool commandPool = VK_NULL_HANDLE;
    VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
    VkBuffer stagingBuffer = VK_NULL_HANDLE;
    VkDeviceMemory stagingMemory = VK_NULL_HANDLE;
    VkImage uploadImage = VK_NULL_HANDLE;
    VkDeviceMemory uploadImageMemory = VK_NULL_HANDLE;
    VkImageView uploadImageView = VK_NULL_HANDLE;
    VkSampler frameSampler = VK_NULL_HANDLE;
    VkDescriptorSetLayout frameDescriptorSetLayout = VK_NULL_HANDLE;
    VkDescriptorPool frameDescriptorPool = VK_NULL_HANDLE;
    VkDescriptorSet frameDescriptorSet = VK_NULL_HANDLE;
    VkPipelineLayout framePipelineLayout = VK_NULL_HANDLE;
    VkRenderPass frameRenderPass = VK_NULL_HANDLE;
    VkPipeline framePipeline = VK_NULL_HANDLE;
    VkPipelineLayout solidRectPipelineLayout = VK_NULL_HANDLE;
    VkPipeline solidRectPipeline = VK_NULL_HANDLE;
    VkPipeline clearRectPipeline = VK_NULL_HANDLE;
    VkPipelineLayout bitmapPipelineLayout = VK_NULL_HANDLE;
    VkPipeline bitmapPipeline = VK_NULL_HANDLE;
    VkPipelineLayout blurPipelineLayout = VK_NULL_HANDLE;
    VkPipeline blurPipeline = VK_NULL_HANDLE;
    VkPipelineLayout liquidGlassPipelineLayout = VK_NULL_HANDLE;
    VkPipeline liquidGlassPipeline = VK_NULL_HANDLE;
    VkPipelineLayout backdropPipelineLayout = VK_NULL_HANDLE;
    VkPipeline backdropPipeline = VK_NULL_HANDLE;
    VkPipelineLayout glowPipelineLayout = VK_NULL_HANDLE;
    VkPipeline glowPipeline = VK_NULL_HANDLE;
    VkPipelineLayout triangleFillPipelineLayout = VK_NULL_HANDLE;
    VkPipeline triangleFillPipeline = VK_NULL_HANDLE;
    VkImage transitionImages[2] = { VK_NULL_HANDLE, VK_NULL_HANDLE };
    VkDeviceMemory transitionImageMemory[2] = { VK_NULL_HANDLE, VK_NULL_HANDLE };
    VkImageView transitionImageViews[2] = { VK_NULL_HANDLE, VK_NULL_HANDLE };
    VkImageLayout transitionImageLayouts[2] = { VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_UNDEFINED };
    uint32_t transitionWidth = 0;
    uint32_t transitionHeight = 0;
    VkDescriptorSetLayout transitionDescriptorSetLayout = VK_NULL_HANDLE;
    VkDescriptorPool transitionDescriptorPool = VK_NULL_HANDLE;
    VkDescriptorSet transitionDescriptorSet = VK_NULL_HANDLE;
    VkPipelineLayout transitionPipelineLayout = VK_NULL_HANDLE;
    VkPipeline transitionPipeline = VK_NULL_HANDLE;
    VkSemaphore imageAvailable = VK_NULL_HANDLE;
    VkFence inFlight = VK_NULL_HANDLE;
    void* mappedPixels = nullptr;
    VkDeviceSize mappedPixelCapacity = 0;

    uint32_t queueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    VkExtent2D extent {};
    VkFormat format = VK_FORMAT_B8G8R8A8_UNORM;
    std::vector<VkImage> images;
    std::vector<VkImageLayout> imageLayouts;
    std::vector<VkImageView> imageViews;
    std::vector<VkFramebuffer> framebuffers;
    VkImageLayout uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t uploadWidth = 0;
    uint32_t uploadHeight = 0;
    bool submitted = false;
    bool initialized = false;

    // Multi-frames-in-flight: each frame needs its own command buffer, fences,
    // semaphores, staging buffer (+mapped pointer), upload image, and descriptor set.
    // Alias model: the single-named fields above act as the "current frame" alias,
    // BeginFrame() copies perFrameStates_[currentFrame_] into the alias, CommitCurrentFrame()
    // writes alias back. Existing helpers (EnsureStagingBuffer / EnsureUploadImage /
    // UpdateFrameDescriptorSet) still operate on the alias with no changes.
    static constexpr uint32_t MAX_FRAMES_IN_FLIGHT = 2;
    struct PerFrameState {
        VkCommandBuffer commandBuffer = VK_NULL_HANDLE;
        VkFence inFlight = VK_NULL_HANDLE;
        VkSemaphore imageAvailable = VK_NULL_HANDLE;
        VkBuffer stagingBuffer = VK_NULL_HANDLE;
        VkDeviceMemory stagingMemory = VK_NULL_HANDLE;
        void* mappedPixels = nullptr;
        VkDeviceSize mappedPixelCapacity = 0;
        VkImage uploadImage = VK_NULL_HANDLE;
        VkDeviceMemory uploadImageMemory = VK_NULL_HANDLE;
        VkImageView uploadImageView = VK_NULL_HANDLE;
        VkImageLayout uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        uint32_t uploadWidth = 0;
        uint32_t uploadHeight = 0;
        VkDescriptorSet frameDescriptorSet = VK_NULL_HANDLE;
        bool submitted = false;
    };
    PerFrameState perFrameStates_[MAX_FRAMES_IN_FLIGHT];
    uint32_t currentFrame_ = 0;
    // renderFinished is **per swap-chain image**, not per frame, because present
    // uses imageIndex as the wait target; two frames in flight may reference the
    // same image slot only after the fence guarantees the previous submit is done,
    // so one semaphore per image is sufficient and avoids present-time validation
    // errors about a semaphore being signaled by two simultaneous submissions.
    std::vector<VkSemaphore> renderFinishedPerImage;

    void BeginFrame();
    void CommitCurrentFrame();
    void EndFrame();
    void DestroyPerFrameResources();

    bool Initialize(const JaliumSurfaceDescriptor& surfaceDescriptor, int32_t width, int32_t height, bool vsync);
    bool RecreateSwapchain(int32_t width, int32_t height, bool vsync);
    bool EnsureStagingCapacity(VkDeviceSize requiredSize);
    bool EnsureStagingBuffer(uint32_t width, uint32_t height);
    bool EnsureUploadImage(uint32_t width, uint32_t height);
    bool EnsureUploadImageCapacity(uint32_t width, uint32_t height);
    bool EnsureTransitionImagesCapacity(uint32_t width, uint32_t height);
    bool EnsureGraphicsResources();
    bool DrawFrame(const uint8_t* pixels, uint32_t width, uint32_t height);
    bool DrawReplayFrame(const std::vector<VulkanRenderTarget::GpuReplayCommand>& commands, const float clearColor[4]);
    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags requiredProperties) const;
    bool UpdateFrameDescriptorSet();
    bool UpdateTransitionDescriptorSet();
    void DestroyUploadImage();
    void DestroyTransitionImages();
    void DestroyGraphicsResources();
    void Destroy();
    ~Impl();
};

VulkanRenderTarget::VulkanRenderTarget(
    VulkanBackend* backend,
    const JaliumSurfaceDescriptor& surface,
    int32_t width,
    int32_t height,
    bool useComposition)
    : backend_(backend)
    , surface_(surface)
    , isComposition_(useComposition)
    , impl_(std::make_unique<Impl>())
{
    width_ = width;
    height_ = height;
    // Default engine: Auto → Impeller on Vulkan
    activeEngine_ = ResolveRenderingEngine(JALIUM_ENGINE_AUTO, JALIUM_BACKEND_VULKAN);
    pendingEngine_ = activeEngine_;
    transformStack_.push_back(CpuTransform {});
    opacityStack_.push_back(1.0f);
    ResizeCpuCanvas();
    if (!impl_->Initialize(surface, width, height, vsyncEnabled_)) {
        VK_LOG("[Vulkan] VulkanRenderTarget: initialization failed, GPU presentation will not work\n");
    }
}

VulkanRenderTarget::~VulkanRenderTarget() = default;

bool VulkanRenderTarget::IsInitialized() const
{
    return impl_ && impl_->initialized;
}

JaliumResult VulkanRenderTarget::Resize(int32_t width, int32_t height)
{
    if (width <= 0 || height <= 0) {
        return JALIUM_ERROR_INVALID_ARGUMENT;
    }

    width_ = width;
    height_ = height;
    ResizeCpuCanvas();
    fullInvalidation_ = true;
    dirtyRects_.clear();
    return impl_ && impl_->RecreateSwapchain(width, height, vsyncEnabled_)
        ? JALIUM_OK
        : JALIUM_ERROR_RESOURCE_CREATION_FAILED;
}

JaliumResult VulkanRenderTarget::BeginDraw()
{
    if (isDrawing_) {
        return JALIUM_ERROR_INVALID_STATE;
    }

    // Apply pending engine switch at frame boundary
    if (pendingEngine_ != activeEngine_) {
        activeEngine_ = pendingEngine_;
    }

    isDrawing_ = true;
    ResetGpuReplay();
    // Eagerly flag the frame as "cleared" so that the first Draw* before the
    // caller gets a chance to invoke Clear() can still record GPU replay
    // commands. The Vulkan DrawReplayFrame unconditionally clears the
    // swap-chain image anyway — gpuReplayHasClear_ is just a latch that says
    // "the GPU replay path is usable for this frame". Treating it as latched
    // from frame start matches the D3D12 backend behavior and prevents the
    // whole frame from falling back to CPU upload when Clear() is skipped or
    // ClearBackground uses a partial-region FillRectangle instead of Clear.
    gpuReplayHasClear_ = true;
    // Predict whether this frame needs CPU rasterization based on the previous
    // frame. If the previous frame ended up falling back to DrawFrame (e.g. it
    // had an effect that required pixelBuffer_), assume this frame will too and
    // start the CPU paths warm from frame start. If it went through
    // DrawReplayFrame, start cold and let EnsureCpuRasterization kick in only
    // when something actually needs it.
    cpuRasterNeeded_ = cpuRasterNeededLastFrame_;

    // Push a root DPI scale transform so all draw calls in DIPs are
    // automatically mapped to physical pixels on high-density displays.
    float scaleX = dpiX_ / 96.0f;
    float scaleY = dpiY_ / 96.0f;
    if (scaleX != 1.0f || scaleY != 1.0f) {
        float m[6] = { scaleX, 0, 0, scaleY, 0, 0 };
        PushTransform(m);
    }

    return JALIUM_OK;
}

JaliumResult VulkanRenderTarget::EndDraw()
{
    if (!isDrawing_) {
        return JALIUM_ERROR_INVALID_STATE;
    }

    // Pop the root DPI scale transform pushed in BeginDraw
    float scaleX = dpiX_ / 96.0f;
    float scaleY = dpiY_ / 96.0f;
    if (scaleX != 1.0f || scaleY != 1.0f) {
        PopTransform();
    }

    bool ok = false;
    if (impl_) {
        if (!impl_->initialized) {
            VK_LOG("[Vulkan] EndDraw: impl not initialized, skipping draw");
        } else if (gpuReplaySupported_ && gpuReplayHasClear_) {
            // GPU replay path: pixelBuffer_ will be discarded, so any CPU work
            // done this frame was wasted — but thanks to cpuRasterNeeded_ being
            // false (or only lazily flipped to true), most of it never ran.
            ok = impl_->DrawReplayFrame(gpuReplayCommands_, clearColor_);
        } else {
            // CPU upload path: DrawFrame will upload pixelBuffer_ verbatim.
            // If the frame skipped CPU rasterization assuming it'd go through
            // the replay path, we now have to catch pixelBuffer_ up to the
            // recorded commands before uploading.
            EnsureCpuRasterization();
            ok = impl_->DrawFrame(pixelBuffer_.data(), static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
        }
    }
    cpuRasterNeededLastFrame_ = cpuRasterNeeded_;
    isDrawing_ = false;
    dirtyRects_.clear();
    fullInvalidation_ = false;
    return ok ? JALIUM_OK : JALIUM_ERROR_DEVICE_LOST;
}

void VulkanRenderTarget::Clear(float r, float g, float b, float a)
{
    clearColor_[0] = r;
    clearColor_[1] = g;
    clearColor_[2] = b;
    clearColor_[3] = a;

    const auto toByte = [](float value) -> uint8_t {
        value = std::clamp(value, 0.0f, 1.0f);
        return static_cast<uint8_t>(value * 255.0f + 0.5f);
    };

    ClearCpuCanvas(toByte(b), toByte(g), toByte(r), toByte(a));
    if (isDrawing_) {
        ResetGpuReplay();
        gpuReplayHasClear_ = true;
    }
}

bool VulkanRenderTarget::Impl::Initialize(const JaliumSurfaceDescriptor& surfaceDescriptor, int32_t width, int32_t height, bool vsync)
{
#if !defined(_WIN32) && !defined(__linux__) && !defined(__ANDROID__)
    (void)surfaceDescriptor;
    (void)width;
    (void)height;
    (void)vsync;
    VK_LOG("[Vulkan] Initialize failed: unsupported platform\n");
    return false;
#else
#ifdef _WIN32
    if (surfaceDescriptor.platform != JALIUM_PLATFORM_WINDOWS || surfaceDescriptor.handle0 == 0) {
        OutputDebugStringA("[Vulkan] Initialize failed: invalid Windows surface descriptor\n");
        return false;
    }
#elif defined(__ANDROID__)
    if (surfaceDescriptor.platform != JALIUM_PLATFORM_ANDROID || surfaceDescriptor.handle0 == 0) {
        VK_LOG("[Vulkan] Initialize failed: invalid Android surface descriptor\n");
        return false;
    }
#elif defined(__linux__)
    if (surfaceDescriptor.platform != JALIUM_PLATFORM_LINUX_X11 || surfaceDescriptor.handle0 == 0 || surfaceDescriptor.handle1 == 0) {
        VK_LOG("[Vulkan] Initialize failed: invalid Linux surface descriptor\n");
        return false;
    }
#endif

    getInstanceProcAddr = GetVulkanGetInstanceProcAddr();
    getDeviceProcAddr = GetVulkanGetDeviceProcAddr();
    if (!getInstanceProcAddr || !getDeviceProcAddr) {
        VK_LOG("[Vulkan] Initialize failed: could not load Vulkan proc addresses\n");
        return false;
    }

    createInstance = LoadInstanceProc<PFN_vkCreateInstance>(getInstanceProcAddr, VK_NULL_HANDLE, "vkCreateInstance");
    if (!createInstance) {
        VK_LOG("[Vulkan] Initialize failed: could not load vkCreateInstance\n");
        return false;
    }

    const char* extensions[] = {
        "VK_KHR_surface",
#ifdef _WIN32
        "VK_KHR_win32_surface"
#elif defined(__ANDROID__)
        "VK_KHR_android_surface"
#else
        "VK_KHR_xlib_surface"
#endif
    };
    VkApplicationInfo appInfo {};
    appInfo.sType = VK_STRUCTURE_TYPE_APPLICATION_INFO;
    appInfo.pApplicationName = "Jalium.UI";
    appInfo.pEngineName = "Jalium";
    appInfo.apiVersion = VK_API_VERSION_1_0;

    VkInstanceCreateInfo instanceInfo {};
    instanceInfo.sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO;
    instanceInfo.pApplicationInfo = &appInfo;
    instanceInfo.enabledExtensionCount = 2;
    instanceInfo.ppEnabledExtensionNames = extensions;

    if (createInstance(&instanceInfo, nullptr, &instance) != VK_SUCCESS || !instance) {
        VK_LOG("[Vulkan] Initialize failed: vkCreateInstance returned failure\n");
        return false;
    }

    destroyInstance = LoadInstanceProc<PFN_vkDestroyInstance>(getInstanceProcAddr, instance, "vkDestroyInstance");
    enumeratePhysicalDevices = LoadInstanceProc<PFN_vkEnumeratePhysicalDevices>(getInstanceProcAddr, instance, "vkEnumeratePhysicalDevices");
    getPhysicalDeviceQueueFamilyProperties = LoadInstanceProc<PFN_vkGetPhysicalDeviceQueueFamilyProperties>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceQueueFamilyProperties");
    getPhysicalDeviceMemoryProperties = LoadInstanceProc<PFN_vkGetPhysicalDeviceMemoryProperties>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceMemoryProperties");
    getPhysicalDeviceSurfaceSupport = LoadInstanceProc<PFN_vkGetPhysicalDeviceSurfaceSupportKHR>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceSurfaceSupportKHR");
    getSurfaceCapabilities = LoadInstanceProc<PFN_vkGetPhysicalDeviceSurfaceCapabilitiesKHR>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
    getSurfaceFormats = LoadInstanceProc<PFN_vkGetPhysicalDeviceSurfaceFormatsKHR>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceSurfaceFormatsKHR");
    getSurfacePresentModes = LoadInstanceProc<PFN_vkGetPhysicalDeviceSurfacePresentModesKHR>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceSurfacePresentModesKHR");
#ifdef _WIN32
    createWin32Surface = LoadInstanceProc<PFN_vkCreateWin32SurfaceKHR>(getInstanceProcAddr, instance, "vkCreateWin32SurfaceKHR");
#elif defined(__ANDROID__)
    createAndroidSurface = LoadInstanceProc<PFN_vkCreateAndroidSurfaceKHR>(getInstanceProcAddr, instance, "vkCreateAndroidSurfaceKHR");
#elif defined(__linux__)
    createXlibSurface = LoadInstanceProc<PFN_vkCreateXlibSurfaceKHR>(getInstanceProcAddr, instance, "vkCreateXlibSurfaceKHR");
#endif
    destroySurface = LoadInstanceProc<PFN_vkDestroySurfaceKHR>(getInstanceProcAddr, instance, "vkDestroySurfaceKHR");
    createDevice = LoadInstanceProc<PFN_vkCreateDevice>(getInstanceProcAddr, instance, "vkCreateDevice");
    if (!destroyInstance || !enumeratePhysicalDevices || !getPhysicalDeviceQueueFamilyProperties ||
        !getPhysicalDeviceMemoryProperties ||
        !getPhysicalDeviceSurfaceSupport || !getSurfaceCapabilities || !getSurfaceFormats ||
        !getSurfacePresentModes || !destroySurface || !createDevice) {
        VK_LOG("[Vulkan] Initialize failed: could not load required instance-level function pointers\n");
        return false;
    }

#ifdef _WIN32
    VkWin32SurfaceCreateInfoKHR surfaceInfo {};
    surfaceInfo.sType = VK_STRUCTURE_TYPE_WIN32_SURFACE_CREATE_INFO_KHR;
    surfaceInfo.hinstance = GetModuleHandleW(nullptr);
    surfaceInfo.hwnd = reinterpret_cast<HWND>(surfaceDescriptor.handle0);
    if (!createWin32Surface || createWin32Surface(instance, &surfaceInfo, nullptr, &surface) != VK_SUCCESS || surface == VK_NULL_HANDLE) {
        OutputDebugStringA("[Vulkan] Initialize failed: could not create Win32 surface\n");
        return false;
    }
#elif defined(__ANDROID__)
    VkAndroidSurfaceCreateInfoKHR surfaceInfo {};
    surfaceInfo.sType = VK_STRUCTURE_TYPE_ANDROID_SURFACE_CREATE_INFO_KHR;
    surfaceInfo.window = reinterpret_cast<ANativeWindow*>(surfaceDescriptor.handle0);
    if (!createAndroidSurface || createAndroidSurface(instance, &surfaceInfo, nullptr, &surface) != VK_SUCCESS || surface == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] Initialize failed: could not create Android surface\n");
        return false;
    }
#elif defined(__linux__)
    VkXlibSurfaceCreateInfoKHR surfaceInfo {};
    surfaceInfo.sType = VK_STRUCTURE_TYPE_XLIB_SURFACE_CREATE_INFO_KHR;
    surfaceInfo.dpy = reinterpret_cast<Display*>(surfaceDescriptor.handle0);
    surfaceInfo.window = static_cast<Window>(reinterpret_cast<uintptr_t>(surfaceDescriptor.handle1));
    if (!createXlibSurface || createXlibSurface(instance, &surfaceInfo, nullptr, &surface) != VK_SUCCESS || surface == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] Initialize failed: could not create Xlib surface\n");
        return false;
    }
#endif

    uint32_t physicalDeviceCount = 0;
    if (enumeratePhysicalDevices(instance, &physicalDeviceCount, nullptr) != VK_SUCCESS || physicalDeviceCount == 0) {
        VK_LOG("[Vulkan] Initialize failed: no physical devices found\n");
        return false;
    }

    std::vector<VkPhysicalDevice> physicalDevices(physicalDeviceCount);
    if (enumeratePhysicalDevices(instance, &physicalDeviceCount, physicalDevices.data()) != VK_SUCCESS) {
        VK_LOG("[Vulkan] Initialize failed: could not enumerate physical devices\n");
        return false;
    }

    for (auto candidate : physicalDevices) {
        uint32_t queueCount = 0;
        getPhysicalDeviceQueueFamilyProperties(candidate, &queueCount, nullptr);
        if (queueCount == 0) {
            continue;
        }

        std::vector<VkQueueFamilyProperties> queueFamilies(queueCount);
        getPhysicalDeviceQueueFamilyProperties(candidate, &queueCount, queueFamilies.data());

        for (uint32_t index = 0; index < queueCount; ++index) {
            VkBool32 presentSupported = VK_FALSE;
            if (getPhysicalDeviceSurfaceSupport(candidate, index, surface, &presentSupported) != VK_SUCCESS) {
                continue;
            }

            if ((queueFamilies[index].queueFlags & VK_QUEUE_GRAPHICS_BIT) != 0 && presentSupported == VK_TRUE) {
                physicalDevice = candidate;
                queueFamilyIndex = index;
                break;
            }
        }

        if (physicalDevice != VK_NULL_HANDLE) {
            break;
        }
    }

    if (physicalDevice == VK_NULL_HANDLE || queueFamilyIndex == VK_QUEUE_FAMILY_IGNORED) {
        VK_LOG("[Vulkan] Initialize failed: no suitable GPU with graphics+present queue found\n");
        return false;
    }

    const float queuePriority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo {};
    queueInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    queueInfo.queueFamilyIndex = queueFamilyIndex;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &queuePriority;

    const char* deviceExtensions[] = { "VK_KHR_swapchain" };
    VkDeviceCreateInfo deviceInfo {};
    deviceInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;
    deviceInfo.enabledExtensionCount = 1;
    deviceInfo.ppEnabledExtensionNames = deviceExtensions;
    if (createDevice(physicalDevice, &deviceInfo, nullptr, &device) != VK_SUCCESS || device == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] Initialize failed: vkCreateDevice returned failure\n");
        return false;
    }

    destroyDevice = LoadDeviceProc<PFN_vkDestroyDevice>(getDeviceProcAddr, device, "vkDestroyDevice");
    getDeviceQueue = LoadDeviceProc<PFN_vkGetDeviceQueue>(getDeviceProcAddr, device, "vkGetDeviceQueue");
    createSwapchain = LoadDeviceProc<PFN_vkCreateSwapchainKHR>(getDeviceProcAddr, device, "vkCreateSwapchainKHR");
    destroySwapchain = LoadDeviceProc<PFN_vkDestroySwapchainKHR>(getDeviceProcAddr, device, "vkDestroySwapchainKHR");
    getSwapchainImages = LoadDeviceProc<PFN_vkGetSwapchainImagesKHR>(getDeviceProcAddr, device, "vkGetSwapchainImagesKHR");
    acquireNextImage = LoadDeviceProc<PFN_vkAcquireNextImageKHR>(getDeviceProcAddr, device, "vkAcquireNextImageKHR");
    queuePresent = LoadDeviceProc<PFN_vkQueuePresentKHR>(getDeviceProcAddr, device, "vkQueuePresentKHR");
    createCommandPool = LoadDeviceProc<PFN_vkCreateCommandPool>(getDeviceProcAddr, device, "vkCreateCommandPool");
    destroyCommandPool = LoadDeviceProc<PFN_vkDestroyCommandPool>(getDeviceProcAddr, device, "vkDestroyCommandPool");
    allocateCommandBuffers = LoadDeviceProc<PFN_vkAllocateCommandBuffers>(getDeviceProcAddr, device, "vkAllocateCommandBuffers");
    createBuffer = LoadDeviceProc<PFN_vkCreateBuffer>(getDeviceProcAddr, device, "vkCreateBuffer");
    destroyBuffer = LoadDeviceProc<PFN_vkDestroyBuffer>(getDeviceProcAddr, device, "vkDestroyBuffer");
    getBufferMemoryRequirements = LoadDeviceProc<PFN_vkGetBufferMemoryRequirements>(getDeviceProcAddr, device, "vkGetBufferMemoryRequirements");
    createImage = LoadDeviceProc<PFN_vkCreateImage>(getDeviceProcAddr, device, "vkCreateImage");
    destroyImage = LoadDeviceProc<PFN_vkDestroyImage>(getDeviceProcAddr, device, "vkDestroyImage");
    getImageMemoryRequirements = LoadDeviceProc<PFN_vkGetImageMemoryRequirements>(getDeviceProcAddr, device, "vkGetImageMemoryRequirements");
    allocateMemory = LoadDeviceProc<PFN_vkAllocateMemory>(getDeviceProcAddr, device, "vkAllocateMemory");
    freeMemory = LoadDeviceProc<PFN_vkFreeMemory>(getDeviceProcAddr, device, "vkFreeMemory");
    bindBufferMemory = LoadDeviceProc<PFN_vkBindBufferMemory>(getDeviceProcAddr, device, "vkBindBufferMemory");
    bindImageMemory = LoadDeviceProc<PFN_vkBindImageMemory>(getDeviceProcAddr, device, "vkBindImageMemory");
    mapMemory = LoadDeviceProc<PFN_vkMapMemory>(getDeviceProcAddr, device, "vkMapMemory");
    unmapMemory = LoadDeviceProc<PFN_vkUnmapMemory>(getDeviceProcAddr, device, "vkUnmapMemory");
    createImageView = LoadDeviceProc<PFN_vkCreateImageView>(getDeviceProcAddr, device, "vkCreateImageView");
    destroyImageView = LoadDeviceProc<PFN_vkDestroyImageView>(getDeviceProcAddr, device, "vkDestroyImageView");
    createSampler = LoadDeviceProc<PFN_vkCreateSampler>(getDeviceProcAddr, device, "vkCreateSampler");
    destroySampler = LoadDeviceProc<PFN_vkDestroySampler>(getDeviceProcAddr, device, "vkDestroySampler");
    createDescriptorSetLayout = LoadDeviceProc<PFN_vkCreateDescriptorSetLayout>(getDeviceProcAddr, device, "vkCreateDescriptorSetLayout");
    destroyDescriptorSetLayout = LoadDeviceProc<PFN_vkDestroyDescriptorSetLayout>(getDeviceProcAddr, device, "vkDestroyDescriptorSetLayout");
    createDescriptorPool = LoadDeviceProc<PFN_vkCreateDescriptorPool>(getDeviceProcAddr, device, "vkCreateDescriptorPool");
    destroyDescriptorPool = LoadDeviceProc<PFN_vkDestroyDescriptorPool>(getDeviceProcAddr, device, "vkDestroyDescriptorPool");
    allocateDescriptorSets = LoadDeviceProc<PFN_vkAllocateDescriptorSets>(getDeviceProcAddr, device, "vkAllocateDescriptorSets");
    updateDescriptorSets = LoadDeviceProc<PFN_vkUpdateDescriptorSets>(getDeviceProcAddr, device, "vkUpdateDescriptorSets");
    createPipelineLayout = LoadDeviceProc<PFN_vkCreatePipelineLayout>(getDeviceProcAddr, device, "vkCreatePipelineLayout");
    destroyPipelineLayout = LoadDeviceProc<PFN_vkDestroyPipelineLayout>(getDeviceProcAddr, device, "vkDestroyPipelineLayout");
    createShaderModule = LoadDeviceProc<PFN_vkCreateShaderModule>(getDeviceProcAddr, device, "vkCreateShaderModule");
    destroyShaderModule = LoadDeviceProc<PFN_vkDestroyShaderModule>(getDeviceProcAddr, device, "vkDestroyShaderModule");
    createRenderPass = LoadDeviceProc<PFN_vkCreateRenderPass>(getDeviceProcAddr, device, "vkCreateRenderPass");
    destroyRenderPass = LoadDeviceProc<PFN_vkDestroyRenderPass>(getDeviceProcAddr, device, "vkDestroyRenderPass");
    createFramebuffer = LoadDeviceProc<PFN_vkCreateFramebuffer>(getDeviceProcAddr, device, "vkCreateFramebuffer");
    destroyFramebuffer = LoadDeviceProc<PFN_vkDestroyFramebuffer>(getDeviceProcAddr, device, "vkDestroyFramebuffer");
    createGraphicsPipelines = LoadDeviceProc<PFN_vkCreateGraphicsPipelines>(getDeviceProcAddr, device, "vkCreateGraphicsPipelines");
    destroyPipeline = LoadDeviceProc<PFN_vkDestroyPipeline>(getDeviceProcAddr, device, "vkDestroyPipeline");
    resetCommandBuffer = LoadDeviceProc<PFN_vkResetCommandBuffer>(getDeviceProcAddr, device, "vkResetCommandBuffer");
    beginCommandBuffer = LoadDeviceProc<PFN_vkBeginCommandBuffer>(getDeviceProcAddr, device, "vkBeginCommandBuffer");
    endCommandBuffer = LoadDeviceProc<PFN_vkEndCommandBuffer>(getDeviceProcAddr, device, "vkEndCommandBuffer");
    cmdPipelineBarrier = LoadDeviceProc<PFN_vkCmdPipelineBarrier>(getDeviceProcAddr, device, "vkCmdPipelineBarrier");
    cmdClearColorImage = LoadDeviceProc<PFN_vkCmdClearColorImage>(getDeviceProcAddr, device, "vkCmdClearColorImage");
    cmdCopyBufferToImage = LoadDeviceProc<PFN_vkCmdCopyBufferToImage>(getDeviceProcAddr, device, "vkCmdCopyBufferToImage");
    cmdBeginRenderPass = LoadDeviceProc<PFN_vkCmdBeginRenderPass>(getDeviceProcAddr, device, "vkCmdBeginRenderPass");
    cmdEndRenderPass = LoadDeviceProc<PFN_vkCmdEndRenderPass>(getDeviceProcAddr, device, "vkCmdEndRenderPass");
    cmdBindPipeline = LoadDeviceProc<PFN_vkCmdBindPipeline>(getDeviceProcAddr, device, "vkCmdBindPipeline");
    cmdBindDescriptorSets = LoadDeviceProc<PFN_vkCmdBindDescriptorSets>(getDeviceProcAddr, device, "vkCmdBindDescriptorSets");
    cmdSetViewport = LoadDeviceProc<PFN_vkCmdSetViewport>(getDeviceProcAddr, device, "vkCmdSetViewport");
    cmdSetScissor = LoadDeviceProc<PFN_vkCmdSetScissor>(getDeviceProcAddr, device, "vkCmdSetScissor");
    cmdPushConstants = LoadDeviceProc<PFN_vkCmdPushConstants>(getDeviceProcAddr, device, "vkCmdPushConstants");
    cmdDraw = LoadDeviceProc<PFN_vkCmdDraw>(getDeviceProcAddr, device, "vkCmdDraw");
    cmdBindVertexBuffers = LoadDeviceProc<PFN_vkCmdBindVertexBuffers>(getDeviceProcAddr, device, "vkCmdBindVertexBuffers");
    queueSubmit = LoadDeviceProc<PFN_vkQueueSubmit>(getDeviceProcAddr, device, "vkQueueSubmit");
    createSemaphore = LoadDeviceProc<PFN_vkCreateSemaphore>(getDeviceProcAddr, device, "vkCreateSemaphore");
    destroySemaphore = LoadDeviceProc<PFN_vkDestroySemaphore>(getDeviceProcAddr, device, "vkDestroySemaphore");
    createFence = LoadDeviceProc<PFN_vkCreateFence>(getDeviceProcAddr, device, "vkCreateFence");
    destroyFence = LoadDeviceProc<PFN_vkDestroyFence>(getDeviceProcAddr, device, "vkDestroyFence");
    waitForFences = LoadDeviceProc<PFN_vkWaitForFences>(getDeviceProcAddr, device, "vkWaitForFences");
    resetFences = LoadDeviceProc<PFN_vkResetFences>(getDeviceProcAddr, device, "vkResetFences");
    deviceWaitIdle = LoadDeviceProc<PFN_vkDeviceWaitIdle>(getDeviceProcAddr, device, "vkDeviceWaitIdle");
    if (!destroyDevice || !getDeviceQueue || !createSwapchain || !destroySwapchain || !getSwapchainImages ||
        !acquireNextImage || !queuePresent || !createCommandPool || !destroyCommandPool ||
        !allocateCommandBuffers || !createBuffer || !destroyBuffer || !getBufferMemoryRequirements ||
        !createImage || !destroyImage || !getImageMemoryRequirements || !allocateMemory || !freeMemory ||
        !bindBufferMemory || !bindImageMemory || !mapMemory || !unmapMemory || !createImageView ||
        !destroyImageView || !createSampler || !destroySampler || !createDescriptorSetLayout ||
        !destroyDescriptorSetLayout || !createDescriptorPool || !destroyDescriptorPool ||
        !allocateDescriptorSets || !updateDescriptorSets || !createPipelineLayout ||
        !destroyPipelineLayout || !createShaderModule || !destroyShaderModule || !createRenderPass ||
        !destroyRenderPass || !createFramebuffer || !destroyFramebuffer || !createGraphicsPipelines ||
        !destroyPipeline || !resetCommandBuffer || !beginCommandBuffer || !endCommandBuffer ||
        !cmdPipelineBarrier || !cmdClearColorImage || !cmdCopyBufferToImage || !cmdBeginRenderPass ||
        !cmdEndRenderPass || !cmdBindPipeline || !cmdBindDescriptorSets || !cmdSetViewport ||
        !cmdSetScissor || !cmdPushConstants || !cmdDraw || !cmdBindVertexBuffers || !queueSubmit || !createSemaphore || !destroySemaphore ||
        !createFence || !destroyFence || !waitForFences || !resetFences || !deviceWaitIdle) {
        VK_LOG("[Vulkan] Initialize failed: could not load required device-level function pointers\n");
        return false;
    }

    getDeviceQueue(device, queueFamilyIndex, 0, &queue);
    if (queue == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] Initialize failed: vkGetDeviceQueue returned null queue\n");
        return false;
    }

    VkCommandPoolCreateInfo commandPoolInfo {};
    commandPoolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    commandPoolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    commandPoolInfo.queueFamilyIndex = queueFamilyIndex;
    if (createCommandPool(device, &commandPoolInfo, nullptr, &commandPool) != VK_SUCCESS || commandPool == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] Initialize failed: could not create command pool\n");
        return false;
    }

    // Allocate MAX_FRAMES_IN_FLIGHT command buffers, one per frame slot.
    VkCommandBufferAllocateInfo commandBufferInfo {};
    commandBufferInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
    commandBufferInfo.commandPool = commandPool;
    commandBufferInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
    commandBufferInfo.commandBufferCount = MAX_FRAMES_IN_FLIGHT;
    VkCommandBuffer allocatedCommandBuffers[MAX_FRAMES_IN_FLIGHT] = {};
    if (allocateCommandBuffers(device, &commandBufferInfo, allocatedCommandBuffers) != VK_SUCCESS) {
        VK_LOG("[Vulkan] Initialize failed: could not allocate command buffers\n");
        return false;
    }

    VkSemaphoreCreateInfo semaphoreInfo {};
    semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;

    // Create fences SIGNALED so the first DrawFrame can unconditionally waitForFences
    // without stalling (the fence is already ready). Without this flag the first
    // wait on an un-signaled fence would hang forever.
    VkFenceCreateInfo fenceInfo {};
    fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
    fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;

    for (uint32_t frameIndex = 0; frameIndex < MAX_FRAMES_IN_FLIGHT; ++frameIndex) {
        auto& frameState = perFrameStates_[frameIndex];
        frameState.commandBuffer = allocatedCommandBuffers[frameIndex];
        if (createSemaphore(device, &semaphoreInfo, nullptr, &frameState.imageAvailable) != VK_SUCCESS ||
            frameState.imageAvailable == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] Initialize failed: could not create imageAvailable semaphore for frame %u\n", frameIndex);
            return false;
        }
        if (createFence(device, &fenceInfo, nullptr, &frameState.inFlight) != VK_SUCCESS ||
            frameState.inFlight == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] Initialize failed: could not create inFlight fence for frame %u\n", frameIndex);
            return false;
        }
    }

    // Start on frame 0; pull its (empty) resources into the alias.
    currentFrame_ = 0;
    BeginFrame();

    initialized = RecreateSwapchain(width, height, vsync);
    if (initialized) {
        VK_LOG("[Vulkan] Initialize succeeded: format=%d extent=%ux%u images=%zu\n",
                static_cast<int>(format), extent.width, extent.height, images.size());
    } else {
        VK_LOG("[Vulkan] Initialize failed: RecreateSwapchain returned false\n");
    }
    return initialized;
#endif
}

bool VulkanRenderTarget::Impl::RecreateSwapchain(int32_t width, int32_t height, bool vsync)
{
    if (!device || !surface || !createSwapchain) {
        return false;
    }

    if (swapchain != VK_NULL_HANDLE && deviceWaitIdle) {
        deviceWaitIdle(device);
    }

    DestroyGraphicsResources();

    VkSurfaceCapabilitiesKHR capabilities {};
    if (getSurfaceCapabilities(physicalDevice, surface, &capabilities) != VK_SUCCESS) {
        return false;
    }

    uint32_t formatCount = 0;
    if (getSurfaceFormats(physicalDevice, surface, &formatCount, nullptr) != VK_SUCCESS || formatCount == 0) {
        return false;
    }

    std::vector<VkSurfaceFormatKHR> formats(formatCount);
    if (getSurfaceFormats(physicalDevice, surface, &formatCount, formats.data()) != VK_SUCCESS) {
        return false;
    }

    VkSurfaceFormatKHR selectedFormat = formats.front();
    // Prefer UNORM format to match D3D12's DXGI_FORMAT_R8G8B8A8_UNORM behavior.
    // CPU canvas and GPU replay pass sRGB color values directly, so using an SRGB
    // swapchain would apply an unwanted linear→sRGB conversion (double encoding).
    // Try B8G8R8A8 first (Windows/desktop common), then R8G8B8A8 (Android common).
    const VkFormat preferredFormats[] = {
        VK_FORMAT_B8G8R8A8_UNORM,
        VK_FORMAT_R8G8B8A8_UNORM,
        VK_FORMAT_B8G8R8A8_SRGB,
        VK_FORMAT_R8G8B8A8_SRGB,
    };
    bool foundPreferred = false;
    for (auto preferred : preferredFormats) {
        if (foundPreferred) break;
        for (const auto& candidate : formats) {
            if (candidate.format == preferred && candidate.colorSpace == VK_COLOR_SPACE_SRGB_NONLINEAR_KHR) {
                selectedFormat = candidate;
                foundPreferred = true;
                break;
            }
        }
    }
    VK_LOG("[Vulkan] Selected swapchain format: %d (from %u available)\n",
            static_cast<int>(selectedFormat.format), formatCount);

    uint32_t presentModeCount = 0;
    if (getSurfacePresentModes(physicalDevice, surface, &presentModeCount, nullptr) != VK_SUCCESS || presentModeCount == 0) {
        return false;
    }

    std::vector<VkPresentModeKHR> presentModes(presentModeCount);
    if (getSurfacePresentModes(physicalDevice, surface, &presentModeCount, presentModes.data()) != VK_SUCCESS) {
        return false;
    }

    VkPresentModeKHR selectedPresentMode = VK_PRESENT_MODE_FIFO_KHR;
    if (!vsync) {
        for (auto candidate : presentModes) {
            if (candidate == VK_PRESENT_MODE_IMMEDIATE_KHR) {
                selectedPresentMode = candidate;
                break;
            }
        }
    }

    VkExtent2D newExtent {};
    if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max()) {
        newExtent = capabilities.currentExtent;
    } else {
        newExtent.width = ClampExtent(static_cast<uint32_t>(width), capabilities.minImageExtent.width, capabilities.maxImageExtent.width);
        newExtent.height = ClampExtent(static_cast<uint32_t>(height), capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
    }

    uint32_t imageCount = capabilities.minImageCount + 1;
    if (capabilities.maxImageCount > 0 && imageCount > capabilities.maxImageCount) {
        imageCount = capabilities.maxImageCount;
    }

    VkCompositeAlphaFlagBitsKHR compositeAlpha = VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR;
    if ((capabilities.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_OPAQUE_BIT_KHR) == 0) {
        if ((capabilities.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR) != 0) {
            compositeAlpha = VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR;
        } else if ((capabilities.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_POST_MULTIPLIED_BIT_KHR) != 0) {
            compositeAlpha = VK_COMPOSITE_ALPHA_POST_MULTIPLIED_BIT_KHR;
        } else if ((capabilities.supportedCompositeAlpha & VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR) != 0) {
            compositeAlpha = VK_COMPOSITE_ALPHA_INHERIT_BIT_KHR;
        }
    }

    const VkImageUsageFlags imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    if ((capabilities.supportedUsageFlags & imageUsage) == 0) {
        return false;
    }

    VkSwapchainKHR oldSwapchain = swapchain;
    VkSwapchainCreateInfoKHR swapchainInfo {};
    swapchainInfo.sType = VK_STRUCTURE_TYPE_SWAPCHAIN_CREATE_INFO_KHR;
    swapchainInfo.surface = surface;
    swapchainInfo.minImageCount = imageCount;
    swapchainInfo.imageFormat = selectedFormat.format;
    swapchainInfo.imageColorSpace = selectedFormat.colorSpace;
    swapchainInfo.imageExtent = newExtent;
    swapchainInfo.imageArrayLayers = 1;
    swapchainInfo.imageUsage = imageUsage;
    swapchainInfo.imageSharingMode = VK_SHARING_MODE_EXCLUSIVE;
    swapchainInfo.preTransform = capabilities.currentTransform;
    swapchainInfo.compositeAlpha = compositeAlpha;
    swapchainInfo.presentMode = selectedPresentMode;
    swapchainInfo.clipped = VK_TRUE;
    swapchainInfo.oldSwapchain = oldSwapchain;

    VkSwapchainKHR newSwapchain = VK_NULL_HANDLE;
    if (createSwapchain(device, &swapchainInfo, nullptr, &newSwapchain) != VK_SUCCESS || newSwapchain == VK_NULL_HANDLE) {
        return false;
    }

    uint32_t actualImageCount = 0;
    if (getSwapchainImages(device, newSwapchain, &actualImageCount, nullptr) != VK_SUCCESS || actualImageCount == 0) {
        destroySwapchain(device, newSwapchain, nullptr);
        return false;
    }

    std::vector<VkImage> newImages(actualImageCount);
    if (getSwapchainImages(device, newSwapchain, &actualImageCount, newImages.data()) != VK_SUCCESS) {
        destroySwapchain(device, newSwapchain, nullptr);
        return false;
    }

    if (oldSwapchain != VK_NULL_HANDLE) {
        destroySwapchain(device, oldSwapchain, nullptr);
    }

    swapchain = newSwapchain;
    images = std::move(newImages);
    imageLayouts.assign(images.size(), VK_IMAGE_LAYOUT_UNDEFINED);
    extent = newExtent;
    format = selectedFormat.format;
    submitted = false;

    // Recreate per-image renderFinished semaphores sized to the new image count.
    for (VkSemaphore sem : renderFinishedPerImage) {
        if (sem != VK_NULL_HANDLE && destroySemaphore) {
            destroySemaphore(device, sem, nullptr);
        }
    }
    renderFinishedPerImage.clear();
    renderFinishedPerImage.resize(images.size(), VK_NULL_HANDLE);
    VkSemaphoreCreateInfo semaphoreInfo {};
    semaphoreInfo.sType = VK_STRUCTURE_TYPE_SEMAPHORE_CREATE_INFO;
    for (size_t i = 0; i < renderFinishedPerImage.size(); ++i) {
        if (createSemaphore(device, &semaphoreInfo, nullptr, &renderFinishedPerImage[i]) != VK_SUCCESS ||
            renderFinishedPerImage[i] == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] RecreateSwapchain: failed to create renderFinished semaphore for image %zu\n", i);
            return false;
        }
    }
    // Staging buffer and upload image are lazy — do not create them here. Each
    // per-frame slot will allocate its own copy the first time DrawFrame runs on
    // that slot, avoiding cross-frame alias pollution that would happen if this
    // function (called out of the BeginFrame/EndFrame cycle) wrote into the alias.
    return true;
}

bool VulkanRenderTarget::Impl::EnsureUploadImage(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) {
        return false;
    }

    if (uploadImage != VK_NULL_HANDLE && uploadWidth == width && uploadHeight == height) {
        return true;
    }

    DestroyUploadImage();

    VkImageCreateInfo imageInfo {};
    imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    imageInfo.format = GetUploadImageFormat(format);
    imageInfo.extent.width = width;
    imageInfo.extent.height = height;
    imageInfo.extent.depth = 1;
    imageInfo.mipLevels = 1;
    imageInfo.arrayLayers = 1;
    imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
    imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
    imageInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
    imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (createImage(device, &imageInfo, nullptr, &uploadImage) != VK_SUCCESS || uploadImage == VK_NULL_HANDLE) {
        return false;
    }

    VkMemoryRequirements memoryRequirements {};
    getImageMemoryRequirements(device, uploadImage, &memoryRequirements);

    const uint32_t memoryTypeIndex = FindMemoryType(memoryRequirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (memoryTypeIndex == VK_QUEUE_FAMILY_IGNORED) {
        return false;
    }

    VkMemoryAllocateInfo allocateInfo {};
    allocateInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocateInfo.allocationSize = memoryRequirements.size;
    allocateInfo.memoryTypeIndex = memoryTypeIndex;
    if (allocateMemory(device, &allocateInfo, nullptr, &uploadImageMemory) != VK_SUCCESS || uploadImageMemory == VK_NULL_HANDLE) {
        return false;
    }

    if (bindImageMemory(device, uploadImage, uploadImageMemory, 0) != VK_SUCCESS) {
        return false;
    }

    VkImageViewCreateInfo viewInfo {};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = uploadImage;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = imageInfo.format;
    viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewInfo.subresourceRange.levelCount = 1;
    viewInfo.subresourceRange.layerCount = 1;
    if (createImageView(device, &viewInfo, nullptr, &uploadImageView) != VK_SUCCESS || uploadImageView == VK_NULL_HANDLE) {
        return false;
    }

    uploadWidth = width;
    uploadHeight = height;
    uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    return uploadImageView == VK_NULL_HANDLE ? true : UpdateFrameDescriptorSet();
}

bool VulkanRenderTarget::Impl::EnsureUploadImageCapacity(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) {
        return false;
    }

    if (uploadImage != VK_NULL_HANDLE && uploadWidth >= width && uploadHeight >= height) {
        return true;
    }

    return EnsureUploadImage(width, height);
}

bool VulkanRenderTarget::Impl::EnsureTransitionImagesCapacity(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) {
        return false;
    }

    if (transitionImages[0] != VK_NULL_HANDLE && transitionWidth == width && transitionHeight == height) {
        return true;
    }

    DestroyTransitionImages();

    for (int index = 0; index < 2; ++index) {
        VkImageCreateInfo imageInfo {};
        imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        imageInfo.imageType = VK_IMAGE_TYPE_2D;
        imageInfo.format = GetUploadImageFormat(format);
        imageInfo.extent.width = width;
        imageInfo.extent.height = height;
        imageInfo.extent.depth = 1;
        imageInfo.mipLevels = 1;
        imageInfo.arrayLayers = 1;
        imageInfo.samples = VK_SAMPLE_COUNT_1_BIT;
        imageInfo.tiling = VK_IMAGE_TILING_OPTIMAL;
        imageInfo.usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT | VK_IMAGE_USAGE_SAMPLED_BIT;
        imageInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        imageInfo.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (createImage(device, &imageInfo, nullptr, &transitionImages[index]) != VK_SUCCESS || transitionImages[index] == VK_NULL_HANDLE) {
            return false;
        }

        VkMemoryRequirements memoryRequirements {};
        getImageMemoryRequirements(device, transitionImages[index], &memoryRequirements);
        const uint32_t memoryTypeIndex = FindMemoryType(memoryRequirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (memoryTypeIndex == VK_QUEUE_FAMILY_IGNORED) {
            return false;
        }

        VkMemoryAllocateInfo allocateInfo {};
        allocateInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocateInfo.allocationSize = memoryRequirements.size;
        allocateInfo.memoryTypeIndex = memoryTypeIndex;
        if (allocateMemory(device, &allocateInfo, nullptr, &transitionImageMemory[index]) != VK_SUCCESS || transitionImageMemory[index] == VK_NULL_HANDLE) {
            return false;
        }

        if (bindImageMemory(device, transitionImages[index], transitionImageMemory[index], 0) != VK_SUCCESS) {
            return false;
        }

        VkImageViewCreateInfo viewInfo {};
        viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        viewInfo.image = transitionImages[index];
        viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
        viewInfo.format = imageInfo.format;
        viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        viewInfo.subresourceRange.levelCount = 1;
        viewInfo.subresourceRange.layerCount = 1;
        if (createImageView(device, &viewInfo, nullptr, &transitionImageViews[index]) != VK_SUCCESS || transitionImageViews[index] == VK_NULL_HANDLE) {
            return false;
        }

        transitionImageLayouts[index] = VK_IMAGE_LAYOUT_UNDEFINED;
    }

    transitionWidth = width;
    transitionHeight = height;
    return UpdateTransitionDescriptorSet();
}

bool VulkanRenderTarget::Impl::EnsureGraphicsResources()
{
    if (swapchain == VK_NULL_HANDLE || images.empty()) {
        VK_LOG("[Vulkan] EnsureGraphicsResources: swapchain=%p images=%zu", (void*)swapchain, images.size());
        return false;
    }

    if (frameRenderPass != VK_NULL_HANDLE && framePipeline != VK_NULL_HANDLE && frameDescriptorSet != VK_NULL_HANDLE &&
        imageViews.size() == images.size() && framebuffers.size() == images.size()) {
        const bool hasAllImageViews = std::all_of(imageViews.begin(), imageViews.end(), [](VkImageView imageView) {
            return imageView != VK_NULL_HANDLE;
        });
        const bool hasAllFramebuffers = std::all_of(framebuffers.begin(), framebuffers.end(), [](VkFramebuffer framebuffer) {
            return framebuffer != VK_NULL_HANDLE;
        });
        if (hasAllImageViews && hasAllFramebuffers) {
            return true;
        }
    }

    if (frameSampler == VK_NULL_HANDLE) {
        VkSamplerCreateInfo samplerInfo {};
        samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        samplerInfo.magFilter = VK_FILTER_LINEAR;
        samplerInfo.minFilter = VK_FILTER_LINEAR;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
        samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.maxLod = 1.0f;
        if (createSampler(device, &samplerInfo, nullptr, &frameSampler) != VK_SUCCESS || frameSampler == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createSampler failed");
            return false;
        }
    }

    if (frameDescriptorSetLayout == VK_NULL_HANDLE) {
        VkDescriptorSetLayoutBinding bindings[2] {};
        bindings[0].binding = 0;
        bindings[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        bindings[0].descriptorCount = 1;
        bindings[0].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        bindings[1].binding = 1;
        bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
        bindings[1].descriptorCount = 1;
        bindings[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;

        VkDescriptorSetLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        layoutInfo.bindingCount = 2;
        layoutInfo.pBindings = bindings;
        if (createDescriptorSetLayout(device, &layoutInfo, nullptr, &frameDescriptorSetLayout) != VK_SUCCESS || frameDescriptorSetLayout == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createDescriptorSetLayout failed");
            return false;
        }
    }

    if (frameDescriptorPool == VK_NULL_HANDLE) {
        VkDescriptorPoolSize poolSizes[2] {};
        poolSizes[0].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        poolSizes[0].descriptorCount = MAX_FRAMES_IN_FLIGHT;
        poolSizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLER;
        poolSizes[1].descriptorCount = MAX_FRAMES_IN_FLIGHT;

        VkDescriptorPoolCreateInfo poolInfo {};
        poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolInfo.maxSets = MAX_FRAMES_IN_FLIGHT;
        poolInfo.poolSizeCount = 2;
        poolInfo.pPoolSizes = poolSizes;
        if (createDescriptorPool(device, &poolInfo, nullptr, &frameDescriptorPool) != VK_SUCCESS || frameDescriptorPool == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (frameDescriptorSet == VK_NULL_HANDLE) {
        VkDescriptorSetAllocateInfo allocateInfo {};
        allocateInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        allocateInfo.descriptorPool = frameDescriptorPool;
        allocateInfo.descriptorSetCount = 1;
        allocateInfo.pSetLayouts = &frameDescriptorSetLayout;
        if (allocateDescriptorSets(device, &allocateInfo, &frameDescriptorSet) != VK_SUCCESS || frameDescriptorSet == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (framePipelineLayout == VK_NULL_HANDLE) {
        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &frameDescriptorSetLayout;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &framePipelineLayout) != VK_SUCCESS || framePipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (frameRenderPass == VK_NULL_HANDLE) {
        VkAttachmentDescription colorAttachment {};
        colorAttachment.format = format;
        colorAttachment.samples = VK_SAMPLE_COUNT_1_BIT;
        colorAttachment.loadOp = VK_ATTACHMENT_LOAD_OP_LOAD;
        colorAttachment.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        colorAttachment.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        colorAttachment.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        colorAttachment.initialLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        colorAttachment.finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

        VkAttachmentReference colorAttachmentRef {};
        colorAttachmentRef.attachment = 0;
        colorAttachmentRef.layout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;

        VkSubpassDescription subpass {};
        subpass.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        subpass.colorAttachmentCount = 1;
        subpass.pColorAttachments = &colorAttachmentRef;

        VkSubpassDependency dependency {};
        dependency.srcSubpass = VK_SUBPASS_EXTERNAL;
        dependency.dstSubpass = 0;
        dependency.srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        dependency.dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        dependency.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;

        VkRenderPassCreateInfo renderPassInfo {};
        renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
        renderPassInfo.attachmentCount = 1;
        renderPassInfo.pAttachments = &colorAttachment;
        renderPassInfo.subpassCount = 1;
        renderPassInfo.pSubpasses = &subpass;
        renderPassInfo.dependencyCount = 1;
        renderPassInfo.pDependencies = &dependency;
        if (createRenderPass(device, &renderPassInfo, nullptr, &frameRenderPass) != VK_SUCCESS || frameRenderPass == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (framePipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kFrameCompositeVertexShaderSpvSize;
        vertexShaderInfo.pCode = kFrameCompositeVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kFrameCompositeFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kFrameCompositeFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = framePipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &framePipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || framePipeline == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createGraphicsPipelines(frame) failed (%d)", static_cast<int>(pipelineResult));
            return false;
        }
    }

    if (solidRectPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(SolidRectPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &solidRectPipelineLayout) != VK_SUCCESS || solidRectPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (solidRectPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kSolidRectVertexShaderSpvSize;
        vertexShaderInfo.pCode = kSolidRectVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kSolidRectFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kSolidRectFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = solidRectPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &solidRectPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || solidRectPipeline == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createGraphicsPipelines(solidRect) failed (%d)", static_cast<int>(pipelineResult));
            return false;
        }
    }

    if (clearRectPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kSolidRectVertexShaderSpvSize;
        vertexShaderInfo.pCode = kSolidRectVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kSolidRectFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kSolidRectFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ZERO;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ZERO;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ZERO;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ZERO;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = solidRectPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &clearRectPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || clearRectPipeline == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createGraphicsPipelines(clearRect) failed (%d)", static_cast<int>(pipelineResult));
            return false;
        }
    }

    if (bitmapPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(BitmapQuadPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &frameDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &bitmapPipelineLayout) != VK_SUCCESS || bitmapPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (bitmapPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kBitmapQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kBitmapQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kBitmapQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kBitmapQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = bitmapPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &bitmapPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || bitmapPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (blurPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(BlurPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &frameDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &blurPipelineLayout) != VK_SUCCESS || blurPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (blurPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kBlurQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kBlurQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kBlurQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kBlurQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = blurPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &blurPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || blurPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (backdropPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(BackdropPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &frameDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &backdropPipelineLayout) != VK_SUCCESS || backdropPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (backdropPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kBackdropQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kBackdropQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kBackdropQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kBackdropQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = backdropPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &backdropPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || backdropPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (glowPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(GlowPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &glowPipelineLayout) != VK_SUCCESS || glowPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (glowPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kGlowQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kGlowQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kGlowQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kGlowQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = glowPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &glowPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || glowPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (liquidGlassPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(LiquidGlassPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &frameDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &liquidGlassPipelineLayout) != VK_SUCCESS || liquidGlassPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (liquidGlassPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kLiquidGlassQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kLiquidGlassQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kLiquidGlassQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kLiquidGlassQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = liquidGlassPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &liquidGlassPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || liquidGlassPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (triangleFillPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(TriangleFillPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &triangleFillPipelineLayout) != VK_SUCCESS || triangleFillPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (triangleFillPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kTriangleFillVertexShaderSpvSize;
        vertexShaderInfo.pCode = kTriangleFillVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kTriangleFillFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kTriangleFillFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkVertexInputBindingDescription bindingDescription {};
        bindingDescription.binding = 0;
        bindingDescription.stride = sizeof(float) * 2;
        bindingDescription.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

        VkVertexInputAttributeDescription attributeDescription {};
        attributeDescription.location = 0;
        attributeDescription.binding = 0;
        attributeDescription.format = VK_FORMAT_R32G32_SFLOAT;
        attributeDescription.offset = 0;

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
        vertexInputInfo.vertexBindingDescriptionCount = 1;
        vertexInputInfo.pVertexBindingDescriptions = &bindingDescription;
        vertexInputInfo.vertexAttributeDescriptionCount = 1;
        vertexInputInfo.pVertexAttributeDescriptions = &attributeDescription;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = triangleFillPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &triangleFillPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || triangleFillPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }


    if (transitionDescriptorSetLayout == VK_NULL_HANDLE) {
        VkDescriptorSetLayoutBinding bindings[3] {};
        bindings[0].binding = 0;
        bindings[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        bindings[0].descriptorCount = 1;
        bindings[0].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        bindings[1].binding = 1;
        bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        bindings[1].descriptorCount = 1;
        bindings[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        bindings[2].binding = 2;
        bindings[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
        bindings[2].descriptorCount = 1;
        bindings[2].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;

        VkDescriptorSetLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        layoutInfo.bindingCount = 3;
        layoutInfo.pBindings = bindings;
        if (createDescriptorSetLayout(device, &layoutInfo, nullptr, &transitionDescriptorSetLayout) != VK_SUCCESS || transitionDescriptorSetLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (transitionDescriptorPool == VK_NULL_HANDLE) {
        VkDescriptorPoolSize poolSizes[2] {};
        poolSizes[0].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        poolSizes[0].descriptorCount = 2;
        poolSizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLER;
        poolSizes[1].descriptorCount = 1;

        VkDescriptorPoolCreateInfo poolInfo {};
        poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolInfo.maxSets = 1;
        poolInfo.poolSizeCount = 2;
        poolInfo.pPoolSizes = poolSizes;
        if (createDescriptorPool(device, &poolInfo, nullptr, &transitionDescriptorPool) != VK_SUCCESS || transitionDescriptorPool == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (transitionDescriptorSet == VK_NULL_HANDLE) {
        VkDescriptorSetAllocateInfo allocateInfo {};
        allocateInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        allocateInfo.descriptorPool = transitionDescriptorPool;
        allocateInfo.descriptorSetCount = 1;
        allocateInfo.pSetLayouts = &transitionDescriptorSetLayout;
        if (allocateDescriptorSets(device, &allocateInfo, &transitionDescriptorSet) != VK_SUCCESS || transitionDescriptorSet == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (transitionPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(TransitionPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &transitionDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &transitionPipelineLayout) != VK_SUCCESS || transitionPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (transitionPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kTransitionQuadVertexShaderSpvSize;
        vertexShaderInfo.pCode = kTransitionQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kTransitionQuadFragmentShaderSpvSize;
        fragmentShaderInfo.pCode = kTransitionQuadFragmentShaderSpv;
        if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS || fragmentShader == VK_NULL_HANDLE) {
            destroyShaderModule(device, vertexShader, nullptr);
            return false;
        }

        VkPipelineShaderStageCreateInfo shaderStages[2] {};
        shaderStages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        shaderStages[0].module = vertexShader;
        shaderStages[0].pName = "main";
        shaderStages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        shaderStages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        shaderStages[1].module = fragmentShader;
        shaderStages[1].pName = "main";

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

        VkPipelineInputAssemblyStateCreateInfo inputAssemblyInfo {};
        inputAssemblyInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        inputAssemblyInfo.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo viewportStateInfo {};
        viewportStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        viewportStateInfo.viewportCount = 1;
        viewportStateInfo.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rasterizationInfo {};
        rasterizationInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rasterizationInfo.polygonMode = VK_POLYGON_MODE_FILL;
        rasterizationInfo.cullMode = VK_CULL_MODE_NONE;
        rasterizationInfo.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rasterizationInfo.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo multisampleInfo {};
        multisampleInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        multisampleInfo.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_SRC_ALPHA;
        colorBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        colorBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        colorBlendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
        colorBlendAttachment.colorWriteMask =
            VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;

        VkPipelineColorBlendStateCreateInfo colorBlendInfo {};
        colorBlendInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        colorBlendInfo.attachmentCount = 1;
        colorBlendInfo.pAttachments = &colorBlendAttachment;

        VkDynamicState dynamicStates[] = {
            VK_DYNAMIC_STATE_VIEWPORT,
            VK_DYNAMIC_STATE_SCISSOR
        };
        VkPipelineDynamicStateCreateInfo dynamicStateInfo {};
        dynamicStateInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dynamicStateInfo.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
        dynamicStateInfo.pDynamicStates = dynamicStates;

        VkGraphicsPipelineCreateInfo pipelineInfo {};
        pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        pipelineInfo.stageCount = static_cast<uint32_t>(std::size(shaderStages));
        pipelineInfo.pStages = shaderStages;
        pipelineInfo.pVertexInputState = &vertexInputInfo;
        pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
        pipelineInfo.pViewportState = &viewportStateInfo;
        pipelineInfo.pRasterizationState = &rasterizationInfo;
        pipelineInfo.pMultisampleState = &multisampleInfo;
        pipelineInfo.pColorBlendState = &colorBlendInfo;
        pipelineInfo.pDynamicState = &dynamicStateInfo;
        pipelineInfo.layout = transitionPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &transitionPipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || transitionPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }

    imageViews.resize(images.size(), VK_NULL_HANDLE);
    framebuffers.resize(images.size(), VK_NULL_HANDLE);
    for (size_t index = 0; index < images.size(); ++index) {
        if (imageViews[index] == VK_NULL_HANDLE) {
            VkImageViewCreateInfo viewInfo {};
            viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
            viewInfo.image = images[index];
            viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
            viewInfo.format = format;
            viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            viewInfo.subresourceRange.levelCount = 1;
            viewInfo.subresourceRange.layerCount = 1;
            if (createImageView(device, &viewInfo, nullptr, &imageViews[index]) != VK_SUCCESS || imageViews[index] == VK_NULL_HANDLE) {
                VK_LOG("[Vulkan] EnsureGraphicsResources: createImageView[%zu] failed", index);
                return false;
            }
        }

        if (framebuffers[index] == VK_NULL_HANDLE) {
            VkFramebufferCreateInfo framebufferInfo {};
            framebufferInfo.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
            framebufferInfo.renderPass = frameRenderPass;
            framebufferInfo.attachmentCount = 1;
            framebufferInfo.pAttachments = &imageViews[index];
            framebufferInfo.width = extent.width;
            framebufferInfo.height = extent.height;
            framebufferInfo.layers = 1;
            if (createFramebuffer(device, &framebufferInfo, nullptr, &framebuffers[index]) != VK_SUCCESS || framebuffers[index] == VK_NULL_HANDLE) {
                VK_LOG("[Vulkan] EnsureGraphicsResources: createFramebuffer[%zu] failed", index);
                return false;
            }
        }
    }

    if (!UpdateFrameDescriptorSet()) {
        VK_LOG("[Vulkan] EnsureGraphicsResources: UpdateFrameDescriptorSet failed");
        return false;
    }
    return transitionImageViews[0] == VK_NULL_HANDLE ? true : UpdateTransitionDescriptorSet();
}

uint32_t VulkanRenderTarget::Impl::FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags requiredProperties) const
{
    VkPhysicalDeviceMemoryProperties memoryProperties {};
    getPhysicalDeviceMemoryProperties(physicalDevice, &memoryProperties);

    for (uint32_t index = 0; index < memoryProperties.memoryTypeCount; ++index) {
        const bool typeMatches = (typeFilter & (1u << index)) != 0;
        const bool propertyMatches = (memoryProperties.memoryTypes[index].propertyFlags & requiredProperties) == requiredProperties;
        if (typeMatches && propertyMatches) {
            return index;
        }
    }

    return VK_QUEUE_FAMILY_IGNORED;
}

bool VulkanRenderTarget::Impl::UpdateFrameDescriptorSet()
{
    if (frameDescriptorSet == VK_NULL_HANDLE || uploadImageView == VK_NULL_HANDLE || frameSampler == VK_NULL_HANDLE) {
        // Return true if resources aren't ready yet (they'll be updated later).
        // Only return false if the descriptor set exists but is in a broken state
        // (has descriptor set + sampler but somehow lost the image view after it was set).
        return uploadImageView == VK_NULL_HANDLE || frameDescriptorSet == VK_NULL_HANDLE;
    }

    VkDescriptorImageInfo sampledImageInfo {};
    sampledImageInfo.imageView = uploadImageView;
    sampledImageInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    VkDescriptorImageInfo samplerInfo {};
    samplerInfo.sampler = frameSampler;

    VkWriteDescriptorSet writes[2] {};
    writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[0].dstSet = frameDescriptorSet;
    writes[0].dstBinding = 0;
    writes[0].descriptorCount = 1;
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    writes[0].pImageInfo = &sampledImageInfo;
    writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[1].dstSet = frameDescriptorSet;
    writes[1].dstBinding = 1;
    writes[1].descriptorCount = 1;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
    writes[1].pImageInfo = &samplerInfo;
    updateDescriptorSets(device, static_cast<uint32_t>(std::size(writes)), writes, 0, nullptr);
    return true;
}

bool VulkanRenderTarget::Impl::UpdateTransitionDescriptorSet()
{
    if (transitionDescriptorSet == VK_NULL_HANDLE ||
        transitionImageViews[0] == VK_NULL_HANDLE ||
        transitionImageViews[1] == VK_NULL_HANDLE ||
        frameSampler == VK_NULL_HANDLE) {
        return transitionDescriptorSet != VK_NULL_HANDLE ? false : true;
    }

    VkDescriptorImageInfo imageInfos[3] {};
    imageInfos[0].imageView = transitionImageViews[0];
    imageInfos[0].imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    imageInfos[1].imageView = transitionImageViews[1];
    imageInfos[1].imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    imageInfos[2].sampler = frameSampler;

    VkWriteDescriptorSet writes[3] {};
    for (uint32_t index = 0; index < 3; ++index) {
        writes[index].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        writes[index].dstSet = transitionDescriptorSet;
        writes[index].dstBinding = index;
        writes[index].descriptorCount = 1;
        writes[index].pImageInfo = &imageInfos[index];
    }
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    writes[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
    updateDescriptorSets(device, static_cast<uint32_t>(std::size(writes)), writes, 0, nullptr);
    return true;
}

bool VulkanRenderTarget::Impl::EnsureStagingCapacity(VkDeviceSize requiredSize)
{
    if (requiredSize == 0 || !createBuffer) {
        return false;
    }

    if (stagingBuffer != VK_NULL_HANDLE && mappedPixelCapacity >= requiredSize) {
        return true;
    }

    if (mappedPixels && unmapMemory && device != VK_NULL_HANDLE && stagingMemory != VK_NULL_HANDLE) {
        unmapMemory(device, stagingMemory);
        mappedPixels = nullptr;
    }
    if (destroyBuffer && device != VK_NULL_HANDLE && stagingBuffer != VK_NULL_HANDLE) {
        destroyBuffer(device, stagingBuffer, nullptr);
        stagingBuffer = VK_NULL_HANDLE;
    }
    if (freeMemory && device != VK_NULL_HANDLE && stagingMemory != VK_NULL_HANDLE) {
        freeMemory(device, stagingMemory, nullptr);
        stagingMemory = VK_NULL_HANDLE;
    }
    mappedPixelCapacity = 0;

    VkBufferCreateInfo bufferInfo {};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = requiredSize;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (createBuffer(device, &bufferInfo, nullptr, &stagingBuffer) != VK_SUCCESS || stagingBuffer == VK_NULL_HANDLE) {
        return false;
    }

    VkMemoryRequirements memoryRequirements {};
    getBufferMemoryRequirements(device, stagingBuffer, &memoryRequirements);

    const uint32_t memoryTypeIndex = FindMemoryType(
        memoryRequirements.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    if (memoryTypeIndex == VK_QUEUE_FAMILY_IGNORED) {
        return false;
    }

    VkMemoryAllocateInfo allocateInfo {};
    allocateInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocateInfo.allocationSize = memoryRequirements.size;
    allocateInfo.memoryTypeIndex = memoryTypeIndex;
    if (allocateMemory(device, &allocateInfo, nullptr, &stagingMemory) != VK_SUCCESS || stagingMemory == VK_NULL_HANDLE) {
        return false;
    }

    if (bindBufferMemory(device, stagingBuffer, stagingMemory, 0) != VK_SUCCESS) {
        return false;
    }

    if (mapMemory(device, stagingMemory, 0, VK_WHOLE_SIZE, 0, &mappedPixels) != VK_SUCCESS || !mappedPixels) {
        return false;
    }

    mappedPixelCapacity = requiredSize;
    return true;
}

void VulkanRenderTarget::Impl::DestroyUploadImage()
{
    if (device == VK_NULL_HANDLE) {
        uploadImage = VK_NULL_HANDLE;
        uploadImageMemory = VK_NULL_HANDLE;
        uploadImageView = VK_NULL_HANDLE;
        uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        uploadWidth = 0;
        uploadHeight = 0;
        return;
    }

    if (destroyImageView && uploadImageView != VK_NULL_HANDLE) {
        destroyImageView(device, uploadImageView, nullptr);
    }
    if (destroyImage && uploadImage != VK_NULL_HANDLE) {
        destroyImage(device, uploadImage, nullptr);
    }
    if (freeMemory && uploadImageMemory != VK_NULL_HANDLE) {
        freeMemory(device, uploadImageMemory, nullptr);
    }

    uploadImage = VK_NULL_HANDLE;
    uploadImageMemory = VK_NULL_HANDLE;
    uploadImageView = VK_NULL_HANDLE;
    uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uploadWidth = 0;
    uploadHeight = 0;
}

void VulkanRenderTarget::Impl::DestroyTransitionImages()
{
    if (device == VK_NULL_HANDLE) {
        transitionImages[0] = transitionImages[1] = VK_NULL_HANDLE;
        transitionImageMemory[0] = transitionImageMemory[1] = VK_NULL_HANDLE;
        transitionImageViews[0] = transitionImageViews[1] = VK_NULL_HANDLE;
        transitionImageLayouts[0] = transitionImageLayouts[1] = VK_IMAGE_LAYOUT_UNDEFINED;
        transitionWidth = 0;
        transitionHeight = 0;
        return;
    }

    for (int index = 0; index < 2; ++index) {
        if (destroyImageView && transitionImageViews[index] != VK_NULL_HANDLE) {
            destroyImageView(device, transitionImageViews[index], nullptr);
        }
        if (destroyImage && transitionImages[index] != VK_NULL_HANDLE) {
            destroyImage(device, transitionImages[index], nullptr);
        }
        if (freeMemory && transitionImageMemory[index] != VK_NULL_HANDLE) {
            freeMemory(device, transitionImageMemory[index], nullptr);
        }
        transitionImages[index] = VK_NULL_HANDLE;
        transitionImageMemory[index] = VK_NULL_HANDLE;
        transitionImageViews[index] = VK_NULL_HANDLE;
        transitionImageLayouts[index] = VK_IMAGE_LAYOUT_UNDEFINED;
    }

    transitionWidth = 0;
    transitionHeight = 0;
}

void VulkanRenderTarget::Impl::DestroyGraphicsResources()
{
    if (device == VK_NULL_HANDLE) {
        imageViews.clear();
        framebuffers.clear();
        solidRectPipeline = VK_NULL_HANDLE;
        clearRectPipeline = VK_NULL_HANDLE;
        solidRectPipelineLayout = VK_NULL_HANDLE;
        bitmapPipeline = VK_NULL_HANDLE;
        bitmapPipelineLayout = VK_NULL_HANDLE;
        blurPipeline = VK_NULL_HANDLE;
        blurPipelineLayout = VK_NULL_HANDLE;
        liquidGlassPipeline = VK_NULL_HANDLE;
        liquidGlassPipelineLayout = VK_NULL_HANDLE;
        backdropPipeline = VK_NULL_HANDLE;
        backdropPipelineLayout = VK_NULL_HANDLE;
        glowPipeline = VK_NULL_HANDLE;
        glowPipelineLayout = VK_NULL_HANDLE;
        triangleFillPipeline = VK_NULL_HANDLE;
        triangleFillPipelineLayout = VK_NULL_HANDLE;
        transitionPipeline = VK_NULL_HANDLE;
        transitionPipelineLayout = VK_NULL_HANDLE;
        transitionDescriptorSet = VK_NULL_HANDLE;
        transitionDescriptorPool = VK_NULL_HANDLE;
        transitionDescriptorSetLayout = VK_NULL_HANDLE;
        framePipeline = VK_NULL_HANDLE;
        frameRenderPass = VK_NULL_HANDLE;
        framePipelineLayout = VK_NULL_HANDLE;
        frameDescriptorSet = VK_NULL_HANDLE;
        frameDescriptorPool = VK_NULL_HANDLE;
        frameDescriptorSetLayout = VK_NULL_HANDLE;
        frameSampler = VK_NULL_HANDLE;
        return;
    }

    for (auto framebuffer : framebuffers) {
        if (destroyFramebuffer && framebuffer != VK_NULL_HANDLE) {
            destroyFramebuffer(device, framebuffer, nullptr);
        }
    }
    framebuffers.clear();

    for (auto imageView : imageViews) {
        if (destroyImageView && imageView != VK_NULL_HANDLE) {
            destroyImageView(device, imageView, nullptr);
        }
    }
    imageViews.clear();

    if (destroyPipeline && framePipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, framePipeline, nullptr);
    }
    if (destroyPipeline && solidRectPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, solidRectPipeline, nullptr);
    }
    if (destroyPipeline && clearRectPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, clearRectPipeline, nullptr);
    }
    if (destroyPipeline && bitmapPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, bitmapPipeline, nullptr);
    }
    if (destroyPipeline && blurPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, blurPipeline, nullptr);
    }
    if (destroyPipeline && liquidGlassPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, liquidGlassPipeline, nullptr);
    }
    if (destroyPipeline && backdropPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, backdropPipeline, nullptr);
    }
    if (destroyPipeline && glowPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, glowPipeline, nullptr);
    }
    if (destroyPipeline && triangleFillPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, triangleFillPipeline, nullptr);
    }
    if (destroyPipeline && transitionPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, transitionPipeline, nullptr);
    }
    if (destroyRenderPass && frameRenderPass != VK_NULL_HANDLE) {
        destroyRenderPass(device, frameRenderPass, nullptr);
    }
    if (destroyPipelineLayout && solidRectPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, solidRectPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && bitmapPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, bitmapPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && blurPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, blurPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && liquidGlassPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, liquidGlassPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && backdropPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, backdropPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && glowPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, glowPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && triangleFillPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, triangleFillPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && transitionPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, transitionPipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && framePipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, framePipelineLayout, nullptr);
    }
    if (destroyDescriptorPool && transitionDescriptorPool != VK_NULL_HANDLE) {
        destroyDescriptorPool(device, transitionDescriptorPool, nullptr);
    }
    if (destroyDescriptorPool && frameDescriptorPool != VK_NULL_HANDLE) {
        destroyDescriptorPool(device, frameDescriptorPool, nullptr);
    }
    if (destroyDescriptorSetLayout && transitionDescriptorSetLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, transitionDescriptorSetLayout, nullptr);
    }
    if (destroyDescriptorSetLayout && frameDescriptorSetLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, frameDescriptorSetLayout, nullptr);
    }
    if (destroySampler && frameSampler != VK_NULL_HANDLE) {
        destroySampler(device, frameSampler, nullptr);
    }

    solidRectPipeline = VK_NULL_HANDLE;
    clearRectPipeline = VK_NULL_HANDLE;
    solidRectPipelineLayout = VK_NULL_HANDLE;
    bitmapPipeline = VK_NULL_HANDLE;
    bitmapPipelineLayout = VK_NULL_HANDLE;
    blurPipeline = VK_NULL_HANDLE;
    blurPipelineLayout = VK_NULL_HANDLE;
    liquidGlassPipeline = VK_NULL_HANDLE;
    liquidGlassPipelineLayout = VK_NULL_HANDLE;
    backdropPipeline = VK_NULL_HANDLE;
    backdropPipelineLayout = VK_NULL_HANDLE;
    glowPipeline = VK_NULL_HANDLE;
    glowPipelineLayout = VK_NULL_HANDLE;
    triangleFillPipeline = VK_NULL_HANDLE;
    triangleFillPipelineLayout = VK_NULL_HANDLE;
    transitionPipeline = VK_NULL_HANDLE;
    transitionPipelineLayout = VK_NULL_HANDLE;
    transitionDescriptorSet = VK_NULL_HANDLE;
    transitionDescriptorPool = VK_NULL_HANDLE;
    transitionDescriptorSetLayout = VK_NULL_HANDLE;
    framePipeline = VK_NULL_HANDLE;
    frameRenderPass = VK_NULL_HANDLE;
    framePipelineLayout = VK_NULL_HANDLE;
    frameDescriptorSet = VK_NULL_HANDLE;
    frameDescriptorPool = VK_NULL_HANDLE;
    frameDescriptorSetLayout = VK_NULL_HANDLE;
    frameSampler = VK_NULL_HANDLE;
}

bool VulkanRenderTarget::Impl::EnsureStagingBuffer(uint32_t width, uint32_t height)
{
    const VkDeviceSize requiredSize = static_cast<VkDeviceSize>(width) * static_cast<VkDeviceSize>(height) * 4u;
    return EnsureStagingCapacity(requiredSize);
}

void VulkanRenderTarget::Impl::BeginFrame()
{
    auto& s = perFrameStates_[currentFrame_];
    commandBuffer = s.commandBuffer;
    inFlight = s.inFlight;
    imageAvailable = s.imageAvailable;
    stagingBuffer = s.stagingBuffer;
    stagingMemory = s.stagingMemory;
    mappedPixels = s.mappedPixels;
    mappedPixelCapacity = s.mappedPixelCapacity;
    uploadImage = s.uploadImage;
    uploadImageMemory = s.uploadImageMemory;
    uploadImageView = s.uploadImageView;
    uploadImageLayout = s.uploadImageLayout;
    uploadWidth = s.uploadWidth;
    uploadHeight = s.uploadHeight;
    frameDescriptorSet = s.frameDescriptorSet;
    submitted = s.submitted;
}

void VulkanRenderTarget::Impl::CommitCurrentFrame()
{
    auto& s = perFrameStates_[currentFrame_];
    s.commandBuffer = commandBuffer;
    s.inFlight = inFlight;
    s.imageAvailable = imageAvailable;
    s.stagingBuffer = stagingBuffer;
    s.stagingMemory = stagingMemory;
    s.mappedPixels = mappedPixels;
    s.mappedPixelCapacity = mappedPixelCapacity;
    s.uploadImage = uploadImage;
    s.uploadImageMemory = uploadImageMemory;
    s.uploadImageView = uploadImageView;
    s.uploadImageLayout = uploadImageLayout;
    s.uploadWidth = uploadWidth;
    s.uploadHeight = uploadHeight;
    s.frameDescriptorSet = frameDescriptorSet;
    s.submitted = submitted;
}

void VulkanRenderTarget::Impl::EndFrame()
{
    CommitCurrentFrame();
    currentFrame_ = (currentFrame_ + 1) % MAX_FRAMES_IN_FLIGHT;
    BeginFrame();
}

void VulkanRenderTarget::Impl::DestroyPerFrameResources()
{
    if (!device) {
        return;
    }
    // Commit any currently-aliased state back into its slot so we free the latest
    // pointers rather than stale ones.
    CommitCurrentFrame();

    for (uint32_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        auto& s = perFrameStates_[i];
        if (s.uploadImageView != VK_NULL_HANDLE && destroyImageView) {
            destroyImageView(device, s.uploadImageView, nullptr);
        }
        if (s.uploadImage != VK_NULL_HANDLE && destroyImage) {
            destroyImage(device, s.uploadImage, nullptr);
        }
        if (s.uploadImageMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.uploadImageMemory, nullptr);
        }
        if (s.mappedPixels != nullptr && s.stagingMemory != VK_NULL_HANDLE && unmapMemory) {
            unmapMemory(device, s.stagingMemory);
        }
        if (s.stagingBuffer != VK_NULL_HANDLE && destroyBuffer) {
            destroyBuffer(device, s.stagingBuffer, nullptr);
        }
        if (s.stagingMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.stagingMemory, nullptr);
        }
        if (s.inFlight != VK_NULL_HANDLE && destroyFence) {
            destroyFence(device, s.inFlight, nullptr);
        }
        if (s.imageAvailable != VK_NULL_HANDLE && destroySemaphore) {
            destroySemaphore(device, s.imageAvailable, nullptr);
        }
        // commandBuffer is freed implicitly by destroying the command pool.
        // frameDescriptorSet is freed implicitly by destroying the descriptor pool.
        s = PerFrameState{};
    }

    for (VkSemaphore sem : renderFinishedPerImage) {
        if (sem != VK_NULL_HANDLE && destroySemaphore) {
            destroySemaphore(device, sem, nullptr);
        }
    }
    renderFinishedPerImage.clear();

    // Clear aliases now that everything they point to has been released.
    commandBuffer = VK_NULL_HANDLE;
    inFlight = VK_NULL_HANDLE;
    imageAvailable = VK_NULL_HANDLE;
    stagingBuffer = VK_NULL_HANDLE;
    stagingMemory = VK_NULL_HANDLE;
    mappedPixels = nullptr;
    mappedPixelCapacity = 0;
    uploadImage = VK_NULL_HANDLE;
    uploadImageMemory = VK_NULL_HANDLE;
    uploadImageView = VK_NULL_HANDLE;
    uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uploadWidth = 0;
    uploadHeight = 0;
    frameDescriptorSet = VK_NULL_HANDLE;
    submitted = false;
    currentFrame_ = 0;
}

bool VulkanRenderTarget::Impl::DrawFrame(const uint8_t* pixels, uint32_t width, uint32_t height)
{
    BeginFrame();
    if (!device || !swapchain || !commandBuffer || !pixels || width == 0 || height == 0) {
        VK_LOG("[Vulkan] DrawFrame: precondition failed (device=%p swapchain=%p cmdBuf=%p pixels=%p w=%u h=%u)\n",
                (void*)device, (void*)swapchain, (void*)commandBuffer, (const void*)pixels, width, height);
        EndFrame();
        return false;
    }

    // Wait for this slot's previous submission to complete before we reuse any of
    // its resources. Fence was created SIGNALED so the very first call falls
    // through immediately. Two frames in flight means this waits at most 1 frame,
    // letting the CPU work of frame N overlap with GPU work of frame N-1.
    if (waitForFences(device, 1, &inFlight, VK_TRUE, std::numeric_limits<uint64_t>::max()) != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: waitForFences failed\n");
        EndFrame();
        return false;
    }

    if (!EnsureStagingBuffer(width, height)) {
        VK_LOG("[Vulkan] DrawFrame: EnsureStagingBuffer failed\n");
        EndFrame();
        return false;
    }
    if (!EnsureUploadImage(width, height)) {
        VK_LOG("[Vulkan] DrawFrame: EnsureUploadImage failed\n");
        EndFrame();
        return false;
    }
    if (!EnsureGraphicsResources()) {
        VK_LOG("[Vulkan] DrawFrame: EnsureGraphicsResources failed\n");
        EndFrame();
        return false;
    }

    std::memcpy(mappedPixels, pixels, static_cast<size_t>(width) * static_cast<size_t>(height) * 4u);

    uint32_t imageIndex = 0;
    const VkResult acquireResult = acquireNextImage(device, swapchain, std::numeric_limits<uint64_t>::max(), imageAvailable, VK_NULL_HANDLE, &imageIndex);
    if (acquireResult == VK_ERROR_OUT_OF_DATE_KHR) {
        EndFrame();
        return true;
    }
    if (acquireResult != VK_SUCCESS && acquireResult != VK_SUBOPTIMAL_KHR) {
        VK_LOG("[Vulkan] DrawFrame: acquireNextImage failed (%d)\n", static_cast<int>(acquireResult));
        EndFrame();
        return false;
    }

    if (resetFences(device, 1, &inFlight) != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: resetFences failed\n");
        EndFrame();
        return false;
    }

    if (resetCommandBuffer(commandBuffer, 0) != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: resetCommandBuffer failed\n");
        EndFrame();
        return false;
    }

    VkCommandBufferBeginInfo beginInfo {};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (beginCommandBuffer(commandBuffer, &beginInfo) != VK_SUCCESS) {
        return false;
    }

    VkImageSubresourceRange subresourceRange {};
    subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    subresourceRange.levelCount = 1;
    subresourceRange.layerCount = 1;

    VkImageMemoryBarrier uploadToTransfer {};
    uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    uploadToTransfer.oldLayout = uploadImageLayout;
    uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToTransfer.image = uploadImage;
    uploadToTransfer.subresourceRange = subresourceRange;
    uploadToTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
        ? VK_ACCESS_SHADER_READ_BIT
        : 0;
    uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
        ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
        : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
    cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

    VkBufferImageCopy bufferImageCopy {};
    bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    bufferImageCopy.imageSubresource.layerCount = 1;
    bufferImageCopy.imageExtent.width = width;
    bufferImageCopy.imageExtent.height = height;
    bufferImageCopy.imageExtent.depth = 1;
    cmdCopyBufferToImage(
        commandBuffer,
        stagingBuffer,
        uploadImage,
        VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
        1,
        &bufferImageCopy);

    VkImageMemoryBarrier uploadToShaderRead {};
    uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToShaderRead.image = uploadImage;
    uploadToShaderRead.subresourceRange = subresourceRange;
    uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);

    VkImageMemoryBarrier toColorAttachment {};
    toColorAttachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toColorAttachment.oldLayout = imageLayouts[imageIndex];
    toColorAttachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    toColorAttachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.image = images[imageIndex];
    toColorAttachment.subresourceRange = subresourceRange;
    toColorAttachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    const VkPipelineStageFlags colorSrcStage = imageLayouts[imageIndex] == VK_IMAGE_LAYOUT_UNDEFINED
        ? VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT
        : VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    cmdPipelineBarrier(commandBuffer, colorSrcStage, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, 0, 0, nullptr, 0, nullptr, 1, &toColorAttachment);

    const VkClearValue clearValue = { { 0.0f, 0.0f, 0.0f, 0.0f } };
    VkRenderPassBeginInfo renderPassInfo {};
    renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    renderPassInfo.renderPass = frameRenderPass;
    renderPassInfo.framebuffer = framebuffers[imageIndex];
    renderPassInfo.renderArea.extent = extent;
    renderPassInfo.clearValueCount = 1;
    renderPassInfo.pClearValues = &clearValue;
    cmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);

    VkViewport viewport {};
    viewport.x = 0.0f;
    viewport.y = 0.0f;
    viewport.width = static_cast<float>(extent.width);
    viewport.height = static_cast<float>(extent.height);
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;
    cmdSetViewport(commandBuffer, 0, 1, &viewport);

    VkRect2D scissor {};
    scissor.extent = extent;
    cmdSetScissor(commandBuffer, 0, 1, &scissor);

    cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, framePipeline);
    cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, framePipelineLayout, 0, 1, &frameDescriptorSet, 0, nullptr);
    cmdDraw(commandBuffer, 3, 1, 0, 0);
    cmdEndRenderPass(commandBuffer);

    VkImageMemoryBarrier toPresent {};
    toPresent.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toPresent.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    toPresent.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    toPresent.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    toPresent.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toPresent.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toPresent.image = images[imageIndex];
    toPresent.subresourceRange = subresourceRange;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0, nullptr, 0, nullptr, 1, &toPresent);

    if (endCommandBuffer(commandBuffer) != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: endCommandBuffer failed\n");
        return false;
    }

    uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    const VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    VkSemaphore signalSemaphore = (imageIndex < renderFinishedPerImage.size())
        ? renderFinishedPerImage[imageIndex]
        : VK_NULL_HANDLE;
    if (signalSemaphore == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] DrawFrame: missing renderFinishedPerImage[%u]\n", imageIndex);
        EndFrame();
        return false;
    }
    VkSubmitInfo submitInfo {};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.waitSemaphoreCount = 1;
    submitInfo.pWaitSemaphores = &imageAvailable;
    submitInfo.pWaitDstStageMask = &waitStage;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &commandBuffer;
    submitInfo.signalSemaphoreCount = 1;
    submitInfo.pSignalSemaphores = &signalSemaphore;
    VkResult submitResult = queueSubmit(queue, 1, &submitInfo, inFlight);
    if (submitResult != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: queueSubmit failed (%d)\n", static_cast<int>(submitResult));
        EndFrame();
        return false;
    }

    VkPresentInfoKHR presentInfo {};
    presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores = &signalSemaphore;
    presentInfo.swapchainCount = 1;
    presentInfo.pSwapchains = &swapchain;
    presentInfo.pImageIndices = &imageIndex;
    const VkResult presentResult = queuePresent(queue, &presentInfo);
    if (presentResult != VK_SUCCESS && presentResult != VK_SUBOPTIMAL_KHR && presentResult != VK_ERROR_OUT_OF_DATE_KHR) {
        VK_LOG("[Vulkan] DrawFrame: queuePresent failed (%d)\n", static_cast<int>(presentResult));
        EndFrame();
        return false;
    }

    imageLayouts[imageIndex] = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    submitted = true;
    EndFrame();
    return true;
}

bool VulkanRenderTarget::Impl::DrawReplayFrame(const std::vector<VulkanRenderTarget::GpuReplayCommand>& commands, const float clearColor[4])
{
    BeginFrame();
    if (!device || !swapchain || !commandBuffer) {
        VK_LOG("[Vulkan] DrawReplayFrame: basic precondition failed (device=%p swapchain=%p cmdBuf=%p)",
               (void*)device, (void*)swapchain, (void*)commandBuffer);
        EndFrame();
        return false;
    }
    // Wait for this slot's previous submission before touching its command buffer
    // or per-frame resources. Fence is SIGNALED-initialized so the first call
    // returns immediately.
    if (waitForFences(device, 1, &inFlight, VK_TRUE, std::numeric_limits<uint64_t>::max()) != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawReplayFrame: waitForFences failed");
        EndFrame();
        return false;
    }
    if (!EnsureGraphicsResources()) {
        VK_LOG("[Vulkan] DrawReplayFrame: EnsureGraphicsResources failed");
        EndFrame();
        return false;
    }
    if (solidRectPipeline == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] DrawReplayFrame: solidRectPipeline is null");
        EndFrame();
        return false;
    }

    std::vector<VkDeviceSize> bitmapOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> backdropOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> liquidGlassOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> blurOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> polygonOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> transitionFromOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> transitionToOffsets(commands.size(), 0);
    uint32_t maxBitmapWidth = 0;
    uint32_t maxBitmapHeight = 0;
    uint32_t maxBackdropWidth = 0;
    uint32_t maxBackdropHeight = 0;
    uint32_t maxLiquidGlassWidth = 0;
    uint32_t maxLiquidGlassHeight = 0;
    uint32_t maxBlurWidth = 0;
    uint32_t maxBlurHeight = 0;
    uint32_t maxTransitionWidth = 0;
    uint32_t maxTransitionHeight = 0;
    VkDeviceSize totalBitmapBytes = 0;
    VkDeviceSize totalBackdropBytes = 0;
    VkDeviceSize totalLiquidGlassBytes = 0;
    VkDeviceSize totalBlurBytes = 0;
    VkDeviceSize totalPolygonBytes = 0;
    VkDeviceSize totalTransitionBytes = 0;
    bool hasBitmapCommands = false;
    bool hasBackdropCommands = false;
    bool hasLiquidGlassCommands = false;
    bool hasBlurCommands = false;
    bool hasPolygonCommands = false;
    bool hasTransitionCommands = false;

    for (size_t index = 0; index < commands.size(); ++index) {
        const auto& command = commands[index];
        if (command.kind == GpuReplayCommandKind::Bitmap) {
            const auto& bmPixels = command.bitmap.GetPixels();
            if (command.bitmap.pixelWidth == 0 || command.bitmap.pixelHeight == 0 || bmPixels.empty()) {
                return false;
            }

            const VkDeviceSize bitmapBytes =
                static_cast<VkDeviceSize>(command.bitmap.pixelWidth) * static_cast<VkDeviceSize>(command.bitmap.pixelHeight) * 4u;
            bitmapOffsets[index] = totalBitmapBytes;
            totalBitmapBytes += bitmapBytes;
            maxBitmapWidth = std::max(maxBitmapWidth, command.bitmap.pixelWidth);
            maxBitmapHeight = std::max(maxBitmapHeight, command.bitmap.pixelHeight);
            hasBitmapCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Backdrop) {
            const size_t expectedSize = static_cast<size_t>(command.backdrop.pixelWidth) * static_cast<size_t>(command.backdrop.pixelHeight) * 4u;
            if (command.backdrop.pixelWidth == 0 || command.backdrop.pixelHeight == 0 || command.backdrop.pixels.size() != expectedSize) {
                return false;
            }

            const VkDeviceSize bytes = static_cast<VkDeviceSize>(expectedSize);
            backdropOffsets[index] = totalBackdropBytes;
            totalBackdropBytes += bytes;
            maxBackdropWidth = std::max(maxBackdropWidth, command.backdrop.pixelWidth);
            maxBackdropHeight = std::max(maxBackdropHeight, command.backdrop.pixelHeight);
            hasBackdropCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::LiquidGlass) {
            const size_t expectedSize = static_cast<size_t>(command.liquidGlass.pixelWidth) * static_cast<size_t>(command.liquidGlass.pixelHeight) * 4u;
            if (command.liquidGlass.pixelWidth == 0 || command.liquidGlass.pixelHeight == 0 || command.liquidGlass.pixels.size() != expectedSize) {
                return false;
            }

            const VkDeviceSize bytes = static_cast<VkDeviceSize>(expectedSize);
            liquidGlassOffsets[index] = totalLiquidGlassBytes;
            totalLiquidGlassBytes += bytes;
            maxLiquidGlassWidth = std::max(maxLiquidGlassWidth, command.liquidGlass.pixelWidth);
            maxLiquidGlassHeight = std::max(maxLiquidGlassHeight, command.liquidGlass.pixelHeight);
            hasLiquidGlassCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Blur) {
            const size_t expectedSize = static_cast<size_t>(command.blur.pixelWidth) * static_cast<size_t>(command.blur.pixelHeight) * 4u;
            if (command.blur.pixelWidth == 0 || command.blur.pixelHeight == 0 || command.blur.pixels.size() != expectedSize) {
                return false;
            }

            const VkDeviceSize blurBytes = static_cast<VkDeviceSize>(expectedSize);
            blurOffsets[index] = totalBlurBytes;
            totalBlurBytes += blurBytes;
            maxBlurWidth = std::max(maxBlurWidth, command.blur.pixelWidth);
            maxBlurHeight = std::max(maxBlurHeight, command.blur.pixelHeight);
            hasBlurCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::FilledPolygon) {
            if (command.filledPolygon.triangleVertices.size() < 6 || (command.filledPolygon.triangleVertices.size() % 2) != 0) {
                return false;
            }

            polygonOffsets[index] = totalPolygonBytes;
            totalPolygonBytes += static_cast<VkDeviceSize>(command.filledPolygon.triangleVertices.size() * sizeof(float));
            hasPolygonCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Transition) {
            const size_t expectedSize = static_cast<size_t>(command.transition.pixelWidth) * static_cast<size_t>(command.transition.pixelHeight) * 4u;
            if (command.transition.pixelWidth == 0 || command.transition.pixelHeight == 0 ||
                command.transition.fromPixels.size() != expectedSize ||
                command.transition.toPixels.size() != expectedSize) {
                return false;
            }

            const VkDeviceSize transitionBytes = static_cast<VkDeviceSize>(expectedSize);
            transitionFromOffsets[index] = totalTransitionBytes;
            totalTransitionBytes += transitionBytes;
            transitionToOffsets[index] = totalTransitionBytes;
            totalTransitionBytes += transitionBytes;
            maxTransitionWidth = std::max(maxTransitionWidth, command.transition.pixelWidth);
            maxTransitionHeight = std::max(maxTransitionHeight, command.transition.pixelHeight);
            hasTransitionCommands = true;
        }
    }

    if (hasBitmapCommands || hasBackdropCommands || hasLiquidGlassCommands || hasBlurCommands) {
        if ((hasBitmapCommands && bitmapPipeline == VK_NULL_HANDLE) ||
            (hasBackdropCommands && backdropPipeline == VK_NULL_HANDLE) ||
            (hasLiquidGlassCommands && liquidGlassPipeline == VK_NULL_HANDLE) ||
            (hasBlurCommands && blurPipeline == VK_NULL_HANDLE) ||
            !EnsureUploadImageCapacity(
                std::max(std::max(std::max(maxBitmapWidth, maxBackdropWidth), maxBlurWidth), maxLiquidGlassWidth),
                std::max(std::max(std::max(maxBitmapHeight, maxBackdropHeight), maxBlurHeight), maxLiquidGlassHeight)) ||
            !UpdateFrameDescriptorSet() ||
            !EnsureStagingCapacity(totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + totalBlurBytes + totalPolygonBytes + totalTransitionBytes)) {
            return false;
        }
    } else if ((hasPolygonCommands || hasTransitionCommands) && !EnsureStagingCapacity(totalPolygonBytes + totalTransitionBytes)) {
        return false;
    }

    if (hasTransitionCommands) {
        if (transitionPipeline == VK_NULL_HANDLE ||
            !EnsureTransitionImagesCapacity(maxTransitionWidth, maxTransitionHeight) ||
            !UpdateTransitionDescriptorSet()) {
            return false;
        }
    }

    auto* stagingBytes = static_cast<uint8_t*>(mappedPixels);
    if ((hasBitmapCommands || hasBackdropCommands || hasLiquidGlassCommands || hasBlurCommands || hasPolygonCommands || hasTransitionCommands) && !stagingBytes) {
        return false;
    }

    for (size_t index = 0; index < commands.size(); ++index) {
        const auto& command = commands[index];
        if (command.kind == GpuReplayCommandKind::Bitmap) {
            const size_t pixelBytes = static_cast<size_t>(command.bitmap.pixelWidth) * static_cast<size_t>(command.bitmap.pixelHeight) * 4u;
            std::memcpy(stagingBytes + bitmapOffsets[index], command.bitmap.GetPixels().data(), pixelBytes);
        } else if (command.kind == GpuReplayCommandKind::Backdrop) {
            const size_t pixelBytes = static_cast<size_t>(command.backdrop.pixelWidth) * static_cast<size_t>(command.backdrop.pixelHeight) * 4u;
            std::memcpy(stagingBytes + totalBitmapBytes + backdropOffsets[index], command.backdrop.pixels.data(), pixelBytes);
        } else if (command.kind == GpuReplayCommandKind::LiquidGlass) {
            const size_t pixelBytes = static_cast<size_t>(command.liquidGlass.pixelWidth) * static_cast<size_t>(command.liquidGlass.pixelHeight) * 4u;
            std::memcpy(stagingBytes + totalBitmapBytes + totalBackdropBytes + liquidGlassOffsets[index], command.liquidGlass.pixels.data(), pixelBytes);
        } else if (command.kind == GpuReplayCommandKind::Blur) {
            const size_t pixelBytes = static_cast<size_t>(command.blur.pixelWidth) * static_cast<size_t>(command.blur.pixelHeight) * 4u;
            std::memcpy(stagingBytes + totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + blurOffsets[index], command.blur.pixels.data(), pixelBytes);
        } else if (command.kind == GpuReplayCommandKind::FilledPolygon) {
            const size_t vertexBytes = command.filledPolygon.triangleVertices.size() * sizeof(float);
            std::memcpy(stagingBytes + totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + totalBlurBytes + polygonOffsets[index], command.filledPolygon.triangleVertices.data(), vertexBytes);
        } else if (command.kind == GpuReplayCommandKind::Transition) {
            const size_t pixelBytes = static_cast<size_t>(command.transition.pixelWidth) * static_cast<size_t>(command.transition.pixelHeight) * 4u;
            const VkDeviceSize baseOffset = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + totalBlurBytes + totalPolygonBytes;
            std::memcpy(stagingBytes + baseOffset + transitionFromOffsets[index], command.transition.fromPixels.data(), pixelBytes);
            std::memcpy(stagingBytes + baseOffset + transitionToOffsets[index], command.transition.toPixels.data(), pixelBytes);
        }
    }

    uint32_t imageIndex = 0;
    const VkResult acquireResult = acquireNextImage(device, swapchain, std::numeric_limits<uint64_t>::max(), imageAvailable, VK_NULL_HANDLE, &imageIndex);
    if (acquireResult == VK_ERROR_OUT_OF_DATE_KHR) {
        EndFrame();
        return true;
    }
    if (acquireResult != VK_SUCCESS && acquireResult != VK_SUBOPTIMAL_KHR) {
        EndFrame();
        return false;
    }

    if (resetFences(device, 1, &inFlight) != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    if (resetCommandBuffer(commandBuffer, 0) != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    VkCommandBufferBeginInfo beginInfo {};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (beginCommandBuffer(commandBuffer, &beginInfo) != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    VkImageSubresourceRange subresourceRange {};
    subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    subresourceRange.levelCount = 1;
    subresourceRange.layerCount = 1;

    VkImageMemoryBarrier toClear {};
    toClear.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toClear.oldLayout = imageLayouts[imageIndex];
    toClear.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    toClear.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toClear.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toClear.image = images[imageIndex];
    toClear.subresourceRange = subresourceRange;
    toClear.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    const VkPipelineStageFlags clearSrcStage = imageLayouts[imageIndex] == VK_IMAGE_LAYOUT_UNDEFINED
        ? VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT
        : VK_PIPELINE_STAGE_ALL_COMMANDS_BIT;
    cmdPipelineBarrier(commandBuffer, clearSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &toClear);

    VkClearColorValue clearValue {};
    clearValue.float32[0] = clearColor[0];
    clearValue.float32[1] = clearColor[1];
    clearValue.float32[2] = clearColor[2];
    clearValue.float32[3] = clearColor[3];
    cmdClearColorImage(commandBuffer, images[imageIndex], VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, &clearValue, 1, &subresourceRange);

    VkImageMemoryBarrier toColorAttachment {};
    toColorAttachment.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toColorAttachment.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    toColorAttachment.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    toColorAttachment.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    toColorAttachment.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toColorAttachment.image = images[imageIndex];
    toColorAttachment.subresourceRange = subresourceRange;
    toColorAttachment.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, 0, 0, nullptr, 0, nullptr, 1, &toColorAttachment);

    VkViewport viewport {};
    viewport.x = 0.0f;
    // Negative viewport height flips Y axis to match OpenGL/D3D conventions
    // (Y=0 at top, increasing downward). Requires VK_KHR_maintenance1 or Vulkan 1.1+.
    viewport.y = static_cast<float>(extent.height);
    viewport.width = static_cast<float>(extent.width);
    viewport.height = -static_cast<float>(extent.height);
    viewport.minDepth = 0.0f;
    viewport.maxDepth = 1.0f;

    VkRect2D scissor {};
    scissor.extent = extent;

    const VkClearValue renderPassClearValue = { { 0.0f, 0.0f, 0.0f, 0.0f } };
    auto beginLoadRenderPass = [&]() {
        VkRenderPassBeginInfo renderPassInfo {};
        renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        renderPassInfo.renderPass = frameRenderPass;
        renderPassInfo.framebuffer = framebuffers[imageIndex];
        renderPassInfo.renderArea.extent = extent;
        renderPassInfo.clearValueCount = 1;
        renderPassInfo.pClearValues = &renderPassClearValue;
        cmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);
        cmdSetViewport(commandBuffer, 0, 1, &viewport);
    };

    for (size_t index = 0; index < commands.size(); ++index) {
        const auto& command = commands[index];
        VkRect2D commandScissor = scissor;
        if (command.hasScissor) {
            commandScissor.offset.x = std::max(0, command.scissorLeft);
            commandScissor.offset.y = std::max(0, command.scissorTop);
            commandScissor.extent.width = command.scissorRight > command.scissorLeft
                ? static_cast<uint32_t>(command.scissorRight - command.scissorLeft)
                : 0u;
            commandScissor.extent.height = command.scissorBottom > command.scissorTop
                ? static_cast<uint32_t>(command.scissorBottom - command.scissorTop)
                : 0u;
        }
        if (commandScissor.extent.width == 0 || commandScissor.extent.height == 0) {
            continue;
        }

        if (command.kind == GpuReplayCommandKind::SolidRect) {
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            SolidRectPushConstants pushConstants {};
            pushConstants.rect[0] = command.solidRect.x;
            pushConstants.rect[1] = command.solidRect.y;
            pushConstants.rect[2] = command.solidRect.w;
            pushConstants.rect[3] = command.solidRect.h;
            pushConstants.color[0] = command.solidRect.r;
            pushConstants.color[1] = command.solidRect.g;
            pushConstants.color[2] = command.solidRect.b;
            pushConstants.color[3] = command.solidRect.a;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasRoundedClip) {
                pushConstants.roundedClipRect[0] = command.roundedClipLeft;
                pushConstants.roundedClipRect[1] = command.roundedClipTop;
                pushConstants.roundedClipRect[2] = command.roundedClipRight;
                pushConstants.roundedClipRect[3] = command.roundedClipBottom;
                pushConstants.roundedClipRadius[0] = command.roundedClipRadiusX;
                pushConstants.roundedClipRadius[1] = command.roundedClipRadiusY;
                pushConstants.clipFlags[0] = 1.0f;
            }
            if (command.hasInnerRoundedClip) {
                pushConstants.innerRoundedClipRect[0] = command.innerRoundedClipLeft;
                pushConstants.innerRoundedClipRect[1] = command.innerRoundedClipTop;
                pushConstants.innerRoundedClipRect[2] = command.innerRoundedClipRight;
                pushConstants.innerRoundedClipRect[3] = command.innerRoundedClipBottom;
                pushConstants.innerRoundedClipRadius[0] = command.innerRoundedClipRadiusX;
                pushConstants.innerRoundedClipRadius[1] = command.innerRoundedClipRadiusY;
                pushConstants.clipFlags[1] = 1.0f;
            }
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, solidRectPipeline);
            cmdPushConstants(
                commandBuffer,
                solidRectPipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::ClearRect) {
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            SolidRectPushConstants pushConstants {};
            pushConstants.rect[0] = command.solidRect.x;
            pushConstants.rect[1] = command.solidRect.y;
            pushConstants.rect[2] = command.solidRect.w;
            pushConstants.rect[3] = command.solidRect.h;
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, clearRectPipeline);
            cmdPushConstants(
                commandBuffer,
                solidRectPipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Glow) {
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &scissor);
            GlowPushConstants pushConstants {};
            pushConstants.rect[0] = command.glow.x;
            pushConstants.rect[1] = command.glow.y;
            pushConstants.rect[2] = command.glow.w;
            pushConstants.rect[3] = command.glow.h;
            pushConstants.glowColor[0] = command.glow.glowR;
            pushConstants.glowColor[1] = command.glow.glowG;
            pushConstants.glowColor[2] = command.glow.glowB;
            pushConstants.glowColor[3] = command.glow.glowA;
            pushConstants.glowInfo1[0] = command.glow.cornerRadius;
            pushConstants.glowInfo1[1] = command.glow.strokeWidth;
            pushConstants.glowInfo1[2] = command.glow.dimOpacity;
            pushConstants.glowInfo1[3] = command.glow.intensity;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, glowPipeline);
            cmdPushConstants(commandBuffer, glowPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pushConstants), &pushConstants);
            cmdDraw(commandBuffer, 3, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::FilledPolygon) {
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            TriangleFillPushConstants pushConstants {};
            pushConstants.color[0] = command.filledPolygon.r;
            pushConstants.color[1] = command.filledPolygon.g;
            pushConstants.color[2] = command.filledPolygon.b;
            pushConstants.color[3] = command.filledPolygon.a;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasRoundedClip) {
                pushConstants.roundedClipRect[0] = command.roundedClipLeft;
                pushConstants.roundedClipRect[1] = command.roundedClipTop;
                pushConstants.roundedClipRect[2] = command.roundedClipRight;
                pushConstants.roundedClipRect[3] = command.roundedClipBottom;
                pushConstants.roundedClipRadius[0] = command.roundedClipRadiusX;
                pushConstants.roundedClipRadius[1] = command.roundedClipRadiusY;
                pushConstants.clipFlags[0] = 1.0f;
            }
            const VkDeviceSize vertexOffset = totalBitmapBytes + polygonOffsets[index];
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, triangleFillPipeline);
            cmdBindVertexBuffers(commandBuffer, 0, 1, &stagingBuffer, &vertexOffset);
            cmdPushConstants(
                commandBuffer,
                triangleFillPipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);
            cmdDraw(commandBuffer, static_cast<uint32_t>(command.filledPolygon.triangleVertices.size() / 2), 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Blur) {
            VkImageMemoryBarrier uploadToTransfer {};
            uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToTransfer.oldLayout = uploadImageLayout;
            uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.image = uploadImage;
            uploadToTransfer.subresourceRange = subresourceRange;
            uploadToTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_ACCESS_SHADER_READ_BIT : 0;
            uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
                : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
            cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

            VkBufferImageCopy bufferImageCopy {};
            bufferImageCopy.bufferOffset = totalBitmapBytes + blurOffsets[index];
            bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            bufferImageCopy.imageSubresource.layerCount = 1;
            bufferImageCopy.imageExtent.width = command.blur.pixelWidth;
            bufferImageCopy.imageExtent.height = command.blur.pixelHeight;
            bufferImageCopy.imageExtent.depth = 1;
            cmdCopyBufferToImage(commandBuffer, stagingBuffer, uploadImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &bufferImageCopy);

            VkImageMemoryBarrier uploadToShaderRead {};
            uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.image = uploadImage;
            uploadToShaderRead.subresourceRange = subresourceRange;
            uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);
            uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            BlurPushConstants pushConstants {};
            pushConstants.rect[0] = command.blur.x;
            pushConstants.rect[1] = command.blur.y;
            pushConstants.rect[2] = command.blur.w;
            pushConstants.rect[3] = command.blur.h;
            pushConstants.blurInfo1[0] = uploadWidth == 0 ? 1.0f : static_cast<float>(command.blur.pixelWidth) / static_cast<float>(uploadWidth);
            pushConstants.blurInfo1[1] = uploadHeight == 0 ? 1.0f : static_cast<float>(command.blur.pixelHeight) / static_cast<float>(uploadHeight);
            pushConstants.blurInfo1[2] = command.blur.pixelWidth == 0 ? 0.0f : 1.0f / static_cast<float>(command.blur.pixelWidth);
            pushConstants.blurInfo1[3] = command.blur.pixelHeight == 0 ? 0.0f : 1.0f / static_cast<float>(command.blur.pixelHeight);
            pushConstants.blurInfo2[0] = command.blur.radius;
            pushConstants.blurInfo2[1] = command.blur.opacity;
            pushConstants.blurInfo2[2] = command.blur.alphaOnlyTint ? 1.0f : 0.0f;
            pushConstants.blurTint[0] = command.blur.tintR;
            pushConstants.blurTint[1] = command.blur.tintG;
            pushConstants.blurTint[2] = command.blur.tintB;
            pushConstants.blurTint[3] = command.blur.tintA;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasRoundedClip) {
                pushConstants.roundedClipRect[0] = command.roundedClipLeft;
                pushConstants.roundedClipRect[1] = command.roundedClipTop;
                pushConstants.roundedClipRect[2] = command.roundedClipRight;
                pushConstants.roundedClipRect[3] = command.roundedClipBottom;
                pushConstants.roundedClipRadius[0] = command.roundedClipRadiusX;
                pushConstants.roundedClipRadius[1] = command.roundedClipRadiusY;
                pushConstants.clipFlags[0] = 1.0f;
            }
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, blurPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, blurPipelineLayout, 0, 1, &frameDescriptorSet, 0, nullptr);
            cmdPushConstants(commandBuffer, blurPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pushConstants), &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Backdrop) {
            VkImageMemoryBarrier uploadToTransfer {};
            uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToTransfer.oldLayout = uploadImageLayout;
            uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.image = uploadImage;
            uploadToTransfer.subresourceRange = subresourceRange;
            uploadToTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_ACCESS_SHADER_READ_BIT : 0;
            uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
                : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
            cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

            VkBufferImageCopy bufferImageCopy {};
            bufferImageCopy.bufferOffset = totalBitmapBytes + backdropOffsets[index];
            bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            bufferImageCopy.imageSubresource.layerCount = 1;
            bufferImageCopy.imageExtent.width = command.backdrop.pixelWidth;
            bufferImageCopy.imageExtent.height = command.backdrop.pixelHeight;
            bufferImageCopy.imageExtent.depth = 1;
            cmdCopyBufferToImage(commandBuffer, stagingBuffer, uploadImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &bufferImageCopy);

            VkImageMemoryBarrier uploadToShaderRead {};
            uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.image = uploadImage;
            uploadToShaderRead.subresourceRange = subresourceRange;
            uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);
            uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            BackdropPushConstants pushConstants {};
            pushConstants.rect[0] = command.backdrop.x;
            pushConstants.rect[1] = command.backdrop.y;
            pushConstants.rect[2] = command.backdrop.w;
            pushConstants.rect[3] = command.backdrop.h;
            pushConstants.backdropInfo1[0] = std::max(std::max(command.backdrop.cornerRadiusTL, command.backdrop.cornerRadiusTR), std::max(command.backdrop.cornerRadiusBR, command.backdrop.cornerRadiusBL));
            pushConstants.backdropInfo1[1] = command.backdrop.blurRadius;
            pushConstants.backdropInfo1[2] = command.backdrop.pixelWidth == 0 ? 0.0f : 1.0f / static_cast<float>(command.backdrop.pixelWidth);
            pushConstants.backdropInfo1[3] = command.backdrop.pixelHeight == 0 ? 0.0f : 1.0f / static_cast<float>(command.backdrop.pixelHeight);
            pushConstants.tintColor[0] = command.backdrop.tintR;
            pushConstants.tintColor[1] = command.backdrop.tintG;
            pushConstants.tintColor[2] = command.backdrop.tintB;
            pushConstants.tintColor[3] = command.backdrop.tintOpacity;
            pushConstants.extraInfo[0] = command.backdrop.saturation;
            pushConstants.extraInfo[1] = command.backdrop.noiseIntensity;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            pushConstants.cornerRadii[0] = command.backdrop.cornerRadiusTL;
            pushConstants.cornerRadii[1] = command.backdrop.cornerRadiusTR;
            pushConstants.cornerRadii[2] = command.backdrop.cornerRadiusBR;
            pushConstants.cornerRadii[3] = command.backdrop.cornerRadiusBL;
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, backdropPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, backdropPipelineLayout, 0, 1, &frameDescriptorSet, 0, nullptr);
            cmdPushConstants(commandBuffer, backdropPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pushConstants), &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::LiquidGlass) {
            VkImageMemoryBarrier uploadToTransfer {};
            uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToTransfer.oldLayout = uploadImageLayout;
            uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToTransfer.image = uploadImage;
            uploadToTransfer.subresourceRange = subresourceRange;
            uploadToTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_ACCESS_SHADER_READ_BIT : 0;
            uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
                : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
            cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

            VkBufferImageCopy bufferImageCopy {};
            bufferImageCopy.bufferOffset = totalBitmapBytes + liquidGlassOffsets[index];
            bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            bufferImageCopy.imageSubresource.layerCount = 1;
            bufferImageCopy.imageExtent.width = command.liquidGlass.pixelWidth;
            bufferImageCopy.imageExtent.height = command.liquidGlass.pixelHeight;
            bufferImageCopy.imageExtent.depth = 1;
            cmdCopyBufferToImage(commandBuffer, stagingBuffer, uploadImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &bufferImageCopy);

            VkImageMemoryBarrier uploadToShaderRead {};
            uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            uploadToShaderRead.image = uploadImage;
            uploadToShaderRead.subresourceRange = subresourceRange;
            uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);
            uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            LiquidGlassPushConstants pushConstants {};
            pushConstants.rect[0] = command.liquidGlass.x;
            pushConstants.rect[1] = command.liquidGlass.y;
            pushConstants.rect[2] = command.liquidGlass.w;
            pushConstants.rect[3] = command.liquidGlass.h;
            pushConstants.glassInfo1[0] = command.liquidGlass.cornerRadius;
            pushConstants.glassInfo1[1] = command.liquidGlass.blurRadius;
            pushConstants.glassInfo1[2] = command.liquidGlass.pixelWidth == 0 ? 0.0f : 1.0f / static_cast<float>(command.liquidGlass.pixelWidth);
            pushConstants.glassInfo1[3] = command.liquidGlass.pixelHeight == 0 ? 0.0f : 1.0f / static_cast<float>(command.liquidGlass.pixelHeight);
            pushConstants.glassInfo2[0] = command.liquidGlass.refractionAmount;
            pushConstants.glassInfo2[1] = command.liquidGlass.chromaticAberration;
            pushConstants.tintColor[0] = command.liquidGlass.tintR;
            pushConstants.tintColor[1] = command.liquidGlass.tintG;
            pushConstants.tintColor[2] = command.liquidGlass.tintB;
            pushConstants.tintColor[3] = command.liquidGlass.tintOpacity;
            pushConstants.lightInfo[0] = command.liquidGlass.lightX;
            pushConstants.lightInfo[1] = command.liquidGlass.lightY;
            pushConstants.lightInfo[2] = command.liquidGlass.highlightBoost;
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, liquidGlassPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, liquidGlassPipelineLayout, 0, 1, &frameDescriptorSet, 0, nullptr);
            cmdPushConstants(commandBuffer, liquidGlassPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pushConstants), &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Transition) {
            for (int transitionIndex = 0; transitionIndex < 2; ++transitionIndex) {
                VkImageMemoryBarrier uploadToTransfer {};
                uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
                uploadToTransfer.oldLayout = transitionImageLayouts[transitionIndex];
                uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                uploadToTransfer.image = transitionImages[transitionIndex];
                uploadToTransfer.subresourceRange = subresourceRange;
                uploadToTransfer.srcAccessMask = transitionImageLayouts[transitionIndex] == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                    ? VK_ACCESS_SHADER_READ_BIT
                    : 0;
                uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                const VkPipelineStageFlags uploadSrcStage = transitionImageLayouts[transitionIndex] == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                    ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
                    : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

                VkBufferImageCopy bufferImageCopy {};
                bufferImageCopy.bufferOffset =
                    totalBitmapBytes + totalPolygonBytes +
                    (transitionIndex == 0 ? transitionFromOffsets[index] : transitionToOffsets[index]);
                bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                bufferImageCopy.imageSubresource.layerCount = 1;
                bufferImageCopy.imageExtent.width = command.transition.pixelWidth;
                bufferImageCopy.imageExtent.height = command.transition.pixelHeight;
                bufferImageCopy.imageExtent.depth = 1;
                cmdCopyBufferToImage(
                    commandBuffer,
                    stagingBuffer,
                    transitionImages[transitionIndex],
                    VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                    1,
                    &bufferImageCopy);

                VkImageMemoryBarrier uploadToShaderRead {};
                uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
                uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                uploadToShaderRead.image = transitionImages[transitionIndex];
                uploadToShaderRead.subresourceRange = subresourceRange;
                uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);
                transitionImageLayouts[transitionIndex] = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            }

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            TransitionPushConstants pushConstants {};
            pushConstants.rect[0] = command.transition.x;
            pushConstants.rect[1] = command.transition.y;
            pushConstants.rect[2] = command.transition.w;
            pushConstants.rect[3] = command.transition.h;
            pushConstants.progressOpacity[0] = command.transition.progress;
            pushConstants.progressOpacity[1] = command.transition.opacity;
            pushConstants.progressOpacity[2] = static_cast<float>(command.transition.mode);
            pushConstants.screenSize[0] = static_cast<float>(extent.width);
            pushConstants.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasRoundedClip) {
                pushConstants.roundedClipRect[0] = command.roundedClipLeft;
                pushConstants.roundedClipRect[1] = command.roundedClipTop;
                pushConstants.roundedClipRect[2] = command.roundedClipRight;
                pushConstants.roundedClipRect[3] = command.roundedClipBottom;
                pushConstants.roundedClipRadius[0] = command.roundedClipRadiusX;
                pushConstants.roundedClipRadius[1] = command.roundedClipRadiusY;
                pushConstants.clipFlags[0] = 1.0f;
            }
            if (command.hasCustomQuad) {
                pushConstants.quadPoint01[0] = command.quadPoint0X;
                pushConstants.quadPoint01[1] = command.quadPoint0Y;
                pushConstants.quadPoint01[2] = command.quadPoint1X;
                pushConstants.quadPoint01[3] = command.quadPoint1Y;
                pushConstants.quadPoint23[0] = command.quadPoint2X;
                pushConstants.quadPoint23[1] = command.quadPoint2Y;
                pushConstants.quadPoint23[2] = command.quadPoint3X;
                pushConstants.quadPoint23[3] = command.quadPoint3Y;
                pushConstants.geometryFlags[0] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, transitionPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, transitionPipelineLayout, 0, 1, &transitionDescriptorSet, 0, nullptr);
            cmdPushConstants(
                commandBuffer,
                transitionPipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.bitmap.pixelWidth == 0 || command.bitmap.pixelHeight == 0 || command.bitmap.GetPixels().empty()) {
            return false;
        }

        VkImageMemoryBarrier uploadToTransfer {};
        uploadToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        uploadToTransfer.oldLayout = uploadImageLayout;
        uploadToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        uploadToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        uploadToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        uploadToTransfer.image = uploadImage;
        uploadToTransfer.subresourceRange = subresourceRange;
        uploadToTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
            ? VK_ACCESS_SHADER_READ_BIT
            : 0;
        uploadToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
            ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
            : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
        cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToTransfer);

        VkBufferImageCopy bufferImageCopy {};
        bufferImageCopy.bufferOffset = bitmapOffsets[index];
        bufferImageCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        bufferImageCopy.imageSubresource.layerCount = 1;
        bufferImageCopy.imageExtent.width = command.bitmap.pixelWidth;
        bufferImageCopy.imageExtent.height = command.bitmap.pixelHeight;
        bufferImageCopy.imageExtent.depth = 1;
        cmdCopyBufferToImage(
            commandBuffer,
            stagingBuffer,
            uploadImage,
            VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            1,
            &bufferImageCopy);

        VkImageMemoryBarrier uploadToShaderRead {};
        uploadToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        uploadToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
        uploadToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
        uploadToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        uploadToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        uploadToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
        uploadToShaderRead.image = uploadImage;
        uploadToShaderRead.subresourceRange = subresourceRange;
        uploadToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &uploadToShaderRead);
        uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        beginLoadRenderPass();
        cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
        BitmapQuadPushConstants pushConstants {};
        pushConstants.rect[0] = command.bitmap.x;
        pushConstants.rect[1] = command.bitmap.y;
        pushConstants.rect[2] = command.bitmap.w;
        pushConstants.rect[3] = command.bitmap.h;
        pushConstants.uvOpacity[0] = uploadWidth == 0 ? 1.0f : static_cast<float>(command.bitmap.pixelWidth) / static_cast<float>(uploadWidth);
        pushConstants.uvOpacity[1] = uploadHeight == 0 ? 1.0f : static_cast<float>(command.bitmap.pixelHeight) / static_cast<float>(uploadHeight);
        pushConstants.uvOpacity[2] = command.bitmap.opacity;
        pushConstants.screenSize[0] = static_cast<float>(extent.width);
        pushConstants.screenSize[1] = static_cast<float>(extent.height);
        if (command.hasRoundedClip) {
            pushConstants.roundedClipRect[0] = command.roundedClipLeft;
            pushConstants.roundedClipRect[1] = command.roundedClipTop;
            pushConstants.roundedClipRect[2] = command.roundedClipRight;
            pushConstants.roundedClipRect[3] = command.roundedClipBottom;
            pushConstants.roundedClipRadius[0] = command.roundedClipRadiusX;
            pushConstants.roundedClipRadius[1] = command.roundedClipRadiusY;
            pushConstants.clipFlags[0] = 1.0f;
        }
        if (command.hasInnerRoundedClip) {
            pushConstants.innerRoundedClipRect[0] = command.innerRoundedClipLeft;
            pushConstants.innerRoundedClipRect[1] = command.innerRoundedClipTop;
            pushConstants.innerRoundedClipRect[2] = command.innerRoundedClipRight;
            pushConstants.innerRoundedClipRect[3] = command.innerRoundedClipBottom;
            pushConstants.innerRoundedClipRadius[0] = command.innerRoundedClipRadiusX;
            pushConstants.innerRoundedClipRadius[1] = command.innerRoundedClipRadiusY;
            pushConstants.clipFlags[1] = 1.0f;
        }
        if (command.hasCustomQuad) {
            pushConstants.quadPoint01[0] = command.quadPoint0X;
            pushConstants.quadPoint01[1] = command.quadPoint0Y;
            pushConstants.quadPoint01[2] = command.quadPoint1X;
            pushConstants.quadPoint01[3] = command.quadPoint1Y;
            pushConstants.quadPoint23[0] = command.quadPoint2X;
            pushConstants.quadPoint23[1] = command.quadPoint2Y;
            pushConstants.quadPoint23[2] = command.quadPoint3X;
            pushConstants.quadPoint23[3] = command.quadPoint3Y;
            pushConstants.geometryFlags[0] = 1.0f;
        }
        cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, bitmapPipeline);
        cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, bitmapPipelineLayout, 0, 1, &frameDescriptorSet, 0, nullptr);
        cmdPushConstants(
            commandBuffer,
            bitmapPipelineLayout,
            VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
            0,
            sizeof(pushConstants),
            &pushConstants);
        cmdDraw(commandBuffer, 6, 1, 0, 0);
        cmdEndRenderPass(commandBuffer);
    }

    VkImageMemoryBarrier toPresent {};
    toPresent.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    toPresent.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    toPresent.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    toPresent.newLayout = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    toPresent.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toPresent.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    toPresent.image = images[imageIndex];
    toPresent.subresourceRange = subresourceRange;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, 0, 0, nullptr, 0, nullptr, 1, &toPresent);

    if (endCommandBuffer(commandBuffer) != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    VkSemaphore signalSemaphore = (imageIndex < renderFinishedPerImage.size())
        ? renderFinishedPerImage[imageIndex]
        : VK_NULL_HANDLE;
    if (signalSemaphore == VK_NULL_HANDLE) {
        VK_LOG("[Vulkan] DrawReplayFrame: missing renderFinishedPerImage[%u]\n", imageIndex);
        EndFrame();
        return false;
    }
    const VkPipelineStageFlags waitStage = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
    VkSubmitInfo submitInfo {};
    submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
    submitInfo.waitSemaphoreCount = 1;
    submitInfo.pWaitSemaphores = &imageAvailable;
    submitInfo.pWaitDstStageMask = &waitStage;
    submitInfo.commandBufferCount = 1;
    submitInfo.pCommandBuffers = &commandBuffer;
    submitInfo.signalSemaphoreCount = 1;
    submitInfo.pSignalSemaphores = &signalSemaphore;
    if (queueSubmit(queue, 1, &submitInfo, inFlight) != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    VkPresentInfoKHR presentInfo {};
    presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores = &signalSemaphore;
    presentInfo.swapchainCount = 1;
    presentInfo.pSwapchains = &swapchain;
    presentInfo.pImageIndices = &imageIndex;
    const VkResult presentResult = queuePresent(queue, &presentInfo);
    if (presentResult != VK_SUCCESS && presentResult != VK_SUBOPTIMAL_KHR && presentResult != VK_ERROR_OUT_OF_DATE_KHR) {
        EndFrame();
        return false;
    }

    imageLayouts[imageIndex] = VK_IMAGE_LAYOUT_PRESENT_SRC_KHR;
    submitted = true;
    EndFrame();
    return true;
}

void VulkanRenderTarget::Impl::Destroy()
{
    if (deviceWaitIdle && device != VK_NULL_HANDLE) {
        deviceWaitIdle(device);
    }

    if (device != VK_NULL_HANDLE) {
        DestroyGraphicsResources();
        DestroyTransitionImages();
        // DestroyPerFrameResources releases the per-frame command buffers (via the
        // command pool below), fences, imageAvailable semaphores, staging buffers,
        // upload images, and renderFinishedPerImage semaphores. It must run before
        // the command pool and descriptor pool are destroyed because it relies on
        // them still being valid.
        DestroyPerFrameResources();
        if (destroyCommandPool && commandPool != VK_NULL_HANDLE) {
            destroyCommandPool(device, commandPool, nullptr);
        }
        if (destroySwapchain && swapchain != VK_NULL_HANDLE) {
            destroySwapchain(device, swapchain, nullptr);
        }
        if (destroyDevice) {
            destroyDevice(device, nullptr);
        }
    }

    if (destroySurface && surface != VK_NULL_HANDLE && instance != VK_NULL_HANDLE) {
        destroySurface(instance, surface, nullptr);
    }
    if (destroyInstance && instance != VK_NULL_HANDLE) {
        destroyInstance(instance, nullptr);
    }

    instance = VK_NULL_HANDLE;
    physicalDevice = VK_NULL_HANDLE;
    device = VK_NULL_HANDLE;
    queue = VK_NULL_HANDLE;
    surface = VK_NULL_HANDLE;
    swapchain = VK_NULL_HANDLE;
    commandPool = VK_NULL_HANDLE;
    commandBuffer = VK_NULL_HANDLE;
    uploadImage = VK_NULL_HANDLE;
    uploadImageMemory = VK_NULL_HANDLE;
    uploadImageView = VK_NULL_HANDLE;
    frameSampler = VK_NULL_HANDLE;
    frameDescriptorSetLayout = VK_NULL_HANDLE;
    frameDescriptorPool = VK_NULL_HANDLE;
    frameDescriptorSet = VK_NULL_HANDLE;
    framePipelineLayout = VK_NULL_HANDLE;
    frameRenderPass = VK_NULL_HANDLE;
    framePipeline = VK_NULL_HANDLE;
    solidRectPipelineLayout = VK_NULL_HANDLE;
    solidRectPipeline = VK_NULL_HANDLE;
    clearRectPipeline = VK_NULL_HANDLE;
    bitmapPipelineLayout = VK_NULL_HANDLE;
    bitmapPipeline = VK_NULL_HANDLE;
    blurPipelineLayout = VK_NULL_HANDLE;
    blurPipeline = VK_NULL_HANDLE;
    liquidGlassPipelineLayout = VK_NULL_HANDLE;
    liquidGlassPipeline = VK_NULL_HANDLE;
    backdropPipelineLayout = VK_NULL_HANDLE;
    backdropPipeline = VK_NULL_HANDLE;
    glowPipelineLayout = VK_NULL_HANDLE;
    glowPipeline = VK_NULL_HANDLE;
    triangleFillPipelineLayout = VK_NULL_HANDLE;
    triangleFillPipeline = VK_NULL_HANDLE;
    transitionPipelineLayout = VK_NULL_HANDLE;
    transitionPipeline = VK_NULL_HANDLE;
    transitionDescriptorSetLayout = VK_NULL_HANDLE;
    transitionDescriptorPool = VK_NULL_HANDLE;
    transitionDescriptorSet = VK_NULL_HANDLE;
    images.clear();
    imageLayouts.clear();
    imageViews.clear();
    framebuffers.clear();
}

VulkanRenderTarget::Impl::~Impl()
{
    Destroy();
}

VulkanRenderTarget::CpuTransform VulkanRenderTarget::GetCurrentTransform() const
{
    return transformStack_.empty() ? CpuTransform {} : transformStack_.back();
}

float VulkanRenderTarget::GetCurrentOpacity() const
{
    return opacityStack_.empty() ? 1.0f : opacityStack_.back();
}

VulkanRenderTarget::CpuTransform VulkanRenderTarget::MultiplyTransforms(const CpuTransform& left, const CpuTransform& right)
{
    CpuTransform result {};
    result.m11 = left.m11 * right.m11 + left.m12 * right.m21;
    result.m12 = left.m11 * right.m12 + left.m12 * right.m22;
    result.m21 = left.m21 * right.m11 + left.m22 * right.m21;
    result.m22 = left.m21 * right.m12 + left.m22 * right.m22;
    result.dx = left.dx * right.m11 + left.dy * right.m21 + right.dx;
    result.dy = left.dx * right.m12 + left.dy * right.m22 + right.dy;
    return result;
}

bool VulkanRenderTarget::TryInvertTransform(const CpuTransform& transform, CpuTransform& inverse)
{
    const float determinant = transform.m11 * transform.m22 - transform.m12 * transform.m21;
    if (std::fabs(determinant) < 0.00001f) {
        inverse = CpuTransform {};
        return false;
    }

    const float invDet = 1.0f / determinant;
    inverse.m11 = transform.m22 * invDet;
    inverse.m12 = -transform.m12 * invDet;
    inverse.m21 = -transform.m21 * invDet;
    inverse.m22 = transform.m11 * invDet;
    inverse.dx = (-transform.dx * inverse.m11) + (-transform.dy * inverse.m21);
    inverse.dy = (-transform.dx * inverse.m12) + (-transform.dy * inverse.m22);
    return true;
}

void VulkanRenderTarget::ApplyTransform(const CpuTransform& transform, float x, float y, float& outX, float& outY)
{
    outX = x * transform.m11 + y * transform.m21 + transform.dx;
    outY = x * transform.m12 + y * transform.m22 + transform.dy;
}

bool VulkanRenderTarget::TryPopulateReplayClip(GpuReplayCommand& command) const
{
    command.hasScissor = false;
    command.scissorLeft = 0;
    command.scissorTop = 0;
    command.scissorRight = width_;
    command.scissorBottom = height_;
    command.hasRoundedClip = false;
    command.roundedClipLeft = 0.0f;
    command.roundedClipTop = 0.0f;
    command.roundedClipRight = 0.0f;
    command.roundedClipBottom = 0.0f;
    command.roundedClipRadiusX = 0.0f;
    command.roundedClipRadiusY = 0.0f;

    int32_t left = 0;
    int32_t top = 0;
    int32_t right = width_;
    int32_t bottom = height_;
    int roundedClipCount = 0;

    constexpr float kEpsilon = 0.0001f;
    for (const auto& clip : clipStack_) {
        if (!clip.hasInverse) {
            return false;
        }

        if (std::fabs(clip.transform.m12) > kEpsilon || std::fabs(clip.transform.m21) > kEpsilon) {
            return false;
        }

        float x0 = 0.0f;
        float y0 = 0.0f;
        float x1 = 0.0f;
        float y1 = 0.0f;
        ApplyTransform(clip.transform, clip.x, clip.y, x0, y0);
        ApplyTransform(clip.transform, clip.x + clip.w, clip.y + clip.h, x1, y1);

        const float clipWorldLeft = std::min(x0, x1);
        const float clipWorldTop = std::min(y0, y1);
        const float clipWorldRight = std::max(x0, x1);
        const float clipWorldBottom = std::max(y0, y1);
        left = std::max(left, static_cast<int32_t>(std::floor(clipWorldLeft)));
        top = std::max(top, static_cast<int32_t>(std::floor(clipWorldTop)));
        right = std::min(right, static_cast<int32_t>(std::ceil(clipWorldRight)));
        bottom = std::min(bottom, static_cast<int32_t>(std::ceil(clipWorldBottom)));

        if (clip.rounded) {
            ++roundedClipCount;
            if (roundedClipCount > 1) {
                return false;
            }

            command.hasRoundedClip = true;
            command.roundedClipLeft = clipWorldLeft;
            command.roundedClipTop = clipWorldTop;
            command.roundedClipRight = clipWorldRight;
            command.roundedClipBottom = clipWorldBottom;
            command.roundedClipRadiusX = std::fabs(clip.transform.m11) * std::min(clip.rx, clip.w * 0.5f);
            command.roundedClipRadiusY = std::fabs(clip.transform.m22) * std::min(clip.ry, clip.h * 0.5f);
        }
    }

    command.scissorLeft = std::clamp(left, 0, width_);
    command.scissorTop = std::clamp(top, 0, height_);
    command.scissorRight = std::clamp(right, 0, width_);
    command.scissorBottom = std::clamp(bottom, 0, height_);
    command.hasScissor = !clipStack_.empty();
    return true;
}

bool VulkanRenderTarget::IsInsideClip(float x, float y) const
{
    for (const auto& clip : clipStack_) {
        if (!clip.hasInverse) {
            return false;
        }

        float localX = 0.0f;
        float localY = 0.0f;
        ApplyTransform(clip.inverseTransform, x, y, localX, localY);

        if (localX < clip.x || localY < clip.y || localX > clip.x + clip.w || localY > clip.y + clip.h) {
            return false;
        }

        if (!clip.rounded) {
            continue;
        }

        const float rx = std::min(clip.rx, clip.w * 0.5f);
        const float ry = std::min(clip.ry, clip.h * 0.5f);
        if (rx <= 0.0f || ry <= 0.0f) {
            continue;
        }

        const float left = clip.x;
        const float top = clip.y;
        const float right = clip.x + clip.w;
        const float bottom = clip.y + clip.h;

        bool insideRounded = true;
        if (localX < left + rx && localY < top + ry) {
            const float dx = (localX - (left + rx)) / rx;
            const float dy = (localY - (top + ry)) / ry;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX > right - rx && localY < top + ry) {
            const float dx = (localX - (right - rx)) / rx;
            const float dy = (localY - (top + ry)) / ry;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX < left + rx && localY > bottom - ry) {
            const float dx = (localX - (left + rx)) / rx;
            const float dy = (localY - (bottom - ry)) / ry;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX > right - rx && localY > bottom - ry) {
            const float dx = (localX - (right - rx)) / rx;
            const float dy = (localY - (bottom - ry)) / ry;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        }

        if (!insideRounded) {
            return false;
        }
    }

    return true;
}

void VulkanRenderTarget::ResizeCpuCanvas()
{
    if (width_ <= 0 || height_ <= 0) {
        pixelBuffer_.clear();
        return;
    }

    pixelBuffer_.assign(static_cast<size_t>(width_) * static_cast<size_t>(height_) * 4u, 0);
}

void VulkanRenderTarget::ClearCpuCanvas(uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    // Lazy CPU rasterization: when the frame will be presented via the GPU replay
    // path (DrawReplayFrame), the CPU pixel buffer is never uploaded, so all of
    // this work is thrown away. Skip it until EnsureCpuRasterization triggers a
    // backfill or EndDraw falls back to DrawFrame with raw pixels.
    if (!cpuRasterNeeded_) {
        return;
    }

    if (pixelBuffer_.empty()) {
        ResizeCpuCanvas();
    }

    for (size_t index = 0; index + 3 < pixelBuffer_.size(); index += 4) {
        pixelBuffer_[index + 0] = b;
        pixelBuffer_[index + 1] = g;
        pixelBuffer_[index + 2] = r;
        pixelBuffer_[index + 3] = a;
    }
}

bool VulkanRenderTarget::TryGetSolidBrushColor(Brush* brush, uint8_t& b, uint8_t& g, uint8_t& r, uint8_t& a) const
{
    if (!brush || brush->GetType() != JALIUM_BRUSH_SOLID) {
        return false;
    }

    const auto* solidBrush = static_cast<VulkanSolidBrush*>(brush);
    const auto toByte = [](float value) -> uint8_t {
        value = std::clamp(value, 0.0f, 1.0f);
        return static_cast<uint8_t>(value * 255.0f + 0.5f);
    };

    b = toByte(solidBrush->b_);
    g = toByte(solidBrush->g_);
    r = toByte(solidBrush->r_);
    a = toByte(solidBrush->a_);
    return true;
}

bool VulkanRenderTarget::TryGetApproximateBrushColor(Brush* brush, uint8_t& b, uint8_t& g, uint8_t& r, uint8_t& a) const
{
    if (!brush) {
        return false;
    }

    const auto toByte = [](float value) -> uint8_t {
        value = std::clamp(value, 0.0f, 1.0f);
        return static_cast<uint8_t>(value * 255.0f + 0.5f);
    };

    switch (brush->GetType()) {
        case JALIUM_BRUSH_SOLID: {
            const auto* solidBrush = static_cast<VulkanSolidBrush*>(brush);
            b = toByte(solidBrush->b_);
            g = toByte(solidBrush->g_);
            r = toByte(solidBrush->r_);
            a = toByte(solidBrush->a_);
            return true;
        }

        case JALIUM_BRUSH_LINEAR_GRADIENT: {
            const auto* lg = static_cast<VulkanLinearGradientBrush*>(brush);
            if (lg->stops_.empty()) {
                return false;
            }
            // Blend every stop with equal weight. This isn't a true gradient
            // average (it ignores stop positions and perceptual curves), but
            // for the common case of a ~2-stop near-solid gradient it lands
            // within a few units of the visual midtone and costs a handful of
            // float ops per draw call.
            float rs = 0.0f, gs = 0.0f, bs = 0.0f, as = 0.0f;
            for (const auto& stop : lg->stops_) {
                rs += stop.r;
                gs += stop.g;
                bs += stop.b;
                as += stop.a;
            }
            const float invCount = 1.0f / static_cast<float>(lg->stops_.size());
            r = toByte(rs * invCount);
            g = toByte(gs * invCount);
            b = toByte(bs * invCount);
            a = toByte(as * invCount);
            return true;
        }

        case JALIUM_BRUSH_RADIAL_GRADIENT: {
            const auto* rg = static_cast<VulkanRadialGradientBrush*>(brush);
            if (rg->stops_.empty()) {
                return false;
            }
            float rs = 0.0f, gs = 0.0f, bs = 0.0f, as = 0.0f;
            for (const auto& stop : rg->stops_) {
                rs += stop.r;
                gs += stop.g;
                bs += stop.b;
                as += stop.a;
            }
            const float invCount = 1.0f / static_cast<float>(rg->stops_.size());
            r = toByte(rs * invCount);
            g = toByte(gs * invCount);
            b = toByte(bs * invCount);
            a = toByte(as * invCount);
            return true;
        }

        default:
            return false;
    }
}

void VulkanRenderTarget::BlendPixel(int x, int y, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    if (x < 0 || y < 0 || x >= width_ || y >= height_ || pixelBuffer_.empty()) {
        return;
    }

    if (!IsInsideClip(static_cast<float>(x) + 0.5f, static_cast<float>(y) + 0.5f)) {
        return;
    }

    const float opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    a = static_cast<uint8_t>(static_cast<float>(a) * opacity + 0.5f);
    if (a == 0) {
        return;
    }

    const size_t offset = (static_cast<size_t>(y) * static_cast<size_t>(width_) + static_cast<size_t>(x)) * 4u;
    const uint32_t srcA = a;
    const uint32_t invA = 255u - srcA;

    pixelBuffer_[offset + 0] = static_cast<uint8_t>((b * srcA + pixelBuffer_[offset + 0] * invA) / 255u);
    pixelBuffer_[offset + 1] = static_cast<uint8_t>((g * srcA + pixelBuffer_[offset + 1] * invA) / 255u);
    pixelBuffer_[offset + 2] = static_cast<uint8_t>((r * srcA + pixelBuffer_[offset + 2] * invA) / 255u);
    pixelBuffer_[offset + 3] = static_cast<uint8_t>(std::min<uint32_t>(255u, srcA + (pixelBuffer_[offset + 3] * invA) / 255u));
}

void VulkanRenderTarget::FillSolidRect(int left, int top, int right, int bottom, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    left = std::max(left, 0);
    top = std::max(top, 0);
    right = std::min(right, width_);
    bottom = std::min(bottom, height_);

    for (int y = top; y < bottom; ++y) {
        for (int x = left; x < right; ++x) {
            BlendPixel(x, y, b, g, r, a);
        }
    }
}

void VulkanRenderTarget::EnsureCpuRasterization()
{
    // Idempotent: once triggered, subsequent calls are no-ops and the CPU canvas
    // stays in sync with every further Draw* call this frame (because those
    // Draw* functions now see cpuRasterNeeded_ = true and run their CPU paths).
    if (cpuRasterNeeded_) {
        return;
    }

    // Short-circuit: when the frame is still eligible for the GPU replay path,
    // EndDraw will go through DrawReplayFrame and discard pixelBuffer_ anyway.
    // Committing to CPU rasterization here (called mid-frame from an effect
    // Draw* such as DrawBackdropFilter or BeginEffectCapture) would force every
    // previously-recorded and every subsequently-issued draw call down the CPU
    // path — approximately 100ms of wasted work per frame in Gallery. The
    // visual tradeoff is that mid-frame effect reads see a stale/empty
    // pixelBuffer_ (Acrylic/Backdrop may render blank or with the prior frame's
    // content), but the CPU-side backdrop blur was going to be overwritten by
    // the GPU replay anyway. The proper long-term fix is to rewrite the GPU
    // Backdrop command to sample the swap-chain image directly rather than
    // carrying a CPU-side pixel snapshot, but this lets Vulkan hit GPU speeds
    // today for the 99% of UI that doesn't use effects.
    if (gpuReplaySupported_ && gpuReplayHasClear_) {
        return;
    }
    cpuRasterNeeded_ = true;

    // Replay uses physical-pixel coordinates already stored in the recorded
    // commands — no DPI scale, no transform, no clip should be re-applied.
    // Save and clear the drawing stacks, then restore them after replay so that
    // whoever called us (mid-frame, inside a Draw* method) continues with their
    // original stacks intact.
    auto savedTransforms = std::move(transformStack_);
    auto savedOpacities = std::move(opacityStack_);
    auto savedClips = std::move(clipStack_);
    transformStack_.clear();
    transformStack_.push_back(CpuTransform{});
    opacityStack_.clear();
    opacityStack_.push_back(1.0f);
    clipStack_.clear();

    const auto toByte = [](float v) -> uint8_t {
        v = std::clamp(v, 0.0f, 1.0f);
        return static_cast<uint8_t>(v * 255.0f + 0.5f);
    };
    // Re-clear the CPU canvas to the recorded clearColor_, matching the state
    // Clear() would have left it in if cpuRasterNeeded_ had been true from the
    // start of the frame. clearColor_ is stored in {r, g, b, a} order and
    // ClearCpuCanvas takes (b, g, r, a).
    ClearCpuCanvas(toByte(clearColor_[2]),
                   toByte(clearColor_[1]),
                   toByte(clearColor_[0]),
                   toByte(clearColor_[3]));

    for (const auto& cmd : gpuReplayCommands_) {
        ReplayCommandToCpu(cmd);
    }

    transformStack_ = std::move(savedTransforms);
    opacityStack_ = std::move(savedOpacities);
    clipStack_ = std::move(savedClips);
}

void VulkanRenderTarget::ReplayCommandToCpu(const GpuReplayCommand& command)
{
    const auto toByte = [](float v) -> uint8_t {
        v = std::clamp(v, 0.0f, 1.0f);
        return static_cast<uint8_t>(v * 255.0f + 0.5f);
    };
    auto pushScissor = [&]() {
        if (command.hasScissor) {
            const float sw = static_cast<float>(command.scissorRight - command.scissorLeft);
            const float sh = static_cast<float>(command.scissorBottom - command.scissorTop);
            PushClip(static_cast<float>(command.scissorLeft),
                     static_cast<float>(command.scissorTop),
                     sw,
                     sh);
        }
    };
    auto popScissor = [&]() {
        if (command.hasScissor) {
            PopClip();
        }
    };

    switch (command.kind) {
        case GpuReplayCommandKind::SolidRect: {
            const auto& r = command.solidRect;
            pushScissor();
            FillSolidRect(static_cast<int>(std::floor(r.x)),
                          static_cast<int>(std::floor(r.y)),
                          static_cast<int>(std::ceil(r.x + r.w)),
                          static_cast<int>(std::ceil(r.y + r.h)),
                          toByte(r.b), toByte(r.g), toByte(r.r), toByte(r.a));
            popScissor();
            break;
        }
        case GpuReplayCommandKind::ClearRect: {
            const auto& r = command.solidRect;
            const int left = std::max(0, static_cast<int>(std::floor(r.x)));
            const int top = std::max(0, static_cast<int>(std::floor(r.y)));
            const int right = std::min(width_, static_cast<int>(std::ceil(r.x + r.w)));
            const int bottom = std::min(height_, static_cast<int>(std::ceil(r.y + r.h)));
            for (int py = top; py < bottom; ++py) {
                for (int px = left; px < right; ++px) {
                    const size_t offset = (static_cast<size_t>(py) * static_cast<size_t>(width_) + static_cast<size_t>(px)) * 4u;
                    if (offset + 3 < pixelBuffer_.size()) {
                        pixelBuffer_[offset + 0] = 0;
                        pixelBuffer_[offset + 1] = 0;
                        pixelBuffer_[offset + 2] = 0;
                        pixelBuffer_[offset + 3] = 0;
                    }
                }
            }
            break;
        }
        case GpuReplayCommandKind::Bitmap: {
            const auto& bmp = command.bitmap;
            pushScissor();
            BlendBuffer(bmp.GetPixels(),
                        static_cast<int>(bmp.pixelWidth),
                        static_cast<int>(bmp.pixelHeight),
                        bmp.x, bmp.y, bmp.w, bmp.h, bmp.opacity);
            popScissor();
            break;
        }
        case GpuReplayCommandKind::FilledPolygon: {
            const auto& p = command.filledPolygon;
            pushScissor();
            RasterizePolygon(p.triangleVertices, 0,
                             toByte(p.b), toByte(p.g), toByte(p.r), toByte(p.a));
            popScissor();
            break;
        }
        case GpuReplayCommandKind::Blur:
        case GpuReplayCommandKind::Backdrop:
        case GpuReplayCommandKind::LiquidGlass:
        case GpuReplayCommandKind::Glow:
        case GpuReplayCommandKind::Transition:
            // Effect commands either already triggered EnsureCpuRasterization
            // at the moment they were issued (because their Draw* methods call
            // EnsureCpuRasterization on entry — they need pixelBuffer_ in sync
            // to read from), or they are GPU-only effects with no CPU fallback.
            // In either case, there is nothing to replay here.
            break;
    }
}

std::vector<uint8_t> VulkanRenderTarget::BlurPixels(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, int radius, float x, float y, float w, float h) const
{
    const size_t expectedSize = static_cast<size_t>(sourceWidth) * static_cast<size_t>(sourceHeight) * 4u;
    if (radius <= 0 || source.size() != expectedSize || sourceWidth <= 0 || sourceHeight <= 0) {
        return source;
    }

    std::vector<uint8_t> horizontal = source;
    std::vector<uint8_t> blurred = source;
    const int left = std::max(0, static_cast<int>(std::floor(x - radius)));
    const int top = std::max(0, static_cast<int>(std::floor(y - radius)));
    const int right = std::min(sourceWidth, static_cast<int>(std::ceil(x + w + radius)));
    const int bottom = std::min(sourceHeight, static_cast<int>(std::ceil(y + h + radius)));

    for (int py = top; py < bottom; ++py) {
        for (int px = left; px < right; ++px) {
            uint32_t sumB = 0, sumG = 0, sumR = 0, sumA = 0, count = 0;
            for (int sx = std::max(left, px - radius); sx <= std::min(right - 1, px + radius); ++sx) {
                const size_t offset = (static_cast<size_t>(py) * static_cast<size_t>(sourceWidth) + static_cast<size_t>(sx)) * 4u;
                sumB += source[offset + 0];
                sumG += source[offset + 1];
                sumR += source[offset + 2];
                sumA += source[offset + 3];
                ++count;
            }

            const size_t destOffset = (static_cast<size_t>(py) * static_cast<size_t>(sourceWidth) + static_cast<size_t>(px)) * 4u;
            horizontal[destOffset + 0] = static_cast<uint8_t>(sumB / count);
            horizontal[destOffset + 1] = static_cast<uint8_t>(sumG / count);
            horizontal[destOffset + 2] = static_cast<uint8_t>(sumR / count);
            horizontal[destOffset + 3] = static_cast<uint8_t>(sumA / count);
        }
    }

    for (int py = top; py < bottom; ++py) {
        for (int px = left; px < right; ++px) {
            uint32_t sumB = 0, sumG = 0, sumR = 0, sumA = 0, count = 0;
            for (int sy = std::max(top, py - radius); sy <= std::min(bottom - 1, py + radius); ++sy) {
                const size_t offset = (static_cast<size_t>(sy) * static_cast<size_t>(sourceWidth) + static_cast<size_t>(px)) * 4u;
                sumB += horizontal[offset + 0];
                sumG += horizontal[offset + 1];
                sumR += horizontal[offset + 2];
                sumA += horizontal[offset + 3];
                ++count;
            }

            const size_t destOffset = (static_cast<size_t>(py) * static_cast<size_t>(sourceWidth) + static_cast<size_t>(px)) * 4u;
            blurred[destOffset + 0] = static_cast<uint8_t>(sumB / count);
            blurred[destOffset + 1] = static_cast<uint8_t>(sumG / count);
            blurred[destOffset + 2] = static_cast<uint8_t>(sumR / count);
            blurred[destOffset + 3] = static_cast<uint8_t>(sumA / count);
        }
    }

    return blurred;
}

void VulkanRenderTarget::BlendBuffer(const std::vector<uint8_t>& source, int sourceWidth, int sourceHeight, float x, float y, float w, float h, float opacity)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    const size_t expectedSize = static_cast<size_t>(sourceWidth) * static_cast<size_t>(sourceHeight) * 4u;
    if (source.empty() || source.size() != expectedSize || sourceWidth <= 0 || sourceHeight <= 0 || opacity <= 0.0f) {
        return;
    }

    const int left = std::max(0, static_cast<int>(std::floor(x)));
    const int top = std::max(0, static_cast<int>(std::floor(y)));
    const int right = std::min(width_, static_cast<int>(std::ceil(x + w)));
    const int bottom = std::min(height_, static_cast<int>(std::ceil(y + h)));
    const uint8_t opacityByte = static_cast<uint8_t>(std::clamp(opacity, 0.0f, 1.0f) * 255.0f + 0.5f);

    for (int py = top; py < bottom; ++py) {
        for (int px = left; px < right; ++px) {
            const float u = (static_cast<float>(px - left) + 0.5f) / std::max(1, right - left);
            const float v = (static_cast<float>(py - top) + 0.5f) / std::max(1, bottom - top);
            const int srcX = std::clamp(static_cast<int>(u * sourceWidth), 0, sourceWidth - 1);
            const int srcY = std::clamp(static_cast<int>(v * sourceHeight), 0, sourceHeight - 1);
            const size_t offset = (static_cast<size_t>(srcY) * static_cast<size_t>(sourceWidth) + static_cast<size_t>(srcX)) * 4u;
            const uint8_t srcB = source[offset + 0];
            const uint8_t srcG = source[offset + 1];
            const uint8_t srcR = source[offset + 2];
            const uint8_t srcA = static_cast<uint8_t>((source[offset + 3] * opacityByte) / 255u);
            BlendPixel(px, py, srcB, srcG, srcR, srcA);
        }
    }
}

void VulkanRenderTarget::PushTemporaryClip(float x, float y, float w, float h, float rx, float ry)
{
    if (rx > 0.0f || ry > 0.0f) {
        PushRoundedRectClip(x, y, w, h, rx, ry);
    } else {
        PushClip(x, y, w, h);
    }
}

void VulkanRenderTarget::PopTemporaryClip()
{
    PopClip();
}

void VulkanRenderTarget::ParseTintColor(const char* tint, float fallbackR, float fallbackG, float fallbackB, uint8_t& outB, uint8_t& outG, uint8_t& outR) const
{
    float r = fallbackR;
    float g = fallbackG;
    float b = fallbackB;

    if (tint && tint[0] == '#' && std::strlen(tint) >= 7) {
        int red = 0;
        int green = 0;
        int blue = 0;
#ifdef _WIN32
        ::sscanf_s(tint + 1, "%02x%02x%02x", &red, &green, &blue);
#else
        std::sscanf(tint + 1, "%02x%02x%02x", &red, &green, &blue);
#endif
        r = red / 255.0f;
        g = green / 255.0f;
        b = blue / 255.0f;
    }

    const auto toByte = [](float value) -> uint8_t {
        value = std::clamp(value, 0.0f, 1.0f);
        return static_cast<uint8_t>(value * 255.0f + 0.5f);
    };

    outR = toByte(r);
    outG = toByte(g);
    outB = toByte(b);
}

void VulkanRenderTarget::BlendOutsideRect(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    const int left = static_cast<int>(std::floor(x));
    const int top = static_cast<int>(std::floor(y));
    const int right = static_cast<int>(std::ceil(x + w));
    const int bottom = static_cast<int>(std::ceil(y + h));

    for (int py = 0; py < height_; ++py) {
        for (int px = 0; px < width_; ++px) {
            if (px >= left && px < right && py >= top && py < bottom) {
                continue;
            }
            BlendPixel(px, py, b, g, r, a);
        }
    }
}

void VulkanRenderTarget::StrokeRoundedRectApprox(float x, float y, float w, float h, float rx, float ry, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    if (rx <= 0.0f && ry <= 0.0f) {
        std::vector<float> rect(8);
        const auto transform = GetCurrentTransform();
        ApplyTransform(transform, x, y, rect[0], rect[1]);
        ApplyTransform(transform, x + w, y, rect[2], rect[3]);
        ApplyTransform(transform, x + w, y + h, rect[4], rect[5]);
        ApplyTransform(transform, x, y + h, rect[6], rect[7]);
        StrokePolyline(rect, true, strokeWidth, b, g, r, a);
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(80);
    float wx = 0.0f;
    float wy = 0.0f;

    ApplyTransform(transform, x + rx, y, wx, wy);
    points.push_back(wx); points.push_back(wy);
    ApplyTransform(transform, x + w - rx, y, wx, wy);
    points.push_back(wx); points.push_back(wy);
    for (int step = 0; step <= 8; ++step) {
        const float angle = -1.57079632679f + (static_cast<float>(step) / 8.0f) * 1.57079632679f;
        ApplyTransform(transform, x + w - rx + std::cos(angle) * rx, y + ry + std::sin(angle) * ry, wx, wy);
        points.push_back(wx); points.push_back(wy);
    }
    ApplyTransform(transform, x + w, y + h - ry, wx, wy);
    points.push_back(wx); points.push_back(wy);
    for (int step = 0; step <= 8; ++step) {
        const float angle = (static_cast<float>(step) / 8.0f) * 1.57079632679f;
        ApplyTransform(transform, x + w - rx + std::cos(angle) * rx, y + h - ry + std::sin(angle) * ry, wx, wy);
        points.push_back(wx); points.push_back(wy);
    }
    ApplyTransform(transform, x + rx, y + h, wx, wy);
    points.push_back(wx); points.push_back(wy);
    for (int step = 0; step <= 8; ++step) {
        const float angle = 1.57079632679f + (static_cast<float>(step) / 8.0f) * 1.57079632679f;
        ApplyTransform(transform, x + rx + std::cos(angle) * rx, y + h - ry + std::sin(angle) * ry, wx, wy);
        points.push_back(wx); points.push_back(wy);
    }
    ApplyTransform(transform, x, y + ry, wx, wy);
    points.push_back(wx); points.push_back(wy);
    for (int step = 0; step <= 8; ++step) {
        const float angle = 3.14159265359f + (static_cast<float>(step) / 8.0f) * 1.57079632679f;
        ApplyTransform(transform, x + rx + std::cos(angle) * rx, y + ry + std::sin(angle) * ry, wx, wy);
        points.push_back(wx); points.push_back(wy);
    }

    StrokePolyline(points, true, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::ResetGpuReplay()
{
    gpuReplaySupported_ = true;
    gpuReplayHasClear_ = false;
    gpuReplayCommands_.clear();
}

void VulkanRenderTarget::InvalidateGpuReplay(const char* caller)
{
    // Called when a Draw* cannot be expressed as a replay command. The frame
    // must now fall back to DrawFrame with raw pixelBuffer_ content, so catch
    // pixelBuffer_ up to every command recorded so far before releasing replay.
    (void)caller;
    if (gpuReplaySupported_ && isDrawing_) {
        EnsureCpuRasterization();
    }
    gpuReplaySupported_ = false;
}

bool VulkanRenderTarget::TryRecordGpuSolidRectCommand(float x, float y, float w, float h, Brush* brush)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }
    // Degenerate rects (w or h == 0) are visual no-ops. Return true so the
    // caller doesn't fall back to CPU upload for an invisible primitive.
    if (w == 0.0f || h == 0.0f) {
        return true;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuSolidRectCommand command {};
    command.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    command.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    command.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - command.x;
    command.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - command.y;
    command.r = static_cast<float>(r) / 255.0f;
    command.g = static_cast<float>(g) / 255.0f;
    command.b = static_cast<float>(b) / 255.0f;
    command.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);

    // Treat zero-area or fully-transparent fills as successful no-ops rather
    // than failures. Gallery's theme recursively fills invisible hit-target
    // rectangles with Transparent brushes as layout stakes, and the old code
    // counted those as "TryRecord failed" → invalidate the whole frame →
    // force CPU upload. With this, transparent fills stay on the GPU replay
    // path (we simply don't push a command, because drawing a 0-alpha rect is
    // a visual no-op anyway).
    if (command.w <= kEpsilon || command.h <= kEpsilon || command.a <= 0.0f) {
        return true;
    }

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::SolidRect;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }
    replayCommand.solidRect = command;
    gpuReplayCommands_.push_back(replayCommand);
    return true;
}

bool VulkanRenderTarget::TryRecordGpuFilledPolygonCommand(const std::vector<float>& points, int32_t fillRule, Brush* brush)
{
    (void)fillRule;

    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || points.size() < 6) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return false;
    }

    std::vector<float> triangleVertices;
    if (!TriangulateSimplePolygon(points, triangleVertices)) {
        return false;
    }

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::FilledPolygon;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    replayCommand.filledPolygon.triangleVertices = std::move(triangleVertices);
    replayCommand.filledPolygon.r = static_cast<float>(r) / 255.0f;
    replayCommand.filledPolygon.g = static_cast<float>(g) / 255.0f;
    replayCommand.filledPolygon.b = static_cast<float>(b) / 255.0f;
    replayCommand.filledPolygon.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.filledPolygon.a <= 0.0f) {
        return false;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuTransitionCommand(const std::vector<uint8_t>& fromPixels, const std::vector<uint8_t>& toPixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float progress, int mode)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || fromPixels.empty() || toPixels.empty() || pixelWidth == 0 || pixelHeight == 0 || w == 0.0f || h == 0.0f) {
        return false;
    }

    const size_t expectedSize = static_cast<size_t>(pixelWidth) * static_cast<size_t>(pixelHeight) * 4u;
    if (fromPixels.size() != expectedSize || toPixels.size() != expectedSize) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Transition;
    replayCommand.transition.pixelWidth = pixelWidth;
    replayCommand.transition.pixelHeight = pixelHeight;
    replayCommand.transition.fromPixels = fromPixels;
    replayCommand.transition.toPixels = toPixels;
    replayCommand.transition.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.transition.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.transition.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.transition.x;
    replayCommand.transition.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.transition.y;
    replayCommand.transition.progress = std::clamp(progress, 0.0f, 1.0f);
    replayCommand.transition.opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    replayCommand.transition.mode = mode;
    if (replayCommand.transition.w <= kEpsilon || replayCommand.transition.h <= kEpsilon || replayCommand.transition.opacity <= 0.0f) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuClearRectCommand(float x, float y, float w, float h)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }
    if (w == 0.0f || h == 0.0f) {
        return true;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::ClearRect;
    replayCommand.solidRect.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.solidRect.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.solidRect.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.solidRect.x;
    replayCommand.solidRect.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.solidRect.y;
    if (replayCommand.solidRect.w <= kEpsilon || replayCommand.solidRect.h <= kEpsilon) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

void VulkanRenderTarget::RecordCachedTextBitmap(std::shared_ptr<const std::vector<uint8_t>> pixels,
                                                int width, int height, float x, float y)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return;
    }
    if (!pixels || pixels->empty() || width <= 0 || height <= 0) {
        return;
    }

    const float fw = static_cast<float>(width);
    const float fh = static_cast<float>(height);
    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;

    float p0x = 0.0f, p0y = 0.0f;
    float p1x = 0.0f, p1y = 0.0f;
    float p2x = 0.0f, p2y = 0.0f;
    float p3x = 0.0f, p3y = 0.0f;
    ApplyTransform(transform, x,       y,       p0x, p0y);
    ApplyTransform(transform, x + fw,  y,       p1x, p1y);
    ApplyTransform(transform, x + fw,  y + fh,  p2x, p2y);
    ApplyTransform(transform, x,       y + fh,  p3x, p3y);

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::Bitmap;
    cmd.bitmap.pixelWidth = static_cast<uint32_t>(width);
    cmd.bitmap.pixelHeight = static_cast<uint32_t>(height);
    cmd.bitmap.sharedPixels = std::move(pixels);
    cmd.bitmap.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    cmd.bitmap.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    cmd.bitmap.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - cmd.bitmap.x;
    cmd.bitmap.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - cmd.bitmap.y;
    cmd.bitmap.opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);

    if (cmd.bitmap.w <= kEpsilon || cmd.bitmap.h <= kEpsilon || cmd.bitmap.opacity <= 0.0f) {
        return;
    }

    if (!TryPopulateReplayClip(cmd)) {
        return;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        cmd.hasCustomQuad = true;
        cmd.quadPoint0X = p0x;
        cmd.quadPoint0Y = p0y;
        cmd.quadPoint1X = p1x;
        cmd.quadPoint1Y = p1y;
        cmd.quadPoint2X = p2x;
        cmd.quadPoint2Y = p2y;
        cmd.quadPoint3X = p3x;
        cmd.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(cmd));
}

void VulkanRenderTarget::RasterizePolygonToGpuBitmap(
    const std::vector<float>& worldPoints,
    int fillRule,
    uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return;
    }
    if (worldPoints.size() < 6) {
        return;
    }

    const float opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (opacity <= 0.0f || a == 0) {
        return;
    }

    // Premultiply brush alpha by the current opacity stack.
    const uint8_t effA = static_cast<uint8_t>(static_cast<float>(a) * opacity + 0.5f);
    if (effA == 0) {
        return;
    }

    // World-space axis-aligned bounding box of the polygon points.
    float minX = worldPoints[0];
    float minY = worldPoints[1];
    float maxX = worldPoints[0];
    float maxY = worldPoints[1];
    for (size_t i = 2; i + 1 < worldPoints.size(); i += 2) {
        minX = std::min(minX, worldPoints[i]);
        minY = std::min(minY, worldPoints[i + 1]);
        maxX = std::max(maxX, worldPoints[i]);
        maxY = std::max(maxY, worldPoints[i + 1]);
    }

    // Intersect with any active scissor (from clipStack_) so the local buffer
    // is no bigger than what could possibly be visible.
    GpuReplayCommand probe {};
    probe.kind = GpuReplayCommandKind::Bitmap;
    if (!TryPopulateReplayClip(probe)) {
        return;
    }
    const float clipLeft = static_cast<float>(probe.scissorLeft);
    const float clipTop = static_cast<float>(probe.scissorTop);
    const float clipRight = static_cast<float>(probe.scissorRight);
    const float clipBottom = static_cast<float>(probe.scissorBottom);
    minX = std::max(minX, clipLeft);
    minY = std::max(minY, clipTop);
    maxX = std::min(maxX, clipRight);
    maxY = std::min(maxY, clipBottom);
    if (maxX - minX <= 0.5f || maxY - minY <= 0.5f) {
        return;
    }

    const int bboxLeft = static_cast<int>(std::floor(minX));
    const int bboxTop = static_cast<int>(std::floor(minY));
    const int bboxRight = static_cast<int>(std::ceil(maxX));
    const int bboxBottom = static_cast<int>(std::ceil(maxY));
    const int bw = bboxRight - bboxLeft;
    const int bh = bboxBottom - bboxTop;
    if (bw <= 0 || bh <= 0 || bw > 4096 || bh > 4096) {
        // Reject absurdly large paths — those are almost certainly a
        // mis-decoded stroke or a path that should have been clipped away.
        return;
    }

    auto pixels = std::make_shared<std::vector<uint8_t>>(
        static_cast<size_t>(bw) * static_cast<size_t>(bh) * 4u, 0);
    auto& buffer = *pixels;
    const size_t stride = static_cast<size_t>(bw) * 4u;
    const size_t vertexCount = worldPoints.size() / 2;

    // Premultiplied target color. SrcOver with an alpha=effA source, so the
    // output pixel is effectively (srcColor * effA / 255, effA). We write
    // alpha unblended — later, DrawReplayFrame's bitmap pipeline composites
    // the whole thing onto the swap-chain with SrcOver, so the final on-
    // screen color is correct.
    const uint32_t srcA = effA;

    for (int y = bboxTop; y < bboxBottom; ++y) {
        const float py = static_cast<float>(y) + 0.5f;
        const int localY = y - bboxTop;
        uint8_t* row = buffer.data() + static_cast<size_t>(localY) * stride;
        for (int x = bboxLeft; x < bboxRight; ++x) {
            const float px = static_cast<float>(x) + 0.5f;

            bool inside = false;
            if (fillRule == 1) {
                int winding = 0;
                for (size_t i = 0; i < vertexCount; ++i) {
                    const size_t j = (i + 1) % vertexCount;
                    const float x0 = worldPoints[i * 2];
                    const float y0 = worldPoints[i * 2 + 1];
                    const float x1 = worldPoints[j * 2];
                    const float y1 = worldPoints[j * 2 + 1];
                    if (y0 <= py) {
                        if (y1 > py && ((x1 - x0) * (py - y0) - (px - x0) * (y1 - y0)) > 0.0f) {
                            ++winding;
                        }
                    } else if (y1 <= py && ((x1 - x0) * (py - y0) - (px - x0) * (y1 - y0)) < 0.0f) {
                        --winding;
                    }
                }
                inside = winding != 0;
            } else {
                bool crossing = false;
                for (size_t i = 0, j = vertexCount - 1; i < vertexCount; j = i++) {
                    const float xi = worldPoints[i * 2];
                    const float yi = worldPoints[i * 2 + 1];
                    const float xj = worldPoints[j * 2];
                    const float yj = worldPoints[j * 2 + 1];
                    const bool intersect = ((yi > py) != (yj > py))
                        && (px < (xj - xi) * (py - yi) / ((yj - yi) == 0.0f ? 1.0f : (yj - yi)) + xi);
                    if (intersect) {
                        crossing = !crossing;
                    }
                }
                inside = crossing;
            }

            if (!inside) {
                continue;
            }

            const int localX = x - bboxLeft;
            uint8_t* dst = row + static_cast<size_t>(localX) * 4u;
            // Write premultiplied BGRA straight — buffer started zeroed.
            dst[0] = static_cast<uint8_t>((static_cast<uint32_t>(b) * srcA) / 255u);
            dst[1] = static_cast<uint8_t>((static_cast<uint32_t>(g) * srcA) / 255u);
            dst[2] = static_cast<uint8_t>((static_cast<uint32_t>(r) * srcA) / 255u);
            dst[3] = static_cast<uint8_t>(srcA);
        }
    }

    // Now emit a Bitmap replay command that points at buffer.
    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::Bitmap;
    cmd.bitmap.pixelWidth = static_cast<uint32_t>(bw);
    cmd.bitmap.pixelHeight = static_cast<uint32_t>(bh);
    cmd.bitmap.sharedPixels = std::shared_ptr<const std::vector<uint8_t>>(pixels);
    cmd.bitmap.x = static_cast<float>(bboxLeft);
    cmd.bitmap.y = static_cast<float>(bboxTop);
    cmd.bitmap.w = static_cast<float>(bw);
    cmd.bitmap.h = static_cast<float>(bh);
    // The pixels are already premultiplied with the current opacity, so the
    // per-bitmap opacity must be 1.0 — otherwise DrawReplayFrame would
    // multiply by opacity twice.
    cmd.bitmap.opacity = 1.0f;

    // Populate clip/scissor on the actual command (not the probe).
    if (!TryPopulateReplayClip(cmd)) {
        return;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return;
    }

    gpuReplayCommands_.push_back(std::move(cmd));
}

void VulkanRenderTarget::RasterizePolylineToGpuBitmap(
    const std::vector<float>& worldPoints,
    bool closed,
    float strokeWidth,
    uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return;
    }
    if (worldPoints.size() < 4 || strokeWidth <= 0.0f) {
        return;
    }

    const float opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (opacity <= 0.0f || a == 0) {
        return;
    }
    const uint8_t effA = static_cast<uint8_t>(static_cast<float>(a) * opacity + 0.5f);
    if (effA == 0) {
        return;
    }

    const int thickness = std::max(1, static_cast<int>(std::round(strokeWidth)));
    const int halfThick = thickness / 2;
    const float halfStrokeF = static_cast<float>(halfThick + 1);

    // Bounding box padded by half-stroke so stamp boxes fit.
    float minX = worldPoints[0];
    float minY = worldPoints[1];
    float maxX = worldPoints[0];
    float maxY = worldPoints[1];
    for (size_t i = 2; i + 1 < worldPoints.size(); i += 2) {
        minX = std::min(minX, worldPoints[i]);
        minY = std::min(minY, worldPoints[i + 1]);
        maxX = std::max(maxX, worldPoints[i]);
        maxY = std::max(maxY, worldPoints[i + 1]);
    }
    minX -= halfStrokeF;
    minY -= halfStrokeF;
    maxX += halfStrokeF;
    maxY += halfStrokeF;

    // Intersect with active scissor.
    GpuReplayCommand probe {};
    probe.kind = GpuReplayCommandKind::Bitmap;
    if (!TryPopulateReplayClip(probe)) {
        return;
    }
    minX = std::max(minX, static_cast<float>(probe.scissorLeft));
    minY = std::max(minY, static_cast<float>(probe.scissorTop));
    maxX = std::min(maxX, static_cast<float>(probe.scissorRight));
    maxY = std::min(maxY, static_cast<float>(probe.scissorBottom));
    if (maxX - minX <= 0.5f || maxY - minY <= 0.5f) {
        return;
    }

    const int bboxLeft = static_cast<int>(std::floor(minX));
    const int bboxTop = static_cast<int>(std::floor(minY));
    const int bboxRight = static_cast<int>(std::ceil(maxX));
    const int bboxBottom = static_cast<int>(std::ceil(maxY));
    const int bw = bboxRight - bboxLeft;
    const int bh = bboxBottom - bboxTop;
    if (bw <= 0 || bh <= 0 || bw > 4096 || bh > 4096) {
        return;
    }

    auto pixels = std::make_shared<std::vector<uint8_t>>(
        static_cast<size_t>(bw) * static_cast<size_t>(bh) * 4u, 0);
    auto& buffer = *pixels;
    const size_t stride = static_cast<size_t>(bw) * 4u;
    const uint32_t srcA = effA;

    auto stamp = [&](int cx, int cy) {
        const int x0 = std::max(bboxLeft,      cx - halfThick);
        const int y0 = std::max(bboxTop,       cy - halfThick);
        const int x1 = std::min(bboxRight,     cx - halfThick + thickness);
        const int y1 = std::min(bboxBottom,    cy - halfThick + thickness);
        for (int py = y0; py < y1; ++py) {
            uint8_t* row = buffer.data() + static_cast<size_t>(py - bboxTop) * stride;
            for (int px = x0; px < x1; ++px) {
                uint8_t* dst = row + static_cast<size_t>(px - bboxLeft) * 4u;
                dst[0] = static_cast<uint8_t>((static_cast<uint32_t>(b) * srcA) / 255u);
                dst[1] = static_cast<uint8_t>((static_cast<uint32_t>(g) * srcA) / 255u);
                dst[2] = static_cast<uint8_t>((static_cast<uint32_t>(r) * srcA) / 255u);
                dst[3] = static_cast<uint8_t>(srcA);
            }
        }
    };

    auto drawSegment = [&](float sx, float sy, float ex, float ey) {
        int x0 = static_cast<int>(std::round(sx));
        int y0 = static_cast<int>(std::round(sy));
        const int x1 = static_cast<int>(std::round(ex));
        const int y1 = static_cast<int>(std::round(ey));
        const int dx = std::abs(x1 - x0);
        const int sxStep = x0 < x1 ? 1 : -1;
        const int dy = -std::abs(y1 - y0);
        const int syStep = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true) {
            stamp(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            const int e2 = err * 2;
            if (e2 >= dy) { err += dy; x0 += sxStep; }
            if (e2 <= dx) { err += dx; y0 += syStep; }
        }
    };

    for (size_t i = 0; i + 3 < worldPoints.size(); i += 2) {
        drawSegment(worldPoints[i], worldPoints[i + 1], worldPoints[i + 2], worldPoints[i + 3]);
    }
    if (closed && worldPoints.size() >= 4) {
        drawSegment(worldPoints[worldPoints.size() - 2], worldPoints[worldPoints.size() - 1],
                    worldPoints[0], worldPoints[1]);
    }

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::Bitmap;
    cmd.bitmap.pixelWidth = static_cast<uint32_t>(bw);
    cmd.bitmap.pixelHeight = static_cast<uint32_t>(bh);
    cmd.bitmap.sharedPixels = std::shared_ptr<const std::vector<uint8_t>>(pixels);
    cmd.bitmap.x = static_cast<float>(bboxLeft);
    cmd.bitmap.y = static_cast<float>(bboxTop);
    cmd.bitmap.w = static_cast<float>(bw);
    cmd.bitmap.h = static_cast<float>(bh);
    cmd.bitmap.opacity = 1.0f;

    if (!TryPopulateReplayClip(cmd)) {
        return;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return;
    }

    gpuReplayCommands_.push_back(std::move(cmd));
}

bool VulkanRenderTarget::TryRecordGpuPixelBufferCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || pixels.empty() || pixelWidth == 0 || pixelHeight == 0 || opacity <= 0.0f || w == 0.0f || h == 0.0f) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Bitmap;
    replayCommand.bitmap.pixelWidth = pixelWidth;
    replayCommand.bitmap.pixelHeight = pixelHeight;
    replayCommand.bitmap.pixels = pixels;
    replayCommand.bitmap.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.bitmap.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.bitmap.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.bitmap.x;
    replayCommand.bitmap.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.bitmap.y;
    replayCommand.bitmap.opacity = std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.bitmap.w <= kEpsilon || replayCommand.bitmap.h <= kEpsilon || replayCommand.bitmap.opacity <= 0.0f) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuBlurCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint, float tintR, float tintG, float tintB, float tintA)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || pixels.empty() || pixelWidth == 0 || pixelHeight == 0 || w == 0.0f || h == 0.0f) {
        return false;
    }

    const size_t expectedSize = static_cast<size_t>(pixelWidth) * static_cast<size_t>(pixelHeight) * 4u;
    if (pixels.size() != expectedSize) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Blur;
    replayCommand.blur.pixelWidth = pixelWidth;
    replayCommand.blur.pixelHeight = pixelHeight;
    replayCommand.blur.pixels = pixels;
    replayCommand.blur.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.blur.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.blur.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.blur.x;
    replayCommand.blur.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.blur.y;
    replayCommand.blur.radius = std::clamp(radius, 0.0f, 12.0f);
    replayCommand.blur.opacity = std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f);
    replayCommand.blur.alphaOnlyTint = alphaOnlyTint;
    replayCommand.blur.tintR = std::clamp(tintR, 0.0f, 1.0f);
    replayCommand.blur.tintG = std::clamp(tintG, 0.0f, 1.0f);
    replayCommand.blur.tintB = std::clamp(tintB, 0.0f, 1.0f);
    replayCommand.blur.tintA = std::clamp(tintA, 0.0f, 1.0f);
    if (replayCommand.blur.w <= kEpsilon || replayCommand.blur.h <= kEpsilon || replayCommand.blur.opacity <= 0.0f) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuLiquidGlassCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || pixels.empty() || pixelWidth == 0 || pixelHeight == 0 || w == 0.0f || h == 0.0f) {
        return false;
    }

    const size_t expectedSize = static_cast<size_t>(pixelWidth) * static_cast<size_t>(pixelHeight) * 4u;
    if (pixels.size() != expectedSize) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::LiquidGlass;
    replayCommand.liquidGlass.pixelWidth = pixelWidth;
    replayCommand.liquidGlass.pixelHeight = pixelHeight;
    replayCommand.liquidGlass.pixels = pixels;
    replayCommand.liquidGlass.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.liquidGlass.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.liquidGlass.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.liquidGlass.x;
    replayCommand.liquidGlass.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.liquidGlass.y;
    replayCommand.liquidGlass.cornerRadius = cornerRadius;
    replayCommand.liquidGlass.blurRadius = blurRadius;
    replayCommand.liquidGlass.refractionAmount = refractionAmount;
    replayCommand.liquidGlass.chromaticAberration = chromaticAberration;
    replayCommand.liquidGlass.tintR = tintR;
    replayCommand.liquidGlass.tintG = tintG;
    replayCommand.liquidGlass.tintB = tintB;
    replayCommand.liquidGlass.tintOpacity = tintOpacity;
    replayCommand.liquidGlass.lightX = lightX;
    replayCommand.liquidGlass.lightY = lightY;
    replayCommand.liquidGlass.highlightBoost = highlightBoost;
    if (replayCommand.liquidGlass.w <= kEpsilon || replayCommand.liquidGlass.h <= kEpsilon) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuBackdropCommand(const std::vector<uint8_t>& pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL, float tintR, float tintG, float tintB, float tintOpacity, float saturation, float noiseIntensity)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || pixels.empty() || pixelWidth == 0 || pixelHeight == 0 || w == 0.0f || h == 0.0f) {
        return false;
    }

    const size_t expectedSize = static_cast<size_t>(pixelWidth) * static_cast<size_t>(pixelHeight) * 4u;
    if (pixels.size() != expectedSize) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f;
    float p0y = 0.0f;
    float p1x = 0.0f;
    float p1y = 0.0f;
    float p2x = 0.0f;
    float p2y = 0.0f;
    float p3x = 0.0f;
    float p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Backdrop;
    replayCommand.backdrop.pixelWidth = pixelWidth;
    replayCommand.backdrop.pixelHeight = pixelHeight;
    replayCommand.backdrop.pixels = pixels;
    replayCommand.backdrop.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.backdrop.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.backdrop.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.backdrop.x;
    replayCommand.backdrop.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.backdrop.y;
    replayCommand.backdrop.blurRadius = blurRadius;
    replayCommand.backdrop.cornerRadiusTL = cornerRadiusTL;
    replayCommand.backdrop.cornerRadiusTR = cornerRadiusTR;
    replayCommand.backdrop.cornerRadiusBR = cornerRadiusBR;
    replayCommand.backdrop.cornerRadiusBL = cornerRadiusBL;
    replayCommand.backdrop.tintR = tintR;
    replayCommand.backdrop.tintG = tintG;
    replayCommand.backdrop.tintB = tintB;
    replayCommand.backdrop.tintOpacity = tintOpacity;
    replayCommand.backdrop.saturation = saturation;
    replayCommand.backdrop.noiseIntensity = noiseIntensity;
    if (replayCommand.backdrop.w <= kEpsilon || replayCommand.backdrop.h <= kEpsilon) {
        return false;
    }

    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        replayCommand.hasCustomQuad = true;
        replayCommand.quadPoint0X = p0x;
        replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x;
        replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x;
        replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x;
        replayCommand.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuGlowCommand(float x, float y, float w, float h, float cornerRadius, float strokeWidth, float glowR, float glowG, float glowB, float glowA, float dimOpacity, float intensity)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }
    if (w <= 0.0f || h <= 0.0f) {
        return true;
    }

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Glow;
    replayCommand.glow.x = x;
    replayCommand.glow.y = y;
    replayCommand.glow.w = w;
    replayCommand.glow.h = h;
    replayCommand.glow.cornerRadius = cornerRadius;
    replayCommand.glow.strokeWidth = strokeWidth;
    replayCommand.glow.glowR = glowR;
    replayCommand.glow.glowG = glowG;
    replayCommand.glow.glowB = glowB;
    replayCommand.glow.glowA = glowA;
    replayCommand.glow.dimOpacity = dimOpacity;
    replayCommand.glow.intensity = intensity;
    gpuReplayCommands_.push_back(std::move(replayCommand));
    return true;
}

bool VulkanRenderTarget::TryRecordGpuDimOutsideRectCommand(float x, float y, float w, float h, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (a == 0) {
        return true;
    }

    VulkanSolidBrush dimBrush(
        static_cast<float>(r) / 255.0f,
        static_cast<float>(g) / 255.0f,
        static_cast<float>(b) / 255.0f,
        static_cast<float>(a) / 255.0f);

    const size_t originalCount = gpuReplayCommands_.size();
    const bool topOk = TryRecordGpuSolidRectCommand(0.0f, 0.0f, static_cast<float>(width_), y, &dimBrush);
    const bool leftOk = TryRecordGpuSolidRectCommand(0.0f, y, x, h, &dimBrush);
    const bool rightOk = TryRecordGpuSolidRectCommand(x + w, y, std::max(0.0f, static_cast<float>(width_) - (x + w)), h, &dimBrush);
    const bool bottomOk = TryRecordGpuSolidRectCommand(0.0f, y + h, static_cast<float>(width_), std::max(0.0f, static_cast<float>(height_) - (y + h)), &dimBrush);
    if (topOk && leftOk && rightOk && bottomOk) {
        return true;
    }

    gpuReplayCommands_.resize(originalCount);
    return false;
}

bool VulkanRenderTarget::TryRecordGpuRoundedRectFillCommand(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
    if (rx <= 0.0f && ry <= 0.0f) {
        return TryRecordGpuSolidRectCommand(x, y, w, h, brush);
    }

    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }
    if (w == 0.0f || h == 0.0f) {
        return true;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        return false;
    }

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::SolidRect;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.hasRoundedClip) {
        return false;
    }

    const float x0 = x * transform.m11 + transform.dx;
    const float y0 = y * transform.m22 + transform.dy;
    const float x1 = (x + w) * transform.m11 + transform.dx;
    const float y1 = (y + h) * transform.m22 + transform.dy;

    replayCommand.solidRect.x = std::min(x0, x1);
    replayCommand.solidRect.y = std::min(y0, y1);
    replayCommand.solidRect.w = std::fabs(x1 - x0);
    replayCommand.solidRect.h = std::fabs(y1 - y0);
    replayCommand.solidRect.r = static_cast<float>(r) / 255.0f;
    replayCommand.solidRect.g = static_cast<float>(g) / 255.0f;
    replayCommand.solidRect.b = static_cast<float>(b) / 255.0f;
    replayCommand.solidRect.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.solidRect.w <= kEpsilon || replayCommand.solidRect.h <= kEpsilon || replayCommand.solidRect.a <= 0.0f) {
        return true;
    }

    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    replayCommand.hasRoundedClip = true;
    replayCommand.roundedClipLeft = replayCommand.solidRect.x;
    replayCommand.roundedClipTop = replayCommand.solidRect.y;
    replayCommand.roundedClipRight = replayCommand.solidRect.x + replayCommand.solidRect.w;
    replayCommand.roundedClipBottom = replayCommand.solidRect.y + replayCommand.solidRect.h;
    replayCommand.roundedClipRadiusX = std::fabs(transform.m11) * std::min(rx, w * 0.5f);
    replayCommand.roundedClipRadiusY = std::fabs(transform.m22) * std::min(ry, h * 0.5f);
    gpuReplayCommands_.push_back(replayCommand);
    return true;
}

bool VulkanRenderTarget::TryRecordGpuRoundedRectStrokeCommand(float x, float y, float w, float h, float rx, float ry, float strokeWidth, Brush* brush)
{
    if (strokeWidth <= 0.0f) {
        return false;
    }
    if (rx <= 0.0f && ry <= 0.0f) {
        return TryRecordGpuRectangleStrokeCommand(x, y, w, h, strokeWidth, brush);
    }

    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }
    if (w == 0.0f || h == 0.0f) {
        return true;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        return false;
    }

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::SolidRect;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    if (replayCommand.hasRoundedClip || replayCommand.hasInnerRoundedClip) {
        return false;
    }

    const float halfStroke = strokeWidth * 0.5f;
    const float outerX = x - halfStroke;
    const float outerY = y - halfStroke;
    const float outerW = w + strokeWidth;
    const float outerH = h + strokeWidth;
    const float outerRx = std::max(0.0f, rx + halfStroke);
    const float outerRy = std::max(0.0f, ry + halfStroke);

    const float x0 = outerX * transform.m11 + transform.dx;
    const float y0 = outerY * transform.m22 + transform.dy;
    const float x1 = (outerX + outerW) * transform.m11 + transform.dx;
    const float y1 = (outerY + outerH) * transform.m22 + transform.dy;

    replayCommand.solidRect.x = std::min(x0, x1);
    replayCommand.solidRect.y = std::min(y0, y1);
    replayCommand.solidRect.w = std::fabs(x1 - x0);
    replayCommand.solidRect.h = std::fabs(y1 - y0);
    replayCommand.solidRect.r = static_cast<float>(r) / 255.0f;
    replayCommand.solidRect.g = static_cast<float>(g) / 255.0f;
    replayCommand.solidRect.b = static_cast<float>(b) / 255.0f;
    replayCommand.solidRect.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.solidRect.w <= kEpsilon || replayCommand.solidRect.h <= kEpsilon || replayCommand.solidRect.a <= 0.0f) {
        return true;
    }

    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    replayCommand.hasRoundedClip = true;
    replayCommand.roundedClipLeft = replayCommand.solidRect.x;
    replayCommand.roundedClipTop = replayCommand.solidRect.y;
    replayCommand.roundedClipRight = replayCommand.solidRect.x + replayCommand.solidRect.w;
    replayCommand.roundedClipBottom = replayCommand.solidRect.y + replayCommand.solidRect.h;
    replayCommand.roundedClipRadiusX = std::fabs(transform.m11) * std::min(outerRx, outerW * 0.5f);
    replayCommand.roundedClipRadiusY = std::fabs(transform.m22) * std::min(outerRy, outerH * 0.5f);

    const float innerW = w - strokeWidth;
    const float innerH = h - strokeWidth;
    if (innerW > kEpsilon && innerH > kEpsilon) {
        replayCommand.hasInnerRoundedClip = true;
        const float innerX = x + halfStroke;
        const float innerY = y + halfStroke;
        const float innerX0 = innerX * transform.m11 + transform.dx;
        const float innerY0 = innerY * transform.m22 + transform.dy;
        const float innerX1 = (innerX + innerW) * transform.m11 + transform.dx;
        const float innerY1 = (innerY + innerH) * transform.m22 + transform.dy;
        replayCommand.innerRoundedClipLeft = std::min(innerX0, innerX1);
        replayCommand.innerRoundedClipTop = std::min(innerY0, innerY1);
        replayCommand.innerRoundedClipRight = std::max(innerX0, innerX1);
        replayCommand.innerRoundedClipBottom = std::max(innerY0, innerY1);
        replayCommand.innerRoundedClipRadiusX = std::fabs(transform.m11) * std::max(0.0f, std::min(rx - halfStroke, innerW * 0.5f));
        replayCommand.innerRoundedClipRadiusY = std::fabs(transform.m22) * std::max(0.0f, std::min(ry - halfStroke, innerH * 0.5f));
    }

    gpuReplayCommands_.push_back(replayCommand);
    return true;
}

bool VulkanRenderTarget::TryRecordGpuEllipseFillCommand(float cx, float cy, float rx, float ry, Brush* brush)
{
    if (rx <= 0.0f || ry <= 0.0f) {
        return false;
    }

    return TryRecordGpuRoundedRectFillCommand(cx - rx, cy - ry, rx * 2.0f, ry * 2.0f, rx, ry, brush);
}

bool VulkanRenderTarget::TryRecordGpuEllipseStrokeCommand(float cx, float cy, float rx, float ry, float strokeWidth, Brush* brush)
{
    if (rx <= 0.0f || ry <= 0.0f) {
        return false;
    }

    return TryRecordGpuRoundedRectStrokeCommand(cx - rx, cy - ry, rx * 2.0f, ry * 2.0f, rx, ry, strokeWidth, brush);
}

bool VulkanRenderTarget::TryRecordGpuLineCommand(float x1, float y1, float x2, float y2, float strokeWidth, Brush* brush)
{
    if (strokeWidth <= 0.0f) {
        return false;
    }

    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        return false;
    }

    float worldX1 = 0.0f;
    float worldY1 = 0.0f;
    float worldX2 = 0.0f;
    float worldY2 = 0.0f;
    ApplyTransform(transform, x1, y1, worldX1, worldY1);
    ApplyTransform(transform, x2, y2, worldX2, worldY2);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::SolidRect;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }

    const float dx = worldX2 - worldX1;
    const float dy = worldY2 - worldY1;
    const float length = std::sqrt(dx * dx + dy * dy);
    if (length <= kEpsilon) {
        return true;
    }

    const float invLength = 1.0f / length;
    const float normalX = -dy * invLength * strokeWidth * 0.5f;
    const float normalY = dx * invLength * strokeWidth * 0.5f;

    replayCommand.hasCustomQuad = true;
    replayCommand.quadPoint0X = worldX1 - normalX;
    replayCommand.quadPoint0Y = worldY1 - normalY;
    replayCommand.quadPoint1X = worldX1 + normalX;
    replayCommand.quadPoint1Y = worldY1 + normalY;
    replayCommand.quadPoint2X = worldX2 + normalX;
    replayCommand.quadPoint2Y = worldY2 + normalY;
    replayCommand.quadPoint3X = worldX2 - normalX;
    replayCommand.quadPoint3Y = worldY2 - normalY;

    float minX = std::min(std::min(replayCommand.quadPoint0X, replayCommand.quadPoint1X), std::min(replayCommand.quadPoint2X, replayCommand.quadPoint3X));
    float minY = std::min(std::min(replayCommand.quadPoint0Y, replayCommand.quadPoint1Y), std::min(replayCommand.quadPoint2Y, replayCommand.quadPoint3Y));
    float maxX = std::max(std::max(replayCommand.quadPoint0X, replayCommand.quadPoint1X), std::max(replayCommand.quadPoint2X, replayCommand.quadPoint3X));
    float maxY = std::max(std::max(replayCommand.quadPoint0Y, replayCommand.quadPoint1Y), std::max(replayCommand.quadPoint2Y, replayCommand.quadPoint3Y));
    replayCommand.solidRect.x = minX;
    replayCommand.solidRect.y = minY;
    replayCommand.solidRect.w = maxX - minX;
    replayCommand.solidRect.h = maxY - minY;

    replayCommand.solidRect.r = static_cast<float>(r) / 255.0f;
    replayCommand.solidRect.g = static_cast<float>(g) / 255.0f;
    replayCommand.solidRect.b = static_cast<float>(b) / 255.0f;
    replayCommand.solidRect.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.solidRect.w <= kEpsilon || replayCommand.solidRect.h <= kEpsilon || replayCommand.solidRect.a <= 0.0f) {
        return true;
    }

    if (replayCommand.scissorRight <= replayCommand.scissorLeft || replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    gpuReplayCommands_.push_back(replayCommand);
    return true;
}

bool VulkanRenderTarget::TryRecordGpuPolylineCommand(const std::vector<float>& points, bool closed, float strokeWidth, Brush* brush)
{
    if (points.size() < 4) {
        return false;
    }

    const size_t originalCount = gpuReplayCommands_.size();
    for (size_t index = 0; index + 3 < points.size(); index += 2) {
        if (!TryRecordGpuLineCommand(points[index], points[index + 1], points[index + 2], points[index + 3], strokeWidth, brush)) {
            gpuReplayCommands_.resize(originalCount);
            return false;
        }
    }

    if (closed) {
        if (!TryRecordGpuLineCommand(points[points.size() - 2], points[points.size() - 1], points[0], points[1], strokeWidth, brush)) {
            gpuReplayCommands_.resize(originalCount);
            return false;
        }
    }

    return true;
}

bool VulkanRenderTarget::TryRecordGpuRectangleStrokeCommand(float x, float y, float w, float h, float strokeWidth, Brush* brush)
{
    if (strokeWidth <= 0.0f) {
        return false;
    }

    const size_t originalCount = gpuReplayCommands_.size();
    const float halfStroke = strokeWidth * 0.5f;
    const float innerHeight = std::max(0.0f, h - strokeWidth);

    const bool topOk = TryRecordGpuSolidRectCommand(x - halfStroke, y - halfStroke, w + strokeWidth, strokeWidth, brush);
    const bool bottomOk = TryRecordGpuSolidRectCommand(x - halfStroke, y + h - halfStroke, w + strokeWidth, strokeWidth, brush);
    if (innerHeight <= 0.0001f) {
        if (topOk && bottomOk) {
            return true;
        }
        gpuReplayCommands_.resize(originalCount);
        return false;
    }

    const bool leftOk = TryRecordGpuSolidRectCommand(x - halfStroke, y + halfStroke, strokeWidth, innerHeight, brush);
    const bool rightOk = TryRecordGpuSolidRectCommand(x + w - halfStroke, y + halfStroke, strokeWidth, innerHeight, brush);
    if (topOk && bottomOk && leftOk && rightOk) {
        return true;
    }

    gpuReplayCommands_.resize(originalCount);
    return false;
}

bool VulkanRenderTarget::TryRecordGpuBitmapCommand(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
    const auto* sourceBitmap = static_cast<const VulkanBitmap*>(bitmap);
    if (!sourceBitmap || sourceBitmap->GetWidth() == 0 || sourceBitmap->GetHeight() == 0 || sourceBitmap->GetPixels().empty()) {
        return false;
    }

    return TryRecordGpuPixelBufferCommand(sourceBitmap->GetPixels(), sourceBitmap->GetWidth(), sourceBitmap->GetHeight(), x, y, w, h, opacity);
}

void VulkanRenderTarget::RasterizePolygon(const std::vector<float>& points, int fillRule, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    if (points.size() < 6) {
        return;
    }

    float minX = points[0];
    float minY = points[1];
    float maxX = points[0];
    float maxY = points[1];
    for (size_t i = 2; i + 1 < points.size(); i += 2) {
        minX = std::min(minX, points[i]);
        minY = std::min(minY, points[i + 1]);
        maxX = std::max(maxX, points[i]);
        maxY = std::max(maxY, points[i + 1]);
    }

    const int left = static_cast<int>(std::floor(minX));
    const int top = static_cast<int>(std::floor(minY));
    const int right = static_cast<int>(std::ceil(maxX));
    const int bottom = static_cast<int>(std::ceil(maxY));
    const size_t vertexCount = points.size() / 2;

    for (int y = top; y < bottom; ++y) {
        for (int x = left; x < right; ++x) {
            const float px = static_cast<float>(x) + 0.5f;
            const float py = static_cast<float>(y) + 0.5f;

            bool inside = false;
            if (fillRule == 1) {
                int winding = 0;
                for (size_t i = 0; i < vertexCount; ++i) {
                    const size_t j = (i + 1) % vertexCount;
                    const float x0 = points[i * 2];
                    const float y0 = points[i * 2 + 1];
                    const float x1 = points[j * 2];
                    const float y1 = points[j * 2 + 1];
                    if (y0 <= py) {
                        if (y1 > py && ((x1 - x0) * (py - y0) - (px - x0) * (y1 - y0)) > 0.0f) {
                            ++winding;
                        }
                    } else if (y1 <= py && ((x1 - x0) * (py - y0) - (px - x0) * (y1 - y0)) < 0.0f) {
                        --winding;
                    }
                }
                inside = winding != 0;
            } else {
                bool crossing = false;
                for (size_t i = 0, j = vertexCount - 1; i < vertexCount; j = i++) {
                    const float xi = points[i * 2];
                    const float yi = points[i * 2 + 1];
                    const float xj = points[j * 2];
                    const float yj = points[j * 2 + 1];
                    const bool intersect = ((yi > py) != (yj > py))
                        && (px < (xj - xi) * (py - yi) / ((yj - yi) == 0.0f ? 1.0f : (yj - yi)) + xi);
                    if (intersect) {
                        crossing = !crossing;
                    }
                }
                inside = crossing;
            }

            if (inside) {
                BlendPixel(x, y, b, g, r, a);
            }
        }
    }
}

void VulkanRenderTarget::StrokePolyline(const std::vector<float>& points, bool closed, float strokeWidth, uint8_t b, uint8_t g, uint8_t r, uint8_t a)
{
    if (!cpuRasterNeeded_) {
        return;
    }
    if (points.size() < 4) {
        return;
    }

    const int thickness = std::max(1, static_cast<int>(std::round(strokeWidth)));
    const size_t segmentCount = points.size() / 2;

    auto drawSegment = [&](float startX, float startY, float endX, float endY) {
        int xStart = static_cast<int>(std::round(startX));
        int yStart = static_cast<int>(std::round(startY));
        const int xEnd = static_cast<int>(std::round(endX));
        const int yEnd = static_cast<int>(std::round(endY));

        const int dx = std::abs(xEnd - xStart);
        const int sx = xStart < xEnd ? 1 : -1;
        const int dy = -std::abs(yEnd - yStart);
        const int sy = yStart < yEnd ? 1 : -1;
        int error = dx + dy;

        while (true) {
            FillSolidRect(
                xStart - thickness / 2,
                yStart - thickness / 2,
                xStart - thickness / 2 + thickness,
                yStart - thickness / 2 + thickness,
                b, g, r, a);
            if (xStart == xEnd && yStart == yEnd) {
                break;
            }
            const int twiceError = error * 2;
            if (twiceError >= dy) {
                error += dy;
                xStart += sx;
            }
            if (twiceError <= dx) {
                error += dx;
                yStart += sy;
            }
        }
    };

    for (size_t i = 0; i + 3 < points.size(); i += 2) {
        drawSegment(points[i], points[i + 1], points[i + 2], points[i + 3]);
    }

    if (closed) {
        drawSegment(points[points.size() - 2], points[points.size() - 1], points[0], points[1]);
    }
}

void VulkanRenderTarget::FillRectangle(float x, float y, float w, float h, Brush* brush)
{
    TouchFrame();
    // A null brush is a "no-op" fill (callers use it as a transparent hit area
    // or to stake out layout space). Don't route it through TryRecord→
    // Invalidate — that would collapse the entire frame's GPU replay path
    // onto the CPU upload fallback for what is visually a no-op.
    if (!brush) {
        return;
    }
    if (!TryRecordGpuSolidRectCommand(x, y, w, h, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points(8);
    ApplyTransform(transform, x, y, points[0], points[1]);
    ApplyTransform(transform, x + w, y, points[2], points[3]);
    ApplyTransform(transform, x + w, y + h, points[4], points[5]);
    ApplyTransform(transform, x, y + h, points[6], points[7]);
    RasterizePolygon(points, 0, b, g, r, a);
}

void VulkanRenderTarget::DrawRectangle(float x, float y, float w, float h, Brush* brush, float strokeWidth)
{
    TouchFrame();
    if (!brush) {
        return;
    }
    if (!TryRecordGpuRectangleStrokeCommand(x, y, w, h, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points(8);
    ApplyTransform(transform, x, y, points[0], points[1]);
    ApplyTransform(transform, x + w, y, points[2], points[3]);
    ApplyTransform(transform, x + w, y + h, points[4], points[5]);
    ApplyTransform(transform, x, y + h, points[6], points[7]);
    StrokePolyline(points, true, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
    TouchFrame();
    if (!TryRecordGpuRoundedRectFillCommand(x, y, w, h, rx, ry, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if ((rx <= 0.0f && ry <= 0.0f) || !brush) {
        FillRectangle(x, y, w, h, brush);
        return;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    // Apply the current transform to map DIP coordinates to physical pixels.
    // FillSolidRect iterates over pixel indices in the pixelBuffer_, so coordinates
    // must be in physical pixel space. The rounded clip stores the transform and
    // maps pixel positions back to local space via IsInsideClip.
    const auto transform = GetCurrentTransform();
    float px0, py0, px1, py1;
    ApplyTransform(transform, x, y, px0, py0);
    ApplyTransform(transform, x + w, y + h, px1, py1);

    PushTemporaryClip(x, y, w, h, rx, ry);
    FillSolidRect(
        static_cast<int>(std::floor(std::min(px0, px1))),
        static_cast<int>(std::floor(std::min(py0, py1))),
        static_cast<int>(std::ceil(std::max(px0, px1))),
        static_cast<int>(std::ceil(std::max(py0, py1))),
        b, g, r, a);
    PopTemporaryClip();
}

void VulkanRenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth)
{
    TouchFrame();
    if (!TryRecordGpuRoundedRectStrokeCommand(x, y, w, h, rx, ry, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if ((rx <= 0.0f && ry <= 0.0f) || !brush) {
        DrawRectangle(x, y, w, h, brush, strokeWidth);
        return;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    StrokeRoundedRectApprox(x, y, w, h, rx, ry, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
    TouchFrame();
    if (!TryRecordGpuEllipseFillCommand(cx, cy, rx, ry, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a) || rx <= 0 || ry <= 0) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(64);
    for (int index = 0; index < 32; ++index) {
        const float angle = static_cast<float>(index) / 32.0f * 6.28318530718f;
        float worldX = 0.0f;
        float worldY = 0.0f;
        ApplyTransform(transform, cx + std::cos(angle) * rx, cy + std::sin(angle) * ry, worldX, worldY);
        points.push_back(worldX);
        points.push_back(worldY);
    }
    RasterizePolygon(points, 0, b, g, r, a);
}

void VulkanRenderTarget::DrawEllipse(float cx, float cy, float rx, float ry, Brush* brush, float strokeWidth)
{
    TouchFrame();
    if (!TryRecordGpuEllipseStrokeCommand(cx, cy, rx, ry, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a) || rx <= 0 || ry <= 0) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(64);
    for (int index = 0; index < 32; ++index) {
        const float angle = static_cast<float>(index) / 32.0f * 6.28318530718f;
        float worldX = 0.0f;
        float worldY = 0.0f;
        ApplyTransform(transform, cx + std::cos(angle) * rx, cy + std::sin(angle) * ry, worldX, worldY);
        points.push_back(worldX);
        points.push_back(worldY);
    }
    StrokePolyline(points, true, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth)
{
    TouchFrame();
    if (!TryRecordGpuLineCommand(x1, y1, x2, y2, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points(4);
    ApplyTransform(transform, x1, y1, points[0], points[1]);
    ApplyTransform(transform, x2, y2, points[2], points[3]);
    StrokePolyline(points, false, strokeWidth, b, g, r, a);
}
void VulkanRenderTarget::FillPolygon(const float* points, uint32_t pointCount, Brush* brush, int32_t fillRule)
{
    TouchFrame();

    // Route through Impeller engine when active
    if (IsImpellerActive() && impellerEngine_ && points && pointCount >= 3) {
        uint8_t br = 0, bg = 0, bb = 0, ba = 0;
        if (TryGetSolidBrushColor(brush, bb, bg, br, ba)) {
            auto t = GetCurrentTransform();
            float opacity = GetCurrentOpacity();

            EngineBrushData bd;
            bd.type = 0;
            bd.r = br / 255.0f; bd.g = bg / 255.0f; bd.b = bb / 255.0f; bd.a = (ba / 255.0f) * opacity;

            EngineTransform et;
            et.m11 = t.m11; et.m12 = t.m12;
            et.m21 = t.m21; et.m22 = t.m22;
            et.dx = t.dx; et.dy = t.dy;

            FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
            if (impellerEngine_->EncodeFillPolygon(points, pointCount, bd, fr, et))
                return;
        }
    }

    if (points && pointCount >= 3) {
        std::vector<float> localPoints;
        localPoints.reserve(pointCount * 2);
        for (uint32_t index = 0; index < pointCount; ++index) {
            localPoints.push_back(points[index * 2]);
            localPoints.push_back(points[index * 2 + 1]);
        }
        const auto transform = GetCurrentTransform();
        std::vector<float> transformedPoints;
        transformedPoints.reserve(pointCount * 2);
        for (uint32_t index = 0; index < pointCount; ++index) {
            float worldX = 0.0f;
            float worldY = 0.0f;
            ApplyTransform(transform, localPoints[index * 2], localPoints[index * 2 + 1], worldX, worldY);
            transformedPoints.push_back(worldX);
            transformedPoints.push_back(worldY);
        }
        if (!TryRecordGpuFilledPolygonCommand(transformedPoints, fillRule, brush)) {
            // Ear-clipping triangulation bailed out (self-intersecting path,
            // multi-contour path, or non-axis-aligned transform). CPU-
            // rasterize into a local bitmap and record it as a GPU bitmap
            // command so the frame stays on the replay path. Typical
            // PathIcon shapes are <= 64x64 px, so this is ~1 ms at most.
            uint8_t bc = 0, gc = 0, rc = 0, ac = 0;
            if (TryGetApproximateBrushColor(brush, bc, gc, rc, ac)) {
                RasterizePolygonToGpuBitmap(transformedPoints, fillRule, bc, gc, rc, ac);
            }
        }
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!points || pointCount < 3 || !TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> transformedPoints;
    transformedPoints.reserve(pointCount * 2);
    for (uint32_t index = 0; index < pointCount; ++index) {
        float worldX = 0.0f;
        float worldY = 0.0f;
        ApplyTransform(transform, points[index * 2], points[index * 2 + 1], worldX, worldY);
        transformedPoints.push_back(worldX);
        transformedPoints.push_back(worldY);
    }
    RasterizePolygon(transformedPoints, fillRule, b, g, r, a);
}

void VulkanRenderTarget::DrawPolygon(const float* points, uint32_t pointCount, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit)
{
    TouchFrame();
    if (points && pointCount >= 2) {
        std::vector<float> localPoints;
        localPoints.reserve(pointCount * 2);
        for (uint32_t index = 0; index < pointCount; ++index) {
            localPoints.push_back(points[index * 2]);
            localPoints.push_back(points[index * 2 + 1]);
        }
        if (!TryRecordGpuPolylineCommand(localPoints, closed, strokeWidth, brush)) {
            // Same fallback as FillPolygon — CPU-rasterize the stroked
            // polyline into a local bitmap. Points need to be in world space
            // for the rasterizer, so re-transform them here.
            uint8_t bc = 0, gc = 0, rc = 0, ac = 0;
            if (TryGetApproximateBrushColor(brush, bc, gc, rc, ac)) {
                const auto transform = GetCurrentTransform();
                std::vector<float> worldPts;
                worldPts.reserve(localPoints.size());
                for (size_t i = 0; i + 1 < localPoints.size(); i += 2) {
                    float wx = 0.0f, wy = 0.0f;
                    ApplyTransform(transform, localPoints[i], localPoints[i + 1], wx, wy);
                    worldPts.push_back(wx);
                    worldPts.push_back(wy);
                }
                RasterizePolylineToGpuBitmap(worldPts, closed, strokeWidth, bc, gc, rc, ac);
            }
        }
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!points || pointCount < 2 || !TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> transformedPoints;
    transformedPoints.reserve(pointCount * 2);
    for (uint32_t index = 0; index < pointCount; ++index) {
        float worldX = 0.0f;
        float worldY = 0.0f;
        ApplyTransform(transform, points[index * 2], points[index * 2 + 1], worldX, worldY);
        transformedPoints.push_back(worldX);
        transformedPoints.push_back(worldY);
    }
    StrokePolyline(transformedPoints, closed, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule)
{
    TouchFrame();

    // Route through Impeller engine when active
    if (IsImpellerActive() && impellerEngine_) {
        uint8_t br = 0, bg = 0, bb = 0, ba = 0;
        if (TryGetSolidBrushColor(brush, bb, bg, br, ba)) {
            auto t = GetCurrentTransform();
            float opacity = GetCurrentOpacity();

            EngineBrushData bd;
            bd.type = 0;
            bd.r = br / 255.0f; bd.g = bg / 255.0f; bd.b = bb / 255.0f; bd.a = (ba / 255.0f) * opacity;

            EngineTransform et;
            et.m11 = t.m11; et.m12 = t.m12;
            et.m21 = t.m21; et.m22 = t.m22;
            et.dx = t.dx; et.dy = t.dy;

            FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
            if (impellerEngine_->EncodeFillPath(startX, startY, commands, commandLength, bd, fr, et))
                return;
        }
    }

    std::vector<float> localPoints;
    localPoints.reserve(commandLength * 2);

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!commands || commandLength == 0 || !TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(commandLength * 2);

    float currentX = startX;
    float currentY = startY;
    float worldX = 0.0f;
    float worldY = 0.0f;
    localPoints.push_back(currentX);
    localPoints.push_back(currentY);
    ApplyTransform(transform, currentX, currentY, worldX, worldY);
    points.push_back(worldX);
    points.push_back(worldY);

    uint32_t index = 0;
    while (index < commandLength) {
        const int tag = static_cast<int>(commands[index++]);
        if (tag == 0 && index + 1 < commandLength) {
            currentX = commands[index++];
            currentY = commands[index++];
            localPoints.push_back(currentX);
            localPoints.push_back(currentY);
            ApplyTransform(transform, currentX, currentY, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
        } else if (tag == 1 && index + 5 < commandLength) {
            const float cp1x = commands[index++];
            const float cp1y = commands[index++];
            const float cp2x = commands[index++];
            const float cp2y = commands[index++];
            const float endX = commands[index++];
            const float endY = commands[index++];
            for (int step = 1; step <= 16; ++step) {
                const float t = static_cast<float>(step) / 16.0f;
                const float omt = 1.0f - t;
                const float bezierX =
                    omt * omt * omt * currentX +
                    3.0f * omt * omt * t * cp1x +
                    3.0f * omt * t * t * cp2x +
                    t * t * t * endX;
                const float bezierY =
                    omt * omt * omt * currentY +
                    3.0f * omt * omt * t * cp1y +
                    3.0f * omt * t * t * cp2y +
                    t * t * t * endY;
                localPoints.push_back(bezierX);
                localPoints.push_back(bezierY);
                ApplyTransform(transform, bezierX, bezierY, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }
            currentX = endX;
            currentY = endY;
        } else if (tag == 2 && index + 1 < commandLength) {
            // MoveTo: new sub-path
            currentX = commands[index++];
            currentY = commands[index++];
            localPoints.push_back(currentX);
            localPoints.push_back(currentY);
            ApplyTransform(transform, currentX, currentY, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
        } else if (tag == 3 && index + 3 < commandLength) {
            // QuadTo [cx, cy, ex, ey]
            const float cpx = commands[index++];
            const float cpy = commands[index++];
            const float endX = commands[index++];
            const float endY = commands[index++];
            for (int step = 1; step <= 8; ++step) {
                const float t = static_cast<float>(step) / 8.0f;
                const float omt = 1.0f - t;
                const float qx = omt * omt * currentX + 2.0f * omt * t * cpx + t * t * endX;
                const float qy = omt * omt * currentY + 2.0f * omt * t * cpy + t * t * endY;
                localPoints.push_back(qx);
                localPoints.push_back(qy);
                ApplyTransform(transform, qx, qy, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }
            currentX = endX;
            currentY = endY;
        } else if (tag == 5) {
            // ClosePath: add line back to the sub-path start if not already there
            float firstX = localPoints[0], firstY = localPoints[1];
            if (std::abs(currentX - firstX) > 1e-4f || std::abs(currentY - firstY) > 1e-4f) {
                currentX = firstX;
                currentY = firstY;
                localPoints.push_back(currentX);
                localPoints.push_back(currentY);
                ApplyTransform(transform, currentX, currentY, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }
        } else {
            break;
        }
    }

    if (!TryRecordGpuFilledPolygonCommand(points, fillRule, brush)) {
        // Path can't be triangulated (typical for SVG icons with multiple
        // subpaths or self-intersecting glyph outlines). Rasterize the path
        // into a local bitmap and record it as a GPU bitmap command, which
        // keeps the frame on the replay path and still shows the icon.
        RasterizePolygonToGpuBitmap(points, fillRule, b, g, r, a);
    }

    RasterizePolygon(points, fillRule, b, g, r, a);
}

void VulkanRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset)
{
    TouchFrame();

    // Route through Impeller engine when active
    if (IsImpellerActive() && impellerEngine_) {
        uint8_t br = 0, bg = 0, bb = 0, ba = 0;
        if (TryGetSolidBrushColor(brush, bb, bg, br, ba)) {
            auto t = GetCurrentTransform();
            float opacity = GetCurrentOpacity();

            EngineBrushData bd;
            bd.type = 0;
            bd.r = br / 255.0f; bd.g = bg / 255.0f; bd.b = bb / 255.0f; bd.a = (ba / 255.0f) * opacity;

            EngineTransform et;
            et.m11 = t.m11; et.m12 = t.m12;
            et.m21 = t.m21; et.m22 = t.m22;
            et.dx = t.dx; et.dy = t.dy;

            if (impellerEngine_->EncodeStrokePath(startX, startY, commands, commandLength,
                    bd, strokeWidth, closed, lineJoin, miterLimit, lineCap,
                    dashPattern, dashCount, dashOffset, et))
                return;
        }
    }

    std::vector<float> localPoints;
    localPoints.reserve(commandLength * 2);

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!commands || commandLength == 0 || !TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(commandLength * 2);

    float currentX = startX;
    float currentY = startY;
    float worldX = 0.0f;
    float worldY = 0.0f;
    localPoints.push_back(currentX);
    localPoints.push_back(currentY);
    ApplyTransform(transform, currentX, currentY, worldX, worldY);
    points.push_back(worldX);
    points.push_back(worldY);

    uint32_t index = 0;
    while (index < commandLength) {
        const int tag = static_cast<int>(commands[index++]);
        if (tag == 0 && index + 1 < commandLength) {
            currentX = commands[index++];
            currentY = commands[index++];
            localPoints.push_back(currentX);
            localPoints.push_back(currentY);
            ApplyTransform(transform, currentX, currentY, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
        } else if (tag == 1 && index + 5 < commandLength) {
            const float cp1x = commands[index++];
            const float cp1y = commands[index++];
            const float cp2x = commands[index++];
            const float cp2y = commands[index++];
            const float endX = commands[index++];
            const float endY = commands[index++];
            for (int step = 1; step <= 16; ++step) {
                const float t = static_cast<float>(step) / 16.0f;
                const float omt = 1.0f - t;
                const float bezierX =
                    omt * omt * omt * currentX +
                    3.0f * omt * omt * t * cp1x +
                    3.0f * omt * t * t * cp2x +
                    t * t * t * endX;
                const float bezierY =
                    omt * omt * omt * currentY +
                    3.0f * omt * omt * t * cp1y +
                    3.0f * omt * t * t * cp2y +
                    t * t * t * endY;
                localPoints.push_back(bezierX);
                localPoints.push_back(bezierY);
                ApplyTransform(transform, bezierX, bezierY, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }
            currentX = endX;
            currentY = endY;
        } else if (tag == 2 && index + 1 < commandLength) {
            // MoveTo: new sub-path
            currentX = commands[index++];
            currentY = commands[index++];
            localPoints.push_back(currentX);
            localPoints.push_back(currentY);
            ApplyTransform(transform, currentX, currentY, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
        } else if (tag == 3 && index + 3 < commandLength) {
            // QuadTo [cx, cy, ex, ey]
            const float cpx = commands[index++];
            const float cpy = commands[index++];
            const float endX = commands[index++];
            const float endY = commands[index++];
            for (int step = 1; step <= 8; ++step) {
                const float t = static_cast<float>(step) / 8.0f;
                const float omt = 1.0f - t;
                const float qx = omt * omt * currentX + 2.0f * omt * t * cpx + t * t * endX;
                const float qy = omt * omt * currentY + 2.0f * omt * t * cpy + t * t * endY;
                localPoints.push_back(qx);
                localPoints.push_back(qy);
                ApplyTransform(transform, qx, qy, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }
            currentX = endX;
            currentY = endY;
        } else if (tag == 5) {
            // ClosePath
            closed = true;
        } else {
            break;
        }
    }

    if (!TryRecordGpuPolylineCommand(localPoints, closed, strokeWidth, brush)) {
        // Stroke couldn't be expressed as a polyline command (e.g. dashed
        // stroke, non-miter join, or just long enough to exceed
        // TryRecordGpuLineCommand's per-segment budget). Rasterize into a
        // local bitmap so the outline still shows up.
        RasterizePolylineToGpuBitmap(points, closed, strokeWidth, b, g, r, a);
    }

    StrokePolyline(points, closed, strokeWidth, b, g, r, a);
}

void VulkanRenderTarget::DrawContentBorder(float x, float y, float w, float h, float blRadius, float brRadius, Brush* fillBrush, Brush* strokeBrush, float strokeWidth)
{
    TouchFrame();
    const float maxRadius = std::min(w, h) * 0.5f;
    const float bl = std::clamp(blRadius, 0.0f, maxRadius);
    const float br = std::clamp(brRadius, 0.0f, maxRadius);

    if (fillBrush) {
        uint8_t b = 0, g = 0, r = 0, a = 0;
        if (TryGetSolidBrushColor(fillBrush, b, g, r, a)) {
            const auto transform = GetCurrentTransform();
            std::vector<float> localPoints;
            localPoints.reserve(48);
            std::vector<float> points;
            points.reserve(48);

            float worldX = 0.0f;
            float worldY = 0.0f;
            localPoints.push_back(x);
            localPoints.push_back(y);
            ApplyTransform(transform, x, y, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
            localPoints.push_back(x + w);
            localPoints.push_back(y);
            ApplyTransform(transform, x + w, y, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
            localPoints.push_back(x + w);
            localPoints.push_back(y + h - br);
            ApplyTransform(transform, x + w, y + h - br, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);

            if (br > 0.0f) {
                for (int step = 1; step <= 8; ++step) {
                    const float angle = (static_cast<float>(step) / 8.0f) * 1.57079632679f;
                    const float px = x + w - br + std::cos(angle) * br;
                    const float py = y + h - br + std::sin(angle) * br;
                    localPoints.push_back(px);
                    localPoints.push_back(py);
                    ApplyTransform(transform, px, py, worldX, worldY);
                    points.push_back(worldX);
                    points.push_back(worldY);
                }
            } else {
                localPoints.push_back(x + w);
                localPoints.push_back(y + h);
                ApplyTransform(transform, x + w, y + h, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }

            localPoints.push_back(x + bl);
            localPoints.push_back(y + h);
            ApplyTransform(transform, x + bl, y + h, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);

            if (bl > 0.0f) {
                for (int step = 1; step <= 8; ++step) {
                    const float angle = 1.57079632679f + (static_cast<float>(step) / 8.0f) * 1.57079632679f;
                    const float px = x + bl + std::cos(angle) * bl;
                    const float py = y + h - bl + std::sin(angle) * bl;
                    localPoints.push_back(px);
                    localPoints.push_back(py);
                    ApplyTransform(transform, px, py, worldX, worldY);
                    points.push_back(worldX);
                    points.push_back(worldY);
                }
            } else {
                localPoints.push_back(x);
                localPoints.push_back(y + h);
                ApplyTransform(transform, x, y + h, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }

            if (!TryRecordGpuFilledPolygonCommand(points, 1, fillBrush)) {
                /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
            }
            RasterizePolygon(points, 1, b, g, r, a);
        }
    }

    if (strokeBrush) {
        uint8_t b = 0, g = 0, r = 0, a = 0;
        if (TryGetSolidBrushColor(strokeBrush, b, g, r, a)) {
            const auto transform = GetCurrentTransform();
            std::vector<float> localPoints;
            localPoints.reserve(40);
            std::vector<float> points;
            points.reserve(40);

            float worldX = 0.0f;
            float worldY = 0.0f;
            localPoints.push_back(x);
            localPoints.push_back(y);
            ApplyTransform(transform, x, y, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
            localPoints.push_back(x);
            localPoints.push_back(y + h - bl);
            ApplyTransform(transform, x, y + h - bl, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);

            if (bl > 0.0f) {
                for (int step = 1; step <= 8; ++step) {
                    const float angle = 3.14159265359f / 2.0f + (static_cast<float>(step) / 8.0f) * 1.57079632679f;
                    const float px = x + bl + std::cos(angle) * bl;
                    const float py = y + h - bl + std::sin(angle) * bl;
                    localPoints.push_back(px);
                    localPoints.push_back(py);
                    ApplyTransform(transform, px, py, worldX, worldY);
                    points.push_back(worldX);
                    points.push_back(worldY);
                }
            } else {
                localPoints.push_back(x);
                localPoints.push_back(y + h);
                ApplyTransform(transform, x, y + h, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }

            localPoints.push_back(x + w - br);
            localPoints.push_back(y + h);
            ApplyTransform(transform, x + w - br, y + h, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);

            if (br > 0.0f) {
                for (int step = 1; step <= 8; ++step) {
                    const float angle = static_cast<float>(step) / 8.0f * 1.57079632679f;
                    const float px = x + w - br + std::cos(angle) * br;
                    const float py = y + h - br + std::sin(angle) * br;
                    localPoints.push_back(px);
                    localPoints.push_back(py);
                    ApplyTransform(transform, px, py, worldX, worldY);
                    points.push_back(worldX);
                    points.push_back(worldY);
                }
            } else {
                localPoints.push_back(x + w);
                localPoints.push_back(y + h);
                ApplyTransform(transform, x + w, y + h, worldX, worldY);
                points.push_back(worldX);
                points.push_back(worldY);
            }

            localPoints.push_back(x + w);
            localPoints.push_back(y);
            ApplyTransform(transform, x + w, y, worldX, worldY);
            points.push_back(worldX);
            points.push_back(worldY);
            if (!TryRecordGpuPolylineCommand(localPoints, false, strokeWidth, strokeBrush)) {
                /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
            }
            StrokePolyline(points, false, strokeWidth, b, g, r, a);
        }
    }
}
void VulkanRenderTarget::PushTransform(const float* matrix)
{
    TouchFrame();
    if (!matrix) {
        return;
    }

    CpuTransform transform {};
    transform.m11 = matrix[0];
    transform.m12 = matrix[1];
    transform.m21 = matrix[2];
    transform.m22 = matrix[3];
    transform.dx = matrix[4];
    transform.dy = matrix[5];

    transformStack_.push_back(MultiplyTransforms(GetCurrentTransform(), transform));
}

void VulkanRenderTarget::PopTransform()
{
    TouchFrame();
    if (transformStack_.size() > 1) {
        transformStack_.pop_back();
    }
}

void VulkanRenderTarget::PushClip(float x, float y, float w, float h)
{
    TouchFrame();

    ClipState clip {};
    clip.rounded = false;
    clip.x = x;
    clip.y = y;
    clip.w = w;
    clip.h = h;
    clip.transform = GetCurrentTransform();
    clip.hasInverse = TryInvertTransform(GetCurrentTransform(), clip.inverseTransform);
    clipStack_.push_back(clip);
}

void VulkanRenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry)
{
    TouchFrame();

    ClipState clip {};
    clip.rounded = true;
    clip.x = x;
    clip.y = y;
    clip.w = w;
    clip.h = h;
    clip.rx = rx;
    clip.ry = ry;
    clip.transform = GetCurrentTransform();
    clip.hasInverse = TryInvertTransform(GetCurrentTransform(), clip.inverseTransform);
    clipStack_.push_back(clip);
}

void VulkanRenderTarget::PopClip()
{
    TouchFrame();
    if (!clipStack_.empty()) {
        clipStack_.pop_back();
    }
}
void VulkanRenderTarget::PunchTransparentRect(float x, float y, float w, float h)
{
    TouchFrame();
    if (!TryRecordGpuClearRectCommand(x, y, w, h)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if (!cpuRasterNeeded_) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> quad(8);
    ApplyTransform(transform, x, y, quad[0], quad[1]);
    ApplyTransform(transform, x + w, y, quad[2], quad[3]);
    ApplyTransform(transform, x + w, y + h, quad[4], quad[5]);
    ApplyTransform(transform, x, y + h, quad[6], quad[7]);

    float minX = quad[0];
    float minY = quad[1];
    float maxX = quad[0];
    float maxY = quad[1];
    for (size_t i = 2; i + 1 < quad.size(); i += 2) {
        minX = std::min(minX, quad[i]);
        minY = std::min(minY, quad[i + 1]);
        maxX = std::max(maxX, quad[i]);
        maxY = std::max(maxY, quad[i + 1]);
    }

    CpuTransform inverse {};
    if (!TryInvertTransform(transform, inverse)) {
        return;
    }

    const int left = static_cast<int>(std::floor(minX));
    const int top = static_cast<int>(std::floor(minY));
    const int right = static_cast<int>(std::ceil(maxX));
    const int bottom = static_cast<int>(std::ceil(maxY));

    for (int py = top; py < bottom; ++py) {
        for (int px = left; px < right; ++px) {
            if (!IsInsideClip(static_cast<float>(px) + 0.5f, static_cast<float>(py) + 0.5f)) {
                continue;
            }

            float localX = 0.0f;
            float localY = 0.0f;
            ApplyTransform(inverse, static_cast<float>(px) + 0.5f, static_cast<float>(py) + 0.5f, localX, localY);
            if (localX < x || localY < y || localX > x + w || localY > y + h) {
                continue;
            }

            if (px < 0 || py < 0 || px >= width_ || py >= height_ || pixelBuffer_.empty()) {
                continue;
            }

            const size_t offset = (static_cast<size_t>(py) * static_cast<size_t>(width_) + static_cast<size_t>(px)) * 4u;
            pixelBuffer_[offset + 0] = 0;
            pixelBuffer_[offset + 1] = 0;
            pixelBuffer_[offset + 2] = 0;
            pixelBuffer_[offset + 3] = 0;
        }
    }
}
void VulkanRenderTarget::PushOpacity(float opacity)
{
    TouchFrame();
    opacityStack_.push_back(GetCurrentOpacity() * std::clamp(opacity, 0.0f, 1.0f));
}

void VulkanRenderTarget::PopOpacity()
{
    TouchFrame();
    if (opacityStack_.size() > 1) {
        opacityStack_.pop_back();
    }
}
void VulkanRenderTarget::SetShapeType(int /*type*/, float /*n*/) {}
void VulkanRenderTarget::SetVSyncEnabled(bool enabled) { vsyncEnabled_ = enabled; }
void VulkanRenderTarget::SetDpi(float dpiX, float dpiY) { dpiX_ = dpiX; dpiY_ = dpiY; }
void VulkanRenderTarget::AddDirtyRect(float x, float y, float w, float h) { dirtyRects_.push_back(JaliumRect { x, y, w, h }); }
void VulkanRenderTarget::SetFullInvalidation() { fullInvalidation_ = true; }
void VulkanRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
    TouchFrame();
    if (!TryRecordGpuBitmapCommand(bitmap, x, y, w, h, opacity)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if (!bitmap || opacity <= 0.0f) {
        return;
    }

    const auto* sourceBitmap = static_cast<VulkanBitmap*>(bitmap);
    const auto& pixels = sourceBitmap->GetPixels();
    if (pixels.empty() || sourceBitmap->GetWidth() == 0 || sourceBitmap->GetHeight() == 0) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> quad(8);
    ApplyTransform(transform, x, y, quad[0], quad[1]);
    ApplyTransform(transform, x + w, y, quad[2], quad[3]);
    ApplyTransform(transform, x + w, y + h, quad[4], quad[5]);
    ApplyTransform(transform, x, y + h, quad[6], quad[7]);

    float minX = quad[0];
    float minY = quad[1];
    float maxX = quad[0];
    float maxY = quad[1];
    for (size_t i = 2; i + 1 < quad.size(); i += 2) {
        minX = std::min(minX, quad[i]);
        minY = std::min(minY, quad[i + 1]);
        maxX = std::max(maxX, quad[i]);
        maxY = std::max(maxY, quad[i + 1]);
    }

    CpuTransform inverse {};
    if (!TryInvertTransform(transform, inverse)) {
        return;
    }

    const int destLeft = static_cast<int>(std::floor(minX));
    const int destTop = static_cast<int>(std::floor(minY));
    const int destRight = static_cast<int>(std::ceil(maxX));
    const int destBottom = static_cast<int>(std::ceil(maxY));
    const uint8_t opacityByte = static_cast<uint8_t>(std::clamp(opacity, 0.0f, 1.0f) * 255.0f + 0.5f);

    for (int destY = destTop; destY < destBottom; ++destY) {
        for (int destX = destLeft; destX < destRight; ++destX) {
            float localX = 0.0f;
            float localY = 0.0f;
            ApplyTransform(inverse, static_cast<float>(destX) + 0.5f, static_cast<float>(destY) + 0.5f, localX, localY);
            if (localX < x || localY < y || localX > x + w || localY > y + h) {
                continue;
            }

            const float u = (localX - x) / std::max(0.0001f, w);
            const float v = (localY - y) / std::max(0.0001f, h);
            const uint32_t srcX = std::min<uint32_t>(sourceBitmap->GetWidth() - 1, static_cast<uint32_t>(u * sourceBitmap->GetWidth()));
            const uint32_t srcY = std::min<uint32_t>(sourceBitmap->GetHeight() - 1, static_cast<uint32_t>(v * sourceBitmap->GetHeight()));
            const size_t srcOffset = (static_cast<size_t>(srcY) * sourceBitmap->GetWidth() + static_cast<size_t>(srcX)) * 4u;

            const uint8_t srcB = pixels[srcOffset + 0];
            const uint8_t srcG = pixels[srcOffset + 1];
            const uint8_t srcR = pixels[srcOffset + 2];
            const uint8_t srcA = static_cast<uint8_t>((pixels[srcOffset + 3] * opacityByte) / 255u);
            BlendPixel(destX, destY, srcB, srcG, srcR, srcA);
        }
    }
}
void VulkanRenderTarget::RenderText(const wchar_t* text, uint32_t textLength, TextFormat* format, float x, float y, float w, float h, Brush* brush)
{
    TouchFrame();

    if (!text || textLength == 0 || !format) {
        return;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a) || a == 0) {
        return;
    }

#ifdef _WIN32
    const auto* textFormat = static_cast<VulkanTextFormat*>(format);
    const int bitmapWidth = std::max(1, static_cast<int>(std::ceil(w)));
    const int bitmapHeight = std::max(1, static_cast<int>(std::ceil(h)));

    const int fontHeight = -static_cast<int>(std::round(textFormat->GetFontSize()));

    UINT drawFlags = DT_NOPREFIX;
    switch (textFormat->GetAlignment()) {
        case JALIUM_TEXT_ALIGN_CENTER:
            drawFlags |= DT_CENTER;
            break;
        case JALIUM_TEXT_ALIGN_TRAILING:
            drawFlags |= DT_RIGHT;
            break;
        default:
            drawFlags |= DT_LEFT;
            break;
    }
    switch (textFormat->GetParagraphAlignment()) {
        case JALIUM_PARAGRAPH_ALIGN_CENTER:
            drawFlags |= DT_VCENTER | DT_SINGLELINE;
            break;
        case JALIUM_PARAGRAPH_ALIGN_FAR:
            drawFlags |= DT_BOTTOM | DT_SINGLELINE;
            break;
        default:
            drawFlags |= DT_TOP | DT_WORDBREAK;
            break;
    }

    // Cache lookup: GDI CreateDIBSection + CreateFontW + DrawTextW costs
    // ~2–5ms per call and Gallery re-runs that for every static label every
    // frame. Same (text, font, size, extents, color, alignment) → same
    // premultiplied BGRA pixels, so we can cache the rasterized bitmap.
    const uint32_t brushBgra =
        static_cast<uint32_t>(b) |
        (static_cast<uint32_t>(g) << 8) |
        (static_cast<uint32_t>(r) << 16) |
        (static_cast<uint32_t>(a) << 24);
    TextCacheKey cacheKey = std::make_tuple(
        std::wstring(text, textLength),
        textFormat->GetFontFamily(),
        fontHeight,
        bitmapWidth,
        bitmapHeight,
        brushBgra,
        static_cast<int>(drawFlags));

    std::shared_ptr<const std::vector<uint8_t>> pixelsForDraw;
    int drawWidth = bitmapWidth;
    int drawHeight = bitmapHeight;

    auto cacheIt = textCache_.find(cacheKey);
    if (cacheIt != textCache_.end()) {
        pixelsForDraw = cacheIt->second.pixels;
        drawWidth = cacheIt->second.width;
        drawHeight = cacheIt->second.height;
    } else {
        BITMAPINFO bitmapInfo {};
        bitmapInfo.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bitmapInfo.bmiHeader.biWidth = bitmapWidth;
        bitmapInfo.bmiHeader.biHeight = -bitmapHeight;
        bitmapInfo.bmiHeader.biPlanes = 1;
        bitmapInfo.bmiHeader.biBitCount = 32;
        bitmapInfo.bmiHeader.biCompression = BI_RGB;

        void* dibPixels = nullptr;
        HDC screenDc = GetDC(nullptr);
        if (!screenDc) {
            return;
        }

        HDC memoryDc = CreateCompatibleDC(screenDc);
        HBITMAP dib = CreateDIBSection(screenDc, &bitmapInfo, DIB_RGB_COLORS, &dibPixels, nullptr, 0);
        ReleaseDC(nullptr, screenDc);
        if (!memoryDc || !dib || !dibPixels) {
            if (dib) DeleteObject(dib);
            if (memoryDc) DeleteDC(memoryDc);
            return;
        }

        HGDIOBJ oldBitmap = SelectObject(memoryDc, dib);
        SetBkMode(memoryDc, TRANSPARENT);
        SetTextColor(memoryDc, RGB(255, 255, 255));

        const int fontWeight = FW_NORMAL;
        HFONT font = CreateFontW(
            fontHeight,
            0,
            0,
            0,
            fontWeight,
            FALSE,
            FALSE,
            FALSE,
            DEFAULT_CHARSET,
            OUT_DEFAULT_PRECIS,
            CLIP_DEFAULT_PRECIS,
            CLEARTYPE_QUALITY,
            DEFAULT_PITCH | FF_DONTCARE,
            textFormat->GetFontFamily().c_str());
        HGDIOBJ oldFont = font ? SelectObject(memoryDc, font) : nullptr;

        RECT rect { 0, 0, bitmapWidth, bitmapHeight };
        DrawTextW(memoryDc, text, static_cast<int>(textLength), &rect, drawFlags);

        auto* source = static_cast<uint8_t*>(dibPixels);
        std::vector<uint8_t> textPixels(static_cast<size_t>(bitmapWidth) * static_cast<size_t>(bitmapHeight) * 4u, 0);
        for (int py = 0; py < bitmapHeight; ++py) {
            for (int px = 0; px < bitmapWidth; ++px) {
                const size_t offset = (static_cast<size_t>(py) * static_cast<size_t>(bitmapWidth) + static_cast<size_t>(px)) * 4u;
                const uint8_t coverage = std::max({ source[offset + 0], source[offset + 1], source[offset + 2] });
                textPixels[offset + 0] = static_cast<uint8_t>((static_cast<uint32_t>(b) * coverage) / 255u);
                textPixels[offset + 1] = static_cast<uint8_t>((static_cast<uint32_t>(g) * coverage) / 255u);
                textPixels[offset + 2] = static_cast<uint8_t>((static_cast<uint32_t>(r) * coverage) / 255u);
                textPixels[offset + 3] = static_cast<uint8_t>((static_cast<uint32_t>(a) * coverage) / 255u);
            }
        }

        if (oldFont) {
            SelectObject(memoryDc, oldFont);
        }
        if (font) {
            DeleteObject(font);
        }
        if (oldBitmap) {
            SelectObject(memoryDc, oldBitmap);
        }
        DeleteObject(dib);
        DeleteDC(memoryDc);

        // Simple bounded cache: once we blow past the cap, dump the whole map
        // rather than maintaining LRU bookkeeping. In practice a frame touches
        // a small working set (~100 labels), so hitting the cap means the UI
        // is cycling through content anyway — restarting from empty is fine.
        if (textCache_.size() >= kMaxTextCacheEntries) {
            textCache_.clear();
        }

        TextCacheEntry entry;
        entry.pixels = std::make_shared<const std::vector<uint8_t>>(std::move(textPixels));
        entry.width = bitmapWidth;
        entry.height = bitmapHeight;
        auto [insertedIt, _] = textCache_.emplace(std::move(cacheKey), std::move(entry));
        pixelsForDraw = insertedIt->second.pixels;
        drawWidth = insertedIt->second.width;
        drawHeight = insertedIt->second.height;
    }

    // Fast path: record directly into the GPU replay command list with a
    // shared_ptr to the cached pixels. No VulkanBitmap construction, no
    // vector copy — just a ref-count bump and a single push_back. This is
    // the single biggest Render-time win for Gallery.
    RecordCachedTextBitmap(std::move(pixelsForDraw), drawWidth, drawHeight, x, y);
#else
    // FreeType + HarfBuzz glyph atlas text rendering (Android / Linux)
    // Render glyphs into a temporary BGRA bitmap in local (DIP) space,
    // then use DrawBitmap which applies the current transform once
    // for both GPU replay and CPU pixel-buffer blitting.
    FreeTypeTextFormat* ftFormat = dynamic_cast<FreeTypeTextFormat*>(format);
    if (!ftFormat) {
        auto* vulkanFormat = dynamic_cast<VulkanTextFormat*>(format);
        if (vulkanFormat) {
            ftFormat = vulkanFormat->GetFreeTypeFormat();
        }
    }
    TextEngine* textEngine = backend_ ? backend_->GetTextEngine() : nullptr;
    if (!ftFormat || !textEngine) {
        return;
    }

    const float brushR = static_cast<float>(r) / 255.0f;
    const float brushG = static_cast<float>(g) / 255.0f;
    const float brushB = static_cast<float>(b) / 255.0f;
    const float brushA = static_cast<float>(a) / 255.0f * GetCurrentOpacity();

    // When DPI scaling is active, rasterize text at physical pixel resolution
    // for crisp rendering. The resulting bitmap is drawn at DIP size and the
    // DPI transform scales it up to match the physical resolution exactly.
    float dpiScaleX = dpiX_ / 96.0f;
    float dpiScaleY = dpiY_ / 96.0f;
    float renderScale = (dpiScaleX != 1.0f || dpiScaleY != 1.0f) ? dpiScaleY : 1.0f;

    // Generate glyph quads in local space (origin 0,0). DrawBitmap will
    // position the resulting bitmap at (x, y) and apply the current transform.
    std::vector<TextGlyphQuad> quads;
    ftFormat->GenerateGlyphQuads(text, textLength, w, h, brushR, brushG, brushB, brushA, 0.0f, 0.0f, quads, renderScale);
    if (quads.empty()) {
        return;
    }

    GlyphAtlas* atlas = textEngine->GetGlyphAtlas();
    const uint8_t* atlasData = atlas->GetPixelData();
    const uint32_t atlasW = atlas->GetWidth();
    const uint32_t atlasH = atlas->GetHeight();

    // Compute tight bounding box of all glyph quads (scaled space when renderScale != 1)
    float scaledW = w * renderScale;
    float scaledH = h * renderScale;
    float bboxMinX = scaledW;
    float bboxMinY = scaledH;
    float bboxMaxX = 0.0f;
    float bboxMaxY = 0.0f;
    for (const auto& quad : quads) {
        bboxMinX = std::min(bboxMinX, quad.posX);
        bboxMinY = std::min(bboxMinY, quad.posY);
        bboxMaxX = std::max(bboxMaxX, quad.posX + quad.sizeX);
        bboxMaxY = std::max(bboxMaxY, quad.posY + quad.sizeY);
    }
    bboxMinX = std::max(bboxMinX, 0.0f);
    bboxMinY = std::max(bboxMinY, 0.0f);
    bboxMaxX = std::min(bboxMaxX, scaledW);
    bboxMaxY = std::min(bboxMaxY, scaledH);

    const int32_t bitmapWidth = static_cast<int32_t>(std::ceil(bboxMaxX - bboxMinX));
    const int32_t bitmapHeight = static_cast<int32_t>(std::ceil(bboxMaxY - bboxMinY));
    if (bitmapWidth <= 0 || bitmapHeight <= 0) {
        return;
    }

    const float bitmapOffsetX = std::floor(bboxMinX);
    const float bitmapOffsetY = std::floor(bboxMinY);

    // Render glyphs into a temporary BGRA bitmap (pre-multiplied alpha)
    std::vector<uint8_t> textPixels(static_cast<size_t>(bitmapWidth) * bitmapHeight * 4, 0);

    for (const auto& quad : quads) {
        const int32_t dstX = static_cast<int32_t>(std::floor(quad.posX - bitmapOffsetX));
        const int32_t dstY = static_cast<int32_t>(std::floor(quad.posY - bitmapOffsetY));
        const int32_t qw = static_cast<int32_t>(std::ceil(quad.sizeX));
        const int32_t qh = static_cast<int32_t>(std::ceil(quad.sizeY));
        const int32_t srcX = static_cast<int32_t>(quad.uvMinX * atlasW);
        const int32_t srcY = static_cast<int32_t>(quad.uvMinY * atlasH);

        for (int32_t row = 0; row < qh; ++row) {
            const int32_t dy = dstY + row;
            const int32_t sy = srcY + row;
            if (dy < 0 || dy >= bitmapHeight || sy < 0 || sy >= static_cast<int32_t>(atlasH)) {
                continue;
            }

            for (int32_t col = 0; col < qw; ++col) {
                const int32_t dx = dstX + col;
                const int32_t sx = srcX + col;
                if (dx < 0 || dx >= bitmapWidth || sx < 0 || sx >= static_cast<int32_t>(atlasW)) {
                    continue;
                }

                const size_t atlasIdx = (static_cast<size_t>(sy) * atlasW + sx) * 4;
                const uint8_t coverage = atlasData[atlasIdx + 3];
                if (coverage == 0) {
                    continue;
                }

                const uint8_t pixelA = static_cast<uint8_t>(brushA * 255.0f * coverage / 255.0f);
                if (pixelA == 0) {
                    continue;
                }

                // Pre-multiplied alpha BGRA
                const size_t pixelIdx = (static_cast<size_t>(dy) * bitmapWidth + dx) * 4;
                const uint8_t pmB = static_cast<uint8_t>(b * pixelA / 255);
                const uint8_t pmG = static_cast<uint8_t>(g * pixelA / 255);
                const uint8_t pmR = static_cast<uint8_t>(r * pixelA / 255);

                // Alpha-blend over existing pixel (handles overlapping glyphs)
                const uint8_t existA = textPixels[pixelIdx + 3];
                if (existA == 0) {
                    textPixels[pixelIdx + 0] = pmB;
                    textPixels[pixelIdx + 1] = pmG;
                    textPixels[pixelIdx + 2] = pmR;
                    textPixels[pixelIdx + 3] = pixelA;
                } else {
                    const uint32_t outA = pixelA + existA * (255u - pixelA) / 255u;
                    if (outA > 0) {
                        textPixels[pixelIdx + 0] = static_cast<uint8_t>((pmB * 255u + textPixels[pixelIdx + 0] * (255u - pixelA)) / 255u);
                        textPixels[pixelIdx + 1] = static_cast<uint8_t>((pmG * 255u + textPixels[pixelIdx + 1] * (255u - pixelA)) / 255u);
                        textPixels[pixelIdx + 2] = static_cast<uint8_t>((pmR * 255u + textPixels[pixelIdx + 2] * (255u - pixelA)) / 255u);
                        textPixels[pixelIdx + 3] = static_cast<uint8_t>(outA);
                    }
                }
            }
        }
    }

    // DrawBitmap handles GPU replay recording + CPU pixel-buffer blitting,
    // and applies the current transform to position the bitmap correctly.
    // When renderScale != 1, convert bitmap offset and display size back to DIP space.
    // The bitmap pixels are at physical resolution; the DPI transform in the pipeline
    // will scale the DIP-space rect back up to match the physical pixel dimensions.
    const float invScale = 1.0f / renderScale;
    VulkanBitmap textBitmap(static_cast<uint32_t>(bitmapWidth), static_cast<uint32_t>(bitmapHeight), std::move(textPixels));
    DrawBitmap(&textBitmap, x + bitmapOffsetX * invScale, y + bitmapOffsetY * invScale,
        static_cast<float>(bitmapWidth) * invScale, static_cast<float>(bitmapHeight) * invScale, 1.0f);
#endif
}
void VulkanRenderTarget::DrawBackdropFilter(float x, float y, float w, float h, const char* backdropFilter, const char* material, const char* materialTint, float tintOpacity, float blurRadius, float cornerRadiusTL, float cornerRadiusTR, float cornerRadiusBR, float cornerRadiusBL)
{
    TouchFrame();
    (void)backdropFilter;
    (void)material;
    (void)cornerRadiusTR;
    (void)cornerRadiusBR;
    (void)cornerRadiusBL;

    // Backdrop reads pixelBuffer_ (both to record the GPU command and to apply
    // the CPU fallback blur), so pixelBuffer_ must reflect every command
    // recorded so far this frame. If we've been lazily skipping CPU work, catch
    // it up now; from this point in the frame on, cpuRasterNeeded_ stays true.
    EnsureCpuRasterization();

    if (pixelBuffer_.empty() || w <= 0.0f || h <= 0.0f) {
        return;
    }

    uint8_t tintB = 0, tintG = 0, tintR = 0;
    ParseTintColor(materialTint, 1.0f, 1.0f, 1.0f, tintB, tintG, tintR);
    auto blurred = BlurPixels(pixelBuffer_, width_, height_, std::max(1, static_cast<int>(std::round(blurRadius))), x, y, w, h);
    PushTemporaryClip(x, y, w, h, cornerRadiusTL, cornerRadiusTL);
    if (!TryRecordGpuBackdropCommand(
            pixelBuffer_,
            static_cast<uint32_t>(width_),
            static_cast<uint32_t>(height_),
            x,
            y,
            w,
            h,
            blurRadius,
            cornerRadiusTL,
            cornerRadiusTR,
            cornerRadiusBR,
            cornerRadiusBL,
            static_cast<float>(tintR) / 255.0f,
            static_cast<float>(tintG) / 255.0f,
            static_cast<float>(tintB) / 255.0f,
            tintOpacity,
            1.0f,
            0.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(blurred, width_, height_, x, y, w, h, 1.0f);

    VulkanSolidBrush tintBrush(
        static_cast<float>(tintR) / 255.0f,
        static_cast<float>(tintG) / 255.0f,
        static_cast<float>(tintB) / 255.0f,
        std::clamp(tintOpacity, 0.0f, 1.0f));
    FillSolidRect(
        static_cast<int>(std::floor(x)),
        static_cast<int>(std::floor(y)),
        static_cast<int>(std::ceil(x + w)),
        static_cast<int>(std::ceil(y + h)),
        tintB, tintG, tintR,
        static_cast<uint8_t>(std::clamp(tintOpacity, 0.0f, 1.0f) * 255.0f + 0.5f));
    PopTemporaryClip();
    (void)tintBrush;
}

void VulkanRenderTarget::DrawGlowingBorderHighlight(float x, float y, float w, float h, float animationPhase, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float trailLength, float dimOpacity, float screenWidth, float screenHeight)
{
    TouchFrame();
    (void)animationPhase;
    (void)trailLength;
    (void)screenWidth;
    (void)screenHeight;

    const uint8_t b = static_cast<uint8_t>(std::clamp(glowColorB, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t g = static_cast<uint8_t>(std::clamp(glowColorG, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t r = static_cast<uint8_t>(std::clamp(glowColorR, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t dimA = static_cast<uint8_t>(std::clamp(dimOpacity, 0.0f, 1.0f) * 255.0f + 0.5f);

    if (!TryRecordGpuGlowCommand(
            x,
            y,
            w,
            h,
            6.0f,
            strokeWidth,
            glowColorR,
            glowColorG,
            glowColorB,
            220.0f / 255.0f,
            dimOpacity,
            1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    BlendOutsideRect(x, y, w, h, 0, 0, 0, dimA);
    StrokeRoundedRectApprox(x, y, w, h, 6.0f, 6.0f, strokeWidth, b, g, r, 220);
}

void VulkanRenderTarget::DrawGlowingBorderTransition(float fromX, float fromY, float fromW, float fromH, float toX, float toY, float toW, float toH, float headProgress, float tailProgress, float animationPhase, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float trailLength, float dimOpacity, float screenWidth, float screenHeight)
{
    TouchFrame();
    (void)tailProgress;
    (void)animationPhase;
    (void)trailLength;
    (void)screenWidth;
    (void)screenHeight;

    const float t = std::clamp(headProgress, 0.0f, 1.0f);
    const float x = fromX + (toX - fromX) * t;
    const float y = fromY + (toY - fromY) * t;
    const float w = fromW + (toW - fromW) * t;
    const float h = fromH + (toH - fromH) * t;
    DrawGlowingBorderHighlight(x, y, w, h, 0.0f, glowColorR, glowColorG, glowColorB, strokeWidth, 1.0f, dimOpacity, screenWidth, screenHeight);
}

void VulkanRenderTarget::DrawRippleEffect(float x, float y, float w, float h, float rippleProgress, float glowColorR, float glowColorG, float glowColorB, float strokeWidth, float dimOpacity, float screenWidth, float screenHeight)
{
    TouchFrame();
    (void)screenWidth;
    (void)screenHeight;

    const float expand = std::max(w, h) * std::clamp(rippleProgress, 0.0f, 1.0f) * 0.3f;
    DrawGlowingBorderHighlight(
        x - expand,
        y - expand,
        w + expand * 2.0f,
        h + expand * 2.0f,
        0.0f,
        glowColorR,
        glowColorG,
        glowColorB,
        strokeWidth,
        1.0f,
        dimOpacity,
        screenWidth,
        screenHeight);
}

void VulkanRenderTarget::CaptureDesktopArea(int32_t screenX, int32_t screenY, int32_t width, int32_t height)
{
    TouchFrame();
#ifdef _WIN32
    if (width <= 0 || height <= 0) {
        desktopCapturePixels_.clear();
        desktopCaptureValid_ = false;
        return;
    }

    if (GetDxgiDesktopDuplicator().Capture(screenX, screenY, width, height, desktopCapturePixels_)) {
        desktopCaptureWidth_ = width;
        desktopCaptureHeight_ = height;
        desktopCaptureValid_ = true;
        return;
    }

    HDC desktopDc = GetDC(nullptr);
    if (!desktopDc) {
        return;
    }

    HDC memoryDc = CreateCompatibleDC(desktopDc);
    HBITMAP bitmap = CreateCompatibleBitmap(desktopDc, width, height);
    if (!memoryDc || !bitmap) {
        if (bitmap) DeleteObject(bitmap);
        if (memoryDc) DeleteDC(memoryDc);
        ReleaseDC(nullptr, desktopDc);
        return;
    }

    HGDIOBJ oldBitmap = SelectObject(memoryDc, bitmap);
    BitBlt(memoryDc, 0, 0, width, height, desktopDc, screenX, screenY, SRCCOPY);
    SelectObject(memoryDc, oldBitmap);

    BITMAPINFOHEADER header {};
    header.biSize = sizeof(BITMAPINFOHEADER);
    header.biWidth = width;
    header.biHeight = -height;
    header.biPlanes = 1;
    header.biBitCount = 32;
    header.biCompression = BI_RGB;

    desktopCapturePixels_.assign(static_cast<size_t>(width) * static_cast<size_t>(height) * 4u, 0);
    GetDIBits(memoryDc, bitmap, 0, height, desktopCapturePixels_.data(), reinterpret_cast<BITMAPINFO*>(&header), DIB_RGB_COLORS);
    for (int i = 0; i < width * height; ++i) {
        desktopCapturePixels_[static_cast<size_t>(i) * 4u + 3] = 255;
    }

    DeleteObject(bitmap);
    DeleteDC(memoryDc);
    ReleaseDC(nullptr, desktopDc);

    desktopCaptureWidth_ = width;
    desktopCaptureHeight_ = height;
    desktopCaptureValid_ = true;
#else
    (void)screenX;
    (void)screenY;
    (void)width;
    (void)height;
#endif
}

void VulkanRenderTarget::DrawDesktopBackdrop(float x, float y, float w, float h, float blurRadius, float tintR, float tintG, float tintB, float tintOpacity, float noiseIntensity, float saturation)
{
    TouchFrame();
    (void)noiseIntensity;
    (void)saturation;

    if (!desktopCaptureValid_ || desktopCapturePixels_.empty()) {
        return;
    }

    auto blurred = BlurPixels(desktopCapturePixels_, desktopCaptureWidth_, desktopCaptureHeight_, std::max(1, static_cast<int>(std::round(blurRadius))), 0.0f, 0.0f, static_cast<float>(desktopCaptureWidth_), static_cast<float>(desktopCaptureHeight_));
    if (!TryRecordGpuBackdropCommand(desktopCapturePixels_, static_cast<uint32_t>(desktopCaptureWidth_), static_cast<uint32_t>(desktopCaptureHeight_), x, y, w, h, blurRadius, 0.0f, 0.0f, 0.0f, 0.0f, tintR, tintG, tintB, tintOpacity, saturation, noiseIntensity)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(blurred, desktopCaptureWidth_, desktopCaptureHeight_, x, y, w, h, 1.0f);

    const uint8_t b = static_cast<uint8_t>(std::clamp(tintB, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t g = static_cast<uint8_t>(std::clamp(tintG, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t r = static_cast<uint8_t>(std::clamp(tintR, 0.0f, 1.0f) * 255.0f + 0.5f);
    VulkanSolidBrush tintBrush(
        static_cast<float>(r) / 255.0f,
        static_cast<float>(g) / 255.0f,
        static_cast<float>(b) / 255.0f,
        std::clamp(tintOpacity, 0.0f, 1.0f));
    FillSolidRect(
        static_cast<int>(std::floor(x)),
        static_cast<int>(std::floor(y)),
        static_cast<int>(std::ceil(x + w)),
        static_cast<int>(std::ceil(y + h)),
        b, g, r,
        static_cast<uint8_t>(std::clamp(tintOpacity, 0.0f, 1.0f) * 255.0f + 0.5f));
    (void)tintBrush;
}
void VulkanRenderTarget::BeginTransitionCapture(int slot, float x, float y, float w, float h)
{
    TouchFrame();
    if (slot < 0 || slot > 1) {
        return;
    }

    // We're about to snapshot pixelBuffer_ into transitionSavedPixels_; it has
    // to be fully rasterized first. Also, from this point on the capture will
    // call Draw* methods expecting the CPU path to actually run (so that
    // EndTransitionCapture can harvest pixelBuffer_), so leave cpuRasterNeeded_
    // latched to true for the rest of the frame.
    EnsureCpuRasterization();

    transitionSavedPixels_ = pixelBuffer_;
    transitionSavedReplayCommands_ = gpuReplayCommands_;
    transitionSavedReplaySupported_ = gpuReplaySupported_;
    transitionSavedReplayHasClear_ = gpuReplayHasClear_;
    activeTransitionSlot_ = slot;
    ResizeCpuCanvas();
    ClearCpuCanvas(0, 0, 0, 0);
    ResetGpuReplay();
    gpuReplayHasClear_ = true;
    transitionSlots_[slot].valid = false;
    (void)x;
    (void)y;
    (void)w;
    (void)h;
}

void VulkanRenderTarget::EndTransitionCapture(int slot)
{
    TouchFrame();
    if (slot < 0 || slot > 1 || activeTransitionSlot_ != slot) {
        return;
    }

    transitionSlots_[slot].pixels = pixelBuffer_;
    transitionSlots_[slot].valid = true;
    pixelBuffer_ = std::move(transitionSavedPixels_);
    transitionSavedPixels_.clear();
    gpuReplayCommands_ = std::move(transitionSavedReplayCommands_);
    transitionSavedReplayCommands_.clear();
    gpuReplaySupported_ = transitionSavedReplaySupported_;
    gpuReplayHasClear_ = transitionSavedReplayHasClear_;
    transitionSavedReplaySupported_ = false;
    transitionSavedReplayHasClear_ = false;
    activeTransitionSlot_ = -1;
}

void VulkanRenderTarget::DrawTransitionShader(float x, float y, float w, float h, float progress, int mode)
{
    TouchFrame();
    (void)mode;
    if (!transitionSlots_[0].valid || !transitionSlots_[1].valid) {
        return;
    }

    if (!TryRecordGpuTransitionCommand(
            transitionSlots_[0].pixels,
            transitionSlots_[1].pixels,
            static_cast<uint32_t>(width_),
            static_cast<uint32_t>(height_),
            x,
            y,
            w,
            h,
            progress,
            mode)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    const float t = std::clamp(progress, 0.0f, 1.0f);
    std::vector<uint8_t> mixedPixels(static_cast<size_t>(width_) * static_cast<size_t>(height_) * 4u, 0);
    const int left = std::max(0, static_cast<int>(std::floor(x)));
    const int top = std::max(0, static_cast<int>(std::floor(y)));
    const int right = std::min(width_, static_cast<int>(std::ceil(x + w)));
    const int bottom = std::min(height_, static_cast<int>(std::ceil(y + h)));

    for (int py = top; py < bottom; ++py) {
        for (int px = left; px < right; ++px) {
            const size_t offset = (static_cast<size_t>(py) * static_cast<size_t>(width_) + static_cast<size_t>(px)) * 4u;
            const auto& from = transitionSlots_[0].pixels;
            const auto& to = transitionSlots_[1].pixels;
            const uint8_t mixB = static_cast<uint8_t>(from[offset + 0] * (1.0f - t) + to[offset + 0] * t);
            const uint8_t mixG = static_cast<uint8_t>(from[offset + 1] * (1.0f - t) + to[offset + 1] * t);
            const uint8_t mixR = static_cast<uint8_t>(from[offset + 2] * (1.0f - t) + to[offset + 2] * t);
            const uint8_t mixA = static_cast<uint8_t>(from[offset + 3] * (1.0f - t) + to[offset + 3] * t);
            mixedPixels[offset + 0] = mixB;
            mixedPixels[offset + 1] = mixG;
            mixedPixels[offset + 2] = mixR;
            mixedPixels[offset + 3] = mixA;
            BlendPixel(px, py, mixB, mixG, mixR, mixA);
        }
    }

}

void VulkanRenderTarget::DrawCapturedTransition(int slot, float x, float y, float w, float h, float opacity)
{
    TouchFrame();
    if (slot < 0 || slot > 1 || !transitionSlots_[slot].valid) {
        return;
    }

    if (!TryRecordGpuPixelBufferCommand(transitionSlots_[slot].pixels, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, opacity)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(transitionSlots_[slot].pixels, width_, height_, x, y, w, h, opacity);
}
void VulkanRenderTarget::BeginEffectCapture(float x, float y, float w, float h)
{
    TouchFrame();

    // The capture snapshots pixelBuffer_ (moving it into savedPixels_) and then
    // begins a fresh sub-frame where child Draw* calls rasterize into a cleared
    // pixelBuffer_ that EndEffectCapture will read. All of that requires the
    // CPU path to be active, so catch up any previously-skipped work first.
    EnsureCpuRasterization();

    EffectCaptureState state {};
    state.savedPixels = std::move(pixelBuffer_);
    state.savedReplayCommands = gpuReplayCommands_;
    state.savedReplaySupported = gpuReplaySupported_;
    state.savedReplayHasClear = gpuReplayHasClear_;
    state.x = x;
    state.y = y;
    state.w = w;
    state.h = h;
    effectCaptureStack_.push_back(std::move(state));

    ResizeCpuCanvas();
    ClearCpuCanvas(0, 0, 0, 0);
    ResetGpuReplay();
    gpuReplayHasClear_ = true;
}

void VulkanRenderTarget::EndEffectCapture()
{
    TouchFrame();

    if (effectCaptureStack_.empty()) {
        lastCapturedPixels_.clear();
        return;
    }

    lastCapturedPixels_ = pixelBuffer_;
    lastCaptureX_ = effectCaptureStack_.back().x;
    lastCaptureY_ = effectCaptureStack_.back().y;
    lastCaptureW_ = effectCaptureStack_.back().w;
    lastCaptureH_ = effectCaptureStack_.back().h;

    pixelBuffer_ = std::move(effectCaptureStack_.back().savedPixels);
    gpuReplayCommands_ = std::move(effectCaptureStack_.back().savedReplayCommands);
    gpuReplaySupported_ = effectCaptureStack_.back().savedReplaySupported;
    gpuReplayHasClear_ = effectCaptureStack_.back().savedReplayHasClear;
    effectCaptureStack_.pop_back();
}

void VulkanRenderTarget::DrawBlurEffect(float x, float y, float w, float h, float radius, float /*uvOffsetX*/, float /*uvOffsetY*/)
{
    TouchFrame();

    if (lastCapturedPixels_.empty()) {
        return;
    }

    if (radius <= 0.0f) {
        if (!TryRecordGpuPixelBufferCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
            /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
        }
        BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
        return;
    }

    const int blurRadius = std::max(1, static_cast<int>(std::round(radius)));
    auto blurred = BlurPixels(lastCapturedPixels_, width_, height_, blurRadius, x, y, w, h);
    if (!TryRecordGpuBlurCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, radius, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(blurred, width_, height_, x, y, w, h, 1.0f);
}

void VulkanRenderTarget::DrawDropShadowEffect(float x, float y, float w, float h, float blurRadius, float offsetX, float offsetY, float r, float g, float b, float a, float /*uvOffsetX*/, float /*uvOffsetY*/, float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    TouchFrame();

    if (lastCapturedPixels_.empty()) {
        return;
    }

    const int shadowRadius = std::max(1, static_cast<int>(std::round(blurRadius)));
    auto blurred = BlurPixels(lastCapturedPixels_, width_, height_, shadowRadius, x, y, w, h);

    std::vector<uint8_t> shadowPixels = blurred;
    const uint8_t shadowB = static_cast<uint8_t>(std::clamp(b, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t shadowG = static_cast<uint8_t>(std::clamp(g, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t shadowR = static_cast<uint8_t>(std::clamp(r, 0.0f, 1.0f) * 255.0f + 0.5f);
    const float shadowOpacity = std::clamp(a, 0.0f, 1.0f);

    for (size_t offset = 0; offset + 3 < shadowPixels.size(); offset += 4) {
        const uint8_t alpha = static_cast<uint8_t>(shadowPixels[offset + 3] * shadowOpacity);
        shadowPixels[offset + 0] = shadowB;
        shadowPixels[offset + 1] = shadowG;
        shadowPixels[offset + 2] = shadowR;
        shadowPixels[offset + 3] = alpha;
    }

    if (!TryRecordGpuBlurCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x + offsetX, y + offsetY, w, h, blurRadius, 1.0f, true, r, g, b, a) ||
        !TryRecordGpuPixelBufferCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(shadowPixels, width_, height_, x + offsetX, y + offsetY, w, h, 1.0f);
    BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
}

void VulkanRenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    (void)shaderBytecode;
    (void)shaderBytecodeSize;
    (void)constants;
    (void)constantFloatCount;
    DrawBlurEffect(x, y, w, h, 0.0f);
}

void VulkanRenderTarget::DrawLiquidGlass(float x, float y, float w, float h, float cornerRadius, float blurRadius, float refractionAmount, float chromaticAberration, float tintR, float tintG, float tintB, float tintOpacity, float lightX, float lightY, float highlightBoost, int shapeType, float shapeExponent, int neighborCount, float fusionRadius, const float* neighborData)
{
    TouchFrame();
    (void)refractionAmount;
    (void)chromaticAberration;
    (void)lightX;
    (void)lightY;
    (void)highlightBoost;
    (void)shapeType;
    (void)shapeExponent;
    (void)neighborCount;
    (void)fusionRadius;
    (void)neighborData;

    // LiquidGlass samples pixelBuffer_ for both the GPU command payload and the
    // CPU blur fallback, so bring pixelBuffer_ up to date before either path.
    EnsureCpuRasterization();

    if (!TryRecordGpuLiquidGlassCommand(pixelBuffer_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, cornerRadius, blurRadius, refractionAmount, chromaticAberration, tintR, tintG, tintB, tintOpacity, lightX, lightY, highlightBoost)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    auto blurred = BlurPixels(pixelBuffer_, width_, height_, std::max(1, static_cast<int>(std::round(blurRadius))), x, y, w, h);
    PushTemporaryClip(x, y, w, h, cornerRadius, cornerRadius);
    BlendBuffer(blurred, width_, height_, x, y, w, h, 1.0f);

    const uint8_t b = static_cast<uint8_t>(std::clamp(tintB, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t g = static_cast<uint8_t>(std::clamp(tintG, 0.0f, 1.0f) * 255.0f + 0.5f);
    const uint8_t r = static_cast<uint8_t>(std::clamp(tintR, 0.0f, 1.0f) * 255.0f + 0.5f);
    FillSolidRect(
        static_cast<int>(std::floor(x)),
        static_cast<int>(std::floor(y)),
        static_cast<int>(std::ceil(x + w)),
        static_cast<int>(std::ceil(y + h)),
        b, g, r,
        static_cast<uint8_t>(std::clamp(tintOpacity, 0.0f, 1.0f) * 255.0f + 0.5f));
    StrokeRoundedRectApprox(x, y, w, h, cornerRadius, cornerRadius, 1.5f, 255, 255, 255, 80);
    PopTemporaryClip();
}

void VulkanRenderTarget::TouchFrame() const
{
    if (!isDrawing_) {
        return;
    }
}

} // namespace jalium
