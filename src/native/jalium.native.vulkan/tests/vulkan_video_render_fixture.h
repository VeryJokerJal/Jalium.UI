#pragma once

#include "jalium_video_surface.h"
#include <vulkan/vulkan.h>
#include <fcntl.h>
#include <sys/mman.h>
#include <unistd.h>

// Lavapipe accepts memfd-backed allocations through its fd import path. This
// fixture exercises the production import, YCbCr sampler, descriptor, pipeline
// and fence path with actual rendered pixels. It is deliberately limited to
// lavapipe and is NOT evidence of hardware dma-buf/VA-API interoperability.
class VideoFdTestProducer {
public:
    VkInstance instance = VK_NULL_HANDLE;
    VkPhysicalDevice physical = VK_NULL_HANDLE;
    VkDevice device = VK_NULL_HANDLE;
    int Initialize()
    {
        VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO};
        app.apiVersion = VK_API_VERSION_1_1;
        VkInstanceCreateInfo instanceInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
        instanceInfo.pApplicationInfo = &app;
        if (vkCreateInstance(&instanceInfo, nullptr, &instance) != VK_SUCCESS) return 77;
        uint32_t count = 0;
        if (vkEnumeratePhysicalDevices(instance, &count, nullptr) != VK_SUCCESS || !count) return 77;
        std::vector<VkPhysicalDevice> devices(count);
        if (vkEnumeratePhysicalDevices(instance, &count, devices.data()) != VK_SUCCESS) return 1;
        for (auto candidate : devices) {
            VkPhysicalDeviceProperties properties{};
            vkGetPhysicalDeviceProperties(candidate, &properties);
            if (properties.deviceType == VK_PHYSICAL_DEVICE_TYPE_CPU &&
                std::strstr(properties.deviceName, "llvmpipe")) { physical = candidate; break; }
        }
        if (!physical) return 77;
        VkPhysicalDeviceSamplerYcbcrConversionFeatures conversion{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SAMPLER_YCBCR_CONVERSION_FEATURES};
        VkPhysicalDeviceFeatures2 features{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_FEATURES_2};
        features.pNext = &conversion;
        vkGetPhysicalDeviceFeatures2(physical, &features);
        if (!conversion.samplerYcbcrConversion) return 77;
        uint32_t queueCount = 0;
        vkGetPhysicalDeviceQueueFamilyProperties(physical, &queueCount, nullptr);
        std::vector<VkQueueFamilyProperties> queues(queueCount);
        vkGetPhysicalDeviceQueueFamilyProperties(physical, &queueCount, queues.data());
        uint32_t family = 0;
        while (family < queueCount && !(queues[family].queueFlags & VK_QUEUE_GRAPHICS_BIT)) ++family;
        if (family == queueCount) return 77;
        const float priority = 1;
        VkDeviceQueueCreateInfo queue{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
        queue.queueFamilyIndex = family; queue.queueCount = 1; queue.pQueuePriorities = &priority;
        VkDeviceCreateInfo info{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
        info.pNext = &conversion; info.queueCreateInfoCount = 1; info.pQueueCreateInfos = &queue;
        return vkCreateDevice(physical, &info, nullptr, &device) == VK_SUCCESS ? 0 : 1;
    }
    ~VideoFdTestProducer()
    {
        if (device) vkDestroyDevice(device, nullptr);
        if (instance) vkDestroyInstance(instance, nullptr);
    }

    JaliumVideoSurface* Create(JaliumContext* context, bool p010, bool disjoint,
        uint32_t matrix, bool fullRange, uint16_t y, uint16_t cb, uint16_t cr, bool splitLuma = false)
    {
        const VkFormat format = p010 ? VK_FORMAT_G10X6_B10X6R10X6_2PLANE_420_UNORM_3PACK16
                                    : VK_FORMAT_G8_B8R8_2PLANE_420_UNORM;
        VkImageCreateInfo info{VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO};
        info.flags = disjoint ? VK_IMAGE_CREATE_DISJOINT_BIT : 0;
        info.imageType = VK_IMAGE_TYPE_2D; info.format = format;
        info.extent = {16, 16, 1}; info.mipLevels = info.arrayLayers = 1;
        info.samples = VK_SAMPLE_COUNT_1_BIT; info.tiling = VK_IMAGE_TILING_LINEAR;
        info.usage = VK_IMAGE_USAGE_SAMPLED_BIT;
        VkImage image = VK_NULL_HANDLE;
        if (!Check(vkCreateImage(device, &info, nullptr, &image) == VK_SUCCESS, "create video producer layout")) return nullptr;
        JaliumVideoSurfaceDescriptor descriptor{};
        descriptor.kind = JALIUM_VS_KIND_LINUX_DMABUF;
        descriptor.width = descriptor.height = 16;
        descriptor.plane_count = 2;
        descriptor.format_hint = p010 ? JALIUM_VS_FORMAT_P010 : JALIUM_VS_FORMAT_NV12;
        descriptor.drm_fourcc = p010 ? 0x30313050u : 0x3231564eu;
        descriptor.color_space = matrix;
        descriptor.descriptor_flags = JALIUM_VS_FLAG_IMMUTABLE_READY |
            JALIUM_VS_FLAG_CHROMA_COSITED_X | JALIUM_VS_FLAG_CHROMA_COSITED_Y |
            (fullRange ? JALIUM_VS_FLAG_FULL_RANGE : 0);
        descriptor.acquire_fence_fd = -1;
        int fds[2] = {-1, -1};
        bool ok = true;
        for (uint32_t plane = 0; plane < 2; ++plane) {
            VkImagePlaneMemoryRequirementsInfo planeInfo{VK_STRUCTURE_TYPE_IMAGE_PLANE_MEMORY_REQUIREMENTS_INFO};
            planeInfo.planeAspect = static_cast<VkImageAspectFlagBits>(VK_IMAGE_ASPECT_PLANE_0_BIT << plane);
            VkImageMemoryRequirementsInfo2 requirementsInfo{VK_STRUCTURE_TYPE_IMAGE_MEMORY_REQUIREMENTS_INFO_2};
            requirementsInfo.pNext = disjoint ? &planeInfo : nullptr;
            requirementsInfo.image = image;
            VkMemoryRequirements2 requirements{VK_STRUCTURE_TYPE_MEMORY_REQUIREMENTS_2};
            vkGetImageMemoryRequirements2(device, &requirementsInfo, &requirements);
            VkImageSubresource subresource{planeInfo.planeAspect, 0, 0};
            VkSubresourceLayout layout{};
            vkGetImageSubresourceLayout(device, image, &subresource, &layout);
            int fd = plane == 1 && !disjoint ? fds[0] : memfd_create("jalium-video-fixture", MFD_CLOEXEC);
            if (fd < 0) { ok = false; break; }
            fds[plane] = fd;
            if ((plane == 0 || disjoint) && ftruncate(fd, requirements.memoryRequirements.size) != 0) { ok = false; break; }
            void* mapped = mmap(nullptr, requirements.memoryRequirements.size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
            if (mapped == MAP_FAILED) { ok = false; break; }
            const uint32_t rows = plane == 0 ? 16 : 8;
            const uint32_t columns = plane == 0 ? 16 : 8;
            for (uint32_t row = 0; row < rows; ++row) {
                auto* bytes = static_cast<uint8_t*>(mapped) + layout.offset + row * layout.rowPitch;
                for (uint32_t col = 0; col < columns; ++col) {
                    if (p010) {
                        auto* words = reinterpret_cast<uint16_t*>(bytes);
                        if (plane == 0) words[col] = (splitLuma && col >= 8 ? (fullRange ? 1023 : 940) : y) << 6;
                        else { words[col * 2] = cb << 6; words[col * 2 + 1] = cr << 6; }
                    } else if (plane == 0) bytes[col] = splitLuma && col >= 8 ? (fullRange ? 255 : 235) : y;
                    else { bytes[col * 2] = cb; bytes[col * 2 + 1] = cr; }
                }
            }
            munmap(mapped, requirements.memoryRequirements.size);
            descriptor.planes[plane] = {fd, static_cast<uint32_t>(layout.rowPitch),
                static_cast<uint32_t>(layout.offset), 0, 0, requirements.memoryRequirements.size};
        }
        vkDestroyImage(device, image, nullptr);
        auto* surface = ok ? jalium_video_surface_wrap_external(context, &descriptor) : nullptr;
        if (fds[1] >= 0 && fds[1] != fds[0]) close(fds[1]);
        if (fds[0] >= 0) close(fds[0]);
        Check(surface != nullptr, "import the video fd fixture without a pixel staging upload");
        return surface;
    }
};

int TestVideoFdRendering(JaliumRenderTarget* target, JaliumContext* context)
{
    VideoFdTestProducer producer;
    const int status = producer.Initialize();
    if (status) {
        if (status == 77) std::cout << "SKIP: video fd fixture requires lavapipe with samplerYcbcrConversion.\n";
        return status;
    }
    for (bool p010 : {false, true}) {
        for (bool disjoint : {false, true}) {
            for (uint32_t matrix : {JALIUM_VS_COLOR_BT601, JALIUM_VS_COLOR_BT709, JALIUM_VS_COLOR_BT2020}) {
                for (bool full : {false, true}) {
                    // Neutral chroma plus black/white endpoints distinguish
                    // full/studio expansion at both 8- and 10-bit precision.
                    const uint16_t neutral = p010 ? 512 : 128;
                    const uint16_t black = full ? 0 : (p010 ? 64 : 16);
                    const uint16_t white = full ? (p010 ? 1023 : 255) : (p010 ? 940 : 235);
                    auto* dark = producer.Create(context, p010, disjoint, matrix, full, black, neutral, neutral);
                    auto* bright = producer.Create(context, p010, disjoint, matrix, full, white, neutral, neutral);
                    // Off-neutral chroma catches NV12/P010 component ordering
                    // and BT.601/709/2020 matrix mixups, which gray cannot see.
                    auto* color = producer.Create(context, p010, disjoint, matrix, full,
                        p010 ? 400 : 100, p010 ? 320 : 80, p010 ? 720 : 180);
                    if (dark && bright && color && Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin video frame")) {
                        jalium_render_target_set_full_invalidation(target);
                        jalium_render_target_clear(target, 1, 1, 1, 1);
                        jalium_render_target_draw_video_surface(target, dark, 16, 16, 64, 64, 1, 0);
                        jalium_render_target_draw_video_surface(target, bright, 96, 16, 64, 64, 1, 0);
                        JaliumEllipticalRectClip clip{};
                        clip.structSize = sizeof(clip); clip.edges = 15;
                        clip.x = 176; clip.y = 16; clip.width = clip.height = 64;
                        for (int c = 0; c < 4; ++c) clip.radiusX[c] = clip.radiusY[c] = 16;
                        jalium_push_elliptical_rect_clip(target, &clip);
                        jalium_render_target_draw_video_surface(target, dark, 176, 16, 64, 64, 0.5f, 0);
                        jalium_pop_clip(target);
                        jalium_render_target_draw_video_surface(target, color, 16, 96, 64, 64, 1, 0);
                        // Release caller references before submission. Recorded
                        // frame references must keep all descriptors alive.
                        jalium_video_surface_destroy(dark); dark = nullptr;
                        jalium_video_surface_destroy(bright); bright = nullptr;
                        jalium_video_surface_destroy(color); color = nullptr;
                        jalium_render_target_request_readback(target);
                        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit YCbCr frame");
                        const auto pixels = Capture(target, 256, 192);
                        CheckPixel(pixels, 256, 32, 32, 0, 0, 0, "YCbCr black endpoint and range are correct");
                        CheckPixel(pixels, 256, 112, 32, 255, 255, 255, "YCbCr white endpoint and range are correct");
                        CheckPixel(pixels, 256, 208, 48, 128, 128, 128, "video opacity composes correctly");
                        CheckPixel(pixels, 256, 176, 16, 255, 255, 255, "video honors the elliptical clip descriptor");
                        const double kr = matrix == JALIUM_VS_COLOR_BT601 ? .299 : matrix == JALIUM_VS_COLOR_BT709 ? .2126 : .2627;
                        const double kb = matrix == JALIUM_VS_COLOR_BT601 ? .114 : matrix == JALIUM_VS_COLOR_BT709 ? .0722 : .0593;
                        const double sampleY = p010 ? 400 : 100;
                        const double sampleCb = p010 ? 320 : 80, sampleCr = p010 ? 720 : 180;
                        const double y = full ? sampleY / (p010 ? 1023. : 255.) :
                            (sampleY - (p010 ? 64 : 16)) / (p010 ? 876. : 219.);
                        const double cb = (sampleCb - neutral) / (full ? (p010 ? 1023. : 255.) : (p010 ? 896. : 224.));
                        const double cr = (sampleCr - neutral) / (full ? (p010 ? 1023. : 255.) : (p010 ? 896. : 224.));
                        const auto channel = [](double x) { return static_cast<int>(std::lround(std::clamp(x, 0., 1.) * 255.)); };
                        CheckPixel(pixels, 256, 32, 112, channel(y + (2 - 2 * kr) * cr),
                            channel(y - (kb * (2 - 2 * kb) * cb + kr * (2 - 2 * kr) * cr) / (1 - kr - kb)),
                            channel(y + (2 - 2 * kb) * cb), "YCbCr color matrix and component order are correct");
                    }
                    if (dark) jalium_video_surface_destroy(dark);
                    if (bright) jalium_video_surface_destroy(bright);
                    if (color) jalium_video_surface_destroy(color);
                }
            }
        }
    }
    auto* pattern = producer.Create(context, false, false, JALIUM_VS_COLOR_BT709, false, 16, 128, 128, true);
    auto* bright = producer.Create(context, false, false, JALIUM_VS_COLOR_BT709, false, 235, 128, 128);
    auto* dark = producer.Create(context, false, false, JALIUM_VS_COLOR_BT709, false, 16, 128, 128);
    if (pattern && bright && dark && Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin video transform/effect frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        const float mirror[] = {-1, 0, 0, 1, 96, 0};
        jalium_push_transform(target, mirror);
        jalium_render_target_draw_video_surface(target, pattern, 16, 16, 64, 64, 1, 3);
        jalium_pop_transform(target);
        jalium_effect_begin_capture(target, 112, 16, 64, 64);
        jalium_render_target_draw_video_surface(target, bright, 112, 16, 64, 64, 1, 0);
        jalium_effect_end_capture(target);
        const float greenOnly[] = {0,0,0,0,0, 0,1,0,0,0, 0,0,0,0,0, 0,0,0,1,0};
        jalium_draw_color_matrix_effect(target, 112, 16, 64, 64, greenOnly);
        jalium_effect_begin_capture(target, 16, 104, 64, 64);
        jalium_render_target_draw_video_surface(target, dark, 24, 112, 48, 48, 1, 0);
        jalium_effect_end_capture(target);
        jalium_draw_blur_effect(target, 16, 104, 64, 64, 2, 0, 0);
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit video transform/effect frame");
        const auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 24, 48, 255, 255, 255, "video reflection reverses luma texture coordinates");
        CheckPixel(pixels, 256, 72, 48, 0, 0, 0, "video reflection preserves the opposite endpoint");
        CheckPixel(pixels, 256, 47, 48, 255, 255, 255, "nearest video scaling keeps the bright edge sharp");
        CheckPixel(pixels, 256, 48, 48, 0, 0, 0, "nearest video scaling keeps the dark edge sharp");
        CheckPixel(pixels, 256, 144, 48, 0, 255, 0, "zero-copy video participates in GPU color-matrix capture");
        CheckPixel(pixels, 256, 48, 136, 0, 0, 0, "zero-copy video participates in isolated GPU blur capture");
        if (const char* path = std::getenv("JALIUM_VK_TEST_VIDEO_CAPTURE")) {
            if (!pixels.empty()) if (FILE* file = std::fopen(path, "wb")) {
                std::fprintf(file, "P6\n256 192\n255\n");
                for (size_t i = 0; i < pixels.size(); i += 4) {
                    const uint8_t rgb[] = {pixels[i + 2], pixels[i + 1], pixels[i]};
                    std::fwrite(rgb, 1, 3, file);
                }
                std::fclose(file);
            }
        }
    }
    if (pattern) jalium_video_surface_destroy(pattern);
    if (bright) jalium_video_surface_destroy(bright);
    if (dark) jalium_video_surface_destroy(dark);
    JaliumWindowParams secondParams{};
    secondParams.title = reinterpret_cast<const JaliumUtf16Char*>(u"Vulkan video generation regression");
    secondParams.x = secondParams.y = JALIUM_DEFAULT_POS;
    secondParams.width = 256; secondParams.height = 192;
    secondParams.style = JALIUM_WINDOW_STYLE_DEFAULT;
    auto* secondWindow = jalium_window_create(&secondParams);
    JaliumRenderTarget* secondTarget = nullptr;
    if (secondWindow) {
        jalium_window_show(secondWindow); PumpEvents();
        auto secondSurface = jalium_window_get_surface(secondWindow);
        secondTarget = jalium_render_target_create_for_surface(context, &secondSurface, 256, 192);
    }
    Check(secondTarget != nullptr, "create a second independent Vulkan device generation");
    auto* foreign = secondTarget ? producer.Create(context, false, false, JALIUM_VS_COLOR_BT709, false, 16, 128, 128) : nullptr;
    if (secondTarget) jalium_render_target_destroy(secondTarget);
    if (secondWindow) jalium_window_destroy(secondWindow);
    // Closing the active import window must restore another healthy target,
    // while an older imported surface keeps its own retired device alive.
    auto* restored = producer.Create(context, false, false, JALIUM_VS_COLOR_BT709, false, 16, 128, 128);
    if (foreign && restored && Check(jalium_render_target_begin_draw(target) == JALIUM_OK, "begin cross-device video frame")) {
        jalium_render_target_set_full_invalidation(target);
        jalium_render_target_clear(target, 1, 1, 1, 1);
        jalium_render_target_draw_video_surface(target, foreign, 16, 16, 64, 64, 1, 0);
        jalium_render_target_draw_video_surface(target, restored, 112, 16, 64, 64, 1, 0);
        jalium_video_surface_destroy(foreign); foreign = nullptr;
        jalium_video_surface_destroy(restored); restored = nullptr;
        jalium_render_target_request_readback(target);
        Check(jalium_render_target_end_draw(target) == JALIUM_OK, "submit cross-device video frame");
        const auto pixels = Capture(target, 256, 192);
        CheckPixel(pixels, 256, 48, 48, 0, 0, 0, "retired foreign-device video uses the readback fallback");
        CheckPixel(pixels, 256, 144, 48, 0, 0, 0, "closing another window restores healthy video imports");
    }
    if (foreign) jalium_video_surface_destroy(foreign);
    if (restored) jalium_video_surface_destroy(restored);
    if (!failures) std::cout << "Vulkan NV12/P010 fd fixture: 24 color/layout combinations plus GPU transform/effects passed.\n";
    return failures ? 1 : 0;
}
