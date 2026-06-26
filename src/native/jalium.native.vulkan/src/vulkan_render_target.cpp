#include "vulkan_render_target.h"
#include "jalium_internal.h"
#include "jalium_path_stats.h"
#include "jalium_text_options.h"  // JALIUM_TEXT_AA_* enum (TextRenderingMode mapping)
#include "jalium_flatten.h"
#include "jalium_triangulate.h"  // FlattenPathToContours / TriangulateCompoundPath / DispatchPathCommand
#include "jalium_gradient_sample.h"  // SampleBrushGradient / FlattenGradientStops — in-order gradient squircle fill

#include "vulkan_backend.h"
#include "vulkan_shader_compiler.h"  // runtime HLSL→SPIR-V for DrawShaderEffectFromSource
#include "vulkan_embedded_shaders.h"
#include "vulkan_ink_composite_shaders.h"  // kInkCompositeFragSpv — premultiplied ink-layer composite FS (all platforms)
#include "vulkan_impeller_shaders.h"   // kImpellerSolidFillVertShaderSpv etc. — used by EnsureEngineBatchPipeline
#include "vulkan_vello_compute.h"      // VelloComputePipeline — real Vello GPU compute path
#include "vulkan_vello_composite_shaders.h"  // kVelloCompositeVert/FragSpv — output-image composite
#include "vulkan_triangle_vc_shaders.h" // kTriangleFillVc{Vert,Frag}ShaderSpv — DrawLine 3-strip AA
#include "vulkan_minimal.h"
#include "vulkan_resources.h"
#include "vulkan_runtime.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>
#include <cstdio>
#include <cstdint>
#include <unordered_map>

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
#include "win32_gdi_pool.h"
// Dedicated Vulkan text-glyph pipeline (B4) — Windows-only because the CPU glyph
// atlas (DirectWrite rasterization) and the embedded text-glyph SPIR-V are
// authored for the Windows DWrite path. The atlas class is itself #ifdef _WIN32
// guarded; the full type must be visible before the std::unique_ptr<VulkanGlyphAtlas>
// member below, and the SPIR-V symbols (kTextGlyphVertSpv / kTextGlyphFragSpv)
// feed the text pipeline created in EnsureGraphicsResources.
#include "vulkan_text_glyph_shaders.h"
#include "vulkan_glyph_atlas.h"
// jalium::text_stats::AddDrawTextCall — feeds the unified DevTools text-cache
// counters (drawTextCalls) so the Vulkan text path is observable like D3D12.
#include "jalium_text_stats.h"
#else
#include "text_engine.h"
#include "text_layout.h"
#include "glyph_atlas.h"
#endif

namespace jalium {

namespace {

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

// ── Monotonic wall-clock helpers for frame-pacing instrumentation ───────
// Mirrors the QpcNow / QpcDiffNs helpers in d3d12_direct_renderer.cpp so
// frameGpuWaitNs is measured in the same units across backends.
inline uint64_t MonotonicNowNs() noexcept
{
#if defined(_WIN32)
    static LARGE_INTEGER s_freq = []() {
        LARGE_INTEGER f{};
        QueryPerformanceFrequency(&f);
        return f;
    }();
    LARGE_INTEGER t{};
    QueryPerformanceCounter(&t);
    if (s_freq.QuadPart <= 0) return 0;
    int64_t whole = (t.QuadPart / s_freq.QuadPart) * 1'000'000'000LL;
    int64_t frac  = ((t.QuadPart % s_freq.QuadPart) * 1'000'000'000LL) / s_freq.QuadPart;
    return static_cast<uint64_t>(whole + frac);
#else
    struct timespec ts{};
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return static_cast<uint64_t>(ts.tv_sec) * 1'000'000'000ull
         + static_cast<uint64_t>(ts.tv_nsec);
#endif
}
inline uint64_t MonotonicDiffNs(uint64_t start, uint64_t end) noexcept
{
    return end > start ? (end - start) : 0;
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
    // Per-corner radii (TL, TR, BR, BL). Switching the fragment shader
    // into per-corner-SDF mode is implicit: any non-zero entry here
    // overrides roundedClipRadius for the outer rounded clip pass.
    // Uniform-radius callers MUST leave these as all zero.
    float perCornerRadiusX[4];
    float perCornerRadiusY[4];
    // Per-corner radii for the INNER rounded clip (border stroke's inside
    // edge), same (TL, TR, BR, BL) order. Non-zero entries switch the inner
    // subtraction to per-corner so a non-uniform Border's inner edge tracks
    // its outer edge. Maps to the HLSL `float4 innerPerCornerRadiusX/Y`
    // (offsets 192 / 208; total struct 224 bytes — verified via spirv-cross).
    float innerPerCornerRadiusX[4];
    float innerPerCornerRadiusY[4];
};
// Must match the HLSL push_constant block in solid_rect.{vert,frag}.hlsl byte
// for byte. 56 floats = 224 bytes; the inner per-corner fields sit at offsets
// 192 / 208 (confirmed via spirv-cross reflection). Push-constant range size is
// sizeof(SolidRectPushConstants) (see solidRectPipelineLayout), so any drift
// here silently corrupts the shader's view — keep this assert green.
static_assert(sizeof(SolidRectPushConstants) == 224, "SolidRectPushConstants must stay in lockstep with the HLSL push_constant block (224 bytes)");

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
    // 2x3 affine transform: world.x = m11*x + m12*y + dx;
    //                        world.y = m21*x + m22*y + dy.
    // Packed as two float4 (.w slots are unused/padding) so std430 layout
    // matches the GLSL push-constant block one-for-one. Lets the vertex
    // shader transform local-space path coords on the GPU instead of
    // having the CPU pre-transform every vertex on each FillPath hit.
    float transformRow0[4]; // (m11, m12, dx, _)
    float transformRow1[4]; // (m21, m22, dy, _)
};

// Layout matches triangle_fill_vc.{vert,frag}.hlsl push-constant block:
//   float4 color;          // multiplied with per-vertex color in fragment shader
//   float2 screenSize;     // for clip-space mapping
//   float2 padding;
//   float4 roundedClipRect;
//   float2 roundedClipRadius;
//   float2 clipFlags;      // clipFlags.x toggles the rounded-clip test
// Vertices are passed PRE-TRANSFORMED in screen / pixel space — the vc
// shader does not multiply by any matrix, so callers must apply the active
// transform CPU-side before recording.
struct TriangleFillVcPushConstants {
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

// Custom-shader-effect vertex-shader push constant (32 bytes): positions the
// fullscreen quad over the element rect and scales the element-local 0..1 UV
// into the captured-content sub-region of the (possibly larger) upload image.
struct CustomShaderGeomPushConstants {
    float rect[4];        // x, y, w, h (screen px)
    float screenSize[2];  // viewport px
    float uvScale[2];     // pixelW/uploadW, pixelH/uploadH
};

// Dedicated text-glyph pipeline push constant (80 bytes). BYTE-MATCHES the HLSL
// `TextPushConstants` in shaders/text_glyph.{vert,frag}.hlsl — each HLSL float2
// is a float[2] here and each float4 a float[4], in the SAME order, so the
// push-constant block the VS and FS see is laid out identically (same style as
// BitmapQuadPushConstants above). screenSize feeds the VS NDC map; the two
// rounded-clip rects + radii + clipFlags drive the FS clip-discard.
struct TextPushConstants {
    float screenSize[2];
    float padding[2];
    float roundedClipRect[4];
    float roundedClipRadius[2];
    float clipFlags[2];
    float innerRoundedClipRect[4];
    float innerRoundedClipRadius[2];
    float padding2[2];
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
    PFN_vkResetDescriptorPool resetDescriptorPool = nullptr;
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
    PFN_vkCmdBlitImage cmdBlitImage = nullptr;
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

    // Timestamp queries — optional, gated by timingSupported_ at runtime.
    PFN_vkCreateQueryPool createQueryPool = nullptr;
    PFN_vkDestroyQueryPool destroyQueryPool = nullptr;
    PFN_vkGetQueryPoolResults getQueryPoolResults = nullptr;
    PFN_vkCmdResetQueryPool cmdResetQueryPool = nullptr;
    PFN_vkCmdWriteTimestamp cmdWriteTimestamp = nullptr;

    // ── Device-loss hardening (GPU switch / TDR / driver restart) ──────────
    //
    // Vulkan analogue of the D3D12 frameDeviceLost_ latch
    // (d3d12_direct_renderer.h) with one deliberate difference: D3D12 resets
    // its latch on every BeginFrame because GetDeviceRemovedReason re-tests
    // the device authoritatively each frame. Vulkan has no such query — the
    // only signal is a VK_ERROR_DEVICE_LOST / VK_ERROR_SURFACE_LOST_KHR from
    // an actual call, and a lost VkDevice never recovers — so the latch here
    // is STICKY for the render target's lifetime. Once set:
    //   * BeginDraw / EndDraw fail fast with JALIUM_ERROR_DEVICE_LOST (the
    //     managed recovery chain — Window.TryRecoverFromRenderPipelineFailure,
    //     backend-agnostic — disposes this RT and builds a fresh one),
    //   * DrawFrame / DrawReplayFrame / RecreateSwapchain refuse to touch the
    //     device again,
    //   * the shared deviceGeneration is marked lost so backend-owned ink
    //     objects on this device fail fast / skip their waits too.
    //
    // The latch deliberately does NOT fire for transient failures (staging
    // allocation, descriptor pool exhaustion, …): those map to
    // JALIUM_ERROR_INVALID_STATE at EndDraw so managed retries/recreates
    // without counting toward the device-lost backend-fallback threshold —
    // mirroring D3D12's "classify by removed reason" rule.
    bool deviceLost = false;

    // True only for an actual VK_ERROR_DEVICE_LOST — the VkDevice itself is
    // gone. VK_ERROR_SURFACE_LOST_KHR sets only deviceLost: the presentation
    // surface is dead (this RT can never present again, so the fail-fast and
    // the DEVICE_LOST report to managed are identical), but the VkDevice may
    // be perfectly healthy with frames still in flight. Teardown MUST keep
    // draining (vkDeviceWaitIdle) on a healthy device — skipping the drain
    // and destroying in-flight resources is exactly the driver-UAF class this
    // hardening exists to remove — and the shared generation must NOT be
    // marked lost (backend-owned ink objects on it stay fully usable).
    // Mirrors D3D12's rule: only an authoritative "removed" verdict justifies
    // skipping waits, never mere unusability of one window's swapchain.
    bool deviceGone = false;

    // Shared keep-alive for this RT's VkDevice/VkInstance (see
    // vulkan_ink_layer.h). Created in Initialize right after the queue is
    // obtained; from then on the generation owns device+instance teardown and
    // Destroy() releases it instead of calling vkDestroyDevice directly.
    std::shared_ptr<VulkanDeviceGeneration> deviceGeneration;

    // Owning backend, captured at Initialize so Destroy can unregister this
    // RT's device generation (the backend would otherwise pin a closed
    // window's VkDevice and hand its orphan device to new ink layers). The
    // backend always outlives its render targets — it is owned by the jalium
    // Context, which is destroyed only after all RTs.
    VulkanBackend* ownerBackend = nullptr;

    void MarkDeviceLost(VkResult result, const char* where, bool isDeviceGone)
    {
        if (!deviceLost) {
            VK_LOG("[Vulkan] device %s at %s (VkResult %d) — failing fast until the managed recovery chain rebuilds this render target",
                   isDeviceGone ? "lost" : "surface lost", where ? where : "?", static_cast<int>(result));
            deviceLost = true;
        }
        if (isDeviceGone) {
            deviceGone = true;
            if (deviceGeneration) {
                deviceGeneration->MarkLost();
            }
        }
    }

    // Pass-through classifier: latches the loss for terminal results, returns
    // the result unchanged so call sites keep their existing `!= VK_SUCCESS`
    // shape. VK_ERROR_OUT_OF_DATE_KHR / VK_SUBOPTIMAL_KHR are resize signals,
    // not device loss, and pass through unlatched.
    VkResult NoteVk(VkResult result, const char* where)
    {
        if (result == VK_ERROR_DEVICE_LOST || result == VK_ERROR_SURFACE_LOST_KHR) {
            MarkDeviceLost(result, where, /*isDeviceGone:*/ result == VK_ERROR_DEVICE_LOST);
        }
        return result;
    }

    // Unified gate predicate (the Vulkan analogue of D3D12's
    // CheckFrameDeviceAlive): folds in losses first observed by the ink
    // subsystem, which latches only the shared generation — e.g. a stylus
    // stroke's synchronous DispatchBrush hitting VK_ERROR_DEVICE_LOST between
    // frames. Without the fold-back the next frame would record fully and only
    // latch at its first classified call, wasting a frame of calls into the
    // dead driver.
    bool DeviceUsable()
    {
        if (deviceLost) {
            return false;
        }
        if (deviceGeneration && deviceGeneration->IsLost()) {
            // Generation losses are only ever true device losses.
            MarkDeviceLost(VK_ERROR_DEVICE_LOST, "deviceGeneration (ink-observed)", /*isDeviceGone:*/ true);
            return false;
        }
        return true;
    }

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
#ifdef _WIN32
    // GPU-resident copy of the CPU glyph atlas (B4). Single instance (not
    // per-frame): the text pipeline samples it through textDescriptorSet. Format
    // is R8G8B8A8_UNORM (DEVICE_LOCAL, TRANSFER_DST|SAMPLED) — distinct from the
    // swap-chain-derived upload image format. Recreated by EnsureTextAtlasImage
    // when the atlas grows; torn down once at device teardown via
    // DestroyTextAtlasImage. glyphAtlas_ owns the CPU rasterizer (DirectWrite);
    // its full type comes from vulkan_glyph_atlas.h included above.
    VkImage textAtlasImage = VK_NULL_HANDLE;
    VkDeviceMemory textAtlasImageMemory = VK_NULL_HANDLE;
    VkImageView textAtlasImageView = VK_NULL_HANDLE;
    VkImageLayout textAtlasImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t textAtlasWidth = 0;
    uint32_t textAtlasHeight = 0;
    std::unique_ptr<VulkanGlyphAtlas> glyphAtlas_;
    // Dedicated host-visible staging buffer for the glyph-atlas dirty-band upload
    // (B4b). Separate from the per-frame `stagingBuffer` (which now also carries
    // the glyph SSBO) so the atlas pixel upload and the glyph-instance SSBO never
    // alias the same bytes within a frame. Grow-only + persistently mapped, single
    // instance tied to the device — torn down with the atlas image at device
    // teardown. See EnsureTextAtlasStaging.
    VkBuffer textAtlasStagingBuffer = VK_NULL_HANDLE;
    VkDeviceMemory textAtlasStagingMemory = VK_NULL_HANDLE;
    void* textAtlasStagingMapped = nullptr;
    VkDeviceSize textAtlasStagingCapacity = 0;
#endif
    VkSampler frameSampler = VK_NULL_HANDLE;
    bool anisotropySupported = false;
    float deviceMaxAnisotropy = 1.0f;
    // dualSrcBlend feature. Gates the ClearType text pipeline's dual-source
    // colour blend (src=ONE / dst=ONE_MINUS_SRC1_COLOR) that composites
    // DirectWrite per-channel sub-pixel coverage exactly like the D3D12 textPSO_.
    // Optional in Vulkan: when false the text path falls back to grayscale
    // coverage (A=max(R,G,B)) with a single-source blend. Probed in Initialize.
    bool dualSrcBlendSupported = false;
    // VK_EXT_scalar_block_layout / scalarBlockLayout feature. The Vello compute
    // pipeline's SPIR-V is compiled with -fvk-use-dx-layout so its StructuredBuffer
    // strides match the C++ structs byte-for-byte (LineSoup 24, VelloPath 20, …) —
    // those tight layouts violate base std430 vec alignment and REQUIRE this
    // feature at runtime. Probed in Initialize; when false the Vello GPU compute
    // path is unavailable and the Vello engine falls back to the CPU tessellation
    // path (the Impeller-equivalent encoder).
    bool scalarBlockLayoutSupported = false;
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
    // Dedicated ink-layer composite pipeline. Same layout/descriptors/vertex
    // shader as bitmapPipeline but with PREMULTIPLIED-SrcOver blend
    // (srcColorBlendFactor = ONE) + the premultiplied FS (kInkCompositeFragSpv),
    // because the resident ink image is premultiplied (brush emits premul +
    // ink pipeline blends with srcColorBlendFactor = ONE). The shared
    // bitmapPipeline stays STRAIGHT-alpha for its straight producers (polygon /
    // stroke rasterizers, WIC/stb images). Created in EnsureGraphicsResources
    // right after bitmapPipeline; all-platform (the bug bites Android/Linux and
    // the Vulkan-on-Windows build that composite ink through this path).
    VkPipeline inkCompositePipeline = VK_NULL_HANDLE;
#ifdef _WIN32
    // Dedicated text-glyph pipeline (B4). Unlike the generic bitmap pipeline it
    // owns its own descriptor set layout (3 bindings: atlas SAMPLED_IMAGE FS,
    // sampler FS, glyph-instance STORAGE_BUFFER VS) so each glyph quad can sample
    // its own atlas sub-rect from a per-frame StructuredBuffer. Created lazily in
    // EnsureGraphicsResources; the FS emits PREMULTIPLIED colour*coverage so the
    // pipeline blend uses srcColorBlendFactor=ONE (see the creation block).
    // Windows-only: the glyph atlas (DirectWrite) and embedded SPIR-V are too.
    VkPipelineLayout textPipelineLayout = VK_NULL_HANDLE;
    VkPipeline textPipeline = VK_NULL_HANDLE;
    VkPipeline textClearTypePipeline = VK_NULL_HANDLE;  // ClearType dual-source variant (opt-in; null => grayscale fallback)
    VkDescriptorSetLayout textDescriptorSetLayout = VK_NULL_HANDLE;
    VkDescriptorPool textDescriptorPool = VK_NULL_HANDLE;
    VkDescriptorSet textDescriptorSet = VK_NULL_HANDLE;
#endif
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
    // Per-vertex-coloured triangle pipeline. Same render pass + blend state
    // as triangleFillPipeline, but vertex format is (vec2 pos + vec4 color)
    // and the fragment shader interpolates the vertex colour instead of
    // using a single push-constant color. Drives the DrawLine 3-strip AA
    // path so each strip can fade out at the feather edges. Shader SPIR-V
    // lives in vulkan_triangle_vc_shaders.h. Vertex data rides the same
    // per-frame staging buffer that bitmap/backdrop uploads use — no extra
    // VkBuffer needed; DrawReplayFrame computes the byte offset.
    VkPipelineLayout vcTrianglePipelineLayout = VK_NULL_HANDLE;
    VkPipeline vcTrianglePipeline = VK_NULL_HANDLE;
    // Engine-batches pipeline (VkImpellerVertex / VkVelloVertex: pos+color, 24B):
    // Used by RenderEngineBatches to drain Impeller / Vello engines' GetBatches()
    // into draw calls on the swap-chain. Vertex shader takes a 4×4 MVP push
    // constant; fragment shader is passthrough (premultiplied alpha).
    VkPipelineLayout engineBatchPipelineLayout = VK_NULL_HANDLE;
    VkPipeline engineBatchPipeline = VK_NULL_HANDLE;
    // ── GPU stencil-then-cover scratch (JALIUM_VK_STENCIL_PATH) ─────────────
    // Solid <Path>/polygon/stroke fills emitted as pipelineType==1 batches are
    // rendered with a two-pass stencil-then-cover into an offscreen N× MSAA
    // color + depth/stencil target, auto-resolved to a 1× image, then
    // composited onto the swap chain (premult SrcOver) — the Vulkan analogue of
    // ImpellerD3D12Engine::StencilThenCoverFill + the D3D12 path MSAA scratch.
    // Single shared instance (written + consumed within one frame's cmd buf),
    // sized to the swap-chain extent; built lazily by EnsureStencilCoverResources
    // and torn down by DestroyStencilCoverResources (called from
    // DestroyGraphicsResources, so resize + teardown both rebuild it). The 3
    // pipelines reuse engineBatchPipelineLayout (64B MVP push) + the solid-fill
    // VS/FS — no new shader, no per-draw transform (geometry is pixel-space).
    VkImage         stencilMsaaColorImage = VK_NULL_HANDLE;
    VkDeviceMemory  stencilMsaaColorMemory = VK_NULL_HANDLE;
    VkImageView     stencilMsaaColorView = VK_NULL_HANDLE;
    VkImage         stencilMsaaDsImage = VK_NULL_HANDLE;
    VkDeviceMemory  stencilMsaaDsMemory = VK_NULL_HANDLE;
    VkImageView     stencilMsaaDsView = VK_NULL_HANDLE;
    VkImage         stencilResolveImage = VK_NULL_HANDLE;
    VkDeviceMemory  stencilResolveMemory = VK_NULL_HANDLE;
    VkImageView     stencilResolveView = VK_NULL_HANDLE;
    VkSampler       stencilResolveSampler = VK_NULL_HANDLE;
    VkRenderPass    stencilCoverRenderPass = VK_NULL_HANDLE;
    VkFramebuffer   stencilCoverFramebuffer = VK_NULL_HANDLE;
    VkPipeline      psoStencilFillEvenOdd = VK_NULL_HANDLE;
    VkPipeline      psoStencilFillNonZero = VK_NULL_HANDLE;
    VkPipeline      psoStencilCover = VK_NULL_HANDLE;
    // MSAA variant of engineBatchPipeline: draws the indexed type0 (gradient /
    // ellipse / CPU-tessellated) batches into the same MSAA scratch as the
    // stencil paths so painter order is preserved with a single resolve+composite
    // (and gradients/ellipses pick up MSAA too). Stencil test disabled.
    VkPipeline      psoStencilQuad = VK_NULL_HANDLE;
    uint32_t        stencilCoverW = 0, stencilCoverH = 0;
    VkSampleCountFlagBits stencilSampleCount = VK_SAMPLE_COUNT_1_BIT;
    VkFormat        stencilDsFormat = VK_FORMAT_UNDEFINED;
    // -- env-gated GPU offscreen effect RT (JALIUM_VK_EFFECT_GPU_RT) ----------
    // Single screen-sized COLOR_ATTACHMENT|SAMPLED image reused every frame (like
    // the stencil scratch). Effected element subtrees render here (isolated,
    // transparent-cleared) so content effects sample the TRUE element instead of
    // the GPU-path halo approximation. Default OFF => none of this is created or
    // referenced (bit-identical).
    bool            effectOffscreenEnabled_ = false;   // set once in Initialize from env
    VkImage         effectOffscreenImage = VK_NULL_HANDLE;
    VkDeviceMemory  effectOffscreenMemory = VK_NULL_HANDLE;
    VkImageView     effectOffscreenView = VK_NULL_HANDLE;
    VkSampler       effectOffscreenSampler = VK_NULL_HANDLE;
    VkRenderPass    effectOffscreenRenderPassClear = VK_NULL_HANDLE; // loadOp=CLEAR (first draw)
    VkRenderPass    effectOffscreenRenderPassLoad  = VK_NULL_HANDLE; // loadOp=LOAD  (subsequent)
    VkFramebuffer   effectOffscreenFramebufferClear = VK_NULL_HANDLE;
    VkFramebuffer   effectOffscreenFramebufferLoad  = VK_NULL_HANDLE;
    VkImageLayout   effectOffscreenLayout = VK_IMAGE_LAYOUT_UNDEFINED; // tracked across frames
    uint32_t        effectOffscreenW = 0, effectOffscreenH = 0;
    VkDescriptorPool effectOffscreenDescPool = VK_NULL_HANDLE;       // 1-set pool for the composite/effect descriptor
    VkDescriptorSet  effectOffscreenDescriptorSet = VK_NULL_HANDLE;  // set0 (SAMPLED_IMAGE@0 + SAMPLER@1) bound to effectOffscreenView
    // Real Vello GPU compute pipeline (lazy; only when scalarBlockLayoutSupported)
    // plus the fullscreen-triangle pipeline that composites its RGBA32F output
    // image onto the swap chain with premultiplied SrcOver blend.
    std::unique_ptr<VelloComputePipeline> velloCompute_;
    VkDescriptorSetLayout velloCompositeDescSetLayout = VK_NULL_HANDLE;
    VkPipelineLayout      velloCompositePipelineLayout = VK_NULL_HANDLE;
    VkPipeline            velloCompositePipeline = VK_NULL_HANDLE;
    VkDescriptorPool      velloCompositeDescPools[2] = {};
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
    // Ink-layer blit: per-frame descriptor pools (frameDescriptorSetLayout:
    // sampled image + sampler) for compositing the resident ink image through
    // the bitmap pipeline. Sets are allocated FRESH each frame pointing at the
    // ink layer's *current* image view and the pool is reset at frame start —
    // never caching across frames, because VkImageView handles can be recycled
    // after an ink layer is resized/destroyed (a cached set would then dangle).
    // One pool per in-flight frame so a reset never touches sets still
    // referenced by the previous frame's command buffer. Sized by literal 2
    // (MAX_FRAMES_IN_FLIGHT is declared further down — same pattern as timing[2];
    // the static_assert there pins the two together).
    VkDescriptorPool inkBlitDescriptorPools[2] = {};

    // ── Custom pixel-shader effect (DrawShaderEffectFromSource) ────────────
    // Runtime-compiled (DXC) custom shaders. The framework VS + descriptor /
    // pipeline layouts are shared; one VkPipeline is cached per HLSL source
    // hash. Per-frame transient descriptor pools (reset at frame start, like
    // the ink blit) bind the upload image (t0@16), shared sampler (s0@32), and
    // a slice of the per-frame user-constants UBO (b0@0). Sized [2] = literal
    // MAX_FRAMES_IN_FLIGHT (declared later; static_assert below pins them).
    VulkanShaderCompiler customShaderCompiler;
    VkShaderModule        customShaderVs = VK_NULL_HANDLE;
    std::vector<uint32_t> customShaderVsSpirv;
    VkDescriptorSetLayout customShaderDescLayout = VK_NULL_HANDLE;
    VkPipelineLayout      customShaderPipelineLayout = VK_NULL_HANDLE;
    std::unordered_map<uint64_t, VkShaderModule> customShaderPsModules;
    std::unordered_map<uint64_t, VkPipeline>     customShaderPipelines;
    bool customShaderBaseAttempted = false;
    bool customShaderBaseReady = false;
    VkDescriptorPool customShaderDescPools[2] = {};
    VkBuffer         customShaderConstantsBuffers[2] = {};
    VkDeviceMemory   customShaderConstantsMemory[2] = {};
    void*            customShaderConstantsMapped[2] = {};
    VkDeviceSize     customShaderConstantsCapacity[2] = {};

    bool EnsureCustomShaderBase();
    VkPipeline EnsureCustomShaderPipeline(uint64_t hash, const char* hlslSource);

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
    // True when the swapchain was created with TRANSFER_SRC usage, so the
    // live-scene capture (CaptureLiveSceneToUpload) can blit the rendered colour
    // image into uploadImage for Backdrop/LiquidGlass instead of sampling the
    // empty CPU pixelBuffer_ snapshot. Set in RecreateSwapchain from the surface
    // capabilities; when false the effects fall back to the CPU-pixel source.
    bool sceneCaptureSupported = false;
    VkImageLayout uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uint32_t uploadWidth = 0;
    uint32_t uploadHeight = 0;
    bool submitted = false;
    // Alias of PerFrameState::fencePending (see the struct for semantics).
    bool fencePending = false;
    bool initialized = false;

    // ── Present-info cache (for VulkanRenderTarget::GetPresentInfo) ────
    // Captured at the tail of RecreateSwapchain so the host doesn't pay a
    // surface-capabilities query just to read these. Updated on every
    // RecreateSwapchain call (resize / vsync toggle).
    VkPresentModeKHR currentPresentMode = VK_PRESENT_MODE_FIFO_KHR;
    uint32_t swapImageCount = 0;

    // ── Frame-pacing wait accumulator ───────────────────────────────────
    // Mirrors D3D12DirectRenderer::accumulatingFrameWaitNs_ / lastFrameGpuWaitNs_
    // (see project_d3d12_frame_pacing_diagnostics memory entry). Sums the
    // wall-clock time spent inside vkWaitForFences + vkAcquireNextImageKHR
    // across every DrawFrame / DrawReplayFrame attempt for one logical
    // frame; when the attempt succeeds we flush the accumulator into
    // lastFrameWaitNs_, which QueryGpuStats hands to DevTools as
    // frameGpuWaitNs. Failed attempts (e.g. waitForFences returns
    // VK_TIMEOUT — currently unreachable because we pass UINT64_MAX, but
    // kept for symmetry with the D3D12 timeout-retry path) keep the
    // accumulator nonzero so the next attempt continues appending.
    uint64_t accumulatingWaitNs = 0;
    uint64_t lastFrameWaitNs = 0;

    // ── Present→ready wall clock ────────────────────────────────────────
    // Mirrors D3D12DirectRenderer::lastFramePresentToReadyNs (see
    // [[project_d3d12_frame_pacing_diagnostics]] v4 retrospective). Each
    // successful queueSubmit stamps lastSubmitMonotonicNs; the next frame's
    // BeginFrame, after observing the fence as complete, diffs that against
    // the current QPC to derive "wall clock between previous submit and the
    // moment this frame learned its GPU work was ready to drain".  NOT pure
    // GPU work — includes Vulkan WSI queue latency / DWM composition / vsync
    // alignment.  The hardware-timestamp totalGpuNs surfaced through
    // QueryGpuTiming is the canonical "GPU work" number.  Reset to 0 by
    // RecreateSwapchain so a stale submit timestamp from a destroyed swap
    // chain never feeds into the new one.
    uint64_t lastSubmitMonotonicNs = 0;
    uint64_t lastFramePresentToReadyNs = 0;

    // ── GPU timestamp queries ───────────────────────────────────────────
    // One VK_QUERY_TYPE_TIMESTAMP pool sized for kMaxTimingSlotsPerFrame
    // slots times MAX_FRAMES_IN_FLIGHT (we partition by currentFrame_).
    // Per-frame state mirrors D3D12's PerFrameTiming: how many slots were
    // written, what category opened each span, and whether the previous
    // frame's data is awaiting decode.
    //
    // Feature-gated by timingSupported_, set during Initialize when
    // VkPhysicalDeviceLimits::timestampPeriod > 0 AND the graphics queue
    // family advertises timestampValidBits > 0 AND query-pool creation
    // succeeds.  Failure is non-fatal: timingSupported_ stays false and
    // QueryGpuTiming reports timingValid == 0.
    static constexpr uint32_t kMaxTimingSlotsPerFrame = 64;
    enum class GpuTimingCategory : uint8_t {
        Other = 0,
        SdfRect = 1,
        Text = 2,
        Bitmap = 3,
        Path = 4,
        Backdrop = 5,
        LiquidGlass = 6,
        kCount = 7,
        kFrameEnd = 0xFF,
    };
    struct GpuTimingSnapshot {
        uint64_t totalNs = 0;
        uint64_t categoryNs[static_cast<size_t>(GpuTimingCategory::kCount)] = {};
        uint32_t batchCount = 0;
        bool valid = false;
    };
    struct PerFrameTiming {
        uint32_t nextSlot = 0;
        uint32_t batchCountAtFinalize = 0;
        bool hasResolvedData = false;
        std::vector<GpuTimingCategory> spanCategories;
    };

    VkQueryPool timingQueryPool = VK_NULL_HANDLE;
    bool timingSupported = false;
    float timestampPeriodNs = 0.0f;          // VkPhysicalDeviceLimits::timestampPeriod
    uint64_t timestampValidBitMask = 0;       // (timestampValidBits == 64) ? ~0 : (1<<bits)-1
    // Sized by literal 2 instead of MAX_FRAMES_IN_FLIGHT — the constexpr is
    // declared further down in the class body so using it here for an array
    // bound trips C++ "incomplete-class" rules. The static_assert below
    // pins the two definitions together: if anyone ever bumps the in-flight
    // count, the compile error here flags this field for matching update.
    PerFrameTiming timing[2];
    GpuTimingSnapshot lastGpuTimingSnapshot{};

    // Record a TIMESTAMP query at the current command-buffer position
    // tagging the span that opens with `category`. No-op when timing is
    // disabled or the per-frame budget is exhausted (drop & continue).
    void MarkGpuTimingPoint(GpuTimingCategory category);
    // After waitForFences observes the previous submission complete,
    // decode that frame's resolved timestamps into lastGpuTimingSnapshot_.
    void DecodeGpuTimingForCompletedFrame(uint32_t frameIndex);

    // Multi-frames-in-flight: each frame needs its own command buffer, fences,
    // semaphores, staging buffer (+mapped pointer), upload image, and descriptor set.
    // Alias model: the single-named fields above act as the "current frame" alias,
    // BeginFrame() copies perFrameStates_[currentFrame_] into the alias, CommitCurrentFrame()
    // writes alias back. Existing helpers (EnsureStagingBuffer / EnsureUploadImage /
    // UpdateFrameDescriptorSet) still operate on the alias with no changes.
    static constexpr uint32_t MAX_FRAMES_IN_FLIGHT = 2;
    static_assert(MAX_FRAMES_IN_FLIGHT == 2,
                  "PerFrameTiming timing[2] above must be resized in lockstep with MAX_FRAMES_IN_FLIGHT");
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
        // True while a queueSubmit owns this slot's inFlight fence (set after
        // a successful submit, cleared after the slot's waitForFences
        // completes). The frame wait is gated on it so a slot whose frame
        // failed anywhere — including queueSubmit itself failing AFTER the
        // fence reset, which leaves the fence unsignaled with nobody to
        // signal it — can never park waitForFences(UINT64_MAX) forever.
        bool fencePending = false;
        // Engine batches buffer (vertex+index, host-visible coherent):
        // RenderEngineBatches uploads VkImpellerDrawBatch contents here each
        // frame. Per-frame so we don't trample data the GPU is still reading
        // from the previous frame.
        VkBuffer engineBatchBuffer = VK_NULL_HANDLE;
        VkDeviceMemory engineBatchMemory = VK_NULL_HANDLE;
        void* engineBatchMapped = nullptr;
        VkDeviceSize engineBatchCapacity = 0;
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
    void ShrinkPerFrameBuffers();

    bool Initialize(const JaliumSurfaceDescriptor& surfaceDescriptor, int32_t width, int32_t height, bool vsync,
                    VulkanBackend* ownerBackend = nullptr);
    bool RecreateSwapchain(int32_t width, int32_t height, bool vsync);
    bool EnsureStagingCapacity(VkDeviceSize requiredSize);
    bool EnsureStagingBuffer(uint32_t width, uint32_t height);
    bool EnsureUploadImage(uint32_t width, uint32_t height);
    bool EnsureUploadImageCapacity(uint32_t width, uint32_t height);
    // Live-scene capture for Backdrop / LiquidGlass: blit the colour image
    // rendered so far (images[imageIndex], left in COLOR_ATTACHMENT_OPTIMAL by
    // the previous load-pass) into uploadImage's [0,dstWidth]x[0,dstHeight]
    // region — the exact region the CPU-pixel cmdCopyBufferToImage would have
    // filled — so the effect shader samples the real scene instead of the empty
    // GPU-path pixelBuffer_ snapshot. Leaves uploadImage in
    // SHADER_READ_ONLY_OPTIMAL and the colour image back in
    // COLOR_ATTACHMENT_OPTIMAL (ready for the next beginLoadRenderPass). Must be
    // called OUTSIDE a render pass. Returns false (caller keeps the CPU-pixel
    // upload) when capture is unavailable.
    //
    // The optional srcX/srcY/srcW/srcH select a SUB-RECT of the rendered colour
    // image to blit (physical px); srcW<=0 || srcH<=0 means "whole frame" (the
    // backdrop/liquid-glass callers, which sample via screen coords). Element
    // BlurEffect passes the element's screen rect so the blur shader — which maps
    // the element quad onto uploadImage[0,dst] — samples exactly the element's
    // composited content.
    bool CaptureLiveSceneToUpload(uint32_t imageIndex,
                                  const VkImageSubresourceRange& range,
                                  VkExtent2D srcExtent,
                                  uint32_t dstWidth, uint32_t dstHeight,
                                  int32_t srcX = 0, int32_t srcY = 0,
                                  int32_t srcW = 0, int32_t srcH = 0);
    bool EnsureTransitionImagesCapacity(uint32_t width, uint32_t height);
    bool EnsureGraphicsResources();
    bool DrawFrame(const uint8_t* pixels, uint32_t width, uint32_t height);
    bool DrawReplayFrame(const std::vector<VulkanRenderTarget::GpuReplayCommand>& commands,
                         const float clearColor[4],
                         const std::vector<VkImpellerDrawBatch>* engineBatches = nullptr,
                         const VelloScene* velloScene = nullptr);
    /// Lazy-create the fullscreen composite pipeline (samples the Vello output
    /// image, premultiplied SrcOver) and its per-frame descriptor pools.
    bool EnsureVelloCompositePipeline();
    /// Composite the Vello compute output image onto the swap-chain framebuffer.
    /// Must be called OUTSIDE an open render pass (it opens its own load-op pass).
    void CompositeVelloOutput(VkCommandBuffer cmd, VkImageView outputView, VkSampler sampler,
                              VkExtent2D extent, uint32_t frameIdx,
                              VkRenderPass renderPass, VkFramebuffer framebuffer);
    /// Lazy-create the POSITION+COLOR pipeline used to drain Impeller / Vello
    /// engine batches.
    bool EnsureEngineBatchPipeline();
    /// Lazy-grow the per-frame engine-batch upload buffer.
    bool EnsureEngineBatchBuffer(uint32_t frameIdx, VkDeviceSize requiredSize);
    /// Drain the supplied batches into draw calls on the current swap-chain
    /// frame. Caller is responsible for being inside an active command buffer
    /// and for ending any open render pass before invoking this.
    void RenderEngineBatches(VkCommandBuffer cmd,
                             const std::vector<VkImpellerDrawBatch>& batches,
                             VkExtent2D extent,
                             uint32_t frameIdx,
                             VkRenderPass renderPass,
                             VkFramebuffer framebuffer);
    /// Lazy-create the offscreen MSAA color + depth/stencil scratch, 1× resolve
    /// image, render pass, framebuffer and the 3 stencil-then-cover pipelines
    /// (fill EvenOdd / fill NonZero / cover), sized to `extent`. Reuses
    /// engineBatchPipelineLayout + the solid-fill VS/FS. Returns false on
    /// failure (caller falls back to skipping the GPU stencil path).
    bool EnsureStencilCoverResources(VkExtent2D extent);
    /// Destroy all stencil-then-cover scratch resources (images/views/memory,
    /// render pass, framebuffer, sampler, pipelines). Safe to call repeatedly.
    void DestroyStencilCoverResources();
    /// Lazy-create the screen-sized COLOR|SAMPLED offscreen effect RT + CLEAR/LOAD
    /// render passes + framebuffers + sampler, sized to `extent`. No-op returning
    /// false when effectOffscreenEnabled_ is false. Mirrors EnsureStencilCoverResources.
    bool EnsureEffectOffscreenResources(VkExtent2D extent);
    /// Destroy all offscreen effect RT resources. Safe to call repeatedly / when off.
    void DestroyEffectOffscreenResources();
    uint32_t FindMemoryType(uint32_t typeFilter, VkMemoryPropertyFlags requiredProperties) const;
    bool UpdateFrameDescriptorSet();
    bool UpdateTransitionDescriptorSet();
    void DestroyUploadImage();
#ifdef _WIN32
    // ── Dedicated text-glyph pipeline helpers (B4 infrastructure) ──────────
    // Lazily construct the CPU glyph atlas (DirectWrite). Returns nullptr when
    // the shared DWrite factory is unavailable. Idempotent.
    VulkanGlyphAtlas* EnsureGlyphAtlas();
    // (Re)create textAtlasImage at (w, h) — R8G8B8A8_UNORM, DEVICE_LOCAL,
    // TRANSFER_DST|SAMPLED. Early-outs when the cached image already matches.
    bool EnsureTextAtlasImage(uint32_t w, uint32_t h);
    void DestroyTextAtlasImage();
    // Point textDescriptorSet at the atlas image + shared sampler + the glyph
    // SSBO slice [glyphOffset, glyphOffset+glyphRange) of the staging buffer.
    bool UpdateTextDescriptorSet(VkDeviceSize glyphOffset, VkDeviceSize glyphRange);
    // Grow-only host-visible staging buffer for the glyph-atlas dirty-band copy
    // (B4b). Clones EnsureStagingCapacity (HOST_VISIBLE|COHERENT, persistently
    // mapped into textAtlasStagingMapped). Returns false on allocation failure.
    bool EnsureTextAtlasStaging(VkDeviceSize size);
    void DestroyTextAtlasStaging();
#endif
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
    , textCache_(std::make_unique<TextLruCache>(kMaxTextCacheEntries))
    , pathCache_(std::make_unique<PathGeometryCache>(kMaxPathCacheEntries))
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
    if (!impl_->Initialize(surface, width, height, vsyncEnabled_, backend_)) {
        VK_LOG("[Vulkan] VulkanRenderTarget: initialization failed, GPU presentation will not work\n");
    }
    // Lazy-construct the default-active engine so the first frame already has
    // a valid encoder. With the Impl::RenderEngineBatches consumer in place,
    // batches now flow through to actual draw calls on the swap chain.
    // Failure here is non-fatal — the existing GPU-replay path keeps working
    // when the engine cannot initialize (e.g. impl_->Initialize failed above).
    if (activeEngine_ == JALIUM_ENGINE_IMPELLER) EnsureImpellerEngine();
    else if (activeEngine_ == JALIUM_ENGINE_VELLO) EnsureVelloEngine();
}

VulkanRenderTarget::~VulkanRenderTarget() = default;

bool VulkanRenderTarget::IsInitialized() const
{
    return impl_ && impl_->initialized;
}

JaliumResult VulkanRenderTarget::QueryGpuStats(JaliumGpuStats* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumGpuStats{};

#ifndef _WIN32
    // On non-Windows the Vulkan backend owns a cross-platform TextEngine
    // + GlyphAtlas (FreeType / HarfBuzz). Windows uses GDI text
    // rasterization fed through TextLruCache — see #else branch.
    if (backend_) {
        if (auto* textEngine = backend_->GetTextEngine()) {
            if (auto* atlas = textEngine->GetGlyphAtlas()) {
                out->glyphSlotsUsed = atlas->GetCacheEntryCount();
                out->glyphSlotsTotal = atlas->GetEstimatedCapacity();
                out->glyphBytes = atlas->GetPackedBytes();
                out->textureCount = 1;
                out->textureBytes = atlas->GetTotalBytes();
            }
        }
    }
#else
    // Windows Vulkan path uses a GDI-rendered bitmap text cache instead of a
    // glyph atlas. TextLruCache exposes O(1) accessors for size and total
    // bytes so we don't have to walk the entries here.
    if (textCache_) {
        out->glyphSlotsUsed  = static_cast<int32_t>(textCache_->Size());
        out->glyphSlotsTotal = static_cast<int32_t>(textCache_->Capacity());
        out->glyphBytes      = textCache_->TotalBytes();
        out->textureBytes    = textCache_->TotalBytes();
        out->textureCount    = static_cast<int32_t>(textCache_->Size());
    }
#endif

    // Path cache — Impeller tessellates per-frame; Vello is compute-based.
    // We only surface the count if the Impeller engine is alive.
    if (impellerEngine_) {
        out->pathEntries = static_cast<int32_t>(impellerEngine_->GetEncodedPathCount());
    }

    // Frame-pacing diagnostics (DevTools Perf tab "Frame pacing" block).
    //   - swapBufferCount: real swap-chain image count resolved during
    //     RecreateSwapchain (driver may round the requested count up to its
    //     minimum). Fallback to MAX_FRAMES_IN_FLIGHT when the swap chain
    //     hasn't been created yet — keeps a sensible non-zero value.
    //   - frameGpuWaitNs: wall-clock time the UI thread spent inside the
    //     last frame's vkWaitForFences + vkAcquireNextImageKHR. The Vulkan
    //     impl issues one big wait per frame (no D3D12-style retry loop
    //     yet), so this is effectively "GPU + DWM pacing" wall time.
    //   - swap-chain frame-latency waitable is a DXGI concept; Vulkan
    //     has no equivalent, so frameWaitableWaitNs stays 0 here.
    if (impl_) {
        out->swapBufferCount = impl_->swapImageCount > 0
            ? static_cast<int32_t>(impl_->swapImageCount)
            : static_cast<int32_t>(Impl::MAX_FRAMES_IN_FLIGHT);
        out->frameGpuWaitNs            = static_cast<int64_t>(impl_->lastFrameWaitNs);
        out->lastFramePresentToReadyNs = static_cast<int64_t>(impl_->lastFramePresentToReadyNs);
    } else {
        out->swapBufferCount = static_cast<int32_t>(Impl::MAX_FRAMES_IN_FLIGHT);
    }

    return JALIUM_OK;
}

JaliumResult VulkanRenderTarget::GetPresentInfo(JaliumPresentInfo* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumPresentInfo{};
    if (!impl_ || !impl_->initialized || impl_->swapImageCount == 0) {
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    // swapEffect 字段对 Vulkan 没有 DXGI 那种 enum；我们把 VkPresentModeKHR
    // 原值放进去（FIFO=2、MAILBOX=1、IMMEDIATE=0、FIFO_RELAXED=3），并在
    // DevTools 文案里通过 backend 类型分辨语义。这样 host 拿到的不是 0/3/4
    // 的 D3D12 数字，而是 Vulkan 原值，需要根据 backend 决定怎么解读。
    // 跟 D3D12 后端共用同一 struct 的代价就是这点歧义，但比每个 backend
    // 各自定一份 struct 更稳。
    out->swapEffect = static_cast<int32_t>(impl_->currentPresentMode);
    out->bufferCount = static_cast<int32_t>(impl_->swapImageCount);

    // Vulkan 的 IMMEDIATE present mode 等价于 D3D12 的 ALLOW_TEARING 路径
    //（驱动允许在 vsync 之外把帧 flush 出去）。这是 DevTools "tearing on"
    // 指示灯唯一关心的语义对应。
    out->tearingEnabled = (impl_->currentPresentMode == VK_PRESENT_MODE_IMMEDIATE_KHR) ? 1 : 0;

    // Vulkan 无 frame-latency-waitable HANDLE 对应物（VK_EXT_present_wait /
    // VK_GOOGLE_display_timing 是可选 extension，本框架尚未启用），所以这两
    // 个字段固定为 0。host 据此渲染成 "—" 而不是 "0 frame latency"。
    out->waitableEnabled = 0;
    out->maxFrameLatency = 0;

    out->composition = isComposition_ ? 1 : 0;
    return JALIUM_OK;
}

JaliumResult VulkanRenderTarget::QueryGpuTiming(JaliumGpuTimingStats* out) const
{
    if (!out) return JALIUM_ERROR_INVALID_ARGUMENT;
    *out = JaliumGpuTimingStats{};
    if (!impl_ || !impl_->timingSupported) {
        // Hardware doesn't support TIMESTAMP queries on the graphics queue, or
        // query-pool creation failed at Impl::Initialize. NOT_SUPPORTED tells
        // the managed caller "no breakdown ever, don't keep polling".
        return JALIUM_ERROR_NOT_SUPPORTED;
    }

    auto snap = impl_->lastGpuTimingSnapshot;
    if (!snap.valid) {
        // Successful frame hasn't completed yet — JALIUM_OK + timingValid=0
        // matches D3D12's contract for "no breakdown yet".
        out->timingValid = 0;
        return JALIUM_OK;
    }

    using Cat = Impl::GpuTimingCategory;
    out->totalGpuNs     = static_cast<int64_t>(snap.totalNs);
    out->sdfRectNs      = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::SdfRect)]);
    out->textNs         = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Text)]);
    out->bitmapNs       = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Bitmap)]);
    out->pathNs         = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Path)]);
    out->backdropNs     = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Backdrop)]);
    out->liquidGlassNs  = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::LiquidGlass)]);
    out->otherNs        = static_cast<int64_t>(snap.categoryNs[static_cast<size_t>(Cat::Other)]);
    out->batchCount     = static_cast<int32_t>(snap.batchCount);
    out->timingValid    = 1;
    return JALIUM_OK;
}

void VulkanRenderTarget::Impl::MarkGpuTimingPoint(GpuTimingCategory category)
{
    if (!timingSupported || !cmdWriteTimestamp || timingQueryPool == VK_NULL_HANDLE) return;
    auto& tf = timing[currentFrame_];
    if (tf.nextSlot >= kMaxTimingSlotsPerFrame) return;  // budget exhausted — drop.

    uint32_t slot = currentFrame_ * kMaxTimingSlotsPerFrame + tf.nextSlot;
    // BOTTOM_OF_PIPE_BIT measures *after* all prior commands for this slot
    // finish — matches D3D12 EndQuery semantics so the deltas are
    // apples-to-apples across backends.
    cmdWriteTimestamp(commandBuffer, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT, timingQueryPool, slot);
    tf.spanCategories.push_back(category);
    tf.nextSlot += 1;
}

void VulkanRenderTarget::Impl::DecodeGpuTimingForCompletedFrame(uint32_t frameIndex)
{
    if (!timingSupported || !getQueryPoolResults || timingQueryPool == VK_NULL_HANDLE
        || frameIndex >= MAX_FRAMES_IN_FLIGHT) {
        return;
    }
    auto& tf = timing[frameIndex];
    if (!tf.hasResolvedData || tf.nextSlot < 2) {
        return;
    }

    // Snap the values out of the query pool into a stack array. WAIT bit not
    // set because the fence on this slot already completed (the caller of
    // DecodeGpuTimingForCompletedFrame is BeginFrame after vkWaitForFences).
    std::vector<uint64_t> timestamps(tf.nextSlot, 0);
    uint32_t base = frameIndex * kMaxTimingSlotsPerFrame;
    VkResult r = NoteVk(getQueryPoolResults(
        device, timingQueryPool,
        base, tf.nextSlot,
        timestamps.size() * sizeof(uint64_t),
        timestamps.data(),
        sizeof(uint64_t),
        VK_QUERY_RESULT_64_BIT), "DecodeGpuTiming.getQueryPoolResults");
    if (r != VK_SUCCESS) {
        // VK_NOT_READY can happen on first-frame races; we just skip and
        // try again next frame (NoteVk has already latched a DEVICE_LOST).
        // Mark consumed so we don't busy-loop.
        tf.hasResolvedData = false;
        return;
    }

    // Mask off invalid bits (queues with timestampValidBits < 64 leave the
    // upper bits as garbage — the standard requires us to ignore them).
    if (timestampValidBitMask != std::numeric_limits<uint64_t>::max()) {
        for (auto& v : timestamps) v &= timestampValidBitMask;
    }

    GpuTimingSnapshot snap;
    snap.batchCount = tf.batchCountAtFinalize;
    auto ticksToNs = [this](uint64_t ticks) -> uint64_t {
        // VkPhysicalDeviceLimits::timestampPeriod is "the number of ns
        // each tick represents" — a floating-point value (typically 1.0
        // on discrete NVIDIA, ~80 on certain mobile GPUs). Multiplying by
        // a float keeps full 64-bit ns range without precision loss on
        // realistic frame durations (under 1 second).
        return static_cast<uint64_t>(static_cast<double>(ticks) * static_cast<double>(timestampPeriodNs));
    };

    for (uint32_t i = 0; i + 1 < tf.nextSlot; ++i) {
        GpuTimingCategory cat = tf.spanCategories[i];
        if (cat == GpuTimingCategory::kFrameEnd) continue;
        uint64_t a = timestamps[i];
        uint64_t b = timestamps[i + 1];
        if (b <= a) continue;
        uint64_t ns = ticksToNs(b - a);
        size_t catIdx = static_cast<size_t>(cat);
        if (catIdx < static_cast<size_t>(GpuTimingCategory::kCount)) {
            snap.categoryNs[catIdx] += ns;
        }
    }
    if (tf.nextSlot >= 2) {
        uint64_t total = timestamps[tf.nextSlot - 1] - timestamps[0];
        snap.totalNs = ticksToNs(total);
    }
    snap.valid = true;
    lastGpuTimingSnapshot = snap;
    tf.hasResolvedData = false;
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
    if (impl_ && impl_->RecreateSwapchain(width, height, vsyncEnabled_)) {
        return JALIUM_OK;
    }
    // Mirror the D3D12 Resize classification (ResizeBuffers failure →
    // GetDeviceRemovedReason discrimination): a swapchain rebuild that failed
    // because the device/surface is gone must surface as DEVICE_LOST so the
    // managed recovery chain rebuilds the whole context on the first round
    // instead of burning retries on RESOURCE_CREATION_FAILED.
    return (impl_ && impl_->deviceLost) ? JALIUM_ERROR_DEVICE_LOST
                                        : JALIUM_ERROR_RESOURCE_CREATION_FAILED;
}

JaliumResult VulkanRenderTarget::ReclaimIdleResources()
{
    // Both caches store payloads as std::shared_ptr — any in-flight GPU
    // command that referenced an entry holds its own strong ref through the
    // PerFrameState lifetime, so dropping the cache lookup table here cannot
    // dangle a pointer the swapchain still needs. That's why this is safe
    // between frames without vkDeviceWaitIdle.
    //
    // Rebuilding cost on the next frame:
    //   * PathGeometryCache miss → re-decompose Bezier + ear-clip (O(N³) per
    //     path, but typically <200 verts → sub-millisecond per icon).
    //   * TextLruCache miss → one GDI draw + readback per text run.
    // Both are amortized over the IdleTimeoutMs window, which is why the
    // managed reclaimer's default (2 s) is far above any user-visible
    // latency budget.

    if (pathCache_) {
        pathCache_->Clear();
    }

    if (textCache_) {
        textCache_->Clear();
    }

#ifndef _WIN32
    // Cross-platform GlyphAtlas (FreeType + HarfBuzz) used on Linux/Android.
    // Clear() drops the cache map + dirty-rect list and zeroes the CPU-side
    // atlas pixel buffer. The GPU-side atlas texture (owned by Impl) keeps
    // its existing memory but its contents will be progressively overwritten
    // as new dirty rects are uploaded for re-rasterized glyphs. Frame-safe
    // because GPU-bound glyph quads carry their UV coordinates in the
    // already-recorded vertex buffer; they are not re-fetched from the
    // cache on the in-flight frame.
    if (backend_) {
        if (auto* textEngine = backend_->GetTextEngine()) {
            if (auto* atlas = textEngine->GetGlyphAtlas()) {
                atlas->Clear();
            }
        }
    }
#endif

    // Per-frame VkBuffer / VkImage / VkDeviceMemory release (staging buffer,
    // upload image, engine batch buffer for each MAX_FRAMES_IN_FLIGHT slot).
    // These grow lazily to accommodate peak desktop captures and Impeller
    // batch sizes; on a now-idle UI the peak capacity may be tens of MB of
    // pinned host memory plus the matching GPU-resident texture. Shrinking
    // here calls vkDeviceWaitIdle (cheap when nothing is in flight) and then
    // destroys+forgets the per-frame buffers; EnsureStagingCapacity /
    // EnsureUploadImage / EnsureEngineBatchBuffer re-allocate from scratch
    // on the next draw. Per-frame fences, semaphores, and command buffers
    // are intentionally preserved.
    if (impl_) {
        impl_->ShrinkPerFrameBuffers();
    }

    return JALIUM_OK;
}

// ---------------------------------------------------------------------------
// Engine lazy initialization
//
// The Impeller / Vello engine classes share their CPU algorithms with the
// D3D12 engines through jalium_impeller_stroke.h / jalium_impeller_shapes.h
// / jalium_scanline_rasterizer.h. The Vulkan GPU consumer that turns the
// engine-emitted batches into actual draws on the swap chain is implemented
// in Impl::RenderEngineBatches (called from DrawReplayFrame after all
// GPU-replay commands have been emitted, so the engines paint over the
// existing framebuffer contents).
//
// SetRenderingEngine() (in vulkan_render_target.h) calls Ensure* whenever
// the user toggles RenderContext.DefaultRenderingEngine; the helpers
// construct the chosen engine on demand. FillPath / FillPolygon /
// StrokePath route through the engine when impellerEngine_ / velloEngine_
// is alive and the active engine matches.
// ---------------------------------------------------------------------------
bool VulkanRenderTarget::EnsureImpellerEngine()
{
    if (impellerEngine_) return true;
    if (!impl_ || impl_->device == VK_NULL_HANDLE || impl_->physicalDevice == VK_NULL_HANDLE) {
        return false;
    }
    impellerEngine_ = std::make_unique<ImpellerVulkanEngine>(
        impl_->device, impl_->physicalDevice);
    if (!impellerEngine_->Initialize()) {
        impellerEngine_.reset();
        return false;
    }
    impellerEngine_->BeginFrame(static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
    return true;
}

bool VulkanRenderTarget::EnsureVelloEngine()
{
    if (velloEngine_) return true;
    if (!impl_ || impl_->device == VK_NULL_HANDLE || impl_->physicalDevice == VK_NULL_HANDLE
        || impl_->queue == VK_NULL_HANDLE
        || impl_->queueFamilyIndex == VK_QUEUE_FAMILY_IGNORED) {
        return false;
    }
    velloEngine_ = std::make_unique<VelloVulkanEngine>(
        impl_->device, impl_->physicalDevice,
        impl_->queue, impl_->queueFamilyIndex);
    if (!velloEngine_->Initialize()) {
        velloEngine_.reset();
        return false;
    }
    // Bring up the real Vello GPU compute pipeline when the device supports
    // scalarBlockLayout (its dx-layout SPIR-V requires it). On success the
    // engine switches to producing the GPU scene; otherwise it keeps the CPU
    // tessellation batch path (the Impeller-equivalent graceful fallback).
    if (impl_->scalarBlockLayoutSupported) {
        if (!impl_->velloCompute_ && impl_->getPhysicalDeviceMemoryProperties) {
            VkPhysicalDeviceMemoryProperties memProps{};
            impl_->getPhysicalDeviceMemoryProperties(impl_->physicalDevice, &memProps);
            auto compute = std::make_unique<VelloComputePipeline>();
            if (compute->Initialize(impl_->device, impl_->physicalDevice,
                                    impl_->getDeviceProcAddr, memProps)) {
                impl_->velloCompute_ = std::move(compute);
            }
        }
        velloEngine_->SetComputeMode(impl_->velloCompute_ != nullptr);
    }
    velloEngine_->BeginFrame(static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
    return true;
}

JaliumResult VulkanRenderTarget::BeginDraw()
{
    if (isDrawing_) {
        return JALIUM_ERROR_INVALID_STATE;
    }

    // Device gate (mirror of D3D12RenderTarget::BeginDraw's CheckDeviceStatus
    // guard): once the device is lost this target can never present again, so
    // refuse before mutating any frame state. Managed treats DEVICE_LOST as a
    // recoverable pipeline failure and rebuilds the render target; this gate
    // covers the window where recovery is throttled/backed-off and render
    // ticks keep arriving.
    if (impl_ && !impl_->DeviceUsable()) {
        return JALIUM_ERROR_DEVICE_LOST;
    }

    // Apply pending engine switch at frame boundary
    if (pendingEngine_ != activeEngine_) {
        activeEngine_ = pendingEngine_;
        // Make sure the newly-active engine exists by the time the first
        // Encode call comes in below.
        if (activeEngine_ == JALIUM_ENGINE_IMPELLER) EnsureImpellerEngine();
        else if (activeEngine_ == JALIUM_ENGINE_VELLO) EnsureVelloEngine();
    }

    isDrawing_ = true;
    ResetGpuReplay();
    // Safety net: clear any SuperEllipse shape state left over from a previous
    // frame (the managed Border resets it after each draw, but an aborted frame
    // could otherwise leak shapeType==1 into the next frame's plain rects).
    currentShapeType_ = 0;
    currentShapeExponent_ = 4.0f;
    // Reset engine batches for the new frame.
    if (impellerEngine_) {
        impellerEngine_->BeginFrame(static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
    }
    if (velloEngine_) {
        velloEngine_->BeginFrame(static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
    }
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

    // Device gate (mirror of D3D12RenderTarget::EndDraw's entry guard): the
    // device can be lost mid-frame, after BeginDraw's gate passed — GPU
    // switch, TDR, driver restart. Abort the frame on the CPU side without
    // touching the device at all and report DEVICE_LOST; managed already
    // returns the consumed present credit on every non-OK EndDraw result, so
    // skipping the present keeps pacing credits balanced.
    if (impl_ && !impl_->DeviceUsable()) {
        if (impellerEngine_) impellerEngine_->ClearBatches();
        if (velloEngine_) velloEngine_->ClearBatches();
        // Drop the recorded replay commands too (the D3D12 AbortFrame clears
        // its instance containers for the same reason): they can carry
        // multi-MB pixel snapshots (Backdrop/Transition/Bitmap), and the
        // usual reclaim point — the next BeginDraw's ResetGpuReplay — is
        // unreachable behind the BeginDraw device gate on this latched RT.
        gpuReplayCommands_.clear();
        cpuRasterNeededLastFrame_ = cpuRasterNeeded_;
        isDrawing_ = false;
        dirtyRects_.clear();
        return JALIUM_ERROR_DEVICE_LOST;
    }

    // Pick the active rendering engine's pending work so the GPU replay pass can
    // drain it after its own commands. Only one engine is active at a time.
    // Vello in compute mode hands over a scene (consumed by the GPU compute graph
    // + composite); everything else hands over CPU-tessellation triangle batches.
    const std::vector<VkImpellerDrawBatch>* engineBatches = nullptr;
    const VelloScene* velloScene = nullptr;
    if (IsImpellerActive() && impellerEngine_ && impellerEngine_->HasPendingWork()) {
        engineBatches = &impellerEngine_->GetBatches();
    } else if (IsVelloActive() && velloEngine_ && velloEngine_->HasPendingWork()) {
        if (velloEngine_->IsComputeMode() && impl_ && impl_->velloCompute_) {
            velloEngine_->FinalizeScene();
            velloScene = &velloEngine_->GetScene();
        } else {
            engineBatches = &velloEngine_->GetBatches();
        }
    }

    bool ok = false;
    if (impl_) {
        if (!impl_->initialized) {
            VK_LOG("[Vulkan] EndDraw: impl not initialized, skipping draw");
        } else if (gpuReplaySupported_ && gpuReplayHasClear_) {
            ok = impl_->DrawReplayFrame(gpuReplayCommands_, clearColor_, engineBatches, velloScene);
        } else {
            EnsureCpuRasterization();
            ok = impl_->DrawFrame(pixelBuffer_.data(), static_cast<uint32_t>(width_), static_cast<uint32_t>(height_));
        }
    }

    // Reset engine batches for the next frame — we just consumed them (or
    // dropped them if the frame fell back to the CPU upload path).
    if (impellerEngine_) impellerEngine_->ClearBatches();
    if (velloEngine_) velloEngine_->ClearBatches();

    cpuRasterNeededLastFrame_ = cpuRasterNeeded_;
    isDrawing_ = false;
    dirtyRects_.clear();
    fullInvalidation_ = false;
    if (ok) {
        return JALIUM_OK;
    }
    // Classify the failure the way D3D12's EndFrame does: only an actual
    // device/surface loss (latched by NoteVk inside DrawFrame /
    // DrawReplayFrame) reports DEVICE_LOST — that makes managed recovery
    // forceNewContext on the first round and counts toward the backend
    // fallback threshold. Transient failures (staging/upload allocation,
    // pipeline creation, …) report INVALID_STATE instead: still recoverable
    // (the managed predicate accepts it), but as a same-context retry that
    // can't escalate a healthy device into a permanent Software fallback.
    return (impl_ && impl_->deviceLost) ? JALIUM_ERROR_DEVICE_LOST
                                        : JALIUM_ERROR_INVALID_STATE;
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

// env helper for the GPU offscreen effect RT (JALIUM_VK_EFFECT_GPU_RT). Defined
// before Initialize so it is visible to every use site (Initialize / replay).
static bool VulkanEffectGpuRtEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        char* e = nullptr; size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_EFFECT_GPU_RT") != 0 || e == nullptr) { if (e) free(e); return false; }
        const bool on = (e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N');
        free(e); return on;
#else
        const char* e = std::getenv("JALIUM_VK_EFFECT_GPU_RT");
        return e && e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N';
#endif
    }();
    return enabled;
}

bool VulkanRenderTarget::Impl::Initialize(const JaliumSurfaceDescriptor& surfaceDescriptor, int32_t width, int32_t height, bool vsync,
                                          VulkanBackend* ownerBackend)
{
    effectOffscreenEnabled_ = VulkanEffectGpuRtEnabled();
    // Stash the backend pointer so we can publish the chosen physical device
    // to it once selection succeeds (see the RegisterAdapterInfo call below).
    VulkanBackend* backend = ownerBackend;
#if !defined(_WIN32) && !defined(__linux__) && !defined(__ANDROID__)
    (void)surfaceDescriptor;
    (void)width;
    (void)height;
    (void)vsync;
    (void)backend;
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
    // 1.1 so vkGetPhysicalDeviceFeatures2 is available in core (used to probe
    // scalarBlockLayout for the Vello compute pipeline). Loaders supporting 1.1
    // are universal; physical devices may still be any version.
    appInfo.apiVersion = VK_API_VERSION_1_1;

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

    // Probe device capabilities so we can opt the bitmap sampler into
    // anisotropic filtering when the GPU supports it. Without this opt-in
    // VK_TRUE on samplerAnisotropy in pEnabledFeatures, the validation
    // layers reject any sampler with anisotropyEnable=VK_TRUE.
    auto getPhysicalDeviceFeatures = LoadInstanceProc<PFN_vkGetPhysicalDeviceFeatures>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceFeatures");
    auto getPhysicalDeviceProperties = LoadInstanceProc<PFN_vkGetPhysicalDeviceProperties>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceProperties");
    VkPhysicalDeviceFeatures supportedFeatures {};
    if (getPhysicalDeviceFeatures) {
        getPhysicalDeviceFeatures(physicalDevice, &supportedFeatures);
    }
    VkPhysicalDeviceProperties physProps {};
    if (getPhysicalDeviceProperties) {
        getPhysicalDeviceProperties(physicalDevice, &physProps);
    }
    anisotropySupported = supportedFeatures.samplerAnisotropy == VK_TRUE;
    deviceMaxAnisotropy = anisotropySupported ? physProps.limits.maxSamplerAnisotropy : 1.0f;
    // dualSrcBlend gates true ClearType sub-pixel text via a dual-source colour
    // blend. Optional core feature; when absent the text pipeline uses grayscale
    // AA coverage instead. Probed here, enabled below, consumed by the text PSO.
    dualSrcBlendSupported = supportedFeatures.dualSrcBlend == VK_TRUE;
    VK_LOG("[Vulkan] dualSrcBlend %s - ClearType text uses %s",
           dualSrcBlendSupported ? "supported" : "UNsupported",
           dualSrcBlendSupported ? "dual-source sub-pixel" : "grayscale fallback");

    // Probe VK_EXT_scalar_block_layout / scalarBlockLayout — gates the Vello GPU
    // compute pipeline (its dx-layout SPIR-V needs scalar block layout). Feature2
    // is core in the 1.1 instance we created above.
    bool scalarBlockLayoutExtNeeded = false;  // enable the EXT string only pre-1.2
    {
        auto getFeatures2 = LoadInstanceProc<PFN_vkGetPhysicalDeviceFeatures2>(
            getInstanceProcAddr, instance, "vkGetPhysicalDeviceFeatures2");
        auto enumDeviceExt = LoadInstanceProc<PFN_vkEnumerateDeviceExtensionProperties>(
            getInstanceProcAddr, instance, "vkEnumerateDeviceExtensionProperties");
        bool scalarExtAdvertised = false;
        if (enumDeviceExt) {
            uint32_t extCount = 0;
            enumDeviceExt(physicalDevice, nullptr, &extCount, nullptr);
            std::vector<VkExtensionProperties> exts(extCount);
            if (extCount) enumDeviceExt(physicalDevice, nullptr, &extCount, exts.data());
            for (const auto& e : exts) {
                if (std::strcmp(e.extensionName, "VK_EXT_scalar_block_layout") == 0) {
                    scalarExtAdvertised = true;
                    break;
                }
            }
        }
        // scalarBlockLayout is available either via the EXT extension (pre-1.2) or
        // as core in Vulkan 1.2+. Only query the feature when one of those holds.
        if (getFeatures2 && (scalarExtAdvertised || physProps.apiVersion >= VK_API_VERSION_1_2)) {
            VkPhysicalDeviceScalarBlockLayoutFeatures scalarFeat{};
            scalarFeat.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES;
            VkPhysicalDeviceFeatures2 feat2{};
            feat2.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2;
            feat2.pNext = &scalarFeat;
            getFeatures2(physicalDevice, &feat2);
            scalarBlockLayoutSupported = scalarFeat.scalarBlockLayout == VK_TRUE;
        }
        // Remember whether the extension STRING must be enabled at device create
        // (pre-1.2 only; 1.2+ exposes the promoted struct without the extension).
        scalarBlockLayoutExtNeeded = scalarBlockLayoutSupported &&
                                     scalarExtAdvertised &&
                                     physProps.apiVersion < VK_API_VERSION_1_2;
    }

    // GPU timing infrastructure (best-effort). Three preconditions must hold:
    //   1. The device advertises a non-zero timestampPeriod (ns per tick).
    //   2. The chosen graphics queue family advertises timestampValidBits > 0
    //      — pure compute queues sometimes have 0 here.
    //   3. CreateQueryPool below succeeds.
    // When any of these fail we leave timingSupported_ == false and the
    // renderer keeps working; QueryGpuTiming simply reports timingValid=0.
    timestampPeriodNs = physProps.limits.timestampPeriod;
    {
        // Probe the queue family's timestampValidBits the cheap way: re-query
        // the queue family properties we already iterated above. (Re-doing
        // the call here is simpler than threading the value out of the
        // selection loop.)
        uint32_t queueFamilyCount = 0;
        getPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, nullptr);
        std::vector<VkQueueFamilyProperties> queueFamilyProps(queueFamilyCount);
        getPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, queueFamilyProps.data());
        uint32_t validBits = (queueFamilyIndex < queueFamilyCount)
            ? queueFamilyProps[queueFamilyIndex].timestampValidBits
            : 0;
        if (validBits == 64) {
            timestampValidBitMask = std::numeric_limits<uint64_t>::max();
        } else if (validBits > 0) {
            timestampValidBitMask = (uint64_t(1) << validBits) - 1u;
        } else {
            timestampValidBitMask = 0;
        }
    }

    // Publish the chosen adapter to the backend so future GetAdapterInfo
    // calls can hand out a cached snapshot without re-enumerating physical
    // devices. Need both VkPhysicalDeviceProperties (already fetched in
    // physProps) and VkPhysicalDeviceMemoryProperties for the VRAM split.
    if (backend && getPhysicalDeviceMemoryProperties) {
        VkPhysicalDeviceMemoryProperties memProps{};
        getPhysicalDeviceMemoryProperties(physicalDevice, &memProps);
        backend->RegisterAdapterInfo(physicalDevice, physProps, memProps);
    }

    VkPhysicalDeviceFeatures enabledFeatures {};
    enabledFeatures.samplerAnisotropy = anisotropySupported ? VK_TRUE : VK_FALSE;
    enabledFeatures.dualSrcBlend      = dualSrcBlendSupported ? VK_TRUE : VK_FALSE;

    const float queuePriority = 1.0f;
    VkDeviceQueueCreateInfo queueInfo {};
    queueInfo.sType = VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO;
    queueInfo.queueFamilyIndex = queueFamilyIndex;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &queuePriority;

    std::vector<const char*> deviceExtensions = { "VK_KHR_swapchain" };
    if (scalarBlockLayoutExtNeeded) {
        deviceExtensions.push_back("VK_EXT_scalar_block_layout");
    }
    // Enable scalarBlockLayout via the promoted feature struct (works on 1.2+
    // core and on 1.1 with the EXT extension enabled above). Chained alongside
    // pEnabledFeatures — that is legal as long as the pNext chain itself does
    // not carry a VkPhysicalDeviceFeatures2.
    VkPhysicalDeviceScalarBlockLayoutFeatures scalarBlockFeature{};
    scalarBlockFeature.sType = VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SCALAR_BLOCK_LAYOUT_FEATURES;
    scalarBlockFeature.scalarBlockLayout = VK_TRUE;

    VkDeviceCreateInfo deviceInfo {};
    deviceInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;
    deviceInfo.enabledExtensionCount = static_cast<uint32_t>(deviceExtensions.size());
    deviceInfo.ppEnabledExtensionNames = deviceExtensions.data();
    deviceInfo.pEnabledFeatures = &enabledFeatures;
    if (scalarBlockLayoutSupported) {
        deviceInfo.pNext = &scalarBlockFeature;
    }
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
    resetDescriptorPool = LoadDeviceProc<PFN_vkResetDescriptorPool>(getDeviceProcAddr, device, "vkResetDescriptorPool");
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
    cmdBlitImage = LoadDeviceProc<PFN_vkCmdBlitImage>(getDeviceProcAddr, device, "vkCmdBlitImage");
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

    // Timestamp-query entry points. Treated as optional — null pointers
    // disable the timing path without failing Initialize.
    createQueryPool      = LoadDeviceProc<PFN_vkCreateQueryPool>(getDeviceProcAddr, device, "vkCreateQueryPool");
    destroyQueryPool     = LoadDeviceProc<PFN_vkDestroyQueryPool>(getDeviceProcAddr, device, "vkDestroyQueryPool");
    getQueryPoolResults  = LoadDeviceProc<PFN_vkGetQueryPoolResults>(getDeviceProcAddr, device, "vkGetQueryPoolResults");
    cmdResetQueryPool    = LoadDeviceProc<PFN_vkCmdResetQueryPool>(getDeviceProcAddr, device, "vkCmdResetQueryPool");
    cmdWriteTimestamp    = LoadDeviceProc<PFN_vkCmdWriteTimestamp>(getDeviceProcAddr, device, "vkCmdWriteTimestamp");
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

    // Wrap the live device in a shared generation token (the ComPtr-style
    // keep-alive — see VulkanDeviceGeneration in vulkan_ink_layer.h). From
    // this point the generation owns device + instance teardown: Destroy()
    // releases the reference instead of calling vkDestroyDevice directly, so
    // backend-owned ink objects that outlive this render target keep a live
    // device to destroy their handles on.
    deviceGeneration = std::make_shared<VulkanDeviceGeneration>();
    deviceGeneration->ctx.instance            = instance;
    deviceGeneration->ctx.physicalDevice      = physicalDevice;
    deviceGeneration->ctx.device              = device;
    deviceGeneration->ctx.graphicsQueue       = queue;
    deviceGeneration->ctx.graphicsQueueFamily = queueFamilyIndex;
    deviceGeneration->ctx.getInstanceProcAddr = getInstanceProcAddr;
    deviceGeneration->ctx.getDeviceProcAddr   = getDeviceProcAddr;
    deviceGeneration->ctx.valid               = true;
    deviceGeneration->deviceWaitIdle          = deviceWaitIdle;
    deviceGeneration->destroyDevice           = destroyDevice;
    deviceGeneration->destroyInstance         = destroyInstance;

    // Publish the live device to the backend so backend-owned ink layers /
    // brush shaders build their GPU objects on the same device this window
    // composites with (the InkCanvas blit samples them directly). See
    // VulkanBackend::RegisterDeviceContext.
    if (backend) {
        ownerBackend = backend;
        backend->RegisterDeviceContext(deviceGeneration);
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

    // GPU timestamp query pool — best-effort, gated on every precondition.
    // Total slot count = kMaxTimingSlotsPerFrame * MAX_FRAMES_IN_FLIGHT so
    // each frame can address its own contiguous slot range without aliasing
    // the previous (still in-flight) frame's data.
    if (createQueryPool && destroyQueryPool && getQueryPoolResults &&
        cmdResetQueryPool && cmdWriteTimestamp &&
        timestampPeriodNs > 0.0f && timestampValidBitMask != 0)
    {
        VkQueryPoolCreateInfo qpInfo{};
        qpInfo.sType = VK_STRUCTURE_TYPE_QUERY_POOL_CREATE_INFO;
        qpInfo.queryType = VK_QUERY_TYPE_TIMESTAMP;
        qpInfo.queryCount = kMaxTimingSlotsPerFrame * MAX_FRAMES_IN_FLIGHT;
        if (createQueryPool(device, &qpInfo, nullptr, &timingQueryPool) == VK_SUCCESS &&
            timingQueryPool != VK_NULL_HANDLE)
        {
            timingSupported = true;
            for (auto& tf : timing) {
                tf.spanCategories.reserve(kMaxTimingSlotsPerFrame);
            }
        }
    }

    initialized = RecreateSwapchain(width, height, vsync);
    if (!initialized) {
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
    // Lost device/surface → the rebuild below cannot succeed
    // (vkCreateSwapchainKHR would just fail) and every intermediate call
    // pokes the dead driver. Fail immediately; Resize classifies this as
    // DEVICE_LOST.
    if (!DeviceUsable()) {
        return false;
    }

    // Must drain ALL in-flight GPU work before tearing down any resource the
    // descriptor sets / command buffers reference. Gating this on
    // swapchain != VK_NULL_HANDLE was too narrow — a rapid sequence of
    // window-state changes (minimize → restore, or two resize messages back
    // to back) can leave swapchain == VK_NULL_HANDLE here while the GPU is
    // still executing commands recorded against the previous frameDescriptor
    // pool / sampler / upload image. The next DestroyGraphicsResources then
    // frees those objects out from under the GPU and the *next* descriptor
    // update walks freed memory inside nvoglv64 (AV at offset 0x104). Wait
    // unconditionally as long as the device is alive — Resize is rare so the
    // cost is negligible.
    if (device != VK_NULL_HANDLE && deviceWaitIdle) {
        // A DEVICE_LOST here (GPU switch landed between frames) latches and
        // aborts the rebuild before any teardown churn; other failure codes
        // keep the historic ignore-and-continue behavior.
        if (NoteVk(deviceWaitIdle(device), "RecreateSwapchain.deviceWaitIdle") != VK_SUCCESS
            && deviceLost) {
            return false;
        }
    }

    DestroyGraphicsResources();

    // The upload image and its view were created for the old swapchain extent.
    // After DestroyGraphicsResources the descriptor pool/set are gone, so
    // UpdateFrameDescriptorSet (called inside EnsureGraphicsResources when the
    // new pool is allocated) would try to write the stale uploadImageView into
    // the new descriptor set — which crashes the NVIDIA driver. Destroy the
    // upload image in *every* per-frame slot (not just the current alias) so
    // EnsureUploadImage recreates it at the new size when each slot runs.
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
        s.uploadImage = VK_NULL_HANDLE;
        s.uploadImageMemory = VK_NULL_HANDLE;
        s.uploadImageView = VK_NULL_HANDLE;
        s.uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        s.uploadWidth = 0;
        s.uploadHeight = 0;

        // DestroyGraphicsResources just freed frameDescriptorPool (and with
        // it every descriptor set allocated from it), and zero'd the alias
        // frameDescriptorSet field. But each per-frame slot also caches the
        // descriptor set handle it allocated during its own BeginFrame —
        // those handles point into the now-freed pool. If we leave them set,
        // the next BeginFrame copies a stale handle into the alias,
        // EnsureGraphicsResources sees alias != VK_NULL_HANDLE and skips
        // allocation, and UpdateFrameDescriptorSet writes through the stale
        // handle. The NVIDIA driver's vkUpdateDescriptorSets then walks the
        // freed pool's bookkeeping and AVs (typically reading offset 0x104).
        // Clear them here so EnsureGraphicsResources reallocates from the
        // newly-created pool on the next frame.
        s.frameDescriptorSet = VK_NULL_HANDLE;
    }
    // Clear the current alias too.
    uploadImage = VK_NULL_HANDLE;
    uploadImageMemory = VK_NULL_HANDLE;
    uploadImageView = VK_NULL_HANDLE;
    uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    uploadWidth = 0;
    uploadHeight = 0;

    VkSurfaceCapabilitiesKHR capabilities {};
    if (NoteVk(getSurfaceCapabilities(physicalDevice, surface, &capabilities),
               "RecreateSwapchain.getSurfaceCapabilities") != VK_SUCCESS) {
        return false;
    }

    uint32_t formatCount = 0;
    if (NoteVk(getSurfaceFormats(physicalDevice, surface, &formatCount, nullptr),
               "RecreateSwapchain.getSurfaceFormats") != VK_SUCCESS || formatCount == 0) {
        return false;
    }

    std::vector<VkSurfaceFormatKHR> formats(formatCount);
    if (NoteVk(getSurfaceFormats(physicalDevice, surface, &formatCount, formats.data()),
               "RecreateSwapchain.getSurfaceFormats2") != VK_SUCCESS) {
        return false;
    }

    VkSurfaceFormatKHR selectedFormat = formats.front();
    // Prefer UNORM format so CPU canvas and GPU replay pass sRGB values directly.
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
    uint32_t presentModeCount = 0;
    if (NoteVk(getSurfacePresentModes(physicalDevice, surface, &presentModeCount, nullptr),
               "RecreateSwapchain.getSurfacePresentModes") != VK_SUCCESS || presentModeCount == 0) {
        return false;
    }

    std::vector<VkPresentModeKHR> presentModes(presentModeCount);
    if (NoteVk(getSurfacePresentModes(physicalDevice, surface, &presentModeCount, presentModes.data()),
               "RecreateSwapchain.getSurfacePresentModes2") != VK_SUCCESS) {
        return false;
    }

    // ── Present mode: do NOT hard-lock the frame rate to the display refresh ──
    // FIFO (the only guaranteed-present mode) makes acquire/present block the
    // calling thread until vblank, which both caps the frame rate AND stalls the
    // UI thread on every frame — exactly the "slow composition → 2 fps, whole
    // window hitches" failure the D3D12 backend escapes by presenting with
    // SyncInterval 0 under external present pacing. Vulkan has no frame-latency
    // waitable, so the managed credit scheduler never engages here
    // (GetFrameLatencyWaitable() == 0 → Window.StartExternalPresentPacingIfSupported
    // bails at "skip: no waitable"); the Vulkan-appropriate equivalent of "don't
    // block the render loop on vblank" is simply to avoid FIFO.
    //
    // The managed CompositionTarget render loop intentionally re-arms at a 1 ms
    // interval ("throttle to achievable rate, like a game engine render loop")
    // and leans on the present mode for pacing, so a non-blocking present mode
    // lets it run uncapped — matching the D3D12 path's Present(0) submit. The
    // per-frame vkWaitForFences (MAX_FRAMES_IN_FLIGHT) still backpressures the CPU
    // to the GPU's real throughput, so this is uncapped-to-achievable, not a busy
    // spin.
    //
    //   vsync == true  (default): MAILBOX → FIFO. MAILBOX is unlocked AND
    //                  tear-free — the compositor still flips at vblank and the
    //                  latest queued image wins — so it never tears for vsync-on
    //                  users (the analogue of D3D12's Present(0) WITHOUT
    //                  ALLOW_TEARING). FIFO only as the last resort when the
    //                  surface lacks MAILBOX (e.g. some Android surfaces).
    //   vsync == false (JALIUM_DISABLE_VSYNC benchmark/animation opt-out):
    //                  IMMEDIATE → MAILBOX → FIFO. Lowest latency, tearing
    //                  accepted (the analogue of D3D12's Present(0) +
    //                  DXGI_PRESENT_ALLOW_TEARING).
    auto surfaceHasPresentMode = [&](VkPresentModeKHR mode) {
        return std::find(presentModes.begin(), presentModes.end(), mode) != presentModes.end();
    };
    VkPresentModeKHR selectedPresentMode = VK_PRESENT_MODE_FIFO_KHR;  // guaranteed-supported fallback
    if (vsync) {
        if (surfaceHasPresentMode(VK_PRESENT_MODE_MAILBOX_KHR)) {
            selectedPresentMode = VK_PRESENT_MODE_MAILBOX_KHR;
        }
    } else {
        if (surfaceHasPresentMode(VK_PRESENT_MODE_IMMEDIATE_KHR)) {
            selectedPresentMode = VK_PRESENT_MODE_IMMEDIATE_KHR;
        } else if (surfaceHasPresentMode(VK_PRESENT_MODE_MAILBOX_KHR)) {
            selectedPresentMode = VK_PRESENT_MODE_MAILBOX_KHR;
        }
    }

    // --- Swapchain extent + preTransform (Android device rotation) ------------------
    // Size the swapchain to the AUTHORITATIVE content dims (width/height == width_/
    // height_ == the CPU canvas / viewport / managed layout), NOT capabilities.
    // currentExtent. On Android currentExtent and currentTransform can lag a frame
    // behind during a rotation transition — verified on-device: on the RETURN rotation
    // the caps still reported the previous orientation (currentExtent=1080x1920,
    // currentTransform=ROTATE_90) while the RESIZE already carried 1920x1080, so
    // trusting currentExtent stretched 1920x1080 content into a 1080x1920 image.
    // Clamp to the surface's valid range: a driver that pins the extent reports
    // min==max==currentExtent, so the clamp collapses back to currentExtent and the
    // behavior is identical to before (e.g. Windows / desktop, where width/height also
    // equal the surface size).
    //
    // We always render upright (logical-orientation) content, so we ask the compositor
    // to apply any physical rotation by declaring preTransform = IDENTITY. Windows /
    // desktop surfaces always report currentTransform == IDENTITY and advertise it in
    // supportedTransforms, so chosenPreTransform stays IDENTITY there too — unchanged.
    const VkSurfaceTransformFlagBitsKHR curTransform = capabilities.currentTransform;
    const bool identitySupported =
        (capabilities.supportedTransforms & VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR) != 0;

    VkExtent2D newExtent {};
    if (width > 0 && height > 0) {
        newExtent.width  = ClampExtent(static_cast<uint32_t>(width),  capabilities.minImageExtent.width,  capabilities.maxImageExtent.width);
        newExtent.height = ClampExtent(static_cast<uint32_t>(height), capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
    } else if (capabilities.currentExtent.width != std::numeric_limits<uint32_t>::max()) {
        newExtent = capabilities.currentExtent;
    } else {
        newExtent.width  = ClampExtent(1u, capabilities.minImageExtent.width,  capabilities.maxImageExtent.width);
        newExtent.height = ClampExtent(1u, capabilities.minImageExtent.height, capabilities.maxImageExtent.height);
    }

    const VkSurfaceTransformFlagBitsKHR chosenPreTransform =
        identitySupported ? VK_SURFACE_TRANSFORM_IDENTITY_BIT_KHR : curTransform;

    uint32_t imageCount = capabilities.minImageCount + 1;
    // MAILBOX only stays non-blocking with at least 3 images (one on screen, one
    // queued in the mailbox, one being rendered); with 2 the acquire degenerates
    // into a FIFO-like vblank stall and the "don't lock the frame rate" intent is
    // lost. Request a third when MAILBOX was selected. FIFO / IMMEDIATE keep
    // minImageCount + 1 (typically 2-3). The maxImageCount clamp below still wins.
    if (selectedPresentMode == VK_PRESENT_MODE_MAILBOX_KHR && imageCount < 3) {
        imageCount = 3;
    }
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

    if ((capabilities.supportedUsageFlags & VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT) == 0) {
        return false;
    }
    // COLOR_ATTACHMENT is mandatory. The replay path also clears the swapchain
    // image via cmdClearColorImage (needs TRANSFER_DST) and the Backdrop /
    // LiquidGlass live-scene capture blits FROM it (needs TRANSFER_SRC). Declare
    // both transfer bits when the surface advertises them; sceneCaptureSupported
    // gates the capture so it degrades to the CPU-pixel source when TRANSFER_SRC
    // is unavailable.
    VkImageUsageFlags imageUsage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT;
    imageUsage |= (capabilities.supportedUsageFlags &
                   (VK_IMAGE_USAGE_TRANSFER_SRC_BIT | VK_IMAGE_USAGE_TRANSFER_DST_BIT));
    sceneCaptureSupported = (capabilities.supportedUsageFlags & VK_IMAGE_USAGE_TRANSFER_SRC_BIT) != 0;

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
    swapchainInfo.preTransform = chosenPreTransform;
    swapchainInfo.compositeAlpha = compositeAlpha;
    swapchainInfo.presentMode = selectedPresentMode;
    swapchainInfo.clipped = VK_TRUE;
    swapchainInfo.oldSwapchain = oldSwapchain;

    VkSwapchainKHR newSwapchain = VK_NULL_HANDLE;
    if (NoteVk(createSwapchain(device, &swapchainInfo, nullptr, &newSwapchain),
               "RecreateSwapchain.createSwapchain") != VK_SUCCESS || newSwapchain == VK_NULL_HANDLE) {
        return false;
    }

    uint32_t actualImageCount = 0;
    if (NoteVk(getSwapchainImages(device, newSwapchain, &actualImageCount, nullptr),
               "RecreateSwapchain.getSwapchainImages") != VK_SUCCESS || actualImageCount == 0) {
        destroySwapchain(device, newSwapchain, nullptr);
        return false;
    }

    std::vector<VkImage> newImages(actualImageCount);
    if (NoteVk(getSwapchainImages(device, newSwapchain, &actualImageCount, newImages.data()),
               "RecreateSwapchain.getSwapchainImages2") != VK_SUCCESS) {
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

    // Cache the present-info fields so VulkanRenderTarget::GetPresentInfo
    // can answer without re-querying surface capabilities. imageCount comes
    // from the actual swap-chain image count we just resolved, not the
    // requested one — the driver may round up to the supported minimum.
    currentPresentMode = selectedPresentMode;
    swapImageCount = static_cast<uint32_t>(images.size());

    // Reset present-pacing state — a submit timestamp from the destroyed
    // swap chain is meaningless for the new one and would otherwise show as
    // a giant lastFramePresentToReadyNs spike on the first frame after resize.
    lastSubmitMonotonicNs = 0;
    lastFramePresentToReadyNs = 0;

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

    // GPU may still be sampling the current uploadImage / uploadImageView via
    // an in-flight frameDescriptorSet — destroying them out from under it
    // (and writing the descriptor to point at the replacement view immediately
    // after) trips an access violation deep inside the NVIDIA Vulkan driver
    // (vkUpdateDescriptorSets dereferences the descriptor's previous image
    // view as part of its bookkeeping). We can't tear down the upload image
    // mid-flight; wait until the device is idle, then it's safe.
    //
    // This path is rare — only hit when a draw command needs an upload image
    // larger than the cached one. It does NOT run every frame, so the cost
    // of vkDeviceWaitIdle here doesn't undo the MAX_FRAMES_IN_FLIGHT win.
    if (deviceWaitIdle && uploadImage != VK_NULL_HANDLE) {
        NoteVk(deviceWaitIdle(device), "EnsureUploadImage.deviceWaitIdle");
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

bool VulkanRenderTarget::Impl::CaptureLiveSceneToUpload(uint32_t imageIndex,
                                                        const VkImageSubresourceRange& range,
                                                        VkExtent2D srcExtent,
                                                        uint32_t dstWidth, uint32_t dstHeight,
                                                        int32_t srcX, int32_t srcY,
                                                        int32_t srcW, int32_t srcH)
{
    if (!sceneCaptureSupported || cmdBlitImage == nullptr || cmdPipelineBarrier == nullptr ||
        uploadImage == VK_NULL_HANDLE || imageIndex >= images.size() ||
        images[imageIndex] == VK_NULL_HANDLE ||
        srcExtent.width == 0 || srcExtent.height == 0 || dstWidth == 0 || dstHeight == 0) {
        return false;
    }
    // Clamp the destination region to the allocated upload image.
    dstWidth = std::min(dstWidth, uploadWidth);
    dstHeight = std::min(dstHeight, uploadHeight);
    if (dstWidth == 0 || dstHeight == 0) {
        return false;
    }

    // Colour image: COLOR_ATTACHMENT_OPTIMAL (load-pass finalLayout) -> TRANSFER_SRC.
    VkImageMemoryBarrier colorToSrc {};
    colorToSrc.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    colorToSrc.oldLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    colorToSrc.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    colorToSrc.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    colorToSrc.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    colorToSrc.image = images[imageIndex];
    colorToSrc.subresourceRange = range;
    colorToSrc.srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    colorToSrc.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT,
                       VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &colorToSrc);

    // Upload image: current layout -> TRANSFER_DST.
    VkImageMemoryBarrier uploadToDst {};
    uploadToDst.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    uploadToDst.oldLayout = uploadImageLayout;
    uploadToDst.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    uploadToDst.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToDst.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToDst.image = uploadImage;
    uploadToDst.subresourceRange = range;
    uploadToDst.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
        ? VK_ACCESS_SHADER_READ_BIT : 0;
    uploadToDst.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    const VkPipelineStageFlags uploadSrcStage = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
        ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
        : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
    cmdPipelineBarrier(commandBuffer, uploadSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT,
                       0, 0, nullptr, 0, nullptr, 1, &uploadToDst);

    // Blit the rendered colour image into the [0,dst] region of uploadImage —
    // the same region the CPU-pixel cmdCopyBufferToImage targets, so the effect
    // shader's UV math (1/pixelWidth, 1/pixelHeight) is unchanged. The source is
    // the whole frame by default (backdrop/glass), or the [srcX,srcY,srcW,srcH]
    // sub-rect when requested (element BlurEffect captures only its own region).
    const int32_t fullW = static_cast<int32_t>(srcExtent.width);
    const int32_t fullH = static_cast<int32_t>(srcExtent.height);
    int32_t sx0 = (srcW > 0 && srcH > 0) ? srcX : 0;
    int32_t sy0 = (srcW > 0 && srcH > 0) ? srcY : 0;
    int32_t sx1 = (srcW > 0 && srcH > 0) ? srcX + srcW : fullW;
    int32_t sy1 = (srcW > 0 && srcH > 0) ? srcY + srcH : fullH;
    sx0 = std::clamp(sx0, 0, fullW); sy0 = std::clamp(sy0, 0, fullH);
    sx1 = std::clamp(sx1, 0, fullW); sy1 = std::clamp(sy1, 0, fullH);
    if (sx1 <= sx0 || sy1 <= sy0) { sx0 = 0; sy0 = 0; sx1 = fullW; sy1 = fullH; }
    VkImageBlit blit {};
    blit.srcSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    blit.srcSubresource.layerCount = 1;
    blit.srcOffsets[0] = { sx0, sy0, 0 };
    blit.srcOffsets[1] = { sx1, sy1, 1 };
    blit.dstSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    blit.dstSubresource.layerCount = 1;
    blit.dstOffsets[0] = { 0, 0, 0 };
    blit.dstOffsets[1] = { static_cast<int32_t>(dstWidth), static_cast<int32_t>(dstHeight), 1 };
    cmdBlitImage(commandBuffer, images[imageIndex], VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                 uploadImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &blit, VK_FILTER_LINEAR);

    // Upload image -> SHADER_READ for sampling by the effect pipeline.
    VkImageMemoryBarrier uploadToRead {};
    uploadToRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    uploadToRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
    uploadToRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    uploadToRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    uploadToRead.image = uploadImage;
    uploadToRead.subresourceRange = range;
    uploadToRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
    uploadToRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT,
                       0, 0, nullptr, 0, nullptr, 1, &uploadToRead);
    uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    // Colour image back to COLOR_ATTACHMENT_OPTIMAL for the next beginLoadRenderPass.
    VkImageMemoryBarrier colorBack {};
    colorBack.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    colorBack.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    colorBack.newLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
    colorBack.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    colorBack.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
    colorBack.image = images[imageIndex];
    colorBack.subresourceRange = range;
    colorBack.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
    colorBack.dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
    cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT,
                       VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT, 0, 0, nullptr, 0, nullptr, 1, &colorBack);
    return true;
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
        // Bitmap-quad path uses a single shared sampler bound through the
        // frame descriptor set. Default to "high quality": linear min/mag,
        // trilinear mipmap, and 16x anisotropic when the device exposes it.
        // The Vulkan upload path currently uploads only mip 0, so the
        // mipmapMode setting is a no-op for now — but this leaves the door
        // open for a future mip-chain blit pass without re-plumbing the
        // sampler. Anisotropic still helps minified UI sprites even
        // without a mip chain because the driver chooses a smarter
        // taps schedule than plain bilinear.
        VkSamplerCreateInfo samplerInfo {};
        samplerInfo.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        samplerInfo.magFilter = VK_FILTER_LINEAR;
        samplerInfo.minFilter = VK_FILTER_LINEAR;
        samplerInfo.mipmapMode = VK_SAMPLER_MIPMAP_MODE_LINEAR;
        samplerInfo.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        samplerInfo.minLod = 0.0f;
        samplerInfo.maxLod = VK_LOD_CLAMP_NONE;
        samplerInfo.anisotropyEnable = anisotropySupported ? VK_TRUE : VK_FALSE;
        samplerInfo.maxAnisotropy = anisotropySupported ? deviceMaxAnisotropy : 1.0f;
        samplerInfo.borderColor = VK_BORDER_COLOR_FLOAT_TRANSPARENT_BLACK;
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

    // ── Dedicated ink-layer composite pipeline ────────────────────────────────
    // Identical to bitmapPipeline (same bitmapPipelineLayout, same vertex shader,
    // same dynamic viewport/scissor) EXCEPT: (1) PREMULTIPLIED-SrcOver blend —
    // srcColorBlendFactor = ONE instead of SRC_ALPHA — and (2) the premultiplied
    // FS (kInkCompositeFragSpv) which scales every channel by opacity. The
    // resident ink image is premultiplied (BrushShaderPreamble emits premul; the
    // ink brush pipeline blends with srcColorBlendFactor = ONE; ReadbackBgra/Clear
    // keep premul), so compositing it through the STRAIGHT bitmapPipeline
    // double-multiplied alpha and darkened AA edges + translucent strokes. The
    // shared bitmapPipeline is left untouched so its STRAIGHT producers (polygon /
    // stroke rasterizers, WIC/stb images, the foreign-ink readback upload) are
    // unaffected. bitmapPipelineLayout is guaranteed non-null here (its creation
    // above returns false on failure).
    if (inkCompositePipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kBitmapQuadVertexShaderSpvSize;  // reuse the shared bitmap quad VS
        vertexShaderInfo.pCode = kBitmapQuadVertexShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kInkCompositeFragSpvSize;
        fragmentShaderInfo.pCode = kInkCompositeFragSpv;
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

        // PREMULTIPLIED SrcOver: out.rgb = src.rgb*1 + dst.rgb*(1 - src.a);
        // out.a = src.a*1 + dst.a*(1 - src.a). The FS already scaled the
        // premultiplied texel by the layer opacity (all channels), so this is
        // correct for any opacity in [0,1]. (The straight bitmapPipeline uses
        // SRC_ALPHA here — that is exactly the blend we must NOT reuse for premul.)
        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
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
        pipelineInfo.layout = bitmapPipelineLayout;  // shared: same push constants + frameDescriptorSetLayout
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &inkCompositePipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || inkCompositePipeline == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createGraphicsPipelines(inkComposite) failed (%d)", static_cast<int>(pipelineResult));
            return false;
        }
    }

#ifdef _WIN32
    // ── Dedicated text-glyph pipeline (B4) ────────────────────────────────────
    // A bespoke descriptor set layout + pipeline so each glyph quad samples its
    // OWN atlas sub-rect from a per-frame glyph-instance StructuredBuffer (the
    // generic bitmap pipeline can only sample the whole image from (0,0)).
    // Bindings: 0 = atlas SAMPLED_IMAGE (FS), 1 = SAMPLER (FS),
    //           2 = glyph-instance STORAGE_BUFFER (VS).
    if (textDescriptorSetLayout == VK_NULL_HANDLE) {
        VkDescriptorSetLayoutBinding bindings[3] {};
        bindings[0].binding = 0;
        bindings[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        bindings[0].descriptorCount = 1;
        bindings[0].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        bindings[1].binding = 1;
        bindings[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
        bindings[1].descriptorCount = 1;
        bindings[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        bindings[2].binding = 2;
        bindings[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        bindings[2].descriptorCount = 1;
        bindings[2].stageFlags = VK_SHADER_STAGE_VERTEX_BIT;

        VkDescriptorSetLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        layoutInfo.bindingCount = 3;
        layoutInfo.pBindings = bindings;
        if (createDescriptorSetLayout(device, &layoutInfo, nullptr, &textDescriptorSetLayout) != VK_SUCCESS || textDescriptorSetLayout == VK_NULL_HANDLE) {
            VK_LOG("[Vulkan] EnsureGraphicsResources: createDescriptorSetLayout(text) failed");
            return false;
        }
    }

    if (textDescriptorPool == VK_NULL_HANDLE) {
        VkDescriptorPoolSize poolSizes[3] {};
        poolSizes[0].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        poolSizes[0].descriptorCount = MAX_FRAMES_IN_FLIGHT;
        poolSizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLER;
        poolSizes[1].descriptorCount = MAX_FRAMES_IN_FLIGHT;
        poolSizes[2].type = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
        poolSizes[2].descriptorCount = MAX_FRAMES_IN_FLIGHT;

        VkDescriptorPoolCreateInfo poolInfo {};
        poolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        poolInfo.maxSets = MAX_FRAMES_IN_FLIGHT;
        poolInfo.poolSizeCount = 3;
        poolInfo.pPoolSizes = poolSizes;
        if (createDescriptorPool(device, &poolInfo, nullptr, &textDescriptorPool) != VK_SUCCESS || textDescriptorPool == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (textDescriptorSet == VK_NULL_HANDLE) {
        VkDescriptorSetAllocateInfo allocateInfo {};
        allocateInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        allocateInfo.descriptorPool = textDescriptorPool;
        allocateInfo.descriptorSetCount = 1;
        allocateInfo.pSetLayouts = &textDescriptorSetLayout;
        if (allocateDescriptorSets(device, &allocateInfo, &textDescriptorSet) != VK_SUCCESS || textDescriptorSet == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (textPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(TextPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.setLayoutCount = 1;
        layoutInfo.pSetLayouts = &textDescriptorSetLayout;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &textPipelineLayout) != VK_SUCCESS || textPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (textPipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kTextGlyphVertSpvSize;
        vertexShaderInfo.pCode = kTextGlyphVertSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kTextGlyphFragSpvSize;
        fragmentShaderInfo.pCode = kTextGlyphFragSpv;
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

        // Empty vertex input: the VS synthesizes the quad from SV_VertexID and
        // reads the glyph instance from the SSBO via SV_InstanceID.
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

        // CRITICAL: the FS emits PREMULTIPLIED colour*coverage (GenerateGlyphs'
        // emitRun premultiplies), so the source colour is already multiplied by
        // alpha — srcColorBlendFactor MUST be ONE (NOT SRC_ALPHA, which the
        // straight-alpha bitmap pipeline uses). Standard premultiplied SrcOver:
        // out = src*1 + dst*(1-src.a).
        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
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
        pipelineInfo.layout = textPipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &textPipeline);

        // ClearType dual-source variant (opt-in, requires dualSrcBlend). Reuses the
        // same VS/FS module + layout; the gClearType=1 specialization selects the
        // ClearType branch and the blend consumes the FS SV_Target1 per-channel
        // coverage (src=ONE / dst=ONE_MINUS_SRC1_COLOR). Best-effort: on failure
        // leave it null and fall back to grayscale at draw time.
        if (pipelineResult == VK_SUCCESS && textPipeline != VK_NULL_HANDLE && dualSrcBlendSupported) {
            const uint32_t clearTypeSpecValue = 1u;
            VkSpecializationMapEntry ctSpecEntry {};
            ctSpecEntry.constantID = 0;
            ctSpecEntry.offset = 0;
            ctSpecEntry.size = sizeof(uint32_t);
            VkSpecializationInfo ctSpecInfo {};
            ctSpecInfo.mapEntryCount = 1;
            ctSpecInfo.pMapEntries = &ctSpecEntry;
            ctSpecInfo.dataSize = sizeof(uint32_t);
            ctSpecInfo.pData = &clearTypeSpecValue;
            VkPipelineShaderStageCreateInfo ctStages[2] = { shaderStages[0], shaderStages[1] };
            ctStages[1].pSpecializationInfo = &ctSpecInfo;

            VkPipelineColorBlendAttachmentState ctBlendAttachment = colorBlendAttachment;
            ctBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
            ctBlendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC1_COLOR;
            ctBlendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
            ctBlendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC1_ALPHA;
            VkPipelineColorBlendStateCreateInfo ctBlendInfo = colorBlendInfo;
            ctBlendInfo.pAttachments = &ctBlendAttachment;

            VkGraphicsPipelineCreateInfo ctPipelineInfo = pipelineInfo;
            ctPipelineInfo.pStages = ctStages;
            ctPipelineInfo.pColorBlendState = &ctBlendInfo;
            if (createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &ctPipelineInfo, nullptr, &textClearTypePipeline) != VK_SUCCESS) {
                textClearTypePipeline = VK_NULL_HANDLE;
            }
        }

        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || textPipeline == VK_NULL_HANDLE) {
            return false;
        }
    }
#endif // _WIN32

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

    // ── Per-vertex-coloured triangle pipeline (vc) ─────────────────────
    // Used by the DrawLine 3-strip AA path (and any future per-vertex-colour
    // primitive). Push-constant layout = TriangleFillVcPushConstants. Vertex
    // input format: (vec2 pos at location 0, vec4 color at location 1) with
    // 24-byte stride — matches both the shader and the interleaved layout
    // recorded in GpuVcTrianglesCommand.vertices.
    if (vcTrianglePipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT;
        pushConstantRange.size = sizeof(TriangleFillVcPushConstants);

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &vcTrianglePipelineLayout) != VK_SUCCESS || vcTrianglePipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    if (vcTrianglePipeline == VK_NULL_HANDLE) {
        VkShaderModule vertexShader = VK_NULL_HANDLE;
        VkShaderModule fragmentShader = VK_NULL_HANDLE;

        VkShaderModuleCreateInfo vertexShaderInfo {};
        vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vertexShaderInfo.codeSize = kTriangleFillVcVertShaderSpvSize;
        vertexShaderInfo.pCode = kTriangleFillVcVertShaderSpv;
        if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS || vertexShader == VK_NULL_HANDLE) {
            return false;
        }

        VkShaderModuleCreateInfo fragmentShaderInfo {};
        fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fragmentShaderInfo.codeSize = kTriangleFillVcFragShaderSpvSize;
        fragmentShaderInfo.pCode = kTriangleFillVcFragShaderSpv;
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
        bindingDescription.stride = sizeof(float) * 6;  // pos(2) + color(4)
        bindingDescription.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

        VkVertexInputAttributeDescription attributeDescriptions[2] {};
        attributeDescriptions[0].location = 0;
        attributeDescriptions[0].binding = 0;
        attributeDescriptions[0].format = VK_FORMAT_R32G32_SFLOAT;
        attributeDescriptions[0].offset = 0;
        attributeDescriptions[1].location = 1;
        attributeDescriptions[1].binding = 0;
        attributeDescriptions[1].format = VK_FORMAT_R32G32B32A32_SFLOAT;
        attributeDescriptions[1].offset = sizeof(float) * 2;

        VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
        vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
        vertexInputInfo.vertexBindingDescriptionCount = 1;
        vertexInputInfo.pVertexBindingDescriptions = &bindingDescription;
        vertexInputInfo.vertexAttributeDescriptionCount = 2;
        vertexInputInfo.pVertexAttributeDescriptions = attributeDescriptions;

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

        // Premultiplied-alpha blending — the AA strips emit (R*a, G*a, B*a, a)
        // at full-coverage vertices and (0,0,0,0) at the feather edge, so
        // straight SRC_ALPHA blending interpolates the right colour while
        // letting the destination show through the feather.
        VkPipelineColorBlendAttachmentState colorBlendAttachment {};
        colorBlendAttachment.blendEnable = VK_TRUE;
        colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
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
            VK_DYNAMIC_STATE_SCISSOR,
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
        pipelineInfo.layout = vcTrianglePipelineLayout;
        pipelineInfo.renderPass = frameRenderPass;
        pipelineInfo.subpass = 0;
        const VkResult pipelineResult = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &vcTrianglePipeline);
        destroyShaderModule(device, fragmentShader, nullptr);
        destroyShaderModule(device, vertexShader, nullptr);
        if (pipelineResult != VK_SUCCESS || vcTrianglePipeline == VK_NULL_HANDLE) {
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
    // Spec says imageView is ignored for VK_DESCRIPTOR_TYPE_SAMPLER, but the
    // NVIDIA driver dereferences it anyway (null + offset 0x3104 → AV).
    // Supply the upload view so the driver sees a valid handle.
    samplerInfo.imageView = uploadImageView;
    samplerInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

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

#ifdef _WIN32
bool VulkanRenderTarget::Impl::UpdateTextDescriptorSet(VkDeviceSize glyphOffset, VkDeviceSize glyphRange)
{
    if (textDescriptorSet == VK_NULL_HANDLE ||
        textAtlasImageView == VK_NULL_HANDLE ||
        frameSampler == VK_NULL_HANDLE ||
        stagingBuffer == VK_NULL_HANDLE) {
        // Not ready yet (atlas image / staging buffer absent): treat as "nothing
        // to write" rather than a hard failure, mirroring UpdateFrameDescriptorSet.
        return textDescriptorSet == VK_NULL_HANDLE;
    }

    VkDescriptorImageInfo atlasImageInfo {};
    atlasImageInfo.imageView = textAtlasImageView;
    atlasImageInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    VkDescriptorImageInfo samplerImageInfo {};
    samplerImageInfo.sampler = frameSampler;
    // Spec says imageView is ignored for VK_DESCRIPTOR_TYPE_SAMPLER, but the
    // NVIDIA driver dereferences it anyway (null + offset → AV); hand it the
    // atlas view so the driver sees a valid handle (same workaround as
    // UpdateFrameDescriptorSet).
    samplerImageInfo.imageView = textAtlasImageView;
    samplerImageInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

    VkDescriptorBufferInfo glyphBufferInfo {};
    glyphBufferInfo.buffer = stagingBuffer;
    glyphBufferInfo.offset = glyphOffset;
    glyphBufferInfo.range = glyphRange;

    VkWriteDescriptorSet writes[3] {};
    writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[0].dstSet = textDescriptorSet;
    writes[0].dstBinding = 0;
    writes[0].descriptorCount = 1;
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    writes[0].pImageInfo = &atlasImageInfo;
    writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[1].dstSet = textDescriptorSet;
    writes[1].dstBinding = 1;
    writes[1].descriptorCount = 1;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
    writes[1].pImageInfo = &samplerImageInfo;
    writes[2].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[2].dstSet = textDescriptorSet;
    writes[2].dstBinding = 2;
    writes[2].descriptorCount = 1;
    writes[2].descriptorType = VK_DESCRIPTOR_TYPE_STORAGE_BUFFER;
    writes[2].pBufferInfo = &glyphBufferInfo;
    updateDescriptorSets(device, static_cast<uint32_t>(std::size(writes)), writes, 0, nullptr);
    return true;
}
#endif // _WIN32

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
    // NVIDIA driver workaround: dereferences imageView even for SAMPLER type.
    imageInfos[2].imageView = transitionImageViews[0];
    imageInfos[2].imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

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
    // TRANSFER_SRC is the legacy use (bitmap / backdrop / etc. upload via
    // vkCmdCopyBufferToImage). VERTEX_BUFFER lets the DrawLine 3-strip AA
    // path bind a slice of this same staging buffer directly as the vertex
    // buffer — host-visible memory is slower for vertex fetch than
    // DEVICE_LOCAL, but the data volume is tiny (~108 bytes per line) and
    // we save the round trip through an intermediate device-local buffer.
    // STORAGE_BUFFER lets the dedicated text-glyph pipeline (B4) bind a slice of
    // this same staging buffer as the per-frame glyph-instance StructuredBuffer
    // (textDescriptorSet binding 2), so glyph instances ride the same upload as
    // every other per-frame staging payload without a second VkBuffer.
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT | VK_BUFFER_USAGE_VERTEX_BUFFER_BIT |
                       VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
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

#ifdef _WIN32
VulkanGlyphAtlas* VulkanRenderTarget::Impl::EnsureGlyphAtlas()
{
    if (glyphAtlas_) {
        return glyphAtlas_.get();
    }
    // The atlas rasterizes through the process-shared DirectWrite factory (same
    // one VulkanTextFormat uses). If it's unavailable there is no text path.
    IDWriteFactory* factory = GetSharedDWriteFactory();
    if (factory == nullptr) {
        return nullptr;
    }
    glyphAtlas_ = std::make_unique<VulkanGlyphAtlas>(factory);
    if (!glyphAtlas_->Initialize()) {
        glyphAtlas_.reset();
        return nullptr;
    }
    return glyphAtlas_.get();
}

bool VulkanRenderTarget::Impl::EnsureTextAtlasImage(uint32_t width, uint32_t height)
{
    if (width == 0 || height == 0) {
        return false;
    }

    if (textAtlasImage != VK_NULL_HANDLE && textAtlasWidth == width && textAtlasHeight == height) {
        return true;
    }

    // The GPU may still be sampling the current textAtlasImage via an in-flight
    // textDescriptorSet — the same NVIDIA-driver hazard EnsureUploadImage guards
    // against. This grow path is rare (only when the atlas spills and is
    // recreated larger), so the vkDeviceWaitIdle cost is irrelevant.
    if (deviceWaitIdle && textAtlasImage != VK_NULL_HANDLE) {
        NoteVk(deviceWaitIdle(device), "EnsureTextAtlasImage.deviceWaitIdle");
    }

    DestroyTextAtlasImage();

    VkImageCreateInfo imageInfo {};
    imageInfo.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    imageInfo.imageType = VK_IMAGE_TYPE_2D;
    // The CPU atlas bitmap is R8G8B8A8 (R,G,B = sub-pixel coverage, A = max), so
    // the GPU image must match — NOT the swap-chain-derived upload format.
    imageInfo.format = VK_FORMAT_R8G8B8A8_UNORM;
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
    if (createImage(device, &imageInfo, nullptr, &textAtlasImage) != VK_SUCCESS || textAtlasImage == VK_NULL_HANDLE) {
        return false;
    }

    VkMemoryRequirements memoryRequirements {};
    getImageMemoryRequirements(device, textAtlasImage, &memoryRequirements);

    const uint32_t memoryTypeIndex = FindMemoryType(memoryRequirements.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
    if (memoryTypeIndex == VK_QUEUE_FAMILY_IGNORED) {
        return false;
    }

    VkMemoryAllocateInfo allocateInfo {};
    allocateInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocateInfo.allocationSize = memoryRequirements.size;
    allocateInfo.memoryTypeIndex = memoryTypeIndex;
    if (allocateMemory(device, &allocateInfo, nullptr, &textAtlasImageMemory) != VK_SUCCESS || textAtlasImageMemory == VK_NULL_HANDLE) {
        return false;
    }

    if (bindImageMemory(device, textAtlasImage, textAtlasImageMemory, 0) != VK_SUCCESS) {
        return false;
    }

    VkImageViewCreateInfo viewInfo {};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = textAtlasImage;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = imageInfo.format;
    viewInfo.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
    viewInfo.subresourceRange.levelCount = 1;
    viewInfo.subresourceRange.layerCount = 1;
    if (createImageView(device, &viewInfo, nullptr, &textAtlasImageView) != VK_SUCCESS || textAtlasImageView == VK_NULL_HANDLE) {
        return false;
    }

    textAtlasWidth = width;
    textAtlasHeight = height;
    textAtlasImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    // Unlike EnsureUploadImage we do NOT touch a descriptor set here: the text
    // descriptor set is (re)written by UpdateTextDescriptorSet once the per-frame
    // glyph SSBO offset/range is known (B4b).
    return true;
}

void VulkanRenderTarget::Impl::DestroyTextAtlasImage()
{
    if (device == VK_NULL_HANDLE) {
        textAtlasImage = VK_NULL_HANDLE;
        textAtlasImageMemory = VK_NULL_HANDLE;
        textAtlasImageView = VK_NULL_HANDLE;
        textAtlasImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        textAtlasWidth = 0;
        textAtlasHeight = 0;
        return;
    }

    if (destroyImageView && textAtlasImageView != VK_NULL_HANDLE) {
        destroyImageView(device, textAtlasImageView, nullptr);
    }
    if (destroyImage && textAtlasImage != VK_NULL_HANDLE) {
        destroyImage(device, textAtlasImage, nullptr);
    }
    if (freeMemory && textAtlasImageMemory != VK_NULL_HANDLE) {
        freeMemory(device, textAtlasImageMemory, nullptr);
    }

    textAtlasImage = VK_NULL_HANDLE;
    textAtlasImageMemory = VK_NULL_HANDLE;
    textAtlasImageView = VK_NULL_HANDLE;
    textAtlasImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    textAtlasWidth = 0;
    textAtlasHeight = 0;
}

bool VulkanRenderTarget::Impl::EnsureTextAtlasStaging(VkDeviceSize size)
{
    if (size == 0 || !createBuffer) {
        return false;
    }
    if (textAtlasStagingBuffer != VK_NULL_HANDLE && textAtlasStagingCapacity >= size) {
        return true;
    }

    // Grow: release the old buffer/mapping before reallocating. The dirty-band
    // copy that consumes this buffer is recorded into the same command buffer
    // every frame, and frame-start waitForFences already drained the previous
    // submission, so the old buffer is safe to destroy here.
    if (textAtlasStagingMapped && unmapMemory && device != VK_NULL_HANDLE && textAtlasStagingMemory != VK_NULL_HANDLE) {
        unmapMemory(device, textAtlasStagingMemory);
        textAtlasStagingMapped = nullptr;
    }
    if (destroyBuffer && device != VK_NULL_HANDLE && textAtlasStagingBuffer != VK_NULL_HANDLE) {
        destroyBuffer(device, textAtlasStagingBuffer, nullptr);
        textAtlasStagingBuffer = VK_NULL_HANDLE;
    }
    if (freeMemory && device != VK_NULL_HANDLE && textAtlasStagingMemory != VK_NULL_HANDLE) {
        freeMemory(device, textAtlasStagingMemory, nullptr);
        textAtlasStagingMemory = VK_NULL_HANDLE;
    }
    textAtlasStagingCapacity = 0;

    VkBufferCreateInfo bufferInfo {};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = size;
    bufferInfo.usage = VK_BUFFER_USAGE_TRANSFER_SRC_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (createBuffer(device, &bufferInfo, nullptr, &textAtlasStagingBuffer) != VK_SUCCESS || textAtlasStagingBuffer == VK_NULL_HANDLE) {
        return false;
    }

    VkMemoryRequirements memoryRequirements {};
    getBufferMemoryRequirements(device, textAtlasStagingBuffer, &memoryRequirements);

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
    if (allocateMemory(device, &allocateInfo, nullptr, &textAtlasStagingMemory) != VK_SUCCESS || textAtlasStagingMemory == VK_NULL_HANDLE) {
        return false;
    }
    if (bindBufferMemory(device, textAtlasStagingBuffer, textAtlasStagingMemory, 0) != VK_SUCCESS) {
        return false;
    }
    if (mapMemory(device, textAtlasStagingMemory, 0, VK_WHOLE_SIZE, 0, &textAtlasStagingMapped) != VK_SUCCESS || !textAtlasStagingMapped) {
        return false;
    }

    textAtlasStagingCapacity = size;
    return true;
}

void VulkanRenderTarget::Impl::DestroyTextAtlasStaging()
{
    if (device == VK_NULL_HANDLE) {
        textAtlasStagingBuffer = VK_NULL_HANDLE;
        textAtlasStagingMemory = VK_NULL_HANDLE;
        textAtlasStagingMapped = nullptr;
        textAtlasStagingCapacity = 0;
        return;
    }
    if (textAtlasStagingMapped && unmapMemory && textAtlasStagingMemory != VK_NULL_HANDLE) {
        unmapMemory(device, textAtlasStagingMemory);
    }
    if (destroyBuffer && textAtlasStagingBuffer != VK_NULL_HANDLE) {
        destroyBuffer(device, textAtlasStagingBuffer, nullptr);
    }
    if (freeMemory && textAtlasStagingMemory != VK_NULL_HANDLE) {
        freeMemory(device, textAtlasStagingMemory, nullptr);
    }
    textAtlasStagingBuffer = VK_NULL_HANDLE;
    textAtlasStagingMemory = VK_NULL_HANDLE;
    textAtlasStagingMapped = nullptr;
    textAtlasStagingCapacity = 0;
}
#endif // _WIN32

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
    // Stencil-then-cover scratch is swap-chain-extent-sized + bound to its own
    // render pass, so it must be rebuilt on resize/teardown like the rest.
    // (Self-guards on device == VK_NULL_HANDLE: just nulls handles when the
    // device is already gone.)
    DestroyStencilCoverResources();
    DestroyEffectOffscreenResources();  // env-gated offscreen effect RT (no-op when off)

    if (device == VK_NULL_HANDLE) {
        imageViews.clear();
        framebuffers.clear();
        solidRectPipeline = VK_NULL_HANDLE;
        clearRectPipeline = VK_NULL_HANDLE;
        solidRectPipelineLayout = VK_NULL_HANDLE;
        bitmapPipeline = VK_NULL_HANDLE;
        inkCompositePipeline = VK_NULL_HANDLE;
        bitmapPipelineLayout = VK_NULL_HANDLE;
#ifdef _WIN32
        textPipeline = VK_NULL_HANDLE;
        textClearTypePipeline = VK_NULL_HANDLE;
        textPipelineLayout = VK_NULL_HANDLE;
        textDescriptorSet = VK_NULL_HANDLE;
        textDescriptorPool = VK_NULL_HANDLE;
        textDescriptorSetLayout = VK_NULL_HANDLE;
#endif
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
        vcTrianglePipeline = VK_NULL_HANDLE;
        vcTrianglePipelineLayout = VK_NULL_HANDLE;
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
    if (destroyPipeline && inkCompositePipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, inkCompositePipeline, nullptr);
    }
#ifdef _WIN32
    if (destroyPipeline && textPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, textPipeline, nullptr);
    }
    if (destroyPipeline && textClearTypePipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, textClearTypePipeline, nullptr);
    }
#endif
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
    if (destroyPipeline && vcTrianglePipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, vcTrianglePipeline, nullptr);
    }
    if (destroyPipeline && engineBatchPipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, engineBatchPipeline, nullptr);
    }
    // The Vello composite pipeline is bound to frameRenderPass (destroyed just
    // below), so drop it here — EnsureVelloCompositePipeline rebuilds it against
    // the new render pass after a swap-chain recreate. The layout / descriptor
    // layout / pools are render-pass-independent and persist.
    if (destroyPipeline && velloCompositePipeline != VK_NULL_HANDLE) {
        destroyPipeline(device, velloCompositePipeline, nullptr);
        velloCompositePipeline = VK_NULL_HANDLE;
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
#ifdef _WIN32
    if (destroyPipelineLayout && textPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, textPipelineLayout, nullptr);
    }
#endif
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
    if (destroyPipelineLayout && vcTrianglePipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, vcTrianglePipelineLayout, nullptr);
    }
    if (destroyPipelineLayout && engineBatchPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, engineBatchPipelineLayout, nullptr);
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
#ifdef _WIN32
    if (destroyDescriptorPool && textDescriptorPool != VK_NULL_HANDLE) {
        destroyDescriptorPool(device, textDescriptorPool, nullptr);
    }
#endif
    if (destroyDescriptorPool) {
        for (auto& inkPool : inkBlitDescriptorPools) {
            if (inkPool != VK_NULL_HANDLE) {
                destroyDescriptorPool(device, inkPool, nullptr);
                inkPool = VK_NULL_HANDLE;
            }
        }
        for (auto& csPool : customShaderDescPools) {
            if (csPool != VK_NULL_HANDLE) {
                destroyDescriptorPool(device, csPool, nullptr);
                csPool = VK_NULL_HANDLE;
            }
        }
    }
    // Custom shader effect: pipelines, PS modules, shared VS + layouts, and the
    // per-frame constants UBOs.
    if (destroyPipeline) {
        for (auto& kv : customShaderPipelines) {
            if (kv.second != VK_NULL_HANDLE) destroyPipeline(device, kv.second, nullptr);
        }
        customShaderPipelines.clear();
    }
    if (destroyShaderModule) {
        for (auto& kv : customShaderPsModules) {
            if (kv.second != VK_NULL_HANDLE) destroyShaderModule(device, kv.second, nullptr);
        }
        customShaderPsModules.clear();
        if (customShaderVs != VK_NULL_HANDLE) { destroyShaderModule(device, customShaderVs, nullptr); customShaderVs = VK_NULL_HANDLE; }
    }
    if (destroyPipelineLayout && customShaderPipelineLayout != VK_NULL_HANDLE) {
        destroyPipelineLayout(device, customShaderPipelineLayout, nullptr);
        customShaderPipelineLayout = VK_NULL_HANDLE;
    }
    if (destroyDescriptorSetLayout && customShaderDescLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, customShaderDescLayout, nullptr);
        customShaderDescLayout = VK_NULL_HANDLE;
    }
    for (uint32_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        if (customShaderConstantsMapped[i] && customShaderConstantsMemory[i] != VK_NULL_HANDLE) {
            unmapMemory(device, customShaderConstantsMemory[i]);
            customShaderConstantsMapped[i] = nullptr;
        }
        if (customShaderConstantsBuffers[i] != VK_NULL_HANDLE && destroyBuffer) {
            destroyBuffer(device, customShaderConstantsBuffers[i], nullptr);
            customShaderConstantsBuffers[i] = VK_NULL_HANDLE;
        }
        if (customShaderConstantsMemory[i] != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, customShaderConstantsMemory[i], nullptr);
            customShaderConstantsMemory[i] = VK_NULL_HANDLE;
        }
        customShaderConstantsCapacity[i] = 0;
    }
    customShaderBaseReady = false;
    customShaderBaseAttempted = false;
    if (destroyDescriptorSetLayout && transitionDescriptorSetLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, transitionDescriptorSetLayout, nullptr);
    }
    if (destroyDescriptorSetLayout && frameDescriptorSetLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, frameDescriptorSetLayout, nullptr);
    }
#ifdef _WIN32
    if (destroyDescriptorSetLayout && textDescriptorSetLayout != VK_NULL_HANDLE) {
        destroyDescriptorSetLayout(device, textDescriptorSetLayout, nullptr);
    }
#endif
    if (destroySampler && frameSampler != VK_NULL_HANDLE) {
        destroySampler(device, frameSampler, nullptr);
    }

    solidRectPipeline = VK_NULL_HANDLE;
    clearRectPipeline = VK_NULL_HANDLE;
    solidRectPipelineLayout = VK_NULL_HANDLE;
    bitmapPipeline = VK_NULL_HANDLE;
    inkCompositePipeline = VK_NULL_HANDLE;
    bitmapPipelineLayout = VK_NULL_HANDLE;
#ifdef _WIN32
    textPipeline = VK_NULL_HANDLE;
    textClearTypePipeline = VK_NULL_HANDLE;
    textPipelineLayout = VK_NULL_HANDLE;
    textDescriptorSet = VK_NULL_HANDLE;
    textDescriptorPool = VK_NULL_HANDLE;
    textDescriptorSetLayout = VK_NULL_HANDLE;
#endif
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
    vcTrianglePipeline = VK_NULL_HANDLE;
    vcTrianglePipelineLayout = VK_NULL_HANDLE;
    engineBatchPipeline = VK_NULL_HANDLE;
    engineBatchPipelineLayout = VK_NULL_HANDLE;
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
    fencePending = s.fencePending;
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
    s.fencePending = fencePending;
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
        // Engine batches per-frame upload buffer.
        if (s.engineBatchMapped != nullptr && s.engineBatchMemory != VK_NULL_HANDLE && unmapMemory) {
            unmapMemory(device, s.engineBatchMemory);
        }
        if (s.engineBatchBuffer != VK_NULL_HANDLE && destroyBuffer) {
            destroyBuffer(device, s.engineBatchBuffer, nullptr);
        }
        if (s.engineBatchMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.engineBatchMemory, nullptr);
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

// Releases the per-frame staging buffers, upload images, and engine batch
// buffers — but keeps the per-frame fences, semaphores, command buffers,
// descriptor sets, and renderFinishedPerImage semaphores intact so the next
// BeginFrame can resume rendering without re-initializing the per-frame
// command-buffer protocol. EnsureStagingCapacity / EnsureUploadImage /
// EnsureEngineBatchBuffer all check their alias-field capacity first; once
// we zero those, the next draw lazily re-allocates with the smallest power-
// of-two it actually needs, which on a now-quiet UI is typically a fraction
// of the peak.
//
// Called from VulkanRenderTarget::ReclaimIdleResources only when the managed
// reclaimer has confirmed the application is idle (see
// ResourceReclaimer.ScanAndReclaim). Idle implies in-flight fences have
// already fired in practice — vkDeviceWaitIdle still runs as a belt-and-
// braces guard against a frame the GPU has not finished while the UI thread
// raced ahead between Render and the reclaim tick.
void VulkanRenderTarget::Impl::ShrinkPerFrameBuffers()
{
    if (!device || !deviceWaitIdle) {
        return;
    }
    // Lost RT → never poke the device from the managed idle reclaimer (it
    // keeps ticking ~2s while recovery is throttled). Nothing here has a
    // consumer anymore — BeginDraw's gate rejects all further frames and
    // Destroy() reclaims everything.
    if (!DeviceUsable()) {
        return;
    }

    // Drain all in-flight GPU work so it is safe to destroy the per-frame
    // VkBuffer / VkImage / VkDeviceMemory objects below. The only
    // alternative is a deferred-destroy queue keyed on per-frame fences;
    // since this path runs at most once every IdleTimeoutMs (default 2 s)
    // on an idle UI, deferring would only add complexity without saving
    // any wall-clock time — the wait completes immediately when there is
    // nothing in flight.
    NoteVk(deviceWaitIdle(device), "ShrinkPerFrameBuffers.deviceWaitIdle");

    // Make sure the alias slot is written back into perFrameStates_ first,
    // otherwise the iteration below would free stale slot pointers and leak
    // the live alias allocations.
    CommitCurrentFrame();

    for (uint32_t i = 0; i < MAX_FRAMES_IN_FLIGHT; ++i) {
        auto& s = perFrameStates_[i];

        // Upload image (per-frame texture for bitmap / desktop-capture uploads)
        if (s.uploadImageView != VK_NULL_HANDLE && destroyImageView) {
            destroyImageView(device, s.uploadImageView, nullptr);
            s.uploadImageView = VK_NULL_HANDLE;
        }
        if (s.uploadImage != VK_NULL_HANDLE && destroyImage) {
            destroyImage(device, s.uploadImage, nullptr);
            s.uploadImage = VK_NULL_HANDLE;
        }
        if (s.uploadImageMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.uploadImageMemory, nullptr);
            s.uploadImageMemory = VK_NULL_HANDLE;
        }
        s.uploadWidth = 0;
        s.uploadHeight = 0;
        s.uploadImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;

        // Staging buffer (host-visible, for bitmap pixel transfers)
        if (s.mappedPixels && s.stagingMemory != VK_NULL_HANDLE && unmapMemory) {
            unmapMemory(device, s.stagingMemory);
            s.mappedPixels = nullptr;
        }
        if (s.stagingBuffer != VK_NULL_HANDLE && destroyBuffer) {
            destroyBuffer(device, s.stagingBuffer, nullptr);
            s.stagingBuffer = VK_NULL_HANDLE;
        }
        if (s.stagingMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.stagingMemory, nullptr);
            s.stagingMemory = VK_NULL_HANDLE;
        }
        s.mappedPixelCapacity = 0;

        // Engine batch buffer (vertex+index, Impeller engine output)
        if (s.engineBatchMapped && s.engineBatchMemory != VK_NULL_HANDLE && unmapMemory) {
            unmapMemory(device, s.engineBatchMemory);
            s.engineBatchMapped = nullptr;
        }
        if (s.engineBatchBuffer != VK_NULL_HANDLE && destroyBuffer) {
            destroyBuffer(device, s.engineBatchBuffer, nullptr);
            s.engineBatchBuffer = VK_NULL_HANDLE;
        }
        if (s.engineBatchMemory != VK_NULL_HANDLE && freeMemory) {
            freeMemory(device, s.engineBatchMemory, nullptr);
            s.engineBatchMemory = VK_NULL_HANDLE;
        }
        s.engineBatchCapacity = 0;

        // Intentionally NOT touched: commandBuffer, inFlight (fence),
        // imageAvailable (semaphore), frameDescriptorSet, submitted,
        // initialized — those describe the per-frame protocol state the
        // command-buffer ring depends on. Destroying them would force a
        // full PerFrameState rebuild on the next draw, which is the job of
        // DestroyPerFrameResources at swapchain teardown, not idle reclaim.
    }

    // Re-aliase the current slot's now-zeroed values into the alias fields
    // so EnsureStagingCapacity / EnsureUploadImage / EnsureEngineBatchBuffer
    // see "no buffer" on their next call and lazily re-allocate.
    BeginFrame();
}

bool VulkanRenderTarget::Impl::DrawFrame(const uint8_t* pixels, uint32_t width, uint32_t height)
{
    BeginFrame();
    // Latched loss → never touch the device again (defense in depth; EndDraw's
    // entry gate normally aborts before reaching here).
    if (!DeviceUsable()) {
        EndFrame();
        return false;
    }
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
    //
    // Frame-pacing instrumentation: every wait (success path) accumulates into
    // accumulatingWaitNs; the next successful BeginCommandBuffer flushes the
    // accumulator into lastFrameWaitNs so QueryGpuStats can surface it as
    // frameGpuWaitNs. Mirrors D3D12DirectRenderer::BeginFrame.
    if (fencePending) {
        uint64_t waitStart = MonotonicNowNs();
        VkResult waitResult = NoteVk(waitForFences(device, 1, &inFlight, VK_TRUE, std::numeric_limits<uint64_t>::max()),
                                     "DrawFrame.waitForFences");
        accumulatingWaitNs += MonotonicDiffNs(waitStart, MonotonicNowNs());
        if (waitResult == VK_SUCCESS) {
            fencePending = false;
        }
        if (waitResult != VK_SUCCESS) {
            VK_LOG("[Vulkan] DrawFrame: waitForFences failed\n");
            // Unlike D3D12 where a failed BeginFrame attempt is part of a
            // retry loop that keeps the accumulator, Vulkan has no
            // per-frame retry — the next DrawFrame call is a fresh logical
            // frame. Zero the accumulator so the next frame's wait time is
            // not polluted with leftover ticks from this failed attempt.
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return false;
        }
    }

    // Fence observed complete → the per-frame timing data the previous
    // submission resolved into the query pool is safe to read back now.
    if (timingSupported) {
        DecodeGpuTimingForCompletedFrame(currentFrame_);
    }
    // Present→ready wall clock: now that the fence signaled, diff from the
    // last successful queueSubmit's QPC stamp.  Skipped on the very first
    // frame (lastSubmitMonotonicNs == 0).
    if (lastSubmitMonotonicNs != 0) {
        lastFramePresentToReadyNs = MonotonicDiffNs(lastSubmitMonotonicNs, MonotonicNowNs());
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
    {
        // acquireNextImage shares the frame-pacing accumulator with the
        // fence wait — both are CPU stalls waiting for the GPU / OS to be
        // ready, so they count toward the same "wait" total reported to
        // DevTools.
        uint64_t acqStart = MonotonicNowNs();
        // DEVICE_LOST / SURFACE_LOST from the acquire is the GPU-switch
        // signature on Vulkan — NoteVk latches it so EndDraw reports
        // DEVICE_LOST instead of a transient failure. OUT_OF_DATE stays the
        // resize signal it always was.
        const VkResult acquireResult = NoteVk(acquireNextImage(device, swapchain, std::numeric_limits<uint64_t>::max(), imageAvailable, VK_NULL_HANDLE, &imageIndex),
                                              "DrawFrame.acquireNextImage");
        accumulatingWaitNs += MonotonicDiffNs(acqStart, MonotonicNowNs());
        if (acquireResult == VK_ERROR_OUT_OF_DATE_KHR) {
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return true;
        }
        if (acquireResult != VK_SUCCESS && acquireResult != VK_SUBOPTIMAL_KHR) {
            VK_LOG("[Vulkan] DrawFrame: acquireNextImage failed (%d)\n", static_cast<int>(acquireResult));
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return false;
        }
    }

    // NOTE: resetFences deliberately does NOT happen here. It is deferred to
    // right before queueSubmit: any failure exit between an early reset and
    // the submit would leave this slot's fence permanently unsignaled, and the
    // slot's next waitForFences(UINT64_MAX) would hang the render loop
    // forever. Between the deferred reset and the submit nothing can fail, so
    // the fence is either signaled (no submit happened) or pending (submit
    // owns it) — never orphaned.

    if (NoteVk(resetCommandBuffer(commandBuffer, 0), "DrawFrame.resetCommandBuffer") != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: resetCommandBuffer failed\n");
        EndFrame();
        return false;
    }

    VkCommandBufferBeginInfo beginInfo {};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (NoteVk(beginCommandBuffer(commandBuffer, &beginInfo), "DrawFrame.beginCommandBuffer") != VK_SUCCESS) {
        // This early-out used to skip EndFrame(), leaving the per-frame ring
        // un-advanced (the alias slot never committed back) — the only DrawFrame
        // exit with that imbalance. Keep it symmetric with every other exit.
        EndFrame();
        return false;
    }

    // Frame-pacing flush: every wait + acquire that fed into accumulatingWaitNs
    // is now consumed by a successful frame attempt. Publish the total to the
    // reader-visible field and reset for the next frame.
    lastFrameWaitNs = accumulatingWaitNs;
    accumulatingWaitNs = 0;

    // GPU timing: reset this frame's portion of the query pool, start span 0
    // tagged as "Other" (covers everything before the first explicit category
    // mark — pipeline barriers, blit setup, etc.). DrawFrame is the CPU-upload
    // fallback path, so most of the work after the initial Other slot is
    // bitmap blit + render-pass composition — recorded as a single Bitmap span
    // by the explicit Mark below.
    if (timingSupported && cmdResetQueryPool && cmdWriteTimestamp && timingQueryPool != VK_NULL_HANDLE) {
        auto& tf = timing[currentFrame_];
        tf.nextSlot = 0;
        tf.spanCategories.clear();
        tf.batchCountAtFinalize = 0;
        tf.hasResolvedData = false;
        uint32_t base = currentFrame_ * kMaxTimingSlotsPerFrame;
        cmdResetQueryPool(commandBuffer, timingQueryPool, base, kMaxTimingSlotsPerFrame);
        MarkGpuTimingPoint(GpuTimingCategory::Other);
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

    // The CPU-upload fallback bottleneck is the BufferToImage copy + render
    // pass composition. Tag the span up to the final timestamp as "Bitmap"
    // so DevTools sees that DrawFrame is paying for blit, not draw work.
    MarkGpuTimingPoint(GpuTimingCategory::Bitmap);

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

    // Final timestamp for this frame. kFrameEnd is a sentinel — the decoder
    // computes total span as (last − first) but skips the kFrameEnd entry
    // when accumulating per-category time.
    if (timingSupported && cmdWriteTimestamp && timingQueryPool != VK_NULL_HANDLE) {
        MarkGpuTimingPoint(GpuTimingCategory::kFrameEnd);
        timing[currentFrame_].hasResolvedData = true;
        // DrawFrame doesn't carry a "batch count" concept (it uploads the
        // full CPU canvas as one blit), so report 1 for "one bitmap blit
        // per frame" — matches the user-visible expectation.
        timing[currentFrame_].batchCountAtFinalize = 1;
    }

    if (NoteVk(endCommandBuffer(commandBuffer), "DrawFrame.endCommandBuffer") != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: endCommandBuffer failed\n");
        // Same ring-balance fix as the beginCommandBuffer exit above.
        EndFrame();
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
    // Deferred fence reset (see the comment at the former call site above):
    // nothing between here and queueSubmit can fail. The one remaining hole —
    // queueSubmit itself failing AFTER this reset (spec: the fence is then
    // NOT signaled) — is closed by fencePending: the slot's next wait is
    // skipped while no submit owns the fence. If the reset itself fails
    // (OOM-class, near theoretical) the fence stays signaled and the next
    // gated wait — if any — passes through.
    if (NoteVk(resetFences(device, 1, &inFlight), "DrawFrame.resetFences") != VK_SUCCESS) {
        VK_LOG("[Vulkan] DrawFrame: resetFences failed\n");
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
    VkResult submitResult = NoteVk(queueSubmit(queue, 1, &submitInfo, inFlight), "DrawFrame.queueSubmit");
    if (submitResult != VK_SUCCESS) {
        // fencePending stays false: the failed submit does not signal the
        // fence (spec) and the fence is now unsignaled from the reset above —
        // the gated wait skips it instead of parking forever.
        VK_LOG("[Vulkan] DrawFrame: queueSubmit failed (%d)\n", static_cast<int>(submitResult));
        EndFrame();
        return false;
    }
    fencePending = true;
    // Stamp QPC right after the submit returns — the next BeginFrame uses
    // this to compute lastFramePresentToReadyNs once the corresponding
    // fence signals.
    lastSubmitMonotonicNs = MonotonicNowNs();

    VkPresentInfoKHR presentInfo {};
    presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores = &signalSemaphore;
    presentInfo.swapchainCount = 1;
    presentInfo.pSwapchains = &swapchain;
    presentInfo.pImageIndices = &imageIndex;
    const VkResult presentResult = NoteVk(queuePresent(queue, &presentInfo), "DrawFrame.queuePresent");
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

// ---------------------------------------------------------------------------
// Engine batches GPU consumer
//
// EnsureEngineBatchPipeline / EnsureEngineBatchBuffer / RenderEngineBatches
// turn the encoder side (ImpellerVulkanEngine / VelloVulkanEngine) into
// actual draws on the swap-chain frame. The pipeline reads VkImpellerVertex
// (pos + premultiplied-alpha color, 24B) and a 4×4 MVP push constant; the
// fragment shader passes the vertex color through. Per-frame upload buffers
// (PerFrameState::engineBatchBuffer) prevent the GPU from reading data the
// CPU is overwriting in the next frame.
// ---------------------------------------------------------------------------

bool VulkanRenderTarget::Impl::EnsureEngineBatchPipeline()
{
    if (engineBatchPipeline != VK_NULL_HANDLE) return true;
    if (device == VK_NULL_HANDLE || frameRenderPass == VK_NULL_HANDLE) return false;

    auto createPipelineLayout =
        LoadDeviceProc<PFN_vkCreatePipelineLayout>(getDeviceProcAddr, device, "vkCreatePipelineLayout");
    auto createShaderModule =
        LoadDeviceProc<PFN_vkCreateShaderModule>(getDeviceProcAddr, device, "vkCreateShaderModule");
    auto destroyShaderModule =
        LoadDeviceProc<PFN_vkDestroyShaderModule>(getDeviceProcAddr, device, "vkDestroyShaderModule");
    auto createGraphicsPipelines =
        LoadDeviceProc<PFN_vkCreateGraphicsPipelines>(getDeviceProcAddr, device, "vkCreateGraphicsPipelines");
    if (!createPipelineLayout || !createShaderModule || !destroyShaderModule || !createGraphicsPipelines) {
        return false;
    }

    if (engineBatchPipelineLayout == VK_NULL_HANDLE) {
        VkPushConstantRange pushConstantRange {};
        pushConstantRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
        pushConstantRange.size = 64; // 4x4 float MVP

        VkPipelineLayoutCreateInfo layoutInfo {};
        layoutInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        layoutInfo.pushConstantRangeCount = 1;
        layoutInfo.pPushConstantRanges = &pushConstantRange;
        if (createPipelineLayout(device, &layoutInfo, nullptr, &engineBatchPipelineLayout) != VK_SUCCESS
            || engineBatchPipelineLayout == VK_NULL_HANDLE) {
            return false;
        }
    }

    VkShaderModule vertexShader = VK_NULL_HANDLE;
    VkShaderModule fragmentShader = VK_NULL_HANDLE;

    VkShaderModuleCreateInfo vertexShaderInfo {};
    vertexShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    vertexShaderInfo.codeSize = kImpellerSolidFillVertShaderSpvSize;
    vertexShaderInfo.pCode = kImpellerSolidFillVertShaderSpv;
    if (createShaderModule(device, &vertexShaderInfo, nullptr, &vertexShader) != VK_SUCCESS
        || vertexShader == VK_NULL_HANDLE) {
        return false;
    }

    VkShaderModuleCreateInfo fragmentShaderInfo {};
    fragmentShaderInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    fragmentShaderInfo.codeSize = kImpellerSolidFillFragShaderSpvSize;
    fragmentShaderInfo.pCode = kImpellerSolidFillFragShaderSpv;
    if (createShaderModule(device, &fragmentShaderInfo, nullptr, &fragmentShader) != VK_SUCCESS
        || fragmentShader == VK_NULL_HANDLE) {
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
    bindingDescription.stride = sizeof(VkImpellerVertex);
    bindingDescription.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;

    VkVertexInputAttributeDescription attributes[2] {};
    attributes[0].location = 0;
    attributes[0].binding = 0;
    attributes[0].format = VK_FORMAT_R32G32_SFLOAT;
    attributes[0].offset = 0;
    attributes[1].location = 1;
    attributes[1].binding = 0;
    attributes[1].format = VK_FORMAT_R32G32B32A32_SFLOAT;
    attributes[1].offset = sizeof(float) * 2;

    VkPipelineVertexInputStateCreateInfo vertexInputInfo {};
    vertexInputInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    vertexInputInfo.vertexBindingDescriptionCount = 1;
    vertexInputInfo.pVertexBindingDescriptions = &bindingDescription;
    vertexInputInfo.vertexAttributeDescriptionCount = 2;
    vertexInputInfo.pVertexAttributeDescriptions = attributes;

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

    // Premultiplied-alpha SrcOver blend (engines pre-multiply at encode time
    // and the fragment shader passes the color through unchanged).
    VkPipelineColorBlendAttachmentState colorBlendAttachment {};
    colorBlendAttachment.blendEnable = VK_TRUE;
    colorBlendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
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
    pipelineInfo.stageCount = 2;
    pipelineInfo.pStages = shaderStages;
    pipelineInfo.pVertexInputState = &vertexInputInfo;
    pipelineInfo.pInputAssemblyState = &inputAssemblyInfo;
    pipelineInfo.pViewportState = &viewportStateInfo;
    pipelineInfo.pRasterizationState = &rasterizationInfo;
    pipelineInfo.pMultisampleState = &multisampleInfo;
    pipelineInfo.pColorBlendState = &colorBlendInfo;
    pipelineInfo.pDynamicState = &dynamicStateInfo;
    pipelineInfo.layout = engineBatchPipelineLayout;
    pipelineInfo.renderPass = frameRenderPass;
    pipelineInfo.subpass = 0;

    const VkResult pipelineResult = createGraphicsPipelines(
        device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &engineBatchPipeline);
    destroyShaderModule(device, fragmentShader, nullptr);
    destroyShaderModule(device, vertexShader, nullptr);
    if (pipelineResult != VK_SUCCESS || engineBatchPipeline == VK_NULL_HANDLE) {
        return false;
    }
    return true;
}

bool VulkanRenderTarget::Impl::EnsureEngineBatchBuffer(uint32_t frameIdx, VkDeviceSize requiredSize)
{
    if (frameIdx >= MAX_FRAMES_IN_FLIGHT) return false;
    PerFrameState& frame = perFrameStates_[frameIdx];
    if (frame.engineBatchBuffer != VK_NULL_HANDLE && frame.engineBatchCapacity >= requiredSize) {
        return true;
    }

    auto createBuffer = LoadDeviceProc<PFN_vkCreateBuffer>(getDeviceProcAddr, device, "vkCreateBuffer");
    auto destroyBuffer = LoadDeviceProc<PFN_vkDestroyBuffer>(getDeviceProcAddr, device, "vkDestroyBuffer");
    auto allocateMemory = LoadDeviceProc<PFN_vkAllocateMemory>(getDeviceProcAddr, device, "vkAllocateMemory");
    auto freeMemory = LoadDeviceProc<PFN_vkFreeMemory>(getDeviceProcAddr, device, "vkFreeMemory");
    auto bindBufferMemory = LoadDeviceProc<PFN_vkBindBufferMemory>(getDeviceProcAddr, device, "vkBindBufferMemory");
    auto getBufferMemReqs = LoadDeviceProc<PFN_vkGetBufferMemoryRequirements>(getDeviceProcAddr, device, "vkGetBufferMemoryRequirements");
    auto mapMemory = LoadDeviceProc<PFN_vkMapMemory>(getDeviceProcAddr, device, "vkMapMemory");
    auto unmapMemory = LoadDeviceProc<PFN_vkUnmapMemory>(getDeviceProcAddr, device, "vkUnmapMemory");
    if (!createBuffer || !destroyBuffer || !allocateMemory || !freeMemory
        || !bindBufferMemory || !getBufferMemReqs || !mapMemory || !unmapMemory) {
        return false;
    }

    // Tear down any previous, smaller buffer first.
    if (frame.engineBatchMapped && frame.engineBatchMemory) {
        unmapMemory(device, frame.engineBatchMemory);
        frame.engineBatchMapped = nullptr;
    }
    if (frame.engineBatchBuffer != VK_NULL_HANDLE) {
        destroyBuffer(device, frame.engineBatchBuffer, nullptr);
        frame.engineBatchBuffer = VK_NULL_HANDLE;
    }
    if (frame.engineBatchMemory != VK_NULL_HANDLE) {
        freeMemory(device, frame.engineBatchMemory, nullptr);
        frame.engineBatchMemory = VK_NULL_HANDLE;
    }
    frame.engineBatchCapacity = 0;

    // Round to next power-of-two-ish chunk so we don't realloc every frame
    // for shapes that grow modestly.
    VkDeviceSize allocSize = 64 * 1024;
    while (allocSize < requiredSize) allocSize *= 2;

    VkBufferCreateInfo bufferInfo {};
    bufferInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
    bufferInfo.size = allocSize;
    bufferInfo.usage = VK_BUFFER_USAGE_VERTEX_BUFFER_BIT | VK_BUFFER_USAGE_INDEX_BUFFER_BIT;
    bufferInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
    if (createBuffer(device, &bufferInfo, nullptr, &frame.engineBatchBuffer) != VK_SUCCESS) {
        return false;
    }

    VkMemoryRequirements memReqs {};
    getBufferMemReqs(device, frame.engineBatchBuffer, &memReqs);
    const uint32_t typeIndex = FindMemoryType(memReqs.memoryTypeBits,
        VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
    if (typeIndex == UINT32_MAX) {
        destroyBuffer(device, frame.engineBatchBuffer, nullptr);
        frame.engineBatchBuffer = VK_NULL_HANDLE;
        return false;
    }

    VkMemoryAllocateInfo allocInfo {};
    allocInfo.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocInfo.allocationSize = memReqs.size;
    allocInfo.memoryTypeIndex = typeIndex;
    if (allocateMemory(device, &allocInfo, nullptr, &frame.engineBatchMemory) != VK_SUCCESS) {
        destroyBuffer(device, frame.engineBatchBuffer, nullptr);
        frame.engineBatchBuffer = VK_NULL_HANDLE;
        return false;
    }
    if (bindBufferMemory(device, frame.engineBatchBuffer, frame.engineBatchMemory, 0) != VK_SUCCESS
        || mapMemory(device, frame.engineBatchMemory, 0, allocSize, 0, &frame.engineBatchMapped) != VK_SUCCESS) {
        freeMemory(device, frame.engineBatchMemory, nullptr);
        destroyBuffer(device, frame.engineBatchBuffer, nullptr);
        frame.engineBatchMemory = VK_NULL_HANDLE;
        frame.engineBatchBuffer = VK_NULL_HANDLE;
        return false;
    }
    frame.engineBatchCapacity = allocSize;
    return true;
}

void VulkanRenderTarget::Impl::DestroyStencilCoverResources()
{
    if (device != VK_NULL_HANDLE) {
        auto destroyPipeline    = LoadDeviceProc<PFN_vkDestroyPipeline>(getDeviceProcAddr, device, "vkDestroyPipeline");
        auto destroyRenderPass  = LoadDeviceProc<PFN_vkDestroyRenderPass>(getDeviceProcAddr, device, "vkDestroyRenderPass");
        auto destroyFramebuffer = LoadDeviceProc<PFN_vkDestroyFramebuffer>(getDeviceProcAddr, device, "vkDestroyFramebuffer");
        auto destroyImageView   = LoadDeviceProc<PFN_vkDestroyImageView>(getDeviceProcAddr, device, "vkDestroyImageView");
        auto destroyImage       = LoadDeviceProc<PFN_vkDestroyImage>(getDeviceProcAddr, device, "vkDestroyImage");
        auto freeMemory         = LoadDeviceProc<PFN_vkFreeMemory>(getDeviceProcAddr, device, "vkFreeMemory");
        auto destroySampler     = LoadDeviceProc<PFN_vkDestroySampler>(getDeviceProcAddr, device, "vkDestroySampler");
        if (destroyPipeline) {
            if (psoStencilFillEvenOdd) destroyPipeline(device, psoStencilFillEvenOdd, nullptr);
            if (psoStencilFillNonZero) destroyPipeline(device, psoStencilFillNonZero, nullptr);
            if (psoStencilCover)       destroyPipeline(device, psoStencilCover, nullptr);
            if (psoStencilQuad)        destroyPipeline(device, psoStencilQuad, nullptr);
        }
        if (destroyFramebuffer && stencilCoverFramebuffer) destroyFramebuffer(device, stencilCoverFramebuffer, nullptr);
        if (destroyRenderPass && stencilCoverRenderPass)   destroyRenderPass(device, stencilCoverRenderPass, nullptr);
        if (destroySampler && stencilResolveSampler)       destroySampler(device, stencilResolveSampler, nullptr);
        if (destroyImageView) {
            if (stencilMsaaColorView) destroyImageView(device, stencilMsaaColorView, nullptr);
            if (stencilMsaaDsView)    destroyImageView(device, stencilMsaaDsView, nullptr);
            if (stencilResolveView)   destroyImageView(device, stencilResolveView, nullptr);
        }
        if (destroyImage) {
            if (stencilMsaaColorImage) destroyImage(device, stencilMsaaColorImage, nullptr);
            if (stencilMsaaDsImage)    destroyImage(device, stencilMsaaDsImage, nullptr);
            if (stencilResolveImage)   destroyImage(device, stencilResolveImage, nullptr);
        }
        if (freeMemory) {
            if (stencilMsaaColorMemory) freeMemory(device, stencilMsaaColorMemory, nullptr);
            if (stencilMsaaDsMemory)    freeMemory(device, stencilMsaaDsMemory, nullptr);
            if (stencilResolveMemory)   freeMemory(device, stencilResolveMemory, nullptr);
        }
    }
    psoStencilFillEvenOdd = VK_NULL_HANDLE;
    psoStencilFillNonZero = VK_NULL_HANDLE;
    psoStencilCover = VK_NULL_HANDLE;
    psoStencilQuad = VK_NULL_HANDLE;
    stencilCoverFramebuffer = VK_NULL_HANDLE;
    stencilCoverRenderPass = VK_NULL_HANDLE;
    stencilResolveSampler = VK_NULL_HANDLE;
    stencilMsaaColorView = VK_NULL_HANDLE;
    stencilMsaaDsView = VK_NULL_HANDLE;
    stencilResolveView = VK_NULL_HANDLE;
    stencilMsaaColorImage = VK_NULL_HANDLE;
    stencilMsaaDsImage = VK_NULL_HANDLE;
    stencilResolveImage = VK_NULL_HANDLE;
    stencilMsaaColorMemory = VK_NULL_HANDLE;
    stencilMsaaDsMemory = VK_NULL_HANDLE;
    stencilResolveMemory = VK_NULL_HANDLE;
    stencilCoverW = 0;
    stencilCoverH = 0;
}

bool VulkanRenderTarget::Impl::EnsureEffectOffscreenResources(VkExtent2D extent)
{
    if (!effectOffscreenEnabled_) return false;
    if (extent.width == 0 || extent.height == 0) return false;
    if (effectOffscreenFramebufferClear != VK_NULL_HANDLE
        && effectOffscreenW == extent.width && effectOffscreenH == extent.height) {
        return true;  // already built at this size
    }
    DestroyEffectOffscreenResources();
    if (device == VK_NULL_HANDLE) return false;

    auto createImage         = LoadDeviceProc<PFN_vkCreateImage>(getDeviceProcAddr, device, "vkCreateImage");
    auto getImageMemReqs     = LoadDeviceProc<PFN_vkGetImageMemoryRequirements>(getDeviceProcAddr, device, "vkGetImageMemoryRequirements");
    auto allocateMemory      = LoadDeviceProc<PFN_vkAllocateMemory>(getDeviceProcAddr, device, "vkAllocateMemory");
    auto bindImageMemory     = LoadDeviceProc<PFN_vkBindImageMemory>(getDeviceProcAddr, device, "vkBindImageMemory");
    auto createImageView     = LoadDeviceProc<PFN_vkCreateImageView>(getDeviceProcAddr, device, "vkCreateImageView");
    auto createRenderPass    = LoadDeviceProc<PFN_vkCreateRenderPass>(getDeviceProcAddr, device, "vkCreateRenderPass");
    auto createFramebuffer   = LoadDeviceProc<PFN_vkCreateFramebuffer>(getDeviceProcAddr, device, "vkCreateFramebuffer");
    auto createSampler       = LoadDeviceProc<PFN_vkCreateSampler>(getDeviceProcAddr, device, "vkCreateSampler");
    if (!createImage || !getImageMemReqs || !allocateMemory || !bindImageMemory || !createImageView
        || !createRenderPass || !createFramebuffer || !createSampler) {
        return false;
    }

    // image+memory+view (single color, no MSAA, COLOR_ATTACHMENT|SAMPLED|TRANSFER_SRC,
    // format == swap-chain format). TRANSFER_SRC kept so a later stage may blit the
    // isolated subtree into uploadImage if a sample-direct path proves awkward.
    {
        VkImageCreateInfo ici {};
        ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        ici.imageType = VK_IMAGE_TYPE_2D;
        ici.format = format;                       // B8G8R8A8_UNORM
        ici.extent.width = extent.width;
        ici.extent.height = extent.height;
        ici.extent.depth = 1;
        ici.mipLevels = 1;
        ici.arrayLayers = 1;
        ici.samples = VK_SAMPLE_COUNT_1_BIT;
        ici.tiling = VK_IMAGE_TILING_OPTIMAL;
        ici.usage = VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT | VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (createImage(device, &ici, nullptr, &effectOffscreenImage) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
        VkMemoryRequirements mr {};
        getImageMemReqs(device, effectOffscreenImage, &mr);
        const uint32_t ti = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (ti == UINT32_MAX) { DestroyEffectOffscreenResources(); return false; }
        VkMemoryAllocateInfo mai {};
        mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        mai.allocationSize = mr.size;
        mai.memoryTypeIndex = ti;
        if (allocateMemory(device, &mai, nullptr, &effectOffscreenMemory) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
        if (bindImageMemory(device, effectOffscreenImage, effectOffscreenMemory, 0) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
        VkImageViewCreateInfo vci {};
        vci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        vci.image = effectOffscreenImage;
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = format;
        vci.subresourceRange.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
        vci.subresourceRange.levelCount = 1;
        vci.subresourceRange.layerCount = 1;
        if (createImageView(device, &vci, nullptr, &effectOffscreenView) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
    }

    // sampler (NEAREST/CLAMP)
    {
        VkSamplerCreateInfo si {};
        si.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        si.magFilter = VK_FILTER_NEAREST;
        si.minFilter = VK_FILTER_NEAREST;
        si.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        si.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.maxLod = 0.0f;
        if (createSampler(device, &si, nullptr, &effectOffscreenSampler) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
    }

    // Two single-attachment render passes differing ONLY in loadOp + initialLayout.
    // Both finalLayout=SHADER_READ_ONLY_OPTIMAL so the image is sample-ready at
    // OffscreenEnd with no manual barrier. format==`format`, samples==1, single
    // subpass color ref@0 => RENDER-PASS COMPATIBLE with every existing pipeline
    // built against frameRenderPass (so no PSO rebuild needed to draw here).
    auto makeRenderPass = [&](VkAttachmentLoadOp loadOp, VkImageLayout initialLayout,
                              VkRenderPass& outRp) -> bool {
        VkAttachmentDescription att {};
        att.format = format;
        att.samples = VK_SAMPLE_COUNT_1_BIT;
        att.loadOp = loadOp;
        att.storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        att.stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        att.stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        att.initialLayout = initialLayout;
        att.finalLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkAttachmentReference colorRef { 0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL };
        VkSubpassDescription sub {};
        sub.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        sub.colorAttachmentCount = 1;
        sub.pColorAttachments = &colorRef;
        // dep0/dep1 cover the PREVIOUS frame's color WRITE + composite shader READ
        // to kill cross-frame WAW/RAW on the single-instance scratch.
        VkSubpassDependency deps[2] {};
        deps[0].srcSubpass = VK_SUBPASS_EXTERNAL;
        deps[0].dstSubpass = 0;
        deps[0].srcStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        deps[0].srcAccessMask = VK_ACCESS_SHADER_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        deps[0].dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        deps[0].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_COLOR_ATTACHMENT_READ_BIT;
        deps[1].srcSubpass = 0;
        deps[1].dstSubpass = VK_SUBPASS_EXTERNAL;
        deps[1].srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        deps[1].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        deps[1].dstStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
        deps[1].dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
        VkRenderPassCreateInfo rpi {};
        rpi.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
        rpi.attachmentCount = 1;
        rpi.pAttachments = &att;
        rpi.subpassCount = 1;
        rpi.pSubpasses = &sub;
        rpi.dependencyCount = 2;
        rpi.pDependencies = deps;
        return createRenderPass(device, &rpi, nullptr, &outRp) == VK_SUCCESS;
    };
    if (!makeRenderPass(VK_ATTACHMENT_LOAD_OP_CLEAR, VK_IMAGE_LAYOUT_UNDEFINED, effectOffscreenRenderPassClear)) { DestroyEffectOffscreenResources(); return false; }
    if (!makeRenderPass(VK_ATTACHMENT_LOAD_OP_LOAD,  VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, effectOffscreenRenderPassLoad)) { DestroyEffectOffscreenResources(); return false; }

    auto makeFramebuffer = [&](VkRenderPass rp, VkFramebuffer& outFb) -> bool {
        VkFramebufferCreateInfo fci {};
        fci.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        fci.renderPass = rp;
        fci.attachmentCount = 1;
        fci.pAttachments = &effectOffscreenView;
        fci.width = extent.width;
        fci.height = extent.height;
        fci.layers = 1;
        return createFramebuffer(device, &fci, nullptr, &outFb) == VK_SUCCESS;
    };
    if (!makeFramebuffer(effectOffscreenRenderPassClear, effectOffscreenFramebufferClear)) { DestroyEffectOffscreenResources(); return false; }
    if (!makeFramebuffer(effectOffscreenRenderPassLoad,  effectOffscreenFramebufferLoad))  { DestroyEffectOffscreenResources(); return false; }

    // Dedicated 1-set descriptor (set0: SAMPLED_IMAGE@0 + SAMPLER@1, same layout as
    // frameDescriptorSetLayout / bitmapPipelineLayout) bound permanently to the offscreen
    // view. Consumed by the OffscreenEnd composite-back (bitmapPipeline) and by content
    // effects sampling the isolated element (Stage 4). Created WITH the resources -- the
    // view is stable until resize/teardown, so there is no per-frame allocation and no
    // update-after-free hazard on the descriptor's image view.
    if (frameDescriptorSetLayout == VK_NULL_HANDLE || !createDescriptorPool
        || !allocateDescriptorSets || !updateDescriptorSets) { DestroyEffectOffscreenResources(); return false; }
    {
        VkDescriptorPoolSize ps[2] {};
        ps[0].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; ps[0].descriptorCount = 1;
        ps[1].type = VK_DESCRIPTOR_TYPE_SAMPLER;       ps[1].descriptorCount = 1;
        VkDescriptorPoolCreateInfo pci {};
        pci.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        pci.maxSets = 1; pci.poolSizeCount = 2; pci.pPoolSizes = ps;
        if (createDescriptorPool(device, &pci, nullptr, &effectOffscreenDescPool) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
        VkDescriptorSetAllocateInfo dai {};
        dai.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
        dai.descriptorPool = effectOffscreenDescPool;
        dai.descriptorSetCount = 1;
        dai.pSetLayouts = &frameDescriptorSetLayout;
        if (allocateDescriptorSets(device, &dai, &effectOffscreenDescriptorSet) != VK_SUCCESS) { DestroyEffectOffscreenResources(); return false; }
        VkDescriptorImageInfo viewInfo {};
        viewInfo.imageView = effectOffscreenView;
        viewInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkDescriptorImageInfo sampInfo {};
        sampInfo.sampler = effectOffscreenSampler;
        sampInfo.imageView = effectOffscreenView;   // NVIDIA derefs imageView even for SAMPLER (see frame set)
        sampInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        VkWriteDescriptorSet w[2] {};
        w[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        w[0].dstSet = effectOffscreenDescriptorSet; w[0].dstBinding = 0; w[0].descriptorCount = 1;
        w[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE; w[0].pImageInfo = &viewInfo;
        w[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
        w[1].dstSet = effectOffscreenDescriptorSet; w[1].dstBinding = 1; w[1].descriptorCount = 1;
        w[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER; w[1].pImageInfo = &sampInfo;
        updateDescriptorSets(device, 2, w, 0, nullptr);
    }

    effectOffscreenLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    effectOffscreenW = extent.width;
    effectOffscreenH = extent.height;
    return true;
}

void VulkanRenderTarget::Impl::DestroyEffectOffscreenResources()
{
    if (device != VK_NULL_HANDLE) {
        auto destroyRenderPass  = LoadDeviceProc<PFN_vkDestroyRenderPass>(getDeviceProcAddr, device, "vkDestroyRenderPass");
        auto destroyFramebuffer = LoadDeviceProc<PFN_vkDestroyFramebuffer>(getDeviceProcAddr, device, "vkDestroyFramebuffer");
        auto destroyImageView   = LoadDeviceProc<PFN_vkDestroyImageView>(getDeviceProcAddr, device, "vkDestroyImageView");
        auto destroyImage       = LoadDeviceProc<PFN_vkDestroyImage>(getDeviceProcAddr, device, "vkDestroyImage");
        auto freeMemory         = LoadDeviceProc<PFN_vkFreeMemory>(getDeviceProcAddr, device, "vkFreeMemory");
        auto destroySampler     = LoadDeviceProc<PFN_vkDestroySampler>(getDeviceProcAddr, device, "vkDestroySampler");
        auto destroyDescPool    = LoadDeviceProc<PFN_vkDestroyDescriptorPool>(getDeviceProcAddr, device, "vkDestroyDescriptorPool");
        if (destroyDescPool && effectOffscreenDescPool) destroyDescPool(device, effectOffscreenDescPool, nullptr); // frees the set too
        if (destroyFramebuffer) {
            if (effectOffscreenFramebufferClear) destroyFramebuffer(device, effectOffscreenFramebufferClear, nullptr);
            if (effectOffscreenFramebufferLoad)  destroyFramebuffer(device, effectOffscreenFramebufferLoad, nullptr);
        }
        if (destroyRenderPass) {
            if (effectOffscreenRenderPassClear) destroyRenderPass(device, effectOffscreenRenderPassClear, nullptr);
            if (effectOffscreenRenderPassLoad)  destroyRenderPass(device, effectOffscreenRenderPassLoad, nullptr);
        }
        if (destroySampler && effectOffscreenSampler) destroySampler(device, effectOffscreenSampler, nullptr);
        if (destroyImageView && effectOffscreenView)  destroyImageView(device, effectOffscreenView, nullptr);
        if (destroyImage && effectOffscreenImage)     destroyImage(device, effectOffscreenImage, nullptr);
        if (freeMemory && effectOffscreenMemory)      freeMemory(device, effectOffscreenMemory, nullptr);
    }
    effectOffscreenFramebufferClear = VK_NULL_HANDLE;
    effectOffscreenFramebufferLoad = VK_NULL_HANDLE;
    effectOffscreenRenderPassClear = VK_NULL_HANDLE;
    effectOffscreenRenderPassLoad = VK_NULL_HANDLE;
    effectOffscreenSampler = VK_NULL_HANDLE;
    effectOffscreenView = VK_NULL_HANDLE;
    effectOffscreenImage = VK_NULL_HANDLE;
    effectOffscreenMemory = VK_NULL_HANDLE;
    effectOffscreenDescPool = VK_NULL_HANDLE;
    effectOffscreenDescriptorSet = VK_NULL_HANDLE;
    effectOffscreenLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    effectOffscreenW = 0; effectOffscreenH = 0;
}

bool VulkanRenderTarget::Impl::EnsureStencilCoverResources(VkExtent2D extent)
{
    if (extent.width == 0 || extent.height == 0) return false;
    if (stencilCoverFramebuffer != VK_NULL_HANDLE && psoStencilCover != VK_NULL_HANDLE
        && stencilCoverW == extent.width && stencilCoverH == extent.height) {
        return true;  // already built at this size
    }
    DestroyStencilCoverResources();
    if (device == VK_NULL_HANDLE || frameRenderPass == VK_NULL_HANDLE) return false;
    // EnsureEngineBatchPipeline owns the shared 64B-MVP pipeline layout we reuse.
    if (!EnsureEngineBatchPipeline() || engineBatchPipelineLayout == VK_NULL_HANDLE) return false;

    auto createImage         = LoadDeviceProc<PFN_vkCreateImage>(getDeviceProcAddr, device, "vkCreateImage");
    auto getImageMemReqs     = LoadDeviceProc<PFN_vkGetImageMemoryRequirements>(getDeviceProcAddr, device, "vkGetImageMemoryRequirements");
    auto allocateMemory      = LoadDeviceProc<PFN_vkAllocateMemory>(getDeviceProcAddr, device, "vkAllocateMemory");
    auto bindImageMemory     = LoadDeviceProc<PFN_vkBindImageMemory>(getDeviceProcAddr, device, "vkBindImageMemory");
    auto createImageView     = LoadDeviceProc<PFN_vkCreateImageView>(getDeviceProcAddr, device, "vkCreateImageView");
    auto createRenderPass    = LoadDeviceProc<PFN_vkCreateRenderPass>(getDeviceProcAddr, device, "vkCreateRenderPass");
    auto createFramebuffer   = LoadDeviceProc<PFN_vkCreateFramebuffer>(getDeviceProcAddr, device, "vkCreateFramebuffer");
    auto createSampler       = LoadDeviceProc<PFN_vkCreateSampler>(getDeviceProcAddr, device, "vkCreateSampler");
    auto createShaderModule  = LoadDeviceProc<PFN_vkCreateShaderModule>(getDeviceProcAddr, device, "vkCreateShaderModule");
    auto destroyShaderModule = LoadDeviceProc<PFN_vkDestroyShaderModule>(getDeviceProcAddr, device, "vkDestroyShaderModule");
    auto createGraphicsPipelines = LoadDeviceProc<PFN_vkCreateGraphicsPipelines>(getDeviceProcAddr, device, "vkCreateGraphicsPipelines");
    if (!createImage || !getImageMemReqs || !allocateMemory || !bindImageMemory || !createImageView
        || !createRenderPass || !createFramebuffer || !createSampler
        || !createShaderModule || !destroyShaderModule || !createGraphicsPipelines) {
        return false;
    }

    // Sample count: highest of 8/4/2 the device supports as a framebuffer
    // color+depth+stencil attachment (mirrors the D3D12 path's 8x default,
    // clamped to support). Falls to 1x if MSAA is unavailable.
    VkSampleCountFlagBits samples = VK_SAMPLE_COUNT_1_BIT;
    {
        auto getProps = LoadInstanceProc<PFN_vkGetPhysicalDeviceProperties>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceProperties");
        VkSampleCountFlags caps = VK_SAMPLE_COUNT_1_BIT;
        if (getProps) {
            VkPhysicalDeviceProperties props {};
            getProps(physicalDevice, &props);
            caps = props.limits.framebufferColorSampleCounts
                 & props.limits.framebufferDepthSampleCounts
                 & props.limits.framebufferStencilSampleCounts;
        }
        const VkSampleCountFlagBits desired[] = {
            VK_SAMPLE_COUNT_8_BIT, VK_SAMPLE_COUNT_4_BIT, VK_SAMPLE_COUNT_2_BIT
        };
        for (VkSampleCountFlagBits s : desired) { if (caps & s) { samples = s; break; } }
    }

    // Depth/stencil format: D24S8 preferred, D32S8 fallback.
    VkFormat dsFormat = VK_FORMAT_D24_UNORM_S8_UINT;
    {
        auto getImgFmtProps = LoadInstanceProc<PFN_vkGetPhysicalDeviceImageFormatProperties>(getInstanceProcAddr, instance, "vkGetPhysicalDeviceImageFormatProperties");
        if (getImgFmtProps) {
            VkImageFormatProperties ifp {};
            if (getImgFmtProps(physicalDevice, VK_FORMAT_D24_UNORM_S8_UINT, VK_IMAGE_TYPE_2D,
                    VK_IMAGE_TILING_OPTIMAL, VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT, 0, &ifp) != VK_SUCCESS) {
                dsFormat = VK_FORMAT_D32_SFLOAT_S8_UINT;
            }
        }
    }

    auto makeImage = [&](VkFormat fmt, VkSampleCountFlagBits s, VkImageUsageFlags usage,
                         VkImageAspectFlags aspect,
                         VkImage& img, VkDeviceMemory& mem, VkImageView& view) -> bool {
        VkImageCreateInfo ici {};
        ici.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        ici.imageType = VK_IMAGE_TYPE_2D;
        ici.format = fmt;
        ici.extent.width = extent.width;
        ici.extent.height = extent.height;
        ici.extent.depth = 1;
        ici.mipLevels = 1;
        ici.arrayLayers = 1;
        ici.samples = s;
        ici.tiling = VK_IMAGE_TILING_OPTIMAL;
        ici.usage = usage;
        ici.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
        ici.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (createImage(device, &ici, nullptr, &img) != VK_SUCCESS) return false;
        VkMemoryRequirements mr {};
        getImageMemReqs(device, img, &mr);
        const uint32_t ti = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT);
        if (ti == UINT32_MAX) return false;
        VkMemoryAllocateInfo mai {};
        mai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        mai.allocationSize = mr.size;
        mai.memoryTypeIndex = ti;
        if (allocateMemory(device, &mai, nullptr, &mem) != VK_SUCCESS) return false;
        if (bindImageMemory(device, img, mem, 0) != VK_SUCCESS) return false;
        VkImageViewCreateInfo vci {};
        vci.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
        vci.image = img;
        vci.viewType = VK_IMAGE_VIEW_TYPE_2D;
        vci.format = fmt;
        vci.subresourceRange.aspectMask = aspect;
        vci.subresourceRange.levelCount = 1;
        vci.subresourceRange.layerCount = 1;
        if (createImageView(device, &vci, nullptr, &view) != VK_SUCCESS) return false;
        return true;
    };

    bool ok = makeImage(format, samples, VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT,
                        VK_IMAGE_ASPECT_COLOR_BIT,
                        stencilMsaaColorImage, stencilMsaaColorMemory, stencilMsaaColorView);
    ok = ok && makeImage(dsFormat, samples, VK_IMAGE_USAGE_DEPTH_STENCIL_ATTACHMENT_BIT,
                        VK_IMAGE_ASPECT_DEPTH_BIT | VK_IMAGE_ASPECT_STENCIL_BIT,
                        stencilMsaaDsImage, stencilMsaaDsMemory, stencilMsaaDsView);
    ok = ok && makeImage(format, VK_SAMPLE_COUNT_1_BIT,
                        VK_IMAGE_USAGE_COLOR_ATTACHMENT_BIT | VK_IMAGE_USAGE_SAMPLED_BIT,
                        VK_IMAGE_ASPECT_COLOR_BIT,
                        stencilResolveImage, stencilResolveMemory, stencilResolveView);
    if (!ok) { DestroyStencilCoverResources(); return false; }

    {
        VkSamplerCreateInfo si {};
        si.sType = VK_STRUCTURE_TYPE_SAMPLER_CREATE_INFO;
        si.magFilter = VK_FILTER_NEAREST;   // resolve image is 1:1 with the screen
        si.minFilter = VK_FILTER_NEAREST;
        si.mipmapMode = VK_SAMPLER_MIPMAP_MODE_NEAREST;
        si.addressModeU = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.addressModeV = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.addressModeW = VK_SAMPLER_ADDRESS_MODE_CLAMP_TO_EDGE;
        si.maxLod = 0.0f;
        if (createSampler(device, &si, nullptr, &stencilResolveSampler) != VK_SUCCESS) {
            DestroyStencilCoverResources();
            return false;
        }
    }

    {
        VkAttachmentDescription atts[3] {};
        atts[0].format = format;                                  // MSAA color
        atts[0].samples = samples;
        atts[0].loadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;             // transparent
        atts[0].storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;       // only the resolve is kept
        atts[0].stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        atts[0].stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        atts[0].initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        atts[0].finalLayout = VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL;
        atts[1].format = dsFormat;                                // MSAA depth/stencil
        atts[1].samples = samples;
        atts[1].loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        atts[1].storeOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        atts[1].stencilLoadOp = VK_ATTACHMENT_LOAD_OP_CLEAR;      // stencil -> 0 at pass begin
        atts[1].stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        atts[1].initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        atts[1].finalLayout = VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL;
        atts[2].format = format;                                  // 1x resolve target
        atts[2].samples = VK_SAMPLE_COUNT_1_BIT;
        atts[2].loadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        atts[2].storeOp = VK_ATTACHMENT_STORE_OP_STORE;
        atts[2].stencilLoadOp = VK_ATTACHMENT_LOAD_OP_DONT_CARE;
        atts[2].stencilStoreOp = VK_ATTACHMENT_STORE_OP_DONT_CARE;
        atts[2].initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        atts[2].finalLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

        VkAttachmentReference colorRef { 0, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL };
        VkAttachmentReference dsRef { 1, VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL };
        VkAttachmentReference resolveRef { 2, VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL };

        VkSubpassDescription sub {};
        sub.pipelineBindPoint = VK_PIPELINE_BIND_POINT_GRAPHICS;
        sub.colorAttachmentCount = 1;
        sub.pColorAttachments = &colorRef;
        sub.pResolveAttachments = &resolveRef;
        sub.pDepthStencilAttachment = &dsRef;

        // dep0: the scratch (MSAA color/DS + resolve) is a single shared instance
        //       reused every frame, so this pass must wait for the PREVIOUS frame's
        //       use of it — both the composite's shader READ of the resolve image
        //       AND the prior color/depth-stencil WRITES (resolve + stencil). A
        //       subpass-EXTERNAL dependency's first scope spans earlier submissions
        //       on the queue, so covering those accesses here synchronizes across
        //       frames without per-frame duplicate scratch. (srcAccess must list
        //       the WRITEs, not just SHADER_READ, or WAW/RAW hazards slip through.)
        // dep1: this pass's resolve write becomes visible to the composite sampler.
        VkSubpassDependency deps[2] {};
        deps[0].srcSubpass = VK_SUBPASS_EXTERNAL;
        deps[0].dstSubpass = 0;
        deps[0].srcStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT
                             | VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT
                             | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT
                             | VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
        deps[0].srcAccessMask = VK_ACCESS_SHADER_READ_BIT
                              | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT
                              | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        deps[0].dstStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT
                             | VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT
                             | VK_PIPELINE_STAGE_LATE_FRAGMENT_TESTS_BIT;
        deps[0].dstAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        deps[1].srcSubpass = 0;
        deps[1].dstSubpass = VK_SUBPASS_EXTERNAL;
        deps[1].srcStageMask = VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        deps[1].srcAccessMask = VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        deps[1].dstStageMask = VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT;
        deps[1].dstAccessMask = VK_ACCESS_SHADER_READ_BIT;

        VkRenderPassCreateInfo rpi {};
        rpi.sType = VK_STRUCTURE_TYPE_RENDER_PASS_CREATE_INFO;
        rpi.attachmentCount = 3;
        rpi.pAttachments = atts;
        rpi.subpassCount = 1;
        rpi.pSubpasses = &sub;
        rpi.dependencyCount = 2;
        rpi.pDependencies = deps;
        if (createRenderPass(device, &rpi, nullptr, &stencilCoverRenderPass) != VK_SUCCESS) {
            DestroyStencilCoverResources();
            return false;
        }
    }

    {
        VkImageView fbViews[3] = { stencilMsaaColorView, stencilMsaaDsView, stencilResolveView };
        VkFramebufferCreateInfo fci {};
        fci.sType = VK_STRUCTURE_TYPE_FRAMEBUFFER_CREATE_INFO;
        fci.renderPass = stencilCoverRenderPass;
        fci.attachmentCount = 3;
        fci.pAttachments = fbViews;
        fci.width = extent.width;
        fci.height = extent.height;
        fci.layers = 1;
        if (createFramebuffer(device, &fci, nullptr, &stencilCoverFramebuffer) != VK_SUCCESS) {
            DestroyStencilCoverResources();
            return false;
        }
    }

    // 3 pipelines: stencil-fill (EvenOdd INVERT / NonZero INCR-DECR, no color)
    // + cover (NOT_EQUAL ref 0 -> write premult color, PassOp ZERO self-clears
    // the stencil so paths within one pass need no per-path clear). All reuse
    // the solid-fill VS/FS + engineBatchPipelineLayout (pixel-space MVP push).
    {
        VkShaderModule vs = VK_NULL_HANDLE, fs = VK_NULL_HANDLE;
        VkShaderModuleCreateInfo vmi {};
        vmi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        vmi.codeSize = kImpellerSolidFillVertShaderSpvSize;
        vmi.pCode = kImpellerSolidFillVertShaderSpv;
        if (createShaderModule(device, &vmi, nullptr, &vs) != VK_SUCCESS) {
            DestroyStencilCoverResources();
            return false;
        }
        VkShaderModuleCreateInfo fmi {};
        fmi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
        fmi.codeSize = kImpellerSolidFillFragShaderSpvSize;
        fmi.pCode = kImpellerSolidFillFragShaderSpv;
        if (createShaderModule(device, &fmi, nullptr, &fs) != VK_SUCCESS) {
            destroyShaderModule(device, vs, nullptr);
            DestroyStencilCoverResources();
            return false;
        }

        VkPipelineShaderStageCreateInfo stages[2] {};
        stages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        stages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
        stages[0].module = vs;
        stages[0].pName = "main";
        stages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
        stages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
        stages[1].module = fs;
        stages[1].pName = "main";

        VkVertexInputBindingDescription bind {};
        bind.binding = 0;
        bind.stride = sizeof(VkImpellerVertex);
        bind.inputRate = VK_VERTEX_INPUT_RATE_VERTEX;
        VkVertexInputAttributeDescription attrs[2] {};
        attrs[0].location = 0; attrs[0].binding = 0; attrs[0].format = VK_FORMAT_R32G32_SFLOAT;       attrs[0].offset = 0;
        attrs[1].location = 1; attrs[1].binding = 0; attrs[1].format = VK_FORMAT_R32G32B32A32_SFLOAT; attrs[1].offset = sizeof(float) * 2;
        VkPipelineVertexInputStateCreateInfo vin {};
        vin.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
        vin.vertexBindingDescriptionCount = 1;
        vin.pVertexBindingDescriptions = &bind;
        vin.vertexAttributeDescriptionCount = 2;
        vin.pVertexAttributeDescriptions = attrs;

        VkPipelineInputAssemblyStateCreateInfo ia {};
        ia.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
        ia.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

        VkPipelineViewportStateCreateInfo vps {};
        vps.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
        vps.viewportCount = 1;
        vps.scissorCount = 1;

        VkPipelineRasterizationStateCreateInfo rs {};
        rs.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
        rs.polygonMode = VK_POLYGON_MODE_FILL;
        rs.cullMode = VK_CULL_MODE_NONE;   // NonZero relies on front+back rasterizing
        rs.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
        rs.lineWidth = 1.0f;

        VkPipelineMultisampleStateCreateInfo ms {};
        ms.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
        ms.rasterizationSamples = samples;

        VkDynamicState dynStates[] = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
        VkPipelineDynamicStateCreateInfo dyns {};
        dyns.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
        dyns.dynamicStateCount = static_cast<uint32_t>(std::size(dynStates));
        dyns.pDynamicStates = dynStates;

        VkPipelineDepthStencilStateCreateInfo dsFillEO {};
        dsFillEO.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
        dsFillEO.stencilTestEnable = VK_TRUE;
        dsFillEO.front.failOp = VK_STENCIL_OP_KEEP;
        dsFillEO.front.passOp = VK_STENCIL_OP_INVERT;       // even-odd toggle
        dsFillEO.front.depthFailOp = VK_STENCIL_OP_KEEP;
        dsFillEO.front.compareOp = VK_COMPARE_OP_ALWAYS;
        dsFillEO.front.compareMask = 0xFF;
        dsFillEO.front.writeMask = 0xFF;
        dsFillEO.front.reference = 0;
        dsFillEO.back = dsFillEO.front;

        VkPipelineDepthStencilStateCreateInfo dsFillNZ = dsFillEO;
        dsFillNZ.front.passOp = VK_STENCIL_OP_INCREMENT_AND_CLAMP;  // winding +
        dsFillNZ.back = dsFillNZ.front;
        dsFillNZ.back.passOp = VK_STENCIL_OP_DECREMENT_AND_CLAMP;   // winding -

        VkPipelineDepthStencilStateCreateInfo dsCover {};
        dsCover.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;
        dsCover.stencilTestEnable = VK_TRUE;
        dsCover.front.failOp = VK_STENCIL_OP_KEEP;
        dsCover.front.passOp = VK_STENCIL_OP_ZERO;          // self-clear inside the shape
        dsCover.front.depthFailOp = VK_STENCIL_OP_KEEP;
        dsCover.front.compareOp = VK_COMPARE_OP_NOT_EQUAL;  // stencil != 0 -> inside
        dsCover.front.compareMask = 0xFF;
        dsCover.front.writeMask = 0xFF;
        dsCover.front.reference = 0;
        dsCover.back = dsCover.front;

        // Quad (type0) variant: no stencil, no depth — just MSAA color.
        VkPipelineDepthStencilStateCreateInfo dsDisabled {};
        dsDisabled.sType = VK_STRUCTURE_TYPE_PIPELINE_DEPTH_STENCIL_STATE_CREATE_INFO;

        VkPipelineColorBlendAttachmentState cbFill {};
        cbFill.blendEnable = VK_FALSE;
        cbFill.colorWriteMask = 0;  // stencil pass writes no color
        VkPipelineColorBlendStateCreateInfo cbFillInfo {};
        cbFillInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        cbFillInfo.attachmentCount = 1;
        cbFillInfo.pAttachments = &cbFill;

        VkPipelineColorBlendAttachmentState cbCover {};
        cbCover.blendEnable = VK_TRUE;
        cbCover.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
        cbCover.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        cbCover.colorBlendOp = VK_BLEND_OP_ADD;
        cbCover.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
        cbCover.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
        cbCover.alphaBlendOp = VK_BLEND_OP_ADD;
        cbCover.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT
                               | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
        VkPipelineColorBlendStateCreateInfo cbCoverInfo {};
        cbCoverInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
        cbCoverInfo.attachmentCount = 1;
        cbCoverInfo.pAttachments = &cbCover;

        VkGraphicsPipelineCreateInfo base {};
        base.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
        base.stageCount = 2;
        base.pStages = stages;
        base.pVertexInputState = &vin;
        base.pInputAssemblyState = &ia;
        base.pViewportState = &vps;
        base.pRasterizationState = &rs;
        base.pMultisampleState = &ms;
        base.pDynamicState = &dyns;
        base.layout = engineBatchPipelineLayout;
        base.renderPass = stencilCoverRenderPass;
        base.subpass = 0;

        // [0]=quad (type0, no stencil, premult SrcOver), [1]=fill EvenOdd,
        // [2]=fill NonZero, [3]=cover.
        VkGraphicsPipelineCreateInfo infos[4] = { base, base, base, base };
        infos[0].pDepthStencilState = &dsDisabled; infos[0].pColorBlendState = &cbCoverInfo;
        infos[1].pDepthStencilState = &dsFillEO;   infos[1].pColorBlendState = &cbFillInfo;
        infos[2].pDepthStencilState = &dsFillNZ;   infos[2].pColorBlendState = &cbFillInfo;
        infos[3].pDepthStencilState = &dsCover;    infos[3].pColorBlendState = &cbCoverInfo;

        VkPipeline pipes[4] = { VK_NULL_HANDLE, VK_NULL_HANDLE, VK_NULL_HANDLE, VK_NULL_HANDLE };
        const VkResult pr = createGraphicsPipelines(device, VK_NULL_HANDLE, 4, infos, nullptr, pipes);
        destroyShaderModule(device, fs, nullptr);
        destroyShaderModule(device, vs, nullptr);
        if (pr != VK_SUCCESS) {
            DestroyStencilCoverResources();
            return false;
        }
        psoStencilQuad        = pipes[0];
        psoStencilFillEvenOdd = pipes[1];
        psoStencilFillNonZero = pipes[2];
        psoStencilCover       = pipes[3];
    }

    stencilCoverW = extent.width;
    stencilCoverH = extent.height;
    stencilSampleCount = samples;
    stencilDsFormat = dsFormat;
    return true;
}

void VulkanRenderTarget::Impl::RenderEngineBatches(
    VkCommandBuffer cmd,
    const std::vector<VkImpellerDrawBatch>& batches,
    VkExtent2D extent,
    uint32_t frameIdx,
    VkRenderPass renderPass,
    VkFramebuffer framebuffer)
{
    if (batches.empty() || cmd == VK_NULL_HANDLE) return;
    if (!EnsureEngineBatchPipeline()) return;

    // ── GPU stencil-then-cover path (JALIUM_VK_STENCIL_PATH) ────────────────
    // When any solid fill/stroke arrived as a stencil batch (pipelineType==1),
    // render the WHOLE engine-batch list (type0 indexed quads + type1 stencil
    // paths, in painter order) into an offscreen N× MSAA color+stencil target,
    // then resolve 1× and composite onto the swap chain (premult SrcOver) in a
    // single pass. Routing type0 through the same MSAA target keeps painter
    // order with ONE composite — CompositeVelloOutput resets its descriptor pool
    // per call, so it must run exactly once — and gives gradients/ellipses MSAA
    // too. With no stencil batch (gate off, or gradient-only frame) we fall
    // through to the original direct-to-swap-chain path below, byte-unchanged.
    bool hasStencil = false;
    if (VkStencilPathEnabled()) {
        for (const auto& b : batches) {
            if (b.pipelineType == 1 && !b.stencilContours.empty()) { hasStencil = true; break; }
        }
    }
    if (hasStencil && EnsureStencilCoverResources(extent)) {
        struct DrawRec {
            int type = 0;                       // 0=quad (indexed), 1=stencil path
            const VkImpellerDrawBatch* src = nullptr;
            VkDeviceSize vtxOffset = 0, idxOffset = 0;
            uint32_t idxCount = 0;
            std::vector<VkImpellerVertex> fill; // type1 fan triangle list
            VkImpellerVertex cover[6] {};       // type1 bbox cover quad
            VkDeviceSize fillOffset = 0, coverOffset = 0;
            uint32_t fillCount = 0;
            FillRule fillRule = FillRule::EvenOdd;
        };
        std::vector<DrawRec> recs;
        recs.reserve(batches.size());
        VkDeviceSize totalBytes = 0;
        for (const auto& b : batches) {
            if (b.pipelineType == 1) {
                if (b.stencilContours.empty()) continue;
                DrawRec rec;
                rec.type = 1;
                rec.src = &b;
                rec.fillRule = b.stencilFillRule;
                float minX = 1e30f, minY = 1e30f, maxX = -1e30f, maxY = -1e30f;
                for (const auto& c : b.stencilContours) {
                    const uint32_t pc = c.VertexCount();
                    if (pc < 3) continue;
                    for (uint32_t k = 1; k + 1 < pc; ++k) {   // fan hub = vertex 0
                        rec.fill.push_back({ c.X(0),     c.Y(0),     0, 0, 0, 0 });
                        rec.fill.push_back({ c.X(k),     c.Y(k),     0, 0, 0, 0 });
                        rec.fill.push_back({ c.X(k + 1), c.Y(k + 1), 0, 0, 0, 0 });
                    }
                    for (uint32_t k = 0; k < pc; ++k) {
                        const float px = c.X(k), py = c.Y(k);
                        if (px < minX) minX = px;
                        if (py < minY) minY = py;
                        if (px > maxX) maxX = px;
                        if (py > maxY) maxY = py;
                    }
                }
                if (rec.fill.empty() || !(maxX > minX) || !(maxY > minY)) continue;
                const float r = b.stencilR, g = b.stencilG, bl = b.stencilB, a = b.stencilA;
                rec.cover[0] = { minX, minY, r, g, bl, a };
                rec.cover[1] = { maxX, minY, r, g, bl, a };
                rec.cover[2] = { maxX, maxY, r, g, bl, a };
                rec.cover[3] = { minX, minY, r, g, bl, a };
                rec.cover[4] = { maxX, maxY, r, g, bl, a };
                rec.cover[5] = { minX, maxY, r, g, bl, a };
                rec.fillCount = static_cast<uint32_t>(rec.fill.size());
                totalBytes += static_cast<VkDeviceSize>(rec.fill.size()) * sizeof(VkImpellerVertex)
                            + sizeof(rec.cover);
                recs.push_back(std::move(rec));
            } else {
                if (b.indices.empty()) continue;
                DrawRec rec;
                rec.type = 0;
                rec.src = &b;
                rec.idxCount = static_cast<uint32_t>(b.indices.size());
                totalBytes += static_cast<VkDeviceSize>(b.vertices.size()) * sizeof(VkImpellerVertex)
                            + static_cast<VkDeviceSize>(b.indices.size()) * sizeof(uint32_t);
                recs.push_back(std::move(rec));
            }
        }
        if (recs.empty() || totalBytes == 0) return;
        if (!EnsureEngineBatchBuffer(frameIdx, totalBytes)) return;

        PerFrameState& frame = perFrameStates_[frameIdx];
        uint8_t* mapped = static_cast<uint8_t*>(frame.engineBatchMapped);
        VkDeviceSize cursor = 0;
        for (auto& rec : recs) {
            if (rec.type == 0) {
                const auto& b = *rec.src;
                const VkDeviceSize vB = b.vertices.size() * sizeof(VkImpellerVertex);
                const VkDeviceSize iB = b.indices.size() * sizeof(uint32_t);
                std::memcpy(mapped + cursor, b.vertices.data(), vB);
                rec.vtxOffset = cursor;
                cursor += vB;
                std::memcpy(mapped + cursor, b.indices.data(), iB);
                rec.idxOffset = cursor;
                cursor += iB;
            } else {
                const VkDeviceSize fB = rec.fill.size() * sizeof(VkImpellerVertex);
                std::memcpy(mapped + cursor, rec.fill.data(), fB);
                rec.fillOffset = cursor;
                cursor += fB;
                std::memcpy(mapped + cursor, rec.cover, sizeof(rec.cover));
                rec.coverOffset = cursor;
                cursor += sizeof(rec.cover);
            }
        }

        auto cmdBeginRenderPass   = LoadDeviceProc<PFN_vkCmdBeginRenderPass>(getDeviceProcAddr, device, "vkCmdBeginRenderPass");
        auto cmdEndRenderPass     = LoadDeviceProc<PFN_vkCmdEndRenderPass>(getDeviceProcAddr, device, "vkCmdEndRenderPass");
        auto cmdBindPipeline      = LoadDeviceProc<PFN_vkCmdBindPipeline>(getDeviceProcAddr, device, "vkCmdBindPipeline");
        auto cmdSetViewport       = LoadDeviceProc<PFN_vkCmdSetViewport>(getDeviceProcAddr, device, "vkCmdSetViewport");
        auto cmdSetScissor        = LoadDeviceProc<PFN_vkCmdSetScissor>(getDeviceProcAddr, device, "vkCmdSetScissor");
        auto cmdPushConstants     = LoadDeviceProc<PFN_vkCmdPushConstants>(getDeviceProcAddr, device, "vkCmdPushConstants");
        auto cmdBindVertexBuffers = LoadDeviceProc<PFN_vkCmdBindVertexBuffers>(getDeviceProcAddr, device, "vkCmdBindVertexBuffers");
        auto cmdBindIndexBuffer   = LoadDeviceProc<PFN_vkCmdBindIndexBuffer>(getDeviceProcAddr, device, "vkCmdBindIndexBuffer");
        auto cmdDrawIndexed       = LoadDeviceProc<PFN_vkCmdDrawIndexed>(getDeviceProcAddr, device, "vkCmdDrawIndexed");
        auto cmdDraw              = LoadDeviceProc<PFN_vkCmdDraw>(getDeviceProcAddr, device, "vkCmdDraw");
        if (!cmdBeginRenderPass || !cmdEndRenderPass || !cmdBindPipeline || !cmdSetViewport
            || !cmdSetScissor || !cmdPushConstants || !cmdBindVertexBuffers
            || !cmdBindIndexBuffer || !cmdDrawIndexed || !cmdDraw) {
            return;
        }

        // Pixel→clip MVP + negative-height viewport — identical to the direct
        // path, so the offscreen MSAA image is screen-oriented and the 1:1
        // composite needs no Y flip.
        const float w = std::max(1.0f, static_cast<float>(extent.width));
        const float h = std::max(1.0f, static_cast<float>(extent.height));
        float mvp[16] = {
            2.0f / w, 0,         0, 0,
            0,        -2.0f / h, 0, 0,
            0,        0,         1, 0,
            -1.0f,    1.0f,      0, 1
        };
        VkViewport viewport {};
        viewport.width = static_cast<float>(extent.width);
        viewport.y = static_cast<float>(extent.height);
        viewport.height = -static_cast<float>(extent.height);
        viewport.maxDepth = 1.0f;

        auto scissorOf = [&](const VkImpellerDrawBatch& src) -> VkRect2D {
            VkRect2D s {};
            if (src.hasScissor) {
                const int32_t l = static_cast<int32_t>(std::max(0.0f, src.scissorL));
                const int32_t t = static_cast<int32_t>(std::max(0.0f, src.scissorT));
                const int32_t r = static_cast<int32_t>(std::max(0.0f, src.scissorR));
                const int32_t b = static_cast<int32_t>(std::max(0.0f, src.scissorB));
                s.offset.x = l;
                s.offset.y = t;
                s.extent.width = static_cast<uint32_t>(std::max(0, r - l));
                s.extent.height = static_cast<uint32_t>(std::max(0, b - t));
            } else {
                s.extent = extent;
            }
            return s;
        };

        VkClearValue clears[3] {};
        clears[0].color = { { 0.0f, 0.0f, 0.0f, 0.0f } };
        clears[1].depthStencil = { 1.0f, 0 };
        VkRenderPassBeginInfo rp {};
        rp.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        rp.renderPass = stencilCoverRenderPass;
        rp.framebuffer = stencilCoverFramebuffer;
        rp.renderArea.extent = extent;
        rp.clearValueCount = 3;
        rp.pClearValues = clears;
        cmdBeginRenderPass(cmd, &rp, VK_SUBPASS_CONTENTS_INLINE);
        cmdSetViewport(cmd, 0, 1, &viewport);
        cmdBindIndexBuffer(cmd, frame.engineBatchBuffer, 0, VK_INDEX_TYPE_UINT32);

        for (const auto& rec : recs) {
            const VkRect2D scissor = scissorOf(*rec.src);
            cmdSetScissor(cmd, 0, 1, &scissor);
            cmdPushConstants(cmd, engineBatchPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(mvp), mvp);
            if (rec.type == 0) {
                cmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, psoStencilQuad);
                cmdBindVertexBuffers(cmd, 0, 1, &frame.engineBatchBuffer, &rec.vtxOffset);
                cmdDrawIndexed(cmd, rec.idxCount, 1,
                               static_cast<uint32_t>(rec.idxOffset / sizeof(uint32_t)), 0, 0);
            } else {
                // Pass 1: write stencil only (no color).
                cmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS,
                                rec.fillRule == FillRule::NonZero ? psoStencilFillNonZero
                                                                  : psoStencilFillEvenOdd);
                cmdBindVertexBuffers(cmd, 0, 1, &frame.engineBatchBuffer, &rec.fillOffset);
                cmdDraw(cmd, rec.fillCount, 1, 0, 0);
                // Pass 2: cover — stencil!=0 ⇒ write premult color + self-clear.
                cmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, psoStencilCover);
                cmdBindVertexBuffers(cmd, 0, 1, &frame.engineBatchBuffer, &rec.coverOffset);
                cmdDraw(cmd, 6, 1, 0, 0);
            }
        }
        cmdEndRenderPass(cmd);

        // Resolve image is now SHADER_READ_ONLY (render-pass final layout);
        // composite it onto the swap chain (premult SrcOver, fullscreen, 1:1).
        CompositeVelloOutput(cmd, stencilResolveView, stencilResolveSampler,
                             extent, frameIdx, renderPass, framebuffer);
        return;
    }

    // Pack all batches into a single host-visible buffer: vertices first
    // (sequential per batch), then indices. Per-batch offsets recorded so
    // we can issue one bind+draw per batch.
    struct BatchOffsets {
        VkDeviceSize vertexByteOffset;
        VkDeviceSize indexByteOffset;
        uint32_t indexCount;
    };
    std::vector<BatchOffsets> offsets;
    offsets.reserve(batches.size());

    VkDeviceSize totalVertexBytes = 0;
    VkDeviceSize totalIndexBytes = 0;
    for (const auto& batch : batches) {
        if (batch.indices.empty()) continue;
        totalVertexBytes += batch.vertices.size() * sizeof(VkImpellerVertex);
        totalIndexBytes += batch.indices.size() * sizeof(uint32_t);
    }
    if (totalVertexBytes == 0 || totalIndexBytes == 0) return;

    const VkDeviceSize totalBytes = totalVertexBytes + totalIndexBytes;
    if (!EnsureEngineBatchBuffer(frameIdx, totalBytes)) return;

    PerFrameState& frame = perFrameStates_[frameIdx];
    uint8_t* mapped = static_cast<uint8_t*>(frame.engineBatchMapped);
    VkDeviceSize vertexCursor = 0;
    VkDeviceSize indexCursor = totalVertexBytes;
    for (const auto& batch : batches) {
        if (batch.indices.empty()) continue;
        const VkDeviceSize vBytes = batch.vertices.size() * sizeof(VkImpellerVertex);
        const VkDeviceSize iBytes = batch.indices.size() * sizeof(uint32_t);
        std::memcpy(mapped + vertexCursor, batch.vertices.data(), vBytes);
        std::memcpy(mapped + indexCursor, batch.indices.data(), iBytes);
        offsets.push_back({ vertexCursor, indexCursor, static_cast<uint32_t>(batch.indices.size()) });
        vertexCursor += vBytes;
        indexCursor += iBytes;
    }

    // Begin a load-op render pass on the swap-chain image so we paint over
    // whatever the GPU-replay path already drew this frame.
    auto cmdBeginRenderPass = LoadDeviceProc<PFN_vkCmdBeginRenderPass>(getDeviceProcAddr, device, "vkCmdBeginRenderPass");
    auto cmdEndRenderPass = LoadDeviceProc<PFN_vkCmdEndRenderPass>(getDeviceProcAddr, device, "vkCmdEndRenderPass");
    auto cmdBindPipeline = LoadDeviceProc<PFN_vkCmdBindPipeline>(getDeviceProcAddr, device, "vkCmdBindPipeline");
    auto cmdSetViewport = LoadDeviceProc<PFN_vkCmdSetViewport>(getDeviceProcAddr, device, "vkCmdSetViewport");
    auto cmdSetScissor = LoadDeviceProc<PFN_vkCmdSetScissor>(getDeviceProcAddr, device, "vkCmdSetScissor");
    auto cmdPushConstants = LoadDeviceProc<PFN_vkCmdPushConstants>(getDeviceProcAddr, device, "vkCmdPushConstants");
    auto cmdBindVertexBuffers = LoadDeviceProc<PFN_vkCmdBindVertexBuffers>(getDeviceProcAddr, device, "vkCmdBindVertexBuffers");
    auto cmdBindIndexBuffer = LoadDeviceProc<PFN_vkCmdBindIndexBuffer>(getDeviceProcAddr, device, "vkCmdBindIndexBuffer");
    auto cmdDrawIndexed = LoadDeviceProc<PFN_vkCmdDrawIndexed>(getDeviceProcAddr, device, "vkCmdDrawIndexed");
    if (!cmdBeginRenderPass || !cmdEndRenderPass || !cmdBindPipeline || !cmdSetViewport
        || !cmdSetScissor || !cmdPushConstants || !cmdBindVertexBuffers
        || !cmdBindIndexBuffer || !cmdDrawIndexed) {
        return;
    }

    VkClearValue dummyClear {};
    VkRenderPassBeginInfo rpBegin {};
    rpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rpBegin.renderPass = renderPass;
    rpBegin.framebuffer = framebuffer;
    rpBegin.renderArea.extent = extent;
    rpBegin.clearValueCount = 1;
    rpBegin.pClearValues = &dummyClear;
    cmdBeginRenderPass(cmd, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);

    cmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, engineBatchPipeline);

    // Negative-height viewport flips Y to match the main GPU-replay pass
    // (the SolidRect / bitmap pipelines set the same flipped viewport at the
    // frame level). The engine vertex MVP already maps pixel→clip with a
    // 1 - 2y/h flip; WITHOUT the flipped viewport here the engine's filled
    // paths (PathIcons, SuperEllipse Border backgrounds, gradient geometry,
    // any FillPath/FillPolygon content) rendered upside-down in Y — e.g. a
    // tile whose geometry sits at y=633 painted at viewportH-633, which is why
    // SuperEllipse specimen tiles appeared shifted to the top of their rail.
    VkViewport viewport {};
    viewport.width = static_cast<float>(extent.width);
    viewport.y = static_cast<float>(extent.height);
    viewport.height = -static_cast<float>(extent.height);
    viewport.maxDepth = 1.0f;
    cmdSetViewport(cmd, 0, 1, &viewport);

    // Push a screen-pixel-to-clip-space MVP. Engine-emitted vertices live in
    // pixel coordinates already, so the transform is just the standard
    // ortho mapping (0,0) → top-left, (W,H) → bottom-right.
    const float w = std::max(1.0f, static_cast<float>(extent.width));
    const float h = std::max(1.0f, static_cast<float>(extent.height));
    float mvp[16] = {
        2.0f / w,      0,             0, 0,
        0,            -2.0f / h,      0, 0,
        0,             0,             1, 0,
        -1.0f,         1.0f,          0, 1
    };

    cmdBindIndexBuffer(cmd, frame.engineBatchBuffer, 0, VK_INDEX_TYPE_UINT32);

    for (const auto& batch : offsets) {
        // Per-batch scissor: engine emits screen-space scissor when the user
        // pushed a clip; otherwise the full viewport.
        const auto& source = batches[&batch - offsets.data()];
        VkRect2D scissor {};
        if (source.hasScissor) {
            const int32_t left = static_cast<int32_t>(std::max(0.0f, source.scissorL));
            const int32_t top = static_cast<int32_t>(std::max(0.0f, source.scissorT));
            const int32_t right = static_cast<int32_t>(std::max(0.0f, source.scissorR));
            const int32_t bottom = static_cast<int32_t>(std::max(0.0f, source.scissorB));
            scissor.offset.x = left;
            scissor.offset.y = top;
            scissor.extent.width = static_cast<uint32_t>(std::max(0, right - left));
            scissor.extent.height = static_cast<uint32_t>(std::max(0, bottom - top));
        } else {
            scissor.extent = extent;
        }
        cmdSetScissor(cmd, 0, 1, &scissor);

        cmdPushConstants(cmd, engineBatchPipelineLayout,
                         VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(mvp), mvp);
        const VkDeviceSize vertexOffset = batch.vertexByteOffset;
        cmdBindVertexBuffers(cmd, 0, 1, &frame.engineBatchBuffer, &vertexOffset);
        // Index buffer was bound at offset 0; pass per-batch firstIndex
        // computed from the byte offset relative to that.
        const uint32_t firstIndex = static_cast<uint32_t>(
            (batch.indexByteOffset) / sizeof(uint32_t));
        cmdDrawIndexed(cmd, batch.indexCount, 1, firstIndex, 0, 0);
    }

    cmdEndRenderPass(cmd);
}

// ---------------------------------------------------------------------------
// Vello GPU-compute output composite
//
// The Vello compute graph (VelloComputePipeline) writes a viewport-sized
// RGBA32F premultiplied storage image. EnsureVelloCompositePipeline builds a
// fullscreen-triangle graphics pipeline that samples it (descriptor set
// {0: sampled image, 1: sampler}) and CompositeVelloOutput draws it over the
// swap-chain frame with premultiplied SrcOver blend — the analogue of the
// D3D12 FlushVelloPaths AddBitmap composite.
// ---------------------------------------------------------------------------
bool VulkanRenderTarget::Impl::EnsureVelloCompositePipeline()
{
    if (velloCompositePipeline != VK_NULL_HANDLE) return true;
    if (device == VK_NULL_HANDLE || frameRenderPass == VK_NULL_HANDLE) return false;

    auto createDescriptorSetLayout = LoadDeviceProc<PFN_vkCreateDescriptorSetLayout>(getDeviceProcAddr, device, "vkCreateDescriptorSetLayout");
    auto createPipelineLayout = LoadDeviceProc<PFN_vkCreatePipelineLayout>(getDeviceProcAddr, device, "vkCreatePipelineLayout");
    auto createShaderModule = LoadDeviceProc<PFN_vkCreateShaderModule>(getDeviceProcAddr, device, "vkCreateShaderModule");
    auto destroyShaderModule = LoadDeviceProc<PFN_vkDestroyShaderModule>(getDeviceProcAddr, device, "vkDestroyShaderModule");
    auto createGraphicsPipelines = LoadDeviceProc<PFN_vkCreateGraphicsPipelines>(getDeviceProcAddr, device, "vkCreateGraphicsPipelines");
    auto createDescriptorPool = LoadDeviceProc<PFN_vkCreateDescriptorPool>(getDeviceProcAddr, device, "vkCreateDescriptorPool");
    if (!createDescriptorSetLayout || !createPipelineLayout || !createShaderModule
        || !destroyShaderModule || !createGraphicsPipelines || !createDescriptorPool) {
        return false;
    }

    if (velloCompositeDescSetLayout == VK_NULL_HANDLE) {
        VkDescriptorSetLayoutBinding binds[2] {};
        binds[0].binding = 0;
        binds[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
        binds[0].descriptorCount = 1;
        binds[0].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        binds[1].binding = 1;
        binds[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
        binds[1].descriptorCount = 1;
        binds[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
        VkDescriptorSetLayoutCreateInfo li {};
        li.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
        li.bindingCount = 2;
        li.pBindings = binds;
        if (createDescriptorSetLayout(device, &li, nullptr, &velloCompositeDescSetLayout) != VK_SUCCESS) {
            return false;
        }
    }
    if (velloCompositePipelineLayout == VK_NULL_HANDLE) {
        VkPipelineLayoutCreateInfo pli {};
        pli.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
        pli.setLayoutCount = 1;
        pli.pSetLayouts = &velloCompositeDescSetLayout;
        if (createPipelineLayout(device, &pli, nullptr, &velloCompositePipelineLayout) != VK_SUCCESS) {
            return false;
        }
    }
    for (uint32_t f = 0; f < MAX_FRAMES_IN_FLIGHT; ++f) {
        if (velloCompositeDescPools[f] != VK_NULL_HANDLE) continue;
        VkDescriptorPoolSize sizes[2] = {
            { VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE, 2 },
            { VK_DESCRIPTOR_TYPE_SAMPLER, 2 },
        };
        VkDescriptorPoolCreateInfo pi {};
        pi.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
        pi.maxSets = 2;
        pi.poolSizeCount = 2;
        pi.pPoolSizes = sizes;
        if (createDescriptorPool(device, &pi, nullptr, &velloCompositeDescPools[f]) != VK_SUCCESS) {
            return false;
        }
    }

    VkShaderModule vs = VK_NULL_HANDLE, fs = VK_NULL_HANDLE;
    VkShaderModuleCreateInfo vsi {};
    vsi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    vsi.codeSize = kVelloCompositeVertSpvSize;
    vsi.pCode = kVelloCompositeVertSpv;
    if (createShaderModule(device, &vsi, nullptr, &vs) != VK_SUCCESS) return false;
    VkShaderModuleCreateInfo fsi {};
    fsi.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    fsi.codeSize = kVelloCompositeFragSpvSize;
    fsi.pCode = kVelloCompositeFragSpv;
    if (createShaderModule(device, &fsi, nullptr, &fs) != VK_SUCCESS) {
        destroyShaderModule(device, vs, nullptr);
        return false;
    }

    VkPipelineShaderStageCreateInfo stages[2] {};
    stages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
    stages[0].module = vs;
    stages[0].pName = "main";
    stages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    stages[1].module = fs;
    stages[1].pName = "main";

    VkPipelineVertexInputStateCreateInfo vertexInput {};
    vertexInput.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;

    VkPipelineInputAssemblyStateCreateInfo inputAssembly {};
    inputAssembly.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    inputAssembly.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;

    VkPipelineViewportStateCreateInfo viewportState {};
    viewportState.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    viewportState.viewportCount = 1;
    viewportState.scissorCount = 1;

    VkPipelineRasterizationStateCreateInfo raster {};
    raster.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    raster.polygonMode = VK_POLYGON_MODE_FILL;
    raster.cullMode = VK_CULL_MODE_NONE;
    raster.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    raster.lineWidth = 1.0f;

    VkPipelineMultisampleStateCreateInfo multisample {};
    multisample.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    multisample.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;

    VkPipelineColorBlendAttachmentState blendAttachment {};
    blendAttachment.blendEnable = VK_TRUE;
    blendAttachment.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
    blendAttachment.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    blendAttachment.colorBlendOp = VK_BLEND_OP_ADD;
    blendAttachment.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
    blendAttachment.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    blendAttachment.alphaBlendOp = VK_BLEND_OP_ADD;
    blendAttachment.colorWriteMask =
        VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT | VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    VkPipelineColorBlendStateCreateInfo colorBlend {};
    colorBlend.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    colorBlend.attachmentCount = 1;
    colorBlend.pAttachments = &blendAttachment;

    VkDynamicState dynamicStates[] = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynamic {};
    dynamic.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynamic.dynamicStateCount = static_cast<uint32_t>(std::size(dynamicStates));
    dynamic.pDynamicStates = dynamicStates;

    VkGraphicsPipelineCreateInfo pipelineInfo {};
    pipelineInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pipelineInfo.stageCount = 2;
    pipelineInfo.pStages = stages;
    pipelineInfo.pVertexInputState = &vertexInput;
    pipelineInfo.pInputAssemblyState = &inputAssembly;
    pipelineInfo.pViewportState = &viewportState;
    pipelineInfo.pRasterizationState = &raster;
    pipelineInfo.pMultisampleState = &multisample;
    pipelineInfo.pColorBlendState = &colorBlend;
    pipelineInfo.pDynamicState = &dynamic;
    pipelineInfo.layout = velloCompositePipelineLayout;
    pipelineInfo.renderPass = frameRenderPass;
    pipelineInfo.subpass = 0;

    const VkResult result = createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &velloCompositePipeline);
    destroyShaderModule(device, fs, nullptr);
    destroyShaderModule(device, vs, nullptr);
    return result == VK_SUCCESS && velloCompositePipeline != VK_NULL_HANDLE;
}

void VulkanRenderTarget::Impl::CompositeVelloOutput(
    VkCommandBuffer cmd, VkImageView outputView, VkSampler sampler,
    VkExtent2D extent, uint32_t frameIdx,
    VkRenderPass renderPass, VkFramebuffer framebuffer)
{
    if (cmd == VK_NULL_HANDLE || outputView == VK_NULL_HANDLE || sampler == VK_NULL_HANDLE) return;
    if (frameIdx >= MAX_FRAMES_IN_FLIGHT) return;
    if (!EnsureVelloCompositePipeline()) return;

    auto resetDescriptorPool = LoadDeviceProc<PFN_vkResetDescriptorPool>(getDeviceProcAddr, device, "vkResetDescriptorPool");
    auto allocateDescriptorSets = LoadDeviceProc<PFN_vkAllocateDescriptorSets>(getDeviceProcAddr, device, "vkAllocateDescriptorSets");
    auto updateDescriptorSets = LoadDeviceProc<PFN_vkUpdateDescriptorSets>(getDeviceProcAddr, device, "vkUpdateDescriptorSets");
    auto cmdBeginRenderPass = LoadDeviceProc<PFN_vkCmdBeginRenderPass>(getDeviceProcAddr, device, "vkCmdBeginRenderPass");
    auto cmdEndRenderPass = LoadDeviceProc<PFN_vkCmdEndRenderPass>(getDeviceProcAddr, device, "vkCmdEndRenderPass");
    auto cmdBindPipeline = LoadDeviceProc<PFN_vkCmdBindPipeline>(getDeviceProcAddr, device, "vkCmdBindPipeline");
    auto cmdSetViewport = LoadDeviceProc<PFN_vkCmdSetViewport>(getDeviceProcAddr, device, "vkCmdSetViewport");
    auto cmdSetScissor = LoadDeviceProc<PFN_vkCmdSetScissor>(getDeviceProcAddr, device, "vkCmdSetScissor");
    auto cmdBindDescriptorSets = LoadDeviceProc<PFN_vkCmdBindDescriptorSets>(getDeviceProcAddr, device, "vkCmdBindDescriptorSets");
    auto cmdDraw = LoadDeviceProc<PFN_vkCmdDraw>(getDeviceProcAddr, device, "vkCmdDraw");
    if (!resetDescriptorPool || !allocateDescriptorSets || !updateDescriptorSets
        || !cmdBeginRenderPass || !cmdEndRenderPass || !cmdBindPipeline
        || !cmdSetViewport || !cmdSetScissor || !cmdBindDescriptorSets || !cmdDraw) {
        return;
    }

    resetDescriptorPool(device, velloCompositeDescPools[frameIdx], 0);
    VkDescriptorSet set = VK_NULL_HANDLE;
    VkDescriptorSetAllocateInfo ai {};
    ai.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
    ai.descriptorPool = velloCompositeDescPools[frameIdx];
    ai.descriptorSetCount = 1;
    ai.pSetLayouts = &velloCompositeDescSetLayout;
    if (allocateDescriptorSets(device, &ai, &set) != VK_SUCCESS) return;

    VkDescriptorImageInfo imgInfo {};
    imgInfo.imageView = outputView;
    imgInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
    VkDescriptorImageInfo smpInfo {};
    smpInfo.sampler = sampler;
    VkWriteDescriptorSet writes[2] {};
    writes[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[0].dstSet = set;
    writes[0].dstBinding = 0;
    writes[0].descriptorCount = 1;
    writes[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    writes[0].pImageInfo = &imgInfo;
    writes[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
    writes[1].dstSet = set;
    writes[1].dstBinding = 1;
    writes[1].descriptorCount = 1;
    writes[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
    writes[1].pImageInfo = &smpInfo;
    updateDescriptorSets(device, 2, writes, 0, nullptr);

    VkClearValue dummyClear {};
    VkRenderPassBeginInfo rpBegin {};
    rpBegin.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
    rpBegin.renderPass = renderPass;
    rpBegin.framebuffer = framebuffer;
    rpBegin.renderArea.extent = extent;
    rpBegin.clearValueCount = 1;
    rpBegin.pClearValues = &dummyClear;
    cmdBeginRenderPass(cmd, &rpBegin, VK_SUBPASS_CONTENTS_INLINE);
    cmdBindPipeline(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, velloCompositePipeline);
    VkViewport vp {};
    vp.width = static_cast<float>(extent.width);
    vp.height = static_cast<float>(extent.height);
    vp.maxDepth = 1.0f;
    cmdSetViewport(cmd, 0, 1, &vp);
    VkRect2D sc {};
    sc.extent = extent;
    cmdSetScissor(cmd, 0, 1, &sc);
    cmdBindDescriptorSets(cmd, VK_PIPELINE_BIND_POINT_GRAPHICS, velloCompositePipelineLayout, 0, 1, &set, 0, nullptr);
    cmdDraw(cmd, 3, 1, 0, 0);
    cmdEndRenderPass(cmd);
}

// ── Custom pixel-shader effect (DrawShaderEffectFromSource) ─────────────────
// Framework vertex shader: a 6-vertex fullscreen quad positioned over the
// element rect, emitting element-local UV scaled into the captured content's
// sub-region of the upload image. Authored for DXC (Vulkan-only) — uses the
// [[vk::push_constant]] attribute, which FXC would reject but DXC accepts.
static const char* kCustomShaderVsHlsl = R"__HLSL__(
struct Geom { float4 rect; float2 screenSize; float2 uvScale; };
[[vk::push_constant]] Geom geom;

struct VsOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

VsOut main(uint vid : SV_VertexID)
{
    float2 corners[6] = {
        float2(0,0), float2(1,0), float2(1,1),
        float2(0,0), float2(1,1), float2(0,1)
    };
    float2 c  = corners[vid];
    float2 px = geom.rect.xy + c * geom.rect.zw;
    // Vulkan framebuffer Y increases downward — no Y flip.
    float2 ndc = (px / geom.screenSize) * 2.0f - 1.0f;
    VsOut o;
    o.pos = float4(ndc, 0.0f, 1.0f);
    o.uv  = c * geom.uvScale;
    return o;
}
)__HLSL__";

bool VulkanRenderTarget::Impl::EnsureCustomShaderBase()
{
    if (customShaderBaseReady) return true;
    if (customShaderBaseAttempted) return false;
    customShaderBaseAttempted = true;

    if (!customShaderCompiler.Available()) {
        return false;  // no DXC → caller composites captured content unmodified
    }

    std::string err;
    customShaderVsSpirv = customShaderCompiler.Compile(kCustomShaderVsHlsl, "main", "vs_6_0", err);
    if (customShaderVsSpirv.empty()) {
        VK_LOG("[Vulkan] custom shader VS compile failed: %s", err.c_str());
        return false;
    }
    VkShaderModuleCreateInfo vsInfo{};
    vsInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    vsInfo.codeSize = customShaderVsSpirv.size() * sizeof(uint32_t);
    vsInfo.pCode = customShaderVsSpirv.data();
    if (createShaderModule(device, &vsInfo, nullptr, &customShaderVs) != VK_SUCCESS) return false;

    // Descriptor layout: b0 UBO (user constants, FS), t0 sampled image (FS),
    // s0 sampler (FS) — bindings follow the compiler's register→binding shifts.
    VkDescriptorSetLayoutBinding binds[3]{};
    binds[0].binding = VulkanShaderCompiler::kBShift + 0;
    binds[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
    binds[0].descriptorCount = 1;
    binds[0].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    binds[1].binding = VulkanShaderCompiler::kTShift + 0;
    binds[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
    binds[1].descriptorCount = 1;
    binds[1].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    binds[2].binding = VulkanShaderCompiler::kSShift + 0;
    binds[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
    binds[2].descriptorCount = 1;
    binds[2].stageFlags = VK_SHADER_STAGE_FRAGMENT_BIT;
    VkDescriptorSetLayoutCreateInfo dslInfo{};
    dslInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO;
    dslInfo.bindingCount = 3;
    dslInfo.pBindings = binds;
    if (createDescriptorSetLayout(device, &dslInfo, nullptr, &customShaderDescLayout) != VK_SUCCESS) return false;

    VkPushConstantRange pcRange{};
    pcRange.stageFlags = VK_SHADER_STAGE_VERTEX_BIT;
    pcRange.offset = 0;
    pcRange.size = sizeof(CustomShaderGeomPushConstants);
    VkPipelineLayoutCreateInfo plInfo{};
    plInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO;
    plInfo.setLayoutCount = 1;
    plInfo.pSetLayouts = &customShaderDescLayout;
    plInfo.pushConstantRangeCount = 1;
    plInfo.pPushConstantRanges = &pcRange;
    if (createPipelineLayout(device, &plInfo, nullptr, &customShaderPipelineLayout) != VK_SUCCESS) return false;

    customShaderBaseReady = true;
    return true;
}

VkPipeline VulkanRenderTarget::Impl::EnsureCustomShaderPipeline(uint64_t hash, const char* hlslSource)
{
    auto it = customShaderPipelines.find(hash);
    if (it != customShaderPipelines.end()) return it->second;
    if (!EnsureCustomShaderBase() || !hlslSource) return VK_NULL_HANDLE;

    std::string err;
    std::vector<uint32_t> psSpirv = customShaderCompiler.Compile(hlslSource, "main", "ps_6_0", err);
    if (psSpirv.empty()) {
        VK_LOG("[Vulkan] custom shader PS compile failed: %s", err.c_str());
        customShaderPipelines.emplace(hash, VK_NULL_HANDLE);  // cache failure → don't retry
        return VK_NULL_HANDLE;
    }
    VkShaderModuleCreateInfo psInfo{};
    psInfo.sType = VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO;
    psInfo.codeSize = psSpirv.size() * sizeof(uint32_t);
    psInfo.pCode = psSpirv.data();
    VkShaderModule psModule = VK_NULL_HANDLE;
    if (createShaderModule(device, &psInfo, nullptr, &psModule) != VK_SUCCESS) {
        customShaderPipelines.emplace(hash, VK_NULL_HANDLE);
        return VK_NULL_HANDLE;
    }
    customShaderPsModules.emplace(hash, psModule);

    VkPipelineShaderStageCreateInfo stages[2]{};
    stages[0].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[0].stage = VK_SHADER_STAGE_VERTEX_BIT;
    stages[0].module = customShaderVs;
    stages[0].pName = "main";
    stages[1].sType = VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO;
    stages[1].stage = VK_SHADER_STAGE_FRAGMENT_BIT;
    stages[1].module = psModule;
    stages[1].pName = "main";

    VkPipelineVertexInputStateCreateInfo vi{};
    vi.sType = VK_STRUCTURE_TYPE_PIPELINE_VERTEX_INPUT_STATE_CREATE_INFO;
    VkPipelineInputAssemblyStateCreateInfo ia{};
    ia.sType = VK_STRUCTURE_TYPE_PIPELINE_INPUT_ASSEMBLY_STATE_CREATE_INFO;
    ia.topology = VK_PRIMITIVE_TOPOLOGY_TRIANGLE_LIST;
    VkPipelineViewportStateCreateInfo vp{};
    vp.sType = VK_STRUCTURE_TYPE_PIPELINE_VIEWPORT_STATE_CREATE_INFO;
    vp.viewportCount = 1; vp.scissorCount = 1;
    VkPipelineRasterizationStateCreateInfo rs{};
    rs.sType = VK_STRUCTURE_TYPE_PIPELINE_RASTERIZATION_STATE_CREATE_INFO;
    rs.polygonMode = VK_POLYGON_MODE_FILL;
    rs.cullMode = VK_CULL_MODE_NONE;
    rs.frontFace = VK_FRONT_FACE_COUNTER_CLOCKWISE;
    rs.lineWidth = 1.0f;
    VkPipelineMultisampleStateCreateInfo ms{};
    ms.sType = VK_STRUCTURE_TYPE_PIPELINE_MULTISAMPLE_STATE_CREATE_INFO;
    ms.rasterizationSamples = VK_SAMPLE_COUNT_1_BIT;
    // Premultiplied source-over (matches the rest of the Vulkan composite path).
    VkPipelineColorBlendAttachmentState ba{};
    ba.colorWriteMask = VK_COLOR_COMPONENT_R_BIT | VK_COLOR_COMPONENT_G_BIT |
                        VK_COLOR_COMPONENT_B_BIT | VK_COLOR_COMPONENT_A_BIT;
    ba.blendEnable = VK_TRUE;
    ba.srcColorBlendFactor = VK_BLEND_FACTOR_ONE;
    ba.dstColorBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    ba.colorBlendOp = VK_BLEND_OP_ADD;
    ba.srcAlphaBlendFactor = VK_BLEND_FACTOR_ONE;
    ba.dstAlphaBlendFactor = VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA;
    ba.alphaBlendOp = VK_BLEND_OP_ADD;
    VkPipelineColorBlendStateCreateInfo cb{};
    cb.sType = VK_STRUCTURE_TYPE_PIPELINE_COLOR_BLEND_STATE_CREATE_INFO;
    cb.attachmentCount = 1; cb.pAttachments = &ba;
    VkDynamicState dyn[2] = { VK_DYNAMIC_STATE_VIEWPORT, VK_DYNAMIC_STATE_SCISSOR };
    VkPipelineDynamicStateCreateInfo dynInfo{};
    dynInfo.sType = VK_STRUCTURE_TYPE_PIPELINE_DYNAMIC_STATE_CREATE_INFO;
    dynInfo.dynamicStateCount = 2; dynInfo.pDynamicStates = dyn;

    VkGraphicsPipelineCreateInfo pInfo{};
    pInfo.sType = VK_STRUCTURE_TYPE_GRAPHICS_PIPELINE_CREATE_INFO;
    pInfo.stageCount = 2; pInfo.pStages = stages;
    pInfo.pVertexInputState = &vi;
    pInfo.pInputAssemblyState = &ia;
    pInfo.pViewportState = &vp;
    pInfo.pRasterizationState = &rs;
    pInfo.pMultisampleState = &ms;
    pInfo.pColorBlendState = &cb;
    pInfo.pDynamicState = &dynInfo;
    pInfo.layout = customShaderPipelineLayout;
    pInfo.renderPass = frameRenderPass;
    pInfo.subpass = 0;

    VkPipeline pipeline = VK_NULL_HANDLE;
    if (createGraphicsPipelines(device, VK_NULL_HANDLE, 1, &pInfo, nullptr, &pipeline) != VK_SUCCESS) {
        pipeline = VK_NULL_HANDLE;
    }
    customShaderPipelines.emplace(hash, pipeline);
    return pipeline;
}

bool VulkanRenderTarget::Impl::DrawReplayFrame(const std::vector<VulkanRenderTarget::GpuReplayCommand>& commands,
                                               const float clearColor[4],
                                               const std::vector<VkImpellerDrawBatch>* engineBatches,
                                               const VelloScene* velloScene)
{
    BeginFrame();
    // Latched loss → never touch the device again (defense in depth; EndDraw's
    // entry gate normally aborts before reaching here).
    if (!DeviceUsable()) {
        EndFrame();
        return false;
    }
    if (!device || !swapchain || !commandBuffer) {
        VK_LOG("[Vulkan] DrawReplayFrame: basic precondition failed (device=%p swapchain=%p cmdBuf=%p)",
               (void*)device, (void*)swapchain, (void*)commandBuffer);
        EndFrame();
        return false;
    }
    // Wait for this slot's previous submission before touching its command buffer
    // or per-frame resources. Fence is SIGNALED-initialized so the first call
    // returns immediately. See DrawFrame for the frame-pacing accumulator
    // rationale — same instrumentation here.
    if (fencePending) {
        uint64_t waitStart = MonotonicNowNs();
        VkResult waitResult = NoteVk(waitForFences(device, 1, &inFlight, VK_TRUE, std::numeric_limits<uint64_t>::max()),
                                     "DrawReplayFrame.waitForFences");
        accumulatingWaitNs += MonotonicDiffNs(waitStart, MonotonicNowNs());
        if (waitResult == VK_SUCCESS) {
            fencePending = false;
        }
        if (waitResult != VK_SUCCESS) {
            VK_LOG("[Vulkan] DrawReplayFrame: waitForFences failed");
            // See DrawFrame for the accumulator-zeroing rationale.
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return false;
        }
    }
    if (timingSupported) {
        DecodeGpuTimingForCompletedFrame(currentFrame_);
    }
    if (lastSubmitMonotonicNs != 0) {
        lastFramePresentToReadyNs = MonotonicDiffNs(lastSubmitMonotonicNs, MonotonicNowNs());
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
    std::vector<VkDeviceSize> vcTriangleOffsets(commands.size(), 0);
    std::vector<VkDeviceSize> customShaderOffsets(commands.size(), 0);
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
    VkDeviceSize totalVcTriangleBytes = 0;
    VkDeviceSize totalCustomShaderBytes = 0;
    uint32_t maxCustomShaderWidth = 0;
    uint32_t maxCustomShaderHeight = 0;
    bool hasCustomShaderCommands = false;
    bool hasBitmapCommands = false;
    bool hasBackdropCommands = false;
    bool hasLiquidGlassCommands = false;
    bool hasBlurCommands = false;
    bool hasPolygonCommands = false;
    bool hasTransitionCommands = false;
    bool hasVcTriangleCommands = false;
#ifdef _WIN32
    // Glyph-instance SSBO sizing (B4b). Each TextRun contributes glyphCount*48
    // bytes to the trailing glyph region of the staging buffer; textRunOffsets is
    // the per-command byte offset WITHIN that region (added to textGlyphStagingBase
    // below).
    std::vector<VkDeviceSize> textRunOffsets(commands.size(), 0);
    VkDeviceSize totalTextBytes = 0;
    bool hasTextCommands = false;
#endif

    for (size_t index = 0; index < commands.size(); ++index) {
        const auto& command = commands[index];
        if (command.kind == GpuReplayCommandKind::Bitmap) {
            const auto& bmPixels = command.bitmap.GetPixels();
            if (command.bitmap.pixelWidth == 0 || command.bitmap.pixelHeight == 0 || bmPixels.empty()) {
                EndFrame();
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
                EndFrame();
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
                EndFrame();
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
            if (command.blur.captureLiveScene) {
                // GPU compositor path: no CPU pixels to stage — the blur source is
                // blitted from the rendered frame's [x,y,w,h] sub-rect at replay
                // time (see the Blur branch below). We still need valid dest dims so
                // the shared upload image is sized to hold the captured region.
                if (command.blur.pixelWidth == 0 || command.blur.pixelHeight == 0) {
                    EndFrame();
                    return false;
                }
                maxBlurWidth = std::max(maxBlurWidth, command.blur.pixelWidth);
                maxBlurHeight = std::max(maxBlurHeight, command.blur.pixelHeight);
                hasBlurCommands = true;
                continue;
            }

            const size_t expectedSize = static_cast<size_t>(command.blur.pixelWidth) * static_cast<size_t>(command.blur.pixelHeight) * 4u;
            if (command.blur.pixelWidth == 0 || command.blur.pixelHeight == 0 || command.blur.pixels.size() != expectedSize) {
                EndFrame();
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
            const auto& verts = command.filledPolygon.GetTriangleVertices();
            if (verts.size() < 6 || (verts.size() % 2) != 0) {
                EndFrame();
                return false;
            }

            polygonOffsets[index] = totalPolygonBytes;
            totalPolygonBytes += static_cast<VkDeviceSize>(verts.size() * sizeof(float));
            hasPolygonCommands = true;
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Transition) {
            const size_t expectedSize = static_cast<size_t>(command.transition.pixelWidth) * static_cast<size_t>(command.transition.pixelHeight) * 4u;
            if (command.transition.pixelWidth == 0 || command.transition.pixelHeight == 0 ||
                command.transition.fromPixels.size() != expectedSize ||
                command.transition.toPixels.size() != expectedSize) {
                EndFrame();
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
            continue;
        }

        if (command.kind == GpuReplayCommandKind::VcTriangles) {
            // Validate vertex stride invariant (6 floats per vertex).
            const auto& verts = command.vcTriangles.vertices;
            if (verts.size() < 6 || (verts.size() % 6) != 0 ||
                verts.size() != static_cast<size_t>(command.vcTriangles.vertexCount) * 6u) {
                EndFrame();
                return false;
            }
            vcTriangleOffsets[index] = totalVcTriangleBytes;
            totalVcTriangleBytes += static_cast<VkDeviceSize>(verts.size() * sizeof(float));
            hasVcTriangleCommands = true;
        }

        if (command.kind == GpuReplayCommandKind::CustomShader) {
            const auto& cs = command.customShader;
            const size_t expectedSize = static_cast<size_t>(cs.pixelWidth) * static_cast<size_t>(cs.pixelHeight) * 4u;
            if (cs.pixelWidth == 0 || cs.pixelHeight == 0 || cs.pixels.size() != expectedSize) {
                EndFrame();
                return false;
            }
            customShaderOffsets[index] = totalCustomShaderBytes;
            totalCustomShaderBytes += static_cast<VkDeviceSize>(expectedSize);
            maxCustomShaderWidth = std::max(maxCustomShaderWidth, cs.pixelWidth);
            maxCustomShaderHeight = std::max(maxCustomShaderHeight, cs.pixelHeight);
            hasCustomShaderCommands = true;
        }

#ifdef _WIN32
        if (command.kind == GpuReplayCommandKind::TextRun) {
            const uint32_t gc = command.textRun.glyphCount;
            if (gc > 0) {
                if (command.textRun.glyphs.size() != static_cast<size_t>(gc) * 12u) { EndFrame(); return false; }
                textRunOffsets[index] = totalTextBytes;
                totalTextBytes += static_cast<VkDeviceSize>(command.textRun.glyphs.size() * sizeof(float));
                hasTextCommands = true;
            }
        }
#endif
    }

    // Custom-shader commands also upload their (cropped) captured content to the
    // shared upload image and live in the staging buffer's trailing region, so
    // they participate in the upload-image sizing + staging total below.
    const VkDeviceSize nonTextStagingBytes = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes
        + totalBlurBytes + totalPolygonBytes + totalTransitionBytes + totalVcTriangleBytes + totalCustomShaderBytes;
#ifdef _WIN32
    // The glyph-instance SSBO region goes LAST in the staging buffer; its base must
    // satisfy the storage-buffer descriptor offset alignment (256 ≥ any
    // minStorageBufferOffsetAlignment). When no text is present the base collapses
    // to nonTextStagingBytes so the total is byte-identical to the pre-B4b layout.
    const VkDeviceSize kTextSsboAlign = 256;
    const VkDeviceSize textGlyphStagingBase = hasTextCommands
        ? ((nonTextStagingBytes + kTextSsboAlign - 1) / kTextSsboAlign) * kTextSsboAlign
        : nonTextStagingBytes;
    const VkDeviceSize totalStagingBytes = textGlyphStagingBase + totalTextBytes;
#else
    const VkDeviceSize totalStagingBytes = nonTextStagingBytes;
#endif
    if (hasBitmapCommands || hasBackdropCommands || hasLiquidGlassCommands || hasBlurCommands || hasCustomShaderCommands) {
        if ((hasBitmapCommands && bitmapPipeline == VK_NULL_HANDLE) ||
            (hasBackdropCommands && backdropPipeline == VK_NULL_HANDLE) ||
            (hasLiquidGlassCommands && liquidGlassPipeline == VK_NULL_HANDLE) ||
            (hasBlurCommands && blurPipeline == VK_NULL_HANDLE) ||
            !EnsureUploadImageCapacity(
                std::max(std::max(std::max(std::max(maxBitmapWidth, maxBackdropWidth), maxBlurWidth), maxLiquidGlassWidth), maxCustomShaderWidth),
                std::max(std::max(std::max(std::max(maxBitmapHeight, maxBackdropHeight), maxBlurHeight), maxLiquidGlassHeight), maxCustomShaderHeight)) ||
            !UpdateFrameDescriptorSet() ||
            !EnsureStagingCapacity(totalStagingBytes)) {
            // EndFrame on every failure exit: the Ensure* helpers destroy the
            // old per-frame resources in place and only rewrite the alias
            // fields — without CommitCurrentFrame (inside EndFrame) the slot
            // would keep the destroyed handles and the next frame's
            // BeginFrame would revive them (use-after-free / double-destroy).
            EndFrame();
            return false;
        }
    } else if ((hasPolygonCommands || hasTransitionCommands || hasVcTriangleCommands
#ifdef _WIN32
                || hasTextCommands
#endif
                )
               && !EnsureStagingCapacity(totalStagingBytes)) {
        EndFrame();
        return false;
    }

#ifdef _WIN32
    // Glyph SSBO is now guaranteed resident (EnsureStagingCapacity ran in one of
    // the branches above whenever hasTextCommands). Size the atlas VkImage to the
    // CPU atlas and (re)point the text descriptor set at the atlas view + the
    // glyph SSBO slice. EnsureTextAtlasImage MUST precede UpdateTextDescriptorSet
    // because the latter binds textAtlasImageView. glyphAtlas_ is non-null
    // whenever a TextRun was recorded (RenderText created it via EnsureGlyphAtlas).
    if (hasTextCommands && textPipeline != VK_NULL_HANDLE && glyphAtlas_) {
        EnsureTextAtlasImage(glyphAtlas_->GetWidth(), glyphAtlas_->GetHeight());
        UpdateTextDescriptorSet(textGlyphStagingBase, totalTextBytes);
    }
#endif

    if (hasTransitionCommands) {
        if (transitionPipeline == VK_NULL_HANDLE ||
            !EnsureTransitionImagesCapacity(maxTransitionWidth, maxTransitionHeight) ||
            !UpdateTransitionDescriptorSet()) {
            EndFrame();
            return false;
        }
    }

    auto* stagingBytes = static_cast<uint8_t*>(mappedPixels);
    if ((hasBitmapCommands || hasBackdropCommands || hasLiquidGlassCommands || hasBlurCommands
         || hasPolygonCommands || hasTransitionCommands || hasVcTriangleCommands || hasCustomShaderCommands
#ifdef _WIN32
         || hasTextCommands
#endif
         ) && !stagingBytes) {
        EndFrame();
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
            // captureLiveScene blur carries NO CPU pixels (its source is blitted
            // from the live frame at replay time), so it allocated 0 staging bytes
            // in the sizing loop — must NOT memcpy here (pixels is empty while
            // pixelWidth/Height are non-zero, which would read out of bounds).
            if (!command.blur.captureLiveScene) {
                const size_t pixelBytes = static_cast<size_t>(command.blur.pixelWidth) * static_cast<size_t>(command.blur.pixelHeight) * 4u;
                std::memcpy(stagingBytes + totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + blurOffsets[index], command.blur.pixels.data(), pixelBytes);
            }
        } else if (command.kind == GpuReplayCommandKind::FilledPolygon) {
            const auto& verts = command.filledPolygon.GetTriangleVertices();
            const size_t vertexBytes = verts.size() * sizeof(float);
            std::memcpy(stagingBytes + totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + totalBlurBytes + polygonOffsets[index], verts.data(), vertexBytes);
        } else if (command.kind == GpuReplayCommandKind::Transition) {
            const size_t pixelBytes = static_cast<size_t>(command.transition.pixelWidth) * static_cast<size_t>(command.transition.pixelHeight) * 4u;
            const VkDeviceSize baseOffset = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes + totalBlurBytes + totalPolygonBytes;
            std::memcpy(stagingBytes + baseOffset + transitionFromOffsets[index], command.transition.fromPixels.data(), pixelBytes);
            std::memcpy(stagingBytes + baseOffset + transitionToOffsets[index], command.transition.toPixels.data(), pixelBytes);
        } else if (command.kind == GpuReplayCommandKind::VcTriangles) {
            const auto& verts = command.vcTriangles.vertices;
            const size_t vertexBytes = verts.size() * sizeof(float);
            const VkDeviceSize baseOffset = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes
                                          + totalBlurBytes + totalPolygonBytes + totalTransitionBytes;
            std::memcpy(stagingBytes + baseOffset + vcTriangleOffsets[index], verts.data(), vertexBytes);
        } else if (command.kind == GpuReplayCommandKind::CustomShader) {
            const auto& cs = command.customShader;
            const size_t pixelBytes = static_cast<size_t>(cs.pixelWidth) * static_cast<size_t>(cs.pixelHeight) * 4u;
            const VkDeviceSize baseOffset = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes
                                          + totalBlurBytes + totalPolygonBytes + totalTransitionBytes + totalVcTriangleBytes;
            std::memcpy(stagingBytes + baseOffset + customShaderOffsets[index], cs.pixels.data(), pixelBytes);
        }
#ifdef _WIN32
        else if (command.kind == GpuReplayCommandKind::TextRun) {
            if (command.textRun.glyphCount > 0) {
                const size_t bytes = command.textRun.glyphs.size() * sizeof(float);
                std::memcpy(stagingBytes + textGlyphStagingBase + textRunOffsets[index], command.textRun.glyphs.data(), bytes);
            }
        }
#endif
    }

    uint32_t imageIndex = 0;
    {
        uint64_t acqStart = MonotonicNowNs();
        // See DrawFrame — DEVICE_LOST/SURFACE_LOST latch, OUT_OF_DATE = resize.
        const VkResult acquireResult = NoteVk(acquireNextImage(device, swapchain, std::numeric_limits<uint64_t>::max(), imageAvailable, VK_NULL_HANDLE, &imageIndex),
                                              "DrawReplayFrame.acquireNextImage");
        accumulatingWaitNs += MonotonicDiffNs(acqStart, MonotonicNowNs());
        if (acquireResult == VK_ERROR_OUT_OF_DATE_KHR) {
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return true;
        }
        if (acquireResult != VK_SUCCESS && acquireResult != VK_SUBOPTIMAL_KHR) {
            lastFrameWaitNs = accumulatingWaitNs;
            accumulatingWaitNs = 0;
            EndFrame();
            return false;
        }
    }

    // resetFences is deferred to right before queueSubmit — see the DrawFrame
    // comment: an early reset plus any later failure exit would orphan this
    // slot's fence unsignaled and hang the slot's next waitForFences forever.

    if (NoteVk(resetCommandBuffer(commandBuffer, 0), "DrawReplayFrame.resetCommandBuffer") != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    VkCommandBufferBeginInfo beginInfo {};
    beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    if (NoteVk(beginCommandBuffer(commandBuffer, &beginInfo), "DrawReplayFrame.beginCommandBuffer") != VK_SUCCESS) {
        EndFrame();
        return false;
    }

    // Frame-pacing flush — see DrawFrame for the rationale.
    lastFrameWaitNs = accumulatingWaitNs;
    accumulatingWaitNs = 0;

    // Reset this frame's slot of the timestamp query pool and write the
    // initial "Other" timestamp.  Subsequent MarkGpuTimingPoint calls in
    // the per-command loop below add per-category boundaries; the final
    // kFrameEnd timestamp is written right before endCommandBuffer.
    if (timingSupported && cmdResetQueryPool && cmdWriteTimestamp && timingQueryPool != VK_NULL_HANDLE) {
        auto& tf = timing[currentFrame_];
        tf.nextSlot = 0;
        tf.spanCategories.clear();
        tf.batchCountAtFinalize = 0;
        tf.hasResolvedData = false;
        uint32_t base = currentFrame_ * kMaxTimingSlotsPerFrame;
        cmdResetQueryPool(commandBuffer, timingQueryPool, base, kMaxTimingSlotsPerFrame);
        MarkGpuTimingPoint(GpuTimingCategory::Other);
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

#ifdef _WIN32
    // ── Glyph-atlas dirty-band upload (B4b) ──────────────────────────────────
    // The CPU atlas was just (re)filled by this frame's RenderText → GenerateGlyphs
    // calls. Push only the dirty row band into the GPU atlas image, then leave it
    // in SHADER_READ_ONLY so the text pipeline's descriptor (which declares that
    // layout) is valid. We are outside any render pass here (the per-command draw
    // blocks open/close their own), so the transfer barriers are legal. NB: no
    // atlas growth in this batch — ConsumeAtlasRecreated() is still drained to keep
    // the recreate flag from accumulating, but the band/extent always fits the
    // current image (RenderText keys the atlas at GetWidth()/GetHeight()).
    if (hasTextCommands && textPipeline != VK_NULL_HANDLE && textAtlasImage != VK_NULL_HANDLE && glyphAtlas_) {
        glyphAtlas_->ConsumeAtlasRecreated();

        uint16_t bandMinY = 0, bandMaxY = 0;
        const bool dirty = glyphAtlas_->TakeDirtyBand(bandMinY, bandMaxY);
        if (dirty && bandMaxY > bandMinY) {
            const uint32_t aw = textAtlasWidth;
            const uint32_t bandRows = static_cast<uint32_t>(bandMaxY - bandMinY);
            const VkDeviceSize bandBytes = static_cast<VkDeviceSize>(aw) * bandRows * 4u;
            if (aw > 0 && bandRows > 0 && bandMaxY <= textAtlasHeight && EnsureTextAtlasStaging(bandBytes) && textAtlasStagingMapped) {
                std::memcpy(textAtlasStagingMapped,
                            glyphAtlas_->GetAtlasBitmap() + static_cast<size_t>(bandMinY) * aw * 4u,
                            static_cast<size_t>(bandBytes));

                VkImageMemoryBarrier atlasToTransfer {};
                atlasToTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
                atlasToTransfer.oldLayout = textAtlasImageLayout;
                atlasToTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                atlasToTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                atlasToTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                atlasToTransfer.image = textAtlasImage;
                atlasToTransfer.subresourceRange = subresourceRange;
                atlasToTransfer.srcAccessMask = textAtlasImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_ACCESS_SHADER_READ_BIT : 0;
                atlasToTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                const VkPipelineStageFlags atlasSrcStage = textAtlasImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL
                    ? (VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT)
                    : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
                cmdPipelineBarrier(commandBuffer, atlasSrcStage, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &atlasToTransfer);

                VkBufferImageCopy atlasCopy {};
                atlasCopy.bufferOffset = 0;
                atlasCopy.bufferRowLength = 0;
                atlasCopy.bufferImageHeight = 0;
                atlasCopy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
                atlasCopy.imageSubresource.mipLevel = 0;
                atlasCopy.imageSubresource.baseArrayLayer = 0;
                atlasCopy.imageSubresource.layerCount = 1;
                atlasCopy.imageOffset.x = 0;
                atlasCopy.imageOffset.y = static_cast<int32_t>(bandMinY);
                atlasCopy.imageOffset.z = 0;
                atlasCopy.imageExtent.width = aw;
                atlasCopy.imageExtent.height = bandRows;
                atlasCopy.imageExtent.depth = 1;
                cmdCopyBufferToImage(commandBuffer, textAtlasStagingBuffer, textAtlasImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &atlasCopy);

                VkImageMemoryBarrier atlasToShaderRead {};
                atlasToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
                atlasToShaderRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
                atlasToShaderRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
                atlasToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
                atlasToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                atlasToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
                atlasToShaderRead.image = textAtlasImage;
                atlasToShaderRead.subresourceRange = subresourceRange;
                atlasToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
                cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT,
                                   VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT,
                                   0, 0, nullptr, 0, nullptr, 1, &atlasToShaderRead);
                textAtlasImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            }
        } else if (textAtlasImageLayout != VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL) {
            // Nothing dirty (e.g. every glyph this frame was a cache hit) but the
            // image is freshly created (UNDEFINED) — transition it so the
            // descriptor's SHADER_READ_ONLY_OPTIMAL declaration is satisfied.
            VkImageMemoryBarrier atlasToShaderRead {};
            atlasToShaderRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            atlasToShaderRead.srcAccessMask = 0;
            atlasToShaderRead.oldLayout = textAtlasImageLayout;
            atlasToShaderRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            atlasToShaderRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            atlasToShaderRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            atlasToShaderRead.image = textAtlasImage;
            atlasToShaderRead.subresourceRange = subresourceRange;
            atlasToShaderRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                               VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT | VK_PIPELINE_STAGE_VERTEX_SHADER_BIT,
                               0, 0, nullptr, 0, nullptr, 1, &atlasToShaderRead);
            textAtlasImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
        }

        // Apply any pending atlas growth/reset that THIS frame's GenerateGlyphs
        // requested when it ran out of 512px atlas space. Deferred to HERE — after
        // this frame's atlas upload, before the next frame's recording — so the
        // current frame's already-recorded glyph instances stay UV-consistent with
        // the image we just uploaded (growing earlier would re-pack the atlas and
        // strand this frame's instances on stale UVs). The grow reallocates the CPU
        // atlas to 2x (preserving packed glyphs), bumps the generation — which
        // invalidates the per-run glyph memo so glyphs that were DROPPED while the
        // atlas was full get re-emitted — and marks the whole atlas dirty; next
        // frame the capacity-phase EnsureTextAtlasImage(GetWidth/Height) recreates
        // the GPU image at the new size and the full band re-uploads. Without this
        // the atlas is stuck at 512px and a text-heavy page drops every glyph past
        // the first ~128 variants (8 sub-pixel buckets x size eat slots fast).
        glyphAtlas_->ApplyPendingGrowthOrReset();
    }
#endif

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

    // env-gated GPU offscreen effect RT (JALIUM_VK_EFFECT_GPU_RT). Inside an
    // OffscreenBegin..OffscreenEnd region beginLoadRenderPass() redirects to the offscreen
    // framebuffer (CLEAR transparent on the region's first draw, LOAD after); OffscreenEnd
    // composites the isolated element back into the main frame. lastOffscreenView is the
    // isolated image the matching effect samples (Stage 4). All inert when off: no marker is
    // ever emitted and offscreenReady short-circuits (EnsureEffectOffscreenResources not called).
    bool offscreenActive = false;
    bool offscreenFirstDraw = false;
    bool offscreenReady = effectOffscreenEnabled_ && EnsureEffectOffscreenResources(extent);
    VkImageView lastOffscreenView = VK_NULL_HANDLE;
    (void)lastOffscreenView;  // consumed in Stage 4

    const VkClearValue renderPassClearValue = { { 0.0f, 0.0f, 0.0f, 0.0f } };
    auto beginLoadRenderPass = [&]() {
        VkRenderPassBeginInfo renderPassInfo {};
        renderPassInfo.sType = VK_STRUCTURE_TYPE_RENDER_PASS_BEGIN_INFO;
        if (offscreenActive && offscreenReady) {
            // First draw of the region clears transparent; later draws preserve prior content.
            renderPassInfo.renderPass  = offscreenFirstDraw ? effectOffscreenRenderPassClear  : effectOffscreenRenderPassLoad;
            renderPassInfo.framebuffer = offscreenFirstDraw ? effectOffscreenFramebufferClear : effectOffscreenFramebufferLoad;
            offscreenFirstDraw = false;
        } else {
            renderPassInfo.renderPass = frameRenderPass;
            renderPassInfo.framebuffer = framebuffers[imageIndex];
        }
        renderPassInfo.renderArea.extent = extent;
        renderPassInfo.clearValueCount = 1;
        renderPassInfo.pClearValues = &renderPassClearValue;
        cmdBeginRenderPass(commandBuffer, &renderPassInfo, VK_SUBPASS_CONTENTS_INLINE);
        cmdSetViewport(commandBuffer, 0, 1, &viewport);
    };

    // Per-command timing category — only emits a TIMESTAMP boundary when the
    // category actually changes, so a run of N SolidRect commands costs just
    // one MarkGpuTimingPoint at the start of the run instead of N. The
    // initial value of `lastTimingCategory` matches the "Other" span the
    // BeginCommandBuffer-time mark opened, so the very first non-Other
    // command in the list still triggers a boundary write.
    GpuTimingCategory lastTimingCategory = GpuTimingCategory::Other;

    // Reset this frame's ink-blit descriptor pool on the first InkLayer command
    // encountered (lazily, so frames with no ink pay nothing). The pool for this
    // frame slot is safe to reset because waitForFences at frame start already
    // observed its previous use complete.
    bool inkBlitPoolReset = false;
    // Custom-shader effect: same per-frame transient pattern. The constants
    // cursor sub-allocates the per-frame user-constants UBO (256-aligned).
    bool customShaderPoolReset = false;
    VkDeviceSize customConstantsCursor = 0;
    const VkDeviceSize kCustomConstantsAlign = 256;  // ≥ any minUniformBufferOffsetAlignment

    // Staging base offset of the custom-shader region (after every other region).
    const VkDeviceSize customShaderStagingBase = totalBitmapBytes + totalBackdropBytes + totalLiquidGlassBytes
        + totalBlurBytes + totalPolygonBytes + totalTransitionBytes + totalVcTriangleBytes;

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

#ifdef __ANDROID__
        { static int s_seqFrame = 0; if (index == 0) s_seqFrame++;
          static int s_seqLines = 0;
          if (s_seqFrame >= 4 && s_seqLines < 400) { s_seqLines++;
            float rx=0,ry=0,rw=0,rh=0,ra=-1.0f;
            if (command.kind == GpuReplayCommandKind::SolidRect || command.kind == GpuReplayCommandKind::ClearRect) { rx=command.solidRect.x; ry=command.solidRect.y; rw=command.solidRect.w; rh=command.solidRect.h; ra=command.solidRect.a; }
            else if (command.kind == GpuReplayCommandKind::Bitmap) { rx=command.bitmap.x; ry=command.bitmap.y; rw=command.bitmap.w; rh=command.bitmap.h; ra=command.bitmap.opacity; }
            VK_LOG("[SEQ] f%d #%zu k%d (%.0f,%.0f %.0fx%.0f) a=%.2f", s_seqFrame, index, (int)command.kind, rx, ry, rw, rh, ra); } }
#endif

        if (timingSupported) {
            GpuTimingCategory cat;
            switch (command.kind) {
            case GpuReplayCommandKind::SolidRect:
            case GpuReplayCommandKind::ClearRect:
                cat = GpuTimingCategory::SdfRect; break;
            case GpuReplayCommandKind::Bitmap:
            case GpuReplayCommandKind::InkLayer:
            case GpuReplayCommandKind::CustomShader:
            case GpuReplayCommandKind::TextRun:
                cat = GpuTimingCategory::Bitmap; break;
            case GpuReplayCommandKind::FilledPolygon:
                cat = GpuTimingCategory::Path; break;
            case GpuReplayCommandKind::Backdrop:
            case GpuReplayCommandKind::Blur:         // Blur shares the backdrop blur pipeline
                cat = GpuTimingCategory::Backdrop; break;
            case GpuReplayCommandKind::LiquidGlass:
                cat = GpuTimingCategory::LiquidGlass; break;
            case GpuReplayCommandKind::VcTriangles:
                // Used by DrawLine 3-strip AA — treat as SdfRect-class
                // (analytical edge-aliased rect-ish primitive). Lets the
                // DevTools breakdown account stroke AA work against the
                // same bucket as the SDF rect / rounded-rect pipelines.
                cat = GpuTimingCategory::SdfRect; break;
            case GpuReplayCommandKind::Glow:
            case GpuReplayCommandKind::Transition:
            default:
                cat = GpuTimingCategory::Other; break;
            }
            if (cat != lastTimingCategory) {
                MarkGpuTimingPoint(cat);
                lastTimingCategory = cat;
            }
        }

        if (command.kind == GpuReplayCommandKind::OffscreenBegin) {
            if (!offscreenReady) {
                // Offscreen RT unavailable: degrade to the legacy behaviour -- leave
                // offscreenActive false so the child draws keep rendering into the main frame.
                continue;
            }
            // No image barrier needed: the CLEAR-variant render pass uses initialLayout=UNDEFINED
            // (discards any prior layout) and the EXTERNAL dep0 (FRAGMENT_SHADER|COLOR_OUTPUT ->
            // COLOR_OUTPUT) orders the previous frame's sample / write before this frame's writes.
            offscreenActive = true;
            offscreenFirstDraw = true;
            continue;
        }
        if (command.kind == GpuReplayCommandKind::OffscreenEnd) {
            offscreenActive = false;
            if (offscreenReady) {
                // The element subtree is now isolated in the offscreen image, which every
                // per-pass finalLayout=SHADER_READ_ONLY_OPTIMAL already left sample-ready.
                // Composite it 1:1 back into the main frame so the element stays visible (it
                // was redirected out of the main framebuffer); the image also remains the
                // effect sampling source (lastOffscreenView, Stage 4). The offscreen is
                // transparent except where the element drew, so a full-screen alpha composite
                // only touches the element's pixels and preserves z-order at this command point.
                lastOffscreenView = effectOffscreenView;
                beginLoadRenderPass();  // offscreenActive==false -> main frame
                cmdSetScissor(commandBuffer, 0, 1, &scissor);
                BitmapQuadPushConstants pushConstants {};
                pushConstants.rect[0] = 0.0f;
                pushConstants.rect[1] = 0.0f;
                pushConstants.rect[2] = static_cast<float>(extent.width);
                pushConstants.rect[3] = static_cast<float>(extent.height);
                pushConstants.uvOpacity[0] = 1.0f;
                pushConstants.uvOpacity[1] = 1.0f;
                pushConstants.uvOpacity[2] = 1.0f;
                pushConstants.screenSize[0] = static_cast<float>(extent.width);
                pushConstants.screenSize[1] = static_cast<float>(extent.height);
                cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, bitmapPipeline);
                cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, bitmapPipelineLayout, 0, 1, &effectOffscreenDescriptorSet, 0, nullptr);
                cmdPushConstants(commandBuffer, bitmapPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT, 0, sizeof(pushConstants), &pushConstants);
                cmdDraw(commandBuffer, 6, 1, 0, 0);
                cmdEndRenderPass(commandBuffer);
            }
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
                // Per-corner radii. Default-initialised to zero, so the
                // memcpy only matters for PerCornerRoundedRectangle paths
                // that actually set non-zero values; otherwise the shader's
                // perCornerSum stays at 0 and the uniform-radius path runs.
                std::memcpy(pushConstants.perCornerRadiusX, command.perCornerRadiusX, sizeof(pushConstants.perCornerRadiusX));
                std::memcpy(pushConstants.perCornerRadiusY, command.perCornerRadiusY, sizeof(pushConstants.perCornerRadiusY));
            }
            if (command.hasInnerRoundedClip) {
                pushConstants.innerRoundedClipRect[0] = command.innerRoundedClipLeft;
                pushConstants.innerRoundedClipRect[1] = command.innerRoundedClipTop;
                pushConstants.innerRoundedClipRect[2] = command.innerRoundedClipRight;
                pushConstants.innerRoundedClipRect[3] = command.innerRoundedClipBottom;
                pushConstants.innerRoundedClipRadius[0] = command.innerRoundedClipRadiusX;
                pushConstants.innerRoundedClipRadius[1] = command.innerRoundedClipRadiusY;
                pushConstants.clipFlags[1] = 1.0f;
                // Per-corner inner radii. Default-initialised to zero, so this
                // only matters for the PerCornerRoundedRectangle STROKE path
                // that sets non-zero values; otherwise the shader's inner
                // per-corner sum stays 0 and the uniform inner path runs.
                std::memcpy(pushConstants.innerPerCornerRadiusX, command.innerPerCornerRadiusX, sizeof(pushConstants.innerPerCornerRadiusX));
                std::memcpy(pushConstants.innerPerCornerRadiusY, command.innerPerCornerRadiusY, sizeof(pushConstants.innerPerCornerRadiusY));
            }
            // Rotated / skewed rounded rect & fill: forward the LOCAL geometry
            // into the (free on this path) inner rounded-clip slots and arm
            // geometryFlags.y so the shader takes its local-space SDF branch.
            // hasRoundedClip / hasInnerRoundedClip are false here, so the
            // screen-space clip writes above did NOT touch clipFlags — exactly
            // what the local path needs (clipFlags stays 0). No push-constant
            // growth: every field written here already exists in the 224-byte
            // block.
            if (command.hasLocalRoundedCoverage) {
                pushConstants.innerRoundedClipRect[0] = command.localOuterW;
                pushConstants.innerRoundedClipRect[1] = command.localOuterH;
                pushConstants.innerRoundedClipRect[2] = command.localInnerW;
                pushConstants.innerRoundedClipRect[3] = command.localInnerH;
                // Per-axis AA pad consumed by the vertex shader to rebuild the
                // expanded local corner positions for the localPos varying.
                pushConstants.roundedClipRadius[0] = command.localExpandX;
                pushConstants.roundedClipRadius[1] = command.localExpandY;
                std::memcpy(pushConstants.innerPerCornerRadiusX, command.localOuterPerCornerRadius, sizeof(pushConstants.innerPerCornerRadiusX));
                std::memcpy(pushConstants.innerPerCornerRadiusY, command.localInnerPerCornerRadius, sizeof(pushConstants.innerPerCornerRadiusY));
                pushConstants.geometryFlags[1] = 1.0f;
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
            // Affine 2x3 transform applied by the vertex shader. Cache hits
            // emit local-space vertices and the recorded transform; rasterize-
            // fallback paths leave it at identity (their vertices are already
            // pre-transformed to world space).
            pushConstants.transformRow0[0] = command.filledPolygon.transformRow0[0];
            pushConstants.transformRow0[1] = command.filledPolygon.transformRow0[1];
            pushConstants.transformRow0[2] = command.filledPolygon.transformRow0[2];
            pushConstants.transformRow0[3] = command.filledPolygon.transformRow0[3];
            pushConstants.transformRow1[0] = command.filledPolygon.transformRow1[0];
            pushConstants.transformRow1[1] = command.filledPolygon.transformRow1[1];
            pushConstants.transformRow1[2] = command.filledPolygon.transformRow1[2];
            pushConstants.transformRow1[3] = command.filledPolygon.transformRow1[3];
            const VkDeviceSize vertexOffset = totalBitmapBytes + polygonOffsets[index];
            const auto& verts = command.filledPolygon.GetTriangleVertices();
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, triangleFillPipeline);
            cmdBindVertexBuffers(commandBuffer, 0, 1, &stagingBuffer, &vertexOffset);
            cmdPushConstants(
                commandBuffer,
                triangleFillPipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);
            cmdDraw(commandBuffer, static_cast<uint32_t>(verts.size() / 2), 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::Blur) {
            if (command.blur.captureLiveScene) {
                // GPU compositor path: blur the LIVE composited frame at the
                // element's screen rect (the element was just rendered there by the
                // replay; BeginEffectCapture is a GPU-path pass-through). No CPU
                // upload and no per-element offscreen render target — we blit the
                // element's own [x,y,w,h] sub-rect of the rendered colour image into
                // the shared upload image and the existing blur pipeline samples it.
                // Must run OUTSIDE a render pass; none is open between commands.
                if (!CaptureLiveSceneToUpload(imageIndex, subresourceRange, extent,
                        command.blur.pixelWidth, command.blur.pixelHeight,
                        static_cast<int32_t>(std::lround(command.blur.x)),
                        static_cast<int32_t>(std::lround(command.blur.y)),
                        static_cast<int32_t>(std::lround(command.blur.w)),
                        static_cast<int32_t>(std::lround(command.blur.h)))) {
                    // No TRANSFER_SRC capture and no CPU-pixel fallback for the
                    // compositor path — skip the composite (element stays sharp this
                    // frame) rather than sampling an uninitialised upload image.
                    continue;
                }
            } else {
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
            }

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
            // Prefer the live rendered scene (real refraction/blur source); fall
            // back to the CPU-pixel upload when TRANSFER_SRC capture is
            // unavailable (then the source is the GPU-path pixelBuffer_ snapshot).
            if (!CaptureLiveSceneToUpload(imageIndex, subresourceRange, extent,
                                          command.backdrop.pixelWidth, command.backdrop.pixelHeight)) {
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
            }

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
            // Live rendered scene as the refraction/blur source; CPU-pixel
            // fallback when TRANSFER_SRC capture is unavailable.
            if (!CaptureLiveSceneToUpload(imageIndex, subresourceRange, extent,
                                          command.liquidGlass.pixelWidth, command.liquidGlass.pixelHeight)) {
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
            }

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

        if (command.kind == GpuReplayCommandKind::VcTriangles) {
            // Per-vertex-coloured triangle list. Vertex data lives at a known
            // byte offset inside the per-frame staging buffer (which now
            // doubles as a vertex buffer — see EnsureStagingCapacity's usage
            // flags). Bind it directly, push the tint = white + clip flags,
            // and issue a single vkCmdDraw.
            if (vcTrianglePipeline == VK_NULL_HANDLE || stagingBuffer == VK_NULL_HANDLE) {
                continue;  // pipeline not ready — skip silently rather than break the frame.
            }
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);

            TriangleFillVcPushConstants pushConstants {};
            // Push-constant color is a tint multiplier — pre-multiplied
            // alpha colours from the vertex data already encode the line's
            // intended colour, so the tint is white. Keeping it as a push
            // constant (rather than removing it from the shader) keeps the
            // vc pipeline reusable for future gradient / non-AA per-vertex
            // colour primitives.
            pushConstants.color[0] = 1.0f;
            pushConstants.color[1] = 1.0f;
            pushConstants.color[2] = 1.0f;
            pushConstants.color[3] = 1.0f;
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

            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, vcTrianglePipeline);
            cmdPushConstants(
                commandBuffer,
                vcTrianglePipelineLayout,
                VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                0,
                sizeof(pushConstants),
                &pushConstants);

            const VkDeviceSize vertexBaseOffset = totalBitmapBytes + totalBackdropBytes
                                                + totalLiquidGlassBytes + totalBlurBytes
                                                + totalPolygonBytes + totalTransitionBytes
                                                + vcTriangleOffsets[index];
            cmdBindVertexBuffers(commandBuffer, 0, 1, &stagingBuffer, &vertexBaseOffset);
            cmdDraw(commandBuffer, command.vcTriangles.vertexCount, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

#ifdef _WIN32
        if (command.kind == GpuReplayCommandKind::TextRun) {
            // Dedicated text-glyph pipeline. The glyph SSBO + atlas image were
            // uploaded above; textDescriptorSet binds the whole glyph region at
            // textGlyphStagingBase, so SV_InstanceID = firstInstance + local
            // indexes gGlyphs[] at the right glyph. 6 verts = one quad per glyph.
            if (textPipeline == VK_NULL_HANDLE || textDescriptorSet == VK_NULL_HANDLE ||
                stagingBuffer == VK_NULL_HANDLE || command.textRun.glyphCount == 0) {
                continue;
            }
            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            TextPushConstants pc {};
            pc.screenSize[0] = static_cast<float>(extent.width);
            pc.screenSize[1] = static_cast<float>(extent.height);
            if (command.hasRoundedClip) {
                pc.roundedClipRect[0] = command.roundedClipLeft;
                pc.roundedClipRect[1] = command.roundedClipTop;
                pc.roundedClipRect[2] = command.roundedClipRight;
                pc.roundedClipRect[3] = command.roundedClipBottom;
                pc.roundedClipRadius[0] = command.roundedClipRadiusX;
                pc.roundedClipRadius[1] = command.roundedClipRadiusY;
                pc.clipFlags[0] = 1.0f;
            }
            if (command.hasInnerRoundedClip) {
                pc.innerRoundedClipRect[0] = command.innerRoundedClipLeft;
                pc.innerRoundedClipRect[1] = command.innerRoundedClipTop;
                pc.innerRoundedClipRect[2] = command.innerRoundedClipRight;
                pc.innerRoundedClipRect[3] = command.innerRoundedClipBottom;
                pc.innerRoundedClipRadius[0] = command.innerRoundedClipRadiusX;
                pc.innerRoundedClipRadius[1] = command.innerRoundedClipRadiusY;
                pc.clipFlags[1] = 1.0f;
            }
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS,
                (command.textRun.clearType && textClearTypePipeline != VK_NULL_HANDLE) ? textClearTypePipeline : textPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, textPipelineLayout, 0, 1, &textDescriptorSet, 0, nullptr);
            cmdPushConstants(commandBuffer, textPipelineLayout,
                             VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                             0, sizeof(pc), &pc);
            const uint32_t firstInstance = static_cast<uint32_t>(textRunOffsets[index] / (12u * sizeof(float)));
            cmdDraw(commandBuffer, 6, command.textRun.glyphCount, 0, firstInstance);
            cmdEndRenderPass(commandBuffer);
            continue;
        }
#endif

        if (command.kind == GpuReplayCommandKind::InkLayer) {
            // Composite a resident ink-layer image through the dedicated
            // premultiplied composite pipeline (the ink image is premultiplied;
            // the shared bitmapPipeline is straight-alpha and would darken it),
            // sampling it directly (no upload). A fresh descriptor set is taken
            // from this frame's ink pool each frame so it always points at the
            // ink layer's current view (recyclable handles → never cached).
            auto* ink = reinterpret_cast<VulkanInkLayerBitmap*>(command.inkLayer.inkLayer);
            if (!ink) continue;
            // Defense in depth behind TryRecordGpuInkLayerCommand's record-time
            // guard: never write a foreign/lost generation's view into this
            // device's descriptor set (cross-device handles are UB; the lost
            // case is the post-GPU-switch crash signature).
            if (ink->DeviceLost() || ink->DeviceHandle() != device) {
                continue;
            }
            VkImageView inkView = ink->ImageView();
            if (inkView == VK_NULL_HANDLE || frameSampler == VK_NULL_HANDLE ||
                inkCompositePipeline == VK_NULL_HANDLE || !resetDescriptorPool) {
                continue;
            }

            VkDescriptorPool& inkPool = inkBlitDescriptorPools[currentFrame_];
            if (inkPool == VK_NULL_HANDLE) {
                VkDescriptorPoolSize inkSizes[2]{};
                inkSizes[0].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
                inkSizes[0].descriptorCount = 32;
                inkSizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLER;
                inkSizes[1].descriptorCount = 32;
                VkDescriptorPoolCreateInfo inkPoolInfo{};
                inkPoolInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
                inkPoolInfo.maxSets = 32;
                inkPoolInfo.poolSizeCount = 2;
                inkPoolInfo.pPoolSizes = inkSizes;
                if (createDescriptorPool(device, &inkPoolInfo, nullptr, &inkPool) != VK_SUCCESS) {
                    inkPool = VK_NULL_HANDLE;
                    continue;
                }
            }
            if (!inkBlitPoolReset) {
                resetDescriptorPool(device, inkPool, 0);
                inkBlitPoolReset = true;
            }

            VkDescriptorSet inkSet = VK_NULL_HANDLE;
            VkDescriptorSetAllocateInfo inkAlloc{};
            inkAlloc.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
            inkAlloc.descriptorPool = inkPool;
            inkAlloc.descriptorSetCount = 1;
            inkAlloc.pSetLayouts = &frameDescriptorSetLayout;
            if (allocateDescriptorSets(device, &inkAlloc, &inkSet) != VK_SUCCESS || inkSet == VK_NULL_HANDLE) {
                continue;  // pool exhausted this frame — drop (cap is generous)
            }

            VkDescriptorImageInfo imgInfo{};
            imgInfo.imageView = inkView;
            imgInfo.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            VkDescriptorImageInfo sampInfo{};
            sampInfo.sampler = frameSampler;
            VkWriteDescriptorSet inkWrites[2]{};
            inkWrites[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            inkWrites[0].dstSet = inkSet;
            inkWrites[0].dstBinding = 0;
            inkWrites[0].descriptorCount = 1;
            inkWrites[0].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            inkWrites[0].pImageInfo = &imgInfo;
            inkWrites[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            inkWrites[1].dstSet = inkSet;
            inkWrites[1].dstBinding = 1;
            inkWrites[1].descriptorCount = 1;
            inkWrites[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
            inkWrites[1].pImageInfo = &sampInfo;
            updateDescriptorSets(device, 2, inkWrites, 0, nullptr);

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            BitmapQuadPushConstants pushConstants{};
            pushConstants.rect[0] = command.inkLayer.x;
            pushConstants.rect[1] = command.inkLayer.y;
            pushConstants.rect[2] = command.inkLayer.w;
            pushConstants.rect[3] = command.inkLayer.h;
            pushConstants.uvOpacity[0] = 1.0f;  // descriptor binds the whole ink image
            pushConstants.uvOpacity[1] = 1.0f;
            pushConstants.uvOpacity[2] = command.inkLayer.opacity;
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
            // Premultiplied composite pipeline (NOT the straight bitmapPipeline):
            // the ink image is premultiplied. uvOpacity[2] carries the layer
            // opacity; the ink FS scales all four channels by it so the premul
            // SrcOver blend (srcColorBlendFactor = ONE) is correct.
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, inkCompositePipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, bitmapPipelineLayout, 0, 1, &inkSet, 0, nullptr);
            cmdPushConstants(commandBuffer, bitmapPipelineLayout,
                             VK_SHADER_STAGE_VERTEX_BIT | VK_SHADER_STAGE_FRAGMENT_BIT,
                             0, sizeof(pushConstants), &pushConstants);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.kind == GpuReplayCommandKind::CustomShader) {
            const auto& cs = command.customShader;
            VkPipeline csPipeline = VK_NULL_HANDLE;
            {
                auto pit = customShaderPipelines.find(cs.shaderHash);
                if (pit != customShaderPipelines.end()) csPipeline = pit->second;
            }
            if (csPipeline == VK_NULL_HANDLE || customShaderPipelineLayout == VK_NULL_HANDLE ||
                frameSampler == VK_NULL_HANDLE || uploadImageView == VK_NULL_HANDLE || !resetDescriptorPool) {
                continue;
            }

            // Upload the cropped captured content to the shared upload image.
            VkImageMemoryBarrier toTransfer {};
            toTransfer.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            toTransfer.oldLayout = uploadImageLayout;
            toTransfer.newLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            toTransfer.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            toTransfer.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            toTransfer.image = uploadImage;
            toTransfer.subresourceRange = subresourceRange;
            toTransfer.srcAccessMask = uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_ACCESS_SHADER_READ_BIT : 0;
            toTransfer.dstAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            cmdPipelineBarrier(commandBuffer,
                uploadImageLayout == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL ? VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT : VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT,
                VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, nullptr, 0, nullptr, 1, &toTransfer);

            VkBufferImageCopy copy {};
            copy.bufferOffset = customShaderStagingBase + customShaderOffsets[index];
            copy.imageSubresource.aspectMask = VK_IMAGE_ASPECT_COLOR_BIT;
            copy.imageSubresource.layerCount = 1;
            copy.imageExtent = { cs.pixelWidth, cs.pixelHeight, 1 };
            cmdCopyBufferToImage(commandBuffer, stagingBuffer, uploadImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);

            VkImageMemoryBarrier toRead {};
            toRead.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
            toRead.srcAccessMask = VK_ACCESS_TRANSFER_WRITE_BIT;
            toRead.dstAccessMask = VK_ACCESS_SHADER_READ_BIT;
            toRead.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL;
            toRead.newLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            toRead.srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            toRead.dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED;
            toRead.image = uploadImage;
            toRead.subresourceRange = subresourceRange;
            cmdPipelineBarrier(commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, 0, 0, nullptr, 0, nullptr, 1, &toRead);
            uploadImageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;

            // Per-frame transient descriptor pool + user-constants UBO.
            VkDescriptorPool& csPool = customShaderDescPools[currentFrame_];
            if (csPool == VK_NULL_HANDLE) {
                VkDescriptorPoolSize csSizes[3]{};
                csSizes[0].type = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
                csSizes[0].descriptorCount = 32;
                csSizes[1].type = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
                csSizes[1].descriptorCount = 32;
                csSizes[2].type = VK_DESCRIPTOR_TYPE_SAMPLER;
                csSizes[2].descriptorCount = 32;
                VkDescriptorPoolCreateInfo cpInfo{};
                cpInfo.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO;
                cpInfo.maxSets = 32;
                cpInfo.poolSizeCount = 3;
                cpInfo.pPoolSizes = csSizes;
                if (createDescriptorPool(device, &cpInfo, nullptr, &csPool) != VK_SUCCESS) { csPool = VK_NULL_HANDLE; continue; }
            }
            if (!customShaderPoolReset) {
                resetDescriptorPool(device, csPool, 0);
                customConstantsCursor = 0;
                customShaderPoolReset = true;
            }

            // Ensure the per-frame constants UBO (host-visible). One generous
            // allocation reused across frames; the cursor sub-allocates per draw.
            VkBuffer&       cBuf = customShaderConstantsBuffers[currentFrame_];
            VkDeviceMemory& cMem = customShaderConstantsMemory[currentFrame_];
            void*&          cMap = customShaderConstantsMapped[currentFrame_];
            VkDeviceSize&   cCap = customShaderConstantsCapacity[currentFrame_];
            const VkDeviceSize constBytes = std::max<VkDeviceSize>(16, cs.constants.size() * sizeof(float));
            const VkDeviceSize needed = ((customConstantsCursor + constBytes + kCustomConstantsAlign - 1) / kCustomConstantsAlign) * kCustomConstantsAlign;
            if (cBuf == VK_NULL_HANDLE || cCap < std::max<VkDeviceSize>(needed, 65536)) {
                if (cMap) { unmapMemory(device, cMem); cMap = nullptr; }
                if (cBuf != VK_NULL_HANDLE) { destroyBuffer(device, cBuf, nullptr); cBuf = VK_NULL_HANDLE; }
                if (cMem != VK_NULL_HANDLE) { freeMemory(device, cMem, nullptr); cMem = VK_NULL_HANDLE; }
                const VkDeviceSize cap = std::max<VkDeviceSize>(needed, 65536);
                VkBufferCreateInfo bInfo{};
                bInfo.sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO;
                bInfo.size = cap;
                bInfo.usage = VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT;
                bInfo.sharingMode = VK_SHARING_MODE_EXCLUSIVE;
                if (createBuffer(device, &bInfo, nullptr, &cBuf) != VK_SUCCESS) { cBuf = VK_NULL_HANDLE; continue; }
                VkMemoryRequirements mr{};
                getBufferMemoryRequirements(device, cBuf, &mr);
                uint32_t ti = FindMemoryType(mr.memoryTypeBits, VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT);
                VkMemoryAllocateInfo ai{};
                ai.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
                ai.allocationSize = mr.size;
                ai.memoryTypeIndex = ti;
                if (allocateMemory(device, &ai, nullptr, &cMem) != VK_SUCCESS) { destroyBuffer(device, cBuf, nullptr); cBuf = VK_NULL_HANDLE; continue; }
                bindBufferMemory(device, cBuf, cMem, 0);
                mapMemory(device, cMem, 0, VK_WHOLE_SIZE, 0, &cMap);
                cCap = cap;
            }
            const VkDeviceSize constOffset = ((customConstantsCursor + kCustomConstantsAlign - 1) / kCustomConstantsAlign) * kCustomConstantsAlign;
            if (cMap) {
                std::memset(static_cast<uint8_t*>(cMap) + constOffset, 0, static_cast<size_t>(constBytes));
                if (!cs.constants.empty()) {
                    std::memcpy(static_cast<uint8_t*>(cMap) + constOffset, cs.constants.data(), cs.constants.size() * sizeof(float));
                }
            }
            customConstantsCursor = constOffset + constBytes;

            VkDescriptorSet csSet = VK_NULL_HANDLE;
            VkDescriptorSetAllocateInfo dsAlloc{};
            dsAlloc.sType = VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO;
            dsAlloc.descriptorPool = csPool;
            dsAlloc.descriptorSetCount = 1;
            dsAlloc.pSetLayouts = &customShaderDescLayout;
            if (allocateDescriptorSets(device, &dsAlloc, &csSet) != VK_SUCCESS || csSet == VK_NULL_HANDLE) continue;

            VkDescriptorBufferInfo bInfoW{ cBuf, constOffset, constBytes };
            VkDescriptorImageInfo imgInfoW{};
            imgInfoW.imageView = uploadImageView;
            imgInfoW.imageLayout = VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL;
            VkDescriptorImageInfo sampInfoW{};
            sampInfoW.sampler = frameSampler;
            VkWriteDescriptorSet csWrites[3]{};
            csWrites[0].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            csWrites[0].dstSet = csSet;
            csWrites[0].dstBinding = VulkanShaderCompiler::kBShift + 0;
            csWrites[0].descriptorCount = 1;
            csWrites[0].descriptorType = VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
            csWrites[0].pBufferInfo = &bInfoW;
            csWrites[1].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            csWrites[1].dstSet = csSet;
            csWrites[1].dstBinding = VulkanShaderCompiler::kTShift + 0;
            csWrites[1].descriptorCount = 1;
            csWrites[1].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLED_IMAGE;
            csWrites[1].pImageInfo = &imgInfoW;
            csWrites[2].sType = VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET;
            csWrites[2].dstSet = csSet;
            csWrites[2].dstBinding = VulkanShaderCompiler::kSShift + 0;
            csWrites[2].descriptorCount = 1;
            csWrites[2].descriptorType = VK_DESCRIPTOR_TYPE_SAMPLER;
            csWrites[2].pImageInfo = &sampInfoW;
            updateDescriptorSets(device, 3, csWrites, 0, nullptr);

            beginLoadRenderPass();
            cmdSetScissor(commandBuffer, 0, 1, &commandScissor);
            CustomShaderGeomPushConstants geom{};
            geom.rect[0] = cs.x; geom.rect[1] = cs.y; geom.rect[2] = cs.w; geom.rect[3] = cs.h;
            geom.screenSize[0] = static_cast<float>(extent.width);
            geom.screenSize[1] = static_cast<float>(extent.height);
            geom.uvScale[0] = uploadWidth == 0 ? 1.0f : static_cast<float>(cs.pixelWidth) / static_cast<float>(uploadWidth);
            geom.uvScale[1] = uploadHeight == 0 ? 1.0f : static_cast<float>(cs.pixelHeight) / static_cast<float>(uploadHeight);
            cmdBindPipeline(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, csPipeline);
            cmdBindDescriptorSets(commandBuffer, VK_PIPELINE_BIND_POINT_GRAPHICS, customShaderPipelineLayout, 0, 1, &csSet, 0, nullptr);
            cmdPushConstants(commandBuffer, customShaderPipelineLayout, VK_SHADER_STAGE_VERTEX_BIT, 0, sizeof(geom), &geom);
            cmdDraw(commandBuffer, 6, 1, 0, 0);
            cmdEndRenderPass(commandBuffer);
            continue;
        }

        if (command.bitmap.pixelWidth == 0 || command.bitmap.pixelHeight == 0 || command.bitmap.GetPixels().empty()) {
#ifdef __ANDROID__
            VK_LOG("[RPLY] BITMAP ABORT frame at #%zu pw=%u ph=%u empty=%d", index, command.bitmap.pixelWidth, command.bitmap.pixelHeight, (int)command.bitmap.GetPixels().empty());
#endif
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

    // AFTER all GPU-replay commands (the command buffer is outside a render pass
    // here): either run the real Vello GPU compute graph + composite its output
    // image, or drain Impeller / Vello CPU-tessellation batches — both paint
    // over the existing framebuffer contents via their own load-op render pass.
    if (velloScene && velloCompute_ && EnsureVelloCompositePipeline() &&
        velloCompute_->Record(commandBuffer, *velloScene, currentFrame_)) {
        CompositeVelloOutput(commandBuffer, velloCompute_->OutputView(),
                             velloCompute_->OutputSampler(), extent, currentFrame_,
                             frameRenderPass, framebuffers[imageIndex]);
    } else if (engineBatches && !engineBatches->empty()) {
        RenderEngineBatches(commandBuffer, *engineBatches, extent,
                            currentFrame_, frameRenderPass, framebuffers[imageIndex]);
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

    // Final timestamp + finalize the per-frame timing record so the next
    // BeginFrame's DecodeGpuTimingForCompletedFrame knows to resolve.
    // batchCount is the engine-batch count when one of the path engines
    // (Impeller / Vello) drained into this frame, plus the GPU-replay
    // command count — both are user-visible "draws per frame".
    if (timingSupported && cmdWriteTimestamp && timingQueryPool != VK_NULL_HANDLE) {
        MarkGpuTimingPoint(GpuTimingCategory::kFrameEnd);
        auto& tf = timing[currentFrame_];
        tf.hasResolvedData = true;
        uint32_t batchCount = static_cast<uint32_t>(commands.size());
        if (engineBatches) {
            batchCount += static_cast<uint32_t>(engineBatches->size());
        }
        tf.batchCountAtFinalize = batchCount;
    }

    if (NoteVk(endCommandBuffer(commandBuffer), "DrawReplayFrame.endCommandBuffer") != VK_SUCCESS) {
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
    // Deferred fence reset — see DrawFrame: nothing between here and
    // queueSubmit can fail, and the residual queueSubmit-failure hole is
    // closed by the fencePending gate on the slot's wait.
    if (NoteVk(resetFences(device, 1, &inFlight), "DrawReplayFrame.resetFences") != VK_SUCCESS) {
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
    if (NoteVk(queueSubmit(queue, 1, &submitInfo, inFlight), "DrawReplayFrame.queueSubmit") != VK_SUCCESS) {
        // fencePending stays false — see DrawFrame's queueSubmit failure note.
        EndFrame();
        return false;
    }
    fencePending = true;
    // See DrawFrame queueSubmit — same present→ready stamp.
    lastSubmitMonotonicNs = MonotonicNowNs();

    VkPresentInfoKHR presentInfo {};
    presentInfo.sType = VK_STRUCTURE_TYPE_PRESENT_INFO_KHR;
    presentInfo.waitSemaphoreCount = 1;
    presentInfo.pWaitSemaphores = &signalSemaphore;
    presentInfo.swapchainCount = 1;
    presentInfo.pSwapchains = &swapchain;
    presentInfo.pImageIndices = &imageIndex;
    const VkResult presentResult = NoteVk(queuePresent(queue, &presentInfo), "DrawReplayFrame.queuePresent");
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
    // Skip the drain only when the VkDevice itself is gone (deviceGone): on a
    // truly lost device there is no in-flight work left and the wait would
    // only poke the torn-down driver — the D3D12 rule of skipping even Close
    // on a removed device. A surface-lost-only RT (deviceLost && !deviceGone)
    // sits on a HEALTHY device that may still be executing the last
    // successfully submitted frames, so it MUST drain before the destroys
    // below — otherwise this teardown would free in-flight resources, the
    // exact driver-UAF class this hardening removes. NoteVk catches the case
    // where the device died unobserved since the last frame, so the ink
    // destructors that follow (via the generation flag) skip their waits.
    if (!deviceGone && deviceWaitIdle && device != VK_NULL_HANDLE) {
        NoteVk(deviceWaitIdle(device), "Destroy.deviceWaitIdle");
    }

    if (device != VK_NULL_HANDLE) {
        // Release the Vello GPU compute pipeline + its composite objects while
        // the device is still alive. VulkanRenderTarget declares the engine
        // unique_ptrs before impl_, so impl_ (this) tears down first — doing the
        // GPU release here (rather than relying on member-destruction order)
        // closes the latent use-after-device-destroy the header warns about.
        velloCompute_.reset();
        {
            auto destroyPipeline = LoadDeviceProc<PFN_vkDestroyPipeline>(getDeviceProcAddr, device, "vkDestroyPipeline");
            auto destroyPipelineLayout = LoadDeviceProc<PFN_vkDestroyPipelineLayout>(getDeviceProcAddr, device, "vkDestroyPipelineLayout");
            auto destroyDescriptorSetLayout = LoadDeviceProc<PFN_vkDestroyDescriptorSetLayout>(getDeviceProcAddr, device, "vkDestroyDescriptorSetLayout");
            auto destroyDescriptorPool = LoadDeviceProc<PFN_vkDestroyDescriptorPool>(getDeviceProcAddr, device, "vkDestroyDescriptorPool");
            if (destroyPipeline && velloCompositePipeline) {
                destroyPipeline(device, velloCompositePipeline, nullptr);
                velloCompositePipeline = VK_NULL_HANDLE;
            }
            if (destroyPipelineLayout && velloCompositePipelineLayout) {
                destroyPipelineLayout(device, velloCompositePipelineLayout, nullptr);
                velloCompositePipelineLayout = VK_NULL_HANDLE;
            }
            if (destroyDescriptorSetLayout && velloCompositeDescSetLayout) {
                destroyDescriptorSetLayout(device, velloCompositeDescSetLayout, nullptr);
                velloCompositeDescSetLayout = VK_NULL_HANDLE;
            }
            if (destroyDescriptorPool) {
                for (auto& pool : velloCompositeDescPools) {
                    if (pool) { destroyDescriptorPool(device, pool, nullptr); pool = VK_NULL_HANDLE; }
                }
            }
        }
        DestroyGraphicsResources();
        DestroyTransitionImages();
#ifdef _WIN32
        // The text glyph atlas VkImage is a single instance tied to the device
        // (not per-frame, not recreated on swapchain resize), so it is torn down
        // once here — parallel to DestroyTransitionImages for the transition
        // single-instance images. DestroyGraphicsResources above already freed the
        // text pipeline / descriptors.
        DestroyTextAtlasImage();
        DestroyTextAtlasStaging();
        glyphAtlas_.reset();
#endif
        // DestroyPerFrameResources releases the per-frame command buffers (via the
        // command pool below), fences, imageAvailable semaphores, staging buffers,
        // upload images, and renderFinishedPerImage semaphores. It must run before
        // the command pool and descriptor pool are destroyed because it relies on
        // them still being valid.
        DestroyPerFrameResources();
        if (destroyQueryPool && timingQueryPool != VK_NULL_HANDLE) {
            destroyQueryPool(device, timingQueryPool, nullptr);
            timingQueryPool = VK_NULL_HANDLE;
        }
        if (destroyCommandPool && commandPool != VK_NULL_HANDLE) {
            destroyCommandPool(device, commandPool, nullptr);
        }
        if (destroySwapchain && swapchain != VK_NULL_HANDLE) {
            destroySwapchain(device, swapchain, nullptr);
        }
        // Device destruction is deferred to the generation token (released
        // below, after the surface — vkDestroySurfaceKHR needs the instance
        // alive). Only the pre-generation init-failure window still destroys
        // the device directly.
        if (!deviceGeneration && destroyDevice) {
            destroyDevice(device, nullptr);
        }
    }

    if (destroySurface && surface != VK_NULL_HANDLE && instance != VK_NULL_HANDLE) {
        destroySurface(instance, surface, nullptr);
    }
    // Unregister from the backend BEFORE dropping our reference: if this RT's
    // generation is the registered one, the backend releases its pin so the
    // device can actually die below and no future ink layer lands on a closed
    // window's device.
    if (ownerBackend && deviceGeneration) {
        ownerBackend->UnregisterDeviceContext(deviceGeneration);
    }
    ownerBackend = nullptr;
    if (deviceGeneration) {
        // Drop this RT's reference. If no backend-owned ink object co-owns the
        // generation this destroys device + instance right here (the historic
        // behavior); otherwise both stay alive until the last ink object dies,
        // so its destructor never feeds handles of a freed device into the
        // driver — the cross-generation crash chain of the GPU-switch bug.
        deviceGeneration.reset();
    } else if (destroyInstance && instance != VK_NULL_HANDLE) {
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
    inkCompositePipeline = VK_NULL_HANDLE;
#ifdef _WIN32
    textPipelineLayout = VK_NULL_HANDLE;
    textPipeline = VK_NULL_HANDLE;
    textClearTypePipeline = VK_NULL_HANDLE;
    textDescriptorSetLayout = VK_NULL_HANDLE;
    textDescriptorPool = VK_NULL_HANDLE;
    textDescriptorSet = VK_NULL_HANDLE;
    textAtlasImage = VK_NULL_HANDLE;
    textAtlasImageMemory = VK_NULL_HANDLE;
    textAtlasImageView = VK_NULL_HANDLE;
    textAtlasImageLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    textAtlasWidth = 0;
    textAtlasHeight = 0;
    textAtlasStagingBuffer = VK_NULL_HANDLE;
    textAtlasStagingMemory = VK_NULL_HANDLE;
    textAtlasStagingMapped = nullptr;
    textAtlasStagingCapacity = 0;
#endif
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
    vcTrianglePipelineLayout = VK_NULL_HANDLE;
    vcTrianglePipeline = VK_NULL_HANDLE;
    engineBatchPipelineLayout = VK_NULL_HANDLE;
    engineBatchPipeline = VK_NULL_HANDLE;
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

void VulkanRenderTarget::SyncClipToEngine(IRenderingEngine* engine) const
{
    if (!engine) return;

    // Empty clip stack means no scissor. Clear unconditionally first so a
    // scissor left set by a previous primitive can never bleed into this batch
    // (the engine's hasScissor_ is sticky — SetScissorRect latches it until
    // cleared).
    if (clipStack_.empty()) {
        engine->ClearScissorRect();
        return;
    }

    // Reuse the exact same AABB resolution the SolidRect GPU-replay path uses,
    // so engine geometry is clipped to byte-identical pixel bounds. TryPopulate-
    // ReplayClip bails (returns false) on rotation / skew / non-invertible
    // transform / more-than-one rounded clip — in every such case we fall back
    // to an unclipped batch, matching the pre-existing behaviour. (It also fills
    // the rounded-clip fields, but the engine batch shader has no rounded-clip
    // channel yet — AABB scissor only for now.)
    GpuReplayCommand probe;
    probe.kind = GpuReplayCommandKind::SolidRect;
    if (TryPopulateReplayClip(probe) && probe.hasScissor) {
        engine->SetScissorRect(
            static_cast<float>(probe.scissorLeft),
            static_cast<float>(probe.scissorTop),
            static_cast<float>(probe.scissorRight),
            static_cast<float>(probe.scissorBottom));
    } else {
        engine->ClearScissorRect();
    }
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
                // The GPU push-constant path (bitmap / SolidRect fragment shaders)
                // carries only ONE rounded clip. Previously a second rounded clip
                // made this return false, and every caller treats false as "drop
                // this primitive". So any content under TWO nested rounded clips
                // -- e.g. an Image inside a rounded card whose header Border is
                // also rounded (rounded RootBorder + rounded HeaderBorder) --
                // vanished entirely (the Jalium.One welcome-page template card
                // thumbnails were blank for exactly this reason).
                //
                // Degrade gracefully instead of dropping: the rectangular AABB
                // intersection is already accumulated above for EVERY clip
                // (including this one), so the content stays correctly clipped to
                // the rectangle; we simply keep the first rounded clip's corner
                // rounding and let any further rounded clips contribute their AABB
                // only. Visible result: content shows, bounded by the true
                // rectangle, with one level of corner rounding -- vs disappearing.
                continue;
            }

            command.hasRoundedClip = true;
            command.roundedClipLeft = clipWorldLeft;
            command.roundedClipTop = clipWorldTop;
            command.roundedClipRight = clipWorldRight;
            command.roundedClipBottom = clipWorldBottom;
            // GPU push-constant path is single-radius (rx, ry) for now; collapse
            // the four corners to their maximum so the visible content is at
            // least bounded by the largest corner.  CPU IsInsideClip performs
            // the precise per-corner test below — that's the path Border content
            // actually exercises today.  Per-corner GPU support would require
            // expanding the push-constant block in every Vulkan fragment shader.
            float rMax = std::max({ clip.radiusTL, clip.radiusTR, clip.radiusBR, clip.radiusBL });
            command.roundedClipRadiusX = std::fabs(clip.transform.m11) * std::min(rMax, clip.w * 0.5f);
            command.roundedClipRadiusY = std::fabs(clip.transform.m22) * std::min(rMax, clip.h * 0.5f);
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

        // Per-corner radii cap to half-min so a single corner can't exceed the
        // available room — matches managed-side NormalizePerCornerRadii.
        const float halfMin = std::min(clip.w, clip.h) * 0.5f;
        const float rTL = std::min(clip.radiusTL, halfMin);
        const float rTR = std::min(clip.radiusTR, halfMin);
        const float rBR = std::min(clip.radiusBR, halfMin);
        const float rBL = std::min(clip.radiusBL, halfMin);
        if (rTL <= 0.0f && rTR <= 0.0f && rBR <= 0.0f && rBL <= 0.0f) {
            continue;
        }

        const float left = clip.x;
        const float top = clip.y;
        const float right = clip.x + clip.w;
        const float bottom = clip.y + clip.h;

        bool insideRounded = true;
        if (localX < left + rTL && localY < top + rTL && rTL > 0.0f) {
            const float dx = (localX - (left + rTL)) / rTL;
            const float dy = (localY - (top + rTL)) / rTL;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX > right - rTR && localY < top + rTR && rTR > 0.0f) {
            const float dx = (localX - (right - rTR)) / rTR;
            const float dy = (localY - (top + rTR)) / rTR;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX < left + rBL && localY > bottom - rBL && rBL > 0.0f) {
            const float dx = (localX - (left + rBL)) / rBL;
            const float dy = (localY - (bottom - rBL)) / rBL;
            insideRounded = (dx * dx + dy * dy) <= 1.0f;
        } else if (localX > right - rBR && localY > bottom - rBR && rBR > 0.0f) {
            const float dx = (localX - (right - rBR)) / rBR;
            const float dy = (localY - (bottom - rBR)) / rBR;
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

bool VulkanRenderTarget::BuildEngineBrush(Brush* brush, float opacity,
                                          EngineBrushData& bd,
                                          std::vector<EngineBrushData::GradientStop>& stopStore) const {
    if (!brush) return false;
    switch (brush->GetType()) {
        case JALIUM_BRUSH_SOLID: {
            auto* s = static_cast<VulkanSolidBrush*>(brush);
            bd.type = 0;
            bd.r = s->r_; bd.g = s->g_; bd.b = s->b_;
            bd.a = s->a_ * opacity;
            return true;
        }
        case JALIUM_BRUSH_LINEAR_GRADIENT: {
            auto* lg = static_cast<VulkanLinearGradientBrush*>(brush);
            if (lg->stops_.empty()) return false;
            bd.type = 1;
            bd.startX = lg->startX_; bd.startY = lg->startY_;
            bd.endX   = lg->endX_;   bd.endY   = lg->endY_;
            stopStore.clear();
            stopStore.reserve(lg->stops_.size());
            // Straight stop colors; opacity folded into the stop alpha (the engine
            // premultiplies the sampled alpha at the vertex).
            for (const auto& st : lg->stops_)
                stopStore.push_back({ st.position, st.r, st.g, st.b, st.a * opacity });
            bd.stops = stopStore.data();
            bd.stopCount = (uint32_t)stopStore.size();
            return true;
        }
        case JALIUM_BRUSH_RADIAL_GRADIENT: {
            auto* rg = static_cast<VulkanRadialGradientBrush*>(brush);
            if (rg->stops_.empty()) return false;
            bd.type = 2;
            bd.centerX = rg->centerX_; bd.centerY = rg->centerY_;
            bd.radiusX = rg->radiusX_; bd.radiusY = rg->radiusY_;
            bd.originX = rg->originX_; bd.originY = rg->originY_;
            stopStore.clear();
            stopStore.reserve(rg->stops_.size());
            for (const auto& st : rg->stops_)
                stopStore.push_back({ st.position, st.r, st.g, st.b, st.a * opacity });
            bd.stops = stopStore.data();
            bd.stopCount = (uint32_t)stopStore.size();
            return true;
        }
        default:
            return false;   // image / unsupported
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
            // sharedTriangleVertices carries vertices in local (pre-transform)
            // space and the affine transform travels in transformRow0/1; we
            // must apply it before rasterizing to the CPU canvas. The legacy
            // pre-transformed path (rasterize-fallback) leaves the transform
            // at identity, so the same code handles both.
            const auto& src = p.GetTriangleVertices();
            std::vector<float> worldVerts;
            worldVerts.reserve(src.size());
            for (size_t i = 0; i + 1 < src.size(); i += 2) {
                const float lx = src[i], ly = src[i + 1];
                const float wx = p.transformRow0[0] * lx + p.transformRow0[1] * ly + p.transformRow0[2];
                const float wy = p.transformRow1[0] * lx + p.transformRow1[1] * ly + p.transformRow1[2];
                worldVerts.push_back(wx);
                worldVerts.push_back(wy);
            }
            RasterizePolygon(worldVerts, 0,
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
        case GpuReplayCommandKind::VcTriangles:
            // DrawLine 3-strip AA path: the matching CPU-side draw is the
            // unconditional StrokePolyline at the end of DrawLine, which
            // self-guards on cpuRasterNeeded_ and runs through pixelBuffer_
            // directly when CPU fallback is active. Nothing to replay here.
            break;
        case GpuReplayCommandKind::InkLayer:
            // The CPU-fallback composite (image readback + BlendBuffer) is done
            // inline in BlitInkLayer when cpuRasterNeeded_ is set, so the
            // committed ink is already in pixelBuffer_. Nothing to replay here.
            break;
        case GpuReplayCommandKind::TextRun:
            // GPU-only text path (Windows DirectWrite glyph pipeline). The CPU
            // replay path does not rasterize glyphs — text falls back to the
            // GDI/FreeType bitmap path when cpuRasterNeeded_ is set. Nothing to
            // replay here; the case keeps the switch exhaustive (no -Wswitch).
            break;
        case GpuReplayCommandKind::OffscreenBegin:
        case GpuReplayCommandKind::OffscreenEnd:
            // env-gated GPU offscreen effect RT markers. No CPU-replay meaning
            // (the CPU fallback rasterizes into pixelBuffer_ directly); keep the
            // switch exhaustive (no -Wswitch).
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

    // Two-tier triangulation matching D3D12 FillPolygon's fallback chain:
    //   1. Core's TriangulatePolygonRobust (jalium_triangulate.h) — same
    //      algorithm D3D12 calls. Cascades through fan / Y-monotone /
    //      improved ear-clip; returns index buffer.
    //   2. If robust fails, fall back to local TriangulateSimplePolygon
    //      (the pure ear-clip already in this TU). Most paths will hit
    //      tier 1; tier 2 is a safety net for shapes that even robust
    //      bails on.
    std::vector<float> triangleVertices;
    {
        path_stats::ScopedTriangulateTimer triTimer;
        const uint32_t pointCount = static_cast<uint32_t>(points.size() / 2);
        std::vector<uint32_t> indices;
        bool ok = TriangulatePolygonRobust(points.data(), pointCount, indices)
                  && indices.size() >= 3;
        if (ok) {
            triangleVertices.reserve(indices.size() * 2);
            for (uint32_t idx : indices) {
                triangleVertices.push_back(points[idx * 2]);
                triangleVertices.push_back(points[idx * 2 + 1]);
            }
            triTimer.MarkOk();
        } else {
            ok = TriangulateSimplePolygon(points, triangleVertices);
            if (ok) triTimer.MarkOk();
        }
        if (!ok) return false;
    }

    return TryRecordPreTriangulatedFilledPolygon(std::move(triangleVertices), brush);
}

bool VulkanRenderTarget::TryRecordSharedLocalFilledPolygon(
    std::shared_ptr<const std::vector<float>> sharedLocalTriangles,
    const CpuTransform& transform,
    Brush* brush)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || !sharedLocalTriangles ||
        sharedLocalTriangles->size() < 6) {
        return false;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
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

    replayCommand.filledPolygon.sharedTriangleVertices = std::move(sharedLocalTriangles);
    replayCommand.filledPolygon.transformRow0[0] = transform.m11;
    replayCommand.filledPolygon.transformRow0[1] = transform.m12;
    replayCommand.filledPolygon.transformRow0[2] = transform.dx;
    replayCommand.filledPolygon.transformRow0[3] = 0.0f;
    replayCommand.filledPolygon.transformRow1[0] = transform.m21;
    replayCommand.filledPolygon.transformRow1[1] = transform.m22;
    replayCommand.filledPolygon.transformRow1[2] = transform.dy;
    replayCommand.filledPolygon.transformRow1[3] = 0.0f;
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

bool VulkanRenderTarget::TryRecordPreTriangulatedFilledPolygon(std::vector<float>&& worldTriangles, Brush* brush)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || worldTriangles.size() < 6) {
        return false;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
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

    replayCommand.filledPolygon.triangleVertices = std::move(worldTriangles);
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

void VulkanRenderTarget::DecomposePathToLocalPoints(float startX, float startY,
                                                    const float* commands,
                                                    uint32_t commandLength,
                                                    std::vector<float>& outLocalPoints,
                                                    float scaleFactor)
{
    outLocalPoints.clear();
    if (!commands || commandLength == 0) return;
    outLocalPoints.reserve(static_cast<size_t>(commandLength) * 2u);

    // Delegate per-command flattening to the shared core utilities used by
    // D3D12's CPU fallback (jalium_triangulate.h::DispatchPathCommand). The
    // shared path covers LineTo / CubicTo (adaptive subdivision) / QuadTo
    // (adaptive) / **ArcTo** (FlattenSvgArc) / ClosePath uniformly — Vulkan's
    // hand-rolled loop previously dropped ArcTo entirely (`break` on tag 4),
    // which truncated any SVG icon containing an arc.
    //
    // Tolerance: smaller value → denser flattening → more vertices but
    // smoother curves. We match the D3D12 fallback's 0.5px tolerance at
    // baseline scale and tighten proportionally on zoom-in to keep per-pixel
    // vertex density roughly flat (4× scale → 0.125px tolerance ≈ 4× density).
    // Caps at 0.05px so extreme zoom doesn't explode the downstream ear-clip
    // (O(N²)+, see project_vulkan_path_cache).
    const float tolerance = std::max(0.05f, 0.5f / std::max(1.0f, scaleFactor));

    float currentX = startX;
    float currentY = startY;
    float subpathStartX = startX;
    float subpathStartY = startY;
    outLocalPoints.push_back(currentX);
    outLocalPoints.push_back(currentY);

    uint32_t i = 0;
    bool seenMoveTo = false;
    while (i < commandLength) {
        const int tag = static_cast<int>(commands[i]);
        if (tag == kTagMoveTo && i + 2 < commandLength) {
            const float targetX = commands[i + 1];
            const float targetY = commands[i + 2];
            const bool moveJumpsAway = std::abs(targetX - currentX) > 1e-4f
                                     || std::abs(targetY - currentY) > 1e-4f;
            // True new sub-path = an already-emitted contour, followed by a
            // MoveTo that lifts the pen to a different point. The single-
            // polygon ear-clip path can't express disjoint contours, so
            // signal multi-contour by clearing the output; the caller
            // (FillPath) falls back to FlattenPathToContours +
            // TriangulateCompoundPath. Redundant MoveTo at the very start
            // (path opens with an explicit MoveTo that happens to match
            // startX/startY) is consumed in place, NOT treated as multi-contour.
            if (seenMoveTo && moveJumpsAway) {
                outLocalPoints.clear();
                return;
            }
            currentX = targetX;
            currentY = targetY;
            subpathStartX = currentX;
            subpathStartY = currentY;
            if (!seenMoveTo) {
                // Replace the implicit start we pushed above so it tracks the
                // path's explicit MoveTo target (the more common shape for
                // SVG-style command streams).
                outLocalPoints[0] = currentX;
                outLocalPoints[1] = currentY;
            }
            seenMoveTo = true;
            i += 3;
        } else if (tag == kTagClosePath) {
            // Line back to sub-path start if not already there.
            if (std::abs(currentX - subpathStartX) > 1e-4f
                || std::abs(currentY - subpathStartY) > 1e-4f) {
                outLocalPoints.push_back(subpathStartX);
                outLocalPoints.push_back(subpathStartY);
            }
            currentX = subpathStartX;
            currentY = subpathStartY;
            i += 1;
        } else {
            uint32_t consumed = DispatchPathCommand(commands, i, commandLength,
                                                    currentX, currentY,
                                                    outLocalPoints, tolerance);
            if (consumed == 0) break;
            i += consumed;
        }
    }
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
                                                int width, int height, float x, float y,
                                                float destScale)
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

    // Local-space (base-DIP) quad size. When the bitmap was rasterized at a
    // magnified em (destScale < 1), shrink the quad so the current transform's
    // scale re-magnifies it to the right on-screen size, sampling the hi-res
    // texture instead of stretching a base-size one (the scaled-text mosaic fix).
    if (!(destScale > 0.0f)) destScale = 1.0f;
    const float fw = static_cast<float>(width) * destScale;
    const float fh = static_cast<float>(height) * destScale;
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

    // Straight (unpremultiplied) BGRA. DrawReplayFrame's bitmap pipeline
    // blends with VK_BLEND_FACTOR_SRC_ALPHA / VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA,
    // and CPU replay's BlendBuffer → BlendPixel does the same straight-alpha
    // SrcOver, so we must store the color channels unmodified and let the
    // blend pipeline multiply by srcA once. Premultiplying here would square
    // alpha and make fallback shapes noticeably dimmer than normal fills.
    const uint8_t srcA = effA;

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
            // Straight BGRA — alpha is applied once by the blend pipeline.
            dst[0] = b;
            dst[1] = g;
            dst[2] = r;
            dst[3] = srcA;
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
    // The opacity stack is already baked into srcA, so the per-bitmap
    // opacity must be 1.0 to avoid multiplying alpha twice at blend time.
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
    // Straight (unpremultiplied) BGRA — the GPU bitmap pipeline and CPU
    // BlendBuffer both apply SrcOver with src * srcA, so we must NOT
    // premultiply here. See RasterizePolygonToGpuBitmap for details.
    const uint8_t srcA = effA;

    auto stamp = [&](int cx, int cy) {
        const int x0 = std::max(bboxLeft,      cx - halfThick);
        const int y0 = std::max(bboxTop,       cy - halfThick);
        const int x1 = std::min(bboxRight,     cx - halfThick + thickness);
        const int y1 = std::min(bboxBottom,    cy - halfThick + thickness);
        for (int py = y0; py < y1; ++py) {
            uint8_t* row = buffer.data() + static_cast<size_t>(py - bboxTop) * stride;
            for (int px = x0; px < x1; ++px) {
                uint8_t* dst = row + static_cast<size_t>(px - bboxLeft) * 4u;
                dst[0] = b;
                dst[1] = g;
                dst[2] = r;
                dst[3] = srcA;
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

bool VulkanRenderTarget::TryRecordGpuPixelBufferCommandShared(std::shared_ptr<const std::vector<uint8_t>> pixels, uint32_t pixelWidth, uint32_t pixelHeight, float x, float y, float w, float h, float opacity, bool opacityAlreadyBaked)
{
    // shared_ptr fast path: pin the source buffer by reference instead of
    // vector-copying. Saves ~pixelWidth*pixelHeight*4 bytes per DrawBitmap
    // call. The caller (VulkanBitmap-backed DrawBitmap) guarantees the
    // pointed-to buffer is immutable while in flight via copy-on-write
    // semantics in UpdatePackedPixels.
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || !pixels || pixels->empty() ||
        pixelWidth == 0 || pixelHeight == 0 || opacity <= 0.0f || w == 0.0f || h == 0.0f) {
        return false;
    }

    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f, p0y = 0.0f, p1x = 0.0f, p1y = 0.0f;
    float p2x = 0.0f, p2y = 0.0f, p3x = 0.0f, p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Bitmap;
    replayCommand.bitmap.pixelWidth = pixelWidth;
    replayCommand.bitmap.pixelHeight = pixelHeight;
    replayCommand.bitmap.sharedPixels = std::move(pixels);
    replayCommand.bitmap.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.bitmap.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.bitmap.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.bitmap.x;
    replayCommand.bitmap.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.bitmap.y;
    // When the source alpha already includes the opacity stack (text glyph
    // bitmaps), record the per-bitmap opacity verbatim — multiplying by
    // GetCurrentOpacity() here would square it and fade pre-baked content
    // toward invisibility during opacity animations.
    replayCommand.bitmap.opacity = opacityAlreadyBaked
        ? std::clamp(opacity, 0.0f, 1.0f)
        : std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f);
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
        replayCommand.quadPoint0X = p0x; replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x; replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x; replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x; replayCommand.quadPoint3Y = p3y;
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

bool VulkanRenderTarget::TryRecordGpuLiveBlurCommand(float x, float y, float w, float h, float radius, float opacity, bool alphaOnlyTint, float tintR, float tintG, float tintB, float tintA)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || w == 0.0f || h == 0.0f) {
        return false;
    }
    // The element already rendered into the frame on the GPU replay path, so we
    // sample its OWN screen region from the live frame at replay time — recording
    // during an active element capture would sample content not yet drawn, so the
    // effectCaptureStack_ / transition guards still apply.
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x = 0.0f, p0y = 0.0f, p1x = 0.0f, p1y = 0.0f, p2x = 0.0f, p2y = 0.0f, p3x = 0.0f, p3y = 0.0f;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::Blur;
    replayCommand.blur.captureLiveScene = true;  // source from the live frame, not CPU pixels
    replayCommand.blur.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    replayCommand.blur.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    replayCommand.blur.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - replayCommand.blur.x;
    replayCommand.blur.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - replayCommand.blur.y;
    // The replay blits this screen rect 1:1 into uploadImage[0,pixelWidth]x[0,pixelHeight];
    // the blur shader's UV (corners * pixelWidth/uploadWidth) then samples exactly it.
    replayCommand.blur.pixelWidth = static_cast<uint32_t>(std::lround(replayCommand.blur.w));
    replayCommand.blur.pixelHeight = static_cast<uint32_t>(std::lround(replayCommand.blur.h));
    replayCommand.blur.radius = std::clamp(radius, 0.0f, 12.0f);
    replayCommand.blur.opacity = std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f);
    replayCommand.blur.alphaOnlyTint = alphaOnlyTint;
    replayCommand.blur.tintR = std::clamp(tintR, 0.0f, 1.0f);
    replayCommand.blur.tintG = std::clamp(tintG, 0.0f, 1.0f);
    replayCommand.blur.tintB = std::clamp(tintB, 0.0f, 1.0f);
    replayCommand.blur.tintA = std::clamp(tintA, 0.0f, 1.0f);
    if (replayCommand.blur.w <= kEpsilon || replayCommand.blur.h <= kEpsilon ||
        replayCommand.blur.pixelWidth == 0 || replayCommand.blur.pixelHeight == 0 ||
        replayCommand.blur.opacity <= 0.0f) {
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
        replayCommand.quadPoint0X = p0x; replayCommand.quadPoint0Y = p0y;
        replayCommand.quadPoint1X = p1x; replayCommand.quadPoint1Y = p1y;
        replayCommand.quadPoint2X = p2x; replayCommand.quadPoint2Y = p2y;
        replayCommand.quadPoint3X = p3x; replayCommand.quadPoint3Y = p3y;
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

bool VulkanRenderTarget::TryRecordGpuRotatedLocalRoundedRectCommand(const CpuTransform& transform,
    float x, float y, float w, float h,
    float tl, float tr, float br, float bl,
    float strokeWidth, Brush* brush)
{
    // strokeWidth > 0 → border ring (outer minus inner); otherwise a solid fill.
    const bool isStroke = strokeWidth > 0.0f;

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

    constexpr float kEpsilon = 0.0001f;

    GpuReplayCommand replayCommand {};
    replayCommand.kind = GpuReplayCommandKind::SolidRect;
    if (!TryPopulateReplayClip(replayCommand)) {
        return false;
    }
    // An ancestor rounded clip would need its own screen-space subtraction that
    // this LOCAL-space path does not model — drop to CPU, mirroring the axis
    // rounded fill/stroke guard. (Also what frees the inner clip push-constant
    // slots for the local geometry: with hasRoundedClip impossible here, the
    // shader's clipFlags.x/.y branches never run.)
    if (replayCommand.hasRoundedClip || replayCommand.hasInnerRoundedClip) {
        return false;
    }
    if (replayCommand.scissorRight <= replayCommand.scissorLeft ||
        replayCommand.scissorBottom <= replayCommand.scissorTop) {
        return true;
    }

    const float halfStroke = isStroke ? (strokeWidth * 0.5f) : 0.0f;

    // LOCAL (pre-transform) OUTER rect: the centred model grows the element rect
    // by halfStroke on every side (fill → halfStroke 0 → just the element rect).
    // The localPos frame origin is this OUTER rect's top-left, in element space.
    const float originX = x - halfStroke;
    const float originY = y - halfStroke;
    const float localOuterW = w + 2.0f * halfStroke;
    const float localOuterH = h + 2.0f * halfStroke;

    // Circular OUTER per-corner radii = corner + halfStroke (clamped >= 0). The
    // FS additionally clamps to min(halfOuter) — D3D12-identical.
    const float outerTL = std::max(0.0f, tl + halfStroke);
    const float outerTR = std::max(0.0f, tr + halfStroke);
    const float outerBR = std::max(0.0f, br + halfStroke);
    const float outerBL = std::max(0.0f, bl + halfStroke);

    // Per-axis on-screen scale of each LOCAL axis = length of the affine column.
    // AA pad = 1px / scale, grown in LOCAL space so the smoothstep band still
    // spans ~1px on screen after the affine (mirrors D3D12 sdf_rect.vs.hlsl).
    const float sx = std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12);
    const float sy = std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22);
    const float expandX = 1.0f / std::max(sx, 1e-4f);
    const float expandY = 1.0f / std::max(sy, 1e-4f);

    // AA-EXPANDED local OUTER corners (relative to the OUTER top-left), pushed
    // through the affine. Winding 0=TL,1=TR,2=BR,3=BL matches the vertex shader's
    // indices[] = {0,1,2,0,2,3} AND the localPos corner table it rebuilds.
    float p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y;
    ApplyTransform(transform, originX - expandX,               originY - expandY,               p0x, p0y); // TL
    ApplyTransform(transform, originX + localOuterW + expandX, originY - expandY,               p1x, p1y); // TR
    ApplyTransform(transform, originX + localOuterW + expandX, originY + localOuterH + expandY, p2x, p2y); // BR
    ApplyTransform(transform, originX - expandX,               originY + localOuterH + expandY, p3x, p3y); // BL

    replayCommand.hasCustomQuad = true;
    replayCommand.quadPoint0X = p0x; replayCommand.quadPoint0Y = p0y;
    replayCommand.quadPoint1X = p1x; replayCommand.quadPoint1Y = p1y;
    replayCommand.quadPoint2X = p2x; replayCommand.quadPoint2Y = p2y;
    replayCommand.quadPoint3X = p3x; replayCommand.quadPoint3Y = p3y;

    // Screen-space AABB of the UNEXPANDED transformed outer quad — not used for
    // rendering (the VS draws the custom quad; the FS works in local space), only
    // for any AABB/telemetry consumer of solidRect.x/y/w/h.
    float q0x, q0y, q1x, q1y, q2x, q2y, q3x, q3y;
    ApplyTransform(transform, originX,               originY,               q0x, q0y);
    ApplyTransform(transform, originX + localOuterW, originY,               q1x, q1y);
    ApplyTransform(transform, originX + localOuterW, originY + localOuterH, q2x, q2y);
    ApplyTransform(transform, originX,               originY + localOuterH, q3x, q3y);
    replayCommand.solidRect.x = std::min(std::min(q0x, q1x), std::min(q2x, q3x));
    replayCommand.solidRect.y = std::min(std::min(q0y, q1y), std::min(q2y, q3y));
    replayCommand.solidRect.w = std::max(std::max(q0x, q1x), std::max(q2x, q3x)) - replayCommand.solidRect.x;
    replayCommand.solidRect.h = std::max(std::max(q0y, q1y), std::max(q2y, q3y)) - replayCommand.solidRect.y;
    replayCommand.solidRect.r = static_cast<float>(r) / 255.0f;
    replayCommand.solidRect.g = static_cast<float>(g) / 255.0f;
    replayCommand.solidRect.b = static_cast<float>(b) / 255.0f;
    replayCommand.solidRect.a = (static_cast<float>(a) / 255.0f) * std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (replayCommand.solidRect.a <= 0.0f) {
        return true;
    }

    // LOCAL geometry forwarded into the FREE inner rounded-clip push-constant
    // slots (see the SolidRect write site). No push-constant growth.
    replayCommand.hasLocalRoundedCoverage = true;
    replayCommand.localOuterW = localOuterW;
    replayCommand.localOuterH = localOuterH;
    replayCommand.localExpandX = expandX;
    replayCommand.localExpandY = expandY;
    replayCommand.localOuterPerCornerRadius[0] = outerTL;
    replayCommand.localOuterPerCornerRadius[1] = outerTR;
    replayCommand.localOuterPerCornerRadius[2] = outerBR;
    replayCommand.localOuterPerCornerRadius[3] = outerBL;

    if (isStroke) {
        const float localInnerW = w - 2.0f * halfStroke;
        const float localInnerH = h - 2.0f * halfStroke;
        if (localInnerW > kEpsilon && localInnerH > kEpsilon) {
            // INNER rect shares the OUTER centre (centred model), so the FS reuses
            // the same p = localPos - halfOuter. Circular inner radii =
            // max(0, corner - halfStroke), clamped to half the inner extent
            // (defensive; FS re-clamps to min(halfInner)).
            const float innerHalfW = localInnerW * 0.5f;
            const float innerHalfH = localInnerH * 0.5f;
            replayCommand.localInnerW = localInnerW;
            replayCommand.localInnerH = localInnerH;
            replayCommand.localInnerPerCornerRadius[0] = std::max(0.0f, std::min(tl - halfStroke, std::min(innerHalfW, innerHalfH)));
            replayCommand.localInnerPerCornerRadius[1] = std::max(0.0f, std::min(tr - halfStroke, std::min(innerHalfW, innerHalfH)));
            replayCommand.localInnerPerCornerRadius[2] = std::max(0.0f, std::min(br - halfStroke, std::min(innerHalfW, innerHalfH)));
            replayCommand.localInnerPerCornerRadius[3] = std::max(0.0f, std::min(bl - halfStroke, std::min(innerHalfW, innerHalfH)));
        }
        // localInnerW/H stay 0 when the band is thicker than the rect → the FS
        // skips the inner subtraction and the whole outer disc is the stroke.
    }
    // Fill: localInnerW/H stay 0 → no inner subtraction (full outer coverage).

    gpuReplayCommands_.push_back(replayCommand);
    return true;
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
        // Rotated / skewed: serve it on the GPU via the LOCAL-space rounded SDF,
        // which models ONE circular radius per corner. An elliptical request
        // (rx != ry — e.g. a rotated non-circular ellipse routed here via
        // TryRecordGpuEllipseFillCommand, or an elliptical-corner rounded rect)
        // would render as a rounded-rect silhouette rather than the true ellipse,
        // so keep those on the CPU exact path (the pre-GPU behaviour). Only the
        // circular case (rx == ry — the scalar-CornerRadius border/fill majority)
        // takes the GPU path.
        if (std::fabs(rx - ry) > kEpsilon) {
            return false;
        }
        const float circ = rx;
        return TryRecordGpuRotatedLocalRoundedRectCommand(transform, x, y, w, h,
            circ, circ, circ, circ, -1.0f /* fill */, brush);
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
    // rx == 0 && ry == 0 used to dispatch to TryRecordGpuRectangleStrokeCommand,
    // but Rectangle stroke now itself routes through here (with rx = ry = 0)
    // to ride the SDF AA. The shader's CoverageRoundRect handles zero radius
    // as a pure AABB SDF, so we just keep going through the rounded path.

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
        // Rotated / skewed: serve the border on the GPU via the LOCAL-space
        // rounded SDF (one circular radius per corner). The helper applies the
        // centred model (outer = r+halfStroke, inner = r-halfStroke) so a rotated
        // border lands where the unrotated one does. An elliptical request
        // (rx != ry — e.g. a rotated non-circular ellipse stroke) would collapse
        // to a rounded-rect silhouette, so keep those on the CPU exact path; only
        // the circular case (rx == ry) goes GPU.
        if (std::fabs(rx - ry) > kEpsilon) {
            return false;
        }
        const float circ = rx;
        return TryRecordGpuRotatedLocalRoundedRectCommand(transform, x, y, w, h,
            circ, circ, circ, circ, strokeWidth, brush);
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

bool VulkanRenderTarget::TryRecordGpuPerCornerRoundedRectFillCommand(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush)
{
    // Rotated / skewed: the per-corner screen-space CoverageRoundRect overwrite
    // below cannot serve a non-axis-aligned quad, so route straight to the
    // LOCAL-space rounded SDF with each corner's own (circular) radius. Must run
    // BEFORE the uniform record, otherwise that record itself takes the rotated
    // branch with a single max-radius and loses the per-corner shape.
    {
        const auto transform = GetCurrentTransform();
        constexpr float kEpsilon = 0.0001f;
        if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
            return TryRecordGpuRotatedLocalRoundedRectCommand(transform, x, y, w, h,
                tl, tr, br, bl, -1.0f /* fill */, brush);
        }
    }

    // First do the uniform-radius record using the largest corner — that
    // populates every common field (scissor, brush, rect transform, outer
    // rounded clip rect) for us. We then overwrite the per-corner radii so
    // the shader switches to CoveragePerCornerRoundRect at fragment time.
    const float maxRadius = std::max({ tl, tr, br, bl, 0.0f });
    if (maxRadius <= 0.0f) {
        return TryRecordGpuSolidRectCommand(x, y, w, h, brush);
    }
    if (!TryRecordGpuRoundedRectFillCommand(x, y, w, h, maxRadius, maxRadius, brush)) {
        return false;
    }
    if (gpuReplayCommands_.empty()) return true;  // record was dropped (cull / no-op).
    GpuReplayCommand& cmd = gpuReplayCommands_.back();
    if (cmd.kind != GpuReplayCommandKind::SolidRect || !cmd.hasRoundedClip) {
        return true;  // record fell into a fast path that doesn't honour per-corner.
    }
    // Bake the transform's per-axis scale into the radii so they live in the
    // same screen-space units as roundedClipRect.
    const auto transform = GetCurrentTransform();
    const float sx = std::fabs(transform.m11);
    const float sy = std::fabs(transform.m22);
    cmd.perCornerRadiusX[0] = std::max(tl * sx, 0.0f);  // TL
    cmd.perCornerRadiusX[1] = std::max(tr * sx, 0.0f);  // TR
    cmd.perCornerRadiusX[2] = std::max(br * sx, 0.0f);  // BR
    cmd.perCornerRadiusX[3] = std::max(bl * sx, 0.0f);  // BL
    cmd.perCornerRadiusY[0] = std::max(tl * sy, 0.0f);
    cmd.perCornerRadiusY[1] = std::max(tr * sy, 0.0f);
    cmd.perCornerRadiusY[2] = std::max(br * sy, 0.0f);
    cmd.perCornerRadiusY[3] = std::max(bl * sy, 0.0f);
    return true;
}

bool VulkanRenderTarget::TryRecordGpuPerCornerRoundedRectStrokeCommand(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, float strokeWidth, Brush* brush)
{
    // Rotated / skewed: route to the LOCAL-space rounded SDF with each corner's
    // own (circular) radius (centred outer/inner band). Must run BEFORE the
    // uniform record so that record doesn't take the rotated branch with a
    // single max-radius and lose the per-corner shape.
    if (strokeWidth > 0.0f) {
        const auto transform = GetCurrentTransform();
        constexpr float kEpsilon = 0.0001f;
        if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
            return TryRecordGpuRotatedLocalRoundedRectCommand(transform, x, y, w, h,
                tl, tr, br, bl, strokeWidth, brush);
        }
    }

    const float maxRadius = std::max({ tl, tr, br, bl, 0.0f });
    if (!TryRecordGpuRoundedRectStrokeCommand(x, y, w, h, maxRadius, maxRadius, strokeWidth, brush)) {
        return false;
    }
    if (gpuReplayCommands_.empty()) return true;
    GpuReplayCommand& cmd = gpuReplayCommands_.back();
    if (cmd.kind != GpuReplayCommandKind::SolidRect || !cmd.hasRoundedClip) {
        return true;
    }
    const auto transform = GetCurrentTransform();
    const float sx = std::fabs(transform.m11);
    const float sy = std::fabs(transform.m22);
    const float halfStroke = strokeWidth * 0.5f;
    // Outer rounded clip uses (corner + halfStroke) — expand each radius
    // so the rounded-band edge matches RoundedRectStroke's behaviour for
    // uniform radii.
    cmd.perCornerRadiusX[0] = std::max((tl + halfStroke) * sx, 0.0f);
    cmd.perCornerRadiusX[1] = std::max((tr + halfStroke) * sx, 0.0f);
    cmd.perCornerRadiusX[2] = std::max((br + halfStroke) * sx, 0.0f);
    cmd.perCornerRadiusX[3] = std::max((bl + halfStroke) * sx, 0.0f);
    cmd.perCornerRadiusY[0] = std::max((tl + halfStroke) * sy, 0.0f);
    cmd.perCornerRadiusY[1] = std::max((tr + halfStroke) * sy, 0.0f);
    cmd.perCornerRadiusY[2] = std::max((br + halfStroke) * sy, 0.0f);
    cmd.perCornerRadiusY[3] = std::max((bl + halfStroke) * sy, 0.0f);
    // Inner rounded clip (the stroke's inside edge): give each corner its own
    // radius = max(0, corner - halfStroke), the per-corner generalisation of
    // the uniform inner (rx - halfStroke) that TryRecordGpuRoundedRectStrokeCommand
    // set above. Outer = corner + halfStroke, inner = corner - halfStroke share
    // the same anchor, so the band stays constant-width per corner and the AA is
    // clean on BOTH edges. This replaces the old "outer per-corner, inner
    // uniform" shortcut, which seeded the inner hole from max(corner) and so
    // carved a rounded notch through the square corners of a non-uniform Border.
    // Clamp to half the inner extent per axis (mirrors the uniform inner clamp;
    // a no-op under the managed min(w,h)/2 pre-clamp, defensive otherwise). The
    // shader only reads these when hasInnerRoundedClip armed the inner clip.
    const float innerHalfW = (w - strokeWidth) * 0.5f;
    const float innerHalfH = (h - strokeWidth) * 0.5f;
    cmd.innerPerCornerRadiusX[0] = sx * std::max(0.0f, std::min(tl - halfStroke, innerHalfW));
    cmd.innerPerCornerRadiusX[1] = sx * std::max(0.0f, std::min(tr - halfStroke, innerHalfW));
    cmd.innerPerCornerRadiusX[2] = sx * std::max(0.0f, std::min(br - halfStroke, innerHalfW));
    cmd.innerPerCornerRadiusX[3] = sx * std::max(0.0f, std::min(bl - halfStroke, innerHalfW));
    cmd.innerPerCornerRadiusY[0] = sy * std::max(0.0f, std::min(tl - halfStroke, innerHalfH));
    cmd.innerPerCornerRadiusY[1] = sy * std::max(0.0f, std::min(tr - halfStroke, innerHalfH));
    cmd.innerPerCornerRadiusY[2] = sy * std::max(0.0f, std::min(br - halfStroke, innerHalfH));
    cmd.innerPerCornerRadiusY[3] = sy * std::max(0.0f, std::min(bl - halfStroke, innerHalfH));
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

bool VulkanRenderTarget::TryRecordGpuLineAACommand(float x1, float y1, float x2, float y2, float strokeWidth, Brush* brush)
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

    uint8_t bb = 0, bg = 0, br = 0, ba = 0;
    if (!TryGetApproximateBrushColor(brush, bb, bg, br, ba)) {
        return false;
    }

    // Build the 18 vertices in DIP space, then ApplyTransform per-vertex
    // before recording. This matches D3D12's behaviour where AddTriangles
    // applies the current transform to each TriangleVertex internally — the
    // perpendicular offset (and therefore the stroke width) gets scaled
    // along with everything else. If we transformed the endpoints first and
    // then computed the perpendicular in screen space, strokeWidth would
    // ignore the transform's scale, so a 2× zoom would still draw a 1-px
    // stroke instead of a 2-px stroke.
    const auto transform = GetCurrentTransform();

    const float dx = x2 - x1;
    const float dy = y2 - y1;
    const float len = std::sqrt(dx * dx + dy * dy);
    if (len < 0.001f) return true;  // degenerate — silently consume.

    // Unit perpendicular to the line direction in DIP space.
    const float invLen = 1.0f / len;
    const float nx = -dy * invLen;
    const float ny =  dx * invLen;

    // Convert brush colour to normalised [0, 1] premultiplied form. Vulkan
    // colour blend is set up for premultiplied alpha — see vc pipeline
    // creation — so we bake the alpha into rgb here.
    const float opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    const float r = br / 255.0f;
    const float g = bg / 255.0f;
    const float b = bb / 255.0f;
    const float baseA = (ba / 255.0f) * opacity;
    const float pr = r * baseA;
    const float pg = g * baseA;
    const float pb = b * baseA;

    // Manual analytical AA: render the line as three side-by-side strips —
    // a solid core flanked by a 1-pixel alpha-ramp on each side. Same
    // construction as D3D12RenderTarget::DrawLine — see
    // [[project_d3d12_line_manual_aa]] for the rationale.
    //
    //   Stroke ≥ 1px: core half-width = (stroke - 1) * 0.5, then 1px feather
    //                  on each side ramping alpha 1 → 0.
    //   Stroke < 1px: no core; the whole stroke becomes the feather and we
    //                  fade alpha by the stroke's pixel coverage so thin
    //                  lines don't pop.
    constexpr float kFeather = 1.0f;
    const float halfStroke = strokeWidth * 0.5f;
    float coreHalf, outerHalf, coverage;
    if (strokeWidth >= kFeather) {
        coreHalf  = halfStroke - kFeather * 0.5f;
        outerHalf = halfStroke + kFeather * 0.5f;
        coverage  = 1.0f;
    } else {
        coreHalf  = 0.0f;
        outerHalf = kFeather * 0.5f;
        coverage  = strokeWidth / kFeather;
    }

    const float caPr = pr * coverage;
    const float caPg = pg * coverage;
    const float caPb = pb * coverage;
    const float caPa = baseA * coverage;

    // Build 8 reference points in DIP space (4 endpoints × {inner core,
    // outer feather} × {positive perp, negative perp}). Then transform
    // each individually to screen space — keeps the perpendicular offset
    // (stroke width) responsive to the current transform's scale, matching
    // D3D12 AddTriangles semantics.
    const float p1cxDip  = x1 + nx * coreHalf,  p1cyDip  = y1 + ny * coreHalf;
    const float p1cnxDip = x1 - nx * coreHalf,  p1cnyDip = y1 - ny * coreHalf;
    const float p2cxDip  = x2 + nx * coreHalf,  p2cyDip  = y2 + ny * coreHalf;
    const float p2cnxDip = x2 - nx * coreHalf,  p2cnyDip = y2 - ny * coreHalf;
    const float p1oxDip  = x1 + nx * outerHalf, p1oyDip  = y1 + ny * outerHalf;
    const float p1onxDip = x1 - nx * outerHalf, p1onyDip = y1 - ny * outerHalf;
    const float p2oxDip  = x2 + nx * outerHalf, p2oyDip  = y2 + ny * outerHalf;
    const float p2onxDip = x2 - nx * outerHalf, p2onyDip = y2 - ny * outerHalf;

    float p1cx, p1cy, p1cnx, p1cny, p2cx, p2cy, p2cnx, p2cny;
    float p1ox, p1oy, p1onx, p1ony, p2ox, p2oy, p2onx, p2ony;
    ApplyTransform(transform, p1cxDip,  p1cyDip,  p1cx,  p1cy);
    ApplyTransform(transform, p1cnxDip, p1cnyDip, p1cnx, p1cny);
    ApplyTransform(transform, p2cxDip,  p2cyDip,  p2cx,  p2cy);
    ApplyTransform(transform, p2cnxDip, p2cnyDip, p2cnx, p2cny);
    ApplyTransform(transform, p1oxDip,  p1oyDip,  p1ox,  p1oy);
    ApplyTransform(transform, p1onxDip, p1onyDip, p1onx, p1ony);
    ApplyTransform(transform, p2oxDip,  p2oyDip,  p2ox,  p2oy);
    ApplyTransform(transform, p2onxDip, p2onyDip, p2onx, p2ony);

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::VcTriangles;
    if (!TryPopulateReplayClip(cmd)) {
        return false;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return true;  // entirely outside the clip — drop, but consume.
    }

    auto pushVert = [&cmd](float x, float y, float pr_, float pg_, float pb_, float pa_) {
        cmd.vcTriangles.vertices.push_back(x);
        cmd.vcTriangles.vertices.push_back(y);
        cmd.vcTriangles.vertices.push_back(pr_);
        cmd.vcTriangles.vertices.push_back(pg_);
        cmd.vcTriangles.vertices.push_back(pb_);
        cmd.vcTriangles.vertices.push_back(pa_);
    };

    cmd.vcTriangles.vertices.reserve(18 * 6);

    // Core strip (solid alpha). Skipped for sub-pixel lines (coreHalf == 0).
    if (coreHalf > 0.0f) {
        pushVert(p1cx,  p1cy,  caPr, caPg, caPb, caPa);
        pushVert(p1cnx, p1cny, caPr, caPg, caPb, caPa);
        pushVert(p2cx,  p2cy,  caPr, caPg, caPb, caPa);
        pushVert(p2cx,  p2cy,  caPr, caPg, caPb, caPa);
        pushVert(p1cnx, p1cny, caPr, caPg, caPb, caPa);
        pushVert(p2cnx, p2cny, caPr, caPg, caPb, caPa);
    }

    // Outer feather, positive-perpendicular side.
    pushVert(p1cx, p1cy, caPr, caPg, caPb, caPa);
    pushVert(p1ox, p1oy, 0.0f, 0.0f, 0.0f, 0.0f);
    pushVert(p2cx, p2cy, caPr, caPg, caPb, caPa);
    pushVert(p2cx, p2cy, caPr, caPg, caPb, caPa);
    pushVert(p1ox, p1oy, 0.0f, 0.0f, 0.0f, 0.0f);
    pushVert(p2ox, p2oy, 0.0f, 0.0f, 0.0f, 0.0f);

    // Outer feather, negative-perpendicular side.
    pushVert(p1cnx, p1cny, caPr, caPg, caPb, caPa);
    pushVert(p2cnx, p2cny, caPr, caPg, caPb, caPa);
    pushVert(p1onx, p1ony, 0.0f, 0.0f, 0.0f, 0.0f);
    pushVert(p1onx, p1ony, 0.0f, 0.0f, 0.0f, 0.0f);
    pushVert(p2cnx, p2cny, caPr, caPg, caPb, caPa);
    pushVert(p2onx, p2ony, 0.0f, 0.0f, 0.0f, 0.0f);

    cmd.vcTriangles.vertexCount = static_cast<uint32_t>(cmd.vcTriangles.vertices.size() / 6);
    if (cmd.vcTriangles.vertexCount == 0) return true;
    gpuReplayCommands_.push_back(std::move(cmd));
    return true;
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
    // Rotation / skew are intentionally NOT rejected here. The oriented quad
    // built below comes from the world-space transformed endpoints plus a
    // world-space perpendicular, so it is already correct for any affine — a
    // plain diagonal line is likewise an oriented quad. Bailing on rotation used
    // to route rotated stroked lines/polylines/polygons/paths to the CPU bitmap
    // fallback (RasterizePolylineToGpuBitmap), which rounds the endpoints to
    // integer pixels and makes a rotating stroke snap to the grid each frame.
    // Staying on the GPU quad path keeps the position fully sub-pixel.

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

    // Delegate to the rounded-rect stroke path with radius = 0. The shader's
    // CoverageRoundRect handles the zero-radius case as a pure AABB SDF (with
    // smoothstep AA on the rectangle edges), and the outer/inner clip pair
    // gives a single-draw stroke band — no corner overlap and no double-alpha
    // artefacts that the older 4×SolidRect approach produced for translucent
    // strokes.
    return TryRecordGpuRoundedRectStrokeCommand(x, y, w, h, 0.0f, 0.0f, strokeWidth, brush);
}

bool VulkanRenderTarget::TryRecordGpuBitmapCommand(Bitmap* bitmap, float x, float y, float w, float h, float opacity, bool opacityAlreadyBaked)
{
    const auto* sourceBitmap = static_cast<const VulkanBitmap*>(bitmap);
    if (!sourceBitmap || sourceBitmap->GetWidth() == 0 || sourceBitmap->GetHeight() == 0) {
        return false;
    }

    auto sharedPixels = sourceBitmap->GetSharedPixels();
    if (!sharedPixels || sharedPixels->empty()) {
        return false;
    }

    return TryRecordGpuPixelBufferCommandShared(std::move(sharedPixels), sourceBitmap->GetWidth(), sourceBitmap->GetHeight(), x, y, w, h, opacity, opacityAlreadyBaked);
}

bool VulkanRenderTarget::TryRecordGpuInkLayerCommand(void* inkLayer, float x, float y, float opacity)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_ || !inkLayer || opacity <= 0.0f) {
        return false;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return false;
    }

    auto* ink = reinterpret_cast<VulkanInkLayerBitmap*>(inkLayer);
    // Generation guard: the ink image must live on THIS render target's device
    // to be GPU-sampled — Vulkan device-level handles never cross devices, so
    // feeding a foreign generation's VkImageView into our descriptor set is
    // undefined behavior (the post-recovery / second-window signature). A lost
    // generation is refused outright. Returning false routes the blit to the
    // CPU readback fallback, which is legal cross-device (pixels travel
    // through host memory).
    if (ink->DeviceLost() || !impl_ || ink->DeviceHandle() != impl_->device) {
        return false;
    }
    const uint32_t iw = ink->Width();
    const uint32_t ih = ink->Height();
    if (iw == 0 || ih == 0 || ink->ImageView() == VK_NULL_HANDLE) {
        return false;
    }
    const float w = static_cast<float>(iw);
    const float h = static_cast<float>(ih);

    const auto transform = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    float p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y;
    ApplyTransform(transform, x, y, p0x, p0y);
    ApplyTransform(transform, x + w, y, p1x, p1y);
    ApplyTransform(transform, x + w, y + h, p2x, p2y);
    ApplyTransform(transform, x, y + h, p3x, p3y);

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::InkLayer;
    cmd.inkLayer.inkLayer = inkLayer;
    cmd.inkLayer.x = std::min(std::min(p0x, p1x), std::min(p2x, p3x));
    cmd.inkLayer.y = std::min(std::min(p0y, p1y), std::min(p2y, p3y));
    cmd.inkLayer.w = std::max(std::max(p0x, p1x), std::max(p2x, p3x)) - cmd.inkLayer.x;
    cmd.inkLayer.h = std::max(std::max(p0y, p1y), std::max(p2y, p3y)) - cmd.inkLayer.y;
    cmd.inkLayer.opacity = std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f);
    if (cmd.inkLayer.w <= kEpsilon || cmd.inkLayer.h <= kEpsilon || cmd.inkLayer.opacity <= 0.0f) {
        return false;
    }

    if (!TryPopulateReplayClip(cmd)) {
        return false;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return true;  // fully clipped — nothing to draw, but stay on the GPU path
    }
    if (std::fabs(transform.m12) > kEpsilon || std::fabs(transform.m21) > kEpsilon) {
        cmd.hasCustomQuad = true;
        cmd.quadPoint0X = p0x; cmd.quadPoint0Y = p0y;
        cmd.quadPoint1X = p1x; cmd.quadPoint1Y = p1y;
        cmd.quadPoint2X = p2x; cmd.quadPoint2Y = p2y;
        cmd.quadPoint3X = p3x; cmd.quadPoint3Y = p3y;
    }

    gpuReplayCommands_.push_back(std::move(cmd));
    return true;
}

void VulkanRenderTarget::BlitInkLayer(void* inkLayerBitmap, float dstX, float dstY, float opacity)
{
    TouchFrame();
    if (!inkLayerBitmap || opacity <= 0.0f) {
        return;
    }

    // Primary path: record an InkLayer replay command that samples the resident
    // ink image directly on the GPU (parity with D3D12 BlitInkLayer / AddBitmap).
    const bool recordedDirect = TryRecordGpuInkLayerCommand(inkLayerBitmap, dstX, dstY, opacity);

    // Foreign-but-healthy generation (the ink image lives on another window's
    // VkDevice — second window / post-recovery before the canvas rebuilt its
    // layer): direct GPU sampling is forbidden (cross-device handles), but the
    // pixels can legally travel through host memory. Read the layer back on
    // its own device and record it as an ordinary bitmap upload so the
    // committed ink stays visible instead of silently vanishing. A LOST
    // generation is excluded — its image content is physically unrecoverable;
    // ReadbackBgra would refuse anyway.
    //
    // This topology can be a steady state (the layer was created after a
    // later window registered its device), so the readback is cached by the
    // layer's ContentVersion: an unchanged layer costs zero GPU round trips
    // per frame; the synchronous readback happens once per stroke/clear. The
    // record-entry preconditions are mirrored up front so a frame that would
    // reject the record anyway (effect/transition capture, replay disabled)
    // doesn't pay for a readback. Known limitation shared with the
    // cpuRasterNeeded_ readback below: this submits to the foreign
    // generation's queue without the backend inkMutex_ — safe while ink ops
    // and rendering share the UI thread (today's model), to revisit if the
    // experimental render thread starts blitting ink.
    if (!recordedDirect && !cpuRasterNeeded_) {
        auto* foreignInk = reinterpret_cast<VulkanInkLayerBitmap*>(inkLayerBitmap);
        const bool recordable = gpuReplaySupported_ && gpuReplayHasClear_ &&
                                effectCaptureStack_.empty() && activeTransitionSlot_ < 0;
        if (recordable && impl_ && !foreignInk->DeviceLost() &&
            foreignInk->DeviceHandle() != impl_->device &&
            foreignInk->Width() > 0 && foreignInk->Height() > 0) {
            // Version read deliberately precedes the readback: a concurrent
            // bump between the two at worst stamps newer content with an
            // older version, costing one redundant readback next frame —
            // never a stale display.
            const uint64_t version = foreignInk->ContentVersion();
            auto& snap = foreignInkSnapshots_[inkLayerBitmap];
            if (!snap.bgra || snap.version != version ||
                snap.width != foreignInk->Width() || snap.height != foreignInk->Height()) {
                std::vector<uint8_t> bgra;
                if (!foreignInk->ReadbackBgra(bgra)) {
                    foreignInkSnapshots_.erase(inkLayerBitmap);
                    return;
                }
                snap.version = version;
                snap.width = foreignInk->Width();
                snap.height = foreignInk->Height();
                snap.bgra = std::make_shared<const std::vector<uint8_t>>(std::move(bgra));
                if (foreignInkSnapshots_.size() > 8) {
                    // Crude but bounded eviction: keep only the entry just
                    // refreshed (stale keys belong to destroyed layers).
                    ForeignInkSnapshot keep = snap;
                    foreignInkSnapshots_.clear();
                    foreignInkSnapshots_[inkLayerBitmap] = std::move(keep);
                }
            }
            const auto& entry = foreignInkSnapshots_[inkLayerBitmap];
            // Shared overload: the replay command reference-counts the cached
            // pixels instead of copying the full buffer every frame.
            TryRecordGpuPixelBufferCommandShared(
                entry.bgra, entry.width, entry.height,
                dstX, dstY,
                static_cast<float>(entry.width),
                static_cast<float>(entry.height),
                opacity);
        }
        return;
    }

    if (!cpuRasterNeeded_) {
        return;
    }

    // CPU fallback (rare — whole frame on CPU): read the ink image back and
    // blend it axis-aligned. A transform on the blit is not honoured here; the
    // GPU path above handles the rotated case.
    auto* ink = reinterpret_cast<VulkanInkLayerBitmap*>(inkLayerBitmap);
    if (ink->Width() == 0 || ink->Height() == 0) {
        return;
    }
    std::vector<uint8_t> bgra;
    if (!ink->ReadbackBgra(bgra)) {
        return;
    }
    BlendBuffer(bgra, static_cast<int>(ink->Width()), static_cast<int>(ink->Height()),
                dstX, dstY, static_cast<float>(ink->Width()), static_cast<float>(ink->Height()),
                std::clamp(opacity * GetCurrentOpacity(), 0.0f, 1.0f));
}

// Retained layers: env-gated (JALIUM_VK_RETAINED_LAYERS) opt-in until validated,
// mirroring the GPU stencil path's gate. Default OFF. Uses _dupenv_s because /sdl
// escalates std::getenv C4996 to a hard error (see VkStencilPathEnabled).
static bool VulkanRetainedLayersEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_RETAINED_LAYERS") != 0 || e == nullptr) {
            if (e) free(e);
            return false;
        }
        const bool on = (e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N');
        free(e);
        return on;
#else
        const char* e = std::getenv("JALIUM_VK_RETAINED_LAYERS");
        return e && e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N';
#endif
    }();
    return enabled;
}

bool VulkanRenderTarget::SupportsRetainedLayers() const
{
    return VulkanRetainedLayersEnabled();
}

void* VulkanRenderTarget::RealizeLayerBegin(void* existingLayer, float x, float y, float w, float h)
{
    // Safety valve: return nullptr for anything we don't handle so the managed side
    // falls back to full re-emission (correct rendering, just no fast path).
    if (!VulkanRetainedLayersEnabled() || !isDrawing_ || w <= 0.0f || h <= 0.0f) {
        return nullptr;
    }
    // Axis-aligned sub-region only: a rotated/skewed transform can't be snapshotted
    // as a rectangular pixel sub-region.
    const auto t = GetCurrentTransform();
    constexpr float kEpsilon = 0.0001f;
    if (std::fabs(t.m12) > kEpsilon || std::fabs(t.m21) > kEpsilon) {
        return nullptr;
    }
    float px0 = 0.0f, py0 = 0.0f, px1 = 0.0f, py1 = 0.0f;
    ApplyTransform(t, x, y, px0, py0);
    ApplyTransform(t, x + w, y + h, px1, py1);
    int32_t physX = static_cast<int32_t>(std::floor(std::min(px0, px1)));
    int32_t physY = static_cast<int32_t>(std::floor(std::min(py0, py1)));
    int32_t physX1 = static_cast<int32_t>(std::ceil(std::max(px0, px1)));
    int32_t physY1 = static_cast<int32_t>(std::ceil(std::max(py0, py1)));
    physX = std::clamp(physX, 0, width_);
    physY = std::clamp(physY, 0, height_);
    physX1 = std::clamp(physX1, 0, width_);
    physY1 = std::clamp(physY1, 0, height_);
    if (physX1 <= physX || physY1 <= physY) {
        return nullptr;
    }
    // Force CPU rasterization so the subtree's draws land in pixelBuffer_ where they
    // can be snapshotted (Vulkan has no GPU offscreen render target for this yet).
    EnsureCpuRasterization();
    if (!cpuRasterNeeded_) {
        return nullptr;  // couldn't promote (e.g. no pixel buffer this frame) — fall back
    }
    // Save the current frame state and start a fresh sub-frame (mirror
    // BeginTransitionCapture). A stack keeps nested realizes safe.
    RetainedCaptureState st;
    st.savedPixels = pixelBuffer_;
    st.savedReplayCommands = gpuReplayCommands_;
    st.savedReplaySupported = gpuReplaySupported_;
    st.savedReplayHasClear = gpuReplayHasClear_;
    st.physX = physX;
    st.physY = physY;
    st.physW = physX1 - physX;
    st.physH = physY1 - physY;
    VulkanRetainedLayer* layer = nullptr;
    for (auto& up : retainedLayers_) {
        if (up.get() == existingLayer) { layer = up.get(); break; }
    }
    if (!layer) {
        retainedLayers_.push_back(std::make_unique<VulkanRetainedLayer>());
        layer = retainedLayers_.back().get();
    }
    st.layer = layer;
    retainedCaptureStack_.push_back(std::move(st));
    ResizeCpuCanvas();
    ClearCpuCanvas(0, 0, 0, 0);
    ResetGpuReplay();
    gpuReplayHasClear_ = true;
    return layer;
}

void VulkanRenderTarget::RealizeLayerEnd(void* layer)
{
    (void)layer;
    if (retainedCaptureStack_.empty()) {
        return;
    }
    RetainedCaptureState st = std::move(retainedCaptureStack_.back());
    retainedCaptureStack_.pop_back();
    VulkanRetainedLayer* lyr = st.layer;
    // Extract the captured physical sub-region from pixelBuffer_ into the layer.
    if (lyr && st.physW > 0 && st.physH > 0 && !pixelBuffer_.empty()) {
        const int32_t stride = width_ * 4;
        lyr->pixels.assign(static_cast<size_t>(st.physW) * static_cast<size_t>(st.physH) * 4u, 0);
        lyr->w = st.physW;
        lyr->h = st.physH;
        for (int32_t row = 0; row < st.physH; ++row) {
            const int32_t srcY = st.physY + row;
            if (srcY < 0 || srcY >= height_) {
                continue;
            }
            const uint8_t* src = pixelBuffer_.data() + static_cast<size_t>(srcY) * static_cast<size_t>(stride) + static_cast<size_t>(st.physX) * 4u;
            uint8_t* dst = lyr->pixels.data() + static_cast<size_t>(row) * static_cast<size_t>(st.physW) * 4u;
            std::memcpy(dst, src, static_cast<size_t>(st.physW) * 4u);
        }
    }
    // Restore the saved frame state.
    pixelBuffer_ = std::move(st.savedPixels);
    gpuReplayCommands_ = std::move(st.savedReplayCommands);
    gpuReplaySupported_ = st.savedReplaySupported;
    gpuReplayHasClear_ = st.savedReplayHasClear;
}

void VulkanRenderTarget::CompositeLayer(void* layer, float x, float y, float w, float h, float opacity)
{
    // Only composite layers we own (guards against foreign handles).
    VulkanRetainedLayer* lyr = nullptr;
    for (auto& up : retainedLayers_) {
        if (up.get() == layer) { lyr = up.get(); break; }
    }
    if (!lyr || lyr->pixels.empty() || lyr->w <= 0 || lyr->h <= 0 ||
        w <= 0.0f || h <= 0.0f || opacity <= 0.0f) {
        return;
    }
    // GPU replay path: composite the snapshot as a transformed+clipped bitmap quad
    // (TryRecordGpuPixelBufferCommand applies the live transform / clip / opacity).
    if (!cpuRasterNeeded_) {
        if (!TryRecordGpuPixelBufferCommand(lyr->pixels, static_cast<uint32_t>(lyr->w),
                                            static_cast<uint32_t>(lyr->h), x, y, w, h, opacity)) {
            /* drop: skip but keep replay path */ (void)__FUNCTION__;
        }
        return;
    }
    // CPU path: blend the snapshot into the pixel buffer.
    BlendBuffer(lyr->pixels, lyr->w, lyr->h, x, y, w, h, opacity);
}

void VulkanRenderTarget::DestroyRetainedLayer(void* layer)
{
    if (!layer) {
        return;
    }
    // Free OUR layer if we own it.
    for (auto it = retainedLayers_.begin(); it != retainedLayers_.end(); ++it) {
        if (it->get() == layer) {
            retainedLayers_.erase(it);
            return;
        }
    }
    // Otherwise the handle is FOREIGN (another backend's layer drained here after a
    // device-lost fallback) and cannot be freed through an unknown concrete type —
    // a bounded, logged leak rather than a silent one or undefined behavior.
    VK_LOG("[Vulkan] DestroyRetainedLayer: ignoring foreign retained-layer handle %p "
           "(not created by this Vulkan target; cross-backend handles cannot be freed here)",
           layer);
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
    // Gradient stroke: true per-vertex gradient via StrokePath (see DrawPolygon),
    // instead of the representative-solid TryGetApproximateBrushColor path below.
    // Solid/image brushes fall through unchanged.
    {
        EngineBrushData gbd {};
        std::vector<EngineBrushData::GradientStop> gstops;
        if (BuildEngineBrush(brush, GetCurrentOpacity(), gbd, gstops) && gbd.type != 0) {
            std::vector<float> gcmds = {
                0.0f, x + w, y,        // LineTo top-right
                0.0f, x + w, y + h,    // LineTo bottom-right
                0.0f, x,     y + h,    // LineTo bottom-left (close edge added by closed=true)
            };
            StrokePath(x, y, gcmds.data(), static_cast<uint32_t>(gcmds.size()),
                       brush, strokeWidth, /*closed*/ true, /*lineJoin*/ 0, /*miterLimit*/ 10.0f,
                       /*lineCap*/ 0, nullptr, 0, 0.0f, /*edgeMode*/ -1);
            return;
        }
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

// Records a gradient fill over a convex local-space perimeter as an IN-ORDER
// vertex-colour triangle fan (see header). Returns false for non-gradient
// brushes (caller does a solid fill) or when the fan can't be recorded.
bool VulkanRenderTarget::TryRecordGradientFanInOrder(const std::vector<float>& perimeterLocal,
                                                     float cxLocal, float cyLocal, Brush* brush)
{
    const uint32_t n = static_cast<uint32_t>(perimeterLocal.size() / 2);
    if (n < 3 || !brush) return false;

    EngineBrushData bd {};
    std::vector<EngineBrushData::GradientStop> stopStore;
    const float opacity = std::clamp(GetCurrentOpacity(), 0.0f, 1.0f);
    if (!BuildEngineBrush(brush, opacity, bd, stopStore) || bd.type == 0) {
        return false;  // solid / image brush — not our job
    }

    std::vector<float> flatStops;
    FlattenGradientStops(bd, flatStops);

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::VcTriangles;
    if (!TryPopulateReplayClip(cmd)) {
        return false;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return true;  // entirely clipped — consume without drawing
    }

    const auto transform = GetCurrentTransform();
    // Sample the gradient at a LOCAL point (bd geometry shares the perimeter's
    // local space), transform to world, then premultiply — the vc pipeline blends
    // premultiplied alpha and opacity is already folded into bd's stop alpha.
    auto sampleVert = [&](float lx, float ly,
                          float& wx, float& wy,
                          float& pr, float& pg, float& pb, float& pa) {
        GradientColor gc = SampleBrushGradient(bd, flatStops.data(), lx, ly);
        ApplyTransform(transform, lx, ly, wx, wy);
        pa = std::clamp(gc.a, 0.0f, 1.0f);
        pr = std::clamp(gc.r, 0.0f, 1.0f) * pa;
        pg = std::clamp(gc.g, 0.0f, 1.0f) * pa;
        pb = std::clamp(gc.b, 0.0f, 1.0f) * pa;
    };
    auto pushVert = [&cmd](float vx, float vy, float pr, float pg, float pb, float pa) {
        cmd.vcTriangles.vertices.push_back(vx);
        cmd.vcTriangles.vertices.push_back(vy);
        cmd.vcTriangles.vertices.push_back(pr);
        cmd.vcTriangles.vertices.push_back(pg);
        cmd.vcTriangles.vertices.push_back(pb);
        cmd.vcTriangles.vertices.push_back(pa);
    };

    cmd.vcTriangles.vertices.reserve(static_cast<size_t>(n) * 3u * 6u);
    float ccx, ccy, cr, cg, cb, ca;
    sampleVert(cxLocal, cyLocal, ccx, ccy, cr, cg, cb, ca);  // fan centre
    for (uint32_t i = 0; i < n; ++i) {
        const uint32_t j = (i + 1u) % n;
        float ax, ay, ar, ag, ab, aa;
        float bx, by, br2, bg2, bb2, ba2;
        sampleVert(perimeterLocal[i * 2], perimeterLocal[i * 2 + 1], ax, ay, ar, ag, ab, aa);
        sampleVert(perimeterLocal[j * 2], perimeterLocal[j * 2 + 1], bx, by, br2, bg2, bb2, ba2);
        pushVert(ccx, ccy, cr, cg, cb, ca);
        pushVert(ax, ay, ar, ag, ab, aa);
        pushVert(bx, by, br2, bg2, bb2, ba2);
    }
    cmd.vcTriangles.vertexCount = static_cast<uint32_t>(cmd.vcTriangles.vertices.size() / 6u);
    if (cmd.vcTriangles.vertexCount < 3) {
        return false;
    }
    gpuReplayCommands_.push_back(std::move(cmd));

    if (cpuRasterNeeded_) {
        uint8_t b8 = 0, g8 = 0, r8 = 0, a8 = 0;
        if (TryGetApproximateBrushColor(brush, b8, g8, r8, a8)) {
            std::vector<float> world;
            world.reserve(perimeterLocal.size());
            for (uint32_t i = 0; i < n; ++i) {
                float wx = 0.0f, wy = 0.0f;
                ApplyTransform(transform, perimeterLocal[i * 2], perimeterLocal[i * 2 + 1], wx, wy);
                world.push_back(wx);
                world.push_back(wy);
            }
            RasterizePolygon(world, /*NonZero*/ 1, b8, g8, r8, a8);
        }
    }
    return true;
}

void VulkanRenderTarget::BuildRoundedRectPolygon(float x, float y, float w, float h,
                                                 float tl, float tr, float br, float bl,
                                                 std::vector<float>& outPts) const
{
    outPts.clear();
    if (w <= 0.0f || h <= 0.0f) return;
    const float maxR = std::min(w, h) * 0.5f;
    tl = std::clamp(tl, 0.0f, maxR);
    tr = std::clamp(tr, 0.0f, maxR);
    br = std::clamp(br, 0.0f, maxR);
    bl = std::clamp(bl, 0.0f, maxR);

    constexpr int kSeg = 8;  // arc segments per corner
    constexpr float kPi = 3.14159265358979323846f;
    auto arc = [&](float cx, float cy, float r, float a0, float a1) {
        if (r <= 0.01f) { outPts.push_back(cx); outPts.push_back(cy); return; }
        for (int i = 0; i <= kSeg; ++i) {
            const float t = a0 + (a1 - a0) * (static_cast<float>(i) / static_cast<float>(kSeg));
            outPts.push_back(cx + std::cos(t) * r);
            outPts.push_back(cy + std::sin(t) * r);
        }
    };
    // Clockwise in screen space (y down): TL → TR → BR → BL corners; the straight
    // edges fall out of consecutive arc endpoints.
    arc(x + tl,     y + tl,     tl, kPi,        1.5f * kPi);  // top-left
    arc(x + w - tr, y + tr,     tr, 1.5f * kPi, 2.0f * kPi);  // top-right
    arc(x + w - br, y + h - br, br, 0.0f,       0.5f * kPi);  // bottom-right
    arc(x + bl,     y + h - bl, bl, 0.5f * kPi, kPi);         // bottom-left
}

bool VulkanRenderTarget::TryFillSuperEllipseInOrder(float x, float y, float w, float h,
                                                    float tl, float tr, float br, float bl, Brush* brush)
{
    std::vector<float> poly;
    BuildSuperEllipsePolygon(x, y, w, h, tl, tr, br, bl, poly);  // squircle boundary (local, convex)
    const uint32_t n = static_cast<uint32_t>(poly.size() / 2);
    if (n < 3) return false;

    // Gradient squircle → in-order vertex-colour fan (true gradient, no occlusion).
    if (TryRecordGradientFanInOrder(poly, x + w * 0.5f, y + h * 0.5f, brush)) {
        return true;
    }

    // Solid squircle → in-order FilledPolygon (transform local → world).
    const auto transform = GetCurrentTransform();
    std::vector<float> world;
    world.reserve(poly.size());
    for (uint32_t i = 0; i < n; ++i) {
        float wx = 0.0f, wy = 0.0f;
        ApplyTransform(transform, poly[i * 2], poly[i * 2 + 1], wx, wy);
        world.push_back(wx);
        world.push_back(wy);
    }
    if (!TryRecordGpuFilledPolygonCommand(world, /*NonZero*/ 1, brush)) {
        return false;
    }
    if (cpuRasterNeeded_) {
        uint8_t b = 0, g = 0, r = 0, a = 0;
        if (TryGetSolidBrushColor(brush, b, g, r, a)) {
            RasterizePolygon(world, /*NonZero*/ 1, b, g, r, a);
        }
    }
    return true;
}

void VulkanRenderTarget::FillRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush)
{
    TouchFrame();

    // SuperEllipse shape (Border.Shape == SuperEllipse): an iOS squircle =
    // straight edges + continuous-curvature (Lamé) corners of the given radius
    // (BuildSuperEllipsePolygon), drawn IN ORDER (TryFillSuperEllipseInOrder) so
    // the card background isn't painted over its own on-top content by the late-
    // draining Impeller batch. rx is the corner radius (uniform overload).
    if (currentShapeType_ == 1 && brush && w > 0.0f && h > 0.0f) {
        if (TryFillSuperEllipseInOrder(x, y, w, h, rx, rx, rx, rx, brush)) {
            return;
        }
        std::vector<float> poly;
        BuildSuperEllipsePolygon(x, y, w, h, rx, rx, rx, rx, poly);
        if (poly.size() >= 6) {
            FillPolygon(poly.data(), static_cast<uint32_t>(poly.size() / 2), brush, /*NonZero*/ 1);
            return;
        }
    }

    // Gradient brush: in-order vertex-colour fan (same path as per-corner /
    // SuperEllipse gradient) so the gradient renders truly and in draw order
    // instead of vanishing at TryGetSolidBrushColor below. Solid/image brushes
    // fall through (TryRecordGradientFanInOrder bails for non-gradient).
    if (brush && w > 0.0f && h > 0.0f) {
        std::vector<float> gradPerimeter;
        BuildRoundedRectPolygon(x, y, w, h, rx, rx, rx, rx, gradPerimeter);
        if (TryRecordGradientFanInOrder(gradPerimeter, x + w * 0.5f, y + h * 0.5f, brush)) {
            return;
        }
    }

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

// Per-corner rounded rectangles. The fragment shader now picks an
// independent (rx, ry) per corner quadrant via CoveragePerCornerRoundRect,
// mirroring D3D12's SdfRect-with-4-corner-radii path — tabs / cards /
// any UI that mixes rounded with square corners renders correctly
// instead of degrading to a single uniform radius.
void VulkanRenderTarget::FillPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush)
{
    TouchFrame();

    // SuperEllipse shape: the managed Border draws its squircle background through
    // the CornerRadius overload of DrawRoundedRectangle, which lands HERE (the
    // per-corner path). Draw the squircle (straight edges + Lamé corners of the
    // per-corner radii) IN ORDER so the card background isn't painted over its own
    // on-top content by the late-draining Impeller batch.
    if (currentShapeType_ == 1 && brush && w > 0.0f && h > 0.0f) {
        if (TryFillSuperEllipseInOrder(x, y, w, h, tl, tr, br, bl, brush)) {
            return;
        }
        std::vector<float> poly;
        BuildSuperEllipsePolygon(x, y, w, h, tl, tr, br, bl, poly);
        if (poly.size() >= 6) {
            FillPolygon(poly.data(), static_cast<uint32_t>(poly.size() / 2), brush, /*NonZero*/ 1);
            return;
        }
    }

    // Gradient brush on an ordinary rounded rect: in-order vertex-colour fan (same
    // path as the SuperEllipse gradient) so the gradient renders truly and in draw
    // order, instead of collapsing to TryGetApproximateBrushColor's solid below.
    if (brush && w > 0.0f && h > 0.0f) {
        std::vector<float> perimeter;
        BuildRoundedRectPolygon(x, y, w, h, tl, tr, br, bl, perimeter);
        if (TryRecordGradientFanInOrder(perimeter, x + w * 0.5f, y + h * 0.5f, brush)) {
            return;
        }
    }

    if (!TryRecordGpuPerCornerRoundedRectFillCommand(x, y, w, h, tl, tr, br, bl, brush)) {
        // GPU path can't represent this (effect capture, rotated transform,
        // non-replayable brush). Degrade to the largest-radius uniform
        // rounded rect — visual diff is minor and the frame stays alive.
        const float maxR = std::max({ tl, tr, br, bl, 0.0f });
        FillRoundedRectangle(x, y, w, h, maxR, maxR, brush);
        return;
    }

    if (cpuRasterNeeded_) {
        // CPU fallback uses the same per-corner clip stack entry the
        // public PushPerCornerRoundedRectClip API maintains — pushing
        // it transiently lets FillSolidRect respect the asymmetric shape.
        uint8_t bb = 0, bg = 0, br8 = 0, ba = 0;
        if (TryGetSolidBrushColor(brush, bb, bg, br8, ba)) {
            ClipState clip {};
            clip.rounded = true;
            clip.x = x; clip.y = y; clip.w = w; clip.h = h;
            clip.radiusTL = tl; clip.radiusTR = tr;
            clip.radiusBR = br; clip.radiusBL = bl;
            clip.transform = GetCurrentTransform();
            clip.hasInverse = TryInvertTransform(clip.transform, clip.inverseTransform);
            clipStack_.push_back(clip);

            const auto transform = GetCurrentTransform();
            float px0, py0, px1, py1;
            ApplyTransform(transform, x, y, px0, py0);
            ApplyTransform(transform, x + w, y + h, px1, py1);
            FillSolidRect(
                static_cast<int>(std::floor(std::min(px0, px1))),
                static_cast<int>(std::floor(std::min(py0, py1))),
                static_cast<int>(std::ceil(std::max(px0, px1))),
                static_cast<int>(std::ceil(std::max(py0, py1))),
                bb, bg, br8, ba);
            clipStack_.pop_back();
        }
    }
}

void VulkanRenderTarget::DrawPerCornerRoundedRectangle(float x, float y, float w, float h,
    float tl, float tr, float br, float bl, Brush* brush, float strokeWidth)
{
    TouchFrame();

    // SuperEllipse border stroke via the per-corner overload (see the fill above).
    if (currentShapeType_ == 1 && brush && w > 0.0f && h > 0.0f && strokeWidth > 0.0f) {
        std::vector<float> poly;
        BuildSuperEllipsePolygon(x, y, w, h, tl, tr, br, bl, poly);
        if (poly.size() >= 6) {
            DrawPolygon(poly.data(), static_cast<uint32_t>(poly.size() / 2), brush, strokeWidth,
                        /*closed*/ true, /*lineJoin*/ 0, /*miterLimit*/ 10.0f);
            return;
        }
    }

    if (!TryRecordGpuPerCornerRoundedRectStrokeCommand(x, y, w, h, tl, tr, br, bl, strokeWidth, brush)) {
        const float maxR = std::max({ tl, tr, br, bl, 0.0f });
        DrawRoundedRectangle(x, y, w, h, maxR, maxR, brush, strokeWidth);
    }
}

void VulkanRenderTarget::DrawRoundedRectangle(float x, float y, float w, float h, float rx, float ry, Brush* brush, float strokeWidth)
{
    TouchFrame();

    // SuperEllipse border stroke — mirror the fill path: stroke the tessellated
    // Lamé-curve outline through DrawPolygon (closed) so a SuperEllipse Border's
    // BorderBrush follows the squircle edge instead of a circular rounded rect.
    if (currentShapeType_ == 1 && brush && w > 0.0f && h > 0.0f && strokeWidth > 0.0f) {
        std::vector<float> poly;
        BuildSuperEllipsePolygon(x, y, w, h, rx, rx, rx, rx, poly);
        if (poly.size() >= 6) {
            DrawPolygon(poly.data(), static_cast<uint32_t>(poly.size() / 2), brush, strokeWidth,
                        /*closed*/ true, /*lineJoin*/ 0, /*miterLimit*/ 10.0f);
            return;
        }
    }

    // Gradient border: true per-vertex gradient via StrokePath over the rounded-rect
    // outline (same BuildRoundedRectPolygon the gradient FILL path uses). Solid/image
    // brushes fall through to the GPU stroke record below.
    if (brush && w > 0.0f && h > 0.0f) {
        EngineBrushData gbd {};
        std::vector<EngineBrushData::GradientStop> gstops;
        if (BuildEngineBrush(brush, GetCurrentOpacity(), gbd, gstops) && gbd.type != 0) {
            std::vector<float> goutline;
            BuildRoundedRectPolygon(x, y, w, h, rx, rx, rx, rx, goutline);
            const size_t gn = goutline.size() / 2;
            if (gn >= 3) {
                std::vector<float> gcmds;
                gcmds.reserve(gn * 3u);
                for (size_t gi = 1; gi < gn; ++gi) {
                    gcmds.push_back(0.0f);
                    gcmds.push_back(goutline[gi * 2]);
                    gcmds.push_back(goutline[gi * 2 + 1]);
                }
                StrokePath(goutline[0], goutline[1], gcmds.data(), static_cast<uint32_t>(gcmds.size()),
                           brush, strokeWidth, /*closed*/ true, /*lineJoin*/ 2, /*miterLimit*/ 10.0f,
                           /*lineCap*/ 0, nullptr, 0, 0.0f, /*edgeMode*/ -1);
                return;
            }
        }
    }
    if (!TryRecordGpuRoundedRectStrokeCommand(x, y, w, h, rx, ry, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if ((rx <= 0.0f && ry <= 0.0f) || !brush) {
        DrawRectangle(x, y, w, h, brush, strokeWidth);
        return;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    // Gradient borders render as a representative solid (stop average) — the
    // CPU stroke rasterizer has no per-pixel gradient — instead of dropping.
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return;
    }

    StrokeRoundedRectApprox(x, y, w, h, rx, ry, strokeWidth, b, g, r, a);
}

// Adaptive ellipse tessellation. D3D12 renders ellipses through a true SDF
// SuperEllipse pipeline ([[project_d3d12_ellipse_true_sdf]]) with analytical
// AA, so a circle of any radius shows zero polygonalisation. The Vulkan
// backend doesn't have that SDF pipeline, so we compensate by scaling the
// vertex count with on-screen radius — small icons keep their cheap 32-vert
// fans, big circles get up to 256 verts so the chord error stays at well
// under 0.5 px regardless of size.
//
// Heuristic: at a screen-space radius of R px, the chord length between
// two adjacent vertices on a circle is ~2π·R / N, and the chord-to-arc
// error is ~(π·R / N)². Solving for error ≤ 0.25 px (sub-pixel by enough
// margin to hide the polygon corners under AA) gives N ≈ 2π·sqrt(R / 0.5).
// We bucket to multiples of 8 so caches reuse the same vertex topology
// across many radii, and clamp to [32, 256]. Cost at 256: 1024 floats
// = 4 KB of staging — negligible.
static inline int VulkanEllipseSegmentCount(float rx, float ry, float scaleX, float scaleY)
{
    const float maxPxRadius = std::max(std::fabs(rx) * std::max(1.0f, scaleX),
                                       std::fabs(ry) * std::max(1.0f, scaleY));
    if (maxPxRadius <= 4.0f) return 32;
    int segments = static_cast<int>(std::ceil(6.2831853f * std::sqrt(maxPxRadius / 0.5f)));
    segments = ((segments + 7) / 8) * 8;       // round up to multiple of 8
    return std::clamp(segments, 32, 256);
}

void VulkanRenderTarget::FillEllipse(float cx, float cy, float rx, float ry, Brush* brush)
{
    TouchFrame();
    // Gradient ellipse: in-order vertex-colour fan over a LOCAL-space ellipse
    // perimeter (TryRecordGradientFanInOrder applies the transform internally),
    // so the gradient renders truly instead of vanishing at TryGetSolidBrushColor
    // below. Solid/image brushes fall through (the fan bails for non-gradient).
    if (brush && rx > 0.0f && ry > 0.0f) {
        const auto gradXf = GetCurrentTransform();
        const float gradSx = std::sqrt(gradXf.m11 * gradXf.m11 + gradXf.m21 * gradXf.m21);
        const float gradSy = std::sqrt(gradXf.m12 * gradXf.m12 + gradXf.m22 * gradXf.m22);
        const int gradSeg = VulkanEllipseSegmentCount(rx, ry, gradSx, gradSy);
        std::vector<float> gradPerimeter;
        gradPerimeter.reserve(static_cast<size_t>(gradSeg) * 2u);
        const float gradInv = 1.0f / static_cast<float>(gradSeg);
        for (int gradI = 0; gradI < gradSeg; ++gradI) {
            const float gradA = static_cast<float>(gradI) * gradInv * 6.28318530718f;
            gradPerimeter.push_back(cx + std::cos(gradA) * rx);
            gradPerimeter.push_back(cy + std::sin(gradA) * ry);
        }
        if (TryRecordGradientFanInOrder(gradPerimeter, cx, cy, brush)) {
            return;
        }
    }

    if (!TryRecordGpuEllipseFillCommand(cx, cy, rx, ry, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!TryGetSolidBrushColor(brush, b, g, r, a) || rx <= 0 || ry <= 0) {
        return;
    }

    const auto transform = GetCurrentTransform();
    // Per-axis scale magnitudes of the current transform — captures both
    // user transform and the root DPI scale push (BeginDraw).
    const float scaleX = std::sqrt(transform.m11 * transform.m11 + transform.m21 * transform.m21);
    const float scaleY = std::sqrt(transform.m12 * transform.m12 + transform.m22 * transform.m22);
    const int segments = VulkanEllipseSegmentCount(rx, ry, scaleX, scaleY);
    const float invSeg = 1.0f / static_cast<float>(segments);

    std::vector<float> points;
    points.reserve(static_cast<size_t>(segments) * 2u);
    for (int index = 0; index < segments; ++index) {
        const float angle = static_cast<float>(index) * invSeg * 6.28318530718f;
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
    // Gradient ring: true per-vertex gradient via StrokePath over a tessellated
    // local-space ellipse outline (same tessellation the solid path uses). Solid/
    // image brushes fall through to the GPU stroke record below.
    if (brush && rx > 0.0f && ry > 0.0f) {
        EngineBrushData gbd {};
        std::vector<EngineBrushData::GradientStop> gstops;
        if (BuildEngineBrush(brush, GetCurrentOpacity(), gbd, gstops) && gbd.type != 0) {
            const auto gt = GetCurrentTransform();
            const float gsx = std::sqrt(gt.m11 * gt.m11 + gt.m21 * gt.m21);
            const float gsy = std::sqrt(gt.m12 * gt.m12 + gt.m22 * gt.m22);
            const int gseg = VulkanEllipseSegmentCount(rx, ry, gsx, gsy);
            std::vector<float> gcmds;
            gcmds.reserve(static_cast<size_t>(gseg) * 3u);
            const float ginv = 1.0f / static_cast<float>(gseg);
            for (int gi = 1; gi < gseg; ++gi) {
                const float ga = static_cast<float>(gi) * ginv * 6.28318530718f;
                gcmds.push_back(0.0f);
                gcmds.push_back(cx + std::cos(ga) * rx);
                gcmds.push_back(cy + std::sin(ga) * ry);
            }
            StrokePath(cx + rx, cy, gcmds.data(), static_cast<uint32_t>(gcmds.size()),
                       brush, strokeWidth, /*closed*/ true, /*lineJoin*/ 2, /*miterLimit*/ 10.0f,
                       /*lineCap*/ 0, nullptr, 0, 0.0f, /*edgeMode*/ -1);
            return;
        }
    }
    if (!TryRecordGpuEllipseStrokeCommand(cx, cy, rx, ry, strokeWidth, brush)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    // Gradient ring renders as a representative solid (stop average) instead of
    // dropping on the CPU stroke path.
    if (!TryGetApproximateBrushColor(brush, b, g, r, a) || rx <= 0 || ry <= 0) {
        return;
    }

    const auto transform = GetCurrentTransform();
    const float scaleX = std::sqrt(transform.m11 * transform.m11 + transform.m21 * transform.m21);
    const float scaleY = std::sqrt(transform.m12 * transform.m12 + transform.m22 * transform.m22);
    const int segments = VulkanEllipseSegmentCount(rx, ry, scaleX, scaleY);
    const float invSeg = 1.0f / static_cast<float>(segments);

    std::vector<float> points;
    points.reserve(static_cast<size_t>(segments) * 2u);
    for (int index = 0; index < segments; ++index) {
        const float angle = static_cast<float>(index) * invSeg * 6.28318530718f;
        float worldX = 0.0f;
        float worldY = 0.0f;
        ApplyTransform(transform, cx + std::cos(angle) * rx, cy + std::sin(angle) * ry, worldX, worldY);
        points.push_back(worldX);
        points.push_back(worldY);
    }
    StrokePolyline(points, true, strokeWidth, b, g, r, a);
}

// Batch ellipse fill — wire format matches D3D12RenderTarget::FillEllipseBatch:
// stride = 5 floats per element { cx, cy, rx, ry, packedRGBA }, where
// packedRGBA is a uint32 (R | G<<8 | B<<16 | A<<24) stored as float bits.
// Reuses the per-element FillEllipse path so each ellipse rides the SDF AA
// pipeline we added to solid_rect.frag — no special batch GPU pipeline.
void VulkanRenderTarget::FillEllipseBatch(const float* data, uint32_t count)
{
    if (!data || count == 0) return;
    TouchFrame();

    constexpr uint32_t kStride = 5;
    for (uint32_t i = 0; i < count; ++i) {
        const uint32_t base = i * kStride;
        const float cx = data[base + 0];
        const float cy = data[base + 1];
        const float rx = data[base + 2];
        const float ry = data[base + 3];

        uint32_t packed;
        std::memcpy(&packed, &data[base + 4], sizeof(uint32_t));
        const uint8_t r = static_cast<uint8_t>(packed         & 0xFFu);
        const uint8_t g = static_cast<uint8_t>((packed >> 8)  & 0xFFu);
        const uint8_t b = static_cast<uint8_t>((packed >> 16) & 0xFFu);
        const uint8_t a = static_cast<uint8_t>((packed >> 24) & 0xFFu);

        // Construct a transient solid brush and route through FillEllipse so
        // we hit the same GPU SDF path + CPU rasterize fallback as the
        // per-call API. Allocating a brush per iteration is cheap (the
        // VulkanSolidBrush constructor is a few field assignments) and
        // matches how D3D12's FillEllipseBatch loops over individual SDF
        // instances — both backends accept the same N draws of overhead.
        VulkanSolidBrush brush(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
        FillEllipse(cx, cy, rx, ry, &brush);
    }
}

void VulkanRenderTarget::DrawLine(float x1, float y1, float x2, float y2, Brush* brush, float strokeWidth)
{
    TouchFrame();

    // Preferred GPU path: 3-strip manual AA (core + 2 feather strips with
    // per-vertex alpha ramp). Matches D3D12RenderTarget::DrawLine pixel-for-
    // pixel; eliminates the GPU rasterizer's staircase aliasing on oblique
    // strokes that the old GpuLineCommand → SolidRect quad path showed.
    bool aaRecorded = TryRecordGpuLineAACommand(x1, y1, x2, y2, strokeWidth, brush);
    if (!aaRecorded) {
        // Fallback: SolidRect quad (no AA). Triggered when AA path can't
        // record (e.g. effect capture / transition slot in progress) but
        // GpuReplay is still otherwise viable.
        if (!TryRecordGpuLineCommand(x1, y1, x2, y2, strokeWidth, brush)) {
            /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
        }
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    // Gradient line renders as a representative solid (stop average) instead of
    // dropping on the CPU stroke path.
    if (!TryGetApproximateBrushColor(brush, b, g, r, a)) {
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

    // Route through the active rendering engine when available.
    if (points && pointCount >= 3) {
        IRenderingEngine* engine = nullptr;
        if (IsImpellerActive() && impellerEngine_) engine = impellerEngine_.get();
        else if (IsVelloActive() && velloEngine_)  engine = velloEngine_.get();
        if (engine) {
            // Route solid AND gradient brushes through the engine. BuildEngineBrush
            // packs linear/radial stops into bd; the Impeller engine bakes a true
            // per-vertex gradient (ImpellerVulkanEngine::EncodeFillPolygon gradient
            // branch). Previously only solids reached the engine (TryGetSolidBrushColor
            // gate + hardcoded bd.type=0), so gradient-filled polygons collapsed to a
            // flat stop-average on the replay fallback.
            float opacity = GetCurrentOpacity();
            EngineBrushData bd;
            std::vector<EngineBrushData::GradientStop> stopStore;
            if (BuildEngineBrush(brush, opacity, bd, stopStore)) {
                auto t = GetCurrentTransform();
                EngineTransform et;
                et.m11 = t.m11; et.m12 = t.m12;
                et.m21 = t.m21; et.m22 = t.m22;
                et.dx = t.dx; et.dy = t.dy;

                FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
                // Producer-side clip sync: snapshot the live clipStack_ AABB
                // scissor into the engine so the batch PushBatch is about to
                // emit gets clipped (ScrollViewer / rounded Border / card).
                SyncClipToEngine(engine);
                if (engine->EncodeFillPolygon(points, pointCount, bd, fr, et))
                    return;
            }
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
    // Gradient stroke: route through StrokePath so the Impeller/Vello engine bakes
    // a TRUE per-vertex gradient stroke (same engine path StrokePath/DrawContentBorder
    // use), instead of collapsing to a representative solid via TryGetApproximateBrushColor
    // below. Solid/image brushes fall through to the existing fast path unchanged.
    if (brush && points && pointCount >= 2) {
        EngineBrushData gbd {};
        std::vector<EngineBrushData::GradientStop> gstops;
        if (BuildEngineBrush(brush, GetCurrentOpacity(), gbd, gstops) && gbd.type != 0) {
            std::vector<float> gcmds;
            gcmds.reserve(static_cast<size_t>(pointCount - 1) * 3u);
            for (uint32_t i = 1; i < pointCount; ++i) {
                gcmds.push_back(0.0f);                 // tag 0 = LineTo
                gcmds.push_back(points[i * 2]);
                gcmds.push_back(points[i * 2 + 1]);
            }
            StrokePath(points[0], points[1], gcmds.data(), static_cast<uint32_t>(gcmds.size()),
                       brush, strokeWidth, closed, lineJoin, miterLimit, /*lineCap*/ 0,
                       nullptr, 0, 0.0f, /*edgeMode*/ -1);
            return;
        }
    }
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
    // Gradient polyline renders as a representative solid (stop average) instead
    // of dropping on the CPU stroke path.
    if (!points || pointCount < 2 || !TryGetApproximateBrushColor(brush, b, g, r, a)) {
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

void VulkanRenderTarget::FillPath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, int32_t fillRule, int32_t edgeMode)
{
    TouchFrame();

    // Route through the active rendering engine when available.
    {
        IRenderingEngine* engine = nullptr;
        if (IsImpellerActive() && impellerEngine_) engine = impellerEngine_.get();
        else if (IsVelloActive() && velloEngine_)  engine = velloEngine_.get();
        if (engine) {
            // Route solid AND gradient brushes through the engine. BuildEngineBrush
            // packs linear/radial stops into bd; the Impeller engine bakes a true
            // per-vertex gradient (ImpellerVulkanEngine::EncodeFillPath gradient
            // branch at brush.type 1/2/3). Previously only solids reached the engine
            // (TryGetSolidBrushColor gate + hardcoded bd.type=0), so gradient-filled
            // glyph geometry — e.g. a gradient headline drawn as text outlines — fell
            // through to the flat stop-average path and vanished on dark backgrounds.
            float opacity = GetCurrentOpacity();
            EngineBrushData bd;
            std::vector<EngineBrushData::GradientStop> stopStore;
            if (BuildEngineBrush(brush, opacity, bd, stopStore)) {
                auto t = GetCurrentTransform();
                EngineTransform et;
                et.m11 = t.m11; et.m12 = t.m12;
                et.m21 = t.m21; et.m22 = t.m22;
                et.dx = t.dx; et.dy = t.dy;

                FillRule fr = (fillRule == 1) ? FillRule::NonZero : FillRule::EvenOdd;
                // Producer-side clip sync (see FillPolygon): clip glyph-outline /
                // SVG Path fills to the active clip before the batch is emitted.
                SyncClipToEngine(engine);
                if (engine->EncodeFillPath(startX, startY, commands, commandLength, bd, fr, et, edgeMode))
                    return;
            }
        }
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    if (!commands || commandLength == 0 || !TryGetSolidBrushColor(brush, b, g, r, a)) {
        return;
    }

    // Cache lookup: same path data + same scale-bucket → same local-space
    // decomposition and triangulation. Bezier decompose is O(N); ear-clip
    // triangulation is O(N³). Both are paid only on the first frame that
    // sees a given path; later frames hit the cache and the per-call work
    // shrinks to applying the current transform to the cached vertex list
    // (O(N), trivial). scaleBucket partitions by transform-scale octave so
    // a 1.0× icon and a 4.0× icon get separate cache entries — the cached
    // vertex density actually matches the on-screen scale.
    const auto curT = GetCurrentTransform();
    // Fold the path-quality knob into the effective scale so a higher
    // SetPathMsaaSampleCount yields denser curve tessellation AND a distinct
    // cache bucket (no cache flush needed when the quality changes).
    const float curMaxScale = MaxScaleFromMatrix(curT.m11, curT.m12, curT.m21, curT.m22)
                              * PathTessellationQualityScale();
    const uint32_t curScaleBucket = ScaleBucketFromMaxScale(curMaxScale);
    const uint64_t pathHash = HashPathInput(startX, startY, commands, commandLength, fillRule, curScaleBucket);

    std::shared_ptr<const CachedPathGeometry> geometry;
    if (auto hit = pathCache_->FindAndTouch(pathHash)) {
        geometry = std::move(hit->entry);
    } else {
        auto fresh = std::make_shared<CachedPathGeometry>();
        {
            path_stats::ScopedFlattenTimer flattenTimer(commandLength);
            DecomposePathToLocalPoints(startX, startY, commands, commandLength,
                                       fresh->localPoints, curMaxScale);
            flattenTimer.RecordOutputVerts(fresh->localPoints.size() / 2);
        }
        std::vector<float> tri;
        {
            path_stats::ScopedTriangulateTimer triTimer;
            bool ok = TriangulateSimplePolygon(fresh->localPoints, tri);
            if (ok) triTimer.MarkOk();
            if (ok) {
                fresh->localTriangles = std::move(tri);
                fresh->triangulationSucceeded = true;
            }
        }
        pathCache_->Insert(pathHash, fresh);
        geometry = std::move(fresh);
    }

    // Multi-subpath signal from DecomposePathToLocalPoints: when the path
    // opens more than one sub-path (e.g. an SVG icon with disjoint contours,
    // or a glyph outline with internal holes), the single-polygon cache /
    // ear-clip pipeline can't represent it. Mirror D3D12's CPU fallback —
    // FlattenPathToContours + TriangulateCompoundPath — which handles both
    // disjoint contours and fill-rule-correct holes. The result is emitted
    // through the world-space pre-triangulated path so it still rides the
    // GPU replay (no fallback to bitmap blit) and the entire frame stays
    // on the GPU path.
    if (geometry->localPoints.empty()) {
        const auto multiTransform = GetCurrentTransform();
        std::vector<Contour> contours = FlattenPathToContours(
            startX, startY, commands, commandLength, 0.5f);
        contours.erase(
            std::remove_if(contours.begin(), contours.end(),
                [](const Contour& c) { return c.VertexCount() < 3; }),
            contours.end());
        if (contours.empty()) return;

        // Bake current transform into the points before triangulation so the
        // world-space output is what the GPU path expects.
        for (auto& c : contours) {
            for (size_t pi = 0; pi + 1 < c.points.size(); pi += 2) {
                float wx = 0.0f, wy = 0.0f;
                ApplyTransform(multiTransform, c.points[pi], c.points[pi + 1], wx, wy);
                c.points[pi] = wx;
                c.points[pi + 1] = wy;
            }
        }

        std::vector<float> triVerts;
        if (TriangulateCompoundPath(contours, fillRule, triVerts) && triVerts.size() >= 6) {
            TryRecordPreTriangulatedFilledPolygon(std::move(triVerts), brush);
            if (cpuRasterNeeded_) {
                // CPU canvas needs world-space points — same as cached path.
                std::vector<float> flatWorld;
                for (auto& c : contours) {
                    flatWorld.insert(flatWorld.end(), c.points.begin(), c.points.end());
                }
                RasterizePolygon(flatWorld, fillRule, b, g, r, a);
            }
            return;
        }
        // Even compound triangulation bailed (degenerate / self-intersecting).
        // Fall through to nothing — the icon was unrenderable. We avoid
        // RasterizePolygonToGpuBitmap because that path expects a single
        // polygon outline; compound contours would smear.
        return;
    }

    if (geometry->localPoints.size() < 4) {
        return;
    }

    const auto transform = GetCurrentTransform();

    if (geometry->triangulationSucceeded) {
        // Fast GPU path: zero-copy share the cached local-space triangle
        // list with the GPU command, and let the vertex shader apply the
        // affine transform from a push constant. No CPU per-vertex work.
        // The shared_ptr aliasing constructor ties the shared lifetime to
        // the cache entry — when the cache evicts and the recorded command
        // also drops its ref, the underlying vector is freed.
        auto sharedTris = std::shared_ptr<const std::vector<float>>(
            geometry, &geometry->localTriangles);
        TryRecordSharedLocalFilledPolygon(std::move(sharedTris), transform, brush);
    } else {
        // Triangulation failed for this path (multi-subpath SVG icon,
        // self-intersecting outline). Fall back to CPU rasterization into a
        // local bitmap; the frame still stays on the GPU replay path and
        // the icon shows. RasterizePolygonToGpuBitmap needs world-space pts.
        std::vector<float> worldPoints;
        worldPoints.reserve(geometry->localPoints.size());
        for (size_t i = 0; i + 1 < geometry->localPoints.size(); i += 2) {
            float wx = 0.0f, wy = 0.0f;
            ApplyTransform(transform, geometry->localPoints[i], geometry->localPoints[i + 1], wx, wy);
            worldPoints.push_back(wx);
            worldPoints.push_back(wy);
        }
        RasterizePolygonToGpuBitmap(worldPoints, fillRule, b, g, r, a);
    }

    // CPU canvas update — RasterizePolygon is a self-guarded no-op when the
    // frame is on the GPU replay path (cpuRasterNeeded_ = false). Only when
    // we've already committed to CPU rasterization this frame does it have
    // to do real work, in which case it needs world-space points.
    if (cpuRasterNeeded_) {
        std::vector<float> worldPoints;
        worldPoints.reserve(geometry->localPoints.size());
        for (size_t i = 0; i + 1 < geometry->localPoints.size(); i += 2) {
            float wx = 0.0f, wy = 0.0f;
            ApplyTransform(transform, geometry->localPoints[i], geometry->localPoints[i + 1], wx, wy);
            worldPoints.push_back(wx);
            worldPoints.push_back(wy);
        }
        RasterizePolygon(worldPoints, fillRule, b, g, r, a);
    }
}

void VulkanRenderTarget::StrokePath(float startX, float startY, const float* commands, uint32_t commandLength, Brush* brush, float strokeWidth, bool closed, int32_t lineJoin, float miterLimit, int32_t lineCap, const float* dashPattern, uint32_t dashCount, float dashOffset, int32_t edgeMode)
{
    TouchFrame();

    // Route through Impeller engine when active
    {
        IRenderingEngine* engine = nullptr;
        if (IsImpellerActive() && impellerEngine_) engine = impellerEngine_.get();
        else if (IsVelloActive() && velloEngine_)  engine = velloEngine_.get();
        if (engine) {
            auto t = GetCurrentTransform();
            float opacity = GetCurrentOpacity();

            // Solid OR linear/radial gradient. Gradients now encode a TRUE
            // per-vertex gradient stroke in the Impeller engine (see
            // ImpellerVulkanEngine::EncodeStrokePath); solids keep bd.type=0.
            EngineBrushData bd;
            std::vector<EngineBrushData::GradientStop> stopStore;
            if (BuildEngineBrush(brush, opacity, bd, stopStore)) {
                EngineTransform et;
                et.m11 = t.m11; et.m12 = t.m12;
                et.m21 = t.m21; et.m22 = t.m22;
                et.dx = t.dx; et.dy = t.dy;

                // Producer-side clip sync (see FillPolygon): clip stroked Path /
                // vector-text-stroke / stroked icons to the active clip before
                // the batch is emitted.
                SyncClipToEngine(engine);
                if (engine->EncodeStrokePath(startX, startY, commands, commandLength,
                        bd, strokeWidth, closed, lineJoin, miterLimit, lineCap,
                        dashPattern, dashCount, dashOffset, et, edgeMode))
                    return;
            }
        }
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    // Gradient stroke renders as a representative solid (stop average) instead of
    // dropping on the CPU stroke path.
    if (!commands || commandLength == 0 || !TryGetApproximateBrushColor(brush, b, g, r, a)) {
        return;
    }

    // CPU fallback uses the same shared core flattener as D3D12
    // (jalium_triangulate.h::FlattenPathCommands). The previous hand-rolled
    // loop dropped ArcTo entirely (tag 4 fell through to `break`), which made
    // any SVG path stroke with an arc visually truncate. FlattenPathCommands
    // handles ArcTo via FlattenSvgArc and uses adaptive Bezier subdivision
    // for cubic/quad — matching D3D12 stroke geometry exactly.
    // Finer flatten tolerance at higher path-quality settings (Vulkan analogue
    // of D3D12's path MSAA). 0.5px at quality 1.0 → ~0.29px at quality 8.
    const float strokeTolerance = 0.5f / PathTessellationQualityScale();
    std::vector<float> localPoints = FlattenPathCommands(
        startX, startY, commands, commandLength, strokeTolerance);
    if (localPoints.size() < 4) {
        return;
    }

    const auto transform = GetCurrentTransform();
    std::vector<float> points;
    points.reserve(localPoints.size());
    for (size_t i = 0; i + 1 < localPoints.size(); i += 2) {
        float wx = 0.0f, wy = 0.0f;
        ApplyTransform(transform, localPoints[i], localPoints[i + 1], wx, wy);
        points.push_back(wx);
        points.push_back(wy);
    }

    if (!TryRecordGpuPolylineCommand(localPoints, closed, strokeWidth, brush)) {
        // Stroke couldn't be expressed as a polyline command (e.g. dashed
        // stroke, non-miter join, or just long enough to exceed
        // TryRecordGpuLineCommand's per-segment budget). Rasterize into a
        // local bitmap so the outline still shows up.
        RasterizePolylineToGpuBitmap(points, closed, strokeWidth, b, g, r, a);
    }

    // StrokePolyline writes into the CPU canvas pixelBuffer_ — it self-guards
    // on cpuRasterNeeded_ so this is a no-op when the frame stays on the GPU
    // replay path.
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
        // Gradient border stroke renders as a representative solid (stop average).
        if (TryGetApproximateBrushColor(strokeBrush, b, g, r, a)) {
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

void VulkanRenderTarget::PushClipAliased(float x, float y, float w, float h)
{
    // D3D12 distinguishes aliased clip (binary pixel coverage) from the
    // standard PushClip purely as an AA hint for the SDF rect shader. The
    // Vulkan SolidRect path uses analytical SDF coverage on rect edges
    // regardless, so we route aliased clip through the same code path —
    // the visual difference at clip edges is sub-pixel and the existing
    // ClipState model has no carve-out for "aliased". When the SDF path is
    // extended with an aliased-clip uniform later, this is the entry point
    // to flag it.
    PushClip(x, y, w, h);
}

void VulkanRenderTarget::PushRoundedRectClip(float x, float y, float w, float h, float rx, float ry)
{
    // Symmetric variant: forward to per-corner with min(rx, ry) on every corner
    // so the SDF stays circular regardless of any non-uniform XY scale that
    // sneaks in via the captured transform.
    float r = std::min(rx, ry);
    PushPerCornerRoundedRectClip(x, y, w, h, r, r, r, r);
}

void VulkanRenderTarget::PushPerCornerRoundedRectClip(float x, float y, float w, float h,
    float tl, float tr, float br, float bl)
{
    TouchFrame();

    ClipState clip {};
    clip.rounded = true;
    clip.x = x;
    clip.y = y;
    clip.w = w;
    clip.h = h;
    clip.radiusTL = tl;
    clip.radiusTR = tr;
    clip.radiusBR = br;
    clip.radiusBL = bl;
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
void VulkanRenderTarget::SetShapeType(int type, float n)
{
    currentShapeType_ = type;
    currentShapeExponent_ = (n > 0.0f) ? n : 4.0f;
}

void VulkanRenderTarget::BuildSuperEllipsePolygon(float x, float y, float w, float h,
                                                  float tl, float tr, float br, float bl,
                                                  std::vector<float>& pts) const
{
    pts.clear();
    if (w <= 0.0f || h <= 0.0f) return;

    // iOS squircle = ordinary rounded rectangle whose corner ARCS use a Lamé
    // (continuous-curvature) curve instead of a circular one, with STRAIGHT
    // edges in between. The earlier implementation drew a full-rect superellipse
    // (|X/(w/2)|^n + |Y/(h/2)|^n = 1), which on a wide/short element (a nav bar,
    // a card) collapses into a flattened oval — the "压扁 / squished" shape. Here
    // each corner is a Lamé quarter of its OWN radius and the edges stay straight.
    const float maxR = std::min(w, h) * 0.5f;
    tl = std::clamp(tl, 0.0f, maxR);
    tr = std::clamp(tr, 0.0f, maxR);
    br = std::clamp(br, 0.0f, maxR);
    bl = std::clamp(bl, 0.0f, maxR);

    const float n = std::max(2.0f, currentShapeExponent_);  // 2 = circular, 4 = iOS squircle
    const float e = 2.0f / n;
    constexpr float kPi = 3.14159265358979323846f;
    constexpr int kSeg = 10;  // segments per corner arc

    // Quarter Lamé corner: centre (ccx,ccy) inset by r from the two edges; sweep
    // angle a0→a1 (screen space, y down). r<=0 collapses to the square corner.
    auto corner = [&](float ccx, float ccy, float r, float a0, float a1) {
        if (r <= 0.01f) { pts.push_back(ccx); pts.push_back(ccy); return; }
        for (int i = 0; i <= kSeg; ++i) {
            const float t  = a0 + (a1 - a0) * (static_cast<float>(i) / static_cast<float>(kSeg));
            const float ct = std::cos(t);
            const float st = std::sin(t);
            const float sx = (ct < 0.0f) ? -1.0f : 1.0f;
            const float sy = (st < 0.0f) ? -1.0f : 1.0f;
            pts.push_back(ccx + sx * std::pow(std::fabs(ct), e) * r);
            pts.push_back(ccy + sy * std::pow(std::fabs(st), e) * r);
        }
    };

    pts.reserve(static_cast<size_t>(kSeg + 1) * 4u * 2u);
    corner(x + tl,     y + tl,     tl, kPi,        1.5f * kPi);  // top-left
    corner(x + w - tr, y + tr,     tr, 1.5f * kPi, 2.0f * kPi);  // top-right
    corner(x + w - br, y + h - br, br, 0.0f,       0.5f * kPi);  // bottom-right
    corner(x + bl,     y + h - bl, bl, 0.5f * kPi, kPi);         // bottom-left
}
void VulkanRenderTarget::SetVSyncEnabled(bool enabled) { vsyncEnabled_ = enabled; }

void VulkanRenderTarget::SetPathMsaaSampleCount(uint32_t sampleCount) {
    // Clamp to the same {1,2,4,8} set D3D12 accepts. Folded into the path
    // tessellation scale (see PathTessellationQualityScale); a changed value
    // naturally re-tessellates because FillPath keys its geometry cache by the
    // boosted scale bucket, so stale lower-quality entries are simply bypassed.
    uint32_t clamped = (sampleCount >= 8) ? 8u : (sampleCount >= 4) ? 4u : (sampleCount >= 2) ? 2u : 1u;
    pathMsaaSampleCount_ = clamped;
}
void VulkanRenderTarget::SetDpi(float dpiX, float dpiY) { dpiX_ = dpiX; dpiY_ = dpiY; }
// ── Dirty-rect aggregation helpers ───────────────────────────────────────────
namespace {

constexpr size_t kVulkanMaxDirtyRects = 32;
constexpr float kVulkanDirtyAdjacencyEpsilon = 1.0f;
constexpr float kVulkanDirtyMergeWasteRatio = 0.3f;

inline bool RectContains(const JaliumRect& outer, const JaliumRect& inner) {
    return outer.x <= inner.x
        && outer.y <= inner.y
        && outer.x + outer.width >= inner.x + inner.width
        && outer.y + outer.height >= inner.y + inner.height;
}

inline bool RectsIntersect(const JaliumRect& a, const JaliumRect& b) {
    return a.x < b.x + b.width
        && b.x < a.x + a.width
        && a.y < b.y + b.height
        && b.y < a.y + a.height;
}

inline JaliumRect RectUnion(const JaliumRect& a, const JaliumRect& b) {
    float x0 = (std::min)(a.x, b.x);
    float y0 = (std::min)(a.y, b.y);
    float x1 = (std::max)(a.x + a.width, b.x + b.width);
    float y1 = (std::max)(a.y + a.height, b.y + b.height);
    return JaliumRect{ x0, y0, x1 - x0, y1 - y0 };
}

inline bool ShouldMergeVkRects(const JaliumRect& a, const JaliumRect& b) {
    if (RectsIntersect(a, b)) return true;

    bool xClose = a.x + a.width + kVulkanDirtyAdjacencyEpsilon >= b.x
        && b.x + b.width + kVulkanDirtyAdjacencyEpsilon >= a.x;
    bool yClose = a.y + a.height + kVulkanDirtyAdjacencyEpsilon >= b.y
        && b.y + b.height + kVulkanDirtyAdjacencyEpsilon >= a.y;
    if (xClose && yClose) return true;

    float aArea = a.width * a.height;
    float bArea = b.width * b.height;
    auto u = RectUnion(a, b);
    float uArea = u.width * u.height;
    float waste = uArea - (aArea + bArea);
    float larger = (std::max)(aArea, bArea);
    if (larger <= 0.0f) return false;
    return waste / larger <= kVulkanDirtyMergeWasteRatio;
}

} // namespace

void VulkanRenderTarget::AddDirtyRect(float x, float y, float w, float h)
{
    if (fullInvalidation_) return;
    if (w <= 0.0f || h <= 0.0f) return;

    JaliumRect r{ x, y, w, h };

    // 1. Absorption.
    for (const auto& existing : dirtyRects_) {
        if (RectContains(existing, r)) return;
    }

    // 2. Replacement — drop rects that r fully swallows.
    for (size_t i = dirtyRects_.size(); i-- > 0; ) {
        if (RectContains(r, dirtyRects_[i])) {
            dirtyRects_.erase(dirtyRects_.begin() + i);
        }
    }

    // 3. Beneficial merge to a fixed point.
    bool changed = true;
    while (changed) {
        changed = false;
        for (size_t i = 0; i < dirtyRects_.size(); i++) {
            if (ShouldMergeVkRects(dirtyRects_[i], r)) {
                r = RectUnion(dirtyRects_[i], r);
                dirtyRects_.erase(dirtyRects_.begin() + i);
                changed = true;
                break;
            }
        }
    }

    dirtyRects_.push_back(r);

    // 4. Capacity — minimum-waste forced merges. No more "give up → full".
    while (dirtyRects_.size() > kVulkanMaxDirtyRects) {
        size_t bestI = 0, bestJ = 1;
        float bestExtra = std::numeric_limits<float>::max();
        for (size_t i = 0; i < dirtyRects_.size(); i++) {
            float ai = dirtyRects_[i].width * dirtyRects_[i].height;
            for (size_t j = i + 1; j < dirtyRects_.size(); j++) {
                auto u = RectUnion(dirtyRects_[i], dirtyRects_[j]);
                float extra = u.width * u.height - ai - dirtyRects_[j].width * dirtyRects_[j].height;
                if (extra < bestExtra) {
                    bestExtra = extra;
                    bestI = i;
                    bestJ = j;
                }
            }
        }
        auto merged = RectUnion(dirtyRects_[bestI], dirtyRects_[bestJ]);
        dirtyRects_.erase(dirtyRects_.begin() + bestJ);
        dirtyRects_.erase(dirtyRects_.begin() + bestI);
        dirtyRects_.push_back(merged);
    }
}
void VulkanRenderTarget::SetFullInvalidation() { fullInvalidation_ = true; dirtyRects_.clear(); }
void VulkanRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity)
{
    DrawBitmap(bitmap, x, y, w, h, opacity, 0 /* JALIUM_BITMAP_SCALING_UNSPECIFIED */);
}

void VulkanRenderTarget::DrawBitmap(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode)
{
    // Image draws apply the opacity stack once via the recorder's
    // opacity*GetCurrentOpacity(); only RenderText's pre-baked glyph bitmap
    // passes opacityAlreadyBaked=true through DrawBitmapInternal.
    DrawBitmapInternal(bitmap, x, y, w, h, opacity, scalingMode, /*opacityAlreadyBaked=*/false);
}

void VulkanRenderTarget::DrawBitmapInternal(Bitmap* bitmap, float x, float y, float w, float h, float opacity, int scalingMode, bool opacityAlreadyBaked)
{
    TouchFrame();
    const bool _recBmp = TryRecordGpuBitmapCommand(bitmap, x, y, w, h, opacity, opacityAlreadyBaked);
    if (!_recBmp) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    if (!cpuRasterNeeded_) {
        return;
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

    // Map JaliumBitmapScalingMode → CPU sampler kernel.
    // NearestNeighbor (3) is the only mode that gets nearest sampling.
    // Everything else (Unspecified, LowQuality, HighQuality, Linear, Fant)
    // uses bilinear — bilinear is the right CPU-fallback default because
    // the GPU path already gets anisotropic via frameSampler.
    const bool useNearest = (scalingMode == 3 /* NearestNeighbor */);
    const uint32_t srcW = sourceBitmap->GetWidth();
    const uint32_t srcH = sourceBitmap->GetHeight();
    const float invW = 1.0f / std::max(0.0001f, w);
    const float invH = 1.0f / std::max(0.0001f, h);

    for (int destY = destTop; destY < destBottom; ++destY) {
        for (int destX = destLeft; destX < destRight; ++destX) {
            float localX = 0.0f;
            float localY = 0.0f;
            ApplyTransform(inverse, static_cast<float>(destX) + 0.5f, static_cast<float>(destY) + 0.5f, localX, localY);
            if (localX < x || localY < y || localX > x + w || localY > y + h) {
                continue;
            }

            const float u = (localX - x) * invW;
            const float v = (localY - y) * invH;

            uint8_t srcB, srcG, srcR, srcA;
            if (useNearest) {
                const uint32_t sx = std::min<uint32_t>(srcW - 1, static_cast<uint32_t>(u * srcW));
                const uint32_t sy = std::min<uint32_t>(srcH - 1, static_cast<uint32_t>(v * srcH));
                const size_t off = (static_cast<size_t>(sy) * srcW + sx) * 4u;
                srcB = pixels[off + 0];
                srcG = pixels[off + 1];
                srcR = pixels[off + 2];
                srcA = pixels[off + 3];
            } else {
                // Bilinear sampling — sample four texels around the unit-pixel
                // centre and blend by fractional position. Half-texel offset
                // matches the GPU pixel-centre convention so a 1:1-mapped
                // bitmap is byte-identical to its source.
                const float fx = u * srcW - 0.5f;
                const float fy = v * srcH - 0.5f;
                const int ix0 = std::clamp(static_cast<int>(std::floor(fx)), 0, static_cast<int>(srcW) - 1);
                const int iy0 = std::clamp(static_cast<int>(std::floor(fy)), 0, static_cast<int>(srcH) - 1);
                const int ix1 = std::min<int>(ix0 + 1, static_cast<int>(srcW) - 1);
                const int iy1 = std::min<int>(iy0 + 1, static_cast<int>(srcH) - 1);
                const float wx = std::clamp(fx - std::floor(fx), 0.0f, 1.0f);
                const float wy = std::clamp(fy - std::floor(fy), 0.0f, 1.0f);
                const size_t o00 = (static_cast<size_t>(iy0) * srcW + ix0) * 4u;
                const size_t o01 = (static_cast<size_t>(iy0) * srcW + ix1) * 4u;
                const size_t o10 = (static_cast<size_t>(iy1) * srcW + ix0) * 4u;
                const size_t o11 = (static_cast<size_t>(iy1) * srcW + ix1) * 4u;
                const float w00 = (1.0f - wx) * (1.0f - wy);
                const float w01 = wx * (1.0f - wy);
                const float w10 = (1.0f - wx) * wy;
                const float w11 = wx * wy;
                auto blend = [&](size_t channel) -> uint8_t {
                    const float v = pixels[o00 + channel] * w00
                                  + pixels[o01 + channel] * w01
                                  + pixels[o10 + channel] * w10
                                  + pixels[o11 + channel] * w11;
                    return static_cast<uint8_t>(std::clamp(v + 0.5f, 0.0f, 255.0f));
                };
                srcB = blend(0);
                srcG = blend(1);
                srcR = blend(2);
                srcA = blend(3);
            }

            srcA = static_cast<uint8_t>((srcA * opacityByte) / 255u);
            BlendPixel(destX, destY, srcB, srcG, srcR, srcA);
        }
    }
}
void VulkanRenderTarget::RenderText(const wchar_t* text, uint32_t textLength, TextFormat* format, float x, float y, float w, float h, Brush* brush)
{
    TouchFrame();

#ifdef __ANDROID__
    static int s_rtDbg = 0; const bool rtDbg = (s_rtDbg++ < 80);
    if (rtDbg) VK_LOG("[TXTDBG] enter len=%u fmt=%p brush=%p x=%.1f y=%.1f w=%.1f h=%.1f gpuReplay=%d hasClear=%d cpuRaster=%d", textLength, (void*)format, (void*)brush, x, y, w, h, (int)gpuReplaySupported_, (int)gpuReplayHasClear_, (int)cpuRasterNeeded_);
#endif

    if (!text || textLength == 0 || !format) {
        return;
    }

    uint8_t b = 0, g = 0, r = 0, a = 0;
    // Use the stop-average for gradient / non-solid foregrounds instead of dropping
    // the text entirely. The GDI/DIB and FreeType glyph paths below are single-
    // colour, so a true per-glyph gradient isn't expressible here — but rendering a
    // representative colour matches the rest of the Vulkan backend (DrawLine /
    // DrawPolygon) and is strictly better than gradient-foreground text vanishing.
    bool _gotBrush = TryGetApproximateBrushColor(brush, b, g, r, a);
#ifdef __ANDROID__
    if (rtDbg) VK_LOG("[TXTDBG] brush got=%d r=%u g=%u b=%u a=%u", (int)_gotBrush, r, g, b, a);
#endif
    if (!_gotBrush || a == 0) {
        return;
    }

#ifdef _WIN32
    // ── Dedicated DirectWrite glyph pipeline (B4b) ───────────────────────────
    // Replaces the legacy GDI DrawTextW bitmap path. We shape the run with
    // DirectWrite (VulkanTextFormat::CreateLayout, cached), rasterize glyphs into
    // the shared CPU glyph atlas (VulkanGlyphAtlas::GenerateGlyphs, cached per
    // layoutKey), transform the returned base-DIP quads to physical pixels, and
    // record a TextRun replay command. DrawReplayFrame uploads the atlas + glyph
    // SSBO and draws them through the text pipeline. This mirrors the proven D3D12
    // RenderText → AddText path (d3d12_direct_renderer.cpp) field-for-field.

    // GPU-replay-only on Windows — exactly as the old path, whose only sink
    // (RecordCachedTextBitmap) early-returned under these same conditions. When
    // the frame is presented through the CPU fallback (no GPU replay) or while an
    // effect capture / transition is active, text is not emitted here.
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) {
        return;
    }
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) {
        return;
    }

    auto* textFormat = static_cast<VulkanTextFormat*>(format);
    jalium::text_stats::AddDrawTextCall();

    // Lazily create the CPU glyph atlas (DirectWrite). No atlas → no text path.
    if (!impl_->EnsureGlyphAtlas() || !impl_->glyphAtlas_) {
        return;
    }

    Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
    uint64_t layoutKey = 0;
    if (FAILED(textFormat->CreateLayout(text, textLength, w, h, &layout, &layoutKey)) || !layout) {
        return;
    }

    // DPI / transform contract (matches the old bitmap path AND the D3D12 path):
    //  • (x, y) is the text box origin in LOCAL / base-DIP space (untransformed).
    //  • GetCurrentTransform() is the DIP→physical-pixel affine; on Vulkan it also
    //    carries the DPI root scale, so applying it lands us in the SAME physical-
    //    pixel space as `extent` (which is what the old RecordCachedTextBitmap fed
    //    the bitmap pipeline via ApplyTransform + cmd.bitmap.x/y).
    //  • txScaleX/txScaleY = the per-axis magnification (same sqrt(m11²+m12²) /
    //    sqrt(m21²+m22²) the OLD body computed for its rasterScale, and the SAME
    //    the D3D12 AddText hands GenerateGlyphs).
    // GenerateGlyphs emits quads relative to the origin we pass, divided back to
    // BASE-DIP (any internal rasterScale magnification removed), so we pass the
    // ALREADY-TRANSFORMED origin (tx, ty) and then re-magnify each quad by the
    // axis scales around (tx, ty) — identical to D3D12. Net mapping for a non-
    // rotated transform: physPos = txScale * localDIP + ApplyTransform(x, y),
    // physSize = txScale * sizeDIP, i.e. the glyph lands at the same on-screen
    // pixels + size the old bitmap text occupied.
    const auto transform = GetCurrentTransform();
    float tx = 0.0f, ty = 0.0f;
    ApplyTransform(transform, x, y, tx, ty);
    const float txScaleX = std::sqrt(transform.m11 * transform.m11 + transform.m12 * transform.m12);
    const float txScaleY = std::sqrt(transform.m21 * transform.m21 + transform.m22 * transform.m22);
    const float effectiveA = (static_cast<float>(a) / 255.0f) * GetCurrentOpacity();
    if (effectiveA <= 0.0f) {
        return;
    }

    // Colour byte→float mapping: TryGetApproximateBrushColor returns B,G,R,A
    // bytes; GenerateGlyphs wants STRAIGHT R,G,B,A floats (it premultiplies
    // internally). So R←r, G←g, B←b, A←effectiveA.
    const float colR = static_cast<float>(r) / 255.0f;
    const float colG = static_cast<float>(g) / 255.0f;
    const float colB = static_cast<float>(b) / 255.0f;

    // Rasterize glyphs at DISPLAY resolution, not base-DIP. On Vulkan the
    // transform's axis scale (txScaleX/Y) carries the DPI root scale, so on a
    // 150%/200% display the glyph bitmap must be fontSize*scale PHYSICAL pixels.
    // Without this, GenerateGlyphs rasterizes at base-DIP and the post-transform
    // re-magnify UPSCALES the bitmap — the exact "blurry text" symptom. SetDpiScale
    // bakes the scale into the atlas glyph size while scaleX/scaleY stay 1, so
    // GenerateGlyphs keeps the crisp 1:1 path (integer-snapped pen + 8 sub-pixel
    // buckets) rather than the continuous/bilinear scaled path. On-screen position
    // and size are unchanged (the re-magnify below still maps base-DIP→physical);
    // only the sampled atlas glyph becomes high-resolution. fontSize is rounded
    // into the GlyphKey so sub-epsilon scale jitter never thrashes the cache.
    // Isotropic DPI is the common case; max() picks the higher-res axis for the
    // rare anisotropic transform.
    // Anisotropic transforms (e.g. liquid-glass squeeze scaleX~0.63, scaleY~2.17)
    // must rasterize the glyph at the FINAL deformed pixel aspect via the atlas's
    // per-axis DWRITE_MATRIX path (GenerateGlyphs scaleX/scaleY) - otherwise an
    // isotropic high-res bitmap is point-minified in one axis and thin stems
    // vanish (the d->c / r->l mosaic). Isotropic DPI keeps the crisp 1:1 path.
    const bool anisoText = std::abs(txScaleX - txScaleY) > 0.01f * std::max(txScaleX, txScaleY);
    float glyphRasterScaleX = 1.0f;
    float glyphRasterScaleY = 1.0f;
    if (anisoText) {
        impl_->glyphAtlas_->SetDpiScale(1.0f);  // size carried by the per-axis deformation matrix
        glyphRasterScaleX = txScaleX;
        glyphRasterScaleY = txScaleY;
    } else {
        impl_->glyphAtlas_->SetDpiScale(std::max(txScaleX, txScaleY));
    }

    // Grayscale single-source for this batch (the text pipeline was created with
    // a grayscale .r-coverage shader + blend src=ONE). hintingMode 0 = Auto.
    std::vector<jalium::VkGlyphInstance> instances;
    std::vector<jalium::VulkanGlyphAtlas::TextDecorationRect> decorations;
    const uint32_t startIdx = 0;  // instances is local + fresh, so the run is [0, count)
    // ClearType is opt-in: only when the resolved AA mode is ClearType AND the
    // dual-source pipeline exists (else grayscale, the shipping default).
    const bool runClearType = (format->ResolveEffectiveTextRenderingMode() == JALIUM_TEXT_AA_CLEARTYPE)
                              && (impl_->textClearTypePipeline != VK_NULL_HANDLE);
    const uint32_t count = impl_->glyphAtlas_->GenerateGlyphs(
        layout.Get(), tx, ty, colR, colG, colB, effectiveA,
        instances, &decorations,
        layoutKey,
        /*aaMode=*/(runClearType ? JALIUM_TEXT_AA_CLEARTYPE : JALIUM_TEXT_AA_GRAYSCALE),
        /*hintingMode=*/format->GetTextHintingMode(),  // honor TextOptions.TextHintingMode (was hardcoded 0); parity with D3D12
        /*scaleX=*/glyphRasterScaleX, /*scaleY=*/glyphRasterScaleY);
    if (count == 0 || instances.empty()) {
        return;
    }

    // Re-magnify each base-DIP quad by the transform's axis scales around the
    // transformed origin (tx, ty) — byte-identical to D3D12 AddText. Identity
    // transform (scale 1) leaves the quads untouched.
    if (std::abs(txScaleX - 1.0f) > 0.001f || std::abs(txScaleY - 1.0f) > 0.001f) {
        for (uint32_t i = startIdx; i < startIdx + count && i < instances.size(); ++i) {
            auto& gi = instances[i];
            gi.posX = tx + (gi.posX - tx) * txScaleX;
            gi.posY = ty + (gi.posY - ty) * txScaleY;
            gi.sizeX *= txScaleX;
            gi.sizeY *= txScaleY;
        }
    }

    // Flatten to the 12-float-per-glyph SSBO layout the TextRun command expects
    // (posX,posY, sizeX,sizeY, uvMinX,uvMinY, uvMaxX,uvMaxY, colorR,colorG,colorB,
    // colorA — UVs + PREMULTIPLIED colour pass through unchanged).
    std::vector<float> glyphFloats;
    glyphFloats.reserve(static_cast<size_t>(count) * 12u);
    for (uint32_t i = startIdx; i < startIdx + count && i < instances.size(); ++i) {
        const auto& gi = instances[i];
        glyphFloats.push_back(gi.posX);
        glyphFloats.push_back(gi.posY);
        glyphFloats.push_back(gi.sizeX);
        glyphFloats.push_back(gi.sizeY);
        glyphFloats.push_back(gi.uvMinX);
        glyphFloats.push_back(gi.uvMinY);
        glyphFloats.push_back(gi.uvMaxX);
        glyphFloats.push_back(gi.uvMaxY);
        glyphFloats.push_back(gi.colorR);
        glyphFloats.push_back(gi.colorG);
        glyphFloats.push_back(gi.colorB);
        glyphFloats.push_back(gi.colorA);
    }
    const uint32_t glyphCount = static_cast<uint32_t>(glyphFloats.size() / 12u);
    if (glyphCount == 0) {
        return;
    }

    // Record the replay command, stamping the CURRENT clip/scissor exactly like
    // RecordCachedTextBitmap (the bitmap-text recorder). TryPopulateReplayClip
    // returns false for a rotated/sheared clip transform → drop (same as bitmap).
    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::TextRun;
    cmd.textRun.glyphs = std::move(glyphFloats);
    cmd.textRun.glyphCount = glyphCount;
    cmd.textRun.clearType = runClearType;
    if (!TryPopulateReplayClip(cmd)) {
        return;
    }
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) {
        return;
    }
    gpuReplayCommands_.push_back(std::move(cmd));

    // Text decorations (underline / strikethrough): emit each as a solid rect in
    // the SAME physical/screen space as the glyph quads. GenerateGlyphs returns
    // them at (origin + base-DIP offset) with PREMULTIPLIED colour; re-magnify the
    // position by the per-axis transform scale around (tx,ty) exactly like the
    // glyph quads above, and un-premultiply the colour for the straight-colour
    // solid_rect pipeline. Mirrors D3D12, which emits decorations as SdfRects.
    for (const auto& dec : decorations) {
        if (dec.colorA <= 0.0f) {
            continue;
        }
        const float decPhysX = tx + (dec.x - tx) * txScaleX;
        const float decPhysY = ty + (dec.y - ty) * txScaleY;
        const float decPhysW = dec.width * txScaleX;
        const float decPhysH = dec.thickness * txScaleY;
        if (decPhysW <= 0.0f || decPhysH <= 0.0f) {
            continue;
        }
        GpuReplayCommand decCmd {};
        decCmd.kind = GpuReplayCommandKind::SolidRect;
        if (!TryPopulateReplayClip(decCmd)) {
            continue;
        }
        if (decCmd.scissorRight <= decCmd.scissorLeft || decCmd.scissorBottom <= decCmd.scissorTop) {
            continue;
        }
        GpuSolidRectCommand decRect {};
        decRect.x = decPhysX;
        decRect.y = decPhysY;
        decRect.w = decPhysW;
        decRect.h = decPhysH;
        const float decInvA = 1.0f / dec.colorA;  // colour is premultiplied; solid_rect wants straight
        decRect.r = dec.colorR * decInvA;
        decRect.g = dec.colorG * decInvA;
        decRect.b = dec.colorB * decInvA;
        decRect.a = dec.colorA;
        decCmd.solidRect = decRect;
        gpuReplayCommands_.push_back(decCmd);
    }
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
#ifdef __ANDROID__
    if (rtDbg) VK_LOG("[TXTDBG] ftFormat=%p textEngine=%p (fmt dynamic_cast / GetFreeTypeFormat)", (void*)ftFormat, (void*)textEngine);
#endif
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
#ifdef __ANDROID__
    if (rtDbg) VK_LOG("[TXTDBG] GenerateGlyphQuads -> quads=%zu dpi=(%.1f,%.1f) renderScale=%.2f brushA=%.2f", quads.size(), dpiX_, dpiY_, renderScale, brushA);
#endif
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
#ifdef __ANDROID__
    if (rtDbg) VK_LOG("[TXTDBG] bbox=(%.1f,%.1f..%.1f,%.1f) scaled=(%.1f,%.1f) bitmap=%dx%d", bboxMinX, bboxMinY, bboxMaxX, bboxMaxY, scaledW, scaledH, bitmapWidth, bitmapHeight);
#endif
    if (bitmapWidth <= 0 || bitmapHeight <= 0) {
        return;
    }

    const float bitmapOffsetX = std::floor(bboxMinX);
    const float bitmapOffsetY = std::floor(bboxMinY);

    // Render glyphs into a temporary BGRA bitmap (STRAIGHT / unpremultiplied alpha;
    // see the per-pixel note below — the bitmap pipeline and CPU BlendPixel both
    // apply srcA themselves, so the colour channels must stay unmultiplied).
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

                // Straight (unpremultiplied) BGRA. DrawReplayFrame's bitmap pipeline
                // blends with VK_BLEND_FACTOR_SRC_ALPHA / VK_BLEND_FACTOR_ONE_MINUS_SRC_ALPHA
                // and the CPU replay path (BlendBuffer → BlendPixel) does the same, applying
                // srcA exactly once. Premultiplying the colour here (the old pmB/pmG/pmR
                // path) made the blend square the alpha — anti-aliased glyph edges (and all
                // of small text, which is mostly partial coverage) collapsed toward
                // coverage², so on Android/Vulkan text rendered effectively invisible while
                // SDF/vector content was unaffected. This is the identical bug commit
                // f8ffb2aa fixed for RasterizePolygon/PolylineToGpuBitmap. All glyphs in a
                // run share one colour, so RGB stays (r,g,b) straight and only the coverage
                // alpha composites (SrcOver) across overlapping glyphs.
                const size_t pixelIdx = (static_cast<size_t>(dy) * bitmapWidth + dx) * 4;
                const uint8_t existA = textPixels[pixelIdx + 3];
                const uint32_t outA = pixelA + existA * (255u - pixelA) / 255u;
                textPixels[pixelIdx + 0] = b;
                textPixels[pixelIdx + 1] = g;
                textPixels[pixelIdx + 2] = r;
                textPixels[pixelIdx + 3] = static_cast<uint8_t>(outA);
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
    // brushA above already baked GetCurrentOpacity() into the glyph bitmap's
    // alpha, so route through DrawBitmapInternal with opacityAlreadyBaked=true:
    // the GPU recorder must record the bitmap opacity as 1.0 verbatim rather
    // than multiplying GetCurrentOpacity() a SECOND time. The old DrawBitmap(..,
    // 1.0f) path squared the opacity (alpha ∝ opacity²), so text faded to
    // invisible far ahead of sibling content during entrance/exit animations.
    // The CPU fallback blit stays correct because the opacity is intrinsic to
    // the baked alpha and it composites at the passed 1.0.
    DrawBitmapInternal(&textBitmap, x + bitmapOffsetX * invScale, y + bitmapOffsetY * invScale,
        static_cast<float>(bitmapWidth) * invScale, static_cast<float>(bitmapHeight) * invScale, 1.0f,
        0 /* JALIUM_BITMAP_SCALING_UNSPECIFIED */, /*opacityAlreadyBaked=*/true);
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

    if (w <= 0.0f || h <= 0.0f) {
        return;
    }

    // Backdrop reads pixelBuffer_ to record the GPU command's source pixels and
    // to apply the CPU fallback blur. EnsureCpuRasterization brings pixelBuffer_
    // up to date when we're already on the CPU path; on the GPU replay path it
    // intentionally does nothing because committing to CPU rasterization
    // mid-frame would force every Draw* through the slow CPU path.
    EnsureCpuRasterization();

    if (pixelBuffer_.empty()) {
        return;
    }

    uint8_t tintB = 0, tintG = 0, tintR = 0;
    ParseTintColor(materialTint, 1.0f, 1.0f, 1.0f, tintB, tintG, tintR);

    PushTemporaryClip(x, y, w, h, cornerRadiusTL, cornerRadiusTL);

    // Always record the GPU command. backdrop_quad.frag.hlsl enforces a
    // non-zero alpha floor (`max(blurred.a, 0.08 + tintAlpha*0.25)`), so
    // even when the GPU replay path leaves pixelBuffer_ as initial zeros
    // the shader still composites a visible tint/glass overlay. PR #114
    // codex review (P1) flagged that the previous GPU-path short-circuit
    // dropped this overlay entirely.
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

    // CPU compositing only when we're already on the CPU rasterization path.
    // BlurPixels is ~6 ms of work on a 4.6 MB buffer; on the GPU path its
    // result is consumed only by BlendBuffer (which self-guards on
    // cpuRasterNeeded_), so the entire chain — blur, blend, tint fill — is
    // wasted. Skipping it keeps the GPU-side overlay intact (see above)
    // while removing the CPU spike that originally motivated this code path.
    if (cpuRasterNeeded_) {
        auto blurred = BlurPixels(pixelBuffer_, width_, height_, std::max(1, static_cast<int>(std::round(blurRadius))), x, y, w, h);
        BlendBuffer(blurred, width_, height_, x, y, w, h, 1.0f);
        FillSolidRect(
            static_cast<int>(std::floor(x)),
            static_cast<int>(std::floor(y)),
            static_cast<int>(std::ceil(x + w)),
            static_cast<int>(std::ceil(y + h)),
            tintB, tintG, tintR,
            static_cast<uint8_t>(std::clamp(tintOpacity, 0.0f, 1.0f) * 255.0f + 0.5f));
    }

    PopTemporaryClip();
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
// Opt-in true effect capture (env JALIUM_VK_EFFECT_CPU_CAPTURE, default OFF).
// Forces the CPU raster path so blur / drop-shadow / outer-glow / colour-matrix /
// emboss / custom-shader effects capture the element's TRUE rasterized content
// (matching D3D12's accuracy) instead of the GPU-path halo-splice approximation.
// Default OFF because it forces effected frames onto the (slower) CPU path; a true
// GPU offscreen render target would remove that cost but is a larger change.
static bool VulkanEffectCpuCaptureEnabled()
{
    static const bool enabled = []() {
#if defined(_WIN32)
        char* e = nullptr;
        size_t len = 0;
        if (_dupenv_s(&e, &len, "JALIUM_VK_EFFECT_CPU_CAPTURE") != 0 || e == nullptr) {
            if (e) free(e);
            return false;
        }
        const bool on = (e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N');
        free(e);
        return on;
#else
        const char* e = std::getenv("JALIUM_VK_EFFECT_CPU_CAPTURE");
        return e && e[0] != '\0' && e[0] != '0' && e[0] != 'f' && e[0] != 'F' && e[0] != 'n' && e[0] != 'N';
#endif
    }();
    return enabled;
}

void VulkanRenderTarget::BeginEffectCapture(float x, float y, float w, float h)
{
    TouchFrame();

    // Opt-in: promote to the CPU canvas up front so the capture below is a TRUE
    // capture on every frame (not just frames already on the CPU path). Default
    // OFF keeps the fast GPU-path pass-through + halo-splice (see below).
    if (VulkanEffectCpuCaptureEnabled()) {
        EnsureCpuRasterization();
    }

    // GPU replay path: a real offscreen capture isn't implemented yet, so the
    // capture sub-frame would (a) suppress the element's own GPU draws (the
    // effectCaptureStack_ guard in every TryRecordGpu*) and (b) hand the effect an
    // all-zero buffer it can't composite back — making EVERY effected element
    // render NOTHING (the amber "that ship." headline carries an OuterGlowEffect,
    // the CTA button a glow, cards a drop shadow). Treat the capture as a
    // pass-through instead: leave effectCaptureStack_ empty so the child draws
    // record straight into the frame (content stays visible), and EndEffectCapture
    // (empty stack) clears lastCapturedPixels_ so the effect itself no-ops. The
    // element's content is no longer lost. The CPU path keeps the true capture.
    // We also record where this element's content begins in the replay stream so
    // the matching OuterGlow / DropShadow effect (which runs AFTER the content per
    // the managed call order) can splice a soft halo behind it.
    if (!cpuRasterNeeded_) {
        effectContentStartStack_.push_back(static_cast<int>(gpuReplayCommands_.size()));
        if (VulkanEffectGpuRtEnabled()) {
            // Open an isolated offscreen capture region. The child element draws
            // still record into gpuReplayCommands_ (effectCaptureStack_ stays empty,
            // so the TryRecordGpu* guards do NOT suppress them); at replay they are
            // redirected into the offscreen framebuffer, and the matching effect then
            // samples that isolated image instead of the live frame.
            GpuReplayCommand mark {};
            mark.kind = GpuReplayCommandKind::OffscreenBegin;
            mark.solidRect.x = x; mark.solidRect.y = y; mark.solidRect.w = w; mark.solidRect.h = h;
            gpuReplayCommands_.push_back(mark);
        }
        return;
    }

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
        // GPU path: BeginEffectCapture recorded the content start in
        // effectContentStartStack_ (not effectCaptureStack_). Stamp the content
        // range [start, now) so the following OuterGlow / DropShadow can splice a
        // halo behind exactly this element's commands.
        if (!effectContentStartStack_.empty()) {
            pendingGlowStart_ = effectContentStartStack_.back();
            pendingGlowEnd_ = static_cast<int>(gpuReplayCommands_.size());
            effectContentStartStack_.pop_back();
        }
        if (VulkanEffectGpuRtEnabled()) {
            GpuReplayCommand mark {};
            mark.kind = GpuReplayCommandKind::OffscreenEnd;
            gpuReplayCommands_.push_back(mark);
        }
        lastCapturedPixels_.clear();
        return;
    }

    // On the GPU replay path the capture sub-frame produced NO content — the
    // child draws' CPU rasterization is gated off (cpuRasterNeeded_ == false) and
    // their GPU commands are suppressed inside the capture — so pixelBuffer_ is an
    // all-zero full-window buffer. Assigning it to lastCapturedPixels_ made every
    // downstream effect (drop shadow / outer glow / blur / colour matrix) treat it
    // as real content and upload + GPU-blur a full-window zero buffer per effect
    // per frame; that GPU work accumulated and dragged the frame to ~1 fps and
    // climbing. Leave the capture EMPTY on the GPU path so the effects' early
    // `if (lastCapturedPixels_.empty()) return;` makes them true no-ops. They were
    // already invisible on the GPU path (the capture is broken until a real
    // offscreen GPU target exists) — this removes pure waste, no visual change.
    if (cpuRasterNeeded_) {
        lastCapturedPixels_ = pixelBuffer_;
    } else {
        lastCapturedPixels_.clear();
    }
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
        // GPU replay path: no CPU capture exists (BeginEffectCapture is a
        // pass-through there, so the element already rendered SHARP into the
        // frame). Apply the blur as a GPU compositor pass that samples the
        // element's OWN live screen region and composites the blurred result
        // back — no per-element offscreen render target. radius<=0 means no blur
        // was requested, so the element is already correct on screen: nothing to do.
        if (radius > 0.0f) {
            TryRecordGpuLiveBlurCommand(x, y, w, h, radius, 1.0f);
        }
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

void VulkanRenderTarget::DrawGlowLayers(float x, float y, float w, float h, float spread,
                                        float r, float g, float b, float a, float offX, float offY,
                                        float cTL, float cTR, float cBR, float cBL)
{
    if (a <= 0.004f || spread <= 0.5f || w <= 0.0f || h <= 0.0f) return;

    // Corner radius of the shadow tracks the element's own corners. Clamp to
    // >= 0 ONLY — do NOT fall back to min(w,h)*0.5 (the old code did), which
    // turned the drop shadow of a wide-short, small/zero-corner card into a
    // full pill/stadium. This mirrors DrawInnerShadowEffect's baseRad handling
    // and D3D12, whose shadow layers use cornerTL+spread with no half-extent
    // fallback.
    float baseRad = (cTL + cTR + cBR + cBL) * 0.25f;
    if (baseRad < 0.0f) baseRad = 0.0f;

    // Equal, low per-layer alpha over many concentric layers that grow OUTWARD
    // approximates a gaussian: the layers over-blend (SrcOver) to ~a where they
    // all overlap (tight to the element) and fade smoothly at the spread edge.
    // The previous a*(1-t)²*0.5 falloff put the STRONGEST alpha on the SMALLEST
    // (tightest) layer, concentrating the shadow into a hard, card-sized offset
    // slab that read as a duplicate of the card rather than a soft shadow.
    constexpr int kLayers = 8;
    const float perLayerA = a / static_cast<float>(kLayers);
    if (perLayerA <= 0.0015f) return;
    for (int i = kLayers; i >= 1; --i) {
        const float t = static_cast<float>(i) / static_cast<float>(kLayers);  // 1 (outer) .. 1/8 (inner)
        const float grow = spread * t;
        const float layerW = w + grow * 2.0f;
        const float layerH = h + grow * 2.0f;
        if (layerW <= 0.0f || layerH <= 0.0f) continue;
        // Per-axis clamp so no layer can become a stadium even after growing.
        const float radX = std::min(baseRad + grow, layerW * 0.5f);
        const float radY = std::min(baseRad + grow, layerH * 0.5f);
        VulkanSolidBrush gb(r, g, b, perLayerA);
        FillRoundedRectangle(x + offX - grow, y + offY - grow,
                             layerW, layerH,
                             radX, radY, &gb);
    }
}

bool VulkanRenderTarget::SpliceGlowBehindContent(float x, float y, float w, float h, float spread,
                                                 float r, float g, float b, float a, float offX, float offY,
                                                 float cTL, float cTR, float cBR, float cBL)
{
    // Only valid when BeginEffectCapture/EndEffectCapture stamped a content range
    // that is still the tail of the replay stream (no commands recorded between
    // EndEffectCapture and this effect — the managed call order guarantees that).
    if (pendingGlowStart_ < 0 || pendingGlowEnd_ < 0) return false;
    const int start = pendingGlowStart_;
    const int end = pendingGlowEnd_;
    pendingGlowStart_ = -1;
    pendingGlowEnd_ = -1;
    const int n = static_cast<int>(gpuReplayCommands_.size());
    if (start < 0 || start > end || end != n) {
        return true;  // pending stale/consumed — skip the halo but DON'T fall to the CPU path
    }

    // Pull the element's content commands off the tail, draw the halo (appends
    // fresh SolidRect commands at `start`), then re-append the content so it
    // paints ON TOP of the halo — leaving only the outer halo visible.
    std::vector<GpuReplayCommand> content(gpuReplayCommands_.begin() + start, gpuReplayCommands_.end());
    gpuReplayCommands_.resize(static_cast<size_t>(start));
    DrawGlowLayers(x, y, w, h, spread, r, g, b, a, offX, offY, cTL, cTR, cBR, cBL);
    gpuReplayCommands_.insert(gpuReplayCommands_.end(), content.begin(), content.end());
    return true;
}

void VulkanRenderTarget::DrawDropShadowEffect(float x, float y, float w, float h, float blurRadius, float offsetX, float offsetY, float r, float g, float b, float a, float /*uvOffsetX*/, float /*uvOffsetY*/, float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    TouchFrame();

    // GPU path: splice a soft drop shadow behind the just-recorded element content
    // (offset by offsetX/offsetY). Falls through to the CPU capture path otherwise.
    if (SpliceGlowBehindContent(x, y, w, h, blurRadius, r, g, b, std::clamp(a, 0.0f, 1.0f),
                                offsetX, offsetY, cornerTL, cornerTR, cornerBR, cornerBL)) {
        return;
    }

    if (lastCapturedPixels_.empty()) {
        return;
    }

    // GPU replay path drives the on-screen result; record those commands always.
    if (!TryRecordGpuBlurCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x + offsetX, y + offsetY, w, h, blurRadius, 1.0f, true, r, g, b, a) ||
        !TryRecordGpuPixelBufferCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    // The CPU multi-tap gaussian below (BlurPixels) and the BlendBuffer composites
    // ONLY feed pixelBuffer_, which the GPU replay path never presents (BlendBuffer
    // already no-ops when !cpuRasterNeeded_). Running the blur on the GPU path was
    // pure waste — a page with several drop-shadow/glow effects spent seconds per
    // frame here (≈0.3 fps). Gate the whole CPU branch on cpuRasterNeeded_.
    if (cpuRasterNeeded_) {
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

        BlendBuffer(shadowPixels, width_, height_, x + offsetX, y + offsetY, w, h, 1.0f);
        BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
    }
}

void VulkanRenderTarget::DrawOuterGlowEffect(float x, float y, float w, float h,
    float glowSize, float r, float g, float b, float a, float intensity,
    float /*uvOffsetX*/, float /*uvOffsetY*/,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    TouchFrame();

    // GPU path: splice a soft, centred glow halo behind the just-recorded content.
    if (SpliceGlowBehindContent(x, y, w, h, glowSize, r, g, b,
                                std::clamp(a * intensity, 0.0f, 1.0f), 0.0f, 0.0f,
                                cornerTL, cornerTR, cornerBR, cornerBL)) {
        return;
    }

    if (lastCapturedPixels_.empty()) {
        return;
    }

    // Soft outer glow = a centred, blurred, tinted silhouette of the captured
    // element (spread outward by glowSize) with the crisp content composited on
    // top. This mirrors DrawDropShadowEffect with a zero offset; D3D12 reaches
    // the same look via 7 concentric SDF rounded-rects. Driving the glow from the
    // capture's own alpha silhouette is actually MORE faithful to the element
    // shape than the rounded-rect approximation, so the per-corner radii are
    // intentionally ignored.
    const float ga = std::clamp(a * intensity, 0.0f, 1.0f);
    const float glowRadius = std::max(0.0f, glowSize);

    if (ga > 0.0f && glowRadius > 0.0f) {
        // GPU fast path: a tinted blur of the capture (centred, no offset).
        if (!TryRecordGpuBlurCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, glowRadius, 1.0f, true, r, g, b, ga)) {
            /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
        }

        // CPU branch only — the gaussian + BlendBuffer below feed pixelBuffer_,
        // which the GPU replay path never presents (BlendBuffer no-ops when
        // !cpuRasterNeeded_). Running this on the GPU path was the dominant frame
        // cost (multi-tap blur per glow × several glows = seconds/frame).
        if (cpuRasterNeeded_) {
            const int blurRadius = std::max(1, static_cast<int>(std::round(glowRadius)));
            auto blurred = BlurPixels(lastCapturedPixels_, width_, height_, blurRadius, x, y, w, h);

            std::vector<uint8_t> glowPixels = blurred;
            const uint8_t glowB = static_cast<uint8_t>(std::clamp(b, 0.0f, 1.0f) * 255.0f + 0.5f);
            const uint8_t glowG = static_cast<uint8_t>(std::clamp(g, 0.0f, 1.0f) * 255.0f + 0.5f);
            const uint8_t glowR = static_cast<uint8_t>(std::clamp(r, 0.0f, 1.0f) * 255.0f + 0.5f);

            for (size_t offset = 0; offset + 3 < glowPixels.size(); offset += 4) {
                const uint8_t alpha = static_cast<uint8_t>(glowPixels[offset + 3] * ga);
                glowPixels[offset + 0] = glowB;
                glowPixels[offset + 1] = glowG;
                glowPixels[offset + 2] = glowR;
                glowPixels[offset + 3] = alpha;
            }
            BlendBuffer(glowPixels, width_, height_, x, y, w, h, 1.0f);
        }
    }

    // Composite the crisp captured content on top of the glow.
    if (!TryRecordGpuPixelBufferCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
}

void VulkanRenderTarget::DrawInnerShadowEffect(float x, float y, float w, float h,
    float blurRadius, float offsetX, float offsetY,
    float r, float g, float b, float a,
    float cornerTL, float cornerTR, float cornerBR, float cornerBL)
{
    TouchFrame();

    // Inner shadow paints ON TOP of the element content (already recorded on the
    // GPU path), clipped to the element bounds, as a soft dark frame hugging the
    // inner edge — the inverse of OuterGlow. Consume any pending OuterGlow /
    // DropShadow capture range first so it can't leak into the next effect (the
    // inner shadow draws over the content in place and never reorders).
    pendingGlowStart_ = -1;
    pendingGlowEnd_ = -1;
    if (a <= 0.004f || w <= 1.0f || h <= 1.0f) return;

    float baseRad = (cornerTL + cornerTR + cornerBR + cornerBL) * 0.25f;
    if (baseRad < 0.0f) baseRad = 0.0f;

    // Clip to the element's rounded rect so only the inner-edge half of each
    // (centred) stroke survives, forming the recessed shadow frame.
    PushTemporaryClip(x, y, w, h, baseRad, baseRad);

    constexpr int kLayers = 5;
    const float blur = std::max(1.0f, blurRadius);
    const float strokeW = std::max(1.5f, blur * 0.7f);
    for (int i = 1; i <= kLayers; ++i) {
        const float t = static_cast<float>(i) / static_cast<float>(kLayers);   // 0.2 .. 1
        const float inset = blur * (t - 1.0f / static_cast<float>(kLayers));    // 0 .. 0.8*blur inward
        const float la = a * (1.0f - t) * 0.65f;                               // edge strong → fades inward
        if (la <= 0.004f) continue;
        const float sx = x + offsetX + inset;
        const float sy = y + offsetY + inset;
        const float sw = w - inset * 2.0f;
        const float sh = h - inset * 2.0f;
        if (sw <= 1.0f || sh <= 1.0f) break;
        VulkanSolidBrush sb(r, g, b, la);
        DrawRoundedRectangle(sx, sy, sw, sh,
                             std::max(0.0f, baseRad - inset), std::max(0.0f, baseRad - inset),
                             &sb, strokeW);
    }

    PopTemporaryClip();
}

void VulkanRenderTarget::DrawColorMatrixEffect(float x, float y, float w, float h, const float* matrix)
{
    TouchFrame();
    if (lastCapturedPixels_.empty() || !matrix) {
        return;
    }

    // 5x4 row-major color matrix (feColorMatrix convention). Each output channel
    // = dot(row, [r,g,b,a]) + row[4] offset, on STRAIGHT (non-premultiplied)
    // RGBA in [0,1] — the standard WPF / Direct2D color-matrix input space.
    std::vector<uint8_t> out = lastCapturedPixels_;  // BGRA8, width_*height_*4
    for (size_t o = 0; o + 3 < out.size(); o += 4) {
        const float b = lastCapturedPixels_[o + 0] / 255.0f;
        const float g = lastCapturedPixels_[o + 1] / 255.0f;
        const float r = lastCapturedPixels_[o + 2] / 255.0f;
        const float a = lastCapturedPixels_[o + 3] / 255.0f;
        const float nr = matrix[0]  * r + matrix[1]  * g + matrix[2]  * b + matrix[3]  * a + matrix[4];
        const float ng = matrix[5]  * r + matrix[6]  * g + matrix[7]  * b + matrix[8]  * a + matrix[9];
        const float nb = matrix[10] * r + matrix[11] * g + matrix[12] * b + matrix[13] * a + matrix[14];
        const float na = matrix[15] * r + matrix[16] * g + matrix[17] * b + matrix[18] * a + matrix[19];
        out[o + 0] = static_cast<uint8_t>(std::clamp(nb, 0.0f, 1.0f) * 255.0f + 0.5f);
        out[o + 1] = static_cast<uint8_t>(std::clamp(ng, 0.0f, 1.0f) * 255.0f + 0.5f);
        out[o + 2] = static_cast<uint8_t>(std::clamp(nr, 0.0f, 1.0f) * 255.0f + 0.5f);
        out[o + 3] = static_cast<uint8_t>(std::clamp(na, 0.0f, 1.0f) * 255.0f + 0.5f);
    }

    if (!TryRecordGpuPixelBufferCommand(out, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(out, width_, height_, x, y, w, h, 1.0f);
}

void VulkanRenderTarget::DrawEmbossEffect(float x, float y, float w, float h,
    float amount, float lightDirX, float lightDirY, float relief)
{
    TouchFrame();
    if (lastCapturedPixels_.empty() || width_ <= 0 || height_ <= 0) {
        return;
    }

    // Directional emboss: per-pixel luminance difference between the pixel and a
    // neighbour offset along the (inverted) light direction, biased to mid-grey.
    // amount scales contrast; relief sets the sample distance in pixels.
    float len = std::sqrt(lightDirX * lightDirX + lightDirY * lightDirY);
    const float lx = (len > 1e-5f) ? (lightDirX / len) : 1.0f;
    const float ly = (len > 1e-5f) ? (lightDirY / len) : 0.0f;
    const int sampleDist = std::max(1, static_cast<int>(std::round(relief <= 0.0f ? 1.0f : relief)));
    const int dx = static_cast<int>(std::round(lx * sampleDist));
    const int dy = static_cast<int>(std::round(ly * sampleDist));
    const float amt = (amount <= 0.0f) ? 1.0f : amount;

    auto lumaAt = [&](int px, int py) -> float {
        px = std::clamp(px, 0, width_ - 1);
        py = std::clamp(py, 0, height_ - 1);
        const size_t o = (static_cast<size_t>(py) * static_cast<size_t>(width_) + static_cast<size_t>(px)) * 4u;
        return (0.114f * lastCapturedPixels_[o + 0] +
                0.587f * lastCapturedPixels_[o + 1] +
                0.299f * lastCapturedPixels_[o + 2]) / 255.0f;
    };

    std::vector<uint8_t> out = lastCapturedPixels_;
    for (int py = 0; py < height_; ++py) {
        for (int px = 0; px < width_; ++px) {
            const size_t o = (static_cast<size_t>(py) * static_cast<size_t>(width_) + static_cast<size_t>(px)) * 4u;
            const float diff = lumaAt(px, py) - lumaAt(px - dx, py - dy);
            const float v = std::clamp(0.5f + diff * amt, 0.0f, 1.0f);
            const uint8_t grey = static_cast<uint8_t>(v * 255.0f + 0.5f);
            out[o + 0] = grey;
            out[o + 1] = grey;
            out[o + 2] = grey;
            // alpha (out[o+3]) preserved from the copy
        }
    }

    if (!TryRecordGpuPixelBufferCommand(out, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }
    BlendBuffer(out, width_, height_, x, y, w, h, 1.0f);
}

void VulkanRenderTarget::DrawShaderEffect(float x, float y, float w, float h,
    const uint8_t* shaderBytecode, uint32_t shaderBytecodeSize,
    const float* constants, uint32_t constantFloatCount)
{
    // DXBC bytecode path: Vulkan can't consume DirectX bytecode, so compose the
    // captured content unmodified (DrawBlurEffect radius 0). This matches the
    // D3D12 backend's fallback when a custom-shader PSO can't be built. Effects
    // that want their shader applied on Vulkan must supply HLSL source via the
    // DrawShaderEffectFromSource path below.
    (void)shaderBytecode;
    (void)shaderBytecodeSize;
    (void)constants;
    (void)constantFloatCount;
    DrawBlurEffect(x, y, w, h, 0.0f);
}

void VulkanRenderTarget::DrawShaderEffectFromSource(float x, float y, float w, float h,
    const char* hlslSource, const float* constants, uint32_t constantFloatCount)
{
    TouchFrame();
    if (lastCapturedPixels_.empty() || !hlslSource) {
        return;
    }

    // FNV-1a hash keys the compiled-pipeline cache.
    uint64_t hash = 1469598103934665603ull;
    for (const char* p = hlslSource; *p; ++p) {
        hash ^= static_cast<uint8_t>(*p);
        hash *= 1099511628211ull;
    }

    VkPipeline pipeline = impl_->EnsureCustomShaderPipeline(hash, hlslSource);
    if (pipeline == VK_NULL_HANDLE) {
        // No DXC or compile failed → composite the captured content unmodified
        // (same as the DXBC path's fallback).
        if (!TryRecordGpuPixelBufferCommand(lastCapturedPixels_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, 1.0f)) {
            /* drop */ (void)__FUNCTION__;
        }
        BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
        return;
    }

    if (!TryRecordGpuCustomShaderCommand(x, y, w, h, hash, constants, constantFloatCount)) {
        // GPU replay inactive for this primitive — fall through to CPU.
    }

    // CPU fallback: a custom GPU shader can't run on the CPU rasterizer, so the
    // captured content is composed unmodified when the whole frame is on CPU.
    BlendBuffer(lastCapturedPixels_, width_, height_, x, y, w, h, 1.0f);
}

bool VulkanRenderTarget::TryRecordGpuCustomShaderCommand(float x, float y, float w, float h,
    uint64_t shaderHash, const float* constants, uint32_t constantFloatCount)
{
    if (!gpuReplaySupported_ || !gpuReplayHasClear_) return false;
    if (!effectCaptureStack_.empty() || activeTransitionSlot_ >= 0) return false;
    if (w <= 0.0f || h <= 0.0f || width_ <= 0 || height_ <= 0) return false;

    // Crop the captured full-frame buffer to the element rect → element-sized
    // BGRA, so the user PS sees uv 0..1 over the element (D3D12 parity: D3D12's
    // offscreen RT is element-sized).
    const int left   = std::max(0, static_cast<int>(std::floor(x)));
    const int top    = std::max(0, static_cast<int>(std::floor(y)));
    const int right  = std::min(width_, static_cast<int>(std::ceil(x + w)));
    const int bottom = std::min(height_, static_cast<int>(std::ceil(y + h)));
    if (right <= left || bottom <= top) return false;
    const int cw = right - left;
    const int ch = bottom - top;

    GpuReplayCommand cmd {};
    cmd.kind = GpuReplayCommandKind::CustomShader;
    cmd.customShader.pixelWidth = static_cast<uint32_t>(cw);
    cmd.customShader.pixelHeight = static_cast<uint32_t>(ch);
    cmd.customShader.x = static_cast<float>(left);
    cmd.customShader.y = static_cast<float>(top);
    cmd.customShader.w = static_cast<float>(cw);
    cmd.customShader.h = static_cast<float>(ch);
    cmd.customShader.shaderHash = shaderHash;
    if (constants && constantFloatCount > 0) {
        cmd.customShader.constants.assign(constants, constants + constantFloatCount);
    }
    cmd.customShader.pixels.resize(static_cast<size_t>(cw) * static_cast<size_t>(ch) * 4u);
    for (int row = 0; row < ch; ++row) {
        const uint8_t* src = lastCapturedPixels_.data() + (static_cast<size_t>(top + row) * static_cast<size_t>(width_) + static_cast<size_t>(left)) * 4u;
        uint8_t* dst = cmd.customShader.pixels.data() + static_cast<size_t>(row) * static_cast<size_t>(cw) * 4u;
        std::memcpy(dst, src, static_cast<size_t>(cw) * 4u);
    }

    if (!TryPopulateReplayClip(cmd)) return false;
    if (cmd.scissorRight <= cmd.scissorLeft || cmd.scissorBottom <= cmd.scissorTop) return true;

    gpuReplayCommands_.push_back(std::move(cmd));
    return true;
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

    PushTemporaryClip(x, y, w, h, cornerRadius, cornerRadius);

    // Always record the GPU command. liquid_glass_quad.frag.hlsl enforces a
    // non-zero alpha floor (`max(glass.a, 0.15 + tintAlpha*0.35)`), so the
    // shader still composites a visible glass/tint layer even when the GPU
    // replay path leaves pixelBuffer_ as initial zeros. PR #114 codex
    // review (P1) flagged that the previous GPU-path short-circuit dropped
    // this overlay entirely.
    if (!TryRecordGpuLiquidGlassCommand(pixelBuffer_, static_cast<uint32_t>(width_), static_cast<uint32_t>(height_), x, y, w, h, cornerRadius, blurRadius, refractionAmount, chromaticAberration, tintR, tintG, tintB, tintOpacity, lightX, lightY, highlightBoost)) {
        /* drop: skip this primitive but keep replay path */ (void)__FUNCTION__;
    }

    // CPU compositing only when we're already on the CPU rasterization path
    // (see DrawBackdropFilter for the same gating rationale).
    if (cpuRasterNeeded_) {
        auto blurred = BlurPixels(pixelBuffer_, width_, height_, std::max(1, static_cast<int>(std::round(blurRadius))), x, y, w, h);
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
    }

    PopTemporaryClip();
}

void VulkanRenderTarget::TouchFrame() const
{
    if (!isDrawing_) {
        return;
    }
}

} // namespace jalium
